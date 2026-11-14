namespace SprocketOscillatingTurret;

/// <summary>
/// The oscillating drive and its generated turret preset share one date. The
/// hinge hardware is not gated; only the oscillating mechanism is.
/// </summary>
public static class OscillatingDatePolicy
{
    public static readonly DateTime MinimumDate = new(1952, 1, 1);

    public static bool Allows(DateTime? vehicleDate) =>
        vehicleDate is { } date &&
        (date.Date == DateTime.MaxValue.Date || date.Date >= MinimumDate.Date);
}
