using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace SprocketUiKit;

/// <summary>
/// One bar of a <see cref="BarChart"/> (0.1.7): a label, a value, an optional colour and an optional
/// tooltip. In a <see cref="BarChart.Waterfall"/> the value is a delta (negative = consumed).
/// </summary>
public readonly struct ChartBar
{
    public string Label { get; init; } = "";
    public float Value { get; init; }
    public Color? Colour { get; init; }
    public string? Tooltip { get; init; }

    public ChartBar(string label, float value, Color? colour = null, string? tooltip = null)
    {
        Label = label ?? "";
        Value = value;
        Colour = colour;
        Tooltip = tooltip;
    }
}

/// <summary>
/// Shared chart element (0.1.7), built on <see cref="CanvasView"/> and <see cref="Painter"/>: one CPU
/// raster into the canvas texture, text on the canvas text layer, and a uGUI hover layer above it.
/// Data comes from getters and is polled every frame; a redraw happens only when the data, the size or
/// the range changes (<see cref="ChartFingerprint"/>: reference, count and value hash). Hover never
/// rasterises: it moves plain images over the image and uses the kit tooltip.
/// </summary>
public abstract class ChartElement : Element
{
    internal sealed class AxisDef
    {
        internal string Label = "";
        internal string Unit = "";
        internal float? Min, Max, Tick;
        internal Func<float, string>? Format;
    }

    internal sealed class RefLineDef
    {
        internal string Label = "";
        internal Func<float>? GetY;
        internal Rgba Colour;
        internal float Value;
        internal bool HasValue;
    }

    internal const float DefaultHeight = 220f;
    internal const float Pad = 8f;
    internal const float TickRow = 16f;
    internal const float AxisLabelRow = 16f;
    internal const float LegendH = 18f;
    internal const float BarLabelRow = 16f;
    internal const float HoverDot = 7f;

    internal ChartElement() { }

    internal readonly AxisDef XAxisDef = new();
    internal readonly AxisDef YAxisDef = new();
    internal readonly List<RefLineDef> RefLines = new();
    internal CanvasView? Canvas;
    internal TextMeshProUGUI? Measure;
    internal RectF Plot;
    internal ChartRange XRange, YRange;
    internal readonly float[] XTicks = new float[ChartMath.MaxTicks];
    internal readonly float[] YTicks = new float[ChartMath.MaxTicks];
    internal int XTickCount, YTickCount;
    internal bool HasData;
    internal bool ConfigDirty = true;
    internal int DataRevision;
    internal bool? LegendChoice;
    internal string EmptyText = "No data.";

    private RectTransform? hoverLayer;
    private Image? hoverLine;
    private BoxView? hoverBox;
    private readonly List<Image> hoverDots = new();
    internal float chartWidth, chartHeight;

    internal override bool Interactive => true;
    internal override float MeasureHeight(float width) => FixedH >= 0 ? FixedH : DefaultHeight;

    /// <summary>Poll the data; true when the pixels must be redrawn.</summary>
    internal abstract bool PollData();
    /// <summary>Draw the data shapes (already clipped to <see cref="Plot"/>).</summary>
    internal abstract void DrawContent(Painter painter, float scale);
    /// <summary>Update the uGUI hover layer from a chart-local pointer.</summary>
    internal abstract void UpdateHover(Vec local);
    /// <summary>Tooltip text for the current hover, or null.</summary>
    internal abstract string? TooltipFor(Vec local);
    /// <summary>Draw series legend chips (line charts); the base draws the frame text.</summary>
    internal virtual void DrawLegend(Painter painter, float scale) { }
    /// <summary>Extra labels under bars (bar charts).</summary>
    internal virtual void DrawBottomLabels(Painter painter) { }
    /// <summary>The x axis has numeric ticks (line charts) or bar labels (bar charts).</summary>
    internal virtual bool HasXTicks => true;

    internal void MarkConfigDirty() { ConfigDirty = true; MarkDirty(); }

    protected override void Build()
    {
        Canvas = new CanvasView(DrawChart) { FixedH = FixedH };
        Canvas.Attach(Panel, this);
        Canvas.BuildIn(Rt!);
        Measure = Draw.Text(Draw.Node("ChartMeasure", Rt!), TextStyle.Faint);
        Measure.color = new Color(1, 1, 1, 0);
        Measure.raycastTarget = false;
        Measure.textWrappingMode = TextWrappingModes.NoWrap;
        Measure.overflowMode = TextOverflowModes.Overflow;
        Draw.Place(Measure.rectTransform, 0, 0, 1, 1);
        hoverLayer = Draw.Node("ChartHover", Rt!);
        hoverLayer.gameObject.AddComponent<RectMask2D>();
        hoverLine = Draw.Child(hoverLayer, "HoverX", null, Tok.Text2);
        hoverLine.raycastTarget = false;
        hoverBox = new BoxView(hoverLayer, "HoverBar", Tok.RadiusSmall, ShapeRaster.Corners.All, ring: false);
        hoverBox.Set(Tok.ChartBarHover, Rgba.Clear);
        Draw.SetActive(hoverBox.Rt, false);
        hoverDots.Clear();
        Canvas.Invalidate();
    }

