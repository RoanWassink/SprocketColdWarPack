using BepInEx.Configuration;
using Sprocket.Engines;
using Sprocket.Powertrains.Engines;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.CrewSystems;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Exhausts;
using Sprocket.Vehicles.Fires;
using Sprocket.Vehicles.Weapons.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;
using NMatrix = System.Numerics.Matrix4x4;
using NVec = System.Numerics.Vector3;

namespace SprocketThermalSight;

// 0.4.0 heat sources, read-only: native fields are polled (<= 10 Hz, vehicles in view only).
// No Harmony patch and no native writes; wrappers are cached at resolve time (every 5 s).
internal static class HeatTracker
{
    internal const int MaxSpots = 64;
    private const int SpotsPerVehicle = 16;
    private enum Kind : byte { Engine, Exhaust, Barrel, Wreck }
    private sealed class EngineRef { internal CombustionEngine Part = null!; internal Transform T = null!; internal EngineBehaviour? Eb; internal IReadOnlyPowertrainEngine? Ro; internal Vector3 Center, Extents; internal float Radius; }
    private sealed class ExhaustRef { internal Transform Parent = null!; internal Vector3 Local; }
    // 0.4.3: one ShotCounter per cannon, observed every frame (not only at the heat poll).
    private sealed class CannonRef { internal CannonBehaviour B = null!; internal readonly ShotCounter Shots = new(); internal float Radius; }
    private sealed class Entry
    {
        internal Vehicle V = null!; internal VehicleBehaviour? Vb; internal Transform? Root;
        internal readonly List<EngineRef> Engines = new(); internal readonly List<ExhaustRef> Exhausts = new(); internal readonly List<CannonRef> Cannons = new();
        internal VehicleCrewWorkspaceModule? Crew; internal FireControlVehicleModule? Fire;
        internal Vector3 BoundsCenter; internal float BoundsRadius = 3;
        internal float ResolveAt, PolledAt = -1, LogAt; internal bool Seen, Failed, Logged;
        internal readonly VehicleHeat Heat = new();
        internal HeatInputs Last;
        internal readonly Vector3[] Local = new Vector3[SpotsPerVehicle];
        internal readonly Vector3[] AxisX = new Vector3[SpotsPerVehicle], AxisY = new Vector3[SpotsPerVehicle], AxisZ = new Vector3[SpotsPerVehicle];
        internal readonly float[] Radius = new float[SpotsPerVehicle], Amp = new float[SpotsPerVehicle];
        internal readonly Kind[] Kinds = new Kind[SpotsPerVehicle];
        internal int Count, InView, PendingShots;
        // 0.4.3 diagnostics of the engine spot in the last projection (one capture in flight at a time).
        internal int DiagIndex = -1; internal SpotReject DiagReason; internal float DiagX, DiagY, DiagDepth; internal bool DiagEngine; internal float DiagAt;
    }
    private static readonly Dictionary<IntPtr, Entry> entries = new();
    private static readonly List<IntPtr> stale = new();
    private static readonly HashSet<IntPtr> alive = new();
    internal static HeatSettings Settings { get; private set; } = new();
    private static float pollAt, summaryAt;
    private static int logs, shotLogs, diagLogs;
    private static string cameraLabel = "none"; private static IntPtr cameraLabelId;
    private static int rejectRange, rejectBehind, rejectFar, rejectOff, rejectSmall;
    private static IntPtr cameraId; private static Transform? cameraTransform;

