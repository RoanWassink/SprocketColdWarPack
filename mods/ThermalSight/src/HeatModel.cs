using NMatrix = System.Numerics.Matrix4x4;
using NVec = System.Numerics.Vector3;

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
    public float HullBaseK { get; set; } = 3;
    public float SpotSoftness { get; set; } = .75f;

    public void ReadSurface(Func<string, float, string, float> read)
    {
        HullBaseK = read(nameof(HullBaseK), HullBaseK, "Uniform hull rise above ambient (K); 0 restores the 0.5.1 hull signal.");
        SpotSoftness = read(nameof(SpotSoftness), SpotSoftness, "Soft edge width (0..1) for engine, exhaust and barrel spots; 0 = hard edge.");
    }
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
    // 0.4.3: engine/exhaust spots on the far side of the hull keep this fraction (deck, plume, conducted heat).
    public float FarSideWeight { get; set; } = .35f;

    public void Validate()
    {
        R(AmbientC, -60, 60, nameof(AmbientC)); R(FullScaleKelvin, 1, 2000, nameof(FullScaleKelvin)); R(HullContrast, 0, 1, nameof(HullContrast));
        R(HullBaseK, 0, 100, nameof(HullBaseK)); R(SpotSoftness, 0, 1, nameof(SpotSoftness));
        foreach (var (v, n) in new[] { (EngineScale, nameof(EngineScale)), (ExhaustScale, nameof(ExhaustScale)), (BarrelScale, nameof(BarrelScale)), (WreckScale, nameof(WreckScale)) }) R(v, 0, 10, n);
        foreach (var (v, n) in new[] { (EngineIdleRiseK, nameof(EngineIdleRiseK)), (EngineFullRiseK, nameof(EngineFullRiseK)), (ExhaustIdleRiseK, nameof(ExhaustIdleRiseK)),
            (ExhaustFullRiseK, nameof(ExhaustFullRiseK)), (BarrelShotRiseK, nameof(BarrelShotRiseK)), (BarrelMaxRiseK, nameof(BarrelMaxRiseK)),
            (WreckStartRiseK, nameof(WreckStartRiseK)), (WreckFireRiseK, nameof(WreckFireRiseK)) }) R(v, 0, 2000, n);
        foreach (var (v, n) in new[] { (EngineWarmTauS, nameof(EngineWarmTauS)), (EngineCoolTauS, nameof(EngineCoolTauS)), (ExhaustWarmTauS, nameof(ExhaustWarmTauS)),
            (ExhaustCoolTauS, nameof(ExhaustCoolTauS)), (BarrelCoolTauS, nameof(BarrelCoolTauS)), (WreckFireWarmTauS, nameof(WreckFireWarmTauS)) }) R(v, 1, 36000, n);
        R(WreckCoolSeconds, 10, 36000, nameof(WreckCoolSeconds)); R(WreckResidualK, 0, 100, nameof(WreckResidualK));
        R(EngineSpotRadiusM, .05f, 10, nameof(EngineSpotRadiusM)); R(ExhaustSpotRadiusM, .05f, 10, nameof(ExhaustSpotRadiusM)); R(BarrelSpotCalibers, .5f, 20, nameof(BarrelSpotCalibers));
        R(PollHz, .5f, 10, nameof(PollHz)); R(MaxRangeM, 100, 10000, nameof(MaxRangeM)); R(FarSideWeight, 0, 1, nameof(FarSideWeight));
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
    public HeatPolygon Polygon;
}

// Inward half-planes of a convex hull, stored by value in the capture snapshot (no per-frame arrays).
public struct HeatPolygon
{
    public int Count;
    public NVec E0, E1, E2, E3, E4, E5, E6, E7;
    public readonly NVec Edge(int i) => i switch { 0 => E0, 1 => E1, 2 => E2, 3 => E3, 4 => E4, 5 => E5, 6 => E6, _ => E7 };
    public void Set(int i, NVec e)
    {
        switch (i) { case 0: E0 = e; break; case 1: E1 = e; break; case 2: E2 = e; break; case 3: E3 = e; break;
            case 4: E4 = e; break; case 5: E5 = e; break; case 6: E6 = e; break; default: E7 = e; break; }
    }
    public readonly float Interior(float x, float y)
    {
        float d = 1;
        for (int i = 0; i < Count; i++) { var e = Edge(i); d = Math.Min(d, e.X * x + e.Y * y + e.Z); if (d <= 0) return 0; }
        return d;
    }
}

