using System.Globalization;
using System.Text;

namespace SprocketWheeledVehicles;

public enum AxleSteerMode
{
    None = 0,
    Front = 1,
    Rear = 2
}

public enum RearSteerHighSpeedMode
{
    Off = 0,
    SameDirection = 1
}

public sealed class AxleConfig
{
    public bool Driven { get; set; } = true;
    public AxleSteerMode SteerMode { get; set; } = AxleSteerMode.None;
    public float SteerScale { get; set; } = 1.0f;

    public AxleConfig Copy() => new()
    {
        Driven = Driven,
        SteerMode = SteerMode,
        SteerScale = SteerScale
    };

    public bool IsValid() => float.IsFinite(SteerScale) && SteerScale >= 0f && SteerScale <= 2f;
}

public sealed class WheeledSettings
{
    public const int CurrentFormatVersion = 1;
    public const float MinSteerAngleDeg = 10f;
    public const float MaxSteerAngleDegLimit = 50f;
    public const float MinGrip = 0.4f;
    public const float MaxGrip = 2.0f;
    public const float MinSuspension = 0.4f;
    public const float MaxSuspension = 2.5f;
    public const float MinDownTravel = 0.06f;
    public const float MaxDownTravel = 0.25f;
    public const float MinUpTravel = 0.08f;
    public const float MaxUpTravel = 0.30f;

    public bool Enabled { get; set; }
    public bool HideTrackBelt { get; set; } = true;
    public bool HideEndWheels { get; set; } = true;
    public bool AckermannEnabled { get; set; } = true;
    public float MaxSteerAngleDeg { get; set; } = 32f;
    public float TireGrip { get; set; } = 1.0f;
    public float SuspensionStiffness { get; set; } = 1.0f;
    public float SuspensionDamping { get; set; } = 1.0f;
    public float DownTravelMeters { get; set; } = 0.15f;
    public float UpTravelMeters { get; set; } = 0.20f;

    // Ready for Phase 2 per-axle & rear-wheel steering persistence without breaking Phase 1 blueprints.
    public bool RearSteerEnabled { get; set; }
    public float RearSteerMaxAngleDeg { get; set; } = 12f;
    public float RearSteerTransitionKmh { get; set; } = 30f;
    public RearSteerHighSpeedMode RearSteerHighSpeed { get; set; } = RearSteerHighSpeedMode.Off;
    public List<AxleConfig> Axles { get; set; } = new();

    public WheeledSettings Copy()
    {
        var copy = new WheeledSettings
        {
            Enabled = Enabled,
            HideTrackBelt = HideTrackBelt,
            HideEndWheels = HideEndWheels,
            AckermannEnabled = AckermannEnabled,
            MaxSteerAngleDeg = MaxSteerAngleDeg,
            TireGrip = TireGrip,
            SuspensionStiffness = SuspensionStiffness,
            SuspensionDamping = SuspensionDamping,
            DownTravelMeters = DownTravelMeters,
            UpTravelMeters = UpTravelMeters,
            RearSteerEnabled = RearSteerEnabled,
            RearSteerMaxAngleDeg = RearSteerMaxAngleDeg,
            RearSteerTransitionKmh = RearSteerTransitionKmh,
            RearSteerHighSpeed = RearSteerHighSpeed,
            Axles = new List<AxleConfig>(Axles.Count)
        };
        for (int i = 0; i < Axles.Count; i++)
            copy.Axles.Add(Axles[i].Copy());
        return copy;
    }

