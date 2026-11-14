using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
namespace SprocketVisualEffects;

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
