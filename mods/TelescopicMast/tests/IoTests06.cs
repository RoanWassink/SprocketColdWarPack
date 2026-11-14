using SprocketTelescopicMast.Io;

// 0.6.0: rule sentences derived from the registry (source, event, command), sentence edits,
// the default rule, templates, live signals, the description fallback and unchanged 0.5 configs.
internal static class IoTests06
{
    private static int checks;
    private static void True(bool condition, string what) { checks++; if (!condition) throw new Exception("FAIL: " + what); }
    private static void Eq(string actual, string expected, string what) { checks++; if (actual != expected) throw new Exception($"FAIL: {what}: '{actual}' != '{expected}'"); }

    private sealed class Output : IOutputType
    {
        public string Id => "hinge"; public string DisplayName => "Hinge"; public SignalKind SignalKind => SignalKind.Level;
        public ParamSchema Schema { get; } = new(new[]
        {
            ParamDef.Float("closedAngle", "Closed angle", -180, 180, 0, 5),
            ParamDef.Float("openAngle", "Open angle", -180, 180, 90, 5),
            ParamDef.Float("speed", "Speed deg/s", 5, 720, 90, 15)
        });
        public IReadOnlyList<IoCommand> Commands { get; } = Enum.GetValues<IoCommand>();
        public string Describe(IReadOnlyDictionary<string, string> v) => "Hinge";
    }
    private sealed class NewInput : IInputType
    {
        public NewInput(string id, string name, SignalKind kind) { Id = id; DisplayName = name; SignalKind = kind; }
        public string Id { get; }
        public string DisplayName { get; }
        public SignalKind SignalKind { get; }
        public ParamSchema Schema { get; } = new(new[] { ParamDef.Int("count", "Count", 1, 4, 2), CannonParams.FireGroup(), CannonParams.Cannon() });
        public string Describe(IReadOnlyDictionary<string, string> v) => DisplayName;
        public InputSample Read(InputInstance instance, object? context) => InputSample.Off;
    }

    private static InputInstance In(string type, params (string Key, string Value)[] p) => new(type, p.ToDictionary(x => x.Key, x => x.Value));
    private static Link L(IoCommand c, string type, params (string Key, string Value)[] p) => new(In(type, p), c);
    private static string Sentence(Link l) { var v = RuleModel.Derive(l); return $"{v.Source.Label} | {v.Event.Label} | {v.Command}"; }

    internal static int Run()
    {
        // Same registration as Plugin.LoadHinge: types in this order, then the sentence descriptions.
        IoRegistry.ClearForTests();
        foreach (var t in new IInputType[] { new CannonFiredInput(), new CannonReloadingInput(), new CannonReloadedInput(),
                     new CannonLoadedInput(), new HingeStateInput(), new MastStateInput(), new KeyInput() })
            IoRegistry.RegisterInput(t);
        IoRegistry.RegisterOutput(new Output());
        BuiltinRules.Register();
        Catalog(true);

        Derivation();
        Edits();
        DefaultAndTemplates();
        Signals();
        NewTypes();
        Config05();
        Catalog(false);
        return checks;
    }

    private static void Catalog(bool on)
    {
        IoCatalog.Cannons = on ? new[] { new ParamOption("vuid:10", "Cannon 1 – 75 mm (group 1)"), new ParamOption("vuid:11", "Cannon 2 – 20 mm (group 2)") } : Array.Empty<ParamOption>();
        IoCatalog.CannonFireGroups = on ? new Dictionary<string, int> { ["vuid:10"] = 1, ["vuid:11"] = 2 } : new Dictionary<string, int>();
        IoCatalog.Hinges = on ? new[] { new ParamOption("vuid:20:Hatch", "Hatch (Leaf)") } : Array.Empty<ParamOption>();
    }

