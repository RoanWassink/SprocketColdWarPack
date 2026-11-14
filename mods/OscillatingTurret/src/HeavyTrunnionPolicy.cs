using System;

namespace SprocketOscillatingTurret;

/// <summary>
/// Pure rules of the Heavy Trunnion Mount (0.3.0). No game types, so the tests run them directly.
/// Native constants are from GameAssembly 3E58082804D9... (Sprocket 0.2.56.2), see
/// tests/native-evidence: LayingDrive.EnableBehaviour inlines BuildRotationAxis as
/// MotorTorque = Ratio * 125 * TorqueMultiplier and MaxSpeed = 180 / Ratio, so the top speed
/// never depends on the torque.
/// </summary>
internal static class HeavyTrunnionPolicy
{
    internal const string MantletGuid = "58b7e71b-b383-4782-ac62-d5b0873688c1";
    internal const string DriveGuid = "5fd35c60-7493-42aa-a065-5910f3280868";
    internal const string MantletName = "heavyTrunnionMantlet";
    internal const string DriveTag = "heavyTrunnionDrive";
    internal const string NativeTrunnionsGuid = "e11ce433-c0b3-4e52-899f-898bfe6d3fd3";
    internal static readonly DateTime AvailableFrom = new(1952, 1, 1);

    // The OpenSCAD model is drawn at trunnionsScale 1.5 and part scale 1.
    internal const float ModelTrunnionsScale = 1.5f;

    internal const float NativeTorquePerRatio = 125f;
    internal const float NativeSpeedConstant = 180f;
    // LayingDrive.ValidateTrunnionDistance compares with this literal; the static
    // MaxDistanceFromTrunnions field is not read there, so writing it would change nothing.
    internal const float NativeMaxDistanceFromTrunnions = 1f;
    // TraverseMotor.Build: mass 0.1 and assembly cost 0.08099862 per unit -> cost per kg of mechanism.
    internal const float NativeMechanismCostPerKg = 0.08099862f / 0.1f;
    // Sector pitch radius 135 mm / pinion pitch radius 36 mm.
    internal const float PinionRatio = 3.75f;
    internal const float PinionAngleEpsilon = 0.01f;

    internal const float DefaultTorqueFactor = 2f;
    internal const float DefaultExtraMassKg = 250f;
    internal const float DefaultCostFactor = 2f;
    internal const float DefaultMaxDistanceFactor = 1.5f;

    internal readonly record struct Settings(
        float TorqueFactor,
        float ExtraMassKg,
        float CostFactor,
        float MaxDistanceFactor);

    internal static readonly Settings Defaults = new(
        DefaultTorqueFactor, DefaultExtraMassKg, DefaultCostFactor, DefaultMaxDistanceFactor);

    /// <summary>
    /// Two fixed floors keep the part never lighter or cheaper than native: ExtraMassKg >= 0 and
    /// CostFactor >= 1. There are no upper limits. Non-finite or non-positive factors fall back to
    /// the default, because a zero torque or reach would break the drive rather than balance it.
    /// </summary>
    internal static Settings Sanitize(float torqueFactor, float extraMassKg, float costFactor, float maxDistanceFactor)
    {
        return new Settings(
            float.IsFinite(torqueFactor) && torqueFactor > 0f ? torqueFactor : DefaultTorqueFactor,
            float.IsFinite(extraMassKg) ? Math.Max(0f, extraMassKg) : DefaultExtraMassKg,
            float.IsFinite(costFactor) ? Math.Max(1f, costFactor) : DefaultCostFactor,
            float.IsFinite(maxDistanceFactor) && maxDistanceFactor > 0f ? maxDistanceFactor : DefaultMaxDistanceFactor);
    }

    internal static bool IsHeavyDrive(Func<string, bool> hasTag) => hasTag(DriveTag);

    internal static bool IsHeavyMantlet(string? layingDrivePartGuid) =>
        string.Equals(layingDrivePartGuid, DriveGuid, StringComparison.OrdinalIgnoreCase);

    internal static float MotorTorque(float ratio, float torqueMultiplier) =>
        ratio * NativeTorquePerRatio * torqueMultiplier;

    // Deliberately has no torque parameter: native MaxSpeed is 180 / Ratio.
    internal static float MaxSpeed(float ratio) => NativeSpeedConstant / ratio;

    internal static float ScaledTorqueMultiplier(float nativeMultiplier, Settings settings) =>
        nativeMultiplier * settings.TorqueFactor;

    internal static float Mass(Settings settings) => settings.ExtraMassKg;

    internal static float Cost(Settings settings) =>
        settings.ExtraMassKg * NativeMechanismCostPerKg * settings.CostFactor;

    internal static float MaxDistance(Settings settings) =>
        NativeMaxDistanceFromTrunnions * settings.MaxDistanceFactor;

    internal static float MeshScale(float trunnionsScale) =>
        (float.IsFinite(trunnionsScale) && trunnionsScale > 0f ? trunnionsScale : ModelTrunnionsScale) / ModelTrunnionsScale;

    /// <summary>Signed angle in degrees of a rotation about local X, from its quaternion x and w.</summary>
    internal static float AngleAboutX(float x, float w)
    {
        float degrees = 2f * MathF.Atan2(x, w) * (180f / MathF.PI);
        if (degrees > 180f) degrees -= 360f;
        else if (degrees <= -180f) degrees += 360f;
        return degrees;
    }

    internal static float PinionAngle(float elevationDegrees) => -PinionRatio * elevationDegrees;

    internal static bool AngleChanged(float last, float now) =>
        float.IsNaN(last) || MathF.Abs(now - last) > PinionAngleEpsilon;

    /// <summary>Quaternion (x, w) of a rotation about local X; y and z are zero.</summary>
    internal static (float X, float W) AboutX(float degrees)
    {
        float half = degrees * (MathF.PI / 360f);
        return (MathF.Sin(half), MathF.Cos(half));
    }
}
