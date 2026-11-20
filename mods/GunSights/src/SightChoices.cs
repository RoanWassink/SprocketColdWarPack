using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using UnityEngine.Events;
using SerializationInfo = Il2CppSystem.Runtime.Serialization.SerializationInfo;

namespace SprocketGunSights;

/// <summary>
/// Per placed sight: the chosen catalogue variant and the "Sight follows gun" choice.
/// Both are saved in the blueprint object state by <see cref="SightPersistence"/>
/// (sprocketGunSightsV1). The component SaveData/LoadData route below is the older
/// variant-only path; it stays as a load fallback.
/// </summary>
[HarmonyPatch]
internal static class SightChoices
{
    internal const string SaveKey = "sprocketGunSightVariant";
    private static readonly Dictionary<IntPtr, string> chosen = new();
    private static readonly Dictionary<IntPtr, bool> follow = new();
    private static readonly HashSet<string> warned = new();

    internal static SightVariant Variant(GunnerSight sight)
    {
        // A saved identity is always returned, even when its date is too late for
        // this vehicle. The runtime refuses to drive it; it is never rewritten.
        if (chosen.TryGetValue(sight.Pointer, out var id) && SightCatalogue.Find(id) is { } v) return v;
        return SightCatalogue.Default(sight.VehicleObject?.GUID, Date(sight));
    }

    internal static bool Saved(GunnerSight sight) => chosen.ContainsKey(sight.Pointer);

    internal static bool IsAtgmGuidance(GunnerSight sight) =>
        string.Equals(sight.VehicleObject?.GUID, SightCatalogue.AtgmGuidanceSightGuid, StringComparison.OrdinalIgnoreCase);

    /// <summary>Explicit "Sight follows gun" choice, or null when this sight uses [View] DefaultSightFollowsGun.</summary>
    internal static bool? FollowChoice(GunnerSight sight) => follow.TryGetValue(sight.Pointer, out var f) ? f : null;

    /// <summary>true = view stays along the sight part (TurretView override); false = native view following the gun.</summary>
    internal static bool Static(GunnerSight sight) =>
        SightViewPolicy.Static(FollowChoice(sight), Plugin.DefaultSightFollowsGun, IsAtgmGuidance(sight));

    internal static void SetFollow(GunnerSight sight, bool value)
    {
        follow[sight.Pointer] = value;
        Plugin.Log.LogInfo($"[Gun sights] Sight {sight.VUID.Value} set to {(value ? "follows gun" : "stays static")}.");
    }

    /// <summary>Row for the object-state envelope; null when nothing was chosen on this sight.</summary>
    internal static SightStateEntry? SavedEntry(GunnerSight sight)
    {
        string? id = chosen.TryGetValue(sight.Pointer, out var v) && SightStateData.ValidId(v) ? v : null;
        bool? f = IsAtgmGuidance(sight) ? null : FollowChoice(sight);
        return id == null && f == null ? null : new SightStateEntry(sight.VUID.Value, id, f);
    }

    /// <summary>
    /// Batch restore after InstantiateStructured: same store as Select and SetFollow, without the
    /// date gate (a saved identity stays authoritative, like the LoadData path). null clears.
    /// </summary>
    internal static void Restore(GunnerSight sight, string? variant, bool? followGun)
    {
        if (variant == null) chosen.Remove(sight.Pointer); else chosen[sight.Pointer] = variant;
        if (followGun is { } f) follow[sight.Pointer] = f; else follow.Remove(sight.Pointer);
        ScopeRuntime.SightChanged(sight.Pointer);
    }

    // Limited diagnostics: one line when a sight becomes active in the scope (max 40 per session).
    private static int activationLogs;
    internal static void LogActivation(GunnerSight sight)
    {
        if (activationLogs >= 40) return;
        activationLogs++;
        var mode = IsAtgmGuidance(sight) ? "free view (ATGM guidance, excluded)"
            : (Static(sight) ? "stays static" : "follows gun") + (FollowChoice(sight) != null ? " (saved choice)" : $" (default, DefaultSightFollowsGun={Plugin.DefaultSightFollowsGun})");
        Plugin.Log.LogInfo($"[Gun sights] Sight {sight.VUID.Value} active: {mode}; variant {Variant(sight).Id}{(Saved(sight) ? " (saved)" : " (date default)")}.");
    }

    internal static DateTime? Date(VehicleComponent component)
    {
        try
        {
            var date = component.Vehicle?.DesignInfo?.Date;
            if (date is not { Valid: true } d) return null;
            // Native TechDate supports year zero; System.DateTime does not.
            return d.Year == 0 ? DateTime.MinValue : new DateTime(d.Year, d.Month, d.Day);
        }
        catch { return null; }
    }

    internal static bool Available(GunnerSight sight, SightVariant variant) =>
        SightDatePolicy.Allows(Date(sight), variant);

