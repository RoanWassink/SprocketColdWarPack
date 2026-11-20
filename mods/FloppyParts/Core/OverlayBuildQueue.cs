namespace SprocketFloppyParts;

internal sealed class OverlayBuildQueue<T>
{
    private readonly Dictionary<long, (T Plate, float Due)> items = new();

    internal int Count => items.Count;

    internal bool Contains(long key) => items.ContainsKey(key);

    internal void Schedule(long key, T plate, float now, float delay, bool replace = false)
    {
        if (replace || !items.ContainsKey(key))
            items[key] = (plate, now + delay);
    }

    internal (long Key, T Plate)[] TakeDue(float now, int budget)
    {
        if (budget <= 0 || items.Count == 0) return Array.Empty<(long, T)>();
        var ready = new List<(long Key, T Plate)>(Math.Min(budget, items.Count));
        foreach (var pair in items)
        {
            if (pair.Value.Due <= now)
            {
                ready.Add((pair.Key, pair.Value.Plate));
                if (ready.Count >= budget) break;
            }
        }
        foreach (var item in ready)
            items.Remove(item.Key);
        return ready.ToArray();
    }

    internal void Remove(long key) => items.Remove(key);

    internal void Clear() => items.Clear();
}
