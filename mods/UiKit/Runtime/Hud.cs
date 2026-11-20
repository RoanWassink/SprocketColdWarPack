using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace SprocketUiKit;

/// <summary>
/// Non-interactive overlay (0.1.2): anchored to a screen corner, auto height, same elements as a
/// panel (Label, Row, Lamp, Progress, Chip, Card, Repeat, Canvas...). Never hit-tested: no raycast,
/// no cursor change, no input block, no Esc; not counted in <see cref="UiKit.AnyPanelOpen"/> or
/// <see cref="UiKit.PointerOverPanel"/>. Own canvas below the kit panels. Getters are only polled
/// while the HUD is shown. Interactive components placed in a HUD are inert.
/// </summary>
public sealed class HudPanel
{
    internal readonly Panel Host;
    internal HudCorner Corner = HudCorner.TopLeft;
    internal float OffsetX = 16, OffsetY = 16, MaxHeightPx = 600, FixedHeight;
    private Func<bool>? visibleGetter;
    private bool shown = true;
    internal float FadeAlpha;
    private CanvasGroup? group;
    internal bool WasVisible;
    internal HudPanel(string modGuid, string id, float width)
    {
        Host = new Panel(modGuid, id, "", null, MathF.Max(40, width), 40, hud: true) { AllowedContexts = UiContexts.Game };
        Host.Hud = this;
    }
    /// <summary>Stable identifier of the mod that owns this panel.</summary>
    public string ModGuid => Host.ModGuid;
    /// <summary>Stable panel identifier within the owning mod.</summary>
    public string Id => Host.Id;
    /// <summary>Content; the same factories as a panel body.</summary>
    public Stack Body => Host.Body;
    /// <summary>Corner and inward offset in reference px (default top-left, 16, 16).</summary>
    public HudPanel Anchor(HudCorner corner, float offsetX = 16, float offsetY = 16) { Corner = corner; OffsetX = offsetX; OffsetY = offsetY; Host.MarkDirty(); return this; }
    /// <summary>Shown while this returns true (evaluated every frame, also while hidden).</summary>
    public HudPanel Visible(Func<bool> when) { visibleGetter = when; return this; }
    /// <summary>Auto height up to this (reference px); content beyond is clipped.</summary>
    public HudPanel MaxHeight(float px) { MaxHeightPx = MathF.Max(20, px); Host.MarkDirty(); return this; }
    /// <summary>Fixed height in reference px; 0 = auto (default).</summary>
    public float Height { get => FixedHeight; set { FixedHeight = MathF.Max(0, value); Host.MarkDirty(); } }
    /// <summary>Current panel width in Kit units.</summary>
    public float Width { get => Host.W; set { Host.W = MathF.Max(40, value); Host.MarkDirty(); } }
    /// <summary>Default <see cref="UiContexts.Game"/>.</summary>
    public UiContexts AllowedContexts { get => Host.AllowedContexts; set => Host.AllowedContexts = value; }
    /// <summary>
    /// Default true: hidden while the cursor is visible in game, any kit panel is open, or the game is
    /// paused. Set false for a HUD that should stay up (e.g. in the designer).
    /// </summary>
    public bool HideWhenCursorFree { get; set; } = true;
    /// <summary>Background alpha (default .82; 0 = text only).</summary>
    public float Opacity { get => Host.HudOpacity; set => Host.SetHudOpacity(value); }
    /// <summary>Multiplier on the kit scale (default 1).</summary>
    public float ScaleFactor { get => scale; set { scale = Math.Clamp(value, .25f, 4f); Host.MarkDirty(); } }
    private float scale = 1;
    /// <summary>Fade time for show/hide in seconds (default .15; 0 = instant).</summary>
    public float FadeSeconds { get; set; } = .15f;
    /// <summary>Show (default state) / hide by code, on top of <see cref="Visible"/>.</summary>
    public void Show() => shown = true;
    /// <summary>Hide the HUD until Show is called again.</summary>
    public void Hide() => shown = false;
    /// <summary>True while the HUD is on screen (after the visibility rules).</summary>
    public bool IsShown => WasVisible;

