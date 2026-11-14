using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;

namespace SprocketCarouselAutoloader;

internal static class AutoloaderCycle
{
    private static readonly HashSet<IntPtr> Substeps = new();
    internal static double Seconds(LoadTask task)
    {
        if (CarouselRuntime.Mechanical(task, out var carousel)) return carousel.Size.ReloadSeconds;
        var cannon = task.target?.TryCast<WeaponBehaviour>()?.mount?.TryCast<Cannon>();
        return BustleRuntime.Mechanical(task, cannon, out var bustle) ? BustleRuntime.Seconds(bustle, cannon!) : 0;
    }

    // Native Update advances only one phase per call and discards its overshoot.
    // Subdivide the SAME delta time for rapid automatic cycles; retain native stock use/state events.
    // 0.2.28 (M2): guarded only. Calling the native step without the hook chain, or letting the phase overshoot,
    // would change the cycle or skip other hooks (Speed/Assist/other mods) on the sub-steps, so the 8 sub-steps stay.
    [HarmonyPrefix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(LoadTask), nameof(LoadTask.Update))]
    private static bool RapidCycle(LoadTask __instance, float __0, float __1, float __2, float __3)
    {
        int steps;
        try
        {
            if (__instance.State != LoadState.Loading || MagazineRefill.Waiting(__instance) ||
                Substeps.Contains(__instance.Pointer) || __3 <= 0 || !float.IsFinite(__3)) return true;
            // M5: only Carousel/bustle/revolver cycles can be subdivided (cached, no allocation for other cannons).
            if (!VehicleLoadIndex.HasLoader(__instance)) return true;
            var seconds = Seconds(__instance);
            steps = LoadWorkPolicy.Steps(true, false, seconds, __3);
        }
        catch (Exception ex) { HookGuard.Warn("LoadTask.Update rapid cycle", HookGuard.TaskVuid(__instance), ex); return true; }
        if (steps == 1) return true;
        Substeps.Add(__instance.Pointer);
        try { for (int i = 0; i < steps; i++) __instance.Update(__0, __1, __2, __3 / steps); }
        catch (Exception ex)
        {
            // Sub-steps already advanced the task with this frame's time: never run the native step again on top.
            HookGuard.Warn("LoadTask.Update rapid cycle sub-step", HookGuard.TaskVuid(__instance), ex);
        }
        finally { Substeps.Remove(__instance.Pointer); }
        return false;
    }
}
