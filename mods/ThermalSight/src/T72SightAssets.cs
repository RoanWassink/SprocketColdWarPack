using System.Globalization;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace SprocketThermalSight;

internal static class T72SightAssets
{
    private sealed class Sight
    {
        internal readonly string Id;
        internal Mesh? mesh;
        internal VehicleMaterial? glass, amberGlass, housing, steel, dark;
        internal bool failed;
        internal int appliedLogs;
        internal Sight(string id) { Id=id; }
    }
    private static readonly Dictionary<string,Sight> sights=new(StringComparer.Ordinal)
    { ["t72StyleSight"]=new("t72StyleSight"), [BoxSightObj.Id]=new(BoxSightObj.Id) };
    private static Sight? Find(VehicleComponent c)
    {
        var id=c.TryCast<VehicleObjectModel>()?.VehicleObject?.GetBehaviour<Sprocket.Vehicles.AttachedBehaviours.GunnerSight>()?.ComponentID;
        return id!=null && sights.TryGetValue(id,out var sight) ? sight : null;
    }
    private static bool Own(VehicleComponent c)=>Find(c)!=null;
    private static float scanAt;
    private static string Asset(string file)=>Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,"assets",file);
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Initiate))]
    private static void Initiating(VehicleObjectModel __instance)
    {
        if(!Own(__instance))return;
        // Keep the native exterior sight interaction category.

        __instance.staticRenderer=false;
        
    }
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Build))]
    private static void Building(VehicleObjectModel __instance)
    {if(Own(__instance))__instance.staticRenderer=false;}
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.OnModelLoaded))]
    private static void Loaded(VehicleObjectModel __instance)
    {try{if(Own(__instance))Apply(__instance);}catch(Exception ex){Runtime.Warn("T72 sight model load: "+ex.Message);}}
    private static void EnsureAssets(Sight state)
    {
        if(state.mesh!=null)return;
        if(state.Id==BoxSightObj.Id){EnsureBoxAssets(state);return;}
        var positions=new List<Vector3>();var texcoords=new List<Vector2>();var sourceNormals=new List<Vector3>();
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var normals=new List<Vector3>();
        var faces=new[]{new List<int>(),new List<int>(),new List<int>()};int material=0;
        float F(string text)=>float.Parse(text,CultureInfo.InvariantCulture);
        foreach(var line in File.ReadLines(Asset("t72-style-sight.obj")))
        {
            var f=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(f.Length==0)continue;
            if(f[0]=="v" && f.Length==4)positions.Add(new Vector3(F(f[1]),F(f[2]),F(f[3])));
            else if(f[0]=="vt" && f.Length>=3)texcoords.Add(new Vector2(F(f[1]),F(f[2])));
            else if(f[0]=="vn" && f.Length==4)sourceNormals.Add(new Vector3(F(f[1]),F(f[2]),F(f[3])));
            else if(f[0]=="usemtl" && f.Length==2)material=f[1] switch{"Housing"=>0,"Glass"=>1,"AmberGlass"=>2,_=>throw new InvalidDataException("Unknown FCS surface")};
            else if(f[0]=="f")
            {
                if(f.Length!=4)throw new InvalidDataException("FCS must be triangulated");
                for(int i=1;i<4;i++)
                {
                    var p=f[i].Split('/');if(p.Length!=3)throw new InvalidDataException("FCS UV/normal tuple missing");
                    int a=int.Parse(p[0],CultureInfo.InvariantCulture)-1,b=int.Parse(p[1],CultureInfo.InvariantCulture)-1,c=int.Parse(p[2],CultureInfo.InvariantCulture)-1;
                    if(a<0 || a>=positions.Count || b<0 || b>=texcoords.Count || c<0 || c>=sourceNormals.Count)throw new InvalidDataException("FCS tuple out of bounds");
                    faces[material].Add(vertices.Count);vertices.Add(positions[a]);uv.Add(texcoords[b]);normals.Add(sourceNormals[c]);
                }
            }
        }
        if(positions.Count!=1731 || vertices.Count!=1056 || faces.Any(f=>f.Count==0) || vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))throw new InvalidDataException("Unexpected FCS model geometry");
        state.mesh=new Mesh{name="TPD-K1 gunner sight",hideFlags=HideFlags.HideAndDontSave};state.mesh.vertices=vertices.ToArray();state.mesh.uv=uv.ToArray();state.mesh.normals=normals.ToArray();state.mesh.subMeshCount=3;
        for(int i=0;i<3;i++)state.mesh.SetTriangles(faces[i].ToArray(),i);state.mesh.RecalculateBounds();
        var shader=Shader.Find("HDRP/Lit");if(shader==null)throw new InvalidOperationException("FCS shader unavailable");
        var surface=new Material(shader){name="FCS eyepiece glass",hideFlags=HideFlags.HideAndDontSave};
        surface.SetColor("_BaseColor",new Color(.035f,.14f,.15f,1));surface.SetFloat("_Smoothness",.92f);
        // Render the optical surface from either side.
        surface.SetFloat("_DoubleSidedEnable",1);surface.SetFloat("_CullMode",0);surface.SetFloat("_CullModeForward",0);HDMaterial.ValidateMaterial(surface);
        state.glass=new VehicleMaterial(surface){Flags=VehicleMaterialFlags.Unpainted|VehicleMaterialFlags.DisableGrime|VehicleMaterialFlags.CustomShader};
        var amber=new Material(surface){name="TPD-K1 amber optical glass",hideFlags=HideFlags.HideAndDontSave};
        amber.SetColor("_BaseColor",new Color(.32f,.20f,.035f,1));HDMaterial.ValidateMaterial(amber);
        state.amberGlass=new VehicleMaterial(amber){Flags=VehicleMaterialFlags.Unpainted|VehicleMaterialFlags.DisableGrime|VehicleMaterialFlags.CustomShader};
        Plugin.Instance.Log.LogInfo("[T72 sight] User model loaded: 352 triangles; original dimensions retained; mounting frame aligned to +Z.");
    }
    private static void Apply(VehicleObjectModel model)
    {
        var state=Find(model);if(state==null || state.failed)return;
        var native=model.Model;if(native==null)return;EnsureAssets(state);model.MarkDynamic();
        if(state.Id==BoxSightObj.Id){ApplyBox(model,state);return;}
        if(native.InteractionMesh!=state.mesh)
        {native.SetMesh(state.mesh!);native.SetShadowProxy(state.mesh!);native.SetInteractionMesh(state.mesh!);native.SetCollisionMesh(state.mesh!);}
        var materials=native.Materials;var paint=materials!=null && materials.Length>0 ? materials[0] : native.Material;
        if(state.housing==null)
        {
            // Fixed housing is used when no vehicle paint controller exists.
            var shader=Shader.Find("HDRP/Lit");if(shader==null)throw new InvalidOperationException("T72 housing shader unavailable");
            var surface=new Material(shader){name="T72 sight housing",hideFlags=HideFlags.HideAndDontSave};
            surface.SetColor("_BaseColor",new Color(.24f,.27f,.18f,1));
            surface.SetFloat("_Smoothness",.25f);surface.SetFloat("_Metallic",0);
            HDMaterial.SetSurfaceType(surface,false);
            surface.SetFloat("_DoubleSidedEnable",1);surface.SetFloat("_DoubleSidedNormalMode",1);
            surface.SetFloat("_CullMode",0);surface.SetFloat("_CullModeForward",0);HDMaterial.ValidateMaterial(surface);
            state.housing=new VehicleMaterial(surface){Flags=VehicleMaterialFlags.Unpainted|VehicleMaterialFlags.DisableGrime|VehicleMaterialFlags.CustomShader};
            Plugin.Instance.Log.LogInfo("[T72 sight] Clean opaque housing; original front optical pane uses glass; shader="+surface.shader.name);
        }
        paint=ExteriorPaint.Get(model,state.housing);
        if(paint!=null && (materials==null || materials.Length!=3 || materials[0]?.Pointer!=paint.Pointer || materials[1]?.Pointer!=state.glass!.Pointer || materials[2]?.Pointer!=state.amberGlass!.Pointer))
        {
            native.SetMaterials(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VehicleMaterial>(new[]{paint,state.glass!,state.amberGlass!}));
            ExteriorPaint.Process(model,paint);
        }
        if(state.appliedLogs<4){state.appliedLogs++;Plugin.Instance.Log.LogInfo($"[T72 sight] Native model applied; selection={model.PartType}; layer={model.InteractionCollider?.Layer}; meshMatch={native.InteractionMesh==state.mesh}");}
    }
    private static VehicleMaterial Surface(string name,Color colour,float smoothness,bool doubleSided=false)
    {
        var shader=Shader.Find("HDRP/Lit");if(shader==null)throw new InvalidOperationException("Box sight shader unavailable");
        var surface=new Material(shader){name=name,hideFlags=HideFlags.HideAndDontSave};
        surface.SetColor("_BaseColor",colour);surface.SetFloat("_Smoothness",smoothness);surface.SetFloat("_Metallic",0);
        HDMaterial.SetSurfaceType(surface,false);
        if(doubleSided)
        {surface.SetFloat("_DoubleSidedEnable",1);surface.SetFloat("_CullMode",0);surface.SetFloat("_CullModeForward",0);}
        HDMaterial.ValidateMaterial(surface);
        return new VehicleMaterial(surface){Flags=VehicleMaterialFlags.Unpainted|VehicleMaterialFlags.DisableGrime|VehicleMaterialFlags.CustomShader};
    }
    private static void EnsureBoxAssets(Sight state)
    {
        var data=BoxSightObj.Read(Asset("box-sight.obj"));
        state.steel=Surface("Laser box sight steel",new Color(.18f,.18f,.19f,1),.5f);
        state.dark=Surface("Laser box sight dark",new Color(.05f,.05f,.05f,1),.3f);
        state.glass=Surface("Laser box sight optical glass",new Color(.06f,.10f,.12f,1),.9f,true);
        state.mesh=new Mesh{name="Laser box sight",hideFlags=HideFlags.HideAndDontSave};
        state.mesh.vertices=data.Vertices.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
        state.mesh.uv=data.Uv.Select(v=>new Vector2(v.X,v.Y)).ToArray();
        state.mesh.normals=data.Normals.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
        state.mesh.subMeshCount=4;
        for(int i=0;i<4;i++)state.mesh.SetTriangles(data.Triangles[i],i);
        state.mesh.RecalculateBounds();
        Plugin.Instance.Log.LogInfo("[Box sight] User model loaded: 5034 triangles (2700 paint, 914 steel, 748 dark, 672 lens); +Z forward; vn/vt retained.");
    }
    private static void ApplyBox(VehicleObjectModel model,Sight state)
    {
        var native=model.Model!;
        if(native.InteractionMesh!=state.mesh)
        {native.SetMesh(state.mesh!);native.SetShadowProxy(state.mesh!);native.SetInteractionMesh(state.mesh!);native.SetCollisionMesh(state.mesh!);}
        var current=native.Materials;
        var paint=ExteriorPaint.Get(model,current!=null && current.Length>0 ? current[0] : native.Material);if(paint==null)return;
        var materials=native.Materials;
        if(materials==null || materials.Length!=4 || materials[0]?.Pointer!=paint.Pointer
            || materials[1]?.Pointer!=state.steel!.Pointer || materials[2]?.Pointer!=state.dark!.Pointer || materials[3]?.Pointer!=state.glass!.Pointer)
        {
            native.SetMaterials(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VehicleMaterial>(new[]{paint,state.steel!,state.dark!,state.glass!}));
            ExteriorPaint.Process(model,paint);
            if(state.appliedLogs++<4)Plugin.Instance.Log.LogInfo($"[Box sight] Native model applied; exterior paint linked; flags={paint.Flags}; slot={paint.PaintSlot}; meshMatch={native.InteractionMesh==state.mesh}");
        }
    }
    internal static void Cleanup()
    {
        foreach(var state in sights.Values)
        {
            if(state.mesh!=null)UnityEngine.Object.Destroy(state.mesh);state.mesh=null;
            foreach(var material in new[]{state.glass,state.amberGlass,state.housing,state.steel,state.dark})
                if(material?.Material!=null)UnityEngine.Object.Destroy(material.Material);
            state.glass=state.amberGlass=state.housing=state.steel=state.dark=null;
            state.failed=false;state.appliedLogs=0;
        }
        // Native paint ownership is shared through ExteriorPaint and released by its unconditional Cleanup.
        scanAt=0;
    }
    internal static void Tick()
    {
        if(!Runtime.HasVehicleContext || Time.unscaledTime<scanAt)return;scanAt=Time.unscaledTime+.5f;
        try
        {
            foreach(var vehicle in UnityEngine.Object.FindObjectsOfType<Vehicle>())
            {
                if(vehicle==null || vehicle.Released)continue;
                var list=vehicle.ObjectReader.Items;int n=list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
                for(int i=0;i<n;i++)
                {
                    var components=list[i]?.Components;if(components==null)continue;
                    int m=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                    for(int j=0;j<m;j++)
                    {
                        var model=components[j]?.TryCast<VehicleObjectModel>();if(model==null)continue;
                        var state=Find(model);if(state==null || state.failed)continue;
                        try{Apply(model);}
                        catch(Exception ex){state.failed=true;Runtime.Warn(state.Id+" visual unavailable: "+ex.Message);}
                    }
                }
            }
        }
        catch(Exception ex){Runtime.Warn("Sight visual scan unavailable: "+ex.Message);}
    }
}
