using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketTelescopicMast.Io;

/// <summary>Lossless node payload. Unknown fields/types are data, never executable code.</summary>
public sealed class FlowNode
{
    public JsonObject Data { get; }
    private readonly string? raw, original;
    private readonly JsonElement originalElement;
    public FlowNode(string id, string type, double x = 0, double y = 0, string title = "")
    { Data = new JsonObject { ["id"] = id, ["type"] = type, ["x"] = x, ["y"] = y, ["title"] = title, ["params"] = new JsonObject() }; }
    internal FlowNode(JsonElement element) { originalElement = element.Clone(); raw = element.GetRawText(); Data = (JsonObject)JsonNode.Parse(raw)!; original = Data.ToJsonString(); }
    public string Id => Text(Data["id"]);
    public string Type => Text(Data["type"]);
    public JsonObject Params => Data["params"] as JsonObject ?? new JsonObject();
    public string PartId => Text(Params["partId"]);
    public string Get(string key, string fallback = "") => Params[key] == null ? fallback : Text(Params[key]);
    public double Number(string key, double fallback = 0) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : fallback;
    public void Set(string key, string value) { if (Data["params"] is not JsonObject) Data["params"] = new JsonObject(); Params[key] = value; }
    internal string Json => raw == null ? Data.ToJsonString() : Data.ToJsonString() == original ? raw : FlowJson.PreserveRaw(Data, originalElement);
    internal static string Text(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n?.ToJsonString() ?? "";
    public IReadOnlyDictionary<string, string> InputParams => Params.ToDictionary(p => p.Key, p => Text(p.Value), StringComparer.Ordinal);
}

public sealed class FlowWire
{
    public JsonObject Data { get; }
    private readonly string? raw, original;
    public FlowWire(string from, string to, int port) { Data = new JsonObject { ["from"] = from, ["to"] = to, ["port"] = port }; }
    internal FlowWire(JsonElement e) { raw = e.GetRawText(); Data = (JsonObject)JsonNode.Parse(raw)!; original = Data.ToJsonString(); }
    public string From => FlowNode.Text(Data["from"]);
    public string To => FlowNode.Text(Data["to"]);
    public int Port => Data["port"] is JsonValue v && v.TryGetValue<int>(out int p) ? p : -1;
    internal string Json => raw != null && Data.ToJsonString() == original ? raw : Data.ToJsonString();
}

public sealed class FlowDocument
{
    public const string StateKey = "ioFlowV1", PartKey = "ioPartId";
    public int Version { get; set; } = 1;
    public long Rev { get; set; }
    public List<FlowNode> Nodes { get; } = new();
    public List<FlowWire> Wires { get; } = new();
    public Dictionary<string, JsonElement> Extra { get; } = new(StringComparer.Ordinal);
    public bool ReadOnly => Version != 1;
    public void Edited() { if (ReadOnly) throw new InvalidOperationException("Future flow is read-only."); Rev = checked(Rev + 1); }
    public void AddNode(FlowNode node)
    {
        if (Nodes.Any(n => n.Id == node.Id)) throw new ArgumentException("Duplicate node id.");
        if (FlowValidation.IsOutput(node) && (node.PartId.Length == 0 || Nodes.Any(n => FlowValidation.IsOutput(n) && n.PartId == node.PartId))) throw new ArgumentException("Duplicate/empty output part.");
        Edited(); Nodes.Add(node);
    }
    public void RemoveNode(string id) { if (!Nodes.Any(n => n.Id == id)) return; Edited(); Nodes.RemoveAll(n => n.Id == id); Wires.RemoveAll(w => w.From == id || w.To == id); }
    public void MoveNode(string id, double x, double y)
    { var n = Nodes.Single(n => n.Id == id); if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("Non-finite position."); Edited(); n.Data["x"] = x; n.Data["y"] = y; }
    public void SetParam(string id, string key, string value) { var n = Nodes.Single(n => n.Id == id); if (key == "partId" && Nodes.Any(other => other != n && FlowValidation.IsOutput(other) && other.PartId == value)) throw new ArgumentException("Duplicate output part."); Edited(); n.Set(key, value); }
    public void Connect(string from, string to, int port)
    {
        var wire = new FlowWire(from, to, port);
        var errors = FlowValidation.ValidateWire(this, wire);
        if (errors != null) throw new ArgumentException(errors);
        Edited(); Wires.RemoveAll(w => w.To == to && w.Port == port); Wires.Add(wire);
    }
}

public static class FlowJson
{
    // Reuse the exact original text for every unchanged subtree, including extension fields
    // inside a known node whose title/position/parameters have since been edited.
    internal static string PreserveRaw(JsonNode? current, JsonElement original)
    {
        if (current == null) return "null";
        if (current.ToJsonString() == JsonNode.Parse(original.GetRawText())?.ToJsonString()) return original.GetRawText();
        if (current is JsonObject obj && original.ValueKind == JsonValueKind.Object)
            return "{" + string.Join(",", obj.Select(p => JsonSerializer.Serialize(p.Key) + ":" +
                (original.TryGetProperty(p.Key, out var old) ? PreserveRaw(p.Value, old) : p.Value?.ToJsonString() ?? "null"))) + "}";
        return current.ToJsonString();
    }
    public static FlowDocument Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var f = new FlowDocument { Version = r.GetProperty("v").GetInt32(), Rev = r.GetProperty("rev").GetInt64() };
        if (f.Rev < 0) throw new JsonException("Negative flow revision.");
        foreach (var n in r.GetProperty("nodes").EnumerateArray()) f.Nodes.Add(new FlowNode(n));
        foreach (var w in r.GetProperty("wires").EnumerateArray()) f.Wires.Add(new FlowWire(w));
        foreach (var p in r.EnumerateObject()) if (p.Name is not ("v" or "rev" or "nodes" or "wires")) f.Extra[p.Name] = p.Value.Clone();
        return f;
    }
    public static string Write(FlowDocument f)
    {
        using var stream = new MemoryStream(); using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject(); w.WriteNumber("v", f.Version); w.WriteNumber("rev", f.Rev);
            w.WriteStartArray("nodes"); foreach (var n in f.Nodes) w.WriteRawValue(n.Json); w.WriteEndArray();
            w.WriteStartArray("wires"); foreach (var wire in f.Wires) w.WriteRawValue(wire.Json); w.WriteEndArray();
            foreach (var p in f.Extra) { w.WritePropertyName(p.Key); w.WriteRawValue(p.Value.GetRawText()); } w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static string Envelope(string partId, FlowDocument flow) => "{\"partId\":" + JsonSerializer.Serialize(partId) + ",\"flow\":" + Write(flow) + "}";
    public static (string PartId, FlowDocument Flow) ReadEnvelope(string json)
    { using var d = JsonDocument.Parse(json); return (d.RootElement.GetProperty("partId").GetString() ?? "", Parse(d.RootElement.GetProperty("flow").GetRawText())); }
}

public static class FlowValidation
{
    public static bool IsOutput(FlowNode n) => n.PartId.Length > 0 || n.Type is "hinge" or "mast" or "rotator" || IoRegistry.FindOutput(n.Type) != null;
    public static int Ports(FlowNode n) => n.Type switch
    { "always" => 0, "rotator" => 4, "and" or "or" or "counter" or "memory" => 2, "not" or "delay" or "pulse" => 1, _ => IsOutput(n) ? 1 : IoRegistry.FindInput(n.Type) != null ? 0 : -1 };
    public static string? ValidateWire(FlowDocument f, FlowWire w)
    {
        var from = f.Nodes.FirstOrDefault(n => n.Id == w.From); var to = f.Nodes.FirstOrDefault(n => n.Id == w.To);
        if (from == null || to == null) return "Unknown node.";
        if (Ports(from) < 0) return "Unknown output port.";
        if (w.Port < 0 || w.Port >= Ports(to)) return "Unknown input port.";
        return null;
    }
    public static IReadOnlyList<string> Validate(FlowDocument f)
    {
        var errors = new List<string>(); var ports = new HashSet<(string, int)>(); var ids = new HashSet<string>(); var parts = new HashSet<string>();
        foreach (var n in f.Nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Id) || !ids.Add(n.Id)) errors.Add("Duplicate/empty node id: " + n.Id);
            if (IsOutput(n) && (n.PartId.Length == 0 || !parts.Add(n.PartId))) errors.Add("Duplicate/empty output part: " + n.PartId);
        }
        foreach (var w in f.Wires) { var e = ValidateWire(f, w); if (e != null) errors.Add(e); if (!ports.Add((w.To, w.Port))) errors.Add("More than one wire per input."); }
        return errors;
    }
}
