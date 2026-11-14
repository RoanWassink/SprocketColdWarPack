using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace SprocketThermalSight;

public sealed class ThermalCatalog
{
    public int Version { get; set; } = 2;
    public string DefaultProfileId { get; set; } = "thermalSightModel3";
    public List<ThermalProfile> Models { get; set; } = new();
    public ThermalProfile Resolve(string? selectedId, string? legacyId = null) =>
        Models.FirstOrDefault(p => p.ComponentId == (selectedId ?? legacyId ?? DefaultProfileId))
        ?? Models.First(p => p.ComponentId == DefaultProfileId);
    public void Validate()
    {
        if (Models == null) throw new InvalidDataException("models must be a non-null array.");
        if (Version is not (1 or 2) || Models.Count == 0) throw new InvalidDataException("Unsupported or empty thermal catalog.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var guids = new HashSet<Guid>();
        foreach (var model in Models)
        {
            if (model == null) throw new InvalidDataException("models must not contain a null model.");
            if (Version == 2 && model.BlackHot) throw new InvalidDataException("blackHot is removed in v2; use palette.");
            model.Validate();
            if (!ids.Add(model.ComponentId) || (!string.IsNullOrEmpty(model.PartGuid) && !guids.Add(Guid.Parse(model.PartGuid))))
                throw new InvalidDataException("Duplicate thermal part GUID/component ID.");
        }
        if (!ids.Contains(DefaultProfileId)) throw new InvalidDataException("defaultProfileId must refer to an existing profile componentId.");
    }
    public static ThermalCatalog Read(string path, Action<string>? log = null)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        RejectUnknown(doc.RootElement, typeof(ThermalCatalog));
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("models must be a non-null array.");
        int version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetInt32() : 1;
        foreach (var model in models.EnumerateArray())
        {
            if (model.ValueKind != JsonValueKind.Object) throw new InvalidDataException("models must not contain a null/non-object model.");
            RejectUnknown(model, typeof(ThermalProfile));
            if (version == 2)
            {
                if (!model.TryGetProperty("availableFrom", out _)) throw new InvalidDataException("v2 profile requires availableFrom (yyyy.MM.dd).");
                if (model.TryGetProperty("blackHot", out _)) throw new InvalidDataException("blackHot is removed in v2; use palette.");
                if (!model.TryGetProperty("sensorType", out _)) throw new InvalidDataException("v2 profile requires sensorType.");
                if (model.TryGetProperty("sensorType", out var sensor) && sensor.GetString() == "activeIR"
                    && (!model.TryGetProperty("beamRangeM", out _) || !model.TryGetProperty("beamAngleDeg", out _)))
                    throw new InvalidDataException("activeIR requires beamRangeM and beamAngleDeg.");
            }
        }
        var result = JsonSerializer.Deserialize<ThermalCatalog>(doc.RootElement.GetRawText(), JsonOptions)!;
        result.Version = version;
        if (result.Version == 1)
        {
            foreach (var model in result.Models)
            {
                if (model == null) throw new InvalidDataException("models must not contain a null model.");
                model.SensorType = "thermal"; model.AvailableFrom = "1977.01.01";
                if (model.BlackHot) model.Palette = "blackHot";
                model.BlackHot = false;
            }
            result.Version = 2;
            log?.Invoke("[Thermal] Migrated v1 catalog in memory: thermal, works from 1977.01.01; blackHot converted to palette.");
        }
        result.Validate();
        return result;
    }
    private static void RejectUnknown(JsonElement element, Type type)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Expected object for {type.Name}.");
        var names = type.GetProperties().Where(p => p.GetCustomAttributes(typeof(JsonIgnoreAttribute), false).Cast<JsonIgnoreAttribute>().All(a => a.Condition != JsonIgnoreCondition.Always))
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in element.EnumerateObject())
        {
            if (!seen.Add(p.Name)) throw new InvalidDataException($"Duplicate setting '{p.Name}' in {type.Name}.");
            if (!names.Contains(p.Name)) throw new InvalidDataException($"Unknown setting '{p.Name}' in {type.Name}.");
        }
    }
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
}

