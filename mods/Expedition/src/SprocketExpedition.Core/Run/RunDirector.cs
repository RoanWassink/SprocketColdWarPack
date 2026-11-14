using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace SprocketExpedition.Core.Run;

// Deterministic, synchronous run state machine. The caller supplies the run ID,
// seed, measured native base budget, native era references and generated offers.
// There is no clock, filesystem, Unity/native access, ambient randomness or global
// mutable state in this engine.
//
// Phase flow: Building -> ChoosingEncounter -> LoadingEncounter -> Fighting ->
// (Resolving is applied atomically inside AcceptResult) -> ChoosingEncounter or
// Building for a new block. A loss is terminal. Suspend is explicit.
public sealed partial class RunDirector
{
    public static decimal FiniteEraBudget(decimal initialBase, int eraIndex)
    {
        if (initialBase <= 0m || eraIndex < 0 || eraIndex >= EraSchedulePolicy.MaximumEraCount)
            throw new ArgumentOutOfRangeException();
        return eraIndex == 0 ? initialBase : RunRules.FloorToMicro(checked(initialBase * RunRules.EraBaseMultiplier(eraIndex)));
    }

    private RunState? _state;

    public RunSnapshot? Snapshot => _state?.ToSnapshot();

    public bool HasRun => _state is not null;
    private static bool RefitBoundary(RunState state) => state.Rewards != null
        && state.BlockIndex > state.StartingEraIndex && state.WinsInBlock == 0
        && state.LastOutcome == EncounterOutcome.Victory;
    public bool CanChooseEncounterBeforeRefit => _state != null && RefitBoundary(_state)
        && _state.Phase is RunPhase.Building or RunPhase.ChoosingEncounter;

    // Starting a run is possible when no run exists or the previous run already
    // reached a terminal Lost/Finished state. It can never overwrite an active or
    // suspended run without the coordinator's explicit abandon flow.
    public RunActionResult NewRun(RunConfiguration configuration)
    {
        if (configuration is null)
            return Rejected("Run configuration is required.");

        if (_state is not null &&
            _state.Phase is not (RunPhase.Lost or RunPhase.Finished or RunPhase.Retired))
        {
            return Rejected("An active or suspended run already exists.");
        }

        if (string.IsNullOrWhiteSpace(configuration.RunId))
            return Rejected("Run ID is required.");
        if (string.IsNullOrWhiteSpace(configuration.CatalogueVersion))
            return Rejected("Catalogue version is required.");
        if (configuration.AdventureRules && configuration.AdventureVersion is not (2 or 3 or 4 or 5 or 6 or 7)) return Rejected("Unsupported adventure rules.");
        if (configuration.TestPool && (!configuration.AdventureRules || configuration.AdventureVersion < 6))
            return Rejected("Test pool requires schema 6.");
        if (configuration.Ironman && (!configuration.AdventureRules || configuration.AdventureVersion < 6))
            return Rejected("Ironman requires schema 6.");
        var requestedEras = configuration.EraSchedule;
        if (requestedEras is null || !EraSchedulePolicy.IsValidEraCount(requestedEras.Count))
        {
            return Rejected("Between 2 and 32 native era references are required.");
        }
        if (!EraSchedulePolicy.IsValidStartingEra(configuration.StartingEraIndex, requestedEras.Count))
        {
            return Rejected("Starting era is outside the native schedule.");
        }
        var initialBase = configuration.InitialBaseBudget;
        if (initialBase is null)
            return Rejected("Measured initial base budget is required.");
        if (string.IsNullOrWhiteSpace(initialBase.UnitId))
            return Rejected("Cost unit is required.");
        if (initialBase.Value <= 0m)
            return Rejected("Initial base budget must be positive.");

        var eraIds = new HashSet<string>(StringComparer.Ordinal);
        var eras = new List<EraReference>(requestedEras.Count);
        foreach (var era in requestedEras)
        {
            if (era is null || string.IsNullOrWhiteSpace(era.NativeId))
                return Rejected("Every era reference needs a native ID.");
            if (!eraIds.Add(era.NativeId))
                return Rejected("Native era references must be unique.");
            eras.Add(era);
        }

        var unitId = initialBase.UnitId;
        // B0 is measured input. The six-decimal down-quantization belongs to the
        // calculated next-block budgets, not to the measured native median.
        var blockBase = new ScenarioValue(initialBase.Value, unitId);
        var startingEra = configuration.StartingEraIndex;
        var startingBudget = new ScenarioValue(FiniteEraBudget(initialBase.Value, startingEra), unitId);

        _state = new RunState
        {
            RunId = configuration.RunId,
            Seed = configuration.Seed,
            CatalogueVersion = configuration.CatalogueVersion,
            CostUnitId = unitId,
            InitialBaseBudget = blockBase,
            EraSchedule = eras,
            Phase = RunPhase.Building,
            StartingEraIndex = startingEra,
            Round = startingEra * (configuration.AdventureRules ? 4 : 3) + 1,
            BlockIndex = startingEra,
            EraIndex = startingEra,
            WinsInBlock = 0,
            Victories = 0,
            Defeats = 0,
            Ironman = configuration.Ironman, TestPool = configuration.TestPool,
            BlockBaseBudget = startingBudget,
            SupplyBonus = 0m,
            AvailableBudget = startingBudget,
            PendingSupplyPoints = 0,
            ConfirmedDesign = null,
            DesignConfirmedForBlock = false,
            Offers = new List<EncounterPlan>(),
            SelectedOfferId = null,
            LockedPlan = null,
            CurrentAttempt = null,
            GenerationCounter = 0,
            ProcessedAttempts = new List<AttemptIdentity>(),
            ConsumedOfferIds = new List<string>(),
            LastOutcome = null,
            LastOutcomeDetail = null,
            GenerationPolicy = configuration.AdventureRules && configuration.AdventureVersion == 5 ? 5 : null,
            Rewards = configuration.AdventureRules ? new RewardState
            {
                Version = configuration.AdventureVersion,
                Inventory = new[] { new RewardItem("starter.mass-production", RewardKind.MassProduction,
                    RewardRarity.Common, .25m, 2, 2) }
            } : null
        };

        return Accepted("Run started.");
    }

    // Validates the final native quote and locks the friendly fleet for this block.
    // The friendly count includes the player and uses the full quote, never a
    // two-decimal display value.
    public RunActionResult ConfirmDesign(DesignQuote quote)
        => ConfirmDesignCore(quote, beforeEncounter: false);

    // A selected challenge stays fixed while the player improves the tank. Only
    // the friendly blueprint/quote changes; loading or fighting freezes both.
    public RunActionResult ConfirmEncounterDesign(DesignQuote quote)
        => ConfirmDesignCore(quote, beforeEncounter: true);

    private RunActionResult ConfirmDesignCore(DesignQuote quote, bool beforeEncounter)
    {
        if (_state is null)
            return Rejected("No run exists.");
        var state = _state;

        if (state.Phase != RunPhase.Building
            && !(beforeEncounter && state.Phase == RunPhase.ChoosingEncounter))
            return Rejected("Designs can only be confirmed at a block boundary.");
        if (quote is null)
            return Rejected("Design quote is required.");
        var blueprint = quote.Blueprint;
        var cost = quote.Cost;
        if (blueprint is null)
            return Rejected("Design blueprint is required.");
        if (cost is null)
            return Rejected("Design cost is required.");
        if (!quote.NativeDesignValid)
            return Rejected("Native design validation failed.");
        if (!quote.NativeEraValid)
            return Rejected("Native era validation failed.");
        if (!string.IsNullOrWhiteSpace(quote.RejectionReason))
            return Rejected("Native quote reported a rejection reason.");
        if (string.IsNullOrWhiteSpace(blueprint.LocalId))
            return Rejected("Blueprint local ID is required.");
        if (string.IsNullOrWhiteSpace(blueprint.SnapshotHash))
            return Rejected("Blueprint snapshot hash is required.");
        if (blueprint.Era is null ||
            string.IsNullOrWhiteSpace(blueprint.Era.NativeId))
        {
            return Rejected("Blueprint native era is required.");
        }
        if (string.IsNullOrWhiteSpace(cost.UnitId))
            return Rejected("Quote cost unit is required.");
        if (!string.Equals(cost.UnitId, state.CostUnitId, StringComparison.Ordinal))
            return Rejected("Quote cost unit does not match the run budget unit.");
        if (cost.Value <= 0m)
            return Rejected("Quote cost must be positive.");

        if (!CalculateFleet(state, cost.Value, out var friendlyCount))
        {
            return Rejected("Design is not affordable with the available fleet budget.");
        }

        state.ConfirmedDesign = new RunDesignState(
            CopyBlueprint(blueprint), cost, friendlyCount);
        state.DesignConfirmedForBlock = true;
        return Accepted("Design confirmed.");
    }

