using System.Globalization;
using System.Text.Json;

namespace SprocketFloppyParts;

internal enum LookPreset
{
    Native = 0,
    Canvas = 1,
    CamoNet = 2,
    Rubber = 3,
    Wood = 4,
    Glass = 5,
    Custom = 6
}

internal enum ColorMode
{
    FollowTankPaint = 0,
    Preset = 1,
    CustomHex = 2
}

internal enum ColorPresetId
{
    Natural = 0,
    OliveDrab = 1,
    RussianGreen = 2,
    Khaki = 3,
    DesertSand = 4,
    WinterWhite = 5,
    FieldGrey = 6,
    Charcoal = 7,
    RubberBlack = 8,
    ClearGlass = 9,
    TintedGlass = 10
}

internal enum MotionProfile
{
    Static = 0,
    Cloth = 1,
    Rubber = 2,
    Hinged = 3
}

internal enum PinMode
{
    Auto = 0,
    TopEdge = 1,
    BottomEdge = 2,
    Border = 3,
    Corners = 4,
    TopAndBottom = 5,
    LeftAndRight = 6
}

internal readonly record struct MotionPresetDefaults(
    PinMode PinMode,
    float Stiffness,
    float Damping,
    float WindScale,
    float PinRadiusCm,
    float MaxSwingDeg);

internal sealed class PlateLookConfig
{
    internal const string DefaultHex = "#5A6345";
    internal const float DefaultStiffness = 0.50f;
    internal const float DefaultDamping = 0.35f;
    internal const float DefaultWindScale = 1.00f;
    internal const float DefaultPinRadiusCm = 4.50f;
    internal const float DefaultMaxSwingDeg = 35.0f;

    public int SchemaVersion { get; set; } = 1;
    public LookPreset Preset { get; set; } = LookPreset.Native;
    public ColorMode ColorMode { get; set; } = ColorMode.FollowTankPaint;
    public ColorPresetId ColorPreset { get; set; } = ColorPresetId.Natural;
    public string CustomHex { get; set; } = DefaultHex;
    public float Brightness { get; set; } = 1.0f;
    public float TileSizeM { get; set; } = 0.50f;
    public float UvRotationDeg { get; set; }
    public float Smoothness { get; set; } = 0.20f;
    public float Opacity { get; set; } = 1.0f;
    public string CustomFolder { get; set; } = "";

    public MotionProfile Motion { get; set; } = MotionProfile.Static;
    public PinMode PinMode { get; set; } = PinMode.Auto;
    public float Stiffness { get; set; } = DefaultStiffness;
    public float Damping { get; set; } = DefaultDamping;
    public float WindScale { get; set; } = DefaultWindScale;
    public float PinRadiusCm { get; set; } = DefaultPinRadiusCm;
    public float MaxSwingDeg { get; set; } = DefaultMaxSwingDeg;

    internal bool IsDefault =>
        Preset == LookPreset.Native &&
        Motion == MotionProfile.Static;

    internal static MotionPresetDefaults MotionDefaults(MotionProfile motion) => motion switch
    {
        MotionProfile.Cloth => new MotionPresetDefaults(
            PinMode.Auto, Stiffness: 0.30f, Damping: 0.35f, WindScale: 1.00f, DefaultPinRadiusCm, DefaultMaxSwingDeg),
        MotionProfile.Rubber => new MotionPresetDefaults(
            PinMode.Auto, Stiffness: 0.55f, Damping: 0.48f, WindScale: 0.25f, DefaultPinRadiusCm, DefaultMaxSwingDeg),
        MotionProfile.Hinged => new MotionPresetDefaults(
            PinMode.TopEdge, Stiffness: 0.15f, Damping: 0.35f, WindScale: 0.50f, DefaultPinRadiusCm, MaxSwingDeg: 35.0f),
        _ => new MotionPresetDefaults(
            PinMode.Auto, DefaultStiffness, DefaultDamping, DefaultWindScale, DefaultPinRadiusCm, DefaultMaxSwingDeg)
    };