/// <summary>Why a hot spot is not on the sensor image (0.4.3 diagnostics).</summary>
public enum SpotReject : byte { None, OutOfRange, BehindCamera, FarSide, OffScreen, Negligible, NoVehiclePixels }

/// <summary>
/// The capture camera as plain numbers: Unity's projectionMatrix * worldToCameraMatrix (m[row,col] stored
/// as M(row+1)(col+1), column vectors) plus its world pose. Same maths as Camera.WorldToViewportPoint.
/// </summary>
public readonly struct SpotCamera
{
    public readonly NMatrix M;
    public readonly NVec Position, Right, Up;
    public SpotCamera(NMatrix viewProjection, NVec position, NVec right, NVec up) { M = viewProjection; Position = position; Right = right; Up = up; }

    /// <summary>World point to viewport (0..1, y up) and depth in metres along the view axis. False behind the camera.</summary>
    public bool ToViewport(NVec w, out float vx, out float vy, out float depth)
    {
        float cx = M.M11 * w.X + M.M12 * w.Y + M.M13 * w.Z + M.M14;
        float cy = M.M21 * w.X + M.M22 * w.Y + M.M23 * w.Z + M.M24;
        float cw = M.M41 * w.X + M.M42 * w.Y + M.M43 * w.Z + M.M44;
        depth = cw; vx = vy = float.NaN;
        if (!(cw > 1e-4f)) return false;
        vx = cx / cw * .5f + .5f; vy = cy / cw * .5f + .5f;
        return float.IsFinite(vx) && float.IsFinite(vy);
    }

    /// <summary>Unity-equivalent camera: Matrix4x4.Perspective(fov, aspect, near, far) * worldToCameraMatrix.</summary>
    public static SpotCamera Perspective(NVec position, NVec forward, NVec up, float verticalFovDeg, float aspect, float near, float far)
    {
        var f = NVec.Normalize(forward);
        // Unity is left-handed: right = up x forward.
        var r = NVec.Normalize(NVec.Cross(up, f)); var u = NVec.Cross(f, r);
        float k = 1 / MathF.Tan(verticalFovDeg * MathF.PI / 360f), a = (far + near) / (near - far), b = 2 * far * near / (near - far);
        // View rows: right, up, -forward (the camera looks down -z); projection as OpenGL/Unity.
        var m = new NMatrix(
            k / aspect * r.X, k / aspect * r.Y, k / aspect * r.Z, -k / aspect * NVec.Dot(r, position),
            k * u.X, k * u.Y, k * u.Z, -k * NVec.Dot(u, position),
            -a * f.X, -a * f.Y, -a * f.Z, a * NVec.Dot(f, position) + b,
            f.X, f.Y, f.Z, -NVec.Dot(f, position));
        return new SpotCamera(m, position, r, u);
    }
}

public static class HeatProjection
{
    public static bool UsableBox(NVec x, NVec y, NVec z)
    {
        float a = x.LengthSquared(), b = y.LengthSquared(), c = z.LengthSquared();
        return float.IsFinite(a + b + c) && a > .0001f && b > .0001f && c > .0001f && a + b + c < 45
            && Math.Abs(NVec.Dot(NVec.Cross(x, y), z)) > 1e-6f;
    }