    // Offers are generated by IEncounterCatalogue (3C) and accepted here. A complete
    // set is validated atomically: exactly three unique offer IDs, one of each risk,
    // matching round/era/catalogue, matching risk reward, positive enemy counts with
    // total 1..12, and meaningfully different roster/map/layout cards.
    public RunActionResult SetOffers(IReadOnlyList<EncounterPlan> offers,
        OfferEconomy? economy = null)
    {
        if (_state is null)
            return Rejected("No run exists.");
        var state = _state;

        if (state.Phase is not (RunPhase.Building or RunPhase.ChoosingEncounter))
            return Rejected("Offers can only be set while building or choosing.");
        if ((!state.DesignConfirmedForBlock || state.ConfirmedDesign is null) && !RefitBoundary(state))
            return Rejected("A design must be confirmed before offers are set.");
        if (state.Offers.Count != 0)
            return Rejected("Offers are already set for this round.");
        if (!ValidateOfferSet(state, offers, out var reason))
            return Rejected(reason);
        if (economy is not null
            && (economy.Budget is null || economy.Budget.Value <= 0m
                || !string.Equals(economy.Budget.UnitId, state.CostUnitId, StringComparison.Ordinal)
                || economy.EnemyBudgetMultiplier is < 1m or > 2.4m))
            return Rejected("Pinned offer generation economy is invalid.");

        state.Offers = offers.Select(CopyPlan).ToList();
        if (economy is not null) state.OfferEconomy = economy;
        state.SelectedOfferId = offers.Count == 1 ? offers[0].OfferId : null;
        state.LockedPlan = null;
        state.CurrentAttempt = null;
        state.Phase = RunPhase.ChoosingEncounter;
        return Accepted("Offers accepted.");
    }

    // Deliberate opt-in for an existing older-schema run. It takes effect only
    // for the next uncommitted offer set; current offers, selections, locked
    // plans and retries are never rewritten. Safe only before offers exist for
    // this round.
    public RunActionResult EnablePolicy5Generation()
    {
        if (_state is null) return Rejected("No run exists.");
        var state = _state;
        if (state.Rewards is null) return Rejected("This legacy run has no adventure items or generation policy.");
        if (state.Rewards.Version != 4)
            return Rejected("Only an existing adventure-v4 run can opt into policy 5 for future offers; start a new run for v5.");
        if (state.GenerationPolicy == 5) return Accepted("Policy 5 generation is already enabled for future offers.");
        if (state.Offers.Count != 0 || state.SelectedOfferId is not null
            || state.LockedPlan is not null || state.CurrentAttempt is not null)
            return Rejected("Current offers or an attempt are already pinned. Resolve or finish them before changing future generation policy.");
        if (state.Phase is not (RunPhase.Building or RunPhase.ChoosingEncounter))
            return Rejected("Future generation policy can only change at an uncommitted build/choice boundary.");
        state.GenerationPolicy = 5;
        return Accepted("Policy 5 generation enabled for the next uncommitted offers.");
    }

    public RunActionResult SelectEncounter(string offerId)
    {
        if (_state is null)
            return Rejected("No run exists.");
        var state = _state;

        if (state.Phase != RunPhase.ChoosingEncounter)
            return Rejected("An encounter can only be selected while choosing.");
        if (!Schema6(state.Rewards) && state.Rewards != null && state.LastOutcome == EncounterOutcome.FriendlyForceDestroyed
            && state.SelectedOfferId != null && state.SelectedOfferId != offerId)
            return Rejected("Recovery replays the same selected encounter.");
        if (string.IsNullOrWhiteSpace(offerId))
            return Rejected("Offer ID is required.");

        var offer = FindOffer(state, offerId);
        if (offer is null)
            return Rejected("Selected offer is not one of the stored offers.");
        if (state.ConsumedOfferIds.Contains(offerId))
            return Rejected("A resolved offer ID cannot be selected again.");

        state.SelectedOfferId = offerId;
        return Accepted("Encounter selected.");
    }

    // Locks the selected plan and creates generation 1 (or the next generation on a
    // retry). The old generation is never reused.
    public RunActionResult BeginEncounter()
    {
        if (_state is null)
            return Rejected("No run exists.");
        var state = _state;
        if (state.Rewards?.Choices.Count > 0) return Rejected("Choose or decline the pending reward before battle.");

        if (state.Phase != RunPhase.ChoosingEncounter)
            return Rejected("An encounter can only begin while choosing.");
        if (!state.DesignConfirmedForBlock || state.ConfirmedDesign is null)
            return Rejected("A design must be confirmed before an encounter begins.");
        if (state.SelectedOfferId is null)
            return Rejected("Select an offer before beginning the encounter.");

        var plan = FindOffer(state, state.SelectedOfferId);
        if (plan is null)
            return Rejected("Selected offer is no longer available.");
        if (state.ConsumedOfferIds.Contains(plan.OfferId))
            return Rejected("A resolved offer ID cannot be reused.");

        state.LockedPlan = CopyPlan(plan);
        state.GenerationCounter++;
        state.CurrentAttempt = new AttemptIdentity(
            state.RunId, plan.OfferId, plan.PlanHash, state.GenerationCounter);
        state.Phase = RunPhase.LoadingEncounter;
        state.SuspendedFrom = null;
        return Accepted("Encounter loading started.");
    }

    // User-requested refit: retain the exact offers/selection and all economics.
    // Ending this generation without an outcome makes late callbacks stale.
    public RunActionResult ReturnToEditor(AttemptIdentity attempt, EditorReturnContext? context = null)
    {
        if (_state is null) return Rejected("No run exists.");
        var state = _state;
        if (state.Phase is not (RunPhase.LoadingEncounter or RunPhase.Fighting)
            || state.CurrentAttempt != attempt || attempt is null)
            return Stale("This encounter is no longer active.");
        if (state.LockedPlan is null || state.SelectedOfferId != state.LockedPlan.OfferId)
            return Rejected("The active encounter has no valid locked selection.");
        if (Schema6(state.Rewards))
        {
            context ??= new EditorReturnContext(false);
            if (context.Contact)
            {
                if (state.Ironman) return Rejected("Ironman: returning to the editor is disabled after contact.");
                return AcceptResult(new EncounterResult(attempt, state.LockedPlan, EncounterOutcome.CombatRetreat,
                    "Returned to the editor after contact.")
                { EnemiesDestroyed = context.EnemiesDestroyed, FriendliesLost = context.FriendliesLost,
                    PlayerTankSurvived = context.PlayerTankSurvived });
            }
            state.Attempts = AddClamped(state.Attempts, 1);
            state.ProcessedAttempts.Add(attempt);
            state.LastOutcome = EncounterOutcome.PrecombatAborted;
            state.LastOutcomeDetail = "Returned to the editor before contact; no score penalty.";
        }
        state.CurrentAttempt = null;
        state.LockedPlan = null;
        state.SuspendedFrom = null;
        state.Phase = RunPhase.ChoosingEncounter;
        return Accepted("Returned to editing; selected encounter and budget retained.");
    }

    // The adapter notifies the director after native loading has settled and live
    // combat has started. This distinguishes CombatRetreat from PrecombatAborted.
    public RunActionResult ReportCombatStarted(AttemptIdentity attempt)
    {
        if (_state is null)
            return Rejected("No run exists.");
        var state = _state;

        if (state.Phase == RunPhase.Fighting &&
            state.CurrentAttempt is not null &&
            attempt == state.CurrentAttempt)
        {
            return Accepted("Combat already started.");
        }

        if (state.Phase != RunPhase.LoadingEncounter)
        {
            if (state.ProcessedAttempts.Contains(attempt))
                return Duplicate("Attempt was already resolved.");
            return Stale("No encounter is loading.");
        }
        var current = state.CurrentAttempt;
        if (current is null)
            return Stale("No active attempt exists.");
        if (attempt != current)
        {
            if (attempt is not null &&
                string.Equals(attempt.EncounterId, current.EncounterId,
                    StringComparison.Ordinal) &&
                attempt.Generation == current.Generation)
            {
                return Rejected("Attempt plan hash does not match the locked plan.");
            }

            return attempt is not null && state.ProcessedAttempts.Contains(attempt)
                ? Duplicate("Attempt was already resolved.")
                : Stale("Attempt does not match the active generation.");
        }

        state.Phase = RunPhase.Fighting;
        return Accepted("Combat started.");
    }

