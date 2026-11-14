using System.Numerics;

namespace SprocketExpandedBallistics;

// Finite launch-to-sight gathering for a dedicated, independent guidance sight.
// This only rotates a missile from an elevated tube onto the sight ray; it does
// not move the missile, change its commanded speed or bypass normal SACLOS.
internal sealed class AtgmLaunchCapture
{
    internal const double ClearanceSeconds = .15;
    internal const double LifetimeSeconds = 5;
    internal const double TurnDegreesPerSecond = 90;
    internal const double LateralAcceleration = 250;

    internal const double ArrivalDegrees = 3;
    internal const double MaximumAcceptedSpeed = 10000;
    internal const double MaximumStepSeconds = .1;

    private const double MinimumLaunchElevation = .05;
    private const double MinimumCaptureDegrees = 15;
    private const double MaximumCaptureDegrees = 100;
    private const double AngleEpsilonDegrees = 1e-4;
    private const double MinimumLengthSquared = 1e-12;

    private readonly bool _hasHorizontalLaunch;
    private readonly Vector3 _launchHorizontal;
    private bool _enabled;
    private bool _finished;
    private string _status = "inactive";

    internal AtgmLaunchCapture(Vector3 launchHeading, Vector3 captureDirection)
    {
        if (!TryNormalize(launchHeading, out var launch) ||
            !TryNormalize(captureDirection, out var capture))
        {
            _status = "invalid";
            _finished = true;
            return;
        }

        var dot = Math.Clamp((double)Vector3.Dot(launch, capture), -1, 1);
        var angleDegrees = Math.Acos(dot) * 180 / Math.PI;
        if (!double.IsFinite(angleDegrees) ||
            angleDegrees <= MinimumCaptureDegrees + AngleEpsilonDegrees ||
            angleDegrees > MaximumCaptureDegrees + AngleEpsilonDegrees ||
            launch.Y <= MinimumLaunchElevation)
        {
            _status = "invalid";
            _finished = true;
            return;
        }

        var launchHorizontal = new Vector3(launch.X, 0, launch.Z);
        var hasHorizontalLaunch = launchHorizontal.LengthSquared() > MinimumLengthSquared;
        // A horizontal sight behind the tube cannot be gathered onto without a
        // turnaround. A truly vertical launch has no rear horizontal hemisphere,
        // so it is allowed to gather in any horizontal direction.
        var captureHorizontalLengthSquared = capture.X * capture.X + capture.Z * capture.Z;
        if (hasHorizontalLaunch && captureHorizontalLengthSquared > MinimumLengthSquared &&
            Behind(launch.X, launch.Z, capture.X, capture.Z))
        {
            _status = "invalid";
            _finished = true;
            return;
        }

        _launchHorizontal = launchHorizontal;
        _hasHorizontalLaunch = hasHorizontalLaunch;
        _enabled = true;
        _finished = false;
        _status = "capturing";
    }

    // Distance along the sight ray (from its origin) of the point gathering steers
    // at. Ahead of the missile by half a second of flight, and at least three times
    // its offset from the ray, so a wide (Swingfire-style) swing approaches the ray
    // at a shallow angle instead of chasing a point that slides behind it.
    internal static float GatherLead(float along, double speed, float offRay) =>
        Math.Max(80, along + Math.Max((float)speed * .5f, offRay * 3));

    internal bool Enabled => _enabled;
    internal bool Finished => _finished;
    internal string Status => _status;

