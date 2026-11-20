using System.Numerics;

namespace SprocketFloppyParts;

internal sealed class XpbdClothSolver
{
    private const float MaxDt = 0.05f;
    private const float TeleportMetersSq = 9.0f; // 3.0 m
    private const float MaxSpeed = 35.0f;
    private const float MaxPivotAccel = 40.0f;

    private readonly MotionProfile motion;
    private readonly Vector3[] restLocalPositions;
    private readonly Vector3[] restLocalNormals;
    private readonly int[] triangles;
    private readonly SheetEdge[] edges;
    private readonly BendingPair[] bendingPairs;
    private readonly bool[] isPinned;
    private readonly int[] anchorIndex;
    private readonly Vector3[] anchorLocalPos;
    private readonly float[] distFromPin;
    private readonly int[] tetherRef;

    private readonly float avgThicknessM;
    private readonly Vector3[] worldPos;
    private readonly Vector3[] prevPos;
    private readonly Vector3[] vel;
    private readonly Vector3[] targetWorld;
    private readonly Vector3[] prevTargetWorld;
    private readonly Vector3[] aeroAccum;
    private readonly float[] areaAccum;
    private readonly LocalColliderMesh[] preparedColliders;
    private readonly Matrix4x4[] prevAnchorL2W;
    private readonly Matrix4x4[] prevInterpAnchorL2W;
    private readonly Matrix4x4[] interpAnchorL2W;
    private readonly Matrix4x4[] anchorW2L;

    private readonly int islandCount;
    private readonly int[][] islandVertices;
    private readonly Vector3[] islandPivotLocal;
    private readonly Vector3[] islandAxisLocal;
    private readonly Vector3[] islandNormalLocal;
    private readonly Vector3[] islandComOffsetLocal;
    private readonly float[] islandComRadius;
    private readonly float[] islandInertia;
    private readonly float[] islandAngle;
    private readonly float[] islandAngVel;
    private readonly Vector3[] islandPrevPivotWorld;
    private readonly Vector3[] islandPrevPivotVel;
    private readonly Vector3[] lastIslandPivotWorld;
    private readonly Vector3[] lastIslandAxisWorld;
    private readonly float[] lastIslandAngle;

    private bool initialized;
    private float blastTimer;
    private Vector3 blastOrigin;
    private Vector3 blastDir = Vector3.UnitZ;
    private float blastCaliber;

