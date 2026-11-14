using System.Text.Json.Nodes;
using SprocketTelescopicMast;
using SprocketTelescopicMast.Io;

// 0.5.0: built-in input types against a fake vehicle snapshot, cannon choice + fallback,
// reload edge, hinge chains with loop protection, mast state, Choice parameters, 0.4.x saves.
internal static class IoTests05
{
    private static int checks;
    private static void True(bool condition, string what) { checks++; if (!condition) throw new Exception("FAIL: " + what); }

    private sealed class FakeSignals : IVehicleSignals
    {
        public bool IsPlayer { get; set; } = true;
        public bool Braking { get; set; }
        public List<CannonShot> Fired { get; } = new();
        public List<CannonState> Cannons { get; } = new();
        public List<HingeState> Hinges { get; } = new();
        public List<MastState> Masts { get; } = new();
        IReadOnlyList<CannonShot> IVehicleSignals.Fired => Fired;
        IReadOnlyList<CannonState> IVehicleSignals.Cannons => Cannons;
        IReadOnlyList<HingeState> IVehicleSignals.Hinges => Hinges;
        IReadOnlyList<MastState> IVehicleSignals.Masts => Masts;
        public int[] Presses = new int[KeyInput.Actions + 1];
        public bool[] Held = new bool[KeyInput.Actions + 1];
        public int KeyPresses(int action) => Presses[action];
        public bool KeyHeld(int action) => Held[action];
        public void SetCannon(int vuid, int mask, bool loading = false, bool loaded = true, int reloaded = 0)
        {
            Cannons.RemoveAll(c => c.Vuid == vuid);
            Cannons.Add(new CannonState(vuid, mask, loading, !loaded, loaded, reloaded));
        }
    }

    private static InputInstance In(string type, params (string Key, string Value)[] p) =>
        new(type, p.ToDictionary(x => x.Key, x => x.Value));
    private static int Events(InputInstance i, object ctx) => IoRegistry.FindInput(i.TypeId)!.Read(i, ctx).Events;
    private static bool Level(InputInstance i, object ctx) => IoRegistry.FindInput(i.TypeId)!.Read(i, ctx).Level;

    internal static int Run()
    {
        // Registered exactly like the plugin does: one class + RegisterInput each.
        foreach (var t in new IInputType[] { new CannonFiredInput(), new CannonReloadingInput(), new CannonReloadedInput(),
                     new CannonLoadedInput(), new HingeStateInput(), new MastStateInput(), new VehicleBrakingInput(), new KeyInput() })
            if (IoRegistry.FindInput(t.Id) == null) IoRegistry.RegisterInput(t);
        foreach (var id in new[] { "cannon.fired", "cannon.reloading", "cannon.reloaded", "cannon.loaded", "hinge.state", "mast.state", "vehicle.braking", "key" })
            True(IoRegistry.FindInput(id) != null, "registered " + id);
        True(IoRegistry.FindInput("hinge.state") is IFeedbackInput && IoRegistry.FindInput("cannon.fired") is not IFeedbackInput, "feedback marker");
        True(IoRegistry.FindInput("cannon.reloaded")!.SignalKind == SignalKind.Event && IoRegistry.FindInput("cannon.loaded")!.SignalKind == SignalKind.Level, "signal kinds");

        ChoiceParams();
        Compat04();
        CannonChoice();
        ReloadEdges();
        ShellEjector();
        HingeChains();
        LoopGuard();
        MastAndKeys();
        Mount();
        IoCatalog.Cannons = Array.Empty<ParamOption>(); IoCatalog.Hinges = Array.Empty<ParamOption>();
        return checks;
    }

