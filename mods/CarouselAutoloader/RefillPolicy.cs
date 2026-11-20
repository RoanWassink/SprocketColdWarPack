using System.Globalization;

namespace SprocketCarouselAutoloader;

// 0.2.26: magazine refill state, shared by the log, the inspector and the HUD. Pure and allocation-free.
public enum RefillReason
{
    None, Full, Crew, Stationary, WaitingStill, WaitingEmpty, NoCrew, NoReach, CrewIdle, NoReserve, NoBody, NotEligible, NoCrewTask
}

public enum RefillMode { None, Full, Crew, Stationary }

public static class RefillPolicy
{
    public static RefillReason CrewStatus(bool healthyBreech, bool activeHealthyCrew, bool nativeValid, bool nativeTask) =>
        !healthyBreech ? RefillReason.NotEligible : !activeHealthyCrew ? RefillReason.NoCrew :
        !nativeValid ? RefillReason.NoReach : !nativeTask ? RefillReason.NoCrewTask : RefillReason.Crew;

    /// <summary>
    /// Which refill path a magazine uses this tick. Bustle (unchanged since 0.2.9): the crew refills from
    /// empty and continues while refilling. Revolver (0.2.26): an assigned, healthy crew loader within the
    /// native hand reach tops the drum up whenever it is not full, also while driving; the native loader
    /// task itself only works while the gun is Loading, so its "working" state is not required. Without
    /// such a crew loader the drum refills only when the vehicle stands still.
    /// </summary>
    public static RefillMode Mode(bool revolver, bool full, bool crewLoader, bool refilling, bool emptyForRequest)
    {
        if (full) return RefillMode.Full;
        if (revolver) return crewLoader ? RefillMode.Crew : RefillMode.Stationary;
        return refilling || emptyForRequest ? RefillMode.Crew : RefillMode.None;
    }

    /// <summary>Short reason text; the same words appear in the log and on the HUD.</summary>
    public static string Text(RefillReason reason) => reason switch
    {
        RefillReason.Full => "full",
        RefillReason.Crew => "crew refill",
        RefillReason.Stationary => "stationary refill",
        RefillReason.WaitingStill => "waiting for the vehicle to stand still",
        RefillReason.WaitingEmpty => "crew refill starts when the magazine is empty",
        RefillReason.NoCrew => "no active healthy crew loader assigned",
        RefillReason.NoCrewTask => "assigned crew loader has no native loading task",
        RefillReason.NoReach => "crew loader cannot reach the magazine",
        RefillReason.CrewIdle => "crew loader not currently working",
        RefillReason.NoReserve => "no matching reserve ammunition",
        RefillReason.NoBody => "vehicle body not found",
        RefillReason.NotEligible => "loader not eligible",
        _ => "idle",
    };

    public static bool Blocked(RefillReason reason) => reason is RefillReason.NoCrew or RefillReason.NoReach or
        RefillReason.CrewIdle or RefillReason.NoReserve or RefillReason.NoBody or RefillReason.NotEligible or RefillReason.NoCrewTask;

    public static bool CanTransferFromReserve(bool revolver, bool full, bool hasCrewLoader, bool hasReserve) =>
        !full && hasReserve && (revolver || hasCrewLoader);

    public static bool ReadyMagazineOnly(bool hasAutoloader, bool isCarousel, bool revolver, bool full, bool hasCrewLoader)
    {
        if (!hasAutoloader) return false;
        if (isCarousel) return !hasCrewLoader;
        return full || (!revolver && !hasCrewLoader);
    }
}

// One HUD row (an autoloader of the player's vehicle). Updated at most 10 times per second; the
// strings are rebuilt only when the shown value changes, so the per-frame kit getters never allocate.
public sealed class HudRow
{
    public bool Active;
    public string Title { get; private set; } = "";
    public string Cycle { get; private set; } = "";
    public string Refill { get; private set; } = "";
    public float CycleFraction { get; private set; }
    public float RefillFraction { get; private set; }
    public bool ShowRefillBar { get; private set; }
    public bool Blocked { get; private set; }

    int kind = -1, stored = -1, capacity = -1, cycleTenths = int.MinValue, refillTenths = int.MinValue;
    bool twoPiece, cycling, waiting;
    RefillReason reason = (RefillReason)(-1);

    /// <summary>cycleRemaining/refillRemaining in seconds (NaN = not running).</summary>
    public bool Update(bool revolver, bool twoPiece, int stored, int capacity, bool cycling, bool waiting, double cycleFraction,
        double cycleRemaining, RefillReason reason, double refillFraction, double refillRemaining)
    {
        bool changed = false;
        int k = revolver ? 1 : 0;
        if (k != kind || twoPiece != this.twoPiece || stored != this.stored || capacity != this.capacity)
        {
            kind = k; this.twoPiece = twoPiece; this.stored = stored; this.capacity = capacity;
            Title = HudText.Title(revolver, twoPiece, stored, capacity);
            changed = true;
        }
        int ct = cycling && double.IsFinite(cycleRemaining) ? (int)Math.Ceiling(Math.Max(0, cycleRemaining) * 10) : -1;
        if (ct != cycleTenths || cycling != this.cycling || waiting != this.waiting)
        {
            cycleTenths = ct; this.cycling = cycling; this.waiting = waiting;
            Cycle = HudText.Cycle(cycling, waiting, ct);
            changed = true;
        }
        int rt = double.IsFinite(refillRemaining) ? (int)Math.Ceiling(Math.Max(0, refillRemaining) * 10) : -1;
        if (reason != this.reason || rt != refillTenths)
        {
            this.reason = reason; refillTenths = rt;
            Refill = HudText.Refill(reason, rt);
            Blocked = RefillPolicy.Blocked(reason);
            changed = true;
        }
        CycleFraction = cycling ? (float)Math.Clamp(cycleFraction, 0, 1) : waiting ? 0f : 1f;
        ShowRefillBar = reason is RefillReason.Crew or RefillReason.Stationary;
        RefillFraction = ShowRefillBar ? (float)Math.Clamp(refillFraction, 0, 1) : reason == RefillReason.Full ? 1f : 0f;
        Active = true;
        return changed;
    }
}

public static class HudText
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Title(bool revolver, bool twoPiece, int stored, int capacity) =>
        $"{(revolver ? "Drum" : "Bustle")} {Math.Max(0, stored)}/{Math.Max(0, capacity)}{(twoPiece ? " (two-piece)" : "")}";

    // tenths: remaining time in tenths of a second, or -1 when unknown.
    public static string Cycle(bool cycling, bool waiting, int tenths) =>
        waiting ? "Cycle: waiting for refill" : !cycling ? "Cycle: ready" :
        tenths >= 0 ? "Cycle " + (tenths / 10.0).ToString("0.0", Invariant) + " s" : "Cycle";

    public static string Refill(RefillReason reason, int tenths) => reason switch
    {
        RefillReason.Full => "Full",
        RefillReason.Crew => tenths >= 0 ? "Crew refill " + (tenths / 10.0).ToString("0.0", Invariant) + " s" : "Crew refill",
        RefillReason.Stationary => tenths >= 0 ? "Stationary refill " + (tenths / 10.0).ToString("0", Invariant) + " s" : "Stationary refill",
        RefillReason.WaitingStill => "Refill: stop to refill (no crew loader)",
        RefillReason.WaitingEmpty => "Refill: when empty",
        RefillReason.None => "",
        _ => "Blocked: " + RefillPolicy.Text(reason),
    };
}
