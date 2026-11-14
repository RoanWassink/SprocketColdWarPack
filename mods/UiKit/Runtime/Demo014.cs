namespace SprocketUiKit;

/// <summary>Demo tabs for the 0.1.4 window features and the 0.1.5 draggable scrollbar.</summary>
internal static class Demo014
{
    internal const int FirstTab = Demo012.FirstTab + 6; // after the six 0.1.2 tabs
    internal const int ScrollTab = FirstTab + 1;
    internal static readonly string[] Tabs = { "Window", "Scroll" };
    internal const float MinW = 1000, MinH = 560;
    private static Panel? fixedPanel;
    private static int resizedCount;
    private static int shortcutCount;
    private static bool refuseClose;
    private static string lastRequest = "-";

    private sealed class ScrollDemoRow { public string Title = "", Sub = ""; public bool On; }
    private static readonly List<ScrollDemoRow> scrollRows = Enumerable.Range(1, 30)
        .Select(i => new ScrollDemoRow { Title = "Composer row " + i, Sub = i % 2 == 0 ? "event" : "level", On = i % 4 == 0 }).ToList();
    private static readonly string[] longOptions = Enumerable.Range(1, 24)
        .Select(i => "Option " + i + " - " + (i % 3 == 0 ? "a longer label for the open list" : "value")).ToArray();
    private static int scrollChoice, scrollSelected, scrollRemoved;

    internal static void Build(Panel p, Func<int> tab, Action<string> log)
    {
        p.MinSize(MinW, MinH);
        p.Resized += () => resizedCount++;
        p.OnCloseRequest = () => { if (refuseClose) log("close refused (demo switch)"); return !refuseClose; };
        var c = p.Columns(0, 0);
        c[0].Parent!.VisibleWhen(() => tab() == FirstTab);
        var s = c[0].Section("Maximize and resize");
        s.Label("Maximize with the button left of the close button or a double-click on the title bar; drag the title bar of a maximized panel to restore it under the cursor. Resize with the right edge, the bottom edge or the grip in the bottom-right corner. Size and maximized state are remembered.", TextStyle.Small).Wrap();
        s.FieldRow("Keyboard focus").Label(() => UiKit.FocusedPanel?.Id ?? "none", TextStyle.Value);
        s.FieldRow("Demo HasFocus").Label(() => p.HasFocus.ToString(), TextStyle.Value);
        p.AddShortcut("Ctrl+Shift+D", () => shortcutCount++);
        s.FieldRow("Ctrl+Shift+D count").Label(() => shortcutCount.ToString(), TextStyle.Value);
        s.Label("Press Ctrl+Shift+D with demo focus: count increases. Click the designer and press again: count stays unchanged.", TextStyle.Small).Wrap();
        s.FieldRow("Disabled game actions").Label(() => KeyboardGuard.Suspended.ToString(), TextStyle.Value);
        var names = new List<string> { "One", "Two" }; int selectedName=0;
        s.Tabs(() => names, () => selectedName, i => selectedName=i);
        s.TextField(() => names[selectedName], value => names[selectedName]=value, "Rename selected tab");
        var tabTools=s.Row(8);
        tabTools.Button("Add tab", () => names.Add("Tab " + (names.Count+1)));
        tabTools.Button("Remove tab", () => { if(names.Count>1) { names.RemoveAt(selectedName); selectedName=Math.Min(selectedName,names.Count-1); } });
        s.FieldRow("Size now").Label(() => $"{p.Width:0} x {p.Height:0}{(p.IsMaximized ? " (maximized)" : "")}", TextStyle.Value);
        s.FieldRow("MinSize of this panel").Label($"{MinW:0} x {MinH:0} (created at 1240 x 640)", TextStyle.Value);
        s.FieldRow("Resized events").Label(() => resizedCount.ToString(System.Globalization.CultureInfo.InvariantCulture), TextStyle.Value);
        var r = s.Row(8);
        r.Button("Maximize / restore", () => { p.ToggleMaximize(); log("toggle maximize -> " + p.IsMaximized); });
        r.Button("Reset position", () => { p.ResetPosition(); log("reset position"); });
        var o = c[1].Section("Closing and other panels");
        o.FieldRow("Refuse close requests (OnCloseRequest)").Toggle(() => refuseClose, v => { refuseClose = v; log("refuse close " + v); });
        var rc = o.Row(8);
        rc.Button("RequestClose()", () => { bool closed = p.RequestClose(); lastRequest = closed ? "true (closed)" : "false (refused)"; log("RequestClose -> " + lastRequest); }, ButtonStyle.Danger);
        rc.Label(() => "last result: " + lastRequest, TextStyle.Small);
        o.Label("RequestClose goes through Closable and OnCloseRequest, like Esc and the close button. With the switch on, Esc and the close button are refused too.", TextStyle.Small).Wrap();
        o.Button("Open a panel with Maximizable = false", () => { EnsureFixed(); fixedPanel!.Open(); log("fixed panel"); });
        BuildScroll(p, tab, log);
    }

