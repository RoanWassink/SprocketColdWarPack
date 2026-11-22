using HarmonyLib;
using Sprocket.Blueprints;
using Sprocket.Vehicles.Tracks;

namespace SprocketWheeledVehicles;

internal static class WheeledProfiles
{
    private const int MaximumEntries = 1024;
    private sealed record Entry(TrackBlueprint Blueprint, WheeledSettings Settings);
    private static readonly Dictionary<IntPtr, Entry> Entries = new();
    internal const string SaveKey = "sprocketWheeledVehiclesV1";

    internal static bool TryGet(TrackBlueprint? blueprint, out WheeledSettings settings)
    {
        if (blueprint != null && Entries.TryGetValue(blueprint.Pointer, out var entry))
        {
            settings = entry.Settings;
            return true;
        }
        settings = null!;
        return false;
    }

    internal static WheeledSettings Get(TrackBlueprint blueprint)
    {
        if (!Entries.TryGetValue(blueprint.Pointer, out var entry))
        {
            if (Entries.Count >= MaximumEntries) Entries.Clear();
            Entries[blueprint.Pointer] = entry = new(blueprint, new WheeledSettings());
        }
        return entry.Settings;
    }

    internal static void Set(TrackBlueprint blueprint, WheeledSettings settings)
    {
        if (!Entries.ContainsKey(blueprint.Pointer) && Entries.Count >= MaximumEntries) Entries.Clear();
        Entries[blueprint.Pointer] = new(blueprint, settings);
    }

    internal static void Remove(TrackBlueprint blueprint) => Entries.Remove(blueprint.Pointer);

    internal static bool IsEnabledFor(TrackAssembly? assembly) =>
        WheeledPart.TryGetActiveSettings(assembly, out _);

    internal static bool TryGetFor(TrackAssembly? assembly, out WheeledSettings settings) =>
        WheeledPart.TryGetActiveSettings(assembly, out settings);
}

[HarmonyPatch]
internal static class BlueprintHooks
{
    [HarmonyPostfix, HarmonyPatch(typeof(TrackBlueprint), nameof(TrackBlueprint.Save))]
    private static void Save(TrackBlueprint __instance, IBlueprintSerializer __0)
    {
        try
        {
            if (WheeledProfiles.TryGet(__instance, out var settings) && settings.Enabled && settings.IsValid())
            {
                __0.AddValue(WheeledProfiles.SaveKey, settings.Serialize());
                Plugin.Diag(() => $"Saved blueprint wheeled settings: {settings.Serialize()}");
            }
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("Blueprint save", ex);
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TrackBlueprint), nameof(TrackBlueprint.Load))]
    private static void Load(TrackBlueprint __instance, IBlueprintDeserializer __0)
    {
        string? raw;
        try { raw = __0.GetString(WheeledProfiles.SaveKey); }
        catch { WheeledProfiles.Remove(__instance); return; }

        if (string.IsNullOrWhiteSpace(raw))
        {
            WheeledProfiles.Remove(__instance);
            return;
        }

        try
        {
            var parsed = WheeledSettings.Parse(raw);
            WheeledProfiles.Set(__instance, parsed);
            Plugin.Diag(() => $"Loaded blueprint wheeled settings: {parsed.Serialize()}");
        }
        catch (Exception ex)
        {
            WheeledProfiles.Remove(__instance);
            RuntimeWheeled.Warn("Invalid saved wheeled settings; falling back to vanilla tracks", ex);
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Blueprint), nameof(Blueprint.Copy))]
    private static void Copy(Blueprint __0, Blueprint __1)
    {
        try
        {
            var source = __0?.TryCast<TrackBlueprint>();
            var destination = __1?.TryCast<TrackBlueprint>();
            if (source == null || destination == null) return;
            if (WheeledProfiles.TryGet(source, out var settings))
                WheeledProfiles.Set(destination, settings.Copy());
            else
                WheeledProfiles.Remove(destination);
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("Blueprint copy", ex);
        }
    }
}
