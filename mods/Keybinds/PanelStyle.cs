// Stijl volgt UI Kit STYLE.md 0.1.0; bewust geen Kit-dependency (kringafhankelijkheid)
using Sprocket.SettingConfiguration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SprocketKeybinds;

// Presentation only. No callbacks, input polling or runtime dependency on the style provider.
internal static class PanelStyle
{
    internal static readonly Color Panel = Hex("#14150f", .96f);
    internal static readonly Color Block = Hex("#1b1c15");
    internal static readonly Color Row = Hex("#24231c");
    internal static readonly Color Button = Hex("#2b2924");
    internal static readonly Color Hover = Hex("#3a372e");
    internal static readonly Color Selected = Hex("#3a3220");
    internal static readonly Color Accent = Hex("#dc9e33");
    internal static readonly Color Text = Hex("#fff0cc");
    internal static readonly Color Text2 = Hex("#b8ad94");
    internal static readonly Color Value = Hex("#c9bc9c");
    internal static readonly Color Faint = Hex("#8f8670");
    internal static readonly Color Heading = Hex("#e9c27a");
    internal static readonly Color Frame = Hex("#4a4536");
    internal static readonly Color Divider = Hex("#3a372e");
    internal static readonly Color Border = Hex("#5c5442");
    internal static readonly Color Disabled = Hex("#1f1e18");
    internal static readonly Color Danger = Hex("#e07a4f");
    internal static readonly Color DangerText = Hex("#ffcbb3");
    internal static readonly Color Warning = Hex("#2a1c15");
    internal static readonly Color WarningBorder = Hex("#8a4a30");
    internal static readonly Color DangerHover = Hex("#3a1f17"); // Explicit Clear hover from the panel brief.
    private static Color Hex(string value, float alpha = 1) => new Color(
        Convert.ToByte(value.Substring(1, 2), 16) / 255f, Convert.ToByte(value.Substring(3, 2), 16) / 255f,
        Convert.ToByte(value.Substring(5, 2), 16) / 255f, alpha);

