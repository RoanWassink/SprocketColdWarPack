using SprocketTelescopicMast;
using SprocketTelescopicMast.Io;
using SprocketUiKit;
using UnityEngine;

/// <summary>
/// 0.9.4: port drags, Ctrl+Z/Ctrl+Y/Ctrl+Shift+Z through the Kit shortcuts, placement from the real
/// VisibleRect, and the Tabs labels getter (no rebuild on rename). Kit behaviour is modelled by the
/// stubs in PanelStubs.cs: panel focus suspends game input, shortcuts go to the focus owner.
/// </summary>
internal static class Tests094
{
    private static int checks;
    private static readonly List<string> report = new();
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("0.9.4: " + why); }
    private static readonly FlowPart Flap = new("flap", "hinge", "Shell flap"), Flap2 = new("flap2", "hinge", "Rear flap"),
        Plate = new("plate", "rotator", "Radar");

    private static void Register()
    {
        IoRegistry.ClearForTests();
        IoRegistry.RegisterInput(new CannonFiredInput()); IoRegistry.RegisterInput(new CannonReloadedInput());
        IoRegistry.RegisterInput(new CannonReloadingInput()); IoRegistry.RegisterInput(new CannonLoadedInput());
        IoRegistry.RegisterInput(new KeyInput()); IoRegistry.RegisterInput(new VehicleBrakingInput());
    }

    public static int Run()
    {
        Register();
        DragModel(); Panel();
        Dependency();
        Directory.CreateDirectory("logs");
        File.WriteAllLines("logs/tests-0.9.4-detail.txt", report);
        return checks;
    }

    private static void DragModel()
    {
        var e = new FlowEditor(new()); int saves = 0; e.Saved = () => saves++;
        var refused = new List<string>(); e.Refused = refused.Add;
        var a = e.Place("and", 0, 0)!; var b = e.Place("or", 300, 0)!; var hinge = e.Place("hinge", 600, 0, Flap)!;
        long rev = e.Flow.Rev; int saved = saves;
        Check(e.Drop(new(a.Id, 0, true), new(hinge.Id, 0, false)), "output -> input drag connects");
        Check(e.Flow.Wires.Count == 1 && e.Flow.Rev == rev + 1 && saves == saved + 1 && e.Notice == null,
            "a drag is one revision and one save, with no refusal");
        var (px, py) = FlowLayout.Port(hinge, false, 0);
        Check(e.SnapFrom(new(b.Id, 0, true), px + 11.9, py, 12) == new FlowPort(hinge.Id, 0, false),
            "a drag within 11.9 px snaps to the input");
        Check(e.SnapFrom(new(b.Id, 0, true), px + 8, py + 8, 12) == new FlowPort(hinge.Id, 0, false),
            "a diagonal drag inside 12 px snaps");
        Check(e.SnapFrom(new(b.Id, 0, true), px + 12.1, py, 12) == null, "12.1 px does not snap");
        rev = e.Flow.Rev; int before = saves;
        Check(e.Drop(new(b.Id, 0, true), new(hinge.Id, 0, false)), "a drag onto an occupied input connects");
        Check(e.Flow.Wires.Count(w => w.To == hinge.Id) == 1 && e.Flow.Wires.Single(w => w.To == hinge.Id).From == b.Id,
            "the drag replaced the old driver");
        Check(e.Flow.Rev == rev + 1 && saves == before + 1 && e.Notice!.StartsWith("Replaced the old wire", StringComparison.Ordinal),
            "replacement is one revision and one save and says so");
        int wires = e.Flow.Wires.Count; rev = e.Flow.Rev;
        Check(!e.Drop(new(a.Id, 0, true), new(a.Id, 0, false)) && refused.Last() == FlowEditor.ReasonLoop &&
            e.Notice!.Contains("would create a loop") && e.Flow.Rev == rev && e.Flow.Wires.Count == wires,
            "a drag to its own block is refused as a loop and changes nothing");
        Check(!e.Drop(new(a.Id, 0, true), new(b.Id, 0, true)) && refused.Last() == FlowEditor.ReasonCompatible &&
            e.Notice!.Contains("not compatible"), "output -> output drag is not compatible");
        Check(e.SnapFrom(new(a.Id, 0, true), -5000, -5000, 12) == null, "a drag on empty canvas has no target");
        report.Add("drag model: exact/snapped/replacement/loop/not-compatible, one revision and save per wire");
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

    private static void Panel()
    {
        Register();
        HingeRuntime.Current = null; UiKit.Context = UiContexts.Designer; Time.unscaledTime = 0;
        SprocketTelescopicMast.Plugin.Traces.Clear();
        HingePanel.Create(new()); UiKit.Poll(); UiKit.Latest.Open(); UiKit.Poll();
        var f = new FlowDocument(); int saves = 0;
        HingeRuntime.Current = new() { Flow = f, Parts = new() { Flap, Flap2, Plate }, Save = () => saves++ };
        void Refresh() { Time.unscaledTime += .1f; HingePanel.Update(); UiKit.Poll(); }
        Refresh();
        var graph = NodeGraph.Latest;
        var bar = UiKit.TabBars.Single();
        // Rename and add through the labels getter on the same Tabs element.
        Check(UiKit.Buttons["+"].Method.Name == "AddTab", "the tab + button is the active + action, got " + UiKit.Buttons["+"].Method.Name);
        UiKit.Buttons["+"](); Refresh();
        Check(ReferenceEquals(UiKit.TabBars.Single(), bar) && bar.Labels().SequenceEqual(new[] { "Main", "Tab 2" }),
            "+ gives the same Tabs element new labels, got " + string.Join("/", bar.Labels()) + " sheets=" + FlowSheets.Read(f).Count +
            " stored=" + FlowSheets.Stored(f) + " rev=" + f.Rev + " nodes=" + f.Nodes.Count);
        UiKit.TextFields["Tab name"]("Lights"); Refresh();
        Check(ReferenceEquals(UiKit.TabBars.Single(), bar) && bar.Labels().Last() == "Lights",
            "renaming a tab updates the getter without rebuilding the tab bar");
        bar.Select(0); Refresh();
        UiKit.Buttons["AND"](); UiKit.Buttons["OR"](); UiKit.Buttons["NOT"](); UiKit.Buttons["Shell flap"](); Refresh();
        Check(f.Nodes.Count == 4 && graph.Nodes.Count == 4, "four blocks on Main");
        FlowNode N(string type) => f.Nodes.First(n => n.Type == type);
        var and = N("and"); var or = N("or"); var not = N("not"); var hinge = N("hinge");
        string PortId(FlowPort p) => p.Output ? "out" : "in" + p.Index;
        GraphHit Hit(FlowPort p) { var (x, y) = FlowLayout.Port(f.Nodes.First(n => n.Id == p.Node), p.Output, p.Index); return new() { Kind = GraphHitKind.Port, NodeId = p.Node, PortId = PortId(p), GraphPos = new((float)x, (float)y) }; }
        void Drag(FlowPort from, GraphHit drop) { graph.Drop(new(from.Node, PortId(from), from.Output, drop)); Refresh(); }
        Drag(new(and.Id, 0, true), Hit(new(or.Id, 0, false)));
        Check(f.Wires.Any(w => w.From == and.Id && w.To == or.Id), "dropping on a port connects");
        var (nx, ny) = FlowLayout.Port(not, false, 0);
        Drag(new(or.Id, 0, true), new() { Kind = GraphHitKind.Canvas, GraphPos = new((float)(nx + 8), (float)(ny + 3)) });
        Check(f.Wires.Any(w => w.From == or.Id && w.To == not.Id), "dropping 8.5 px from an input snaps and connects");
        Drag(new(not.Id, 0, true), Hit(new(or.Id, 0, false)));
        Check(f.Wires.Single(w => w.To == or.Id && w.Port == 0).From == not.Id &&
            UiKit.Latest.Status.StartsWith("Replaced the old wire", StringComparison.Ordinal), "a drag replaces an occupied input");
        int wires = f.Wires.Count, traces = SprocketTelescopicMast.Plugin.Traces.Count;
        Drag(new(or.Id, 0, true), new() { Kind = GraphHitKind.Canvas, GraphPos = new(-9000, -9000) });
        Check(f.Wires.Count == wires && SprocketTelescopicMast.Plugin.Traces.Count == traces &&
            !UiKit.Latest.Status.StartsWith("Wire refused", StringComparison.Ordinal), "a drag with no target changes and reports nothing");
        Drag(new(and.Id, 0, true), Hit(new(and.Id, 0, false)));
        Check(UiKit.Latest.Status.StartsWith("Wire refused: would create a loop", StringComparison.Ordinal) &&
            UiKit.Latest.StatusKind == StatusKind.Warning &&
            SprocketTelescopicMast.Plugin.Traces.Contains("[FLOW] wire refused would create a loop"),
            "a refused drag writes the status line and the [FLOW] trace");
        Drag(new(and.Id, 0, true), Hit(new(or.Id, 0, true)));
        Check(UiKit.Latest.Status.StartsWith("Wire refused: not compatible", StringComparison.Ordinal),
            "an output -> output drag reports not compatible");
        // Shortcuts: only the focus owner runs them; game input is blocked while a panel has focus.
        int count = f.Nodes.Count;
        UiKit.Buttons["Memory"](); Refresh(); count = f.Nodes.Count;
        Check(UiKit.Key("Ctrl+Z"), "Ctrl+Z reaches the focused I/O Flow panel");
        Refresh(); Check(f.Nodes.Count == count - 1 && UiKit.GameUndo == 0, "Ctrl+Z undoes the flow, not the designer");
        UiKit.Key("Ctrl+Shift+Z"); Refresh(); Check(f.Nodes.Count == count, "Ctrl+Shift+Z redoes");
        UiKit.Key("Ctrl+Z"); UiKit.Key("Ctrl+Y"); Refresh(); Check(f.Nodes.Count == count, "Ctrl+Z then Ctrl+Y round-trips");
        var other = new Panel(); UiKit.Focus = other; int kept = f.Nodes.Count;
        UiKit.Key("Ctrl+Z"); Refresh();
        Check(f.Nodes.Count == kept, "another panel's focus does not receive the I/O Flow shortcuts");
        Check(UiKit.GameKeysBlocked, "the Kit keyboard guard is active while a panel has focus");
        bool blocked = false; try { UiKit.GameKey("Ctrl+Z"); } catch (InvalidOperationException) { blocked = true; }
        Check(blocked && UiKit.GameUndo == 0, "the game's Ctrl+Z cannot fire while the panel has focus");
        UiKit.Focus = null;
        Check(!UiKit.Key("Ctrl+Z") && f.Nodes.Count == kept && UiKit.GameUndo == 1,
            "Kit 0.1.12: an open panel without focus cannot steal designer undo");
        UiKit.GameUndo = 0;
        UiKit.Focus = UiKit.Latest;
        // Placement uses the graph's real visible rectangle, not a fixed 760 x 560.
        graph.SetSize(400, 300); graph.SetView(new Vector2(-10000, 8000), 1);
        UiKit.Buttons["OR"](); Refresh();
        var placed = f.Nodes.Last(); var view = graph.VisibleRect;
        var bounds = FlowLayout.Bounds(placed);
        Check(view.width == 400 && view.height == 300 && new FlowRect(view.x, view.y, view.width, view.height).Contains(bounds),
            "a new block lands inside VisibleRect");
        graph.SetSize(210, 200); int oldX = (int)graph.Pan.x;
        UiKit.Buttons["OR"](); Refresh();
        var second = f.Nodes.Last(); var b2 = FlowLayout.Bounds(second); var v2 = graph.VisibleRect;
        Check(graph.Pan.x != oldX && new FlowRect(v2.x, v2.y, v2.width, v2.height).Contains(b2),
            "when the visible part is too small the view follows the new block into view: pan " + oldX + " -> " + graph.Pan.x +
            " block " + b2.X + "," + b2.Y + "," + b2.W + "," + b2.H + " view " + v2.x + "," + v2.y + "," + v2.width + "," + v2.height);
        // The game's undo works again once no panel owns focus.
        UiKit.Latest.Close(); int game = UiKit.GameUndo;
        Check(!UiKit.Key("Ctrl+Z") && UiKit.GameUndo == game + 1, "with no panel focus Ctrl+Z reaches the designer");
        Check(saves > 0, "panel edits still save");
        File.WriteAllText("logs/panel-0.9.4.txt",
            "Actual HingePanel.cs with the 0.1.11 Kit API doubles.\n" +
            "Drag: exact port, 8.5 px snap, replacement, no-target no-op, loop and not-compatible refusals.\n" +
            "Shortcuts: Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z through the focus owner; another panel and the game get nothing.\n" +
            $"VisibleRect placement: {view.width}x{view.height} -> block inside; then {v2.width}x{v2.height} -> panned to {v2.x:0},{v2.y:0}.\n" +
            $"Tabs element reused for add and rename. Saves: {saves}. Traces: {string.Join(" | ", SprocketTelescopicMast.Plugin.Traces.Distinct())}\n");
        HingePanel.Destroy(); HingeRuntime.Current = null;
        report.Add("panel: drag connect/snap/replace/no-op/refusals, focus-only shortcuts with game input blocked, VisibleRect placement, Tabs getter reuse");
    }

    private static void Dependency()
    {
        string plugin = File.ReadAllText("Plugin.cs"), project = File.ReadAllText("SprocketTelescopicMast.csproj");
        string readme = File.ReadAllText("README.md"), plan = File.ReadAllText("TESTPLAN.md");
        Check(plugin.Contains("internal const string Version = \"0.9.9\""), "mast version 0.9.9");
        Check(project.Contains("<Version>0.9.9</Version>"), "project version 0.9.9");
        Check(plugin.Contains("BepInDependency(UiKit.PluginGuid, \">=0.1.12 <0.2.0\")"), "Kit dependency >=0.1.12 <0.2.0");
        Check(readme.Contains("0.1.12") && plan.Contains("0.1.12"), "README and TESTPLAN name Kit 0.1.12");
        report.Add("dependency: version 0.9.9, Kit >=0.1.12 <0.2.0, README/TESTPLAN");
    }
}