    private static void ChoiceParams()
    {
        IoCatalog.Cannons = new[] { new ParamOption("vuid:10", "Cannon 1 – 75 mm (group 1)"), new ParamOption("vuid:11", "Cannon 2 – 20 mm (group 2)") };
        var def = IoRegistry.FindInput("cannon.fired")!.Schema.Find("cannon")!;
        True(def.Kind == ParamKind.Choice && def.Default == "any", "choice default any");
        True(IoCatalog.Cannons.Count == 3 && IoCatalog.Cannons[0].Value == "any", "catalog always starts with any");
        True(def.Normalize("vuid:99") == "vuid:99" && def.Normalize("  ") == "any" && def.Normalize(null) == "any" && def.Normalize("a\nb") == "any", "choice keeps unknown keys, rejects empty/control");
        True(def.Nudge("any", 1) == "vuid:10" && def.Nudge("vuid:11", 1) == "any" && def.Nudge("any", -1) == "vuid:11", "choice cycles live options");
        True(def.Nudge("vuid:99", 1) == "any" && def.Nudge("vuid:99", -1) == "vuid:11", "missing key jumps into the list");
        True(def.LabelOf("vuid:10") == "Cannon 1 – 75 mm (group 1)" && def.LabelOf("vuid:99") == null, "labels");
        var throwing = ParamDef.Choice("x", "X", () => throw new InvalidOperationException(), "any");
        True(throwing.CurrentOptions().Count == 0 && throwing.Nudge("any", 1) == "any", "failing option source never throws");
        True(PartSelector.TryParse("vuid:12:Hatch: rear", out int v, out var n) && v == 12 && n == "Hatch: rear", "selector with name containing colon");
        True(!PartSelector.TryParse("any", out _, out _) && !PartSelector.TryParse("vuid:x", out _, out _), "selector rejects");
        True(PartSelector.ForVuid(7, "A") == "vuid:7:A" && PartSelector.ForVuid(7) == "vuid:7", "selector format");
    }

    /// <summary>0.4.x links (cannon.fired with only fireGroup, cannon.reloading with mode) load and behave as before.</summary>
    private static void Compat04()
    {
        const string old = "{\"Version\":1,\"Name\":\"Hatch\",\"Output\":{\"Type\":\"hinge\",\"Params\":{\"closedAngle\":0,\"openAngle\":90,\"speed\":90}},\"Links\":[" +
            "{\"Input\":{\"Type\":\"cannon.fired\",\"Params\":{\"fireGroup\":2}},\"Command\":\"Pulse\",\"Delay\":0,\"Duration\":2}," +
            "{\"Input\":{\"Type\":\"cannon.reloading\",\"Params\":{\"fireGroup\":1,\"mode\":\"notReady\"}},\"Command\":\"Follow\",\"Delay\":0,\"Duration\":0}," +
            "{\"Input\":{\"Type\":\"key\",\"Params\":{\"action\":3}},\"Command\":\"Toggle\",\"Delay\":0.5,\"Duration\":2}]}";
        var c = HingeConfigJson.Parse(old);
        True(c.Links.Count == 3 && c.PreservedLinks.Count == 0 && c.Name == "Hatch", "0.4.x links all active");
        var fired = c.Links[0].Input;
        True(fired.Get("cannon") == "any" && fired.GetInt("fireGroup") == 2, "old cannon.fired gets cannon=any, keeps group");
        True(c.Links[1].Input.Get("mode") == "notReady" && c.Links[1].Input.Get("cannon") == "any", "old cannon.reloading keeps mode");
        True(c.Links[2].Input.GetInt("action") == 3 && Math.Abs(c.Links[2].DelaySeconds - .5f) < 1e-6, "old key link");
        var s = new FakeSignals();
        s.SetCannon(10, 1); s.SetCannon(12, 2);
        s.Fired.Add(new CannonShot(12, 2));
        True(Events(fired, s) == 1, "old group-2 link counts a group-2 shot");
        s.Fired.Clear(); s.Fired.Add(new CannonShot(10, 1));
        True(Events(fired, s) == 0, "old group-2 link ignores group 1");
        var json = HingeConfigJson.Write(c);
        True(json.Contains("\"cannon\":\"any\"") && HingeConfigJson.Write(HingeConfigJson.Parse(json)) == json, "re-saved in 0.5 form, stable");
        // Engine: same pulse behaviour as 0.4.x
        var engine = new IoEngine();
        engine.Configure(new[] { ("H", (IEnumerable<Link>)c.Links.Take(1).ToArray()) });
        s.Fired.Clear(); s.Fired.Add(new CannonShot(12, 2));
        engine.Tick(1, s); True(engine.IsOpen("H", 1) && !engine.IsOpen("H", 3), "old link pulses 2 s");
    }