    private static void Derivation()
    {
        True(RuleModel.Groups().SequenceEqual(new[] { BuiltinRules.Cannons, BuiltinRules.Hinges, BuiltinRules.Mast, BuiltinRules.Keys }), "groups in registration order");
        var cannons = RuleModel.SourcesOf(BuiltinRules.Cannons);
        True(cannons.Count == 3 + FireGroups.Max && cannons[0].Label.StartsWith("Cannon 1") && cannons[1].Label.StartsWith("Cannon 2")
             && cannons[2].Label == "Any cannon" && cannons[3].Label == "Any cannon in group 1" && cannons[11].Label == "Any cannon in group 9", "cannon sources: specific first, then any-in-group 1-9");
        True(cannons[0].Specific && !cannons[2].Specific && cannons[1].Params["fireGroup"] == "2", "specific cannon carries its own fire group");
        True(RuleModel.Events(BuiltinRules.Cannons).Select(e => e.Label).SequenceEqual(new[]
            { "fires", "starts reloading", "finished reloading", "is loaded (while)", "is reloading (while)", "becomes loaded", "becomes not ready", "is not ready (while)" }),
            "cannon events in the mockup order");
        True(RuleModel.SourcesOf(BuiltinRules.Keys).Select(s => s.Label).SequenceEqual(new[] { "Action 1", "Action 2", "Action 3", "Action 4" }), "key sources from KeyInput.Actions");
        True(RuleModel.SourcesOf(BuiltinRules.Hinges).Single().Label == "Hatch (Leaf)" && RuleModel.SourcesOf(BuiltinRules.Mast).Single().Label == "Mast", "hinge and mast sources");

        Eq(Sentence(L(IoCommand.Open, "cannon.fired", ("cannon", "vuid:11"), ("fireGroup", "2"))), "Cannon 2 – 20 mm (group 2) | fires | Open", "specific cannon fires");
        Eq(Sentence(L(IoCommand.Pulse, "cannon.fired", ("fireGroup", "3"))), "Any cannon in group 3 | fires | Pulse", "any cannon in group");
        Eq(Sentence(L(IoCommand.Follow, "cannon.reloading", ("fireGroup", "1"), ("mode", "loading"))), "Any cannon in group 1 | is reloading (while) | Follow", "level + follow = while");
        Eq(Sentence(L(IoCommand.Close, "cannon.reloading", ("cannon", "vuid:10"), ("mode", "loading"))), "Cannon 1 – 75 mm (group 1) | starts reloading | Close", "level + edge command = starts");
        Eq(Sentence(L(IoCommand.Follow, "cannon.reloading", ("mode", "notReady"))), "Any cannon | is not ready (while) | Follow", "notReady while");
        Eq(Sentence(L(IoCommand.Toggle, "cannon.reloading", ("mode", "notReady"))), "Any cannon | becomes not ready | Toggle", "notReady edge");
        Eq(Sentence(L(IoCommand.Close, "cannon.reloaded", ("cannon", "vuid:10"))), "Cannon 1 – 75 mm (group 1) | finished reloading | Close", "reloaded");
        Eq(Sentence(L(IoCommand.Open, "cannon.loaded")), "Any cannon | becomes loaded | Open", "loaded edge");
        Eq(Sentence(L(IoCommand.Follow, "cannon.loaded")), "Any cannon | is loaded (while) | Follow", "loaded while");
        Eq(Sentence(L(IoCommand.Toggle, "key", ("action", "3"))), "Action 3 | is pressed | Toggle", "key pressed");
        Eq(Sentence(L(IoCommand.Follow, "key", ("action", "2"))), "Action 2 | is held (while) | Follow", "key held");
        Eq(Sentence(L(IoCommand.Follow, "hinge.state", ("hinge", "vuid:20:Hatch"), ("state", "closed"))), "Hatch (Leaf) | is closed (while) | Follow", "hinge closed while");
        Eq(Sentence(L(IoCommand.Open, "hinge.state", ("hinge", "vuid:20:Hatch"), ("state", "open"))), "Hatch (Leaf) | has opened | Open", "hinge opened");
        Eq(Sentence(L(IoCommand.Follow, "mast.state", ("state", "raised"))), "Mast | is up (while) | Follow", "mast up");
        Eq(Sentence(L(IoCommand.Open, "mast.state", ("state", "extended"))), "Mast | is fully extended | Open", "mast extended");

        // Missing sources: a cannon/hinge of another vehicle stays as it is, shown as "not found".
        var missing = L(IoCommand.Open, "cannon.fired", ("cannon", "vuid:99"), ("fireGroup", "2"));
        var mv = RuleModel.Derive(missing);
        True(mv.Source.Missing && mv.Source.Label == "Cannon #99 (not found: any in group 2)" && mv.Event.Label == "fires", "missing cannon source");
        True(RuleModel.SourcesFor(missing).Last().Missing && RuleModel.SourcesFor(missing).Count == 3 + FireGroups.Max + 4 + 1 + 1 + 1, "missing source added to its picker only");
        True(RuleModel.Derive(L(IoCommand.Open, "hinge.state", ("hinge", "vuid:77:Gone"))).Source.Label == "Hinge 'Gone' (not found)", "missing hinge source");
        True(missing.Input.Key == In("cannon.fired", ("cannon", "vuid:99"), ("fireGroup", "2")).Key, "derive never changes the link");
        True(RuleModel.CommandsFor(missing).SequenceEqual(RuleModel.EdgeCommands), "event-only type: no follow");
        True(RuleModel.CommandsFor(L(IoCommand.Open, "cannon.reloading")).Contains(IoCommand.Follow) && RuleModel.CommandsFor(L(IoCommand.Open, "key")).Contains(IoCommand.Follow), "while-capable types offer follow");
        True(RuleModel.CommandsFor(L(IoCommand.Follow, "cannon.fired")).Contains(IoCommand.Follow), "a loaded follow link keeps its command visible");
    }

