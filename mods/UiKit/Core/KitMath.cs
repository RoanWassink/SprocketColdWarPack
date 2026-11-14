using System.Globalization;
namespace SprocketUiKit;

/// <summary>Point in kit units (reference px), origin top-left, y down.</summary>
internal readonly struct Vec
{
    internal readonly float X, Y;
    internal Vec(float x, float y) { X = x; Y = y; }
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public override string ToString() => $"({X:0.#},{Y:0.#})";
}

/// <summary>Rectangle in kit units, origin top-left, y down.</summary>
internal readonly struct RectF
{
    internal readonly float X, Y, W, H;
    internal RectF(float x, float y, float w, float h) { X = x; Y = y; W = MathF.Max(0, w); H = MathF.Max(0, h); }
    internal float Right => X + W;
    internal float Bottom => Y + H;
    internal static readonly RectF Infinite = new(-1e6f, -1e6f, 2e6f, 2e6f);
    internal static readonly RectF Empty = new(0, 0, 0, 0);
    /// <summary>Half-open: the left/top edge is inside, the right/bottom edge is not (adjacent cells never both hit).</summary>
    internal bool Contains(Vec p) => p.X >= X && p.X < X + W && p.Y >= Y && p.Y < Y + H;
    internal RectF Intersect(RectF o)
    {
        float x = MathF.Max(X, o.X), y = MathF.Max(Y, o.Y);
        return new RectF(x, y, MathF.Min(Right, o.Right) - x, MathF.Min(Bottom, o.Bottom) - y);
    }
    internal RectF Inset(float l, float t, float r, float b) => new(X + l, Y + t, W - l - r, H - t - b);
    internal RectF Inset(float all) => Inset(all, all, all, all);
    internal RectF Offset(float dx, float dy) => new(X + dx, Y + dy, W, H);
    internal RectF Expand(float all) => new(X - all, Y - all, W + 2 * all, H + 2 * all);
    public override string ToString() => $"[{X:0.#},{Y:0.#} {W:0.#}x{H:0.#}]";
}

internal static class KitMath
{
    internal const float ReferenceHeight = 1080f;
    internal const float MinFactor = .5f, MaxFactor = 2f, MinScale = .5f, MaxScale = 4f;

    /// <summary>UI scale = screen height / 1080 × configurable factor (factor 0.5–2, result 0.5–4).</summary>
    internal static float Scale(int screenHeight, float factor)
    {
        if (screenHeight <= 0 || float.IsNaN(factor)) return 1f;
        return Math.Clamp(screenHeight / ReferenceHeight * Math.Clamp(factor, MinFactor, MaxFactor), MinScale, MaxScale);
    }

    /// <summary>Input System pointer (pixels, origin bottom-left) to kit units (origin top-left).</summary>
    internal static Vec ScreenToKit(float px, float py, int screenHeight, float scale) =>
        new(px / scale, (screenHeight - py) / scale);

    /// <summary>
    /// Distribute <paramref name="total"/> over items separated by <paramref name="gap"/>.
    /// Spec &gt; 0 is a fixed size; spec &lt;= 0 is flexible with weight -spec (0 means weight 1).
    /// Flexible items never go below zero; fixed items keep their size even if that overflows.
    /// </summary>
    internal static float[] Distribute(float total, float gap, IReadOnlyList<float> specs)
    {
        var sizes = new float[specs.Count];
        if (specs.Count == 0) return sizes;
        float fixedSum = 0, weights = 0;
        for (int i = 0; i < specs.Count; i++)
        {
            if (specs[i] > 0) { sizes[i] = specs[i]; fixedSum += specs[i]; }
            else weights += specs[i] == 0 ? 1 : -specs[i];
        }
        float free = MathF.Max(0, total - fixedSum - gap * (specs.Count - 1));
        if (weights > 0)
            for (int i = 0; i < specs.Count; i++)
                if (specs[i] <= 0) sizes[i] = free * (specs[i] == 0 ? 1 : -specs[i]) / weights;
        return sizes;
    }

    /// <summary>
    /// Vertical stack: items keep their measured height; items flagged <paramref name="fill"/> share
    /// the leftover of <paramref name="available"/> equally (on top of their measured minimum).
    /// </summary>
    internal static float[] StackHeights(float available, float gap, IReadOnlyList<float> heights, IReadOnlyList<bool> fill)
    {
        var result = heights.ToArray();
        int fills = fill.Count(f => f);
        if (fills == 0 || heights.Count == 0) return result;
        float used = heights.Sum() + gap * (heights.Count - 1);
        float extra = MathF.Max(0, available - used) / fills;
        for (int i = 0; i < result.Length; i++) if (fill[i]) result[i] += extra;
        return result;
    }

