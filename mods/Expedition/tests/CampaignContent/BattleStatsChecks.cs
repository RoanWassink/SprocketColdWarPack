using SprocketExpedition.Game;
using SprocketExpedition.Game.UI;

internal static class BattleStatsChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    internal static void Run()
    {
        Aggregation();
        DestroyedByRule();
        Reset();
        Formatting();
        Console.WriteLine("PASS battle stats: accuracy, 0-shot dash, hits survived, kills, longest hit, destroyed-by windows, reset and log format");
    }

    private static void Aggregation()
    {
        var stats = new CampaignBattleStats();
        var player = new IntPtr(11);
        var ally = new IntPtr(12);
        var quiet = new IntPtr(13);
        var enemy = new IntPtr(21);
        var second = new IntPtr(22);
        stats.Begin("battle-a", 0);
        stats.Register(player, "King Tiger", true);
        stats.Register(ally, "Panther", true);
        stats.Register(quiet, "Scout", true);
        stats.Register(enemy, "T-34", false);
        stats.Register(second, "KV-1", false);
        stats.NotePlayer(player);

        stats.Shot(player, new IntPtr(100));
        stats.Shot(player, new IntPtr(101));
        stats.Shot(player, new IntPtr(199));
        stats.Shot(ally, new IntPtr(102));
        stats.Shot(ally, new IntPtr(198));
        stats.Shot(enemy, new IntPtr(103));
        stats.Shot(enemy, new IntPtr(104));
        stats.Shot(enemy, new IntPtr(105));
        stats.Shot(enemy, new IntPtr(197));

        stats.Hit(new IntPtr(100), player, enemy, 500m, 10);
        stats.Hit(new IntPtr(101), player, enemy, 1200m, 20);
        stats.Hit(new IntPtr(101), player, enemy, 1200m, 20);   // internal structure: folded
        stats.Hit(new IntPtr(102), ally, enemy, 400m, 22);
        stats.Hit(new IntPtr(103), enemy, player, 600m, 30);
        stats.Hit(new IntPtr(104), enemy, ally, 300m, 35);
        stats.Hit(new IntPtr(105), enemy, player, 650m, 50);
        stats.Hit(new IntPtr(999), player, enemy, 300m, 55); // launch not observed: no extra hit
        stats.Destroyed(enemy, 24);
        stats.ExplicitKill(player, enemy, 24);
        stats.Destroyed(ally, 40);
        stats.Destroyed(player, 60);
        stats.Destroyed(player, 61); // idempotent

        var report = stats.Complete("FriendlyForceDestroyed", 192)!;
        var king = report.Tanks.Single(t => t.Name == "King Tiger");
        var panther = report.Tanks.Single(t => t.Name == "Panther");
        var scout = report.Tanks.Single(t => t.Name == "Scout");
        Check(report.AttemptKey == "battle-a" && report.Outcome == "FriendlyForceDestroyed", "report identity/outcome");
        Check(king.IsPlayer && king.Fired == 3 && king.Hit == 2, "player fired/hit");
        Check(BattleStatsText.Accuracy(king.Fired, king.Hit) == "67%", "player accuracy 2/3 = 67%");
        Check(king.Taken == 2 && king.Survived == 1 && king.Kills == 1, "player taken/survived/kills");
        Check(king.Destroyed && king.DestroyedByName == "T-34" && king.DestroyedByDistanceM == 650m,
            "player destroyed by last enemy hit");
        Check(king.LongestHitM == 1200m, "longest hit measured");
        Check(panther.Fired == 2 && panther.Hit == 1 && panther.Taken == 1 && panther.Survived == 0
            && panther.Kills == 0, "ally counters");
        Check(panther.DestroyedByName == "T-34" && panther.DestroyedByDistanceM == 300m, "ally destroyed-by");
        Check(scout.Fired == 0 && BattleStatsText.Accuracy(0, 0) == "—" && scout.Survived == 0,
            "zero shots render as a dash, not 0%");
        Check(!scout.Destroyed && scout.DestroyedByName is null, "surviving tank has no killer");
        Check(report.Tanks.Count == 3 && report.Tanks[0].IsPlayer, "only own tanks, player first");
        Check(report.Tanks.Sum(t => t.Fired) == 5 && report.Tanks.Sum(t => t.Hit) == 3,
            "team totals add up to the measured shots");
        Check(stats.For("battle-a") == report && stats.For("other") is null, "report lookup is keyed by attempt");
        Check(!stats.Active, "a finished battle stops recording");
    }

    private static void DestroyedByRule()
    {
        var victim = new IntPtr(1);
        var enemy = new IntPtr(2);
        var hits = new[]
        {
            (Shooter: enemy, Target: victim, Time: 10d, DistanceM: 450m)
        };

        var explicitKiller = CampaignBattleStats.ResolveDestroyedBy(victim, enemy, hits, 12d);
        Check(explicitKiller is { } explicitHit && explicitHit.Attacker == enemy && explicitHit.DistanceM == 450m,
            "explicit attacker wins and keeps its measured distance");
        var explicitNoDistance = CampaignBattleStats.ResolveDestroyedBy(victim, enemy,
            Array.Empty<(IntPtr, IntPtr, double, decimal)>(), 12d);
        Check(explicitNoDistance is { } noDistance && noDistance.DistanceM is null,
            "explicit attacker without a hit has no invented distance");

        var inside = CampaignBattleStats.ResolveDestroyedBy(victim, null, hits, 20d);
        Check(inside is { } within && within.Attacker == enemy, "last hit inside 15 s attributes");
        var boundary = CampaignBattleStats.ResolveDestroyedBy(victim, null, hits, 25d);
        Check(boundary is not null, "exactly 15 s still counts");
        var outside = CampaignBattleStats.ResolveDestroyedBy(victim, null, hits, 25.1d);
        Check(outside is null, "a hit older than 15 s is unknown");
        var none = CampaignBattleStats.ResolveDestroyedBy(victim, null,
            Array.Empty<(IntPtr, IntPtr, double, decimal)>(), 20d);
        Check(none is null, "no hit is unknown");
        var wrongVictim = CampaignBattleStats.ResolveDestroyedBy(new IntPtr(9), null, hits, 20d);
        Check(wrongVictim is null, "a hit on another tank cannot attribute this death");
    }

    private static void Reset()
    {
        var stats = new CampaignBattleStats();
        var tank = new IntPtr(5);
        stats.Begin("first", 100);
        stats.Register(tank, "Tank", true);
        stats.Shot(tank, new IntPtr(1));
        stats.Complete("Victory", 110);
        Check(stats.For("first") is not null, "finished report available");
        stats.Begin("second", 200);
        Check(stats.For("first") is null && stats.Last is null, "new battle clears the previous report");
        stats.Register(tank, "Tank", true);
        stats.Shot(tank, new IntPtr(2)); stats.Shot(tank, new IntPtr(3));
        stats.Restart(205);
        stats.Register(tank, "Tank", true);
        var restarted = stats.Complete("Victory", 210)!;
        Check(restarted.AttemptKey == "second" && restarted.Tanks.Single().Fired == 0,
            "Retry/Restart clears counters and keeps the encounter identity");
        Check(restarted.DurationSeconds == 5d, "restart resets the battle clock");
    }

    private static void Formatting()
    {
        var tanks = Enumerable.Range(0, 8).Select(i => new BattleTankStats(
            "Tank " + i, i == 0, i == 0 || i >= 3, 10, 4, 2, 2, 1, null,
            i == 0 ? "T-34" : null, i == 0 ? 412m : null)).ToList();
        var losses = BattleStatsText.LossesLines(tanks);
        Check(losses.Count == 7 && losses[^1] == "+2 more", "losses show six rows and +n more");
        Check(losses[0] == "Tank 0 — destroyed by T-34 at 412 m", "destroyed-by text");
        Check(losses[3] == "Tank 3 — destroyed (cause unknown)", "unknown cause text");
        var unknownDistance = BattleStatsText.Status(tanks[3] with { DestroyedByName = "Panther", DestroyedByDistanceM = null });
        Check(unknownDistance == "Tank 3 — destroyed by Panther at — m", "unknown distance shows a dash");
        Check(losses[6] == "+2 more", "overflow marker last");

        var report = new BattleStatsReport("key", "FriendlyForceDestroyed", 192d, tanks.Take(2).ToList());
        var blocks = BattleStatsText.BattleLines(report);
        Check(blocks[0].StartsWith("You — Tank 0: fired 10 · hit 4 · acc 40%", StringComparison.Ordinal),
            "player battle line");
        Check(blocks[1].StartsWith("Team: fired 20 · hit 8 · acc 40%", StringComparison.Ordinal), "team total line");
        Check(blocks[2] == "Battle 3:12", "duration without a longest hit");

        var log = BattleStatsText.LogLine("FriendlyForceDestroyed", tanks[0]);
        Check(log == "Expedition battle stats: FriendlyForceDestroyed; player 'Tank 0' fired=10 hit=4 acc=40%"
            + " taken=2 survived=2 kills=1 destroyedBy='T-34' at 412 m", "exact log line: " + log);
        var alive = tanks[1] with { Destroyed = false, DestroyedByName = null, DestroyedByDistanceM = null };
        Check(BattleStatsText.LogLine("Victory", alive).EndsWith("destroyedBy=alive", StringComparison.Ordinal),
            "alive log suffix");
        var unknown = tanks[0] with { DestroyedByName = null, DestroyedByDistanceM = null };
        Check(BattleStatsText.LogLine("Victory", unknown).EndsWith("destroyedBy=unknown", StringComparison.Ordinal),
            "unknown log suffix");
        Check(BattleStatsText.LogLine("Victory", tanks[0] with { IsPlayer = false }).Contains("; ally '", StringComparison.Ordinal),
            "ally log role");
    }
}

