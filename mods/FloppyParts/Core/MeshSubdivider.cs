using System.Numerics;

namespace SprocketFloppyParts;

internal readonly record struct BendingPair(int C, int D, float RestDistance);

internal readonly record struct DirectedEdge(int A, int B);

internal sealed record ProxySkin(
    Vector3[] ProxyPositions,
    Vector3[] ProxyNormals,
    int[] ProxyTriangles,
    SheetEdge[] ProxyEdges,
    BendingPair[] ProxyBendingPairs,
    int[] ProxyIslandIds,
    int ProxyIslandCount,
    int[] VertexProxy0,
    int[] VertexProxy1,
    int[] VertexProxy2,
    float[] VertexWeight0,
    float[] VertexWeight1,
    float[] VertexWeight2);

internal sealed record SubdividedSheet(
    Vector3[] Positions,
    Vector3[] Normals,
    Vector2[] Uvs,
    float[] BackOffset,
    int[] Triangles,
    SheetEdge[] Edges,
    DirectedEdge[] BoundaryEdges,
    BendingPair[] BendingPairs,
    int[] IslandIds,
    int IslandCount,
    ProxySkin? Proxy);

internal static class MeshSubdivider
{
    internal const float MaxEdgeMeters = 0.15f;
    internal const int AdaptiveInputCap = 500;
    internal const int AdaptiveOutputCap = 1200;
    internal const int MaxSubdividePasses = 3;
    internal const int ProxyThreshold = 2800;
    internal const int MaxProxyCount = 450;

    internal static SubdividedSheet Build(
        OverlaySurface front,
        OverlaySurface back,
        Vector3 scale,
        float tileSizeM,
        float uvRotationDeg,
        Matrix4x4 mirror,
        bool mirrored,
        MotionProfile motion)
    {
        var absScale = Vector3.Abs(scale);
        if (!Finite(absScale) || MathF.Min(absScale.X, MathF.Min(absScale.Y, absScale.Z)) < 1e-6f)
            throw new InvalidOperationException("Invalid plate scale.");
        if (!float.IsFinite(tileSizeM) || tileSizeM < 0.01f)
            throw new InvalidOperationException("Invalid tile size.");
        if (!float.IsFinite(uvRotationDeg))
            throw new InvalidOperationException("Invalid UV rotation.");

        var workFront = mirrored ? CombineMirrored(front, mirror) : front;
        var workBack = mirrored ? CombineMirrored(back, mirror) : back;

        var welded = WeldedPlateGeometry.BuildWeldedSheet(workFront, workBack);
        if (welded.Positions.Length < 3 || welded.Triangles.Length < 3)
            throw new InvalidOperationException("Degenerate sheet geometry.");

        var positions = new List<Vector3>(welded.Positions);
        var normals = new List<Vector3>(welded.Normals);
        var backOffset = new float[welded.Positions.Length];

        if (workBack.Vertices.Length > 0)
        {
            for (int i = 0; i < welded.Positions.Length; i++)
            {
                var p = welded.Positions[i];
                var n = welded.Normals[i];
                float bestSq = float.PositiveInfinity;
                var bestBack = p;
                for (int j = 0; j < workBack.Vertices.Length; j++)
                {
                    float dSq = Vector3.DistanceSquared(p, workBack.Vertices[j]);
                    if (dSq < bestSq)
                    {
                        bestSq = dSq;
                        bestBack = workBack.Vertices[j];
                    }
                }
                float dist = float.IsFinite(bestSq) ? MathF.Sqrt(bestSq) : 0f;
                float dot = Vector3.Dot(bestBack - p, n);
                backOffset[i] = dot < 0f ? -dist : dist;
            }
        }

        var offsets = new List<float>(backOffset);
        var triangles = new List<int>(welded.Triangles);

        if ((motion == MotionProfile.Cloth || motion == MotionProfile.Rubber) &&
            positions.Count < AdaptiveInputCap)
        {
            SubdivideAdaptive(positions, normals, offsets, triangles, absScale);
        }

        var posArr = positions.ToArray();
        var normArr = normals.ToArray();
        var offArr = offsets.ToArray();
        var triArr = triangles.ToArray();
        var uvArr = ComputeBoxUvs(posArr, normArr, absScale, tileSizeM, uvRotationDeg);

        var edges = ExtractEdges(posArr, triArr, absScale);
        var boundary = FindBoundaryEdges(triArr);
        var bending = FindBendingPairs(posArr, triArr, absScale);
        var (islands, islandCount) = FindIslands(posArr.Length, triArr);

        ProxySkin? proxy = posArr.Length > ProxyThreshold
            ? BuildProxySkin(posArr, normArr, triArr, absScale)
            : null;

        return new SubdividedSheet(
            posArr,
            normArr,
            uvArr,
            offArr,
            triArr,
            edges,
            boundary,
            bending,
            islands,
            islandCount,
            proxy);
    }

