using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

public static class RestoreTests
{
    public static void RestoredOffersAreExactAndFindable()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: 5, seed: 14, runId: "restore-exact", budget: 460m,
            era: TestCatalogueFactory.InterwarEra);
        var original = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(request);

        var restoredCatalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var result = restoredCatalogue.RestoreOffers(request, original);
        TestAssert.True(result.IsSuccess, string.Join(" | ", result.Diagnostics));
        TestAssert.Equal(3, result.Offers.Count);

        foreach (var plan in original)
        {
            var found = restoredCatalogue.Find(plan.OfferId);
            TestAssert.NotNull(found);
            TestAssert.Equal(plan.PlanHash, found!.PlanHash);
            TestAssert.Equal(plan.MapId, found.MapId);
            TestAssert.Equal(plan.SpawnLayoutId, found.SpawnLayoutId);
        }

        var regenerated = restoredCatalogue.GenerateOffers(request);
        TestAssert.Equal(
            string.Join("|", original.Select(plan => plan.PlanHash)),
            string.Join("|", regenerated.Select(plan => plan.PlanHash)));
    }

    public static void RestoreAcceptsReorderedPersistedSet()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: 2, seed: 19, runId: "restore-order", budget: 330m);
        var original = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(request);
        var reordered = original.Reverse().ToArray();

        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var result = catalogue.RestoreOffers(request, reordered);
        TestAssert.True(result.IsSuccess, string.Join(" | ", result.Diagnostics));
        foreach (var plan in original)
        {
            TestAssert.NotNull(catalogue.Find(plan.OfferId));
        }
    }

    public static void RestoreRejectsMissingRiskAtomically()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: 4, seed: 22, runId: "restore-missing", budget: 380m);
        var original = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(request);

        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var result = catalogue.RestoreOffers(
            request, original.Take(2).ToArray());

        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidPersistedOffers, result.Status);
        TestAssert.ContainsError(result.Diagnostics, "exactly one Dangerous");
        foreach (var plan in original)
        {
            TestAssert.Null(catalogue.Find(plan.OfferId));
        }
    }

    public static void RestoreRejectsDuplicateOfferId()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: 6, seed: 23, runId: "restore-duplicate", budget: 410m);
        var original = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(request);
        var duplicate = new[]
        {
            original[0],
            original[0],
            original[2]
        };

        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var result = catalogue.RestoreOffers(request, duplicate);
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidPersistedOffers, result.Status);
        TestAssert.ContainsError(result.Diagnostics, "duplicate offer id");
        TestAssert.Null(catalogue.Find(original[0].OfferId));
    }

    public static void RestoreRejectsWrongRound()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: 7, seed: 24, runId: "restore-round", budget: 430m);
        var original = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(request);
        var changed = original
            .Select(plan => TestCatalogueFactory.Rehash(
                definition,
                request,
                plan with { Round = plan.Round + 1 }))
            .ToArray();

        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var result = catalogue.RestoreOffers(request, changed);
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidPersistedOffers, result.Status);
        TestAssert.ContainsError(result.Diagnostics, "does not match request round");
    }

    public static void RestoreRejectsWrongEra()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: 8, seed: 25, runId: "restore-era", budget: 440m);
        var original = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(request);
        var changed = original
            .Select(plan => TestCatalogueFactory.Rehash(
                definition,
                request,
                plan with { Era = TestCatalogueFactory.InterwarEra }))
            .ToArray();

        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var result = catalogue.RestoreOffers(request, changed);
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidPersistedOffers, result.Status);
        TestAssert.ContainsError(result.Diagnostics, "native era does not match");
    }

    public static void RestoreRejectsTamperedPlanHashAndOfferId()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: 9, seed: 26, runId: "restore-hash", budget: 450m);
        var original = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(request);

        var badHash = original
            .Select((plan, index) => index == 1
                ? plan with { PlanHash = new string('0', 64) }
                : plan)
            .ToArray();
        var hashCatalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var hashResult = hashCatalogue.RestoreOffers(request, badHash);
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidPersistedOffers, hashResult.Status);
        TestAssert.ContainsError(hashResult.Diagnostics, "plan hash does not match");

        var badId = original
            .Select((plan, index) => index == 2
                ? plan with { OfferId = "enc.tampered.id" }
                : plan)
            .ToArray();
        var idCatalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var idResult = idCatalogue.RestoreOffers(request, badId);
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidPersistedOffers, idResult.Status);
        TestAssert.ContainsError(idResult.Diagnostics, "not tied to the plan payload");
    }

    public static void RestoreRejectsConflictingExistingSet()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: 10, seed: 27, runId: "restore-conflict", budget: 470m);
        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var original = catalogue.GenerateOffers(request);

        var otherRequest = request with { Seed = 99 };
        var other = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(otherRequest);

        var result = catalogue.RestoreOffers(request, other);
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidPersistedOffers, result.Status);
        TestAssert.ContainsError(result.Diagnostics, "conflict with already registered");
        TestAssert.Equal(
            original[0].PlanHash, catalogue.Find(original[0].OfferId)!.PlanHash);
    }

    public static void RestoreRejectsWrongCatalogueVersion()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: 11, seed: 28, runId: "restore-version", budget: 490m);
        var original = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(request);
        var badRequest = request with
        {
            CatalogueVersion = "another.catalogue"
        };

        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var result = catalogue.RestoreOffers(badRequest, original);
        TestAssert.Equal(
            EncounterAvailabilityStatus.CatalogueVersionMismatch, result.Status);
        TestAssert.Null(catalogue.Find(original[0].OfferId));
    }

    public static void RestoreRejectsNullSet()
    {
        var catalogue = TestCatalogueFactory.CreateCatalogue();
        var result = catalogue.RestoreOffers(
            TestCatalogueFactory.Request(round: 1), null);
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidPersistedOffers, result.Status);
    }

    public static void ResolvedOffersRemainFindableAfterRestore()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: 3, seed: 29, runId: "restore-resolved", budget: 360m);
        var original = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(request);

        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);

        // MarkResolved requires the exact validated plan to be registered first.
        TestAssert.False(catalogue.MarkResolved(original[1].OfferId));

        var restore = catalogue.RestoreOffers(request, original);
        TestAssert.True(restore.IsSuccess);

        // After restore the plan is known; marking now locks the round while
        // keeping every persisted plan reachable through Find for retries.
        TestAssert.True(catalogue.MarkResolved(original[1].OfferId));
        TestAssert.True(catalogue.IsResolved(original[1].OfferId));
        TestAssert.NotNull(catalogue.Find(original[1].OfferId));
        TestAssert.NotNull(catalogue.Find(original[0].OfferId));
        TestAssert.NotNull(catalogue.Find(original[2].OfferId));

        var reroll = catalogue.TryGenerateOffers(request);
        TestAssert.Equal(
            EncounterAvailabilityStatus.OfferAlreadyResolved, reroll.Status);
    }
}
