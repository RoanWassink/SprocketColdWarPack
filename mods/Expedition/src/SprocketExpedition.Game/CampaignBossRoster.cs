#if CAMPAIGN
using BepInEx.Configuration;

namespace SprocketExpedition.Game;

internal static class CampaignBossRoster
{
    // Config keys that existed before the era list became dynamic. They stay
    // bound so a user's stored boss choices are kept. Other native eras get a key
    // the first time the game reports them (see Slot).
    private static readonly string[] LegacyConfigEras =
        { "WWI", "Interwar", "Earlywar", "Midwar", "Latewar", "Coldwar" };
    private static readonly Dictionary<string, ConfigEntry<string>> slots = new(StringComparer.Ordinal);
    private static ConfigFile? file;

    internal static void Configure(ConfigFile config)
    {
        file = config;
        foreach (var era in LegacyConfigEras) Slot(era);
        try
        {
            // The native list may not be loaded yet at plugin start; Resolve binds
            // any era that is still missing when a catalogue is built.
            foreach (var era in CampaignNativeCatalogue.EraNames) Slot(era);
        }
        catch { /* bound later by Resolve */ }
    }

    private static ConfigEntry<string>? Slot(string era)
    {
        if (slots.TryGetValue(era, out var existing)) return existing;
        if (file == null) return null;
        return slots[era] = file.Bind("BossBlueprints", era, "",
            "Exact blueprint names or native content IDs separated by | for this era's boss. Use a tank from the upcoming native era; the final included era is its own boss source. Empty uses the nearest earlier era's choice when it fits this era, otherwise a labelled automatic placeholder. Applied to new run catalogues.");
    }

    internal static IReadOnlyDictionary<string, string>? Resolve(
        IEnumerable<CampaignNativeCatalogue.Tank> tanks,
        IReadOnlyList<string> activeEras,
        out IReadOnlyDictionary<string, IReadOnlyList<string>>? candidates,
        Action<string>? diagnostic = null, Action<string>? info = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var multiple = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var loaded = tanks.ToArray();
        string? lastSetting = null;
        for (var index = 0; index < activeEras.Count; index++)
        {
            var era = activeEras[index];
            var own = Slot(era)?.Value;
            var inherited = string.IsNullOrWhiteSpace(own);
            if (!inherited)
            {
                lastSetting = own;
            }
            // An era without its own boss uses the nearest earlier era that has one.
            var setting = inherited ? lastSetting : own;
            if (string.IsNullOrWhiteSpace(setting)) continue;
            // The final included era is its own boss source, so an excluded era
            // never makes a mapping unusable: it falls back automatically.
            var nextEra = activeEras[Math.Min(index + 1, activeEras.Count - 1)];
            var ids = CampaignBossCandidatePolicy.Resolve(setting, era, nextEra,
                loaded.Select(t => new BossCandidateDesign(t.Id, t.Name, t.Era)), diagnostic);
            if (ids.Count == 0) continue;
            result[era] = ids[0];
            if (ids.Count > 1) multiple[era] = ids;
            info?.Invoke($"Expedition boss candidates: {era} -> "
                + string.Join("|", ids.Select(id => loaded.Single(t => t.Id == id).Name)));
        }

        // Entries for an era outside the schedule are intentionally ignored. They
        // are retained in config, not deleted or rewritten.
        foreach (var pair in slots)
        {
            if (string.IsNullOrWhiteSpace(pair.Value.Value)
                || activeEras.Contains(pair.Key, StringComparer.Ordinal)) continue;
            diagnostic?.Invoke($"blueprint mapping for excluded era {pair.Key} ignored; config value retained.");
        }
        candidates = multiple.Count == 0 ? null : multiple;
        return result.Count == 0 ? null : result;
    }
}
#endif
