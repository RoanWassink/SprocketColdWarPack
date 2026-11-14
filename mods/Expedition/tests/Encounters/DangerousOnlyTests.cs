using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

// 0.3.7: catalogue designs with MinimumRisk = Dangerous may only appear in a
// Dangerous plan, never as boss, reward or in an older rules version.
public static class DangerousOnlyTests
{
    private const string Version = "fixture.dangerous.1";
    private const string Unit = "fixture.dangerous.unit.v1";
    private const string DangerId = "fixture.dangerous.era.0.danger";

    private static readonly EraReference Era0 = new("fixture.dangerous.era.0");
    private static readonly EraReference Era1 = new("fixture.dangerous.era.1");

    private static readonly CatalogueOptions Options =
        new(ContentQualification.Fixture, false, true);

    public static void DangerousOnlyAppearsInDangerousPlansOnly()
    {
        var definition = Definition(withDangerous: true);
        var usedByDangerous = false;

        for (var round = 1; round <= 12; round++)
        {
            var request = Request(round);
            var plans = new SeededEncounterCatalogue(definition, Options).GenerateOffers(request);
            foreach (var plan in plans)
            {
                var usesDangerous = plan.Enemies.Any(enemy => IsDangerous(definition, enemy.BlueprintId));
                if (plan.Risk == EncounterRisk.Dangerous)
                {
                    usedByDangerous |= usesDangerous;
                }
                else
                {
                    TestAssert.False(usesDangerous,
                        $"round {round} {plan.Risk} plan may not use a dangerous-only design");
                }

                var extras = TestAssert.NotNull(plan.Extras, "every plan has extras");
                var reward = TestAssert.NotNull(extras.Reward, "every plan has a reward");
                TestAssert.False(reward.BlueprintId != null && IsDangerous(definition, reward.BlueprintId),
                    $"round {round} {plan.Risk} reward may not use a dangerous-only design");
                if (extras.BossBlueprintId != null)
                {
                    TestAssert.False(IsDangerous(definition, extras.BossBlueprintId),
                        $"round {round} boss may not be dangerous-only");
                }
            }
        }

        TestAssert.True(usedByDangerous, "at least one Dangerous plan must use the dangerous design");
    }

    public static void CautiousAndStandardAreUnchangedByTheDangerousDesign()
    {
        var baseline = Definition(withDangerous: false);
        var extended = Definition(withDangerous: true);
        var request = Request(round: 3, seed: 424242L);

        var before = new SeededEncounterCatalogue(baseline, Options).GenerateOffers(request);
        var after = new SeededEncounterCatalogue(extended, Options).GenerateOffers(request);
        var baselineHash = CatalogueHasher.Compute(baseline);

        foreach (var risk in new[] { EncounterRisk.Cautious, EncounterRisk.Standard })
        {
            var a = before.Single(plan => plan.Risk == risk);
            var b = after.Single(plan => plan.Risk == risk);
            TestAssert.Equal(a.MapId, b.MapId, $"{risk}: map");
            TestAssert.Equal(a.SpawnLayoutId, b.SpawnLayoutId, $"{risk}: layout");
            TestAssert.Equal(Roster(a), Roster(b), $"{risk}: roster");
            TestAssert.Equal(a.SupplyReward, b.SupplyReward, $"{risk}: supply");
            TestAssert.Equal(a.Extras?.EnemyBudgetTarget, b.Extras?.EnemyBudgetTarget,
                $"{risk}: pinned target");
            TestAssert.Equal(JsonSerializer.Serialize(a.Extras), JsonSerializer.Serialize(b.Extras),
                $"{risk}: extras");

            // The only permitted difference is the catalogue identity that now
            // includes the minimum-risk design. Rehashing the extended plan with
            // the baseline catalogue hash must reproduce the baseline offer bytes.
            var draft = b with { OfferId = string.Empty, PlanHash = string.Empty };
            var offerId = PlanHasher.CreateOfferId(request.RunId, baselineHash, draft);
            var identified = draft with { OfferId = offerId };
            TestAssert.Equal(a.OfferId, offerId, $"{risk}: offer id from the baseline catalogue");
            TestAssert.Equal(a.PlanHash, PlanHasher.ComputePlanHash(baselineHash, identified),
                $"{risk}: plan hash from the baseline catalogue");
        }

        var neutral = extended with
        {
            Eras = extended.Eras.Select(era => era with
            {
                Enemies = era.Enemies.Select(enemy => enemy.MinimumRisk == null
                    ? enemy : enemy with { MinimumRisk = null }).ToArray()
            }).ToArray()
        };
        var neutralText = JsonSerializer.Serialize(neutral);
        TestAssert.False(neutralText.Contains("MinimumRisk", StringComparison.Ordinal),
            "a null MinimumRisk is not written");
        var neutralRoundTrip = JsonSerializer.Deserialize<CatalogueDefinition>(neutralText)!;
        var neutralHash = CatalogueHasher.Compute(neutral);
        TestAssert.Equal(neutralHash, CatalogueHasher.Compute(neutralRoundTrip),
            "an older catalogue without the field hashes exactly like its null copy");
        TestAssert.NotEqual(neutralHash, CatalogueHasher.Compute(extended),
            "the catalogue hash must change when a design carries MinimumRisk");
    }

