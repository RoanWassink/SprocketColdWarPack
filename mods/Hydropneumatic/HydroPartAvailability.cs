using DesignerFramework.AssetManagement;
using DesignerFramework.UI;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Selection;
using Sprocket.VehicleDesigner;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles;

namespace SprocketHydropneumatic;

// Exact panel-to-own-editor binding for the HPS part menu. No global editor or
// scene scan is used; every panel is resolved to the editor that created it.
[HarmonyPatch]
internal static class HydroPartAvailability
{
    private const int MaximumOwners = 128;
    private static readonly Dictionary<IntPtr, IVehicleEditor> Owners = new();
    private static readonly HashSet<IntPtr> Conflicted = new();
    private static readonly HashSet<string> Warnings = new();

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponentSelectionDisplay), nameof(VehicleComponentSelectionDisplay.CreatePlugins))]
    private static void Bind(VehicleComponentSelectionDisplay __instance, EditorPluginLoadContext __1)
    {
        try
        {
            var panel = __instance?.display;
            var editor = __1?.Editor;
            if (panel == null || editor == null) return;
            var pointer = panel.Pointer;
            if (Conflicted.Contains(pointer)) return;
            if (Owners.TryGetValue(pointer, out var existing))
            {
                if (existing.Pointer != editor.Pointer)
                {
                    Owners.Remove(pointer);
                    Conflicted.Add(pointer);
                    Warn("conflicting owner for panel " + pointer);
                }
                return;
            }
            if (Owners.Count >= MaximumOwners)
            {
                Warn("owner table full; new panel left unavailable");
                return;
            }
            Owners[pointer] = editor;
        }
        catch (Exception ex) { Warn("bind unavailable: " + ex.Message); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponentSelectionDisplayEditorPlugin), nameof(VehicleComponentSelectionDisplayEditorPlugin.Release))]
    private static void Release(VehicleComponentSelectionDisplayEditorPlugin __instance)
    {
        try
        {
            var panel = __instance?.display;
            if (panel == null) return;
            Owners.Remove(panel.Pointer);
            Conflicted.Remove(panel.Pointer);
        }
        catch (Exception ex) { Warn("release unavailable: " + ex.Message); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.Detach))]
    private static void Detach(VehicleEditor __instance)
    {
        try
        {
            foreach (var pair in Owners.Where(pair => pair.Value.Pointer == __instance.Pointer).ToArray())
                Owners.Remove(pair.Key);
            Conflicted.Clear();
        }
        catch (Exception ex) { Warn("detach unavailable: " + ex.Message); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(SelectionPanel), nameof(SelectionPanel.SetDisplayOptions))]
    private static void Filter(SelectionPanel __instance, ref Il2CppReferenceArray<ISelectionGroup> __0, ref int __1)
    {
        if (__0 == null || Allows(__instance)) return;
        try
        {
            var groups = new List<ISelectionGroup>();
            int newActive = -1, oldIndex = 0;
            bool activeRemoved = false, anyOwn = false;
            foreach (var item in __0)
            {
                bool wasActive = oldIndex++ == __1;
                var group = item?.TryCast<OptionGroup>();
                if (group?.items == null)
                {
                    if (wasActive) newActive = groups.Count;
                    groups.Add(item!);
                    continue;
                }
                var retained = new List<Option>();
                bool changed = false;
                foreach (var option in group.items)
                {
                    if (IsOwn(option)) { changed = true; anyOwn = true; }
                    else retained.Add(option!);
                }
                if (!changed)
                {
                    if (wasActive) newActive = groups.Count;
                    groups.Add(item!);
                    continue;
                }
                if (retained.Count > 0)
                {
                    var copy = new OptionGroup(group.Name, retained.ToArray())
                    { Flags = group.Flags, InteractableTest = group.InteractableTest };
                    if (wasActive) newActive = groups.Count;
                    groups.Add(copy.Cast<ISelectionGroup>());
                }
                else if (wasActive) activeRemoved = true;
            }
            if (!anyOwn) return;
            var active = groups.Count == 0 ? 0 : Math.Clamp(activeRemoved ? __1 : newActive, 0, groups.Count - 1);
            __0 = new Il2CppReferenceArray<ISelectionGroup>(groups.ToArray());
            __1 = active;
        }
        catch (Exception ex) { Warn("filter unavailable; native selection retained: " + ex.Message); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(SelectionPanel), nameof(SelectionPanel.OnClickSelection))]
    private static bool Select(SelectionPanel __instance, int __0)
    {
        try
        {
            var group = __instance.activeGroup?.TryCast<OptionGroup>();
            if (group?.items != null && __0 >= 0 && __0 < group.items.Length &&
                IsOwn(group.items[__0]) && !Allows(__instance)) return false;
        }
        catch (Exception ex) { Warn("selection guard unavailable: " + ex.Message); }
        return true;
    }

    private static bool IsOwn(Option? option)
    {
        if (option?.Tags == null) return false;
        foreach (var tag in option.Tags)
            if (string.Equals(tag, HydroPart.MenuMarker, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool Allows(SelectionPanel panel) => HydroEraAccess.Allowed(Target(panel));

    private static IVehicleGateway? Target(SelectionPanel panel)
    {
        if (panel == null || Conflicted.Contains(panel.Pointer) ||
            !Owners.TryGetValue(panel.Pointer, out var editor)) return null;
        try { return editor.Target; }
        catch (Exception ex) { Warn("target unavailable: " + ex.Message); return null; }
    }

    private static void Warn(string detail)
    {
        if (Warnings.Count < 32 && Warnings.Add(detail))
            Plugin.ModLog.LogWarning("[Hydro] Part-menu availability: " + detail);
    }
}
