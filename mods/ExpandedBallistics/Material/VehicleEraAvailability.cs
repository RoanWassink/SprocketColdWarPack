using System;
using System.Collections.Generic;

namespace SprocketExpandedBallistics;

// One boundary of the vehicle era timeline. Pure data only: this file is linked
// into the coordinator's main test suite and must not reference native assemblies.
public readonly record struct VehicleEraBoundary(string Name, DateTime Start);

// Pure date policy shared by face materials and placed parts. Native TechDate
// and VehicleClassifications reads live in VehicleEraAccess.
public static class VehicleEraAvailability
{
    public static bool TryCalendarDate(int year, int month, int day, out DateTime date)
    {
        date = default;
        if (year is < 0 or > 9999 || month is < 1 or > 12 || day < 1) return false;
        // Native TechDate supports year zero. Validate it in the same leap-year
        // cycle as year 400, then return that mapped calendar value.
        var calendarYear = year == 0 ? 400 : year;
        if (day > DateTime.DaysInMonth(calendarYear, month)) return false;
        date = new DateTime(calendarYear, month, day);
        return true;
    }

    // Shared production policy. The two public route names are deliberately thin:
    // face materials and placed parts must never diverge in their date contract.
    public static bool AllowsMaterial(DateTime? designDate, IReadOnlyList<VehicleEraBoundary> timeline,
        DateTime? minimumDate, bool hasTechnologyFrame, bool materialInFrame)
        => Allows(designDate, timeline, minimumDate, hasTechnologyFrame, materialInFrame);

    public static bool AllowsPlacedPart(DateTime? designDate, IReadOnlyList<VehicleEraBoundary> timeline,
        DateTime? minimumDate, bool hasTechnologyFrame, bool materialInFrame)
        => Allows(designDate, timeline, minimumDate, hasTechnologyFrame, materialInFrame);

    private static bool Allows(DateTime? designDate, IReadOnlyList<VehicleEraBoundary> timeline,
        DateTime? minimumDate, bool hasTechnologyFrame, bool materialInFrame)
    {
        // Unknown context, absent technology frame, or a recipe not present in that
        // frame keeps the native/passive fallback rather than inventing availability.
        if (designDate is not { } date || !hasTechnologyFrame || !materialInFrame) return false;
        if (timeline == null || timeline.Count == 0) return false;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < timeline.Count; i++)
        {
            var boundary = timeline[i];
            if (string.IsNullOrWhiteSpace(boundary.Name) || !names.Add(boundary.Name.Trim())) return false;
            // StartDate MaxValue is a malformed timeline sentinel, not a finite era.
            if (boundary.Start.Date == DateTime.MaxValue.Date) return false;
            if (i > 0 && boundary.Start.Date <= timeline[i - 1].Start.Date) return false;
        }

        // A null minimumDate means the native Technology/Part date is the only
        // floor (used by the placed-part menu). Otherwise this is the item's real
        // availability date, never an era name.
        var floor = minimumDate?.Date ?? DateTime.MinValue;

        // Exact native DateTime.MaxValue is the final-era sentinel. In the last
        // era it means "everything is available", including 2003/2006/2009
        // technology, so it must not be compared with the final finite START.
        if (date.Date == DateTime.MaxValue.Date) return true;

        // Custom/renamed era names remain valid because ordering and dates, not
        // names, carry the contract. A finite future date is valid once it falls
        // in the known finite timeline at or after the declared minimum.
        if (date.Date < floor) return false;
        return date.Date >= timeline[0].Start.Date;
    }
}
