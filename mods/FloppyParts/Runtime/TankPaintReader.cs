using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;

namespace SprocketFloppyParts;

internal static class TankPaintReader
{
    private const int MaxLoggedPlates = 256;
    private const int MaxCachedPlates = 512;
    private static readonly HashSet<long> loggedPlates = new();
    private static readonly Dictionary<long, (float R, float G, float B, float Saturation)> cachedPaint = new();

    internal static bool HasPaintProperties(Material? mat) =>
        mat != null &&
        mat.HasProperty("_PaintTint") &&
        mat.HasProperty("_PaintRoughness") &&
        mat.HasProperty("_PaintMetallic") &&
        mat.HasProperty("_Saturation");

    internal static bool IsXRayMaterial(PlateStructure? plate)
    {
        try
        {
            var extMat = plate?.exteriorMaterial?.Material;
            if (extMat == null || HasPaintProperties(extMat))
                return false;

            string? matName = extMat.name;
            string? shaderName = extMat.shader?.name;
            return (matName != null && matName.IndexOf("XRay", StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (shaderName != null && shaderName.IndexOf("InternalComponent", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch
        {
            return false;
        }
    }

    internal static (float R, float G, float B, float Saturation)? Read(PlateStructure? plate)
    {
        if (plate == null) return null;
        try
        {
            long key = plate.Pointer.ToInt64();
            Material? mat = null;
            var extMat = plate.exteriorMaterial?.Material;
            if (HasPaintProperties(extMat))
            {
                mat = extMat;
            }
            else
            {
                var defMat = plate.defaultExteriorMaterial?.Material;
                if (HasPaintProperties(defMat))
                    mat = defMat;
            }

            var painter = VehicleResources.Global?.vehiclePainter;
            if (mat == null || painter == null)
            {
                if (cachedPaint.TryGetValue(key, out var cached))
                    return cached;
                LogOnce(plate, null);
                return null;
            }

            var cfg = painter.GetCurrentConfiguration(mat);
            var result = (cfg.tintR, cfg.tintG, cfg.tintB, cfg.saturation);
            if (cachedPaint.Count < MaxCachedPlates || cachedPaint.ContainsKey(key))
                cachedPaint[key] = result;
            LogOnce(plate, result);
            return result;
        }
        catch
        {
            return null;
        }
    }

    internal static void Clear()
    {
        loggedPlates.Clear();
        cachedPaint.Clear();
    }

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
