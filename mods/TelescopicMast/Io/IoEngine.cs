namespace SprocketTelescopicMast.Io;

/// <summary>
/// One engine per vehicle. Fixed order per tick: sample inputs -> evaluate links (delays go
/// into a time queue) -> due queue items -> output targets -> loop watch. Pure logic; time is passed in.
/// Safety: at most one command per output per tick (Follow levels excepted), and outputs driven by
/// feedback inputs (IFeedbackInput, e.g. hinge.state) that flip too often have those links paused.
/// </summary>
public sealed class IoEngine
{
    /// <summary>Open/closed changes within LoopWindowSeconds that mark an output as looping.</summary>
    public const int LoopThreshold = 8;
    public const double LoopWindowSeconds = 10;

    private sealed class OutputState
    {
        internal bool Latched;
        internal double PulseUntil = double.NegativeInfinity;
        internal readonly Dictionary<int, bool> Follow = new();
        internal long LastCommandTick = -1;
        internal bool LastOpen, HasFeedback, Loop;
        internal readonly Queue<double> Changes = new();
    }
    private readonly record struct LinkSlot(int Id, string OutputId, Link Link, bool Feedback);
    private readonly record struct Pending(double Due, long Seq, string OutputId, IoCommand Command, float Duration, int FollowSlot, bool Level, bool Feedback);

    private readonly Dictionary<string, OutputState> outputs = new(StringComparer.Ordinal);
    private readonly List<LinkSlot> links = new();
    private readonly Dictionary<string, InputInstance> inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InputSample> samples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> previousLevel = new(StringComparer.Ordinal);
    private readonly Dictionary<int, bool> scheduledFollow = new();
    private readonly List<Pending> queue = new();
    private long sequence, tick;
    private double lastNow = double.NegativeInfinity;
    /// <summary>Errors per input type id from the last tick (for diagnostics); never thrown.</summary>
    public Dictionary<string, string> InputErrors { get; } = new(StringComparer.Ordinal);
    /// <summary>Fired whenever a command is applied: (outputId, command, time).</summary>
    public Action<string, IoCommand, double>? CommandApplied { get; set; }
    /// <summary>Fired once when an output is found looping: (outputId, time). Its feedback links are paused.</summary>
    public Action<string, double>? LoopDetected { get; set; }
    /// <summary>Commands dropped by the one-command-per-output-per-tick rule (diagnostics).</summary>
    public int DroppedCommands { get; private set; }

    /// <summary>Replace the link table. Latched/pulse state of surviving outputs is kept; loop pauses are cleared.</summary>
    public void Configure(IEnumerable<(string OutputId, IEnumerable<Link> Links)> table)
    {
#if FLOW_TEST_CAPTURE
        table = table.Select(t => (t.OutputId, (IEnumerable<Link>)t.Links.Select(l => l.Clone()).ToArray())).ToArray();
        MigrationFixtures.Capture(table);
#endif
        links.Clear(); inputs.Clear(); scheduledFollow.Clear();
        queue.RemoveAll(p => p.FollowSlot >= 0);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int id = 0;
        foreach (var (outputId, list) in table)
        {
            seen.Add(outputId);
            if (!outputs.TryGetValue(outputId, out var state)) outputs[outputId] = state = new OutputState();
            state.Follow.Clear();
            state.HasFeedback = false; state.Loop = false; state.Changes.Clear();
            foreach (var link in list)
            {
                bool feedback = IoRegistry.FindInput(link.Input.TypeId) is IFeedbackInput;
                state.HasFeedback |= feedback;
                links.Add(new LinkSlot(id++, outputId, link.Clone(), feedback));
                inputs[link.Input.Key] = link.Input;
            }
        }
        foreach (var gone in outputs.Keys.Where(k => !seen.Contains(k)).ToArray()) outputs.Remove(gone);
        queue.RemoveAll(p => !outputs.ContainsKey(p.OutputId));
        foreach (var key in samples.Keys.Where(k => !inputs.ContainsKey(k)).ToArray()) { samples.Remove(key); previousLevel.Remove(key); }
    }