    private static void CannonChoice()
    {
        var s = new FakeSignals();
        s.SetCannon(10, 1); s.SetCannon(11, 1); s.SetCannon(12, 6);
        var specific = In("cannon.fired", ("fireGroup", "1"), ("cannon", "vuid:11"));
        s.Fired.Add(new CannonShot(10, 1));
        True(Events(specific, s) == 0, "specific cannon ignores the other cannon in its group");
        s.Fired.Add(new CannonShot(11, 1));
        True(Events(specific, s) == 1, "specific cannon counts its own shot");
        var any = In("cannon.fired", ("fireGroup", "1"));
        True(Events(any, s) == 2, "any in group 1 counts both");
        var group3 = In("cannon.fired", ("fireGroup", "3"));
        s.Fired.Add(new CannonShot(12, 6));
        True(Events(group3, s) == 1, "mask 6 is in group 3");

        // Copied vehicle: VUID 99 not on this vehicle -> any in fire group 1, and the panel says so.
        var missing = In("cannon.fired", ("fireGroup", "1"), ("cannon", "vuid:99"));
        True(PartSelector.ResolveCannon("vuid:99", s.Cannons, out bool fellBack) == null && fellBack, "fallback flagged");
        True(PartSelector.ResolveCannon("vuid:11", s.Cannons, out fellBack) == 11 && !fellBack, "present cannon resolves");
        True(PartSelector.ResolveCannon("any", s.Cannons, out fellBack) == null && !fellBack, "any is not a fallback");
        True(Events(missing, s) == 2, "missing cannon falls back to any in group 1");
        IoCatalog.Cannons = new[] { new ParamOption("vuid:11", "Cannon 2 – 75 mm (group 1)") };
        True(missing.Describe() == "Cannon fired (cannon #99 not found: any in group 1)", "fallback described: " + missing.Describe());
        True(specific.Describe() == "Cannon fired (Cannon 2 – 75 mm (group 1))", "specific described: " + specific.Describe());
        True(any.Describe() == "Cannon fired (any in group 1)", "any described");
        // The choice survives JSON (stable key, no remap) and parameters stay distinct instances.
        var back = HingeConfigJson.Parse(HingeConfigJson.Write(new HingeConfig { Links = { new Link(specific, IoCommand.Open) } }));
        True(back.Links[0].Input.Equals(specific) && !specific.Equals(any), "choice round trip / identity");

        // reloading / loaded with a specific cannon
        s.SetCannon(11, 1, loading: true, loaded: false);
        True(Level(In("cannon.reloading", ("cannon", "vuid:11")), s) && !Level(In("cannon.reloading", ("cannon", "vuid:10")), s), "reloading per cannon");
        True(!Level(In("cannon.loaded", ("cannon", "vuid:11")), s) && Level(In("cannon.loaded", ("cannon", "vuid:10")), s), "loaded per cannon");
        True(Level(In("cannon.loaded", ("fireGroup", "1")), s), "loaded: any cannon in group 1 loaded");
        True(!Level(In("cannon.loaded", ("fireGroup", "4")), s), "loaded: no cannon in group 4");
    }

