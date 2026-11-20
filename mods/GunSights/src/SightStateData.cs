using System.Text.Json;

namespace SprocketGunSights;

/// <summary>One explicit per-sight choice. null = not chosen (vehicle-date variant / [View] default).</summary>
internal sealed record SightStateEntry(int Sight, string? Variant, bool? FollowGun);
internal sealed record SightStateDocument(int Version, SightStateEntry[] Sights);
/// <summary>A row ready to apply after the batch: Variant is null when the saved id is unknown (RejectedVariant).</summary>
internal sealed record SightRestore(int Sight, string? Variant, bool? FollowGun, string? RejectedVariant);

/// <summary>
/// Gun Sights envelope in the native blueprint object state (key sprocketGunSightsV1), written by the
/// VehicleObjectSerialization.ToBlueprint postfix and read after VehicleObjectRegister.InstantiateStructured.
/// Same rules as Thermal Sight's sprocketFcsLinksV1/sprocketThermalProfilesV1: strict codec, foreign keys kept,
/// poison-aware VUID remap, ambiguous rows dropped. Pure code: no game types, so the tests use it directly.
/// </summary>
internal static class SightStateData
{
    internal const string Key = "sprocketGunSightsV1";
    internal const int MaxRows = 64, MaxIdLength = 64, MaxBytes = 16384;

    /// <summary>The only object-state key this mod owns. Every other key (vanilla, Thermal, other mods) is kept.</summary>
    internal static bool Owned(string name) => name == Key;

    internal static string Encode(IEnumerable<SightStateEntry> rows)
    {
        var all = rows.ToArray(); Validate(all);
        return JsonSerializer.Serialize(new SightStateDocument(1, all));
    }

    internal static SightStateEntry[] Decode(string json)
    {
        if (json == null || json.Length > MaxBytes) throw new InvalidDataException("Gun sight document missing or too large.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement; Properties(root, "Version", "Sights");
        if (!root.TryGetProperty("Version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int v) || v != 1 ||
            !root.TryGetProperty("Sights", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > MaxRows)
            throw new InvalidDataException("Unsupported gun sight document.");
        var rows = new List<SightStateEntry>();
        foreach (var row in list.EnumerateArray())
        {
            Properties(row, "Sight", "Variant", "FollowGun");
            if (!row.TryGetProperty("Sight", out var s) || s.ValueKind != JsonValueKind.Number || !s.TryGetInt32(out int sight) ||
                !row.TryGetProperty("Variant", out var variant) || !row.TryGetProperty("FollowGun", out var follow))
                throw new InvalidDataException("Missing gun sight row field.");
            string? id = variant.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => variant.GetString(),
                _ => throw new InvalidDataException("Invalid gun sight variant."),
            };
            bool? followGun = follow.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidDataException("Invalid gun sight follow flag."),
            };
            rows.Add(new(sight, id, followGun));
        }
        var result = rows.ToArray(); Validate(result); return result;
    }

    internal static bool ValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= MaxIdLength && id.Trim() == id && !id.Any(char.IsControl);

    private static void Validate(SightStateEntry[] rows)
    {
        if (rows.Length > MaxRows || rows.Any(r => r == null || r.Sight < 0 ||
                (r.Variant == null && r.FollowGun == null) || (r.Variant != null && !ValidId(r.Variant))) ||
            rows.Select(r => r.Sight).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Invalid or ambiguous gun sight rows.");
    }

    private static void Properties(JsonElement node, params string[] allowed)
    {
        if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid gun sight object.");
        var names = new HashSet<string>();
        foreach (var p in node.EnumerateObject())
            if (!allowed.Contains(p.Name) || !names.Add(p.Name)) throw new InvalidDataException("Unknown/duplicate gun sight field.");
    }

    /// <summary>Rows that belong to sights actually owned by the source object (old VUIDs).</summary>
    internal static SightStateEntry[] ForOwner(IEnumerable<SightStateEntry> rows, IReadOnlySet<int> sourceIds) =>
        rows.Where(r => sourceIds.Contains(r.Sight)).ToArray();

    /// <summary>Old -> new VUID. A sight that occurs in more than one row of the batch is ambiguous: all its rows are dropped.
    /// An unmapped sight never keeps its raw old VUID.</summary>
    internal static SightStateEntry[] Remap(IEnumerable<SightStateEntry> rows, IReadOnlyDictionary<int, int> map) =>
        rows.ToArray().GroupBy(r => r.Sight).Where(g => g.Count() == 1).Select(g => g.Single())
            .Where(r => map.ContainsKey(r.Sight)).Select(r => r with { Sight = map[r.Sight] }).ToArray();

    /// <summary>Remapped rows ready to apply. An unknown variant id is not applied (default used, warned); its follow flag still is.</summary>
    internal static SightRestore[] Plan(IEnumerable<SightStateEntry> rows, IReadOnlyDictionary<int, int> map, Func<string, bool> knownVariant) =>
        Remap(rows, map).Select(r => r.Variant != null && !knownVariant(r.Variant)
            ? new SightRestore(r.Sight, null, r.FollowGun, r.Variant)
            : new SightRestore(r.Sight, r.Variant, r.FollowGun, null)).ToArray();

    /// <summary>
    /// Fresh state for the object: every entry except our own key, in original order, then our envelope when there is one.
    /// The runtime does the same on SerializationInfo; Thermal Sight does the same for its own keys, so the two
    /// postfixes keep each other's keys in either order.
    /// </summary>
    internal static List<KeyValuePair<string, T>> Capture<T>(IEnumerable<KeyValuePair<string, T>> state, IEnumerable<SightStateEntry> rows, Func<string, T> wrap)
    {
        var next = state.Where(e => !Owned(e.Key)).ToList();
        var all = rows.ToArray();
        if (all.Length > 0) next.Add(new(Key, wrap(Encode(all))));
        return next;
    }
}

/// <summary>Old -> new VUID map for one InstantiateStructured batch (same poison rules as Thermal's ConsoleVuidMap).</summary>
internal sealed class SightVuidMap
{
    private readonly Dictionary<int, int> map = new();
    private readonly HashSet<int> ambiguous = new(), ambiguousNext = new();
    internal IReadOnlyDictionary<int, int> Values => map;

    private void PoisonNext(int next)
    {
        ambiguousNext.Add(next);
        foreach (int old in map.Where(p => p.Value == next).Select(p => p.Key).ToArray()) { map.Remove(old); ambiguous.Add(old); }
    }

    internal void Add(int old, int next)
    {
        if (old < 0 || next < 0 || ambiguous.Contains(old)) return;
        if (map.TryGetValue(old, out int previous)) { PoisonNext(previous); PoisonNext(next); ambiguous.Add(old); return; }
        if (ambiguousNext.Contains(next)) { ambiguous.Add(old); return; }
        if (map.Values.Contains(next)) { PoisonNext(next); ambiguous.Add(old); return; }
        map[old] = next;
    }
}

/// <summary>Which view a scoped sight gets.</summary>
internal static class SightViewPolicy
{
    /// <summary>
    /// true = "stays static": the scope picture is drawn along the sight part's own axis and the native aim circle is hidden.
    /// false = native view that follows the gun (no override). The ATGM guidance sight always keeps its native free view.
    /// </summary>
    internal static bool Static(bool? savedFollowGun, bool defaultFollowsGun, bool atgmGuidance) =>
        !atgmGuidance && !(savedFollowGun ?? defaultFollowsGun);
}
