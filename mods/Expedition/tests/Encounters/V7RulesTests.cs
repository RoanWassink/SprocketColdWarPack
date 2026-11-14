using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;

namespace EncountersTests;

// Adventure policy 7: yellow specials, wider ambush and character boss escorts.
public static class V7RulesTests
{
    private const string Unit = TestCatalogueFactory.PriceUnit;
    private const int Eras = 4;

    public static void V7SpecialsAppearAboutAQuarterAndNeverOnBossOrTestPool()
    {
        var definition = Definition();
        var specials = 0;
        var rounds = 0;
        var presets = new HashSet<SpecialPreset>();
        for (var i = 0; i < 600; i++)
        {
            var era = i % Eras;
            var round = era * 4 + (i % 3) + 1;
            var request = Request(definition, era, round, 424242L + i * 7919L, i % 11);
            var plans = Generate(definition, request);
            rounds++;
            foreach (var plan in plans)
            {
                var special = plan.Extras?.Special;
                if (special is null || special.Preset == SpecialPreset.TacticalGroup) continue;
                specials++;
                presets.Add(special.Preset);
            }
        }
        var rate = specials / (double)rounds;
        TestAssert.True(rate >= .20 && rate <= .30,
            $"v7 special rate {rate:P1} over {rounds} rounds is not about 25%");
        TestAssert.True(presets.Contains(SpecialPreset.RatPlague)
            && presets.Contains(SpecialPreset.SteelWall)
            && presets.Contains(SpecialPreset.Crossfire),
            "all three yellow specials occur");

        for (var i = 0; i < 120; i++)
        {
            var era = i % Eras;
            var request = Request(definition, era, era * 4 + 4, 9917L + i * 31L, i % 5);
            foreach (var plan in Generate(definition, request))
            {
                var preset = plan.Extras?.Special?.Preset;
                TestAssert.True(preset is null or SpecialPreset.TacticalGroup,
                    $"boss round {era * 4 + 4} must never roll a yellow special: {preset}");
            }
        }

        var pool = Definition(withTestPool: true);
        var poolRequest = Request(pool, 0, 1, 5150L, 0) with { TestPool = true };
        foreach (var plan in Generate(pool, poolRequest))
            TestAssert.True(plan.Extras?.Special is null,
                "the test pool never receives an adventure special or formation");
    }

    public static void V7SpecialNeverUsesDangerousOnlyOnNonDangerousCards()
    {
        var definition = Definition();
        var checkedSpecial = 0;
        for (var i = 0; i < 400; i++)
        {
            var era = i % Eras;
            var request = Request(definition, era, era * 4 + (i % 3) + 1, 7717L + i * 313L, i % 7);
            foreach (var plan in Generate(definition, request))
            {
                if (plan.Risk == EncounterRisk.Dangerous) continue;
                var special = plan.Extras?.Special;
                if (special is null || special.Preset == SpecialPreset.TacticalGroup) continue;
                checkedSpecial++;
                foreach (var unit in special.Enemies)
                {
                    var design = definition.Eras[era].Enemies
                        .Single(entry => entry.Blueprint.LocalId == unit.BlueprintId);
                    TestAssert.False(DangerousDesignPolicy.IsDangerousOnly(design),
                        $"non-dangerous {plan.Risk} special used {unit.BlueprintId}");
                }
            }
        }
        TestAssert.True(checkedSpecial > 40, "enough non-dangerous specials were checked");
    }

    public static void V7SpecialPlanValidatesAndReloads()
    {
        var definition = Definition();
        var request = Request(definition, 0, 1, 9917L, 0);
        for (var roll = 0; roll < 100; roll++)
        {
            request = request with { OfferRoll = roll };
            var plans = Generate(definition, request);
            if (!plans.Any(plan => plan.Extras?.Special is { Preset: not SpecialPreset.TacticalGroup })) continue;
            var validation = AdventureOfferValidation.Validate(request, plans, definition,
                CatalogueOptions.Fixture);
            TestAssert.True(validation.IsValid, "v7 special evidence must validate: "
                + string.Join(" | ", validation.Errors));
            var json = JsonSerializer.Serialize(plans);
            var reloaded = JsonSerializer.Deserialize<EncounterPlan[]>(json);
            TestAssert.NotNull(reloaded, "saved v7 plans reload");
            TestAssert.Equal(plans.Count, reloaded!.Length);
            for (var i = 0; i < plans.Count; i++)
            {
                TestAssert.Equal(plans[i].OfferId, reloaded[i].OfferId);
                TestAssert.Equal(plans[i].PlanHash, reloaded[i].PlanHash);
                TestAssert.True(SpecialBattleRules.TryValidatePlan(reloaded[i].Extras,
                    reloaded[i].Enemies, out var error), "reloaded plan validates: " + error);
            }
            var special = plans.Select(plan => plan.Extras?.Special)
                .First(battle => battle is { Preset: not SpecialPreset.TacticalGroup })!;
            TestAssert.Equal(RewardRarity.Special, plans
                .Single(plan => plan.Extras?.Special == special).Extras!.Reward.Rarity);
            TestAssert.True(special.Duration > 0 && special.RewardMagnitude > 0m,
                "a special keeps its yellow reward duration and magnitude");
            return;
        }
        throw new TestFailureException("no v7 special found to validate");
    }

