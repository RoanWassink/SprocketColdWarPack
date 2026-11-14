using HarmonyLib;
using Sprocket.Vehicles.Engines;
using SprocketEngineExpansion.Core;
using UnityEngine;
namespace SprocketEngineExpansion.Native;

[HarmonyPatch]
internal static class BlueprintHooks
{
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBlueprint),"get_MaxRPM")]
    private static void RPM(EngineBlueprint __instance,ref int __result)
    {if(Plans.Modified(__instance))__result=(int)Math.Round(__result*Catalog.Get(Plans.EffectiveKind(__instance)).RPM);}
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBlueprint),"get_MaxTorque")]
    private static void Torque(EngineBlueprint __instance,ref float __result)
    {
        if(!Plans.Modified(__instance))return;
        try{var value=Plans.Capture(__instance);__result=Plans.PeakTorque(value,AssemblyStatus.ValidFor(__instance))/value.OutputCoefficient;}
        catch(Exception ex){Plugin.Warn("torque",ex);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBlueprint),"get_TorqueCurve")]
    private static void Curve(EngineBlueprint __instance,ref AnimationCurve __result)
    {
        if(!Plans.Modified(__instance))return;
        try{__result=Plans.DisplayCurve(Plans.Capture(__instance),AssemblyStatus.ValidFor(__instance));}
        catch(Exception ex){Plugin.Warn("curve",ex);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBlueprint),"get_Mass")]
    private static void Mass(EngineBlueprint __instance,ref float __result)
    {
        if(Plans.NativeRead!=0)return;
        if(Plans.Modified(__instance))__result=(float)(__result*Catalog.Get(Plans.EffectiveKind(__instance)).Mass);
        // Housing is a fraction of the profile engine mass, only while the powerpack is date-effective.
        if(EngineDateRegistry.PowerpackEffective(__instance))__result=(float)PowerpackRules.WithHousing(__result,PowerpackLayout.MassFraction(),true);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBlueprint),"get_MaterialCost")]
    private static void MaterialCost(EngineBlueprint __instance,ref float __result)
    {
        if(Plans.NativeRead!=0)return;
        if(Plans.Modified(__instance))__result*=(float)Catalog.Get(Plans.EffectiveKind(__instance)).Cost;
        if(EngineDateRegistry.PowerpackEffective(__instance))__result=(float)PowerpackRules.WithHousing(__result,PowerpackLayout.CostFraction(),true);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBlueprint),"get_AssemblyCost")]
    private static void AssemblyCost(EngineBlueprint __instance,ref float __result)
    {
        if(!Plans.Modified(__instance))return;var s=Plans.EffectiveKind(__instance);
        __result*=(float)Catalog.Get(s).Cost;
    }
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBlueprint),"get_FuelPerRevolution")]
    private static void Fuel(EngineBlueprint __instance,ref float __result)
    {if(Plans.Modified(__instance))__result*=(float)Catalog.Get(Plans.EffectiveKind(__instance)).Fuel;}
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBlueprint),"get_AveragePower")]
    private static void Power(EngineBlueprint __instance,ref float __result)
    {
        if(!Plans.Modified(__instance))return;
        try
        {
            // Current body evaluates power at the midpoint of native downshift/upshift; it does not integrate a curve.
            var plan=Plans.Capture(__instance).Plan;
            double rpm=(__instance.Downshift+__instance.Upshift)*.5;
            __result=(float)plan.Power(rpm,AssemblyStatus.ValidFor(__instance));
        }
        catch(Exception ex){Plugin.Warn("average power",ex);}
    }
}


