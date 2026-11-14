using System.Globalization;
namespace SprocketUiKit;

/// <summary>Resize grip under the pointer (0.1.4): right edge, bottom edge or the bottom-right corner.</summary>
internal enum ResizeEdge { None, Right, Bottom, Corner }

/// <summary>
/// Remembered panel state (0.1.4): position, and optionally size and maximized. When maximized, X/Y/W/H
/// is the restore rectangle. W/H = 0 means "no size stored" (0.1.3 line with only "x,y").
/// </summary>
internal readonly record struct PanelState(float X, float Y, float W = 0, float H = 0, bool Maximized = false)
{
    internal bool HasSize => W > 0 && H > 0;
}

/// <summary>Pure window logic for 0.1.4: maximize/restore, resize, keep on screen, double-click, state store.</summary>
internal static class Window
{
    /// <summary>Grip band: this far inside the panel edge and this far outside it; corner square size inside.</summary>
    internal const float GripInside = 4, GripOutside = 4, GripCorner = 16;
    /// <summary>Pointer movement before a press on a maximized title bar counts as a drag (restores).</summary>
    internal const float DragThreshold = 4;

    /// <summary>Maximized rectangle: the visible view minus <paramref name="margin"/> on every side.</summary>
    internal static RectF Maximized(float viewW, float viewH, float margin = Tok.MaximizeMargin) =>
        new(margin, margin, MathF.Max(1, viewW - 2 * margin), MathF.Max(1, viewH - 2 * margin));

    /// <summary>Size clamp: at least the minimum, at most the view (the view wins over the minimum).</summary>
    internal static (float W, float H) ClampSize(float w, float h, float minW, float minH, float viewW, float viewH)
    {
        w = MathF.Min(MathF.Max(w, minW), MathF.Max(1, viewW));
        h = MathF.Min(MathF.Max(h, minH), MathF.Max(1, viewH));
        return (w, h);
    }

    /// <summary>
    /// New rectangle while a grip is dragged by (<paramref name="dx"/>, <paramref name="dy"/>) from
    /// <paramref name="start"/>. The top-left corner stays; the size is clamped to the minimum and to the
    /// view: the right/bottom edge never goes past the view edge (the view wins over the minimum).
    /// </summary>
    internal static RectF Resize(RectF start, ResizeEdge edge, float dx, float dy, float minW, float minH, float viewW, float viewH)
    {
        float w = start.W, h = start.H;
        if (edge is ResizeEdge.Right or ResizeEdge.Corner) w += dx;
        if (edge is ResizeEdge.Bottom or ResizeEdge.Corner) h += dy;
        float loW = MathF.Min(minW, viewW), loH = MathF.Min(minH, viewH);
        float hiW = MathF.Max(loW, viewW - MathF.Max(0, start.X)), hiH = MathF.Max(loH, viewH - MathF.Max(0, start.Y));
        if (edge != ResizeEdge.Bottom) w = Math.Clamp(w, loW, hiW);
        if (edge != ResizeEdge.Right) h = Math.Clamp(h, loH, hiH);
        return new RectF(start.X, start.Y, w, h);
    }

    /// <summary>
    /// Keep a panel fully on screen (#23): a size larger than the view shrinks to the view (the view
    /// wins over the minimum), then the panel moves inside the view. A panel already inside is unchanged.
    /// </summary>
    internal static RectF FitOnScreen(RectF r, float viewW, float viewH)
    {
        float w = MathF.Min(r.W, MathF.Max(1, viewW)), h = MathF.Min(r.H, MathF.Max(1, viewH));
        float x = Math.Clamp(r.X, 0, MathF.Max(0, viewW - w)), y = Math.Clamp(r.Y, 0, MathF.Max(0, viewH - h));
        return new RectF(x, y, w, h);
    }
    internal static bool FullyOnScreen(RectF r, float viewW, float viewH) =>
        r.X >= -.01f && r.Y >= -.01f && r.Right <= viewW + .01f && r.Bottom <= viewH + .01f;

    /// <summary>
    /// Dragging the title bar of a maximized panel restores it first (like Windows): the restored panel is
    /// placed so the pointer keeps the same relative x position in the title bar and the same height in it.
    /// </summary>
    internal static RectF RestoreUnderPointer(Vec pointer, RectF maximized, float restoreW, float restoreH)
    {
        float fx = maximized.W > 0 ? Math.Clamp((pointer.X - maximized.X) / maximized.W, 0, 1) : .5f;
        float oy = Math.Clamp(pointer.Y - maximized.Y, 0, Tok.HeaderH - 1);
        return new RectF(pointer.X - fx * restoreW, pointer.Y - oy, restoreW, restoreH);
    }

    /// <summary>
    /// Resize grip versus a scrollbar (0.1.5): inside the panel a click on an overflowing scrollbar's
    /// strip goes to the bar, so a full-width ScrollView's thumb is draggable. The grip keeps the band
    /// outside the panel edge (and the bottom/corner bands), so resizing stays available there.
    /// </summary>
    internal static ResizeEdge ResolveGrip(ResizeEdge grip, bool overScrollBar, bool insidePanel) =>
        overScrollBar && insidePanel ? ResizeEdge.None : grip;

    /// <summary>Grip at a panel-local point (y down); the bands reach <see cref="GripOutside"/> past the edges.</summary>
    internal static ResizeEdge GripAt(Vec local, float w, float h)
    {
        float x = local.X, y = local.Y;
        if (x < 0 || y < 0 || x >= w + GripOutside || y >= h + GripOutside) return ResizeEdge.None;
        if (x >= w - GripCorner && y >= h - GripCorner) return ResizeEdge.Corner;
        if (x >= w - GripInside) return ResizeEdge.Right;
        if (y >= h - GripInside) return ResizeEdge.Bottom;
        return ResizeEdge.None;
    }

