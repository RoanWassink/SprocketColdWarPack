namespace SprocketThermalSight;

// Pure heat model (0.4.0): no Unity or native types, so every rule is unit-tested.
// Temperatures are rises above ambient in kelvin; the sensor maps them to signal units.

public sealed class HeatSettings
{
    public bool Enabled { get; set; } = true;
    public float AmbientC { get; set; } = 15;
    // A rise of FullScaleKelvin adds 1.0 signal on top of the hull level (saturates to hot).
    public float FullScaleKelvin { get; set; } = 60;
    public float HullContrast { get; set; } = .1f;
    public float EngineScale { get; set; } = 1;
    public float ExhaustScale { get; set; } = 1;
    public float BarrelScale { get; set; } = 1;
    public float WreckScale { get; set; } = 1;
    public float EngineIdleRiseK { get; set; } = 30;
    public float EngineFullRiseK { get; set; } = 65;
    public float EngineWarmTauS { get; set; } = 180;
    public float EngineCoolTauS { get; set; } = 900;
    public float ExhaustIdleRiseK { get; set; } = 120;
    public float ExhaustFullRiseK { get; set; } = 280;
    public float ExhaustWarmTauS { get; set; } = 30;
    public float ExhaustCoolTauS { get; set; } = 240;
    public float BarrelShotRiseK { get; set; } = 6;
    public float BarrelMaxRiseK { get; set; } = 120;
    public float BarrelCoolTauS { get; set; } = 90;
    public float WreckStartRiseK { get; set; } = 250;
    public float WreckFireRiseK { get; set; } = 450;
    public float WreckFireWarmTauS { get; set; } = 20;
    public float WreckCoolSeconds { get; set; } = 600;
    public float WreckResidualK { get; set; } = 4;
    public float EngineSpotRadiusM { get; set; } = .9f;
    public float ExhaustSpotRadiusM { get; set; } = .35f;
    public float BarrelSpotCalibers { get; set; } = 2.5f;
    public float PollHz { get; set; } = 5;
    public float MaxRangeM { get; set; } = 4000;
    public bool InvertSpotY { get; set; }

    public void Validate()
    {
        R(AmbientC, -60, 60, nameof(AmbientC)); R(FullScaleKelvin, 1, 2000, nameof(FullScaleKelvin)); R(HullContrast, 0, 1, nameof(HullContrast));
        foreach (var (v, n) in new[] { (EngineScale, nameof(EngineScale)), (ExhaustScale, nameof(ExhaustScale)), (BarrelScale, nameof(BarrelScale)), (WreckScale, nameof(WreckScale)) }) R(v, 0, 10, n);
        foreach (var (v, n) in new[] { (EngineIdleRiseK, nameof(EngineIdleRiseK)), (EngineFullRiseK, nameof(EngineFullRiseK)), (ExhaustIdleRiseK, nameof(ExhaustIdleRiseK)),
            (ExhaustFullRiseK, nameof(ExhaustFullRiseK)), (BarrelShotRiseK, nameof(BarrelShotRiseK)), (BarrelMaxRiseK, nameof(BarrelMaxRiseK)),
            (WreckStartRiseK, nameof(WreckStartRiseK)), (WreckFireRiseK, nameof(WreckFireRiseK)) }) R(v, 0, 2000, n);
        foreach (var (v, n) in new[] { (EngineWarmTauS, nameof(EngineWarmTauS)), (EngineCoolTauS, nameof(EngineCoolTauS)), (ExhaustWarmTauS, nameof(ExhaustWarmTauS)),
            (ExhaustCoolTauS, nameof(ExhaustCoolTauS)), (BarrelCoolTauS, nameof(BarrelCoolTauS)), (WreckFireWarmTauS, nameof(WreckFireWarmTauS)) }) R(v, 1, 36000, n);
        R(WreckCoolSeconds, 10, 36000, nameof(WreckCoolSeconds)); R(WreckResidualK, 0, 100, nameof(WreckResidualK));
        R(EngineSpotRadiusM, .05f, 10, nameof(EngineSpotRadiusM)); R(ExhaustSpotRadiusM, .05f, 10, nameof(ExhaustSpotRadiusM)); R(BarrelSpotCalibers, .5f, 20, nameof(BarrelSpotCalibers));
        R(PollHz, .5f, 10, nameof(PollHz)); R(MaxRangeM, 100, 10000, nameof(MaxRangeM));
    }
    private static void R(float x, float min, float max, string name)
    { if (!float.IsFinite(x) || x < min || x > max) throw new InvalidDataException($"Invalid [Heat] {name}; expected {min}..{max}."); }
}

