using System;
using System.Numerics;

namespace SprocketExpandedBallistics;

internal static class AtgmLaunchCaptureChecks
{
    private const double Degrees = Math.PI / 180;

    internal static void Run(Action<bool, string> check)
    {
        TubeClearance(check);
        PerStepTurnAndSpeed(check);
        HighSpeedLateralCap(check);
        Gathering(check);
        MissingAim(check);
        ExpiryAndNoRestart(check);
        RearCommandRejected(check);
        ConstructorBounds(check);
        MalformedState(check);
        OffSightAlignmentDoesNotFinish(check);
        StableSubdivision(check);
    }

    private static void TubeClearance(Action<bool, string> check)
    {
        var launch = Heading(30);
        var capture = Heading(30, 60);
        var hold = new AtgmLaunchCapture(launch, capture);
        check(hold.Enabled && !hold.Finished, "valid elevated sight capture is enabled");

        var before = hold.Step(launch, capture, .149, 278, .1);
        check(SameDirection(before, launch) && Near(before.Length(), 278, .01),
            "before .15s tube clearance keeps heading and commanded speed");
        check(!hold.Finished, "tube clearance does not complete capture");

        var after = hold.Step(launch, capture, .15, 278, .1);
        check(AngleDegrees(launch, after) > 1,
            "steering starts at the absolute .15s clearance boundary");
        check(Near(after.Length(), 278, .01), "clearance step preserves commanded speed");
    }

    private static void PerStepTurnAndSpeed(Action<bool, string> check)
    {
        var launch = Heading(30);
        var capture = Heading(30, 60);
        var turn = new AtgmLaunchCapture(launch, capture);

        var result = turn.Step(launch, capture, .2, 278, .02);
        var expected = Math.Min(90, 250d / 278 * 180 / Math.PI) * .02;
        var actual = AngleDegrees(launch, capture) - AngleDegrees(result, capture);
        check(Near(actual, expected, .02),
            "per-step turn combines 90deg/s and 250m/s2 lateral limits");
        check(Near(result.Length(), 278, .01), "per-step turn preserves actual speed");
        check(!turn.Finished, "ordinary gathering step does not finish early");
    }

    private static void HighSpeedLateralCap(Action<bool, string> check)
    {
        var launch = Heading(30);
        var capture = Heading(30, 60);
        var fast = new AtgmLaunchCapture(launch, capture);

        var result = fast.Step(launch, capture, .2, 1000, .1);
        var expected = Math.Min(90, 250d / 1000 * 180 / Math.PI) * .1;
        var actual = AngleDegrees(launch, capture) - AngleDegrees(result, capture);
        check(Near(actual, expected, .02),
            "high speed is limited by 250m/s2 lateral acceleration, not 90deg/s");
        check(Near(result.Length(), 1000, .01), "high-speed step preserves commanded speed");

        var capped = new AtgmLaunchCapture(launch, capture);
        var cappedResult = capped.Step(launch, capture, .2, 20000, .01);
        check(Near(cappedResult.Length(), 10000, .01),
            "accepted speed range is capped at 10000 for numeric safety");
    }

    private static void Gathering(Action<bool, string> check)
    {
        foreach (var initialAngle in new[] { 45d, 60d, 90d })
        {
            var launch = Heading(45);
            var capture = Heading(45 - initialAngle);
            var gather = new AtgmLaunchCapture(launch, capture);
            check(gather.Enabled, $"capture from {initialAngle:0}deg is enabled");

            var first = gather.Step(launch, capture, .2, 200, .02);
            var firstTurn = AngleDegrees(launch, first);
            check(firstTurn <= Math.Min(90, 250d / 200 * 180 / Math.PI) * .02 + .05,
                $"{initialAngle:0}deg gathering does not teleport");

            var heading = launch;
            var age = .2;
            var steps = 0;
            while (!gather.Finished && steps < 400)
            {
                var next = gather.Step(heading, capture, age, 200, .02);
                check(Finite(next), $"{initialAngle:0}deg gathering stays finite");
                heading = SafeNormalize(next, heading);
                age += .02;
                steps++;
            }

            check(gather.Finished && !gather.Enabled,
                $"{initialAngle:0}deg gathering completes within the finite window");
            check(AngleDegrees(heading, capture) <= 3.01,
                $"{initialAngle:0}deg gathering arrives inside 3deg");
        }
    }