    // Eight world-space corners -> convex screen hull. Invalid/near-plane/degenerate boxes use the old ellipse.
    public static SpotReject EvaluateBox(in SpotCamera cam, NVec world, NVec ax, NVec ay, NVec az, float fallbackRadius,
        float amp, NVec vehicleCenter, float farSideWeight, float maxRangeM, int width, int height, bool invertY,
        out HeatSpot spot, out float vx, out float vy, out float depth)
    {
        bool valid = UsableBox(ax, ay, az);
        float radius = valid ? ax.Length() + ay.Length() + az.Length() : fallbackRadius;
        var result = Evaluate(cam, world, radius, amp, true, vehicleCenter, farSideWeight, maxRangeM, width, height,
            false, invertY, out spot, out vx, out vy, out depth);
        if (result != SpotReject.None || !valid) return result;
        if (TryBox(cam, world, ax, ay, az, width, height, invertY, out var shape))
        {
            shape.Amp = spot.Amp; spot = shape;
            return spot.X + spot.Rx < 0 || spot.X - spot.Rx > width - 1 || spot.Y + spot.Ry < 0 || spot.Y - spot.Ry > height - 1
                ? SpotReject.OffScreen : SpotReject.None;
        }
        return Evaluate(cam, world, fallbackRadius, amp, true, vehicleCenter, farSideWeight, maxRangeM, width, height,
            false, invertY, out spot, out vx, out vy, out depth);
    }