    private static void Edits()
    {
        var link = L(IoCommand.Open, "cannon.fired", ("cannon", "vuid:10"), ("fireGroup", "1"));
        var events = RuleModel.Events(BuiltinRules.Cannons);
        RuleModel.SetEvent(link, events.First(e => e.Label == "finished reloading"));
        True(link.Input.TypeId == "cannon.reloaded" && link.Input.Get("cannon") == "vuid:10" && link.Command == IoCommand.Open, "event change keeps the cannon");
        RuleModel.SetEvent(link, events.First(e => e.Label == "is reloading (while)"));
        True(link.Input.TypeId == "cannon.reloading" && link.Input.Get("mode") == "loading" && link.Command == IoCommand.Follow, "while event switches to follow");
        RuleModel.SetCommand(link, IoCommand.Pulse);
        Eq(Sentence(link), "Cannon 1 – 75 mm (group 1) | starts reloading | Pulse", "edge command switches to the edge twin");
        RuleModel.SetCommand(link, IoCommand.Follow);
        Eq(Sentence(link), "Cannon 1 – 75 mm (group 1) | is reloading (while) | Follow", "follow switches to the while twin");
        RuleModel.SetEvent(link, events.First(e => e.Label == "fires"));
        True(link.Input.TypeId == "cannon.fired" && link.Command == IoCommand.Open, "edge event leaves follow as open");

        var sources = RuleModel.Sources();
        RuleModel.SetSource(link, sources.First(s => s.Label.StartsWith("Cannon 2")));
        True(link.Input.Get("cannon") == "vuid:11" && link.Input.Get("fireGroup") == "2" && link.Input.TypeId == "cannon.fired", "specific cannon source sets its fire group");
        RuleModel.SetSource(link, sources.First(s => s.Label == "Any cannon in group 4"));
        True(link.Input.Get("cannon") == "any" && link.Input.Get("fireGroup") == "4", "any-in-group source");
        RuleModel.SetSource(link, sources.First(s => s.Label == "Action 2"));
        True(link.Input.TypeId == "key" && link.Input.Get("action") == "2" && link.Command == IoCommand.Open, "other group: its first event");
        var follow = L(IoCommand.Follow, "cannon.loaded");
        RuleModel.SetSource(follow, sources.First(s => s.Label == "Mast"));
        Eq(Sentence(follow), "Mast | is fully extended (while) | Follow", "other group keeps follow via a while event");

        var lost = L(IoCommand.Open, "cannon.fired", ("cannon", "vuid:99"), ("fireGroup", "2"));
        RuleModel.SetEvent(lost, events.First(e => e.Label == "finished reloading"));
        True(lost.Input.TypeId == "cannon.reloaded" && lost.Input.Get("cannon") == "vuid:99" && lost.Input.Get("fireGroup") == "2", "missing cannon kept when the event changes");
        var timed = new Link(In("cannon.fired"), IoCommand.Pulse, 1.5f, 3);
        RuleModel.SetSource(timed, sources.First(s => s.Label == "Action 1"));
        True(timed.DelaySeconds == 1.5f && timed.DurationSeconds == 3 && timed.Command == IoCommand.Pulse, "delay, duration and command survive edits");
    }

