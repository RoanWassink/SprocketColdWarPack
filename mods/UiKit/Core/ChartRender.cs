namespace SprocketUiKit;

/// <summary>
/// Pure chart shape rendering (0.1.7) on the kit's existing <see cref="Raster"/>: grid, axes, series
/// (line/staircase/points), bars, waterfall bars, reference lines and legend chips. Coordinates are
/// reference px with y down; <c>scale</c> converts them to device px (the CanvasView draw scale).
/// Keeping this here lets the tests time a real full chart raster pass. Text is drawn by the Painter.
/// </summary>
internal static class ChartRender
{
    internal const float LineWidth = 2f;
    internal const float DotRadius = 3.2f;
    internal const float AxisWidth = 1f;
    internal const float RefWidth = 1.2f;
    internal const float RefDash = 5f;
    private static ChartPoint[] stepBuffer = new ChartPoint[256];

    /// <summary>Subtle grid at every tick, then the left and bottom axes in the border colour.</summary>
    internal static void Grid(Raster raster, float scale, RectF plot, ChartRange xRange, ChartRange yRange, ReadOnlySpan<float> xTicks, ReadOnlySpan<float> yTicks)
    {
        if (plot.W <= 0 || plot.H <= 0) return;
        uint grid = Raster.Pack(Tok.ChartGrid);
        float gx0 = plot.X * scale, gx1 = plot.Right * scale;
        float gy0 = plot.Y * scale, gy1 = plot.Bottom * scale;
        for (int i = 0; i < yTicks.Length; i++)
        {
            float y = ChartMath.MapY(yTicks[i], yRange, plot.Y, plot.H);
            if (float.IsFinite(y)) raster.Segment(gx0, y * scale, gx1, y * scale, scale, grid);
        }
        for (int i = 0; i < xTicks.Length; i++)
        {
            float x = ChartMath.MapX(xTicks[i], xRange, plot.X, plot.W);
            if (float.IsFinite(x)) raster.Segment(x * scale, gy0, x * scale, gy1, scale, grid);
        }
        uint axis = Raster.Pack(Tok.ChartAxis);
        raster.Segment(gx0, gy0, gx0, gy1, AxisWidth * scale, axis);
        raster.Segment(gx0, gy1, gx1, gy1, AxisWidth * scale, axis);
    }

