using Il2CppInterop.Runtime;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Control;
using Sprocket.Vehicles.Powertrains;
using Sprocket.Vehicles.Weapons.Cannons;
using Sprocket.WeaponFramework;
using SprocketTelescopicMast.Io;
using UnityEngine;
namespace SprocketTelescopicMast;

/// <summary>
/// Runtime context handed to every input type for one vehicle tick: the pure IVehicleSignals
/// snapshot (shots, cannon states, other hinges, masts, keys) plus the vehicle itself for
/// input types from other mods. The engine never looks inside.
/// </summary>
public sealed class VehicleIoContext : IVehicleSignals, IExternalVehicleContext, IRotatorSignals
{
    public VehicleBehaviour? Vehicle { get; private set; }
    public object? VehicleToken => Vehicle != null ? Vehicle.gameObject : null;
    public bool IsPlayer { get; private set; }
    public bool Braking { get; private set; }
    public double Now { get; private set; }
    public IReadOnlyList<CannonShot> Fired => fired;
    public IReadOnlyList<CannonState> Cannons => cannons;
    public IReadOnlyList<HingeState> Hinges => hinges;
    public IReadOnlyList<MastState> Masts => masts;
    public IReadOnlyList<RotatorState> Rotators => rotators;
    private readonly List<RotatorState> rotators = new();
    private readonly List<CannonShot> fired = new();
    private readonly List<CannonState> cannons = new();
    private readonly List<HingeState> hinges = new();
    private readonly List<MastState> masts = new();
    private readonly int[] presses = new int[KeyInput.Actions + 1];
    private readonly bool[] held = new bool[KeyInput.Actions + 1];

    public int KeyPresses(int action) => action >= 1 && action <= KeyInput.Actions ? presses[action] : 0;
    public bool KeyHeld(int action) => action >= 1 && action <= KeyInput.Actions && held[action];

    internal void Begin(VehicleBehaviour vehicle, double now, IEnumerable<HingeState> hingeStates)
    {
        Vehicle = vehicle; Now = now;
        IsPlayer = vehicle.ControlType == ControlType.Player;
        Braking = HingeBraking.Read(vehicle);
        fired.Clear(); HingeFire.Take(vehicle.Pointer, fired);
        cannons.Clear(); HingeCannons.Collect(vehicle.Pointer, now, cannons);
        hinges.Clear(); hinges.AddRange(hingeStates);
        masts.Clear(); MastRuntime.Collect(vehicle.Pointer, masts);
        RotatorRuntime.Collect(vehicle.Pointer, HingeRuntime.PartId, rotators);
        Array.Clear(presses); Array.Clear(held);
        if (IsPlayer) HingeKeys.Take(presses, held);
    }
    internal void End() { Vehicle = null; }
}

// ---------- Built-in output type (the input types live in Io/BuiltinInputs.cs) ----------

public sealed class HingeOutput : IOutputType
{
    public string Id => "hinge";
    public string DisplayName => "Hinge";
    public SignalKind SignalKind => SignalKind.Level;
    public ParamSchema Schema { get; } = new(new[]
    {
        ParamDef.Float("closedAngle", "Closed angle", -180, 180, 0, 5),
        ParamDef.Float("openAngle", "Open angle", -180, 180, 90, 5),
        ParamDef.Float("speed", "Speed deg/s", 5, 720, 90, 15)
    });
    public IReadOnlyList<IoCommand> Commands { get; } = new[] { IoCommand.Open, IoCommand.Close, IoCommand.Toggle, IoCommand.Pulse, IoCommand.Follow };
    public string Describe(IReadOnlyDictionary<string, string> v) => "Hinge";
}

// ---------- Braking input: read-only powertrain snapshot ----------

internal static class HingeBraking
{
    private static readonly HashSet<int> Warned = new();