    public bool IsValid()
    {
        if (!float.IsFinite(MaxSteerAngleDeg) || MaxSteerAngleDeg < MinSteerAngleDeg || MaxSteerAngleDeg > MaxSteerAngleDegLimit) return false;
        if (!float.IsFinite(TireGrip) || TireGrip < MinGrip || TireGrip > MaxGrip) return false;
        if (!float.IsFinite(SuspensionStiffness) || SuspensionStiffness < MinSuspension || SuspensionStiffness > MaxSuspension) return false;
        if (!float.IsFinite(SuspensionDamping) || SuspensionDamping < MinSuspension || SuspensionDamping > MaxSuspension) return false;
        if (!float.IsFinite(DownTravelMeters) || DownTravelMeters < MinDownTravel || DownTravelMeters > MaxDownTravel) return false;
        if (!float.IsFinite(UpTravelMeters) || UpTravelMeters < MinUpTravel || UpTravelMeters > MaxUpTravel) return false;
        if (!float.IsFinite(RearSteerMaxAngleDeg) || RearSteerMaxAngleDeg < 0f || RearSteerMaxAngleDeg > 35f) return false;
        if (!float.IsFinite(RearSteerTransitionKmh) || RearSteerTransitionKmh < 5f || RearSteerTransitionKmh > 120f) return false;
        for (int i = 0; i < Axles.Count; i++)
        {
            if (Axles[i] == null || !Axles[i].IsValid()) return false;
        }
        return true;
    }

    public AxleConfig GetEffectiveAxleConfig(int axleIndex, int totalAxles)
    {
        if (axleIndex >= 0 && axleIndex < Axles.Count && Axles[axleIndex] != null)
            return Axles[axleIndex];

        // Sensible defaults when no explicit per-axle overrides are stored yet:
        // All axles driven; front axle steers (and second axle on 4+ axle vehicles in Phase 2,
        // or axle 0 on 2-3 axle vehicles; rear axle steers if RearSteerEnabled).
        var mode = AxleSteerMode.None;
        if (axleIndex == 0)
        {
            mode = AxleSteerMode.Front;
        }
        else if (totalAxles >= 4 && axleIndex == 1)
        {
            mode = AxleSteerMode.Front;
        }
        else if (RearSteerEnabled && totalAxles >= 2 && axleIndex == totalAxles - 1)
        {
            mode = AxleSteerMode.Rear;
        }

        return new AxleConfig
        {
            Driven = true,
            SteerMode = mode,
            SteerScale = 1.0f
        };
    }

    public void EnsureAxleCount(int totalAxles)
    {
        if (totalAxles <= 0) return;
        if (Axles.Count == totalAxles) return;

        var resized = new List<AxleConfig>(totalAxles);
        for (int i = 0; i < totalAxles; i++)
            resized.Add(GetEffectiveAxleConfig(i, totalAxles).Copy());
        Axles = resized;
    }

    public bool HasAnyRearSteer(int totalAxles)
    {
        if (RearSteerEnabled) return true;
        int n = Math.Max(totalAxles, Axles.Count);
        for (int i = 0; i < n; i++)
        {
            if (GetEffectiveAxleConfig(i, Math.Max(1, totalAxles)).SteerMode == AxleSteerMode.Rear)
                return true;
        }
        return false;
    }

    public string DescribeDriveLayout(int totalAxles)
    {
        if (totalAxles <= 0) return "0x0";
        int drivenAxles = 0;
        for (int i = 0; i < totalAxles; i++)
        {
            if (GetEffectiveAxleConfig(i, totalAxles).Driven)
                drivenAxles++;
        }
        return $"{totalAxles * 2}x{drivenAxles * 2}";
    }

    public void ApplyPreset(int totalAxles, bool[] driven, AxleSteerMode[] steer)
    {
        Axles.Clear();
        bool anyRear = false;
        for (int i = 0; i < totalAxles; i++)
        {
            var mode = i < steer.Length ? steer[i] : AxleSteerMode.None;
            if (mode == AxleSteerMode.Rear) anyRear = true;
            Axles.Add(new AxleConfig
            {
                Driven = i < driven.Length ? driven[i] : true,
                SteerMode = mode,
                SteerScale = 1.0f
            });
        }
        RearSteerEnabled = anyRear;
    }