    internal static void Configure(ConfigFile c)
    {
        var d = new HeatSettings();
        ConfigEntry<float> F(string key, float value, string text) => c.Bind("Heat", key, value, text);
        var s = new HeatSettings
        {
            Enabled = c.Bind("Heat", "Enabled", d.Enabled, "Per-vehicle heat model (engine/exhaust, barrel, wrecks). False = 0.3.x uniform vehicle heat.").Value,
            AmbientC = F("AmbientC", d.AmbientC, "Ambient temperature in degC (diagnostics and reference).").Value,
            FullScaleKelvin = F("FullScaleKelvin", d.FullScaleKelvin, "Temperature rise (K) that adds full signal above the hull; smaller = hot spots saturate sooner.").Value,
            HullContrast = F("HullContrast", d.HullContrast, "Cold hull signal above its surroundings (0..1).").Value,
            EngineScale = F("EngineScale", d.EngineScale, "Signal scale of the engine-compartment hot spot.").Value,
            ExhaustScale = F("ExhaustScale", d.ExhaustScale, "Signal scale of exhaust outlet hot spots.").Value,
            BarrelScale = F("BarrelScale", d.BarrelScale, "Signal scale of the barrel after firing.").Value,
            WreckScale = F("WreckScale", d.WreckScale, "Signal scale of burning/cooling wreck heat.").Value,
            EngineIdleRiseK = F("EngineIdleRiseK", d.EngineIdleRiseK, "Engine deck rise above ambient at idle (K).").Value,
            EngineFullRiseK = F("EngineFullRiseK", d.EngineFullRiseK, "Engine deck rise at full load (K).").Value,
            EngineWarmTauS = F("EngineWarmTauS", d.EngineWarmTauS, "Engine warm-up time constant (s).").Value,
            EngineCoolTauS = F("EngineCoolTauS", d.EngineCoolTauS, "Engine cool-down time constant after shutdown (s).").Value,
            ExhaustIdleRiseK = F("ExhaustIdleRiseK", d.ExhaustIdleRiseK, "Exhaust outlet rise at idle (K).").Value,
            ExhaustFullRiseK = F("ExhaustFullRiseK", d.ExhaustFullRiseK, "Exhaust outlet rise at full load (K).").Value,
            ExhaustWarmTauS = F("ExhaustWarmTauS", d.ExhaustWarmTauS, "Exhaust warm-up time constant (s).").Value,
            ExhaustCoolTauS = F("ExhaustCoolTauS", d.ExhaustCoolTauS, "Exhaust cool-down time constant (s).").Value,
            BarrelShotRiseK = F("BarrelShotRiseK", d.BarrelShotRiseK, "Barrel rise per shot (K).").Value,
            BarrelMaxRiseK = F("BarrelMaxRiseK", d.BarrelMaxRiseK, "Maximum barrel rise (K).").Value,
            BarrelCoolTauS = F("BarrelCoolTauS", d.BarrelCoolTauS, "Barrel cool-down time constant (s); 3x is ~95 % cooled.").Value,
            WreckStartRiseK = F("WreckStartRiseK", d.WreckStartRiseK, "Hull rise when a vehicle is knocked out (K).").Value,
            WreckFireRiseK = F("WreckFireRiseK", d.WreckFireRiseK, "Hull rise while burning at full intensity (K).").Value,
            WreckFireWarmTauS = F("WreckFireWarmTauS", d.WreckFireWarmTauS, "Time constant for a fire heating the hull (s).").Value,
            WreckCoolSeconds = F("WreckCoolSeconds", d.WreckCoolSeconds, "Seconds for a wreck to cool to just above ambient (95 %).").Value,
            WreckResidualK = F("WreckResidualK", d.WreckResidualK, "Wreck rise left after cooling (K).").Value,
            EngineSpotRadiusM = F("EngineSpotRadiusM", d.EngineSpotRadiusM, "Engine spot radius when the part bounds are unusable (m).").Value,
            ExhaustSpotRadiusM = F("ExhaustSpotRadiusM", d.ExhaustSpotRadiusM, "Exhaust spot radius (m).").Value,
            BarrelSpotCalibers = F("BarrelSpotCalibers", d.BarrelSpotCalibers, "Barrel spot radius in calibers.").Value,
            PollHz = F("PollHz", d.PollHz, "Native polling rate for vehicles in view (0.5..10 Hz).").Value,
            MaxRangeM = F("MaxRangeM", d.MaxRangeM, "Vehicles farther than this are not polled or drawn (m).").Value,
            InvertSpotY = c.Bind("Heat", "InvertSpotY", d.InvertSpotY, "Flip hot spots vertically if they appear mirrored on the sensor image.").Value,
            FarSideWeight = F("FarSideWeight", d.FarSideWeight, "Fraction of engine/exhaust heat still shown when it sits on the far side of the hull (0 = hidden, as 0.4.0-0.4.2).").Value
        };
        s.ReadSurface((key, value, text) => F(key, value, text).Value);
        try { s.Validate(); Settings = s; }
        catch (Exception ex) { Settings = d; Plugin.Instance.Log.LogWarning("[Heat] " + ex.Message + " All [Heat] defaults used."); }
        Clear();
    }
    internal static void Clear() { entries.Clear(); stale.Clear(); alive.Clear(); logs = shotLogs = diagLogs = 0; cameraTransform = null; cameraId = IntPtr.Zero; cameraLabelId = IntPtr.Zero; cameraLabel = "none"; }