    public static bool TryBox(in SpotCamera cam, NVec center, NVec ax, NVec ay, NVec az,
        int width, int height, bool invertY, out HeatSpot spot)
    {
        spot = default;
        if (!UsableBox(ax, ay, az)) return false;
        Span<System.Numerics.Vector2> p = stackalloc System.Numerics.Vector2[8];
        Span<System.Numerics.Vector2> hull = stackalloc System.Numerics.Vector2[16];
        for (int i = 0; i < 8; i++)
        {
            var w = center + ((i & 1) == 0 ? -ax : ax) + ((i & 2) == 0 ? -ay : ay) + ((i & 4) == 0 ? -az : az);
            if (!cam.ToViewport(w, out float x, out float y, out var z) || z <= .05f) return false;
            p[i] = new(x * width - .5f, (invertY ? 1 - y : y) * height - .5f);
        }
        for (int i = 1; i < 8; i++)
        {
            var v = p[i]; int j = i - 1;
            while (j >= 0 && (p[j].X > v.X || (p[j].X == v.X && p[j].Y > v.Y))) { p[j + 1] = p[j]; j--; }
            p[j + 1] = v;
        }
        int n = 0;
        for (int i = 0; i < 8; i++) { while (n >= 2 && Cross(hull[n - 1] - hull[n - 2], p[i] - hull[n - 1]) <= 0) n--; hull[n++] = p[i]; }
        int lower = n + 1;
        for (int i = 6; i >= 0; i--) { while (n >= lower && Cross(hull[n - 1] - hull[n - 2], p[i] - hull[n - 1]) <= 0) n--; hull[n++] = p[i]; }
        n--; if (n < 3 || n > 8) return false;
        var centroid = System.Numerics.Vector2.Zero;
        float minX = float.MaxValue, minY = minX, maxX = float.MinValue, maxY = maxX;
        for (int i = 0; i < n; i++) { centroid += hull[i]; minX = Math.Min(minX, hull[i].X); minY = Math.Min(minY, hull[i].Y); maxX = Math.Max(maxX, hull[i].X); maxY = Math.Max(maxY, hull[i].Y); }
        centroid /= n;
        if (maxX - minX < 1 || maxY - minY < 1) return false; // Preserve the existing energy-conserving subpixel fallback.
        var polygon = new HeatPolygon { Count = n };
        for (int i = 0; i < n; i++)
        {
            var a = hull[i]; var d = hull[(i + 1) % n] - a;
            var e = new NVec(-d.Y, d.X, d.Y * a.X - d.X * a.Y);
            float inside = e.X * centroid.X + e.Y * centroid.Y + e.Z;
            if (!(inside > .001f)) return false;
            polygon.Set(i, e / inside);
        }
        spot = new HeatSpot { X = (minX + maxX) / 2, Y = (minY + maxY) / 2, Rx = (maxX - minX) / 2, Ry = (maxY - minY) / 2, Polygon = polygon };
        return true;
    }
    private static float Cross(System.Numerics.Vector2 a, System.Numerics.Vector2 b) => a.X * b.Y - a.Y * b.X;
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
        return Place(vx, vy, rx, ry, width, height, amp, plateau, invertY, out spot) == SpotReject.None;
    }

    /// <summary>
    /// Full 0.4.3 projection of one world-space spot with the capture camera's own matrices.
    /// The pixel radius comes from projecting the radius along the camera's right/up axes, so it is
    /// exact for physical (focal-length) cameras too. Row 0 of the readback is the image bottom (viewport y 0).
    /// </summary>
    public static SpotReject Evaluate(in SpotCamera cam, NVec world, float radiusM, float amp, bool facing, NVec vehicleCenter, float farSideWeight,
        float maxRangeM, int width, int height, bool plateau, bool invertY, out HeatSpot spot, out float vx, out float vy, out float depth)
    {
        spot = default; vx = vy = depth = float.NaN;
        if (!((world - cam.Position).LengthSquared() <= maxRangeM * maxRangeM)) return SpotReject.OutOfRange;
        if (!cam.ToViewport(world, out vx, out vy, out depth) || !(depth > .05f)) return SpotReject.BehindCamera;
        if (facing)
        {
            NVec o = world - vehicleCenter, c = cam.Position - world;
            amp *= Facing(o.X, o.Y, o.Z, c.X, c.Y, c.Z, farSideWeight);
            if (!(amp > 0)) return SpotReject.FarSide;
        }
        if (!(amp > 0) || !(radiusM > 0)) return SpotReject.Negligible;
        float rx = 1, ry = 1;
        if (cam.ToViewport(world + cam.Right * radiusM, out var ax, out var ay, out _)) rx = MathF.Sqrt(Sq((ax - vx) * width) + Sq((ay - vy) * height));
        if (cam.ToViewport(world + cam.Up * radiusM, out var bx, out var by, out _)) ry = MathF.Sqrt(Sq((bx - vx) * width) + Sq((by - vy) * height));
        if (!float.IsFinite(rx)) rx = 1;
        if (!float.IsFinite(ry)) ry = 1;
        return Place(vx, vy, rx, ry, width, height, amp, plateau, invertY, out spot);
    }
    private static float Sq(float x) => x * x;

    /// <summary>Pixel radii and viewport position to a grid spot; OffScreen or Negligible when it cannot be drawn.</summary>
    public static SpotReject Place(float vx, float vy, float rx, float ry, int width, int height, float amp, bool plateau, bool invertY, out HeatSpot spot)
    {
        spot = default;
        if (!float.IsFinite(vx) || !float.IsFinite(vy)) return SpotReject.BehindCamera;
        float x = vx * width - .5f, y = (invertY ? 1 - vy : vy) * height - .5f;
        if (x + rx < 0 || x - rx > width - 1 || y + ry < 0 || y - ry > height - 1) return SpotReject.OffScreen;
        // Sub-pixel spots keep their energy on one pixel instead of vanishing at range.
        if (rx < 1) { amp *= rx; rx = 1; }
        if (ry < 1) { amp *= ry; ry = 1; }
        if (!(amp >= .002f)) return SpotReject.Negligible;
        float cap = 2f * Math.Max(width, height);
        spot = new HeatSpot { X = x, Y = y, Rx = Math.Min(rx, cap), Ry = Math.Min(ry, cap), Amp = amp, Plateau = plateau };
        return SpotReject.None;
    }

    /// <summary>
    /// See-through approximation: weight 1 when the spot lies on the camera side of the vehicle
    /// centre, <paramref name="farSide"/> on the far side (0.4.0-0.4.2 used 0). o = spot - centre, c = camera - spot.
    /// </summary>
    public static float Facing(float ox, float oy, float oz, float cx, float cy, float cz, float farSide = 0)
    {
        float lo = MathF.Sqrt(ox * ox + oy * oy + oz * oz), lc = MathF.Sqrt(cx * cx + cy * cy + cz * cz);
        if (!(lo > .25f) || !(lc > 0)) return 1;
        float d = (ox * cx + oy * cy + oz * cz) / (lo * lc);
        float t = Math.Clamp((d + .15f) / .35f, 0, 1);
        float floor = float.IsFinite(farSide) ? Math.Clamp(farSide, 0, 1) : 0;
        return floor + (1 - floor) * (t * t * (3 - 2 * t));
    }

    /// <summary>Vehicle-mask pixels (any colour channel above zero) inside a spot's ellipse; used by the diagnostics.</summary>
    public static int MaskPixels(byte[] mask, int width, int height, in HeatSpot s)
    {
        if (mask.Length < width * height * 4) return 0;
        float rx = Math.Max(1, s.Rx), ry = Math.Max(1, s.Ry);
        int x0 = Math.Max(0, (int)MathF.Floor(s.X - rx)), x1 = Math.Min(width - 1, (int)MathF.Ceiling(s.X + rx));
        int y0 = Math.Max(0, (int)MathF.Floor(s.Y - ry)), y1 = Math.Min(height - 1, (int)MathF.Ceiling(s.Y + ry));
        int n = 0;
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
        {
            float dx = (x - s.X) / rx, dy = (y - s.Y) / ry;
            if (s.Polygon.Count > 0 ? s.Polygon.Interior(x, y) <= 0 : dx * dx + dy * dy > 1) continue;
            int k = (y * width + x) * 4;
            if (mask[k] > 0 || mask[k + 1] > 0 || mask[k + 2] > 0) n++;
        }
        return n;
    }
}

