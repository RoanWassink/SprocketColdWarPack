using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.CrewSystems;
using Sprocket.Vehicles.Powertrains.Layouts;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;
using UnityEngine;
using UnityEngine.Events;
using SerializationInfo = Il2CppSystem.Runtime.Serialization.SerializationInfo;

namespace SprocketThermalSight;

internal static class ConsoleLinks
{
    internal const string Id = "sightConsole";
    internal const string Guid = "ef0537bb-b57e-4e96-a65c-48f757a5644d";
    internal const string DisplayFcsId = "fcsDisplayConsole";
    internal const string DisplayFcsGuid = "8e31c4a9-2f6b-4a1d-b835-9d6a1e7c3f02";
    internal const string DriverId = "driverConsole";
    internal const string DriverGuid = "4c92a7f3-8b1e-4d5a-9e21-7f4b2c8d1a03";
    internal const string CommanderId = "commanderConsole";
    internal const string CommanderGuid = "6a4d8e21-3c7f-4b92-a156-8e0d4f9c2b17";
    internal const string CommanderSightId = "commanderPanoramicSight";
    internal const string CommanderSightGuid = "9b72e4c1-5a8d-4f3e-b209-1d6c8a4e7f53";
    internal const string CommanderSightModelId = "commanderPanoramicSightModel";
    internal const string OesOpModelId = "oesOpSightModel";

    private sealed record Link(VehicleComponent Owner, int Target);
    private static readonly Dictionary<IntPtr, Link> links = new();
    private static readonly HashSet<IntPtr> routed = new();
    private static readonly Dictionary<IntPtr, int> reported = new();
    private static int logs;
    private static float scanAt;

    internal static bool IsGunnerConsole(VehicleComponent c) =>
        c != null && (c.ComponentID == Id || c.ComponentID == DisplayFcsId) && c.TryCast<VehicleObjectModel>() != null;
    internal static bool IsDriverConsole(VehicleComponent c) =>
        c != null && c.ComponentID == DriverId && c.TryCast<VehicleObjectModel>() != null;
    internal static bool IsCommanderConsole(VehicleComponent c) =>
        c != null && c.ComponentID == CommanderId && c.TryCast<VehicleObjectModel>() != null;
    internal static bool IsDisplayModel(VehicleComponent c) =>
        c != null && (c.ComponentID == DisplayFcsId || c.ComponentID == DriverId || c.ComponentID == CommanderId) && c.TryCast<VehicleObjectModel>() != null;
    internal static bool IsCommanderSightModel(VehicleComponent c) =>
        c != null && c.ComponentID == CommanderSightModelId && c.TryCast<VehicleObjectModel>() != null;
    internal static bool IsOesOpModel(VehicleComponent c) =>
        c != null && c.ComponentID == OesOpModelId && c.TryCast<VehicleObjectModel>() != null;
    internal static bool IsAnyConsole(VehicleComponent c) => IsGunnerConsole(c) || IsDriverConsole(c) || IsCommanderConsole(c);
    internal static bool IsConsole(VehicleComponent c) => IsGunnerConsole(c);

    internal static bool IsLinkable(VehicleComponent c) =>
        c != null && (c.TryCast<GunnerSight>() != null || c.TryCast<LayingDrive>() != null ||
                      c.TryCast<TraverseMotor>() != null || c.TryCast<PowertrainSteeringControls>() != null ||
                      c.TryCast<CommanderCupola>() != null || c.TryCast<Viewport>() != null);

    internal static IEnumerable<VehicleComponent> Components(VehicleComponent owner)
    {
        var items = owner.Vehicle?.ObjectReader?.Items; if (items == null) yield break;
        int n = items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for (int i = 0; i < n; i++)
        {
            var obj = items[i]; if (obj == null || obj.Released) continue;
            var list = obj.Components; if (list == null) continue;
            int m = list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for (int j = 0; j < m; j++) if (list[j] != null && list[j].Vehicle?.Pointer == owner.Vehicle?.Pointer) yield return list[j];
        }
    }

    internal static int Target(VehicleComponent source) =>
        links.TryGetValue(source.Pointer, out var link) && link.Owner.Pointer == source.Pointer ? link.Target : -1;

    internal static VehicleComponent? Resolve(VehicleComponent source) => ResolveGunner(source);

