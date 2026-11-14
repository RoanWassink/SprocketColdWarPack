using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using Sprocket.Vehicles.Turrets;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

namespace SprocketOscillatingTurret;

// Pure render adapter. The original ring, basket, motor, native colliders,
// placement frame, resources, blueprint IDs and -X elevation axis stay intact.
[HarmonyPatch]
internal static class PivotHousingVisuals
{
    internal const string Owner="sprocket.oscillatingturret.visuals";
    internal const string PartGuid="31fa3dd0-0738-4d29-b6a5-c3d77c291ea2";
    internal static bool Enabled;
    internal const float MinVisualDiameter=.36f;
    private sealed record Visual(IntPtr Model,IntPtr Controlled,float Diameter,Vector3 Scale,Mesh Body,Mesh Insets,Mesh Upper,GameObject Detail,GameObject Saddle,TurretRing Ring,GameObject Shell);
    private static readonly Dictionary<IntPtr,Visual> visuals=new();
    private static Material? olive,dark;
    private static readonly HashSet<string> warnings=new();
    private static Material Paint(string name,Color c,float smooth)
    {
        var m=new Material(Shader.Find("HDRP/Lit")??throw new InvalidOperationException("HDRP/Lit unavailable")){name=name,hideFlags=HideFlags.HideAndDontSave};
        m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",smooth);HDMaterial.ValidateMaterial(m);return m;
    }
    private static void Materials()
    {
        if(olive==null)olive=Paint("Enclosed pivot olive",new(.17f,.21f,.155f,1),.3f);
        if(dark==null)dark=Paint("Pivot bearing and actuator inserts",new(.075f,.085f,.085f,1),.64f);
    }
    private static Mesh Mesh(PivotMesh data,string name,Func<System.Numerics.Vector3,Vector3> point)
    {
        var mesh=new Mesh{name=name,hideFlags=HideFlags.HideAndDontSave};
        mesh.vertices=data.Vertices.Select(point).ToArray();mesh.triangles=data.Triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    private static GameObject Detail(Transform parent,string name,Mesh mesh,Material material,int layer)
    {
        var root=new GameObject(name){layer=layer};
        root.transform.SetParent(parent,false);root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;
        return root; // No extra colliders, physical body, crew task or mass.
    }
    internal static bool Apply(OwnedTurret entry)=>Apply(entry.RingRef);
    // The housing is our own renderers in the ring's base frame; the native ring
    // model is only hidden (it keeps its picker, colliders and lifecycle). The game
    // can rebuild or re-mesh that model freely without affecting the housing.
    internal static bool Apply(TurretRing? ring)
    {
        if(!Enabled||ring==null)return false;
        if(ring.VehicleObject?.GUID!=PartGuid){Report(ring,$"skipped: guid={ring.VehicleObject?.GUID}");return false;}
        try
        {
            var component=ring.ringModel;var model=component?.TryCast<VehicleObjectModel>()?.Model;
            var controlled=ring.motor?.ControlledTransform??ring.structure?.VehicleTransform;
            var root=ring.VehicleTransform?.transform;
            if(component==null||!component.HasModel||model==null||controlled==null||root==null)
            {
                Report(ring,$"waiting: hasModel={component?.HasModel} model={model!=null} controlled={controlled!=null}");
                return true;
            }
            // Small elevation rings (the ATGM mount uses 180 mm) would hide the
            // housing under their own structure; keep the cheeks visibly wider.
            float d=Math.Max(ring.Blueprint.RingDiameter*.001f,MinVisualDiameter);
            if(!visuals.TryGetValue(ring.Pointer,out var v)||v.Model!=model.Pointer||v.Controlled!=controlled.Pointer||Math.Abs(v.Diameter-d)>.0001f||v.Shell==null)
            {
                Release(ring.Pointer);Materials();var geometry=new PivotHousingGeometry(d);
                Vector3 Point(System.Numerics.Vector3 p)=>new(p.X,p.Y,p.Z);
                var body=Mesh(geometry.Body,"Horizontal pivot and actuator housing",Point);
                var inserts=Mesh(geometry.Inserts,"Enclosed pivot bearing details",Point);
                var upper=Mesh(geometry.Upper,"Pivot upper saddle",Point);
                var shell=Detail(root,"Pivot housing",body,olive!,model.Layer);
                var detail=Detail(root,"Enclosed pivot details",inserts,dark!,model.Layer);
                var saddle=Detail(controlled.transform,"Native-driven pivot saddle",upper,olive!,model.Layer);
                visuals[ring.Pointer]=v=new(model.Pointer,controlled.Pointer,d,component.ModelScale,body,inserts,upper,detail,saddle,ring,shell);
            }
            model.SetVisible(false);
            ring.basket?.TryCast<TurretBasket>()?.SetVisible(false);
            v.Shell.SetActive(true);v.Detail.SetActive(true);v.Saddle.SetActive(true);
            return true;
        }
        catch(Exception ex)
        {if(warnings.Add(ex.Message))Plugin.Warn("[Pivot housing] Visual adapter: "+ex.Message);return true;}
    }
    // Editor and play rings are housed as soon as the native ring is (re)built,
    // independent of the elevation ownership registry.
    [HarmonyPostfix,HarmonyPatch(typeof(TurretRing),nameof(TurretRing.Build))]
    private static void Built(TurretRing __instance){try{if(__instance.VehicleObject?.GUID==PartGuid)Apply(__instance);}catch{}}
    // One line per ring and state, so a silent skip shows in the log.
    private static readonly HashSet<string> reports=new();
    private static void Report(TurretRing ring,string state)
    {if(reports.Add(ring.Pointer+state.Split(':')[0]))Plugin.Info($"[Pivot housing] ring={ring.VUID.Value} {state}");}
    [HarmonyPrefix,HarmonyPatch(typeof(TurretBasket),nameof(TurretBasket.SetVisible))]
    private static void HideBasket(TurretBasket __instance,ref bool __0)
    {if(Enabled&&__instance.VehicleObject?.GUID==PartGuid)__0=false;}
    internal static void Release(IntPtr pointer)
    {
        if(!visuals.Remove(pointer,out var v))return;
        if(v.Detail!=null){v.Detail.SetActive(false);Object.Destroy(v.Detail);}
        if(v.Saddle!=null){v.Saddle.SetActive(false);Object.Destroy(v.Saddle);}
        if(v.Shell!=null){v.Shell.SetActive(false);Object.Destroy(v.Shell);}
        Object.Destroy(v.Body);Object.Destroy(v.Insets);Object.Destroy(v.Upper);
    }
    // Just before rendering: keep the native ring model hidden (the game may show it
    // again on rebuild) and rebuild the housing if the native model was replaced.
    internal static void Frame()
    {
        if(!Enabled||visuals.Count==0)return;
        foreach(var pair in visuals.ToArray())
        {
            try
            {
                var v=pair.Value;
                var model=v.Ring.ringModel?.TryCast<VehicleObjectModel>()?.Model;
                if(model==null)continue;
                if(model.Pointer!=v.Model||v.Shell==null){Release(pair.Key);Apply(v.Ring);continue;}
                model.SetVisible(false);
            }
            catch(Exception ex){Release(pair.Key);if(warnings.Add("frame:"+ex.Message))Plugin.Warn("[Pivot housing] Frame: "+ex.Message);}
        }
    }
    // Ownership entries come and go during battle (motor re-enable, physics hand-
    // over); the housing must not. Drop a visual only when its ring itself is gone.
    // Component release also clears it through the release prefix.
    internal static void Sweep(IEnumerable<OwnedTurret> entries)
    {
        foreach(var pair in visuals.ToArray())
        {
            bool gone;
            try{gone=pair.Value.Ring==null||pair.Value.Ring.WasCollected||pair.Value.Ring.VehicleObject==null||pair.Value.Ring.VehicleObject.Released;}
            catch{gone=true;}
            if(gone)Release(pair.Key);
        }
    }
    internal static void Clear()
    {
        foreach(var p in visuals.Keys.ToArray())Release(p);
        if(olive!=null)Object.Destroy(olive);if(dark!=null)Object.Destroy(dark);
        olive=dark=null;warnings.Clear();reports.Clear();
    }
}
