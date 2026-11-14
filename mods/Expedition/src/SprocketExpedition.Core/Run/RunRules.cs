using SprocketExpedition.Core;

namespace SprocketExpedition.Core.Run;

// Approved v1 tuning. These coefficients are authored starting values; the engine
// only applies them and never derives era dates or native prices.
internal static class RunRules
{
    public const int SchemaVersion = 1;
    public const int FiniteRounds = 18;
    public const int BlocksPerFiniteRun = 6;
    public const int BattlesPerBlock = 3;
    public const int MaxFriendlyIncludingPlayer = 8;
    public const int MaxEnemies = 12;
    public const decimal EndlessGrowth = 1.15m;
    public const decimal SupplyBonusFactor = 0.01m;

    // Indexed by the actual canonical era ordinal. An optional five-era schedule
    // simply omits its last multiplier; entries are never shifted. More than six eras continue the table through EraBaseMultiplier.
    public static readonly decimal[] EraBaseMultipliers = { 1m, 1.6m, 2.5m, 3.5m, 5m, 8m };

    // Growth per era beyond the authored table (index 6 onward). Runs with more
    // than six native eras continue the table: 8 -> 10 -> 12.5 -> ...
    public const decimal ExtraEraGrowth = 1.25m;

    // Base budget multiplier for a schedule position. Positions 0-5 keep their
    // authored values, so pinned five/six-era runs are unchanged.
    public static decimal EraBaseMultiplier(int eraIndex)
    {
        if (eraIndex < 0) throw new ArgumentOutOfRangeException(nameof(eraIndex));
        if (eraIndex < EraBaseMultipliers.Length) return EraBaseMultipliers[eraIndex];
        var value = EraBaseMultipliers[^1];
        for (var step = EraBaseMultipliers.Length; step <= eraIndex; step++)
            value = checked(value * ExtraEraGrowth);
        return value;
    }

    public static int SupplyRewardFor(EncounterRisk risk) => risk switch
    {
        EncounterRisk.Cautious => 1,
        EncounterRisk.Standard => 2,
        EncounterRisk.Dangerous => 3,
        _ => 0
    };

    // Budget arithmetic is decimal. Calculated block budgets are quantized down to
    // six decimal places at block boundaries only; native quote comparison never
    // uses a two-decimal display value.
    public static decimal FloorToMicro(decimal value) =>
        decimal.Floor(value * 1_000_000m) / 1_000_000m;

    public static decimal SupplyBonus(decimal nextBaseBudget, int points) =>
        FloorToMicro(nextBaseBudget * points * SupplyBonusFactor);
}
