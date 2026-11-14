using System.Text;
using SprocketTelescopicMast.Io;
using UnityEngine;

/// <summary>0.9.3: IO-API v1.1 per-part data, managed outputs, part icons and M4.</summary>
internal static class Tests093
{
    private static int checks;
    private static readonly List<string> report = new();
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("0.9.3: " + why); }

    private sealed class Bag : IStateBag<string>
    {
        internal readonly List<string> Names = new(), Values = new();
        public int Count => Names.Count;
        public string NameAt(int index) => Names[index];
        public string? ValueAt(int index) => Values[index];
        public void Set(int index, string key, string value, string type) { Names[index] = key; Values[index] = value; }
        public void Add(string key, string value, string type) { Names.Add(key); Values.Add(value); }
    }

    public static int Run()
    {
        Managed(); Data(); Limits(); RoundTrip(); Icons(); M4(); Shim();
        Directory.CreateDirectory("logs");
        File.WriteAllLines("logs/tests-0.9.3-detail.txt", report);
        return checks;
    }

    private static void Managed()
    {
        IoRegistry.ClearForTests(); IoApiCore.ClearForTests();
        var parts = new[] { new GameObject(), new GameObject(), new GameObject() };
        int applied = 0;
        Check(IoApi.RegisterManagedOutput("vfx.light", "VFX light", "vfxLamp", (_, _) => applied++, "On", "Off"),
            "one managed registration for three part definitions");
        var registration = IoApiCore.FindOutput("vfx.light")!;
        Check(registration.Managed && registration.LoadRules == null && registration.SaveRules == null,
            "managed output has no loadRules/saveRules");
        Check(!IoApi.RegisterManagedOutput("vfx.light2", "VFX light 2", "vfxLamp", (_, _) => { }),
            "a second managed registration for the same ComponentID is refused");
        Check(IoApiCore.IsManagedComponent("vfxLamp") && IoApiCore.IsRegisteredComponent("vfxLamp") && !IoApiCore.IsManagedComponent("other"),
            "ComponentID lookup distinguishes managed and unregistered parts");
        for (int i = 0; i < parts.Length; i++)
        {
            IoApiCore.AttachPart(parts[i], "vfxLamp");
            Check(IoApi.SetPartData(parts[i], "visualeffects.colour", "c" + i), "part " + (i + 1) + " accepts data");
            Check(IoApi.GetPartData(parts[i], "visualeffects.colour") == "c" + i, "part " + (i + 1) + " keeps its own value");
        }
        Check(IoApiCore.TryLoadRules("vfx.light", new object(), parts[0], out var loaded) && loaded == null,
            "managed output skips loadRules");
        Check(applied == 0, "registration never calls apply itself");
        Check(IoApiCore.ExternalLabel("Lamp round", "VFX light", 1) == "Lamp round 1", "M2 name: part name plus number");
        Check(IoApiCore.ExternalLabel("", "VFX light", 2) == "VFX light 2", "M2 fallback: registration displayName plus number");
        report.Add("managed: one registration for 3 parts sharing vfxLamp; per-part data isolated; no rules callbacks; M2 label format");
    }

    private static void Data()
    {
        IoApiCore.ClearForTests(); IoRegistry.ClearForTests();
        Check(IoApi.RegisterOutput("vfx.legacy", "Legacy", "vfxBulb", (_, _) => { }, _ => null, (_, _) => { }) &&
            IoApiCore.FindOutput("vfx.legacy")!.Managed == false && IoApiCore.FindOutput("vfx.legacy")!.LoadRules != null,
            "IoApi.RegisterOutput is still the v1 rules path");
        var part = new GameObject();
        IoApiCore.AttachPart(part, "vfxBulb");
        Check(IoApi.SetPartData(part, "visualeffects.mode", "steady"), "v1 rules output also carries per-part data");
        Check(IoApi.SetPartData(part, "visualeffects.mode", null) && IoApi.GetPartData(part, "visualeffects.mode") == null, "null removes a key");
        Check(IoApiCore.PartDataJson(part) == "{}", "an emptied part still writes an empty JSON object");
        Check(!IoApi.SetPartData(new GameObject(), "visualeffects.x", "1"), "an unregistered part is refused");
        var unknown = new GameObject();
        int warnings = 0; IoApiCore.Log = _ => warnings++;
        Check(!IoApi.SetPartData(unknown, "visualeffects.x", "1"), "unregistered part refused again");
        Check(warnings == 1, "one warning per part, not per attempt");
        IoApiCore.Log = null;
        report.Add("data: v1 rules outputs keep their callbacks and also get ioPartDataV1; null removes; unknown parts refused once");
    }

    private static void Limits()
    {
        IoApiCore.ClearForTests();
        IoApiCore.TryRegisterOutput("vfx.light", "Lamp", "vfxLamp", (_, _) => { }, null, null, "On", "Off", true, out _, out _);
        var logs = new List<string>(); IoApiCore.Log = logs.Add;
        var part = new GameObject(); IoApiCore.AttachPart(part, "vfxLamp");
        string key64 = "visualeffects." + new string('k', 50);
        Check(key64.Length == 64 && IoApi.SetPartData(part, key64, "v"), "64-character key accepted");
        Check(!IoApi.SetPartData(part, key64 + "x", "v"), "65-character key refused");
        Check(!IoApi.SetPartData(part, "VisualEffects.x", "v"), "uppercase key refused");
        Check(!IoApi.SetPartData(part, "visualeffects", "v"), "key without a prefix dot refused");
        Check(!IoApi.SetPartData(part, "visualeffects..x", "v"), "empty segment refused");
        Check(!IoApi.SetPartData(part, "visualeffects.a b", "v"), "space refused");
        Check(!IoApi.SetPartData(part, "io.reserved", "v"), "'io.' prefix reserved");
        Check(logs.Count == 1, "all key failures warn once for this part");
        var valuePart = new GameObject(); IoApiCore.AttachPart(valuePart, "vfxLamp");
        Check(IoApi.SetPartData(valuePart, "visualeffects.value", new string('x', 4096)), "4 KB value accepted");
        Check(!IoApi.SetPartData(valuePart, "visualeffects.value", new string('x', 4097)), "4 KB + 1 refused");
        var big = new GameObject(); IoApiCore.AttachPart(big, "vfxLamp");
        for (int i = 0; i < 3; i++) Check(IoApi.SetPartData(big, "visualeffects.v" + i, new string('x', 4096)), "part value " + i + " fits");
        Check(!IoApi.SetPartData(big, "visualeffects.v3", new string('x', 4096)), "a fourth 4 KB value exceeds 16 KB per part");
        Check(logs.Count(l => l.Contains("16 KB")) == 1, "one 16 KB warning for that part");
        report.Add("limits: 64-char keys, [a-z0-9._-], prefix dot, io. reserved, 4 KB values, 16 KB per part, one warning per part");
    }

    private static void RoundTrip()
    {
        IoApiCore.ClearForTests();
        IoApiCore.TryRegisterOutput("vfx.light", "Lamp", "vfxLamp", (_, _) => { }, null, null, "On", "Off", true, out _, out _);
        var part = new GameObject(); IoApiCore.AttachPart(part, "vfxLamp");
        string partId = Guid.NewGuid().ToString("D");
        var flow = new FlowDocument { Rev = 7 };
        var always = new FlowNode("m1", "always", 0, 0, "Always on");
        var lamp = new FlowNode("m2", "vfx.light", 300, 0, "Lamp"); lamp.Set("partId", partId); lamp.Set("mode", "Follow");
        flow.Nodes.Add(always); flow.Nodes.Add(lamp); flow.Wires.Add(new FlowWire("m1", "m2", 0));
        string flowJson = FlowJson.Write(flow);
        IoApi.SetPartData(part, "visualeffects.colour", "#ffcc66");
        IoApi.SetPartData(part, "visualeffects.level", "3");

        var bag = new Bag(); bag.Add("vanillaMass", "12.5", "string");
        ExternalPartState.WriteFlow(bag, "string", partId, flow);
        ExternalPartState.WriteData(bag, "string", part);
        Check(bag.Count == 4 && bag.Names.Contains("ioPartId") && bag.Names.Contains("ioFlowV1") && bag.Names.Contains("ioPartDataV1"),
            "one ioPartDataV1 key plus ioPartId/ioFlowV1 on the same part state");
        Check(bag.Names[0] == "vanillaMass" && bag.Values[0] == "12.5", "another mod's entry is untouched");
        Check(StateMerge.Merge(bag, "ioPartDataV1", bag.Values[bag.Names.IndexOf("ioPartDataV1")], "string"), "a second save replaces ioPartDataV1 in place");

        // New spawn: no mast part, only this API part's own state.
        var reloaded = new GameObject(); IoApiCore.AttachPart(reloaded, "vfxLamp");
        string? partError = ExternalPartState.ReadFlow(bag, out var savedId, out var savedFlow);
        ExternalPartState.ReadData(bag, reloaded, "vfxLamp");
        Check(partError == null && savedId == partId && savedFlow != null && FlowJson.Write(savedFlow) == flowJson,
            "managed flow and ioPartId survive save/load without a mast part");
        Check(IoApi.GetPartData(reloaded, "visualeffects.colour") == "#ffcc66" && IoApi.GetPartData(reloaded, "visualeffects.level") == "3",
            "per-part data survives the same state round trip");
        ExternalPartState.ReadData(new Bag(), reloaded, "vfxLamp");
        Check(IoApi.GetPartData(reloaded, "visualeffects.colour") == "#ffcc66", "the transform backup without the data key does not erase loaded data");
        var storage = new FlowStorage(); storage.Attach("managed", savedId, savedFlow);
        Check(storage.Flow != null && FlowJson.Write(storage.Flow) == flowJson && storage.Contains(partId),
            "the loaded copy and id seed a vehicle flow with no native part");
        report.Add("roundtrip: ioPartId + ioFlowV1 + ioPartDataV1 through StateMerge; flow rev 7 and two values survive with no mast part");
    }

    private static void Icons()
    {
        IoApiCore.ClearForTests();
        string root = Path.GetFullPath("logs/icon-0.9.3/plugins");
        string outside = Path.GetFullPath("logs/icon-0.9.3/outside.png");
        if (Directory.Exists(Path.GetDirectoryName(root)!)) Directory.Delete(Path.GetDirectoryName(root)!, true);
        Directory.CreateDirectory(root); Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        string good = Path.Combine(root, "lamp.png"); File.WriteAllBytes(good, new byte[] { 1, 2, 3 });
        string second = Path.Combine(root, "second.png"); File.WriteAllBytes(second, new byte[] { 4 });
        string text = Path.Combine(root, "lamp.txt"); File.WriteAllText(text, "x");
        string huge = Path.Combine(root, "huge.png"); File.WriteAllBytes(huge, new byte[PartIcons.MaxBytes + 1]);
        File.WriteAllBytes(outside, new byte[] { 1 });
        var logs = new List<string>(); IoApiCore.Log = logs.Add;
        IoApiCore.ConfigurePluginRoot(root);
        string guid = Guid.NewGuid().ToString("D");
        Check(!IoApi.RegisterPartIcon(guid, outside), "a PNG outside BepInEx\\plugins is refused");
        Check(!IoApi.RegisterPartIcon(guid, text), "a non-PNG is refused");
        Check(!IoApi.RegisterPartIcon(guid, huge), "a PNG over 2 MB is refused");
        Check(!IoApi.RegisterPartIcon(guid, "relative.png"), "a relative path is refused");
        Check(IoApi.RegisterPartIcon(guid, good), "a valid PNG under BepInEx\\plugins is accepted");
        Check(!PartIcons.Requested(guid), "registration does not load the image");
        Check(IoApi.RegisterPartIcon(guid, second) == false, "a second registration for the same GUID is refused");
        Check(IoApiCore.TryGetPartIcon(guid, out var path) && path == good && PartIcons.Requested(guid), "the first card loads the first path");
        Check(!IoApiCore.TryGetPartIcon(Guid.NewGuid().ToString("D"), out _), "an unregistered GUID falls back to the native icon");
        Check(IoApi.ApiRevision == 2, "ApiRevision is 2");
        File.WriteAllLines("logs/icons-0.9.3.txt", new[] { "good: " + good, "outside: " + outside, "refusals: " + string.Join(" | ", logs), "first-wins path: " + path });
        report.Add("icons: absolute PNG under plugins, <=2 MB, first GUID wins, image only read on first card, own icons untouched");
    }

    private static void M4()
    {
        var power = new ExternalOutputPower();
        Check(power.ShouldApply(true), "first observation applies the initial state");
        power.Applied(true);
        Check(!power.ShouldApply(true), "an unchanged on state does not apply again");
        Check(power.TurnOff() && !power.LastApplied, "designer return applies off once");
        Check(!power.TurnOff(), "a second designer/release event does not apply again");
        power.Applied(false);
        Check(!power.TurnOff(), "an output that was off is not turned off again");
        power.Reset(); Check(power.ShouldApply(true) && !power.LastApplied, "a new spawn resets the gate");
        report.Add("M4: on -> one apply(false) at designer return or release, then no repeat; off stays off");
    }

    private static void Shim()
    {
        Debug.Warnings.Clear();
        Check(IoApiClient.Bind(typeof(OldFacade)), "the shim still binds a real v1 mast");
        Check(IoApiClient.ApiVersion == 1 && IoApiClient.ApiRevision == 0, "old mast: version 1, revision 0");
        Check(!IoApiClient.SetPartData(new GameObject(), "visualeffects.x", "v"), "old mast: SetPartData false");
        Check(IoApiClient.GetPartData(new GameObject(), "visualeffects.x") == null, "old mast: GetPartData null");
        Check(!IoApiClient.RegisterManagedOutput("vfx.light", "Lamp", "vfxLamp", (_, _) => { }), "old mast: RegisterManagedOutput false");
        Check(!IoApiClient.RegisterPartIcon(Guid.NewGuid().ToString("D"), "x.png"), "old mast: RegisterPartIcon false");
        Check(Debug.Warnings.Count == 1, "one log line for all missing v1.1 methods");
        Check(IoApiClient.TryBind() == false, "no mast: TryBind false");
        Check(IoApiClient.Bind(typeof(StubFacade)), "bind a v1.1 facade");
        Check(IoApiClient.ApiVersion == 1 && IoApiClient.ApiRevision == 1, "new mast: revision 1");
        StubFacade.SetCalls = StubFacade.GetCalls = StubFacade.ManagedCalls = StubFacade.IconCalls = 0;
        Check(IoApiClient.SetPartData(new GameObject(), "visualeffects.x", "v") && StubFacade.SetCalls == 1, "SetPartData is optional and bound");
        Check(IoApiClient.GetPartData(new GameObject(), "visualeffects.x") == "stub" && StubFacade.GetCalls == 1, "GetPartData is optional and bound");
        Check(IoApiClient.RegisterManagedOutput("vfx.light", "Lamp", "vfxLamp", (_, _) => { }) && StubFacade.ManagedCalls == 1, "RegisterManagedOutput is optional and bound");
        Check(IoApiClient.RegisterPartIcon(Guid.NewGuid().ToString("D"), "x.png") && StubFacade.IconCalls == 1, "RegisterPartIcon is optional and bound");
        Check(Debug.Warnings.Count == 1, "a bound v1.1 facade logs nothing");
        report.Add("shim: v1 facade binds with revision 0 and false/null plus one log line; v1.1 facade exposes all four additions");
    }
}
