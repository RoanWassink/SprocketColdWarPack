using BepInEx.Configuration;
using SprocketExpedition.Core.Encounters;

namespace SprocketExpedition.Game;

// User-facing enemy difficulty. The native Expedition DIFFICULTY screen writes
// these existing BepInEx config entries; generation reads them only for NEW,
// uncommitted offer sets. A saved/selected/retrying battle keeps the economy
// pinned in its own checkpoint, so changing this never rewrites existing plans.
internal static class CampaignDifficulty
{
    private static ConfigEntry<string> multiplier = null!;
    private static ConfigEntry<bool> eraProgression = null!;
    private static ConfigFile? configFile;
    private static bool invalidLogged;

    internal static void Configure(ConfigFile config)
    {
        configFile = config;
        multiplier = config.Bind("Campaign", "DifficultyEnemyBudget",
            DifficultySettings.Format(DifficultySettings.Default.EnemyBudgetMultiplier),
            "Enemy fleet budget multiplier for newly generated Expedition offers. "
            + "Allowed values: 1.0, 1.25, 1.5 (default) or 2.0. Player budget and native "
            + "design statistics are never scaled.");
        eraProgression = config.Bind("Campaign", "DifficultyEraProgression", true,
            "Raise the enemy budget target by 5% per later native era (the difficulty screen shows the final era of the current run). "
            + "Applies only to newly generated, uncommitted offers.");
        _ = Selected;
    }

    internal static IReadOnlyList<decimal> Multipliers => DifficultySettings.Multipliers;

    internal static decimal Selected
    {
        get
        {
            if (multiplier is null) return DifficultySettings.Default.EnemyBudgetMultiplier;
            var parsed = DifficultySettings.ParseMultiplier(multiplier.Value);
            if (!invalidLogged && !string.Equals(multiplier.Value,
                    DifficultySettings.Format(parsed), StringComparison.Ordinal))
            {
                invalidLogged = true;
                Plugin.Logger.LogWarning(
                    $"Expedition difficulty: '{multiplier.Value}' is not a supported multiplier; "
                    + $"using {DifficultySettings.Format(parsed)} without changing saved offers.");
            }
            return parsed;
        }
    }

    internal static bool EraProgression =>
        eraProgression?.Value ?? DifficultySettings.Default.EraProgressionEnabled;

    internal static DifficultySettings Current =>
        new(Selected, EraProgression);

    internal static void SetMultiplier(decimal value)
    {
        if (multiplier is null) return;
        foreach (var allowed in Multipliers)
            if (allowed == value)
            {
                multiplier.Value = DifficultySettings.Format(allowed);
                configFile?.Save();
                return;
            }
    }

    internal static void ToggleEraProgression()
    {
        if (eraProgression is null) return;
        eraProgression.Value = !eraProgression.Value;
        configFile?.Save();
    }

    internal static string Label(decimal value) =>
        SprocketExpedition.Core.Run.ScoreRules.DifficultyName(value) + " (" + DifficultySettings.Format(value)
        + "x) · score ×" + DifficultySettings.Format(SprocketExpedition.Core.Run.ScoreRules.DifficultyFactor(value));

    internal static string Summary =>
        Label(Selected);

    internal static string HonestNote =>
        "More enemy budget means larger enemy forces, and later eras are stronger. "
        + "The budget is a ceiling: vehicles available in an era and the 12-vehicle limit "
        + "can keep a force smaller. Changes apply to future battles; a battle already "
        + "underway keeps its current enemy force.";
}
