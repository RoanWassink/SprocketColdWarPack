namespace SprocketCupolas;

// Native-independent policy, shared verbatim with the fake driver tests.
internal static class CupolaPolicy
{
    internal const string PartGuid = "b56e5c50-f099-4bf0-b031-93f19212b862";
    internal const string AbramsGuid = "c8a4f2d1-7e3b-4c9a-91d5-6b2e8f0a4c11";
    internal const string LeopardGuid = "d4b7e3a2-9c1f-4d8b-a2e6-7c3f9a1b5d22";
    internal const string ChallengerGuid = "e9c2a5f4-3b8d-4e7c-b1f8-8d4a0c2e6f33";
    internal const string T80BvmGuid = "f1a3c6e8-4d9b-4f2a-8c7e-9b5d1e3a7c44";
    internal const string T90ADriverHatchGuid = "a2b4c6d8-1e3f-4a5b-9c7d-2e4f6a8b0c55";
    internal static bool IsKnownGuid(string? guid) =>
        string.Equals(guid, PartGuid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(guid, AbramsGuid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(guid, LeopardGuid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(guid, ChallengerGuid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(guid, T80BvmGuid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(guid, T90ADriverHatchGuid, StringComparison.OrdinalIgnoreCase);
    internal const int Limit = 4096, MaxWalk = 256, MaxObjects = 65536, MaxComponents = 256;
    internal readonly record struct Node(long Pointer, long Vehicle, bool Released, bool Placement);
    internal static string? Eligible(string guid, long vehicle, bool installed, bool preview, bool dynamicRoot, IReadOnlyList<Node> chain, bool terminated)
    {
        if (!IsKnownGuid(guid)) return "foreign-guid";
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
        if (owned.Length != 2 || owned.Count(m => m.Id == "baseModel" && m.Owner == root) != 1) return false;
        var doors = owned.Where(m => m.Id == "hatchModel" || m.Id == "doorModel").ToArray();
        if (doors.Length != 1) return false;
        basis = owned.Single(m => m.Id == "baseModel"); door = doors[0];
        if (basis.Pointer == door.Pointer || door.Pointer != hatchDoor) return false;
        return commander == 0 || (basis.Association == commander && door.Association == commander);
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