    // One authoritative adapter result. The outcome, reward and counters are applied
    // atomically; Resolving is therefore never persisted as a half-applied reward.
    // Stale and duplicate attempts are ignored without any state change.
    public RunActionResult AcceptResult(EncounterResult result)
    {
        if (_state is null)
            return Stale("No run exists.");
        var state = _state;

        if (result is null)
            return Rejected("Encounter result is required.");
        if (result.Attempt is null)
            return Rejected("Attempt identity is required.");

        var attempt = result.Attempt;
        if (state.ProcessedAttempts.Contains(attempt))
            return Duplicate("Attempt result was already processed.");
        if (!string.Equals(attempt.RunId, state.RunId, StringComparison.Ordinal))
            return Stale("Result belongs to another run.");

        var current = state.CurrentAttempt;
        if (current is null)
            return Stale("No active attempt exists.");
        if (attempt != current)
        {
            if (string.Equals(attempt.EncounterId, current.EncounterId,
                    StringComparison.Ordinal) &&
                attempt.Generation == current.Generation)
            {
                return Rejected("Attempt plan hash does not match the locked plan.");
            }

            return Stale("Result belongs to a stale encounter generation.");
        }

        if (state.Phase is not (RunPhase.LoadingEncounter or RunPhase.Fighting))
            return Stale("No combat attempt is active.");
        var plan = state.LockedPlan;
        if (plan is null)
            return Rejected("No locked plan exists for this attempt.");
        if (result.Plan is null)
            return Rejected("Result plan is required.");
        if (!PlanEquals(result.Plan, plan))
            return Rejected("Result plan does not match the locked plan.");
        if (!Enum.IsDefined(typeof(EncounterOutcome), result.Outcome))
            return Rejected("Result outcome is not defined.");

        var outcome = result.Outcome;
        var enemyKills = Math.Clamp(result.ConfirmedEnemyKills ?? (outcome == EncounterOutcome.Victory ? plan.Enemies.Sum(e => e.Count) : 0), 0, 12);
        var survivingAllies = Math.Clamp(result.SurvivingAllies ?? 0, 0, 7);
        if (state.Phase == RunPhase.LoadingEncounter &&
            outcome is not (EncounterOutcome.PrecombatAborted or EncounterOutcome.TechnicalFailure))
        {
            return Rejected("Combat outcomes are not accepted before combat starts.");
        }
        if (state.Phase == RunPhase.Fighting && outcome == EncounterOutcome.PrecombatAborted)
            return Rejected("PrecombatAborted is only valid while loading.");
        if (outcome == EncounterOutcome.Victory &&
            state.Victories >= FiniteRounds(state) &&
            state.BlockIndex < state.EraSchedule.Count)
        {
            return Rejected("The finite run is already complete.");
        }

        // Validate the entire victory transition on an isolated copy before committing.
        if (outcome == EncounterOutcome.Victory)
        {
            try
            {
                var candidate = CloneAndValidate(Snapshot!);
                candidate.LastEnemyKills = enemyKills;
                candidate.LastAlliesSurvived = survivingAllies;
                if (Schema6(candidate.Rewards)) candidate.Score = checked(candidate.Score + ScoreFor(state, plan, result).Points);
                ApplyVictory(candidate, plan);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
            { return Rejected(ex.Message); }
        }
        state.LastEnemyKills = enemyKills;
        state.LastAlliesSurvived = survivingAllies;
        state.ProcessedAttempts.Add(attempt);
        state.LastOutcome = outcome;
        state.LastOutcomeDetail = result.Detail;
        state.CurrentAttempt = null;
        // Every accepted attempt counts, including aborts and failures. Kill and
        // loss totals use only values the adapter could actually measure; null
        // stays null in the battle log instead of being invented.
        var friendlyCount = state.ConfirmedDesign?.FriendlyCountIncludingPlayer ?? 0;
        var destroyed = Math.Max(0, result.EnemiesDestroyed ?? result.ConfirmedEnemyKills ?? 0);
        var lost = Math.Max(0, result.FriendliesLost
            ?? (outcome == EncounterOutcome.FriendlyForceDestroyed ? friendlyCount
                : outcome == EncounterOutcome.Victory && result.SurvivingAllies is { } survived
                    ? Math.Max(0, friendlyCount - 1 - survived) : 0));
        if (Schema6(state.Rewards))
        {
            state.Attempts = AddClamped(state.Attempts, 1);
            state.EnemiesDestroyed = AddClamped(state.EnemiesDestroyed, destroyed);
            state.FriendliesLost = AddClamped(state.FriendliesLost, lost);
        }

        switch (outcome)
        {
            case EncounterOutcome.Victory:
                if (Schema6(state.Rewards))
                {
                    state.Score = checked(state.Score + ScoreFor(state, plan, result).Points);
                    state.RoundAttempts = 0;
                }
                ApplyVictory(state, plan);
                break;

            case EncounterOutcome.FriendlyForceDestroyed:
            case EncounterOutcome.CombatRetreat:
                if (!Schema6(state.Rewards) && outcome == EncounterOutcome.FriendlyForceDestroyed
                    && state.Rewards?.Active.Any(item => item.Kind == RewardKind.EmergencyRecovery) == true)
                {
                    state.Phase = RunPhase.AwaitingRecovery;
                    break;
                }
                state.Defeats = AddClamped(state.Defeats, 1);
                // Schema 6 keeps the round's pinned offers even on a terminal
                // loss, so a future retry-after-loss reuses the exact same cards.
                // Older schemas keep their historical consume/clear behavior.
                if (Schema6(state.Rewards))
                {
                    state.SelectedOfferId = plan.OfferId;
                    state.LockedPlan = null;
                    state.CurrentAttempt = null;
                    state.RoundAttempts = AddClamped(state.RoundAttempts, 1);
                    state.Phase = state.Ironman ? RunPhase.Lost : RunPhase.ChoosingEncounter;
                    // 0.3.4 (user: "Yes, after any fight"): a fought test round
                    // moves the cursor too. The pinned cards belong to the old
                    // offset, so the same round is offered again from the new one.
                    if (state.TestPool)
                    {
                        state.TestPoolOffset = checked(state.TestPoolOffset + plan.Enemies.Count);
                        ClearEncounterSelection(state);
                    }
                    break;
                }
                else
                {
                    ConsumeResolvedOffer(state, plan);
                    ClearEncounterSelection(state);
                }
                state.Phase = RunPhase.Lost;
                break;

            case EncounterOutcome.PrecombatAborted:
            case EncounterOutcome.TechnicalFailure:
                // No reward, no loss, no round advance. The locked plan and selection
                // survive so Resume can replay the exact encounter as a fresh attempt.
                state.SuspendedFrom = RunPhase.LoadingEncounter;
                state.Phase = RunPhase.Suspended;
                break;

            default:
                return Rejected("Unsupported outcome.");
        }

        return Accepted("Result processed once.");
    }

    // Leaves the finite run after the final included era. The final block's supply
    // is applied to the first endless block, whose base is the final-era base *
    // 1.15. The previous block's bonus is not carried into that base.
    public RunActionResult ContinueEndless()
    {
        if (_state is null)
            return Rejected("No run exists.");
        var state = _state;

        if (state.Phase != RunPhase.AwaitingEndlessChoice)
            return Rejected("Endless mode can only be chosen after the finite run.");
        if (state.Rewards?.Choices.Count > 0) return Rejected("Resolve the boss reward first.");
        var finalBlock = state.EraSchedule.Count - 1;
        if (state.Victories != FiniteRounds(state) ||
            state.BlockIndex != finalBlock)
        {
            return Rejected("The finite run is not complete.");
        }

        var nextBase = RunRules.FloorToMicro(
            state.BlockBaseBudget.Value * RunRules.EndlessGrowth);
        AdvanceBlock(state, nextBase, finalBlock + 1);
        state.EraIndex = finalBlock;
        state.Round = Battles(state) * state.EraSchedule.Count + 1;
        state.WinsInBlock = 0;
        state.Phase = RunPhase.Building;
        state.DesignConfirmedForBlock = false;
        ClearEncounterSelection(state);
        return Accepted("Endless mode started.");
    }

    public RunActionResult Finish()
    {
        if (_state is null)
            return Rejected("No run exists.");
        var state = _state;

        if (state.Phase != RunPhase.AwaitingEndlessChoice)
            return Rejected("The run can only be finished at the endless choice.");
        if (state.Rewards?.Choices.Count > 0) return Rejected("Resolve the boss reward first.");

        ClearEncounterSelection(state);
        state.Phase = RunPhase.Finished;
        return Accepted("Run finished.");
    }

    public RunActionResult Retire()
    {
        if (_state is null || !Schema6(_state.Rewards)
            || _state.Phase is not (RunPhase.Building or RunPhase.ChoosingEncounter))
            return Rejected("Retire is available in the schema-6 build or battle-choice phase.");
        ClearEncounterSelection(_state);
        _state.Phase = RunPhase.Retired;
        return Accepted("Run retired; record saved by the controller.");
    }

    private static BattleScore ScoreFor(RunState state, EncounterPlan plan, EncounterResult result) =>
        ScoreRules.Battle(state.EraIndex, plan.Risk, plan.Extras?.BossBlueprintId != null,
            result.FriendliesLost, state.RoundAttempts, state.OfferEconomy?.BaseDifficulty ?? 1.5m, state.Ironman);

    // User exit outside combat suspends. A live or loading attempt is also suspended
    // and will be replayed as a fresh generation after cleanup on Resume.
    public RunActionResult Suspend()
    {
        if (_state is null)
            return Rejected("No run exists.");
        var state = _state;

        if (state.Phase is not (RunPhase.Building or RunPhase.ChoosingEncounter or
            RunPhase.LoadingEncounter or RunPhase.Fighting or RunPhase.AwaitingEndlessChoice or RunPhase.AwaitingRecovery))
        {
            return Rejected("This phase cannot be suspended.");
        }

        state.SuspendedFrom = state.Phase;
        state.Phase = RunPhase.Suspended;
        return Accepted("Run suspended.");
    }

    public RunActionResult Resume()
    {
        if (_state is null)
            return Rejected("No run exists.");
        var state = _state;

        if (state.Phase != RunPhase.Suspended || state.SuspendedFrom is null)
            return Rejected("No suspended run exists.");

        var from = state.SuspendedFrom.Value;
        if (from is RunPhase.LoadingEncounter or RunPhase.Fighting)
        {
            var lockedPlan = state.LockedPlan;
            if (lockedPlan is null)
                return Rejected("No locked plan exists to retry.");
            if (state.SelectedOfferId is null)
                return Rejected("No selected offer exists to retry.");

            state.GenerationCounter++;
            state.CurrentAttempt = new AttemptIdentity(
                state.RunId, lockedPlan.OfferId, lockedPlan.PlanHash,
                state.GenerationCounter);
            state.Phase = RunPhase.LoadingEncounter;
        }
        else if (from is RunPhase.Building or RunPhase.ChoosingEncounter or
                 RunPhase.AwaitingEndlessChoice or RunPhase.AwaitingRecovery)
        {
            state.Phase = from;
        }
        else
        {
            return Rejected("Suspended phase cannot be resumed.");
        }

        state.SuspendedFrom = null;
        return Accepted("Run resumed.");
    }

    // Builds the deterministic request for IEncounterCatalogue. OfferRequest.Block is
    // the zero-based block index. The available budget already includes the current
    // supply bonus, so threat scaling sees the real fleet ceiling.
    public OfferRequest CreateOfferRequest()
    {
        var state = _state ?? throw new InvalidOperationException("No run exists.");
        if (state.Phase is not (RunPhase.Building or RunPhase.ChoosingEncounter))
            throw new InvalidOperationException("Offers cannot be requested in this phase.");
        if ((!state.DesignConfirmedForBlock || state.ConfirmedDesign is null) && !RefitBoundary(state))
            throw new InvalidOperationException("A design must be confirmed first.");
        if (state.Offers.Count != 0)
            throw new InvalidOperationException("Offers already exist for this round.");

        // Schema 6 separates the enemy ceiling from the player's item-boosted
        // fleet budget. Older schemas pass their item-inclusive available budget
        // exactly as before, so their generated offer bytes do not change.
        var offerBudget = Schema6(state.Rewards)
            ? new ScenarioValue(OfferBaseBudget(state), state.CostUnitId)
            : state.OfferEconomy?.Budget ?? state.AvailableBudget;
        return new OfferRequest(
            state.RunId,
            state.Seed,
            state.Round,
            state.BlockIndex,
            state.EraSchedule[state.EraIndex],
            offerBudget,
            state.PendingSupplyPoints,
            state.CatalogueVersion)
        {
            TestPool = state.TestPool, TestPoolOffset = state.TestPoolOffset,
            RulesVersion = state.Rewards?.Version ?? 1,
            OfferRoll = state.Rewards?.OfferRoll ?? 0,
            EnemyBudgetMultiplier = state.OfferEconomy?.EnemyBudgetMultiplier
                ?? (state.Rewards?.Active.Any(item => item.Kind == RewardKind.ExperimentalContract) == true ? 1.2m : 1m),
            EraProgressionEnabled = state.OfferEconomy?.EraProgression ?? false,
            GenerationPolicy = state.GenerationPolicy
                ?? (state.Rewards?.Version == 5 ? 5 : 0)
        };
    }

    public EncounterRequest CreateEncounterRequest()
    {
        var state = _state ?? throw new InvalidOperationException("No run exists.");
        if (state.Phase is not (RunPhase.LoadingEncounter or RunPhase.Fighting))
            throw new InvalidOperationException("No encounter attempt is active.");

        var lockedPlan = state.LockedPlan;
        var attempt = state.CurrentAttempt;
        var design = state.ConfirmedDesign;
        if (lockedPlan is null || attempt is null || design is null)
            throw new InvalidOperationException("Encounter attempt is incomplete.");

        return new EncounterRequest(
            attempt,
            CopyPlan(lockedPlan),
            CopyBlueprint(design.Blueprint),
            design.FriendlyCountIncludingPlayer)
        {
            TestPool = state.TestPool,
            Reinforcements = state.Rewards?.Active.Where(item => item.Kind is RewardKind.LendLease or RewardKind.CapturedPrototype)
                .Where(item => item.BlueprintId != null).Take(Math.Min(1, design.FriendlyCountIncludingPlayer - 1))
                .Select(item => new EnemyUnit(item.BlueprintId!, 1)).ToArray() ?? Array.Empty<EnemyUnit>()
        };
    }

    // Persistence restore. A snapshot saved while loading or fighting cannot resume
    // the old callback: the same locked plan is replayed under a new generation.
    // Suspended snapshots remain suspended and require an explicit Resume.
    public static RunDirector Restore(RunSnapshot snapshot)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        var state = CloneAndValidate(snapshot);
        if (state.Phase is RunPhase.LoadingEncounter or RunPhase.Fighting)
        {
            var lockedPlan = state.LockedPlan;
            if (lockedPlan is null)
                throw new ArgumentException("Active snapshot has no locked plan.", nameof(snapshot));
            state.GenerationCounter++;
            state.CurrentAttempt = new AttemptIdentity(
                state.RunId, lockedPlan.OfferId, lockedPlan.PlanHash,
                state.GenerationCounter);
            state.Phase = RunPhase.LoadingEncounter;
            state.SuspendedFrom = null;
        }

        return new RunDirector { _state = state };
    }

