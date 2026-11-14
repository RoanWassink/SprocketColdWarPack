using RunTests;

var tests = new (string Name, Action Body)[]
{
    (nameof(RunDirectorTests.ReturnToEditorPreservesEncounterAndRejectsLateResults),
        RunDirectorTests.ReturnToEditorPreservesEncounterAndRejectsLateResults),
    (nameof(RunDirectorTests.RoundFourUsesCampaignBudgetAndRequotesFleet),
        RunDirectorTests.RoundFourUsesCampaignBudgetAndRequotesFleet),
    (nameof(RunDirectorTests.PreEncounterUpgradeKeepsChallengeAndFreezesAtLoading),
        RunDirectorTests.PreEncounterUpgradeKeepsChallengeAndFreezesAtLoading),
    (nameof(RunDirectorTests.NewRunStartsInBuildingWithMeasuredBase),
        RunDirectorTests.NewRunStartsInBuildingWithMeasuredBase),
    (nameof(RunDirectorTests.NewRunRejectsIncompleteConfiguration),
        RunDirectorTests.NewRunRejectsIncompleteConfiguration),
    (nameof(RunDirectorTests.DesignPriceTenBudgetTwentyGivesPlayerPlusOneAlly),
        RunDirectorTests.DesignPriceTenBudgetTwentyGivesPlayerPlusOneAlly),
    (nameof(RunDirectorTests.DesignCountCapsAtEightIncludingPlayer),
        RunDirectorTests.DesignCountCapsAtEightIncludingPlayer),
    (nameof(RunDirectorTests.InvalidDesignQuotesAreRejectedWithoutChangingState),
        RunDirectorTests.InvalidDesignQuotesAreRejectedWithoutChangingState),
    (nameof(RunDirectorTests.FullNativeQuoteControlsAffordabilityNotTwoDecimalDisplay),
        RunDirectorTests.FullNativeQuoteControlsAffordabilityNotTwoDecimalDisplay),
    (nameof(RunDirectorTests.DesignCannotChangeMidBlockForThreeFights),
        RunDirectorTests.DesignCannotChangeMidBlockForThreeFights),
    (nameof(RunDirectorTests.SetOffersRequiresConfirmedDesignAndCannotBeReplaced),
        RunDirectorTests.SetOffersRequiresConfirmedDesignAndCannotBeReplaced),
    (nameof(RunDirectorTests.OfferSetValidationIsAtomic),
        RunDirectorTests.OfferSetValidationIsAtomic),
    (nameof(RunDirectorTests.ResolvedOfferIdCannotBeReusedInLaterRound),
        RunDirectorTests.ResolvedOfferIdCannotBeReusedInLaterRound),
    (nameof(RunDirectorTests.SelectionMustComeFromStoredOffersAndBeginCreatesGeneration),
        RunDirectorTests.SelectionMustComeFromStoredOffersAndBeginCreatesGeneration),
    (nameof(RunDirectorTests.VictoryAdvancesOnceAndDuplicateResultIsIgnored),
        RunDirectorTests.VictoryAdvancesOnceAndDuplicateResultIsIgnored),
    (nameof(RunDirectorTests.SuspendedChoosingSnapshotPreservesOffersAndSelection),
        RunDirectorTests.SuspendedChoosingSnapshotPreservesOffersAndSelection),
    (nameof(RunDirectorTests.SuspendAndResumeBuildingKeepsBlockState),
        RunDirectorTests.SuspendAndResumeBuildingKeepsBlockState),
    (nameof(RunDirectorTests.SuspendDuringCombatReplaysLockedChoiceWithNewGeneration),
        RunDirectorTests.SuspendDuringCombatReplaysLockedChoiceWithNewGeneration),
    (nameof(RunDirectorTests.TechnicalFailureRetainsChoiceAndRetryUsesNewGeneration),
        RunDirectorTests.TechnicalFailureRetainsChoiceAndRetryUsesNewGeneration),
    (nameof(RunDirectorTests.AbortTaxonomySuspendsWithoutRewardOrLoss),
        RunDirectorTests.AbortTaxonomySuspendsWithoutRewardOrLoss),
    (nameof(RunDirectorTests.StaleGenerationAndPlanMismatchCannotGrantResults),
        RunDirectorTests.StaleGenerationAndPlanMismatchCannotGrantResults),
    (nameof(RunDirectorTests.LossOutcomesArePermanentAndConsolidated),
        RunDirectorTests.LossOutcomesArePermanentAndConsolidated),
    (nameof(RunDirectorTests.EveryFiniteEraBoundaryUsesApprovedBudget),
        RunDirectorTests.EveryFiniteEraBoundaryUsesApprovedBudget),
    (nameof(RunDirectorTests.EndlessBlocksGrowBaseByFifteenPercentWithoutCompoundingBonus),
        RunDirectorTests.EndlessBlocksGrowBaseByFifteenPercentWithoutCompoundingBonus),
    (nameof(RunDirectorTests.WinEighteenRequiresExplicitEndlessChoiceOrFinish),
        RunDirectorTests.WinEighteenRequiresExplicitEndlessChoiceOrFinish),
    (nameof(RunDirectorTests.CurrentSupplyBonusIsIncludedInOfferRequest),
        RunDirectorTests.CurrentSupplyBonusIsIncludedInOfferRequest),
    (nameof(RunDirectorTests.StartingNewRunCannotOverwriteActiveOrSuspendedRun),
        RunDirectorTests.StartingNewRunCannotOverwriteActiveOrSuspendedRun),
    (nameof(RunDirectorTests.RestoreRejectsBadSchemaAndReplaysBattleAsFreshGeneration),
        RunDirectorTests.RestoreRejectsBadSchemaAndReplaysBattleAsFreshGeneration),
    (nameof(RunDirectorTests.SnapshotJsonRoundTripPreservesSuspendedSelection),
        RunDirectorTests.SnapshotJsonRoundTripPreservesSuspendedSelection),
    (nameof(RunDirectorTests.AllThreeRiskRewardPairsAreApplied),
        RunDirectorTests.AllThreeRiskRewardPairsAreApplied),
    (nameof(EraScheduleTests.ScheduleIsEveryPlayableEraWithATankInStartOrder),
        EraScheduleTests.ScheduleIsEveryPlayableEraWithATankInStartOrder),
    (nameof(EraScheduleTests.EraWithoutATankOrNotPlayableIsSkipped),
        EraScheduleTests.EraWithoutATankOrNotPlayableIsSkipped),
    (nameof(EraScheduleTests.TooFewEraSchedulesAreReportedNotThrown),
        EraScheduleTests.TooFewEraSchedulesAreReportedNotThrown),
    (nameof(EraScheduleTests.EraCountAndFamilyRules),
        EraScheduleTests.EraCountAndFamilyRules),
    (nameof(EraScheduleTests.EightEraRunCompletesAtTheLastEraAndKeepsItsSchedule),
        EraScheduleTests.EightEraRunCompletesAtTheLastEraAndKeepsItsSchedule),
    (nameof(EraScheduleTests.FiveEraRunCompletesAtLatewarAndContinuesEndless),
        EraScheduleTests.FiveEraRunCompletesAtLatewarAndContinuesEndless),
    (nameof(EraScheduleTests.FiveEraRunCanStartInLatewarButNotPastTheSchedule),
        EraScheduleTests.FiveEraRunCanStartInLatewarButNotPastTheSchedule)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;
