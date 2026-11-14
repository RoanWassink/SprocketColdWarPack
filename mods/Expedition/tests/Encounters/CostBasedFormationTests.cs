using System.Globalization;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

// Cost-mode tests use only synthetic single-era definitions. Every fixture
// design sets ThreatScore equal to its CheckedPrice so the catalogue is a valid
// cost profile; the legacy example intentionally keeps them different.
public static class CostBasedFormationTests
{
    private const string Version = "fixture.cost.catalogue.1";
    private const string Unit = "fixture.cost.scenario.unit.v1";

    private static readonly CatalogueOptions CostOptions =
        new(ContentQualification.Fixture, false, true);

    private static readonly CatalogueOptions LegacyOptions =
        new(ContentQualification.Fixture, false, false);

    private static readonly EraReference EraA = new("fixture.cost.era.a");
    private static readonly EraReference EraB = new("fixture.cost.era.b");

    public static void PriceNotCountDeterminesFormationCost()
    {
        var definition = Definition(
            BuildEra(
                EraA, "a", 1, 3,
                new EnemySpec("cheap", 1m),
                new EnemySpec("expensive", 50m)));
        var catalogue = new SeededEncounterCatalogue(definition, CostOptions);
        var request = Request(EraA, budget: 70m, round: 1);

        var result = catalogue.TryGenerateOffers(request);
        TestAssert.True(
            result.IsSuccess,
            $"expected cost generation success, got {result.Status}");

        var cautious = Plan(result.Offers, EncounterRisk.Cautious);
        var standard = Plan(result.Offers, EncounterRisk.Standard);
        var dangerous = Plan(result.Offers, EncounterRisk.Dangerous);

        decimal cautiousCost = Cost(definition, cautious);
        decimal standardCost = Cost(definition, standard);
        decimal dangerousCost = Cost(definition, dangerous);

        TestAssert.True(
            cautiousCost < standardCost && standardCost < dangerousCost,
            "actual total costs must be strictly increasing");
        TestAssert.True(
            dangerousCost > cautiousCost,
            "one high-price unit must outweigh many low-price units");
        TestAssert.True(
            Count(dangerous) < Count(cautious),
            "price-based strength must not be raw count");
        TestAssert.Equal(52m, dangerousCost, "dangerous cost = 50 + 1 + 1");
    }

    public static void ActualTotalsAreStrictlyOrderedAcrossSeedsAndRounds()
    {
        var definition = Definition(
            BuildEra(
                EraA, "a", 1, 3,
                new EnemySpec("p5", 5m),
                new EnemySpec("p12", 12m),
                new EnemySpec("p30", 30m),
                new EnemySpec("p47", 47m)));
        var catalogue = new SeededEncounterCatalogue(definition, CostOptions);

        foreach (long seed in new long[] { 1, 2, 3, 7, 13, 21, 34, 55, 89, 144 })
        {
            foreach (int round in new[] { 1, 2, 3, 4, 7, 13 })
            {
                var request = Request(
                    EraA,
                    budget: 400m,
                    round: round,
                    seed: seed,
                    runId: $"cost-order-{seed}-{round}");

                var result = catalogue.TryGenerateOffers(request);
                TestAssert.True(
                    result.IsSuccess,
                    $"seed {seed} round {round}: expected success, got {result.Status}");

                var cautious = Plan(result.Offers, EncounterRisk.Cautious);
                var standard = Plan(result.Offers, EncounterRisk.Standard);
                var dangerous = Plan(result.Offers, EncounterRisk.Dangerous);

                decimal cautiousCost = Cost(definition, cautious);
                decimal standardCost = Cost(definition, standard);
                decimal dangerousCost = Cost(definition, dangerous);

                TestAssert.True(
                    cautiousCost < standardCost,
                    $"seed {seed} round {round}: cautious {cautiousCost} must be below standard {standardCost}");
                TestAssert.True(
                    standardCost < dangerousCost,
                    $"seed {seed} round {round}: standard {standardCost} must be below dangerous {dangerousCost}");
                TestAssert.True(
                    cautiousCost <= CostTarget(
                        definition, request, EncounterRisk.Cautious),
                    "cautious formation must be affordable");
                TestAssert.True(
                    standardCost <= CostTarget(
                        definition, request, EncounterRisk.Standard),
                    "standard formation must be affordable");
                TestAssert.True(
                    dangerousCost <= CostTarget(
                        definition, request, EncounterRisk.Dangerous),
                    "dangerous formation must be affordable");

                foreach (var plan in result.Offers)
                {
                    TestAssert.InRange(1, 12, Count(plan));
                    TestAssert.True(
                        Cost(definition, plan) > 0m,
                        "formation cost must be positive");
                }
            }
        }
    }

