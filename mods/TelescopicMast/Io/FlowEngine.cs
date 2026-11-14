namespace SprocketTelescopicMast.Io;

/// <summary>Accumulates only active game frames; a native pause cannot age pending graph work.</summary>
public sealed class FlowGameClock
{
    public double Now { get; private set; }
    public double Advance(double deltaSeconds, bool paused = false)
    { if (!paused && double.IsFinite(deltaSeconds) && deltaSeconds > 0) Now += deltaSeconds; return Now; }
}

/// <summary>One managed graph per vehicle. Time is monotonic game time; equal times do no work.</summary>
public sealed class FlowEngine
{
    private sealed class State
    {
        internal bool A, B, On, Value, Target, Initialized;
        internal int Count;
        internal RotatorCommand RotatorRequested;
        internal string Part = "";
        internal double Until = double.NegativeInfinity, PausedUntil = double.NegativeInfinity;
        internal readonly Queue<(double Due, bool Value, bool Event)> Delays = new();
        internal readonly Queue<double> Changes = new();
    }
    private FlowDocument flow = new();
    private readonly Dictionary<string, State> states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> values = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, int), string> incoming = new();
    private readonly Dictionary<string, RotatorCommand> rotators = new();
    public readonly Dictionary<string, string> RotatorFooters = new();
    public RotatorCommand Rotator(string id) => rotators.GetValueOrDefault(id);
    private readonly List<FlowNode> order = new();
    private readonly HashSet<string> cyclic = new();
    private readonly Dictionary<string, double> displayPulses = new();
    private double lastNow = double.NegativeInfinity;
    public bool Loop { get; private set; }
    public Dictionary<string, string> Status { get; } = new();
    public Dictionary<string, string> InputErrors { get; } = new();
    public Action<string, double>? LoopDetected { get; set; }
    public Func<string, bool> PartExists { get; set; } = _ => true;
    /// <summary>Only an initial value or an accepted target change calls Apply.</summary>
    public Action<FlowNode, bool>? Apply { get; set; }
    public Func<FlowNode, InputSample?>? InputOverride { get; set; }
    public int Count(string id) => states.TryGetValue(id, out var s) ? s.Count : 0;
    public bool Value(string id) => values.TryGetValue(id, out var v) && v;
    /// <summary>Keep momentary event signals visible for one 10 Hz UI interval; evaluation remains one tick.</summary>
    public bool DisplayValue(string id) => Value(id) || displayPulses.TryGetValue(id, out var at) && lastNow - at < .1;
    public bool Target(string id) => states.TryGetValue(id, out var s) && s.Target;
    public bool Paused(string id, double now) => states.TryGetValue(id, out var s) && now < s.PausedUntil;
    public void Configure(FlowDocument document)
    {
        flow = document; order.Clear(); cyclic.Clear(); incoming.Clear(); Status.Clear(); rotators.Clear(); RotatorFooters.Clear();
        var ids = new HashSet<string>();
        foreach (var n in flow.Nodes)
        {
            if (!ids.Add(n.Id)) throw new ArgumentException("Duplicate node id.");
            if (!states.TryGetValue(n.Id, out var s)) states[n.Id] = s = new State();
            if (FlowValidation.IsOutput(n) && s.Part != n.PartId) { s.Part = n.PartId; s.Initialized = false; }
        }
        foreach (var gone in states.Keys.Where(k => !ids.Contains(k)).ToArray()) { states.Remove(gone); values.Remove(gone); displayPulses.Remove(gone); }
        var degree = flow.Nodes.ToDictionary(n => n.Id, _ => 0); var adj = flow.Nodes.ToDictionary(n => n.Id, _ => new List<string>());
        foreach (var w in flow.Wires)
        {
            if (FlowValidation.ValidateWire(flow, w) != null) continue; // preserve invalid/future wires, never execute
            if (!incoming.TryAdd((w.To, w.Port), w.From)) throw new ArgumentException("More than one wire per input.");
            degree[w.To]++; adj[w.From].Add(w.To);
        }
        var q = new Queue<string>(flow.Nodes.Where(n => degree[n.Id] == 0).Select(n => n.Id));
        var byId = flow.Nodes.ToDictionary(n => n.Id);
        while (q.Count > 0) { var id = q.Dequeue(); order.Add(byId[id]); foreach (var to in adj[id]) if (--degree[to] == 0) q.Enqueue(to); }
        // Unresolved nodes (including descendants of cycles) form the final, previous-tick partition.
        foreach (var n in flow.Nodes.Where(n => degree[n.Id] > 0)) { cyclic.Add(n.Id); order.Add(n); }
        Loop = cyclic.Count > 0;
    }
    public void Tick(double now, object? context)
    {
        if (!double.IsFinite(now) || now <= lastNow || flow.ReadOnly) return;
        lastNow = now; InputErrors.Clear();
        var previous = new Dictionary<string, bool>(values);
        var sampled = new Dictionary<string, InputSample>();
        var events = new HashSet<string>();
        var claimed = new HashSet<string>();
        foreach (var n in order)
        {
            var s = states[n.Id]; bool v = false;
            bool In(int p)
            {
                if (!incoming.TryGetValue((n.Id, p), out var source)) return false;
                var src = cyclic.Contains(n.Id) && cyclic.Contains(source) ? previous : values;
                return src.TryGetValue(source, out var b) && b;
            }
            bool a = In(0), b = In(1);
            bool Event(int p) => incoming.TryGetValue((n.Id, p), out var src) && events.Contains(src);
            bool legacy = n.Get("_legacyEvents") == "true";
            bool riseA = (a && !s.A) || (legacy && Event(0));
            bool riseB = (b && !s.B) || (legacy && Event(1));
            if (FlowValidation.IsOutput(n))
            {
                if (!PartExists(n.PartId)) { Status[n.Id] = "missing part"; values[n.Id] = false; rotators.Remove(n.Id); continue; }
                if (!claimed.Add(n.PartId)) { Status[n.Id] = "duplicate part"; values[n.Id] = false; rotators.Remove(n.Id); continue; }
                if (n.Type == "rotator")
                {
                    var command = new RotatorCommand(a, b, In(2), In(3));
                    bool changedCommand = !s.Initialized || command != s.RotatorRequested;
                    s.RotatorRequested = command;
                    if (changedCommand)
                    {
                        while (s.Changes.Count > 0 && now - s.Changes.Peek() >= 1) s.Changes.Dequeue();
                        s.Changes.Enqueue(now);
                        if (s.Changes.Count > 10) { s.PausedUntil = now + 2; s.Changes.Clear(); LoopDetected?.Invoke(n.Id, now); }
                    }
                    rotators[n.Id] = now < s.PausedUntil ? default : command;
                    if (now < s.PausedUntil) Status[n.Id] = "loop paused"; else Status.Remove(n.Id);
                    bool on = now >= s.PausedUntil && (command.Home || command.Run);
                    if (changedCommand || s.Target != on) { s.Target = on; Apply?.Invoke(n, on); }
                    s.Initialized = true; s.A = a; s.B = b; values[n.Id] = on; continue;
                }
                bool target = s.Target;
                string mode = n.Get("mode", "Follow");
                if (mode == "Follow") target = a;
                else if (riseA) target = mode == "Toggle" ? !s.Target : mode == "Open" || (mode != "Close" && s.Target);
                bool changed = target != s.Target;
                if (now >= s.PausedUntil && changed)
                {
                    while (s.Changes.Count > 0 && now - s.Changes.Peek() >= 1) s.Changes.Dequeue();
                    s.Changes.Enqueue(now);
                    if (s.Changes.Count > 10) { s.PausedUntil = now + 2; s.Changes.Clear(); LoopDetected?.Invoke(n.Id, now); }
                }
                if (now < s.PausedUntil) Status[n.Id] = "loop paused";
                else
                {
                    Status.Remove(n.Id);
                    if (!s.Initialized || changed) { s.Target = target; s.Initialized = true; Apply?.Invoke(n, target); }
                }
                s.A = a; values[n.Id] = a; continue;
            }
            switch (n.Type)
            {
                case "always": v = true; break;
                case "and": v = a && b; break;
                case "or":
                    if (n.Get("_legacyEdges") == "true")
                    { v = a && !s.A || b && !s.B || Event(0) || Event(1); if (v) events.Add(n.Id); }
                    else { v = a || b; if (Event(0) || Event(1)) events.Add(n.Id); }
                    break;
                case "not": v = !a; break;
                case "delay":
                    if (a != s.A || Event(0)) s.Delays.Enqueue((now + Math.Max(0, n.Number("t", 1)), a, Event(0)));
                    while (s.Delays.Count > 0 && s.Delays.Peek().Due <= now)
                    { var due = s.Delays.Dequeue(); s.Value = due.Value; if (due.Event) events.Add(n.Id); }
                    v = s.Value; break;
                case "pulse":
                    if (riseA) s.Until = now + Math.Max(0, n.Number("t", 1));
                    v = now < s.Until; break;
                case "counter":
                    if (a && !s.A) s.Count++; if (b && !s.B) s.Count = 0;
                    v = s.Count >= n.Number("n", 3); break;
                case "memory":
                    if (riseA) s.On = true; if (riseB) s.On = false;
                    v = s.On; break;
                default:
                    var type = IoRegistry.FindInput(n.Type);
                    if (type == null) { Status[n.Id] = "unknown block"; break; }
                    try
                    {
                        var instance = new InputInstance(n.Type, n.InputParams);
                        var overridden = InputOverride?.Invoke(n);
                        if (!sampled.TryGetValue(instance.Key, out var sample) || overridden.HasValue)
                            sampled[instance.Key] = sample = overridden ?? type.Read(instance, context);
                        bool heldKey = n.Type == "key" && n.Get("signal") == "held";
                        v = !heldKey && type.SignalKind == SignalKind.Event ? sample.Events > 0 : sample.Level;
                        if (type.SignalKind == SignalKind.Event && sample.Events > 0) events.Add(n.Id);
                        Status.Remove(n.Id);
                    }
                    catch (Exception ex) { InputErrors[n.Type] = ex.Message; }
                    break;
            }
            s.A = a; s.B = b; values[n.Id] = v;
        }
        foreach (var id in events) displayPulses[id] = now;
    }
}
