using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

// Byte-level compatibility proof for schema 6. The checked-in golden payload was
// produced by the 0.3.7 baseline build from an isolated copy of its Core project;
// the current build must reproduce exactly the same 240 offer sets.
public static class GoldenV6Tests
{
    private const string Unit = "fixture.scenario.unit.v1";
    private const string GoldenFile = "golden/v6-offers.txt";

    public static void Run()
    {
        var actual = Emit();
        var path = Path.Combine(AppContext.BaseDirectory, GoldenFile);
        if (!File.Exists(path))
            throw new InvalidOperationException("golden file is missing: " + path);
        var expected = File.ReadAllText(path);
        if (expected == actual) return;
        var limit = Math.Min(expected.Length, actual.Length);
        var index = 0;
        while (index < limit && expected[index] == actual[index]) index++;
        throw new InvalidOperationException($"v6 golden bytes differ at character {index}: "
            + $"expected '{(index < expected.Length ? expected[index].ToString() : "<end>")}', "
            + $"actual '{(index < actual.Length ? actual[index].ToString() : "<end>")}'");
    }

    // Also called by the isolated baseline generator.
    public static string Emit()
    {
        var definition = Definition();
        var options = CatalogueOptions.Fixture;
        var lines = new List<string> { "v6|fixture|catalogue=1|combinations=240" };
        long[] seeds = { 9917L, 424242L, 777777L };
        foreach (var seed in seeds)
        foreach (var era in Enumerable.Range(0, 5))
        for (var roundInEra = 1; roundInEra <= 4; roundInEra++)
        for (var roll = 0; roll < 4; roll++)
        {
            var round = era * 4 + roundInEra;
            var request = new OfferRequest("golden-v6-run", seed, round, era,
                definition.Eras[era].Era, new ScenarioValue(300m + era * 50m, Unit), 0,
                definition.Version)
            {
                RulesVersion = 6,
                OfferRoll = roll,
                EnemyBudgetMultiplier = roll % 2 == 0 ? 1m : 1.2m,
                EraProgressionEnabled = era % 2 == 0
            };
            var result = new SeededEncounterCatalogue(definition, options).TryGenerateOffers(request);
            lines.Add($"{seed}|{era}|{round}|{roll}|" + (result.IsSuccess
                ? JsonSerializer.Serialize(result.Offers)
                : "ERROR|" + result.Status + "|" + string.Join(";", result.Diagnostics)));
        }
        if (lines.Count - 1 != 240)
            throw new InvalidOperationException("golden matrix must contain exactly 240 offer sets");
        return string.Join("\n", lines) + "\n";
    }

    private static CatalogueDefinition Definition()
    {
        var eras = new List<CatalogueEraContent>();
        for (var e = 0; e < 5; e++)
        {
            var era = new EraReference("golden.v6.era." + e);
            var maps = Enumerable.Range(0, 3).Select(m => new MapEntry(era,
                $"golden.v6.{e}.map.{m}", Hash(6100 + e * 10 + m),
                new[] { $"golden.v6.{e}.layout.{m}" }, Array.Empty<ModRequirement>(),
                ContentQualification.Fixture)).ToArray();
            var roles = new[]
            {
                EnemyRole.Line, EnemyRole.Support, EnemyRole.Heavy,
                EnemyRole.Recon, EnemyRole.Line
            };
            var enemies = roles.Select((role, d) =>
            {
                var price = 20m + d * 18m + e * 3m;
                return new EnemyDesignEntry(
                    new BlueprintReference($"golden.v6.{e}.d{d}",
                        Hash(6200 + e * 20 + d), era,
                        Array.Empty<ModRequirement>()),
                    role, price, new ScenarioValue(price, Unit),
                    ContentQualification.Fixture);
            }).ToArray();
            eras.Add(new CatalogueEraContent(era, maps, enemies));
        }
        return new CatalogueDefinition("golden.v6.fixture.1",
            CatalogueDefinition.FixtureLabel, true, Unit, eras.ToArray());
    }

    private static string Hash(int seed) =>
        seed.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0');
}
