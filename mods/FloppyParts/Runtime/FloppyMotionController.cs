using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Engines;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Exhausts;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.Powertrains.Layouts;
using Sprocket.Vehicles.Tracks;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons.Cannons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace SprocketFloppyParts;

internal static class FloppyMotionController
{
    private readonly record struct DetachedTransform(Transform Tf, Vector3 RestLocalPos, Quaternion RestLocalRot);

    private sealed class AttachedChild
    {
        internal VehicleTransform Transform = null!;
        internal int IslandIndex;
        internal Vector3 RestLocalPos;
        internal Quaternion RestLocalRot;
        internal DetachedTransform[] Detached = Array.Empty<DetachedTransform>();
        internal bool Displaced;
    }

    private sealed class MotionInstance
    {
        internal PlateStructure Plate = null!;
        internal SubdividedSheet Sheet = null!;
        internal ThickShellMesh Shell = null!;
        internal PinResult Pins = null!;
        internal XpbdClothSolver Solver = null!;
        internal Transform[] Anchors = null!;
        internal string[] AnchorNames = null!;
        internal AttachedChild[] Children = Array.Empty<AttachedChild>();
        internal EngineExhaust[] Exhausts = Array.Empty<EngineExhaust>();
        internal EngineVent[] Vents = Array.Empty<EngineVent>();
        internal CombustionEngine[] Engines = Array.Empty<CombustionEngine>();
        internal AirflowJet[] JetBuffer = Array.Empty<AirflowJet>();
        internal Mesh Mesh = null!;
        internal MeshRenderer Renderer = null!;
        internal Material NormalMaterial = null!;
        internal Il2CppStructArray<Vector2> NormalUvs = null!;
        internal Il2CppStructArray<Vector2> PinPreviewUvs = null!;
        internal NVector[] SimPositions = null!;
        internal NVector[] FineSimPositions = null!;
        internal NVector[] DeformedVerts = null!;
        internal NVector[] DeformedNorms = null!;
        internal Il2CppStructArray<Vector3> UnityVerts = null!;
        internal Il2CppStructArray<Vector3> UnityNorms = null!;
        internal NMatrix[] AnchorLocalToWorld = null!;

        internal bool AtRest = true;
        internal bool ShowingPinPreview;
        internal bool ChildrenScannedInPlay;
        internal bool LoggedChildMove;
        internal float PendingLodDt;
        internal int FramePhase;
        internal int BoundsCounter;
        internal string Description = "";
    }

    private static readonly Dictionary<long, MotionInstance> instances = new();
    private static readonly List<long> toRemove = new();
    private static readonly HashSet<long> groundLogged = new();

    private static Texture2D? pinPreviewTexture;
    private static Material? pinPreviewMaterial;
    private static bool pinPreviewEnabled;
    private static float simTime;

    internal static bool PinPreviewEnabled
    {
        get => pinPreviewEnabled;
        set
        {
            if (pinPreviewEnabled == value) return;
            pinPreviewEnabled = value;
            RefreshPreviewAppearanceAll(isDesigner: true);
        }
    }