    public static void V7AmbushFrequencyWithAndWithoutFlankers()
    {
        var withFlankers = Pool(withFlankers: true);
        var withoutFlankers = Pool(withFlankers: false);
        var withFlankAmbush = 0;
        var withFlanks = 0;
        var withoutAmbush = 0;
        var withoutGroups = 0;
        for (var seed = 0; seed < 240; seed++)
        {
            var round = seed % 4 + 1;
            var with = ThematicFormationPlannerV5.Create(withFlankers, Array.Empty<EnemyUnit>(),
                500m, 0m, seed, round, seed % 5, 0, 1, 7);
            var hulls = with.Enemies.Sum(unit => unit.Count);
            var hasFlank = with.Enemies.Any(unit => unit.DeploymentRole == "Flank");
            if (hasFlank)
            {
                withFlanks++;
                if (hulls >= 2 && with.Pattern == SpecialSpawnPattern.Ambush) withFlankAmbush++;
            }

            var without = ThematicFormationPlannerV5.Create(withoutFlankers, Array.Empty<EnemyUnit>(),
                500m, 0m, seed, round, seed % 5, 0, 1, 7);
            if (without.Enemies.Sum(unit => unit.Count) >= 2)
            {
                withoutGroups++;
                if (without.Pattern == SpecialSpawnPattern.Ambush) withoutAmbush++;
            }
        }
        TestAssert.True(withFlanks >= 80, $"enough flank groups: {withFlanks}");
        TestAssert.True(withoutGroups >= 80, $"enough non-flank groups: {withoutGroups}");
        var withRate = withFlankAmbush / (double)withFlanks;
        var withoutRate = withoutAmbush / (double)withoutGroups;
        TestAssert.True(withRate >= .36 && withRate <= .64,
            $"flank ambush rate {withRate:P1} is not about 1/2");
        TestAssert.True(withoutRate >= .20 && withoutRate <= .47,
            $"no-flank ambush rate {withoutRate:P1} is not about 1/3");
    }

    public static void V7BossPlansPerCharacter()
    {
        var definition = Definition();
        for (var era = 0; era < Eras; era++)
        {
            var request = Request(definition, era, era * 4 + 4, 9917L, 0);
            var plan = Generate(definition, request).Single();
            var extras = TestAssert.NotNull(plan.Extras, "boss extras");
            var formation = extras.Special;
            var expected = (BossEscortCharacter)(era % 4);
            switch (expected)
            {
                case BossEscortCharacter.Fortress:
                    TestAssert.NotNull(formation, "Fortress escorts");
                    TestAssert.Equal(SpecialSpawnPattern.Defensive, formation!.Pattern);
                    TestAssert.True(formation.Enemies.All(unit => unit.DeploymentRole == "Center"),
                        "Fortress escorts are a compact centre group");
                    break;
                case BossEscortCharacter.Hunter:
                    TestAssert.NotNull(formation, "Hunter escorts");
                    TestAssert.Equal(SpecialSpawnPattern.Ambush, formation!.Pattern);
                    TestAssert.True(formation.Enemies
                            .Where(unit => unit.BlueprintId != extras.BossBlueprintId)
                            .All(unit => unit.DeploymentRole == "Flank"),
                        "Hunter escorts are flankers in an ambush");
                    foreach (var unit in formation.Enemies.Where(unit =>
                        unit.BlueprintId != extras.BossBlueprintId))
                    {
                        var design = definition.Eras[era].Enemies
                            .Single(entry => entry.Blueprint.LocalId == unit.BlueprintId);
                        TestAssert.Equal(EnemyRole.Recon, EnemyDesignClassifier.PlannedRole(design),
                            "Hunter escort is a light/recon design");
                    }
                    break;
                case BossEscortCharacter.Battery:
                    TestAssert.NotNull(formation, "Battery escorts");
                    TestAssert.Equal(SpecialSpawnPattern.CombinedArms, formation!.Pattern);
                    TestAssert.True(formation.Enemies.All(unit => unit.DeploymentRole == "Support"),
                        "Battery escort spawns in the deepest support lane");
                    break;
                default:
                    TestAssert.Null(formation, "Veteran keeps its existing boss minions");
                    break;
            }
            if (formation != null)
            {
                var counts = formation.Enemies.GroupBy(unit => unit.BlueprintId)
                    .ToDictionary(group => group.Key, group => group.Sum(unit => unit.Count));
                TestAssert.Equal(plan.Enemies.Count, counts.Count);
                foreach (var enemy in plan.Enemies)
                    TestAssert.Equal(enemy.Count, counts[enemy.BlueprintId],
                        "boss formation roster matches the offer");
            }
        }
    }

