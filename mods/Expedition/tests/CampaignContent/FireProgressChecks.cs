using System.Numerics;
using SprocketExpedition.Game;

// 0.3.5 fire-progress tracker. The negative/positive series below are taken
// from fixtures\LogOutput-gevecht25-26.log (battle 25); each 15 s weapon sample
// is held until the next one, and observed shots are placed at their logged
// fireBufferHead change. A 0.25 s tick matches the live AI scan.
internal static class FireProgressChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static float Abs(float value) => Math.Abs(value);

        // AMX-13-90 (enemy slot 1): only two shots, then has=False and the gun
        // error locks at -21.12 deg. Last shot at t=30, so the stall starts at 38.
        {
            var tracker = new CampaignCombatPolicy.FireTracker();
            tracker.Reset(0f);
            for (var step = 0; step <= 160; step++)
            {
                var now = step * .25f;
                if (now > 39f) break;
                var shot = Abs(now - 15f) < .01f || Abs(now - 30f) < .01f ? 1 : 0;
                var error = now < 30f ? -3.83f : -21.12f;
                tracker.Observe(now, shot, true, error, 11f, false);
            }
            Check(tracker.Shots == 2, "AMX-13-90 counts exactly two logged shots");
            Check(tracker.Stalled, "AMX-13-90 is stalled after its last shot");
            Check(tracker.StalledFor >= 8f && tracker.StalledFor < 9.5f,
                "AMX-13-90 stalls about 8 s after the last shot, not later");
        }

        // Cruiser Mk VII Cavalier (enemy slot 3): last logged shot at t=45,
        // hull angle rises 43 -> 53 deg while FaceTargetTask runs and the error
        // stays at/above its last-shot value, so this is a stall at t=53.
        {
            var tracker = new CampaignCombatPolicy.FireTracker();
            tracker.Reset(0f);
            for (var step = 0; step <= 212; step++)
            {
                var now = step * .25f;
                if (now > 53f) break;
                var shot = now < .01f || Abs(now - 15f) < .01f || Abs(now - 30f) < .01f || Abs(now - 45f) < .01f ? 1 : 0;
                var error = now < 15f ? .48f : now < 30f ? .54f : now < 45f ? .00f : now < 60f ? .52f : 2.78f;
                var hull = now < 15f ? 43f : now < 30f ? 45f : now < 45f ? 48f : now < 60f ? 50f : 53f;
                tracker.Observe(now, shot, true, error, hull, turning: true);
            }
            Check(tracker.Shots == 4, "Cavalier counts the four logged shots before the stall");
            Check(tracker.Stalled && tracker.StalledFor >= 8f && tracker.StalledFor < 9.5f,
                "Cavalier stalls about 8 s after its last shot despite FaceTargetTask");
        }

        // bmpt terminator (enemy slot 2): its 15 s samples contain 6/3/4/5/4/3/1
        // fireBufferHead changes, 26 shots over 105 s. Spread those logged shots
        // across that span; the largest gap stays below 8 s, so it never stalls.
        {
            var tracker = new CampaignCombatPolicy.FireTracker();
            tracker.Reset(0f);
            const int loggedShots = 26;
            var eventTimes = new List<float>();
            for (var shot = 1; shot <= loggedShots; shot++)
                eventTimes.Add(shot * 105f / (loggedShots + 1));
            var next = 0;
            var everStalled = false;
            for (var step = 0; step <= 420; step++)
            {
                var now = step * .25f;
                var shots = 0;
                while (next < eventTimes.Count && eventTimes[next] <= now + .001f) { shots++; next++; }
                // The Terminator's logged deviations stay within a band that
                // never improves by 0.5 deg inside a 2 s window.
                var error = -0.48f + (step % 8) * .02f;
                tracker.Observe(now, shots, true, error, 0f, false);
                everStalled |= tracker.Stalled;
            }
            Check(!everStalled, "Terminator never stalls: its logged shot cadence beats 8 s");
        }

        // A 15 s reload without a shot is not a stall: the timer is paused.
        {
            var tracker = new CampaignCombatPolicy.FireTracker();
            tracker.Reset(0f);
            for (var step = 0; step <= 60; step++)
            {
                var now = step * .25f;
                if (now <= 15f) tracker.Observe(now, 0, false, 5f, 20f, false);
            }
            Check(!tracker.Stalled && tracker.StalledFor == 0f,
                "a 15 s reload pauses the stall timer completely");
            for (var step = 61; step <= 90; step++) tracker.Observe(step * .25f, 0, true, 5f, 20f, false);
            Check(!tracker.Stalled, "the timer resumes after reload, not before");
            for (var step = 91; step <= 96; step++) tracker.Observe(step * .25f, 0, true, 5f, 20f, false);
            Check(tracker.Stalled, "eight loaded seconds after the reload do stall");
        }

        // A hull that does not turn while FaceTargetTask runs is a stall ...
        {
            var tracker = new CampaignCombatPolicy.FireTracker();
            tracker.Reset(0f);
            for (var step = 0; step <= 36; step++)
                tracker.Observe(step * .25f, 0, true, 1f, 30f, turning: true);
            Check(tracker.Stalled, "constant hull angle during FaceTargetTask is a stall");

            // ... while 2 deg of hull improvement inside 2 s is progress.
            var turning = new CampaignCombatPolicy.FireTracker();
            turning.Reset(0f);
            for (var step = 0; step <= 38; step++)
            {
                var now = step * .25f;
                turning.Observe(now, 0, true, 1f, now < 2f ? 30f : 27.9f, turning: true);
            }
            Check(!turning.Stalled, "a 2 deg hull turn within 2 s counts as progress");
        }

        // Aim error: 0.5 deg inside 2 s is progress; even 0.49 deg is not.
        {
            var good = new CampaignCombatPolicy.FireTracker();
            good.Reset(0f);
            for (var step = 0; step <= 36; step++)
            {
                var now = step * .25f;
                good.Observe(now, 0, true, now < 2f ? 5f : 4.4f, float.NaN, false);
            }
            Check(!good.Stalled && good.StalledFor < 8f, "0.5 deg aim improvement resets the stall timer");
            var bad = new CampaignCombatPolicy.FireTracker();
            bad.Reset(0f);
            for (var step = 0; step <= 36; step++)
            {
                var now = step * .25f;
                bad.Observe(now, 0, true, now < 2f ? 5f : 4.51f, float.NaN, false);
            }
            Check(bad.Stalled, "0.49 deg aim improvement does not reset the stall timer");
        }

        // Flank side alternates on every stall and stays inside the 25-40 m /
        // 15-30 m band. Ambushers never receive the movement.
        Check(CampaignCombatPolicy.NextFlankSide(1) == -1
            && CampaignCombatPolicy.NextFlankSide(-1) == 1,
            "a following stall always takes the other flank side");
        for (var slot = 0; slot < 8; slot++)
        {
            var lateral = CampaignCombatPolicy.FlankLateral(slot);
            var forward = CampaignCombatPolicy.FlankForward(slot);
            Check(lateral >= 25f && lateral <= 40f && forward >= 15f && forward <= 30f,
                "flank offsets stay inside 25-40 m sideways and 15-30 m forward");
            var start = new Vector2(10f, -20f);
            var target = new Vector2(10f, 80f);
            var right = CampaignCombatPolicy.FlankWaypoint(start, target, 1, lateral, forward);
            var left = CampaignCombatPolicy.FlankWaypoint(start, target, -1, lateral, forward);
            Check(right.X - start.X is > 0 and <= 40f && Math.Abs(right.X - start.X - lateral) < .01f
                && Math.Abs(right.Y - (start.Y + forward)) < .01f,
                "flank point moves exactly the requested lateral/forward distance");
            Check(Math.Abs(left.X - start.X + lateral) < .01f && left.Y == right.Y,
                "the other flank side is the signed mirror");
        }
        Check(CampaignCombatPolicy.Repositions(false, false, false, true),
            "a mobile AI hull may reposition");
        Check(!CampaignCombatPolicy.Repositions(true, false, false, true),
            "an Ambusher never repositions");
        Check(!CampaignCombatPolicy.Repositions(false, true, false, true)
            && !CampaignCombatPolicy.Repositions(false, false, true, true)
            && !CampaignCombatPolicy.Repositions(false, false, false, false),
            "yielded, player-controlled and immobile hulls never reposition");

        Console.WriteLine("PASS fire-progress tracker: battle-25 AMX/Cavalier stalls, Terminator cadence, "
            + "reload pause, hull-turn and 0.5 deg/2 s aim progress, alternating grounded flank and Ambusher hold");
    }
}
