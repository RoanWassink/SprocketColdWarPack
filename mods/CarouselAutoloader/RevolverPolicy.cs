namespace SprocketCarouselAutoloader;

/// <summary>
/// Pure revolver-drum limits and timing. The drum itself is a native ammo rack; since 0.2.23
/// its capacity is min(native packer, one drum) (RevolverDrum).
/// </summary>
public static class RevolverPolicy
{
    // 0.2.24 (BESLUITEN 2026-11-02 "Instelbaar, ruimer"): config [Revolver] MaxCaliberMm (155, the T58
    // drum) and MaxRoundLengthMm (0 = no fixed limit: the rack decides through the drum).
    public const double DefaultMaxCaliberMm = 155, DefaultMaxRoundLengthMm = 0;
    public static double MaxCaliberMm { get; set; } = DefaultMaxCaliberMm;
    public static double MaxRoundLengthMm { get; set; } = DefaultMaxRoundLengthMm;
    // 0.2.25 (BESLUITEN 2026-11-02 "Ja, maak hem sneller"): the fire cycle is 0.6 x the bustle cycle (was
    // 1.5 x); refilling stays slow and is computed from the BUSTLE cycle (two-piece factor included), so
    // the faster fire cycle never speeds up the refill. Config [Revolver] CycleFactor, RefillCycleFactor
    // and MinimumRefillSeconds; a value <= 0, NaN or infinite falls back to the default.
    public const double DefaultCycleFactor = 0.6, DefaultRefillCycleFactor = 3, DefaultMinimumRefillSeconds = 10;
    static double cycleFactor = DefaultCycleFactor, refillCycleFactor = DefaultRefillCycleFactor, minimumRefillSeconds = DefaultMinimumRefillSeconds;
    public static double CycleFactor { get => cycleFactor; set => cycleFactor = Positive(value, DefaultCycleFactor); }
    public static double RefillCycleFactor { get => refillCycleFactor; set => refillCycleFactor = Positive(value, DefaultRefillCycleFactor); }
    public static double MinimumRefillSeconds { get => minimumRefillSeconds; set => minimumRefillSeconds = Positive(value, DefaultMinimumRefillSeconds); }
    public const double StationarySpeedMs = .5;
    public const double StationarySeconds = 3;

    // storedLengthMm: the complete round, or with two-piece ammunition the longest piece.
    public static bool Fits(double caliberMm, double storedLengthMm) =>
        double.IsFinite(caliberMm) && double.IsFinite(storedLengthMm) &&
        caliberMm > 0 && storedLengthMm > 0 &&
        caliberMm <= MaxCaliberMm && (!(MaxRoundLengthMm > 0) || storedLengthMm <= MaxRoundLengthMm);

    // A config value is usable only when it is finite and above zero; anything else is the default.
    public static double Positive(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;

    // Fire cycle of the revolver for the bustle cycle of the same ammunition (no two-piece factor).
    public static double CycleSeconds(double bustleSeconds) => CycleFactor * bustleSeconds;

    // Bustle cycle of the round in the rack: the plain bustle cycle, x 1.25 with two-piece ammunition.
    // This is the basis of the refill; the revolver factor never enters it.
    public static double BaseCycleSeconds(double bustleSeconds, bool twoPiece) =>
        twoPiece ? TwoPiecePolicy.CycleSeconds(bustleSeconds) : bustleSeconds;

    // Fire cycle shown and used in the game: revolver factor (revolver only), then two-piece x 1.25.
    public static double FireCycleSeconds(double bustleSeconds, bool revolver, bool twoPiece) =>
        BaseCycleSeconds(revolver ? CycleSeconds(bustleSeconds) : bustleSeconds, twoPiece);

    // Refill per round (per pair with two-piece): max(minimum, factor x the BUSTLE cycle incl. two-piece).
    public static double RefillSecondsPerRound(double bustleCycle) =>
        Math.Max(MinimumRefillSeconds, RefillCycleFactor * bustleCycle);

    public static double RefillSeconds(double bustleSeconds, bool twoPiece) =>
        RefillSecondsPerRound(BaseCycleSeconds(bustleSeconds, twoPiece));

    public static double StillSeconds(double previous, double speedMs, double dt)
    {
        // A missing or invalid speed cannot prove that the vehicle is standing still.
        if (!double.IsFinite(speedMs) || speedMs < 0 || speedMs >= StationarySpeedMs) return 0;
        var still = double.IsFinite(previous) && previous > 0 ? previous : 0;
        if (!double.IsFinite(dt) || dt <= 0) return still;
        var next = still + dt;
        return double.IsFinite(next) ? next : still;
    }

    public static bool IsStationary(double stillSeconds) =>
        double.IsFinite(stillSeconds) && stillSeconds >= StationarySeconds;

    public static double MechanismMassKg(int capacity) => 60 + 5.0 * Math.Max(0, capacity);

    public const string TooSmallText = "Too small for a revolver drum";

    // Exactly one reason why a revolver cannot take this ammunition, or null when it can. "Too small"
    // only when the round is within the limits and the rack is long enough, but no chamber fits.
    public static string? Refusal(double caliberMm, double storedLengthMm, double rackLengthMm, int chambers, bool twoPiece)
    {
        if (!(caliberMm <= MaxCaliberMm))
            return $"Calibre {caliberMm:0} mm above the revolver limit {MaxCaliberMm:0} mm (config Revolver.MaxCaliberMm)";
        if (MaxRoundLengthMm > 0 && storedLengthMm > MaxRoundLengthMm)
            return $"{(twoPiece ? "Longest piece" : "Round")} {storedLengthMm:0} mm above the revolver limit {MaxRoundLengthMm:0} mm (config Revolver.MaxRoundLengthMm)";
        if (RevolverDrum.Rows(rackLengthMm, storedLengthMm) <= 0)
            return twoPiece ? $"Rack shorter than the longest piece ({storedLengthMm:0} mm): lengthen it"
                : $"Rack shorter than the round ({storedLengthMm:0} mm): lengthen it or use two-piece";
        return chambers <= 0 ? TooSmallText : null;
    }

    // One drum (0.2.23). Plain "x" instead of a multiplication sign: safe in every UI font.
    public static string DrumText(int chambers, int rows, int rounds)
    {
        chambers = Math.Max(0, chambers); rows = Math.Max(0, rows); rounds = Math.Max(0, rounds);
        return $"1 drum: {chambers} chamber{(chambers == 1 ? "" : "s")} x {rows} row{(rows == 1 ? "" : "s")} = {rounds} round{(rounds == 1 ? "" : "s")}";
    }
}
