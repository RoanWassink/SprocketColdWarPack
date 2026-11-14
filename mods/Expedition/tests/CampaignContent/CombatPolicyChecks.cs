using SprocketExpedition.Game;

internal static class CombatPolicyChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        decimal[] costs = { 10m, 20m, 40m, 100m };
        var roles = new HashSet<CampaignCombatPolicy.Role>();
        var delays = new HashSet<float>();
        for (var slot = 0; slot < 100; slot++)
        {
            var cheap = CampaignCombatPolicy.Create("encounter", slot, false, 10m, costs);
            var expensive = CampaignCombatPolicy.Create("encounter", slot, false, 100m, costs);
            var middle = CampaignCombatPolicy.Create("encounter", slot, true, null, costs);
            Check(cheap.ReactionSeconds == 0f && expensive.ReactionSeconds == 0f && middle.ReactionSeconds == 0f,
                "0.3.3: every profile reacts at once; native laying is the only first-shot delay");
            Check(cheap == CampaignCombatPolicy.Create("encounter", slot, false, 10m, costs.Reverse().ToArray()),
                "profiles are reproducible and independent of catalogue ordering");
            Check(CampaignCombatPolicy.StopToFire(cheap, cheap.StopRange, false)
                && CampaignCombatPolicy.StopToFire(cheap, cheap.StopRange + 30f, true)
                && !CampaignCombatPolicy.StopToFire(cheap, cheap.ResumeRange + 1, true), "role ranges have stop/resume hysteresis");
            Check(!CampaignCombatPolicy.StopToFire(cheap, float.NaN, false), "invalid ranges cannot trigger combat");
            roles.Add(cheap.Role); roles.Add(expensive.Role); delays.Add(cheap.ReactionSeconds);
        }
        var firstAlly = CampaignCombatPolicy.Create("small-fleet", 0, true, null, costs);
        var secondAlly = CampaignCombatPolicy.Create("small-fleet", 1, true, null, costs);
        var supportAlly = CampaignCombatPolicy.Create("small-fleet", 2, true, null, costs);
        Check(firstAlly.Role == CampaignCombatPolicy.Role.Assault && secondAlly.Role == CampaignCombatPolicy.Role.Line
            && supportAlly.Role == CampaignCombatPolicy.Role.FireSupport
            && CampaignCombatPolicy.HoldForShot(firstAlly, 1400, false, true)
            && CampaignCombatPolicy.HoldForShot(secondAlly, 350, false, true)
            && !CampaignCombatPolicy.HoldForShot(firstAlly, float.NaN, false, true),
            "0.3.3 seek and destroy: any role with a clear shot stops at any valid range");
        Check(CampaignCombatPolicy.HoldForShot(supportAlly, 350, false, true)
            && !CampaignCombatPolicy.HoldForShot(supportAlly, 350, true, false)
            && !CampaignCombatPolicy.HoldForShot(firstAlly, 80, true, false),
            "even an acquired close contact cannot keep any role halted without a clear firing line");
        Check(roles.Count == 3 && delays.Count == 1, "formations retain three distinct roles; reaction is uniformly zero");
        Check(CampaignCombatPolicy.FaceToFire(180f, false) && CampaignCombatPolicy.FaceToFire(15f, false)
            && CampaignCombatPolicy.FaceToFire(8f, true) && !CampaignCombatPolicy.FaceToFire(8f, false)
            && !CampaignCombatPolicy.FaceToFire(6f, true) && !CampaignCombatPolicy.FaceToFire(float.NaN, false),
            "a target beside/behind the hull requires stationary facing before a strict firing hold, with hysteresis");
        Check(CampaignCombatPolicy.ForwardTravelOnly(477f) && CampaignCombatPolicy.ForwardTravelOnly(44f)
            && !CampaignCombatPolicy.ForwardTravelOnly(18f) && !CampaignCombatPolicy.ForwardTravelOnly(8f)
            && !CampaignCombatPolicy.ForwardTravelOnly(float.NaN)
            && !CampaignCombatPolicy.ForwardTravelOnly(float.PositiveInfinity)
            && !CampaignCombatPolicy.ForwardTravelOnly(-10f),
            "long rally and recovery legs prefer forward travel; arrival manoeuvres and invalid ranges retain native defaults");
        foreach (var price in new decimal?[]{ null, 0, 10, decimal.MaxValue })
        {
            var p = CampaignCombatPolicy.Create("single", 0, false, price, new[]{10m});
            Check(p.ReactionSeconds == 0f && float.IsFinite(p.StopRange), "small pools and unmeasured prices remain bounded");
        }
        var boss = CampaignCombatPolicy.Create("boss", 0, false, null, costs, boss: true);
        Check(boss.ReactionSeconds == 0f, "bosses react at once like every other profile");
        var instant = new CampaignCombatPolicy.Reaction();
        Check(instant.Ready(33, 0, 0), "a zero reaction fires on the first genuine sighting");
        var reaction = new CampaignCombatPolicy.Reaction();
        Check(!reaction.Ready(11, 0, 2), "first sighting begins the timer");
        Check(!reaction.Ready(11, 1, 2) && !reaction.Ready(11, 1.9f, 2) && reaction.Ready(11, 2, 2),
            "consecutive scans wait once rather than rerolling or resetting reaction");
        Check(reaction.Ready(11, 3, 2), "short visibility flicker retains acquired reaction");
        Check(!reaction.Ready(11, 5, 2), "a long loss of visibility starts a new acquisition");
        Check(!reaction.Ready(22, 5.2f, 1), "a different target needs its own reaction");
        reaction.Clear();
        Check(!reaction.Ready(22, 5.3f, 1) && !reaction.Ready(0, 99, 1)
            && !reaction.Ready(22, float.NaN, 1), "release and invalid/hidden targets cannot carry an acquired shot");

        var gate = new CampaignReturnGate();
        Check(gate.Request() && gate.Pending && !gate.Request(), "return requests coalesce before native work");
        Check(!gate.Complete(true, false) && gate.Pending, "menu evidence cannot clear guards before a scene request");
        Check(!gate.AllowsCleanup("ResetGame", false) && gate.AllowsCleanup("ResetGame", true),
            "running-battle resets stay suppressed but native Exiting state always permits actual cleanup");
        gate.SceneStarted();
        Check(gate.AllowsCleanup("ResetGame", false) && !gate.AllowsCleanup("Retry", false)
            && !gate.AllowsCleanup("SetupGame", false),
            "scene exit must await its real ResetGame task while restart/setup remain suppressed");
        Check(!gate.Complete(false, false) && !gate.Complete(true, true) && gate.Pending,
            "guards survive loading and old game-controller teardown");
        Check(gate.Complete(true, false) && !gate.Pending && !gate.SceneRequested,
            "guards release only after the actual native main menu is ready");
        Check(gate.Request(), "a later encounter can return independently");
        gate.Reset();
        Check(!gate.Pending, "destroyed host clears its return generation");
        Console.WriteLine("PASS cost-weighted varied combat roles, zero reaction, seek-and-destroy holds, acquisition timing and native unload guard lifecycle");
    }
}
