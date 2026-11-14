namespace SprocketExpedition.Core.Run;

public sealed partial class RunDirector
{
    private static int OfferCount(RunState state) => state.Rewards?.Version >= 3
        && state.BlockIndex < state.EraSchedule.Count && state.Round % 4 == 0 ? 1 : 3;
    private static int Battles(RunState state) => state.Rewards == null ? 3 : 4;
    private static int FiniteRounds(RunState state) =>
        Battles(state) * (state.EraSchedule.Count - state.StartingEraIndex);
    private static RewardState? CloneRewards(RewardState? rewards) => rewards == null ? null : rewards with
    {
        Inventory = rewards.Inventory.ToArray(), Active = rewards.Active.ToArray(),
        Choices = rewards.Choices.ToArray(), GrantedOffers = rewards.GrantedOffers.ToArray()
    };
    private static decimal Budget(RewardState? rewards, decimal basis) => rewards == null ? basis
        : RewardRules.Fleet(rewards, checked(basis + rewards.EarnedFunds), 1m).EffectiveBudget;
    // Schema 6 threat budget: era base + supply + earned funds, before any item
    // multiplier, discount or free ally. One expression keeps the persisted
    // checkpoint, the offer request and the battle log on exactly the same value.
    internal static decimal OfferBaseBudget(decimal blockBase, decimal supplyBonus, decimal earnedFunds) =>
        checked(blockBase + supplyBonus + earnedFunds);
    private static decimal OfferBaseBudget(RunState state) => OfferBaseBudget(
        state.BlockBaseBudget.Value, state.SupplyBonus, state.Rewards?.EarnedFunds ?? 0m);
    internal static decimal OfferBaseBudget(RunSnapshot snapshot) => OfferBaseBudget(
        snapshot.BlockBaseBudget.Value, snapshot.SupplyBonus, snapshot.Rewards?.EarnedFunds ?? 0m);
    private static bool Schema6(RewardState? rewards) => rewards?.Version >= 6;
    private static void RefreshBudget(RunState state)
    {
        state.AvailableBudget = new ScenarioValue(Budget(state.Rewards,
            state.BlockBaseBudget.Value + state.SupplyBonus), state.CostUnitId);
        if (state.ConfirmedDesign is { } design && CalculateFleet(state, design.Cost.Value, out var count))
            state.ConfirmedDesign = design with { FriendlyCountIncludingPlayer = count };
        else if (state.ConfirmedDesign != null) state.DesignConfirmedForBlock = false;
    }
    private static bool CalculateFleet(RunState state, decimal price, out int count)
    {
        if (state.Rewards == null) return TryCalculateFriendlyCount(state.AvailableBudget.Value, price, out count);
        var fleet = RewardRules.Fleet(state.Rewards,
            state.BlockBaseBudget.Value + state.SupplyBonus + state.Rewards.EarnedFunds, price);
        count = fleet.Total;
        return fleet.PlayerCopies > 0 && count is >= 1 and <= 8;
    }

    public RunActionResult ActivateReward(string id, bool cashBonds = false)
        => ChangeReward(rewards =>
        {
            if (OfferCount(_state!) == 1 && rewards.Inventory.Any(i => i.Id == id && i.Kind == RewardKind.OperationalFreedom))
                throw new InvalidOperationException("The curated boss battle cannot be rerolled.");
            if (rewards.OfferRoll >= 10000 && rewards.Inventory.Any(i => i.Id == id && i.Kind == RewardKind.OperationalFreedom))
                throw new InvalidOperationException("The offer reroll limit has been reached.");
            return RewardRules.Activate(rewards, id, cashBonds, _state!.BlockBaseBudget.Value);
        }, true);
    public RunActionResult DiscardReward(string id)
        => ChangeReward(rewards => RewardRules.Discard(rewards, id), false);
    public RunActionResult DiscardActiveReward(string id)
        => ChangeReward(rewards => RewardRules.DiscardActive(rewards, id), true);
    public RunActionResult ClaimReward(string id)
        => ChangeReward(rewards => RewardRules.Claim(rewards, id), false);
    public RunActionResult SkipRewardChoice()
        => ChangeReward(rewards => rewards with { Choices = Array.Empty<RewardItem>() }, false);

