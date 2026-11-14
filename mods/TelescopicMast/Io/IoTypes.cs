using System.Globalization;
namespace SprocketTelescopicMast.Io;

// Pure signal model: no Unity or IL2CPP types, so tests can run it directly.

/// <summary>Event = momentary pulses (a shot); Level = on/off state (reloading, key held).</summary>
public enum SignalKind { Event, Level }

/// <summary>Choice = enum whose options come from the live game (cannons, hinges); stored as a string key.</summary>
public enum ParamKind { Int, Float, Bool, Enum, Choice }

/// <summary>One option of a Choice parameter: stored value plus readable label.</summary>
public readonly record struct ParamOption(string Value, string Label);

/// <summary>Commands a link can send to an output.</summary>
public enum IoCommand { Open, Close, Toggle, Pulse, Follow }

public sealed class ParamDef
{
    public string Id { get; }
    public string DisplayName { get; }
    public ParamKind Kind { get; }
    public double Min { get; }
    public double Max { get; }
    public double Step { get; }
    public string Default { get; }
    public IReadOnlyList<string> Options { get; }
    /// <summary>Choice only: current options (read when the panel draws or nudges; may change at any time).</summary>
    public Func<IReadOnlyList<ParamOption>>? OptionSource { get; }
    public const int MaxChoiceLength = 160;

    private ParamDef(string id, string name, ParamKind kind, double min, double max, double step, string def, IReadOnlyList<string>? options,
        Func<IReadOnlyList<ParamOption>>? source = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Parameter id required.", nameof(id));
        Id = id; DisplayName = name; Kind = kind; Min = min; Max = max; Step = step; Options = options ?? Array.Empty<string>();
        OptionSource = source;
        Default = Normalize(def);
    }
    public static ParamDef Int(string id, string name, int min, int max, int def) =>
        new(id, name, ParamKind.Int, min, max, 1, def.ToString(CultureInfo.InvariantCulture), null);
    public static ParamDef Float(string id, string name, double min, double max, double def, double step) =>
        new(id, name, ParamKind.Float, min, max, step, def.ToString(CultureInfo.InvariantCulture), null);
    public static ParamDef Bool(string id, string name, bool def) =>
        new(id, name, ParamKind.Bool, 0, 1, 1, def ? "true" : "false", null);
    public static ParamDef Enum(string id, string name, IReadOnlyList<string> options, string def)
    {
        if (options == null || options.Count == 0) throw new ArgumentException("Enum needs options.", nameof(options));
        return new(id, name, ParamKind.Enum, 0, options.Count - 1, 1, def, options);
    }
    /// <summary>
    /// Enum with dynamic options (e.g. the cannons of the vehicle). Any non-empty key is kept, even when it
    /// is not in the current list (copied vehicle, part not loaded yet): the input type decides the fallback.
    /// </summary>
    public static ParamDef Choice(string id, string name, Func<IReadOnlyList<ParamOption>> source, string def)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(def)) throw new ArgumentException("Choice needs a default key.", nameof(def));
        return new(id, name, ParamKind.Choice, 0, 0, 1, def, null, source);
    }

    /// <summary>Choice options right now; never throws (a failing source yields an empty list).</summary>
    public IReadOnlyList<ParamOption> CurrentOptions()
    {
        if (OptionSource == null) return Array.Empty<ParamOption>();
        try { return OptionSource() ?? Array.Empty<ParamOption>(); } catch { return Array.Empty<ParamOption>(); }
    }
    /// <summary>Readable label for a value; null when a Choice key is not among the current options.</summary>
    public string? LabelOf(string value)
    {
        if (Kind != ParamKind.Choice) return value;
        foreach (var o in CurrentOptions()) if (o.Value == value) return o.Label;
        return null;
    }

    /// <summary>Clamps/validates a raw value; invalid input becomes the default.</summary>
    public string Normalize(string? raw)
    {
        switch (Kind)
        {
            case ParamKind.Int:
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var i) || !double.IsFinite(i))
                    return Default ?? ((int)Min).ToString(CultureInfo.InvariantCulture);
                return ((int)Math.Clamp(Math.Round(i), Min, Max)).ToString(CultureInfo.InvariantCulture);
            case ParamKind.Float:
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) || !double.IsFinite(f))
                    return Default ?? Min.ToString(CultureInfo.InvariantCulture);
                return Math.Round(Math.Clamp(f, Min, Max), 4).ToString(CultureInfo.InvariantCulture);
            case ParamKind.Bool:
                if (raw is "true" or "1" or "True") return "true";
                if (raw is "false" or "0" or "False") return "false";
                return Default ?? "false";
            case ParamKind.Choice:
                {
                    var t = raw?.Trim();
                    if (string.IsNullOrEmpty(t) || t.Length > MaxChoiceLength || t.Any(char.IsControl)) return Default ?? "";
                    return t;
                }
            default:
                foreach (var o in Options) if (string.Equals(o, raw, StringComparison.Ordinal)) return o;
                return Default ?? Options[0];
        }
    }

    /// <summary>Value after pressing - (direction -1) or + (direction +1) in the UI.</summary>
    public string Nudge(string current, int direction)
    {
        direction = Math.Sign(direction);
        switch (Kind)
        {
            case ParamKind.Bool: return Normalize(current) == "true" ? "false" : "true";
            case ParamKind.Choice:
                {
                    var options = CurrentOptions();
                    string value = Normalize(current);
                    if (options.Count == 0) return value;
                    int index = -1;
                    for (int k = 0; k < options.Count; k++) if (options[k].Value == value) { index = k; break; }
                    // A key that is no longer offered: the first press goes to the first (or last) option.
                    if (index < 0) return options[direction < 0 ? options.Count - 1 : 0].Value;
                    int n = options.Count;
                    return options[((index + direction) % n + n) % n].Value;
                }
            case ParamKind.Enum:
                {
                    int index = Math.Max(0, IndexOf(Normalize(current)));
                    int n = Options.Count;
                    return Options[((index + direction) % n + n) % n];
                }
            default:
                {
                    double v = double.Parse(Normalize(current), CultureInfo.InvariantCulture);
                    return Normalize((v + direction * Step).ToString(CultureInfo.InvariantCulture));
                }
        }
    }
    private int IndexOf(string value) { for (int i = 0; i < Options.Count; i++) if (Options[i] == value) return i; return -1; }
}