    internal void ApplyMotionDefaults(MotionProfile motion)
    {
        var d = MotionDefaults(motion);
        Motion = motion;
        PinMode = d.PinMode;
        Stiffness = d.Stiffness;
        Damping = d.Damping;
        WindScale = d.WindScale;
        PinRadiusCm = d.PinRadiusCm;
        MaxSwingDeg = d.MaxSwingDeg;
    }

    internal static PlateLookConfig DefaultsFor(LookPreset preset) => preset switch
    {
        LookPreset.Canvas => new PlateLookConfig
        {
            Preset = LookPreset.Canvas,
            ColorMode = ColorMode.FollowTankPaint,
            ColorPreset = ColorPresetId.Natural,
            TileSizeM = 0.35f,
            Smoothness = 0.06f,
            Opacity = 1.0f
        },
        LookPreset.CamoNet => new PlateLookConfig
        {
            Preset = LookPreset.CamoNet,
            ColorMode = ColorMode.FollowTankPaint,
            ColorPreset = ColorPresetId.Natural,
            TileSizeM = 0.45f,
            Smoothness = 0.08f,
            Opacity = 0.45f
        },
        LookPreset.Rubber => new PlateLookConfig
        {
            Preset = LookPreset.Rubber,
            ColorMode = ColorMode.Preset,
            ColorPreset = ColorPresetId.RubberBlack,
            TileSizeM = 0.45f,
            Smoothness = 0.14f,
            Opacity = 1.0f
        },
        LookPreset.Wood => new PlateLookConfig
        {
            Preset = LookPreset.Wood,
            ColorMode = ColorMode.Preset,
            ColorPreset = ColorPresetId.Natural,
            TileSizeM = 0.60f,
            Smoothness = 0.22f,
            Opacity = 1.0f
        },
        LookPreset.Glass => new PlateLookConfig
        {
            Preset = LookPreset.Glass,
            ColorMode = ColorMode.Preset,
            ColorPreset = ColorPresetId.TintedGlass,
            TileSizeM = 1.00f,
            Smoothness = 0.96f,
            Opacity = 0.23f
        },
        LookPreset.Custom => new PlateLookConfig
        {
            Preset = LookPreset.Custom,
            ColorMode = ColorMode.Preset,
            ColorPreset = ColorPresetId.Natural,
            TileSizeM = 0.50f,
            Smoothness = 0.20f,
            Opacity = 1.0f
        },
        _ => new PlateLookConfig
        {
            Preset = LookPreset.Native,
            ColorMode = ColorMode.FollowTankPaint,
            ColorPreset = ColorPresetId.Natural,
            TileSizeM = 0.50f,
            Smoothness = 0.20f,
            Opacity = 1.0f
        }
    };

