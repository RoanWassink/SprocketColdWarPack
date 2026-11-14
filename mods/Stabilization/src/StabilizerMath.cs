using System;
using System.Numerics;

namespace SprocketStabilization;

/// <summary>
/// Pure stabilization math, kept free of Unity/IL2CPP types so it can be
/// regression-tested locally.
///
/// The native aim controllers follow a world-space target direction. The aim
/// target produced by the player camera rotates with the vehicle hull while the
/// player holds a steady aim. To hold the gun on the world direction, the
/// incoming world direction is expressed in the hull frame: as the hull rotates,
/// the incoming direction rotates with it, so the hull-frame direction stays
/// constant. The mechanisms then track that constant world direction.
/// </summary>
public static class StabilizerMath
{
    private const float MinimumLengthSquared = 1e-12f;

    public static bool TryNormalize(Vector3 value, out Vector3 normalized)
    {
        normalized = Vector3.Zero;

        float lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= MinimumLengthSquared)
        {
            return false;
        }

        normalized = value / MathF.Sqrt(lengthSquared);
        return true;
    }

    public static Quaternion NormalizeOrIdentity(Quaternion value)
    {
        float lengthSquared =
            value.X * value.X +
            value.Y * value.Y +
            value.Z * value.Z +
            value.W * value.W;

        if (!float.IsFinite(lengthSquared) || lengthSquared <= MinimumLengthSquared)
        {
            return Quaternion.Identity;
        }

        float scale = 1f / MathF.Sqrt(lengthSquared);
        return new Quaternion(
            value.X * scale,
            value.Y * scale,
            value.Z * scale,
            value.W * scale);
    }

    /// <summary>
    /// Converts an incoming world-space aim direction into the hull reference
    /// frame. Returns a normalized direction, or zero when the input cannot be
    /// used (the caller then leaves the native direction untouched).
    /// </summary>
    public static Vector3 CompensateDirection(Vector3 worldDirection, Quaternion hullWorldRotation)
    {
        if (!TryNormalize(worldDirection, out Vector3 direction))
        {
            return Vector3.Zero;
        }

        Quaternion hull = NormalizeOrIdentity(hullWorldRotation);
        Vector3 local = Vector3.Transform(direction, Quaternion.Inverse(hull));
        return TryNormalize(local, out Vector3 normalized) ? normalized : Vector3.Zero;
    }

    /// <summary>
    /// The stabilizer may only act when the master switch and the dated
    /// technology both allow it.
    /// </summary>
    public static bool AcceptsEdit(bool masterEnabled, bool technologyEnabled)
    {
        return masterEnabled && technologyEnabled;
    }

    public static float AngleDegrees(Vector3 from, Vector3 to)
    {
        if (!TryNormalize(from, out Vector3 a) || !TryNormalize(to, out Vector3 b))
        {
            return float.NaN;
        }

        float dot = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);
        return (float)(Math.Acos(dot) * (180.0 / Math.PI));
    }
}