    private static void MissingAim(Action<bool, string> check)
    {
        var launch = Heading(30);
        var capture = Heading(30, 60);
        var captureState = new AtgmLaunchCapture(launch, capture);

        var noAim = captureState.Step(launch, null, .2, 200, .02);
        check(SameDirection(noAim, launch), "missing aim makes no angular change");
        check(!captureState.Finished, "missing aim does not complete capture");

        var malformedAim = captureState.Step(launch, new Vector3(float.NaN, 0, 1), .2, 200, .02);
        check(SameDirection(malformedAim, launch), "malformed aim makes no angular change");
        check(!captureState.Finished, "malformed aim does not complete capture");

        captureState.Step(launch, null, 5, 200, .02);
        check(captureState.Finished && !captureState.Enabled,
            "capture expires even while aim is missing");
    }

    private static void ExpiryAndNoRestart(Action<bool, string> check)
    {
        var launch = Heading(30);
        var capture = Heading(30, 60);

        var expired = new AtgmLaunchCapture(launch, capture);
        var atExpiry = expired.Step(launch, capture, 5, 200, .02);
        check(expired.Finished && !expired.Enabled, "absolute lifetime ends at 5s");
        check(Finite(atExpiry), "expiry returns a finite straight command");
        var afterExpiry = expired.Step(launch, capture, .2, 200, .02);
        check(SameDirection(afterExpiry, launch) && expired.Finished,
            "expired capture never restarts or turns again");

        var negative = new AtgmLaunchCapture(launch, capture);
        negative.Step(launch, capture, -.01, 200, .02);
        check(negative.Finished && !negative.Enabled, "negative age terminates capture");

        var nonFinite = new AtgmLaunchCapture(launch, capture);
        var nonFiniteResult = nonFinite.Step(launch, capture, double.NaN, 200, .02);
        check(nonFinite.Finished && Finite(nonFiniteResult),
            "non-finite age terminates safely with a finite command");
    }

    private static void RearCommandRejected(Action<bool, string> check)
    {
        var launch = Heading(30);
        var capture = Heading(30, 60);
        var reject = new AtgmLaunchCapture(launch, capture);

        var rear = Heading(30, 120);
        var result = reject.Step(launch, rear, .2, 200, .02);
        check(reject.Finished && !reject.Enabled,
            "command behind the initial horizontal launch hemisphere ends capture");
        check(SameDirection(result, launch), "rear rejection does not turn the missile");
    }

    private static void ConstructorBounds(Action<bool, string> check)
    {
        var capture = Heading(30);

        var zero = new AtgmLaunchCapture(Vector3.Zero, capture);
        check(!zero.Enabled && zero.Finished && zero.Status == "invalid",
            "zero launch vector is inert and marked invalid");

        var nan = new AtgmLaunchCapture(new Vector3(float.NaN, 0, 1), capture);
        check(!nan.Enabled && nan.Finished, "non-finite launch vector is inert");

        var forward = new AtgmLaunchCapture(new Vector3(0, 0, 1), capture);
        check(!forward.Enabled && forward.Finished,
            "ordinary horizontal forward launch is inert");

        var inverted = new AtgmLaunchCapture(new Vector3(0, -1, 0), capture);
        check(!inverted.Enabled && inverted.Finished, "inverted launch is inert");

        var tooSmall = new AtgmLaunchCapture(Heading(30), Heading(30, 10));
        check(!tooSmall.Enabled && tooSmall.Finished, "initial angle at or below 15deg is inert");

        var tooLarge = new AtgmLaunchCapture(Heading(80), Heading(-30));
        check(!tooLarge.Enabled && tooLarge.Finished, "initial angle above 100deg is inert");

        var behind = new AtgmLaunchCapture(Heading(80), Heading(10, 120));
        check(!behind.Enabled && behind.Finished,
            "capture direction behind the horizontal launch projection is inert");

        var vertical = new AtgmLaunchCapture(Vector3.UnitY, Vector3.UnitZ);
        check(vertical.Enabled && !vertical.Finished,
            "exactly vertical launch permits horizontal gathering");
        var verticalStep = vertical.Step(Vector3.UnitY, Vector3.UnitZ, .2, 200, .02);
        check(AngleDegrees(Vector3.UnitY, verticalStep) > 1,
            "vertical launch gathers toward a horizontal sight");
    }

