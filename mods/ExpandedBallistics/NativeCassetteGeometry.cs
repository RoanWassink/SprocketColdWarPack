using HarmonyLib;
using Sprocket.Vehicles.PlateStructures;
using NumericVector3=System.Numerics.Vector3;
using UnityVector3=UnityEngine.Vector3;
namespace SprocketExpandedBallistics;

// The authored plate faces are read through the snapshot helper and cached in plate local space, per native mesh.
// 0.22.24: the cache is keyed on a content signature (face and loop counts plus a checksum of the vertex positions and loop thicknesses),
// not only on the OnAssignedMeshModified event: the edge tool, a vertex move, a thickness edit or an extrude can change a plate without that
// event reaching us, and a stale cache fed the hit model old face planes. The event still drops the entry at once.
[HarmonyPatch]
internal static class NativeCassetteGeometry
{
    private sealed record Cached(string Signature,NumericVector3[] Normals);
    private static readonly Dictionary<IntPtr,Cached> cache=new();
    private static readonly HashSet<IntPtr> failed=new();
    private static int warnings;

    /// <summary>The current content of the plate's mesh (signature-checked); null when it cannot be read.</summary>
    private static Cached? Get(PlateStructure plate)
    {
        try
        {
            var mesh=plate.Mesh;
            if(mesh==null)return null;
            var pointer=mesh.Pointer;
            // Combat (Play) reads a mesh nobody is editing: no quiet period, and the dirty flags of a built mesh do not matter.
            var snapshot=PlateMeshAccess.ReadUngated(mesh,out var reason);
            if(snapshot==null)
            {
                cache.Remove(pointer);
                if(failed.Add(pointer)&&warnings++<4)Plugin.ModLog.LogWarning("[Cassettes] Plate faces unavailable: "+reason);
                return null;
            }
            failed.Remove(pointer);
            cache.TryGetValue(pointer,out var cached);
            if(cached!=null&&cached.Signature==snapshot.Signature)return cached;
            if(cached!=null&&ReleaseDiagnostics.Enabled)
                Plugin.ModLog.LogInfo($"[Composite plate cache] invalidated by signature ({MeshSignature.Change(cached.Signature,snapshot.Signature)}) for the hit geometry of mesh 0x{pointer.ToInt64():x}.");
            var fresh=new Cached(snapshot.Signature,snapshot.Faces.Select(f=>Finite(f.Normal)?f.Normal:NumericVector3.Zero).ToArray());
            cache[pointer]=fresh;
            return fresh;
        }
        catch(Exception error)
        {
            if(warnings++<4)Plugin.ModLog.LogWarning("[Cassettes] Plate faces unavailable: "+error.Message);
            return null;
        }
    }
    private static bool Finite(NumericVector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z)&&v.LengthSquared()>1e-10f;

    internal static bool TryLocalNormals(PlateStructure plate,out NumericVector3[] normals)
    {
        var data=Get(plate);
        normals=data?.Normals??Array.Empty<NumericVector3>();
        return data!=null&&normals.Length>0;
    }
    internal static bool TryWorldFront(PlateStructure plate,NumericVector3 local,out NumericVector3 world)
    {
        world=default;
        try
        {
            var source=new UnityVector3(local.X,local.Y,local.Z);
            var result=plate.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(source);
            if(!float.IsFinite(result.x)||!float.IsFinite(result.y)||!float.IsFinite(result.z)||result.sqrMagnitude<.25f)return false;
            result.Normalize();world=new(result.x,result.y,result.z);return true;
        }
        catch(Exception){return false;}
    }
    // 0.22.12: compare the shot direction with the authored faces in world
    // space. The old local-space comparison transformed the direction with the
    // normal matrix, which is only valid for an identity plate transform; a
    // rotated or scaled plate could therefore flip a frontal hit to "back".
    // 0.22.28: only a composite plate that native skipped is read this way (its drawn face gives the angle); a face without a usable normal
    // keeps its index with a zero normal.
    internal static bool TryWorldSide(PlateStructure plate,UnityVector3 worldDirection,
        out CassetteFaceHit hit,out NumericVector3 frontWorld,out double alignment)
    {
        hit=default;frontWorld=default;alignment=0;
        if(!TryLocalNormals(plate,out var local))return false;
        var direction=new NumericVector3(worldDirection.x,worldDirection.y,worldDirection.z);
        var length=direction.Length();
        if(!float.IsFinite(length)||length<1e-6f)return false;
        direction/=length;
        var worldNormals=new List<NumericVector3>(local.Length);var usable=0;
        for(int i=0;i<local.Length;i++)
            if(TryWorldFront(plate,local[i],out var world)){worldNormals.Add(world);usable++;}else worldNormals.Add(NumericVector3.Zero);
        if(usable==0)return false;
        hit=CassetteImpactPath.ResolveSide(direction,worldNormals);
        if(hit.FaceIndex<0)return false;
        alignment=hit.Alignment;
        frontWorld=hit.FrontNormal;
        return true;
    }
    internal static void Invalidate(PlateStructure? plate)
    {
        try{if(plate!=null&&plate.HasMesh)Invalidate(plate.Mesh.Pointer);}catch(Exception){}
    }
    internal static void Invalidate(IntPtr mesh)
    {
        cache.Remove(mesh);failed.Remove(mesh);
    }
    internal static void Clear(){cache.Clear();failed.Clear();warnings=0;}
    // ICF: shared with OnAssignedMeshModified; no argument registers may be read.
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.RequestMeshRefresh))]
    private static void Modified(PlateStructure __instance){if(__instance==null)return;Invalidate(__instance);}
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureMesh),nameof(PlateStructureMesh.FromJson))]
    private static void Loaded(PlateStructureMesh __instance)=>Invalidate(__instance.Pointer);
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureMesh),nameof(PlateStructureMesh.LoadBlueprint),new[]{typeof(Sprocket.Vehicles.MeshGeneration.PlateStructureMeshBlueprint)})]
    private static void ShapeLoaded(PlateStructureMesh __instance)=>Invalidate(__instance.Pointer);
}
