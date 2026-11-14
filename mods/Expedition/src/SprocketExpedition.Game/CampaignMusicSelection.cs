using System;
using System.Collections.Generic;
using System.IO;

namespace SprocketExpedition.Game;

// Pure music configuration and rotation policy shared by the native adapter and
// its managed checks. No Unity calls and no audio decoding happen here.
internal static class CampaignMusicOptions
{
    internal static readonly IReadOnlyDictionary<CampaignMusicCue, string> DefaultFiles =
        new Dictionary<CampaignMusicCue, string>
        {
            [CampaignMusicCue.Background] = "Mechanical Tension.wav",
            [CampaignMusicCue.Build] = "Minimal Focus Loop.wav",
            [CampaignMusicCue.Combat] = "Heavy Industry.wav",
            [CampaignMusicCue.CombatWWI] = "Heavy Industry.wav",
            [CampaignMusicCue.CombatInterwar] = "Heavy Industry.wav",
            [CampaignMusicCue.CombatEarlywar] = "Heavy Industry.wav",
            [CampaignMusicCue.CombatMidwar] = "Heavy Industry.wav",
            [CampaignMusicCue.CombatLatewar] = "Heavy Industry.wav",
            [CampaignMusicCue.CombatColdwar] = "Heavy Industry.wav",
            [CampaignMusicCue.BossBattle] = "The Final Blow.wav",
            [CampaignMusicCue.Victory] = "Triumph Complete.wav",
            [CampaignMusicCue.Defeat] = "Skill Issue.wav"
        };

    internal static bool IsCombatCue(CampaignMusicCue cue) => cue is CampaignMusicCue.Combat
        or CampaignMusicCue.CombatWWI or CampaignMusicCue.CombatInterwar or CampaignMusicCue.CombatEarlywar
        or CampaignMusicCue.CombatMidwar or CampaignMusicCue.CombatLatewar or CampaignMusicCue.CombatColdwar;

    internal static bool Loops(CampaignMusicCue cue) => cue is CampaignMusicCue.Background
        or CampaignMusicCue.Build or CampaignMusicCue.BossBattle || IsCombatCue(cue);

    // The game ships six era cues. A native era that has no cue of its own (for
    // example Coldwar2 or Coldwar3) uses the cue of the last known era before it in
    // eraOrder (native era names in start-date order); without an order, a trailing
    // number is dropped ("Coldwar2" -> "Coldwar"). Unknown names use the shared Combat cue.
    internal static CampaignMusicCue CombatCueForEra(string? eraId, IReadOnlyList<string>? eraOrder = null)
    {
        var known = KnownCue(eraId);
        if (known != CampaignMusicCue.Combat || string.IsNullOrWhiteSpace(eraId)) return known;
        var name = eraId!.Trim();
        if (eraOrder != null)
        {
            var position = -1;
            for (var index = 0; index < eraOrder.Count; index++)
                if (string.Equals(eraOrder[index], name, StringComparison.OrdinalIgnoreCase)) { position = index; break; }
            for (var index = position - 1; index >= 0; index--)
            {
                var earlier = KnownCue(eraOrder[index]);
                if (earlier != CampaignMusicCue.Combat) return earlier;
            }
            if (position >= 0) return known;
        }
        return KnownCue(name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'));
    }

    // Native era names of the six built-in cues.
    private static CampaignMusicCue KnownCue(string? eraId) => (eraId ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "wwi" => CampaignMusicCue.CombatWWI,
        "interwar" => CampaignMusicCue.CombatInterwar,
        "earlywar" => CampaignMusicCue.CombatEarlywar,
        "midwar" => CampaignMusicCue.CombatMidwar,
        "latewar" => CampaignMusicCue.CombatLatewar,
        "coldwar" => CampaignMusicCue.CombatColdwar,
        _ => CampaignMusicCue.Combat
    };

    // Era-specific combat pool wins when configured; blank or fully invalid input
    // inherits the shared Combat pool; the built-in track is the last resort.
    internal static string[] CombatTracks(string? eraId, string? eraSpecific, string? basePool, ICollection<string>? warnings = null,
        IReadOnlyList<string>? eraOrder = null)
    {
        var specific = ParseList(eraSpecific, warnings, CombatCueForEra(eraId, eraOrder));
        if (specific.Length > 0) return specific;
        var baseTracks = ParseList(basePool, warnings, CampaignMusicCue.Combat);
        return baseTracks.Length > 0 ? baseTracks : new[] { DefaultFiles[CampaignMusicCue.Combat] };
    }

    // Accepts a comma/semicolon separated list of WAV files relative to the plugin
    // Audio folder. Entries keep their order, duplicates are removed and unsafe or
    // non-WAV entries are reported and dropped. An empty or fully invalid list
    // falls back to the built-in track.
    internal static string[] TracksFor(CampaignMusicCue cue, string? configured, ICollection<string>? warnings = null)
    {
        var list = ParseList(configured, warnings, cue);
        if (list.Length > 0) return list;
        if (!string.IsNullOrWhiteSpace(configured))
            warnings?.Add($"No usable music files for {cue}; using the built-in track '{DefaultFiles[cue]}'.");
        return new[] { DefaultFiles[cue] };
    }

    private static string[] ParseList(string? configured, ICollection<string>? warnings, CampaignMusicCue cue)
    {
        if (string.IsNullOrWhiteSpace(configured)) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var raw in configured.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            if (!IsSafeRelativeWav(name))
            {
                warnings?.Add($"Ignoring music entry '{name}' for {cue}: expected a relative .wav path inside the Audio folder.");
                continue;
            }
            if (!result.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))) result.Add(name);
        }
        return result.ToArray();
    }

    internal static bool IsSafeRelativeWav(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 260) return false;
        if (Path.IsPathRooted(name)) return false;
        if (!name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = name.Split('/', '\\');
        if (parts.Length == 0) return false;
        foreach (var part in parts)
        {
            if (part.Length == 0 || part == "." || part == "..") return false;
        }
        return true;
    }

    // Resolves a validated relative name under the plugin Audio folder and proves
    // the result cannot escape that folder.
    internal static string? ResolvePath(string audioRoot, string relativeName)
    {
        if (!IsSafeRelativeWav(relativeName)) return null;
        var root = Path.GetFullPath(audioRoot);
        var full = Path.GetFullPath(Path.Combine(root, relativeName));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}

// Deterministic rotation: the first pick per cue is derived from the session seed,
// later picks advance through the configured list so every track is used and an
// immediate repeat is avoided. Equal seeds and call sequences pick equal tracks.
internal sealed class CampaignMusicRotation
{
    private readonly Dictionary<CampaignMusicCue, int> cursor = new();
    private long serial;

    internal void Reset() { cursor.Clear(); serial = 0; }

    internal string? Next(CampaignMusicCue cue, IReadOnlyList<string> candidates, long seed)
    {
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) { cursor[cue] = 0; return candidates[0]; }
        serial++;
        if (!cursor.TryGetValue(cue, out var index))
        {
            unchecked
            {
                var mixed = (ulong)seed * 1469598103934665603UL
                    ^ (ulong)serial * 1099511628211UL
                    ^ (ulong)(int)cue * 0x9E3779B97F4A7C15UL;
                mixed ^= mixed >> 33;
                index = (int)(mixed % (ulong)candidates.Count);
            }
        }
        else index = (index + 1) % candidates.Count;
        cursor[cue] = index;
        return candidates[index];
    }
}