    internal static void Register(
        long key,
        PlateStructure plate,
        SubdividedSheet sheet,
        ThickShellMesh shell,
        PlateLookConfig cfg,
        Mesh mesh,
        MeshRenderer renderer,
        Material normalMaterial,
        bool designer)
    {
        if (instances.TryGetValue(key, out var existing))
            RestoreInstanceChildren(existing);

        var scan = RuntimePinDetector.Scan(plate);
        var xform = plate.transform;
        var l2w = xform.localToWorldMatrix;
        var lossy = xform.lossyScale;
        var restScale = NVector.Abs(new NVector(lossy.x, lossy.y, lossy.z));

        var proxy = sheet.Proxy;
        var simRestPos = proxy != null ? proxy.ProxyPositions : sheet.Positions;
        var simRestNorm = proxy != null ? proxy.ProxyNormals : sheet.Normals;
        var simTris = proxy != null ? proxy.ProxyTriangles : sheet.Triangles;
        var simEdges = proxy != null ? proxy.ProxyEdges : sheet.Edges;
        var simBend = proxy != null ? proxy.ProxyBendingPairs : sheet.BendingPairs;
        var simIslands = proxy != null ? proxy.ProxyIslandIds : sheet.IslandIds;
        int simIslandCount = proxy != null ? proxy.ProxyIslandCount : sheet.IslandCount;

        var simWorldPos = ComputeWorldPositions(l2w, simRestPos);

        var pins = PinSolver.Solve(
            simWorldPos,
            simEdges,
            proxy != null ? Array.Empty<DirectedEdge>() : sheet.BoundaryEdges,
            simIslands,
            simIslandCount,
            cfg.PinMode,
            cfg.PinRadiusCm * 0.01f,
            scan.WorldToLocal,
            scan.Colliders);

        var anchorMatrices = new NMatrix[scan.Anchors.Length];
        SampleAnchors(scan.Anchors, anchorMatrices);

        float avgScale = (restScale.X + restScale.Y + restScale.Z) / 3f;
        float avgThicknessM = 0.015f;
        if (sheet.BackOffset.Length > 0)
        {
            float sumThick = 0f;
            for (int i = 0; i < sheet.BackOffset.Length; i++)
                sumThick += MathF.Abs(sheet.BackOffset[i]) * avgScale;
            avgThicknessM = Math.Clamp(sumThick / sheet.BackOffset.Length, 0.002f, 0.25f);
        }

        var solver = new XpbdClothSolver(
            cfg.Motion,
            simRestPos,
            simRestNorm,
            simTris,
            simEdges,
            simBend,
            simIslands,
            simIslandCount,
            pins,
            restScale,
            scan.LocalColliders,
            anchorMatrices,
            avgThicknessM);

        int renderCount = shell.RenderVertexCount;
        var normalUvs = new Vector2[renderCount];
        var pinUvs = new Vector2[renderCount];
        int simCount = shell.SimVertexCount;

        for (int i = 0; i < renderCount; i++)
            normalUvs[i] = new Vector2(shell.Uvs[i].X, shell.Uvs[i].Y);

        for (int i = 0; i < simCount; i++)
        {
            bool pinned = IsFineVertexPinned(i, proxy, pins.IsPinned);
            var uv = new Vector2(pinned ? 0.25f : 0.75f, 0.5f);
            pinUvs[i] = uv;
            if (shell.IsThick)
                pinUvs[simCount + i] = uv;
        }

        if (shell.IsThick)
        {
            int rimStart = simCount * 2;
            for (int k = 0; k < sheet.BoundaryEdges.Length; k++)
            {
                var e = sheet.BoundaryEdges[k];
                bool pA = IsFineVertexPinned(e.A, proxy, pins.IsPinned);
                bool pB = IsFineVertexPinned(e.B, proxy, pins.IsPinned);
                int vBase = rimStart + k * 4;
                pinUvs[vBase] = new Vector2(pA ? 0.25f : 0.75f, 0.5f);
                pinUvs[vBase + 1] = new Vector2(pB ? 0.25f : 0.75f, 0.5f);
                pinUvs[vBase + 2] = new Vector2(pB ? 0.25f : 0.75f, 0.5f);
                pinUvs[vBase + 3] = new Vector2(pA ? 0.25f : 0.75f, 0.5f);
            }
        }

        var uVerts = new Vector3[renderCount];
        var uNorms = new Vector3[renderCount];
        for (int i = 0; i < renderCount; i++)
        {
            var v = shell.RestVertices[i];
            var n = shell.RestNormals[i];
            uVerts[i] = new Vector3(v.X, v.Y, v.Z);
            uNorms[i] = new Vector3(n.X, n.Y, n.Z);
        }

        var simPositions = new NVector[simRestPos.Length];
        solver.ResetToRest(anchorMatrices, simPositions);

        var children = cfg.Motion == MotionProfile.Hinged
            ? ScanHingedChildren(plate, simWorldPos, simIslands, simIslandCount)
            : Array.Empty<AttachedChild>();

        var desc = BuildDescription(cfg, simRestPos.Length, pins, scan.AnchorNames, proxy != null, children.Length);

        var inst = new MotionInstance
        {
            Plate = plate,
            Sheet = sheet,
            Shell = shell,
            Pins = pins,
            Solver = solver,
            Anchors = scan.Anchors,
            AnchorNames = scan.AnchorNames,
            Children = children,
            Exhausts = scan.Exhausts,
            Vents = scan.Vents,
            Engines = scan.Engines,
            JetBuffer = (scan.Exhausts.Length + scan.Vents.Length) > 0
                ? new AirflowJet[Math.Min(32, scan.Exhausts.Length + scan.Vents.Length)]
                : Array.Empty<AirflowJet>(),
            Mesh = mesh,
            Renderer = renderer,
            NormalMaterial = normalMaterial,
            NormalUvs = new Il2CppStructArray<Vector2>(normalUvs),
            PinPreviewUvs = new Il2CppStructArray<Vector2>(pinUvs),
            SimPositions = simPositions,
            FineSimPositions = new NVector[simCount],
            DeformedVerts = new NVector[renderCount],
            DeformedNorms = new NVector[renderCount],
            UnityVerts = new Il2CppStructArray<Vector3>(uVerts),
            UnityNorms = new Il2CppStructArray<Vector3>(uNorms),
            AnchorLocalToWorld = anchorMatrices,
            AtRest = true,
            FramePhase = instances.Count,
            Description = desc
        };

        instances[key] = inst;
        ApplyPreviewAppearance(inst, designer);

        if (Plugin.Detailed)
        {
            Plugin.ModLog.LogInfo(
                $"[Motion] Plate #{plate.VUID.Value} (designer={designer}): {desc} | stiff={cfg.Stiffness:F2} damp={cfg.Damping:F2} wind={cfg.WindScale:F2}");
            if (scan.Engines.Length > 0 || scan.Exhausts.Length > 0 || scan.Vents.Length > 0)
            {
                Plugin.ModLog.LogInfo(
                    $"[Airflow] Plate #{plate.VUID.Value}: {scan.Engines.Length} engine(s), {scan.Exhausts.Length} exhaust(s), {scan.Vents.Length} vent(s)");
            }
        }
    }

    internal static void UpdateMaterialReference(long key, Material normalMaterial)
    {
        if (!instances.TryGetValue(key, out var inst)) return;
        inst.NormalMaterial = normalMaterial;
        if (!inst.ShowingPinPreview && inst.Renderer != null)
            inst.Renderer.sharedMaterial = normalMaterial;
    }

    internal static void Unregister(long key)
    {
        if (instances.TryGetValue(key, out var inst))
        {
            RestoreInstanceChildren(inst);
            instances.Remove(key);
        }
    }

    internal static void RestoreAllAttachedChildrenToRest()
    {
        foreach (var inst in instances.Values)
            RestoreInstanceChildren(inst);
    }

