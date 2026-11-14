namespace SprocketTelescopicMast;

/// <summary>
/// The three mast/telescope parts are dated 1967.01.01. Hinge Base/Leaf/Mount
/// are workshop hardware and stay at 0000.01.01 (all dates).
/// </summary>
public static class MastDatePolicy
{
    public static readonly DateTime MastDate = new(1967, 1, 1);
    public static readonly DateTime HingeDate = new(1, 1, 1);

    public static bool Allows(DateTime? vehicleDate, DateTime availableFrom) =>
        vehicleDate is { } date &&
        (date.Date == DateTime.MaxValue.Date || date.Date >= availableFrom.Date);
}
