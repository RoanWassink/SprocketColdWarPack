using System.Numerics;
using SprocketExpedition.Game;

internal static class SpawnSafetyChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

        var offsets = CampaignSpawnSupport.Footprint;
        Check(offsets.Length == 9, "conservative spawn footprint exposes every support sample");
        var hullHalfDiagonal = MathF.Sqrt(5.5f * 5.5f + 3.5f * 3.5f);
        Check(CampaignSpawnSupport.HalfExtent >= hullHalfDiagonal,
            "the rotation-independent half extent covers the old 11 x 7 m hull corner at any yaw");
        Check(offsets.All(offset =>
                MathF.Sqrt(offset.X * offset.X + offset.Y * offset.Y)
                <= CampaignSpawnSupport.HalfExtent + 0.001f),
            "every footprint sample lies inside the rotation-independent square");
        Check(offsets.Count(offset => offset == Vector2.Zero) == 1,
            "the footprint keeps exactly one center sample");
        var rotatedHullRadius = Enumerable.Range(0, 8).Select(index =>
        {
            var angle = index * MathF.PI / 4f;
            var x = 5.5f * MathF.Cos(angle) - 3.5f * MathF.Sin(angle);
            var y = 5.5f * MathF.Sin(angle) + 3.5f * MathF.Cos(angle);
            return MathF.Sqrt(x * x + y * y);
        }).Max();
        Check(rotatedHullRadius <= CampaignSpawnSupport.HalfExtent + 0.001f,
            "a rotated 11 x 7 m hull rectangle cannot overhang the sampled footprint");
        var flat = offsets.Select(offset =>
            new SpawnGroundSample(new Vector3(offset.X, 10f, offset.Y), Vector3.UnitY, true)).ToArray();
        Check(CampaignSpawnSupport.TryEvaluate(flat, out var flatCenter, out var flatError),
            "a truly flat spawn patch is accepted: " + flatError);
        Check(flatCenter.Y == 10f, "flat support reports the sampled terrain height");

        var uneven = flat.ToArray();
        uneven[^1] = new SpawnGroundSample(new Vector3(uneven[^1].Point.X,
            10f + CampaignSpawnSupport.MaximumHeightDelta + .5f, uneven[^1].Point.Z),
            Vector3.UnitY, true);
        Check(!CampaignSpawnSupport.TryEvaluate(uneven, out _, out var unevenError)
            && unevenError.Contains("height delta", StringComparison.Ordinal),
            "a flat center with an uneven footprint edge is rejected");
        var slopeRadians = 6f * MathF.PI / 180f;
        var normal = Vector3.Normalize(new Vector3(-MathF.Tan(slopeRadians), 1f, 0f));
        var rolling = offsets.Select(offset => new SpawnGroundSample(
            new Vector3(offset.X, 10f + offset.X * MathF.Tan(slopeRadians), offset.Y),
            normal, true)).ToArray();
        Check(CampaignSpawnSupport.TryEvaluate(rolling, out _, out var rollingError),
            "a continuous 6-degree dune slope inside the coherent height allowance is accepted: "
            + rollingError);

        var steep = flat.ToArray();
        steep[^1] = new SpawnGroundSample(steep[^1].Point,
            Vector3.Normalize(new Vector3(.4f, .8f, .4f)), true);
        Check(!CampaignSpawnSupport.TryEvaluate(steep, out _, out var steepError)
            && steepError.Contains("slope", StringComparison.Ordinal),
            "a steep footprint corner is rejected even when the center is flat");

        var missing = flat.ToArray();
        missing[^1] = new SpawnGroundSample(default, default, false);
        Check(!CampaignSpawnSupport.TryEvaluate(missing, out _, out _),
            "a missing terrain sample fails safe");

        var nonfinite = flat.ToArray();
        nonfinite[^1] = new SpawnGroundSample(new Vector3(float.NaN, 10f, 0f), Vector3.UnitY, true);
        Check(!CampaignSpawnSupport.TryEvaluate(nonfinite, out _, out _),
            "a nonfinite terrain sample fails safe");

        var anchors = new[]
        {
            new SpawnArcAnchor(new Vector2(-20, -25), Vector2.UnitY),
            new SpawnArcAnchor(new Vector2(20, 25), Vector2.UnitY)
        };
        var roles = new[] { "Center", "Center", "Center", "Flank", "Flank" };
        Check(!CampaignSpecialSpawnPlanner.TryPlan(anchors, 5, "exposed-first", "CombinedArms",
                (_, _) => true, (_, _) => false, out _, out _, out var exposedReport,
                range: new CampaignSpawnRange(350, 500), deploymentRoles: roles,
                allowOpenFallback: false),
            "production covered-only search rejects an exposed candidate: " + exposedReport);
        Check(CampaignSpecialSpawnPlanner.TryPlan(anchors, 5, "hidden-later", "CombinedArms",
                (_, _) => true, (_, _) => true, out _, out var hiddenCover,
                out var hiddenReport, range: new CampaignSpawnRange(350, 500),
                deploymentRoles: roles, allowOpenFallback: false) && hiddenCover,
            "covered-only search accepts a later complete hidden candidate: " + hiddenReport);
        Check(!CampaignSpecialSpawnPlanner.TryPlan(anchors, 5, "partial-group", "CombinedArms",
                (_, _) => true, (index, _) => index != 4, out _, out _, out _,
                range: new CampaignSpawnRange(350, 500), deploymentRoles: roles,
                allowOpenFallback: false),
            "one exposed member rejects the complete group rather than moving a partial formation");

        var ambushRoles = new[] { "Center", "Center", "Center", "Flank", "Flank" };
        Check(CampaignSpecialSpawnPlanner.TryPlan(anchors, 5, "ambush-layout", "Ambush",
                (_, _) => true, (_, _) => true, out var ambush, out var covered,
                out var ambushReport, range: new CampaignSpawnRange(350, 500),
                deploymentRoles: ambushRoles, allowOpenFallback: false) && covered,
            "a complete hidden ambush layout is placed: " + ambushReport);
        Check(CampaignSpecialSpawnPlanner.TryPlan(anchors, 5, "ambush-layout", "Ambush",
                (_, _) => true, (_, _) => true, out var ambushRetry, out _, out _,
                range: new CampaignSpawnRange(350, 500), deploymentRoles: ambushRoles,
                allowOpenFallback: false) && ambush.SequenceEqual(ambushRetry),
            "one retry identity yields the same ambush layout");
        var player = anchors[0];
        var playerForward = Vector2.Normalize(player.Forward);
        var playerRight = new Vector2(playerForward.Y, -playerForward.X);
        for (var index = 0; index < 3; index++)
            Check(Vector2.Dot(ambush[index] - player.Position, playerForward) > 100f,
                "medium/heavy centre group is placed ahead of the player's spawn");
        var flankSides = new List<float>();
        for (var index = 3; index < 5; index++)
        {
            var delta = ambush[index] - player.Position;
            var ahead = Vector2.Dot(delta, playerForward);
            var lateral = Vector2.Dot(delta, playerRight);
            var bearing = MathF.Atan2(MathF.Abs(lateral), ahead) * 180f / MathF.PI;
            Check(MathF.Abs(ahead) <= .01f,
                $"ambush flank local Y must be exactly zero, actual {ahead:G6}");
            Check(MathF.Abs(bearing - 90f) <= .01f,
                $"recon flank must sit exactly at 3/9 o'clock, actual {bearing:N3}");
            flankSides.Add(MathF.Sign(lateral));
        }
        Check(flankSides.Distinct().Count() == 2,
            "recon elements are placed on both sides, not a single lateral group");
        var exactOffsets = CampaignGroupDoctrine.AmbushOffsets(ambushRoles, 400f);
        Check(exactOffsets[3].Y == 0f && exactOffsets[4].Y == 0f
            && MathF.Sign(exactOffsets[3].X) != MathF.Sign(exactOffsets[4].X),
            "ambush offsets use exact side axes with opposite X signs");
        Check(MathF.Abs(exactOffsets[3].X - exactOffsets[1].X) >= 23.99f
            && MathF.Abs(exactOffsets[4].X - exactOffsets[2].X) >= 23.99f,
            "repeated ambush side offsets keep at least 24 m along the clock axis");

        // Rotated player heading, extra anchors that are offset and deliberately
        // misaligned (one opposite, one perpendicular), plus repeated flank pairs.
        var rotated = Vector2.Normalize(new Vector2(MathF.Sin(.73f), MathF.Cos(.73f)));
        var rotatedPlayer = new SpawnArcAnchor(new Vector2(120f, -80f), rotated);
        var misalignedAnchors = new[]
        {
            rotatedPlayer,
            new SpawnArcAnchor(rotatedPlayer.Position + new Vector2(24f, -18f), -rotated),
            new SpawnArcAnchor(rotatedPlayer.Position + new Vector2(-18f, 28f),
                new Vector2(rotated.Y, -rotated.X))
        };
        var repeatedRoles = new[] { "Center", "Flank", "Flank", "Flank", "Flank", "Support" };
        var rotatedLayouts = new List<Vector2[]>();
        foreach (var identity in new[] { "ambush-rotated-a", "ambush-rotated-b", "ambush-rotated-a" })
        {
            Check(CampaignSpecialSpawnPlanner.TryPlan(misalignedAnchors, repeatedRoles.Length, identity,
                    "Ambush", (_, _) => true, (_, _) => true, out var rotatedAmbush, out var rotatedCover,
                    out var rotatedReport, range: new CampaignSpawnRange(350, 500),
                    deploymentRoles: repeatedRoles, allowOpenFallback: false),
                "rotated/misaligned ambush placement failed: " + rotatedReport);
            Check(rotatedCover, "ambush still prefers concealed placement");
            Check(rotatedAmbush.Length == repeatedRoles.Length, "ambush keeps the complete formation");
            for (var i = 0; i < rotatedAmbush.Length; i++)
            {
                var delta = rotatedAmbush[i] - rotatedPlayer.Position;
                var ahead = Vector2.Dot(delta, rotated);
                foreach (var anchor in misalignedAnchors)
                {
                    var range = Vector2.Distance(rotatedAmbush[i], anchor.Position);
                    Check(range >= 349.99f && range <= 500.01f,
                        "ambush retains every friendly anchor's safe range");
                }
                for (var j = 0; j < i; j++)
                    Check(Vector2.Distance(rotatedAmbush[i], rotatedAmbush[j]) >= 23.99f,
                        "repeated ambush side pairs keep native spacing");
                if (repeatedRoles[i] == "Flank")
                    Check(MathF.Abs(ahead) <= .01f,
                        "rotated ambush flank must stay on the player's exact side axis");
            }
            var left = new List<float>();
            var rightSide = new List<float>();
            for (var i = 0; i < rotatedAmbush.Length; i++)
                if (repeatedRoles[i] == "Flank")
                {
                    var lateral = Vector2.Dot(rotatedAmbush[i] - rotatedPlayer.Position,
                        new Vector2(rotated.Y, -rotated.X));
                    (lateral < 0 ? left : rightSide).Add(MathF.Abs(lateral));
                }
            Check(left.Count == 2 && rightSide.Count == 2,
                "two repeated recon pairs occupy both side axes");
            left.Sort(); rightSide.Sort();
            Check(left[1] - left[0] >= 23.99f && rightSide[1] - rightSide[0] >= 23.99f,
                "repeated side pairs are spaced at least 24 m along the same clock axis");
            rotatedLayouts.Add(rotatedAmbush);
        }
        Check(rotatedLayouts[0].SequenceEqual(rotatedLayouts[2]),
            "same ambush identity keeps deterministic placement");
        Check(!rotatedLayouts[0].SequenceEqual(rotatedLayouts[1]),
            "different ambush identities vary the deterministic layout");
        var compactRoles = new[] { "Center", "Support" };
        Check(CampaignSpecialSpawnPlanner.TryPlan(anchors, 2, "ambush-compact", "Ambush",
                (_, _) => true, (_, _) => true, out var compactAmbush, out _,
                out var compactReport, range: new CampaignSpawnRange(24, 160),
                deploymentRoles: compactRoles, allowOpenFallback: false,
                terrainSamplesPerPoint: 9) && compactAmbush.Length == 2,
            "compact-ranged ambush with support clears the 24 m spacing gate: " + compactReport);
        Check(Vector2.Distance(compactAmbush[0], compactAmbush[1]) >= 24f,
            "ambush support keeps absolute depth behind the centre at compact ranges");
        Check(CampaignSpecialSpawnPlanner.TryPlan(anchors, 5, "probe-accounting", "CombinedArms",
                (_, _) => true, (_, _) => true, out _, out _, out var accountingReport,
                range: new CampaignSpawnRange(350, 500), deploymentRoles: roles,
                allowOpenFallback: false, terrainSamplesPerPoint: 9)
            && accountingReport.Contains("terrainProbes=45", StringComparison.Ordinal),
            "planner diagnostics account for every footprint terrain sample: " + accountingReport);

        var friendlies = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0) };
        var enemy = new Vector3(100, 0, 0);
        Check(!CampaignSpawnCover.Occluded(friendlies, enemy, (from, _) => from.Y < 6f),
            "low hull cover is rejected when the camera-height ray is visible");
        Check(CampaignSpawnCover.Occluded(friendlies, enemy, (_, _) => true),
            "a fully concealed group passes every camera and hull-height ray");
        Check(!CampaignSpawnCover.Occluded(friendlies, enemy, (from, _) => from.X < 5f),
            "every friendly observer must be concealed for group cover");

        CampaignSpawnSettleSampler.Reset();
        var t = 0f;
        Check(CampaignSpawnSettleSampler.Observe(t, 7, "Leopard", Vector3.Zero,
            Vector3.UnitY, 0.2f, out var first), "first settle sample is captured");
        Check(first.Design == "Leopard" && first.Speed == 0.2f,
            "settle sample carries design, speed and native up reference");
        Check(!CampaignSpawnSettleSampler.Observe(t + .1f, 7, "Leopard", Vector3.Zero,
            Vector3.UnitY, 0.2f, out _), "samples are rate-limited");
        for (var i = 1; i <= 3; i++)
            Check(CampaignSpawnSettleSampler.Observe(t + i * .75f, 7, "Leopard", Vector3.Zero,
                Vector3.UnitY, 0.2f, out _), "bounded settle sample " + i);
        Check(!CampaignSpawnSettleSampler.Observe(t + 3f, 7, "Leopard", Vector3.Zero,
            Vector3.UnitY, 0.2f, out _), "the four-sample cap rejects a further in-window sample");
        Check(CampaignSpawnSettleSampler.Samples.Count == 4,
            "each owned hull is bounded to four samples");
        Check(CampaignSpawnSettleSampler.Observe(t, 8, "Light", Vector3.Zero,
            Vector3.UnitY, 0f, out _), "a separate hull starts its own bounded window");
        Check(!CampaignSpawnSettleSampler.Observe(t + 3.1f, 8, "Light", Vector3.Zero,
            Vector3.UnitY, 0f, out _), "sampling stops after the settle window");
        CampaignSpawnSettleSampler.Reset();
        Check(CampaignSpawnSettleSampler.Samples.Count == 0, "scene reset clears settle samples");

        Console.WriteLine("PASS broad spawn support, covered-only group search, exact player-relative ambush, camera-height cover and bounded settle diagnostics");
    }
}