/// <summary>
/// 0.4.3 shot counter for one cannon, observed every frame (read-only fields of the native behaviour).
/// A shot is a LastFireTime change, or Loaded to Loading/Unloaded when the time did not change.
/// Both signals of the same shot count once, also when they arrive on different frames.
/// </summary>
public sealed class ShotCounter
{
    public const int Loaded = 0, Loading = 1, Unloaded = 2;
    private bool started, shotSinceLoaded, dropCounted;
    private float lastTime; private int lastState;
    public int Total { get; private set; }

    /// <summary>Returns the new shots since the previous observation; the first observation is the baseline.</summary>
    public int Observe(float lastFireTime, int loadState)
    {
        if (!started) { started = true; lastTime = lastFireTime; lastState = loadState; return 0; }
        int shots = 0;
        bool timeChanged = lastFireTime != lastTime && !(float.IsNaN(lastFireTime) && float.IsNaN(lastTime));
        bool dropped = lastState == Loaded && (loadState == Loading || loadState == Unloaded);
        if (timeChanged)
        {
            // A drop already counted this shot a frame earlier: the late time change is the same shot.
            if (dropCounted) dropCounted = false; else shots++;
            shotSinceLoaded = true;
        }
        if (dropped && !shotSinceLoaded) { shots++; shotSinceLoaded = true; dropCounted = true; }
        if (loadState == Loaded && lastState != Loaded) { shotSinceLoaded = false; dropCounted = false; }
        lastTime = lastFireTime; lastState = loadState; Total += shots;
        return shots;
    }
}

/// <summary>Per-pixel heat added on vehicle pixels; reused between frames (no allocation).</summary>
public sealed class HeatField
{
    public readonly int Width, Height;
    public readonly float[] Add;
    public float HullContrast = .1f;
    public float HullBaseSignal;
    public float SpotSoftness = .75f;
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
            if (s.Polygon.Count == 0 && dy2 >= 1) continue;
            int row = y * Width;
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - s.X) * irx, d2 = dx * dx + dy2;
                float inside = s.Polygon.Count > 0 ? s.Polygon.Interior(x, y) : 1 - d2;
                if (inside <= 0) continue;
                // Soft spots fall off smoothly; plateau spots (whole-hull wreck heat) stay flat inside 80 %.
                float w = s.Plateau ? (d2 < .64f ? 1 : (1 - d2) / .36f) : EdgeWeight(inside, SpotSoftness);
                Add[row + x] += s.Amp * w;
            }
        }
        Spots++;
    }

    public static float EdgeWeight(float interior, float softness)
    {
        if (interior <= 0) return 0;
        if (softness <= 0) return 1;
        float t = Math.Clamp(interior / softness, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
