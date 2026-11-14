using System.Numerics;

namespace SprocketExpandedBallistics;

// Optional extended flight feel. A null tuning on AtgmSettings keeps the legacy
// sight-ray law, motor model and flight path exactly as they were.
internal sealed record AtgmFlightProfile(
    string GuidanceLaw = "legacy",
    double LeadDistanceM = 25,
    string RangePattern = "none",
    double HumpHeightM = 1,
    double HumpRangeMinM = 200,
    double HumpRangeMaxM = 1200,
    double HumpTerminalDistanceM = 80,
    double HumpRampDistanceM = 40,
    double DirectRangeBelowM = 120,
    double ShortRangeThresholdM = 150,
    double ShortRangeGuidanceDelay = .05,
    double SpiralAmplitudeM = 0,
    double SpiralFrequencyHz = 1.5,
    double DisturbanceAmplitudeM = 0,
    double DisturbanceFrequencyHz = .8,
    string SpeedCalibration = "motor",
    double InitialElevationDegrees = 0)
{
    internal static readonly AtgmFlightProfile Legacy = new();

    internal static readonly HashSet<string> FlatKeys = new(StringComparer.Ordinal)
    {
        "guidanceLaw", "leadDistanceM", "rangePattern", "humpHeightM", "humpRangeMinM",
        "humpRangeMaxM", "humpTerminalDistanceM", "humpRampDistanceM", "directRangeBelowM",
        "shortRangeThresholdM", "shortRangeGuidanceDelay", "spiralAmplitudeM",
        "spiralFrequencyHz", "disturbanceAmplitudeM", "disturbanceFrequencyHz", "speedCalibration",
        "initialElevationDegrees"
    };

    internal static bool IsChoiceKey(string key) => key is "guidanceLaw" or "rangePattern" or "speedCalibration";

    internal void Validate(string profileId)
    {
        void Range(string field, double value, double min, double max)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new FormatException(ShellBallistics.RangeError(profileId, field, value, min, max));
        }
        if (GuidanceLaw is not ("legacy" or "losLead"))
            throw new FormatException($"Profile '{profileId}': guidanceLaw is '{GuidanceLaw}'; expected legacy or losLead.");
        if (RangePattern is not ("none" or "hump"))
            throw new FormatException($"Profile '{profileId}': rangePattern is '{RangePattern}'; expected none or hump.");
        if (SpeedCalibration is not ("motor" or "fixed" or "motorSmooth"))
            throw new FormatException($"Profile '{profileId}': speedCalibration is '{SpeedCalibration}'; expected motor, motorSmooth or fixed.");
        Range("leadDistanceM", LeadDistanceM, 0, 5000);
        Range("humpHeightM", HumpHeightM, 0, 10);
        Range("humpRangeMinM", HumpRangeMinM, 10, 20000);
        Range("humpRangeMaxM", HumpRangeMaxM, 10, 20000);
        Range("humpTerminalDistanceM", HumpTerminalDistanceM, 1, 2000);
        Range("humpRampDistanceM", HumpRampDistanceM, 0, 2000);
        Range("directRangeBelowM", DirectRangeBelowM, 0, 2000);
        Range("shortRangeThresholdM", ShortRangeThresholdM, 0, 2000);
        Range("shortRangeGuidanceDelay", ShortRangeGuidanceDelay, 0, 5);
        Range("spiralAmplitudeM", SpiralAmplitudeM, 0, 2);
        Range("spiralFrequencyHz", SpiralFrequencyHz, 0, 20);
        Range("disturbanceAmplitudeM", DisturbanceAmplitudeM, 0, 2);
        Range("disturbanceFrequencyHz", DisturbanceFrequencyHz, 0, 20);
        Range("initialElevationDegrees", InitialElevationDegrees, 0, 45);
        if (RangePattern == "hump" && HumpRangeMaxM < HumpRangeMinM)
            throw new FormatException($"Profile '{profileId}': humpRangeMaxM must be greater than or equal to humpRangeMinM.");
    }
}

internal enum AtgmRangePattern { Direct, Normal, Hump }

// Per-flight oscillator identity. No phase or basis is shared between missiles.
internal sealed class AtgmFlightState
{
    internal readonly long Seed;
    internal Vector3 Right = Vector3.UnitX;
    internal Vector3 Up = Vector3.UnitY;
    internal AtgmFlightState(long seed) => Seed = seed;
}

