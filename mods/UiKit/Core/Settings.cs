namespace SprocketUiKit;

// Pure policies shared by retained settings rows and tests.
internal static class SettingsPolicy
{
    internal static int Control(int kind) => kind is >= 0 and <= 3 ? kind : -1;
    internal static bool Include(bool visible, bool advanced, bool showAdvanced) => visible && (!advanced || showAdvanced);
    internal static bool ResetVisible(object value, object defaults) => !Equals(value, defaults);
    internal static bool Show(bool open, bool configuring, bool paused, bool stayOpen) => open && !configuring && (!paused || stayOpen);
}

internal sealed class SettingWriteGate
{
    private double last = double.NegativeInfinity;
    internal bool Pending { get; private set; }
    internal double Value { get; private set; }
    internal void Queue(double value) { Value = value; Pending = true; }
    internal bool Take(double now, bool release, out double value)
    {
        value = Value;
        if (!Pending || (!release && now - last < .250)) return false;
        Pending = false; last = now; return true;
    }
}

internal sealed class DeferredSettingsOpen
{
    internal bool Pending { get; private set; }
    internal void Request(bool configuring) => Pending = configuring;
    internal void Cancel() => Pending = false;
    internal bool Reveal(bool configuring, bool paused, bool stayOpen)
    {
        if (!Pending || !SettingsPolicy.Show(true, configuring, paused, stayOpen)) return false;
        Pending = false; return true;
    }
}

// Retain a snapshot until membership/order changes; callbacks may mutate the source safely.
internal sealed class StableSnapshot<T>
{
    private T[] items = Array.Empty<T>();
    internal T[] Get(IReadOnlyList<T> source)
    {
        bool same = source.Count == items.Length;
        for (int i = 0; same && i < items.Length; i++) same = EqualityComparer<T>.Default.Equals(items[i], source[i]);
        if (!same) { items = new T[source.Count]; for (int i = 0; i < items.Length; i++) items[i] = source[i]; }
        return items;
    }
}
internal static class TextCommitPolicy
{
    internal static bool ShouldWrite(bool commit, bool changed) => commit && changed;
}
