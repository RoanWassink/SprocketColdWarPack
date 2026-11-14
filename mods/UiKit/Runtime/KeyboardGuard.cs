using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
namespace SprocketUiKit;

/// <summary>Shared ownership for panel and text focus. No proxy selection for panel focus.</summary>
internal static class KeyboardGuard
{
    private static readonly SuspensionSet<InputAction> owned = new();
    private static readonly RescanThrottle throttle = new();
    private static readonly HashSet<nint> uiMaps = new();
    private static readonly Action<InputAction> disable = static action => action.Disable();
    private static readonly Action<InputAction> enable = static action => action.Enable();
    internal static int Suspended => owned.Count;
    internal static void Tick(string reason = "focus scan")
    {
        bool requested = InputFocusPolicy.Block(Kit.KeyboardFocus != null, Kit.Focus != null,
            Plugin.BlockKeysWhilePanelFocused?.Value ?? true, Plugin.SuspendGameKeys?.Value ?? true);
        if (!requested)
        {
            throttle.Due(false, Time.unscaledTime);
            Restore(reason); // also repairs any orphaned set in this frame
            return;
        }
        if (!throttle.Due(true, Time.unscaledTime)) return;
        int disabled = 0;
        try
        {
            var ui = EventSystem.current?.currentInputModule?.TryCast<InputSystemUIInputModule>();
            if (ui == null) return; // preserve the existing safe selection policy
            uiMaps.Clear();
            AddUiMap(ui.point); AddUiMap(ui.leftClick); AddUiMap(ui.rightClick); AddUiMap(ui.middleClick);
            AddUiMap(ui.scrollWheel); AddUiMap(ui.move); AddUiMap(ui.submit); AddUiMap(ui.cancel);
            if (uiMaps.Count == 0) return;
            foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
            {
                if (asset == null || (asset.hideFlags & HideFlags.DontSave) != 0) continue;
                foreach (var map in asset.actionMaps)
                {
                    if (uiMaps.Contains(map.Pointer)) continue;
                    foreach (var action in map.actions)
                        if (action.enabled) { owned.Suspend(action.Pointer, action, true, disable); disabled++; }
                }
            }
            if (Plugin.Detailed && disabled > 0)
                Plugin.Trace($"[UI Kit] Keys disabled: {disabled} actions ({reason}); owned={owned.Count}.");
        }
        catch (Exception ex)
        {
            // Restore every owned action before reporting, even when Disable threw halfway through.
            Kit.ReleaseKeyboard("guard exception");
            Guard.Report("Keyboard input suspension", ex);
        }
    }
    private static void AddUiMap(InputActionReference? reference)
    { if (reference?.action?.actionMap is { } map) uiMaps.Add(map.Pointer); }
    internal static void Restore(string reason)
    {
        int count = owned.Count;
        if (count == 0) return;
        var failure = owned.Restore(enable);
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] Keys restored: {count-owned.Count} actions ({reason}); remaining={owned.Count}.");
        if (failure != null) Guard.Report("Keyboard input restore", failure);
    }
    internal static void Reset(string reason)
    { throttle.Due(false, Time.unscaledTime); Restore(reason); }
}
