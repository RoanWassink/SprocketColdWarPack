namespace SprocketUiKit;

/// <summary>Horizontal alignment of a table cell (and its column header).</summary>
/// <summary>Right UI data and behavior.</summary>
public enum CellAlign { Left, Centre, Right }

/// <summary>
/// Width of one table column: <see cref="Fixed"/> in reference px, <see cref="Auto"/> around the
/// widest header/cell text, or <see cref="Fill"/> of the leftover width with a weight.
/// </summary>
public readonly struct ColumnWidth
{
    internal readonly TableColumnKind Kind;
    internal readonly float Value;

    internal ColumnWidth(TableColumnKind kind, float value) { Kind = kind; Value = value; }

    /// <summary>Exact width in reference px (a non-finite or tiny value is treated as 1).</summary>
    public static ColumnWidth Fixed(float pixels) => new(TableColumnKind.Fixed, MathF.Max(1f, float.IsFinite(pixels) ? pixels : 1f));
    /// <summary>As wide as the widest of the header and the cells, plus the cell padding.</summary>
    public static ColumnWidth Auto => new(TableColumnKind.Auto, 0);
    /// <summary>Share of the leftover width; weights of 2:1 give twice the width (default weight 1).</summary>
    public static ColumnWidth Fill(float weight = 1) => new(TableColumnKind.Fill, MathF.Max(.01f, float.IsFinite(weight) ? weight : 1f));
}

internal enum TableColumnKind { Fixed, Auto, Fill }

/// <summary>One column at layout time: a fixed width, a measured auto width, or a fill weight and minimum.</summary>
internal readonly struct TableColumnSpec
{
    internal readonly TableColumnKind Kind;
    internal readonly float Size;
    internal readonly int AutoWidth, MinWidth;

    private TableColumnSpec(TableColumnKind kind, float size, int autoWidth, int minWidth) { Kind = kind; Size = size; AutoWidth = autoWidth; MinWidth = minWidth; }
    internal static TableColumnSpec Fixed(float pixels) => new(TableColumnKind.Fixed, pixels, 0, 0);
    internal static TableColumnSpec Auto(int width) => new(TableColumnKind.Auto, 0, Math.Max(0, width), 0);
    internal static TableColumnSpec Fill(float weight, int minWidth) => new(TableColumnKind.Fill, MathF.Max(.01f, weight), 0, Math.Max(0, minWidth));
}

/// <summary>
/// Pure table geometry (0.1.6): whole-pixel column widths and x positions, shrink order
/// (Fill to its minimum, then Auto proportionally, Fixed never), alignment offsets, row indexing,
/// zebra/selection colours and the no-allocation text cache.
/// </summary>
internal static class TableLayout
{
    /// <summary>
    /// Column widths in whole pixels. The sum of the widths plus the gaps equals the rounded
    /// available width whenever the fixed columns and fill minima leave room; fixed columns are
    /// never shrunk, so they may overflow a very narrow table.
    /// </summary>
    internal static int[] Compute(float width, int gap, IReadOnlyList<TableColumnSpec> columns)
    {
        int n = columns.Count;
        var widths = new int[n];
        if (n == 0) return widths;
        int available = Math.Max(0, (int)MathF.Round(width));
        int gapTotal = Math.Max(0, n - 1) * Math.Max(0, gap);
        int space = Math.Max(0, available - gapTotal);
        int fixedSum = 0, autoSum = 0, fillMinSum = 0, autos = 0, fills = 0;
        for (int i = 0; i < n; i++)
        {
            var c = columns[i];
            switch (c.Kind)
            {
                case TableColumnKind.Fixed:
                    widths[i] = Math.Max(1, (int)MathF.Round(c.Size));
                    fixedSum += widths[i];
                    break;
                case TableColumnKind.Auto:
                    widths[i] = Math.Max(0, c.AutoWidth);
                    autoSum += widths[i]; autos++;
                    break;
                default:
                    widths[i] = Math.Max(0, c.MinWidth);
                    fillMinSum += widths[i]; fills++;
                    break;
            }
        }
        if (fills > 0)
        {
            int rest = space - fixedSum - autoSum;
            if (rest < fillMinSum)
            {
                // Step 1: the fill columns are already at their minimum. Step 2: shrink auto proportionally.
                int reduce = fillMinSum - rest;
                int target = Math.Max(0, autoSum - reduce);
                var natural = new float[autos];
                int a = 0;
                for (int i = 0; i < n; i++) if (columns[i].Kind == TableColumnKind.Auto) natural[a++] = widths[i];
                var shrunk = Allocate(target, natural);
                a = 0;
                for (int i = 0; i < n; i++) if (columns[i].Kind == TableColumnKind.Auto) widths[i] = shrunk[a++];
            }
            else
            {
                var weights = new float[fills];
                var mins = new int[fills];
                int f = 0;
                for (int i = 0; i < n; i++) if (columns[i].Kind == TableColumnKind.Fill) { weights[f] = columns[i].Size; mins[f++] = widths[i]; }
                var distributed = AllocateWithMin(rest, weights, mins);
                f = 0;
                for (int i = 0; i < n; i++) if (columns[i].Kind == TableColumnKind.Fill) widths[i] = distributed[f++];
            }
            return widths;
        }
        // No fill: auto columns absorb the leftover (or shrink when too narrow); fixed stay exact.
        if (autos > 0)
        {
            int target = Math.Max(0, space - fixedSum);
            var natural = new float[autos];
            int a = 0;
            for (int i = 0; i < n; i++) if (columns[i].Kind == TableColumnKind.Auto) natural[a++] = widths[i];
            var fitted = Allocate(target, natural);
            a = 0;
            for (int i = 0; i < n; i++) if (columns[i].Kind == TableColumnKind.Auto) widths[i] = fitted[a++];
        }
        return widths;
    }

