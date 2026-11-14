using System.Numerics;

namespace SprocketExpedition.Game;

// Pure pursuit/search policy shared by the native adapter and its managed checks.
// Positions are horizontal map coordinates. The native adapter supplies the
// actual ground height, pathfinding and visibility; this class only decides
// where each team member should go and when an order needs refreshing.
internal static class CampaignHuntPlanner
{
    internal const int SearchStepLimit = 999;
    internal const float SearchStepSeconds = 30f;
    internal const float AttackInterval = 45f;
    internal const float MoveInterval = 10f;
    internal const float TargetShift = 20f;
    internal const float StuckDistance = 3f;
    // Slow early-era hulls need time to turn onto a native path before a
    // recovery order changes their steering destination again.
    internal const float StuckSeconds = 18f;

    // Capture each real hull only once, then freeze both starting centers.
    // Later movement, sightings and casualties cannot alter these spawn facts.
    internal sealed class SpawnMemory
    {
        private readonly Dictionary<long, (bool Friendly, Vector2 Position)> positions = new();
        internal bool Frozen { get; private set; }
        internal Vector2 Friendly { get; private set; }
        internal Vector2 Enemy { get; private set; }
        internal int FriendlyCount => positions.Values.Count(point => point.Friendly);
        internal int EnemyCount => positions.Count - FriendlyCount;
        internal void Observe(long identity, bool friendly, Vector2 position)
        {
            if (!Frozen && Finite(position)) positions.TryAdd(identity, (friendly, position));
        }
        internal bool Freeze(int expectedFriendlies, int expectedEnemies, bool allowPartial)
        {
            if (Frozen) return true;
            var friendlyCount = FriendlyCount;
            var enemyCount = EnemyCount;
            if (friendlyCount == 0 || enemyCount == 0 || (!allowPartial
                && (friendlyCount < expectedFriendlies || enemyCount < expectedEnemies))) return false;
            Friendly = positions.Values.Where(point => point.Friendly)
                .Aggregate(Vector2.Zero, (sum, point) => sum + point.Position) / friendlyCount;
            Enemy = positions.Values.Where(point => !point.Friendly)
                .Aggregate(Vector2.Zero, (sum, point) => sum + point.Position) / enemyCount;
            Frozen = true;
            return true;
        }
    }

    internal sealed class GoalProgress
    {
        private Vector2 anchor;
        private bool hasGoal;
        internal float BestDistance { get; private set; }
        internal float LastProgressAt { get; private set; }
        internal bool Stalled(Vector2 goal, Vector2 position, float now)
        {
            var distance = Vector2.Distance(position, goal);
            if (!hasGoal || Vector2.DistanceSquared(anchor, goal) >= TargetShift * TargetShift)
            {
                anchor = goal;
                hasGoal = true;
                BestDistance = distance;
                LastProgressAt = now;
                return false;
            }
            if (distance <= BestDistance - StuckDistance)
            {
                BestDistance = distance;
                LastProgressAt = now;
            }
            return now - LastProgressAt >= StuckSeconds;
        }
        internal void GrantTurnTime(float now) => LastProgressAt = now;
        internal void Clear() => hasGoal = false;
    }

    // Sweep a small corridor through the contact/spawn, oriented from our own
    // initial spawn. No random wide rings that send a newly arrived hull away.
    internal static Vector2 SearchWaypoint(Vector2 contact, Vector2 origin, int slot, int step)
    {
        if (!Finite(contact) || !Finite(origin)) return Vector2.Zero;
        var forward = Direction(contact - origin);
        var right = new Vector2(forward.Y, -forward.X);
        var column = Column(slot);
        var depth = (((step % 4) + 4) % 4) switch { 0 => 0f, 1 => 18f, 2 => 36f, _ => 18f };
        return contact + right * (column * 24f) + forward * (depth - slot / 5 * 24f);
    }

    internal static int NextSearchStep(int step) =>
        step < 0 || step >= SearchStepLimit ? 0 : step + 1;

    // A progressing native path is left intact; elapsed time alone must not
    // cancel it. Missing native tasks get a bounded retry, not a per-scan reset.
    internal static bool ShouldRefresh(float now, float lastOrderAt, bool orderActive,
        float targetShiftSquared, bool stuck, float interval, bool recoveryBlocked = false) =>
        (!orderActive && now - lastOrderAt >= interval)
        || targetShiftSquared >= TargetShift * TargetShift
        || (stuck && !recoveryBlocked);

    internal static bool AttackDue(float now, float lastOrderAt, bool active,
        bool targetChanged, bool engageChanged) => now - lastOrderAt >= 2.5f
        && (targetChanged || engageChanged || now - lastOrderAt >= (active ? AttackInterval : 8f));

    // Separate enter/exit distances stop a moving target at ~300 m from making
    // the AI alternate between braking and accelerating every visibility scan.
    internal static bool StopToFire(float distance, bool wasStopped) =>
        distance <= (wasStopped ? 340f : 280f);

    internal readonly record struct VisibleTarget(int Key, float Distance, int Assigned);

    internal static int ChooseVisibleTarget(IReadOnlyList<VisibleTarget> candidates, int current)
    {
        var best = -1;
        var score = float.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            if (!float.IsFinite(candidate.Distance) || candidate.Distance < 0) continue;
            var value = candidate.Distance + Math.Max(0, candidate.Assigned) * 45f
                - (candidate.Key == current ? 60f : 0f);
            if (value >= score) continue;
            score = value;
            best = candidate.Key;
        }
        return best;
    }

    // Fixed slot numbers survive casualties. Rank-based columns replace the
    // old clamped spawn offset, which could send several hulls to one point.
    internal static Vector2 ApproachWaypoint(Vector2 target, Vector2 origin, int slot, float lateralSpacing = 24f)
    {
        var forward = Direction(target - origin);
        var right = new Vector2(forward.Y, -forward.X);
        var column = Column(slot);
        var row = slot / 5;
        return target + right * (column * lateralSpacing) - forward * (18f + row * 24f);
    }

    internal static Vector2 RecoveryWaypoint(Vector2 position, Vector2 destination, int slot, int attempt)
    {
        var forward = Direction(destination - position);
        var right = new Vector2(forward.Y, -forward.X);
        var side = (slot + attempt) % 2 == 0 ? -1f : 1f;
        var distance = Vector2.Distance(position, destination);
        return position + right * side * (16f + 4f * (attempt % 2))
            + forward * Math.Min(45f, Math.Max(18f, distance * .5f));
    }

    private static int Column(int slot) => (slot % 5) switch { 0 => 0, 1 => -1, 2 => 1, 3 => -2, _ => 2 };

    private static Vector2 Direction(Vector2 value) => Finite(value) && value.LengthSquared() > .01f
        ? Vector2.Normalize(value) : Vector2.UnitY;

    internal static bool IsStuck(float now, float lastProgressAt, float movedDistance) =>
        now - lastProgressAt >= StuckSeconds && movedDistance < StuckDistance;

    internal static float HorizontalDistance(Vector2 a, Vector2 b) => (a - b).Length();

    internal static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

}
