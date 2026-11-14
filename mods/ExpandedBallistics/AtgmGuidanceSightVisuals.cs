using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.PartImporting;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;
namespace SprocketExpandedBallistics;

[HarmonyPatch]
internal static class AtgmGuidanceSightVisuals
{
    internal const string Owner="sprocket.expandedballistics.atgm.sight-visuals";
    private sealed record Visual(VehicleObjectModel Component,Transform Target,Mesh HeadMesh,Mesh Pedestal,Mesh Hull,GameObject Head,Material Olive,Material Dark,Material Glass);
    private static readonly Dictionary<IntPtr,Visual> visuals=new();
    private static readonly HashSet<string> warnings=new();
    private static Sprite? icon;
    private static Texture2D? iconTexture;
    private static void Guard(string name,Action action)
    {try{action();}catch(Exception ex){if(warnings.Add(name))Plugin.ModLog.LogWarning("[ATGM sight] "+name+": "+ex.Message);}}
    private static Material Paint(string name,Color color,float smooth)
    {
        var m=new Material(Shader.Find("HDRP/Lit")??throw new InvalidOperationException("HDRP/Lit unavailable")){name=name,hideFlags=HideFlags.HideAndDontSave};
        m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",smooth);HDMaterial.ValidateMaterial(m);return m;
    }
    private static void Box(Transform parent,Material mat,string name,Vector3 position,Vector3 scale)
    {
        var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.layer=parent.gameObject.layer;
        var collider=o.GetComponent<Collider>();collider.enabled=false;Object.Destroy(collider);
        o.transform.SetParent(parent,false);o.transform.localPosition=position;o.transform.localScale=scale;
        o.GetComponent<MeshRenderer>().sharedMaterial=mat;
    }
    private static Mesh Housing(bool head,bool pedestal)
    {
        // Chamfered instrument head; optical endpoint remains at native +Z.
        var points=new[]{new Vector2(-.12f,-.09f),new Vector2(.12f,-.09f),new Vector2(.15f,-.06f),new Vector2(.15f,.06f),new Vector2(.12f,.09f),new Vector2(-.12f,.09f),new Vector2(-.15f,.06f),new Vector2(-.15f,-.06f)};
        var vertices=new List<Vector3>();var triangles=new List<int>();
        // Each face owns its vertices so normals stay flat; shared corners made
        // the end caps shade like a hollow.
        void Face(params Vector3[] v){int s=vertices.Count;vertices.AddRange(v);for(int i=1;i<v.Length-1;i++)triangles.AddRange(new[]{s,s+i,s+i+1});}
        var rear=points.Select(p=>new Vector3(p.x,p.y,-.35f)).ToArray();var front=points.Select(p=>new Vector3(p.x,p.y,.026f)).ToArray();
        if(head)
        {
            Face(rear.Reverse().ToArray());Face(front);
            for(int i=0;i<8;i++){int n=(i+1)%8;Face(rear[i],rear[n],front[n],front[i]);}
        }
        // Pedestal shares the native housing collider and selection association.
        var c=new List<Vector3>();
        foreach(var x in new[]{-.08f,.08f})foreach(var y in new[]{-.17f,-.08f})foreach(var z in new[]{-.24f,-.08f})c.Add(new(x,y,z));
        if(pedestal)foreach(var q in new[]{new[]{0,1,3,2},new[]{4,6,7,5},new[]{0,4,5,1},new[]{2,3,7,6},new[]{0,2,6,4},new[]{1,5,7,3}})Face(q.Select(i=>c[i]).ToArray());
        var mesh=new Mesh{name="Modern dual-aperture ATGM sight",hideFlags=HideFlags.HideAndDontSave};
        mesh.vertices=vertices.ToArray();mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    private static void Apply(VehicleObjectModel component)
    {
        if(component.VehicleObject?.GUID!=RuntimeAtgmGuidanceSight.Guid||component.Model==null)return;
        Remove(component.Pointer);
        var model=component.Model;var mesh=Housing(true,false);var pedestal=Housing(false,true);var hull=Housing(true,true);
        var olive=Paint("ATGM sight housing",new(.16f,.20f,.15f,1),.28f);
        var dark=Paint("ATGM lens surround",new(.018f,.025f,.027f,1),.35f);
        var glass=Paint("ATGM blue coated optics",new(.035f,.22f,.32f,1),.88f);
        // Ordinary, detached objects: native firing can clone model children.
        var detail=new GameObject("ATGM sight head"){layer=model.transform.gameObject.layer};
        detail.AddComponent<MeshFilter>().sharedMesh=mesh;detail.AddComponent<MeshRenderer>().sharedMaterial=olive;
        var visual=new Visual(component,model.transform,mesh,pedestal,hull,detail,olive,dark,glass);visuals[component.Pointer]=visual;
        Install();Follow(visual);
        component.MarkDynamic();model.Flags&=~VehicleRendererFlags.Static;
        model.SetMesh(pedestal);model.SetShadowProxy(pedestal);model.SetInteractionMesh(hull);model.SetCollisionMesh(hull);
        component.interactionCollider?.SetMesh(hull.vertices,hull.triangles);
        model.SetMaterial(new Sprocket.Vehicles.AssetManagement.VehicleMaterial(olive){Flags=VehicleMaterialFlags.Unpainted|VehicleMaterialFlags.DisableGrime|VehicleMaterialFlags.CustomShader});
        foreach(var x in new[]{-.071f,.071f})
        {
            Box(detail.transform,dark,"Optical recess",new(x,0,.029f),new(.12f,.125f,.011f));
            Box(detail.transform,glass,"Coated objective",new(x,0,.0395f),new(.098f,.102f,.003f));
            Box(detail.transform,olive,"Lens hood",new(x,.069f,.030f),new(.132f,.012f,.048f));
        }
        detail.SetActive(component.visible);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.OnModelLoaded))]
    private static void Loaded(VehicleObjectModel __instance)
    {if(__instance.VehicleObject?.GUID==RuntimeAtgmGuidanceSight.Guid)Guard("Modern model",()=>Apply(__instance));}
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Build))]
    private static void Built(VehicleObjectModel __instance)=>Loaded(__instance);
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.SetVisible))]
    private static void Visible(VehicleObjectModel __instance)
    {if(visuals.TryGetValue(__instance.Pointer,out var v))v.Head.SetActive(__instance.visible);}
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Release))]
    private static void Released(VehicleObjectModel __instance)=>Remove(__instance.Pointer);
    private static void Remove(IntPtr pointer)
    {
        if(!visuals.Remove(pointer,out var v))return;
        if(v.Head!=null){v.Head.SetActive(false);Object.Destroy(v.Head);}
        Object.Destroy(v.HeadMesh);Object.Destroy(v.Pedestal);Object.Destroy(v.Hull);Object.Destroy(v.Olive);Object.Destroy(v.Dark);Object.Destroy(v.Glass);
    }
    private static UnityAction? beforeRender;
    private static void Install()
    {
        if(beforeRender!=null)return;
        beforeRender=DelegateSupport.ConvertDelegate<UnityAction>((Action)BeforeRender);Application.add_onBeforeRender(beforeRender);
    }
    private static void Follow(Visual v)
    {
        var rotation=RuntimeAtgmGuidanceSight.VisualRotation(v.Component.VehicleObject.Pointer,v.Target.rotation);
        var pivot=Vector3.Scale(v.Target.lossyScale,RuntimeAtgmGuidanceSight.HeadPivot);
        v.Head.transform.SetPositionAndRotation(v.Target.position+v.Target.rotation*pivot-rotation*pivot,rotation);
        v.Head.transform.localScale=v.Target.lossyScale;
    }
    private static void BeforeRender()
    {
        foreach(var pair in visuals.ToArray())
            try
            {
                var v=pair.Value;
                if(v.Target==null||v.Head==null||v.Component.WasCollected){Remove(pair.Key);continue;}
                if(v.Head.activeSelf)Follow(v);
            }
            catch(Exception ex){Remove(pair.Key);if(warnings.Add("Head follow"))Plugin.ModLog.LogWarning("[ATGM sight] Head follow: "+ex.Message);}
    }
    internal static void Clear()
    {
        foreach(var p in visuals.Keys.ToArray())Remove(p);
        if(beforeRender!=null){Application.remove_onBeforeRender(beforeRender);beforeRender=null;}
        if(icon!=null)Object.Destroy(icon);if(iconTexture!=null)Object.Destroy(iconTexture);
        icon=null;iconTexture=null;warnings.Clear();
    }
    internal static Sprite Icon()
    {
        if(icon!=null)return icon;
        using var stream=typeof(Plugin).Assembly.GetManifestResourceStream("ExpandedBallistics.Icons.atgm-guidance-sight.png")??throw new InvalidOperationException("Sight icon missing");
        using var bytes=new MemoryStream();stream.CopyTo(bytes);
        iconTexture=new Texture2D(2,2,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
        if(!ImageConversion.LoadImage(iconTexture,bytes.ToArray(),false))throw new InvalidOperationException("Sight icon decode failed");
        icon=Sprite.Create(iconTexture,new Rect(0,0,iconTexture.width,iconTexture.height),new(.5f,.5f),100);icon.hideFlags=HideFlags.HideAndDontSave;return icon;
    }
    [HarmonyPostfix,HarmonyPatch(typeof(PartDefinitionCardFactory),nameof(PartDefinitionCardFactory.CreateCard))]
    private static void Card(PartDefinition __0,PartDisplayCard __result)
    {if(__0?.guid==RuntimeAtgmGuidanceSight.Guid&&__result!=null)Guard("Sight card",()=>__result.Icon=Icon());}
}
