namespace SprocketExpedition.Core.Encounters;

// Validates a complete offer set atomically. Callers must reject the whole set
// if any rule fails; partial offers are never registered or shown.
public static class EncounterOfferValidator
{
    public static CatalogueValidationResult ValidateRequest(
        OfferRequest? request,
        CatalogueDefinition? definition,
        CatalogueOptions? options = null)
    {
        var errors = new List<string>();
        var opts = options ?? CatalogueOptions.Release;

        if (request is null)
        {
            errors.Add("offer request is null");
            return new CatalogueValidationResult(errors);
        }

        if (definition is null)
        {
            errors.Add("catalogue definition is null");
            return new CatalogueValidationResult(errors);
        }

        if (!ContentSafety.IsSafeRunId(request.RunId))
        {
            errors.Add("offer request: run id is empty or contains control characters");
        }

        if (request.RulesVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7)) errors.Add("Unsupported encounter rules version.");
        if (request.TestPool && (request.RulesVersion < 6 || request.TestPoolOffset < 0 || definition.TestPool == null))
            errors.Add("Test pool request requires schema 6 and a pinned pool.");
        if (request.Round < 1)
        {
            errors.Add("offer request: round must be at least one");
        }
        else
        {
            int expectedBlock = request.RulesVersion >= 2 ? (request.Round - 1) / 4 : EncounterRules.BlockForRound(request.Round);
            if (request.Block != expectedBlock)
            {
                errors.Add(
                    $"offer request: block {request.Block} does not match round {request.Round} (expected {expectedBlock})");
            }
        }

        if (!ContentSafety.IsSafeLocalReference(request.Era?.NativeId))
        {
            errors.Add("offer request: native era is not a safe local reference");
        }

        if (request.AvailableBudget is null)
        {
            errors.Add("offer request: available budget is null");
        }
        else
        {
            if (request.AvailableBudget.Value <= 0m)
            {
                errors.Add("offer request: available budget must be positive");
            }

            if (!ContentSafety.IsSafeLocalReference(request.AvailableBudget.UnitId))
            {
                errors.Add("offer request: available budget unit is not a safe local reference");
            }
            else if (!string.Equals(
                request.AvailableBudget.UnitId,
                definition.PriceUnitId,
                StringComparison.Ordinal))
            {
                errors.Add(
                    $"offer request: budget unit '{request.AvailableBudget.UnitId}' does not match catalogue price unit '{definition.PriceUnitId}'");
            }
        }

        if (request.PendingSupplyPoints < 0)
        {
            errors.Add("offer request: pending supply points cannot be negative");
        }

        if (!string.Equals(
            request.CatalogueVersion,
            definition.Version,
            StringComparison.Ordinal))
        {
            errors.Add(
                $"offer request: catalogue version '{request.CatalogueVersion}' does not match loaded catalogue '{definition.Version}'");
        }

        if (!Enum.IsDefined(
            typeof(ContentQualification), opts.RequiredQualification))
        {
            errors.Add("offer request: required qualification value is not defined");
        }

