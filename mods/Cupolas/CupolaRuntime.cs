using System.Security.Cryptography;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using Sprocket.Vehicles.PartImporting;
using Sprocket.Vehicles.CrewSystems;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace SprocketCupolas;

public sealed class CupolaRuntime : MonoBehaviour
{
    public CupolaRuntime(IntPtr pointer):base(pointer) { }
    private sealed class VariantMeshes
    {
        internal Mesh Base=null!;
        internal Mesh Door=null!;
        internal bool HasGlass;
    }
    private sealed class Entry
    {
        internal VehicleObject Root=null!;
        internal VehicleObjectModel Base=null!, Door=null!;
        internal Hatch Hatch=null!;
        internal long BaseWrapper, DoorWrapper;
        internal int Generation;
    }
    private readonly CupolaCache<Entry> cache=new();
    private readonly HashSet<string> rejected=new();
    private readonly Dictionary<string, VariantMeshes> variants=new(StringComparer.OrdinalIgnoreCase);
    private VehicleMaterial? glassMaterial;
    private bool ready,disposed,failed;
    private int scene=int.MinValue,generation,logs,lastCandidates=-1,scanLogs;
    private float scanAt;

    public void Start()
    {
        try {
            if(!NativeGuard.Prepare())return;
            var hiddenDoor = CupolaMesh.Create(CupolaMeshData.HiddenDoor);
            variants[CupolaPolicy.PartGuid] = new VariantMeshes {
                Base = CupolaMesh.Create(CupolaMeshData.T90ABase),
                Door = CupolaMesh.Create(CupolaMeshData.T90ADoor),
                HasGlass = false
            };
            variants[CupolaPolicy.AbramsGuid] = new VariantMeshes {
                Base = CupolaMesh.CreateWithGlass(CupolaMeshData.AbramsBase, CupolaMeshData.AbramsGlass),
                Door = hiddenDoor,
                HasGlass = true
            };
            variants[CupolaPolicy.LeopardGuid] = new VariantMeshes {
                Base = CupolaMesh.CreateWithGlass(CupolaMeshData.LeopardBase, CupolaMeshData.LeopardGlass),
                Door = hiddenDoor,
                HasGlass = true
            };
            variants[CupolaPolicy.ChallengerGuid] = new VariantMeshes {
                Base = CupolaMesh.CreateWithGlass(CupolaMeshData.ChallengerBase, CupolaMeshData.ChallengerGlass),
                Door = hiddenDoor,
                HasGlass = true
            };
            variants[CupolaPolicy.T80BvmGuid] = new VariantMeshes {
                Base = CupolaMesh.CreateWithGlass(CupolaMeshData.T80BvmBase, CupolaMeshData.T80BvmGlass),
                Door = hiddenDoor,
                HasGlass = true
            };
            variants[CupolaPolicy.T90ADriverHatchGuid] = new VariantMeshes {
                Base = CupolaMesh.CreateWithGlass(CupolaMeshData.T90ADriverHatchBase, CupolaMeshData.T90ADriverHatchGlass),
                Door = hiddenDoor,
                HasGlass = true
            };

            var shader=Shader.Find("HDRP/Lit");
            if(shader!=null) {
                var surface=new Material(shader){name="Cupola periscope glass",hideFlags=HideFlags.HideAndDontSave};
                surface.SetColor("_BaseColor",new Color(0.035f,0.065f,0.075f,1f));
                surface.SetFloat("_Smoothness",0.90f);
                surface.SetFloat("_Metallic",0.35f);
                surface.SetFloat("_DoubleSidedEnable",1);
                surface.SetFloat("_CullMode",0);
                surface.SetFloat("_CullModeForward",0);
                HDMaterial.ValidateMaterial(surface);
                glassMaterial=new VehicleMaterial(surface){Flags=VehicleMaterialFlags.Unpainted|VehicleMaterialFlags.DisableGrime|VehicleMaterialFlags.CustomShader};
            }
            ready=true;
            Plugin.Diag(()=>$"START ok variants={variants.Count} glassMat={glassMaterial!=null}");
        } catch(Exception ex) { Fail(ex); }
    }
    [HideFromIl2Cpp] private void Reject(long pointer,string reason)
    {
        if(rejected.Count<40 && rejected.Add(pointer+":"+reason))Plugin.Diag(()=>"REJECT root="+pointer+" reason="+reason);
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
    [HideFromIl2Cpp] private bool Resolve(VehicleObject root,out VehicleObjectModel basis,out VehicleObjectModel door,out Hatch hatch)
    {
        basis=null!;door=null!;hatch=null!;
        var own=Components(root);
        var commander=own.Where(c=>c.ComponentID=="cupola"&&c.TryCast<CommanderCupola>()!=null).ToArray();
        var hatches=own.Where(c=>c.ComponentID=="hatch").Select(c=>c.TryCast<Hatch>()).Where(c=>c!=null).ToArray();
        if(commander.Length>1||hatches.Length!=1){Reject(root.Pointer.ToInt64(),$"resolve-own count={own.Count} cmd={commander.Length} hatch={hatches.Length}");return false;}
        hatch=hatches[0]!;
        long commanderPtr=commander.Length==1?commander[0].Pointer.ToInt64():0;
        var list=new List<VehicleObjectModel>();var stack=new Stack<VehicleTransform>();var seen=new HashSet<long>();
        stack.Push(root.VehicleTransform);
        while(stack.Count>0) {
            var vt=stack.Pop();if(vt==null||seen.Count>=CupolaPolicy.MaxWalk||!seen.Add(vt.Pointer.ToInt64()))return false;
            var obj=vt.VehicleObject;
            if(obj!=null) {
                if(obj.Released||obj.Vehicle?.Pointer!=root.Vehicle?.Pointer)return false;
                if((obj.Flags&(VehicleObjectFlags.ActivePlacement|VehicleObjectFlags.ActivePlacementChild|VehicleObjectFlags.DontSaveWithVehicle|VehicleObjectFlags.NeverSave))!=0)return false;
                var cs=Components(obj);
                foreach(var c in cs)if(c.TryCast<VehicleObjectModel>() is {} m)list.Add(m);
            }
            var children=vt.Children;
            if(children!=null) {int n=Count(children);if(n>CupolaPolicy.MaxWalk)return false;for(int i=0;i<n;i++)stack.Push(children[i]);}
        }
        var models=list.Select(m=>new CupolaPolicy.Model(m.Pointer.ToInt64(),m.ComponentID,m.VehicleObject.Pointer.ToInt64(),root.Pointer.ToInt64(),m.associatedComponent?.Pointer.ToInt64()??0)).ToArray();
        if(!CupolaPolicy.Resolve(root.Pointer.ToInt64(),commanderPtr,hatch.DoorModel?.Pointer.ToInt64()??0,models,out var b,out var d)){
            Reject(root.Pointer.ToInt64(),$"resolve-models count={models.Length}");
            return false;
        }
        basis=list.Single(m=>m.Pointer.ToInt64()==b.Pointer);door=list.Single(m=>m.Pointer.ToInt64()==d.Pointer);
        return basis.Vehicle?.Pointer==root.Vehicle?.Pointer&&door.Vehicle?.Pointer==root.Vehicle?.Pointer;
    }
    [HideFromIl2Cpp] private void ApplyModel(VehicleObjectModel owner,Mesh mesh,bool withGlass)
    {
        var native=owner.Model;
        if(native==null)return;
        owner.MarkDynamic();
        if(native.InteractionMesh!=mesh) {
            native.SetMesh(mesh);
            native.SetShadowProxy(mesh);
            native.SetInteractionMesh(mesh);
            if(logs++<80)Plugin.Diag(()=>$"SWAP model={owner.ComponentID} withGlass={withGlass} meshMatch={native.InteractionMesh==mesh}");
        }
        var mats=native.Materials;
        var fallback=(mats!=null && mats.Length>0 ? mats[0] : null) ?? native.Material;
        var paint=ExteriorPaint.Get(owner, fallback);
        if(paint==null)return;
        if(withGlass && glassMaterial!=null) {
            if(mats==null || mats.Length!=2 || mats[0]?.Pointer!=paint.Pointer || mats[1]?.Pointer!=glassMaterial.Pointer) {
                native.SetMaterials(new Il2CppReferenceArray<VehicleMaterial>(new[]{paint,glassMaterial}));
                ExteriorPaint.Process(owner, paint);
            }
        } else {
            if(mats==null || mats.Length!=1 || mats[0]?.Pointer!=paint.Pointer) {
                native.SetMaterials(new Il2CppReferenceArray<VehicleMaterial>(new[]{paint}));
                ExteriorPaint.Process(owner, paint);
            }
        }
    }
    [HideFromIl2Cpp] private void Adopt(VehicleObject root, VariantMeshes vm)
    {
        long ptr=root.Pointer.ToInt64();
        string? reason=Eligibility(root);if(reason!=null){Reject(ptr,reason);return;}
        if(!Resolve(root,out var basis,out var door,out var hatch)){Reject(ptr,"not-ready-or-ambiguous-topology");return;}
        if(basis.Model==null||door.Model==null){Reject(ptr,"native-model-null");return;}
        var key=new CupolaKey(root.Vehicle.Pointer.ToInt64(),ptr,root.VUID.Value);
        if(!cache.TryGet(key,out var e)) {
            e=new Entry{Root=root,Base=basis,Door=door,Hatch=hatch,BaseWrapper=basis.Model.Pointer.ToInt64(),DoorWrapper=door.Model.Pointer.ToInt64(),Generation=++generation};
            if(!cache.Add(key,e)){Reject(ptr,"cache-limit");return;}
            if(logs++<80)Plugin.Diag(()=>$"ADOPT rootVuid={root.VUID.Value} partGuid={root.GUID}");
        }
        ApplyModel(basis,vm.Base,vm.HasGlass);
        ApplyModel(door,vm.Door,false);
    }
    [HideFromIl2Cpp] private void Scan()
    {
        int candidates=0,objects=0;var vehicles=Object.FindObjectsOfType<Vehicle>();
        if(vehicles.Length>128)throw new InvalidDataException("vehicle budget");
        foreach(var vehicle in vehicles) {
            if(vehicle==null||vehicle.Released)continue;
            var items=vehicle.ObjectReader?.Items;if(items==null)continue;
            int n=Count(items);if(n>CupolaPolicy.MaxObjects)throw new InvalidDataException("object budget");
            for(int i=0;i<n;i++) {
                if(++objects>CupolaPolicy.MaxObjects)throw new InvalidDataException("scan budget");
                var obj=items[i];if(obj==null||obj.Released||obj.GUID==null)continue;
                if(!variants.TryGetValue(obj.GUID, out var vm))continue;
                candidates++;
                if(obj.Vehicle?.Pointer!=vehicle.Pointer){Reject(obj.Pointer.ToInt64(),"foreign-reader-owner");continue;}
                Adopt(obj, vm);
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
            if(scene!=current){cache.Clear(e=>Release(e,"scene-change"));scene=current;rejected.Clear();logs=scanLogs=0;lastCandidates=-1;scanAt=0;}
            if(Time.unscaledTime<scanAt)return;scanAt=Time.unscaledTime+.5f;Scan();
        } catch(Exception ex){Fail(ex);}
    }
    [HideFromIl2Cpp] private void Release(Entry e,string reason){if(logs++<80)Plugin.Diag(()=>$"RELEASE rootVuid={e.Root.VUID.Value} generation={e.Generation} reason={reason}");}
    [HideFromIl2Cpp] private void Fail(Exception ex) {if(!failed)Plugin.Instance.Log.LogWarning("[Cupola] REJECT replacement disabled: "+ex.GetType().Name+": "+ex.Message);failed=true;}
    [HideFromIl2Cpp] public void Dispose()
    {
        if(disposed)return;disposed=true;
        try {
            cache.Clear(e=>Release(e,"driver-release"));
            foreach(var vm in variants.Values){if(vm.Base!=null)Object.Destroy(vm.Base);if(vm.Door!=null)Object.Destroy(vm.Door);}
            variants.Clear();
            if(glassMaterial?.Material!=null)Object.Destroy(glassMaterial.Material);
            ExteriorPaint.Cleanup();
        } catch(Exception ex){Fail(ex);}
    }
    public void OnDestroy()=>Dispose();
}