    internal static VehicleComponent? ResolveGunner(VehicleComponent source)
    {
        int target = Target(source); if (target < 0) return null;
        var found = Components(source).Where(c => IsGunnerConsole(c) && c.IsInstalled && c.VUID.Value == target && ThermalEraAccess.Allowed(c)).Take(2).ToArray();
        return found.Length == 1 ? found[0] : null;
    }

    internal static VehicleComponent? ResolveDriver(VehicleComponent source)
    {
        int target = Target(source); if (target < 0) return null;
        var found = Components(source).Where(c => IsDriverConsole(c) && c.IsInstalled && c.VUID.Value == target && ThermalEraAccess.Allowed(c)).Take(2).ToArray();
        return found.Length == 1 ? found[0] : null;
    }

    internal static VehicleComponent? ResolveCommander(VehicleComponent source)
    {
        int target = Target(source); if (target < 0) return null;
        var found = Components(source).Where(c => IsCommanderConsole(c) && c.IsInstalled && c.VUID.Value == target && ThermalEraAccess.Allowed(c)).Take(2).ToArray();
        return found.Length == 1 ? found[0] : null;
    }

    private static Vector3 ConsoleWorldPosition(VehicleComponent console) =>
        console.VehicleTransform.LocalToWorldSpace(IsDisplayModel(console) ? new Vector3(0f, 0.16f, 0.06f) : new Vector3(0f, 0f, 0.06f));

    private static string ConsoleLabel(VehicleComponent c) =>
        c.ComponentID == DisplayFcsId ? "FCS Display #" + c.VUID.Value
        : c.ComponentID == DriverId ? "Driver Console #" + c.VUID.Value
        : c.ComponentID == CommanderId ? "Commander Console #" + c.VUID.Value
        : "FCS #" + c.VUID.Value;

    internal static bool Profile(GunnerSight sight, out ThermalProfile profile)
    {
        profile = null!;
        // Console supplies a remote eyepiece, never the sensor's imaging capability.
        if (Target(sight) >= 0 && ResolveGunner(sight) == null) return false;
        return Runtime.FindProfile(sight, out profile);
    }

    internal static void Select(GunnerSight sight, int target)
    {
        if (target < 0) links.Remove(sight.Pointer); else links[sight.Pointer] = new(sight, target);
        Runtime.Deactivate();
        foreach (var c in Components(sight))
            if (c.TryCast<Cannon>() is { } cannon && cannon.AssignedSightVUID.Value == sight.VUID.Value) { Apply(cannon); cannon.RequestRebuild(); }
    }

    internal static void SelectLayingDrive(LayingDrive drive, int target)
    {
        if (target < 0) links.Remove(drive.Pointer); else links[drive.Pointer] = new(drive, target);
        ApplyLayingDrive(drive);
    }

    internal static void SelectTraverse(TraverseMotor motor, int target)
    {
        if (target < 0) links.Remove(motor.Pointer); else links[motor.Pointer] = new(motor, target);
        ApplyTraverse(motor);
        if (target < 0) motor.RequestRebuild();
    }

    internal static void SelectDriver(PowertrainSteeringControls controls, int target)
    {
        if (target < 0) links.Remove(controls.Pointer); else links[controls.Pointer] = new(controls, target);
        ApplyDriver(controls);
        controls.ValidateViewportLineOfSight();
    }

    internal static void SelectCommander(VehicleComponent source, int target)
    {
        if (target < 0) links.Remove(source.Pointer); else links[source.Pointer] = new(source, target);
        if (source.TryCast<CommanderCupola>() is { } cupola) ApplyCommanderCupola(cupola);
        else if (source.TryCast<Viewport>() is { } vp) ApplyCommanderViewport(vp);
    }

