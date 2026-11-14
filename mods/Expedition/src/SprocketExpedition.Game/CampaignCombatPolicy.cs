using System.Numerics;

namespace SprocketExpedition.Game;

// Pure intent only. Detection, braking, pathfinding and firing remain native.
internal static class CampaignCombatPolicy
{
    internal enum Role { Assault, Line, FireSupport, Ambusher }
    internal readonly record struct Profile(Role Role, float StopRange, float ResumeRange, float ReactionSeconds);

    // 0.3.5 fire-progress policy. A halted hull that neither fires nor improves
    // its lay has no effective clear shot, so it repositions for a better line.
    internal const float FireStallSeconds = 8f;
    internal const float FireProgressWindowSeconds = 2f;
    internal const float FireErrorProgressDegrees = .5f;
    internal const float FireHullProgressDegrees = 2f;
    internal const float RepositionSeconds = 12f;
    internal const float RepositionMeters = 60f;
    internal const float RepositionArriveMeters = 10f;
    internal const float FlankLateralMin = 25f;
    internal const float FlankLateralMax = 40f;
    internal const float FlankForwardMin = 15f;
    internal const float FlankForwardMax = 30f;

    // Side-to-side flank step: never the same lateral point twice in a row.
    internal static int NextFlankSide(int side) => side >= 0 ? -1 : 1;

    // Deterministic per-slot spread, always inside the promised 25-40 m and
    // 15-30 m bands. Slots are stable, so the point is reproducible per hull.
    internal static float FlankLateral(int slot) =>
        FlankLateralMin + Math.Abs(slot % 4) * 4f;

    internal static float FlankForward(int slot) =>
        FlankForwardMin + Math.Abs(slot % 4) * 5f;

    internal static Vector2 FlankWaypoint(Vector2 position, Vector2 target, int side,
        float lateral, float forward)
    {
        if (!Finite(position) || !Finite(target)) return position;
        var delta = target - position;
        var length = delta.Length();
        if (!float.IsFinite(length) || length < 1f) return position;
        var ahead = delta / length;
        var right = new Vector2(ahead.Y, -ahead.X);
        var sign = side >= 0 ? 1f : -1f;
        lateral = float.IsFinite(lateral) ? Math.Clamp(lateral, FlankLateralMin, FlankLateralMax) : 32f;
        forward = float.IsFinite(forward) ? Math.Clamp(forward, FlankForwardMin, FlankForwardMax) : 22f;
        return position + right * (sign * lateral) + ahead * forward;
    }

    // Ambushers hold by decision (2026-11-01), and yielded/player hulls never
    // receive automatic movement. Everything else may reposition when stalled.
    internal static bool Repositions(bool ambusher, bool yielded, bool playerControlled, bool mobile) =>
        !ambusher && !yielded && !playerControlled && mobile;

    // Per stopped-to-fire episode. Pure timing/observation only; the adapter
    // supplies native shots, reload state, gun error and hull angle.
    internal sealed class FireTracker
    {
        private readonly List<Sample> samples = new();
        private float clock;
        private float lastNow;
        private float lastProgressAt;
        private int shots;

        private readonly record struct Sample(float At, float Error, float Hull);

        internal bool Active { get; private set; }
        internal int Shots => shots;
        internal float StalledFor => Active ? Math.Max(0f, clock - lastProgressAt) : 0f;
        internal bool Stalled => Active && StalledFor >= FireStallSeconds;

        internal void Reset(float now)
        {
            Active = true;
            clock = 0f;
            lastNow = float.IsFinite(now) ? now : 0f;
            lastProgressAt = 0f;
            shots = 0;
            samples.Clear();
        }

        internal void Release()
        {
            Active = false;
            samples.Clear();
        }

