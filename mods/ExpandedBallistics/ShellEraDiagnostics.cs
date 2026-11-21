namespace SprocketExpandedBallistics;

internal enum TechnologyFilesKind { Ok, MissingDirectory, MissingAll, MissingCore }

internal sealed record TechnologyFilesStatus(
    TechnologyFilesKind Kind,
    int FoundCount,
    IReadOnlyList<string> MissingCoreFiles,
    string? Warning)
{
    internal bool Ok => Kind == TechnologyFilesKind.Ok;
}

/// <summary>
/// Pure diagnostics for missing Sprocket_Data technology files or broken native Eras timelines.
/// Explains why shells show as "Unavailable" and how the user can fix their installation.
/// </summary>
internal static class ShellEraDiagnostics
{
    internal const string TechnologyRelativePath = @"Sprocket_Data\StreamingAssets\Technology\";
    internal const string ErasRelativePath = @"Sprocket_Data\StreamingAssets\Eras\";
    internal static readonly ShellNativeDate EarliestCoreTechDate = new(1914, 7, 28);

    internal static readonly string[] CoreTechnologyFiles =
    {
        "ExpandedBallistics-ap.json",
        "ExpandedBallistics-he.json",
        "ExpandedBallistics-aphe.json",
        "ExpandedBallistics-heat.json",
        "ExpandedBallistics-hesh.json",
        "ExpandedBallistics-apfsds.json",
        "ExpandedBallistics-atgm.json",
        "ExpandedBallistics-atgm_gun.json"
    };

