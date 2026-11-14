namespace SprocketTelescopicMast;

// Unity-free lifecycle policy shared by the native adapter and the managed tests.
internal sealed class PartPaintCache<T> where T : class
{
    private sealed class Entry
    {
        internal IntPtr Owner, Vehicle, Transform;
        internal T? Material;
        internal bool Failed;
    }
    private readonly Dictionary<IntPtr, Entry> entries = new();
    private readonly Action<T> dispose;
    private readonly Action<Exception> warn;
    internal PartPaintCache(Action<T> dispose, Action<Exception> warn)
    { this.dispose = dispose; this.warn = warn; }

    internal void Prepare(IntPtr model, IntPtr owner, IntPtr vehicle, IntPtr transform,
        Func<T> create, Action<T> bind)
    {
        try
        {
            if (entries.TryGetValue(model, out var entry) &&
                (entry.Owner != owner || entry.Vehicle != vehicle || entry.Transform != transform))
            { Drop(model); entry = null; }
            if (entry == null)
            {
                if (entries.Count >= 4096) throw new InvalidOperationException("Part paint cache is full.");
                entries.Add(model, entry = new Entry { Owner = owner, Vehicle = vehicle, Transform = transform });
            }
            if (entry.Failed) return;
            try
            {
                entry.Material ??= create();
                bind(entry.Material);
            }
            catch { entry.Failed = true; throw; }
        }
        catch (Exception ex) { warn(ex); }
    }
    private void Drop(IntPtr model)
    {
        if (entries.Remove(model, out var entry) && entry.Material != null)
            try { dispose(entry.Material); } catch (Exception ex) { warn(ex); }
    }
    internal void Release(IntPtr owner)
    {
        foreach (var key in entries.Where(x => x.Value.Owner == owner).Select(x => x.Key).ToArray()) Drop(key);
    }
    internal void Clear()
    { foreach (var key in entries.Keys.ToArray()) Drop(key); }
}
