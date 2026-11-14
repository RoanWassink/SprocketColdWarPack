using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SprocketExpedition.Game;

internal sealed record SpawnArcAnchor(Vector2 Position, Vector2 Forward);

// Geometry only. The caller validates terrain without moving any native marker.
// Check every possible friendly marker so whichever one the native player uses,
// every enemy remains inside the advertised front half-annulus.
internal static class CampaignSpawnArcPlanner
{
    internal const float MinimumDistance = 350f;
    internal const float MaximumDistance = 500f;
    internal const float MinimumEnemySpacing = 24f;
    private const int AttemptsPerMarker = 2048;

    internal static bool TryPlan(IReadOnlyList<SpawnArcAnchor> friendlies, int count,
        string identity, Func<int, Vector2, bool> terrainClear, out Vector2[] positions)
        => TryPlan(friendlies, count, identity, terrainClear, out positions, out _);

    internal static bool TryPlan(IReadOnlyList<SpawnArcAnchor> friendlies, int count,
        string identity, Func<int, Vector2, bool> terrainClear, out Vector2[] positions, out string diagnostics,
        int maximumTerrainProbes = 4096, CampaignSpawnRange? range = null)
    {
        positions = Array.Empty<Vector2>();
        diagnostics = "invalid anchors or marker count";
        var distances = range ?? CampaignSpawnRange.Default;
        if (!distances.Valid) return false;
        if (friendlies.Count == 0 || count is < 1 or > 128 || maximumTerrainProbes < 1
            || friendlies.Any(a => !Finite(a.Position) || !Finite(a.Forward) || a.Forward.LengthSquared() < 0.01f)) return false;
        var center = friendlies.Aggregate(Vector2.Zero, (sum, a) => sum + a.Position) / friendlies.Count;
        var forward = Vector2.Normalize(friendlies[0].Forward);
        var right = new Vector2(forward.Y, -forward.X);
        uint state = BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        if (state == 0) state = 0x6d2b79f5;
        float Next()
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (float)(state / 4294967296d);
        }
        var terrainProbes = 0;
        var geometryRejects = 0;
        var spacingRejects = 0;
        var terrainRejects = 0;
        var best = 0;
        string Report() => $"placed={best}/{count}, geometryRejects={geometryRejects}, spacingRejects={spacingRejects}, terrainProbes={terrainProbes}, terrainRejects={terrainRejects}";
        // Rebuild a bounded number of times: early random points can otherwise
        // crowd a valid narrow annulus and strand the final few native markers.
        for (var layoutAttempt = 0; layoutAttempt < 8; layoutAttempt++)
        {
            var proposed = new List<Vector2>(count);
            var complete = true;
            for (var index = 0; index < count; index++)
            {
                var found = false;
                for (var attempt = 0; attempt < AttemptsPerMarker; attempt++)
                {
                    var angle = (Next() - 0.5f) * MathF.PI;
                    var radius = distances.Minimum + Next() * (distances.Maximum - distances.Minimum);
                    var point = center + (forward * MathF.Cos(angle) + right * MathF.Sin(angle)) * radius;
                    if (friendlies.Any(a =>
                    {
                        var delta = point - a.Position;
                        var distance = delta.LengthSquared();
                        return distance < distances.Minimum * distances.Minimum || distance > distances.Maximum * distances.Maximum
                            || Vector2.Dot(delta, a.Forward) < 0f;
                    })) { geometryRejects++; continue; }
                    if (proposed.Any(other => Vector2.DistanceSquared(point, other) < MinimumEnemySpacing * MinimumEnemySpacing))
                    { spacingRejects++; continue; }
                    if (++terrainProbes > maximumTerrainProbes) { diagnostics = Report() + ", terrain limit"; return false; }
                    if (!terrainClear(index, point)) { terrainRejects++; continue; }
                    proposed.Add(point);
                    best = Math.Max(best, proposed.Count);
                    found = true;
                    break;
                }
                if (!found) { complete = false; break; }
            }
            if (complete) { positions = proposed.ToArray(); diagnostics = Report(); return true; }
        }
        diagnostics = Report() + ", layout attempt limit";
        return false;
    }
    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
