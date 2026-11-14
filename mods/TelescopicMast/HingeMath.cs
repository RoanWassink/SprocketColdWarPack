namespace SprocketTelescopicMast;

/// <summary>Pure hinge geometry, mass and motion (metres, degrees, seconds). Shared with the tests.</summary>
public static class HingeMath
{
    // Base frame: origin on the mounting surface directly below the pin, pin along local X.
    public const float PinRadius = .02f, PinLength = .54f, PinHeight = .025f;
    public const float KnuckleRadius = .025f, LeafWidth = .50f, LeafDepth = .40f, LeafThickness = .02f;
    public const float StrapDepth = .07f, StrapThickness = .02f, LugWidth = .02f, LugHeight = .045f, LugDepth = .055f;
    public const float SteelDensity = 7850f;
    // Hinge Mount (0.5.0): small hidden carrier plate, origin on its own pin axis.
    public const float MountWidth = .10f, MountDepth = .10f, MountThickness = .01f, MountPinRadius = .01f;

    /// <summary>Leaf steel at scale 1: plate outside the knuckle plus the knuckle tube around the pin.</summary>
    public static float LeafVolume =>
        LeafWidth * LeafThickness * (LeafDepth - KnuckleRadius) +
        MathF.PI * (KnuckleRadius * KnuckleRadius - PinRadius * PinRadius) * LeafWidth;
    /// <summary>Base steel at scale 1: strap plate, two end lugs (minus the pin bore) and the solid pin.</summary>
    public static float BaseVolume =>
        PinLength * StrapThickness * StrapDepth +
        2 * (LugWidth * LugHeight * LugDepth - MathF.PI * PinRadius * PinRadius * LugWidth) +
        MathF.PI * PinRadius * PinRadius * PinLength;

    /// <summary>Mount steel at scale 1: plate outside the pin plus the solid 20 mm pin.</summary>
    public static float MountVolume =>
        MountWidth * MountThickness * (MountDepth - MountPinRadius) +
        MathF.PI * MountPinRadius * MountPinRadius * MountWidth;

    public static float Mass(float volumeAtScaleOne, float sx, float sy, float sz) =>
        volumeAtScaleOne * Travel.VolumeFactor(sx, sy, sz) * SteelDensity;

    /// <summary>Material cost per kg matches the mast (1.2/kg); assembly covers the pin/actuator work.</summary>
    public static float MaterialCost(float kg) => kg * 1.2f;
    public static float AssemblyCost(bool isBase, float sx, float sy, float sz) =>
        (isBase ? 40f : 10f) * MathF.Sqrt(Travel.VolumeFactor(sx, sy, sz));

    public static float SanitizeAngle(float degrees) =>
        float.IsFinite(degrees) ? Math.Clamp(degrees, -180f, 180f) : 0f;

    /// <summary>Moves current toward target at speed deg/s, never overshooting; dt capped at 0.1 s.</summary>
    public static float StepAngle(float current, float target, float speed, float dt)
    {
        current = SanitizeAngle(current); target = SanitizeAngle(target);
        if (!float.IsFinite(speed) || !float.IsFinite(dt) || speed <= 0 || dt <= 0) return current;
        float step = Math.Clamp(speed, 1f, 720f) * Math.Min(dt, .1f);
        float delta = target - current;
        return MathF.Abs(delta) <= step ? target : current + MathF.Sign(delta) * step;
    }

    /// <summary>
    /// Rotation used on the leaf: about the base's local X, sign chosen so that a positive
    /// angle lifts the plate (which extends along +Z) away from the mounting surface.
    /// Returns the axis-angle in base-local terms: (axis x, angle degrees).
    /// </summary>
    public static float LocalRotationDegrees(float hingeAngle) => -SanitizeAngle(hingeAngle);
}
