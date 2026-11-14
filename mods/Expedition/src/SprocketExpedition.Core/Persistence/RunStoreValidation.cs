using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Persistence;

// Structural validation only. Storage does not re-derive phase, generation,
// dependency or reward rules; it only refuses shapes that would not round-trip.
internal static class RunSnapshotValidation
{
    public static RunStoreIssue? Validate(RunSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return Invalid("Snapshot is null.");
        }
        if (snapshot.EraSchedule is null
            || !EraSchedulePolicy.IsValidEraCount(snapshot.EraSchedule.Count))
            return Invalid("EraSchedule must contain 2 to 32 native eras.");
        if (!EraSchedulePolicy.IsValidStartingEra(
                snapshot.StartingEraIndex, snapshot.EraSchedule.Count))
            return Invalid("Starting era index is invalid.");
        if (snapshot.GenerationPolicy is { } generationPolicy && generationPolicy != 5)
            return Invalid("Generation policy is not supported.");
        if (snapshot.GenerationPolicy == 5
            && (snapshot.Rewards is null || snapshot.Rewards.Version < 4))
            return Invalid("The policy-5 overlay requires an adventure-v4 or newer run.");
        if (snapshot.OfferEconomy is { } economy)
        {
            var issue = ValidateValue(economy.Budget, "OfferEconomy.Budget");
            if (issue != null) return issue;
            if (economy.Budget.Value <= 0 || economy.Budget.UnitId != snapshot.CostUnitId
                || economy.EnemyBudgetMultiplier is < 1m or > 2.4m)
                return Invalid("Pinned offer economy is invalid.");
        }

        if ((snapshot.SchemaVersion >= 2) != (snapshot.Rewards != null)) return Invalid("Reward state does not match snapshot schema.");
        if (snapshot.Rewards != null)
        {
            if (snapshot.Rewards.Version != snapshot.SchemaVersion) return Invalid("Adventure rules do not match snapshot schema.");
            try { RewardRules.Validate(snapshot.Rewards); }
            catch (Exception ex) { return Invalid("Invalid reward state: " + ex.Message); }
        }

        if (!Enum.IsDefined(typeof(RunPhase), snapshot.Phase))
        {
            return Invalid("Phase is not a defined RunPhase.");
        }

        // Frozen decision: Resolving is applied atomically by the engine and is
        // never persisted. Storing it would imply a half-applied reward.
        if (snapshot.Phase == RunPhase.Resolving)
        {
            return Invalid("RunPhase.Resolving is not persisted.");
        }

        if (snapshot.SuspendedFrom.HasValue
            && !Enum.IsDefined(typeof(RunPhase), snapshot.SuspendedFrom.Value))
        {
            return Invalid("SuspendedFrom is not a defined RunPhase.");
        }

        if (snapshot.LastOutcome.HasValue
            && !Enum.IsDefined(typeof(EncounterOutcome), snapshot.LastOutcome.Value))
        {
            return Invalid("LastOutcome is not a defined EncounterOutcome.");
        }

        if (snapshot.EnemiesDestroyed < 0 || snapshot.FriendliesLost < 0
            || snapshot.Attempts < 0 || snapshot.RoundAttempts < 0 || snapshot.Score < 0m
            || (snapshot.SchemaVersion < 6 && (snapshot.Ironman || snapshot.RoundAttempts != 0 || snapshot.Score != 0m
                || snapshot.Phase == RunPhase.Retired)))
        {
            return Invalid("Run totals must not be negative.");
        }

        if (snapshot.RunId is null)
        {
            return Invalid("RunId is null.");
        }

        if (snapshot.CatalogueVersion is null)
        {
            return Invalid("CatalogueVersion is null.");
        }

        if (snapshot.CostUnitId is null)
        {
            return Invalid("CostUnitId is null.");
        }

        var budgetIssue = ValidateValue(snapshot.InitialBaseBudget, "InitialBaseBudget")
            ?? ValidateValue(snapshot.BlockBaseBudget, "BlockBaseBudget")
            ?? ValidateValue(snapshot.AvailableBudget, "AvailableBudget");
        if (budgetIssue is not null)
        {
            return budgetIssue;
        }

        foreach (var era in snapshot.EraSchedule)
        {
            var eraIssue = ValidateEra(era, "EraSchedule");
            if (eraIssue is not null)
            {
                return eraIssue;
            }
        }

        if (snapshot.ConfirmedDesign is not null)
        {
            var designIssue = ValidateDesign(snapshot.ConfirmedDesign, "ConfirmedDesign");
            if (designIssue is not null)
            {
                return designIssue;
            }
        }

        if (snapshot.Offers is null)
        {
            return Invalid("Offers is null.");
        }

        var planRulesVersion = snapshot.Rewards?.Version ?? snapshot.SchemaVersion;
        foreach (var offer in snapshot.Offers)
        {
            var offerIssue = ValidatePlan(offer, "Offers", planRulesVersion);
            if (offerIssue is not null)
            {
                return offerIssue;
            }
        }

        if (snapshot.LockedPlan is not null)
        {
            var planIssue = ValidatePlan(snapshot.LockedPlan, "LockedPlan", planRulesVersion);
            if (planIssue is not null)
            {
                return planIssue;
            }
        }