    /// <summary>x offset of each column relative to the table's left edge (whole pixels).</summary>
    internal static int[] Positions(IReadOnlyList<int> widths, int gap)
    {
        var x = new int[widths.Count];
        int at = 0;
        for (int i = 0; i < widths.Count; i++) { x[i] = at; at += widths[i] + gap; }
        return x;
    }

    /// <summary>x offset of a text of <paramref name="textWidth"/> inside a column cell.</summary>
    internal static int AlignOffset(int columnWidth, int textWidth, int padding, CellAlign align)
    {
        int content = Math.Max(0, columnWidth - 2 * Math.Max(0, padding));
        int tw = Math.Clamp(textWidth, 0, content);
        return align switch
        {
            CellAlign.Right => Math.Max(0, padding) + content - tw,
            CellAlign.Centre => Math.Max(0, padding) + (content - tw) / 2,
            _ => Math.Max(0, padding)
        };
    }

    /// <summary>True when <paramref name="textWidth"/> does not fit the cell's content width (needs an ellipsis).</summary>
    internal static bool Truncates(float textWidth, int contentWidth) => textWidth > contentWidth + .5f;

    /// <summary>Header lines needed for a wrapped height, capped at two; every column shares that height.</summary>
    internal static int HeaderLineCount(float wrappedHeight, float lineHeight)
    {
        if (lineHeight <= 0) return 1;
        return Math.Clamp((int)MathF.Round(MathF.Max(0, wrappedHeight) / lineHeight), 1, 2);
    }

    /// <summary>Row index under a body-local y (0 = the body's top), or -1 outside the rows.</summary>
    internal static int RowAt(float bodyY, float scroll, float rowHeight, int total)
    {
        if (total <= 0 || rowHeight <= 0 || bodyY < 0) return -1;
        int index = (int)MathF.Floor((bodyY + MathF.Max(0, scroll)) / rowHeight);
        return index >= 0 && index < total ? index : -1;
    }

    /// <summary>Rows that intersect a viewport; used to keep only visible rows pooled and active.</summary>
    internal static (int First, int Count) VisibleRows(float scroll, float viewport, float rowHeight, int total)
    {
        if (total <= 0 || rowHeight <= 0 || viewport <= 0) return (0, 0);
        int first = Math.Clamp((int)MathF.Floor(MathF.Max(0, scroll) / rowHeight), 0, total - 1);
        int last = Math.Clamp((int)MathF.Floor((MathF.Max(0, scroll) + viewport - .001f) / rowHeight), first, total - 1);
        return (first, last - first + 1);
    }

    /// <summary>Zebra striping: the first row is plain, the second striped, and so on.</summary>
    internal static bool Zebra(int index) => (index & 1) == 1;

    /// <summary>Row colours in the ListView look; selection wins over hover, hover only when clickable.</summary>
    internal static (Rgba Fill, Rgba Border) RowColours(int index, bool selected, bool hover, bool zebra, bool clickable)
    {
        if (selected) return (Tok.Selected, Tok.Accent);
        if (hover && clickable) return (Tok.Btn, Tok.BorderHover);
        return zebra && Zebra(index) ? (Tok.TableZebra, Rgba.Clear) : (Tok.TableRowBg, Rgba.Clear);
    }

