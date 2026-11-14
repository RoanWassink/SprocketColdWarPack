#if CAMPAIGN
using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Game.UI;
using SprocketUiKit;
using UnityEngine;

namespace SprocketExpedition.Game;

// Visual pieces of the Expedition menu, drawn with Kit components only: encounter
// offer cards (map thumbnail, chips, cost bar, composition, reward block, details)
// and the result banner. The data comes from the pure models in UI\MenuVisuals.cs
// and ScreenComposer; nothing here decides gameplay.
internal static partial class CampaignNativeMenu
{
    private const string ThumbnailFill = "#2b2924";   // Kit button colour, used when no picture exists
    private const string ThumbnailText = "#fff0cc";   // Kit text colour
    private const string BarTrack = "#14150f";        // Kit panel colour
    private const string BarOver = "#e07a4f";         // Kit error colour: enemy force above the budget
    private const string ToneVictory = "#8fd99c";     // Kit success text
    private const string ToneDefeat = "#e07a4f";      // Kit error
    private const string ToneNeutral = "#f3cf86";     // Kit accent text
    private const float ThumbnailHeight = 150f;

    private static void AddFactChips(Stack stack, List<ScreenFact> facts)
    {
        var row = stack.Row(8);
        foreach (var fact in facts)
        {
            var chip = row.Chip(fact.Text, fact.Emphasis ? ChipStyle.Accent : ChipStyle.Context);
            if (fact.Tooltip != null)
                chip.Tooltip(fact.Tooltip.Replace(CampaignNativeCatalogue.CostUnit, "design cost", StringComparison.Ordinal));
        }
        row.Spacer();
    }

    // ---------------------------------------------------------------- offers

    private static void AddOfferCard(Row row, MenuCard card)
    {
        var model = card.Offer!;
        var accent = model.Rarity is { } rarity ? RewardColor(rarity) : (Color?)null;
        var box = row.Card().Flex().Fill();
        if (accent is { } edge) box.Accent(edge);
        var stack = box.Stack(8);

        AddThumbnail(stack, model.MapId, card.Thumb);
        stack.Label(model.Title, TextStyle.Section).Wrap();
        if (!string.IsNullOrWhiteSpace(model.Tagline))
            stack.Label(model.Tagline!, TextStyle.Value).Wrap();

        // Kind (BOSS / FORMATION / SPECIAL) and rarity: one row of chips in the rarity colour.
        if (model.Kinds.Count > 0 || model.RarityText != null)
        {
            var chips = stack.Row(6);
            foreach (var kind in model.Kinds)
            {
                if (accent is { } colour) chips.Chip(kind, colour);
                else chips.Chip(kind, ChipStyle.Tag);
            }
            if (model.RarityText != null)
            {
                if (accent is { } colour) chips.Chip(model.RarityText, colour);
                else chips.Chip(model.RarityText, ChipStyle.Tag);
            }
            chips.Spacer();
        }

        // Core figures; the map is already printed on the picture.
        var figures = stack.Row(6);
        figures.Chip(model.EnemyCount == 1 ? "1 enemy" : $"{model.EnemyCount} enemies", ChipStyle.Neutral);
        figures.Chip($"Supply +{model.Supply}", ChipStyle.Neutral);
        figures.Spacer();

        if (model.Cost is { } cost) AddCostBar(stack, cost);

        foreach (var line in model.Composition)
        {
            var entry = stack.Row(4).ShrinkToFit();
            entry.Label(line.Text, TextStyle.Value);
            if (line.Role != null) entry.Label("· " + line.Role, TextStyle.Faint);
        }

        if (model.Reward is { } reward) AddRewardBlock(stack, reward);

        AddDetails(stack, model.Details);

        if (card.Buttons.Count == 0) return;
        var buttons = stack.Row(8);
        foreach (var button in card.Buttons) AddKitButton(buttons, button).Flex();
    }

    // Picture over the full card width, map name bottom left with a shadow. The
    // texture was loaded when the page was composed; drawing only reads it.
    private static void AddThumbnail(Stack stack, string mapId, Texture2D? texture)
    {
        stack.Canvas(ThumbnailHeight, painter =>
        {
            var size = painter.Size;
            if (texture != null)
            {
                var uv = MapThumbnailRules.CoverUv(texture.width, texture.height, size.x, size.y);
                painter.Image(texture, 0, 0, size.x, size.y, new Rect(uv.X, uv.Y, uv.W, uv.H));
            }
            else painter.FillRect(0, 0, size.x, size.y, UiKit.Colour(ThumbnailFill));
            painter.FillRect(0, size.y - 36, size.x, 36, new Color(0f, 0f, 0f, .45f));
            painter.Text(10, size.y - 8, mapId, UiKit.Colour(ThumbnailText), TextStyle.Section, TextAnchor.LowerLeft);
        });
    }

