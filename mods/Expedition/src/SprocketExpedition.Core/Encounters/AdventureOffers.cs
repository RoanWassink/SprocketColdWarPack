using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Encounters;

public sealed partial class SeededEncounterCatalogue
{
    // The test-pool cursor is part of the key: a lost test round is offered
    // again from the next tanks. It is always 0 outside the test pool.
    private readonly Dictionary<(string RunId, int Round, int Roll, int TestPoolOffset), IReadOnlyList<EncounterPlan>> adventureOffers = new();

    private OfferGenerationResult AdventureOffers(OfferRequest request)
    {
        var errors = EncounterOfferValidator.ValidateRequest(request, _definition, _options);
        if (!errors.IsValid) return OfferGenerationResult.Failure(EncounterAvailabilityStatus.InvalidRequest, errors.Errors);
        if (_resolvedRounds.Contains((request.RunId, request.Round)))
            return OfferGenerationResult.Failure(EncounterAvailabilityStatus.OfferAlreadyResolved, "Round already resolved.");
        var key = (request.RunId, request.Round, request.OfferRoll, request.TestPoolOffset);
        if (adventureOffers.TryGetValue(key, out var cached)) return OfferGenerationResult.Success(cached);
        try
        {
            var plans = AdventurePlanner.Generate(request, _definition, _options);
            adventureOffers[key] = plans;
            Register((request.RunId, request.Round), plans);
            return OfferGenerationResult.Success(plans);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException)
        { return OfferGenerationResult.Failure(EncounterAvailabilityStatus.NoValidFormation, ex.Message); }
        catch (EncounterAvailabilityException ex)
        { return OfferGenerationResult.Failure(ex.Status, ex.Message); }
    }

    private OfferGenerationResult RestoreAdventure(OfferRequest request, IReadOnlyList<EncounterPlan>? plans)
    {
        var valid = AdventureOfferValidation.Validate(request, plans, _definition, _options);
        if (!valid.IsValid) return OfferGenerationResult.Failure(EncounterAvailabilityStatus.InvalidPersistedOffers, valid.Errors);
        var restored = plans!.ToArray();
        adventureOffers[(request.RunId, request.Round, request.OfferRoll, request.TestPoolOffset)] = restored;
        Register((request.RunId, request.Round), restored);
        return OfferGenerationResult.Success(restored);
    }
}

