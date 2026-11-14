using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace SprocketUiKit;

/// <summary>Font and procedural sprites, created once and protected from unused-asset collection.</summary>
internal static class Assets
{
    private static TMP_FontAsset? font;
    private static float nextFontTry;
    private static readonly Dictionary<string, Sprite> Sprites = new(StringComparer.Ordinal);
    private static readonly List<Object> Owned = new();
    internal static string FontRoute { get; private set; } = "none";
    internal const float ShadowBlur = 24;

    /// <summary>
    /// Game TMP font. Route (config Display.FontSource): "Default" = TMP_Settings.defaultFontAsset
    /// first (Ballistics HUD / Expedition route), then the font of a live scene TMP_Text (JSON Editor
    /// route); "Scene" swaps the order. Null until one is available; retried every 0.5 s.
    /// </summary>
    internal static TMP_FontAsset? Font
    {
        get
        {
            if (font != null) return font;
            if (Time.unscaledTime < nextFontTry) return null;
            nextFontTry = Time.unscaledTime + .5f;
            bool sceneFirst = string.Equals(Plugin.FontSource?.Value, "Scene", StringComparison.OrdinalIgnoreCase);
            font = sceneFirst ? SceneFont() ?? DefaultFont() : DefaultFont() ?? SceneFont();
            if (font != null)
            {
                Plugin.Instance.Log.LogInfo($"[UI Kit] Font: '{font.name}' via {FontRoute}" + (Plugin.Detailed ? $" (default asset '{SafeName(DefaultFontQuiet())}', scene font '{SafeName(SceneFontQuiet())}')." : "."));
            }
            return font;
        }
    }
    private static TMP_FontAsset? DefaultFont()
    {
        var f = DefaultFontQuiet();
        if (f != null) FontRoute = "TMP_Settings.defaultFontAsset";
        return f;
    }
    private static TMP_FontAsset? SceneFont()
    {
        var f = SceneFontQuiet();
        if (f != null) FontRoute = "scene TMP_Text (JSON Editor route)";
        return f;
    }
    private static TMP_FontAsset? DefaultFontQuiet() { try { return TMP_Settings.defaultFontAsset; } catch { return null; } }
    private static TMP_FontAsset? SceneFontQuiet()
    {
        try
        {
            // Most used font among live TMP texts = the font the game's visible UI uses.
            var counts = new Dictionary<IntPtr, (TMP_FontAsset Font, int N)>();
            foreach (var t in Object.FindObjectsOfType<TMP_Text>())
            {
                var f = t?.font;
                if (f == null) continue;
                counts[f.Pointer] = counts.TryGetValue(f.Pointer, out var c) ? (c.Font, c.N + 1) : (f, 1);
            }
            return counts.Count == 0 ? null : counts.Values.OrderByDescending(c => c.N).First().Font;
        }
        catch { return null; }
    }
    private static string SafeName(TMP_FontAsset? f) { try { return f == null ? "-" : f.name; } catch { return "?"; } }
    internal static bool FontAlive() { try { return font != null; } catch { return false; } }
    internal static void ForgetFont() { font = null; nextFontTry = 0; }

