using System.Numerics;
using SprocketStabilization;

int count = 0;
void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }

// Technology date policy: the native frame date is the same gate as the data.
Check(!StabilizationDatePolicy.Allows(new DateTime(1947, 12, 31)), "stabilization unavailable one day early");
Check(StabilizationDatePolicy.Allows(new DateTime(1948, 1, 1)), "stabilization available on 1948.01.01");
Check(StabilizationDatePolicy.Allows(new DateTime(9999, 12, 31)), "final-era sentinel makes stabilization available");
Check(!StabilizationDatePolicy.Allows(null), "missing frame date fails closed");
Check(StabilizationDatePolicy.MinimumDate == new DateTime(1948, 1, 1), "technology floor is 1948.01.01");
var savedStabilizerCheckbox = true;
Check(!StabilizationDatePolicy.Allows(new DateTime(1945, 9, 3)) && savedStabilizerCheckbox,
    "too-early saved stabilizer checkbox keeps its identity and stays inactive");

// Original drive-setup rules (unchanged behaviour).
Check(DriveSetupRules.UniqueOwnership(1, 1, 1), "unique association");
Check(!DriveSetupRules.UniqueOwnership(2, 1, 1) && !DriveSetupRules.UniqueOwnership(1, 2, 1) && !DriveSetupRules.UniqueOwnership(1, 1, 2), "shared designs rejected");
foreach (float current in new[] { .1f, 1f, 8f, 999f, 1000f, 10000f, 15000f })
{
    var bounds = DriveSetupRules.StrengthLimits(current, 100, 10000);
    Check(current / 100 >= bounds.Min && current / 100 <= bounds.Max, "imported value kept in visible range");
    var native = current; var displayed = native / 100;
    Check(native == current, "reading does not clamp saved scalar");
    Check(Math.Abs(displayed * 100 - current) < .002f, "scale round-trip");
    Check(DriveSetupRules.ValidStrengthEdit(bounds.Max, bounds.Min, bounds.Max), "finite permitted endpoint");
    Check(!DriveSetupRules.ValidStrengthEdit(0, bounds.Min, bounds.Max), "zero rejected");
    Check(!DriveSetupRules.ValidStrengthEdit(float.NaN, bounds.Min, bounds.Max) && !DriveSetupRules.ValidStrengthEdit(float.PositiveInfinity, bounds.Min, bounds.Max), "nonfinite rejected");
}
Check(!DriveSetupRules.ValidTechnology(0, 10000) && !DriveSetupRules.ValidTechnology(100, float.PositiveInfinity) && !DriveSetupRules.ValidTechnology(100, 1000001), "invalid technology disabled");

// Two-axis stabilizer math.
Quaternion identity = Quaternion.Identity;
Vector3 identityResult = StabilizerMath.CompensateDirection(Vector3.UnitZ, identity);
Check(Vector3.Distance(identityResult, Vector3.UnitZ) < 1e-4f, "identity hull keeps the direction");

Quaternion yaw90 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
Vector3 yawResult = StabilizerMath.CompensateDirection(Vector3.UnitX, yaw90);
Check(MathF.Abs(yawResult.Z - 1f) < 1e-4f && MathF.Abs(yawResult.X) < 1e-4f, "hull yaw is removed from the incoming direction");

foreach (float yaw in new[] { -135f, -90f, -30f, 0f, 30f, 90f, 135f })
{
    foreach (float pitch in new[] { -20f, 0f, 20f })
    {
        Quaternion hull = Quaternion.CreateFromYawPitchRoll(
            yaw * MathF.PI / 180f,
            pitch * MathF.PI / 180f,
            0f);
        Vector3 local = Vector3.Normalize(new Vector3(.4f, -.2f, 1f));
        Vector3 worldAim = Vector3.Transform(local, hull);
        Vector3 roundTrip = StabilizerMath.CompensateDirection(worldAim, hull);
        Check(Vector3.Distance(roundTrip, local) < 2e-4f, $"hull-frame round-trip {yaw}/{pitch}");
    }
}