    internal override void Unbuild()
    {
        Canvas?.Unbuild(); Canvas = null; Measure = null;
        hoverLayer = null; hoverLine = null; hoverBox = null; hoverDots.Clear();
        base.Unbuild();
    }

    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        Canvas?.Arrange(new RectF(0, 0, r.W, r.H), new Vec(0, 0), RectF.Infinite);
    }

    internal override void Refresh(float now)
    {
        if (Canvas == null) return;
        bool changed = ConfigDirty; ConfigDirty = false;
        if (PollData()) changed = true;
        if (changed) { DataRevision++; Canvas.Invalidate(); }
        Canvas.Refresh(now);
        if (!Hovered) { HideHover(); return; }
        UpdateHover(Panel.LocalPointer - new Vec(Rect.X, Rect.Y));
    }

    internal override string? TooltipNow() => Hovered ? TooltipFor(Panel.LocalPointer - new Vec(Rect.X, Rect.Y)) : null;

    internal void HideHover()
    {
        if (hoverLayer != null) Draw.SetActive(hoverLayer, false);
    }

    internal bool ShowHoverLayer()
    {
        if (hoverLayer == null) return false;
        Draw.SetActive(hoverLayer, true);
        Draw.Place(hoverLayer, Plot.X, Plot.Y, Plot.W, Plot.H);
        return true;
    }

    internal void HideHoverLine()
    {
        if (hoverLine != null) Draw.SetActive(hoverLine, false);
        if (hoverBox != null) Draw.SetActive(hoverBox.Rt, false);
        for (int i = 0; i < hoverDots.Count; i++) Draw.SetActive(hoverDots[i], false);
    }

    internal Image Dot(int index, Rgba colour)
    {
        while (hoverDots.Count <= index)
        {
            var image = Draw.Child(hoverLayer!, "HoverDot" + hoverDots.Count, Assets.Circle(), Rgba.Clear);
            image.raycastTarget = false;
            hoverDots.Add(image);
        }
        var dot = hoverDots[index];
        Draw.SetActive(dot, true);
        Draw.SetColor(dot, colour);
        return dot;
    }

    internal void PlaceLine(float x, float height)
    {
        if (hoverLine == null) return;
        Draw.SetActive(hoverLine, true);
        Draw.SetColor(hoverLine, Tok.Text2);
        Draw.Place(hoverLine.rectTransform, x, 0, 1, height);
    }

    internal void PlaceHoverBox(float x, float y, float w, float h)
    {
        if (hoverBox == null) return;
        Draw.SetActive(hoverBox.Rt, true);
        hoverBox.Place(x, y, MathF.Max(1, w), MathF.Max(1, h));
    }

    internal void PlaceDot(Image dot, float x, float y)
    {
        Draw.Place(dot.rectTransform, x - HoverDot / 2, y - HoverDot / 2, HoverDot, HoverDot);
    }

    // ---------- Axis config ----------
    internal static void SetAxis(AxisDef axis, string? label, string? unit, float? min, float? max, float? tick)
    {
        axis.Label = label ?? "";
        axis.Unit = unit ?? "";
        axis.Min = min is { } a && float.IsFinite(a) ? a : null;
        axis.Max = max is { } b && float.IsFinite(b) ? b : null;
        axis.Tick = tick is { } c && float.IsFinite(c) && c > 0 ? c : null;
    }

    internal static string AxisText(AxisDef axis) => ChartMath.AxisLabel(axis.Label, axis.Unit);

    internal void AddRefLine(string? label, Func<float> y, Color? colour)
    {
        if (y == null) throw new ArgumentNullException(nameof(y));
        RefLines.Add(new RefLineDef { Label = label ?? "", GetY = y, Colour = colour is { } c ? Draw.R(c) : Tok.ChartRef });
        MarkConfigDirty();
    }

    private string TickText(AxisDef axis, float value)
    {
        if (axis.Format != null)
        {
            try { return axis.Format(value) ?? ""; }
            catch (Exception ex) { Guard.Report(Where + ".TickFormat", ex); axis.Format = null; }
        }
        return ChartMath.FormatTick(value, axis == XAxisDef ? XRange.Step : YRange.Step);
    }

    internal float MeasureText(string text) => Measure != null ? Draw.Width(Measure, text) : text.Length * 6f;

    internal string Fit(string text, float maxWidth)
    {
        if (maxWidth <= 0) return "";
        if (MeasureText(text) <= maxWidth) return text;
        for (int n = text.Length - 1; n > 0; n--)
        {
            var candidate = text.Substring(0, n) + "..";
            if (MeasureText(candidate) <= maxWidth) return candidate;
        }
        return "";
    }

    // ---------- Drawn frame ----------
    private void DrawChart(Painter painter)
    {
        chartWidth = painter.Size.x; chartHeight = painter.Size.y;
        XTickCount = HasXTicks ? ChartMath.Ticks(XRange, XTicks) : 0;
        YTickCount = ChartMath.Ticks(YRange, YTicks);
        bool legend = ShowLegend;
        LayoutPlot(legend);
        bool showFrame = HasData && Plot.W >= 20 && Plot.H >= 20;
        if (!showFrame)
        {
            HideHover();
            if (!HasData) painter.Text(chartWidth / 2, chartHeight / 2, EmptyText, Draw.C(Tok.Faint), TextStyle.Small, TextAnchor.MiddleCenter, 0, false);
            return;
        }
        float scale = painter.Raster.Width / MathF.Max(1f, painter.Size.x);
        var raster = painter.Raster;
        ChartRender.Grid(raster, scale, Plot, XRange, YRange, new ReadOnlySpan<float>(XTicks, 0, XTickCount), new ReadOnlySpan<float>(YTicks, 0, YTickCount));
        painter.Push(0, 0);
        painter.Clip(Plot.X, Plot.Y, Plot.W, Plot.H);
        DrawContent(painter, scale);
        for (int i = 0; i < RefLines.Count; i++)
        {
            var line = RefLines[i];
            if (line.HasValue) ChartRender.RefLine(raster, scale, Plot, YRange, line.Value, line.Colour);
        }
        painter.Pop();
        DrawTexts(painter);
        for (int i = 0; i < RefLines.Count; i++)
        {
            var line = RefLines[i];
            if (!line.HasValue) continue;
            float y = ChartMath.MapY(line.Value, YRange, Plot.Y, Plot.H);
            if (!float.IsFinite(y) || y < Plot.Y - .5f || y > Plot.Bottom + .5f) continue;
            painter.Text(Plot.Right - 3, y - 2, line.Label, Draw.C(line.Colour), TextStyle.Faint, TextAnchor.LowerRight, 0, false);
        }
        DrawBottomLabels(painter);
        if (legend) DrawLegend(painter, scale);
    }

    private void LayoutPlot(bool legend)
    {
        string yText = AxisText(YAxisDef);
        float top = Pad + MathF.Max(yText.Length > 0 ? AxisLabelRow : 0, legend ? LegendH : 0) + 6;
        float bottom = Pad;
        if (!HasXTicks) bottom += BarLabelRow + 4;
        else
        {
            if (XTickCount > 0) bottom += TickRow + 4;
            if (AxisText(XAxisDef).Length > 0) bottom += AxisLabelRow + 4;
        }
        float left = Pad;
        if (YTickCount > 0)
        {
            float widest = 0;
            for (int i = 0; i < YTickCount; i++) widest = MathF.Max(widest, MeasureText(TickText(YAxisDef, YTicks[i])));
            left += widest + 6;
        }
        Plot = new RectF(left, top, chartWidth - left - 10, chartHeight - top - bottom);
    }

    private void DrawTexts(Painter painter)
    {
        var tickColour = Draw.C(Tok.ChartTick);
        var labelColour = Draw.C(Tok.Text2);
        for (int i = 0; i < YTickCount; i++)
        {
            float y = ChartMath.MapY(YTicks[i], YRange, Plot.Y, Plot.H);
            if (float.IsFinite(y)) painter.Text(Plot.X - 6, y, TickText(YAxisDef, YTicks[i]), tickColour, TextStyle.Faint, TextAnchor.MiddleRight, 0, false);
        }
        for (int i = 0; i < XTickCount; i++)
        {
            float x = ChartMath.MapX(XTicks[i], XRange, Plot.X, Plot.W);
            if (float.IsFinite(x)) painter.Text(x, Plot.Bottom + 3, TickText(XAxisDef, XTicks[i]), tickColour, TextStyle.Faint, TextAnchor.UpperCenter, 0, false);
        }
        string yLabel = AxisText(YAxisDef);
        if (yLabel.Length > 0) painter.Text(Pad, Pad, yLabel, labelColour, TextStyle.Small, TextAnchor.UpperLeft, 0, false);
        string xLabel = AxisText(XAxisDef);
        if (xLabel.Length > 0)
        {
            float y = Plot.Bottom + (XTickCount > 0 ? TickRow + 6 : 6);
            painter.Text(Plot.X + Plot.W / 2, y, xLabel, labelColour, TextStyle.Small, TextAnchor.UpperCenter, 0, false);
        }
    }

    // ---------- Shared polling ----------
    internal bool PollRefLines(out float min, out float max)
    {
        min = float.MaxValue; max = float.MinValue;
        bool changed = false;
        for (int i = 0; i < RefLines.Count; i++)
        {
            var line = RefLines[i];
            float value = line.GetY == null ? float.NaN : Guard.Get(Site(3, ".RefLine"), line.GetY, float.NaN);
            if (!value.Equals(line.Value))
            {
                line.Value = value;
                changed = true;
            }
            line.HasValue = float.IsFinite(value);
            if (line.HasValue) { min = MathF.Min(min, value); max = MathF.Max(max, value); }
        }
        return changed;
    }

    internal bool ShowLegend => LegendChoice ?? SeriesCount > 1;
    internal virtual int SeriesCount => 0;
}

