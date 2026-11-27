using System.Globalization;

namespace SprocketThermalSight;

// 0.5.0: pure HUD helpers (no Unity types), shared by NightHud and the tests.
public enum HudStyle { None, ActiveIr, Intensifier, Gen1, Gen1Late, Gen2, Gen3, Gen3Uncooled, Gen3Hd, OesOp2s38 }

public enum RangeStatus { None, Ok, NoReturn, TooClose, Cancelled }

// The HUD is drawn on a 640x480 design frame (the approved mockups), fitted and centred on screen.
public readonly record struct HudLayout(float Scale, float OriginX, float OriginY)
{
    public const float DesignWidth = 640, DesignHeight = 480;
    public static HudLayout For(int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0) return new(0, 0, 0);
        float scale = Math.Min(screenHeight / DesignHeight, screenWidth / DesignWidth);
        return new(scale, (screenWidth - DesignWidth * scale) / 2f, (screenHeight - DesignHeight * scale) / 2f);
    }
    public float X(float designX) => OriginX + designX * Scale;
    public float Y(float designY) => OriginY + designY * Scale;
}

public static class HudMath
{
    // Compass bearing of a world direction, 0 = +Z, clockwise seen from above.
    public static float? Heading(float x, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z) || x * x + z * z < 1e-10f) return null;
        return Wrap360(MathF.Atan2(x, z) * 180f / MathF.PI);
    }
    public static float Wrap360(float degrees)
    {
        if (!float.IsFinite(degrees)) return 0;
        float d = degrees % 360f;
        if (d < 0) d += 360f;
        return d >= 360f ? 0 : d;
    }
    // Whole degrees 000..359; 359.6 rounds to 000, never to 360.
    public static int WholeDegrees(float degrees) => (int)MathF.Round(Wrap360(degrees), MidpointRounding.AwayFromZero) % 360;
    public static string FormatHeading(float? degrees) => degrees is { } d ? WholeDegrees(d).ToString("000", CultureInfo.InvariantCulture) : "---";
    // Turret bearing relative to the hull, clockwise 0..360.
    public static float? Relative(float? turretHeading, float? hullHeading)
        => turretHeading is { } t && hullHeading is { } h ? Wrap360(t - h) : null;

    // Bearing of f relative to the hull forward h, both projected on the hull plane (normal u); clockwise seen from above.
    public static float? RelativeBearing(float fx, float fy, float fz, float hx, float hy, float hz, float ux, float uy, float uz)
    {
        float ul = MathF.Sqrt(ux * ux + uy * uy + uz * uz);
        if (!float.IsFinite(ul) || ul < 1e-6f) return null;
        ux /= ul; uy /= ul; uz /= ul;
        float fd = fx * ux + fy * uy + fz * uz, hd = hx * ux + hy * uy + hz * uz;
        fx -= fd * ux; fy -= fd * uy; fz -= fd * uz; hx -= hd * ux; hy -= hd * uy; hz -= hd * uz;
        if (fx * fx + fy * fy + fz * fz < 1e-10f || hx * hx + hy * hy + hz * hz < 1e-10f) return null;
        // Unity is left-handed: cross(h, f) points along +up for a clockwise turn seen from above.
        float cx = hy * fz - hz * fy, cy = hz * fx - hx * fz, cz = hx * fy - hy * fx;
        float sin = cx * ux + cy * uy + cz * uz, cos = hx * fx + hy * fy + hz * fz;
        return Wrap360(MathF.Atan2(sin, cos) * 180f / MathF.PI);
    }
    // Gun elevation against the hull plane, degrees (+ up).
    public static float? Elevation(float fx, float fy, float fz, float ux, float uy, float uz)
    {
        float fl = MathF.Sqrt(fx * fx + fy * fy + fz * fz), ul = MathF.Sqrt(ux * ux + uy * uy + uz * uz);
        if (!float.IsFinite(fl) || !float.IsFinite(ul) || fl < 1e-6f || ul < 1e-6f) return null;
        return MathF.Asin(Math.Clamp((fx * ux + fy * uy + fz * uz) / (fl * ul), -1, 1)) * 180f / MathF.PI;
    }

    // "1 238", "850", "----" (no reading). Whole metres, a space as thousands separator.
    public static string FormatRange(double? metres)
    {
        if (metres is not { } m || !double.IsFinite(m) || m < 0) return "----";
        long whole = (long)Math.Round(m, MidpointRounding.AwayFromZero);
        if (whole > 99999) return "----";
        return whole >= 1000 ? $"{whole / 1000} {whole % 1000:000}" : whole.ToString(CultureInfo.InvariantCulture);
    }
    // Four digits as on early fire-control displays: "0850", "1240", "----".
    public static string FormatRange4(double? metres)
    {
        if (metres is not { } m || !double.IsFinite(m) || m < 0) return "----";
        long whole = (long)Math.Round(m, MidpointRounding.AwayFromZero);
        return whole > 9999 ? "----" : whole.ToString("0000", CultureInfo.InvariantCulture);
    }
    public static string FormatSigned(float? degrees) => degrees is { } d && float.IsFinite(d)
        ? (d >= 0 ? "+" : "-") + Math.Abs(d).ToString("0.0", CultureInfo.InvariantCulture) + "°" : "----";

    // Magnification against a reference vertical field of view (60 degrees = 1x, as in Gun Sights).
    public static float? Magnification(float fovDegrees, float referenceDegrees = 60f)
    {
        if (!float.IsFinite(fovDegrees) || fovDegrees <= 0 || fovDegrees >= 179) return null;
        float m = MathF.Tan(referenceDegrees * MathF.PI / 360f) / MathF.Tan(fovDegrees * MathF.PI / 360f);
        return float.IsFinite(m) && m > 0 ? m : null;
    }
    // Narrow field of view when the native zoom sits in the upper half of its range; unknown on a fixed optic.
    public static bool? Narrow(float applied, float minZoom, float maxZoom)
    {
        if (!float.IsFinite(applied) || !float.IsFinite(minZoom) || !float.IsFinite(maxZoom) || maxZoom - minZoom < 1e-4f) return null;
        return applied >= (minZoom + maxZoom) / 2f;
    }

    // Separate returns along the laser ray: hits closer than the gate belong to one object.
    public static int Echoes(IEnumerable<double> distances, double minimum, double maximum, double gate = 50)
    {
        var valid = distances.Where(d => double.IsFinite(d) && d >= minimum && d <= maximum).OrderBy(d => d).ToArray();
        if (valid.Length == 0) return 0;
        int echoes = 1;
        for (int i = 1; i < valid.Length; i++) if (valid[i] - valid[i - 1] > gate) echoes++;
        return echoes;
    }

    // Image-intensifier automatic gain (0.25..64) as a 0..1 bar, logarithmic.
    public static float GainFraction(float gain)
    {
        if (!float.IsFinite(gain) || gain <= .25f) return 0;
        return Math.Clamp(MathF.Log(gain / .25f) / MathF.Log(64f / .25f), 0, 1);
    }

    public static HudStyle StyleFor(string componentId, string sensorType, int width)
    {
        switch (componentId)
        {
            case "tiGen1": return HudStyle.Gen1;
            case "tiGen1Late": return HudStyle.Gen1Late;
            case "tiGen2": return HudStyle.Gen2;
            case "tiGen3": return HudStyle.Gen3;
            case "tiGen3Uncooled": return HudStyle.Gen3Uncooled;
            case "tiGen3HD": return HudStyle.Gen3Hd;
            case "ti2s38OesOp": return HudStyle.OesOp2s38;
        }
        return sensorType switch
        {
            "activeIR" => HudStyle.ActiveIr,
            "imageIntensifier" => HudStyle.Intensifier,
            "thermal" => width < 320 ? HudStyle.Gen1 : width < 640 ? HudStyle.Gen1Late : HudStyle.Gen2,
            _ => HudStyle.None,
        };
    }
    public static string Polarity(string palette) => palette == "blackHot" ? "BH" : "WH";
}

