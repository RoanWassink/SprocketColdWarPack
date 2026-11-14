using System.Globalization;
using System.Text.Json;
namespace SprocketTelescopicMast.Io;

public static class FlowMigration
{
    /// <summary>Deterministic columns: input, delay, pulse/OR, memory, output. Source JSON is retained for downgrade.</summary>
    public static FlowDocument Migrate(IEnumerable<(string PartId, HingeConfig Config)> configs, Action<string>? warning = null)
    {
        var f = new FlowDocument { Rev = 1 }; int seq = 0, row = 0;
        FlowNode Node(string type, int column, string title)
        { var n = new FlowNode("m" + ++seq, type, 32 + column * 232, 40 + row * 112, title); f.Nodes.Add(n); return n; }
        void Wire(FlowNode a, FlowNode b, int port = 0) => f.Wires.Add(new FlowWire(a.Id, b.Id, port));
        FlowNode? Or(List<FlowNode> list, bool edges = false)
        {
            if (list.Count == 0) return null;
            var a = list[0]; foreach (var b in list.Skip(1)) { var o = Node("or", 3, "Any signal"); if (edges) o.Set("_legacyEdges", "true"); Wire(a, o); Wire(b, o, 1); a = o; row++; } return a;
        }
        bool warned = false;
        foreach (var (partId, c) in configs.OrderBy(c => c.PartId, StringComparer.Ordinal))
        {
            f.Extra["legacy:" + partId] = JsonDocument.Parse(HingeConfigJson.Write(c)).RootElement.Clone();
            if (c.ReadOnly) continue;
            var output = new FlowNode("m" + ++seq, c.OutputType, 32 + 5 * 232, 40 + row * 112, c.Name);
            output.Set("partId", partId); foreach (var p in c.OutputParams) output.Set(p.Key, p.Value);
            output.Set("_legacyEvents", "true");
            var chains = new List<(FlowNode Tail, IoCommand Command)>();
            foreach (var link in c.Links)
            {
                var input = Node(link.Input.TypeId, 0, link.Input.Describe()); foreach (var p in link.Input.Params) input.Set(p.Key, p.Value);
                var tail = input;
                if (link.DelaySeconds > 0) { var d = Node("delay", 1, "Delay"); d.Set("t", ((double)link.DelaySeconds).ToString("R", CultureInfo.InvariantCulture)); Wire(tail, d); tail = d; }
                if (link.Command == IoCommand.Pulse) { var p = Node("pulse", 2, "Pulse"); p.Set("_legacyEvents", "true"); p.Set("t", ((double)link.DurationSeconds).ToString("R", CultureInfo.InvariantCulture)); Wire(tail, p); tail = p; }
                chains.Add((tail, link.Command)); row++;
            }
            var commands = chains.Select(c => c.Command).Distinct().ToArray();
            if (commands.Length <= 1)
            {
                output.Set("mode", commands.Length == 0 || commands[0] is IoCommand.Pulse or IoCommand.Follow ? "Follow" : commands[0].ToString());
                var merged = Or(chains.Select(c => c.Tail).ToList(), commands.Length > 0 && commands[0] is not (IoCommand.Pulse or IoCommand.Follow)); if (merged != null) Wire(merged, output);
            }
            else
            {
                bool mixedToggle = commands.Contains(IoCommand.Toggle);
                if (mixedToggle && !warned) { warned = true; warning?.Invoke("Mixed Toggle commands migrated as Open alongside other commands; closest mapping."); }
                var open = Or(chains.Where(c => c.Command is IoCommand.Open or IoCommand.Toggle).Select(c => c.Tail).ToList(), true);
                var close = Or(chains.Where(c => c.Command == IoCommand.Close).Select(c => c.Tail).ToList(), true);
                var follows = chains.Where(c => c.Command is IoCommand.Follow or IoCommand.Pulse).Select(c => c.Tail).ToList();
                if (open != null || close != null)
                {
                    var memory = Node("memory", 4, "Open / close memory"); memory.Set("_legacyEvents", "true"); if (open != null) Wire(open, memory); if (close != null) Wire(close, memory, 1); follows.Insert(0, memory);
                }
                var merged = Or(follows); if (merged != null) Wire(merged, output); output.Set("mode", "Follow");
            }
            f.Nodes.Add(output); row++;
        }
        return f;
    }
}