/// <summary>
/// Line chart (0.1.7) with one to four series, a numeric x/y axis, automatic or fixed ranges with
/// 1/2/5 ticks, dashed reference lines, a legend and hover. NaN or infinite points are skipped and
/// leave a gap; one point is drawn as a dot. The wheel goes to the surrounding ScrollView (no pan/zoom).
/// </summary>
public sealed class LineChart : ChartElement
{
    private sealed class SeriesDef
    {
        internal string Name = "";
        internal Func<IReadOnlyList<Vector2>> Get = null!;
        internal Color? Colour;
        internal SeriesStyle Style;
        internal ChartPoint[] Points = new ChartPoint[16];
        internal int Count;
        internal ChartFingerprint Fingerprint;
    }

    private readonly List<SeriesDef> series = new();
    private int tipKey;
    private string? tipText;

    internal LineChart() { }

    /// <summary>Add a series (at most four). Points are in axis units; the getter is polled every frame.</summary>
    public LineChart Series(string name, Func<IReadOnlyList<Vector2>> points, Color? colour = null, SeriesStyle style = SeriesStyle.Line)
    {
        if (points == null) throw new ArgumentNullException(nameof(points));
        if (series.Count >= 4) throw new InvalidOperationException("A LineChart supports at most four series.");
        series.Add(new SeriesDef { Name = name ?? "", Get = points, Colour = colour, Style = style });
        MarkConfigDirty();
        return this;
    }