    private static void DefaultAndTemplates()
    {
        var d = RuleModel.DefaultRule()!;
        True(d.Input.TypeId == "cannon.fired" && d.Input.Get("cannon") == "vuid:10" && d.Input.Get("fireGroup") == "1" && d.Command == IoCommand.Open && d.DelaySeconds == 0,
            "default rule: first cannon, fires -> open");
        var flap = RuleTemplate.All[0].Build()!;
        True(flap.Count == 2 && flap[0].Input.Key == In("cannon.fired", ("cannon", "vuid:10"), ("fireGroup", "1")).Key && flap[0].Command == IoCommand.Open
             && flap[1].Input.Key == In("cannon.reloaded", ("cannon", "vuid:10"), ("fireGroup", "1")).Key && flap[1].Command == IoCommand.Close, "case flap template");
        var hatch = RuleTemplate.All[1].Build()!;
        True(hatch.Count == 1 && hatch[0].Input.Key == In("key", ("action", "1")).Key && hatch[0].Command == IoCommand.Toggle, "hatch on key template");
        var mast = RuleTemplate.All[2].Build()!;
        True(mast.Count == 1 && mast[0].Input.Key == In("mast.state", ("state", "raised")).Key && mast[0].Command == IoCommand.Follow, "open while mast is up template");

        Catalog(false); // no cannons known yet (designer timing): any cannon in group 1
        var any = RuleModel.DefaultRule()!;
        True(any.Input.Get("cannon") == "any" && any.Input.Get("fireGroup") == "0" && any.Input.TypeId == "cannon.fired", "default rule without cannons");
        True(RuleTemplate.All[0].Build()![1].Input.Key == In("cannon.reloaded", ("cannon", "any"), ("fireGroup", "0")).Key, "case flap without cannons uses any group");
        Catalog(true);
        var json = HingeConfigJson.Write(new HingeConfig { Name = "T", Links = { flap[0], flap[1], hatch[0], mast[0] } });
        True(HingeConfigJson.Parse(json).Links.Select(Sentence).SequenceEqual(new[] { flap[0], flap[1], hatch[0], mast[0] }.Select(Sentence)), "template links round-trip through hingeIoV1");
    }

    private static void Signals()
    {
        var titles = RuleModel.Signals().Select(s => s.Title).ToList();
        True(titles.Count == 2 * 3 + 2 + 2 + 4, "signals: 2 cannons x 3, hinge x 2, mast x 2, keys x 4");
        True(titles.Take(3).SequenceEqual(new[] { "Cannon 1 fires", "Cannon 1 starts reloading", "Cannon 1 finished reloading" }) && titles.Contains("Action 4 is pressed")
             && titles.Contains("Hatch has opened") && titles.Contains("Mast is fully extended"), "signal titles");
        var sig = RuleModel.Signals()[1];
        var l = RuleModel.Build(sig.Source, sig.Event, IoCommand.Open);
        True(l.Input.Key == sig.Instance().Key && l.Input.Get("mode") == "loading" && l.Command == IoCommand.Open, "signal click builds the same input");
    }

    private static void NewTypes()
    {
        // A new type without a description still appears: its own group, source and event.
        IoRegistry.RegisterInput(new NewInput("test.smoke", "Smoke launched", SignalKind.Event));
        True(RuleModel.Groups().Last() == "Smoke launched" && RuleModel.SourcesOf("Smoke launched").Single().Label == "Smoke launched", "fallback group + source");
        True(RuleModel.Events("Smoke launched").Single().Label == "triggers" && RuleModel.Signals().Any(s => s.Title == "Smoke launched triggers"), "fallback event + signal");
        var smoke = RuleModel.Build(RuleModel.SourcesOf("Smoke launched")[0], RuleModel.Events("Smoke launched")[0]);
        True(smoke.Input.TypeId == "test.smoke" && smoke.Input.Get("count") == "2", "fallback builds defaults");
        var custom = new Link(In("test.smoke", ("count", "4")), IoCommand.Toggle);
        RuleModel.SetCommand(custom, IoCommand.Open);
        True(Sentence(custom) == "Smoke launched | triggers | Open" && custom.Input.Get("count") == "4", "fallback keeps custom parameters");
        IoRegistry.RegisterInput(new NewInput("test.level", "Overheated", SignalKind.Level));
        True(RuleModel.Events("Overheated").Select(e => e.Label).SequenceEqual(new[] { "turns on", "is on (while)" }), "fallback level: edge + while");

        // A described type joins an existing group: it shows up in the cannon event list automatically.
        IoRegistry.RegisterInput(new NewInput("test.jammed", "Cannon jammed", SignalKind.Level));
        IoRegistry.DescribeInput("test.jammed", new RuleDescription(BuiltinRules.Cannons, new[] { "cannon", "fireGroup" }, null, new[]
        {
            new RuleEventDef("jams", new Dictionary<string, string>(), false, true, 10),
            new RuleEventDef("is jammed (while)", new Dictionary<string, string>(), true, false, 11)
        }));
        True(RuleModel.Events(BuiltinRules.Cannons).Select(e => e.Label).TakeLast(2).SequenceEqual(new[] { "jams", "is jammed (while)" }), "described type joins the cannon events");
        True(!RuleModel.Groups().Contains("Cannon jammed") && RuleModel.SourcesOf(BuiltinRules.Cannons).Count == 3 + FireGroups.Max, "no extra group, same cannon sources");
        var jam = L(IoCommand.Open, "cannon.fired", ("cannon", "vuid:11"), ("fireGroup", "2"));
        RuleModel.SetEvent(jam, RuleModel.Events(BuiltinRules.Cannons).First(e => e.Label == "is jammed (while)"));
        True(jam.Input.TypeId == "test.jammed" && jam.Input.Get("cannon") == "vuid:11" && jam.Command == IoCommand.Follow, "new event keeps the chosen cannon");
        foreach (var id in new[] { "test.smoke", "test.level", "test.jammed" }) IoRegistry.UnregisterInput(id);
        True(RuleModel.Groups().Count == 4, "unregistered types disappear");
    }