    /// <summary>
    /// One line series. Non-finite points are skipped and split the line into separate runs, so a NaN
    /// leaves a real gap. A run of one point is drawn as a dot; <see cref="SeriesStyle.Points"/> draws
    /// every finite point as a dot.
    /// </summary>
    internal static void Series(Raster raster, float scale, RectF plot, ChartRange xRange, ChartRange yRange, ReadOnlySpan<ChartPoint> points, SeriesStyle style, Rgba colour)
    {
        if (!xRange.Valid || !yRange.Valid) return;
        uint packed = Raster.Pack(colour);
        if (style == SeriesStyle.Points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                if (!points[i].Finite) continue;
                float x = ChartMath.MapX(points[i].X, xRange, plot.X, plot.W);
                float y = ChartMath.MapY(points[i].Y, yRange, plot.Y, plot.H);
                if (float.IsFinite(x) && float.IsFinite(y)) raster.FillCircle(x * scale, y * scale, DotRadius * scale, packed);
            }
            return;
        }
        int start = -1;
        for (int i = 0; i <= points.Length; i++)
        {
            bool finite = i < points.Length && points[i].Finite;
            if (finite && start < 0) { start = i; continue; }
            if (finite) continue;
            if (start >= 0)
            {
                DrawRun(raster, scale, plot, xRange, yRange, points.Slice(start, i - start), style, packed);
                start = -1;
            }
        }
    }

    private static void DrawRun(Raster raster, float scale, RectF plot, ChartRange xRange, ChartRange yRange, ReadOnlySpan<ChartPoint> run, SeriesStyle style, uint colour)
    {
        if (run.Length == 0) return;
        int count = run.Length;
        if (style == SeriesStyle.Step)
        {
            if (count == 1) { Dot(raster, scale, plot, xRange, yRange, run[0], colour); return; }
            int floats = (2 * count - 1) * 2;
            var buffer = raster.Scratch(floats);
            // Reused scratch for the staircase points: one allocation when a longer series first appears,
            // never per redraw. (A redraw runs on the Unity main thread, so one buffer is enough.)
            if (stepBuffer.Length < count * 2 - 1) stepBuffer = new ChartPoint[count * 2 - 1];
            int n = ChartMath.BuildStep(run, stepBuffer);
            for (int i = 0; i < n && i * 2 + 1 < floats; i++)
            {
                buffer[2 * i] = ChartMath.MapX(stepBuffer[i].X, xRange, plot.X, plot.W) * scale;
                buffer[2 * i + 1] = ChartMath.MapY(stepBuffer[i].Y, yRange, plot.Y, plot.H) * scale;
            }
            raster.Polyline(new ReadOnlySpan<float>(buffer, 0, Math.Min(floats, n * 2)), LineWidth * scale, colour);
            return;
        }
        if (count == 1) { Dot(raster, scale, plot, xRange, yRange, run[0], colour); return; }
        var line = raster.Scratch(count * 2);
        for (int i = 0; i < count; i++)
        {
            line[2 * i] = ChartMath.MapX(run[i].X, xRange, plot.X, plot.W) * scale;
            line[2 * i + 1] = ChartMath.MapY(run[i].Y, yRange, plot.Y, plot.H) * scale;
        }
        raster.Polyline(new ReadOnlySpan<float>(line, 0, count * 2), LineWidth * scale, colour);
    }

    private static void Dot(Raster raster, float scale, RectF plot, ChartRange xr, ChartRange yr, ChartPoint p, uint colour)
    {
        float x = ChartMath.MapX(p.X, xr, plot.X, plot.W), y = ChartMath.MapY(p.Y, yr, plot.Y, plot.H);
        if (float.IsFinite(x) && float.IsFinite(y)) raster.FillCircle(x * scale, y * scale, DotRadius * scale, colour);
    }

    /// <summary>Horizontal dashed reference line at <paramref name="value"/> (label is Painter text).</summary>
    internal static void RefLine(Raster raster, float scale, RectF plot, ChartRange yRange, float value, Rgba colour)
    {
        float y = ChartMath.MapY(value, yRange, plot.Y, plot.H);
        if (!float.IsFinite(y) || y < plot.Y - .5f || y > plot.Bottom + .5f) return;
        Span<float> seg = stackalloc float[4];
        seg[0] = plot.X * scale; seg[1] = y * scale; seg[2] = plot.Right * scale; seg[3] = y * scale;
        raster.Polyline(seg, RefWidth * scale, Raster.Pack(colour), RefDash * scale);
    }

    /// <summary>One bar from <paramref name="from"/> to <paramref name="to"/> (values on the y axis).</summary>
    internal static void Bar(Raster raster, float scale, RectF slot, RectF plot, ChartRange yRange, float from, float to, Rgba fill, Rgba border)
    {
        float yTop = ChartMath.MapY(MathF.Max(from, to), yRange, plot.Y, plot.H);
        float yBottom = ChartMath.MapY(MathF.Min(from, to), yRange, plot.Y, plot.H);
        if (!float.IsFinite(yTop) || !float.IsFinite(yBottom)) return;
        float x = slot.X * scale, w = slot.W * scale;
        float y = MathF.Min(yTop, yBottom), h = MathF.Max(.5f, MathF.Abs(yBottom - yTop));
        raster.FillRect(x, y, w, h, Raster.Pack(fill));
        if (border.A == 0) return;
        Span<float> q = stackalloc float[8];
        q[0] = x; q[1] = y; q[2] = x + w; q[3] = y; q[4] = x + w; q[5] = y + h; q[6] = x; q[7] = y + h;
        raster.Polyline(q, AxisWidth * scale, Raster.Pack(border));
    }

    /// <summary>Small legend chip: filled rectangle with a 1 px border (the series colour is a swatch).</summary>
    internal static void Chip(Raster raster, float scale, float x, float y, float w, float h, Rgba fill, Rgba border)
    {
        raster.FillRect(x * scale, y * scale, w * scale, h * scale, Raster.Pack(fill));
        Span<float> q = stackalloc float[8];
        q[0] = x; q[1] = y; q[2] = x + w; q[3] = y; q[4] = x + w; q[5] = y + h; q[6] = x; q[7] = y + h;
        var buffer = raster.Scratch(8);
        for (int i = 0; i < 8; i++) buffer[i] = q[i] * scale;
        raster.Polyline(new ReadOnlySpan<float>(buffer, 0, 8), AxisWidth * scale, Raster.Pack(border), 0, true);
    }
}
