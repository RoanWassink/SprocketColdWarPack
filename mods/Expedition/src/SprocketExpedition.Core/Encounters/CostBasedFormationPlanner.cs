namespace SprocketExpedition.Core.Encounters;

// One real unit choice in a candidate formation. Design always comes from the
// requested native era; no synthetic unit, map or era is ever created here.
internal sealed record CostFormationUnit(
    EnemyDesignEntry Design,
    int Count);

// One formation per risk. Costs are strictly increasing by construction.
internal sealed record CostFormationSelection(
    IReadOnlyList<CostFormationUnit> Cautious,
    IReadOnlyList<CostFormationUnit> Standard,
    IReadOnlyList<CostFormationUnit> Dangerous);

// Deterministic, bounded cost planner. Formation strength is always
// SUM(CheckedPrice.Value * count); counts alone never affect difficulty.
// The search builds a shared candidate frontier (single-type counts plus a
// bounded set of greedy mixed variants), then picks three formations backwards
// from the most expensive affordable dangerous formation. It deliberately does
// not enumerate every combination, index by currency or claim optimality.
internal static class CostBasedFormationPlanner
{
    // A dedicated card stream keeps the frontier ordering stable and independent
    // of the legacy per-card formation draws.
    private const int FrontierCardIndex = 4096;
    private const int MaxCandidates = 1024;

    public static IReadOnlyList<string> ValidateCheckedPrices(
        CatalogueDefinition definition)
    {
        var errors = new List<string>();

        foreach (var era in definition.Eras ?? Array.Empty<CatalogueEraContent>())
        {
            if (era is null)
            {
                continue;
            }

            foreach (var enemy in era.Enemies
                ?? Array.Empty<EnemyDesignEntry>())
            {
                if (enemy is null)
                {
                    continue;
                }

                string blueprintId = enemy.Blueprint?.LocalId ?? "<unknown>";
                if (enemy.CheckedPrice is null)
                {
                    errors.Add(
                        $"cost mode: enemy '{blueprintId}' has no checked price");
                    continue;
                }

                if (enemy.ThreatScore != enemy.CheckedPrice.Value)
                {
                    errors.Add(
                        $"cost mode: enemy '{blueprintId}' ThreatScore {enemy.ThreatScore} does not match CheckedPrice.Value {enemy.CheckedPrice.Value}");
                }
                if (enemy.CheckedPrice.Value > decimal.MaxValue / EncounterRules.MaxEnemies)
                    errors.Add($"cost mode: enemy '{blueprintId}' price cannot be represented at the fleet cap");
            }
        }

        return errors;
    }

