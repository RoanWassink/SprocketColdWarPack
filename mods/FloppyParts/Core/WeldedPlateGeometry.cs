using System.Globalization;
using System.Numerics;

namespace SprocketFloppyParts;

internal sealed record OverlaySurface(Vector3[] Vertices, int[] Triangles, Vector3[] Normals);

internal sealed record OverlayCopy(
    Vector3[] Vertices,
    int[] Triangles,
    Vector3[] Normals,
    Vector2[] Uv,
    Vector4[] Tangents);

internal readonly record struct SheetEdge(int A, int B, float RestLength);

internal sealed record WeldedSheet(
    Vector3[] Positions,
    Vector3[] Normals,
    int[] Triangles,
    SheetEdge[] Edges,
    float[] Thickness);

internal static class WeldedPlateGeometry
{
    internal const int MaxVertices = 250_000;
    internal const float WeldRadiusMeters = 1e-4f;

    internal static OverlayCopy Build(
        Vector3 scale,
        float tileSizeM,
        float uvRotationDeg,
        params OverlaySurface[] surfaces)
    {
        scale = Vector3.Abs(scale);
        if (!Finite(scale) || MathF.Min(scale.X, MathF.Min(scale.Y, scale.Z)) < 1e-6f)
            throw new InvalidOperationException("Invalid plate scale.");
        if (!float.IsFinite(tileSizeM) || tileSizeM < 0.01f)
            throw new InvalidOperationException("Invalid tile size.");
        if (!float.IsFinite(uvRotationDeg))
            throw new InvalidOperationException("Invalid UV rotation.");

        float rad = uvRotationDeg * (MathF.PI / 180f);
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        float invTile = 1f / tileSizeM;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        var remap = new Dictionary<(int Surface, int Vertex, int Axis), int>();

        for (int sIdx = 0; sIdx < surfaces.Length; sIdx++)
        {
            var s = surfaces[sIdx];
            ValidateSurface(s);

            for (int t = 0; t < s.Triangles.Length; t += 3)
            {
                int i0 = s.Triangles[t];
                int i1 = s.Triangles[t + 1];
                int i2 = s.Triangles[t + 2];

                var p0 = s.Vertices[i0] * scale;
                var p1 = s.Vertices[i1] * scale;
                var p2 = s.Vertices[i2] * scale;
                var cross = Vector3.Cross(p1 - p0, p2 - p0);
                if (cross.LengthSquared() < 1e-18f) continue;

                var absCross = Vector3.Abs(cross);
                int axis = absCross.X >= absCross.Y && absCross.X >= absCross.Z
                    ? 0
                    : (absCross.Y >= absCross.Z ? 1 : 2);

                triangles.Add(Emit(sIdx, i0, axis));
                triangles.Add(Emit(sIdx, i1, axis));
                triangles.Add(Emit(sIdx, i2, axis));
            }

            int Emit(int surfaceIndex, int vertexIndex, int axis)
            {
                var key = (surfaceIndex, vertexIndex, axis);
                if (remap.TryGetValue(key, out int existing)) return existing;
                if (vertices.Count >= MaxVertices)
                    throw new InvalidOperationException("Overlay vertex budget exceeded.");

                var pos = s.Vertices[vertexIndex];
                var n = s.Normals[vertexIndex];
                var scaled = pos * scale;
                Vector2 proj = axis == 0
                    ? new Vector2(scaled.Z, scaled.Y)
                    : (axis == 1 ? new Vector2(scaled.X, scaled.Z) : new Vector2(scaled.X, scaled.Y));
                proj *= invTile;
                var uv = new Vector2(proj.X * cos - proj.Y * sin, proj.X * sin + proj.Y * cos);

                int idx = vertices.Count;
                vertices.Add(pos);
                normals.Add(n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY);
                uvs.Add(uv);
                remap.Add(key, idx);
                return idx;
            }
        }

        if (vertices.Count < 3 || triangles.Count < 3)
            throw new InvalidOperationException("Degenerate or empty plate geometry.");

        var vertArr = vertices.ToArray();
        var triArr = triangles.ToArray();
        var normArr = normals.ToArray();
        var uvArr = uvs.ToArray();
        var tangents = ComputeTangents(vertArr, triArr, normArr, uvArr);
        return new OverlayCopy(vertArr, triArr, normArr, uvArr, tangents);
    }

