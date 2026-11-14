using DesignerFramework.AssetManagement;
using DesignerFramework.UI;
using HarmonyLib;
using SelectionPanel=DesignerFramework.UI.SelectionPanel;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.PartImporting;
using Sprocket.Selection;
using UnityEngine;

namespace SprocketExpandedBallistics;

/// <summary>
/// Native part-picker integration:
/// - composes WW1/WW2 icons from the actual native vanilla sight icon;
/// - marks the exact vanilla gunner-sight card;
/// - removes that exact option only while the authoritative Expedition session
///   owner reports an active session. Definitions and saved assignments are untouched.
/// </summary>
[HarmonyPatch]
internal static class SightUiIntegration
{
    internal const string Owner = "sprocket.expandedballistics.sightui";
    private static bool warned;

    [HarmonyPrefix, HarmonyPatch(typeof(GameIcons), nameof(GameIcons.GetOrDefault))]
    private static bool IconLookup(string __0, ref Sprite __result)
    {
        if (!SightCatalog.TryResolveIdentifier(__0, out var sight)) return true;
        try
        {
            var icon = SightIcons.For(sight.Guid);
            if (icon == null) return true;
            __result = icon;
            return false;
        }
        catch (Exception ex)
        {
            Warn(ex);
            return true;
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(PartDefinitionCardFactory), nameof(PartDefinitionCardFactory.CreateCard))]
    private static void Card(PartDefinition __0, PartDisplayCard __result)
    {
        if (__0 == null || __result == null) return;
        try
        {
            if (SightMenuPolicy.IsVanillaGuid(__0.guid))
            {
                var tags = (__result.Tags?.ToArray() ?? Array.Empty<string>()).ToList();
                if (!tags.Contains(SightMenuPolicy.VanillaMarker)) tags.Add(SightMenuPolicy.VanillaMarker);
                __result.Tags = new Il2CppStringArray(tags.ToArray());
            }
            if (SightCatalog.TryResolve(__0.guid, out var sight))
            {
                var icon = SightIcons.For(sight.Guid);
                if (icon != null) __result.Icon = icon;
            }
        }
        catch (Exception ex) { Warn(ex); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(SelectionPanel), nameof(SelectionPanel.SetDisplayOptions))]
    private static void FilterVanilla(SelectionPanel __instance,
        ref Il2CppReferenceArray<ISelectionGroup> __0, ref int __1)
    {
        if (__0 == null || !ExpeditionState.ShouldHidePickerEntry()) return;
        try
        {
            var groups = new List<ISelectionGroup>();
            var removed = new List<bool>();
            var changed = false;
            for (var i = 0; i < __0.Length; i++)
            {
                var item = __0[i];
                var group = item?.TryCast<OptionGroup>();
                if (group?.items == null)
                {
                    groups.Add(item!);
                    removed.Add(false);
                    continue;
                }

                var retained = new List<Option>();
                foreach (var option in group.items)
                    if (!IsVanillaOption(option)) retained.Add(option!);
                if (retained.Count == group.items.Length)
                {
                    groups.Add(item!);
                    removed.Add(false);
                    continue;
                }
                changed = true;
                if (retained.Count == 0)
                {
                    removed.Add(true);
                    continue;
                }

                var copy = new OptionGroup(group.Name, retained.ToArray())
                {
                    Flags = group.Flags,
                    InteractableTest = group.InteractableTest
                };
                groups.Add(copy.Cast<ISelectionGroup>());
                removed.Add(false);
            }

            if (!changed) return;
            __0 = new Il2CppReferenceArray<ISelectionGroup>(groups.ToArray());
            __1 = SightMenuPolicy.RemapActive(__1, removed);
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static bool IsVanillaOption(Option? option)
    {
        if (option == null) return false;
        try
        {
            if (option.Tags != null)
                foreach (var tag in option.Tags)
                    if (SightMenuPolicy.IsVanillaMarker(tag)) return true;
            if (option.Data != null)
            {
                var card = option.Data.TryCast<PartDisplayCard>();
                if (card != null && SightMenuPolicy.IsVanillaGuid(card.PartGuid)) return true;
                var definition = option.Data.TryCast<PartDefinition>();
                if (definition != null && SightMenuPolicy.IsVanillaGuid(definition.guid)) return true;
            }
        }
        catch (Exception ex) { Warn(ex); }
        return false;
    }

    private static void Warn(Exception ex)
    {
        if (warned) return;
        warned = true;
        Plugin.ModLog.LogWarning("[Sight picker] Optional icon/filter integration unavailable; native picker retained: " + ex.Message);
    }

    internal static void Clear()
    {
        SightIcons.Clear();
        ExpeditionState.Reset();
        warned = false;
    }
}
