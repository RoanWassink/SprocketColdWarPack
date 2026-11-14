using System.Text.Json.Nodes;
using SprocketTelescopicMast;
using SprocketTelescopicMast.Io;

// Pure I/O tests: registry, schema, engine timing, JSON round trip and fire-group bitmask.
internal static class IoTests
{
    private static int checks;
    private static void True(bool condition, string what) { checks++; if (!condition) throw new Exception("FAIL: " + what); }
    private static void Near(double actual, double expected, string what, double tolerance = 1e-4)
    { checks++; if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance) throw new Exception($"FAIL: {what}: {actual} != {expected}"); }
    private static void Throws(Action action, string what)
    { checks++; try { action(); } catch { return; } throw new Exception("FAIL: expected exception: " + what); }

    /// <summary>Test context: per input key the level and pending events.</summary>
    private sealed class FakeWorld
    {
        public readonly Dictionary<string, bool> Levels = new();
        public readonly Dictionary<string, int> Events = new();
        public int FiredMaskEvents(int group, IEnumerable<int> masks) => masks.Count(m => FireGroups.Contains(m, group));
    }
    private sealed class FakeInput : IInputType
    {
        public string Id { get; }
        public string DisplayName => Id;
        public SignalKind SignalKind { get; }
        public ParamSchema Schema { get; } = new(new[] { ParamDef.Int("channel", "Channel", 1, 4, 1) });
        public FakeInput(string id, SignalKind kind) { Id = id; SignalKind = kind; }
        public string Describe(IReadOnlyDictionary<string, string> v) => $"{Id} {v["channel"]}";
        public InputSample Read(InputInstance instance, object? context)
        {
            var world = (FakeWorld)context!;
            world.Levels.TryGetValue(instance.Key, out var level);
            world.Events.TryGetValue(instance.Key, out var events);
            world.Events[instance.Key] = 0; // consumed
            return new InputSample(level, events);
        }
    }
    private sealed class ThrowingInput : IInputType
    {
        public string Id => "test.throws"; public string DisplayName => Id; public SignalKind SignalKind => SignalKind.Event;
        public ParamSchema Schema => ParamSchema.Empty;
        public string Describe(IReadOnlyDictionary<string, string> v) => Id;
        public InputSample Read(InputInstance instance, object? context) => throw new InvalidOperationException("boom");
    }
    private sealed class FakeOutput : IOutputType
    {
        public string Id => "hinge"; public string DisplayName => "Hinge"; public SignalKind SignalKind => SignalKind.Level;
        public ParamSchema Schema { get; } = new(new[]
        {
            ParamDef.Float("closedAngle", "Closed", -180, 180, 0, 5),
            ParamDef.Float("openAngle", "Open", -180, 180, 90, 5),
            ParamDef.Float("speed", "Speed", 5, 720, 90, 15)
        });
        public IReadOnlyList<IoCommand> Commands { get; } = Enum.GetValues<IoCommand>();
        public string Describe(IReadOnlyDictionary<string, string> v) => "Hinge";
    }

    internal static int Run()
    {
        IoRegistry.ClearForTests();
        var ev = new FakeInput("test.event", SignalKind.Event);
        var lv = new FakeInput("test.level", SignalKind.Level);
        IoRegistry.RegisterInput(ev); IoRegistry.RegisterInput(lv); IoRegistry.RegisterInput(new ThrowingInput());
        IoRegistry.RegisterOutput(new FakeOutput());

        // Registry
        True(IoRegistry.FindInput("test.event") == ev, "find input");
        True(IoRegistry.FindInput("missing") == null, "missing input");
        True(IoRegistry.InputTypes.Select(t => t.Id).SequenceEqual(new[] { "test.event", "test.level", "test.throws" }), "registration order");
        Throws(() => IoRegistry.RegisterInput(new FakeInput("test.event", SignalKind.Event)), "duplicate id");
        Throws(() => IoRegistry.RegisterInput(new FakeInput("bad id!", SignalKind.Event)), "invalid id");
        True(IoRegistry.FindOutput("hinge") != null, "find output");

        // Parameter schema
        var i = ParamDef.Int("g", "G", 1, 9, 1);
        True(i.Normalize("12") == "9" && i.Normalize("-3") == "1" && i.Normalize("abc") == "1" && i.Normalize("2.6") == "3", "int clamp/round/default");
        True(i.Nudge("9", 1) == "9" && i.Nudge("1", -1) == "1" && i.Nudge("4", 1) == "5", "int nudge");
        var f = ParamDef.Float("a", "A", -180, 180, 90, 5);
        True(f.Normalize("200") == "180" && f.Normalize("NaN") == "90" && f.Nudge("90", -1) == "85", "float clamp/nudge");
        var b = ParamDef.Bool("x", "X", false);
        True(b.Normalize("1") == "true" && b.Nudge("true", 1) == "false" && b.Normalize("maybe") == "false", "bool");
        var e = ParamDef.Enum("m", "M", new[] { "loading", "notReady" }, "loading");
        True(e.Normalize("other") == "loading" && e.Nudge("notReady", 1) == "loading" && e.Nudge("loading", -1) == "notReady", "enum wrap");
        Throws(() => new ParamSchema(new[] { i, ParamDef.Int("g", "dup", 0, 1, 0) }), "duplicate parameter");
        var schema = new ParamSchema(new[] { i, f });
        var normalized = schema.Normalize(new Dictionary<string, string> { ["g"] = "40", ["future"] = "kept" });
        True(normalized["g"] == "9" && normalized["a"] == "90" && normalized["future"] == "kept", "schema normalize keeps unknown keys");

        // Input identity
        var g1 = new InputInstance("test.event", new Dictionary<string, string> { ["channel"] = "1" });
        var g1b = new InputInstance("test.event", new Dictionary<string, string> { ["channel"] = "1.0" });
        var g2 = new InputInstance("test.event", new Dictionary<string, string> { ["channel"] = "2" });
        True(g1.Equals(g1b) && !g1.Equals(g2) && g1.Key == "test.event{channel=1}", "instance key canonical");
        True(new InputInstance("test.event").GetInt("channel") == 1, "instance defaults");

        // Fire-group bitmask: 5 = groups 1+3, 6 = groups 2+3 (seen in saved blueprints)
        True(FireGroups.Contains(1, 1) && !FireGroups.Contains(1, 2), "mask 1");
        True(FireGroups.Contains(5, 1) && !FireGroups.Contains(5, 2) && FireGroups.Contains(5, 3), "mask 5");
        True(!FireGroups.Contains(6, 1) && FireGroups.Contains(6, 2) && FireGroups.Contains(6, 3), "mask 6");
        True(FireGroups.Contains(0x100, 9) && !FireGroups.Contains(0x100, 8) && !FireGroups.Contains(8, 0), "mask 9 / group 0");
        True(FireGroups.Describe(6) == "2+3" && FireGroups.Describe(0) == "none", "describe mask");
        True(new FakeWorld().FiredMaskEvents(3, new[] { 1, 5, 6, 2 }) == 2, "count shots per group");

        EngineTiming(g1, g2);
        Json(g1);
        Filters();
        Geometry();
        return checks;
    }

    private static void EngineTiming(InputInstance g1, InputInstance g2)
    {
        var world = new FakeWorld();
        var lvl = new InputInstance("test.level");
        // Pulse 2 s: open at shot, closed after 2 s, re-trigger extends.
        var engine = new IoEngine();
        engine.Configure(new[] { ("A", (IEnumerable<Link>)new[] { new Link(g1, IoCommand.Pulse, 0, 2) }) });
        engine.Tick(0, world); True(!engine.IsOpen("A", 0), "pulse idle");
        world.Events[g1.Key] = 1; engine.Tick(1, world); True(engine.IsOpen("A", 1), "pulse opens");
        engine.Tick(2.9, world); True(engine.IsOpen("A", 2.9), "pulse holds");
        engine.Tick(3.0, world); True(!engine.IsOpen("A", 3.0), "pulse ends at 2 s");
        world.Events[g1.Key] = 1; engine.Tick(4, world);
        world.Events[g1.Key] = 1; engine.Tick(5.5, world); // re-trigger during pulse
        True(engine.IsOpen("A", 7.4), "re-trigger extends"); True(!engine.IsOpen("A", 7.5), "extended pulse ends at 5.5+2");
        world.Events[g2.Key] = 1; engine.Tick(10, world); True(!engine.IsOpen("A", 10), "other fire group ignored");

        // Delay: shot at t=1, delay 0.5 -> pulse 1 s from 1.5
        engine = new IoEngine();
        engine.Configure(new[] { ("A", (IEnumerable<Link>)new[] { new Link(g1, IoCommand.Pulse, .5f, 1) }) });
        world.Events[g1.Key] = 1; engine.Tick(1, world); True(!engine.IsOpen("A", 1) && engine.PendingCount == 1, "delay queued");
        engine.Tick(1.49, world); True(!engine.IsOpen("A", 1.49), "not before delay");
        engine.Tick(1.5, world); True(engine.IsOpen("A", 1.5), "applies at delay");
        engine.Tick(2.49, world); True(engine.IsOpen("A", 2.49), "pulse from due time");
        engine.Tick(2.5, world); True(!engine.IsOpen("A", 2.5), "delayed pulse ends");
        // Late tick: due time, not tick time, defines pulse end
        world.Events[g1.Key] = 1; engine.Tick(3, world); engine.Tick(4.2, world); True(!engine.IsOpen("A", 4.6), "late tick keeps due-based end");

        world.Events.Clear();
        // Toggle / Open / Close
        engine = new IoEngine();
        engine.Configure(new[] { ("A", (IEnumerable<Link>)new[] { new Link(g1, IoCommand.Toggle) }), ("B", new[] { new Link(g1, IoCommand.Open), new Link(g2, IoCommand.Close) }) });
        world.Events[g1.Key] = 1; engine.Tick(1, world); True(engine.IsOpen("A", 1) && engine.IsOpen("B", 1), "toggle on + open");
        world.Events[g1.Key] = 1; engine.Tick(2, world); True(!engine.IsOpen("A", 2) && engine.IsOpen("B", 2), "toggle off, open stays");
        // 0.5.0: at most one command per output per tick, so two shots in one frame toggle once.
        world.Events[g1.Key] = 2; engine.Tick(3, world); True(engine.IsOpen("A", 3) && engine.DroppedCommands == 2 /* A toggle + B open */, "two events in one tick toggle once");
        world.Events[g1.Key] = 1; engine.Tick(3.5, world); True(!engine.IsOpen("A", 3.5), "next tick toggles again");
        world.Events[g2.Key] = 1; engine.Tick(4, world); True(!engine.IsOpen("B", 4), "close");
        engine.Command("A", IoCommand.Pulse, 5, 1); True(engine.IsOpen("A", 5.5), "manual pulse");
        engine.Command("A", IoCommand.Toggle, 5.6); True(!engine.IsOpen("A", 5.7), "toggle during pulse closes");
        engine.Command("A", IoCommand.Open, 6); engine.Command("A", IoCommand.Close, 6); True(!engine.IsOpen("A", 6), "manual close");

        world.Events.Clear();
        // Follow a level, with and without delay; rising edge counts as event for a Level input
        engine = new IoEngine();
        engine.Configure(new[] { ("F", (IEnumerable<Link>)new[] { new Link(lvl, IoCommand.Follow) }), ("D", new[] { new Link(lvl, IoCommand.Follow, 1) }), ("P", new[] { new Link(lvl, IoCommand.Pulse, 0, .5f) }) });
        engine.Tick(0, world); True(!engine.IsOpen("F", 0), "follow idle");
        world.Levels[lvl.Key] = true; engine.Tick(1, world);
        True(engine.IsOpen("F", 1) && !engine.IsOpen("D", 1) && engine.IsOpen("P", 1), "follow on, delayed not yet, rising edge pulse");
        engine.Tick(1.6, world); True(!engine.IsOpen("P", 1.6), "level held: no repeated edge");
        engine.Tick(2, world); True(engine.IsOpen("D", 2), "delayed follow on");
        world.Levels[lvl.Key] = false; engine.Tick(3, world);
        True(!engine.IsOpen("F", 3) && engine.IsOpen("D", 3.5), "follow off, delayed still on");
        engine.Tick(4, world); True(!engine.IsOpen("D", 4), "delayed follow off");
        world.Levels[lvl.Key] = true; engine.Tick(5, world); world.Levels[lvl.Key] = false; engine.Tick(5.2, world);
        engine.Tick(6.0, world); True(engine.IsOpen("D", 6.0), "delayed follow replays short level");
        engine.Tick(6.2, world); True(!engine.IsOpen("D", 6.2), "delayed short level ends");
        True(engine.Sample(lvl.Key).Level == false, "lamp sample");

        // Follow on an event input: open for that tick only
        engine = new IoEngine();
        engine.Configure(new[] { ("E", (IEnumerable<Link>)new[] { new Link(g1, IoCommand.Follow) }) });
        world.Events[g1.Key] = 1; engine.Tick(1, world); True(engine.IsOpen("E", 1), "event follow on");
        engine.Tick(1.02, world); True(!engine.IsOpen("E", 1.02), "event follow off next tick");

        world.Events.Clear();
        // Multiple links: one input drives two outputs; two inputs drive one output
        engine = new IoEngine();
        engine.Configure(new[]
        {
            ("A", (IEnumerable<Link>)new[] { new Link(g1, IoCommand.Pulse, 0, 1), new Link(lvl, IoCommand.Follow) }),
            ("B", new[] { new Link(g1, IoCommand.Pulse, .25f, 1) })
        });
        world.Levels[lvl.Key] = false;
        world.Events[g1.Key] = 1; engine.Tick(10, world);
        True(engine.IsOpen("A", 10) && !engine.IsOpen("B", 10), "one input, two outputs");
        engine.Tick(10.3, world); True(engine.IsOpen("B", 10.3), "second output delayed");
        world.Levels[lvl.Key] = true; engine.Tick(11.5, world); True(engine.IsOpen("A", 11.5), "follow keeps A open after pulse");
        world.Levels[lvl.Key] = false; engine.Tick(11.6, world); True(!engine.IsOpen("A", 11.6), "A closes when both inactive");

        // Reconfigure keeps latched state of surviving outputs, drops removed
        engine.Command("A", IoCommand.Open, 12);
        engine.Configure(new[] { ("A", (IEnumerable<Link>)Array.Empty<Link>()) });
        True(engine.IsOpen("A", 12) && !engine.HasOutput("B"), "reconfigure state");

        // A throwing input type never breaks the tick
        engine = new IoEngine();
        engine.Configure(new[] { ("A", (IEnumerable<Link>)new[] { new Link(new InputInstance("test.throws"), IoCommand.Open), new Link(g1, IoCommand.Open) }) });
        world.Events[g1.Key] = 1; engine.Tick(1, world);
        True(engine.IsOpen("A", 1) && engine.InputErrors.ContainsKey("test.throws"), "throwing input isolated");
        // Time never runs backwards
        engine.Tick(0.5, world); True(engine.IsOpen("A", 1), "monotonic");
    }

    private static void Json(InputInstance g1)
    {
        var config = HingeConfig.CreateDefault("Hatch L");
        config.SetOutputParam("openAngle", "120");
        config.SetOutputParam("speed", "9999");
        config.Links.Add(new Link(g1, IoCommand.Pulse, .25f, 2));
        config.Links.Add(new Link(new InputInstance("test.level"), IoCommand.Follow, 0, 0));
        string json = HingeConfigJson.Write(config);
        var back = HingeConfigJson.Parse(json);
        True(back.Name == "Hatch L" && back.GetOutputFloat("openAngle", 0) == 120 && back.GetOutputFloat("speed", 0) == 720, "round trip output params (clamped)");
        True(back.Links.Count == 2 && back.Links[0].Input.Equals(g1) && back.Links[0].Command == IoCommand.Pulse &&
             Math.Abs(back.Links[0].DelaySeconds - .25f) < 1e-6 && back.Links[0].DurationSeconds == 2 && back.Links[1].Command == IoCommand.Follow, "round trip links");
        True(HingeConfigJson.Write(back) == json, "stable serialization");
        True(json.Contains("\"Version\":1") && json.Contains("\"fireGroup\"") == false, "version field present");

        // Unknown input type / command / top-level fields are preserved, not discarded
        var foreign = "{\"Version\":1,\"Name\":\"X\",\"Future\":{\"a\":1},\"Output\":{\"Type\":\"hinge\",\"Params\":{\"openAngle\":45}},\"Links\":[" +
            "{\"Input\":{\"Type\":\"other.mod.trigger\",\"Params\":{\"zone\":\"north\"}},\"Command\":\"Open\",\"Delay\":0,\"Duration\":0}," +
            "{\"Input\":{\"Type\":\"test.event\",\"Params\":{\"channel\":3}},\"Command\":\"Wobble\",\"Delay\":0,\"Duration\":0}," +
            "{\"Input\":{\"Type\":\"test.event\",\"Params\":{\"channel\":2}},\"Command\":\"Toggle\",\"Delay\":1,\"Duration\":0}]}";
        var parsed = HingeConfigJson.Parse(foreign);
        True(parsed.Links.Count == 1 && parsed.PreservedLinks.Count == 2 && parsed.Links[0].Input.GetInt("channel") == 2, "unknown links kept aside");
        var rewritten = HingeConfigJson.Write(parsed);
        var root = JsonNode.Parse(rewritten)!.AsObject();
        True(rewritten.Contains("other.mod.trigger") && rewritten.Contains("north") && rewritten.Contains("Wobble"), "unknown links written back");
        True(root["Future"]?["a"]?.GetValue<int>() == 1, "unknown top-level kept");
        True(parsed.GetOutputFloat("openAngle", 0) == 45 && parsed.GetOutputFloat("closedAngle", 99) == 0, "missing output params default");

        // Newer version: kept verbatim and read-only
        var newer = "{\"Version\":7,\"Name\":\"N\",\"Anything\":[1,2]}";
        var future = HingeConfigJson.Parse(newer);
        True(future.ReadOnly && future.Name == "N" && HingeConfigJson.Write(future) == newer, "future version preserved");
        // Garbage never throws
        var bad = HingeConfigJson.Parse("{not json");
        True(bad.Links.Count == 0 && !bad.ReadOnly, "corrupt json defaults");
        True(HingeConfigJson.Parse("").Links.Count == 0 && HingeConfigJson.Parse(null).Links.Count == 0, "empty json");
        // Clamped link timing
        var link = new Link(g1, IoCommand.Pulse, -5, 1000);
        True(link.DelaySeconds == 0 && link.DurationSeconds == Link.MaxSeconds, "link time clamp");
        True(config.Clone().Links.Count == 2, "clone");
    }

    private static void Filters()
    {
        var filter = new LevelFilter(.1);
        True(!filter.Update(true, 0) && !filter.Update(true, .05) && filter.Update(true, .1), "level on after 0.1 s");
        True(filter.Update(false, .12) && filter.Update(true, .15) && filter.Update(true, .3), "short drop ignored");
        True(filter.Update(false, .4) && !filter.Update(false, .51), "off after 0.1 s");
        var dup = new DuplicateFilter(.15);
        True(dup.Accept(1, 0) && !dup.Accept(1, .1) && dup.Accept(2, .1) && dup.Accept(1, .15) && !dup.Accept(1, .29) && dup.Accept(1, .31), "150 ms duplicate filter per source");
    }

    private static void Geometry()
    {
        Near(HingeMath.LeafVolume, .5 * .02 * .375 + Math.PI * (.025 * .025 - .02 * .02) * .5, "leaf volume");
        Near(HingeMath.Mass(HingeMath.LeafVolume, 1, 1, 1), 32.21, "leaf mass at scale 1", .05);
        Near(HingeMath.Mass(HingeMath.BaseVolume, 1, 1, 1), 11.65, "base mass at scale 1", .05);
        Near(HingeMath.Mass(HingeMath.LeafVolume, 2, 1, .5f), HingeMath.Mass(HingeMath.LeafVolume, 1, 1, 1), "mass scales with volume");
        Near(HingeMath.StepAngle(0, 90, 90, .05f), 4.5, "step 50 ms");
        Near(HingeMath.StepAngle(85, 90, 90, .1f), 90, "no overshoot");
        Near(HingeMath.StepAngle(90, -90, 90, 10), 81, "dt capped at 0.1 s");
        Near(HingeMath.StepAngle(float.NaN, 30, 90, .1f), 9, "NaN angle sanitized");
        Near(HingeMath.StepAngle(10, 30, float.NaN, .1f), 10, "invalid speed holds");
        Near(HingeMath.SanitizeAngle(500), 180, "angle clamp");
        // Positive hinge angle lifts the +Z plate: rotate (0,0,1) about +X by LocalRotationDegrees(90)
        double r = HingeMath.LocalRotationDegrees(90) * Math.PI / 180;
        Near(-Math.Sin(r), 1, "plate tip goes up (y)"); Near(Math.Cos(r), 0, "plate tip z");
        float angle = 0; for (int k = 0; k < 200; k++) angle = HingeMath.StepAngle(angle, 90, 90, .01f);
        Near(angle, 90, "reaches open"); for (int k = 0; k < 200; k++) angle = HingeMath.StepAngle(angle, 0, 90, .01f);
        Near(angle, 0, "returns closed without drift");
    }
}
