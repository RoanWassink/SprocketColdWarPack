using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DesignerFramework.AssetManagement;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Selection;
using Sprocket.Vehicles.PlateStructures.Design;
using SprocketOscillatingTurret;

namespace OscillatingSelector;

// Keep native turret construction, undo, attachment and blueprint ownership.
//
// The oscillating variant is merged into the native generated-turret preset
// tab instead of being exposed as a separate selection group. The merge is
// deduplicated by display name and rebinds the select callback when a designer
// instance changes, so a shared native group cannot keep a stale designer.
internal static class SelectorIntegration
{
    internal const string PartGuid = "31fa3dd0-0738-4d29-b6a5-c3d77c291ea2";
    private const string OptionPrefix = "Oscillating ";
    private const string OptionDescription =
        "Upper turret with a compact elevation basket. Place upright on the azimuth turret; " +
        "adjust its native motor elevation limits.";

    private static readonly Dictionary<IntPtr, OptionGroup> Groups = new();
    private static readonly Dictionary<IntPtr, IntPtr> GroupDesigner = new();
    private static ManualLogSource Log = null!;
    [ThreadStatic] private static int CreationDepth;
    internal static bool Creating => CreationDepth > 0;

    internal static void Install(Harmony harmony, ManualLogSource log)
    {
        Log = log;
        harmony.Patch(AccessTools.Method(typeof(PlateStructureDesigner), nameof(PlateStructureDesigner.GetSelection)),
            postfix: new HarmonyMethod(typeof(SelectorIntegration), nameof(GetSelection)));
        harmony.Patch(AccessTools.Method(typeof(PlateStructureDesigner), nameof(PlateStructureDesigner.Release)),
            prefix: new HarmonyMethod(typeof(SelectorIntegration), nameof(Release)));
    }

    private static bool IsOscillatingOption(Option? option)
    {
        return option != null &&
               option.Name != null &&
               option.Name.StartsWith(OptionPrefix, StringComparison.Ordinal);
    }

    private static Option CreateOption(
        PlateStructureDesigner designer,
        PlateStructureDesigner.GeneratedDesignOption preset)
    {
        var chosen = preset;
        return new Option
        {
            Name = OptionPrefix + chosen.Name,
            Description = OptionDescription,
            Icon = chosen.Icon,
            Data = chosen,
            OnSelect = (Option.PerformSelect)(Action<Il2CppSystem.Object>)(_ =>
            {
                string original = designer.turretRingPartGuid;
                try
                {
                    CreationDepth++;
                    designer.turretRingPartGuid = PartGuid;
                    designer.LoadGeneratedDesign(chosen, -1);
                }
                finally
                {
                    designer.turretRingPartGuid = original;
                    CreationDepth--;
                }
            })
        };
    }

    private static bool DateAllowed(PlateStructureDesigner designer)
    {
        try
        {
            var value = designer.Target?.DesignInfo?.Date;
            if (value is not { Valid: true } d) return true;
            var date = d.Year == 0 ? DateTime.MinValue : new DateTime(d.Year, d.Month, d.Day);
            return OscillatingDatePolicy.Allows(date);
        }
        catch
        {
            // A missing/ambiguous designer date must not expose an undated preset.
            return false;
        }
    }

