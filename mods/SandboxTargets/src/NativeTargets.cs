using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.GameControl;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Missions;
using Sprocket.Vehicles.Spawning;
using UnityEngine;

namespace SprocketSandboxTargets;

// The flat sandbox spawns one AI copy of the player's design through this event.
// Appending instances to its list lets the native spawner build, register, activate
// and reset the targets exactly like that stock copy. Nothing is moved, frozen or
// registered afterwards: doing that by hand left kinematic bodies behind.
// Targets with their own blueprint go to cloned spawn events (see CustomSpawners),
// which is how the normal sandbox map spawns its fixed IS-3 target.
[HarmonyPatch(typeof(StageVehicleSpawnEvent),nameof(StageVehicleSpawnEvent.GetSpawnInstances))]
internal static class NativeTargets
{
    internal const string FlatScene = "Assets/Scenes/Game Scenes/Sandbox (Low performance).unity";
    internal static bool Stopped;

    internal static bool IsStock(StageVehicleSpawnEvent e) =>
        !CustomSpawners.IsClone(e) && e.blueprintSource != null &&
        e.blueprintSource.Mode == SpawnMode.EditorExitSave && e.gameObject.scene.path == FlatScene;

    private static void Postfix(StageVehicleSpawnEvent __instance,ref Il2CppStructArray<VehicleSpawnInstance> __result)
    {
        if (Stopped) return;
        if (CustomSpawners.TryTake(__instance,out var custom)) { __result = custom; return; }
        if (__result == null || __result.Length == 0) return;
        try
        {
            if (!IsStock(__instance)) return;
            CustomSpawners.ResetClones();
            var config = TargetConfig.Load(Plugin.ConfigPath,Plugin.Warn);
            Plugin.Info($"Config geladen: enabled={config.Enabled}, targets={config.Targets.Length}, bestand={Plugin.ConfigPath}");
            if (!config.Enabled || config.Targets.Length == 0) return;
            if (!TryPlayer(out var player,out var body)) { Plugin.Warn("Spelertank niet gereed; alleen de stock kopie."); return; }

            var stock = __result[0];
            if (!TryGround(stock.position,out var stockGround)) { Plugin.Warn("Geen grond onder stock kopie; doelen overgeslagen."); return; }
            var clearance = stock.position.y - stockGround;
            var anchor = Center(body);
            var forward = player.transform.forward; forward.y = 0;
            if (forward.sqrMagnitude < .001f) { Plugin.Warn("Geen horizontale rijrichting; doelen overgeslagen."); return; }
            var heading = Mathf.Atan2(forward.x,forward.z) * Mathf.Rad2Deg;
            // Own-design targets are copies of this design, so the root-to-centre offset carries over.
            // For other blueprints the bounds are unknown before spawning; their root goes on the spot.
            var offset = Quaternion.Inverse(player.transform.rotation) * (anchor - player.transform.position);

            var own = new List<VehicleSpawnInstance>();
            var groups = new Dictionary<string,List<VehicleSpawnInstance>>(StringComparer.OrdinalIgnoreCase);
            var number = 0;
            foreach (var spec in config.Targets)
            {
                number++;
                var pose = TargetGeometry.Place(anchor.x,anchor.z,heading,spec);
                var rotation = Quaternion.Euler(0,(float)pose.Yaw,0);
                var shift = spec.Blueprint == null ? rotation * offset : Vector3.zero;
                var root = new Vector3((float)pose.X - shift.x,0,(float)pose.Z - shift.z);
                if (!TryGround(root,out var ground)) { Plugin.Warn($"Doel {number}: geen grond; overgeslagen."); continue; }
                root.y = ground + clearance;
                var instance = stock; // keeps the native team/group overrides
                instance.position = root; instance.rotation = rotation;
                if (spec.Blueprint == null) own.Add(instance);
                else
                {
                    if (!groups.TryGetValue(spec.Blueprint,out var list)) groups[spec.Blueprint] = list = new();
                    list.Add(instance);
                }
                Plugin.Info(FormattableString.Invariant($"Doel {number}: {spec.Blueprint ?? "eigen tank"}, root=({root.x:F2},{root.y:F2},{root.z:F2}), yaw={pose.Yaw:F1}, afstand={spec.Distance:F0} m"));
            }

            foreach (var failed in CustomSpawners.Start(__instance,groups))
            {
                // Recalculate with the own-design offset so a fallback copy stands where planned.
                foreach (var instance in groups[failed])
                {
                    var moved = instance;
                    var shift = instance.rotation * offset;
                    moved.position = new Vector3(instance.position.x - shift.x,instance.position.y,instance.position.z - shift.z);
                    own.Add(moved);
                }
            }
            if (own.Count == 0) return;
            var all = new Il2CppStructArray<VehicleSpawnInstance>(__result.Length + own.Count);
            for (var i = 0; i < __result.Length; i++) all[i] = __result[i];
            for (var i = 0; i < own.Count; i++) all[__result.Length + i] = own[i];
            __result = all;
            Plugin.Info($"{own.Count} doelen met eigen tank aan de native sandbox-spawn toegevoegd (totaal {all.Length}).");
        }
        catch (Exception ex) { Plugin.Warn("Doelen overgeslagen: " + ex); }
    }

    private static bool TryPlayer(out IVehicleGateway player,out VehicleBehaviour body)
    {
        player = null!; body = null!;
        var controller = UnityEngine.Object.FindObjectOfType<GameController>();
        var mode = controller?.gamemode?.TryCast<ScenarioGameMode>();
        var gateway = mode?.sharedState?.PlayerVehicle?.TryCast<IVehicleGateway>();
        var behaviour = gateway?.Behaviour?.TryCast<VehicleBehaviour>();
        if (gateway == null || behaviour == null || !behaviour.initiated) return false;
        player = gateway; body = behaviour;
        return true;
    }

    private static Vector3 Center(VehicleBehaviour body) => body.transform.TransformPoint(body.LocalBounds.center);

    private static bool TryGround(Vector3 point,out float height)
    {
        height = float.NegativeInfinity;
        var hits = Physics.RaycastAll(new Vector3(point.x,point.y + 2000,point.z),Vector3.down,4000,~0,QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            var collider = hit.collider;
            if (collider == null || collider.GetComponentInParent<Vehicle>() != null ||
                collider.GetComponentInParent<VehicleObject>() != null || collider.GetComponentInParent<VehicleBehaviour>() != null)
                continue;
            if (hit.normal.y < .5f) continue;
            if (hit.point.y > height) height = hit.point.y;
        }
        return !float.IsNegativeInfinity(height);
    }
}
