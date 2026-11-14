using SprocketExpedition.Core;
using SprocketExpedition.Core.Run;

namespace RunTests;

internal static class RunFixture
{
    public const string Unit = "scenario-value";
    public const int DefaultBaseBudget = 100;

    public static IReadOnlyList<EraReference> Eras() => new[]
    {
        new EraReference("native-era-0"),
        new EraReference("native-era-1"),
        new EraReference("native-era-2"),
        new EraReference("native-era-3"),
        new EraReference("native-era-4"),
        new EraReference("native-era-5")
    };

    public static RunConfiguration Config(
        decimal baseBudget = DefaultBaseBudget,
        string unit = Unit,
        long seed = 424242,
        string runId = "run-1",
        string catalogueVersion = "catalogue-1",
        IReadOnlyList<EraReference>? eras = null,
        int startingEraIndex = 0)
    {
        return new RunConfiguration(
            runId,
            seed,
            new ScenarioValue(baseBudget, unit),
            eras ?? Eras(),
            catalogueVersion)
        {
            StartingEraIndex = startingEraIndex
        };
    }

    public static RunDirector NewRun(
        decimal baseBudget = DefaultBaseBudget,
        string unit = Unit,
        long seed = 424242,
        string runId = "run-1",
        string catalogueVersion = "catalogue-1",
        IReadOnlyList<EraReference>? eras = null,
        int startingEraIndex = 0)
    {
        var director = new RunDirector();
        Check.Accepted(director.NewRun(Config(baseBudget, unit, seed, runId,
            catalogueVersion, eras, startingEraIndex)), "new run");
        return director;
    }

    public static RunSnapshot Snap(RunDirector director)
    {
        var snapshot = director.Snapshot;
        Check.NotNull(snapshot, "director snapshot");
        return snapshot!;
    }

    public static DesignQuote Quote(
        RunSnapshot snapshot,
        decimal price = 10m,
        bool nativeDesignValid = true,
        bool nativeEraValid = true,
        string? rejectionReason = null,
        string unit = Unit,
        string localId = "design-local",
        string snapshotHash = "design-snapshot-hash")
    {
        return new DesignQuote(
            new BlueprintReference(
                localId,
                snapshotHash,
                snapshot.EraSchedule[snapshot.EraIndex],
                Array.Empty<ModRequirement>()),
            new ScenarioValue(price, unit),
            nativeDesignValid,
            nativeEraValid,
            rejectionReason);
    }

    public static void Confirm(
        RunDirector director,
        decimal price = 10m,
        bool nativeDesignValid = true,
        bool nativeEraValid = true,
        string? rejectionReason = null,
        string unit = Unit,
        string localId = "design-local",
        string snapshotHash = "design-snapshot-hash")
    {
        var snapshot = Snap(director);
        Check.Accepted(director.ConfirmDesign(Quote(snapshot, price, nativeDesignValid,
            nativeEraValid, rejectionReason, unit, localId, snapshotHash)),
            "confirm design");
    }

    public static EncounterPlan Plan(
        RunSnapshot snapshot,
        EncounterRisk risk,
        string suffix = "")
    {
        var supply = risk switch
        {
            EncounterRisk.Cautious => 1,
            EncounterRisk.Standard => 2,
            EncounterRisk.Dangerous => 3,
            _ => 0
        };
        var label = risk.ToString().ToLowerInvariant();
        return new EncounterPlan(
            $"offer-{snapshot.Round}-{label}{suffix}",
            $"plan-hash-{snapshot.Round}-{label}{suffix}",
            snapshot.Round,
            $"map-{label}{suffix}",
            $"spawn-{label}{suffix}",
            snapshot.EraSchedule[snapshot.EraIndex],
            risk,
            new[] { new EnemyUnit($"enemy-{label}{suffix}", 1) },
            supply,
            Array.Empty<ModRequirement>(),
            snapshot.CatalogueVersion);
    }

