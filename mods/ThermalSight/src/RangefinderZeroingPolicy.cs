namespace SprocketThermalSight;

// Local record of the distance this mod last committed through the native
// ScopeController.SetGunConvergance path. ScopeController owns the authoritative
// retained value; this record only detects identity changes and manual input.
public readonly record struct RangefinderZeroRecord(
    long Scope, long Sight, long Cannon, double Range, int Revision);

// Pure policy for the automatic-laser native distance commit. No native writes
// happen here. Rounding is only the range value; the caller commits it through
// ScopeController.SetGunConvergance, whose native field is retained on the
// controller and used by SetAimPoint.
public static class RangefinderZeroingPolicy
{
    public const double MinimumRange = 50;
    public const double MaximumRange = 4000;
    public const double Step = 10;
    public const double ManualTolerance = .51;

    // Preserve the existing automatic-laser setting maths: nearest 10 m, ties
    // away from zero, then clamp to the native sight limits supplied by caller.
    public static double? Setting(double distance, double minimum, double maximum)
    {
        if (!double.IsFinite(distance) || !double.IsFinite(minimum) || !double.IsFinite(maximum)
            || minimum <= 0 || maximum < minimum) return null;
        var rounded = Math.Round(distance / Step, MidpointRounding.AwayFromZero) * Step;
        return Math.Clamp(rounded, minimum, maximum);
    }

    public static bool ValidIdentity(long scope, long sight, long cannon)
        => scope != 0 && sight != 0 && cannon != 0;

    // Only the active, owned automatic-laser path with a linked, healthy,
    // non-guided cannon may write the retained native distance.
    public static bool CanSet(bool automaticLaser, bool owned, bool sightAssigned, bool cannonHealthy, bool guided)
        => automaticLaser && owned && sightAssigned && cannonHealthy && !guided;

    // Runtime eligibility for an already-retained zero. Readiness/configuration
    // failure and a recorded measurement failure suspend correction; pause or the
    // absence of fresh measurement input does not clear the retained distance.
    public static bool CanUse(bool ready, bool enabled, bool measurementFailed, bool automaticLaser,
        bool owned, bool sightAssigned, bool cannonHealthy, bool guided, bool nativeCurrent)
        => Refusal(ready, enabled, measurementFailed, automaticLaser,
            owned, sightAssigned, cannonHealthy, guided, nativeCurrent) == null;

    // The first failed CanUse condition, for diagnostics; null when usable.
    public static string? Refusal(bool ready, bool enabled, bool measurementFailed, bool automaticLaser,
        bool owned, bool sightAssigned, bool cannonHealthy, bool guided, bool nativeCurrent)
        => !ready ? "not-ready" : !enabled ? "disabled" : measurementFailed ? "measurement-failed"
           : !automaticLaser ? "device-not-automatic" : !owned ? "not-owned"
           : !sightAssigned ? "no-linked-cannon" : !cannonHealthy ? "cannon-unhealthy"
           : guided ? "guided" : !nativeCurrent ? "native-distance-changed" : null;

    // Commit the value actually read back from the native field. A different
    // scope/sight/cannon identity starts a new revision; remeasurement of the
    // same identity advances it so stale callbacks can be rejected.
    public static RangefinderZeroRecord? Commit(RangefinderZeroRecord? previous,
        long scope, long sight, long cannon, double? nativeReadBack)
    {
        if (!ValidIdentity(scope, sight, cannon)
            || nativeReadBack is not { } range || !double.IsFinite(range)) return null;
        int revision = previous is { } old
            && old.Scope == scope && old.Sight == sight && old.Cannon == cannon
                ? old.Revision + 1 : 1;
        return new RangefinderZeroRecord(scope, sight, cannon, range, revision);
    }

    // The native field is shared by the ScopeController. A changed value is
    // only attributable to manual input when the scope/sight/cannon identity is
    // unchanged; a different identity is unobservable and must not be guessed.
    public static bool ManualChanged(RangefinderZeroRecord? record,
        long scope, long sight, long cannon, double nativeReadBack)
    {
        if (record is not { } value || !double.IsFinite(nativeReadBack)) return false;
        if (value.Scope != scope || value.Sight != sight || value.Cannon != cannon) return false;
        return !Matches(value.Range, nativeReadBack);
    }

    // A retained automatic value is current only while the identity and the
    // native read-back still agree. This never reasserts the value.
    public static bool IsCurrent(RangefinderZeroRecord? record,
        long scope, long sight, long cannon, double nativeReadBack)
    {
        if (record is not { } value || !double.IsFinite(nativeReadBack)) return false;
        return value.Scope == scope && value.Sight == sight && value.Cannon == cannon
            && Matches(value.Range, nativeReadBack);
    }

    public static bool Matches(double expected, double observed, double tolerance = ManualTolerance)
        => double.IsFinite(expected) && double.IsFinite(observed)
           && Math.Abs(expected - observed) <= Math.Max(0, tolerance);

    // The requested target is stationary in world space; in the launch frame its
    // relative velocity is the negative of the platform's inherited velocity.
    // No target velocity or aim-lock input is sampled here.
    public static bool TryStationaryTargetVelocity(double inheritedX, double inheritedY, double inheritedZ,
        out double targetVelocityX, out double targetVelocityY, out double targetVelocityZ)
    {
        targetVelocityX = targetVelocityY = targetVelocityZ = 0;
        if (!double.IsFinite(inheritedX) || !double.IsFinite(inheritedY) || !double.IsFinite(inheritedZ)) return false;
        targetVelocityX = -inheritedX;
        targetVelocityY = -inheritedY;
        targetVelocityZ = -inheritedZ;
        return true;
    }
}
