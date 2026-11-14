using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Run;

namespace RunTests;

internal static class RunDirectorTests
{
    public static void ReturnToEditorPreservesEncounterAndRejectsLateResults()
    {
        var director = RunFixture.NewRun(baseBudget: 100m);
        RunFixture.Confirm(director, price: 20m);
        var offers = RunFixture.Plans(RunFixture.Snap(director));
        Check.Accepted(director.SetOffers(offers), "offers");
        Check.Accepted(director.SelectEncounter(offers[1].OfferId), "selection");
        Check.Accepted(director.BeginEncounter(), "begin");
        var attempt = RunFixture.Snap(director).CurrentAttempt!;
        Check.Accepted(director.ReportCombatStarted(attempt), "combat");
        var before = RunFixture.Snap(director);
        Check.Ignored(director.ReturnToEditor(attempt with { Generation = attempt.Generation + 1 }), "stale refit request");
        Check.Accepted(director.ReturnToEditor(attempt), "return to editor");
        var after = RunFixture.Snap(director);
        Check.Equal(RunPhase.ChoosingEncounter, after.Phase, "editable choice phase");
        Check.Equal(before.SelectedOfferId, after.SelectedOfferId, "same selected offer");
        Check.Equal(JsonSerializer.Serialize(before.Offers), JsonSerializer.Serialize(after.Offers), "same frozen offers");
        Check.Equal(before.AvailableBudget, after.AvailableBudget, "budget unchanged");
        Check.Equal(before.Round, after.Round, "round unchanged");
        Check.Equal(before.Victories, after.Victories, "no victory");
        Check.Equal(before.Defeats, after.Defeats, "no defeat");
        Check.Equal(before.PendingSupplyPoints, after.PendingSupplyPoints, "no supply");
        Check.Equal(before.ConsumedOfferIds.Count, after.ConsumedOfferIds.Count, "offer not consumed");
        Check.True(after.CurrentAttempt == null && after.LockedPlan == null, "old attempt ended");
        var late = new EncounterResult(attempt, before.LockedPlan!, EncounterOutcome.Victory, "late callback");
        Check.Ignored(director.AcceptResult(late), "late victory after refit is ignored");
        director = RunDirector.Restore(after);
        Check.Accepted(director.ConfirmEncounterDesign(RunFixture.Quote(after, price: 25m)), "edited tank is recaptured");
        Check.Accepted(director.BeginEncounter(), "replay same offer");
        var replay = RunFixture.Snap(director);
        Check.Equal(attempt.PlanHash, replay.CurrentAttempt!.PlanHash, "exact challenge retained");
        Check.True(replay.CurrentAttempt.Generation > attempt.Generation, "new attempt generation");
        Check.Ignored(director.AcceptResult(late), "old victory cannot resolve new fight");
        Check.Accepted(director.ReportCombatStarted(replay.CurrentAttempt), "new combat");
        Check.Accepted(director.AcceptResult(new EncounterResult(replay.CurrentAttempt, replay.LockedPlan!,
            EncounterOutcome.FriendlyForceDestroyed, "actual loss")), "actual loss");
        Check.Ignored(director.ReturnToEditor(replay.CurrentAttempt), "cannot refit after resolved loss");
    }

    public static void PreEncounterUpgradeKeepsChallengeAndFreezesAtLoading()
    {
        var director = RunFixture.NewRun(baseBudget: 100m);
        Check.Accepted(director.ConfirmDesign(RunFixture.Quote(RunFixture.Snap(director), price: 20m)), "initial tank");
        var offers = RunFixture.Plans(RunFixture.Snap(director));
        Check.Accepted(director.SetOffers(offers), "three challenges");
        Check.Accepted(director.SelectEncounter(offers[1].OfferId), "chosen challenge");
        var before = RunFixture.Snap(director);
        var upgraded = RunFixture.Quote(before, price: 40m) with
        {
            Blueprint = before.ConfirmedDesign!.Blueprint with { SnapshotHash = "upgraded-tank" }
        };
        Check.Accepted(director.ConfirmEncounterDesign(upgraded), "upgrade before Play");
        var after = RunFixture.Snap(director);
        Check.Equal(before.SelectedOfferId, after.SelectedOfferId, "chosen challenge survives");
        Check.Equal(before.Offers[1].PlanHash, after.Offers[1].PlanHash, "enemy plan is unchanged");
        Check.Equal(2, after.ConfirmedDesign!.FriendlyCountIncludingPlayer, "fleet uses upgraded quote");
        Check.Equal("upgraded-tank", after.ConfirmedDesign.Blueprint.SnapshotHash, "new tank snapshot");
        Check.Rejected(director.ConfirmEncounterDesign(upgraded with
        { Cost = new ScenarioValue(101m, RunFixture.Unit) }), "unaffordable upgrade rejected");
        Check.Equal("upgraded-tank", RunFixture.Snap(director).ConfirmedDesign!.Blueprint.SnapshotHash,
            "rejected upgrade preserves tank");
        Check.Accepted(director.BeginEncounter(), "Play begins encounter");
        Check.Rejected(director.ConfirmEncounterDesign(upgraded), "no edits during native loading");
    }

    private static void AssertIgnoredWithoutStateChange(RunDirector director,
        RunSnapshot before, RunActionResult action, string message)
    {
        Check.Ignored(action, message);
        var after = RunFixture.Snap(director);
        Check.Equal(before.Phase, after.Phase, message + " phase");
        Check.Equal(before.Round, after.Round, message + " round");
        Check.Equal(before.Victories, after.Victories, message + " victories");
        Check.Equal(before.Defeats, after.Defeats, message + " defeats");
        Check.Equal(before.PendingSupplyPoints, after.PendingSupplyPoints,
            message + " pending supply");
        Check.Equal(before.AvailableBudget.Value, after.AvailableBudget.Value,
            message + " available budget");
        Check.Equal(before.GenerationCounter, after.GenerationCounter,
            message + " generation counter");
    }

    public static void NewRunStartsInBuildingWithMeasuredBase()
    {
        var director = RunFixture.NewRun(baseBudget: 100.1234567m, seed: 987654321L,
            runId: "run-explicit", catalogueVersion: "catalogue-explicit");
        var snapshot = RunFixture.Snap(director);

        Check.Equal(1, snapshot.SchemaVersion, "schema version");
        Check.Equal("run-explicit", snapshot.RunId, "run id");
        Check.Equal(987654321L, snapshot.Seed, "seed");
        Check.Equal(RunPhase.Building, snapshot.Phase, "initial phase");
        Check.Equal(1, snapshot.Round, "initial round");
        Check.Equal(0, snapshot.BlockIndex, "initial block");
        Check.Equal(0, snapshot.EraIndex, "initial era");
        Check.Equal(0, snapshot.WinsInBlock, "initial block wins");
        Check.Equal(0, snapshot.Victories, "initial victories");
        Check.Equal(0, snapshot.Defeats, "initial defeats");
        Check.Equal(100.1234567m, snapshot.InitialBaseBudget.Value,
            "measured base is preserved for later boundary calculations");
        Check.Equal(100.1234567m, snapshot.BlockBaseBudget.Value, "initial block base");
        Check.Equal(100.1234567m, snapshot.AvailableBudget.Value,
            "initial available budget");
        Check.Equal(0m, snapshot.SupplyBonus, "initial supply bonus");
        Check.Equal(0, snapshot.PendingSupplyPoints, "initial pending supply");
        Check.Equal(RunFixture.Unit, snapshot.AvailableBudget.UnitId, "cost unit");
        Check.Equal(6, snapshot.EraSchedule.Count, "native era schedule");
        Check.Equal("native-era-0", snapshot.EraSchedule[0].NativeId, "first era");
        Check.Equal(0, snapshot.Offers.Count, "no initial offers");
        Check.True(snapshot.ConfirmedDesign is null, "no initial design");
        Check.Equal(0L, snapshot.GenerationCounter, "initial generation");
    }

