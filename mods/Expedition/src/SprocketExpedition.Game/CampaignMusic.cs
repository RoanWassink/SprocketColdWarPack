#if CAMPAIGN
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using Sprocket.AudioSystems;
using UnityEngine;
using UnityEngine.Bindings;
using UObject = UnityEngine.Object;

namespace SprocketExpedition.Game;

// Optional presentation service. All engine calls run from Update on the main
// thread; background work only reads/decodes our local WAV assets. Track selection
// is configured in the Expedition Music section and rotates per phase.
internal static class CampaignMusic
{
    private static ConfigEntry<bool> enabled = null!;
    private static ConfigEntry<float> volume = null!;
    private static ConfigEntry<bool> playlist = null!;
    private static readonly Dictionary<CampaignMusicCue, ConfigEntry<string>> trackEntries = new();
    private static readonly CampaignMusicState state = new();
    private static readonly CampaignMusicRotation rotation = new();
    private static readonly Dictionary<string, AudioClip> clips = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<(AudioSource Source, bool Muted)> nativeMusic = new();
    private static GameObject? holder;
    private static AudioSource? current, outgoing;
    private static CampaignMusicCue playing;
    private static string? playingFile, outgoingFile, pendingFile, combatEra;
    private static Task<(CampaignPcmWave? Wave, string? Error)>? decode;
    private static string[] loadOrder = Array.Empty<string>();
    private static CampaignMusicCue loadCue;
    private static int loadIndex;
    private static bool loading;
    private static long generation = -1;
    private static float blend = 1f, nextNativeCheck, nextResumeCheck;
    private static int resumeAttempts;
    private static float expectedTrackEnd;
    private static bool failed;
    private static CampaignMusicCue phase = CampaignMusicCue.Background;
    private static string audioRoot = string.Empty;

