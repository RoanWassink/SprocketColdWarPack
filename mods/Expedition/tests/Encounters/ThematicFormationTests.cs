using System.Text;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;

namespace EncountersTests;

public static class ThematicFormationTests
{
    private static readonly EraReference Era = new("fixture.era.themed");

    public static void ExactThreeCentreTwoFlanksWhenAffordable()
    {
        var pool = ThemedPool();
        var battle = ThematicFormationPlanner.Create(pool,
            new[] { new EnemyUnit("themed.heavy", 1) }, 340m, 0m, seed: 7, round: 1, roll: 0);

        var units = battle.Enemies.ToDictionary(unit => unit.BlueprintId, StringComparer.Ordinal);
        TestAssert.Equal(3, units["themed.leopard"].Count);
        TestAssert.Equal("Center", units["themed.leopard"].DeploymentRole);
        TestAssert.Equal(2, units["themed.scout"].Count);
        TestAssert.Equal("Flank", units["themed.scout"].DeploymentRole);
        TestAssert.False(units.ContainsKey("themed.support"), "support must not be invented into a full-budget group");
        TestAssert.Equal(5, battle.Enemies.Sum(unit => unit.Count));
        TestAssert.Equal(340m, Cost(battle, pool));
        TestAssert.Contains("Leopard", battle.Title);
        TestAssert.Contains("light flanks", battle.Title);
        TestAssert.Equal(SpecialSpawnPattern.CombinedArms, battle.Pattern);
    }

    public static void AddsSupportGroupWhenBudgetAllows()
    {
        var pool = ThemedPool();
        var battle = ThematicFormationPlanner.Create(pool,
            new[] { new EnemyUnit("themed.heavy", 1) }, 390m, 0m, seed: 8, round: 2, roll: 0);
        var units = battle.Enemies.ToDictionary(unit => unit.BlueprintId, StringComparer.Ordinal);

        TestAssert.Equal(3, units["themed.leopard"].Count);
        TestAssert.Equal(2, units["themed.scout"].Count);
        TestAssert.Equal(1, units["themed.support"].Count);
        TestAssert.Equal("Support", units["themed.support"].DeploymentRole);
        TestAssert.Equal(390m, Cost(battle, pool));
        TestAssert.Contains("support", battle.Title);
    }

    public static void DoesNotInferScoutsFromLowPrice()
    {
        var pool = new[]
        {
            Design("themed.cheap", EnemyRole.Line, 20m, "Cheap"),
            Design("themed.expensive", EnemyRole.Line, 100m, "Expensive")
        };
        var battle = ThematicFormationPlanner.Create(pool,
            new[] { new EnemyUnit("themed.cheap", 5) }, 100m, 0m, seed: 9, round: 3, roll: 0);

        TestAssert.True(battle.Enemies.All(unit => unit.DeploymentRole == "Center"),
            "a cheap Line design must stay a centre design, never a light flanker");
        TestAssert.False(battle.Title.Contains("flank", StringComparison.OrdinalIgnoreCase),
            "the title must not promise flanks that do not exist");
        TestAssert.True(battle.Enemies.Sum(unit => unit.Count) <= EncounterRules.MaxEnemies);
    }

    public static void FallbackKeepsExactLegitimateRoster()
    {
        var pool = new[]
        {
            Design("themed.primary", EnemyRole.Line, 300m, "Primary"),
            Design("themed.second", EnemyRole.Line, 180m, "Second"),
            Design("themed.scout", EnemyRole.Recon, 20m, "Light")
        };
        var original = new[]
        {
            new EnemyUnit("themed.primary", 1),
            new EnemyUnit("themed.second", 1)
        };
        var battle = ThematicFormationPlanner.Create(pool, original, 500m, 450m,
            seed: 10, round: 4, roll: 0);

        TestAssert.Equal(
            string.Join(",", original.Select(unit => $"{unit.BlueprintId}x{unit.Count}")),
            string.Join(",", battle.Enemies.Select(unit => $"{unit.BlueprintId}x{unit.Count}")));
        TestAssert.Equal("Center", battle.Enemies.Single(unit => unit.BlueprintId == "themed.primary").DeploymentRole);
        TestAssert.Equal("Center", battle.Enemies.Single(unit => unit.BlueprintId == "themed.second").DeploymentRole);
        TestAssert.Equal(SpecialPreset.TacticalGroup, battle.Preset);
        TestAssert.Equal(SpecialSpawnPattern.CombinedArms, battle.Pattern);
    }