    /// <summary>
    /// Appends one oscillating variant after every native generated-turret
    /// preset in this group. Returns the number of newly added options, 0 when
    /// the group had nothing to do, or -1 when the group could not be modified.
    /// </summary>
    private static int MergeInto(PlateStructureDesigner designer, OptionGroup group)
    {
        if (!DateAllowed(designer)) return 0;
        try
        {
            var items = group.items;
            if (items == null)
            {
                return 0;
            }

            var current = new List<Option>();
            foreach (var item in items)
            {
                if (item != null) current.Add(item);
            }

            int nativePresets = 0;
            int ours = 0;
            foreach (var item in current)
            {
                if (IsOscillatingOption(item))
                {
                    ours++;
                    continue;
                }

                if (item.Data != null &&
                    item.Data.TryCast<PlateStructureDesigner.GeneratedDesignOption>() != null)
                {
                    nativePresets++;
                }
            }

            bool sameDesigner =
                GroupDesigner.TryGetValue(group.Pointer, out IntPtr mapped) &&
                mapped == designer.Pointer;
            if (sameDesigner && nativePresets > 0 && ours == nativePresets)
            {
                return 0;
            }

            var baseItems = new List<Option>();
            var usedNames = new HashSet<string>();
            foreach (var item in current)
            {
                if (IsOscillatingOption(item)) continue;
                baseItems.Add(item);
                if (item.Name != null) usedNames.Add(item.Name);
            }

            var additions = new List<Option>();
            int presets = 0;
            foreach (var item in baseItems)
            {
                var preset = item.Data != null
                    ? item.Data.TryCast<PlateStructureDesigner.GeneratedDesignOption>()
                    : null;
                if (preset == null) continue;

                presets++;
                string name = OptionPrefix + preset.Name;
                if (usedNames.Contains(name)) continue;
                usedNames.Add(name);
                additions.Add(CreateOption(designer, preset));
            }

            if (presets == 0)
            {
                return 0;
            }

            var merged = new Option[baseItems.Count + additions.Count];
            baseItems.CopyTo(merged, 0);
            additions.CopyTo(merged, baseItems.Count);
            group.SetOptions(merged);
            group.MarkModified();
            GroupDesigner[group.Pointer] = designer.Pointer;

            return additions.Count;
        }
        catch (Exception ex)
        {
            Log.LogWarning(
                "Oscillating turret tab merge failed: " + ex.GetType().Name + ": " + ex.Message);
            return -1;
        }
    }

    private static void GetSelection(PlateStructureDesigner __instance, ISelectionBuffer __0)
    {
        if (__instance == null)
        {
            return;
        }

        int reachableGroups = 0;
        int failedGroups = 0;
        try
        {
            if (__instance.selection != null)
            {
                foreach (var nativeGroup in __instance.selection)
                {
                    var group = nativeGroup?.TryCast<OptionGroup>();
                    if (group == null) continue;
                    int result = MergeInto(__instance, group);
                    if (result < 0) failedGroups++;
                    else reachableGroups++;
                }
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning(
                "Oscillating turret selection scan failed: " + ex.GetType().Name + ": " + ex.Message);
            failedGroups++;
        }

        if (reachableGroups > 0 && failedGroups == 0)
        {
            return;
        }

        // Fallback: keep the previous dedicated tab when the native turret group
        // cannot be identified or modified on this selection build.
        var fallback = CreateFallbackGroup(__instance);
        if (fallback != null)
        {
            __0.Add(fallback.Cast<ISelectionGroup>());
        }
    }

    private static OptionGroup? CreateFallbackGroup(PlateStructureDesigner designer)
    {
        if (!DateAllowed(designer)) return null;
        if (Groups.TryGetValue(designer.Pointer, out var cached))
        {
            return cached;
        }

        var presets = new List<PlateStructureDesigner.GeneratedDesignOption>();
        var seen = new HashSet<IntPtr>();
        if (designer.selection == null) return null;
        foreach (var nativeGroup in designer.selection)
        {
            var optionGroup = nativeGroup?.TryCast<OptionGroup>();
            if (optionGroup?.items == null) continue;
            foreach (var option in optionGroup.items)
            {
                var preset = option?.Data?.TryCast<PlateStructureDesigner.GeneratedDesignOption>();
                if (preset != null && seen.Add(preset.Pointer)) presets.Add(preset);
            }
        }

        if (presets.Count == 0) return null;

        var options = new List<Option>();
        foreach (var preset in presets)
        {
            if (preset == null) continue;
            options.Add(CreateOption(designer, preset));
        }

        var result = new OptionGroup("Oscillating turrets", options.ToArray());
        result.MarkModified();
        Groups[designer.Pointer] = result;
        Log.LogWarning(
            "Native turret tab could not be merged; falling back to a separate 'Oscillating turrets' tab.");
        return result;
    }

    private static void Release(PlateStructureDesigner __instance)
    {
        if (__instance == null) return;
        Groups.Remove(__instance.Pointer);
        var stale = new List<IntPtr>();
        foreach (var pair in GroupDesigner)
        {
            if (pair.Value == __instance.Pointer) stale.Add(pair.Key);
        }

        foreach (IntPtr key in stale) GroupDesigner.Remove(key);
    }

    internal static void Clear()
    {
        Groups.Clear();
        GroupDesigner.Clear();
    }
}