    private static void ReloadEdges()
    {
        var e = new ReloadEdge(.1);
        // Spawn: NeverLoaded -> Loading -> Loaded is the first load, not a reload.
        True(!e.Update(LoadPhase.NeverLoaded, 0) && !e.Update(LoadPhase.Loading, .1) && !e.Update(LoadPhase.Loaded, 3), "first load is no event");
        True(!e.Update(LoadPhase.Loaded, 3.5), "staying loaded: no event");
        // Shot: Unloaded -> Loading 2 s -> Loaded = one event
        True(!e.Update(LoadPhase.Unloaded, 4) && !e.Update(LoadPhase.Loading, 4.02) && !e.Update(LoadPhase.Loading, 5.5), "loading");
        True(e.Update(LoadPhase.Loaded, 6.02) && !e.Update(LoadPhase.Loaded, 6.04), "Loading -> Loaded edge once");
        // Flicker: a 50 ms Loading blip is not a reload
        True(!e.Update(LoadPhase.Loading, 7) && !e.Update(LoadPhase.Loaded, 7.05), "short loading blip ignored");
        // Unloaded in the middle of loading keeps the phase start
        e.Update(LoadPhase.Loading, 8); e.Update(LoadPhase.Unloaded, 8.05); e.Update(LoadPhase.Loading, 8.06);
        True(e.Update(LoadPhase.Loaded, 8.2), "loading interrupted by Unloaded still counts from its start");
        // Out of ammunition: Unloaded -> Loaded without Loading is no reload event
        e.Update(LoadPhase.Unloaded, 9); True(!e.Update(LoadPhase.Loaded, 9.5), "no Loading seen: no event");
        // First seen while already loading (polling started mid-spawn): not an event
        var late = new ReloadEdge(.1);
        True(!late.Update(LoadPhase.Loading, 0) && !late.Update(LoadPhase.Loaded, 2), "first observed load is not an event");
        True(!late.Update(LoadPhase.Loading, 3) && late.Update(LoadPhase.Loaded, 5), "following reload is");
    }

    /// <summary>The user's shell ejector: fire -> Open, reloaded -> Close, specific cannon.</summary>
    private static void ShellEjector()
    {
        var s = new FakeSignals();
        s.SetCannon(10, 1); s.SetCannon(11, 1);
        var links = new[]
        {
            new Link(In("cannon.fired", ("cannon", "vuid:11")), IoCommand.Open),
            new Link(In("cannon.reloaded", ("cannon", "vuid:11")), IoCommand.Close)
        };
        var engine = new IoEngine();
        engine.Configure(new[] { ("ejector", (IEnumerable<Link>)links) });
        engine.Tick(0, s); True(!engine.IsOpen("ejector", 0), "ejector closed at start");
        s.Fired.Add(new CannonShot(10, 1)); engine.Tick(1, s); s.Fired.Clear();
        True(!engine.IsOpen("ejector", 1), "other cannon's shot does not open");
        s.Fired.Add(new CannonShot(11, 1)); s.SetCannon(11, 1, loading: true, loaded: false); engine.Tick(2, s); s.Fired.Clear();
        True(engine.IsOpen("ejector", 2), "own shot opens");
        s.SetCannon(10, 1, reloaded: 1); engine.Tick(3, s); s.SetCannon(10, 1);
        True(engine.IsOpen("ejector", 3), "other cannon's reload does not close");
        engine.Tick(4, s); True(engine.IsOpen("ejector", 4), "stays open while reloading");
        s.SetCannon(11, 1, reloaded: 1); engine.Tick(5, s); s.SetCannon(11, 1);
        True(!engine.IsOpen("ejector", 5), "end of own reload closes");
        engine.Tick(6, s); True(!engine.IsOpen("ejector", 6), "stays closed");
    }

    private static void HingeChains()
    {
        var s = new FakeSignals();
        s.Hinges.Add(new HingeState(5, "Hatch A", true, false));
        s.Hinges.Add(new HingeState(6, "Hatch B", false, true));
        s.Hinges.Add(new HingeState(0, "Twin", false, true));
        s.Hinges.Add(new HingeState(0, "Twin", true, false));
        True(Level(In("hinge.state", ("hinge", "vuid:5:Hatch A")), s), "A open by VUID");
        True(!Level(In("hinge.state", ("hinge", "vuid:5:Hatch A"), ("state", "closed")), s), "A not closed");
        True(Level(In("hinge.state", ("hinge", "vuid:6:Hatch B"), ("state", "closed")), s), "B closed");
        True(Level(In("hinge.state", ("hinge", "vuid:42:Hatch B"), ("state", "closed")), s), "copied vehicle: falls back to the hinge name");
        True(!Level(In("hinge.state", ("hinge", "vuid:42:Twin"), ("state", "closed")), s), "ambiguous name: not found");
        True(!Level(In("hinge.state"), s), "no hinge picked: off");
        True(In("hinge.state", ("hinge", "vuid:42:Gone")).Describe() == "Hinge 'Gone' is open (not found)", "missing hinge described");
        // Chain A -> B: B opens when A reaches open (rising edge), no loop.
        var engine = new IoEngine();
        engine.Configure(new[] { ("B", (IEnumerable<Link>)new[] { new Link(In("hinge.state", ("hinge", "vuid:5:Hatch A")), IoCommand.Open) }) });
        s.Hinges[0] = new HingeState(5, "Hatch A", false, true); engine.Tick(0, s); True(!engine.IsOpen("B", 0), "chain idle");
        s.Hinges[0] = new HingeState(5, "Hatch A", true, false); engine.Tick(1, s); True(engine.IsOpen("B", 1) && engine.LoopOutputs.Count == 0, "A reaches open -> B opens");
    }

