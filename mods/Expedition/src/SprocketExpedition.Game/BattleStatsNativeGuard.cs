using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Sprocket;

namespace SprocketExpedition.Game;

// Native-side guard for the battle-stats observers. IL2CPP folds identical
// method bodies, so a Harmony detour on a shared address also fires for unrelated
// callers. Read the generated method's Il2CppMethodInfo, take its native
// methodPointer and sample the first bytes before patching.
internal static unsafe class BattleStatsNativeGuard
{
    internal static bool TryReadMethodBody(MethodBase method, out IntPtr methodPointer,
        out string bytes, out MethodBodyClassification classification, out string error)
    {
        methodPointer = IntPtr.Zero;
        bytes = "-";
        classification = default;
        try
        {
            // Il2CppInterop's own lookup for the NativeMethodInfoPtr_* field that
            // the generated wrapper uses for this exact method.
            var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
            if (field is null)
            {
                error = "no generated MethodInfo field";
                return false;
            }

            if (field.GetValue(null) is not IntPtr infoPointer || infoPointer == IntPtr.Zero)
            {
                error = "generated MethodInfo pointer is null";
                return false;
            }

            // The Il2CppMethodInfo layout is version-specific; let Il2CppInterop
            // wrap it instead of assuming an offset.
            methodPointer = UnityVersionHandler.Wrap((Il2CppMethodInfo*)infoPointer).MethodPointer;
            if (methodPointer == IntPtr.Zero)
            {
                error = "native methodPointer is null";
                return false;
            }

            var sample = new byte[BattleStatsHookGuard.SampleBytes];
            Marshal.Copy(methodPointer, sample, 0, sample.Length);
            bytes = Convert.ToHexString(sample);
            classification = BattleStatsHookGuard.Classify(sample);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    // A folded constructor trampoline can receive a different class. The kill-event
    // postfix only reads KillEventArgs members when the native class is exactly it.
    internal static bool IsKillEventArgs(IntPtr instance)
    {
        if (instance == IntPtr.Zero) return false;
        var expected = Il2CppClassPointerStore<KillEventArgs>.NativeClassPtr;
        if (expected == IntPtr.Zero) return false;
        return IL2CPP.il2cpp_object_get_class(instance) == expected;
    }
}
