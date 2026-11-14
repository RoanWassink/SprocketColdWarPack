namespace SprocketExpandedBallistics;

internal readonly record struct ShellStorageDimensions(double ProjectileLengthMm,double PropellantLengthMm,double DiameterMm)
{
    internal double CombinedLengthMm=>ProjectileLengthMm+PropellantLengthMm;
}
internal static class ShellStorageGeometry
{
    internal static ShellStorageDimensions Calculate(double calibreMm,double propellantLengthMm,DartSettings settings)
    {
        ShellBallistics.ValidateSettings(settings);
        if(!double.IsFinite(calibreMm)||calibreMm<=0||!double.IsFinite(propellantLengthMm)||propellantLengthMm<0)
            throw new ArgumentOutOfRangeException(nameof(calibreMm));
        // Preserve native packaging for short projectiles. An actual longer rod
        // can never be treated as a shorter, three-calibre projectile for storage.
        return new(Math.Max(3*calibreMm,settings.LengthInCalibres*calibreMm),propellantLengthMm,calibreMm);
    }
}
