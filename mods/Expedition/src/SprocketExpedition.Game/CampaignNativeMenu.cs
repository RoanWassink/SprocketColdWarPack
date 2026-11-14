using Sprocket;
using Sprocket.CustomBattles;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Game.UI;
using SprocketUiKit;
using UnityEngine;

namespace SprocketExpedition.Game;

// Expedition menu on the shared Sprocket UI Kit. All campaign choices live here
// (slots, delete, offers, result, continue/return); the battle itself has no
// campaign buttons. This file keeps the flow facade and composes the screens into
// a MenuPage; CampaignKitMenu.cs owns the Kit panel that shows it.
internal static partial class CampaignNativeMenu
{
    private static bool wantOpen;
    private static bool inBattle;
    private static object? lastView;
    private static float nextRefresh;
    private static bool lastBusy;
    private static int pendingArchive;
    private static bool timeScaleApplied;
    private static float previousTimeScale = 1f;
    private static string? preparationProgress;
    private static bool newGameOptions;
    private static bool slotsFull;
    private static int newGameSlot;
    private static bool difficultyOptions;
    private static bool difficultyFromNewGame;
    private static int startingEraSelection;
    private static bool ironmanSelection;
    private static object? pendingRetire;

    internal static void ShowSlotsFull()
    {
        slotsFull = true;
        pendingArchive = 0;
        newGameOptions = false;
        difficultyOptions = false;
        preparationProgress = null;
        Open();
    }

    internal static void ShowNewGameOptions(int slot = 0)
    {
        if (slot == 0 && !CampaignControllerHost.HasNewGameSpace())
        { ShowSlotsFull(); return; }
        slotsFull = false;
        pendingArchive = 0;
        newGameSlot = slot;
        newGameOptions = true;
        difficultyOptions = false;
        difficultyFromNewGame = false;
        startingEraSelection = 0;
        ironmanSelection = false;
        Open();
    }

    internal static void ShowDifficulty()
    {
        if (CampaignControllerHost.Busy) return;
        // Capture the parent screen before clearing the New Game marker. OK must
        // return there without resetting the selected starting era.
        difficultyFromNewGame = newGameOptions;
        newGameOptions = false;
        difficultyOptions = true;
        Open();
        Build();
    }

    private static void CloseDifficulty()
    {
        difficultyOptions = false;
        if (difficultyFromNewGame) newGameOptions = true;
        difficultyFromNewGame = false;
        Build();
    }

    internal static void ShowPreparation(string progress)
    {
        preparationProgress = progress;
        if (IsOpen) Build(); else Open();
    }
    internal static void ClearPreparation() => preparationProgress = null;

    // The menu flow wants the menu open (the Kit panel may be closed for a moment,
    // e.g. under the game's pause menu; Tick reopens it once the pause menu is gone).
    internal static bool IsOpen => wantOpen;

    internal static void Open()
    {
        var wasOpen = wantOpen;
        wantOpen = true;
        closeRequested = false;
        backRequested = false;
        closedByPause = false;
        if (!EnsurePanel()) return;
        ShowKitPanel(wasOpen ? "open (already open)" : "open");
        FreezeCampaignBattle();
        Build();
        if (!wasOpen) Plugin.Logger.LogInfo("Expedition native menu opened.");
    }

    internal static void FreezeForCampaignChoices() => FreezeCampaignBattle();

    private static void FreezeCampaignBattle()
    {
        if (timeScaleApplied) return;
        var mode = UnityEngine.Object.FindObjectOfType<DeathmatchGameMode>();
        if (mode == null || mode.gameState != DeathmatchGameMode.ModeState.Running) return;
        previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        timeScaleApplied = true;
        Plugin.Logger.LogInfo(
            "Expedition native menu: campaign battle frozen while the UI-only choice screen is open; "
            + $"previousTimeScale={previousTimeScale:G3}.");
    }

