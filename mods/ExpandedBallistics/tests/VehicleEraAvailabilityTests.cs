using System;
using System.Collections.Generic;

namespace SprocketExpandedBallistics.Tests;

// Framework-free production-policy fixture linked into the main test project.
internal static class VehicleEraAvailabilityTests
{
    private static readonly VehicleEraBoundary[] Timeline =
    {
        new("WWI", new DateTime(1914, 7, 28)),
        new("Interwar", new DateTime(1918, 11, 12)),
        new("Earlywar", new DateTime(1939, 9, 2)),
        new("Midwar", new DateTime(1942, 1, 1)),
        new("Latewar", new DateTime(1944, 1, 1)),
        new("Coldwar", new DateTime(1945, 9, 3)),   // the game's own one-day era, not playable
        new("Coldwar1", new DateTime(1945, 9, 4)),
        new("Coldwar2", new DateTime(1960, 1, 1)),
        new("Coldwar3", new DateTime(1975, 1, 1))
    };

    // Renamed/custom era names are valid because the contract is carried by
    // ordered finite starts, not by the literal native era name.
    private static readonly VehicleEraBoundary[] RenamedTimeline =
    {
        new("Start", new DateTime(1914, 7, 28)),
        new("Middle", new DateTime(1939, 9, 1)),
        new("Late", new DateTime(1945, 9, 3)),
        new("Now", new DateTime(1975, 1, 1))
    };

    internal static IReadOnlyList<string> Run()
    {
        var failures = new List<string>();
        var modern = new DateTime(1945, 9, 3);
        var coldwar3 = new DateTime(1975, 1, 1);
        var duplet = new DateTime(2009, 1, 1);

        // Native technology membership remains a separate gate on both routes.
        Check(failures, "WW1 material route", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1916, 1, 1), Timeline, modern, true, true));
        Check(failures, "WW1 placed-part route", false,
            VehicleEraAvailability.AllowsPlacedPart(new DateTime(1916, 1, 1), Timeline, modern, true, true));
        Check(failures, "no technology frame", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1960, 1, 1), Timeline, modern, false, true));
        Check(failures, "recipe not in frame", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1960, 1, 1), Timeline, modern, true, false));

        // Day before / at / after the real threshold.
        Check(failures, "one day before 1945.09.03", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1945, 9, 2), Timeline, modern, true, true));
        Check(failures, "on 1945.09.03", true,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1945, 9, 3), Timeline, modern, true, true));
        Check(failures, "one day after 1945.09.03", true,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1945, 9, 4), Timeline, modern, true, true));

        // Variant B of the era split: the not playable Coldwar (1945.09.03) and Coldwar1 (1945.09.04) are strictly ascending, so the
        // timeline stays valid on both days; two eras with the same start would fail closed (that is why Coldwar1 starts a day later).
        Check(failures, "Coldwar1 first day with the one-day Coldwar before it", true,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1945, 9, 4), Timeline, modern, true, true));
        Check(failures, "Coldwar on its only day", true,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1945, 9, 3), Timeline, modern, true, true));
        Check(failures, "two eras with the same start fail closed", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1960, 1, 1),
                new[] { new VehicleEraBoundary("Coldwar", new DateTime(1945, 9, 3)), new VehicleEraBoundary("Coldwar1", new DateTime(1945, 9, 3)) },
                modern, true, true));

        // Coldwar2 on 1974.12.31 must not use a part introduced in 1975.
        Check(failures, "Coldwar2 day before 1975", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1974, 12, 31), Timeline, coldwar3, true, true));
        Check(failures, "Coldwar3 start 1975", true,
            VehicleEraAvailability.AllowsMaterial(coldwar3, Timeline, coldwar3, true, true));

        // The final-era sentinel means everything is available, including 2009.
        Check(failures, "sentinel uses Duplet 2009", true,
            VehicleEraAvailability.AllowsMaterial(DateTime.MaxValue, Timeline, duplet, true, true));
        Check(failures, "sentinel placed part uses Duplet 2009", true,
            VehicleEraAvailability.AllowsPlacedPart(DateTime.MaxValue, Timeline, duplet, true, true));
        Check(failures, "finite 2008 cannot use Duplet 2009", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(2008, 12, 31), Timeline, duplet, true, true));

        // Placed parts carry their own native Technology date; null adds no floor.
        Check(failures, "placed part without extra date", true,
            VehicleEraAvailability.AllowsPlacedPart(new DateTime(1960, 1, 1), Timeline, null, true, true));

        // Unknown/malformed context fails closed.
        Check(failures, "unknown vehicle context", false,
            VehicleEraAvailability.AllowsMaterial(null, Timeline, modern, true, true));
        Check(failures, "empty timeline", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1960, 1, 1), Array.Empty<VehicleEraBoundary>(), modern, true, true));
        Check(failures, "unordered timeline", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1960, 1, 1),
                new[] { new VehicleEraBoundary("ColdWar", new DateTime(1960, 1, 1)), new VehicleEraBoundary("Earlier", new DateTime(1950, 1, 1)) },
                modern, true, true));
        Check(failures, "missing era name", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1960, 1, 1),
                new[] { new VehicleEraBoundary("", new DateTime(1900, 1, 1)), new VehicleEraBoundary("ColdWar", new DateTime(1945, 9, 3)) },
                modern, true, true));

        // Renamed/custom era names support modern and future finite dates.
        Check(failures, "renamed eras modern material", true,
            VehicleEraAvailability.AllowsMaterial(new DateTime(2000, 1, 1), RenamedTimeline, modern, true, true));
        Check(failures, "renamed eras modern part", true,
            VehicleEraAvailability.AllowsPlacedPart(new DateTime(2000, 1, 1), RenamedTimeline, modern, true, true));
        Check(failures, "renamed eras prewar", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(1945, 9, 2), RenamedTimeline, modern, true, true));
        Check(failures, "renamed eras future finite", true,
            VehicleEraAvailability.AllowsMaterial(new DateTime(2100, 1, 1), RenamedTimeline, modern, true, true));

        // A MaxValue timeline start is malformed and still fails closed.
        Check(failures, "future era sentinel", false,
            VehicleEraAvailability.AllowsMaterial(new DateTime(2000, 1, 1),
                new[] { new VehicleEraBoundary("ColdWar", new DateTime(1945, 9, 3)), new VehicleEraBoundary("Open", DateTime.MaxValue) },
                modern, true, true));

        // Native calendar validation remains available for diagnostics.
        Check(failures, "valid year-zero calendar", true,
            VehicleEraAvailability.TryCalendarDate(0, 2, 29, out _));
        Check(failures, "invalid calendar", false,
            VehicleEraAvailability.TryCalendarDate(2025, 2, 29, out _));

        return failures;
    }

    private static void Check(List<string> failures, string name, bool expected, bool actual)
    {
        if (actual != expected) failures.Add($"{name}: expected {expected}, got {actual}");
    }
}
