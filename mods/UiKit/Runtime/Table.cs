using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace SprocketUiKit;

/// <summary>
/// Per-cell look (0.1.6). <see cref="Colour"/> overrides the text colour; <see cref="Strong"/> uses
/// the bold label style and <see cref="Faint"/> the muted small style (Strong wins if both are set).
/// </summary>
public readonly struct CellStyle
{
    public Color? Colour { get; init; }
    public bool Strong { get; init; }
    public bool Faint { get; init; }

    public CellStyle(Color? colour = null, bool strong = false, bool faint = false)
    {
        Colour = colour; Strong = strong; Faint = faint;
    }
}

/// <summary>
/// Data table in the kit style (0.1.6), like <see cref="ListView{T}"/>: rows from a getter, polled
/// every frame. Columns keep their x positions at every size and Kit scale; text gets an ellipsis
/// instead of running into its neighbour. By default the table measures its full height and the
/// ScrollView around it scrolls; <see cref="MaxHeight"/> or <c>.Fill()</c> gives it a fixed header
/// and its own scrollbar (the 0.1.5 helper).
/// </summary>
public sealed class Table<T> : Element
{
    private sealed class ColumnDef
    {
        internal string Header = "";
        internal Func<T, string> Cell = null!;
        internal ColumnWidth Width;
        internal CellAlign Align;
        internal string? HeaderTooltip;
    }

    private sealed class CellCache
    {
        internal readonly TableTextCache Texts = new();
        internal float[] Widths = Array.Empty<float>();
        internal bool[] Valid = Array.Empty<bool>();

        internal void Ensure(int n)
        {
            if (Widths.Length >= n) return;
            int size = Math.Max(n, Widths.Length == 0 ? 4 : Widths.Length * 2);
            Array.Resize(ref Widths, size);
            Array.Resize(ref Valid, size);
        }
    }

    private sealed class RowView
    {
        internal BoxView Box = null!;
        internal TextMeshProUGUI[] Cells = Array.Empty<TextMeshProUGUI>();
        internal TextStyle[] Styles = Array.Empty<TextStyle>();
        internal bool[] StyleSet = Array.Empty<bool>();

        internal void Ensure(int n)
        {
            if (Cells.Length >= n) return;
            int old = Cells.Length;
            Array.Resize(ref Cells, n);
            Array.Resize(ref Styles, n);
            Array.Resize(ref StyleSet, n);
            for (int i = old; i < n; i++)
            {
                var t = Draw.Text(Draw.Node("Cell" + i, Box.Rt), TextStyle.Value);
                Cells[i] = t;
            }
        }
    }

    private readonly Func<IReadOnlyList<T>> rows;
    private readonly List<ColumnDef> columns = new();
    private readonly List<T> current = new();
    private readonly List<CellCache> caches = new();
    private readonly List<RowView> pool = new();
    private readonly List<TextMeshProUGUI> headerTexts = new();
    private Func<T, int, CellStyle>? cellStyle;
    private Func<T, string?>? rowTooltip;
    private Func<T, int, string?>? cellTooltip;
    private Func<T, bool>? selected;
    private Action<T>? onSelect;
    private bool zebra;
    private string emptyText = "Nothing here yet.";
    private float maxHeight;
    private float scroll;
    private int lastRowCount = -1;

    private RectTransform? viewport;
    private BoxView? header;
    private Image? headerLine;
    private TextMeshProUGUI? empty, measureBody, measureHeader;
    private ScrollBarHandle? bar;

    private int[] colWidths = Array.Empty<int>(), colX = Array.Empty<int>();
    private float[] headerNatural = Array.Empty<float>(), autoNatural = Array.Empty<float>();
    private int[] fillMin = Array.Empty<int>();
    private int headerH = (int)(Tok.TableHeaderPadY * 2 + Tok.TableHeaderLineH);
    private int layoutWidth = int.MinValue, layoutColumns = -1;
    private bool layoutDirty = true, autoDirty = true, headerDirty = true, barSpace;