public sealed class ThermalProfile
{
    public string PartGuid { get; set; } = "";
    public string ComponentId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AvailableFrom { get; set; } = "1977.01.01";
    public string SensorType { get; set; } = "thermal";
    public float BeamRangeM { get; set; } = 800;
    public float BeamAngleDeg { get; set; } = 6;
    [JsonIgnore] public DateTime AvailableDate => DateTime.ParseExact(AvailableFrom, "yyyy.MM.dd", CultureInfo.InvariantCulture);
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 240;
    public float RefreshHz { get; set; } = 15;
    public float Contrast { get; set; } = 1.4f;
    public float Brightness { get; set; } = 0;
    public float Gamma { get; set; } = 1;
    public float Noise { get; set; } = .025f;
    public float Blur { get; set; } = .25f;
    public int GrayLevels { get; set; } = 64;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool BlackHot { get; set; }
    public string Palette { get; set; } = "whiteHot";
    public string[] CustomColors { get; set; } = new[] { "#000000", "#FFFFFF" };
    public bool SmoothPixels { get; set; }
    public bool FlipVertical { get; set; }
    public float BackgroundLevel { get; set; } = .3f;
    public float BackgroundDetail { get; set; } = .3f;
    public float BackgroundGamma { get; set; } = .6f;
    public float VehicleHeat { get; set; } = .8f;
    public float ExtraMassKg { get; set; } = 15;
    public float ExtraAssemblyCost { get; set; } = 300;

