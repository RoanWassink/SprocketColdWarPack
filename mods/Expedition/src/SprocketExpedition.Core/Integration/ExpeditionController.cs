using System.Diagnostics;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Persistence;
using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Integration;

// Coordinator-owned Phase 4 controller. It serializes commands, owns the one
// in-flight operation, and is the only place that combines the deterministic run
// engine, the encounter catalogue, local persistence and the native adapter.
// The presenter is output-only; the UI host calls these commands on the Unity main
// thread and marshals presenter calls back to it.
public sealed record NewRunSetup(
    ScenarioValue InitialBaseBudget,
    IReadOnlyList<EraReference> EraSchedule,
    string CatalogueVersion)
{
    public bool AdventureRules { get; init; }
    public int AdventureVersion { get; init; } = 2;
    public int StartingEraIndex { get; init; }
    public bool Ironman { get; init; }
    public bool TestPool { get; init; }
}

public sealed partial class ExpeditionController
{
    public RunSnapshot? Snapshot => _engine?.Snapshot;
    private readonly SeededEncounterCatalogue _catalogue;
    private readonly RunStore _store;
    private readonly IGameAdapter _adapter;
    private readonly IExpeditionPresenter _presenter;
    private readonly Func<ModAvailability> _mods;
    private readonly Func<string> _newRunId;
    private readonly Func<long> _newSeed;
    private readonly Func<DifficultySettings>? _difficulty;
    private readonly Action<BattleLogRow>? _battleLog;
    private readonly Action<string>? _battleLogWarning;
    private readonly Func<string, string>? _enemyName;
    private readonly Action<string>? _testPoolLog;
    private readonly int _slot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RunDirector? _engine;
    private BlueprintSnapshot? _blueprint;

    public ExpeditionController(
        SeededEncounterCatalogue catalogue,
        RunStore store,
        IGameAdapter adapter,
        IExpeditionPresenter presenter,
        Func<ModAvailability> mods,
        Func<string> newRunId,
        Func<long> newSeed,
        Func<DifficultySettings>? difficulty = null,
        Action<BattleLogRow>? battleLog = null,
        Action<string>? battleLogWarning = null,
        int slot = 1, Func<string, string>? enemyName = null,
        Action<string>? testPoolLog = null)
    {
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _mods = mods ?? throw new ArgumentNullException(nameof(mods));
        _newRunId = newRunId ?? throw new ArgumentNullException(nameof(newRunId));
        _newSeed = newSeed ?? throw new ArgumentNullException(nameof(newSeed));
        _difficulty = difficulty;
        _battleLog = battleLog;
        _battleLogWarning = battleLogWarning;
        _slot = slot; _enemyName = enemyName; _testPoolLog = testPoolLog;
    }

    public Task ShowStartAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var active = _store.LoadActiveRun(_mods());
            if (active.Status is RunStoreLoadStatus.ResumeBlocked
                or RunStoreLoadStatus.RecoveryRequired
                or RunStoreLoadStatus.SchemaUnsupported)
            {
                _presenter.ShowResumeBlocked(new BlockedView(
                    Describe(active),
                    active.Status == RunStoreLoadStatus.RecoveryRequired));
                return;
            }
            if (active.Status == RunStoreLoadStatus.Error)
            {
                _presenter.ShowError(new ErrorView(Describe(active), false));
                return;
            }

            var list = _store.ListRuns();
            var records = list.Runs
                .Select(r => new RunRecordSummary(
                    r.RunId,
                    r.Phase ?? RunPhase.Building,
                    r.Victories,
                    r.Defeats,
                    r.Era)
                    {
                        Score = r.Score,
                        TestPool = r.TestPool, Ironman = r.Ironman
                    })
                .ToArray();