    internal static (int[] IslandIds, int IslandCount) FindIslands(int vertexCount, int[] triangles)
    {
        var adj = new List<int>[vertexCount];
        for (int i = 0; i < vertexCount; i++) adj[i] = new List<int>(4);

        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = triangles[t];
            int b = triangles[t + 1];
            int c = triangles[t + 2];
            adj[a].Add(b); adj[b].Add(a);
            adj[b].Add(c); adj[c].Add(b);
            adj[c].Add(a); adj[a].Add(c);
        }

        var islandIds = new int[vertexCount];
        Array.Fill(islandIds, -1);
        var queue = new Queue<int>();
        int count = 0;

        for (int i = 0; i < vertexCount; i++)
        {
            if (islandIds[i] >= 0) continue;
            int id = count++;
            islandIds[i] = id;
            queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                foreach (int v in adj[u])
                {
                    if (islandIds[v] >= 0) continue;
                    islandIds[v] = id;
                    queue.Enqueue(v);
                }
            }
        }

        return (islandIds, count);
    }

    internal static DirectedEdge[] FindBoundaryEdges(int[] triangles)
    {
        var counts = new Dictionary<(int, int), (int Count, DirectedEdge Dir)>();
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int i0 = triangles[t];
            int i1 = triangles[t + 1];
            int i2 = triangles[t + 2];
            CountEdge(i0, i1);
            CountEdge(i1, i2);
            CountEdge(i2, i0);
        }

        var result = new List<DirectedEdge>();
        foreach (var entry in counts.Values)
        {
            if (entry.Count == 1)
                result.Add(entry.Dir);
        }
        return result.ToArray();

        void CountEdge(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (counts.TryGetValue(key, out var cur))
                counts[key] = (cur.Count + 1, cur.Dir);
            else
                counts[key] = (1, new DirectedEdge(a, b));
        }
    }

    internal static BendingPair[] FindBendingPairs(Vector3[] positions, int[] triangles, Vector3 absScale)
    {
        var edgeOpp = new Dictionary<(int, int), List<int>>();
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int i0 = triangles[t];
            int i1 = triangles[t + 1];
            int i2 = triangles[t + 2];
            Record(i0, i1, i2);
            Record(i1, i2, i0);
            Record(i2, i0, i1);
        }

        var result = new List<BendingPair>();
        var seen = new HashSet<(int, int)>();
        foreach (var list in edgeOpp.Values)
        {
            if (list.Count != 2) continue;
            int c = list[0];
            int d = list[1];
            if (c == d) continue;
            int lo = c < d ? c : d;
            int hi = c < d ? d : c;
            if (seen.Add((lo, hi)))
                result.Add(new BendingPair(lo, hi, Vector3.Distance(positions[lo] * absScale, positions[hi] * absScale)));
        }
        return result.ToArray();

        void Record(int a, int b, int opp)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!edgeOpp.TryGetValue(key, out var list))
                edgeOpp[key] = list = new List<int>(2);
            list.Add(opp);
        }
    }

    private static void SubdivideAdaptive(
        List<Vector3> positions,
        List<Vector3> normals,
        List<float> offsets,
        List<int> triangles,
        Vector3 absScale)
    {
        float maxEdgeSq = MaxEdgeMeters * MaxEdgeMeters;

        for (int pass = 0; pass < MaxSubdividePasses; pass++)
        {
            if (positions.Count >= AdaptiveOutputCap) break;

            var midpoints = new Dictionary<(int, int), int>();
            int GetOrSplit(int u, int v)
            {
                var key = u < v ? (u, v) : (v, u);
                if (midpoints.TryGetValue(key, out int existing)) return existing;
                if (positions.Count >= AdaptiveOutputCap) return -1;

                int idx = positions.Count;
                positions.Add((positions[u] + positions[v]) * 0.5f);
                var n = normals[u] + normals[v];
                normals.Add(n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY);
                offsets.Add((offsets[u] + offsets[v]) * 0.5f);
                midpoints[key] = idx;
                return idx;
            }

            // First pass: mark and create midpoints for all edges exceeding MaxEdgeMeters.
            bool anySplit = false;
            for (int t = 0; t < triangles.Count; t += 3)
            {
                int a = triangles[t];
                int b = triangles[t + 1];
                int c = triangles[t + 2];

                float dab = Vector3.DistanceSquared(positions[a] * absScale, positions[b] * absScale);
                float dbc = Vector3.DistanceSquared(positions[b] * absScale, positions[c] * absScale);
                float dca = Vector3.DistanceSquared(positions[c] * absScale, positions[a] * absScale);

                if (dab > maxEdgeSq && GetOrSplit(a, b) >= 0) anySplit = true;
                if (dbc > maxEdgeSq && GetOrSplit(b, c) >= 0) anySplit = true;
                if (dca > maxEdgeSq && GetOrSplit(c, a) >= 0) anySplit = true;
            }

            if (!anySplit) break;

            // Second pass: conformally split every triangle using all created edge midpoints.
            var nextTris = new List<int>(triangles.Count * 2);
            for (int t = 0; t < triangles.Count; t += 3)
            {
                int a = triangles[t];
                int b = triangles[t + 1];
                int c = triangles[t + 2];

                bool sab = midpoints.TryGetValue(a < b ? (a, b) : (b, a), out int mab);
                bool sbc = midpoints.TryGetValue(b < c ? (b, c) : (c, b), out int mbc);
                bool sca = midpoints.TryGetValue(c < a ? (c, a) : (a, c), out int mca);

                int splitCount = (sab ? 1 : 0) + (sbc ? 1 : 0) + (sca ? 1 : 0);
                if (splitCount == 0)
                {
                    nextTris.Add(a); nextTris.Add(b); nextTris.Add(c);
                }
                else if (splitCount == 1)
                {
                    // Rotate so the single split edge is (v0, v1) with midpoint m.
                    int v0, v1, v2, m;
                    if (sab) { v0 = a; v1 = b; v2 = c; m = mab; }
                    else if (sbc) { v0 = b; v1 = c; v2 = a; m = mbc; }
                    else { v0 = c; v1 = a; v2 = b; m = mca; }

                    nextTris.Add(v0); nextTris.Add(m); nextTris.Add(v2);
                    nextTris.Add(m); nextTris.Add(v1); nextTris.Add(v2);
                }
                else if (splitCount == 2)
                {
                    // Rotate so the UNSPLIT edge is (v2, v0); split edges are (v0, v1)=m01 and (v1, v2)=m12.
                    int v0, v1, v2, m01, m12;
                    if (!sca) { v0 = a; v1 = b; v2 = c; m01 = mab; m12 = mbc; }
                    else if (!sab) { v0 = b; v1 = c; v2 = a; m01 = mbc; m12 = mca; }
                    else { v0 = c; v1 = a; v2 = b; m01 = mca; m12 = mab; }

                    nextTris.Add(m01); nextTris.Add(v1); nextTris.Add(m12);
                    float d0 = Vector3.DistanceSquared(positions[v0], positions[m12]);
                    float d1 = Vector3.DistanceSquared(positions[v2], positions[m01]);
                    if (d0 <= d1)
                    {
                        nextTris.Add(v0); nextTris.Add(m01); nextTris.Add(m12);
                        nextTris.Add(v0); nextTris.Add(m12); nextTris.Add(v2);
                    }
                    else
                    {
                        nextTris.Add(v0); nextTris.Add(m01); nextTris.Add(v2);
                        nextTris.Add(m01); nextTris.Add(m12); nextTris.Add(v2);
                    }
                }
                else
                {
                    nextTris.Add(a); nextTris.Add(mab); nextTris.Add(mca);
                    nextTris.Add(mab); nextTris.Add(b); nextTris.Add(mbc);
                    nextTris.Add(mca); nextTris.Add(mbc); nextTris.Add(c);
                    nextTris.Add(mab); nextTris.Add(mbc); nextTris.Add(mca);
                }
            }

            triangles.Clear();
            triangles.AddRange(nextTris);
        }
    }

    private static ProxySkin BuildProxySkin(
        Vector3[] positions,
        Vector3[] normals,
        int[] triangles,
        Vector3 absScale)
    {
        var min = positions[0];
        var max = positions[0];
        for (int i = 1; i < positions.Length; i++)
        {
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }

        var extent = max - min;
        float maxSpan = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
        float cellSize = MathF.Max(maxSpan / 18f, 0.02f);

        int[] vertexCluster = new int[positions.Length];
        int clusterCount = 0;

        for (int attempt = 0; attempt < 16; attempt++)
        {
            var cellToCluster = new Dictionary<(int, int, int), int>();
            clusterCount = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                var key = (
                    (int)MathF.Floor((p.X - min.X) / cellSize),
                    (int)MathF.Floor((p.Y - min.Y) / cellSize),
                    (int)MathF.Floor((p.Z - min.Z) / cellSize));
                if (!cellToCluster.TryGetValue(key, out int cid))
                    cellToCluster[key] = cid = clusterCount++;
                vertexCluster[i] = cid;
            }
            if (clusterCount <= MaxProxyCount) break;
            cellSize *= 1.35f;
        }

        // Hard cap safety if geometry spans wildly across huge scale.
        if (clusterCount > MaxProxyCount)
        {
            for (int i = 0; i < positions.Length; i++)
                vertexCluster[i] %= MaxProxyCount;
            clusterCount = MaxProxyCount;
        }

        var pPos = new Vector3[clusterCount];
        var pNorm = new Vector3[clusterCount];
        var counts = new int[clusterCount];

        for (int i = 0; i < positions.Length; i++)
        {
            int cid = vertexCluster[i];
            pPos[cid] += positions[i];
            pNorm[cid] += normals[i];
            counts[cid]++;
        }
        for (int c = 0; c < clusterCount; c++)
        {
            if (counts[c] > 0) pPos[c] /= counts[c];
            pNorm[c] = pNorm[c].LengthSquared() > 1e-12f ? Vector3.Normalize(pNorm[c]) : Vector3.UnitY;
        }

        var proxyTriSet = new HashSet<(int, int, int)>();
        var proxyTris = new List<int>();
        var proxyEdgeSet = new HashSet<(int, int)>();
        var proxyEdges = new List<SheetEdge>();

        void AddProxyEdge(int u, int v)
        {
            if (u == v) return;
            int lo = u < v ? u : v;
            int hi = u < v ? v : u;
            if (proxyEdgeSet.Add((lo, hi)))
                proxyEdges.Add(new SheetEdge(lo, hi, Vector3.Distance(pPos[lo] * absScale, pPos[hi] * absScale)));
        }

        for (int t = 0; t < triangles.Length; t += 3)
        {
            int ca = vertexCluster[triangles[t]];
            int cb = vertexCluster[triangles[t + 1]];
            int cc = vertexCluster[triangles[t + 2]];
            AddProxyEdge(ca, cb);
            AddProxyEdge(cb, cc);
            AddProxyEdge(cc, ca);
            if (ca != cb && cb != cc && cc != ca && proxyTriSet.Add((ca, cb, cc)))
            {
                proxyTris.Add(ca);
                proxyTris.Add(cb);
                proxyTris.Add(cc);
            }
        }

        var proxyTriArr = proxyTris.ToArray();
        var proxyBending = FindBendingPairs(pPos, proxyTriArr, absScale);
        var (proxyIslands, proxyIslandCount) = FindIslands(clusterCount, proxyTriArr);

        int n = positions.Length;
        var vp0 = new int[n];
        var vp1 = new int[n];
        var vp2 = new int[n];
        var vw0 = new float[n];
        var vw1 = new float[n];
        var vw2 = new float[n];

        for (int i = 0; i < n; i++)
        {
            var pos = positions[i];
            int i0 = vertexCluster[i], i1 = i0, i2 = i0;
            float d0 = Vector3.DistanceSquared(pos, pPos[i0]);
            float d1 = float.PositiveInfinity, d2 = float.PositiveInfinity;

            for (int c = 0; c < clusterCount; c++)
            {
                if (c == i0) continue;
                float d = Vector3.DistanceSquared(pos, pPos[c]);
                if (d < d1)
                {
                    d2 = d1; i2 = i1;
                    d1 = d; i1 = c;
                }
                else if (d < d2)
                {
                    d2 = d; i2 = c;
                }
            }

            float w0 = 1f / (MathF.Sqrt(d0) + 1e-4f);
            float w1 = i1 != i0 ? 1f / (MathF.Sqrt(d1) + 1e-4f) : 0f;
            float w2 = i2 != i0 && i2 != i1 ? 1f / (MathF.Sqrt(d2) + 1e-4f) : 0f;
            float invSum = 1f / (w0 + w1 + w2);

            vp0[i] = i0; vp1[i] = i1; vp2[i] = i2;
            vw0[i] = w0 * invSum;
            vw1[i] = w1 * invSum;
            vw2[i] = w2 * invSum;
        }

        return new ProxySkin(
            pPos, pNorm, proxyTriArr, proxyEdges.ToArray(), proxyBending,
            proxyIslands, proxyIslandCount,
            vp0, vp1, vp2, vw0, vw1, vw2);
    }

    private static SheetEdge[] ExtractEdges(Vector3[] positions, int[] triangles, Vector3 absScale)
    {
        var edgeSet = new HashSet<(int, int)>();
        var edges = new List<SheetEdge>();
        void Add(int u, int v)
        {
            if (u == v) return;
            int a = u < v ? u : v;
            int b = u < v ? v : u;
            if (edgeSet.Add((a, b)))
                edges.Add(new SheetEdge(a, b, Vector3.Distance(positions[a] * absScale, positions[b] * absScale)));
        }
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Add(triangles[t], triangles[t + 1]);
            Add(triangles[t + 1], triangles[t + 2]);
            Add(triangles[t + 2], triangles[t]);
        }
        return edges.ToArray();
    }

    private static Vector2[] ComputeBoxUvs(
        Vector3[] positions,
        Vector3[] normals,
        Vector3 absScale,
        float tileSizeM,
        float uvRotationDeg)
    {
        float rad = uvRotationDeg * (MathF.PI / 180f);
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        float invTile = 1f / tileSizeM;

        var uvs = new Vector2[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            var an = Vector3.Abs(normals[i]);
            int axis = an.X >= an.Y && an.X >= an.Z ? 0 : (an.Y >= an.Z ? 1 : 2);
            var scaled = positions[i] * absScale;
            Vector2 proj = axis == 0
                ? new Vector2(scaled.Z, scaled.Y)
                : (axis == 1 ? new Vector2(scaled.X, scaled.Z) : new Vector2(scaled.X, scaled.Y));
            proj *= invTile;
            uvs[i] = new Vector2(proj.X * cos - proj.Y * sin, proj.X * sin + proj.Y * cos);
        }
        return uvs;
    }

    private static OverlaySurface CombineMirrored(OverlaySurface src, Matrix4x4 mirror)
    {
        if (!Matrix4x4.Invert(mirror, out var inv))
            throw new InvalidOperationException("Singular mirror matrix.");
        var normalMatrix = Matrix4x4.Transpose(inv);
        bool flip = mirror.GetDeterminant() < 0f;

        int n = src.Vertices.Length;
        var verts = new Vector3[n * 2];
        var norms = new Vector3[n * 2];
        Array.Copy(src.Vertices, 0, verts, 0, n);
        Array.Copy(src.Normals, 0, norms, 0, n);

        for (int i = 0; i < n; i++)
        {
            var mv = Vector3.Transform(src.Vertices[i], mirror);
            var mn = Vector3.TransformNormal(src.Normals[i], normalMatrix);
            verts[n + i] = mv;
            norms[n + i] = mn.LengthSquared() > 1e-12f && Finite(mn) ? Vector3.Normalize(mn) : Vector3.UnitY;
        }

        int tc = src.Triangles.Length;
        var tris = new int[tc * 2];
        Array.Copy(src.Triangles, 0, tris, 0, tc);
        for (int t = 0; t < tc; t += 3)
        {
            int a = src.Triangles[t] + n;
            int b = src.Triangles[t + 1] + n;
            int c = src.Triangles[t + 2] + n;
            tris[tc + t] = a;
            tris[tc + t + 1] = flip ? c : b;
            tris[tc + t + 2] = flip ? b : c;
        }

        return new OverlaySurface(verts, tris, norms);
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
