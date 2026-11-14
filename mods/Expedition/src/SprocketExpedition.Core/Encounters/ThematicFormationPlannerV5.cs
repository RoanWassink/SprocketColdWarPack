using System.Globalization;

namespace SprocketExpedition.Core.Encounters;

// Adventure policy 5. Builds a harder, varied combined-arms group from the
// pinned same-era catalogue and its checked native prices. The policy-4 planner
// remains byte-for-byte in charge of v4 offers; this type is only reached from
// an explicit RulesVersion >= 5 request.
internal static class ThematicFormationPlannerV5
{
    private const int MaxEnemies = 12;
    private const decimal MinimumSpend = 0.65m;
    // Pick the theme deterministically among centre designs that spend within
    // 15% of the best achievable checked-price fill. This keeps budgets honest
    // while changing the chosen real family across rounds.
    private const decimal ThemeSpendWindow = 0.15m;
    private const int MaximumCentreCandidates = 8;
    private const int MaximumFlankCandidates = 4;
    private const int MaximumSupportCandidates = 2;

    internal static SpecialBattle Create(
        IReadOnlyList<EnemyDesignEntry> pool,
        IReadOnlyList<EnemyUnit> original,
        decimal target,
        decimal previousCost,
        long seed,
        int round,
        int roll,
        int eraIndex,
        int riskIndex,
        int rulesVersion = 5)
    {
        if (target <= 0m || pool is null || pool.Count == 0)
            return Legacy(pool, original, target, previousCost, seed, round, roll);

        var designs = pool
            .Where(design => design?.Blueprint is not null
                && design.CheckedPrice is not null
                && design.CheckedPrice.Value > 0m
                && design.CheckedPrice.UnitId is { Length: > 0 })
            .GroupBy(design => design.Blueprint.LocalId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(design => design.Blueprint.LocalId, StringComparer.Ordinal)
            .ToArray();

        var centres = designs
            .Where(design => EnemyDesignClassifier.PlannedRole(design) is EnemyRole.Line or EnemyRole.Heavy)
            .ToArray();
        if (centres.Length == 0)
            return Legacy(pool, original, target, previousCost, seed, round, roll);

        var flankers = designs
            .Where(design => EnemyDesignClassifier.PlannedRole(design) == EnemyRole.Recon)
            .ToArray();
        var supports = designs
            .Where(design => EnemyDesignClassifier.PlannedRole(design) is EnemyRole.Support or EnemyRole.AntiTank)
            .ToArray();

        centres = Rotate(centres, MaximumCentreCandidates,
            StableKey(seed, round, roll, eraIndex, riskIndex, "centres"));
        flankers = Rotate(flankers, MaximumFlankCandidates,
            StableKey(seed, round, roll, eraIndex, riskIndex, "flanks"));
        supports = Rotate(supports, MaximumSupportCandidates,
            StableKey(seed, round, roll, eraIndex, riskIndex, "support"));

        var bestByCentre = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        var spendWindow = SafeSpendWindow(target);

        void Consider(EnemyDesignEntry centre, int centreCount,
            EnemyDesignEntry? flanker, int flankerCount,
            EnemyDesignEntry? support, int supportCount)
        {
            var total = centreCount + flankerCount + supportCount;
            if (centreCount < 1 || total > MaxEnemies) return;
            if (flanker is not null && Same(flanker, centre)) return;
            if (support is not null
                && (Same(support, centre) || flanker is not null && Same(support, flanker)))
                return;
            if (!TryCost(centre, centreCount, flanker, flankerCount, support, supportCount,
                    out var cost)
                || cost <= previousCost || cost > target) return;
            var spend = cost / target;
            if (spend < MinimumSpend) return;

            var units = new List<SpecialUnit>(3)
            {
                new(centre.Blueprint.LocalId, centreCount)
                {
                    DeploymentRole = CampaignRole.Center
                }
            };
            if (flanker is not null && flankerCount > 0)
                units.Add(new SpecialUnit(flanker.Blueprint.LocalId, flankerCount)
                {
                    DeploymentRole = CampaignRole.Flank
                });
            if (support is not null && supportCount > 0)
                units.Add(new SpecialUnit(support.Blueprint.LocalId, supportCount)
                {
                    DeploymentRole = CampaignRole.Support
                });

            var candidate = new Candidate(
                centre.Blueprint.LocalId,
                units.ToArray(),
                cost,
                flanker is not null && flankerCount > 0,
                support is not null && supportCount > 0,
                Family(centre));
            if (bestByCentre.TryGetValue(candidate.CentreId, out var existing))
            {
                if (candidate.Cost > existing.Cost
                    || candidate.Cost == existing.Cost && candidate.Flankers && !existing.Flankers
                    || candidate.Flankers && !existing.Flankers
                        && candidate.Cost >= existing.Cost - spendWindow)
                {
                    bestByCentre[candidate.CentreId] = candidate;
                }
            }
            else
            {
                bestByCentre[candidate.CentreId] = candidate;
            }
        }

        foreach (var centre in centres)
        {
            var affordable = decimal.Floor(target / centre.CheckedPrice!.Value);
            var maxCentre = affordable >= MaxEnemies ? MaxEnemies : (int)affordable;
            if (maxCentre < 1) continue;

            for (var centreCount = 1; centreCount <= maxCentre; centreCount++)
            {
                Consider(centre, centreCount, null, 0, null, 0);
                if (centreCount < MaxEnemies)
                    foreach (var support in supports)
                        Consider(centre, centreCount, null, 0, support, 1);
                if (flankers.Length == 0) continue;
                foreach (var flanker in flankers)
                {
                    if (centreCount + 2 > MaxEnemies) break;
                    Consider(centre, centreCount, flanker, 2, null, 0);
                    if (centreCount + 3 <= MaxEnemies)
                        Consider(centre, centreCount, flanker, 3, null, 0);
                    if (centreCount + 2 < MaxEnemies)
                        foreach (var support in supports)
                            Consider(centre, centreCount, flanker, 2, support, 1);
                }
            }
        }

        if (bestByCentre.Count == 0)
            return Legacy(pool, original, target, previousCost, seed, round, roll);

        var bestFill = bestByCentre.Values.Max(candidate => candidate.Cost / target);
        var eligible = bestByCentre.Values
            .Where(candidate => candidate.Cost / target >= bestFill - ThemeSpendWindow
                && candidate.Cost / target >= MinimumSpend)
            .OrderBy(candidate => candidate.CentreId, StringComparer.Ordinal)
            .ToArray();
        if (eligible.Length == 0)
            return Legacy(pool, original, target, previousCost, seed, round, roll);
        var chosen = eligible[(int)(StableKey(seed, round, roll, eraIndex, riskIndex,
            "theme") % (uint)eligible.Length)];

        var ambush = false;
        if (rulesVersion >= 7)
        {
            // Policy 7: every tactical group of two or more hulls can ambush.
            // A group with flankers rolls 1 in 2; one without rolls 1 in 3.
            var hulls = chosen.Units.Sum(unit => unit.Count);
            if (hulls >= 2)
            {
                var key = StableKey(seed, round, roll, eraIndex, riskIndex, "ambush-v7");
                ambush = chosen.Flankers ? key % 2u == 0u : key % 3u == 0u;
            }
        }
        else
        {
            ambush = chosen.Flankers
                && (round + riskIndex
                    + (int)(StableKey(seed, 0, roll, eraIndex, 0, "pattern-offset") % 3u)) % 3 == 0;
        }
        var pattern = ambush ? SpecialSpawnPattern.Ambush : SpecialSpawnPattern.CombinedArms;
        return new SpecialBattle(
            SpecialPreset.TacticalGroup,
            Title(chosen.Family, chosen.Flankers, chosen.Supporters, ambush),
            pattern,
            chosen.Units,
            string.Empty,
            0m,
            0);
    }

    private static SpecialBattle Legacy(
        IReadOnlyList<EnemyDesignEntry>? pool,
        IReadOnlyList<EnemyUnit>? original,
        decimal target,
        decimal previousCost,
        long seed,
        int round,
        int roll) =>
        ThematicFormationPlanner.Create(
            pool ?? Array.Empty<EnemyDesignEntry>(),
            original ?? Array.Empty<EnemyUnit>(),
            target,
            previousCost,
            seed,
            round,
            roll);

    private static bool Same(EnemyDesignEntry left, EnemyDesignEntry right) =>
        string.Equals(left.Blueprint.LocalId, right.Blueprint.LocalId, StringComparison.Ordinal);

    private static bool TryCost(
        EnemyDesignEntry centre, int centreCount,
        EnemyDesignEntry? flanker, int flankerCount,
        EnemyDesignEntry? support, int supportCount,
        out decimal cost)
    {
        cost = 0m;
        try
        {
            cost = checked(centre.CheckedPrice!.Value * centreCount);
            if (flanker is not null && flankerCount > 0)
                cost = checked(cost + flanker.CheckedPrice!.Value * flankerCount);
            if (support is not null && supportCount > 0)
                cost = checked(cost + support.CheckedPrice!.Value * supportCount);
            return true;
        }
        catch (OverflowException)
        {
            cost = 0m;
            return false;
        }
    }

    private static EnemyDesignEntry[] Rotate(
        EnemyDesignEntry[] values, int maximum, uint offset)
    {
        if (values.Length == 0) return values;
        var count = Math.Min(values.Length, maximum);
        var start = (int)(offset % (uint)values.Length);
        var result = new EnemyDesignEntry[count];
        for (var index = 0; index < count; index++)
            result[index] = values[(start + index) % values.Length];
        return result;
    }

    private static string Family(EnemyDesignEntry design)
    {
        var family = design.FormationFamily;
        if (string.IsNullOrWhiteSpace(family)) family = design.Blueprint.LocalId;
        var safe = new string(family!.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe)) safe = "Armoured";
        return safe.Length > 40 ? safe[..40].TrimEnd() : safe;
    }

    private static string Title(string family, bool flankers, bool supporters, bool ambush)
    {
        var title = family + " battlegroup";
        if (ambush) title += " · ambush";
        else if (flankers) title += " · recon flanks";
        if (supporters) title += " · support";
        return title;
    }

    private static decimal SafeSpendWindow(decimal target)
    {
        try
        {
            return checked(target * ThemeSpendWindow);
        }
        catch (OverflowException)
        {
            return target;
        }
    }

    private static uint StableKey(
        long seed, int round, int roll, int eraIndex, int riskIndex, string channel)
    {
        var hash = CanonicalText.Sha256(
            string.Format(CultureInfo.InvariantCulture,
                "thematic-v5|{0}|{1}|{2}|{3}|{4}|{5}",
                seed, round, roll, eraIndex, riskIndex, channel));
        return uint.Parse(hash[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static class CampaignRole
    {
        internal const string Center = "Center";
        internal const string Flank = "Flank";
        internal const string Support = "Support";
    }

    private sealed record Candidate(
        string CentreId,
        SpecialUnit[] Units,
        decimal Cost,
        bool Flankers,
        bool Supporters,
        string Family);
}
