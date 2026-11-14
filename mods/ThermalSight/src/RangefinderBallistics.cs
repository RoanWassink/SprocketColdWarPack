using System.Text;
using HarmonyLib;
using Sprocket;
using Sprocket.VehicleControl;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.CrewSystems;
using Sprocket.Vehicles.Weapons;
using UnityEngine;
using PlayerController = Sprocket.Gameplay.VehicleControl.VehicleController;

namespace SprocketThermalSight;

// Linked-cannon identity, guided-profile checks and the gravity-aware aiming
// request for the retained automatic-laser distance. ScopeController still owns
// the persistent native distance; this class only converts the current owned
// sight ray at that distance into a native GunLayer aim request.
internal static class RangefinderBallistics
{
    private static int logs;
    private static bool warned;
    private static LoadedShellAdapter? shellAdapter;
    private static float lookupAt;
    internal static string? GuidedReason;
    // Chambered-round muzzle speed read with GuidedReason; null keeps Cannon.muzzleVelocity.
    internal static double? ChamberedSpeed;
    private static float loggedSpeed;
    private static int speedLogs;
    // Diagnostics while an automatic zero is active: aim calls, applied
    // corrections and refusals by reason, summarised from Rangefinder.Tick.
    private static readonly Dictionary<string, int> refusals = new();
    private static int calls, playerCalls, applied, reports;
    private static float reportAt;
    private static bool mismatchLogged;
    // One topology block per vehicle and session; the layer set is small.
    private static readonly HashSet<IntPtr> topologyVehicles = new();
    private static bool topologyWarned;
    internal static Cannon? LinkedCannon(GunnerSight sight)
    {
        if(sight.LinkedWeapon==null)return null;
        var objects=sight.Vehicle?.ObjectReader?.Items;if(objects==null)return null;
        int n=objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        Cannon? assigned=null;
        for(int i=0;i<n;i++)
        {
            var cannon=objects[i]?.GetBehaviour<Cannon>();
            if(cannon?.Behaviour==null || cannon.Vehicle?.Pointer!=sight.Vehicle!.Pointer)continue;
            if(cannon.Behaviour.Transform?.Pointer==sight.LinkedWeapon.Pointer)return cannon;
            if(cannon.AssignedSightVUID.Value==sight.VUID.Value)
            {if(assigned!=null)return null;assigned=cannon;}
        }
        return assigned;
    }
    internal static bool Guided(Cannon? cannon)
    {
        GuidedReason = null; ChamberedSpeed = null;
        if(cannon==null)return false;
        if (shellAdapter == null && Time.unscaledTime >= lookupAt)
        {
            lookupAt = Time.unscaledTime + 5;
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SprocketExpandedBallistics");
            var selection = assembly?.GetType("SprocketExpandedBallistics.RuntimeShellSelection");
            if (selection != null) shellAdapter = new LoadedShellAdapter(selection, assembly?.GetType("SprocketExpandedBallistics.BallisticsAmmoApi"));
        }
        try
        {
            GuidedReason = shellAdapter?.SuspendReason(cannon);
            ChamberedSpeed = GuidedReason == null ? shellAdapter?.ChamberedSpeed : null;
            return GuidedReason != null;
        }
        catch (Exception ex)
        {
            GuidedReason = "loaded-shell-exception:" + ex.GetType().Name;
            Runtime.Warn("Loaded-shell identity unavailable; automatic correction suspended: " + ex.GetType().Name, "loaded-shell-api");
            return true;
        }
    }

