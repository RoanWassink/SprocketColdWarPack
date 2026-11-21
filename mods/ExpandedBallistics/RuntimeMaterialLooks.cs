using Sprocket.AssetManagement;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;
using NVector=System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

// Invoked ONLY from the pre-existing cassette overlay callbacks. No new patches.
// Native materials, paint, meshes, colliders and blueprint data are never written.
internal static class RuntimeMaterialLooks
{
    private sealed record View(GameObject Root,Mesh Mesh,OverlayCopy Data,string Id,Vector3 Scale,int Scene);
    private sealed class Source
    {
        internal AssetLoadRequest<IVehicleMaterialFactory>? Request;
        internal Il2CppSystem.Threading.Tasks.Task? Task;
        internal Material? Material;
        internal float Started;
        internal bool Failed;
        internal bool Custom,CustomFailed;
    }
    private static readonly Dictionary<string,Source> sources=new();
    private static readonly Dictionary<long,PlateStructure> plates=new();
    private static readonly OverlayBuildQueue<PlateStructure> pending=new();
    private static readonly OverlayVisibility hidden=new(ex=>Warn("restore",ex.Message));
    private static readonly MaterialLookCache<View> cache=new(v=>{if(v.Root!=null){v.Root.SetActive(false);Object.DestroyImmediate(v.Root);}if(v.Mesh!=null)Object.DestroyImmediate(v.Mesh);});
    private static readonly HashSet<string> warnings=new();
    private static readonly List<long> removed=new();
    private static readonly Func<string,bool> pluginLoaded=IsPluginLoaded;
    private static bool diagnosticOverlay,wasDesigner,suppressedLogged;
    private static float nextPoll;
    private static int lastFrame=-1;

    private static bool IsPluginLoaded(string guid)
    {
        var plugins=BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance?.Plugins;
        return plugins!=null&&plugins.ContainsKey(guid);
    }
    internal static bool SuppressedByFloppyParts()
    {
        if(MaterialLook.OverlaysEnabled(pluginLoaded))return false;
        if(!suppressedLogged)
        {
            suppressedLogged=true;
            Plugin.ModLog.LogInfo($"[Material look] Disabled automatically: {MaterialLook.FloppyPartsGuid} is loaded; armour protection for ebWood, ebRubber, ebFabric and ebGlass remains active.");
        }
        if(plates.Count>0||pending.Count>0||hidden.Count>0||cache.Count>0)
        {
            try{SuspendAll();cache.Clear();hidden.RestoreAll();pending.Clear();plates.Clear();}
            catch(Exception ex){Warn("suppress",ex.Message);}
        }
        return true;
    }