    public static bool TrySelect(
        IReadOnlyList<EnemyDesignEntry> pool,
        string eraId,
        string priceUnitId,
        decimal cautiousTarget,
        decimal standardTarget,
        decimal dangerousTarget,
        long seed,
        int round,
        out CostFormationSelection? selection,
        out string error)
    {
        selection = null;

        if (cautiousTarget <= 0m
            || standardTarget <= 0m
            || dangerousTarget <= 0m)
        {
            error = "cost mode: every risk target must be positive";
            return false;
        }

        // Same-era, qualified, price-consistent real designs only. The caller has
        // already filtered qualification; these checks keep the planner safe when
        // it is used directly or when content data is malformed.
        var designs = (pool ?? Array.Empty<EnemyDesignEntry>())
            .Where(design => design?.Blueprint is not null
                && design.CheckedPrice is not null
                && design.CheckedPrice.Value > 0m
                && design.ThreatScore == design.CheckedPrice.Value
                && string.Equals(
                    design.CheckedPrice.UnitId,
                    priceUnitId,
                    StringComparison.Ordinal)
                && string.Equals(
                    design.Blueprint.Era?.NativeId,
                    eraId,
                    StringComparison.Ordinal))
            .GroupBy(
                design => design.Blueprint.LocalId,
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(
                design => design.Blueprint.LocalId,
                StringComparer.Ordinal)
            .ToArray();

        if (designs.Length == 0)
        {
            error = $"cost mode: era '{eraId}' has no real price-consistent enemy designs";
            return false;
        }

        // Stable seeded design order. It also supplies the tie order for equal
        // cost candidates, so equal-price rosters are not chosen by accident of
        // dictionary or source ordering.
        var random = new StableRandom(seed, round, FrontierCardIndex);
        var order = Enumerable.Range(0, designs.Length).ToArray();
        for (int index = order.Length - 1; index > 0; index--)
        {
            int swap = random.NextInt(index + 1);
            (order[index], order[swap]) = (order[swap], order[index]);
        }

        var shuffled = order
            .Select(index => designs[index])
            .ToArray();

        var candidates = new List<CostFormationCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        bool TryAdd(IReadOnlyList<CostFormationUnit> units)
        {
            if (candidates.Count >= MaxCandidates)
            {
                return false;
            }

            if (!TryComputeCost(units, dangerousTarget, out decimal cost))
            {
                return false;
            }

            string key = RosterKey(units);
            if (!seen.Add(key))
            {
                return false;
            }

            candidates.Add(new CostFormationCandidate(units, cost));
            return true;
        }

        // Single-type counts: bounded by the 12-unit cap and the price-consistent
        // data. No currency-indexed table is built.
        foreach (int index in order)
        {
            var design = designs[index];
            decimal price = design.CheckedPrice!.Value;

            for (int count = 1; count <= EncounterRules.MaxEnemies; count++)
            {
                if (!TryMultiply(price, count, out decimal cost)
                    || cost > dangerousTarget)
                {
                    break;
                }

                TryAdd(new[] { new CostFormationUnit(design, count) });
            }
        }

        // Bounded mixed variants: for each rotation, add at most one of each
        // design in price order, then fill the remaining cap with the cheapest
        // real design. Ascending and descending price orders give both
        // cheap-heavy and expensive-heavy rosters.
        foreach (bool ascending in new[] { true, false })
        {
            var ranked = ascending
                ? shuffled
                    .OrderBy(design => design.CheckedPrice!.Value)
                    .ThenBy(
                        design => design.Blueprint.LocalId,
                        StringComparer.Ordinal)
                    .ToArray()
                : shuffled
                    .OrderByDescending(design => design.CheckedPrice!.Value)
                    .ThenBy(
                        design => design.Blueprint.LocalId,
                        StringComparer.Ordinal)
                    .ToArray();

            var cheapest = designs
                .OrderBy(design => design.CheckedPrice!.Value)
                .ThenBy(
                    design => design.Blueprint.LocalId,
                    StringComparer.Ordinal)
                .First();

            for (int rotation = 0; rotation < designs.Length; rotation++)
            {
                int start = rotation % ranked.Length;
                var counts = new Dictionary<string, (EnemyDesignEntry Design, int Count)>(
                    StringComparer.Ordinal);
                int totalCount = 0;
                decimal spent = 0m;

                for (int step = 0;
                    step < ranked.Length
                        && totalCount < EncounterRules.MaxEnemies;
                    step++)
                {
                    var design = ranked[(start + step) % ranked.Length];
                    decimal price = design.CheckedPrice!.Value;

                    if (price > dangerousTarget - spent)
                    {
                        continue;
                    }

                    counts[design.Blueprint.LocalId] = (design, 1);
                    spent += price;
                    totalCount++;
                }

                if (counts.Count == 0)
                {
                    continue;
                }

                // Keep both the one-each frontier point and its cheapest fill.
                TryAdd(BuildUnits(counts));

                decimal cheapestPrice = cheapest.CheckedPrice!.Value;
                while (totalCount < EncounterRules.MaxEnemies
                    && cheapestPrice <= dangerousTarget - spent)
                {
                    string cheapestId = cheapest.Blueprint.LocalId;
                    if (counts.TryGetValue(cheapestId, out var existing))
                    {
                        counts[cheapestId] = (existing.Design, existing.Count + 1);
                    }
                    else
                    {
                        counts[cheapestId] = (cheapest, 1);
                    }

                    spent += cheapestPrice;
                    totalCount++;
                }

                TryAdd(BuildUnits(counts));
            }
        }

        if (candidates.Count == 0)
        {
            error = $"cost mode: no real formation fits the dangerous target {dangerousTarget} for era '{eraId}'";
            return false;
        }

        // Seeded tie order for equal-cost roster choices.
        for (int index = candidates.Count - 1; index > 0; index--)
        {
            int swap = random.NextInt(index + 1);
            (candidates[index], candidates[swap]) =
                (candidates[swap], candidates[index]);
        }

        var ordered = candidates
            .Select((candidate, rank) => new RankedCandidate(candidate, rank))
            .OrderByDescending(item => item.Candidate.TotalCost)
            .ThenBy(item => item.Rank)
            .ToArray();

        // Work backwards: highest affordable dangerous, then the highest
        // standard strictly below it, then the highest cautious strictly below
        // the standard. The first complete triple is the deterministic result.
        foreach (var rankedDangerous in ordered)
        {
            var dangerous = rankedDangerous.Candidate;
            if (dangerous.TotalCost > dangerousTarget)
            {
                continue;
            }

            int standardIndex = FindBelow(
                ordered, dangerous.TotalCost, standardTarget);
            if (standardIndex < 0)
            {
                continue;
            }

            var standard = ordered[standardIndex].Candidate;
            int cautiousIndex = FindBelow(
                ordered, standard.TotalCost, cautiousTarget);
            if (cautiousIndex < 0)
            {
                continue;
            }

            var cautious = ordered[cautiousIndex].Candidate;
            selection = new CostFormationSelection(
                cautious.Units,
                standard.Units,
                dangerous.Units);
            error = string.Empty;
            return true;
        }

        error = $"cost mode: no three real formations with strictly increasing total cost fit cautious {cautiousTarget}, standard {standardTarget} and dangerous {dangerousTarget} for era '{eraId}'";
        return false;
    }

    private static int FindBelow(
        RankedCandidate[] ordered,
        decimal exclusiveUpperBound,
        decimal inclusiveTarget)
    {
        for (int index = 0; index < ordered.Length; index++)
        {
            decimal cost = ordered[index].Candidate.TotalCost;
            if (cost < exclusiveUpperBound && cost <= inclusiveTarget)
            {
                return index;
            }
        }

        return -1;
    }

    private static CostFormationUnit[] BuildUnits(
        Dictionary<string, (EnemyDesignEntry Design, int Count)> counts) =>
        counts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CostFormationUnit(
                pair.Value.Design, pair.Value.Count))
            .ToArray();

