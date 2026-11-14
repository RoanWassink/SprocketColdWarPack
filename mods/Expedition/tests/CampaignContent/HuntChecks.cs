using System.Numerics;
using SprocketExpedition.Game;

internal static class HuntChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        var memory = new CampaignHuntPlanner.SpawnMemory();
        memory.Observe(1, true, new Vector2(0, 0));
        memory.Observe(1, true, new Vector2(9999, 9999));
        memory.Observe(3, false, new Vector2(400, 0));
        Check(!memory.Freeze(2, 2, false), "opening rally waits for the requested real spawns");
        memory.Observe(2, true, new Vector2(20, 0));
        memory.Observe(4, false, new Vector2(400, 40));
        Check(memory.Freeze(2, 2, false) && memory.Friendly == new Vector2(10, 0)
            && memory.Enemy == new Vector2(400, 20), "each team center uses first-observed hull positions exactly once");
        memory.Observe(2, true, new Vector2(2000, -500));
        memory.Observe(5, false, new Vector2(-9000, 10000));
        Check(memory.FriendlyCount == 2 && memory.EnemyCount == 2
            && memory.Friendly == new Vector2(10, 0) && memory.Enemy == new Vector2(400, 20),
            "frozen spawn facts ignore subsequent hidden movement, handoffs and late observations");
        var partial = new CampaignHuntPlanner.SpawnMemory();
        partial.Observe(1, true, Vector2.Zero);
        Check(!partial.Freeze(2, 3, true), "a timeout cannot invent a missing opposing spawn");
        partial.Observe(2, false, Vector2.UnitX * 400f);
        Check(partial.Freeze(2, 3, true), "a bounded partial fallback uses only actually observed hulls");

        var friendly = Vector2.Zero;
        var enemy = new Vector2(400, 0);
        foreach (var (origin, target) in new[] { (friendly, enemy), (enemy, friendly) })
        {
            var forward = Vector2.Normalize(target - origin);
            var slots = Enumerable.Range(0, 12)
                .Select(slot => CampaignHuntPlanner.ApproachWaypoint(target, origin, slot)).ToArray();
            for (var i = 0; i < slots.Length; i++)
            {
                Check(Vector2.Dot(slots[i] - origin, forward) > 300f
                    && Vector2.Distance(slots[i], target) < 80f,
                    "both teams advance along the closing axis toward the opposing spawn");
                for (var j = i + 1; j < slots.Length; j++)
                    Check(Vector2.Distance(slots[i], slots[j]) >= 23.99f,
                        "twelve approach slots cannot collapse into one destination");
                for (var step = 0; step < 8; step++)
                {
                    var point = CampaignHuntPlanner.SearchWaypoint(target, origin, i, step);
                    Check(CampaignHuntPlanner.Finite(point) && Vector2.Distance(point, target) <= 70f,
                        "directed searches stay near the contact instead of wandering on 175 m rings");
                    var along = Vector2.Dot(point - target, forward);
                    Check(along >= -48.01f && along <= 36.01f,
                        "search legs sweep a short corridor through the opposing spawn/contact");
                }
            }
        }
        Check(CampaignHuntPlanner.SearchWaypoint(new Vector2(float.NaN, 0), enemy, 0, 0) == Vector2.Zero,
            "invalid knowledge cannot produce a native waypoint");
        Check(CampaignHuntPlanner.NextSearchStep(CampaignHuntPlanner.SearchStepLimit) == 0,
            "directed search steps remain bounded");

        var progress = new CampaignHuntPlanner.GoalProgress();
        var destination = new Vector2(0, 200);
        Check(!progress.Stalled(destination, Vector2.Zero, 0)
            && !progress.Stalled(destination, Vector2.Zero, 8)
            && !progress.Stalled(destination, Vector2.Zero, 17),
            "slow early-era hulls get eighteen seconds to rotate/accelerate before recovery");
        Check(progress.Stalled(destination, new Vector2(25, 0), 18),
            "large sideways movement without closing the goal cannot mask a recovery loop");
        Check(!progress.Stalled(destination, new Vector2(0, 4), 19)
            && !progress.Stalled(destination, new Vector2(0, 4), 36),
            "actual goal progress renews the movement window");
        Check(progress.Stalled(destination, new Vector2(0, 4), 37),
            "new stalled movement is eventually detected after real forward progress");
        progress.GrantTurnTime(40);
        Check(!progress.Stalled(destination, new Vector2(0, 4), 57)
            && progress.Stalled(destination, new Vector2(0, 4), 58),
            "returning from a detour grants a complete native turning window");
        Check(!progress.Stalled(new Vector2(100, 200), new Vector2(0, 4), 59),
            "a materially changed genuine contact starts a fresh progress measurement");
        var recoveryA = CampaignHuntPlanner.RecoveryWaypoint(Vector2.Zero, destination, 0, 0);
        var recoveryB = CampaignHuntPlanner.RecoveryWaypoint(Vector2.Zero, destination, 0, 1);
        Check(recoveryA.X * recoveryB.X < 0 && recoveryA.Y >= 40 && recoveryB.Y >= 40
            && Vector2.Distance(recoveryA, destination) < destination.Length()
            && Vector2.Distance(recoveryB, destination) < destination.Length()
            && recoveryA.Length() < 50 && recoveryB.Length() < 50,
            "both alternative native recovery goals close the objective, rather than oscillating only sideways");

        Check(!CampaignHuntPlanner.ShouldRefresh(100, 10, true, 0, false, 10)
            && !CampaignHuntPlanner.ShouldRefresh(100, 99, false, 0, false, 10)
            && CampaignHuntPlanner.ShouldRefresh(110, 99, false, 0, false, 10),
            "progressing paths keep running and absent tasks get bounded retries");
        Check(CampaignHuntPlanner.ShouldRefresh(100, 99, true, 25 * 25, false, 10)
            && CampaignHuntPlanner.ShouldRefresh(100, 99, true, 0, true, 10)
            && !CampaignHuntPlanner.ShouldRefresh(100, 99, true, 0, true, 10,
                recoveryBlocked: true),
            "a materially shifted objective or proven stall can change a move order, but a failed recovery cannot reissue it every scan");
        var laneContact = new Vector2(0, 500);
        var doctrineLane = CampaignGroupDoctrine.ApproachWaypoint(
            laneContact, Vector2.Zero, CampaignGroupDoctrine.Center,
            CampaignGroupDoctrine.Side(1), 1);
        var huntLane = CampaignHuntPlanner.ApproachWaypoint(laneContact, Vector2.Zero, 1);
        Check(Math.Sign(doctrineLane.X) == Math.Sign(huntLane.X),
            "doctrine and hunt approach lanes must place the same slot on the same side");

        var targets = new[] { new CampaignHuntPlanner.VisibleTarget(1, 230, 0),
            new CampaignHuntPlanner.VisibleTarget(2, 200, 0) };
        Check(CampaignHuntPlanner.ChooseVisibleTarget(targets, 1) == 1,
            "small range changes do not interrupt aim at a current visible target");
        Check(CampaignHuntPlanner.ChooseVisibleTarget(new[] { targets[0], targets[1] with { Distance = 120 } }, 1) == 2,
            "a substantially closer visible threat can take priority");
        Check(CampaignHuntPlanner.ChooseVisibleTarget(new[] { new CampaignHuntPlanner.VisibleTarget(1, 200, 2),
            new CampaignHuntPlanner.VisibleTarget(2, 220, 0) }, -1) == 2,
            "team assignments avoid all tanks choosing one equally reachable opponent");
        Check(CampaignHuntPlanner.ChooseVisibleTarget(Array.Empty<CampaignHuntPlanner.VisibleTarget>(), 1) == -1,
            "an old known target is not an attack candidate without current visibility");
        Check(CampaignHuntPlanner.ChooseVisibleTarget(new[] {
            new CampaignHuntPlanner.VisibleTarget(1, float.NaN, 0) }, -1) == -1,
            "invalid target coordinates cannot become an attack");
        Check(!CampaignHuntPlanner.AttackDue(2, 0, true, true, true)
            && CampaignHuntPlanner.AttackDue(3, 0, true, true, false),
            "a new target respects minimum combat order spacing");
        Check(!CampaignHuntPlanner.AttackDue(7, 0, false, false, false)
            && CampaignHuntPlanner.AttackDue(8, 0, false, false, false)
            && !CampaignHuntPlanner.AttackDue(44, 0, true, false, false)
            && CampaignHuntPlanner.AttackDue(45, 0, true, false, false),
            "attack-token uncertainty does not reset aim every scan");
        Check(!CampaignHuntPlanner.StopToFire(300, false) && CampaignHuntPlanner.StopToFire(300, true)
            && CampaignHuntPlanner.StopToFire(280, false) && !CampaignHuntPlanner.StopToFire(341, true),
            "range hysteresis avoids repeated braking near the engagement boundary");
        Console.WriteLine("PASS team rally AI: frozen mutual spawn centers, twelve distinct closing slots, short directed search, goal-progress/turn grace, forward recovery and visible-target cadence");
    }
}