internal static class AtgmFlightDynamics
{
    internal const double MinimumLaunchRangeM = 5;
    internal const double MaximumLaunchRangeM = 20000;

    internal static bool ValidLaunchRange(double range) =>
        double.IsFinite(range) && range >= MinimumLaunchRangeM && range <= MaximumLaunchRangeM;

    // Launch-range bands. A hump profile without a fresh exact range uses the
    // documented safe direct fallback rather than guessing a distance.
    internal static AtgmRangePattern SelectPattern(AtgmFlightProfile? tuning, double? launchRange)
    {
        if (tuning is null || tuning.RangePattern != "hump") return AtgmRangePattern.Normal;
        if (launchRange is not { } range || !ValidLaunchRange(range)) return AtgmRangePattern.Direct;
        if (tuning.DirectRangeBelowM > 0 && range < tuning.DirectRangeBelowM) return AtgmRangePattern.Direct;
        return range >= tuning.HumpRangeMinM && range <= tuning.HumpRangeMaxM
            ? AtgmRangePattern.Hump
            : AtgmRangePattern.Normal;
    }

    // Short-range shots may skip or shorten only the steering lockout. The
    // vehicle input-release contract and maximum flight time are untouched.
    internal static double EffectiveGuidanceDelay(double legacyDelay, AtgmFlightProfile? tuning, double? launchRange)
    {
        if (tuning is null || tuning.RangePattern != "hump" || tuning.ShortRangeThresholdM <= 0)
            return legacyDelay;
        // Unknown/invalid range already selects the DIRECT pattern. Keep its
        // short lockout too, rather than retaining a blind one-second interval.
        if(launchRange is {} range && ValidLaunchRange(range) && range>=tuning.ShortRangeThresholdM)
            return legacyDelay;
        return Math.Min(legacyDelay, Math.Max(0, tuning.ShortRangeGuidanceDelay));
    }

    // Reproject a native cursor-picked point onto the physical optical origin.
    // Extending a high third-person camera ray past a nearby target drives a
    // missile below its intended LOS. No lock or future target search is involved.
    internal static bool TryOpticalAim(Vector3 opticalOrigin,Vector3 nativeAimPoint,out Vector3 direction)
    {
        direction=default;
        if(!Finite(opticalOrigin)||!Finite(nativeAimPoint))return false;
        var delta=nativeAimPoint-opticalOrigin;var size=delta.LengthSquared();
        if(!float.IsFinite(size)||size<1)return false;
        direction=Vector3.Normalize(delta);return Finite(direction);
    }

    // LOS-lead law: aim ahead on the visible sight ray at radial missile distance
    // plus a fixed metre lead. The legacy projected-distance law remains separate.
    internal static bool TryAimAlongSightRay(Vector3 origin, Vector3 direction, Vector3 missilePosition,
        double leadDistanceM, out Vector3 point)
    {
        point = default;
        var lengthSquared = direction.LengthSquared();
        if (!Finite(origin) || !Finite(direction) || !float.IsFinite(lengthSquared) || lengthSquared < 1e-8f ||
            !Finite(missilePosition) || !double.IsFinite(leadDistanceM) || leadDistanceM < 0)
            return false;
        var axis = Vector3.Normalize(direction);
        var forward = Vector3.Distance(origin, missilePosition) + leadDistanceM;
        if (!double.IsFinite(forward) || forward > 1e7) return false;
        point = origin + axis * (float)forward;
        return Finite(point);
    }

    // Height above the sight-ray command until near the snapshotted launch
    // range, then a smooth descent to LOS. Values outside the valid window are 0.
    internal static double HumpOffset(double height, double launchRange, double currentRange,
        double terminalDistance, double rampDistance)
    {
        if (!double.IsFinite(height) || height <= 0 ||
            !ValidLaunchRange(launchRange) || !double.IsFinite(currentRange) || currentRange < 0 ||
            currentRange >= launchRange) return 0;
        var terminal = Math.Max(1e-3, double.IsFinite(terminalDistance) ? terminalDistance : 0);
        var descend = Smooth01((launchRange - currentRange) / terminal);
        var ramp = double.IsFinite(rampDistance) && rampDistance > 0 ? Smooth01(currentRange / rampDistance) : 1;
        return height * descend * ramp;
    }