    private static string RosterKey(
        IReadOnlyList<CostFormationUnit> units) =>
        string.Join(
            "|",
            units
                .OrderBy(
                    unit => unit.Design.Blueprint.LocalId,
                    StringComparer.Ordinal)
                .Select(unit =>
                    $"{unit.Design.Blueprint.LocalId}x{unit.Count}"));

    private static bool TryComputeCost(
        IReadOnlyList<CostFormationUnit> units,
        decimal maximum,
        out decimal cost)
    {
        cost = 0m;
        int totalCount = 0;

        foreach (var unit in units)
        {
            if (unit.Design?.CheckedPrice is null || unit.Count <= 0)
            {
                return false;
            }

            if (unit.Count > EncounterRules.MaxEnemies - totalCount)
            {
                return false;
            }

            totalCount += unit.Count;

            if (!TryMultiply(
                unit.Design.CheckedPrice.Value,
                unit.Count,
                out decimal lineCost))
            {
                return false;
            }

            if (lineCost > maximum - cost)
            {
                return false;
            }

            cost += lineCost;
        }

        return totalCount > 0
            && totalCount <= EncounterRules.MaxEnemies
            && cost > 0m;
    }

    private static bool TryMultiply(
        decimal value,
        int count,
        out decimal product)
    {
        if (value <= 0m || count <= 0)
        {
            product = 0m;
            return false;
        }

        if (value > decimal.MaxValue / count)
        {
            product = 0m;
            return false;
        }

        product = value * count;
        return true;
    }

    private sealed record CostFormationCandidate(
        IReadOnlyList<CostFormationUnit> Units,
        decimal TotalCost);

    private sealed record RankedCandidate(
        CostFormationCandidate Candidate,
        int Rank);
}