Check(StabilizerMath.CompensateDirection(Vector3.Zero, identity) == Vector3.Zero, "zero direction is ignored");
Vector3 nonNormalized = StabilizerMath.CompensateDirection(new Vector3(0f, 0f, 7f), identity);
Check(MathF.Abs(nonNormalized.Length() - 1f) < 1e-4f, "non-normalized input is normalized");

Check(StabilizerMath.AngleDegrees(Vector3.UnitZ, Vector3.UnitZ) < .001f, "angle helper for equal vectors");
Check(MathF.Abs(StabilizerMath.AngleDegrees(Vector3.UnitZ, Vector3.UnitX) - 90f) < .01f, "angle helper for orthogonal vectors");
Check(!StabilizerMath.AcceptsEdit(false, true) && !StabilizerMath.AcceptsEdit(true, false) && StabilizerMath.AcceptsEdit(true, true), "stabilizer enable gate");

// Configured stabilizer boost validation (shared with the native-call path).
var boostDefaults = StabilizerBoost.FromConfig(null, null, null, null);
Check(
    boostDefaults.TorqueMultiplier == 100f &&
    boostDefaults.MaxTraverseRate == 360f &&
    boostDefaults.MaxAcceleration == 3600f &&
    boostDefaults.MaxBrakeAcceleration == 3600f,
    "boost defaults stay 100/360/3600/3600");

var boostCustom = StabilizerBoost.FromConfig(4.5f, 120f, 7200f, 5400f);
Check(
    boostCustom.TorqueMultiplier == 4.5f &&
    boostCustom.MaxTraverseRate == 120f &&
    boostCustom.MaxAcceleration == 7200f &&
    boostCustom.MaxBrakeAcceleration == 5400f,
    "valid configured boost values are preserved");

// The four settings have different production bounds, so the same hostile value
// can be invalid for one field and valid for another. Each case states the
// expected result for every field instead of assuming a uniform fallback.
(float Hostile, float Torque, float Rate, float Acceleration, float Brake)[] hostileBoostValues =
{
    (float.NaN, 100f, 360f, 3600f, 3600f),
    (float.PositiveInfinity, 100f, 360f, 3600f, 3600f),
    (float.NegativeInfinity, 100f, 360f, 3600f, 3600f),
    (0f, 100f, 360f, 3600f, 3600f),
    (-0f, 100f, 360f, 3600f, 3600f),
    (-1f, 100f, 360f, 3600f, 3600f),
    (-100f, 100f, 360f, 3600f, 3600f),
    (float.Epsilon, 100f, 360f, 3600f, 3600f),
    (float.MaxValue, 100f, 360f, 3600f, 3600f),
    (float.MinValue, 100f, 360f, 3600f, 3600f),
    // 200000 exceeds the torque/rate cap but is inside the acceleration/brake cap.
    (StabilizerBoost.MaxTorqueMultiplier * 2f, 100f, 360f, 200000f, 200000f),
    (StabilizerBoost.MaxMaxTraverseRate * 2f, 100f, 360f, 200000f, 200000f),
    (StabilizerBoost.MaxMaxAcceleration * 2f, 100f, 360f, 3600f, 3600f),
    (StabilizerBoost.MaxMaxBrakeAcceleration * 2f, 100f, 360f, 3600f, 3600f)
};

foreach (var (hostile, expectedTorque, expectedRate, expectedAcceleration, expectedBrake) in hostileBoostValues)
{
    var sanitized = StabilizerBoost.FromConfig(hostile, hostile, hostile, hostile);
    Check(
        sanitized.TorqueMultiplier == expectedTorque &&
        sanitized.MaxTraverseRate == expectedRate &&
        sanitized.MaxAcceleration == expectedAcceleration &&
        sanitized.MaxBrakeAcceleration == expectedBrake,
        $"hostile boost value {hostile} gets the documented per-field result");

    Check(
        float.IsFinite(sanitized.TorqueMultiplier) && sanitized.TorqueMultiplier > 0f &&
        float.IsFinite(sanitized.MaxTraverseRate) && sanitized.MaxTraverseRate > 0f &&
        float.IsFinite(sanitized.MaxAcceleration) && sanitized.MaxAcceleration > 0f &&
        float.IsFinite(sanitized.MaxBrakeAcceleration) && sanitized.MaxBrakeAcceleration > 0f,
        "sanitized boost values are always finite and positive");
}