    /// <summary>A hingeIoV1 config as 0.5.0 writes it: loads, derives and saves byte-identical.</summary>
    private static void Config05()
    {
        const string v05 = "{\"Version\":1,\"Name\":\"Case flap\",\"Output\":{\"Type\":\"hinge\",\"Params\":{\"closedAngle\":-10,\"openAngle\":95,\"speed\":180}},\"Links\":[" +
            "{\"Input\":{\"Type\":\"cannon.fired\",\"Params\":{\"cannon\":\"vuid:10\",\"fireGroup\":1}},\"Command\":\"Open\",\"Delay\":0.5,\"Duration\":2}," +
            "{\"Input\":{\"Type\":\"cannon.reloaded\",\"Params\":{\"cannon\":\"vuid:99\",\"fireGroup\":2}},\"Command\":\"Close\",\"Delay\":0,\"Duration\":2}," +
            "{\"Input\":{\"Type\":\"cannon.reloading\",\"Params\":{\"cannon\":\"any\",\"fireGroup\":1,\"mode\":\"notReady\"}},\"Command\":\"Follow\",\"Delay\":0,\"Duration\":0}," +
            "{\"Input\":{\"Type\":\"cannon.loaded\",\"Params\":{\"cannon\":\"any\",\"fireGroup\":3}},\"Command\":\"Pulse\",\"Delay\":1.25,\"Duration\":4}," +
            "{\"Input\":{\"Type\":\"hinge.state\",\"Params\":{\"hinge\":\"vuid:20:Hatch\",\"state\":\"open\"}},\"Command\":\"Follow\",\"Delay\":0,\"Duration\":2}," +
            "{\"Input\":{\"Type\":\"mast.state\",\"Params\":{\"state\":\"raised\"}},\"Command\":\"Follow\",\"Delay\":0,\"Duration\":2}," +
            "{\"Input\":{\"Type\":\"key\",\"Params\":{\"action\":2}},\"Command\":\"Toggle\",\"Delay\":0,\"Duration\":2}," +
            "{\"Input\":{\"Type\":\"future.radar\",\"Params\":{\"range\":500}},\"Command\":\"Open\",\"Delay\":0,\"Duration\":2}]," +
            "\"Colour\":\"green\"}";
        var c = HingeConfigJson.Parse(v05);
        True(c.Links.Count == 7 && c.PreservedLinks.Count == 1 && c.Name == "Case flap" && c.GetOutputFloat("closedAngle", 0) == -10, "0.5 config loads: 7 active, 1 kept");
        Eq(HingeConfigJson.Write(c), v05, "0.5 config saved byte-identical");
        var sentences = c.Links.Select(Sentence).ToArray();
        True(sentences.SequenceEqual(new[]
        {
            "Cannon 1 – 75 mm (group 1) | fires | Open",
            "Cannon #99 (not found: any in group 2) | finished reloading | Close",
            "Any cannon in group 1 | is not ready (while) | Follow",
            "Any cannon in group 3 | becomes loaded | Pulse",
            "Hatch (Leaf) | is open (while) | Follow",
            "Mast | is up (while) | Follow",
            "Action 2 | is pressed | Toggle"
        }), "0.5 links read as sentences: " + string.Join(" / ", sentences));
        Eq(HingeConfigJson.Write(c), v05, "showing the sentences changes nothing");
        // Editing one rule changes only that link; the others and the kept link stay as saved.
        RuleModel.SetCommand(c.Links[0], IoCommand.Toggle);
        var edited = HingeConfigJson.Write(c);
        Eq(edited, v05.Replace("\"Command\":\"Open\",\"Delay\":0.5", "\"Command\":\"Toggle\",\"Delay\":0.5"), "one edit, one change");
    }
}
