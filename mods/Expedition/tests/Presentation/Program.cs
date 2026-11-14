using System;
using System.Collections.Generic;
using System.Linq;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Core.Run;
using SprocketExpedition.Game.UI;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("every presenter view maps to a screen", EveryViewMaps),
            ("start resume/new-game policy and record limit", StartPolicy),
            ("building displays supplied values and confirm rules", BuildingDisplayAndConfirm),
            ("owned building offers return to designer", BuildingReturnToDesigner),
            ("valid offers include risk, map, composition and ids", ValidOffersDisplay),
            ("offers use actual context map in owned sessions", OwnedOffersUseActualMap),
            ("malformed offers fail safe with no choose actions", MalformedOffers),
            ("selected offer marker is shown", SelectedOfferMarker),
            ("encounter shows supplied counts and live state", EncounterMapping),
            ("result separates victory, loss, abort and technical failure", ResultOutcomes),
            ("result retry and continue gates", ResultActionGates),
            ("finished run blocks resume and retry", FinishedRun),
            ("endless choice exposes continue and finish", EndlessChoice),
            ("blocked screen preserves data without recovery action", BlockedScreen),
            ("error retry requires view and context permission", ErrorRetry),
            ("unknown view gives a safe message", UnknownView),
            ("busy disables every action and card action", BusyDisablesEverything),
            ("counts and amounts come from supplied views", SuppliedCounts)
            ,("curated boss has one card and one refit action", CuratedBossCard)
            ,("themed groups show roles without promising absent flanks", ThemedGroupCard)
            ,("menu numbers use spaces and ignore the culture", NumberFormatting)
            ,("offers header becomes facts and one hint", OffersHeaderFacts)
            ,("composition lists one line per design with roles", CompositionLines)
            ,("reward block names the item, effect and duration", RewardBlocks)
            ,("cost bar clamps and flags an over-budget force", CostBars)
            ,("offer card model keeps every old line", OfferCardModelKeepsInformation)
            ,("map thumbnails: file lookup and uv crop", MapThumbnailFiles)
            ,("result banner, tiles and details", ResultSummaries)
            ,("schema 6 retry, retire, score, boss preview and records", Phase1Screens)
            ,("test pool chip, tank title and record label", TestPoolLabels)
            ,("defeat blocks show losses, battle stats and +n more", BattleStatsScreen)
        };

        foreach (var test in tests)
        {
            try
            {
                test.Body();
                _passed++;
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                _failed++;
                Console.WriteLine($"FAIL {test.Name}");
                Console.WriteLine($"     {exception.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed, {tests.Length} total");
        return _failed == 0 ? 0 : 1;
    }

    private static void EveryViewMaps()
    {
        var views = new (string Name, object View)[]
        {
            ("start", StartViewSample(active: false, resume: false, records: 2)),
            ("building", BuildingSample(DesignQuoteSample())),
            ("offers", OffersSample(ValidOffers())),
            ("encounter", EncounterSample(4, 6, combatStarted: false)),
            ("result", ResultSample(EncounterOutcome.Victory)),
            ("finished", new RunFinishedView(3, 1, RunPhase.Finished, false)),
            ("endless", new EndlessChoiceView(4, Money(25), 9)),
            ("blocked", new BlockedView("The save is unavailable.", true)),
            ("error", new ErrorView("Catalogue unavailable.", true))
        };

        foreach (var (name, view) in views)
        {
            var model = ScreenComposer.Compose(view, Context());
            Check(!string.IsNullOrWhiteSpace(model.Title), $"{name}: title must not be empty");
            Check(model.Lines.Count > 0, $"{name}: screen must include instructions");
            Check(FindAction(model, ScreenCommand.Slots) is not null,
                $"{name}: slots action must always be present");
            CheckNoNullCollections(model, name);
        }

        var unknown = ScreenComposer.Compose(new object(), Context());
        Check(!string.IsNullOrWhiteSpace(unknown.Title), "unknown: title must not be empty");
        Check(unknown.Lines.Count > 0, "unknown: safe message must not be empty");
        CheckNoNullCollections(unknown, "unknown");
    }

    private static void Phase1Screens()
    {
        var loss=ResultSample(EncounterOutcome.FriendlyForceDestroyed) with
            {CanRetryLoss=true,RoundAttempts=2,NextPhase=RunPhase.ChoosingEncounter,IsTerminal=false};
        var screen=ScreenComposer.Compose(loss,Context());
        Check(RequireAction(screen,ScreenCommand.Retry).Enabled,"normal loss RETRY");
        Check(RequireAction(screen,ScreenCommand.ChooseAnotherBattle).Enabled,"normal loss another battle");
        Text(screen,"Attempt 2 failed");Text(screen,"×0.75 per retry");
        screen=ScreenComposer.Compose(loss with{CanRetryLoss=false,IsTerminal=true,NextPhase=RunPhase.Lost},Context(canRetry:true));
        Check(FindAction(screen,ScreenCommand.Retry)==null&&FindAction(screen,ScreenCommand.ChooseAnotherBattle)==null,"Ironman terminal has no retry");
        var win=ResultSample(EncounterOutcome.Victory) with{ScoreGained=843.75m,ScoreBreakdown="Dangerous x1.5 · flawless +25% · 1 retry x0.75"};
        screen=ScreenComposer.Compose(win,Context());
        Check(screen.Result!.Tiles.Any(t=>t.Caption=="SCORE"&&t.Value=="+844"),"score rounded only in display");
        Check(screen.Result.Message==win.ScoreBreakdown,"score breakdown is visible in the banner before expanding Details");
        Text(screen,"flawless +25%");
        var offers=OffersSample(ValidOffers()) with{CanRetire=true,EraBoss="Era boss: Fixture boss (400)",
            EnemyCosts=ValidOffers().ToDictionary(o=>o.OfferId,_=>14m),AvailableBudget=Money(10)};
        screen=ScreenComposer.Compose(offers,Context());
        Check(RequireAction(screen,ScreenCommand.Retire).Enabled,"offer Retire button");
        Text(screen,"Enemy force: 1.4× your budget");Text(screen,"Era boss: Fixture boss (400)");
        Check(screen.Hint!.Contains("Era boss: Fixture boss"),"boss visible in rich offer header too");
        screen=ScreenComposer.Compose(BuildingSample(DesignQuoteSample()) with{CanRetire=true},Context());
        Check(RequireAction(screen,ScreenCommand.Retire).Enabled,"building Retire button");
        screen=ScreenComposer.Compose(new StartView(false,false,new[]{new RunRecordSummary("retired",RunPhase.Retired,4,2,new EraReference("Fixture era")){Score=123.45m,Ironman=true}}),Context());
        Text(screen,"Score: 123");Text(screen,"Ironman: Yes");Text(screen,"Outcome: Retired");Text(screen,"Era: Fixture era");
        screen=ScreenComposer.Compose(new RunFinishedView(4,2,RunPhase.Retired,false),Context());
        Check(screen.Title=="Expedition retired","Retired is explicit");
        var busy=ScreenComposer.Compose(loss,Context(busy:true));
        Check(!RequireAction(busy,ScreenCommand.Retry).Enabled&&!RequireAction(busy,ScreenCommand.ChooseAnotherBattle).Enabled,"busy guards new actions");
    }

    private static void CuratedBossCard()
    {
        var reward = new SprocketExpedition.Core.Run.RewardItem("boss-item", SprocketExpedition.Core.Run.RewardKind.CapturedPrototype,
            SprocketExpedition.Core.Run.RewardRarity.UltraRare, 1m, 6, 6, "boss");
        var boss = Plan("boss-offer", EncounterRisk.Standard) with { Round = 4, SupplyReward = 2,
            Extras = new SprocketExpedition.Core.Run.EncounterExtras(3, 0, reward, "boss", "next-era", "The Fortress") { CuratedBoss = true } };
        var view = new OffersView(4,boss.Era,Money(100),new[]{boss},boss.OfferId);
        var model = ScreenComposer.Compose(view,new ScreenContext(false,true,false,1,"Fields"));
        Check(model.Title == "Boss encounter" && model.Cards.Count == 1,"one explicit boss screen");
        Check(model.Cards[0].Action?.Label == "BUILD & FACE BOSS","no risk alternatives, keep editor loop");
        Check(ScreenComposer.Compose(view with{Offers=new[]{boss,boss}},Context()).Cards.Count==0,"two boss cards rejected");
        Check(ScreenComposer.Compose(view with{Offers=new[]{boss with{Extras=boss.Extras! with{CuratedBoss=false}}}},Context()).Cards.Count==0,"single unmarked card rejected");
    }

    private static void ThemedGroupCard()
    {
        var reward = new RewardItem("themed-item", RewardKind.MassProduction,
            RewardRarity.Common, .25m, 2, 2);
        var withFlank = new SpecialBattle(SpecialPreset.TacticalGroup,
            "Leopard battlegroup · light flanks", SpecialSpawnPattern.CombinedArms,
            new[]
            {
                new SpecialUnit("Leopard", 3) { DeploymentRole = "Center" },
                new SpecialUnit("Light", 2) { DeploymentRole = "Flank" }
            }, "", 0m, 0);
        var themed = Plan("offer-themed", EncounterRisk.Standard) with
        {
            Enemies = new[] { new EnemyUnit("Leopard", 3), new EnemyUnit("Light", 2) },
            Extras = new EncounterExtras(4, 0, reward) { Special = withFlank }
        };
        var offers = new[] { themed, Plan("offer-b", EncounterRisk.Cautious),
            Plan("offer-c", EncounterRisk.Dangerous) };
        var model = ScreenComposer.Compose(OffersSample(offers), Context(owned: true));
        var card = model.Cards[0];
        Check(card.Title.Contains("Leopard", StringComparison.Ordinal),
            "themed card title carries the actual theme");
        Check(card.Lines.Any(line => line.StartsWith("Formation:", StringComparison.Ordinal)
            && line.Contains("flanks 2", StringComparison.Ordinal)),
            "themed card shows actual flank composition");
        Check(card.Lines.Any(line => line.StartsWith("Tactical intent:", StringComparison.Ordinal)
            && line.Contains("flankers", StringComparison.Ordinal)),
            "themed card states the actual tactical intent");
        NoText(model, "SPECIAL ·");

        var noFlank = new SpecialBattle(SpecialPreset.TacticalGroup,
            "Leopard battlegroup · support", SpecialSpawnPattern.CombinedArms,
            new[]
            {
                new SpecialUnit("Leopard", 3) { DeploymentRole = "Center" },
                new SpecialUnit("Support", 1) { DeploymentRole = "Support" }
            }, "", 0m, 0);
        var supportOnly = themed with
        {
            Enemies = new[] { new EnemyUnit("Leopard", 3), new EnemyUnit("Support", 1) },
            Extras = themed.Extras! with { Special = noFlank }
        };
        var noFlankModel = ScreenComposer.Compose(
            OffersSample(new[] { supportOnly, offers[1], offers[2] }), Context(owned: true));
        var noFlankCard = noFlankModel.Cards[0];
        Check(noFlankCard.Lines.All(line =>
                !line.Contains("flank", StringComparison.OrdinalIgnoreCase)),
            "a missing flanker must never produce a light-flank promise");
        Check(noFlankCard.Lines.Any(line => line.Contains("support fire", StringComparison.Ordinal)),
            "support-only groups describe their actual intent");
    }

    private static void StartPolicy()
    {
        var fresh = ScreenComposer.Compose(
            StartViewSample(active: false, resume: false, records: 6), Context());
        Check(fresh.Cards.Count == 4, "start: at most four record cards must be shown");
        Check(FindAction(fresh, ScreenCommand.Resume) is null,
            "start: resume must not be offered without CanResume");
        var newGame = RequireAction(fresh, ScreenCommand.NewGame);
        Check(newGame.Enabled, "start: new game must be enabled when there is no active run");
        Check(FindAction(fresh, ScreenCommand.Slots) is { Enabled: true },
            "start: slots must be available");

        var active = ScreenComposer.Compose(
            StartViewSample(active: true, resume: true, records: 1), Context());
        Check(RequireAction(active, ScreenCommand.Resume).Enabled,
            "start: resume must be enabled when CanResume is true");
        Check(FindAction(active, ScreenCommand.NewGame) is null,
            "start: new game must not be offered while a run is active");
        Text(active, "active expedition");

        var blocked = ScreenComposer.Compose(
            StartViewSample(active: true, resume: false, records: 1), Context());
        Check(FindAction(blocked, ScreenCommand.Resume) is null,
            "start: resume must not be offered when it cannot resume");
        Check(FindAction(blocked, ScreenCommand.NewGame) is null,
            "start: new game must not replace an unresumable active run");
        Check(FindAction(blocked, ScreenCommand.ReturnToMenu) is null,
            "start: the menu should not return to itself");

        var recordCard = fresh.Cards[0];
        Check(recordCard.Title.Contains("run-0", StringComparison.Ordinal),
            "start: record card must identify its run");
        Check(recordCard.Lines.Any(line => line.Contains("victories", StringComparison.OrdinalIgnoreCase)),
            "start: record card must show the victory/defeat record");
    }

    private static void BuildingDisplayAndConfirm()
    {
        var editing = Context(editing: true);
        var valid = ScreenComposer.Compose(BuildingSample(DesignQuoteSample()), editing);
        Check(RequireAction(valid, ScreenCommand.ConfirmDesign).Enabled,
            "building: a valid quote while editing must enable confirm");
        Text(valid, "Budget: 20 credits");
        Text(valid, "Quote cost: 10 credits");
        Text(valid, "Friendly units: 2");
        Text(valid, "Round: 2");
        Text(valid, "Era: era-native");
        Text(valid, "Rejection: None");

        var pending = ScreenComposer.Compose(BuildingSample(null), editing);
        Check(RequireAction(pending, ScreenCommand.ConfirmDesign).Enabled,
            "building: a null quote is a pending capture and may confirm while editing");
        Text(pending, "Pending native capture");

        var invalidDesign = ScreenComposer.Compose(
            BuildingSample(DesignQuoteSample(designValid: false)), editing);
        Check(!RequireAction(invalidDesign, ScreenCommand.ConfirmDesign).Enabled,
            "building: an invalid design must disable confirm");
        Text(invalidDesign, "Design valid: No");

        var invalidEra = ScreenComposer.Compose(
            BuildingSample(DesignQuoteSample(eraValid: false)), editing);
        Check(!RequireAction(invalidEra, ScreenCommand.ConfirmDesign).Enabled,
            "building: an invalid era must disable confirm");
        Text(invalidEra, "Era valid: No");

        var quoteRejected = ScreenComposer.Compose(
            BuildingSample(DesignQuoteSample(rejection: "Native design failed")), editing);
        Check(!RequireAction(quoteRejected, ScreenCommand.ConfirmDesign).Enabled,
            "building: a quote rejection must disable confirm");
        Text(quoteRejected, "Native design failed");

        var viewRejected = ScreenComposer.Compose(
            BuildingSample(DesignQuoteSample(), rejection: "Missing content"), editing);
        Check(!RequireAction(viewRejected, ScreenCommand.ConfirmDesign).Enabled,
            "building: a view rejection must disable confirm even with a valid quote");
        Text(viewRejected, "Missing content");

        var notEditing = ScreenComposer.Compose(BuildingSample(DesignQuoteSample()), Context());
        Check(!RequireAction(notEditing, ScreenCommand.ConfirmDesign).Enabled,
            "building: confirm must be disabled outside the editor");
        Text(notEditing, "Open the design editor");
    }

    private static void BuildingReturnToDesigner()
    {
        var owned = ScreenComposer.Compose(BuildingSample(DesignQuoteSample()),
            Context(editing: true, owned: true));
        Check(RequireAction(owned, ScreenCommand.ReturnToDesigner).Enabled,
            "building: owned sessions must offer return to designer");

        var borrowed = ScreenComposer.Compose(BuildingSample(DesignQuoteSample()),
            Context(editing: true, owned: false));
        Check(FindAction(borrowed, ScreenCommand.ReturnToDesigner) is null,
            "building: non-owned sessions must not offer return to designer");
    }

    private static void ValidOffersDisplay()
    {
        var model = ScreenComposer.Compose(OffersSample(ValidOffers()), Context(owned: true));
        Check(model.Cards.Count == 3, "offers: exactly three offer cards are required");
        Text(model, "Round: 3");
        Text(model, "Available budget: 20 credits");
        Text(model, "improve your design");
        Text(model, "Play");

        var choices = AllActions(model)
            .Where(action => action.Command == ScreenCommand.ChooseOffer)
            .ToList();
        Check(choices.Count == 3, "offers: each valid offer must have one choose action");
        Check(choices.All(action => action.Label == "CHOOSE & BUILD"),
            "offers: owned sessions must label the action CHOOSE & BUILD");

        var ids = choices.Select(action => action.Argument ?? string.Empty)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Check(ids.SequenceEqual(new[] { "offer-a", "offer-b", "offer-c" }),
            "offers: choose actions must carry the exact distinct offer ids");
        Check(choices.All(action => action.Enabled),
            "offers: choose actions must be enabled when not busy");

        var cautious = CardFor(model, "offer-a");
        Check(cautious.Lines.Contains("Risk: Cautious"), "offers: card must show its risk");
        Check(cautious.Lines.Contains("Spawn layout: layout-delta"),
            "offers: card must show the spawn layout");
        Check(cautious.Lines.Contains("Enemy total: 5"),
            "offers: card must show the supplied composition total");
        Check(cautious.Lines.Any(line => line.Contains("Goblin x3", StringComparison.Ordinal)),
            "offers: card must show enemy composition id and count");
        Check(cautious.Lines.Any(line => line.Contains("Wolf x2", StringComparison.Ordinal)),
            "offers: card must show every enemy composition entry");
        Check(cautious.Lines.Contains("Supply reward: 4"),
            "offers: card must show the supply reward");

        var borrowed = ScreenComposer.Compose(OffersSample(ValidOffers()), Context(owned: false));
        Check(AllActions(borrowed)
                .Where(action => action.Command == ScreenCommand.ChooseOffer)
                .All(action => action.Label == "CHOOSE OFFER"),
            "offers: non-owned sessions use the plain choose label");
        Text(borrowed, "Planned map: fixture-map");
        NoText(borrowed, "Actual map:");
    }

    private static void OwnedOffersUseActualMap()
    {
        var model = ScreenComposer.Compose(OffersSample(ValidOffers()),
            Context(owned: true, actualMap: "native-arena-07"));
        Text(model, "Actual map: native-arena-07");
        NoText(model, "fixture-map");
    }

    private static void MalformedOffers()
    {
        var cases = new (string Name, EncounterPlan[] Offers)[]
        {
            ("two offers", new[] { Plan("offer-a", EncounterRisk.Cautious), Plan("offer-b", EncounterRisk.Standard) }),
            ("duplicate ids", new[]
            {
                Plan("same", EncounterRisk.Cautious),
                Plan("same", EncounterRisk.Standard),
                Plan("offer-c", EncounterRisk.Dangerous)
            }),
            ("empty id", new[]
            {
                Plan("", EncounterRisk.Cautious),
                Plan("offer-b", EncounterRisk.Standard),
                Plan("offer-c", EncounterRisk.Dangerous)
            }),
            ("whitespace id", new[]
            {
                Plan("   ", EncounterRisk.Cautious),
                Plan("offer-b", EncounterRisk.Standard),
                Plan("offer-c", EncounterRisk.Dangerous)
            }),
            ("duplicate risks", new[]
            {
                Plan("offer-a", EncounterRisk.Cautious),
                Plan("offer-b", EncounterRisk.Cautious),
                Plan("offer-c", EncounterRisk.Dangerous)
            }),
            ("invalid risk", new[]
            {
                Plan("offer-a", (EncounterRisk)99),
                Plan("offer-b", EncounterRisk.Standard),
                Plan("offer-c", EncounterRisk.Dangerous)
            })
        };

        foreach (var (name, offers) in cases)
        {
            var model = ScreenComposer.Compose(OffersSample(offers), Context(owned: true));
            Check(!AllActions(model).Any(action => action.Command == ScreenCommand.ChooseOffer),
                $"offers ({name}): malformed data must expose no choose actions");
            Check(model.Cards.Count == 0,
                $"offers ({name}): malformed data must expose no offer cards");
            Text(model, "could not be loaded safely");
            Check(FindAction(model, ScreenCommand.Slots) is { Enabled: true },
                $"offers ({name}): slots must remain available");
        }
    }

    private static void SelectedOfferMarker()
    {
        var model = ScreenComposer.Compose(
            OffersSample(ValidOffers(), selected: "offer-b"), Context(owned: true));
        var selected = CardFor(model, "offer-b");
        Check(selected.Lines.Any(line => line.Equals("Selected", StringComparison.OrdinalIgnoreCase)),
            "offers: the selected offer card must show a selected marker");
        Check(!CardFor(model, "offer-a").Lines.Any(line =>
                line.Contains("Selected", StringComparison.OrdinalIgnoreCase)),
            "offers: unselected cards must not claim to be selected");

        var noSelection = ScreenComposer.Compose(OffersSample(ValidOffers()), Context(owned: true));
        NoText(noSelection, "Selected");
    }

    private static void EncounterMapping()
    {
        var model = ScreenComposer.Compose(
            EncounterSample(7, 13, combatStarted: false), Context());
        Text(model, "Round: 3");
        Text(model, "Era: era-native");
        Text(model, "Friendly units: 7");
        Text(model, "Enemy units: 13");
        Text(model, "Loading encounter");
        Check(AllActions(model).All(action =>
                action.Command != ScreenCommand.Retry &&
                action.Command != ScreenCommand.Continue),
            "encounter: live retry and continue must never be exposed");
        NoText(model, "Enemy total:");

        var combat = ScreenComposer.Compose(
            EncounterSample(7, 13, combatStarted: true), Context());
        Text(combat, "Combat in progress");
        Check(AllActions(combat).All(action =>
                action.Command != ScreenCommand.Retry &&
                action.Command != ScreenCommand.Continue),
            "encounter: combat state must not expose retry or continue");
    }

    private static void ResultOutcomes()
    {
        var victory = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.Victory), Context());
        Check(victory.Title == "Victory", "result: victory must be explicit");
        Text(victory, "Outcome: Victory");
        Text(victory, "Pending supply points: 7");
        Text(victory, "Next budget: 30 credits");
        Text(victory, "Next phase: Building");
        Check(FindAction(victory, ScreenCommand.Continue) is { Enabled: true },
            "result: a nonterminal victory must expose continue");
        Check(FindAction(victory, ScreenCommand.Retry) is null,
            "result: retry must not appear when context disallows it");

        var destroyed = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.FriendlyForceDestroyed, loss: true), Context());
        Check(destroyed.Title == "Defeat", "result: destroyed force must be a defeat");
        Text(destroyed, "Friendly force destroyed");
        Text(destroyed, "records a loss");
        Check(FindAction(destroyed, ScreenCommand.Continue) is null,
            "result: defeat must not expose continue");

        var retreat = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.CombatRetreat, loss: true), Context());
        Check(retreat.Title == "Defeat", "result: combat retreat must be a defeat");
        Text(retreat, "Combat retreat");
        Text(retreat, "retreated");
        Check(FindAction(retreat, ScreenCommand.Continue) is null,
            "result: retreat must not expose continue");

        var aborted = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.PrecombatAborted), Context());
        Check(aborted.Title == "Encounter aborted", "result: precombat abort must be distinct");
        Text(aborted, "Precombat aborted");
        Text(aborted, "No combat loss was recorded");
        Text(aborted, "No supply reward was granted");
        Check(FindAction(aborted, ScreenCommand.Continue) is null,
            "result: precombat abort must not expose continue");

        var technical = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.TechnicalFailure), Context());
        Check(technical.Title == "Technical failure", "result: technical failure must be distinct");
        Text(technical, "Technical failure");
        Text(technical, "No combat loss was recorded");
        Text(technical, "No supply reward was granted");
        Check(FindAction(technical, ScreenCommand.Continue) is null,
            "result: technical failure must not expose continue");

        var noBudget = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.Victory, noBudget: true), Context());
        Text(noBudget, "Next budget: Not available");
    }

    private static void ResultActionGates()
    {
        var terminal = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.Victory, terminal: true), Context());
        Check(FindAction(terminal, ScreenCommand.Continue) is null,
            "result: terminal victory must not expose continue");
        Text(terminal, "This run is complete");

        var retryAllowed = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.TechnicalFailure), Context(canRetry: true));
        Check(RequireAction(retryAllowed, ScreenCommand.Retry).Enabled,
            "result: retry must be enabled when context permits it");

        var abortedRetry = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.PrecombatAborted), Context(canRetry: true));
        Check(RequireAction(abortedRetry, ScreenCommand.Retry).Enabled,
            "result: an aborted preparation must offer RETRY when the context permits it");

        var retryBlocked = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.TechnicalFailure), Context(canRetry: false));
        Check(FindAction(retryBlocked, ScreenCommand.Retry) is null,
            "result: retry must be absent when context forbids it");
        foreach (var outcome in new[] { EncounterOutcome.Victory,
                     EncounterOutcome.FriendlyForceDestroyed, EncounterOutcome.CombatRetreat })
        {
            var terminalWithStaleRetry = ScreenComposer.Compose(
                ResultSample(outcome, terminal: true), Context(canRetry: true));
            Check(FindAction(terminalWithStaleRetry, ScreenCommand.Retry) is null,
                "terminal result must reject stale retry availability");
        }
    }

    private static void FinishedRun()
    {
        var complete = ScreenComposer.Compose(
            new RunFinishedView(4, 2, RunPhase.Finished, false), Context());
        Check(complete.Title == "Expedition complete", "finished: victory title must be explicit");
        Text(complete, "Result: Victory");
        Text(complete, "Victories: 4");
        Text(complete, "Defeats: 2");
        Check(FindAction(complete, ScreenCommand.Resume) is null,
            "finished: no resume action is allowed");
        Check(FindAction(complete, ScreenCommand.Retry) is null,
            "finished: no retry action is allowed");
        Check(FindAction(complete, ScreenCommand.Continue) is null,
            "finished: no continue action is allowed");

        var lost = ScreenComposer.Compose(
            new RunFinishedView(1, 3, RunPhase.Lost, true), Context());
        Check(lost.Title == "Expedition lost", "finished: loss title must be explicit");
        Text(lost, "Result: Loss");
        Check(FindAction(lost, ScreenCommand.Retry) is null,
            "finished: a lost run must not offer retry");
    }

    private static void EndlessChoice()
    {
        var model = ScreenComposer.Compose(
            new EndlessChoiceView(6, Money(25), 9), Context());
        Text(model, "Completed rounds: 6");
        Text(model, "Next budget: 25 credits");
        Text(model, "Pending supply points: 9");
        Check(RequireAction(model, ScreenCommand.ContinueEndless).Enabled,
            "endless: continue endless must be enabled");
        Check(RequireAction(model, ScreenCommand.Finish).Enabled,
            "endless: finish must be enabled");
        Check(FindAction(model, ScreenCommand.Retry) is null,
            "endless: retry must not be offered");
    }

    private static void BlockedScreen()
    {
        var model = ScreenComposer.Compose(
            new BlockedView("Save data belongs to another era.", true), Context());
        Text(model, "Save data belongs to another era");
        Text(model, "preserved");
        Check(AllActions(model).All(action =>
                action.Command == ScreenCommand.Slots ||
                action.Command == ScreenCommand.ReturnToMenu),
            "blocked: only navigation actions are allowed");
        Check(FindAction(model, ScreenCommand.Retry) is null,
            "blocked: no retry action is allowed");
        Check(FindAction(model, ScreenCommand.Continue) is null,
            "blocked: no pretend recovery action is allowed");
        Check(FindAction(model, ScreenCommand.NewGame) is null,
            "blocked: new game must not be offered as recovery");
    }

    private static void ErrorRetry()
    {
        var allowed = ScreenComposer.Compose(
            new ErrorView("Catalogue unavailable.", true), Context(canRetry: true));
        Check(allowed.Title == "Unexpected error",
            "error: the default title must stay generic");
        Text(allowed, "Catalogue unavailable");
        Check(RequireAction(allowed, ScreenCommand.Retry).Enabled,
            "error: retry must be enabled when both permissions are true");

        var viewBlocked = ScreenComposer.Compose(
            new ErrorView("Catalogue unavailable.", false), Context(canRetry: true));
        Check(FindAction(viewBlocked, ScreenCommand.Retry) is null,
            "error: retry must be absent when the view forbids it");

        var contextBlocked = ScreenComposer.Compose(
            new ErrorView("Catalogue unavailable.", true), Context(canRetry: false));
        Check(FindAction(contextBlocked, ScreenCommand.Retry) is null,
            "error: retry must be absent when the context forbids it");

        var slotsFull = ScreenComposer.Compose(
            new ErrorView(
                "All four save slots are occupied. Open SLOTS and use DELETE / ARCHIVE "
                + "to free one, or resume an existing expedition.", false)
            { Title = "Save slots full" },
            Context(canRetry: true));
        Check(slotsFull.Title == "Save slots full",
            "error: a custom title must replace the generic title");
        Text(slotsFull, "All four save slots are occupied");
        Text(slotsFull, "DELETE / ARCHIVE");
        Text(slotsFull, "resume an existing expedition");
        Check(RequireAction(slotsFull, ScreenCommand.Slots).Enabled,
            "error: full slots must keep the SLOTS action available");
        Check(FindAction(slotsFull, ScreenCommand.Retry) is null,
            "error: full slots must not offer retry even when the context permits it");

        var fallback = ScreenComposer.Compose(
            new ErrorView("   ", false), Context(canRetry: false));
        Check(fallback.Title == "Unexpected error",
            "error: a blank message must keep the generic title");
        Text(fallback, "unexpected error");
        Check(FindAction(fallback, ScreenCommand.Slots) is not null,
            "error: slots must remain available");
        Check(FindAction(fallback, ScreenCommand.ReturnToMenu) is not null,
            "error: return to menu must remain available");

        var blankTitle = ScreenComposer.Compose(
            new ErrorView("Catalogue unavailable.", false) { Title = "   " },
            Context(canRetry: false));
        Check(blankTitle.Title == "Unexpected error",
            "error: a blank title must fall back to the generic title");
        var nullTitle = ScreenComposer.Compose(
            new ErrorView("Catalogue unavailable.", false) { Title = null },
            Context(canRetry: false));
        Check(nullTitle.Title == "Unexpected error",
            "error: a null title must fall back to the generic title");
    }

    private static void UnknownView()
    {
        foreach (var view in new object?[] { new object(), null })
        {
            var model = ScreenComposer.Compose(view!, Context());
            Text(model, "not available");
            Check(AllActions(model).All(action =>
                    action.Command == ScreenCommand.Slots ||
                    action.Command == ScreenCommand.ReturnToMenu),
                "unknown: only safe navigation actions are allowed");
            NoText(model, "CHOOSE");
            NoText(model, "CONFIRM");
        }
    }

    private static void BusyDisablesEverything()
    {
        var busy = Context(busy: true, editing: true, owned: true, canRetry: true);
        var models = new ScreenModel[]
        {
            ScreenComposer.Compose(StartViewSample(false, false, 2), busy),
            ScreenComposer.Compose(StartViewSample(true, true, 2), busy),
            ScreenComposer.Compose(BuildingSample(DesignQuoteSample()), busy),
            ScreenComposer.Compose(OffersSample(ValidOffers(), "offer-a"), busy),
            ScreenComposer.Compose(EncounterSample(4, 6, false), busy),
            ScreenComposer.Compose(ResultSample(EncounterOutcome.Victory), busy),
            ScreenComposer.Compose(new RunFinishedView(3, 1, RunPhase.Finished, false), busy),
            ScreenComposer.Compose(new EndlessChoiceView(4, Money(25), 9), busy),
            ScreenComposer.Compose(new BlockedView("The save is unavailable.", true), busy),
            ScreenComposer.Compose(new ErrorView("Catalogue unavailable.", true), busy),
            ScreenComposer.Compose(new object(), busy)
        };

        foreach (var model in models)
        {
            var actions = AllActions(model).ToList();
            Check(actions.Count > 0, $"busy: '{model.Title}' must still describe its actions");
            Check(actions.All(action => !action.Enabled),
                $"busy: '{model.Title}' left an action enabled: " +
                string.Join(", ", actions.Where(action => action.Enabled)
                    .Select(action => action.Command.ToString())));
        }

        var offerModel = models[3];
        Check(offerModel.Cards.Select(card => card.Action).All(action =>
                action is { Enabled: false }),
            "busy: offer card actions must also be disabled");
    }

    private static void SuppliedCounts()
    {
        var building = ScreenComposer.Compose(
            BuildingSample(DesignQuoteSample(cost: 10m), friendly: 2, budget: 20m),
            Context(editing: true));
        Text(building, "Budget: 20 credits");
        Text(building, "Quote cost: 10 credits");
        Text(building, "Friendly units: 2");
        NoText(building, "Friendly units: 4");
        NoText(building, "Friendly units: 0");

        var encounter = ScreenComposer.Compose(
            EncounterSample(7, 13, false), Context());
        Text(encounter, "Friendly units: 7");
        Text(encounter, "Enemy units: 13");
        NoText(encounter, "Friendly units: 5");
        NoText(encounter, "Enemy units: 5");

        var result = ScreenComposer.Compose(
            ResultSample(EncounterOutcome.Victory, pending: 7), Context());
        Text(result, "Pending supply points: 7");
        NoText(result, "Pending supply points: 4");

        var offers = ScreenComposer.Compose(OffersSample(ValidOffers()), Context(owned: true));
        Text(offers, "Enemy total: 5");
        Text(offers, "Supply reward: 4");
    }

    private static void CheckNoNullCollections(ScreenModel model, string name)
    {
        Check(model.Lines is not null, $"{name}: lines must not be null");
        Check(model.Cards is not null, $"{name}: cards must not be null");
        Check(model.Actions is not null, $"{name}: actions must not be null");
        Check(model.Cards!.All(card => card.Lines is not null),
            $"{name}: card lines must not be null");
    }


    // ---- Visual menu helpers (2026-10-31) ----

    private static OfferLookups Lookups() => new(
        id => id == "Leopard" ? "Leopard 2A7" : id,
        id => id switch { "Leopard" => 100m, "Light" => 40m, "Goblin" => 50m, "Wolf" => 25m, _ => 10m },
        id => id + " (Coldwar, 1 each)");

    private static EncounterPlan FormationOffer(out SpecialBattle special)
    {
        var reward = new RewardItem("r1", RewardKind.SalvageRights, RewardRarity.Rare, .01m, 2, 2);
        special = new SpecialBattle(SpecialPreset.TacticalGroup, "Leopard battlegroup · recon flanks",
            SpecialSpawnPattern.CombinedArms,
            new[]
            {
                new SpecialUnit("Light", 2) { DeploymentRole = "Flank" },
                new SpecialUnit("Leopard", 4) { DeploymentRole = "Center" }
            }, "", 0m, 0);
        return Plan("offer-f", EncounterRisk.Standard, "Defence") with
        {
            Enemies = new[] { new EnemyUnit("Leopard", 4), new EnemyUnit("Light", 2) },
            Extras = new EncounterExtras(4, 0, reward) { Special = special, EnemyBudgetTarget = 500m }
        };
    }

    private static void NumberFormatting()
    {
        Check(MenuText.Number(287942m) == "287 942", "grouped with a space: " + MenuText.Number(287942m));
        Check(MenuText.Number(0m) == "0", "zero");
        Check(MenuText.Number(1234567.6m) == "1 234 568", "rounded: " + MenuText.Number(1234567.6m));
        Check(MenuText.Number(950) == "950", "no separator under 1000");
        Check(MenuText.JoinFacts("Round 5", "", null, "Defence") == "Round 5 · Defence", "blank parts are skipped");
    }

    private static void OffersHeaderFacts()
    {
        var model = ScreenComposer.Compose(OffersSample(ValidOffers()), Context(owned: true));
        Check(model.Facts is { Count: 3 }, "round, era and budget become three facts");
        Check(model.Facts![0].Text == "Round 3" && model.Facts[1].Text == "Era era-native"
            && model.Facts[2].Text == "Budget 20" && model.Facts[2].Emphasis, "fact texts");
        Check(model.Facts[2].Tooltip == "Available budget: 20 credits", "budget unit stays in the tooltip");
        Check(model.Hint is { } hint && hint.Contains("Review each risk", StringComparison.Ordinal)
            && hint.Contains("then press Play", StringComparison.Ordinal) && !hint.Contains('\n'),
            "the two instruction lines become one line");
        Text(model, "Round: 3");
        var boss = Plan("boss", EncounterRisk.Standard) with { Round = 4, Extras = new EncounterExtras(3, 0,
            new RewardItem("b", RewardKind.CapturedPrototype, RewardRarity.UltraRare, 1m, 6, 6, "boss"),
            "boss", "next", "The Fortress") { CuratedBoss = true } };
        var curated = ScreenComposer.Compose(new OffersView(4, boss.Era, Money(100), new[] { boss }, boss.OfferId),
            Context(owned: true));
        Check(curated.Hint!.Contains("curated boss", StringComparison.Ordinal), "curated boss hint");
    }

    private static void CompositionLines()
    {
        var plain = OfferVisuals.Composition(Plan("p", EncounterRisk.Cautious), Lookups().Name);
        Check(plain.Count == 2 && plain[0].Text == "3× Goblin" && plain[0].Role == null, "plain offer: one line per design");

        var formation = FormationOffer(out _);
        var lines = OfferVisuals.Composition(formation, Lookups().Name);
        Check(lines.Count == 2, "formation: one line per design and role");
        Check(lines[0].Text == "4× Leopard 2A7" && lines[0].Role == "centre", "centre first, readable name");
        Check(lines[1].Text == "2× Light" && lines[1].Role == "flanks", "flanks second");

        var mismatch = formation with { Enemies = new[] { new EnemyUnit("Leopard", 9) } };
        var fallback = OfferVisuals.Composition(mismatch, Lookups().Name);
        Check(fallback.Count == 1 && fallback[0].Text == "9× Leopard 2A7" && fallback[0].Role == null,
            "a formation that does not match the offer falls back to the offer list");

        var unrolled = formation with { Extras = formation.Extras! with { Special = new SpecialBattle(
            SpecialPreset.RatPlague, "Rats", SpecialSpawnPattern.Swarm,
            new[] { new SpecialUnit("Light", 6) }, "", 0m, 0) } };
        Check(OfferVisuals.Composition(unrolled, Lookups().Name).All(line => line.Role == null),
            "no roles recorded: no role suffix");
    }

    private static void RewardBlocks()
    {
        var salvage = new RewardItem("s", RewardKind.SalvageRights, RewardRarity.Common, .01m, 2, 2);
        var block = OfferVisuals.Reward(salvage, boss: false);
        Check(block.Name == "Salvage Rights", "reward name");
        Check(block.Effect == "+1% of base budget per defeated enemy (up to 12) · 2 victories", "reward effect: " + block.Effect);
        Check(block.Extra == null && block.RarityText == "COMMON", "no extra rule outside boss cards");
        var bonds = OfferVisuals.Reward(new RewardItem("w", RewardKind.WarBonds, RewardRarity.Rare, .3m, 2, 2), boss: true);
        Check(bonds.Effect.Contains("pays +30% budget when it expires (delayed)", StringComparison.Ordinal)
            && bonds.Effect.EndsWith("cash now or invest for 2 victories", StringComparison.Ordinal),
            "extra rule and timing of war bonds are kept: " + bonds.Effect);
        Check(bonds.Extra!.StartsWith("Choose 1 of 3 purple items", StringComparison.Ordinal), "boss card keeps the choose-1-of-3 rule");
        var ultra = OfferVisuals.Reward(new RewardItem("u", RewardKind.CapturedPrototype, RewardRarity.UltraRare, 1m, 1, 1, "bp"), false);
        Check(ultra.RarityText == "ULTRA RARE" && ultra.Effect == "1 free ally · 1 victory", "ultra rare text and singular victory: " + ultra.Effect);

        foreach (var kind in Enum.GetValues<RewardKind>())
        {
            var item = new RewardItem("k", kind, RewardRarity.Rare, .25m, 3, 2, "bp");
            foreach (var active in new[] { false, true })
            {
                var parts = RewardRules.DescribeParts(item, active);
                var text = $"{parts.Name} {parts.Value} ({parts.RarityName}, {parts.Timing})"
                    + (parts.Note.Length == 0 ? "" : "; " + parts.Note);
                Check(text == RewardRules.Describe(item, active), "Describe is composed from DescribeParts: " + kind);
            }
        }
    }

    private static void CostBars()
    {
        var offer = Plan("c", EncounterRisk.Standard);   // 3 Goblin (150) + 2 Wolf (50) = 200
        var model = OfferVisuals.Build(offer, "Offer", 400m, Lookups());
        Check(model.Cost is { Enemy: 200m, Over: false } c && Math.Abs(c.Fraction - .5f) < .001f, "half of the budget");
        Check(model.Cost!.Label.StartsWith("Enemy 200 · your budget 400") && model.Cost.Label.Contains("Enemy force: 0.5× your budget"), "label: " + model.Cost.Label);
        var over = OfferVisuals.Build(offer, "Offer", 100m, Lookups());
        Check(over.Cost is { Over: true, Fraction: 1f }, "over budget clamps to 1 and is flagged");
        var none = OfferVisuals.Build(offer, "Offer", 0m, Lookups());
        Check(none.Cost is { Over: true, Fraction: 1f }, "zero budget with enemies counts as over");
        Check(OfferVisuals.Build(offer, "Offer", null, Lookups()).Cost == null, "no budget, no bar");
        Check(model.EnemyCount == 5 && model.Supply == 4 && model.MapId == "fixture-map", "tiles carry count, supply and map");
    }

    private static void OfferCardModelKeepsInformation()
    {
        var formation = FormationOffer(out var special);
        var model = OfferVisuals.Build(formation, "Offer 1 - Standard risk", 800m, Lookups());
        Check(model.Title == "Leopard battlegroup · recon flanks", "title is only the theme");
        Check(model.Kinds.SequenceEqual(new[] { "FORMATION" }) && model.RarityText == "RARE" && model.Rarity == RewardRarity.Rare,
            "kind and rarity become chips");
        Check(model.Reward is { Name: "Salvage Rights" }, "reward block");
        var details = string.Join("\n", model.Details);
        foreach (var expected in new[] { "Era: era-native · map: Defence", "Risk: Standard", "Spawn layout: layout-delta",
                     "FORMATION · Leopard battlegroup · recon flanks · CombinedArms formation", "Intent: ",
                     "Target enemy cost: <=500 · actual 480", "Supply: 4 for next era · win funds +5% base",
                     "4 × Leopard (Coldwar, 1 each)", "2 × Light (Coldwar, 1 each)" })
            Check(details.Contains(expected, StringComparison.Ordinal), "details keep: " + expected + "\n" + details);

        var boss = Plan("b", EncounterRisk.Standard, "Fields") with { Extras = new EncounterExtras(3, 0,
            new RewardItem("b", RewardKind.CapturedPrototype, RewardRarity.UltraRare, 1m, 6, 6, "boss"),
            "boss", "Coldwar2", "The Fortress", PlaceholderBoss: true) { CuratedBoss = true } };
        var bossModel = OfferVisuals.Build(boss, "Offer", 100m, Lookups());
        Check(bossModel.Title == "The Fortress" && bossModel.Kinds.SequenceEqual(new[] { "BOSS" }), "curated boss title and chip");
        Check(bossModel.Tagline == "Dug in — you must attack", "Fortress tagline sits under the boss name");
        var bossDetails = string.Join("\n", bossModel.Details);
        Check(bossDetails.Contains("BOSS — The Fortress · Coldwar2 (placeholder)", StringComparison.Ordinal), "boss line kept");
        Check(!bossDetails.Contains("Risk:", StringComparison.Ordinal), "curated boss has no risk line");
        var bossWithEscort = boss with
        {
            Extras = boss.Extras! with
            {
                Special = new SpecialBattle(SpecialPreset.TacticalGroup, "The Fortress dug in",
                    SpecialSpawnPattern.Defensive,
                    new[] { new SpecialUnit("boss", 1) { DeploymentRole = "Center" } },
                    string.Empty, 0m, 0)
            }
        };
        var escortedModel = OfferVisuals.Build(bossWithEscort, "Offer", 100m, Lookups());
        Check(escortedModel.Title == "The Fortress" && escortedModel.Kinds.SequenceEqual(new[] { "BOSS" }),
            "a boss escort formation never replaces the boss card title or chip");
        var hunter = boss with
        {
            Extras = boss.Extras! with { BossCharacter = "The Hunter" }
        };
        Check(OfferVisuals.Build(hunter, "Offer", 100m, Lookups()).Tagline == "Hunts you with flankers",
            "Hunter tagline");
        Check(OfferVisuals.BossTagline("The Battery") == "Long-range battery"
            && OfferVisuals.BossTagline("The Veteran") == null,
            "Battery tagline and unchanged Veteran card");

        var plain = OfferVisuals.Build(Plan("n", EncounterRisk.Cautious), "Offer 1 - Cautious risk", 400m, Lookups());
        Check(plain.Title == "Offer 1 - Cautious risk" && plain.Kinds.Count == 0 && plain.Reward == null && plain.RarityText == null,
            "plain offer: title kept, no chips, no reward");
        Check(special.Enemies.Count == 2, "fixture sanity");
    }

    private static void MapThumbnailFiles()
    {
        var files = MapThumbnailRules.Candidates(@"C:\Splash", "Defence");
        Check(files.Count == 2 && files[0].EndsWith("Defence_CustomBattle.png", StringComparison.Ordinal)
            && files[1].EndsWith("Defence_Scenario.png", StringComparison.Ordinal), "custom battle first, scenario as fallback");
        foreach (var bad in new[] { "", "..\\x", "a/b", "C:\\x", "x.png", new string('a', 80) })
            Check(MapThumbnailRules.Candidates(@"C:\Splash", bad).Count == 0, "rejected map id: " + bad);
        var wide = MapThumbnailRules.CoverUv(1920, 1080, 490, 150);
        Check(wide.X == 0 && wide.W == 1 && Math.Abs(wide.H - (1920f / 1080f) / (490f / 150f)) < .0001f
            && Math.Abs(wide.Y - (1 - wide.H) / 2) < .0001f, "wide box crops top and bottom, centred");
        var tall = MapThumbnailRules.CoverUv(1920, 1080, 100, 200);
        Check(tall.Y == 0 && tall.H == 1 && tall.W < 1 && Math.Abs(tall.X - (1 - tall.W) / 2) < .0001f, "tall box crops the sides");
        Check(MapThumbnailRules.CoverUv(0, 0, 10, 10) == (0, 0, 1, 1), "degenerate input shows the whole picture");
    }

    private static void BattleStatsScreen()
    {
        var tanks = new List<BattleTankStats>();
        for (var index = 0; index < 8; index++)
            tanks.Add(new BattleTankStats("Tank " + index, index == 0, index < 3,
                10, 4, 2, 2, 1, index == 0 ? 1200m : null,
                index == 0 ? "T-34" : null, index == 0 ? 650m : null));
        var report = new BattleStatsReport("key", "FriendlyForceDestroyed", 192d, tanks);
        var loss = ResultSample(EncounterOutcome.FriendlyForceDestroyed) with { CanRetryLoss = true };
        var screen = ScreenComposer.Compose(loss, Context(), report);
        Check(screen.Blocks.Count == 2, "a defeat with measured stats shows two blocks");
        Check(screen.Blocks[0].Title == "Losses" && screen.Blocks[1].Title == "Battle stats",
            "block titles are Losses and Battle stats");
        Check(screen.Blocks[0].Lines.Count == 7 && screen.Blocks[0].Lines[^1] == "+2 more",
            "losses stop after six tank rows and add +n more");
        Check(screen.Blocks[0].Lines[0] == "Tank 0 — destroyed by T-34 at 650 m", "losses carry destroyed-by");
        Check(screen.Blocks[1].Lines.Count == 3
            && screen.Blocks[1].Lines[0].StartsWith("You — Tank 0:", StringComparison.Ordinal)
            && screen.Blocks[1].Lines[1].StartsWith("Team:", StringComparison.Ordinal)
            && screen.Blocks[1].Lines[2].StartsWith("Longest hit 1200 m", StringComparison.Ordinal),
            "player line, one team-total line and the measured extra");
        Check(screen.Result is not null && screen.Result.Headline == "Defeat", "existing defeat banner stays");
        Check(ScreenComposer.Compose(ResultSample(EncounterOutcome.Victory), Context(), report).Blocks.Count == 0,
            "victory keeps its exact screen without the new blocks");
        Check(ScreenComposer.Compose(ResultSample(EncounterOutcome.PrecombatAborted), Context(), report).Blocks.Count == 0,
            "an aborted encounter never shows battle blocks");
        Check(ScreenComposer.Compose(loss, Context()).Blocks.Count == 0,
            "without a report the defeat screen is unchanged");
    }

    private static void ResultSummaries()
    {
        var victory = ScreenComposer.Compose(ResultSample(EncounterOutcome.Victory), Context()).Result;
        Check(victory is { Tone: ResultTone.Victory, Headline: "Victory", OutcomeText: null, Message: null }, "victory banner");
        Check(victory!.Subline == "Round 3 · era-native · fixture-map", "subline: " + victory.Subline);
        Check(victory.Tiles.Select(t => (t.Caption, t.Value)).SequenceEqual(new[] { ("Next budget", "30"), ("Pending supply", "7") }),
            "tiles carry next budget and pending supply");
        var details = string.Join("\n", victory.Details);
        Check(details.Contains("Detail: Encounter cleared.", StringComparison.Ordinal)
            && details.Contains("The encounter was won.", StringComparison.Ordinal)
            && details.Contains("Next phase: Building", StringComparison.Ordinal), "victory details: " + details);

        var defeat = ScreenComposer.Compose(ResultSample(EncounterOutcome.FriendlyForceDestroyed, loss: true), Context()).Result!;
        Check(defeat.Tone == ResultTone.Defeat && defeat.Headline == "Defeat" && defeat.OutcomeText == "Friendly force destroyed"
            && defeat.Message!.Contains("records a loss", StringComparison.Ordinal), "defeat banner with outcome chip and message");

        var aborted = ScreenComposer.Compose(ResultSample(EncounterOutcome.PrecombatAborted), Context()).Result!;
        Check(aborted.Tone == ResultTone.Neutral && aborted.Message!.Contains("No supply reward was granted", StringComparison.Ordinal),
            "abort is neutral and keeps its message");
        var technical = ScreenComposer.Compose(ResultSample(EncounterOutcome.TechnicalFailure), Context()).Result!;
        Check(technical.Message!.Contains("The run has been preserved.", StringComparison.Ordinal), "technical failure message");

        var noBudget = ScreenComposer.Compose(ResultSample(EncounterOutcome.Victory, noBudget: true), Context()).Result!;
        Check(noBudget.Tiles.Count == 1 && noBudget.Tiles[0].Caption == "Pending supply", "a missing budget gives no invented tile");
        var terminal = ScreenComposer.Compose(ResultSample(EncounterOutcome.Victory, terminal: true), Context()).Result!;
        Check(terminal.Details.Contains("This run is complete."), "terminal note kept");
        var screen = ScreenComposer.Compose(ResultSample(EncounterOutcome.Victory), Context());
        Text(screen, "Pending supply points: 7");
        Check(ScreenComposer.Compose(OffersSample(ValidOffers()), Context()).Result == null, "only results carry a banner");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool HasText(ScreenModel model, string fragment)
    {
        if (model.Title.Contains(fragment, StringComparison.OrdinalIgnoreCase) ||
            model.Lines.Any(line => line.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return model.Cards.Any(card =>
            card.Title.Contains(fragment, StringComparison.OrdinalIgnoreCase) ||
            card.Lines.Any(line => line.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    private static void Text(ScreenModel model, string fragment)
    {
        Check(HasText(model, fragment),
            $"expected text '{fragment}' in screen '{model.Title}'{Environment.NewLine}{Describe(model)}");
    }

    private static void NoText(ScreenModel model, string fragment)
    {
        Check(!HasText(model, fragment),
            $"unexpected text '{fragment}' in screen '{model.Title}'{Environment.NewLine}{Describe(model)}");
    }

    private static string Describe(ScreenModel model)
    {
        return "  lines: " + string.Join(" | ", model.Lines) +
               Environment.NewLine +
               "  actions: " + string.Join(", ", AllActions(model)
                   .Select(action =>
                       $"{action.Command}:{action.Label}:{(action.Enabled ? "on" : "off")}")) +
               Environment.NewLine +
               "  cards: " + string.Join(", ", model.Cards.Select(card =>
                   $"{card.Title} [{string.Join(" / ", card.Lines)}]"));
    }

    private static IEnumerable<ScreenAction> AllActions(ScreenModel model)
    {
        foreach (var action in model.Actions)
        {
            yield return action;
        }

        foreach (var card in model.Cards)
        {
            var action = card.Action;
            if (action is not null)
            {
                yield return action;
            }
        }
    }

    private static ScreenAction? FindAction(ScreenModel model, ScreenCommand command)
    {
        return AllActions(model).FirstOrDefault(action => action.Command == command);
    }

    private static ScreenAction RequireAction(ScreenModel model, ScreenCommand command)
    {
        return FindAction(model, command) ??
               throw new InvalidOperationException(
                   $"expected action {command} in screen '{model.Title}'{Environment.NewLine}{Describe(model)}");
    }

    private static ScreenCard CardFor(ScreenModel model, string offerId)
    {
        foreach (var card in model.Cards)
        {
            var action = card.Action;
            if (action is not null &&
                string.Equals(action.Argument, offerId, StringComparison.Ordinal))
            {
                return card;
            }
        }

        throw new InvalidOperationException(
            $"expected an offer card for '{offerId}'{Environment.NewLine}{Describe(model)}");
    }

    private static void TestPoolLabels()
    {
        var offer = Plan("test-card", EncounterRisk.Standard) with { Enemies = new[] { new EnemyUnit("tank", 1) } };
        var look = new OfferLookups(_ => "Experimental Tank", _ => 999999m, _ => "Experimental Tank (future era)");
        var normal = OfferVisuals.Build(offer, "Normal title", 100m, look);
        var test = OfferVisuals.Build(offer, "Normal title", 100m, look, true);
        Check(!normal.Kinds.Contains("TEST") && test.Kinds.Contains("TEST"), "TEST chip only on test cards");
        Check(test.Title == "Experimental Tank" && test.EnemyCount == 1, "test card shows its tank name and one copy");
        var record = new RunRecordSummary("test-run", RunPhase.Retired, 4, 0, offer.Era) { TestPool = true };
        var model = ScreenComposer.Compose(new StartView(false, false, new[] { record }), Context());
        Check(model.Cards.Any(c => c.Title.Contains("TEST", StringComparison.Ordinal)), "record screen marks test run");
    }

    private static ScreenContext Context(bool busy = false, bool owned = false,
        bool editing = false, string actualMap = "native-arena", bool canRetry = false,
        int slot = 1)
    {
        return new ScreenContext(busy, owned, editing, slot, actualMap, canRetry);
    }

    private static StartView StartViewSample(bool active, bool resume, int records)
    {
        var summaries = Enumerable.Range(0, records)
            .Select(index => new RunRecordSummary(
                $"run-{index}",
                index == 0 ? RunPhase.Finished : RunPhase.Building,
                index,
                index + 1,
                new EraReference("era-native")))
            .ToArray();
        return new StartView(active, resume, summaries);
    }

    private static BuildingView BuildingSample(DesignQuote? quote, int friendly = 2,
        decimal budget = 20m, string? rejection = null)
    {
        return new BuildingView(RunPhase.Building, 2, 0, new EraReference("era-native"),
            Money(budget), quote, friendly, rejection);
    }

    private static OffersView OffersSample(EncounterPlan[] offers, string? selected = null)
    {
        return new OffersView(3, new EraReference("era-native"), Money(20m), offers, selected);
    }

    private static EncounterView EncounterSample(int friendly, int enemy, bool combatStarted)
    {
        return new EncounterView(
            new AttemptIdentity("run-1", "encounter-1", "plan-a", 1),
            Plan("offer-a", EncounterRisk.Standard),
            friendly, enemy, combatStarted);
    }

    private static ResultView ResultSample(EncounterOutcome outcome, bool terminal = false,
        bool loss = false, bool noBudget = false, int pending = 7)
    {
        var result = new EncounterResult(
            new AttemptIdentity("run-1", "encounter-1", "plan-a", 1),
            Plan("offer-a", EncounterRisk.Standard),
            outcome,
            outcome == EncounterOutcome.Victory ? "Encounter cleared." : "Reported by the host.");
        return new ResultView(result, RunPhase.Building,
            noBudget ? null : Money(30m), pending, terminal, loss);
    }

    private static ScenarioValue Money(decimal value) => new(value, "credits");

    private static DesignQuote DesignQuoteSample(decimal cost = 10m,
        bool designValid = true, bool eraValid = true, string? rejection = null)
    {
        var blueprint = new BlueprintReference(
            "bp-local", "snapshot-hash", new EraReference("era-native"),
            Array.Empty<ModRequirement>());
        return new DesignQuote(blueprint, Money(cost), designValid, eraValid, rejection);
    }

    private static EncounterPlan[] ValidOffers()
    {
        return new[]
        {
            Plan("offer-a", EncounterRisk.Cautious),
            Plan("offer-b", EncounterRisk.Standard),
            Plan("offer-c", EncounterRisk.Dangerous)
        };
    }

    private static EncounterPlan Plan(string id, EncounterRisk risk,
        string mapId = "fixture-map", int supply = 4)
    {
        return new EncounterPlan(
            id, "plan-" + id, 3, mapId, "layout-delta",
            new EraReference("era-native"), risk,
            new[] { new EnemyUnit("Goblin", 3), new EnemyUnit("Wolf", 2) },
            supply, Array.Empty<ModRequirement>(), "catalogue-1");
    }
}