    public static void DecimalCostsAreDeterministicForSameSeed()
    {
        var definition = Definition(
            BuildEra(EraA, "decimal", 1, 3, new EnemySpec("p75", 7.5m)));

        var first = new SeededEncounterCatalogue(definition, CostOptions)
            .TryGenerateOffers(Request(EraA, budget: 300m, round: 3, seed: 42));
        var second = new SeededEncounterCatalogue(definition, CostOptions)
            .TryGenerateOffers(Request(EraA, budget: 300m, round: 3, seed: 42));

        TestAssert.True(first.IsSuccess, $"first generation failed: {first.Status}");
        TestAssert.True(second.IsSuccess, $"second generation failed: {second.Status}");
        TestAssert.Equal(
            Signature(first.Offers),
            Signature(second.Offers),
            "same seed must reproduce the exact offer set");

        var cautious = Plan(first.Offers, EncounterRisk.Cautious);
        var standard = Plan(first.Offers, EncounterRisk.Standard);
        var dangerous = Plan(first.Offers, EncounterRisk.Dangerous);

        TestAssert.Equal(75m, Cost(definition, cautious));
        TestAssert.Equal(82.5m, Cost(definition, standard));
        TestAssert.Equal(90m, Cost(definition, dangerous));
        TestAssert.True(
            Cost(definition, standard) != decimal.Truncate(
                Cost(definition, standard)),
            "decimal price arithmetic must remain fractional");
    }

    public static void CostModeAllowsOneRealMapAndOneDesign()
    {
        var definition = Definition(
            BuildEra(EraA, "small", 1, 1, new EnemySpec("only", 25m)));
        var catalogue = new SeededEncounterCatalogue(definition, CostOptions);
        var request = Request(EraA, budget: 300m, round: 3);

        var result = catalogue.TryGenerateOffers(request);
        TestAssert.True(
            result.IsSuccess,
            $"one-map cost profile must generate, got {result.Status}");
        TestAssert.Equal(3, result.Offers.Count);

        var cautious = Plan(result.Offers, EncounterRisk.Cautious);
        var standard = Plan(result.Offers, EncounterRisk.Standard);
        var dangerous = Plan(result.Offers, EncounterRisk.Dangerous);

        TestAssert.Equal(
            "fixture.cost.map.small.1", cautious.MapId);
        TestAssert.True(
            result.Offers.All(plan => plan.MapId == "fixture.cost.map.small.1"),
            "the one real map must be used for every offer");
        TestAssert.True(
            result.Offers.All(plan => plan.Enemies.All(
                enemy => enemy.BlueprintId
                    == "fixture.cost.blueprint.small.only")),
            "the one real design must be used for every offer");

        TestAssert.Equal(10, Count(cautious));
        TestAssert.Equal(11, Count(standard));
        TestAssert.Equal(12, Count(dangerous));
        TestAssert.Equal(250m, Cost(definition, cautious));
        TestAssert.Equal(275m, Cost(definition, standard));
        TestAssert.Equal(300m, Cost(definition, dangerous));
    }