    internal static void Tick()
    {
        if (!Runtime.HasVehicleContext || Time.unscaledTime < scanAt) return; scanAt = Time.unscaledTime + .25f;
        foreach (var vehicle in UnityEngine.Object.FindObjectsOfType<Vehicle>())
        {
            if (vehicle == null || vehicle.Released) continue;
            var items = vehicle.ObjectReader.Items;
            int n = items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for (int i = 0; i < n; i++)
            {
                var obj = items[i]; if (obj == null || obj.Released) continue;
                var list = obj.Components; if (list == null) continue;
                int m = list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for (int j = 0; j < m; j++)
                {
                    var comp = list[j]; if (comp == null) continue;
                    if (comp.TryCast<Cannon>() is { } cannon) Apply(cannon);
                    else if (comp.TryCast<LayingDrive>() is { } drive) ApplyLayingDrive(drive);
                    else if (comp.TryCast<TraverseMotor>() is { } motor) ApplyTraverse(motor);
                    else if (comp.TryCast<PowertrainSteeringControls>() is { } controls) ApplyDriver(controls);
                    else if (comp.TryCast<CommanderCupola>() is { } cupola) ApplyCommanderCupola(cupola);
                    else if (comp.TryCast<Viewport>() is { } vp) ApplyCommanderViewport(vp);
                }
            }
        }
    }

    private static void Apply(Cannon cannon)
    {
        if (cannon.Operable == null || cannon.VehicleObject.Released) return;
        var candidates = Components(cannon).Where(c => c.VUID.Value == cannon.AssignedSightVUID.Value && c.TryCast<GunnerSight>() != null).Take(2).ToArray();
        if (candidates.Length != 1) return;
        var sight = candidates[0].TryCast<GunnerSight>()!;
        var console = ResolveGunner(sight);
        if (console == null && !routed.Contains(cannon.Pointer)) return;
        // Exactly the native sight-to-cannon coordinate conversion; only the endpoint changes.
        // All native eyes/hands/feet distances and operator assignment remain intact.
        var world = console != null ? ConsoleWorldPosition(console) : sight.VehicleTransform.LocalToWorldSpace(sight.EyepieceLocalPosition);
        var local = cannon.VehicleTransform.ToLocalSpace(world);
        if ((cannon.Operable.OperateLocalPosition - local).sqrMagnitude > 1e-8f)
        { cannon.Operable.operateLocalPosition = local; cannon.Operable.MarkModified(); }
        if (console != null) routed.Add(cannon.Pointer); else routed.Remove(cannon.Pointer);
        int endpoint = console?.VUID.Value ?? -1;
        if (logs < 16 && (!reported.TryGetValue(cannon.Pointer, out int previous) || previous != endpoint))
        {
            reported[cannon.Pointer] = endpoint; logs++;
            Plugin.Instance.Log.LogInfo($"[Sight console] cannon={cannon.VUID.Value}; sight={sight.VUID.Value}; console={endpoint}; native reach={cannon.Operable.MinimumEyesDistance}; endpoint={local}");
        }
    }

    private static void ApplyLayingDrive(LayingDrive drive)
    {
        if (drive.Operable == null || drive.VehicleObject.Released) return;
        var console = ResolveGunner(drive);
        if (console == null && !routed.Contains(drive.Pointer)) return;
        var local = console != null ? drive.VehicleTransform.ToLocalSpace(ConsoleWorldPosition(console)) : drive.OperateLocalPosition;
        if ((drive.Operable.OperateLocalPosition - local).sqrMagnitude > 1e-8f)
        { drive.Operable.operateLocalPosition = local; drive.Operable.MarkModified(); }
        if (console != null) routed.Add(drive.Pointer); else routed.Remove(drive.Pointer);
        int endpoint = console?.VUID.Value ?? -1;
        if (logs < 16 && (!reported.TryGetValue(drive.Pointer, out int previous) || previous != endpoint))
        {
            reported[drive.Pointer] = endpoint; logs++;
            Plugin.Instance.Log.LogInfo($"[FCS drive] layingDrive={drive.VUID.Value}; console={endpoint}; native handReach={drive.Operable.MinimumHandDistance}; endpoint={local}");
        }
    }

    private static void ApplyTraverse(TraverseMotor motor)
    {
        if (motor.Operable == null || motor.VehicleObject.Released) return;
        var console = ResolveGunner(motor);
        if (console == null && !routed.Contains(motor.Pointer)) return;
        var local = console != null ? motor.VehicleTransform.ToLocalSpace(ConsoleWorldPosition(console)) : motor.OperateLocalPosition;
        if ((motor.Operable.OperateLocalPosition - local).sqrMagnitude > 1e-8f)
        { motor.Operable.operateLocalPosition = local; motor.Operable.MarkModified(); }
        if (console != null) routed.Add(motor.Pointer); else routed.Remove(motor.Pointer);
        int endpoint = console?.VUID.Value ?? -1;
        if (logs < 16 && (!reported.TryGetValue(motor.Pointer, out int previous) || previous != endpoint))
        {
            reported[motor.Pointer] = endpoint; logs++;
            Plugin.Instance.Log.LogInfo($"[FCS drive] traverseMotor={motor.VUID.Value}; console={endpoint}; native handReach={motor.Operable.MinimumHandDistance}; endpoint={local}");
        }
    }

