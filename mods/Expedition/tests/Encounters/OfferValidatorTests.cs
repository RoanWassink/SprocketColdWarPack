using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

public static class OfferValidatorTests
{
    public static void AcceptsGeneratedSet()
    {
        var context = Create();
        var result = EncounterOfferValidator.ValidateSet(
            context.Request,
            context.Offers,
            context.Definition,
            TestCatalogueFactory.Options);
        TestAssert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    public static void RejectsWrongSupplyReward()
    {
        var context = Create();
        var changed = RehashSet(context, context.Offers
            .Select(plan => plan.Risk == EncounterRisk.Cautious
                ? plan with { SupplyReward = 3 }
                : plan));

        var errors = Validate(context, changed).Errors;
        TestAssert.ContainsError(errors, "supply reward");
    }

    public static void RejectsMissingOrDuplicateRisk()
    {
        var context = Create();
        var duplicate = new[]
        {
            context.Offers[0],
            context.Offers[0],
            context.Offers[2]
        };

        var errors = Validate(context, duplicate).Errors;
        TestAssert.ContainsError(errors, "exactly one Cautious");
        TestAssert.ContainsError(errors, "exactly one Standard");
        TestAssert.ContainsError(errors, "duplicate offer id");
    }

    public static void RejectsWrongOfferCountAndNullPlan()
    {
        var context = Create();
        TestAssert.ContainsError(
            Validate(context, context.Offers.Take(2).ToArray()).Errors,
            "exactly 3 offers");

        var withNull = new EncounterPlan?[]
        {
            context.Offers[0], null, context.Offers[2]
        };
        var errors = EncounterOfferValidator.ValidateSet(
            context.Request,
            withNull.Select(plan => plan!).ToArray(),
            context.Definition,
            TestCatalogueFactory.Options).Errors;
        TestAssert.ContainsError(errors, "offer[1] is null");
    }

    public static void RejectsEnemyTotalAboveCap()
    {
        var context = Create(budget: 600m);
        string blueprintId = context.Offers[0].Enemies[0].BlueprintId;
        var changed = RehashSet(context, context.Offers
            .Select(plan => plan.Risk == EncounterRisk.Cautious
                ? plan with
                {
                    Enemies = new[] { new EnemyUnit(blueprintId, 13) }
                }
                : plan));

        TestAssert.ContainsError(
            Validate(context, changed).Errors,
            "must be between 1 and 12");
    }

    public static void RejectsNonPositiveEnemyCount()
    {
        var context = Create();
        var changed = RehashSet(context, context.Offers
            .Select(plan => plan.Risk == EncounterRisk.Standard
                ? plan with
                {
                    Enemies = plan.Enemies
                        .Select((enemy, index) => index == 0
                            ? enemy with { Count = 0 }
                            : enemy)
                        .ToArray()
                }
                : plan));

        TestAssert.ContainsError(
            Validate(context, changed).Errors,
            "must be positive");
    }

    public static void RejectsUnresolvedAndWrongEraBlueprint()
    {
        var context = Create();
        var missing = RehashSet(context, context.Offers
            .Select(plan => plan.Risk == EncounterRisk.Cautious
                ? plan with
                {
                    Enemies = new[]
                    {
                        new EnemyUnit("fixture.blueprint.wwi.missing", 1)
                    }
                }
                : plan));
        TestAssert.ContainsError(
            Validate(context, missing).Errors,
            "unresolved for the requested native era");

        var wrongEra = RehashSet(context, context.Offers
            .Select(plan => plan.Risk == EncounterRisk.Standard
                ? plan with
                {
                    Enemies = new[]
                    {
                        new EnemyUnit("fixture.blueprint.interwar.line", 1)
                    }
                }
                : plan));
        TestAssert.ContainsError(
            Validate(context, wrongEra).Errors,
            "unresolved for the requested native era");
    }

    public static void RejectsDuplicateBlueprintEntry()
    {
        var context = Create();
        var changed = RehashSet(context, context.Offers
            .Select(plan =>
            {
                if (plan.Risk != EncounterRisk.Dangerous)
                {
                    return plan;
                }

                var enemy = plan.Enemies[0];
                return plan with
                {
                    Enemies = plan.Enemies
                        .Append(enemy with { Count = 1 })
                        .ToArray()
                };
            }));

        TestAssert.ContainsError(
            Validate(context, changed).Errors,
            "duplicate blueprint");
    }

    public static void RejectsMissingRequiredModUnion()
    {
        var context = Create();
        var definition = context.Definition;
        var era = TestCatalogueFactory.Era(
            definition, TestCatalogueFactory.WwiEra);
        var map = era.Maps.Single(entry => entry.MapId == "fixture.map.wwi.a");
        var line = era.Enemies.Single(
            entry => entry.Blueprint.LocalId == "fixture.blueprint.wwi.line");

        var changed = RehashSet(context, context.Offers
            .Select(plan => plan.Risk == EncounterRisk.Cautious
                ? plan with
                {
                    MapId = map.MapId,
                    SpawnLayoutId = map.SpawnLayoutIds[0],
                    Enemies = new[]
                    {
                        new EnemyUnit(line.Blueprint.LocalId, 1)
                    },
                    RequiredMods = Array.Empty<ModRequirement>()
                }
                : plan));

        TestAssert.ContainsError(
            Validate(context, changed).Errors,
            "missing from the plan union");
    }

    public static void RejectsExtraModOutsideUnion()
    {
        var context = Create();
        var changed = RehashSet(context, context.Offers
            .Select(plan => plan.Risk == EncounterRisk.Standard
                ? plan with
                {
                    RequiredMods = plan.RequiredMods
                        .Append(new ModRequirement("fixture.mod.extra", "1.0"))
                        .ToArray()
                }
                : plan));

        TestAssert.ContainsError(
            Validate(context, changed).Errors,
            "not part of the selected content union");
    }

    public static void RejectsUnknownMapAndLayout()
    {
        var context = Create();
        var badMap = RehashSet(context, context.Offers
            .Select(plan => plan.Risk == EncounterRisk.Cautious
                ? plan with { MapId = "fixture.map.unknown" }
                : plan));
        TestAssert.ContainsError(
            Validate(context, badMap).Errors,
            "is not a resolved map");

        var badLayout = RehashSet(context, context.Offers
            .Select(plan => plan.Risk == EncounterRisk.Standard
                ? plan with { SpawnLayoutId = "fixture.layout.unknown" }
                : plan));
        TestAssert.ContainsError(
            Validate(context, badLayout).Errors,
            "is not part of map");
    }

    public static void RejectsWrongRoundAndCatalogueVersion()
    {
        var context = Create();
        var wrongRound = RehashSet(context, context.Offers
            .Select(plan => plan with { Round = plan.Round + 1 }));
        TestAssert.ContainsError(
            Validate(context, wrongRound).Errors,
            "does not match request round");

        var wrongVersion = RehashSet(context, context.Offers
            .Select(plan => plan with
            {
                CatalogueVersion = "another.catalogue"
            }));
        TestAssert.ContainsError(
            Validate(context, wrongVersion).Errors,
            "catalogue version does not match");
    }

    public static void RejectsThreatAboveTarget()
    {
        var context = Create(budget: 200m);
        var era = TestCatalogueFactory.Era(
            context.Definition, TestCatalogueFactory.WwiEra);
        var strongest = era.Enemies
            .OrderByDescending(entry => entry.ThreatScore)
            .First();
        var map = era.Maps.Single(
            entry => entry.MapId == context.Offers[0].MapId);
        var mods = map.RequiredMods
            .Concat(strongest.Blueprint.RequiredMods)
            .ToArray();

        var changed = RehashSet(context, context.Offers
            .Select(plan => plan.Risk == EncounterRisk.Cautious
                ? plan with
                {
                    Enemies = new[]
                    {
                        new EnemyUnit(strongest.Blueprint.LocalId, 12)
                    },
                    RequiredMods = mods
                }
                : plan));

        TestAssert.ContainsError(
            Validate(context, changed).Errors,
            "exceeds target");
    }

    public static void RejectsIdenticalChoices()
    {
        var context = Create();
        var template = context.Offers[0];
        var changed = RehashSet(context, context.Offers
            .Select(plan => plan with
            {
                MapId = template.MapId,
                SpawnLayoutId = template.SpawnLayoutId,
                Enemies = template.Enemies
            }));

        TestAssert.ContainsError(
            Validate(context, changed).Errors,
            "not meaningfully distinct");
    }

    public static void RejectsTamperedHashAndOfferId()
    {
        var context = Create();
        var badHash = context.Offers
            .Select((plan, index) => index == 0
                ? plan with { PlanHash = new string('a', 64) }
                : plan)
            .ToArray();
        TestAssert.ContainsError(
            Validate(context, badHash).Errors,
            "plan hash does not match");

        var badId = context.Offers
            .Select((plan, index) => index == 2
                ? plan with { OfferId = "enc.wrong.payload" }
                : plan)
            .ToArray();
        TestAssert.ContainsError(
            Validate(context, badId).Errors,
            "not tied to the plan payload");

        var unsafeId = context.Offers
            .Select((plan, index) => index == 2
                ? plan with { OfferId = "BAD ID" }
                : plan)
            .ToArray();
        TestAssert.ContainsError(
            Validate(context, unsafeId).Errors,
            "not a safe generated identifier");
    }

    public static void RejectsMapBelowRequiredQualification()
    {
        var context = Create();
        var result = EncounterOfferValidator.ValidateSet(
            context.Request,
            context.Offers,
            context.Definition,
            new CatalogueOptions(ContentQualification.Candidate, false));
        TestAssert.ContainsError(
            result.Errors, "below the required qualification");
    }

    public static void RejectsInvalidRequestFields()
    {
        var context = Create();
        var wrongUnit = context.Request with
        {
            AvailableBudget = new ScenarioValue(300m, "other.unit")
        };
        TestAssert.ContainsError(
            EncounterOfferValidator.ValidateSet(
                wrongUnit, context.Offers, context.Definition,
                TestCatalogueFactory.Options).Errors,
            "does not match catalogue price unit");

        var negativeSupply = context.Request with
        {
            PendingSupplyPoints = -1
        };
        TestAssert.ContainsError(
            EncounterOfferValidator.ValidateSet(
                negativeSupply, context.Offers, context.Definition,
                TestCatalogueFactory.Options).Errors,
            "cannot be negative");

        var badRound = context.Request with
        {
            Round = 0,
            Block = 0
        };
        TestAssert.ContainsError(
            EncounterOfferValidator.ValidateSet(
                badRound, context.Offers, context.Definition,
                TestCatalogueFactory.Options).Errors,
            "round must be at least one");
    }

    public static void RejectsPlanFromChangedCatalogueContent()
    {
        var context = Create();
        var era = TestCatalogueFactory.Era(
            context.Definition, TestCatalogueFactory.WwiEra);
        var changedEnemy = era.Enemies[0] with
        {
            ThreatScore = era.Enemies[0].ThreatScore + 1m
        };
        var changedEra = era with
        {
            Enemies = new[] { changedEnemy }
                .Concat(era.Enemies.Skip(1))
                .ToArray()
        };
        var changedDefinition = context.Definition with
        {
            Eras = context.Definition.Eras
                .Select(entry => entry.Era.NativeId == TestCatalogueFactory.WwiEra.NativeId
                    ? changedEra
                    : entry)
                .ToArray()
        };

        var result = EncounterOfferValidator.ValidateSet(
            context.Request,
            context.Offers,
            changedDefinition,
            TestCatalogueFactory.Options);
        TestAssert.ContainsError(result.Errors, "plan hash does not match");
        TestAssert.ContainsError(
            result.Errors, "not tied to the plan payload");
    }

    public static void ZeroRoundReturnsValidationErrorWithoutThrowing()
    {
        var context = Create();
        var request = context.Request with
        {
            Round = 0,
            Block = 0
        };

        // ValidateSet must surface the request error and must not call
        // round-dependent target helpers with an invalid round.
        var result = EncounterOfferValidator.ValidateSet(
            request,
            context.Offers,
            context.Definition,
            TestCatalogueFactory.Options);

        TestAssert.False(result.IsValid);
        TestAssert.ContainsError(result.Errors, "round must be at least one");
    }

    private static ValidationContext Create(
        int round = 1,
        long seed = 7,
        decimal budget = 400m)
    {
        var definition = TestCatalogueFactory.CreateExample();
        var request = TestCatalogueFactory.Request(
            round: round,
            seed: seed,
            runId: $"validator-{round}-{seed}",
            budget: budget);
        var offers = TestCatalogueFactory.CreateCatalogue(definition)
            .GenerateOffers(request);
        return new ValidationContext(definition, request, offers);
    }

    private static IReadOnlyList<EncounterPlan> RehashSet(
        ValidationContext context,
        IEnumerable<EncounterPlan> plans) =>
        plans
            .Select(plan => TestCatalogueFactory.Rehash(
                context.Definition, context.Request, plan))
            .ToArray();

    private static CatalogueValidationResult Validate(
        ValidationContext context,
        IReadOnlyList<EncounterPlan> plans) =>
        EncounterOfferValidator.ValidateSet(
            context.Request,
            plans,
            context.Definition,
            TestCatalogueFactory.Options);

    private sealed record ValidationContext(
        CatalogueDefinition Definition,
        OfferRequest Request,
        IReadOnlyList<EncounterPlan> Offers);
}