    private static void ApplyVictory(RunState state, EncounterPlan plan)
    {
        if (state.TestPool) state.TestPoolOffset = checked(state.TestPoolOffset + plan.Enemies.Count);
        state.Victories++;
        state.WinsInBlock++;
        state.PendingSupplyPoints += plan.SupplyReward;
        if (state.Rewards != null)
        {
            if (plan.Extras == null) throw new InvalidOperationException("Adventure encounter has no locked reward.");
            state.Rewards = RewardRules.Victory(state.Rewards, plan.Extras.Reward, plan.OfferId,
                plan.Extras.BossBlueprintId != null, state.Seed, plan.Round, state.BlockBaseBudget.Value,
                state.LastEnemyKills, state.LastAlliesSurvived);
            state.Rewards = state.Rewards with { Revision = checked(state.Rewards.Revision + 1) };
            if (plan.Extras.BossBlueprintId != null)
                state.Rewards = state.Rewards with { Choices = state.Rewards.Choices.Select((item, index) => index == 0
                    ? plan.Extras.Reward with { Id = plan.OfferId + ".captured-prototype" } : item).ToArray() };
            RefreshBudget(state);
        }
        state.Round++;
        ConsumeResolvedOffer(state, plan);
        ClearEncounterSelection(state);

        if (state.BlockIndex < state.EraSchedule.Count &&
            state.Victories >= FiniteRounds(state))
        {
            state.Phase = RunPhase.AwaitingEndlessChoice;
            return;
        }

        if (state.WinsInBlock == Battles(state))
        {
            if (state.BlockIndex < state.EraSchedule.Count - 1)
            {
                var nextIndex = state.BlockIndex + 1;
                var nextBase = RunRules.FloorToMicro(
                    state.InitialBaseBudget.Value *
                    RunRules.EraBaseMultiplier(nextIndex));
                AdvanceBlock(state, nextBase, nextIndex);
                state.EraIndex = nextIndex;
                state.WinsInBlock = 0;
                state.Phase = RunPhase.Building;
                state.DesignConfirmedForBlock = false;
            }
            else
            {
                var nextBase = RunRules.FloorToMicro(
                    state.BlockBaseBudget.Value * RunRules.EndlessGrowth);
                AdvanceBlock(state, nextBase, state.BlockIndex + 1);
                state.EraIndex = state.EraSchedule.Count - 1;
                state.WinsInBlock = 0;
                state.Phase = RunPhase.Building;
                state.DesignConfirmedForBlock = false;
            }

            return;
        }

        state.Phase = state.DesignConfirmedForBlock ? RunPhase.ChoosingEncounter : RunPhase.Building;
    }

