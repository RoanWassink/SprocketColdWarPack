using System.Text.Json;
using SprocketTelescopicMast.Io;

internal static class FlowTests
{
    private static int checks;
    private static void Check(bool b, string message) { checks++; if (!b) throw new Exception("FLOW: " + message); }
    internal sealed class Source : IInputType
    {
        public string Id { get; init; } = "test.flow";
        public string DisplayName => Id;
        public SignalKind SignalKind { get; init; } = SignalKind.Level;
        public ParamSchema Schema { get; init; } = ParamSchema.Empty;
        public Func<InputInstance, InputSample> Sample = _ => InputSample.Off;
        public int Reads;
        public InputSample Read(InputInstance i, object? context) { Reads++; return Sample(i); }
        public string Describe(IReadOnlyDictionary<string, string> p) => Id;
    }
    private static (FlowDocument F, FlowEngine E, Source A, Source B, FlowNode Logic, FlowNode Output) Graph(string type, string mode = "Follow")
    {
        IoRegistry.ClearForTests(); var a = new Source { Id = "test.a" }; var b = new Source { Id = "test.b" }; IoRegistry.RegisterInput(a); IoRegistry.RegisterInput(b);
        var f = new FlowDocument(); var an = new FlowNode("a", a.Id); var bn = new FlowNode("b", b.Id); var n = new FlowNode("logic", type); var o = new FlowNode("out", "hinge");
        o.Set("partId", "part"); o.Set("mode", mode); f.Nodes.AddRange(new[] { o, n, an, bn });
        f.Wires.Add(new("a", "logic", 0)); if (FlowValidation.Ports(n) == 2) f.Wires.Add(new("b", "logic", 1)); f.Wires.Add(new("logic", "out", 0));
        var e = new FlowEngine(); e.Configure(f); return (f, e, a, b, n, o);
    }
    private static void Level(Source s, bool value) => s.Sample = _ => new InputSample(value, 0);
    public static int Run()
    {
        var clock = new FlowGameClock(); clock.Advance(.25); clock.Advance(100, true); clock.Advance(double.NaN); Check(clock.Now == .25, "paused/non-finite frames do not age game clock"); clock.Advance(.25); Check(clock.Now == .5, "clock resumes in game time");
        foreach (string kind in new[] { "and", "or", "not" })
        {
            var g = Graph(kind); double t = 0;
            foreach (bool a in new[] { false, true }) foreach (bool b in new[] { false, true })
            { Level(g.A, a); Level(g.B, b); g.E.Tick(t++, null); Check(g.E.Target("out") == (kind == "and" ? a && b : kind == "or" ? a || b : !a), kind + " truth table / topology"); }
        }
        {
            var g = Graph("delay"); g.Logic.Set("t", "0.5");
            Level(g.A, true); g.E.Tick(0, null); Check(!g.E.Target("out"), "delay waits");
            Level(g.A, false); g.E.Tick(.1, null); g.E.Tick(.5, null); Check(g.E.Target("out"), "short event survives transport delay");
            g.E.Tick(.61, null); Check(!g.E.Target("out"), "falling edge delayed too");
        }
        {
            var g = Graph("pulse"); g.Logic.Set("t", "1"); Level(g.A, true); g.E.Tick(0, null); Check(g.E.Target("out"), "pulse begins");
            g.E.Tick(.8, null); g.E.Tick(1, null); Check(!g.E.Target("out"), "held trigger does not retrigger");
            Level(g.A, false); g.E.Tick(1.1, null); Level(g.A, true); g.E.Tick(1.2, null); Check(g.E.Target("out"), "second rising edge retriggers");
            Level(g.A, false); g.E.Tick(1.3, null); Level(g.A, true); g.E.Tick(1.4, null); g.E.Tick(2.3, null); Check(g.E.Target("out"), "active pulse extends from latest rising edge");
            g.E.Tick(2.5, null); Check(!g.E.Target("out"), "extended pulse expires");
        }
        {
            var g = Graph("counter"); g.Logic.Set("n", "2"); Level(g.A, true); g.E.Tick(0, null); g.E.Tick(.1, null); Check(!g.E.Target("out"), "counter counts edges");
            Level(g.A, false); g.E.Tick(.2, null); Level(g.A, true); g.E.Tick(.3, null); Check(g.E.Target("out"), "counter threshold");
            Level(g.B, true); g.E.Tick(.4, null); Check(!g.E.Target("out"), "counter reset");
        }
        {
            var g = Graph("memory"); Level(g.A, true); g.E.Tick(0, null); Check(g.E.Target("out"), "memory set");
            Level(g.A, false); g.E.Tick(.1, null); Check(g.E.Target("out"), "memory holds");
            Level(g.A, true); Level(g.B, true); g.E.Tick(.2, null); Check(!g.E.Target("out"), "memory reset dominates simultaneous edges");
            Level(g.B, false); g.E.Tick(.3, null); Check(!g.E.Target("out"), "held set does not retrigger");
        }
        foreach (var mode in new[] { "Follow", "Toggle", "Open", "Close" })
        {
            var g = Graph("or", mode); int applied = 0; g.E.Apply = (_, _) => applied++;
            Level(g.A, true); g.E.Tick(0, null); Check(g.E.Target("out") == (mode != "Close"), mode + " first edge");
            g.E.Tick(.1, null); Check(applied == 1, mode + " apply initial / no repeat");
            Level(g.A, false); g.E.Tick(.2, null); Check(g.E.Target("out") == (mode is "Open" or "Toggle"), mode + " release");
            Level(g.A, true); g.E.Tick(.3, null); Check(g.E.Target("out") == (mode is "Follow" or "Open"), mode + " next edge");
            Check(applied == (mode == "Follow" ? 3 : mode == "Toggle" ? 2 : 1), mode + " changed-only callbacks");
        }
        {
            var g = Graph("or", "Open"); Level(g.A, true); g.E.Tick(0, null); g.Output.Set("mode", "Close"); Level(g.A, false); g.E.Tick(.1, null);
            Check(g.E.Target("out"), "Close mode holds an open output before trigger"); Level(g.A, true); g.E.Tick(.2, null); Check(!g.E.Target("out"), "Close edge closes an open output");
        }
        {
            var g = Graph("memory"); g.F.Wires.RemoveAll(w => w.To == "logic" && w.Port == 1); g.F.Wires.Add(new("logic", "logic", 1)); g.E.Configure(g.F);
            Level(g.A, true); g.E.Tick(0, null); Check(g.E.Loop && g.E.Value("logic"), "memory cycle LOOP / initial set");
            g.E.Tick(.1, null); Check(!g.E.Value("logic"), "cycle reads previous tick");
            for (int i = 2; i < 100; i++) g.E.Tick(i * .1, null); Check(g.E.Loop, "cycle cannot hang");
        }
        {
            var g = Graph("or"); int warnings = 0; g.E.LoopDetected = (_, _) => warnings++;
            for (int i = 0; i < 12; i++) { Level(g.A, i % 2 == 0); g.E.Tick(i * .04, null); }
            Check(warnings == 1 && g.E.Paused("out", .5), "11th change pauses for 2s / one warning");
            Level(g.A, false); g.E.Tick(1, null); Check(g.E.Paused("out", 2.3), "still paused");
            g.E.Tick(2.5, null); Check(!g.E.Paused("out", 2.5) && !g.E.Target("out"), "resumes after cooldown");
            int reads = g.A.Reads; Level(g.A, true); g.E.Tick(2.5, null); g.E.Tick(2, null); Check(g.A.Reads == reads, "pause/backwards clock cannot advance engine");
        }
        JsonAndStorage();
        {
            var g = Graph("or"); g.E.PartExists = _ => false; int applies = 0; g.E.Apply = (_, _) => applies++; Level(g.A, true); g.E.Tick(0, null);
            Check(g.E.Status["out"] == "missing part" && applies == 0, "missing part is retained and never executed");
            g.E.PartExists = _ => true; g.E.Tick(1, null); Check(applies == 1 && g.E.Target("out"), "part returns / initial state");
        }
        {
            IoRegistry.ClearForTests(); var ev = new Source { Id = "test.ev", SignalKind = SignalKind.Event }; IoRegistry.RegisterInput(ev);
            var f = new FlowDocument(); f.Nodes.Add(new("e", ev.Id)); f.Nodes.Add(new("e2", ev.Id)); var e = new FlowEngine(); e.Configure(f);
            ev.Sample = _ => new InputSample(false, 4); e.Tick(0, null); Check(e.Value("e") && e.Value("e2") && ev.Reads == 1, "event input one sample / fan-out");
            ev.Sample = _ => InputSample.Off; e.Tick(1, null); Check(!e.Value("e"), "event lasts exactly one tick");
        }
        var control = new FlowMastControl(); control.Flow(true); control.Manual(-1); control.Flow(true); Check(control.Direction == -1, "manual wins over unchanged flow");
        control.Flow(false); control.Flow(true); Check(control.Direction == 1, "new flow command wins"); control.ReleaseManual(); Check(control.Direction == 0, "key release stops at height");
        control.Flow(false); control.Unbind(); Check(control.Direction == 0, "removing mast block stops flow motion"); control.Manual(1); control.Unbind(); Check(control.Direction == 1, "unplaced mast keeps manual controls");
        return checks;
    }
    private static void JsonAndStorage()
    {
        string rawNode = "{ \"id\":\"u\", \"type\":\"future.block\",\"params\":{\"str\":\"a\\nb\",\"n\":1e+03},\"future\":[3,2,1] }";
        string raw = "{\"v\":1,\"rev\":7,\"nodes\":[" + rawNode + "],\"wires\":[],\"future\":{ \"value\": 1e+02 }}";
        var f = FlowJson.Parse(raw); string back = FlowJson.Write(f);
        Check(back.Contains(rawNode) && back.Contains("{ \"value\": 1e+02 }"), "unknown node / root fields literally preserved");
        Check(FlowJson.Write(FlowJson.Parse(back)) == back, "JSON stable roundtrip"); f.Edited(); Check(f.Rev == 8, "edit increments rev");
        int warnings = 0; var store = new FlowStorage { Warning = _ => warnings++ }; string id = Guid.NewGuid().ToString();
        string first = store.Attach("a", id, f); string second = store.Attach("b", id, new FlowDocument { Rev = 9 });
        Check(first == id && second != id && Guid.TryParse(second, out _), "duplicate id becomes new unplaced part");
        Check(store.Attach("b", id, null) == second && warnings == 2, "duplicate/copy warnings once / idempotent load");
        Check(store.Flow!.Rev == 9, "highest revision wins");
        store.Attach("b", id, new FlowDocument { Rev = 1 }); Check(store.Flow!.Rev == 9, "backup load cannot replace newer copy");
        var a = new FlowDocument { Rev = 10 }; var b = new FlowDocument { Rev = 10 }; b.Extra["tie"] = JsonDocument.Parse("true").RootElement.Clone();
        var order1 = new FlowStorage(); order1.Attach("z", null, b); order1.Attach("a", null, a);
        var order2 = new FlowStorage(); order2.Attach("a", null, a); order2.Attach("z", null, b);
        Check(FlowJson.Write(order1.Flow!) == FlowJson.Write(order2.Flow!), "equal revisions use stable order independent of arrival");
        store.Detach("a"); Check(!store.Contains(id) && store.Flow!.Rev == 9, "missing part keeps complete graph");
        var g = Graph("or"); g.F.Connect("b", "out", 0); Check(g.F.Wires.Count(w => w.To == "out") == 1 && g.F.Rev == 1, "connection replaces existing input wire");
        g.F.Wires.Add(new("a", "out", 0)); Check(FlowValidation.Validate(g.F).Contains("More than one wire per input."), "duplicate input validation");
        g.F.Wires.Add(new("a", "out", 8)); Check(FlowValidation.Validate(g.F).Contains("Unknown input port."), "unknown input port validation");
        var envelope = FlowJson.Envelope(second, f); var decoded = FlowJson.ReadEnvelope(envelope); Check(decoded.PartId == second && FlowJson.Write(decoded.Flow) == FlowJson.Write(f), "API envelope roundtrip");
        f.Nodes[0].Data["title"] = "Edited"; Check(FlowJson.Write(f).Contains("{\"str\":\"a\\nb\",\"n\":1e+03}"), "unknown nested payload remains literal after known field edit");
        var future = FlowJson.Parse(raw.Replace("\"v\":1", "\"v\":2")); var futureEngine = new FlowEngine(); futureEngine.Configure(future); futureEngine.Tick(0, null); Check(future.ReadOnly && !futureEngine.Value("u"), "future format preserved without execution");
        var edits = new FlowDocument(); var newNode = new FlowNode("new", "not"); edits.AddNode(newNode); edits.MoveNode("new", 10, 20); edits.SetParam("new", "test", "kept"); edits.RemoveNode("new"); Check(edits.Rev == 4 && edits.Nodes.Count == 0, "model edit operations each bump revision");
        IoApiCore.ClearForTests(); object vehicle = new(), part = new(); string? saved = null;
        IoApiCore.TryRegisterOutput("test.flow.out", "Output", "component", (_, _) => { }, _ => saved, (_, json) => saved = json, "On", "Off", out _, out _);
        Check(IoApiCore.TrySaveRules("test.flow.out", vehicle, part, envelope) && IoApiCore.TryLoadRules("test.flow.out", vehicle, part, out var loaded) && loaded == envelope, "actual API saveRules/loadRules callbacks preserve envelope");
    }
}
