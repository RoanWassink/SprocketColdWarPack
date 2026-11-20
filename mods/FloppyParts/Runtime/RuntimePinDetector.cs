using Sprocket.PlateMesh;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.CrewSystems;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Exhausts;
using Sprocket.Vehicles.FuelTanks;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.Tracks;
using Sprocket.Vehicles.Transmissions;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;
using Sprocket.Vehicles.Weapons.Cannons;
using UnityEngine;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace SprocketFloppyParts;

internal static class RuntimePinDetector
{
    internal sealed record AnchorScan(
        Transform[] Anchors,
        NMatrix[] WorldToLocal,
        RigidColliderSample[] Colliders,
        LocalColliderMesh[] LocalColliders,
        string[] AnchorNames,
        EngineExhaust[] Exhausts,
        EngineVent[] Vents,
        CombustionEngine[] Engines);

    internal static AnchorScan Scan(PlateStructure selfPlate)
    {
        var anchors = new List<Transform> { selfPlate.transform };
        var worldToLocal = new List<NMatrix> { ToNumerics(selfPlate.transform.worldToLocalMatrix) };
        var colliders = new List<RigidColliderSample>();
        var localColliders = new List<LocalColliderMesh>();
        var names = new List<string> { CleanName(selfPlate.gameObject.name, $"Plate #{selfPlate.VUID.Value}") };
        var exhausts = new List<EngineExhaust>();
        var vents = new List<EngineVent>();
        var engines = new List<CombustionEngine>();

        try
        {
            var selfVo = selfPlate.VehicleObject;
            IntPtr selfVoPtr = selfVo != null ? selfVo.Pointer : IntPtr.Zero;
            var items = selfVo?.Vehicle?.ObjectReader?.Items;
            if (items == null)
            {
                return new AnchorScan(
                    anchors.ToArray(), worldToLocal.ToArray(),
                    colliders.ToArray(), localColliders.ToArray(), names.ToArray(),
                    Array.Empty<EngineExhaust>(), Array.Empty<EngineVent>(), Array.Empty<CombustionEngine>());
            }

            int count = items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for (int i = 0; i < count; i++)
            {
                var obj = items[i];
                if (obj == null || obj.WasCollected || obj.Released) continue;
                if (selfVoPtr != IntPtr.Zero && obj.Pointer == selfVoPtr) continue;

                var vTrans = obj.VehicleTransform;
                if (vTrans == null || vTrans.WasCollected) continue;
                var anchorTransform = vTrans.transform;
                if (anchorTransform == null) continue;

                PlateStructure? otherPlate = null;
                bool skipSoftBody = false;
                var comps = obj.Components;
                if (comps != null)
                {
                    int compCount = comps.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                    for (int c = 0; c < compCount; c++)
                    {
                        var comp = comps[c];
                        if (comp == null || comp.WasCollected) continue;
                        if (comp.TryCast<EngineExhaust>() is { } ex) exhausts.Add(ex);
                        if (comp.TryCast<EngineVent>() is { } vent) vents.Add(vent);
                        if (comp.TryCast<CombustionEngine>() is { } eng) engines.Add(eng);
                        if (comp.TryCast<PlateStructure>() is { } p)
                        {
                            if (p.Pointer == selfPlate.Pointer || PlateConfigStore.Get(p).Motion != MotionProfile.Static)
                            {
                                skipSoftBody = true;
                                break;
                            }
                            otherPlate ??= p;
                        }
                    }
                }
                if (skipSoftBody || IsInternalOnlyObject(obj)) continue;

                int anchorIdx = anchors.Count;
                string label = CleanName(
                    vTrans.gameObject != null ? vTrans.gameObject.name : "",
                    otherPlate != null ? $"Plate #{otherPlate.VUID.Value}" : $"Part #{obj.GetInstanceID()}");

                bool addedSample = otherPlate != null
                    ? TrySamplePlate(otherPlate, anchorTransform, anchorIdx, label, colliders, localColliders)
                    : TrySampleRigidObject(vTrans, vTrans.gameObject, anchorTransform, anchorIdx, label, colliders, localColliders);

                if (addedSample)
                {
                    anchors.Add(anchorTransform);
                    worldToLocal.Add(ToNumerics(anchorTransform.worldToLocalMatrix));
                    names.Add(label);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[PinDetector] Scan warning: {ex.Message}");
        }

        return new AnchorScan(
            anchors.ToArray(),
            worldToLocal.ToArray(),
            colliders.ToArray(),
            localColliders.ToArray(),
            names.ToArray(),
            exhausts.ToArray(),
            vents.ToArray(),
            engines.ToArray());
    }

    private static bool IsInternalOnlyObject(VehicleObject obj)
    {
        var comps = obj.Components;
        if (comps == null) return false;
        bool hasInternal = false;
        int compCount = comps.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        for (int c = 0; c < compCount; c++)
        {
            var comp = comps[c];
            if (comp == null || comp.WasCollected) continue;
            if (comp.TryCast<PlateStructure>() != null ||
                comp.TryCast<Cannon>() != null ||
                comp.TryCast<CannonBehaviour>() != null ||
                comp.TryCast<TrackAssembly>() != null ||
                comp.TryCast<WheelMount>() != null ||
                comp.TryCast<Suspension>() != null)
                return false;

            if (comp.TryCast<TurretRing>() != null ||
                comp.TryCast<CrewSeat>() != null ||
                comp.TryCast<AmmoRack>() != null ||
                comp.TryCast<FuelTank>() != null ||
                comp.TryCast<CombustionEngine>() != null ||
                comp.TryCast<TransmissionBlock>() != null ||
                comp.TryCast<TraverseMotor>() != null ||
                comp.TryCast<LayingDrive>() != null)
                hasInternal = true;
        }
        return hasInternal;
    }

    private static bool TrySamplePlate(
        PlateStructure plate,
        Transform anchorTransform,
        int anchorIdx,
        string label,
        List<RigidColliderSample> colliders,
        List<LocalColliderMesh> localColliders)
    {
        if (!plate.HasMesh || plate.meshRefresh) return false;
        var native = plate.Mesh;
        if (native == null || native.Released || native.RebuildRequired) return false;

        var front = native.frontMesh;
        var back = native.thickenFaceMesh;
        if (front == null && back == null) return false;

        var wPts = new List<NVector>();
        var lPts = new List<NVector>();
        var lNorms = new List<NVector>();
        var tris = new List<int>();
        var wMin = new NVector(float.PositiveInfinity);
        var wMax = new NVector(float.NegativeInfinity);
        var lMin = new NVector(float.PositiveInfinity);
        var lMax = new NVector(float.NegativeInfinity);

        var xform = plate.transform.localToWorldMatrix;
        var toAnchor = anchorTransform.worldToLocalMatrix * xform;
        var normalMat = toAnchor.inverse.transpose;
        bool mirrored = plate.Mirrored;
        Matrix4x4 mirrorM = Matrix4x4.identity;
        Matrix4x4 mirrorNormalM = Matrix4x4.identity;
        bool flipMirrorWinding = false;
        if (mirrored)
        {
            plate.GetMirrorMatrix(out mirrorM);
            mirrorNormalM = mirrorM.inverse.transpose;
            flipMirrorWinding = mirrorM.determinant < 0f;
        }

        void AppendTess(TessellatedMesh? tm)
        {
            if (tm == null) return;
            var verts = tm.vertices;
            var norms = tm.normals;
            var tArr = tm.triangles;
            if (verts == null || tArr == null || verts.Length == 0 || tArr.Length < 3) return;
            int needed = mirrored ? verts.Length * 2 : verts.Length;
            int offset = wPts.Count;
            if (offset + needed > 16384) return;
            bool hasNorms = norms != null && norms.Length == verts.Length;

            for (int i = 0; i < verts.Length; i++)
            {
                var wp = xform.MultiplyPoint3x4(verts[i]);
                var nwp = new NVector(wp.x, wp.y, wp.z);
                wPts.Add(nwp);
                wMin = NVector.Min(wMin, nwp);
                wMax = NVector.Max(wMax, nwp);

                var lp = toAnchor.MultiplyPoint3x4(verts[i]);
                var nlp = new NVector(lp.x, lp.y, lp.z);
                lPts.Add(nlp);
                lMin = NVector.Min(lMin, nlp);
                lMax = NVector.Max(lMax, nlp);

                var ln = hasNorms ? normalMat.MultiplyVector(norms![i]) : Vector3.up;
                var nln = new NVector(ln.x, ln.y, ln.z);
                lNorms.Add(nln.LengthSquared() > 1e-12f ? NVector.Normalize(nln) : NVector.UnitY);
            }
            for (int i = 0; i < tArr.Length; i++)
                tris.Add(tArr[i] + offset);

            if (mirrored)
            {
                int mOffset = wPts.Count;
                for (int i = 0; i < verts.Length; i++)
                {
                    var mv = mirrorM.MultiplyPoint3x4(verts[i]);
                    var wp = xform.MultiplyPoint3x4(mv);
                    var nwp = new NVector(wp.x, wp.y, wp.z);
                    wPts.Add(nwp);
                    wMin = NVector.Min(wMin, nwp);
                    wMax = NVector.Max(wMax, nwp);

                    var lp = toAnchor.MultiplyPoint3x4(mv);
                    var nlp = new NVector(lp.x, lp.y, lp.z);
                    lPts.Add(nlp);
                    lMin = NVector.Min(lMin, nlp);
                    lMax = NVector.Max(lMax, nlp);

                    var mn = hasNorms ? mirrorNormalM.MultiplyVector(norms![i]) : Vector3.up;
                    var ln = normalMat.MultiplyVector(mn);
                    var nln = new NVector(ln.x, ln.y, ln.z);
                    lNorms.Add(nln.LengthSquared() > 1e-12f ? NVector.Normalize(nln) : NVector.UnitY);
                }
                for (int t = 0; t + 2 < tArr.Length; t += 3)
                {
                    tris.Add(tArr[t] + mOffset);
                    tris.Add(tArr[flipMirrorWinding ? t + 2 : t + 1] + mOffset);
                    tris.Add(tArr[flipMirrorWinding ? t + 1 : t + 2] + mOffset);
                }
            }
        }

        AppendTess(front);
        NVector frontLMin = lMin;
        NVector frontLMax = lMax;
        NVector[] frontPts = lPts.ToArray();
        NVector[] frontNorms = lNorms.ToArray();
        int[] frontTriArr = tris.ToArray();

        AppendTess(back);

        if (wPts.Count == 0 || tris.Count < 3) return false;

        var triArr = tris.ToArray();
        colliders.Add(new RigidColliderSample(anchorIdx, wMin, wMax, wPts.ToArray(), triArr, label));

        if (frontPts.Length > 0 && frontTriArr.Length >= 3)
        {
            BuildTriangleData(frontPts, frontNorms, frontTriArr, out var frontTriNormals, out var frontTriMin, out var frontTriMax);
            localColliders.Add(new LocalColliderMesh(
                anchorIdx, frontLMin, frontLMax, frontPts, frontTriArr, frontTriNormals, frontTriMin, frontTriMax, IsPlate: true));
        }
        return true;
    }

    private static bool TrySampleRigidObject(
        VehicleTransform ownerVt,
        GameObject? go,
        Transform anchorTransform,
        int anchorIdx,
        string label,
        List<RigidColliderSample> colliders,
        List<LocalColliderMesh> localColliders)
    {
        if (go == null || !go.activeInHierarchy) return false;
        bool any = false;
        var anchorW2L = anchorTransform.worldToLocalMatrix;

        var filters = go.GetComponentsInChildren<MeshFilter>(false);
        for (int f = 0; f < filters.Length; f++)
        {
            var mf = filters[f];
            if (mf == null) continue;
            var parentVt = mf.GetComponentInParent<VehicleTransform>();
            if (parentVt != null && parentVt.Pointer != ownerVt.Pointer) continue;
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;

            var xform = mf.transform.localToWorldMatrix;
            var toAnchor = anchorW2L * xform;

            if (mesh.isReadable && mesh.vertexCount > 0 && mesh.vertexCount <= 4096)
            {
                var verts = mesh.vertices;
                var norms = mesh.normals;
                var tris = mesh.triangles;
                if (verts != null && tris != null && verts.Length > 0 && tris.Length >= 3)
                {
                    var wPts = new NVector[verts.Length];
                    var lPts = new NVector[verts.Length];
                    var lNorms = norms != null && norms.Length == verts.Length ? new NVector[verts.Length] : null;
                    var normalMat = toAnchor.inverse.transpose;

                    var wMin = new NVector(float.PositiveInfinity);
                    var wMax = new NVector(float.NegativeInfinity);
                    var lMin = new NVector(float.PositiveInfinity);
                    var lMax = new NVector(float.NegativeInfinity);

                    for (int i = 0; i < verts.Length; i++)
                    {
                        var wp = xform.MultiplyPoint3x4(verts[i]);
                        var nwp = new NVector(wp.x, wp.y, wp.z);
                        wPts[i] = nwp;
                        wMin = NVector.Min(wMin, nwp);
                        wMax = NVector.Max(wMax, nwp);

                        var lp = toAnchor.MultiplyPoint3x4(verts[i]);
                        var nlp = new NVector(lp.x, lp.y, lp.z);
                        lPts[i] = nlp;
                        lMin = NVector.Min(lMin, nlp);
                        lMax = NVector.Max(lMax, nlp);

                        if (lNorms != null)
                        {
                            var ln = normalMat.MultiplyVector(norms![i]);
                            var nln = new NVector(ln.x, ln.y, ln.z);
                            lNorms[i] = nln.LengthSquared() > 1e-12f ? NVector.Normalize(nln) : NVector.UnitY;
                        }
                    }

                    var triArr = new int[tris.Length];
                    for (int i = 0; i < tris.Length; i++)
                        triArr[i] = tris[i];

                    BuildTriangleData(lPts, lNorms, triArr, out var triNormals, out var triMin, out var triMax);
                    colliders.Add(new RigidColliderSample(anchorIdx, wMin, wMax, wPts, triArr, label));
                    localColliders.Add(new LocalColliderMesh(
                        anchorIdx, lMin, lMax, lPts, triArr, triNormals, triMin, triMax, IsPlate: false));
                    any = true;
                    continue;
                }
            }

            // Unreadable mesh fallback: transform sharedMesh.bounds (always readable) into tight local OBB.
            var mb = mesh.bounds;
            if (mb.size.sqrMagnitude >= 1e-8f)
            {
                TransformBoundsToAabb(mb, xform, out var wMin, out var wMax);
                TransformBoundsToAabb(mb, toAnchor, out var lMin, out var lMax);
                colliders.Add(new RigidColliderSample(anchorIdx, wMin, wMax, null, null, label));
                localColliders.Add(new LocalColliderMesh(
                    anchorIdx, lMin, lMax, null, null, null, null, null, IsPlate: false));
                any = true;
            }
        }

        if (any) return true;

        var renderers = go.GetComponentsInChildren<Renderer>(false);
        for (int r = 0; r < renderers.Length; r++)
        {
            var ren = renderers[r];
            if (ren == null || !ren.enabled) continue;
            var parentVt = ren.GetComponentInParent<VehicleTransform>();
            if (parentVt != null && parentVt.Pointer != ownerVt.Pointer) continue;
            var b = ren.bounds;
            if (b.size.sqrMagnitude < 1e-8f) continue;
            var bMin = b.min;
            var bMax = b.max;
            TransformBoundsToAabb(b, anchorW2L, out var lMin, out var lMax);
            colliders.Add(new RigidColliderSample(
                anchorIdx,
                new NVector(bMin.x, bMin.y, bMin.z),
                new NVector(bMax.x, bMax.y, bMax.z),
                null,
                null,
                label));
            localColliders.Add(new LocalColliderMesh(
                anchorIdx, lMin, lMax, null, null, null, null, null, IsPlate: false));
            any = true;
        }

        return any;
    }

    private static void BuildTriangleData(
        NVector[] vertices,
        NVector[]? vertexNormals,
        int[] triangles,
        out NVector[] triNormals,
        out NVector[] triMin,
        out NVector[] triMax)
    {
        int triCount = triangles.Length / 3;
        triNormals = new NVector[triCount];
        triMin = new NVector[triCount];
        triMax = new NVector[triCount];

        var meshCenter = NVector.Zero;
        if (vertexNormals == null && vertices.Length > 0)
        {
            for (int i = 0; i < vertices.Length; i++)
                meshCenter += vertices[i];
            meshCenter /= vertices.Length;
        }

        for (int t = 0; t < triCount; t++)
        {
            int i0 = triangles[t * 3];
            int i1 = triangles[t * 3 + 1];
            int i2 = triangles[t * 3 + 2];
            var a = vertices[i0];
            var b = vertices[i1];
            var c = vertices[i2];

            triMin[t] = NVector.Min(a, NVector.Min(b, c));
            triMax[t] = NVector.Max(a, NVector.Max(b, c));

            var cross = NVector.Cross(b - a, c - a);
            var n = cross.LengthSquared() > 1e-12f ? NVector.Normalize(cross) : NVector.UnitY;

            if (vertexNormals != null)
            {
                var vSum = vertexNormals[i0] + vertexNormals[i1] + vertexNormals[i2];
                if (NVector.Dot(n, vSum) < 0f) n = -n;
            }
            else
            {
                var triCenter = (a + b + c) * (1f / 3f);
                if (NVector.Dot(n, triCenter - meshCenter) < 0f) n = -n;
            }

            triNormals[t] = n;
        }
    }

    private static void TransformBoundsToAabb(Bounds bounds, Matrix4x4 m, out NVector min, out NVector max)
    {
        min = new NVector(float.PositiveInfinity);
        max = new NVector(float.NegativeInfinity);
        var c = bounds.center;
        var e = bounds.extents;
        for (int i = 0; i < 8; i++)
        {
            var local = c + Vector3.Scale(
                e,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            var wp = m.MultiplyPoint3x4(local);
            var nv = new NVector(wp.x, wp.y, wp.z);
            min = NVector.Min(min, nv);
            max = NVector.Max(max, nv);
        }
    }

    private static string CleanName(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        string s = raw.Replace("(Clone)", "", StringComparison.Ordinal).Trim();
        return s.Length > 0 ? s : fallback;
    }

    private static NMatrix ToNumerics(Matrix4x4 m) => new(
        m.m00, m.m10, m.m20, m.m30,
        m.m01, m.m11, m.m21, m.m31,
        m.m02, m.m12, m.m22, m.m32,
        m.m03, m.m13, m.m23, m.m33);
}