        // newShots is the number of native fireBufferHead changes since the
        // previous observation, summed over all guns. Returns true when this
        // tick made progress (shot, aim improvement or hull turning).
        internal bool Observe(float now, int newShots, bool loaded, float errorDegrees,
            float hullDegrees, bool turning)
        {
            if (!Active || !float.IsFinite(now)) return false;
            var elapsed = Math.Max(0f, now - lastNow);
            lastNow = now;
            // Reloading pauses the clock: a long reload is not a stall.
            if (loaded) clock += elapsed;
            var progressed = false;
            if (newShots > 0)
            {
                shots += newShots;
                progressed = true;
            }
            if (loaded)
            {
                var error = float.IsFinite(errorDegrees) ? Math.Abs(errorDegrees) : float.NaN;
                var hull = turning && float.IsFinite(hullDegrees) ? hullDegrees : float.NaN;
                for (var i = samples.Count - 1; i >= 0; i--)
                {
                    var age = clock - samples[i].At;
                    if (age > FireProgressWindowSeconds) { samples.RemoveAt(i); continue; }
                    if (age < 0f) continue;
                    if (float.IsFinite(error) && float.IsFinite(samples[i].Error)
                        && samples[i].Error - error >= FireErrorProgressDegrees) progressed = true;
                    if (float.IsFinite(hull) && float.IsFinite(samples[i].Hull)
                        && samples[i].Hull - hull >= FireHullProgressDegrees) progressed = true;
                }
                if (progressed) lastProgressAt = clock;
                if (float.IsFinite(error) || float.IsFinite(hull))
                {
                    samples.Add(new Sample(clock,
                        float.IsFinite(error) ? error : float.NaN,
                        float.IsFinite(hull) ? hull : float.NaN));
                    if (samples.Count > 32) samples.RemoveRange(0, samples.Count - 32);
                }
            }
            else
            {
                // Reloading also pauses the 2 s progress window, so a value
                // recorded before the reload cannot count as fresh progress.
                samples.Clear();
                if (progressed) lastProgressAt = clock;
            }
            return progressed;
        }
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    internal static Profile Create(string encounter, int slot, bool friendly, decimal? price,
        IReadOnlyList<decimal> eraPrices, bool boss = false, int eraIndex = 0, bool ambusher = false)
    {
        var rank = .5f; // Unmeasured/ambiguous content keeps neutral reaction skill.
        var prices = eraPrices.Where(p => p > 0).ToArray();
        if (price > 0 && prices.Length > 1)
        {
            var lower = prices.Count(p => p < price.Value);
            var equal = prices.Count(p => p == price.Value);
            rank = Math.Clamp((lower + Math.Max(0, equal - 1) * .5f) / (prices.Length - 1), 0, 1);
        }
        if (boss) rank = 1f;
        var hash = Hash(encounter, slot, friendly);
        var roll = hash % 100;
        var role = rank < .34f ? (roll < 60 ? Role.Assault : roll < 85 ? Role.Line : Role.FireSupport)
            : rank > .66f ? (roll < 60 ? Role.FireSupport : roll < 85 ? Role.Line : Role.Assault)
            : roll < 33 ? Role.Assault : roll < 75 ? Role.Line : Role.FireSupport;
        // Small player fleets need an advancing unit before adding stand-off
        // support. Keep enemy cost-weighted roles and per-unit skill variation.
        if (friendly) role = (slot % 3) switch { 0 => Role.Assault, 1 => Role.Line, _ => Role.FireSupport };
        var variation = ((hash >> 8) % 1001) / 1000f;
        // Combat ranges saturate at index 5 (the authored Coldwar values); later native eras reuse them.
        var era = Math.Clamp(eraIndex, 0, 5);
        var range = role switch { Role.Assault => 100f + era * 70f + variation * (60f + era * 18f),
            Role.Line => 230f + era * 94f + variation * (70f + era * 26f),
            _ => 600f + era * 80f + variation * (100f + era * 20f) };
        // 0.3.3 (user: "Zo snel mogelijk"): no extra reaction wait. Native turret
        // laying already delays the first shot, so every profile reacts at once.
        const float reaction = 0f;
        // Casemate enemies keep the range their measured role would have had,
        // and the shared zero reaction. Friendly hulls never receive this role.
        if (ambusher && !friendly)
            return new Profile(Role.Ambusher, range, range + 60f + era * 18f, reaction);
        return new Profile(role, range, range + 60f + era * 18f, reaction);
    }

    internal static bool StopToFire(Profile profile, float distance, bool stopped) =>
        float.IsFinite(distance) && distance >= 0 && distance <= (stopped ? profile.ResumeRange : profile.StopRange);

    // 0.3.3 seek and destroy (user decision): a clear shot on a visible target
    // always stops to fire; without one the hull keeps moving for a better line.
    internal static bool HoldForShot(Profile profile, float distance, bool stopped, bool clearShot) =>
        clearShot && float.IsFinite(distance) && distance >= 0;

    internal static bool FaceToFire(float angle, bool alreadyTurning) =>
        float.IsFinite(angle) && Math.Abs(angle) > (alreadyTurning ? 6f : 12f);

    // Keep short native positioning manoeuvres, but turn before a long rally leg.
    // Explicit reverse/retreat tasks are excluded by the native adapter.
    internal static bool ForwardTravelOnly(float distance) => float.IsFinite(distance) && distance > 18f;

    private static uint Hash(string text, int slot, bool friendly)
    {
        var hash = 2166136261u;
        foreach (var c in text) hash = unchecked((hash ^ c) * 16777619u);
        hash = unchecked((hash ^ (uint)slot) * 16777619u);
        return unchecked((hash ^ (friendly ? 1u : 0u)) * 16777619u);
    }

    internal sealed class Reaction
    {
        private long target;
        private float firstSeen;
        private float lastSeen = float.NegativeInfinity;
        internal bool Ready(long visibleTarget, float now, float seconds)
        {
            if (visibleTarget == 0 || !float.IsFinite(now) || !float.IsFinite(seconds)) return false;
            if (target != visibleTarget || now - lastSeen > 1.5f || now < firstSeen)
            { target = visibleTarget; firstSeen = now; }
            lastSeen = now;
            return now - firstSeen >= Math.Clamp(seconds, 0f, 5f);
        }
        internal void Clear() { target = 0; lastSeen = float.NegativeInfinity; }
    }
}
