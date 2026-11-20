using System.Numerics;

namespace SprocketFloppyParts;

internal sealed record RigidColliderSample(
    int AnchorIndex,
    Vector3 MinWorld,
    Vector3 MaxWorld,
    Vector3[]? WorldSamplePoints,
    int[]? Triangles,
    string AnchorName = "");

internal sealed record PinResult(
    bool[] IsPinned,
    int[] AnchorIndex,
    Vector3[] AnchorLocalPos,
    float[] DistanceFromPin,
    bool FellBackToTopEdge,
    int PinnedCount,
    int UsedAnchorCount);

internal static class PinSolver
{
    internal static PinResult Solve(
        Vector3[] worldPositions,
        SheetEdge[] edges,
        DirectedEdge[] boundaryEdges,
        int[] islandIds,
        int islandCount,
        PinMode mode,
        float pinRadiusM,
        Matrix4x4[] anchorWorldToLocal,
        RigidColliderSample[]? rigidColliders = null)
    {
        int n = worldPositions.Length;
        var isPinned = new bool[n];
        var anchorIndex = new int[n];
        var anchorLocal = new Vector3[n];
        bool fellBack = false;

        float radius = float.IsFinite(pinRadiusM) ? Math.Clamp(pinRadiusM, 0.005f, 0.5f) : 0.045f;

        if (n == 0)
            return new PinResult(isPinned, anchorIndex, anchorLocal, Array.Empty<float>(), false, 0, 0);

        switch (mode)
        {
            case PinMode.Auto:
            {
                int hits = PinAuto(worldPositions, radius, rigidColliders, isPinned, anchorIndex);
                if (hits == 0)
                {
                    fellBack = true;
                    PinTopEdge(worldPositions, islandIds, islandCount, radius, isPinned, anchorIndex);
                }
                break;
            }
            case PinMode.TopEdge:
                PinTopEdge(worldPositions, islandIds, islandCount, radius, isPinned, anchorIndex);
                break;
            case PinMode.BottomEdge:
                PinBottomEdge(worldPositions, islandIds, islandCount, radius, isPinned, anchorIndex);
                break;
            case PinMode.TopAndBottom:
                PinTopAndBottom(worldPositions, islandIds, islandCount, radius, isPinned, anchorIndex);
                break;
            case PinMode.LeftAndRight:
                PinLeftAndRight(worldPositions, islandIds, islandCount, radius, isPinned, anchorIndex);
                break;
            case PinMode.Corners:
                PinCorners(worldPositions, islandIds, islandCount, radius, isPinned, anchorIndex);
                break;
            case PinMode.Border:
            {
                if (boundaryEdges.Length == 0)
                {
                    fellBack = true;
                    PinTopEdge(worldPositions, islandIds, islandCount, radius, isPinned, anchorIndex);
                }
                else
                {
                    for (int i = 0; i < boundaryEdges.Length; i++)
                    {
                        isPinned[boundaryEdges[i].A] = true;
                        isPinned[boundaryEdges[i].B] = true;
                    }
                }
                break;
            }
            default:
                PinTopEdge(worldPositions, islandIds, islandCount, radius, isPinned, anchorIndex);
                break;
        }

        int pinnedCount = 0;
        var usedAnchors = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            if (!isPinned[i]) continue;
            pinnedCount++;
            int aIdx = anchorIndex[i];
            if (aIdx < 0 || aIdx >= anchorWorldToLocal.Length) aIdx = 0;
            anchorIndex[i] = aIdx;
            usedAnchors.Add(aIdx);
            anchorLocal[i] = Vector3.Transform(worldPositions[i], anchorWorldToLocal[aIdx]);
        }