public readonly record struct HeatInputs(bool EngineRunning, float EngineLoad, int Shots, bool Wreck, float Fire);

public sealed class VehicleHeat
{
    public float EngineK, ExhaustK, BarrelK, WreckK;
    public bool Initialized, WasWreck;
    public int TotalShots;

    /// <summary>First sighting: start at the steady state of the current inputs (history unknown).</summary>
    public void Initialize(HeatInputs input, HeatSettings s)
    {
        EngineK = EngineTarget(input, s); ExhaustK = ExhaustTarget(input, s);
        WreckK = input.Wreck ? Math.Max(s.WreckStartRiseK, FireTarget(input, s)) : FireTarget(input, s);
        WasWreck = input.Wreck; Initialized = true;
        BarrelK = Math.Min(s.BarrelMaxRiseK, Math.Max(0, input.Shots) * s.BarrelShotRiseK); TotalShots += Math.Max(0, input.Shots);
    }
    public void Step(float dt, HeatInputs input, HeatSettings s)
    {
        if (!Initialized) { Initialize(input, s); return; }
        dt = float.IsFinite(dt) ? Math.Clamp(dt, 0, 36000) : 0;
        EngineK = Relax(EngineK, EngineTarget(input, s), dt, s.EngineWarmTauS, s.EngineCoolTauS);
        ExhaustK = Relax(ExhaustK, ExhaustTarget(input, s), dt, s.ExhaustWarmTauS, s.ExhaustCoolTauS);
        // Cool first, then add the shots of this interval: shots are near the sample time.
        BarrelK *= MathF.Exp(-dt / s.BarrelCoolTauS);
        int shots = Math.Max(0, input.Shots); TotalShots += shots;
        BarrelK = Math.Min(s.BarrelMaxRiseK, BarrelK + shots * s.BarrelShotRiseK);
        if (input.Wreck && !WasWreck) WreckK = Math.Max(WreckK, s.WreckStartRiseK);
        WasWreck |= input.Wreck;
        float fire = FireTarget(input, s);
        float rest = WasWreck ? s.WreckResidualK : 0;
        // A burning hull heats quickly; afterwards it decays to just above ambient in ~WreckCoolSeconds (3 tau = 95 %).
        WreckK = Relax(WreckK, Math.Max(fire, rest), dt, s.WreckFireWarmTauS, s.WreckCoolSeconds / 3);
        if (!float.IsFinite(EngineK + ExhaustK + BarrelK + WreckK)) { EngineK = ExhaustK = BarrelK = WreckK = 0; }
    }
    public static float EngineTarget(HeatInputs i, HeatSettings s) => i.EngineRunning ? s.EngineIdleRiseK + (s.EngineFullRiseK - s.EngineIdleRiseK) * Load(i) : 0;
    public static float ExhaustTarget(HeatInputs i, HeatSettings s) => i.EngineRunning ? s.ExhaustIdleRiseK + (s.ExhaustFullRiseK - s.ExhaustIdleRiseK) * Load(i) : 0;
    private static float FireTarget(HeatInputs i, HeatSettings s) => s.WreckFireRiseK * (float.IsFinite(i.Fire) ? Math.Clamp(i.Fire, 0, 1) : 0);
    private static float Load(HeatInputs i) => float.IsFinite(i.EngineLoad) ? Math.Clamp(i.EngineLoad, 0, 1) : 0;
    /// <summary>Exact first-order step towards target; warming and cooling have their own time constants.</summary>
    public static float Relax(float value, float target, float dt, float warmTau, float coolTau)
        => target + (value - target) * MathF.Exp(-dt / (target > value ? warmTau : coolTau));
    /// <summary>Engine load 0..1 from throttle and normalised rpm.</summary>
    public static float EngineLoad(float throttle, float rpm, float idleRpm, float maxRpm)
    {
        float t = float.IsFinite(throttle) ? Math.Abs(throttle) : 0;
        float r = maxRpm > idleRpm && float.IsFinite(rpm) ? (rpm - idleRpm) / (maxRpm - idleRpm) : 0;
        return Math.Clamp(Math.Max(t, r), 0, 1);
    }
}