    public static void FallbackPreservesExplicitSupportRole()
    {
        var pool = ThemedPool();
        var original = new[] { new EnemyUnit("themed.support", 1) };
        var battle = ThematicFormationPlanner.Create(pool, original, 50m, 0m,
            seed: 13, round: 5, roll: 0);

        TestAssert.Equal(1, battle.Enemies.Count);
        TestAssert.Equal("themed.support", battle.Enemies[0].BlueprintId);
        TestAssert.Equal(1, battle.Enemies[0].Count);
        TestAssert.Equal("Support", battle.Enemies[0].DeploymentRole,
            "an authored Support role must survive the exact-roster fallback");
    }

    public static void PrimarySupportDesignKeepsSupportRole()
    {
        foreach (var role in new[] { EnemyRole.Support, EnemyRole.AntiTank })
        {
            var pool = new[] { Design("themed.primary", role, 50m, "Support") };
            var battle = ThematicFormationPlanner.Create(pool,
                new[] { new EnemyUnit("themed.primary", 1) }, 50m, 0m,
                seed: 14, round: 6, roll: 0);
            TestAssert.Equal(1, battle.Enemies.Count);
            TestAssert.Equal("Support", battle.Enemies[0].DeploymentRole,
                "an authored Support/AntiTank primary must not be relabelled Line");
        }
    }

    public static void ReconOnlyPoolKeepsAuthoredFlankRole()
    {
        var pool = new[] { Design("themed.scout", EnemyRole.Recon, 40m, "Scout") };
        var original = new[] { new EnemyUnit("themed.scout", 2) };
        var battle = ThematicFormationPlanner.Create(pool, original, 100m, 0m,
            seed: 11, round: 3, roll: 0);

        TestAssert.Equal(1, battle.Enemies.Count);
        TestAssert.Equal(2, battle.Enemies[0].Count);
        TestAssert.Equal("Flank", battle.Enemies[0].DeploymentRole,
            "a Recon-only pool must not relabel the authored Recon unit as a centre");
        TestAssert.Contains("light flanks", battle.Title);
        TestAssert.False(battle.Title.Contains("support", StringComparison.OrdinalIgnoreCase),
            "a Recon-only fallback must not claim support it does not have");
    }

    public static void FallbackTitleUsesCentreFamily()
    {
        var pool = new[]
        {
            Design("themed.stuart", EnemyRole.Support, 20m, "Stuart"),
            Design("themed.leopard.centre", EnemyRole.Line, 180m, "Leopard")
        };
        var original = new[]
        {
            new EnemyUnit("themed.stuart", 2),
            new EnemyUnit("themed.leopard.centre", 1)
        };
        var battle = ThematicFormationPlanner.Create(pool, original, 220m, 200m,
            seed: 17, round: 9, roll: 0);

        TestAssert.Equal("themed.stuart", battle.Enemies[0].BlueprintId);
        TestAssert.Equal("Support", battle.Enemies[0].DeploymentRole);
        TestAssert.Equal("Center", battle.Enemies[1].DeploymentRole);
        TestAssert.Contains("Leopard", battle.Title);
        TestAssert.False(battle.Title.Contains("Stuart", StringComparison.Ordinal),
            "fallback title must use the centre family, not the first support family");
    }

    public static void FallbackRejectsOverBudgetOriginal()
    {
        var original = new[]
        {
            new EnemyUnit("themed.heavy", 1),
            new EnemyUnit("themed.scout", 2)
        };
        var exception = TestAssert.Throws<InvalidOperationException>(
            () => ThematicFormationPlanner.Create(ThemedPool(), original, 90m, 0m,
                seed: 10, round: 4, roll: 0));
        TestAssert.Contains("exceeds the checked target budget", exception.Message);
    }