    /// <summary>X axis caption and optional fixed range and tick step. Without a range the axis is automatic.</summary>
    public LineChart XAxis(string label, string unit = "", float? min = null, float? max = null, float? tick = null)
    {
        SetAxis(XAxisDef, label, unit, min, max, tick); MarkConfigDirty(); return this;
    }

    /// <summary>Y axis caption and optional fixed range and tick step. Without a range the axis is automatic.</summary>
    public LineChart YAxis(string label, string unit = "", float? min = null, float? max = null, float? tick = null)
    {
        SetAxis(YAxisDef, label, unit, min, max, tick); MarkConfigDirty(); return this;
    }

    /// <summary>Tick text for both axes (e.g. a time or percentage format).</summary>
    public LineChart TickFormat(Func<float, string> format) { XAxisDef.Format = format; YAxisDef.Format = format; MarkConfigDirty(); return this; }
    /// <summary>Tick text per axis.</summary>
    public LineChart TickFormat(Func<float, string> x, Func<float, string> y) { XAxisDef.Format = x; YAxisDef.Format = y; MarkConfigDirty(); return this; }

    /// <summary>Dashed horizontal line at the current y value (e.g. "RHA").</summary>
    public LineChart RefLine(string label, Func<float> y, Color? colour = null) { AddRefLine(label, y, colour); return this; }

    /// <summary>Show the series legend; default on when there is more than one series.</summary>
    public LineChart Legend(bool show = true) { LegendChoice = show; MarkConfigDirty(); return this; }

    /// <summary>Text shown when no series has a finite point.</summary>
    public LineChart Empty(string text) { EmptyText = text ?? ""; MarkConfigDirty(); return this; }

    internal override int SeriesCount => series.Count;