/// <summary>An elliptical soft spot on the sensor grid; Y counts rows of the readback buffer.</summary>
public struct HeatSpot
{
    public float X, Y, Rx, Ry, Amp; public bool Plateau;
}

public static class HeatProjection
{
    /// <summary>
    /// Viewport coordinates (0..1, Unity WorldToViewportPoint; z = metres in front of the camera)
    /// to a sensor-grid spot. False when behind the camera, off the grid or negligible.
    /// </summary>
    public static bool TryProject(float vx, float vy, float z, float radiusM, float tanHalfFovY, float aspect,
        int width, int height, float amp, bool plateau, bool invertY, out HeatSpot spot)
    {
        spot = default;
        if (!(z > .05f) || !float.IsFinite(vx) || !float.IsFinite(vy) || !(amp > 0) || !(radiusM > 0) || !(tanHalfFovY > 0) || !(aspect > 0)) return false;
        float ry = radiusM * height / (2 * z * tanHalfFovY), rx = radiusM * width / (2 * z * tanHalfFovY * aspect);
        float x = vx * width - .5f, y = (invertY ? 1 - vy : vy) * height - .5f;
        if (x + rx < 0 || x - rx > width - 1 || y + ry < 0 || y - ry > height - 1) return false;
        // Sub-pixel spots keep their energy on one pixel instead of vanishing at range.
        if (rx < 1) { amp *= rx; rx = 1; }
        if (ry < 1) { amp *= ry; ry = 1; }
        if (amp < .002f) return false;
        float cap = 2f * Math.Max(width, height);
        spot = new HeatSpot { X = x, Y = y, Rx = Math.Min(rx, cap), Ry = Math.Min(ry, cap), Amp = amp, Plateau = plateau };
        return true;
    }
    /// <summary>
    /// See-through approximation: weight 1 when the spot lies on the camera side of the vehicle
    /// centre, 0 on the far side. o = spot - centre, c = camera - spot.
    /// </summary>
    public static float Facing(float ox, float oy, float oz, float cx, float cy, float cz)
    {
        float lo = MathF.Sqrt(ox * ox + oy * oy + oz * oz), lc = MathF.Sqrt(cx * cx + cy * cy + cz * cz);
        if (!(lo > .25f) || !(lc > 0)) return 1;
        float d = (ox * cx + oy * cy + oz * cz) / (lo * lc);
        float t = Math.Clamp((d + .15f) / .35f, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>Per-pixel heat added on vehicle pixels; reused between frames (no allocation).</summary>
public sealed class HeatField
{
    public readonly int Width, Height;
    public readonly float[] Add;
    public float HullContrast = .1f;
    public int Spots { get; private set; }
    public HeatField(int width, int height) { Width = width; Height = height; Add = new float[checked(width * height)]; }
    public void Clear() { Array.Clear(Add, 0, Add.Length); Spots = 0; }
    public void AddSpot(in HeatSpot s)
    {
        if (!(s.Rx > 0) || !(s.Ry > 0) || !(s.Amp > 0)) return;
        int x0 = Math.Max(0, (int)MathF.Floor(s.X - s.Rx)), x1 = Math.Min(Width - 1, (int)MathF.Ceiling(s.X + s.Rx));
        int y0 = Math.Max(0, (int)MathF.Floor(s.Y - s.Ry)), y1 = Math.Min(Height - 1, (int)MathF.Ceiling(s.Y + s.Ry));
        if (x0 > x1 || y0 > y1) return;
        float irx = 1 / s.Rx, iry = 1 / s.Ry;
        for (int y = y0; y <= y1; y++)
        {
            float dy = (y - s.Y) * iry, dy2 = dy * dy;
            if (dy2 >= 1) continue;
            int row = y * Width;
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - s.X) * irx, d2 = dx * dx + dy2;
                if (d2 >= 1) continue;
                // Soft spots fall off smoothly; plateau spots (whole-hull wreck heat) stay flat inside 80 %.
                float w = s.Plateau ? (d2 < .64f ? 1 : (1 - d2) / .36f) : (1 - d2) * (1 - d2);
                Add[row + x] += s.Amp * w;
            }
        }
        Spots++;
    }
}
