using System.Globalization;
namespace SprocketEngineExpansion.Core;

/// <summary>
/// Availability dates for the three thermodynamic profiles. Native layouts
/// (inline, radial, V) keep their own Technology dates and are not changed.
/// </summary>
public static class EngineDates
{
    public static readonly DateTime PetrolDate = new(1914, 7, 28);
    public static readonly DateTime DieselDate = new(1935, 1, 1);
    public static readonly DateTime TurbineDate = new(1967, 1, 1);
    public static readonly DateTime DefaultPowerpackDate = new(1965, 1, 1);

    /// <summary>Configured through Powerpack.AvailableFrom; gated on the vehicle date only.</summary>
    public static DateTime PowerpackDate { get; set; } = DefaultPowerpackDate;

    public static DateTime For(EngineKind kind) => kind switch
    {
        EngineKind.Petrol => PetrolDate,
        EngineKind.Diesel => DieselDate,
        EngineKind.GasTurbine => TurbineDate,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool Allows(DateTime? vehicleDate, EngineKind kind) =>
        Allows(vehicleDate, For(kind));

    public static bool AllowsPowerpack(DateTime? vehicleDate) =>
        Allows(vehicleDate, PowerpackDate);

    // The exact final-era sentinel 9999.12.31 allows everything.
    private static bool Allows(DateTime? vehicleDate, DateTime from) =>
        vehicleDate is { } date &&
        (date.Date == DateTime.MaxValue.Date || date.Date >= from.Date);

    /// <summary>
    /// A saved too-early profile keeps its JSON identity but runs as the native
    /// petrol baseline. Unknown dates keep the saved profile (native data still
    /// gates it elsewhere).
    /// </summary>
    public static EngineKind Effective(DateTime? vehicleDate, EngineKind saved)
    {
        if (vehicleDate is null) return saved;
        return Allows(vehicleDate, saved) ? saved : EngineKind.Petrol;
    }

    /// <summary>A saved too-early powerpack keeps its layout; only the housing mass and cost lapse.</summary>
    public static bool PowerpackEffective(DateTime? vehicleDate, bool saved) =>
        saved && (vehicleDate is null || AllowsPowerpack(vehicleDate));

    /// <summary>Parses the config value (yyyy.MM.dd); invalid text keeps the default.</summary>
    public static DateTime ParseDate(string? text, DateTime fallback) =>
        DateTime.TryParseExact(text?.Trim(), new[] { "yyyy.MM.dd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.Date : fallback;
}