    private static void AdvanceBlock(RunState state, decimal nextBase, int nextBlockIndex)
    {
        var bonus = RunRules.SupplyBonus(nextBase, state.PendingSupplyPoints);
        state.BlockIndex = nextBlockIndex;
        state.BlockBaseBudget = new ScenarioValue(nextBase, state.CostUnitId);
        state.SupplyBonus = bonus;
        state.AvailableBudget = new ScenarioValue(nextBase + bonus, state.CostUnitId);
        RefreshBudget(state);
        state.PendingSupplyPoints = 0;
    }

    private static void ConsumeResolvedOffer(RunState state, EncounterPlan plan) =>
        state.ConsumedOfferIds.Add(plan.OfferId);

    private static void ClearEncounterSelection(RunState state)
    {
        state.OfferEconomy = null;
        state.Offers.Clear();
        state.SelectedOfferId = null;
        state.LockedPlan = null;
        state.CurrentAttempt = null;
    }

    private static EncounterPlan? FindOffer(RunState state, string offerId)
    {
        foreach (var offer in state.Offers)
        {
            if (string.Equals(offer.OfferId, offerId, StringComparison.Ordinal))
                return offer;
        }

        return null;
    }

    private static bool TryCalculateFriendlyCount(
        decimal availableBudget, decimal designPrice, out int friendlyCount)
    {
        friendlyCount = 0;
        if (designPrice <= 0m || availableBudget <= 0m)
            return false;

        var floor = decimal.Floor(availableBudget / designPrice);
        if (floor < 1m)
            return false;

        friendlyCount = floor > RunRules.MaxFriendlyIncludingPlayer
            ? RunRules.MaxFriendlyIncludingPlayer
            : (int)floor;
        return friendlyCount is >= 1 and <= RunRules.MaxFriendlyIncludingPlayer;
    }