    private static void MalformedState(Action<bool, string> check)
    {
        var launch = Heading(30);
        var capture = Heading(30, 60);

        var current = new AtgmLaunchCapture(launch, capture);
        check(current.Step(new Vector3(float.NaN, 0, 0), capture, .2, 200, .02) == Vector3.Zero,
            "non-finite current heading returns finite zero");
        check(current.Step(Vector3.Zero, capture, .2, 200, .02) == Vector3.Zero,
            "zero current heading returns finite zero");

        foreach (var badSpeed in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            var speedCase = new AtgmLaunchCapture(launch, capture);
            var result = speedCase.Step(launch, capture, .2, badSpeed, .02);
            check(result == Vector3.Zero, "invalid speed returns finite zero");
        }

        var largeSpeed = new AtgmLaunchCapture(launch, capture);
        var largeResult = largeSpeed.Step(launch, capture, .2, 50000, .02);
        check(Near(largeResult.Length(), 10000, .01), "oversized speed is capped at 10000");

        foreach (var badDt in new[] { 0d, -.01, double.NaN, double.PositiveInfinity, .101 })
        {
            var dtCase = new AtgmLaunchCapture(launch, capture);
            var result = dtCase.Step(launch, capture, .2, 200, badDt);
            check(SameDirection(result, launch),
                $"invalid timestep {badDt} causes no angular movement");
            check(!dtCase.Finished, "invalid timestep does not complete an unaligned capture");
        }

        var antiParallel = new AtgmLaunchCapture(launch, capture);
        var backward = -capture;
        var antiResult = antiParallel.Step(backward, capture, .2, 200, .02);
        check(Finite(antiResult) && antiParallel.Finished && !antiParallel.Enabled,
            "anti-parallel heading terminates safely instead of producing NaN");
        check(Near(antiResult.Length(), 200, .01),
            "anti-parallel termination retains commanded speed");
    }

    private static void OffSightAlignmentDoesNotFinish(Action<bool, string> check)
    {
        var launch = Heading(30);
        var capture = Heading(30, 60);
        var offRay = new AtgmLaunchCapture(launch, capture);

        var aligned = offRay.Step(capture, capture, .2, 200, .02, false);
        check(!offRay.Finished && offRay.Enabled,
            "heading alignment while off the sight ray remains capturing");
        check(SameDirection(aligned, capture), "coincident heading and aim stay straight without NaN");

        var heading = launch;
        var age = .2;
        for (var i = 0; i < 120; i++)
        {
            heading = SafeNormalize(offRay.Step(heading, capture, age, 200, .02, false), heading);
            age += .02;
        }
        check(!offRay.Finished, "off-ray gathering never marks capture complete");
        check(AngleDegrees(heading, capture) <= 3.01, "off-ray gathering still aligns heading");

        var onRay = offRay.Step(heading, capture, age, 200, .02, true);
        check(offRay.Finished && !offRay.Enabled,
            "the same aligned heading completes once nearSightLine is true");
        check(Finite(onRay), "completion returns a finite command");
    }

    private static void StableSubdivision(Action<bool, string> check)
    {
        var launch = Heading(30);
        var capture = Heading(30, 80);

        var single = new AtgmLaunchCapture(launch, capture);
        var oneStep = single.Step(launch, capture, .2, 200, .1, false);

        var split = new AtgmLaunchCapture(launch, capture);
        var heading = launch;
        for (var i = 0; i < 10; i++)
            heading = SafeNormalize(split.Step(heading, capture, .2, 200, .01, false), heading);

        check(Vector3.Distance(SafeNormalize(oneStep, launch), heading) < .005,
            "ten .01s steps match one .1s step for bounded gathering");

        var oversized = new AtgmLaunchCapture(launch, capture);
        var oversizedResult = oversized.Step(launch, capture, .2, 200, .2, false);
        check(SameDirection(oversizedResult, launch),
            "oversized timestep is rejected rather than clamped upward");
    }

    private static Vector3 Heading(double pitchDegrees, double yawDegrees = 0)
    {
        var pitch = pitchDegrees * Degrees;
        var yaw = yawDegrees * Degrees;
        return new Vector3((float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch),
            (float)(Math.Cos(yaw) * Math.Cos(pitch)));
    }

    private static double AngleDegrees(Vector3 from, Vector3 to)
    {
        var a = SafeNormalize(from, Vector3.UnitZ);
        var b = SafeNormalize(to, Vector3.UnitZ);
        var dot = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);
        return Math.Atan2(Vector3.Cross(a, b).Length(), dot) / Degrees;
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        if (!Finite(value) || value.LengthSquared() < 1e-12f) return fallback;
        var normalized = Vector3.Normalize(value);
        return Finite(normalized) ? normalized : fallback;
    }

    private static bool SameDirection(Vector3 value, Vector3 expected)
    {
        if (!Finite(value)) return false;
        return Vector3.Distance(SafeNormalize(value, Vector3.UnitZ),
            SafeNormalize(expected, Vector3.UnitZ)) < 1e-4f;
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Near(double value, double expected, double tolerance) =>
        double.IsFinite(value) && Math.Abs(value - expected) <= tolerance;
}


