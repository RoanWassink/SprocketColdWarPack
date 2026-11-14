using System;
using System.Numerics;

namespace SprocketOscillatingTurret;

/// <summary>
/// Pure elevation math shared by the runtime plugin and the executable tests.
///
/// Conventions used here:
/// * System.Numerics (right-handed) quaternions and vectors.
/// * The elevation axis is the local -X axis of the elevation ring, expressed in
///   the foundation frame: <c>restRotation * Vector3.left</c>.
/// * A positive elevation angle rotates that axis so that local +Z moves toward
///   +Y (raises the gun).
/// * The rest rotation is applied first, then the elevation delta:
///   <c>AngleAxis(angle, restRotation * left) * restRotation</c>.
///
/// The runtime adapter converts UnityEngine.Quaternion/Vector3 to these types at
/// the patch boundary so the math remains independently testable.
/// </summary>
public static class ElevationMath
{
    private const float MinimumLengthSquared = 1e-12f;

    /// <summary>
    /// Normalizes a quaternion, rejecting zero-length and non-finite input.
    /// </summary>
    public static bool TryNormalize(Quaternion value, out Quaternion normalized)
    {
        normalized = Quaternion.Identity;

        float lengthSquared =
            value.X * value.X +
            value.Y * value.Y +
            value.Z * value.Z +
            value.W * value.W;

        if (!float.IsFinite(lengthSquared) || lengthSquared <= MinimumLengthSquared)
        {
            return false;
        }

        float scale = 1f / MathF.Sqrt(lengthSquared);
        normalized = new Quaternion(
            value.X * scale,
            value.Y * scale,
            value.Z * scale,
            value.W * scale);
        return true;
    }

    /// <summary>
    /// Returns a normalized quaternion, or identity when the input is unusable.
    /// </summary>
    public static Quaternion NormalizeOrIdentity(Quaternion value)
    {
        return TryNormalize(value, out Quaternion normalized)
            ? normalized
            : Quaternion.Identity;
    }

    /// <summary>
    /// Replaces NaN/Infinity with zero so invalid simulation values cannot poison
    /// the rendered pose.
    /// </summary>
    public static float SanitizeAngleDegrees(float angleDegrees)
    {
        return float.IsFinite(angleDegrees) ? angleDegrees : 0f;
    }

    public static float DegreesToRadians(float degrees)
    {
        return degrees * (MathF.PI / 180f);
    }

    public static float RadiansToDegrees(float radians)
    {
        return radians * (180f / MathF.PI);
    }

    /// <summary>
    /// The local -X axis of the elevation ring expressed in the foundation frame.
    /// This is the axis the native traverse motor/behaviour should use for pitch.
    /// </summary>
    public static Vector3 ComputeElevationAxis(Quaternion restRotation)
    {
        if (!TryNormalize(restRotation, out Quaternion rest))
        {
            return -Vector3.UnitX;
        }

        Vector3 axis = Vector3.Transform(-Vector3.UnitX, rest);
        return NormalizeVectorOrFallback(axis, -Vector3.UnitX);
    }

    /// <summary>
    /// The native traverse axis (<c>restRotation * Vector3.up</c>). Used only when
    /// this plugin deliberately returns control of a behaviour to the native game.
    /// </summary>
    public static Vector3 ComputeNativeAxis(Quaternion restRotation)
    {
        if (!TryNormalize(restRotation, out Quaternion rest))
        {
            return Vector3.UnitY;
        }

        Vector3 axis = Vector3.Transform(Vector3.UnitY, rest);
        return NormalizeVectorOrFallback(axis, Vector3.UnitY);
    }

    /// <summary>
    /// Computes the local rotation of the elevation ring for a registered
    /// simulation angle in degrees. The axis is re-derived from the rest rotation
    /// so arbitrary and mirrored rest frames keep the same +up convention.
    /// </summary>
    public static Quaternion ComputeElevationRotation(
        Quaternion restRotation,
        float angleDegrees)
    {
        if (!TryNormalize(restRotation, out Quaternion rest))
        {
            return Quaternion.Identity;
        }

        if (!float.IsFinite(angleDegrees))
        {
            return rest;
        }

        Vector3 axis = ComputeElevationAxis(rest);
        Quaternion delta = Quaternion.CreateFromAxisAngle(
            axis,
            DegreesToRadians(angleDegrees));

        return Quaternion.Normalize(delta * rest);
    }

    /// <summary>
    /// Mirrors a rotation across the plane whose normal is local/world X.
    ///
    /// For a reflection matrix S = diag(-1, 1, 1), a rotation R maps to S*R*S.
    /// In quaternion components that is (x, y, z, w) -> (x, -y, -z, w).
    /// A mirrored elevation assembly at the same pitch angle must produce the
    /// mirrored pose; this helper is used to test that parity.
    /// </summary>
    public static Quaternion MirrorAcrossXPlane(Quaternion rotation)
    {
        if (!TryNormalize(rotation, out Quaternion normalized))
        {
            return Quaternion.Identity;
        }

        return new Quaternion(
            normalized.X,
            -normalized.Y,
            -normalized.Z,
            normalized.W);
    }

    public static Vector3 MirrorAcrossXPlane(Vector3 value)
    {
        return new Vector3(-value.X, value.Y, value.Z);
    }

    /// <summary>
    /// Signed angle, in degrees in [-180, 180], from <paramref name="restForwards"/>
    /// to <paramref name="targetDirection"/> after projecting both onto the plane
    /// perpendicular to <paramref name="axis"/>. The sign is consistent with
    /// <see cref="Quaternion.CreateFromAxisAngle"/> and therefore with
    /// <see cref="ComputeElevationRotation"/>.
    ///
    /// Returns NaN for non-finite input or for a degenerate projection.
    /// </summary>
    public static float SignedAngleInPlane(
        Vector3 targetDirection,
        Vector3 restForwards,
        Vector3 axis)
    {
        if (!IsFinite(targetDirection) ||
            !IsFinite(restForwards) ||
            !IsFinite(axis))
        {
            return float.NaN;
        }

        Vector3 normalizedAxis = NormalizeVectorOrFallback(axis, Vector3.Zero);
        if (normalizedAxis == Vector3.Zero)
        {
            return float.NaN;
        }

        Vector3 forwards = restForwards -
            normalizedAxis * Vector3.Dot(restForwards, normalizedAxis);
        Vector3 target = targetDirection -
            normalizedAxis * Vector3.Dot(targetDirection, normalizedAxis);

        if (forwards.LengthSquared() <= MinimumLengthSquared ||
            target.LengthSquared() <= MinimumLengthSquared)
        {
            return float.NaN;
        }

        forwards = Vector3.Normalize(forwards);
        target = Vector3.Normalize(target);

        float sine = Vector3.Dot(Vector3.Cross(forwards, target), normalizedAxis);
        float cosine = Vector3.Dot(forwards, target);

        return RadiansToDegrees(MathF.Atan2(sine, cosine));
    }

    private static Vector3 NormalizeVectorOrFallback(Vector3 value, Vector3 fallback)
    {
        if (!IsFinite(value))
        {
            return fallback;
        }

        float lengthSquared = value.LengthSquared();
        if (lengthSquared <= MinimumLengthSquared)
        {
            return fallback;
        }

        return value / MathF.Sqrt(lengthSquared);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z);
    }
}
