using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace SprocketThermalSight;

public sealed class ThermalCatalog
{
    public int Version { get; set; } = 3;
    public string DefaultProfileId { get; set; } = "tiGen1";
    public List<ThermalProfile> Models { get; set; } = new();
    // 0.5.4: the five legacy sight parts are no longer selectable; their part GUIDs and profile ids
    // resolve to a modern profile, so old blueprints and saves keep working.
    public List<ThermalAlias> Aliases { get; set; } = new();
    public ThermalProfile Resolve(string? selectedId, string? legacyId = null) =>
        Models.FirstOrDefault(p => p.ComponentId == (selectedId ?? legacyId ?? DefaultProfileId))
        ?? Models.First(p => p.ComponentId == DefaultProfileId);
    // Legacy part GUID or legacy profile id to the profile that replaces it; null when it is not an alias.
    public ThermalAlias? Alias(string? legacy) =>
        string.IsNullOrWhiteSpace(legacy) ? null
            : Aliases.FirstOrDefault(a => string.Equals(a.PartGuid, legacy, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.LegacyId, legacy, StringComparison.Ordinal));
    // A saved or part identity to the id of a profile that exists in this catalogue.
    public string? Map(string? id) => id == null ? null : Models.Any(p => p.ComponentId == id) ? id : Alias(id)?.ProfileId;
    // 0.5.4: the polarity a removed legacy part showed (the old black-hot Mk3), false for every other identity.
    public bool AliasBlackHot(string? id) => Alias(id)?.BlackHot ?? false;
    public void Validate()
    {
        if (Models == null) throw new InvalidDataException("models must be a non-null array.");
        if (Version is not (1 or 2 or 3) || Models.Count == 0) throw new InvalidDataException("Unsupported or empty thermal catalog.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var guids = new HashSet<Guid>();
        foreach (var model in Models)
        {
            if (model == null) throw new InvalidDataException("models must not contain a null model.");
            if (Version >= 2 && model.BlackHot) throw new InvalidDataException("blackHot is removed in v2; use palette.");
            model.Validate();
            if (!ids.Add(model.ComponentId) || (!string.IsNullOrEmpty(model.PartGuid) && !guids.Add(Guid.Parse(model.PartGuid))))
                throw new InvalidDataException("Duplicate thermal part GUID/component ID.");
        }
        if (!ids.Contains(DefaultProfileId)) throw new InvalidDataException("defaultProfileId must refer to an existing profile componentId.");
        if (Aliases == null) throw new InvalidDataException("aliases must be a non-null array.");
        var legacyIds = new HashSet<string>(StringComparer.Ordinal);
        var legacyGuids = new HashSet<Guid>();
        foreach (var alias in Aliases)
        {
            if (alias == null) throw new InvalidDataException("aliases must not contain a null alias.");
            alias.Validate();
            if (!ids.Contains(alias.ProfileId)) throw new InvalidDataException("alias profileId must refer to an existing profile componentId.");
            if (ids.Contains(alias.LegacyId)) throw new InvalidDataException("alias legacyId must not be a selectable profile.");
            if (!legacyIds.Add(alias.LegacyId) || !legacyGuids.Add(Guid.Parse(alias.PartGuid)))
                throw new InvalidDataException("Duplicate legacy alias id/GUID.");
            // A black-hot alias only makes sense on a profile that has the polarity switch.
            if (alias.BlackHot && !Models.First(m => m.ComponentId == alias.ProfileId).PolaritySwitch)
                throw new InvalidDataException("alias blackHot requires a thermal profile.");
        }
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
            if (version >= 2)
            {
                if (!model.TryGetProperty("availableFrom", out _)) throw new InvalidDataException("v2 profile requires availableFrom (yyyy.MM.dd).");
                if (model.TryGetProperty("blackHot", out _)) throw new InvalidDataException("blackHot is removed in v2; use palette.");
                if (!model.TryGetProperty("sensorType", out _)) throw new InvalidDataException("v2 profile requires sensorType.");
                if (model.TryGetProperty("sensorType", out var sensor) && sensor.GetString() == "activeIR"
                    && (!model.TryGetProperty("beamRangeM", out _) || !model.TryGetProperty("beamAngleDeg", out _)))
                    throw new InvalidDataException("activeIR requires beamRangeM and beamAngleDeg.");
            }
        }
        // 0.5.4: every image field is explicit; a v3 profile may not lean on a code default.
        if (version >= 3)
            foreach (var model in models.EnumerateArray())
            {
                foreach (var field in ThermalProfile.RequiredFields)
                    if (!model.TryGetProperty(field, out _)) throw new InvalidDataException($"v3 profile requires {field}.");
                if (model.TryGetProperty("sensorType", out var kind) && kind.GetString() == "activeIR")
                    foreach (var field in ThermalProfile.RequiredBeamFields)
                        if (!model.TryGetProperty(field, out _)) throw new InvalidDataException($"v3 activeIR profile requires {field}.");
            }
        if (doc.RootElement.TryGetProperty("aliases", out var aliases))
        {
            if (aliases.ValueKind != JsonValueKind.Array) throw new InvalidDataException("aliases must be an array.");
            foreach (var alias in aliases.EnumerateArray())
            {
                if (alias.ValueKind != JsonValueKind.Object) throw new InvalidDataException("aliases must not contain a null/non-object alias.");
                RejectUnknown(alias, typeof(ThermalAlias));
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

// 0.5.4: a removed legacy sight part (GUID) and its old profile id, both mapped to one modern profile.
public sealed class ThermalAlias
{
    public static readonly HashSet<string> LegacyPartGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "e1e0a2bf-5316-416a-b45e-b8e70b21a151",
        "40b644a7-90ed-47c7-b78f-e2f7310b80a2",
        "39614761-b743-408d-b112-9d4c24673c03",
        "37d49a2d-57ab-598a-8622-e909c48b2ad9",
        "ae0c73ac-6831-5c43-abf4-118b5352914d"
    };
    public string PartGuid { get; set; } = "";
    public string LegacyId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    // The old part showed black-hot (the Mk3 black-hot); the modern profile is white-hot by default.
    public bool BlackHot { get; set; }
    public void Validate()
    {
        if (!Guid.TryParse(PartGuid, out _) || !LegacyPartGuids.Contains(PartGuid))
            throw new InvalidDataException("alias partGuid must be a known legacy thermal part GUID.");
        if (string.IsNullOrWhiteSpace(LegacyId) || string.IsNullOrWhiteSpace(ProfileId))
            throw new InvalidDataException("alias requires legacyId and profileId.");
    }
}

public sealed class ThermalProfile
{
    // 0.5.4: the fields every v3 profile states explicitly (no implicit defaults).
    public static readonly string[] RequiredFields =
    {
        "componentId", "displayName", "sensorType", "availableFrom", "width", "height", "refreshHz", "noise", "blur",
        "sharpen", "contrast", "brightness", "gamma", "grayLevels", "smoothPixels", "backgroundLevel", "backgroundDetail",
        "backgroundGamma", "vehicleHeat", "palette", "cooled", "extraMassKg", "extraAssemblyCost"
    };
    public static readonly string[] RequiredBeamFields = { "beamRangeM", "beamAngleDeg", "exposureK" };

    public string PartGuid { get; set; } = "";
    public string ComponentId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AvailableFrom { get; set; } = "1977.01.01";
    public string SensorType { get; set; } = "thermal";
    public float BeamRangeM { get; set; } = 800;
    public float BeamAngleDeg { get; set; } = 6;
    // 0.5.4: soft saturation of the active-IR tube: signal = 1 - exp(-exposureK * luma * illumination).
    public float ExposureK { get; set; } = 3;
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
    // 0.5.0: cooled detectors show a grey, noisy start-up image for [Sensor] CooldownSeconds after switching on.
    public bool Cooled { get; set; }
    // 0.5.0: unsharp mask on the existing 3x3 neighbourhood (0 = off); no shader.
    public float Sharpen { get; set; }

    // 0.5.4: thermal profiles have a white-hot/black-hot switch; the palette is the default polarity.
    [JsonIgnore] public bool Thermal => SensorType == "thermal";
    [JsonIgnore] public bool PolaritySwitch => Thermal;
    [JsonIgnore] public bool BlackHotDefault => Palette == "blackHot";
    // The palette actually used for this frame: the switch only exists for thermal profiles.
    public string EffectivePalette(bool blackHot) =>
        PolaritySwitch ? (blackHot ? "blackHot" : (Palette == "blackHot" ? "whiteHot" : Palette)) : Palette;
    // True when the image is inverted: the switch controls polarity on thermal profiles.
    public bool Inverted(bool blackHot) => PolaritySwitch ? blackHot : Palette == "blackHot";

    public const int MaxWidth = 1280, MaxHeight = 1024, MaxPixels = 1280 * 1024;
    // Above this many pixels the per-row pass runs in parallel (row-seeded noise); smaller sensors keep the 0.4.x sequence.
    public const int ParallelPixels = 524288;
    public void Validate()
    {
        if (AvailableFrom == null || AvailableFrom.Length != 10 || !DateTime.TryParseExact(AvailableFrom, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidDataException("availableFrom requires a valid calendar date yyyy.MM.dd.");
        if (SensorType is not ("thermal" or "imageIntensifier" or "activeIR")) throw new InvalidDataException("sensorType must be thermal, imageIntensifier or activeIR.");
        if (SensorType == "activeIR") { Range(BeamRangeM, 100, 2000, nameof(BeamRangeM)); Range(BeamAngleDeg, 1, 30, nameof(BeamAngleDeg)); Range(ExposureK, .1f, 40, nameof(ExposureK)); }
        if ((!string.IsNullOrWhiteSpace(PartGuid) && !Guid.TryParse(PartGuid, out _)) || string.IsNullOrWhiteSpace(ComponentId) || string.IsNullOrWhiteSpace(DisplayName))
            throw new InvalidDataException("Profile requires componentId and displayName; optional legacy partGuid must be a GUID.");
        if (Width < 80 || Width > MaxWidth || Height < 60 || Height > MaxHeight || Width * Height > MaxPixels)
            throw new InvalidDataException("Sensor resolution outside 80x60..1280x1024; maximum 1310720 pixels.");
        Range(RefreshHz, 1, 60, nameof(RefreshHz)); Range(Contrast, .1f, 5, nameof(Contrast));
        Range(Brightness, -1, 1, nameof(Brightness)); Range(Gamma, .2f, 4, nameof(Gamma));
        Range(Noise, 0, .5f, nameof(Noise)); Range(Blur, 0, 1, nameof(Blur)); Range(Sharpen, 0, 1, nameof(Sharpen));
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

// 0.5.4: the white-hot/black-hot choice per sight, in memory for the session only. No save hook and no
// change to the save format; a sight that was never switched is white-hot (or the profile's own default).
public static class PolarityState
{
    private static readonly Dictionary<long, bool> blackHot = new();
    private static readonly HashSet<long> seen = new();
    public static int Count => blackHot.Count;
    public static bool Get(long sight) => blackHot.TryGetValue(sight, out bool value) && value;
    // Effective polarity for a sight: the session choice if switched, otherwise the profile/alias default.
    public static bool Effective(long sight, bool defaultBlackHot) => seen.Contains(sight) ? Get(sight) : defaultBlackHot;
    // True once this sight has been looked at or switched, so a legacy black-hot part only sets its polarity once.
    public static bool Seen(long sight) => seen.Contains(sight);
    public static void Set(long sight, bool value) { seen.Add(sight); if (value) blackHot[sight] = true; else blackHot.Remove(sight); }
    public static bool Toggle(long sight, bool defaultBlackHot) { bool next = !Effective(sight, defaultBlackHot); Set(sight, next); return next; }
    public static void Clear() { blackHot.Clear(); seen.Clear(); }
}

public static class SensorProcessor
{
    // Each input is RGBA32. Heat is a depth-tested binary vehicle mask, not a physical temperature map.
    public static byte[] Process(byte[] scene, byte[] mask, ThermalProfile p, uint seed)
        => new SensorWorkspace(p).Process(scene, mask, seed);
    internal static byte[] BuildPalette(ThermalProfile p, bool blackHot = false)
    {
        string[] colors = p.EffectivePalette(blackHot) switch
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
        BlackHot = profile.BlackHotDefault;
    }
    // Last image-intensifier automatic gain (1 for other sensors); read by the HUD gain bar.
    public float LastGain { get; private set; } = 1;
    // 0.5.4: white-hot/black-hot switch of the sight; only thermal profiles use it.
    public bool BlackHot { get; set; }
    public byte[] Process(byte[] scene, byte[] mask, uint seed, float[]? illumination = null, HeatField? heat = null, bool cooling = false)
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
            // 0.5.4: the soft saturation below compresses the top, so the gain aims a little higher to keep
            // the same 0.45 mean on the tube (1 - exp(-0.5) = 0.393 of the target).
            gain = Math.Clamp(.45f / Math.Max((float)(mean / count), .0001f) / .393f, .25f, 64);
            // The tube's own gain is what the HUD bar shows; the soft curve is applied on top of it.
            LastGain = gain;
        }
        if (cooling) return Cooling(seed);
        float noise = p.SensorType == "imageIntensifier" ? p.Noise * MathF.Sqrt(gain) : p.Noise;
        if (count > ThermalProfile.ParallelPixels) { Parallel(scene, mask, gain, noise, seed, illumination, heat); return output; }
        Signal(0, count, scene, mask, gain, illumination, heat);
        uint state = seed == 0 ? 1u : seed;
        for (int y = 0; y < p.Height; y++) Row(y, noise, ref state);
        return output;
    }
    // Separate method: the closures are only allocated for large (HD) sensors. Both passes run per row;
    // the second pass reads neighbouring signal rows, so it starts after the first has finished.
    private void Parallel(byte[] scene, byte[] mask, float gain, float noise, uint seed, float[]? illumination, HeatField? heat)
    {
        System.Threading.Tasks.Parallel.For(0, p.Height, y => Signal(y * p.Width, (y + 1) * p.Width, scene, mask, gain, illumination, heat));
        System.Threading.Tasks.Parallel.For(0, p.Height, y =>
        {
            uint row = (seed ^ (uint)(y + 1) * 2654435761u) | 1u;
            Row(y, noise, ref row);
        });
    }
    private void Signal(int from, int to, byte[] scene, byte[] mask, float gain, float[]? illumination, HeatField? heat)
    {
        for (int i = from; i < to; i++)
        {
            int k = i * 4;
            float luma = Luma(scene, k);
            if (p.SensorType == "imageIntensifier")
            {
                // 0.5.4: soft saturation instead of a hard clamp, bloom on bright sources only.
                // The gain already carries the 0.393 compensation of the curve.
                signal[i] = SoftSaturation(luma * gain * .393f);
                bloom[i] = Math.Max(0, signal[i] - .88f);
            }
            else if (p.SensorType == "activeIR")
            {
                // 0.5.4: the tube saturates softly; the linear gain of 20 turned every lit surface into a white blob.
                float lit = luma * (illumination?[i] ?? 0);
                signal[i] = lit > 0 ? SoftSaturation(p.ExposureK * lit) : 0;
            }
            else
            {
                float coverage = Math.Max(mask[k], Math.Max(mask[k + 1], mask[k + 2])) / 255f;
                float background = Math.Clamp(p.BackgroundLevel + MathF.Pow(luma, p.BackgroundGamma) * p.BackgroundDetail, 0, 1);
                // 0.4.0: with a heat field the hull sits just above its surroundings and hot spots add on top;
                // the mask coverage keeps every spot on vehicle pixels only.
                float vehicle = heat == null ? p.VehicleHeat : Math.Clamp(background + heat.HullContrast + heat.Add[i] + heat.HullBaseSignal, 0, 1);
                signal[i] = background + (vehicle - background) * coverage;
            }
        }
    }
    // 1 - exp(-x): linear near zero, asymptotic to 1, monotonic and smooth (no clipping edge).
    public static float SoftSaturation(float x) => !(x > 0) ? 0 : 1 - MathF.Exp(-x);
    private void Row(int y, float noise, ref uint state)
    {
        float filter = p.Blur - p.Sharpen;
        bool neighbourhood = p.Blur > 0 || p.Sharpen > 0;
        for (int x = 0; x < p.Width; x++)
        {
            int i = y * p.Width + x;
            float sum = 0; int samples = 0;
            if (neighbourhood) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            { int xx = Math.Clamp(x + dx, 0, p.Width - 1), yy = Math.Clamp(y + dy, 0, p.Height - 1); sum += signal[yy * p.Width + xx]; samples++; }
            float v = !neighbourhood ? signal[i] : signal[i] + (sum / samples - signal[i]) * filter;
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            float grain = (state & 0xffff) / 65535f * 2 - 1;
            if (p.SensorType == "imageIntensifier")
            {
                float glow = 0;
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                    glow += bloom[Math.Clamp(y + dy, 0, p.Height - 1) * p.Width + Math.Clamp(x + dx, 0, p.Width - 1)];
                v += glow * .08f;
                v += grain * noise;
            }
            else if (p.SensorType == "activeIR")
            {
                // 0.5.4: fine tube grain, a little stronger in the dark; no blocky pixels.
                v += grain * noise * (1 + 1.5f * (1 - Math.Clamp(v, 0, 1)));
            }
            else v += grain * noise;
            v = p.SensorType == "activeIR" ? Math.Clamp(v * p.Contrast + p.Brightness, 0, 1)
                : Math.Clamp((v - .5f) * p.Contrast + .5f + p.Brightness, 0, 1);
            v = MathF.Pow(v, 1 / p.Gamma);
            v = MathF.Round(v * (p.GrayLevels - 1)) / (p.GrayLevels - 1);
            if (p.BlackHot || p.Inverted(BlackHot)) v = 1 - v;
            int target = ((p.FlipVertical ? p.Height - 1 - y : y) * p.Width + x) * 4;
            byte b = (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
            output[target] = palette[b * 3]; output[target + 1] = palette[b * 3 + 1]; output[target + 2] = palette[b * 3 + 2]; output[target + 3] = 255;
        }
    }
    // Detector still cooling: neutral grey with strong noise, independent of scene and palette.
    private byte[] Cooling(uint seed)
    {
        uint state = seed == 0 ? 1u : seed;
        for (int i = 0; i < p.Width * p.Height; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            float v = Math.Clamp(.45f + ((state & 0xffff) / 65535f * 2 - 1) * .2f, 0, 1);
            byte b = (byte)MathF.Round(v * 255);
            int k = i * 4; output[k] = output[k + 1] = output[k + 2] = b; output[k + 3] = 255;
        }
        return output;
    }
    private static float Luma(byte[] scene, int k) => (.2126f * scene[k] + .7152f * scene[k + 1] + .0722f * scene[k + 2]) / 255;
}
