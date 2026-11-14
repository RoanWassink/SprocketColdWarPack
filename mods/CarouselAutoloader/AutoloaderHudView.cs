using System.Runtime.CompilerServices;
using SprocketUiKit;
using UnityEngine;

namespace SprocketCarouselAutoloader;

// 0.2.26: the only class that touches SprocketUiKit types (with AutoloaderHudRing for the ring from
// kit 0.1.8). Plugin calls it only after the kit plugin is found (soft dependency), so Carousel loads
// and works without the kit. Public kit API of 0.1.2 (CreateHud/HudPanel, Label, Progress, VisibleWhen,
// Outline, Colour) plus Row, Chip and Spacer; nothing from 0.1.6 or later here.
// 0.2.27: the HUD sits directly below the Expanded Ballistics AMMO panel (same corner, X and width) and
// is visible only while something runs; see AutoloaderHudLayout and HudCard for the rules.
internal static class AutoloaderHudView
{
    private static HudPanel? hud;
    private static HudPlace placement;
    private static HudSpot manual;
    private static float gap = AutoloaderHudLayout.DefaultGap;
    private static HudSpot applied;
    private static bool hasApplied;
    private static float nextPlace, placedHeight = -1;
    private static int reportedMask;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Create(HudPlace mode, string corner, float offsetX, float offsetY, float gapPx)
    {
        if (hud != null) return;
        placement = mode; gap = gapPx;
        manual = new HudSpot(AutoloaderHudLayout.TryParseCorner(corner, out var anchor) ? anchor : HudAnchor.BottomRight,
            offsetX, offsetY, HudSpotSource.Manual);
        hud = UiKit.CreateHud(Plugin.Guid, "autoloader", AutoloaderHudLayout.Width);
        try
        {
            // Hidden until Poll says otherwise, whatever happens while the body is built.
            hud.Visible(Poll);
            // Same visibility rules as the AMMO panel: Play only, hidden while a kit panel is open or the game
            // is paused, and a free cursor alone (scope, free look) does not hide it.
            hud.AllowedContexts = UiContexts.All;
            hud.HideWhenCursorFree = false;
            hud.MaxHeight(AutoloaderHudLayout.MaxHeight);
            hud.Anchor(ToKit(manual.Corner), manual.X, manual.Y);
            var body = hud.Body;
            body.Spacing(AutoloaderHudLayout.BodyGap);
            for (int i = 0; i < AutoloaderHudFeed.MaxRows; i++) BuildCard(body, i);
        }
        catch
        {
            try { UiKit.DestroyHud(hud); } catch { /* the HUD is just not shown */ }
            hud = null;
            throw;
        }
    }

    private static void BuildCard(Stack body, int k)
    {
        var card = body.Stack(AutoloaderHudLayout.CardGap).VisibleWhen(() => AutoloaderHudFeed.Shown[k] != null);
        var head = card.Row(8);
        head.Label(() => AutoloaderHudFeed.Shown[k]?.Heading ?? "", TextStyle.Section).Outline();
        head.Chip("two-piece", ChipStyle.Tag).VisibleWhen(() => AutoloaderHudFeed.Shown[k]?.TwoPiece == true);
        head.Spacer();
        if (Plugin.HudRings) AutoloaderHudRing.Build(card, k);
        else BuildBars(card, k);
    }

    // Kit before 0.1.8: a label and a bar per running action, as in 0.2.26 (but never "ready" or "Full").
    private static void BuildBars(Stack card, int k)
    {
        card.Label(() => AutoloaderHudFeed.Shown[k]?.Row.Cycle ?? "", TextStyle.Small).Outline()
            .VisibleWhen(() => AutoloaderHudFeed.Shown[k]?.ShowCycle == true);
        card.Progress(() => AutoloaderHudFeed.Shown[k]?.Row.CycleFraction ?? 0f)
            .VisibleWhen(() => AutoloaderHudFeed.Shown[k]?.ShowCycle == true);
        card.Label(() => AutoloaderHudFeed.Shown[k]?.Row.Refill ?? "", TextStyle.Small).Outline()
            .Colour(() => AutoloaderHudFeed.Shown[k]?.Row.Blocked == true ? "#e07a4f" : null)
            .VisibleWhen(() => AutoloaderHudFeed.Shown[k]?.ShowRefill == true);
        card.Progress(() => AutoloaderHudFeed.Shown[k]?.Row.RefillFraction ?? 0f)
            .VisibleWhen(() => AutoloaderHudFeed.Shown[k]?.RefillRunning == true);
    }

    /// <summary>The kit polls this every frame, also while hidden. Same rules as the AMMO panel, plus "something runs".</summary>
    private static bool Poll()
    {
        if (!AutoloaderHudFeed.Active) return false;
        Place();
        return !UiKit.AnyPanelOpen && Time.timeScale > 0f;
    }

    // Anchors the HUD again when the number of lines changed (its height) and at most once per second otherwise,
    // when the AMMO panel's config is read again too. Nothing allocates.
    private static void Place()
    {
        float now = Time.unscaledTime, height = AutoloaderHudFeed.HeightPx;
        if (now < nextPlace && height == placedHeight) return;
        nextPlace = now + 1f; placedHeight = height;
        bool known = AmmoHudAnchor.TryRead(now, out var ammoCorner, out var ammoX, out var ammoY);
        var spot = AutoloaderHudLayout.Resolve(placement, manual, known, ammoCorner, ammoX, ammoY, gap, height);
        if ((reportedMask & (1 << (int)spot.Source)) == 0) Report(spot);
        if (hasApplied && spot == applied) return;
        applied = spot; hasApplied = true;
        hud!.Anchor(ToKit(spot.Corner), spot.X, spot.Y);
    }

    // One Info line when the position below the AMMO panel is not possible (once per reason).
    private static void Report(HudSpot spot)
    {
        reportedMask |= 1 << (int)spot.Source;
        if (spot.Source == HudSpotSource.NoAmmoHud)
            Plugin.ModLog.LogInfo($"HUD: Expanded Ballistics AMMO panel not found; using the manual position {spot.Corner} {spot.X:0}/{spot.Y:0}.");
        else if (spot.Source == HudSpotSource.AmmoHudTopCorner)
            Plugin.ModLog.LogInfo($"HUD: the AMMO panel is in a top corner; using the manual position {spot.Corner} {spot.X:0}/{spot.Y:0}.");
    }

    private static HudCorner ToKit(HudAnchor corner) => corner switch
    {
        HudAnchor.TopLeft => HudCorner.TopLeft,
        HudAnchor.TopRight => HudCorner.TopRight,
        HudAnchor.BottomLeft => HudCorner.BottomLeft,
        _ => HudCorner.BottomRight,
    };
}
