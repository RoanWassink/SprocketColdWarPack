namespace SprocketSmokeLaunchers;

/// <summary>
/// Part dates for the two smoke launchers. The German launcher is a native
/// decorative part made functional; the modern bank is the mod's own part.
/// </summary>
public static class SmokeDatePolicy
{
    public static readonly DateTime GermanLauncherDate = new(1942, 1, 1);
    public static readonly DateTime LauncherBankDate = new(1945, 1, 1);

    public static bool Allows(DateTime? vehicleDate, DateTime availableFrom) =>
        vehicleDate is { } date &&
        (date.Date == DateTime.MaxValue.Date || date.Date >= availableFrom.Date);
}
