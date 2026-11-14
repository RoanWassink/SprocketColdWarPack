using System.Globalization;
namespace SprocketUiKit;

/// <summary>How a line series is drawn (0.1.7): a straight line, a staircase or separate points.</summary>
public enum SeriesStyle { Line, Step, Points }

/// <summary>One data point of a chart series, in the same units as the chart axes.</summary>
internal readonly struct ChartPoint
{
    internal readonly float X, Y;
    internal ChartPoint(float x, float y) { X = x; Y = y; }
    internal bool Finite => float.IsFinite(X) && float.IsFinite(Y);
    public override string ToString() => $"({X:0.###},{Y:0.###})";
}

/// <summary>A resolved axis: exact minimum and maximum, plus the tick step (1, 2 or 5 x 10^n).</summary>
internal readonly struct ChartRange
{
    internal readonly float Min, Max, Step;
    internal ChartRange(float min, float max, float step) { Min = min; Max = max; Step = step; }
    internal float Span => Max - Min;
    internal bool Valid => float.IsFinite(Min) && float.IsFinite(Max) && Max > Min && Step > 0;
}

/// <summary>One bar of a waterfall: the floating span from the old to the new remainder.</summary>
internal readonly struct WaterfallStep
{
    internal readonly float From, To;
    internal readonly bool Stopped;
    internal WaterfallStep(float from, float to, bool stopped) { From = from; To = to; Stopped = stopped; }
}

/// <summary>
/// Pure chart geometry (0.1.7): nice 1/2/5 ticks, automatic and fixed ranges, data-to-pixel
/// mapping, nearest-x lookup, the step polyline, waterfall geometry, NaN rules and the cheap
/// data fingerprint. No Unity types, so the tests time the real code.
/// </summary>
internal static class ChartMath
{
    internal const int MaxTicks = 64;

    /// <summary>
    /// Data range with nice ticks. A fixed bound is used exactly; an automatic bound snaps out to a
    /// multiple of the step. An empty or flat range (one point, all equal, no data) gets a margin.
    /// </summary>
    internal static ChartRange Range(float dataMin, float dataMax, float? fixedMin, float? fixedMax, bool includeZero, float? tick = null, int targetTicks = 5)
    {
        bool hasData = float.IsFinite(dataMin) && float.IsFinite(dataMax) && dataMax >= dataMin;
        bool hasLo = fixedMin is { } fm && float.IsFinite(fm);
        bool hasHi = fixedMax is { } fx && float.IsFinite(fx);
        float lo = hasLo ? fixedMin!.Value : hasData ? dataMin : 0f;
        float hi = hasHi ? fixedMax!.Value : hasData ? dataMax : 1f;
        if (includeZero) { lo = MathF.Min(lo, 0f); hi = MathF.Max(hi, 0f); }
        if (hi < lo) (lo, hi) = (hi, lo);
        if (hi - lo < 1e-6f)
        {
            float pad = MathF.Max(1f, MathF.Abs(lo) * .05f);
            lo -= pad; hi += pad;
        }
        float step = tick is { } t && float.IsFinite(t) && t > 0 ? t : NiceStep(hi - lo, targetTicks);
        if (!(step > 0) || !float.IsFinite(step)) step = 1f;
        if (!hasLo) lo = MathF.Floor(lo / step) * step;
        if (!hasHi) hi = MathF.Ceiling(hi / step) * step;
        if (hi - lo < step) hi = lo + step;
        return new ChartRange(lo, hi, step);
    }

    /// <summary>Nice tick step: 1, 2 or 5 x 10^n, about <paramref name="targetTicks"/> intervals.</summary>
    internal static float NiceStep(float span, int targetTicks = 5)
    {
        if (!(span > 0) || !float.IsFinite(span)) return 1f;
        targetTicks = Math.Clamp(targetTicks, 2, 12);
        float rough = span / targetTicks;
        float power = MathF.Pow(10f, MathF.Floor(MathF.Log10(rough)));
        float fraction = rough / power;
        float nice = fraction <= 1f ? 1f : fraction <= 2f ? 2f : fraction <= 5f ? 5f : 10f;
        return nice * power;
    }