    private static IReadOnlyList<EncounterPlan> Generate(CatalogueDefinition definition,
        OfferRequest request) =>
        new SeededEncounterCatalogue(definition, CatalogueOptions.Fixture).GenerateOffers(request);

    private static OfferRequest Request(CatalogueDefinition definition, int era, int round,
        long seed, int roll) =>
        new("v7-run", seed, round, era, definition.Eras[era].Era,
            new ScenarioValue(500m, Unit), 0, definition.Version)
        { RulesVersion = 7, OfferRoll = roll, EnemyBudgetMultiplier = 1m };

    private static CatalogueDefinition Definition(bool withTestPool = false)
    {
        var eras = new List<CatalogueEraContent>();
        for (var e = 0; e < Eras; e++)
        {
            var era = new EraReference("v7.era." + e);
            var mod = new ModRequirement("v7.mod." + e, "1.0");
            var maps = Enumerable.Range(0, 3).Select(m => new MapEntry(era,
                $"v7.{e}.map.{m}", TestCatalogueFactory.Hash(8100 + e * 10 + m),
                new[] { $"v7.{e}.layout.{m}" }, Array.Empty<ModRequirement>(),
                ContentQualification.Fixture)).ToArray();
            var enemies = new List<EnemyDesignEntry>();
            var roles = new[]
            {
                EnemyRole.Recon, EnemyRole.Line, EnemyRole.Heavy, EnemyRole.Support,
                EnemyRole.AntiTank, EnemyRole.Line, EnemyRole.Recon, EnemyRole.Line
            };
            for (var d = 0; d < roles.Length; d++)
            {
                var price = 40m + d * 24m + e * 5m;
                var entry = new EnemyDesignEntry(
                    new BlueprintReference($"v7.{e}.d{d}", TestCatalogueFactory.Hash(8200 + e * 20 + d),
                        era, d % 3 == 0 ? new[] { mod } : Array.Empty<ModRequirement>()),
                    roles[d], price, new ScenarioValue(price, Unit), ContentQualification.Fixture);
                // The last two designs are dangerous-only; a non-dangerous
                // special must never use them.
                if (d >= 6) entry = entry with { MinimumRisk = EncounterRisk.Dangerous };
                enemies.Add(entry);
            }
            eras.Add(new CatalogueEraContent(era, maps, enemies.ToArray()));
        }
        var definition = new CatalogueDefinition("v7.fixture.catalogue.1",
            CatalogueDefinition.FixtureLabel, true, Unit, eras.ToArray());
        if (!withTestPool) return definition;
        var testEra = eras[0].Era;
        var testPool = Enumerable.Range(0, 4).Select(i => new TestPoolDesign(
            "Test tank " + i,
            new EnemyDesignEntry(
                new BlueprintReference($"v7.test.{i}", TestCatalogueFactory.Hash(9000 + i),
                    testEra, Array.Empty<ModRequirement>()),
                EnemyRole.Line, 100m + i, new ScenarioValue(100m + i, Unit),
                ContentQualification.Fixture))).ToArray();
        return definition with { TestPool = testPool };
    }

    private static EnemyDesignEntry[] Pool(bool withFlankers)
    {
        var era = new EraReference("v7.ambush.era");
        var list = new List<EnemyDesignEntry>();
        for (var i = 0; i < 4; i++)
            list.Add(Design(era, "center" + i, EnemyRole.Line, 60m + i * 5m));
        if (withFlankers)
            for (var i = 0; i < 4; i++)
                list.Add(Design(era, "recon" + i, EnemyRole.Recon, 55m + i * 4m));
        for (var i = 0; i < 2; i++)
            list.Add(Design(era, "support" + i, EnemyRole.Support, 70m + i * 5m));
        return list.ToArray();
    }

    private static EnemyDesignEntry Design(EraReference era, string id, EnemyRole role, decimal price) =>
        new(new BlueprintReference("v7.ambush." + id, TestCatalogueFactory.Hash(9500 + id.Length * 7 + (int)price),
            era, Array.Empty<ModRequirement>()), role, price, new ScenarioValue(price, Unit),
            ContentQualification.Fixture);
}
