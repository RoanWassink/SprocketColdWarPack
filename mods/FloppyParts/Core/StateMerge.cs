namespace SprocketFloppyParts;

internal interface IStateBag<TType>
{
    int Count { get; }
    string NameAt(int index);
    string? ValueAt(int index);
    void Set(int index, string key, string value, TType type);
    void Add(string key, string value, TType type);
}

internal static class StateMerge
{
    /// <summary>
    /// Adds or replaces one string key without reordering or modifying other entries.
    /// Returns true if an existing entry was replaced.
    /// </summary>
    internal static bool Merge<TType>(IStateBag<TType> state, string key, string value, TType type)
    {
        for (int i = 0; i < state.Count; i++)
        {
            if (!string.Equals(state.NameAt(i), key, StringComparison.Ordinal)) continue;
            state.Set(i, key, value, type);
            return true;
        }
        state.Add(key, value, type);
        return false;
    }
}
