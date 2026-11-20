using System.Numerics;

namespace SprocketCarouselAutoloader;

// AmmoRack.Build (0x1C970AC-0x1C97214), unlike the five-ushort utility, includes
// per-axis spacing and padding. Use float metres, like the native implementation.
public readonly record struct RackGrid(int Columns, int Layers, int Rows, double PitchX, double PitchY, double PitchZ)
{
    public int Capacity => Columns * Layers * Rows;
    public static RackGrid Of(double x, double y, double z, int diameterMm, int lengthMm,
        Vector3 spacing = default, Vector3 min = default, Vector3 max = default)
    {
        int Count(double extent, int mm, float gap, float a, float b)
        {
            float size = mm * .001f, usable = (float)extent - a - b;
            if (mm <= 0 || !float.IsFinite(usable + gap) || gap < 0 || a < 0 || b < 0 || usable < size) return 0;
            return Math.Max(0, (int)Math.Floor((usable + gap) / (size + gap)));
        }
        // Preserve exact integer-mm division for the historical zero-spacing geometry.
        int Axis(double extent, int mm, float gap, float a, float b) => gap == 0 && a == 0 && b == 0
            ? mm > 0 ? Math.Max(0, (int)Math.Round(extent * 1000) / mm) : 0 : Count(extent, mm, gap, a, b);
        return new(Axis(x, diameterMm, spacing.X, min.X, max.X), Axis(y, diameterMm, spacing.Y, min.Y, max.Y),
            Axis(z, lengthMm, spacing.Z, min.Z, max.Z), diameterMm / 1000d + spacing.X,
            diameterMm / 1000d + spacing.Y, lengthMm / 1000d + spacing.Z);
    }
}