    public static void FallbackRejectsMissingOrInvalidEntries()
    {
        var missing = TestAssert.Throws<InvalidOperationException>(
            () => ThematicFormationPlanner.Create(ThemedPool(),
                new[] { new EnemyUnit("themed.missing", 1) }, 90m, 0m,
                seed: 15, round: 7, roll: 0));
        TestAssert.Contains("missing or has no valid checked price", missing.Message);

        var zero = TestAssert.Throws<InvalidOperationException>(
            () => ThematicFormationPlanner.Create(ThemedPool(),
                new[] { new EnemyUnit("themed.leopard", 0) }, 90m, 0m,
                seed: 16, round: 8, roll: 0));
        TestAssert.Contains("invalid unit", zero.Message);
    }

    public static void NeverDropsBelowPreviousRiskCost()
    {
        var exception = TestAssert.Throws<InvalidOperationException>(
            () => ThematicFormationPlanner.Create(ThemedPool(),
                new[] { new EnemyUnit("themed.leopard", 1) }, 100m, 100m,
                seed: 11, round: 5, roll: 0));

        TestAssert.Contains("strict risk progression", exception.Message);
    }

    public static void SameRetryAndReversedPoolStayIdentical()
    {
        var first = ThematicFormationPlanner.Create(ThemedPool(),
            new[] { new EnemyUnit("themed.heavy", 1) }, 340m, 0m, seed: 12, round: 6, roll: 3);
        var second = ThematicFormationPlanner.Create(ThemedPool().Reverse().ToArray(),
            new[] { new EnemyUnit("themed.heavy", 1) }, 340m, 0m, seed: 12, round: 6, roll: 3);

        TestAssert.Equal(first.Title, second.Title);
        TestAssert.Equal(Describe(first), Describe(second));
    }

    public static void EveryRosterUnitExistsInPinnedPool()
    {
        var pool = ThemedPool();
        var ids = pool.Select(design => design.Blueprint.LocalId).ToHashSet(StringComparer.Ordinal);
        for (var seed = 0; seed < 32; seed++)
        {
            var battle = ThematicFormationPlanner.Create(pool,
                new[] { new EnemyUnit("themed.heavy", 1) }, 390m, 0m, seed, 7, 0);
            TestAssert.True(battle.Enemies.All(unit => ids.Contains(unit.BlueprintId)),
                "thematic planner must never invent a model");
            TestAssert.InRange(1, EncounterRules.MaxEnemies, battle.Enemies.Sum(unit => unit.Count));
        }
    }

