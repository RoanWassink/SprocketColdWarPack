using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using SprocketTelescopicMast;
using SprocketTelescopicMast.Io;
using SprocketUiKit;
using UnityEngine;

/// <summary>0.9.2: reliable wires, tabs, free placement, undo/redo and the "I/O Flow" name.</summary>
internal static class Tests092
{
    private static int checks;
    private static readonly List<string> report = new();
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("0.9.2: " + why); }
    private static readonly FlowPart Flap = new("flap", "hinge", "Shell flap"), Flap2 = new("flap2", "hinge", "Rear flap");
    private static readonly FlowPart Plate = new("plate", "rotator", "Radar"), Lamp = new("lamp", "effects.light", "Brake light", "Effects");
    private static void Register()
    {
        IoRegistry.ClearForTests();
        IoRegistry.RegisterInput(new CannonFiredInput()); IoRegistry.RegisterInput(new CannonReloadedInput());
        IoRegistry.RegisterInput(new CannonReloadingInput()); IoRegistry.RegisterInput(new CannonLoadedInput());
        IoRegistry.RegisterInput(new KeyInput()); IoRegistry.RegisterInput(new VehicleBrakingInput());
    }
    private static string NoRev(string json) => Regex.Replace(json, "\"rev\":\\d+", "\"rev\":0");
    public static int Run()
    {
        Register();
        Replace(); Snap(); Reasons(); Tabs(); Migration(); RoundTrip(); Placement(); UndoRedo(); UndoLimit();
        Panel(); Name();
        Directory.CreateDirectory("logs");
        File.WriteAllLines("logs/tests-0.9.2-detail.txt", report);
        return checks;
    }

    private static void Replace()
    {
        var e = new FlowEditor(new()); int saves = 0; e.Saved = () => saves++;
        var a = e.Place("and", 0, 0)!; var b = e.Place("or", 0, 200)!; var hinge = e.Place("hinge", 400, 0, Flap)!;
        e.ClickPort(new(a.Id, 0, true)); Check(e.ClickPort(new(hinge.Id, 0, false)) && e.Notice == null, "first wire into a free input");
        long rev = e.Flow.Rev; int before = saves;
        e.ClickPort(new(b.Id, 0, true)); Check(e.ClickPort(new(hinge.Id, 0, false)), "drop on an occupied input connects");
        Check(e.Flow.Wires.Count(w => w.To == hinge.Id) == 1 && e.Flow.Wires.Single(w => w.To == hinge.Id).From == b.Id, "the new wire replaced the old driver");
        Check(e.Flow.Rev == rev + 1 && saves == before + 1, "replacement is one revision and one save");
        Check(e.Notice != null && e.Notice.StartsWith("Replaced the old wire", StringComparison.Ordinal), "status says the old wire was replaced");
        Check(FlowValidation.Validate(e.Flow).Count == 0, "replacement keeps one wire per input");
        // Input first, then output: the same replacement rule.
        e.ClickPort(new(hinge.Id, 0, false)); Check(e.ClickPort(new(a.Id, 0, true)) && e.Flow.Wires.Single(w => w.To == hinge.Id).From == a.Id, "input-first drop also replaces");
        Check(e.Undo() && e.Flow.Wires.Single(w => w.To == hinge.Id).From == b.Id, "undo brings back the replaced wire");
        report.Add("replace: drop on an occupied input replaces its driver in one revision; undo restores it");
    }

    private static void Snap()
    {
        var e = new FlowEditor(new());
        var src = e.Place("and", 0, 0)!; var dst = e.Place("or", 400, 0)!; var other = e.Place("not", 400, 200)!;
        var (px, py) = FlowLayout.Port(dst, false, 0);
        Check(e.Snap(px + 5, py + 5, 12) == null, "no snap without a pending wire");
        e.ClickPort(new(src.Id, 0, true));
        Check(e.Snap(px + 11.9, py, 12) == new FlowPort(dst.Id, 0, false), "snap at 11.9 px picks the input");
        Check(e.Snap(px + 8, py + 8, 12) == new FlowPort(dst.Id, 0, false), "snap on the diagonal inside 12 px");
        Check(e.Snap(px + 12.1, py, 12) == null, "no snap at 12.1 px");
        var (bx, by) = FlowLayout.Port(dst, false, 1);
        Check(e.Snap(bx, by - 4, 12) == new FlowPort(dst.Id, 1, false), "nearest of two close inputs wins (B)");
        Check(e.Snap(px, py + 4, 12) == new FlowPort(dst.Id, 0, false), "nearest of two close inputs wins (A)");
        var (ox, oy) = FlowLayout.Port(src, true, 0);
        Check(e.Snap(ox, oy, 12) == null, "never snaps back onto the starting output or another output");
        Check(e.Snap(px, py, 24) == new FlowPort(dst.Id, 0, false) && e.Snap(px + 20, py, 24) != null, "the panel scales the 12 px radius by 1/zoom");
        // A port on another tab is never a snap target, even at the same coordinates.
        var tab = e.AddSheet("Other")!; var hidden = e.Place("or", 400, 0)!; e.SelectSheet(e.Sheets[0].Id); e.ClickPort(new(src.Id, 0, true));
        Check(e.Snap(px, py, 12) == new FlowPort(dst.Id, 0, false) && e.Snap(px, py, 12) != new FlowPort(hidden.Id, 0, false), "snap only considers the visible tab");
        report.Add("snap: nearest fitting port within 12 px (graph radius 12/zoom), visible tab only, never same direction or the start block");
    }

    private static void Reasons()
    {
        var e = new FlowEditor(new()); var refused = new List<string>(); e.Refused = refused.Add;
        var a = e.Place("and", 0, 0)!; var b = e.Place("or", 300, 0)!;
        e.ClickPort(new(a.Id, 0, true)); Check(!e.ClickPort(new(a.Id, 0, false)) && refused.Last() == FlowEditor.ReasonLoop && e.Notice!.Contains("would create a loop"), "self wire: would create a loop");
        Check(e.Pending == new FlowPort(a.Id, 0, true), "after a refusal the wire is still pending");
        Check(!e.ClickPort(new(b.Id, 0, true)) && refused.Last() == FlowEditor.ReasonCompatible && e.Notice!.Contains("not compatible") && e.Pending == new FlowPort(b.Id, 0, true), "two outputs: not compatible, the wire now starts at the second");
        e.ClickPort(new(a.Id, 0, false)); Check(e.Flow.Wires.Count == 1, "valid wire after the corrections");
        e.ClickPort(new(b.Id, 0, true)); Check(!e.ClickPort(new(a.Id, 0, false)) && refused.Last() == FlowEditor.ReasonConnected && e.Flow.Wires.Count == 1 && e.Pending == null, "same wire again: already connected, no new revision");
        e.ClickPort(new(b.Id, 0, true)); Check(!e.ClickPort(new(a.Id, 5, false)) && refused.Last() == FlowEditor.ReasonCompatible, "unknown input port: not compatible");
        e.Cancel();
        var tab = e.AddSheet("Lights")!; var c = e.Place("not", 0, 0)!;
        Check(e.Refusal(new(b.Id, 0, true), new(c.Id, 0, false)) == FlowEditor.ReasonTab, "a wire between two tabs is refused");
        // The panel never shows the other tab, but a stale pending port from a model call is still refused.
        e.ClickPort(new(c.Id, 0, false)); Check(!e.ClickPort(new(b.Id, 0, true)) && refused.Last() == FlowEditor.ReasonTab && e.Flow.Wires.Count == 1, "cross-tab click is refused with the tab reason");
        Check(e.Notice!.Contains("different tabs"), "tab refusal text");
        var future = new FlowEditor(FlowJson.Parse("{\"v\":2,\"rev\":3,\"nodes\":[{\"id\":\"x\",\"type\":\"and\",\"params\":{}}],\"wires\":[]}"));
        Check(!future.ClickPort(new("x", 0, true)) && future.Pending == null, "read-only flow never starts a wire");
        report.Add("reasons: " + string.Join(", ", refused.Distinct()));
    }

    private static void Tabs()
    {
        var f = new FlowDocument(); var e = new FlowEditor(f); int saves = 0; e.Saved = () => saves++;
        Check(e.Sheets.Count == 1 && e.Sheets[0] == new FlowSheet("main", "Main") && !FlowSheets.Stored(f), "new flow: virtual Main tab, nothing stored");
        var a = e.Place("and", 0, 0)!; Check(e.SheetOf(a) == "main", "block lands on the visible tab");
        string id = e.AddSheet()!; Check(e.ActiveSheet == id && e.Sheets.Count == 2 && e.Sheets[1].Name == "Tab 2" && FlowSheets.Stored(f), "+ adds Tab 2 and shows it");
        var b = e.Place("or", 0, 0)!; Check(e.SheetOf(b) == id && e.SheetNodes().Single() == b, "the new tab only shows its own block");
        Check(e.RenameSheet(id, "  Lights\n ") && e.Sheets[1].Name == "Lights", "rename trims and cleans");
        Check(!e.RenameSheet(id, "   ") && !e.RenameSheet(id, "Lights"), "blank or unchanged name is a no-op");
        Check(e.RenameSheet(id, new string('x', 60)) && e.Sheets[1].Name.Length == FlowSheets.MaxName, "names are capped at 40 characters");
        long rev = f.Rev;
        Check(e.DeleteSheet(id) == SheetDelete.NeedsConfirm && f.Rev == rev && f.Nodes.Contains(b), "a tab with blocks needs confirmation");
        Check(e.DeleteSheet(id, true) == SheetDelete.Deleted && !f.Nodes.Contains(b) && e.Sheets.Count == 1 && f.Rev == rev + 1, "confirmed delete removes the tab and its blocks once");
        string empty = e.AddSheet("Empty")!; rev = f.Rev;
        Check(e.DeleteSheet(empty) == SheetDelete.Deleted && f.Rev == rev + 1, "an empty tab is deleted at once");
        Check(e.DeleteSheet(e.Sheets[0].Id, true) == SheetDelete.LastTab, "the last tab stays");
        // Templates open in a new tab named after the template; one edit, one save.
        Register(); int before = saves; rev = f.Rev;
        Check(e.Template(FlowTemplates.Reload, new[] { Flap }) && f.Rev == rev + 1 && saves == before + 1, "template is one edit and one save");
        Check(e.Sheets.Last().Name == "Shell flap: open while reloading" && e.ActiveSheet == e.Sheets.Last().Id, "template opens its own named tab");
        Check(e.SheetNodes().Count() == 2 && f.Wires.All(w => e.SheetOf(f.Nodes.First(n => n.Id == w.From)) == e.SheetOf(f.Nodes.First(n => n.Id == w.To))), "template wires stay inside its tab");
        e.SelectSheet(e.Sheets[0].Id);
        Check(e.Template(FlowTemplates.Reload, new[] { Flap, Flap2 }) && e.Sheets.Count(s => s.Name.StartsWith("Shell flap: open while reloading", StringComparison.Ordinal)) == 2 && e.Sheets.Last().Name == "Shell flap: open while reloading 2", "a second tab with the same template name is numbered");
        // An already placed output keeps its tab: the template chain joins it there.
        var flapNode = f.Nodes.First(n => n.PartId == Flap.Id); string flapSheet = e.SheetOf(flapNode); int tabs = e.Sheets.Count;
        e.SelectSheet(flapSheet); e.SelectNode(flapNode.Id);
        Check(e.Template(FlowTemplates.Fired, new[] { Flap }) && e.Sheets.Count == tabs && e.ActiveSheet == flapSheet && e.SheetNodes().Count() == 5, "template on a placed output goes into that output's tab");
        // The engine runs all tabs as one vehicle flow; the duplicate-part rule spans tabs.
        var engine = new FlowEngine(); engine.Configure(f); engine.Tick(0, new Signals());
        Check(f.Nodes.Where(FlowValidation.IsOutput).All(n => engine.Status.GetValueOrDefault(n.Id) != "unknown block"), "all tabs execute");
        var dup = new FlowNode("dup", "hinge"); dup.Set("partId", Flap.Id); dup.Data["sheet"] = e.Sheets[0].Id; f.Nodes.Add(dup);
        engine.Configure(f); engine.Tick(1, new Signals());
        Check(f.Nodes.Count(n => n.PartId == Flap.Id && engine.Status.GetValueOrDefault(n.Id) == "duplicate part") == 1, "duplicate part is still detected across tabs (one of the two blocks)");
        f.Nodes.Remove(dup);
        report.Add("tabs: + adds, rename cleans/caps at 40, delete empty at once or with blocks after confirmation, last tab stays, templates open a named tab");
    }

    private static void Migration()
    {
        string old = "{\"v\":1,\"rev\":7,\"nodes\":[{\"id\":\"a\",\"type\":\"and\",\"x\":0,\"y\":0,\"title\":\"\",\"params\":{}},{\"id\":\"h\",\"type\":\"hinge\",\"x\":300,\"y\":0,\"title\":\"Flap\",\"params\":{\"partId\":\"flap\",\"mode\":\"Follow\"}}],\"wires\":[{\"from\":\"a\",\"to\":\"h\",\"port\":0}],\"future\":{\"k\":1}}";
        var f = FlowJson.Parse(old); var e = new FlowEditor(f);
        Check(e.Sheets.Count == 1 && e.Sheets[0].Name == "Main" && e.SheetNodes().Count() == 2, "a flow without sheets loads as one tab Main with every block");
        Check(FlowJson.Write(f) == old, "loading alone never rewrites the 0.9.1 flow");
        var bad = FlowJson.Parse(old.Replace("\"future\":{\"k\":1}", "\"sheets\":[{\"id\":\"\"},{\"name\":\"no id\"},7]"));
        Check(new FlowEditor(bad).Sheets.Single().Name == "Main", "malformed sheets fall back to Main");
        var unknown = FlowJson.Parse(old.Replace("\"future\":{\"k\":1}", "\"sheets\":[{\"id\":\"s1\",\"name\":\"Radar\"}]"));
        Check(new FlowEditor(unknown).SheetNodes("s1").Count() == 2, "blocks without a known tab belong to the first tab");
        report.Add("migration: no sheets -> one tab Main; loading is byte-identical; blocks without sheet go to the first tab");
    }

    private static void RoundTrip()
    {
        var f = new FlowDocument(); var e = new FlowEditor(f);
        var a = e.Place("and", 10, 20)!; string lights = e.AddSheet("Lights")!; var b = e.Place("or", 30, 40)!; string radar = e.AddSheet("Radar")!; var c = e.Place("not", 50, 60)!;
        e.RenameSheet(e.Sheets[0].Id, "Hatches");
        f.Extra["sheets"] = System.Text.Json.JsonDocument.Parse(f.Extra["sheets"].GetRawText().Replace("\"name\":\"Radar\"", "\"name\":\"Radar\",\"colour\":\"#79d8e8\"")).RootElement.Clone();
        e.RenameSheet(radar, "Radar mast");
        string json = FlowJson.Write(f); var back = FlowJson.Parse(json); var e2 = new FlowEditor(back);
        Check(e2.Sheets.Select(s => s.Name).SequenceEqual(new[] { "Hatches", "Lights", "Radar mast" }), "tab order and names survive save/load");
        Check(e2.SheetOf(back.Nodes.Single(n => n.Id == a.Id)) == e.Sheets[0].Id && e2.SheetOf(back.Nodes.Single(n => n.Id == b.Id)) == lights && e2.SheetOf(back.Nodes.Single(n => n.Id == c.Id)) == radar, "every block keeps its tab");
        Check(FlowJson.Write(back) == json && json.Contains("\"colour\":\"#79d8e8\""), "byte-identical rewrite; unknown tab fields survive a rename");
        var storage = new FlowStorage(); storage.Attach("p", Guid.NewGuid().ToString("D"), back);
        Check(FlowJson.Write(storage.Flow!) == json, "storage arbitration keeps the tabs");
        string envelope = FlowJson.Envelope("part", back); Check(FlowJson.Write(FlowJson.ReadEnvelope(envelope).Flow) == json, "API envelope round trip keeps the tabs");
        File.WriteAllText("logs/sheets-roundtrip-0.9.2.json", json + "\n");
        report.Add("roundtrip: " + json.Length + " bytes, 3 tabs, byte-identical through parse/write, storage and envelope");
    }

    private static List<FlowRect> Rects(FlowEditor e) => e.SheetNodes().Select(FlowLayout.Bounds).ToList();
    private static int Overlaps(List<FlowRect> rects)
    {
        int bad = 0;
        for (int i = 0; i < rects.Count; i++) for (int j = i + 1; j < rects.Count; j++) if (rects[i].Inflate(FlowLayout.Margin).Overlaps(rects[j])) bad++;
        return bad;
    }
    private static void Placement()
    {
        Register();
        // 30 loose blocks of mixed heights, palette route, one tab.
        var e = new FlowEditor(new()) { View = FlowLayout.View(0, 0, 1) };
        string[] types = { "and", "or", "not", "delay", "pulse", "counter", "memory", "always", "key", "vehicle.braking" };
        for (int i = 0; i < 30; i++) e.PlaceFree(types[i % types.Length]);
        e.PlaceFree("rotator", Plate); e.PlaceFree("hinge", Flap);
        var loose = Rects(e);
        Check(loose.Count == 32 && Overlaps(loose) == 0, "32 blocks (30 loose + 2 outputs): every rectangle plus 24 px is free");
        int inView = loose.Count(r => e.View!.Value.Contains(r));
        Check(inView >= 12 && loose.Take(11).All(r => e.View!.Value.Contains(r)), "the visible area fills first (gaps are reused later)");
        Check(loose.Where(r => !e.View!.Value.Contains(r)).All(r => r.X >= e.View!.Value.Right - FlowLayout.Width && r.Y == loose.Min(q => q.Y)), "otherwise right of the existing content, top-aligned");
        // 5 templates in one tab: all on the same placed hinge output.
        var t = new FlowEditor(new()) { View = FlowLayout.View(0, 0, 1) };
        var hinge = t.PlaceFree("hinge", Flap)!; var rot = t.PlaceFree("rotator", Plate)!;
        string[] ids = { FlowTemplates.Reload, FlowTemplates.Fired, FlowTemplates.Brake, FlowTemplates.Reload, FlowTemplates.Fired };
        foreach (var id in ids) { t.SelectNode(hinge.Id); Check(t.Template(id, new[] { Flap }), id + " applies to the selected hinge"); }
        t.SelectNode(rot.Id); Check(t.Template(FlowTemplates.Spin, new[] { Plate }), "spin template on the placed rotator");
        Check(t.Sheets.Count == 1 && t.SheetNodes().Count() == t.Flow.Nodes.Count, "templates on placed outputs stay in their tab");
        var templated = Rects(t);
        Check(Overlaps(templated) == 0, "5+1 templates in one tab: no block overlaps another block plus 24 px");
        Check(t.Flow.Nodes.Count == 2 + 1 + 3 + 1 + 1 + 3 + 1 && FlowValidation.Validate(t.Flow).Count == 0, "all template chains placed and valid");
        // The palette route through the actual panel also uses free placement.
        report.Add($"placement: 32 loose blocks, {inView} in view, 0 overlaps; 6 templates in one tab ({templated.Count} blocks), 0 overlaps");
        File.WriteAllLines("logs/placement-0.9.2.csv", new[] { "set,x,y,w,h" }.Concat(loose.Select(r => $"loose,{r.X},{r.Y},{r.W},{r.H}")).Concat(templated.Select(r => $"templates,{r.X},{r.Y},{r.W},{r.H}")));
    }

    private static void UndoRedo()
    {
        Register();
        var f = new FlowDocument(); var e = new FlowEditor(f); int saves = 0; e.Saved = () => saves++;
        Check(!e.CanUndo && !e.CanRedo && !e.Undo() && !e.Redo(), "nothing to undo on a new flow");
        var a = e.Place("and", 0, 0)!; var b = e.Place("or", 300, 0)!; var c = e.Place("delay", 300, 200)!; var hinge = e.Place("hinge", 600, 0, Flap)!;
        e.ClickPort(new(a.Id, 0, true)); e.ClickPort(new(hinge.Id, 0, false));
        var kinds = new List<(string Name, Action Edit)>
        {
            ("add block", () => e.PlaceFree("not")),
            ("delete block", () => { e.SelectNode(c.Id); e.Delete(); }),
            ("lay wire", () => { e.ClickPort(new(a.Id, 0, true)); e.ClickPort(new(b.Id, 0, false)); }),
            ("replace wire", () => { e.ClickPort(new(b.Id, 0, true)); e.ClickPort(new(hinge.Id, 0, false)); }),
            ("remove wire", () => { e.SelectWire(f.Wires.First()); e.Delete(); }),
            ("parameter", () => e.Parameter(hinge.Id, "openAngle", "45")),
            ("mode", () => e.Parameter(hinge.Id, "mode", "Toggle")),
            ("move (one drop)", () => { e.Move(a.Id, 10, 10, false); e.Move(a.Id, 20, 20, false); e.Move(a.Id, 40, 48, true); }),
            ("rename block", () => e.Rename(a.Id, "Both")),
            ("add tab", () => e.AddSheet("Lights")),
            ("rename tab", () => e.RenameSheet(e.ActiveSheet, "Lamps")),
            ("delete tab", () => Check(e.DeleteSheet(e.ActiveSheet) == SheetDelete.Deleted, "the renamed empty tab is deleted at once")),
            ("template", () => e.Template(FlowTemplates.Reload, new[] { Flap2 })),
        };
        foreach (var (name, edit) in kinds)
        {
            // Restore handles: undo/redo rebuilds node objects, so look blocks up by id.
            FlowNode N(string id) => f.Nodes.First(n => n.Id == id);
            string before = NoRev(FlowJson.Write(f)); int undo = e.History.UndoCount; long rev = f.Rev;
            edit();
            string after = NoRev(FlowJson.Write(f));
            Check(after != before, name + " changed the flow");
            Check(e.History.UndoCount == undo + 1 && f.Rev > rev, name + " is exactly one undo step");
            long r1 = f.Rev; int s1 = saves;
            Check(e.Undo() && NoRev(FlowJson.Write(f)) == before && f.Rev == r1 + 1 && saves == s1 + 1, name + ": undo restores the previous flow with a new rev and one save");
            Check(e.CanRedo && e.Redo() && NoRev(FlowJson.Write(f)) == after && f.Rev == r1 + 2 && saves == s1 + 2, name + ": redo reapplies it with a new rev");
            if (name is "delete block") c = null!;
            a = N(a.Id); b = N(b.Id); hinge = N(hinge.Id);
            report.Add("undo/redo: " + name);
        }
        Check(e.Undo() && e.Undo() && NoRev(FlowJson.Write(f)) != "" && e.CanRedo, "several undos in a row");
        e.PlaceFree("and"); Check(!e.CanRedo, "a new edit clears redo");
        Check(FlowValidation.Validate(f).Count == 0, "undo/redo never leaves an invalid graph");
    }

    private static void UndoLimit()
    {
        var f = new FlowDocument(); var e = new FlowEditor(f); var n = e.Place("and", 0, 0)!;
        var states = new List<string>();
        for (int i = 1; i <= 60; i++) { states.Add(NoRev(FlowJson.Write(f))); e.Move(n.Id, i * 8, 0, true); }
        Check(e.History.UndoCount == FlowHistory.Limit, "the stack keeps 50 steps");
        int undone = 0; while (e.Undo()) undone++;
        Check(undone == 50 && NoRev(FlowJson.Write(f)) == states[10], "50 undos reach the state after the 10th move, then stop");
        Check(!e.CanUndo && e.History.RedoCount == 50, "redo also holds 50");
        int redone = 0; while (e.Redo()) redone++;
        Check(redone == 50 && FlowPresentation.Coordinate(f.Nodes[0], "x") == 480, "50 redos return to the last move");
        report.Add("undo limit: 60 moves -> 50 undo steps, 50 redo steps");
    }

    private sealed class Signals : IVehicleSignals
    {
        public bool IsPlayer => true; public bool Braking => false;
        public IReadOnlyList<CannonShot> Fired => Array.Empty<CannonShot>();
        public IReadOnlyList<CannonState> Cannons { get; set; } = Array.Empty<CannonState>();
        public IReadOnlyList<HingeState> Hinges => Array.Empty<HingeState>();
        public IReadOnlyList<MastState> Masts => Array.Empty<MastState>();
        public int KeyPresses(int action) => 0; public bool KeyHeld(int action) => false;
    }

    /// <summary>The actual HingePanel with Kit API doubles: 24 blocks, tabs, snap, refusals, undo buttons, pending calls.</summary>
    private static void Panel()
    {
        Register();
        HingeRuntime.Current = null; UiKit.Context = UiContexts.Designer; Time.unscaledTime = 0; SprocketTelescopicMast.Plugin.Traces.Clear();
        HingePanel.Create(new()); UiKit.Poll(); UiKit.Latest.Open(); UiKit.Poll();
        Check(UiKit.Created.Id == "hinge-io" && UiKit.Created.Title == "I/O Flow", "panel id hinge-io, title I/O Flow");
        var f = new FlowDocument(); int saves = 0;
        HingeRuntime.Current = new() { Flow = f, Parts = new() { Flap, Flap2, Plate }, Save = () => saves++ };
        void Refresh() { Time.unscaledTime += .1f; HingePanel.Update(); UiKit.Poll(); }
        Refresh(); var graph = NodeGraph.Latest;
        Check(UiKit.ButtonEnabled.ContainsKey("Undo") && !UiKit.ButtonEnabled["Undo"]() && !UiKit.ButtonEnabled["Redo"](), "Undo/Redo buttons are disabled with nothing to do");
        Check(UiKit.TabBars.Count == 1 && UiKit.TabBars[0].Labels().SequenceEqual(new[] { "Main" }), "one Kit Tabs bar showing Main");
        // 24 blocks from the palette buttons.
        string[] palette = { "AND", "OR", "NOT", "Delay", "Pulse", "Counter", "Memory", "Always on" };
        for (int i = 0; i < 22; i++) UiKit.Buttons[palette[i % palette.Length]]();
        UiKit.Buttons["Shell flap"](); UiKit.Buttons["Rear flap"](); Refresh();
        Check(f.Nodes.Count == 24 && graph.Nodes.Count == 24, "24 blocks placed and drawn");
        Check(Overlaps(f.Nodes.Select(FlowLayout.Bounds).ToList()) == 0, "palette placement through the panel: no overlaps");
        Check(UiKit.ButtonEnabled["Undo"](), "Undo enabled after an edit");
        FlowPort Port(FlowNode n, bool output, int i) => new(n.Id, i, output);
        string PortId(FlowPort p) => p.Output ? "out" : "in" + p.Index;
        void ClickPort(FlowPort p) => graph.Click!(new() { Kind = GraphHitKind.Port, NodeId = p.Node, PortId = PortId(p) });
        void ClickAt(GraphHitKind kind, string? node, double x, double y) => graph.Click!(new() { Kind = kind, NodeId = node, GraphPos = new((float)x, (float)y) });
        var logic = f.Nodes.Where(n => FlowEditor.HasOutput(n) && FlowValidation.Ports(n) >= 1).ToList();
        var outputs = f.Nodes.Where(FlowValidation.IsOutput).ToList();
        int begins = graph.Begins, ends = graph.Ends, connected = 0;
        // 20 wires: every source drives the next block; half by exact port clicks, half by near misses on the block body.
        for (int i = 0; i < 20; i++)
        {
            var from = logic[i % logic.Count]; var to = i < 18 ? logic[(i + 1) % logic.Count] : outputs[i - 18];
            ClickPort(Port(from, true, 0)); Refresh();
            var (x, y) = FlowLayout.Port(to, false, 0);
            if (i % 2 == 0) ClickPort(Port(to, false, 0));
            else ClickAt(GraphHitKind.Node, to.Id, x + 9, y - 3); // 9.5 px inside the body: Kit reports Node, not Port
            Refresh();
            if (f.Wires.Any(w => w.From == from.Id && w.To == to.Id)) connected++;
        }
        Check(connected == 20, "20 of 20 wires connect among 24 blocks, including 10 near misses on the block body");
        Check(graph.Begins == begins + 20 && graph.Ends == ends + 20, "each wire begins once and ends once (pending only on change)");
        int structural = graph.StructuralCalls; for (int i = 0; i < 300; i++) Refresh();
        Check(graph.StructuralCalls == structural && graph.Begins == begins + 20 && graph.Ends == ends + 20, "300 idle refreshes: no structural or pending calls");
        // Outside 12 px a body click selects the block and cancels the pending wire (old behaviour kept for real misses).
        var lone = logic[3]; var (lx, ly) = FlowLayout.Port(lone, false, 0);
        ClickPort(Port(logic[5], true, 0)); ClickAt(GraphHitKind.Node, lone.Id, lx + 20, ly); Refresh();
        Check(f.Wires.Count(w => w.To == lone.Id) == 1 && UiKit.Latest.Status.Length > 0 && graph.Ends == ends + 21, "a click 20 px away is not a drop");
        // Refusal: status line text and the DetailedLogging trace.
        ClickPort(Port(logic[2], true, 0)); ClickPort(Port(logic[2], false, 0)); Refresh();
        Check(UiKit.Latest.Status.StartsWith("Wire refused: would create a loop", StringComparison.Ordinal) && UiKit.Latest.StatusKind == StatusKind.Warning, "status line shows the refusal reason");
        Check(SprocketTelescopicMast.Plugin.Traces.Contains("[FLOW] wire refused would create a loop"), "[FLOW] wire refused trace");
        SprocketTelescopicMast.Plugin.Detailed = false; ClickPort(Port(logic[4], true, 0)); Refresh();
        int traces = SprocketTelescopicMast.Plugin.Traces.Count; ClickPort(Port(logic[4], false, 0));
        Check(SprocketTelescopicMast.Plugin.Traces.Count == traces, "no trace with DetailedLogging off");
        SprocketTelescopicMast.Plugin.Detailed = true; graph.Click!(new() { Kind = GraphHitKind.Canvas, GraphPos = new(-5000, -5000) }); Refresh();
        Time.unscaledTime += 6; Refresh(); Check(!UiKit.Latest.Status.StartsWith("Wire refused", StringComparison.Ordinal), "the notice clears after 5 s");
        // Replacement through the panel, with snap at zoom 0.5 (12 px = 24 graph units).
        graph.SetView(graph.Pan, .5f); var target = outputs[0]; var (tx, ty) = FlowLayout.Port(target, false, 0);
        ClickPort(Port(logic[7], true, 0)); ClickAt(GraphHitKind.Canvas, null, tx - 20, ty); Refresh();
        Check(f.Wires.Single(w => w.To == target.Id).From == logic[7].Id && UiKit.Latest.Status.StartsWith("Replaced the old wire", StringComparison.Ordinal), "drop near an occupied input at zoom 0.5 replaces its wire");
        graph.SetView(graph.Pan, 1);
        // Tabs: +, rename, switch, template tab, delete with confirmation, undo.
        UiKit.Buttons["+"](); Refresh();
        Check(UiKit.TabBars.Last().Labels().SequenceEqual(new[] { "Main", "Tab 2" }) && graph.Nodes.Count == 0, "+ creates Tab 2 and shows the empty tab");
        UiKit.TextFields["Tab name"]("Lights"); Refresh(); Check(UiKit.TabBars.Last().Labels().Last() == "Lights", "tab renamed through the field");
        UiKit.Buttons["AND"](); Refresh(); Check(graph.Nodes.Count == 1, "a block on the new tab");
        UiKit.TabBars.Last().Select(0); Refresh(); Check(graph.Nodes.Count == 24 && UiKit.TabBars.Last().Selected() == 0, "switching back shows the 24 blocks");
        UiKit.TabBars.Last().Select(1); Refresh();
        int confirms = UiKit.Confirms.Count; UiKit.Buttons["Delete tab"]();
        Check(UiKit.Confirms.Count == confirms + 1 && f.Nodes.Count == 25, "deleting a tab with blocks asks first");
        UiKit.Confirms.Last().Confirm(); Refresh();
        Check(f.Nodes.Count == 24 && UiKit.TabBars.Last().Labels().Count == 1 && graph.Nodes.Count == 24, "confirmed: tab and block gone, Main shown");
        UiKit.Buttons["Undo"](); Refresh();
        Check(f.Nodes.Count == 25 && UiKit.TabBars.Last().Labels().SequenceEqual(new[] { "Main", "Lights" }) && graph.Nodes.Count == 1, "Undo button brings the tab back and shows it");
        UiKit.Buttons["Redo"](); Refresh(); Check(f.Nodes.Count == 24 && UiKit.ButtonEnabled["Redo"]() == false, "Redo button deletes it again");
        HingeRuntime.Current.Parts.Add(new FlowPart("free", "hinge", "Side flap")); Refresh();
        UiKit.Buttons["Shell flap: open while reloading"](); Refresh();
        Check(UiKit.TabBars.Last().Labels().Last() == "Shell flap: open while reloading" && graph.Nodes.Count == 2, "template button opens a new tab named after the template");
        // Undo of a move through the panel moves the drawn block back.
        var moved = f.Nodes.Last(); graph.Move!(moved.Id, new(900, 400), true); Refresh();
        UiKit.Buttons["Undo"](); Refresh();
        Check(graph.Nodes[moved.Id].Position.x == (float)FlowPresentation.Coordinate(f.Nodes.First(n => n.Id == moved.Id), "x"), "undo of a drag moves the drawn block back");
        Check(UiKit.ButtonEnabled["Undo"]() && UiKit.ButtonEnabled["Redo"](), "both buttons enabled with steps either way");
        UiKit.Latest.Close(); UiKit.Latest.Open(); Refresh();
        Check(!UiKit.ButtonEnabled["Undo"]() && !UiKit.ButtonEnabled["Redo"](), "closing the panel clears the undo stack");
        Check(saves > 0, "panel edits save");
        File.WriteAllText("logs/panel-0.9.2.txt", $"Actual HingePanel.cs with Kit API doubles.\n24 blocks, 20/20 wires (10 exact port clicks, 10 near misses 9.5 px on the block body).\nPending: {graph.Begins} begins / {graph.Ends} ends in total; 300 idle refreshes: 0 structural or pending calls.\nTraces: {string.Join(" | ", SprocketTelescopicMast.Plugin.Traces.Distinct())}\n");
        HingePanel.Destroy(); HingeRuntime.Current = null;
        report.Add("panel: 24 blocks, 20/20 wires incl. 10 near misses, pending begin/end once per wire, refusal status + trace, tabs, confirm, undo/redo buttons");
    }

    private static readonly Regex OldName = new(@"Hinge\s*/\s*I/O|Hinge\s+IO\b|Hinge\s+I/O|I/O SANDBOX|Hinge\s*/\s*Action", RegexOptions.Compiled);
    private static void Name()
    {
        // User-visible text: shipped C# string literals, part descriptions (UTF-16), README, TESTPLAN, CONFIGURATION.
        var hits = new List<string>();
        foreach (var file in Directory.GetFiles(".", "*.cs").Concat(Directory.GetFiles("Io", "*.cs")).Concat(Directory.GetFiles("api", "*.cs")))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(?:[^\"\\\\]|\\\\.)*\""))
                if (OldName.IsMatch(m.Value)) hits.Add(file + ": " + m.Value);
        foreach (var file in Directory.GetFiles("assets", "*.xml"))
        {
            var bytes = File.ReadAllBytes(file);
            Check(bytes[0] == 0xFF && bytes[1] == 0xFE, file + " stays UTF-16");
            if (OldName.IsMatch(System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2))) hits.Add(file);
        }
        foreach (var doc in new[] { "README.md", "TESTPLAN.md", "CONFIGURATION.md" })
            foreach (var (line, i) in File.ReadAllLines(doc).Select((l, i) => (l, i)))
                if (OldName.IsMatch(line)) hits.Add(doc + ":" + (i + 1));
        Check(hits.Count == 0, "old panel name in user-visible text: " + string.Join("; ", hits));
        // Ids are unchanged: keybind actions, panel id, config sections/keys.
        string plugin = File.ReadAllText("Plugin.cs");
        Check(plugin.Contains("PanelActionId = \"hinge-panel\"") && plugin.Contains("\"hinge-action-\" + i"), "keybind action ids unchanged");
        Check(plugin.Contains("\"I/O Flow / Open panel\"") && plugin.Contains("\"I/O Flow / Action \" + i"), "keybind labels renamed");
        Check(File.ReadAllText("HingePanel.cs").Contains("CreatePanel(Plugin.ModGuid, \"hinge-io\""), "panel id hinge-io unchanged");
        var bind = new Regex("Config\\.Bind\\(\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\"");
        var keysNow = bind.Matches(plugin).Select(m => m.Groups[1].Value + "/" + m.Groups[2].Value).ToList();
        var keysExpected = new[] { "General/Enabled", "Diagnostics/DetailedLogging", "Keys/Raise", "Keys/Lower", "Mechanism/StrokeMetres", "Mechanism/SpeedMetresPerSecond", "Diagnostics/PassiveAtStartup" };
        Check(keysNow.SequenceEqual(keysExpected), "config sections and keys as specified (" + keysNow.Count + ")");
        var ids = new Regex("RegisterButton\\(ModGuid, \"Telescopic Mast\", ([^,]+),");
        Check(ids.Matches(plugin).Select(m => m.Groups[1].Value).SequenceEqual(new[] { "PanelActionId", "\"hinge-action-\" + i", "RaiseActionId", "LowerActionId" }), "every keybind registration has its specified id expression");
        report.Add("name: 0 old-name hits in C# literals, 8 part XMLs, README/TESTPLAN/CONFIGURATION; ids hinge-panel, hinge-action-N, hinge-io and " + keysNow.Count + " config keys unchanged");
    }
}