    // Existing motor calibration is the default. The fixed arcade option is a
    // gameplay calibration only; it does not model a real engine curve.
    internal static double FlightSpeed(AtgmSettings settings, AtgmFlightProfile? tuning, double initialSpeed, double age)
    {
        if(tuning?.SpeedCalibration=="motorSmooth")return SmoothMotorSpeed(settings,initialSpeed,age);
        if (tuning?.SpeedCalibration == "fixed" &&
            double.IsFinite(settings.FlightSpeed) && settings.FlightSpeed > 0)
            return Math.Clamp(settings.FlightSpeed, 10, 1000);
        return AtgmGuidance.SpeedAtAge(settings, initialSpeed, age);
    }

    // A boost curve with a short thrust ramp at ignition and before cruise.
    // Acceleration remains a physical upper bound; speed is continuous at both
    // ends. This is a gameplay calibration, not a measured real missile motor.
    internal static double SmoothMotorSpeed(AtgmSettings settings,double initialSpeed,double age)
    {
        if(!double.IsFinite(initialSpeed)||initialSpeed<0||!double.IsFinite(age)||age<0)
            throw new ArgumentOutOfRangeException(nameof(age));
        if(settings.Acceleration<=0)return AtgmGuidance.SpeedAtAge(settings,initialSpeed,age);
        var start=Math.Clamp(initialSpeed,10,settings.FlightSpeed);
        var difference=settings.FlightSpeed-start;
        var poweredAge=Math.Max(0,age-settings.MotorDelay);
        var boostAge=settings.MotorBurnTime>0?Math.Min(poweredAge,settings.MotorBurnTime):poweredAge;
        var speed=start;
        if(difference>1e-9)
        {
            var linearDuration=difference/settings.Acceleration;
            var ramp=Math.Min(.2,linearDuration);
            var duration=linearDuration+ramp;
            if(boostAge<ramp)speed=start+settings.Acceleration*boostAge*boostAge/(2*ramp);
            else if(boostAge<linearDuration)speed=start+settings.Acceleration*(boostAge-ramp*.5);
            else if(boostAge<duration){var remaining=duration-boostAge;speed=settings.FlightSpeed-settings.Acceleration*remaining*remaining/(2*ramp);}
            else speed=settings.FlightSpeed;
        }
        var coastAge=settings.MotorBurnTime>0?Math.Max(0,poweredAge-settings.MotorBurnTime):0;
        return Math.Clamp(speed-settings.CoastDeceleration*coastAge,10,settings.FlightSpeed);
    }

