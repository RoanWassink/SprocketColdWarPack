using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Presentation;

namespace SprocketExpedition.Game.UI;

// Pure presentation mapping. This type never starts a run, opens an editor or
// launches combat; it only describes the supplied view using the frozen model.
internal static class ScreenComposer
{
    internal static ScreenModel Compose(object view, ScreenContext context, BattleStatsReport? battleStats = null)
    {
        return view switch
        {
            StartView start => ComposeStart(start, context),
            BuildingView building => ComposeBuilding(building, context),
            OffersView offers => ComposeOffers(offers, context),
            EncounterView encounter => ComposeEncounter(encounter, context),
            ResultView result => ComposeResult(result, context, battleStats),
            RunFinishedView finished => ComposeFinished(finished, context),
            EndlessChoiceView endless => ComposeEndless(endless, context),
            BlockedView blocked => ComposeBlocked(blocked, context),
            ErrorView error => ComposeError(error, context),
            _ => ComposeUnknown(context)
        };
    }

    private static ScreenModel ComposeStart(StartView view, ScreenContext context)
    {
        var actions = new List<ScreenAction>();
        if (view.CanResume)
        {
            actions.Add(Action("RESUME", ScreenCommand.Resume, context, primary: true));
        }

        if (!view.HasActiveRun)
        {
            actions.Add(Action("NEW GAME", ScreenCommand.NewGame, context,
                primary: !view.CanResume));
        }

        actions.Add(Action("SLOTS", ScreenCommand.Slots, context));

        var records = view.Records ?? Array.Empty<RunRecordSummary>();
        var cards = records.Take(4).Select(ComposeRecordCard).ToList();

        var lines = new List<string> { $"Slot: {Num(context.Slot)}" };
        if (view.HasActiveRun)
        {
            lines.Add(view.CanResume
                ? "An active expedition is ready to resume."
                : "An active expedition exists but cannot be resumed right now.");
            if (!view.CanResume)
            {
                lines.Add("Open the save slots or return to the menu; a new game is not started automatically.");
            }
        }
        else
        {
            lines.Add("Start a new expedition or open a save slot.");
        }

        if (records.Count > 4)
        {
            lines.Add($"Showing 4 of {Num(records.Count)} recent run records.");
        }

        lines.Add("Resuming never launches a battle automatically.");

        return new ScreenModel("Expedition", lines, cards, actions);
    }

    private static ScreenCard ComposeRecordCard(RunRecordSummary record)
    {
        var era = record.Era;
        var lines = new List<string>
        {
            $"Phase: {PhaseText(record.Phase)}",
            $"Record: {Num(record.Victories)} victories, {Num(record.Defeats)} defeats",
            era is null ? "Era: Not recorded" : $"Era: {era.NativeId}",
            $"Score: {MenuText.Number(record.Score)}",
            $"Ironman: {YesNo(record.Ironman)}",
            $"Outcome: {PhaseText(record.Phase)}"
        };

        return new ScreenCard($"Run {record.RunId} · {(record.TestPool ? "TEST" : record.Ironman ? "IRONMAN" : "NORMAL")}", lines);
    }