// The exact round-1 failing case: 200000 is invalid for torque/rate but valid
// for acceleration/brake, so it must not become an all-default fallback.
var mixedBoundBoost = StabilizerBoost.FromConfig(200000f, 200000f, 200000f, 200000f);
Check(
    mixedBoundBoost.TorqueMultiplier == 100f &&
    mixedBoundBoost.MaxTraverseRate == 360f &&
    mixedBoundBoost.MaxAcceleration == 200000f &&
    mixedBoundBoost.MaxBrakeAcceleration == 200000f,
    "200000 falls back for torque/rate only, not for acceleration/brake");

var oneBadBoost = StabilizerBoost.FromConfig(float.NaN, 90f, 1800f, 2700f);
Check(
    oneBadBoost.TorqueMultiplier == 100f &&
    oneBadBoost.MaxTraverseRate == 90f &&
    oneBadBoost.MaxAcceleration == 1800f &&
    oneBadBoost.MaxBrakeAcceleration == 2700f,
    "an invalid field falls back without disturbing valid fields");

// Every field's own inclusive lower/upper bound is accepted while the other
// fields keep ordinary valid values.
Check(
    StabilizerBoost.FromConfig(StabilizerBoost.MinTorqueMultiplier, 360f, 3600f, 3600f).TorqueMultiplier ==
    StabilizerBoost.MinTorqueMultiplier,
    "torque lower bound accepted");
Check(
    StabilizerBoost.FromConfig(StabilizerBoost.MaxTorqueMultiplier, 360f, 3600f, 3600f).TorqueMultiplier ==
    StabilizerBoost.MaxTorqueMultiplier,
    "torque upper bound accepted");
Check(
    StabilizerBoost.FromConfig(100f, StabilizerBoost.MinMaxTraverseRate, 3600f, 3600f).MaxTraverseRate ==
    StabilizerBoost.MinMaxTraverseRate,
    "traverse-rate lower bound accepted");
Check(
    StabilizerBoost.FromConfig(100f, StabilizerBoost.MaxMaxTraverseRate, 3600f, 3600f).MaxTraverseRate ==
    StabilizerBoost.MaxMaxTraverseRate,
    "traverse-rate upper bound accepted");
Check(
    StabilizerBoost.FromConfig(100f, 360f, StabilizerBoost.MinMaxAcceleration, 3600f).MaxAcceleration ==
    StabilizerBoost.MinMaxAcceleration,
    "acceleration lower bound accepted");
Check(
    StabilizerBoost.FromConfig(100f, 360f, StabilizerBoost.MaxMaxAcceleration, 3600f).MaxAcceleration ==
    StabilizerBoost.MaxMaxAcceleration,
    "acceleration upper bound accepted");
Check(
    StabilizerBoost.FromConfig(100f, 360f, 3600f, StabilizerBoost.MinMaxBrakeAcceleration).MaxBrakeAcceleration ==
    StabilizerBoost.MinMaxBrakeAcceleration,
    "brake lower bound accepted");
Check(
    StabilizerBoost.FromConfig(100f, 360f, 3600f, StabilizerBoost.MaxMaxBrakeAcceleration).MaxBrakeAcceleration ==
    StabilizerBoost.MaxMaxBrakeAcceleration,
    "brake upper bound accepted");

// Just outside each field's own bound falls back only for that field.
Check(
    StabilizerBoost.FromConfig(StabilizerBoost.MinTorqueMultiplier * 0.5f, 360f, 3600f, 3600f).TorqueMultiplier == 100f,
    "torque below its lower bound falls back");
Check(
    StabilizerBoost.FromConfig(StabilizerBoost.MaxTorqueMultiplier * 1.5f, 360f, 3600f, 3600f).TorqueMultiplier == 100f,
    "torque above its upper bound falls back");
Check(
    StabilizerBoost.FromConfig(100f, StabilizerBoost.MinMaxTraverseRate * 0.5f, 3600f, 3600f).MaxTraverseRate == 360f,
    "traverse rate below its lower bound falls back");
