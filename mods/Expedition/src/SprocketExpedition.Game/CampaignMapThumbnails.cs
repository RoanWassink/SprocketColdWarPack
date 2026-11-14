#if CAMPAIGN
using BepInEx;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SprocketExpedition.Game.UI;
using UnityEngine;

namespace SprocketExpedition.Game;

// Map pictures for the encounter cards. The game's own splash PNGs (1920x1080,
// 2-4 MB) are decoded once per map on the main thread, shrunk to 768 px wide
// and cached for the rest of the session, so four maps cost about 5 MB instead
// of 33 MB. The textures are HideAndDontSave: a scene change does not unload them.
// A map without a usable file gets null and the card shows a flat panel instead.
internal static class CampaignMapThumbnails
{
    private const int ThumbnailWidth = 768;

    private sealed class Entry
    {
        public Texture2D? Texture;
        public bool Failed;
        public int Loads;
    }

    private static readonly Dictionary<string, Entry> cache = new(StringComparer.Ordinal);

    internal static string SplashDirectory => Path.Combine(
        Paths.GameRootPath, "Sprocket_Data", "StreamingAssets", "Scenarios", "Splash");

    // Main thread only; call while building a page, never while drawing.
    internal static Texture2D? Get(string mapId)
    {
        if (string.IsNullOrEmpty(mapId)) return null;
        if (cache.TryGetValue(mapId, out var entry))
        {
            if (entry.Failed) return null;
            if (entry.Texture != null) return entry.Texture;
            // Destroyed from outside: reload, but never more than three times in a session.
            if (entry.Loads >= 3)
            {
                entry.Failed = true;
                Plugin.Logger.LogWarning($"Expedition map picture '{mapId}' keeps getting destroyed; using a flat panel.");
                return null;
            }
        }
        else cache[mapId] = entry = new Entry();

        entry.Loads++;
        try { entry.Texture = Load(mapId); }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"Expedition map picture '{mapId}' failed to load: {ex.Message}");
        }
        if (entry.Texture == null)
        {
            entry.Failed = true;
            Plugin.Logger.LogWarning($"Expedition map picture '{mapId}' unavailable; the card shows a flat panel.");
        }
        return entry.Texture;
    }

    private static Texture2D? Load(string mapId)
    {
        foreach (var file in MapThumbnailRules.Candidates(SplashDirectory, mapId))
        {
            if (!File.Exists(file)) continue;
            var full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                var bytes = File.ReadAllBytes(file);
                if (!ImageConversion.LoadImage(full, new Il2CppStructArray<byte>(bytes), false))
                {
                    UnityEngine.Object.Destroy(full);
                    continue;
                }
                var result = Shrink(full, mapId);
                if (!ReferenceEquals(result, full)) UnityEngine.Object.Destroy(full);
                result.hideFlags = HideFlags.HideAndDontSave;
                result.name = "Expedition map " + mapId;
                result.wrapMode = TextureWrapMode.Clamp;
                result.filterMode = FilterMode.Bilinear;
                return result;
            }
            catch
            {
                UnityEngine.Object.Destroy(full);
                throw;
            }
        }
        return null;
    }

    // GPU blit into a smaller texture. If anything in that path fails the full-size
    // texture is kept: a bigger picture is better than none.
    private static Texture2D Shrink(Texture2D full, string mapId)
    {
        if (full.width <= ThumbnailWidth || full.height <= 0) return full;
        var height = Math.Max(1, (int)Math.Round(full.height * (ThumbnailWidth / (double)full.width)));
        RenderTexture? target = null;
        var previous = RenderTexture.active;
        try
        {
            target = RenderTexture.GetTemporary(ThumbnailWidth, height, 0, RenderTextureFormat.ARGB32);
            full.filterMode = FilterMode.Bilinear;
            Graphics.Blit(full, target);
            RenderTexture.active = target;
            var small = new Texture2D(ThumbnailWidth, height, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, ThumbnailWidth, height), 0, 0);
            small.Apply(false, true);
            return small;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"Expedition map picture '{mapId}' could not be shrunk ({ex.Message}); keeping the full-size texture.");
            return full;
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) RenderTexture.ReleaseTemporary(target);
        }
    }
}
#endif