    public static void V4OffersAreCoherentAndRiskCostsIncrease()
    {
        var definition = CostBased(TestCatalogueFactory.CreateExample());
        var request = TestCatalogueFactory.Request(
            round: 3, seed: 21, runId: "themed-v4", budget: 700m)
            with { RulesVersion = 4, OfferRoll = 3 };
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var offers = new SeededEncounterCatalogue(definition, options).GenerateOffers(request);
        var era = TestCatalogueFactory.Era(definition, TestCatalogueFactory.WwiEra);
        var costs = new List<decimal>();

        foreach (var offer in offers)
        {
            var special = offer.Extras?.Special;
            TestAssert.NotNull(special, "every normal v4 card has a formation theme");
            TestAssert.Equal(SpecialPreset.TacticalGroup, special!.Preset);
            TestAssert.Equal(SpecialSpawnPattern.CombinedArms, special.Pattern);
            TestAssert.False(string.IsNullOrWhiteSpace(special.Title));
            TestAssert.True(special.Enemies.All(unit =>
                unit.DeploymentRole is "Center" or "Flank" or "Support"),
                "roles are explicit authored categories");
            TestAssert.True(SpecialBattleRules.TryValidatePlan(offer.Extras, offer.Enemies, out var error),
                error);
            TestAssert.True(offer.Extras!.Reward.Rarity != RewardRarity.Special,
                "ordinary themed offers must not masquerade as yellow special rewards");

            var cost = offer.Enemies.Sum(enemy =>
                era.Enemies.Single(design => design.Blueprint.LocalId == enemy.BlueprintId)
                    .CheckedPrice.Value * enemy.Count);
            TestAssert.True(cost > 0m);
            TestAssert.True(cost <= EncounterRules.CostThreatTarget(
                request.AvailableBudget, request.Round, offer.Risk, era.Enemies));
            costs.Add(cost);
            TestAssert.InRange(1, EncounterRules.MaxEnemies,
                offer.Enemies.Sum(enemy => enemy.Count));
        }
        TestAssert.Equal(3, costs.Count);
        TestAssert.True(costs[0] < costs[1] && costs[1] < costs[2],
            $"risk costs must increase: {string.Join(",", costs)}");

        var reversedDefinition = TestCatalogueFactory.ReplaceEra(definition,
            TestCatalogueFactory.WwiEra,
            era with { Enemies = era.Enemies.Reverse().ToArray() });
        var replay = new SeededEncounterCatalogue(reversedDefinition, options)
            .GenerateOffers(request);
        TestAssert.Equal(
            string.Join("|", offers.Select(offer => offer.OfferId + ":" + offer.PlanHash)),
            string.Join("|", replay.Select(offer => offer.OfferId + ":" + offer.PlanHash)));
    }

    public static void V4RestoreSurvivesListOrderAndRejectsRoleTamper()
    {
        var definition = CostBased(TestCatalogueFactory.CreateExample());
        var request = TestCatalogueFactory.Request(
            round: 7, seed: 31, runId: "themed-restore", budget: 750m)
            with { RulesVersion = 4, OfferRoll = 2, Block = (7 - 1) / 4 };
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var offers = new SeededEncounterCatalogue(definition, options).GenerateOffers(request);

        var restoredCatalogue = new SeededEncounterCatalogue(definition, options);
        var restore = restoredCatalogue.RestoreOffers(request, offers.Reverse().ToArray());
        TestAssert.True(restore.IsSuccess, string.Join(" | ", restore.Diagnostics));
        foreach (var offer in offers)
            TestAssert.NotNull(restoredCatalogue.Find(offer.OfferId));

        var special = offers[1].Extras!.Special!;
        var tampered = offers.Select((plan, index) => index != 1 ? plan : plan with
        {
            Extras = plan.Extras! with
            {
                Special = special with
                {
                    Enemies = special.Enemies.Select(unit => unit with
                    {
                        DeploymentRole = unit.DeploymentRole == "Center" ? "Flank" : "Center"
                    }).ToArray()
                }
            }
        }).ToArray();
        var rejected = new SeededEncounterCatalogue(definition, options)
            .RestoreOffers(request, tampered);
        TestAssert.Equal(EncounterAvailabilityStatus.InvalidPersistedOffers, rejected.Status);
    }