        return new CatalogueValidationResult(errors);
    }

    public static CatalogueValidationResult ValidateSet(
        OfferRequest? request,
        IReadOnlyList<EncounterPlan>? plans,
        CatalogueDefinition? definition,
        CatalogueOptions? options = null)
    {
        if (request?.RulesVersion >= 2 && definition != null)
            return AdventureOfferValidation.Validate(request, plans, definition, options ?? CatalogueOptions.Release);
        var errors = new List<string>();
        var opts = options ?? CatalogueOptions.Release;

        var requestResult = ValidateRequest(request, definition, opts);
        errors.AddRange(requestResult.Errors);

        if (plans is null)
        {
            errors.Add("offer set is null");
            return new CatalogueValidationResult(errors);
        }

        if (plans.Count != EncounterRules.OfferCount)
        {
            errors.Add(
                $"offer set must contain exactly {EncounterRules.OfferCount} offers, found {plans.Count}");
        }

        if (request is null || definition is null)
        {
            return new CatalogueValidationResult(errors);
        }

        string contentHash = CatalogueHasher.Compute(definition);
        var eraContent = FindEra(definition, request.Era?.NativeId);

        int[] riskCounts = new int[3];
        var offerIds = new HashSet<string>(StringComparer.Ordinal);

        for (int index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            if (plan is null)
            {
                errors.Add($"offer[{index}] is null");
                continue;
            }

            string planContext = string.IsNullOrEmpty(plan.OfferId)
                ? $"offer[{index}]"
                : $"offer '{plan.OfferId}'";

            if (!ContentSafety.IsSafeRunId(request.RunId))
            {
                // Already reported by ValidateRequest; skip run-dependent checks.
            }
            else if (!PlanHasher.IsSafeOfferId(plan.OfferId))
            {
                errors.Add($"{planContext}: offer id is not a safe generated identifier");
            }
            else if (!offerIds.Add(plan.OfferId))
            {
                errors.Add($"{planContext}: duplicate offer id");
            }

            if (plan.Round != request.Round)
            {
                errors.Add(
                    $"{planContext}: round {plan.Round} does not match request round {request.Round}");
            }

            if (!string.Equals(
                plan.Era?.NativeId,
                request.Era?.NativeId,
                StringComparison.Ordinal))
            {
                errors.Add($"{planContext}: native era does not match request era");
            }

            if (!string.Equals(
                plan.CatalogueVersion,
                definition.Version,
                StringComparison.Ordinal))
            {
                errors.Add($"{planContext}: catalogue version does not match loaded catalogue");
            }

            bool riskDefined = EncounterRules.IsDefinedRisk(plan.Risk);
            if (!riskDefined)
            {
                errors.Add($"{planContext}: encounter risk is not defined");
            }
            else
            {
                riskCounts[(int)plan.Risk]++;
                int approvedSupply = EncounterRules.Profile(plan.Risk).SupplyReward;
                if (plan.SupplyReward != approvedSupply)
                {
                    errors.Add(
                        $"{planContext}: supply reward {plan.SupplyReward} is not approved for {plan.Risk} (expected {approvedSupply})");
                }
            }

            MapEntry? resolvedMap = null;
            if (eraContent is null)
            {
                errors.Add($"{planContext}: native era is not present in the catalogue");
            }
            else
            {
                resolvedMap = FindMap(eraContent, plan.MapId);
                if (resolvedMap is null)
                {
                    errors.Add(
                        $"{planContext}: map '{plan.MapId}' is not a resolved map for the requested native era");
                }
                else
                {
                    if (resolvedMap.Qualification < opts.RequiredQualification)
                    {
                        errors.Add(
                            $"{planContext}: map '{plan.MapId}' is below the required qualification");
                    }

                    if (!string.Equals(
                        resolvedMap.Era?.NativeId,
                        plan.Era?.NativeId,
                        StringComparison.Ordinal))
                    {
                        errors.Add($"{planContext}: map native era does not match plan era");
                    }

                    var layouts = resolvedMap.SpawnLayoutIds
                        ?? Array.Empty<string>();
                    if (!layouts.Contains(plan.SpawnLayoutId, StringComparer.Ordinal))
                    {
                        errors.Add(
                            $"{planContext}: spawn layout '{plan.SpawnLayoutId}' is not part of map '{plan.MapId}'");
                    }
                }
            }

            var expectedMods = new Dictionary<string, string>(StringComparer.Ordinal);
            bool expectedModsConflict = false;

            if (resolvedMap is not null)
            {
                AddExpectedMods(resolvedMap.RequiredMods, expectedMods,
                    ref expectedModsConflict, planContext, errors);
            }

            decimal formationThreat = 0m;
            long totalEnemies = 0;
            bool threatResolved = true;
            var seenBlueprints = new HashSet<string>(StringComparer.Ordinal);

            if (plan.Enemies is null || plan.Enemies.Count == 0)
            {
                errors.Add($"{planContext}: enemy formation is empty");
                threatResolved = false;
            }
            else
            {
                foreach (var enemy in plan.Enemies)
                {
                    if (enemy is null)
                    {
                        errors.Add($"{planContext}: enemy entry is null");
                        threatResolved = false;
                        continue;
                    }

                    if (!ContentSafety.IsSafeLocalReference(enemy.BlueprintId))
                    {
                        errors.Add(
                            $"{planContext}: enemy blueprint id is not a safe local reference");
                        threatResolved = false;
                        continue;
                    }

                    if (!seenBlueprints.Add(enemy.BlueprintId))
                    {
                        errors.Add(
                            $"{planContext}: duplicate blueprint '{enemy.BlueprintId}' in formation");
                    }

                    if (enemy.Count <= 0)
                    {
                        errors.Add(
                            $"{planContext}: enemy count for '{enemy.BlueprintId}' must be positive");
                        threatResolved = false;
                        continue;
                    }

                    totalEnemies += enemy.Count;

                    EnemyDesignEntry? design = eraContent is null
                        ? null
                        : FindEnemy(eraContent, enemy.BlueprintId);
                    if (design is null)
                    {
                        errors.Add(
                            $"{planContext}: blueprint '{enemy.BlueprintId}' is unresolved for the requested native era");
                        threatResolved = false;
                        continue;
                    }

                    if (design.Qualification < opts.RequiredQualification)
                    {
                        errors.Add(
                            $"{planContext}: blueprint '{enemy.BlueprintId}' is below the required qualification");
                    }

                    formationThreat += design.ThreatScore * enemy.Count;
                    AddExpectedMods(
                        design.Blueprint?.RequiredMods,
                        expectedMods,
                        ref expectedModsConflict,
                        planContext,
                        errors);
                }
            }

            if (totalEnemies < 1 || totalEnemies > EncounterRules.MaxEnemies)
            {
                errors.Add(
                    $"{planContext}: total enemy count {totalEnemies} must be between 1 and {EncounterRules.MaxEnemies}");
            }

            if (threatResolved && formationThreat <= 0m)
            {
                errors.Add($"{planContext}: formation threat must be positive");
            }

            if (riskDefined
                && request.Round >= 1
                && request.AvailableBudget is not null
                && request.AvailableBudget.Value > 0m
                && threatResolved)
            {
                decimal target = EncounterRules.ThreatTarget(
                    request.AvailableBudget, request.Round, plan.Risk);
                if (opts.CostBasedDifficulty && eraContent != null)
                    target = EncounterRules.CostThreatTarget(request.AvailableBudget, request.Round, plan.Risk,
                        eraContent.Enemies.Where(e => e.Qualification >= opts.RequiredQualification));
                if (target <= 0m)
                {
                    errors.Add($"{planContext}: threat target is not positive");
                }
                else if (formationThreat > target)
                {
                    errors.Add(
                        $"{planContext}: formation threat {formationThreat} exceeds target {target}");
                }
            }

            var planMods = new Dictionary<string, string>(StringComparer.Ordinal);
            bool planModsValid = true;
            if (plan.RequiredMods is null)
            {
                errors.Add($"{planContext}: required mods list is null");
                planModsValid = false;
            }
            else
            {
                foreach (var mod in plan.RequiredMods)
                {
                    if (mod is null)
                    {
                        errors.Add($"{planContext}: required mod entry is null");
                        planModsValid = false;
                        continue;
                    }

                    if (!ContentSafety.IsSafeLocalReference(mod.Id))
                    {
                        errors.Add(
                            $"{planContext}: required mod id is not a safe local reference");
                        planModsValid = false;
                    }

                    if (!ContentSafety.IsSafeVersion(mod.Version))
                    {
                        errors.Add(
                            $"{planContext}: required mod version is not a safe version token");
                        planModsValid = false;
                    }

                    if (planMods.TryGetValue(mod.Id, out var existing)
                        && !string.Equals(existing, mod.Version, StringComparison.Ordinal))
                    {
                        errors.Add(
                            $"{planContext}: conflicting versions listed for mod '{mod.Id}'");
                        planModsValid = false;
                    }
                    else
                    {
                        planMods[mod.Id] = mod.Version;
                    }
                }
            }

            if (expectedModsConflict)
            {
                errors.Add(
                    $"{planContext}: selected content requires conflicting mod versions");
            }

            if (planModsValid)
            {
                foreach (var pair in expectedMods)
                {
                    if (!planMods.TryGetValue(pair.Key, out var version))
                    {
                        errors.Add(
                            $"{planContext}: required mod '{pair.Key}' is missing from the plan union");
                    }
                    else if (!string.Equals(version, pair.Value, StringComparison.Ordinal))
                    {
                        errors.Add(
                            $"{planContext}: required mod '{pair.Key}' expects version '{pair.Value}', plan lists '{version}'");
                    }
                }

                foreach (var pair in planMods)
                {
                    if (!expectedMods.ContainsKey(pair.Key))
                    {
                        errors.Add(
                            $"{planContext}: mod '{pair.Key}' is not part of the selected content union");
                    }
                }
            }

            if (ContentSafety.IsSafeRunId(request.RunId))
            {
                string expectedOfferId = PlanHasher.CreateOfferId(
                    request.RunId, contentHash, plan);
                if (!string.Equals(
                    expectedOfferId, plan.OfferId, StringComparison.Ordinal))
                {
                    errors.Add(
                        $"{planContext}: offer id is not tied to the plan payload");
                }
            }

            string expectedPlanHash = PlanHasher.ComputePlanHash(contentHash, plan);
            if (!string.Equals(expectedPlanHash, plan.PlanHash, StringComparison.Ordinal))
            {
                errors.Add($"{planContext}: plan hash does not match the plan payload");
            }
        }

        for (int riskIndex = 0; riskIndex < riskCounts.Length; riskIndex++)
        {
            if (riskCounts[riskIndex] != 1)
            {
                var risk = (EncounterRisk)riskIndex;
                errors.Add(
                    $"offer set must contain exactly one {risk} offer, found {riskCounts[riskIndex]}");
            }
        }

        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            if (plan is null)
            {
                continue;
            }

            string signature = BuildDistinctnessSignature(plan);
            if (!signatures.TryAdd(signature, plan.OfferId ?? "<unknown>"))
            {
                errors.Add(
                    $"offers '{signatures[signature]}' and '{plan.OfferId}' are not meaningfully distinct in roster, map and layout");
            }
        }

        return new CatalogueValidationResult(errors);
    }

    public static decimal ComputeFormationThreat(
        CatalogueEraContent era,
        IReadOnlyList<EnemyUnit> enemies)
    {
        ArgumentNullException.ThrowIfNull(era);
        ArgumentNullException.ThrowIfNull(enemies);

        var byId = new Dictionary<string, EnemyDesignEntry>(StringComparer.Ordinal);
        foreach (var design in era.Enemies ?? Array.Empty<EnemyDesignEntry>())
        {
            if (design?.Blueprint is null)
            {
                continue;
            }

            byId[design.Blueprint.LocalId] = design;
        }

        decimal threat = 0m;
        foreach (var enemy in enemies)
        {
            if (enemy is null)
            {
                throw new ArgumentException("Formation contains a null enemy.", nameof(enemies));
            }

            if (!byId.TryGetValue(enemy.BlueprintId, out var design))
            {
                throw new ArgumentException(
                    $"Unresolved blueprint '{enemy.BlueprintId}'.", nameof(enemies));
            }

            if (enemy.Count <= 0)
            {
                throw new ArgumentException(
                    $"Enemy count for '{enemy.BlueprintId}' must be positive.",
                    nameof(enemies));
            }

            threat += design.ThreatScore * enemy.Count;
        }

        return threat;
    }

    private static CatalogueEraContent? FindEra(
        CatalogueDefinition definition,
        string? eraId)
    {
        if (eraId is null || definition.Eras is null)
        {
            return null;
        }

        return definition.Eras.FirstOrDefault(
            era => era?.Era is not null
                && string.Equals(era.Era.NativeId, eraId, StringComparison.Ordinal));
    }

    private static MapEntry? FindMap(CatalogueEraContent era, string? mapId)
    {
        if (mapId is null || era.Maps is null)
        {
            return null;
        }

        return era.Maps.FirstOrDefault(
            map => map is not null
                && string.Equals(map.MapId, mapId, StringComparison.Ordinal));
    }

    private static EnemyDesignEntry? FindEnemy(
        CatalogueEraContent era,
        string blueprintId)
    {
        if (era.Enemies is null)
        {
            return null;
        }

        return era.Enemies.FirstOrDefault(
            enemy => enemy?.Blueprint is not null
                && string.Equals(
                    enemy.Blueprint.LocalId,
                    blueprintId,
                    StringComparison.Ordinal));
    }

    private static void AddExpectedMods(
        IReadOnlyList<ModRequirement>? mods,
        Dictionary<string, string> expected,
        ref bool conflict,
        string context,
        List<string> errors)
    {
        if (mods is null)
        {
            errors.Add($"{context}: content required mods list is null");
            conflict = true;
            return;
        }

        foreach (var mod in mods)
        {
            if (mod is null)
            {
                errors.Add($"{context}: content required mod entry is null");
                conflict = true;
                continue;
            }

            if (expected.TryGetValue(mod.Id, out var existing)
                && !string.Equals(existing, mod.Version, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{context}: selected content requires conflicting versions of mod '{mod.Id}'");
                conflict = true;
            }
            else
            {
                expected[mod.Id] = mod.Version;
            }
        }
    }

    private static string BuildDistinctnessSignature(EncounterPlan plan)
    {
        var roster = (plan.Enemies ?? Array.Empty<EnemyUnit>())
            .Where(enemy => enemy is not null)
            .OrderBy(enemy => enemy.BlueprintId, StringComparer.Ordinal)
            .ThenBy(enemy => enemy.Count)
            .Select(enemy => $"{enemy.BlueprintId}x{enemy.Count}")
            .ToArray();

        return string.Join(
            "\u001f",
            new[]
            {
                plan.MapId ?? string.Empty,
                plan.SpawnLayoutId ?? string.Empty,
                string.Join(",", roster)
            });
    }
}
