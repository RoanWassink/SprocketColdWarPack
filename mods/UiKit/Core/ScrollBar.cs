namespace SprocketUiKit;

/// <summary>Where a vertical scrollbar press landed (0.1.5).</summary>
internal enum ScrollPart { None, Above, Thumb, Below }

/// <summary>Thumb geometry inside a vertical track: offset from the track top and length.</summary>
internal readonly record struct ScrollThumb(float Offset, float Length);

/// <summary>
/// Pure vertical scrollbar logic (0.1.5), shared by ScrollView, ListView and the dropdown popup.
/// The kit never scrolls horizontally. Coordinates are reference px, y down, local to the track.
/// </summary>
internal static class ScrollBarMath
{
    /// <summary>Content overflows the viewport (same .5 px tolerance as the wheel and layout code).</summary>
    internal static bool Overflow(float contentH, float viewH) => contentH > viewH + .5f;

    internal static float MaxScroll(float contentH, float viewH) => MathF.Max(0, contentH - MathF.Max(0, viewH));

    /// <summary>
    /// Thumb from scroll, viewport height and content height. The thumb is proportional, at least
    /// <paramref name="minLength"/> high, never taller than the track, and its bottom reaches the track
    /// bottom at the maximum scroll.
    /// </summary>
    internal static ScrollThumb Thumb(float scroll, float contentH, float viewH, float minLength = Tok.ScrollMinH)
    {
        float view = MathF.Max(0, viewH);
        if (!Overflow(contentH, view)) return new ScrollThumb(0, view);
        float max = MaxScroll(contentH, view);
        if (max <= 0) return new ScrollThumb(0, view);
        float length = MathF.Min(view, MathF.Max(MathF.Max(0, minLength), view * view / MathF.Max(1, contentH)));
        float travel = MathF.Max(0, view - length);
        float offset = travel * Math.Clamp(scroll, 0, max) / max;
        return new ScrollThumb(offset, length);
    }

    /// <summary>
    /// Scroll while the thumb is dragged by <paramref name="pointerDeltaY"/> from the press point.
    /// Proportional to the pointer and clamped to 0 and the maximum; the grab point stays under the
    /// cursor because the thumb moves by the same delta (unless a clamp stops it).
    /// </summary>
    internal static float DragTo(float startScroll, float pointerDeltaY, float trackTravel, float contentH, float viewH)
    {
        float max = MaxScroll(contentH, viewH);
        if (max <= 0) return 0;
        if (trackTravel <= .001f) return Math.Clamp(startScroll, 0, max);
        return Math.Clamp(startScroll + pointerDeltaY * max / trackTravel, 0, max);
    }

    /// <summary>
    /// Scroll for a click/hold on the track (<paramref name="direction"/> -1 above the thumb, +1 below):
    /// one viewport height, stopped where the thumb reaches the pointer so it never jumps past it.
    /// </summary>
    internal static float PageTarget(float scroll, float contentH, float viewH, float pointerY, float trackTop, int direction)
    {
        float max = MaxScroll(contentH, viewH);
        if (max <= 0 || direction == 0) return Math.Clamp(scroll, 0, max);
        var thumb = Thumb(scroll, contentH, viewH);
        float travel = MathF.Max(1, MathF.Max(0, viewH) - thumb.Length);
        float target = scroll + Math.Sign(direction) * MathF.Max(0, viewH);
        if (direction > 0)
        {
            // moving down: stop when the thumb bottom reaches the pointer
            float stop = (pointerY - trackTop - thumb.Length) * max / travel;
            target = MathF.Min(target, stop);
        }
        else
        {
            // moving up: stop when the thumb top reaches the pointer
            float stop = (pointerY - trackTop) * max / travel;
            target = MathF.Max(target, stop);
        }
        return Math.Clamp(target, 0, max);
    }

    /// <summary>Track part under a pointer (track-local y). No overflow means no bar at all.</summary>
    internal static ScrollPart PartAt(float pointerY, float trackTop, float contentH, float viewH, float scroll)
    {
        float view = MathF.Max(0, viewH);
        if (!Overflow(contentH, view)) return ScrollPart.None;
        float y = pointerY - trackTop;
        if (y < 0 || y >= view) return ScrollPart.None;
        var thumb = Thumb(scroll, contentH, view);
        if (y >= thumb.Offset && y <= thumb.Offset + thumb.Length) return ScrollPart.Thumb;
        return y < thumb.Offset ? ScrollPart.Above : ScrollPart.Below;
    }

    /// <summary>
    /// Click strip: at most 10 px from the right edge, and only while the content overflows. The kit
    /// draws the thumb 3 px wide (6 on hover) and reserves 6 px of content, so the strip overlaps a few
    /// px of content; the bar wins there.
    /// </summary>
    internal static bool InStrip(float x, float right, bool overflow) =>
        overflow && x >= right - Tok.ScrollHitW && x < right;
}
