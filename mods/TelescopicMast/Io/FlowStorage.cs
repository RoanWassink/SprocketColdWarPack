namespace SprocketTelescopicMast.Io;

/// <summary>Per-vehicle copy arbitration and part identity, shared by native and API adapters.</summary>
public sealed class FlowStorage
{
    private readonly Dictionary<string, (string Id, FlowDocument? Copy)> parts = new(StringComparer.Ordinal);
    private readonly HashSet<string> warnings = new();
    public Action<string>? Warning { get; set; }
    public FlowDocument? Flow { get; private set; }
    public string Attach(string stableKey, string? savedId, FlowDocument? copy)
    {
        if (parts.TryGetValue(stableKey, out var old))
        {
            // Repeated LoadData/LoadState for the same object must be idempotent.
            var best = copy != null && (old.Copy == null || copy.Rev > old.Copy.Rev) ? copy : old.Copy;
            parts[stableKey] = (old.Id, best); Select(); return old.Id;
        }
        string id = Guid.TryParse(savedId, out _) ? savedId! : Guid.NewGuid().ToString("D");
        if (parts.Values.Any(p => p.Id == id)) { id = Guid.NewGuid().ToString("D"); Warn("duplicate", "Duplicate ioPartId: assigned a new id; output remains unplaced."); }
        parts[stableKey] = (id, copy); Select(); return id;
    }
    public void Detach(string stableKey) { parts.Remove(stableKey); } // keep graph, so missing outputs survive
    public bool Contains(string partId) => parts.Values.Any(p => p.Id == partId);
    public string? Id(string stableKey) => parts.TryGetValue(stableKey, out var p) ? p.Id : null;
    public void Set(FlowDocument flow) { Flow = flow; }
    /// <summary>Publish an editor revision to every attached copy before a later load can arbitrate.</summary>
    public void Publish(FlowDocument flow)
    { Flow = flow; foreach (var key in parts.Keys.ToArray()) parts[key] = (parts[key].Id, flow); }
    private void Select()
    {
        var copies = parts.OrderBy(p => p.Key, StringComparer.Ordinal).Where(p => p.Value.Copy != null).Select(p => p.Value.Copy!).ToList();
        if (copies.Count == 0) return;
        var selected = copies.OrderByDescending(c => c.Rev).First();
        if (copies.Any(c => FlowJson.Write(c) != FlowJson.Write(selected))) Warn("copies", "Different ioFlowV1 copies: highest rev wins; ties use stable part order.");
        // A local edit or migration is newer than a delayed stale load.
        if (Flow == null || selected.Rev >= Flow.Rev) Flow = selected;
    }
    private void Warn(string key, string message) { if (warnings.Add(key)) Warning?.Invoke(message); }
}
