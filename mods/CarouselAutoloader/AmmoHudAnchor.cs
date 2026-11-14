using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace SprocketCarouselAutoloader;

// 0.2.27: where the Expanded Ballistics AMMO panel is, read through BepInEx's public API only
// (the loaded plugin's ConfigFile; no reflection on Expanded Ballistics or the UI Kit). No kit types
// here. The values are read once when first asked and then at most once per second; nothing allocates.
internal static class AmmoHudAnchor
{
    // Expanded Ballistics 0.22.x: Plugin.cs [BepInPlugin("sprocket.expandedballistics", ...)],
    // AmmoLoadoutHud.Configure binds "Ammunition HUD" / Corner (string), Offset X and Offset Y (float).
    internal const string PluginGuid = "sprocket.expandedballistics", Section = "Ammunition HUD";

    private static ConfigEntry<string>? corner;
    private static ConfigEntry<float>? offsetX, offsetY;
    private static float nextRead;
    private static bool known;
    private static string? cornerText;
    private static float x, y;

    /// <summary>True when the AMMO panel's corner and offsets could be read. <paramref name="now"/> is unscaled time.</summary>
    internal static bool TryRead(float now, out string? cornerName, out float offsetXValue, out float offsetYValue)
    {
        if (now >= nextRead || nextRead - now > 5f) { nextRead = now + 1f; Refresh(); }
        cornerName = cornerText; offsetXValue = x; offsetYValue = y;
        return known;
    }

    private static void Refresh()
    {
        try
        {
            if (corner == null || offsetX == null || offsetY == null)
            {
                known = false;
                if (!IL2CPPChainloader.Instance.Plugins.TryGetValue(PluginGuid, out var info)) return;
                var config = (info.Instance as BasePlugin)?.Config;
                if (config == null) return;
                if (!config.TryGetEntry<string>(Section, "Corner", out var c)) return;
                if (!config.TryGetEntry<float>(Section, "Offset X", out var ox)) return;
                if (!config.TryGetEntry<float>(Section, "Offset Y", out var oy)) return;
                corner = c; offsetX = ox; offsetY = oy;
            }
            cornerText = corner.Value; x = offsetX.Value; y = offsetY.Value;
            known = true;
        }
        catch
        {
            known = false; corner = null; offsetX = null; offsetY = null;
        }
    }
}