    internal static OverlayCopy Mirror(OverlayCopy source, Matrix4x4 mirror)
    {
        if (!Matrix4x4.Invert(mirror, out var inverse))
            throw new InvalidOperationException("Singular mirror matrix.");
        if (source.Vertices.Length * 2 > MaxVertices)
            throw new InvalidOperationException("Mirrored overlay vertex budget exceeded.");

        var normalMatrix = Matrix4x4.Transpose(inverse);
        bool flipWinding = mirror.GetDeterminant() < 0f;
        int n = source.Vertices.Length;

        var vertices = new Vector3[n * 2];
        var normals = new Vector3[n * 2];
        var uvs = new Vector2[n * 2];
        Array.Copy(source.Vertices, 0, vertices, 0, n);
        Array.Copy(source.Normals, 0, normals, 0, n);
        Array.Copy(source.Uv, 0, uvs, 0, n);

        for (int i = 0; i < n; i++)
        {
            var mv = Vector3.Transform(source.Vertices[i], mirror);
            var mn = Vector3.TransformNormal(source.Normals[i], normalMatrix);
            if (!Finite(mv)) throw new InvalidOperationException("Non-finite mirrored vertex.");
            vertices[n + i] = mv;
            normals[n + i] = mn.LengthSquared() > 1e-12f && Finite(mn) ? Vector3.Normalize(mn) : Vector3.UnitY;
            uvs[n + i] = source.Uv[i];
        }

        var triangles = new int[source.Triangles.Length * 2];
        Array.Copy(source.Triangles, 0, triangles, 0, source.Triangles.Length);
        int tOff = source.Triangles.Length;
        for (int t = 0; t < source.Triangles.Length; t += 3)
        {
            int a = source.Triangles[t] + n;
            int b = source.Triangles[t + 1] + n;
            int c = source.Triangles[t + 2] + n;
            triangles[tOff + t] = a;
            triangles[tOff + t + 1] = flipWinding ? c : b;
            triangles[tOff + t + 2] = flipWinding ? b : c;
        }

        var tangents = ComputeTangents(vertices, triangles, normals, uvs);
        return new OverlayCopy(vertices, triangles, normals, uvs, tangents);
    }

    internal static Vector4[] ComputeTangents(
        Vector3[] vertices,
        int[] triangles,
        Vector3[] normals,
        Vector2[] uv)
    {
        var tan1 = new Vector3[vertices.Length];
        var tan2 = new Vector3[vertices.Length];

        for (int t = 0; t < triangles.Length; t += 3)
        {
            int i0 = triangles[t];
            int i1 = triangles[t + 1];
            int i2 = triangles[t + 2];

            var e1 = vertices[i1] - vertices[i0];
            var e2 = vertices[i2] - vertices[i0];
            var u1 = uv[i1] - uv[i0];
            var u2 = uv[i2] - uv[i0];
            float det = u1.X * u2.Y - u1.Y * u2.X;

            Vector3 sdir, tdir;
            if (MathF.Abs(det) > 1e-12f)
            {
                float inv = 1f / det;
                sdir = (e1 * u2.Y - e2 * u1.Y) * inv;
                tdir = (e2 * u1.X - e1 * u2.X) * inv;
            }
            else
            {
                sdir = e1;
                tdir = e2;
            }

            if (Finite(sdir))
            {
                tan1[i0] += sdir;
                tan1[i1] += sdir;
                tan1[i2] += sdir;
            }
            if (Finite(tdir))
            {
                tan2[i0] += tdir;
                tan2[i1] += tdir;
                tan2[i2] += tdir;
            }
        }

        var result = new Vector4[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            var n = normals[i].LengthSquared() > 1e-12f ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
            var t = tan1[i];
            var ortho = t - n * Vector3.Dot(n, t);
            if (ortho.LengthSquared() < 1e-12f || !Finite(ortho))
            {
                var axis = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
                ortho = Vector3.Cross(n, axis);
            }
            ortho = Vector3.Normalize(ortho);
            float w = Vector3.Dot(Vector3.Cross(n, ortho), tan2[i]) < 0f ? -1f : 1f;
            result[i] = new Vector4(ortho.X, ortho.Y, ortho.Z, w);
        }
        return result;
    }

