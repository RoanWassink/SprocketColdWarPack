using System.Globalization;
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
        if(scope==null){LogDuplicate(__instance);return;}
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

    // 0.22.94: read-only diagnosis of a placement without the new-part scope
    // (duplicate, pickup or move) of an EB ERA part. Managed property wrappers
    // only, no offsets, no patch on ManualRotation; it writes nothing. At most
    // DuplicateLogCap lines per session, also when ReleaseDiagnostics is off.
    internal const int DuplicateLogCap=24;
    internal const string DuplicatePrefix="[ERA duplicate placement]";
    private static int duplicateLogs;
    private static readonly HashSet<IntPtr> duplicateLogged=new();
    private static void LogDuplicate(PlaceOperation op)
    {
        if(duplicateLogs>=DuplicateLogCap)return;
        try
        {
            var obj=op.target?.Object;
            var guid=obj?.GUID;
            if(obj==null||string.IsNullOrEmpty(guid)||!EraPresetRegistry.Presets.Any(p=>p.PartGuid==guid))return;
            if(!duplicateLogged.Add(op.Pointer))return;
            duplicateLogs++;
            var placement=op.placementInfo?.placement;
            var transform=obj.VehicleTransform;
            var placementRotation=placement==null?"n/a":placement.ManualRotation.ToString("0.###",CultureInfo.InvariantCulture);
            var transformRotation=transform==null?"n/a":transform.ManualRotation.ToString("0.###",CultureInfo.InvariantCulture);
            var mode=placement==null?"n/a":placement.orientationMode.ToString();
            var options=placement==null?"n/a":$"{placement.options} (0x{(int)placement.options:X})";
            var euler=transform==null?default:transform.Rotation.eulerAngles;
            var world=transform==null?"n/a":string.Format(CultureInfo.InvariantCulture,"({0:0.#},{1:0.#},{2:0.#})",euler.x,euler.y,euler.z);
            MaterialFeature.ModLog.LogInfo($"{DuplicatePrefix} guid={guid} placementManualRotation={placementRotation} transformManualRotation={transformRotation} orientationMode={mode} options={options} worldRotation={world}; read only, line {duplicateLogs}/{DuplicateLogCap}");
        }
        catch(Exception ex){duplicateLogs++;MaterialFeature.ModLog.LogWarning(DuplicatePrefix+" read failed: "+ex.Message);}
    }
}