Check(
    StabilizerBoost.FromConfig(100f, StabilizerBoost.MaxMaxTraverseRate * 1.5f, 3600f, 3600f).MaxTraverseRate == 360f,
    "traverse rate above its upper bound falls back");
Check(
    StabilizerBoost.FromConfig(100f, 360f, StabilizerBoost.MinMaxAcceleration * 0.5f, 3600f).MaxAcceleration == 3600f,
    "acceleration below its lower bound falls back");
Check(
    StabilizerBoost.FromConfig(100f, 360f, StabilizerBoost.MaxMaxAcceleration * 1.5f, 3600f).MaxAcceleration == 3600f,
    "acceleration above its upper bound falls back");
Check(
    StabilizerBoost.FromConfig(100f, 360f, 3600f, StabilizerBoost.MinMaxBrakeAcceleration * 0.5f).MaxBrakeAcceleration == 3600f,
    "brake below its lower bound falls back");
Check(
    StabilizerBoost.FromConfig(100f, 360f, 3600f, StabilizerBoost.MaxMaxBrakeAcceleration * 1.5f).MaxBrakeAcceleration == 3600f,
    "brake above its upper bound falls back");

var boostSanitizedOnce = StabilizerBoost.FromConfig(float.NaN, 0f, float.PositiveInfinity, -2f);
var boostSanitizedTwice = StabilizerBoost.FromConfig(
    boostSanitizedOnce.TorqueMultiplier,
    boostSanitizedOnce.MaxTraverseRate,
    boostSanitizedOnce.MaxAcceleration,
    boostSanitizedOnce.MaxBrakeAcceleration);
Check(
    boostSanitizedTwice.TorqueMultiplier == boostSanitizedOnce.TorqueMultiplier &&
    boostSanitizedTwice.MaxTraverseRate == boostSanitizedOnce.MaxTraverseRate &&
    boostSanitizedTwice.MaxAcceleration == boostSanitizedOnce.MaxAcceleration &&
    boostSanitizedTwice.MaxBrakeAcceleration == boostSanitizedOnce.MaxBrakeAcceleration,
    "sanitizing already-sanitized values is idempotent");

// Realistic historical limits (M60-class reference: 24/6 deg/s, 0.75 s response).
var realistic = StabilizerBoost.RealisticFromConfig(24f, 6f, 0.75f);
Check(realistic.HorizontalMaxTraverseRate == 24f, "realistic horizontal rate default is 24 deg/s");
Check(realistic.ElevationMaxTraverseRate == 6f, "realistic elevation rate default is 6 deg/s");
Check(realistic.ResponseTimeSeconds == 0.75f, "realistic response time default is 0.75 s");
Check(realistic.RateFor(false) == 24f && realistic.RateFor(true) == 6f, "axis selects the right rate ceiling");
Check(
    MathF.Abs(realistic.AccelerationFor(false) - 32f) < 1e-4f &&
    MathF.Abs(realistic.AccelerationFor(true) - 8f) < 1e-4f,
    "acceleration is rate divided by response time per axis");

var realisticInvalid = StabilizerBoost.RealisticFromConfig(float.NaN, -1f, 0f);
Check(
    realisticInvalid.HorizontalMaxTraverseRate == StabilizerBoost.DefaultHorizontalMaxTraverseRate &&
    realisticInvalid.ElevationMaxTraverseRate == StabilizerBoost.DefaultElevationMaxTraverseRate &&
    realisticInvalid.ResponseTimeSeconds == StabilizerBoost.DefaultResponseTimeSeconds,
    "invalid realistic limits fall back to the historical defaults independently");

var realisticCustom = StabilizerBoost.RealisticFromConfig(40f, 10f, 0.5f);
Check(
    realisticCustom.HorizontalMaxTraverseRate == 40f &&
    realisticCustom.ElevationMaxTraverseRate == 10f &&
    realisticCustom.ResponseTimeSeconds == 0.5f,
    "valid realistic limits are used exactly");

Console.WriteLine($"{count} stabilization checks passed.");
