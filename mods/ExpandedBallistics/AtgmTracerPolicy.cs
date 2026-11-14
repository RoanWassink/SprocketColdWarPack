using System.Numerics;

namespace SprocketExpandedBallistics;

// Pure visual policy shared by the optional tracer and the managed checks.
internal readonly record struct AtgmTracerSettings(bool Enabled, double BaseSizeM, double MinSizeM,
    double MaxSizeM, double MaxDistanceM, double LifetimeSeconds);

internal readonly record struct AtgmTracerFrame(bool Visible, double SizeM);

internal static class AtgmTracerPolicy
{
    // Physical tail flare eligibility follows a live powered missile of a guided
    // type, not command ownership. Superseding another missile or losing the
    // control link must not extinguish the flare; the caller still removes it on
    // real impact/release/reset/reused identity and Evaluate enforces flare age.
    internal static bool FlareEligible(bool liveMissile, bool powered, string? guidanceMode) =>
        liveMissile && powered && !string.IsNullOrEmpty(guidanceMode) && guidanceMode != "none";

    // A rear flare grows with camera distance so it stays readable, but it is
    // clamped to a finite world size and refused beyond the configured range.
    internal static AtgmTracerFrame Evaluate(bool flightAlive, double age, double distanceToCamera,
        AtgmTracerSettings settings, double rearFactor = 1)
    {
        if (!settings.Enabled || !flightAlive ||
            !double.IsFinite(age) || !double.IsFinite(distanceToCamera) ||
            !double.IsFinite(settings.BaseSizeM) || !double.IsFinite(settings.MinSizeM) ||
            !double.IsFinite(settings.MaxSizeM) || !double.IsFinite(settings.MaxDistanceM) ||
            !double.IsFinite(settings.LifetimeSeconds) ||
            settings.MinSizeM <= 0 || settings.MaxSizeM < settings.MinSizeM ||
            settings.BaseSizeM <= 0 || settings.MaxDistanceM <= 0 || settings.LifetimeSeconds <= 0)
            return new(false, 0);
        if (age < 0 || age > settings.LifetimeSeconds || distanceToCamera < 0 || distanceToCamera > settings.MaxDistanceM)
            return new(false, 0);
        var growth = Math.Max(1, distanceToCamera / 250d);
        var size = Math.Clamp(settings.BaseSizeM * growth, settings.MinSizeM, settings.MaxSizeM);
        var rear = double.IsFinite(rearFactor) ? Math.Clamp(rearFactor, .45, 1) : .55;
        return new(true, Math.Clamp(size * rear, settings.MinSizeM, settings.MaxSizeM));
    }

    // Behind the missile reads as a full trailing flare; a head-on view is smaller.
    internal static double RearFactor(Vector3 cameraPosition, Vector3 missilePosition, Vector3 forward)
    {
        if (!Finite(cameraPosition) || !Finite(missilePosition) || !Finite(forward) || forward.LengthSquared() < 1e-8f)
            return .55;
        var toCamera = cameraPosition - missilePosition;
        if (toCamera.LengthSquared() < 1e-8f) return .55;
        var dot = Math.Clamp(Vector3.Dot(Vector3.Normalize(forward), Vector3.Normalize(toCamera)), -1, 1);
        return .55 + .45 * ((1 - dot) / 2);
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
