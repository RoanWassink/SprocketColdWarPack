using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
namespace SprocketCupolas;

// Runtime safety net in front of risky interop calls. An Il2CppInterop wrapper
// invokes the IL2CPP MethodInfo stored in its static NativeMethodInfoPtr_* field.
// Before the call we verify that exactly one such field exists, that it holds a
// MethodInfo with the expected name/parameter count, that it is not a generic
// method definition, and that it has native code (methodPointer != 0).
// 0.2.1 failure this catches: two fields named
// NativeMethodInfoPtr_LoadAllAssetsAsync_Public_AssetBundleRequest_0 (name
// collision with LoadAllAssetsAsync<T>); the non-generic wrapper invoked the
// generic definition -> "Invalid call to method". Reads only; writes nothing.
internal static class NativeGuard
{
    internal static bool Prepare()
    {
        foreach(var (type,name,count,filter) in new (Type,string,int,string)[]{
            (typeof(Sprocket.Vehicles.VehicleObjectModel),"MarkDynamic",0,""),
            (typeof(Sprocket.Vehicles.VehicleRendererGroup),"SetMesh",1,"Il2CppReferenceArray"),
            (typeof(Sprocket.Vehicles.VehicleRendererGroup),"SetShadowProxy",1,""),
            (typeof(Sprocket.Vehicles.VehicleRendererGroup),"SetInteractionMesh",1,""),
            (typeof(Sprocket.Vehicles.VehicleMeshRenderer),"get_SharedMesh",0,""),
            (typeof(UnityEngine.Mesh),"set_vertices",1,""),
            (typeof(UnityEngine.Mesh),"set_normals",1,""),
            (typeof(UnityEngine.Mesh),"set_uv",1,""),
            (typeof(UnityEngine.Mesh),"set_tangents",1,""),
            (typeof(UnityEngine.Mesh),"set_triangles",1,""),
            (typeof(UnityEngine.Mesh),"RecalculateBounds",0,"_Void_0"),
            (typeof(Sprocket.Vehicles.VehicleObject),"get_Flags",0,""),
            (typeof(Sprocket.Vehicles.VehicleObject),"get_Vehicle",0,""),
            (typeof(Sprocket.Vehicles.VehicleObject),"get_VehicleTransform",0,""),
            (typeof(Sprocket.Vehicles.VehicleTransform),"get_Parent",0,""),
            (typeof(Sprocket.Vehicles.VehicleTransform),"get_VehicleObject",0,"")}) {
            var fields=type.GetFields(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)
                .Where(f=>f.Name.StartsWith("NativeMethodInfoPtr_"+name+"_")&&f.Name.Contains(filter)).ToArray();
            var r=fields.Length==1?Check(type,fields[0].Name,name,count):new Result(false,"missing/ambiguous wrapper");
            if(!r.Ok){Plugin.Instance.Log.LogWarning("[Cupola] REJECT native guard "+type.Name+"."+name+": "+r.Detail);return false;}
        }
        return true;
    }
    internal readonly record struct Result(bool Ok,string Detail);
    internal static unsafe Result Check(Type interopType,string field,string expectedName,int expectedParams)
    {
        try
        {
            var fields=interopType.GetFields(BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static).Where(f=>f.Name==field).ToArray();
            if(fields.Length==0)return new(false,$"missing: interop field {field} not found");
            if(fields.Length>1)
                return new(false,$"alias: {fields.Length} interop fields named {field} ({string.Join("; ",fields.Select(f=>Describe((IntPtr)f.GetValue(null)!)))})");
            var info=(IntPtr)fields[0].GetValue(null)!;
            if(info==IntPtr.Zero)return new(false,"stripped: no MethodInfo in game metadata");
            var name=Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(info))??"?";
            var count=(int)IL2CPP.il2cpp_method_get_param_count(info);
            var generic=IL2CPP.il2cpp_method_is_generic(info);
            if(name!=expectedName||count!=expectedParams||generic)
                return new(false,$"alias: {field} holds {Describe(info)}, expected {expectedName}/{expectedParams} params, non-generic");
            var pointer=UnityVersionHandler.Wrap((Il2CppMethodInfo*)info).MethodPointer;
            if(pointer==IntPtr.Zero)return new(false,$"stripped: {name} has no native code (methodPointer 0)");
            return new(true,"native code present");
        }
        catch(Exception ex){return new(false,"guard error "+ex.GetType().Name+": "+ex.Message);}
    }
    private static unsafe string Describe(IntPtr info)
    {
        if(info==IntPtr.Zero)return "null MethodInfo";
        var name=Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(info))??"?";
        var pointer=UnityVersionHandler.Wrap((Il2CppMethodInfo*)info).MethodPointer;
        return $"{name}/{IL2CPP.il2cpp_method_get_param_count(info)} generic={IL2CPP.il2cpp_method_is_generic(info)} native={(pointer!=IntPtr.Zero?"yes":"no")}";
    }
}

