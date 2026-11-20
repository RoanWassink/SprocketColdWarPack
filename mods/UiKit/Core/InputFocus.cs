namespace SprocketUiKit;

internal static class InputFocusPolicy
{
    internal static bool CanFocus(bool lifecycleAllowed, bool paused, bool stayOpen) => lifecycleAllowed && (!paused || stayOpen);

    // Shared by shortcuts and Enter; rendering order is deliberately not an input.
    internal static T? ShortcutTarget<T>(T? focusedPanel, bool popupOpen) where T : class =>
        popupOpen ? null : focusedPanel;

    internal static bool Block(bool panel, bool text, bool panelEnabled, bool typingEnabled) =>
        panel && panelEnabled || text && typingEnabled;
}

/// <summary>Windows-style keyboard owner, independent of pointer hover and rendering order.</summary>
internal sealed class KeyboardFocusState<T> where T : class
{
    internal T? Current { get; private set; }
    internal void Open(T panel, bool normal) { if (normal) Current = panel; }
    internal void Press(T? panel, bool normal = true) { Current = normal ? panel : null; }
    internal void Clear() => Current = null;
    internal void Close(T panel, IReadOnlyList<T> order, Func<T, bool> eligible)
    {
        if (!ReferenceEquals(Current, panel)) return;
        Current = null;
        for (int i = order.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(order[i], panel) && eligible(order[i])) { Current = order[i]; break; }
    }
}

internal sealed class RescanThrottle
{
    private bool active;
    private float next;
    internal bool Due(bool requested, float now)
    {
        if (!requested) { active = false; return false; }
        if (active && now < next) return false;
        active = true; next = now + 1; return true;
    }
}

/// <summary>Own only actions we disabled. Retain failed restores for a later retry.</summary>
internal sealed class SuspensionSet<T>
{
    private readonly Dictionary<nint, T> owned = new();
    private readonly List<nint> restored = new();
    internal int Count => owned.Count;
    internal void Suspend(nint key, T action, bool enabled, Action<T> disable)
    {
        if (!enabled) return;
        owned.TryAdd(key, action); // record before the native call, including its exception path
        disable(action);
    }
    internal Exception? Restore(Action<T> enable)
    {
        Exception? failure = null;
        restored.Clear();
        foreach (var pair in owned)
        {
            try { enable(pair.Value); restored.Add(pair.Key); }
            catch (Exception ex) { failure ??= ex; }
        }
        foreach (var key in restored) owned.Remove(key);
        return failure;
    }
}

/// <summary>Snapshots labels, so in-place edits and new list instances both work without idle allocation.</summary>
internal sealed class TabLabelCache
{
    private string[] labels = Array.Empty<string>();
    internal IReadOnlyList<string> Labels => labels;
    internal bool Poll(IReadOnlyList<string> source)
    {
        bool changed = source.Count != labels.Length;
        if (changed) labels = new string[source.Count];
        for (int i = 0; i < labels.Length; i++)
        {
            string value = source[i] ?? "";
            if (labels[i] != value) { labels[i] = value; changed = true; }
        }
        return changed;
    }
}