    private static void RestoreCampaignTimeScale()
    {
        if (!timeScaleApplied) return;
        Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        timeScaleApplied = false;
        Plugin.Logger.LogInfo(
            $"Expedition native menu: campaign battle time restored to {Time.timeScale:G3}.");
    }

    internal static void Close()
    {
        var wasOpen = wantOpen;
        slotsFull = false;
        newGameSlot = 0;
        newGameOptions = false;
        difficultyOptions = false;
        difficultyFromNewGame = false;
        preparationProgress = null;
        pendingArchive = 0;
        pendingRetire = null;
        RestoreCampaignTimeScale();
        wantOpen = false;
        closeRequested = false;
        backRequested = false;
        closedByPause = false;
        HideKitPanel("menu close");
        lastView = null;
        ResetPage();
        if (wasOpen && Plugin.Detailed) Plugin.Trace("[Expedition menu] closed by the Expedition flow.");
    }

    internal static void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    private static void CloseOrLeave()
    {
        if (CampaignNativeFlow.Editing && CampaignControllerHost.View is ArmoryView)
        {
            CampaignControllerHost.CloseItemsToDesigner();
            return;
        }
        if (!CampaignNativeFlow.Editing)
        {
            CampaignNativeFlow.EndSession();
            CampaignControllerHost.BeginSession();
        }
        else Close();
    }

    private static void ComposeScreen(MenuPage next, object view)
    {
        if (view is ArmoryView armory) { ComposeArmory(next, armory); return; }
        var display = view;
        if (view is OffersView { EraBoss: not null } offers && CampaignControllerHost.BossPreviewText is { } boss)
            display = offers with { EraBoss = boss };
        var screen = ScreenComposer.Compose(display, new ScreenContext(
            CampaignControllerHost.Busy, CampaignNativeFlow.OwnsSession,
            CampaignNativeFlow.Editing, CampaignControllerHost.CurrentSlot,
            CampaignNativeFlow.BattleMap, CampaignControllerHost.CanRetry),
            display is ResultView statsView
                ? CampaignBattleStats.Current.For(CampaignBattleStats.Key(statsView.Result.Attempt.RunId,
                    statsView.Result.Attempt.EncounterId, statsView.Result.Attempt.PlanHash,
                    statsView.Result.Attempt.Generation))
                : null);
        var enabled = !CampaignControllerHost.Busy;
        if (view is BuildingView or OffersView or ResultView && CampaignControllerHost.RunSnapshot != null)
            next.Header.Add(new MenuButton("DIFFICULTY · " + CampaignDifficulty.Summary, ShowDifficulty, false, enabled));
        if (CampaignControllerHost.RewardSnapshot is { } inventory)
            next.Header.Add(new MenuButton($"ITEMS {inventory.Inventory.Count}/6 · ACTIVE {inventory.Active.Count}/3",
                CampaignControllerHost.ShowItems, false, enabled));
        else if (view is StartView)
            next.Header.Add(new MenuButton("DIFFICULTY · " + CampaignDifficulty.Summary, ShowDifficulty, false, enabled));
        next.Title = screen.Title.ToUpperInvariant();

        if (view is ResultView resultView && screen.Result is { } summary)
            // Visual result: banner, tiles, notice, received item and Details replace the plain lines.
            next.Banners.Add(BuildBanner(resultView, summary, screen.Blocks));
        else if (view is OffersView && screen.Facts is { } facts)
        {
            // Visual offers: round / era / budget as chips and one instruction line.
            next.Facts.AddRange(facts);
            next.Hint = screen.Hint ?? "";
        }
        else
            foreach (var line in screen.Lines)
                next.Lines.Add(line.Replace(CampaignNativeCatalogue.CostUnit, "design cost", StringComparison.Ordinal));

        var cards = new List<MenuCard>();
        for (var index = 0; index < screen.Cards.Count; index++)
        {
            var card = screen.Cards[index];
            var menuCard = new MenuCard { Title = card.Title };
            if (CampaignControllerHost.UsesNativeContent && view is OffersView nativeOffers)
                TryComposeOffer(menuCard, nativeOffers, index, card.Title);
            if (menuCard.Offer == null)
            {
                // Text card: fixture catalogue, or the visual card could not be composed.
                if (view is OffersView rewardOffers && rewardOffers.Offers[index].Extras is { } drop)
                {
                    menuCard.Tag = RarityText(drop.Reward.Rarity);
                    menuCard.Colour = RewardColor(drop.Reward.Rarity);
                }
                menuCard.Body = string.Join("\n", card.Lines);
            }
            if (card.Action is { } cardAction)
                menuCard.Buttons.Add(new MenuButton(cardAction.Label, () => Dispatch(cardAction, view),
                    cardAction.Primary, cardAction.Enabled));
            cards.Add(menuCard);
        }
        if (cards.Count > 0) next.CardRows.Add(cards);
        foreach (var action in screen.Actions)
            next.Actions.Add(new MenuButton(action.Label, () => Dispatch(action, view), action.Primary, action.Enabled));
    }

