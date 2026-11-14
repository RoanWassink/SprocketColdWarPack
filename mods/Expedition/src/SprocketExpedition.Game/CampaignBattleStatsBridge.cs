using HarmonyLib;
using Sprocket;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons.Cannons;
using SprocketExpedition.Game.UI;
using UnityEngine;
using NativeObject = Il2CppSystem.Object;
using NativeVehicle = Sprocket.Vehicles.Vehicle;

namespace SprocketExpedition.Game;

// Unity-side adapter for CampaignBattleStats. Its observers are read-only: they
// never write native state, never change a result and run only while a campaign
// battle is active. Each hook is wrapped in try/catch because a statistics failure
// must never affect combat, Expanded Ballistics or AI Orders.
internal static class BattleStatsBridge
{
    private sealed record Launch(IntPtr Tank, int Id, float SpawnTime, Vector3 Origin);
    private static readonly Dictionary<IntPtr, Launch> launches = new();
    private static readonly Dictionary<IntPtr, IntPtr> tankByRigidBody = new();
    private static readonly Dictionary<IntPtr, IntPtr> tankByApplier = new();

    internal static void Begin(string attemptKey)
    {
        ResetMaps();
        CampaignBattleStats.Current.Begin(attemptKey, Time.time);
    }

    internal static void Restart()
    {
        ResetMaps();
        CampaignBattleStats.Current.Restart(Time.time);
    }

    internal static void Finish(string outcome)
    {
        try
        {
            var report = CampaignBattleStats.Current.Complete(outcome, Time.time);
            if (report is null) return;
            foreach (var tank in report.Tanks)
                Plugin.Logger.LogInfo(BattleStatsText.LogLine(outcome, tank));
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("Expedition battle stats could not be completed: " + ex.Message);
        }
    }

    internal static void Register(IntPtr tank, string? name, bool friendly, IntPtr rigidBody, IntPtr applier)
    {
        if (tank == IntPtr.Zero) return;
        CampaignBattleStats.Current.Register(tank, name, friendly);
        if (rigidBody != IntPtr.Zero) tankByRigidBody[rigidBody] = tank;
        if (applier != IntPtr.Zero) tankByApplier[applier] = tank;
    }

    internal static void NotePlayer(IntPtr tank)
    {
        if (tank != IntPtr.Zero) CampaignBattleStats.Current.NotePlayer(tank);
    }

    internal static void RecordLaunch(IntPtr tank, IntPtr projectileTransform, int projectileId,
        float spawnTime, Vector3 origin)
    {
        if (!CampaignBattleStats.Current.Active || projectileTransform == IntPtr.Zero) return;
        if (launches.Count >= 2048) launches.Clear();
        launches[projectileTransform] = new Launch(tank, projectileId, spawnTime, origin);
        CampaignBattleStats.Current.Shot(tank, projectileTransform);
    }

    internal static void RecordHit(ref ProjectileInstance projectile, ref ProjectileDamageModelHit hit)
    {
        if (!CampaignBattleStats.Current.Active) return;
        var transform = projectile.transform?.Pointer ?? IntPtr.Zero;
        if (transform == IntPtr.Zero || !launches.TryGetValue(transform, out var launch)) return;
        // IDs are recycled; only the exact projectile instance that was launched counts.
        if (launch.Id != projectile.ID || Math.Abs(launch.SpawnTime - projectile.spawnTime) > .01f) return;
        var target = ResolveStructure(hit.ModelHit.Model);
        if (target == IntPtr.Zero) return;
        var delta = hit.HitPoint - launch.Origin;
        delta.y = 0f;
        var distance = Math.Abs(delta.magnitude);
        if (!float.IsFinite(distance)) return;
        CampaignBattleStats.Current.Hit(transform, launch.Tank, target, (decimal)distance, Time.time);
    }

