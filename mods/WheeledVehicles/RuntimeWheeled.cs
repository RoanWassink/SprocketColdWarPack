using HarmonyLib;
using Sprocket.ContinuousTracks;
using Sprocket.DamageModelling;
using Sprocket.Gameplay.VehicleControl;
using Sprocket.Powertrains.Engines;
using Sprocket.Powertrains.Propulsion;
using Sprocket.Powertrains.Transmissions;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Powertrains;
using Sprocket.Vehicles.Tracks;
using Sprocket.Vehicles.Weapons;
using UnityEngine;
using DriveAction = Sprocket.Vehicles.Powertrains.PowertrainBehaviour.DriveAction;

namespace SprocketWheeledVehicles;

internal static class RuntimeWheeled
{
    private sealed class BoundTrack
    {
        internal readonly TrackBehaviour Track;
        internal readonly TrackBlueprint Blueprint;
        internal readonly TrackAssembly Owner;
        internal readonly Rigidbody Body;
        internal readonly int BodyId;
        internal readonly bool IsLeft;
        internal readonly bool IsHps;
        internal readonly int GroundLayerMask;

        // Baseline snapshot of patchBeltRestHeights so HPS ride-height/pitch/roll commands
        // can be read live without any dependency on the HPS DLL.
        internal float[] HpsBaselineHeights = Array.Empty<float>();
        internal float[] HpsSampleZ = Array.Empty<float>();

        internal BoundTrack(TrackBehaviour track, TrackBlueprint blueprint, TrackAssembly owner, Rigidbody body)
        {
            Track = track;
            Blueprint = blueprint;
            Owner = owner;
            Body = body;
            BodyId = body.GetInstanceID();
            IsLeft = track.LeftSide;
            IsHps = WheeledPart.IsHpsSelected(owner);
            GroundLayerMask = track.groundSampler != null && track.groundSampler.LayerMask != 0
                ? track.groundSampler.LayerMask
                : Physics.DefaultRaycastLayers;
        }
    }

    private sealed class WheelRuntime
    {
        internal readonly Transform Transform;
        internal readonly Transform? ArmTransform;
        internal readonly Axle? DamageAxle;
        internal readonly WheelMount? DamageMount;
        internal readonly Vector3 RestLocalPos;
        internal readonly Vector3 ArmPivotLocal;
        internal readonly Vector3 ArmRestVec;
        internal readonly Quaternion BaseRelRotation;
        internal readonly Quaternion BaseArmRelRotation;
        internal readonly float Radius;
        internal readonly bool IsLeft;

        internal int AxleIndex;
        internal int SideOrderIndex;
        internal bool Grounded;
        internal bool Damaged;
        internal float WheelHealth = 1f; // Individual per-wheel health (1 = pristine, 0 = destroyed/flat)
        internal float DamageSeverity; // Smoothed visual/physical severity (0 = healthy, 1 = flat/destroyed)
        internal float Compression;
        internal float CompressionVelocity;
        internal float NormalForce;
        internal float VerticalOffset;
        internal float RideHeightOffset;
        internal float SteerAngleDeg;
        internal float SpinAngleDeg;
        internal float AngularVelocityRad;
        internal Vector3 HitPoint;
        internal Vector3 HitNormal;
        internal float GroundForwardSpeed;

        internal WheelRuntime(
            Transform tf,
            Transform? armTf,
            Axle? damageAxle,
            WheelMount? damageMount,
            Vector3 restLocalPos,
            Vector3 armPivotLocal,
            Quaternion baseRelRot,
            Quaternion baseArmRelRot,
            float radius,
            bool isLeft)
        {
            Transform = tf;
            ArmTransform = armTf;
            DamageAxle = damageAxle;
            DamageMount = damageMount;
            RestLocalPos = restLocalPos;
            ArmPivotLocal = armPivotLocal;
            ArmRestVec = armTf != null ? (restLocalPos - armPivotLocal) : Vector3.zero;
            BaseRelRotation = baseRelRot;
            BaseArmRelRotation = baseArmRelRot;
            Radius = Math.Clamp(radius, 0.15f, 1.50f);
            IsLeft = isLeft;
        }

        internal float ReadHealthFraction()
        {
            try
            {
                float h = 1f;
                if (DamageAxle != null) h = MathF.Min(h, DamageAxle.HealthFraction);
                if (DamageMount != null) h = MathF.Min(h, DamageMount.HealthFraction);
                return float.IsFinite(h) ? Math.Clamp(h, 0f, 1f) : 1f;
            }
            catch { return 1f; }
        }
    }

    private sealed class VehicleState
    {
        internal readonly int BodyId;
        internal readonly Rigidbody Body;
        internal readonly VehiclePowertrainBehaviourModule Module;
        internal readonly TrackBlueprint Blueprint;
        internal readonly BoundTrack? LeftTrack;
        internal readonly BoundTrack? RightTrack;
        internal readonly bool IsHpsCombined;
        internal readonly bool IsDedicatedWheeledPart;
        internal readonly int GroundLayerMask;
        internal readonly List<WheelRuntime> Wheels = new();
        internal readonly List<WheelGeometryInput> Geometry = new();
        internal List<AxleGroup> Axles = new();

        internal float RawSteerInput;
        internal float RawThrottleInput;
        internal float SmoothedSteer;
        internal float LastLeftNormalForce;
        internal float LastRightNormalForce;
        internal float LastForwardSpeed;
        internal float LastLeftSideHealth = 1f;
        internal float LastRightSideHealth = 1f;
        internal bool LastLeftGrounded;
        internal bool LastRightGrounded;
        internal float LastDiagTime;
        internal int VisualUpdateCount;
        internal bool LoggedVisualInit;
        internal bool Failed;

        internal VehicleState(
            VehiclePowertrainBehaviourModule module,
            BoundTrack? left,
            BoundTrack? right,
            TrackBlueprint blueprint)
        {
            Module = module;
            Body = module.body;
            BodyId = Body.GetInstanceID();
            LeftTrack = left;
            RightTrack = right;
            Blueprint = blueprint;
            IsHpsCombined = (left?.IsHps ?? false) || (right?.IsHps ?? false);
            IsDedicatedWheeledPart = WheeledPart.IsWheeledPartSelected(left?.Owner) || WheeledPart.IsWheeledPartSelected(right?.Owner);
            GroundLayerMask = left?.GroundLayerMask ?? right?.GroundLayerMask ?? Physics.DefaultRaycastLayers;
            BuildWheels();
        }

        internal void DestroyVisualRigs() { }