    internal bool WantVisible(UiContexts context, bool cursorFree, bool panelsOpen, bool paused)
    {
        if (!shown || (Host.AllowedContexts & context) == 0) return false;
        if (HideWhenCursorFree && (paused || panelsOpen || cursorFree && context == UiContexts.Game)) return false;
        return visibleGetter == null || Guard.Get(Host.Key + ".Visible", visibleGetter, false);
    }
    /// <summary>Per frame on the HUD layer; returns false when it is fully hidden (nothing polled).</summary>
    internal void Frame(RectTransform layer, bool visible, float now, float dt)
    {
        FadeAlpha = Logic.Fade(FadeAlpha, visible, FadeSeconds, dt);
        bool on = FadeAlpha > .001f;
        WasVisible = visible;
        if (!on) { if (Host.Rt != null) Draw.SetActive(Host.Rt, false); return; }
        bool fresh = !Host.Built;
        Host.EnsureBuilt(layer);
        if (fresh || group == null) { group = Host.Rt!.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false; Host.MarkDirty(); }
        if (MathF.Abs(group.alpha - FadeAlpha) > .001f) group.alpha = FadeAlpha;
        Host.Tick(now);
    }
    internal void Unbuilt() { group = null; Host.Invalidate(); }
}

/// <summary>
/// Screen marker above a world point or Transform (0.1.2): a ring with an optional label, drawn on
/// the HUD layer. Shown in its allowed contexts (default designer and game) also while panels are
/// open, hidden when the point is behind the camera. Never interactive.
/// </summary>
public sealed class WorldMarker
{
    internal readonly string Key;
    private Func<Vector3?>? position; private Func<Transform?>? target;
    private Func<string?>? label; private Func<bool>? visibleGetter;
    internal Rgba Tint = Tok.Accent;
    private float offsetPx = 0, size = 18;
    public UiContexts AllowedContexts { get; set; } = UiContexts.DesignerAndGame;
    private RectTransform? rt; private Image? ring, dot; private TextMeshProUGUI? text, shadow;
    internal WorldMarker(string key) { Key = key; }
    /// <summary>World position from a getter (null = hidden).</summary>
    public WorldMarker At(Func<Vector3?> worldPosition) { position = worldPosition; target = null; return this; }
    /// <summary>Follow a Transform (null or destroyed = hidden).</summary>
    public WorldMarker Follow(Func<Transform?> transform) { target = transform; position = null; return this; }
    public WorldMarker Label(Func<string?> text) { label = text; return this; }
    public WorldMarker Visible(Func<bool> when) { visibleGetter = when; return this; }
    /// <summary>Marker colour (default accent #dc9e33).</summary>
    public WorldMarker Colour(Color colour) { Tint = Draw.R(colour); return this; }
    /// <summary>Ring diameter in reference px (default 18) and a vertical lift in px (default 0).</summary>
    public WorldMarker Size(float diameter, float liftPx = 0) { size = MathF.Max(6, diameter); offsetPx = liftPx; return this; }
    private bool shown = true;
    public void Show() => shown = true;
    public void Hide() => shown = false;

    internal void Frame(RectTransform layer, UiContexts context, float scale, float viewH)
    {
        Vector3? world = null;
        bool want = shown && (AllowedContexts & context) != 0 && (visibleGetter == null || Guard.Get(Key + ".Visible", visibleGetter, false));
        if (want)
        {
            if (target != null) { var t = Guard.Get(Key + ".Follow", target, null); try { if (t != null) world = t.position; } catch { world = null; } }
            else if (position != null) world = Guard.Get(Key + ".At", position, null);
        }
        Vector3 sp = default;
        if (world != null)
        {
            try { var cam = Camera.main; if (cam == null) world = null; else sp = cam.WorldToScreenPoint(world.Value); }
            catch (Exception ex) { Guard.Report(Key + ".Camera", ex); world = null; }
        }
        bool visible = world != null && sp.z > 0;
        if (!visible) { if (rt != null) Draw.SetActive(rt, false); return; }
        if (rt == null) Build(layer);
        Draw.SetActive(rt, true);
        var k = KitMath.ScreenToKit(sp.x, sp.y, Screen.height, scale);
        float y = k.Y - offsetPx;
        Draw.Place(rt!, k.X - size / 2, y - size / 2, size, size);
        Draw.SetColor(ring, Tint); Draw.SetColor(dot, Tint);
        string? l = label != null ? Guard.Get(Key + ".Label", label, null) : null;
        Draw.SetActive(text, !string.IsNullOrEmpty(l)); Draw.SetActive(shadow, !string.IsNullOrEmpty(l));
        if (!string.IsNullOrEmpty(l)) { Draw.SetText(text, l); Draw.SetText(shadow, l); }
    }
    private void Build(RectTransform layer)
    {
        rt = Draw.Node("Marker " + Key, layer);
        ring = Draw.Child(rt, "Ring", Assets.Circle(true), Tint); Draw.Stretch(ring.rectTransform);
        dot = Draw.Child(rt, "Dot", Assets.Circle(), Tint); Draw.Stretch(dot.rectTransform, size * .38f, size * .38f, size * .38f, size * .38f);
        shadow = Draw.Text(Draw.Node("Shadow", rt), TextStyle.Label); shadow.color = new Color(0, 0, 0, .85f); shadow.alignment = TextAlignmentOptions.Bottom;
        text = Draw.Text(Draw.Node("Label", rt), TextStyle.Label); text.alignment = TextAlignmentOptions.Bottom;
        Draw.Place(shadow.rectTransform, size / 2 - 150 + 1, -26 + 1, 300, 22);
        Draw.Place(text.rectTransform, size / 2 - 150, -26, 300, 22);
    }
    internal void Unbuilt() { rt = null; ring = dot = null; text = shadow = null; }
    internal void Destroy() { if (rt != null) Object.Destroy(rt.gameObject); Unbuilt(); }
}