    private static Viewport? AssignedViewport(PowertrainSteeringControls controls)
    {
        int vpId = controls.assignedViewportVUID.Value;
        if (vpId < 0) return null;
        var found = Components(controls).Where(c => c.VUID.Value == vpId && c.TryCast<Viewport>() != null).Take(2).ToArray();
        return found.Length == 1 ? found[0].TryCast<Viewport>() : null;
    }

    private static void ApplyDriver(PowertrainSteeringControls controls)
    {
        if (controls.Operable == null || controls.VehicleObject.Released) return;
        var console = ResolveDriver(controls);
        if (console == null && !routed.Contains(controls.Pointer)) return;
        Vector3 local;
        if (console != null)
        {
            local = controls.VehicleTransform.ToLocalSpace(ConsoleWorldPosition(console));
        }
        else
        {
            var vp = AssignedViewport(controls);
            local = vp != null
                ? controls.VehicleTransform.ToLocalSpace(vp.VehicleTransform.LocalToWorldSpace(vp.EyepieceLocalPosition))
                : Vector3.zero;
        }
        if ((controls.Operable.OperateLocalPosition - local).sqrMagnitude > 1e-8f)
        { controls.Operable.operateLocalPosition = local; controls.Operable.MarkModified(); }
        if (console != null) routed.Add(controls.Pointer); else routed.Remove(controls.Pointer);
        int endpoint = console?.VUID.Value ?? -1;
        if (logs < 16 && (!reported.TryGetValue(controls.Pointer, out int previous) || previous != endpoint))
        {
            reported[controls.Pointer] = endpoint; logs++;
            Plugin.Instance.Log.LogInfo($"[Driver console] steering={controls.VUID.Value}; viewport={controls.assignedViewportVUID.Value}; console={endpoint}; endpoint={local}");
        }
    }

    private static void ValidateDriverViewport(PowertrainSteeringControls controls)
    {
        if (controls == null || controls.VehicleObject.Released) return;
        if (ResolveDriver(controls) == null) return;
        var vp = AssignedViewport(controls);
        if (vp != null && vp.CheckViewportClear())
        {
            controls.viewportValid = true;
            controls.viewportObstructionDistance = float.PositiveInfinity;
        }
    }

    private static void ApplyCommanderCupola(CommanderCupola cupola)
    {
        if (cupola.Operable == null || cupola.VehicleObject.Released) return;
        var console = ResolveCommander(cupola);
        if (console == null && !routed.Contains(cupola.Pointer)) return;
        var local = console != null ? cupola.VehicleTransform.ToLocalSpace(ConsoleWorldPosition(console)) : cupola.EyepieceLocalPosition;
        if ((cupola.Operable.OperateLocalPosition - local).sqrMagnitude > 1e-8f)
        { cupola.Operable.operateLocalPosition = local; cupola.Operable.MarkModified(); }
        if (console != null) routed.Add(cupola.Pointer); else routed.Remove(cupola.Pointer);
        int endpoint = console?.VUID.Value ?? -1;
        if (logs < 16 && (!reported.TryGetValue(cupola.Pointer, out int previous) || previous != endpoint))
        {
            reported[cupola.Pointer] = endpoint; logs++;
            Plugin.Instance.Log.LogInfo($"[Commander console] cupola={cupola.VUID.Value}; console={endpoint}; endpoint={local}");
        }
    }