    private static ScreenModel ComposeBuilding(BuildingView view, ScreenContext context)
    {
        var quote = view.Quote;
        var rejection = FirstText(view.RejectionReason, quote?.RejectionReason);
        var hasRejection = rejection is not null;
        var quoteValid = quote is null ||
            (quote.NativeDesignValid && quote.NativeEraValid &&
             string.IsNullOrWhiteSpace(quote.RejectionReason));
        var canConfirm = context.Editing && !hasRejection && quoteValid;

        var lines = new List<string>
        {
            $"Era: {view.Era.NativeId}",
            $"Round: {Num(view.Round)}",
            $"Block: {Num(view.BlockIndex)}",
            $"Budget: {Amount(view.AvailableBudget)}",
            $"Friendly units: {Num(view.FriendlyCount)}"
        };

        if (quote is null)
        {
            lines.Add("Quote cost: Pending native capture");
            lines.Add("Design validity: Pending native capture");
        }
        else
        {
            lines.Add($"Quote cost: {Amount(quote.Cost)}");
            lines.Add($"Design valid: {YesNo(quote.NativeDesignValid)}");
            lines.Add($"Era valid: {YesNo(quote.NativeEraValid)}");
        }

        lines.Add(hasRejection ? $"Rejected: {rejection}" : "Rejection: None");

        if (!context.Editing)
        {
            lines.Add("Open the design editor to prepare the build, then confirm.");
        }
        else if (quote is null)
        {
            lines.Add("Editing is active. Confirm once the native editor has captured the design.");
        }
        else
        {
            lines.Add("Review the supplied budget, quote and friendly count before confirming.");
        }


        var actions = new List<ScreenAction>
        {
            Action("CONFIRM DESIGN", ScreenCommand.ConfirmDesign, context, canConfirm,
                primary: true)
        };

        if (context.OwnedSession)
        {
            actions.Add(Action("RETURN TO DESIGNER", ScreenCommand.ReturnToDesigner, context));
        }

        if (view.CanRetire) actions.Add(Action("RETIRE RUN", ScreenCommand.Retire, context));

        actions.Add(Action("SLOTS", ScreenCommand.Slots, context));
        actions.Add(Action("RETURN TO MENU", ScreenCommand.ReturnToMenu, context));

        return new ScreenModel("Build your expedition", lines,
            Array.Empty<ScreenCard>(), actions);
    }

