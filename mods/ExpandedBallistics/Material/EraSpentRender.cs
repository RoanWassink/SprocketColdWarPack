using Sprocket.Vehicles;
using SprocketExpandedBallistics;
using UnityEngine;
using Object=UnityEngine.Object;

namespace SprocketExpandedBallistics.Materials;

// No hooks, model visibility calls or geometry/collider writes. The sources remain
// immutable and native-owned. Only an exact renderer's mesh binding is replaced.
internal static class EraSpentRender
{
    private sealed record Source(Mesh Mesh,int Instance,int[]? Cells);
    private sealed class View
    {
        internal readonly VehicleRenderer Slot;
        internal readonly Mesh Source,Copy;
        internal readonly int Instance;
        internal View(VehicleRenderer slot,Mesh source,Mesh copy)
        {Slot=slot;Source=source;Copy=copy;Instance=slot.Renderer.GetInstanceID();}
        internal Mesh? Current=>Slot.TryCast<VehicleMeshRenderer>() is {} m?m.Filter?.sharedMesh:Slot.TryCast<VehicleSkinnedMeshRenderer>()?.SkinnedRenderer?.sharedMesh;
        internal void Bind(Mesh mesh)
        {
            if(Slot.TryCast<VehicleMeshRenderer>() is {} m)m.Filter.sharedMesh=mesh;
            else if(Slot.TryCast<VehicleSkinnedMeshRenderer>() is {} s)s.SkinnedRenderer.sharedMesh=mesh;
            else throw new InvalidOperationException("Unsupported individual ERA renderer.");
        }
        internal bool OwnCopy=>Slot.Renderer!=null&&Slot.Renderer.GetInstanceID()==Instance&&Current==Copy;
        internal void Restore()
        {
            if(OwnCopy)Bind(Source);
            Object.Destroy(Copy);
        }
    }
    private sealed class Owner
    {
        internal readonly VehicleObjectModel Model;
        internal readonly List<View> Views=new();
        internal long Version=-1;
        internal IntPtr Wrapper;
        internal int Attempts;
        internal Owner(VehicleObjectModel model){Model=model;}
    }
    // 0.22.70: a housing shows its existing damaged form after an own unit fires. Same route as the cell cut:
    // only the mesh binding of its exact renderers changes, to a private copy owned and destroyed here.
    private sealed record HousingSource(Mesh Mesh,int Instance,string Component);
    private sealed class HousingOwner
    {
        internal readonly VehicleObjectModel Model;
        internal readonly HousingLookBook<VehicleRenderer,Mesh> Book=new();
        internal IntPtr Wrapper;
        internal int Attempts;
        internal bool Activated;
        internal HousingOwner(VehicleObjectModel model){Model=model;}
    }
    private sealed class UnityHousingHost:IHousingLookHost<VehicleRenderer,Mesh>
    {
        internal static readonly UnityHousingHost Shared=new();
        public Mesh? Current(VehicleRenderer slot)=>slot.TryCast<VehicleMeshRenderer>() is {} m?m.Filter?.sharedMesh:slot.TryCast<VehicleSkinnedMeshRenderer>()?.SkinnedRenderer?.sharedMesh;
        public bool Same(Mesh? a,Mesh b)=>a!=null&&a==b;
        public int Instance(VehicleRenderer slot)=>slot.Renderer.GetInstanceID();
        public bool Alive(VehicleRenderer slot)=>slot.Renderer!=null;
        public void Bind(VehicleRenderer slot,Mesh mesh)
        {
            if(slot.TryCast<VehicleMeshRenderer>() is {} m)m.Filter.sharedMesh=mesh;
            else if(slot.TryCast<VehicleSkinnedMeshRenderer>() is {} s)s.SkinnedRenderer.sharedMesh=mesh;
            else throw new InvalidOperationException("Unsupported individual ERA housing renderer.");
        }
        public Mesh Create(string asset)=>HousingLookMesh(asset);
        public void Destroy(Mesh mesh){if(mesh!=null)Object.Destroy(mesh);}
        public int Submeshes(Mesh mesh)=>mesh.subMeshCount;
    }
    private static readonly Dictionary<IntPtr,Source> Sources=new();
    private static readonly Dictionary<IntPtr,Owner> Owners=new();
    private static readonly Dictionary<IntPtr,HousingSource> HousingSources=new();
    private static readonly Dictionary<IntPtr,HousingOwner> Housings=new();
    // Managed vertex data of the existing state OBJ files, read once at load/build time (never during combat).
    private static readonly Dictionary<string,Vector3[]> HousingLooks=new(StringComparer.Ordinal);
    private static readonly HashSet<IntPtr> Pending=new();
    private static readonly HashSet<string> Warnings=new();
    private static int mainThread,lastFrame=-1,logs,housingLogs;
    private static bool started;
    private static bool LiveImpact=>RuntimeSpall.Impact?.LiveImpact==true;
    internal static void Start(){if(started)return;started=true;mainThread=Environment.CurrentManagedThreadId;PlacedEra.Activated+=Activated;}
    internal static void Stop()
    {
        PlacedEra.Activated-=Activated;started=false;
        foreach(var o in Owners.Values)Restore(o);
        foreach(var h in Housings.Values)h.Book.Restore(UnityHousingHost.Shared);
        Owners.Clear();Housings.Clear();Pending.Clear();Sources.Clear();HousingSources.Clear();HousingLooks.Clear();Warnings.Clear();lastFrame=-1;
    }
    internal static void Register(Mesh mesh,int[]? cells=null)=>Sources[mesh.Pointer]=new(mesh,mesh.GetInstanceID(),cells);
    // The intact (state 0) housing render mesh built by ReliktPrototype.Geometry; it stays native/cache owned.
    internal static void RegisterHousing(Mesh mesh,string component)=>HousingSources[mesh.Pointer]=new(mesh,mesh.GetInstanceID(),component);
    internal static void ForgetMesh(Mesh mesh){Sources.Remove(mesh.Pointer);HousingSources.Remove(mesh.Pointer);}
    private static bool Own(VehicleObjectModel m)=>m!=null&&EraPresetRegistry.Visual(m.ComponentID,out _,out bool cassette)&&cassette;
    private static bool OwnHousing(VehicleObjectModel m,out EraVisualPreset preset)
    {
        preset=null!;
        return m!=null&&EraPresetRegistry.Visual(m.ComponentID,out preset,out bool cassette)&&!cassette&&!preset.WholeCells
            &&preset.HousingActiveIds is {Length:>0} ids&&preset.HousingDamageVisuals is {} visuals&&visuals.Length==ids.Length+1;
    }
    private static void TrackHousing(VehicleObjectModel model)
    {
        if(!OwnHousing(model,out var preset))return;
        // Read the damaged forms now, inside the existing load/build callback, so activation never touches disk.
        var visuals=preset.HousingDamageVisuals!;
        try{for(int i=1;i<visuals.Length;i++)if(!HousingLooks.ContainsKey(visuals[i]))HousingLooks[visuals[i]]=ReadHousingLook(visuals[i]);}
        catch(Exception ex){WarnHousing($"housing look {model.ComponentID} unavailable ({ex.Message}); intact look retained.");return;}
        if(!Housings.TryGetValue(model.Pointer,out var owner))
        {
            if(Housings.Count>=4096){WarnHousing("Housing owner limit; intact look retained.");return;}
            Housings[model.Pointer]=owner=new(model);
        }
        owner.Attempts=0;Pending.Add(model.Pointer);
    }
    private static Vector3[] ReadHousingLook(string asset)
    {
        var path=Path.Combine(Path.GetDirectoryName(typeof(MaterialFeature).Assembly.Location)!,"era-assets",asset);
        return EraObjGeometry.Read(File.ReadAllLines(path),"mount",(x,y,z)=>new Vector3(x,y,z),v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)).ToArray();
    }
    // Built exactly like the intact housing render mesh in ReliktPrototype.Geometry (expanded hard-edged
    // triangles, one submesh, planar UV/tangent frame for VehicleLit). A private copy per renderer.
    private static Mesh HousingLookMesh(string asset)
    {
        if(!HousingLooks.TryGetValue(asset,out var expanded))throw new InvalidOperationException("Housing look "+asset+" was not prepared at load.");
        var mesh=new Mesh{name="ERA housing spent look "+asset,hideFlags=HideFlags.HideAndDontSave};
        mesh.vertices=expanded;mesh.triangles=Enumerable.Range(0,expanded.Length).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
        var normals=mesh.normals;
        var uv=new Vector2[expanded.Length];var tangents=new Vector4[expanded.Length];
        for(int i=0;i<expanded.Length;i+=3)
        {
            var normal=normals[i];
            var axis=Math.Abs(normal.y)<.9f ? Vector3.up : Vector3.forward;
            var tangent=Vector3.Cross(axis,normal).normalized;
            var bitangent=Vector3.Cross(normal,tangent);
            for(int j=i;j<i+3;j++)
            {
                uv[j]=new Vector2(Vector3.Dot(expanded[j],tangent),Vector3.Dot(expanded[j],bitangent));
                tangents[j]=new Vector4(tangent.x,tangent.y,tangent.z,1f);
            }
        }
        mesh.uv=uv;mesh.tangents=tangents;
        return mesh;
    }
    internal static void Track(VehicleObjectModel model)
    {
        if(!Own(model)){TrackHousing(model);return;}
        if(!Owners.TryGetValue(model.Pointer,out var owner))
        {
            if(Owners.Count>=4096){Warn("Owner limit; ERA visual retained.");return;}
            Owners[model.Pointer]=owner=new(model);
        }
        owner.Attempts=0;Pending.Add(model.Pointer);
    }
    // Called before an existing construction callback swaps/releases native meshes.
    internal static void BeforeGeometry(VehicleObjectModel model)
    {
        if(LiveImpact)return;
        if(Owners.TryGetValue(model.Pointer,out var owner)){Restore(owner);owner.Version=-1;}
        if(Housings.TryGetValue(model.Pointer,out var housing)){housing.Book.Restore(UnityHousingHost.Shared);housing.Wrapper=IntPtr.Zero;}
    }
    internal static void Forget(VehicleObjectModel model)
    {
        if(Owners.Remove(model.Pointer,out var owner))Restore(owner);
        if(Housings.Remove(model.Pointer,out var housing))housing.Book.Restore(UnityHousingHost.Shared);
        Pending.Remove(model.Pointer);
    }
    private static void Activated(PlacedEraActivation a)
    {
        // Notification only queues an exact, already registered live owner.
        foreach(var entry in Owners)
        {
            var m=entry.Value.Model;
            if(m!=null&&m.VUID.Value==a.ComponentVuid&&m.VehicleRoot?.Pointer==a.VehicleRootPointer&&m.Vehicle?.Pointer==a.VehiclePointer)
            {entry.Value.Attempts=0;Pending.Add(entry.Key);}
        }
        // Housings of the same exact vehicle/root are queued; the drain recounts their own units from the ledger.
        foreach(var entry in Housings)
        {
            var m=entry.Value.Model;
            if(m!=null&&m.VehicleRoot?.Pointer==a.VehicleRootPointer&&m.Vehicle?.Pointer==a.VehiclePointer)
            {entry.Value.Attempts=0;entry.Value.Activated=true;Pending.Add(entry.Key);}
        }
    }
    internal static void ResetForEdit()
    {
        foreach(var entry in Owners){entry.Value.Version=-1;entry.Value.Attempts=0;Pending.Add(entry.Key);}
        foreach(var entry in Housings){entry.Value.Wrapper=IntPtr.Zero;entry.Value.Attempts=0;Pending.Add(entry.Key);}
        if(!LiveImpact)
        {
            foreach(var o in Owners.Values)Restore(o);
            foreach(var h in Housings.Values)h.Book.Restore(UnityHousingHost.Shared);
        }
    }
    internal static void Tick()
    {
        if(LiveImpact||Environment.CurrentManagedThreadId!=mainThread||Time.frameCount==lastFrame)return;
        lastFrame=Time.frameCount;
        foreach(var key in Pending.ToArray())
        {
            if(Housings.TryGetValue(key,out var housing))
            {
                try{if(ReconcileHousing(housing)){Pending.Remove(key);continue;}}
                catch(Exception ex){WarnHousing($"vuid={housing.Model?.VUID.Value}: {ex.Message}");}
                if(++housing.Attempts>=180){Pending.Remove(key);WarnHousing($"vuid={housing.Model?.VUID.Value}: renderer not ready after 180 frames; intact look retained.");}
                continue;
            }
            if(!Owners.TryGetValue(key,out var owner)){Pending.Remove(key);continue;}
            try
            {
                if(Reconcile(owner)){Pending.Remove(key);continue;}
            }
            catch(Exception ex){Warn(ex.Message);}
            if(++owner.Attempts>=180){Pending.Remove(key);Warn("ERA renderer not ready after 180 frames; visible fallback retained.");}
        }
    }
    // Exact own-unit membership, as the 0.22.50 count rule: one sibling of the same object and root per id.
    private static HousingMember Member(VehicleObjectModel housing,IntPtr root,string id)
    {
        var owner=housing.VehicleObject;
        var components=owner?.Components;
        if(owner==null||components==null)return HousingMember.Missing;
        int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        VehicleObjectModel? match=null;
        for(int i=0;i<count;i++)
        {
            if(components[i].TryCast<VehicleObjectModel>() is not {} sibling||sibling.ComponentID!=id)continue;
            if(sibling.VehicleObject?.Pointer!=owner.Pointer||sibling.VehicleRoot?.Pointer!=root)return HousingMember.Foreign;
            if(match!=null)return HousingMember.Duplicate;
            match=sibling;
        }
        if(match==null)return HousingMember.Missing;
        return PlacedEra.IsSpent(root,match.VUID.Value)?HousingMember.Spent:HousingMember.Intact;
    }
    private static bool ReconcileHousing(HousingOwner owner)
    {
        var host=UnityHousingHost.Shared;
        var m=owner.Model;
        if(!OwnHousing(m,out var preset)||m.VehicleRoot is not {} root||m.Model is not {} native)return false;
        var visuals=preset.HousingDamageVisuals!;
        int state=HousingSpentPolicy.State(preset.HousingActiveIds,visuals,id=>Member(m,root.Pointer,id));
        bool activated=owner.Activated;owner.Activated=false;
        if(state==HousingSpentPolicy.FailClosed)
        {
            if(activated)WarnHousing($"housing {m.ComponentID} vuid={m.VUID.Value}: an own unit is missing, duplicate or foreign; current look retained.");
            return true;
        }
        if(state==owner.Book.State&&owner.Wrapper==native.Pointer&&owner.Book.AllOwned(host))return true;
        owner.Book.Restore(host);owner.Wrapper=native.Pointer;
        if(state==0)return true;
        if(native.TryCast<VehicleRendererGroup>() is not {} group||group.id.Value!=m.VUID.Value){owner.Wrapper=IntPtr.Zero;return false;}
        var slots=new List<VehicleRenderer>();
        if(group.lods is {} lods)foreach(var slot in lods)if(slot!=null)slots.Add(slot);
        if(group.shadow!=null)slots.Add(group.shadow);
        slots=slots.GroupBy(s=>s.Pointer).Select(g=>g.First()).ToList();
        // Only the renderers of this housing that show its registered intact mesh; any other binding stays.
        var candidates=new List<(VehicleRenderer Slot,Mesh Source)>();
        foreach(var slot in slots)
        {
            var current=host.Current(slot);
            bool intact=current!=null&&HousingSources.TryGetValue(current.Pointer,out var source)&&source.Mesh==current
                &&source.Instance==current.GetInstanceID()&&source.Component==m.ComponentID;
            if(!intact)continue;
            if(!HousingSpentPolicy.Candidate(m.VUID.Value,group.id.Value,slot.ID.Value,true))
                throw new InvalidOperationException("ERA housing renderer ownership mismatch; no render binding changed.");
            candidates.Add((slot,current!));
        }
        if(candidates.Count==0){owner.Wrapper=IntPtr.Zero;return false;}
        int materials=native.Materials?.Length??0;
        var refused=owner.Book.Apply(host,candidates,state,visuals[state],materials);
        if(refused!=null){WarnHousing($"housing {m.ComponentID} vuid={m.VUID.Value}: {refused}; current look retained.");return true;}
        if(housingLogs++<48&&ReleaseDiagnostics.Enabled)
            MaterialFeature.ModLog.LogInfo($"[ERA housing look] root={root.Pointer} vuid={m.VUID.Value} component={m.ComponentID} state={state}/{visuals.Length-1} asset={visuals[state]} renderers={candidates.Count} trianglesBefore={candidates.Sum(c=>c.Source.triangles.Length/3)} trianglesAfter={owner.Book.Copies.Sum(c=>c.triangles.Length/3)}; collision/interaction/mass unchanged");
        return true;
    }
    private static bool Reconcile(Owner owner)
    {
        var m=owner.Model;
        if(!Own(m)||m.VehicleRoot is not {} root||m.Model is not {} native)return false;
        var version=PlacedEra.SpentVersion(root.Pointer,m.VUID.Value);
        if(owner.Version==version&&owner.Wrapper==native.Pointer&&owner.Views.All(v=>v.OwnCopy))return true;
        Restore(owner);
        var spentCells=PlacedEra.SpentCells(root.Pointer,m.VUID.Value);
        if(spentCells.Length==0){owner.Version=version;owner.Wrapper=native.Pointer;return true;}
        if(native.TryCast<VehicleRendererGroup>() is not {} group||group.id.Value!=m.VUID.Value)return false;
        EraPresetRegistry.Visual(m.ComponentID,out var preset,out _);
        var partitioned=PlacedEraCells.Partitioned(preset);
        EraCellLayout layout=default;
        if(partitioned&&!PlacedEraCells.TryLayout(m,preset,out layout))return false;
        var spent=new HashSet<int>();
        foreach(var c in spentCells)
            if(c.Surface==0&&(!partitioned||c.X>=0&&c.X<layout.CountU&&c.Y>=0&&c.Y<layout.CountV))
                spent.Add(partitioned?c.X+c.Y*layout.CountU:0);
        if(spent.Count==0){owner.Version=version;owner.Wrapper=native.Pointer;return true;}
        var slots=new List<VehicleRenderer>();
        if(group.lods is {} lods)foreach(var slot in lods)if(slot!=null)slots.Add(slot);
        if(group.shadow!=null)slots.Add(group.shadow);
        slots=slots.GroupBy(s=>s.Pointer).Select(g=>g.First()).ToList();
        if(slots.Count==0)return false;
        // Validate and construct every LOD and shadow before changing any binding.
        var prepared=new List<View>();
        try
        {
            foreach(var slot in slots)
            {
                var source=slot.TryCast<VehicleMeshRenderer>() is {} sm?sm.Filter?.sharedMesh:slot.TryCast<VehicleSkinnedMeshRenderer>()?.SkinnedRenderer?.sharedMesh;
                bool known=source!=null&&Sources.TryGetValue(source.Pointer,out var registered)&&registered.Mesh==source&&registered.Instance==source.GetInstanceID();
                if(!SpentCellRenderPolicy.ExactOwner(m.VUID.Value,group.id.Value,slot.ID.Value,known))
                    throw new InvalidOperationException("ERA renderer ownership/source mismatch; no render binding changed.");
                var indices=source!.triangles.ToArray();
                var cells=Sources[source.Pointer].Cells;
                if(cells==null)
                {
                    cells=new int[indices.Length/3];
                    if(partitioned)
                    {
                        var vs=source.vertices;var scale=m.GetModelScale();
                        for(int t=0;t<cells.Length;t++)
                            cells[t]=PlacedEraCells.TriangleCell(layout,vs[indices[3*t]],vs[indices[3*t+1]],vs[indices[3*t+2]],scale,preset.SurfaceAxes,out int x,out int y)?x+y*layout.CountU:-1;
                    }
                }
                if(cells.Length!=indices.Length/3)throw new InvalidDataException("ERA source cell map does not match its triangles.");
                var copy=Object.Instantiate(source).Cast<Mesh>();
                var view=new View(slot,source,copy);prepared.Add(view);
                // Preserve vertices, skinning, normals, materials, bounds and submesh slots.
                int offset=0;
                for(int sub=0;sub<source.subMeshCount;sub++)
                {
                    var subIndices=source.GetTriangles(sub).ToArray();
                    var subCells=cells.Skip(offset).Take(subIndices.Length/3).ToArray();offset+=subCells.Length;
                    copy.SetTriangles(SpentCellRenderPolicy.Filter(subIndices,subCells,spent),sub,false);
                }
            }
            owner.Views.AddRange(prepared);
            foreach(var view in prepared)view.Bind(view.Copy);
            owner.Version=version;owner.Wrapper=native.Pointer;
            if(logs++<48&&ReleaseDiagnostics.Enabled)
                MaterialFeature.ModLog.LogInfo($"[ERA render cut] root={root.Pointer} vuid={m.VUID.Value} cells={string.Join(",",spent.OrderBy(x=>x))} renderers={prepared.Count} trianglesBefore={prepared.Sum(v=>v.Source.triangles.Length/3)} trianglesAfter={prepared.Sum(v=>v.Copy.triangles.Length/3)}; collision/interaction unchanged");
            return true;
        }
        catch
        {
            foreach(var view in prepared)view.Restore();owner.Views.Clear();throw;
        }
    }
    private static void Restore(Owner owner)
    {
        foreach(var view in owner.Views)view.Restore();owner.Views.Clear();
    }
    private static void WarnHousing(string message){if(Warnings.Count<16&&Warnings.Add(message))MaterialFeature.ModLog.LogWarning("[ERA housing look] "+message);}
    private static void Warn(string message){if(Warnings.Count<16&&Warnings.Add(message))MaterialFeature.ModLog.LogWarning("[ERA render cut] "+message);}
}
