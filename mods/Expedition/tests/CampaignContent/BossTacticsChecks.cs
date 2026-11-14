using System.Numerics;
using SprocketExpedition.Game;

internal static class BossTacticsChecks
{
    internal static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        try
        {
            const string nativeName = "Expedition Boss Placeholder 01";
            CampaignBossTactics.Prepare(nativeName, "Fortress");
            Check(CampaignBossTactics.Match(nativeName, true, 1) == CampaignBossTactics.Character.Fortress,
                "one exact native enemy name qualifies for the requested character");
            Check(CampaignBossTactics.Match(nativeName, false, 1) == CampaignBossTactics.Character.None
                && CampaignBossTactics.Match(nativeName, true, 0) == CampaignBossTactics.Character.None
                && CampaignBossTactics.Match(nativeName, true, 2) == CampaignBossTactics.Character.None,
                "friendly hulls, missing identities and duplicate enemy names cannot become the boss");
            Check(!CampaignBossTactics.ExactEnemyName(nativeName.ToLowerInvariant(), true)
                && !CampaignBossTactics.ExactEnemyName(nativeName + " (Clone)", true)
                && !CampaignBossTactics.ExactEnemyName(null, true),
                "boss selection cannot fall back to fuzzy, GameObject or absent names");
            var hiddenPlayer = new Vector2(400, 250);
            Check(!CampaignBossTactics.TryPlayerPursuit(CampaignBossTactics.Character.Fortress, true, true,
                    hiddenPlayer, out _),
                "Fortress no longer receives a private player pursuit objective");
            CampaignBossTactics.Prepare(nativeName, "Hunter");
            Check(CampaignBossTactics.TryPlayerPursuit(CampaignBossTactics.Character.Hunter, true, true,
                    hiddenPlayer, out var pursuit) && pursuit == hiddenPlayer,
                "the identified Hunter knows the active player position without a sighting");
            hiddenPlayer = new Vector2(-50, 120);
            Check(CampaignBossTactics.TryPlayerPursuit(CampaignBossTactics.Character.Hunter, true, true,
                    hiddenPlayer, out pursuit) && pursuit == hiddenPlayer,
                "Hunter pursuit follows the new player position rather than a spawn or last sighting");
            Check(!CampaignBossTactics.TryPlayerPursuit(CampaignBossTactics.Character.None, true, true, hiddenPlayer, out _)
                && !CampaignBossTactics.TryPlayerPursuit(CampaignBossTactics.Character.Hunter, false, true, hiddenPlayer, out _)
                && !CampaignBossTactics.TryPlayerPursuit(CampaignBossTactics.Character.Hunter, true, false, hiddenPlayer, out _)
                && !CampaignBossTactics.TryPlayerPursuit(CampaignBossTactics.Character.Battery, true, true, hiddenPlayer, out _)
                && !CampaignBossTactics.TryPlayerPursuit(CampaignBossTactics.Character.Hunter, true, true,
                    new Vector2(float.NaN, 0), out _),
                "ordinary bosses, allies, unavailable players and invalid coordinates cannot track");
            Check(CampaignBossTactics.StopToFire(CampaignBossTactics.Character.Fortress, 700, false)
                && CampaignBossTactics.StopToFire(CampaignBossTactics.Character.Battery, 100, false),
                "Fortress and Battery stop to fire at a genuinely visible opponent");
            Check(!CampaignBossTactics.StopToFire(CampaignBossTactics.Character.Hunter, 250, false)
                && CampaignBossTactics.StopToFire(CampaignBossTactics.Character.Veteran, 250, false)
                && CampaignBossTactics.StopToFire(CampaignBossTactics.Character.Hunter, 140, false)
                && CampaignBossTactics.StopToFire(CampaignBossTactics.Character.Hunter, 190, true)
                && !CampaignBossTactics.StopToFire(CampaignBossTactics.Character.Hunter, 201, true),
                "Hunter closes further before stopping, with hysteresis instead of brake chatter");
            var battery = new Vector2(200, 50);
            var threat = new Vector2(100, 50);
            Check(CampaignBossTactics.TryBatteryRetreat(CampaignBossTactics.Character.Battery, battery,
                    threat, 300f, out var retreat)
                && retreat.X > battery.X && retreat.Y == battery.Y
                && Vector2.Distance(retreat, battery) <= 50f
                && Vector2.Distance(retreat, threat) > Vector2.Distance(battery, threat),
                "Battery withdraws after the player closes inside half the initial range");
            Check(!CampaignBossTactics.TryBatteryRetreat(CampaignBossTactics.Character.Battery, battery,
                    threat, 150f, out _)
                && !CampaignBossTactics.TryBatteryRetreat(CampaignBossTactics.Character.Battery, battery,
                    threat, 0f, out _)
                && !CampaignBossTactics.TryBatteryRetreat(CampaignBossTactics.Character.Hunter, battery,
                    threat, 300f, out _)
                && !CampaignBossTactics.TryBatteryRetreat(CampaignBossTactics.Character.Battery, battery,
                    battery, 300f, out _)
                && !CampaignBossTactics.TryBatteryRetreat(CampaignBossTactics.Character.Battery, battery,
                    new Vector2(float.NaN, 0), 300f, out _),
                "retreat intent rejects half-range, other roles, degenerate targets and invalid input");
            Check(CampaignBossTactics.HoldsPosition(CampaignBossTactics.Character.Fortress)
                && !CampaignBossTactics.HoldsPosition(CampaignBossTactics.Character.Hunter)
                && !CampaignBossTactics.HoldsPosition(CampaignBossTactics.Character.Battery),
                "only the Fortress holds position by character policy");
            Check(CampaignBossTactics.SidestepsOnly(CampaignBossTactics.Character.Fortress)
                && CampaignBossTactics.SidestepsOnly(CampaignBossTactics.Character.Battery)
                && !CampaignBossTactics.SidestepsOnly(CampaignBossTactics.Character.Hunter),
                "Fortress and Battery use the sideways-only fallback");
            var target = new Vector2(200, 50);
            var start = new Vector2(100, 50);
            Check(CampaignBossTactics.TrySidestep(CampaignBossTactics.Character.Fortress, start, target, 1, out var step)
                && Math.Abs(step.Y - start.Y) <= CampaignBossTactics.MaximumSidestep
                && Vector2.Distance(step, start) <= CampaignBossTactics.MaximumSidestep
                && step.X == start.X,
                "the Fortress fallback is a lateral step of at most 20 m, never an advance");
            Check(CampaignBossTactics.TrySidestep(CampaignBossTactics.Character.Battery, start, target, -1, out var back)
                && back.X == start.X && Math.Abs(back.Y - start.Y) <= CampaignBossTactics.MaximumSidestep,
                "the Battery fallback is sideways only");
            Check(CampaignBossTactics.MaySlowAdvance(CampaignBossTactics.Character.Fortress, 90f, 0f)
                && !CampaignBossTactics.MaySlowAdvance(CampaignBossTactics.Character.Fortress, 89.9f, 0f)
                && !CampaignBossTactics.MaySlowAdvance(CampaignBossTactics.Character.Battery, 200f, 0f),
                "only a Fortress may make one slow move after 90 s without contact");
            Check(CampaignBossTactics.TrySlowAdvancePoint(new Vector2(0, 0), new Vector2(0, 500), out var slow)
                && Vector2.Distance(slow, new Vector2(0, 0)) <= CampaignBossTactics.FortressSlowStepMeters,
                "the Fortress slow move is bounded to a short step");
            Check(CampaignBossTactics.BehaviorName(CampaignBossTactics.Character.Fortress).Contains("dug-in")
                && CampaignBossTactics.BehaviorName(CampaignBossTactics.Character.Hunter).Contains("flanking")
                && CampaignBossTactics.BehaviorName(CampaignBossTactics.Character.Battery).Contains("long range"),
                "each boss behavior has one stable log description");
            CampaignBossTactics.Prepare(nativeName, "unknown-role");
            Check(!CampaignBossTactics.Enabled, "unknown characters do not silently select an arbitrary boss behavior");
            CampaignBossTactics.Prepare(nativeName, "999");
            Check(!CampaignBossTactics.Enabled, "undefined enum numbers are rejected");
            CampaignBossTactics.Prepare(nativeName, "veteran");
            Check(CampaignBossTactics.Enabled && CampaignBossTactics.PreparedCharacter == CampaignBossTactics.Character.Veteran,
                "character identifiers accept stable case-insensitive content labels");
            CampaignBossTactics.Release();
            Check(!CampaignBossTactics.Enabled && CampaignBossTactics.NativeTankName.Length == 0,
                "encounter release removes all configured boss identity and tactics");
            Check(!CampaignBossTactics.TryPlayerPursuit(CampaignBossTactics.Character.Hunter, true, true,
                    hiddenPlayer, out _), "player tracking cannot leak into the next encounter after release");
            Console.WriteLine("PASS boss tactics: exact identity, Hunter pursuit, Fortress hold, 20 m sidestep, half-range Battery retreat and release");
        }
        finally { CampaignBossTactics.Release(); }
    }
}