    /// <summary>
    /// Worst case: hinge state follows the commanded state instantly (open angle == closed angle).
    /// A follows "B closed", B follows "A open": an endless inverter loop that must be detected and paused.
    /// </summary>
    private static void LoopGuard()
    {
        var engine = new IoEngine();
        var a = new Link(In("hinge.state", ("hinge", "vuid:2:B"), ("state", "closed")), IoCommand.Follow);
        var b = new Link(In("hinge.state", ("hinge", "vuid:1:A")), IoCommand.Follow);
        var detected = new List<string>();
        engine.LoopDetected = (id, _) => detected.Add(id);
        engine.Configure(new[] { ("A", (IEnumerable<Link>)new[] { a }), ("B", new[] { b }) });
        var s = new FakeSignals();
        double t = 0;
        void Step()
        {
            bool oa = engine.IsOpen("A", t), ob = engine.IsOpen("B", t);
            s.Hinges.Clear();
            s.Hinges.Add(new HingeState(1, "A", oa, !oa)); s.Hinges.Add(new HingeState(2, "B", ob, !ob));
            t += .02; engine.Tick(t, s);
        }
        for (int i = 0; i < 60; i++) Step();
        True(detected.Contains("A") && engine.InLoop("A"), "inverter loop detected on A");
        int changes = 0; bool last = engine.IsOpen("A", t);
        for (int i = 0; i < 200; i++) { Step(); bool now = engine.IsOpen("A", t); if (now != last) changes++; last = now; }
        True(changes == 0, "paused: A no longer flips");
        True(detected.Count == detected.Distinct().Count(), "each loop reported once");
        // Editing the configuration resumes the links (and a fixed config stays calm).
        engine.Configure(new[] { ("A", (IEnumerable<Link>)new[] { a }), ("B", Array.Empty<Link>()) });
        True(!engine.InLoop("A") && engine.LoopOutputs.Count == 0, "configure clears loop pause");

        // Toggle ping-pong: each hinge toggles whenever the other reaches open or closed - endless.
        engine = new IoEngine(); detected.Clear(); engine.LoopDetected = (id, _) => detected.Add(id);
        Link Tog(string sel, string state) => new(In("hinge.state", ("hinge", sel), ("state", state)), IoCommand.Toggle);
        engine.Configure(new[]
        {
            ("A", (IEnumerable<Link>)new[] { Tog("vuid:2:B", "open"), Tog("vuid:2:B", "closed") }),
            ("B", new[] { Tog("vuid:1:A", "open"), Tog("vuid:1:A", "closed") })
        });
        for (int i = 0; i < 400; i++) Step();
        True(detected.Count == 2 && engine.InLoop("A") && engine.InLoop("B"), "toggle ping-pong detected on both hinges");
        bool la = engine.IsOpen("A", t), lb = engine.IsOpen("B", t); int flips = 0;
        for (int i = 0; i < 200; i++) { Step(); if (engine.IsOpen("A", t) != la || engine.IsOpen("B", t) != lb) flips++; la = engine.IsOpen("A", t); lb = engine.IsOpen("B", t); }
        True(flips == 0, "ping-pong paused");

        // Not a loop: a fast autocannon pulsing an output without chain inputs.
        engine = new IoEngine(); detected.Clear(); engine.LoopDetected = (id, _) => detected.Add(id);
        engine.Configure(new[] { ("C", (IEnumerable<Link>)new[] { new Link(In("cannon.fired"), IoCommand.Pulse, 0, .05f) }) });
        var gun = new FakeSignals(); gun.SetCannon(1, 1);
        for (int i = 0; i < 300; i++) { gun.Fired.Clear(); if (i % 5 == 0) gun.Fired.Add(new CannonShot(1, 1)); engine.Tick(i * .02, gun); }
        True(detected.Count == 0, "fast pulses without chain inputs are not a loop");
        // Not a loop: a normal one-way chain driven by a key a few times.
        engine = new IoEngine(); detected.Clear(); engine.LoopDetected = (id, _) => detected.Add(id);
        engine.Configure(new[] { ("A", (IEnumerable<Link>)new[] { new Link(In("key"), IoCommand.Toggle) }), ("B", new[] { new Link(In("hinge.state", ("hinge", "vuid:1:A")), IoCommand.Follow) }) });
        t = 0;
        for (int i = 0; i < 500; i++) { s.Presses[1] = i % 100 == 0 ? 1 : 0; Step(); }
        s.Presses[1] = 0;
        True(detected.Count == 0, "one-way chain with occasional key presses is not a loop");
    }