    public static void OlderRulesIgnoreMinimumRisk()
    {
        var flagged = Definition(withDangerous: true);
        var neutral = flagged with
        {
            Eras = flagged.Eras.Select(era => era with
            {
                Enemies = era.Enemies.Select(enemy => enemy.MinimumRisk == null
                    ? enemy : enemy with { MinimumRisk = null }).ToArray()
            }).ToArray()
        };

        foreach (var round in new[] { 1, 2, 3, 5 })
        {
            var request = Request(round, rulesVersion: 5);
            var a = new SeededEncounterCatalogue(flagged, Options).GenerateOffers(request);
            var b = new SeededEncounterCatalogue(neutral, Options).GenerateOffers(request);
            foreach (var risk in Enum.GetValues<EncounterRisk>())
            {
                var left = a.Single(plan => plan.Risk == risk);
                var right = b.Single(plan => plan.Risk == risk);
                TestAssert.Equal(left.MapId, right.MapId, $"rules 5 round {round} {risk}: map");
                TestAssert.Equal(left.SpawnLayoutId, right.SpawnLayoutId,
                    $"rules 5 round {round} {risk}: layout");
                TestAssert.Equal(Roster(left), Roster(right),
                    $"rules 5 round {round} {risk}: roster ignores MinimumRisk");
                TestAssert.Equal(JsonSerializer.Serialize(left.Extras),
                    JsonSerializer.Serialize(right.Extras),
                    $"rules 5 round {round} {risk}: extras ignore MinimumRisk");
            }
        }
    }

    public static void BossSelectionNeverReturnsDangerousOnly()
    {
        var definition = Definition(withDangerous: true);
        for (var era = 0; era < definition.Eras.Count; era++)
        {
            var boss = BossSelection.Select(definition, era, ContentQualification.Fixture);
            TestAssert.False(IsDangerous(definition, boss.Blueprint.LocalId),
                $"era {era} boss may not be dangerous-only");
        }
    }

    public static void DuplicateMarkerPrefersTheDangerousFolder()
    {
        var scan = DangerousBlueprintFolders.Resolve(
            new[] { "bb", "aa" }, new[] { "cc", "bb" });
        TestAssert.Equal("aa", string.Join(",", scan.CommunityOnly), "normal-only markers");
        TestAssert.Equal("bb,cc", string.Join(",", scan.Dangerous), "dangerous markers");
        TestAssert.Equal("bb", string.Join(",", scan.Duplicates), "one duplicate warning");
    }

    public static void CatalogueJsonWithoutMinimumRiskRoundTripsByteIdentically()
    {
        var definition = CatalogueJson.Load(OldJson);
        var once = JsonSerializer.Serialize(definition);
        TestAssert.False(once.Contains("MinimumRisk", StringComparison.Ordinal),
            "absent MinimumRisk must not serialize");
        var twice = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<CatalogueDefinition>(once)!);
        TestAssert.Equal(once, twice, "read/write must reproduce the old bytes exactly");