    internal PlateLookConfig Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Preset = Preset,
        ColorMode = ColorMode,
        ColorPreset = ColorPreset,
        CustomHex = CustomHex,
        Brightness = Brightness,
        TileSizeM = TileSizeM,
        UvRotationDeg = UvRotationDeg,
        Smoothness = Smoothness,
        Opacity = Opacity,
        CustomFolder = CustomFolder,
        Motion = Motion,
        PinMode = PinMode,
        Stiffness = Stiffness,
        Damping = Damping,
        WindScale = WindScale,
        PinRadiusCm = PinRadiusCm,
        MaxSwingDeg = MaxSwingDeg
    };

    internal PlateLookConfig Sanitize()
    {
        var preset = Enum.IsDefined(Preset) ? Preset : LookPreset.Native;
        var def = DefaultsFor(preset);
        Preset = preset;
        SchemaVersion = 2;
        ColorMode = Enum.IsDefined(ColorMode) ? ColorMode : def.ColorMode;
        ColorPreset = Enum.IsDefined(ColorPreset) ? ColorPreset : def.ColorPreset;
        CustomHex = ColorMath.TryParseHex(CustomHex, out var r, out var g, out var b)
            ? ColorMath.FormatHex(r, g, b)
            : DefaultHex;
        Brightness = float.IsFinite(Brightness) ? Math.Clamp(Brightness, 0.25f, 2.0f) : def.Brightness;
        TileSizeM = float.IsFinite(TileSizeM) ? Math.Clamp(TileSizeM, 0.05f, 5.0f) : def.TileSizeM;
        UvRotationDeg = NormalizeAngle(UvRotationDeg);
        Smoothness = float.IsFinite(Smoothness) ? Math.Clamp(Smoothness, 0.0f, 1.0f) : def.Smoothness;
        Opacity = float.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.05f, 1.0f) : def.Opacity;
        CustomFolder = SanitizeFolder(CustomFolder);

        Motion = Enum.IsDefined(Motion) ? Motion : MotionProfile.Static;
        var mDef = MotionDefaults(Motion);
        PinMode = Enum.IsDefined(PinMode) ? PinMode : mDef.PinMode;
        Stiffness = float.IsFinite(Stiffness) ? Math.Clamp(Stiffness, 0.05f, 1.0f) : mDef.Stiffness;
        Damping = float.IsFinite(Damping) ? Math.Clamp(Damping, 0.02f, 0.95f) : mDef.Damping;
        WindScale = float.IsFinite(WindScale) ? Math.Clamp(WindScale, 0.0f, 2.0f) : mDef.WindScale;
        PinRadiusCm = float.IsFinite(PinRadiusCm) ? Math.Clamp(PinRadiusCm, 1.0f, 15.0f) : mDef.PinRadiusCm;
        MaxSwingDeg = float.IsFinite(MaxSwingDeg) ? Math.Clamp(MaxSwingDeg, 5.0f, 75.0f) : mDef.MaxSwingDeg;
        return this;
    }

    internal static float NormalizeAngle(float deg)
    {
        if (!float.IsFinite(deg)) return 0f;
        float wrapped = deg % 360f;
        if (wrapped < 0f) wrapped += 360f;
        if (wrapped >= 360f) wrapped = 0f;
        return wrapped;
    }

    internal static string SanitizeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "";
        string trimmed = folder.Trim();
        if (trimmed == "." || trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains(':'))
            return "";
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in trimmed)
        {
            if (char.IsControl(c) || Array.IndexOf(invalid, c) >= 0)
                return "";
        }
        return trimmed;
    }

    internal string GeometrySignature() =>
        FormattableString.Invariant(
            $"{(int)Preset}|{TileSizeM:R}|{UvRotationDeg:R}|{(int)Motion}|{(int)PinMode}|{PinRadiusCm:R}");

    internal string MaterialSignature() =>
        FormattableString.Invariant(
            $"{(int)Preset}|{(int)ColorMode}|{(int)ColorPreset}|{CustomHex}|{Brightness:R}|{Smoothness:R}|{Opacity:R}|{CustomFolder}|{Stiffness:R}|{Damping:R}|{WindScale:R}|{MaxSwingDeg:R}");

    internal string Serialize()
    {
        var copy = Clone().Sanitize();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("SchemaVersion", copy.SchemaVersion);
            writer.WriteNumber("Preset", (int)copy.Preset);
            writer.WriteNumber("ColorMode", (int)copy.ColorMode);
            writer.WriteNumber("ColorPreset", (int)copy.ColorPreset);
            writer.WriteString("CustomHex", copy.CustomHex);
            writer.WriteNumber("Brightness", copy.Brightness);
            writer.WriteNumber("TileSizeM", copy.TileSizeM);
            writer.WriteNumber("UvRotationDeg", copy.UvRotationDeg);
            writer.WriteNumber("Smoothness", copy.Smoothness);
            writer.WriteNumber("Opacity", copy.Opacity);
            writer.WriteString("CustomFolder", copy.CustomFolder);

            if (copy.Motion != MotionProfile.Static)
            {
                writer.WriteNumber("Motion", (int)copy.Motion);
                writer.WriteNumber("PinMode", (int)copy.PinMode);
                writer.WriteNumber("Stiffness", copy.Stiffness);
                writer.WriteNumber("Damping", copy.Damping);
                writer.WriteNumber("WindScale", copy.WindScale);
                writer.WriteNumber("PinRadiusCm", copy.PinRadiusCm);
                writer.WriteNumber("MaxSwingDeg", copy.MaxSwingDeg);
            }

            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static bool TryDeserialize(string? json, out PlateLookConfig config)
    {
        config = DefaultsFor(LookPreset.Native);
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            var root = doc.RootElement;

            var preset = LookPreset.Native;
            if (TryGetPropertyIgnoreCase(root, "Preset", out var presetElem))
                preset = ParseEnum(presetElem, LookPreset.Native);

            config = DefaultsFor(preset);

            if (TryGetPropertyIgnoreCase(root, "ColorMode", out var modeElem))
                config.ColorMode = ParseEnum(modeElem, config.ColorMode);
            if (TryGetPropertyIgnoreCase(root, "ColorPreset", out var colorElem))
                config.ColorPreset = ParseEnum(colorElem, config.ColorPreset);
            if (TryGetPropertyIgnoreCase(root, "CustomHex", out var hexElem) && hexElem.ValueKind == JsonValueKind.String)
                config.CustomHex = hexElem.GetString() ?? DefaultHex;
            if (TryReadFiniteFloat(root, "Brightness", out var brightness))
                config.Brightness = brightness;
            if (TryReadFiniteFloat(root, "TileSizeM", out var tile))
                config.TileSizeM = tile;
            if (TryReadFiniteFloat(root, "UvRotationDeg", out var rot))
                config.UvRotationDeg = rot;
            if (TryReadFiniteFloat(root, "Smoothness", out var smooth))
                config.Smoothness = smooth;
            if (TryReadFiniteFloat(root, "Opacity", out var opacity))
                config.Opacity = opacity;
            if (TryGetPropertyIgnoreCase(root, "CustomFolder", out var folderElem) && folderElem.ValueKind == JsonValueKind.String)
                config.CustomFolder = folderElem.GetString() ?? "";

            MotionProfile motion = MotionProfile.Static;
            if (TryGetPropertyIgnoreCase(root, "Motion", out var motionElem))
                motion = ParseEnum(motionElem, MotionProfile.Static);
            else if (TryGetPropertyIgnoreCase(root, "MotionProfile", out var motionLegacyElem))
                motion = ParseEnum(motionLegacyElem, MotionProfile.Static);

            config.ApplyMotionDefaults(motion);

            if (TryGetPropertyIgnoreCase(root, "PinMode", out var pinElem))
                config.PinMode = ParseEnum(pinElem, config.PinMode);
            if (TryReadFiniteFloat(root, "Stiffness", out var stiffness))
                config.Stiffness = stiffness;
            if (TryReadFiniteFloat(root, "Damping", out var damping))
                config.Damping = damping;
            if (TryReadFiniteFloat(root, "WindScale", out var wind))
                config.WindScale = wind;
            if (TryReadFiniteFloat(root, "PinRadiusCm", out var pinRad))
                config.PinRadiusCm = pinRad;
            if (TryReadFiniteFloat(root, "MaxSwingDeg", out var maxSwing))
                config.MaxSwingDeg = maxSwing;

            config.Sanitize();
            return true;
        }
        catch (JsonException)
        {
            config = DefaultsFor(LookPreset.Native);
            return false;
        }
        catch (FormatException)
        {
            config = DefaultsFor(LookPreset.Native);
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value)) return true;
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool TryReadFiniteFloat(JsonElement obj, string name, out float value)
    {
        value = 0f;
        if (!TryGetPropertyIgnoreCase(obj, name, out var elem) || elem.ValueKind != JsonValueKind.Number)
            return false;
        if (!elem.TryGetSingle(out var f) || !float.IsFinite(f))
            return false;
        value = f;
        return true;
    }

    private static TEnum ParseEnum<TEnum>(JsonElement elem, TEnum fallback) where TEnum : struct, Enum
    {
        if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt32(out var num))
        {
            var candidate = (TEnum)(object)num;
            return Enum.IsDefined(candidate) ? candidate : fallback;
        }
        if (elem.ValueKind == JsonValueKind.String)
        {
            var s = elem.GetString();
            if (!string.IsNullOrWhiteSpace(s) && Enum.TryParse<TEnum>(s.Trim(), true, out var parsed) && Enum.IsDefined(parsed))
                return parsed;
        }
        return fallback;
    }
}