    // currentHeading and speed are the caller's actual missile state. freshAim is
    // the ray-gather target computed by the caller; null/invalid means no command.
    internal Vector3 Step(Vector3 currentHeading, Vector3? freshAimDirection, double age,
        double speed, double dt, bool nearSightLine = true)
    {
        var hasHeading = TryNormalize(currentHeading, out var current);
        var hasSpeed = TrySpeed(speed, out var actualSpeed);

        // Once terminal, stay terminal. Returning the current (validated) direction
        // at the commanded speed keeps the caller's straight-flight contract.
        if (_finished)
            return hasHeading && hasSpeed ? current * (float)actualSpeed : Vector3.Zero;

        if (!double.IsFinite(age) || age < 0)
        {
            Finish("terminated");
            return hasHeading && hasSpeed ? current * (float)actualSpeed : Vector3.Zero;
        }
        if (age >= LifetimeSeconds)
        {
            Finish("expired");
            return hasHeading && hasSpeed ? current * (float)actualSpeed : Vector3.Zero;
        }

        // A malformed heading or speed has no safe finite direction to return.
        if (!hasHeading || !hasSpeed) return Vector3.Zero;

        // The tube is cleared absolutely at .15s. No steering or capture completion
        // is allowed before that; the caller still gets its exact commanded speed.
        if (age < ClearanceSeconds)
        {
            _status = "clearance";
            return current * (float)actualSpeed;
        }
        if (_status == "clearance") _status = "capturing";

        // Missing or malformed aim is a command dropout, not a reason to invent a
        // target. Age still runs, so the finite capture window closes normally.
        if (freshAimDirection is not { } freshAim || !TryNormalize(freshAim, out var target))
            return current * (float)actualSpeed;

        // Never chase a command behind the original horizontal launch direction.
        // This is the one turnaround guard retained by this primitive; normal
        // AtgmGuidance.Step keeps its own no-U-turn guard untouched.
        if (IsRearOfLaunch(target))
        {
            Finish("rejected");
            return current * (float)actualSpeed;
        }

        var currentDotTarget = Math.Clamp(Vector3.Dot(current, target), -1f, 1f);
        if (currentDotTarget <= -.999999f)
        {
            // The turn axis is undefined when heading and command are anti-parallel.
            // End safely instead of constructing a NaN/zero quaternion.
            Finish("terminated");
            return current * (float)actualSpeed;
        }

        // Arrival needs both alignment and the caller's near-ray confirmation.
        // Alignment alone while off the sight ray stays in gathering.
        if (nearSightLine && AngleDegrees(current, target) <= ArrivalDegrees + AngleEpsilonDegrees)
        {
            Finish("captured");
            return current * (float)actualSpeed;
        }

        // A zero, negative, oversized or non-finite timestep never moves the
        // heading and is never silently clamped upward.
        if (!double.IsFinite(dt) || dt <= 0 || dt > MaximumStepSeconds)
            return current * (float)actualSpeed;

        var angularRate = Math.Min(TurnDegreesPerSecond * Math.PI / 180,
            LateralAcceleration / actualSpeed);
        var allowed = angularRate * dt;
        if (!double.IsFinite(allowed) || allowed <= 0)
            return current * (float)actualSpeed;

        var angle = AngleRadians(current, target);
        if (angle <= allowed) current = target;
        else if (!TryTurn(current, target, allowed, out var turned))
        {
            Finish("terminated");
            return current * (float)actualSpeed;
        }
        else current = turned;

        if (nearSightLine && AngleDegrees(current, target) <= ArrivalDegrees + AngleEpsilonDegrees)
            Finish("captured");

        return current * (float)actualSpeed;
    }

    private void Finish(string status)
    {
        _finished = true;
        _enabled = false;
        _status = status;
    }

    private bool IsRearOfLaunch(Vector3 target)
    {
        if (!_hasHorizontalLaunch) return false;
        var targetX = target.X;
        var targetZ = target.Z;
        if (targetX * targetX + targetZ * targetZ < MinimumLengthSquared) return false;
        return Behind(_launchHorizontal.X, _launchHorizontal.Z, targetX, targetZ);
    }

    // Horizontal angle beyond which a command counts as behind the launch. A
    // Swingfire-style box gathers onto a sight ~90 degrees to the side; the
    // missile drifts forward first, so the ray ends up slightly behind it.
    // 110 degrees still rejects turnarounds.
    internal const double MaximumSwingDegrees = 110;
    private static readonly double BehindCosine = Math.Cos(MaximumSwingDegrees * Math.PI / 180);
    private static bool Behind(double ax, double az, double bx, double bz)
    {
        var lengths = Math.Sqrt((ax * ax + az * az) * (bx * bx + bz * bz));
        return lengths > MinimumLengthSquared && (ax * bx + az * bz) / lengths < BehindCosine;
    }

    private static bool TryTurn(Vector3 from, Vector3 to, double maxRadians, out Vector3 result)
    {
        result = from;
        var cross = Vector3.Cross(from, to);
        var axisLength = cross.Length();
        if (!float.IsFinite(axisLength) || axisLength < 1e-8f) return false;

        var axis = cross / axisLength;
        if (!Finite(axis) || axis.LengthSquared() < MinimumLengthSquared) return false;

        var quaternion = Quaternion.CreateFromAxisAngle(axis, (float)maxRadians);
        if (!float.IsFinite(quaternion.X) || !float.IsFinite(quaternion.Y) ||
            !float.IsFinite(quaternion.Z) || !float.IsFinite(quaternion.W))
            return false;

        var turned = Vector3.Normalize(Vector3.Transform(from, quaternion));
        if (!TryNormalize(turned, out var normalized)) return false;
        result = normalized;
        return true;
    }

    private static bool TryNormalize(Vector3 value, out Vector3 normalized)
    {
        normalized = default;
        if (!Finite(value)) return false;

        double x = value.X, y = value.Y, z = value.Z;
        var lengthSquared = x * x + y * y + z * z;
        if (!double.IsFinite(lengthSquared) || lengthSquared < MinimumLengthSquared) return false;

        var length = Math.Sqrt(lengthSquared);
        if (!double.IsFinite(length) || length < 1e-6) return false;

        normalized = new Vector3((float)(x / length), (float)(y / length), (float)(z / length));
        return Finite(normalized) && normalized.LengthSquared() > MinimumLengthSquared;
    }

    private static bool TrySpeed(double speed, out double acceptedSpeed)
    {
        if (!double.IsFinite(speed) || speed <= 0)
        {
            acceptedSpeed = 0;
            return false;
        }
        acceptedSpeed = Math.Min(speed, MaximumAcceptedSpeed);
        return true;
    }

    private static double AngleRadians(Vector3 from, Vector3 to)
    {
        var dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        return Math.Atan2(Vector3.Cross(from, to).Length(), dot);
    }

    private static double AngleDegrees(Vector3 from, Vector3 to) => AngleRadians(from, to) * 180 / Math.PI;

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
