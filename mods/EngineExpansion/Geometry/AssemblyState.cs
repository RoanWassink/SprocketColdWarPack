using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Engines;
using System.Text.Json;
using SprocketEngineExpansion.Core;
namespace SprocketEngineExpansion.Geometry;

[HarmonyPatch]
internal static class AssemblyState
{
    private const string Key="sprocket.engineexpansion.assembly.v1";
    internal sealed record Entry(bool Independent=false,bool Editable=true,string? Raw=null,Il2CppSystem.Object? Native=null,bool Preserve=false,int? OriginalOptions=null);
    private static readonly Dictionary<IntPtr,Entry> entries=new();
    private static readonly HashSet<IntPtr> attempted=new();
    private static readonly HashSet<IntPtr> missingAfterLoad=new();
    internal static bool MissingAfterLoad(CombustionEngine engine)=>missingAfterLoad.Contains(engine.Pointer);
    internal static bool HasState(VehicleComponent component)=>component.TryCast<CombustionEngine>() is {} engine&&entries.ContainsKey(engine.Pointer);
    internal static Entry Read(CombustionEngine engine)=>entries.TryGetValue(engine.Pointer,out var e)?e:new();
    internal static void Restore(CombustionEngine engine,Entry state)=>entries[engine.Pointer]=state;
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleComponent),nameof(VehicleComponent.SaveData))]
    internal static void Save(VehicleComponent __instance,Il2CppSystem.Runtime.Serialization.SerializationInfo __0)
    {
        if(__instance.TryCast<CombustionEngine>() is not {} engine||!entries.TryGetValue(engine.Pointer,out var state))return;
        if(PartPersistence.Contains(__0,Key))return;
        if(state.Preserve)__0.AddValue(Key,state.Native);else if(state.Raw!=null)__0.AddValue(Key,state.Raw);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleComponent),nameof(VehicleComponent.LoadData))]
    private static void Load(VehicleComponent __instance,Il2CppSystem.Runtime.Serialization.SerializationInfo __0)
    {
        if(__instance.TryCast<CombustionEngine>() is not {} engine)return;
        entries.Remove(engine.Pointer);attempted.Remove(engine.Pointer);missingAfterLoad.Remove(engine.Pointer);
        var values=__0.GetEnumerator();Il2CppSystem.Object? native=null;bool found=false;
        while(values.MoveNext())if(values.Name==Key){native=values.Value;found=true;break;}
        if(!found){missingAfterLoad.Add(engine.Pointer);return;}
        string? raw=null;
        try
        {
            raw=__0.GetString(Key);var marker=AssemblyMarker.Decode(raw!);
            entries[engine.Pointer]=new(marker.Independent,true,raw,native,true,marker.OriginalOptions);
        }
        catch(Exception ex){entries[engine.Pointer]=new(false,false,raw,native,true);Plugin.Warn("assembly metadata preserved",ex);}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(CombustionEngine),nameof(CombustionEngine.Release))]
    private static void Release(CombustionEngine __instance){entries.Remove(__instance.Pointer);attempted.Remove(__instance.Pointer);missingAfterLoad.Remove(__instance.Pointer);}
}