    /// <summary>Row: fixed/intrinsic widths (&gt;= 0) stay; flexible (&lt; 0, weight = -w) share the rest.</summary>
    internal static float[] RowWidths(float available, float gap, IReadOnlyList<float> intrinsic)
    {
        // Map intrinsic convention (>=0 fixed, <0 flex weight) onto Distribute (>0 fixed, <=0 flex).
        var specs = intrinsic.Select(w => w >= 0 ? MathF.Max(w, 0.0001f) : w).ToArray();
        return Distribute(available, gap, specs);
    }

    /// <summary>Offsets (start positions) of items laid out with <see cref="Distribute"/>.</summary>
    internal static float[] Offsets(float start, float gap, IReadOnlyList<float> sizes)
    {
        var o = new float[sizes.Count];
        float x = start;
        for (int i = 0; i < sizes.Count; i++) { o[i] = x; x += sizes[i] + gap; }
        return o;
    }

    /// <summary>
    /// Next stepper value: snaps to the step grid (anchored at min when finite, else 0), moves
    /// <paramref name="direction"/> steps and clamps. Removes binary drift (0.1+0.2 → 0.3).
    /// </summary>
    internal static double Step(double value, double step, int direction, double min, double max)
    {
        if (step <= 0 || double.IsNaN(step)) step = 1;
        double anchor = double.IsFinite(min) ? min : 0;
        double index = Math.Round((value - anchor) / step, 6);
        double snapped = direction > 0 ? Math.Floor(index + 1e-9) : direction < 0 ? Math.Ceiling(index - 1e-9) : Math.Round(index);
        double next = anchor + (snapped + direction) * step;
        return Clean(Math.Clamp(next, min, max), step);
    }

    /// <summary>Clamp + round to the step's decimal precision.</summary>
    internal static double Clean(double value, double step)
    {
        int decimals = Math.Clamp(Decimals(step) + 1, 0, 10);
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    }
    internal static int Decimals(double step)
    {
        for (int d = 0; d <= 8; d++) if (Math.Abs(Math.Round(step, d) - step) < 1e-9) return d;
        return 8;
    }

    /// <summary>Default stepper text: invariant culture (game UI is English), decimals from the step, optional unit.</summary>
    internal static string FormatValue(double value, double step, string? unit, string? format = null)
    {
        string text = value.ToString(format ?? (Decimals(step) == 0 ? "0" : "0." + new string('0', Decimals(step))), CultureInfo.InvariantCulture);
        if (text == "-0" || text.StartsWith("-0.") && double.Parse(text, CultureInfo.InvariantCulture) == 0) text = text.TrimStart('-');
        return string.IsNullOrEmpty(unit) ? text : unit is "°" or "%" ? text + unit : text + " " + unit;
    }

    internal static float ClampScroll(float offset, float content, float viewport) =>
        Math.Clamp(offset, 0, MathF.Max(0, content - viewport));

    /// <summary>Rows [first, first+count) that intersect the viewport for fixed-height rows.</summary>
    internal static (int First, int Count) VisibleRange(float scroll, float viewport, float rowH, float gap, int total)
    {
        if (total <= 0 || rowH <= 0) return (0, 0);
        float pitch = rowH + gap;
        int first = Math.Clamp((int)MathF.Floor(scroll / pitch), 0, total - 1);
        int last = Math.Clamp((int)MathF.Floor((scroll + viewport) / pitch), 0, total - 1);
        return (first, last - first + 1);
    }

    /// <summary>Mouse wheel delta to notches: Windows reports ±120 per notch, some devices ±1.</summary>
    internal static float WheelNotches(float raw) => MathF.Abs(raw) >= 30 ? raw / 120f : raw;

    /// <summary>Keep a dragged panel reachable: at least <paramref name="keep"/> units of the title bar on screen.</summary>
    internal static RectF ClampToView(RectF r, float viewW, float viewH, float keep = 120, float titleH = Tok.HeaderH)
    {
        float x = Math.Clamp(r.X, MathF.Min(0, keep - r.W), MathF.Max(0, viewW - keep));
        float y = Math.Clamp(r.Y, 0, MathF.Max(0, viewH - titleH));
        return new RectF(x, y, r.W, r.H);
    }

