using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketTelescopicMast.Io;

public sealed record FlowSheet(string Id, string Name);

/// <summary>
/// Editor tabs (0.9.2). Stored as ioFlowV1.sheets [{id,name}] plus a "sheet" field per block.
/// The engine never reads either: all tabs run together as one vehicle flow. A flow without
/// sheets, or a block without a known sheet (for example one added by 0.9.1), belongs to the
/// first tab. Reading never writes, so an untouched flow keeps its exact bytes.
/// </summary>
public static class FlowSheets
{
    public const string Key = "sheets", NodeKey = "sheet", MainId = "main", MainName = "Main";
    public const int MaxName = 40;
    public static readonly IReadOnlyList<FlowSheet> Default = new[] { new FlowSheet(MainId, MainName) };
    public static bool Stored(FlowDocument f) => f.Extra.ContainsKey(Key);
    public static IReadOnlyList<FlowSheet> Read(FlowDocument f)
    {
        if (!f.Extra.TryGetValue(Key, out var e) || e.ValueKind != JsonValueKind.Array) return Default;
        var list = new List<FlowSheet>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in e.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object || !s.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
            string value = id.GetString() ?? "";
            if (value.Length == 0 || !ids.Add(value)) continue;
            string name = s.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? Clean(n.GetString()) : "";
            list.Add(new(value, name.Length > 0 ? name : "Tab " + (list.Count + 1)));
        }
        return list.Count > 0 ? list : Default;
    }
    /// <summary>Trimmed, single line, at most <see cref="MaxName"/> characters.</summary>
    public static string Clean(string? name)
    {
        var b = new StringBuilder();
        foreach (char c in name ?? "") b.Append(char.IsControl(c) ? ' ' : c);
        string s = b.ToString().Trim();
        return s.Length > MaxName ? s.Substring(0, MaxName).TrimEnd() : s;
    }
    public static string Of(FlowNode n, IReadOnlyList<FlowSheet> sheets)
    {
        string id = n.Data[NodeKey] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        foreach (var sheet in sheets) if (sheet.Id == id) return id;
        return sheets[0].Id;
    }
    internal static void Assign(FlowNode n, string sheet) => n.Data[NodeKey] = sheet;
    /// <summary>Rewrites the list; unknown fields of a kept sheet object survive.</summary>
    internal static void Write(FlowDocument f, IReadOnlyList<FlowSheet> sheets)
    {
        var old = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (f.Extra.TryGetValue(Key, out var e) && JsonNode.Parse(e.GetRawText()) is JsonArray existing)
            foreach (var item in existing)
                if (item is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<string>(out var id) && !old.ContainsKey(id)) old[id] = o;
        var array = new JsonArray();
        foreach (var s in sheets)
        {
            var o = old.TryGetValue(s.Id, out var previous) ? (JsonObject)JsonNode.Parse(previous.ToJsonString())! : new JsonObject();
            o["id"] = s.Id; o["name"] = s.Name; array.Add(o);
        }
        using var doc = JsonDocument.Parse(array.ToJsonString());
        f.Extra[Key] = doc.RootElement.Clone();
    }
}
