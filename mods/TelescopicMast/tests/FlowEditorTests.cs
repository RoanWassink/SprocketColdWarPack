using SprocketTelescopicMast.Io;

internal static class FlowEditorTests
{
    private static int checks;
    private static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception("EDITOR: " + text); }
    public static int Run()
    {
        IoRegistry.ClearForTests();
        var level = new FlowTests.Source { Id = "api.level", Sample = _ => throw new Exception("Designer must not read a real part") };
        var events = new FlowTests.Source { Id = "api.event", SignalKind = SignalKind.Event, Sample = _ => throw new Exception("Designer must not read events") };
        IoRegistry.RegisterInput(level); IoRegistry.RegisterInput(events);
        var f = new FlowDocument(); int saves = 0;
        var storage = new FlowStorage();
        string partId = Guid.NewGuid().ToString("D");
        storage.Attach("a", partId, null); storage.Attach("b", Guid.NewGuid().ToString("D"), null);
        var editor = new FlowEditor(f) { Saved = () => { saves++; storage.Publish(f); } };
        var a = editor.Place(level.Id, 0, 0)!; var b = editor.Place(events.Id, 0, 120)!;
        var logic = editor.Place("counter", 240, 0)!;
        var part = new FlowPart(partId, "hinge", "Shell flap"); var output = editor.Place("hinge", 480, 0, part)!;
        Check(f.Rev == 4 && saves == 4, "each placement commits exactly one revision");
        Check(logic.Get("n") == "3", "logic defaults"); Check(output.Get("mode") == "Follow", "output default mode");
        Check(editor.SelectedNode == output.Id && editor.SelectedWire == null, "placement selects node");
        Check(editor.Place("hinge", 1, 2, part) == output && f.Rev == 4 && saves == 4, "placed output selects existing block without saving");
        var aOut = new FlowPort(a.Id, 0, true); var bOut = new FlowPort(b.Id, 0, true); var countIn = new FlowPort(logic.Id, 0, false);
        Check(!editor.ClickPort(aOut) && editor.Pending == aOut && editor.Hint!.Contains("input port"), "output click starts pending and hint");
        Check(f.Rev == 4 && saves == 4, "pending is not persisted");
        Check(!editor.ClickPort(aOut) && editor.Pending == null, "same port cancels");
        editor.ClickPort(aOut); editor.Canvas(); Check(editor.Pending == null && editor.Node == null && editor.SelectedWire == null, "canvas cancels and clears selection");
        editor.ClickPort(aOut); editor.ClickPort(bOut); Check(editor.Pending == bOut, "same direction changes chosen port");
        Check(editor.ClickPort(countIn) && f.Wires.Count == 1, "output-first connection");
        Check(editor.SelectedWire == f.Wires[0] && editor.Node == null && editor.Pending == null, "connection selects wire exclusively and clears pending");
        Check(f.Rev == 5 && saves == 5, "wire increments revision once");
        editor.ClickPort(countIn); Check(editor.Hint!.Contains("output port"), "input-first hint");
        Check(editor.ClickPort(aOut) && f.Wires.Count == 1 && f.Wires[0].From == a.Id, "input-first replaces occupied input");
        Check(f.Rev == 6 && saves == 6, "replacement saves once");
        editor.ClickPort(new(logic.Id, 0, true));
        Check(!editor.ClickPort(new(logic.Id, 1, false)) && f.Wires.Count == 1 && f.Rev == 6, "no self wire, pending remains available");
        editor.Cancel();
        Check(!editor.ClickPort(new(output.Id, 0, true)) && editor.Pending == null, "outputs have no outgoing port");
        Check(!editor.ClickPort(new(logic.Id, 2, false)) && editor.Pending == null, "reject nonexistent input port");
        Check(!editor.ClickPort(new("absent", 0, true)) && editor.Pending == null, "reject nonexistent node");
        editor.Move(a.Id, 80, 64, false); Check(f.Rev == 6 && FlowPresentation.Coordinate(a, "x") == 0, "live drag does not save");
        editor.Move(a.Id, 80, 64, true); Check(f.Rev == 7 && saves == 7 && FlowPresentation.Coordinate(a, "x") == 80, "completed drag saves exact final coordinates once");
        editor.Move(a.Id, 80, 64, true); Check(f.Rev == 7, "unchanged drag does not save");
        editor.Rename(a.Id, "Brake"); editor.Parameter(logic.Id, "n", "2"); Check(f.Rev == 9 && saves == 9 && FlowPresentation.Title(a) == "Brake", "name and parameter edits save");
        editor.Rename(a.Id, "Brake"); editor.Parameter(logic.Id, "n", "2"); Check(f.Rev == 9 && saves == 9, "no-op fields do not save");
        Check(ReferenceEquals(storage.Flow, f), "editor publishes into phase-A storage");
        storage.Attach("a", partId, FlowJson.Parse("{\"v\":1,\"rev\":1,\"nodes\":[],\"wires\":[]}"));
        Check(ReferenceEquals(storage.Flow, f), "published copies resist delayed stale load");
        string json = FlowJson.Write(f); Check(!json.Contains("zoom") && !json.Contains("pan") && !json.Contains("pending") && !json.Contains("selected"), "view and selection are not persisted");
        editor.SelectWire(f.Wires[0]); editor.Delete(); Check(f.Rev == 10 && f.Wires.Count == 0 && editor.SelectedWire == null, "delete wire saves and clears selection");
        editor.ClickPort(aOut); editor.ClickPort(countIn); editor.SelectNode(a.Id);
        Check(editor.Node == a && editor.SelectedWire == null, "node selection excludes wire");
        editor.Delete(); Check(f.Rev == 12 && f.Nodes.All(n => n.Id != a.Id) && f.Wires.Count == 0, "delete node also removes incident wires with one revision");
        int before = saves; editor.Delete(); Check(saves == before, "empty delete no save");
        Check(FlowValidation.Validate(f).Count == 0, "editor produces valid graph");
        var future = FlowJson.Parse("{\"v\":9,\"rev\":10,\"nodes\":[],\"wires\":[]}"); var readonlyEditor = new FlowEditor(future);
        Check(readonlyEditor.Place("and", 0, 0) == null && future.Rev == 10, "future flow cannot be edited");
        // A fresh graph proves the sandbox never invokes registered reads or real output adapters.
        f = new(); editor = new(f); a = editor.Place(level.Id, 0, 0)!; b = editor.Place(events.Id, 0, 80)!;
        logic = editor.Place("counter", 240, 80)!; output = editor.Place("hinge", 480, 0, part)!;
        f.Connect(a.Id, output.Id, 0); f.Connect(b.Id, logic.Id, 0);
        var preview = new FlowPreview(); preview.Configure(f, _ => true); preview.Tick(0);
        Check(level.Reads == 0 && events.Reads == 0, "sandbox suppresses every real callback");
        preview.Test(a); preview.Tick(.1); Check(preview.Held(a.Id) && preview.Engine.Target(output.Id), "Hold drives simulated output");
        Check(preview.Position(output.Id) > 0, "simulated angle moves");
        preview.Test(b); preview.Tick(.2); Check(preview.Engine.Value(b.Id) && preview.Engine.Count(logic.Id) == 1, "Fire emits one simulated event");
        preview.Tick(.3); Check(!preview.Engine.Value(b.Id) && preview.Engine.Count(logic.Id) == 1, "event lasts one tick");
        preview.Test(a); preview.Tick(.4); Check(!preview.Held(a.Id) && !preview.Engine.Target(output.Id), "Release clears simulated level");
        long revision = f.Rev; preview.Test(b); preview.Tick(.5); Check(f.Rev == revision, "tests never edit or save the graph");
        Check(FlowPresentation.Summary(logic, preview.Engine) == "Count 2 / 3", "live counter summary");
        preview.Configure(f, _ => false); preview.Tick(.6); Check(preview.Engine.Status[output.Id] == "missing part", "missing part is not simulated");
        var delay = new FlowNode("delay", "delay"); delay.Set("t", "0.5");
        Check(FlowPresentation.Summary(delay, preview.Engine) == "Delay 0.5 s", "delay footer");
        Check(FlowPresentation.Summary(output, new FlowEngine(), 37) == "Follow - 37 deg", "ASCII hinge footer");
        Check(FlowPresentation.Parameters(delay).Single().Step == .1, "delay stepper follows design");
        // Reassigning a block to an unplaced part must apply once even if its target did not change.
        var engine = new FlowEngine(); int applies = 0; engine.Apply = (_, _) => applies++;
        engine.InputOverride = _ => InputSample.Off; engine.Configure(f); engine.Tick(0, null);
        output.Set("partId", Guid.NewGuid().ToString("D")); engine.Configure(f); engine.Tick(.1, null);
        Check(applies == 2, "changing output part reapplies its initial target");
        // An event between two UI polls remains visible, but cannot retrigger downstream logic.
        var eventGraph = new FlowDocument(); var eventNode = new FlowNode("event", events.Id); eventGraph.Nodes.Add(eventNode);
        var eventEngine = new FlowEngine(); bool fire = true;
        eventEngine.InputOverride = _ => new InputSample(false, fire ? 1 : 0); eventEngine.Configure(eventGraph);
        eventEngine.Tick(1, null); fire = false; eventEngine.Tick(1.02, null);
        Check(!eventEngine.Value(eventNode.Id) && eventEngine.DisplayValue(eventNode.Id), "10 Hz display catches an event between polls without extending evaluation");
        eventEngine.Tick(1.11, null); Check(!eventEngine.DisplayValue(eventNode.Id), "display pulse expires in game time");
        // Feedback inputs read the simulated output position, never the real vehicle snapshot.
        IoRegistry.RegisterInput(new HingeStateInput()); IoRegistry.RegisterInput(new MastStateInput());
        f = new(); editor = new(f); a = editor.Place(level.Id, 0, 0)!;
        part = part with { Selector = PartSelector.ForVuid(12, "Shell flap") };
        output = editor.Place("hinge", 400, 0, part)!; f.Connect(a.Id, output.Id, 0);
        var feedback = editor.Place("hinge.state", 600, 0)!; feedback.Set("hinge", part.Selector);
        preview = new(); preview.Configure(f, _ => true, new[] { part }); preview.Tick(0); preview.Test(a);
        for (int i = 1; i <= 12; i++) preview.Tick(i * .1);
        Check(preview.Position(output.Id) == 90 && preview.Engine.Value(feedback.Id), "hinge feedback follows simulated angle");
        preview.Test(a); for (int i = 13; i <= 24; i++) preview.Tick(i * .1);
        feedback.Set("state", "closed"); preview.Configure(f, _ => true, new[] { part }); preview.Tick(2.5);
        Check(preview.Engine.Value(feedback.Id), "closed feedback after simulated release");
        var mast = editor.Place("mast", 400, 200, new FlowPart("mast", "mast", "Mast"))!;
        var mastFeedback = editor.Place("mast.state", 600, 200)!; mastFeedback.Set("state", "raised"); f.Connect(a.Id, mast.Id, 0);
        preview.Configure(f, _ => true, new[] { part, new FlowPart("mast", "mast", "Mast") }); preview.Test(a); preview.Tick(2.6); preview.Tick(2.7);
        Check(preview.Engine.Value(mastFeedback.Id), "mast feedback follows simulated extension");
        var absent = new FlowNode("absent", "missing.api"); absent.Set("partId", "removed-part");
        f.Nodes.Add(absent); var missingEngine = new FlowEngine { PartExists = _ => false }; missingEngine.Configure(f); missingEngine.Tick(0, null);
        Check(FlowValidation.IsOutput(absent) && FlowValidation.Ports(absent) == 1 && !FlowEditor.HasOutput(absent), "missing API output retains its output tag and input port");
        Check(missingEngine.Status[absent.Id] == "missing part", "absent provider leaves a missing-part output rather than an input block");
        return checks;
    }
}