public sealed class ParamSchema
{
    public static readonly ParamSchema Empty = new(Array.Empty<ParamDef>());
    public IReadOnlyList<ParamDef> Params { get; }
    public ParamSchema(IEnumerable<ParamDef> defs)
    {
        var list = defs.ToList();
        if (list.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new ArgumentException("Duplicate parameter id.");
        Params = list;
    }
    public ParamDef? Find(string id) => Params.FirstOrDefault(p => p.Id == id);
    public SortedDictionary<string, string> Defaults()
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in Params) d[p.Id] = p.Default;
        return d;
    }
    /// <summary>Returns a complete, clamped value set; unknown keys are kept verbatim for forward compatibility.</summary>
    public SortedDictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? values)
    {
        var d = Defaults();
        if (values != null)
            foreach (var pair in values)
            {
                var def = Find(pair.Key);
                d[pair.Key] = def != null ? def.Normalize(pair.Value) : pair.Value;
            }
        return d;
    }
}

/// <summary>What an input type returns for one tick.</summary>
public readonly record struct InputSample(bool Level, int Events)
{
    public static readonly InputSample Off = new(false, 0);
}

public interface IIoType
{
    string Id { get; }
    string DisplayName { get; }
    SignalKind SignalKind { get; }
    ParamSchema Schema { get; }
    /// <summary>Short text for the UI, e.g. "Cannon fired (group 1)".</summary>
    string Describe(IReadOnlyDictionary<string, string> values);
}

/// <summary>
/// A signal source. Read is called once per tick per distinct input instance.
/// context is an opaque runtime object (the vehicle in game, a fake in tests).
/// </summary>
public interface IInputType : IIoType
{
    InputSample Read(InputInstance instance, object? context);
}

/// <summary>
/// Marker for inputs that read the state of other outputs (hinge chains). The engine watches outputs
/// driven by such inputs for endless loops and pauses those links when one is found.
/// </summary>
public interface IFeedbackInput : IInputType { }

/// <summary>A signal consumer. The engine computes an open/closed target; the runtime moves it.</summary>
public interface IOutputType : IIoType
{
    IReadOnlyList<IoCommand> Commands { get; }
}

public static class FireGroups
{
    public const int Max = 9;
    /// <summary>Cannon fire group is a [Flags] bitmask: group N is bit N-1.</summary>
    public static bool Contains(int mask, int group) => group >= 1 && group <= 16 && (mask & (1 << (group - 1))) != 0;
    public static string Describe(int mask)
    {
        var groups = new List<string>();
        for (int g = 1; g <= 16; g++) if (Contains(mask, g)) groups.Add(g.ToString(CultureInfo.InvariantCulture));
        return groups.Count == 0 ? "none" : string.Join("+", groups);
    }
}
