using BepInEx;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Integration;
using SprocketExpedition.Core.Persistence;
using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Game;

// Native campaign controller and configurable local checkpoint slots.
internal static class CampaignControllerHost
{
    private static readonly CampaignPresenter presenter = new();
    private static readonly Queue<Func<Task>> queue = new();
    private static ExpeditionController? controller;
    private static RunStore? store;
    private static CampaignGameAdapter? gameAdapter;
    private static CampaignNativeCatalogue? nativeContent;
    private static NewRunSetup? setup;
    private static bool initialized;
    private static bool busy;
    private static string? fatal;
    private static long commandGeneration;
    private static CancellationTokenSource lifetime = new();
    private static bool interruptedRetry;
    private static CancellationTokenSource? encounterCancellation;
    private static (AttemptIdentity Attempt, long Session, EditorReturnContext Context)? pendingEditorReturn;
    private static bool itemsFromDesigner;

    internal static object? View => presenter.CurrentView;
    internal static SprocketExpedition.Core.Run.RewardState? RewardSnapshot => controller?.Snapshot?.Rewards;
    internal static SprocketExpedition.Core.Run.RunSnapshot? RunSnapshot => controller?.Snapshot;
    internal static bool ItemsFromDesigner => itemsFromDesigner;
    internal static bool CanOpenDesignerItems => CampaignNativeFlow.Editing && !Busy && RewardSnapshot != null;
    internal static void ShowItems()
    {
        if (EnsureInitialized()) Enqueue(async () => { itemsFromDesigner = false; await controller!.ShowArmoryAsync(); });
    }
    internal static void ShowItemsFromDesigner()
    {
        if (!CanOpenDesignerItems) return;
        Enqueue(async () =>
        {
            itemsFromDesigner = true;
            await controller!.ShowArmoryAsync(lifetime.Token);
            CampaignNativeMenu.Open();
        });
    }
    internal static void CloseItemsToDesigner()
    {
        if (!CampaignNativeFlow.Editing || Busy) return;
        itemsFromDesigner = true;
        LeaveItems();
    }
    internal static void ItemAction(string action, string? id = null)
    { if (EnsureInitialized()) Enqueue(() => controller!.RewardCommandAsync(action, id)); }
    internal static void LeaveItems()
    {
        if (!EnsureInitialized()) return;
        Enqueue(async () =>
        {
            await controller!.LeaveArmoryAsync();
            if (itemsFromDesigner && CampaignNativeFlow.Editing && View is BuildingView or OffersView)
            {
                // The current editor and its unsaved tank remain in place.
                itemsFromDesigner = false;
                CampaignNativeMenu.Close();
                return;
            }
            if (View is BuildingView && CampaignNativeFlow.OwnsSession) CampaignNativeFlow.ReturnToDesigner();
        });
    }
    internal static void UseRecovery(bool acceptDefeat = false)
    {
        if (!EnsureInitialized()) return;
        Enqueue(async () =>
        {
            await controller!.UseRecoveryAsync(acceptDefeat);
            if (!acceptDefeat && View is OffersView) CampaignNativeFlow.ReturnToDesigner();
        });
    }
    internal static bool Busy => busy || queue.Count > 0 || CampaignNativeFlow.IsTransferring;
    internal static bool CanRetry => !Busy && (interruptedRetry
        || View is ResultView { CanRetryLoss: true, IsTerminal: false }
        || View is ResultView
        {
            Result.Outcome: EncounterOutcome.TechnicalFailure or EncounterOutcome.PrecombatAborted
        });
    internal static bool CanEnablePolicy5ForCurrentRun =>
        !Busy && RunSnapshot is
        {
            Rewards: { Version: 4 },
            GenerationPolicy: null,
            Offers: { Count: 0 },
            SelectedOfferId: null,
            LockedPlan: null,
            CurrentAttempt: null
        };
    internal static void EnablePolicy5ForCurrentRun()
    {
        if (!EnsureInitialized() || !CanEnablePolicy5ForCurrentRun) return;
        Enqueue(() => controller!.EnablePolicy5GenerationAsync());
    }
    internal static bool CanShowReturnToEditor => CampaignNativeFlow.OwnsSession
        && !CampaignNativeFlow.IsTransferring && pendingEditorReturn == null
        && encounterCancellation is { IsCancellationRequested: false }
        && View is EncounterView { CombatStarted: true } active
        && controller?.CanReturnToEditor(active.Attempt) == true;
    internal static bool CanReturnToEditor => CanShowReturnToEditor
        && !(RunSnapshot is { SchemaVersion: >= 6, Ironman: true } && CustomBattleHostSpike.ContactMade);
    internal static string ReturnToEditorLabel => RunSnapshot?.SchemaVersion >= 6 && CustomBattleHostSpike.ContactMade
        ? RunSnapshot.Ironman ? "Return to Editor (Ironman: contact made)" : "Return to Editor (counts as retry)"
        : "Return to Editor";
    internal static string? BossPreviewText
    {
        get
        {
            if (nativeContent == null || RunSnapshot is not { SchemaVersion: >= 6, TestPool: false } run
                || run.BlockIndex >= run.EraSchedule.Count) return null;
            var boss = BossSelection.Select(nativeContent.Definition, run.EraIndex, ContentQualification.Candidate,
                run.Seed, run.Round + 3 - run.WinsInBlock, run.Rewards?.Version ?? run.SchemaVersion);
            return $"Era boss: {EnemyName(boss.Blueprint.LocalId)} ({boss.CheckedPrice.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)})";
        }
    }
    // One always-on line per minimum-risk design actually used by the Dangerous
    // card. Names come from the same pinned definition the plan was made from.
    internal static void LogDangerousOnlyOffers(OffersView? view)
    {
        if (nativeContent == null || view?.Offers == null) return;
        foreach (var boss in view.Offers.Select(plan => plan.Extras?.BossBlueprintId).OfType<string>().Distinct(StringComparer.Ordinal))
            Plugin.Logger.LogInfo($"Expedition boss pick: round {view.Round} {RunSnapshot?.EraSchedule[RunSnapshot.EraIndex].NativeId} -> '{EnemyName(boss)}'");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in view.Offers.Where(plan => plan.Risk == EncounterRisk.Dangerous))
            foreach (var unit in plan.Enemies)
                if (nativeContent.Definition.Eras.SelectMany(era => era.Enemies)
                    .Any(design => design.Blueprint.LocalId == unit.BlueprintId
                        && DangerousDesignPolicy.IsDangerousOnly(design)))
                    used.Add(unit.BlueprintId);
        foreach (var id in used.OrderBy(id => id, StringComparer.Ordinal))
            Plugin.Logger.LogInfo($"Expedition dangerous-only: round {view.Round} uses '{EnemyName(id)}'");
    }
    internal static string? Fatal => fatal;
    internal static int CurrentSlot { get; private set; } = 1;
    internal static bool UsesNativeContent => nativeContent != null;
    internal static decimal? DesignerBudget => nativeContent == null ? null : View switch
    {
        BuildingView view => view.AvailableBudget.Value,
        OffersView view => view.AvailableBudget.Value,
        _ => null
    };
    internal static string? DesignerEra => nativeContent == null ? null : View switch
    {
        BuildingView view => view.Era.NativeId,
        OffersView view => view.Era.NativeId,
        EncounterView view => view.Plan.Era.NativeId,
        _ => null
    };
    internal static string DescribeEnemy(string id, string era) => nativeContent!.DescribeAny(id);
    internal static decimal EnemyCost(string id, string era) => nativeContent!.FindAny(id).Price;
    // Readable design name for menu text; falls back to the id when the design is not pinned.
    internal static string EnemyName(string id)
    {
        try { return nativeContent?.FindAny(id).Name ?? id; }
        catch (InvalidOperationException) { return id; }
    }

    internal static bool CurrentSlotTerminal
    {
        get
        {
            var candidate = new RunStore(CampaignSlotManager.Root(CurrentSlot));
            var records = candidate.ListRuns();
            return records.Runs.Count > 0 && CampaignResumePreparation.CanReplaceWithNewRun(
                candidate.LoadActiveRun(ModAvailability.None), records);
        }
    }

    internal static void Tick()
    {
        // A native return/scene change can happen without our Return button. Drop
        // the in-memory view/controller so a later Custom Battle shows the slot
        // panel and resumes from the authoritative RunStore, not a stale view.
        if (initialized && !CampaignNativeFlow.OwnsSession && !CampaignControllerHub.InCampaignScene())
        {
            ResetInMemory(
                "campaign scene ended; in-memory view reset; slot stores remain authoritative.");
        }

        if (!busy && pendingEditorReturn is { } edit)
        {
            pendingEditorReturn = null;
            if (CampaignNativeFlow.OwnsSession && CampaignNativeFlow.SessionGeneration == edit.Session)
                Enqueue(async () =>
                {
                    await controller!.ReturnToEditorAsync(edit.Attempt, edit.Context, lifetime.Token);
                    // 0.3.4: a test round that was fought is offered again from
                    // the next tanks, with the same risk selected under a new ID.
                    if (View is OffersView { SelectedOfferId: not null } offers
                        && (offers.SelectedOfferId == edit.Attempt.EncounterId || controller!.Snapshot?.TestPool == true))
                    {
                        interruptedRetry = false;
                        CustomBattleHostSpike.PrepareEditorReturn();
                        CampaignNativeFlow.ReturnToDesigner();
                    }
                });
        }
        if (busy || queue.Count == 0) return;
        var action = queue.Dequeue();
        busy = true;
        RunCommand(action);
    }

    internal static SlotSummary? Slot(int slot) => CampaignSlotManager.Read(slot);

    internal static void SelectSlot(int slot)
    {
        if (Busy || slot < 1 || slot > CampaignSlotManager.SlotCount) return;
        if (CampaignSlotManager.IsEmpty(slot) && Plugin.AllowFixtureCatalogue?.Value != true)
        {
            Plugin.Logger.LogInfo($"Expedition slots: empty slot {slot} -> new game (native)");
            CampaignNativeMenu.ShowNewGameOptions(slot);
            return;
        }
        CampaignNativeFlow.CommitNewGame();
        if (slot == CurrentSlot && initialized && fatal == null)
        {
            ShowStart();
            return;
        }

        CurrentSlot = slot;
        interruptedRetry = false;
        controller = null;
        store = null;
        initialized = false;
        fatal = null;
        presenter.Reset();
        if (!EnsureInitialized()) return;
        Enqueue(() => controller!.ShowStartAsync());
    }

    internal static void ShowStart()
    {
        if (!EnsureInitialized()) return;
        Enqueue(() => controller!.ShowStartAsync());
    }

    internal static void DeleteSlot(int slot)
    {
        if (Busy || slot < 1 || slot > CampaignSlotManager.SlotCount) return;
        if (!CampaignSlotManager.ArchiveSlot(slot))
        {
            presenter.ShowError(new ErrorView(
                $"Slot {slot} could not be archived/deleted.", false));
            return;
        }

        if (slot == CurrentSlot)
        {
            ResetInMemory($"slot {slot} deleted/archived; in-memory state cleared.");
        }
        else
        {
            presenter.Reset();
            Plugin.Logger.LogInfo(
                $"Expedition slot {slot}: deleted/archived from the slot menu; current slot {CurrentSlot} view reset.");
        }
    }

    internal static void StartNew()
    {
        if (!CampaignNativeFlow.NewGameSession && Plugin.AllowFixtureCatalogue?.Value != true)
        { CampaignNativeMenu.ShowNewGameOptions(); return; }
        if (!EnsureInitialized()) return;
        if (CurrentSlotTerminal)
        {
            if (!CampaignSlotManager.ArchiveSlot(CurrentSlot))
            {
                presenter.ShowError(new ErrorView(
                    "The terminal run could not be archived; a new run was not started.", false));
                return;
            }

            controller = null;
            store = null;
            initialized = false;
            fatal = null;
            presenter.Reset();
            if (!EnsureInitialized()) return;
        }

        Enqueue(() =>
        {
            nativeContent?.PinTo(store!.RootDirectory);
            return controller!.StartNewRunAsync(setup!);
        });
    }

    // Active, suspended and unverified slots are never reclaimed implicitly.
    internal static void StartNewGame()
    {
        if (!EnsureInitialized()) return;

        var chosen = ChooseNewGameSlot();
        if (chosen == 0)
        {
            ShowSlotsFull();
            return;
        }

        if (chosen != CurrentSlot)
        {
            UseSlot(chosen);
            if (!EnsureInitialized()) return;
        }
        StartNew();
    }

    private static int ChooseNewGameSlot()
    {
        if (CampaignNativeFlow.NewGameSession && CampaignNativeFlow.NewGameSlot > 0)
            return CampaignSlotManager.IsEmpty(CampaignNativeFlow.NewGameSlot)
                ? CampaignNativeFlow.NewGameSlot : 0;
        for (var slot = 1; slot <= CampaignSlotManager.SlotCount; slot++)
        {
            var root = CampaignSlotManager.Root(slot);
            if (CampaignSlotManager.IsEmpty(slot)) return slot;
        }
        for (var slot = 1; slot <= CampaignSlotManager.SlotCount; slot++)
        {
            var candidate = new RunStore(CampaignSlotManager.Root(slot));
            if (CampaignResumePreparation.CanReplaceWithNewRun(
                    candidate.LoadActiveRun(ModAvailability.None), candidate.ListRuns())
                && CampaignSlotManager.ArchiveSlot(slot)) return slot;
        }
        return 0;
    }

    internal static bool HasNewGameSpace()
    {
        for (var slot = 1; slot <= CampaignSlotManager.SlotCount; slot++)
        {
            if (CampaignSlotManager.IsEmpty(slot)) return true;
            var candidate = new RunStore(CampaignSlotManager.Root(slot));
            if (CampaignResumePreparation.CanReplaceWithNewRun(
                    candidate.LoadActiveRun(ModAvailability.None), candidate.ListRuns())) return true;
        }
        return false;
    }

    internal static bool DeleteAndStartNew(int slot)
    {
        if (Busy || slot < 1 || slot > CampaignSlotManager.SlotCount) return false;
        return CampaignSlotPolicy.ArchiveAndStart(slot, CampaignSlotManager.ArchiveSlot, selected =>
        {
            ResetInMemory($"slot {selected} archived for New Game.");
            Plugin.Logger.LogInfo($"Expedition slots: full -> delete slot {selected} -> new game");
            CampaignNativeMenu.ShowNewGameOptions(selected);
        });
    }

    private static void ShowSlotsFull() => CampaignNativeMenu.ShowSlotsFull();

    private static void UseSlot(int slot)
    {
        commandGeneration++;
        queue.Clear();
        busy = false;
        presenter.Reset();
        CurrentSlot = slot;
        controller = null;
        store = null;
        initialized = false;
        fatal = null;
    }

    internal static void Resume()
    {
        if (!EnsureInitialized()) return;
        var cancellationToken = lifetime.Token;
        Enqueue(async () =>
        {
            var prepared = CampaignResumePreparation.Read(store!, ModAvailability.None);
            if (prepared.Error != null)
            {
                presenter.ShowResumeBlocked(new BlockedView(prepared.Error,
                    prepared.Load.Status == RunStoreLoadStatus.RecoveryRequired));
                return;
            }
            await controller!.ResumeAsync(cancellationToken);
            if (presenter.CurrentView is BlockedView or ErrorView) return;
            if (!CampaignNativeFlow.OwnsSession)
                throw new InvalidOperationException("Use Expedition > Load Game from the main menu.");
            gameAdapter!.RestoreLockedDesign(
                prepared.Load.Snapshot!.ConfirmedDesign!.Blueprint, prepared.Blueprint!.Bytes);
            await CampaignNativeFlow.OpenSavedDesignerAsync(prepared.Blueprint.Bytes, cancellationToken, DesignerEra);
            interruptedRetry = presenter.CurrentView is EncounterView;
            if (interruptedRetry)
                presenter.ShowError(new ErrorView(
                    "The interrupted encounter is ready to retry with the saved tank and exact enemy plan. "
                    + "Every vehicle will start fresh. No result or reward was granted.", true));
        });
    }

    internal static void ConfirmDesign()
    {
        if (!EnsureInitialized()) return;
        Enqueue(() => controller!.ConfirmDesignAsync());
    }

    internal static void SelectOnly(string offerId)
    {
        if (!EnsureInitialized()) return;
        Enqueue(() => controller!.SelectOfferAsync(offerId));
    }

    internal static void BeginSelected()
    {
        if (!EnsureInitialized()) return;
        Enqueue(() => RunBattleAsync(token => controller!.BeginEncounterAsync(token)));
    }

    internal static void BeginSession() => ResetInMemory("editor/battle session boundary; saved slots retained.");

    internal static void PlayFromDesigner()
    {
        if (!CampaignNativeFlow.Editing || !EnsureInitialized()) return;
        var cancellationToken = lifetime.Token;
        Enqueue(async () =>
        {
            if (presenter.CurrentView is null or StartView)
            {
                if (nativeContent == null && Plugin.AllowFixtureCatalogue?.Value != true)
                { CampaignNativeMenu.ShowNewGameOptions(); return; }
                var chosen = ChooseNewGameSlot();
                if (chosen == 0)
                {
                    ShowSlotsFull();
                    return;
                }
                if (chosen != CurrentSlot)
                {
                    // No command can be in flight before the first Play.
                    CurrentSlot = chosen;
                    controller = null;
                    store = null;
                    initialized = false;
                    if (!EnsureInitialized()) return;
                }
                nativeContent?.PinTo(store!.RootDirectory);
                await controller!.StartNewRunAsync(setup!, cancellationToken);
                if (presenter.CurrentView is OffersView)
                {
                    CampaignNativeFlow.CommitNewGame();
                }
                // First Play quotes the actual editor tank and prepares the
                // offers. Only an explicit card choice may select a battle.
                CampaignNativeMenu.Open();
                return;
            }
            else if (controller!.Snapshot?.SelectedOfferId == null && presenter.CurrentView is BuildingView)
            {
                await controller!.ConfirmDesignAsync(cancellationToken);
                return; // Era boundary: choose a challenge before this fight.
            }
            if (controller!.Snapshot?.SelectedOfferId == null)
            {
                CampaignNativeMenu.Open();
                return;
            }
            await controller!.RefreshEncounterDesignAsync(cancellationToken);
            if (presenter.CurrentView is not OffersView) return;
            CampaignSlotManager.SavePreEncounter(CurrentSlot);
            CampaignNativeMenu.Close();
            await RunBattleAsync(token => controller.BeginEncounterAsync(token));
        });
    }

    internal static void SelectForEditor(string offerId)
    {
        if (!EnsureInitialized()) return;
        Enqueue(async () =>
        {
            await controller!.SelectOfferAsync(offerId);
            if (presenter.CurrentView is OffersView { SelectedOfferId: not null })
                CampaignNativeFlow.ReturnToDesigner();
        });
    }

    internal static void SelectAndBegin(string offerId)
    {
        if (!EnsureInitialized()) return;
        Enqueue(async () =>
        {
            await controller!.SelectOfferAsync(offerId);
            CampaignSlotManager.SavePreEncounter(CurrentSlot);
            await RunBattleAsync(token => controller.BeginEncounterAsync(token));
        });
    }

    internal static void ShowError(string message)
    {
        presenter.ShowError(new ErrorView(message, false));
    }

    internal static void Continue()
    {
        if (!EnsureInitialized()) return;
        Enqueue(async () =>
        {
            await controller!.ContinueAsync();
            if (CampaignNativeFlow.OwnsSession && presenter.CurrentView is BuildingView)
                CampaignNativeFlow.ReturnToDesigner();
        });
    }

    internal static void Retry()
    {
        if (!EnsureInitialized()) return;
        var cancellationToken = lifetime.Token;
        Enqueue(async () =>
        {
            if (presenter.CurrentView is ResultView { CanRetryLoss: true, IsTerminal: false })
            {
                await controller!.PrepareLossRetryAsync(cancellationToken);
                if (View is OffersView) { CampaignNativeFlow.ReturnToDesigner(); CampaignNativeMenu.Close(); }
                return;
            }
            if (presenter.CurrentView is ResultView { NextPhase: RunPhase.Suspended })
                await controller!.ResumeAsync(cancellationToken);
            if (interruptedRetry || presenter.CurrentView is EncounterView)
            {
                interruptedRetry = false;
                await RunBattleAsync(token => controller!.RetryEncounterAsync(token));
            }
        });
    }

    internal static void ContinueEndless()
    {
        if (!EnsureInitialized()) return;
        Enqueue(async () =>
        {
            await controller!.ContinueEndlessAsync(lifetime.Token);
            if (presenter.CurrentView is BuildingView) CampaignNativeFlow.ReturnToDesigner();
        });
    }

    internal static void Finish()
    {
        if (EnsureInitialized()) Enqueue(() => controller!.FinishAsync(lifetime.Token));
    }

    internal static void ChooseAnotherBattle()
    {
        if (!EnsureInitialized()) return;
        Enqueue(async () => { await controller!.PrepareLossRetryAsync(lifetime.Token); CampaignNativeMenu.Open(); });
    }

    internal static void Retire()
    {
        if (EnsureInitialized()) Enqueue(() => controller!.RetireAsync(lifetime.Token));
    }

    internal static void ShowSlots()
    {
        if (Busy) return;
        ResetInMemory("slot list requested; saved runs retained.");
        CampaignNativeMenu.Open();
    }

    internal static void ReturnToMenu()
    {
        // Keep the campaign outcome registered until the host has finished the
        // native mission and requested the scene change; clearing it early lets
        // native result screens race the return and hang the loading scene.
        CustomBattleHostSpike.RequestCampaignReturn();
        CampaignNativeFlow.EndSession();
        ResetInMemory(
            "return requested; in-memory controller/view cleared; slot stores remain authoritative.");
    }

    private static void ResetInMemory(string reason)
    {
        pendingEditorReturn = null;
        itemsFromDesigner = false;
        commandGeneration++;
        var previousLifetime = lifetime;
        lifetime = new CancellationTokenSource();
        previousLifetime.Cancel();
        previousLifetime.Dispose();
        queue.Clear();
        busy = false;
        presenter.Reset();
        controller = null;
        store = null;
        gameAdapter = null;
        nativeContent = null;
        interruptedRetry = false;
        initialized = false;
        fatal = null;
        Plugin.Logger.LogInfo("Expedition controller host: " + reason);
    }

    private static void Enqueue(Func<Task> action)
    {
        if (busy || queue.Count > 0)
        {
            // Serialized commands; the UI disables buttons while busy, but a
            // duplicate click should never enqueue a second native operation.
            return;
        }
        queue.Enqueue(action);
    }

    internal static bool RequestReturnToEditor(AttemptIdentity attempt, long session)
    {
        if (!CanReturnToEditor || CampaignNativeFlow.SessionGeneration != session
            || View is not EncounterView active || active.Attempt != attempt) return false;
        pendingEditorReturn = (attempt, session, CampaignGameAdapter.CaptureEditorReturn());
        encounterCancellation!.Cancel();
        Plugin.Logger.LogInfo($"Expedition pause: return to editor requested; contact={pendingEditorReturn.Value.Context.Contact}; cancelling generation {attempt.Generation} before persisting the return.");
        return true;
    }

    private static async Task RunBattleAsync(Func<CancellationToken, Task> run)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        encounterCancellation = cancellation;
        try { await run(cancellation.Token); }
        finally { if (ReferenceEquals(encounterCancellation, cancellation)) encounterCancellation = null; }
    }

    private static bool EnsureInitialized()
    {
        if (initialized) return fatal == null;
        initialized = true;
        try
        {
            var slotRoot = CampaignSlotManager.Root(CurrentSlot);
            Directory.CreateDirectory(slotRoot);
            store = CampaignSlotManager.Store(CurrentSlot);
            var saved = store.LoadActiveRun(ModAvailability.None);
            CampaignVeteranUnlocks.CaptureBoundary(store, saved);
            var useNative = !CampaignSlotPolicy.UseFixture(saved.Snapshot?.CatalogueVersion,
                CampaignNativeFlow.NewGameSession, Plugin.AllowFixtureCatalogue?.Value == true);
            if (useNative && saved.Snapshot == null && !CampaignNativeFlow.NewGameSession)
                throw new InvalidOperationException("Choose New Game to prepare the native catalogue. No run was created.");
            nativeContent = useNative ? CampaignNativeCatalogue.ForSlot(store, saved, CampaignNativeFlow.NewGameSession) : null;
            var definition = nativeContent?.Definition ?? CampaignFixtureCatalogue.Create();
            var catalogue = new SeededEncounterCatalogue(definition,
                nativeContent == null ? CatalogueOptions.Fixture
                    : new CatalogueOptions(ContentQualification.Candidate, false, true));
            gameAdapter = new CampaignGameAdapter(
                definition.PriceUnitId, fixtureCost: nativeContent == null ? 100m : null, nativeContent);
            setup = new NewRunSetup(
                new ScenarioValue(nativeContent?.InitialBudget ?? 200m, definition.PriceUnitId),
                nativeContent?.Eras ?? CampaignFixtureCatalogue.EraSchedule(),
                definition.Version) { AdventureRules = nativeContent != null,
                    AdventureVersion = nativeContent != null ? 7 : 4,
                    StartingEraIndex = CampaignNativeFlow.NewGameSession ? CampaignNativeFlow.NewGameStartingEraIndex : 0,
                    TestPool = nativeContent?.Definition.TestPool != null,
                    Ironman = CampaignNativeFlow.NewGameSession && CampaignNativeFlow.NewGameIronman };
            controller = new ExpeditionController(
                catalogue,
                store,
                gameAdapter,
                presenter,
                () => ModAvailability.None,
                () => Guid.NewGuid().ToString("N"),
                () => DateTime.UtcNow.Ticks,
                () => CampaignDifficulty.Current,
                row => BattleLog.Append(
                    Path.Combine(Paths.ConfigPath, "SprocketExpedition", "stats", "battles.csv"),
                    row,
                    message => Plugin.Logger.LogWarning(message)),
                message => Plugin.Logger.LogWarning(message),
                CurrentSlot, EnemyName, message => Plugin.Logger.LogInfo(message));
            CampaignSlotManager.Write(CurrentSlot, store);
            presenter.Activate();
            Plugin.Logger.LogInfo(
                $"Expedition controller host initialized; slot={CurrentSlot}; "
                + $"catalogue={definition.Version}; nativeCostMode={nativeContent != null}; store={slotRoot}.");
            return true;
        }
        catch (Exception ex)
        {
            fatal = "Campaign controller initialization failed: " + ex.Message;
            presenter.ShowError(new ErrorView(fatal, false));
            Plugin.Logger.LogError(fatal + Environment.NewLine + ex);
            return false;
        }
    }

    private static async void RunCommand(Func<Task> action)
    {
        var generation = commandGeneration;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            if (generation == commandGeneration)
            {
                presenter.ShowError(new ErrorView(ex.Message, true));
            }
            Plugin.Logger.LogError("Expedition controller command failed: " + ex);
        }
        finally
        {
            if (generation == commandGeneration)
            {
                if (store != null)
                {
                    // Autosave summary after every command; the RunStore checkpoint
                    // itself is committed by the controller's SaveAsync boundaries.
                    CampaignSlotManager.Write(CurrentSlot, store);
                    CampaignVeteranUnlocks.CaptureBoundary(store, store.LoadActiveRun(ModAvailability.None));
                }
                busy = false;
            }
        }
    }
}
