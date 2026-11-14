namespace SprocketExpedition.Core.Encounters;

// Rules for designs that carry a MinimumRisk. A design is only a candidate for
// a plan when the plan's risk reaches that minimum. Bosses, rewards, starters and
// veterans always require a design without a minimum risk.
public static class DangerousDesignPolicy
{
    public static bool IsDangerousOnly(EnemyDesignEntry? design) =>
        design?.MinimumRisk == EncounterRisk.Dangerous;

    public static bool Allows(EnemyDesignEntry? design, EncounterRisk risk) =>
        design?.MinimumRisk is not { } minimum
        || (int)risk >= (int)minimum;

    public static bool AllowsAsReward(EnemyDesignEntry? design) =>
        design?.MinimumRisk is null;

    public static bool AllowsAsBoss(EnemyDesignEntry? design) =>
        design?.MinimumRisk is null;

    public static bool AllowsAsPlayerContent(EnemyDesignEntry? design) =>
        design?.MinimumRisk is null;
}

// Content-addressed markers (<sha256>.blueprint) in the normal and dangerous
// community folders. A marker that exists in both folders is imported only from
// the dangerous folder; the duplicates are reported once by the caller.
public sealed record DangerousBlueprintScan(
    IReadOnlyList<string> CommunityOnly,
    IReadOnlyList<string> Dangerous,
    IReadOnlyList<string> Duplicates);

public static class DangerousBlueprintFolders
{
    public static DangerousBlueprintScan Resolve(
        IEnumerable<string>? communityMarkers,
        IEnumerable<string>? dangerousMarkers)
    {
        var community = Normalize(communityMarkers);
        var dangerous = Normalize(dangerousMarkers);
        var dangerousSet = new HashSet<string>(dangerous, StringComparer.Ordinal);
        return new DangerousBlueprintScan(
            community.Where(marker => !dangerousSet.Contains(marker)).ToArray(),
            dangerous,
            community.Where(marker => dangerousSet.Contains(marker)).ToArray());
    }

    private static string[] Normalize(IEnumerable<string>? markers) =>
        (markers ?? Array.Empty<string>())
            .Where(marker => !string.IsNullOrWhiteSpace(marker))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(marker => marker, StringComparer.Ordinal)
            .ToArray();
}