    internal static WeldedSheet BuildWeldedSheet(OverlaySurface front, OverlaySurface back)
    {
        ValidateSurface(front);
        ValidateSurface(back);

        float cell = WeldRadiusMeters;
        float radiusSq = WeldRadiusMeters * WeldRadiusMeters;
        var grid = new Dictionary<(int, int, int), List<int>>();
        var weldedPositions = new List<Vector3>();
        var normalAccum = new List<Vector3>();
        var vertexRemap = new int[front.Vertices.Length];

        for (int i = 0; i < front.Vertices.Length; i++)
        {
            var p = front.Vertices[i];
            var n = front.Normals[i];
            int cx = (int)MathF.Floor(p.X / cell);
            int cy = (int)MathF.Floor(p.Y / cell);
            int cz = (int)MathF.Floor(p.Z / cell);

            int match = -1;
            float bestDistSq = radiusSq;

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var bucket)) continue;
                        foreach (int idx in bucket)
                        {
                            float dSq = Vector3.DistanceSquared(p, weldedPositions[idx]);
                            if (dSq <= bestDistSq)
                            {
                                bestDistSq = dSq;
                                match = idx;
                            }
                        }
                    }

            if (match < 0)
            {
                match = weldedPositions.Count;
                weldedPositions.Add(p);
                normalAccum.Add(n);
                var key = (cx, cy, cz);
                if (!grid.TryGetValue(key, out var bucket))
                    grid[key] = bucket = new List<int>(2);
                bucket.Add(match);
            }
            else
            {
                normalAccum[match] += n;
            }
            vertexRemap[i] = match;
        }

        var positions = weldedPositions.ToArray();
        var normals = new Vector3[positions.Length];
        for (int i = 0; i < normals.Length; i++)
            normals[i] = normalAccum[i].LengthSquared() > 1e-12f ? Vector3.Normalize(normalAccum[i]) : Vector3.UnitY;

        var triangles = new List<int>(front.Triangles.Length);
        var edgeSet = new HashSet<(int, int)>();
        var edges = new List<SheetEdge>();

        void AddEdge(int u, int v)
        {
            if (u == v) return;
            int a = u < v ? u : v;
            int b = u < v ? v : u;
            if (edgeSet.Add((a, b)))
                edges.Add(new SheetEdge(a, b, Vector3.Distance(positions[a], positions[b])));
        }

        for (int t = 0; t < front.Triangles.Length; t += 3)
        {
            int a = vertexRemap[front.Triangles[t]];
            int b = vertexRemap[front.Triangles[t + 1]];
            int c = vertexRemap[front.Triangles[t + 2]];
            if (a == b || b == c || c == a) continue;
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            AddEdge(a, b);
            AddEdge(b, c);
            AddEdge(c, a);
        }

        var thickness = new float[positions.Length];
        if (back.Vertices.Length > 0)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                float minDistSq = float.PositiveInfinity;
                var p = positions[i];
                for (int j = 0; j < back.Vertices.Length; j++)
                {
                    float dSq = Vector3.DistanceSquared(p, back.Vertices[j]);
                    if (dSq < minDistSq) minDistSq = dSq;
                }
                thickness[i] = float.IsFinite(minDistSq) ? MathF.Sqrt(minDistSq) : 0f;
            }
        }

        return new WeldedSheet(positions, normals, triangles.ToArray(), edges.ToArray(), thickness);
    }

    internal static bool Covers(
        Vector3[] vertices,
        int triCount,
        Vector3[] boxCorners,
        int nativeTriCount)
    {
        if (vertices.Length == 0 || boxCorners.Length == 0 || nativeTriCount <= 0 || triCount < nativeTriCount)
            return false;

        var min = vertices[0];
        var max = vertices[0];
        for (int i = 1; i < vertices.Length; i++)
        {
            min = Vector3.Min(min, vertices[i]);
            max = Vector3.Max(max, vertices[i]);
        }

        const float eps = 0.001f;
        for (int i = 0; i < boxCorners.Length; i++)
        {
            var p = boxCorners[i];
            if (p.X < min.X - eps || p.Y < min.Y - eps || p.Z < min.Z - eps ||
                p.X > max.X + eps || p.Y > max.Y + eps || p.Z > max.Z + eps)
                return false;
        }
        return true;
    }

    internal static string Fingerprint(params OverlaySurface[] parts)
    {
        ulong hash = 14695981039346656037UL;
        void Mix(int value)
        {
            unchecked { hash = (hash ^ (uint)value) * 1099511628211UL; }
        }
        foreach (var p in parts)
        {
            Mix(p.Vertices.Length);
            Mix(p.Normals.Length);
            Mix(p.Triangles.Length);
            foreach (var v in p.Vertices)
            {
                Mix(BitConverter.SingleToInt32Bits(v.X));
                Mix(BitConverter.SingleToInt32Bits(v.Y));
                Mix(BitConverter.SingleToInt32Bits(v.Z));
            }
            foreach (var n in p.Normals)
            {
                Mix(BitConverter.SingleToInt32Bits(n.X));
                Mix(BitConverter.SingleToInt32Bits(n.Y));
                Mix(BitConverter.SingleToInt32Bits(n.Z));
            }
            foreach (var t in p.Triangles)
                Mix(t);
        }
        return hash.ToString("X16", CultureInfo.InvariantCulture);
    }

    private static void ValidateSurface(OverlaySurface part)
    {
        if (part.Vertices.Length != part.Normals.Length || part.Triangles.Length % 3 != 0)
            throw new InvalidOperationException("Invalid plate surface array lengths.");
        for (int i = 0; i < part.Vertices.Length; i++)
        {
            if (!Finite(part.Vertices[i]) || !Finite(part.Normals[i]))
                throw new InvalidOperationException("Non-finite vertex or normal in plate surface.");
        }
        for (int i = 0; i < part.Triangles.Length; i++)
        {
            int idx = part.Triangles[i];
            if (idx < 0 || idx >= part.Vertices.Length)
                throw new InvalidOperationException("Out-of-range triangle index in plate surface.");
        }
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
