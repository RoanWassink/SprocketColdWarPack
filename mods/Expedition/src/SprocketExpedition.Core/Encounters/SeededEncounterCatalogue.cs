using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Encounters;

// Deterministic, pure in-memory catalogue implementation of the frozen
// IEncounterCatalogue boundary. Generation never mutates native state and never
// falls back to a different era or synthetic enemy statistics.
public sealed partial class SeededEncounterCatalogue : IEncounterCatalogue
{
    public const string SeedAlgorithm = "splitmix64/v1";

    private readonly CatalogueDefinition _definition;
    private readonly CatalogueOptions _options;
    private readonly string _contentHash;
    private readonly Dictionary<(string RunId, int Round), IReadOnlyList<EncounterPlan>>
        _offersByRound = new();
    private readonly Dictionary<string, EncounterPlan> _plansById =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string RunId, int Round)> _offerRoundKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<(string RunId, int Round)> _resolvedRounds = new();
    private readonly HashSet<string> _resolvedOfferIds = new(StringComparer.Ordinal);

    public SeededEncounterCatalogue(
        CatalogueDefinition definition,
        CatalogueOptions? options = null)
    {
        _definition = definition
            ?? throw new ArgumentNullException(nameof(definition));
        _options = options ?? CatalogueOptions.Release;

        // Cost mode is only meaningful when the curated score really is the
        // native saved price. Any mismatch is an explicit construction failure,
        // never a silent fallback to count-based strength.
        if (_options.CostBasedDifficulty)
        {
            var costErrors =
                CostBasedFormationPlanner.ValidateCheckedPrices(_definition);
            if (costErrors.Count > 0)
            {
                throw new EncounterAvailabilityException(
                    EncounterAvailabilityStatus.InvalidContent,
                    costErrors);
            }
        }

        var validation = CatalogueValidator.Validate(_definition, _options);
        if (!validation.IsValid)
        {
            throw new EncounterAvailabilityException(
                EncounterAvailabilityStatus.InvalidContent,
                validation.Errors);
        }

        _contentHash = CatalogueHasher.Compute(_definition);
    }

    public CatalogueDefinition Definition => _definition;

    public CatalogueOptions Options => _options;

    // Checked hash of every content field that can affect generation.
    public string ContentHash => _contentHash;

    // Native cost profiles may run with authored minimums disabled. That permits
    // one real map and one real enemy design; it never permits synthetic content.
    // Legacy mode keeps its historical three-map/four-design gate unchanged.
    private int RequiredMapCount =>
        _options.CostBasedDifficulty && !_options.EnforceMinimumContent
            ? 1
            : EncounterRules.OfferCount;

    private int RequiredEnemyDesignCount =>
        _options.CostBasedDifficulty && !_options.EnforceMinimumContent
            ? 1
            : 4;

    public IReadOnlyList<EncounterPlan> GenerateOffers(OfferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = TryGenerateOffers(request);
        if (!result.IsSuccess)
        {
            throw new EncounterAvailabilityException(
                result.Status, result.Diagnostics);
        }

        return result.Offers;
    }

    // Non-throwing variant used by the run layer and content tools so the exact
    // availability status (including endless completion) is explicit.
    public OfferGenerationResult TryGenerateOffers(OfferRequest? request)
    {
        if (request?.RulesVersion >= 2) return AdventureOffers(request);
        if (request is null)
        {
            return OfferGenerationResult.Failure(
                EncounterAvailabilityStatus.InvalidRequest,
                "offer request is null");
        }

        var requestValidation = EncounterOfferValidator.ValidateRequest(
            request, _definition, _options);
        if (!requestValidation.IsValid)
        {
            return OfferGenerationResult.Failure(
                RequestFailureStatus(request), requestValidation.Errors);
        }

        var roundKey = (request.RunId, request.Round);

        // A resolved round must never be rerolled for new offers. Crash recovery
        // and retries use Find/RestoreOffers with the persisted exact plan.
        if (_resolvedRounds.Contains(roundKey))
        {
            return OfferGenerationResult.Failure(
                EncounterAvailabilityStatus.OfferAlreadyResolved,
                $"round {request.Round} offers were already resolved for this run");
        }

        if (_offersByRound.TryGetValue(roundKey, out var persisted))
        {
            return OfferGenerationResult.Success(persisted);
        }

        var eraContent = FindEra(request.Era.NativeId);
        if (eraContent is null)
        {
            return OfferGenerationResult.Failure(
                EncounterAvailabilityStatus.UnknownEra,
                $"native era '{request.Era.NativeId}' is not present in catalogue '{_definition.Version}'");
        }

        var maps = (eraContent.Maps ?? Array.Empty<MapEntry>())
            .Where(map => map is not null
                && map.Qualification >= _options.RequiredQualification
                && map.SpawnLayoutIds is not null
                && map.SpawnLayoutIds.Count > 0)
            .OrderBy(map => map.MapId, StringComparer.Ordinal)
            .ToList();

        if (maps.Count < RequiredMapCount)
        {
            if (RequiredMapCount == EncounterRules.OfferCount)
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.NoQualifiedMaps,
                    $"native era '{request.Era.NativeId}' has {maps.Count} qualified maps; {EncounterRules.OfferCount} are required for distinct choices");
            }

            return OfferGenerationResult.Failure(
                EncounterAvailabilityStatus.NoQualifiedMaps,
                $"native era '{request.Era.NativeId}' has {maps.Count} qualified maps; at least {RequiredMapCount} is required");
        }

        var enemies = (eraContent.Enemies ?? Array.Empty<EnemyDesignEntry>())
            .Where(enemy => enemy is not null
                && enemy.Blueprint is not null
                && enemy.Qualification >= _options.RequiredQualification
                && enemy.ThreatScore > 0m)
            .OrderBy(enemy => enemy.Blueprint.LocalId, StringComparer.Ordinal)
            .ToList();

        if (enemies.Count < RequiredEnemyDesignCount)
        {
            if (RequiredEnemyDesignCount == 4)
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.NoQualifiedEnemyDesigns,
                    $"native era '{request.Era.NativeId}' has {enemies.Count} qualified enemy designs; 4 are required");
            }

            return OfferGenerationResult.Failure(
                EncounterAvailabilityStatus.NoQualifiedEnemyDesigns,
                $"native era '{request.Era.NativeId}' has {enemies.Count} qualified enemy designs; at least {RequiredEnemyDesignCount} is required");
        }

        decimal Target(EncounterRisk risk) => _options.CostBasedDifficulty
            ? EncounterRules.CostThreatTarget(request.AvailableBudget, request.Round, risk, enemies)
            : EncounterRules.ThreatTarget(request.AvailableBudget, request.Round, risk);
        // This is the pre-adventure (rules < 2) path. A definition with at least
        // five eras owns its finite boundary; smaller one-off fixture definitions
        // keep the historical six-block rule. Adventure rules (v2+) use their own
        // era count in AdventureOffers and accept any valid schedule.
        var finiteBlocks = _definition.Eras is { Count: >= 5 } eras
            && EraSchedulePolicy.IsValidEraCount(eras.Count)
                ? eras.Count
                : EncounterRules.FiniteBlocks;
        bool endless = request.Block >= finiteBlocks;
        decimal maxFormationThreat = EncounterRules.MaxEnemies
            * enemies.Max(enemy => enemy.ThreatScore);

        foreach (var profile in EncounterRules.All)
        {
            decimal target = Target(profile.Risk);

            if (target <= 0m)
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.NoValidFormation,
                    $"{profile.Risk} threat target is not positive for budget {request.AvailableBudget.Value}");
            }

            // Endless scaling cannot invent stronger enemies. When the 12-enemy
            // cap cannot reach the requested threat target, the run is complete.
            if (endless && target > maxFormationThreat)
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.EndlessContentExhausted,
                    $"endless block {request.Block}: {profile.Risk} target threat {target} exceeds the maximum {maxFormationThreat} at the {EncounterRules.MaxEnemies}-enemy cap");
            }
        }

        // Cost mode selects the complete risk-ordered set before map/layout
        // assignment. The legacy path below is untouched when the option is off.
        CostFormationSelection? costSelection = null;
        if (_options.CostBasedDifficulty)
        {
            decimal cautiousTarget = Target(EncounterRisk.Cautious);
            decimal standardTarget = Target(EncounterRisk.Standard);
            decimal dangerousTarget = Target(EncounterRisk.Dangerous);

            if (!CostBasedFormationPlanner.TrySelect(
                enemies,
                request.Era.NativeId,
                _definition.PriceUnitId,
                cautiousTarget,
                standardTarget,
                dangerousTarget,
                request.Seed,
                request.Round,
                out costSelection,
                out string costError))
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.NoValidFormation,
                    costError);
            }
        }

        var roundRandom = StableRandom.ForRound(request.Seed, request.Round);
        int mapOffset = roundRandom.NextInt(maps.Count);

        var plans = new List<EncounterPlan>(EncounterRules.OfferCount);
        for (int cardIndex = 0; cardIndex < EncounterRules.OfferCount; cardIndex++)
        {
            var profile = EncounterRules.Profile((EncounterRisk)cardIndex);
            var map = maps[(mapOffset + cardIndex) % maps.Count];
            var cardRandom = new StableRandom(
                request.Seed, request.Round, cardIndex);
            string layout = map.SpawnLayoutIds[
                cardRandom.NextInt(map.SpawnLayoutIds.Count)];

            decimal target = Target(profile.Risk);
            IReadOnlyList<FormationUnit>? formation;
            if (costSelection is not null)
            {
                var selected = cardIndex switch
                {
                    0 => costSelection.Cautious,
                    1 => costSelection.Standard,
                    _ => costSelection.Dangerous
                };
                formation = selected
                    .Select(unit => new FormationUnit(unit.Design, unit.Count))
                    .ToArray();
            }
            else
            {
                formation = BuildFormation(
                    enemies, target, cardRandom, cardIndex);
            }

            if (formation is null)
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.NoValidFormation,
                    $"{profile.Risk} risk cannot form a non-empty formation within target threat {target} for era '{request.Era.NativeId}'");
            }

            if (!TryBuildRequiredMods(
                map,
                formation.Select(unit => unit.Design),
                out var requiredMods,
                out string modError))
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.InvalidContent,
                    modError);
            }

            var enemiesForPlan = formation
                .Select(unit => new EnemyUnit(
                    unit.Design.Blueprint.LocalId, unit.Count))
                .ToArray();

            var draft = new EncounterPlan(
                string.Empty,
                string.Empty,
                request.Round,
                map.MapId,
                layout,
                request.Era,
                profile.Risk,
                enemiesForPlan,
                profile.SupplyReward,
                requiredMods,
                _definition.Version);

            string offerId = PlanHasher.CreateOfferId(
                request.RunId, _contentHash, draft);
            var identified = draft with { OfferId = offerId };
            var plan = identified with
            {
                PlanHash = PlanHasher.ComputePlanHash(_contentHash, identified)
            };

            plans.Add(plan);
        }

        // Atomic set validation: nothing is registered unless all three cards
        // are valid together (one per risk, distinct, affordable, era-correct).
        var setValidation = EncounterOfferValidator.ValidateSet(
            request, plans, _definition, _options);
        if (!setValidation.IsValid)
        {
            return OfferGenerationResult.Failure(
                EncounterAvailabilityStatus.InvalidContent,
                setValidation.Errors);
        }

        var offerArray = plans.ToArray();
        foreach (var plan in offerArray)
        {
            if (_plansById.ContainsKey(plan.OfferId))
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.InvalidContent,
                    $"generated offer id '{plan.OfferId}' already exists in this catalogue instance");
            }
        }

        Register(roundKey, offerArray);
        return OfferGenerationResult.Success(offerArray);
    }

    public EncounterPlan? Find(string offerId)
    {
        if (string.IsNullOrEmpty(offerId))
        {
            return null;
        }

        return _plansById.TryGetValue(offerId, out var plan) ? plan : null;
    }

    // Restores an already persisted offer set after restart or crash recovery.
    // The set is validated atomically and exact replay wins over regeneration.
    public OfferGenerationResult RestoreOffers(
        OfferRequest? request,
        IReadOnlyList<EncounterPlan>? persisted)
    {
        if (request?.RulesVersion >= 2) return RestoreAdventure(request, persisted);
        if (request is null)
        {
            return OfferGenerationResult.Failure(
                EncounterAvailabilityStatus.InvalidRequest,
                "offer request is null");
        }

        var requestValidation = EncounterOfferValidator.ValidateRequest(
            request, _definition, _options);
        if (!requestValidation.IsValid)
        {
            return OfferGenerationResult.Failure(
                RequestFailureStatus(request), requestValidation.Errors);
        }

        if (persisted is null || persisted.Count == 0)
        {
            return OfferGenerationResult.Failure(
                EncounterAvailabilityStatus.InvalidPersistedOffers,
                "persisted offer set is null or empty");
        }

        var setValidation = EncounterOfferValidator.ValidateSet(
            request, persisted, _definition, _options);
        if (!setValidation.IsValid)
        {
            return OfferGenerationResult.Failure(
                EncounterAvailabilityStatus.InvalidPersistedOffers,
                setValidation.Errors);
        }

        var restored = persisted.ToArray();
        var roundKey = (request.RunId, request.Round);

        if (_offersByRound.TryGetValue(roundKey, out var existing))
        {
            if (!SameOfferSet(existing, restored))
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.InvalidPersistedOffers,
                    $"persisted offers conflict with already registered offers for round {request.Round}");
            }

            return OfferGenerationResult.Success(existing);
        }

        foreach (var plan in restored)
        {
            if (_plansById.TryGetValue(plan.OfferId, out var existingPlan)
                && !SamePlan(existingPlan, plan))
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.InvalidPersistedOffers,
                    $"persisted offer id '{plan.OfferId}' is already registered with a different payload");
            }

            if (_offerRoundKeys.TryGetValue(plan.OfferId, out var existingKey)
                && existingKey != roundKey)
            {
                return OfferGenerationResult.Failure(
                    EncounterAvailabilityStatus.InvalidPersistedOffers,
                    $"persisted offer id '{plan.OfferId}' belongs to a different run/round");
            }
        }

        Register(roundKey, restored);
        return OfferGenerationResult.Success(restored);
    }

    // Marks the chosen offer's round resolved. The offer must already be known
    // through GenerateOffers or RestoreOffers; an unknown id is rejected so the
    // catalogue never records a resolved round without its exact validated plan.
    // No new offers may be generated for that round; the exact plans remain
    // reachable through Find for retries.
    public bool MarkResolved(string? offerId)
    {
        if (string.IsNullOrEmpty(offerId)
            || !_plansById.ContainsKey(offerId)
            || !_offerRoundKeys.TryGetValue(offerId, out var roundKey))
        {
            return false;
        }

        _resolvedOfferIds.Add(offerId);
        _resolvedRounds.Add(roundKey);
        return true;
    }

    public bool IsResolved(string? offerId) =>
        offerId is not null && _resolvedOfferIds.Contains(offerId);

    private EncounterAvailabilityStatus RequestFailureStatus(
        OfferRequest request)
    {
        if (!string.Equals(
            request.CatalogueVersion,
            _definition.Version,
            StringComparison.Ordinal))
        {
            return EncounterAvailabilityStatus.CatalogueVersionMismatch;
        }

        return EncounterAvailabilityStatus.InvalidRequest;
    }

    private CatalogueEraContent? FindEra(string? eraId)
    {
        if (eraId is null)
        {
            return null;
        }

        return _definition.Eras.FirstOrDefault(
            era => era?.Era is not null
                && string.Equals(
                    era.Era.NativeId, eraId, StringComparison.Ordinal));
    }

    // Picks a formation from qualified era content only. The anchor is rotated
    // by card index so the three risk cards differ in roster even when their
    // threat ceilings happen to be similar. No count ever exceeds the cap and no
    // enemy is added once the curated threat budget is exhausted.
    private static IReadOnlyList<FormationUnit>? BuildFormation(
        IReadOnlyList<EnemyDesignEntry> pool,
        decimal target,
        StableRandom random,
        int cardIndex)
    {
        int poolCount = pool.Count;
        int offset = random.NextInt(poolCount);

        var ordered = new EnemyDesignEntry[poolCount];
        for (int index = 0; index < poolCount; index++)
        {
            ordered[index] = pool[(offset + cardIndex + index) % poolCount];
        }

        EnemyDesignEntry? anchor = null;
        foreach (var design in ordered)
        {
            if (design.ThreatScore <= target)
            {
                anchor = design;
                break;
            }
        }

        if (anchor is null)
        {
            return null;
        }

        int maxAnchorCount = (int)Math.Min(
            (decimal)EncounterRules.MaxEnemies,
            decimal.Floor(target / anchor.ThreatScore));
        if (maxAnchorCount < 1)
        {
            return null;
        }

        int anchorCount = 1 + random.NextInt(maxAnchorCount);
        var counts = new SortedDictionary<string, (EnemyDesignEntry Design, int Count)>(
            StringComparer.Ordinal)
        {
            [anchor.Blueprint.LocalId] = (anchor, anchorCount)
        };

        decimal spentThreat = anchor.ThreatScore * anchorCount;
        int totalCount = anchorCount;

        foreach (var design in ordered)
        {
            if (totalCount >= EncounterRules.MaxEnemies)
            {
                break;
            }

            string blueprintId = design.Blueprint.LocalId;
            if (counts.ContainsKey(blueprintId))
            {
                continue;
            }

            decimal remaining = target - spentThreat;
            if (remaining <= 0m)
            {
                break;
            }

            int affordable = (int)Math.Min(
                (decimal)(EncounterRules.MaxEnemies - totalCount),
                decimal.Floor(remaining / design.ThreatScore));
            if (affordable <= 0)
            {
                continue;
            }

            int add = 1 + random.NextInt(affordable);
            counts[blueprintId] = (design, add);
            spentThreat += design.ThreatScore * add;
            totalCount += add;

            if (random.NextInt(4) == 0)
            {
                break;
            }
        }

        return counts.Values
            .Select(pair => new FormationUnit(pair.Design, pair.Count))
            .ToArray();
    }

    private static bool TryBuildRequiredMods(
        MapEntry map,
        IEnumerable<EnemyDesignEntry> designs,
        out IReadOnlyList<ModRequirement> mods,
        out string error)
    {
        var versionsById = new Dictionary<string, string>(StringComparer.Ordinal);

        var all = new List<ModRequirement>();
        all.AddRange(map.RequiredMods ?? Array.Empty<ModRequirement>());
        foreach (var design in designs)
        {
            all.AddRange(
                design.Blueprint.RequiredMods ?? Array.Empty<ModRequirement>());
        }

        foreach (var mod in all)
        {
            if (mod is null)
            {
                continue;
            }

            if (versionsById.TryGetValue(mod.Id, out var existing)
                && !string.Equals(existing, mod.Version, StringComparison.Ordinal))
            {
                mods = Array.Empty<ModRequirement>();
                error = $"selected content requires conflicting versions of mod '{mod.Id}'";
                return false;
            }

            versionsById[mod.Id] = mod.Version;
        }

        mods = versionsById
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ModRequirement(pair.Key, pair.Value))
            .ToArray();
        error = string.Empty;
        return true;
    }

    private void Register(
        (string RunId, int Round) roundKey,
        IReadOnlyList<EncounterPlan> offers)
    {
        _offersByRound[roundKey] = offers;
        foreach (var plan in offers)
        {
            _plansById[plan.OfferId] = plan;
            _offerRoundKeys[plan.OfferId] = roundKey;
        }
    }

    private static bool SamePlan(EncounterPlan left, EncounterPlan right) =>
        string.Equals(left.OfferId, right.OfferId, StringComparison.Ordinal)
        && string.Equals(left.PlanHash, right.PlanHash, StringComparison.Ordinal);

    private static bool SameOfferSet(
        IReadOnlyList<EncounterPlan> left,
        IReadOnlyList<EncounterPlan> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var leftKeys = left
            .Select(plan => plan.OfferId + "|" + plan.PlanHash)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var rightKeys = right
            .Select(plan => plan.OfferId + "|" + plan.PlanHash)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        return leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
    }

    private sealed record FormationUnit(EnemyDesignEntry Design, int Count);
}
