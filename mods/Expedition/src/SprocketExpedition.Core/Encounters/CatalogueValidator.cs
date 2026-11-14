namespace SprocketExpedition.Core.Encounters;

// Structural and evidence validation for an entire catalogue. Release defaults
// require at least three qualified maps and four qualified enemy designs per
// native era. Fixture catalogues are allowed only when the caller explicitly
// lowers RequiredQualification, and they can never claim qualified content.
public static class CatalogueValidator
{
    public static CatalogueValidationResult Validate(
        CatalogueDefinition? definition,
        CatalogueOptions? options = null)
    {
        var errors = new List<string>();
        var opts = options ?? CatalogueOptions.Release;

        if (definition is null)
        {
            errors.Add("catalogue: definition is null");
            return new CatalogueValidationResult(errors);
        }

        if (!ContentSafety.IsSafeVersion(definition.Version))
        {
            errors.Add("catalogue: version is not a safe version token");
        }

        if (string.IsNullOrWhiteSpace(definition.Label)
            || definition.Label.Length > ContentSafety.MaxReferenceLength)
        {
            errors.Add("catalogue: label must be a non-empty local label");
        }

        if (definition.IsFixture
            && !string.Equals(
                definition.Label,
                CatalogueDefinition.FixtureLabel,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"catalogue: fixture content must be labelled exactly '{CatalogueDefinition.FixtureLabel}'");
        }

        if (definition.IsFixture
            && opts.RequiredQualification == ContentQualification.Qualified)
        {
            errors.Add(
                "catalogue: fixture content cannot satisfy release/live qualification");
        }

        if (!ContentSafety.IsSafeLocalReference(definition.PriceUnitId))
        {
            errors.Add("catalogue: price unit id is not a safe local reference");
        }

        if (definition.Eras is null || definition.Eras.Count == 0)
        {
            errors.Add("catalogue: at least one native era entry is required");
            return new CatalogueValidationResult(errors);
        }

        if (definition.TestPool != null)
        {
            var ids = new HashSet<string>(definition.Eras.SelectMany(e => e.Enemies).Select(e => e.Blueprint.LocalId), StringComparer.Ordinal);
            if (definition.TestPool.Count > 4096) errors.Add("Test pool exceeds 4096 designs.");
            foreach (var tank in definition.TestPool)
            {
                if (tank == null || tank.Design?.Blueprint?.Era == null || string.IsNullOrWhiteSpace(tank.Name))
                { errors.Add("Invalid test pool tank metadata."); continue; }
                if (!ids.Add(tank.Design.Blueprint.LocalId)) errors.Add("Duplicate test pool identity.");
                errors.AddRange(Validate(definition with { TestPool = null, BossBlueprints = null, BossCandidates = null,
                    Eras = new[] { new CatalogueEraContent(tank.Design.Blueprint.Era, Array.Empty<MapEntry>(), new[] { tank.Design }) } },
                    opts with { EnforceMinimumContent = false }).Errors);
                if (tank.Design.CheckedPrice is not { Value: > 0 } price || tank.Design.ThreatScore != price.Value)
                    errors.Add("Test pool requires positive measured native prices.");
            }
        }
        if (definition.BossCandidates != null)
            foreach (var pair in definition.BossCandidates)
            {
                var index = definition.Eras.ToList().FindIndex(e => e.Era?.NativeId == pair.Key);
                if (index < 0 || pair.Value == null || pair.Value.Count < 2)
                { errors.Add("Invalid boss candidate era or list."); continue; }
                var next = definition.Eras[Math.Min(index + 1, definition.Eras.Count - 1)];
                if (pair.Value.Distinct(StringComparer.Ordinal).Count() != pair.Value.Count
                    || pair.Value.Any(id => !next.Enemies.Any(e => e.Blueprint.LocalId == id
                        && e.Qualification >= opts.RequiredQualification && DangerousDesignPolicy.AllowsAsBoss(e))))
                    errors.Add("Boss candidates must be distinct eligible upcoming-era designs.");
            }
        var eraIds = new HashSet<string>(StringComparer.Ordinal);

        for (int eraIndex = 0; eraIndex < definition.Eras.Count; eraIndex++)
        {
            var era = definition.Eras[eraIndex];
            if (era is null)
            {
                errors.Add($"catalogue: era[{eraIndex}] is null");
                continue;
            }

            string? eraId = era.Era?.NativeId;
            if (!ContentSafety.IsSafeLocalReference(eraId))
            {
                errors.Add(
                    $"catalogue: era[{eraIndex}] native id is not a safe local reference");
                eraId = null;
            }
            else if (!eraIds.Add(eraId!))
            {
                errors.Add($"catalogue: duplicate native era id '{eraId}'");
            }

            string context = eraId is null
                ? $"catalogue era[{eraIndex}]"
                : $"catalogue era '{eraId}'";

            int eligibleMaps = 0;
            if (era.Maps is null)
            {
                errors.Add($"{context}: maps list is null");
            }
            else
            {
                var mapIds = new HashSet<string>(StringComparer.Ordinal);
                for (int mapIndex = 0; mapIndex < era.Maps.Count; mapIndex++)
                {
                    var map = era.Maps[mapIndex];
                    if (map is null)
                    {
                        errors.Add($"{context}: map[{mapIndex}] is null");
                        continue;
                    }

                    bool structurallyValid = true;
                    string mapContext = $"{context} map '{map.MapId}'";

                    if (!ContentSafety.IsSafeLocalReference(map.MapId))
                    {
                        errors.Add($"{mapContext}: map id is not a safe local reference");
                        structurallyValid = false;
                    }
                    else if (!mapIds.Add(map.MapId))
                    {
                        errors.Add($"{mapContext}: duplicate map id");
                        structurallyValid = false;
                    }

                    if (!ContentSafety.IsHexHash(map.ContentHash))
                    {
                        errors.Add($"{mapContext}: content hash must be 64 hex characters");
                        structurallyValid = false;
                    }

                    if (eraId is not null
                        && !string.Equals(
                            map.Era?.NativeId, eraId, StringComparison.Ordinal))
                    {
                        errors.Add($"{mapContext}: map native era does not match its era entry");
                        structurallyValid = false;
                    }

                    if (map.SpawnLayoutIds is null || map.SpawnLayoutIds.Count == 0)
                    {
                        errors.Add($"{mapContext}: at least one spawn layout is required");
                        structurallyValid = false;
                    }
                    else
                    {
                        var layoutIds = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var layout in map.SpawnLayoutIds)
                        {
                            if (!ContentSafety.IsSafeLocalReference(layout))
                            {
                                errors.Add(
                                    $"{mapContext}: spawn layout id is not a safe local reference");
                                structurallyValid = false;
                            }
                            else if (!layoutIds.Add(layout))
                            {
                                errors.Add($"{mapContext}: duplicate spawn layout id '{layout}'");
                                structurallyValid = false;
                            }
                        }
                    }

                    if (!ValidateMods(map.RequiredMods, mapContext, errors))
                    {
                        structurallyValid = false;
                    }

                    if (!Enum.IsDefined(typeof(ContentQualification), map.Qualification))
                    {
                        errors.Add($"{mapContext}: qualification value is not defined");
                        structurallyValid = false;
                    }
                    else
                    {
                        if (definition.IsFixture
                            && map.Qualification == ContentQualification.Qualified)
                        {
                            errors.Add(
                                $"{mapContext}: fixture content cannot claim qualified status");
                        }

                        if (structurallyValid
                            && map.Qualification >= opts.RequiredQualification)
                        {
                            eligibleMaps++;
                        }
                    }
                }
            }

            if (opts.EnforceMinimumContent && eligibleMaps < 3)
            {
                errors.Add(
                    $"{context}: needs at least 3 qualified maps, found {eligibleMaps}");
            }

            int eligibleEnemies = 0;
            if (era.Enemies is null)
            {
                errors.Add($"{context}: enemies list is null");
            }
            else
            {
                var blueprintIds = new HashSet<string>(StringComparer.Ordinal);
                for (int enemyIndex = 0; enemyIndex < era.Enemies.Count; enemyIndex++)
                {
                    var enemy = era.Enemies[enemyIndex];
                    if (enemy is null)
                    {
                        errors.Add($"{context}: enemy[{enemyIndex}] is null");
                        continue;
                    }

                    var blueprint = enemy.Blueprint;
                    string blueprintId = blueprint?.LocalId ?? "<invalid>";
                    string enemyContext = $"{context} enemy '{blueprintId}'";
                    bool structurallyValid = true;

                    if (blueprint is null)
                    {
                        errors.Add($"{context}: enemy[{enemyIndex}] blueprint is null");
                        continue;
                    }

                    if (!ContentSafety.IsSafeLocalReference(blueprint.LocalId))
                    {
                        errors.Add(
                            $"{enemyContext}: blueprint id is not a safe local reference");
                        structurallyValid = false;
                    }
                    else if (!blueprintIds.Add(blueprint.LocalId))
                    {
                        errors.Add($"{enemyContext}: duplicate blueprint id in native era");
                        structurallyValid = false;
                    }

                    if (!ContentSafety.IsHexHash(blueprint.SnapshotHash))
                    {
                        errors.Add(
                            $"{enemyContext}: blueprint snapshot hash must be 64 hex characters");
                        structurallyValid = false;
                    }

                    if (eraId is not null
                        && !string.Equals(
                            blueprint.Era?.NativeId, eraId, StringComparison.Ordinal))
                    {
                        errors.Add(
                            $"{enemyContext}: blueprint native era does not match its era entry");
                        structurallyValid = false;
                    }

                    if (!ValidateMods(blueprint.RequiredMods, enemyContext, errors))
                    {
                        structurallyValid = false;
                    }

                    if (!Enum.IsDefined(typeof(EnemyRole), enemy.Role))
                    {
                        errors.Add($"{enemyContext}: role value is not defined");
                        structurallyValid = false;
                    }

                    if (enemy.FormationFamily is { } family
                        && (string.IsNullOrWhiteSpace(family)
                            || family.Length > ContentSafety.MaxReferenceLength
                            || family.Any(char.IsControl)))
                    {
                        errors.Add(
                            $"{enemyContext}: formation family must be a short local label");
                        structurallyValid = false;
                    }

                    if (enemy.MaxPerBattle is <= 0)
                    {
                        errors.Add($"{enemyContext}: max per battle must be positive");
                        structurallyValid = false;
                    }

                    if (enemy.MassKg is { } massKg && massKg <= 0)
                    {
                        errors.Add(
                            $"{enemyContext}: measured mass must be a positive kilogram value");
                        structurallyValid = false;
                    }

                    if (enemy.ThreatScore <= 0m)
                    {
                        errors.Add($"{enemyContext}: threat score must be positive");
                        structurallyValid = false;
                    }

                    if (enemy.CheckedPrice is null || enemy.CheckedPrice.Value <= 0m)
                    {
                        errors.Add($"{enemyContext}: checked price must be positive");
                        structurallyValid = false;
                    }
                    else if (!string.Equals(
                        enemy.CheckedPrice.UnitId,
                        definition.PriceUnitId,
                        StringComparison.Ordinal))
                    {
                        errors.Add(
                            $"{enemyContext}: checked price unit '{enemy.CheckedPrice.UnitId}' does not match catalogue price unit");
                        structurallyValid = false;
                    }

                    if (!Enum.IsDefined(typeof(ContentQualification), enemy.Qualification))
                    {
                        errors.Add($"{enemyContext}: qualification value is not defined");
                        structurallyValid = false;
                    }
                    else
                    {
                        if (definition.IsFixture
                            && enemy.Qualification == ContentQualification.Qualified)
                        {
                            errors.Add(
                                $"{enemyContext}: fixture content cannot claim qualified status");
                        }

                        if (structurallyValid
                            && enemy.Qualification >= opts.RequiredQualification)
                        {
                            eligibleEnemies++;
                        }
                    }
                }
            }

            if (opts.EnforceMinimumContent && eligibleEnemies < 4)
            {
                errors.Add(
                    $"{context}: needs at least 4 qualified enemy designs, found {eligibleEnemies}");
            }
        }

        return new CatalogueValidationResult(errors);
    }

    private static bool ValidateMods(
        IReadOnlyList<ModRequirement>? mods,
        string context,
        List<string> errors)
    {
        if (mods is null)
        {
            errors.Add($"{context}: required mods list is null");
            return false;
        }

        bool valid = true;
        var versionsById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mod in mods)
        {
            if (mod is null)
            {
                errors.Add($"{context}: required mod entry is null");
                valid = false;
                continue;
            }

            if (!ContentSafety.IsSafeLocalReference(mod.Id))
            {
                errors.Add($"{context}: required mod id is not a safe local reference");
                valid = false;
            }

            if (!ContentSafety.IsSafeVersion(mod.Version))
            {
                errors.Add($"{context}: required mod version is not a safe version token");
                valid = false;
            }

            if (versionsById.TryGetValue(mod.Id, out var existing)
                && !string.Equals(existing, mod.Version, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{context}: conflicting versions required for mod '{mod.Id}'");
                valid = false;
            }
            else
            {
                versionsById[mod.Id] = mod.Version;
            }
        }

        return valid;
    }
}
