using System.Security.Cryptography;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PartImporting;
using Sprocket.Vehicles.CrewSystems;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace SprocketCupolas;

public sealed class CupolaRuntime : MonoBehaviour
{
    public CupolaRuntime(IntPtr pointer):base(pointer) { }
    private sealed class Entry
    {
        internal VehicleObject Root=null!;
        internal VehicleObjectModel Base=null!, Door=null!;
        internal Hatch Hatch=null!;
        internal long BaseWrapper, DoorWrapper;
        internal int Generation;
        internal string? State;
        internal Snapshot BaseOriginal=null!,DoorOriginal=null!;
    }
    private sealed record Snapshot(StaticVehicleRendererGroup Group,Mesh[] Lods,Mesh Shadow,Mesh Interaction)
    {
        internal static Snapshot Capture(StaticVehicleRendererGroup group)=>new(group,group.LODs.Select(r=>r.SharedMesh).ToArray(),group.shadow.SharedMesh,group.InteractionMesh);
        internal void Restore(Mesh own,bool force=false)
        {
            // Never resurrect a released renderer or overwrite a foreign replacement.
            if(Group==null||Group.markedForDestroy||(!force&&Group.InteractionMesh!=own))return;
            Group.SetMesh(new Il2CppReferenceArray<Mesh>(Lods));Group.SetShadowProxy(Shadow);Group.SetInteractionMesh(Interaction);
        }
    }
    private readonly CupolaCache<Entry> cache=new();
    private readonly HashSet<string> rejected=new();
    private Mesh? baseMesh,doorMesh;
    private bool ready,disposed,definitionReady,failed;
    private int scene=int.MinValue,generation,logs,lastCandidates=-1,scanLogs;
    private float scanAt;
    private string gameHash="unavailable";
    public void Start()
    {
        try {
            if(!NativeGuard.Prepare())return;
            gameHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(BepInEx.Paths.GameRootPath,"GameAssembly.dll"))));
            // Validate both before allocating either native mesh.
            MeshGeometry.Create(CupolaMeshData.Base); MeshGeometry.Create(CupolaMeshData.Door);
            baseMesh=CupolaMesh.Create(CupolaMeshData.Base);doorMesh=CupolaMesh.Create(CupolaMeshData.Door);
            ready=true;
        } catch(Exception ex) { Fail(ex); }
    }
    [HideFromIl2Cpp] private void Reject(long pointer,string reason)
    {
        if(rejected.Count<20 && rejected.Add(pointer+":"+reason))Plugin.Diag(()=>"REJECT root="+pointer+" reason="+reason);
    }
    [HideFromIl2Cpp] private static int Count<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> list) where T:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        =>list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;
    [HideFromIl2Cpp] private static List<VehicleComponent> Components(VehicleObject obj)
    {
        var list=obj.Components; if(list==null)throw new InvalidDataException("components not ready");
        int n=Count(list); if(n>CupolaPolicy.MaxComponents)throw new InvalidDataException("component budget");
        var result=new List<VehicleComponent>(n); for(int i=0;i<n;i++)if(list[i]!=null)result.Add(list[i]);return result;
    }
    [HideFromIl2Cpp] private string? Eligibility(VehicleObject root)
    {
        if(root==null||root.Released)return "released";
        var vehicle=root.Vehicle;
        var chain=new List<CupolaPolicy.Node>(); var node=root.VehicleTransform; bool terminated=false;
        var seen=new HashSet<long>();
        for(int i=0;i<CupolaPolicy.MaxWalk&&node!=null;i++) {
            if(!seen.Add(node.Pointer.ToInt64()))return "cyclic-ancestry";
            var part=node.VehicleObject;
            if(part!=null && part.Vehicle==null)return "foreign-ancestor";
            chain.Add(new(node.Pointer.ToInt64(),part?.Vehicle?.Pointer.ToInt64()??0,part?.Released??false,
                part!=null&&(part.Flags&(VehicleObjectFlags.ActivePlacement|VehicleObjectFlags.ActivePlacementChild))!=0));
            node=node.Parent; if(node==null)terminated=true;
        }
        var flags=root.Flags;
        return CupolaPolicy.Eligible(root.GUID,vehicle?.Pointer.ToInt64()??0,(flags&VehicleObjectFlags.Installed)!=0,
            (flags&(VehicleObjectFlags.DontSaveWithVehicle|VehicleObjectFlags.NeverSave))!=0,(flags&VehicleObjectFlags.Dynamic)!=0,chain,terminated);
    }
    // Only the original two-object stock topology is accepted. Extra attached
    // objects/render models make atlas safety unverifiable and keep stock meshes.
    [HideFromIl2Cpp] private bool Resolve(VehicleObject root,out VehicleObjectModel basis,out VehicleObjectModel door,out Hatch hatch)
    {
        basis=null!;door=null!;hatch=null!;
        var own=Components(root);
        if(own.Count!=3)return false;
        var commander=own.Where(c=>c.ComponentID=="cupola"&&c.TryCast<CommanderCupola>()!=null).ToArray();
        var hatches=own.Where(c=>c.ComponentID=="hatch").Select(c=>c.TryCast<Hatch>()).Where(c=>c!=null).ToArray();
        if(commander.Length!=1||hatches.Length!=1)return false;
        hatch=hatches[0]!;
        var list=new List<VehicleObjectModel>();var stack=new Stack<VehicleTransform>();var seen=new HashSet<long>();
        stack.Push(root.VehicleTransform);
        while(stack.Count>0) {
            var vt=stack.Pop();if(vt==null||seen.Count>=CupolaPolicy.MaxWalk||!seen.Add(vt.Pointer.ToInt64()))return false;
            var obj=vt.VehicleObject;
            if(obj!=null) {
                if(obj.Released||obj.Vehicle?.Pointer!=root.Vehicle?.Pointer)return false;
                if((obj.Flags&(VehicleObjectFlags.ActivePlacement|VehicleObjectFlags.ActivePlacementChild|VehicleObjectFlags.DontSaveWithVehicle|VehicleObjectFlags.NeverSave))!=0)return false;
                var cs=Components(obj);
                if(obj.Pointer!=root.Pointer&&(cs.Count!=1||cs[0].ComponentID!="hatchModel"))return false;
                foreach(var c in cs)if(c.TryCast<VehicleObjectModel>() is {} m)list.Add(m);
            }
            var children=vt.Children;
            if(children!=null) {int n=Count(children);if(n>CupolaPolicy.MaxWalk)return false;for(int i=0;i<n;i++)stack.Push(children[i]);}
        }
        var models=list.Select(m=>new CupolaPolicy.Model(m.Pointer.ToInt64(),m.ComponentID,m.VehicleObject.Pointer.ToInt64(),root.Pointer.ToInt64(),m.associatedComponent?.Pointer.ToInt64()??0)).ToArray();
        if(!CupolaPolicy.Resolve(root.Pointer.ToInt64(),commander[0].Pointer.ToInt64(),hatch.DoorModel?.Pointer.ToInt64()??0,models,out var b,out var d))return false;
        basis=list.Single(m=>m.Pointer.ToInt64()==b.Pointer);door=list.Single(m=>m.Pointer.ToInt64()==d.Pointer);
        return basis.Vehicle?.Pointer==root.Vehicle?.Pointer&&door.Vehicle?.Pointer==root.Vehicle?.Pointer;
    }
    [HideFromIl2Cpp] private bool Definition(Vehicle vehicle)
    {
        if(definitionReady)return true;
        var source=vehicle.PartInfoSource?.TryCast<PartInfoSource>();
        var definition=source?.partDatabase?.Get(CupolaPolicy.PartGuid);
        if(definition?.name!="t90aCupola"||definition.transform==null){Reject(0,"definition-not-ready-or-missing");return false;}
        var date=definition.creationDate;
        if(date.Year!=1992||date.Month!=1||date.Day!=1||definition.tags==null||!definition.tags.SequenceEqual(new[]{"cupola","commander","ussr"})) {Reject(0,"definition-contract-mismatch");return false;}
        // JSON is validated offline; database readback establishes actual discovery.
        definitionReady=true;
        Plugin.Diag(()=>"READY gameHash="+gameHash+" partGuid="+definition.guid+" definitionFound=true date=1992.01.01 rootSchema=0.3");
        return true;
    }
    [HideFromIl2Cpp] private static StaticVehicleRendererGroup? Group(VehicleObjectModel model)=>model.Model?.TryCast<StaticVehicleRendererGroup>();
    [HideFromIl2Cpp] private static bool ReadyGroup(StaticVehicleRendererGroup? group)
    {
        if(group==null||group.FullyBaked||group.LODCount<1||group.LODCount>8||group.LODs==null||group.LODs.Length!=group.LODCount||group.Materials==null||group.Materials.Length!=1||group.Materials[0]?.Material==null||group.CollisionMesh==null||group.shadow?.SharedMesh==null||group.InteractionMesh==null)return false;
        foreach(var lod in group.LODs)if(lod==null||lod.SharedMesh==null||lod.Materials==null||lod.Materials.Length!=1||lod.Materials[0]?.Material==null)return false;
        return true;
    }
    [HideFromIl2Cpp] private static bool AtlasSafe(VehicleObject root,StaticVehicleRendererGroup a,StaticVehicleRendererGroup b)
    {
        var renderers=new HashSet<long>();var meshes=new HashSet<long>();
        foreach(var g in new[]{a,b})foreach(var r in g.LODs){renderers.Add(r.Pointer.ToInt64());meshes.Add(r.SharedMesh.Pointer.ToInt64());}
        var stages=root.Vehicle?.TryCast<Vehicle>()?.behaviourAssemblyStages;
        if(stages==null||stages.Length==0||stages.Length>128)return false;
        int examined=0,mergers=0;
        foreach(var stage in stages) {
            var merger=stage?.TryCast<RendererMergerAssemblyStage>();if(merger==null)continue;mergers++;
            var users=merger.atlaser?.users;if(users==null)return false;
            if(users.Count>CupolaPolicy.MaxObjects)return false;
            for(int i=0;i<users.Count;i++) {
                if(++examined>CupolaPolicy.MaxObjects)return false;
                var u=users[i];if(u==null)return false;
                if(u.Renderer!=null&&renderers.Contains(u.Renderer.Pointer.ToInt64()))return false;
                // Mesh-only overlap is conservative; unrelated sharing also falls back.
                if(u.Mesh!=null&&meshes.Contains(u.Mesh.Pointer.ToInt64()))return false;
            }
        }
        return mergers>0;
    }
    [HideFromIl2Cpp] private static bool Matches(StaticVehicleRendererGroup g,Mesh mesh,int count)
        =>g.LODCount==count&&g.LODs.Length==count&&g.LODs.All(r=>r!=null&&r.SharedMesh==mesh)&&g.shadow?.SharedMesh==mesh&&g.InteractionMesh==mesh;
    [HideFromIl2Cpp] private void Swap(Entry e,VehicleObjectModel owner,StaticVehicleRendererGroup g,Mesh mesh,CupolaData data,int lodCount)
    {
        if(Matches(g,mesh,lodCount)&&(g.Flags&VehicleRendererFlags.Static)==0)return;
        var collision=g.CollisionMesh; var materials=g.Materials.Select(m=>m.Pointer).ToArray();
        owner.MarkDynamic();
        g.SetMesh(new Il2CppReferenceArray<Mesh>(Enumerable.Repeat(mesh,lodCount).ToArray()));
        g.SetShadowProxy(mesh);g.SetInteractionMesh(mesh);
        bool renderMatch=g.LODCount==lodCount&&g.LODs.Length==lodCount&&g.LODs.All(r=>r.SharedMesh==mesh);
        bool shadowMatch=g.shadow?.SharedMesh==mesh,interactionMatch=g.InteractionMesh==mesh;
        if(!renderMatch||!shadowMatch||!interactionMatch||g.CollisionMesh!=collision||!g.Materials.Select(m=>m.Pointer).SequenceEqual(materials)||(g.Flags&VehicleRendererFlags.Static)!=0)throw new InvalidDataException("native mesh readback mismatch");
        if(logs++<80)Plugin.Diag(()=>$"SWAP model={owner.ComponentID} generation={e.Generation} meshHash={data.ObjSha256} lodCount={lodCount} renderMatch={renderMatch} shadowMatch={shadowMatch} interactionMatch={interactionMatch} collision=stock material=stock static=false");
    }
    [HideFromIl2Cpp] private void Adopt(VehicleObject root)
    {
        long ptr=root.Pointer.ToInt64();
        string? reason=Eligibility(root);if(reason!=null){Reject(ptr,reason);return;}
        if(!Resolve(root,out var basis,out var door,out var hatch)){Reject(ptr,"not-ready-or-ambiguous-topology");return;}
        var bg=Group(basis);var dg=Group(door);
        int lodCount=VehicleRendererGroup.LODNames?.Length??0;
        if(lodCount<1||lodCount>8||!ReadyGroup(bg)||!ReadyGroup(dg)||!AtlasSafe(root,bg!,dg!)){Reject(ptr,"render-or-atlas-unverifiable");return;}
        var key=new CupolaKey(root.Vehicle.Pointer.ToInt64(),ptr,root.VUID.Value);
        if(cache.TryGet(key,out var old)&&(old.Base.Pointer!=basis.Pointer||old.Door.Pointer!=door.Pointer||old.BaseWrapper!=bg!.Pointer.ToInt64()||old.DoorWrapper!=dg!.Pointer.ToInt64()))
            cache.Retire((k,v)=>k==key,v=>Release(v,"wrapper-replaced"));
        if(!cache.TryGet(key,out var e)) {
            e=new Entry{Root=root,Base=basis,Door=door,Hatch=hatch,BaseWrapper=bg!.Pointer.ToInt64(),DoorWrapper=dg!.Pointer.ToInt64(),Generation=++generation,BaseOriginal=Snapshot.Capture(bg!),DoorOriginal=Snapshot.Capture(dg!)};
            if(!cache.Add(key,e)){Reject(ptr,"cache-limit");return;}
            if(logs++<80)Plugin.Diag(()=>$"ADOPT rootVuid={root.VUID.Value} partGuid={root.GUID} baseVuid={basis.VUID.Value} doorVuid={door.VUID.Value} doorResolved=true");
            if(logs++<80)Plugin.Diag(()=>$"BALANCE rootVuid={root.VUID.Value} resourceMass={basis.GetMass(MassType.Everything)+door.GetMass(MassType.Everything)} cachedMass={basis.CachedMass+door.CachedMass} costs={basis.GetCost(MassType.Everything,CostType.Everything)+door.GetCost(MassType.Everything,CostType.Everything)}");
        }
        try {
            Swap(e,basis,bg!,baseMesh!,CupolaMeshData.Base,lodCount);
            Swap(e,door,dg!,doorMesh!,CupolaMeshData.Door,lodCount);
        } catch {
            e.BaseOriginal.Restore(baseMesh!,true);e.DoorOriginal.Restore(doorMesh!,true);throw;
        }
        string state=$"blown={(int)hatch.cookoffFlags} visible={door.visible} localPosition={door.ModelLocalPosition} localRotation={door.ModelLocalRotation}";
        if(state!=e.State) {bool baseline=e.State==null;e.State=state;if(logs++<80)Plugin.Diag(()=>$"STATE rootVuid={root.VUID.Value} baseline={(baseline?"CLOSED_REFERENCE":"CHANGED")} {state}");}
    }
    [HideFromIl2Cpp] private void Scan()
    {
        int candidates=0,objects=0;var vehicles=Object.FindObjectsOfType<Vehicle>();
        if(vehicles.Length>128)throw new InvalidDataException("vehicle budget");
        foreach(var vehicle in vehicles) {
            if(vehicle==null||vehicle.Released)continue;
            bool db=Definition(vehicle);
            var items=vehicle.ObjectReader?.Items;if(items==null)continue;
            int n=Count(items);if(n>CupolaPolicy.MaxObjects)throw new InvalidDataException("object budget");
            for(int i=0;i<n;i++) {
                if(++objects>CupolaPolicy.MaxObjects)throw new InvalidDataException("scan budget");
                var obj=items[i];if(obj==null||obj.Released||!string.Equals(obj.GUID,CupolaPolicy.PartGuid,StringComparison.OrdinalIgnoreCase))continue;
                candidates++;
                if(!db){Reject(obj.Pointer.ToInt64(),"definition-not-ready");continue;}
                if(obj.Vehicle?.Pointer!=vehicle.Pointer){Reject(obj.Pointer.ToInt64(),"foreign-reader-owner");continue;}
                Adopt(obj);
            }
        }
        if(candidates!=lastCandidates&&scanLogs++<10){lastCandidates=candidates;Plugin.Diag(()=>$"SCAN candidates={candidates} cached={cache.Count}");}
        cache.Retire((k,e)=>e.Root==null||e.Root.Released||e.Base==null||e.Door==null,v=>Release(v,"native-release"));
    }
    public void Update()
    {
        if(disposed||!ready||failed)return;
        try {
            int current=SceneManager.GetActiveScene().handle;
            if(scene!=current){cache.Clear(e=>{Restore(e);Release(e,"scene-change");});scene=current;rejected.Clear();logs=scanLogs=0;lastCandidates=-1;definitionReady=false;scanAt=0;}
            if(Time.unscaledTime<scanAt)return;scanAt=Time.unscaledTime+.5f;Scan();
        } catch(Exception ex){Fail(ex);}
    }
    [HideFromIl2Cpp] private void Release(Entry e,string reason){if(logs++<80)Plugin.Diag(()=>$"RELEASE rootVuid={e.Root.VUID.Value} generation={e.Generation} reason={reason}");}
    [HideFromIl2Cpp] private void Fail(Exception ex) {if(!failed)Plugin.Instance.Log.LogWarning("[Cupola] REJECT replacement disabled: "+ex.GetType().Name+": "+ex.Message);failed=true;}
    [HideFromIl2Cpp] private void Restore(Entry e)
    {
        if(e.Root==null||e.Root.Released)return;
        if(e.Base!=null&&e.Base.Model?.Pointer.ToInt64()==e.BaseWrapper)e.BaseOriginal.Restore(baseMesh!);
        if(e.Door!=null&&e.Door.Model?.Pointer.ToInt64()==e.DoorWrapper)e.DoorOriginal.Restore(doorMesh!);
    }
    [HideFromIl2Cpp] public void Dispose()
    {
        if(disposed)return;disposed=true;
        try {
            cache.Clear(e=>{Restore(e);Release(e,"driver-release");});
            if(baseMesh!=null)Object.Destroy(baseMesh);if(doorMesh!=null)Object.Destroy(doorMesh);
        } catch(Exception ex){Fail(ex);} // Retain meshes if a native owner cannot be safely restored.
    }
    public void OnDestroy()=>Dispose();
}