    private static ScreenModel ComposeOffers(OffersView view, ScreenContext context)
    {
        if (!HasValidOffers(view.Offers))
        {
            var safeLines = new List<string>
            {
                "The encounter offers could not be loaded safely.",
                "No offer has been selected and no encounter will start.",
                "Return to the menu or choose a save slot and try again."
            };

            return new ScreenModel("Offers unavailable", safeLines,
                Array.Empty<ScreenCard>(), Navigation(context));
        }

        var lines = new List<string>
        {
            $"Round: {Num(view.Round)}",
            $"Era: {view.Era.NativeId}",
            $"Available budget: {Amount(view.AvailableBudget)}",
            "Review each risk, map, layout and enemy composition.",
            "Choose an offer, improve your design, then press Play."
        };

        var curatedBoss = view.Offers.Count == 1 && view.Offers[0].Extras?.CuratedBoss == true;
        if (curatedBoss)
        {
            lines[3] = "One curated boss battle. The opponent and battlefield are fixed.";
            lines[4] = "Refit your tank in the editor, then press Play to face the boss.";
        }
        var cards = new List<ScreenCard>();
        for (var index = 0; index < view.Offers.Count; index++)
        {
            var offer = view.Offers[index];
            var selected = string.Equals(view.SelectedOfferId, offer.OfferId,
                StringComparison.Ordinal);
            var cardLines = new List<string>();

            if (selected)
            {
                cardLines.Add("Selected");
            }

            cardLines.Add($"Risk: {RiskText(offer.Risk)}");
            cardLines.Add(context.OwnedSession
                ? $"Actual map: {SuppliedText(context.ActualMap, "Not supplied")}"
                : $"Planned map: {SuppliedText(offer.MapId, "Not supplied")}");
            cardLines.Add($"Spawn layout: {SuppliedText(offer.SpawnLayoutId, "Not supplied")}");

            var enemies = offer.Enemies ?? Array.Empty<EnemyUnit>();
            var total = enemies.Sum(enemy => enemy.Count);
            cardLines.Add($"Enemy total: {Num(total)}");
            if (view.EnemyCosts?.TryGetValue(offer.OfferId, out var actualCost) == true && view.AvailableBudget.Value > 0m)
                cardLines.Add($"Enemy force: {(actualCost / view.AvailableBudget.Value).ToString("0.0", CultureInfo.InvariantCulture)}× your budget");
            cardLines.Add(enemies.Count == 0
                ? "Composition: None"
                : "Composition: " + string.Join(", ", enemies.Select(enemy =>
                    $"{SuppliedText(enemy.BlueprintId, "Unknown")} x{Num(enemy.Count)}")));
            if (offer.Extras?.Special is { } special && offer.Extras.BossBlueprintId == null)
            {
                cardLines.Add($"Theme: {SuppliedText(special.Title, "Unnamed formation")}");
                cardLines.Add($"Formation: {SpecialBattleRules.DescribeComposition(special)}");
                cardLines.Add($"Tactical intent: {SpecialBattleRules.DescribeIntent(special)}");
            }
            if (offer.Extras?.EnemyBudgetTarget is { } enemyBudgetTarget)
                cardLines.Add("Pinned enemy target: <= "
                    + enemyBudgetTarget.ToString("0.##", CultureInfo.InvariantCulture));
            cardLines.Add($"Supply reward: {Num(offer.SupplyReward)}");

            var action = Action(
                curatedBoss ? "BUILD & FACE BOSS" : context.OwnedSession ? "CHOOSE & BUILD" : "CHOOSE OFFER",
                ScreenCommand.ChooseOffer, context, argument: offer.OfferId,
                primary: selected);

            var cardTitle = offer.Extras?.Special is { } themed && offer.Extras.BossBlueprintId == null
                ? (themed.Preset == SpecialPreset.TacticalGroup ? "Formation" : "Special")
                    + " - " + themed.Title
                : $"Offer {Num(index + 1)} - {RiskText(offer.Risk)} risk";
            cards.Add(new ScreenCard(
                cardTitle, cardLines, action));
        }

        var actions = new List<ScreenAction>
        {
            Action("SLOTS", ScreenCommand.Slots, context),
            Action("RETURN TO MENU", ScreenCommand.ReturnToMenu, context)
        };
        if (view.CanRetire) actions.Add(Action("RETIRE RUN", ScreenCommand.Retire, context));
        if (view.EraBoss is { } bossNote) lines.Add(bossNote);

        // Structured header for the Kit menu: three facts and one instruction line.
        var facts = new List<ScreenFact>
        {
            new($"Round {Num(view.Round)}"),
            new($"Era {view.Era.NativeId}"),
            new($"Budget {MenuText.Number(view.AvailableBudget.Value)}", true,
                "Available budget: " + Amount(view.AvailableBudget))
        };
        return new ScreenModel(curatedBoss ? "Boss encounter" : "Choose an encounter", lines, cards, actions)
        {
            Facts = facts,
            Hint = lines[3] + " " + lines[4] + (view.EraBoss is { } preview ? " " + preview : "")
        };
    }

    private static bool HasValidOffers(IReadOnlyList<EncounterPlan>? offers)
    {
        var curated = offers?.Count == 1 && offers[0]?.Extras is { CuratedBoss: true, RulesVersion: >= 3, BossBlueprintId: not null }
            && offers[0].Round % 4 == 0 && offers[0].Risk == EncounterRisk.Standard;
        if (offers is null || (offers.Count != 3 && !curated))
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var risks = new HashSet<EncounterRisk>();
        foreach (var offer in offers)
        {
            if (offer is null || string.IsNullOrWhiteSpace(offer.OfferId))
            {
                return false;
            }

            if (!ids.Add(offer.OfferId))
            {
                return false;
            }

            if (offer.Risk != EncounterRisk.Cautious &&
                offer.Risk != EncounterRisk.Standard &&
                offer.Risk != EncounterRisk.Dangerous)
            {
                return false;
            }

            if (!risks.Add(offer.Risk))
            {
                return false;
            }
        }

        return risks.Count == (curated ? 1 : 3);
    }

