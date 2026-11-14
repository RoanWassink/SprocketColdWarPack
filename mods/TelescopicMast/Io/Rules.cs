using System.Globalization;
namespace SprocketTelescopicMast.Io;

// Rule sentences for the I/O panel v2: "IF [source] [event] THEN [command] after [delay]".
// Pure C#: a rule is just a Link (stored unchanged in hingeIoV1); this file only derives the
// sentence from the registry and turns sentence edits back into a Link. Input types describe
// themselves through IoRegistry.DescribeInput (optional); a type without a description gets a
// fallback so a new type always appears in the pickers.

/// <summary>One concrete source in the source picker, e.g. "Cannon 1 · 75 mm" or "Action 2".</summary>
/// <param name="Label">Dropdown text.</param>
/// <param name="Params">Parameter values written into the input when this source is chosen.</param>
/// <param name="MatchKeys">Parameters compared when deriving the source of an existing link (default: all of Params).</param>
/// <param name="Specific">A real part or key (shown in the live signal list); false for "any cannon in group N".</param>
public sealed record RuleSourceOption(string Label, IReadOnlyDictionary<string, string> Params, IReadOnlyList<string>? MatchKeys = null, bool Specific = true);

/// <summary>One event of an input type, e.g. "fires" (cannon.fired) or "is reloading (while)" (cannon.reloading, Follow).</summary>
/// <param name="Params">Fixed parameter values that select this event within the type (e.g. mode=loading).</param>
/// <param name="While">Level event used with Follow ("while"); false = the moment it happens.</param>
/// <param name="Signal">Shown in the live signal list.</param>
/// <param name="Order">Sort key within the source group (stable).</param>
public sealed record RuleEventDef(string Label, IReadOnlyDictionary<string, string> Params, bool While = false, bool Signal = false, int Order = 0);

/// <summary>How an input type reads in a rule sentence. Register with IoRegistry.DescribeInput.</summary>
public sealed class RuleDescription
{
    /// <summary>Source group heading, e.g. "Cannons". Types with the same group share one source list.</summary>
    public string Group { get; }
    /// <summary>Parameters that identify the source (kept when switching between events of the group).</summary>
    public IReadOnlyList<string> SourceParams { get; }
    /// <summary>Concrete sources; the first type of a group that provides this defines the group's list.</summary>
    public Func<IReadOnlyList<RuleSourceOption>>? Sources { get; }
    public IReadOnlyList<RuleEventDef> Events { get; }
    /// <summary>Label for a source of an existing link that is not in the current list (e.g. a cannon of another vehicle).</summary>
    public Func<IReadOnlyDictionary<string, string>, string>? MissingSourceLabel { get; }
    public bool IsFallback { get; init; }

    public RuleDescription(string group, IEnumerable<string>? sourceParams, Func<IReadOnlyList<RuleSourceOption>>? sources,
        IEnumerable<RuleEventDef> events, Func<IReadOnlyDictionary<string, string>, string>? missingSourceLabel = null)
    {
        if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("Group required.", nameof(group));
        Group = group; SourceParams = sourceParams?.ToArray() ?? Array.Empty<string>(); Sources = sources;
        Events = events?.ToArray() ?? Array.Empty<RuleEventDef>();
        if (Events.Count == 0) throw new ArgumentException("At least one event required.", nameof(events));
        MissingSourceLabel = missingSourceLabel;
    }

    /// <summary>Description for a type that registered none: one source named after the type, one event (two for a level).</summary>
    public static RuleDescription Fallback(IInputType type)
    {
        var none = new Dictionary<string, string>();
        var events = type.SignalKind == SignalKind.Level
            ? new[] { new RuleEventDef("turns on", none, false, true), new RuleEventDef("is on (while)", none, true) }
            : new[] { new RuleEventDef("triggers", none, false, true) };
        string name = string.IsNullOrWhiteSpace(type.DisplayName) ? type.Id : type.DisplayName;
        return new RuleDescription(name, null, () => new[] { new RuleSourceOption(name, none) }, events) { IsFallback = true };
    }
}

