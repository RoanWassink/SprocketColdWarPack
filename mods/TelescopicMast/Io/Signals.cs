using System.Globalization;
namespace SprocketTelescopicMast.Io;

// Pure per-vehicle signal snapshot used by the built-in inputs. The game adapter (VehicleIoContext)
// fills it once per tick; tests use a fake. No Unity or IL2CPP types here.

/// <summary>A cannon that really fired since the previous tick.</summary>
public readonly record struct CannonShot(int Vuid, int FireGroupMask);

/// <summary>Filtered state of one cannon this tick. ReloadedEvents: Loading -> Loaded edges this tick.</summary>
public readonly record struct CannonState(int Vuid, int FireGroupMask, bool Loading, bool NotReady, bool Loaded, int ReloadedEvents);

/// <summary>Another hinge on the same vehicle, as seen at the end of the previous tick.</summary>
public readonly record struct HingeState(int Vuid, string Name, bool OpenReached, bool ClosedReached);

/// <summary>A telescopic mast on the same vehicle: extension as a fraction of its stroke (0 = retracted).</summary>
public readonly record struct MastState(float Fraction);

public interface IVehicleSignals
{
    bool IsPlayer { get; }
    /// <summary>Driver/vehicle braking signal, read from the powertrain this tick.</summary>
    bool Braking { get; }
    IReadOnlyList<CannonShot> Fired { get; }
    IReadOnlyList<CannonState> Cannons { get; }
    IReadOnlyList<HingeState> Hinges { get; }
    IReadOnlyList<MastState> Masts { get; }
    /// <summary>I/O Flow / Action N presses this tick (player vehicle only, else 0).</summary>
    int KeyPresses(int action);
    bool KeyHeld(int action);
}

/// <summary>Native weapon load state, mirrored so the reload logic stays testable.</summary>
public enum LoadPhase { Loaded, Loading, Unloaded, NeverLoaded }

/// <summary>
/// Emits one event when a cannon goes from Loading to Loaded ("end of the reload cycle").
/// The first load after spawn (from NeverLoaded, or already loading when first seen) is not an event,
/// and a Loading phase shorter than MinLoadingSeconds is treated as flicker.
/// </summary>
public sealed class ReloadEdge
{
    private double loadingSince = double.NaN;
    private bool initial = true;
    public double MinLoadingSeconds { get; }
    public ReloadEdge(double minLoadingSeconds = .1) { MinLoadingSeconds = Math.Max(0, minLoadingSeconds); }
    public bool Update(LoadPhase phase, double now)
    {
        switch (phase)
        {
            case LoadPhase.Loading:
                if (double.IsNaN(loadingSince)) loadingSince = now;
                return false;
            case LoadPhase.Loaded:
                {
                    bool edge = !initial && !double.IsNaN(loadingSince) && now - loadingSince >= MinLoadingSeconds;
                    initial = false; loadingSince = double.NaN;
                    return edge;
                }
            case LoadPhase.NeverLoaded:
                initial = true; loadingSince = double.NaN;
                return false;
            default: // Unloaded (e.g. out of ammunition): keep an ongoing Loading phase
                return false;
        }
    }
}

/// <summary>The exact read-only load-state adapter used by native polling and end-to-end tests.</summary>
public sealed class CannonLoadSampler
{
    private readonly LevelFilter loading = new(.1), notReady = new(.1), loaded = new(.1);
    private readonly ReloadEdge reloaded = new(.1);
    public CannonState Sample(int vuid, int mask, LoadPhase phase, double now) => new(vuid, mask,
        loading.Update(phase == LoadPhase.Loading, now),
        notReady.Update(phase != LoadPhase.Loaded && phase != LoadPhase.NeverLoaded, now),
        loaded.Update(phase == LoadPhase.Loaded, now), reloaded.Update(phase, now) ? 1 : 0);
}