    private static void ApplyCommanderViewport(Viewport vp)
    {
        if (vp.Operable == null || vp.VehicleObject.Released) return;
        var console = ResolveCommander(vp);
        if (console == null && !routed.Contains(vp.Pointer)) return;
        var local = console != null ? vp.VehicleTransform.ToLocalSpace(ConsoleWorldPosition(console)) : vp.EyepieceLocalPosition;
        if ((vp.Operable.OperateLocalPosition - local).sqrMagnitude > 1e-8f)
        { vp.Operable.operateLocalPosition = local; vp.Operable.MarkModified(); }
        if (console != null) routed.Add(vp.Pointer); else routed.Remove(vp.Pointer);
        int endpoint = console?.VUID.Value ?? -1;
        if (logs < 16 && (!reported.TryGetValue(vp.Pointer, out int previous) || previous != endpoint))
        {
            reported[vp.Pointer] = endpoint; logs++;
            Plugin.Instance.Log.LogInfo($"[Commander console] viewport={vp.VUID.Value}; console={endpoint}; endpoint={local}");
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Cannon), nameof(Cannon.UpdateAssociatedSight))]
    private static void Associated(Cannon __instance) { try { Apply(__instance); } catch (Exception ex) { Runtime.Warn("Console endpoint: " + ex.Message); } }
    [HarmonyPostfix, HarmonyPatch(typeof(Cannon), nameof(Cannon.UpdateSightTransform))]
    private static void Moved(Cannon __instance) { try { Apply(__instance); } catch (Exception ex) { Runtime.Warn("Console movement: " + ex.Message); } }

    // 0.5.5: four unique native hooks (verified Shared=1 in build 26757044) for remote traverse/laying drives and driver console.
    [HarmonyPostfix, HarmonyPatch(typeof(TraverseMotor), nameof(TraverseMotor.Build))]
    private static void TraverseBuilt(TraverseMotor __instance) { try { ApplyTraverse(__instance); } catch (Exception ex) { Runtime.Warn("FCS traverse endpoint: " + ex.Message); } }
    [HarmonyPostfix, HarmonyPatch(typeof(PowertrainSteeringControls), nameof(PowertrainSteeringControls.SetAssignedViewport))]
    private static void DriverViewportSet(PowertrainSteeringControls __instance) { try { ApplyDriver(__instance); } catch (Exception ex) { Runtime.Warn("Driver console endpoint: " + ex.Message); } }
    [HarmonyPostfix, HarmonyPatch(typeof(PowertrainSteeringControls), nameof(PowertrainSteeringControls.OnViewportTransformChanged))]
    private static void DriverViewportMoved(PowertrainSteeringControls __instance) { try { ApplyDriver(__instance); } catch (Exception ex) { Runtime.Warn("Driver console movement: " + ex.Message); } }
    [HarmonyPostfix, HarmonyPatch(typeof(PowertrainSteeringControls), nameof(PowertrainSteeringControls.ValidateViewportLineOfSight))]
    private static void DriverViewportValidated(PowertrainSteeringControls __instance) { try { ValidateDriverViewport(__instance); } catch (Exception ex) { Runtime.Warn("Driver console LOS: " + ex.Message); } }

    internal static void Restore(VehicleComponent source, int target)
    {
        if (target < 0) links.Remove(source.Pointer); else links[source.Pointer] = new(source, target);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.LoadData))]
    private static void Load(VehicleComponent __instance, SerializationInfo __0, Sprocket.StateContext __1)
    {
        if (!IsLinkable(__instance)) return;
        try
        {
            Restore(__instance, -1);
            // Copy/paste is restored through the batch's explicit VUID map, never raw IDs.
            if (!__1.LoadIDs) return;
            var decoded = ConsolePersistence.Read(__0);
            // Canonical rows restore only after the full native allocation batch.
            if (!decoded.Canonical && decoded.Legacy >= 0 && __instance.TryCast<GunnerSight>() != null) Restore(__instance, decoded.Legacy);
        }
        catch (Exception ex) { Runtime.Warn("Console link load: " + ex.Message); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.ReleaseInternal))]
    private static void Release(VehicleComponent __instance) { links.Remove(__instance.Pointer); routed.Remove(__instance.Pointer); reported.Remove(__instance.Pointer); }

    internal static void Clear()
    {
        var owners = links.Values.Select(l => l.Owner).ToArray(); links.Clear();
        foreach (var owner in owners)
            try
            {
                if (owner.VehicleObject.Released) continue;
                if (owner.TryCast<GunnerSight>() != null)
                { foreach (var c in Components(owner)) if (c.TryCast<Cannon>() is { } cannon) Apply(cannon); }
                else if (owner.TryCast<LayingDrive>() is { } drive) ApplyLayingDrive(drive);
                else if (owner.TryCast<TraverseMotor>() is { } motor) ApplyTraverse(motor);
                else if (owner.TryCast<PowertrainSteeringControls>() is { } controls) ApplyDriver(controls);
                else if (owner.TryCast<CommanderCupola>() is { } cupola) ApplyCommanderCupola(cupola);
                else if (owner.TryCast<Viewport>() is { } vp) ApplyCommanderViewport(vp);
            }
            catch (Exception ex) { Runtime.Warn("Console unload restore: " + ex.Message); }
        routed.Clear(); reported.Clear();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectEditor), nameof(VehicleObjectEditor.OnGUI))]
    private static void Inspector(VehicleObjectEditor __instance, IGUILayout __0)
    {
        try
        {
            var obj = __instance.target; if (obj == null) return;
            var ui = __0.TryCast<IGUIElementDrawer>(); if (ui == null) return;
            var sight = obj.GetBehaviour<GunnerSight>();
            var drive = obj.GetBehaviour<LayingDrive>();
            var motor = obj.GetBehaviour<TraverseMotor>();
            var steering = obj.GetBehaviour<PowertrainSteeringControls>();
            var cupola = obj.GetBehaviour<CommanderCupola>();
            var viewport = obj.GetBehaviour<Viewport>();
            var model = obj.GetBehaviour<VehicleObjectModel>();
            if (sight != null)
            {
                var consoles = Components(sight).Where(c => IsGunnerConsole(c) && ThermalEraAccess.Allowed(c)).ToArray();
                var names = new Il2CppSystem.Collections.Generic.List<string>(); names.Add("Direct eyepiece (no console)");
                foreach (var c in consoles) names.Add(ConsoleLabel(c));
                int index = Array.FindIndex(consoles, c => c.VUID.Value == Target(sight)) + 1;
                ui.Dropdown("FCS", names.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), index,
                    DelegateSupport.ConvertDelegate<UnityAction<int>>((Action<int>)(v => { if (v < 0 || v > consoles.Length || sight.VehicleObject.Released) return; Select(sight, v == 0 ? -1 : consoles[v - 1].VUID.Value); __instance.RequestRedraw(); }))!,
                    "Crew operates this sight at the linked console. No console-to-sight distance limit. Assign this external sight to the cannon as usual.");
                var assigned = Components(sight).Where(c => c.TryCast<Cannon>() is { } cannon && cannon.AssignedSightVUID.Value == sight.VUID.Value).ToArray();
                if (Target(sight) >= 0 && assigned.Length == 0)
                    ui.InfoField("This sight is linked to FCS but is not assigned to a cannon. Assign this sight on the cannon; FCS linking does not change the cannon's selected sight.", 2);
                if (Target(sight) >= 0 && ResolveGunner(sight) == null) ui.InfoField("Linked console missing: thermal disabled. Select a valid console or direct eyepiece.", 2);
            }
            else if (drive != null)
            {
                var consoles = Components(drive).Where(c => IsGunnerConsole(c) && ThermalEraAccess.Allowed(c)).ToArray();
                var names = new Il2CppSystem.Collections.Generic.List<string>(); names.Add("Direct operation (no console)");
                foreach (var c in consoles) names.Add(ConsoleLabel(c));
                int index = Array.FindIndex(consoles, c => c.VUID.Value == Target(drive)) + 1;
                ui.Dropdown("FCS", names.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), index,
                    DelegateSupport.ConvertDelegate<UnityAction<int>>((Action<int>)(v => { if (v < 0 || v > consoles.Length || drive.VehicleObject.Released) return; SelectLayingDrive(drive, v == 0 ? -1 : consoles[v - 1].VUID.Value); __instance.RequestRedraw(); }))!,
                    "Gunner operates this elevation/laying drive at the linked FCS console. No console-to-drive distance limit.");
                if (Target(drive) >= 0 && ResolveGunner(drive) == null) ui.InfoField("Linked FCS console missing. Select a valid console or direct operation.", 2);
            }
            else if (motor != null)
            {
                var consoles = Components(motor).Where(c => IsGunnerConsole(c) && ThermalEraAccess.Allowed(c)).ToArray();
                var names = new Il2CppSystem.Collections.Generic.List<string>(); names.Add("Direct operation (no console)");
                foreach (var c in consoles) names.Add(ConsoleLabel(c));
                int index = Array.FindIndex(consoles, c => c.VUID.Value == Target(motor)) + 1;
                ui.Dropdown("FCS", names.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), index,
                    DelegateSupport.ConvertDelegate<UnityAction<int>>((Action<int>)(v => { if (v < 0 || v > consoles.Length || motor.VehicleObject.Released) return; SelectTraverse(motor, v == 0 ? -1 : consoles[v - 1].VUID.Value); __instance.RequestRedraw(); }))!,
                    "Gunner operates this turret traverse drive at the linked FCS console. No console-to-drive distance limit.");
                if (Target(motor) >= 0 && ResolveGunner(motor) == null) ui.InfoField("Linked FCS console missing. Select a valid console or direct operation.", 2);
            }
            else if (steering != null)
            {
                var consoles = Components(steering).Where(c => IsDriverConsole(c) && ThermalEraAccess.Allowed(c)).ToArray();
                var names = new Il2CppSystem.Collections.Generic.List<string>(); names.Add("Direct viewport (no console)");
                foreach (var c in consoles) names.Add(ConsoleLabel(c));
                int index = Array.FindIndex(consoles, c => c.VUID.Value == Target(steering)) + 1;
                ui.Dropdown("Driver Console", names.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), index,
                    DelegateSupport.ConvertDelegate<UnityAction<int>>((Action<int>)(v => { if (v < 0 || v > consoles.Length || steering.VehicleObject.Released) return; SelectDriver(steering, v == 0 ? -1 : consoles[v - 1].VUID.Value); __instance.RequestRedraw(); }))!,
                    "Driver operates steering and views the assigned viewport at the linked Driver Console. No console-to-viewport distance limit; external viewport must still be clear.");
                if (Target(steering) >= 0 && ResolveDriver(steering) == null) ui.InfoField("Linked Driver Console missing. Select a valid console or direct viewport.", 2);
            }
            else if (cupola != null)
            {
                var consoles = Components(cupola).Where(c => IsCommanderConsole(c) && ThermalEraAccess.Allowed(c)).ToArray();
                var names = new Il2CppSystem.Collections.Generic.List<string>(); names.Add("Direct cupola/sight (no console)");
                foreach (var c in consoles) names.Add(ConsoleLabel(c));
                int index = Array.FindIndex(consoles, c => c.VUID.Value == Target(cupola)) + 1;
                ui.Dropdown("Commander Console", names.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), index,
                    DelegateSupport.ConvertDelegate<UnityAction<int>>((Action<int>)(v => { if (v < 0 || v > consoles.Length || cupola.VehicleObject.Released) return; SelectCommander(cupola, v == 0 ? -1 : consoles[v - 1].VUID.Value); __instance.RequestRedraw(); }))!,
                    "Commander operates this cupola or remote panoramic sight at the linked Commander Console. No console-to-sight distance limit.");
                if (Target(cupola) >= 0 && ResolveCommander(cupola) == null) ui.InfoField("Linked Commander Console missing. Select a valid console or direct cupola.", 2);
            }
            else if (viewport != null)
            {
                var consoles = Components(viewport).Where(c => IsCommanderConsole(c) && ThermalEraAccess.Allowed(c)).ToArray();
                var names = new Il2CppSystem.Collections.Generic.List<string>(); names.Add("Direct viewport (no console)");
                foreach (var c in consoles) names.Add(ConsoleLabel(c));
                int index = Array.FindIndex(consoles, c => c.VUID.Value == Target(viewport)) + 1;
                ui.Dropdown("Commander Console", names.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), index,
                    DelegateSupport.ConvertDelegate<UnityAction<int>>((Action<int>)(v => { if (v < 0 || v > consoles.Length || viewport.VehicleObject.Released) return; SelectCommander(viewport, v == 0 ? -1 : consoles[v - 1].VUID.Value); __instance.RequestRedraw(); }))!,
                    "Commander operates this viewport at the linked Commander Console. No console-to-viewport distance limit.");
                if (Target(viewport) >= 0 && ResolveCommander(viewport) == null) ui.InfoField("Linked Commander Console missing. Select a valid console or direct viewport.", 2);
            }
            else if (model != null && IsGunnerConsole(model))
            {
                ui.Header(ConsoleLabel(model));
                ui.InfoField("Link external sights and remote laying/traverse drives below. Place this console within the gunner's normal operating reach.", 2);
                ui.InfoField("Thermal profiles are selected on each thermal sight. A daylight sight stays daylight when connected.", 2);
                foreach (var c in Components(model).Select(c => c.TryCast<GunnerSight>()).Where(c => c != null).ToArray())
                {
                    var entry = c!;
                    ui.ToggleField(entry.ComponentID + " #" + entry.VUID.Value, Target(entry) == model.VUID.Value,
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v => { if (entry.VehicleObject.Released) return; Select(entry, v ? model.VUID.Value : -1); __instance.RequestRedraw(); }))!,
                        "A sight has one console; linking it here replaces its previous console.");
                }
                foreach (var c in Components(model).Select(c => c.TryCast<LayingDrive>()).Where(c => c != null).ToArray())
                {
                    var entry = c!;
                    ui.ToggleField("Laying Drive #" + entry.VUID.Value, Target(entry) == model.VUID.Value,
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v => { if (entry.VehicleObject.Released) return; SelectLayingDrive(entry, v ? model.VUID.Value : -1); __instance.RequestRedraw(); }))!,
                        "Gunner operates this elevation/laying drive at this FCS console.");
                }
                foreach (var c in Components(model).Select(c => c.TryCast<TraverseMotor>()).Where(c => c != null).ToArray())
                {
                    var entry = c!;
                    ui.ToggleField("Traverse Drive #" + entry.VUID.Value, Target(entry) == model.VUID.Value,
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v => { if (entry.VehicleObject.Released) return; SelectTraverse(entry, v ? model.VUID.Value : -1); __instance.RequestRedraw(); }))!,
                        "Gunner operates this turret traverse drive at this FCS console.");
                }
            }
            else if (model != null && IsDriverConsole(model))
            {
                ui.Header(ConsoleLabel(model));
                ui.InfoField("Link driver steering controls below. Place this console within the driver's normal operating reach; the assigned viewport can be anywhere on the vehicle as long as its exterior lens is clear.", 3);
                foreach (var c in Components(model).Select(c => c.TryCast<PowertrainSteeringControls>()).Where(c => c != null).ToArray())
                {
                    var entry = c!;
                    ui.ToggleField("Steering Controls #" + entry.VUID.Value, Target(entry) == model.VUID.Value,
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v => { if (entry.VehicleObject.Released) return; SelectDriver(entry, v ? model.VUID.Value : -1); __instance.RequestRedraw(); }))!,
                        "Driver operates steering and views the assigned viewport at this Driver Console.");
                }
            }
            else if (model != null && IsCommanderConsole(model))
            {
                ui.Header(ConsoleLabel(model));
                ui.InfoField("Link an external Commander Panoramic Sight, cupola or viewport below. Place this console within the commander's normal operating reach.", 2);
                foreach (var c in Components(model).Select(c => c.TryCast<CommanderCupola>()).Where(c => c != null).ToArray())
                {
                    var entry = c!;
                    ui.ToggleField((entry.ComponentID == CommanderSightId ? "Commander Sight #" : "Cupola #") + entry.VUID.Value, Target(entry) == model.VUID.Value,
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v => { if (entry.VehicleObject.Released) return; SelectCommander(entry, v ? model.VUID.Value : -1); __instance.RequestRedraw(); }))!,
                        "Commander operates this sight or cupola at this Commander Console.");
                }
                foreach (var c in Components(model).Select(c => c.TryCast<Viewport>()).Where(c => c != null).ToArray())
                {
                    var entry = c!;
                    ui.ToggleField("Viewport #" + entry.VUID.Value, Target(entry) == model.VUID.Value,
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v => { if (entry.VehicleObject.Released) return; SelectCommander(entry, v ? model.VUID.Value : -1); __instance.RequestRedraw(); }))!,
                        "Commander operates this viewport at this Commander Console.");
                }
            }
        }
        catch (Exception ex) { Runtime.Warn("Console inspector: " + ex.Message); }
    }
}
