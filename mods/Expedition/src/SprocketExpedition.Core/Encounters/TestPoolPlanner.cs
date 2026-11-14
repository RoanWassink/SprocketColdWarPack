using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Encounters;

// The cursor counts tanks fought, independent of starting era and rerolls.
// 0.3.4: a lost or retreated round advances it too; an aborted or technically
// failed attempt does not. 0.3.2: every card of a round holds the same next
// TanksPerBattle distinct tanks (risk only changes map and reward), so a win
// tests several designs at once and none is skipped. The native era on each blueprint remains untouched.
internal static class TestPoolPlanner
{
    internal const int TanksPerBattle = 4;

    internal static IReadOnlyList<EncounterPlan> Generate(OfferRequest request, CatalogueDefinition definition, CatalogueOptions options)
    {
        var pool = (definition.TestPool ?? Array.Empty<TestPoolDesign>())
            .Where(t => t.Design.Qualification >= options.RequiredQualification)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.Design.Blueprint.LocalId, StringComparer.Ordinal).ToArray();
        if (pool.Length < 3) throw new InvalidOperationException("Test pool needs at least three accepted distinct tanks.");
        var era = definition.Eras.Single(e => e.Era == request.Era);
        var maps = era.Maps.Where(m => m.Qualification >= options.RequiredQualification)
            .OrderBy(m => m.MapId, StringComparer.Ordinal).ToArray();
        if (maps.Length == 0) throw new InvalidOperationException("No qualified map exists for this test round.");
        var lend = era.Enemies.Where(e => e.Qualification >= options.RequiredQualification)
            .OrderBy(e => e.CheckedPrice.Value).ThenBy(e => e.Blueprint.LocalId, StringComparer.Ordinal).First();
        var boss = request.Block < definition.Eras.Count && request.Round % 4 == 0;
        var hash = CatalogueHasher.Compute(definition);
        var tanks = Enumerable.Range(0, Math.Min(TanksPerBattle, pool.Length))
            .Select(j => pool[(int)(((long)request.TestPoolOffset + j) % pool.Length)]).ToArray();
        var enemies = tanks.Select(t => new EnemyUnit(t.Design.Blueprint.LocalId, 1)).ToArray();
        var plans = new List<EncounterPlan>();
        foreach (var risk in boss ? new[] { EncounterRisk.Standard } : Enum.GetValues<EncounterRisk>())
        {
            var index = boss ? 0 : (int)risk;
            var tank = tanks[0];
            var design = tank.Design;
            var map = maps[(request.Round - 1 + index) % maps.Length];
            var reward = RewardRules.Create(request.Seed, request.Round,
                $"loot.{request.RunId}.{request.Round}.{request.OfferRoll}.{(int)risk}", (int)risk,
                blueprintId: lend.Blueprint.LocalId, rulesVersion: request.RulesVersion);
            if (boss) reward = reward with { Kind = RewardKind.CapturedPrototype, Rarity = RewardRarity.UltraRare,
                Magnitude = 1m, Duration = 6, RemainingRounds = 6, BlueprintId = design.Blueprint.LocalId };
            var extras = new EncounterExtras(request.RulesVersion, request.OfferRoll, reward,
                BossBlueprintId: boss ? design.Blueprint.LocalId : null,
                BossEra: boss ? design.Blueprint.Era.NativeId : null,
                BossCharacter: boss ? tank.Name : null) { CuratedBoss = boss };
            var pending = new EncounterPlan("", "", request.Round, map.MapId, map.SpawnLayoutIds[0],
                request.Era, risk, enemies,
                EncounterRules.Profile(risk).SupplyReward,
                map.RequiredMods.Concat(tanks.SelectMany(t => t.Design.Blueprint.RequiredMods)).Distinct().ToArray(), definition.Version)
                { Extras = extras };
            var identified = pending with { OfferId = PlanHasher.CreateOfferId(request.RunId, hash, pending) };
            plans.Add(identified with { PlanHash = PlanHasher.ComputePlanHash(hash, identified) });
        }
        return plans;
    }
}