    private RunActionResult ChangeReward(Func<RewardState, RewardState> change, bool rebuildOffers)
    {
        if (_state?.Rewards == null) return Rejected("This legacy run has no item inventory.");
        var state = _state;
        if (!Schema6(state.Rewards) && rebuildOffers && state.LastOutcome == EncounterOutcome.FriendlyForceDestroyed && state.SelectedOfferId != null)
            return Rejected("Recovery keeps the same challenge; activate new items after completing it.");
        if (state.Phase is not (RunPhase.Building or RunPhase.ChoosingEncounter or RunPhase.AwaitingEndlessChoice))
            return Rejected("Manage items in the editor before combat.");
        try
        {
            var updated = change(CloneRewards(state.Rewards)!);
            if (rebuildOffers && state.Rewards.Choices.Count > 0) return Rejected("Choose or decline the pending reward first.");
            // Schema 6 pins the round's offers. Only Operational Freedom may roll
            // a new set; activating, replacing or discarding other items keeps the
            // exact offer ids and hashes. Older schemas keep their reroll behavior.
            var fixedOffers = Schema6(state.Rewards);
            var operationalFreedom = rebuildOffers && state.Rewards.Inventory.Any(item =>
                item.Kind == RewardKind.OperationalFreedom
                && !updated.Inventory.Any(remaining => remaining.Id == item.Id));
            var rerolls = !fixedOffers || operationalFreedom;
            var preserveSelected = rebuildOffers && (!rerolls
                || state.SelectedOfferId != null && updated.OfferRoll == state.Rewards.OfferRoll);
            if (rebuildOffers && rerolls && !preserveSelected)
            {
                updated = updated with { OfferRoll = Math.Max(updated.OfferRoll, state.Rewards.OfferRoll + 1) };
            }
            updated = updated with { Revision = checked(state.Rewards.Revision + 1) };
            RewardRules.Validate(updated);
            _ = Budget(updated, state.BlockBaseBudget.Value + state.SupplyBonus);
            if (preserveSelected && state.OfferEconomy == null && !fixedOffers)
                state.OfferEconomy = new OfferEconomy(state.AvailableBudget,
                    state.Rewards.Active.Any(i => i.Kind == RewardKind.ExperimentalContract) ? 1.2m : 1m);
            state.Rewards = updated;
            if (rebuildOffers && rerolls && !preserveSelected)
            {
                ClearEncounterSelection(state);
                if (state.Phase == RunPhase.ChoosingEncounter) state.Phase = RunPhase.Building;
            }
            RefreshBudget(state);
            return Accepted("Item inventory saved.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException)
        { return Rejected(ex.Message); }
    }

    public RunActionResult UseRecovery()
    {
        if (_state?.Phase != RunPhase.AwaitingRecovery || _state.Rewards == null)
            return Rejected("No recovery is waiting.");
        var state = _state;
        try { state.Rewards = RewardRules.ConsumeRecovery(state.Rewards) with { Revision = checked(state.Rewards.Revision + 1) }; }
        catch (InvalidOperationException ex) { return Rejected(ex.Message); }
        state.LockedPlan = null;
        state.CurrentAttempt = null;
        state.Phase = RunPhase.ChoosingEncounter;
        RefreshBudget(state);
        return Accepted("Recovery consumed. Refit and retry the same encounter.");
    }

    public RunActionResult AcceptPermanentLoss()
    {
        if (_state?.Phase != RunPhase.AwaitingRecovery) return Rejected("No recovery is waiting.");
        var state = _state;
        if (!Schema6(state.Rewards))
        {
            if (state.LockedPlan is { } plan) ConsumeResolvedOffer(state, plan);
            ClearEncounterSelection(state);
        }
        else
        {
            // Keep the fixed round offers for a future retry-after-loss.
            state.LockedPlan = null;
            state.CurrentAttempt = null;
        }
        state.Defeats = 1;
        state.Phase = RunPhase.Lost;
        return Accepted("Defeat accepted.");
    }
}
