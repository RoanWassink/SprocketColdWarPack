using System.Numerics;
using SprocketExpedition.Game;

internal static class SupportDepthChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var bearings = new[] { 0f, .7f, 1.9f, 3.0f };
        var ranges = new[] { new CampaignSpawnRange(350f, 500f), new CampaignSpawnRange(250f, 350f) };
        var roleSets = new[]
        {
            new[] { "Center", "Center", "Support" },
            new[] { "Center", "Center", "Support", "Flank", "Flank" }
        };

        foreach (var bearing in bearings)
        {
            var forward = new Vector2(MathF.Sin(bearing), MathF.Cos(bearing));
            var anchors = new[] { new SpawnArcAnchor(Vector2.Zero, forward) };
            foreach (var range in ranges)
            foreach (var roles in roleSets)
            for (var seed = 0; seed < 8; seed++)
            {
                var identity = $"support-depth|{bearing}|{range.Minimum}|{roles.Length}|{seed}";
                Check(CampaignSpecialSpawnPlanner.TryPlan(anchors, roles.Length, identity,
                        "CombinedArms", (_, _) => true, (_, _) => false,
                        out var points, out _, out var diagnostics, range: range,
                        deploymentRoles: roles),
                    $"production combined-arms placement failed: {identity}; {diagnostics}");
                float Projection(Vector2 point) => Vector2.Dot(point, forward);
                var centreProjection = Math.Max(Projection(points[0]), Projection(points[1]));
                var supportProjection = Projection(points[2]);
                Check(supportProjection > centreProjection + 20f,
                    $"support must be at least 20 m farther from the player than the centre "
                    + $"({supportProjection - centreProjection:N1} m) at {identity}");
                for (var i = 0; i < points.Length; i++)
                {
                    var distance = Vector2.Distance(points[i], anchors[0].Position);
                    Check(distance >= range.Minimum - .01f && distance <= range.Maximum + .01f,
                        $"world range must remain intact at {identity}");
                    for (var j = 0; j < i; j++)
                        Check(Vector2.Distance(points[i], points[j]) >= 23.99f,
                            $"support world placement must not overlap at {identity}");
                }
            }
        }
        Console.WriteLine("PASS world-space support depth: actual TryPlan keeps support >20 m behind centre across bearings, ranges and flanker variants");
    }
}
