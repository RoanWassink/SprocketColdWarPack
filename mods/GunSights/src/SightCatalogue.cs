namespace SprocketGunSights;

internal enum SightEra { Ww1, EarlyWar, MidWar, LateWar, ColdWar }

// Illuminated Cold War reticles are drawn red; etched WW-era reticles black.
internal enum ReticleInk { Black, Red, Amber }

/// <summary>
/// One historical gunner's sight. Magnifications are the selectable optical
/// powers: two values model a sight with two optics or a switchable power,
/// more values sample a variable-power sight. Estimated = no source confirmed
/// the power; it is a period-plausible value.
/// AvailableFrom is the only availability gate; Era is a catalogue label.
/// </summary>
internal sealed record SightVariant(
    string Id, string Country, SightEra Era, string Name, float[] Magnifications,
    string Reticle, ReticleInk Ink, string Tanks, string Source, bool Estimated,
    DateTime AvailableFrom)
{
    internal string Label => $"{Name} ({SightCatalogue.EraName(Era)}, {SightCatalogue.Powers(this)})";
}

/// <summary>
/// Pure date policy: a variant is usable from its own date, and the exact
/// native final-era sentinel (9999.12.31) makes everything available.
/// </summary>
internal static class SightDatePolicy
{
    internal static bool Allows(DateTime? vehicleDate, DateTime availableFrom) =>
        vehicleDate is { } date &&
        (date.Date == DateTime.MaxValue.Date || date.Date >= availableFrom.Date);

    internal static bool Allows(DateTime? vehicleDate, SightVariant variant) =>
        Allows(vehicleDate, variant.AvailableFrom);
}

internal static class SightCatalogue
{
    internal static readonly string[] Countries = { "Germany", "Soviet Union", "United States", "United Kingdom" };

    private static DateTime D(int year, int month, int day) => new(year, month, day);

