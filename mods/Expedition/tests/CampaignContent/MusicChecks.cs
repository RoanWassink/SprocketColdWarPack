using System.Text;
using SprocketExpedition.Game;

internal static class MusicChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
        var music = new CampaignMusicState();
        music.BeginPhase(CampaignMusicCue.Background);
        Check(music.Cue == CampaignMusicCue.Background, "menu and loading start background music");
        music.BeginPhase(CampaignMusicCue.Build);
        Check(music.Cue == CampaignMusicCue.Build, "editor/build mode selects the build track");
        music.BeginCombat(false);
        Check(music.Cue == CampaignMusicCue.Combat, "normal combat selects the combat track");
        music.BeginPhase(CampaignMusicCue.Build);
        Check(music.Cue == CampaignMusicCue.Build, "returning to the editor restores the build track");
        music.BeginCombat(true);
        Check(music.Cue == CampaignMusicCue.BossBattle, "actual boss combat selects its separate soundtrack");
        music.Finished();
        Check(music.Cue == CampaignMusicCue.BossBattle, "boss music keeps its loop cue until a result or phase change");
        music.BeginCombat(false);
        Check(music.Cue == CampaignMusicCue.Combat, "a following normal encounter leaves boss music for combat music");
        music.Outcome(true); music.Outcome(false);
        Check(music.Cue == CampaignMusicCue.Victory, "duplicate result cannot replace accepted win cue");
        music.Finished(); music.Outcome(true);
        Check(music.Cue == CampaignMusicCue.Background, "victory ends into background without replay");
        music.BeginPhase(CampaignMusicCue.Build); music.Outcome(false); music.Finished(); music.Outcome(false);
        Check(music.Cue == CampaignMusicCue.Silence, "defeat plays once then remains silent");
        music.BeginPhase(CampaignMusicCue.Build); music.Outcome(false);
        Check(music.Cue == CampaignMusicCue.Defeat, "new encounter rearms outcome cue");
        music.BeginCombat(true); music.Outcome(false); music.Finished();
        Check(music.Cue == CampaignMusicCue.Silence, "boss defeat uses the existing one-shot defeat and silence path");
        music.BeginPhase(CampaignMusicCue.Build);
        Check(music.Cue == CampaignMusicCue.Build, "a new session can start directly in the build phase");
        music.Reset();
        Check(music.Cue == CampaignMusicCue.Silence, "session cleanup stops music");
        music.BeginCombat(true); music.Reset();
        Check(music.Cue == CampaignMusicCue.Silence, "leaving the session also clears the boss cue");

        byte[] Fixture()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(44);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(44100); writer.Write(176400);
            writer.Write((short)4); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(8);
            foreach (var sample in new short[] { short.MinValue, short.MaxValue, 0, 16384 }) writer.Write(sample);
            return stream.ToArray();
        }
        void Reject(byte[] bytes)
        {
            try { CampaignPcmWave.Read(new MemoryStream(bytes)); }
            catch (InvalidDataException) { return; }
            throw new Exception("invalid audio must be rejected before native upload");
        }
        var fixture = Fixture();
        var pcm = CampaignPcmWave.Read(new MemoryStream(fixture));
        Check(pcm.Channels == 2 && pcm.Frequency == 44100 && pcm.Samples.Length == 4
            && pcm.Samples[0] == -1f && pcm.Samples[1] > 0.99f && pcm.Samples[2] == 0f && pcm.Samples[3] == 0.5f,
            "PCM decoder preserves stereo and frame/sample scaling");
        Reject(fixture[..^1]);
        var bad = (byte[])fixture.Clone(); bad[20] = 3; Reject(bad);
        bad = (byte[])fixture.Clone(); bad[22] = 3; Reject(bad);
        bad = (byte[])fixture.Clone(); bad[32] = 1; Reject(bad);
        bad = (byte[])fixture.Clone(); bad[40] = 7; Reject(bad);

        foreach (var cue in new[] { CampaignMusicCue.Background, CampaignMusicCue.Build, CampaignMusicCue.Combat,
                     CampaignMusicCue.CombatWWI, CampaignMusicCue.CombatInterwar, CampaignMusicCue.CombatEarlywar,
                     CampaignMusicCue.CombatMidwar, CampaignMusicCue.CombatLatewar, CampaignMusicCue.CombatColdwar,
                     CampaignMusicCue.BossBattle, CampaignMusicCue.Victory, CampaignMusicCue.Defeat })
            Check(CampaignMusicOptions.DefaultFiles.ContainsKey(cue), "every playable cue has a built-in track");

        var warnings = new List<string>();
        var parsed = CampaignMusicOptions.TracksFor(CampaignMusicCue.Combat,
            " Local\\a.wav ; b.WAV,local\\a.wav,../evil.wav, C:\\x.wav, bad.mp3, ", warnings);
        Check(parsed.Length == 2 && parsed[0] == "Local\\a.wav" && parsed[1] == "b.WAV",
            "music config trims, de-duplicates and keeps the configured order");
        Check(warnings.Count == 3, "unsafe or non-WAV configured entries are reported and ignored");
        var invalid = CampaignMusicOptions.TracksFor(CampaignMusicCue.Combat, " ../x.wav ; ", warnings);
        Check(invalid.Length == 1 && invalid[0] == CampaignMusicOptions.DefaultFiles[CampaignMusicCue.Combat],
            "fully invalid config falls back to the built-in track");
        var empty = CampaignMusicOptions.TracksFor(CampaignMusicCue.Build, " ", warnings);
        Check(empty.Length == 1 && empty[0] == CampaignMusicOptions.DefaultFiles[CampaignMusicCue.Build],
            "empty config keeps the built-in build track");
        Check(CampaignMusicOptions.ResolvePath("C:\\Game\\Audio", "Local\\a.wav") != null,
            "a relative configured WAV resolves under the plugin Audio folder");
        Check(CampaignMusicOptions.ResolvePath("C:\\Game\\Audio", "../evil.wav") == null,
            "path traversal is rejected");
        Check(CampaignMusicOptions.ResolvePath("C:\\Game\\Audio", "C:\\evil.wav") == null,
            "absolute music paths are rejected");
        Check(CampaignMusicOptions.ResolvePath("C:\\Game\\Audio", "evil.mp3") == null,
            "non-WAV music entries are rejected");

        var tracks = new[] { "a.wav", "b.wav", "c.wav" };
        var first = new CampaignMusicRotation();
        var second = new CampaignMusicRotation();
        var picks = new List<string>();
        var picksAgain = new List<string>();
        for (var i = 0; i < 9; i++)
        {
            picks.Add(first.Next(CampaignMusicCue.Combat, tracks, 7)!);
            picksAgain.Add(second.Next(CampaignMusicCue.Combat, tracks, 7)!);
        }
        Check(picks.Count == picksAgain.Count && picks.TrueForAll(pick => picksAgain[picks.IndexOf(pick)] != null),
            "rotation returns a track for every call");
        Check(picks.SequenceEqual(picksAgain), "rotation is deterministic for equal seeds");
        Check(picks.Distinct(StringComparer.Ordinal).Count() == 3, "rotation uses every configured track");
        for (var i = 1; i < picks.Count; i++) Check(picks[i] != picks[i - 1], "rotation never repeats immediately");
        Check(picks[0] == picks[3] && picks[3] == picks[6], "rotation cycles through the configured list");
        var single = new CampaignMusicRotation();
        Check(single.Next(CampaignMusicCue.Build, new[] { "only.wav" }, 1) == "only.wav",
            "a single configured track stays selected");
        Check(single.Next(CampaignMusicCue.Build, Array.Empty<string>(), 1) == null,
            "an empty candidate list selects nothing");
        Check(CampaignMusicOptions.CombatCueForEra("Midwar") == CampaignMusicCue.CombatMidwar,
            "Midwar combat music uses its own era cue");
        Check(CampaignMusicOptions.CombatCueForEra("coldwar") == CampaignMusicCue.CombatColdwar,
            "era names are matched case-insensitively");
        Check(CampaignMusicOptions.CombatCueForEra(null) == CampaignMusicCue.Combat
            && CampaignMusicOptions.CombatCueForEra("unknown") == CampaignMusicCue.Combat,
            "missing or unknown eras use the shared combat cue");
        var order = new[] { "WWI", "Interwar", "Earlywar", "Midwar", "Latewar", "Coldwar", "Coldwar2", "Coldwar3" };
        Check(CampaignMusicOptions.CombatCueForEra("Coldwar2", order) == CampaignMusicCue.CombatColdwar
            && CampaignMusicOptions.CombatCueForEra("Coldwar3", order) == CampaignMusicCue.CombatColdwar,
            "a native era without its own cue uses the last known era cue before it");
        Check(CampaignMusicOptions.CombatCueForEra("Coldwar2") == CampaignMusicCue.CombatColdwar,
            "without an era order a trailing number is dropped");
        Check(CampaignMusicOptions.CombatCueForEra("Midwar", order) == CampaignMusicCue.CombatMidwar,
            "known eras keep their own cue when an order is given");
        Check(CampaignMusicOptions.CombatCueForEra("Prewar", new[] { "Prewar", "WWI" }) == CampaignMusicCue.Combat,
            "an unknown first era uses the shared combat cue");
        var eraWarnings = new List<string>();
        var inherited = CampaignMusicOptions.CombatTracks("Earlywar", "", "Base A.wav", eraWarnings);
        Check(inherited.Length == 1 && inherited[0] == "Base A.wav",
            "an empty era pool inherits the shared Combat list");
        var specific = CampaignMusicOptions.CombatTracks("Latewar", "Local\\late.wav", "Base A.wav", eraWarnings);
        Check(specific.Length == 1 && specific[0] == "Local\\late.wav",
            "an era-specific pool wins over the shared Combat list");
        var invalidEra = CampaignMusicOptions.CombatTracks("Coldwar", "../bad.wav", "Base A.wav", eraWarnings);
        Check(invalidEra.Length == 1 && invalidEra[0] == "Base A.wav",
            "an invalid era pool falls back to the shared Combat list");
        Check(CampaignMusicOptions.Loops(CampaignMusicCue.CombatMidwar) && CampaignMusicOptions.Loops(CampaignMusicCue.Build)
            && !CampaignMusicOptions.Loops(CampaignMusicCue.Victory) && !CampaignMusicOptions.Loops(CampaignMusicCue.Defeat),
            "build and every combat cue loop while outcome cues stay one-shot");
        Console.WriteLine("PASS per-era combat music mapping, Combat fallback and loop policy");
        Console.WriteLine("PASS music config parsing, path safety and deterministic rotation");
    }

    internal static void VerifyAssets(string directory)
    {
        foreach (var name in new[]{"Mechanical Tension.wav", "Minimal Focus Loop.wav", "Heavy Industry.wav", "Triumph Complete.wav", "Skill Issue.wav", "The Final Blow.wav"})
        {
            using var stream = File.OpenRead(Path.Combine(directory, name));
            var wave = CampaignPcmWave.Read(stream);
            if (wave.Frequency != 44100 || wave.Channels != 2) throw new Exception("Unexpected converted music format: " + name);
            Console.WriteLine($"PASS actual PCM asset: {name}; {wave.Samples.Length / (double)(wave.Frequency * wave.Channels):F2} s; stereo PCM16 / 44100 Hz");
        }
    }
}