internal static class AdventurePlanner
{
    internal static IReadOnlyList<EncounterPlan> Generate(OfferRequest request, CatalogueDefinition definition, CatalogueOptions options)
    {
        if (request.RulesVersion is not (2 or 3 or 4 or 5 or 6 or 7) || request.OfferRoll is < 0 or > 10000
            || request.EnemyBudgetMultiplier < 1m
            || request.EnemyBudgetMultiplier > (request.RulesVersion >= 5 || request.GenerationPolicy >= 5 ? 2.4m : 2m))
            throw new InvalidOperationException("Invalid adventure offer policy.");
        if (request.TestPool) return TestPoolPlanner.Generate(request, definition, options);
        var eraIndex = definition.Eras.ToList().FindIndex(era => era.Era == request.Era);
        if (eraIndex < 0) throw new InvalidOperationException("The native era is missing.");
        var policy5 = request.RulesVersion >= 5 || request.GenerationPolicy >= 5;
        // Schema 6 changes only the within-era curve and the threat budget source.
        // Rules 5 and older keep the exact arithmetic below.
        // Schema 7 adds the yellow specials, wider ambush chances and
        // character boss escorts. Every branch below is gated on policy7 so
        // schemas 2-6 keep byte-identical generation.
        var policy6 = request.RulesVersion >= 6;
        var policy7 = request.RulesVersion >= 7;
        var progression = policy5 && request.EraProgressionEnabled;
        var roundInEra = (request.Round - 1) % 4 + 1;
        var bossFight = request.Block < definition.Eras.Count && roundInEra == 4;
        var v7Special = policy7 && !bossFight && SpecialBattleRules.ShouldOfferV7(
            request.Seed, request.Round, request.OfferRoll)
            ? SpecialBattleRules.SpecialRiskV7(request.Seed, request.Round, request.OfferRoll)
            : (EncounterRisk?)null;
        var legacyRound = request.Block * 3 + Math.Min(3, roundInEra);
        var legacyWithin = EncounterRules.WithinBlockMultiplier(legacyRound);
        var schema6Within = policy6 ? EncounterRules.WithinBlockMultiplierV6(roundInEra) : 0m;
        var contentHash = CatalogueHasher.Compute(definition);
        var result = new List<EncounterPlan>();
        decimal previousFormationCost = 0;
        var curated = bossFight && request.RulesVersion >= 3;
        foreach (var risk in curated ? new[] { EncounterRisk.Standard } : Enum.GetValues<EncounterRisk>())
        {
            // Schema 6 honours MinimumRisk. A non-dangerous card is generated
            // from a catalogue that simply does not contain those designs, so
            // its roster, targets and cost distribution stay exactly what they
            // were before the dangerous folder existed. Older rules keep the
            // original definition untouched.
            var scoped = policy6 && risk != EncounterRisk.Dangerous
                ? WithoutHigherRiskDesigns(definition, risk)
                : definition;
            decimal multiplier;
            decimal legacyRequestBudget;
            if (policy5)
            {
                if (!DifficultyScaling.TryEffectiveMultiplier(request.EnemyBudgetMultiplier,
                        progression, eraIndex, risk, out multiplier))
                    throw new InvalidOperationException(
                        $"Difficulty multiplier is outside the supported policy-5 range for {risk}.");
                if (policy6 && bossFight) multiplier *= BossSelection.Schema6EnemyFactor;
                if (!DifficultyScaling.TryScaledBudget(request.AvailableBudget.Value, multiplier,
                        out legacyRequestBudget))
                    throw new InvalidOperationException(
                        "Difficulty-scaled enemy budget overflowed or is not positive.");
                if (policy6)
                {
                    // The legacy generator still applies its 3-round curve while
                    // producing the fallback roster/minions. Scale its request so
                    // that intermediate spend matches the explicit schema-6 curve.
                    try
                    {
                        legacyRequestBudget = checked(
                            legacyRequestBudget * schema6Within / legacyWithin);
                    }
                    catch (OverflowException)
                    {
                        throw new InvalidOperationException(
                            "Schema-6 within-era budget scaling overflowed.");
                    }
                }
            }
            else
            {
                multiplier = request.EnemyBudgetMultiplier * (risk == EncounterRisk.Dangerous ? 1.35m : 1m);
                legacyRequestBudget = request.AvailableBudget.Value * multiplier;
            }
            var legacyRequest = request with
            {
                RulesVersion = 1, Round = legacyRound, OfferRoll = 0,
                Seed = unchecked(request.Seed + request.OfferRoll * 6364136223846793005L),
                AvailableBudget = new ScenarioValue(legacyRequestBudget, request.AvailableBudget.UnitId)
            };
            var legacy = new SeededEncounterCatalogue(scoped, options).GenerateOffers(legacyRequest)
                .Single(plan => plan.Risk == risk);
            var currentContent = scoped.Eras[eraIndex];
            var lend = currentContent.Enemies.Where(enemy => enemy.Qualification >= options.RequiredQualification
                    && (!policy6 || DangerousDesignPolicy.AllowsAsReward(enemy)))
                .OrderBy(enemy => enemy.CheckedPrice.Value).ThenBy(enemy => enemy.Blueprint.LocalId, StringComparer.Ordinal).FirstOrDefault();
            var reward = RewardRules.Create(request.Seed, request.Round,
                $"loot.{request.RunId}.{request.Round}.{request.OfferRoll}.{(int)risk}", (int)risk,
                blueprintId: lend?.Blueprint.LocalId, rulesVersion: request.RulesVersion);
            var extras = new EncounterExtras(request.RulesVersion, request.OfferRoll, reward, EnemyBudgetMultiplier: multiplier)
                { CuratedBoss = curated, GenerationPolicy = policy6 ? null : policy5 ? 5 : null };
            var enemies = legacy.Enemies;
            var mods = legacy.RequiredMods;
            if (request.RulesVersion == 3 && !bossFight && risk == EncounterRisk.Standard
                && SpecialBattleRules.ShouldOffer(request.Seed, request.Round, request.OfferRoll))
            {
                var designs = currentContent.Enemies.Where(e => e.Qualification >= options.RequiredQualification)
                    .Select(e => new SpecialDesign(e.Blueprint.LocalId, e.CheckedPrice.Value)).ToArray();
                var target = legacy.Enemies.Sum(e => currentContent.Enemies.Single(t => t.Blueprint.LocalId == e.BlueprintId).CheckedPrice.Value * e.Count);
                var special = SpecialBattleRules.Create(request.Seed, request.Round, request.OfferRoll, target, designs);
                if (special != null && special.Enemies.Sum(e => designs.Single(d => d.BlueprintId == e.BlueprintId).Cost * e.Count) >= target * .8m)
                {
                    enemies = special.Enemies.Select(e => new EnemyUnit(e.BlueprintId, e.Count)).ToArray();
                    reward = reward with { Kind = Enum.Parse<RewardKind>(special.RewardFamily), Rarity = RewardRarity.Special,
                        Magnitude = special.RewardMagnitude, Duration = special.Duration, RemainingRounds = special.Duration, BlueprintId = null };
                    extras = extras with { Special = special, Reward = reward };
                    mods = mods.Concat(enemies.SelectMany(e => currentContent.Enemies.Single(t => t.Blueprint.LocalId == e.BlueprintId).Blueprint.RequiredMods))
                        .Distinct().ToArray();
                }
            }
            decimal? formationTarget = null;
            EnemyDesignEntry[]? formationPool = null;
            if (request.RulesVersion >= 4 && !bossFight)
            {
                var pool = currentContent.Enemies.Where(e => e.Qualification >= options.RequiredQualification
                    && (!policy6 || DangerousDesignPolicy.Allows(e, risk))).ToArray();
                var target = legacy.Enemies.Sum(e => pool.Single(t => t.Blueprint.LocalId == e.BlueprintId).CheckedPrice.Value * e.Count);
                if (policy5)
                {
                    // Policy 5 reports and plans against the exact scaled checked
                    // price target rather than the realised legacy roster spend.
                    var scaled = policy6
                        ? new ScenarioValue(checked(request.AvailableBudget.Value * multiplier),
                            request.AvailableBudget.UnitId)
                        : new ScenarioValue(legacyRequestBudget, request.AvailableBudget.UnitId);
                    target = policy6
                        ? options.CostBasedDifficulty
                            ? EncounterRules.CostThreatTargetV6(scaled, roundInEra, risk, pool)
                            : EncounterRules.ThreatTargetV6(scaled, roundInEra, risk)
                        : options.CostBasedDifficulty
                            ? EncounterRules.CostThreatTarget(scaled, request.Round, risk, pool)
                            : EncounterRules.ThreatTarget(scaled, request.Round, risk);
                }
                var theme = policy5
                    ? ThematicFormationPlannerV5.Create(pool, legacy.Enemies, target, previousFormationCost,
                        request.Seed, request.Round, request.OfferRoll, eraIndex, (int)risk,
                        request.RulesVersion)
                    : ThematicFormationPlanner.Create(pool, legacy.Enemies, target, previousFormationCost,
                        request.Seed, request.Round, request.OfferRoll);
                enemies = theme.Enemies.Select(e => new EnemyUnit(e.BlueprintId, e.Count)).ToArray();
                previousFormationCost = enemies.Sum(e => pool.Single(t => t.Blueprint.LocalId == e.BlueprintId).CheckedPrice.Value * e.Count);
                extras = extras with
                {
                    Special = theme,
                    EnemyBudgetTarget = policy5 ? target : null
                };
                mods = mods.Concat(enemies.SelectMany(e => pool.Single(t => t.Blueprint.LocalId == e.BlueprintId).Blueprint.RequiredMods)).Distinct().ToArray();
                formationTarget = target;
                formationPool = pool;
            }
            if (policy7 && !bossFight && v7Special == risk && formationPool != null)
            {
                // The special replaces the card's roster and reward, but only
                // when its checked cost still reaches the schema-6 threat target.
                var specialDesigns = formationPool
                    .Select(e => new SpecialDesign(e.Blueprint.LocalId, e.CheckedPrice.Value)).ToArray();
                var specialTarget = formationTarget ?? 0m;
                var special = SpecialBattleRules.Create(request.Seed, request.Round,
                    request.OfferRoll, specialTarget, specialDesigns);
                if (special != null)
                {
                    var specialCost = special.Enemies.Sum(e => specialDesigns
                        .Single(d => d.BlueprintId == e.BlueprintId).Cost * e.Count);
                    if (specialCost >= specialTarget * .8m)
                    {
                        enemies = special.Enemies.Select(e => new EnemyUnit(e.BlueprintId, e.Count)).ToArray();
                        reward = reward with
                        {
                            Kind = Enum.Parse<RewardKind>(special.RewardFamily),
                            Rarity = RewardRarity.Special,
                            Magnitude = special.RewardMagnitude,
                            Duration = special.Duration,
                            RemainingRounds = special.Duration,
                            BlueprintId = null
                        };
                        extras = extras with { Special = special, Reward = reward };
                        mods = mods.Concat(enemies.SelectMany(e => formationPool
                            .Single(t => t.Blueprint.LocalId == e.BlueprintId).Blueprint.RequiredMods))
                            .Distinct().ToArray();
                    }
                }
            }
            if (bossFight)
            {
                var character = new[] { "The Fortress", "The Hunter", "The Battery", "The Veteran" }[eraIndex % 4];
                var nextContent = scoped.Eras[Math.Min(eraIndex + 1, scoped.Eras.Count - 1)];
                var pool = nextContent.Enemies.Where(enemy => enemy.Qualification >= options.RequiredQualification
                    && (!policy6 || DangerousDesignPolicy.AllowsAsBoss(enemy))).ToArray();
                if (!policy6 && pool.Length == 0) throw new InvalidOperationException("No native placeholder boss exists in the upcoming era.");
                string? configured = null;
                var named = scoped.BossBlueprints?.TryGetValue(request.Era.NativeId, out configured) == true;
                var boss = policy6 ? BossSelection.Select(scoped, eraIndex, options.RequiredQualification, request.Seed, request.Round, request.RulesVersion)
                    : named ? pool.SingleOrDefault(enemy => enemy.Blueprint.LocalId == configured)
                    ?? throw new InvalidOperationException("The configured boss is missing or has the wrong native era.")
                    : pool.OrderBy(enemy => enemy.CheckedPrice.Value).ThenBy(enemy => enemy.Blueprint.LocalId, StringComparer.Ordinal)
                        .ElementAt(request.RulesVersion >= 3 || eraIndex == scoped.Eras.Count - 1
                            ? pool.Length - 1 : pool.Length / 2);
                var minion = currentContent.Enemies.Where(enemy => enemy.Qualification >= options.RequiredQualification
                        && (!policy6 || DangerousDesignPolicy.AllowsAsReward(enemy))
                        && enemy.Blueprint.LocalId != boss.Blueprint.LocalId)
                    .OrderBy(enemy => enemy.CheckedPrice.Value).FirstOrDefault() ?? lend;
                // Keep the cost-planned current-era formation, reserving one
                // of the native twelve slots for the upcoming-era boss.
                var legacyMinions = new List<EnemyUnit>();
                var remaining = 11;
                foreach (var entry in legacy.Enemies.Where(e => e.BlueprintId != boss.Blueprint.LocalId
                    && (!policy6 || currentContent.Enemies.Any(d => d.Blueprint.LocalId == e.BlueprintId
                        && DangerousDesignPolicy.AllowsAsReward(d)))))
                {
                    var count = Math.Min(entry.Count, remaining);
                    if (count > 0) legacyMinions.Add(entry with { Count = count });
                    remaining -= count;
                }
                SpecialBattle? bossFormation = null;
                var minions = legacyMinions;
                if (policy7)
                {
                    var escortPool = currentContent.Enemies
                        .Where(enemy => enemy.Qualification >= options.RequiredQualification
                            && DangerousDesignPolicy.AllowsAsReward(enemy))
                        .ToArray();
                    var plan = BossEscortPlanner.Plan(BossEscortPlanner.CharacterFor(eraIndex),
                        boss, escortPool, legacyMinions, request.Seed, request.Round, request.OfferRoll);
                    minions = plan.Escorts.ToList();
                    bossFormation = plan.Formation;
                }
                if (minions.Count == 0)
                {
                    if (minion is null) throw new InvalidOperationException("No boss minion without a minimum risk exists in the current era.");
                    minions.Add(new EnemyUnit(minion.Blueprint.LocalId, 2 + (int)risk));
                    bossFormation = null;
                }
                enemies = minions.Prepend(new EnemyUnit(boss.Blueprint.LocalId, 1))
                    .GroupBy(e => e.BlueprintId).Select(g => new EnemyUnit(g.Key, g.Sum(e => e.Count))).ToArray();
                mods = mods.Concat(boss.Blueprint.RequiredMods).Concat(minions.SelectMany(e =>
                    currentContent.Enemies.Single(t => t.Blueprint.LocalId == e.BlueprintId).Blueprint.RequiredMods))
                    .Distinct().ToArray();
                reward = reward with { Kind = RewardKind.CapturedPrototype, Rarity = RewardRarity.UltraRare,
                    Magnitude = 1m, Duration = 6, RemainingRounds = 6, BlueprintId = boss.Blueprint.LocalId };
                extras = extras with { Reward = reward, BossBlueprintId = boss.Blueprint.LocalId,
                    BossEra = boss.Blueprint.Era.NativeId, BossCharacter = character, PlaceholderBoss = !named,
                    Special = bossFormation };
                if (policy6)
                {
                    // The fourth finite fight is explicitly the 1.15 boss step,
                    // never a value derived from the legacy 3-round modulo.
                    var bossScaled = new ScenarioValue(
                        checked(request.AvailableBudget.Value * multiplier),
                        request.AvailableBudget.UnitId);
                    extras = extras with
                    {
                        EnemyBudgetTarget = EncounterRules.ThreatTargetV6(bossScaled, 4, risk)
                    };
                }
            }
            if (policy7 && scoped.Eras.SelectMany(e => e.Enemies).Any(e => e.MaxPerBattle != null))
            {
                var capPool = currentContent.Enemies.Where(e => e.Qualification >= options.RequiredQualification
                    && (bossFight ? DangerousDesignPolicy.AllowsAsReward(e) : DangerousDesignPolicy.Allows(e, risk)))
                    .ToList();
                var bossId = extras.BossBlueprintId;
                if (bossId != null && capPool.All(e => e.Blueprint.LocalId != bossId))
                    capPool.Add(scoped.Eras.SelectMany(e => e.Enemies).First(e => e.Blueprint.LocalId == bossId));
                var target = formationTarget ?? enemies.Sum(e => capPool.Single(d => d.Blueprint.LocalId == e.BlueprintId).CheckedPrice.Value * e.Count);
                enemies = DesignCapPolicy.Apply(enemies, capPool, target, bossId);
                if (extras.Special is { } formation)
                    extras = extras with { Special = formation with { Enemies = enemies.Select(e =>
                        new SpecialUnit(e.BlueprintId, e.Count) { DeploymentRole =
                            formation.Enemies.FirstOrDefault(u => u.BlueprintId == e.BlueprintId)?.DeploymentRole
                            ?? (bossFight ? formation.Enemies.First().DeploymentRole : null)
                            ?? (EnemyDesignClassifier.PlannedRole(capPool.Single(d => d.Blueprint.LocalId == e.BlueprintId)) switch
                            { EnemyRole.Recon => "Flank", EnemyRole.Support or EnemyRole.AntiTank => "Support", _ => "Center" }) }).ToArray() } };
                mods = mods.Concat(enemies.SelectMany(e => capPool.Single(d => d.Blueprint.LocalId == e.BlueprintId).Blueprint.RequiredMods)).Distinct().ToArray();
                previousFormationCost = enemies.Sum(e => capPool.Single(d => d.Blueprint.LocalId == e.BlueprintId).CheckedPrice.Value * e.Count);
            }
            var pending = legacy with { OfferId = "", PlanHash = "", Round = request.Round,
                Enemies = enemies, RequiredMods = mods, Extras = extras };
            if (curated)
            {
                var bossMaps = currentContent.Maps.Where(m => m.Qualification >= options.RequiredQualification)
                    .OrderBy(m => m.MapId, StringComparer.Ordinal).ToArray();
                var map = bossMaps[eraIndex % bossMaps.Length];
                pending = pending with { MapId = map.MapId, SpawnLayoutId = map.SpawnLayoutIds[0],
                    RequiredMods = mods.Concat(map.RequiredMods).Distinct().ToArray() };
            }
            var identified = pending with { OfferId = PlanHasher.CreateOfferId(request.RunId, contentHash, pending) };
            result.Add(identified with { PlanHash = PlanHasher.ComputePlanHash(contentHash, identified) });
        }
        return result.ToArray();
    }

