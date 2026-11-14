using System.Numerics;
using SprocketExpedition.Game;

internal static class BossFormationChecks
{
    internal static void Run()
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        // A policy-7 ambush without flanker roles puts half of the centre
        // hulls on the side bearings instead of leaving them in one front line.
        var noFlank = CampaignGroupDoctrine.AmbushOffsets(
            new[] { "Center", "Center", "Center", "Center" }, 400f);
        var side = noFlank.Where(point => Math.Abs(point.Y) < .01f).ToArray();
        var front = noFlank.Where(point => Math.Abs(point.Y - 400f) < .01f).ToArray();
        Check(side.Length == 2 && front.Length == 2,
            "half of a no-flanker ambush group takes a side bearing");
        Check(side.Select(point => Math.Sign(point.X)).Distinct().Count() == 2
            && side.All(point => Math.Abs(Math.Abs(point.X) - 400f) < .01f),
            "the two side hulls sit at opposite 3/9 o'clock bearings");
        Check(noFlank.All(point => Math.Abs(point.Y) < .01f || point.Y > 0f),
            "no-flanker ambush points stay in the player's forward half-plane");

        // The Fortress escort box stays compact and in front of the group.
        var anchors = new[]
        {
            new SpawnArcAnchor(new Vector2(0, 0), Vector2.UnitY),
            new SpawnArcAnchor(new Vector2(20, 0), Vector2.UnitY)
        };
        var roles = Enumerable.Repeat("Center", 4).ToArray();
        Check(CampaignSpecialSpawnPlanner.TryPlan(anchors, 4, "fortress-box", "Defensive",
                (_, _) => true, (_, _) => true, out var points, out var covered,
                out var report, range: new CampaignSpawnRange(350, 500),
                deploymentRoles: roles, allowOpenFallback: false) && covered,
            "a Defensive boss box is a complete covered formation: " + report);
        Check(points.All(point => point.Y > 250f), "the defensive box stays in front");
        var width = points.Max(point => point.X) - points.Min(point => point.X);
        Check(width <= 60f, $"the defensive box stays compact, actual width {width:N1} m");
        Console.WriteLine("PASS boss formations: no-flank ambush half on side bearings and a compact Defensive escort box");
    }
}