    // Stable identity seed for this launch, not a global phase.
    internal static long Seed(int id, float spawn, string profileId)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var c in profileId ?? "") { hash ^= c; hash *= 1099511628211UL; }
            hash ^= (uint)id; hash *= 1099511628211UL;
            hash ^= (uint)BitConverter.SingleToInt32Bits(spawn); hash *= 1099511628211UL;
            return (long)(hash & 0x7FFFFFFFFFFFFFFFUL);
        }
    }

    // Unit right/up basis that stays finite and orthogonal at a vertical heading.
    internal static (Vector3 Right, Vector3 Up) OrthonormalBasis(Vector3 direction)
    {
        if (!Finite(direction) || !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < 1e-8f)
            direction = Vector3.UnitZ;
        direction = Vector3.Normalize(direction);
        var reference = Math.Abs(direction.Y) > .9f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Cross(reference, direction);
        if (!Finite(right) || right.LengthSquared() < 1e-8f)
            right = Vector3.Cross(Vector3.UnitX, direction);
        if (!Finite(right) || right.LengthSquared() < 1e-8f) right = Vector3.UnitX;
        right = Vector3.Normalize(right);
        var up = Vector3.Cross(direction, right);
        if (!Finite(up) || up.LengthSquared() < 1e-8f) up = Vector3.UnitY;
        return (right, Vector3.Normalize(up));
    }

    // Smooth deterministic low-amplitude flight feel. The basis and seed are
    // per-flight; zero amplitudes return exactly zero so legacy paths are unchanged.
    internal static Vector3 Disturbance(AtgmFlightProfile? tuning, double age, AtgmFlightState? state, Vector3 direction)
    {
        if (tuning is null || state is null ||
            (tuning.SpiralAmplitudeM <= 0 && tuning.DisturbanceAmplitudeM <= 0)) return Vector3.Zero;
        if (!double.IsFinite(age) || age < 0 ||
            !double.IsFinite(tuning.SpiralAmplitudeM) || !double.IsFinite(tuning.SpiralFrequencyHz) ||
            !double.IsFinite(tuning.DisturbanceAmplitudeM) || !double.IsFinite(tuning.DisturbanceFrequencyHz))
            return Vector3.Zero;
        var basis = StableBasis(direction, state);
        state.Right = basis.Right; state.Up = basis.Up;
        var phase = (state.Seed & 0xFFFF) / 65536d * Math.PI * 2;
        // A true two-axis circular spiral in the perpendicular plane. Its radius
        // stays at the configured amplitude; the independent smooth disturbance
        // is added separately so spiral-only checks can verify a constant radius.
        var spiralPhase = 2 * Math.PI * tuning.SpiralFrequencyHz * age + phase;
        var spiralRight = tuning.SpiralAmplitudeM * Math.Cos(spiralPhase);
        var spiralUp = tuning.SpiralAmplitudeM * Math.Sin(spiralPhase);
        var noise = tuning.DisturbanceAmplitudeM * (
            .6 * Math.Sin(2 * Math.PI * tuning.DisturbanceFrequencyHz * age + phase * 1.7) +
            .4 * Math.Sin(2 * Math.PI * tuning.DisturbanceFrequencyHz * .37 * age + phase * .31));
        var offset = state.Right * (float)spiralRight + state.Up * (float)(spiralUp + noise);
        var limit = Math.Min(2, tuning.SpiralAmplitudeM + tuning.DisturbanceAmplitudeM);
        if (offset.LengthSquared() > limit * limit && offset.LengthSquared() > 0)
            offset = Vector3.Normalize(offset) * (float)limit;
        return Finite(offset) ? offset : Vector3.Zero;
    }

    // Apply the same bounded disturbance as a small heading deflection. This is
    // used by manual/keyboard steering, where there is no sight aim point to offset.
    internal static Vector3 DisturbDirection(Vector3 direction, AtgmFlightProfile? tuning, double age,
        AtgmFlightState? state, double lookAheadM = 20)
    {
        var offset = Disturbance(tuning, age, state, direction);
        if (offset.LengthSquared() <= 0 || !Finite(direction) ||
            !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < 1e-8f ||
            !double.IsFinite(lookAheadM) || lookAheadM <= 0)
            return direction;
        var forward = Vector3.Normalize(direction);
        var point = forward * (float)lookAheadM + offset;
        return Finite(point) && point.LengthSquared() > 1e-8f ? Vector3.Normalize(point) : direction;
    }

    // Combine input and a time-scaled disturbance before one turn cap from the
    // original heading. Disturbance remains present while a key is held. This
    // keeps combined steering bounded, forward-only and target-free, and returns
    // the exact legacy KeyboardStep when tuning is zero. A null result means the
    // command is not active (before activation, after link loss, superseded).
    internal static Vector3? ManualCommand(Vector3 heading, double yaw, double pitch,
        AtgmSettings settings, AtgmFlightProfile? tuning, double age, AtgmFlightState? state,
        double deltaSeconds, bool commandActive)
    {
        if (!commandActive || settings.GuidanceMode != "keyboard" ||
            !double.IsFinite(deltaSeconds) || deltaSeconds <= 0 ||
            !Finite(heading) || heading.LengthSquared() < 1e-8f) return null;
        var keyboard = AtgmGuidance.KeyboardStep(heading, yaw, pitch, settings, deltaSeconds);
        if (!Finite(keyboard) || keyboard.LengthSquared() < 1e-8f) return null;
        if (tuning is null || (tuning.SpiralAmplitudeM <= 0 && tuning.DisturbanceAmplitudeM <= 0))
            return keyboard;
        var forward = Vector3.Normalize(heading);
        var manual = Vector3.Normalize(keyboard);
        var allowed = Math.Max(0, settings.MaxTurnRate) * Math.PI / 180 * deltaSeconds;
        var disturbed = DisturbDirection(manual, tuning, age, state);
        var noiseAngle = AngleBetween(manual, disturbed);
        var combined = TurnTowards(manual, disturbed, noiseAngle * deltaSeconds);
        var steered = TurnTowards(forward, combined, allowed);
        if (!Finite(steered) || steered.LengthSquared() < 1e-8f ||
            Vector3.Dot(Vector3.Normalize(steered), forward) <= 0) return keyboard;
        return Vector3.Normalize(steered) * keyboard.Length();
    }

    // Optional launch elevation relative to the snapshotted launch heading. It is
    // applied only while the initial steering lockout is active and is skipped in
    // the direct short-range fallback. Rotation obeys the actual turn cap.
    internal static Vector3 InitialElevationHeading(Vector3 launchHeading, double elevationDegrees,
        double maxTurnRateDegrees, double deltaSeconds, Vector3 currentHeading,
        bool initialLockout, bool directMode)
    {
        if (!initialLockout || directMode || !double.IsFinite(elevationDegrees) || elevationDegrees <= 0 ||
            !double.IsFinite(maxTurnRateDegrees) || maxTurnRateDegrees <= 0 ||
            !double.IsFinite(deltaSeconds) || deltaSeconds <= 0 ||
            !Finite(launchHeading) || launchHeading.LengthSquared() < 1e-8f ||
            !Finite(currentHeading) || currentHeading.LengthSquared() < 1e-8f)
            return currentHeading;
        var forward = Vector3.Normalize(launchHeading);
        var (_, up) = OrthonormalBasis(forward);
        var elevation = Math.Clamp(elevationDegrees, 0, 45) * Math.PI / 180;
        var target = Vector3.Normalize(forward * (float)Math.Cos(elevation) + up * (float)Math.Sin(elevation));
        var allowed = maxTurnRateDegrees * Math.PI / 180 * deltaSeconds;
        var result = TurnTowards(currentHeading, target, allowed);
        return Finite(result) && result.LengthSquared() > 1e-8f &&
            Vector3.Dot(Vector3.Normalize(result), forward) > 0 ? result : currentHeading;
    }

    // Shared bounded turn helper for the manual and launch-elevation paths.
    internal static Vector3 TurnTowards(Vector3 current, Vector3 desired, double maxRadians)
    {
        if (!Finite(current) || !Finite(desired) ||
            current.LengthSquared() < 1e-8f || desired.LengthSquared() < 1e-8f) return current;
        var from = Vector3.Normalize(current);
        var to = Vector3.Normalize(desired);
        if (!double.IsFinite(maxRadians) || maxRadians <= 0) return from;
        var axis = Vector3.Cross(from, to);
        var dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (Math.Atan2(axis.Length(), dot) <= maxRadians) return to;
        if (!Finite(axis) || axis.LengthSquared() < 1e-12f) return from;
        return Vector3.Normalize(Vector3.Transform(from,
            Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)maxRadians)));
    }

    private static double AngleBetween(Vector3 a, Vector3 b)
    {
        if (!Finite(a) || !Finite(b) || a.LengthSquared() < 1e-8f || b.LengthSquared() < 1e-8f) return 0;
        var from=Vector3.Normalize(a);var to=Vector3.Normalize(b);
        return Math.Atan2(Vector3.Cross(from,to).Length(),Math.Clamp(Vector3.Dot(from,to),-1f,1f));
    }

    // Launch-frame distance, independent of a moving camera/sight origin.
    internal static double RangeProgress(Vector3 launchPosition,Vector3 position,double previous)
    {
        var prior=double.IsFinite(previous)?Math.Clamp(previous,0,1e7):0;
        if(!Finite(launchPosition)||!Finite(position))return prior;
        var distance=Vector3.Distance(launchPosition,position);
        return float.IsFinite(distance)?Math.Max(prior,Math.Clamp(distance,0,1e7)):prior;
    }

    // Reuse the previous perpendicular basis when possible. This avoids a
    // reference-axis switch artifact while a heading crosses near vertical.
    private static (Vector3 Right, Vector3 Up) StableBasis(Vector3 direction, AtgmFlightState state)
    {
        var fallback = OrthonormalBasis(direction);
        if (!Finite(direction) || !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < 1e-8f)
            direction = Vector3.UnitZ;
        direction = Vector3.Normalize(direction);
        var right = state.Right - direction * Vector3.Dot(state.Right, direction);
        if (!Finite(right) || right.LengthSquared() < 1e-8f) right = fallback.Right;
        else right = Vector3.Normalize(right);
        var up = Vector3.Cross(direction, right);
        if (!Finite(up) || up.LengthSquared() < 1e-8f) up = fallback.Up;
        else up = Vector3.Normalize(up);
        return (right, up);
    }

    private static double Smooth01(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
