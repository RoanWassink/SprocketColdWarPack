using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

public static class CatalogueJsonTests
{
    public static void ExampleFile_IsLabelledFixtureAndLoads()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "example.catalogue.json");
        TestAssert.True(File.Exists(path), $"missing test content file: {path}");

        var definition = CatalogueJson.Load(File.ReadAllText(path));

        TestAssert.Equal(
            CatalogueDefinition.FixtureLabel, definition.Label);
        TestAssert.True(definition.IsFixture);
        TestAssert.Equal("fixture.catalogue.1", definition.Version);
        TestAssert.Equal(3, definition.Eras.Count);
        TestAssert.Equal(3, definition.Eras[0].Maps.Count);
        TestAssert.Equal(4, definition.Eras[0].Enemies.Count);

        var validation = CatalogueValidator.Validate(
            definition, CatalogueOptions.Fixture);
        TestAssert.True(
            validation.IsValid,
            string.Join(" | ", validation.Errors));

        var release = CatalogueValidator.Validate(
            definition, CatalogueOptions.Release);
        TestAssert.False(
            release.IsValid,
            "fixture file must never pass release qualification");
        TestAssert.ContainsError(release.Errors, "fixture content cannot satisfy");

        var catalogue = new SeededEncounterCatalogue(
            definition, CatalogueOptions.Fixture);
        var offers = catalogue.GenerateOffers(new OfferRequest(
            "json-fixture-run",
            42L,
            1,
            EncounterRules.BlockForRound(1),
            new EraReference("fixture.era.wwi"),
            new ScenarioValue(300m, definition.PriceUnitId),
            0,
            definition.Version));

        TestAssert.Equal(3, offers.Count);
    }

    public static void JsonLoader_RejectsUnknownProperty()
    {
        string text = ReadExample()
            .Replace(
                "\"eras\": [",
                "\"unexpected\": true,\n  \"eras\": [",
                StringComparison.Ordinal);
        var exception = TestAssert.Throws<CatalogueJsonException>(
            () => CatalogueJson.Load(text));
        TestAssert.Contains("unknown property", exception.Message);
    }

    public static void JsonLoader_RejectsDuplicateProperty()
    {
        string text = ReadExample().Replace(
            "\"label\": \"TEST FIXTURE NOT PLAYABLE\",",
            "\"label\": \"TEST FIXTURE NOT PLAYABLE\",\n  \"label\": \"TEST FIXTURE NOT PLAYABLE\",",
            StringComparison.Ordinal);
        var exception = TestAssert.Throws<CatalogueJsonException>(
            () => CatalogueJson.Load(text));
        TestAssert.Contains("duplicate property", exception.Message);
    }

    public static void JsonLoader_RejectsMissingRequiredProperty()
    {
        string text = ReadExample().Replace(
            "\"priceUnitId\": \"fixture.scenario.unit.v1\",",
            string.Empty,
            StringComparison.Ordinal);
        var exception = TestAssert.Throws<CatalogueJsonException>(
            () => CatalogueJson.Load(text));
        TestAssert.Contains("priceUnitId", exception.Message);
    }

    public static void JsonLoader_RejectsMalformedJsonAndWrongShape()
    {
        TestAssert.Throws<CatalogueJsonException>(
            () => CatalogueJson.Load("{"));
        TestAssert.Throws<CatalogueJsonException>(
            () => CatalogueJson.Load("[]"));
        TestAssert.Throws<CatalogueJsonException>(
            () => CatalogueJson.Load(string.Empty));
    }

    public static void JsonLoader_RejectsUnknownRoleAndBadHash()
    {
        string badRole = ReadExample().Replace(
            "\"role\": \"Line\"",
            "\"role\": \"Tank\"",
            StringComparison.Ordinal);
        var roleException = TestAssert.Throws<CatalogueJsonException>(
            () => CatalogueJson.Load(badRole));
        TestAssert.Contains("role", roleException.Message);

        string badHash = ReadExample().Replace(
            "3123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "not-a-hash",
            StringComparison.Ordinal);
        var definition = CatalogueJson.Load(badHash);
        var validation = CatalogueValidator.Validate(
            definition, CatalogueOptions.Fixture);
        TestAssert.ContainsError(validation.Errors, "64 hex characters");
    }

    public static void JsonLoader_ParsesOptionalFormationFamily()
    {
        var text = ReadExample().Replace(
            "\"role\": \"Line\"",
            "\"role\": \"Line\",\n        \"formationFamily\": \"Leopard\"",
            StringComparison.Ordinal);
        var definition = CatalogueJson.Load(text);
        TestAssert.True(definition.Eras[0].Enemies.Any(enemy =>
            enemy.FormationFamily == "Leopard"),
            "optional authored formation family must load");
        TestAssert.True(definition.Eras[0].Enemies.Any(enemy =>
            enemy.FormationFamily is null),
            "absent optional family remains null, not inferred");
    }

    public static void JsonLoader_RejectsBlankFormationFamily()
    {
        var text = ReadExample().Replace(
            "\"role\": \"Line\"",
            "\"role\": \"Line\",\n        \"formationFamily\": \"   \"",
            StringComparison.Ordinal);
        var definition = CatalogueJson.Load(text);
        var validation = CatalogueValidator.Validate(
            definition, CatalogueOptions.Fixture);
        TestAssert.ContainsError(validation.Errors, "formation family");
    }

    public static void JsonLoader_ParsesOptionalMeasuredMass()
    {
        var text = ReadExample().Replace(
            "\"role\": \"Line\"",
            "\"role\": \"Line\",\n        \"massKg\": 42000",
            StringComparison.Ordinal);
        var definition = CatalogueJson.Load(text);
        var measured = definition.Eras[0].Enemies.Where(enemy => enemy.MassKg == 42_000).ToArray();
        TestAssert.Equal(1, measured.Length, "optional measured native mass must load");
        TestAssert.True(definition.Eras[0].Enemies.Any(enemy => enemy.MassKg is null),
            "absent optional mass remains unknown, never zero");
        var zero = ReadExample().Replace(
            "\"role\": \"Line\"",
            "\"role\": \"Line\",\n        \"massKg\": 0",
            StringComparison.Ordinal);
        var exception = TestAssert.Throws<CatalogueJsonException>(() => CatalogueJson.Load(zero));
        TestAssert.Contains("massKg", exception.Message);
    }

    private static string ReadExample()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "example.catalogue.json");
        return File.ReadAllText(path);
    }
}
