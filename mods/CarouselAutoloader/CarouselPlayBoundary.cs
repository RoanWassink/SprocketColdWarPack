using HarmonyLib;
using Sprocket.VehicleDesigner;
using Sprocket.Vehicles;
namespace SprocketCarouselAutoloader;

internal static class CarouselPlayBoundary
{
    private static readonly Dictionary<IntPtr,PlayBoundaryGate> Entering=new();
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleEditor),nameof(VehicleEditor.EnableBehaviour))]
    private static void BeforeEditorPlay(VehicleEditor __instance,out IntPtr __state)
    {
        __state=IntPtr.Zero;
        try
        {
            var target=__instance.target;
            if(target==null || target.BehaviourEnabled) return;
            var pointer=target.Pointer;
            if(!Entering.TryGetValue(pointer,out var gate)) Entering[pointer]=gate=new();
            if(!gate.Begin(false)) return;
            __state=pointer;
            CarouselRuntime.Guard("Reset new editor Play supply",()=>CarouselRuntime.ResetEditorPlaySupply(target.Cast<IVehicleGateway>()));
        }
        catch(Exception ex) { HookGuard.Warn("VehicleEditor.EnableBehaviour",-1,ex); }
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(VehicleEditor),nameof(VehicleEditor.EnableBehaviour))]
    private static void FinishedEditorPlay(IntPtr __state)
    {
        try { if(__state!=IntPtr.Zero && Entering.Remove(__state,out var gate)) gate.End(); }
        catch(Exception ex) { HookGuard.Warn("VehicleEditor.EnableBehaviour finalizer",-1,ex); }
    }
}