    /// <summary>
    /// Distribute <paramref name="total"/> over <paramref name="weights"/> (whole pixels, sum exact).
    /// Zero weights share equally; a zero total gives zeros.
    /// </summary>
    internal static int[] Allocate(int total, IReadOnlyList<float> weights)
    {
        int n = weights.Count;
        var result = new int[n];
        if (n == 0 || total <= 0) return result;
        double sum = 0;
        for (int i = 0; i < n; i++) if (weights[i] > 0) sum += weights[i];
        double used = 0;
        var fraction = new double[n];
        for (int i = 0; i < n; i++)
        {
            double w = sum > 0 ? Math.Max(0, weights[i]) : 1;
            double exact = total * w / (sum > 0 ? sum : n);
            int floor = (int)Math.Floor(exact);
            result[i] = floor;
            fraction[i] = exact - floor;
            used += floor;
        }
        int remainder = total - (int)used;
        while (remainder > 0)
        {
            int best = 0;
            for (int i = 1; i < n; i++) if (fraction[i] > fraction[best]) best = i;
            result[best]++;
            fraction[best] -= 1;
            remainder--;
        }
        return result;
    }

    /// <summary>
    /// Distribute <paramref name="total"/> by weight with a lower bound per item. When the total is
    /// below the sum of the minima the minima are returned (the result overflows, as fill minimums do).
    /// </summary>
    internal static int[] AllocateWithMin(int total, IReadOnlyList<float> weights, IReadOnlyList<int> mins)
    {
        int n = weights.Count;
        var result = new int[n];
        if (n == 0) return result;
        var active = new bool[n];
        int minSum = 0;
        for (int i = 0; i < n; i++) { active[i] = true; minSum += Math.Max(0, mins[i]); }
        if (total <= minSum)
        {
            for (int i = 0; i < n; i++) result[i] = Math.Max(0, mins[i]);
            return result;
        }
        int remaining = total;
        while (true)
        {
            double weightSum = 0;
            for (int i = 0; i < n; i++) if (active[i]) weightSum += Math.Max(0, weights[i]);
            bool fixedOne = false;
            for (int i = 0; i < n; i++)
            {
                if (!active[i]) continue;
                double share = remaining * (weightSum > 0 ? Math.Max(0, weights[i]) / weightSum : 1.0 / active.Count(x => x));
                if (share < Math.Max(0, mins[i]) - 1e-9)
                {
                    result[i] = Math.Max(0, mins[i]); remaining -= result[i]; active[i] = false; fixedOne = true;
                }
            }
            if (fixedOne) continue;
            var activeWeights = new float[n];
            for (int i = 0; i < n; i++) activeWeights[i] = active[i] ? Math.Max(0, weights[i]) : 0;
            var shareResult = Allocate(remaining, activeWeights);
            for (int i = 0; i < n; i++) if (active[i]) result[i] = shareResult[i];
            // Integer rounding can drop an active column one pixel below its minimum.
            for (int i = 0; i < n; i++)
            {
                int min = Math.Max(0, mins[i]);
                if (active[i] && result[i] < min)
                {
                    int need = min - result[i]; result[i] = min;
                    for (int j = 0; j < n && need > 0; j++)
                    {
                        if (!active[j] || j == i || result[j] <= Math.Max(0, mins[j])) continue;
                        int take = Math.Min(need, result[j] - Math.Max(0, mins[j]));
                        result[j] -= take; need -= take;
                    }
                }
            }
            return result;
        }
    }
}

/// <summary>
/// Per-cell text strings with change detection. Unchanged text (even another string instance with
/// the same contents) allocates nothing, so the table can poll its getters every frame.
/// </summary>
internal sealed class TableTextCache
{
    private string?[] texts = Array.Empty<string?>();
    internal int Length => texts.Length;

    internal string Get(int index) => index >= 0 && index < texts.Length ? texts[index] ?? "" : "";

    /// <summary>Set the text; true when it differs from the cached value (then the width must be measured again).</summary>
    internal bool Set(int index, string? text)
    {
        if (index < 0) return false;
        if (index >= texts.Length) Array.Resize(ref texts, Math.Max(index + 1, texts.Length == 0 ? 8 : texts.Length * 2));
        text ??= "";
        if (string.Equals(texts[index], text, StringComparison.Ordinal)) return false;
        texts[index] = text;
        return true;
    }
}
