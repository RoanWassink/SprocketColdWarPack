namespace SprocketCarouselAutoloader;

// 0.2.28 safety pattern (BESLUITEN 2026-11-03: "clean, stable mod"; H2/H3 "Alleen bewaken", H4 switch, middle package).
// Pure logic without game types, so the offline tests run exactly this code. The runtime glue is in SafetyRuntime.cs.

/// <summary>Time-out for an intervention that holds a native load task in an artificial state:
/// 3 x the expected cycle, at least 10 s; an unknown or invalid cycle gives the minimum.</summary>
internal static class GuardPolicy
{
    internal const double MinimumTimeoutSeconds = 10;
    internal const double TimeoutCycles = 3;

    internal static double TimeoutSeconds(double expectedCycleSeconds) =>
        double.IsFinite(expectedCycleSeconds) && expectedCycleSeconds > 0
            ? Math.Max(MinimumTimeoutSeconds, TimeoutCycles * expectedCycleSeconds) : MinimumTimeoutSeconds;

    internal static bool Expired(double started, double now, double expectedCycleSeconds) =>
        double.IsFinite(started) && double.IsFinite(now) && now - started > TimeoutSeconds(expectedCycleSeconds);
}

/// <summary>Open interventions per native object (pointer), each with the snapshot of the native state taken
/// just before the intervention. A normal end closes the entry; a time-out or an error closes it and returns the
/// snapshot, which the caller writes back.</summary>
internal sealed class InterventionLedger<T> where T : class
{
    private readonly Dictionary<IntPtr, (T Snapshot, double Started)> open = new();
    internal int Count => open.Count;
    internal bool Contains(IntPtr key) => open.ContainsKey(key);

    /// <summary>False when the object already has an open intervention (the first snapshot is kept).</summary>
    internal bool Begin(IntPtr key, T snapshot, double now)
    {
        if (open.ContainsKey(key)) return false;
        open[key] = (snapshot, now);
        return true;
    }

    /// <summary>Normal end (resumed, released, object disabled): nothing to restore.</summary>
    internal bool Complete(IntPtr key) => open.Remove(key);

    /// <summary>The snapshot to restore when the intervention is older than the time-out; null otherwise.</summary>
    internal T? Expired(IntPtr key, double now, double expectedCycleSeconds)
    {
        if (!open.TryGetValue(key, out var entry) || !GuardPolicy.Expired(entry.Started, now, expectedCycleSeconds)) return null;
        open.Remove(key);
        return entry.Snapshot;
    }

    /// <summary>Error: the snapshot to restore, or null when nothing was open.</summary>
    internal T? Abort(IntPtr key) => open.Remove(key, out var entry) ? entry.Snapshot : null;

    internal double Started(IntPtr key) => open.TryGetValue(key, out var entry) ? entry.Started : double.NaN;
}

/// <summary>One warning per hook and id (VUID): a failing hook never spams the log per frame.</summary>
internal sealed class WarnOnce
{
    private readonly HashSet<(string Hook, int Id)> seen = new();
    internal int Count => seen.Count;
    internal bool First(string hook, int id) => seen.Add((hook, id));
}

/// <summary>An intervention switched off after a failure stays off for the rest of the session. The key is
/// "design|VUID", so a respawn of the same vehicle (new pointers) stays off too; the pointer cache keeps the
/// hot-path check to one dictionary lookup, and nothing at all while no intervention failed.</summary>
internal sealed class SessionSwitch
{
    private readonly HashSet<string> keys = new();
    private readonly Dictionary<IntPtr, bool> pointers = new();
    internal bool Any => keys.Count > 0;
    internal bool? Cached(IntPtr pointer) => pointers.TryGetValue(pointer, out var off) ? off : null;

    internal bool Resolve(IntPtr pointer, string key)
    {
        var off = keys.Contains(key);
        pointers[pointer] = off;
        return off;
    }

    /// <summary>True only the first time for this key: the caller logs one warning then.</summary>
    internal bool Disable(IntPtr pointer, string key)
    {
        if (pointer != IntPtr.Zero) pointers[pointer] = true;
        return keys.Add(key);
    }

    /// <summary>Called on every topology invalidation: native pointers can be reused for new objects.</summary>
    internal void ForgetPointers() => pointers.Clear();
}

/// <summary>Per-frame early exit (M5): resolves a native object once (the only step that allocates) and answers
/// every later call from a dictionary keyed by the native pointer. An unresolvable object (null) is not cached and
/// is resolved again on the next call; callers treat it as "unknown" and run the 0.2.27 path.</summary>
internal sealed class PresenceCache<T> where T : class
{
    private readonly Dictionary<IntPtr, T> map = new();
    internal int Count => map.Count;
    internal int Resolves { get; private set; }

    internal T? Get<TSource>(IntPtr key, TSource source, Func<TSource, T?> resolve)
    {
        if (map.TryGetValue(key, out var found)) return found;
        Resolves++;
        var value = resolve(source);
        if (value != null) map[key] = value;
        return value;
    }

    internal void Clear() => map.Clear();
}

internal static class EarlyExit
{
    /// <summary>A cannon takes part in Carousel work only when its turret has a bustle, revolver or Semi rack, or an
    /// enabled carousel basket. Every per-frame hook returns at once for other cannons, as its 0.2.27 code did
    /// after allocating its wrappers.</summary>
    internal static bool Candidate(int bustleRacks, int enabledBaskets) => bustleRacks > 0 || enabledBaskets > 0;
}

/// <summary>Config switches of 0.2.28.</summary>
internal static class SafetySwitches
{
    /// <summary>The Unity version whose internal audio bindings (CreateUserSound_Injected, SetData_Injected and the
    /// span layout) AutoloaderAudio was written against: Sprocket 0.2.56.2.</summary>
    internal const string AudioUnityVersion = "6000.3.21f1";

    /// <summary>[Damage] NativeCookoff: false leaves the native-damage hook group unpatched.</summary>
    internal static bool PatchNativeDamage(bool nativeCookoff) => nativeCookoff;

    /// <summary>Null when the own reload audio may run; otherwise the reason it stays off (one Info line).</summary>
    internal static string? AudioOff(bool enabled, string? unityVersion) =>
        !enabled ? "config Audio.Enabled = false" :
        string.IsNullOrWhiteSpace(unityVersion) ? "the Unity version could not be read" :
        unityVersion != AudioUnityVersion ? $"Unity {unityVersion}; its raw Unity audio calls were made for Unity {AudioUnityVersion}" : null;
}
