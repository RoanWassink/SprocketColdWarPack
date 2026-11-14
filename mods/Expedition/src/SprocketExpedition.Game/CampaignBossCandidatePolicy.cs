namespace SprocketExpedition.Game;

// Managed config policy, shared with tests; no native card or config dependency.
internal sealed record BossCandidateDesign(string Id, string Name, string Era);
internal static class CampaignBossCandidatePolicy
{
    internal static IReadOnlyList<string> Resolve(string setting, string era, string nextEra,
        IEnumerable<BossCandidateDesign> designs, Action<string>? warning = null)
    {
        var loaded = designs.ToArray();
        var result = new List<string>();
        foreach (var token in setting.Split('|').Select(s => s.Trim()).Distinct(StringComparer.Ordinal))
        {
            var matches = loaded.Where(t => (t.Name == token || t.Id == token) && t.Era == nextEra).ToArray();
            if (matches.Length == 1)
            {
                if (!result.Contains(matches[0].Id, StringComparer.Ordinal)) result.Add(matches[0].Id);
            }
            else warning?.Invoke($"blueprint '{token}' for {era} does not uniquely match an active {nextEra} design; candidate ignored.");
        }
        return result;
    }
}
