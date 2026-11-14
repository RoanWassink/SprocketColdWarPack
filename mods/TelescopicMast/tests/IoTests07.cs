using System.Text.Json.Nodes;
using BepInEx.Unity.IL2CPP;
using SprocketTelescopicMast.Io;
using UnityEngine;

// 0.7.0: public I/O API facade, callback isolation, external-output change gate, the
// braking decision, 0.5/0.6 config compatibility and the copyable client shim.
internal static class IoTests07
{
    private static int checks;
    private static void True(bool condition, string what) { checks++; if (!condition) throw new Exception("FAIL: " + what); }
    private static void Eq(string actual, string expected, string what)
    { checks++; if (actual != expected) throw new Exception($"FAIL: {what}:\n'{actual}'\n!=\n'{expected}'"); }

    private sealed class FakeContext : IExternalVehicleContext
    {
        public object? VehicleToken { get; set; }
    }

    private sealed class TestHingeOutput : IOutputType
    {
        public string Id => "hinge"; public string DisplayName => "Hinge"; public SignalKind SignalKind => SignalKind.Level;
        public ParamSchema Schema { get; } = new(new[]
        {
            ParamDef.Float("closedAngle", "Closed", -180, 180, 0, 5),
            ParamDef.Float("openAngle", "Open", -180, 180, 90, 5),
            ParamDef.Float("speed", "Speed", 5, 720, 90, 15)
        });
        public IReadOnlyList<IoCommand> Commands { get; } = Enum.GetValues<IoCommand>();
        public string Describe(IReadOnlyDictionary<string, string> values) => "Hinge";
    }

    internal static int Run()
    {
        IoRegistry.ClearForTests();
        IoApiCore.ClearForTests();
        var logs = new List<string>();
        IoApiCore.Log = logs.Add;

        ApiValidation(logs);
        CallbackFailures(logs);
        EngineStillRuns();
        UnknownRule();
        RoundTripSaveLoad();
        ApplyGate();
        Braking();
        Config06();
        Shim();

        IoRegistry.ClearForTests();
        IoApiCore.ClearForTests();
        return checks;
    }

    private static void ApiValidation(List<string> logs)
    {
        var vehicle = new GameObject();
        int before = logs.Count;
        True(!IoApi.RegisterLevelInput("bad id", "Bad", "Test", _ => false), "invalid level id rejected");
        True(logs.Count == before + 1, "invalid id logs exactly one line");
        True(!IoApi.RegisterOutput("bad/output", "Bad", "comp", (_, _) => { }, _ => null, (_, _) => { }), "invalid output id rejected");
        True(logs.Count == before + 2, "invalid output id logs exactly one line");

        True(IoApi.RegisterLevelInput("test.api.level", "Test level", "API tests", _ => true), "valid level registration");
        True(IoRegistry.FindInput("test.api.level") != null, "facade maps input into IoRegistry");
        before = logs.Count;
        True(!IoApi.RegisterLevelInput("test.api.level", "Duplicate", "API tests", _ => true), "duplicate input rejected");
        True(logs.Count == before + 1, "duplicate input logs exactly one line");

        True(IoApi.RegisterEventInput("test.api.event", "Test event", "API tests", _ => 1), "valid event registration");
        True(IoApi.RegisterOutput("test.api.out", "Test output", "comp", (_, _) => { }, _ => null, (_, _) => { }), "valid output registration");
        True(IoRegistry.FindOutput("test.api.out") != null, "facade maps output into IoRegistry");
        before = logs.Count;
        True(!IoApi.RegisterLevelInput("test.api.out", "Cross duplicate", "API tests", _ => false), "input/output ids share a namespace");
        True(logs.Count == before + 1, "cross duplicate logs exactly one line");
        True(!IoApi.RegisterOutput("test.api.out", "Duplicate output", "comp", (_, _) => { }, _ => null, (_, _) => { }), "duplicate output rejected");
        True(logs.Count == before + 2, "duplicate output logs exactly one line");

        var sources = RuleModel.SourcesOf("API tests");
        True(sources.Count == 2, "two source-less API inputs in one group remain two sources");
        var eventLink = new Link(new InputInstance("test.api.event"), IoCommand.Open);
        True(RuleModel.Derive(eventLink).Source.Label == "Test event", "API source derives from its own type, not the first in the group");
        IoApiCore.ResetVehicle(vehicle);
    }

