namespace SprocketCarouselAutoloader;

// 0.2.27: pure HUD placement, height, line choice and kit-ring gate. No Unity, no BepInEx and no UI Kit
// types here, so the test project compiles this file as it is.
public enum HudAnchor { TopLeft, TopRight, BottomLeft, BottomRight }

public enum HudPlace { BelowAmmo, Manual }

public enum HudSpotSource { BelowAmmo, Manual, NoAmmoHud, AmmoHudTopCorner }

/// <summary>Corner plus inward offsets in kit reference px (1080p), and where the spot came from.</summary>
public readonly record struct HudSpot(HudAnchor Corner, float X, float Y, HudSpotSource Source);

/// <summary>Which parts of one HUD card are on screen; enough to know its height.</summary>
public readonly record struct HudCardShape(bool Cycle, bool CycleRunning, bool Refill, bool RefillRunning);

public static class AutoloaderHudLayout
{
    /// <summary>Same width as the Expanded Ballistics AMMO panel (kit reference px).</summary>
    public const float Width = 300f;
    /// <summary>Lowest Y of a HUD that sits below the AMMO panel; below this it would leave the screen.</summary>
    public const float MinY = 8f;
    public const float DefaultGap = 8f;
    public const float DefaultBlockedSeconds = 5f;
    public const int MaxRows = 4;
    public const float MaxHeight = 420f;

    // Kit metrics, reference px (docs\STYLE.md, kit source 0.1.x): HUD padding 10; a label line is
    // round(font size x 1.35), so Section (15) = 20 and Small (12) = 16; chip Tag 17; progress bar 4.
    public const float Pad = 10f, BodyGap = 4f, CardGap = 2f, HeadLine = 20f, TagChip = 17f, SmallLine = 16f, BarHeight = 4f;
    /// <summary>Diameter of the kit ring (0.1.8 and later) in front of a running action.</summary>
    public const float RingSize = 40f;

    public static bool IsBottom(HudAnchor corner) => corner is HudAnchor.BottomLeft or HudAnchor.BottomRight;

    /// <summary>The four corner names of the config, case-insensitive; no allocation.</summary>
    public static bool TryParseCorner(string? text, out HudAnchor corner)
    {
        corner = HudAnchor.BottomRight;
        if (text == null) return false;
        if (string.Equals(text, "TopLeft", StringComparison.OrdinalIgnoreCase)) { corner = HudAnchor.TopLeft; return true; }
        if (string.Equals(text, "TopRight", StringComparison.OrdinalIgnoreCase)) { corner = HudAnchor.TopRight; return true; }
        if (string.Equals(text, "BottomLeft", StringComparison.OrdinalIgnoreCase)) { corner = HudAnchor.BottomLeft; return true; }
        if (string.Equals(text, "BottomRight", StringComparison.OrdinalIgnoreCase)) { corner = HudAnchor.BottomRight; return true; }
        return false;
    }

    /// <summary>"Manual" (any case) means manual; anything else is the default, BelowAmmo.</summary>
    public static HudPlace ParsePlacement(string? text) =>
        string.Equals(text, "Manual", StringComparison.OrdinalIgnoreCase) ? HudPlace.Manual : HudPlace.BelowAmmo;

    /// <summary>
    /// Where the HUD goes. BelowAmmo with a bottom-corner AMMO panel: same corner and X, and the top of
    /// this HUD <paramref name="gap"/> below the bottom of the AMMO panel, so Y = ammoY - gap - height
    /// (never below <see cref="MinY"/>). With a top-corner AMMO panel, without the AMMO panel, or with
    /// Placement = Manual, the manual spot is used (the Source says which case applied).
    /// An unreadable AMMO corner counts as BottomRight, as the AMMO panel itself does.
    /// </summary>
    public static HudSpot Resolve(HudPlace placement, HudSpot manual, bool ammoKnown, string? ammoCorner,
        float ammoX, float ammoY, float gap, float height)
    {
        if (placement == HudPlace.Manual) return manual with { Source = HudSpotSource.Manual };
        if (!ammoKnown || !float.IsFinite(ammoX) || !float.IsFinite(ammoY)) return manual with { Source = HudSpotSource.NoAmmoHud };
        if (!TryParseCorner(ammoCorner, out var corner)) corner = HudAnchor.BottomRight;
        if (!IsBottom(corner)) return manual with { Source = HudSpotSource.AmmoHudTopCorner };
        if (!float.IsFinite(gap) || gap < 0) gap = DefaultGap;
        if (!float.IsFinite(height) || height < 0) height = 0;
        return new HudSpot(corner, ammoX, Math.Max(MinY, ammoY - gap - height), HudSpotSource.BelowAmmo);
    }

    /// <summary>
    /// Height of one card as the kit lays it out: head row (Section label, Tag chip), then the cycle part
    /// and the refill part, separated by <see cref="CardGap"/>. With bars: label + bar for the cycle, label
    /// (+ bar while running) for the refill. With rings: one row per part, a ring row while that action
    /// runs and a plain label row otherwise (waiting, blocked).
    /// </summary>
    public static float CardHeight(HudCardShape shape, bool rings)
    {
        float h = Math.Max(HeadLine, TagChip);
        int parts = 1;
        if (shape.Cycle)
        {
            if (rings) { h += shape.CycleRunning ? Math.Max(RingSize, SmallLine) : SmallLine; parts++; }
            else { h += SmallLine + BarHeight; parts += 2; }
        }
        if (shape.Refill)
        {
            if (rings) { h += shape.RefillRunning ? Math.Max(RingSize, SmallLine) : SmallLine; parts++; }
            else { h += SmallLine; parts++; if (shape.RefillRunning) { h += BarHeight; parts++; } }
        }
        return h + CardGap * (parts - 1);
    }

    /// <summary>Total HUD height: padding top and bottom, the cards, and the gap between cards (capped like the kit's MaxHeight).</summary>
    public static float Height(ReadOnlySpan<HudCardShape> cards, bool rings)
    {
        float h = 2 * Pad;
        for (int i = 0; i < cards.Length; i++) h += CardHeight(cards[i], rings);
        if (cards.Length > 1) h += BodyGap * (cards.Length - 1);
        return Math.Min(MaxHeight, h);
    }

    /// <summary>
    /// The cards on screen: the first <c>picked.Length</c> autoloaders (in tracking order) that have something
    /// running. Returns how many were picked; their indices are in <paramref name="picked"/>.
    /// </summary>
    public static int Select(ReadOnlySpan<bool> shown, Span<int> picked)
    {
        int n = 0;
        for (int i = 0; i < shown.Length && n < picked.Length; i++)
            if (shown[i]) picked[n++] = i;
        return n;
    }
}

/// <summary>Kit rings need UI Kit 0.1.8 or later; with an older kit the HUD keeps its bars.</summary>
public static class HudRingGate
{
    public static readonly Version MinimumKit = new(0, 1, 8);

    /// <summary>True when the loaded kit plugin version is known and is 0.1.8 or later.</summary>
    public static bool Supported(Version? kit) => kit != null && kit >= MinimumKit;

    /// <summary>The same gate from the parts of the BepInEx plugin version (a pre-release tag does not matter).</summary>
    public static bool Supported(int major, int minor, int patch) => Supported(new Version(Math.Max(0, major), Math.Max(0, minor), Math.Max(0, patch)));
}
