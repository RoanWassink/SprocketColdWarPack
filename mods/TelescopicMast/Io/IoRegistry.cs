namespace SprocketTelescopicMast.Io;

/// <summary>
/// Public registry of input and output types. Other mods (or a later mast output)
/// add a trigger by implementing IInputType and calling RegisterInput once at load.
/// UI, storage and engine look types up by string id only.
/// </summary>
public static class IoRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IInputType> Inputs = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IOutputType> Outputs = new(StringComparer.Ordinal);
    private static readonly List<string> InputOrder = new(), OutputOrder = new();
    private static readonly Dictionary<string, RuleDescription> Descriptions = new(StringComparer.Ordinal);
    /// <summary>Incremented on every registration so caches can refresh.</summary>
    public static int Revision { get; private set; }

    public static void RegisterInput(IInputType type)
    {
        Validate(type);
        lock (Gate)
        {
            if (Inputs.ContainsKey(type.Id)) throw new InvalidOperationException($"Input type '{type.Id}' is already registered.");
            Inputs.Add(type.Id, type); InputOrder.Add(type.Id); Revision++;
        }
    }
    public static void RegisterOutput(IOutputType type)
    {
        Validate(type);
        lock (Gate)
        {
            if (Outputs.ContainsKey(type.Id)) throw new InvalidOperationException($"Output type '{type.Id}' is already registered.");
            Outputs.Add(type.Id, type); OutputOrder.Add(type.Id); Revision++;
        }
    }
    public static bool UnregisterInput(string id) { lock (Gate) { InputOrder.Remove(id); Revision++; return Inputs.Remove(id); } }
    public static bool UnregisterOutput(string id) { lock (Gate) { OutputOrder.Remove(id); Revision++; return Outputs.Remove(id); } }
    public static IInputType? FindInput(string? id) { if (id == null) return null; lock (Gate) return Inputs.TryGetValue(id, out var t) ? t : null; }
    public static IOutputType? FindOutput(string? id) { if (id == null) return null; lock (Gate) return Outputs.TryGetValue(id, out var t) ? t : null; }
    /// <summary>Registration order, used for the UI type picker.</summary>
    public static IReadOnlyList<IInputType> InputTypes { get { lock (Gate) return InputOrder.Select(id => Inputs[id]).ToArray(); } }
    public static IReadOnlyList<IOutputType> OutputTypes { get { lock (Gate) return OutputOrder.Select(id => Outputs[id]).ToArray(); } }

    /// <summary>
    /// Optional: how an input type reads in the I/O panel's rule sentence ("IF [source] [event]").
    /// May be called before or after RegisterInput; a later call replaces the description.
    /// Types without one get RuleDescription.Fallback (one source named after the type).
    /// </summary>
    public static void DescribeInput(string typeId, RuleDescription description)
    {
        if (string.IsNullOrWhiteSpace(typeId)) throw new ArgumentException("Type id required.", nameof(typeId));
        if (description == null) throw new ArgumentNullException(nameof(description));
        lock (Gate) { Descriptions[typeId] = description; Revision++; }
    }
    /// <summary>The registered description, or null (RuleModel.DescriptionOf supplies the fallback).</summary>
    public static RuleDescription? FindDescription(string? typeId) { if (typeId == null) return null; lock (Gate) return Descriptions.TryGetValue(typeId, out var d) ? d : null; }

    private static void Validate(IIoType type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (string.IsNullOrWhiteSpace(type.Id) || type.Id.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException($"Invalid type id '{type.Id}': use letters, digits, '.', '_' or '-'.");
        if (type.Schema == null) throw new ArgumentException($"Type '{type.Id}' has no parameter schema.");
    }
    /// <summary>Test support only.</summary>
    public static void ClearForTests() { lock (Gate) { Inputs.Clear(); Outputs.Clear(); InputOrder.Clear(); OutputOrder.Clear(); Descriptions.Clear(); Revision++; } }
}