    internal static void ApplyMuzzleBlast(NVector muzzlePos, NVector muzzleDir, float caliberMm)
    {
        if (!Plugin.MuzzleBlastEnabled.Value || instances.Count == 0) return;
        int affected = 0;
        foreach (var inst in instances.Values)
        {
            try
            {
                if (inst.Plate == null || inst.Plate.WasCollected ||
                    inst.Renderer == null || !inst.Renderer.gameObject.activeInHierarchy)
                    continue;
                if (inst.Solver.ApplyBlastImpulse(muzzlePos, muzzleDir, caliberMm))
                    affected++;
            }
            catch
            {
            }
        }

        if (affected > 0 && Plugin.Detailed)
            Plugin.ModLog.LogInfo($"[Blast] {caliberMm:F0}mm muzzle blast hit {affected} plate(s)");
    }

    internal static string Describe(PlateStructure? plate)
    {
        if (plate == null) return "Motion: Static";
        var cfg = PlateConfigStore.Get(plate);
        if (cfg.Motion == MotionProfile.Static) return "Motion: Static (rigid plate)";
        if (instances.TryGetValue(plate.Pointer.ToInt64(), out var inst))
            return inst.Description;
        return $"{cfg.Motion}: preparing simulation mesh...";
    }

    internal static void Tick(bool designer, bool photoMode, bool paused, float dt)
    {
        if (instances.Count == 0) return;

        if (designer || !Plugin.SimulationEnabled.Value)
        {
            ResetAllToRest();
            RefreshPreviewAppearanceAll(designer);
            return;
        }

        RefreshPreviewAppearanceAll(isDesigner: false);

        if (paused || !float.IsFinite(dt) || dt <= 0f)
        {
            if (photoMode)
            {
                SettleAtRestInstancesForPhotoMode();
                ReapplyHingedChildrenForPhotoMode();
            }
            return;
        }

        simTime += MathF.Min(dt, 0.05f);

        Vector3 camPos = Vector3.zero;
        bool hasCamera = false;
        try
        {
            var cam = Camera.main;
            if (cam != null)
            {
                camPos = cam.transform.position;
                hasCamera = true;
            }
        }
        catch
        {
        }

        int maxSimulated = Math.Clamp(Plugin.MaxSimulatedPlates.Value, 1, 128);
        int simulatedThisFrame = 0;
        int frame = Time.frameCount;

        toRemove.Clear();
        foreach (var pair in instances)
        {
            var inst = pair.Value;
            try
            {
                if (inst.Plate == null || inst.Plate.WasCollected || inst.Mesh == null || inst.Renderer == null)
                {
                    toRemove.Add(pair.Key);
                    continue;
                }
                if (!inst.Renderer.gameObject.activeInHierarchy) continue;
                if (simulatedThisFrame >= maxSimulated) continue;

                var cfg = PlateConfigStore.Get(inst.Plate);
                if (cfg.Motion == MotionProfile.Static)
                {
                    toRemove.Add(pair.Key);
                    continue;
                }

                int substeps = 4;
                float stepDt = dt;
                if (hasCamera)
                {
                    float dist = Vector3.Distance(camPos, inst.Plate.transform.position);
                    if (dist > 80f)
                    {
                        inst.PendingLodDt = 0f;
                        continue;
                    }
                    if (dist >= 35f)
                    {
                        inst.PendingLodDt += dt;
                        if (((frame + inst.FramePhase) & 1) != 0)
                            continue;
                        stepDt = inst.PendingLodDt;
                        inst.PendingLodDt = 0f;
                        substeps = 2;
                    }
                    else
                    {
                        inst.PendingLodDt = 0f;
                    }
                }

                simulatedThisFrame++;
                StepInstance(inst, cfg, stepDt, substeps, photoMode);
            }
            catch
            {
                toRemove.Add(pair.Key);
            }
        }

        foreach (long k in toRemove)
            Unregister(k);
    }

    internal static void Clear()
    {
        RestoreAllAttachedChildrenToRest();
        instances.Clear();
        toRemove.Clear();
        groundLogged.Clear();
        if (pinPreviewMaterial != null) Object.DestroyImmediate(pinPreviewMaterial);
        if (pinPreviewTexture != null) Object.DestroyImmediate(pinPreviewTexture);
        pinPreviewMaterial = null;
        pinPreviewTexture = null;
        simTime = 0f;
    }

    private static void SettleAtRestInstancesForPhotoMode()
    {
        foreach (var inst in instances.Values)
        {
            if (!inst.AtRest) continue;
            try
            {
                if (inst.Plate == null || inst.Plate.WasCollected ||
                    inst.Mesh == null || inst.Renderer == null ||
                    !inst.Renderer.gameObject.activeInHierarchy)
                    continue;

                var cfg = PlateConfigStore.Get(inst.Plate);
                if (cfg.Motion == MotionProfile.Static) continue;

                for (int step = 0; step < 90; step++)
                    StepInstance(inst, cfg, 1f / 60f, 4, photoMode: true, windOverride: 0f);
            }
            catch
            {
            }
        }
    }

