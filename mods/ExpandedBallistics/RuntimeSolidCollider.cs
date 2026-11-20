using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Colliders;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;

namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.97: builds a native solid 3D damage collider for closed [Solid block] composite plates
/// inside PlateStructure.BuildDamageModelColliders (ICF=1, RVA 0x1E7EB10) via the 6-arg
/// IVehicleComponentDamageModelBuilder.AddMesh overload. Falls back cleanly (returns true)
/// whenever disabled, not marked [Solid block], unreadable, or non-closed.
/// </summary>
[HarmonyPatch]
internal static class RuntimeSolidCollider
{
    private static ConfigEntry<bool>? enabled;
    private static readonly Dictionary<IntPtr,IntPtr> active=new();
    private static readonly Dictionary<IntPtr,(string Signature,SolidColliderMesh Mesh)> meshCache=new();
    private static readonly HashSet<IntPtr> deferredRebuild=new();
    private static int buildLogs,fallbackLogs;

    internal static bool Enabled=>enabled?.Value??true;

    internal static void Configure(ConfigFile config)
    {
        var entry=config.Bind("Composite fill","Solid block native collider",true,
            "Build a native solid 3D damage collider for closed [Solid block] plates instead of the thin-shell cavity fallback. Turn off to revert to the 0.22.82 thin-shell behaviour.");
        enabled=entry;
        void Sync(){SolidBlockApply.NativeCollider=entry.Value;}
        entry.SettingChanged+=(_,_)=>Sync();
        Sync();
    }

    internal static bool IsNativeSolid(PlateStructure? plate)
    {
        if(!Enabled||plate==null)return false;
        try{return plate.HasMesh&&active.TryGetValue(plate.Pointer,out var builtMesh)&&builtMesh==plate.Mesh.Pointer;}
        catch(Exception){return false;}
    }

    internal static void Invalidate(IntPtr mesh)
    {
        meshCache.Remove(mesh);
        foreach(var key in active.Where(pair=>pair.Value==mesh).Select(pair=>pair.Key).ToArray())
            active.Remove(key);
    }

    internal static void Clear()
    {
        active.Clear();
        meshCache.Clear();
        deferredRebuild.Clear();
        buildLogs=0;
        fallbackLogs=0;
    }

    [HarmonyPrefix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.BuildDamageModelColliders))]
    private static bool Build(PlateStructure __instance,IVehicleComponentDamageModelBuilder __0)
    {
        if(__instance==null||__0==null)return true;
        var platePtr=__instance.Pointer;
        active.Remove(platePtr);
        try
        {
            if(!Enabled||!__instance.HasMesh||__instance.VehicleObject==null)return true;
            if(!CompositePlate.IsCassette(__instance.armourTechID))return true;

            var mesh=__instance.Mesh;
            var meshPtr=mesh.Pointer;
            bool designer=RuntimeComposerFill.Designer;

            SolidMarks.Answer(platePtr.ToInt64(),out var markEntry,out _);
            if(markEntry.Read&&markEntry.Mesh==meshPtr.ToInt64())
            {
                if(!markEntry.Marked)return true;
            }
            else
            {
                if(designer&&PlateMeshAccess.Gate.Busy)
                {
                    ScheduleDesignerRebuild(__instance);
                    return true;
                }
                var name=mesh.ToMeshBlueprint(false)?.Name;
                if(!CompositePlate.IsSolidBlock(name))return true;
            }

            var snapshot=designer
                ?PlateMeshAccess.Read(__instance,"solid collider",SnapshotUse.Fresh,out var readReason)
                :PlateMeshAccess.ReadUngated(mesh,out readReason);
            if(snapshot==null)
            {
                if(designer)ScheduleDesignerRebuild(__instance);
                else if(fallbackLogs++<16)
                    Plugin.ModLog.LogWarning($"[Solid block] native collider fallback on plate #{__instance.VUID.Value}: mesh unreadable ({readReason}); thin-shell collider retained.");
                return true;
            }

            SolidColliderMesh colliderMesh;
            if(meshCache.TryGetValue(meshPtr,out var cached)&&cached.Signature==snapshot.Signature)
            {
                colliderMesh=cached.Mesh;
            }
            else
            {
                var built=SolidColliderGeometry.FromSnapshot(snapshot);
                if(built.Mesh==null)
                {
                    if(fallbackLogs++<16)
                        Plugin.ModLog.LogWarning($"[Solid block] native collider fallback on plate #{__instance.VUID.Value}: {built.Refusal} Thin-shell collider retained.");
                    return true;
                }
                colliderMesh=built.Mesh;
                if(meshCache.Count>=512)meshCache.Clear();
                meshCache[meshPtr]=(snapshot.Signature,colliderMesh);
            }

            var finalMesh=colliderMesh;
            bool mirrored=__instance.Mirrored;
            if(mirrored)
            {
                __instance.GetMirrorMatrix(out var um);
                var affine=new SolidAffine(um.m00,um.m01,um.m02,um.m03,um.m10,um.m11,um.m12,um.m13,um.m20,um.m21,um.m22,um.m23);
                finalMesh=SolidColliderGeometry.AppendMirrored(colliderMesh,affine);
            }

            var uVerts=new Il2CppStructArray<Vector3>(finalMesh.Vertices.Length);
            for(int i=0;i<finalMesh.Vertices.Length;i++)
            {
                var p=finalMesh.Vertices[i];
                uVerts[i]=new Vector3(p.X,p.Y,p.Z);
            }
            var uTris=new Il2CppStructArray<int>(finalMesh.Triangles.Length);
            for(int i=0;i<finalMesh.Triangles.Length;i++)uTris[i]=finalMesh.Triangles[i];

            var matrix=__instance.transform.localToWorldMatrix;
            var parameters=__instance.damageModelParameters;
            __0.AddMesh(__instance.VUID,ref matrix,__instance.VehicleObject.EntityID,uVerts,uTris,ref parameters);

            if(active.Count>=4096)active.Clear();
            active[platePtr]=meshPtr;
            if(buildLogs++<24)
            {
                Plugin.ModLog.LogInfo(string.Create(CultureInfo.InvariantCulture,
                    $"[Solid block] native collider built: plate #{__instance.VUID.Value} ({finalMesh.Pieces} piece(s), {finalMesh.Vertices.Length} verts, {finalMesh.Triangles.Length/3} tris, {finalMesh.VolumeM3:0.####} m3, mirrored={mirrored}, {(designer?"designer":"play")})."));
            }
            return false;
        }
        catch(Exception ex)
        {
            active.Remove(platePtr);
            if(fallbackLogs++<16)
                Plugin.ModLog.LogWarning($"[Solid block] native collider failed on plate #{__instance.VUID.Value} ({ex.GetType().Name}: {ex.Message}); thin-shell collider retained.");
            return true;
        }
    }

    private static void ScheduleDesignerRebuild(PlateStructure plate)
    {
        var ptr=plate.Pointer;
        if(!deferredRebuild.Add(ptr))return;
        PlateMeshDeferred.WhenReady("solid collider rebuild",
            ()=>plate==null||plate.Pointer!=ptr||!PlateMeshAccess.Gate.Busy,
            ()=>
            {
                deferredRebuild.Remove(ptr);
                try
                {
                    if(plate!=null&&plate.Pointer==ptr&&plate.Vehicle!=null)
                        plate.Vehicle.RaiseDirtyFlags(VehicleDirtyFlags.DamageModel);
                }
                catch(Exception){}
            },10f);
    }
}