    private static void MastAndKeys()
    {
        var s = new FakeSignals();
        var ext = In("mast.state"); var ret = In("mast.state", ("state", "retracted")); var up = In("mast.state", ("state", "raised"));
        True(!Level(ext, s) && !Level(ret, s) && !Level(up, s), "no masts: all off");
        s.Masts.Add(new MastState(0));
        True(!Level(ext, s) && Level(ret, s) && !Level(up, s), "retracted");
        s.Masts[0] = new MastState(.4f);
        True(!Level(ext, s) && !Level(ret, s) && Level(up, s), "raised, not full");
        s.Masts[0] = new MastState(1);
        True(Level(ext, s) && !Level(ret, s) && Level(up, s), "fully extended");
        True(ext.Describe() == "Mast fully extended", "mast describe");
        // Engine: Follow on mast extended
        var engine = new IoEngine();
        engine.Configure(new[] { ("H", (IEnumerable<Link>)new[] { new Link(ext, IoCommand.Follow) }) });
        engine.Tick(1, s); True(engine.IsOpen("H", 1), "follows extended mast");
        s.Masts[0] = new MastState(.5f); engine.Tick(2, s); True(!engine.IsOpen("H", 2), "closes when lowering");
        // Vehicle braking level: direct snapshot.
        var brake = In("vehicle.braking");
        True(!Level(brake, s), "vehicle not braking");
        s.Braking = true; True(Level(brake, s), "vehicle braking level");
        s.Braking = false;
        // Keys: player only
        s.Presses[2] = 1; s.Held[2] = true;
        var key = In("key", ("action", "2"));
        True(Events(key, s) == 1 && Level(key, s), "player key");
        s.IsPlayer = false; True(Events(key, s) == 0 && !Level(key, s), "AI vehicle ignores keys");
    }

    private static void Mount()
    {
        double vol = .1 * .01 * .09 + Math.PI * .01 * .01 * .1;
        True(Math.Abs(HingeMath.MountVolume - vol) < 1e-7, "mount volume");
        float kg = HingeMath.Mass(HingeMath.MountVolume, 1, 1, 1);
        True(Math.Abs(kg - .953) < .005, "mount mass ~0.95 kg at scale 1: " + kg);
        True(Math.Abs(HingeMath.Mass(HingeMath.MountVolume, 2, 2, 2) - 8 * kg) < 1e-3, "mount scales with volume");
        True(Math.Abs(HingeMath.MaterialCost(kg) - kg * 1.2f) < 1e-5 && HingeMath.AssemblyCost(false, 1, 1, 1) == 10f, "mount cost like the leaf");
    }
}