    // Visual offer card from the native catalogue. Loads the map picture once (cached).
    // On any failure the card stays a text card, built from the composer's lines.
    private static void TryComposeOffer(MenuCard menuCard, OffersView view, int index, string plainTitle)
    {
        try
        {
            var offer = view.Offers[index];
            var era = offer.Era.NativeId;
            var lookups = new OfferLookups(
                CampaignControllerHost.EnemyName,
                id => CampaignControllerHost.EnemyCost(id, era),
                id => CampaignControllerHost.DescribeEnemy(id, era));
            var model = OfferVisuals.Build(offer, plainTitle, view.AvailableBudget.Value, lookups, CampaignControllerHost.RunSnapshot?.TestPool == true);
            menuCard.Thumb = CampaignMapThumbnails.Get(offer.MapId);
            menuCard.Title = model.Title;
            menuCard.Offer = model;
        }
        catch (Exception ex)
        {
            menuCard.Offer = null;
            Plugin.Logger.LogWarning("Expedition menu: visual offer card failed, showing the text card. " + ex.Message);
        }
    }

    private static void Dispatch(ScreenAction action, object source)
    {
        if (!action.Enabled || CampaignControllerHost.Busy
            || !ReferenceEquals(source, CampaignControllerHost.View)) return;
        switch (action.Command)
        {
            case ScreenCommand.Resume: CampaignControllerHost.Resume(); break;
            case ScreenCommand.NewGame:
                if (Plugin.AllowFixtureCatalogue?.Value == true && !CampaignControllerHost.UsesNativeContent)
                    CampaignControllerHost.StartNew();
                else ShowNewGameOptions();
                break;
            case ScreenCommand.ChooseOffer:
                if (action.Argument == null) return;
                if (CampaignNativeFlow.OwnsSession) CampaignControllerHost.SelectForEditor(action.Argument);
                else CampaignControllerHost.SelectAndBegin(action.Argument);
                break;
            case ScreenCommand.ConfirmDesign: CampaignControllerHost.ConfirmDesign(); break;
            case ScreenCommand.Continue: CampaignControllerHost.Continue(); break;
            case ScreenCommand.Retry: CampaignControllerHost.Retry(); break;
            case ScreenCommand.ChooseAnotherBattle: CampaignControllerHost.ChooseAnotherBattle(); break;
            case ScreenCommand.Retire: pendingRetire = source; Build(); break;
            case ScreenCommand.ContinueEndless: CampaignControllerHost.ContinueEndless(); break;
            case ScreenCommand.Finish: CampaignControllerHost.Finish(); break;
            case ScreenCommand.Slots: CampaignControllerHost.ShowSlots(); break;
            case ScreenCommand.ReturnToMenu:
                if (CampaignNativeFlow.Editing) CampaignNativeFlow.ReturnToMainMenu();
                else CampaignControllerHost.ReturnToMenu();
                break;
            case ScreenCommand.ReturnToDesigner: CampaignNativeFlow.ReturnToDesigner(); Close(); break;
        }
    }