/// <summary>The HUD canvas (sorting order 80, no raycaster), its HUDs and world markers.</summary>
internal static class HudLayer
{
    internal static readonly List<HudPanel> Huds = new();
    internal static readonly Dictionary<string, WorldMarker> Markers = new(StringComparer.Ordinal);
    private static GameObject? root; private static Canvas? canvas; private static RectTransform? hudNode, markerNode;
    private static float scale = -1;

    internal static void Update(UiContexts context, bool configuring, bool panelsOpen, bool paused)
    {
        if (Huds.Count == 0 && Markers.Count == 0) { if (root != null && root.activeSelf) root.SetActive(false); return; }
        if (configuring) { if (root != null && root.activeSelf) root.SetActive(false); return; }
        bool cursorFree = false;
        try { cursorFree = Cursor.visible && Cursor.lockState != CursorLockMode.Locked; } catch { }
        bool any = false;
        foreach (var h in Huds) { bool v = h.WantVisible(context, cursorFree, panelsOpen, paused); any |= v || h.FadeAlpha > .001f; h.WasVisible = v; }
        bool markers = Markers.Count > 0;
        if (!any && !markers) { if (root != null && root.activeSelf) root.SetActive(false); return; }
        if (!EnsureCanvas()) return;
        if (!root!.activeSelf) root.SetActive(true);
        float s = KitMath.Scale(Screen.height, Plugin.ScaleFactor?.Value ?? 1f);
        if (MathF.Abs(s - scale) > .0001f || MathF.Abs(Screen.width / s - Kit.HudViewW) > .5f || MathF.Abs(Screen.height / s - Kit.HudViewH) > .5f)
        {
            scale = s; canvas!.scaleFactor = s; Kit.HudViewW = Screen.width / s; Kit.HudViewH = Screen.height / s;
            foreach (var h in Huds) h.Host.MarkDirty();
        }
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        foreach (var h in Huds.ToList())
        {
            try { h.Frame(hudNode!, h.WasVisible, now, dt); }
            catch (Exception ex) { Guard.Report(h.Host.Key + ".Hud", ex); }
        }
        foreach (var m in Markers.Values.ToList())
        {
            try { m.Frame(markerNode!, context, scale, Kit.HudViewH); }
            catch (Exception ex) { Guard.Report(m.Key + ".Marker", ex); }
        }
    }
    private static bool EnsureCanvas()
    {
        bool alive = false;
        try { alive = root != null && canvas != null && hudNode != null; } catch { }
        if (alive) return true;
        if (Assets.Font == null) return false;
        root = new GameObject("SprocketUiKit.Hud") { layer = 5 };
        root.AddComponent<RectTransform>();
        Object.DontDestroyOnLoad(root);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Tok.HudSortingOrder;
        canvas.pixelPerfect = false;
        var group = root.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
        var crt = root.GetComponent<RectTransform>();
        markerNode = Draw.Node("Markers", crt);
        hudNode = Draw.Node("Huds", crt);
        foreach (var h in Huds) h.Unbuilt();
        foreach (var m in Markers.Values) m.Unbuilt();
        scale = -1;
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] HUD canvas created (sort {Tok.HudSortingOrder}, no raycaster).");
        return true;
    }
    internal static void Destroy(HudPanel hud)
    {
        Huds.Remove(hud);
        if (hud.Host.Rt != null) Object.Destroy(hud.Host.Rt.gameObject);
        hud.Unbuilt();
    }
    internal static void Shutdown()
    {
        if (root != null) Object.Destroy(root);
        root = null; canvas = null; hudNode = markerNode = null;
        foreach (var h in Huds) h.Unbuilt();
        foreach (var m in Markers.Values) m.Unbuilt();
    }
}
