using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketTelescopicMast.Io;

/// <summary>
/// Per-part API data (IO-API.md §6 M1). One state key, "ioPartDataV1", holding a compact JSON
/// object. Unity-free: the runtime adapter supplies a GameObject identity and SerializationInfo;
/// tests use plain objects and the in-memory IStateBag.
/// </summary>
internal static class PartData
{
    internal const string StateKey = "ioPartDataV1";
    internal const int MaxKeyChars = 64;
    internal const int MaxValueBytes = 4 * 1024;
    internal const int MaxPartBytes = 16 * 1024;

    private sealed class Entry
    {
        internal readonly SortedDictionary<string, string> Values = new(StringComparer.Ordinal);
    }

    internal sealed class PartComparer : IEqualityComparer<object>
    {
        internal static readonly PartComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y) || (x != null && x.Equals(y));
        public int GetHashCode(object obj) => obj.GetHashCode();
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<object, Entry> Parts = new(PartComparer.Instance);
    private static readonly Dictionary<object, string> Components = new(PartComparer.Instance);
    private static readonly HashSet<object> Warned = new(PartComparer.Instance);

    internal static void Attach(object part, string componentId)
    {
        if (part == null || string.IsNullOrWhiteSpace(componentId)) return;
        lock (Gate) Components[part] = componentId;
    }

    internal static string? ComponentId(object part)
    {
        lock (Gate) return Components.TryGetValue(part, out var id) ? id : null;
    }

    internal static bool Set(object part, string key, string? value)
    {
        if (part == null) return false;
        string? component = ComponentId(part);
        if (component == null || !IoApiCore.HasRegistrationFor(component))
        { Warn(part, "part data requires a registered external ComponentID."); return false; }
        if (!ValidKey(key, out string keyError)) { Warn(part, "invalid key " + Quote(key) + ": " + keyError); return false; }
        if (value != null && Encoding.UTF8.GetByteCount(value) > MaxValueBytes)
        { Warn(part, "value for " + Quote(key) + " exceeds 4 KB."); return false; }
        lock (Gate)
        {
            var entry = EntryFor(part);
            var candidate = new SortedDictionary<string, string>(entry.Values, StringComparer.Ordinal);
            if (value == null) candidate.Remove(key); else candidate[key] = value;
            string json = Json(candidate);
            if (Encoding.UTF8.GetByteCount(json) > MaxPartBytes)
            { Warn(part, "part data exceeds 16 KB."); return false; }
            entry.Values.Clear();
            foreach (var pair in candidate) entry.Values[pair.Key] = pair.Value;
            return true;
        }
    }

    internal static bool CanSet(object part, string key, string value)
    {
        if (!IoApiCore.HasRegistrationFor(ComponentId(part)) || !ValidKey(key, out _) || Encoding.UTF8.GetByteCount(value) > MaxValueBytes)
        { Warn(part, "invalid or oversized settings value."); return false; }
        lock (Gate)
        {
            var candidate = new SortedDictionary<string,string>(EntryFor(part).Values, StringComparer.Ordinal) { [key] = value };
            if (Encoding.UTF8.GetByteCount(Json(candidate)) <= MaxPartBytes) return true;
            Warn(part, "part data exceeds 16 KB."); return false;
        }
    }

    internal static string? Get(object part, string key)
    {
        if (part == null || key == null) return null;
        lock (Gate) return Parts.TryGetValue(part, out var entry) && entry.Values.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>The exact state value to merge, or null when this part never held API data.</summary>
    internal static string? StateJson(object part)
    {
        if (part == null) return null;
        lock (Gate) return Parts.TryGetValue(part, out var entry) ? Json(entry.Values) : null;
    }

    /// <summary>Replace a part's data from its saved state. Bad or oversized content is ignored with one warning.</summary>
    internal static void LoadState(object part, string? json)
    {
        if (part == null || string.IsNullOrWhiteSpace(json)) return;
        if (Encoding.UTF8.GetByteCount(json) > MaxPartBytes)
        { Warn(part, "saved part data exceeds 16 KB; ignored."); return; }
        JsonObject? root;
        try { root = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { root = null; }
        if (root == null) { Warn(part, "saved part data is not a JSON object; ignored."); return; }
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in root)
        {
            if (!ValidKey(pair.Key, out _) || pair.Value is not JsonValue node || !node.TryGetValue<string>(out var text))
            { Warn(part, "saved part data contains an invalid entry; ignored."); return; }
            if (Encoding.UTF8.GetByteCount(text) > MaxValueBytes)
            { Warn(part, "saved part data value exceeds 4 KB; ignored."); return; }
            values[pair.Key] = text;
        }
        lock (Gate)
        {
            var entry = EntryFor(part);
            entry.Values.Clear();
            foreach (var pair in values) entry.Values[pair.Key] = pair.Value;
        }
    }

    internal static void Remove(object part)
    {
        if (part == null) return;
        lock (Gate) { Parts.Remove(part); Components.Remove(part); Warned.Remove(part); }
    }

    /// <summary>Drop the values but keep the part registration; used when a load has no data key.</summary>
    internal static void ClearEntry(object part)
    {
        if (part == null) return;
        lock (Gate) Parts.Remove(part);
    }

    internal static bool HasPart(object part) { lock (Gate) return Parts.ContainsKey(part); }
    internal static bool WasWarned(object part) { lock (Gate) return Warned.Contains(part); }

    internal static void ClearForTests()
    {
        lock (Gate) { Parts.Clear(); Components.Clear(); Warned.Clear(); }
    }

    internal static bool ValidKey(string? key, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(key)) { error = "key is empty."; return false; }
        if (key.Length > MaxKeyChars) { error = "key is longer than 64 characters."; return false; }
        foreach (char c in key) if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-'))
        { error = "use only a-z, 0-9, '.', '_' or '-'."; return false; }
        int dot = key.IndexOf('.');
        if (dot <= 0 || dot == key.Length - 1 || key.Contains("..", StringComparison.Ordinal))
        { error = "expected <prefix>.<name>."; return false; }
        string prefix = key[..dot];
        if (prefix == "io") { error = "the 'io.' prefix is reserved for the mast."; return false; }
        return true;
    }

    internal static string Json(IReadOnlyDictionary<string, string> values)
    {
        var root = new JsonObject();
        foreach (var pair in values) root[pair.Key] = pair.Value;
        return root.ToJsonString();
    }

    private static Entry EntryFor(object part)
    {
        if (!Parts.TryGetValue(part, out var entry)) Parts[part] = entry = new Entry();
        return entry;
    }

    private static void Warn(object part, string message)
    {
        lock (Gate) { if (!Warned.Add(part)) return; }
        IoApiCore.Log?.Invoke("[IO API] " + message);
    }

    private static string Quote(string? value) => "\"" + (value ?? "") + "\"";
}