    internal static void NoteModel(VehicleObjectModel model)
    {
        if(SuppressedByFloppyParts())return;
        try
        {
            var components=model?.VehicleObject?.Components;if(components==null)return;
            int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for(int i=0;i<count;i++)if(components[i]?.TryCast<PlateStructure>() is {} plate)Note(plate);
        }
        catch(Exception ex){Warn("model",ex.Message);}
    }
    internal static void Note(PlateStructure plate,bool changed=false)
    {
        if(SuppressedByFloppyParts())return;
        try
        {
            if(plate==null)return;
            long key=plate.Pointer.ToInt64();
            if(MaterialLook.For(plate.armourTechID)==null){if(plates.ContainsKey(key))Release(plate);return;}
            plates[key]=plate;
            if(changed)Suspend(key);
            if(changed||!cache.TryGet(key,out _))pending.Schedule(key,plate,Time.unscaledTime,.15f,changed);
        }
        catch(Exception ex){Warn("note",ex.Message);}
    }
    internal static void Overlay(bool active)
    {
        if(SuppressedByFloppyParts())return;
        if(diagnosticOverlay==active)return;
        diagnosticOverlay=active;SuspendAll();foreach(var p in plates)pending.Schedule(p.Key,p.Value,Time.unscaledTime,.15f);
    }
    internal static void BeginEdit(){if(SuppressedByFloppyParts())return;SuspendAll();foreach(var p in plates)pending.Schedule(p.Key,p.Value,Time.unscaledTime,.15f);}
    internal static void Release(PlateStructure plate)
    {if(plate==null)return;long key=plate.Pointer.ToInt64();hidden.Restore(key);cache.Remove(key);pending.Remove(key);plates.Remove(key);}
    private static void Suspend(long key){hidden.Restore(key);if(cache.TryGet(key,out var v)&&v!.Root!=null)v.Root.SetActive(false);}
    private static void SuspendAll(){foreach(var e in cache.Entries)Suspend(e.Key);}
    internal static void Tick(bool designer)
    {
        if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        if(SuppressedByFloppyParts())return;
        try{TickCore(designer);}catch(Exception ex){SuspendAll();Warn("tick",ex.Message);}
    }
    private static void TickCore(bool designer)
    {
        if(designer!=wasDesigner){SuspendAll();diagnosticOverlay=false;wasDesigner=designer;nextPoll=0;foreach(var p in plates)pending.Schedule(p.Key,p.Value,Time.unscaledTime,.2f);}
        hidden.RestoreOrphans(key=>cache.TryGet(key,out _));
        removed.Clear();
        foreach(var p in plates)
        {
            try
            {
                var plate=p.Value;
                if(plate==null||plate.VehicleObject==null||plate.VehicleObject.Released||MaterialLook.For(plate.armourTechID)==null){removed.Add(p.Key);continue;}
                if(!cache.TryGet(p.Key,out var view))continue;
                if(view!.Root==null||view.Scene!=plate.gameObject.scene.handle)
                {Suspend(p.Key);cache.Remove(p.Key);pending.Schedule(p.Key,plate,Time.unscaledTime,.2f);continue;}
                if(view.Id!=plate.armourTechID||view.Scale!=plate.transform.lossyScale)
                {Suspend(p.Key);pending.Schedule(p.Key,plate,Time.unscaledTime,.15f);continue;}
                if(designer&&diagnosticOverlay||PlateMeshAccess.Gate.Quiet){Suspend(p.Key);continue;}
                if(!plate.gameObject.activeInHierarchy){Suspend(p.Key);continue;}
                if(!plate.HasMesh||plate.meshRefresh||plate.Mesh.RebuildRequired){Suspend(p.Key);pending.Schedule(p.Key,plate,Time.unscaledTime,.2f);continue;}
                if(!view.Root.activeSelf){pending.Schedule(p.Key,plate,Time.unscaledTime,.1f);continue;}
                if(!hidden.Intact(p.Key)){Suspend(p.Key);pending.Schedule(p.Key,plate,Time.unscaledTime,.1f);continue;}
                Place(view.Root.transform,plate.transform);
            }
            catch(Exception ex){Suspend(p.Key);removed.Add(p.Key);Warn("follow",ex.Message);}
        }
        foreach(var key in removed){hidden.Restore(key);cache.Remove(key);pending.Remove(key);plates.Remove(key);}
        if(designer&&diagnosticOverlay||PlateMeshAccess.Gate.Quiet)return;
        // No geometry reads while the editor is dragging; Play mouse buttons are unrelated.
        if(designer&&PlateMeshAccess.Gate.Busy)return;
        if(Time.unscaledTime>=nextPoll)
        {nextPoll=Time.unscaledTime+1.1f;foreach(var p in plates)pending.Schedule(p.Key,p.Value,Time.unscaledTime,0);}
        if(pending.Count==0)return;
        foreach(var job in pending.TakeDue(Time.unscaledTime,4))
        {
            try{Build(job.Plate,designer);}
            catch(Exception ex){Suspend(job.Key);Warn("build:"+job.Key,ex.Message);pending.Schedule(job.Key,job.Plate,Time.unscaledTime,2f);}
        }
    }
    private static void Build(PlateStructure plate,bool designer)
    {
        if(SuppressedByFloppyParts())return;
        if(plate==null)return;
        long key=plate.Pointer.ToInt64();var look=MaterialLook.For(plate.armourTechID);
        if(look==null||plate.VehicleObject==null||plate.VehicleObject.Released){Release(plate);return;}
        if(!plate.gameObject.activeInHierarchy){Suspend(key);pending.Schedule(key,plate,Time.unscaledTime,1f);return;}
        var material=GetMaterial(look);
        if(material==null){Suspend(key);if(!sources[look.Id].Failed)pending.Schedule(key,plate,Time.unscaledTime,.25f);return;}
        var snap=Read(plate,designer);
        if(snap==null){Suspend(key);pending.Schedule(key,plate,Time.unscaledTime,.25f);return;}
        var scale=plate.transform.lossyScale;
        var signature=OverlayContent.Signature(snap.Signature,look.Id,scale.ToString("R"));
        if(cache.TryReuse(key,signature,out var old))
        {Activate(key,plate,old!,look);return;}
        var parts=new[]{snap.Front,snap.Back,snap.Sides};
        var custom=sources[look.Id].Custom?RuntimeCustomTextures.For(look.Id):null;
        var data=custom==null?MaterialLookGeometry.Copy(look,new(scale.x,scale.y,scale.z),parts):MaterialLookGeometry.CopyRepeat(look,new(scale.x,scale.y,scale.z),custom.Settings.tileSizeM,parts);
        if(plate.Mirrored)data=PlateOverlayGeometry.Mirror(data,snap.Mirror);
        var tangents=MaterialLookGeometry.Tangents(data);
        GameObject? root=null;Mesh? mesh=null;
        try
        {
            root=new GameObject("Material look "+look.Id){hideFlags=HideFlags.HideAndDontSave};root.SetActive(false);
            root.layer=plate.gameObject.layer;Place(root.transform,plate.transform);
            mesh=new Mesh{name="material-look-native-copy",hideFlags=HideFlags.HideAndDontSave,indexFormat=IndexFormat.UInt32};
            mesh.vertices=data.Vertices.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
            mesh.normals=data.Normals.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
            mesh.uv=data.Uv.Select(v=>new Vector2(v.X,v.Y)).ToArray();
            mesh.tangents=tangents.Select(v=>new Vector4(v.X,v.Y,v.Z,v.W)).ToArray();
            mesh.triangles=data.Triangles;mesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
            // Restore old native flags before replacing resources. An exception never strands a hidden plate.
            hidden.Restore(key);
            var view=new View(root,mesh,data,look.Id,scale,plate.gameObject.scene.handle);cache.Put(key,signature,view);root=null;mesh=null;
            Activate(key,plate,view,look);
            if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Material look] plate {plate.VUID.Value} {look.Id}: {data.Vertices.Length} vertices; generation {cache.Rebuilds}; cached until content/scale/material changes.");
        }
        finally{if(root!=null)Object.DestroyImmediate(root);if(mesh!=null)Object.DestroyImmediate(mesh);}
    }
    private static PlateOverlaySnapshot? Read(PlateStructure plate,bool designer)
    {
        if(designer)return PlateOverlaySnapshotReader.Read(plate,"material look");
        // Play may discard the authored editMesh. Read the completed tessellation only,
        // never walk editor faces or ask native to rebuild/upload anything.
        if(!plate.HasMesh||plate.meshRefresh)return null;
        var mesh=plate.Mesh;if(mesh.Released||mesh.RebuildRequired)return null;
        var front=PlateOverlaySnapshotReader.Copy(mesh.frontMesh);var back=PlateOverlaySnapshotReader.Copy(mesh.thickenFaceMesh);var sides=PlateOverlaySnapshotReader.Copy(mesh.thickenEdgeMesh);
        if(front==null||back==null||sides==null||mesh.Released||mesh.RebuildRequired||plate.meshRefresh||plate.Mesh.Pointer!=mesh.Pointer)return null;
        var mirror=NativePlateMirror.Read(plate);
        return new(front,back,sides,CassetteNativeOverlay.Fingerprint(front,back,sides)+"/"+mirror,mirror);
    }
    private static void Place(Transform visual,Transform plate){visual.SetPositionAndRotation(plate.position,plate.rotation);visual.localScale=plate.lossyScale;}
    private static void Activate(long key,PlateStructure plate,View view,MaterialLook look)
    {
        if(wasDesigner&&diagnosticOverlay||PlateMeshAccess.Gate.Quiet){Suspend(key);return;}
        Place(view.Root.transform,plate.transform);
        try
        {
            if(!hidden.Intact(key))
            {
                hidden.Restore(key);
                // Prove all visible LODs before hiding any. Opaque keeps the native shadow proxy.
                var renderers=new List<(int Slot,Renderer Renderer)>();
                for(int slot=0;slot<(look.Glass?3:2);slot++)
                {
                    var wrapper=slot==0?plate.highDetailRenderer:slot==1?plate.lowDetailRenderer:plate.shadowProxyRenderer;
                    var renderer=wrapper?.Renderer;if(renderer==null)continue;
                    var mesh=wrapper?.SharedMesh;
                    if(mesh==null||!Covers(plate,renderer,mesh,view.Data))throw new InvalidOperationException("Native LOD coverage not proven; native appearance retained.");
                    renderers.Add((slot,renderer));
                }
                if(renderers.Count==0)throw new InvalidOperationException("No native renderer ready.");
                foreach(var r in renderers)hidden.Hide(key,new NativeRenderer(plate,r.Slot,r.Renderer));
            }
            view.Root.SetActive(true);
        }
        catch{Suspend(key);throw;}
    }
    private sealed class NativeRenderer:IOverlayNativeRenderer
    {
        private readonly PlateStructure plate;private readonly int slot,instance;private readonly Renderer renderer;
        internal NativeRenderer(PlateStructure plate,int slot,Renderer renderer){this.plate=plate;this.slot=slot;this.renderer=renderer;instance=renderer.GetInstanceID();}
        public long Identity=>renderer.Pointer.ToInt64();
        public bool Current=>plate!=null&&renderer!=null&&(slot==0?plate.highDetailRenderer:slot==1?plate.lowDetailRenderer:plate.shadowProxyRenderer)?.Renderer?.Pointer==renderer.Pointer&&renderer.GetInstanceID()==instance;
        public bool Hidden{get=>renderer.forceRenderingOff;set=>renderer.forceRenderingOff=value;}
    }
    private static bool Covers(PlateStructure plate,Renderer renderer,Mesh mesh,OverlayCopy data)
    {
        var bounds=mesh.bounds;var corners=new NVector[8];
        for(int i=0;i<8;i++)
        {
            var p=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            p=plate.transform.worldToLocalMatrix.MultiplyPoint3x4(renderer.transform.localToWorldMatrix.MultiplyPoint3x4(p));corners[i]=new(p.x,p.y,p.z);
        }
        long count=0;for(int i=0;i<mesh.subMeshCount;i++)count+=mesh.GetIndexCount(i)/3;
        return count<=int.MaxValue&&PlateOverlayGeometry.Covers(data.Vertices,data.Triangles.Length/3,corners,(int)count);
    }
    private static Material? GetMaterial(MaterialLook look)
    {
        if(!sources.TryGetValue(look.Id,out var source)){sources.Add(look.Id,source=new());source.Started=Time.unscaledTime;}
        if(source.Material!=null)return source.Material;
        if(source.Failed)return null;
        try
        {
            RuntimeCustomTextures.Initialize();
            var custom=RuntimeCustomTextures.For(look.Id);
            if(custom!=null&&!source.CustomFailed)
            {
                try{source.Material=Make(look,null,custom);source.Custom=true;return source.Material;}
                catch(Exception ex){source.CustomFailed=true;Warn("custom:"+look.Id,ex.Message+"; using native look.");}
            }
            if(look.Glass){source.Material=Make(look,null);return source.Material;}
            if(source.Request==null)
            {
                source.Request=new(look.Guid,Il2CppSystem.Threading.CancellationToken.None);
                source.Task=source.Request.Load(Il2CppSystem.Threading.CancellationToken.None);return null;
            }
            if(source.Request.State!=AssetRequestState.Loaded)
            {
                if(source.Request.State is AssetRequestState.Faulted or AssetRequestState.Cancelled or AssetRequestState.Released||Time.unscaledTime-source.Started>30f)
                    throw new InvalidOperationException("Native material load failed/timed out: "+source.Request.State);
                return null;
            }
            var config=source.Request.LoadedAsset?.TryCast<VehicleMaterialConfiguration>();
            if(config==null||config.name!=look.Asset||config.BaseColourMap==null)throw new InvalidOperationException("Native material identity/texture mismatch.");
            source.Material=Make(look,config);
            Plugin.ModLog.LogInfo($"[Material look] {look.Id}: {look.Asset} ({look.Guid}); native base colour/normal; no paint/grime; shared cached material.");
            return source.Material;
        }
        catch(Exception ex){source.Failed=true;Warn(look.Id,ex.Message+" Native plate retained.");return null;}
    }
    private static Material Make(MaterialLook look,VehicleMaterialConfiguration? config,CustomTextureSet<Texture2D>? custom=null)
    {
        var shader=Shader.Find("HDRP/Lit");
        if(shader==null||!shader.isSupported)throw new InvalidOperationException("Supported HDRP/Lit unavailable.");
        var material=new Material(shader){hideFlags=HideFlags.HideAndDontSave,name="EB "+look.Id};
        try
        {
            void Float(string key,float value){if(material.HasProperty(key))material.SetFloat(key,value);}
            Float("_Metallic",0);Float("_Smoothness",look.Smoothness);Float("_CullMode",0);Float("_CullModeForward",0);Float("_TransparentCullMode",0);
            Float("_DoubleSidedEnable",1);Float("_DoubleSidedNormalMode",1);Float("_UVBase",0);Float("_NormalMapSpace",0);
            material.SetVector("_DoubleSidedConstants",new Vector4(1,1,-1,0));material.SetVector("_UVMappingMask",new Vector4(1,0,0,0));
            material.SetColor("_BaseColor",look.Glass?new Color(.60f,.86f,.85f,.23f):Color.white);
            if(config!=null)
            {
                material.SetTexture("_BaseColorMap",config.BaseColourMap);material.SetTextureScale("_BaseColorMap",Vector2.one);material.SetTextureOffset("_BaseColorMap",Vector2.zero);
                if(config.NormalMap!=null){material.SetTexture("_NormalMap",config.NormalMap);material.SetTextureScale("_NormalMap",Vector2.one);material.SetTextureOffset("_NormalMap",Vector2.zero);}
                // Native ARM/DPM are not HDRP mask/paint maps. Never feed the wrong channels to HDRP.
            }
            if(custom!=null)RuntimeCustomTextures.Apply(material,custom);
            if(look.Glass)
            {
                bool transparent=false;
                try
                {
                    HDMaterial.SetSurfaceType(material,true);Float("_BlendMode",0);Float("_ZWrite",0);Float("_TransparentZWrite",0);Float("_SrcBlend",1);Float("_DstBlend",10);Float("_CoatMask",1);
                    Float("_AlphaSrcBlend",1);Float("_AlphaDstBlend",10);material.renderQueue=3000;
                    bool validated=HDMaterial.ValidateMaterial(material);
                    transparent=MaterialLook.TransparentGlass(shader.isSupported,material.HasProperty("_SurfaceType")&&material.HasProperty("_DstBlend"),validated,material.GetFloat("_SurfaceType")==1,material.renderQueue==3000&&material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
                }
                catch(Exception ex){Warn("glass-validation",ex.Message);}
                if(!transparent)
                {
                    HDMaterial.SetSurfaceType(material,false);material.SetColor("_BaseColor",new Color(.035f,.07f,.075f,1));Float("_ZWrite",1);Float("_TransparentZWrite",0);Float("_SrcBlend",1);Float("_DstBlend",0);Float("_AlphaSrcBlend",1);Float("_AlphaDstBlend",0);material.renderQueue=2000;
                    if(!HDMaterial.ValidateMaterial(material))throw new InvalidOperationException("Opaque glass fallback validation failed.");
                    Warn("glass-fallback","Transparent HDRP variant unavailable: using dark glossy opaque glass.");
                }
                Plugin.ModLog.LogInfo("[Material look] glass "+(transparent?"transparent; queue 3000":"opaque fallback; queue 2000")+"; two-sided; smoothness .96.");
            }
            else if(!HDMaterial.ValidateMaterial(material))throw new InvalidOperationException("HDRP texture material validation failed.");
            return material;
        }
        catch{Object.DestroyImmediate(material);throw;}
    }
    private static void Warn(string key,string message){if(warnings.Add(key))Plugin.ModLog.LogWarning("[Material look] "+message);}
    internal static void Clear()
    {
        SuspendAll();cache.Clear();hidden.RestoreAll();pending.Clear();plates.Clear();
        foreach(var source in sources.Values){if(source.Material!=null)Object.DestroyImmediate(source.Material);try{source.Request?.Cancel();source.Request?.Unload();}catch(Exception ex){Warn("unload",ex.Message);}}
        sources.Clear();RuntimeCustomTextures.Clear();warnings.Clear();removed.Clear();diagnosticOverlay=false;wasDesigner=false;suppressedLogged=false;nextPoll=0;lastFrame=-1;
    }
}
