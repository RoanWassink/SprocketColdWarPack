using HarmonyLib;
using Sprocket.Engines;
using Sprocket.Powertrains.Exhausts;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Events.Fires;
using Sprocket.Vehicles.Fires;
using Sprocket.Vehicles.FuelTanks;
using Sprocket.Vehicles.Powertrains;
using SprocketEngineExpansion.Core;
namespace SprocketEngineExpansion.Native;

[HarmonyPatch]
internal static class EngineCharacterHooks
{
    internal static bool Enabled=true,SpoolAndFuel=true,FireRisk=true,Exhaust=true;

    [HarmonyPostfix,HarmonyPatch(typeof(EngineBehaviour),"Sprocket_Powertrains_Exhausts_IExhaustProducer_get_ExhaustInfo")]
    private static void AdaptExhaust(EngineBehaviour __instance,ref ExhaustInfo __result)
    {
        if(!Enabled||!Exhaust||__instance==null)return;
        try
        {
            if(!RuntimeHooks.TryGetState(__instance.Pointer,out var kind,out var spool,out var req,out var running))return;
            var adapted=EngineCharacterRules.AdaptExhaust(__result.Rate,__result.Throttle,__result.FuelBurnFraction,req,spool,running,EngineCharacterRules.For(kind));
            __result.Rate=(float)adapted.Rate;
            __result.Throttle=(float)adapted.Throttle;
            __result.FuelBurnFraction=(float)adapted.FuelBurn;
        }
        catch(Exception ex){Plugin.Warn("engine exhaust",ex);}
    }

    [HarmonyPrefix,HarmonyPatch(typeof(FireControlVehicleModule),nameof(FireControlVehicleModule.OnFireSourceFireStarted))]
    private static bool FilterFire(FireControlVehicleModule __instance,Il2CppSystem.Object __0,FireStartEventArgs __1)
    {
        if(!Enabled||!FireRisk||__1==null)return true;
        try
        {
            var kind=ResolveFireEngineKind(__instance,__0);
            if(kind==EngineKind.Petrol)return true;
            var outcome=EngineCharacterRules.FireOutcome(UnityEngine.Random.value,__1.FireDuration,EngineCharacterRules.For(kind));
            if(!outcome.Ignites)return false;
            __1._FireDuration_k__BackingField=(float)outcome.Duration;
            return true;
        }
        catch(Exception ex){Plugin.Warn("engine fire risk",ex);return true;}
    }

    private static EngineKind ResolveFireEngineKind(FireControlVehicleModule module,Il2CppSystem.Object? sender)
    {
        if(sender!=null)
        {
            if(sender.TryCast<FuelTankBehaviour>()!=null||sender.TryCast<FuelTank>()!=null)return EngineKind.Petrol;
            if(sender.TryCast<EngineBehaviour>() is {} eb&&RuntimeHooks.TryGetState(eb.Pointer,out var k,out _,out _,out _))return k;
            if(sender.TryCast<CombustionEngine>() is {} ce&&ce.Blueprint!=null)return Plans.EffectiveKind(ce.Blueprint);
        }
        var sources=module?.fireSources;
        if(sources==null)return EngineKind.Petrol;
        for(int i=0;i<sources.Length;i++)
        {
            var src=sources[i];
            if(src==null)continue;
            if(src.TryCast<CombustionEngine>() is {} engine&&engine.Blueprint!=null)
                return Plans.EffectiveKind(engine.Blueprint);
        }
        return EngineKind.Petrol;
    }
}
