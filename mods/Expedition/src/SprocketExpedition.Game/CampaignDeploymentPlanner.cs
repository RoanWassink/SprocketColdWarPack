using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SprocketExpedition.Game;

// Whole-friendly-formation proposals only. Native terrain/bounds and the complete
// opposing layout must pass before the adapter moves any marker.
internal static class CampaignDeploymentPlanner
{
    internal const int OriginalCandidate = 11;
    internal const int RotationCandidate = 10;
    internal static IEnumerable<SpawnArcAnchor[]> Candidates(IReadOnlyList<SpawnArcAnchor> original, string identity)
    {
        if (original.Count == 0 || original.Any(a => !Finite(a.Position) || !Finite(a.Forward)
            || a.Forward.LengthSquared() < .01f)) yield break;
        var center = original.Aggregate(Vector2.Zero, (sum, a) => sum + a.Position) / original.Count;
        uint state = BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|deployment.v2")));
        if (state == 0) state = 0x6d2b79f5;
        float Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (float)(state / 4294967296d); }
        var initialBearing = Next() * 2f * MathF.PI;
        for (var attempt = 0; attempt < OriginalCandidate; attempt++)
        {
            // Cover eight distinct directions before accepting a nearer or
            // rotation-only fallback on a constrained map.
            var bearing = initialBearing + (attempt % 8 + Next() * .5f) * MathF.PI / 4f;
            var radius = attempt == RotationCandidate ? 0f
                : attempt < 8 ? 250f + Next() * 400f : 125f + Next() * 125f;
            var shift = new Vector2(MathF.Sin(bearing), MathF.Cos(bearing)) * radius;
            var yaw = Next() * 2f * MathF.PI;
            Vector2 Rotate(Vector2 v) => new(v.X * MathF.Cos(yaw) - v.Y * MathF.Sin(yaw),
                v.X * MathF.Sin(yaw) + v.Y * MathF.Cos(yaw));
            yield return original.Select(a => new SpawnArcAnchor(center + shift + Rotate(a.Position - center),
                Vector2.Normalize(Rotate(a.Forward)))).ToArray();
        }
        // A safe fallback is useful on small/steep maps. Retry never rerolls it.
        yield return original.Select(a => new SpawnArcAnchor(a.Position, a.Forward)).ToArray();
    }

    private static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}