    private static void StepInstance(
        MotionInstance inst,
        PlateLookConfig cfg,
        float dt,
        int substeps,
        bool photoMode,
        float? windOverride = null)
    {
        SampleAnchors(inst.Anchors, inst.AnchorLocalToWorld);

        bool surfaceCollision = Plugin.SurfaceCollisionEnabled.Value;
        GroundPlane ground = surfaceCollision && (cfg.Motion == MotionProfile.Cloth || cfg.Motion == MotionProfile.Rubber)
            ? SampleGroundPlane(inst.Plate.transform.position, inst.Plate.VUID.Value)
            : default;

        int jetCount = FillAirflowJets(inst);

        inst.Solver.Step(
            dt,
            simTime,
            inst.AnchorLocalToWorld,
            cfg.Stiffness,
            cfg.Damping,
            windOverride ?? cfg.WindScale,
            cfg.MaxSwingDeg,
            substeps,
            inst.SimPositions,
            ground,
            surfaceCollision,
            jetCount > 0 ? inst.JetBuffer.AsSpan(0, jetCount) : default);

        ReadOnlySpan<NVector> simSpan;
        var proxy = inst.Sheet.Proxy;
        if (proxy != null)
        {
            SkinFromProxy(proxy, inst.Sheet.Positions, inst.SimPositions, inst.FineSimPositions);
            simSpan = inst.FineSimPositions;
        }
        else
        {
            simSpan = inst.SimPositions;
        }

        inst.Shell.UpdateDeformed(simSpan, inst.DeformedVerts, inst.DeformedNorms);

        int renderCount = inst.Shell.RenderVertexCount;
        var uVerts = inst.UnityVerts;
        var uNorms = inst.UnityNorms;
        var dVerts = inst.DeformedVerts;
        var dNorms = inst.DeformedNorms;
        for (int i = 0; i < renderCount; i++)
        {
            var v = dVerts[i];
            var n = dNorms[i];
            uVerts[i] = new Vector3(v.X, v.Y, v.Z);
            uNorms[i] = new Vector3(n.X, n.Y, n.Z);
        }

        inst.Mesh.vertices = uVerts;
        inst.Mesh.normals = uNorms;
        if ((++inst.BoundsCounter & 3) == 0)
            inst.Mesh.RecalculateBounds(MeshUpdateFlags.DontValidateIndices);

        inst.AtRest = false;

        if (cfg.Motion == MotionProfile.Hinged)
        {
            if (!inst.ChildrenScannedInPlay)
            {
                bool physicsOrPhoto = photoMode || (inst.Plate.VehicleObject?.PhysicsEnabled == true);
                if (physicsOrPhoto)
                {
                    RefreshHingedChildren(inst);
                    inst.ChildrenScannedInPlay = true;
                }
            }
            MoveAttachedChildren(inst, cfg, photoMode);
        }
    }

    internal static void RefreshHingedChildren(long key)
    {
        if (!instances.TryGetValue(key, out var inst)) return;
        if (inst.Plate == null || inst.Plate.WasCollected) return;
        RefreshHingedChildren(inst);
    }

    private static void RefreshHingedChildren(MotionInstance inst)
    {
        try
        {
            RestoreInstanceChildren(inst);
            if (inst.Plate == null || inst.Plate.WasCollected) return;

            var l2w = inst.Plate.transform.localToWorldMatrix;
            var proxy = inst.Sheet.Proxy;
            var simRestPos = proxy != null ? proxy.ProxyPositions : inst.Sheet.Positions;
            var simIslands = proxy != null ? proxy.ProxyIslandIds : inst.Sheet.IslandIds;
            int simIslandCount = proxy != null ? proxy.ProxyIslandCount : inst.Sheet.IslandCount;

            var simWorldPos = ComputeWorldPositions(l2w, simRestPos);

            inst.Children = ScanHingedChildren(inst.Plate, simWorldPos, simIslands, simIslandCount);
            var cfg = PlateConfigStore.Get(inst.Plate);
            inst.Description = BuildDescription(cfg, simRestPos.Length, inst.Pins, inst.AnchorNames, proxy != null, inst.Children.Length);

            if (Plugin.Detailed)
                Plugin.ModLog.LogInfo($"[Hinged] Plate #{inst.Plate.VUID.Value} refreshed attached children: {inst.Children.Length}");
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[Hinged] Refresh children warning: {ex.Message}");
        }
    }