    /// <summary>
    /// Current driver brake state. ClutchBraking supplies both brake fractions; any other
    /// PowertrainBehaviour layout can still use action/lastAction (the base fields).
    /// Zero side effects: no Harmony hook and no write to native state.
    /// </summary>
    internal static bool Read(VehicleBehaviour? vehicle)
    {
        if (vehicle == null) return false;
        try
        {
            var modules = vehicle.Modules;
            if (modules == null)
            {
                Warn(vehicle, "no powertrain modules");
                return false;
            }
            int moduleCount = modules.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleModule>>().Count;
            bool sawAnyModule = moduleCount > 0;
            for (int i = 0; i < moduleCount; i++)
            {
                var module = modules[i]?.TryCast<VehiclePowertrainBehaviourModule>();
                if (module == null) continue;
                var powertrain = module.powertrain;
                if (powertrain == null) continue;
                var fields = new BrakingFields(true, (int)powertrain.action, true, (int)powertrain.lastAction, false, 0, 0);
                var clutch = powertrain.TryCast<ClutchBraking>();
                if (clutch != null)
                {
                    var job = clutch.updateJob;
                    fields = fields with
                    {
                        HasFractions = true,
                        LeftBrakeFraction = job.leftBrakeFraction,
                        RightBrakeFraction = job.rightBrakeFraction
                    };
                }
                if (BrakingPolicy.IsBraking(fields)) return true;
            }
            if (!sawAnyModule) Warn(vehicle, "no powertrain module");
            return false;
        }
        catch (Exception ex)
        {
            Warn(vehicle, ex.Message);
            return false;
        }
    }

    private static void Warn(VehicleBehaviour vehicle, string reason)
    {
        try
        {
            int id = vehicle.GetInstanceID();
            if (!Warned.Add(id)) return;
            Plugin.Instance.Log.LogWarning($"[IO] vehicle.braking unavailable on vehicle {id}: {reason}. The input stays false.");
        }
        catch { }
    }
}

// ---------- Key accumulation (polled once per frame from the host MonoBehaviour) ----------

internal static class HingeKeys
{
    private static readonly int[] presses = new int[KeyInput.Actions + 1];
    private static readonly bool[] held = new bool[KeyInput.Actions + 1];
    private static float lastTaken;
    internal static bool Suspended; // while the panel's name field has focus
    internal static void Poll()
    {
        if (Plugin.ActionBindings == null) return;
        // Nobody consumed presses recently (no player hinge): drop them instead of replaying later.
        if (Time.unscaledTime - lastTaken > .5f) Array.Clear(presses);
        for (int i = 1; i <= KeyInput.Actions; i++)
        {
            var b = Plugin.ActionBindings[i - 1];
            if (b == null || Suspended) { held[i] = false; continue; }
            held[i] = b.IsHeld;
            if (b.WasPressedThisFrame && presses[i] < 8) presses[i]++;
        }
    }
    /// <summary>Snapshot for the player's vehicle tick; each press is delivered exactly once.</summary>
    internal static void Take(int[] pressInto, bool[] heldInto)
    {
        lastTaken = Time.unscaledTime;
        Array.Copy(presses, pressInto, presses.Length); Array.Clear(presses);
        Array.Copy(held, heldInto, held.Length);
    }
}


// ---------- Cannon registry and load-state polling ----------

internal static class HingeCannons
{
    private sealed class Entry
    {
        internal Cannon Cannon = null!;
        internal IntPtr Vehicle, Root;
        internal int Vuid, Caliber;
        internal float NextResolve;
        internal readonly CannonLoadSampler Sampler = new();
        internal bool Logged;
    }
    private static readonly Dictionary<IntPtr, Entry> Entries = new();
    private static int reloadTraces;

    internal static void Track(Cannon cannon)
    {
        if (cannon == null) return;
        if (!Entries.TryGetValue(cannon.Pointer, out var e)) Entries[cannon.Pointer] = e = new Entry { Cannon = cannon };
        e.Cannon = cannon; e.NextResolve = 0;
    }
    internal static void Forget(Cannon cannon) { if (cannon != null) Entries.Remove(cannon.Pointer); }

