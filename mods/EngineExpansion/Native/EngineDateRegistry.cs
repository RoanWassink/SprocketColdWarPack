using Sprocket.TechTrees;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Engines;
using SprocketEngineExpansion.Core;

namespace SprocketEngineExpansion.Native;

/// <summary>Per-blueprint owning-vehicle date captured from the editor/component.</summary>
internal static class EngineDateRegistry
{
    private static readonly Dictionary<IntPtr, DateTime?> Dates = new();
    private static readonly HashSet<string> Warnings = new();

    internal static DateTime? Date(VehicleComponent? component)
    {
        try
        {
            var value = component?.Vehicle?.DesignInfo?.Date;
            if (value is not { Valid: true } date) return null;
            return date.Year == 0 ? DateTime.MinValue : new DateTime(date.Year, date.Month, date.Day);
        }
        catch { return null; }
    }

    internal static void Remember(EngineBlueprint blueprint, VehicleComponent? component) =>
        Remember(blueprint, Date(component));

    internal static void Remember(EngineBlueprint blueprint, DateTime? date)
    {
        if (blueprint == null) return;
        Dates[blueprint.Pointer] = date;
    }

    internal static DateTime? For(EngineBlueprint blueprint) =>
        blueprint != null && Dates.TryGetValue(blueprint.Pointer, out var date) ? date : null;

    internal static void Forget(EngineBlueprint blueprint)
    {
        if (blueprint != null) Dates.Remove(blueprint.Pointer);
    }

    internal static EngineKind Effective(EngineBlueprint blueprint, EngineKind saved)
    {
        var date = For(blueprint);
        if (date is null) return saved;
        var effective = EngineDates.Effective(date, saved);
        if (effective != saved && Warnings.Add(blueprint.Pointer + ":" + (int)saved))
            Plugin.ModLog.LogWarning(
                $"[Engine Expansion] {saved} needs {EngineDates.For(saved):yyyy-MM-dd}; this vehicle is " +
                $"{date.Value:yyyy-MM-dd}. Saved profile kept, native petrol baseline used.");
        return effective;
    }

    /// <summary>Housing mass and cost apply only on or after the powerpack date; the layout is kept either way.</summary>
    internal static bool PowerpackEffective(EngineBlueprint blueprint)
    {
        if (!Selections.Get(blueprint).Powerpack) return false;
        var date = For(blueprint);
        if (EngineDates.PowerpackEffective(date, true)) return true;
        if (Warnings.Add(blueprint.Pointer + ":powerpack"))
            Plugin.ModLog.LogWarning(
                $"[Engine Expansion] Powerpack needs {EngineDates.PowerpackDate:yyyy-MM-dd}; this vehicle is " +
                $"{date!.Value:yyyy-MM-dd}. Saved layout kept, housing mass and cost not applied.");
        return false;
    }
}