/// <summary>A source as shown in a rule (resolved from a RuleSourceOption, or a synthetic "not found" source).</summary>
public sealed class RuleSource
{
    public string Group { get; }
    public string TypeId { get; }
    public string Label { get; }
    public IReadOnlyDictionary<string, string> Params { get; }
    public IReadOnlyList<string> MatchKeys { get; }
    public bool Specific { get; }
    /// <summary>Synthetic: the link's source is not in the current list.</summary>
    public bool Missing { get; }
    public string Key { get; }
    internal RuleSource(string group, string typeId, string label, IReadOnlyDictionary<string, string> p, IReadOnlyList<string>? matchKeys, bool specific, bool missing)
    {
        Group = group; TypeId = typeId; Label = label; Params = new SortedDictionary<string, string>(p.ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal);
        MatchKeys = matchKeys?.ToArray() ?? Params.Keys.ToArray(); Specific = specific; Missing = missing;
        // Sources with parameters are the same concrete source across input types (a cannon
        // event may reuse cannon.fired's list). Source-less API sources need their type id,
        // otherwise two registrations in one group would collapse into one key.
        Key = group + "|" + (MatchKeys.Count == 0 ? typeId : "") + "|" +
              string.Join(",", MatchKeys.OrderBy(k => k, StringComparer.Ordinal).Select(k => k + "=" + Params.GetValueOrDefault(k, "")));
    }
    /// <summary>Short name for the signal list: the label up to " \u00b7 " / " \u2013 " / " (".</summary>
    public string Short
    {
        get
        {
            int cut = Label.Length;
            foreach (var sep in new[] { " \u00b7 ", " \u2013 ", " (" }) { int i = Label.IndexOf(sep, StringComparison.Ordinal); if (i > 0 && i < cut) cut = i; }
            return Label.Substring(0, cut);
        }
    }
    public override string ToString() => Label;
}

/// <summary>An event of one input type, as shown in a rule.</summary>
public sealed class RuleEvent
{
    public IInputType Type { get; }
    public string Label { get; }
    public IReadOnlyDictionary<string, string> Params { get; }
    public bool While { get; }
    public bool Signal { get; }
    public int Order { get; }
    public bool Synthetic { get; }
    public string Key { get; }
    internal RuleEvent(IInputType type, string label, IReadOnlyDictionary<string, string> p, bool @while, bool signal, int order, bool synthetic)
    {
        Type = type; Label = label; Params = new SortedDictionary<string, string>(p.ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal);
        While = @while; Signal = signal; Order = order; Synthetic = synthetic;
        Key = type.Id + "{" + string.Join(",", Params.Select(x => x.Key + "=" + x.Value)) + "}" + (@while ? "~" : "");
    }
    public override string ToString() => Label;
}

/// <summary>The sentence view of a link.</summary>
public readonly record struct RuleView(RuleSource Source, RuleEvent Event, IoCommand Command);

/// <summary>A live signal: a specific source plus one of its events.</summary>
public sealed record RuleSignal(RuleSource Source, RuleEvent Event)
{
    public string Title => Source.Short + " " + Event.Label;
    public InputInstance Instance() => RuleModel.Instance(Source, Event);
}

public static class RuleModel
{
    public static readonly IReadOnlyList<IoCommand> EdgeCommands = new[] { IoCommand.Open, IoCommand.Close, IoCommand.Toggle, IoCommand.Pulse };

    public static RuleDescription DescriptionOf(IInputType type) => IoRegistry.FindDescription(type.Id) ?? RuleDescription.Fallback(type);

    /// <summary>Source groups in registration order of their first input type.</summary>
    public static IReadOnlyList<string> Groups() =>
        IoRegistry.InputTypes.Select(t => DescriptionOf(t).Group).Distinct(StringComparer.Ordinal).ToArray();

    private static IEnumerable<IInputType> TypesIn(string group) =>
        IoRegistry.InputTypes.Where(t => string.Equals(DescriptionOf(t).Group, group, StringComparison.Ordinal));

