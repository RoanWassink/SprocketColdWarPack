using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons.Cannons;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace SprocketFloppyParts;

[HarmonyPatch]
internal static class MuzzleBlastHooks
{
    [ThreadStatic] private static ProjectileRegister? sampleReg;
    [ThreadStatic] private static int sampleBefore;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CannonBehaviour), nameof(CannonBehaviour.FireInternal))]
    private static void BeforeFire(CannonBehaviour __instance)
    {
        sampleReg = null;
        try
        {
            if (!Plugin.MuzzleBlastEnabled.Value || __instance == null) return;
            var reg = __instance.projectileFactory?.TryCast<ProjectileRegister>();
            if (reg == null) return;
            sampleReg = reg;
            sampleBefore = reg.nextID;
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[Blast] Prefix warning: {ex.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CannonBehaviour), nameof(CannonBehaviour.FireInternal))]
    private static void AfterFire(CannonBehaviour __instance)
    {
        var sampled = sampleReg;
        int before = sampleBefore;
        bool fired = false;
        try
        {
            if (sampled != null && Plugin.MuzzleBlastEnabled.Value && __instance != null)
            {
                var reg = __instance.projectileFactory?.TryCast<ProjectileRegister>();
                if (reg != null && reg.Pointer == sampled.Pointer && reg.nextID != before)
                    fired = true;
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[Blast] Postfix warning: {ex.Message}");
        }
        finally
        {
            sampleReg = null;
        }

        if (!fired || __instance == null) return;

        try
        {
            var cannon = __instance.mount?.TryCast<Cannon>();
            var barrel = cannon?.Barrel?.TryCast<CannonBarrelModel>();
            Vector3 mPos = barrel != null
                ? barrel.transform.TransformPoint(barrel.FireLocalPosition)
                : __instance.transform.position;
            Vector3 mDir = barrel != null
                ? barrel.transform.forward
                : __instance.transform.forward;
            float caliber = Math.Clamp((float)__instance.Caliber, 20f, 300f);

            FloppyMotionController.ApplyMuzzleBlast(
                new NVector(mPos.x, mPos.y, mPos.z),
                new NVector(mDir.x, mDir.y, mDir.z),
                caliber);
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[Blast] Apply warning: {ex.Message}");
        }
    }
}
