namespace SprocketExpandedBallistics;

/// <summary>
/// Pure policy for the Expedition-only picker filter. The native definition and
/// every saved blueprint remain untouched; only the display option list is filtered.
/// </summary>
internal static class SightMenuPolicy
{
    internal const string VanillaGunnerSightGuid = "f90f373f-4edb-4563-a162-6eee6a5d77dd";
    internal const string VanillaMarker = "sprocket.expandedballistics.native-vanilla-sight:";

    internal static bool ShouldHide(bool? expeditionOwnsSession) => expeditionOwnsSession == true;

    internal static bool IsVanillaMarker(string? tag) =>
        tag == VanillaMarker + VanillaGunnerSightGuid;

    internal static bool IsVanillaGuid(string? guid) =>
        string.Equals(guid, VanillaGunnerSightGuid, StringComparison.OrdinalIgnoreCase);

    internal static string Label(string? guid) =>
        IsVanillaGuid(guid) ? "Vanilla" : string.IsNullOrWhiteSpace(guid) ? "?" : guid!;

    /// <summary>Remap the active group index after groups that became empty are removed.</summary>
    internal static int RemapActive(int oldActive, IReadOnlyList<bool> removed)
    {
        if (oldActive < 0) return 0;
        var kept = 0;
        var active = -1;
        for (var i = 0; i < removed.Count; i++)
        {
            if (removed[i]) continue;
            if (i == oldActive) active = kept;
            kept++;
        }
        if (kept == 0) return 0;
        if (active >= 0) return active;
        // The old active group was removed: choose the nearest remaining group.
        var before = 0;
        for (var i = 0; i < Math.Min(oldActive, removed.Count); i++) if (!removed[i]) before++;
        return Math.Clamp(before, 0, kept - 1);
    }
}
