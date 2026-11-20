using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using Sprocket.Vehicles.Transmissions;
using UnityEngine;
namespace SprocketEngineExpansion.Native;

[HarmonyPatch]
internal static class PowerpackTransmission
{
    private static readonly Dictionary<IntPtr,bool> activeModels=new();
    private static readonly Dictionary<IntPtr,string> reports=new();
    // Existing Segment models are cached by native Generate. Retire them when changing the asset set;
    // keep their native transforms and let Generate synchronously create replacements.
    [HarmonyPrefix,HarmonyPatch(typeof(TransmissionBlockModel),nameof(TransmissionBlockModel.Generate))]
    private static void Generate(TransmissionBlockModel __instance,ITransmissionModelAssetLoader __1)
    {
        try
        {
            if(__1.TryCast<TransmissionBlock>() is not {} block)return;
            bool active=PowerpackModel.TransmissionActive(block);
            bool changed=activeModels.TryGetValue(__instance.Pointer,out var old)?old!=active:active;
            activeModels[__instance.Pointer]=active;
            if(!changed)return;
            static TransmissionBlockModel.Segment Reset(TransmissionBlockModel.Segment item)
            {item.Model?.Release();item.Model=null!;return item;}
            if(__instance.input!=null)__instance.input=Reset(__instance.input);
            if(__instance.gearShrouds!=null)for(int i=0;i<__instance.gearShrouds.Length;i++)__instance.gearShrouds[i]=Reset(__instance.gearShrouds[i]);
            if(__instance.outputs!=null)for(int i=0;i<__instance.outputs.Length;i++)__instance.outputs[i]=Reset(__instance.outputs[i]);
        }
        catch(Exception ex){Plugin.Warn("powerpack transmission reset",ex);throw;}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(TransmissionBlock),nameof(TransmissionBlock.Sprocket_Vehicles_Transmissions_ITransmissionModelAssetLoader_CreateModel))]
    private static void Create(TransmissionBlock __instance,ref IVehicleMesh __0)
    {
        if(!PowerpackModel.TransmissionActive(__instance))return;
        var lods=__0?.LODs;
        if(lods==null||lods.Length==0||lods[0]==null)return;
        string? name=lods[0].name;
        if(string.IsNullOrEmpty(name))return;
        string? part=Core.PowerpackMeshNames.Transmission(name);
        if(part==null)
        {
            if(!Core.PowerpackMeshNames.IsInternalGear(name))
                Plugin.Warn("powerpack transmission mesh",new InvalidOperationException("Unrecognised native powerpack transmission mesh "+name));
            return;
        }
        __0=PowerpackMeshes.Vehicle(part).Cast<IVehicleMesh>();
    }
    [HarmonyPostfix,HarmonyPatch(typeof(TransmissionBlockModel),nameof(TransmissionBlockModel.Release))]
    private static void Released(TransmissionBlockModel __instance)=>activeModels.Remove(__instance.Pointer);
    [HarmonyPostfix,HarmonyPatch(typeof(TransmissionBlock),nameof(TransmissionBlock.Build))]
    private static void Report(TransmissionBlock __instance)
    {
        if(!PowerpackModel.TransmissionActive(__instance)){reports.Remove(__instance.Pointer);return;}
        var box=(__instance.obstructionCollider??__instance.selectionCollider)?.TryCast<Sprocket.Vehicles.Colliders.VehicleBoxCollider>();
        if(box==null)return;
        string line=FormattableString.Invariant($"POWERPACK trans size module={__instance.powertrainNode?.TryCast<Sprocket.Vehicles.Powertrains.Layouts.PowertrainComponent>()?.powertrainModuleID} native-box={box.size.x:0.####}x{box.size.y:0.####}x{box.size.z:0.####} centre={box.centre} volume={box.size.x*box.size.y*box.size.z:0.######}m3");
        if(Plugin.Detailed&&(!reports.TryGetValue(__instance.Pointer,out var old)||old!=line))Plugin.ModLog.LogInfo(line);
        reports[__instance.Pointer]=line;
    }
    [HarmonyPostfix,HarmonyPatch(typeof(TransmissionBlock),nameof(TransmissionBlock.Release))]
    private static void BlockReleased(TransmissionBlock __instance){reports.Remove(__instance.Pointer);PowerpackCollision.RemoveComponent(__instance.Pointer);}
}
