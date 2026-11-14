namespace SprocketTelescopicMast.Io;

// Unity-free fire decision. FireInternal moves ProjectileRegister.nextID only when its Launch
// actually created a projectile, so a prefix/postfix pair compares that integer around the call.
// A cancelled launch (another mod's prefix) leaves the counter untouched and cannot start a pulse
// or consume the 150 ms window of the next real shot.
internal static class FireGate
{
    /// <summary>True only when the native projectile counter moved between the two samples.</summary>
    internal static bool Launched(int nextIdBefore, int nextIdAfter) => nextIdBefore != nextIdAfter;

    internal static bool Accept(bool launched, long cannonKey, double now, DuplicateFilter duplicates) =>
        launched && duplicates.Accept(cannonKey, now);
}

/// <summary>Bounded, one-consumption shot batches keyed by live vehicle identity, never cannon VUID.</summary>
internal sealed class CannonShotBuffer
{
    private readonly Dictionary<IntPtr, List<CannonShot>> pending = new();
    internal void Add(IntPtr vehicle, CannonShot shot)
    {
        if (vehicle == IntPtr.Zero) return;
        if (!pending.TryGetValue(vehicle, out var list)) pending[vehicle] = list = new();
        if (list.Count < 64) list.Add(shot);
    }
    internal void Take(IntPtr vehicle, List<CannonShot> into)
    { if (pending.Remove(vehicle, out var list)) into.AddRange(list); }
    internal void Discard(IntPtr vehicle) => pending.Remove(vehicle);
}