    internal override bool PollData()
    {
        bool changed = false;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        bool any = false;
        for (int s = 0; s < series.Count; s++)
        {
            var def = series[s];
            var list = Guard.Get<IReadOnlyList<Vector2>?>(Site(1, ".Series"), def.Get, null);
            int n = list?.Count ?? 0;
            if (n > def.Points.Length) def.Points = new ChartPoint[Math.Max(4, n)];
            for (int i = 0; i < n; i++)
            {
                var p = list![i];
                def.Points[i] = new ChartPoint(p.x, p.y);
            }
            def.Count = n;
            changed |= def.Fingerprint.Changed(list, new ReadOnlySpan<ChartPoint>(def.Points, 0, n));
            if (ChartMath.Bounds(new ReadOnlySpan<ChartPoint>(def.Points, 0, n), out float x0, out float x1, out float y0, out float y1))
            {
                any = true;
                minX = MathF.Min(minX, x0); maxX = MathF.Max(maxX, x1);
                minY = MathF.Min(minY, y0); maxY = MathF.Max(maxY, y1);
            }
        }
        changed |= PollRefLines(out float refMin, out float refMax);
        if (any && float.IsFinite(refMin)) { minY = MathF.Min(minY, refMin); maxY = MathF.Max(maxY, refMax); }
        HasData = any;
        if (!any) { minX = 0; maxX = 1; minY = 0; maxY = 1; }
        XRange = ChartMath.Range(minX, maxX, XAxisDef.Min, XAxisDef.Max, false, XAxisDef.Tick);
        YRange = ChartMath.Range(minY, maxY, YAxisDef.Min, YAxisDef.Max, false, YAxisDef.Tick);
        return changed;
    }

    internal override void DrawContent(Painter painter, float scale)
    {
        for (int i = 0; i < series.Count; i++)
        {
            var def = series[i];
            ChartRender.Series(painter.Raster, scale, Plot, XRange, YRange, new ReadOnlySpan<ChartPoint>(def.Points, 0, def.Count), def.Style, ColourOf(i));
        }
    }

    private Rgba ColourOf(int index) => series[index].Colour is { } c ? Draw.R(c) : Tok.ChartSeries(index);

    private int FirstSeries()
    {
        for (int i = 0; i < series.Count; i++)
            if (ChartMath.Bounds(new ReadOnlySpan<ChartPoint>(series[i].Points, 0, series[i].Count), out _, out _, out _, out _)) return i;
        return -1;
    }

    internal override void UpdateHover(Vec local)
    {
        if (!HasData || Plot.W <= 0 || local.X < Plot.X || local.X >= Plot.Right || local.Y < Plot.Y || local.Y >= Plot.Bottom)
        {
            HideHover();
            return;
        }
        if (!ShowHoverLayer()) return;
        HideHoverLine();
        int first = FirstSeries();
        if (first < 0) return;
        float xValue = XRange.Min + (local.X - Plot.X) / Plot.W * XRange.Span;
        int gi = ChartMath.NearestPointIndex(new ReadOnlySpan<ChartPoint>(series[first].Points, 0, series[first].Count), xValue);
        if (gi < 0) { HideHover(); return; }
        float dataX = series[first].Points[gi].X;
        float lineX = ChartMath.MapX(dataX, XRange, Plot.X, Plot.W);
        if (!float.IsFinite(lineX)) return;
        PlaceLine(lineX - Plot.X, Plot.H);
        for (int i = 0; i < series.Count; i++)
        {
            var def = series[i];
            int ni = ChartMath.NearestPointIndex(new ReadOnlySpan<ChartPoint>(def.Points, 0, def.Count), xValue);
            if (ni < 0 || !def.Points[ni].Finite) continue;
            float x = ChartMath.MapX(def.Points[ni].X, XRange, Plot.X, Plot.W);
            float y = ChartMath.MapY(def.Points[ni].Y, YRange, Plot.Y, Plot.H);
            if (!float.IsFinite(x) || !float.IsFinite(y) || y < Plot.Y || y > Plot.Bottom) continue;
            var dot = Dot(i, ColourOf(i));
            PlaceDot(dot, x - Plot.X, y - Plot.Y);
        }
    }

    internal override string? TooltipFor(Vec local)
    {
        if (!HasData || Plot.W <= 0 || local.X < Plot.X || local.X >= Plot.Right || local.Y < Plot.Y || local.Y >= Plot.Bottom) return null;
        int first = FirstSeries();
        if (first < 0) return null;
        float xValue = XRange.Min + (local.X - Plot.X) / Plot.W * XRange.Span;
        int gi = ChartMath.NearestPointIndex(new ReadOnlySpan<ChartPoint>(series[first].Points, 0, series[first].Count), xValue);
        if (gi < 0) return null;
        float dataX = series[first].Points[gi].X;
        int key = unchecked(DataRevision * 397 ^ BitConverter.SingleToInt32Bits(dataX));
        if (key == tipKey && tipText != null) return tipText;
        var text = new StringBuilder();
        string xLabel = AxisText(XAxisDef);
        if (xLabel.Length > 0) text.Append(xLabel).Append(": ").Append(ChartMath.FormatValue(dataX, XRange.Step, null));
        else text.Append("x: ").Append(ChartMath.FormatValue(dataX, XRange.Step, null));
        for (int i = 0; i < series.Count; i++)
        {
            var def = series[i];
            int ni = ChartMath.NearestPointIndex(new ReadOnlySpan<ChartPoint>(def.Points, 0, def.Count), xValue);
            if (ni < 0 || !def.Points[ni].Finite) continue;
            text.Append('\n').Append(def.Name).Append(": ").Append(ChartMath.FormatValue(def.Points[ni].Y, YRange.Step, YAxisDef.Unit));
        }
        tipText = text.ToString();
        tipKey = key;
        return tipText;
    }