        var flagged = definition with
        {
            Eras = definition.Eras.Select(era => era with
            {
                Enemies = era.Enemies.Select(enemy => enemy with
                {
                    MinimumRisk = EncounterRisk.Dangerous
                }).ToArray()
            }).ToArray()
        };
        var withField = JsonSerializer.Serialize(flagged);
        TestAssert.Contains("MinimumRisk", withField);
        TestAssert.Equal(withField, JsonSerializer.Serialize(
            JsonSerializer.Deserialize<CatalogueDefinition>(withField)!),
            "flagged read/write is stable");
    }

    public static void CatalogueJsonAcceptsMinimumRisk()
    {
        var definition = CatalogueJson.Load(OldJson.Replace(
            "\"massKg\": 1000", "\"massKg\": 1000, \"minimumRisk\": \"Dangerous\""));
        TestAssert.Equal<EncounterRisk?>(EncounterRisk.Dangerous,
            definition.Eras[0].Enemies[0].MinimumRisk, "minimumRisk is parsed");
    }

    private static bool IsDangerous(CatalogueDefinition definition, string id) =>
        definition.Eras.SelectMany(era => era.Enemies)
            .Any(enemy => enemy.Blueprint.LocalId == id
                && DangerousDesignPolicy.IsDangerousOnly(enemy));

    private static string Roster(EncounterPlan plan) =>
        string.Join(",", plan.Enemies.OrderBy(enemy => enemy.BlueprintId, StringComparer.Ordinal)
            .Select(enemy => $"{enemy.BlueprintId}x{enemy.Count}"));

    private static OfferRequest Request(int round, long seed = 9917L, int rulesVersion = 6) =>
        new("danger-run", seed, round, (round - 1) / 4, Era0,
            new ScenarioValue(600m, Unit), 0, Version)
        {
            RulesVersion = rulesVersion,
            EnemyBudgetMultiplier = 1m,
            EraProgressionEnabled = false
        };

    private static CatalogueDefinition Definition(bool withDangerous)
    {
        return new CatalogueDefinition(Version, CatalogueDefinition.FixtureLabel, true, Unit,
            new[] { BuildEra(Era0, withDangerous), BuildEra(Era1, withDangerous) });
    }

    private static CatalogueEraContent BuildEra(EraReference era, bool withDangerous)
    {
        var suffix = era.NativeId.EndsWith(".0", StringComparison.Ordinal) ? "a" : "b";
        var maps = Enumerable.Range(0, 3).Select(index => new MapEntry(era,
            $"fixture.dangerous.map.{suffix}.{index}",
            TestCatalogueFactory.Hash(7100 + suffix[0] * 10 + index),
            new[] { $"fixture.dangerous.layout.{suffix}.{index}" },
            Array.Empty<ModRequirement>(), ContentQualification.Fixture)).ToArray();
        var enemies = new List<EnemyDesignEntry>
        {
            Design(era, suffix, "line", EnemyRole.Line, 120m, "Line"),
            Design(era, suffix, "heavy", EnemyRole.Heavy, 200m, "Heavy"),
            Design(era, suffix, "recon", EnemyRole.Recon, 80m, "Recon"),
            Design(era, suffix, "support", EnemyRole.Support, 100m, "Support")
        };
        if (withDangerous)
            enemies.Add(Design(era, suffix, "danger", EnemyRole.Line, 400m, "Danger")
                with { MinimumRisk = EncounterRisk.Dangerous });
        return new CatalogueEraContent(era, maps, enemies);
    }

    private static EnemyDesignEntry Design(EraReference era, string suffix, string name,
        EnemyRole role, decimal price, string family) =>
        new(new BlueprintReference($"fixture.dangerous.era.{suffix}.{name}",
                TestCatalogueFactory.Hash((int)(price * 17) + suffix[0]),
                era, Array.Empty<ModRequirement>()),
            role, price, new ScenarioValue(price, Unit), ContentQualification.Fixture)
        {
            FormationFamily = family
        };

    private const string OldJson = @"
{
  ""label"": ""TEST FIXTURE NOT PLAYABLE"",
  ""fixture"": true,
  ""version"": ""fixture.dangerous.json.1"",
  ""priceUnitId"": ""fixture.dangerous.unit.v1"",
  ""eras"": [
    {
      ""era"": ""fixture.dangerous.era.0"",
      ""maps"": [
        {
          ""mapId"": ""fixture.dangerous.map.0"",
          ""contentHash"": ""0000000000000000000000000000000000000000000000000000000000000001"",
          ""layouts"": [""fixture.dangerous.layout.0""],
          ""requiredMods"": [],
          ""qualification"": ""Fixture""
        }
      ],
      ""enemies"": [
        {
          ""blueprintId"": ""fixture.dangerous.era.a.line"",
          ""snapshotHash"": ""0000000000000000000000000000000000000000000000000000000000000002"",
          ""role"": ""Line"",
          ""threatScore"": 20,
          ""checkedPrice"": 20,
          ""requiredMods"": [],
          ""qualification"": ""Fixture"",
          ""formationFamily"": ""Line"",
          ""massKg"": 1000
        }
      ]
    }
  ]
}";
}
