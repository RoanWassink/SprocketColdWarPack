using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;

namespace EncountersTests;

public static class DifficultyAndAmbushTests
{
    private static readonly EraReference Era = new("fixture.era.policy5");
    private const string Version = "fixture.policy5.defence.1";

    public static void V5DifficultyRaisesRealSpendWithoutChangingPlayerBudget()
    {
        var definition = Definition();
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        decimal Spend(IReadOnlyList<EncounterPlan> plans) =>
            plans.Sum(plan => plan.Enemies.Sum(enemy =>
                Pool().Single(design => design.Blueprint.LocalId == enemy.BlueprintId)
                    .CheckedPrice!.Value * enemy.Count));

        var baseline = Request(round: 5, multiplier: 1.0m, progression: false, budget: 700m);
        var harder = Request(round: 5, multiplier: 1.5m, progression: true, budget: 700m);
        var low = new SeededEncounterCatalogue(definition, options).GenerateOffers(baseline);
        var high = new SeededEncounterCatalogue(definition, options).GenerateOffers(harder);

        TestAssert.Equal(baseline.AvailableBudget.Value, harder.AvailableBudget.Value,
            "the player's purchasing budget must not be multiplied");
        TestAssert.True(Spend(high) > Spend(low),
            $"harder settings must raise real checked enemy spend: {Spend(low)} vs {Spend(high)}");
        foreach (var plan in high)
        {
            var extras = TestAssert.NotNull(plan.Extras, "policy 5 keeps a coherent formation");
            TestAssert.True(extras.Special is not null, "policy 5 keeps a coherent formation");
            TestAssert.True(extras.EnemyBudgetMultiplier >= 1.5m,
                "the pinned multiplier records the harder generation");
            TestAssert.True(extras.EnemyBudgetTarget is { } target && target > 0m,
                "the honest scaled target is pinned on policy-5 cards");
            TestAssert.True(SpecialBattleRules.TryValidatePlan(extras, plan.Enemies, out var error),
                error);
        }

        var progressionWins = Spend(new SeededEncounterCatalogue(definition, options)
            .GenerateOffers(Request(round: 5, multiplier: 1.5m, progression: true, budget: 700m)));
        var none = Spend(new SeededEncounterCatalogue(definition, options)
            .GenerateOffers(Request(round: 5, multiplier: 1.5m, progression: false, budget: 700m)));
        TestAssert.True(progressionWins >= none,
            "era progression must not reduce the achievable checked-price target");
    }

    public static void V5ThemesAndPatternsVaryAcrossRounds()
    {
        var definition = Definition();
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var families = new HashSet<string>(StringComparer.Ordinal);
        var patterns = new HashSet<SpecialSpawnPattern>();
        foreach (var round in new[] { 1, 2, 3, 5, 6, 7, 9, 10, 11, 13, 14, 15 })
        {
            var request = Request(round, multiplier: 1.5m, progression: true, budget: 700m);
            var offers = new SeededEncounterCatalogue(definition, options).GenerateOffers(request);
            var costs = new List<decimal>();
            foreach (var plan in offers)
            {
                var special = TestAssert.NotNull(plan.Extras?.Special,
                    "policy 5 must always expose its formation theme");
                patterns.Add(special.Pattern);
                var centre = special.Enemies.First(unit => unit.DeploymentRole == "Center");
                var design = Pool().Single(entry => entry.Blueprint.LocalId == centre.BlueprintId);
                TestAssert.True(design.Role is EnemyRole.Line or EnemyRole.Heavy,
                    "the centre is a medium/heavy-line design, never a Recon design");
                var family = design.FormationFamily;
                if (!string.IsNullOrWhiteSpace(family)) families.Add(family);
                TestAssert.True(plan.Enemies.Sum(enemy => enemy.Count) <= EncounterRules.MaxEnemies);
                costs.Add(plan.Enemies.Sum(enemy => Pool()
                    .Single(entry => entry.Blueprint.LocalId == enemy.BlueprintId)
                    .CheckedPrice!.Value * enemy.Count));
            }
            TestAssert.True(costs[0] < costs[1] && costs[1] < costs[2],
                $"risk costs must strictly increase in round {round}: {string.Join(",", costs)}");
        }

        TestAssert.True(families.Count > 1,
            "deterministic round/seed variation must change the real centre family: "
            + string.Join(",", families));
        TestAssert.True(patterns.Count > 1,
            "some, not all, mixed offers use the ambush pattern: "
            + string.Join(",", patterns));
    }