    /// <summary>Tick values inside the range, starting at the first multiple of the step. At most <c>MaxTicks</c>.</summary>
    internal static int Ticks(ChartRange range, Span<float> into)
    {
        if (!range.Valid || into.Length == 0) return 0;
        long first = (long)MathF.Ceiling(range.Min / range.Step - 1e-4f);
        long last = (long)MathF.Floor(range.Max / range.Step + 1e-4f);
        if (last < first) return 0;
        long total = last - first + 1;
        int max = Math.Min(into.Length, MaxTicks);
        long stride = total > max ? (total + max - 1) / max : 1;
        int count = 0;
        for (long i = first; i <= last && count < max; i += stride)
        {
            float v = i * range.Step;
            if (v < range.Min - range.Step * 1e-3f || v > range.Max + range.Step * 1e-3f) continue;
            into[count++] = MathF.Abs(v) < range.Step * 1e-6f ? 0f : v;
        }
        return count;
    }

    /// <summary>Value to a pixel inside a horizontal plot span. NaN for a non-finite value.</summary>
    internal static float MapX(float value, ChartRange range, float x0, float width)
    {
        if (!float.IsFinite(value) || !range.Valid) return float.NaN;
        return x0 + (value - range.Min) / range.Span * width;
    }

    /// <summary>Value to a pixel inside a vertical plot span (y down). NaN for a non-finite value.</summary>
    internal static float MapY(float value, ChartRange range, float y0, float height)
    {
        if (!float.IsFinite(value) || !range.Valid) return float.NaN;
        return y0 + height - (value - range.Min) / range.Span * height;
    }

