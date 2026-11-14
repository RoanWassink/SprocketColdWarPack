using SprocketTelescopicMast.Io;

// The original suites record every configured link table and serialized config in this test-only build.
// The shipping assembly has no recorder or test dependency.
internal static class MigrationFixtures
{
    private sealed record Fixture(string Label, (string Id, Link[] Links)[] Table, Dictionary<string, (SignalKind Kind, ParamSchema Schema)> Types);
    private static readonly List<Fixture> All = new();
    private static bool recording = true;
    internal static void Capture(IEnumerable<(string OutputId, IEnumerable<Link> Links)> table)
    {
        if (!recording) return;
        var rows = table.Select(t => (t.OutputId, t.Links.Select(l => l.Clone()).ToArray())).ToArray();
        var types = rows.SelectMany(t => t.Item2).Select(l => l.Input.TypeId).Distinct().ToDictionary(id => id, id =>
        { var type = IoRegistry.FindInput(id); return (type?.SignalKind ?? SignalKind.Event, type?.Schema ?? ParamSchema.Empty); });
        var caller = new System.Diagnostics.StackTrace().GetFrames()?.Select(f => f.GetMethod()).FirstOrDefault(m => m?.DeclaringType?.Name.StartsWith("IoTests", StringComparison.Ordinal) == true);
        All.Add(new Fixture((caller?.DeclaringType?.Name ?? "unknown") + "." + (caller?.Name ?? "unknown"), rows, types));
    }
    internal static void Capture(HingeConfig config)
    { if (recording && !config.ReadOnly) Capture(new[] { ("config", (IEnumerable<Link>)config.Links) }); }
    internal static (int Fixtures, int Checks, int Deviations) Run()
    {
        recording = false;
        Directory.CreateDirectory("logs");
        File.WriteAllText("logs/migration-fixtures-0.8a.json", System.Text.Json.JsonSerializer.Serialize(All.Select(f => new { f.Label, Table = f.Table.Select(t => new { t.Id, Links = t.Links.Select(l => new { Type = l.Input.TypeId, Params = l.Input.Params, Command = l.Command.ToString(), l.DelaySeconds, l.DurationSeconds }) }) }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        int checks = 0, deviations = 0, index = 0; var lines = new List<string> { "fixture,links,ticks,mismatches,mixedToggle" };
        foreach (var fixture in All)
        {
            IoRegistry.ClearForTests(); var signals = new Dictionary<string, InputSample>();
            foreach (var (id, type) in fixture.Types)
                IoRegistry.RegisterInput(new FlowTests.Source { Id = id, SignalKind = type.Kind, Schema = type.Schema, Sample = i => signals.GetValueOrDefault(i.Key) });
            var configs = fixture.Table.Select((r, i) =>
            {
                var c = new HingeConfig(); c.Links.AddRange(r.Links.Select(l => l.Clone()));
                return (PartId: "part-" + i, Config: c);
            }).ToArray();
            var flow = FlowMigration.Migrate(configs); var graph = new FlowEngine(); graph.Configure(flow);
            var legacy = new IoEngine(); legacy.Configure(fixture.Table.Select(r => (r.Id, (IEnumerable<Link>)r.Links)));
            var instances = fixture.Table.SelectMany(t => t.Links).Select(l => l.Input).Distinct().ToArray();
            int mismatch = 0; bool mixed = fixture.Table.Any(r => r.Links.Any(l => l.Command == IoCommand.Toggle) && r.Links.Select(l => l.Command).Distinct().Count() > 1);
            // Fixed 40s trace, 50ms ticks, offset levels/events, both sparse and overlapping stimuli.
            for (int tick = 0; tick <= 800; tick++)
            {
                double now = tick * .05;
                for (int i = 0; i < instances.Length; i++)
                {
                    var inst = instances[i]; int phase = (tick + i * 31) % 200;
                    bool level = phase >= 20 && phase < 60;
                    bool ev = phase == 20 || phase == 120;
                    signals[inst.Key] = fixture.Types[inst.TypeId].Kind == SignalKind.Event ? new InputSample(false, ev ? 1 : 0) : new InputSample(level, 0);
                }
                legacy.Tick(now, null); graph.Tick(now, null);
                for (int i = 0; i < fixture.Table.Length; i++)
                {
                    var node = flow.Nodes.Single(n => n.PartId == "part-" + i);
                    bool expected = legacy.IsOpen(fixture.Table[i].Id, now), actual = graph.Target(node.Id); checks++;
                    if (expected != actual) mismatch++;
                }
            }
            if (mismatch > 0 && !mixed) throw new Exception($"Migration parity failed for non-mixed-Toggle fixture {index}: {mismatch} mismatches.");
            if (mismatch > 0)
            {
                deviations++;
                File.WriteAllText($"logs/migration-fixture-{index}.json", System.Text.Json.JsonSerializer.Serialize(fixture.Table.Select(t => new { t.Id, Links = t.Links.Select(l => new { Type = l.Input.TypeId, Params = l.Input.Params, Command = l.Command.ToString(), l.DelaySeconds, l.DurationSeconds }) }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            lines.Add($"{index++}:{fixture.Label},{fixture.Table.Sum(t => t.Links.Length)},801,{mismatch},{mixed}");
        }
        Directory.CreateDirectory("logs"); File.WriteAllLines("logs/migration-0.8a.csv", lines);
        return (All.Count, checks, deviations);
    }
}
