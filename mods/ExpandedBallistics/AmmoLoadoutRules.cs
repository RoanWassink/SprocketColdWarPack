namespace SprocketExpandedBallistics;

/// <summary>Ammunition mix entry: rounds of one shell profile carried in the racks.</summary>
internal sealed record AmmoLoadoutEntry(string ProfileId, int Count);

/// <summary>One native rack slot. A null profile marks a rack that stays outside the mix.</summary>
internal readonly record struct AmmoLoadoutAssignment(int RackIndex, string? ProfileId, int Amount);

/// <summary>
/// Pure configuration rules for the carried ammunition mix (FR-002). The native game
/// stores one projectile type per rack, so the mix is distributed over whole racks in
/// order: the first rack receives the first type up to its capacity, then the next type
/// continues in the following rack. Total carried rounds never exceed the rack capacity.
/// </summary>
internal static class AmmoLoadoutRules
{
    internal const int MaximumTypes = 8;
    internal const int MaximumCount = 60000;

    internal static IReadOnlyList<AmmoLoadoutEntry> Parse(string? text)
    {
        var entries = new List<AmmoLoadoutEntry>();
        if (string.IsNullOrWhiteSpace(text)) return entries;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator == part.Length - 1) continue;
            var id = part[..separator].Trim();
            var countText = part[(separator + 1)..].Trim();
            if (!Valid(id) || id == ShellProfiles.Vanilla && entries.Any(e => e.ProfileId == ShellProfiles.Vanilla)) continue;
            if (!int.TryParse(countText, out var count) || count <= 0) continue;
            if (!seen.Add(id)) continue;
            entries.Add(new AmmoLoadoutEntry(id, Math.Min(count, MaximumCount)));
            if (entries.Count == MaximumTypes) break;
        }
        return entries;
    }

    private static bool Valid(string id) =>
        id.Length is > 0 and <= 64 && id.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.');

    internal static string Serialize(IReadOnlyList<AmmoLoadoutEntry> entries) =>
        string.Join(';', entries.Where(e => e.Count > 0 && Valid(e.ProfileId)).Take(MaximumTypes)
            .Select(e => $"{e.ProfileId}={Math.Min(e.Count, MaximumCount)}"));

    internal static int Total(IReadOnlyList<AmmoLoadoutEntry> entries) =>
        entries.Sum(e => Math.Max(0, e.Count));

    /// <summary>Clamp the mix to the shared maximum by trimming the last entries first.</summary>
    internal static IReadOnlyList<AmmoLoadoutEntry> Fit(IReadOnlyList<AmmoLoadoutEntry> entries, int capacity)
    {
        var budget = Math.Max(0, capacity);
        var fitted = new List<AmmoLoadoutEntry>();
        foreach (var entry in entries.Take(MaximumTypes))
        {
            if (entry.Count <= 0 || budget <= 0 || !Valid(entry.ProfileId)) continue;
            var count = Math.Min(entry.Count, budget);
            fitted.Add(entry with { Count = count });
            budget -= count;
        }
        return fitted;
    }

    /// <summary>One type per rack, in rack order. Racks outside the mix are returned empty.</summary>
    internal static IReadOnlyList<AmmoLoadoutAssignment> Distribute(IReadOnlyList<int> rackCapacities,
        IReadOnlyList<AmmoLoadoutEntry> entries)
    {
        var assignments = new List<AmmoLoadoutAssignment>();
        var index = 0;
        var used = 0;
        foreach (var capacity in rackCapacities)
        {
            // Carried types without rounds are skipped so they never block the types
            // behind them (the editor keeps a row at zero until it is removed).
            while (index < entries.Count && (entries[index].Count <= 0 || used >= entries[index].Count))
            {
                index++;
                used = 0;
            }
            if (index >= entries.Count || capacity <= 0)
            {
                assignments.Add(new AmmoLoadoutAssignment(assignments.Count, null, 0));
                continue;
            }
            var entry = entries[index];
            var amount = Math.Min(capacity, entry.Count - used);
            assignments.Add(new AmmoLoadoutAssignment(assignments.Count, entry.ProfileId, amount));
            used += amount;
            if (used >= entry.Count)
            {
                index++;
                used = 0;
            }
        }
        return assignments;
    }
    // Native sources remain uniform, but several finite typed sources may share
    // one physical store. This plan spends each physical slot exactly once.
    internal static IReadOnlyList<AmmoLoadoutAssignment> DistributeMixed(IReadOnlyList<int> capacities,IReadOnlyList<AmmoLoadoutEntry> entries)
    {
        var result=new List<AmmoLoadoutAssignment>();var remaining=entries.Select(e=>Math.Max(0,e.Count)).ToArray();
        for(int rack=0;rack<capacities.Count;rack++)
        {
            var free=Math.Max(0,capacities[rack]);var assigned=new int[entries.Count];
            while(free>0)
            {
                var active=Enumerable.Range(0,remaining.Length).Where(i=>remaining[i]>0).ToArray();if(active.Length==0)break;
                var share=Math.Max(1,free/active.Length);
                foreach(var i in active)
                {var amount=Math.Min(free,Math.Min(share,remaining[i]));assigned[i]+=amount;remaining[i]-=amount;free-=amount;if(free==0)break;}
            }
            for(int i=0;i<assigned.Length;i++)if(assigned[i]>0)result.Add(new(rack,entries[i].ProfileId,assigned[i]));
        }
        return result;
    }

    /// <summary>Index of a profile inside the mix, or -1.</summary>
    internal static int IndexOf(IReadOnlyList<AmmoLoadoutEntry> entries, string? profileId)
    {
        if (string.IsNullOrEmpty(profileId)) return -1;
        for (var i = 0; i < entries.Count; i++) if (entries[i].ProfileId == profileId) return i;
        return -1;
    }

    /// <summary>The next carried type with rounds left, searched from the current type.</summary>
    internal static string? Step(IReadOnlyList<AmmoLoadoutEntry> entries, Func<string, int> remaining,
        string? current, int direction)
    {
        if (entries.Count == 0 || direction == 0) return null;
        var step = direction > 0 ? 1 : -1;
        var start = IndexOf(entries, current);
        for (var offset = 1; offset <= entries.Count; offset++)
        {
            var index = ((start + step * offset) % entries.Count + entries.Count) % entries.Count;
            if (remaining(entries[index].ProfileId) > 0) return entries[index].ProfileId;
        }
        return null;
    }

    /// <summary>Direct slot key 0..7.</summary>
    internal static string? Slot(IReadOnlyList<AmmoLoadoutEntry> entries, int index) =>
        index >= 0 && index < entries.Count ? entries[index].ProfileId : null;
}
