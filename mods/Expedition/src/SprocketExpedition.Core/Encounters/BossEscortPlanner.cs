namespace SprocketExpedition.Core.Encounters;

// Adventure policy 7 boss character escort planning. Pure and deterministic:
// the same seed/round/roll always produces the same escort roster and pattern.
// Older rules versions never call this type, so their boss bytes stay unchanged.
internal enum BossEscortCharacter { Fortress, Hunter, Battery, Veteran }

internal sealed record BossEscortPlan(
    IReadOnlyList<EnemyUnit> Escorts,
    SpecialBattle? Formation);

internal static class BossEscortPlanner
{
    internal const int MaximumHulls = 12;

    internal static BossEscortCharacter CharacterFor(int eraIndex)
    {
        if (eraIndex < 0) throw new ArgumentOutOfRangeException(nameof(eraIndex));
        return (BossEscortCharacter)(eraIndex % 4);
    }

    internal static BossEscortPlan Plan(
        BossEscortCharacter character,
        EnemyDesignEntry boss,
        IReadOnlyList<EnemyDesignEntry> pool,
        IReadOnlyList<EnemyUnit> legacyEscorts,
        long seed,
        int round,
        int roll)
    {
        ArgumentNullException.ThrowIfNull(boss);
        var designs = (pool ?? Array.Empty<EnemyDesignEntry>())
            .Where(design => design?.Blueprint is not null
                && design.CheckedPrice is { Value: > 0m }
                && design.CheckedPrice.UnitId is { Length: > 0 }
                && !string.Equals(design.Blueprint.LocalId, boss.Blueprint.LocalId, StringComparison.Ordinal))
            .GroupBy(design => design.Blueprint.LocalId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(design => design.CheckedPrice!.Value)
            .ThenBy(design => design.Blueprint.LocalId, StringComparer.Ordinal)
            .ToArray();

        var escorts = character == BossEscortCharacter.Hunter
            ? new List<EnemyUnit>()
            : UseLegacy(legacyEscorts, designs);
        if (escorts.Count == 0)
            escorts = Fallback(character, designs, seed, round, roll);

        escorts = Trim(escorts);
        if (character == BossEscortCharacter.Veteran)
            return new BossEscortPlan(escorts, null);

        var role = character == BossEscortCharacter.Hunter ? "Flank"
            : character == BossEscortCharacter.Battery ? "Support"
            : "Center";
        var pattern = character == BossEscortCharacter.Hunter ? SpecialSpawnPattern.Ambush
            : character == BossEscortCharacter.Battery ? SpecialSpawnPattern.CombinedArms
            : SpecialSpawnPattern.Defensive;
        var units = new List<SpecialUnit>(escorts.Count + 1)
        {
            new(boss.Blueprint.LocalId, 1) { DeploymentRole = role }
        };
        units.AddRange(escorts.Select(escort => new SpecialUnit(escort.BlueprintId, escort.Count)
        {
            DeploymentRole = role
        }));
        var title = character switch
        {
            BossEscortCharacter.Fortress => "The Fortress dug in",
            BossEscortCharacter.Hunter => "The Hunter ambush",
            BossEscortCharacter.Battery => "The Battery at range",
            _ => "Boss escort"
        };
        return new BossEscortPlan(escorts,
            new SpecialBattle(SpecialPreset.TacticalGroup, title, pattern, units,
                string.Empty, 0m, 0));
    }

    private static List<EnemyUnit> UseLegacy(
        IReadOnlyList<EnemyUnit>? legacy,
        IReadOnlyList<EnemyDesignEntry> designs)
    {
        var known = designs.ToDictionary(design => design.Blueprint.LocalId,
            design => design.CheckedPrice!.Value, StringComparer.Ordinal);
        var result = new List<EnemyUnit>();
        foreach (var entry in legacy ?? Array.Empty<EnemyUnit>())
        {
            if (entry is null || entry.Count <= 0 || !known.ContainsKey(entry.BlueprintId)) continue;
            result.Add(new EnemyUnit(entry.BlueprintId, entry.Count));
        }
        return result;
    }

    private static List<EnemyUnit> Fallback(
        BossEscortCharacter character,
        IReadOnlyList<EnemyDesignEntry> designs,
        long seed,
        int round,
        int roll)
    {
        if (designs.Count == 0) return new List<EnemyUnit>();
        var wanted = character switch
        {
            BossEscortCharacter.Fortress => EnemyRole.Line,
            BossEscortCharacter.Hunter => EnemyRole.Recon,
            BossEscortCharacter.Battery => EnemyRole.Support,
            _ => EnemyRole.Line
        };
        var preferred = designs
            .Where(design => EnemyDesignClassifier.PlannedRole(design) == wanted
                || wanted == EnemyRole.Support
                && EnemyDesignClassifier.PlannedRole(design) == EnemyRole.AntiTank)
            .ToArray();
        var source = preferred.Length > 0 ? preferred : designs.ToArray();
        var start = (int)(Hash(seed, round, roll, (int)character) % (uint)source.Length);
        var count = Math.Clamp(character == BossEscortCharacter.Hunter ? 3 : 2, 1, source.Length);
        var result = new List<EnemyUnit>();
        for (var i = 0; i < count; i++)
            result.Add(new EnemyUnit(source[(start + i) % source.Length].Blueprint.LocalId, 1));
        return result
            .GroupBy(unit => unit.BlueprintId, StringComparer.Ordinal)
            .Select(group => new EnemyUnit(group.Key, group.Sum(unit => unit.Count)))
            .ToList();
    }

    private static List<EnemyUnit> Trim(List<EnemyUnit> escorts)
    {
        var result = new List<EnemyUnit>();
        var remaining = MaximumHulls - 1;
        foreach (var escort in escorts)
        {
            if (remaining <= 0) break;
            var count = Math.Min(escort.Count, remaining);
            if (count > 0) result.Add(new EnemyUnit(escort.BlueprintId, count));
            remaining -= count;
        }
        return result;
    }

    private static ulong Hash(long seed, int round, int roll, int tag)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var value in new[] { (ulong)seed, (uint)round, (uint)roll, (uint)tag })
            {
                var current = value;
                for (var i = 0; i < 8; i++)
                {
                    hash ^= (byte)(current & 0xFF);
                    hash *= 1099511628211UL;
                    current >>= 8;
                }
            }
            return hash;
        }
    }
}