    internal static Sprite Box(float radius, ShapeRaster.Corners corners, float ring)
    {
        string key = $"box:{radius}:{(int)corners}:{ring}";
        if (Sprites.TryGetValue(key, out var s) && s != null) return s;
        var (size, border) = ShapeRaster.BoxLayout(radius, ring);
        s = Make(key, ShapeRaster.Box(radius, corners, ring), size, border);
        return s;
    }
    internal static Sprite Circle(bool ring = false)
    {
        string key = ring ? "circle:ring" : "circle";
        if (Sprites.TryGetValue(key, out var s) && s != null) return s;
        return Make(key, ShapeRaster.Circle(64, ring ? 6 : 0), 64, 0);
    }
    // 0.1.8: radial progress ring, cached by diameter and thickness in device pixels; the mask is the
    // same anti-aliased Circle the other kit rings use.
    private static readonly Dictionary<RingKey, Sprite> Rings = new();
    internal static Sprite Ring(RingKey key)
    {
        if (Rings.TryGetValue(key, out var s) && s != null) return s;
        s = Make(key.ToString(), ShapeRaster.Circle(key.DiameterPx, key.ThicknessPx), key.DiameterPx, 0);
        Rings[key] = s;
        return s;
    }
    internal static Sprite Glow()
    {
        if (Sprites.TryGetValue("glow", out var s) && s != null) return s;
        return Make("glow", ShapeRaster.Glow(64, 10f / 26f), 64, 0);
    }
    internal static Sprite Shadow(out int border)
    {
        border = (int)MathF.Ceiling(Tok.RadiusPanel + ShadowBlur) + 1;
        if (Sprites.TryGetValue("shadow", out var s) && s != null) return s;
        var mask = ShapeRaster.Shadow(Tok.RadiusPanel, ShadowBlur, out int size, out border);
        return Make("shadow", mask, size, border);
    }
    // 0.1.4 (#18): icon sprites are looked up per frame (dropdown chevron); index by enum value, no key string
    private static Sprite?[] iconSprites = new Sprite?[32];
    internal static Sprite IconSprite(Icon icon)
    {
        int i = (int)icon;
        if (i >= 0 && i < iconSprites.Length && iconSprites[i] is { } cached && cached != null) return cached;
        string key = "icon:" + icon;
        if (!Sprites.TryGetValue(key, out var s) || s == null) s = Make(key, ShapeRaster.IconMask(icon, 64), 64, 0);
        if (i >= 0) { if (i >= iconSprites.Length) Array.Resize(ref iconSprites, i + 8); iconSprites[i] = s; }
        return s;
    }
    private static Sprite Make(string key, float[] alpha, int size, int border)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "UiKit " + key };
        tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[alpha.Length];
        for (int i = 0; i < alpha.Length; i++) pixels[i] = new Color(1, 1, 1, alpha[i]);
        tex.SetPixels(pixels);
        tex.Apply(false, false);
        var sprite = border > 0
            ? Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border))
            : Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
        sprite.hideFlags = HideFlags.HideAndDontSave; sprite.name = "UiKit " + key;
        Owned.Add(tex); Owned.Add(sprite);
        Sprites[key] = sprite;
        return sprite;
    }
    internal static void DestroyAll()
    {
        foreach (var o in Owned) try { if (o != null) Object.Destroy(o); } catch { }
        Owned.Clear(); Sprites.Clear(); Rings.Clear(); Array.Clear(iconSprites);
    }
}

