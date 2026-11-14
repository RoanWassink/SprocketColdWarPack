using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Game.UI;

// Pure helpers behind the visual encounter menu and result screen (2026-10-31).
// No Unity, BepInEx or native types: the Kit view (CampaignMenuVisuals.cs) draws
// these models, tests/Presentation checks them.

internal static class MenuText
{
    private static readonly NumberFormatInfo Grouped = new()
    {
        NumberGroupSeparator = " ",
        NumberDecimalSeparator = ".",
        NumberGroupSizes = new[] { 3 }
    };

    // 287942 -> "287 942", independent of the player's culture.
    internal static string Number(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", Grouped);

    internal static string Number(int value) => Number((decimal)value);

    // "Round 5 · Coldwar · Defence"; blank parts are skipped.
    internal static string JoinFacts(params string?[] parts) =>
        string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    internal static string RarityText(RewardRarity rarity) => rarity switch
    {
        RewardRarity.UltraRare => "ULTRA RARE",
        _ => rarity.ToString().ToUpperInvariant()
    };
}

internal sealed record CompositionLine(string Text, string? Role);

internal sealed record RewardBlock(string Name, string Effect, string? Extra, string RarityText, RewardRarity Rarity);

// Enemy cost against the player's budget. Fraction is limited to 0..1; Over marks
// an enemy force that costs more than the budget.
internal sealed record CostBar(decimal Enemy, decimal Budget, float Fraction, bool Over, string Label);

internal sealed record OfferCardModel(
    string Title, IReadOnlyList<string> Kinds, string? RarityText, RewardRarity? Rarity,
    string MapId, int EnemyCount, int Supply, CostBar? Cost,
    IReadOnlyList<CompositionLine> Composition, RewardBlock? Reward,
    IReadOnlyList<string> Details, string? Tagline = null);

// Name/price lookups of the native catalogue, injected so the model stays pure.
internal sealed record OfferLookups(
    Func<string, string> Name, Func<string, decimal> Cost, Func<string, string> Describe);

internal static class OfferVisuals
{
    internal static OfferCardModel Build(EncounterPlan offer, string plainTitle, decimal? budget, OfferLookups look, bool testPool = false)
    {
        var extras = offer.Extras;
        var enemies = offer.Enemies ?? Array.Empty<EnemyUnit>();
        var count = enemies.Sum(enemy => enemy.Count);
        var total = enemies.Sum(enemy => look.Cost(enemy.BlueprintId) * enemy.Count);

        var title = plainTitle;
        var kinds = new List<string>();
        if (testPool) { kinds.Add("TEST"); title = look.Name(enemies[0].BlueprintId); }
        var details = new List<string>();
        var boss = extras?.BossBlueprintId != null;
        var curated = extras?.CuratedBoss == true;
        var tagline = boss ? BossTagline(extras?.BossCharacter) : null;

        if (boss)
        {
            kinds.Add("BOSS");
            details.Add($"BOSS — {extras!.BossCharacter} · {extras.BossEra}" + (extras.PlaceholderBoss ? " (placeholder)" : ""));
        }
        SpecialBattle? special = extras?.Special;
        var formation = false;
        // A boss keeps its name and BOSS chip; its Special record only carries
        // the escort formation/roles, never a second card title.
        if (special != null && !boss)
        {
            formation = special.Preset == SpecialPreset.TacticalGroup;
            var label = formation ? "FORMATION" : "SPECIAL";
            kinds.Add(label);
            title = special.Title;
            details.Add($"{label} · {special.Title} · {special.Pattern} formation");
        }
        if (curated && !string.IsNullOrWhiteSpace(extras!.BossCharacter)) title = extras.BossCharacter!;

        details.Add($"Era: {offer.Era.NativeId} · map: {offer.MapId}");
        if (!curated) details.Add($"Risk: {offer.Risk}");
        if (!string.IsNullOrWhiteSpace(offer.SpawnLayoutId)) details.Add($"Spawn layout: {offer.SpawnLayoutId}");
        if (formation) details.Add("Intent: " + SpecialBattleRules.DescribeIntent(special!));
        if (extras?.EnemyBudgetTarget is { } target)
            details.Add($"Target enemy cost: <={MenuText.Number(target)} · actual {MenuText.Number(total)}");
        if (extras != null)
            details.Add($"Supply: {offer.SupplyReward} for next era · win funds +5% base");
        details.AddRange(enemies.Select(enemy => $"{enemy.Count} × " + look.Describe(enemy.BlueprintId)));

        CostBar? cost = null;
        if (budget is { } available)
        {
            var fraction = available <= 0m ? (total > 0m ? 1f : 0f) : (float)Math.Clamp(total / available, 0m, 1m);
            cost = new CostBar(total, available, fraction, total > available,
                $"Enemy {MenuText.Number(total)} · your budget {MenuText.Number(available)}"
                + (available > 0m ? $" · Enemy force: {(total / available).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}× your budget" : ""));
        }

        RewardBlock? reward = null;
        string? rarityText = null;
        RewardRarity? rarity = null;
        if (extras != null)
        {
            reward = Reward(extras.Reward, boss);
            rarityText = reward.RarityText;
            rarity = extras.Reward.Rarity;
        }

        return new OfferCardModel(title, kinds, rarityText, rarity, offer.MapId, count, offer.SupplyReward, cost,
            Composition(offer, look.Name), reward, details, tagline);
    }

