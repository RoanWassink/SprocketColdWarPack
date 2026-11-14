namespace SprocketUiKit;

/// <summary>
/// Key of the procedural ring sprite (0.1.8): diameter and thickness in device pixels, so a sprite
/// stays sharp at the Kit scale and is shared by the track and fill images. Pure, no allocation.
/// </summary>
internal readonly struct RingKey : IEquatable<RingKey>
{
    internal readonly int DiameterPx, ThicknessPx;
    internal RingKey(int diameterPx, int thicknessPx) { DiameterPx = diameterPx; ThicknessPx = thicknessPx; }
    public bool Equals(RingKey o) => DiameterPx == o.DiameterPx && ThicknessPx == o.ThicknessPx;
    public override bool Equals(object? obj) => obj is RingKey o && Equals(o);
    public override int GetHashCode() => (DiameterPx * 397) ^ ThicknessPx;
    public override string ToString() => $"ring:{DiameterPx}:{ThicknessPx}";
}

/// <summary>
/// Pure radial-progress rules (0.1.8): clamping, the perceptible fill step, the default ring
/// thickness, the device-pixel sprite key and the centre type size. Shared by the element and the tests.
/// </summary>
internal static class RadialMath
{
    /// <summary>Default ring thickness as a fraction of the diameter.</summary>
    internal const float ThicknessFraction = .12f;
    /// <summary>Never thinner than this many reference px.</summary>
    internal const float MinThickness = 3f;
    /// <summary>Smallest fillAmount change worth uploading (1/720, half a degree).</summary>
    internal const float FillStep = 1f / 720f;
    /// <summary>Centre text as a fraction of the diameter, never above TextStyle.Value.</summary>
    internal const float CentreFraction = .28f;

    /// <summary>0..1; NaN and infinite values count as 0.</summary>
    internal static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

    /// <summary>About 12% of the diameter (rounded), at least 3 px.</summary>
    internal static float DefaultThickness(float diameter)
    {
        if (!float.IsFinite(diameter) || diameter <= 0) return MinThickness;
        return MathF.Max(MinThickness, MathF.Round(diameter * ThicknessFraction));
    }

    /// <summary>Centre text size: 28% of the diameter, clamped to 8..13 px (TextStyle.Value).</summary>
    internal static float CentreFontSize(float diameter)
    {
        if (!float.IsFinite(diameter) || diameter <= 0) return 8;
        return Math.Clamp(MathF.Round(diameter * CentreFraction), 8, Tok.FontValue);
    }

    /// <summary>True when moving from <paramref name="shown"/> to <paramref name="value"/> is worth a new fillAmount.</summary>
    internal static bool Perceptible(float shown, float value) => MathF.Abs(value - shown) >= FillStep;

    private static int DevicePixels(float kitSize, float scale)
    {
        double px = (double)kitSize * (float.IsFinite(scale) && scale > 0 ? scale : 1);
        if (double.IsNaN(px) || px < 1) return 1;
        return px >= 4096 ? 4096 : (int)Math.Ceiling(px);
    }

    /// <summary>Sprite cache key for a diameter and thickness in Kit units, in device pixels.</summary>
    internal static RingKey SpriteKey(float diameter, float thickness, float scale)
    {
        int d = DevicePixels(diameter, scale);
        int t = Math.Clamp((int)MathF.Round(thickness * (float.IsFinite(scale) && scale > 0 ? scale : 1)), 1, Math.Max(1, d / 2));
        return new RingKey(d, t);
    }
}

/// <summary>Change reported by <see cref="RadialPoll.Poll"/>.</summary>
[Flags] internal enum RadialChange { None = 0, Value = 1, Text = 2 }

/// <summary>
/// Per-frame poll state of a radial progress (0.1.8): clamps the value, applies the 1/720 fill
/// threshold and compares the centre text without allocating. Pure, so the tests cover it.
/// </summary>
internal sealed class RadialPoll
{
    internal float Value;
    internal string? Text;
    private bool first = true;

    /// <summary>Poll a raw value and centre text; returns which of them changed.</summary>
    internal RadialChange Poll(float raw, string? text)
    {
        float v = RadialMath.Clamp(raw);
        var changed = RadialChange.None;
        if (first || RadialMath.Perceptible(Value, v)) { Value = v; changed |= RadialChange.Value; }
        if (!string.Equals(Text, text, StringComparison.Ordinal)) { Text = text; changed |= RadialChange.Text; }
        first = false;
        return changed;
    }
}