    /// <summary>Concrete sources of one group (never throws; a failing source list yields none).</summary>
    public static IReadOnlyList<RuleSource> SourcesOf(string group)
    {
        var list = new List<RuleSource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in TypesIn(group))
        {
            var d = DescriptionOf(type);
            if (d.Sources == null) continue;
            IReadOnlyList<RuleSourceOption> options;
            try { options = d.Sources() ?? Array.Empty<RuleSourceOption>(); } catch { options = Array.Empty<RuleSourceOption>(); }
            foreach (var o in options)
            {
                if (o == null) continue;
                var s = new RuleSource(group, type.Id, o.Label, Normalize(type, o.Params), o.MatchKeys, o.Specific, false);
                if (seen.Add(s.Key)) list.Add(s);
            }
        }
        return list;
    }

    /// <summary>Every source, grouped, in registry order.</summary>
    public static IReadOnlyList<RuleSource> Sources() => Groups().SelectMany(SourcesOf).ToArray();

    /// <summary>Events of a group: every type of the group, ordered by RuleEventDef.Order (stable).</summary>
    public static IReadOnlyList<RuleEvent> Events(string group)
    {
        var list = new List<RuleEvent>();
        foreach (var type in TypesIn(group))
            foreach (var e in DescriptionOf(type).Events)
                list.Add(new RuleEvent(type, e.Label, Normalize(type, e.Params), e.While, e.Signal, e.Order, false));
        return list.Select((e, i) => (e, i)).OrderBy(x => x.e.Order).ThenBy(x => x.i).Select(x => x.e).ToArray();
    }

    /// <summary>Source and event of a link. Missing sources/events become synthetic entries; the link is not changed.</summary>
    public static RuleView Derive(Link link)
    {
        var type = IoRegistry.FindInput(link.Input.TypeId) ?? throw new InvalidOperationException($"Unknown input type '{link.Input.TypeId}'.");
        var d = DescriptionOf(type);
        var values = link.Input.Params;
        RuleSource? source = null;
        var sourceCandidates = SourcesOf(d.Group);
        // First the sources contributed by this same input type (important for API inputs in a
        // shared group whose source has no parameters), then any type in the group (cannons:
        // cannon.reloaded reuses the source list of cannon.fired).
        foreach (var s in sourceCandidates.Where(s => s.TypeId == type.Id).Concat(sourceCandidates.Where(s => s.TypeId != type.Id)))
            if (s.MatchKeys.All(k => Same(type, k, values.GetValueOrDefault(k, ""), s.Params.GetValueOrDefault(k, "")))) { source = s; break; }
        if (source == null)
        {
            var own = d.SourceParams.Where(values.ContainsKey).ToDictionary(k => k, k => values[k]);
            string label;
            try { label = d.MissingSourceLabel?.Invoke(values) ?? link.Input.Describe(); } catch { label = link.Input.Describe(); }
            source = new RuleSource(d.Group, type.Id, label, own, own.Keys.ToArray(), false, true);
        }
        bool follow = link.Command == IoCommand.Follow;
        var candidates = Events(d.Group).Where(e => e.Type.Id == type.Id &&
            e.Params.All(p => Same(type, p.Key, values.GetValueOrDefault(p.Key, ""), p.Value))).ToList();
        var ev = candidates.FirstOrDefault(e => e.While == follow) ?? candidates.FirstOrDefault();
        if (ev == null)
        {
            var rest = values.Where(p => !d.SourceParams.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            ev = new RuleEvent(type, type.DisplayName, rest, follow, false, int.MaxValue, true);
        }
        return new RuleView(source, ev, link.Command);
    }

    /// <summary>Picker contents for one rule: all sources of all groups plus the link's own source if it is missing.</summary>
    public static IReadOnlyList<RuleSource> SourcesFor(Link link)
    {
        var all = Sources().ToList();
        var view = Derive(link);
        if (view.Source.Missing) all.Add(view.Source);
        return all;
    }
    public static IReadOnlyList<RuleEvent> EventsFor(Link link)
    {
        var view = Derive(link);
        var list = Events(view.Source.Group).ToList();
        if (view.Event.Synthetic) list.Add(view.Event);
        return list;
    }
    /// <summary>Open/Close/Toggle/Pulse, plus Follow when the type has a "while" event (or the link already follows).</summary>
    public static IReadOnlyList<IoCommand> CommandsFor(Link link)
    {
        var type = IoRegistry.FindInput(link.Input.TypeId);
        bool follow = link.Command == IoCommand.Follow || (type != null && (DescriptionOf(type).Events.Any(e => e.While) || type.SignalKind == SignalKind.Level));
        return follow ? EdgeCommands.Append(IoCommand.Follow).ToArray() : EdgeCommands;
    }

    /// <summary>Input instance for a source + event (source params, then event params, over the type defaults).</summary>
    public static InputInstance Instance(RuleSource source, RuleEvent ev, IReadOnlyDictionary<string, string>? keep = null)
    {
        var p = new Dictionary<string, string>(StringComparer.Ordinal);
        if (keep != null) foreach (var x in keep) p[x.Key] = x.Value;
        foreach (var x in source.Params) p[x.Key] = x.Value;
        foreach (var x in ev.Params) p[x.Key] = x.Value;
        return new InputInstance(ev.Type.Id, p);
    }

    /// <summary>New link from a sentence. A "while" event always follows; Follow on a non-while event becomes Open.</summary>
    public static Link Build(RuleSource source, RuleEvent ev, IoCommand command = IoCommand.Open, float delay = 0, float duration = 2)
        => new(Instance(source, ev), ev.While ? IoCommand.Follow : command == IoCommand.Follow ? IoCommand.Open : command, delay, duration);

    /// <summary>Change the source. Same group: same event; other group: its first event (a while event if the link follows).</summary>
    public static void SetSource(Link link, RuleSource source)
    {
        var view = Derive(link);
        RuleEvent ev = view.Event;
        if (source.Group != view.Source.Group)
        {
            var events = Events(source.Group);
            bool follow = link.Command == IoCommand.Follow;
            ev = events.FirstOrDefault(e => e.While == follow && !e.Synthetic) ?? events.First();
        }
        // Same type: keep its other parameters (fallback types, fire group of a specific cannon).
        var keep = ev.Type.Id == link.Input.TypeId ? link.Input.Params : null;
        link.Input = Instance(source, ev, keep);
        FixCommand(link, ev);
    }

    /// <summary>Change the event; the source parameters are carried over.</summary>
    public static void SetEvent(Link link, RuleEvent ev)
    {
        var view = Derive(link);
        var keep = ev.Type.Id == link.Input.TypeId ? link.Input.Params : null;
        var source = view.Source.Group == DescriptionOf(ev.Type).Group ? view.Source : SourcesOf(DescriptionOf(ev.Type).Group).FirstOrDefault() ?? view.Source;
        // A missing source keeps its own values (e.g. a cannon VUID from a copied vehicle).
        link.Input = Instance(source, ev, keep);
        FixCommand(link, ev);
    }

    /// <summary>Change the command; Follow switches to the "while" twin of the event and back.</summary>
    public static void SetCommand(Link link, IoCommand command)
    {
        var view = Derive(link);
        bool wantWhile = command == IoCommand.Follow;
        if (view.Event.While != wantWhile && !view.Event.Synthetic)
        {
            var twin = Events(view.Source.Group).Where(e => e.Type.Id == view.Event.Type.Id && e.While == wantWhile).ToList();
            var pick = twin.FirstOrDefault(e => e.Params.Count == view.Event.Params.Count && e.Params.All(p => view.Event.Params.GetValueOrDefault(p.Key) == p.Value))
                       ?? twin.FirstOrDefault();
            if (pick != null) link.Input = Instance(view.Source, pick, link.Input.Params);
        }
        link.Command = command;
    }

    private static void FixCommand(Link link, RuleEvent ev)
    {
        if (ev.Synthetic) return;
        if (ev.While) link.Command = IoCommand.Follow;
        else if (link.Command == IoCommand.Follow) link.Command = IoCommand.Open;
    }

    /// <summary>Default source: the first specific source of the first group (first cannon), else its first source.</summary>
    public static RuleSource? DefaultSource()
    {
        foreach (var g in Groups())
        {
            var sources = SourcesOf(g);
            if (sources.Count == 0) continue;
            return sources.FirstOrDefault(s => s.Specific) ?? sources[0];
        }
        return null;
    }

    /// <summary>"+ Add rule": first cannon, fires -> open, no delay. Null when no input type exists.</summary>
    public static Link? DefaultRule()
    {
        var source = DefaultSource();
        if (source == null) return null;
        var ev = Events(source.Group).FirstOrDefault(e => !e.While);
        return ev == null ? null : Build(source, ev, IoCommand.Open);
    }

    /// <summary>Live signals: every specific source with its signal events (groups without one: their first source).</summary>
    public static IReadOnlyList<RuleSignal> Signals()
    {
        var list = new List<RuleSignal>();
        foreach (var g in Groups())
        {
            var sources = SourcesOf(g).Where(s => s.Specific).ToList();
            if (sources.Count == 0) sources = SourcesOf(g).Take(1).ToList();
            var events = Events(g).Where(e => e.Signal).ToList();
            if (events.Count == 0) events = Events(g).Where(e => !e.While).Take(1).ToList();
            foreach (var s in sources) foreach (var e in events) list.Add(new RuleSignal(s, e));
        }
        return list;
    }

    /// <summary>Find an event of a type by its event parameters (templates).</summary>
    public static RuleEvent? FindEvent(string typeId, bool @while = false, params (string Key, string Value)[] p)
    {
        var type = IoRegistry.FindInput(typeId);
        if (type == null) return null;
        return Events(DescriptionOf(type).Group).FirstOrDefault(e => e.Type.Id == typeId && e.While == @while &&
            p.All(x => e.Params.GetValueOrDefault(x.Key) == x.Value));
    }

    private static IReadOnlyDictionary<string, string> Normalize(IInputType type, IReadOnlyDictionary<string, string>? p)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (p == null) return d;
        foreach (var x in p) d[x.Key] = type.Schema.Find(x.Key)?.Normalize(x.Value) ?? x.Value;
        return d;
    }
    private static bool Same(IInputType type, string key, string a, string b)
    {
        var def = type.Schema.Find(key);
        return def != null ? def.Normalize(a) == def.Normalize(b) : a == b;
    }
}