        private void BuildWheels()
        {
            Wheels.Clear();
            Geometry.Clear();

            AddTrackRoadwheels(LeftTrack, isLeft: true);
            AddTrackRoadwheels(RightTrack, isLeft: false);

            Axles = WheelPhysicsModel.GroupAxles(Geometry);
            int leftOrder = 0, rightOrder = 0;
            for (int a = 0; a < Axles.Count; a++)
            {
                foreach (int wi in Axles[a].WheelIndices)
                {
                    Wheels[wi].AxleIndex = a;
                    Wheels[wi].SideOrderIndex = Wheels[wi].IsLeft ? leftOrder++ : rightOrder++;
                }
            }

            if (LeftTrack != null) LeftTrack.Track.TransformRotationsEnabled = false;
            if (RightTrack != null) RightTrack.Track.TransformRotationsEnabled = false;

            Plugin.Diag(() =>
                $"VEHICLE READY bodyId={BodyId} mass={Body.mass:0}kg wheels={Wheels.Count} axles={Axles.Count} hps={IsHpsCombined} mask=0x{GroundLayerMask:X}");
            if (Plugin.DetailedLogging.Value)
            {
                var settings = WheeledProfiles.Get(Blueprint);
                for (int a = 0; a < Axles.Count; a++)
                {
                    var ax = Axles[a];
                    var cfg = settings.GetEffectiveAxleConfig(a, Axles.Count);
                    Plugin.Diag(() => $"  Axle {a}: z={ax.MeanZ:0.00}m trackWidth={ax.TrackWidth:0.00}m wheels={ax.WheelIndices.Count} driven={cfg.Driven} steer={cfg.SteerMode}");
                }
            }
        }

