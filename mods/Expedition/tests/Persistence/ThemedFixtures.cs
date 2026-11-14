using System.Text;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace SprocketExpedition.Core.Persistence.Tests;

// Synthetic catalogue content used only by persistence regressions. It is built
// through the real public catalogue/offer pipeline so storage tests never
// hand-author an invalid EncounterPlan.
internal sealed record AdventureOfferFixture(
    CatalogueDefinition Definition,
    IReadOnlyList<EncounterPlan> Offers,
    OfferRequest Request,
    IReadOnlyList<EraReference> Eras);

internal static class ThemedFixtures
{
    internal static AdventureOfferFixture Create(int rulesVersion, long seed = 1234L,
        decimal budget = 400m, decimal multiplier = 1m, bool progression = false)
    {
        var era = new EraReference("era-themed-wwi");
        var maps = Enumerable.Range(0, 3).Select(index => new MapEntry(
            era,
            "themed-map-" + index,
            Fixtures.Sha(Encoding.UTF8.GetBytes("themed-map-" + index)),
            new[] { "themed-layout-" + index },
            Array.Empty<ModRequirement>(),
            ContentQualification.Fixture)).ToArray();
        var enemies = new[]
        {
            Design(era, "enemy.leopard", EnemyRole.Line, 100m, "Leopard"),
            Design(era, "enemy.light", EnemyRole.Recon, 20m, "Light"),
            Design(era, "enemy.support", EnemyRole.Support, 50m, "Support"),
            Design(era, "enemy.heavy", EnemyRole.Heavy, 180m, "Heavy")
        };
        var definition = new CatalogueDefinition(
            "fixture.adventure.persistence.1",
            CatalogueDefinition.FixtureLabel,
            true,
            Fixtures.CostUnit,
            new[] { new CatalogueEraContent(era, maps, enemies) });
        var catalogue = new SeededEncounterCatalogue(definition,
            new CatalogueOptions(ContentQualification.Fixture, false, true));
        var request = new OfferRequest(
            Fixtures.RunId,
            seed,
            1,
            0,
            era,
            new ScenarioValue(budget, Fixtures.CostUnit),
            0,
            definition.Version)
        {
            RulesVersion = rulesVersion,
            OfferRoll = 0,
            EnemyBudgetMultiplier = multiplier,
            EraProgressionEnabled = progression
        };
        var offers = catalogue.GenerateOffers(request);
        var eras = new[]
        {
            era,
            new EraReference("era-themed-2"),
            new EraReference("era-themed-3"),
            new EraReference("era-themed-4"),
            new EraReference("era-themed-5"),
            new EraReference("era-themed-6")
        };
        return new AdventureOfferFixture(definition, offers, request, eras);
    }

    internal static AdventureOfferFixture CreateV3WithSpecial()
    {
        for (var seed = 1L; seed <= 128L; seed++)
        {
            var fixture = Create(3, seed);
            if (fixture.Offers.Any(offer => offer.Extras?.Special is not null))
                return fixture;
        }
        throw new InvalidOperationException(
            "No v3 fixture seed produced an actual pre-v4 special preset.");
    }

    private static EnemyDesignEntry Design(EraReference era, string id, EnemyRole role,
        decimal price, string family) => new(
            new BlueprintReference(id,
                Fixtures.Sha(Encoding.UTF8.GetBytes(id)),
                era,
                Array.Empty<ModRequirement>()),
            role,
            price,
            new ScenarioValue(price, Fixtures.CostUnit),
            ContentQualification.Fixture)
        {
            FormationFamily = family
        };
}