    /// <summary>Extra input instances to sample for lamps even when no link uses them.</summary>
    public void Watch(InputInstance input) { inputs.TryAdd(input.Key, input); }

    public void Tick(double now, object? context)
    {
        if (!double.IsFinite(now)) return;
        if (now < lastNow) now = lastNow; // never run time backwards
        lastNow = now;
        tick++;
        InputErrors.Clear();
        var events = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, instance) in inputs)
        {
            var type = IoRegistry.FindInput(instance.TypeId);
            InputSample sample = InputSample.Off;
            if (type != null)
            {
                try { sample = type.Read(instance, context); }
                catch (Exception ex) { InputErrors[instance.TypeId] = ex.Message; sample = InputSample.Off; }
            }
            bool prev = previousLevel.TryGetValue(key, out var p) && p;
            int count = Math.Max(0, sample.Events);
            if (count == 0 && type?.SignalKind == SignalKind.Level && sample.Level && !prev) count = 1; // rising edge
            previousLevel[key] = sample.Level;
            samples[key] = sample with { Events = count };
            events[key] = count;
        }
        foreach (var slot in links)
        {
            bool paused = slot.Feedback && InLoop(slot.OutputId);
            var sample = samples.TryGetValue(slot.Link.Input.Key, out var s) ? s : InputSample.Off;
            if (slot.Link.Command == IoCommand.Follow)
            {
                bool level = !paused && (sample.Level || sample.Events > 0);
                if (slot.Link.DelaySeconds <= 0 || paused) { SetFollow(slot.OutputId, slot.Id, level); if (paused) scheduledFollow.Remove(slot.Id); }
                else if (!scheduledFollow.TryGetValue(slot.Id, out var last) || last != level)
                {
                    scheduledFollow[slot.Id] = level;
                    Enqueue(new Pending(now + slot.Link.DelaySeconds, 0, slot.OutputId, IoCommand.Follow, 0, slot.Id, level, slot.Feedback));
                }
                continue;
            }
            if (paused) continue;
            int n = events.TryGetValue(slot.Link.Input.Key, out var c) ? c : 0;
            for (int i = 0; i < n; i++)
            {
                if (slot.Link.DelaySeconds <= 0) Apply(slot.OutputId, slot.Link.Command, now, slot.Link.DurationSeconds, false);
                else Enqueue(new Pending(now + slot.Link.DelaySeconds, 0, slot.OutputId, slot.Link.Command, slot.Link.DurationSeconds, -1, false, slot.Feedback));
            }
        }
        RunQueue(now);
        WatchLoops(now);
    }

    /// <summary>Manual command (panel test buttons). Applied immediately, outside the per-tick limit.</summary>
    public void Command(string outputId, IoCommand command, double now, float duration = 0)
    {
        if (!outputs.ContainsKey(outputId)) outputs[outputId] = new OutputState();
        Apply(outputId, command, now, duration, true);
    }

    public bool IsOpen(string outputId, double now) =>
        outputs.TryGetValue(outputId, out var s) && (s.Latched || now < s.PulseUntil || s.Follow.Values.Any(v => v));
    public bool HasOutput(string outputId) => outputs.ContainsKey(outputId);
    /// <summary>True while the output's feedback links are paused because it was looping.</summary>
    public bool InLoop(string outputId) => outputs.TryGetValue(outputId, out var s) && s.Loop;
    public IReadOnlyList<string> LoopOutputs => outputs.Where(p => p.Value.Loop).Select(p => p.Key).ToArray();
    /// <summary>Resume paused feedback links (also happens on every Configure).</summary>
    public void ResetLoops() { foreach (var s in outputs.Values) { s.Loop = false; s.Changes.Clear(); } }
    public InputSample Sample(string inputKey) => samples.TryGetValue(inputKey, out var s) ? s : InputSample.Off;
    public int PendingCount => queue.Count;

    private void SetFollow(string outputId, int slot, bool level)
    {
        if (outputs.TryGetValue(outputId, out var s)) s.Follow[slot] = level;
    }
    private void Enqueue(Pending p)
    {
        p = p with { Seq = ++sequence };
        int index = queue.FindIndex(q => q.Due > p.Due);
        if (index < 0) queue.Add(p); else queue.Insert(index, p);
    }
    private void RunQueue(double now)
    {
        while (queue.Count > 0 && queue[0].Due <= now)
        {
            var p = queue[0]; queue.RemoveAt(0);
            bool paused = p.Feedback && InLoop(p.OutputId);
            if (p.FollowSlot >= 0) SetFollow(p.OutputId, p.FollowSlot, p.Level && !paused);
            else if (!paused) Apply(p.OutputId, p.Command, p.Due, p.Duration, false);
        }
    }
    private void Apply(string outputId, IoCommand command, double at, float duration, bool manual)
    {
        if (!outputs.TryGetValue(outputId, out var s) || command == IoCommand.Follow) return; // Follow: levels only
        if (!manual)
        {
            // One command per output per tick: two shots in one frame toggle once, and a chain cannot
            // bounce an output back and forth inside a single tick.
            if (s.LastCommandTick == tick) { DroppedCommands++; return; }
            s.LastCommandTick = tick;
        }
        switch (command)
        {
            case IoCommand.Open: s.Latched = true; break;
            case IoCommand.Close: s.Latched = false; s.PulseUntil = double.NegativeInfinity; break;
            case IoCommand.Toggle:
                if (s.Latched || at < s.PulseUntil || s.Follow.Values.Any(v => v)) { s.Latched = false; s.PulseUntil = double.NegativeInfinity; }
                else s.Latched = true;
                break;
            case IoCommand.Pulse:
                // Re-triggering during a pulse extends it from the new trigger time.
                s.PulseUntil = Math.Max(s.PulseUntil, at + Math.Max(0, duration));
                break;
        }
        CommandApplied?.Invoke(outputId, command, at);
    }
    /// <summary>Counts open/closed changes of outputs that have feedback links; too many in the window = loop.</summary>
    private void WatchLoops(double now)
    {
        foreach (var (id, s) in outputs)
        {
            bool open = IsOpen(id, now);
            if (open == s.LastOpen) continue;
            s.LastOpen = open;
            if (!s.HasFeedback || s.Loop) continue;
            s.Changes.Enqueue(now);
            while (s.Changes.Count > 0 && now - s.Changes.Peek() > LoopWindowSeconds) s.Changes.Dequeue();
            if (s.Changes.Count < LoopThreshold) continue;
            s.Loop = true; s.Changes.Clear();
            // Release Follow slots of the paused links right away; the next tick keeps them released.
            foreach (var slot in links) if (slot.Feedback && slot.OutputId == id) { s.Follow[slot.Id] = false; scheduledFollow.Remove(slot.Id); }
            LoopDetected?.Invoke(id, now);
        }
    }
}

/// <summary>On/off filter: a level must hold for Delay seconds before the output changes (reload flicker).</summary>
public sealed class LevelFilter
{
    private bool output, candidate;
    private double since = double.NegativeInfinity;
    public double DelaySeconds { get; }
    public LevelFilter(double delaySeconds) { DelaySeconds = Math.Max(0, delaySeconds); }
    public bool Value => output;
    public bool Update(bool raw, double now)
    {
        if (raw != candidate) { candidate = raw; since = now; }
        if (candidate != output && now - since >= DelaySeconds) output = candidate;
        return output;
    }
}

/// <summary>Drops repeated events from the same source within a window (two barrels, child weapons).</summary>
public sealed class DuplicateFilter
{
    private readonly Dictionary<long, double> last = new();
    public double WindowSeconds { get; }
    public DuplicateFilter(double windowSeconds) { WindowSeconds = windowSeconds; }
    public bool Accept(long source, double now)
    {
        if (last.TryGetValue(source, out var t) && now - t >= 0 && now - t < WindowSeconds) return false;
        last[source] = now;
        if (last.Count > 512) foreach (var key in last.Where(p => now - p.Value > WindowSeconds * 4).Select(p => p.Key).ToArray()) last.Remove(key);
        return true;
    }
}