    // ---------- Columns (0.1.4) ----------
    /// <summary>
    /// Column widths for a panel that may be resized. Mixed specs behave as before (<see cref="KitMath.Distribute"/>):
    /// fixed columns (sidebars) keep their width and flexible ones take all extra space. Only when every
    /// column is fixed, the growth of the panel beyond its original width (<paramref name="grow"/>, capped at
    /// the free space) goes to the widest column (ties: an inner column before an edge column), so fixed
    /// sidebars stay fixed. At the original size <paramref name="grow"/> is 0: identical to 0.1.3.
    /// </summary>
    internal static float[] ColumnWidths(float total, IReadOnlyList<float> specs, float grow)
    {
        var w = KitMath.Distribute(total, 0, specs);
        if (grow <= 0 || specs.Count == 0) return w;
        float sum = 0;
        for (int i = 0; i < specs.Count; i++) { if (specs[i] <= 0) return w; sum += w[i]; }
        float extra = MathF.Min(grow, MathF.Max(0, total - sum));
        if (extra <= 0) return w;
        int best = 0;
        for (int i = 1; i < w.Length; i++)
        {
            bool inner = i > 0 && i < w.Length - 1, bestInner = best > 0 && best < w.Length - 1;
            if (w[i] > w[best] + .001f || MathF.Abs(w[i] - w[best]) <= .001f && inner && !bestInner) best = i;
        }
        w[best] += extra;
        return w;
    }

    // ---------- State store ----------
    /// <summary>
    /// "x,y" (0.1.3 format, used while the panel has its original size and is not maximized),
    /// "x,y,w,h" (resized) or "x,y,w,h,max" (maximized; x,y,w,h = restore rectangle). Invariant culture.
    /// </summary>
    internal static string Format(PanelState s)
    {
        string pos = KitMath.FormatPosition(s.X, s.Y);
        if (!s.HasSize) return pos;
        string text = pos + "," + F(s.W) + "," + F(s.H);
        return s.Maximized ? text + ",max" : text;
        static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }
    internal static bool TryParse(string? text, out PanelState state)
    {
        state = default;
        var parts = (text ?? "").Split(',');
        if (parts.Length == 2) { if (!KitMath.TryParsePosition(text, out var x0, out var y0)) return false; state = new PanelState(x0, y0); return true; }
        if (parts.Length != 4 && parts.Length != 5) return false;
        if (parts.Length == 5 && !parts[4].Trim().Equals("max", StringComparison.OrdinalIgnoreCase)) return false;
        var v = new float[4];
        for (int i = 0; i < 4; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !float.IsFinite(v[i])) return false;
        if (v[2] <= 0 || v[3] <= 0) return false;
        state = new PanelState(v[0], v[1], v[2], v[3], parts.Length == 5);
        return true;
    }
}

/// <summary>Double-click detector (title bar): two presses within <see cref="Time"/> s and <see cref="Distance"/> units.</summary>
internal sealed class DoubleClick
{
    internal const float Time = .5f, Distance = 6;
    private float lastTime = -100; private Vec lastPos;
    /// <summary>Register a press; true when it completes a double-click (the next press starts over).</summary>
    internal bool Press(float now, Vec p)
    {
        bool hit = now - lastTime <= Time && MathF.Abs(p.X - lastPos.X) <= Distance && MathF.Abs(p.Y - lastPos.Y) <= Distance;
        if (hit) { lastTime = -100; return true; }
        lastTime = now; lastPos = p;
        return false;
    }
    internal void Reset() => lastTime = -100;
}

/// <summary>
/// Dynamic dropdown options without per-frame allocation (#18): the Choice array is rebuilt only when
/// the labels change (another list with other labels, or the same list edited in place).
/// </summary>
internal sealed class ChoiceCache
{
    private readonly Func<IReadOnlyList<string>> source;
    private string[] lastLabels = Array.Empty<string>();
    private Choice[] choices = Array.Empty<Choice>();
    internal int Rebuilds { get; private set; }
    internal ChoiceCache(Func<IReadOnlyList<string>> source) { this.source = source; }
    internal IReadOnlyList<Choice> Get()
    {
        var list = source();
        if (list == null) { lastLabels = Array.Empty<string>(); choices = Array.Empty<Choice>(); return choices; }
        // same instance or a new list with the same labels: keep the Choice array
        if (Same(list)) return choices;
        int n = list.Count;
        if (lastLabels.Length != n) lastLabels = new string[n];
        // a new array (never edited in place): a popup that is open keeps the list it was built from
        choices = new Choice[n];
        for (int i = 0; i < n; i++) { lastLabels[i] = list[i]; choices[i] = new Choice(list[i]); }
        Rebuilds++;
        return choices;
    }
    private bool Same(IReadOnlyList<string> list)
    {
        if (list.Count != lastLabels.Length) return false;
        for (int i = 0; i < lastLabels.Length; i++) if (!string.Equals(list[i], lastLabels[i], StringComparison.Ordinal)) return false;
        return true;
    }
}

/// <summary>Section count text without per-frame allocation (#18): ToString only when the value changes.</summary>
internal sealed class IntText
{
    private readonly Func<int> source;
    private int last; private string? text;
    internal IntText(Func<int> source) { this.source = source; }
    internal string Get()
    {
        int v = source();
        if (text == null || v != last) { last = v; text = v.ToString(CultureInfo.InvariantCulture); }
        return text;
    }
}
