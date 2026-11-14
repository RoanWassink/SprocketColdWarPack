using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketTelescopicMast.Io;

/// <summary>An input type plus its parameter values, e.g. cannon.fired with fireGroup=1.</summary>
public sealed class InputInstance : IEquatable<InputInstance>
{
    public string TypeId { get; }
    public IReadOnlyDictionary<string, string> Params => values;
    /// <summary>Canonical identity: two links with the same key share one input lamp/sample.</summary>
    public string Key { get; }
    private readonly SortedDictionary<string, string> values;

    public InputInstance(string typeId, IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(typeId)) throw new ArgumentException("Type id required.", nameof(typeId));
        TypeId = typeId;
        var type = IoRegistry.FindInput(typeId);
        values = type != null ? type.Schema.Normalize(parameters) :
            new SortedDictionary<string, string>(parameters?.ToDictionary(p => p.Key, p => p.Value) ?? new(), StringComparer.Ordinal);
        Key = typeId + "{" + string.Join(",", values.Select(p => p.Key + "=" + p.Value)) + "}";
    }
    public string Get(string id) => values.TryGetValue(id, out var v) ? v : "";
    public int GetInt(string id, int fallback = 0) =>
        double.TryParse(Get(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? (int)Math.Round(v) : fallback;
    public float GetFloat(string id, float fallback = 0) =>
        double.TryParse(Get(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? (float)v : fallback;
    public bool GetBool(string id) => Get(id) == "true";
    public InputInstance With(string id, string value)
    {
        var copy = new Dictionary<string, string>(values) { [id] = value };
        return new InputInstance(TypeId, copy);
    }
    public string Describe() => IoRegistry.FindInput(TypeId)?.Describe(values) ?? $"Unknown input '{TypeId}'";
    public bool Equals(InputInstance? other) => other != null && other.Key == Key;
    public override bool Equals(object? obj) => Equals(obj as InputInstance);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
    public override string ToString() => Key;
}

public sealed class Link
{
    public const float MaxSeconds = 60f;
    public InputInstance Input { get; set; }
    public IoCommand Command { get; set; }
    private float delay, duration;
    public float DelaySeconds { get => delay; set => delay = Clamp(value); }
    /// <summary>Used by Pulse: how long the output stays open.</summary>
    public float DurationSeconds { get => duration; set => duration = Clamp(value); }
    public Link(InputInstance input, IoCommand command, float delaySeconds = 0, float durationSeconds = 2)
    { Input = input; Command = command; DelaySeconds = delaySeconds; DurationSeconds = durationSeconds; }
    private static float Clamp(float v) => float.IsFinite(v) ? MathF.Round(Math.Clamp(v, 0, MaxSeconds), 3) : 0;
    public Link Clone() => new(Input, Command, DelaySeconds, DurationSeconds);
    public string Describe(string outputName) =>
        $"{Input.Describe()} -> {Command}{(Command == IoCommand.Pulse ? $" {DurationSeconds:0.0#}s" : "")} -> {outputName}{(DelaySeconds > 0 ? $" (after {DelaySeconds:0.0#}s)" : "")}";
}

/// <summary>
/// Everything stored for one hinge (the target output): name, output settings and the links
/// that drive it. Links reference input type + parameters, never other parts' VUIDs, so a
/// copied hinge stays consistent.
/// </summary>
public sealed class HingeConfig
{
    public const int CurrentVersion = 1;
    public string Name { get; set; } = "";
    public string OutputType { get; set; } = "hinge";
    public SortedDictionary<string, string> OutputParams { get; set; } = new(StringComparer.Ordinal);
    public List<Link> Links { get; } = new();
    /// <summary>Links whose input type or command is unknown in this session: kept verbatim, not evaluated.</summary>
    public List<JsonObject> PreservedLinks { get; } = new();
    /// <summary>Unknown top-level fields, written back unchanged.</summary>
    public JsonObject Extra { get; } = new();
    /// <summary>A config written by a newer version: kept verbatim and read-only.</summary>
    public string? FutureRaw { get; internal set; }
    public bool ReadOnly => FutureRaw != null;

    public static HingeConfig CreateDefault(string name = "")
    {
        var c = new HingeConfig { Name = name };
        c.OutputParams = IoRegistry.FindOutput(c.OutputType)?.Schema.Defaults() ?? new(StringComparer.Ordinal);
        return c;
    }
    public string GetOutputParam(string id) =>
        OutputParams.TryGetValue(id, out var v) ? v : IoRegistry.FindOutput(OutputType)?.Schema.Find(id)?.Default ?? "";
    public float GetOutputFloat(string id, float fallback) =>
        double.TryParse(GetOutputParam(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? (float)v : fallback;
    public void SetOutputParam(string id, string value)
    {
        var def = IoRegistry.FindOutput(OutputType)?.Schema.Find(id);
        OutputParams[id] = def != null ? def.Normalize(value) : value;
    }
    public HingeConfig Clone() => HingeConfigJson.Parse(HingeConfigJson.Write(this));
}

public static class HingeConfigJson
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
    private static readonly string[] KnownTop = { "Version", "Name", "Output", "Links" };

    public static string Write(HingeConfig config)
    {
#if FLOW_TEST_CAPTURE
        MigrationFixtures.Capture(config);
#endif
        if (config.FutureRaw != null) return config.FutureRaw;
        var root = new JsonObject
        {
            ["Version"] = HingeConfig.CurrentVersion,
            ["Name"] = config.Name ?? "",
            ["Output"] = new JsonObject
            {
                ["Type"] = config.OutputType,
                ["Params"] = WriteParams(config.OutputParams, IoRegistry.FindOutput(config.OutputType)?.Schema)
            }
        };
        var links = new JsonArray();
        foreach (var link in config.Links)
            links.Add(new JsonObject
            {
                ["Input"] = new JsonObject
                {
                    ["Type"] = link.Input.TypeId,
                    ["Params"] = WriteParams(link.Input.Params, IoRegistry.FindInput(link.Input.TypeId)?.Schema)
                },
                ["Command"] = link.Command.ToString(),
                ["Delay"] = link.DelaySeconds,
                ["Duration"] = link.DurationSeconds
            });
        foreach (var raw in config.PreservedLinks) links.Add(Clone(raw));
        root["Links"] = links;
        foreach (var pair in config.Extra) if (!root.ContainsKey(pair.Key)) root[pair.Key] = Clone(pair.Value);
        return root.ToJsonString(Compact);
    }

    /// <summary>Never throws for content errors: bad links are preserved, bad fields fall back to defaults.</summary>
    public static HingeConfig Parse(string? json)
    {
        var config = HingeConfig.CreateDefault();
        if (string.IsNullOrWhiteSpace(json)) return config;
        JsonObject? root;
        try { root = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { root = null; }
        if (root == null) { config.Extra["corrupt"] = json; return config; }
        int version = TryInt(root["Version"]) ?? 0;
        if (version > HingeConfig.CurrentVersion)
        {
            var future = HingeConfig.CreateDefault(root["Name"] is JsonValue n && n.TryGetValue<string>(out var fn) ? fn : "");
            future.FutureRaw = json;
            return future;
        }
        if (root["Name"] is JsonValue name && name.TryGetValue<string>(out var s)) config.Name = s;
        if (root["Output"] is JsonObject output)
        {
            if (output["Type"] is JsonValue t && t.TryGetValue<string>(out var type) && !string.IsNullOrWhiteSpace(type)) config.OutputType = type;
            var schema = IoRegistry.FindOutput(config.OutputType)?.Schema;
            var values = ReadParams(output["Params"] as JsonObject);
            config.OutputParams = schema != null ? schema.Normalize(values) : new SortedDictionary<string, string>(values, StringComparer.Ordinal);
        }
        if (root["Links"] is JsonArray links)
            foreach (var node in links)
            {
                if (node is not JsonObject obj) continue;
                var link = TryLink(obj);
                if (link != null) config.Links.Add(link);
                else config.PreservedLinks.Add((JsonObject)Clone(obj)!);
            }
        foreach (var pair in root)
            if (!KnownTop.Contains(pair.Key)) config.Extra[pair.Key] = Clone(pair.Value);
        return config;
    }

    private static Link? TryLink(JsonObject obj)
    {
        if (obj["Input"] is not JsonObject input || input["Type"] is not JsonValue tv || !tv.TryGetValue<string>(out var typeId)) return null;
        if (IoRegistry.FindInput(typeId) == null) return null; // unknown type: preserve verbatim
        if (obj["Command"] is not JsonValue cv || !cv.TryGetValue<string>(out var cmd) ||
            !Enum.TryParse<IoCommand>(cmd, false, out var command) || !Enum.IsDefined(command)) return null;
        var instance = new InputInstance(typeId, ReadParams(input["Params"] as JsonObject));
        return new Link(instance, command, TryFloat(obj["Delay"]) ?? 0, TryFloat(obj["Duration"]) ?? 2);
    }

    private static JsonObject WriteParams(IReadOnlyDictionary<string, string> values, ParamSchema? schema)
    {
        var o = new JsonObject();
        foreach (var pair in values)
        {
            var def = schema?.Find(pair.Key);
            JsonNode? node = def?.Kind switch
            {
                ParamKind.Int => JsonValue.Create(int.Parse(def.Normalize(pair.Value), CultureInfo.InvariantCulture)),
                ParamKind.Float => JsonValue.Create(double.Parse(def.Normalize(pair.Value), CultureInfo.InvariantCulture)),
                ParamKind.Bool => JsonValue.Create(def.Normalize(pair.Value) == "true"),
                _ => JsonValue.Create(pair.Value)
            };
            o[pair.Key] = node;
        }
        return o;
    }
    private static Dictionary<string, string> ReadParams(JsonObject? obj)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (obj == null) return d;
        foreach (var pair in obj)
        {
            if (pair.Value is not JsonValue v) continue;
            if (v.TryGetValue<bool>(out var b)) d[pair.Key] = b ? "true" : "false";
            else if (v.TryGetValue<double>(out var x)) d[pair.Key] = x.ToString(CultureInfo.InvariantCulture);
            else if (v.TryGetValue<string>(out var s)) d[pair.Key] = s;
        }
        return d;
    }
    // JsonNode.DeepClone is .NET 8+; the game runs .NET 6.
    internal static JsonNode? Clone(JsonNode? node) => node == null ? null : JsonNode.Parse(node.ToJsonString());
    private static int? TryInt(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? (int)d : null;
    private static float? TryFloat(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? (float)d : null;
}