    internal static void ResetDiagnostics()
    { refusals.Clear(); calls = playerCalls = applied = 0; reportAt = Time.unscaledTime + 2; }
    private static void Refuse(string reason)
    {
        if (!Runtime.DetailedLogging) return;
        if (refusals.TryGetValue(reason, out var n)) refusals[reason] = n + 1;
        else if (refusals.Count < 24) refusals[reason] = 1;
    }
    // Rate-limited summary while a zero is active: every 5 s, at most 60 lines.
    // aimCalls=0 means the GunLayer.AimAtPosition prefix is not reached at all.
    internal static void Report(double zeroRange)
    {
        if (!Runtime.DetailedLogging || reports >= 60 || Time.unscaledTime < reportAt) return;
        reportAt = Time.unscaledTime + 5; reports++;
        var why = refusals.Count == 0 ? "none" : string.Join(", ", refusals.Select(p => $"{p.Key} x{p.Value}"));
        Plugin.Instance.Log.LogInfo($"[Rangefinder FCS diag] zero={zeroRange:F0}m aimCalls={calls} playerLayerCalls={playerCalls} applied={applied}; refused: {why}.");
        refusals.Clear(); calls = playerCalls = applied = 0;
    }

    // The native gun layer asks for a raw world target each aiming update. Replace
    // only that request with the current sight-ray point at the persistent measured
    // range, corrected by the native gravity/constant-velocity solver. No camera,
    // scope aim point, gun angle or global target ray is modified.
    [HarmonyPrefix, HarmonyPatch(typeof(GunLayer), nameof(GunLayer.AimAtPosition), new[] { typeof(Vector3), typeof(GunLayerAimFlags) })]
    private static void Aim(GunLayer __instance, ref Vector3 __0)
    {
        try
        {
            calls++;
            if (!Rangefinder.BallisticContext(out var player, out var sight, out var linked, out var range, out var refusal))
            { if (refusal != null) Refuse(refusal); return; }
            // Other vehicles' layers (AI) are not refusals.
            if (player.gunLayers?.Any(layer => layer?.Pointer == __instance.Pointer) != true) return;
            playerCalls++;
            LogTopology(player, linked);
            if (!ControlsCannon(__instance, linked))
            {
                var mechanism = __instance.ControlTarget;
                Refuse("control-target-not-linked-cannon");
                if (Runtime.DetailedLogging && !mismatchLogged) { mismatchLogged = true; Plugin.Instance.Log.LogInfo($"[Rangefinder FCS diag] layer target={mechanism?.Pointer} horizontal={linked.HorizontalAimingMechanism?.Pointer} vertical={linked.VerticalAimingMechanism?.Pointer} cannon={linked.VUID.Value}."); }
                return;
            }
            var direction = player.AimRay.direction;
            if (direction.sqrMagnitude < .000001f || !Finite(direction)) { Refuse("aim-ray"); return; }
            var lens = sight.SightParent.TransformPoint(sight.ObjectiveLensLocalPosition);
            if (!Finite(lens)) { Refuse("lens"); return; }
            // Recompute from the current owned sight ray and lens every update; never
            // reuse a previous world target and never feed ScopeAimPoint back in.
            var target = lens + direction.normalized * range;
            var muzzle = linked.Behaviour.ShellSpawnPosition;
            // Expanded Ballistics keeps muzzleVelocity at the design shell; prefer the
            // speed of the round really in the chamber when it reports one.
            var source = ChamberedSpeed != null ? "chambered" : "cannon";
            var speed = ChamberedSpeed is { } chambered ? (float)chambered : linked.muzzleVelocity;
            if (!Finite(muzzle) || !float.IsFinite(speed) || speed < 50)
            { Refuse($"muzzle speed={speed:F0}"); return; }
            var inherited = linked.Behaviour.InheritedVelocity;
            if (!RangefinderZeroingPolicy.TryStationaryTargetVelocity(inherited.x, inherited.y, inherited.z,
                out var relativeX, out var relativeY, out var relativeZ)) { Refuse("inherited-velocity"); return; }
            var targetVelocity = new Vector3((float)relativeX, (float)relativeY, (float)relativeZ);
            var correction = Sprocket.PhysicsRules.Ballistics.CalculateInterceptPoint(
                target, targetVelocity, muzzle, speed, 6);
            if (!Finite(correction) || Vector3.Distance(correction, target) > range * .25f) { Refuse("solver-out-of-bounds"); return; }
            __0 = correction;
            applied++;
            // First 8 corrections, plus up to 16 more whenever the speed changes (ammo switch).
            if (logs++ < 8 || (Mathf.Abs(speed - loggedSpeed) > .5f && speedLogs++ < 16))
            {
                loggedSpeed = speed;
                Plugin.Instance.Log.LogInfo($"[Rangefinder FCS] range={range:F1}m speed={speed:F1}m/s speedSource={source} cannonSpeed={linked.muzzleVelocity:F1}m/s inherited={inherited} lift={correction.y-target.y:F3}m target={target} muzzle={muzzle} cannon={linked.VUID.Value}.");
            }
        }
        catch (Exception ex)
        {
            Refuse("exception:" + ex.GetType().Name);
            if (!warned) { warned = true; Plugin.Instance.Log.LogWarning("[Rangefinder FCS] Gravity-aware aim unavailable; native request retained: " + ex.Message); }
        }
    }
    // A player layer may control the cannon through a ring or cradle rather than
    // through the exact mechanism object the cannon reports. Match the mount
    // path, not just object identity, so the elevating cradle is accepted and
    // a layer for another cannon or vehicle is still refused.
    private static bool ControlsCannon(GunLayer layer, Cannon cannon)
    {
        var target = layer.ControlTarget;
        if (target == null) return false;
        var horizontal = cannon.HorizontalAimingMechanism;
        var vertical = cannon.VerticalAimingMechanism;
        var targetTransform = AimableTransform(target);
        return AimChain.Controls(target.Pointer, targetTransform,
            cannon.Behaviour?.Transform?.Pointer ?? IntPtr.Zero,
            horizontal?.Pointer ?? IntPtr.Zero, AimableTransform(horizontal),
            vertical?.Pointer ?? IntPtr.Zero, AimableTransform(vertical),
            OwningCannon(targetTransform), cannon.Pointer, ParentTransform);
    }
    // Nearest Cannon above the target transform, used to keep a layer that
    // drives another gun refused even when both share a cradle.
    private static IntPtr OwningCannon(IntPtr transform)
    {
        if (transform == IntPtr.Zero) return IntPtr.Zero;
        Transform? node;
        try { node = new Transform(transform); }
        catch { return IntPtr.Zero; }
        for (int depth = 0; node != null && depth < AimChain.MaxDepth; depth++)
        {
            try
            {
                var cannon = node.GetComponent<Cannon>();
                if (cannon != null) return cannon.Pointer;
            }
            catch { }
            node = Parent(node);
        }
        return IntPtr.Zero;
    }
    private static IntPtr AimableTransform(IAimable? aimable)
        => TransformPointer(aimable?.TryCast<TurretBehaviour>()?.transform);
    private static IntPtr AimableTransform(IReadOnlyAimable? aimable)
        => TransformPointer(aimable?.TryCast<TurretBehaviour>()?.transform);
    private static IntPtr TransformPointer(Transform? transform)
    {
        try { return transform == null ? IntPtr.Zero : transform.Pointer; }
        catch { return IntPtr.Zero; }
    }
    private static IntPtr ParentTransform(IntPtr pointer)
    {
        try { return new Transform(pointer).parent?.Pointer ?? IntPtr.Zero; }
        catch { return IntPtr.Zero; }
    }

