using System.Runtime.CompilerServices;
using SprocketUiKit;

namespace SprocketCarouselAutoloader;

// 0.2.27: the only class that calls Container.Radial (UI Kit 0.1.8). AutoloaderHudView calls Build only
// when HudRingGate.Supported(the loaded kit's plugin version) is true, so an older kit never JITs the
// missing method; with kit 0.1.7 the HUD keeps its bars. Build is NoInlining and its signature uses only
// types that exist in every 0.1.x kit.
internal static class AutoloaderHudRing
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Build(Stack card, int k)
    {
        // One row per action: a ring (fill = progress, centre = remaining seconds) while it runs, then its name.
        // Without a running action (waiting for a refill, "Blocked: ...") only the label is shown.
        var cycle = card.Row(8).VisibleWhen(() => AutoloaderHudFeed.Shown[k]?.ShowCycle == true);
        cycle.Radial(() => AutoloaderHudFeed.Shown[k]?.Row.CycleFraction ?? 0f, AutoloaderHudLayout.RingSize,
                () => AutoloaderHudFeed.Shown[k]?.CycleCentre)
            .VisibleWhen(() => AutoloaderHudFeed.Shown[k]?.CycleRunning == true);
        cycle.Label(() => AutoloaderHudFeed.Shown[k]?.CycleName ?? "", TextStyle.Small).Outline().Flex();

        var refill = card.Row(8).VisibleWhen(() => AutoloaderHudFeed.Shown[k]?.ShowRefill == true);
        refill.Radial(() => AutoloaderHudFeed.Shown[k]?.Row.RefillFraction ?? 0f, AutoloaderHudLayout.RingSize,
                () => AutoloaderHudFeed.Shown[k]?.RefillCentre)
            .VisibleWhen(() => AutoloaderHudFeed.Shown[k]?.RefillRunning == true);
        refill.Label(() => AutoloaderHudFeed.Shown[k]?.RefillName ?? "", TextStyle.Small).Outline().Flex()
            .Colour(() => AutoloaderHudFeed.Shown[k]?.Row.Blocked == true ? "#e07a4f" : null);
    }
}