            _presenter.ShowStart(new StartView(
                active.Status == RunStoreLoadStatus.Loaded,
                active.CanResume,
                records));
            await Task.CompletedTask;
        }, cancellationToken);

    public Task StartNewRunAsync(NewRunSetup setup, CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(setup);

            var active = _store.LoadActiveRun(_mods());
            if (active.Status is RunStoreLoadStatus.Loaded
                or RunStoreLoadStatus.RecoveryRequired
                or RunStoreLoadStatus.ResumeBlocked
                or RunStoreLoadStatus.SchemaUnsupported)
            {
                _presenter.ShowResumeBlocked(new BlockedView(
                    "An existing run cannot be overwritten. Resume it or recover it first.",
                    active.Status == RunStoreLoadStatus.RecoveryRequired));
                return;
            }

            if (active.Status == RunStoreLoadStatus.Error)
            {
                _presenter.ShowError(new ErrorView(Describe(active), true));
                return;
            }

            var readiness = _adapter.CheckReadiness();
            if (!readiness.Ready)
            {
                _presenter.ShowError(new ErrorView(readiness.Reason, true));
                return;
            }

            if (setup.StartingEraIndex < 0 || setup.StartingEraIndex >= setup.EraSchedule.Count)
            {
                _presenter.ShowError(new ErrorView("Starting era is outside the native schedule.", true));
                return;
            }
            var capture = await _adapter.CaptureDesignAsync(
                setup.EraSchedule[setup.StartingEraIndex],
                cancellationToken);
            if (!ValidQuote(capture, out var reason))
            {
                _presenter.ShowError(new ErrorView(reason!, true));
                return;
            }

            var configuration = new RunConfiguration(
                _newRunId(),
                _newSeed(),
                setup.InitialBaseBudget,
                setup.EraSchedule,
                setup.CatalogueVersion) { AdventureRules = setup.AdventureRules, AdventureVersion = setup.AdventureVersion,
                    StartingEraIndex = setup.StartingEraIndex, TestPool = setup.TestPool, Ironman = setup.Ironman };

            var engine = new RunDirector();
            var started = engine.NewRun(configuration);
            if (!started.IsAccepted)
            {
                _presenter.ShowError(new ErrorView(started.Reason, true));
                return;
            }

            var confirmed = engine.ConfirmDesign(capture.Quote);
            if (!confirmed.IsAccepted)
            {
                _presenter.ShowError(new ErrorView(confirmed.Reason, true));
                return;
            }

            _engine = engine;
            _blueprint = new BlueprintSnapshot(
                capture.Quote.Blueprint.SnapshotHash,
                capture.SnapshotBytes);

            if (!await SaveAsync("run creation", cancellationToken)) return;
            PresentBuilding();
            await EnsureOffersAsync(cancellationToken);
        }, cancellationToken);

    // User-authorized opt-in for an existing older-schema run. Only future,
    // uncommitted offers change; current offers/retries remain exact.
    public Task EnablePolicy5GenerationAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            var result = engine.EnablePolicy5Generation();
            if (!result.IsAccepted)
            {
                _presenter.ShowError(new ErrorView(result.Reason, false));
                return;
            }
            if (!await SaveAsync("future generation policy", cancellationToken)) return;
        }, cancellationToken);

    public Task ConfirmDesignAsync(CancellationToken cancellationToken = default)        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            var snapshot = engine.Snapshot!;
            if (snapshot.Phase != RunPhase.Building)
            {
                _presenter.ShowError(new ErrorView("Design confirmation is only available at a build boundary.", true));
                return;
            }

            var readiness = _adapter.CheckReadiness();
            if (!readiness.Ready)
            {
                _presenter.ShowError(new ErrorView(readiness.Reason, true));
                return;
            }

            var capture = await _adapter.CaptureDesignAsync(
                snapshot.EraSchedule[snapshot.EraIndex],
                cancellationToken);
            if (!ValidQuote(capture, out var reason))
            {
                _presenter.ShowError(new ErrorView(reason!, true));
                return;
            }

            var confirmed = engine.ConfirmDesign(capture.Quote);
            if (!confirmed.IsAccepted)
            {
                _presenter.ShowError(new ErrorView(confirmed.Reason, true));
                return;
            }

            _blueprint = new BlueprintSnapshot(
                capture.Quote.Blueprint.SnapshotHash,
                capture.SnapshotBytes);

            if (!await SaveAsync("design confirmation", cancellationToken)) return;
            PresentBuilding();
            await EnsureOffersAsync(cancellationToken);
        }, cancellationToken);

    public Task SelectOfferAsync(string offerId, CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            var selected = engine.SelectEncounter(offerId);
            if (!selected.IsAccepted)
            {
                _presenter.ShowError(new ErrorView(selected.Reason, true));
                return;
            }

            if (!await SaveAsync("offer selection", cancellationToken)) return;
            PresentOffers();
            await Task.CompletedTask;
        }, cancellationToken);

    public Task RefreshEncounterDesignAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            var snapshot = engine.Snapshot!;
            if (snapshot.Phase != RunPhase.ChoosingEncounter)
                throw new InvalidOperationException("Tank upgrades require the pre-encounter choice phase.");
            var readiness = _adapter.CheckReadiness();
            if (!readiness.Ready) throw new InvalidOperationException(readiness.Reason);
            var capture = await _adapter.CaptureDesignAsync(
                snapshot.EraSchedule[snapshot.EraIndex], cancellationToken);
            if (!ValidQuote(capture, out var reason))
                throw new InvalidOperationException(reason);
            var confirmed = engine.ConfirmEncounterDesign(capture.Quote);
            if (!confirmed.IsAccepted) throw new InvalidOperationException(confirmed.Reason);
            _blueprint = new BlueprintSnapshot(capture.Quote.Blueprint.SnapshotHash, capture.SnapshotBytes);
            if (!await SaveAsync("pre-encounter tank upgrade", cancellationToken)) return;
            PresentOffers();
        }, cancellationToken);

    public Task BeginEncounterAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            var begun = engine.BeginEncounter();
            if (!begun.IsAccepted)
            {
                _presenter.ShowError(new ErrorView(begun.Reason, true));
                return;
            }

            if (!await SaveAsync("encounter begin", cancellationToken)) return;
            await RunActiveEncounterAsync(cancellationToken);
        }, cancellationToken);

    public Task RetryEncounterAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            var phase = engine.Snapshot!.Phase;
            if (phase is not (RunPhase.LoadingEncounter or RunPhase.Fighting))
            {
                _presenter.ShowError(new ErrorView("There is no persisted encounter attempt to retry.", true));
                return;
            }

            await RunActiveEncounterAsync(cancellationToken);
        }, cancellationToken);

    public bool CanReturnToEditor(AttemptIdentity attempt)
    {
        var snapshot = _engine?.Snapshot;
        return snapshot?.Phase is RunPhase.LoadingEncounter or RunPhase.Fighting
            && snapshot.CurrentAttempt == attempt;
    }

    // The host first cancels and awaits the active battle operation. Only then
    // can this serialized transition persist the refit checkpoint.
    public Task ReturnToEditorAsync(AttemptIdentity attempt, CancellationToken cancellationToken = default)
        => ReturnToEditorAsync(attempt, new EditorReturnContext(false), cancellationToken);

    public Task ReturnToEditorAsync(AttemptIdentity attempt, EditorReturnContext context,
        CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var before = RequireEngine().Snapshot!;
            var returned = RequireEngine().ReturnToEditor(attempt, context);
            if (!returned.IsAccepted)
            {
                _presenter.ShowError(new ErrorView(returned.Reason, false));
                return;
            }
            if (before.SchemaVersion >= 6)
                WriteBattleLog(before, new EncounterResult(attempt, before.LockedPlan!,
                    context.Contact ? EncounterOutcome.CombatRetreat : EncounterOutcome.PrecombatAborted,
                    context.Contact ? "Return to Editor after contact" : "Return to Editor before contact")
                { EnemiesDestroyed = context.EnemiesDestroyed, FriendliesLost = context.FriendliesLost,
                    PlayerTankSurvived = context.PlayerTankSurvived }, context.DurationSeconds is { } duration ? (double)duration : 0d);
            LogTestPoolCursor(before, context.Contact ? EncounterOutcome.CombatRetreat : EncounterOutcome.PrecombatAborted);
            if (!await SaveAsync("return to editor", cancellationToken)) return;
            if (NeedsTestPoolOffers(before))
            {
                if (await PinOffersAsync(cancellationToken, report: true, select: before.LockedPlan!.Risk)) PresentOffers();
                return;
            }
            PresentOffers();
        }, cancellationToken);

    // Called by the UI after a result screen or after resuming a non-combat screen.
    // It never invents a result; it only presents the next persisted phase and
    // generates offers if the engine is waiting for them.
    public Task ContinueAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            var snapshot = engine.Snapshot!;
            if (snapshot.Rewards?.Choices.Count > 0 || snapshot.Phase == RunPhase.AwaitingRecovery)
            { PresentArmory(); return; }
            switch (snapshot.Phase)
            {
                case RunPhase.Building:
                    if (engine.CanChooseEncounterBeforeRefit) await EnsureOffersAsync(cancellationToken);
                    else PresentBuilding();
                    break;
                case RunPhase.ChoosingEncounter:
                    await EnsureOffersAsync(cancellationToken);
                    break;
                case RunPhase.LoadingEncounter:
                case RunPhase.Fighting:
                    PresentEncounter(snapshot.Phase == RunPhase.Fighting);
                    break;
                case RunPhase.AwaitingEndlessChoice:
                    PresentEndlessChoice();
                    break;
                case RunPhase.Lost:
                case RunPhase.Finished:
                case RunPhase.Retired:
                    PresentRunFinished();
                    break;
                case RunPhase.Suspended:
                    _presenter.ShowStart(new StartView(true, true, Array.Empty<RunRecordSummary>()));
                    break;
                default:
                    _presenter.ShowError(new ErrorView(
                        $"The run is in unsupported phase {snapshot.Phase}.", true));
                    break;
            }
        }, cancellationToken);

    public Task ContinueEndlessAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            if (engine.Snapshot?.Rewards?.Choices.Count > 0) { PresentArmory(); return; }
            var continued = engine.ContinueEndless();
            if (!continued.IsAccepted)
            {
                _presenter.ShowError(new ErrorView(continued.Reason, true));
                return;
            }

            if (!await SaveAsync("endless choice", cancellationToken)) return;
            PresentBuilding();
            await EnsureOffersAsync(cancellationToken);
        }, cancellationToken);

    public Task FinishAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            if (engine.Snapshot?.Rewards?.Choices.Count > 0) { PresentArmory(); return; }
            var finished = engine.Finish();
            if (!finished.IsAccepted)
            {
                _presenter.ShowError(new ErrorView(finished.Reason, true));
                return;
            }

            if (!await SaveAsync("run finish", cancellationToken)) return;
            PresentRunFinished();
            await Task.CompletedTask;
        }, cancellationToken);

    public Task RetireAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var retired = RequireEngine().Retire();
            if (!retired.IsAccepted) { _presenter.ShowError(new ErrorView(retired.Reason, false)); return; }
            if (await SaveAsync("retire run", cancellationToken)) PresentRunFinished();
        }, cancellationToken);

    public Task PrepareLossRetryAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var snapshot = RequireEngine().Snapshot!;
            if (snapshot.SchemaVersion < 6 || snapshot.Ironman || snapshot.Phase != RunPhase.ChoosingEncounter)
            { _presenter.ShowError(new ErrorView("This run cannot retry a defeat.", false)); return; }
            await _adapter.CleanupAsync(cancellationToken);
            // A fought test round has no cards until they are pinned from the
            // new cursor (normally done right after the result).
            if (snapshot.TestPool && snapshot.Offers.Count == 0) await EnsureOffersAsync(cancellationToken);
            else PresentOffers();
        }, cancellationToken);

    public Task SuspendAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            var suspended = engine.Suspend();
            if (!suspended.IsAccepted)
            {
                _presenter.ShowError(new ErrorView(suspended.Reason, true));
                return;
            }

            if (!await SaveAsync("suspend", cancellationToken)) return;
            _presenter.ShowStart(new StartView(true, true, Array.Empty<RunRecordSummary>()));
            await Task.CompletedTask;
        }, cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var load = _store.LoadActiveRun(_mods());
            if (!load.CanResume || load.Snapshot is null)
            {
                _presenter.ShowResumeBlocked(new BlockedView(
                    Describe(load),
                    load.Status == RunStoreLoadStatus.RecoveryRequired));
                return;
            }

            if (!LoadBlueprint(load.Snapshot))
            {
                return;
            }

            _engine = RunDirector.Restore(load.Snapshot);
            if (_engine.Snapshot!.Phase == RunPhase.Suspended)
            {
                var resumed = _engine.Resume();
                if (!resumed.IsAccepted)
                {
                    _presenter.ShowError(new ErrorView(resumed.Reason, true));
                    return;
                }
            }

            if (!RestoreCatalogue(load.Snapshot))
            {
                return;
            }

            if (_engine.Snapshot!.Rewards?.Choices.Count > 0 || _engine.Snapshot.Phase == RunPhase.AwaitingRecovery)
                PresentArmory();
            else if (_engine.Snapshot!.Phase == RunPhase.ChoosingEncounter || _engine.CanChooseEncounterBeforeRefit)
                await EnsureOffersAsync(cancellationToken);
            else PresentCurrent();
        }, cancellationToken);

    public Task RecoverAsync(
        string runId,
        RunCheckpointSelection selection,
        CancellationToken cancellationToken = default)
        => RunExclusiveAsync(async () =>
        {
            var recovered = _store.RecoverRun(runId, selection, _mods());
            if (!recovered.CanResume || recovered.Snapshot is null)
            {
                _presenter.ShowResumeBlocked(new BlockedView(
                    Describe(recovered),
                    recovered.Status == RunStoreLoadStatus.RecoveryRequired));
                return;
            }

            if (!LoadBlueprint(recovered.Snapshot))
            {
                return;
            }

            _engine = RunDirector.Restore(recovered.Snapshot);
            if (_engine.Snapshot!.Phase == RunPhase.Suspended)
            {
                var resumed = _engine.Resume();
                if (!resumed.IsAccepted)
                {
                    _presenter.ShowError(new ErrorView(resumed.Reason, true));
                    return;
                }
            }

            if (!RestoreCatalogue(recovered.Snapshot))
            {
                return;
            }

            if (_engine.Snapshot!.Rewards?.Choices.Count > 0 || _engine.Snapshot.Phase == RunPhase.AwaitingRecovery)
                PresentArmory();
            else if (_engine.Snapshot!.Phase == RunPhase.ChoosingEncounter || _engine.CanChooseEncounterBeforeRefit)
                await EnsureOffersAsync(cancellationToken);
            else PresentCurrent();
        }, cancellationToken);

    private async Task RunActiveEncounterAsync(CancellationToken cancellationToken)
    {
        var engine = RequireEngine();
        var preBattle = engine.Snapshot!;
        var request = engine.CreateEncounterRequest();
        PresentEncounter(combatStarted: false);
        var attemptStarted = Stopwatch.GetTimestamp();

        var started = false;
        async Task CombatStarted()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (started) return;
            started = true;

            var reported = engine.ReportCombatStarted(request.Attempt);
            if (!reported.IsAccepted)
            {
                throw new InvalidOperationException(reported.Reason);
            }

            if (!await SaveAsync("combat start", cancellationToken))
            {
                throw new InvalidOperationException("Could not persist the combat boundary.");
            }

            PresentEncounter(combatStarted: true);
        }

        var result = await _adapter.RunEncounterAsync(request, CombatStarted, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var elapsed = (Stopwatch.GetTimestamp() - attemptStarted) / (double)Stopwatch.Frequency;
        var accepted = engine.AcceptResult(result);
        if (!accepted.IsAccepted)
        {
            _presenter.ShowError(new ErrorView(accepted.Reason, true));
            return;
        }

        // A completed attempt is logged even if the checkpoint save below fails.
        WriteBattleLog(preBattle, result, elapsed);
        LogTestPoolCursor(preBattle, result.Outcome);
        if (preBattle.SchemaVersion >= 6 ? result.Outcome == EncounterOutcome.Victory
            : engine.Snapshot!.Phase != RunPhase.AwaitingRecovery)
            _catalogue.MarkResolved(result.Attempt.EncounterId);
        if (!await SaveAsync("encounter result", cancellationToken)) return;
        // A failure here leaves the round without cards; the next choice
        // (RETRY / CHOOSE ANOTHER BATTLE / resume) generates and reports them.
        if (NeedsTestPoolOffers(preBattle))
            await PinOffersAsync(cancellationToken, report: false, select: result.Plan.Risk);
        PresentResult(result, preBattle);
    }

    // 0.3.4: a fought test round moved the cursor and cleared its cards.
    private bool NeedsTestPoolOffers(RunSnapshot before)
    {
        var snapshot = RequireEngine().Snapshot!;
        return before.TestPool && snapshot.Phase == RunPhase.ChoosingEncounter && snapshot.Offers.Count == 0
            && snapshot.TestPoolOffset != before.TestPoolOffset;
    }

    private void LogTestPoolCursor(RunSnapshot before, EncounterOutcome outcome)
    {
        if (!before.TestPool || outcome is not (EncounterOutcome.Victory or EncounterOutcome.FriendlyForceDestroyed
            or EncounterOutcome.CombatRetreat)) return;
        try
        {
            _testPoolLog?.Invoke($"Expedition test pool: offset {before.TestPoolOffset} -> "
                + $"{RequireEngine().Snapshot!.TestPoolOffset} after "
                + (outcome == EncounterOutcome.CombatRetreat ? "Retreat" : outcome.ToString()));
        }
        catch (Exception ex) { BattleLog.Warn(_battleLogWarning, "Expedition test pool cursor log failed: " + ex.Message); }
    }

    private async Task EnsureOffersAsync(CancellationToken cancellationToken)
    {
        var engine = RequireEngine();
        var snapshot = engine.Snapshot!;
        if (snapshot.Phase == RunPhase.ChoosingEncounter && snapshot.Offers.Count > 0)
        {
            PresentOffers();
            return;
        }

        if (snapshot.Phase is not (RunPhase.Building or RunPhase.ChoosingEncounter)
            || ((!snapshot.DesignConfirmedForBlock || snapshot.ConfirmedDesign is null) && !engine.CanChooseEncounterBeforeRefit))
        {
            PresentCurrent();
            return;
        }

        if (await PinOffersAsync(cancellationToken, report: true, select: null)) PresentOffers();
    }

    // Generates, validates and saves the current round's cards. `select` keeps
    // a lost test card's risk chosen after the round was offered again.
    private async Task<bool> PinOffersAsync(CancellationToken cancellationToken, bool report, EncounterRisk? select)
    {
        var engine = RequireEngine();
        var snapshot = engine.Snapshot!;
        var request = engine.CreateOfferRequest();
        decimal? baseDifficulty = null;
        if ((request.RulesVersion >= 5 || request.GenerationPolicy >= 5)
            && snapshot.OfferEconomy == null)
        {
            var settings = _difficulty?.Invoke() ?? DifficultySettings.Default;
            if (!settings.IsValid)
                settings = DifficultySettings.Default;
            baseDifficulty = settings.EnemyBudgetMultiplier;
            var contract = snapshot.Rewards?.Active.Any(item =>
                item.Kind == RewardKind.ExperimentalContract) == true ? 1.2m : 1m;
            request = request with
            {
                EnemyBudgetMultiplier = settings.EnemyBudgetMultiplier * contract,
                EraProgressionEnabled = settings.EraProgressionEnabled
            };
        }
        var generated = _catalogue.TryGenerateOffers(request);
        if (!generated.IsSuccess)
        {
            if (report)
                _presenter.ShowResumeBlocked(new BlockedView(
                    $"{generated.Status}: {string.Join("; ", generated.Diagnostics)}",
                    false));
            return false;
        }

        var pinnedEconomy = request.RulesVersion >= 5 || request.GenerationPolicy >= 5
            ? new OfferEconomy(request.AvailableBudget, request.EnemyBudgetMultiplier)
            {
                EraProgression = request.EraProgressionEnabled,
                // Only schema 6 records the base difficulty; older pinned
                // economies keep their exact serialized bytes.
                BaseDifficulty = request.RulesVersion >= 6 ? baseDifficulty : null
            }
            : null;
        var set = engine.SetOffers(generated.Offers, pinnedEconomy);
        if (!set.IsAccepted)
        {
            if (report) _presenter.ShowError(new ErrorView(set.Reason, true));
            return false;
        }
        if (select is { } risk && engine.Snapshot!.SelectedOfferId is null
            && engine.Snapshot.Offers.FirstOrDefault(o => o.Risk == risk) is { } same)
            engine.SelectEncounter(same.OfferId);

        return await SaveAsync("offer generation", cancellationToken);
    }

    private bool LoadBlueprint(RunSnapshot snapshot)
    {
        if (snapshot.ConfirmedDesign is null)
        {
            _blueprint = null;
            return true;
        }

        var hash = snapshot.ConfirmedDesign.Blueprint.SnapshotHash;
        var read = _store.ReadBlueprint(snapshot.RunId, hash);
        if (read.Bytes is null)
        {
            _presenter.ShowResumeBlocked(new BlockedView(
                "The locked design snapshot is missing or unreadable: "
                + string.Join("; ", read.Issues.Select(i => i.Code + ": " + i.Message)),
                true));
            return false;
        }

        _blueprint = new BlueprintSnapshot(hash, read.Bytes);
        return true;
    }

    private bool RestoreCatalogue(RunSnapshot snapshot)
    {
        if (snapshot.Offers.Count == 0)
        {
            return true;
        }

        var restoreBudget = snapshot.OfferEconomy?.Budget ?? (snapshot.Rewards?.Version >= 6
            ? new ScenarioValue(RunDirector.OfferBaseBudget(snapshot), snapshot.CostUnitId)
            : snapshot.AvailableBudget);
        var request = new OfferRequest(
            snapshot.RunId,
            snapshot.Seed,
            snapshot.Round,
            snapshot.BlockIndex,
            snapshot.EraSchedule[snapshot.EraIndex],
            restoreBudget,
            snapshot.PendingSupplyPoints,
            snapshot.CatalogueVersion)
        {
            TestPool = snapshot.TestPool, TestPoolOffset = snapshot.TestPoolOffset,
                        RulesVersion = snapshot.Rewards?.Version ?? 1,
            OfferRoll = snapshot.Rewards?.OfferRoll ?? 0,
            EnemyBudgetMultiplier = snapshot.OfferEconomy?.EnemyBudgetMultiplier
                ?? (snapshot.Rewards?.Active.Any(item => item.Kind == RewardKind.ExperimentalContract) == true ? 1.2m : 1m),
            EraProgressionEnabled = snapshot.OfferEconomy?.EraProgression ?? false,
            GenerationPolicy = snapshot.GenerationPolicy
                ?? (snapshot.Rewards?.Version >= 5 ? 5 : 0)
        };

        var restored = _catalogue.RestoreOffers(request, snapshot.Offers);
        if (!restored.IsSuccess)
        {
            _presenter.ShowResumeBlocked(new BlockedView(
                $"{restored.Status}: {string.Join("; ", restored.Diagnostics)}",
                false));
            return false;
        }

        foreach (var offerId in snapshot.ConsumedOfferIds)
        {
            _catalogue.MarkResolved(offerId);
        }

        return true;
    }

    private async Task<bool> SaveAsync(string operation, CancellationToken cancellationToken)
    {
        var engine = RequireEngine();
        var snapshot = engine.Snapshot;
        if (snapshot is null)
        {
            _presenter.ShowError(new ErrorView("The run has no snapshot to save.", true));
            return false;
        }

        var blueprints = _blueprint is null
            ? Array.Empty<BlueprintSnapshot>()
            : new[] { _blueprint };

        var saved = _store.SaveCheckpoint(snapshot, blueprints);
        if (!saved.IsCommitted)
        {
            _presenter.ShowError(new ErrorView(
                $"Could not save {operation}: "
                + string.Join("; ", saved.Issues.Select(i => i.Code + ": " + i.Message)),
                true));
            return false;
        }

        await Task.CompletedTask;
        return true;
    }

    private void PresentCurrent()
    {
        var snapshot = RequireEngine().Snapshot!;
        if (snapshot.Rewards?.Choices.Count > 0 || snapshot.Phase == RunPhase.AwaitingRecovery)
        { PresentArmory(); return; }
        switch (snapshot.Phase)
        {
            case RunPhase.Building:
                PresentBuilding();
                break;
            case RunPhase.ChoosingEncounter:
                PresentOffers();
                break;
            case RunPhase.LoadingEncounter:
            case RunPhase.Fighting:
                PresentEncounter(snapshot.Phase == RunPhase.Fighting);
                break;
            case RunPhase.AwaitingEndlessChoice:
                PresentEndlessChoice();
                break;
            case RunPhase.Lost:
            case RunPhase.Finished:
            case RunPhase.Retired:
                PresentRunFinished();
                break;
            case RunPhase.Suspended:
                _presenter.ShowStart(new StartView(true, true, Array.Empty<RunRecordSummary>()));
                break;
            default:
                _presenter.ShowError(new ErrorView(
                    $"The run is in unsupported phase {snapshot.Phase}.", true));
                break;
        }
    }

    private void PresentBuilding()
    {
        var snapshot = RequireEngine().Snapshot!;
        var design = snapshot.ConfirmedDesign;
        var quote = design is null
            ? null
            : new DesignQuote(design.Blueprint, design.Cost, true, true, null);
        _presenter.ShowBuilding(new BuildingView(
            snapshot.Phase,
            snapshot.Round,
            snapshot.BlockIndex,
            snapshot.EraSchedule[snapshot.EraIndex],
            snapshot.AvailableBudget,
            quote,
            design?.FriendlyCountIncludingPlayer ?? 0,
            null) { CanRetire = snapshot.SchemaVersion >= 6 });
    }

    private void PresentOffers()
    {
        var snapshot = RequireEngine().Snapshot!;
        _presenter.ShowOffers(new OffersView(
            snapshot.Round,
            snapshot.EraSchedule[snapshot.EraIndex],
            snapshot.AvailableBudget,
            snapshot.Offers,
            snapshot.SelectedOfferId)
        {
            CanRetire = snapshot.SchemaVersion >= 6,
            EraBoss = !snapshot.TestPool && snapshot.SchemaVersion >= 6 && snapshot.WinsInBlock == 0 && snapshot.BlockIndex < snapshot.EraSchedule.Count
                ? BossPreview(snapshot) : null,
            EnemyCosts = snapshot.SchemaVersion >= 6 ? snapshot.Offers.ToDictionary(o => o.OfferId, o => EnemyCostFor(o) ?? 0m) : null
        });
    }

    private void PresentEncounter(bool combatStarted)
    {
        var snapshot = RequireEngine().Snapshot!;
        if (snapshot.CurrentAttempt is null || snapshot.LockedPlan is null)
        {
            _presenter.ShowError(new ErrorView("The active encounter is incomplete.", true));
            return;
        }

        _presenter.ShowEncounter(new EncounterView(
            snapshot.CurrentAttempt,
            snapshot.LockedPlan,
            snapshot.ConfirmedDesign?.FriendlyCountIncludingPlayer ?? 0,
            snapshot.LockedPlan.Enemies.Sum(e => e.Count),
            combatStarted));
    }

    private string BossPreview(RunSnapshot snapshot)
    {
        var boss = BossSelection.Select(_catalogue.Definition, snapshot.EraIndex, _catalogue.Options.RequiredQualification,
            snapshot.Seed, snapshot.Round + 3 - snapshot.WinsInBlock, snapshot.Rewards?.Version ?? snapshot.SchemaVersion);
        return $"Era boss: {boss.Blueprint.LocalId} ({boss.CheckedPrice.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)})";
    }

    private void PresentResult(EncounterResult result, RunSnapshot preBattle)
    {
        var snapshot = RequireEngine().Snapshot!;
        if (snapshot.SchemaVersion < 6 && snapshot.Rewards?.Choices.Count > 0 || snapshot.Phase == RunPhase.AwaitingRecovery)
        { PresentArmory(); return; }
        var terminal = snapshot.Phase is RunPhase.Lost or RunPhase.Finished or RunPhase.Retired;
        var loss = result.Outcome is EncounterOutcome.FriendlyForceDestroyed
            or EncounterOutcome.CombatRetreat;

        _presenter.ShowResult(new ResultView(
            result,
            snapshot.Phase,
            terminal ? null : snapshot.AvailableBudget,
            snapshot.PendingSupplyPoints,
            terminal,
            loss)
        {
            CanRetryLoss = snapshot.SchemaVersion >= 6 && !snapshot.Ironman && loss && !terminal,
            RoundAttempts = snapshot.RoundAttempts,
            ScoreGained = snapshot.SchemaVersion >= 6 && result.Outcome == EncounterOutcome.Victory ? snapshot.Score - preBattle.Score : null,
            ScoreBreakdown = snapshot.SchemaVersion >= 6 && result.Outcome == EncounterOutcome.Victory
                ? ScoreRules.Battle(preBattle.EraIndex, result.Plan.Risk, result.Plan.Extras?.BossBlueprintId != null,
                    result.FriendliesLost, preBattle.RoundAttempts, preBattle.OfferEconomy?.BaseDifficulty ?? 1.5m,
                    preBattle.Ironman).Breakdown : null
        });

        if (snapshot.SchemaVersion < 6 && snapshot.Phase == RunPhase.AwaitingEndlessChoice)
        {
            PresentEndlessChoice();
        }
    }

    private void PresentEndlessChoice()
    {
        var snapshot = RequireEngine().Snapshot!;
        _presenter.ShowEndlessChoice(new EndlessChoiceView(
            snapshot.Victories,
            snapshot.AvailableBudget,
            snapshot.PendingSupplyPoints));
    }

    private void PresentRunFinished()
    {
        var snapshot = RequireEngine().Snapshot!;
        _presenter.ShowRunFinished(new RunFinishedView(
            snapshot.Victories,
            snapshot.Defeats,
            snapshot.Phase,
            snapshot.Phase == RunPhase.Lost));
    }

    private RunDirector RequireEngine()
        => _engine ?? throw new InvalidOperationException("No run is loaded.");

    private static bool ValidQuote(DesignCapture capture, out string? reason)
    {
        if (capture is null || capture.Quote is null || capture.SnapshotBytes is null
            || capture.SnapshotBytes.Length == 0)
        {
            reason = "The native design capture is incomplete.";
            return false;
        }

        var quote = capture.Quote;
        if (!quote.NativeDesignValid || !quote.NativeEraValid
            || !string.IsNullOrEmpty(quote.RejectionReason))
        {
            reason = quote.RejectionReason ?? "The native design is not valid for this scenario.";
            return false;
        }

        if (quote.Cost is null || quote.Cost.Value <= 0m)
        {
            reason = "The native design quote is not positive.";
            return false;
        }

        reason = null;
        return true;
    }

    private static string Describe(RunLoadResult load)
    {
        if (load.Issues.Count == 0)
        {
            return load.Status.ToString();
        }

        return $"{load.Status}: " + string.Join("; ", load.Issues.Select(i => i.Code + ": " + i.Message));
    }

    // Builds and hands off one finished-attempt row. Everything here is
    // best-effort: a missing native market price, a config read failure or a
    // rejected file write only leaves cells empty / logs a warning.
    private void WriteBattleLog(RunSnapshot pre, EncounterResult result, double elapsed)
    {
        try
        {
            if (_battleLog is null) return;
            var plan = result.Plan;
            var rewards = pre.Rewards;
            var friendlyCount = pre.ConfirmedDesign?.FriendlyCountIncludingPlayer ?? 0;
            var baseBudget = RunDirector.OfferBaseBudget(pre);
            var settings = _difficulty?.Invoke() ?? DifficultySettings.Default;
            decimal? fleetCost = null;
            if (pre.ConfirmedDesign is { } design && rewards != null)
            {
                var fleet = RewardRules.Fleet(rewards, baseBudget, design.Cost.Value);
                fleetCost = fleet.TotalCost;
            }
            var destroyed = result.EnemiesDestroyed ?? result.ConfirmedEnemyKills
                ?? (result.Outcome == EncounterOutcome.Victory ? plan.Enemies.Sum(e => e.Count) : null);
            var lost = result.FriendliesLost
                ?? (result.Outcome == EncounterOutcome.FriendlyForceDestroyed ? friendlyCount
                    : result.Outcome == EncounterOutcome.Victory && result.SurvivingAllies is { } survived
                        ? Math.Max(0, friendlyCount - 1 - survived) : null);
            _battleLog(new BattleLogRow(
                DateTime.UtcNow,
                pre.RunId,
                _slot,
                rewards?.Version ?? pre.SchemaVersion,
                pre.OfferEconomy?.BaseDifficulty ?? settings.EnemyBudgetMultiplier,
                pre.OfferEconomy?.EraProgression ?? settings.EraProgressionEnabled,
                plan.Era.NativeId,
                pre.EraIndex,
                plan.Round,
                (plan.Round - 1) % 4 + 1,
                plan.Extras?.BossBlueprintId != null,
                pre.BlockIndex >= pre.EraSchedule.Count,
                plan.Risk.ToString(),
                plan.MapId,
                plan.Extras?.Special?.Pattern.ToString(),
                plan.Enemies.Sum(e => e.Count),
                EnemyCostFor(plan),
                plan.Extras?.EnemyBudgetTarget,
                friendlyCount,
                pre.ConfirmedDesign?.Blueprint.LocalId,
                pre.ConfirmedDesign?.Cost.Value,
                fleetCost,
                pre.AvailableBudget.Value,
                baseBudget,
                result.Outcome == EncounterOutcome.CombatRetreat && pre.SchemaVersion >= 6 ? "Retreat" : result.Outcome.ToString(),
                (decimal)Math.Round(elapsed, 3, MidpointRounding.AwayFromZero),
                destroyed,
                lost,
                result.PlayerTankSurvived,
                result.Attempt.Generation,
                rewards is null ? string.Empty : string.Join("|", rewards.Active.Select(i => i.Kind.ToString())))
                { TestPool = pre.TestPool, EnemyDesigns = string.Join("|", plan.Enemies.Select(e => _enemyName?.Invoke(e.BlueprintId)
                    ?? _catalogue.Definition.TestPool?.FirstOrDefault(t => t.Design.Blueprint.LocalId == e.BlueprintId)?.Name ?? e.BlueprintId)),
                    ScoreGained = pre.SchemaVersion >= 6 ? Math.Max(0m, RequireEngine().Snapshot!.Score - pre.Score) : null });
        }
        catch (Exception ex)
        {
            BattleLog.Warn(_battleLogWarning, "Expedition battle log row could not be built: " + ex.Message);
        }
    }

    private decimal? EnemyCostFor(EncounterPlan plan)
    {
        try
        {
            var total = 0m;
            foreach (var enemy in plan.Enemies)
            {
                var eraId = string.Equals(enemy.BlueprintId, plan.Extras?.BossBlueprintId,
                    StringComparison.Ordinal)
                    ? plan.Extras!.BossEra : plan.Era.NativeId;
                var era = _catalogue.Definition.Eras.FirstOrDefault(e =>
                    string.Equals(e.Era.NativeId, eraId, StringComparison.Ordinal));
                var entry = era?.Enemies.FirstOrDefault(e =>
                    string.Equals(e.Blueprint.LocalId, enemy.BlueprintId, StringComparison.Ordinal));
                entry ??= _catalogue.Definition.TestPool?.FirstOrDefault(t => t.Design.Blueprint.LocalId == enemy.BlueprintId)?.Design;
                if (entry?.CheckedPrice is null) return null;
                total = checked(total + entry.CheckedPrice.Value * enemy.Count);
            }
            return total;
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
        { return null; }
    }

    private async Task RunExclusiveAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            _presenter.ShowError(new ErrorView("Another expedition operation is still running.", false));
            return;
        }

        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            _presenter.ShowError(new ErrorView("The expedition operation was cancelled.", true));
        }
        catch (Exception ex)
        {
            _presenter.ShowError(new ErrorView(ex.Message, true));
        }
        finally
        {
            _gate.Release();
        }
    }
}