    // One short character line directly under the boss name (0.3.8).
    internal static string? BossTagline(string? character)
    {
        var name = character?.Trim();
        if (name == null) return null;
        if (name.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) name = name[4..];
        return name switch
        {
            "Fortress" => "Dug in — you must attack",
            "Hunter" => "Hunts you with flankers",
            "Battery" => "Long-range battery",
            _ => null
        };
    }

    // One line per design: "4× Leopard 2A7", with the deployment role when the
    // formation records one. A formation uses its own unit list only when it adds
    // up to the same number of hulls as the offer.
    internal static IReadOnlyList<CompositionLine> Composition(EncounterPlan offer, Func<string, string> name)
    {
        var enemies = offer.Enemies ?? Array.Empty<EnemyUnit>();
        var special = offer.Extras?.Special;
        if (special?.Enemies is { Count: > 0 } units
            && units.Where(unit => unit.Count > 0).Sum(unit => unit.Count) == enemies.Sum(enemy => enemy.Count)
            && units.Any(unit => !string.IsNullOrWhiteSpace(unit.DeploymentRole)))
        {
            return units.Where(unit => unit.Count > 0)
                .GroupBy(unit => (unit.BlueprintId, Role: RoleLabel(unit.DeploymentRole)))
                .OrderBy(group => RoleOrder(group.Key.Role))
                .ThenBy(group => group.Key.BlueprintId, StringComparer.Ordinal)
                .Select(group => new CompositionLine($"{group.Sum(unit => unit.Count)}× {name(group.Key.BlueprintId)}", group.Key.Role))
                .ToList();
        }
        return enemies.Where(enemy => enemy.Count > 0)
            .Select(enemy => new CompositionLine($"{enemy.Count}× {name(enemy.BlueprintId)}", null))
            .ToList();
    }

    internal static string? RoleLabel(string? role) => string.IsNullOrWhiteSpace(role) ? null : role!.Trim() switch
    {
        "Center" => "centre",
        "Flank" => "flanks",
        "Support" => "support",
        var other => other.ToLowerInvariant()
    };

    private static int RoleOrder(string? role) => role switch
    {
        "centre" => 0,
        "flanks" => 1,
        "support" => 2,
        null => 4,
        _ => 3
    };

    // Reward card: name, then one short effect line with the duration. A boss
    // card adds the "choose 1 of 3" rule that the old text carried.
    internal static RewardBlock Reward(RewardItem item, bool boss)
    {
        var parts = RewardRules.DescribeParts(item);
        var effect = parts.Value + (parts.Note.Length == 0 ? "" : "; " + parts.Note);
        var timing = item.Kind == RewardKind.WarBonds
            ? parts.Timing
            : item.Duration == 1 ? "1 victory" : $"{item.Duration} victories";
        return new RewardBlock(parts.Name, effect + " · " + timing,
            boss ? "Choose 1 of 3 purple items after victory; this one is included." : null,
            MenuText.RarityText(item.Rarity), item.Rarity);
    }
}

// File lookup and crop for the map thumbnails.
internal static class MapThumbnailRules
{
    // <Map>_CustomBattle.png first, <Map>_Scenario.png as fallback. A map id that
    // is not a plain name (it comes from saved data) yields no candidates.
    internal static IReadOnlyList<string> Candidates(string splashDirectory, string mapId)
    {
        if (string.IsNullOrEmpty(mapId) || mapId.Length > 64
            || !mapId.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
            return Array.Empty<string>();
        return new[]
        {
            System.IO.Path.Combine(splashDirectory, mapId + "_CustomBattle.png"),
            System.IO.Path.Combine(splashDirectory, mapId + "_Scenario.png")
        };
    }

    // uv rectangle (x, y, w, h; 0..1, origin bottom-left as in Unity) that fills a box of
    // boxW x boxH from a texW x texH picture without distortion, centred.
    internal static (float X, float Y, float W, float H) CoverUv(float texW, float texH, float boxW, float boxH)
    {
        if (texW <= 0 || texH <= 0 || boxW <= 0 || boxH <= 0) return (0, 0, 1, 1);
        var texAspect = texW / texH;
        var boxAspect = boxW / boxH;
        if (boxAspect > texAspect)
        {
            var h = texAspect / boxAspect;
            return (0, (1 - h) / 2, 1, h);
        }
        var w = boxAspect / texAspect;
        return ((1 - w) / 2, 0, w, 1);
    }
}