        var distFromPin = ComputeGeodesicDistances(n, worldPositions, edges, isPinned);
        return new PinResult(
            isPinned,
            anchorIndex,
            anchorLocal,
            distFromPin,
            fellBack,
            pinnedCount,
            usedAnchors.Count);
    }

    private static int PinAuto(
        Vector3[] worldPositions,
        float radius,
        RigidColliderSample[]? colliders,
        bool[] isPinned,
        int[] anchorIndex)
    {
        if (colliders == null || colliders.Length == 0) return 0;
        int pinned = PinAutoPass(worldPositions, radius, colliders, sampledOnly: false, isPinned, anchorIndex);

        if (pinned * 10 >= worldPositions.Length * 9 &&
            colliders.Any(c => c.WorldSamplePoints != null))
        {
            var retryPinned = new bool[worldPositions.Length];
            var retryAnchor = new int[worldPositions.Length];
            int retryCount = PinAutoPass(worldPositions, radius, colliders, sampledOnly: true, retryPinned, retryAnchor);
            if (retryCount > 0 && retryCount * 10 < worldPositions.Length * 9)
            {
                Array.Copy(retryPinned, isPinned, worldPositions.Length);
                Array.Copy(retryAnchor, anchorIndex, worldPositions.Length);
                return retryCount;
            }
        }

        return pinned;
    }

    private static int PinAutoPass(
        Vector3[] worldPositions,
        float radius,
        RigidColliderSample[] colliders,
        bool sampledOnly,
        bool[] isPinned,
        int[] anchorIndex)
    {
        float radiusSq = radius * radius;
        int pinned = 0;

        for (int i = 0; i < worldPositions.Length; i++)
        {
            var p = worldPositions[i];
            float bestDistSq = radiusSq;
            int bestAnchor = -1;

            for (int c = 0; c < colliders.Length; c++)
            {
                var col = colliders[c];
                if (col.AnchorIndex <= 0) continue;
                if (sampledOnly && col.WorldSamplePoints == null) continue;
                if (p.X < col.MinWorld.X - radius || p.X > col.MaxWorld.X + radius ||
                    p.Y < col.MinWorld.Y - radius || p.Y > col.MaxWorld.Y + radius ||
                    p.Z < col.MinWorld.Z - radius || p.Z > col.MaxWorld.Z + radius)
                    continue;

                float dSq = DistanceSquaredToCollider(p, col, bestDistSq);
                if (dSq <= bestDistSq)
                {
                    bestDistSq = dSq;
                    bestAnchor = col.AnchorIndex;
                }
            }

            if (bestAnchor > 0)
            {
                isPinned[i] = true;
                anchorIndex[i] = bestAnchor;
                pinned++;
            }
        }

        return pinned;
    }

    private static float DistanceSquaredToCollider(Vector3 p, RigidColliderSample col, float limitSq)
    {
        var pts = col.WorldSamplePoints;
        var tris = col.Triangles;
        if (pts != null && tris != null && tris.Length >= 3)
        {
            float best = float.PositiveInfinity;
            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];
                if ((uint)i0 >= (uint)pts.Length || (uint)i1 >= (uint)pts.Length || (uint)i2 >= (uint)pts.Length)
                    continue;
                var closest = ClosestPointOnTriangle(p, pts[i0], pts[i1], pts[i2]);
                float d = Vector3.DistanceSquared(p, closest);
                if (d < best)
                {
                    best = d;
                    if (best <= 1e-10f) break;
                }
            }
            return best;
        }

        if (pts != null && pts.Length > 0)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < pts.Length; i++)
            {
                float d = Vector3.DistanceSquared(p, pts[i]);
                if (d < best) best = d;
            }
            return best;
        }

        _ = limitSq;
        if (p.X >= col.MinWorld.X && p.X <= col.MaxWorld.X &&
            p.Y >= col.MinWorld.Y && p.Y <= col.MaxWorld.Y &&
            p.Z >= col.MinWorld.Z && p.Z <= col.MaxWorld.Z)
        {
            float d = MathF.Min(
                MathF.Min(p.X - col.MinWorld.X, col.MaxWorld.X - p.X),
                MathF.Min(
                    MathF.Min(p.Y - col.MinWorld.Y, col.MaxWorld.Y - p.Y),
                    MathF.Min(p.Z - col.MinWorld.Z, col.MaxWorld.Z - p.Z)));
            return d * d;
        }
        var clamped = Vector3.Clamp(p, col.MinWorld, col.MaxWorld);
        return Vector3.DistanceSquared(p, clamped);
    }

    private static void PinTopEdge(
        Vector3[] pos,
        int[] islands,
        int islandCount,
        float radius,
        bool[] isPinned,
        int[] anchorIndex)
    {
        _ = radius;
        PinVerticalEdge(pos, islands, islandCount, pinTop: true, isPinned, anchorIndex);
    }

    private static void PinBottomEdge(
        Vector3[] pos,
        int[] islands,
        int islandCount,
        float radius,
        bool[] isPinned,
        int[] anchorIndex)
    {
        _ = radius;
        PinVerticalEdge(pos, islands, islandCount, pinTop: false, isPinned, anchorIndex);
    }

    private static void PinVerticalEdge(
        Vector3[] pos,
        int[] islands,
        int islandCount,
        bool pinTop,
        bool[] isPinned,
        int[] anchorIndex)
    {
        for (int isl = 0; isl < Math.Max(1, islandCount); isl++)
        {
            float maxY = float.NegativeInfinity, minY = float.PositiveInfinity;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            for (int i = 0; i < pos.Length; i++)
            {
                if (islandCount > 0 && islands[i] != isl) continue;
                var p = pos[i];
                if (p.Y > maxY) maxY = p.Y; if (p.Y < minY) minY = p.Y;
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
            if (!float.IsFinite(maxY)) continue;

            float span = maxY - minY;
            if (span < 0.02f)
            {
                bool useX = (maxX - minX) >= (maxZ - minZ);
                float minH = useX ? minX : minZ;
                float maxH = useX ? maxX : maxZ;
                float hSpan = maxH - minH;
                float hTol = MathF.Max(0.01f, hSpan * 0.025f);
                for (int i = 0; i < pos.Length; i++)
                {
                    if (islandCount > 0 && islands[i] != isl) continue;
                    float h = useX ? pos[i].X : pos[i].Z;
                    if (pinTop ? (h >= maxH - hTol) : (h <= minH + hTol))
                    {
                        isPinned[i] = true;
                        anchorIndex[i] = 0;
                    }
                }
                continue;
            }

            float tol = MathF.Max(0.008f, span * 0.025f);
            for (int i = 0; i < pos.Length; i++)
            {
                if (islandCount > 0 && islands[i] != isl) continue;
                if (pinTop ? (pos[i].Y >= maxY - tol) : (pos[i].Y <= minY + tol))
                {
                    isPinned[i] = true;
                    anchorIndex[i] = 0;
                }
            }
        }
    }

    private static void PinTopAndBottom(
        Vector3[] pos,
        int[] islands,
        int islandCount,
        float radius,
        bool[] isPinned,
        int[] anchorIndex)
    {
        PinTopEdge(pos, islands, islandCount, radius, isPinned, anchorIndex);
        PinBottomEdge(pos, islands, islandCount, radius, isPinned, anchorIndex);
    }

    private static void PinLeftAndRight(
        Vector3[] pos,
        int[] islands,
        int islandCount,
        float radius,
        bool[] isPinned,
        int[] anchorIndex)
    {
        _ = radius;
        for (int isl = 0; isl < Math.Max(1, islandCount); isl++)
        {
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            for (int i = 0; i < pos.Length; i++)
            {
                if (islandCount > 0 && islands[i] != isl) continue;
                var p = pos[i];
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
            if (!float.IsFinite(minX)) continue;
            bool useX = (maxX - minX) >= (maxZ - minZ);
            float minU = useX ? minX : minZ;
            float maxU = useX ? maxX : maxZ;
            float tol = MathF.Max(0.008f, (maxU - minU) * 0.025f);
            for (int i = 0; i < pos.Length; i++)
            {
                if (islandCount > 0 && islands[i] != isl) continue;
                float u = useX ? pos[i].X : pos[i].Z;
                if (u <= minU + tol || u >= maxU - tol)
                {
                    isPinned[i] = true;
                    anchorIndex[i] = 0;
                }
            }
        }
    }

    private static void PinCorners(
        Vector3[] pos,
        int[] islands,
        int islandCount,
        float radius,
        bool[] isPinned,
        int[] anchorIndex)
    {
        Span<int> corners = stackalloc int[4];
        for (int isl = 0; isl < Math.Max(1, islandCount); isl++)
        {
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            for (int i = 0; i < pos.Length; i++)
            {
                if (islandCount > 0 && islands[i] != isl) continue;
                var p = pos[i];
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
            if (!float.IsFinite(minX)) continue;
            bool useX = (maxX - minX) >= (maxZ - minZ);
            float uSpan = useX ? (maxX - minX) : (maxZ - minZ);
            float span = MathF.Max(uSpan, maxY - minY);
            float effRadius = MathF.Max(0.015f, MathF.Min(radius, span * 0.08f));
            float radSq = effRadius * effRadius;

            // Find the 4 extreme vertices under (+-u +- y) in the island.
            int c0 = -1, c1 = -1, c2 = -1, c3 = -1;
            float s0 = float.NegativeInfinity, s1 = float.NegativeInfinity;
            float s2 = float.NegativeInfinity, s3 = float.NegativeInfinity;
            for (int i = 0; i < pos.Length; i++)
            {
                if (islandCount > 0 && islands[i] != isl) continue;
                float u = useX ? pos[i].X : pos[i].Z;
                float y = pos[i].Y;
                if (-u + y > s0) { s0 = -u + y; c0 = i; }
                if (u + y > s1) { s1 = u + y; c1 = i; }
                if (-u - y > s2) { s2 = -u - y; c2 = i; }
                if (u - y > s3) { s3 = u - y; c3 = i; }
            }

            corners[0] = c0; corners[1] = c1; corners[2] = c2; corners[3] = c3;
            for (int k = 0; k < corners.Length; k++)
            {
                int ci = corners[k];
                if (ci < 0) continue;
                var cp = pos[ci];
                for (int i = 0; i < pos.Length; i++)
                {
                    if (islandCount > 0 && islands[i] != isl) continue;
                    if (Vector3.DistanceSquared(pos[i], cp) <= radSq)
                    {
                        isPinned[i] = true;
                        anchorIndex[i] = 0;
                    }
                }
            }
        }
    }

    private static float[] ComputeGeodesicDistances(
        int n,
        Vector3[] positions,
        SheetEdge[] edges,
        bool[] isPinned)
    {
        var dist = new float[n];
        Array.Fill(dist, float.PositiveInfinity);

        var adj = new List<(int Next, float Len)>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<(int, float)>(4);
        for (int i = 0; i < edges.Length; i++)
        {
            var e = edges[i];
            adj[e.A].Add((e.B, e.RestLength));
            adj[e.B].Add((e.A, e.RestLength));
        }

        var pq = new PriorityQueue<int, float>();
        for (int i = 0; i < n; i++)
        {
            if (isPinned[i])
            {
                dist[i] = 0f;
                pq.Enqueue(i, 0f);
            }
        }

        while (pq.Count > 0)
        {
            pq.TryDequeue(out int u, out float d);
            if (d > dist[u]) continue;
            var neighbors = adj[u];
            for (int k = 0; k < neighbors.Count; k++)
            {
                var (v, len) = neighbors[k];
                float nd = d + len;
                if (nd < dist[v])
                {
                    dist[v] = nd;
                    pq.Enqueue(v, nd);
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (float.IsFinite(dist[i])) continue;
            float best = float.PositiveInfinity;
            for (int j = 0; j < n; j++)
            {
                if (!isPinned[j]) continue;
                float d = Vector3.Distance(positions[i], positions[j]);
                if (d < best) best = d;
            }
            dist[i] = float.IsFinite(best) ? best : 0f;
        }

        return dist;
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + ab * (d1 / (d1 - d3));

        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = va + vb + vc;
        if (MathF.Abs(denom) < 1e-20f) return a;
        return a + ab * (vb / denom) + ac * (vc / denom);
    }
}