/// <summary>One-click rule sets for a hinge. Built through the registry, so they only use registered types.</summary>
public sealed class RuleTemplate
{
    public string Name { get; }
    public string Help { get; }
    private readonly Func<IReadOnlyList<Link>?> build;
    public RuleTemplate(string name, string help, Func<IReadOnlyList<Link>?> build) { Name = name; Help = help; this.build = build; }
    /// <summary>The links this template adds now; null when a needed input type or source is missing.</summary>
    public IReadOnlyList<Link>? Build() { try { return build(); } catch { return null; } }

    public static readonly IReadOnlyList<RuleTemplate> All = new[]
    {
        new RuleTemplate("Case flap: shot -> open, reloaded -> close",
            "Opens when the first cannon fires and closes when it has finished reloading.", () =>
            {
                var fired = RuleModel.FindEvent("cannon.fired");
                var reloaded = RuleModel.FindEvent("cannon.reloaded");
                var cannon = fired == null ? null : RuleModel.SourcesOf(RuleModel.DescriptionOf(fired.Type).Group).FirstOrDefault(s => s.Specific)
                             ?? RuleModel.SourcesOf(RuleModel.DescriptionOf(fired.Type).Group).FirstOrDefault();
                if (fired == null || reloaded == null || cannon == null) return null;
                return new[] { RuleModel.Build(cannon, fired, IoCommand.Open), RuleModel.Build(cannon, reloaded, IoCommand.Close) };
            }),
        new RuleTemplate("Hatch on key (toggle)", "I/O Flow / Action 1 opens and closes the hinge.", () =>
            {
                var press = RuleModel.FindEvent("key");
                var key = press == null ? null : RuleModel.SourcesOf(RuleModel.DescriptionOf(press.Type).Group).FirstOrDefault();
                return press == null || key == null ? null : new[] { RuleModel.Build(key, press, IoCommand.Toggle) };
            }),
        new RuleTemplate("Open while mast is up", "Open as long as a telescopic mast of this vehicle is raised.", () =>
            {
                var up = RuleModel.FindEvent("mast.state", true, ("state", "raised"));
                var mast = up == null ? null : RuleModel.SourcesOf(RuleModel.DescriptionOf(up.Type).Group).FirstOrDefault();
                return up == null || mast == null ? null : new[] { RuleModel.Build(mast, up, IoCommand.Follow) };
            })
    };
}

