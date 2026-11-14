using SprocketExpedition.Game.UI;

namespace SprocketExpedition.Game;

// Per-battle combat statistics, kept only in memory for the running encounter.
// Pure managed code (no Unity, BepInEx or native types) so the managed suites can
// exercise the aggregation, the destroyed-by rule and the reset without a game.
//
// Native observers feed it through CampaignBattleStatsBridge (a Unity-side file):
// a projectile launch identifies the firing tank; a damage-model hit identifies
// the projectile and the struck tank; the native kill event supplies an explicit
// killer when the game publishes one. Every value is measured; unknown stays
// unknown rather than defaulting to zero.
internal sealed class CampaignBattleStats
{
    // One shared tracker for the live encounter; tests create their own instance.
    internal static CampaignBattleStats Current { get; } = new();

    // The game raises a kill event before or after the crew poll observes the
    // death, so the window is applied to the *hit* that preceded the destruction.
    internal const double DestroyedByWindowSeconds = 15d;

    private sealed class TankState
    {
        internal IntPtr Key;
        internal string Name = string.Empty;
        internal bool Friendly;
        internal bool Player;
        internal bool Destroyed;
        internal double DestroyedAt;
        internal int Fired;
        internal int Hit;
        internal int Taken;
        internal int Kills;
        internal decimal? LongestHitM;
    }

    private sealed record HitRecord(IntPtr Projectile, IntPtr Shooter, IntPtr Target,
        bool ShooterFriendly, double Time, decimal DistanceM);

    private readonly Dictionary<IntPtr, TankState> tanks = new();
    private readonly List<HitRecord> hits = new();
    private readonly HashSet<(IntPtr Projectile, IntPtr Target)> hitKeys = new();
    // Accuracy counts a fired round at most once, even if one shell touches more
    // than one enemy structure or hull. A hit only scores when its launch was
    // observed, so a late-registered hull can never produce more hits than shots.
    private readonly HashSet<IntPtr> scoredRounds = new();
    private readonly HashSet<(IntPtr Shooter, IntPtr Projectile)> firedRounds = new();
    private readonly Dictionary<IntPtr, IntPtr> explicitKillers = new();
    private readonly HashSet<IntPtr> countedKills = new();
    private string? attempt;
    private double startedAt;
    private double finishedAt;
    private bool active;
    private BattleStatsReport? last;

    internal bool Active => active;
    internal bool HasBattle => attempt is not null;
    internal string? Attempt => attempt;

    // Stable battle key without a Core dependency (the proof build is Core-free).
    internal static string Key(string runId, string encounterId, string planHash, long generation) =>
        runId + "\u001f" + encounterId + "\u001f" + planHash + "\u001f" + generation;

    // A new encounter (including every Retry, which carries a new generation).
    internal void Begin(string identity, double now = 0d)
    {
        attempt = identity;
        startedAt = now;
        finishedAt = now;
        active = true;
        last = null;
        tanks.Clear();
        hits.Clear();
        hitKeys.Clear();
        scoredRounds.Clear();
        firedRounds.Clear();
        explicitKillers.Clear();
        countedKills.Clear();
    }

    // A native restart of the same encounter: counters start over, identity stays.
    internal void Restart(double now = 0d)
    {
        if (attempt is null) return;
        startedAt = now;
        finishedAt = now;
        active = true;
        last = null;
        tanks.Clear();
        hits.Clear();
        hitKeys.Clear();
        scoredRounds.Clear();
        firedRounds.Clear();
        explicitKillers.Clear();
        countedKills.Clear();
    }

    internal void Register(IntPtr key, string? name, bool friendly)
    {
        if (!active || key == IntPtr.Zero) return;
        if (!tanks.TryGetValue(key, out var tank))
        {
            tank = new TankState { Key = key, Friendly = friendly };
            tanks[key] = tank;
        }
        if (!string.IsNullOrWhiteSpace(name)) tank.Name = name!;
    }

    internal void NotePlayer(IntPtr key)
    {
        if (!active || !tanks.TryGetValue(key, out var player) || !player.Friendly) return;
        foreach (var tank in tanks.Values) tank.Player = false;
        player.Player = true;
    }

    internal void Shot(IntPtr shooter, IntPtr projectile)
    {
        if (!active || !tanks.TryGetValue(shooter, out var tank)) return;
        tank.Fired++;
        if (projectile != IntPtr.Zero) firedRounds.Add((shooter, projectile));
    }

    // One damage-model call per projectile/target pair; internal structures and
    // repeated fragments of the same shell are folded into one hit.
    internal void Hit(IntPtr projectile, IntPtr shooter, IntPtr target, decimal distanceM, double now)
    {
        if (!active || !tanks.TryGetValue(shooter, out var firing) || !tanks.TryGetValue(target, out var struck)) return;
        if (!hitKeys.Add((projectile, target))) return;
        var distance = distanceM < 0m ? 0m : decimal.Round(distanceM, 1);
        hits.Add(new HitRecord(projectile, shooter, target, firing.Friendly, now, distance));
        if (struck.Friendly) struck.Taken++;
        if (!struck.Friendly && firing.Friendly && scoredRounds.Add(projectile)
            && firedRounds.Contains((shooter, projectile)))
        {
            firing.Hit++;
            if (firing.LongestHitM is null || distance > firing.LongestHitM) firing.LongestHitM = distance;
        }
    }