    public string Serialize()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(160);
        sb.Append("v=").Append(CurrentFormatVersion);
        sb.Append(";en=").Append(Enabled ? 1 : 0);
        sb.Append(";hb=").Append(HideTrackBelt ? 1 : 0);
        sb.Append(";he=").Append(HideEndWheels ? 1 : 0);
        sb.Append(";ack=").Append(AckermannEnabled ? 1 : 0);
        sb.Append(";ang=").Append(MaxSteerAngleDeg.ToString("0.##", inv));
        sb.Append(";grip=").Append(TireGrip.ToString("0.###", inv));
        sb.Append(";stiff=").Append(SuspensionStiffness.ToString("0.###", inv));
        sb.Append(";damp=").Append(SuspensionDamping.ToString("0.###", inv));
        sb.Append(";dt=").Append(DownTravelMeters.ToString("0.###", inv));
        sb.Append(";ut=").Append(UpTravelMeters.ToString("0.###", inv));
        sb.Append(";rse=").Append(RearSteerEnabled ? 1 : 0);
        sb.Append(";rsang=").Append(RearSteerMaxAngleDeg.ToString("0.##", inv));
        sb.Append(";rskmh=").Append(RearSteerTransitionKmh.ToString("0.##", inv));
        sb.Append(";rshs=").Append((int)RearSteerHighSpeed);
        if (Axles.Count > 0)
        {
            sb.Append(";ax=");
            for (int i = 0; i < Axles.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var a = Axles[i];
                sb.Append(a.Driven ? '1' : '0')
                  .Append(':')
                  .Append((int)a.SteerMode)
                  .Append(':')
                  .Append(a.SteerScale.ToString("0.##", inv));
            }
        }
        return sb.ToString();
    }

    public static WheeledSettings Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FormatException("Empty wheeled vehicle settings.");

        var inv = CultureInfo.InvariantCulture;
        var s = new WheeledSettings();
        var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || eq >= part.Length - 1) continue;
            string key = part.Substring(0, eq).Trim();
            string val = part.Substring(eq + 1).Trim();
            switch (key)
            {
                case "en": s.Enabled = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "hb": s.HideTrackBelt = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "he": s.HideEndWheels = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "ack": s.AckermannEnabled = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "ang": s.MaxSteerAngleDeg = Math.Clamp(float.Parse(val, inv), MinSteerAngleDeg, MaxSteerAngleDegLimit); break;
                case "grip": s.TireGrip = Math.Clamp(float.Parse(val, inv), MinGrip, MaxGrip); break;
                case "stiff": s.SuspensionStiffness = Math.Clamp(float.Parse(val, inv), MinSuspension, MaxSuspension); break;
                case "damp": s.SuspensionDamping = Math.Clamp(float.Parse(val, inv), MinSuspension, MaxSuspension); break;
                case "dt": s.DownTravelMeters = Math.Clamp(float.Parse(val, inv), MinDownTravel, MaxDownTravel); break;
                case "ut": s.UpTravelMeters = Math.Clamp(float.Parse(val, inv), MinUpTravel, MaxUpTravel); break;
                case "rse": s.RearSteerEnabled = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "rsang": s.RearSteerMaxAngleDeg = Math.Clamp(float.Parse(val, inv), 0f, 35f); break;
                case "rskmh": s.RearSteerTransitionKmh = Math.Clamp(float.Parse(val, inv), 5f, 120f); break;
                case "rshs":
                    if (int.TryParse(val, NumberStyles.Integer, inv, out int hs) && (hs == 0 || hs == 1))
                        s.RearSteerHighSpeed = (RearSteerHighSpeedMode)hs;
                    break;
                case "ax":
                    s.Axles.Clear();
                    foreach (var tok in val.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var fields = tok.Split(':');
                        if (fields.Length < 2) continue;
                        var ax = new AxleConfig
                        {
                            Driven = fields[0] == "1",
                            SteerMode = int.TryParse(fields[1], NumberStyles.Integer, inv, out int sm) && sm >= 0 && sm <= 2
                                ? (AxleSteerMode)sm : AxleSteerMode.None,
                            SteerScale = fields.Length >= 3 && float.TryParse(fields[2], NumberStyles.Float, inv, out float sc)
                                ? Math.Clamp(sc, 0f, 2f) : 1.0f
                        };
                        s.Axles.Add(ax);
                    }
                    break;
            }
        }
        if (!s.IsValid())
            throw new FormatException("Parsed wheeled settings failed validation.");
        return s;
    }
}
