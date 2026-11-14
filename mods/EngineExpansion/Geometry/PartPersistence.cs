using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Serialization;
using SprocketEngineExpansion.Native;
namespace SprocketEngineExpansion.Geometry;

[HarmonyPatch]
internal static class PartPersistence
{
    internal static bool Contains(Il2CppSystem.Runtime.Serialization.SerializationInfo info,string key)
    {var values=info.GetEnumerator();while(values.MoveNext())if(values.Name==key)return true;return false;}

    // Native ToBlueprint inlines SaveData and calls SaveDataInternal directly. Hook the outer
    // snapshot, which feeds disk saves, vehicle clones and native ObjectTreeDestroy history.
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectSerialization),nameof(VehicleObjectSerialization.ToBlueprint))]
    private static void Snapshot(VehicleObject __0,VehicleObjectBlueprint __result)
    {
        var components=__0.Components;
        int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        try
        {
            for(int i=0;i<count;i++)
            {
                var component=components[i];if(!PartState.HasState(component)&&!AssemblyState.HasState(component))continue;
                var state=__result?.State??throw new InvalidOperationException("Owned turbo snapshot has no state; preservation refused.");
                PartState.Save(component,state);AssemblyState.Save(component,state);
            }
        }
        catch(Exception ex){Plugin.Warn("turbo snapshot preservation refused",ex);throw;}
    }

}
