using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

public static class OfferGenerationTests
{
    public static void GeneratesExactlyOneOfferPerRisk()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var offers = catalogue.GenerateOffers(
            TestCatalogueFactory.Request(round: 1, seed: 3, budget: 300m));

        TestAssert.Equal(3, offers.Count);
        TestAssert.Equal(3, offers.Select(offer => offer.Risk).Distinct().Count());
        TestAssert.Equal(
            1, offers.Count(offer => offer.Risk == EncounterRisk.Cautious));
        TestAssert.Equal(
            1, offers.Count(offer => offer.Risk == EncounterRisk.Standard));
        TestAssert.Equal(
            1, offers.Count(offer => offer.Risk == EncounterRisk.Dangerous));

        foreach (var offer in offers)
        {
            int expectedSupply = offer.Risk switch
            {
                EncounterRisk.Cautious => 1,
                EncounterRisk.Standard => 2,
                EncounterRisk.Dangerous => 3,
                _ => throw new TestFailureException("unexpected risk")
            };
            TestAssert.Equal(expectedSupply, offer.SupplyReward);
        }
    }

    public static void GeneratedSetPassesAtomicValidation()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var request = TestCatalogueFactory.Request(
            round: 5, seed: 17, budget: 450m,
            era: TestCatalogueFactory.InterwarEra);
        var offers = catalogue.GenerateOffers(request);

        var validation = EncounterOfferValidator.ValidateSet(
            request, offers, definition, TestCatalogueFactory.Options);
        TestAssert.True(
            validation.IsValid,
            string.Join(" | ", validation.Errors));
    }

    public static void PlansMatchRoundEraVersionMapAndLayout()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var request = TestCatalogueFactory.Request(
            round: 8, seed: 21, budget: 550m,
            era: TestCatalogueFactory.ColdwarEra);
        var offers = catalogue.GenerateOffers(request);
        var eraContent = TestCatalogueFactory.Era(
            definition, TestCatalogueFactory.ColdwarEra);

        foreach (var offer in offers)
        {
            TestAssert.Equal(8, offer.Round);
            TestAssert.Equal(
                TestCatalogueFactory.ColdwarEra.NativeId,
                offer.Era.NativeId);
            TestAssert.Equal(definition.Version, offer.CatalogueVersion);
            TestAssert.Equal(64, offer.PlanHash.Length);

            var map = eraContent.Maps.Single(
                entry => entry.MapId == offer.MapId);
            TestAssert.True(
                map.SpawnLayoutIds.Contains(offer.SpawnLayoutId),
                "spawn layout must belong to selected map");
        }
    }

    public static void EnemyCountsAndThreatStayWithinContract()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);

        for (int seed = 1; seed <= 24; seed++)
        {
            var request = TestCatalogueFactory.Request(
                round: 6, seed: seed, runId: $"counts-{seed}", budget: 500m,
                era: TestCatalogueFactory.InterwarEra);
            var offers = catalogue.GenerateOffers(request);
            var eraContent = TestCatalogueFactory.Era(
                definition, TestCatalogueFactory.InterwarEra);

            foreach (var offer in offers)
            {
                int total = offer.Enemies.Sum(enemy => enemy.Count);
                TestAssert.InRange(1, EncounterRules.MaxEnemies, total);
                TestAssert.True(
                    offer.Enemies.All(enemy => enemy.Count > 0),
                    "every enemy count must be positive");

                decimal threat = EncounterOfferValidator.ComputeFormationThreat(
                    eraContent, offer.Enemies);
                decimal target = EncounterRules.ThreatTarget(
                    request.AvailableBudget, request.Round, offer.Risk);
                TestAssert.True(
                    threat > 0m && threat <= target,
                    $"threat {threat} must be within (0, {target}]");
            }
        }
    }

    public static void GenerationIsDeterministicAcrossInstances()
    {
        var request = TestCatalogueFactory.Request(
            round: 10, seed: 1234, runId: "deterministic", budget: 600m
        );

        var first = TestCatalogueFactory.CreateCatalogue()
            .GenerateOffers(request);
        var second = TestCatalogueFactory.CreateCatalogue()
            .GenerateOffers(request);

        TestAssert.Equal(first.Count, second.Count);
        for (int index = 0; index < first.Count; index++)
        {
            TestAssert.Equal(first[index].OfferId, second[index].OfferId);
            TestAssert.Equal(first[index].PlanHash, second[index].PlanHash);
            TestAssert.Equal(first[index].MapId, second[index].MapId);
            TestAssert.Equal(
                first[index].SpawnLayoutId, second[index].SpawnLayoutId);
            TestAssert.Equal(first[index].Risk, second[index].Risk);
            TestAssert.Equal(
                string.Join(",", first[index].Enemies.Select(
                    enemy => $"{enemy.BlueprintId}x{enemy.Count}")),
                string.Join(",", second[index].Enemies.Select(
                    enemy => $"{enemy.BlueprintId}x{enemy.Count}")));
        }
    }

    public static void SameInstanceReturnsPersistedOffersWithoutReroll()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var request = TestCatalogueFactory.Request(
            round: 2, seed: 77, budget: 350m);
        var first = catalogue.GenerateOffers(request);
        var second = catalogue.GenerateOffers(request);

        TestAssert.True(
            ReferenceEquals(first, second),
            "persisted offers must be returned unchanged, not regenerated");
    }

    public static void DifferentSeedsProduceVariety()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var signatures = new HashSet<string>(StringComparer.Ordinal);

        for (int seed = 1; seed <= 64; seed++)
        {
            var offers = catalogue.GenerateOffers(TestCatalogueFactory.Request(
                round: 3,
                seed: seed,
                runId: $"variety-{seed}",
                budget: 420m));
            signatures.Add(string.Join("|", offers.Select(offer =>
                $"{offer.MapId}/{offer.SpawnLayoutId}/{string.Join(",", offer.Enemies.Select(enemy => $"{enemy.BlueprintId}x{enemy.Count}"))}")));
        }

        TestAssert.True(
            signatures.Count >= 12,
            $"expected broad seeded variety, found {signatures.Count} signatures");
    }

    public static void DifferentRoundsAndRunsProduceDistinctOfferIds()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var roundOne = catalogue.GenerateOffers(
            TestCatalogueFactory.Request(round: 1, seed: 5, runId: "ids-run"));
        var roundTwo = catalogue.GenerateOffers(
            TestCatalogueFactory.Request(round: 2, seed: 5, runId: "ids-run"));
        var otherRun = catalogue.GenerateOffers(
            TestCatalogueFactory.Request(round: 1, seed: 5, runId: "ids-other"));
        var thirdRun = catalogue.GenerateOffers(
            TestCatalogueFactory.Request(round: 1, seed: 5, runId: "ids-third"));

        var all = roundOne
            .Concat(roundTwo)
            .Concat(otherRun)
            .Concat(thirdRun)
            .Select(offer => offer.OfferId)
            .ToArray();
        TestAssert.Equal(12, all.Length);
        TestAssert.Equal(12, all.Distinct(StringComparer.Ordinal).Count());
    }

    public static void OfferIdsAreUniqueAcrossAllFiniteRounds()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (int round = 1; round <= EncounterRules.FiniteRounds; round++)
        {
            var offers = catalogue.GenerateOffers(
                TestCatalogueFactory.Request(
                    round: round, seed: 9001, runId: "unique-rounds",
                    budget: 300m + round * 15m));
            foreach (var offer in offers)
            {
                TestAssert.True(
                    ids.Add(offer.OfferId),
                    $"duplicate offer id across rounds: {offer.OfferId}");
            }
        }

        TestAssert.Equal(EncounterRules.FiniteRounds * 3, ids.Count);
    }

    public static void ThreeCardsUseDistinctMapsAndDistinctSignatures()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        for (int seed = 1; seed <= 32; seed++)
        {
            var offers = catalogue.GenerateOffers(TestCatalogueFactory.Request(
                round: 7, seed: seed, runId: $"distinct-{seed}",
                budget: 480m));
            TestAssert.Equal(
                3,
                offers.Select(offer => offer.MapId)
                    .Distinct(StringComparer.Ordinal).Count());
            TestAssert.Equal(
                3,
                offers.Select(offer => $"{offer.SpawnLayoutId}/{string.Join(",", offer.Enemies.Select(enemy => $"{enemy.BlueprintId}x{enemy.Count}"))}")
                    .Distinct(StringComparer.Ordinal).Count());
        }
    }

    public static void SelectionUsesCuratedThreatNotVehiclePrice()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var era = TestCatalogueFactory.Era(
            definition, TestCatalogueFactory.WwiEra);
        var enemies = era.Enemies
            .Select((enemy, index) => index == 0
                ? enemy with
                {
                    ThreatScore = 10m,
                    CheckedPrice = new ScenarioValue(
                        99999m, TestCatalogueFactory.PriceUnit)
                }
                : enemy with
                {
                    ThreatScore = 500m + index * 100m,
                    CheckedPrice = new ScenarioValue(
                        1m, TestCatalogueFactory.PriceUnit)
                })
            .ToArray();
        var changed = TestCatalogueFactory.ReplaceEra(
            definition, TestCatalogueFactory.WwiEra, era with { Enemies = enemies });
        var catalogue = new SeededEncounterCatalogue(
            changed, TestCatalogueFactory.Options);

        var offers = catalogue.GenerateOffers(
            TestCatalogueFactory.Request(round: 1, seed: 2, budget: 40m));
        string cheapBlueprintId = era.Enemies[0].Blueprint.LocalId;

        foreach (var offer in offers)
        {
            TestAssert.Contains(
                cheapBlueprintId,
                string.Join(",", offer.Enemies.Select(enemy => enemy.BlueprintId)));
        }
    }

    public static void UnknownEraIsExplicitErrorWithoutFallback()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var request = TestCatalogueFactory.Request(
            round: 1,
            seed: 1,
            era: new EraReference("fixture.era.missing"),
            budget: 300m);

        var result = catalogue.TryGenerateOffers(request);
        TestAssert.Equal(
            EncounterAvailabilityStatus.UnknownEra, result.Status);

        var exception = TestAssert.Throws<EncounterAvailabilityException>(
            () => catalogue.GenerateOffers(request));
        TestAssert.Equal(
            EncounterAvailabilityStatus.UnknownEra, exception.Status);
        TestAssert.ContainsError(exception.Diagnostics, "not present");
    }

    public static void MissingMapsIsExplicitAvailabilityError()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var era = TestCatalogueFactory.Era(
            definition, TestCatalogueFactory.WwiEra);
        var changed = TestCatalogueFactory.ReplaceEra(
            definition, TestCatalogueFactory.WwiEra,
            era with { Maps = Array.Empty<MapEntry>() });
        var options = new CatalogueOptions(ContentQualification.Fixture, false);
        var catalogue = new SeededEncounterCatalogue(changed, options);

        var result = catalogue.TryGenerateOffers(
            TestCatalogueFactory.Request(round: 1, budget: 300m));
        TestAssert.Equal(
            EncounterAvailabilityStatus.NoQualifiedMaps, result.Status);
    }

    public static void MissingEnemyDesignsIsExplicitAvailabilityError()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var era = TestCatalogueFactory.Era(
            definition, TestCatalogueFactory.WwiEra);
        var changed = TestCatalogueFactory.ReplaceEra(
            definition, TestCatalogueFactory.WwiEra,
            era with { Enemies = Array.Empty<EnemyDesignEntry>() });
        var options = new CatalogueOptions(ContentQualification.Fixture, false);
        var catalogue = new SeededEncounterCatalogue(changed, options);

        var result = catalogue.TryGenerateOffers(
            TestCatalogueFactory.Request(round: 1, budget: 300m));
        TestAssert.Equal(
            EncounterAvailabilityStatus.NoQualifiedEnemyDesigns, result.Status);
    }

    public static void MissingQualifiedContentIsAvailabilityError()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var era = TestCatalogueFactory.Era(
            definition, TestCatalogueFactory.WwiEra);
        var changed = TestCatalogueFactory.ReplaceEra(
            definition, TestCatalogueFactory.WwiEra,
            era with
            {
                Maps = era.Maps
                    .Select(map => map with
                    {
                        Qualification = ContentQualification.Candidate
                    })
                    .ToArray(),
                Enemies = era.Enemies
                    .Select(enemy => enemy with
                    {
                        Qualification = ContentQualification.Fixture
                    })
                    .ToArray()
            });
        var options = new CatalogueOptions(
            ContentQualification.Candidate, false);
        var catalogue = new SeededEncounterCatalogue(changed, options);

        var result = catalogue.TryGenerateOffers(
            TestCatalogueFactory.Request(round: 1, budget: 300m));
        TestAssert.Equal(
            EncounterAvailabilityStatus.NoQualifiedEnemyDesigns, result.Status);
    }

    public static void InvalidRequestVariantsAreRejected()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();

        var wrongUnit = TestCatalogueFactory.Request(round: 1) with
        {
            AvailableBudget = new ScenarioValue(300m, "other.unit")
        };
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidRequest,
            catalogue.TryGenerateOffers(wrongUnit).Status);

        var zeroBudget = TestCatalogueFactory.Request(round: 1) with
        {
            AvailableBudget = TestCatalogueFactory.Budget(0m)
        };
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidRequest,
            catalogue.TryGenerateOffers(zeroBudget).Status);

        var wrongBlock = TestCatalogueFactory.Request(round: 4, block: 2);
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidRequest,
            catalogue.TryGenerateOffers(wrongBlock).Status);

        var nullRequest = catalogue.TryGenerateOffers(null);
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidRequest, nullRequest.Status);
    }

    public static void CatalogueVersionMismatchIsExplicit()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var request = TestCatalogueFactory.Request(
            round: 1, catalogueVersion: "some.other.catalogue");
        var result = catalogue.TryGenerateOffers(request);
        TestAssert.Equal(
            EncounterAvailabilityStatus.CatalogueVersionMismatch, result.Status);

        var exception = TestAssert.Throws<EncounterAvailabilityException>(
            () => catalogue.GenerateOffers(request));
        TestAssert.Equal(
            EncounterAvailabilityStatus.CatalogueVersionMismatch, exception.Status);
    }

    public static void ResolvedRoundCannotBeRegenerated()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var request = TestCatalogueFactory.Request(
            round: 3, seed: 8, runId: "resolved-run", budget: 320m);
        var offers = catalogue.GenerateOffers(request);

        TestAssert.True(catalogue.MarkResolved(offers[0].OfferId));
        TestAssert.True(catalogue.IsResolved(offers[0].OfferId));
        TestAssert.False(catalogue.MarkResolved("enc.unknown"));

        var reroll = catalogue.TryGenerateOffers(request);
        TestAssert.Equal(
            EncounterAvailabilityStatus.OfferAlreadyResolved, reroll.Status);
        TestAssert.Equal(
            offers[0].PlanHash, catalogue.Find(offers[0].OfferId)!.PlanHash);
        TestAssert.Equal(
            offers[1].PlanHash, catalogue.Find(offers[1].OfferId)!.PlanHash);
    }

    public static void FindReturnsExactPlansAndUnknownReturnsNull()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var offers = catalogue.GenerateOffers(
            TestCatalogueFactory.Request(round: 9, seed: 4, budget: 500m));

        foreach (var offer in offers)
        {
            var found = catalogue.Find(offer.OfferId);
            TestAssert.NotNull(found);
            TestAssert.Equal(offer.PlanHash, found!.PlanHash);
            TestAssert.Equal(offer.MapId, found.MapId);
            TestAssert.Equal(offer.SupplyReward, found.SupplyReward);
        }

        TestAssert.Null(catalogue.Find("enc.missing"));
        TestAssert.Null(catalogue.Find(string.Empty));
    }

    public static void EndlessExhaustionIsReportedAsCompletion()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var request = TestCatalogueFactory.Request(
            round: 19,
            seed: 6,
            era: TestCatalogueFactory.ColdwarEra,
            budget: 1000000m);
        TestAssert.Equal(6, request.Block);

        var result = catalogue.TryGenerateOffers(request);
        TestAssert.Equal(
            EncounterAvailabilityStatus.EndlessContentExhausted, result.Status);
        TestAssert.ContainsError(result.Diagnostics, "12-enemy cap");

        var exception = TestAssert.Throws<EncounterAvailabilityException>(
            () => catalogue.GenerateOffers(request));
        TestAssert.Equal(
            EncounterAvailabilityStatus.EndlessContentExhausted, exception.Status);
    }

    public static void EndlessWithNormalBudgetStillGenerates()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var offers = catalogue.GenerateOffers(
            TestCatalogueFactory.Request(
                round: 19,
                seed: 6,
                runId: "endless-ok",
                era: TestCatalogueFactory.ColdwarEra,
                budget: 700m));
        TestAssert.Equal(3, offers.Count);
    }

    public static void HugeFiniteBudgetRoundsDownWithoutExceedingCap()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var request = TestCatalogueFactory.Request(
            round: 18, seed: 31, budget: 1000000m,
            era: TestCatalogueFactory.ColdwarEra);
        var offers = catalogue.GenerateOffers(request);
        var eraContent = TestCatalogueFactory.Era(
            definition, TestCatalogueFactory.ColdwarEra);

        foreach (var offer in offers)
        {
            int total = offer.Enemies.Sum(enemy => enemy.Count);
            TestAssert.InRange(1, EncounterRules.MaxEnemies, total);
            decimal threat = EncounterOfferValidator.ComputeFormationThreat(
                eraContent, offer.Enemies);
            decimal target = EncounterRules.ThreatTarget(
                request.AvailableBudget, request.Round, offer.Risk);
            TestAssert.True(
                threat <= target,
                $"threat {threat} must not exceed finite target {target}");
        }
    }

    public static void TinyBudgetProducesExplicitNoFormationError()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var request = TestCatalogueFactory.Request(round: 1, budget: 5m);
        var result = catalogue.TryGenerateOffers(request);
        TestAssert.Equal(
            EncounterAvailabilityStatus.NoValidFormation, result.Status);
    }

    public static void PendingSupplyPointsDoNotReduceSuppliedBudget()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var budget = TestCatalogueFactory.Budget(300m);

        var requestA = TestCatalogueFactory.Request(
            round: 12, seed: 55, runId: "bonus-a", budget: 300m) with
        {
            AvailableBudget = budget,
            PendingSupplyPoints = 0
        };
        var requestB = requestA with
        {
            RunId = "bonus-b",
            PendingSupplyPoints = 3
        };

        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var offersA = catalogue.GenerateOffers(requestA);
        var offersB = catalogue.GenerateOffers(requestB);

        for (int index = 0; index < offersA.Count; index++)
        {
            TestAssert.Equal(offersA[index].Risk, offersB[index].Risk);
            TestAssert.Equal(offersA[index].MapId, offersB[index].MapId);
            TestAssert.Equal(
                offersA[index].SpawnLayoutId,
                offersB[index].SpawnLayoutId);
            TestAssert.Equal(
                string.Join(",", offersA[index].Enemies.Select(
                    enemy => $"{enemy.BlueprintId}x{enemy.Count}")),
                string.Join(",", offersB[index].Enemies.Select(
                    enemy => $"{enemy.BlueprintId}x{enemy.Count}")));
        }
    }
}