    public static void V3GenerationDoesNotAddNewRoleMetadata()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var foundSpecial = false;
        for (var seed = 1; seed <= 64; seed++)
        {
            var request = TestCatalogueFactory.Request(
                round: 2, seed: seed, runId: $"v3-{seed}", budget: 500m)
                with { RulesVersion = 3, OfferRoll = 0 };
            var offers = TestCatalogueFactory.CreateCatalogue(definition).GenerateOffers(request);
            foreach (var special in offers.Select(offer => offer.Extras?.Special).Where(s => s != null))
            {
                foundSpecial = true;
                TestAssert.True(special!.Enemies.All(unit => unit.DeploymentRole == null),
                    "v3 hashes/payloads must not gain optional deployment roles");
                TestAssert.False(special.Title.Contains("battlegroup", StringComparison.OrdinalIgnoreCase));
            }
        }
        TestAssert.True(foundSpecial, "the v3 fixture sweep should exercise at least one special battle");
    }

    public static void CatalogueHashIgnoresAbsentOptionalFamily()
    {
        var definition = TestCatalogueFactory.CreateExample();
        TestAssert.Equal(LegacyCatalogueHash(definition), CatalogueHasher.Compute(definition));

        var era = TestCatalogueFactory.Era(definition, TestCatalogueFactory.WwiEra);
        var withFamily = era with
        {
            Enemies = era.Enemies.Select((enemy, index) => index == 0
                ? enemy with { FormationFamily = "OptionalFamily" }
                : enemy).ToArray()
        };
        var changed = TestCatalogueFactory.ReplaceEra(
            definition, TestCatalogueFactory.WwiEra, withFamily);
        TestAssert.NotEqual(CatalogueHasher.Compute(definition), CatalogueHasher.Compute(changed));
    }

    public static void CatalogueHashIgnoresAbsentMassAndIncludesMeasuredMass()
    {
        var definition = TestCatalogueFactory.CreateExample();
        TestAssert.Equal(LegacyCatalogueHash(definition), CatalogueHasher.Compute(definition),
            "an absent optional mass must leave old catalogue hashes byte-identical");

        var era = TestCatalogueFactory.Era(definition, TestCatalogueFactory.WwiEra);
        var withMass = era with
        {
            Enemies = era.Enemies.Select((enemy, index) => index == 0
                ? enemy with { MassKg = 12_345 }
                : enemy).ToArray()
        };
        var changed = TestCatalogueFactory.ReplaceEra(
            definition, TestCatalogueFactory.WwiEra, withMass);
        TestAssert.NotEqual(CatalogueHasher.Compute(definition), CatalogueHasher.Compute(changed),
            "measured mass is part of new-catalogue identity");

        var absentAgain = era with
        {
            Enemies = era.Enemies.Select((enemy, index) => index == 0
                ? enemy with { MassKg = null }
                : enemy).ToArray()
        };
        TestAssert.Equal(CatalogueHasher.Compute(definition),
            CatalogueHasher.Compute(TestCatalogueFactory.ReplaceEra(
                definition, TestCatalogueFactory.WwiEra, absentAgain)));
    }

    public static void DescriptionsDoNotPromiseMissingFlanks()
    {
        var withFlank = new SpecialBattle(SpecialPreset.TacticalGroup, "Leopard battlegroup · light flanks",
            SpecialSpawnPattern.CombinedArms, new[]
            {
                new SpecialUnit("leopard", 3) { DeploymentRole = "Center" },
                new SpecialUnit("scout", 2) { DeploymentRole = "Flank" }
            }, "", 0m, 0);
        var noFlank = new SpecialBattle(SpecialPreset.TacticalGroup, "Leopard battlegroup · support",
            SpecialSpawnPattern.CombinedArms, new[]
            {
                new SpecialUnit("leopard", 3) { DeploymentRole = "Center" },
                new SpecialUnit("support", 1) { DeploymentRole = "Support" }
            }, "", 0m, 0);

        var flankComposition = SpecialBattleRules.DescribeComposition(withFlank);
        TestAssert.Contains("flanks 2", flankComposition);
        TestAssert.Contains("flankers work the side lanes", SpecialBattleRules.DescribeIntent(withFlank));

        var noFlankComposition = SpecialBattleRules.DescribeComposition(noFlank);
        TestAssert.False(noFlankComposition.Contains("flank", StringComparison.OrdinalIgnoreCase));
        TestAssert.False(SpecialBattleRules.DescribeIntent(noFlank)
            .Contains("flank", StringComparison.OrdinalIgnoreCase));
        TestAssert.Contains("support fire", SpecialBattleRules.DescribeIntent(noFlank));

        var noRoles = new SpecialBattle(SpecialPreset.TacticalGroup, "Legacy formation",
            SpecialSpawnPattern.CombinedArms, new[] { new SpecialUnit("legacy", 1) },
            "", 0m, 0);
        var noRoleComposition = SpecialBattleRules.DescribeComposition(noRoles);
        TestAssert.Contains("no deployment roles were recorded", noRoleComposition);
        TestAssert.False(noRoleComposition.Contains("centre", StringComparison.OrdinalIgnoreCase));

        var flankOnly = new SpecialBattle(SpecialPreset.TacticalGroup, "Flank screen",
            SpecialSpawnPattern.CombinedArms,
            new[] { new SpecialUnit("scout", 2) { DeploymentRole = "Flank" } }, "", 0m, 0);
        var flankIntent = SpecialBattleRules.DescribeIntent(flankOnly);
        TestAssert.Contains("Flankers work the side lanes", flankIntent);
        TestAssert.False(flankIntent.Contains("centre", StringComparison.OrdinalIgnoreCase),
            "flank-only intent must not assert a centre");

        var supportOnly = new SpecialBattle(SpecialPreset.TacticalGroup, "Support line",
            SpecialSpawnPattern.CombinedArms,
            new[] { new SpecialUnit("support", 1) { DeploymentRole = "Support" } }, "", 0m, 0);
        var supportIntent = SpecialBattleRules.DescribeIntent(supportOnly);
        TestAssert.Contains("Support holds depth", supportIntent);
        TestAssert.False(supportIntent.Contains("centre", StringComparison.OrdinalIgnoreCase),
            "support-only intent must not assert a centre");
    }

    public static void V4TacticalGroupRequiresExplicitRoles()
    {
        var reward = RewardRules.Create(21, 3, "v4-role-check", 1);
        var missingRole = new SpecialBattle(SpecialPreset.TacticalGroup,
            "Leopard battlegroup", SpecialSpawnPattern.CombinedArms,
            new[] { new SpecialUnit("themed.leopard", 1) }, "", 0m, 0);
        var extras = new EncounterExtras(4, 0, reward) { Special = missingRole };
        var enemies = new[] { new EnemyUnit("themed.leopard", 1) };
        TestAssert.False(SpecialBattleRules.TryValidatePlan(extras, enemies, out var error));
        TestAssert.Contains("explicit Center, Flank or Support role", error);

        var explicitRoles = missingRole with
        {
            Enemies = new[]
            {
                new SpecialUnit("themed.leopard", 1) { DeploymentRole = "Center" }
            }
        };
        TestAssert.True(SpecialBattleRules.TryValidatePlan(
            extras with { Special = explicitRoles }, enemies, out var validError), validError);

        var legacy = new EncounterExtras(3, 0, reward)
        {
            Special = missingRole with
            {
                Preset = SpecialPreset.Crossfire,
                Pattern = SpecialSpawnPattern.Split
            }
        };
        TestAssert.True(SpecialBattleRules.TryValidatePlan(legacy, enemies, out var legacyError),
            legacyError);

        var v3Tactical = new EncounterExtras(3, 0, reward) { Special = explicitRoles };
        TestAssert.False(SpecialBattleRules.TryValidatePlan(v3Tactical, enemies, out var v3TacticalError));
        TestAssert.Contains("version 4", v3TacticalError);

        var dangerousMultiplier = extras with { Special = explicitRoles, EnemyBudgetMultiplier = 2.16m };
        TestAssert.True(SpecialBattleRules.TryValidatePlan(
            dangerousMultiplier, enemies, out var multiplierError), multiplierError);
        var excessiveMultiplier = extras with { Special = explicitRoles, EnemyBudgetMultiplier = 2.71m };
        TestAssert.False(SpecialBattleRules.TryValidatePlan(excessiveMultiplier, enemies, out _));
    }

    private static EnemyDesignEntry[] ThemedPool() => new[]
    {
        Design("themed.leopard", EnemyRole.Line, 100m, "Leopard"),
        Design("themed.heavy", EnemyRole.Heavy, 180m, "Heavy"),
        Design("themed.scout", EnemyRole.Recon, 20m, "Light"),
        Design("themed.support", EnemyRole.Support, 50m, "Support")
    };

    private static EnemyDesignEntry Design(string id, EnemyRole role, decimal price, string? family) =>
        new(new BlueprintReference(id, TestCatalogueFactory.Hash(id.Length * 17 + (int)role),
                Era, Array.Empty<ModRequirement>()),
            role, price, new ScenarioValue(price, TestCatalogueFactory.PriceUnit),
            ContentQualification.Fixture)
        {
            FormationFamily = family
        };

    private static CatalogueDefinition CostBased(CatalogueDefinition definition) =>
        definition with
        {
            Eras = definition.Eras.Select(era => era with
            {
                Enemies = era.Enemies.Select(enemy => enemy with
                {
                    ThreatScore = enemy.CheckedPrice.Value,
                    CheckedPrice = new ScenarioValue(
                        enemy.CheckedPrice.Value, definition.PriceUnitId)
                }).ToArray()
            }).ToArray()
        };

    private static decimal Cost(SpecialBattle battle, IReadOnlyList<EnemyDesignEntry> pool) =>
        battle.Enemies.Sum(unit => pool.Single(design =>
            design.Blueprint.LocalId == unit.BlueprintId).CheckedPrice.Value * unit.Count);

    private static string Describe(SpecialBattle battle) => string.Join("|",
        battle.Title,
        battle.Pattern,
        string.Join(",", battle.Enemies.Select(unit =>
            $"{unit.BlueprintId}x{unit.Count}:{unit.DeploymentRole}")));

    // Replica of the pre-FormationFamily catalogue encoding. It proves that an
    // absent optional field leaves every existing content hash byte-identical.
    internal static string LegacyCatalogueHash(CatalogueDefinition definition)
    {
        var builder = new StringBuilder();
        CanonicalText.Append(builder, "catalogue/v1");
        CanonicalText.Append(builder, definition.Version);
        CanonicalText.Append(builder, definition.PriceUnitId);
        CanonicalText.Append(builder, definition.IsFixture ? 1 : 0);
        var eras = definition.Eras.OrderBy(era => era.Era.NativeId, StringComparer.Ordinal).ToArray();
        CanonicalText.Append(builder, eras.Length);
        foreach (var era in eras)
        {
            CanonicalText.Append(builder, era.Era.NativeId);
            var maps = era.Maps.OrderBy(map => map.MapId, StringComparer.Ordinal).ToArray();
            CanonicalText.Append(builder, maps.Length);
            foreach (var map in maps)
            {
                CanonicalText.Append(builder, map.Era.NativeId);
                CanonicalText.Append(builder, map.MapId);
                CanonicalText.Append(builder, map.ContentHash);
                var layouts = map.SpawnLayoutIds.OrderBy(layout => layout, StringComparer.Ordinal).ToArray();
                CanonicalText.Append(builder, layouts.Length);
                foreach (var layout in layouts) CanonicalText.Append(builder, layout);
                CanonicalText.Append(builder, (int)map.Qualification);
                CanonicalText.AppendMods(builder, map.RequiredMods);
            }

            var enemies = era.Enemies.OrderBy(enemy => enemy.Blueprint.LocalId, StringComparer.Ordinal).ToArray();
            CanonicalText.Append(builder, enemies.Length);
            foreach (var enemy in enemies)
            {
                CanonicalText.Append(builder, enemy.Blueprint.LocalId);
                CanonicalText.Append(builder, enemy.Blueprint.SnapshotHash);
                CanonicalText.Append(builder, enemy.Blueprint.Era.NativeId);
                CanonicalText.Append(builder, (int)enemy.Role);
                CanonicalText.Append(builder, enemy.ThreatScore);
                CanonicalText.Append(builder, enemy.CheckedPrice.Value);
                CanonicalText.Append(builder, enemy.CheckedPrice.UnitId);
                CanonicalText.Append(builder, (int)enemy.Qualification);
                CanonicalText.AppendMods(builder, enemy.Blueprint.RequiredMods);
            }
        }
        if (definition.BossBlueprints != null)
            foreach (var boss in definition.BossBlueprints.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                CanonicalText.Append(builder, boss.Key);
                CanonicalText.Append(builder, boss.Value);
            }
        return CanonicalText.Sha256(builder.ToString());
    }
}
