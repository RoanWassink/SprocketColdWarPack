namespace SprocketExpandedBallistics;

// Gameplay defaults; this is a subcalibre AP approximation, not a long-rod model.
internal readonly record struct DartSettings(double DiameterRatio = .22, double LengthInCalibres = 5,
    double Density = 17500, double VelocityEfficiency = .85, double MaxVelocityFactor = 2.2,
    double MaxVelocity = 2200, double PenetrationQuality = .45, double DamageMultiplier = .5,
    double VelocityMultiplier = 1, double? CarrierDiameterFactor = null, double? CarrierMassFraction = null)
{
    public DartSettings() : this(.22, 5, 17500, .85, 2.2, 2200, .45, .5) { }
    internal bool HasCarrier => CarrierDiameterFactor.HasValue || CarrierMassFraction.HasValue;
}

internal readonly record struct DartBallistics(float Diameter, float Length, float Mass,
    float Velocity, ushort PenetratorConstant, float DamageMultiplier,
    float CarrierDiameter = 0, float CarrierMass = 0)
{
    // A configured carrier is the projectile native drag sees; the core is what
    // reaches the plate. Profiles without a carrier use the core for both.
    internal float FlightDiameter => CarrierDiameter > 0 ? CarrierDiameter : Diameter;
    internal float FlightMass => CarrierMass > 0 ? CarrierMass : Mass;
}

internal static class ShellBallistics
{
    // Core diameter, length and mass from calibre and profile geometry. No native types.
    internal static (double Diameter, double Length, double Mass) CoreGeometry(double calibreMm, DartSettings settings)
    {
        if (!double.IsFinite(calibreMm) || calibreMm <= 0 || calibreMm > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(calibreMm), "Invalid cannon ballistics.");
        var diameter = calibreMm * .001 * settings.DiameterRatio;
        var length = calibreMm * .001 * settings.LengthInCalibres;
        var mass = Math.PI * diameter * diameter * .25 * length * settings.Density;
        return (diameter, length, mass);
    }
    internal static DartBallistics Calculate(double calibreMm, double vanillaVelocity,
        ushort vanillaK, DartSettings settings)
    {
        if (!double.IsFinite(calibreMm) || calibreMm <= 0 || calibreMm > ushort.MaxValue ||
            !double.IsFinite(vanillaVelocity) || vanillaVelocity <= 0 || vanillaK == 0)
            throw new ArgumentOutOfRangeException(nameof(calibreMm), "Invalid cannon ballistics.");
        ValidateSettings(settings);
        var core = CoreGeometry(calibreMm, settings);
        var diameter = core.Diameter;
        var length = core.Length;
        var mass = core.Mass;
        var vanillaMass = 1.59e-5 * calibreMm * calibreMm * calibreMm;
        // APCR/HVAP: a light full-bore carrier drags natively, while the impact
        // rewrite below hands native penetration only the dense sub-calibre core.
        var carrierDiameter = calibreMm * .001 * (settings.CarrierDiameterFactor ?? settings.DiameterRatio);
        var carrierMass = settings.CarrierMassFraction is {} fraction ? fraction * vanillaMass : mass;
        var velocityFactor = Math.Clamp(Math.Sqrt(vanillaMass / carrierMass) * settings.VelocityEfficiency * settings.VelocityMultiplier,
            1, settings.MaxVelocityFactor);
        var velocity = Math.Min(vanillaVelocity * velocityFactor, settings.MaxVelocity);
        var k = Math.Clamp(Math.Round(vanillaK / Math.Pow(settings.PenetrationQuality, 1 / 1.43)), 1, ushort.MaxValue);
        var hasCarrier = settings.HasCarrier;
        return new((float)diameter, (float)length, (float)mass, (float)velocity, (ushort)k,
            (float)settings.DamageMultiplier, hasCarrier ? (float)carrierDiameter : 0f,
            hasCarrier ? (float)carrierMass : 0f);
    }

    internal static string RangeError(string? profileId, string field, double value, double min, double max) =>
        FormattableString.Invariant($"{(profileId is null ? "Shell settings" : $"Profile '{profileId}'")}: {field} is {value}; expected {min}–{max} (finite, inclusive).");

    internal static void ValidateSettings(DartSettings settings, string? profileId = null)
    {
        void Range(string field, double value, double min, double max)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(field, RangeError(profileId, field, value, min, max));
        }
        Range("penetratorDiameterFactor", settings.DiameterRatio, .05, .9);
        Range("penetratorLengthInCalibres", settings.LengthInCalibres, .5, 10);
        Range("penetratorDensity", settings.Density, 1000, 25000);
        Range("velocityEfficiency", settings.VelocityEfficiency, .1, 2);
        Range("velocityMultiplier", settings.VelocityMultiplier, .1, 4);
        Range("maximumVelocityFactor", settings.MaxVelocityFactor, 1, 4);
        Range("maximumVelocity", settings.MaxVelocity, 100, 5000);
        Range("penetrationQuality", settings.PenetrationQuality, .1, 4);
        Range("fragmentDamageMultiplier", settings.DamageMultiplier, .01, 1);
        if (settings.CarrierDiameterFactor is {} carrierDiameter)
            Range("carrierDiameterFactor", carrierDiameter, .05, 1);
        if (settings.CarrierMassFraction is {} carrierMass)
            Range("carrierMassFraction", carrierMass, .05, 1.5);
    }
}


