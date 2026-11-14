namespace SprocketStabilization;

/// <summary>
/// Pure availability policy shared by the runtime and the checks. The exact
/// native final-era sentinel keeps everything available; finite dates before
/// 1948.01.01 cannot use the stabilization technology.
/// </summary>
public static class StabilizationDatePolicy
{
    public static readonly DateTime MinimumDate = new(1948, 1, 1);

    public static bool Allows(DateTime? vehicleDate) =>
        vehicleDate is { } date &&
        (date.Date == DateTime.MaxValue.Date || date.Date >= MinimumDate.Date);
}
