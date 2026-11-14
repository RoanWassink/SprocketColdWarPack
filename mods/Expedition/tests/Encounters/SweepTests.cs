using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

public static class SweepTests
{
    private const int SeedCount = 128;

    public static void LargeSeededSweep_AllErasAllRounds()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var eras = new[]
        {
            TestCatalogueFactory.WwiEra,
            TestCatalogueFactory.InterwarEra,
            TestCatalogueFactory.ColdwarEra
        };

        var offerIds = new HashSet<string>(StringComparer.Ordinal);
        var stats = new SortedDictionary<
            (int Round, EncounterRisk Risk),
            (int MinCount, int MaxCount, decimal MinThreat, decimal MaxThreat, int Samples)>();

        int requestCount = 0;
        int planCount = 0;

        for (int seed = 1; seed <= SeedCount; seed++)
        {
            for (int eraIndex = 0; eraIndex < eras.Length; eraIndex++)
            {
                var era = eras[eraIndex];
                var eraContent = TestCatalogueFactory.Era(definition, era);
                var catalogue = TestCatalogueFactory.CreateCatalogue(definition);

                for (int round = 1; round <= EncounterRules.FiniteRounds; round++)
                {
                    decimal budget = 100m + round * 20m;
                    var request = TestCatalogueFactory.Request(
                        round: round,
                        seed: seed,
                        runId: $"sweep-{seed}-{eraIndex}",
                        era: era,
                        budget: budget);

                    var result = catalogue.TryGenerateOffers(request);
                    requestCount++;
                    TestAssert.True(
                        result.IsSuccess,
                        $"{era.NativeId} round {round} seed {seed}: {result.Status} {string.Join(" | ", result.Diagnostics)}");
                    TestAssert.Equal(3, result.Offers.Count);
                    TestAssert.Equal(
                        3,
                        result.Offers.Select(plan => plan.Risk)
                            .Distinct().Count());

                    var validation = EncounterOfferValidator.ValidateSet(
                        request, result.Offers, definition,
                        TestCatalogueFactory.Options);
                    TestAssert.True(
                        validation.IsValid,
                        $"{era.NativeId} round {round} seed {seed}: {string.Join(" | ", validation.Errors)}");

                    foreach (var plan in result.Offers)
                    {
                        planCount++;
                        TestAssert.True(
                            offerIds.Add(plan.OfferId),
                            $"duplicate offer id across sweep: {plan.OfferId}");
                        TestAssert.Equal(round, plan.Round);
                        TestAssert.Equal(era.NativeId, plan.Era.NativeId);
                        TestAssert.Equal(definition.Version, plan.CatalogueVersion);

                        int count = plan.Enemies.Sum(enemy => enemy.Count);
                        TestAssert.InRange(1, EncounterRules.MaxEnemies, count);

                        decimal threat =
                            EncounterOfferValidator.ComputeFormationThreat(
                                eraContent, plan.Enemies);
                        decimal target = EncounterRules.ThreatTarget(
                            request.AvailableBudget, request.Round, plan.Risk);
                        TestAssert.True(
                            threat > 0m && threat <= target,
                            $"{era.NativeId} round {round} seed {seed} {plan.Risk}: threat {threat} target {target}");

                        var key = (round, plan.Risk);
                        if (stats.TryGetValue(key, out var existing))
                        {
                            stats[key] = (
                                Math.Min(existing.MinCount, count),
                                Math.Max(existing.MaxCount, count),
                                Math.Min(existing.MinThreat, threat),
                                Math.Max(existing.MaxThreat, threat),
                                existing.Samples + 1);
                        }
                        else
                        {
                            stats[key] = (count, count, threat, threat, 1);
                        }
                    }
                }
            }
        }

        TestAssert.Equal(
            SeedCount * eras.Length * EncounterRules.FiniteRounds,
            requestCount);
        TestAssert.Equal(requestCount * 3, planCount);
        TestAssert.Equal(planCount, offerIds.Count);
        TestAssert.Equal(EncounterRules.FiniteRounds * 3, stats.Count);

        foreach (var entry in stats)
        {
            TestAssert.Equal(SeedCount * eras.Length, entry.Value.Samples);
            TestAssert.InRange(1, EncounterRules.MaxEnemies, entry.Value.MinCount);
            TestAssert.InRange(1, EncounterRules.MaxEnemies, entry.Value.MaxCount);
            TestAssert.True(entry.Value.MinThreat > 0m);
            TestAssert.True(entry.Value.MaxThreat >= entry.Value.MinThreat);
        }

