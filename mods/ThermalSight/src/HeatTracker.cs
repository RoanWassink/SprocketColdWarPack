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

namespace SprocketThermalSight;

// 0.4.0 heat sources, read-only: native fields are polled (<= 10 Hz, vehicles in view only).
// No Harmony patch and no native writes; wrappers are cached at resolve time (every 5 s).
internal static class HeatTracker
{
    internal const int MaxSpots = 64;
    private const int SpotsPerVehicle = 16;
    private enum Kind : byte { Engine, Exhaust, Barrel, Wreck }
    private sealed class EngineRef { internal CombustionEngine Part = null!; internal Transform T = null!; internal EngineBehaviour? Eb; internal IReadOnlyPowertrainEngine? Ro; internal Vector3 Center; internal float Radius; }
    private sealed class ExhaustRef { internal Transform Parent = null!; internal Vector3 Local; }
    private sealed class CannonRef { internal CannonBehaviour B = null!; internal float LastFire; internal LoadState LastState; internal float Radius; }
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
        internal readonly float[] Radius = new float[SpotsPerVehicle], Amp = new float[SpotsPerVehicle];
        internal readonly Kind[] Kinds = new Kind[SpotsPerVehicle];
        internal int Count, InView;
    }
    private static readonly Dictionary<IntPtr, Entry> entries = new();
    private static readonly List<IntPtr> stale = new();
    private static readonly HashSet<IntPtr> alive = new();
    internal static HeatSettings Settings { get; private set; } = new();
    private static float pollAt, summaryAt;
    private static int logs, shotLogs;
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
            InvertSpotY = c.Bind("Heat", "InvertSpotY", d.InvertSpotY, "Flip hot spots vertically if they appear mirrored on the sensor image.").Value
        };
        try { s.Validate(); Settings = s; }
        catch (Exception ex) { Settings = d; Plugin.Instance.Log.LogWarning("[Heat] " + ex.Message + " All [Heat] defaults used."); }
        Clear();
    }
    internal static void Clear() { entries.Clear(); stale.Clear(); alive.Clear(); logs = shotLogs = 0; cameraTransform = null; cameraId = IntPtr.Zero; }

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
                        var lb = engine.LocalBounds; float er = lb.extents.magnitude * .6f;
                        if (float.IsFinite(er) && er > .2f && er < 4) { r.Center = lb.center; r.Radius = er; } else { r.Center = Vector3.zero; r.Radius = Settings.EngineSpotRadiusM; }
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
                        e.Cannons.Add(old ?? new CannonRef { B = cb, LastFire = cb.LastFireTime, LastState = cb.LoadState, Radius = Math.Max(.08f, caliber * Settings.BarrelSpotCalibers) });
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
        int shots = 0;
        foreach (var c in e.Cannons)
        {
            float fired = c.B.LastFireTime; var state = c.B.LoadState;
            bool timeChanged = fired != c.LastFire;
            bool dropped = c.LastState == LoadState.Loaded && (state == LoadState.Loading || state == LoadState.Unloaded);
            // Either signal is one shot per poll; both together still count once.
            if (timeChanged || dropped) shots++;
            c.LastFire = fired; c.LastState = state;
        }
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
        try { PollInternal(cam); }
        catch (Exception ex) { Runtime.Warn($"Heat polling skipped: {ex.GetType().Name}: {ex.Message}", "heat-poll"); }
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
                if (!(vp.z > 0) || vp.z > s.MaxRangeM || vp.x < -.25f || vp.x > 1.25f || vp.y < -.25f || vp.y > 1.25f) continue;
                // Part lists are refreshed only for vehicles in view.
                if (Time.unscaledTime >= e.ResolveAt) Resolve(e);
                if (e.Vb == null || e.Root == null) continue;
                var input = Sample(e);
                // Not-polled time (out of view, sight off) is extrapolated with the current inputs.
                float now = Time.time;
                if (!e.Seen) { e.Heat.Initialize(input with { Shots = 0 }, s); e.Seen = true; }
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
        foreach (var r in e.Engines) Add(r.T.TransformPoint(r.Center), r.Radius, h.EngineK, s.EngineScale, Kind.Engine);
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

    /// <summary>Project the current spots with the sight camera onto the sensor grid. No allocation.</summary>
    internal static int Project(Camera cam, HeatSpot[] output, int width, int height)
    {
        // A heat failure never disables the sensor: the image falls back to hull-only heat.
        try { return ProjectInternal(cam, output, width, height); }
        catch (Exception ex) { Runtime.Warn($"Heat projection skipped: {ex.GetType().Name}: {ex.Message}", "heat-project-all"); return 0; }
    }
    private static int ProjectInternal(Camera cam, HeatSpot[] output, int width, int height)
    {
        var s = Settings; int total = 0;
        if (!s.Enabled) return 0;
        var camT = CameraTransform(cam); if (camT == null) return 0;
        Vector3 cp = camT.position;
        float tanHalf = MathF.Tan(cam.fieldOfView * MathF.PI / 360f), aspect = cam.aspect;
        foreach (var e in entries.Values)
        {
            e.InView = 0;
            if (e.Failed || e.Count == 0 || e.Root == null) continue;
            try
            {
                Vector3 center = e.Root.TransformPoint(e.BoundsCenter);
                if ((center - cp).sqrMagnitude > s.MaxRangeM * s.MaxRangeM) continue;
                for (int i = 0; i < e.Count && total < output.Length; i++)
                {
                    Vector3 w = e.Root.TransformPoint(e.Local[i]);
                    float amp = e.Amp[i];
                    if (e.Kinds[i] is Kind.Engine or Kind.Exhaust)
                    {
                        var o = w - center; var c = cp - w;
                        amp *= HeatProjection.Facing(o.x, o.y, o.z, c.x, c.y, c.z);
                    }
                    var vp = cam.WorldToViewportPoint(w);
                    if (HeatProjection.TryProject(vp.x, vp.y, vp.z, e.Radius[i], tanHalf, aspect, width, height, amp, e.Kinds[i] == Kind.Wreck, s.InvertSpotY, out output[total]))
                    { total++; e.InView++; }
                }
            }
            catch (Exception ex) { e.Failed = true; Runtime.Warn($"Heat projection disabled for one vehicle: {ex.GetType().Name}: {ex.Message}", "heat-project"); }
        }
        if (Runtime.DetailedLogging && logs < 200 && Time.unscaledTime >= summaryAt)
        { logs++; summaryAt = Time.unscaledTime + 10; Plugin.Instance.Log.LogInfo($"[Heat] Hot spots in view: {total} from {entries.Count} tracked vehicles (sensor {width}x{height})."); }
        return total;
    }

    private static Transform? CameraTransform(Camera cam)
    {
        if (cam.Pointer != cameraId || cameraTransform == null) { cameraId = cam.Pointer; cameraTransform = cam.transform; }
        return cameraTransform;
    }
    private static string Name(Entry e) { try { return e.V.name + "#" + e.V.GetInstanceID(); } catch { return "vehicle"; } }
}