    // Once per vehicle: names, types, VUIDs and six parent levels for every
    // player layer target and both cannon mechanisms. The next game test then
    // confirms the actual mount topology without another build.
    private static void LogTopology(PlayerController player, Cannon cannon)
    {
        if (!Runtime.DetailedLogging) return;
        var vehicle = cannon.Vehicle;
        if (vehicle == null || vehicle.Pointer == IntPtr.Zero || topologyVehicles.Count >= 8
            || !topologyVehicles.Add(vehicle.Pointer)) return;
        try
        {
            var text = new StringBuilder();
            text.Append("[Rangefinder FCS diag] topology vehicle=0x").Append(vehicle.Pointer.ToInt64().ToString("X"))
                .Append(" cannon=").Append(cannon.VUID.Value)
                .Append(" cannonTransform=").Append(Describe(cannon.Behaviour?.Transform));
            var layers = player.gunLayers;
            if (layers != null)
            {
                int logged = 0;
                for (int i = 0; i < layers.Length && logged < 8; i++)
                {
                    var layer = layers[i]?.TryCast<GunLayer>();
                    if (layer == null) continue;
                    text.Append(" layer").Append(logged++).Append('=').Append(Describe(layer.ControlTarget))
                        .Append(" accepted=").Append(ControlsCannon(layer, cannon) ? "yes" : "no");
                }
            }
            text.Append(" horizontal=").Append(Describe(cannon.HorizontalAimingMechanism));
            text.Append(" vertical=").Append(Describe(cannon.VerticalAimingMechanism));
            Plugin.Instance.Log.LogInfo(text.ToString());
        }
        catch (Exception ex)
        {
            if (!topologyWarned)
            {
                topologyWarned = true;
                Plugin.Instance.Log.LogWarning("[Rangefinder FCS diag] Topology unavailable: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
    private static string Describe(IAimable? aimable)
        => aimable == null ? "none" : Describe(AimableType(aimable), aimable.Pointer, aimable.TryCast<TurretBehaviour>()?.transform);
    private static string Describe(IReadOnlyAimable? aimable)
        => aimable == null ? "none" : Describe(AimableType(aimable), aimable.Pointer, aimable.TryCast<TurretBehaviour>()?.transform);
    private static string AimableType(IAimable aimable)
        => aimable.TryCast<TurretBehaviour>() != null ? "TurretBehaviour" : aimable.GetType().Name;
    private static string AimableType(IReadOnlyAimable aimable)
        => aimable.TryCast<TurretBehaviour>() != null ? "TurretBehaviour" : aimable.GetType().Name;
    private static string Describe(Transform? transform)
        => transform == null ? "none" : Describe("Transform", transform.Pointer, transform);
    private static string Describe(string type, IntPtr pointer, Transform? transform)
    {
        if (transform == null) return type + " ptr=0x" + pointer.ToInt64().ToString("X") + " transform=none";
        var text = new StringBuilder();
        text.Append(type).Append(" ptr=0x").Append(pointer.ToInt64().ToString("X"))
            .Append(" vuid=").Append(Vuid(transform)).Append(" chain=");
        Transform? node = transform;
        for (int level = 0; level < 6 && node != null; level++)
        {
            if (level > 0) text.Append("<-");
            text.Append('\'').Append(TransformName(node)).Append('\'');
            var component = ExactComponent(node);
            if (component != null)
            {
                text.Append('(').Append(component.GetType().Name).Append(',').Append(component.VUID.Value);
                var guid = Guid(component);
                if (!string.IsNullOrEmpty(guid)) text.Append(',').Append(guid);
                text.Append(')');
            }
            node = Parent(node);
        }
        return text.ToString();
    }
    private static int Vuid(Transform transform)
        => (ExactComponent(transform) ?? NearestComponent(transform))?.VUID.Value ?? 0;
    private static VehicleComponent? ExactComponent(Transform transform)
    {
        try { return transform.GetComponent<VehicleComponent>(); }
        catch { return null; }
    }
    private static VehicleComponent? NearestComponent(Transform transform)
    {
        try { return transform.GetComponentInParent<VehicleComponent>(); }
        catch { return null; }
    }
    private static string? Guid(VehicleComponent component)
    {
        try { return component.VehicleObject?.GUID; }
        catch { return null; }
    }
    private static string TransformName(Transform transform)
    {
        try { return transform.gameObject.name; }
        catch { return "?"; }
    }
    private static Transform? Parent(Transform transform)
    {
        try { return transform.parent; }
        catch { return null; }
    }
    private static bool Finite(Vector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}
