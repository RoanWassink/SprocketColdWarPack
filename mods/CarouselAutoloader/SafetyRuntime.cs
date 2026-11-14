using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons;

namespace SprocketCarouselAutoloader;

// 0.2.28 runtime glue for the safety pattern in SafetyPolicy.cs.
internal static class HookGuard
{
    private static readonly WarnOnce Seen = new();

    /// <summary>A hook failed: the caller keeps the native behaviour for this call. One warning per hook and VUID.</summary>
    internal static void Warn(string hook, int vuid, Exception ex)
    {
        try
        {
            if (Seen.First(hook, vuid))
                Plugin.ModLog.LogWarning($"[Autoloaders] {hook} failed (VUID {vuid}); native behaviour kept. Logged once per hook and VUID: {ex}");
        }
        catch { /* Logging never changes the game. */ }
    }

    internal static int Vuid(VehicleComponent? component)
    {
        try { return component?.VUID.Value ?? -1; }
        catch { return -1; }
    }

    /// <summary>VUID of the cannon behind a load task, from the early-exit cache; -1 when unknown.</summary>
    internal static int TaskVuid(LoadTask? task)
    {
        try { return task == null ? -1 : Vuid(VehicleLoadIndex.Link(task)?.Cannon); }
        catch { return -1; }
    }
}

/// <summary>An intervention on native state (H2 magazine route, H3 contributor lists) that is switched off for one
/// cannon (design|VUID) for the rest of the session after it failed and its native state was restored.</summary>
internal sealed class InterventionSwitch
{
    private readonly SessionSwitch keys = new();
    private readonly string name;
    internal InterventionSwitch(string name) => this.name = name;

    internal bool Off(Cannon cannon)
    {
        if (!keys.Any) return false; // Hot path while nothing failed: no lookup, no allocation.
        return keys.Cached(cannon.Pointer) ?? keys.Resolve(cannon.Pointer, Key(cannon));
    }

    internal void Disable(Cannon? cannon, Exception ex)
    {
        try
        {
            var key = cannon == null ? "|-1" : Key(cannon);
            if (keys.Disable(cannon?.Pointer ?? IntPtr.Zero, key))
                Plugin.ModLog.LogWarning($"[Autoloaders] {name} failed for cannon {HookGuard.Vuid(cannon)}: native state restored; " +
                    $"{name} stays off for this cannon for the rest of the session (native loading). Logged once per VUID: {ex}");
        }
        catch { /* Logging never changes the game. */ }
    }

    internal void ForgetPointers() => keys.ForgetPointers();

    private static string Key(Cannon cannon)
    {
        string design;
        try { design = cannon.Vehicle?.DesignInfo?.Name ?? ""; } catch { design = ""; }
        return design + "|" + HookGuard.Vuid(cannon);
    }
}