    internal override void DrawLegend(Painter painter, float scale)
    {
        if (series.Count == 0) return;
        float total = 0;
        for (int i = 0; i < series.Count; i++) total += ChipWidth(series[i].Name) + (i > 0 ? 8 : 0);
        float x = MathF.Max(Plot.X, Plot.Right - total);
        float y = Pad;
        float h = LegendH;
        for (int i = 0; i < series.Count; i++)
        {
            float w = ChipWidth(series[i].Name);
            if (x + w > chartWidth - 2) break;
            ChartRender.Chip(painter.Raster, scale, x, y, w, h, Tok.ChartChip, Tok.Divider);
            painter.Raster.FillRect((x + 7) * scale, (y + h / 2 - 1.5f) * scale, 10 * scale, 3 * scale, Raster.Pack(ColourOf(i)));
            painter.Text(x + 21, y + h / 2, Fit(series[i].Name, w - 26), Draw.C(Tok.Text2), TextStyle.Faint, TextAnchor.MiddleLeft, 0, false);
            x += w + 8;
        }
    }

    private float ChipWidth(string name) => MathF.Min(180f, 32f + MeasureText(name));
}

/// <summary>
/// Bar chart (0.1.7): labelled bars on a numeric value axis, or a waterfall in which every bar is a
/// delta (negative = consumed) and the bars float from the old to the new remainder. A layer that
/// reaches 0 is clamped to 0, marked "Stopped" and ends the waterfall. Value axis, reference lines and
/// hover behave like <see cref="LineChart"/>; the wheel goes to the surrounding ScrollView.
/// </summary>
public sealed class BarChart : ChartElement
{
    private Func<IReadOnlyList<ChartBar>>? barsGetter;
    private ChartBar[] barData = new ChartBar[8];
    private int barCount;
    private object? barsReference;
    private int barsHash;
    private bool barsPrimed;
    private bool waterfall;
    private Func<float>? waterfallStart;
    private float startValue;
    private bool startPrimed;
    private string startLabel = "Start", endLabel = "Left";

    private float[] deltas = new float[8];
    private WaterfallStep[] steps = new WaterfallStep[16];
    private float[] barFrom = new float[16], barTo = new float[16];
    private bool[] barStopped = new bool[16];
    private int geomCount;
    private int tipKey;
    private string? tipText;

    internal BarChart() { }

    /// <summary>Bars from a getter, polled every frame. In a waterfall the value is a delta.</summary>
    public BarChart Bars(Func<IReadOnlyList<ChartBar>> bars)
    {
        barsGetter = bars ?? throw new ArgumentNullException(nameof(bars));
        MarkConfigDirty();
        return this;
    }

    /// <summary>
    /// Turn the bars into a waterfall. The first bar runs from 0 to <paramref name="start"/>; each bar
    /// then moves the remainder by its delta; the last bar runs from 0 to the end remainder. The bars
    /// come from <see cref="Bars"/> (label, delta, optional colour/tooltip).
    /// </summary>
    public BarChart Waterfall(Func<float> start, string startLabel = "Start", string endLabel = "Left")
    {
        waterfallStart = start ?? throw new ArgumentNullException(nameof(start));
        this.startLabel = startLabel ?? "";
        this.endLabel = endLabel ?? "";
        waterfall = true;
        MarkConfigDirty();
        return this;
    }

    /// <summary>Value axis caption and optional fixed range and tick step.</summary>
    public BarChart YAxis(string label, string unit = "", float? min = null, float? max = null, float? tick = null)
    {
        SetAxis(YAxisDef, label, unit, min, max, tick); MarkConfigDirty(); return this;
    }

    /// <summary>Tick text for the value axis.</summary>
    public BarChart TickFormat(Func<float, string> format) { YAxisDef.Format = format; MarkConfigDirty(); return this; }

    /// <summary>Dashed horizontal line at the current y value.</summary>
    public BarChart RefLine(string label, Func<float> y, Color? colour = null) { AddRefLine(label, y, colour); return this; }