        private void AddTrackRoadwheels(BoundTrack? bound, bool isLeft)
        {
            if (bound == null) return;
            var track = bound.Track;
            var wheelsManaged = track.wheelsManaged;
            var transforms = track.wheelTransforms;
            var armTransforms = track.suspensionArmTransforms;
            if (wheelsManaged == null || transforms == null) return;

            int count = Math.Min(wheelsManaged.Length, transforms.Length);
            Quaternion invBodyRot = Quaternion.Inverse(Body.transform.rotation);

            // Collect roadwheel mounts/axles for per-wheel damage lookup (HealthFraction).
            var damageCandidates = new List<(Axle axle, WheelMount mount, Vector3 pos)>();
            try
            {
                var assemblies = bound.Owner.Suspensions?.Assemblies;
                if (assemblies != null)
                {
                    int mCount = assemblies.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<WheelMount>>().Count;
                    for (int m = 0; m < mCount; m++)
                    {
                        var mount = assemblies[m];
                        if (mount?.axles == null) continue;
                        for (int k = 0; k < mount.axles.Length; k++)
                        {
                            var ax = mount.axles[k]?.TryCast<Axle>();
                            if (ax != null && ax.transform != null)
                                damageCandidates.Add((ax, mount, ax.transform.position));
                        }
                    }
                }
            }
            catch { }

            for (int i = 0; i < count; i++)
            {
                var w = wheelsManaged[i];
                var tf = transforms[i];
                if (tf == null) continue;
                if ((w.flags & WheelFlags.Roadwheel) == 0)
                {
                    // Non-roadwheel (sprocket / idler / roller): ensure renderers stay hidden even after transform flattening.
                    if (WheeledProfiles.TryGet(bound.Blueprint, out var s) && s.HideEndWheels)
                        VisualHooks.SetTransformRenderersHidden(tf, true);
                    continue;
                }

                Transform? armTf = null;
                if (armTransforms != null && w.springIndex >= 0 && w.springIndex < armTransforms.Length)
                    armTf = armTransforms[w.springIndex];

                Axle? matchedAxle = null;
                WheelMount? matchedMount = null;
                float bestDistSq = 0.25f; // within 0.5m
                Vector3 wp = tf.position;
                for (int c = 0; c < damageCandidates.Count; c++)
                {
                    var cand = damageCandidates[c];
                    if (cand.axle.transform.Pointer == tf.Pointer)
                    {
                        matchedAxle = cand.axle;
                        matchedMount = cand.mount;
                        break;
                    }
                    float dSq = (cand.pos - wp).sqrMagnitude;
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        matchedAxle = cand.axle;
                        matchedMount = cand.mount;
                    }
                }

                Vector3 restLocal = Body.transform.InverseTransformPoint(tf.position);
                Vector3 armPivotLocal = armTf != null
                    ? Body.transform.InverseTransformPoint(armTf.position)
                    : restLocal;
                Quaternion baseRel = invBodyRot * tf.rotation;
                Quaternion baseArmRel = armTf != null
                    ? invBodyRot * armTf.rotation
                    : Quaternion.identity;
                float radius = w.radius > 0.05f ? w.radius : 0.45f;

                var wr = new WheelRuntime(tf, armTf, matchedAxle, matchedMount, restLocal, armPivotLocal, baseRel, baseArmRel, radius, isLeft);
                Wheels.Add(wr);
                Geometry.Add(new WheelGeometryInput(restLocal.x, restLocal.y, restLocal.z, wr.Radius, isLeft));
            }
        }
    }

    private static readonly Dictionary<IntPtr, BoundTrack> BoundTracks = new();
    private static readonly Dictionary<IntPtr, VehicleState> VehiclesByModule = new();
    private static readonly Dictionary<IntPtr, VehicleState> VehiclesByPowertrain = new();
    private static readonly HashSet<string> Warnings = new();
    private static float _lastFixedDt = 0.02f;
    private static int _playerControlledBodyId;

    internal static bool IsBoundWheeledTrack(IntPtr trackPtr) => BoundTracks.ContainsKey(trackPtr);

    /// <summary>
    /// Observes projectile impacts and explosions (read-only) to damage ONLY the specific wheel(s)
    /// near the actual impact position, instead of breaking all 4 wheels on one side at once.
    /// </summary>
    internal static void OnProjectileImpactEffect(ProjectileEffectInfo info)
    {
        if (!Plugin.Enabled.Value || VehiclesByModule.Count == 0) return;

        Vector3 hitWorld = info.Position;
        if (!IsFinite(hitWorld)) return;
        int calibreMm = Math.Max(7, (int)info.Calibre);
        bool isExplosion = (info.Type & ProjectileEffectType.Explosion) != 0;

        foreach (var vs in VehiclesByModule.Values)
        {
            if (vs.Failed || vs.Body == null) continue;
            Transform bodyTf = vs.Body.transform;

            // Fast bounding reject: skip vehicles far from this impact.
            if ((bodyTf.position - hitWorld).sqrMagnitude > 225f) continue;

            Vector3 localHit = bodyTf.InverseTransformPoint(hitWorld);

            if (!isExplosion)
            {
                // Kinetic/direct hit: damage ONLY the single closest wheel within direct hit reach.
                int bestIdx = -1;
                float bestDist = float.PositiveInfinity;
                for (int i = 0; i < vs.Wheels.Count; i++)
                {
                    var w = vs.Wheels[i];
                    Vector3 wCenter = new(w.RestLocalPos.x, w.RestLocalPos.y + w.VerticalOffset, w.RestLocalPos.z);
                    float d = (localHit - wCenter).magnitude;
                    float reach = w.Radius * 1.25f + 0.18f;
                    if (d <= reach && d < bestDist)
                    {
                        bestDist = d;
                        bestIdx = i;
                    }
                }
                if (bestIdx >= 0)
                {
                    var w = vs.Wheels[bestIdx];
                    float dmg = WheelPhysicsModel.ComputeWheelImpactDamage(bestDist, w.Radius, calibreMm, false);
                    if (dmg > 0f)
                    {
                        w.WheelHealth = Math.Clamp(w.WheelHealth - dmg, 0f, 1f);
                        Plugin.Diag(() =>
                            $"WHEEL HIT bodyId={vs.BodyId} side={(w.IsLeft ? "L" : "R")} axle={w.AxleIndex + 1} cal={calibreMm}mm dist={bestDist:0.00}m health={w.WheelHealth:0.00}");
                    }
                }
            }
            else
            {
                // Explosive blast: damage only wheels inside the localized blast radius.
                for (int i = 0; i < vs.Wheels.Count; i++)
                {
                    var w = vs.Wheels[i];
                    Vector3 wCenter = new(w.RestLocalPos.x, w.RestLocalPos.y + w.VerticalOffset, w.RestLocalPos.z);
                    float d = (localHit - wCenter).magnitude;
                    float dmg = WheelPhysicsModel.ComputeWheelImpactDamage(d, w.Radius, calibreMm, true);
                    if (dmg > 0.05f)
                    {
                        w.WheelHealth = Math.Clamp(w.WheelHealth - dmg, 0f, 1f);
                        Plugin.Diag(() =>
                            $"WHEEL BLAST bodyId={vs.BodyId} side={(w.IsLeft ? "L" : "R")} axle={w.AxleIndex + 1} cal={calibreMm}mm dist={d:0.00}m health={w.WheelHealth:0.00}");
                    }
                }
            }
        }
    }

    internal static void CapturePlayerVehicle(VehicleController controller)
    {
        try
        {
            var veh = controller?.ControlledVehicle?.TryCast<VehicleBehaviour>();
            _playerControlledBodyId = veh?.Rigidbody != null ? veh.Rigidbody.GetInstanceID() : 0;
        }
        catch { }
    }

    internal static void Warn(string area, Exception ex)
    {
        if (Warnings.Add(area))
            Plugin.ModLog.LogWarning($"[Wheeled] {area}: {ex.Message}");
    }

    internal static void BindTrack(TrackAssembly assembly, Rigidbody body, ITrackBehaviour result)
    {
        if (!Plugin.Enabled.Value || assembly == null || body == null || result == null) return;
        if (!WheeledPart.TryGetActiveSettings(assembly, out _)) return;

        var track = result.TryCast<TrackBehaviour>();
        var bp = assembly.BlueprintSlot?.Blueprint;
        if (track == null || bp == null) return;

        BoundTracks[track.Pointer] = new BoundTrack(track, bp, assembly, body);
        VisualHooks.ApplyTrackAssemblyVisibility(assembly);

        Plugin.Diag(() =>
            $"BOUND track={track.Pointer} side={(track.LeftSide ? "L" : "R")} hps={WheeledPart.IsHpsSelected(assembly)} bodyId={body.GetInstanceID()} wheels={track.wheelsManaged?.Length ?? 0}");
    }

    internal static void ReleaseTrack(IntPtr trackPtr)
    {
        if (!BoundTracks.Remove(trackPtr, out var bound)) return;

        var toRemove = new List<IntPtr>();
        foreach (var kv in VehiclesByModule)
        {
            if (kv.Value.LeftTrack == bound || kv.Value.RightTrack == bound)
                toRemove.Add(kv.Key);
        }
        foreach (var modPtr in toRemove)
        {
            if (VehiclesByModule.Remove(modPtr, out var vs))
            {
                vs.DestroyVisualRigs();
                AudioHooks.StopAndRemove(vs.BodyId);
                if (vs.Module?.powertrain != null)
                    VehiclesByPowertrain.Remove(vs.Module.powertrain.Pointer);
            }
        }
    }

    internal static void ReleaseModule(IntPtr modulePtr)
    {
        if (VehiclesByModule.Remove(modulePtr, out var vs))
        {
            vs.DestroyVisualRigs();
            AudioHooks.StopAndRemove(vs.BodyId);
            if (vs.Module?.powertrain != null)
                VehiclesByPowertrain.Remove(vs.Module.powertrain.Pointer);
        }
    }

    internal static void ClearAll()
    {
        foreach (var vs in VehiclesByModule.Values)
            vs.DestroyVisualRigs();
        AudioHooks.ClearAll();
        BoundTracks.Clear();
        VehiclesByModule.Clear();
        VehiclesByPowertrain.Clear();
        _playerControlledBodyId = 0;
    }

    private static VehicleState? ResolveVehicle(VehiclePowertrainBehaviourModule? module)
    {
        if (!Plugin.Enabled.Value || module == null || module.body == null) return null;

        if (VehiclesByModule.TryGetValue(module.Pointer, out var existing))
        {
            if (existing.Failed || existing.Body == null) return null;
            if (!WheeledProfiles.TryGet(existing.Blueprint, out var cur) || !cur.Enabled)
            {
                ReleaseModule(module.Pointer);
                return null;
            }
            return existing;
        }

        BoundTrack? left = null;
        BoundTrack? right = null;
        var leftTrack = module.leftPropulsion?.TryCast<TrackBehaviour>();
        var rightTrack = module.rightPropulsion?.TryCast<TrackBehaviour>();
        if (leftTrack != null) BoundTracks.TryGetValue(leftTrack.Pointer, out left);
        if (rightTrack != null) BoundTracks.TryGetValue(rightTrack.Pointer, out right);

        if (left == null && right == null) return null;

        var owner = left?.Owner ?? right!.Owner;
        if (!WheeledPart.TryGetActiveSettings(owner, out _))
            return null;

        var bp = left?.Blueprint ?? right!.Blueprint;
        var created = new VehicleState(module, left, right, bp);
        if (created.Wheels.Count < 2)
        {
            Plugin.ModLog.LogWarning($"[Wheeled] Vehicle bodyId={created.BodyId} has fewer than 2 roadwheels; keeping vanilla tracks.");
            created.Failed = true;
            return null;
        }

        VehiclesByModule[module.Pointer] = created;
        if (module.powertrain != null)
            VehiclesByPowertrain[module.powertrain.Pointer] = created;

        return created;
    }

    internal static void OnPowertrainSetInputsPostfix(PowertrainBehaviour powertrain)
    {
        if (!Plugin.Enabled.Value || powertrain == null) return;
        if (!VehiclesByPowertrain.TryGetValue(powertrain.Pointer, out var vs) || vs.Failed) return;

        vs.RawSteerInput = Math.Clamp(powertrain.steerTarget, -1f, 1f);
        vs.RawThrottleInput = Math.Clamp(powertrain.throttleTarget, -1f, 1f);

        // AI-controlled wheeled vehicles: Sprocket's tank AI often requests pure pivot steer
        // (steer != 0, throttle == 0) at standstill. Give a gentle creep throttle so AI wheeled
        // vehicles actually carve turns instead of steering in place forever.
        if (vs.BodyId != _playerControlledBodyId &&
            MathF.Abs(vs.RawSteerInput) > 0.25f &&
            MathF.Abs(vs.RawThrottleInput) < 0.08f &&
            MathF.Abs(vs.LastForwardSpeed) < 2.0f)
        {
            vs.RawThrottleInput = 0.35f;
            powertrain.throttleTarget = 0.35f;
        }

        powertrain.steerTarget = 0f;
        powertrain.steerInput = 0f;
    }

    internal static void OnPowertrainFixedUpdatePrefix(PowertrainBehaviour powertrain)
    {
        if (!Plugin.Enabled.Value || powertrain == null) return;
        if (!VehiclesByPowertrain.TryGetValue(powertrain.Pointer, out var vs) || vs.Failed) return;

        powertrain.steerTarget = 0f;
        powertrain.steerInput = 0f;
    }

    internal static void OnMovementRegisterFixedUpdatePrefix(VehicleMovementRegister register, float dt)
    {
        if (float.IsFinite(dt) && dt > 1e-4f && dt < 0.2f)
            _lastFixedDt = dt;

        if (!Plugin.Enabled.Value || BoundTracks.Count == 0 || register?.register == null) return;

        register.currentUpdateHandle.Complete();

        for (int i = 0; i < register.register.Count; i++)
        {
            var module = register.register[i];
            var vs = ResolveVehicle(module);
            if (vs == null) continue;

            if (vs.IsHpsCombined)
                SampleHpsRideHeightOffsets(vs);

            FeedPhysicsStateToPowertrain(vs);
        }
    }

    /// <summary>
    /// Reads live HPS ride-height, pitch and roll commands from SprocketHydropneumatic's updates
    /// to patchBeltRestHeights (baseline - current = commanded hull rise in meters).
    /// Zero hard dependency on SprocketHydropneumatic.dll.
    /// </summary>
    private static void SampleHpsRideHeightOffsets(VehicleState vs)
    {
        SampleTrackHps(vs, vs.LeftTrack, isLeft: true);
        SampleTrackHps(vs, vs.RightTrack, isLeft: false);
    }

    private static void SampleTrackHps(VehicleState vs, BoundTrack? bound, bool isLeft)
    {
        if (bound == null || !bound.IsHps) return;
        try
        {
            var info = bound.Track.UpdateInfo;
            var heights = info.patchBeltRestHeights;
            var samples = info.beltSamplePoints;
            if (!heights.IsCreated || heights.Length == 0 || samples.Length != heights.Length) return;

            if (bound.HpsBaselineHeights.Length != heights.Length)
            {
                bound.HpsBaselineHeights = new float[heights.Length];
                bound.HpsSampleZ = new float[heights.Length];
                var l2w = info.localToWorldMatrix;
                Transform bodyTf = vs.Body.transform;
                for (int i = 0; i < heights.Length; i++)
                {
                    bound.HpsBaselineHeights[i] = heights[i];
                    var o = samples[i].origin;
                    // Transform 2D belt-space sample origin (x=longitudinal, y=height, z=0) into vehicle-local Z.
                    Vector3 worldP = new(
                        l2w.c0.x * o.x + l2w.c1.x * o.y + l2w.c3.x,
                        l2w.c0.y * o.x + l2w.c1.y * o.y + l2w.c3.y,
                        l2w.c0.z * o.x + l2w.c1.z * o.y + l2w.c3.z);
                    bound.HpsSampleZ[i] = bodyTf.InverseTransformPoint(worldP).z;
                }
                Plugin.Diag(() => $"HPS-WHEELED LINKED side={(isLeft ? "L" : "R")} samples={heights.Length}");
            }

            for (int wi = 0; wi < vs.Wheels.Count; wi++)
            {
                var w = vs.Wheels[wi];
                if (w.IsLeft != isLeft) continue;
                int bestIdx = 0;
                float bestDz = float.PositiveInfinity;
                for (int s = 0; s < bound.HpsSampleZ.Length; s++)
                {
                    float dz = MathF.Abs(bound.HpsSampleZ[s] - w.RestLocalPos.z);
                    if (dz < bestDz) { bestDz = dz; bestIdx = s; }
                }
                // HPS lowers patchBeltRestHeights to raise the hull: rise = baseline - current.
                float delta = bound.HpsBaselineHeights[bestIdx] - heights[bestIdx];
                w.RideHeightOffset = float.IsFinite(delta) ? Math.Clamp(delta, -0.35f, 0.35f) : 0f;
            }
        }
        catch { }
    }

    private static void FeedPhysicsStateToPowertrain(VehicleState vs)
    {
        float massN = MathF.Max(1000f, vs.Body.mass * WheelPhysicsModel.Gravity);
        float halfDefaultN = massN * 0.5f;

        WriteTrackPhysicsState(vs.LeftTrack, vs.LastLeftGrounded ? vs.LastLeftNormalForce : halfDefaultN * 0.05f,
            vs.LastForwardSpeed, vs.LastLeftGrounded);
        WriteTrackPhysicsState(vs.RightTrack, vs.LastRightGrounded ? vs.LastRightNormalForce : halfDefaultN * 0.05f,
            vs.LastForwardSpeed, vs.LastRightGrounded);
    }

    private static void WriteTrackPhysicsState(BoundTrack? bound, float normalForce, float forwardSpeed, bool grounded)
    {
        if (bound == null) return;
        try
        {
            var arr = bound.Track.physicsState;
            if (!arr.IsCreated || arr.Length == 0) return;

            var ps = arr[0];
            ps.totalNormalForce = MathF.Max(0f, normalForce);
            ps.hullForwardVelocityAtTrack = forwardSpeed;
            ps.rollingResistance = MathF.Max(0f, normalForce * WheelPhysicsModel.RollingResistanceCoeff);
            ps.contactState = grounded ? ContactState.FullContact : ContactState.NoContact;
            arr[0] = ps;
        }
        catch { }
    }

    internal static bool OnSyncRigidbodyPrefix(VehiclePowertrainBehaviourModule module)
    {
        var vs = ResolveVehicle(module);
        if (vs == null) return true;

        try
        {
            StepWheeledVehicle(vs, _lastFixedDt);
            return false;
        }
        catch (Exception ex)
        {
            vs.Failed = true;
            Warn($"Wheel physics step disabled for bodyId={vs.BodyId}", ex);
            return true;
        }
    }

    private static void StepWheeledVehicle(VehicleState vs, float dt)
    {
        var body = vs.Body;
        var settings = WheeledProfiles.Get(vs.Blueprint);
        if (!settings.Enabled || !settings.IsValid()) return;

        Transform bodyTf = body.transform;
        Vector3 bodyForward = bodyTf.forward;
        Vector3 bodyUp = bodyTf.up;
        Vector3 bodyVel = body.linearVelocity;
        Vector3 worldCom = body.worldCenterOfMass;
        float forwardSpeed = Vector3.Dot(bodyVel, bodyForward);
        vs.LastForwardSpeed = forwardSpeed;

        // Responsive steering: brisk turn-in (~0.28s full lock), fast return-to-center.
        float steerRate = MathF.Abs(vs.RawSteerInput) > 0.05f ? 3.6f : 5.8f;
        vs.SmoothedSteer = MoveTowards(vs.SmoothedSteer, vs.RawSteerInput, steerRate * dt);

        // Compute multi-axle Ackermann + speed-dependent rear-wheel steering angles.
        var steerAngles = WheelPhysicsModel.ComputeWheelSteerAnglesDeg(
            vs.Geometry, vs.Axles, settings, vs.SmoothedSteer, forwardSpeed);
        for (int i = 0; i < vs.Wheels.Count; i++)
            vs.Wheels[i].SteerAngleDeg = steerAngles[i];

        // 1. Raycast & independent vertical suspension for every wheel (100% decoupled from tank armLength).
        float massKg = MathF.Max(500f, body.mass);
        float baseUpTravel = Math.Clamp(settings.UpTravelMeters, WheeledSettings.MinUpTravel, WheeledSettings.MaxUpTravel);
        float baseDownTravel = Math.Clamp(settings.DownTravelMeters, WheeledSettings.MinDownTravel, WheeledSettings.MaxDownTravel);
        int totalWheels = vs.Wheels.Count;
        int groundedCount = 0;
        int damagedCount = 0;
        float leftNormalSum = 0f, rightNormalSum = 0f;
        bool leftGrounded = false, rightGrounded = false;

        // Track crew repairs: when Sprocket's native side health recovers upward significantly (e.g. after crew repair),
        // restore damaged wheels on that side. Individual hits are tracked per wheel via OnProjectileImpactEffect.
        float leftHealth = ReadSideHealth(vs.LeftTrack);
        float rightHealth = ReadSideHealth(vs.RightTrack);
        if (leftHealth > vs.LastLeftSideHealth + 0.15f && leftHealth >= 0.85f)
        {
            for (int i = 0; i < totalWheels; i++)
                if (vs.Wheels[i].IsLeft) vs.Wheels[i].WheelHealth = 1f;
            Plugin.Diag(() => $"WHEELS REPAIRED bodyId={vs.BodyId} side=L");
        }
        if (rightHealth > vs.LastRightSideHealth + 0.15f && rightHealth >= 0.85f)
        {
            for (int i = 0; i < totalWheels; i++)
                if (!vs.Wheels[i].IsLeft) vs.Wheels[i].WheelHealth = 1f;
            Plugin.Diag(() => $"WHEELS REPAIRED bodyId={vs.BodyId} side=R");
        }
        vs.LastLeftSideHealth = leftHealth;
        vs.LastRightSideHealth = rightHealth;

        for (int i = 0; i < totalWheels; i++)
        {
            var w = vs.Wheels[i];
            float targetSeverity = 1f - Math.Clamp(w.WheelHealth, 0f, 1f);
            bool nowDamaged = w.WheelHealth <= 0.35f;
            w.Damaged = nowDamaged;
            w.DamageSeverity = MoveTowards(w.DamageSeverity, targetSeverity, 3.5f * dt);
            if (w.Damaged) damagedCount++;
            float effectiveRadius = w.Radius * (1f - 0.18f * w.DamageSeverity);

            Vector3 topLocal = new(w.RestLocalPos.x, w.RestLocalPos.y + baseUpTravel, w.RestLocalPos.z);
            Vector3 rayOrigin = bodyTf.TransformPoint(topLocal);
            Vector3 rayDir = -bodyUp;
            float effectiveDroop = MathF.Max(0.06f, baseDownTravel + w.RideHeightOffset);
            float maxRayLen = baseUpTravel + effectiveDroop + effectiveRadius;

            if (TryWheelRaycast(rayOrigin, rayDir, maxRayLen, vs.GroundLayerMask, body, out var hit))
            {
                var susp = WheelPhysicsModel.ComputeSuspension(
                    massKg, totalWheels, effectiveRadius, baseUpTravel, baseDownTravel,
                    hit.distance, w.Compression, w.Grounded,
                    settings.SuspensionStiffness, settings.SuspensionDamping, dt,
                    w.RideHeightOffset);

                w.Grounded = true;
                w.Compression = susp.Compression;
                w.CompressionVelocity = susp.CompressionVelocity;
                w.NormalForce = susp.NormalForce;
                w.VerticalOffset = Math.Clamp(susp.VerticalWheelOffset, -effectiveDroop, baseUpTravel);
                w.HitPoint = hit.point;
                w.HitNormal = hit.normal;
                groundedCount++;

                if (w.IsLeft) { leftNormalSum += w.NormalForce; leftGrounded = true; }
                else { rightNormalSum += w.NormalForce; rightGrounded = true; }
            }
            else
            {
                w.Grounded = false;
                w.Compression = 0f;
                w.CompressionVelocity = 0f;
                w.NormalForce = 0f;
                w.VerticalOffset = MoveTowards(w.VerticalOffset, -effectiveDroop, 1.8f * dt);
            }
        }

        // 2. Anti-roll bar stabilization per axle.
        for (int a = 0; a < vs.Axles.Count; a++)
        {
            var ax = vs.Axles[a];
            WheelRuntime? leftW = null, rightW = null;
            foreach (int wi in ax.WheelIndices)
            {
                if (vs.Wheels[wi].IsLeft) leftW = vs.Wheels[wi];
                else rightW = vs.Wheels[wi];
            }
            if (leftW != null && rightW != null && (leftW.Grounded || rightW.Grounded))
            {
                float rollF = WheelPhysicsModel.ComputeAntiRollForce(
                    leftW.Compression, rightW.Compression, massKg, totalWheels, baseDownTravel, settings.SuspensionStiffness);
                if (leftW.Grounded) leftW.NormalForce = MathF.Max(0f, leftW.NormalForce + rollF);
                if (rightW.Grounded) rightW.NormalForce = MathF.Max(0f, rightW.NormalForce - rollF);
            }
        }

        // 3. Read drive force & braking state from Sprocket's real engine + transmission.
        ReadPowertrainDemand(vs, forwardSpeed, out float totalDriveForce, out float totalBrakeForce, out bool parkingBrake);

        float drivenNormalSum = 0f;
        int drivenGroundedCount = 0;
        var wheelDriven = new bool[totalWheels];
        for (int i = 0; i < totalWheels; i++)
        {
            var cfg = settings.GetEffectiveAxleConfig(vs.Wheels[i].AxleIndex, vs.Axles.Count);
            wheelDriven[i] = cfg.Driven;
            if (cfg.Driven && vs.Wheels[i].Grounded && vs.Wheels[i].NormalForce > 1f)
            {
                drivenNormalSum += vs.Wheels[i].NormalForce;
                drivenGroundedCount++;
            }
        }

        float wheelShareMass = MathF.Max(150f, massKg / MathF.Max(2, totalWheels));
        float brakePerWheel = groundedCount > 0 ? totalBrakeForce / groundedCount : 0f;
        float maxSlipIntensity = 0f;
        int totalDrivenWheels = 0;
        int damagedDrivenWheels = 0;

        // 4. Apply suspension, drive, brake and lateral tire grip forces per wheel.
        for (int i = 0; i < totalWheels; i++)
        {
            var w = vs.Wheels[i];
            if (wheelDriven[i])
            {
                totalDrivenWheels++;
                if (w.Damaged) damagedDrivenWheels++;
            }
            if (!w.Grounded || w.NormalForce <= 1f)
            {
                w.AngularVelocityRad = MoveTowards(w.AngularVelocityRad, 0f, 3f * dt);
                w.SpinAngleDeg = WrapDeg(w.SpinAngleDeg + w.AngularVelocityRad * WheelPhysicsModel.RadToDeg * dt);
                continue;
            }

            Quaternion steerYaw = Quaternion.AngleAxis(w.SteerAngleDeg, bodyUp);
            Vector3 steeredForward = steerYaw * bodyForward;
            Vector3 wheelForward = Vector3.ProjectOnPlane(steeredForward, w.HitNormal);
            if (wheelForward.sqrMagnitude < 1e-4f) wheelForward = steeredForward;
            wheelForward.Normalize();
            Vector3 wheelRight = Vector3.Cross(w.HitNormal, wheelForward);
            if (wheelRight.sqrMagnitude < 1e-4f) wheelRight = bodyTf.right;
            wheelRight.Normalize();

            Vector3 pointVel = body.GetPointVelocity(w.HitPoint);
            float vLong = Vector3.Dot(pointVel, wheelForward);
            float vLat = Vector3.Dot(pointVel, wheelRight);
            w.GroundForwardSpeed = vLong;

            float wheelDriveForce = 0f;
            if (wheelDriven[i] && drivenGroundedCount > 0 && drivenNormalSum > 1f)
            {
                float equalShare = totalDriveForce / drivenGroundedCount;
                float loadShare = totalDriveForce * (w.NormalForce / drivenNormalSum);
                wheelDriveForce = 0.5f * (equalShare + loadShare);
                if (w.Damaged) wheelDriveForce *= 0.30f; // Punctured/damaged run-flat wheel delivers reduced torque
            }

            // Extra rolling drag on a flat/damaged wheel.
            if (w.Damaged && MathF.Abs(vLong) > 0.1f)
                wheelDriveForce -= MathF.Sign(vLong) * (0.10f * w.NormalForce);

            float effectiveGrip = w.Damaged ? MathF.Max(WheeledSettings.MinGrip, settings.TireGrip * 0.45f) : settings.TireGrip;
            float effectiveBrake = w.Damaged ? brakePerWheel * 0.35f : brakePerWheel;

            var tire = WheelPhysicsModel.ComputeTireForces(
                w.NormalForce, wheelShareMass, vLong, vLat,
                wheelDriveForce, effectiveBrake, parkingBrake,
                effectiveGrip, dt);

            float slipNow = Math.Clamp(MathF.Abs(vLat) / 4.5f + (tire.Saturated ? 0.35f : 0f), 0f, 1f);
            if (slipNow > maxSlipIntensity) maxSlipIntensity = slipNow;

            // Vertical suspension force at contact point.
            Vector3 suspForceVec = w.HitNormal * w.NormalForce;
            if (IsFinite(suspForceVec) && IsFinite(w.HitPoint))
                body.AddForceAtPosition(suspForceVec, w.HitPoint, ForceMode.Force);

            // Wheel hub world position (anti-squat longitudinal application point).
            Vector3 wheelHubWorld = bodyTf.TransformPoint(new Vector3(w.RestLocalPos.x, w.RestLocalPos.y + w.VerticalOffset, w.RestLocalPos.z));
            // Roll-center compensated lateral application point: stays on the wheel's (x,z) column
            // at ~72% height from wheel hub toward vehicle CoM so tall turrets lean realistically without flipping.
            float hubToComUp = Vector3.Dot(worldCom - wheelHubWorld, bodyUp);
            Vector3 latAppPoint = wheelHubWorld + bodyUp * (Math.Clamp(hubToComUp, 0f, 1.8f) * 0.72f);

            Vector3 longForceVec = wheelForward * tire.LongitudinalForce;
            Vector3 latForceVec = wheelRight * tire.LateralForce;
            if (IsFinite(longForceVec) && IsFinite(wheelHubWorld))
                body.AddForceAtPosition(longForceVec, wheelHubWorld, ForceMode.Force);
            if (IsFinite(latForceVec) && IsFinite(latAppPoint))
                body.AddForceAtPosition(latForceVec, latAppPoint, ForceMode.Force);

            // True ground rolling speed -> visual wheel spin (locks on parking brake).
            float targetOmega = parkingBrake ? 0f : (vLong / w.Radius);
            w.AngularVelocityRad = targetOmega;
            w.SpinAngleDeg = WrapDeg(w.SpinAngleDeg + w.AngularVelocityRad * WheelPhysicsModel.RadToDeg * dt);
        }

        vs.LastLeftNormalForce = leftNormalSum;
        vs.LastRightNormalForce = rightNormalSum;
        vs.LastLeftGrounded = leftGrounded;
        vs.LastRightGrounded = rightGrounded;
        vs.Module.ForwardVelocity = forwardSpeed;
        vs.Module.fullContact = groundedCount > 0;
        // Multi-wheel vehicles keep limping on run-flats until >75% of driven wheels are knocked out.
        vs.Module.immobilized = totalDrivenWheels > 0 && damagedDrivenWheels * 4 > totalDrivenWheels * 3;

        float groundedFrac = totalWheels > 0 ? (groundedCount / (float)totalWheels) : 0f;
        AudioHooks.UpdateTireAudio(vs.BodyId, body, forwardSpeed, groundedFrac, maxSlipIntensity, dt);

        if (MathF.Abs(vs.RawThrottleInput) > 0.02f || MathF.Abs(vs.RawSteerInput) > 0.02f)
            body.WakeUp();

        if (Plugin.DetailedLogging.Value && Time.unscaledTime - vs.LastDiagTime >= 1.0f &&
            (MathF.Abs(forwardSpeed) > 0.1f || MathF.Abs(vs.RawThrottleInput) > 0.05f || MathF.Abs(vs.RawSteerInput) > 0.05f))
        {
            vs.LastDiagTime = Time.unscaledTime;
            var pt = vs.Module.powertrain;
            int frontIdx = vs.Axles.Count > 0 && vs.Axles[0].WheelIndices.Count > 0 ? vs.Axles[0].WheelIndices[0] : 0;
            Plugin.Diag(() =>
                $"STEP bodyId={vs.BodyId} spd={forwardSpeed * 3.6f:0.0}km/h thr={vs.RawThrottleInput:0.00} str={vs.RawSteerInput:0.00} " +
                $"steerFront={vs.Wheels[frontIdx].SteerAngleDeg:0.0}° spinFront={vs.Wheels[frontIdx].SpinAngleDeg:0}° vis={vs.VisualUpdateCount} " +
                $"act={pt?.action} rpm={pt?.EngineRPM:0} driveF={totalDriveForce:0}N grounded={groundedCount}/{totalWheels}");
        }
    }

    private static void ReadPowertrainDemand(
        VehicleState vs,
        float forwardSpeed,
        out float totalDriveForce,
        out float totalBrakeForce,
        out bool parkingBrake)
    {
        totalDriveForce = 0f;
        totalBrakeForce = 0f;
        parkingBrake = false;

        var pt = vs.Module.powertrain;
        if (pt == null || !pt.Functioning)
        {
            parkingBrake = MathF.Abs(forwardSpeed) < 0.35f;
            return;
        }

        var action = pt.action;
        float throttle = vs.RawThrottleInput;
        float massKg = MathF.Max(500f, vs.Body.mass);

        bool reversingIntoForward = throttle < -0.05f && forwardSpeed > 0.45f;
        bool acceleratingIntoReverse = throttle > 0.05f && forwardSpeed < -0.45f;
        if (action == DriveAction.Stopped && MathF.Abs(throttle) < 0.05f && MathF.Abs(forwardSpeed) < 0.8f)
        {
            parkingBrake = true;
            return;
        }
        if (action == DriveAction.Braking || reversingIntoForward || acceleratingIntoReverse)
        {
            totalBrakeForce = massKg * WheelPhysicsModel.Gravity * 0.80f;
            return;
        }
        if (action == DriveAction.Coasting || action == DriveAction.Shifting || MathF.Abs(throttle) < 0.02f)
        {
            if (MathF.Abs(forwardSpeed) < 0.15f && MathF.Abs(throttle) < 0.02f)
                parkingBrake = true;
            return;
        }

        float leftTraction = ReadTrackTraction(vs.LeftTrack);
        float rightTraction = ReadTrackTraction(vs.RightTrack);
        float sumTraction = leftTraction + rightTraction;

        if (MathF.Abs(leftTraction) > 50f && MathF.Abs(rightTraction) > 50f &&
            MathF.Abs(sumTraction) < 0.25f * (MathF.Abs(leftTraction) + MathF.Abs(rightTraction)))
        {
            sumTraction = MathF.Sign(throttle) * (MathF.Abs(leftTraction) + MathF.Abs(rightTraction));
        }

        bool nativeSideDamaged = vs.LastLeftSideHealth < 0.90f || vs.LastRightSideHealth < 0.90f;
        if (nativeSideDamaged)
        {
            float gearForce = ComputeEngineTransmissionForce(vs, pt, throttle);
            if (MathF.Abs(sumTraction) < MathF.Abs(gearForce) * 0.45f && MathF.Abs(gearForce) > 100f)
            {
                totalDriveForce = gearForce;
                return;
            }
        }

        if (MathF.Abs(sumTraction) > 1f)
        {
            totalDriveForce = MathF.Sign(throttle) * MathF.Abs(sumTraction);
            float cap = massKg * WheelPhysicsModel.Gravity * 0.85f;
            totalDriveForce = Math.Clamp(totalDriveForce, -cap, cap);
        }
        else
        {
            totalDriveForce = ComputeEngineTransmissionForce(vs, pt, throttle);
        }
    }

    private static float ReadSideHealth(BoundTrack? bound)
    {
        if (bound == null) return 1f;
        try
        {
            float h = bound.Owner.HealthFraction;
            if (bound.Track.Broken) h = 0f;
            return float.IsFinite(h) ? Math.Clamp(h, 0f, 1f) : 1f;
        }
        catch { return 1f; }
    }

    private static float ReadTrackTraction(BoundTrack? bound)
    {
        if (bound == null) return 0f;
        try
        {
            var arr = bound.Track.powertrainState;
            if (arr.IsCreated && arr.Length > 0)
            {
                float f = arr[0].beltTractionForce;
                return float.IsFinite(f) ? f : 0f;
            }
        }
        catch { }
        return 0f;
    }

    private static float ComputeEngineTransmissionForce(VehicleState vs, PowertrainBehaviour pt, float throttle)
    {
        try
        {
            var engine = pt.Engine;
            var transmissions = pt.Transmissions;
            if (engine == null || transmissions == null || transmissions.Length == 0) return 0f;

            var engJob = engine.UpdateJob;
            if (!engJob.outputStateReference.IsCreated || engJob.outputStateReference.Length == 0) return 0f;
            float engineTorque = engJob.outputStateReference[0].OutputTorque;
            if (!float.IsFinite(engineTorque) || MathF.Abs(engineTorque) < 1f)
                engineTorque = engJob.maxTorque * MathF.Abs(throttle) * 0.65f;

            float ratioSum = 0f;
            int activeTrans = 0;
            for (int i = 0; i < transmissions.Length; i++)
            {
                var tr = transmissions[i];
                if (tr == null) continue;
                var trJob = tr.UpdateJob;
                if (!trJob.externalState.IsCreated || trJob.externalState.Length == 0) continue;
                var st = trJob.externalState[0];
                if (float.IsFinite(st.Ratio) && MathF.Abs(st.Ratio) > 0.01f)
                {
                    ratioSum += MathF.Abs(st.Ratio) * Math.Clamp(st.Engage, 0.25f, 1f);
                    activeTrans++;
                }
            }
            if (activeTrans == 0) return 0f;

            float avgGearRatio = ratioSum / activeTrans;
            float finalDrive = vs.Blueprint.FinalDriveRatio > 0.05f ? vs.Blueprint.FinalDriveRatio : 1.0f;
            float avgRadius = 0.45f;
            if (vs.Wheels.Count > 0)
            {
                float rSum = 0f;
                for (int i = 0; i < vs.Wheels.Count; i++) rSum += vs.Wheels[i].Radius;
                avgRadius = MathF.Max(0.15f, rSum / vs.Wheels.Count);
            }

            float force = MathF.Abs(engineTorque) * avgGearRatio * finalDrive * 0.86f / avgRadius;
            float maxSafe = vs.Body.mass * WheelPhysicsModel.Gravity * 0.85f;
            return MathF.Sign(throttle) * Math.Clamp(force, 0f, maxSafe);
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>
    /// Runs every render frame right after VehiclePowertrainBehaviourModule.Update()
    /// (which is directly called by VehiclesGamePlugin.GameUpdate and runs TrackBehaviour.Update).
    /// Applies true visual steering yaw, rolling spin and suspension travel to every roadwheel Transform.
    /// </summary>
    internal static void OnModuleUpdatePostfix(VehiclePowertrainBehaviourModule module)
    {
        if (!Plugin.Enabled.Value || module == null) return;
        if (!VehiclesByModule.TryGetValue(module.Pointer, out var vs)) return;
        if (vs.Failed || vs.Body == null) return;

        try
        {
            vs.VisualUpdateCount++;
            Transform bodyTf = vs.Body.transform;
            Quaternion bodyRot = bodyTf.rotation;

            if (!vs.LoggedVisualInit && vs.Wheels.Count > 0)
            {
                vs.LoggedVisualInit = true;
                // Re-apply end-wheel/housing visibility once on first rendered frame (after spawn LOD/flattening).
                if (vs.LeftTrack != null) VisualHooks.ApplyTrackAssemblyVisibility(vs.LeftTrack.Owner);
                if (vs.RightTrack != null) VisualHooks.ApplyTrackAssemblyVisibility(vs.RightTrack.Owner);

                var w0 = vs.Wheels[0];
                int rendCount = w0.Transform != null ? (w0.Transform.GetComponentsInChildren<Renderer>(true)?.Length ?? 0) : -1;
                Plugin.Diag(() =>
                    $"VISUAL READY bodyId={vs.BodyId} dedicated={vs.IsDedicatedWheeledPart} wheel0='{w0.Transform?.name}' parent='{w0.Transform?.parent?.name}' renderers={rendCount}");
            }

            var settings = WheeledProfiles.Get(vs.Blueprint);
            float baseUp = Math.Clamp(settings.UpTravelMeters, WheeledSettings.MinUpTravel, WheeledSettings.MaxUpTravel);
            float baseDown = Math.Clamp(settings.DownTravelMeters, WheeledSettings.MinDownTravel, WheeledSettings.MaxDownTravel);

            for (int i = 0; i < vs.Wheels.Count; i++)
            {
                var w = vs.Wheels[i];
                if (w.Transform == null) continue;

                float maxDown = MathF.Max(0.06f, baseDown + MathF.Max(0f, w.RideHeightOffset));
                float maxUp = MathF.Max(0.08f, baseUp + MathF.Max(0f, -w.RideHeightOffset));
                float clampedOffset = Math.Clamp(w.VerticalOffset, -maxDown, maxUp);

                // Keep the Christie / HPS swing arm attached to the wheel hub while keeping
                // steering and suspension physics 100% decoupled from armLength.
                float armLenSq = w.ArmRestVec.y * w.ArmRestVec.y + w.ArmRestVec.z * w.ArmRestVec.z;
                Vector3 localPos;
                if (w.ArmTransform != null && armLenSq > 0.01f)
                {
                    var (clampedY, arcZ) = WheelPhysicsModel.SolveSwingArmArc(
                        w.ArmRestVec.y, w.ArmRestVec.z, clampedOffset, maxDown, maxUp);
                    localPos = new Vector3(
                        w.RestLocalPos.x,
                        w.ArmPivotLocal.y + clampedY,
                        w.ArmPivotLocal.z + arcZ);

                    Vector3 restDirYZ = new Vector3(0f, w.ArmRestVec.y, w.ArmRestVec.z);
                    Vector3 curDirYZ = new Vector3(0f, clampedY, arcZ);
                    if (restDirYZ.sqrMagnitude > 1e-4f && curDirYZ.sqrMagnitude > 1e-4f)
                    {
                        Quaternion armDelta = Quaternion.FromToRotation(restDirYZ.normalized, curDirYZ.normalized);
                        w.ArmTransform.rotation = bodyRot * armDelta * w.BaseArmRelRotation;
                    }
                }
                else
                {
                    localPos = new Vector3(w.RestLocalPos.x, w.RestLocalPos.y + clampedOffset, w.RestLocalPos.z);
                }

                w.Transform.position = bodyTf.TransformPoint(localPos);

                Quaternion steerRot = Quaternion.AngleAxis(w.SteerAngleDeg, Vector3.up);
                Quaternion spinRot = Quaternion.AngleAxis(w.SpinAngleDeg, Vector3.right);
                // Visibly tilt and slightly wobble a punctured/damaged wheel (camber + wobble) so battle damage reads at a glance.
                Quaternion damageTilt = Quaternion.identity;
                if (w.DamageSeverity > 0.01f)
                {
                    float camberSign = w.IsLeft ? 1f : -1f;
                    float camberDeg = camberSign * 13f * w.DamageSeverity;
                    float wobbleDeg = MathF.Sin(w.SpinAngleDeg * WheelPhysicsModel.DegToRad) * 5.5f * w.DamageSeverity;
                    damageTilt = Quaternion.AngleAxis(camberDeg + wobbleDeg, Vector3.forward);
                }
                w.Transform.rotation = bodyRot * steerRot * damageTilt * spinRot * w.BaseRelRotation;
            }
        }
        catch (Exception ex)
        {
            Warn($"Visual wheel update bodyId={vs.BodyId}", ex);
        }
    }

    private static bool TryWheelRaycast(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        int layerMask,
        Rigidbody ownBody,
        out RaycastHit bestHit)
    {
        bestHit = default;
        var hits = Physics.RaycastAll(origin, direction, maxDistance, layerMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return false;

        float bestDist = float.PositiveInfinity;
        bool found = false;
        Transform ownRoot = ownBody.transform;

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.distance <= 0.001f || h.distance >= bestDist) continue;
            var col = h.collider;
            if (col == null) continue;
            if (col.attachedRigidbody != null && col.attachedRigidbody.GetInstanceID() == ownBody.GetInstanceID())
                continue;
            if (col.transform.IsChildOf(ownRoot))
                continue;

            bestDist = h.distance;
            bestHit = h;
            found = true;
        }
        return found;
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta) return target;
        return current + MathF.Sign(target - current) * maxDelta;
    }

    private static float WrapDeg(float angle)
    {
        angle %= 360f;
        if (angle < 0f) angle += 360f;
        return angle;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
}

