using DesignerFramework.UI;
using Sprocket.VehicleDesigner;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles;

namespace SprocketExpandedBallistics.Materials;

// Exact panel-to-own-editor ownership for the ERA part menu. The binding is
// created only from VehicleComponentSelectionDisplay.CreatePlugins; no global
// "current editor" is consulted.
internal static class EraPartAvailability
{
    private const int MaximumOwners = 128;
    private static readonly Dictionary<IntPtr, IVehicleEditor> Owners = new();
    private static readonly HashSet<IntPtr> Conflicted = new();
    private static readonly HashSet<string> Warnings = new();

    internal static void Bind(VehicleComponentSelectionDisplay? display, EditorPluginLoadContext? context)
    {
        try
        {
            var panel = display?.display;
            var editor = context?.Editor;
            if (panel == null || editor == null) return;
            var pointer = panel.Pointer;
            if (Conflicted.Contains(pointer)) return;
            if (Owners.TryGetValue(pointer, out var existing))
            {
                // The same panel must never silently switch owners. Fail closed and
                // keep the conflict until the real plugin/editor teardown.
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

    internal static void Release(SelectionPanel? panel)
    {
        if (panel == null) return;
        Owners.Remove(panel.Pointer);
        Conflicted.Remove(panel.Pointer);
    }

    internal static void ReleaseEditor(IntPtr editorPointer)
    {
        foreach (var pair in Owners.Where(pair => pair.Value.Pointer == editorPointer).ToArray())
            Owners.Remove(pair.Key);
        Conflicted.Clear();
    }

    internal static IVehicleGateway? Target(SelectionPanel? panel)
    {
        if (panel == null || Conflicted.Contains(panel.Pointer) ||
            !Owners.TryGetValue(panel.Pointer, out var editor)) return null;
        try { return editor.Target; }
        catch (Exception ex) { Warn("target unavailable: " + ex.Message); return null; }
    }

    internal static bool AllowsOwnPart(SelectionPanel? panel) => AllowsVehicle(Target(panel));

    internal static bool AllowsVehicle(IVehicleGateway? vehicle)
    {
        try
        {
            // The native part menu already owns part/technology membership and
            // the part's own Technology date. No era name or blanket modern floor
            // is added here; a missing part in the frame still falls outside it.
            var design = vehicle?.DesignInfo;
            var tech = vehicle?.Tech;
            if (design == null || tech == null) return false;
            return SprocketExpandedBallistics.VehicleEraAccess.AllowsPlacedPart(
                design.Date, null, true, true);
        }
        catch (Exception ex) { Warn("vehicle date unavailable: " + ex.Message); return false; }
    }

    private static void Warn(string detail)
    {
        if (Warnings.Count < 32 && Warnings.Add(detail))
            MaterialFeature.ModLog.LogWarning("ERA part-menu availability: " + detail);
    }
}