    public static void NewRunRejectsIncompleteConfiguration()
    {
        var director = new RunDirector();

        Check.Rejected(director.NewRun(RunFixture.Config(runId: "")),
            "empty run id rejected");
        Check.Rejected(director.NewRun(RunFixture.Config(catalogueVersion: "")),
            "empty catalogue version rejected");
        Check.Rejected(director.NewRun(RunFixture.Config(unit: "")),
            "empty unit rejected");
        Check.Rejected(director.NewRun(RunFixture.Config(baseBudget: 0m)),
            "zero base budget rejected");
        Check.Rejected(director.NewRun(RunFixture.Config(baseBudget: -5m)),
            "negative base budget rejected");
        Check.Rejected(director.NewRun(RunFixture.Config(
                eras: RunFixture.Eras().Take(1).ToArray())),
            "fewer than two eras rejected");
        var duplicateEras = new[]
        {
            new EraReference("same"),
            new EraReference("same"),
            new EraReference("era-2"),
            new EraReference("era-3"),
            new EraReference("era-4"),
            new EraReference("era-5")
        };
        Check.Rejected(director.NewRun(RunFixture.Config(eras: duplicateEras)),
            "duplicate era IDs rejected");
        Check.Rejected(director.NewRun(new RunConfiguration(
                "run", 1L, new ScenarioValue(100m, RunFixture.Unit), null!, "cat")),
            "missing era schedule rejected");

        Check.True(director.Snapshot is null, "no state after rejected starts");
    }

    public static void DesignPriceTenBudgetTwentyGivesPlayerPlusOneAlly()
    {
        var director = RunFixture.NewRun(baseBudget: 20m);
        RunFixture.Confirm(director, price: 10m);
        var snapshot = RunFixture.Snap(director);

        Check.NotNull(snapshot.ConfirmedDesign, "confirmed design");
        var design = snapshot.ConfirmedDesign!;
        Check.Equal(2, design.FriendlyCountIncludingPlayer,
            "price 10 with budget 20 gives player plus one AI");
        Check.Equal(10m, design.Cost.Value, "full quote price");
        Check.True(snapshot.DesignConfirmedForBlock, "design locks the block");
    }

    public static void DesignCountCapsAtEightIncludingPlayer()
    {
        var director = RunFixture.NewRun(baseBudget: 1000m);
        RunFixture.Confirm(director, price: 10m);
        var snapshot = RunFixture.Snap(director);

        Check.NotNull(snapshot.ConfirmedDesign, "confirmed design");
        Check.Equal(8, snapshot.ConfirmedDesign!.FriendlyCountIncludingPlayer,
            "friendly count caps at eight including player");
    }

