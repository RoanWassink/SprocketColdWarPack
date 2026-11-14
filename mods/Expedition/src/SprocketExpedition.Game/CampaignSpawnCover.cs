using System.Numerics;

namespace SprocketExpedition.Game;

internal static class CampaignSpawnCover
{
    // Multiple parallel hull/turret rays reduce false cover from a narrow pole
    // or a low rock. This samples a tank-sized envelope, not full mesh visibility.
    internal static bool Occluded(IReadOnlyList<Vector3> friendlyGround, Vector3 enemyGround,
        Func<Vector3, Vector3, bool> blocked)
    {
        if (friendlyGround.Count == 0) return false;
        foreach (var friendly in friendlyGround)
        {
            var delta = enemyGround - friendly;
            delta.Y = 0;
            if (delta.LengthSquared() < 1) return false;
            var right = Vector3.Normalize(new Vector3(delta.Z, 0, -delta.X));
            // 10 m is a conservative third-person/AI camera-relative eye line;
            // the lower rays represent hull and turret silhouettes.
            foreach (var height in new[] { 10f, 4f, 2.5f, 1.2f })
                foreach (var offset in new[] { 0f, -3f, 3f })
                {
                    var shift = Vector3.UnitY * height + right * offset;
                    if (!blocked(friendly + shift, enemyGround + shift)) return false;
                }
        }
        return true;
    }

    internal static bool TryPlan(IReadOnlyList<SpawnArcAnchor> anchors, int count, string identity,
        Func<int, Vector2, bool> groundClear, Func<int, Vector2, bool> covered,
        out Vector2[] positions, out bool withCover, out string diagnostics, int maximumOpenTerrainProbes = 4096,
        CampaignSpawnRange? range = null)
    {
        withCover = CampaignSpawnArcPlanner.TryPlan(anchors, count, identity + "|cover",
            (index, point) => groundClear(index, point) && covered(index, point),
            out positions, out var coverReport, maximumTerrainProbes: 512, range: range);
        if (withCover) { diagnostics = "covered: " + coverReport; return true; }
        var success = CampaignSpawnArcPlanner.TryPlan(anchors, count, identity + "|open",
            groundClear, out positions, out var openReport, maximumTerrainProbes: maximumOpenTerrainProbes, range: range);
        diagnostics = "cover search: " + coverReport + "; open fallback: " + openReport;
        return success;
    }

    internal static int[] FriendlyIndices(IReadOnlyList<Vector2> nativePositions, int requested)
    {
        if (requested < 1 || requested > nativePositions.Count) throw new ArgumentOutOfRangeException(nameof(requested));
        var center = nativePositions.Aggregate(Vector2.Zero, (sum, p) => sum + p) / nativePositions.Count;
        // A central selection can straddle a large gap in irregular map rows.
        // Prefer the tightest authored cluster; centre proximity breaks ties.
        return Enumerable.Range(0, nativePositions.Count).Select(anchor =>
        {
            var indices = Enumerable.Range(0, nativePositions.Count)
                .OrderBy(i => Vector2.DistanceSquared(nativePositions[i], nativePositions[anchor])).ThenBy(i => i)
                .Take(requested).ToArray();
            var diameter = indices.Max(i => indices.Max(j => Vector2.DistanceSquared(nativePositions[i], nativePositions[j])));
            var clusterCenter = indices.Aggregate(Vector2.Zero, (sum, i) => sum + nativePositions[i]) / requested;
            return new { Indices = indices, Diameter = diameter, CenterDistance = Vector2.DistanceSquared(clusterCenter, center), Anchor = anchor };
        }).OrderBy(c => c.Diameter).ThenBy(c => c.CenterDistance).ThenBy(c => c.Anchor)
            .First().Indices.OrderBy(i => i).ToArray();
    }
}