    // A copy for one non-dangerous card: designs that demand a higher risk are
    // absent, so every planner and cost target sees the pre-dangerous pool.
    private static CatalogueDefinition WithoutHigherRiskDesigns(
        CatalogueDefinition definition, EncounterRisk risk) =>
        definition with
        {
            Eras = definition.Eras.Select(era => era with
            {
                Enemies = era.Enemies
                    .Where(enemy => DangerousDesignPolicy.Allows(enemy, risk))
                    .ToArray()
            }).ToArray()
        };
}

internal static class AdventureOfferValidation
{
    internal static CatalogueValidationResult Validate(OfferRequest request, IReadOnlyList<EncounterPlan>? plans,
        CatalogueDefinition definition, CatalogueOptions options)
    {
        var errors = EncounterOfferValidator.ValidateRequest(request, definition, options).Errors.ToList();
        var curated = request.RulesVersion >= 3 && request.Block < definition.Eras.Count
            && request.Round % 4 == 0;
        if (plans?.Count != (curated ? 1 : 3)) errors.Add(curated ? "Exactly one curated boss card is required." : "Normal rounds need three cards.");
        if (errors.Count > 0) return new(errors);
        try
        {
            var expected = AdventurePlanner.Generate(request, definition, options);
            var hash = CatalogueHasher.Compute(definition);
            foreach (var risk in curated ? new[] { EncounterRisk.Standard } : Enum.GetValues<EncounterRisk>())
            {
                var actual = plans!.Where(plan => plan != null && plan.Risk == risk).ToArray();
                var target = expected.Single(plan => plan.Risk == risk);
                if (actual.Length != 1 || actual[0].OfferId != target.OfferId || actual[0].PlanHash != target.PlanHash
                    || PlanHasher.ComputePlanHash(hash, actual[0]) != target.PlanHash)
                    errors.Add($"{risk}: saved reward, boss, formation or offer identity differs from the pinned adventure plan.");
            }
        }
        catch (Exception ex) { errors.Add("Adventure content unavailable: " + ex.Message); }
        return new(errors);
    }
}
