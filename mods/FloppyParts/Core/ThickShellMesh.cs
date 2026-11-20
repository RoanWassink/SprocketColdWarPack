using System.Numerics;

namespace SprocketFloppyParts;

internal sealed class ThickShellMesh
{
    private readonly record struct RimSlot(int VertexOffset, int EdgeA, int EdgeB);

    internal bool IsThick { get; }
    internal int SimVertexCount { get; }
    internal int RenderVertexCount { get; }
    internal Vector3[] RestVertices { get; }
    internal Vector3[] RestNormals { get; }
    internal Vector2[] Uvs { get; }
    internal Vector4[] Tangents { get; }
    internal int[] Triangles { get; }

    private readonly float[] backOffset;
    private readonly int[] frontTriangles;
    private readonly RimSlot[] rimSlots;

    private ThickShellMesh(
        bool isThick,
        int simVertexCount,
        Vector3[] restVertices,
        Vector3[] restNormals,
        Vector2[] uvs,
        Vector4[] tangents,
        int[] triangles,
        float[] backOffset,
        int[] frontTriangles,
        RimSlot[] rimSlots)
    {
        IsThick = isThick;
        SimVertexCount = simVertexCount;
        RenderVertexCount = restVertices.Length;
        RestVertices = restVertices;
        RestNormals = restNormals;
        Uvs = uvs;
        Tangents = tangents;
        Triangles = triangles;
        this.backOffset = backOffset;
        this.frontTriangles = frontTriangles;
        this.rimSlots = rimSlots;
    }

    internal static ThickShellMesh Build(SubdividedSheet sheet, LookPreset preset, Vector3 absScale)
    {
        int n = sheet.Positions.Length;
        float avgScale = (MathF.Abs(absScale.X) + MathF.Abs(absScale.Y) + MathF.Abs(absScale.Z)) / 3f;
        float medianThickness = ComputeMedianAbs(sheet.BackOffset) * avgScale;
        bool thin = (preset == LookPreset.Canvas || preset == LookPreset.CamoNet) &&
                    medianThickness <= 0.004f;

        if (thin)
        {
            var restVerts = sheet.Positions.ToArray();
            var restNorms = sheet.Normals.ToArray();
            var uvs = sheet.Uvs.ToArray();
            var tris = sheet.Triangles.ToArray();
            var tangents = WeldedPlateGeometry.ComputeTangents(restVerts, tris, restNorms, uvs);
            return new ThickShellMesh(
                isThick: false,
                simVertexCount: n,
                restVertices: restVerts,
                restNormals: restNorms,
                uvs: uvs,
                tangents: tangents,
                triangles: tris,
                backOffset: sheet.BackOffset,
                frontTriangles: tris,
                rimSlots: Array.Empty<RimSlot>());
        }

        int rimCount = sheet.BoundaryEdges.Length;
        int totalVerts = n * 2 + rimCount * 4;
        var vertices = new Vector3[totalVerts];
        var normals = new Vector3[totalVerts];
        var uvArr = new Vector2[totalVerts];

        for (int i = 0; i < n; i++)
        {
            vertices[i] = sheet.Positions[i];
            normals[i] = sheet.Normals[i];
            uvArr[i] = sheet.Uvs[i];

            vertices[n + i] = sheet.Positions[i] + sheet.Normals[i] * sheet.BackOffset[i];
            normals[n + i] = -sheet.Normals[i];
            uvArr[n + i] = sheet.Uvs[i];
        }

        var frontTris = sheet.Triangles;
        var triList = new List<int>(frontTris.Length * 2 + rimCount * 6);
        triList.AddRange(frontTris);
        for (int t = 0; t < frontTris.Length; t += 3)
        {
            triList.Add(frontTris[t] + n);
            triList.Add(frontTris[t + 2] + n);
            triList.Add(frontTris[t + 1] + n);
        }

        var rims = new RimSlot[rimCount];
        for (int k = 0; k < rimCount; k++)
        {
            var e = sheet.BoundaryEdges[k];
            int vBase = n * 2 + k * 4;
            rims[k] = new RimSlot(vBase, e.A, e.B);

            var pA = vertices[e.A];
            var pB = vertices[e.B];
            var pAb = vertices[n + e.A];
            var pBb = vertices[n + e.B];

            var avgN = normals[e.A] + normals[e.B];
            var rimCross = Vector3.Cross(pB - pA, avgN);
            var rimN = rimCross.LengthSquared() > 1e-12f ? Vector3.Normalize(rimCross) : Vector3.UnitY;

            vertices[vBase] = pA;
            vertices[vBase + 1] = pB;
            vertices[vBase + 2] = pBb;
            vertices[vBase + 3] = pAb;

            normals[vBase] = rimN;
            normals[vBase + 1] = rimN;
            normals[vBase + 2] = rimN;
            normals[vBase + 3] = rimN;

            uvArr[vBase] = uvArr[e.A];
            uvArr[vBase + 1] = uvArr[e.B];
            uvArr[vBase + 2] = uvArr[e.B];
            uvArr[vBase + 3] = uvArr[e.A];

            var faceCross = Vector3.Cross(pB - pA, pBb - pA);
            bool flipRim = Vector3.Dot(faceCross, rimN) < 0f;
            if (!flipRim)
            {
                triList.Add(vBase); triList.Add(vBase + 1); triList.Add(vBase + 2);
                triList.Add(vBase); triList.Add(vBase + 2); triList.Add(vBase + 3);
            }
            else
            {
                triList.Add(vBase); triList.Add(vBase + 2); triList.Add(vBase + 1);
                triList.Add(vBase); triList.Add(vBase + 3); triList.Add(vBase + 2);
            }
        }

        var allTris = triList.ToArray();
        var tangentArr = WeldedPlateGeometry.ComputeTangents(vertices, allTris, normals, uvArr);
        return new ThickShellMesh(
            isThick: true,
            simVertexCount: n,
            restVertices: vertices,
            restNormals: normals,
            uvs: uvArr,
            tangents: tangentArr,
            triangles: allTris,
            backOffset: sheet.BackOffset,
            frontTriangles: frontTris,
            rimSlots: rims);
    }