    public static void AmbushUsesCentreAndReconOnBothWideFlanks()
    {
        var definition = Definition();
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        SpecialBattle? ambush = null;
        for (var round = 1; round <= 16 && ambush is null; round++)
        {
            if (round % 4 == 0) continue;
            var offers = new SeededEncounterCatalogue(definition, options)
                .GenerateOffers(Request(round, 1.5m, true, 700m));
            ambush = offers.Select(plan => plan.Extras?.Special)
                .FirstOrDefault(special => special?.Pattern == SpecialSpawnPattern.Ambush);
        }

        var ambushBattle = TestAssert.NotNull(ambush, "some mixed policy-5 offers must be ambushes");
        var units = ambushBattle.Enemies;
        var centre = units.Where(unit => unit.DeploymentRole == "Center").ToArray();
        var flank = units.Where(unit => unit.DeploymentRole == "Flank").ToArray();
        TestAssert.True(centre.Length > 0, "ambush retains a real centre group");
        TestAssert.True(flank.Sum(unit => unit.Count) >= 2,
            "ambush places recon on both wide side bearings");
        foreach (var unit in centre)
            TestAssert.True(Pool().Single(design => design.Blueprint.LocalId == unit.BlueprintId)
                .Role is EnemyRole.Line or EnemyRole.Heavy, "centre units are medium/heavy designs");
        foreach (var unit in flank)
            TestAssert.Equal(EnemyRole.Recon, Pool()
                .Single(design => design.Blueprint.LocalId == unit.BlueprintId).Role);
        TestAssert.Contains("wide side bearings", SpecialBattleRules.DescribeIntent(ambushBattle));
        TestAssert.False(ambushBattle.Title.Contains("light flanks", StringComparison.Ordinal),
            "policy 5 advertises recon flanks, not an unverified light-tank claim");
    }

    public static void V5RefinesPinnedAecNameMetadataWithoutMutatingCatalogue()
    {
        var definition = Definition();
        var era = definition.Eras.Single();
        var pinnedAec = era.Enemies.Single(enemy => enemy.Blueprint.LocalId == "p5.aec") with
        {
            Role = EnemyRole.Line,
            FormationFamily = "AEC Mk II"
        };
        var pinned = definition with
        {
            Eras = new[]
            {
                era with
                {
                    Enemies = era.Enemies.Select(enemy =>
                        enemy.Blueprint.LocalId == "p5.aec" ? pinnedAec : enemy).ToArray()
                }
            }
        };
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var offers = new SeededEncounterCatalogue(pinned, options)
            .GenerateOffers(Request(round: 2, multiplier: 1.5m, progression: true, budget: 700m));
        var sawAec = false;
        foreach (var plan in offers)
        {
            var special = plan.Extras!.Special!;
            foreach (var unit in special.Enemies.Where(unit => unit.BlueprintId == "p5.aec"))
            {
                sawAec = true;
                TestAssert.Equal("Flank", unit.DeploymentRole,
                    "a pinned AEC full-name family is a recon flanker in a new policy-5 offer");
            }
            TestAssert.False(special.Enemies.Any(unit => unit.BlueprintId == "p5.aec"
                    && unit.DeploymentRole == "Center"),
                "the pinned AEC metadata must not dominate the centre");
        }
        TestAssert.True(sawAec, "the refined AEC was actually selected as a flanker");
        TestAssert.Equal(EnemyRole.Line, pinnedAec.Role,
            "policy-5 planning must not rewrite the pinned catalogue role");
        TestAssert.Equal("AEC Mk II", pinnedAec.FormationFamily,
            "policy-5 planning must not rewrite the pinned family metadata");
    }

