using Sprocket;
using UnityEngine;
using Object=UnityEngine.Object;

namespace SprocketExpandedBallistics;

/// <summary>
/// Runtime-only composition of the vanilla gunner-sight icon with a WW1/WW2 label.
/// The game-owned sprite/texture is read, never modified or distributed; only our
/// composed Texture2D/Sprite pair is owned and destroyed here.
/// </summary>
internal static class SightIcons
{
    private const int Size = 128;
    private const int LabelHeight = 26;
    private static readonly Dictionary<string, Entry> Cache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Warnings = new(StringComparer.Ordinal);

    private sealed record Entry(Sprite Sprite, Texture2D Texture);

    internal static Sprite? For(string? guid)
    {
        if (!SightCatalog.TryResolve(guid, out var sight)) return null;
        if (Cache.TryGetValue(sight.Guid, out var cached) && cached.Sprite != null) return cached.Sprite;
        try
        {
            var texture = Compose(sight);
            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            var entry = new Entry(sprite, texture);
            Cache[sight.Guid] = entry;
            return sprite;
        }
        catch (Exception ex)
        {
            if (Warnings.Add(sight.Guid))
                Plugin.ModLog.LogWarning($"[Sight icons] {sight.DisplayName} icon unavailable; native fallback retained: {ex.Message}");
            return null;
        }
    }

    private static Texture2D Compose(SightSpec sight)
    {
        var native = NativeSightIcon();
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = sight.Key + " composed sight icon"
        };
        if (native == null || native.texture == null)
        {
            // Fail visibly rather than silently reverting to an unrelated stock icon.
            Fallback(texture, sight);
            if (Warnings.Add(sight.Guid + ":native"))
                Plugin.ModLog.LogWarning($"[Sight icons] Native vanilla gunner sight icon not found; {sight.DisplayName} uses a labelled fallback.");
            return texture;
        }

        var source = native.texture;
        var rect = native.textureRect;
        if (rect.width <= 0 || rect.height <= 0) rect = native.rect;
        if (source.width <= 0 || source.height <= 0 || rect.width <= 0 || rect.height <= 0)
        {
            Fallback(texture, sight);
            return texture;
        }
        const int iconWidth = 92;
        var iconHeight = Mathf.Clamp(Mathf.RoundToInt(iconWidth * rect.height / rect.width), 1, iconWidth);
        var previous = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(iconWidth, iconHeight, 0, RenderTextureFormat.ARGB32);
        Texture2D? sampled = null;
        try
        {
            // Graphics.Blit is the native-supported path used by other local part-icon
            // adapters; Graphics.DrawTexture is a stripped stub in this player.
            Graphics.Blit(source, rt,
                new Vector2(rect.width / source.width, rect.height / source.height),
                new Vector2(rect.x / source.width, rect.y / source.height));
            RenderTexture.active = rt;
            sampled = new Texture2D(iconWidth, iconHeight, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            sampled.ReadPixels(new Rect(0, 0, iconWidth, iconHeight), 0, 0, false);
            sampled.Apply(false, false);
            var from = sampled.GetPixels();
            var to = new Color[Size * Size];
            var left = (Size - iconWidth) / 2;
            var bottom = LabelHeight + 5 + (iconWidth - iconHeight) / 2;
            for (var y = 0; y < iconHeight; y++)
                for (var x = 0; x < iconWidth; x++)
                    to[(bottom + y) * Size + left + x] = from[y * iconWidth + x];
            texture.SetPixels(to);
            texture.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            if (sampled != null) Object.Destroy(sampled);
        }
        DrawLabel(texture, sight);
        return texture;
    }

    private static void Fallback(Texture2D texture, SightSpec sight)
    {
        var pixels = new Color[Size * Size];
        Fill(pixels, new Color(38 / 255f, 42 / 255f, 38 / 255f, 1f));
        Border(pixels, new Color(150 / 255f, 148 / 255f, 130 / 255f, 1f));
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        DrawLabel(texture, sight);
    }

    private static Sprite? NativeSightIcon()
    {
        foreach (var key in new[]
                 {
                     SightMenuPolicy.VanillaGunnerSightGuid, "gunnerSight", "gunnerSightPart", "Gunner Sight"
                 })
        {
            try
            {
                if (GameIcons.TryGet(key, out var sprite) && sprite != null) return sprite;
            }
            catch
            {
                // Try the next native identifier; missing icon is handled by the caller.
            }
        }
        return null;
    }

    private static void DrawLabel(Texture2D texture, SightSpec sight)
    {
        var pixels = texture.GetPixels();
        var band = new Color(0, 0, 0, 185 / 255f);
        for (var y = 0; y < LabelHeight && y < Size; y++)
            for (var x = 0; x < Size; x++)
                Blend(pixels, y * Size + x, band);

        var text = sight.Key == "ww1" ? "WW1" : "WW2";
        const int scale = 3, glyphWidth = 5, glyphHeight = 7, gap = 2;
        var width = text.Length * glyphWidth * scale + (text.Length - 1) * gap;
        var left = Math.Max(2, (Size - width) / 2);
        var top = 3;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = Glyph(text[i]);
            for (var row = 0; row < glyphHeight; row++)
                for (var col = 0; col < glyphWidth; col++)
                {
                    if ((glyph[row] & (1 << (glyphWidth - 1 - col))) == 0) continue;
                    var x0 = left + i * (glyphWidth * scale + gap) + col * scale;
                    var y0 = top + row * scale;
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                            Blend(pixels, (y0 + dy) * Size + x0 + dx, new Color(0, 0, 0, 220 / 255f));
                    for (var dy = 0; dy < scale; dy++)
                        for (var dx = 0; dx < scale; dx++)
                            Set(pixels, (y0 + dy) * Size + x0 + dx, new Color(245 / 255f, 238 / 255f, 210 / 255f, 1f));
                }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, false);
    }

    private static int[] Glyph(char c) => c switch
    {
        'W' => new[] { 0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b11011, 0b10001 },
        '1' => new[] { 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 },
        '2' => new[] { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111 },
        _ => new[] { 0, 0, 0, 0, 0, 0, 0 }
    };

    private static void Fill(Color[] pixels, Color color)
    {
        for (var i = 0; i < pixels.Length; i++) pixels[i] = color;
    }

    private static void Border(Color[] pixels, Color color)
    {
        for (var i = 0; i < Size; i++)
        {
            Set(pixels, i, color);
            Set(pixels, (Size - 1) * Size + i, color);
            Set(pixels, i * Size, color);
            Set(pixels, i * Size + Size - 1, color);
        }
    }

    private static void Set(Color[] pixels, int index, Color color)
    {
        if ((uint)index < (uint)pixels.Length) pixels[index] = color;
    }

    private static void Blend(Color[] pixels, int index, Color color)
    {
        if ((uint)index >= (uint)pixels.Length) return;
        var under = pixels[index];
        var alpha = color.a;
        var outAlpha = alpha + under.a * (1 - alpha);
        pixels[index] = outAlpha <= 0.0001f
            ? Color.clear
            : new Color(
                (color.r * alpha + under.r * under.a * (1 - alpha)) / outAlpha,
                (color.g * alpha + under.g * under.a * (1 - alpha)) / outAlpha,
                (color.b * alpha + under.b * under.a * (1 - alpha)) / outAlpha,
                outAlpha);
    }

    internal static void Clear()
    {
        foreach (var entry in Cache.Values)
        {
            if (entry.Sprite != null) Object.Destroy(entry.Sprite);
            if (entry.Texture != null) Object.Destroy(entry.Texture);
        }
        Cache.Clear();
        Warnings.Clear();
    }
}