    // Kit progress bar while the enemy force fits the budget; a bar in the Kit error
    // colour with a tick at the budget when it does not (the Kit bar has one colour).
    private static void AddCostBar(Stack stack, CostBar cost)
    {
        stack.Label(cost.Label, TextStyle.Small).Wrap();
        if (!cost.Over)
        {
            stack.Progress(() => cost.Fraction);
            return;
        }
        stack.Canvas(8, painter =>
        {
            var size = painter.Size;
            painter.FillRect(0, 0, size.x, size.y, UiKit.Colour(BarOver));
            if (cost.Budget > 0m && cost.Enemy > 0m)
            {
                var x = Math.Clamp(size.x * (float)(cost.Budget / cost.Enemy), 1f, size.x - 2f);
                painter.FillRect(x - 1f, 0, 2f, size.y, UiKit.Colour(ThumbnailText));
            }
        }).Background(UiKit.Colour(BarTrack), false);
    }

    private static void AddRewardBlock(Stack stack, RewardBlock reward)
    {
        var block = stack.Card().Accent(RewardColor(reward.Rarity)).MinHeight(48);
        var text = block.Stack(2);
        text.Label(reward.Name, TextStyle.Label).Wrap();
        text.Label(reward.Effect, TextStyle.Value).Wrap();
        if (reward.Extra != null) text.Label(reward.Extra, TextStyle.Faint).Wrap();
    }

    private static void AddDetails(Stack stack, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        var details = stack.Collapsible("Details");
        foreach (var line in lines) details.Body.Label(line, TextStyle.Small).Wrap();
    }

    // ---------------------------------------------------------------- result

    private static void AddResultBanner(Stack stack, MenuBanner banner)
    {
        var hex = banner.Tone switch
        {
            ResultTone.Victory => ToneVictory,
            ResultTone.Defeat => ToneDefeat,
            _ => ToneNeutral
        };
        var colour = UiKit.Colour(hex);

        var headline = stack.Card().Accent(colour).MinHeight(92);
        var text = headline.Stack(4);
        text.Label(banner.Headline.ToUpperInvariant(), TextStyle.Title).Colour(hex);
        text.Label(banner.Subline, TextStyle.Label);
        if (banner.OutcomeText != null)
        {
            var chips = text.Row(6);
            chips.Chip(banner.OutcomeText, colour);
            chips.Spacer();
        }
        if (!string.IsNullOrEmpty(banner.Message)) text.Label(banner.Message, TextStyle.Value).Wrap();

        if (banner.Tiles.Count > 0)
        {
            var tiles = stack.Row(10);
            foreach (var tile in banner.Tiles)
            {
                var cell = tiles.Card().Flex().MinHeight(64);
                var cellText = cell.Stack(2);
                cellText.Label(tile.Caption, TextStyle.Faint);
                cellText.Label(tile.Value, TextStyle.Title);
            }
        }

        if (!string.IsNullOrEmpty(banner.Notice))
        {
            var notice = stack.Card().Accent(UiKit.Colour(ToneVictory)).MinHeight(40);
            notice.Label(banner.Notice, TextStyle.Value).Wrap();
        }

        if (banner.Item is { } item)
        {
            var itemColour = RewardColor(item.Rarity);
            var received = stack.Card().Accent(itemColour).MinHeight(72);
            var itemText = received.Stack(4);
            var head = itemText.Row(8);
            head.Label("ITEM RECEIVED", TextStyle.Section);
            head.Chip(item.RarityText, itemColour);
            head.Spacer();
            itemText.Label(item.Name, TextStyle.Label).Wrap();
            itemText.Label(item.Effect, TextStyle.Value).Wrap();
            if (item.Extra != null) itemText.Label(item.Extra, TextStyle.Faint).Wrap();
        }

        // 0.3.6 defeat blocks (Losses / Battle stats): plain Kit cards, no new
        // Kit API and no change to the Victory markup.
        foreach (var block in banner.Blocks)
        {
            var card = stack.Card().MinHeight(48);
            var blockText = card.Stack(4);
            blockText.Label(block.Title.ToUpperInvariant(), TextStyle.Section);
            foreach (var line in block.Lines) blockText.Label(line, TextStyle.Value).Wrap();
        }

        AddDetails(stack, banner.Details);
    }

    private static MenuBanner BuildBanner(ResultView result, ResultSummary summary,
        IReadOnlyList<ResultBlock>? blocks)
    {
        var banner = new MenuBanner
        {
            Tone = summary.Tone,
            Headline = summary.Headline,
            Subline = summary.Subline,
            OutcomeText = summary.OutcomeText,
            Message = summary.Message,
            Notice = CampaignVeteranUnlocks.NoticeFor(result.Result)
        };
        if (blocks is { Count: > 0 }) banner.Blocks.AddRange(blocks);
        banner.Tiles.AddRange(summary.Tiles);
        banner.Details.AddRange(summary.Details);
        if (result.Result.Outcome == SprocketExpedition.Core.EncounterOutcome.Victory
            && result.Result.Plan.Extras is { } loot && CampaignControllerHost.RewardSnapshot is { } earned)
        {
            // Earned funds sit before the last tile (pending supply).
            banner.Tiles.Insert(Math.Max(0, banner.Tiles.Count - 1),
                new ResultTile("Earned funds · total", "+" + MenuText.Number(earned.EarnedFunds)));
            banner.Item = OfferVisuals.Reward(loot.Reward, false) with { Extra = "Open ITEMS to activate your reward." };
        }
        return banner;
    }
}
#endif
