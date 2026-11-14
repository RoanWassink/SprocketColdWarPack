using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Powertrains.Layouts;
using SprocketEngineExpansion.Core;
using UnityEngine;
namespace SprocketEngineExpansion.Native;

/// <summary>Render-only casing/bridge children. The existing BuildLayout/Clear hooks own the whole group.</summary>
[HarmonyPatch]
internal static class PowerpackBridge
{
    internal static bool CasingEnabled=true;
    private sealed record Child(GameObject Object,List<Mesh> Meshes);
    private static readonly Dictionary<IntPtr,RenderChildSlot<Child>> slots=new();
    private static void Retire(Child child)
    {
        if(child.Object!=null){child.Object.SetActive(false);UnityEngine.Object.Destroy(child.Object);}
        foreach(var mesh in child.Meshes)UnityEngine.Object.Destroy(mesh);
    }
    internal static void Clear(IntPtr id)
    {if(slots.Remove(id,out var slot))slot.Clear(Retire);}
    // The engine model mesh is combined in CombustionEngineModel.BuildMeshes, after BuildLayout may
    // already have run. Refresh the owned child without dirtying native layout: no native rebuild,
    // no second history step, and the slot retires the old child before creating a new one.
    internal static void Refresh(VehiclePowertrain powertrain)
    {
        if(!slots.TryGetValue(powertrain.Pointer,out var slot))return;
        try
        {
            slot.Rebuild(()=>Create(powertrain),Retire);
        }
        catch(Exception ex){Clear(powertrain.Pointer);Plugin.Warn("powerpack casing/bridge refresh",ex);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehiclePowertrain),nameof(VehiclePowertrain.BuildLayout),new[]{typeof(Sprocket.PowertrainLayouts.PowertrainLayout)})]
    private static void Rebuilt(VehiclePowertrain __instance)
    {
        Clear(__instance.Pointer);
        if(!PowerpackModel.Enabled)return;
        // Keep an empty slot: a later model-mesh completion can fill it through Refresh without a
        // native rebuild. Clear still owns and retires it.
        var slot=new RenderChildSlot<Child>();slots[__instance.Pointer]=slot;
        try{slot.Rebuild(()=>Create(__instance),Retire);}
        catch(Exception ex){Clear(__instance.Pointer);Plugin.Warn("powerpack casing/bridge",ex);}
    }
    private static Child? Create(VehiclePowertrain powertrain)
    {
        var child=new Child(new GameObject("Powerpack visual housings"),new());
        child.Object.transform.SetParent(powertrain.transform,false);
        try
        {
            int visible=0;
            foreach(var engine in PowerpackModel.Engines(powertrain))
            {
                if(!PowerpackModel.Effective(engine))continue;
                var context=PowerpackLayout.Resolve(engine);
                if(context is not {Marked:true,Status:PowerpackStatus.Ready})continue;
                int[] ids=context.ModuleIds;
                if(ids.Length==0)continue;
                bool twin=ids.Length>1;
                if(!CasingEnabled&&!twin)continue;
                var layout=powertrain.Layout;
                var enginePort=layout.GetPort(engine.OutputPortID);
                var rotation=Quaternion.LookRotation(enginePort.direction,enginePort.module.rotation*Vector3.up);
                var basis=new Frame(new(0,0,0),new(rotation.x,rotation.y,rotation.z,rotation.w),new(1,1,1));
                var render=EngineRender(engine);
                if(render==null)continue;
                int layer=render.gameObject.layer;
                Material? material=render.sharedMaterial;
                if(material==null&&layout.GetModule(ids[0]) is {} firstModule&&PowerpackLayout.FindTransmission(powertrain,firstModule) is {} block)
                    material=block.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r=>r.sharedMaterial!=null)?.sharedMaterial;
                if(material==null)continue;
                var boxes=new List<Vec3[]>();bool exact=true;
                foreach(int id in ids)
                {
                    var module=layout.GetModule(id);
                    if(module==null){exact=false;break;}
                    var extent=PowerpackLayout.Measure(powertrain,module,out var measured);
                    if(!exact||!measured||extent==null){exact=false;break;}
                    var r=module.rotation;var qr=new Quat(r.x,r.y,r.z,r.w);
                    boxes.Add(new OrientedBox(ToVec(module.position)+qr.Rotate(extent.Value.Centre),extent.Value.Size,qr).Corners().Select(basis.InverseTransformPoint).ToArray());
                }
                if(!exact||boxes.Count!=ids.Length)continue;
                CasingFit? fit=null;
                if(CasingVisibility.Visible(PowerpackModel.Enabled,CasingEnabled,true,context.Marked))
                {
                    try
                    {
                        var mesh=render.GetComponent<MeshFilter>()?.sharedMesh;
                        if(mesh!=null)
                        {
                            var b=mesh.bounds;var source=new OrientedBox(ToVec(b.center),ToVec(b.size),new(0,0,0,1));
                            var frame=FrameOf(render.transform);var layoutFrame=FrameOf(powertrain.transform);
                            var engineCorners=source.Corners().Select(frame.TransformPoint).Select(layoutFrame.InverseTransformPoint).Select(basis.InverseTransformPoint);
                            fit=CasingFit.FromCorners(engineCorners,boxes.SelectMany(x=>x));
                            var group=NewObject("Powerpack casing "+enginePort.module.id,child.Object.transform,rotation,layer);
                            var renderers=new MeshRenderer[2];
                            for(int lod=0;lod<2;lod++)
                            {
                                var data=fit.Warp(PowerpackMeshes.Data("casing-lod"+lod));
                                var go=NewObject("Casing LOD"+lod,group.transform,Quaternion.identity,layer);
                                var output=Mesh(data,"Powerpack casing LOD"+lod,child.Meshes);
                                go.AddComponent<MeshFilter>().sharedMesh=output;
                                renderers[lod]=go.AddComponent<MeshRenderer>();renderers[lod].sharedMaterial=material;
                            }
                            var lodGroup=group.AddComponent<LODGroup>();
                            lodGroup.SetLODs(new Il2CppReferenceArray<LOD>(new[]{
                                new LOD{screenRelativeTransitionHeight=.35f,renderers=new Il2CppReferenceArray<Renderer>(new Renderer[]{renderers[0]})},
                                new LOD{screenRelativeTransitionHeight=.01f,renderers=new Il2CppReferenceArray<Renderer>(new Renderer[]{renderers[1]})}}));
                            lodGroup.RecalculateBounds();visible++;
                        }
                    }
                    catch(Exception ex){Plugin.Warn("powerpack casing fit",ex);}
                }
                if(twin&&context.SplitterId>=0&&layout.GetModule(context.SplitterId) is {} splitterModule)
                {
                    var a=boxes[0];var b=boxes[1];if(a.Average(v=>v.X)>b.Average(v=>v.X))(a,b)=(b,a);
                    double x0=a.Max(v=>v.X),x1=b.Min(v=>v.X),y0=Math.Max(a.Min(v=>v.Y),b.Min(v=>v.Y)),y1=Math.Min(a.Max(v=>v.Y),b.Max(v=>v.Y));
                    double z0=Math.Max(a.Min(v=>v.Z),b.Min(v=>v.Z)),z1=Math.Min(a.Max(v=>v.Z),b.Max(v=>v.Z));
                    if(x1-x0<.001||y1-y0<.001||z1-z0<.001)continue;
                    var splitter=basis.InverseTransformPoint(ToVec(splitterModule.position));
                    double radius=(x1-x0)*.24;z0=Math.Min(z0,splitter.Z-radius);z1=Math.Max(z1,splitter.Z+radius);
                    var data=PowerpackBridgeGeometry.Build(x1-x0,y1-y0,z1-z0,Math.Clamp(splitter.X-(x0+x1)*.5,-(x1-x0)*.35,(x1-x0)*.35),Math.Clamp(splitter.Z-z0,0,z1-z0));
                    data=new(data.Vertices.Select(v=>v+new Vec3((x0+x1)*.5,y0,z0)).ToArray(),data.Triangles);
                    if(fit!=null)data=fit.ClearBridge(data);
                    var go=NewObject("Powerpack Twin bridge",child.Object.transform,rotation,layer);
                    go.AddComponent<MeshFilter>().sharedMesh=Mesh(data,"Powerpack Twin bridge",child.Meshes);
                    go.AddComponent<MeshRenderer>().sharedMaterial=material;visible++;
                }
            }
            if(visible==0){Retire(child);return null;}
            return child;
        }
        catch{Retire(child);throw;}
    }
    private static MeshRenderer? EngineRender(CombustionEngine engine)
    {
        var source=engine.modelGenerator?.TryCast<CombustionEngineModel>()?.outputLODs;
        var renderers=engine.transform.GetComponentsInChildren<MeshRenderer>();
        if(source!=null&&source.Length>0)
        {
            var exact=renderers.FirstOrDefault(r=>r.GetComponent<MeshFilter>()?.sharedMesh?.Pointer==source[0]?.Pointer);
            if(exact!=null)return exact;
        }
        // Native non-V engines have a different generator. Use the largest current engine render mesh.
        return renderers.Where(r=>r.GetComponent<MeshFilter>()?.sharedMesh!=null).OrderByDescending(r=>
        {var s=r.GetComponent<MeshFilter>().sharedMesh.bounds.size;return s.x*s.y*s.z;}).FirstOrDefault();
    }
    private static GameObject NewObject(string name,Transform parent,Quaternion rotation,int layer)
    {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localRotation=rotation;go.layer=layer;return go;}
    private static Mesh Mesh(PowerpackBridgeMesh data,string name,List<Mesh> owned)
    {
        var mesh=new Mesh{name=name};owned.Add(mesh);
        mesh.vertices=new Il2CppStructArray<Vector3>(data.Vertices.Select(ToVector).ToArray());mesh.triangles=new Il2CppStructArray<int>(data.Triangles);
        mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    // Existing geometry entry point retained for the regression preview/tests.
    internal static Mesh Build(float width,float height,float length,float capX,float capZ)=>
        Mesh(PowerpackBridgeGeometry.Build(width,height,length,capX,capZ),"Powerpack Twin bridge",new());
    private static Vec3 ToVec(Vector3 v)=>new(v.x,v.y,v.z);
    private static Vector3 ToVector(Vec3 v)=>new((float)v.X,(float)v.Y,(float)v.Z);
    private static Frame FrameOf(Transform t)=>new(ToVec(t.position),new(t.rotation.x,t.rotation.y,t.rotation.z,t.rotation.w),ToVec(t.lossyScale));
    // Existing unique target. Never hook the folded VehicleComponent.Release.
    [HarmonyPrefix,HarmonyPatch(typeof(VehiclePowertrain),nameof(VehiclePowertrain.Clear))]
    private static void Cleared(VehiclePowertrain __instance)=>Clear(__instance.Pointer);
}