    // Refreshes the page model that the Kit panel reads; builds no GameObjects.
    internal static void Build()
    {
        if (!wantOpen) return;
        inBattle = UnityEngine.Object.FindObjectOfType<DeathmatchGameMode>() != null;
        var next = new MenuPage();
        var view = CampaignControllerHost.View;
        if (pendingRetire != null && ReferenceEquals(pendingRetire, view))
        {
            next.Title = "RETIRE RUN?";
            next.Lines.Add("End this run and save the record? This cannot be undone.");
            next.CloseAction = () => { pendingRetire = null; Build(); };
            next.Actions.Add(new MenuButton("END RUN AND SAVE RECORD", () =>
            {
                if (!ReferenceEquals(pendingRetire, CampaignControllerHost.View)) return;
                pendingRetire = null;
                CampaignControllerHost.Retire();
            }, true, !CampaignControllerHost.Busy));
            next.Actions.Add(new MenuButton("CANCEL", () => { pendingRetire = null; Build(); }));
        }
        else if (slotsFull)
            ComposeSlots(next);
        else if (difficultyOptions)
            ComposeDifficulty(next);
        else if (newGameOptions)
            ComposeNewGameOptions(next);
        else if (preparationProgress != null)
        {
            next.Title = "PREPARING NEW EXPEDITION";
            next.Lines.Add(preparationProgress);
            next.Status = preparationProgress;
        }
        else
        {
            switch (view)
            {
                case null:
                    ComposeSlots(next);
                    break;
                case EncounterView:
                    Close();
                    return;
                default:
                    ComposeScreen(next, view);
                    break;
            }
            Action closeAction = inBattle ? CampaignControllerHost.ReturnToMenu : CloseOrLeave;
            next.Header.Add(new MenuButton(inBattle ? "RETURN TO MENU" : "CLOSE", closeAction,
                false, !CampaignControllerHost.Busy));
            // Esc / ✕ = the old CLOSE button; RETURN TO MENU in battle stays an explicit button.
            next.CloseAction = inBattle ? null : CloseOrLeave;
        }
        PublishPage(next, view);
    }

    private static void ComposeNewGameOptions(MenuPage next)
    {
        CampaignEraAvailability.EnsureStarted();
        var schedule = CampaignEraAvailability.ScheduleEraNames;
        if (schedule.Count == 0)
        {
            next.Title = "NEW EXPEDITION - OPTIONS";
            next.CloseAction = Close;
            next.Notes.Add("No playable native era was found. " + CampaignEraAvailability.Status);
            next.Actions.Add(new MenuButton("CANCEL", Close));
            return;
        }
        // The menu may finish the era check after it opened. Selection is an
        // index into the ordered schedule, so clipping to the actual list never
        // points past a changed schedule.
        if (startingEraSelection >= schedule.Count) startingEraSelection = schedule.Count - 1;
        if (startingEraSelection < 0) startingEraSelection = 0;
        var battlesPerEra = 4;
        var totalRounds = schedule.Count * battlesPerEra;

        next.Title = "NEW EXPEDITION - OPTIONS";
        next.CloseAction = Close; // Esc / ✕ = CANCEL
        next.ChoicesHeading = "STARTING ERA";
        List<MenuButton>? row = null;
        for (var index = 0; index < schedule.Count; index++)
        {
            var choice = index;
            if (index % 3 == 0) next.Choices.Add(row = new List<MenuButton>());
            row!.Add(new MenuButton((index == startingEraSelection ? "SELECTED: " : "") + schedule[index],
                () => { startingEraSelection = choice; Build(); }, index == startingEraSelection));
        }
        var era = schedule[startingEraSelection];
        var lastEra = schedule[schedule.Count - 1];
        if (CampaignEraAvailability.Ready)
        {
            next.Notes.Add($"Start in {era}, at round {startingEraSelection * battlesPerEra + 1} of {totalRounds}.");
            next.Notes.Add($"{(schedule.Count - startingEraSelection) * battlesPerEra} battles remain through {lastEra}; every fourth battle is a boss.");
        }
        else
        {
            next.Notes.Add("Checking available tanks...");
            next.Status = "Checking available tanks...";
        }
        if (CampaignNativeCatalogue.TestPoolEnabled)
            next.Notes.Add($"TEST POOL ACTIVE: {CampaignNativeCatalogue.TestPoolCount} tanks (enemies only from test pool)");
        next.Notes.Add("Earlier rounds are skipped: no wins, loot or veteran unlocks are granted.");
        next.Notes.Add("You receive a starter tank from this era and a budget for at least two copies.");
        next.Notes.Add("This choice applies only to this new run. Existing saves keep their own era.");
        next.Actions.Add(new MenuButton("START EXPEDITION", () =>
        {
            if (!CampaignEraAvailability.Ready) { Build(); return; }
            CampaignNativeFlow.BeginNewGame(startingEraSelection, ironmanSelection, newGameSlot);
        }, true, CampaignEraAvailability.Ready));
        next.Actions.Add(new MenuButton("DIFFICULTY · " + CampaignDifficulty.Summary, ShowDifficulty));
        next.Actions.Add(new MenuButton("IRONMAN: " + (ironmanSelection ? "ON" : "OFF"),
            () => { ironmanSelection = !ironmanSelection; Build(); }));
        next.Notes.Add("One defeat ends the run. Separate records. Score ×1.3.");
        next.Actions.Add(new MenuButton("CANCEL", Close));
    }

