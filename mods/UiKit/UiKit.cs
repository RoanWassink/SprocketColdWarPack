using SprocketKeybinds;
namespace SprocketUiKit;

/// <summary>
/// Sprocket UI Kit public API (stable within 0.1.x). Reference SprocketUiKit.dll with Private=false
/// and declare <c>[BepInDependency(UiKit.PluginGuid, "&gt;=0.1.0 &lt;0.2.0")]</c>.
/// Call from the Unity main thread (plugin Load or later).
/// </summary>
public static class UiKit
{
    /// <summary>Stable BepInEx owner identifier for the UI Kit.</summary>
    public const string PluginGuid = "sprocket.uikit";
    /// <summary>UI Kit semantic version.</summary>
    public const string Version = "0.1.13";
    /// <summary>The keyboard-focus owner (never a HUD), acquired on open/inside press and released or handed on by outside press, close or lifecycle changes (see README.md).</summary>
    public static Panel? FocusedPanel => Kit.KeyboardFocus;

    /// <summary>
    /// Create a panel (once, e.g. in Load). <paramref name="modGuid"/> + <paramref name="id"/> must be
    /// unique and stable: the position is remembered under that key. Duplicate creation throws.
    /// </summary>
    public static Panel CreatePanel(string modGuid, string id, string title, string? subtitle = null, float width = 900, float height = 560)
        => Kit.Create(modGuid, id, title, subtitle, width, height);

    /// <summary>Remove a panel created by your mod (plugin unload).</summary>
    public static void DestroyPanel(Panel panel) => Kit.Destroy(panel);

    /// <summary>Toggle <paramref name="panel"/> when the Keybinds API action is pressed (ignored while typing or in the settings menu).</summary>
    public static void BindToggle(Panel panel, ModKeybind keybind)
    {
        if (panel == null || keybind == null) throw new ArgumentNullException(panel == null ? nameof(panel) : nameof(keybind));
        Kit.Toggles.RemoveAll(t => t.Panel == panel);
        Kit.Toggles.Add((panel, keybind));
        panel.ToggleKey = keybind;
    }

    /// <summary>Current game context: Designer, Game (own vehicle in control) or Other (menus).</summary>
    public static UiContexts Context => Kit.Context;
    /// <summary>"DESIGNER", "IN GAME" or null; the default context chip text.</summary>
    public static string? ContextLabel => Kit.Context switch { UiContexts.Designer => "DESIGNER", UiContexts.Game => "IN GAME", _ => null };
    /// <summary>True while at least one normal panel is logically open, including a panel waiting for Settings to close.</summary>
    public static bool AnyPanelOpen => Kit.OpenCount > 0;
    /// <summary>True while the mouse is over (or dragging/pressing in) an open kit panel or dropdown.</summary>
    public static bool PointerOverPanel => Kit.PointerCaptured();
    /// <summary>True while a kit text field has keyboard focus (skip your own key handling then).</summary>
    public static bool TextInputActive => Kit.Focus != null;
    /// <summary>Current UI scale (screen height / 1080 × Display.Scale).</summary>
    public static float Scale => Kit.Scale > 0 ? Kit.Scale : KitMath.Scale(UnityEngine.Screen.height, Plugin.ScaleFactor?.Value ?? 1f);

    // ---------- 0.1.2 (declare [BepInDependency(UiKit.PluginGuid, ">=0.1.2 <0.2.0")] to use these) ----------
    /// <summary>
    /// Create a non-interactive HUD overlay (once). Default: top-left, game context only, hidden while the
    /// cursor is free / a kit panel is open / paused. Duplicate modGuid+id throws.
    /// </summary>
    public static HudPanel CreateHud(string modGuid, string id, float width = 420)
    {
        if (string.IsNullOrWhiteSpace(modGuid) || string.IsNullOrWhiteSpace(id)) throw new ArgumentException("modGuid and id are required.");
        if (HudLayer.Huds.Any(h => h.ModGuid == modGuid && h.Id == id)) throw new InvalidOperationException($"HUD {modGuid}/{id} already exists.");
        var hud = new HudPanel(modGuid, id, width);
        HudLayer.Huds.Add(hud);
        return hud;
    }
    /// <summary>Remove a HUD and destroy its retained objects.</summary>
    public static void DestroyHud(HudPanel hud) { if (hud != null) HudLayer.Destroy(hud); }
    /// <summary>Create a world marker (ring + label above a Transform or world point) on the HUD layer. Duplicate throws.</summary>
    public static WorldMarker CreateMarker(string modGuid, string id)
    {
        string key = modGuid + "/" + id;
        if (string.IsNullOrWhiteSpace(modGuid) || string.IsNullOrWhiteSpace(id)) throw new ArgumentException("modGuid and id are required.");
        if (HudLayer.Markers.ContainsKey(key)) throw new InvalidOperationException($"Marker {key} already exists.");
        var m = new WorldMarker(key);
        HudLayer.Markers[key] = m;
        return m;
    }
    /// <summary>Remove a world marker and destroy its retained objects.</summary>
    public static void DestroyMarker(WorldMarker marker) { if (marker != null && HudLayer.Markers.Remove(marker.Key)) marker.Destroy(); }
    /// <summary>"#rrggbb" or "#rrggbbaa" to a Unity colour (throws on bad input).</summary>
    public static UnityEngine.Color Colour(string hex) => Draw.C(Rgba.Hex(hex));
}