    /// <summary>Index of the point closest to <paramref name="x"/>, ignoring NaN/infinite points.</summary>
    internal static int NearestPointIndex(ReadOnlySpan<ChartPoint> points, float x)
    {
        if (!float.IsFinite(x)) return -1;
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            if (!points[i].Finite) continue;
            float d = MathF.Abs(points[i].X - x);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    /// <summary>Index of the closest finite x value; used by the hover line.</summary>
    internal static int NearestIndex(ReadOnlySpan<float> xs, float x)
    {
        if (!float.IsFinite(x)) return -1;
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < xs.Length; i++)
        {
            if (!float.IsFinite(xs[i])) continue;
            float d = MathF.Abs(xs[i] - x);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Staircase polyline: horizontal to the next x, then vertical to its y. NaN/infinite points break
    /// the run, so the gap stays empty; a run of one point stays a single point (drawn as a dot).
    /// </summary>
    internal static int BuildStep(ReadOnlySpan<ChartPoint> points, Span<ChartPoint> into)
    {
        int n = 0, i = 0;
        while (i < points.Length && n < into.Length)
        {
            if (!points[i].Finite) { i++; continue; }
            var first = points[i];
            into[n++] = first;
            float previousY = first.Y;
            int j = i + 1;
            while (j < points.Length && points[j].Finite)
            {
                if (n + 2 > into.Length) { i = j; break; }
                into[n++] = new ChartPoint(points[j].X, previousY);
                into[n++] = points[j];
                previousY = points[j].Y;
                j++;
            }
            i = j;
        }
        return n;
    }

    /// <summary>
    /// Waterfall geometry: the start bar from 0 to the start, each layer a floating bar from the old to
    /// the new remainder, then the end bar from 0 to the remainder. A layer that reaches 0 is clamped
    /// to 0, marked <see cref="WaterfallStep.Stopped"/> and ends the waterfall (the layers after it are
    /// not drawn; the end bar is 0).
    /// </summary>
    internal static int Waterfall(float start, ReadOnlySpan<float> deltas, Span<WaterfallStep> into)
    {
        int n = 0;
        float remaining = float.IsFinite(start) ? MathF.Max(0f, start) : 0f;
        if (into.Length == 0) return 0;
        into[n++] = new WaterfallStep(0f, remaining, false);
        for (int i = 0; i < deltas.Length && n < into.Length; i++)
        {
            float delta = float.IsFinite(deltas[i]) ? deltas[i] : 0f;
            float from = remaining;
            float to = remaining + delta;
            if (to <= 0f)
            {
                if (n < into.Length) into[n++] = new WaterfallStep(from, 0f, true);
                remaining = 0f;
                break;
            }
            remaining = to;
            if (n < into.Length) into[n++] = new WaterfallStep(from, to, false);
        }
        if (n < into.Length) into[n++] = new WaterfallStep(0f, remaining, false);
        return n;
    }

    /// <summary>Reference-px rectangle of one bar in a slot layout with a gap between bars.</summary>
    internal static RectF BarRect(RectF plot, int index, int count, float gap = 4f)
    {
        if (count <= 0) return new RectF(plot.X, plot.Y, 0, plot.H);
        float slot = plot.W / count;
        float w = MathF.Max(1f, slot - gap);
        return new RectF(plot.X + slot * index + (slot - w) * .5f, plot.Y, w, plot.H);
    }

    /// <summary>Bar index under a plot-local x, or -1 outside the plot.</summary>
    internal static int BarAt(RectF plot, int count, float x)
    {
        if (count <= 0 || plot.W <= 0 || x < plot.X || x >= plot.Right) return -1;
        int index = (int)((x - plot.X) / (plot.W / count));
        return Math.Clamp(index, 0, count - 1);
    }

    /// <summary>Axis caption in the kit convention: "Angle (deg)" when both are set, either one alone otherwise.</summary>
    internal static string AxisLabel(string? label, string? unit)
    {
        label ??= "";
        unit ??= "";
        if (label.Length == 0) return unit;
        if (unit.Length == 0) return label;
        return label + " (" + unit + ")";
    }

    /// <summary>Tick text without trailing zeros, invariant culture, never "-0".</summary>
    internal static string FormatTick(float value, float step)
    {
        if (!float.IsFinite(value)) return "";
        int decimals = step > 0 && step < 1 ? Math.Clamp((int)MathF.Ceiling(-MathF.Log10(step) - 1e-4f), 0, 6) : 0;
        string text = value.ToString(decimals > 0 ? "0." + new string('0', decimals) : "0.######", CultureInfo.InvariantCulture);
        if (text.IndexOf('.') >= 0) text = text.TrimEnd('0').TrimEnd('.');
        return text.Length == 0 || text == "-0" ? "0" : text;
    }

    /// <summary>Value for a tooltip, with the axis unit appended.</summary>
    internal static string FormatValue(float value, float step, string? unit)
    {
        if (!float.IsFinite(value)) return "-";
        var text = FormatTick(value, step);
        return string.IsNullOrEmpty(unit) ? text : text + " " + unit;
    }

    /// <summary>Cheap, allocation-free hash over the point values (NaN bit patterns included).</summary>
    internal static int HashPoints(ReadOnlySpan<ChartPoint> points)
    {
        unchecked
        {
            int h = (int)2166136261;
            for (int i = 0; i < points.Length; i++)
            {
                h = (h ^ BitConverter.SingleToInt32Bits(points[i].X)) * 16777619;
                h = (h ^ BitConverter.SingleToInt32Bits(points[i].Y)) * 16777619;
            }
            return h;
        }
    }

    /// <summary>Finite bounds over a series; false when there is no finite point.</summary>
    internal static bool Bounds(ReadOnlySpan<ChartPoint> points, out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = minY = float.MaxValue; maxX = maxY = float.MinValue;
        bool any = false;
        for (int i = 0; i < points.Length; i++)
        {
            var p = points[i];
            if (!p.Finite) continue;
            any = true;
            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
        }
        return any;
    }
}

/// <summary>
/// Per-series change detection (0.1.7): the list reference, the point count and a cheap value hash.
/// Updating allocates nothing, so a chart can poll its getters every frame.
/// </summary>
internal struct ChartFingerprint
{
    private object? reference;
    private int count, hash;
    private bool primed;

    internal bool Changed(object? listReference, ReadOnlySpan<ChartPoint> points)
    {
        int h = ChartMath.HashPoints(points);
        bool changed = !primed || !ReferenceEquals(listReference, reference) || points.Length != count || h != hash;
        reference = listReference; count = points.Length; hash = h; primed = true;
        return changed;
    }
}