    private static void BuildScroll(Panel p, Func<int> tab, Action<string> log)
    {
        var c = p.Columns(0, 0, 0);
        c[0].Parent!.VisibleWhen(() => tab() == ScrollTab);
        var d = c[0].Section("Long dropdown");
        d.Label("The open list scrolls with the wheel and has a draggable bar. Dragging or paging it keeps the list open and chooses nothing; a click on an option works as before.", TextStyle.Small).Wrap();
        d.Labeled("24 options").Dropdown(longOptions, () => scrollChoice, i => { scrollChoice = i; log("scroll dropdown " + (i + 1)); });
        d.Label("The thumb follows the mouse while dragged. Click the track above or below it to page one viewport; hold to repeat until the thumb is under the cursor.", TextStyle.Small).Wrap();

        var l = c[1].Section("ListView", () => scrollRows.Count);
        l.List(() => scrollRows, (r, row) =>
        {
            row.Title = r.Title; row.Subtitle = r.Sub; row.Tag = r.On ? "ON" : "OFF"; row.Lamp = r.On; row.Removable = true; row.Key = r.Title;
        }).Height(300)
          .OnSelect(r => { scrollSelected++; log("scroll list select " + r.Title); })
          .OnRemove(r => { scrollRows.Remove(r); scrollRemoved++; log("scroll list remove " + r.Title); });
        l.FieldRow("Selected / removed").Label(() => scrollSelected + " / " + scrollRemoved, TextStyle.Value);
        l.Label("Click a row to select and the bin to remove. A click on the bar does neither.", TextStyle.Small).Wrap();

        var sc = c[2].Section("ScrollView (right column)");
        var sv = sc.ScrollView().Height(300);
        for (int i = 1; i <= 30; i++) sv.Label("Scrollable line " + i + " - drag the thumb, click the track or use the wheel.", TextStyle.Body).Wrap();
        sc.Label("This is the rightmost column: the bar sits inside the column padding, clear of the panel's 4 px resize band. Maximized, the same holds; resizing from just outside the right edge still works.", TextStyle.Small).Wrap();
    }
    private static void EnsureFixed()
    {
        if (fixedPanel != null) return;
        fixedPanel = UiKit.CreatePanel(UiKit.PluginGuid, "demo-fixed", "Not maximizable", "resize only", 460, 260);
        fixedPanel.AllowedContexts = UiContexts.All;
        fixedPanel.Maximizable = false;
        var c = fixedPanel.Columns(0)[0];
        var s = c.Section("Maximizable = false");
        s.Label("No maximize button and a double-click on the title bar does nothing. The edges and the corner grip still resize (minimum: the size at creation, 460 x 260).", TextStyle.Small).Wrap();
        c.Footer().Button("Close", () => fixedPanel.RequestClose());
    }
    internal static void Close() => fixedPanel?.Close();
}