    /// <summary>Text shown when there are no bars.</summary>
    public BarChart Empty(string text) { EmptyText = text ?? ""; MarkConfigDirty(); return this; }

    internal override bool HasXTicks => false;

    private static void Ensure<T>(ref T[] array, int count)
    {
        if (array.Length < count) array = new T[Math.Max(4, count)];
    }

    private int BarHash(ReadOnlySpan<ChartBar> items)
    {
        unchecked
        {
            int h = (int)2166136261;
            for (int i = 0; i < items.Length; i++)
            {
                var b = items[i];
                h = (h ^ (b.Label?.GetHashCode() ?? 0)) * 16777619;
                h = (h ^ BitConverter.SingleToInt32Bits(b.Value)) * 16777619;
                h = (h ^ (b.Tooltip?.GetHashCode() ?? 0)) * 16777619;
                h = (h ^ (b.Colour.HasValue ? 1 : 0)) * 16777619;
                if (b.Colour is { } c)
                {
                    Color32 k = c;
                    h = (h ^ (k.r << 24 | k.g << 16 | k.b << 8 | k.a)) * 16777619;
                }
            }
            return h;
        }
    }

    internal override bool PollData()
    {
        bool changed = false;
        var list = barsGetter == null ? null : Guard.Get<IReadOnlyList<ChartBar>?>(Site(1, ".Bars"), barsGetter, null);
        int n = list?.Count ?? 0;
        Ensure(ref barData, n);
        for (int i = 0; i < n; i++) barData[i] = list![i];
        int hash = BarHash(new ReadOnlySpan<ChartBar>(barData, 0, n));
        int previousCount = barCount;
        bool listChanged = !barsPrimed || !ReferenceEquals(list, barsReference) || n != previousCount || hash != barsHash;
        barCount = n;
        changed |= listChanged;
        barsReference = list; barsHash = hash; barsPrimed = true;

        float min = float.MaxValue, max = float.MinValue;
        if (waterfall)
        {
            float start = waterfallStart == null ? 0f : Guard.Get(Site(2, ".WaterfallStart"), waterfallStart, 0f);
            if (!startPrimed || !start.Equals(startValue)) { startValue = start; startPrimed = true; changed = true; }
            Ensure(ref deltas, n);
            for (int i = 0; i < n; i++) deltas[i] = float.IsFinite(barData[i].Value) ? barData[i].Value : 0f;
            Ensure(ref steps, n + 2);
            int count = ChartMath.Waterfall(startValue, new ReadOnlySpan<float>(deltas, 0, n), steps);
            Ensure(ref barFrom, count); Ensure(ref barTo, count); Ensure(ref barStopped, count);
            for (int i = 0; i < count; i++)
            {
                barFrom[i] = steps[i].From; barTo[i] = steps[i].To; barStopped[i] = steps[i].Stopped;
                min = MathF.Min(min, MathF.Min(steps[i].From, steps[i].To));
                max = MathF.Max(max, MathF.Max(steps[i].From, steps[i].To));
            }
            geomCount = count;
            HasData = true;
        }
        else
        {
            Ensure(ref barFrom, n); Ensure(ref barTo, n); Ensure(ref barStopped, n);
            for (int i = 0; i < n; i++)
            {
                float value = float.IsFinite(barData[i].Value) ? barData[i].Value : 0f;
                barFrom[i] = 0f; barTo[i] = value; barStopped[i] = false;
                min = MathF.Min(min, value); max = MathF.Max(max, value);
            }
            geomCount = n;
            HasData = n > 0;
        }
        changed |= PollRefLines(out float refMin, out float refMax);
        if (float.IsFinite(refMin)) { min = MathF.Min(min, refMin); max = MathF.Max(max, refMax); }
        if (!HasData || !float.IsFinite(min)) { min = 0; max = 1; }
        YRange = ChartMath.Range(min, max, YAxisDef.Min, YAxisDef.Max, YAxisDef.Min == null, YAxisDef.Tick);
        XRange = ChartMath.Range(0, MathF.Max(1, geomCount), null, null, false);
        return changed;
    }

    private Rgba FillOf(int index)
    {
        if (!waterfall) return barData[index].Colour is { } c ? Draw.R(c) : Tok.ChartBar;
        if (barStopped[index]) return Tok.ChartStopped;
        if (index == 0) return Tok.Accent;
        if (index == geomCount - 1) return Tok.Text2;
        int source = index - 1;
        return source >= 0 && source < barCount && barData[source].Colour is { } own ? Draw.R(own) : Tok.ChartBar;
    }

