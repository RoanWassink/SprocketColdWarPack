using System.Numerics;
using SprocketExpedition.Game;

internal static class SpawnCoverChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        var starts = new[] { Vector3.Zero, new Vector3(20, 5, 0) };
        var enemy = new Vector3(0, 0, 400);
        var rays = 0;
        Check(CampaignSpawnCover.Occluded(starts, enemy, (a, b) => { rays++; return true; }) && rays == 24,
            "cover must block camera, hull/turret rays and both flanks from every friendly start");
        Check(!CampaignSpawnCover.Occluded(starts, enemy, (a, b) => a.Y < 2), "a low wall cannot hide the turret");
        Check(!CampaignSpawnCover.Occluded(new[] { Vector3.Zero }, enemy, (a, b) => Math.Abs(a.X) < 0.1f),
            "a narrow central blocker cannot hide the full tank envelope");
        Check(!CampaignSpawnCover.Occluded(starts, enemy, (a, b) => a.X < 10),
            "one friendly having a clear shot rejects covered placement");
        Check(!CampaignSpawnCover.Occluded(Array.Empty<Vector3>(), enemy, (_, _) => true), "missing origins cannot establish cover");
        var anchors = new[] { new SpawnArcAnchor(Vector2.Zero, Vector2.UnitY) };
        Check(CampaignSpawnCover.TryPlan(anchors, 12, "cover-test", (_, _) => true, (_, p) => p.X > 0,
            out var covered, out var hasCover, out _) && hasCover && covered.All(p => p.X > 0),
            "prefer a complete covered layout inside the same distance bounds");
        Check(CampaignSpawnCover.TryPlan(anchors, 12, "cover-test", (_, _) => true, (_, p) => p.X > 0,
            out var retry, out _, out _) && covered.SequenceEqual(retry), "cover retry keeps encounter-seeded positions");
        Check(CampaignSpawnCover.TryPlan(anchors, 12, "flat-open-map", (_, _) => true, (_, _) => false,
            out var open, out hasCover, out _) && !hasCover && open.Length == 12
            && open.All(p => p.Length() >= 350 && p.Length() <= 500),
            "no cover is an explicit open fallback while distance and count remain intact");
        Check(!CampaignSpawnCover.TryPlan(anchors, 2, "blocked-map", (_, _) => false, (_, _) => true,
            out var failed, out _, out _) && failed.Length == 0, "cover search never bypasses unsafe terrain");
        var longLine = Enumerable.Range(0, 16).Select(i => new Vector2(i * 70f, 0)).ToArray();
        var irregular = new[] { 0f, 20f, 40f, 60f, 400f, 500f, 600f, 700f }.Select(x => new Vector2(x, 0)).ToArray();
        Check(CampaignSpawnCover.FriendlyIndices(irregular, 4).SequenceEqual(new[] { 0, 1, 2, 3 }),
            "a compact authored cluster is preferred over a central group spanning a large gap");
        foreach (var count in new[] { 1, 8 })
        {
            var selected = CampaignSpawnCover.FriendlyIndices(longLine, count);
            Check(selected.Length == count && selected.Distinct().Count() == count
                && selected.SequenceEqual(CampaignSpawnCover.FriendlyIndices(longLine, count)),
                "select exactly the actual friendly quantity, deterministically, from native slots");
            var selectedAnchors = selected.Select(i => new SpawnArcAnchor(longLine[i], Vector2.UnitY)).ToArray();
            Check(CampaignSpawnArcPlanner.TryPlan(selectedAnchors, 12, "wide-map-" + count, (_, _) => true, out _),
                "a kilometre-wide native lineup works with only the selected friendly formation");
        }
        Console.WriteLine("PASS hull/turret/flank visibility, covered preference/open fallback, deterministic friendly slot selection");
    }
}
