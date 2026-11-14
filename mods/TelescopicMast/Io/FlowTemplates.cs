namespace SprocketTelescopicMast.Io;

public sealed record FlowTemplate(string Id, string Title);

/// <summary>Plans target selection without changing the graph or reading native state.</summary>
public static class FlowTemplates
{
    public const string Reload = "shell-reload", Fired = "shell-pulse", Brake = "brake";
    public const string Spin = "rotator-spin", Home = "rotator-home";
    public const string MastFired = "mast-after-firing";
    public static IReadOnlyList<FlowTemplate> All { get; } = new[]
    {
        new FlowTemplate(Reload, "Shell flap: open while reloading"),
        new FlowTemplate(Fired, "Shell flap: pulse after firing"),
        new FlowTemplate(Brake, "Brake light"),
        new FlowTemplate(MastFired, "Mast: raise after firing"),
        new FlowTemplate(Spin, "Radar: spin while key held"),
        new FlowTemplate(Home, "Turntable: go home on key")
    };
    public static FlowPart? Target(string id, FlowDocument flow, FlowNode? selected, IReadOnlyList<FlowPart> parts)
    {
        if (flow.ReadOnly || !All.Any(t => t.Id == id)) return null;
        if (id == MastFired) return parts.FirstOrDefault(p => p.Type == "mast" && p.Id == selected?.PartId)
            ?? parts.FirstOrDefault(p => p.Type == "mast" && !flow.Nodes.Any(n => n.PartId == p.Id && flow.Wires.Any(w => w.To == n.Id)));
        if (id is Spin or Home) return parts.FirstOrDefault(p => p.Type == "rotator" && p.Id == selected?.PartId)
            ?? parts.FirstOrDefault(p => p.Type == "rotator" && !flow.Nodes.Any(n => n.PartId == p.Id && flow.Wires.Any(w => w.To == n.Id)));
        var hinge = parts.FirstOrDefault(p => p.Type == "hinge" && p.Id == selected?.PartId);
        bool Free(FlowPart part) => !flow.Nodes.Any(n => n.PartId == part.Id && flow.Wires.Any(w => w.To == n.Id));
        if (id == Brake) return parts.FirstOrDefault(p => p.Type != "hinge" && p.Type != "mast" && p.Type != "rotator" && Free(p)) ?? hinge;
        return hinge ?? parts.FirstOrDefault(p => p.Type == "hinge" && Free(p));
    }
}
