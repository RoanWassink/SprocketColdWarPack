using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.PlateMesh;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Colliders;
using Sprocket.Vehicles.FuelTanks;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;

namespace SprocketFreeformFuelTank;

[HarmonyPatch]
internal static class FreeformFuelTankHooks
{
    private static readonly HashSet<IntPtr> RebuildingFuelTanks = new();
    private static readonly HashSet<IntPtr> SeededStructures = new();
    private static readonly Dictionary<IntPtr, string> LastBuildLog = new();

    private static void HideDummyModelAndBoxCollider(FuelTank fuelTank)
    {
        if (fuelTank.boxCollider != null)
        {
            try { fuelTank.boxCollider.Set(Vector3.zero, Vector3.zero); } catch { }
        }

        if (fuelTank.model != null)
        {
            try
            {
                var comp = fuelTank.model.TryCast<Component>();
                if (comp != null && comp.gameObject != null && comp.gameObject.activeSelf)
                {
                    comp.gameObject.SetActive(false);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Ensures a newly created Freeform Fuel Tank's PlateStructure has the default 0.5m closed cube mesh.
    /// Never overwrites a structure that already has vertices/faces (e.g. loaded from a blueprint) or was
    /// already seeded once (so the user can still freely edit/delete faces).
    /// </summary>
    private static void EnsureDefaultCubeMesh(PlateStructure plateStructure, string caller)
    {
        if (plateStructure == null) return;
        IntPtr psPtr = plateStructure.Pointer;
        if (SeededStructures.Contains(psPtr)) return;

        var existing = plateStructure.HasMesh ? plateStructure.Mesh : null;
        if (existing != null && existing.VertexCount > 0 && existing.FaceCount > 0)
        {
            SeededStructures.Add(psPtr);
            return;
        }

        try
        {
            PlateStructureMesh? mesh = existing;
            bool newlyCreated = false;
            if (mesh == null)
            {
                var meshReg = plateStructure.Vehicle?.Meshes;
                if (meshReg == null)
                {
                    Plugin.Diag(() => $"EnsureDefaultCubeMesh ({caller}): Vehicle.Meshes is null, deferring.");
                    return;
                }
                mesh = meshReg.New<PlateStructureMesh>();
                newlyCreated = true;
            }

            mesh.FromJson(FreeformFuelTankPart.DefaultCubeMeshJson);
            mesh.Rebuild();

            if (newlyCreated)
            {
                plateStructure.Mesh = mesh;
            }
            plateStructure.RequestMeshRefresh();

            SeededStructures.Add(psPtr);
            TessellatedMesh front = mesh.frontMesh;
            Plugin.Diag(() =>
                $"Seeded default 0.5m cube mesh ({caller}, new={newlyCreated}): " +
                $"verts={mesh.VertexCount} faces={mesh.FaceCount} frontValid={front.Valid} " +
                $"frontTris={(front.triangles != null ? front.triangles.Length / 3 : 0)}");
        }
        catch (Exception ex)
        {
            Plugin.Warn($"EnsureDefaultCubeMesh ({caller})", ex);
        }
    }

    private static bool TryComputeVolume(
        FuelTank fuelTank,
        PlateStructure plateStructure,
        out FreeformMeshMath.MeshVolumeResult result)
    {
        result = default;
        var psMesh = plateStructure.Mesh;
        if (psMesh == null) return false;

        TessellatedMesh front = psMesh.frontMesh;
        if (!front.Valid)
        {
            front = psMesh.manifoldMesh;
        }
        if (!front.Valid) return false;

        var ilVerts = front.vertices;
        var ilTris = front.triangles;
        if (ilVerts == null || ilTris == null || ilVerts.Length == 0 || ilTris.Length < 3)
        {
            return false;
        }

        int vCount = ilVerts.Length;
        int tCount = ilTris.Length;
        var verts = new FreeformMeshMath.Vec3[vCount];
        for (int i = 0; i < vCount; i++)
        {
            Vector3 v = ilVerts[i];
            verts[i] = new FreeformMeshMath.Vec3(v.x, v.y, v.z);
        }

        var tris = new int[tCount];
        for (int i = 0; i < tCount; i++)
        {
            tris[i] = ilTris[i];
        }

        Vector3 scale = fuelTank.Scale;
        var scaleVec = new FreeformMeshMath.Vec3(scale.x, scale.y, scale.z);
        result = FreeformMeshMath.Compute(verts, tris, scaleVec);
        return true;
    }

    /// <summary>
    /// Ensures a newly placed Freeform Fuel Tank starts with a valid 0.5m closed cube PlateStructureMesh
    /// and disables the unused rectangular FuelTank boxCollider and dummy model so only the freeform mesh is active.
    /// RVA 0x1DB5BA0 (Len 0x260, Shared=1 in 0.2.56.3).
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(typeof(FuelTank), nameof(FuelTank.Initiate))]
    private static void FuelTankInitiatePostfix(FuelTank __instance)
    {
        if (!FreeformFuelTankPart.TryGetPair(__instance, out var fuelTank, out var plateStructure))
            return;

        try
        {
            HideDummyModelAndBoxCollider(fuelTank);
            EnsureDefaultCubeMesh(plateStructure, "FuelTank.Initiate");
        }
        catch (Exception ex)
        {
            Plugin.Warn("FuelTank.Initiate postfix", ex);
        }
    }

    /// <summary>
    /// Replaces vanilla box/barrel capacity, mass, and cost math with exact freeform enclosed volume.
    /// RVA 0x1DB6200 (Len 0x5C5, Shared=1 in 0.2.56.3).
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(FuelTank), nameof(FuelTank.Build))]
    private static bool FuelTankBuildPrefix(FuelTank __instance)
    {
        if (!FreeformFuelTankPart.TryGetPair(__instance, out var fuelTank, out var plateStructure))
            return true;

        try
        {
            HideDummyModelAndBoxCollider(fuelTank);
            EnsureDefaultCubeMesh(plateStructure, "FuelTank.Build");

            float capacityLitres = 0f;
            bool closed = false;
            int openEdges = 0;
            double volM3 = 0.0;

            if (TryComputeVolume(fuelTank, plateStructure, out var res))
            {
                volM3 = res.VolumeCubicMeters;
                closed = res.IsClosed;
                openEdges = res.OpenEdgeCount;

                bool requireClosed = Plugin.RequireClosedMesh?.Value ?? true;
                if (!requireClosed || closed)
                {
                    float eff = Math.Clamp(Plugin.FillEfficiency?.Value ?? 1.0f, 0.05f, 2.0f);
                    capacityLitres = FreeformMeshMath.ComputeCapacityLitres(volM3, eff);
                }
            }

            float mirrorMult = plateStructure.Mirrored ? 2f : 1f;
            float totalCapacity = capacityLitres * mirrorMult;
            float density = Math.Clamp(Plugin.FuelDensity?.Value ?? FreeformMeshMath.VanillaDieselDensityKgPerLitre, 0.1f, 5.0f);
            float massKg = FreeformMeshMath.ComputeFuelMassKg(totalCapacity, density);
            float cost = FreeformMeshMath.ComputeFuelCost(massKg);

            fuelTank.capacity = totalCapacity;
            fuelTank.SetMass(massKg, MassType.Fuel);
            fuelTank.SetCost(cost, MassType.Fuel, CostType.Material);

            if (fuelTank.behaviour != null)
            {
                fuelTank.behaviour.SetCapacity(totalCapacity);
            }

            string summary =
                $"Build VUID={fuelTank.VUID}: vol={volM3:F4}m3 closed={closed} openEdges={openEdges} " +
                $"mirrored={plateStructure.Mirrored} cap={totalCapacity:F1}L mass={massKg:F1}kg cost={cost:F1}";
            IntPtr ptr = fuelTank.Pointer;
            if (!LastBuildLog.TryGetValue(ptr, out var prev) || prev != summary)
            {
                LastBuildLog[ptr] = summary;
                Plugin.Diag(() => summary);
            }

            return false; // Skip vanilla box/barrel Build
        }
        catch (Exception ex)
        {
            Plugin.Warn("FuelTank.Build prefix", ex);
            return false;
        }
    }

    /// <summary>
    /// Adds the freeform mesh itself as the FuelTank damage model collider (with vanilla FuelTank
    /// DamageModelParameters: RhaFactor=0.1, Density=0.832, SpallFactor=0, Priority=PartPriority),
    /// instead of a rectangular box collider.
    /// RVA 0x1DB67D0 (Len 0x47D, Shared=1 in 0.2.56.3).
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(FuelTank), nameof(FuelTank.BuildDamageModelColliders))]
    private static bool FuelTankBuildDamageModelPrefix(
        FuelTank __instance,
        IVehicleComponentDamageModelBuilder __0)
    {
        if (!FreeformFuelTankPart.TryGetPair(__instance, out var fuelTank, out var plateStructure))
            return true;

        try
        {
            if (__0 == null || !plateStructure.HasMesh) return false;
            var psMesh = plateStructure.Mesh;
            if (psMesh == null) return false;

            TessellatedMesh front = psMesh.frontMesh;
            if (!front.Valid) front = psMesh.manifoldMesh;
            if (!front.Valid || front.vertices == null || front.triangles == null || front.triangles.Length < 3)
                return false;

            float density = Math.Clamp(Plugin.FuelDensity?.Value ?? FreeformMeshMath.VanillaDieselDensityKgPerLitre, 0.1f, 5.0f);
            var dmp = new DamageModelParameters(
                rhaFactor: 0.1f,
                spallFactor: 0.0f,
                density: density,
                priority: DamageModelParameters.PartPriority);

            Matrix4x4 localToWorld = fuelTank.transform.localToWorldMatrix;

            // Ensure triangles have outward-facing winding in world space (even if the user
            // dragged faces across each other or applied a negative scale).
            bool meshInsideOut = TryComputeVolume(fuelTank, plateStructure, out var volRes) && volRes.IsInsideOut;
            bool worldFlip = localToWorld.determinant < 0f;
            bool flipBase = meshInsideOut ^ worldFlip;

            var baseTris = flipBase ? FlipTriangles(front.triangles) : front.triangles;
            __0.AddMesh(
                fuelTank.VUID,
                ref localToWorld,
                fuelTank.EntityID,
                front.vertices,
                baseTris,
                ref dmp);

            if (plateStructure.Mirrored)
            {
                // PlateStructure.GetMirrorMatrix is in LOCAL space (same as PlateStructure.BuildDamageModelColliders).
                // Transform vertices in local space and flip winding when det(mirrorMat) < 0.
                plateStructure.GetMirrorMatrix(out Matrix4x4 mirrorMat);
                int vCount = front.vertices.Length;
                var mirroredVerts = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(vCount);
                for (int i = 0; i < vCount; i++)
                {
                    mirroredVerts[i] = mirrorMat.MultiplyPoint3x4(front.vertices[i]);
                }

                bool mirrorFlip = mirrorMat.determinant < 0f;
                var mirroredTris = (flipBase ^ mirrorFlip) ? FlipTriangles(front.triangles) : front.triangles;

                __0.AddMesh(
                    fuelTank.VUID,
                    ref localToWorld,
                    fuelTank.EntityID,
                    mirroredVerts,
                    mirroredTris,
                    ref dmp);
            }

            Plugin.Diag(() =>
            {
                ComputeWorldBounds(front.vertices, localToWorld, out Vector3 bMin, out Vector3 bMax);
                return $"BuildDamageModelColliders VUID={fuelTank.VUID}: verts={front.vertices.Length} " +
                       $"tris={front.triangles.Length / 3} mirrored={plateStructure.Mirrored} " +
                       $"insideOut={meshInsideOut} flipBase={flipBase} " +
                       $"worldBounds=[({bMin.x:F2},{bMin.y:F2},{bMin.z:F2})..({bMax.x:F2},{bMax.y:F2},{bMax.z:F2})]";
            });
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Warn("FuelTank.BuildDamageModelColliders prefix", ex);
            return false;
        }
    }

    /// <summary>
    /// Computes Center of Mass from the exact freeform volume-weighted centroid.
    /// RVA 0x1DB7010 (Len 0x143, Shared=1 in 0.2.56.3).
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(FuelTank), nameof(FuelTank.CalculateCenterOfMass_LocalSpace))]
    private static bool FuelTankComPrefix(FuelTank __instance, ref Vector3 __result)
    {
        if (!FreeformFuelTankPart.TryGetPair(__instance, out var fuelTank, out var plateStructure))
            return true;

        try
        {
            if (TryComputeVolume(fuelTank, plateStructure, out var res))
            {
                __result = new Vector3((float)res.Centroid.X, (float)res.Centroid.Y, (float)res.Centroid.Z);
            }
            else
            {
                __result = Vector3.zero;
            }
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Warn("FuelTank.CalculateCenterOfMass_LocalSpace prefix", ex);
            __result = Vector3.zero;
            return false;
        }
    }

    /// <summary>
    /// Seeds the default cube mesh before PlateStructure.Build runs SyncMesh so the mesh renders immediately
    /// during placement preview.
    /// RVA 0x1E7D0B0 (Len 0x4C7, Shared=1 in 0.2.56.3).
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(PlateStructure), nameof(PlateStructure.Build))]
    private static void PlateStructureBuildPrefix(PlateStructure __instance)
    {
        if (!FreeformFuelTankPart.TryGetPair(__instance, out _, out var plateStructure))
            return;

        EnsureDefaultCubeMesh(plateStructure, "PlateStructure.Build");
    }

    /// <summary>
    /// Zeroes out steel-armour mass and cost on the companion PlateStructure and triggers
    /// a live FuelTank rebuild whenever the freeform mesh changes.
    /// RVA 0x1E7D0B0 (Len 0x4C7, Shared=1 in 0.2.56.3).
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(typeof(PlateStructure), nameof(PlateStructure.Build))]
    private static void PlateStructureBuildPostfix(PlateStructure __instance)
    {
        if (!FreeformFuelTankPart.TryGetPair(__instance, out var fuelTank, out var plateStructure))
            return;

        try
        {
            plateStructure.armourVolume = 0f;
            plateStructure.SetMass(0f, MassType.Armour);
            plateStructure.SetCost(0f, MassType.Armour, CostType.Material);
            plateStructure.SetCost(0f, MassType.Armour, CostType.Assembly);

            IntPtr ptr = fuelTank.Pointer;
            if (RebuildingFuelTanks.Add(ptr))
            {
                try
                {
                    fuelTank.RequestRebuild();
                }
                finally
                {
                    RebuildingFuelTanks.Remove(ptr);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Warn("PlateStructure.Build postfix", ex);
        }
    }

    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> FlipTriangles(
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> src)
    {
        int len = src.Length;
        var dst = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(len);
        int triEnd = (len / 3) * 3;
        for (int i = 0; i < triEnd; i += 3)
        {
            dst[i] = src[i];
            dst[i + 1] = src[i + 2];
            dst[i + 2] = src[i + 1];
        }
        return dst;
    }

    private static void ComputeWorldBounds(
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> verts,
        Matrix4x4 localToWorld,
        out Vector3 min,
        out Vector3 max)
    {
        min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 w = localToWorld.MultiplyPoint3x4(verts[i]);
            if (w.x < min.x) min.x = w.x;
            if (w.y < min.y) min.y = w.y;
            if (w.z < min.z) min.z = w.z;
            if (w.x > max.x) max.x = w.x;
            if (w.y > max.y) max.y = w.y;
            if (w.z > max.z) max.z = w.z;
        }
    }

    /// <summary>
    /// Skips steel armour damage colliders on the companion PlateStructure so the freeform fuel tank
    /// does not act as extra armour plating (its damage collider is registered by FuelTank instead).
    /// RVA 0x1E7EB10 (Len 0x48D, Shared=1 in 0.2.56.3).
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(PlateStructure), nameof(PlateStructure.BuildDamageModelColliders))]
    private static bool PlateStructureBuildDamageModelPrefix(PlateStructure __instance)
    {
        if (!FreeformFuelTankPart.TryGetPair(__instance, out _, out _))
            return true;

        return false;
    }
}
