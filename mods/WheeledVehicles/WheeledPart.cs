using Sprocket.Vehicles.Tracks;

namespace SprocketWheeledVehicles;

internal static class WheeledPart
{
    internal const string Guid = "9a4e7c12-3b8f-4e51-a219-6c8d3f0e5b41";
    internal const string HpsGuid = "75f3d8e2-6c19-4b27-9f50-481c6a8d02b4";

    internal static string? GetMountPartGuid(TrackAssembly? track)
    {
        try { return track?.Suspensions?.SharedMountBlueprint?.Blueprint?.partGuid; }
        catch { return null; }
    }

    internal static bool IsWheeledPartSelected(TrackAssembly? track) =>
        string.Equals(GetMountPartGuid(track), Guid, StringComparison.OrdinalIgnoreCase);

    internal static bool IsHpsSelected(TrackAssembly? track) =>
        string.Equals(GetMountPartGuid(track), HpsGuid, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when wheeled physics/visuals should be active for this track assembly:
    /// either the dedicated Wheeled Suspension part is selected, or wheeled mode was explicitly
    /// enabled (e.g. combined with Hydropneumatic Suspension).
    /// </summary>
    internal static bool TryGetActiveSettings(TrackAssembly? track, out WheeledSettings settings)
    {
        settings = null!;
        if (track == null) return false;
        var bp = track.BlueprintSlot?.Blueprint;
        if (bp == null) return false;

        if (IsWheeledPartSelected(track))
        {
            var s = WheeledProfiles.Get(bp);
            s.Enabled = true;
            if (!s.IsValid()) return false;
            settings = s;
            return true;
        }

        if (WheeledProfiles.TryGet(bp, out var existing) && existing.Enabled && existing.IsValid())
        {
            settings = existing;
            return true;
        }

        return false;
    }
}