        if (snapshot.CurrentAttempt is not null)
        {
            var attemptIssue = ValidateAttempt(snapshot.CurrentAttempt, "CurrentAttempt");
            if (attemptIssue is not null)
            {
                return attemptIssue;
            }
        }

        if (snapshot.ProcessedAttempts is null)
        {
            return Invalid("ProcessedAttempts is null.");
        }

        foreach (var attempt in snapshot.ProcessedAttempts)
        {
            var attemptIssue = ValidateAttempt(attempt, "ProcessedAttempts");
            if (attemptIssue is not null)
            {
                return attemptIssue;
            }
        }

        if (snapshot.ConsumedOfferIds is null)
        {
            return Invalid("ConsumedOfferIds is null.");
        }

        foreach (var offerId in snapshot.ConsumedOfferIds)
        {
            if (offerId is null)
            {
                return Invalid("ConsumedOfferIds contains null.");
            }
        }

        return null;
    }

    private static RunStoreIssue? ValidatePlan(EncounterPlan? plan, string path, int rulesVersion)
    {
        if (plan is null)
        {
            return Invalid($"{path} contains null.");
        }

        if (plan.OfferId is null
            || plan.PlanHash is null
            || plan.MapId is null
            || plan.SpawnLayoutId is null
            || plan.CatalogueVersion is null)
        {
            return Invalid($"{path} has a null identifier.");
        }

        if (!Enum.IsDefined(typeof(EncounterRisk), plan.Risk))
        {
            return Invalid($"{path} has an undefined Risk.");
        }

        if (plan.Extras is { } extrasForVersion && extrasForVersion.RulesVersion != rulesVersion)
        {
            return Invalid($"{path} encounter plan rules version does not match the run.");
        }

        var eraIssue = ValidateEra(plan.Era, $"{path}.Era");
        if (eraIssue is not null)
        {
            return eraIssue;
        }

        if (plan.Enemies is null)
        {
            return Invalid($"{path}.Enemies is null.");
        }

        foreach (var enemy in plan.Enemies)
        {
            if (enemy is null || enemy.BlueprintId is null)
            {
                return Invalid($"{path}.Enemies contains null or a null BlueprintId.");
            }
        }

        var modsIssue = ValidateMods(plan.RequiredMods, $"{path}.RequiredMods");
        if (modsIssue is not null)
        {
            return modsIssue;
        }

        if (!SpecialBattleRules.TryValidatePlan(plan.Extras, plan.Enemies, out var formationError))
        {
            return Invalid($"{path}: {formationError}");
        }

        return null;
    }

    private static RunStoreIssue? ValidateDesign(RunDesignState? design, string path)
    {
        if (design is null)
        {
            return Invalid($"{path} contains null.");
        }

        var blueprintIssue = ValidateBlueprint(design.Blueprint, $"{path}.Blueprint");
        if (blueprintIssue is not null)
        {
            return blueprintIssue;
        }

        var valueIssue = ValidateValue(design.Cost, $"{path}.Cost");
        if (valueIssue is not null)
        {
            return valueIssue;
        }

        return null;
    }

    private static RunStoreIssue? ValidateBlueprint(BlueprintReference? blueprint, string path)
    {
        if (blueprint is null)
        {
            return Invalid($"{path} is null.");
        }

        if (blueprint.LocalId is null || blueprint.SnapshotHash is null)
        {
            return Invalid($"{path} has a null identifier.");
        }

        var eraIssue = ValidateEra(blueprint.Era, $"{path}.Era");
        if (eraIssue is not null)
        {
            return eraIssue;
        }

        return ValidateMods(blueprint.RequiredMods, $"{path}.RequiredMods");
    }

    private static RunStoreIssue? ValidateEra(EraReference? era, string path)
    {
        if (era is null)
        {
            return Invalid($"{path} is null.");
        }

        if (era.NativeId is null)
        {
            return Invalid($"{path}.NativeId is null.");
        }

        return null;
    }

    private static RunStoreIssue? ValidateAttempt(AttemptIdentity? attempt, string path)
    {
        if (attempt is null)
        {
            return Invalid($"{path} contains null.");
        }

        if (attempt.RunId is null
            || attempt.EncounterId is null
            || attempt.PlanHash is null)
        {
            return Invalid($"{path} has a null identifier.");
        }

        return null;
    }

    private static RunStoreIssue? ValidateValue(ScenarioValue? value, string path)
    {
        if (value is null)
        {
            return Invalid($"{path} is null.");
        }

        if (value.UnitId is null)
        {
            return Invalid($"{path}.UnitId is null.");
        }

        return null;
    }

    private static RunStoreIssue? ValidateMods(
        IReadOnlyList<ModRequirement>? mods,
        string path)
    {
        if (mods is null)
        {
            return Invalid($"{path} is null.");
        }

        foreach (var mod in mods)
        {
            if (mod is null || mod.Id is null || mod.Version is null)
            {
                return Invalid($"{path} contains null or a null id/version.");
            }
        }

        return null;
    }

    private static RunStoreIssue Invalid(string message) =>
        new(RunStoreIssueCodes.InvalidSnapshot, message);
}