    private string LabelOf(int index)
    {
        if (!waterfall) return index >= 0 && index < barCount ? barData[index].Label ?? "" : "";
        if (index == 0) return startLabel;
        if (index == geomCount - 1) return endLabel;
        int source = index - 1;
        return source >= 0 && source < barCount ? barData[source].Label ?? "" : "";
    }

    internal override void DrawContent(Painter painter, float scale)
    {
        for (int i = 0; i < geomCount; i++)
        {
            var slot = ChartMath.BarRect(Plot, i, geomCount);
            var fill = FillOf(i);
            var border = barStopped[i] ? Tok.Error : Tok.ChartBarBorder;
            ChartRender.Bar(painter.Raster, scale, slot, Plot, YRange, barFrom[i], barTo[i], fill, border);
            if (barStopped[i])
            {
                float top = ChartMath.MapY(MathF.Max(barFrom[i], barTo[i]), YRange, Plot.Y, Plot.H);
                float bottom = ChartMath.MapY(MathF.Min(barFrom[i], barTo[i]), YRange, Plot.Y, Plot.H);
                painter.Text(slot.X + slot.W / 2, MathF.Max(top, bottom) - 4, "Stopped", Draw.C(Tok.ErrorText), TextStyle.Faint, TextAnchor.LowerCenter, 0, false);
            }
        }
    }

    internal override void DrawBottomLabels(Painter painter)
    {
        for (int i = 0; i < geomCount; i++)
        {
            var slot = ChartMath.BarRect(Plot, i, geomCount);
            var colour = barStopped[i] ? Draw.C(Tok.ChartStopped) : Draw.C(Tok.ChartTick);
            painter.Text(slot.X + slot.W / 2, Plot.Bottom + 3, Fit(LabelOf(i), slot.W + 4), colour, TextStyle.Faint, TextAnchor.UpperCenter, 0, false);
        }
    }

    internal override void UpdateHover(Vec local)
    {
        if (!HasData || geomCount == 0 || Plot.W <= 0 || local.X < Plot.X || local.X >= Plot.Right || local.Y < Plot.Y || local.Y >= Plot.Bottom)
        {
            HideHover();
            return;
        }
        int index = ChartMath.BarAt(Plot, geomCount, local.X);
        if (index < 0) { HideHover(); return; }
        if (!ShowHoverLayer()) return;
        HideHoverLine();
        float top = ChartMath.MapY(MathF.Max(barFrom[index], barTo[index]), YRange, Plot.Y, Plot.H);
        float bottom = ChartMath.MapY(MathF.Min(barFrom[index], barTo[index]), YRange, Plot.Y, Plot.H);
        if (!float.IsFinite(top) || !float.IsFinite(bottom)) { HideHover(); return; }
        var slot = ChartMath.BarRect(Plot, index, geomCount);
        PlaceHoverBox(slot.X - Plot.X, MathF.Min(top, bottom) - Plot.Y, slot.W, MathF.Abs(bottom - top));
    }

    internal override string? TooltipFor(Vec local)
    {
        if (!HasData || geomCount == 0 || Plot.W <= 0 || local.X < Plot.X || local.X >= Plot.Right || local.Y < Plot.Y || local.Y >= Plot.Bottom) return null;
        int index = ChartMath.BarAt(Plot, geomCount, local.X);
        if (index < 0) return null;
        int key = unchecked(DataRevision * 397 ^ index);
        if (key == tipKey && tipText != null) return tipText;
        var text = new StringBuilder();
        string unit = YAxisDef.Unit;
        if (!waterfall)
        {
            var bar = barData[index];
            text.Append(bar.Label).Append(": ").Append(ChartMath.FormatValue(bar.Value, YRange.Step, unit));
            if (!string.IsNullOrEmpty(bar.Tooltip)) text.Append('\n').Append(bar.Tooltip);
        }
        else if (index == 0)
        {
            text.Append(startLabel).Append(": ").Append(ChartMath.FormatValue(barTo[0], YRange.Step, unit));
        }
        else if (index == geomCount - 1)
        {
            text.Append(endLabel).Append(": ").Append(ChartMath.FormatValue(barTo[index], YRange.Step, unit));
        }
        else
        {
            int source = index - 1;
            var bar = source >= 0 && source < barCount ? barData[source] : default;
            text.Append(LabelOf(index)).Append(": ").Append(ChartMath.FormatValue(bar.Value, YRange.Step, unit));
            text.Append(" (").Append(ChartMath.FormatValue(barTo[index], YRange.Step, unit)).Append(" left)");
            if (barStopped[index]) text.Append("\nStopped at 0.");
            if (!string.IsNullOrEmpty(bar.Tooltip)) text.Append('\n').Append(bar.Tooltip);
        }
        tipText = text.ToString();
        tipKey = key;
        return tipText;
    }
}


