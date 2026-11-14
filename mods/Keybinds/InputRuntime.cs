using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SprocketKeybinds;

// Button edges are polled once per frame on the main thread. Il2CppInterop cannot convert delegates with
// struct parameters (InputAction.CallbackContext), so no native input callback is used. Edge order inside
// a single frame is not observable: a modifier and main key pressed in the same frame count as modifier-first.
internal static class InputRuntime
{
    private static readonly Dictionary<string, (bool Down, Modifiers PressModifiers, Modifiers RetainedModifiers)> buttons = new(StringComparer.OrdinalIgnoreCase);
    private static (string Path, ButtonControl Control, bool Modifier)[] controls = Array.Empty<(string, ButtonControl, bool)>();
    private static Keyboard? polledKeyboard;
    private static Mouse? polledMouse;
    private static int sampledFrame = -1;
    private static int polledFrame = -1;
    private static bool pollFailed;
    private static bool wasCapturing;
    internal static Modifiers HeldModifiers
    {
        get
        {
            var k = Keyboard.current;
            if (k == null) return Modifiers.None;
            return (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed ? Modifiers.Shift : 0)
                | (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed ? Modifiers.Ctrl : 0)
                | (k.leftAltKey.isPressed || k.rightAltKey.isPressed ? Modifiers.Alt : 0);
        }
    }
    internal static void Initialize() => RefreshControls();
    private static void RefreshControls()
    {
        polledKeyboard = Keyboard.current;
        polledMouse = Mouse.current;
        var list = new List<(string, ButtonControl, bool)>();
        if (polledKeyboard != null)
            foreach (var key in Enum.GetValues<Key>())
            {
                if (key == Key.None || key == Key.IMESelected) continue;
                KeyControl? control = null;
                try { control = polledKeyboard[key]; } catch { }
                if (control == null || control.synthetic) continue;
                list.Add(("<keyboard>/" + control.name.ToLowerInvariant(), control, ButtonBinding.Modifier(control.name) != Modifiers.None));
            }
        if (polledMouse != null)
            foreach (var control in new[] { polledMouse.leftButton, polledMouse.rightButton, polledMouse.middleButton, polledMouse.forwardButton, polledMouse.backButton })
                if (control != null) list.Add(("<mouse>/" + control.name.ToLowerInvariant(), control, false));
        controls = list.ToArray();
    }
    // Called first in the driver's Update and lazily by Sample, so capture and handle reads see the same edges.
    internal static void Poll()
    {
        if (pollFailed || polledFrame == Time.frameCount) return;
        polledFrame = Time.frameCount;
        try
        {
            if (Keyboard.current != polledKeyboard || Mouse.current != polledMouse) RefreshControls();
            // Outside rebind capture only combo main keys and modifiers matter; with no combos nothing is polled.
            bool capturing = VanillaMenu.Rebinding;
            bool captureStarted = capturing && !wasCapturing;
            wasCapturing = capturing;
            if (!capturing && watched.Count == 0) return;
            var modifiers = HeldModifiers;
            // Unwatched keys went unpolled before capture: record their real state silently so a key
            // already held is not reported as a new press and a stale "down" cannot hide the next press.
            if (captureStarted)
                foreach (var (path, control, isModifier) in controls)
                    if (!isModifier && !watched.Contains(path))
                    {
                        var held = control.isPressed;
                        buttons[path] = (held, modifiers, modifiers);
                    }
            bool modifierChanged = false;
            foreach (var (path, control, isModifier) in controls)
            {
                if (!capturing && !isModifier && !watched.Contains(path)) continue;
                bool down = control.isPressed;
                var previous = buttons.GetValueOrDefault(path);
                if (previous.Down == down) continue;
                buttons[path] = (down, down ? modifiers : previous.PressModifiers, down ? modifiers : previous.RetainedModifiers);
                modifierChanged |= isModifier;
                VanillaMenu.CaptureEvent(path, down, modifiers);
            }
            if (modifierChanged)
                foreach (var key in buttons.Keys.ToArray())
                {
                    var state = buttons[key];
                    if (state.Down) buttons[key] = (state.Down, state.PressModifiers, state.RetainedModifiers & modifiers);
                }
        }
        catch (Exception e)
        {
            // Without edge tracking combos cannot resolve; plain bindings keep native polling below.
            pollFailed = true;
            Plugin.Logger.LogWarning("[Keybinds] Combo input polling disabled after error; plain bindings unaffected: " + e);
        }
    }
    private static readonly HashSet<string> watched = new(StringComparer.OrdinalIgnoreCase);
    internal static void Invalidate()
    {
        sampledFrame = -1;
        watched.Clear();
        foreach (var entry in Keybinds.Entries)
            foreach (var binding in entry.Bindings)
                if (binding.Modifiers != Modifiers.None) watched.Add(binding.Path);
    }
    internal static bool IsManaged(InputAction action) => Keybinds.Entries.Any(e => action.actionMap?.asset?.Pointer == e.Asset.Pointer);
    internal static void Sample()
    {
        Poll();
        if (sampledFrame == Time.frameCount) return;
        sampledFrame = Time.frameCount;
        var entries = Keybinds.Entries.ToArray();
        var all = entries.SelectMany(e => e.Bindings).ToArray();
        var modifiers = HeldModifiers;
        bool allowed = Keybinds.InputAllowed;
        foreach (var entry in entries)
        {
            var bindings = entry.Bindings.ToArray();
            // Keep the entire native polling behavior for unaffected legacy actions (interactions/processors included).
            bool needsResolution = bindings.Any(b => all.Any(other => other.Path == b.Path && other.Modifiers != Modifiers.None));
            if (!needsResolution)
            {
                entry.State = (allowed && entry.Action.IsPressed(), allowed && entry.Action.WasPressedThisFrame(), allowed && entry.Action.WasReleasedThisFrame());
                continue;
            }
            var states = new (bool Held, bool Pressed, bool Released)[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                var observed = buttons.GetValueOrDefault(binding.Path);
                var slot = entry.Slots[i];
                states[i] = entry.Latches[i].Step(binding, slot.IsPressed(), slot.WasPressedThisFrame(),
                    modifiers, all, allowed, observed.PressModifiers, observed.RetainedModifiers);
            }
            bool wasHeld = entry.State.Held;
            // Per-slot native probes preserve processors/thresholds and prevent a suppressed primary
            // from swallowing a press on the secondary. The original action retains aggregate re-press edges.
            entry.State = BindingLatch.Combine(wasHeld, states, entry.Action.WasPressedThisFrame(), allowed);
        }
    }
}