    public static IReadOnlyList<EncounterPlan> Plans(RunSnapshot snapshot,
        string suffix = "")
    {
        return new[]
        {
            Plan(snapshot, EncounterRisk.Cautious, suffix),
            Plan(snapshot, EncounterRisk.Standard, suffix),
            Plan(snapshot, EncounterRisk.Dangerous, suffix)
        };
    }

    public static EncounterPlan OfferOf(RunSnapshot snapshot, EncounterRisk risk)
    {
        foreach (var offer in snapshot.Offers)
        {
            if (offer.Risk == risk)
                return offer;
        }

        throw new CheckFailedException($"No offer with risk {risk}.");
    }

    public static void SetOffers(RunDirector director,
        IReadOnlyList<EncounterPlan>? offers = null)
    {
        var snapshot = Snap(director);
        Check.Accepted(director.SetOffers(offers ?? Plans(snapshot)), "set offers");
    }

    public static void EnsureRoundReady(RunDirector director)
    {
        var snapshot = Snap(director);
        if (snapshot.Phase == RunPhase.Building)
        {
            Confirm(director);
            snapshot = Snap(director);
        }
        if ((snapshot.Phase is (RunPhase.Building or RunPhase.ChoosingEncounter)) &&
            snapshot.Offers.Count == 0)
        {
            SetOffers(director);
            snapshot = Snap(director);
        }

        Check.Equal(RunPhase.ChoosingEncounter, snapshot.Phase,
            "round is ready for selection");
        Check.Equal(3, snapshot.Offers.Count, "round has three offers");
    }

    public static EncounterRequest StartFight(RunDirector director,
        EncounterRisk risk = EncounterRisk.Standard, bool markFighting = true)
    {
        EnsureRoundReady(director);
        var snapshot = Snap(director);
        var offer = OfferOf(snapshot, risk);
        Check.Accepted(director.SelectEncounter(offer.OfferId), "select encounter");
        Check.Accepted(director.BeginEncounter(), "begin encounter");
        var request = director.CreateEncounterRequest();
        if (markFighting)
            Check.Accepted(director.ReportCombatStarted(request.Attempt),
                "report combat started");
        return request;
    }

    public static EncounterResult Result(EncounterRequest request,
        EncounterOutcome outcome, string? detail = null) =>
        new(request.Attempt, request.Plan, outcome, detail);

    public static EncounterRequest PlayVictory(RunDirector director,
        EncounterRisk risk = EncounterRisk.Standard)
    {
        var request = StartFight(director, risk);
        Check.Accepted(
            director.AcceptResult(Result(request, EncounterOutcome.Victory)),
            "accept victory");
        return request;
    }

    public static EncounterRequest PlayLoss(RunDirector director,
        EncounterOutcome outcome = EncounterOutcome.FriendlyForceDestroyed,
        EncounterRisk risk = EncounterRisk.Standard)
    {
        var request = StartFight(director, risk);
        Check.Accepted(
            director.AcceptResult(Result(request, outcome)),
            "accept loss");
        return request;
    }

    public static void AdvanceToAwaitingEndless(RunDirector director,
        EncounterRisk risk = EncounterRisk.Standard)
    {
        for (var i = 0; i < 18; i++)
            PlayVictory(director, risk);

        var snapshot = Snap(director);
        Check.Equal(RunPhase.AwaitingEndlessChoice, snapshot.Phase,
            "eighteen victories reach endless choice");
        Check.Equal(18, snapshot.Victories, "eighteen victories recorded");
    }

    public static void AdvanceToFirstEndless(RunDirector director,
        EncounterRisk risk = EncounterRisk.Standard)
    {
        AdvanceToAwaitingEndless(director, risk);
        Check.Accepted(director.ContinueEndless(), "continue endless");
        Check.Equal(RunPhase.Building, Snap(director).Phase,
            "first endless block starts building");
    }

    public static void PlayEndlessBlock(RunDirector director,
        EncounterRisk risk = EncounterRisk.Standard)
    {
        for (var i = 0; i < 3; i++)
            PlayVictory(director, risk);
    }
}
