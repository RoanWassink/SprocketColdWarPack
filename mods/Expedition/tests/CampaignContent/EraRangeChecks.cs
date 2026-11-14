using System.Numerics;
using SprocketExpedition.Game;

internal static class EraRangeChecks
{
    internal static void Run()
    {
        static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
        var anchors = new[] { new SpawnArcAnchor(new(-20, -25), Vector2.UnitY),
            new SpawnArcAnchor(new(20, 25), Vector2.UnitY) };
        var previous = new CampaignSpawnRange(0, 0);
        for (var era = 0; era < 6; era++)
        {
            var range = CampaignSpawnRange.ForEra(era);
            Check(range.Valid && range.Minimum > previous.Minimum && range.Maximum > previous.Maximum,
                "both spawn distance bounds increase through the native era schedule");
            previous = range;
            for (var seed = 0; seed < 24; seed++)
            {
                var identity = $"era-{era}-seed-{seed}";
                Check(CampaignSpawnCover.TryPlan(anchors, 12, identity, (_, _) => true, (_, _) => false,
                    out var points, out var cover, out _, range: range) && !cover,
                    "open fallback preserves requested era distance rather than reverting to WWI");
                Check(CampaignSpawnCover.TryPlan(anchors, 12, identity, (_, _) => true, (_, _) => false,
                    out var retry, out _, out _, range: range) && points.SequenceEqual(retry), "era deployment retry is deterministic");
                Verify(points, range);
                foreach (var pattern in new[] { "Swarm", "Line", "Split" })
                {
                    var count = pattern == "Swarm" ? 12 : pattern == "Line" ? 3 : 8;
                    Check(CampaignSpecialSpawnPlanner.TryPlan(anchors, count, identity, pattern, (_, _) => true,
                        (_, _) => true, out var special, out var covered, out _, range: range) && covered,
                        "every special pattern supports every era distance");
                    Verify(special, range);
                }
            }
        }
        Check(CampaignSpawnRange.ForEra(5) == new CampaignSpawnRange(1200, 1500), "Coldwar restores long-distance spawns");
        Check(CampaignSpawnRange.ForEra(6) == CampaignSpawnRange.ForEra(5)
            && CampaignSpawnRange.ForEra(7) == CampaignSpawnRange.ForEra(5),
            "native eras after Coldwar keep the longest authored spawn range");
        var lateBefore = CampaignCombatPolicy.Create("range", 0, true, null, new[] { 10m, 20m, 30m }, eraIndex: 5);
        Check(CampaignCombatPolicy.Create("range", 0, true, null, new[] { 10m, 20m, 30m }, eraIndex: 7) == lateBefore,
            "combat ranges saturate at the Coldwar values for later native eras");
        // Run copy dates (slot 4: the saved Coldwar copy carries the old open-ended date,
        // now classified in the later Coldwar3).
        Check(CampaignEraDatePolicy.Decide(7, 5, writtenByExpedition: true) == RunCopyDateAction.Normalise,
            "an Expedition-written date that the game files in a later era is normalised");
        Check(CampaignEraDatePolicy.Decide(-1, 5, writtenByExpedition: true) == RunCopyDateAction.Normalise,
            "an Expedition-written date that lies in no era is normalised");
        Check(CampaignEraDatePolicy.Decide(7, 5, writtenByExpedition: false) == RunCopyDateAction.Reject,
            "a later-era date Expedition did not write keeps the error");
        Check(CampaignEraDatePolicy.Decide(-1, 5, writtenByExpedition: false) == RunCopyDateAction.Reject,
            "an unclassified foreign date keeps the error");
        Check(CampaignEraDatePolicy.Decide(5, 5, writtenByExpedition: true) == RunCopyDateAction.Keep
            && CampaignEraDatePolicy.Decide(5, 5, writtenByExpedition: false) == RunCopyDateAction.Keep,
            "a date in the build era is kept");
        Check(CampaignEraDatePolicy.Decide(2, 5, writtenByExpedition: false) == RunCopyDateAction.Advance,
            "an earlier-era copy advances to the end of the build era");
        var rejected = false;
        try { _ = CampaignSpawnRange.ForEra(-1); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "a negative era index is rejected");
        Check(!CampaignSpawnCover.TryPlan(anchors, 2, "bad", (_, _) => true, (_, _) => true,
            out var failed, out _, out _, range: new(float.NaN, 1500)) && failed.Length == 0,
            "invalid distance cannot yield a partial spawn layout");

        var dunes = CampaignSpawnRange.ForEra(5).FitToMap(750f, 678f);
        Check(dunes.Valid && dunes.Maximum < 700f,
            "the 750x678 authored rectangle must compress the Coldwar range before planning");
        var dunesMap = new CampaignAreaFootprint(Vector2.Zero, new Vector2(750, 678));
        var dunesAnchors = new[]
        {
            new SpawnArcAnchor(new Vector2(-40, -250), Vector2.UnitY),
            new SpawnArcAnchor(new Vector2(0, -250), Vector2.UnitY),
            new SpawnArcAnchor(new Vector2(40, -250), Vector2.UnitY)
        };
        var dunesRoles = new[] { "Center", "Center", "Center", "Flank", "Flank" };
        Check(CampaignSpecialSpawnPlanner.TryPlan(dunesAnchors, 5, "dunes-compact", "CombinedArms",
            (_, point) => dunesMap.Contains(point, 32f), (_, _) => false,
            out var dunesPoints, out _, out var dunesReport, range: dunes, deploymentRoles: dunesRoles),
            "compact Dunes rectangle still fits a complete themed group: " + dunesReport);
        Check(dunesPoints.All(point => dunesMap.Contains(point, 32f)),
            "no compact-map marker is accepted outside the authored usable rectangle");

        var wide = CampaignSpawnRange.ForEra(5).FitToMap(2400f, 1800f);
        Check(wide.Valid && wide.Maximum > dunes.Maximum,
            "a wider authored map adapts to a longer, still bounded range");
        Check(CampaignSpawnArcPlanner.TryPlan(dunesAnchors, 12, "wide-map",
            (_, _) => true, out var wideArc, out _, range: wide) && wideArc.Length == 12,
            "a wider map admits the complete twelve-marker group");
        Check(CampaignSpawnRange.ForEra(5).FitToMap(2400f, 2400f)
            == CampaignSpawnRange.ForEra(5),
            "a map that already contains the requested era range keeps it unchanged");
        Check(!CampaignSpawnRange.ForEra(5).FitToMap(180f, 180f).Valid,
            "a too-small authored rectangle cannot claim a safe group range");
        Check(dunes.Attempts().Count() >= 2 && dunes.Attempts().First() == dunes,
            "compact retries start with the exact map-fitted range");

        var costs = new[] { 10m, 100m };
        for (var slot = 0; slot < 12; slot++)
        {
            var early = CampaignCombatPolicy.Create("range", slot, true, null, costs);
            var late = CampaignCombatPolicy.Create("range", slot, true, null, costs, eraIndex: 5);
            Check(late.Role == early.Role && late.ReactionSeconds == early.ReactionSeconds
                && late.StopRange > early.StopRange && late.ResumeRange > late.StopRange,
                "era spacing changes combat distance without changing role or reaction skill");
            Check(late.Role switch {
                CampaignCombatPolicy.Role.Assault => late.StopRange is >= 450 and <= 600,
                CampaignCombatPolicy.Role.Line => late.StopRange is >= 700 and <= 900,
                _ => late.StopRange is >= 1000 and <= 1200 }, "Coldwar holds before point-blank range when a firing line exists");
        }
        Console.WriteLine("PASS all six era distances, normal/covered/open/special deployment, retry and era-aware combat ranges");

        void Verify(Vector2[] points, CampaignSpawnRange range)
        {
            foreach (var point in points) foreach (var anchor in anchors)
            {
                var delta = point - anchor.Position;
                Check(delta.Length() >= range.Minimum - .01f && delta.Length() <= range.Maximum + .01f
                    && Vector2.Dot(delta, anchor.Forward) >= 0, "each enemy respects every friendly's era front arc");
            }
            for (var i = 0; i < points.Length; i++) for (var j = 0; j < i; j++)
                Check(Vector2.Distance(points[i], points[j]) >= 23.99f, "era changes preserve enemy hull spacing");
        }
    }
}
