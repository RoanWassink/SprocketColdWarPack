using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using Sprocket.Vehicles.AttachedBehaviours;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace SprocketThermalSight;

internal static class ThermalModelAssets
{
    private static readonly Dictionary<bool,Mesh> meshes=new();
    private static VehicleMaterial? dark, lens;
    private static float scanAt;
    private static bool failed;
    private static int appliedLogs;
    private static readonly HashSet<IntPtr> reportedModels=new();
    private static bool Own(VehicleObjectModel model) => model.VehicleObject?.GetBehaviour<GunnerSight>() is {} sight && Runtime.IsThermal(sight);
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Build))]
    private static void Building(VehicleObjectModel __instance)
    {
        if(!Own(__instance))return;
        __instance.staticRenderer=false;
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.OnModelLoaded))]
    private static void Loaded(VehicleObjectModel __instance)
    {
        try{if(Own(__instance))Apply(__instance);}catch(Exception ex){Runtime.Warn("Thermal loaded model: "+ex.Message);}
    }
    private static void Apply(VehicleObjectModel model)
    {
        // Model is the actual IVehicleModel returned by OnModelLoaded. The inherited
        // component models list can be empty when this visual belongs to a sight.
        var native=model.Model;if(native==null)return;
        var mesh=EnsureAssets(model.VehicleObject.GetBehaviour<GunnerSight>()?.ComponentID==Runtime.VerticalId);model.MarkDynamic();
        bool changed=native.InteractionMesh!=mesh;
        var group=native.TryCast<VehicleRendererGroup>();
        if(group?.lods!=null)
            foreach(var lod in group.lods)
            {
                var filter=lod?.Renderer?.GetComponent<MeshFilter>();
                if(filter!=null && filter.sharedMesh!=mesh){changed=true;break;}
            }
        if(changed)
        {
            native.SetMesh(mesh!);native.SetShadowProxy(mesh!);
            native.SetInteractionMesh(mesh!);native.SetCollisionMesh(mesh!);
        }
        var materials=native.Materials;
        var paint=ExteriorPaint.Get(model,materials!=null && materials.Length>0 ? materials[0] : native.Material);
        if(paint!=null && (materials==null || materials.Length!=3 || materials[0]?.Pointer!=paint.Pointer || materials[1]?.Pointer!=dark!.Pointer || materials[2]?.Pointer!=lens!.Pointer))
        {
            native.SetMaterials(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VehicleMaterial>(new[]{paint,dark!,lens!}));
            ExteriorPaint.Process(model,paint);
        }
        if(appliedLogs<6 && reportedModels.Add(native.Pointer))
        {
            appliedLogs++;
            Plugin.Instance.Log.LogInfo($"[Thermal model] Applied to active Model: object={model.VehicleObject.EntityID}; LODs={native.LODCount}; baked={native.FullyBaked}; interactionMatch={native.InteractionMesh==mesh}; collisionMatch={native.CollisionMesh==mesh}");
        }
    }
    private static Mesh EnsureAssets(bool vertical)
    {
        if(meshes.TryGetValue(vertical,out var cached))return cached;
        string path=Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,"assets",vertical ? "thermal-head-vertical.obj" : "thermal-head.obj");
        var data=PaintedObj.Read(path);
        var shader=Shader.Find("HDRP/Lit");if(shader==null)throw new InvalidOperationException("Thermal model shader unavailable");
        VehicleMaterial Surface(string name,Color color,float smoothness)
        {
            var m=new Material(shader){name=name,hideFlags=HideFlags.HideAndDontSave};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",smoothness);HDMaterial.ValidateMaterial(m);
            return new VehicleMaterial(m){Flags=VehicleMaterialFlags.Unpainted|VehicleMaterialFlags.DisableGrime|VehicleMaterialFlags.CustomShader};
        }
        dark ??=Surface("Thermal aperture rim",new Color(.18f,.23f,.17f,1),.35f);
        lens ??=Surface("Thermal IR aperture",new Color(.04f,.12f,.14f,1),.85f);
        var mesh=new Mesh{name=vertical ? "Wall-mounted thermal head" : "Stepped thermal periscope head",hideFlags=HideFlags.HideAndDontSave};mesh.vertices=data.Vertices.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
        mesh.uv=data.Uv.Select(v=>new Vector2(v.X,v.Y)).ToArray();mesh.normals=data.Normals.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();mesh.subMeshCount=3;
        for(int i=0;i<3;i++)mesh.SetTriangles(data.Triangles[i],i);mesh.RecalculateBounds();
        meshes.Add(vertical,mesh);
        Plugin.Instance.Log.LogInfo($"[Thermal model] {(vertical ? "Wall-mounted" : "Periscope")} head ready; optical axis retained at (0,0,0.0409).");
        return mesh;
    }
    internal static void Cleanup()
    {
        foreach (var owned in meshes.Values) if (owned != null) UnityEngine.Object.Destroy(owned);
        meshes.Clear(); reportedModels.Clear();
        if (dark?.Material != null) UnityEngine.Object.Destroy(dark.Material); dark = null;
        if (lens?.Material != null) UnityEngine.Object.Destroy(lens.Material); lens = null;
        failed = false; scanAt = 0; appliedLogs = 0;
    }
    internal static void Tick()
    {
        if(!Runtime.HasVehicleContext || failed || Time.unscaledTime<scanAt)return;scanAt=Time.unscaledTime+.5f;
        try
        {
            foreach(var vehicle in UnityEngine.Object.FindObjectsOfType<Vehicle>())
            {
                if(vehicle==null || vehicle.Released)continue;
                var items=vehicle.ObjectReader.Items;int n=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
                for(int i=0;i<n;i++)
                {
                    var obj=items[i];if(obj==null || obj.Released)continue;
                    var sight=obj.GetBehaviour<GunnerSight>();if(sight==null || !Runtime.IsThermal(sight))continue;
                    var model=obj.GetBehaviour<VehicleObjectModel>();if(model==null)continue;
                    Apply(model);
                }
            }
        }
        catch(Exception ex){failed=true;Runtime.Warn("Optional thermal model unavailable: "+ex.Message);}
    }
}