    internal static int Mask(Cannon cannon)
    {
        try { return cannon.InstanceBlueprint?.FireGroup ?? 0; } catch { return 0; }
    }
    internal static int Vuid(Cannon cannon)
    {
        try { return cannon.VUID.Value; } catch { return 0; }
    }

    private static void Resolve(Entry e)
    {
        if (Time.unscaledTime < e.NextResolve) return;
        e.NextResolve = Time.unscaledTime + 2f;
        e.Vehicle = e.Cannon.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer ?? IntPtr.Zero;
        e.Root = e.Cannon.Vehicle?.Pointer ?? IntPtr.Zero;
        e.Vuid = Vuid(e.Cannon);
        try { e.Caliber = e.Cannon.Blueprint?.Caliber ?? 0; } catch { e.Caliber = 0; }
    }

    private static LoadPhase Phase(LoadState state) => state switch
    {
        LoadState.Loaded => LoadPhase.Loaded,
        LoadState.Loading => LoadPhase.Loading,
        LoadState.NeverLoaded => LoadPhase.NeverLoaded,
        _ => LoadPhase.Unloaded
    };

    internal static void Collect(IntPtr vehicle, double now, List<CannonState> output)
    {
        foreach (var pair in Entries.ToArray())
        {
            var e = pair.Value;
            try
            {
                if (e.Cannon == null || e.Cannon.WasCollected) { Entries.Remove(pair.Key); continue; }
                Resolve(e);
                if (e.Vehicle != vehicle) continue;
                var behaviour = e.Cannon.Behaviour;
                if (behaviour == null) continue;
                var state = behaviour.LoadState;
                int mask = Mask(e.Cannon);
                var sample = e.Sampler.Sample(e.Vuid, mask, Phase(state), now);
                int reloaded = sample.ReloadedEvents;
                output.Add(sample);
                if (reloaded > 0 && Plugin.Detailed && reloadTraces++ < 100)
                    Plugin.Trace($"[IO RELOADED] vuid={e.Vuid} vehicle={vehicle} group={FireGroups.Describe(mask)} t={now:F2}.");
                if (!e.Logged && Plugin.Detailed)
                {
                    e.Logged = true;
                    // Measuring point 1: blueprint bitmask versus the behaviour's int field.
                    Plugin.Trace($"[IO CANNON] vuid={e.Vuid} caliber={e.Caliber}mm vehicle={vehicle} blueprintFireGroup={mask} ({FireGroups.Describe(mask)}) behaviourFireGroup={behaviour.FireGroup} loadState={state}.");
                }
            }
            catch (Exception ex) { HingeRuntime.Warn("Cannon poll", ex); Entries.Remove(pair.Key); }
        }
    }

    /// <summary>
    /// Panel options: "Cannon 2 - 75 mm (group 1)" for the cannons of the given vehicles (gateway pointers).
    /// Only matching vehicle roots are offered. Numbering follows VUID order.
    /// </summary>
    internal static List<ParamOption> Options(ICollection<IntPtr> roots, Dictionary<string, int>? firstGroups = null, Dictionary<string, int>? fireMasks = null)
    {
        var live = new List<Entry>();
        foreach (var pair in Entries.ToArray())
        {
            var e = pair.Value;
            try
            {
                if (e.Cannon == null || e.Cannon.WasCollected) { Entries.Remove(pair.Key); continue; }
                Resolve(e);
                if (e.Vuid != 0) live.Add(e);
            }
            catch (Exception ex) { HingeRuntime.Warn("Cannon list", ex); }
        }
        var mine = live.Where(e => roots.Contains(e.Root)).ToList();
        // Never offer a cannon from a different vehicle when this vehicle has no tracked cannon yet.
        var options = new List<ParamOption>();
        int n = 0;
        foreach (var e in mine.GroupBy(e => e.Vuid).Select(g => g.First()).OrderBy(e => e.Vuid))
        {
            int mask = Mask(e.Cannon);
            var key = PartSelector.ForVuid(e.Vuid);
            if (fireMasks != null) fireMasks[key] = mask;
            options.Add(new ParamOption(key, $"Cannon {++n} - {(e.Caliber > 0 ? e.Caliber + " mm" : "n/a mm")} (group {FireGroups.Describe(mask)})"));
            if (firstGroups != null)
                for (int g = 1; g <= FireGroups.Max; g++) if (FireGroups.Contains(mask, g)) { firstGroups[key] = g; break; }
        }
        return options;
    }
}

