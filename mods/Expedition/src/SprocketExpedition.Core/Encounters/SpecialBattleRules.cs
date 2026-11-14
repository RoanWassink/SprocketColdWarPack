namespace SprocketExpedition.Core.Encounters;

using System.Globalization;
using SprocketExpedition.Core.Run;

/// <summary>
/// Pure, deterministic construction rules for yellow special battles.
/// This type contains no Unity or game-runtime dependencies.
/// </summary>
public static class SpecialBattleRules
{
    private const int MaxEnemies = 12;
    private const decimal WindowFloor = 0.65m;
    private const decimal WindowCeiling = 1.25m;

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static bool ShouldOffer(long seed, int round, int offerRoll)
    {
        if (round < 1 || offerRoll < 0 || round % 4 == 0)
        {
            return false;
        }

        // A stable hash is used instead of GetHashCode so the result is the
        // same in every process and on every runtime.
        ulong hash = HashKey(seed, round, offerRoll, 0x0FF3);
        return hash % 100UL < 30UL;
    }

    // Adventure policy 7: a normal round has a 25% chance of one yellow special.
    // The boss-round/test-pool gates live in the planner, not in this stable roll.
    internal static bool ShouldOfferV7(long seed, int round, int offerRoll)
    {
        if (round < 1 || offerRoll < 0) return false;
        return HashKey(seed, round, offerRoll, 0x0FF7) % 100UL < 25UL;
    }

    // Which one of the three risk cards becomes the special. Stable per
    // seed/round/roll and drawn from the same family as ShouldOfferV7.
    internal static EncounterRisk SpecialRiskV7(long seed, int round, int offerRoll)
    {
        var index = (int)(HashKey(seed, round, offerRoll, 0x0FF8) % 3UL);
        return (EncounterRisk)index;
    }

    public static SpecialBattle? Create(
        long seed,
        int round,
        int offerRoll,
        decimal targetBudget,
        IReadOnlyList<SpecialDesign> designs)
    {
        if (round < 1 || offerRoll < 0 || targetBudget <= 0m || designs is null || designs.Count == 0)
        {
            return null;
        }

        List<SpecialDesign> pool = BuildPool(designs);
        if (pool.Count == 0)
        {
            return null;
        }

        // Multiplying a positive decimal by a fraction below one cannot
        // overflow. The upper bound is guarded because it can.
        decimal lowerBound = targetBudget * WindowFloor;
        decimal upperBound;
        if (!TryMultiply(targetBudget, WindowCeiling, out upperBound))
        {
            upperBound = decimal.MaxValue;
        }

        SpecialPreset[] presetOrder = GetPresetOrder(seed, round, offerRoll);
        foreach (SpecialPreset preset in presetOrder)
        {
            List<SpecialDesign> bucket = SelectBucket(pool, preset);
            if (bucket.Count == 0)
            {
                continue;
            }

            List<Roster> candidates = BuildCandidates(
                bucket,
                preset,
                targetBudget);

            Roster? best = null;
            foreach (Roster candidate in candidates)
            {
                if (candidate.Cost < lowerBound || candidate.Cost > upperBound)
                {
                    continue;
                }

                if (best is null ||
                    IsBetter(candidate, best, preset, targetBudget, seed, round, offerRoll))
                {
                    best = candidate;
                }
            }

            if (best is null)
            {
                continue;
            }

            (string title, SpecialSpawnPattern pattern, string rewardFamily, decimal rewardMagnitude, int duration) =
                GetMetadata(preset);

            return new SpecialBattle(
                preset,
                title,
                pattern,
                best.Units,
                rewardFamily,
                rewardMagnitude,
                duration);
        }

        return null;
    }