    public static void V4GenerationIgnoresNewPolicySettings()
    {
        var definition = Definition();
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var pinned = Request(round: 3, multiplier: 1.2m, progression: false, budget: 600m) with
        {
            RulesVersion = 4,
            OfferRoll = 2,
            Block = 0
        };
        var changedGlobal = pinned with { EraProgressionEnabled = true };
        var first = new SeededEncounterCatalogue(definition, options).GenerateOffers(pinned);
        var second = new SeededEncounterCatalogue(definition, options).GenerateOffers(changedGlobal);

        TestAssert.Equal(Describe(first), Describe(second),
            "v4 producers must not consult the policy-5 era-progression setting");
        foreach (var plan in first)
        {
            TestAssert.Equal(4, plan.Extras!.RulesVersion);
            TestAssert.True(plan.Extras.EnemyBudgetTarget is null,
                "legacy v4 plans do not gain new optional target metadata");
        }
    }

    public static void V5EraProgressionRaisesTheRealTargetAndSpend()
    {
        var eras = new[]
        {
            new EraReference("fixture.policy5.0"), new EraReference("fixture.policy5.1"),
            new EraReference("fixture.policy5.2"), new EraReference("fixture.policy5.3"),
            new EraReference("fixture.policy5.4"), new EraReference("fixture.policy5.5")
        };
        var definition = new CatalogueDefinition(Version, CatalogueDefinition.FixtureLabel, true,
            TestCatalogueFactory.PriceUnit,
            eras.Select((era, index) => new CatalogueEraContent(era,
                new[]
                {
                    new MapEntry(era, "fixture.policy5." + index + ".a",
                        TestCatalogueFactory.Hash(800 + index * 3),
                        new[] { "fixture.policy5.layout." + index + ".a" },
                        Array.Empty<ModRequirement>(), ContentQualification.Fixture),
                    new MapEntry(era, "fixture.policy5." + index + ".b",
                        TestCatalogueFactory.Hash(801 + index * 3),
                        new[] { "fixture.policy5.layout." + index + ".b" },
                        Array.Empty<ModRequirement>(), ContentQualification.Fixture),
                    new MapEntry(era, "fixture.policy5." + index + ".c",
                        TestCatalogueFactory.Hash(802 + index * 3),
                        new[] { "fixture.policy5.layout." + index + ".c" },
                        Array.Empty<ModRequirement>(), ContentQualification.Fixture)
                }, Pool(era))).ToArray());
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var without = Request(round: 21, multiplier: 1.5m, progression: false, budget: 700m)
            with { Era = eras[5] };
        var with = without with { EraProgressionEnabled = true };
        var low = new SeededEncounterCatalogue(definition, options).GenerateOffers(without);
        var high = new SeededEncounterCatalogue(definition, options).GenerateOffers(with);
        var lowTarget = low.Single(plan => plan.Risk == EncounterRisk.Standard)
            .Extras!.EnemyBudgetTarget!.Value;
        var highTarget = high.Single(plan => plan.Risk == EncounterRisk.Standard)
            .Extras!.EnemyBudgetTarget!.Value;
        TestAssert.True(highTarget > lowTarget,
            $"era progression must raise the real target: {lowTarget} vs {highTarget}");
        decimal Spend(IReadOnlyList<EncounterPlan> plans) => plans.Sum(plan =>
            plan.Enemies.Sum(enemy => Pool().Single(design =>
                design.Blueprint.LocalId == enemy.BlueprintId).CheckedPrice!.Value * enemy.Count));
        TestAssert.True(Spend(high) > Spend(low),
            "later-era progression must raise achievable real checked-price spend");
    }

    public static void V5PinnedEconomyRestoresAfterSettingChange()
    {
        var definition = Definition();
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var request = Request(round: 6, multiplier: 1.5m, progression: true, budget: 700m);
        var offers = new SeededEncounterCatalogue(definition, options).GenerateOffers(request);

        var pinned = request with
        {
            EnemyBudgetMultiplier = 1.5m,
            EraProgressionEnabled = true
        };
        var restored = new SeededEncounterCatalogue(definition, options)
            .RestoreOffers(pinned, offers.Reverse().ToArray());
        TestAssert.True(restored.IsSuccess, string.Join(" | ", restored.Diagnostics));

        // A later global setting (2.0) must not silently rehash or accept the
        // saved offer set under a different economy.
        var changed = pinned with { EnemyBudgetMultiplier = 2.0m };
        var rejected = new SeededEncounterCatalogue(definition, options)
            .RestoreOffers(changed, offers);
        TestAssert.Equal(EncounterAvailabilityStatus.InvalidPersistedOffers, rejected.Status);
    }

