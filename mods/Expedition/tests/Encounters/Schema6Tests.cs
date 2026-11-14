using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

public static class Schema6Tests
{
    private const string Unit = TestCatalogueFactory.PriceUnit;

    public static void V6CurveRisesInsideEveryEraAndStartEra()
    {
        var definition = EightEraDefinition();
        var options = CatalogueOptions.Fixture;

        foreach (var startEra in Enumerable.Range(0, 8))
        {
            for (var era = startEra; era < 8; era++)
            {
                var ratios = new List<decimal>();
                for (var roundInEra = 1; roundInEra <= 4; roundInEra++)
                {
                    var round = era * 4 + roundInEra;
                    var budget = 1_000m + era * 250m;
                    var request = new OfferRequest("v6-curve", 9917L, round, era,
                        definition.Eras[era].Era, new ScenarioValue(budget, Unit), 0, definition.Version)
                    {
                        RulesVersion = 6,
                        EnemyBudgetMultiplier = 1m,
                        EraProgressionEnabled = false
                    };
                    var plans = new SeededEncounterCatalogue(definition, options).GenerateOffers(request);
                    var standard = plans.Single(plan => plan.Risk == EncounterRisk.Standard);
                    TestAssert.True(standard.Extras?.EnemyBudgetTarget is not null,
                        "schema 6 pins a target on every card");
                    var target = standard.Extras!.EnemyBudgetTarget!.Value;
                    var expected = decimal.Floor(budget
                        * EncounterRules.WithinBlockMultiplierV6(roundInEra)
                        * (roundInEra == 4 ? BossSelection.Schema6EnemyFactor : 1m));
                    TestAssert.Equal(expected, target,
                        $"era {era}, start {startEra}, fight {roundInEra}");
                    ratios.Add(target / budget);
                }

                TestAssert.True(ratios[0] < ratios[1] && ratios[1] < ratios[2],
                    $"fight 1<2<3 in era {era}: {string.Join(",", ratios)}");
                TestAssert.True(ratios[3] > ratios[2], "boss target exceeds fight 3");
                TestAssert.Equal(1.15m, EncounterRules.WithinBlockMultiplierV6(4),
                    "the fourth fight keeps the explicit boss factor");
            }
        }
    }

    public static void V5CurveKeepsTheLegacyThreeRoundRotation()
    {
        var definition = EightEraDefinition();
        var options = CatalogueOptions.Fixture;
        TestAssert.Equal(0.65m, EncounterRules.WithinBlockMultiplier(1));
        TestAssert.Equal(0.90m, EncounterRules.WithinBlockMultiplier(2));
        TestAssert.Equal(1.15m, EncounterRules.WithinBlockMultiplier(3));
        TestAssert.Equal(0.65m, EncounterRules.WithinBlockMultiplier(4));

        var seen = new List<decimal>();
        for (var era = 0; era < 8; era++)
        {
            for (var roundInEra = 1; roundInEra <= 4; roundInEra++)
            {
                var round = era * 4 + roundInEra;
                var budget = 1_000m + era * 250m;
                var request = new OfferRequest("v5-curve", 9917L, round, era,
                    definition.Eras[era].Era, new ScenarioValue(budget, Unit), 0, definition.Version)
                {
                    RulesVersion = 5,
                    EnemyBudgetMultiplier = 1m,
                    EraProgressionEnabled = false
                };
                var plans = new SeededEncounterCatalogue(definition, options).GenerateOffers(request);
                var standard = plans.Single(plan => plan.Risk == EncounterRisk.Standard);
                if (roundInEra == 4)
                {
                    TestAssert.True(standard.Extras?.EnemyBudgetTarget is null,
                        "schema 5 keeps its old boss bytes without pinned target metadata");
                    continue;
                }
                var expected = decimal.Floor(budget
                    * EncounterRules.WithinBlockMultiplier(round));
                TestAssert.Equal(expected, standard.Extras!.EnemyBudgetTarget,
                    $"legacy era {era}, round {roundInEra}");
                if (era == 0 && roundInEra <= 3) seen.Add(expected / budget);
            }
        }

        TestAssert.Equal(0.65m, seen[0]);
        TestAssert.Equal(0.90m, seen[1]);
        TestAssert.Equal(1.15m, seen[2]);
    }

    public static void FinalEraNoteShowsTheRealProgression()
    {
        TestAssert.Equal("+5% per era; the final era of this run: +35%",
            DifficultyScaling.FinalEraNote(8));
        TestAssert.Equal("+5% per era; the final era of this run: +25%",
            DifficultyScaling.FinalEraNote(6));
        TestAssert.Equal("+5% per era; the final era of this run: +75%",
            DifficultyScaling.FinalEraNote(20));
        TestAssert.False(DifficultyScaling.FinalEraNote(null).Contains("final era of this run"),
            "without a schedule no final-era number is invented");
    }

    private static CatalogueDefinition EightEraDefinition()
    {
        var eras = new List<CatalogueEraContent>();
        for (var i = 0; i < 8; i++)
        {
            var era = new EraReference("fixture.schema6.era." + i);
            var maps = Enumerable.Range(0, 3).Select(m => new MapEntry(era,
                $"fixture.schema6.{i}.map.{m}",
                TestCatalogueFactory.Hash(5000 + i * 10 + m),
                new[] { $"fixture.schema6.{i}.layout.{m}" },
                Array.Empty<ModRequirement>(), ContentQualification.Fixture)).ToArray();
            var designs = new (string Id, EnemyRole Role, decimal Price)[]
            {
                ($"fixture.schema6.{i}.line", EnemyRole.Line, 100m + i * 20m),
                ($"fixture.schema6.{i}.heavy", EnemyRole.Heavy, 140m + i * 20m),
                ($"fixture.schema6.{i}.recon", EnemyRole.Recon, 50m + i * 10m),
                ($"fixture.schema6.{i}.support", EnemyRole.Support, 70m + i * 10m)
            };
            var enemies = designs.Select(d => new EnemyDesignEntry(
                new BlueprintReference(d.Id, TestCatalogueFactory.Hash(6000 + i * 10 + (int)d.Role),
                    era, Array.Empty<ModRequirement>()),
                d.Role, d.Price, new ScenarioValue(d.Price, Unit),
                ContentQualification.Fixture)
            {
                FormationFamily = "Fixture " + d.Role
            }).ToArray();
            eras.Add(new CatalogueEraContent(era, maps, enemies));
        }

        return new CatalogueDefinition("fixture.schema6.1", CatalogueDefinition.FixtureLabel,
            true, Unit, eras);
    }
}