    // Called from the existing 1 Hz renderer refresh with the same vehicle list.
    internal static void BeginTrack() => alive.Clear();
    internal static void Track(Vehicle v)
    {
        if (!Settings.Enabled) return;
        alive.Add(v.Pointer);
        if (!entries.ContainsKey(v.Pointer)) entries[v.Pointer] = new Entry { V = v };
    }
    internal static void EndTrack()
    {
        stale.Clear();
        foreach (var key in entries.Keys) if (!alive.Contains(key)) stale.Add(key);
        foreach (var key in stale) entries.Remove(key);
    }

    private static void Resolve(Entry e)
    {
        e.ResolveAt = Time.unscaledTime + 5;
        e.Engines.Clear(); e.Exhausts.Clear(); e.Crew = null; e.Fire = null;
        var v = e.V; e.Vb = v.behaviour; e.Root = e.Vb != null ? e.Vb.transform : null;
        if (e.Vb == null || e.Root == null) return;
        var b = e.Vb.LocalBounds;
        float br = b.extents.magnitude;
        if (float.IsFinite(br) && br > .5f && br < 30 && b.center.magnitude < 30) { e.BoundsCenter = b.center; e.BoundsRadius = br; } else { e.BoundsCenter = Vector3.zero; e.BoundsRadius = 3; }
        var keep = new List<CannonRef>(e.Cannons); e.Cannons.Clear();
        var items = v.ObjectReader?.Items;
        if (items != null)
        {
            int n = items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for (int i = 0; i < n; i++)
            {
                var obj = items[i]; if (obj == null || obj.Released) continue;
                var list = obj.Components; if (list == null) continue;
                int m = list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for (int j = 0; j < m; j++)
                {
                    var c = list[j]; if (c == null || c.Vehicle?.Pointer != v.Pointer) continue;
                    var engine = c.TryCast<CombustionEngine>();
                    if (engine != null)
                    {
                        var behaviour = engine.Behaviour;
                        var eb = behaviour?.TryCast<EngineBehaviour>();
                        var r = new EngineRef { Part = engine, T = engine.transform, Eb = eb, Ro = eb == null ? behaviour?.TryCast<IReadOnlyPowertrainEngine>() : null };
                        var lb = engine.LocalBounds;
                        r.Radius = Settings.EngineSpotRadiusM;
                        if (HeatProjection.UsableBox(new NVec(lb.extents.x, 0, 0), new NVec(0, lb.extents.y, 0), new NVec(0, 0, lb.extents.z))
                            && float.IsFinite(lb.center.sqrMagnitude) && lb.center.sqrMagnitude < 900)
                        { r.Center = lb.center; r.Extents = lb.extents; }
                        if (r.Eb != null || r.Ro != null) e.Engines.Add(r);
                        continue;
                    }
                    var exhaust = c.TryCast<EngineExhaust>();
                    if (exhaust != null) { var parent = exhaust.OutputParent; if (parent != null) e.Exhausts.Add(new ExhaustRef { Parent = parent, Local = exhaust.OutputLocalPosition }); continue; }
                    var cannon = c.TryCast<Cannon>();
                    var cb = cannon?.Behaviour;
                    if (cb != null)
                    {
                        // Keep shot bookkeeping across re-resolves so a refresh never counts a phantom shot.
                        var old = keep.FirstOrDefault(k => k.B.Pointer == cb.Pointer);
                        float caliber = cb.Caliber / 1000f;
                        e.Cannons.Add(old ?? new CannonRef { B = cb, Radius = Math.Max(.08f, caliber * Settings.BarrelSpotCalibers) });
                        continue;
                    }
                }
            }
        }
        var modules = e.Vb.modules;
        if (modules != null)
            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i]; if (module == null) continue;
                e.Crew ??= module.TryCast<VehicleCrewWorkspaceModule>();
                e.Fire ??= module.TryCast<FireControlVehicleModule>();
            }
        if (Runtime.DetailedLogging && logs < 200 && !e.Logged)
        {
            logs++; e.Logged = true;
            Plugin.Instance.Log.LogInfo($"[Heat] Resolved vehicle {Name(e)}: engines={e.Engines.Count} (radius {(e.Engines.Count > 0 ? e.Engines[0].Radius : 0):F2} m), exhausts={e.Exhausts.Count}, cannons={e.Cannons.Count}, crewModule={e.Crew != null}, fireModule={e.Fire != null}, bounds={e.BoundsRadius:F1} m.");
        }
    }

    private static HeatInputs Sample(Entry e)
    {
        bool running = false; float load = 0;
        foreach (var r in e.Engines)
        {
            EngineState state; float rpm, idle, max, throttle;
            if (r.Eb != null) { state = r.Eb.State; rpm = r.Eb.RPM; idle = r.Eb.IdleRPM; max = r.Eb.MaxRPM; throttle = r.Eb.Throttle; }
            else { var ro = r.Ro!; state = ro.State; rpm = ro.RPM; idle = ro.IdleRPM; max = ro.MaxRPM; throttle = ro.Throttle; }
            if ((state & (EngineState.Running | EngineState.Starting)) == 0) continue;
            running = true; load = Math.Max(load, VehicleHeat.EngineLoad(throttle, rpm, idle, max));
        }
        // 0.4.3: shots were counted every frame by ObserveShots; the poll consumes them all.
        int shots = e.PendingShots; e.PendingShots = 0;
        bool wreck = false;
        var ops = e.Crew?.operators;
        if (ops != null && ops.Length > 0)
        {
            bool readable = true; int living = 0;
            for (int i = 0; i < ops.Length; i++)
            {
                var member = ops[i]?.internalOperator; if (member == null) { readable = false; continue; }
                float h = member.HealthFraction;
                if (!float.IsFinite(h)) { readable = false; continue; }
                if (h > 0) living++;
            }
            // Unreadable crew never proves a wreck (same rule as Expedition).
            wreck = readable && living == 0;
        }
        float fire = 0;
        bool onFire = e.Vb != null && (e.Vb.Flags & VehicleFlags.OnFire) != 0;
        if (e.Fire != null && e.Fire.OnFire) { onFire = true; fire = e.Fire.Intensity; }
        if (onFire && !(fire > 0)) fire = 1;
        return new HeatInputs(running, load, shots, wreck, fire);
    }

    /// <summary>Poll in-view vehicles at PollHz and refresh their vehicle-local spot list.</summary>
    internal static void Poll(Camera cam)
    {
        try { ObserveShots(); }
        catch (Exception ex) { Runtime.Warn($"Heat shot counting skipped: {ex.GetType().Name}: {ex.Message}", "heat-shots"); }
        try { PollInternal(cam); }
        catch (Exception ex) { Runtime.Warn($"Heat polling skipped: {ex.GetType().Name}: {ex.Message}", "heat-poll"); }
    }
    // 0.4.3: every frame, two read-only getters per resolved cannon of every tracked vehicle (in view or not),
    // so shots between polls, before the first sighting or on the own (often out-of-view) vehicle are not lost.
    private static void ObserveShots()
    {
        if (!Settings.Enabled) return;
        foreach (var e in entries.Values)
        {
            if (e.Failed || e.Cannons.Count == 0) continue;
            try { foreach (var c in e.Cannons) e.PendingShots += c.Shots.Observe(c.B.LastFireTime, (int)c.B.LoadState); }
            catch (Exception ex) { e.Failed = true; Runtime.Warn($"Heat model disabled for one vehicle (shot counter): {ex.GetType().Name}: {ex.Message}", "heat-vehicle-shots"); }
        }
    }
    private static void PollInternal(Camera cam)
    {
        var s = Settings;
        if (!s.Enabled || Time.unscaledTime < pollAt) return;
        pollAt = Time.unscaledTime + 1 / s.PollHz;
        var camT = CameraTransform(cam); if (camT == null) return;
        foreach (var e in entries.Values)
        {
            if (e.Failed) continue;
            try
            {
                if (e.Root == null && Time.unscaledTime >= e.ResolveAt) Resolve(e);
                if (e.Vb == null || e.Root == null) continue;
                var vp = cam.WorldToViewportPoint(e.Root.position);
                // 0.4.3: vehicles close to the camera (the own vehicle) are always polled; others only in view.
                bool near = (e.Root.position - camT.position).sqrMagnitude < (e.BoundsRadius + 15) * (e.BoundsRadius + 15);
                if (!near && (!(vp.z > 0) || vp.z > s.MaxRangeM || vp.x < -.25f || vp.x > 1.25f || vp.y < -.25f || vp.y > 1.25f)) continue;
                // Part lists are refreshed only for vehicles in view.
                if (Time.unscaledTime >= e.ResolveAt) Resolve(e);
                if (e.Vb == null || e.Root == null) continue;
                var input = Sample(e);
                // Not-polled time (out of view, sight off) is extrapolated with the current inputs.
                float now = Time.time;
                // 0.4.3: shots counted before the first sighting are kept (0.4.2 dropped them here).
                if (!e.Seen) { e.Heat.Initialize(input, s); e.Seen = true; }
                else e.Heat.Step(now - e.PolledAt, input, s);
                e.PolledAt = now; e.Last = input;
                BuildSpots(e, s);
                if (input.Shots > 0 && Runtime.DetailedLogging && shotLogs < 50)
                { shotLogs++; Plugin.Instance.Log.LogInfo($"[Heat] Shot detected on {Name(e)}: barrel {s.AmbientC + e.Heat.BarrelK:F0} C after {e.Heat.TotalShots} shots."); }
                if (Runtime.DetailedLogging && logs < 200 && Time.unscaledTime >= e.LogAt)
                {
                    logs++; e.LogAt = Time.unscaledTime + 10;
                    Plugin.Instance.Log.LogInfo($"[Heat] {Name(e)}: engine {s.AmbientC + e.Heat.EngineK:F0} C (running={input.EngineRunning}, load={input.EngineLoad:F2}), exhaust {s.AmbientC + e.Heat.ExhaustK:F0} C, barrel {s.AmbientC + e.Heat.BarrelK:F0} C, wreck {s.AmbientC + e.Heat.WreckK:F0} C (wreck={input.Wreck}, fire={input.Fire:F2}), spots in view={e.InView}/{e.Count}.");
                }
            }
            catch (Exception ex) { e.Failed = true; Runtime.Warn($"Heat model disabled for one vehicle: {ex.GetType().Name}: {ex.Message}", "heat-vehicle"); }
        }
    }

    private static void BuildSpots(Entry e, HeatSettings s)
    {
        var root = e.Root!; int n = 0; var h = e.Heat;
        void Add(Vector3 world, float radius, float kelvin, float scale, Kind kind)
        {
            float amp = scale * kelvin / s.FullScaleKelvin;
            if (n >= SpotsPerVehicle || !(amp > .002f)) return;
            e.Local[n] = root.InverseTransformPoint(world); e.Radius[n] = radius; e.Amp[n] = amp; e.Kinds[n] = kind; n++;
        }
        Add(root.TransformPoint(e.BoundsCenter), e.BoundsRadius, h.WreckK, s.WreckScale, Kind.Wreck);
        foreach (var r in e.Engines)
        {
            int index = n;
            Add(r.T.TransformPoint(r.Center), r.Radius, h.EngineK, s.EngineScale, Kind.Engine);
            if (n == index) continue;
            // Keep the oriented half-axes in vehicle-local space alongside the centre between polls.
            var ex = r.Extents;
            e.AxisX[index] = root.InverseTransformPoint(r.T.TransformPoint(r.Center + new Vector3(ex.x, 0, 0))) - e.Local[index];
            e.AxisY[index] = root.InverseTransformPoint(r.T.TransformPoint(r.Center + new Vector3(0, ex.y, 0))) - e.Local[index];
            e.AxisZ[index] = root.InverseTransformPoint(r.T.TransformPoint(r.Center + new Vector3(0, 0, ex.z))) - e.Local[index];
        }
        foreach (var x in e.Exhausts) Add(x.Parent.TransformPoint(x.Local), s.ExhaustSpotRadiusM, h.ExhaustK, s.ExhaustScale, Kind.Exhaust);
        foreach (var c in e.Cannons)
        {
            // Strongest at the chamber and the muzzle, weaker mid-barrel.
            Vector3 breech = c.B.LoadPosition, muzzle = c.B.ShellSpawnPosition;
            Add(breech, c.Radius, h.BarrelK, s.BarrelScale, Kind.Barrel);
            Add(Vector3.Lerp(breech, muzzle, .5f), c.Radius, h.BarrelK * .5f, s.BarrelScale, Kind.Barrel);
            Add(muzzle, c.Radius, h.BarrelK * .85f, s.BarrelScale, Kind.Barrel);
        }
        e.Count = n;
    }

    /// <summary>
    /// Project the current spots onto the sensor grid with the capture camera: the camera of the
    /// DrawRenderersCustomPass HDCamera that draws the mask, and its own matrices. No allocation.
    /// </summary>
    internal static int Project(Camera cam, HeatSpot[] output, int width, int height)
    {
        // A heat failure never disables the sensor: the image falls back to hull-only heat.
        try { return ProjectInternal(cam, output, width, height); }
        catch (Exception ex) { Runtime.Warn($"Heat projection skipped: {ex.GetType().Name}: {ex.Message}", "heat-project-all"); return 0; }
    }
    private static float cameraFov; private static bool cameraPhysical;
    private static int ProjectInternal(Camera cam, HeatSpot[] output, int width, int height)
    {
        var s = Settings; int total = 0;
        if (!s.Enabled) return 0;
        var camT = CameraTransform(cam); if (camT == null) return 0;
        // 0.4.3: projectionMatrix * worldToCameraMatrix of the mask camera (also right for physical/focal-length
        // cameras); 0.4.0-0.4.2 derived the spot size from fieldOfView.
        var m = cam.projectionMatrix * cam.worldToCameraMatrix;
        var sc = new SpotCamera(new NMatrix(m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33),
            N(camT.position), N(camT.right), N(camT.up));
        rejectRange = rejectBehind = rejectFar = rejectOff = rejectSmall = 0;
        if (Runtime.DetailedLogging)
        {
            if (cam.Pointer != cameraLabelId) { cameraLabelId = cam.Pointer; cameraLabel = cam.name + "#" + cam.GetInstanceID(); }
            cameraFov = cam.fieldOfView; cameraPhysical = cam.usePhysicalProperties;
        }
        foreach (var e in entries.Values)
        {
            e.InView = 0; e.DiagIndex = -1; e.DiagEngine = false; e.DiagReason = SpotReject.None;
            if (e.Failed || e.Count == 0 || e.Root == null) continue;
            try
            {
                var center = N(e.Root.TransformPoint(e.BoundsCenter));
                for (int i = 0; i < e.Count && total < output.Length; i++)
                {
                    var kind = e.Kinds[i];
                    SpotReject r; float vx, vy, depth;
                    if (kind == Kind.Engine)
                    {
                        var world = e.Root.TransformPoint(e.Local[i]);
                        r = HeatProjection.EvaluateBox(sc, N(world),
                            N(e.Root.TransformPoint(e.Local[i] + e.AxisX[i]) - world),
                            N(e.Root.TransformPoint(e.Local[i] + e.AxisY[i]) - world),
                            N(e.Root.TransformPoint(e.Local[i] + e.AxisZ[i]) - world), e.Radius[i], e.Amp[i], center,
                            s.FarSideWeight, s.MaxRangeM, width, height, s.InvertSpotY, out output[total], out vx, out vy, out depth);
                    }
                    else r = HeatProjection.Evaluate(sc, N(e.Root.TransformPoint(e.Local[i])), e.Radius[i], e.Amp[i], kind == Kind.Exhaust, center,
                        s.FarSideWeight, s.MaxRangeM, width, height, kind == Kind.Wreck, s.InvertSpotY, out output[total], out vx, out vy, out depth);
                    if (kind == Kind.Engine && !e.DiagEngine)
                    {
                        e.DiagEngine = true; e.DiagReason = r; e.DiagDepth = depth;
                        e.DiagX = vx * width - .5f; e.DiagY = (s.InvertSpotY ? 1 - vy : vy) * height - .5f;
                        if (r == SpotReject.None) e.DiagIndex = total;
                    }
                    switch (r)
                    {
                        case SpotReject.None: total++; e.InView++; break;
                        case SpotReject.OutOfRange: rejectRange++; break;
                        case SpotReject.BehindCamera: rejectBehind++; break;
                        case SpotReject.FarSide: rejectFar++; break;
                        case SpotReject.OffScreen: rejectOff++; break;
                        default: rejectSmall++; break;
                    }
                }
            }
            catch (Exception ex) { e.Failed = true; Runtime.Warn($"Heat projection disabled for one vehicle: {ex.GetType().Name}: {ex.Message}", "heat-project"); }
        }
        if (Runtime.DetailedLogging && logs < 200 && Time.unscaledTime >= summaryAt)
        {
            logs++; summaryAt = Time.unscaledTime + 10;
            Plugin.Instance.Log.LogInfo($"[Heat] Hot spots in view: {total} from {entries.Count} tracked vehicles (sensor {width}x{height}); rejected: behind-camera={rejectBehind}, off-screen={rejectOff}, far-side={rejectFar}, out-of-range={rejectRange}, negligible={rejectSmall}; camera={cameraLabel} fov={cameraFov:F2} physical={cameraPhysical}.");
        }
        return total;
    }

    /// <summary>
    /// 0.4.3 diagnostics after the readback of the same capture: per vehicle the screen position of the
    /// engine spot, why it was rejected and how many vehicle-mask pixels lie under it. Rate-limited.
    /// </summary>
    internal static void DiagnoseMask(byte[] mask, HeatSpot[] spots, int count, int width, int height)
    {
        if (!Runtime.DetailedLogging || diagLogs >= 150) return;
        try
        {
            float now = Time.unscaledTime;
            foreach (var e in entries.Values)
            {
                if (diagLogs >= 150 || now < e.DiagAt || e.Failed || e.Root == null) continue;
                e.DiagAt = now + 10; diagLogs++;
                if (!e.DiagEngine)
                {
                    Plugin.Instance.Log.LogInfo($"[Heat] Diag {Name(e)}: no engine spot (engines={e.Engines.Count}, engine rise {e.Heat.EngineK:F0} K, spots={e.Count}); camera={cameraLabel}.");
                    continue;
                }
                var reason = e.DiagReason; int pixels = -1;
                if (reason == SpotReject.None && e.DiagIndex >= 0 && e.DiagIndex < count)
                { pixels = HeatProjection.MaskPixels(mask, width, height, spots[e.DiagIndex]); if (pixels == 0) reason = SpotReject.NoVehiclePixels; }
                Plugin.Instance.Log.LogInfo($"[Heat] Diag {Name(e)}: engine spot screen=({e.DiagX:F0}, {e.DiagY:F0}) px on {width}x{height} (row 0 = bottom), depth {e.DiagDepth:F0} m, result={Reason(reason)}, vehiclePixels={(pixels < 0 ? "n/a" : pixels.ToString())}, spots in view={e.InView}/{e.Count}; camera={cameraLabel} fov={cameraFov:F2}.");
            }
        }
        catch (Exception ex) { Runtime.Warn($"Heat diagnostics skipped: {ex.GetType().Name}: {ex.Message}", "heat-diag"); }
    }
    private static string Reason(SpotReject r) => r switch
    {
        SpotReject.None => "in-view", SpotReject.OutOfRange => "out-of-range", SpotReject.BehindCamera => "behind-camera",
        SpotReject.FarSide => "far-side", SpotReject.OffScreen => "off-screen", SpotReject.NoVehiclePixels => "no-vehicle-pixels", _ => "negligible"
    };
    private static NVec N(Vector3 v) => new(v.x, v.y, v.z);

    private static Transform? CameraTransform(Camera cam)
    {
        if (cam.Pointer != cameraId || cameraTransform == null) { cameraId = cam.Pointer; cameraTransform = cam.transform; }
        return cameraTransform;
    }
    private static string Name(Entry e) { try { return e.V.name + "#" + e.V.GetInstanceID(); } catch { return "vehicle"; } }
}
