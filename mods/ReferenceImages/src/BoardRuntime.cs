using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.VehicleDesigner;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace SprocketReferenceImages;

internal static class BoardRuntime
{
    internal sealed class Board
    {
        internal VehicleObjectModel Component=null!;
        internal long Vehicle;
        internal string File="", Attempt="", SizeText="";
        internal bool Mirror;
        internal Pose Pose;
        internal SessionMemory.Item? Memory;
        internal ImageLibrary.Entry? Image;
        internal GameObject? Root, Slab;
        internal Transform? ContentTransform, PictureTransform, ModelTransform;
        internal Mesh? Mesh;
        internal Material? Material, FrameMaterial;
        internal IntPtr ModelPointer;
        internal Vector3 LastScale=new(float.NaN,float.NaN,float.NaN);
        internal Vector3 LastInherited=new(float.NaN,float.NaN,float.NaN), ReportScale=new(float.NaN,float.NaN,float.NaN);
        internal float Aspect=1;
        internal bool Dirty=true;
    }
    private static readonly Dictionary<int,Board> boards=new();
    private static readonly SessionMemory memory=new();
    // A managed serial per LIVE native Unity vehicle identity, retaining the Unity wrapper.
    // Never reuse a key just because Unity reuses an instance ID after destruction.
    private sealed record Identity(GameObject Object,long Key);
    private static readonly Dictionary<int,Identity> identities=new();
    private static long nextIdentity;
    private static int lastCount=-1;
    private static float nextDiscovery;
    private static bool discoveryLogged;
    private static VehicleDesignerCore[] cores=Array.Empty<VehicleDesignerCore>();
    internal static Board? Selected {get;private set;}
    internal static bool Designer {get;private set;}
    internal static int SelectedCount {get;private set;}
    private static bool Own(VehicleObjectModel m)
    {
        var o=m.VehicleObject;
        return o!=null && !o.Released && m.ComponentID==BoardPolicy.ModelId && o.HasTag(BoardPolicy.PartName) &&
            o.Vehicle?.PartInfoSource?.GetIdentifier(o.GUID)==BoardPolicy.PartName;
    }
    private static long VehicleKey(VehicleObjectModel m)
    {
        var go=m.Vehicle.gameObject; int id=go.GetInstanceID();
        if(identities.TryGetValue(id,out var old) && old.Object!=null && old.Object.Pointer==go.Pointer)return old.Key;
        identities[id]=new(go,++nextIdentity); return nextIdentity;
    }
    private static Pose ReadPose(VehicleObjectModel m)
    {
        var t=m.VehicleTransform;var p=t.LocalPosition;var q=t.LocalRotation;var s=t.Scale;
        return new(new NVector(p.x,p.y,p.z),new NQuaternion(q.x,q.y,q.z,q.w),new NVector(s.x,s.y,s.z));
    }
    internal static void Poll()
    {
        Designer=SceneManager.GetSceneByName("VehicleDesignerUI").isLoaded && !SceneManager.GetSceneByName("VehicleControlUI").isLoaded;
        if(!Designer){Selected=null;SelectedCount=0;ReferencePanel.Close();return;}
        if(Time.unscaledTime>=nextDiscovery)
        {
            nextDiscovery=Time.unscaledTime+1f;
            Discover();
        }
        // Absence from FindObjectsOfType is NOT deletion: pickup can deactivate a
        // native object temporarily. Keep its picture, memory and known identity.
        foreach(int id in boards.Keys.Where(id=>boards[id].Component==null || boards[id].Component.VehicleObject==null || boards[id].Component.VehicleObject.Released).ToArray())
        {Destroy(boards[id]);boards.Remove(id);}
        foreach(var b in boards.Values)
        {
            try
            {
                if(!b.Component.HasModel)continue;
                Pose pose=ReadPose(b.Component);
                if(b.Pose!=pose){b.Pose=pose;if(b.File.Length>0)Remember(b);}
                RefreshVisual(b);
            }
            catch(Exception ex){Plugin.WarnOnce("board error: "+ex.Message);}
        }
        if(boards.Count!=lastCount){lastCount=boards.Count;if(Plugin.Detailed)Plugin.Info("boards recognized: "+lastCount);}
        Select();
    }
    private static void Discover()
    {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        cores=UnityEngine.Object.FindObjectsOfType<VehicleDesignerCore>().ToArray();
        foreach(var c in UnityEngine.Object.FindObjectsOfType<VehicleObjectModel>())
        {
            if(c==null || !Own(c) || !c.HasModel)continue;
            int id=c.GetInstanceID();
            try
            {
                long vehicle=VehicleKey(c); Pose pose=ReadPose(c);
                if(!boards.TryGetValue(id,out var b) || b.Component.Pointer!=c.Pointer || b.Vehicle!=vehicle)
                {
                    if(b!=null)Destroy(b);
                    b=new Board {Component=c,Vehicle=vehicle,Pose=pose}; boards[id]=b;
                    b.Memory=memory.Match(vehicle,pose,out int count);
                    if(b.Memory!=null){b.File=b.Memory.File;b.Mirror=b.Memory.Mirror;}
                    if(Plugin.Detailed)Plugin.Info($"session memory {(b.Memory!=null?"restored":"refused")}: vehicle={vehicle}, candidates={count}, file={(b.File.Length==0?"NO IMAGE":b.File)}");
                }
            }
            catch(Exception ex){Plugin.WarnOnce("board error: "+ex.Message);}
        }
        if(!discoveryLogged)
        {
            discoveryLogged=true;
            Plugin.Info(FormattableString.Invariant($"discovery: {(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency:0.###} ms (designer, at most 1 Hz)"));
        }
    }
    private static void Select()
    {
        var previous=Selected; Selected=null; SelectedCount=0;
        if(Designer)
        {
            foreach(var core in cores)
            {
                if(core==null || !core.isActiveAndEnabled || !core.HasEditor || core.Editor==null)continue;
                var items=core.Editor.SelectionReader.Items; var selected=new HashSet<int>(); Board? match=null;
                int count=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(int i=0;i<count;i++)
                {
                    var obj=items[i]?.VehicleObject; if(obj==null)continue;
                    if(!selected.Add(obj.GetInstanceID()))continue;
                    match=boards.Values.FirstOrDefault(b=>b.Component.VehicleObject.Pointer==obj.Pointer);
                }
                SelectedCount=selected.Count;
                if(selected.Count==1)Selected=match;
                break;
            }
        }
        if(Selected==null)ReferencePanel.Close();
        else if(!ReferenceEquals(previous,Selected))ReferencePanel.Open();
    }
    private static void Remember(Board b)
    {
        // A new native duplicate can match an existing entry. Fork on its first edit.
        if(b.Memory!=null && boards.Values.Any(other=>!ReferenceEquals(other,b) && ReferenceEquals(other.Memory,b.Memory)))b.Memory=null;
        b.Memory=memory.Remember(b.Vehicle,b.Memory,b.Pose,b.File,b.Mirror);
    }
    internal static void Assign(string file)
    {
        var b=Selected; if(!Designer || b==null)return;
        // Validate and decode before replacing the current assignment.
        var image=ImageLibrary.Acquire(file);
        ImageLibrary.Release(b.Image); b.Image=image; b.File=file; b.Attempt=image.Key; b.Dirty=true;
        b.Pose=ReadPose(b.Component); Remember(b); RefreshVisual(b);
    }
    internal static void SetMirror(bool mirror)
    {
        var b=Selected;if(!Designer||b==null||b.Mirror==mirror)return;
        b.Mirror=mirror;b.Dirty=true; if(b.File.Length>0)Remember(b); RefreshVisual(b);
    }
    internal static void RefreshImages()
    {
        ImageLibrary.Refresh(); foreach(var b in boards.Values){ b.Attempt="";b.Dirty=true; }
    }
    private static void RefreshVisual(Board b)
    {
        var c=b.Component;var model=c.Model;
        // Native mesh replacement (including all LODs) is reapplied after a model rebuild.
        if(b.Root==null || b.ModelPointer!=model.Pointer || model.InteractionMesh!=BoardMeshes.Combined)
        {
            ReleaseVisual(b);
            Material? candidate=model.Material?.Material;
            b.Material=BoardMeshes.Material(candidate,"Reference image picture");
            b.FrameMaterial=BoardMeshes.Material(candidate,"Reference image frame"); b.FrameMaterial.SetColor("_BaseColor",new Color(.12f,.13f,.12f,1));
            c.MarkDynamic();
            var lods=Enumerable.Repeat(BoardMeshes.Combined,Math.Max(1,model.LODCount)).ToArray();
            model.SetMesh(new Il2CppReferenceArray<Mesh>(lods));
            model.SetShadowProxy(BoardMeshes.Combined);model.SetInteractionMesh(BoardMeshes.Combined);model.SetCollisionMesh(BoardMeshes.Combined);
            model.SetMaterial(new VehicleMaterial(b.FrameMaterial));
            // The reference setters alone do not update an existing collider.
            // Native ForceRebuild -> BuildInternal -> Build calls both collider
            // SetMesh(vertices, triangles) and SetTransform with the model pose.
            c.ForceRebuild();
            b.Root=new GameObject("Reference Images content") { layer=c.Layer };b.Root.transform.SetParent(model.transform,false);
            b.ContentTransform=b.Root.transform;b.ModelTransform=model.transform;
            b.Mesh=BoardMeshes.Image(b.Mirror);
            b.Slab=BoardMeshes.Render(b.Root.transform,"Reference Images picture",b.Mesh,b.Material,c.Layer);
            b.PictureTransform=b.Slab.transform;
            b.LastScale=b.LastInherited=b.ReportScale=new Vector3(float.NaN,float.NaN,float.NaN);
            b.ModelPointer=model.Pointer;b.Dirty=true;
        }
        string signature=b.File.Length==0 ? "NO IMAGE" : ImageLibrary.Signature(b.File) ?? "MISSING:"+b.File;
        if(b.Attempt!=signature)
        {
            b.Attempt=signature; ImageLibrary.Release(b.Image);b.Image=null;
            if(b.File.Length>0)
            {
                try { b.Image=ImageLibrary.Acquire(b.File); }
                catch(Exception ex){Plugin.WarnOnce("image error: "+b.File+": "+ex.Message);}
            }
            b.Dirty=true;
        }
        if(b.Dirty)
        {
            var texture=b.Image?.Texture ?? ImageLibrary.Placeholder;
            b.Aspect=(float)texture.width/texture.height;
            b.Material!.SetTexture("_BaseColorMap",texture);
            // UV is per-board; both wide faces use identical projection (rear reads mirrored).
            if(b.Dirty)
            {
                var mesh=BoardMeshes.Image(b.Mirror); b.Slab!.GetComponent<MeshFilter>().sharedMesh=mesh;
                if(b.Mesh!=null)UnityEngine.Object.Destroy(b.Mesh);b.Mesh=mesh;
            }
            b.LastScale=new Vector3(float.NaN,float.NaN,float.NaN);b.Dirty=false;
        }
        UpdateBoard(b);
        var scale=b.LastScale;
        if(!Same(b.ReportScale,scale))
        {
            var fit=BoardPolicy.Fit(scale.z,scale.y,b.Aspect);
            float exact=fit.ExactHeight;
            b.SizeText=FormattableString.Invariant($"Displayed: {fit.Width:0.###} x {fit.Height:0.###} m\nBoard: {2*Math.Abs(scale.z):0.###} x {2*Math.Abs(scale.y):0.###} m\nHeight scale for exact fit: {exact:0.###}")+(exact<.1f||exact>6?" (outside native limits)":"");
            b.ReportScale=scale;
        }
    }
    internal static void UpdateKnown()
    {
        if(!Designer)return;
        foreach(var b in boards.Values)
            if(b.Component!=null && b.Component.HasModel && b.ContentTransform!=null && b.PictureTransform!=null && b.ModelTransform!=null)UpdateBoard(b);
    }
    private static bool Same(Vector3 a,Vector3 b)=>a.x==b.x && a.y==b.y && a.z==b.z;
    private static void UpdateBoard(Board b)
    {
        // GetModelScale reads VehicleTransform.Scale when AutoApplyScale is true;
        // ModelScale is merely requestedModelScale. No native scale is written.
        var scale=b.Component.GetModelScale();var inherited=b.ModelTransform!.lossyScale;
        if(Same(scale,b.LastScale) && Same(inherited,b.LastInherited))return;
        if(Math.Abs(scale.x)<1e-7f || Math.Abs(scale.y)<1e-7f || Math.Abs(scale.z)<1e-7f)return;
        var fit=BoardPolicy.Fit(scale.z,scale.y,b.Aspect);
        b.ContentTransform!.localScale=new Vector3(Div(scale.x,inherited.x),Div(scale.y,inherited.y),Div(scale.z,inherited.z));
        // Two quads only: the native solid board supplies letterboxing 3 mm
        // behind each image in world units, at every native depth scale.
        b.PictureTransform!.localScale=new Vector3(BoardPolicy.PictureDepth(scale.x),fit.Y,fit.X);
        b.LastScale=scale;b.LastInherited=inherited;
    }
    private static float Div(float a,float b) => Math.Abs(b)<1e-7f ? a : a/b;
    private static void ReleaseVisual(Board b)
    {
        if(b.Root!=null)UnityEngine.Object.Destroy(b.Root);b.Root=null;b.Slab=null;
        b.ContentTransform=b.PictureTransform=b.ModelTransform=null;
        if(b.Mesh!=null)UnityEngine.Object.Destroy(b.Mesh);b.Mesh=null;
        foreach(var m in new[]{b.Material,b.FrameMaterial})if(m!=null)UnityEngine.Object.Destroy(m);
        b.Material=b.FrameMaterial=null;
    }
    private static void Destroy(Board b){ReleaseVisual(b);ImageLibrary.Release(b.Image);b.Image=null;}
    internal static void Clear(){foreach(var b in boards.Values)Destroy(b);boards.Clear();Selected=null;}
}
