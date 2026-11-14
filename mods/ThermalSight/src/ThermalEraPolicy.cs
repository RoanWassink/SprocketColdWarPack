namespace SprocketThermalSight;

public static class ThermalEraPolicy
{
    // The caller supplies the current native part invention date. No mod-owned epoch.
    public static bool Allows(DateTime? vehicleDate, ReadOnlySpan<DateTime> eraStarts, DateTime floor, DateTime? profileDate = null)
    {
        if (vehicleDate == null || eraStarts.Length == 0 || eraStarts[^1].Date == DateTime.MaxValue.Date) return false;
        for (int i = 1; i < eraStarts.Length; i++)
            if (eraStarts[i] <= eraStarts[i - 1]) return false;
        if (vehicleDate.Value.Date == DateTime.MaxValue.Date)
            return true; // Native final-era selection is open-ended, including later dated parts.
        if (vehicleDate.Value.Date < floor.Date || (profileDate.HasValue && vehicleDate.Value.Date < profileDate.Value.Date) || vehicleDate.Value.Date < eraStarts[0].Date) return false;
        // No invented technology horizon. Any valid finite date in a known
        // native timeline from the policy floor onwards is eligible.
        return true;
    }
}


public static class ProfileSelectionPolicy
{
    // A saved identity is resolved without applying the date gate.
    public static ThermalProfile? Default(IEnumerable<ThermalProfile> profiles, string defaultId, Func<ThermalProfile, bool> allowed)
    {
        var valid = profiles.Where(allowed).ToArray();
        return valid.FirstOrDefault(p => p.ComponentId == defaultId)
            ?? valid.OrderByDescending(p => p.AvailableDate).ThenBy(p => p.ComponentId, StringComparer.Ordinal).FirstOrDefault();
    }
}
