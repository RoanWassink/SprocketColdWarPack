using System.Globalization;

namespace SprocketExpedition.Core.Run;

public sealed record BattleScore(decimal Points, decimal RetryFactor, bool Flawless, string Breakdown);

public static class ScoreRules
{
    public static decimal DifficultyFactor(decimal enemyMultiplier) => enemyMultiplier switch
    { 1m => .75m, 1.25m => 1m, 1.5m => 1.25m, 2m => 1.6m,
        _ => throw new ArgumentOutOfRangeException(nameof(enemyMultiplier)) };

    public static string DifficultyName(decimal multiplier) => multiplier switch
    { 1m => "Recruit", 1.25m => "Regular", 1.5m => "Veteran", 2m => "Ace",
        _ => throw new ArgumentOutOfRangeException(nameof(multiplier)) };

    public static decimal RetryFactor(int failures)
    {
        if (failures < 0) throw new ArgumentOutOfRangeException(nameof(failures));
        var factor = 1m;
        for (var i = 0; i < failures && factor > .25m; i++) factor *= .75m;
        return Math.Max(.25m, factor);
    }

    public static BattleScore Battle(int eraIndex, EncounterRisk risk, bool boss,
        int? friendliesLost, int roundAttempts, decimal difficulty, bool ironman)
    {
        if (eraIndex < 0) throw new ArgumentOutOfRangeException(nameof(eraIndex));
        var riskFactor = risk switch
        { EncounterRisk.Cautious => .75m, EncounterRisk.Standard => 1m,
          EncounterRisk.Dangerous => 1.5m, _ => throw new ArgumentOutOfRangeException(nameof(risk)) };
        var flawless = friendliesLost == 0;
        var retry = RetryFactor(roundAttempts);
        var points = checked(100m * (1m + .5m * eraIndex) * riskFactor
            * (boss ? 3m : 1m) * (flawless ? 1.25m : 1m) * retry
            * DifficultyFactor(difficulty) * (ironman ? 1.3m : 1m));
        var facts = new List<string> { $"Era x{F(1m + .5m * eraIndex)}", $"{risk} x{F(riskFactor)}" };
        if (boss) facts.Add("boss x3");
        if (flawless) facts.Add("flawless +25%");
        else if (friendliesLost is null) facts.Add("losses unverified: no flawless bonus");
        if (roundAttempts > 0) facts.Add($"{roundAttempts} retries x{F(retry)}");
        facts.Add($"{DifficultyName(difficulty)} x{F(DifficultyFactor(difficulty))}");
        if (ironman) facts.Add("Ironman x1.3");
        return new(points, retry, flawless, string.Join(" · ", facts));
    }

    private static string F(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

public static class ContactRules
{
    public const double GraceSeconds = 60;
    public static bool HasContact(double combatSeconds, int? enemiesDestroyed, int? friendliesLost,
        bool nativeHit = false) => nativeHit || enemiesDestroyed > 0 || friendliesLost > 0
        || double.IsFinite(combatSeconds) && combatSeconds >= GraceSeconds;
}

public sealed record EditorReturnContext(bool Contact, int? EnemiesDestroyed = null,
    int? FriendliesLost = null, bool? PlayerTankSurvived = null, decimal? DurationSeconds = null);