    private static bool ValidateOfferSet(
        RunState state, IReadOnlyList<EncounterPlan>? offers, out string reason)
    {
        reason = string.Empty;
        if (offers is null)
        {
            reason = "An offer set is required.";
            return false;
        }
        if (offers.Count != OfferCount(state))
        {
            reason = "The round requires " + OfferCount(state) + " encounter card(s).";
            return false;
        }

        var era = state.EraSchedule[state.EraIndex];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var risks = new HashSet<EncounterRisk>();
        var signatures = new List<string>(offers.Count);

        foreach (var plan in offers)
        {
            if (plan is null)
            {
                reason = "Offer plan is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(plan.OfferId))
            {
                reason = "Offer ID is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(plan.PlanHash))
            {
                reason = "Offer plan hash is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(plan.MapId) ||
                string.IsNullOrWhiteSpace(plan.SpawnLayoutId))
            {
                reason = "Offer map and spawn layout are required.";
                return false;
            }
            if ((state.Rewards == null) != (plan.Extras == null)
                || (plan.Extras != null && (plan.Extras.RulesVersion != state.Rewards!.Version || plan.Extras.OfferRoll != state.Rewards!.OfferRoll)))
            { reason = "Offer reward rules or generation does not match this run."; return false; }
            if (plan.Extras != null)
            {
                try { RewardRules.Validate(new RewardState { Version = plan.Extras.RulesVersion, Inventory = new[] { plan.Extras.Reward } }); }
                catch (InvalidOperationException ex) { reason = ex.Message; return false; }
                if (!SpecialBattleRules.TryValidatePlan(plan.Extras, plan.Enemies, out var formationError))
                { reason = formationError; return false; }
            }
            if (plan.Extras?.CuratedBoss != (OfferCount(state) == 1) && state.Rewards?.Version >= 3)
            { reason = "Curated boss marker does not match this round."; return false; }
            if (OfferCount(state) == 1 && (plan.Risk != EncounterRisk.Standard || plan.Extras?.BossBlueprintId == null))
            { reason = "The curated boss uses its fixed Standard profile."; return false; }
            var enemies = plan.Enemies;
            var mods = plan.RequiredMods;
            if (enemies is null || enemies.Count == 0 || mods is null)
            {
                reason = "Offer enemy roster and mod list are required.";
                return false;
            }
            if (!Enum.IsDefined(typeof(EncounterRisk), plan.Risk))
            {
                reason = "Offer risk is not defined.";
                return false;
            }
            if (plan.SupplyReward != RunRules.SupplyRewardFor(plan.Risk))
            {
                reason = "Offer supply reward does not match its risk.";
                return false;
            }
            if (plan.Round != state.Round)
            {
                reason = "Offer round does not match the current round.";
                return false;
            }
            var planEra = plan.Era;
            if (planEra is null ||
                !string.Equals(planEra.NativeId, era.NativeId, StringComparison.Ordinal))
            {
                reason = "Offer era does not match the current native era.";
                return false;
            }
            if (!string.Equals(plan.CatalogueVersion, state.CatalogueVersion,
                    StringComparison.Ordinal))
            {
                reason = "Offer catalogue version does not match the run.";
                return false;
            }
            if (!ids.Add(plan.OfferId))
            {
                reason = "Offer IDs must be unique within the set.";
                return false;
            }
            if (state.ConsumedOfferIds.Contains(plan.OfferId))
            {
                reason = "A resolved offer ID cannot be reused.";
                return false;
            }
            if (!hashes.Add(plan.PlanHash))
            {
                reason = "Offer plan hashes must be unique within the set.";
                return false;
            }

            long enemyTotal = 0;
            foreach (var enemy in enemies)
            {
                if (enemy is null || string.IsNullOrWhiteSpace(enemy.BlueprintId) ||
                    enemy.Count <= 0)
                {
                    reason = "Every enemy entry needs a blueprint ID and positive count.";
                    return false;
                }

                enemyTotal += enemy.Count;
                if (enemyTotal > RunRules.MaxEnemies)
                {
                    reason = "Enemy total must not exceed twelve.";
                    return false;
                }
            }

            if (enemyTotal < 1)
            {
                reason = "Every offer needs at least one enemy.";
                return false;
            }
            foreach (var mod in mods)
            {
                if (mod is null || string.IsNullOrWhiteSpace(mod.Id))
                {
                    reason = "Offer required-mod entries need an ID.";
                    return false;
                }
            }

            risks.Add(plan.Risk);
            signatures.Add(DifficultySignature(plan));
        }

        if (risks.Count != OfferCount(state))
        {
            reason = "Exactly one cautious, standard and dangerous offer is required.";
            return false;
        }

        for (var i = 0; i < signatures.Count; i++)
        {
            for (var j = i + 1; j < signatures.Count; j++)
            {
                if (string.Equals(signatures[i], signatures[j], StringComparison.Ordinal))
                {
                    reason = "Offer cards must differ in roster, map or spawn layout.";
                    return false;
                }
            }
        }

        return true;
    }

    private static string DifficultySignature(EncounterPlan plan)
    {
        var enemies = plan.Enemies
            .OrderBy(enemy => enemy.BlueprintId, StringComparer.Ordinal)
            .ThenBy(enemy => enemy.Count)
            .Select(enemy => enemy.BlueprintId + ":" +
                enemy.Count.ToString(CultureInfo.InvariantCulture));
        return plan.MapId + "\u001f" + plan.SpawnLayoutId + "\u001f" +
            string.Join(";", enemies);
    }

    private static bool PlanEquals(EncounterPlan left, EncounterPlan right)
    {
        if (!string.Equals(left.OfferId, right.OfferId, StringComparison.Ordinal) ||
            !string.Equals(left.PlanHash, right.PlanHash, StringComparison.Ordinal) ||
            left.Round != right.Round ||
            !string.Equals(left.MapId, right.MapId, StringComparison.Ordinal) ||
            !string.Equals(left.SpawnLayoutId, right.SpawnLayoutId,
                StringComparison.Ordinal) ||
            left.Risk != right.Risk ||
            left.SupplyReward != right.SupplyReward ||
            System.Text.Json.JsonSerializer.Serialize(left.Extras) != System.Text.Json.JsonSerializer.Serialize(right.Extras) ||
            !string.Equals(left.CatalogueVersion, right.CatalogueVersion,
                StringComparison.Ordinal))
        {
            return false;
        }

        var leftEra = left.Era;
        var rightEra = right.Era;
        if (leftEra is null || rightEra is null ||
            !string.Equals(leftEra.NativeId, rightEra.NativeId, StringComparison.Ordinal))
        {
            return false;
        }

        var leftEnemies = left.Enemies;
        var rightEnemies = right.Enemies;
        if (leftEnemies is null || rightEnemies is null ||
            leftEnemies.Count != rightEnemies.Count)
        {
            return false;
        }
        for (var i = 0; i < leftEnemies.Count; i++)
        {
            var a = leftEnemies[i];
            var b = rightEnemies[i];
            if (a is null || b is null ||
                !string.Equals(a.BlueprintId, b.BlueprintId, StringComparison.Ordinal) ||
                a.Count != b.Count)
            {
                return false;
            }
        }

        var leftMods = left.RequiredMods;
        var rightMods = right.RequiredMods;
        if (leftMods is null || rightMods is null || leftMods.Count != rightMods.Count)
            return false;
        for (var i = 0; i < leftMods.Count; i++)
        {
            var a = leftMods[i];
            var b = rightMods[i];
            if (a is null || b is null ||
                !string.Equals(a.Id, b.Id, StringComparison.Ordinal) ||
                !string.Equals(a.Version, b.Version, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static RunState CloneAndValidate(RunSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7) || (snapshot.SchemaVersion >= 2) != (snapshot.Rewards != null)
            || (snapshot.Rewards != null && snapshot.Rewards.Version != snapshot.SchemaVersion))
            throw new ArgumentException("Unsupported snapshot schema version.", nameof(snapshot));
        var battles = snapshot.Rewards == null ? 3 : 4;
        if (snapshot.Rewards != null) RewardRules.Validate(snapshot.Rewards);
        if (!Enum.IsDefined(typeof(RunPhase), snapshot.Phase) ||
            snapshot.Phase == RunPhase.Resolving)
        {
            throw new ArgumentException("Snapshot phase is not persistable.", nameof(snapshot));
        }
        if (string.IsNullOrWhiteSpace(snapshot.RunId) ||
            string.IsNullOrWhiteSpace(snapshot.CatalogueVersion) ||
            string.IsNullOrWhiteSpace(snapshot.CostUnitId))
        {
            throw new ArgumentException("Snapshot identity is incomplete.", nameof(snapshot));
        }
        if (snapshot.EraSchedule is null ||
            !EraSchedulePolicy.IsValidEraCount(snapshot.EraSchedule.Count))
        {
            throw new ArgumentException("Snapshot era schedule must contain 2 to 32 eras.",
                nameof(snapshot));
        }
        var eraCount = snapshot.EraSchedule.Count;
        if (snapshot.BlockBaseBudget is null || snapshot.AvailableBudget is null ||
            snapshot.InitialBaseBudget is null)
        {
            throw new ArgumentException("Snapshot budget is missing.", nameof(snapshot));
        }
        if (snapshot.Offers is null || snapshot.ProcessedAttempts is null ||
            snapshot.ConsumedOfferIds is null)
        {
            throw new ArgumentException("Snapshot collections are missing.", nameof(snapshot));
        }
        var initialBase = snapshot.InitialBaseBudget;
        var blockBase = snapshot.BlockBaseBudget;
        var available = snapshot.AvailableBudget;
        if (snapshot.OfferEconomy is { } economy && (snapshot.Offers.Count == 0
            || economy.Budget == null || economy.Budget.Value <= 0 || economy.Budget.UnitId != snapshot.CostUnitId
            || economy.EnemyBudgetMultiplier is < 1m or > 2.4m))
            throw new ArgumentException("Pinned offer economy is invalid.", nameof(snapshot));
        if (snapshot.GenerationPolicy is { } policy && policy != 5)
            throw new ArgumentException("Snapshot generation policy is not supported.", nameof(snapshot));
        if (snapshot.GenerationPolicy == 5
            && (snapshot.Rewards is null || snapshot.Rewards.Version < 4))
            throw new ArgumentException("The policy-5 overlay requires an adventure-v4 or newer run.", nameof(snapshot));
        if (snapshot.Phase == RunPhase.Suspended)
        {
            if (snapshot.SuspendedFrom is null ||
                !Enum.IsDefined(typeof(RunPhase), snapshot.SuspendedFrom.Value) ||
                snapshot.SuspendedFrom is RunPhase.Suspended or RunPhase.Resolving or
                    RunPhase.Lost or RunPhase.Finished or RunPhase.Retired)
            {
                throw new ArgumentException("Snapshot suspension phase is invalid.", nameof(snapshot));
            }
        }
        else if (snapshot.SuspendedFrom is not null)
        {
            throw new ArgumentException("Only suspended snapshots carry SuspendedFrom.", nameof(snapshot));
        }

        if (!EraSchedulePolicy.IsValidStartingEra(snapshot.StartingEraIndex, eraCount) ||
            snapshot.BlockIndex < snapshot.StartingEraIndex || snapshot.EraIndex < 0 ||
            snapshot.EraIndex >= eraCount ||
            snapshot.Round < 1 || (snapshot.WinsInBlock < 0 || snapshot.WinsInBlock > battles) ||
            snapshot.Victories < 0 || snapshot.Defeats < 0 || (snapshot.SchemaVersion < 6 && snapshot.Defeats > 1) ||
            (snapshot.PendingSupplyPoints < 0 || snapshot.PendingSupplyPoints > battles * 3) ||
            snapshot.GenerationCounter < 0 || snapshot.SupplyBonus < 0m ||
            snapshot.EnemiesDestroyed < 0 || snapshot.FriendliesLost < 0 || snapshot.Attempts < 0
            || snapshot.RoundAttempts < 0 || snapshot.Score < 0m
            || snapshot.TestPoolOffset < 0 || (!snapshot.TestPool && snapshot.TestPoolOffset != 0)
            || (snapshot.TestPool && snapshot.SchemaVersion < 6)
            // 0.3.1 advanced one cursor step per card; 0.3.2 one per tank fought
            // in a won round; 0.3.4 also per lost round, each of which adds a Defeat.
            || (snapshot.TestPool && snapshot.TestPoolOffset != 3L * snapshot.Victories
                - 2L * Math.Min(snapshot.Victories / 4, eraCount - snapshot.StartingEraIndex)
                && snapshot.TestPoolOffset > (long)TestPoolPlanner.TanksPerBattle * ((long)snapshot.Victories + snapshot.Defeats))
            || (snapshot.SchemaVersion < 6 && (snapshot.Ironman || snapshot.Score != 0m || snapshot.RoundAttempts != 0))
            || (snapshot.SchemaVersion < 6 && snapshot.Phase == RunPhase.Retired)
            || (snapshot.SchemaVersion >= 6 && (snapshot.Phase == RunPhase.AwaitingRecovery
                || snapshot.SuspendedFrom == RunPhase.AwaitingRecovery)))
        {
            throw new ArgumentException("Snapshot counters are invalid.", nameof(snapshot));
        }
        if (snapshot.EraIndex != (snapshot.BlockIndex < eraCount
                ? snapshot.BlockIndex
                : eraCount - 1))
        {
            throw new ArgumentException("Snapshot era index is inconsistent.", nameof(snapshot));
        }
        if (snapshot.Round !=
            snapshot.BlockIndex * battles + snapshot.WinsInBlock + 1)
        {
            throw new ArgumentException("Snapshot round is inconsistent.", nameof(snapshot));
        }
        if (snapshot.Victories !=
            (snapshot.BlockIndex - snapshot.StartingEraIndex) * battles + snapshot.WinsInBlock)
        {
            throw new ArgumentException("Snapshot victory count is inconsistent.", nameof(snapshot));
        }
        if (!string.Equals(initialBase.UnitId, snapshot.CostUnitId,
                StringComparison.Ordinal) ||
            !string.Equals(blockBase.UnitId, snapshot.CostUnitId,
                StringComparison.Ordinal) ||
            !string.Equals(available.UnitId, snapshot.CostUnitId,
                StringComparison.Ordinal) ||
            initialBase.Value <= 0m ||
            blockBase.Value <= 0m ||
            available.Value != Budget(snapshot.Rewards, blockBase.Value + snapshot.SupplyBonus))
        {
            throw new ArgumentException("Snapshot budget is inconsistent.", nameof(snapshot));
        }
        if (snapshot.BlockIndex < eraCount)
        {
            var expectedBase = snapshot.BlockIndex == 0
                ? initialBase.Value
                : RunRules.FloorToMicro(
                    initialBase.Value *
                    RunRules.EraBaseMultiplier(snapshot.BlockIndex));
            if (blockBase.Value != expectedBase)
            {
                throw new ArgumentException("Snapshot finite block base is inconsistent.",
                    nameof(snapshot));
            }
        }

        var eraIds = new HashSet<string>(StringComparer.Ordinal);
        var eras = new List<EraReference>(snapshot.EraSchedule.Count);
        foreach (var era in snapshot.EraSchedule)
        {
            if (era is null || string.IsNullOrWhiteSpace(era.NativeId) ||
                !eraIds.Add(era.NativeId))
            {
                throw new ArgumentException("Snapshot era schedule is invalid.", nameof(snapshot));
            }
            eras.Add(era);
        }

        if (snapshot.Phase == RunPhase.AwaitingEndlessChoice &&
            (snapshot.Victories != battles * (eraCount - snapshot.StartingEraIndex) ||
             snapshot.BlockIndex != eraCount - 1))
        {
            throw new ArgumentException("Snapshot endless choice is inconsistent.", nameof(snapshot));
        }
        if (snapshot.Phase == RunPhase.Lost && snapshot.Defeats < 1)
            throw new ArgumentException("Lost snapshot has no defeat.", nameof(snapshot));
        if (snapshot.SchemaVersion < 6 && snapshot.Phase != RunPhase.Lost && snapshot.Defeats != 0)
            throw new ArgumentException("Snapshot has a defeat without being lost.", nameof(snapshot));

        var state = new RunState
        {
            RunId = snapshot.RunId,
            Seed = snapshot.Seed,
            CatalogueVersion = snapshot.CatalogueVersion,
            CostUnitId = snapshot.CostUnitId,
            InitialBaseBudget = initialBase,
            StartingEraIndex = snapshot.StartingEraIndex,
            OfferEconomy = snapshot.OfferEconomy,
            GenerationPolicy = snapshot.GenerationPolicy,
            EraSchedule = eras,
            Phase = snapshot.Phase,
            SuspendedFrom = snapshot.SuspendedFrom,
            Round = snapshot.Round,
            BlockIndex = snapshot.BlockIndex,
            EraIndex = snapshot.EraIndex,
            WinsInBlock = snapshot.WinsInBlock,
            Victories = snapshot.Victories,
            Defeats = snapshot.Defeats,
            BlockBaseBudget = blockBase,
            SupplyBonus = snapshot.SupplyBonus,
            AvailableBudget = available,
            PendingSupplyPoints = snapshot.PendingSupplyPoints,
            ConfirmedDesign = snapshot.ConfirmedDesign is null
                ? null
                : CopyDesign(snapshot.ConfirmedDesign),
            DesignConfirmedForBlock = snapshot.DesignConfirmedForBlock,
            Offers = snapshot.Offers is null
                ? new List<EncounterPlan>()
                : snapshot.Offers.Select(CopyPlan).ToList(),
            SelectedOfferId = snapshot.SelectedOfferId,
            LockedPlan = snapshot.LockedPlan is null ? null : CopyPlan(snapshot.LockedPlan),
            CurrentAttempt = snapshot.CurrentAttempt,
            GenerationCounter = snapshot.GenerationCounter,
            ProcessedAttempts = snapshot.ProcessedAttempts is null
                ? new List<AttemptIdentity>()
                : new List<AttemptIdentity>(snapshot.ProcessedAttempts),
            ConsumedOfferIds = snapshot.ConsumedOfferIds is null
                ? new List<string>()
                : new List<string>(snapshot.ConsumedOfferIds),
            LastOutcome = snapshot.LastOutcome,
            LastOutcomeDetail = snapshot.LastOutcomeDetail,
            Rewards = CloneRewards(snapshot.Rewards),
            EnemiesDestroyed = snapshot.EnemiesDestroyed,
            FriendliesLost = snapshot.FriendliesLost,
            Attempts = snapshot.Attempts, RoundAttempts = snapshot.RoundAttempts,
            Ironman = snapshot.Ironman, Score = snapshot.Score,
            TestPool = snapshot.TestPool, TestPoolOffset = snapshot.TestPoolOffset
        };

        ValidateSnapshotState(snapshot, state);
        return state;
    }

    private static void ValidateSnapshotState(RunSnapshot snapshot, RunState state)
    {
        if (state.ConfirmedDesign is not null)
        {
            var design = state.ConfirmedDesign!;
            var blueprint = design.Blueprint;
            var cost = design.Cost;
            if (blueprint is null ||
                string.IsNullOrWhiteSpace(blueprint.LocalId) ||
                string.IsNullOrWhiteSpace(blueprint.SnapshotHash) ||
                blueprint.Era is null ||
                string.IsNullOrWhiteSpace(blueprint.Era.NativeId) ||
                cost is null ||
                !string.Equals(cost.UnitId, state.CostUnitId,
                    StringComparison.Ordinal) ||
                cost.Value <= 0m ||
                design.FriendlyCountIncludingPlayer is < 1 or > RunRules.MaxFriendlyIncludingPlayer)
            {
                throw new ArgumentException("Snapshot design is invalid.", nameof(snapshot));
            }

            if (state.DesignConfirmedForBlock &&
                (!CalculateFleet(state, cost.Value, out var expectedCount) ||
                 expectedCount != design.FriendlyCountIncludingPlayer))
            {
                throw new ArgumentException("Snapshot design count is inconsistent.",
                    nameof(snapshot));
            }
        }
        else if (state.DesignConfirmedForBlock)
        {
            throw new ArgumentException("Confirmed snapshot has no design.", nameof(snapshot));
        }

        var selectedRefit = (state.SelectedOfferId != null && state.OfferEconomy != null && state.ConfirmedDesign != null)
            || RefitBoundary(state);
        if ((state.Phase == RunPhase.ChoosingEncounter && !selectedRefit) || state.Phase is RunPhase.LoadingEncounter or
            RunPhase.Fighting or RunPhase.AwaitingRecovery || (state.Phase == RunPhase.AwaitingEndlessChoice && state.Rewards == null))
        {
            if (state.ConfirmedDesign is null || !state.DesignConfirmedForBlock)
                throw new ArgumentException("Snapshot phase requires a confirmed design.",
                    nameof(snapshot));
        }
        if (state.Phase == RunPhase.Suspended &&
            ((state.SuspendedFrom == RunPhase.ChoosingEncounter && !selectedRefit) ||
                state.SuspendedFrom is RunPhase.LoadingEncounter or RunPhase.Fighting or RunPhase.AwaitingRecovery) &&
            (state.ConfirmedDesign is null || !state.DesignConfirmedForBlock))
        {
            throw new ArgumentException("Suspended snapshot requires a confirmed design.",
                nameof(snapshot));
        }
        if (state.Rewards == null && state.Phase == RunPhase.Suspended && state.SuspendedFrom == RunPhase.AwaitingEndlessChoice
            && (state.ConfirmedDesign == null || !state.DesignConfirmedForBlock))
            throw new ArgumentException("Legacy endless choice requires a confirmed design.", nameof(snapshot));

        if (state.Offers.Count != 0 && state.Offers.Count != OfferCount(state))
            throw new ArgumentException("Snapshot offer count is invalid.", nameof(snapshot));
        if (state.Offers.Count > 0 &&
            !ValidateOfferSet(state, state.Offers, out var offerReason))
        {
            throw new ArgumentException("Snapshot offers are invalid: " + offerReason,
                nameof(snapshot));
        }
        if (state.Phase == RunPhase.Building && state.Offers.Count != 0)
            throw new ArgumentException("Building snapshot must not have offers.", nameof(snapshot));
        if ((state.Phase is (RunPhase.LoadingEncounter or RunPhase.Fighting)) &&
            state.Offers.Count != OfferCount(state))
        {
            throw new ArgumentException("Active snapshot must have offers.", nameof(snapshot));
        }
        // Schema 6 deliberately keeps the round's fixed cards on a terminal
        // loss, ready for a future retry-after-loss. Older schemas stay strict.
        var fixedLossOffers = state.Phase == RunPhase.Lost && state.Rewards?.Version >= 6;
        if ((state.Phase is (RunPhase.AwaitingEndlessChoice or RunPhase.Lost or
                RunPhase.Finished or RunPhase.Retired)) && state.Offers.Count != 0 && !fixedLossOffers)
        {
            throw new ArgumentException("Terminal snapshot must not have offers.", nameof(snapshot));
        }

        if (state.SelectedOfferId is not null &&
            FindOffer(state, state.SelectedOfferId) is null)
        {
            throw new ArgumentException("Snapshot selection is not in offers.", nameof(snapshot));
        }
        if (state.SelectedOfferId is not null &&
            state.ConsumedOfferIds.Contains(state.SelectedOfferId))
        {
            throw new ArgumentException("Snapshot selection was already consumed.", nameof(snapshot));
        }

        if ((state.Phase == RunPhase.AwaitingRecovery || (state.Phase == RunPhase.Suspended && state.SuspendedFrom == RunPhase.AwaitingRecovery)) && (state.Rewards == null
            || !state.Rewards.Active.Any(item => item.Kind == RewardKind.EmergencyRecovery)
            || state.LastOutcome != EncounterOutcome.FriendlyForceDestroyed || state.CurrentAttempt != null
            || state.LockedPlan == null || state.Offers.Count != OfferCount(state)))
            throw new ArgumentException("Invalid pending recovery checkpoint.", nameof(snapshot));
        var lockedPlan = state.LockedPlan;
        if (lockedPlan is not null)
        {
            if (state.SelectedOfferId is null ||
                !string.Equals(lockedPlan.OfferId, state.SelectedOfferId,
                    StringComparison.Ordinal) ||
                FindOffer(state, lockedPlan.OfferId) is null)
            {
                throw new ArgumentException("Snapshot locked plan is inconsistent.",
                    nameof(snapshot));
            }
        }
        if (state.Phase is RunPhase.LoadingEncounter or RunPhase.Fighting)
        {
            if (lockedPlan is null || state.CurrentAttempt is null)
                throw new ArgumentException("Active snapshot has no attempt.", nameof(snapshot));
        }
        if (state.Phase == RunPhase.Suspended &&
            state.SuspendedFrom is (RunPhase.LoadingEncounter or RunPhase.Fighting) &&
            lockedPlan is null)
        {
            throw new ArgumentException("Suspended combat snapshot has no locked plan.",
                nameof(snapshot));
        }
        if ((state.Phase is (RunPhase.Lost or RunPhase.Finished or RunPhase.Retired)) &&
            (lockedPlan is not null || state.CurrentAttempt is not null))
        {
            throw new ArgumentException("Terminal snapshot still has an attempt.", nameof(snapshot));
        }

        var currentAttempt = state.CurrentAttempt;
        if (currentAttempt is not null)
        {
            if (!string.Equals(currentAttempt.RunId, state.RunId, StringComparison.Ordinal) ||
                currentAttempt.Generation < 1 ||
                currentAttempt.Generation > state.GenerationCounter ||
                lockedPlan is null ||
                !string.Equals(currentAttempt.EncounterId, lockedPlan.OfferId,
                    StringComparison.Ordinal) ||
                !string.Equals(currentAttempt.PlanHash, lockedPlan.PlanHash,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException("Snapshot current attempt is invalid.",
                    nameof(snapshot));
            }
        }

        var processed = new HashSet<AttemptIdentity>();
        foreach (var attempt in state.ProcessedAttempts)
        {
            if (attempt is null ||
                !string.Equals(attempt.RunId, state.RunId, StringComparison.Ordinal) ||
                attempt.Generation < 1 ||
                attempt.Generation > state.GenerationCounter ||
                !processed.Add(attempt))
            {
                throw new ArgumentException("Snapshot processed attempts are invalid.",
                    nameof(snapshot));
            }
            if (currentAttempt is not null && attempt == currentAttempt)
                throw new ArgumentException("Snapshot active attempt was already processed.",
                    nameof(snapshot));
        }

        var consumed = new HashSet<string>(state.ConsumedOfferIds, StringComparer.Ordinal);
        if (consumed.Count != state.ConsumedOfferIds.Count)
            throw new ArgumentException("Snapshot consumed offer IDs are not unique.",
                nameof(snapshot));
        if (state.Rewards != null && (state.Rewards.GrantedOffers.Count != state.Victories
            || state.Rewards.GrantedOffers.Any(id => !consumed.Contains(id))))
            throw new ArgumentException("Reward grants do not match the committed victories.", nameof(snapshot));
        if (state.LastOutcome is not null &&
            !Enum.IsDefined(typeof(EncounterOutcome), state.LastOutcome.Value))
        {
            throw new ArgumentException("Snapshot last outcome is invalid.", nameof(snapshot));
        }
    }

    private static EncounterPlan CopyPlan(EncounterPlan plan)
    {
        var enemies = plan.Enemies;
        var enemyCopy = enemies is null
            ? Array.Empty<EnemyUnit>()
            : enemies.ToArray();
        var planMods = plan.RequiredMods;
        var modCopy = planMods is null
            ? Array.Empty<ModRequirement>()
            : planMods.ToArray();
        return plan with
        {
            Enemies = new ReadOnlyCollection<EnemyUnit>(enemyCopy),
            RequiredMods = new ReadOnlyCollection<ModRequirement>(modCopy),
            Extras = plan.Extras?.Special is { } special ? plan.Extras with
            { Special = special with { Enemies = special.Enemies.ToArray() } } : plan.Extras
        };
    }

    private static BlueprintReference CopyBlueprint(BlueprintReference blueprint)
    {
        var blueprintMods = blueprint.RequiredMods;
        var modCopy = blueprintMods is null
            ? Array.Empty<ModRequirement>()
            : blueprintMods.ToArray();
        return blueprint with
        {
            RequiredMods = new ReadOnlyCollection<ModRequirement>(modCopy)
        };
    }

    private static RunDesignState CopyDesign(RunDesignState design)
    {
        var blueprint = design.Blueprint;
        if (blueprint is null)
            return design;
        return design with { Blueprint = CopyBlueprint(blueprint) };
    }

    private static int AddClamped(int value, int addition)
    {
        var total = (long)value + addition;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private static IReadOnlyList<T> ReadOnlyCopy<T>(IEnumerable<T> source) =>
        new ReadOnlyCollection<T>(source.ToArray());

    private static IReadOnlyList<EncounterPlan> ReadOnlyPlans(IEnumerable<EncounterPlan> plans) =>
        new ReadOnlyCollection<EncounterPlan>(plans.Select(CopyPlan).ToArray());

    private static RunActionResult Accepted(string reason) =>
        new(RunActionStatus.Accepted, reason);

    private static RunActionResult Rejected(string reason) =>
        new(RunActionStatus.Rejected, reason);

    private static RunActionResult Stale(string reason) =>
        new(RunActionStatus.IgnoredStale, reason);

    private static RunActionResult Duplicate(string reason) =>
        new(RunActionStatus.IgnoredDuplicate, reason);

    private sealed class RunState
    {
        public string RunId = string.Empty;
        public long Seed;
        public string CatalogueVersion = string.Empty;
        public string CostUnitId = string.Empty;
        public ScenarioValue InitialBaseBudget = new(0m, string.Empty);
        public List<EraReference> EraSchedule = new();
        public RunPhase Phase;
        public RunPhase? SuspendedFrom;
        public int Round;
        public int BlockIndex;
        public int EraIndex;
        public int StartingEraIndex;
        public OfferEconomy? OfferEconomy;
        public int? GenerationPolicy;
        public int WinsInBlock;
        public int Victories;
        public int Defeats;
        public ScenarioValue BlockBaseBudget = new(0m, string.Empty);
        public decimal SupplyBonus;
        public ScenarioValue AvailableBudget = new(0m, string.Empty);
        public int PendingSupplyPoints;
        public RunDesignState? ConfirmedDesign;
        public bool DesignConfirmedForBlock;
        public List<EncounterPlan> Offers = new();
        public string? SelectedOfferId;
        public EncounterPlan? LockedPlan;
        public AttemptIdentity? CurrentAttempt;
        public long GenerationCounter;
        public List<AttemptIdentity> ProcessedAttempts = new();
        public List<string> ConsumedOfferIds = new();
        public EncounterOutcome? LastOutcome;
        public string? LastOutcomeDetail;
        public RewardState? Rewards;
        public int LastEnemyKills;
        public int LastAlliesSurvived;
        public int EnemiesDestroyed;
        public int FriendliesLost;
        public int Attempts;
        public int RoundAttempts;
        public bool Ironman;
        public bool TestPool;
        public int TestPoolOffset;
        public decimal Score;

        public RunSnapshot ToSnapshot() => new(
            Rewards?.Version ?? 1,
            RunId,
            Seed,
            CatalogueVersion,
            CostUnitId,
            InitialBaseBudget,
            ReadOnlyCopy(EraSchedule),
            Phase,
            SuspendedFrom,
            Round,
            BlockIndex,
            EraIndex,
            WinsInBlock,
            Victories,
            Defeats,
            BlockBaseBudget,
            SupplyBonus,
            AvailableBudget,
            PendingSupplyPoints,
            ConfirmedDesign is null ? null : CopyDesign(ConfirmedDesign),
            DesignConfirmedForBlock,
            ReadOnlyPlans(Offers),
            SelectedOfferId,
            LockedPlan is null ? null : CopyPlan(LockedPlan),
            CurrentAttempt,
            GenerationCounter,
            ReadOnlyCopy(ProcessedAttempts),
            ReadOnlyCopy(ConsumedOfferIds),
            LastOutcome,
            LastOutcomeDetail)
        {
            Rewards = CloneRewards(Rewards), StartingEraIndex = StartingEraIndex,
            OfferEconomy = OfferEconomy, GenerationPolicy = GenerationPolicy,
            EnemiesDestroyed = EnemiesDestroyed, FriendliesLost = FriendliesLost,
            Attempts = Attempts, RoundAttempts = RoundAttempts, Ironman = Ironman, Score = Score, TestPool = TestPool, TestPoolOffset = TestPoolOffset
        };
    }
}
