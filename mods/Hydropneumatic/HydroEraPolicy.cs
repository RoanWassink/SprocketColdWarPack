namespace SprocketHydropneumatic;

public static class HydroEraPolicy
{
    public static readonly DateTime MinimumDate = new(1967, 1, 1);
    // Native finite intervals are [start, next start). The exact MaxValue value
    // is the final-era selection sentinel: it makes every dated part available,
    // so it is never replaced by the last finite era start.
    public static bool Allows(DateTime? vehicleDate, ReadOnlySpan<DateTime> eraStarts)
    {
        if (vehicleDate == null || eraStarts.Length == 0 || eraStarts[^1].Date == DateTime.MaxValue.Date) return false;
        for (int i = 1; i < eraStarts.Length; i++)
            if (eraStarts[i] <= eraStarts[i - 1]) return false;
        if (vehicleDate.Value.Date == DateTime.MaxValue.Date)
            return true;
        if (vehicleDate.Value.Date < MinimumDate || vehicleDate.Value.Date < eraStarts[0].Date) return false;
        // No invented technology horizon. Any valid finite date in a known
        // native timeline from the policy floor onwards is eligible.
        return true;
    }
}