// Cooled detector start-up: a fixed delay after switching on, shown as mm:ss.
public sealed class CooldownTimer
{
    private float end;
    public void Start(float now, float seconds) => end = float.IsFinite(now) && float.IsFinite(seconds) && seconds > 0 ? now + seconds : 0;
    public void Reset() => end = 0;
    public float Remaining(float now) => end <= 0 || !float.IsFinite(now) ? 0 : Math.Max(0, end - now);
    public bool Active(float now) => Remaining(now) > 0;
    public static string Format(float seconds)
    {
        int total = !float.IsFinite(seconds) || seconds <= 0 ? 0 : (int)Math.Ceiling(seconds);
        return $"{total / 60:00}:{total % 60:00}";
    }
}

// 0.5.1: a cooled detector cools once per sight per session. The clock starts at the first switch-on and
// keeps running while the sight is switched off; once cooled, the sight is ready at every switch-on.
// A different vehicle or a new mission (Reset) starts the cooling again.
public sealed class CoolingLedger
{
    private sealed class Entry { internal float End; internal bool Done; internal string Name = ""; }
    private readonly Dictionary<long, Entry> sights = new();
    public long Vehicle { get; private set; }
    // True when the vehicle changed and every sight has to cool again.
    public bool SetVehicle(long vehicle)
    {
        if (vehicle == Vehicle) return false;
        Vehicle = vehicle; sights.Clear(); return true;
    }
    public void Reset() { Vehicle = 0; sights.Clear(); }
    public int Count => sights.Count;
    // Switch-on of a cooled sight. started is true only the first time for this sight (log "Cooling").
    public float SwitchOn(long sight, string name, float now, float seconds, out bool started)
    {
        started = false;
        if (!float.IsFinite(now)) return 0;
        if (!sights.TryGetValue(sight, out var entry))
        {
            if (!float.IsFinite(seconds) || seconds <= 0) return 0;
            sights[sight] = new Entry { End = now + seconds, Name = name };
            started = true;
            return seconds;
        }
        return Math.Max(0, entry.End - now);
    }
    public float Remaining(long sight, float now)
        => float.IsFinite(now) && sights.TryGetValue(sight, out var entry) ? Math.Max(0, entry.End - now) : 0;
    // Name of a sight whose cooling ran out since the last call (log "Cooled" once), switched on or not.
    public string? Finished(float now)
    {
        if (!float.IsFinite(now)) return null;
        foreach (var entry in sights.Values)
            if (!entry.Done && now >= entry.End) { entry.Done = true; return entry.Name; }
        return null;
    }
}