    private static void CallbackFailures(List<string> logs)
    {
        var vehicle = new GameObject();
        var part = new GameObject();
        int before = logs.Count;
        True(IoApi.RegisterLevelInput("test.api.throws", "Throws", "API tests", _ => throw new InvalidOperationException("boom")), "throwing input registers");
        var type = IoRegistry.FindInput("test.api.throws");
        True(type != null, "throwing input is in registry");
        var input = new InputInstance("test.api.throws");
        var context = new FakeContext { VehicleToken = vehicle };
        var first = type!.Read(input, context);
        True(!first.Level && first.Events == 0, "throwing input returns off");
        True(IoApiCore.IsDisabled("test.api.throws", vehicle), "throwing input is disabled for this vehicle");
        int afterFirst = logs.Count;
        var second = type.Read(input, context);
        True(!second.Level && logs.Count == afterFirst, "disabled input is not called again and does not log again");
        True(afterFirst == before + 1, "throwing input logs exactly one line");

        before = logs.Count;
        True(IoApi.RegisterOutput("test.api.badOut", "Bad output", "comp", (_, _) => throw new InvalidOperationException("apply boom"), _ => null, (_, _) => { }), "throwing output registers");
        True(!IoApiCore.TryApply("test.api.badOut", vehicle, part, true), "throwing output apply returns false");
        True(IoApiCore.IsDisabled("test.api.badOut", vehicle), "throwing output is disabled for this vehicle");
        int afterOutput = logs.Count;
        True(!IoApiCore.TryApply("test.api.badOut", vehicle, part, false), "disabled output is not called again");
        True(logs.Count == afterOutput && afterOutput == before + 1, "throwing output logs exactly one line and stays off");

        IoApiCore.ResetVehicle(vehicle);
        True(!IoApiCore.IsDisabled("test.api.badOut", vehicle), "next spawn clears disabled state");
    }

    private static void EngineStillRuns()
    {
        var ctx = new FakeContext { VehicleToken = new GameObject() };
        int events = 0;
        True(IoApi.RegisterEventInput("test.api.work", "Work", "API tests", _ => ++events), "working event registers");
        var engine = new IoEngine();
        engine.Configure(new[]
        {
            ("A", (IEnumerable<Link>)new[] { new Link(new InputInstance("test.api.throws"), IoCommand.Open) }),
            ("B", new[] { new Link(new InputInstance("test.api.work"), IoCommand.Open) })
        });
        engine.Tick(1, ctx);
        True(engine.IsOpen("B", 1) && !engine.IsOpen("A", 1), "throwing input does not stop another rule in the same engine");
        True(events == 1, "working callback was read once");
    }

    private static void UnknownRule()
    {
        const string json = "{\"Version\":1,\"Name\":\"X\",\"Output\":{\"Type\":\"hinge\",\"Params\":{}},\"Links\":[" +
            "{\"Input\":{\"Type\":\"other.mod.trigger\",\"Params\":{\"zone\":\"north\"}},\"Command\":\"Open\",\"Delay\":0,\"Duration\":0}]}";
        var config = HingeConfigJson.Parse(json);
        True(config.Links.Count == 0 && config.PreservedLinks.Count == 1, "unknown input kept literally, not active");
        True(HingeConfigJson.Write(config).Contains("other.mod.trigger") && HingeConfigJson.Write(config).Contains("north"), "unknown input written back unchanged");
        var engine = new IoEngine();
        engine.Configure(new[] { ("H", (IEnumerable<Link>)config.Links) });
        engine.Tick(1, new FakeContext { VehicleToken = new GameObject() });
        True(!engine.IsOpen("H", 1), "unknown input is not executed");
    }

    private static void RoundTripSaveLoad()
    {
        var vehicle = new GameObject();
        var part = new GameObject();
        string? stored = null;
        True(IoApi.RegisterOutput("test.api.rules", "Rules output", "comp", (_, _) => { }, _ => stored, (_, json) => stored = json), "rules output registers");
        var config = new HingeConfig { Name = "Bulb rules", OutputType = "test.api.out" };
        config.Links.Add(new Link(new InputInstance("test.api.event"), IoCommand.Pulse, .25f, 3));
        string json = HingeConfigJson.Write(config);
        True(IoApiCore.TrySaveRules("test.api.rules", vehicle, part, json), "saveRules callback succeeds");
        True(IoApiCore.TryLoadRules("test.api.rules", vehicle, part, out var loaded), "loadRules callback succeeds");
        True(loaded == json, "saveRules -> loadRules returns the same JSON");
        var back = HingeConfigJson.Parse(loaded);
        True(back.Links.Count == 1 && back.Links[0].Input.Equals(config.Links[0].Input) && back.Links[0].Command == IoCommand.Pulse
             && Math.Abs(back.Links[0].DelaySeconds - .25f) < 1e-6 && back.Links[0].DurationSeconds == 3, "round-tripped rule is unchanged");
    }

