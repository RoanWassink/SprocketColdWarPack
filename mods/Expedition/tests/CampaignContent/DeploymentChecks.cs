using System.Numerics;
using SprocketExpedition.Game;

internal static class DeploymentChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }
        var original = new[] { new SpawnArcAnchor(new(900, 200), Vector2.UnitY),
            new SpawnArcAnchor(new(924, 200), Vector2.UnitY), new SpawnArcAnchor(new(948, 200), Vector2.UnitY) };
        var layouts = CampaignDeploymentPlanner.Candidates(original, "offer-1").ToArray();
        Check(layouts.Length == 12 && layouts[^1].SequenceEqual(original), "bounded proposals end with exact original anchors");
        Check(layouts.SelectMany(x => x).SequenceEqual(CampaignDeploymentPlanner.Candidates(original, "offer-1").SelectMany(x => x)),
            "retry reuses identical deployment geometry");
        Check(!layouts[0].SequenceEqual(CampaignDeploymentPlanner.Candidates(original, "offer-2").First()),
            "new encounter changes player position and formation heading");
        for (var n = 0; n < layouts.Length - 1; n++)
        {
            var layout = layouts[n];
            var oldCenter = original.Aggregate(Vector2.Zero, (sum, p) => sum + p.Position) / original.Length;
            var center = layout.Aggregate(Vector2.Zero, (sum, p) => sum + p.Position) / layout.Length;
            var distance = Vector2.Distance(center, oldCenter);
            Check(n == CampaignDeploymentPlanner.RotationCandidate ? distance < .01f
                : n < 8 ? distance is >= 249.99f and <= 650.01f : distance is >= 124.99f and <= 250.01f,
                "prefer substantial displacement before nearer and rotation-only fallbacks");
            for (var i = 0; i < layout.Length; i++)
            {
                Check(Math.Abs(layout[i].Forward.Length() - 1f) < .001f, "headings remain normalized");
                for (var j = 0; j < layout.Length; j++)
                    Check(Math.Abs(Vector2.Distance(layout[i].Position, layout[j].Position)
                        - Vector2.Distance(original[i].Position, original[j].Position)) < .01f, "rigid formation preserves native hull spacing");
            }
            Check(CampaignSpawnArcPlanner.TryPlan(layout, 12, "enemy-" + n, (_, _) => true, out var enemies),
                "rotated/translated full allied formation still admits twelve enemies");
            Check(enemies.All(p => layout.All(a => Vector2.Distance(p, a.Position) is >= 350f and <= 500f
                && Vector2.Dot(p - a.Position, a.Forward) >= 0)), "every enemy stays 350-500 m in front of every friendly");
        }
        Check(!CampaignDeploymentPlanner.Candidates(Array.Empty<SpawnArcAnchor>(), "empty").Any()
            && !CampaignDeploymentPlanner.Candidates(new[] { original[0] with { Position = new(float.NaN, 0) } }, "bad").Any(),
            "invalid anchors cannot qualify deployment candidates");
        Console.WriteLine("PASS varied player/ally deployment, preserved formation, stable retry, original fallback and full enemy arc geometry");
    }
}