    internal static void Configure(ConfigFile config)
    {
        enabled = config.Bind("Expedition Music", "Enabled", true, "Play the Expedition soundtrack during the campaign.");
        volume = config.Bind("Expedition Music", "Volume", 0.35f,
            new ConfigDescription("Soundtrack gain, in addition to vanilla Master/Music volume. 0 mutes the soundtrack.",
                new AcceptableValueRange<float>(0f, 1f)));
        playlist = config.Bind("Expedition Music", "Playlist", true,
            "Advance to the next configured track when one finishes instead of repeating the selected track "
            + "for the whole phase (background, build, combat and boss cues).");
        audioRoot = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "Audio");
        Bind(config, CampaignMusicCue.Background, "Background", "Menu and loading");
        Bind(config, CampaignMusicCue.Build, "Build", "Editor and build mode");
        Bind(config, CampaignMusicCue.Combat, "Combat", "Normal combat");
        Bind(config, CampaignMusicCue.BossBattle, "Boss", "Boss combat");
        Bind(config, CampaignMusicCue.Victory, "Victory", "Victory");
        Bind(config, CampaignMusicCue.Defeat, "Defeat", "Defeat");
        BindCombat(config, CampaignMusicCue.CombatWWI, "CombatWWI", "WWI");
        BindCombat(config, CampaignMusicCue.CombatInterwar, "CombatInterwar", "Interwar");
        BindCombat(config, CampaignMusicCue.CombatEarlywar, "CombatEarlywar", "Earlywar");
        BindCombat(config, CampaignMusicCue.CombatMidwar, "CombatMidwar", "Midwar");
        BindCombat(config, CampaignMusicCue.CombatLatewar, "CombatLatewar", "Latewar");
        BindCombat(config, CampaignMusicCue.CombatColdwar, "CombatColdwar", "Coldwar");
    }

    private static void Bind(ConfigFile config, CampaignMusicCue cue, string key, string use)
    {
        trackEntries[cue] = config.Bind("Expedition Music", key, CampaignMusicOptions.DefaultFiles[cue],
            use + " music. One or more WAV files, comma-separated, relative to the plugin Audio folder "
            + "(for example Local\\my-track.wav). The next selected file rotates per encounter. "
            + "Empty uses the built-in track.");
    }

    private static void BindCombat(ConfigFile config, CampaignMusicCue cue, string key, string era)
    {
        trackEntries[cue] = config.Bind("Expedition Music", key, string.Empty,
            "Optional combat music for the " + era + " era. Empty uses the Combat list. "
            + "One or more WAV files, comma-separated, relative to the plugin Audio folder.");
    }

    internal static void BeginPhase(CampaignMusicCue requested)
    {
        phase = requested;
        state.BeginPhase(requested);
    }

    // Native era names in start-date order, for eras that have no cue of their own.
    private static IReadOnlyList<string>? NativeEraOrder()
    {
        try { return CampaignNativeCatalogue.EraNames; }
        catch { return null; }
    }

    internal static void BeginCombat(bool boss, string? eraId)
    {
        combatEra = eraId;
        BeginPhase(boss ? CampaignMusicCue.BossBattle : CampaignMusicOptions.CombatCueForEra(eraId, NativeEraOrder()));
    }
    internal static void Outcome(bool victory) => state.Outcome(victory);

    internal static void Tick()
    {
        try
        {
            var ownsSession = CampaignNativeFlow.OwnsSession;
            if (!ownsSession && !CampaignMenuButton.IsExpeditionMenuOpen)
            { if (generation != -1) Release(); return; }
            var wantedGeneration = ownsSession ? CampaignNativeFlow.SessionGeneration : -2;
            if (generation != wantedGeneration)
            {
                var requestedPhase = ownsSession ? phase : CampaignMusicCue.Background;
                Release();
                generation = wantedGeneration;
                rotation.Reset();
                phase = requestedPhase;
                state.BeginPhase(requestedPhase);
            }
            if (failed) return;
            if (!enabled.Value)
            {
                StopPlayback(); RestoreNativeMusic(); return;
            }
            if (state.Cue == playing && playing is CampaignMusicCue.Victory or CampaignMusicCue.Defeat
                && current != null && !current.isPlaying && blend >= 1f)
                state.Finished();

            var wanted = state.Cue;
            if (wanted != playing)
            {
                if (wanted == CampaignMusicCue.Silence) SwitchTo(null, wanted, null);
                else if (TryGetClip(wanted, out var clip, out var file) && clip != null) SwitchTo(clip, wanted, file);
            }
            // Keep native music suppressed after the defeat cue finishes too.
            // Restore only when Expedition ends or its music is disabled/fails.
            if (holder != null) SuppressNativeMusic();
            var mix = GlobalAudioSettings.FinalMix;
            var gain = Mathf.Clamp01(float.IsFinite(volume.Value) ? volume.Value : 0.35f)
                * Mathf.Clamp01(mix.MasterVolume) * Mathf.Clamp01(mix.MusicVolume);
            blend = Math.Min(1f, blend + Time.unscaledDeltaTime / 0.8f);
            if (current != null) current.volume = gain * blend;
            if (outgoing != null)
            {
                outgoing.volume = gain * (1f - blend);
                if (blend >= 1f)
                {
                    outgoing.Stop();
                    outgoing.clip = null;
                    outgoingFile = null;
                    PruneClips();
                }
            }
            // Unity virtualises low-priority sources when many native engine and gun
            // sources are active (large spawns). Looping music is priority 0, but keep
            // a bounded recovery path in case the engine still stopped the source.
            // In playlist mode a naturally finished track advances to the next item.
            if (CampaignMusicOptions.Loops(playing) && blend >= 1f && current != null)
            {
                if (current.clip == null)
                {
                    current.Stop();
                    playing = CampaignMusicCue.Silence;
                    Plugin.Logger.LogWarning($"Expedition music: the clip for {state.Cue} disappeared; reloading the cue.");
                }
                else if (!current.isPlaying)
                {
                    if (playlist.Value && Time.realtimeSinceStartup >= expectedTrackEnd - .1f)
                    {
                        current.Stop();
                        loading = loadCue == playing && loadIndex < loadOrder.Length;
                        playing = CampaignMusicCue.Silence;
                        Plugin.Logger.LogInfo($"Expedition music: track finished; continuing the {state.Cue} playlist.");
                    }
                    else if (Time.realtimeSinceStartup >= nextResumeCheck)
                    {
                        nextResumeCheck = Time.realtimeSinceStartup + 2f;
                        current.mute = false;
                        current.volume = gain;
                        current.Play();
                        resumeAttempts++;
                        Plugin.Logger.LogWarning($"Expedition music: looping cue {playing} stopped (attempt {resumeAttempts}); resumed playback.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failed = true;
            try { StopPlayback(); RestoreNativeMusic(); } catch { /* optional audio must not interrupt gameplay */ }
            Plugin.Logger.LogWarning("Expedition music disabled for this session: " + ex.Message);
        }
    }

    private static string[] BuildLoadOrder(CampaignMusicCue cue)
    {
        var warnings = new List<string>();
        var configured = trackEntries.TryGetValue(cue, out var entry) ? entry.Value : null;
        string[] candidates;
        if (CampaignMusicOptions.IsCombatCue(cue))
        {
            var baseConfigured = trackEntries.TryGetValue(CampaignMusicCue.Combat, out var baseEntry) ? baseEntry.Value : null;
            candidates = CampaignMusicOptions.CombatTracks(combatEra, configured, baseConfigured, warnings, NativeEraOrder());
        }
        else candidates = CampaignMusicOptions.TracksFor(cue, configured, warnings);
        foreach (var warning in warnings) Plugin.Logger.LogWarning("Expedition music config: " + warning);
        var first = rotation.Next(cue, candidates, CampaignNativeFlow.SessionGeneration);
        var order = new List<string>();
        if (first != null) order.Add(first);
        foreach (var name in candidates)
            if (!order.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))) order.Add(name);
        var fallback = CampaignMusicOptions.DefaultFiles[cue];
        if (!order.Exists(existing => string.Equals(existing, fallback, StringComparison.OrdinalIgnoreCase))) order.Add(fallback);
        return order.ToArray();
    }

    // Returns false while a background decode is still running or while moving to
    // the next configured candidate; returns true with the ready clip.
    private static bool TryGetClip(CampaignMusicCue cue, out AudioClip? clip, out string? file)
    {
        clip = null;
        file = null;
        if (decode != null)
        {
            if (!decode.IsCompleted) return false;
            var result = decode.GetAwaiter().GetResult();
            var loaded = pendingFile!;
            decode = null;
            pendingFile = null;
            if (result.Wave == null)
            {
                Plugin.Logger.LogWarning($"Expedition music: cannot read '{loaded}': {result.Error}");
                return false;
            }
            var uploaded = Upload(result.Wave, cue, loaded);
            clips[loaded] = uploaded;
            clip = uploaded;
            file = loaded;
            loading = false;
            return true;
        }
        if (!loading || loadCue != cue)
        {
            loadCue = cue;
            loadOrder = BuildLoadOrder(cue);
            loadIndex = 0;
            loading = true;
        }
        while (loadIndex < loadOrder.Length)
        {
            var name = loadOrder[loadIndex++];
            if (clips.TryGetValue(name, out var cached) && cached != null)
            {
                clip = cached;
                file = name;
                loading = false;
                return true;
            }
            var path = CampaignMusicOptions.ResolvePath(audioRoot, name);
            if (path == null || !File.Exists(path))
            {
                Plugin.Logger.LogWarning($"Expedition music: track '{name}' for {cue} is missing or outside the Audio folder.");
                continue;
            }
            pendingFile = name;
            var target = path;
            decode = Task.Run(() =>
            {
                try { using var stream = File.OpenRead(target); return (CampaignPcmWave.Read(stream), (string?)null); }
                catch (Exception ex) { return ((CampaignPcmWave?)null, ex.Message); }
            });
            return false;
        }
        loading = false;
        throw new InvalidDataException(
            $"No playable music file for {cue}; check the Expedition Music config and the Audio folder.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSpan { internal IntPtr Begin; internal int Length; }

    private static AudioClip Upload(CampaignPcmWave wave, CampaignMusicCue cue, string fileName)
    {
        if (Marshal.SizeOf<NativeSpan>() != Marshal.SizeOf<ManagedSpanWrapper>()
            || Marshal.OffsetOf<ManagedSpanWrapper>(nameof(ManagedSpanWrapper.begin)).ToInt32() != 0
            || Marshal.OffsetOf<ManagedSpanWrapper>(nameof(ManagedSpanWrapper.length)).ToInt32() != IntPtr.Size)
            throw new InvalidOperationException("Unsupported Unity audio span layout.");
        var clip = AudioClip.Construct_Internal();
        clip.hideFlags = HideFlags.HideAndDontSave;
        var title = ("Expedition " + cue).ToCharArray();
        var titlePin = GCHandle.Alloc(title, GCHandleType.Pinned);
        var samplePin = GCHandle.Alloc(wave.Samples, GCHandleType.Pinned);
        try
        {
            var nativeTitle = new NativeSpan { Begin = titlePin.AddrOfPinnedObject(), Length = title.Length };
            ref var label = ref Unsafe.As<NativeSpan, ManagedSpanWrapper>(ref nativeTitle);
            var frames = wave.Samples.Length / wave.Channels;
            AudioClip.CreateUserSound_Injected(clip.m_CachedPtr, ref label, frames, wave.Channels, wave.Frequency, false);
            var nativeSamples = new NativeSpan { Begin = samplePin.AddrOfPinnedObject(), Length = wave.Samples.Length };
            ref var data = ref Unsafe.As<NativeSpan, ManagedSpanWrapper>(ref nativeSamples);
            if (!AudioClip.SetData_Injected(clip.m_CachedPtr, ref data, 0)
                || clip.samples != frames || clip.channels != wave.Channels || clip.frequency != wave.Frequency)
                throw new InvalidOperationException("Native soundtrack upload failed.");
            Plugin.Logger.LogInfo($"Expedition music loaded {fileName}: {clip.length:N1} s, stereo={wave.Channels == 2}.");
            return clip;
        }
        catch { UObject.Destroy(clip); throw; }
        finally { samplePin.Free(); titlePin.Free(); }
    }

    private static void SwitchTo(AudioClip? clip, CampaignMusicCue cue, string? file)
    {
        if (holder == null)
        {
            holder = new GameObject("Expedition soundtrack");
            UObject.DontDestroyOnLoad(holder);
            current = holder.AddComponent<AudioSource>();
            outgoing = holder.AddComponent<AudioSource>();
            foreach (var source in new[] { current, outgoing })
            {
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
                source.priority = 0;
                source.bypassEffects = true;
                source.bypassListenerEffects = true;
                source.bypassReverbZones = true;
                source.volume = 0f;
            }
            Plugin.Logger.LogInfo("Expedition music sources: priority 0, listener pause ignored, effects/reverb bypassed.");
        }
        var previousFile = playingFile;
        (current, outgoing) = (outgoing, current);
        current!.Stop();
        current.clip = clip;
        current.loop = CampaignMusicOptions.Loops(cue) && !playlist.Value;
        current.volume = 0f;
        resumeAttempts = 0;
        nextResumeCheck = 0f;
        blend = 0f;
        playing = cue;
        playingFile = file;
        outgoingFile = previousFile;
        if (clip != null) current.Play();
        expectedTrackEnd = clip == null ? 0f : Time.realtimeSinceStartup + clip.length;
        Plugin.Logger.LogInfo("Expedition music cue: " + cue + (file == null ? string.Empty : " (" + file + ")"));
    }

    private static void PruneClips()
    {
        if (clips.Count <= 1) return;
        foreach (var key in clips.Keys.ToList())
        {
            if (playingFile != null && string.Equals(key, playingFile, StringComparison.OrdinalIgnoreCase)) continue;
            if (outgoingFile != null && string.Equals(key, outgoingFile, StringComparison.OrdinalIgnoreCase)) continue;
            var clip = clips[key];
            if (clip != null) UObject.Destroy(clip);
            clips.Remove(key);
        }
    }

    private static void SuppressNativeMusic()
    {
        if (Time.realtimeSinceStartup < nextNativeCheck) return;
        nextNativeCheck = Time.realtimeSinceStartup + 0.5f;
        nativeMusic.RemoveAll(x => x.Source == null);
        foreach (var player in UObject.FindObjectsOfType<PeriodicAudioPlayer>())
        {
            var source = player.source;
            if (player.AudioType != Sprocket.AudioSystems.AudioType.Music || source == null) continue;
            if (!nativeMusic.Any(x => x.Source.Pointer == source.Pointer)) nativeMusic.Add((source, source.mute));
            source.mute = true;
        }
    }

    private static void RestoreNativeMusic()
    {
        foreach (var saved in nativeMusic) if (saved.Source != null) saved.Source.mute = saved.Muted;
        nativeMusic.Clear();
        nextNativeCheck = 0f;
    }

    private static void StopPlayback()
    {
        if (current != null) { current.Stop(); current.clip = null; }
        if (outgoing != null) { outgoing.Stop(); outgoing.clip = null; }
        playing = CampaignMusicCue.Silence;
        playingFile = null;
        outgoingFile = null;
        decode = null;
        pendingFile = null;
        loading = false;
        loadOrder = Array.Empty<string>();
        loadIndex = 0;
        resumeAttempts = 0;
        nextResumeCheck = 0f;
        expectedTrackEnd = 0f;
        blend = 1f;
    }

    internal static void Release()
    {
        try
        {
            StopPlayback();
            RestoreNativeMusic();
            if (holder != null) UObject.Destroy(holder);
            foreach (var clip in clips.Values) if (clip != null) UObject.Destroy(clip);
        }
        catch (Exception ex) { Plugin.Logger.LogWarning("Expedition soundtrack cleanup: " + ex.Message); }
        finally
        {
            holder = null; current = null; outgoing = null;
            clips.Clear(); decode = null; pendingFile = null; generation = -1; failed = false;
            phase = CampaignMusicCue.Background; rotation.Reset(); loading = false; combatEra = null; resumeAttempts = 0; nextResumeCheck = 0f; expectedTrackEnd = 0f;
            state.Reset();
        }
    }
}
#endif
