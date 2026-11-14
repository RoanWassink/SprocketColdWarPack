using System.Reflection;
using Sprocket.Vehicles;
namespace SprocketEmitter;

// Shared entrance for Ensure, callbacks and already-cached lamps. No cache writes.
internal static class EmitterEligibility
{
    private static bool? nativeReady;
    internal static bool Prepare()
    {
        if(nativeReady.HasValue)return nativeReady.Value;
        bool ok=true;
        foreach(var (type,name) in new (Type,string)[]{
            (typeof(VehicleObject),"get_Flags"),(typeof(VehicleObject),"get_Vehicle"),
            (typeof(VehicleObject),"get_VehicleTransform"),(typeof(VehicleTransform),"get_Parent"),
            (typeof(VehicleTransform),"get_VehicleObject")}) {
            var fields=type.GetFields(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)
                .Where(f=>f.Name.StartsWith("NativeMethodInfoPtr_"+name+"_")).ToArray();
            var result=fields.Length==1?NativeGuard.Check(type,fields[0].Name,name,0):new NativeGuard.Result(false,"missing/ambiguous field");
            Plugin.Instance.Log.LogInfo("SelfTest emitter eligibility "+type.Name+"."+name+" "+(result.Ok?"PASS ":"FAIL ")+result.Detail);
            ok &= result.Ok;
        }
        nativeReady=ok;return ok;
    }
    // The budget is shared by Ensure, Apply and Alive; no resources are allocated here.
    private static readonly HashSet<IntPtr> rejected=new();
    private static int diagnosticScene=int.MinValue;
    internal static int Reject(VehicleObjectModel model,string reason) {
        int scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if(scene!=diagnosticScene){diagnosticScene=scene;rejected.Clear();}
        var pointer=model.VehicleObject?.Pointer ?? model.Pointer;
        if(rejected.Count<20 && rejected.Add(pointer))
            Plugin.Instance.Log.LogInfo("[EMITTER] reject scene="+scene+" part="+pointer+" reason="+reason);
        return -1;
    }
    internal static int Check(VehicleObjectModel? model)
    {
        if(model==null || model.ComponentID!=EmitterRuntime.ComponentId)return -1;
        var owner=model.VehicleObject;
        if(!Prepare())return Reject(model,"native guard");
        if(owner==null || owner.Released)return Reject(model,"missing/released owner");
        if(model.Vehicle==null || owner.Vehicle==null || model.Vehicle.Pointer!=owner.Vehicle.Pointer)
            return Reject(model,"missing/foreign vehicle");
        int form=(string.Equals(EmitterRuntime.PartGuid,owner.GUID,StringComparison.OrdinalIgnoreCase)?0:-1);
        if(form<0)return Reject(model,"unknown lamp GUID="+owner.GUID);
        // Object placement is authoritative. Component IsInstalled is a separate
        // behaviour bit; a plain model need not have it set by object installation.
        if((owner.Flags & VehicleObjectFlags.Installed)==0)return Reject(model,"owner not installed flags="+owner.Flags);
        const VehicleObjectFlags placement=VehicleObjectFlags.ActivePlacement|VehicleObjectFlags.ActivePlacementChild;
        if((owner.Flags & (VehicleObjectFlags.DontSaveWithVehicle|VehicleObjectFlags.NeverSave))!=0)
            return Reject(model,"preview owner save flags="+owner.Flags);
        var node=owner.VehicleTransform;
        if(node==null)return Reject(model,"missing transform");
        // Parent transforms can be structural nodes without a VehicleObject.
        // Save flags on parents concern their own serialization, not child placement.
        for(int depth=0;depth<256;depth++) {
            var part=node.VehicleObject;
            if(part!=null) {
                if(part.Released)return Reject(model,"released ancestor depth="+depth);
                if(part.Vehicle==null || part.Vehicle.Pointer!=owner.Vehicle.Pointer)
                    return Reject(model,"foreign ancestor depth="+depth);
                if((part.Flags&placement)!=0)return Reject(model,"placement ancestor depth="+depth+" flags="+part.Flags);
            }
            node=node.Parent;
            if(node==null)return form;
        }
        return Reject(model,"cyclic/deep transform tree");
    }
}