    private static ScreenModel ComposeEncounter(EncounterView view, ScreenContext context)
    {
        var state = view.CombatStarted ? "Combat in progress" : "Loading encounter";
        var lines = new List<string>
        {
            $"Round: {Num(view.Plan.Round)}",
            $"Era: {view.Plan.Era.NativeId}",
            $"Friendly units: {Num(view.FriendlyCount)}",
            $"Enemy units: {Num(view.EnemyCount)}",
            $"State: {state}",
            "Live encounter controls are unavailable on this screen.",
            "Wait for the battlefield to finish loading."
        };

        return new ScreenModel(view.CombatStarted ? "Combat in progress" : "Loading encounter",
            lines, Array.Empty<ScreenCard>(), Navigation(context));
    }

    private static ScreenModel ComposeResult(ResultView view, ScreenContext context, BattleStatsReport? battleStats)
    {
        var outcome = view.Result.Outcome;
        var victory = outcome == EncounterOutcome.Victory;
        var defeat = outcome == EncounterOutcome.FriendlyForceDestroyed ||
                     outcome == EncounterOutcome.CombatRetreat;
        var aborted = outcome == EncounterOutcome.PrecombatAborted;

        var title = victory
            ? "Victory"
            : defeat
                ? "Defeat"
                : aborted
                    ? "Encounter aborted"
                    : "Technical failure";

        var lines = new List<string>
        {
            $"Outcome: {OutcomeText(outcome)}",
            $"Round: {Num(view.Result.Plan.Round)}"
        };

        var detail = view.Result.Detail;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            lines.Add($"Detail: {detail}");
        }

        var narrative = ResultNarrative(outcome, victory, defeat, aborted);
        if (defeat && view.CanRetryLoss)
            narrative.Add($"Attempt {Num(view.RoundAttempts)} failed. Score for this battle is reduced (×0.75 per retry).");
        lines.AddRange(narrative);

        lines.Add($"Pending supply points: {Num(view.PendingSupplyPoints)}");
        lines.Add($"Next budget: {Amount(view.NextBudget)}");
        lines.Add($"Next phase: {PhaseText(view.NextPhase)}");
        if (view.IsTerminal)
        {
            lines.Add("This run is complete.");
        }

