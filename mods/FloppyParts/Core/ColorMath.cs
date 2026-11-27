using System.Globalization;

namespace SprocketFloppyParts;

internal static class ColorMath
{
    internal static bool TryParseHex(string? hex, out float r, out float g, out float b)
    {
        r = g = b = 0f;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var span = hex.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];

        if (span.Length == 3)
        {
            if (!TryHexNibble(span[0], out int r1) ||
                !TryHexNibble(span[1], out int g1) ||
                !TryHexNibble(span[2], out int b1))
                return false;
            r = (r1 * 17) / 255f;
            g = (g1 * 17) / 255f;
            b = (b1 * 17) / 255f;
            return true;
        }

        if (span.Length == 6)
        {
            if (!byte.TryParse(span[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte rb) ||
                !byte.TryParse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte gb) ||
                !byte.TryParse(span[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte bb))
                return false;
            r = rb / 255f;
            g = gb / 255f;
            b = bb / 255f;
            return true;
        }

        return false;
    }

    internal static string FormatHex(float r, float g, float b)
    {
        byte rb = (byte)Math.Clamp((int)MathF.Round(Clamp01(r) * 255f), 0, 255);
        byte gb = (byte)Math.Clamp((int)MathF.Round(Clamp01(g) * 255f), 0, 255);
        byte bb = (byte)Math.Clamp((int)MathF.Round(Clamp01(b) * 255f), 0, 255);
        return FormattableString.Invariant($"#{rb:X2}{gb:X2}{bb:X2}");
    }

    internal static (float R, float G, float B) PresetRgb(ColorPresetId id) => id switch
    {
        ColorPresetId.Natural => (0.86f, 0.82f, 0.72f),
        ColorPresetId.OliveDrab => (0.36f, 0.40f, 0.25f),
        ColorPresetId.RussianGreen => (0.26f, 0.35f, 0.22f),
        ColorPresetId.Khaki => (0.58f, 0.53f, 0.36f),
        ColorPresetId.DesertSand => (0.76f, 0.67f, 0.49f),
        ColorPresetId.WinterWhite => (0.88f, 0.89f, 0.90f),
        ColorPresetId.FieldGrey => (0.36f, 0.39f, 0.35f),
        ColorPresetId.Charcoal => (0.18f, 0.19f, 0.20f),
        ColorPresetId.RubberBlack => (0.12f, 0.12f, 0.13f),
        ColorPresetId.ClearGlass => (0.90f, 0.96f, 1.00f),
        ColorPresetId.TintedGlass => (0.25f, 0.85f, 0.45f),
        ColorPresetId.BrownTintedGlass => (0.65f, 0.36f, 0.14f),
        ColorPresetId.YellowTintedGlass => (1.00f, 0.85f, 0.15f),
        ColorPresetId.SmokeDarkGlass => (0.06f, 0.07f, 0.08f),
        ColorPresetId.BlueAntiGlareGlass => (0.25f, 0.65f, 1.00f),
        ColorPresetId.ThermalRainbowGlass => (0.95f, 0.97f, 1.00f),
        _ => (0.86f, 0.82f, 0.72f)
    };

    internal static float DefaultGlassOpacity(ColorPresetId id) => id switch
    {
        ColorPresetId.ClearGlass => 0.08f,
        ColorPresetId.TintedGlass => 0.22f,
        ColorPresetId.BrownTintedGlass => 0.24f,
        ColorPresetId.YellowTintedGlass => 0.22f,
        ColorPresetId.SmokeDarkGlass => 0.55f,
        ColorPresetId.BlueAntiGlareGlass => 0.20f,
        ColorPresetId.ThermalRainbowGlass => 0.30f,
        _ => 0.15f
    };

    internal static float DefaultGlassSmoothness(ColorPresetId id) => id switch
    {
        ColorPresetId.ThermalRainbowGlass => 0.92f,
        _ => 0.86f
    };

    /// <summary>
    /// Converts Sprocket's PaintConfig channels into a matte dyed-fabric tint safely clamped to [0.04..1.0].
    /// </summary>
    internal static (float R, float G, float B) DyeFromTankPaint(
        float paintR,
        float paintG,
        float paintB,
        float saturation,
        float brightness)
    {
        float r = float.IsFinite(paintR) ? Math.Clamp(paintR, 0f, 2f) : 0.36f;
        float g = float.IsFinite(paintG) ? Math.Clamp(paintG, 0f, 2f) : 0.40f;
        float b = float.IsFinite(paintB) ? Math.Clamp(paintB, 0f, 2f) : 0.25f;
        float sat = float.IsFinite(saturation) ? Math.Clamp(saturation, 0f, 1.5f) : 1.0f;
        float bright = float.IsFinite(brightness) ? Math.Clamp(brightness, 0.25f, 2.0f) : 1.0f;

        // Matte dyed fabric absorbs slightly more specular energy and mutes extreme paint saturation.
        float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        float fabricSat = sat * 0.85f;
        float dr = (luma + (r - luma) * fabricSat) * bright;
        float dg = (luma + (g - luma) * fabricSat) * bright;
        float db = (luma + (b - luma) * fabricSat) * bright;

        return (
            Math.Clamp(dr, 0.04f, 1.0f),
            Math.Clamp(dg, 0.04f, 1.0f),
            Math.Clamp(db, 0.04f, 1.0f));
    }

    internal static (float R, float G, float B, float A) ResolveTint(
        PlateLookConfig cfg,
        (float R, float G, float B, float Saturation)? tankPaint)
    {
        float alpha = cfg.Preset == LookPreset.Glass
            ? Math.Clamp(float.IsFinite(cfg.Opacity) ? cfg.Opacity : 0.08f, 0.01f, 1.0f)
            : 1.0f;
        float bright = float.IsFinite(cfg.Brightness) ? Math.Clamp(cfg.Brightness, 0.25f, 2.0f) : 1.0f;

        if (cfg.ColorMode == ColorMode.FollowTankPaint)
        {
            if (tankPaint is { } p)
            {
                var dyed = DyeFromTankPaint(p.R, p.G, p.B, p.Saturation, bright);
                return (dyed.R, dyed.G, dyed.B, alpha);
            }
            var fallback = PresetRgb(ColorPresetId.OliveDrab);
            return (
                Math.Clamp(fallback.R * bright, 0.04f, 1.0f),
                Math.Clamp(fallback.G * bright, 0.04f, 1.0f),
                Math.Clamp(fallback.B * bright, 0.04f, 1.0f),
                alpha);
        }

        if (cfg.ColorMode == ColorMode.CustomHex)
        {
            if (!TryParseHex(cfg.CustomHex, out float r, out float g, out float b))
                (r, g, b) = PresetRgb(ColorPresetId.OliveDrab);
            return (
                Math.Clamp(r * bright, 0.04f, 1.0f),
                Math.Clamp(g * bright, 0.04f, 1.0f),
                Math.Clamp(b * bright, 0.04f, 1.0f),
                alpha);
        }

        var preset = PresetRgb(cfg.ColorPreset);
        return (
            Math.Clamp(preset.R * bright, 0.04f, 1.0f),
            Math.Clamp(preset.G * bright, 0.04f, 1.0f),
            Math.Clamp(preset.B * bright, 0.04f, 1.0f),
            alpha);
    }

    private static float Clamp01(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 0f;

    private static bool TryHexNibble(char c, out int value)
    {
        if (c >= '0' && c <= '9') { value = c - '0'; return true; }
        if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
        if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
        value = 0;
        return false;
    }
}
