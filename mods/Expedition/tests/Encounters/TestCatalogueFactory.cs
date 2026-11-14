using System.Globalization;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

// TEST FIXTURE ONLY. These are deterministic synthetic references, never native
// designs, maps or prices. Qualification is Fixture so release checks reject it.
internal static class TestCatalogueFactory
{
    public const string Version = "fixture.test.catalogue.1";
    public const string PriceUnit = "fixture.scenario.unit.v1";

    public static readonly EraReference WwiEra = new("fixture.era.wwi");
    public static readonly EraReference InterwarEra = new("fixture.era.interwar");
    public static readonly EraReference ColdwarEra = new("fixture.era.coldwar");

    public static readonly ModRequirement CoreMod = new("fixture.mod.core", "1.0");
    public static readonly ModRequirement MapsMod = new("fixture.mod.maps", "1.0");

    public static CatalogueOptions Options => CatalogueOptions.Fixture;

    public static CatalogueDefinition CreateExample() => new(
        Version,
        CatalogueDefinition.FixtureLabel,
        true,
        PriceUnit,
        new[]
        {
            BuildEra(
                WwiEra, "wwi", 0,
                new[] { 20m, 30m, 45m, 60m },
                new[] { 10m, 12m, 18m, 24m }),
            BuildEra(
                InterwarEra, "interwar", 1,
                new[] { 35m, 50m, 70m, 95m },
                new[] { 20m, 26m, 34m, 44m }),
            BuildEra(
                ColdwarEra, "coldwar", 2,
                new[] { 60m, 85m, 115m, 150m },
                new[] { 40m, 52m, 70m, 90m })
        });

    public static SeededEncounterCatalogue CreateCatalogue(
        CatalogueDefinition? definition = null) =>
        new(definition ?? CreateExample(), Options);

    public static ScenarioValue Budget(decimal value) => new(value, PriceUnit);

    public static OfferRequest Request(
        int round,
        long seed = 1,
        string runId = "run-1",
        EraReference? era = null,
        decimal budget = 300m,
        string? catalogueVersion = null,
        int? block = null,
        int pendingSupplyPoints = 0) =>
        new(
            runId,
            seed,
            round,
            block ?? EncounterRules.BlockForRound(round),
            era ?? WwiEra,
            Budget(budget),
            pendingSupplyPoints,
            catalogueVersion ?? Version);

    public static EncounterPlan FindPlan(
        IReadOnlyList<EncounterPlan> plans,
        EncounterRisk risk) =>
        plans.Single(plan => plan.Risk == risk);

    public static IReadOnlyList<EncounterPlan> ReplaceRisk(
        IReadOnlyList<EncounterPlan> plans,
        EncounterRisk risk,
        EncounterPlan replacement) =>
        plans
            .Select(plan => plan.Risk == risk ? replacement : plan)
            .ToArray();

    public static EncounterPlan Rehash(
        CatalogueDefinition definition,
        OfferRequest request,
        EncounterPlan plan)
    {
        string contentHash = CatalogueHasher.Compute(definition);
        var draft = plan with
        {
            OfferId = string.Empty,
            PlanHash = string.Empty
        };
        string offerId = PlanHasher.CreateOfferId(
            request.RunId, contentHash, draft);
        var identified = draft with { OfferId = offerId };
        return identified with
        {
            PlanHash = PlanHasher.ComputePlanHash(contentHash, identified)
        };
    }

    public static CatalogueEraContent Era(
        CatalogueDefinition definition,
        EraReference era) =>
        definition.Eras.Single(
            entry => entry.Era.NativeId == era.NativeId);

    public static string Hash(int seed) =>
        seed.ToString("x", CultureInfo.InvariantCulture).PadLeft(64, '0');

    public static CatalogueDefinition ReplaceEra(
        CatalogueDefinition definition,
        EraReference era,
        CatalogueEraContent replacement) =>
        definition with
        {
            Eras = definition.Eras
                .Select(entry => entry.Era.NativeId == era.NativeId
                    ? replacement
                    : entry)
                .ToArray()
        };

    private static CatalogueEraContent BuildEra(
        EraReference era,
        string suffix,
        int hashGroup,
        decimal[] threats,
        decimal[] prices)
    {
        var eraMod = new ModRequirement($"fixture.mod.{suffix}", "1.0");

        var maps = new[]
        {
            Map(era, $"{suffix}.a", hashGroup * 10 + 1, MapsMod),
            Map(era, $"{suffix}.b", hashGroup * 10 + 2),
            Map(era, $"{suffix}.c", hashGroup * 10 + 3)
        };

        var enemies = new[]
        {
            Enemy(
                era, suffix, "line", hashGroup * 10 + 4,
                EnemyRole.Line, threats[0], prices[0], CoreMod),
            Enemy(
                era, suffix, "support", hashGroup * 10 + 5,
                EnemyRole.Support, threats[1], prices[1], CoreMod),
            Enemy(
                era, suffix, "heavy", hashGroup * 10 + 6,
                EnemyRole.Heavy, threats[2], prices[2], CoreMod, eraMod),
            Enemy(
                era, suffix, "recon", hashGroup * 10 + 7,
                EnemyRole.Recon, threats[3], prices[3])
        };

        return new CatalogueEraContent(era, maps, enemies);
    }

    private static MapEntry Map(
        EraReference era,
        string suffix,
        int hashSeed,
        params ModRequirement[] mods) =>
        new(
            era,
            $"fixture.map.{suffix}",
            Hash(hashSeed),
            new[]
            {
                $"fixture.layout.{suffix}.1",
                $"fixture.layout.{suffix}.2"
            },
            mods,
            ContentQualification.Fixture);

    private static EnemyDesignEntry Enemy(
        EraReference era,
        string suffix,
        string blueprintSuffix,
        int hashSeed,
        EnemyRole role,
        decimal threat,
        decimal price,
        params ModRequirement[] mods) =>
        new(
            new BlueprintReference(
                $"fixture.blueprint.{suffix}.{blueprintSuffix}",
                Hash(hashSeed),
                era,
                mods),
            role,
            threat,
            new ScenarioValue(price, PriceUnit),
            ContentQualification.Fixture);
}