    public static string Describe(SpecialBattle battle)
    {
        if (battle is null)
        {
            return "No special battle.";
        }

        List<SpecialUnit> enemies = AggregateEnemies(battle.Enemies);
        long totalHulls = 0;
        foreach (SpecialUnit enemy in enemies)
        {
            totalHulls += enemy.Count;
        }

        string title = string.IsNullOrWhiteSpace(battle.Title)
            ? battle.Preset.ToString()
            : battle.Title;

        string patternText = battle.Pattern switch
        {
            SpecialSpawnPattern.Swarm => "swarm formation, concentrated in one mass",
            SpecialSpawnPattern.Line => "broad line formation",
            SpecialSpawnPattern.Split => "split left/right formation",
            SpecialSpawnPattern.Ambush => "wide-bearing ambush formation",
            SpecialSpawnPattern.Defensive => "compact defensive formation",
            _ => "formation"
        };

        string enemyText = enemies.Count == 0
            ? "no enemies"
            : string.Join(", ", enemies.Select(unit =>
                $"{unit.Count} x {unit.BlueprintId}"));

        string hullWord = totalHulls == 1 ? "hull" : "hulls";
        string roundWord = battle.Duration == 1 ? "round" : "rounds";
        string rewardFamily = string.IsNullOrWhiteSpace(battle.RewardFamily)
            ? "none"
            : battle.RewardFamily;

        decimal percent;
        string percentText;
        if (TryMultiply(battle.RewardMagnitude, 100m, out percent))
        {
            percentText = percent.ToString("0.##", CultureInfo.InvariantCulture);
        }
        else
        {
            percentText = battle.RewardMagnitude.ToString(CultureInfo.InvariantCulture);
        }

        string rewardSign = percentText.StartsWith("-", StringComparison.Ordinal) ? string.Empty : "+";

        return $"{title}: {totalHulls} enemy {hullWord} in a {patternText}. " +
               $"Enemies: {enemyText}. " +
               $"Reward: {rewardFamily} {rewardSign}{percentText}% for {battle.Duration} {roundWord}.";
    }