    private static void ComposeDifficulty(MenuPage next)
    {
        next.Title = "ENEMY DIFFICULTY";
        next.SubPage = PageDifficulty;
        next.Back = CloseDifficulty;
        next.Lines.Add("Choose how much enemy force future battles can contain. More budget means larger "
            + "enemy fleets, and later eras are naturally stronger. Your own purchasing budget "
            + "is unchanged.");
        var multipliers = new List<MenuButton>();
        foreach (var choice in CampaignDifficulty.Multipliers)
            multipliers.Add(new MenuButton(
                (choice == CampaignDifficulty.Selected ? "SELECTED: " : "") + CampaignDifficulty.Label(choice),
                () => { CampaignDifficulty.SetMultiplier(choice); Build(); },
                choice == CampaignDifficulty.Selected));
        next.Choices.Add(multipliers);
        next.Choices.Add(new List<MenuButton>
        {
            new((CampaignDifficulty.EraProgression ? "[X] " : "[ ] ") + "STRONGER LATER ERAS",
                () => { CampaignDifficulty.ToggleEraProgression(); Build(); },
                CampaignDifficulty.EraProgression)
        });
        var eraCount = CampaignControllerHost.RunSnapshot?.EraSchedule.Count;
        if (eraCount is null && CampaignEraAvailability.Ready)
        {
            var scheduled = CampaignEraAvailability.ScheduleEraNames.Count;
            if (scheduled > 0) eraCount = scheduled;
        }
        next.Notes.Add(DifficultyScaling.FinalEraNote(eraCount));
        next.Notes.Add(CampaignDifficulty.HonestNote);
        var run = CampaignControllerHost.RunSnapshot;
        if (run?.Rewards != null)
        {
            if (run.Rewards.Version >= 5 || run.GenerationPolicy == 5)
                next.Notes.Add("THIS RUN: difficulty changes apply to future battles.");
            else if (CampaignControllerHost.CanEnablePolicy5ForCurrentRun)
                next.Choices.Add(new List<MenuButton>
                {
                    new("APPLY DIFFICULTY TO FUTURE BATTLES IN THIS RUN",
                        CampaignControllerHost.EnablePolicy5ForCurrentRun, true, !CampaignControllerHost.Busy)
                });
            else if (run.Rewards.Version < 4)
                next.Notes.Add("THIS RUN: difficulty cannot be enabled mid-run. Start a new expedition "
                    + "to use difficulty, formations and ambushes.");
            else
                next.Notes.Add("THIS RUN: finish the current battle and its rewards first. "
                    + "Then you can enable difficulty for future battles.");
        }
        else if (run != null)
            next.Notes.Add("THIS RUN: difficulty is unavailable for this saved expedition. "
                + "Start a new expedition to use it.");
        next.Actions.Add(new MenuButton("OK", CloseDifficulty));
    }

