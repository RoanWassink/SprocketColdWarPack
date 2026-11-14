using System.Globalization;
using SprocketExpedition.Core.Persistence;

namespace SprocketExpedition.Core.Persistence.Tests;

internal static class BattleLogTests
{
    public static void Register()
    {
        TestRunner.Add("battle log formats invariant CSV with correct quoting",
            FormatsInvariantCsv);
        TestRunner.Add("battle log writes one header and never throws",
            AppendsSafely);
        TestRunner.Add("battle log atomically extends the old header including multiline quoted rows",
            ExtendsOldHeader);
    }

    private static BattleLogRow Row(string design = "design, \"quoted\"") => new(
        new DateTime(2026, 11, 1, 12, 34, 56, DateTimeKind.Utc),
        "run-1", 3, 6, 1.5m, true, "era-fixture", 2, 9, 1, false, false,
        "Dangerous", "map,one", "CombinedArms", 2, 12.5m, 10m, 3, design,
        20.25m, 40.5m, 60m, 50m, "Victory", 61.125m, 2, 1, true, 4,
        "EliteWorkshop|MassProduction");

    private static void FormatsInvariantCsv()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
            var text = BattleLog.Format(Row());
            AssertEx.True(text.EndsWith(",0,", StringComparison.Ordinal), "normal run flag is numeric zero");
            AssertEx.True(BattleLog.Format(Row() with { TestPool = true, EnemyDesigns = "Tank, A|Tank B" }).EndsWith(",1,\"Tank, A|Tank B\"", StringComparison.Ordinal), "test flag and enemy names quoted");
            AssertEx.True(text.Contains("12.5", StringComparison.Ordinal),
                "decimal point is invariant: " + text);
            AssertEx.False(text.Contains("12,5", StringComparison.Ordinal),
                "no comma decimal separator");
            AssertEx.True(text.Contains("\"design, \"\"quoted\"\"\"", StringComparison.Ordinal),
                "commas and quotes are quoted/doubled: " + text);
            AssertEx.True(text.Contains("\"map,one\"", StringComparison.Ordinal),
                "map commas are quoted");
            AssertEx.Equal(34, BattleLog.Header.Split(',').Length,
                "the documented header appends score_gained as column 32");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static void AppendsSafely()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Root, "stats", "battles.csv");
        BattleLog.Append(path, Row());
        BattleLog.Append(path, Row("plain"));
        var lines = File.ReadAllLines(path);
        AssertEx.Equal(3, lines.Length, "header plus two rows");
        AssertEx.Equal(BattleLog.Header, lines[0], "header is written only once");
        var warnings = new List<string>();
        var blocker = Path.Combine(temp.Root, "blocker");
        File.WriteAllText(blocker, "not a directory");
        BattleLog.Append(Path.Combine(blocker, "battles.csv"), Row(), warnings.Add);
        AssertEx.True(warnings.Count == 1, "write failure is reported, not thrown");
        BattleLog.Append(Path.Combine(blocker, "battles.csv"), Row(), _ => throw new IOException("logger failed too"));
    }

    private static void ExtendsOldHeader()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Root,"battles.csv");
        var oldHeader=BattleLog.Header[..BattleLog.Header.IndexOf(",score_gained", StringComparison.Ordinal)];
        var oldRow=BattleLog.Format(Row("line 1, \"quoted\"\nline 2"))[..^4];
        File.WriteAllText(path,oldHeader+"\n"+oldRow+"\n");
        var scored=Row("scored") with {ScoreGained=123.456m};
        BattleLog.Append(path,scored);
        var text=File.ReadAllText(path);
        AssertEx.Equal(BattleLog.Header+"\n"+oldRow+",,,\n"+BattleLog.Format(scored)+"\n",text,
            "phase-0 row gains three fields, quoted newline survives");
        var scoreHeader = BattleLog.Header[..BattleLog.Header.IndexOf(",test_pool", StringComparison.Ordinal)];
        var scoreRow = BattleLog.Format(scored)[..^3];
        File.WriteAllText(path, scoreHeader + "\r\n" + scoreRow);
        BattleLog.Append(path, scored);
        AssertEx.Equal(BattleLog.Header + "\n" + scoreRow + ",,\n" + BattleLog.Format(scored) + "\n", File.ReadAllText(path),
            "0.3.0 score header gains two fields, final row without newline survives");
        File.WriteAllText(path,"unexpected header\nkeep me\n");
        BattleLog.Append(path,scored,_=>throw new IOException("warning also fails"));
        AssertEx.Equal("unexpected header\nkeep me\n",File.ReadAllText(path),"unknown header is preserved");
    }
}
