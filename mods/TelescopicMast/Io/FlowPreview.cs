namespace SprocketTelescopicMast.Io;

/// <summary>Designer sandbox. It never samples registered callbacks or applies to real parts.</summary>
public sealed class FlowPreview
{
    public FlowEngine Engine { get; } = new();
    private readonly HashSet<string> held = new(), fired = new();
    private readonly HashSet<string> forced = new();
    private readonly Dictionary<string, double> positions = new();
    private readonly Dictionary<string, RotatorMotion> rotators = new();
    private IReadOnlyList<FlowPart> parts = Array.Empty<FlowPart>();
    private FlowDocument flow = new();
    private double last;
    public FlowPreview() { Engine.InputOverride = Read; }
    public void Configure(FlowDocument document, Func<string, bool> exists, IReadOnlyList<FlowPart>? available = null)
    { flow = document; parts = available ?? Array.Empty<FlowPart>(); Engine.PartExists = exists; Engine.Configure(flow); }
    private InputSample? Read(FlowNode n)
    {
        bool value = held.Contains(n.Id);
        if (!forced.Contains(n.Id) && n.Type == "rotator.state")
        {
            var part = parts.FirstOrDefault(p => p.Type == "rotator" && p.Id == n.Get("plate"));
            var output = flow.Nodes.FirstOrDefault(o => o.Type == "rotator" && o.PartId == part?.Id);
            value = part != null && RotatorMotion.InRange(output == null ? 0 : Position(output.Id),n.Number("from"),n.Number("to",359));
        }
        if (!forced.Contains(n.Id) && n.Type == "hinge.state")
        {
            var hinges = parts.Where(p => p.Type == "hinge").ToArray();
            var part = hinges.FirstOrDefault(p => p.Selector == n.Get("hinge"));
            if (part == null && PartSelector.TryParse(n.Get("hinge"), out _, out var name))
            { var matches = hinges.Where(p => p.Title == name).ToArray(); if (matches.Length == 1) part = matches[0]; }
            var output = flow.Nodes.FirstOrDefault(o => o.Type == "hinge" && o.PartId == part?.Id);
            if (output != null)
            {
                bool open = n.Get("state", "open") != "closed";
                value = Engine.Target(output.Id) == open && Math.Abs(Position(output.Id) - output.Number(open ? "openAngle" : "closedAngle", open ? 90 : 0)) < .01;
            }
        }
        if (!forced.Contains(n.Id) && n.Type == "mast.state")
        {
            var fractions = parts.Where(p => p.Type == "mast").Select(p =>
                flow.Nodes.FirstOrDefault(o => o.Type == "mast" && o.PartId == p.Id) is { } o ? Position(o.Id) : 0).ToArray();
            value = fractions.Length > 0 && (n.Get("state", "extended") switch
            { "retracted" => fractions.All(f => f <= .001), "raised" => fractions.Any(f => f > .001), _ => fractions.Any(f => f >= .999) });
        }
        return new InputSample(value, fired.Contains(n.Id) ? 1 : 0);
    }
    public bool Held(string id) => held.Contains(id);
    public void Test(FlowNode node)
    {
        var type = IoRegistry.FindInput(node.Type); if (type == null) return;
        forced.Add(node.Id);
        if (type.SignalKind == SignalKind.Event && !(node.Type == "key" && node.Get("signal") == "held")) fired.Add(node.Id);
        else if (!held.Remove(node.Id)) held.Add(node.Id);
    }
    public double Position(string id) => positions.GetValueOrDefault(id);
    public void Tick(double now)
    {
        double dt = Math.Clamp(now - last, 0, .25); last = now;
        Engine.Tick(now, null); fired.Clear();
        foreach (var n in flow.Nodes.Where(FlowValidation.IsOutput))
        {
            if (Engine.Status.GetValueOrDefault(n.Id) == "missing part") continue;
            if (n.Type == "rotator")
            {
                if (!rotators.TryGetValue(n.Id,out var motion)) rotators[n.Id]=motion=new();
                motion.Tick(n,Engine.Rotator(n.Id),dt); positions[n.Id]=motion.Angle;
                Engine.RotatorFooters[n.Id]=motion.Footer(n); continue;
            }
            double pos = Position(n.Id), target = Engine.Target(n.Id) ? 1 : 0;
            if (n.Type == "hinge") target = Engine.Target(n.Id) ? n.Number("openAngle", 90) : n.Number("closedAngle");
            double speed = n.Type == "hinge" ? n.Number("speed", 90) : .2;
            positions[n.Id] = pos + Math.Clamp(target - pos, -speed * dt, speed * dt);
        }
    }
}