    public static void InvalidDesignQuotesAreRejectedWithoutChangingState()
    {
        var director = RunFixture.NewRun(baseBudget: 20m);
        var snapshot = RunFixture.Snap(director);
        var quote = RunFixture.Quote(snapshot);

        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(snapshot, price: 0m)),
            "zero price rejected");
        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(snapshot, price: -1m)),
            "negative price rejected");
        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(snapshot, price: 20.000001m)),
            "over-budget price rejected");
        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(snapshot,
            nativeDesignValid: false)), "invalid native design rejected");
        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(snapshot,
            nativeEraValid: false)), "invalid native era rejected");
        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(snapshot,
            rejectionReason: "native rejection")), "rejection reason rejected");
        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(snapshot,
            unit: "different-unit")), "cost unit mismatch rejected");
        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(snapshot,
            localId: "")), "empty local design ID rejected");
        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(snapshot,
            snapshotHash: "")), "empty blueprint snapshot hash rejected");

        var badEraQuote = quote with
        {
            Blueprint = quote.Blueprint with { Era = new EraReference("") }
        };
        Check.Rejected(director.ConfirmDesign(badEraQuote), "empty blueprint era rejected");

        var after = RunFixture.Snap(director);
        Check.True(after.ConfirmedDesign is null, "no design was stored");
        Check.False(after.DesignConfirmedForBlock, "design was not confirmed");
        Check.Equal(RunPhase.Building, after.Phase, "still building");
    }

    public static void FullNativeQuoteControlsAffordabilityNotTwoDecimalDisplay()
    {
        var precise = RunFixture.NewRun(baseBudget: 20m);
        RunFixture.Confirm(precise, price: 10.004m);
        var preciseSnapshot = RunFixture.Snap(precise);
        Check.NotNull(preciseSnapshot.ConfirmedDesign, "precise design");
        Check.Equal(1, preciseSnapshot.ConfirmedDesign!.FriendlyCountIncludingPlayer,
            "10.004 full quote with budget 20 gives one friendly, not two");

        var justAbove = RunFixture.NewRun(baseBudget: 160.004m);
        var justAboveSnapshot = RunFixture.Snap(justAbove);
        // Both values display as 160.00 at two decimals; the full quote is above the
        // full ceiling and must be rejected rather than using formatted text.
        Check.Rejected(justAbove.ConfirmDesign(RunFixture.Quote(justAboveSnapshot,
            price: 160.005m)), "quote just above the full ceiling rejected");
        Check.True(RunFixture.Snap(justAbove).ConfirmedDesign is null,
            "rejected precise quote changed no design");

        var exact = RunFixture.NewRun(baseBudget: 160.004m);
        RunFixture.Confirm(exact, price: 160.004m);
        var exactSnapshot = RunFixture.Snap(exact);
        Check.NotNull(exactSnapshot.ConfirmedDesign, "exact ceiling design accepted");
        Check.Equal(1, exactSnapshot.ConfirmedDesign!.FriendlyCountIncludingPlayer,
            "exact full quote is affordable");
    }

    public static void DesignCannotChangeMidBlockForThreeFights()
    {
        var director = RunFixture.NewRun(baseBudget: 20m);
        RunFixture.Confirm(director, price: 10m);
        var initial = RunFixture.Snap(director);
        Check.NotNull(initial.ConfirmedDesign, "initial design");
        var initialDesign = initial.ConfirmedDesign!;
        var originalHash = initialDesign.Blueprint.SnapshotHash;
        var originalCount = initialDesign.FriendlyCountIncludingPlayer;

        RunFixture.PlayVictory(director);
        var afterOne = RunFixture.Snap(director);
        Check.Equal(RunPhase.ChoosingEncounter, afterOne.Phase, "choosing after first win");
        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(afterOne, price: 10m)),
            "design change rejected between fights");
        Check.NotNull(afterOne.ConfirmedDesign, "design after first fight");
        var afterOneDesign = afterOne.ConfirmedDesign!;
        Check.Equal(originalHash, afterOneDesign.Blueprint.SnapshotHash,
            "design unchanged after first fight");
        Check.Equal(originalCount, afterOneDesign.FriendlyCountIncludingPlayer,
            "fleet count unchanged after first fight");

        RunFixture.PlayVictory(director);
        var afterTwo = RunFixture.Snap(director);
        Check.Equal(RunPhase.ChoosingEncounter, afterTwo.Phase, "choosing after second win");
        Check.Rejected(director.ConfirmDesign(RunFixture.Quote(afterTwo, price: 10m)),
            "design change rejected between second and third fight");
        Check.NotNull(afterTwo.ConfirmedDesign, "design after second fight");
        Check.Equal(originalHash, afterTwo.ConfirmedDesign!.Blueprint.SnapshotHash,
            "design unchanged after second fight");

        RunFixture.PlayVictory(director);
        var afterThree = RunFixture.Snap(director);
        Check.Equal(RunPhase.Building, afterThree.Phase, "building after third win");
        Check.False(afterThree.DesignConfirmedForBlock,
            "new block requires a fresh confirmation");
        Check.Rejected(director.SetOffers(RunFixture.Plans(afterThree)),
            "offers rejected before the new block design is confirmed");
        RunFixture.Confirm(director, price: 10m);
        Check.True(RunFixture.Snap(director).DesignConfirmedForBlock,
            "new block design confirmed");
    }

    public static void SetOffersRequiresConfirmedDesignAndCannotBeReplaced()
    {
        var director = RunFixture.NewRun();
        var initial = RunFixture.Snap(director);
        Check.Rejected(director.SetOffers(RunFixture.Plans(initial)),
            "offers rejected before design confirmation");
        Check.Equal(0, RunFixture.Snap(director).Offers.Count,
            "no offers stored after rejection");

        RunFixture.Confirm(director);
        RunFixture.SetOffers(director);
        var afterSet = RunFixture.Snap(director);
        Check.Equal(RunPhase.ChoosingEncounter, afterSet.Phase, "choosing after offers");
        Check.Equal(3, afterSet.Offers.Count, "three offers stored");
        Check.Rejected(director.SetOffers(RunFixture.Plans(afterSet)),
            "offer replacement rejected");
    }

    public static void OfferSetValidationIsAtomic()
    {
        var director = RunFixture.NewRun();
        RunFixture.Confirm(director);
        var snapshot = RunFixture.Snap(director);

        Check.Rejected(director.SetOffers(new[] { RunFixture.Plan(snapshot, EncounterRisk.Cautious),
            RunFixture.Plan(snapshot, EncounterRisk.Standard) }), "two offers rejected");

        var duplicateId = RunFixture.Plans(snapshot).ToArray();
        duplicateId[1] = duplicateId[1] with { OfferId = duplicateId[0].OfferId };
        Check.Rejected(director.SetOffers(duplicateId), "duplicate offer ID rejected");

        var duplicateRisk = RunFixture.Plans(snapshot).ToArray();
        duplicateRisk[1] = duplicateRisk[1] with
        {
            Risk = EncounterRisk.Cautious,
            SupplyReward = 1,
            OfferId = "duplicate-risk",
            PlanHash = "duplicate-risk-hash"
        };
        Check.Rejected(director.SetOffers(duplicateRisk), "duplicate risk rejected");

        var wrongSupply = RunFixture.Plans(snapshot).ToArray();
        wrongSupply[1] = wrongSupply[1] with { SupplyReward = 1 };
        Check.Rejected(director.SetOffers(wrongSupply), "wrong risk reward rejected");

        var wrongRound = RunFixture.Plans(snapshot).ToArray();
        wrongRound[2] = wrongRound[2] with { Round = snapshot.Round + 1 };
        Check.Rejected(director.SetOffers(wrongRound), "wrong offer round rejected");

        var wrongEra = RunFixture.Plans(snapshot).ToArray();
        wrongEra[2] = wrongEra[2] with { Era = new EraReference("foreign-era") };
        Check.Rejected(director.SetOffers(wrongEra), "wrong offer era rejected");

        var wrongCatalogue = RunFixture.Plans(snapshot).ToArray();
        wrongCatalogue[2] = wrongCatalogue[2] with { CatalogueVersion = "foreign-catalogue" };
        Check.Rejected(director.SetOffers(wrongCatalogue),
            "wrong catalogue version rejected");

        var zeroEnemy = RunFixture.Plans(snapshot).ToArray();
        zeroEnemy[2] = zeroEnemy[2] with
        {
            Enemies = new[] { new EnemyUnit("zero-enemy", 0) }
        };
        Check.Rejected(director.SetOffers(zeroEnemy), "zero enemy count rejected");

        var tooManyEnemies = RunFixture.Plans(snapshot).ToArray();
        tooManyEnemies[2] = tooManyEnemies[2] with
        {
            Enemies = new[] { new EnemyUnit("mass-enemy", 13) }
        };
        Check.Rejected(director.SetOffers(tooManyEnemies), "enemy total over twelve rejected");

        var identicalCards = RunFixture.Plans(snapshot).ToArray();
        var sameEnemies = new[] { new EnemyUnit("same-enemy", 2) };
        identicalCards[0] = identicalCards[0] with
        {
            OfferId = "same-card-cautious",
            PlanHash = "same-card-cautious-hash",
            MapId = "same-map",
            SpawnLayoutId = "same-spawn",
            Enemies = sameEnemies
        };
        identicalCards[1] = identicalCards[1] with
        {
            OfferId = "same-card-standard",
            PlanHash = "same-card-standard-hash",
            MapId = "same-map",
            SpawnLayoutId = "same-spawn",
            Enemies = sameEnemies
        };
        identicalCards[2] = identicalCards[2] with
        {
            OfferId = "same-card-dangerous",
            PlanHash = "same-card-dangerous-hash",
            MapId = "same-map",
            SpawnLayoutId = "same-spawn",
            Enemies = sameEnemies
        };
        Check.Rejected(director.SetOffers(identicalCards),
            "risk-only identical cards rejected");

        var after = RunFixture.Snap(director);
        Check.Equal(RunPhase.Building, after.Phase, "invalid offers did not change phase");
        Check.Equal(0, after.Offers.Count, "invalid offers were not partially stored");
        RunFixture.SetOffers(director);
        Check.Equal(3, RunFixture.Snap(director).Offers.Count,
            "a valid set is still accepted afterwards");
    }

    public static void ResolvedOfferIdCannotBeReusedInLaterRound()
    {
        var director = RunFixture.NewRun();
        var request = RunFixture.PlayVictory(director);
        var afterVictory = RunFixture.Snap(director);

        var plans = RunFixture.Plans(afterVictory).ToArray();
        plans[1] = plans[1] with
        {
            OfferId = request.Plan.OfferId,
            PlanHash = "reused-id-hash"
        };
        Check.Rejected(director.SetOffers(plans), "consumed offer ID rejected next round");
        Check.Equal(0, RunFixture.Snap(director).Offers.Count,
            "no offers stored after consumed-ID rejection");
    }

    public static void SelectionMustComeFromStoredOffersAndBeginCreatesGeneration()
    {
        var director = RunFixture.NewRun(baseBudget: 20m);
        RunFixture.Confirm(director, price: 10m);
        RunFixture.SetOffers(director);
        var snapshot = RunFixture.Snap(director);

        Check.Rejected(director.SelectEncounter("unknown-offer"),
            "selection outside stored offers rejected");
        Check.True(RunFixture.Snap(director).SelectedOfferId is null,
            "rejected selection changed nothing");
        Check.Rejected(director.BeginEncounter(), "begin without selection rejected");

        var standard = RunFixture.OfferOf(snapshot, EncounterRisk.Standard);
        Check.Accepted(director.SelectEncounter(standard.OfferId), "stored offer selected");
        Check.Equal(standard.OfferId, RunFixture.Snap(director).SelectedOfferId,
            "selection stored");

        Check.Accepted(director.BeginEncounter(), "begin encounter");
        var loading = RunFixture.Snap(director);
        Check.Equal(RunPhase.LoadingEncounter, loading.Phase, "loading phase");
        Check.NotNull(loading.CurrentAttempt, "attempt identity");
        var attempt = loading.CurrentAttempt!;
        Check.Equal(1L, attempt.Generation, "first generation");
        Check.Equal(standard.OfferId, attempt.EncounterId,
            "attempt encounter id is offer id");
        Check.Equal(standard.PlanHash, attempt.PlanHash, "attempt plan hash");
        Check.NotNull(loading.LockedPlan, "locked plan");
        Check.SamePlan(standard, loading.LockedPlan!, "locked plan equals selection");

        var request = director.CreateEncounterRequest();
        Check.Equal(2, request.FriendlyCountIncludingPlayer,
            "encounter request includes player in friendly count");
        Check.SamePlan(standard, request.Plan, "encounter request plan");

        Check.Rejected(director.BeginEncounter(), "begin rejected while already loading");
        Check.Rejected(director.SelectEncounter(standard.OfferId),
            "selection rejected while loading");

        var badAttempt = loading.CurrentAttempt! with { PlanHash = "different-hash" };
        Check.Rejected(director.ReportCombatStarted(badAttempt),
            "same generation with different plan hash rejected");
        Check.Equal(RunPhase.LoadingEncounter, RunFixture.Snap(director).Phase,
            "bad attempt did not start combat");

        Check.Accepted(director.ReportCombatStarted(loading.CurrentAttempt!),
            "current attempt starts combat");
        Check.Equal(RunPhase.Fighting, RunFixture.Snap(director).Phase, "fighting phase");
        Check.SamePlan(standard, director.CreateEncounterRequest().Plan,
            "request remains available while fighting");
    }

    public static void VictoryAdvancesOnceAndDuplicateResultIsIgnored()
    {
        var director = RunFixture.NewRun(baseBudget: 20m);
        var request = RunFixture.PlayVictory(director);
        var afterVictory = RunFixture.Snap(director);

        Check.Equal(RunPhase.ChoosingEncounter, afterVictory.Phase, "choosing after victory");
        Check.Equal(2, afterVictory.Round, "round advanced once");
        Check.Equal(1, afterVictory.Victories, "victory recorded once");
        Check.Equal(1, afterVictory.WinsInBlock, "block win recorded");
        Check.Equal(2, afterVictory.PendingSupplyPoints,
            "standard victory supply awarded once");
        Check.Equal(0, afterVictory.Defeats, "no defeat");
        Check.Equal(0, afterVictory.Offers.Count, "offers cleared for next round");
        Check.True(afterVictory.SelectedOfferId is null, "selection cleared");

        var duplicate = director.AcceptResult(
            RunFixture.Result(request, EncounterOutcome.Victory));
        AssertIgnoredWithoutStateChange(director, afterVictory, duplicate,
            "duplicate victory ignored");

        var conflictingLoss = director.AcceptResult(
            RunFixture.Result(request, EncounterOutcome.FriendlyForceDestroyed));
        AssertIgnoredWithoutStateChange(director, afterVictory, conflictingLoss,
            "loss supplied after victory is ignored as duplicate");
    }

    public static void SuspendedChoosingSnapshotPreservesOffersAndSelection()
    {
        var director = RunFixture.NewRun();
        RunFixture.Confirm(director);
        RunFixture.SetOffers(director);
        var chosen = RunFixture.OfferOf(RunFixture.Snap(director), EncounterRisk.Dangerous);
        Check.Accepted(director.SelectEncounter(chosen.OfferId), "select dangerous");

        var beforeSuspend = RunFixture.Snap(director);
        Check.Accepted(director.Suspend(), "suspend choosing");
        var suspended = RunFixture.Snap(director);
        Check.Equal(RunPhase.Suspended, suspended.Phase, "suspended phase");
        Check.Equal(RunPhase.ChoosingEncounter, suspended.SuspendedFrom!.Value,
            "suspended from choosing");
        Check.Equal(chosen.OfferId, suspended.SelectedOfferId, "selected offer preserved");
        Check.Equal(3, suspended.Offers.Count, "offers preserved");
        for (var i = 0; i < 3; i++)
        {
            Check.SamePlan(beforeSuspend.Offers[i], suspended.Offers[i],
                "suspended offer " + i);
        }

        var restored = RunDirector.Restore(suspended);
        var restoredSnapshot = RunFixture.Snap(restored);
        Check.Equal(RunPhase.Suspended, restoredSnapshot.Phase, "restored still suspended");
        Check.Equal(chosen.OfferId, restoredSnapshot.SelectedOfferId,
            "restored selection preserved");
        Check.Accepted(restored.Resume(), "resume choosing");
        var resumed = RunFixture.Snap(restored);
        Check.Equal(RunPhase.ChoosingEncounter, resumed.Phase, "resume exact choosing");
        Check.Equal(chosen.OfferId, resumed.SelectedOfferId, "resume exact selection");
        for (var i = 0; i < 3; i++)
        {
            Check.SamePlan(beforeSuspend.Offers[i], resumed.Offers[i],
                "resumed offer " + i);
        }
    }

    public static void SuspendAndResumeBuildingKeepsBlockState()
    {
        var director = RunFixture.NewRun();
        RunFixture.Confirm(director);
        var before = RunFixture.Snap(director);
        Check.Accepted(director.Suspend(), "suspend building");
        var suspended = RunFixture.Snap(director);
        Check.Equal(RunPhase.Suspended, suspended.Phase, "suspended");
        Check.Equal(RunPhase.Building, suspended.SuspendedFrom!.Value,
            "suspended from building");

        Check.Accepted(director.Resume(), "resume building");
        var resumed = RunFixture.Snap(director);
        Check.Equal(RunPhase.Building, resumed.Phase, "building resumed");
        Check.Equal(before.Round, resumed.Round, "round preserved");
        Check.Equal(before.AvailableBudget.Value, resumed.AvailableBudget.Value,
            "budget preserved");
        Check.True(resumed.DesignConfirmedForBlock, "design confirmation preserved");

        var request = director.CreateOfferRequest();
        Check.Equal(before.Round, request.Round, "offer request round");
        Check.Equal(before.BlockIndex, request.Block, "offer request block");
        Check.Equal(before.AvailableBudget.Value, request.AvailableBudget.Value,
            "offer request budget");
    }

    public static void SuspendDuringCombatReplaysLockedChoiceWithNewGeneration()
    {
        var director = RunFixture.NewRun(baseBudget: 20m);
        var request = RunFixture.StartFight(director);
        var oldAttempt = request.Attempt;
        var oldPlan = request.Plan;

        Check.Accepted(director.Suspend(), "suspend fighting");
        var suspended = RunFixture.Snap(director);
        Check.Equal(RunPhase.Suspended, suspended.Phase, "suspended phase");
        Check.Equal(RunPhase.Fighting, suspended.SuspendedFrom!.Value,
            "suspended from fighting");
        Check.Equal(oldPlan.OfferId, suspended.SelectedOfferId, "choice preserved");

        Check.Accepted(director.Resume(), "resume fighting");
        var resumed = RunFixture.Snap(director);
        Check.Equal(RunPhase.LoadingEncounter, resumed.Phase,
            "combat retry starts in loading");
        Check.NotNull(resumed.CurrentAttempt, "new attempt");
        Check.Equal(oldAttempt.Generation + 1, resumed.CurrentAttempt!.Generation,
            "retry uses a new generation");
        Check.SamePlan(oldPlan, resumed.LockedPlan!, "retry retains exact plan");

        var stale = director.AcceptResult(
            RunFixture.Result(request, EncounterOutcome.Victory));
        AssertIgnoredWithoutStateChange(director, resumed, stale,
            "old combat callback after retry ignored");

        Check.Accepted(director.ReportCombatStarted(resumed.CurrentAttempt!),
            "new attempt starts combat");
        Check.Accepted(director.AcceptResult(RunFixture.Result(
            director.CreateEncounterRequest(), EncounterOutcome.Victory)),
            "new attempt wins");
        Check.Equal(2, RunFixture.Snap(director).Round, "new attempt advanced once");
    }

    public static void TechnicalFailureRetainsChoiceAndRetryUsesNewGeneration()
    {
        var director = RunFixture.NewRun(baseBudget: 20m);
        RunFixture.Confirm(director, price: 10m);
        RunFixture.SetOffers(director);
        var offer = RunFixture.OfferOf(RunFixture.Snap(director), EncounterRisk.Standard);
        Check.Accepted(director.SelectEncounter(offer.OfferId), "select standard");
        Check.Accepted(director.BeginEncounter(), "begin loading");
        var request = director.CreateEncounterRequest();

        Check.Accepted(director.AcceptResult(RunFixture.Result(request,
            EncounterOutcome.TechnicalFailure, "spawn failed")), "technical failure");
        var failed = RunFixture.Snap(director);
        Check.Equal(RunPhase.Suspended, failed.Phase, "technical failure suspends");
        Check.Equal(RunPhase.LoadingEncounter, failed.SuspendedFrom!.Value,
            "suspend source is loading");
        Check.Equal(0, failed.Victories, "no victory for technical failure");
        Check.Equal(0, failed.Defeats, "no defeat for technical failure");
        Check.Equal(0, failed.PendingSupplyPoints, "no supply for technical failure");
        Check.Equal(1, failed.Round, "round not advanced");
        Check.Equal(offer.OfferId, failed.SelectedOfferId, "choice retained");
        Check.SamePlan(request.Plan, failed.LockedPlan!, "locked plan retained");
        Check.Equal(3, failed.Offers.Count, "offers retained");

        var restoredFailure = RunDirector.Restore(failed);
        Check.Equal(RunPhase.Suspended, RunFixture.Snap(restoredFailure).Phase,
            "stored failure snapshot restores suspended");
        Check.Accepted(restoredFailure.Resume(), "restored failure can retry");
        var restoredRetry = RunFixture.Snap(restoredFailure);
        Check.Equal(request.Attempt.Generation + 1,
            restoredRetry.CurrentAttempt!.Generation,
            "restored failure retry uses the next generation");
        Check.SamePlan(request.Plan, restoredRetry.LockedPlan!,
            "restored failure retry retains exact plan");

        Check.Accepted(director.Resume(), "retry after technical failure");
        var retry = RunFixture.Snap(director);
        Check.Equal(RunPhase.LoadingEncounter, retry.Phase, "retry loading");
        Check.Equal(request.Attempt.Generation + 1, retry.CurrentAttempt!.Generation,
            "retry generation advanced");
        Check.SamePlan(request.Plan, retry.LockedPlan!, "retry exact plan");
        Check.Equal(offer.OfferId, retry.SelectedOfferId, "retry exact choice");

        var oldCallback = director.AcceptResult(RunFixture.Result(request,
            EncounterOutcome.TechnicalFailure));
        AssertIgnoredWithoutStateChange(director, retry, oldCallback,
            "previous failure callback after retry ignored");

        Check.Accepted(director.ReportCombatStarted(retry.CurrentAttempt!),
            "retry starts combat");
        Check.Accepted(director.AcceptResult(RunFixture.Result(
            director.CreateEncounterRequest(), EncounterOutcome.Victory)),
            "retry can win once");
        Check.Equal(2, RunFixture.Snap(director).Round, "retry win advances once");
    }

    public static void AbortTaxonomySuspendsWithoutRewardOrLoss()
    {
        var director = RunFixture.NewRun(baseBudget: 20m);
        RunFixture.Confirm(director, price: 10m);
        RunFixture.SetOffers(director);
        var offer = RunFixture.OfferOf(RunFixture.Snap(director), EncounterRisk.Cautious);
        Check.Accepted(director.SelectEncounter(offer.OfferId), "select cautious");
        Check.Accepted(director.BeginEncounter(), "begin loading");
        var request = director.CreateEncounterRequest();
        var loading = RunFixture.Snap(director);

        Check.Rejected(director.AcceptResult(RunFixture.Result(request,
            EncounterOutcome.Victory)), "victory before combat rejected");
        Check.Rejected(director.AcceptResult(RunFixture.Result(request,
            EncounterOutcome.FriendlyForceDestroyed)),
            "friendly loss before combat rejected");
        Check.Rejected(director.AcceptResult(RunFixture.Result(request,
            EncounterOutcome.CombatRetreat)), "retreat before combat rejected");
        Check.Equal(RunPhase.LoadingEncounter, RunFixture.Snap(director).Phase,
            "rejected combat outcomes leave loading");

        Check.Accepted(director.AcceptResult(RunFixture.Result(request,
            EncounterOutcome.PrecombatAborted, "cancelled loading")),
            "precombat abort accepted");
        var aborted = RunFixture.Snap(director);
        Check.Equal(RunPhase.Suspended, aborted.Phase, "precombat abort suspends");
        Check.Equal(RunPhase.LoadingEncounter, aborted.SuspendedFrom!.Value,
            "precombat abort suspended from loading");
        Check.Equal(0, aborted.Victories, "no victory for abort");
        Check.Equal(0, aborted.Defeats, "no loss for abort");
        Check.Equal(0, aborted.PendingSupplyPoints, "no supply for abort");
        Check.SamePlan(loading.LockedPlan!, aborted.LockedPlan!, "abort keeps plan");

        Check.Accepted(director.Resume(), "resume aborted loading");
        var retry = RunFixture.Snap(director);
        Check.Equal(request.Attempt.Generation + 1, retry.CurrentAttempt!.Generation,
            "abort retry uses new generation");
        Check.Accepted(director.ReportCombatStarted(retry.CurrentAttempt!),
            "abort retry starts combat");
        Check.Rejected(director.AcceptResult(RunFixture.Result(
            director.CreateEncounterRequest(), EncounterOutcome.PrecombatAborted)),
            "precombat abort rejected once combat is live");
        Check.Equal(RunPhase.Fighting, RunFixture.Snap(director).Phase,
            "rejected abort did not leave combat");

        Check.Accepted(director.AcceptResult(RunFixture.Result(
            director.CreateEncounterRequest(), EncounterOutcome.TechnicalFailure)),
            "technical failure during combat suspends");
        Check.Equal(RunPhase.Suspended, RunFixture.Snap(director).Phase,
            "combat technical failure suspended");
    }

    public static void StaleGenerationAndPlanMismatchCannotGrantResults()
    {
        var director = RunFixture.NewRun(baseBudget: 20m);
        var request = RunFixture.StartFight(director);
        var snapshot = RunFixture.Snap(director);

        var differentHash = new AttemptIdentity(
            request.Attempt.RunId,
            request.Attempt.EncounterId,
            "different-plan-hash",
            request.Attempt.Generation);
        var hashMismatch = director.AcceptResult(
            new EncounterResult(differentHash, request.Plan, EncounterOutcome.Victory, null));
        Check.Rejected(hashMismatch, "same generation with a different plan hash rejected");
        AssertStateStillFighting(director, snapshot);

        var changedPlan = request.Plan with { MapId = "changed-map" };
        var planMismatch = director.AcceptResult(
            new EncounterResult(request.Attempt, changedPlan, EncounterOutcome.Victory, null));
        Check.Rejected(planMismatch, "result plan payload mismatch rejected");
        AssertStateStillFighting(director, snapshot);

        var oldGeneration = request.Attempt with
        {
            Generation = request.Attempt.Generation - 1
        };
        var stale = director.AcceptResult(
            new EncounterResult(oldGeneration, request.Plan, EncounterOutcome.Victory, null));
        AssertIgnoredWithoutStateChange(director, snapshot, stale,
            "older generation ignored");

        var foreign = request.Attempt with { RunId = "other-run" };
        var foreignResult = director.AcceptResult(
            new EncounterResult(foreign, request.Plan, EncounterOutcome.Victory, null));
        AssertIgnoredWithoutStateChange(director, snapshot, foreignResult,
            "foreign run result ignored");

        Check.Accepted(director.AcceptResult(
            RunFixture.Result(request, EncounterOutcome.Victory)),
            "valid result still accepted");
        Check.Equal(1, RunFixture.Snap(director).Victories,
            "rejected callbacks did not poison the attempt");
    }

    public static void LossOutcomesArePermanentAndConsolidated()
    {
        var destroyed = RunFixture.NewRun(baseBudget: 20m);
        var destroyedRequest = RunFixture.PlayLoss(destroyed,
            EncounterOutcome.FriendlyForceDestroyed);
        var lost = RunFixture.Snap(destroyed);
        Check.Equal(RunPhase.Lost, lost.Phase, "all friendlies destroyed is loss");
        Check.Equal(1, lost.Defeats, "defeat recorded");
        Check.Equal(0, lost.Victories, "no victory on loss");
        Check.Equal(0, lost.PendingSupplyPoints, "no supply on loss");
        Check.Rejected(destroyed.Suspend(), "lost run cannot suspend");
        Check.Rejected(destroyed.Resume(), "lost run cannot resume");

        var lateVictory = destroyed.AcceptResult(
            RunFixture.Result(destroyedRequest, EncounterOutcome.Victory));
        AssertIgnoredWithoutStateChange(destroyed, lost, lateVictory,
            "victory after loss is duplicate and cannot revoke defeat");

        var retreat = RunFixture.NewRun(baseBudget: 20m);
        RunFixture.PlayLoss(retreat, EncounterOutcome.CombatRetreat);
        var retreated = RunFixture.Snap(retreat);
        Check.Equal(RunPhase.Lost, retreated.Phase, "combat retreat is loss");
        Check.Equal(1, retreated.Defeats, "retreat defeat recorded");
        Check.Equal(0, retreated.PendingSupplyPoints, "retreat grants no supply");
        Check.Rejected(retreat.Resume(), "retreat loss cannot resume");
    }

    public static void EveryFiniteEraBoundaryUsesApprovedBudget()
    {
        var nativeNames = new[] { "WWI", "Interwar", "Earlywar", "Midwar", "Latewar", "Coldwar" };
        var director = RunFixture.NewRun(baseBudget: 100m,
            eras: nativeNames.Select(name => new EraReference(name)).ToArray());
        Check.Equal("WWI", RunFixture.Snap(director).EraSchedule[0].NativeId, "campaign starts in WWI");
        var expected = new[]
        {
            (Block: 1, Era: 1, Base: 160m, Bonus: 9.6m, Available: 169.6m, Round: 4),
            (Block: 2, Era: 2, Base: 250m, Bonus: 15m, Available: 265m, Round: 7),
            (Block: 3, Era: 3, Base: 350m, Bonus: 21m, Available: 371m, Round: 10),
            (Block: 4, Era: 4, Base: 500m, Bonus: 30m, Available: 530m, Round: 13),
            (Block: 5, Era: 5, Base: 800m, Bonus: 48m, Available: 848m, Round: 16)
        };

        for (var block = 0; block < expected.Length; block++)
        {
            for (var battle = 0; battle < 3; battle++)
                RunFixture.PlayVictory(director);

            var snapshot = RunFixture.Snap(director);
            var want = expected[block];
            Check.Equal(want.Block, snapshot.BlockIndex, "block index after boundary");
            Check.Equal(want.Era, snapshot.EraIndex, "era index after boundary");
            Check.Equal(nativeNames[want.Era], snapshot.EraSchedule[snapshot.EraIndex].NativeId,
                "native era order after three victories");
            Check.Equal(want.Base, snapshot.BlockBaseBudget.Value,
                "absolute era base budget");
            Check.Equal(want.Bonus, snapshot.SupplyBonus, "supply bonus at boundary");
            Check.Equal(want.Available, snapshot.AvailableBudget.Value,
                "available budget includes current bonus only");
            Check.Equal(want.Round, snapshot.Round, "next round after boundary");
            Check.Equal((block + 1) * 3, snapshot.Victories, "victory total");
            Check.Equal(0, snapshot.PendingSupplyPoints, "pending supply converted once");
            Check.Equal(RunPhase.Building, snapshot.Phase, "block boundary enters building");
            Check.False(snapshot.DesignConfirmedForBlock,
                "block boundary requires a new confirmation");
        }

        Check.True(expected[1].Available != expected[0].Available + 15m,
            "next block bonus does not compound from prior available budget");

        EncounterRequest? last = null;
        for (var battle = 0; battle < 3; battle++)
            last = RunFixture.PlayVictory(director);
        Check.NotNull(last, "final finite attempt");

        var awaiting = RunFixture.Snap(director);
        Check.Equal(RunPhase.AwaitingEndlessChoice, awaiting.Phase,
            "win eighteen enters endless choice");
        Check.Equal(18, awaiting.Victories, "eighteen finite victories");
        Check.Equal(5, awaiting.BlockIndex, "still sixth finite block");
        Check.Equal(5, awaiting.EraIndex, "Coldwar era retained");
        Check.Equal(800m, awaiting.BlockBaseBudget.Value, "Coldwar base");
        Check.Equal(48m, awaiting.SupplyBonus, "sixth block bonus is current");
        Check.Equal(848m, awaiting.AvailableBudget.Value, "sixth block budget");
        Check.Equal(6, awaiting.PendingSupplyPoints,
            "sixth block supply is pending, not yet applied");
        Check.Equal(19, awaiting.Round, "next round is endless round nineteen");

        var duplicate = director.AcceptResult(
            RunFixture.Result(last!, EncounterOutcome.Victory));
        AssertIgnoredWithoutStateChange(director, awaiting, duplicate,
            "repeated boundary result cannot double-apply supply");
    }

    public static void RoundFourUsesCampaignBudgetAndRequotesFleet()
    {
        var director = RunFixture.NewRun(baseBudget: 21391m);
        RunFixture.Confirm(director, price: 10940m);
        RunFixture.PlayVictory(director, EncounterRisk.Standard);
        RunFixture.PlayVictory(director, EncounterRisk.Cautious);
        RunFixture.PlayVictory(director, EncounterRisk.Standard);
        var boundary = RunFixture.Snap(director);
        Check.Equal(4, boundary.Round, "third victory starts round four");
        Check.Equal(34225.6m, boundary.BlockBaseBudget.Value, "Interwar base is 1.6 times measured B0");
        Check.Equal(1711.28m, boundary.SupplyBonus, "five earned points become five percent");
        Check.Equal(35936.88m, boundary.AvailableBudget.Value, "vanilla map budget never enters campaign economics");
        RunFixture.Confirm(director, price: 10940m);
        Check.Equal(3, RunFixture.Snap(director).ConfirmedDesign!.FriendlyCountIncludingPlayer,
            "round four requotes the fleet to player plus two allies");
        RunFixture.SetOffers(director);
        var offer = RunFixture.OfferOf(RunFixture.Snap(director), EncounterRisk.Standard);
        Check.Accepted(director.SelectEncounter(offer.OfferId), "select round four encounter");
        Check.Accepted(director.BeginEncounter(), "begin round four encounter");
        var request = director.CreateEncounterRequest();
        Check.Equal(3, request.FriendlyCountIncludingPlayer, "three total tanks reach the spawn request");
    }

    public static void EndlessBlocksGrowBaseByFifteenPercentWithoutCompoundingBonus()
    {
        var director = RunFixture.NewRun(baseBudget: 100m);
        RunFixture.AdvanceToFirstEndless(director);

        var first = RunFixture.Snap(director);
        Check.Equal(6, first.BlockIndex, "first endless block index");
        Check.Equal(5, first.EraIndex, "endless remains Coldwar era");
        Check.Equal(920m, first.BlockBaseBudget.Value, "first endless base is Coldwar * 1.15");
        Check.Equal(55.2m, first.SupplyBonus, "sixth block supply applied to first endless");
        Check.Equal(975.2m, first.AvailableBudget.Value, "first endless available budget");
        Check.Equal(0, first.PendingSupplyPoints, "pending supply converted");
        Check.Equal(19, first.Round, "first endless round");
        Check.Equal(RunPhase.Building, first.Phase, "first endless starts building");

        RunFixture.PlayEndlessBlock(director, EncounterRisk.Dangerous);
        var second = RunFixture.Snap(director);
        Check.Equal(7, second.BlockIndex, "second endless block index");
        Check.Equal(1058m, second.BlockBaseBudget.Value,
            "second endless base grows 15 percent");
        Check.Equal(95.22m, second.SupplyBonus,
            "second endless bonus uses new base and preceding supply only");
        Check.Equal(1153.22m, second.AvailableBudget.Value,
            "second endless available budget");
        Check.Equal(21, second.Victories, "twenty-one victories");
        Check.Equal(22, second.Round, "twenty-second round");
        Check.Equal(0, second.PendingSupplyPoints, "supply reset at endless boundary");

        RunFixture.PlayEndlessBlock(director, EncounterRisk.Cautious);
        var third = RunFixture.Snap(director);
        Check.Equal(8, third.BlockIndex, "third endless block index");
        Check.Equal(1216.7m, third.BlockBaseBudget.Value,
            "third endless base grows from base, not previous available budget");
        Check.Equal(36.501m, third.SupplyBonus,
            "third endless bonus uses new base and preceding supply only");
        Check.Equal(1253.201m, third.AvailableBudget.Value,
            "third endless available budget");
        Check.Equal(24, third.Victories, "twenty-four victories");
        Check.Equal(25, third.Round, "twenty-fifth round");
        Check.Rejected(director.Finish(), "finish rejected during endless building");
    }

    public static void WinEighteenRequiresExplicitEndlessChoiceOrFinish()
    {
        var director = RunFixture.NewRun();
        RunFixture.AdvanceToAwaitingEndless(director);
        Check.Accepted(director.Suspend(), "suspend at endless choice");
        var suspended = RunFixture.Snap(director);
        Check.Equal(RunPhase.Suspended, suspended.Phase, "awaiting choice suspended");
        Check.Equal(RunPhase.AwaitingEndlessChoice, suspended.SuspendedFrom!.Value,
            "suspended from endless choice");
        Check.Accepted(director.Resume(), "resume endless choice");
        Check.Equal(RunPhase.AwaitingEndlessChoice, RunFixture.Snap(director).Phase,
            "endless choice resumed exactly");

        Check.Accepted(director.Finish(), "finish run at endless choice");
        var finished = RunFixture.Snap(director);
        Check.Equal(RunPhase.Finished, finished.Phase, "finished phase");
        Check.Equal(18, finished.Victories, "records retained on finish");
        Check.Rejected(director.Resume(), "finished run cannot resume");
        Check.Rejected(director.Suspend(), "finished run cannot suspend");
        Check.Rejected(director.Finish(), "finished run cannot finish twice");
        Check.Accepted(director.NewRun(RunFixture.Config(runId: "run-after-finish")),
            "new run allowed after finish");
        Check.Equal("run-after-finish", RunFixture.Snap(director).RunId, "new run id");

        var continueThenFinish = RunFixture.NewRun(runId: "run-continue");
        RunFixture.AdvanceToAwaitingEndless(continueThenFinish);
        Check.Accepted(continueThenFinish.ContinueEndless(), "continue endless");
        Check.Rejected(continueThenFinish.Finish(),
            "finish rejected after endless mode starts");
    }

    public static void CurrentSupplyBonusIsIncludedInOfferRequest()
    {
        var director = RunFixture.NewRun(baseBudget: 100m);
        for (var i = 0; i < 3; i++)
            RunFixture.PlayVictory(director);

        var boundary = RunFixture.Snap(director);
        Check.Equal(169.6m, boundary.AvailableBudget.Value, "boundary budget with bonus");
        RunFixture.Confirm(director);

        var requestA = director.CreateOfferRequest();
        var requestB = director.CreateOfferRequest();
        Check.Equal(requestA, requestB, "offer request is deterministic");
        Check.Equal("run-1", requestA.RunId, "offer request run");
        Check.Equal(424242L, requestA.Seed, "offer request seed");
        Check.Equal(4, requestA.Round, "offer request round");
        Check.Equal(1, requestA.Block, "offer request zero-based block");
        Check.Equal("native-era-1", requestA.Era.NativeId, "offer request era");
        Check.Equal(169.6m, requestA.AvailableBudget.Value,
            "offer request sees current supply bonus");
        Check.Equal(0, requestA.PendingSupplyPoints, "converted supply is not pending");
        Check.Equal("catalogue-1", requestA.CatalogueVersion, "offer request catalogue");

        RunFixture.SetOffers(director, RunFixture.Plans(boundary));
        RunFixture.PlayVictory(director);
        var inBlock = RunFixture.Snap(director);
        Check.Equal(RunPhase.ChoosingEncounter, inBlock.Phase, "choosing after block win");
        Check.Equal(5, inBlock.Round, "next round inside block");
        var requestC = director.CreateOfferRequest();
        Check.Equal(169.6m, requestC.AvailableBudget.Value,
            "same fleet budget across the three-battle block");
        Check.Equal(5, requestC.Round, "inside-block request round");
        Check.Equal(1, requestC.Block, "inside-block request block");
    }

    public static void StartingNewRunCannotOverwriteActiveOrSuspendedRun()
    {
        var active = RunFixture.NewRun(runId: "active-run");
        Check.Rejected(active.NewRun(RunFixture.Config(runId: "replacement")),
            "active run cannot be overwritten");
        Check.Equal("active-run", RunFixture.Snap(active).RunId, "active run retained");

        Check.Accepted(active.Suspend(), "suspend active run");
        Check.Rejected(active.NewRun(RunFixture.Config(runId: "replacement")),
            "suspended run cannot be overwritten");
        var suspended = RunFixture.Snap(active);
        Check.Equal(RunPhase.Suspended, suspended.Phase, "still suspended");
        Check.Equal("active-run", suspended.RunId, "suspended run retained");

        var lost = RunFixture.NewRun(runId: "lost-run");
        RunFixture.PlayLoss(lost);
        Check.Accepted(lost.NewRun(RunFixture.Config(runId: "after-loss")),
            "new run allowed after a terminal loss");
        Check.Equal("after-loss", RunFixture.Snap(lost).RunId, "new run replaces terminal run");
    }

    public static void RestoreRejectsBadSchemaAndReplaysBattleAsFreshGeneration()
    {
        var director = RunFixture.NewRun(baseBudget: 20m);
        var request = RunFixture.StartFight(director);
        var snapshot = RunFixture.Snap(director);
        var oldAttempt = request.Attempt;

        Check.Throws<ArgumentException>(() =>
        {
            RunDirector.Restore(snapshot with { SchemaVersion = 99 });
        }, "unsupported schema rejected");

        var restored = RunDirector.Restore(snapshot);
        var restoredSnapshot = RunFixture.Snap(restored);
        Check.Equal(RunPhase.LoadingEncounter, restoredSnapshot.Phase,
            "crash restore replays loading");
        Check.NotNull(restoredSnapshot.CurrentAttempt, "restored attempt");
        Check.Equal(oldAttempt.Generation + 1, restoredSnapshot.CurrentAttempt!.Generation,
            "crash restore uses fresh generation");
        Check.SamePlan(request.Plan, restoredSnapshot.LockedPlan!,
            "crash restore retains exact plan");

        var stale = restored.AcceptResult(
            RunFixture.Result(request, EncounterOutcome.Victory));
        AssertIgnoredWithoutStateChange(restored, restoredSnapshot, stale,
            "old attempt callback after crash restore ignored");

        Check.Accepted(restored.ReportCombatStarted(restoredSnapshot.CurrentAttempt!),
            "restored attempt starts combat");
        Check.Accepted(restored.AcceptResult(RunFixture.Result(
            restored.CreateEncounterRequest(), EncounterOutcome.Victory)),
            "restored attempt wins once");
        Check.Equal(1, RunFixture.Snap(restored).Victories, "restored victory once");
    }

    public static void SnapshotJsonRoundTripPreservesSuspendedSelection()
    {
        var director = RunFixture.NewRun();
        RunFixture.Confirm(director);
        RunFixture.SetOffers(director);
        var chosen = RunFixture.OfferOf(RunFixture.Snap(director), EncounterRisk.Dangerous);
        Check.Accepted(director.SelectEncounter(chosen.OfferId), "select for json");
        Check.Accepted(director.Suspend(), "suspend for json");
        var snapshot = RunFixture.Snap(director);

        var json = JsonSerializer.Serialize(snapshot);
        var roundTrip = JsonSerializer.Deserialize<RunSnapshot>(json);
        Check.NotNull(roundTrip, "json round trip snapshot");
        var trip = roundTrip!;
        Check.Equal(RunPhase.Suspended, trip.Phase, "json phase");
        Check.Equal(RunPhase.ChoosingEncounter, trip.SuspendedFrom!.Value,
            "json suspended source");
        Check.Equal(chosen.OfferId, trip.SelectedOfferId, "json selection");
        Check.Equal(3, trip.Offers.Count, "json offers");
        Check.Equal(snapshot.AvailableBudget.Value, trip.AvailableBudget.Value,
            "json budget");

        var restored = RunDirector.Restore(trip);
        Check.Accepted(restored.Resume(), "resume json snapshot");
        var resumed = RunFixture.Snap(restored);
        Check.Equal(RunPhase.ChoosingEncounter, resumed.Phase, "json resume phase");
        Check.Equal(chosen.OfferId, resumed.SelectedOfferId, "json resume selection");
        for (var i = 0; i < snapshot.Offers.Count; i++)
        {
            Check.SamePlan(snapshot.Offers[i], resumed.Offers[i],
                "json resumed offer " + i);
        }
    }

    public static void AllThreeRiskRewardPairsAreApplied()
    {
        var cases = new[]
        {
            (Risk: EncounterRisk.Cautious, Reward: 1),
            (Risk: EncounterRisk.Standard, Reward: 2),
            (Risk: EncounterRisk.Dangerous, Reward: 3)
        };

        foreach (var item in cases)
        {
            var director = RunFixture.NewRun();
            RunFixture.Confirm(director);
            RunFixture.SetOffers(director);
            var offer = RunFixture.OfferOf(RunFixture.Snap(director), item.Risk);
            Check.Equal(item.Reward, offer.SupplyReward,
                item.Risk + " offer reward");

            RunFixture.PlayVictory(director, item.Risk);
            var snapshot = RunFixture.Snap(director);
            Check.Equal(RunPhase.ChoosingEncounter, snapshot.Phase,
                item.Risk + " victory advances");
            Check.Equal(item.Reward, snapshot.PendingSupplyPoints,
                item.Risk + " reward applied");
            Check.Equal(1, snapshot.Victories, item.Risk + " one victory");
        }
    }

    private static void AssertStateStillFighting(RunDirector director,
        RunSnapshot expected)
    {
        var after = RunFixture.Snap(director);
        Check.Equal(expected.Phase, after.Phase, "phase unchanged");
        Check.Equal(expected.Round, after.Round, "round unchanged");
        Check.Equal(expected.Victories, after.Victories, "victories unchanged");
        Check.Equal(expected.Defeats, after.Defeats, "defeats unchanged");
        Check.Equal(expected.PendingSupplyPoints, after.PendingSupplyPoints,
            "pending supply unchanged");
        Check.Equal(expected.CurrentAttempt, after.CurrentAttempt, "attempt unchanged");
    }
}
