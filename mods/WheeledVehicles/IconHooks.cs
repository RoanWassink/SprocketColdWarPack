using HarmonyLib;
using Sprocket.PartImporting;
using UnityEngine;

namespace SprocketWheeledVehicles;

[HarmonyPatch]
internal static class IconHooks
{
    private static Texture2D? _texture;
    private static Sprite? _icon;
    private static bool _attempted;

    [HarmonyPostfix, HarmonyPatch(typeof(PartDefinitionCardFactory), nameof(PartDefinitionCardFactory.CreateCard))]
    private static void CreateCardPostfix(PartDefinition __0, PartDisplayCard __result)
    {
        if (__0 == null || __result == null) return;
        if (!string.Equals(__0.guid, WheeledPart.Guid, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            if (!_attempted)
            {
                _attempted = true;
                string pluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".";
                string path = Path.Combine(pluginDir, "assets", "wheeled-suspension-icon.png");
                if (!File.Exists(path))
                {
                    Plugin.ModLog.LogWarning($"[Wheeled] Part icon not found at {path}; using stock fallback.");
                    return;
                }
                _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(_texture, File.ReadAllBytes(path), false))
                {
                    Plugin.ModLog.LogWarning($"[Wheeled] Failed to decode part icon PNG at {path}.");
                    return;
                }
                _icon = Sprite.Create(_texture, new Rect(0, 0, _texture.width, _texture.height), new Vector2(0.5f, 0.5f), 100f);
                _texture.hideFlags = HideFlags.HideAndDontSave;
                _icon.hideFlags = HideFlags.HideAndDontSave;
                Plugin.Diag(() => $"Loaded custom Wheeled Suspension icon ({_texture.width}x{_texture.height}).");
            }
            if (_icon != null)
                __result.Icon = _icon;
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("Part icon", ex);
        }
    }
}
