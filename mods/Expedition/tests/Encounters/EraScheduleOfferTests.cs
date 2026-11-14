using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

// Focused boundary evidence for the legacy planner path used by Adventure
// offers. The finite boundary follows the pinned era definition (five to eight eras are covered here).
public static class EraScheduleOfferTests
{
    public static void FiveEraLegacyRequestUsesDefinitionFiniteBoundary()
    {
        var five = DefinitionWithEras(5);
        var lastEra = five.Eras[^1].Era;
        var catalogue = new SeededEncounterCatalogue(five, TestCatalogueFactory.Options);

        var finite = catalogue.TryGenerateOffers(TestCatalogueFactory.Request(
            round: 13, seed: 3, runId: "optional-five-finite", era: lastEra,
            budget: 1_000_000m, catalogueVersion: five.Version));
        TestAssert.Equal(EncounterAvailabilityStatus.Success, finite.Status);
        TestAssert.Equal(3, finite.Offers.Count);

        var endless = catalogue.TryGenerateOffers(TestCatalogueFactory.Request(
            round: 16, seed: 3, runId: "optional-five-endless", era: lastEra,
            budget: 1_000_000m, catalogueVersion: five.Version));
        TestAssert.Equal(5, TestCatalogueFactory.Request(
            round: 16, seed: 3, era: lastEra, budget: 1m,
            catalogueVersion: five.Version).Block);
        TestAssert.Equal(EncounterAvailabilityStatus.EndlessContentExhausted, endless.Status);
        TestAssert.ContainsError(endless.Diagnostics, "12-enemy cap");
    }

    public static void SixEraLegacyRequestStillUsesSixthBlockAsFinite()
    {
        var six = DefinitionWithEras(6);
        var lastEra = six.Eras[^1].Era;
        var catalogue = new SeededEncounterCatalogue(six, TestCatalogueFactory.Options);

        var sixthFinite = catalogue.TryGenerateOffers(TestCatalogueFactory.Request(
            round: 16, seed: 4, runId: "optional-six-last-finite", era: lastEra,
            budget: 1_000_000m, catalogueVersion: six.Version));
        TestAssert.Equal(EncounterAvailabilityStatus.Success, sixthFinite.Status);

        var seventhEndless = catalogue.TryGenerateOffers(TestCatalogueFactory.Request(
            round: 19, seed: 4, runId: "optional-six-endless", era: lastEra,
            budget: 1_000_000m, catalogueVersion: six.Version));
        TestAssert.Equal(EncounterAvailabilityStatus.EndlessContentExhausted,
            seventhEndless.Status);
    }

    public static void EightEraLegacyRequestUsesEighthBlockAsFinite()
    {
        var eight = DefinitionWithEras(8);
        var lastEra = eight.Eras[^1].Era;
        var catalogue = new SeededEncounterCatalogue(eight, TestCatalogueFactory.Options);

        var lastFinite = catalogue.TryGenerateOffers(TestCatalogueFactory.Request(
            round: 22, seed: 5, runId: "optional-eight-last-finite", era: lastEra,
            budget: 1_000_000m, catalogueVersion: eight.Version));
        TestAssert.Equal(EncounterAvailabilityStatus.Success, lastFinite.Status);

        var endless = catalogue.TryGenerateOffers(TestCatalogueFactory.Request(
            round: 25, seed: 5, runId: "optional-eight-endless", era: lastEra,
            budget: 1_000_000m, catalogueVersion: eight.Version));
        TestAssert.Equal(EncounterAvailabilityStatus.EndlessContentExhausted, endless.Status);
    }

    private static CatalogueDefinition DefinitionWithEras(int count)
    {
        var template = TestCatalogueFactory.CreateExample();
        var eras = new List<CatalogueEraContent>();
        for (var index = 0; index < count; index++)
        {
            var era = new EraReference("fixture.era.optional." + index);
            var source = template.Eras[index % template.Eras.Count];
            eras.Add(new CatalogueEraContent(era,
                source.Maps.Select(map => map with { Era = era }).ToArray(),
                source.Enemies.Select(enemy => enemy with
                {
                    Blueprint = enemy.Blueprint with { Era = era }
                }).ToArray()));
        }

        return template with { Eras = eras.ToArray() };
    }
}