    private static IntPtr ResolveStructure(IDamageModelStructure? structure)
    {
        if (structure is null) return IntPtr.Zero;
        try
        {
            var body = structure.RigidBody;
            if (body != null && tankByRigidBody.TryGetValue(body.Pointer, out var byBody)) return byBody;
        }
        catch { }
        try
        {
            var applier = structure.DamageApplier;
            if (applier != null && tankByApplier.TryGetValue(applier.Pointer, out var byApplier)) return byApplier;
        }
        catch { }
        return IntPtr.Zero;
    }

    // Explicit native killer and victim of a kill event, mapped to the hulls the
    // roster registered. Unreadable objects leave the fallback last-hit rule.
    internal static void RecordKill(NativeObject? killerObject, NativeObject? victimObject, bool suicide)
    {
        if (!CampaignBattleStats.Current.Active || suicide) return;
        var victim = ResolveVehicle(victimObject);
        if (victim == IntPtr.Zero) return;
        var killer = ResolveVehicle(killerObject);
        CampaignBattleStats.Current.ExplicitKill(killer, victim, Time.time);
    }

    private static IntPtr ResolveVehicle(NativeObject? value)
    {
        if (value is null) return IntPtr.Zero;
        try
        {
            var behaviour = value.TryCast<VehicleBehaviour>();
            if (behaviour != null) return behaviour.Pointer;
        }
        catch { }
        try
        {
            var cannon = value.TryCast<CannonBehaviour>();
            var vehicle = cannon?.mount?.TryCast<Cannon>()?.Vehicle;
            if (vehicle?.Behaviour is { } driven) return driven.Pointer;
        }
        catch { }
        try
        {
            var data = value.TryCast<NativeVehicle>();
            if (data?.Behaviour is { } behaviour) return behaviour.Pointer;
        }
        catch { }
        try
        {
            var component = value.TryCast<Component>();
            for (var transform = component?.transform; transform != null; transform = transform.parent)
            {
                var behaviour = transform.GetComponent<VehicleBehaviour>();
                if (behaviour != null) return behaviour.Pointer;
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    private static void ResetMaps()
    {
        launches.Clear();
        tankByRigidBody.Clear();
        tankByApplier.Clear();
    }
}

// Observers only. Prefix/postfix methods read the native event; none returns a
// result, writes an argument or alters the patched method's behaviour. Patches
// are installed manually inside try/catch so one unavailable signature disables
// that measurement instead of the whole mod. Before every patch the native body
// is classified; a trivial or ICF-shared body is never patched.
internal static class BattleStatsPatches
{
    internal static void Install(Harmony harmony, bool enabled)
    {
        var plan = BattleStatsHookPlan.For(enabled);
        if (plan.Count == 0)
        {
            Plugin.Logger.LogInfo(
                "Expedition battle stats: hooks disabled (Diagnostics.BattleStatsHooks=false).");
            return;
        }

        foreach (var label in plan)
        {
            switch (label)
            {
                case BattleStatsHookPlan.Launch:
                    TryInstall(harmony, label,
                        () => AccessTools.Method(typeof(ProjectileRegister), nameof(ProjectileRegister.Launch)),
                        nameof(Launched), postfix: true);
                    break;
                case BattleStatsHookPlan.ArmourHit:
                    TryInstall(harmony, label,
                        () => AccessTools.Method(typeof(ArmourPiercingProjectileFunction),
                            nameof(ArmourPiercingProjectileFunction.HitDamageModel)),
                        nameof(ArmourHit), postfix: false);
                    break;
                case BattleStatsHookPlan.ExplosiveHit:
                    TryInstall(harmony, label,
                        () => AccessTools.Method(typeof(ExplosiveProjectileFunction),
                            nameof(ExplosiveProjectileFunction.HitDamageModel)),
                        nameof(ExplosiveHit), postfix: false);
                    break;
                // Shrapnel is intentionally not installed: its empty HitDamageModel
                // shares a native address with unrelated methods under ICF.
                case BattleStatsHookPlan.KillEvent:
                    TryInstall(harmony, label,
                        () => AccessTools.Constructor(typeof(KillEventArgs),
                            new[] { typeof(NativeObject), typeof(NativeObject), typeof(Relationship) }),
                        nameof(Killed), postfix: true);
                    break;
            }
        }
    }

    private static void TryInstall(Harmony harmony, string label,
        System.Func<System.Reflection.MethodBase?> resolve, string patch, bool postfix)
    {
        System.Reflection.MethodBase? target;
        try { target = resolve(); }
        catch (Exception ex)
        {
            Report(label, IntPtr.Zero, "-", false, "target unavailable: " + ex.Message);
            return;
        }
        if (target is null)
        {
            Report(label, IntPtr.Zero, "-", false, "target not found");
            return;
        }

        if (!BattleStatsNativeGuard.TryReadMethodBody(target, out var methodPointer,
                out var bytes, out var classification, out var error))
        {
            Report(label, methodPointer, bytes, false, error);
            return;
        }

        if (classification.IsTrivial)
        {
            Report(label, methodPointer, bytes, false, "trivial body: " + classification.Reason);
            return;
        }

        try
        {
            var method = new HarmonyMethod(typeof(BattleStatsPatches), patch);
            if (postfix) harmony.Patch(target, postfix: method);
            else harmony.Patch(target, prefix: method);
            Report(label, methodPointer, bytes, true, null);
        }
        catch (Exception ex)
        {
            Report(label, methodPointer, bytes, false, "patch failed: " + ex.Message);
        }
    }

    // Exactly one line per hook, installed or skipped. The byte sample is the
    // first 16 bytes at the native methodPointer.
    private static void Report(string label, IntPtr methodPointer, string bytes, bool patched,
        string? reason)
    {
        var outcome = patched ? "patched" : "skipped (" + reason + ")";
        Plugin.Logger.LogInfo($"Expedition battle stats: {label} "
            + $"methodPointer=0x{methodPointer.ToInt64():X} bytes={bytes} -> {outcome}");
    }

    private static void Launched(ProjectileRegister __instance, NativeObject source, ref ProjectileFireInfo fireInfo)
    {
        try
        {
            if (!CampaignBattleStats.Current.Active) return;
            var cannon = source?.TryCast<CannonBehaviour>()?.mount?.TryCast<Cannon>();
            var vehicle = cannon?.Vehicle?.Behaviour;
            if (vehicle is null || vehicle.Pointer == IntPtr.Zero) return;
            if (!__instance.activeIdMap.TryGetValue(fireInfo.ProjectileID, out var index)) return;
            var projectile = __instance.pool[index];
            var transform = projectile.transform?.Pointer ?? IntPtr.Zero;
            if (transform == IntPtr.Zero) return;
            BattleStatsBridge.RecordLaunch(vehicle.Pointer, transform, projectile.ID,
                projectile.spawnTime, projectile.position);
        }
        catch { }
    }

    private static void ArmourHit(ref ProjectileInstance projectile, ref ProjectileDamageModelHit hit)
        => SafeImpact(ref projectile, ref hit);

    private static void ExplosiveHit(ref ProjectileInstance projectile, ref ProjectileDamageModelHit hit)
        => SafeImpact(ref projectile, ref hit);

    private static void SafeImpact(ref ProjectileInstance projectile, ref ProjectileDamageModelHit hit)
    {
        try { BattleStatsBridge.RecordHit(ref projectile, ref hit); }
        catch { }
    }

    // The game's own kill attribution (Killer/Victim/Relation/Suicide). A folded
    // constructor address can deliver another class; only read when it is exactly
    // KillEventArgs.
    private static void Killed(KillEventArgs __instance)
    {
        try
        {
            if (__instance is null || !BattleStatsNativeGuard.IsKillEventArgs(__instance.Pointer)) return;
            BattleStatsBridge.RecordKill(__instance.Killer, __instance.Victim, __instance.Suicide);
        }
        catch { }
    }
}