    public static void V5RetryAndReversedPoolAreDeterministic()
    {
        var definition = Definition();
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var request = Request(round: 10, multiplier: 2.0m, progression: true, budget: 900m);
        var first = new SeededEncounterCatalogue(definition, options).GenerateOffers(request);
        var era = definition.Eras.Single();
        var reversed = definition with
        {
            Eras = new[]
            {
                era with { Enemies = era.Enemies.Reverse().ToArray() }
            }
        };
        var second = new SeededEncounterCatalogue(reversed, options).GenerateOffers(request);
        TestAssert.Equal(Describe(first), Describe(second),
            "policy-5 generation must not depend on catalogue input list order");
    }

    private static OfferRequest Request(int round, decimal multiplier, bool progression,
        decimal budget) => new(
            "policy5-" + round + "-" + multiplier,
            9917L,
            round,
            (round - 1) / 4,
            Era,
            new ScenarioValue(budget, TestCatalogueFactory.PriceUnit),
            0,
            Version)
        {
            RulesVersion = 5,
            OfferRoll = 0,
            EnemyBudgetMultiplier = multiplier,
            EraProgressionEnabled = progression
        };

    private static string Describe(IReadOnlyList<EncounterPlan> plans) =>
        string.Join("|", plans.Select(plan =>
            plan.OfferId + ":" + plan.PlanHash + ":"
            + string.Join(",", plan.Extras!.Special!.Enemies.Select(unit =>
                $"{unit.BlueprintId}x{unit.Count}:{unit.DeploymentRole}"))
            + ":" + plan.Extras.Special.Pattern));

    private static EnemyDesignEntry[] Pool(EraReference? era = null)
    {
        var target = era ?? Era;
        return new[]
        {
            Design(target, "p5.leopard", EnemyRole.Line, 100m, "Leopard"),
            Design(target, "p5.abrams", EnemyRole.Line, 120m, "Abrams"),
            Design(target, "p5.centurion", EnemyRole.Line, 140m, "Centurion"),
            Design(target, "p5.panther", EnemyRole.Line, 160m, "Panther"),
            Design(target, "p5.churchill", EnemyRole.Heavy, 180m, "Churchill"),
            Design(target, "p5.heavy", EnemyRole.Heavy, 200m, "Heavy Tank"),
            Design(target, "p5.aec", EnemyRole.Recon, 40m, "AEC"),
            Design(target, "p5.scout", EnemyRole.Recon, 30m, "Light"),
            Design(target, "p5.support", EnemyRole.Support, 60m, "Support")
        };
    }

    private static EnemyDesignEntry Design(EraReference era, string id, EnemyRole role,
        decimal price, string family) =>
        new(new BlueprintReference(id, TestCatalogueFactory.Hash(id.Length * 13 + (int)role + (int)price),
                era, Array.Empty<ModRequirement>()),
            role, price, new ScenarioValue(price, TestCatalogueFactory.PriceUnit),
            ContentQualification.Fixture)
        {
            FormationFamily = family
        };

    private static CatalogueDefinition Definition()
    {
        var maps = new[]
        {
            Map("a", 1), Map("b", 2), Map("c", 3)
        };
        return new CatalogueDefinition(Version, CatalogueDefinition.FixtureLabel, true,
            TestCatalogueFactory.PriceUnit,
            new[] { new CatalogueEraContent(Era, maps, Pool()) });
    }

    private static MapEntry Map(string suffix, int seed) => new(
        Era,
        "fixture.policy5.map." + suffix,
        TestCatalogueFactory.Hash(700 + seed),
        new[] { "fixture.policy5.layout." + suffix },
        Array.Empty<ModRequirement>(),
        ContentQualification.Fixture);
}
