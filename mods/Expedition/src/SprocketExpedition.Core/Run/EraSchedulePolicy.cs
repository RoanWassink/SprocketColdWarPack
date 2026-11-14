namespace SprocketExpedition.Core.Run;

// One native era as the game lists it. StartKey orders eras: year * 10000 +
// month * 100 + day of the native start date. HasUsableTank is true when at least
// one usable, non-stationary tank is loaded for that era. A plugin file, an era
// entry alone or a catalogue card is not proof that its dependencies exist.
public sealed record NativeEraInfo(string Name, int StartKey, bool Playable, bool HasUsableTank);

// Result of the schedule decision. EraNames is the ordered schedule of a new run.
// Skipped lists "Name (reason)" for each native era that was left out.
public sealed record EraScheduleDecision(
    IReadOnlyList<string> EraNames,
    IReadOnlyList<string> Skipped,
    string Reason)
{
    public bool IsUsable => EraSchedulePolicy.IsValidEraCount(EraNames.Count);
}

// The era schedule is dynamic: every playable native era that has a usable,
// non-stationary tank, ordered by native start date. There are no fixed names or
// counts, so a new native era (for example a mod adding Coldwar2) works at once.
// A run pins the schedule it started with and keeps it.
public static class EraSchedulePolicy
{
    // A run needs at least a start era and one more era to progress through.
    public const int MinimumEraCount = 2;
    // Sanity bound for persisted data, not a game design limit.
    public const int MaximumEraCount = 32;

    public static bool IsValidEraCount(int count) =>
        count is >= MinimumEraCount and <= MaximumEraCount;

    public static bool IsValidStartingEra(int index, int eraCount) =>
        IsValidEraCount(eraCount) && index >= 0 && index < eraCount;

    // Era family: "Coldwar2" and "Coldwar3" belong to the "Coldwar" family. Used
    // to inherit per-era data (mass thresholds, music) for eras without their own.
    public static string FamilyName(string? era)
    {
        if (string.IsNullOrEmpty(era)) return string.Empty;
        var end = era.Length;
        while (end > 1 && char.IsDigit(era[end - 1])) end--;
        return era.Substring(0, end);
    }

    public static EraScheduleDecision Resolve(IReadOnlyList<NativeEraInfo> nativeEras)
    {
        if (nativeEras is null) throw new ArgumentNullException(nameof(nativeEras));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var era in nativeEras)
        {
            if (era is null || string.IsNullOrWhiteSpace(era.Name))
                throw new ArgumentException("Every native era needs a name.", nameof(nativeEras));
            if (!names.Add(era.Name))
                throw new ArgumentException($"Native era '{era.Name}' is listed twice.", nameof(nativeEras));
        }

        // Stable sort on the start date; ties keep the native list order.
        var ordered = nativeEras.Select((era, position) => (era, position))
            .OrderBy(pair => pair.era.StartKey).ThenBy(pair => pair.position)
            .Select(pair => pair.era).ToArray();
        var schedule = new List<string>();
        var skipped = new List<string>();
        foreach (var era in ordered)
        {
            if (!era.Playable) skipped.Add($"{era.Name} (not playable)");
            else if (!era.HasUsableTank) skipped.Add($"{era.Name} (no usable non-stationary tank)");
            else schedule.Add(era.Name);
        }

        var listed = schedule.Count == 0 ? "none" : string.Join(", ", schedule);
        var reason = $"Era schedule: {listed} ({schedule.Count} era(s))."
            + (skipped.Count == 0 ? string.Empty : " Skipped: " + string.Join(", ", skipped) + ".");
        if (!IsValidEraCount(schedule.Count))
            reason = $"Era schedule unusable: at least {MinimumEraCount} playable eras with a usable "
                + $"non-stationary tank are required and {schedule.Count} found. " + reason;
        return new EraScheduleDecision(schedule, skipped, reason);
    }
}
