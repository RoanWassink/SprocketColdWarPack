using System.Numerics;

namespace SprocketFloppyParts;

internal sealed record LocalColliderMesh(
    int AnchorIndex,
    Vector3 LocalMin,
    Vector3 LocalMax,
    Vector3[]? LocalVertices,
    int[]? Triangles,
    Vector3[]? TriNormals,
    Vector3[]? TriMin,
    Vector3[]? TriMax,
    bool IsPlate);

internal readonly record struct GroundPlane(bool Valid, Vector3 Origin, Vector3 Normal);

internal readonly record struct AirflowJet(
    Vector3 Origin,
    Vector3 Direction,
    float RangeM,
    float RadiusM,
    float Strength,
    float PulseHz,
    float Phase);

internal static class SurfaceCollision
{
    internal const int MaxCollidersPerPlate = 24;
    internal const int MaxTrianglesPerPlate = 1400;

    internal static LocalColliderMesh[] PrepareColliders(
        LocalColliderMesh[]? rawColliders,
        Matrix4x4[] anchorL2W,
        Vector3[] clothWorldPositions,
        float maxReachM)
    {
        if (rawColliders == null || rawColliders.Length == 0 ||
            anchorL2W.Length == 0 || clothWorldPositions.Length == 0)
            return Array.Empty<LocalColliderMesh>();

        var clothMin = clothWorldPositions[0];
        var clothMax = clothWorldPositions[0];
        for (int i = 1; i < clothWorldPositions.Length; i++)
        {
            clothMin = Vector3.Min(clothMin, clothWorldPositions[i]);
            clothMax = Vector3.Max(clothMax, clothWorldPositions[i]);
        }

        float expand = MathF.Max(0f, maxReachM) + 0.45f;
        var expandVec = new Vector3(expand);
        var reachMin = clothMin - expandVec;
        var reachMax = clothMax + expandVec;
        var clothCenter = (clothMin + clothMax) * 0.5f;

        var candidates = new List<(LocalColliderMesh Col, float DistSq)>(rawColliders.Length);
        for (int c = 0; c < rawColliders.Length; c++)
        {
            var raw = rawColliders[c];
            if ((uint)raw.AnchorIndex >= (uint)anchorL2W.Length) continue;
            var l2w = anchorL2W[raw.AnchorIndex];
            ComputeWorldAabb(raw.LocalMin, raw.LocalMax, l2w, out var wMin, out var wMax);

            if (wMax.X < reachMin.X || wMin.X > reachMax.X ||
                wMax.Y < reachMin.Y || wMin.Y > reachMax.Y ||
                wMax.Z < reachMin.Z || wMin.Z > reachMax.Z)
                continue;

            var clamped = Vector3.Clamp(clothCenter, wMin, wMax);
            float dSq = Vector3.DistanceSquared(clothCenter, clamped);
            candidates.Add((raw, dSq));
        }

        candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));

        var prepared = new List<LocalColliderMesh>(Math.Min(candidates.Count, MaxCollidersPerPlate));
        int totalTris = 0;

        for (int i = 0; i < candidates.Count && prepared.Count < MaxCollidersPerPlate; i++)
        {
            var raw = candidates[i].Col;
            if (raw.Triangles == null || raw.LocalVertices == null || raw.Triangles.Length < 3)
            {
                if (!IsEnclosingObb(raw, clothWorldPositions, anchorL2W))
                    prepared.Add(raw);
                continue;
            }

            int triCount = raw.Triangles.Length / 3;
            var normals = raw.TriNormals;
            var tMin = raw.TriMin;
            var tMax = raw.TriMax;

            if (normals == null || tMin == null || tMax == null ||
                normals.Length < triCount || tMin.Length < triCount || tMax.Length < triCount)
            {
                normals = new Vector3[triCount];
                tMin = new Vector3[triCount];
                tMax = new Vector3[triCount];
                for (int t = 0; t < triCount; t++)
                {
                    var a = raw.LocalVertices[raw.Triangles[t * 3]];
                    var b = raw.LocalVertices[raw.Triangles[t * 3 + 1]];
                    var cv = raw.LocalVertices[raw.Triangles[t * 3 + 2]];
                    tMin[t] = Vector3.Min(a, Vector3.Min(b, cv));
                    tMax[t] = Vector3.Max(a, Vector3.Max(b, cv));
                    var cross = Vector3.Cross(b - a, cv - a);
                    normals[t] = cross.LengthSquared() > 1e-12f ? Vector3.Normalize(cross) : Vector3.UnitY;
                }
            }

            if (totalTris + triCount <= MaxTrianglesPerPlate)
            {
                prepared.Add(raw with { TriNormals = normals, TriMin = tMin, TriMax = tMax });
                totalTris += triCount;
            }
            else
            {
                int remaining = MaxTrianglesPerPlate - totalTris;
                if (remaining <= 0)
                {
                    // Fallback to tight OBB when triangle budget is exhausted.
                    var obb = new LocalColliderMesh(
                        raw.AnchorIndex, raw.LocalMin, raw.LocalMax,
                        null, null, null, null, null, raw.IsPlate);
                    if (!IsEnclosingObb(obb, clothWorldPositions, anchorL2W))
                        prepared.Add(obb);
                    continue;
                }

                // Filter triangles nearest to reach box in anchor-local space up to remaining budget.
                Matrix4x4.Invert(anchorL2W[raw.AnchorIndex], out var w2l);
                ComputeWorldAabb(reachMin, reachMax, w2l, out var lReachMin, out var lReachMax);
                var lCenter = (lReachMin + lReachMax) * 0.5f;

                var keptTris = new List<(int TriIdx, float DistSq)>(triCount);
                for (int t = 0; t < triCount; t++)
                {
                    if (tMax[t].X < lReachMin.X || tMin[t].X > lReachMax.X ||
                        tMax[t].Y < lReachMin.Y || tMin[t].Y > lReachMax.Y ||
                        tMax[t].Z < lReachMin.Z || tMin[t].Z > lReachMax.Z)
                        continue;
                    var tc = (tMin[t] + tMax[t]) * 0.5f;
                    keptTris.Add((t, Vector3.DistanceSquared(tc, lCenter)));
                }
                if (keptTris.Count == 0) continue;
                if (keptTris.Count > remaining)
                {
                    keptTris.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
                    keptTris.RemoveRange(remaining, keptTris.Count - remaining);
                }

                int kc = keptTris.Count;
                var fTris = new int[kc * 3];
                var fNorms = new Vector3[kc];
                var fMin = new Vector3[kc];
                var fMax = new Vector3[kc];
                for (int k = 0; k < kc; k++)
                {
                    int src = keptTris[k].TriIdx;
                    fTris[k * 3] = raw.Triangles[src * 3];
                    fTris[k * 3 + 1] = raw.Triangles[src * 3 + 1];
                    fTris[k * 3 + 2] = raw.Triangles[src * 3 + 2];
                    fNorms[k] = normals[src];
                    fMin[k] = tMin[src];
                    fMax[k] = tMax[src];
                }
                prepared.Add(new LocalColliderMesh(
                    raw.AnchorIndex, raw.LocalMin, raw.LocalMax,
                    raw.LocalVertices, fTris, fNorms, fMin, fMax, raw.IsPlate));
                totalTris += kc;
            }
        }

        return prepared.ToArray();
    }

    private static bool IsEnclosingObb(
        LocalColliderMesh raw,
        ReadOnlySpan<Vector3> clothWorldPositions,
        ReadOnlySpan<Matrix4x4> anchorL2W)
    {
        if (clothWorldPositions.Length == 0 || (uint)raw.AnchorIndex >= (uint)anchorL2W.Length)
            return false;
        if (!Matrix4x4.Invert(anchorL2W[raw.AnchorIndex], out var w2l))
            return false;

        int insideCount = 0;
        for (int v = 0; v < clothWorldPositions.Length; v++)
        {
            var lp = Vector3.Transform(clothWorldPositions[v], w2l);
            if (lp.X > raw.LocalMin.X && lp.X < raw.LocalMax.X &&
                lp.Y > raw.LocalMin.Y && lp.Y < raw.LocalMax.Y &&
                lp.Z > raw.LocalMin.Z && lp.Z < raw.LocalMax.Z)
            {
                insideCount++;
            }
        }
        return insideCount * 100 >= clothWorldPositions.Length * 35;
    }

    internal static void ResolveSurfaceCollisions(
        Vector3[] worldPos,
        Vector3[] prevPos,
        bool[] isPinned,
        float[] distFromPin,
        LocalColliderMesh[] colliders,
        Matrix4x4[] anchorL2W,
        Matrix4x4[] anchorW2L,
        Matrix4x4[]? prevAnchorL2W = null)
    {
        const float pad = 0.08f;
        const float maxDistSq = 0.01f; // 0.10 m^2

        for (int c = 0; c < colliders.Length; c++)
        {
            var col = colliders[c];
            int aIdx = col.AnchorIndex;
            if ((uint)aIdx >= (uint)anchorL2W.Length || (uint)aIdx >= (uint)anchorW2L.Length)
                continue;

            var w2l = anchorW2L[aIdx];
            var l2w = anchorL2W[aIdx];
            var prevL2W = (prevAnchorL2W != null && (uint)aIdx < (uint)prevAnchorL2W.Length)
                ? prevAnchorL2W[aIdx]
                : l2w;
            var verts = col.LocalVertices;
            var tris = col.Triangles;
            var triNormals = col.TriNormals;
            var triMin = col.TriMin;
            var triMax = col.TriMax;
            bool hasTris = verts != null && tris != null && triNormals != null && triMin != null && triMax != null;
            int triCount = hasTris ? tris!.Length / 3 : 0;

            for (int i = 0; i < worldPos.Length; i++)
            {
                if (isPinned[i]) continue;
                var pLocal = Vector3.Transform(worldPos[i], w2l);

                if (pLocal.X < col.LocalMin.X - pad || pLocal.X > col.LocalMax.X + pad ||
                    pLocal.Y < col.LocalMin.Y - pad || pLocal.Y > col.LocalMax.Y + pad ||
                    pLocal.Z < col.LocalMin.Z - pad || pLocal.Z > col.LocalMax.Z + pad)
                    continue;

                float clear = MathF.Min(0.014f, MathF.Max(0.004f, distFromPin[i] * 0.45f));

                if (hasTris && triCount > 0)
                {
                    float bestDistSq = maxDistSq;
                    var bestPt = Vector3.Zero;
                    var bestN = Vector3.UnitY;
                    bool found = false;

                    for (int t = 0; t < triCount; t++)
                    {
                        var mn = triMin![t];
                        var mx = triMax![t];
                        if (pLocal.X < mn.X - pad || pLocal.X > mx.X + pad ||
                            pLocal.Y < mn.Y - pad || pLocal.Y > mx.Y + pad ||
                            pLocal.Z < mn.Z - pad || pLocal.Z > mx.Z + pad)
                            continue;

                        var a = verts![tris![t * 3]];
                        var b = verts[tris[t * 3 + 1]];
                        var cv = verts[tris[t * 3 + 2]];
                        var pt = ClosestPointOnTriangle(pLocal, a, b, cv);
                        float dSq = Vector3.DistanceSquared(pLocal, pt);
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            bestPt = pt;
                            bestN = triNormals![t];
                            found = true;
                        }
                    }

                    if (found && Vector3.Dot(pLocal - bestPt, bestN) < clear)
                    {
                        pLocal = bestPt + bestN * clear;
                        ApplySurfacePush(worldPos, prevPos, i, pLocal, l2w, prevL2W);
                    }
                }
                else
                {
                    float minX = col.LocalMin.X - clear, maxX = col.LocalMax.X + clear;
                    float minY = col.LocalMin.Y - clear, maxY = col.LocalMax.Y + clear;
                    float minZ = col.LocalMin.Z - clear, maxZ = col.LocalMax.Z + clear;

                    if (pLocal.X > minX && pLocal.X < maxX &&
                        pLocal.Y > minY && pLocal.Y < maxY &&
                        pLocal.Z > minZ && pLocal.Z < maxZ)
                    {
                        float dNegX = pLocal.X - minX, dPosX = maxX - pLocal.X;
                        float dNegY = pLocal.Y - minY, dPosY = maxY - pLocal.Y;
                        float dNegZ = pLocal.Z - minZ, dPosZ = maxZ - pLocal.Z;

                        float best = dNegX;
                        int face = 0;
                        if (dPosX < best) { best = dPosX; face = 1; }
                        if (dNegY < best) { best = dNegY; face = 2; }
                        if (dPosY < best) { best = dPosY; face = 3; }
                        if (dNegZ < best) { best = dNegZ; face = 4; }
                        if (dPosZ < best) { face = 5; }

                        switch (face)
                        {
                            case 0: pLocal.X = minX; break;
                            case 1: pLocal.X = maxX; break;
                            case 2: pLocal.Y = minY; break;
                            case 3: pLocal.Y = maxY; break;
                            case 4: pLocal.Z = minZ; break;
                            default: pLocal.Z = maxZ; break;
                        }

                        ApplySurfacePush(worldPos, prevPos, i, pLocal, l2w, prevL2W);
                    }
                }
            }
        }
    }

    private static void ApplySurfacePush(
        Vector3[] worldPos,
        Vector3[] prevPos,
        int index,
        Vector3 resolvedLocal,
        Matrix4x4 l2w,
        Matrix4x4 prevL2W)
    {
        var oldPos = worldPos[index];
        var resolvedW = Vector3.Transform(resolvedLocal, l2w);
        var delta = resolvedW - oldPos;
        float dSq = delta.LengthSquared();
        if (dSq > 0.04f * 0.04f)
            resolvedW = oldPos + delta * (0.04f / MathF.Sqrt(dSq));

        var pushVec = resolvedW - oldPos;
        float pushLenSq = pushVec.LengthSquared();
        var carrierMove = resolvedW - Vector3.Transform(resolvedLocal, prevL2W);
        worldPos[index] = resolvedW;

        if (pushLenSq > 1e-12f)
        {
            var pushDir = pushVec / MathF.Sqrt(pushLenSq);
            var relMove = (oldPos - prevPos[index]) - carrierMove;
            float vn = Vector3.Dot(relMove, pushDir);
            if (vn < 0f) relMove -= pushDir * vn;
            prevPos[index] = resolvedW - (carrierMove + relMove * 0.65f);
        }
        else
        {
            prevPos[index] = resolvedW - carrierMove;
        }
    }

    internal static void ResolveGroundCollision(
        Vector3[] worldPos,
        Vector3[] prevPos,
        bool[] isPinned,
        GroundPlane ground)
    {
        if (!ground.Valid) return;
        const float clearance = 0.015f;
        for (int i = 0; i < worldPos.Length; i++)
        {
            if (isPinned[i]) continue;
            float h = Vector3.Dot(worldPos[i] - ground.Origin, ground.Normal);
            if (h < -2.0f) continue;
            if (h < clearance)
            {
                var oldPos = worldPos[i];
                float push = MathF.Min(clearance - h, 0.05f);
                var resolved = oldPos + ground.Normal * push;
                var stepMove = oldPos - prevPos[i];
                float vn = Vector3.Dot(stepMove, ground.Normal);
                if (vn < 0f) stepMove -= ground.Normal * vn;
                worldPos[i] = resolved;
                prevPos[i] = resolved - stepMove * 0.65f;
            }
        }
    }

    private static void ComputeWorldAabb(
        Vector3 lMin,
        Vector3 lMax,
        Matrix4x4 m,
        out Vector3 wMin,
        out Vector3 wMax)
    {
        wMin = new Vector3(float.PositiveInfinity);
        wMax = new Vector3(float.NegativeInfinity);
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3(
                (i & 1) == 0 ? lMin.X : lMax.X,
                (i & 2) == 0 ? lMin.Y : lMax.Y,
                (i & 4) == 0 ? lMin.Z : lMax.Z);
            var w = Vector3.Transform(c, m);
            wMin = Vector3.Min(wMin, w);
            wMax = Vector3.Max(wMax, w);
        }
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

    internal static float ClipJetRangeByColliders(
        Vector3 originWorld,
        Vector3 dirWorld,
        float maxRangeM,
        LocalColliderMesh[]? colliders,
        ReadOnlySpan<Matrix4x4> anchorW2L)
    {
        if (maxRangeM <= 0.06f || colliders == null || colliders.Length == 0 || anchorW2L.IsEmpty)
            return MathF.Max(0f, maxRangeM);

        float dLenSq = dirWorld.LengthSquared();
        if (dLenSq < 1e-12f || !float.IsFinite(dLenSq))
            return MathF.Max(0f, maxRangeM);

        var dirNorm = dirWorld / MathF.Sqrt(dLenSq);
        float bestT = maxRangeM;

        for (int c = 0; c < colliders.Length; c++)
        {
            var col = colliders[c];
            if (!col.IsPlate ||
                col.LocalVertices == null ||
                col.Triangles == null ||
                col.TriMin == null ||
                col.TriMax == null ||
                (uint)col.AnchorIndex >= (uint)anchorW2L.Length)
                continue;

            var w2l = anchorW2L[col.AnchorIndex];
            var localOrigin = Vector3.Transform(originWorld, w2l);
            var localDir = Vector3.TransformNormal(dirNorm, w2l);

            if (!RayIntersectsAabb(localOrigin, localDir, col.LocalMin, col.LocalMax, 0.06f, bestT))
                continue;

            var verts = col.LocalVertices;
            var tris = col.Triangles;
            var tMin = col.TriMin;
            var tMax = col.TriMax;
            int triCount = tris.Length / 3;

            for (int t = 0; t < triCount; t++)
            {
                if (!RayIntersectsAabb(localOrigin, localDir, tMin[t], tMax[t], 0.06f, bestT))
                    continue;

                var a = verts[tris[t * 3]];
                var b = verts[tris[t * 3 + 1]];
                var cv = verts[tris[t * 3 + 2]];
                if (RayIntersectsTriangleDoubleSided(localOrigin, localDir, a, b, cv, 0.06f, bestT, out float hitT) &&
                    hitT < bestT)
                {
                    bestT = hitT;
                }
            }
        }

        return bestT;
    }

    private static bool RayIntersectsAabb(
        Vector3 orig,
        Vector3 dir,
        Vector3 minB,
        Vector3 maxB,
        float tMin,
        float tMax)
    {
        if (!RayAxisSlab(orig.X, dir.X, minB.X, maxB.X, ref tMin, ref tMax)) return false;
        if (!RayAxisSlab(orig.Y, dir.Y, minB.Y, maxB.Y, ref tMin, ref tMax)) return false;
        if (!RayAxisSlab(orig.Z, dir.Z, minB.Z, maxB.Z, ref tMin, ref tMax)) return false;
        return tMin <= tMax;
    }

    private static bool RayAxisSlab(float o, float d, float mn, float mx, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(d) < 1e-9f)
            return o >= mn && o <= mx;

        float inv = 1f / d;
        float t1 = (mn - o) * inv;
        float t2 = (mx - o) * inv;
        if (t1 > t2) (t1, t2) = (t2, t1);
        if (t1 > tMin) tMin = t1;
        if (t2 < tMax) tMax = t2;
        return tMin <= tMax;
    }

    private static bool RayIntersectsTriangleDoubleSided(
        Vector3 orig,
        Vector3 dir,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        float tMin,
        float tMax,
        out float hitT)
    {
        hitT = 0f;
        var e1 = v1 - v0;
        var e2 = v2 - v0;
        var pvec = Vector3.Cross(dir, e2);
        float det = Vector3.Dot(e1, pvec);
        if (MathF.Abs(det) < 1e-8f) return false;

        float invDet = 1f / det;
        var tvec = orig - v0;
        float u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < -1e-4f || u > 1.0001f) return false;

        var qvec = Vector3.Cross(tvec, e1);
        float v = Vector3.Dot(dir, qvec) * invDet;
        if (v < -1e-4f || u + v > 1.0001f) return false;

        float t = Vector3.Dot(e2, qvec) * invDet;
        if (t < tMin || t >= tMax) return false;

        hitT = t;
        return true;
    }
}