    internal static float Scale { get; private set; } = 1;
    internal static TMP_FontAsset? Font { get; private set; }
    private static bool warned;
    private static readonly Dictionary<string, Sprite> sprites = new(StringComparer.Ordinal);
    private static readonly List<Object> owned = new();
    private sealed record TextSpec(TMP_Text Label, float Size, float Spacing);
    private sealed record ButtonSpec(UnityEngine.UI.Button Control, Image? Ring, TMP_Text Label, bool Quiet);
    private static readonly List<TextSpec> texts = new();
    private static readonly List<Image> boxes = new();
    private static readonly List<ButtonSpec> buttons = new();
    private sealed record RowRect(RectTransform Rect, Vector2 Position, Vector2 Size);
    private sealed record RowLayout(GameObject Root, RowRect[] Rects);
    private static readonly Dictionary<IntPtr, RowLayout> rowRects = new();
    internal static void Guard(Action action)
    {
        try { action(); }
        catch (Exception e)
        {
            if (warned) return;
            warned = true;
            Plugin.Logger.LogWarning("[Keybinds style] Using plain UI fallback where styling failed: " + e.Message);
        }
    }
    internal static void Initialize(TMP_FontAsset? fallback)
    {
        Font = fallback;
        Guard(() => { if (TMP_Settings.defaultFontAsset != null) Font = TMP_Settings.defaultFontAsset; });
    }
    internal static string Glyph(char desired, string fallback)
    {
        string result = fallback;
        Guard(() => { if (Font != null && Font.HasCharacter(desired)) result = desired.ToString(); });
        return result;
    }
    internal static float CanvasScale(Transform parent)
    {
        float canvasScale = 1;
        Guard(() =>
        {
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas != null) canvasScale = canvas.rootCanvas.scaleFactor;
        });
        return Math.Max(.0001f, canvasScale);
    }
    internal static float ReferenceScale(int screenHeight, float canvasScale) => screenHeight / 1080f / canvasScale;
    internal static void SetScale(float value)
    {
        Scale = value;
        texts.RemoveAll(item => item.Label == null);
        boxes.RemoveAll(item => item == null);
        buttons.RemoveAll(item => item.Control == null);
        foreach (var pointer in rowRects.Where(item => item.Value.Root == null).Select(item => item.Key).ToArray()) rowRects.Remove(pointer);
        foreach (var item in texts) Guard(() =>
        {
            if (item.Label == null) return;
            item.Label.fontSize = item.Size * Scale;
            // TMP characterSpacing is relative to the font size (em/100), not canvas pixels.
            item.Label.characterSpacing = item.Spacing;
        });
        foreach (var item in boxes) Guard(() => SliceScale(item));
    }
    internal static void Label(TMP_Text text, float size, Color color, bool caps = false, float spacing = 0)
    {
        // Register first: font/sprite failures cannot prevent later resolution updates.
        texts.RemoveAll(item => item.Label == null || item.Label.Pointer == text.Pointer);
        texts.Add(new(text, size, spacing));
        Guard(() =>
        {
            text.fontSize = size * Scale; text.color = color;
            text.enableAutoSizing = false; text.richText = false; text.characterSpacing = spacing;
            text.fontStyle = caps ? FontStyles.Bold | FontStyles.UpperCase : FontStyles.Normal;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.margin = Vector4.zero; text.raycastTarget = false;
        });
        Guard(() => { if (Font != null) text.font = Font; });
    }
    internal static void Wrapped(TMP_Text text) => Guard(() =>
    {
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.TopLeft;
    });
    internal static float Height(TMP_Text text, float width)
    {
        float result = text.fontSize * 1.4f * (1 + text.text.Length * text.fontSize * .55f / Math.Max(1, width));
        Guard(() => result = Math.Max(text.fontSize * 1.3f, text.GetPreferredValues(text.text, Math.Max(1, width), 0).y));
        return result;
    }
    internal static Image Box(RectTransform rect, Color color, float radius = 4, bool ring = false, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = raycast;
        boxes.Add(image);
        Shape(image, radius, ring);
        return image;
    }
    private static void Shape(Image image, float radius, bool ring = false)
    {
        try { image.sprite = Rounded(radius, ring); image.type = Image.Type.Sliced; SliceScale(image); }
        catch (Exception e)
        {
            image.sprite = null; image.type = Image.Type.Simple;
            // A failed outline must not cover the content with an opaque rectangle.
            if (ring) image.color = Color.clear;
            Guard(() => throw e);
        }
    }
    private static void SliceScale(Image image)
    {
        // Sprite PPU=100; cancel the canvas reference PPU, then divide by the ref-px factor.
        image.pixelsPerUnitMultiplier = 4 * (image.canvas == null ? 100 : image.canvas.referencePixelsPerUnit) / 100f / Scale;
    }
    internal static Image Outline(RectTransform rect, Color color, float radius = 4)
    {
        var child = Node("Style.Border", rect); Stretch(child);
        return Box(child, color, radius, true);
    }
    internal static void OutlineColor(Image? image, Color color) => Guard(() =>
    {
        if (image != null) image.color = image.sprite == null ? Color.clear : color;
    });
    internal static RectTransform Node(string name, Transform parent)
    {
        var rect = new GameObject(name) { layer = parent.gameObject.layer }.AddComponent<RectTransform>();
        rect.SetParent(parent, false); rect.localScale = Vector3.one;
        return rect;
    }
    internal static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
    }
    internal static void Shadow(RectTransform rect)
    {
        var child = Node("Style.Shadow", rect); Stretch(child); child.SetAsFirstSibling();
        var image = child.gameObject.AddComponent<Image>();
        image.raycastTarget = false; image.color = new Color(0, 0, 0, .55f);
        try { image.sprite = ShadowSprite(); image.type = Image.Type.Sliced; SliceScale(image); }
        catch (Exception e) { image.color = Color.clear; Guard(() => throw e); }
        boxes.Add(image);
    }
    internal static void StyleButton(UnityEngine.UI.Button button, Image? ring, TMP_Text label, bool quiet = false)
    {
        buttons.Add(new(button, ring, label, quiet));
        ConfigureButton(button, false, quiet, false);
    }
    internal static void ConfigureButton(UnityEngine.UI.Button button, bool chosen = false, bool quiet = false, bool section = false) => Guard(() =>
    {
        var colors = button.colors;
        colors.normalColor = section || quiet ? Color.clear : chosen ? Selected : Button;
        colors.highlightedColor = quiet ? DangerHover : section ? Row : Hover;
        colors.pressedColor = quiet ? DangerHover : Selected;
        colors.selectedColor = quiet ? DangerHover : Selected;
        colors.disabledColor = Disabled; colors.colorMultiplier = 1; colors.fadeDuration = .1f;
        button.transition = Selectable.Transition.ColorTint; button.colors = colors;
        // ColorBlock tints the renderer; the underlying image must be white to avoid double tinting.
        if (button.targetGraphic != null) button.targetGraphic.color = Color.white;
    });
    internal static void Tick()
    {
        foreach (var item in buttons) Guard(() =>
        {
            if (item.Control == null || !item.Control.gameObject.activeInHierarchy || !item.Quiet) return;
            bool hot = item.Control.isPointerInside || item.Control.currentSelectionState == Selectable.SelectionState.Selected || item.Control.IsPressed();
            OutlineColor(item.Ring, hot ? Danger : Border);
            item.Label.color = hot ? DangerText : Text2;
        });
    }
    internal static void StyleRow(GameObject clone) => Guard(() =>
    {
        var ui = clone.GetComponentInChildren<RebindActionUI>(true);
        var controls = clone.GetComponentsInChildren<Selectable>(true);
        foreach (var image in clone.GetComponentsInChildren<Image>(true)) Guard(() =>
        {
            var control = controls.FirstOrDefault(s => s.targetGraphic != null && s.targetGraphic.Pointer == image.Pointer);
            // Non-sliced sprites without a Selectable are icons: tint them, keep their shape.
            if (control == null && image.sprite != null && image.type != Image.Type.Sliced && !image.sprite.name.StartsWith("Keybinds.Style."))
            { image.color = Text2; return; }
            image.color = control == null ? Row : Color.white;
            Shape(image, 4);
            if (control != null)
            {
                var colors = control.colors;
                colors.normalColor = Button; colors.highlightedColor = Hover;
                colors.pressedColor = colors.selectedColor = Selected; colors.disabledColor = Disabled;
                colors.colorMultiplier = 1; colors.fadeDuration = .1f;
                control.transition = Selectable.Transition.ColorTint; control.colors = colors;
            }
        });
        foreach (var label in clone.GetComponentsInChildren<TMP_Text>(true)) Guard(() =>
        {
            label.enableAutoSizing = false; label.fontSize = 14 * Scale; label.richText = false;
            label.color = ui != null && ui.BindingText != null && label.Pointer == ui.BindingText.Pointer ? Value : Text;
            Guard(() => { if (Font != null) label.font = Font; });
        });
        if (!rowRects.TryGetValue(clone.Pointer, out var layout) || layout.Root == null)
        {
            var rects = clone.GetComponentsInChildren<RectTransform>(true).Where(r => r.Pointer != clone.transform.Pointer)
                .Select(r => new RowRect(r, r.anchoredPosition, r.sizeDelta)).ToArray();
            rowRects[clone.Pointer] = layout = new RowLayout(clone, rects);
        }
        foreach (var original in layout.Rects) Guard(() =>
        {
            if (original.Rect == null) return;
            original.Rect.anchoredPosition = original.Position * Scale;
            original.Rect.sizeDelta = original.Size * Scale;
        });
    });

    // Small supersampled, cached 9-slices. Runtime-owned textures survive scene asset collection.
    private static Sprite Rounded(float radius, bool ring)
    {
        string key = $"round:{radius}:{ring}";
        if (sprites.TryGetValue(key, out var cached) && cached != null) return cached;
        int border = (int)Math.Ceiling(radius + 1), size = border * 2 + 2;
        return Make(key, size, border, (x, y) =>
        {
            float d = Distance(x, y, size, radius);
            return Math.Clamp(.5f - d, 0, 1) * (ring ? Math.Clamp(d + 1.5f, 0, 1) : 1);
        });
    }
    private static float Distance(float x, float y, float size, float radius)
    {
        float qx = Math.Abs(x - size / 2) - (size / 2 - radius);
        float qy = Math.Abs(y - size / 2) - (size / 2 - radius);
        return MathF.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - radius;
    }
    private static Sprite ShadowSprite()
    {
        if (sprites.TryGetValue("shadow", out var cached) && cached != null) return cached;
        // 48px blur diameter, 24px support around a radius-6 box.
        return Make("shadow", 64, 31, (x, y) =>
        {
            float d = Math.Max(0, Distance(x - 24, y - 24, 16, 6));
            float t = Math.Clamp(1 - d / 24, 0, 1);
            return t * t * (3 - 2 * t);
        });
    }
    private static Sprite Make(string key, int size, int border, Func<float, float, float> alpha)
    {
        const int samples = 4;
        int pixels = size * samples;
        var texture = new Texture2D(pixels, pixels, TextureFormat.RGBA32, false)
        { name = "Keybinds.Style." + key, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        owned.Add(texture);
        var colors = new Color[pixels * pixels];
        for (int y = 0; y < pixels; y++) for (int x = 0; x < pixels; x++)
            colors[y * pixels + x] = new Color(1, 1, 1, alpha((x + .5f) / samples, (y + .5f) / samples));
        texture.SetPixels(colors); texture.Apply(false, false);
        var sprite = Sprite.Create(texture, new Rect(0, 0, pixels, pixels), new Vector2(.5f, .5f), 100, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border) * samples);
        sprite.name = texture.name; sprite.hideFlags = HideFlags.HideAndDontSave;
        owned.Add(sprite); sprites[key] = sprite;
        return sprite;
    }
    internal static void Cleanup()
    {
        texts.Clear(); boxes.Clear(); buttons.Clear(); rowRects.Clear(); Font = null;
        foreach (var item in owned) Guard(() => { if (item != null) Object.Destroy(item); });
        owned.Clear(); sprites.Clear();
    }
}
