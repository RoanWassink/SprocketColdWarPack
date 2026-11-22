using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using Sprocket.ContinuousTracks;
using Sprocket.Vehicles.Tracks;
using UnityEngine;
using UnityEngine.Bindings;
using Object = UnityEngine.Object;

namespace SprocketWheeledVehicles;

[HarmonyPatch]
internal static class AudioHooks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSpan { internal IntPtr Begin; internal int Length; }

    private sealed class TireAudioPlayer
    {
        internal readonly AudioSource Source;
        internal float SmoothedVolume;
        internal float SmoothedPitch = 0.85f;
        internal TireAudioPlayer(AudioSource source) => Source = source;
    }

    private static AudioClip? _tireRollClip;
    private static bool _clipAttempted;
    private static bool _clipFailed;
    private static float _lastNativeVolumeMultiplier = 1f;
    private static readonly Dictionary<int, TireAudioPlayer> Players = new();

    internal static float LastNativeVolumeMultiplier => _lastNativeVolumeMultiplier;

    /// <summary>
    /// Mutes Sprocket's metallic track-clatter audio sources for wheeled vehicles while leaving
    /// all normal tracked vehicles untouched (and keeping engine/exhaust/gearbox sounds 100% intact).
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(TrackAudioController), nameof(TrackAudioController.UpdateAudio))]
    private static bool UpdateAudioPrefix(TrackAudioController __instance, float __0, float __1)
    {
        if (!Plugin.Enabled.Value || __instance == null) return true;
        try
        {
            if (float.IsFinite(__0) && __0 >= 0f)
                _lastNativeVolumeMultiplier = Math.Clamp(__0, 0f, 2f);

            var sources = __instance.sources;
            if (sources == null || sources.Length == 0) return true;

            int wheeledCount = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                var entry = sources[i];
                if (entry == null) continue;
                var track = entry.source?.TryCast<TrackBehaviour>();
                if (track == null || !RuntimeWheeled.IsBoundWheeledTrack(track.Pointer)) continue;

                wheeledCount++;
                MuteBlender(entry.audio);
            }

            // If all sources on this controller belong to a wheeled vehicle, skip native track clatter completely.
            return wheeledCount < sources.Length;
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("TrackAudioController mute", ex);
            return true;
        }
    }

    private static void MuteBlender(MultiClipAudioSourceBlender? blender)
    {
        if (blender == null) return;
        try
        {
            MuteChannel(blender.aChannel);
            MuteChannel(blender.bChannel);
            blender.playing = false;
        }
        catch { }
    }

    private static void MuteChannel(AudioSource? src)
    {
        if (src == null) return;
        if (src.isPlaying) src.Stop();
        src.volume = 0f;
    }

    internal static void UpdateTireAudio(
        int bodyId,
        Rigidbody body,
        float speedMps,
        float groundedFraction,
        float lateralSlipIntensity,
        float dt)
    {
        if (!Plugin.EnableTireAudio.Value || Plugin.TireAudioVolume.Value <= 0.001f || _clipFailed || body == null)
        {
            StopAndRemove(bodyId);
            return;
        }

        try
        {
            var clip = EnsureTireRollClip();
            if (clip == null) return;

            if (!Players.TryGetValue(bodyId, out var player) || player.Source == null)
            {
                var src = body.gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.clip = clip;
                src.loop = true;
                src.spatialBlend = 0.65f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 10f;
                src.maxDistance = 75f;
                src.dopplerLevel = 0.1f;
                src.volume = 0f;
                src.pitch = 0.78f;
                player = new TireAudioPlayer(src);
                Players[bodyId] = player;
            }

            var audio = player.Source;
            if (!audio.isActiveAndEnabled) return;

            // Silence cleanly when paused or stopped/airborne.
            bool paused = Time.timeScale <= 0.001f;
            float speedKmh = MathF.Abs(speedMps) * 3.6f;
            float speedFactor = Math.Clamp((speedKmh - 2.0f) / 55f, 0f, 1f);
            float slipBoost = Math.Clamp(lateralSlipIntensity, 0f, 1f) * 0.15f;
            float masterVol = Math.Clamp(Plugin.TireAudioVolume.Value, 0f, 1.5f) * _lastNativeVolumeMultiplier;

            float targetVol = (paused || groundedFraction <= 0.01f || speedKmh < 1.5f)
                ? 0f
                : Math.Clamp((speedFactor * 0.75f + slipBoost) * groundedFraction * masterVol, 0f, 0.85f);
            float targetPitch = Math.Clamp(0.68f + speedFactor * 0.58f, 0.65f, 1.35f);

            float blend = Math.Clamp(dt * 8f, 0.05f, 1f);
            player.SmoothedVolume += (targetVol - player.SmoothedVolume) * blend;
            player.SmoothedPitch += (targetPitch - player.SmoothedPitch) * blend;

            if (player.SmoothedVolume > 0.004f && !paused)
            {
                audio.volume = player.SmoothedVolume;
                audio.pitch = player.SmoothedPitch;
                if (!audio.isPlaying) audio.Play();
            }
            else if (audio.isPlaying)
            {
                audio.volume = 0f;
                audio.Stop();
            }
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("Tire roll audio", ex);
            StopAndRemove(bodyId);
        }
    }

    internal static void StopAndRemove(int bodyId)
    {
        if (!Players.Remove(bodyId, out var player)) return;
        try
        {
            if (player.Source != null)
            {
                player.Source.Stop();
                Object.Destroy(player.Source);
            }
        }
        catch { }
    }

    internal static void ClearAll()
    {
        foreach (var player in Players.Values)
        {
            try
            {
                if (player.Source != null)
                {
                    player.Source.Stop();
                    Object.Destroy(player.Source);
                }
            }
            catch { }
        }
        Players.Clear();
    }

    private static AudioClip? EnsureTireRollClip()
    {
        if (_tireRollClip != null) return _tireRollClip;
        if (_clipFailed) return null;
        if (_clipAttempted && _tireRollClip == null)
        {
            // Unity cleaned up clip across scene reload -> allow recreation.
            _clipAttempted = false;
        }

        _clipAttempted = true;
        try
        {
            if (Marshal.SizeOf<NativeSpan>() != Marshal.SizeOf<ManagedSpanWrapper>() ||
                Marshal.OffsetOf<ManagedSpanWrapper>(nameof(ManagedSpanWrapper.begin)).ToInt32() != 0 ||
                Marshal.OffsetOf<ManagedSpanWrapper>(nameof(ManagedSpanWrapper.length)).ToInt32() != IntPtr.Size)
            {
                _clipFailed = true;
                Plugin.ModLog.LogWarning("[Wheeled] Tire audio skipped: ManagedSpanWrapper layout differs from expected Unity 6000.3 layout.");
                return null;
            }

            float[] samples = TireAudioSynthesizer.GenerateTireRollLoop();
            var clip = AudioClip.Construct_Internal();
            clip.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;

            var title = "WheeledTireRoll".ToCharArray();
            var titlePin = GCHandle.Alloc(title, GCHandleType.Pinned);
            var samplePin = GCHandle.Alloc(samples, GCHandleType.Pinned);
            try
            {
                var nativeLabel = new NativeSpan { Begin = titlePin.AddrOfPinnedObject(), Length = title.Length };
                ref var label = ref Unsafe.As<NativeSpan, ManagedSpanWrapper>(ref nativeLabel);
                AudioClip.CreateUserSound_Injected(clip.m_CachedPtr, ref label, samples.Length, 1, TireAudioSynthesizer.SampleRate, false);

                var nativeData = new NativeSpan { Begin = samplePin.AddrOfPinnedObject(), Length = samples.Length };
                ref var data = ref Unsafe.As<NativeSpan, ManagedSpanWrapper>(ref nativeData);
                if (!AudioClip.SetData_Injected(clip.m_CachedPtr, ref data, 0) ||
                    clip.samples != samples.Length || clip.channels != 1)
                {
                    throw new InvalidOperationException("AudioClip.SetData_Injected verification failed.");
                }

                _tireRollClip = clip;
                Plugin.Diag(() => $"Procedural tyre roll audio ready ({clip.samples} samples, {clip.frequency} Hz, {clip.length:0.00} s).");
                return _tireRollClip;
            }
            catch
            {
                Object.Destroy(clip);
                throw;
            }
            finally
            {
                samplePin.Free();
                titlePin.Free();
            }
        }
        catch (Exception ex)
        {
            _clipFailed = true;
            Plugin.ModLog.LogWarning($"[Wheeled] Tire audio disabled (track rattle still muted): {ex.Message}");
            return null;
        }
    }
}
