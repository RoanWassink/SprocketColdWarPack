using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;

namespace SprocketFloppyParts;

internal static class TankPaintReader
{
    private const int MaxLoggedPlates = 256;
    private static readonly HashSet<long> loggedPlates = new();

    internal static (float R, float G, float B, float Saturation)? Read(PlateStructure? plate)
    {
        if (plate == null) return null;
        try
        {
            var mat = plate.exteriorMaterial?.Material ?? plate.defaultExteriorMaterial?.Material;
            var painter = VehicleResources.Global?.vehiclePainter;
            if (mat == null || painter == null)
            {
                LogOnce(plate, null);
                return null;
            }
            var cfg = painter.GetCurrentConfiguration(mat);
            var result = (cfg.tintR, cfg.tintG, cfg.tintB, cfg.saturation);
            LogOnce(plate, result);
            return result;
        }
        catch
        {
            return null;
        }
    }

    internal static void Clear() => loggedPlates.Clear();

    private static void LogOnce(PlateStructure plate, (float R, float G, float B, float Sat)? paint)
    {
        if (!Plugin.Detailed || loggedPlates.Count >= MaxLoggedPlates) return;
        long key = plate.Pointer.ToInt64();
        if (!loggedPlates.Add(key)) return;
        string info = paint.HasValue
            ? $"(R={paint.Value.R:F3}, G={paint.Value.G:F3}, B={paint.Value.B:F3}, Sat={paint.Value.Sat:F3})"
            : "null (fallback)";
        Plugin.ModLog.LogInfo($"[Paint] Plate #{plate.VUID.Value}: {info}");
    }
}