    private static void MoveAttachedChildren(MotionInstance inst, PlateLookConfig cfg, bool photoMode)
    {
        if (!Plugin.HingedAttachmentsEnabled.Value || cfg.Motion != MotionProfile.Hinged || inst.Children.Length == 0)
        {
            RestoreInstanceChildren(inst);
            return;
        }

        bool physicsRunning = false;
        try { physicsRunning = inst.Plate.VehicleObject?.PhysicsEnabled == true; }
        catch { physicsRunning = false; }

        if (!physicsRunning && !photoMode)
            return;

        var plateTf = inst.Plate.transform;
        for (int i = 0; i < inst.Children.Length; i++)
        {
            var child = inst.Children[i];
            var vt = child.Transform;
            if (vt == null || vt.WasCollected) continue;
            if (!inst.Solver.TryGetIslandSwing(child.IslandIndex, out var pivotW, out var axisW, out float angleRad))
                continue;

            var restWorldPos = plateTf.TransformPoint(child.RestLocalPos);
            var restWorldRot = plateTf.rotation * child.RestLocalRot;
            var qSwing = Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, new Vector3(axisW.X, axisW.Y, axisW.Z));
            var pivotU = new Vector3(pivotW.X, pivotW.Y, pivotW.Z);

            var newPos = pivotU + qSwing * (restWorldPos - pivotU);
            var newRot = qSwing * restWorldRot;
            var ctf = vt.transform;
            if (Quaternion.Angle(ctf.rotation, newRot) > 0.0005f || Vector3.Distance(ctf.position, newPos) > 0.00001f)
            {
                ctf.SetPositionAndRotation(newPos, newRot);
                child.Displaced = true;
            }
            for (int d = 0; d < child.Detached.Length; d++)
            {
                var dt = child.Detached[d];
                if (dt.Tf == null) continue;
                var dRestW = plateTf.TransformPoint(dt.RestLocalPos);
                var dRestR = plateTf.rotation * dt.RestLocalRot;
                dt.Tf.SetPositionAndRotation(pivotU + qSwing * (dRestW - pivotU), qSwing * dRestR);
                child.Displaced = true;
            }

            if (Plugin.Detailed && !inst.LoggedChildMove && MathF.Abs(angleRad) > 0.005f)
            {
                inst.LoggedChildMove = true;
                float angleDeg = angleRad * Mathf.Rad2Deg;
                string vtParent = ctf.parent != null ? ctf.parent.name : "<root>";
                Plugin.ModLog.LogInfo(
                    $"[Hinged] Plate #{inst.Plate.VUID.Value} moved {inst.Children.Length} child(ren): angleDeg={angleDeg:F2} vtParent={vtParent} detachedCount={child.Detached.Length} newPos={newPos} worldPos={ctf.position}");
            }
        }
    }

    private static void ReapplyHingedChildrenForPhotoMode()
    {
        foreach (var inst in instances.Values)
        {
            if (inst.AtRest || inst.Plate == null || inst.Plate.WasCollected) continue;
            try
            {
                var cfg = PlateConfigStore.Get(inst.Plate);
                if (cfg.Motion == MotionProfile.Hinged)
                    MoveAttachedChildren(inst, cfg, photoMode: true);
            }
            catch
            {
            }
        }
    }

    private static void RestoreInstanceChildren(MotionInstance inst)
    {
        if (inst.Children.Length == 0) return;
        bool plateAlive = inst.Plate != null && !inst.Plate.WasCollected;
        Transform? plateTf = plateAlive ? inst.Plate!.transform : null;

        for (int i = 0; i < inst.Children.Length; i++)
        {
            var child = inst.Children[i];
            if (!child.Displaced) continue;
            child.Displaced = false;
            try
            {
                if (plateTf == null || child.Transform == null || child.Transform.WasCollected) continue;
                child.Transform.transform.SetPositionAndRotation(
                    plateTf.TransformPoint(child.RestLocalPos),
                    plateTf.rotation * child.RestLocalRot);
                for (int d = 0; d < child.Detached.Length; d++)
                {
                    var dt = child.Detached[d];
                    if (dt.Tf == null) continue;
                    dt.Tf.SetPositionAndRotation(
                        plateTf.TransformPoint(dt.RestLocalPos),
                        plateTf.rotation * dt.RestLocalRot);
                }
            }
            catch
            {
            }
        }
    }

    private static AttachedChild[] ScanHingedChildren(
        PlateStructure plate,
        NVector[] simWorldPos,
        int[] simIslands,
        int simIslandCount)
    {
        var list = new List<AttachedChild>();
        try
        {
            FloppyOverlayRuntime.ProtectTransformHierarchy(plate.gameObject);
            var selfVo = plate.VehicleObject;
            var selfVt = selfVo?.VehicleTransform;
            if (selfVt == null || selfVt.WasCollected) return Array.Empty<AttachedChild>();

            var seen = new HashSet<IntPtr> { selfVt.Pointer };
            var seenDetached = new HashSet<IntPtr>();
            var mirrorVt = plate.Mirrored ? selfVt.Mirror : null;
            if (mirrorVt != null && !mirrorVt.WasCollected)
                seen.Add(mirrorVt.Pointer);

            var plateTf = plate.transform;
            var invPlateRot = Quaternion.Inverse(plateTf.rotation);

            void CollectFrom(VehicleTransform? rootVt)
            {
                var children = rootVt?.Children;
                if (children == null) return;
                int count = children.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleTransform>>().Count;
                for (int i = 0; i < count; i++)
                {
                    var child = children[i];
                    if (child == null || child.WasCollected) continue;
                    if (!seen.Add(child.Pointer)) continue;

                    var childVo = child.VehicleObject;
                    if (childVo == null || childVo.WasCollected || childVo.Released) continue;
                    if (IsCoreVehiclePart(childVo)) continue;

                    var ctf = child.transform;
                    var detached = new List<DetachedTransform>();
                    MarkSubtreeDynamic(child, ctf, plateTf, invPlateRot, seenDetached, detached);

                    var pos = ctf.position;
                    var nPos = new NVector(pos.x, pos.y, pos.z);
                    int nearestIsl = 0;
                    float bestDistSq = float.PositiveInfinity;
                    if (simIslandCount > 1)
                    {
                        for (int v = 0; v < simWorldPos.Length; v++)
                        {
                            float dSq = NVector.DistanceSquared(nPos, simWorldPos[v]);
                            if (dSq < bestDistSq)
                            {
                                bestDistSq = dSq;
                                nearestIsl = v < simIslands.Length ? simIslands[v] : 0;
                            }
                        }
                    }

                    list.Add(new AttachedChild
                    {
                        Transform = child,
                        IslandIndex = Math.Clamp(nearestIsl, 0, Math.Max(0, simIslandCount - 1)),
                        RestLocalPos = plateTf.InverseTransformPoint(pos),
                        RestLocalRot = invPlateRot * ctf.rotation,
                        Detached = detached.ToArray(),
                        Displaced = false
                    });
                }
            }

            CollectFrom(selfVt);
            if (mirrorVt != null && !mirrorVt.WasCollected)
                CollectFrom(mirrorVt);
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[Hinged] Child scan warning: {ex.Message}");
        }

        return list.ToArray();
    }

    internal static bool IsCoreVehiclePart(VehicleObject vo)
    {
        var comps = vo.Components;
        if (comps == null) return false;
        int count = comps.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        for (int i = 0; i < count; i++)
        {
            var c = comps[i];
            if (c == null || c.WasCollected) continue;
            if (c.TryCast<TrackAssembly>() != null ||
                c.TryCast<WheelMount>() != null ||
                c.TryCast<Suspension>() != null ||
                c.TryCast<PowertrainComponent>() != null ||
                c.TryCast<TurretRing>() != null ||
                c.TryCast<Cannon>() != null ||
                c.TryCast<CannonBehaviour>() != null)
                return true;
        }
        return false;
    }

    private static NVector[] ComputeWorldPositions(Matrix4x4 l2w, NVector[] localPositions)
    {
        var worldPositions = new NVector[localPositions.Length];
        for (int i = 0; i < localPositions.Length; i++)
        {
            var wp = l2w.MultiplyPoint3x4(new Vector3(localPositions[i].X, localPositions[i].Y, localPositions[i].Z));
            worldPositions[i] = new NVector(wp.x, wp.y, wp.z);
        }
        return worldPositions;
    }

    private static void MarkSubtreeDynamic(
        VehicleTransform root,
        Transform rootUnityTf,
        Transform plateTf,
        Quaternion invPlateRot,
        HashSet<IntPtr> seenDetached,
        List<DetachedTransform> detached)
    {
        var visited = new HashSet<IntPtr>();
        MarkSubtreeRecursive(root, 0, visited, rootUnityTf, plateTf, invPlateRot, seenDetached, detached);
    }

    private static void MarkSubtreeRecursive(
        VehicleTransform? node,
        int depth,
        HashSet<IntPtr> visited,
        Transform rootUnityTf,
        Transform plateTf,
        Quaternion invPlateRot,
        HashSet<IntPtr> seenDetached,
        List<DetachedTransform> detached)
    {
        if (node == null || node.WasCollected || depth > 8 || !visited.Add(node.Pointer))
            return;

        var vo = node.VehicleObject;
        if (vo == null || vo.WasCollected || vo.Released || IsCoreVehiclePart(vo))
            return;

        FloppyOverlayRuntime.ProtectTransformHierarchy(node.gameObject);

        var comps = vo.Components;
        if (comps != null)
        {
            int count = comps.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for (int i = 0; i < count; i++)
            {
                if (comps[i]?.TryCast<VehicleObjectModel>() is { } model)
                {
                    FloppyOverlayRuntime.MarkModelDynamic(model);
                    var mtf = model.Model?.transform;
                    if (mtf != null && !mtf.IsChildOf(rootUnityTf) && seenDetached.Add(mtf.Pointer))
                    {
                        detached.Add(new DetachedTransform(
                            mtf,
                            plateTf.InverseTransformPoint(mtf.position),
                            invPlateRot * mtf.rotation));
                    }
                }
            }
        }

        var children = node.Children;
        if (children == null) return;
        int childCount = children.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleTransform>>().Count;
        for (int i = 0; i < childCount; i++)
            MarkSubtreeRecursive(children[i], depth + 1, visited, rootUnityTf, plateTf, invPlateRot, seenDetached, detached);
    }

    private static GroundPlane SampleGroundPlane(Vector3 worldPos, long plateVuid)
    {
        try
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return default;
            float baseY = terrain.transform.position.y;
            float h0 = terrain.SampleHeight(worldPos);
            float hx = terrain.SampleHeight(worldPos + new Vector3(0.5f, 0f, 0f));
            float hz = terrain.SampleHeight(worldPos + new Vector3(0f, 0f, 0.5f));
            float y0 = baseY + h0;
            float yx = baseY + hx;
            float yz = baseY + hz;

            bool accepted = float.IsFinite(y0) && float.IsFinite(yx) && float.IsFinite(yz) &&
                            y0 <= worldPos.y + 0.5f && y0 >= worldPos.y - 20.0f;
            if (Plugin.Detailed && groundLogged.Add(plateVuid))
            {
                Plugin.ModLog.LogInfo(
                    $"[Ground] Plate #{plateVuid}: plateY={worldPos.y:F2} baseY={baseY:F2} sampleH={h0:F2} worldY={y0:F2} accepted={accepted}");
            }
            if (!accepted) return default;

            var p0 = new NVector(worldPos.x, y0, worldPos.z);
            var vx = new NVector(0.5f, yx - y0, 0f);
            var vz = new NVector(0f, yz - y0, 0.5f);
            var n = NVector.Cross(vz, vx);
            float lenSq = n.LengthSquared();
            var normal = lenSq > 1e-12f ? n / MathF.Sqrt(lenSq) : NVector.UnitY;
            if (normal.Y < 0f) normal = -normal;
            return new GroundPlane(true, p0, normal);
        }
        catch
        {
            return default;
        }
    }

    private static void ResetAllToRest()
    {
        RestoreAllAttachedChildrenToRest();
        foreach (var inst in instances.Values)
        {
            inst.ChildrenScannedInPlay = false;
            inst.LoggedChildMove = false;
            if (inst.AtRest) continue;
            try
            {
                if (inst.Mesh == null || inst.Plate == null || inst.Plate.WasCollected) continue;
                SampleAnchors(inst.Anchors, inst.AnchorLocalToWorld);
                inst.Solver.ResetToRest(inst.AnchorLocalToWorld, inst.SimPositions);
                int count = inst.Shell.RenderVertexCount;
                for (int i = 0; i < count; i++)
                {
                    var v = inst.Shell.RestVertices[i];
                    var n = inst.Shell.RestNormals[i];
                    inst.UnityVerts[i] = new Vector3(v.X, v.Y, v.Z);
                    inst.UnityNorms[i] = new Vector3(n.X, n.Y, n.Z);
                }
                inst.Mesh.vertices = inst.UnityVerts;
                inst.Mesh.normals = inst.UnityNorms;
                inst.Mesh.RecalculateBounds(MeshUpdateFlags.DontValidateIndices);
                inst.AtRest = true;
                inst.PendingLodDt = 0f;
            }
            catch
            {
                inst.AtRest = true;
            }
        }
    }

    private static void RefreshPreviewAppearanceAll(bool isDesigner)
    {
        foreach (var inst in instances.Values)
            ApplyPreviewAppearance(inst, isDesigner);
    }

    private static void ApplyPreviewAppearance(MotionInstance inst, bool isDesigner)
    {
        bool want = isDesigner && pinPreviewEnabled;
        if (inst.ShowingPinPreview == want) return;
        if (inst.Renderer == null || inst.Mesh == null) return;

        if (want)
        {
            var previewMat = GetOrCreatePinPreviewMaterial();
            if (previewMat == null) return;
            inst.Mesh.uv = inst.PinPreviewUvs;
            inst.Renderer.sharedMaterial = previewMat;
            inst.Renderer.SetPropertyBlock(null);
            inst.ShowingPinPreview = true;
        }
        else
        {
            inst.Mesh.uv = inst.NormalUvs;
            inst.Renderer.sharedMaterial = inst.NormalMaterial;
            if (inst.Plate != null && !inst.Plate.WasCollected)
            {
                var cfg = PlateConfigStore.Get(inst.Plate);
                if (cfg.Preset == LookPreset.Native)
                {
                    inst.Renderer.SetPropertyBlock(null);
                }
                else
                {
                    var paint = cfg.ColorMode == ColorMode.FollowTankPaint ? TankPaintReader.Read(inst.Plate) : null;
                    MaterialLibrary.GetMaterial(cfg.Preset, cfg.CustomFolder, out bool hasMask);
                    MaterialLibrary.ApplyPropertyBlock(inst.Renderer, cfg, paint, hasMask);
                }
            }
            inst.ShowingPinPreview = false;
        }
    }

    private static Material? GetOrCreatePinPreviewMaterial()
    {
        if (pinPreviewMaterial != null) return pinPreviewMaterial;
        try
        {
            var shader = Shader.Find("HDRP/Lit");
            if (shader == null || !shader.isSupported) return null;

            var tex = new Texture2D(2, 1, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "Floppy PinPreview",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            var px = new Color32[]
            {
                new Color32(235, 55, 55, 255),
                new Color32(55, 220, 90, 255)
            };
            tex.SetPixels32(new Il2CppStructArray<Color32>(px), 0);
            tex.Apply(false, false);

            var mat = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "Floppy PinPreview Material"
            };
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
            if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 0f);
            if (mat.HasProperty("_CullModeForward")) mat.SetFloat("_CullModeForward", 0f);
            if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
            if (mat.HasProperty("_DoubleSidedNormalMode")) mat.SetFloat("_DoubleSidedNormalMode", 1f);
            mat.SetVector("_DoubleSidedConstants", new Vector4(1f, 1f, -1f, 0f));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseColorMap", tex);
            HDMaterial.ValidateMaterial(mat);

            pinPreviewTexture = tex;
            pinPreviewMaterial = mat;
            return pinPreviewMaterial;
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[PinPreview] Material init failed: {ex.Message}");
            return null;
        }
    }

    private static void SampleAnchors(Transform[] anchors, NMatrix[] matrices)
    {
        var fallback = anchors.Length > 0 && anchors[0] != null
            ? ToNumerics(anchors[0].localToWorldMatrix)
            : NMatrix.Identity;

        for (int i = 0; i < anchors.Length; i++)
        {
            var t = anchors[i];
            matrices[i] = t != null ? ToNumerics(t.localToWorldMatrix) : fallback;
        }
    }

    private static void SkinFromProxy(
        ProxySkin proxy,
        NVector[] fineRest,
        NVector[] proxySim,
        NVector[] fineOut)
    {
        var proxyRest = proxy.ProxyPositions;
        int n = fineRest.Length;
        for (int i = 0; i < n; i++)
        {
            int p0 = proxy.VertexProxy0[i];
            int p1 = proxy.VertexProxy1[i];
            int p2 = proxy.VertexProxy2[i];
            var d0 = proxySim[p0] - proxyRest[p0];
            var d1 = proxySim[p1] - proxyRest[p1];
            var d2 = proxySim[p2] - proxyRest[p2];
            fineOut[i] = fineRest[i] +
                         d0 * proxy.VertexWeight0[i] +
                         d1 * proxy.VertexWeight1[i] +
                         d2 * proxy.VertexWeight2[i];
        }
    }

    private static bool IsFineVertexPinned(int fineIdx, ProxySkin? proxy, bool[] simPinned)
    {
        if (proxy == null) return fineIdx < simPinned.Length && simPinned[fineIdx];
        int p0 = proxy.VertexProxy0[fineIdx];
        return p0 < simPinned.Length && simPinned[p0];
    }

    private static string BuildDescription(
        PlateLookConfig cfg,
        int simVerts,
        PinResult pins,
        string[] anchorNames,
        bool usesProxy,
        int childCount)
    {
        var usedNames = new List<string>();
        var seenAnchors = new HashSet<int>();
        for (int i = 0; i < pins.IsPinned.Length; i++)
        {
            if (!pins.IsPinned[i]) continue;
            int a = pins.AnchorIndex[i];
            if (seenAnchors.Add(a) && a >= 0 && a < anchorNames.Length)
                usedNames.Add(anchorNames[a]);
        }

        string anchorList = usedNames.Count > 0 ? string.Join(", ", usedNames.Take(4)) : "Self";
        if (usedNames.Count > 4) anchorList += $" +{usedNames.Count - 4}";
        string fallbackNote = pins.FellBackToTopEdge ? " (TopEdge fallback)" : "";
        string proxyNote = usesProxy ? " [proxy]" : "";
        string childNote = childCount > 0 ? $" | {childCount} attached part(s)" : "";
        return $"{cfg.Motion}: {simVerts} sim verts{proxyNote} | {pins.PinnedCount} pinned across {pins.UsedAnchorCount} anchor(s) ({anchorList}){fallbackNote}{childNote}";
    }

    private static int FillAirflowJets(MotionInstance inst)
    {
        if (!Plugin.ExhaustAndVentAirflowEnabled.Value || inst.JetBuffer.Length == 0)
            return 0;

        float activity = 0f;
        float pulseHz = 8f;
        int runningEngines = 0;

        for (int e = 0; e < inst.Engines.Length; e++)
        {
            var eng = inst.Engines[e];
            if (eng == null || eng.WasCollected) continue;
            try
            {
                var beh = eng.Behaviour?.TryCast<EngineBehaviour>();
                if (beh == null || beh.WasCollected) continue;
                float rpm = beh.RPM;
                if (!float.IsFinite(rpm) || rpm <= 10f) continue;

                float idle = beh.IdleRPM;
                float maxRpm = beh.MaxRPM;
                float throttle = float.IsFinite(beh.Throttle) ? Math.Clamp(beh.Throttle, 0f, 1f) : 0f;
                float normRpm = (maxRpm > idle + 1f)
                    ? Math.Clamp((rpm - idle) / (maxRpm - idle), 0f, 1.2f)
                    : Math.Clamp(rpm / 2500f, 0f, 1f);
                float engAct = Math.Clamp(0.25f + 0.55f * normRpm + 0.35f * throttle, 0f, 1.25f);
                if (engAct > activity) activity = engAct;
                float engHz = Math.Clamp(6f + rpm * 0.02f, 6f, 45f);
                if (runningEngines == 0 || engHz > pulseHz) pulseHz = engHz;
                runningEngines++;
            }
            catch
            {
            }
        }

        if (runningEngines == 0)
            return 0;

        int jetCount = 0;
        int maxJets = inst.JetBuffer.Length;

        for (int i = 0; i < inst.Exhausts.Length && jetCount < maxJets; i++)
        {
            var ex = inst.Exhausts[i];
            if (ex == null || ex.WasCollected) continue;
            try
            {
                var parent = ex.OutputParent ?? ex.transform;
                if (parent == null) continue;
                var origU = parent.TransformPoint(ex.OutputLocalPosition);
                var dirU = parent.TransformDirection(ex.OutputLocalDirection);
                if (dirU.sqrMagnitude < 1e-6f) dirU = parent.up;
                dirU.Normalize();

                var sc = ex.OutputScale;
                float scaleAvg = (MathF.Abs(sc.x) + MathF.Abs(sc.y) + MathF.Abs(sc.z)) / 3f;
                float radiusM = Math.Clamp(0.30f * Math.Clamp(scaleAvg, 0.4f, 3.0f), 0.15f, 1.2f);
                float rangeM = Math.Clamp(2.0f + 1.4f * activity, 1.5f, 4.5f);
                float strength = 4.5f + 8.5f * activity;

                inst.JetBuffer[jetCount++] = new AirflowJet(
                    new NVector(origU.x, origU.y, origU.z),
                    new NVector(dirU.x, dirU.y, dirU.z),
                    rangeM,
                    radiusM,
                    strength,
                    pulseHz,
                    i * 1.37f);
            }
            catch
            {
            }
        }

        for (int i = 0; i < inst.Vents.Length && jetCount < maxJets; i++)
        {
            var v = inst.Vents[i];
            if (v == null || v.WasCollected) continue;
            try
            {
                var vt = v.transform;
                if (vt == null) continue;

                var rawPos = v.exitPosition;
                var localPos = (float.IsFinite(rawPos.x) && float.IsFinite(rawPos.y) && float.IsFinite(rawPos.z))
                    ? rawPos
                    : Vector3.zero;

                var rawDir = v.exitDirection;
                var localDir = (float.IsFinite(rawDir.x) && float.IsFinite(rawDir.y) && float.IsFinite(rawDir.z))
                    ? rawDir
                    : Vector3.up;

                float exitSize = v.exitSize;
                float ventRadius = Math.Clamp(
                    float.IsFinite(exitSize) && exitSize > 0.01f ? exitSize * 0.85f : 0.45f,
                    0.20f,
                    1.5f);

                var origU = vt.TransformPoint(localPos);
                if (!float.IsFinite(origU.x) || !float.IsFinite(origU.y) || !float.IsFinite(origU.z))
                    continue;

                var dirU = vt.TransformDirection(localDir);
                if (!float.IsFinite(dirU.x) || !float.IsFinite(dirU.y) || !float.IsFinite(dirU.z) ||
                    dirU.sqrMagnitude < 1e-6f)
                {
                    dirU = vt.up;
                }
                if (!float.IsFinite(dirU.x) || !float.IsFinite(dirU.y) || !float.IsFinite(dirU.z) ||
                    dirU.sqrMagnitude < 1e-6f)
                {
                    dirU = Vector3.up;
                }
                dirU.Normalize();

                float rangeM = 1.6f + 0.8f * activity;
                float strength = 3.0f + 5.5f * activity;
                float ventHz = Math.Clamp(4.5f + activity * 6f, 4f, 16f);

                inst.JetBuffer[jetCount++] = new AirflowJet(
                    new NVector(origU.x, origU.y, origU.z),
                    new NVector(dirU.x, dirU.y, dirU.z),
                    rangeM,
                    ventRadius,
                    strength,
                    ventHz,
                    i * 2.11f);
            }
            catch
            {
            }
        }

        return jetCount;
    }

    private static NMatrix ToNumerics(Matrix4x4 m) => new(
        m.m00, m.m10, m.m20, m.m30,
        m.m01, m.m11, m.m21, m.m31,
        m.m02, m.m12, m.m22, m.m32,
        m.m03, m.m13, m.m23, m.m33);
}