    internal XpbdClothSolver(
        MotionProfile motion,
        Vector3[] restLocalPositions,
        Vector3[] restLocalNormals,
        int[] triangles,
        SheetEdge[] edges,
        BendingPair[] bendingPairs,
        int[] islandIds,
        int islandCount,
        PinResult pins,
        Vector3 restScale,
        LocalColliderMesh[]? surfaceColliders = null,
        Matrix4x4[]? restAnchorL2W = null,
        float avgThicknessM = 0.015f)
    {
        this.motion = motion;
        this.avgThicknessM = Math.Clamp(float.IsFinite(avgThicknessM) ? avgThicknessM : 0.015f, 0.002f, 0.25f);
        this.restLocalPositions = restLocalPositions;
        this.restLocalNormals = restLocalNormals;
        this.triangles = triangles;
        this.edges = edges;
        this.bendingPairs = bendingPairs;
        isPinned = pins.IsPinned;
        anchorIndex = pins.AnchorIndex;
        anchorLocalPos = pins.AnchorLocalPos;
        distFromPin = pins.DistanceFromPin;

        var absScale = Vector3.Abs(restScale);
        if (!Finite(absScale) || MathF.Min(absScale.X, MathF.Min(absScale.Y, absScale.Z)) < 1e-6f)
            absScale = Vector3.One;

        int n = restLocalPositions.Length;
        worldPos = new Vector3[n];
        prevPos = new Vector3[n];
        vel = new Vector3[n];
        targetWorld = new Vector3[n];
        prevTargetWorld = new Vector3[n];
        aeroAccum = new Vector3[n];
        areaAccum = new float[n];
        tetherRef = new int[n];

        int maxAnchor = restAnchorL2W?.Length ?? 1;
        for (int i = 0; i < anchorIndex.Length; i++)
            if (anchorIndex[i] + 1 > maxAnchor) maxAnchor = anchorIndex[i] + 1;
        if (surfaceColliders != null)
        {
            for (int i = 0; i < surfaceColliders.Length; i++)
                if (surfaceColliders[i].AnchorIndex + 1 > maxAnchor) maxAnchor = surfaceColliders[i].AnchorIndex + 1;
        }

        prevAnchorL2W = new Matrix4x4[maxAnchor];
        prevInterpAnchorL2W = new Matrix4x4[maxAnchor];
        interpAnchorL2W = new Matrix4x4[maxAnchor];
        anchorW2L = new Matrix4x4[maxAnchor];
        for (int a = 0; a < maxAnchor; a++)
        {
            prevAnchorL2W[a] = Matrix4x4.Identity;
            prevInterpAnchorL2W[a] = Matrix4x4.Identity;
            interpAnchorL2W[a] = Matrix4x4.Identity;
        }

        // Nearest pinned vertex per unpinned vertex (within same island if possible) in scaled meters.
        for (int i = 0; i < n; i++)
        {
            if (isPinned[i])
            {
                tetherRef[i] = i;
                continue;
            }
            int best = -1;
            float bestSq = float.PositiveInfinity;
            int myIsl = i < islandIds.Length ? islandIds[i] : 0;
            var pi = restLocalPositions[i] * absScale;
            for (int j = 0; j < n; j++)
            {
                if (!isPinned[j]) continue;
                int jIsl = j < islandIds.Length ? islandIds[j] : 0;
                if (jIsl != myIsl) continue;
                float dSq = Vector3.DistanceSquared(pi, restLocalPositions[j] * absScale);
                if (dSq < bestSq) { bestSq = dSq; best = j; }
            }
            if (best < 0)
            {
                for (int j = 0; j < n; j++)
                {
                    if (!isPinned[j]) continue;
                    float dSq = Vector3.DistanceSquared(pi, restLocalPositions[j] * absScale);
                    if (dSq < bestSq) { bestSq = dSq; best = j; }
                }
            }
            tetherRef[i] = best;
        }

        this.islandCount = Math.Max(1, islandCount);
        islandVertices = new int[this.islandCount][];
        islandPivotLocal = new Vector3[this.islandCount];
        islandAxisLocal = new Vector3[this.islandCount];
        islandNormalLocal = new Vector3[this.islandCount];
        islandComOffsetLocal = new Vector3[this.islandCount];
        islandComRadius = new float[this.islandCount];
        islandInertia = new float[this.islandCount];
        islandAngle = new float[this.islandCount];
        islandAngVel = new float[this.islandCount];
        islandPrevPivotWorld = new Vector3[this.islandCount];
        islandPrevPivotVel = new Vector3[this.islandCount];
        lastIslandPivotWorld = new Vector3[this.islandCount];
        lastIslandAxisWorld = new Vector3[this.islandCount];
        lastIslandAngle = new float[this.islandCount];

        if (surfaceColliders != null && surfaceColliders.Length > 0 &&
            (motion == MotionProfile.Cloth || motion == MotionProfile.Rubber))
        {
            var prepareAnchors = restAnchorL2W;
            if (prepareAnchors == null || prepareAnchors.Length == 0)
            {
                prepareAnchors = new Matrix4x4[maxAnchor];
                prepareAnchors[0] = Matrix4x4.CreateScale(absScale);
                for (int a = 1; a < maxAnchor; a++)
                    prepareAnchors[a] = Matrix4x4.Identity;
            }
            var restWorld = new Vector3[n];
            for (int i = 0; i < n; i++)
                restWorld[i] = Vector3.Transform(restLocalPositions[i], prepareAnchors[0]);
            float maxReach = 0.5f;
            for (int i = 0; i < distFromPin.Length; i++)
                if (float.IsFinite(distFromPin[i]) && distFromPin[i] > maxReach) maxReach = distFromPin[i];
            preparedColliders = SurfaceCollision.PrepareColliders(surfaceColliders, prepareAnchors, restWorld, maxReach);
        }
        else
        {
            preparedColliders = Array.Empty<LocalColliderMesh>();
        }

        var buckets = new List<int>[this.islandCount];
        for (int k = 0; k < this.islandCount; k++) buckets[k] = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int isl = (i < islandIds.Length && islandIds[i] >= 0 && islandIds[i] < this.islandCount) ? islandIds[i] : 0;
            buckets[isl].Add(i);
        }

