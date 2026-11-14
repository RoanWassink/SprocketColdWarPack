using System.Numerics;

namespace SprocketExpedition.Game;

internal readonly record struct SpawnGroundSample(Vector3 Point, Vector3 Normal, bool HasTerrain);

// Conservative terrain-support test for a relocated spawn marker. The footprint
// is a rotation-independent circle-sized sample: a 7 m half extent covers the
// diagonal of the earlier 11 x 7 m envelope (sqrt(5.5^2 + 3.5^2) ~= 6.52 m) at
// any hull yaw, because native assembled hulls may face an arbitrary heading.
// Native dimensions are not read here, so this is a safety envelope rather than
// a measured tank hull.
internal static class CampaignSpawnSupport
{
    internal const float HalfExtent = 7f;
    internal const float MaximumSlopeDegrees = 12f;
    // A 14 m footprint at the allowed 12 degree slope can legitimately vary by
    // ~3 m. A fixed 1 m delta silently reduced the real limit to ~4 degrees and
    // rejected ordinary rolling dune patches. Derive the height allowance from
    // the same footprint and slope gate instead.
    internal static readonly float MaximumHeightDelta =
        2f * HalfExtent * MathF.Tan(MaximumSlopeDegrees * MathF.PI / 180f);

    internal static readonly Vector2[] Footprint = BuildFootprint();

    private static Vector2[] BuildFootprint()
    {
        var offsets = new Vector2[9];
        offsets[0] = Vector2.Zero;
        for (var index = 0; index < 8; index++)
        {
            var angle = index * MathF.PI / 4f;
            offsets[index + 1] = new Vector2(
                MathF.Cos(angle) * HalfExtent, MathF.Sin(angle) * HalfExtent);
        }
        return offsets;
    }

    internal static bool TryEvaluate(
        IReadOnlyList<SpawnGroundSample> samples,
        out Vector3 center,
        out string error)
    {
        center = default;
        error = string.Empty;
        if (samples is null || samples.Count != Footprint.Length)
        {
            error = "spawn footprint sample count is incomplete";
            return false;
        }

        var minimumHeight = float.PositiveInfinity;
        var maximumHeight = float.NegativeInfinity;
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            if (!sample.HasTerrain)
            {
                error = $"spawn footprint sample {i + 1} has no terrain";
                return false;
            }
            if (!Finite(sample.Point) || !Finite(sample.Normal)
                || sample.Normal.LengthSquared() < .01f)
            {
                error = $"spawn footprint sample {i + 1} is not finite";
                return false;
            }

            var normal = Vector3.Normalize(sample.Normal);
            var upDot = Math.Clamp(Vector3.Dot(normal, Vector3.UnitY), -1f, 1f);
            var slope = MathF.Acos(upDot) * (180f / MathF.PI);
            if (!float.IsFinite(slope) || slope > MaximumSlopeDegrees)
            {
                error = $"spawn footprint sample {i + 1} slope {slope:N1} deg exceeds {MaximumSlopeDegrees:N0} deg";
                return false;
            }

            minimumHeight = Math.Min(minimumHeight, sample.Point.Y);
            maximumHeight = Math.Max(maximumHeight, sample.Point.Y);
            if (i == 0) center = sample.Point;
        }

        if (!float.IsFinite(minimumHeight) || !float.IsFinite(maximumHeight)
            || maximumHeight - minimumHeight > MaximumHeightDelta)
        {
            error = $"spawn footprint height delta {maximumHeight - minimumHeight:N2} m exceeds {MaximumHeightDelta:N1} m";
            return false;
        }

        return true;
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
