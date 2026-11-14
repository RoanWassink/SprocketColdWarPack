using System.Globalization;
using System.Text;

namespace SprocketExpedition.Core.Persistence;

// One completed attempt, exactly as it will be written to battles.csv. Every
// nullable field is an honest "could not measure" and renders as an empty cell;
// unknown values are never replaced by a plausible guess.
public sealed record BattleLogRow(
    DateTime Utc,
    string RunId,
    int Slot,
    int Schema,
    decimal? Difficulty,
    bool? EraProgression,
    string Era,
    int EraIndex,
    int Round,
    int RoundInEra,
    bool IsBoss,
    bool IsEndless,
    string Risk,
    string Map,
    string? Formation,
    int EnemyCount,
    decimal? EnemyCost,
    decimal? EnemyTarget,
    int FriendlyCount,
    string? PlayerDesign,
    decimal? PlayerUnitCost,
    decimal? FleetCost,
    decimal? AvailableBudget,
    decimal? BaseBudget,
    string Outcome,
    decimal? DurationSeconds,
    int? EnemiesDestroyed,
    int? FriendliesLost,
    bool? PlayerTankSurvived,
    long AttemptGeneration,
    string ActiveItems)
{
    public decimal? ScoreGained { get; init; }
    public bool TestPool { get; init; }
    public string? EnemyDesigns { get; init; }
}

public static class BattleLog
{
    public const string Header =
        "utc,run_id,slot,schema,difficulty,era_progression,era,era_index,round,round_in_era,"
        + "is_boss,is_endless,risk,map,formation,enemy_count,enemy_cost,enemy_target,"
        + "friendly_count,player_design,player_unit_cost,fleet_cost,available_budget,"
        + "base_budget,outcome,duration_s,enemies_destroyed,friendlies_lost,"
        + "player_tank_survived,attempt_generation,active_items,score_gained,test_pool,enemy_designs";

    // Pure row-to-text formatting, split out so quoting and culture can be tested
    // without touching a disk. The returned text has no trailing newline.
    public static string Format(BattleLogRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var fields = new[]
        {
            row.Utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            Csv(row.RunId),
            Number(row.Slot),
            Number(row.Schema),
            Decimal(row.Difficulty),
            Bool(row.EraProgression),
            Csv(row.Era),
            Number(row.EraIndex),
            Number(row.Round),
            Number(row.RoundInEra),
            Bool(row.IsBoss),
            Bool(row.IsEndless),
            Csv(row.Risk),
            Csv(row.Map),
            Csv(row.Formation),
            Number(row.EnemyCount),
            Decimal(row.EnemyCost),
            Decimal(row.EnemyTarget),
            Number(row.FriendlyCount),
            Csv(row.PlayerDesign),
            Decimal(row.PlayerUnitCost),
            Decimal(row.FleetCost),
            Decimal(row.AvailableBudget),
            Decimal(row.BaseBudget),
            Csv(row.Outcome),
            Decimal(row.DurationSeconds),
            Integer(row.EnemiesDestroyed),
            Integer(row.FriendliesLost),
            Bool(row.PlayerTankSurvived),
            Number(row.AttemptGeneration),
            Csv(row.ActiveItems),
            Decimal(row.ScoreGained),
            row.TestPool ? "1" : "0",
            Csv(row.EnemyDesigns)
        };
        return string.Join(",", fields);
    }

    // Never throws: a logging problem must not break combat or the run flow.
    public static void Append(string path, BattleLogRow row, Action<string>? warning = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            UpgradeHeader(path);
            var isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            if (isNew) writer.Write(Header + "\n");
            writer.Write(Format(row) + "\n");
        }
        catch (Exception ex)
        {
            Warn(warning, "Expedition battle log could not be written: " + ex.Message);
        }
    }

    public static void Warn(Action<string>? warning, string message)
    {
        try { warning?.Invoke(message); }
        catch (Exception) { /* Diagnostics must never break the battle flow. */ }
    }

    private static void UpgradeHeader(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return;
        string? header;
        string body;
        int missing;
        using (var reader = new StreamReader(path))
        {
            header = reader.ReadLine();
            if (header == Header) return;
            var scoreHeader = Header[..Header.IndexOf(",test_pool", StringComparison.Ordinal)];
            missing = header == scoreHeader ? 2 : header == scoreHeader[..scoreHeader.LastIndexOf(',')] ? 3 : 0;
            if (missing == 0)
                throw new InvalidDataException("Unrecognized battles.csv header; original file preserved.");
            body = reader.ReadToEnd();
        }
        // Extend the phase-0 format atomically, including rows with quoted newlines.
        var migrated = new StringBuilder(Header).Append('\n');
        var quoted = false;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '"')
            {
                if (quoted && i + 1 < body.Length && body[i + 1] == '"')
                { migrated.Append("\"\""); i++; continue; }
                quoted = !quoted;
            }
            if (!quoted && (c == '\n' || c == '\r'))
            {
                migrated.Append(new string(',', missing)).Append(c);
                if (c == '\r' && i + 1 < body.Length && body[i + 1] == '\n') migrated.Append(body[++i]);
            }
            else migrated.Append(c);
        }
        if (quoted) throw new InvalidDataException("Incomplete quoted CSV row; original file preserved.");
        if (body.Length > 0 && body[^1] is not ('\r' or '\n')) migrated.Append(new string(',', missing)).Append('\n');
        var temp = path + ".upgrade-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, migrated.ToString(), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception) { }
        }
    }

    private static string Number(long value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Integer(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Decimal(decimal? value) =>
        value?.ToString("0.############################", CultureInfo.InvariantCulture)
        ?? string.Empty;

    private static string Bool(bool? value) =>
        value is null ? string.Empty : value.Value ? "true" : "false";

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var mustQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]);
        return mustQuote ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