    // Theme/roles copy shared by the native menu, HUD and generic presenter. It
    // never promises a light/flank element that the saved battle does not contain.
    // displayName maps a blueprint id to a readable design name (default: the id).
    public static string DescribeComposition(SpecialBattle battle, Func<string, string>? displayName = null)
    {
        if (battle is null) return "No formation recorded.";
        var units = (battle.Enemies ?? Array.Empty<SpecialUnit>())
            .Where(unit => unit is not null && unit.Count > 0)
            .ToArray();
        if (!units.Any(unit => !string.IsNullOrWhiteSpace(unit.DeploymentRole)))
            return $"{units.Sum(unit => unit.Count)} hull(s); no deployment roles were recorded.";
        var groups = new[]
        {
            ("centre", "Center"),
            ("flanks", "Flank"),
            ("support", "Support")
        };
        var parts = new List<string>();
        foreach (var (label, role) in groups)
        {
            var members = units
                .Where(unit => string.Equals(unit.DeploymentRole, role,
                    StringComparison.Ordinal))
                .GroupBy(unit => unit.BlueprintId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            if (members.Length == 0) continue;
            var total = members.Sum(group => group.Sum(unit => unit.Count));
            var detail = string.Join(", ", members.Select(group =>
                $"{group.Sum(unit => unit.Count)} x {(displayName == null ? group.Key : displayName(group.Key))}"));
            parts.Add($"{label} {total} ({detail})");
        }
        var unassigned = units.Where(unit =>
            string.IsNullOrWhiteSpace(unit.DeploymentRole)).ToArray();
        if (unassigned.Length > 0)
        {
            var total = unassigned.Sum(unit => unit.Count);
            var detail = string.Join(", ", unassigned
                .GroupBy(unit => unit.BlueprintId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Sum(unit => unit.Count)} x {(displayName == null ? group.Key : displayName(group.Key))}"));
            parts.Add($"unassigned {total} ({detail})");
        }
        if (parts.Count == 0)
            return $"{units.Sum(unit => unit.Count)} hull(s); no deployment roles were recorded.";
        return string.Join("; ", parts) + ".";
    }

    public static string DescribeIntent(SpecialBattle battle)
    {
        if (battle is null) return "No tactical intent recorded.";
        var units = battle.Enemies ?? Array.Empty<SpecialUnit>();
        var hasRoleData = units.Any(unit => unit is not null
            && !string.IsNullOrWhiteSpace(unit.DeploymentRole));
        if (!hasRoleData)
            return "Saved without explicit deployment roles; the group advances toward the opposing force.";
        var centre = units.Any(unit => unit is not null && string.Equals(
            unit.DeploymentRole, "Center", StringComparison.Ordinal));
        var flank = units.Any(unit => unit is not null && string.Equals(
            unit.DeploymentRole, "Flank", StringComparison.Ordinal));
        var support = units.Any(unit => unit is not null && string.Equals(
            unit.DeploymentRole, "Support", StringComparison.Ordinal));
        if (battle.Pattern == SpecialSpawnPattern.Ambush && centre && flank)
            return "The centre advances to contact while flankers approach on wide side bearings around your force."
                + UnknownRoleClause(units);
        var parts = new List<string>();
        if (centre)
        {
            if (support && flank)
                return "The centre advances in short alternating bounds under support fire; "
                    + "flankers work the side lanes."
                    + UnknownRoleClause(units);
            if (support)
                return "The centre advances in short alternating bounds under support fire."
                    + UnknownRoleClause(units);
            if (flank)
                return "The centre advances in short alternating bounds; "
                    + "flankers work the side lanes."
                    + UnknownRoleClause(units);
            return "The centre advances in short alternating bounds."
                + UnknownRoleClause(units);
        }
        if (support && flank)
            return "Support holds depth to cover the advance; flankers work the side lanes."
                + UnknownRoleClause(units);
        if (support)
            return "Support holds depth to cover the advance."
                + UnknownRoleClause(units);
        if (flank)
            return "Flankers work the side lanes." + UnknownRoleClause(units);
        return "Saved without a usable deployment-role description."
            + UnknownRoleClause(units);
    }

    private static string UnknownRoleClause(IReadOnlyList<SpecialUnit> units)
    {
        var unknown = units.Count(unit => unit is null
            || string.IsNullOrWhiteSpace(unit.DeploymentRole));
        return unknown == 0 ? string.Empty
            : $" {unknown} unit(s) have no recorded deployment role.";
    }

    // Every persisted thematic/boss plan is checked structurally before it can be
    // restored. Legacy v2/v3 offers without optional roles remain valid.
    public static bool TryValidatePlan(
        EncounterExtras? extras,
        IReadOnlyList<EnemyUnit>? planEnemies,
        out string error)
    {
        error = string.Empty;
        if (extras is null)
        {
            return true;
        }

        if (extras.RulesVersion is < 2 or > 7)
        {
            error = "Encounter extras rules version is not supported.";
            return false;
        }
        if (extras.GenerationPolicy is { } generationPolicy && generationPolicy != 5)
        {
            error = "Encounter extras generation policy is not supported.";
            return false;
        }
        var policy = extras.GenerationPolicy ?? extras.RulesVersion;
        if (extras.OfferRoll is < 0 or > 10_000)
        {
            error = "Encounter extras offer roll is outside the supported range.";
            return false;
        }
        // The request-level base multiplier is 1..2 (or 2.4 with the existing
        // ExperimentalContract on policy 5); Dangerous stores the product with
        // its 1.35 risk factor. Policy 5 adds optional era progression, bounded.
        var maximumMultiplier = policy >= 5 ? DifficultyScaling.MaximumPersistedMultiplier : 2.7m;
        if (extras.EnemyBudgetMultiplier is < 1m || extras.EnemyBudgetMultiplier > maximumMultiplier)
        {
            error = "Encounter extras enemy budget multiplier is outside the supported range.";
            return false;
        }
        if (extras.EnemyBudgetTarget is { } budgetTarget && budgetTarget <= 0m)
        {
            error = "Encounter extras enemy budget target must be positive.";
            return false;
        }
        if (extras.Reward is null)
        {
            error = "Encounter extras reward is missing.";
            return false;
        }

        try
        {
            RewardRules.Validate(new RewardState
            {
                Version = extras.RulesVersion,
                Inventory = new[] { extras.Reward }
            });
        }
        catch (InvalidOperationException ex)
        {
            error = "Encounter reward is invalid: " + ex.Message;
            return false;
        }

        var hasBoss = !string.IsNullOrWhiteSpace(extras.BossBlueprintId);
        if (hasBoss)
        {
            if (string.IsNullOrWhiteSpace(extras.BossEra))
            {
                error = "Boss encounter has no native era.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(extras.BossCharacter))
            {
                error = "Boss encounter has no character.";
                return false;
            }
        }
        else if (extras.CuratedBoss || extras.PlaceholderBoss
            || !string.IsNullOrWhiteSpace(extras.BossEra)
            || !string.IsNullOrWhiteSpace(extras.BossCharacter))
        {
            error = "Boss markers exist without a boss blueprint.";
            return false;
        }

        var special = extras.Special;
        if (special is null) return true;
        if (extras.RulesVersion < 3)
        {
            error = "A special formation cannot precede adventure rules version 3.";
            return false;
        }
        if (!Enum.IsDefined(typeof(SpecialPreset), special.Preset)
            || !Enum.IsDefined(typeof(SpecialSpawnPattern), special.Pattern))
        {
            error = "Special formation preset or pattern is not defined.";
            return false;
        }
        if (special.Preset == SpecialPreset.TacticalGroup && extras.RulesVersion < 4)
        {
            error = "A TacticalGroup requires adventure rules version 4.";
            return false;
        }
        if (special.Pattern == SpecialSpawnPattern.Ambush && policy < 5)
        {
            error = "An Ambush formation requires adventure policy version 5.";
            return false;
        }
        if (special.Pattern == SpecialSpawnPattern.Defensive && policy < 7)
        {
            error = "A Defensive formation requires adventure policy version 7.";
            return false;
        }
        var strictV4Roles = policy >= 4
            && special.Preset == SpecialPreset.TacticalGroup;
        if (strictV4Roles && policy == 4
            && special.Pattern != SpecialSpawnPattern.CombinedArms)
        {
            error = "A v4 TacticalGroup must use the CombinedArms spawn pattern.";
            return false;
        }
        if (strictV4Roles && policy >= 5
            && special.Pattern is not (SpecialSpawnPattern.CombinedArms
                or SpecialSpawnPattern.Ambush
                or SpecialSpawnPattern.Defensive))
        {
            error = "A v5 TacticalGroup must use the CombinedArms, Ambush or Defensive spawn pattern.";
            return false;
        }
        if (special.Pattern == SpecialSpawnPattern.Ambush
            && special.Preset != SpecialPreset.TacticalGroup)
        {
            error = "An Ambush spawn pattern requires a TacticalGroup formation.";
            return false;
        }
        if (strictV4Roles && special.Enemies is not null && special.Enemies.Any(unit =>
                unit is null || string.IsNullOrWhiteSpace(unit.DeploymentRole)))
        {
            error = "Each v4 TacticalGroup unit requires an explicit Center, Flank or Support role.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(special.Title)
            || special.Title.Length > ContentSafety.MaxReferenceLength
            || special.Title.Any(char.IsControl))
        {
            error = "Special formation title is not a short local label.";
            return false;
        }
        if (special.Enemies is null || special.Enemies.Count == 0)
        {
            error = "Special formation has no units.";
            return false;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        foreach (var unit in special.Enemies)
        {
            if (unit is null || !ContentSafety.IsSafeLocalReference(unit.BlueprintId)
                || unit.Count <= 0)
            {
                error = "Special formation contains a null, unsafe or non-positive unit.";
                return false;
            }
            if (unit.DeploymentRole is { } role
                && role is not ("Center" or "Flank" or "Support"))
            {
                error = $"Special formation has an unknown deployment role '{role}'.";
                return false;
            }
            if (total > MaxEnemies - unit.Count)
            {
                error = "Special formation exceeds the native slot cap.";
                return false;
            }
            total += unit.Count;
            counts[unit.BlueprintId] = counts.TryGetValue(unit.BlueprintId, out var count)
                ? count + unit.Count : unit.Count;
        }

        if (planEnemies is not null)
        {
            var planCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var enemy in planEnemies)
            {
                if (enemy is null) continue;
                planCounts[enemy.BlueprintId] = planCounts.TryGetValue(
                    enemy.BlueprintId, out var count) ? count + enemy.Count : enemy.Count;
            }
            if (planCounts.Count != counts.Count
                || planCounts.Any(pair => !counts.TryGetValue(pair.Key, out var value)
                    || value != pair.Value))
            {
                error = "Special formation roster does not match the plan roster.";
                return false;
            }
        }

        return true;
    }

    private static List<SpecialDesign> BuildPool(IReadOnlyList<SpecialDesign> designs)
    {
        // Same ID with one price is kept once. Same ID with different prices
        // invalidates the whole ID. Blank IDs and nonpositive prices are dropped.
        Dictionary<string, decimal> prices = new(StringComparer.Ordinal);
        HashSet<string> invalidIds = new(StringComparer.Ordinal);

        foreach (SpecialDesign? design in designs)
        {
            if (design is null)
            {
                continue;
            }

            string? id = design.BlueprintId;
            if (string.IsNullOrWhiteSpace(id) || design.Cost <= 0m)
            {
                continue;
            }

            if (invalidIds.Contains(id))
            {
                continue;
            }

            if (prices.TryGetValue(id, out decimal existingPrice))
            {
                if (existingPrice != design.Cost)
                {
                    prices.Remove(id);
                    invalidIds.Add(id);
                }
            }
            else
            {
                prices.Add(id, design.Cost);
            }
        }

        List<SpecialDesign> pool = new(prices.Count);
        foreach (KeyValuePair<string, decimal> entry in prices)
        {
            pool.Add(new SpecialDesign(entry.Key, entry.Value));
        }

        pool.Sort(CompareDesigns);
        return pool;
    }

    private static int CompareDesigns(SpecialDesign left, SpecialDesign right)
    {
        int costComparison = left.Cost.CompareTo(right.Cost);
        return costComparison != 0
            ? costComparison
            : StringComparer.Ordinal.Compare(left.BlueprintId, right.BlueprintId);
    }

    private static List<SpecialDesign> SelectBucket(List<SpecialDesign> pool, SpecialPreset preset)
    {
        int count = pool.Count;
        int thirdSize = count / 3;
        if (thirdSize < 1)
        {
            thirdSize = 1;
        }

        int start;
        int end;
        switch (preset)
        {
            case SpecialPreset.RatPlague:
                start = 0;
                end = Math.Min(count, thirdSize) - 1;
                break;
            case SpecialPreset.SteelWall:
                start = Math.Max(0, count - thirdSize);
                end = count - 1;
                break;
            default:
                start = thirdSize;
                end = count - thirdSize - 1;
                break;
        }

        if (start > end)
        {
            // Tiny pools: fall back to the nearest endpoint or median rank.
            int fallback = preset switch
            {
                SpecialPreset.RatPlague => 0,
                SpecialPreset.SteelWall => count - 1,
                _ => count / 2
            };

            start = Math.Clamp(fallback, 0, count - 1);
            end = start;
        }

        List<SpecialDesign> bucket = new(end - start + 1);
        for (int i = start; i <= end; i++)
        {
            bucket.Add(pool[i]);
        }

        return bucket;
    }

    private static List<Roster> BuildCandidates(
        List<SpecialDesign> bucket,
        SpecialPreset preset,
        decimal targetBudget)
    {
        (int minimum, int maximum) = GetCountRange(preset);
        List<Roster> candidates = new();

        // Single-design rosters are always considered. This keeps small pools
        // usable and lets a roster repeat copies of one design.
        foreach (SpecialDesign design in bucket)
        {
            for (int hullCount = minimum; hullCount <= maximum; hullCount++)
            {
                Roster? roster = CreateRoster(hullCount, (design, hullCount));
                if (roster is not null)
                {
                    candidates.Add(roster);
                }
            }
        }

        // A bounded set of representative two-design mixtures adds variety and
        // makes it easier to land close to the target budget. No exhaustive
        // subset or combination enumeration is performed.
        List<SpecialDesign> representatives = GetRepresentatives(bucket, 17);
        for (int i = 0; i < representatives.Count; i++)
        {
            for (int j = i + 1; j < representatives.Count; j++)
            {
                AddPairCandidates(
                    candidates,
                    representatives[i],
                    representatives[j],
                    targetBudget,
                    minimum,
                    maximum);
            }
        }

        if (bucket.Count > 1)
        {
            SpecialDesign cheapest = bucket[0];
            SpecialDesign mostExpensive = bucket[bucket.Count - 1];

            foreach (SpecialDesign design in bucket)
            {
                if (!string.Equals(design.BlueprintId, cheapest.BlueprintId, StringComparison.Ordinal))
                {
                    AddPairCandidates(
                        candidates,
                        cheapest,
                        design,
                        targetBudget,
                        minimum,
                        maximum);
                }

                if (!string.Equals(design.BlueprintId, mostExpensive.BlueprintId, StringComparison.Ordinal))
                {
                    AddPairCandidates(
                        candidates,
                        design,
                        mostExpensive,
                        targetBudget,
                        minimum,
                        maximum);
                }
            }
        }

        return candidates;
    }

    private static void AddPairCandidates(
        List<Roster> candidates,
        SpecialDesign first,
        SpecialDesign second,
        decimal targetBudget,
        int minimum,
        int maximum)
    {
        if (string.Equals(first.BlueprintId, second.BlueprintId, StringComparison.Ordinal))
        {
            return;
        }

        SpecialDesign low = first;
        SpecialDesign high = second;
        if (low.Cost > high.Cost)
        {
            (low, high) = (high, low);
        }

        decimal costDifference = high.Cost - low.Cost;
        if (costDifference <= 0m)
        {
            return;
        }

        for (int hullCount = minimum; hullCount <= maximum; hullCount++)
        {
            decimal lowCost;
            if (!TryMultiply(low.Cost, hullCount, out lowCost))
            {
                continue;
            }

            decimal remaining = targetBudget - lowCost;
            if (remaining <= 0m)
            {
                // A mixture containing the more expensive design is already
                // above the pure cheap roster, which was added separately.
                continue;
            }

            // Always include the two simplest mixtures, even if the ideal
            // allocation cannot be represented as a decimal.
            AddPairAt(candidates, low, high, hullCount, 1);
            AddPairAt(candidates, low, high, hullCount, hullCount - 1);

            decimal idealHighCount;
            if (!TryDivide(remaining, costDifference, out idealHighCount))
            {
                continue;
            }

            int center = ClampToHullCount(idealHighCount, hullCount);
            AddPairAt(candidates, low, high, hullCount, center - 1);
            AddPairAt(candidates, low, high, hullCount, center);
            AddPairAt(candidates, low, high, hullCount, center + 1);
        }
    }

    private static void AddPairAt(
        List<Roster> candidates,
        SpecialDesign low,
        SpecialDesign high,
        int hullCount,
        int highCount)
    {
        if (highCount <= 0 || highCount >= hullCount)
        {
            return;
        }

        int lowCount = hullCount - highCount;
        Roster? roster = CreateRoster(
            hullCount,
            (low, lowCount),
            (high, highCount));

        if (roster is not null)
        {
            candidates.Add(roster);
        }
    }

    private static List<SpecialDesign> GetRepresentatives(List<SpecialDesign> bucket, int maximum)
    {
        if (bucket.Count <= maximum)
        {
            return bucket;
        }

        List<SpecialDesign> representatives = new(maximum);
        int previousIndex = -1;

        for (int i = 0; i < maximum; i++)
        {
            int index = (int)((long)i * (bucket.Count - 1) / (maximum - 1));
            if (index == previousIndex)
            {
                continue;
            }

            representatives.Add(bucket[index]);
            previousIndex = index;
        }

        return representatives;
    }

    private static int ClampToHullCount(decimal value, int maximum)
    {
        if (value <= 0m)
        {
            return 0;
        }

        if (value >= maximum)
        {
            return maximum;
        }

        return (int)decimal.Round(value, 0, MidpointRounding.AwayFromZero);
    }

    private static Roster? CreateRoster(
        int hullCount,
        params (SpecialDesign Design, int Copies)[] parts)
    {
        decimal totalCost = 0m;
        int actualHullCount = 0;
        Dictionary<string, int> counts = new(StringComparer.Ordinal);

        foreach ((SpecialDesign design, int copies) in parts)
        {
            if (copies <= 0)
            {
                continue;
            }

            decimal partCost;
            if (!TryMultiply(design.Cost, copies, out partCost))
            {
                return null;
            }

            decimal newTotal;
            if (!TryAdd(totalCost, partCost, out newTotal))
            {
                return null;
            }

            totalCost = newTotal;

            if (counts.TryGetValue(design.BlueprintId, out int existingCount))
            {
                counts[design.BlueprintId] = existingCount + copies;
            }
            else
            {
                counts.Add(design.BlueprintId, copies);
            }

            actualHullCount += copies;
        }

        if (actualHullCount != hullCount || counts.Count == 0)
        {
            return null;
        }

        List<SpecialUnit> units = new(counts.Count);
        foreach (KeyValuePair<string, int> entry in counts)
        {
            if (entry.Value <= 0)
            {
                return null;
            }

            units.Add(new SpecialUnit(entry.Key, entry.Value));
        }

        units.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.BlueprintId, right.BlueprintId));

        return new Roster(totalCost, units.ToArray(), actualHullCount);
    }

    private static bool IsBetter(
        Roster candidate,
        Roster current,
        SpecialPreset preset,
        decimal targetBudget,
        long seed,
        int round,
        int offerRoll)
    {
        int comparison = CompareRosters(candidate, current, preset, targetBudget);
        if (comparison != 0)
        {
            return comparison < 0;
        }

        ulong candidateTie = TieKey(seed, round, offerRoll, preset, candidate.Units);
        ulong currentTie = TieKey(seed, round, offerRoll, preset, current.Units);
        return candidateTie < currentTie;
    }

    private static int CompareRosters(
        Roster left,
        Roster right,
        SpecialPreset preset,
        decimal targetBudget)
    {
        if (preset == SpecialPreset.RatPlague)
        {
            bool leftHasTwelve = left.HullCount == MaxEnemies;
            bool rightHasTwelve = right.HullCount == MaxEnemies;
            if (leftHasTwelve != rightHasTwelve)
            {
                return leftHasTwelve ? -1 : 1;
            }
        }
        else if (preset == SpecialPreset.SteelWall)
        {
            int hullComparison = left.HullCount.CompareTo(right.HullCount);
            if (hullComparison != 0)
            {
                return hullComparison;
            }
        }

        decimal leftDistance = DistanceFromTarget(left.Cost, targetBudget);
        decimal rightDistance = DistanceFromTarget(right.Cost, targetBudget);
        return leftDistance.CompareTo(rightDistance);
    }

    private static decimal DistanceFromTarget(decimal cost, decimal targetBudget)
    {
        return cost >= targetBudget
            ? cost - targetBudget
            : targetBudget - cost;
    }

    private static (int Minimum, int Maximum) GetCountRange(SpecialPreset preset)
    {
        return preset switch
        {
            SpecialPreset.RatPlague => (8, 12),
            SpecialPreset.SteelWall => (1, 3),
            SpecialPreset.Crossfire => (4, 8),
            _ => (1, 1)
        };
    }

    private static (
        string Title,
        SpecialSpawnPattern Pattern,
        string RewardFamily,
        decimal RewardMagnitude,
        int Duration) GetMetadata(SpecialPreset preset)
    {
        return preset switch
        {
            SpecialPreset.RatPlague => (
                "Rat Plague",
                SpecialSpawnPattern.Swarm,
                "AssemblyLine",
                0.20m,
                3),
            SpecialPreset.SteelWall => (
                "Steel Wall",
                SpecialSpawnPattern.Line,
                "EliteWorkshop",
                0.25m,
                4),
            SpecialPreset.Crossfire => (
                "Crossfire",
                SpecialSpawnPattern.Split,
                "VeteranCrew",
                0.30m,
                4),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
    }

    private static SpecialPreset[] GetPresetOrder(long seed, int round, int offerRoll)
    {
        SpecialPreset[] presets =
        {
            SpecialPreset.RatPlague,
            SpecialPreset.SteelWall,
            SpecialPreset.Crossfire
        };

        int start = (int)(HashKey(seed, round, offerRoll, 0x5EED) % 3UL);
        return new[]
        {
            presets[start],
            presets[(start + 1) % 3],
            presets[(start + 2) % 3]
        };
    }

    private static List<SpecialUnit> AggregateEnemies(IReadOnlyList<SpecialUnit>? enemies)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);

        if (enemies is not null)
        {
            foreach (SpecialUnit? enemy in enemies)
            {
                if (enemy is null ||
                    string.IsNullOrWhiteSpace(enemy.BlueprintId) ||
                    enemy.Count <= 0)
                {
                    continue;
                }

                if (counts.TryGetValue(enemy.BlueprintId, out int existingCount))
                {
                    int combined = existingCount > int.MaxValue - enemy.Count
                        ? int.MaxValue
                        : existingCount + enemy.Count;
                    counts[enemy.BlueprintId] = combined;
                }
                else
                {
                    counts.Add(enemy.BlueprintId, enemy.Count);
                }
            }
        }

        List<SpecialUnit> units = new(counts.Count);
        foreach (KeyValuePair<string, int> entry in counts)
        {
            units.Add(new SpecialUnit(entry.Key, entry.Value));
        }

        units.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.BlueprintId, right.BlueprintId));

        return units;
    }

    private static ulong HashKey(long seed, int round, int offerRoll, int tag)
    {
        ulong hash = FnvOffset;
        hash = HashUInt64(hash, unchecked((ulong)seed));
        hash = HashUInt64(hash, unchecked((ulong)(uint)round));
        hash = HashUInt64(hash, unchecked((ulong)(uint)offerRoll));
        hash = HashUInt64(hash, unchecked((ulong)(uint)tag));
        return hash;
    }

    private static ulong HashUInt64(ulong hash, ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            hash ^= (byte)(value & 0xFFUL);
            hash = unchecked(hash * FnvPrime);
            value >>= 8;
        }

        return hash;
    }

    private static ulong HashString(ulong hash, string value)
    {
        foreach (char character in value)
        {
            hash ^= (byte)(character & 0xFF);
            hash = unchecked(hash * FnvPrime);
            hash ^= (byte)(character >> 8);
            hash = unchecked(hash * FnvPrime);
        }

        return hash;
    }

    private static ulong TieKey(
        long seed,
        int round,
        int offerRoll,
        SpecialPreset preset,
        IReadOnlyList<SpecialUnit> units)
    {
        ulong hash = HashKey(seed, round, offerRoll, (int)preset + 1);
        foreach (SpecialUnit unit in units)
        {
            hash = HashString(hash, unit.BlueprintId);
            hash = HashUInt64(hash, unchecked((ulong)(uint)unit.Count));
        }

        return hash;
    }

    private static bool TryMultiply(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left * right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    private static bool TryAdd(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left + right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    private static bool TryDivide(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left / right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    private sealed class Roster
    {
        public Roster(decimal cost, SpecialUnit[] units, int hullCount)
        {
            Cost = cost;
            Units = units;
            HullCount = hullCount;
        }

        public decimal Cost { get; }

        public SpecialUnit[] Units { get; }

        public int HullCount { get; }
    }
}
