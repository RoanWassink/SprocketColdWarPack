using HarmonyLib;
using Sprocket;
using Sprocket.ArtificialIntelligence;
using Sprocket.Orders;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Control;
using Sprocket.Vehicles.Orders.Construction;

namespace SprocketAIOrders;

[HarmonyPatch(typeof(CommanderAI), nameof(CommanderAI.Order))]
internal static class OrderGuard
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(CommanderAI __instance, Order __0)
    {
        try { return OrdersHost.Current?.Allows(__instance, __0) ?? true; }
        catch { return true; }
    }
}
// Native formation tasks stay enabled for native Follow; every other owned
// order keeps the native formation logic from re-attaching the unit.
[HarmonyPatch(typeof(CommanderAI), nameof(CommanderAI.UpdateFormationTasks))]
internal static class FormationGuard
{
    private static bool Prefix(CommanderAI __instance)
    {
        try { return !(OrdersHost.Current?.SuppressesFormation(__instance) ?? false); }
        catch { return true; }
    }
}
[HarmonyPatch(typeof(VehicleOrderConstruction), nameof(VehicleOrderConstruction.SelectConstructAndApply))]
internal static class ManualIngress
{
    private static void Prefix(IVehicleBehaviour __0, Il2CppSystem.Collections.Generic.List<IOrderReceiver> __2)
    {
        try
        {
            if (__0?.TryCast<VehicleBehaviour>()?.ControlType == ControlType.Player) OrdersHost.Current?.NativeCommand(__2);
        }
        catch (Exception ex) { Plugin.Warn("Native command observer: " + ex.Message); }
    }
}

