namespace SprocketCupolas;

// Native-independent policy, shared verbatim with the fake driver tests.
internal static class CupolaPolicy
{
    internal const string PartGuid = "b56e5c50-f099-4bf0-b031-93f19212b862";
    internal const int Limit = 4096, MaxWalk = 256, MaxObjects = 65536, MaxComponents = 256;
    internal readonly record struct Node(long Pointer, long Vehicle, bool Released, bool Placement);
    internal static string? Eligible(string guid, long vehicle, bool installed, bool preview, bool dynamicRoot, IReadOnlyList<Node> chain, bool terminated)
    {
        if (!string.Equals(guid, PartGuid, StringComparison.OrdinalIgnoreCase)) return "foreign-guid";
        if (vehicle == 0) return "foreign-vehicle";
        if (!installed) return "not-installed";
        if (preview) return "ghost-save-flags";
        if (!dynamicRoot) return "not-dynamic";
        if (chain.Count == 0 || !terminated || chain.Count > MaxWalk) return "not-ready-ancestry";
        var seen = new HashSet<long>();
        foreach (var n in chain) {
            if (!seen.Add(n.Pointer)) return "cyclic-ancestry";
            if (n.Released) return "released";
            if (n.Vehicle != 0 && n.Vehicle != vehicle) return "foreign-ancestor";
            if (n.Placement) return "ghost-placement";
        }
        return null;
    }
    internal readonly record struct Model(long Pointer, string Id, long Owner, long Root, long Association);
    internal static bool Resolve(long root, long commander, long hatchDoor, IReadOnlyList<Model> models, out Model basis, out Model door)
    {
        basis = default; door = default;
        var owned = models.Where(m => m.Root == root).ToArray();
        if (owned.Length != 2 || owned.Count(m => m.Id == "baseModel" && m.Owner == root) != 1 || owned.Count(m => m.Id == "hatchModel") != 1) return false;
        basis = owned.Single(m => m.Id == "baseModel"); door = owned.Single(m => m.Id == "hatchModel");
        return basis.Pointer != door.Pointer && door.Pointer == hatchDoor && basis.Association == commander && door.Association == commander;
    }
}

internal readonly record struct CupolaKey(long Vehicle, long Root, int Vuid);
internal sealed class CupolaCache<T> where T : class
{
    private readonly Dictionary<CupolaKey, T> entries = new();
    internal IEnumerable<KeyValuePair<CupolaKey, T>> Entries => entries;
    internal int Count => entries.Count;
    internal bool TryGet(CupolaKey key, out T value) => entries.TryGetValue(key, out value!);
    internal bool Add(CupolaKey key, T value) { if (entries.Count >= CupolaPolicy.Limit || entries.ContainsKey(key)) return false; entries.Add(key, value); return true; }
    internal void Retire(Func<CupolaKey, T, bool> released, Action<T> release) {
        foreach (var p in entries.ToArray()) if (released(p.Key, p.Value)) { entries.Remove(p.Key); release(p.Value); }
    }
    internal void Clear(Action<T> release) { foreach (var v in entries.Values) release(v); entries.Clear(); }
}