/// <summary>
/// Unity-free view of the runtime context for external API inputs: the token is the vehicle's
/// GameObject in game, a fake object in tests. Keeps the public facade free of runtime types.
/// </summary>
public interface IExternalVehicleContext
{
    object? VehicleToken { get; }
}

/// <summary>Stable keys for Choice parameters that point at a part: "any", "vuid:12" or "vuid:12:Name".</summary>
public static class PartSelector
{
    public const string Any = "any";
    public static string ForVuid(int vuid, string? name = null) =>
        "vuid:" + vuid.ToString(CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(name) ? "" : ":" + name);
    public static bool TryParse(string? selector, out int vuid, out string name)
    {
        vuid = 0; name = "";
        if (selector == null || !selector.StartsWith("vuid:", StringComparison.Ordinal)) return false;
        var rest = selector.Substring(5);
        int colon = rest.IndexOf(':');
        var number = colon < 0 ? rest : rest.Substring(0, colon);
        if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out vuid)) return false;
        name = colon < 0 ? "" : rest.Substring(colon + 1);
        return true;
    }

    /// <summary>
    /// Cannon choice. Returns the VUID to match, or null for "any cannon in the fire group" - also when the
    /// selected cannon is not on this vehicle (copied vehicle: VUIDs are reissued), which sets fellBack.
    /// </summary>
    public static int? ResolveCannon(string? selector, IReadOnlyList<CannonState> cannons, out bool fellBack)
    {
        fellBack = false;
        if (!TryParse(selector, out int vuid, out _)) return null;
        foreach (var c in cannons) if (c.Vuid == vuid) return vuid;
        fellBack = true;
        return null;
    }

    /// <summary>Hinge choice: by VUID on this vehicle, else by a unique name. -1 when not found.</summary>
    public static int ResolveHinge(string? selector, IReadOnlyList<HingeState> hinges)
    {
        if (!TryParse(selector, out int vuid, out var name)) return -1;
        if (vuid != 0) for (int i = 0; i < hinges.Count; i++) if (hinges[i].Vuid == vuid) return i;
        if (name.Length == 0) return -1;
        int found = -1;
        for (int i = 0; i < hinges.Count; i++)
            if (string.Equals(hinges[i].Name, name, StringComparison.Ordinal)) { if (found >= 0) return -1; found = i; }
        return found;
    }
}

/// <summary>
/// Option lists for Choice parameters, filled by the game (the I/O Flow panel's vehicle) and read by the
/// built-in input types. Defaults contain only "any" so the pure code works without a game.
/// </summary>
public static class IoCatalog
{
    public static IReadOnlyList<ParamOption> Rotators { get; set; } = Array.Empty<ParamOption>();
    private static IReadOnlyList<ParamOption> cannons = new[] { new ParamOption(PartSelector.Any, "Any cannon") };
    private static IReadOnlyList<ParamOption> hinges = Array.Empty<ParamOption>();
    /// <summary>Always starts with "any".</summary>
    public static IReadOnlyList<ParamOption> Cannons
    {
        get => cannons;
        set => cannons = new[] { new ParamOption(PartSelector.Any, "Any cannon") }.Concat((value ?? Array.Empty<ParamOption>()).Where(o => o.Value != PartSelector.Any)).ToArray();
    }
    public static IReadOnlyList<ParamOption> Hinges { get => hinges; set => hinges = value?.ToArray() ?? Array.Empty<ParamOption>(); }
    public static IReadOnlyDictionary<string, int> CannonFireMasks { get; set; } = new Dictionary<string, int>();
    private static IReadOnlyDictionary<string, int> cannonGroups = new Dictionary<string, int>();
    /// <summary>First fire group of each specific cannon option (key = option value): the rule's fallback group.</summary>
    public static IReadOnlyDictionary<string, int> CannonFireGroups
    {
        get => cannonGroups;
        set => cannonGroups = value != null ? value.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) : new Dictionary<string, int>();
    }
}