    /// <summary>Centre a panel in the view (used when no position is stored).</summary>
    internal static RectF Centre(float w, float h, float viewW, float viewH) =>
        new(MathF.Max(0, (viewW - w) / 2), MathF.Max(0, (viewH - h) / 2), w, h);

    /// <summary>Tooltip placement near the pointer, flipped/clamped to stay on screen.</summary>
    internal static RectF PlaceTooltip(Vec pointer, float w, float h, float viewW, float viewH)
    {
        float x = pointer.X + 14, y = pointer.Y + 20;
        if (x + w > viewW - 4) x = pointer.X - w - 8;
        if (y + h > viewH - 4) y = pointer.Y - h - 8;
        return new RectF(Math.Clamp(x, 4, MathF.Max(4, viewW - w - 4)), Math.Clamp(y, 4, MathF.Max(4, viewH - h - 4)), w, h);
    }

    /// <summary>
    /// Popup (dropdown list) in screen kit units: below the anchor with <paramref name="gap"/>, or above
    /// when it does not fit below and there is more room above; shrunk (then scrolled) to the room
    /// available and never off-screen.
    /// </summary>
    internal static RectF PlacePopup(RectF anchor, float w, float h, float viewW, float viewH, float gap = 4)
    {
        float roomBelow = viewH - 4 - (anchor.Bottom + gap), roomAbove = anchor.Y - gap - 4;
        bool up = h > roomBelow && roomAbove > roomBelow;
        float room = MathF.Max(0, up ? roomAbove : roomBelow);
        if (h > room) h = room;
        float y = up ? anchor.Y - gap - h : anchor.Bottom + gap;
        float x = Math.Clamp(anchor.X, 4, MathF.Max(4, viewW - w - 4));
        return new RectF(x, y, w, h);
    }

    /// <summary>"x,y" in invariant culture for the panel position store.</summary>
    internal static string FormatPosition(float x, float y) => x.ToString("0.#", CultureInfo.InvariantCulture) + "," + y.ToString("0.#", CultureInfo.InvariantCulture);
    internal static bool TryParsePosition(string? text, out float x, out float y)
    {
        x = y = 0;
        var parts = (text ?? "").Split(',');
        return parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) && float.IsFinite(x) && float.IsFinite(y);
    }

    /// <summary>Config key for a panel: BepInEx keys may not contain = \n \t \ " ' [ ].</summary>
    internal static string PositionKey(string modGuid, string id)
    {
        var chars = (modGuid + "." + id).Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }

    /// <summary>Lamp afterglow: lit while on, or until <paramref name="hold"/> seconds after it was last on.</summary>
    internal static bool LampLit(bool on, float now, ref float lastOn, float hold)
    {
        if (on) { lastOn = now; return true; }
        return hold > 0 && now - lastOn < hold;
    }
}

/// <summary>Front-to-back panel order. Index 0 is the bottom; the last item is on top.</summary>
internal sealed class ZOrder<T> where T : class
{
    private readonly List<T> items = new();
    internal IReadOnlyList<T> Items => items;
    internal T? Top => items.Count > 0 ? items[^1] : null;
    internal void BringToFront(T item) { items.Remove(item); items.Add(item); }
    internal void Remove(T item) => items.Remove(item);
    internal int IndexOf(T item) => items.IndexOf(item);
    /// <summary>Topmost item for which <paramref name="hit"/> is true.</summary>
    internal T? TopAt(Func<T, bool> hit)
    {
        for (int i = items.Count - 1; i >= 0; i--) if (hit(items[i])) return items[i];
        return null;
    }
    /// <summary>Topmost item matching <paramref name="filter"/> (e.g. open panels).</summary>
    internal T? TopWhere(Func<T, bool> filter) => TopAt(filter);
}

/// <summary>Key auto-repeat for held editing keys (backspace, delete, arrows).</summary>
internal sealed class KeyRepeat
{
    internal const float Delay = .45f, Rate = .035f;
    /// <summary>Repeat rate for press-and-hold buttons (Stepper, scrollbar track paging).</summary>
    internal const float StepRate = .07f;
    private float next = float.MaxValue;
    /// <summary>True on the press frame and then at the repeat rate while held.</summary>
    internal bool Tick(bool pressedThisFrame, bool held, float now)
    {
        if (pressedThisFrame) { next = now + Delay; return true; }
        if (!held) { next = float.MaxValue; return false; }
        if (now >= next) { next = now + Rate; return true; }
        return false;
    }
}
