using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

public static class CatalogueValidationTests
{
    public static void FixtureCatalogue_IsStructurallyValidAsFixture()
    {
        var result = CatalogueValidator.Validate(
            TestCatalogueFactory.CreateExample(),
            TestCatalogueFactory.Options);
        TestAssert.True(
            result.IsValid,
            string.Join(" | ", result.Errors));
    }

    public static void FixtureCatalogue_IsNotReleaseQualified()
    {
        var result = CatalogueValidator.Validate(
            TestCatalogueFactory.CreateExample(),
            CatalogueOptions.Release);
        TestAssert.False(result.IsValid);
        TestAssert.ContainsError(result.Errors, "fixture content cannot satisfy");
    }

    public static void FixtureWithoutExactLabel_IsRejected()
    {
        var definition = TestCatalogueFactory.CreateExample() with
        {
            Label = "example catalogue"
        };
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "must be labelled exactly");
    }

    public static void FixtureClaimingQualifiedContent_IsRejected()
    {
        var definition = MutateFirstEnemy(
            TestCatalogueFactory.CreateExample(),
            enemy => enemy with { Qualification = ContentQualification.Qualified });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "cannot claim qualified status");
    }

    public static void TooFewMaps_IsRejected()
    {
        var definition = MutateEra(
            TestCatalogueFactory.CreateExample(),
            TestCatalogueFactory.WwiEra,
            era => era with { Maps = era.Maps.Take(2).ToArray() });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "at least 3 qualified maps");
    }

    public static void TooFewEnemyDesigns_IsRejected()
    {
        var definition = MutateEra(
            TestCatalogueFactory.CreateExample(),
            TestCatalogueFactory.WwiEra,
            era => era with { Enemies = era.Enemies.Take(3).ToArray() });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "at least 4 qualified enemy designs");
    }

    public static void UnsafeBlueprintReference_IsRejected()
    {
        var definition = MutateFirstEnemy(
            TestCatalogueFactory.CreateExample(),
            enemy => enemy with
            {
                Blueprint = enemy.Blueprint with
                {
                    LocalId = "../outside"
                }
            });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "safe local reference");
    }

    public static void NonPositiveThreatOrPrice_IsRejected()
    {
        var zeroThreat = MutateFirstEnemy(
            TestCatalogueFactory.CreateExample(),
            enemy => enemy with { ThreatScore = 0m });
        var threatResult = CatalogueValidator.Validate(
            zeroThreat, TestCatalogueFactory.Options);
        TestAssert.ContainsError(threatResult.Errors, "threat score must be positive");

        var zeroPrice = MutateFirstEnemy(
            TestCatalogueFactory.CreateExample(),
            enemy => enemy with
            {
                CheckedPrice = new ScenarioValue(0m, TestCatalogueFactory.PriceUnit)
            });
        var priceResult = CatalogueValidator.Validate(
            zeroPrice, TestCatalogueFactory.Options);
        TestAssert.ContainsError(priceResult.Errors, "checked price must be positive");
    }

    public static void ConflictingModVersions_AreRejected()
    {
        var definition = MutateFirstEnemy(
            TestCatalogueFactory.CreateExample(),
            enemy => enemy with
            {
                Blueprint = enemy.Blueprint with
                {
                    RequiredMods = new[]
                    {
                        TestCatalogueFactory.CoreMod,
                        new ModRequirement("fixture.mod.core", "2.0")
                    }
                }
            });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "conflicting versions");
    }

    public static void BlueprintWrongEra_IsRejected()
    {
        var definition = MutateFirstEnemy(
            TestCatalogueFactory.CreateExample(),
            enemy => enemy with
            {
                Blueprint = enemy.Blueprint with
                {
                    Era = TestCatalogueFactory.InterwarEra
                }
            });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "native era does not match");
    }

    public static void InvalidSnapshotHash_IsRejected()
    {
        var definition = MutateFirstEnemy(
            TestCatalogueFactory.CreateExample(),
            enemy => enemy with
            {
                Blueprint = enemy.Blueprint with
                {
                    SnapshotHash = "not-a-hash"
                }
            });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "64 hex characters");
    }

    public static void DuplicateEra_IsRejected()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var duplicated = definition with
        {
            Eras = definition.Eras
                .Append(TestCatalogueFactory.Era(
                    definition, TestCatalogueFactory.WwiEra))
                .ToArray()
        };
        var result = CatalogueValidator.Validate(
            duplicated, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "duplicate native era");
    }

    public static void DuplicateMapId_IsRejected()
    {
        var definition = MutateEra(
            TestCatalogueFactory.CreateExample(),
            TestCatalogueFactory.WwiEra,
            era =>
            {
                var duplicate = era.Maps[1] with { MapId = era.Maps[0].MapId };
                return era with
                {
                    Maps = new[] { era.Maps[0], duplicate, era.Maps[2] }
                };
            });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "duplicate map id");
    }

    public static void DuplicateBlueprintId_IsRejected()
    {
        var definition = MutateEra(
            TestCatalogueFactory.CreateExample(),
            TestCatalogueFactory.WwiEra,
            era => era with
            {
                Enemies = new[]
                {
                    era.Enemies[0],
                    era.Enemies[1] with
                    {
                        Blueprint = era.Enemies[1].Blueprint with
                        {
                            LocalId = era.Enemies[0].Blueprint.LocalId
                        }
                    },
                    era.Enemies[2],
                    era.Enemies[3]
                }
            });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "duplicate blueprint id");
    }

    public static void WrongCheckedPriceUnit_IsRejected()
    {
        var definition = MutateFirstEnemy(
            TestCatalogueFactory.CreateExample(),
            enemy => enemy with
            {
                CheckedPrice = new ScenarioValue(
                    enemy.CheckedPrice.Value, "some.other.unit")
            });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "does not match catalogue price unit");
    }

    public static void MissingMapLayouts_IsRejected()
    {
        var definition = MutateEra(
            TestCatalogueFactory.CreateExample(),
            TestCatalogueFactory.WwiEra,
            era => era with
            {
                Maps = era.Maps
                    .Select((map, index) => index == 0
                        ? map with { SpawnLayoutIds = Array.Empty<string>() }
                        : map)
                    .ToArray()
            });
        var result = CatalogueValidator.Validate(
            definition, TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "spawn layout");
    }

    public static void InvalidRoleAndQualification_AreRejected()
    {
        var badRole = MutateFirstEnemy(
            TestCatalogueFactory.CreateExample(),
            enemy => enemy with { Role = (EnemyRole)99 });
        TestAssert.ContainsError(
            CatalogueValidator.Validate(badRole, TestCatalogueFactory.Options).Errors,
            "role value is not defined");

        var badQualification = MutateFirstEnemy(
            TestCatalogueFactory.CreateExample(),
            enemy => enemy with { Qualification = (ContentQualification)99 });
        TestAssert.ContainsError(
            CatalogueValidator.Validate(
                badQualification, TestCatalogueFactory.Options).Errors,
            "qualification value is not defined");
    }

    public static void FixtureCatalogueCannotSatisfyReleaseConstruction()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var exception = TestAssert.Throws<EncounterAvailabilityException>(
            () => new SeededEncounterCatalogue(
                definition, CatalogueOptions.Release));
        TestAssert.Equal(
            EncounterAvailabilityStatus.InvalidContent, exception.Status);
        TestAssert.ContainsError(exception.Diagnostics, "fixture");
    }

    private static CatalogueDefinition MutateFirstEnemy(
        CatalogueDefinition definition,
        Func<EnemyDesignEntry, EnemyDesignEntry> mutate)
    {
        var era = TestCatalogueFactory.Era(
            definition, TestCatalogueFactory.WwiEra);
        var changed = era with
        {
            Enemies = new[] { mutate(era.Enemies[0]) }
                .Concat(era.Enemies.Skip(1))
                .ToArray()
        };
        return TestCatalogueFactory.ReplaceEra(
            definition, TestCatalogueFactory.WwiEra, changed);
    }

    private static CatalogueDefinition MutateEra(
        CatalogueDefinition definition,
        EraReference eraReference,
        Func<CatalogueEraContent, CatalogueEraContent> mutate) =>
        TestCatalogueFactory.ReplaceEra(
            definition,
            eraReference,
            mutate(TestCatalogueFactory.Era(definition, eraReference)));
}
