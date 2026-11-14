using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SprocketExpedition.Core.Encounters;

// Length-prefixed canonical encoding. Every component is ordered before it is
// encoded so that list order in a record cannot silently change a hash.
internal static class CanonicalText
{
    public static void Append(StringBuilder builder, string? value)
    {
        var text = value ?? string.Empty;
        builder
            .Append(text.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(text)
            .Append(';');
    }

    public static void Append(StringBuilder builder, int value) =>
        Append(builder, value.ToString(CultureInfo.InvariantCulture));

    public static void Append(StringBuilder builder, long value) =>
        Append(builder, value.ToString(CultureInfo.InvariantCulture));

    public static void Append(StringBuilder builder, decimal value) =>
        Append(builder, value.ToString("G29", CultureInfo.InvariantCulture));

    public static void AppendMods(
        StringBuilder builder,
        IReadOnlyList<ModRequirement>? mods)
    {
        var ordered = (mods ?? Array.Empty<ModRequirement>())
            .Where(mod => mod is not null)
            .OrderBy(mod => mod.Id, StringComparer.Ordinal)
            .ThenBy(mod => mod.Version, StringComparer.Ordinal)
            .ToArray();

        Append(builder, ordered.Length);
        foreach (var mod in ordered)
        {
            Append(builder, mod.Id);
            Append(builder, mod.Version);
        }
    }

    public static string Sha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public static class CatalogueHasher
{
    // Covers every checked content field that can affect a generated encounter,
    // including blueprint snapshot hashes, map content hashes, curated threat
    // scores, checked prices, roles, required mods and qualification.
    public static string Compute(CatalogueDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var builder = new StringBuilder();
        CanonicalText.Append(builder, "catalogue/v1");
        CanonicalText.Append(builder, definition.Version);
        CanonicalText.Append(builder, definition.PriceUnitId);
        CanonicalText.Append(builder, definition.IsFixture ? 1 : 0);

        var eras = (definition.Eras ?? Array.Empty<CatalogueEraContent>())
            .Where(era => era is not null)
            .OrderBy(era => era.Era?.NativeId ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        CanonicalText.Append(builder, eras.Length);
        foreach (var era in eras)
        {
            CanonicalText.Append(builder, era.Era?.NativeId);

            var maps = (era.Maps ?? Array.Empty<MapEntry>())
                .Where(map => map is not null)
                .OrderBy(map => map.MapId, StringComparer.Ordinal)
                .ToArray();
            CanonicalText.Append(builder, maps.Length);
            foreach (var map in maps)
            {
                CanonicalText.Append(builder, map.Era?.NativeId);
                CanonicalText.Append(builder, map.MapId);
                CanonicalText.Append(builder, map.ContentHash);

                var layouts = (map.SpawnLayoutIds ?? Array.Empty<string>())
                    .OrderBy(layout => layout, StringComparer.Ordinal)
                    .ToArray();
                CanonicalText.Append(builder, layouts.Length);
                foreach (var layout in layouts)
                {
                    CanonicalText.Append(builder, layout);
                }

                CanonicalText.Append(builder, (int)map.Qualification);
                CanonicalText.AppendMods(builder, map.RequiredMods);
            }

            var enemies = (era.Enemies ?? Array.Empty<EnemyDesignEntry>())
                .Where(enemy => enemy is not null)
                .OrderBy(enemy => enemy.Blueprint?.LocalId ?? string.Empty,
                    StringComparer.Ordinal)
                .ToArray();
            CanonicalText.Append(builder, enemies.Length);
            foreach (var enemy in enemies)
            {
                var blueprint = enemy.Blueprint;
                CanonicalText.Append(builder, blueprint?.LocalId);
                CanonicalText.Append(builder, blueprint?.SnapshotHash);
                CanonicalText.Append(builder, blueprint?.Era?.NativeId);
                CanonicalText.Append(builder, (int)enemy.Role);
                CanonicalText.Append(builder, enemy.ThreatScore);
                CanonicalText.Append(builder, enemy.CheckedPrice?.Value ?? 0m);
                CanonicalText.Append(builder, enemy.CheckedPrice?.UnitId);
                CanonicalText.Append(builder, (int)enemy.Qualification);
                CanonicalText.AppendMods(builder, blueprint?.RequiredMods);
                if (enemy.FormationFamily != null)
                { CanonicalText.Append(builder, "formation-family/v1"); CanonicalText.Append(builder, enemy.FormationFamily); }
                if (enemy.MassKg != null)
                { CanonicalText.Append(builder, "enemy-mass-kg/v1"); CanonicalText.Append(builder, enemy.MassKg.Value.ToString(CultureInfo.InvariantCulture)); }
                if (enemy.MaxPerBattle is { } cap)
                { CanonicalText.Append(builder, "enemy-max-per-battle/v1"); CanonicalText.Append(builder, cap); }
                if (enemy.MinimumRisk != null)
                { CanonicalText.Append(builder, "enemy-minimum-risk/v1"); CanonicalText.Append(builder, (int)enemy.MinimumRisk.Value); }
            }
        }

        if (definition.BossBlueprints != null)
            foreach (var boss in definition.BossBlueprints.OrderBy(x => x.Key, StringComparer.Ordinal))
            { CanonicalText.Append(builder, boss.Key); CanonicalText.Append(builder, boss.Value); }
        if (definition.BossCandidates != null)
            foreach (var pair in definition.BossCandidates.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                CanonicalText.Append(builder, "boss-candidates/v1");
                CanonicalText.Append(builder, pair.Key);
                CanonicalText.Append(builder, pair.Value.Count);
                foreach (var id in pair.Value) CanonicalText.Append(builder, id);
            }
        if (definition.TestPool != null)
        {
            CanonicalText.Append(builder, "test-pool/v1");
            foreach (var tank in definition.TestPool.OrderBy(t => t.Design.Blueprint.LocalId, StringComparer.Ordinal))
            {
                CanonicalText.Append(builder, tank.Name);
                // Reuse the canonical full-design encoding, without recursion.
                CanonicalText.Append(builder, Compute(definition with { TestPool = null, BossBlueprints = null, BossCandidates = null,
                    Eras = new[] { new CatalogueEraContent(tank.Design.Blueprint.Era, Array.Empty<MapEntry>(), new[] { tank.Design }) } }));
            }
        }
        return CanonicalText.Sha256(builder.ToString());
    }
}

public static class PlanHasher
{
    public const string Algorithm = "SHA-256/canonical-v1";
    public const string OfferIdPrefix = "enc";

    // Hash of every field that affects the encounter, excluding the offer id and
    // the plan hash itself.
    public static string ComputeContentHash(
        string catalogueContentHash,
        EncounterPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new StringBuilder();
        AppendPayload(builder, catalogueContentHash, plan);
        return CanonicalText.Sha256(builder.ToString());
    }

    // Covers the content hash, every plan field and the offer id.
    public static string ComputePlanHash(
        string catalogueContentHash,
        EncounterPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new StringBuilder();
        AppendPayload(builder, catalogueContentHash, plan);
        CanonicalText.Append(builder, plan.OfferId);
        return CanonicalText.Sha256(builder.ToString());
    }

    public static string CreateOfferId(
        string runId,
        string catalogueContentHash,
        EncounterPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string contentHash = ComputeContentHash(catalogueContentHash, plan);
        string round = plan.Round.ToString("D2", CultureInfo.InvariantCulture);
        return string.Join(
            '.',
            OfferIdPrefix,
            RunToken(runId),
            round,
            RiskToken(plan.Risk),
            contentHash.Substring(0, 12));
    }

    public static string RunToken(string runId)
    {
        if (!ContentSafety.IsSafeRunId(runId))
        {
            throw new ArgumentException("Run id is not a safe local identifier.", nameof(runId));
        }

        return CanonicalText.Sha256(runId).Substring(0, 8);
    }

    public static string RiskToken(EncounterRisk risk) => risk switch
    {
        EncounterRisk.Cautious => "cautious",
        EncounterRisk.Standard => "standard",
        EncounterRisk.Dangerous => "dangerous",
        _ => throw new ArgumentOutOfRangeException(
            nameof(risk), risk, "Unknown encounter risk.")
    };

    public static bool IsSafeOfferId(string? offerId)
    {
        if (string.IsNullOrEmpty(offerId) || offerId.Length > 128)
        {
            return false;
        }

        foreach (char c in offerId)
        {
            bool allowed = (c >= 'a' && c <= 'z')
                || (c >= '0' && c <= '9')
                || c == '.'
                || c == '-';
            if (!allowed)
            {
                return false;
            }
        }

        return offerId.StartsWith(OfferIdPrefix + ".", StringComparison.Ordinal);
    }

    private static void AppendPayload(
        StringBuilder builder,
        string catalogueContentHash,
        EncounterPlan plan)
    {
        CanonicalText.Append(builder, "encounter-plan-payload/v1");
        CanonicalText.Append(builder, catalogueContentHash);
        CanonicalText.Append(builder, plan.Round);
        CanonicalText.Append(builder, plan.MapId);
        CanonicalText.Append(builder, plan.SpawnLayoutId);
        CanonicalText.Append(builder, plan.Era?.NativeId);
        CanonicalText.Append(builder, (int)plan.Risk);
        CanonicalText.Append(builder, plan.SupplyReward);
        CanonicalText.Append(builder, plan.CatalogueVersion);

        var enemies = (plan.Enemies ?? Array.Empty<EnemyUnit>())
            .Where(enemy => enemy is not null)
            .OrderBy(enemy => enemy.BlueprintId, StringComparer.Ordinal)
            .ThenBy(enemy => enemy.Count)
            .ToArray();
        CanonicalText.Append(builder, enemies.Length);
        foreach (var enemy in enemies)
        {
            CanonicalText.Append(builder, enemy.BlueprintId);
            CanonicalText.Append(builder, enemy.Count);
        }

        CanonicalText.AppendMods(builder, plan.RequiredMods);
        if (plan.Extras != null) CanonicalText.Append(builder, System.Text.Json.JsonSerializer.Serialize(plan.Extras));
    }
}