    internal static TechnologyFilesStatus CheckTechnologyDirectory(string? technologyDirectory)
    {
        if (string.IsNullOrWhiteSpace(technologyDirectory) || !Directory.Exists(technologyDirectory))
            return new(
                TechnologyFilesKind.MissingDirectory,
                0,
                CoreTechnologyFiles,
                $"Warning: {TechnologyRelativePath} was not found. Copy the mod's Sprocket_Data folder into your Sprocket game folder (next to BepInEx) and restart Sprocket.");

        try
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(technologyDirectory, "*.json"))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("ExpandedBallistics-", StringComparison.OrdinalIgnoreCase))
                    found.Add(name);
            }

            if (found.Count == 0)
                return new(
                    TechnologyFilesKind.MissingAll,
                    0,
                    CoreTechnologyFiles,
                    $"Warning: Missing ExpandedBallistics-*.json files in {TechnologyRelativePath}. Copy the mod's Sprocket_Data folder into your Sprocket game folder (next to BepInEx) and restart Sprocket.");

            var missing = CoreTechnologyFiles.Where(f => !found.Contains(f)).ToArray();
            if (missing.Length > 0)
            {
                var preview = missing.Length <= 4
                    ? string.Join(", ", missing)
                    : string.Join(", ", missing.Take(4)) + $", +{missing.Length - 4} more";
                return new(
                    TechnologyFilesKind.MissingCore,
                    found.Count,
                    missing,
                    $"Warning: Incomplete shell technology files in {TechnologyRelativePath} (missing: {preview}). Re-copy the mod's Sprocket_Data folder into your Sprocket game folder (next to BepInEx) and restart Sprocket.");
            }

            return new(TechnologyFilesKind.Ok, found.Count, Array.Empty<string>(), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(
                TechnologyFilesKind.MissingDirectory,
                0,
                CoreTechnologyFiles,
                $"Warning: Cannot read {TechnologyRelativePath} ({ex.Message}). Check folder permissions and ensure the mod's Sprocket_Data files are installed, then restart Sprocket.");
        }
    }

    internal static string? DiagnoseTimeline(
        ShellNativeDate? vehicleDate,
        string?[]? eraNames,
        ShellNativeDate[]? eraStarts,
        int resolvedIndex)
    {
        if (eraNames == null || eraStarts == null || eraNames.Length == 0 || eraStarts.Length == 0)
            return $"Warning: No eras are loaded from {ErasRelativePath}. Restore or fix the Eras JSON files in your Sprocket game folder and restart Sprocket.";

        if (eraNames.Length != eraStarts.Length)
            return $"Warning: Invalid era timeline in {ErasRelativePath} (era names and start dates do not match). Restore or fix the Eras JSON files and restart Sprocket.";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < eraNames.Length; i++)
        {
            var name = eraNames[i];
            if (string.IsNullOrWhiteSpace(name))
                return $"Warning: Invalid era timeline in {ErasRelativePath} (era #{i + 1} is missing a name). Restore or fix the Eras JSON files and restart Sprocket.";
            if (!seen.Add(name))
                return $"Warning: Invalid era timeline in {ErasRelativePath} (duplicate era name '{name}'). Remove conflicting Eras JSON files and restart Sprocket.";
        }

        for (var i = 0; i < eraStarts.Length; i++)
        {
            if (!eraStarts[i].Valid)
                return $"Warning: Invalid era timeline in {ErasRelativePath} (era '{eraNames[i]}' has an invalid start date). Restore or fix the Eras JSON files and restart Sprocket.";
        }

        if (eraStarts[^1] == ShellNativeDate.Maximum)
            return $"Warning: Invalid era timeline in {ErasRelativePath} (last era '{eraNames[^1]}' uses 9999.12.31 as its start date). Restore or fix the Eras JSON files and restart Sprocket.";

        for (var i = 1; i < eraStarts.Length; i++)
        {
            if (eraStarts[i] <= eraStarts[i - 1])
                return $"Warning: Invalid era timeline in {ErasRelativePath} (era start dates are not strictly ascending: '{eraNames[i - 1]}' {eraStarts[i - 1].Text} vs '{eraNames[i]}' {eraStarts[i].Text}). Remove conflicting Eras JSON files and restart Sprocket.";
        }

        if (vehicleDate is { } date)
        {
            if (!date.Valid)
                return "Warning: This vehicle has an invalid design date. Select a valid era/date on the vehicle.";

            if (resolvedIndex < 0 || resolvedIndex >= eraNames.Length)
                return $"Warning: This vehicle's design date ({date.Text}) does not match any era in {ErasRelativePath}. Select a valid era/date on the vehicle or check your Eras JSON files.";

            if (date != ShellNativeDate.Maximum &&
                (date < eraStarts[resolvedIndex] || (resolvedIndex < eraStarts.Length - 1 && date >= eraStarts[resolvedIndex + 1])))
                return $"Warning: This vehicle's design date ({date.Text}) does not match the active era timeline in {ErasRelativePath}. Check your Eras JSON files or select a valid era on the vehicle.";
        }

        return null;
    }

    internal static string? Diagnose(
        TechnologyFilesStatus filesStatus,
        string? timelineWarning,
        ShellEraContext? context,
        ShellNativeDate? earliestKnownTechDate = null)
    {
        var filesWarning = filesStatus.Warning;
        if (!string.IsNullOrEmpty(filesWarning) && !string.IsNullOrEmpty(timelineWarning))
        {
            const string prefix = "Warning: ";
            var second = timelineWarning.StartsWith(prefix, StringComparison.Ordinal)
                ? timelineWarning[prefix.Length..]
                : timelineWarning;
            return filesWarning + " Also: " + second;
        }
        if (!string.IsNullOrEmpty(filesWarning)) return filesWarning;
        if (!string.IsNullOrEmpty(timelineWarning)) return timelineWarning;

        if (context != null && context.AvailableBehaviors.Count == 0)
        {
            var floor = earliestKnownTechDate ?? EarliestCoreTechDate;
            if (context.Date >= floor)
                return $"Warning: Shell technology files exist in {TechnologyRelativePath}, but none are loaded in this session. Restart Sprocket (or verify the JSON files are valid) so the game loads them.";
        }

        return null;
    }
}
