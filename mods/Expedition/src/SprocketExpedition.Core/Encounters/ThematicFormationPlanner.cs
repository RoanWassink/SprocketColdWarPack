namespace SprocketExpedition.Core.Encounters;

// Version-4 offers only. Builds one coherent combined-arms group from real pinned
// same-era designs and their checked prices. A cheap design is never re-labelled
// as a scout: Flank/Support only appear when the catalogue author supplied that
// role. If no coherent group can beat the previous risk cost inside the exact
// checked-price baseline, the already validated legacy roster is retained only
// when it also satisfies the same strict cost/progression gate; impossible
// caller input fails explicitly instead of producing inconsistent risk cards.
internal static class ThematicFormationPlanner
{
    private const int MaxEnemies = 12;
    private const int PreferredCentreCount = 3;
    private const decimal MinimumSpend = 0.65m;

    internal static SpecialBattle Create(
        IReadOnlyList<EnemyDesignEntry> pool,
        IReadOnlyList<EnemyUnit> original,
        decimal target,
        decimal previousCost,
        long seed,
        int round,
        int roll)
    {
        // Stable input order is essential: the native catalogue is a dictionary
        // built from file order, and retries must not depend on that order.
        var designs = (pool ?? Array.Empty<EnemyDesignEntry>())
            .Where(design => design?.Blueprint is not null
                && design.CheckedPrice is not null
                && design.CheckedPrice.Value > 0m
                && design.CheckedPrice.UnitId is { Length: > 0 })
            .GroupBy(design => design.Blueprint.LocalId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(design => design.Blueprint.LocalId, StringComparer.Ordinal)
            .ToArray();

        var centres = designs
            .Where(design => design.Role is not (EnemyRole.Recon or EnemyRole.Support or EnemyRole.AntiTank))
            .ToArray();
        if (centres.Length == 0)
            centres = designs.Where(design => design.Role != EnemyRole.Recon).ToArray();
        if (centres.Length == 0)
            return Fallback(original, designs, target, previousCost);

        var flankers = designs.Where(design => design.Role == EnemyRole.Recon).ToArray();
        var supports = designs
            .Where(design => design.Role is EnemyRole.Support or EnemyRole.AntiTank)
            .ToArray();

        Candidate? best = null;

        void Consider(
            EnemyDesignEntry centre,
            int centreCount,
            EnemyDesignEntry? flanker,
            int flankerCount,
            EnemyDesignEntry? support,
            int supportCount)
        {
            var total = centreCount + flankerCount + supportCount;
            if (total is < 1 or > MaxEnemies) return;
            if (flanker is not null
                && string.Equals(flanker.Blueprint.LocalId, centre.Blueprint.LocalId,
                    StringComparison.Ordinal)) return;
            if (support is not null
                && (string.Equals(support.Blueprint.LocalId, centre.Blueprint.LocalId,
                        StringComparison.Ordinal)
                    || flanker is not null
                    && string.Equals(support.Blueprint.LocalId, flanker.Blueprint.LocalId,
                        StringComparison.Ordinal))) return;
            if (!TryCost(centre, centreCount, flanker, flankerCount, support, supportCount,
                    out var cost)
                || cost <= previousCost || cost > target) return;
            var spend = target > 0m ? cost / target : 0m;
            if (spend < MinimumSpend) return;

            var units = new List<SpecialUnit>(3)
            {
                new(centre.Blueprint.LocalId, centreCount)
                {
                    DeploymentRole = PrimaryRole(centre.Role).ToString()
                }
            };
            if (flanker is not null && flankerCount > 0)
                units.Add(new SpecialUnit(flanker.Blueprint.LocalId, flankerCount)
                {
                    DeploymentRole = RoleName(EnemyRole.Recon)
                });
            if (support is not null && supportCount > 0)
                units.Add(new SpecialUnit(support.Blueprint.LocalId, supportCount)
                {
                    DeploymentRole = RoleName(EnemyRole.Support)
                });

            var value = (target - cost) / target
                + Math.Abs(centreCount - PreferredCentreCount) * .02m
                + (flanker is null ? .04m : -.05m)
                + (support is null ? 0m : -.015m);
            var title = Title(centre.FormationFamily,
                units.Any(unit => unit.DeploymentRole == CampaignRole.Flank.ToString()),
                units.Any(unit => unit.DeploymentRole == CampaignRole.Support.ToString()));
            var roster = string.Join("|", units.Select(unit =>
                $"{unit.BlueprintId}x{unit.Count}:{unit.DeploymentRole}"));
            var tie = CanonicalText.Sha256(
                $"{seed}|{round}|{roll}|{centre.Blueprint.LocalId}|"
                + $"{flanker?.Blueprint.LocalId ?? "-"}|{support?.Blueprint.LocalId ?? "-"}|{roster}");

            if (best is not null
                && (value > best.Value
                    || value == best.Value
                    && string.CompareOrdinal(tie, best.TieKey) >= 0))
                return;

            best = new Candidate(units.ToArray(), cost, title, tie, value);
        }

        if (target > 0m && designs.Length > 0)
        {
            foreach (var centre in centres)
            {
                var affordable = decimal.Floor(target / centre.CheckedPrice.Value);
                var maxCentre = affordable >= MaxEnemies ? MaxEnemies : (int)affordable;
                for (var centreCount = 1; centreCount <= maxCentre; centreCount++)
                {
                    Consider(centre, centreCount, null, 0, null, 0);
                    foreach (var flanker in flankers)
                    {
                        Consider(centre, centreCount, flanker, 2, null, 0);
                        Consider(centre, centreCount, flanker, 1, null, 0);
                        if (centreCount + 2 < MaxEnemies)
                            foreach (var support in supports)
                                Consider(centre, centreCount, flanker, 2, support, 1);
                    }

                    if (centreCount < MaxEnemies)
                        foreach (var support in supports)
                            Consider(centre, centreCount, null, 0, support, 1);
                }
            }
        }

        if (best is null)
            return Fallback(original, designs, target, previousCost);

        return new SpecialBattle(
            SpecialPreset.TacticalGroup,
            best.Title,
            SpecialSpawnPattern.CombinedArms,
            best.Units,
            string.Empty,
            0m,
            0);
    }

    private static bool TryCost(
        EnemyDesignEntry centre,
        int centreCount,
        EnemyDesignEntry? flanker,
        int flankerCount,
        EnemyDesignEntry? support,
        int supportCount,
        out decimal cost)
    {
        cost = 0m;
        try
        {
            cost = checked(centre.CheckedPrice.Value * centreCount);
            if (flanker is not null)
                cost = checked(cost + flanker.CheckedPrice.Value * flankerCount);
            if (support is not null)
                cost = checked(cost + support.CheckedPrice.Value * supportCount);
            return true;
        }
        catch (OverflowException)
        {
            cost = 0m;
            return false;
        }
    }

    private static SpecialBattle Fallback(
        IReadOnlyList<EnemyUnit> original,
        IReadOnlyList<EnemyDesignEntry> designs,
        decimal target,
        decimal previousCost)
    {
        if (original is null || original.Count == 0)
            throw new InvalidOperationException(
                "Thematic fallback has no original roster.");
        if (target <= 0m)
            throw new InvalidOperationException(
                "Thematic fallback target budget must be positive.");

        var byId = designs.ToDictionary(design => design.Blueprint.LocalId,
            StringComparer.Ordinal);
        var priceUnits = designs
            .Select(design => design.CheckedPrice.UnitId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (byId.Count == 0 || priceUnits.Length != 1
            || string.IsNullOrWhiteSpace(priceUnits[0]))
            throw new InvalidOperationException(
                "Thematic fallback requires one checked price unit across real designs.");

        decimal cost = 0m;
        var total = 0;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in original)
        {
            if (unit is null || string.IsNullOrWhiteSpace(unit.BlueprintId)
                || unit.Count <= 0)
                throw new InvalidOperationException(
                    "Thematic fallback original roster contains an invalid unit.");
            if (!seenIds.Add(unit.BlueprintId))
                throw new InvalidOperationException(
                    $"Thematic fallback original roster repeats '{unit.BlueprintId}'.");
            if (!byId.TryGetValue(unit.BlueprintId, out var design)
                || design.Blueprint is null
                || design.CheckedPrice is null
                || design.CheckedPrice.Value <= 0m
                || !string.Equals(design.CheckedPrice.UnitId, priceUnits[0],
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Thematic fallback original unit '{unit.BlueprintId}' is missing or has no valid checked price.");
            if (unit.Count > MaxEnemies - total)
                throw new InvalidOperationException(
                    "Thematic fallback roster exceeds the native slot cap.");
            total += unit.Count;
            try { cost = checked(cost + design.CheckedPrice.Value * unit.Count); }
            catch (OverflowException)
            {
                throw new InvalidOperationException(
                    "Thematic fallback roster cost overflowed.");
            }
        }

        if (cost <= previousCost)
            throw new InvalidOperationException(
                $"Thematic fallback cost {cost} cannot preserve strict risk progression above {previousCost}.");
        if (cost > target)
            throw new InvalidOperationException(
                $"Thematic fallback cost {cost} exceeds the checked target budget {target}.");

        var units = original
            .Select(unit =>
            {
                var design = byId[unit.BlueprintId];
                var role = DeploymentRole(design.Role);
                return new SpecialUnit(unit.BlueprintId, unit.Count)
                {
                    DeploymentRole = role.ToString()
                };
            })
            .ToArray();
        string? family = null;
        var centreUnit = units.FirstOrDefault(unit =>
            string.Equals(unit.DeploymentRole, CampaignRole.Center.ToString(),
                StringComparison.Ordinal));
        if (centreUnit is not null
            && byId.TryGetValue(centreUnit.BlueprintId, out var centreDesign))
            family = centreDesign.FormationFamily;
        if (string.IsNullOrWhiteSpace(family))
            foreach (var unit in units)
                if (byId.TryGetValue(unit.BlueprintId, out var design)
                    && !string.IsNullOrWhiteSpace(design.FormationFamily))
                {
                    family = design.FormationFamily;
                    break;
                }

        var hasFlank = units.Any(unit => unit.DeploymentRole == CampaignRole.Flank.ToString());
        var hasSupport = units.Any(unit => unit.DeploymentRole == CampaignRole.Support.ToString());
        return new SpecialBattle(
            SpecialPreset.TacticalGroup,
            Title(family, hasFlank, hasSupport),
            SpecialSpawnPattern.CombinedArms,
            units,
            string.Empty,
            0m,
            0);
    }

    private static string RoleName(EnemyRole role) => DeploymentRole(role).ToString();

    private static CampaignRole PrimaryRole(EnemyRole role) =>
        role is EnemyRole.Support or EnemyRole.AntiTank
            ? CampaignRole.Support : CampaignRole.Center;

    private static string Title(string? family, bool hasFlank, bool hasSupport)
    {
        var theme = string.IsNullOrWhiteSpace(family) ? "Armoured" : family.Trim();
        if (theme.Length > 48) theme = theme[..48].TrimEnd();
        var title = theme + " battlegroup";
        if (hasFlank) title += " · light flanks";
        if (hasSupport) title += " · support";
        return title;
    }

    private static CampaignRole DeploymentRole(EnemyRole role) => role switch
    {
        EnemyRole.Recon => CampaignRole.Flank,
        EnemyRole.Support or EnemyRole.AntiTank => CampaignRole.Support,
        _ => CampaignRole.Center
    };

    private enum CampaignRole
    {
        Center,
        Flank,
        Support
    }

    private sealed record Candidate(
        SpecialUnit[] Units,
        decimal Cost,
        string Title,
        string TieKey,
        decimal Value);
}