[HarmonyPatch]
internal static class RuntimeHooks
{
    [HarmonyPostfix, HarmonyPatch(typeof(TrackAssembly), nameof(TrackAssembly.CreateTrackController))]
    private static void CreateTrackControllerPostfix(TrackAssembly __instance, Rigidbody __0, ITrackBehaviour __result)
    {
        try { RuntimeWheeled.BindTrack(__instance, __0, __result); }
        catch (Exception ex) { RuntimeWheeled.Warn("BindTrack", ex); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TrackBehaviour), nameof(TrackBehaviour.Release))]
    private static void TrackReleasePostfix(TrackBehaviour __instance)
    {
        try { RuntimeWheeled.ReleaseTrack(__instance.Pointer); }
        catch { }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehiclePowertrainBehaviourModule), nameof(VehiclePowertrainBehaviourModule.Release))]
    private static void ModuleReleasePostfix(VehiclePowertrainBehaviourModule __instance)
    {
        try { RuntimeWheeled.ReleaseModule(__instance.Pointer); }
        catch { }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleMovementRegister), nameof(VehicleMovementRegister.Dispose))]
    private static void RegisterDisposePostfix()
    {
        try { RuntimeWheeled.ClearAll(); }
        catch { }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(PowertrainBehaviour), nameof(PowertrainBehaviour.SetInputs))]
    private static void PowertrainSetInputsPostfix(PowertrainBehaviour __instance)
    {
        try { RuntimeWheeled.OnPowertrainSetInputsPostfix(__instance); }
        catch (Exception ex) { RuntimeWheeled.Warn("Powertrain.SetInputs", ex); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PowertrainBehaviour), nameof(PowertrainBehaviour.FixedUpdate))]
    private static void PowertrainFixedUpdatePrefix(PowertrainBehaviour __instance)
    {
        try { RuntimeWheeled.OnPowertrainFixedUpdatePrefix(__instance); }
        catch (Exception ex) { RuntimeWheeled.Warn("Powertrain.FixedUpdate", ex); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(VehicleMovementRegister), nameof(VehicleMovementRegister.FixedUpdate))]
    private static void MovementRegisterFixedUpdatePrefix(VehicleMovementRegister __instance, float __0)
    {
        try { RuntimeWheeled.OnMovementRegisterFixedUpdatePrefix(__instance, __0); }
        catch (Exception ex) { RuntimeWheeled.Warn("MovementRegister.FixedUpdate", ex); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(VehiclePowertrainBehaviourModule), nameof(VehiclePowertrainBehaviourModule.SyncRigidbody))]
    private static bool SyncRigidbodyPrefix(VehiclePowertrainBehaviourModule __instance)
    {
        try { return RuntimeWheeled.OnSyncRigidbodyPrefix(__instance); }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("SyncRigidbody", ex);
            return true;
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehiclePowertrainBehaviourModule), nameof(VehiclePowertrainBehaviourModule.Update))]
    private static void ModuleUpdatePostfix(VehiclePowertrainBehaviourModule __instance)
    {
        try { RuntimeWheeled.OnModuleUpdatePostfix(__instance); }
        catch (Exception ex) { RuntimeWheeled.Warn("Module.Update", ex); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(TrackBehaviour), nameof(TrackBehaviour.SetBroken))]
    private static bool TrackSetBrokenPrefix(TrackBehaviour __instance)
    {
        // Wheeled vehicles have no track belt to snap; individual wheel damage is handled per wheel.
        if (!Plugin.Enabled.Value || __instance == null) return true;
        return !RuntimeWheeled.IsBoundWheeledTrack(__instance.Pointer);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleController), nameof(VehicleController.UpdateControl))]
    private static void VehicleControllerUpdateControlPostfix(VehicleController __instance)
    {
        try { RuntimeWheeled.CapturePlayerVehicle(__instance); }
        catch { }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ProjectileImpactEffect), nameof(ProjectileImpactEffect.PlayEffect))]
    private static void ProjectileImpactEffectPostfix(ProjectileEffectInfo __0)
    {
        try { RuntimeWheeled.OnProjectileImpactEffect(__0); }
        catch (Exception ex) { RuntimeWheeled.Warn("ProjectileImpactEffect", ex); }
    }
}