    internal Table(Func<IReadOnlyList<T>> rows)
    {
        this.rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    /// <summary>Add a column. <paramref name="align"/> applies to the header and the cells.</summary>
    public Table<T> Column(string header, Func<T, string> cell, ColumnWidth width, CellAlign align, string? headerTooltip = null)
    {
        if (cell == null) throw new ArgumentNullException(nameof(cell));
        columns.Add(new ColumnDef { Header = header ?? "", Cell = cell, Width = width, Align = align, HeaderTooltip = headerTooltip });
        headerDirty = true; autoDirty = true; layoutDirty = true;
        MarkDirty();
        return this;
    }

    /// <summary>Per-cell colour/style; <paramref name="style"/> receives the row and the zero-based column.</summary>
    public Table<T> CellStyle(Func<T, int, CellStyle> style) { cellStyle = style; return this; }
    /// <summary>Tooltip for the whole row (shown when the cell itself has no tooltip).</summary>
    public Table<T> RowTooltip(Func<T, string?> tooltip) { rowTooltip = tooltip; return this; }
    /// <summary>Tooltip for one cell; a truncated cell falls back to its full text.</summary>
    public Table<T> CellTooltip(Func<T, int, string?> tooltip) { cellTooltip = tooltip; return this; }
    /// <summary>Alternate row backgrounds (the first row is plain).</summary>
    public Table<T> Zebra(bool on = true) { zebra = on; return this; }
    /// <summary>Called when a row is clicked; also enables the ListView-style row hover.</summary>
    public Table<T> OnSelect(Action<T> select) { onSelect = select; return this; }
    /// <summary>Highlight rows for which the getter returns true (polled every frame).</summary>
    public Table<T> Selected(Func<T, bool> isSelected) { selected = isSelected; return this; }
    /// <summary>Text shown when there are no rows.</summary>
    public Table<T> Empty(string text) { emptyText = text ?? ""; Draw.SetText(empty, emptyText); return this; }
    /// <summary>Cap the table height (header included) and scroll only the body beyond it.</summary>
    public Table<T> MaxHeight(float height) { maxHeight = MathF.Max(0, height); barSpace = false; MarkDirty(); return this; }

    private int PadX => (int)Tok.TableCellPadX;
    private int Gap => (int)Tok.TableColumnGap;

    /// <summary>Build retained Unity views when the element is first shown.</summary>
    protected override void Build()
    {
        header = new BoxView(Rt!, "TableHeader", Tok.TableRadius, ShapeRaster.Corners.All, ring: false);
        headerLine = Draw.Child(Rt!, "TableHeaderLine", null, Tok.TableHeaderLine);
        viewport = Draw.Node("TableBody", Rt!); viewport.gameObject.AddComponent<RectMask2D>();
        empty = Draw.Text(Draw.Node("Empty", viewport), TextStyle.Small, emptyText);
        measureBody = MakeMeasure("MeasureBody", TextStyle.Value, false);
        measureHeader = MakeMeasure("MeasureHeader", TextStyle.BlockTitle, true);
        bar = new ScrollBarHandle(() => scroll, v => { scroll = v; });
        bar.Attach(Panel, this); bar.BuildIn(Rt!);
        pool.Clear(); headerTexts.Clear(); caches.Clear(); current.Clear();
        colWidths = Array.Empty<int>(); colX = Array.Empty<int>();
        headerNatural = Array.Empty<float>(); autoNatural = Array.Empty<float>(); fillMin = Array.Empty<int>();
        layoutWidth = int.MinValue; layoutColumns = -1; lastRowCount = -1;
        layoutDirty = true; autoDirty = true; headerDirty = true; barSpace = false;
    }

    private TextMeshProUGUI MakeMeasure(string name, TextStyle style, bool wrap)
    {
        var t = Draw.Text(Draw.Node(name, Rt!), style);
        t.color = new Color(1, 1, 1, 0);
        t.raycastTarget = false;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        Draw.Place(t.rectTransform, 0, 0, 1, 1);
        return t;
    }

    internal override void Unbuild()
    {
        pool.Clear(); caches.Clear(); current.Clear(); headerTexts.Clear();
        viewport = null; header = null; headerLine = null; empty = null;
        measureBody = null; measureHeader = null;
        bar?.Unbuild(); bar = null;
        base.Unbuild();
    }

    private void EnsureColumnArrays()
    {
        int n = columns.Count;
        if (headerNatural.Length == n) return;
        Array.Resize(ref headerNatural, n);
        Array.Resize(ref autoNatural, n);
        Array.Resize(ref fillMin, n);
        headerDirty = true; autoDirty = true; layoutDirty = true;
    }

    private void EnsureHeaderMetrics()
    {
        if (!headerDirty) return;
        EnsureColumnArrays();
        for (int c = 0; c < columns.Count; c++)
        {
            var h = columns[c].Header;
            headerNatural[c] = MeasureHeaderText(h);
            float word = 0;
            foreach (var part in h.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                word = MathF.Max(word, MeasureHeaderText(part));
            fillMin[c] = (int)MathF.Ceiling(MathF.Max(Tok.TableMinCol, word + 2 * Tok.TableCellPadX));
        }
        headerDirty = false; autoDirty = true; layoutDirty = true;
    }

    private void EnsureAutoMetrics()
    {
        if (!autoDirty) return;
        EnsureColumnArrays();
        for (int c = 0; c < columns.Count; c++)
        {
            if (columns[c].Width.Kind != TableColumnKind.Auto) { autoNatural[c] = 0; continue; }
            float natural = headerNatural[c];
            for (int r = 0; r < current.Count && r < caches.Count; r++)
            {
                var cache = caches[r];
                if (c < cache.Valid.Length && cache.Valid[c]) natural = MathF.Max(natural, cache.Widths[c]);
            }
            autoNatural[c] = natural;
        }
        autoDirty = false; layoutDirty = true;
    }

    private float MeasureHeaderText(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return measureHeader != null ? Draw.Width(measureHeader, text) : text.Length * 7;
    }

    private float MeasureBodyText(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return measureBody != null ? Draw.Width(measureBody, text) : text.Length * 7;
    }

    private void EnsureLayout(float width)
    {
        EnsureHeaderMetrics();
        EnsureAutoMetrics();
        int w = Math.Max(0, (int)MathF.Round(width));
        if (!layoutDirty && w == layoutWidth && layoutColumns == columns.Count) return;
        int n = columns.Count;
        var specs = new TableColumnSpec[n];
        for (int c = 0; c < n; c++)
        {
            var def = columns[c];
            specs[c] = def.Width.Kind switch
            {
                TableColumnKind.Auto => TableColumnSpec.Auto((int)MathF.Ceiling(autoNatural[c] + 2 * Tok.TableCellPadX)),
                TableColumnKind.Fill => TableColumnSpec.Fill(def.Width.Value, fillMin[c]),
                _ => TableColumnSpec.Fixed(def.Width.Value)
            };
        }
        colWidths = TableLayout.Compute(w, Gap, specs);
        colX = TableLayout.Positions(colWidths, Gap);
        int lines = 1;
        for (int c = 0; c < n; c++)
        {
            int content = Math.Max(0, colWidths[c] - 2 * PadX);
            if (columns[c].Header.Length == 0) continue;
            if (content <= 0) { lines = 2; continue; }
            float h = measureHeader != null ? Draw.WrappedHeight(measureHeader, columns[c].Header, content) : Tok.TableHeaderLineH * 2;
            lines = Math.Max(lines, TableLayout.HeaderLineCount(h, Tok.TableHeaderLineH));
        }
        headerH = (int)(Tok.TableHeaderPadY * 2 + lines * Tok.TableHeaderLineH);
        layoutWidth = w; layoutColumns = n; layoutDirty = false;
    }

    private int BodyHeight => Math.Max(0, (int)MathF.Round(Rect.H) - headerH);
    private float ContentHeight => current.Count == 0 ? Tok.TableRowH : current.Count * Tok.TableRowH;
    private bool OwnScroll => maxHeight > 0 || FillHeight;

    internal override float MeasureHeight(float width)
    {
        EnsureLayout(OwnScroll && barSpace ? MathF.Max(0, width - 6) : width);
        float content = ContentHeight;
        float full = headerH + content;
        if (FillHeight) return headerH + Tok.TableRowH;
        if (maxHeight > 0) return MathF.Min(full, maxHeight);
        return full;
    }

    internal override bool Interactive => true;

    internal override void Refresh(float now)
    {
        if (viewport == null) return;
        var list = Guard.Get(Site(0, ".Rows"), rows, (IReadOnlyList<T>)Array.Empty<T>()) ?? Array.Empty<T>();
        current.Clear(); current.AddRange(list);
        EnsureColumnArrays();
        while (caches.Count < current.Count) caches.Add(new CellCache());
        bool widthChanged = false;
        for (int i = 0; i < current.Count; i++)
        {
            var cache = caches[i];
            cache.Ensure(columns.Count);
            var item = current[i];
            for (int c = 0; c < columns.Count; c++)
            {
                string text;
                try { text = columns[c].Cell(item) ?? ""; }
                catch (Exception ex) { Guard.Report(Site(1, ".Cell"), ex); text = ""; }
                if (!cache.Texts.Set(c, text)) continue;
                bool had = c < cache.Valid.Length && cache.Valid[c];
                float old = had ? cache.Widths[c] : 0;
                float measured = MeasureBodyText(text);
                cache.Widths[c] = measured; cache.Valid[c] = true;
                if (!had || MathF.Abs(measured - old) > .01f) widthChanged = true;
            }
        }
        if (widthChanged) { autoDirty = true; layoutDirty = true; MarkDirty(); }
        if (current.Count != lastRowCount) { lastRowCount = current.Count; autoDirty = true; MarkDirty(); }

        bool own = OwnScroll;
        if (!own) barSpace = false;
        int fullW = Math.Max(0, (int)MathF.Round(Rect.W));
        EnsureLayout(own && barSpace ? Math.Max(0, fullW - 6) : fullW);
        int bodyH = BodyHeight;
        bool over = own && ContentHeight > bodyH + .5f;
        // Switch the reserved bar width at most once on a change, never back and forth every frame.
        if (own && over != barSpace)
        {
            barSpace = over;
            EnsureLayout(barSpace ? Math.Max(0, fullW - 6) : fullW);
            bodyH = BodyHeight;
        }
        if (!own) scroll = 0;
        else if (ContentHeight <= bodyH + .5f) scroll = 0;
        else scroll = Math.Clamp(scroll, 0, MathF.Max(0, ContentHeight - bodyH));

        EnsureHeaderTexts();
        int tableW = layoutWidth;
        if (header != null) { header.Place(0, 0, tableW, headerH); header.Set(Tok.TableHeaderBg, Rgba.Clear); }
        if (headerLine != null) Draw.Place(headerLine.rectTransform, 0, Math.Max(0, headerH - 1), tableW, 1);
        Draw.Place(viewport, 0, headerH, tableW, bodyH);
        for (int c = 0; c < columns.Count && c < headerTexts.Count; c++)
        {
            var t = headerTexts[c];
            Draw.SetActive(t, true);
            Draw.SetText(t, columns[c].Header);
            t.alignment = AlignOption(columns[c].Align);
            int content = Math.Max(0, colWidths[c] - 2 * PadX);
            Draw.Place(t.rectTransform, colX[c] + PadX, 0, content, headerH);
        }
        for (int c = columns.Count; c < headerTexts.Count; c++) Draw.SetActive(headerTexts[c], false);

        Draw.SetActive(empty, current.Count == 0);
        if (empty != null) Draw.Place(empty.rectTransform, PadX, 0, Math.Max(0, tableW - 2 * PadX), Tok.TableRowH);

        if (bar != null)
        {
            bar.Enabled = Enabled;
            if (bar.Rt != null) Draw.Place(bar.Rt, 0, 0, Rect.W, Rect.H);
            bar.LayoutBar(new RectF(0, headerH, Rect.W, bodyH), new Vec(Rect.X, Rect.Y), own ? ContentHeight : bodyH, scroll);
            bar.Tick(now, Panel.LocalPointer, Kit.Hover == bar, Kit.Pressed == bar);
        }

        int hoverRow = onSelect != null && Hovered ? RowAtLocal(Panel.LocalPointer) : -1;
        var (first, count) = TableLayout.VisibleRows(scroll, bodyH, Tok.TableRowH, current.Count);
        while (pool.Count < count)
        {
            var v = new RowView();
            v.Box = new BoxView(viewport, "Row", Tok.TableRadius);
            pool.Add(v);
        }
        for (int s = 0; s < count; s++)
        {
            int index = first + s;
            var v = pool[s];
            v.Ensure(columns.Count);
            float y = index * Tok.TableRowH - scroll;
            Draw.SetActive(v.Box.Rt, true);
            v.Box.Place(0, y, tableW, Tok.TableRowH);
            bool sel = IsSelected(current[index]);
            var look = TableLayout.RowColours(index, sel, hoverRow == index, zebra, onSelect != null);
            v.Box.Set(look.Fill, look.Border);
            var cache = caches[index];
            for (int c = 0; c < columns.Count; c++)
            {
                var t = v.Cells[c];
                string text = cache.Texts.Get(c);
                float natural = c < cache.Widths.Length ? cache.Widths[c] : 0;
                int content = Math.Max(0, colWidths[c] - 2 * PadX);
                bool truncated = TableLayout.Truncates(natural, content);
                Draw.SetText(t, text);
                Draw.SetActive(t, text.Length > 0);
                if (text.Length == 0) continue;
                var st = GetCellStyle(current[index], c);
                var ts = st.Strong ? TextStyle.Label : st.Faint ? TextStyle.Faint : TextStyle.Value;
                if (!v.StyleSet[c] || v.Styles[c] != ts) { Draw.ApplyStyle(t, ts); v.Styles[c] = ts; v.StyleSet[c] = true; }
                Draw.SetColor(t, st.Colour is { } colour ? Draw.R(colour) : Draw.Style(ts).Color);
                if (truncated)
                {
                    Draw.Place(t.rectTransform, colX[c] + PadX, 0, content, Tok.TableRowH);
                    t.alignment = AlignOption(columns[c].Align);
                }
                else
                {
                    int tw = Math.Min((int)MathF.Ceiling(natural), content);
                    int off = TableLayout.AlignOffset(colWidths[c], tw, PadX, columns[c].Align);
                    Draw.Place(t.rectTransform, colX[c] + off, 0, tw, Tok.TableRowH);
                    t.alignment = TextAlignmentOptions.MidlineLeft;
                }
            }
        }
        for (int s = count; s < pool.Count; s++) Draw.SetActive(pool[s].Box.Rt, false);
    }

    private void EnsureHeaderTexts()
    {
        while (headerTexts.Count < columns.Count)
        {
            var t = Draw.Text(Draw.Node("Header" + headerTexts.Count, Rt!), TextStyle.BlockTitle);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.maxVisibleLines = 2;
            headerTexts.Add(t);
        }
    }

    private static TextAlignmentOptions AlignOption(CellAlign align) => align switch
    {
        CellAlign.Right => TextAlignmentOptions.MidlineRight,
        CellAlign.Centre => TextAlignmentOptions.Midline,
        _ => TextAlignmentOptions.MidlineLeft
    };

    private bool IsSelected(T item)
    {
        if (selected == null) return false;
        try { return selected(item); }
        catch (Exception ex) { Guard.Report(Site(2, ".Selected"), ex); return false; }
    }

    private CellStyle GetCellStyle(T item, int column)
    {
        if (cellStyle == null) return default;
        try { return cellStyle(item, column); }
        catch (Exception ex) { Guard.Report(Site(3, ".CellStyle"), ex); return default; }
    }

    private string? CellTooltipNow(T item, int column)
    {
        if (cellTooltip == null) return null;
        try { return cellTooltip(item, column); }
        catch (Exception ex) { Guard.Report(Site(3, ".CellTooltip"), ex); return null; }
    }

    private string? RowTooltipNow(T item)
    {
        if (rowTooltip == null) return null;
        try { return rowTooltip(item); }
        catch (Exception ex) { Guard.Report(Site(3, ".RowTooltip"), ex); return null; }
    }

    private int RowAtLocal(Vec p)
    {
        float y = p.Y - Rect.Y;
        if (y < headerH || y >= headerH + BodyHeight) return -1;
        return TableLayout.RowAt(y - headerH, scroll, Tok.TableRowH, current.Count);
    }

    private int ColumnAt(float x)
    {
        for (int c = 0; c < columns.Count && c < colX.Length; c++)
            if (x >= colX[c] && x < colX[c] + colWidths[c]) return c;
        return -1;
    }

    internal override string? TooltipNow()
    {
        Vec p = Panel != null ? Panel.LocalPointer : default;
        float x = p.X - Rect.X, y = p.Y - Rect.Y;
        if (y >= 0 && y < headerH)
        {
            int c = ColumnAt(x);
            return c >= 0 && !string.IsNullOrEmpty(columns[c].HeaderTooltip) ? columns[c].HeaderTooltip : null;
        }
        int row = RowAtLocal(p);
        if (row < 0 || row >= current.Count) return null;
        int col = ColumnAt(x);
        if (col < 0 || col >= columns.Count) return RowTooltipNow(current[row]);
        var custom = CellTooltipNow(current[row], col);
        if (!string.IsNullOrEmpty(custom)) return custom;
        var cache = caches[row];
        int content = Math.Max(0, colWidths[col] - 2 * PadX);
        if (col < cache.Widths.Length && TableLayout.Truncates(cache.Widths[col], content))
        {
            var text = cache.Texts.Get(col);
            if (text.Length > 0) return text;
        }
        return RowTooltipNow(current[row]);
    }

    internal override Element? HitTest(Vec p)
    {
        if (!Visible || !Clip.Contains(p) || !Rect.Contains(p)) return null;
        if (bar != null && bar.HitTest(p) != null) return bar;
        return this;
    }

    internal override void OnClick(Vec p)
    {
        if (onSelect == null) return;
        int row = RowAtLocal(p);
        if (row < 0 || row >= current.Count) return;
        var item = current[row];
        Guard.Run(Where + ".Select", () => onSelect(item));
    }

    internal override bool OnScroll(float notches, Vec p)
    {
        if (!OwnScroll) return false;
        int bodyH = BodyHeight;
        float max = MathF.Max(0, ContentHeight - bodyH);
        if (max <= .5f) return false;
        scroll = Math.Clamp(scroll - notches * 40, 0, max);
        return true;
    }
}