// ---------- Fire detection: ProjectileRegister.nextID around CannonBehaviour.FireInternal ----------

internal static class HingeFire
{
    // Prefix sample, per thread (Harmony runs the pair on the calling thread). A single slot is
    // enough: the prefix overwrites it on every call, so a skipped postfix cannot leak a sample.
    [ThreadStatic] private static ProjectileRegister? sampleRegister;
    [ThreadStatic] private static int sampleBefore;

    private static readonly CannonShotBuffer Pending = new();
    private static readonly DuplicateFilter Duplicates = new(.15);
    private static int traceCount;

    internal static void BeginFire(CannonBehaviour behaviour)
    {
        sampleRegister = null;
        try
        {
            // CannonBehaviour.projectileFactory is the shared ProjectileRegister; nextID is the
            // projectile id counter that Launch advances only after allocating the projectile.
            var register = behaviour.projectileFactory?.TryCast<ProjectileRegister>();
            if (register == null) return;
            sampleRegister = register;
            sampleBefore = register.nextID;
        }
        catch (Exception ex) { HingeRuntime.Warn("Fire counter", ex); }
    }

    internal static void EndFire(CannonBehaviour behaviour)
    {
        var sampled = sampleRegister;
        int before = sampleBefore;
        int after = before;
        bool launched = false;
        try
        {
            if (sampled != null)
            {
                var register = behaviour.projectileFactory?.TryCast<ProjectileRegister>();
                if (register != null && register.Pointer == sampled.Pointer)
                {
                    after = register.nextID;
                    launched = FireGate.Launched(before, after);
                }
            }
        }
        catch (Exception ex) { HingeRuntime.Warn("Fire counter", ex); }
        finally { sampleRegister = null; }

        if (!launched) return;
        var cannon = behaviour.mount?.TryCast<Cannon>();
        if (cannon == null) return;
        bool accepted = FireGate.Accept(launched, cannon.Pointer.ToInt64(), Time.time, Duplicates);
        int mask = HingeCannons.Mask(cannon);
        var vehicle = cannon.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer ?? IntPtr.Zero;
        if (Plugin.Detailed && traceCount++ < 200)
            // Measuring point 2 (and 1): blocked launches and fire-group fields.
            Plugin.Trace($"[IO FIRE] vuid={cannon.VUID.Value} vehicle={vehicle} nextID={before}->{after} blueprintFireGroup={mask} ({FireGroups.Describe(mask)}) behaviourFireGroup={behaviour.FireGroup} counted={accepted}.");
        if (!accepted || vehicle == IntPtr.Zero || !HingeRuntime.AnyLeaves) return;
        // Only queue for a vehicle that actually has an I/O sink; otherwise the shot would sit
        // in Pending forever (the old per-vehicle GameUpdate tick cleared those).
        var io = HingeRuntime.IoFor(vehicle);
        if (io == null || (io.Leaves.Count == 0 && io.External.Count == 0 && !HingeRuntime.HasFlowParts(io.Vehicle))) return;
        Pending.Add(vehicle, new CannonShot(HingeCannons.Vuid(cannon), mask));
    }

    internal static void Take(IntPtr vehicle, List<CannonShot> into)
    {
        Pending.Take(vehicle, into);
    }
    internal static void Discard(IntPtr vehicle) => Pending.Discard(vehicle);
}