    private static void ComposeSlots(MenuPage next)
    {
        var enabled = !CampaignControllerHost.Busy;
        if (pendingArchive != 0)
        {
            var selected = pendingArchive;
            next.Title = $"ARCHIVE SLOT {selected}?";
            next.SubPage = PageArchive;
            next.Back = () => { pendingArchive = 0; Build(); };
            next.Lines.Add("This frees the slot for a new run. The existing files are kept in a local archive.");
            next.Lines.Add("This action does not record a combat result.");
            next.Actions.Add(new MenuButton(slotsFull ? "DELETE & START NEW" : "ARCHIVE & FREE SLOT",
                () =>
                {
                    if (pendingArchive != selected) return;
                    pendingArchive = 0;
                    if (slotsFull)
                    {
                        if (!CampaignControllerHost.DeleteAndStartNew(selected))
                        { pendingArchive = selected; next.Status = "Archive failed; no new run was started."; next.StatusKind = StatusKind.Error; PublishPage(next, CampaignControllerHost.View); }
                    }
                    else { CampaignControllerHost.DeleteSlot(selected); Build(); }
                }, true, enabled));
            next.Actions.Add(new MenuButton("CANCEL", () => { pendingArchive = 0; Build(); }, false, enabled));
            return;
        }
        next.Title = slotsFull ? "SAVE SLOTS FULL" : "SAVE SLOTS";
        if (slotsFull)
        {
            next.CloseAction = Close;
            next.Actions.Add(new MenuButton("BACK", Close));
        }
        next.Lines.Add(slotsFull
            ? "Choose a run to archive, then continue to New Game options."
            : "Open a saved expedition or archive a slot to make room for a new run.");
        for (var slot = 1; slot <= CampaignSlotManager.SlotCount; slot++)
        {
            var selectedSlot = slot;
            var summary = CampaignControllerHost.Slot(slot);
            var card = new MenuCard
            {
                Inline = true,
                Title = $"Slot {slot}",
                Body = summary?.Describe() ?? $"Slot {slot} — open to check save status"
            };
            if (!slotsFull)
                card.Buttons.Add(new MenuButton("OPEN", () => CampaignControllerHost.SelectSlot(selectedSlot), true, enabled));
            card.Buttons.Add(new MenuButton(slotsFull ? "Delete & start new" : "DELETE / ARCHIVE",
                () => { pendingArchive = selectedSlot; Build(); }, false, enabled));
            next.CardRows.Add(new List<MenuCard> { card });
        }

        if (CampaignControllerHost.Fatal is { } fatal)
        {
            next.Notes.Add(fatal);
            next.Status = fatal;
            next.StatusKind = StatusKind.Error;
        }
        else if (!slotsFull)
            next.Actions.Add(new MenuButton("DIFFICULTY · " + CampaignDifficulty.Summary, ShowDifficulty, false, enabled));
    }

    internal static void Tick()
    {
        if (!IsOpen) return;
        FreeCursorOutsideKitGameContext();
        HandleKitRequests();
        if (!IsOpen) return;
        ReopenAfterPause();
        if (newGameOptions && CampaignEraAvailability.IsChecking && CampaignEraAvailability.Pump())
        {
            Build();
            return;
        }
        if (!newGameOptions && preparationProgress == null && CampaignControllerHost.View is EncounterView)
        {
            Close();
            return;
        }

        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;
        var view = CampaignControllerHost.View;
        var currentBusy = CampaignControllerHost.Busy;
        if (!ReferenceEquals(view, lastView) || currentBusy != lastBusy)
        {
            lastView = view;
            lastBusy = currentBusy;
            Build();
        }
    }
}
