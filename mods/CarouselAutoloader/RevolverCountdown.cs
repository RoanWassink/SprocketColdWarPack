using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;

namespace SprocketCarouselAutoloader;

// Display correction only for a revolver task waiting on the crew's stationary
// refill. Every other case keeps the native countdown and fraction.
// 0.2.28 (M3): no hooks of its own; called second from the SemiCountdown getter postfixes (same order as before).
internal static class RevolverCountdown
{
    internal static void Remaining(LoadTask __instance, ref float __result)
    {
        if (!MagazineRefill.NoLoaderDisplay(__instance, out var progress, out var perRound)) return;
        __result = (float)((1 - progress) * perRound);
    }

    internal static void Fraction(LoadTask __instance, ref float __result)
    {
        if (!MagazineRefill.NoLoaderDisplay(__instance, out var progress, out _)) return;
        __result = (float)Math.Clamp(progress, 0, 1);
    }
}
