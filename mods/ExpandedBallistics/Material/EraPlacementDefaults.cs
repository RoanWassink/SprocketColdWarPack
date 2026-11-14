using HarmonyLib;
using Sprocket.VehicleDesigner.Operations;
using Sprocket.VehicleDesigner.Operations.Place;

namespace SprocketExpandedBallistics.Materials;

[HarmonyPatch]
internal static class EraPlacementDefaults
{
    internal sealed class NewPartScope
    {
        internal readonly string Guid;
        internal readonly float Rotation;
        internal readonly HashSet<IntPtr> Applied=new();
        internal NewPartScope(string guid,float rotation){Guid=guid;Rotation=rotation;}
    }
    [ThreadStatic]private static NewPartScope? current;
    private static int logs;

    [HarmonyPrefix,HarmonyPatch(typeof(VehicleOperations),nameof(VehicleOperations.InstantiateAndBeginPlacement))]
    private static void NewPart(string __0,out NewPartScope? __state)
    {
        __state=current;
        var preset=EraPresetRegistry.Presets.FirstOrDefault(p=>p.PartGuid==__0);
        // Scoped to creation from the part menu. Pickup, duplicate, load and
        // transform operations use BeginPlacement without this creation scope.
        current=preset==null?null:new NewPartScope(__0,preset.DefaultPlacementRotation);
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(VehicleOperations),nameof(VehicleOperations.InstantiateAndBeginPlacement))]
    private static void Finished(NewPartScope? __state)=>current=__state;

    [HarmonyPostfix,HarmonyPatch(typeof(PlaceOperation),nameof(PlaceOperation.UpdateStart))]
    private static void Started(PlaceOperation __instance)
    {
        var scope=current;
        if(scope==null)return;
        try
        {
            if(__instance.target?.Object?.GUID!=scope.Guid||!scope.Applied.Add(__instance.Pointer))return;
            // Native zero-snap path adds degrees to primary manual rotation and
            // subtracts them from the mirror, retaining the mounting normal.
            if(scope.Rotation!=0f)__instance.AddRotation(scope.Rotation,0f);
            if(logs++<16)if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo($"[ERA new placement] guid={scope.Guid} manualRotationAdded={scope.Rotation}; new part only, native mirror rotation retained");
        }
        catch(Exception ex){MaterialFeature.ModLog.LogWarning("ERA new placement orientation: "+ex.Message);}
    }
}
