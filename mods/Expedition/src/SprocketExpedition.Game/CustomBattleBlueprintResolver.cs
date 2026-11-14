using System.Security.Cryptography;
using BepInEx;
using Sprocket.CustomBattles;
using Sprocket.Vehicles.Serialization;

namespace SprocketExpedition.Game;

/// <summary>
/// Converts the opaque locked blueprint bytes captured from the native editor into a
/// fresh native UnitDefinition for the Custom Battle host. The bytes are written to a
/// run-owned cache file only; no named player design is overwritten. Campaign build
/// only. A native card/unit failure is reported and never replaced by a fake unit.
/// </summary>
internal static class CustomBattleBlueprintResolver
{
    internal static UnitDefinition? CreateLockedUnit(byte[]? bytes, out string? error)
    {
        error = null;
        if (bytes is null || bytes.Length == 0)
        {
            error = "The locked blueprint is empty.";
            return null;
        }

        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var root = Path.Combine(Paths.CachePath, "SprocketExpedition", "locked-blueprints");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "locked-" + hash + ".blueprint");
            if (!File.Exists(path))
            {
                var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
            }

            var card = VehicleCard.LoadVehicleCard(path);
            if (card == null)
            {
                error = "The native vehicle card loader returned null for the locked blueprint.";
                return null;
            }

            var unit = UnitSelectionLoader.NewUnit(card);
            if (unit == null)
            {
                error = "The native UnitSelectionLoader returned null for the locked blueprint.";
                return null;
            }

            Plugin.Logger.LogInfo(
                $"Expedition campaign locked blueprint resolved to a native UnitDefinition; hash={hash}; cached={path}.");
            return unit;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
