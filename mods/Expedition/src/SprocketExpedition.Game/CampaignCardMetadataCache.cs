namespace SprocketExpedition.Game;

// Process-local managed headers. Runtime wrappers and blueprint bytes stay out.
internal sealed class CampaignCardMetadataCache<T> where T : class
{
    private sealed record Entry(long Length, long ModifiedTicks, string Hash, T Value);
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private string context = string.Empty;
    internal void SetContext(string nativeEraSignature)
    {
        if (context == nativeEraSignature) return;
        entries.Clear(); context = nativeEraSignature;
    }
    internal bool TryGet(string path, long length, long modifiedTicks, string hash, out T value)
    {
        if (entries.TryGetValue(path, out var entry) && entry.Length == length
            && entry.ModifiedTicks == modifiedTicks && entry.Hash == hash)
        { value = entry.Value; return true; }
        value = null!;
        return false;
    }
    internal void Store(string path, long length, long modifiedTicks, string hash, T value) =>
        entries[path] = new Entry(length, modifiedTicks, hash, value);
}