        // Structured banner for the Kit menu. Everything in Lines is either shown
        // here (round/era/map, outcome, numbers) or kept in Details.
        var plan = view.Result.Plan;
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(detail))
        {
            details.Add($"Detail: {detail}");
        }
        if (victory)
        {
            details.AddRange(narrative);
        }
        details.Add($"Next phase: {PhaseText(view.NextPhase)}");
        if (view.IsTerminal)
        {
            details.Add("This run is complete.");
        }
        var tiles = new List<ResultTile>();
        if (victory && view.ScoreGained is { } score)
        {
            tiles.Add(new ResultTile("SCORE", "+" + MenuText.Number(score)));
            if (view.ScoreBreakdown is { } breakdown) { details.Add(breakdown); lines.Add(breakdown); }
        }
        if (view.NextBudget is { } nextBudget)
        {
            tiles.Add(new ResultTile("Next budget", MenuText.Number(nextBudget.Value)));
        }
        tiles.Add(new ResultTile("Pending supply", MenuText.Number(view.PendingSupplyPoints)));
        var outcomeText = OutcomeText(outcome);
        var summary = new ResultSummary(
            victory ? ResultTone.Victory : defeat ? ResultTone.Defeat : ResultTone.Neutral,
            title,
            MenuText.JoinFacts($"Round {Num(plan.Round)}", plan.Era.NativeId, plan.MapId),
            string.Equals(outcomeText, title, StringComparison.OrdinalIgnoreCase) ? null : outcomeText,
            victory ? view.ScoreBreakdown : string.Join(" ", narrative),
            tiles, details);

        var actions = new List<ScreenAction>();
        if (context.CanRetry && !view.IsTerminal
            && outcome is EncounterOutcome.TechnicalFailure or EncounterOutcome.PrecombatAborted)
        {
            actions.Add(Action("RETRY", ScreenCommand.Retry, context));
        }
        if (defeat && view.CanRetryLoss && !view.IsTerminal)
        {
            actions.Add(Action("RETRY", ScreenCommand.Retry, context, primary: true));
            actions.Add(Action("CHOOSE ANOTHER BATTLE", ScreenCommand.ChooseAnotherBattle, context));
        }

        if (victory && !view.IsTerminal)
        {
            actions.Add(Action("CONTINUE", ScreenCommand.Continue, context, primary: true));
        }

        actions.Add(Action("SLOTS", ScreenCommand.Slots, context));
        actions.Add(Action("RETURN TO MENU", ScreenCommand.ReturnToMenu, context));

        // 0.3.6: only a fought defeat shows the two blocks. Victory, abort and
        // technical failure keep their exact previous screen. The builder is a
        // separate method so Victory can reuse it later without moving logic.
        var blocks = ComposeBattleBlocks(defeat, battleStats);

        return new ScreenModel(title, lines, Array.Empty<ScreenCard>(), actions)
        {
            Result = summary,
            Blocks = blocks
        };
    }

    private static List<ResultBlock> ComposeBattleBlocks(bool show, BattleStatsReport? battleStats)
    {
        var blocks = new List<ResultBlock>();
        if (!show || battleStats is not { } stats || stats.Tanks.Count == 0) return blocks;
        blocks.Add(new ResultBlock("Losses", BattleStatsText.LossesLines(stats.Tanks)));
        blocks.Add(new ResultBlock("Battle stats", BattleStatsText.BattleLines(stats)));
        return blocks;
    }

    private static List<string> ResultNarrative(EncounterOutcome outcome, bool victory, bool defeat, bool aborted)
    {
        if (victory)
        {
            return new List<string> { "The encounter was won." };
        }
        if (defeat)
        {
            return new List<string>
            {
                outcome == EncounterOutcome.CombatRetreat
                    ? "You retreated from live combat. The expedition records a loss."
                    : "Your friendly force was destroyed. The expedition records a loss."
            };
        }
        var none = new List<string> { "No combat loss was recorded.", "No supply reward was granted." };
        if (!aborted)
        {
            none.Add("The run has been preserved.");
        }
        return none;
    }

    private static ScreenModel ComposeFinished(RunFinishedView view, ScreenContext context)
    {
        var lines = new List<string>
        {
            $"Result: {(view.Phase == RunPhase.Retired ? "Retired" : view.IsLoss ? "Loss" : "Victory")}",
            $"Victories: {Num(view.Victories)}",
            $"Defeats: {Num(view.Defeats)}",
            $"Final phase: {PhaseText(view.Phase)}",
            "No resume or retry is available for a finished run."
        };

        return new ScreenModel(view.Phase == RunPhase.Retired ? "Expedition retired" : view.IsLoss ? "Expedition lost" : "Expedition complete",
            lines, Array.Empty<ScreenCard>(), Navigation(context));
    }

    private static ScreenModel ComposeEndless(EndlessChoiceView view, ScreenContext context)
    {
        var lines = new List<string>
        {
            $"Completed rounds: {Num(view.CompletedRounds)}",
            $"Next budget: {Amount(view.NextBudget)}",
            $"Pending supply points: {Num(view.PendingSupplyPoints)}",
            "Continue the endless expedition or finish the run."
        };

        var actions = new List<ScreenAction>
        {
            Action("CONTINUE ENDLESS", ScreenCommand.ContinueEndless, context,
                primary: true),
            Action("FINISH", ScreenCommand.Finish, context),
            Action("SLOTS", ScreenCommand.Slots, context),
            Action("RETURN TO MENU", ScreenCommand.ReturnToMenu, context)
        };

        return new ScreenModel("Endless expedition", lines,
            Array.Empty<ScreenCard>(), actions);
    }

    private static ScreenModel ComposeBlocked(BlockedView view, ScreenContext context)
    {
        var suppliedReason = view.Reason;
        var reason = string.IsNullOrWhiteSpace(suppliedReason)
            ? "The run cannot continue safely right now."
            : suppliedReason;

        var lines = new List<string>
        {
            $"Reason: {reason}",
            "Your run data has been preserved.",
            "Recovery is not available from this screen; return to the menu or slots."
        };

        return new ScreenModel("Run blocked", lines,
            Array.Empty<ScreenCard>(), Navigation(context));
    }

    private static ScreenModel ComposeError(ErrorView view, ScreenContext context)
    {
        var suppliedMessage = view.Message;
        var message = string.IsNullOrWhiteSpace(suppliedMessage)
            ? "An unexpected error occurred. Open the save slots to check the last checkpoint."
            : suppliedMessage;

        var title = string.IsNullOrWhiteSpace(view.Title)
            ? "Unexpected error"
            : view.Title!;

        var canRetry = view.CanRetry && context.CanRetry;
        var lines = new List<string>
        {
            message,
            "The last saved checkpoint remains available in the save slots."
        };

        lines.Add(canRetry
            ? "A safe retry is available."
            : "Retry is not available for this error.");

        var actions = new List<ScreenAction>();
        if (canRetry)
        {
            actions.Add(Action("RETRY", ScreenCommand.Retry, context, primary: true));
        }

        actions.Add(Action("SLOTS", ScreenCommand.Slots, context));
        actions.Add(Action("RETURN TO MENU", ScreenCommand.ReturnToMenu, context));

        return new ScreenModel(title, lines,
            Array.Empty<ScreenCard>(), actions);
    }

    private static ScreenModel ComposeUnknown(ScreenContext context)
    {
        var lines = new List<string>
        {
            "This screen is not available in the current version.",
            "No run action was taken. Return to the menu or open a save slot."
        };

        return new ScreenModel("Screen unavailable", lines,
            Array.Empty<ScreenCard>(), Navigation(context));
    }

    private static List<ScreenAction> Navigation(ScreenContext context)
    {
        return new List<ScreenAction>
        {
            Action("SLOTS", ScreenCommand.Slots, context),
            Action("RETURN TO MENU", ScreenCommand.ReturnToMenu, context)
        };
    }

    private static ScreenAction Action(string label, ScreenCommand command,
        ScreenContext context, bool allowed = true, string? argument = null,
        bool primary = false)
    {
        return new ScreenAction(label, command, allowed && !context.Busy, argument, primary);
    }

    private static string Num(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Amount(ScenarioValue? value)
    {
        if (value is null)
        {
            return "Not available";
        }

        var unit = string.IsNullOrWhiteSpace(value.UnitId) ? "units" : value.UnitId;
        return $"{value.Value.ToString(CultureInfo.InvariantCulture)} {unit}";
    }

    private static string? FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string SuppliedText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value!;

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string RiskText(EncounterRisk risk) => risk switch
    {
        EncounterRisk.Cautious => "Cautious",
        EncounterRisk.Standard => "Standard",
        EncounterRisk.Dangerous => "Dangerous",
        _ => "Unknown"
    };

    private static string OutcomeText(EncounterOutcome outcome) => outcome switch
    {
        EncounterOutcome.Victory => "Victory",
        EncounterOutcome.FriendlyForceDestroyed => "Friendly force destroyed",
        EncounterOutcome.CombatRetreat => "Combat retreat",
        EncounterOutcome.PrecombatAborted => "Precombat aborted",
        EncounterOutcome.TechnicalFailure => "Technical failure",
        _ => "Unknown"
    };

    private static string PhaseText(RunPhase phase) => phase switch
    {
        RunPhase.Building => "Building",
        RunPhase.ChoosingEncounter => "Choosing encounter",
        RunPhase.LoadingEncounter => "Loading encounter",
        RunPhase.Fighting => "Fighting",
        RunPhase.Resolving => "Resolving",
        RunPhase.AwaitingEndlessChoice => "Awaiting endless choice",
        RunPhase.Lost => "Lost",
        RunPhase.Finished => "Finished",
        RunPhase.Retired => "Retired",
        RunPhase.Suspended => "Suspended",
        _ => "Unknown"
    };
}