        for (int isl = 0; isl < this.islandCount; isl++)
        {
            var verts = buckets[isl].ToArray();
            islandVertices[isl] = verts;
            if (verts.Length == 0)
            {
                islandAxisLocal[isl] = Vector3.UnitX;
                islandNormalLocal[isl] = Vector3.UnitZ;
                islandComRadius[isl] = 0.05f;
                islandInertia[isl] = 1f;
                continue;
            }

            var pivotSum = Vector3.Zero;
            int pinCount = 0;
            var comSum = Vector3.Zero;
            var normSum = Vector3.Zero;
            for (int k = 0; k < verts.Length; k++)
            {
                int idx = verts[k];
                comSum += restLocalPositions[idx];
                normSum += restLocalNormals[idx];
                if (isPinned[idx])
                {
                    pivotSum += restLocalPositions[idx];
                    pinCount++;
                }
            }

            Vector3 pivot = pinCount > 0 ? pivotSum / pinCount : restLocalPositions[verts[0]];
            Vector3 com = comSum / verts.Length;
            Vector3 avgNorm = normSum.LengthSquared() > 1e-12f ? Vector3.Normalize(normSum) : Vector3.UnitZ;

            // Fit dominant hinge axis across pinned vertices (fallback to horizontal orthogonal to normal).
            Vector3 bestAxis = Vector3.Zero;
            float bestSpanSq = 0f;
            if (pinCount >= 2)
            {
                for (int a = 0; a < verts.Length; a++)
                {
                    int ia = verts[a];
                    if (!isPinned[ia]) continue;
                    for (int b = a + 1; b < verts.Length; b++)
                    {
                        int ib = verts[b];
                        if (!isPinned[ib]) continue;
                        var diff = restLocalPositions[ib] - restLocalPositions[ia];
                        float dSq = (diff * absScale).LengthSquared();
                        if (dSq > bestSpanSq)
                        {
                            bestSpanSq = dSq;
                            bestAxis = diff;
                        }
                    }
                }
            }

            if (bestSpanSq < 1e-8f)
            {
                bestAxis = Vector3.Cross(avgNorm, Vector3.UnitY);
                if (bestAxis.LengthSquared() < 1e-6f)
                    bestAxis = Vector3.UnitX;
            }
            bestAxis = Vector3.Normalize(bestAxis);

            var comOffset = com - pivot;
            if ((comOffset * absScale).LengthSquared() < 1e-6f)
                comOffset = -Vector3.UnitY * 0.25f;

            // Orient hinge axis so positive angle rotates comOffset outward (+avgNorm).
            if (Vector3.Dot(Vector3.Cross(bestAxis, comOffset), avgNorm) < 0f)
                bestAxis = -bestAxis;

            var scaledAxis = NormalizeSafe(bestAxis * absScale);
            float inertia = 0f;
            for (int k = 0; k < verts.Length; k++)
            {
                var rel = (restLocalPositions[verts[k]] - pivot) * absScale;
                var perp = rel - scaledAxis * Vector3.Dot(rel, scaledAxis);
                inertia += perp.LengthSquared();
            }
            inertia = MathF.Max(inertia / verts.Length, 0.02f);

            islandPivotLocal[isl] = pivot;
            islandAxisLocal[isl] = bestAxis;
            islandNormalLocal[isl] = avgNorm;
            islandComOffsetLocal[isl] = comOffset;
            islandComRadius[isl] = MathF.Max((comOffset * absScale).Length(), 0.05f);
            islandInertia[isl] = inertia;
        }
    }

    internal void ResetToRest(
        ReadOnlySpan<Matrix4x4> anchorLocalToWorld,
        Span<Vector3> outLocalPositions)
    {
        if (anchorLocalToWorld.Length == 0)
        {
            int c = Math.Min(outLocalPositions.Length, restLocalPositions.Length);
            restLocalPositions.AsSpan(0, c).CopyTo(outLocalPositions);
            return;
        }

        var l2w0 = anchorLocalToWorld[0];

        for (int i = 0; i < restLocalPositions.Length; i++)
        {
            var wp = Vector3.Transform(restLocalPositions[i], l2w0);
            worldPos[i] = wp;
            prevPos[i] = wp;
            vel[i] = Vector3.Zero;
            if (i < outLocalPositions.Length)
                outLocalPositions[i] = restLocalPositions[i];
        }

        int aCount = Math.Min(prevAnchorL2W.Length, anchorLocalToWorld.Length);
        for (int a = 0; a < aCount; a++)
            prevAnchorL2W[a] = anchorLocalToWorld[a];

        for (int isl = 0; isl < islandCount; isl++)
        {
            islandAngle[isl] = 0f;
            islandAngVel[isl] = 0f;
            var pw = Vector3.Transform(islandPivotLocal[isl], l2w0);
            islandPrevPivotWorld[isl] = pw;
            islandPrevPivotVel[isl] = Vector3.Zero;
            lastIslandPivotWorld[isl] = pw;
            lastIslandAxisWorld[isl] = NormalizeSafe(Vector3.TransformNormal(islandAxisLocal[isl], l2w0));
            lastIslandAngle[isl] = 0f;
        }

        blastTimer = 0f;
        blastOrigin = Vector3.Zero;
        blastDir = Vector3.UnitZ;
        blastCaliber = 0f;
        initialized = true;
    }

    internal bool TryGetIslandSwing(
        int islandIndex,
        out Vector3 pivotWorld,
        out Vector3 axisWorld,
        out float angleRad)
    {
        if ((uint)islandIndex >= (uint)islandCount)
        {
            pivotWorld = Vector3.Zero;
            axisWorld = Vector3.UnitX;
            angleRad = 0f;
            return false;
        }
        pivotWorld = lastIslandPivotWorld[islandIndex];
        axisWorld = lastIslandAxisWorld[islandIndex];
        angleRad = lastIslandAngle[islandIndex];
        return true;
    }

    internal bool ApplyBlastImpulse(Vector3 muzzlePos, Vector3 muzzleDir, float caliberMm)
    {
        if (!initialized || !Finite(muzzlePos) || !Finite(muzzleDir) || !float.IsFinite(caliberMm))
            return false;

        var dirNorm = NormalizeSafe(muzzleDir);
        float cal = Math.Clamp(caliberMm, 20f, 300f);
        bool hit = ApplyOverpressure(muzzlePos, dirNorm, cal, impulseScale: 1.0f);
        if (hit)
        {
            blastTimer = 0.18f;
            blastOrigin = muzzlePos;
            blastDir = dirNorm;
            blastCaliber = cal;
        }
        return hit;
    }

    private bool ApplyOverpressure(Vector3 muzzlePos, Vector3 dirNorm, float cal, float impulseScale)
    {
        float maxRange = 8.0f + cal * 0.06f;
        float calFactor = MathF.Pow(cal / 100f, 0.85f);
        bool hit = false;

        if (motion == MotionProfile.Hinged)
        {
            float strength = 18f * calFactor * impulseScale;
            for (int isl = 0; isl < islandCount; isl++)
            {
                var verts = islandVertices[isl];
                if (verts.Length == 0) continue;
                var center = Vector3.Zero;
                for (int k = 0; k < verts.Length; k++)
                    center += worldPos[verts[k]];
                center /= verts.Length;

                var diff = center - muzzlePos;
                float dist = diff.Length();
                if (dist >= maxRange) continue;

                var rDir = dist > 1e-4f ? diff / dist : dirNorm;
                float cone = MathF.Max(0.55f, 0.70f + 0.30f * Vector3.Dot(rDir, dirNorm));
                float falloff = (1f - dist / maxRange) / (1f + dist * 0.22f);

                var kickDir = NormalizeSafe(rDir * 0.65f + dirNorm * 0.35f);
                var pivot = lastIslandPivotWorld[isl];
                var axis = lastIslandAxisWorld[isl];
                var comDir = center - pivot;
                float torque = Vector3.Dot(Vector3.Cross(comDir, kickDir), axis);
                if (MathF.Abs(torque) < 0.05f)
                    torque = 0.25f * islandComRadius[isl];

                islandAngVel[isl] = Math.Clamp(
                    islandAngVel[isl] + torque * (strength * cone * falloff) / islandInertia[isl],
                    -25f,
                    25f);
                hit = true;
            }
            return hit;
        }

        if (motion == MotionProfile.Cloth || motion == MotionProfile.Rubber)
        {
            float baseStrength = (motion == MotionProfile.Cloth ? 22f : 16f) * calFactor * impulseScale;
            for (int i = 0; i < worldPos.Length; i++)
            {
                if (isPinned[i]) continue;
                var diff = worldPos[i] - muzzlePos;
                float dist = diff.Length();
                if (dist >= maxRange) continue;

                var rDir = dist > 1e-4f ? diff / dist : dirNorm;
                float cone = MathF.Max(0.55f, 0.70f + 0.30f * Vector3.Dot(rDir, dirNorm));
                float falloff = (1f - dist / maxRange) / (1f + dist * 0.22f);
                float flutter = motion == MotionProfile.Cloth ? (0.80f + 0.40f * MathF.Sin(i * 2.13f)) : 1.0f;

                var impulse = NormalizeSafe(rDir * 0.65f + dirNorm * 0.35f) * (baseStrength * cone * falloff * flutter);
                var v = vel[i] + impulse;
                float speedSq = v.LengthSquared();
                if (speedSq > MaxSpeed * MaxSpeed)
                    v *= MaxSpeed / MathF.Sqrt(speedSq);
                vel[i] = v;
                hit = true;
            }
        }

        return hit;
    }

    internal void Step(
        float dt,
        float time,
        ReadOnlySpan<Matrix4x4> anchorLocalToWorld,
        float stiffness,
        float damping,
        float windScale,
        float maxSwingDeg,
        int substeps,
        Span<Vector3> outLocalPositions,
        GroundPlane groundPlane = default,
        bool enableSurfaceCollision = true,
        ReadOnlySpan<AirflowJet> airflowJets = default)
    {
        if (restLocalPositions.Length == 0 || outLocalPositions.Length < restLocalPositions.Length ||
            anchorLocalToWorld.Length == 0)
            return;

        var l2w0 = anchorLocalToWorld[0];
        if (!Matrix4x4.Invert(l2w0, out var plateWorldToLocal))
        {
            ResetToRest(anchorLocalToWorld, outLocalPositions);
            return;
        }

        if (!float.IsFinite(dt) || dt <= 0f)
        {
            WriteLocalFromWorld(plateWorldToLocal, outLocalPositions);
            return;
        }

        dt = MathF.Min(dt, MaxDt);

        if (!initialized ||
            !Finite(l2w0.Translation) ||
            Vector3.DistanceSquared(l2w0.Translation, prevAnchorL2W[0].Translation) > TeleportMetersSq)
        {
            ResetToRest(anchorLocalToWorld, outLocalPositions);
            return;
        }

        if (motion == MotionProfile.Static)
        {
            ResetToRest(anchorLocalToWorld, outLocalPositions);
            return;
        }

        if (motion == MotionProfile.Hinged)
        {
            if (blastTimer > 0f)
            {
                float blastFrac = Math.Clamp(blastTimer / 0.18f, 0f, 1f);
                ApplyOverpressure(blastOrigin, blastDir, blastCaliber, impulseScale: 3.5f * blastFrac * dt);
                blastTimer = MathF.Max(0f, blastTimer - dt);
            }
            StepHinged(dt, time, l2w0, plateWorldToLocal, stiffness, damping, windScale, maxSwingDeg, outLocalPositions, airflowJets);
            UpdatePreviousAnchors(anchorLocalToWorld);
            return;
        }

        if (blastTimer > 0f)
        {
            float blastFrac = Math.Clamp(blastTimer / 0.18f, 0f, 1f);
            ApplyOverpressure(blastOrigin, blastDir, blastCaliber, impulseScale: 3.5f * blastFrac * dt);
            blastTimer = MathF.Max(0f, blastTimer - dt);
        }

        int steps = Math.Clamp(substeps, 1, 8);
        float subDt = dt / steps;
        float subDtSq = subDt * subDt;
        int n = restLocalPositions.Length;
        float clampedStiff = Math.Clamp(stiffness, 0.05f, 1.0f);
        float clampedDamp = Math.Clamp(damping, 0.02f, 0.95f);
        float clampedWind = Math.Clamp(windScale, 0f, 2.0f);

        int aCount = Math.Min(prevAnchorL2W.Length, anchorLocalToWorld.Length);
        float thicknessFactor = Math.Clamp(MathF.Sqrt(avgThicknessM / 0.015f), 0.70f, 1.85f);
        var gravity = new Vector3(0f, -9.81f, 0f);
        float dampExp = MathF.Exp(-clampedDamp * 8.0f * subDt);
        float stiffCube = clampedStiff * clampedStiff * clampedStiff;
        float kBase = (8f + 950f * stiffCube) * thicknessFactor;

        for (int s = 0; s < steps; s++)
        {
            float prevFrac = (float)s / steps;
            float frac = (s + 1f) / steps;
            for (int a = 0; a < aCount; a++)
            {
                prevInterpAnchorL2W[a] = s == 0 ? prevAnchorL2W[a] : interpAnchorL2W[a];
                interpAnchorL2W[a] = LerpMatrix(prevAnchorL2W[a], anchorLocalToWorld[a], frac);
            }

            var prevIl2w0 = prevInterpAnchorL2W[0];
            var il2w0 = interpAnchorL2W[0];
            var normalMat = Matrix4x4.Invert(il2w0, out var invI0) ? Matrix4x4.Transpose(invI0) : il2w0;

            for (int i = 0; i < n; i++)
            {
                targetWorld[i] = Vector3.Transform(restLocalPositions[i], il2w0);
                prevTargetWorld[i] = Vector3.Transform(restLocalPositions[i], prevIl2w0);
            }

            // Subdivision-independent areal aerodynamic drag with smooth slipstream saturation.
            Array.Clear(aeroAccum, 0, n);
            Array.Clear(areaAccum, 0, n);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int i0 = triangles[t];
                int i1 = triangles[t + 1];
                int i2 = triangles[t + 2];
                var triCross = Vector3.Cross(worldPos[i1] - worldPos[i0], worldPos[i2] - worldPos[i0]);
                float area2 = triCross.Length();
                if (area2 < 1e-7f) continue;
                var nrm = triCross / area2;
                float area = area2 * 0.5f;
                var c = (worldPos[i0] + worldPos[i1] + worldPos[i2]) * (1f / 3f);
                var avgV = (vel[i0] + vel[i1] + vel[i2]) * (1f / 3f);

                Vector3 wind = Vector3.Zero;
                if (clampedWind > 1e-4f)
                {
                    float wx = MathF.Sin(time * 2.1f + c.Z * 1.7f + c.Y * 0.9f);
                    float wy = 0.25f * MathF.Sin(time * 3.3f + c.X * 1.9f);
                    float wz = MathF.Cos(time * 1.7f + c.X * 1.4f - c.Y * 1.1f);
                    wind = new Vector3(wx, wy, wz) * (0.9f * clampedWind);
                }

                var relV = avgV - wind;
                float relSpeed = relV.Length();
                float effSpeed = relSpeed / (1f + relSpeed / 10f);
                var relDir = relSpeed > 1e-4f ? relV / relSpeed : Vector3.Zero;
                float vnFrac = Vector3.Dot(relDir, nrm);
                float dragScale = motion == MotionProfile.Cloth ? (0.18f + 0.14f * clampedWind) : 0.04f;
                float maxDrag = motion == MotionProfile.Cloth ? 14.0f : 4.0f;
                var dragAccel = -(nrm * (vnFrac * MathF.Abs(vnFrac)) + relDir * 0.20f) * (effSpeed * effSpeed * dragScale);
                float dragSq = dragAccel.LengthSquared();
                if (dragSq > maxDrag * maxDrag)
                    dragAccel *= maxDrag / MathF.Sqrt(dragSq);
                var weighted = dragAccel * area;
                aeroAccum[i0] += weighted;
                aeroAccum[i1] += weighted;
                aeroAccum[i2] += weighted;
                areaAccum[i0] += area;
                areaAccum[i1] += area;
                areaAccum[i2] += area;
            }

            // Relative-to-carrier velocity integration + damping + areal aero acceleration.
            for (int i = 0; i < n; i++)
            {
                if (isPinned[i])
                {
                    int a = anchorIndex[i] < aCount ? anchorIndex[i] : 0;
                    var pinnedWorld = Vector3.Transform(anchorLocalPos[i], interpAnchorL2W[a]);
                    worldPos[i] = pinnedWorld;
                    prevPos[i] = pinnedWorld;
                    vel[i] = Vector3.Zero;
                    continue;
                }

                prevPos[i] = worldPos[i];
                var vCarrier = (targetWorld[i] - prevTargetWorld[i]) / subDt;
                float cSq = vCarrier.LengthSquared();
                if (cSq > MaxSpeed * MaxSpeed)
                    vCarrier *= MaxSpeed / MathF.Sqrt(cSq);

                var v = vel[i];
                v += gravity * subDt;
                v = vCarrier + (v - vCarrier) * dampExp;
                if (areaAccum[i] > 1e-8f)
                    v += (aeroAccum[i] / areaAccum[i]) * subDt;

                if (!airflowJets.IsEmpty)
                {
                    float jetScale = motion == MotionProfile.Cloth ? 1.0f : 0.45f;
                    for (int j = 0; j < airflowJets.Length; j++)
                    {
                        if (TryEvaluateJetAt(airflowJets[j], worldPos[i], time, i, out var jetAccel))
                            v += jetAccel * (jetScale * subDt);
                    }
                }

                float speedSq = v.LengthSquared();
                if (speedSq > MaxSpeed * MaxSpeed)
                    v *= MaxSpeed / MathF.Sqrt(speedSq);

                vel[i] = v;
                worldPos[i] += v * subDt;
            }

            if (motion == MotionProfile.Rubber)
            {
                for (int i = 0; i < n; i++)
                {
                    if (isPinned[i]) continue;
                    float kVert = kBase / (1f + distFromPin[i] * 1.4f);
                    float alphaShape = 1f / (kVert * subDtSq);
                    float pull = 1f / (1f + alphaShape);
                    var delta = worldPos[i] - targetWorld[i];
                    worldPos[i] -= delta * pull;

                    // Resist deep inward penetration into the plate backing.
                    var restN = NormalizeSafe(Vector3.TransformNormal(restLocalNormals[i], normalMat));
                    float inward = Vector3.Dot(worldPos[i] - targetWorld[i], restN);
                    if (inward < -0.015f)
                        worldPos[i] -= restN * ((inward + 0.015f) * 0.75f);
                }
            }

            EnforceDisplacementClamps(targetWorld, clampedStiff);

            // XPBD bending (2 alternating sweeps) followed by stretch (2 alternating sweeps)
            // so edge rest lengths are enforced cleanly after clamping and bending.
            float rubberBendStiff = 0.45f + 0.55f * clampedStiff;
            float bendCompliance = motion == MotionProfile.Cloth
                ? 0.00018f / stiffCube
                : 0.00018f / (rubberBendStiff * rubberBendStiff * thicknessFactor * thicknessFactor);
            float alphaBend = bendCompliance / subDtSq;
            for (int bIdx = 0; bIdx < bendingPairs.Length; bIdx++)
                SolveBendPair(bIdx, alphaBend);
            for (int bIdx = bendingPairs.Length - 1; bIdx >= 0; bIdx--)
                SolveBendPair(bIdx, alphaBend);

            const float stretchCompliance = 1e-7f;
            float alphaStretch = stretchCompliance / subDtSq;

            for (int eIdx = 0; eIdx < edges.Length; eIdx++)
                SolveStretchEdge(eIdx, alphaStretch);
            for (int eIdx = edges.Length - 1; eIdx >= 0; eIdx--)
                SolveStretchEdge(eIdx, alphaStretch);

            if (enableSurfaceCollision && (preparedColliders.Length > 0 || groundPlane.Valid))
            {
                if (preparedColliders.Length > 0)
                {
                    for (int a = 0; a < aCount; a++)
                    {
                        if (!Matrix4x4.Invert(interpAnchorL2W[a], out anchorW2L[a]))
                            anchorW2L[a] = Matrix4x4.Identity;
                    }
                    SurfaceCollision.ResolveSurfaceCollisions(
                        worldPos, prevPos, isPinned, distFromPin,
                        preparedColliders, interpAnchorL2W, anchorW2L, prevInterpAnchorL2W);
                }
                if (groundPlane.Valid)
                    SurfaceCollision.ResolveGroundCollision(worldPos, prevPos, isPinned, groundPlane);

                for (int eIdx = 0; eIdx < edges.Length; eIdx++)
                    SolveStretchEdge(eIdx, alphaStretch, tensionOnly: true);

                EnforceDisplacementClamps(targetWorld, clampedStiff);
            }

            // Re-pin fixed vertices cleanly and derive velocity.
            for (int i = 0; i < n; i++)
            {
                if (isPinned[i])
                {
                    int a = anchorIndex[i] < aCount ? anchorIndex[i] : 0;
                    worldPos[i] = Vector3.Transform(anchorLocalPos[i], interpAnchorL2W[a]);
                    vel[i] = Vector3.Zero;
                    continue;
                }

                if (!Finite(worldPos[i]))
                {
                    ResetToRest(anchorLocalToWorld, outLocalPositions);
                    return;
                }

                var newVel = (worldPos[i] - prevPos[i]) / subDt;
                float speedSq = newVel.LengthSquared();
                if (speedSq > MaxSpeed * MaxSpeed)
                    newVel *= MaxSpeed / MathF.Sqrt(speedSq);
                vel[i] = newVel;
            }
        }

        UpdatePreviousAnchors(anchorLocalToWorld);
        WriteLocalFromWorld(plateWorldToLocal, outLocalPositions);
    }

    private void SolveStretchEdge(int eIdx, float alphaStretch, bool tensionOnly = false)
    {
        var e = edges[eIdx];
        bool pinA = isPinned[e.A];
        bool pinB = isPinned[e.B];
        if (pinA && pinB) return;

        var diff = worldPos[e.B] - worldPos[e.A];
        float len = diff.Length();
        if (len < 1e-7f) return;
        float c = len - e.RestLength;
        if (tensionOnly && c <= 0f) return;
        float wA = pinA ? 0f : 1f;
        float wB = pinB ? 0f : 1f;
        float denom = wA + wB + alphaStretch;
        var corr = diff * (c / (len * denom));
        if (!pinA) worldPos[e.A] += corr * wA;
        if (!pinB) worldPos[e.B] -= corr * wB;
    }

    private void SolveBendPair(int bIdx, float alphaBend)
    {
        var bp = bendingPairs[bIdx];
        bool pinC = isPinned[bp.C];
        bool pinD = isPinned[bp.D];
        if (pinC && pinD) return;

        var diff = worldPos[bp.D] - worldPos[bp.C];
        float len = diff.Length();
        if (len < 1e-7f) return;
        float c = len - bp.RestDistance;
        float wC = pinC ? 0f : 1f;
        float wD = pinD ? 0f : 1f;
        float denom = wC + wD + alphaBend;
        var corr = diff * (c / (len * denom));
        if (!pinC) worldPos[bp.C] += corr * wC;
        if (!pinD) worldPos[bp.D] -= corr * wD;
    }

    private void EnforceDisplacementClamps(Vector3[] targetWorld, float clampedStiff)
    {
        if (motion == MotionProfile.Rubber)
        {
            float maxRatio = Math.Clamp(0.55f - 0.38f * clampedStiff, 0.16f, 0.55f);
            for (int i = 0; i < worldPos.Length; i++)
            {
                if (isPinned[i]) continue;
                var postDelta = worldPos[i] - targetWorld[i];
                float maxDisp = MathF.Max(0.02f, distFromPin[i] * maxRatio);
                float dSq = postDelta.LengthSquared();
                if (dSq > maxDisp * maxDisp)
                    worldPos[i] = targetWorld[i] + postDelta * (maxDisp / MathF.Sqrt(dSq));
            }
        }
        else if (motion == MotionProfile.Cloth)
        {
            for (int i = 0; i < worldPos.Length; i++)
            {
                if (isPinned[i]) continue;
                int rIdx = tetherRef[i];
                if (rIdx < 0) continue;
                float maxLen = 1.08f * distFromPin[i] + 0.02f;
                var diff = worldPos[i] - worldPos[rIdx];
                float dSq = diff.LengthSquared();
                if (dSq > maxLen * maxLen)
                    worldPos[i] = worldPos[rIdx] + diff * (maxLen / MathF.Sqrt(dSq));
            }
        }
    }

    private void StepHinged(
        float dt,
        float time,
        Matrix4x4 l2w0,
        Matrix4x4 plateWorldToLocal,
        float stiffness,
        float damping,
        float windScale,
        float maxSwingDeg,
        Span<Vector3> outLocalPositions,
        ReadOnlySpan<AirflowJet> airflowJets = default)
    {
        float clampedStiff = Math.Clamp(stiffness, 0.05f, 1.0f);
        float clampedDamp = Math.Clamp(damping, 0.02f, 0.95f);
        float clampedWind = Math.Clamp(windScale, 0f, 2.0f);
        float deg2rad = MathF.PI / 180f;
        float clampedMaxDeg = Math.Clamp(maxSwingDeg, 5f, 75f);
        float baseMaxRad = clampedMaxDeg * deg2rad;
        float baseMinRad = -MathF.Min(4f, clampedMaxDeg * 0.15f) * deg2rad;

        var normalMat0 = Matrix4x4.Transpose(plateWorldToLocal);
        var gravity = new Vector3(0f, -9.81f, 0f);

        for (int isl = 0; isl < islandCount; isl++)
        {
            var verts = islandVertices[isl];
            if (verts.Length == 0) continue;

            var pivotW = Vector3.Transform(islandPivotLocal[isl], l2w0);
            var axisW = NormalizeSafe(Vector3.TransformNormal(islandAxisLocal[isl], l2w0));
            var normalW = NormalizeSafe(Vector3.TransformNormal(islandNormalLocal[isl], normalMat0));
            var comWorldOffset = Vector3.Transform(islandPivotLocal[isl] + islandComOffsetLocal[isl], l2w0) - pivotW;

            if (Vector3.Dot(Vector3.Cross(axisW, comWorldOffset), normalW) < 0f)
                axisW = -axisW;

            var cDir = comWorldOffset - axisW * Vector3.Dot(comWorldOffset, axisW);
            var downDir = -Vector3.UnitY - axisW * Vector3.Dot(-Vector3.UnitY, axisW);
            float verticalAngle = 0f;
            if (cDir.LengthSquared() > 1e-8f && downDir.LengthSquared() > 1e-8f)
            {
                cDir = Vector3.Normalize(cDir);
                downDir = Vector3.Normalize(downDir);
                verticalAngle = MathF.Atan2(Vector3.Dot(Vector3.Cross(cDir, downDir), axisW), Vector3.Dot(cDir, downDir));
            }
            float minRad = MathF.Min(baseMinRad, verticalAngle - 4f * deg2rad);
            float maxRad = MathF.Max(baseMaxRad, verticalAngle + 4f * deg2rad);

            var vPivot = (pivotW - islandPrevPivotWorld[isl]) / dt;
            var aPivot = (vPivot - islandPrevPivotVel[isl]) / dt;
            float accelSq = aPivot.LengthSquared();
            if (accelSq > MaxPivotAccel * MaxPivotAccel)
                aPivot *= MaxPivotAccel / MathF.Sqrt(accelSq);

            islandPrevPivotWorld[isl] = pivotW;
            islandPrevPivotVel[isl] = vPivot;

            float angle = islandAngle[isl];
            float angVel = islandAngVel[isl];

            var swingQ = Quaternion.CreateFromAxisAngle(axisW, angle);
            var comDirW = Vector3.Transform(comWorldOffset, swingQ);
            var swungNormalW = Vector3.Transform(normalW, swingQ);

            var effAccel = gravity - aPivot;
            float gravTorque = Vector3.Dot(Vector3.Cross(comDirW, effAccel), axisW);

            float gust = clampedWind > 1e-4f
                ? (MathF.Sin(time * 2.4f + pivotW.X + pivotW.Z) * 1.4f + 0.6f) * clampedWind
                : 0f;
            float airNormal = Vector3.Dot(swungNormalW * gust - vPivot, swungNormalW);
            float airTorque = airNormal * islandComRadius[isl] * 0.45f;

            float springK = 0.4f + 8.0f * clampedStiff * clampedStiff;
            float springTorque = -springK * (angle - verticalAngle);
            float dampTorque = -clampedDamp * 4.0f * angVel;

            float jetTorque = 0f;
            if (!airflowJets.IsEmpty)
            {
                var islandCenter = pivotW + comDirW;
                for (int j = 0; j < airflowJets.Length; j++)
                {
                    if (!TryEvaluateJetAt(airflowJets[j], islandCenter, time, isl, out var jetAccel))
                        continue;
                    float tComp = Vector3.Dot(Vector3.Cross(comDirW, jetAccel), axisW);
                    if (MathF.Abs(tComp) < 1e-4f)
                        tComp = jetAccel.Length() * islandComRadius[isl] * 0.20f;
                    jetTorque += tComp * 0.35f;
                }
            }

            float alpha = (gravTorque + airTorque + springTorque + dampTorque + jetTorque) / islandInertia[isl];
            angVel += alpha * dt;
            angle += angVel * dt;

            if (angle > maxRad)
            {
                angle = maxRad;
                if (angVel > 0f) angVel = 0f;
            }
            else if (angle < minRad)
            {
                angle = minRad;
                if (angVel < 0f) angVel = 0f;
            }

            if (!float.IsFinite(angle) || !float.IsFinite(angVel))
            {
                angle = 0f;
                angVel = 0f;
            }

            islandAngle[isl] = angle;
            islandAngVel[isl] = angVel;
            lastIslandPivotWorld[isl] = pivotW;
            lastIslandAxisWorld[isl] = axisW;
            lastIslandAngle[isl] = angle;

            var finalSwingW = Quaternion.CreateFromAxisAngle(axisW, angle);
            for (int k = 0; k < verts.Length; k++)
            {
                int idx = verts[k];
                var wpRest = Vector3.Transform(restLocalPositions[idx], l2w0);
                var wp = isPinned[idx]
                    ? wpRest
                    : pivotW + Vector3.Transform(wpRest - pivotW, finalSwingW);
                worldPos[idx] = wp;
                prevPos[idx] = wp;
                vel[idx] = Vector3.Zero;
                outLocalPositions[idx] = Vector3.Transform(wp, plateWorldToLocal);
            }
        }
    }

    private void UpdatePreviousAnchors(ReadOnlySpan<Matrix4x4> anchorLocalToWorld)
    {
        int aCount = Math.Min(prevAnchorL2W.Length, anchorLocalToWorld.Length);
        for (int a = 0; a < aCount; a++)
            prevAnchorL2W[a] = anchorLocalToWorld[a];
    }

    private void WriteLocalFromWorld(Matrix4x4 plateWorldToLocal, Span<Vector3> outLocalPositions)
    {
        for (int i = 0; i < restLocalPositions.Length; i++)
            outLocalPositions[i] = Vector3.Transform(worldPos[i], plateWorldToLocal);
    }

    private static bool TryEvaluateJetAt(
        in AirflowJet jet,
        Vector3 pointWorld,
        float time,
        int sampleIndex,
        out Vector3 accel)
    {
        accel = Vector3.Zero;
        if (jet.Strength <= 1e-4f || jet.RangeM <= 1e-4f || jet.RadiusM <= 1e-4f)
            return false;

        var jDir = NormalizeSafe(jet.Direction);
        var toPoint = pointWorld - jet.Origin;
        float axial = Vector3.Dot(toPoint, jDir);
        if (axial < -0.15f || axial > jet.RangeM)
            return false;

        float radialSq = (toPoint - jDir * axial).LengthSquared();
        float coneRadius = jet.RadiusM + MathF.Max(0f, axial) * 0.35f;
        if (radialSq > coneRadius * coneRadius)
            return false;

        float axialFalloff = axial < 0f
            ? Math.Clamp(1f + axial / 0.15f, 0f, 1f)
            : 1f - axial / jet.RangeM;
        float radialFalloff = 1f - MathF.Sqrt(radialSq) / coneRadius;
        float pulse = 0.70f + 0.30f * MathF.Sin(time * jet.PulseHz * MathF.PI * 2f + jet.Phase - axial * 3.0f);
        float swirl = MathF.Sin(time * 11.0f + sampleIndex * 1.73f + jet.Phase) * 0.35f;
        var lateral = Vector3.Cross(jDir, MathF.Abs(jDir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
        var pushDir = NormalizeSafe(jDir + lateral * swirl);
        accel = pushDir * (jet.Strength * axialFalloff * radialFalloff * pulse);
        return true;
    }

    private static Matrix4x4 LerpMatrix(Matrix4x4 a, Matrix4x4 b, float t) =>
        t >= 1f ? b : a * (1f - t) + b * t;

    private static Vector3 NormalizeSafe(Vector3 v)
    {
        float lenSq = v.LengthSquared();
        return lenSq > 1e-12f && Finite(v) ? v / MathF.Sqrt(lenSq) : Vector3.UnitY;
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
