using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Colliders;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Powertrains.Layouts;
using Sprocket.Vehicles.Transmissions;
using SprocketEngineExpansion.Core;
namespace SprocketEngineExpansion.Native;

/// <summary>
/// Targeted powerpack collision filter on VehicleColliderRegister.IsValidObstructionCandidate.
/// Ignores false overlaps inside an active powerpack (engine &lt;-&gt; own transmission(s),
/// twin left &lt;-&gt; right, and same-powertrain driveshafts &lt;-&gt; powerpack) while retaining
/// 100% of external collisions (hull, turret, crew, ammunition, fuel, other engines).
/// </summary>
[HarmonyPatch]
internal static class PowerpackCollision
{
    internal static bool Installed;
    internal static bool Enabled = true;
    internal static PowerpackDriveshaftCollisionMode DriveshaftMode = PowerpackDriveshaftCollisionMode.SamePowertrain;
    internal static bool Active => Installed && Enabled;

    private sealed class PowertrainIndex
    {
        internal IntPtr LayoutPointer;
        internal readonly Dictionary<IntPtr, PowerpackMember> Members = new();
        internal readonly Dictionary<int, HashSet<int>> GroupModules = new();
        internal IntPtr ShaftBuilder;
        internal readonly HashSet<int> ActiveEngineIds = new();
    }

    private static readonly Dictionary<IntPtr, PowertrainIndex> byPowertrain = new();
    private static readonly Dictionary<IntPtr, PowerpackMember> memberLookup = new();
    private static readonly Dictionary<IntPtr, IntPtr> shaftBuilderLookup = new();

    internal static void Refresh(VehiclePowertrain? powertrain)
    {
        if (powertrain == null) return;
        RemovePowertrainIndex(powertrain.Pointer);
        if (powertrain.VehicleObject == null || powertrain.VehicleObject.Released) return;
        var layout = powertrain.Layout;
        if (layout == null) return;

        var index = new PowertrainIndex { LayoutPointer = layout.Pointer };
        var builder = powertrain.driveshaftBuilder?.TryCast<DriveshaftBuilder>();
        if (builder != null)
        {
            index.ShaftBuilder = builder.Pointer;
            shaftBuilderLookup[builder.Pointer] = powertrain.Pointer;
        }

        foreach (var engine in PowerpackModel.Engines(powertrain))
        {
            if (engine.Blueprint == null) continue;
            var context = PowerpackLayout.Resolve(engine);
            if (context is not { Marked: true, Status: PowerpackStatus.Ready }) continue;
            if (!Selections.Get(engine.Blueprint).Powerpack && !PowerpackModel.Effective(engine)) continue;

            int engineModId = layout.GetPort(engine.OutputPortID)?.module?.id ?? -1;
            if (engineModId < 0) continue;

            var engineMember = new PowerpackMember(powertrain.Pointer, engineModId, engineModId);
            index.Members[engine.Pointer] = engineMember;
            memberLookup[engine.Pointer] = engineMember;

            var groupSet = new HashSet<int> { engineModId };
            if (context.SplitterId >= 0) groupSet.Add(context.SplitterId);

            foreach (int transModId in context.ModuleIds)
            {
                groupSet.Add(transModId);
                var module = layout.GetModule(transModId);
                if (module == null) continue;
                var block = PowerpackLayout.FindTransmission(powertrain, module);
                if (block == null) continue;
                var transMember = new PowerpackMember(powertrain.Pointer, engineModId, transModId);
                index.Members[block.Pointer] = transMember;
                memberLookup[block.Pointer] = transMember;
            }

            index.GroupModules[engineModId] = groupSet;
            index.ActiveEngineIds.Add(engineModId);
        }

        byPowertrain[powertrain.Pointer] = index;
    }

    internal static void Clear(IntPtr powertrainPtr) => RemovePowertrainIndex(powertrainPtr);

    internal static void RemoveComponent(IntPtr componentPtr) => memberLookup.Remove(componentPtr);

    internal static bool IsActiveFor(VehiclePowertrain? powertrain, int engineModuleId) =>
        Active && powertrain != null && byPowertrain.TryGetValue(powertrain.Pointer, out var idx) && idx.ActiveEngineIds.Contains(engineModuleId);

    internal static void MarkPowertrainCollidersModified(VehiclePowertrain? powertrain)
    {
        if (powertrain == null) return;
        try
        {
            var reader = powertrain.Vehicle?.ObjectReader;
            if (reader == null) return;
            var items = reader.Items;
            int n = items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for (int i = 0; i < n; i++)
            {
                var components = items[i].Components;
                int m = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for (int j = 0; j < m; j++)
                {
                    var comp = components[j];
                    if (!BelongsToPowertrain(comp, powertrain)) continue;
                    var colliders = comp.Colliders;
                    int nc = colliders.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<IVehicleCollider>>().Count;
                    for (int k = 0; k < nc; k++)
                    {
                        var c = colliders[k].TryCast<VehicleCollider>();
                        if (c != null && !c.Released) c.MarkModified();
                    }
                }
            }
        }
        catch (Exception ex) { Plugin.Warn("powerpack mark colliders", ex); }
    }

