using System.Globalization;

namespace SprocketCarouselAutoloader;

/// <summary>Phase-one, complete-round belt policy. No Unity or native storage state.</summary>
public static class Belt
{
    public const string PartGuid = "89bbda52-50ed-4ad6-9944-b59ef842e0fa";
    public const string DefaultTable = "9:750,15:550,23:900,30:200,40:150,57:120";
    public static bool Enabled = true;
    public static double MaxCalibre = 57, MaxFeedDistance = 1.5, BoxMassKg = 25, MassPerRoundKg = .1;
    public static double MaxRpm = 1200, RefillSecondsPerRound = 1, StationarySeconds = 3, StationarySpeed = .5;
    private static (double Calibre, double Rpm)[] table = ParseDefault();

    public static bool Fits(double diameter, double maximum) => double.IsFinite(diameter) && diameter > 0 &&
        (maximum == 0 || diameter <= Nonnegative(maximum, 57));
    public static bool Fits(double diameter) => Fits(diameter, MaxCalibre);
    public static double Nonnegative(double value, double fallback) => double.IsFinite(value) && value >= 0 ? value : fallback;
    public static double Positive(double value, double fallback) => double.IsFinite(value) && value > 0 ? value : fallback;
    public static double MechanismMassKg(int capacity) => Nonnegative(BoxMassKg, 25) + Nonnegative(MassPerRoundKg, .1) * Math.Max(0, capacity);
    public static bool TwoPiece(bool saved) => false;

    /// <summary>Call once when binding config; false asks the caller to emit one warning.</summary>
    public static bool SetTable(string text)
    {
        bool valid = TryParse(text, out var parsed);
        table = valid ? parsed : ParseDefault();
        return valid;
    }
    private static (double, double)[] ParseDefault() { TryParse(DefaultTable, out var result); return result; }
    private static bool TryParse(string text, out (double, double)[] result)
    {
        result = Array.Empty<(double, double)>();
        if (string.IsNullOrWhiteSpace(text)) return false;
        var items = text.Split(',');
        var values = new (double, double)[items.Length];
        double previous = 0;
        for (int i = 0; i < items.Length; i++)
        {
            var pair = items[i].Split(':');
            if (pair.Length != 2 || !double.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var calibre) ||
                !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rpm) ||
                !double.IsFinite(calibre) || calibre <= previous || !double.IsFinite(rpm) || rpm <= 0) return false;
            previous = calibre; values[i] = (calibre, rpm);
        }
        result = values; return true;
    }
    public static double Rpm(double calibre)
    {
        double rpm = table[table.Length - 1].Rpm;
        for (int i = 0; i < table.Length; i++)
            if (calibre <= table[i].Calibre) { rpm = table[i].Rpm; break; }
        return Math.Min(rpm, Math.Min(1200, Positive(MaxRpm, 1200)));
    }
    public static double Cycle(double calibre) => 60 / Rpm(calibre);
    public static double Still(double previous, double speed, double dt) =>
        double.IsFinite(speed) && speed >= 0 && speed < Positive(StationarySpeed, .5) && double.IsFinite(dt) && dt > 0
            ? Math.Max(0, previous) + dt : 0;
    public static bool Stationary(double seconds) => seconds >= Nonnegative(StationarySeconds, 3);
    public static double RefillTime(double efficiency) => Math.Max(.5, Nonnegative(RefillSecondsPerRound, 1)) /
        Math.Min(Positive(efficiency, 1), 1);
    public static bool AdvanceRefill(ref double progress, bool crew, bool stationary, bool full, bool reserve, double dt, double efficiency)
    {
        if (!crew || !stationary || full || !reserve) { progress = 0; return false; }
        if (!double.IsFinite(dt) || dt <= 0) return false;
        progress += dt / RefillTime(efficiency);
        if (progress + 1e-9 < 1) return false;
        progress = 0; return true; // Caller transfers ONE finite round; no catch-up or synthetic stock.
    }
    public static string Status(bool cycling, int stored, int capacity, bool refilling, bool stationary, bool crew) =>
        cycling ? "vuurt" : capacity > 0 && stored >= capacity ? "vol" : !stationary ? "rijdt" : !crew ? "geen lader" :
        refilling ? "bijvullen" : stored == 0 ? "leeg" : "vuurt";
}

public static class LoaderIdentity
{
    public static LoaderKind Kind(string? guid) => guid switch
    {
        "f8db54a4-6f03-48c4-b163-990b2d3ec7c2" => LoaderKind.Semi,
        "3189ea9f-9b42-4204-977f-8cebdcf4f77c" => LoaderKind.Revolver,
        Belt.PartGuid => LoaderKind.Belt,
        _ => LoaderKind.Bustle
    };
}

/// <summary>Cache the one physical/typed source. Native wrappers are retained until topology changes.</summary>
public struct BeltSourceCache<T> where T : class
{
    private IntPtr owner;
    private int type;
    private T? source;
    public bool TryGet(IntPtr aggregate, int requested, out T? value)
    { value = source; return source != null && owner == aggregate && type == requested; }
    public void Store(IntPtr aggregate, int requested, T value) { owner = aggregate; type = requested; source = value; }
    public void Clear() { source = null; owner = IntPtr.Zero; type = -1; }
}

/// <summary>
/// Native Update has four exclusive branches: prepare, pickup, travel, ram (>= comparisons).
/// The first three consume one call each. Put the variable work into ram and carry rounding
/// error into the next round, bounded to one quantum. Never write native phase/progress/state.
/// </summary>
public struct BeltCycleClock
{
    public const int Phases = 4;
    public bool Active { get; private set; }
    public double Budget { get; private set; }
    public double Elapsed { get; private set; }
    private double credit, target, frameQuantum;
    public static int Steps(double cycle, double frame)
    {
        if (!double.IsFinite(frame) || frame <= 0 || !double.IsFinite(cycle) || cycle <= 0 || cycle + frame * 1e-6 >= Phases * frame) return 1;
        // A loaded gun can fire on a frame boundary. ceil(4*frame/cycle) alone can leave
        // 4 phases straddling two frames at 30 fps / 1200 rpm, limiting firing to 900 rpm.
        // Use the fewest uniform subdivisions that fit four phases in the SHORT cycle.
        int shortFrames = Math.Max(1, (int)Math.Floor(cycle / frame + 1e-6));
        return (int)Math.Ceiling((double)Phases / shortFrames);
    }
    public void Begin(double cycle, double quantum, double frame = 0)
    {
        target = cycle; Elapsed = 0; Active = true;
        frameQuantum = Math.Max(quantum, frame);
        credit = Math.Clamp(credit, -frameQuantum, frameQuantum);
        Budget = Math.Max(Phases * quantum, cycle + credit);
    }
    public float RamWork(double current, double quantum) =>
        (float)Math.Max(0, current + Math.Max(quantum * .5, Budget - Elapsed - quantum * 1e-5));
    public void Step(double quantum, bool loaded)
    {
        if (!Active) return;
        Elapsed += quantum;
        if (loaded) { credit = Math.Clamp(credit + target - Elapsed, -frameQuantum, frameQuantum); Active = false; }
    }
    public void FinishFrame(double unused) { if (!Active) { Elapsed += unused; credit = Math.Clamp(credit - unused, -frameQuantum, frameQuantum); } }
    public void Reset() { this = default; }
}