    public static void WrongEraIsNeverChosen()
    {
        var definition = Definition(
            BuildEra(
                EraA, "a", 1, 2,
                new EnemySpec("a1", 6m),
                new EnemySpec("a2", 19m)),
            BuildEra(
                EraB, "b", 2, 2,
                new EnemySpec("b1", 3m),
                new EnemySpec("b2", 31m)));
        var catalogue = new SeededEncounterCatalogue(definition, CostOptions);

        var result = catalogue.TryGenerateOffers(
            Request(EraA, budget: 300m, round: 3, seed: 5));
        TestAssert.True(
            result.IsSuccess,
            $"expected era-a generation success, got {result.Status}");

        var eraA = TestCatalogueFactory.Era(definition, EraA);
        foreach (var plan in result.Offers)
        {
            TestAssert.Equal(EraA.NativeId, plan.Era.NativeId);
            TestAssert.True(
                plan.MapId.StartsWith("fixture.cost.map.a.", StringComparison.Ordinal),
                $"wrong-era map '{plan.MapId}'");
            foreach (var enemy in plan.Enemies)
            {
                TestAssert.True(
                    eraA.Enemies.Any(design => design.Blueprint.LocalId
                        == enemy.BlueprintId),
                    $"wrong-era blueprint '{enemy.BlueprintId}'");
            }
        }
    }

    public static void InvalidScoreAndPriceFailConstruction()
    {
        var mismatch = Definition(
            BuildEra(
                EraA, "mismatch", 1, 1,
                new EnemySpec("mismatch", 10m, Threat: 9m)));
        var mismatchError = TestAssert.Throws<EncounterAvailabilityException>(
            () => new SeededEncounterCatalogue(mismatch, CostOptions));
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidContent, mismatchError.Status);
        TestAssert.ContainsError(
            mismatchError.Diagnostics, "does not match CheckedPrice.Value");