    // The game's own kill attribution (KillEventArgs.Killer/Victim), when present.
    internal void ExplicitKill(IntPtr killer, IntPtr victim, double now)
    {
        if (!active || victim == IntPtr.Zero || victim == killer) return;
        if (!tanks.TryGetValue(victim, out var victimTank)) return;
        if (!victimTank.Destroyed)
        {
            victimTank.Destroyed = true;
            victimTank.DestroyedAt = now;
        }
        if (killer != IntPtr.Zero && tanks.ContainsKey(killer)) explicitKillers[victim] = killer;
    }

    // A destroyed hull observed by the roster poll. Only the first observation counts.
    internal void Destroyed(IntPtr victim, double now)
    {
        if (!active || !tanks.TryGetValue(victim, out var tank) || tank.Destroyed) return;
        tank.Destroyed = true;
        tank.DestroyedAt = now;
    }

    // Builds the immutable report and keeps it for the result screen. Unknown
    // values remain null; the formatter renders them as an em dash.
    internal BattleStatsReport? Complete(string outcome, double now)
    {
        if (attempt is null) return null;
        finishedAt = now;
        foreach (var enemy in tanks.Values.Where(t => !t.Friendly && t.Destroyed))
        {
            if (!countedKills.Add(enemy.Key)) continue;
            var resolved = ResolveAttacker(enemy, enemy.DestroyedAt);
            if (resolved is { } killer && tanks.TryGetValue(killer.Key, out var attacker) && attacker.Friendly)
                attacker.Kills++;
        }

        var friendly = tanks.Values.Where(t => t.Friendly)
            .OrderByDescending(t => t.Player).ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(tank =>
            {
                var destroyedBy = tank.Destroyed ? ResolveAttacker(tank, tank.DestroyedAt) : null;
                var killerName = destroyedBy is { } by && tanks.TryGetValue(by.Key, out var killer)
                    ? killer.Name : null;
                return new BattleTankStats(tank.Name, tank.Player, tank.Destroyed,
                    tank.Fired, tank.Hit, tank.Taken,
                    tank.Destroyed ? Math.Max(0, tank.Taken - 1) : tank.Taken,
                    tank.Kills, tank.LongestHitM, killerName, destroyedBy?.DistanceM);
            }).ToList();

        var report = new BattleStatsReport(attempt!, outcome, Math.Max(0d, finishedAt - startedAt), friendly);
        last = report;
        active = false;
        return report;
    }

    internal BattleStatsReport? For(string attemptKey) =>
        last is { } report && report.AttemptKey == attemptKey ? report : null;

    internal BattleStatsReport? Last => last;

    private readonly record struct Attacker(IntPtr Key, decimal? DistanceM);

    // Explicit attacker first. Otherwise the shooter of the last hostile hit on
    // this hull no more than 15 s before destruction. Never a distance/sight guess.
    private Attacker? ResolveAttacker(TankState victim, double at)
    {
        IntPtr? explicitKiller = victim.Destroyed && explicitKillers.TryGetValue(victim.Key, out var killer)
            ? killer : null;
        var resolved = ResolveDestroyedBy(victim.Key, explicitKiller,
            hits.Select(hit => (hit.Shooter, hit.Target, hit.Time, hit.DistanceM)), at);
        return resolved is { } found ? new Attacker(found.Attacker, found.DistanceM) : null;
    }

    // The destroyed-by rule, directly testable without a tracker.
    internal readonly record struct DestroyedByEvidence(IntPtr Attacker, decimal? DistanceM);

    internal static DestroyedByEvidence? ResolveDestroyedBy(IntPtr victim, IntPtr? explicitKiller,
        IEnumerable<(IntPtr Shooter, IntPtr Target, double Time, decimal DistanceM)> recordedHits,
        double destroyedAt)
    {
        var latest = (Shooter: IntPtr.Zero, Time: double.NegativeInfinity, Distance: 0m, Found: false);
        foreach (var hit in recordedHits)
        {
            if (hit.Target != victim || hit.Time > destroyedAt
                || destroyedAt - hit.Time > DestroyedByWindowSeconds) continue;
            if (hit.Time > latest.Time)
                latest = (hit.Shooter, hit.Time, hit.DistanceM, true);
        }
        if (explicitKiller is { } killer)
        {
            decimal? measured = null;
            foreach (var hit in recordedHits)
                if (hit.Target == victim && hit.Shooter == killer && hit.Time <= destroyedAt
                    && destroyedAt - hit.Time <= DestroyedByWindowSeconds)
                    measured = hit.DistanceM;
            return new DestroyedByEvidence(killer, measured);
        }
        return latest.Found ? new DestroyedByEvidence(latest.Shooter, latest.Distance) : null;
    }
}
