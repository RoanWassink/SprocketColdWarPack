using System.Globalization;

namespace SprocketExpedition.Core.Encounters;

public static class DesignCapPolicy
{
    public const string DefaultConfig = "T-84 Oplot-M Duplet (WT-maten)=2";

    // Ordinal native names; the first valid entry wins. A duplicate is invalid.
    public static IReadOnlyDictionary<string, int> Parse(string? text, Action<string>? warning = null)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in (text ?? "").Split(';'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var entry = raw.Trim();
            var separator = entry.LastIndexOf('=');
            var name = separator > 0 ? entry[..separator].Trim() : "";
            if (name.Length == 0 || name.Contains('=') || name.Any(char.IsControl)
                || !int.TryParse(entry[(separator + 1)..].Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var count) || count <= 0
                || result.ContainsKey(name))
            {
                warning?.Invoke($"Invalid DesignCaps entry '{entry}'; ignored.");
                continue;
            }
            result.Add(name, count);
        }
        return result;
    }

    public static bool SkipFactionBlueprint(string path) =>
        Path.GetFileName(path).StartsWith("Autosave", StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetExtension(path), ".blueprint", StringComparison.OrdinalIgnoreCase);

    // Final v7 boundary shared by normal, special and boss planners. Preserve
    // the chosen formation where possible, then refill using the same bounded
    // ascending/descending greedy rotations as the cost planner. Never index by
    // currency or perform an unbounded combinatorial search.
    internal static IReadOnlyList<EnemyUnit> Apply(IReadOnlyList<EnemyUnit> original,
        IReadOnlyList<EnemyDesignEntry> pool, decimal target, string? bossId = null)
    {
        var designs = pool.GroupBy(d => d.Blueprint.LocalId, StringComparer.Ordinal)
            .Select(g => g.First()).OrderBy(d => d.Blueprint.LocalId, StringComparer.Ordinal).ToArray();
        var lookup = designs.ToDictionary(d => d.Blueprint.LocalId, StringComparer.Ordinal);
        var counts = original.GroupBy(u => u.BlueprintId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(u => u.Count), StringComparer.Ordinal);
        var changed = false;
        foreach (var id in counts.Keys.ToArray())
        {
            if (!lookup.TryGetValue(id, out var design)) continue; // upcoming-era boss
            var limit = design.MaxPerBattle ?? 12;
            if (counts[id] > limit) { counts[id] = limit; changed = true; }
        }
        if (!changed) return original;
        decimal Cost(Dictionary<string, int> roster) => roster.Sum(p =>
            lookup.TryGetValue(p.Key, out var d) ? d.CheckedPrice.Value * p.Value : 0m);
        var best = counts;
        var bestCost = Cost(best);
        var candidates = designs.Where(d => d.Blueprint.LocalId != bossId).ToArray();
        foreach (var ascending in new[] { true, false })
        {
            var ranked = (ascending ? candidates.OrderBy(d => d.CheckedPrice.Value)
                : candidates.OrderByDescending(d => d.CheckedPrice.Value)).ToArray();
            for (var offset = 0; offset < ranked.Length; offset++)
            {
                var trial = new Dictionary<string, int>(counts, StringComparer.Ordinal);
                var spent = Cost(trial);
                var hulls = trial.Values.Sum();
                for (var i = 0; i < ranked.Length && hulls < 12; i++)
                {
                    var design = ranked[(offset + i) % ranked.Length];
                    var id = design.Blueprint.LocalId;
                    trial.TryGetValue(id, out var have);
                    var room = Math.Min(12 - hulls, (design.MaxPerBattle ?? 12) - have);
                    var add = (int)Math.Min(Math.Max(0, room),
                        Math.Max(0m, decimal.Floor((target - spent) / design.CheckedPrice.Value)));
                    if (add == 0) continue;
                    trial[id] = have + add;
                    hulls += add;
                    spent += design.CheckedPrice.Value * add;
                }
                if (spent > bestCost) { best = trial; bestCost = spent; }
            }
        }
        return best.Where(p => p.Value > 0).Select(p => new EnemyUnit(p.Key, p.Value)).ToArray();
    }
}