/// <summary>uGUI building blocks. All rects: anchor/pivot top-left, position (x, -y) in kit units.</summary>
internal static class Draw
{
    internal static Color C(Rgba c) => new Color32(c.R, c.G, c.B, c.A);
    internal static Rgba R(Color c) { Color32 k = c; return new Rgba(k.r, k.g, k.b, k.a); }
    private static readonly Dictionary<string, Rgba?> HexCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>"#rrggbb[aa]" to colour (cached); null or invalid gives null (invalid is logged once).</summary>
    internal static Rgba? TryHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        if (HexCache.TryGetValue(hex, out var c)) return c;
        try { c = Rgba.Hex(hex); } catch (Exception ex) { Guard.Report("Colour '" + hex + "'", ex); c = null; }
        if (HexCache.Count < 4096) HexCache[hex] = c;
        return c;
    }

    internal static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name) { layer = 5 };
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.localScale = Vector3.one;
        return rt;
    }
    internal static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(MathF.Max(0, w), MathF.Max(0, h));
    }
    internal static void Place(RectTransform rt, RectF r, Vec origin) => Place(rt, r.X - origin.X, r.Y - origin.Y, r.W, r.H);
    internal static void Stretch(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
    }
    internal static Image Image(RectTransform rt, Sprite? sprite, Rgba color, bool sliced, bool raycast = false)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        if (sliced) { img.type = UnityEngine.UI.Image.Type.Sliced; img.pixelsPerUnitMultiplier = ShapeRaster.Supersample; img.fillCenter = true; }
        img.color = C(color); img.raycastTarget = raycast;
        return img;
    }
    /// <summary>Plain image child (lines, lamp, icon) at a rect relative to <paramref name="parent"/>.</summary>
    internal static Image Child(RectTransform parent, string name, Sprite? sprite, Rgba color, bool sliced = false)
    {
        var rt = Node(name, parent);
        return Image(rt, sprite, color, sliced);
    }

    internal static TextMeshProUGUI Text(RectTransform rt, TextStyle style, string text = "")
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var f = Assets.Font;
        if (f != null) t.font = f;
        t.raycastTarget = false; t.richText = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.margin = Vector4.zero;
        ApplyStyle(t, style);
        t.text = text;
        return t;
    }
    internal static void ApplyStyle(TextMeshProUGUI t, TextStyle style)
    {
        var (size, color, bold, upper, spacing) = Style(style);
        t.fontSize = size; t.color = C(color); t.characterSpacing = spacing;
        var fs = FontStyles.Normal;
        if (bold) fs |= FontStyles.Bold;
        if (upper) fs |= FontStyles.UpperCase;
        t.fontStyle = fs;
    }
    /// <summary>Type scale from Components.dc.html (TYPOGRAFIE 1080p); letter-spacing in TMP em/100.</summary>
    internal static (float Size, Rgba Color, bool Bold, bool Upper, float Spacing) Style(TextStyle s) => s switch
    {
        TextStyle.Title => (Tok.FontTitle, Tok.Text, true, true, 6),
        TextStyle.Section => (Tok.FontSection, Tok.Heading, true, true, 10),
        TextStyle.Heading => (Tok.FontSection, Tok.Text, true, true, 6),
        TextStyle.Label => (Tok.FontLabel, Tok.Text, true, false, 0),
        TextStyle.Value => (Tok.FontValue, Tok.Value, false, false, 0),
        TextStyle.Small => (Tok.FontSmall, Tok.Text2, false, false, 0),
        TextStyle.Faint => (Tok.FontSmall, Tok.Faint, false, false, 0),
        TextStyle.BlockTitle => (Tok.FontSmall, Tok.Text2, true, true, 10),
        TextStyle.Keyword => (Tok.FontLabel, Tok.Faint, true, true, 8),
        TextStyle.Body => (Tok.FontLabel, Tok.Text, false, false, 0),
        _ => (Tok.FontLabel, Tok.Text, false, false, 0)
    };
    internal static float Width(TextMeshProUGUI t, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        try { return MathF.Ceiling(t.GetPreferredValues(text).x); } catch { return text.Length * t.fontSize * .55f; }
    }
    internal static float WrappedHeight(TextMeshProUGUI t, string text, float width)
    {
        if (string.IsNullOrEmpty(text)) return t.fontSize * 1.3f;
        try { return MathF.Ceiling(t.GetPreferredValues(text, width, 0).y); } catch { return t.fontSize * 1.4f * (1 + text.Length * t.fontSize * .55f / MathF.Max(1, width)); }
    }
    internal static void SetText(TextMeshProUGUI? t, string? text)
    {
        if (t == null) return;
        text ??= "";
        if (t.text != text) t.text = text;
    }
    internal static void SetColor(Graphic? g, Rgba c)
    {
        if (g == null) return;
        var col = C(c);
        if (g.color != col) g.color = col;
    }
    internal static void SetActive(Component? c, bool on)
    {
        if (c == null) return;
        var go = c.gameObject;
        if (go.activeSelf != on) go.SetActive(on);
    }
}

/// <summary>Rounded box with an optional 1-unit border: fill image + outline image.</summary>
internal sealed class BoxView
{
    internal readonly RectTransform Rt;
    internal readonly Image Fill;
    internal readonly Image? Ring;
    private Rgba fill, border;
    internal BoxView(RectTransform parent, string name, float radius, ShapeRaster.Corners corners = ShapeRaster.Corners.All, bool ring = true, bool raycast = false)
    {
        Rt = Draw.Node(name, parent);
        Fill = Draw.Image(Rt, Assets.Box(radius, corners, 0), Rgba.Clear, true, raycast);
        if (ring)
        {
            var r = Draw.Node("Border", Rt); Draw.Stretch(r);
            Ring = Draw.Image(r, Assets.Box(radius, corners, 1), Rgba.Clear, true);
        }
    }
    internal void Set(Rgba fillColour, Rgba borderColour)
    {
        if (!fill.Equals(fillColour)) { fill = fillColour; Fill.color = Draw.C(fillColour); }
        if (Ring != null && !border.Equals(borderColour)) { border = borderColour; Ring.color = Draw.C(borderColour); }
    }
    internal void Place(RectF r, Vec origin) => Draw.Place(Rt, r, origin);
    internal void Place(float x, float y, float w, float h) => Draw.Place(Rt, x, y, w, h);
}
