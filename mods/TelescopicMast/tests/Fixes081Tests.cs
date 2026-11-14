using SprocketTelescopicMast;
using SprocketTelescopicMast.Io;
using SprocketUiKit;
using UnityEngine;

internal static class Fixes081Tests
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("0.8.1: " + message); }
    private sealed class Signals : IVehicleSignals
    {
        public bool IsPlayer => true; public bool Braking { get; set; }
        public IReadOnlyList<CannonShot> Fired { get; set; } = Array.Empty<CannonShot>();
        public IReadOnlyList<CannonState> Cannons { get; set; } = Array.Empty<CannonState>();
        public IReadOnlyList<HingeState> Hinges => Array.Empty<HingeState>();
        public IReadOnlyList<MastState> Masts => Array.Empty<MastState>();
        public int KeyPresses(int action) => 0; public bool KeyHeld(int action) => false;
    }
    private static readonly FlowPart Flap = new("flap", "hinge", "Shell flap");
    private static void Register()
    {
        IoRegistry.ClearForTests();
        IoRegistry.RegisterInput(new CannonReloadingInput()); IoRegistry.RegisterInput(new CannonFiredInput());
        IoRegistry.RegisterInput(new CannonLoadedInput()); IoRegistry.RegisterInput(new CannonReloadedInput());
        IoRegistry.RegisterInput(new VehicleBrakingInput());
    }
    public static int Run()
    {
        Register(); Templates(); ReloadFollow(); Footers(); Panel(); return checks;
    }
    private static void Templates()
    {
        var lamp = new FlowPart("lamp", "effects.light", "Brake light", "Effects");
        var mast = new FlowPart("mast", "mast", "Mast");
        var parts = new[] { mast, Flap, lamp };
        foreach (var t in FlowTemplates.All.Where(t => t.Id is FlowTemplates.Reload or FlowTemplates.Fired or FlowTemplates.Brake))
        {
            var f = new FlowDocument(); var e = new FlowEditor(f); int saves = 0; e.Saved = () => saves++;
            Check(e.Template(t.Id, parts), t.Id + " places");
            Check(f.Rev == 1 && saves == 1 && e.StructureRevision == 1, t.Id + " is one complete edit/save");
            Check(FlowValidation.Validate(f).Count == 0, t.Id + " valid graph");
            var output = f.Nodes.Single(FlowValidation.IsOutput);
            Check(output.PartId == (t.Id == FlowTemplates.Brake ? lamp.Id : Flap.Id) && output.Get("mode") == "Follow", t.Id + " correct target and mode");
            Check(f.Nodes.Count == (t.Id == FlowTemplates.Fired ? 4 : 2) && f.Wires.Count == f.Nodes.Count - 1, t.Id + " connected chain");
            var input = f.Nodes.First();
            if (t.Id != FlowTemplates.Brake) Check(input.Get("cannon") == "any" && input.Get("fireGroup") == "0", "default any cannon and any group");
            if (t.Id == FlowTemplates.Reload) Check(input.Get("mode") == "notReady", "reload template holds until ready");
            if (t.Id == FlowTemplates.Fired)
            {
                Check(f.Nodes[1].Type == "delay" && f.Nodes[1].Number("t") == .5 && f.Nodes[2].Type == "pulse" && f.Nodes[2].Number("t") == 2, "exact delay/pulse defaults and order");
                var signals = new Signals { Cannons = new[] { new CannonState(88, 1, false, false, true, 0) } };
                var engine = new FlowEngine(); engine.Configure(f); engine.Tick(0, signals);
                signals.Fired = new[] { new CannonShot(88, 1) }; engine.Tick(.1, signals); signals.Fired = Array.Empty<CannonShot>();
                engine.Tick(.2, signals); engine.Tick(.59, signals); Check(!engine.Target(output.Id), "pulse template waits half a second");
                engine.Tick(.6, signals); Check(engine.Target(output.Id), "pulse template fires after delay");
                engine.Tick(.7, signals); engine.Tick(2.59, signals); Check(engine.Target(output.Id), "pulse holds for two seconds");
                engine.Tick(2.61, signals); Check(!engine.Target(output.Id), "pulse closes independent of readiness");
            }
            if (t.Id == FlowTemplates.Brake)
            {
                var signals = new Signals(); var engine = new FlowEngine(); engine.Configure(f); engine.Tick(0, signals);
                signals.Braking = true; engine.Tick(.1, signals); Check(engine.Target(output.Id), "braking turns API target on");
                signals.Braking = false; engine.Tick(.2, signals); Check(!engine.Target(output.Id), "release turns API target off");
            }
            // Reuse a selected output, replacing only its incoming wire; preserve hinge geometry and position.
            e.SelectNode(output.Id); output.Set("openAngle", "37"); int count = f.Nodes.Count;
            Check(e.Template(FlowTemplates.Reload, new[] { Flap, lamp, output.Type == "hinge" ? Flap : new("other", "hinge", "Other flap") }), "selected/free hinge target available");
            Check(f.Nodes.Count(n => n.PartId == Flap.Id) == 1, "never duplicate a part output");
            if (output.Type == "hinge") Check(output.Number("openAngle") == 37 && f.Wires.Count(w => w.To == output.Id) == 1, "selected hinge preserves settings and has one driver");
            Check(FlowJson.Write(FlowJson.Parse(FlowJson.Write(f))) == FlowJson.Write(f), "template persists and reloads");
        }
        var empty = new FlowEditor(new()); int noSaves = 0; empty.Saved = () => noSaves++;
        Check(!empty.Template(FlowTemplates.Reload, Array.Empty<FlowPart>()) && empty.Flow.Rev == 0 && noSaves == 0, "no target is a complete no-op");
        Check(!empty.Template("unknown", parts), "unknown template is a no-op");
        Check(!empty.Template(FlowTemplates.Brake, new[] { Flap }), "brake fallback needs a selected hinge");
        var selected = empty.Place("hinge", 0, 0, Flap)!;
        Check(empty.Template(FlowTemplates.Brake, new[] { Flap }), "brake falls back to selected hinge");
        Check(empty.Flow.Nodes.Count(n => n.PartId == Flap.Id) == 1, "fallback reuses selected output");
        empty.Canvas(); long rev = empty.Flow.Rev;
        Check(!empty.Template(FlowTemplates.Reload, new[] { Flap }) && empty.Flow.Rev == rev, "occupied hinge is not free");
        var free = new FlowPart("free", "hinge", "Free flap");
        Check(empty.Template(FlowTemplates.Reload, new[] { Flap, free }) && empty.Node!.PartId == free.Id, "first free hinge after occupied hinge");
        empty.SelectNode(selected.Id); Check(empty.Template(FlowTemplates.Fired, new[] { free, Flap }), "selection wins over first free hinge");
        Check(empty.Node == selected, "selected hinge retained");
        var readOnly = new FlowEditor(new FlowDocument { Version = 9 });
        Check(!readOnly.Template(FlowTemplates.Reload, parts), "future flow stays read-only");
        // First free API output includes an already placed but unconnected block, excluding masts.
        var e2 = new FlowEditor(new()); var used = e2.Place(lamp.Type, 0, 0, lamp)!;
        Check(e2.Template(FlowTemplates.Brake, parts) && e2.Node == used, "reuse placed free API output");
        e2.Canvas(); var lamp2 = lamp with { Id = "lamp2" };
        Check(e2.Template(FlowTemplates.Brake, new[] { mast, lamp, lamp2 }) && e2.Node!.PartId == lamp2.Id, "skip occupied API output and mast");
    }
    private static void ReloadFollow()
    {
        var report = new List<string> { "mode,cycle,open,closed,applies" };
        foreach (string mode in new[] { "loading", "notReady" })
        {
            var f = new FlowDocument(); var editor = new FlowEditor(f); editor.Template(FlowTemplates.Reload, new[] { Flap });
            var input = f.Nodes.Single(n => n.Type == "cannon.reloading"); input.Set("mode", mode);
            var output = f.Nodes.Single(FlowValidation.IsOutput); f = FlowJson.Parse(FlowJson.Write(f));
            var engine = new FlowEngine(); engine.Configure(f); var sampler = new CannonLoadSampler(); var signals = new Signals();
            float angle = 0; int applies = 0; engine.Apply = (_, _) => applies++;
            double now = 0;
            void Tick(LoadPhase phase, bool shot = false)
            {
                now += .02; signals.Cannons = new[] { sampler.Sample(88, 1, phase, now) };
                signals.Fired = shot ? new[] { new CannonShot(88, 1) } : Array.Empty<CannonShot>();
                engine.Tick(now, signals);
                angle = HingeMath.StepAngle(angle, engine.Target(output.Id) ? 90 : 0, 90, .02f);
            }
            for (int i = 0; i < 20; i++) Tick(LoadPhase.NeverLoaded);
            Check(!engine.Value(input.Id) && angle == 0, mode + " first spawn is not reloading");
            for (int i = 0; i < 20; i++) Tick(LoadPhase.Loaded);
            for (int cycle = 1; cycle <= 5; cycle++)
            {
                Tick(LoadPhase.Unloaded, true);
                Check(!engine.Value(input.Id), "shot alone does not fake a loading level");
                for (int i = 0; i < 100; i++) Tick(LoadPhase.Loading);
                Check(engine.Value(input.Id) && angle == 90, mode + " cycle " + cycle + " reload opens Follow hinge");
                if (mode == "notReady")
                {
                    for (int i = 0; i < 20; i++) Tick(LoadPhase.Unloaded);
                    Check(engine.Value(input.Id) && angle == 90, "notReady remains open during unloaded gap");
                }
                // IO RELOADED is emitted before the level debounce releases; Follow must still close.
                for (int i = 0; i < 100; i++) Tick(LoadPhase.Loaded);
                Check(!engine.Value(input.Id) && angle == 0, mode + " cycle " + cycle + " ready closes Follow hinge");
                Check(applies == 1 + cycle * 2, "initial apply and two transitions per reload; no repeated apply");
                report.Add($"{mode},{cycle},90,0,{applies}");
            }
        }
        Directory.CreateDirectory("logs"); File.WriteAllLines("logs/reload-e2e-0.8.1.csv", report);
        // Default any is OR over the selected group; another loading cannon keeps the flap open.
        var type = new CannonReloadingInput(); var def = new InputInstance(type.Id, new Dictionary<string,string> { ["fireGroup"] = "1" });
        var multi = new Signals { Cannons = new[] { new CannonState(88, 1, false, false, true, 1), new CannonState(89, 1, true, true, false, 0) } };
        Check(type.Read(def, multi).Level, "any group 1 stays on until all matching cannons are ready");
        var specific = new InputInstance(type.Id, new Dictionary<string, string> { ["cannon"] = "vuid:88", ["fireGroup"] = "2" });
        Check(!type.Read(specific, multi).Level, "specific cannon wins over fallback group");
        var fallback = new InputInstance(type.Id, new Dictionary<string, string> { ["cannon"] = "vuid:999", ["fireGroup"] = "1" });
        Check(type.Read(fallback, multi).Level, "missing cannon falls back to group matcher");
        multi.Cannons = new[] { new CannonState(88, 1, false, false, true, 1), new CannonState(89, 2, true, true, false, 0) };
        Check(!type.Read(def, multi).Level, "group 2 loading cannon does not hold group 1 flap");
    }
    private static void Footers()
    {
        IoCatalog.Cannons = new[] { new ParamOption("vuid:88", "Main gun 125 mm") };
        IoCatalog.CannonFireMasks = new Dictionary<string, int> { ["vuid:88"] = 1 };
        var node = new FlowNode("reload", "cannon.reloading"); node.Set("fireGroup", "1"); node.Set("cannon", "any");
        Check(FlowPresentation.Summary(node, new()) == "Reloading - Main gun 125 mm - group 1", "default source resolves readable gun and group");
        node.Set("mode", "notReady"); Check(FlowPresentation.Summary(node, new()).StartsWith("Not ready - Main gun"), "footer exposes mode");
        node.Set("cannon", "vuid:999"); Check(FlowPresentation.Summary(node, new()).Contains("Missing cannon #999; fallback:"), "footer exposes missing selector fallback");
        IoCatalog.Cannons = Array.Empty<ParamOption>(); IoCatalog.CannonFireMasks = new Dictionary<string, int>();
        Check(FlowPresentation.Summary(node, new()).Contains("Any cannon - group 1"), "empty vehicle catalog is safe");
    }
    private static void Panel()
    {
        HingeRuntime.Current = null; UiKit.Context = UiContexts.Designer; Time.unscaledTime = 0;
        HingePanel.Create(new()); UiKit.Poll(); UiKit.Latest.Open(); UiKit.Poll();
        Check(NodeGraph.Latest.Nodes.Count == 0, "all getters safe at creation before graph assignment and without vehicle/selection");
        var f = new FlowDocument(); var e = new FlowEditor(f); e.Template(FlowTemplates.Reload, new[] { Flap });
        var input = f.Nodes.First(); var output = f.Nodes.Last(); int saves = 0;
        HingeRuntime.Current = new() { Flow = f, Parts = new() { Flap }, Save = () => saves++ };
        void Refresh() { Time.unscaledTime += .1f; HingePanel.Update(); UiKit.Poll(); }
        Refresh(); var graph = NodeGraph.Latest; var heldNodes = graph.Nodes.Values.ToArray(); var heldWires = graph.Wires.Values.ToArray();
        void Click(GraphHitKind kind, string? node = null, string? port = null) => graph.Click!(new() { Kind = kind, NodeId = node, PortId = port });
        Click(GraphHitKind.Port, input.Id, "out"); Check(graph.Begins == 1 && graph.Ends == 0, "pending wire begins once");
        // Dragging/saving a different block reconciles selection without restarting an unchanged pending port.
        graph.Move!(output.Id, new(700, 80), true); Refresh();
        Check(graph.Begins == 1 && graph.Ends == 0 && saves == 1, "unchanged pending survives structural sync without API calls");
        int structural = graph.StructuralCalls, flags = graph.FlagWrites;
        for (int i = 0; i < 600; i++) Refresh();
        Check(graph.StructuralCalls == structural && graph.Begins == 1 && graph.Ends == 0, "600 refreshes produce zero add/remove/move/clear/title or pending calls");
        Check(graph.FlagWrites > flags && graph.Nodes.Values.SequenceEqual(heldNodes) && graph.Wires.Values.SequenceEqual(heldWires), "live flags update retained handles");
        Click(GraphHitKind.Port, output.Id, "in0"); Check(graph.Begins == 1 && graph.Ends == 1, "completion ends pending exactly once");
        Click(GraphHitKind.Port, output.Id, "in0"); Check(graph.Begins == 2, "input-first begins once");
        Click(GraphHitKind.Port, output.Id, "in0"); Check(graph.Begins == 2 && graph.Ends == 2, "same port cancels once");
        Click(GraphHitKind.Port, output.Id, "in0"); Click(GraphHitKind.Port, input.Id, "out");
        Check(f.Wires.Count == 1 && graph.Ends == 3, "two-click replacement retains one driver");
        Click(GraphHitKind.Port, input.Id, "out"); Click(GraphHitKind.Canvas); UiKit.Poll();
        Check(graph.Ends == 4, "canvas cancellation calls end once and null-selection getters stay safe");
        Click(GraphHitKind.Node, output.Id); UiKit.Poll();
        Check(UiKit.Buttons.ContainsKey("Shell flap: pulse after firing"), "all three templates are library buttons");
        long rev = f.Rev; UiKit.Buttons["Shell flap: pulse after firing"](); UiKit.Poll();
        Check(f.Rev == rev + 1 && f.Nodes.Any(n => n.Type == "pulse"), "actual panel template button saves complete chain");
        UiKit.Context = UiContexts.Game; Refresh();
        Click(GraphHitKind.Port, input.Id, "out");
        int gameStructure = graph.StructuralCalls, gameBegins = graph.Begins, gameEnds = graph.Ends;
        for (int i = 0; i < 600; i++) Refresh();
        Check(graph.StructuralCalls == gameStructure && graph.Begins == gameBegins && graph.Ends == gameEnds, "600 game refreshes also have zero structural or pending calls");
        Click(GraphHitKind.Canvas);
        UiKit.Context = UiContexts.Designer; Refresh();
        int endsBeforeClose = graph.Ends;
        Click(GraphHitKind.Port, input.Id, "out"); UiKit.Latest.Close();
        Check(graph.Ends == endsBeforeClose + 1, "panel close ends pending once");
        HingeRuntime.Current = null; UiKit.Latest.Open(); Refresh(); UiKit.Poll();
        Check(graph.Nodes.Count == 0, "vehicle removed clears view safely");
        HingePanel.Destroy(); UiKit.Poll(); Check(!HingePanel.IsOpen, "teardown getters remain safe");
        File.WriteAllText("logs/panel-counters-0.8.1.txt", "Actual HingePanel.cs with public API doubles; no Kit renderer simulated.\n600 designer + 600 game 0.1s refreshes: 0 graph structure/title/pending calls; handles retained; live flags updated.\nPending: begin once per selection, end once per completion/cancel/close; drag reconciliation adds 0 pending calls.\nEager label/getter evaluation: safe before graph assignment, no vehicle, no selection, removal and teardown.\n");
    }
}
