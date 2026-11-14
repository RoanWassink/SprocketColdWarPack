using System;
using Sprocket.TechTrees;
using Sprocket.Vehicles;

namespace SprocketExpandedBallistics;

// Native adapter for the pure VehicleEraAvailability policy. Production callers
// pass the owning vehicle's actual TechDate; tests link only the pure policy.
internal static class VehicleEraAccess
{
    internal static bool AllowsMaterial(TechDate design, DateTime? minimumDate,
        bool hasTechnologyFrame, bool materialInFrame)
        => Allows(design, minimumDate, hasTechnologyFrame, materialInFrame, placedPart: false);

    internal static bool AllowsPlacedPart(TechDate design, DateTime? minimumDate,
        bool hasTechnologyFrame, bool materialInFrame)
        => Allows(design, minimumDate, hasTechnologyFrame, materialInFrame, placedPart: true);

    private static bool Allows(TechDate design, DateTime? minimumDate,
        bool hasTechnologyFrame, bool materialInFrame, bool placedPart)
    {
        if (!VehicleEraAvailability.TryCalendarDate(design.Year, design.Month, design.Day, out var date)) return false;
        if (!TryTimeline(out var timeline)) return false;
        return placedPart
            ? VehicleEraAvailability.AllowsPlacedPart(date, timeline, minimumDate, hasTechnologyFrame, materialInFrame)
            : VehicleEraAvailability.AllowsMaterial(date, timeline, minimumDate, hasTechnologyFrame, materialInFrame);
    }

    // Reads the actual native era timeline. A null/empty timeline, missing name,
    // invalid start, or timeline StartDate MaxValue makes the context unusable.
    private static bool TryTimeline(out VehicleEraBoundary[] timeline)
    {
        timeline = Array.Empty<VehicleEraBoundary>();
        try
        {
            var eras = VehicleClassifications.eras;
            if (eras == null || eras.Length == 0) return false;
            var candidate = new VehicleEraBoundary[eras.Length];
            for (var i = 0; i < eras.Length; i++)
            {
                var era = eras[i];
                if (era == null || string.IsNullOrWhiteSpace(era.Name)) return false;
                var start = era.StartDate;
                if (!VehicleEraAvailability.TryCalendarDate(start.Year, start.Month, start.Day, out var date)) return false;
                candidate[i] = new(era.Name.Trim(), date);
            }
            timeline = candidate;
            return true;
        }
        catch { return false; }
    }
}