        Console.WriteLine(
            "sweep summary: round/risk count and threat ranges across "
            + $"{SeedCount} seeds x {eras.Length} fixture eras");
        foreach (var entry in stats)
        {
            Console.WriteLine(
                $"  round {entry.Key.Round,2} {entry.Key.Risk,-9} "
                + $"count {entry.Value.MinCount,2}-{entry.Value.MaxCount,2} "
                + $"threat {entry.Value.MinThreat,4}-{entry.Value.MaxThreat,4} "
                + $"samples {entry.Value.Samples}");
        }
    }

    public static void LargeSeededSweep_RestoreRoundTrips()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var eras = new[]
        {
            TestCatalogueFactory.WwiEra,
            TestCatalogueFactory.InterwarEra,
            TestCatalogueFactory.ColdwarEra
        };

        for (int seed = 1; seed <= SeedCount; seed++)
        {
            for (int eraIndex = 0; eraIndex < eras.Length; eraIndex++)
            {
                var era = eras[eraIndex];
                var request = TestCatalogueFactory.Request(
                    round: 1,
                    seed: seed,
                    runId: $"restore-sweep-{seed}-{eraIndex}",
                    era: era,
                    budget: 300m);
                var original = TestCatalogueFactory.CreateCatalogue(definition)
                    .GenerateOffers(request);

                var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
                var restore = catalogue.RestoreOffers(request, original);
                TestAssert.True(
                    restore.IsSuccess,
                    $"{era.NativeId} seed {seed}: {string.Join(" | ", restore.Diagnostics)}");

                foreach (var plan in original)
                {
                    var found = catalogue.Find(plan.OfferId);
                    TestAssert.NotNull(found);
                    TestAssert.Equal(plan.PlanHash, found!.PlanHash);
                    TestAssert.Equal(plan.MapId, found.MapId);
                    TestAssert.Equal(plan.SpawnLayoutId, found.SpawnLayoutId);
                    TestAssert.Equal(plan.SupplyReward, found.SupplyReward);
                }

                var replay = catalogue.GenerateOffers(request);
                TestAssert.Equal(
                    string.Join("|", original.Select(plan => plan.PlanHash)),
                    string.Join("|", replay.Select(plan => plan.PlanHash)));
            }
        }
    }

    public static void LargeSeededSweep_EndlessExhaustionIsConsistent()
    {
        var catalogueDefinition = TestCatalogueFactory.CreateExample();

        for (int seed = 1; seed <= 64; seed++)
        {
            var catalogue = TestCatalogueFactory.CreateCatalogue(catalogueDefinition);
            var request = TestCatalogueFactory.Request(
                round: 19,
                seed: seed,
                runId: $"exhaust-{seed}",
                era: TestCatalogueFactory.ColdwarEra,
                budget: 1000000m);
            var result = catalogue.TryGenerateOffers(request);
            TestAssert.Equal(
                EncounterAvailabilityStatus.EndlessContentExhausted,
                result.Status);
        }
    }

    public static void LargeSeededSweep_FiniteHugeBudgetsStayWithinCap()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var eras = new[]
        {
            TestCatalogueFactory.WwiEra,
            TestCatalogueFactory.InterwarEra,
            TestCatalogueFactory.ColdwarEra
        };

        for (int seed = 1; seed <= 32; seed++)
        {
            for (int eraIndex = 0; eraIndex < eras.Length; eraIndex++)
            {
                var era = eras[eraIndex];
                var eraContent = TestCatalogueFactory.Era(definition, era);
                var catalogue = TestCatalogueFactory.CreateCatalogue(definition);

                for (int round = 1; round <= EncounterRules.FiniteRounds; round++)
                {
                    var request = TestCatalogueFactory.Request(
                        round: round,
                        seed: seed,
                        runId: $"huge-{seed}-{eraIndex}",
                        era: era,
                        budget: 1000000m);
                    var result = catalogue.TryGenerateOffers(request);
                    TestAssert.True(
                        result.IsSuccess,
                        $"{era.NativeId} round {round} seed {seed}: {result.Status}");

                    foreach (var plan in result.Offers)
                    {
                        int count = plan.Enemies.Sum(enemy => enemy.Count);
                        TestAssert.InRange(1, EncounterRules.MaxEnemies, count);
                        decimal threat =
                            EncounterOfferValidator.ComputeFormationThreat(
                                eraContent, plan.Enemies);
                        decimal target = EncounterRules.ThreatTarget(
                            request.AvailableBudget, request.Round, plan.Risk);
                        TestAssert.True(
                            threat <= target,
                            $"{era.NativeId} round {round} seed {seed} {plan.Risk}: {threat} > {target}");
                    }
                }
            }
        }
    }
}
