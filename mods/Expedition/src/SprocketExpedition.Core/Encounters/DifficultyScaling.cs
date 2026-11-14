namespace SprocketExpedition.Core.Encounters;

// Authored enemy-budget difficulty for adventure policy 5. The multiplier only
// raises the enemy fleet ceiling used by the exact checked-price planner; the
// player's purchasing budget, native prices and native design statistics are
// never modified. Policy 1-4 callers must not consult this type.
public sealed record DifficultySettings(decimal EnemyBudgetMultiplier, bool EraProgressionEnabled)
{
    public static DifficultySettings Default { get; } = new(1.5m, true);

    // Discrete choices offered by the native DIFFICULTY screen. 1.0 is the
    // historical baseline; 1.5 is the default "generally harder" setting.
    public static IReadOnlyList<decimal> Multipliers { get; } =
        new[] { 1.0m, 1.25m, 1.5m, 2.0m };

    public bool IsValid =>
        EnemyBudgetMultiplier is >= DifficultyScaling.MinimumMultiplier
            and <= DifficultyScaling.MaximumBaseMultiplier;

    public static decimal ParseMultiplier(string? text)
    {
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return Default.EnemyBudgetMultiplier;
        }

        foreach (var candidate in Multipliers)
            if (candidate == parsed) return candidate;
        return Default.EnemyBudgetMultiplier;
    }

    public static string Format(decimal multiplier) =>
        multiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

public static class DifficultyScaling
{
    public const decimal MinimumMultiplier = 1m;
    public const decimal MaximumBaseMultiplier = 2m;
    // A pinned request may also include the existing ExperimentalContract
    // multiplier (x1.2) on top of the selected difficulty.
    public const decimal MaximumRequestMultiplier = 2.4m;
    // Modest, documented era progression: +5% of the fleet ceiling per era index
    // (index 5 = +25%) before the selected base multiplier and risk factor. The
    // index follows the native era list, so later native eras keep getting
    // stronger up to MaximumEraIndex.
    public const decimal ProgressionStep = 0.05m;
    public const int MaximumEraIndex = 15;
    // Policy 5 persists the fully scaled base x progression x dangerous factor:
    // 2.4 x (1 + 0.05 x 15) x 1.35 = 5.67. The earlier limit was 4.1 (index 5).
    public const decimal MaximumPersistedMultiplier = 5.7m;

    // Honest difficulty-page sentence. The old text promised +25% while the code
    // adds 5% per era index up to index 15. No run schedule yet means no final
    // era value is invented.
    public static string FinalEraNote(int? eraCount)
    {
        if (eraCount is not > 0)
            return "+5% per era; later eras are stronger.";
        var percent = checked((ProgressionFactor(eraCount.Value - 1) - 1m) * 100m);
        return "+5% per era; the final era of this run: +"
            + percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    public static decimal ProgressionFactor(int eraIndex)
    {
        var bounded = Math.Clamp(eraIndex, 0, MaximumEraIndex);
        try
        {
            return checked(1m + ProgressionStep * bounded);
        }
        catch (OverflowException)
        {
            return 1m;
        }
    }

    // Mirrors the existing adventure risk factor (Dangerous scales the enemy
    // budget by 1.35 in addition to the risk target profile). Legacy policies
    // pass progression 1 and therefore keep their original arithmetic.
    public static bool TryEffectiveMultiplier(
        decimal requested, bool eraProgression, int eraIndex, EncounterRisk risk,
        out decimal multiplier)
    {
        multiplier = 0m;
        if (requested is < MinimumMultiplier or > MaximumRequestMultiplier) return false;
        try
        {
            var progression = eraProgression ? ProgressionFactor(eraIndex) : 1m;
            multiplier = checked(requested * progression
                * (risk == EncounterRisk.Dangerous ? 1.35m : 1m));
        }
        catch (OverflowException)
        {
            multiplier = 0m;
            return false;
        }

        return multiplier >= MinimumMultiplier && multiplier <= MaximumPersistedMultiplier;
    }

    public static bool TryScaledBudget(decimal budget, decimal multiplier, out decimal scaled)
    {
        scaled = 0m;
        if (budget <= 0m || multiplier < MinimumMultiplier) return false;
        try
        {
            scaled = checked(budget * multiplier);
        }
        catch (OverflowException)
        {
            return false;
        }

        return scaled > 0m;
    }
}