    internal static readonly SightVariant[] All =
    {
        // ---------------------------------------------------------------- Germany
        new("de_a7v", "Germany", SightEra.Ww1, "A7V gun telescope", new[] { 2f },
            "post", ReticleInk.Black, "A7V", "Period-plausible WW1 power", true, D(1918, 1, 1)),
        new("de_tzf5a", "Germany", SightEra.EarlyWar, "TZF 5a", new[] { 2.5f },
            "winkel", ReticleInk.Black, "Pz.Kpfw. III Ausf. A-F",
            "TM-E 30-451: 2.5x, 25 deg field", false, D(1937, 1, 1)),
        new("de_tzf5b", "Germany", SightEra.EarlyWar, "TZF 5b", new[] { 2.5f },
            "winkel", ReticleInk.Black, "Pz.Kpfw. IV Ausf. A-F1 (7.5 cm L/24)",
            "TZF 5 family power", true, D(1937, 1, 1)),
        new("de_tzf5f", "Germany", SightEra.MidWar, "TZF 5f", new[] { 2.5f },
            "winkel", ReticleInk.Black, "Pz.Kpfw. IV Ausf. F2-H",
            "TZF 5 family power", true, D(1942, 1, 1)),
        new("de_tzf9b", "Germany", SightEra.MidWar, "TZF 9b", new[] { 2.5f },
            "winkel", ReticleInk.Black, "Tiger I", "TZF 9 family power", true, D(1942, 8, 1)),
        new("de_tzf12a", "Germany", SightEra.LateWar, "TZF 12a", new[] { 2.5f, 5f },
            "winkel", ReticleInk.Black, "Panther Ausf. A, G",
            "TM-E 30-451: monocular, dual 2.5x / 5x", false, D(1943, 11, 1)),
        new("de_tzf9d", "Germany", SightEra.LateWar, "TZF 9d", new[] { 3f, 6f },
            "winkel", ReticleInk.Black, "Tiger II", "Commonly cited dual 3x / 6x", true, D(1944, 1, 1)),
        new("de_tzf1a", "Germany", SightEra.ColdWar, "TZF 1A", new[] { 8f },
            "nato", ReticleInk.Red, "Leopard 1 (early)", "Period-plausible power", true, D(1965, 1, 1)),
        new("de_emes15", "Germany", SightEra.ColdWar, "EMES 15", new[] { 4f, 12f },
            "nato", ReticleInk.Red, "Leopard 2", "Commonly cited 4x / 12x", true, D(1979, 10, 25)),

        // ----------------------------------------------------------- Soviet Union
        new("su_top", "Soviet Union", SightEra.EarlyWar, "TOP", new[] { 2.5f },
            "chevron", ReticleInk.Black, "T-26, BT-7", "Period-plausible power", true, D(1933, 1, 1)),
        new("su_tod6", "Soviet Union", SightEra.EarlyWar, "TOD-6", new[] { 2.5f },
            "chevron", ReticleInk.Black, "T-34 (1940-41), KV-1",
            "TOD family: 2.5x, 26 deg field", true, D(1940, 1, 1)),
        new("su_tmfd7", "Soviet Union", SightEra.MidWar, "TMFD-7", new[] { 2.5f },
            "chevron", ReticleInk.Black, "T-34-76 (1942-43), KV-1S, early T-34-85",
            "2.5x; field 15 or 26 deg, sources conflict", false, D(1942, 1, 1)),
        new("su_tsh16", "Soviet Union", SightEra.LateWar, "TSh-16", new[] { 4f },
            "chevron", ReticleInk.Black, "T-34-85, SU-100", "4x, 16 deg field", false, D(1944, 1, 1)),
        new("su_tsh17", "Soviet Union", SightEra.LateWar, "TSh-17", new[] { 4f },
            "chevron", ReticleInk.Black, "IS-2, IS-3", "4x, 16 deg field", false, D(1944, 1, 1)),
        new("su_tsh222", "Soviet Union", SightEra.ColdWar, "TSh-2-22", new[] { 3.5f, 7f },
            "rangefinder", ReticleInk.Black, "T-54",
            "CIA T-54 handbook: switched 3.5x / 7x, 18 / 9 deg", false, D(1955, 1, 1)),
        new("su_tsh2b32", "Soviet Union", SightEra.ColdWar, "TSh-2B-32P", new[] { 3.5f, 7f },
            "rangefinder", ReticleInk.Black, "T-55", "Two powers 3.5x / 7x", false, D(1962, 1, 1)),
        new("su_tpdk1", "Soviet Union", SightEra.ColdWar, "TPD-K1", new[] { 8f },
            "rangefinder", ReticleInk.Red, "T-72, T-72A", "Period-plausible power", true, D(1975, 1, 1)),
        new("su_1g46", "Soviet Union", SightEra.ColdWar, "1G46", new[] { 2.7f, 6f, 12f },
            "rangefinder", ReticleInk.Red, "T-72B, T-80U, T-90",
            "Variable 2.7x-12x, sampled", true, D(1985, 1, 1)),

        // ---------------------------------------------------------- United States
        new("us_m1917", "United States", SightEra.Ww1, "M1917 37 mm telescope", new[] { 2f },
            "post", ReticleInk.Black, "M1917 light tank, Mark VIII Liberty",
            "Period-plausible WW1 power", true, D(1918, 1, 1)),
        new("us_m21a1", "United States", SightEra.EarlyWar, "M21A1", new[] { 3f },
            "ladder", ReticleInk.Black, "M3 Lee/Grant (75 mm)", "Period-plausible power", true, D(1941, 1, 1)),
        new("us_m38", "United States", SightEra.MidWar, "M4 periscope with M38", new[] { 1.44f },
            "ladder", ReticleInk.Black, "M4 Sherman, M10", "Commonly cited 1.44x", true, D(1942, 1, 1)),
        new("us_m70f", "United States", SightEra.MidWar, "M70F", new[] { 3f },
            "ladder", ReticleInk.Black, "M4 Sherman (75 mm)", "3x, 12 deg 19' field", false, D(1942, 1, 1)),
        new("us_m71d", "United States", SightEra.LateWar, "M71D", new[] { 5f },
            "ladder", ReticleInk.Black, "M26 Pershing, M36 Jackson", "5x, 13 deg field", false, D(1944, 1, 1)),
        new("us_m83", "United States", SightEra.ColdWar, "M83", new[] { 4f, 8f },
            "ladder", ReticleInk.Black, "M41 Walker Bulldog, M47 Patton",
            "Variable 4x-8x, 13 to 6.5 deg", false, D(1951, 1, 1)),
        new("us_m105d", "United States", SightEra.ColdWar, "M105D", new[] { 8f },
            "nato", ReticleInk.Black, "M48 Patton, M60 (secondary)", "Period-plausible power", true, D(1952, 1, 1)),
        new("us_gps", "United States", SightEra.ColdWar, "M1 GPS", new[] { 3f, 10f },
            "nato", ReticleInk.Red, "M1 Abrams", "Commonly cited 3x / 10x", true, D(1980, 1, 1)),

        // --------------------------------------------------------- United Kingdom
        new("uk_ts15", "United Kingdom", SightEra.Ww1, "Telescope Sight No.15 Mk I", new[] { 2f },
            "post", ReticleInk.Black, "Mark IV, Mark V (male)", "Surviving artefact: 2x", false, D(1917, 5, 1)),
        new("uk_no24b", "United Kingdom", SightEra.EarlyWar, "Telescope No.24B", new[] { 1.9f },
            "cross", ReticleInk.Black, "Matilda II, Valentine, Cruiser A13 (2-pdr)",
            "Period-plausible power", true, D(1939, 1, 1)),
        new("uk_no33", "United Kingdom", SightEra.MidWar, "Telescope No.33", new[] { 1.9f },
            "cross", ReticleInk.Black, "Churchill III-IV, Crusader III (6-pdr)",
            "Period-plausible power", true, D(1942, 1, 1)),
        new("uk_no51", "United Kingdom", SightEra.LateWar, "Telescope No.51", new[] { 3f },
            "cross", ReticleInk.Black, "Sherman Firefly, Challenger A30, Achilles (17-pdr)",
            "Collector sources: 3x, 13 deg field", false, D(1944, 1, 1)),
        new("uk_centurion", "United Kingdom", SightEra.ColdWar, "Centurion gunner's telescope", new[] { 6f },
            "cross", ReticleInk.Black, "Centurion Mk 3-13", "Period-plausible power", true, D(1948, 1, 1)),
        new("uk_chieftain", "United Kingdom", SightEra.ColdWar, "Chieftain gunner's periscope", new[] { 1f, 10f },
            "nato", ReticleInk.Red, "Chieftain, Challenger 1", "Period-plausible 1x / 10x", true, D(1966, 1, 1)),
        new("uk_swingfire", "United Kingdom", SightEra.ColdWar, "Swingfire monocular sight", new[] { 1f, 10f },
            "atgm", ReticleInk.Black, "FV438 Swingfire, FV102 Striker, Ferret Mk 5",
            "Hensoldt 1x / 10x monocular periscope", false, D(1969, 1, 1)),
    };