    internal void UpdateDeformed(
        ReadOnlySpan<Vector3> localSimPositions,
        Vector3[] outVertices,
        Vector3[] outNormals)
    {
        if (localSimPositions.Length < SimVertexCount ||
            outVertices.Length < RenderVertexCount ||
            outNormals.Length < RenderVertexCount)
        {
            CopyRest(outVertices, outNormals);
            return;
        }

        for (int i = 0; i < SimVertexCount; i++)
        {
            var p = localSimPositions[i];
            if (!Finite(p))
            {
                CopyRest(outVertices, outNormals);
                return;
            }
            outVertices[i] = p;
            outNormals[i] = Vector3.Zero;
        }

        for (int t = 0; t < frontTriangles.Length; t += 3)
        {
            int a = frontTriangles[t];
            int b = frontTriangles[t + 1];
            int c = frontTriangles[t + 2];
            var cross = Vector3.Cross(outVertices[b] - outVertices[a], outVertices[c] - outVertices[a]);
            if (Finite(cross))
            {
                outNormals[a] += cross;
                outNormals[b] += cross;
                outNormals[c] += cross;
            }
        }

        for (int i = 0; i < SimVertexCount; i++)
        {
            var n = outNormals[i];
            float lenSq = n.LengthSquared();
            outNormals[i] = lenSq > 1e-16f ? n / MathF.Sqrt(lenSq) : RestNormals[i];
        }

        if (!IsThick) return;

        int nSim = SimVertexCount;
        for (int i = 0; i < nSim; i++)
        {
            outVertices[nSim + i] = outVertices[i] + outNormals[i] * backOffset[i];
            outNormals[nSim + i] = -outNormals[i];
        }

        for (int k = 0; k < rimSlots.Length; k++)
        {
            var rim = rimSlots[k];
            var pA = outVertices[rim.EdgeA];
            var pB = outVertices[rim.EdgeB];
            var pBb = outVertices[nSim + rim.EdgeB];
            var pAb = outVertices[nSim + rim.EdgeA];

            var avgN = outNormals[rim.EdgeA] + outNormals[rim.EdgeB];
            var cross = Vector3.Cross(pB - pA, avgN);
            float lenSq = cross.LengthSquared();
            var rimN = lenSq > 1e-16f ? cross / MathF.Sqrt(lenSq) : RestNormals[rim.VertexOffset];

            int v = rim.VertexOffset;
            outVertices[v] = pA;
            outVertices[v + 1] = pB;
            outVertices[v + 2] = pBb;
            outVertices[v + 3] = pAb;

            outNormals[v] = rimN;
            outNormals[v + 1] = rimN;
            outNormals[v + 2] = rimN;
            outNormals[v + 3] = rimN;
        }
    }

    private void CopyRest(Vector3[] outVertices, Vector3[] outNormals)
    {
        int count = Math.Min(RenderVertexCount, Math.Min(outVertices.Length, outNormals.Length));
        Array.Copy(RestVertices, 0, outVertices, 0, count);
        Array.Copy(RestNormals, 0, outNormals, 0, count);
    }

    private static float ComputeMedianAbs(float[] values)
    {
        if (values.Length == 0) return 0f;
        var sorted = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
            sorted[i] = MathF.Abs(values[i]);
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