    internal static void WarnUnavailable(GunnerSight sight, SightVariant variant)
    {
        var date = Date(sight);
        var from = variant.AvailableFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var on = date is { } d && d != DateTime.MinValue
            ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : "an unknown date";
        var key = "unavailable:" + sight.Pointer + ":" + variant.Id;
        if (warned.Add(key))
            Plugin.Log.LogWarning($"[Gun sights] Saved sight '{variant.Name}' ({variant.Id}) needs {from} but this vehicle is {on}; identity kept, stock sight used.");
    }

    internal static void Select(GunnerSight sight, SightVariant variant)
    {
        if (!Available(sight, variant)) { WarnUnavailable(sight, variant); return; }
        chosen[sight.Pointer] = variant.Id;
        ScopeRuntime.SightChanged(sight.Pointer);
        Plugin.Log.LogInfo($"[Gun sights] Sight {sight.VUID.Value} set to {variant.Id} ({variant.Name}).");
    }

    internal static void Clear() { chosen.Clear(); follow.Clear(); warned.Clear(); }

    private static void Warn(string area, Exception ex)
    { if (warned.Add(area)) Plugin.Log.LogWarning($"[Gun sights] {area}: {ex.Message}"); }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.SaveData))]
    private static void Save(VehicleComponent __instance, SerializationInfo __0)
    {
        try
        {
            // Legacy variant-only key, kept: whether any native path (undo, copy) still reads
            // component SaveData could not be ruled out. On load the object-state envelope wins.
            if (chosen.TryGetValue(__instance.Pointer, out var id)) __0.AddValue(SaveKey, id);
        }
        catch (Exception ex) { Warn("Save sight", ex); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.LoadData))]
    private static void Load(VehicleComponent __instance, SerializationInfo __0)
    {
        try
        {
            if (__instance.TryCast<GunnerSight>() is null) return;
            chosen.Remove(__instance.Pointer);
            var entries = __0.GetEnumerator();
            while (entries.MoveNext())
            {
                if (entries.Name != SaveKey) continue;
                var id = __0.GetString(SaveKey);
                if (SightCatalogue.Find(id) != null) chosen[__instance.Pointer] = id;
                else Plugin.Log.LogWarning($"[Gun sights] Unknown saved sight '{id}' on {__instance.VUID.Value}; default used.");
                break;
            }
        }
        catch (Exception ex) { Warn("Load sight", ex); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.ReleaseInternal))]
    private static void Release(VehicleComponent __instance) { chosen.Remove(__instance.Pointer); follow.Remove(__instance.Pointer); }

    // ------------------------------------------------------------------ inspector
    private static bool open = true;
    private static readonly Dictionary<IntPtr, string> browsingCountry = new();

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectEditor), nameof(VehicleObjectEditor.OnGUI))]
    private static void Inspector(VehicleObjectEditor __instance, IGUILayout __0)
    {
        try
        {
            var sight = __instance.target?.GetBehaviour<GunnerSight>();
            if (sight == null) return;
            var ui = __0.TryCast<IGUIElementDrawer>();
            if (ui == null) return;
            var current = Variant(sight);
            var date = Date(sight);
            var country = browsingCountry.TryGetValue(sight.Pointer, out var c) ? c : current.Country;
            var countries = SightCatalogue.Countries;
            var variants = SightCatalogue.ForCountry(country);
            var available = variants.Where(v => SightDatePolicy.Allows(date, v)).ToArray();
            var unavailable = variants.Where(v => !SightDatePolicy.Allows(date, v)).ToArray();

            __0.EndAllDropdowns();
            __0.BeginDropdown("Gun sight", open, DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v => open = v))!);
            try
            {
                ui.Dropdown("Country", List(countries), Array.IndexOf(countries, country),
                    Action(index =>
                    {
                        if (index < 0 || index >= countries.Length || sight.VehicleObject.Released) return;
                        browsingCountry[sight.Pointer] = countries[index];
                        // Keep the period: pick that country's earliest usable sight
                        // closest to the current era. Never select a too-early variant.
                        var options = SightCatalogue.ForCountry(countries[index])
                            .Where(v => SightDatePolicy.Allows(date, v)).ToArray();
                        if (options.Length == 0) { __instance.RequestRedraw(); return; }
                        var best = options.OrderBy(v => Math.Abs((int)v.Era - (int)current.Era))
                            .ThenByDescending(v => v.AvailableFrom).First();
                        Select(sight, best);
                        __instance.RequestRedraw();
                    }), "Nation of the sight. Then choose the exact variant below.");

                var index = Array.FindIndex(available, v => v.Id == current.Id);
                if (available.Length > 0)
                    ui.Dropdown("Variant", List(available.Select(v => v.Label).ToArray()), index,
                        Action(i =>
                        {
                            if (i < 0 || i >= available.Length || sight.VehicleObject.Released) return;
                            Select(sight, available[i]);
                            __instance.RequestRedraw();
                        }), index >= 0 ? "Used on: " + current.Tanks : "Pick a sight of this country.");
                else
                    ui.InfoField("No sight of this country is available on this vehicle date.", 2,
                        new UnityEngine.Color(.6f, .6f, .6f));

                // Too-early variants stay visible but cannot be chosen; grey text
                // with their own start date is the picker's disabled state.
                foreach (var early in unavailable)
                    ui.InfoField($"vanaf {early.AvailableFrom:yyyy-MM-dd}  {early.Label}", 2,
                        new UnityEngine.Color(.6f, .6f, .6f));

                if (index >= 0 || Array.FindIndex(variants, v => v.Id == current.Id) >= 0)
                {
                    ui.InfoField("Used on: " + current.Tanks, 2);
                    ui.InfoField($"Optics: {SightCatalogue.Powers(current)}" +
                        (current.Magnifications.Length > 1 ? " (mouse wheel switches in the sight)" : ""), 2);
                    ui.InfoField((current.Estimated ? "Estimate: " : "Source: ") + current.Source, 2);
                }
                if (Array.FindIndex(variants, v => v.Id == current.Id) >= 0 && !SightDatePolicy.Allows(date, current))
                    ui.InfoField($"Saved sight needs {current.AvailableFrom:yyyy-MM-dd}; stock sight is used until then. The saved choice is kept.", 3,
                        new UnityEngine.Color(.6f, .6f, .6f));
                if (!Saved(sight)) ui.InfoField("Default for this vehicle's date; choose to fix it on this sight.", 2);
                if (IsAtgmGuidance(sight))
                    ui.InfoField("Sight follows gun: not used here. The ATGM guidance sight always keeps its own free view.", 2,
                        new UnityEngine.Color(.6f, .6f, .6f));
                else
                {
                    var follows = !Static(sight);
                    ui.ToggleField("Sight follows gun", follows,
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v =>
                        {
                            try
                            {
                                if (sight.VehicleObject.Released) return;
                                SetFollow(sight, v);
                                __instance.RequestRedraw();
                            }
                            catch (Exception ex) { Warn("Follow gun toggle", ex); }
                        }))!,
                        "On: the game's own view, aimed with the gun (up and down), stock aim circle shown. Off: the view stays along this sight part (stays static), for sights that should not elevate.");
                    if (FollowChoice(sight) == null)
                        ui.InfoField($"Default ({(Plugin.DefaultSightFollowsGun ? "follows gun" : "stays static")}); click to fix it on this sight.", 2);
                }
                var linked = Linked(sight);
                ui.InfoField(linked.Count == 0 ? "Not assigned to any weapon yet." : "Assigned to: " + string.Join(", ", linked.Select(l => l.Name)), 2);
                if (string.Equals(sight.VehicleObject?.GUID, SightCatalogue.AtgmGuidanceSightGuid, StringComparison.OrdinalIgnoreCase) && linked.Any(l => !l.Launcher))
                    ui.InfoField("This ATGM guidance sight is also assigned to an ordinary gun, so its free view is off. Give that gun its own sight in the gun's Sight setting.", 3, new UnityEngine.Color(1f, .55f, .3f));
            }
            finally { __0.EndAllDropdowns(); }
        }
        catch (Exception ex) { Warn("Inspector", ex); }
    }

    private const string LauncherGuid = "07a91e5d-be14-5a5c-9321-574b21aadcf3";
    private static List<(string Name, bool Launcher)> Linked(GunnerSight sight)
    {
        var result = new List<(string, bool)>();
        var objects = sight.Vehicle?.ObjectReader?.Items;
        if (objects == null) return result;
        int count = objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for (int i = 0; i < count; i++)
        {
            var parts = objects[i].Components;
            int n = parts.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for (int j = 0; j < n; j++)
                if (parts[j].TryCast<Sprocket.Vehicles.Cannons.Cannon>() is { } c && c.TryGetAssignedSight(out var a) && a?.Pointer == sight.Pointer)
                {
                    var launcher = c.VehicleObject?.GUID == LauncherGuid;
                    result.Add(((launcher ? "ATGM launcher" : $"{c.Blueprint?.Caliber ?? 0} mm gun") + $" #{c.VUID.Value}", launcher));
                }
        }
        return result;
    }

    private static Il2CppSystem.Collections.Generic.IReadOnlyList<string> List(IEnumerable<string> items)
    {
        var list = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var item in items) list.Add(item);
        return list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>();
    }

    private static UnityAction<int> Action(Action<int> action) =>
        DelegateSupport.ConvertDelegate<UnityAction<int>>((Action<int>)(i =>
        {
            try { action(i); } catch (Exception ex) { Warn("Select sight", ex); }
        }))!;
}
