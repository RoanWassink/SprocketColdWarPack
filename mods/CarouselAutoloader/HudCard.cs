using System.Globalization;

namespace SprocketCarouselAutoloader;

// 0.2.27: what one autoloader shows on the HUD and when. Pure and allocation-free while the shown
// values stay the same. The strings of a running action come from HudRow/HudText (0.2.26) unchanged.

/// <summary>
/// "Blocked: reason" stays visible for a while: for <c>seconds</c> after the blocked reason appears or
/// changes, then it hides until the reason changes or something runs again. A reason that is not a blocked
/// one closes the window, so the same reason coming back counts as a new start.
/// </summary>
public struct BlockedWindow
{
    private RefillReason last;
    private float since;

    /// <summary>True while the blocked reason is inside its window. <paramref name="now"/> is game time in seconds.</summary>
    public bool Open(RefillReason reason, float now, float seconds)
    {
        if (!RefillPolicy.Blocked(reason)) { last = RefillReason.None; return false; }
        if (reason != last) { last = reason; since = now; }
        if (!float.IsFinite(seconds)) seconds = AutoloaderHudLayout.DefaultBlockedSeconds;
        return now - since < seconds;
    }

    public void Reset() { last = RefillReason.None; since = 0; }
}

public static class HudCardText
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Ring centre text: remaining time in tenths of a second (-1 = unknown = empty); whole seconds for the slow stationary refill.</summary>
    public static string Centre(int tenths, bool wholeSeconds) =>
        tenths < 0 ? "" : (tenths / 10.0).ToString(wholeSeconds ? "0" : "0.0", Invariant);
}

public sealed class HudCard
{
    /// <summary>The 0.2.26 row: titles, bar fractions and the "Cycle ..." / "Crew refill ..." / "Blocked: ..." texts.</summary>
    public readonly HudRow Row = new();

    /// <summary>Section label without the two-piece suffix, e.g. "Drum 5/18" (the style shows it in capitals).</summary>
    public string Heading { get; private set; } = "";
    public bool TwoPiece { get; private set; }
    public bool IsBelt => kind == 2;
    public string BeltStatus { get; private set; } = "";
    public string BeltStatusColour { get; private set; } = BeltWarningColour;
    public string BeltRate { get; private set; } = "";
    public float BeltFraction { get; private set; }
    public const string BeltReadyColour = "#8fd99c";
    public const string BeltRefillColour = "#f3cf86";
    public const string BeltWarningColour = "#ffcbb3";

    /// <summary>On screen: the fire cycle runs, the refill runs, or a "Blocked" reason is inside its window.</summary>
    public bool Shown { get; private set; }
    /// <summary>Cycle part: running, or the gun waits for a refill while the card is on screen. Never "Cycle: ready".</summary>
    public bool ShowCycle { get; private set; }
    /// <summary>Refill part: crew or stationary refill running, or "Blocked: ..." inside its window. Never "Full".</summary>
    public bool ShowRefill { get; private set; }
    public bool CycleRunning { get; private set; }
    public bool RefillRunning { get; private set; }

    // Ring mode (UI Kit 0.1.8): short names and the remaining seconds for the ring centre.
    public string CycleName { get; private set; } = "";
    public string RefillName { get; private set; } = "";
    public string CycleCentre { get; private set; } = "";
    public string RefillCentre { get; private set; } = "";

    public HudCardShape Shape => new(ShowCycle, CycleRunning, ShowRefill, RefillRunning, IsBelt);

    BlockedWindow window;
    int kind = -1, stored = -1, capacity = -1, cycleTenths = int.MinValue, refillTenths = int.MinValue;
    bool cycling, waiting;
    RefillReason reason = (RefillReason)(-1);
    double beltRpm = -1;
    string beltStatus = "";

    public void UpdateBelt(int stock, int slots, double rpm, string status)
    {
        if (kind != 2 || stored != stock || capacity != slots || beltRpm != rpm || beltStatus != status)
        {
            Heading = $"Belt {stock}/{slots}";
            BeltStatus = status;
            BeltStatusColour = status switch
            {
                "vuurt" or "vol" => BeltReadyColour,
                "bijvullen" => BeltRefillColour,
                _ => BeltWarningColour
            };
            BeltRate = rpm.ToString("0", CultureInfo.InvariantCulture) + " rpm";
            BeltFraction = slots > 0 ? Math.Clamp((float)stock / slots, 0f, 1f) : 0f;
            kind = 2; stored = stock; capacity = slots; beltRpm = rpm; beltStatus = status;
        }
        TwoPiece = false; Shown = true;
        ShowCycle = ShowRefill = CycleRunning = RefillRunning = false;
    }

    /// <summary>
    /// Same inputs as <see cref="HudRow.Update"/> plus game time and the Blocked window length.
    /// Returns true when a shown text changed.
    /// </summary>
    public bool Update(bool revolver, bool twoPiece, int stored, int capacity, bool cycling, bool waiting, double cycleFraction,
        double cycleRemaining, RefillReason reason, double refillFraction, double refillRemaining, float now, float blockedSeconds)
    {
        bool changed = Row.Update(revolver, twoPiece, stored, capacity, cycling, waiting, cycleFraction, cycleRemaining, reason,
            refillFraction, refillRemaining);
        int k = revolver ? 1 : 0;
        if (k != kind || stored != this.stored || capacity != this.capacity)
        {
            kind = k; this.stored = stored; this.capacity = capacity;
            Heading = HudText.Title(revolver, false, stored, capacity);
            changed = true;
        }
        TwoPiece = twoPiece;
        int ct = cycling && double.IsFinite(cycleRemaining) ? (int)Math.Ceiling(Math.Max(0, cycleRemaining) * 10) : -1;
        if (ct != cycleTenths || cycling != this.cycling || waiting != this.waiting)
        {
            cycleTenths = ct; this.cycling = cycling; this.waiting = waiting;
            CycleName = HudText.Cycle(cycling, waiting, -1);
            CycleCentre = HudCardText.Centre(ct, false);
            changed = true;
        }
        int rt = double.IsFinite(refillRemaining) ? (int)Math.Ceiling(Math.Max(0, refillRemaining) * 10) : -1;
        if (reason != this.reason || rt != refillTenths)
        {
            this.reason = reason; refillTenths = rt;
            RefillName = HudText.Refill(reason, -1);
            RefillCentre = HudCardText.Centre(rt, reason == RefillReason.Stationary);
            changed = true;
        }
        bool refillRuns = reason is RefillReason.Crew or RefillReason.Stationary;
        bool blockedOpen = window.Open(reason, now, blockedSeconds);
        CycleRunning = cycling;
        RefillRunning = refillRuns;
        ShowRefill = refillRuns || blockedOpen;
        Shown = cycling || ShowRefill;
        ShowCycle = Shown && (cycling || waiting);
        return changed;
    }

    /// <summary>The autoloader stopped reporting (vehicle gone or no longer the player's): hide and forget the Blocked window.</summary>
    public void Reset()
    {
        window.Reset();
        Shown = ShowCycle = ShowRefill = CycleRunning = RefillRunning = false;
        Row.Active = false;
    }
}