/// <summary>Sentence descriptions of the built-in input types (registered by the plugin and the tests).</summary>
public static class BuiltinRules
{
    public const string Cannons = "Cannons", Keys = "Keys", Hinges = "Hinges", Mast = "Mast", Vehicle = "Vehicle";
    private static IReadOnlyDictionary<string, string> P(params (string K, string V)[] p) => p.ToDictionary(x => x.K, x => x.V);

    public static void Register()
    {
        string[] cannonParams = { "cannon", "fireGroup" };
        Func<IReadOnlyList<RuleSourceOption>> cannons = CannonSources;
        Func<IReadOnlyDictionary<string, string>, string> missingCannon = v =>
        {
            PartSelector.TryParse(v.GetValueOrDefault("cannon"), out int vuid, out _);
            string group = v.GetValueOrDefault("fireGroup", "0");
            return $"Cannon #{vuid} (not found: " + (group == "0" ? "any cannon)" : $"any in group {group})");
        };
        IoRegistry.DescribeInput("cannon.fired", new RuleDescription(Cannons, cannonParams, cannons,
            new[] { new RuleEventDef("fires", P(), false, true, 0) }, missingCannon));
        IoRegistry.DescribeInput("cannon.reloading", new RuleDescription(Cannons, cannonParams, cannons, new[]
        {
            new RuleEventDef("starts reloading", P(("mode", "loading")), false, true, 1),
            new RuleEventDef("is reloading (while)", P(("mode", "loading")), true, false, 4),
            new RuleEventDef("becomes not ready", P(("mode", "notReady")), false, false, 6),
            new RuleEventDef("is not ready (while)", P(("mode", "notReady")), true, false, 7)
        }, missingCannon));
        IoRegistry.DescribeInput("cannon.reloaded", new RuleDescription(Cannons, cannonParams, cannons,
            new[] { new RuleEventDef("finished reloading", P(), false, true, 2) }, missingCannon));
        IoRegistry.DescribeInput("cannon.loaded", new RuleDescription(Cannons, cannonParams, cannons, new[]
        {
            new RuleEventDef("is loaded (while)", P(), true, false, 3),
            new RuleEventDef("becomes loaded", P(), false, false, 5)
        }, missingCannon));
        IoRegistry.DescribeInput("key", new RuleDescription(Keys, new[] { "action" },
            () => Enumerable.Range(1, KeyInput.Actions).Select(i => new RuleSourceOption("Action " + i.ToString(CultureInfo.InvariantCulture), P(("action", i.ToString(CultureInfo.InvariantCulture))))).ToArray(),
            new[] { new RuleEventDef("is pressed", P(), false, true), new RuleEventDef("is held (while)", P(), true) }));
        IoRegistry.DescribeInput("hinge.state", new RuleDescription(Hinges, new[] { "hinge" },
            () => IoCatalog.Hinges.Select(o => new RuleSourceOption(o.Label, P(("hinge", o.Value)))).ToArray(), new[]
            {
                new RuleEventDef("has opened", P(("state", "open")), false, true),
                new RuleEventDef("is open (while)", P(("state", "open")), true),
                new RuleEventDef("has closed", P(("state", "closed")), false, true),
                new RuleEventDef("is closed (while)", P(("state", "closed")), true)
            }, v => PartSelector.TryParse(v.GetValueOrDefault("hinge"), out _, out var name) && name.Length > 0 ? $"Hinge '{name}' (not found)" : "Hinge (pick one)"));
        IoRegistry.DescribeInput("mast.state", new RuleDescription(Mast, null, () => new[] { new RuleSourceOption("Mast", P()) }, new[]
        {
            new RuleEventDef("is fully extended", P(("state", "extended")), false, true),
            new RuleEventDef("is fully extended (while)", P(("state", "extended")), true),
            new RuleEventDef("is fully retracted", P(("state", "retracted")), false, true),
            new RuleEventDef("is fully retracted (while)", P(("state", "retracted")), true),
            new RuleEventDef("starts rising", P(("state", "raised")), false),
            new RuleEventDef("is up (while)", P(("state", "raised")), true)
        }));
        IoRegistry.DescribeInput("vehicle.braking", new RuleDescription(Vehicle, null,
            () => new[] { new RuleSourceOption("Vehicle", P()) }, new[]
            {
                new RuleEventDef("brakes", P(), false, true, 0),
                new RuleEventDef("is braking (while)", P(), true, false, 1)
            }));
    }

    /// <summary>Specific cannons of the vehicle (with their own first fire group as fallback), then "any cannon in group N".</summary>
    public static IReadOnlyList<RuleSourceOption> CannonSources()
    {
        var list = new List<RuleSourceOption>();
        foreach (var o in IoCatalog.Cannons)
        {
            if (o.Value == PartSelector.Any) continue;
            int group = IoCatalog.CannonFireGroups.TryGetValue(o.Value, out var g) && g >= 1 ? g : 1;
            list.Add(new RuleSourceOption(o.Label, P(("cannon", o.Value), ("fireGroup", group.ToString(CultureInfo.InvariantCulture))), new[] { "cannon" }));
        }
        list.Add(new RuleSourceOption("Any cannon", P(("cannon", PartSelector.Any), ("fireGroup", "0")), null, false));
        for (int g = 1; g <= FireGroups.Max; g++)
            list.Add(new RuleSourceOption("Any cannon in group " + g.ToString(CultureInfo.InvariantCulture),
                P(("cannon", PartSelector.Any), ("fireGroup", g.ToString(CultureInfo.InvariantCulture))), null, false));
        return list;
    }
}