    public void Validate()
    {
        if (AvailableFrom == null || AvailableFrom.Length != 10 || !DateTime.TryParseExact(AvailableFrom, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidDataException("availableFrom requires a valid calendar date yyyy.MM.dd.");
        if (SensorType is not ("thermal" or "imageIntensifier" or "activeIR")) throw new InvalidDataException("sensorType must be thermal, imageIntensifier or activeIR.");
        if (SensorType == "activeIR") { Range(BeamRangeM, 100, 2000, nameof(BeamRangeM)); Range(BeamAngleDeg, 1, 30, nameof(BeamAngleDeg)); }
        if ((!string.IsNullOrWhiteSpace(PartGuid) && !Guid.TryParse(PartGuid, out _)) || string.IsNullOrWhiteSpace(ComponentId) || string.IsNullOrWhiteSpace(DisplayName))
            throw new InvalidDataException("Profile requires componentId and displayName; optional legacy partGuid must be a GUID.");
        if (Width < 80 || Width > 1024 || Height < 60 || Height > 768 || Width * Height > 524288)
            throw new InvalidDataException("Sensor resolution outside 80x60..1024x768; maximum 524288 pixels.");
        Range(RefreshHz, 1, 60, nameof(RefreshHz)); Range(Contrast, .1f, 5, nameof(Contrast));
        Range(Brightness, -1, 1, nameof(Brightness)); Range(Gamma, .2f, 4, nameof(Gamma));
        Range(Noise, 0, .5f, nameof(Noise)); Range(Blur, 0, 1, nameof(Blur));
        Range(BackgroundLevel, 0, 1, nameof(BackgroundLevel)); Range(BackgroundDetail, 0, 1, nameof(BackgroundDetail));
        Range(BackgroundGamma, .2f, 4, nameof(BackgroundGamma));
        Range(VehicleHeat, 0, 1, nameof(VehicleHeat)); Range(ExtraMassKg, 0, 500, nameof(ExtraMassKg));
        Range(ExtraAssemblyCost, 0, 100000, nameof(ExtraAssemblyCost));
        if (GrayLevels < 2 || GrayLevels > 256) throw new InvalidDataException("grayLevels must be 2..256.");
        if (Palette is not ("whiteHot" or "blackHot" or "greenHot" or "amberHot" or "ironbow" or "rainbow" or "custom"))
            throw new InvalidDataException("Unknown palette. Use whiteHot, blackHot, greenHot, amberHot, ironbow, rainbow or custom.");
        if (CustomColors == null || CustomColors.Length < 2 || CustomColors.Length > 16 || CustomColors.Any(c => c == null || !System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$")))
            throw new InvalidDataException("customColors requires 2..16 #RRGGBB colors from cold to hot.");
    }
    private static void Range(float x, float min, float max, string name)
    { if (!float.IsFinite(x) || x < min || x > max) throw new InvalidDataException($"Invalid {name}; expected {min}..{max}."); }
}

public static class SensorProcessor
{
    // Each input is RGBA32. Heat is a depth-tested binary vehicle mask, not a physical temperature map.
    public static byte[] Process(byte[] scene, byte[] mask, ThermalProfile p, uint seed)
        => new SensorWorkspace(p).Process(scene, mask, seed);
    internal static byte[] BuildPalette(ThermalProfile p)
    {
        string[] colors = p.Palette switch
        {
            "greenHot" => new[] { "#000000", "#00B840", "#DEFFE2" },
            "amberHot" => new[] { "#000000", "#BA6500", "#FFE3A0" },
            "ironbow" => new[] { "#000000", "#24054D", "#841B7B", "#D74637", "#FFAC25", "#FFFFD8" },
            "rainbow" => new[] { "#000020", "#2020A0", "#00A8DD", "#20D050", "#FFE020", "#F04020", "#FFFFFF" },
            "custom" => p.CustomColors,
            _ => new[] { "#000000", "#FFFFFF" }
        };
        var stops = colors.Select(c => new[] { Convert.ToInt32(c.Substring(1, 2), 16), Convert.ToInt32(c.Substring(3, 2), 16), Convert.ToInt32(c.Substring(5, 2), 16) }).ToArray();
        var lut = new byte[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            float pos = i / 255f * (stops.Length - 1);
            int low = Math.Min((int)pos, stops.Length - 2); float weight = pos - low;
            for (int channel = 0; channel < 3; channel++) lut[i * 3 + channel] = (byte)Math.Clamp((int)MathF.Round(stops[low][channel] + (stops[low + 1][channel] - stops[low][channel]) * weight), 0, 255);
        }
        return lut;
    }

}

public sealed class SensorWorkspace
{
    private readonly ThermalProfile p;
    private readonly float[] signal, bloom;
    private readonly byte[] output, palette;
    public SensorWorkspace(ThermalProfile profile)
    {
        p = profile; signal = new float[checked(p.Width * p.Height)]; bloom = new float[signal.Length];
        output = new byte[signal.Length * 4]; palette = SensorProcessor.BuildPalette(p);
    }
    public byte[] Process(byte[] scene, byte[] mask, uint seed, float[]? illumination = null, HeatField? heat = null)
    {
        int count = checked(p.Width * p.Height);
        if (scene.Length != count * 4 || mask.Length != count * 4) throw new ArgumentException("Sensor buffer size mismatch.");
        if (illumination != null && illumination.Length != count) throw new ArgumentException("IR illumination size mismatch.");
        if (heat != null && heat.Add.Length != count) throw new ArgumentException("Heat field size mismatch.");
        float gain = 1;
        if (p.SensorType == "imageIntensifier")
        {
            double mean = 0;
            for (int i = 0; i < count; i++) mean += Luma(scene, i * 4);
            gain = Math.Clamp(.45f / Math.Max((float)(mean / count), .0001f), .25f, 64);
        }
        for (int i = 0; i < count; i++)
        {
            int k = i * 4;
            float luma = Luma(scene, k);
            if (p.SensorType == "imageIntensifier")
            {
                signal[i] = Math.Clamp(luma * gain, 0, 2);
                bloom[i] = Math.Max(0, signal[i] - .75f);
            }
            else if (p.SensorType == "activeIR")
                signal[i] = luma * (.002f + 20 * (illumination?[i] ?? 0));
            else
            {
                float coverage = Math.Max(mask[k], Math.Max(mask[k + 1], mask[k + 2])) / 255f;
                float background = Math.Clamp(p.BackgroundLevel + MathF.Pow(luma, p.BackgroundGamma) * p.BackgroundDetail, 0, 1);
                // 0.4.0: with a heat field the hull sits just above its surroundings and hot spots add on top;
                // the mask coverage keeps every spot on vehicle pixels only.
                float vehicle = heat == null ? p.VehicleHeat : Math.Clamp(background + heat.HullContrast + heat.Add[i], 0, 1);
                signal[i] = background + (vehicle - background) * coverage;
            }
        }
        float noise = p.SensorType == "imageIntensifier" ? p.Noise * MathF.Sqrt(gain) : p.Noise;
        uint state = seed == 0 ? 1u : seed;
        for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++)
        {
            int i = y * p.Width + x;
            float sum = 0; int samples = 0;
            if (p.Blur > 0) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            { int xx = Math.Clamp(x + dx, 0, p.Width - 1), yy = Math.Clamp(y + dy, 0, p.Height - 1); sum += signal[yy * p.Width + xx]; samples++; }
            float v = p.Blur == 0 ? signal[i] : signal[i] + (sum / samples - signal[i]) * p.Blur;
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            if (p.SensorType == "imageIntensifier")
            {
                float glow = 0;
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                    glow += bloom[Math.Clamp(y + dy, 0, p.Height - 1) * p.Width + Math.Clamp(x + dx, 0, p.Width - 1)];
                v += glow * .08f;
            }
            v += ((state & 0xffff) / 65535f * 2 - 1) * noise;
            v = p.SensorType == "activeIR" ? Math.Clamp(v * p.Contrast + p.Brightness, 0, 1)
                : Math.Clamp((v - .5f) * p.Contrast + .5f + p.Brightness, 0, 1);
            v = MathF.Pow(v, 1 / p.Gamma);
            v = MathF.Round(v * (p.GrayLevels - 1)) / (p.GrayLevels - 1);
            if (p.BlackHot || p.Palette == "blackHot") v = 1 - v;
            int target = ((p.FlipVertical ? p.Height - 1 - y : y) * p.Width + x) * 4;
            byte b = (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
            output[target] = palette[b * 3]; output[target + 1] = palette[b * 3 + 1]; output[target + 2] = palette[b * 3 + 2]; output[target + 3] = 255;
        }
        return output;
    }
    private static float Luma(byte[] scene, int k) => (.2126f * scene[k] + .7152f * scene[k + 1] + .0722f * scene[k + 2]) / 255;
}
