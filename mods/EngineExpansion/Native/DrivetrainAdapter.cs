using HarmonyLib;
using Sprocket.PowertrainLayouts;
using Sprocket.Vehicles.Colliders;
using Sprocket.Vehicles.Powertrains.Layouts;
using Sprocket.Vehicles.Transmissions;
using SprocketEngineExpansion.Core;
using UnityEngine;
namespace SprocketEngineExpansion.Native;

[HarmonyPatch]
internal static class DrivetrainAdapter
{
    internal static Func<double> Volume=()=>.5,AngleMultiplier=()=>2;
    private sealed record BuildScope(TransmissionBlock Block,float Scale);
    [ThreadStatic]private static BuildScope? current;
    private static float Scale(BuildScope scope)=>scope.Scale;
    [HarmonyPrefix,HarmonyPatch(typeof(TransmissionBlock),nameof(TransmissionBlock.Build))]
    private static void Begin(TransmissionBlock __instance,out BuildScope? __state)
    {
        __state=current;
        current=null;
        try
        {
            if(Environment.CurrentManagedThreadId==Plugin.MainThreadId&&!__instance.VehicleObject.Released)
                current=new(__instance,(float)(DrivetrainRules.LengthScale(Volume())*(PowerpackModel.TransmissionActive(__instance)?Math.Cbrt(PowerpackModel.Settings.TransmissionVolumeFactor):1)));
        }
        catch(Exception ex){Plugin.Warn("drivetrain configuration",ex);}
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(TransmissionBlock),nameof(TransmissionBlock.Build))]
    private static Exception? End(BuildScope? __state,Exception? __exception)
    {current=__state;return __exception;}
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleBoxCollider),nameof(VehicleBoxCollider.Set))]
    private static void Box(VehicleBoxCollider __instance,ref Vector3 __0,ref Vector3 __1)
    {
        if(current is not {} c)return;
        if(__instance.Pointer!=c.Block.selectionCollider?.Pointer&&__instance.Pointer!=c.Block.obstructionCollider?.Pointer)return;
        __0*=Scale(c);__1*=Scale(c);
    }
    [HarmonyPrefix,HarmonyPatch(typeof(PowertrainComponent),nameof(PowertrainComponent.SetPortTransform))]
    private static void ComponentPort(Il2CppSystem.Object __instance,ref Vector3 __1)=>PortPosition(__instance,ref __1);
    [HarmonyPrefix,HarmonyPatch(typeof(VehiclePowertrain),nameof(VehiclePowertrain.SetPortTransform))]
    private static void VehiclePort(Il2CppSystem.Object __instance,ref Vector3 __1)=>PortPosition(__instance,ref __1);
    private static void PortPosition(Il2CppSystem.Object __instance,ref Vector3 __1)
    {if(current is {} c&&__instance.Pointer==c.Block.PowertrainNode?.Pointer)__1*=Scale(c);}
    private static bool OwnModel(Il2CppSystem.Object model,BuildScope c)=>model.Pointer==c.Block.model?.Pointer||model.Pointer==c.Block.gearVisualizerModel?.Pointer;
    [HarmonyPrefix,HarmonyPatch(typeof(TransmissionBlockModel),nameof(TransmissionBlockModel.Generate))]
    private static void BlockModel(Il2CppSystem.Object __instance,ref TransmissionModelBlueprint __0,out TransmissionModelBlueprint? __state)=>Model(__instance,ref __0,out __state);
    [HarmonyPrefix,HarmonyPatch(typeof(TransmissionGearVisualizer),nameof(TransmissionGearVisualizer.Generate))]
    private static void GearModel(Il2CppSystem.Object __instance,ref TransmissionModelBlueprint __0,out TransmissionModelBlueprint? __state)=>Model(__instance,ref __0,out __state);
    private static void Model(Il2CppSystem.Object __instance,ref TransmissionModelBlueprint __0,out TransmissionModelBlueprint? __state)
    {
        __state=null;
        if(current is not {} c||!OwnModel(__instance,c)||c.Scale==1)return;
        __state=new TransmissionModelBlueprint(__0.Scale,__0.GearSegmentLength,__0.GearShroudCount,__0.Layout,__0.gearRadii);
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<GearCombination>? gears=null;
        if(__0.gearRadii!=null)
        {
            gears=new(__0.gearRadii.Length);
            for(int i=0;i<gears.Length;i++){var gear=__0.gearRadii[i];gear.CountershaftRadius*=c.Scale;gear.OutputRadius*=c.Scale;gears[i]=gear;}
        }
        // Generated value wrappers can alias a boxed native value: allocate a complete new input.
        __0=new TransmissionModelBlueprint(__0.Scale*c.Scale,__0.GearSegmentLength*c.Scale,__0.GearShroudCount,__0.Layout,gears!);
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(TransmissionBlockModel),nameof(TransmissionBlockModel.Generate))]
    private static Exception? BlockModelFinished(ref TransmissionModelBlueprint __0,TransmissionModelBlueprint? __state,Exception? __exception)=>ModelFinished(ref __0,__state,__exception);
    [HarmonyFinalizer,HarmonyPatch(typeof(TransmissionGearVisualizer),nameof(TransmissionGearVisualizer.Generate))]
    private static Exception? GearModelFinished(ref TransmissionModelBlueprint __0,TransmissionModelBlueprint? __state,Exception? __exception)=>ModelFinished(ref __0,__state,__exception);
    private static Exception? ModelFinished(ref TransmissionModelBlueprint __0,TransmissionModelBlueprint? __state,Exception? __exception)
    {if(__state is {} original)__0=original;return __exception;}
    private sealed record AngleScope(Port Port,float Original);
    [ThreadStatic]private static HashSet<IntPtr>? anglePorts;
    [HarmonyPrefix,HarmonyPatch(typeof(DriveShaft),nameof(DriveShaft.PowertrainLayoutUpdate))]
    private static void Shaft(DriveShaft __instance,PowertrainLayout __0,out AngleScope[] __state)
    {
        __state=Array.Empty<AngleScope>();
        if(Environment.CurrentManagedThreadId!=Plugin.MainThreadId)return;
        try
        {
            var connection=__0.GetConnection(__instance.LayoutConnectionID);
            anglePorts??=new();
            var ports=new[]{connection.provider,connection.receiver}.DistinctBy(p=>p.Pointer).Where(p=>!anglePorts.Contains(p.Pointer)).ToArray();
            var saved=ports.Select(p=>new AngleScope(p,p.minConnectionAngle)).ToArray();
            // Validate the whole batch before touching native ports; finalizer restores on exceptions.
            var values=saved.Select(p=>DrivetrainRules.Angle(p.Original,AngleMultiplier())).ToArray();
            __state=saved;
            for(int i=0;i<saved.Length;i++){anglePorts.Add(saved[i].Port.Pointer);saved[i].Port.minConnectionAngle=values[i];}
        }
        catch(Exception ex){Plugin.Warn("driveshaft angle",ex);}
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(DriveShaft),nameof(DriveShaft.PowertrainLayoutUpdate))]
    private static Exception? ShaftFinished(AngleScope[] __state,Exception? __exception)
    {
        foreach(var item in __state)
        {
            try{item.Port.minConnectionAngle=item.Original;}
            catch(Exception ex){Plugin.Warn("driveshaft angle restore",ex);__exception??=ex;}
            finally{anglePorts?.Remove(item.Port.Pointer);}
        }
        return __exception;
    }
}