    private static void ApplyGate()
    {
        var gate = new OutputChangeGate();
        True(gate.ShouldApply(false), "initial false state is applied once");
        gate.AppliedState(false);
        True(!gate.ShouldApply(false), "unchanged false state is not applied again");
        True(gate.ShouldApply(true), "change to true is applied");
        gate.AppliedState(true);
        True(!gate.ShouldApply(true) && gate.ShouldApply(false), "unchanged true is skipped, false change is due");
        gate.AppliedState(false);
        gate.Reset();
        True(gate.ShouldApply(false) && gate.Applied == 0, "reset applies the next spawn state once");
    }

    private static void Braking()
    {
        True(BrakingPolicy.IsBraking(new BrakingFields(true, 7, true, 6, true, .6f, .6f)), "action Braking with both tracks true");
        True(!BrakingPolicy.IsBraking(new BrakingFields(true, 7, true, 6, true, .6f, .05f)), "one-sided brake does not count");
        True(!BrakingPolicy.IsBraking(new BrakingFields(true, 7, true, 6, true, .6f, .3f)), "strongly asymmetric brake does not count");
        True(BrakingPolicy.IsBraking(new BrakingFields(true, 7, true, 6, true, .6f, .5f)), "small asymmetry still counts");
        True(BrakingPolicy.IsBraking(new BrakingFields(true, 7, true, 6, true, 0, 0)), "both fractions near zero still use action");
        True(BrakingPolicy.IsBraking(new BrakingFields(true, 7, true, 6, false, 0, 0)), "layout without fractions uses action");
        True(!BrakingPolicy.IsBraking(new BrakingFields(true, 6, true, 7, true, .6f, .6f)), "coasting with lastAction braking is not braking");
        True(!BrakingPolicy.IsBraking(new BrakingFields(false, 7, false, 6, true, .6f, .6f)), "missing action fields stays false");
    }

    private static void Config06()
    {
        if (IoRegistry.FindOutput("hinge") == null) IoRegistry.RegisterOutput(new TestHingeOutput());
        if (IoRegistry.FindInput("vehicle.braking") == null) IoRegistry.RegisterInput(new VehicleBrakingInput());
        const string v06 = "{\"Version\":1,\"Name\":\"Brake flap\",\"Output\":{\"Type\":\"hinge\",\"Params\":{\"closedAngle\":0,\"openAngle\":90,\"speed\":90}},\"Links\":[" +
            "{\"Input\":{\"Type\":\"vehicle.braking\",\"Params\":{}},\"Command\":\"Follow\",\"Delay\":0,\"Duration\":0}]}";
        var config = HingeConfigJson.Parse(v06);
        True(config.Links.Count == 1 && config.PreservedLinks.Count == 0, "0.6 vehicle.braking rule loads");
        Eq(HingeConfigJson.Write(config), v06, "0.6 config saves byte-identical");
    }

    private static void Shim()
    {
        IL2CPPChainloader.Instance.Plugins.Clear();
        True(!IoApiClient.TryBind(), "missing mast does not bind and does not throw");
        True(!IoApiClient.Available && IoApiClient.ApiVersion == 0, "missing mast leaves the client unavailable");

        StubFacade.LevelCalls = 0; StubFacade.OutputCalls = 0;
        True(IoApiClient.Bind(typeof(StubFacade)), "stub facade with ApiVersion 1 binds");
        True(IoApiClient.Available && IoApiClient.ApiVersion == 1, "shim reports v1");
        True(IoApiClient.RegisterLevelInput("shim.level", "Shim", "Test", _ => false), "level delegate bound");
        True(IoApiClient.RegisterOutput("shim.out", "Shim out", "comp", (_, _) => { }, _ => null, (_, _) => { }, "On", "Off"), "output delegate bound");
        True(StubFacade.LevelCalls == 1 && StubFacade.OutputCalls == 1, "delegates reach the facade");

        True(!IoApiClient.Bind(typeof(BadFacade)), "ApiVersion 0 is refused");
        True(!IoApiClient.Available, "refused facade leaves the shim unavailable");
        True(!IoApiClient.RegisterLevelInput("x", "x", "x", _ => false), "calls after refusal are no-ops");
        True(!IoApiClient.Bind(null), "null facade is refused without throwing");
    }
}
