using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Engines;
using SprocketEngineExpansion.Core;
namespace SprocketEngineExpansion.Geometry;

// Native hierarchy identifies ownership; route order is state of the owned model component.
[HarmonyPatch]
internal static class PartState
{
    internal const string Prefix="sprocketTurbo";
    internal const string Housing=Prefix+"Housing",EngineEnd=Prefix+"EngineEnd",TurboEnd=Prefix+"TurboEnd",Bend=Prefix+"Bend",Segment=Prefix+"Segment";
    private const string Key="sprocket.engineexpansion.route.v1";
    internal sealed record Entry(double Order,bool Editable,string? Raw,Il2CppSystem.Object? RawValue=null,bool PreserveRaw=false);
    private static readonly Dictionary<IntPtr,Entry> entries=new();
    internal static bool HasState(VehicleComponent model)=>Own(model)&&entries.ContainsKey(model.Pointer);
    internal static bool Own(VehicleComponent model)=>model.TryCast<VehicleObjectModel>()!=null&&new[]{Housing,EngineEnd,TurboEnd,Bend,Segment}.Contains(model.ComponentID);
    internal static Entry Read(VehicleComponent model)=>entries.TryGetValue(model.Pointer,out var state)?state:new(0,true,null);
    internal static void Set(VehicleComponent model,double order)
    {
        if(!Own(model)||!Read(model).Editable||!double.IsFinite(order))throw new ArgumentException("Unsupported pipe point state.");
        entries[model.Pointer]=new(order,true,RouteState.Encode(order));
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleComponent),nameof(VehicleComponent.SaveData))]
    internal static void Save(VehicleComponent __instance,Il2CppSystem.Runtime.Serialization.SerializationInfo __0)
    {
        if(!Own(__instance)||!entries.TryGetValue(__instance.Pointer,out var state))return;
        if(PartPersistence.Contains(__0,Key))return; // Direct SaveData and outer snapshot callers may share the info.
        if(state.PreserveRaw)__0.AddValue(Key,state.RawValue);else if(state.Raw!=null)__0.AddValue(Key,state.Raw); // Native object tree snapshots carry this same component state on undo/copy.
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleComponent),nameof(VehicleComponent.LoadData))]
    private static void Load(VehicleComponent __instance,Il2CppSystem.Runtime.Serialization.SerializationInfo __0)
    {
        if(!Own(__instance))return;entries.Remove(__instance.Pointer);
        var values=__0.GetEnumerator();string? raw=null;Il2CppSystem.Object? value=null;bool present=false;
        while(values.MoveNext())if(values.Name==Key){present=true;value=values.Value;break;}
        if(!present)return;
        try
        {
            raw=__0.GetString(Key);
            entries[__instance.Pointer]=new(RouteState.Decode(raw!),true,raw,value,true);
        }
        catch(Exception ex){entries[__instance.Pointer]=new(0,false,raw,value,true);Plugin.Warn("pipe point state preserved",ex);}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Release))]
    private static void Release(VehicleObjectModel __instance){if(Own(__instance))entries.Remove(__instance.Pointer);}
}