    private static bool BelongsToPowertrain(VehicleComponent comp, VehiclePowertrain powertrain)
    {
        if (comp.TryCast<CombustionEngine>() is { } eng)
            return eng.PowertrainNode?.TryCast<PowertrainComponent>()?.layout?.Pointer == powertrain.Pointer;
        if (comp.TryCast<TransmissionBlock>() is { } tb)
            return tb.powertrainNode?.TryCast<PowertrainComponent>()?.layout?.Pointer == powertrain.Pointer;
        if (comp.TryCast<DriveshaftBuilder>() is { } db)
            return db.Pointer == powertrain.driveshaftBuilder?.Pointer || (shaftBuilderLookup.TryGetValue(db.Pointer, out var pt) && pt == powertrain.Pointer);
        return false;
    }

    private static void RemovePowertrainIndex(IntPtr powertrainPtr)
    {
        if (!byPowertrain.Remove(powertrainPtr, out var old)) return;
        foreach (var memberPtr in old.Members.Keys) memberLookup.Remove(memberPtr);
        if (old.ShaftBuilder != IntPtr.Zero) shaftBuilderLookup.Remove(old.ShaftBuilder);
    }

    private static void EnsureIndexed(VehicleComponent owner)
    {
        VehiclePowertrain? pt = null;
        if (owner.TryCast<CombustionEngine>() is { } eng)
            pt = eng.PowertrainNode?.TryCast<PowertrainComponent>()?.layout?.TryCast<VehiclePowertrain>();
        else if (owner.TryCast<TransmissionBlock>() is { } tb)
            pt = tb.powertrainNode?.TryCast<PowertrainComponent>()?.layout?.TryCast<VehiclePowertrain>();
        if (pt == null) return;
        var currentLayout = pt.Layout?.Pointer ?? IntPtr.Zero;
        if (!byPowertrain.TryGetValue(pt.Pointer, out var idx) || idx.LayoutPointer != currentLayout)
            Refresh(pt);
    }

    private static (int Provider, int Receiver)? ResolveShaftEndpoints(VehicleCollider collider, VehicleComponent owner)
    {
        if (owner.TryCast<DriveshaftBuilder>() is not { } builder) return null;
        var layout = builder.Layout;
        if (layout == null) return null;
        int connId = builder.GetDriveshaftConnectionIDByCollider(collider.Cast<IVehicleCollider>());
        if (connId >= 0)
        {
            var conn = layout.GetConnection(connId);
            var prov = conn?.provider?.module;
            var recv = conn?.receiver?.module;
            if (prov != null && recv != null) return (prov.id, recv.id);
            return null;
        }
        var junctions = builder.driveShaftJunctions;
        if (junctions != null)
        {
            for (int i = 0; i < junctions.Length; i++)
            {
                var j = junctions[i];
                if (j?.collider?.Pointer == collider.Pointer)
                    return (j.layoutModuleID, j.layoutModuleID);
            }
        }
        return null;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleColliderRegister), nameof(VehicleColliderRegister.IsValidObstructionCandidate))]
    private static void Filter(VehicleCollider __0, VehicleCollider __1, ref bool __result)
    {
        if (!Active || !__result || __0 == null || __1 == null) return;
        byte la = (byte)__0.Layer, lb = (byte)__1.Layer;
        if (!PowerpackCollisionRules.IsPowertrainLayer(la) || !PowerpackCollisionRules.IsPowertrainLayer(lb)) return;
        try
        {
            var ownerA = __0.Owner;
            var ownerB = __1.Owner;
            if (ownerA == null || ownerB == null) return;
            EnsureIndexed(ownerA);
            EnsureIndexed(ownerB);
            if (PowerpackCollisionRules.ShouldIgnore(
                la, ownerA.Pointer,
                lb, ownerB.Pointer,
                ptr => memberLookup.TryGetValue(ptr, out var m) ? m : null,
                ptr => shaftBuilderLookup.TryGetValue(ptr, out var pt) ? pt : null,
                shaftIsA => ResolveShaftEndpoints(shaftIsA ? __0 : __1, shaftIsA ? ownerA : ownerB),
                (pt, key) => byPowertrain.TryGetValue(pt, out var idx) && idx.GroupModules.TryGetValue(key, out var set) ? set : null,
                ignoreInternal: true,
                driveshaftMode: DriveshaftMode))
            {
                __result = false;
            }
        }
        catch (Exception ex) { Plugin.Warn("powerpack collision filter", ex); }
    }
}
