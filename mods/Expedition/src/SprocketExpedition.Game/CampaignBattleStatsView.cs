using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SprocketExpedition.Game.UI;

// One own tank on the result screen and in the battle log. All counters are
// measured from native events; unknown values stay null so the text can say "—".
internal sealed record BattleTankStats(
    string Name, bool IsPlayer, bool Destroyed,
    int Fired, int Hit, int Taken, int Survived, int Kills,
    decimal? LongestHitM, string? DestroyedByName, decimal? DestroyedByDistanceM);

internal sealed record BattleStatsReport(
    string AttemptKey, string Outcome, double DurationSeconds,
    IReadOnlyList<BattleTankStats> Tanks);

// Pure text mapping shared by the Kit result screen and the always-on log line.
internal static class BattleStatsText
{
    internal const string Unknown = "—";

    internal static string Accuracy(int fired, int hit)
    {
        if (fired <= 0) return Unknown;
        var percent = decimal.Round(hit * 100m / fired, 0, MidpointRounding.AwayFromZero);
        return percent.ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    internal static string Metres(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    internal static string Clock(double seconds)
    {
        var total = (int)Math.Max(0d, Math.Round(seconds));
        return (total / 60).ToString(CultureInfo.InvariantCulture) + ":"
            + (total % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    // "King Tiger — destroyed by Panther at 412 m", or an honest unknown.
    internal static string Status(BattleTankStats tank)
    {
        if (!tank.Destroyed) return $"{Name(tank.Name)} — alive";
        if (string.IsNullOrWhiteSpace(tank.DestroyedByName)) return $"{Name(tank.Name)} — destroyed (cause unknown)";
        var by = $"destroyed by {Name(tank.DestroyedByName!)} at "
            + (tank.DestroyedByDistanceM is { } distance ? Metres(distance) + " m" : Unknown + " m");
        return $"{Name(tank.Name)} — {by}";
    }

    // Losses block: one line per own tank, at most six, then "+n more".
    internal static IReadOnlyList<string> LossesLines(IReadOnlyList<BattleTankStats> tanks, int maximum = 6)
    {
        var lines = tanks.Take(maximum).Select(Status).ToList();
        if (tanks.Count > maximum) lines.Add($"+{tanks.Count - maximum} more");
        return lines;
    }

    // Battle stats block: the player's tank separately, then one team-total line.
    internal static IReadOnlyList<string> BattleLines(BattleStatsReport report)
    {
        var tanks = report.Tanks;
        var lines = new List<string>();
        var player = tanks.FirstOrDefault(tank => tank.IsPlayer);
        if (player is not null) lines.Add($"You — {Name(player.Name)}: {Summary(player)}");
        lines.Add("Team: " + Summary(tanks));
        var longest = tanks.Where(tank => tank.LongestHitM is not null)
            .Select(tank => tank.LongestHitM!.Value).DefaultIfEmpty(0m).Max();
        var tail = "Battle " + Clock(report.DurationSeconds);
        lines.Add(longest > 0m ? $"Longest hit {Metres(longest)} m · {tail}" : tail);
        return lines;
    }

    private static string Summary(BattleTankStats tank) =>
        $"fired {tank.Fired} · hit {tank.Hit} · acc {Accuracy(tank.Fired, tank.Hit)}"
        + $" · taken {tank.Taken} · survived {tank.Survived} · kills {tank.Kills}";

    private static string Summary(IReadOnlyList<BattleTankStats> tanks)
    {
        var fired = tanks.Sum(tank => tank.Fired);
        var hit = tanks.Sum(tank => tank.Hit);
        return $"fired {fired} · hit {hit} · acc {Accuracy(fired, hit)}"
            + $" · taken {tanks.Sum(tank => tank.Taken)} · survived {tanks.Sum(tank => tank.Survived)}"
            + $" · kills {tanks.Sum(tank => tank.Kills)}";
    }

    // One always-on line per own tank. Format fixed by the 0.3.6 brief.
    internal static string LogLine(string outcome, BattleTankStats tank)
    {
        var role = tank.IsPlayer ? "player" : "ally";
        string destroyedBy;
        if (!tank.Destroyed) destroyedBy = "alive";
        else if (string.IsNullOrWhiteSpace(tank.DestroyedByName)) destroyedBy = "unknown";
        else destroyedBy = "'" + Clean(tank.DestroyedByName!) + "' at "
            + (tank.DestroyedByDistanceM is { } distance ? Metres(distance) + " m" : Unknown + " m");
        return $"Expedition battle stats: {outcome}; {role} '{Clean(tank.Name)}'"
            + $" fired={tank.Fired} hit={tank.Hit} acc={Accuracy(tank.Fired, tank.Hit)}"
            + $" taken={tank.Taken} survived={tank.Survived} kills={tank.Kills} destroyedBy={destroyedBy}";
    }

    private static string Name(string? name) => string.IsNullOrWhiteSpace(name) ? "Unknown tank" : Clean(name!);

    private static string Clean(string name) => name.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
