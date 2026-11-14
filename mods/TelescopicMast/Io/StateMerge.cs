namespace SprocketTelescopicMast.Io;

// Unity-free merge policy for one serialized object state. The runtime adapter in HingeRuntime
// wraps SerializationInfo; tests use an in-memory bag. Only the requested key is written, so every
// other entry keeps its name, value and type and the state object itself is not replaced.
internal interface IStateBag<TType>
{
    int Count { get; }
    string NameAt(int index);
    /// <summary>The string value at index; callers only read keys they wrote as strings.</summary>
    string? ValueAt(int index);
    void Set(int index, string key, string value, TType type);
    void Add(string key, string value, TType type);
}

internal static class StateMerge
{
    /// <summary>
    /// Add or replace exactly one string key. Returns true when an existing entry was replaced.
    /// No other entry is read, copied or reordered.
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