        var unmatchedUnit = Definition(
            BuildEra(
                EraA, "unmatched", 2, 1,
                new EnemySpec(
                    "unmatched", 10m, UnitId: "fixture.cost.other.unit")));
        var unitError = TestAssert.Throws<EncounterAvailabilityException>(
            () => new SeededEncounterCatalogue(unmatchedUnit, CostOptions));
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidContent, unitError.Status);
        TestAssert.ContainsError(
            unitError.Diagnostics, "checked price unit");
    }

    public static void FormationCapIsTwelveUnits()
    {
        var definition = Definition(
            BuildEra(EraA, "cap", 1, 3, new EnemySpec("p2", 2m)));
        var catalogue = new SeededEncounterCatalogue(definition, CostOptions);

        var result = catalogue.TryGenerateOffers(
            Request(EraA, budget: 1000m, round: 3, seed: 8));
        TestAssert.True(result.IsSuccess, $"expected success, got {result.Status}");

        var dangerous = Plan(result.Offers, EncounterRisk.Dangerous);
        TestAssert.Equal(12, Count(dangerous));
        TestAssert.Equal(24m, Cost(definition, dangerous));
        TestAssert.True(
            result.Offers.All(plan => Count(plan) <= 12),
            "no formation may exceed the 12-unit cap");
        TestAssert.True(
            result.Offers.All(plan => plan.Enemies.All(enemy => enemy.Count > 0)),
            "no zero-count unit may be persisted");
    }

    public static void SuccessfulOffersArePositiveAndAffordable()
    {
        var definition = Definition(
            BuildEra(
                EraA, "affordable", 1, 2,
                new EnemySpec("p3", 3m),
                new EnemySpec("p8", 8m)));
        var catalogue = new SeededEncounterCatalogue(definition, CostOptions);

        foreach (long seed in new long[] { 1, 4, 9, 16 })
        {
            var request = Request(
                EraA, budget: 100m, round: 1, seed: seed,
                runId: $"cost-affordable-{seed}");
            var result = catalogue.TryGenerateOffers(request);
            TestAssert.True(
                result.IsSuccess,
                $"seed {seed}: expected success, got {result.Status}");

            foreach (var plan in result.Offers)
            {
                decimal cost = Cost(definition, plan);
                TestAssert.True(cost > 0m, "cost must be positive");
                TestAssert.True(
                    cost <= CostTarget(definition, request, plan.Risk),
                    $"{plan.Risk} cost {cost} exceeds target");
                TestAssert.True(
                    plan.Enemies.Count > 0
                        && plan.Enemies.All(enemy => enemy.Count > 0),
                    "formation must contain positive real units");
            }
        }
    }

    public static void SinglePriceEraOffersOneTwoThreeCopies()
    {
        var definition = Definition(
            BuildEra(EraA, "single", 1, 1, new EnemySpec("only", 100m)));
        var catalogue = new SeededEncounterCatalogue(definition, CostOptions);
        var request = Request(EraA, budget: 10m, round: 1);
        var era = TestCatalogueFactory.Era(definition, EraA);

        // Authored targets are below the only real price; the explicit discrete
        // floor raises them to one, two and three copies of that price.
        TestAssert.Equal(
            100m,
            EncounterRules.CostThreatTarget(
                request.AvailableBudget,
                request.Round,
                EncounterRisk.Cautious,
                era.Enemies));
        TestAssert.Equal(
            200m,
            EncounterRules.CostThreatTarget(
                request.AvailableBudget,
                request.Round,
                EncounterRisk.Standard,
                era.Enemies));
        TestAssert.Equal(
            300m,
            EncounterRules.CostThreatTarget(
                request.AvailableBudget,
                request.Round,
                EncounterRisk.Dangerous,
                era.Enemies));

        var result = catalogue.TryGenerateOffers(request);
        TestAssert.True(
            result.IsSuccess,
            $"single-price era must offer three positive cards, got {result.Status}");
        TestAssert.Equal(3, result.Offers.Count);

        var cautious = Plan(result.Offers, EncounterRisk.Cautious);
        var standard = Plan(result.Offers, EncounterRisk.Standard);
        var dangerous = Plan(result.Offers, EncounterRisk.Dangerous);

        TestAssert.Equal(1, Count(cautious));
        TestAssert.Equal(2, Count(standard));
        TestAssert.Equal(3, Count(dangerous));
        TestAssert.Equal(100m, Cost(definition, cautious));
        TestAssert.Equal(200m, Cost(definition, standard));
        TestAssert.Equal(300m, Cost(definition, dangerous));
        TestAssert.True(
            result.Offers.All(plan => plan.Enemies.All(
                enemy => enemy.BlueprintId
                    == "fixture.cost.blueprint.single.only")),
            "only the real single-price design may be used");

        var restoring = new SeededEncounterCatalogue(definition, CostOptions);
        var restored = restoring.RestoreOffers(request, result.Offers);
        TestAssert.True(
            restored.IsSuccess,
            $"floor-adjusted offers must restore exactly, got {restored.Status}");
        TestAssert.Equal(
            Signature(result.Offers), Signature(restored.Offers));
    }

    public static void CostThreatTargetFloorsToDiscreteCopies()
    {
        var definition = Definition(
            BuildEra(
                EraA, "floor", 1, 1,
                new EnemySpec("p5", 5m),
                new EnemySpec("p10", 10m),
                new EnemySpec("p20", 20m),
                new EnemySpec("p1000", 1000m)));
        var era = TestCatalogueFactory.Era(definition, EraA);
        var low = Request(EraA, budget: 1m, round: 1);

        // Only the three cheapest distinct prices contribute. With 5, 10 and 20
        // the first attainable positive totals are 5, 10 and 15.
        TestAssert.Equal(
            5m,
            EncounterRules.CostThreatTarget(
                low.AvailableBudget, low.Round,
                EncounterRisk.Cautious, era.Enemies));
        TestAssert.Equal(
            10m,
            EncounterRules.CostThreatTarget(
                low.AvailableBudget, low.Round,
                EncounterRisk.Standard, era.Enemies));
        TestAssert.Equal(
            15m,
            EncounterRules.CostThreatTarget(
                low.AvailableBudget, low.Round,
                EncounterRisk.Dangerous, era.Enemies));

        // A larger authored target is retained; the floor only raises targets.
        var high = Request(EraA, budget: 1000m, round: 3);
        foreach (var risk in new[]
        {
            EncounterRisk.Cautious,
            EncounterRisk.Standard,
            EncounterRisk.Dangerous
        })
        {
            decimal authored = EncounterRules.ThreatTarget(
                high.AvailableBudget, high.Round, risk);
            decimal floored = EncounterRules.CostThreatTarget(
                high.AvailableBudget, high.Round, risk, era.Enemies);
            TestAssert.Equal(
                authored,
                floored,
                $"{risk}: authored target must win when it is already larger");
        }
    }

    public static void LegacyModeUsesAuthoredTargetNotDiscreteFloor()
    {
        var definition = Definition(
            BuildEra(
                EraA, "legacy-target", 1, 3,
                new EnemySpec("l1", 100m),
                new EnemySpec("l2", 100m),
                new EnemySpec("l3", 100m),
                new EnemySpec("l4", 100m)));
        var catalogue = new SeededEncounterCatalogue(
            definition, CatalogueOptions.Fixture);
        var request = Request(EraA, budget: 10m, round: 1);
        var era = TestCatalogueFactory.Era(definition, EraA);

        TestAssert.Equal(
            5m,
            EncounterRules.ThreatTarget(
                request.AvailableBudget,
                request.Round,
                EncounterRisk.Cautious));
        TestAssert.Equal(
            100m,
            EncounterRules.CostThreatTarget(
                request.AvailableBudget,
                request.Round,
                EncounterRisk.Cautious,
                era.Enemies));

        // Cost mode would use the 100 floor and succeed. Legacy mode must keep
        // the authored target, which cannot afford any 100-price design.
        var result = catalogue.TryGenerateOffers(request);
        TestAssert.Equal(
            EncounterAvailabilityStatus.NoValidFormation, result.Status);
        TestAssert.Equal(0, result.Offers.Count);
    }

    public static void FullEighteenRoundSixEraSweep()
    {
        decimal[] prices = { 4m, 9m, 16m, 25m, 36m, 49m };
        var contents = new CatalogueEraContent[prices.Length];
        for (int index = 0; index < prices.Length; index++)
        {
            contents[index] = BuildEra(
                new EraReference($"fixture.cost.sweep.era.{index + 1}"),
                $"sweep{index + 1}",
                100 + index,
                1,
                new EnemySpec("only", prices[index]));
        }

        var definition = Definition(contents);
        var catalogue = new SeededEncounterCatalogue(definition, CostOptions);

        foreach (var content in contents)
        {
            var design = content.Enemies.Single();
            var map = content.Maps.Single();
            for (int round = 1; round <= 18; round++)
            {
                var request = Request(
                    content.Era,
                    budget: 320m,
                    round: round,
                    seed: round * 37L,
                    runId: $"cost-sweep-{content.Era.NativeId}-{round}");
                var result = catalogue.TryGenerateOffers(request);
                TestAssert.True(
                    result.IsSuccess,
                    $"{content.Era.NativeId} round {round}: expected success, got {result.Status}");

                var cautious = Plan(result.Offers, EncounterRisk.Cautious);
                var standard = Plan(result.Offers, EncounterRisk.Standard);
                var dangerous = Plan(result.Offers, EncounterRisk.Dangerous);
                decimal cautiousCost = Cost(definition, cautious);
                decimal standardCost = Cost(definition, standard);
                decimal dangerousCost = Cost(definition, dangerous);

                TestAssert.True(
                    cautiousCost < standardCost,
                    $"{content.Era.NativeId} round {round}: cautious cost must be strictly below standard");
                TestAssert.True(
                    standardCost < dangerousCost,
                    $"{content.Era.NativeId} round {round}: standard cost must be strictly below dangerous");

                foreach (var plan in result.Offers)
                {
                    TestAssert.Equal(
                        content.Era.NativeId, plan.Era.NativeId);
                    TestAssert.Equal(map.MapId, plan.MapId);
                    TestAssert.True(
                        plan.Enemies.Count > 0
                            && plan.Enemies.All(enemy => enemy.Count > 0),
                        $"{content.Era.NativeId} round {round}: empty or zero formation");
                    TestAssert.True(
                        plan.Enemies.All(enemy => enemy.BlueprintId
                            == design.Blueprint.LocalId),
                        $"{content.Era.NativeId} round {round}: wrong-era blueprint");
                    TestAssert.InRange(1, 12, Count(plan));

                    decimal cost = Cost(definition, plan);
                    decimal bound = EncounterRules.CostThreatTarget(
                        request.AvailableBudget,
                        request.Round,
                        plan.Risk,
                        content.Enemies);
                    TestAssert.True(
                        cost > 0m && cost <= bound,
                        $"{content.Era.NativeId} round {round}: {plan.Risk} cost {cost} outside supplied bound {bound}");
                    TestAssert.True(
                        bound >= design.CheckedPrice!.Value
                            * ((int)plan.Risk + 1),
                        $"{content.Era.NativeId} round {round}: supplied bound must cover the discrete risk floor");
                }
            }
        }
    }

    public static void LegacyModeKeepsItsHistoricalGate()
    {
        var definition = Definition(
            BuildEra(EraA, "legacy", 1, 1, new EnemySpec("legacy", 9m)));
        var catalogue = new SeededEncounterCatalogue(definition, LegacyOptions);

        var result = catalogue.TryGenerateOffers(
            Request(EraA, budget: 300m, round: 3));
        TestAssert.Equal(
            EncounterAvailabilityStatus.NoQualifiedMaps, result.Status);
        TestAssert.Equal(0, result.Offers.Count);

        // The shared legacy fixture has ThreatScore independent of CheckedPrice.
        // Legacy mode must still accept it and keep its existing generation path.
        var legacyDefinition = TestCatalogueFactory.CreateExample();
        var legacyCatalogue = TestCatalogueFactory.CreateCatalogue(
            legacyDefinition);
        var legacyResult = legacyCatalogue.TryGenerateOffers(
            TestCatalogueFactory.Request(round: 3, budget: 300m));
        TestAssert.True(
            legacyResult.IsSuccess,
            $"legacy fixture generation must still succeed, got {legacyResult.Status}");
        TestAssert.Equal(3, legacyResult.Offers.Count);
    }

    public static void ExactSavedOffersRestoreAndTamperIsRejected()
    {
        var definition = Definition(
            BuildEra(
                EraA, "restore", 1, 2,
                new EnemySpec("r1", 5m),
                new EnemySpec("r2", 17m)));
        var request = Request(
            EraA, budget: 300m, round: 3, seed: 77, runId: "cost-restore");
        var generated = new SeededEncounterCatalogue(definition, CostOptions)
            .TryGenerateOffers(request);
        TestAssert.True(
            generated.IsSuccess, $"generation failed: {generated.Status}");

        var restoring = new SeededEncounterCatalogue(definition, CostOptions);
        var restored = restoring.RestoreOffers(request, generated.Offers);
        TestAssert.True(
            restored.IsSuccess,
            $"exact saved offers must restore, got {restored.Status}");
        TestAssert.Equal(
            Signature(generated.Offers), Signature(restored.Offers));
        foreach (var plan in restored.Offers)
        {
            TestAssert.NotNull(restoring.Find(plan.OfferId));
        }

        var tampered = generated.Offers.ToArray();
        var first = tampered[0];
        tampered[0] = first with
        {
            Enemies = first.Enemies
                .Select((enemy, index) => index == 0
                    ? enemy with { Count = enemy.Count + 1 }
                    : enemy)
                .ToArray()
        };

        var rejecting = new SeededEncounterCatalogue(definition, CostOptions);
        var rejected = rejecting.RestoreOffers(request, tampered);
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidPersistedOffers, rejected.Status);
        TestAssert.Equal(0, rejected.Offers.Count);
    }

    private static CatalogueDefinition Definition(
        params CatalogueEraContent[] eras) =>
        new(
            Version,
            CatalogueDefinition.FixtureLabel,
            true,
            Unit,
            eras);

    private static CatalogueEraContent BuildEra(
        EraReference era,
        string suffix,
        int hashSeed,
        int mapCount,
        params EnemySpec[] enemies)
    {
        var maps = new MapEntry[mapCount];
        for (int index = 0; index < mapCount; index++)
        {
            maps[index] = new MapEntry(
                era,
                $"fixture.cost.map.{suffix}.{index + 1}",
                Hash(hashSeed * 100 + index + 1),
                new[] { $"fixture.cost.layout.{suffix}.{index + 1}.1" },
                Array.Empty<ModRequirement>(),
                ContentQualification.Fixture);
        }

        var designs = new EnemyDesignEntry[enemies.Length];
        for (int index = 0; index < enemies.Length; index++)
        {
            var spec = enemies[index];
            designs[index] = new EnemyDesignEntry(
                new BlueprintReference(
                    $"fixture.cost.blueprint.{suffix}.{spec.Id}",
                    Hash(hashSeed * 100 + 50 + index),
                    era,
                    Array.Empty<ModRequirement>()),
                EnemyRole.Line,
                spec.Threat ?? spec.Price,
                new ScenarioValue(spec.Price, spec.UnitId ?? Unit),
                ContentQualification.Fixture);
        }

        return new CatalogueEraContent(era, maps, designs);
    }

    private static OfferRequest Request(
        EraReference era,
        decimal budget,
        int round,
        long seed = 1,
        string runId = "cost-run-1") =>
        new(
            runId,
            seed,
            round,
            EncounterRules.BlockForRound(round),
            era,
            new ScenarioValue(budget, Unit),
            0,
            Version);

    private static EncounterPlan Plan(
        IReadOnlyList<EncounterPlan> plans,
        EncounterRisk risk) =>
        plans.Single(plan => plan.Risk == risk);

    private static decimal Cost(
        CatalogueDefinition definition,
        EncounterPlan plan)
    {
        var era = definition.Eras.Single(
            entry => entry.Era.NativeId == plan.Era.NativeId);
        decimal total = 0m;
        foreach (var enemy in plan.Enemies)
        {
            var design = era.Enemies.Single(
                entry => entry.Blueprint.LocalId == enemy.BlueprintId);
            total += design.CheckedPrice!.Value * enemy.Count;
        }

        return total;
    }

    private static decimal CostTarget(
        CatalogueDefinition definition,
        OfferRequest request,
        EncounterRisk risk)
    {
        var era = TestCatalogueFactory.Era(definition, request.Era);
        return EncounterRules.CostThreatTarget(
            request.AvailableBudget,
            request.Round,
            risk,
            era.Enemies);
    }

    private static int Count(EncounterPlan plan) =>
        plan.Enemies.Sum(enemy => enemy.Count);

    private static string Signature(IReadOnlyList<EncounterPlan> plans) =>
        string.Join(
            "|",
            plans.Select(plan =>
                $"{plan.OfferId}:{plan.PlanHash}:{plan.MapId}:{plan.SpawnLayoutId}:"
                + string.Join(
                    ",",
                    plan.Enemies
                        .OrderBy(enemy => enemy.BlueprintId, StringComparer.Ordinal)
                        .Select(enemy => $"{enemy.BlueprintId}x{enemy.Count}"))));

    private static string Hash(int seed) =>
        seed.ToString("x", CultureInfo.InvariantCulture).PadLeft(64, '0');

    private sealed record EnemySpec(
        string Id,
        decimal Price,
        decimal? Threat = null,
        string? UnitId = null);
}
