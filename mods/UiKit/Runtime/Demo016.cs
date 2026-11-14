namespace SprocketUiKit;

/// <summary>Demo tab for the 0.1.6 table: protection columns, a scrolling 60-row table and a table in a ScrollView.</summary>
internal static class Demo016
{
    internal const int FirstTab = Demo014.ScrollTab + 1;
    internal static readonly string[] Tabs = { "Table" };

    private sealed class ProtectionRow { public string Threat = "", Note = ""; public int Rha; public double Thickness, Mass; }
    private sealed class ManyRow { public int Index; public string Name = "", Class = ""; public int Value; }
    private sealed class ScrollRow { public int Line; public string Name = "", Value = ""; }

    private static readonly List<ProtectionRow> protection = new()
    {
        new() { Threat = "KE", Rha = 150, Thickness = 1.00, Mass = 1.00, Note = "Kinetic energy: long rod and AP shot." },
        new() { Threat = "HEAT", Rha = 95, Thickness = .75, Mass = .85, Note = "Chemical energy: shaped charge jet." },
    };
    private static readonly List<ManyRow> many = Enumerable.Range(1, 60)
        .Select(i => new ManyRow { Index = i, Name = "Composer row " + i, Class = i % 3 == 0 ? "HEAT" : i % 2 == 0 ? "KE" : "HESH", Value = 40 + i })
        .ToList();
    private static readonly List<ScrollRow> scrollRows = Enumerable.Range(1, 14)
        .Select(i => new ScrollRow { Line = i, Name = "ScrollView content line " + i, Value = i % 2 == 0 ? "even" : "odd" })
        .ToList();
    private static ManyRow? selected;

    internal static void Build(Panel p, Func<int> tab, Action<string> log)
    {
        var c = p.Columns(0, 0);
        c[0].Parent!.VisibleWhen(() => tab() == FirstTab);
        c[1].Parent!.VisibleWhen(() => tab() == FirstTab);

        var protectionSection = c[0].Section("Protection table");
        protectionSection.Label("Resize the panel: columns stay aligned.", TextStyle.Value);
        protectionSection.Table(() => protection)
            .Column("Threat", r => r.Threat, ColumnWidth.Fill(1), CellAlign.Left, "The projectile class this row describes.")
            .Column("Protection (mm RHA)", r => r.Rha.ToString(), ColumnWidth.Fixed(160), CellAlign.Right, "Equivalent homogeneous armour at the given angle.")
            .Column("Factor on plate thickness", r => r.Thickness.ToString("0.00"), ColumnWidth.Fixed(130), CellAlign.Right, "Protection multiplier applied to the plate thickness.")
            .Column("Factor on mass", r => r.Mass.ToString("0.00"), ColumnWidth.Fill(1.2f), CellAlign.Right)
            .CellStyle((r, col) => col == 1 && r.Threat == "HEAT" ? new CellStyle(UiKit.Colour("#e07a4f"))
                : col == 1 && r.Threat == "KE" ? new CellStyle(strong: true) : default)
            .RowTooltip(r => r.Note)
            .Empty("No threats in this vehicle.");

        var manySection = c[0].Section("60 rows, MaxHeight, zebra and selection");
        manySection.Label("Wheel, drag the bar or click the track. Only visible rows are active.", TextStyle.Small);
        manySection.Table(() => many)
            .Column("Row", r => r.Index.ToString(), ColumnWidth.Fixed(56), CellAlign.Right)
            .Column("Name", r => r.Name, ColumnWidth.Auto, CellAlign.Left)
            .Column("Class", r => r.Class, ColumnWidth.Fill(1), CellAlign.Left)
            .Column("Value", r => r.Value.ToString(), ColumnWidth.Fixed(72), CellAlign.Right)
            .MaxHeight(224)
            .Zebra()
            .OnSelect(r => { selected = r; log("table row " + r.Index); })
            .Selected(r => r == selected)
            .Empty("No rows.");
        manySection.Label(() => selected == null ? "Click a row to select it." : "Selected: " + selected.Name, TextStyle.Small);

        var scrollSection = c[1].Section("Table inside a ScrollView");
        scrollSection.Label("The table measures its full height; the outer ScrollView owns the wheel and scrolls it. No inner bar appears.", TextStyle.Small).Wrap();
        var sv = scrollSection.ScrollView().Height(230);
        sv.Label("Content above the table.", TextStyle.Faint);
        sv.Table(() => scrollRows)
            .Column("Line", r => r.Line.ToString(), ColumnWidth.Fixed(50), CellAlign.Right)
            .Column("Description", r => r.Name, ColumnWidth.Fill(1), CellAlign.Left)
            .Column("Parity", r => r.Value, ColumnWidth.Auto, CellAlign.Right)
            .Zebra()
            .Empty("No scroll rows.");
        sv.Label("Content below the table; the wheel still scrolls the outer view.", TextStyle.Faint).Wrap();
        scrollSection.Label("A table with .MaxHeight or .Fill() scrolls its own body and keeps the header in place.", TextStyle.Small).Wrap();
    }
}