    internal static SightVariant? Find(string? id) =>
        id == null ? null : All.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.Ordinal));

    internal static SightVariant[] ForCountry(string country) =>
        All.Where(v => v.Country == country).OrderBy(v => v.Era).ThenBy(v => v.AvailableFrom).ToArray();

    internal static string EraName(SightEra era) => era switch
    {
        SightEra.Ww1 => "WW1",
        SightEra.EarlyWar => "early WW2",
        SightEra.MidWar => "mid-war",
        SightEra.LateWar => "late war",
        _ => "Cold War",
    };

    internal static string Powers(SightVariant v) =>
        string.Join(" / ", v.Magnifications.Select(m => m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x"));

    // Era from the vehicle's actual date. 1945.09.03 is the native Coldwar start;
    // 1946 is no longer used as a proxy.
    internal static SightEra EraForDate(DateTime date) =>
        date < D(1919, 1, 1) ? SightEra.Ww1 :
        date < D(1942, 1, 1) ? SightEra.EarlyWar :
        date < D(1944, 1, 1) ? SightEra.MidWar :
        date < D(1945, 9, 3) ? SightEra.LateWar : SightEra.ColdWar;

    internal static SightEra EraForYear(int year) => EraForDate(D(year, 1, 1));

    /// <summary>Default sight for a part without a saved choice, usable on its date.</summary>
    internal static SightVariant Default(string? partGuid, DateTime? date)
    {
        var preferred = Preferred(partGuid, date);
        if (preferred != null && SightDatePolicy.Allows(date, preferred)) return preferred;
        if (date is { } d)
        {
            var available = All.Where(v => SightDatePolicy.Allows(d, v))
                .OrderByDescending(v => v.AvailableFrom).ThenBy(v => v.Id).ToArray();
            if (available.Length > 0) return available[0];
        }
        return preferred ?? Find("de_tzf12a")!;
    }

    private static SightVariant? Preferred(string? partGuid, DateTime? date)
    {
        if (string.Equals(partGuid, AtgmGuidanceSightGuid, StringComparison.OrdinalIgnoreCase)) return Find("uk_swingfire");
        if (string.Equals(partGuid, Ww1SightGuid, StringComparison.OrdinalIgnoreCase)) return Find("uk_ts15");
        if (string.Equals(partGuid, Ww2SightGuid, StringComparison.OrdinalIgnoreCase)) return Find("de_tzf12a");
        var era = date is { } d ? EraForDate(d) : SightEra.LateWar;
        return era switch
        {
            SightEra.Ww1 => Find("uk_ts15"),
            SightEra.EarlyWar => Find("de_tzf5a"),
            SightEra.MidWar => Find("us_m70f"),
            SightEra.LateWar => Find("de_tzf12a"),
            _ => Find("us_m105d"),
        };
    }

    // Parts from Sprocket Expanded Ballistics; matched by GUID only, no dependency.
    internal const string AtgmGuidanceSightGuid = "cd1dfaf1-4ce6-4d8d-a1b5-fc0d09f35001";
    internal const string Ww1SightGuid = "4f6f0d8e-2b8a-4e1f-9c3a-1d2e3f4a5b60";
    internal const string Ww2SightGuid = "9a7c1b2d-3e4f-4a5b-8c6d-7e8f9a0b1c2d";

    /// <summary>
    /// Vertical field of view for a magnification relative to an unmagnified
    /// reference view: tan(fov/2) = tan(reference/2) / magnification.
    /// </summary>
    internal static float FieldOfView(float referenceDegrees, float magnification)
    {
        var half = referenceDegrees * MathF.PI / 360f;
        return 2f * MathF.Atan(MathF.Tan(half) / Math.Max(magnification, .1f)) * 180f / MathF.PI;
    }
}
