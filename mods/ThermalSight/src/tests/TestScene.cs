using System.Numerics;
using System.Text.Json;

namespace SprocketThermalSight.Tests;

// 0.5.4: realistic synthetic night scene for the image targets and review images.
// Camera 2.5 m above flat ground (y = 0), lamp at the camera with a 6 degree cone aimed at the target,
// a 7 x 3.5 x 2.5 m tank (tracks, hull, turret, gun barrel) resting on the ground at 300 m or 1000 m,
// engine/exhaust/barrel heat spots projected through SpotCamera, and night sky above the horizon.
internal static class TestScene
{
    internal const int Width = 480, Height = 360;
    internal const float VerticalFovDeg = 8f;
    internal const float CameraHeightM = 2.5f;
    internal const int BeamGridWidth = 32, BeamGridHeight = 24;

    internal sealed class Frame
    {
        internal byte[] Scene = Array.Empty<byte>();
        internal byte[] Mask = Array.Empty<byte>();
        internal HeatField Heat = null!;
        internal int Width, Height;
        internal bool[] Vehicle = Array.Empty<bool>();
        internal bool[] Ground = Array.Empty<bool>();
        internal bool[] Sky = Array.Empty<bool>();
        internal bool[] Hot = Array.Empty<bool>();
        internal bool[] Hull = Array.Empty<bool>();
        internal float[] Illumination = Array.Empty<float>();
        internal float[] OldIllumination = Array.Empty<float>();
        internal float[] Cone = Array.Empty<float>();
    }

    private readonly struct BoxPart
    {
        internal readonly Vector3 Center, Half;
        internal readonly float Albedo;
        internal BoxPart(Vector3 center, Vector3 half, float albedo) { Center = center; Half = half; Albedo = albedo; }
    }

    // Camera and lamp at (0, 2.5, 0) aimed at the tank centre (0, 1.25, distanceM).
    // Row 0 is the bottom of the viewport (matching Unity texture readback / SpotCamera).
    internal static Frame Build(int width, int height, float distanceM, bool night = true, bool beam = false, float beamRangeM = 800f, float beamAngleDeg = 6f)
    {
        var camPos = new Vector3(0, CameraHeightM, 0);
        var target = new Vector3(0, 1.25f, distanceM);
        var camFwd = Vector3.Normalize(target - camPos);
        var cam = SpotCamera.Perspective(camPos, camFwd, Vector3.UnitY, VerticalFovDeg, (float)width / height, .1f, 5000);
        var f = new Frame { Width = width, Height = height };
        int n = width * height;
        f.Scene = new byte[n * 4]; f.Mask = new byte[n * 4];
        f.Vehicle = new bool[n]; f.Ground = new bool[n]; f.Sky = new bool[n];
        f.Hot = new bool[n]; f.Hull = new bool[n];
        f.Illumination = new float[n]; f.OldIllumination = new float[n]; f.Cone = new float[n];
        f.Heat = new HeatField(width, height) { HullContrast = .1f, HullBaseSignal = 3 / 60f, SpotSoftness = .75f };

        // 7 x 3.5 x 2.5 m tank resting on y = 0, rotated slightly so front-3/4 and side detail both show.
        const float yaw = .32f; // ~18 degrees off broadside: hull length ~7 m spans horizontally, depth ~3.5 m
        var invRot = Matrix4x4.CreateRotationY(-yaw);
        var rot = Matrix4x4.CreateRotationY(yaw);
        var tankOrigin = new Vector3(0, 0, distanceM);
        var parts = new[]
        {
            new BoxPart(new Vector3(0, .32f, 0), new Vector3(3.35f, .32f, 1.75f), .28f),         // tracks & lower hull
            new BoxPart(new Vector3(0, 1.05f, 0), new Vector3(3.50f, .50f, 1.65f), .46f),        // main hull (7 x 3.3 m)
            new BoxPart(new Vector3(.35f, 2.02f, 0), new Vector3(1.55f, .48f, 1.25f), .54f),     // turret (top at 2.50 m)
            new BoxPart(new Vector3(3.15f, 2.04f, 0), new Vector3(1.65f, .10f, .10f), .48f)      // gun barrel
        };

        // Heat spots in tank-local space transformed to world, projected through the exact same HeatProjection as the game.
        Vector3 ToWorld(Vector3 local) => tankOrigin + Vector3.TransformNormal(local, rot);
        var engineCenter = ToWorld(new Vector3(-2.15f, 1.15f, 0));
        if (HeatProjection.EvaluateBox(cam, engineCenter,
                Vector3.TransformNormal(new Vector3(1.05f, 0, 0), rot),
                Vector3.TransformNormal(new Vector3(0, .45f, 0), rot),
                Vector3.TransformNormal(new Vector3(0, 0, 1.15f), rot),
                .9f, .50f, target, .35f, 4000, width, height, false, out var engineSpot, out _, out _, out _) == SpotReject.None)
            f.Heat.AddSpot(engineSpot);
        void Spot(Vector3 local, float radiusM, float amp)
        {
            if (HeatProjection.Evaluate(cam, ToWorld(local), radiusM, amp, false, target, .35f, 4000, width, height, false, false, out var s, out _, out _, out _) == SpotReject.None)
                f.Heat.AddSpot(s);
        }
        Spot(new Vector3(-3.35f, 1.28f, -.65f), .35f, 1.2f);   // exhaust (idle 120 K -> ~0.42..0.70 after facing)
        Spot(new Vector3(2.20f, 2.04f, 0), .14f, .24f);        // barrel breech
        Spot(new Vector3(3.30f, 2.04f, 0), .13f, .20f);        // barrel mid
        Spot(new Vector3(4.50f, 2.04f, 0), .12f, .18f);        // muzzle

        // Helper: intersect a ray from camPos against the tank boxes and flat y = 0 ground.
        bool TraceTank(Vector3 origin, Vector3 dir, out float hitT, out Vector3 normal, out float albedo, out Vector3 localHit)
        {
            var lo = Vector3.TransformNormal(origin - tankOrigin, invRot);
            var ld = Vector3.TransformNormal(dir, invRot);
            hitT = float.PositiveInfinity; normal = Vector3.UnitY; albedo = .4f; localHit = Vector3.Zero;
            bool hit = false;
            foreach (var part in parts)
            {
                if (IntersectBox(lo - part.Center, ld, part.Half, out float t, out var nrm) && t > .1f && t < hitT)
                {
                    hitT = t;
                    normal = Vector3.TransformNormal(nrm, rot);
                    albedo = part.Albedo;
                    localHit = lo + ld * t;
                    hit = true;
                }
            }
            return hit;
        }

        // Key light (moonlight / ambient sky direction) for subtle natural 3D face shading.
        var lightDir = Vector3.Normalize(new Vector3(-.35f, .78f, -.52f));
        float tanHalf = MathF.Tan(VerticalFovDeg * MathF.PI / 360f);
        float aspect = (float)width / height;

        // Pixel scene synthesis (exact ray-traced ground + sky + 3D tank).
        for (int y = 0; y < height; y++)
        {
            float sy = ((y + .5f) / height - .5f) * 2f * tanHalf;
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float sx = ((x + .5f) / width - .5f) * 2f * tanHalf * aspect;
                var dir = Vector3.Normalize(camFwd + cam.Right * sx + cam.Up * sy);

                if (TraceTank(camPos, dir, out _, out var normal, out float albedo, out var localHit))
                {
                    f.Vehicle[i] = true;
                    f.Mask[i * 4] = f.Mask[i * 4 + 1] = f.Mask[i * 4 + 2] = 255;
                    f.Hull[i] = f.Heat.Add[i] < .01f;
                    f.Hot[i] = f.Heat.Add[i] >= .01f;
                    float ndl = Math.Clamp(Vector3.Dot(normal, lightDir), 0, 1);
                    // Gentle surface panel variation (smooth world-space, no pixel checkerboard).
                    float panel = .92f + .08f * MathF.Sin(localHit.X * 2.1f) * MathF.Cos(localHit.Y * 3.7f);
                    float luma = albedo * (.55f + .65f * ndl) * panel;
                    if (!night) luma = Math.Clamp(luma * 1.35f, 0, 1);
                    byte b = (byte)Math.Clamp((int)MathF.Round(luma * 255), 0, 255);
                    f.Scene[i * 4] = f.Scene[i * 4 + 1] = f.Scene[i * 4 + 2] = b; f.Scene[i * 4 + 3] = 255;
                }
                else if (dir.Y < -.0002f)
                {
                    f.Ground[i] = true;
                    float tGround = -CameraHeightM / dir.Y;
                    float wx = camPos.X + dir.X * tGround;
                    float wz = camPos.Z + dir.Z * tGround;
                    // Smooth multi-scale world-space terrain shading (no repeating pixel grid / checkerboard).
                    float terrain = SmoothTerrain(wx, wz);
                    float haze = Math.Clamp(tGround / 2500f, 0, .25f);
                    float luma = night
                        ? Math.Clamp(.14f + .16f * terrain - .04f * haze, .04f, .45f)
                        : Math.Clamp(.28f + .26f * terrain, .10f, .70f);
                    byte b = (byte)Math.Clamp((int)MathF.Round(luma * 255), 0, 255);
                    f.Scene[i * 4] = f.Scene[i * 4 + 1] = f.Scene[i * 4 + 2] = b; f.Scene[i * 4 + 3] = 255;
                }
                else
                {
                    f.Sky[i] = true;
                    float elev = Math.Clamp(dir.Y * 14f, 0, 1);
                    float luma = night ? .022f - .012f * elev : .55f - .15f * elev;
                    byte b = (byte)Math.Clamp((int)MathF.Round(luma * 255), 0, 255);
                    f.Scene[i * 4] = f.Scene[i * 4 + 1] = f.Scene[i * 4 + 2] = b; f.Scene[i * 4 + 3] = 255;
                }
            }
        }

        if (beam)
        {
            // Lamp sits at the camera (0, 2.5, 0) with a 6 degree cone aimed at the target.
            // Build BOTH the 0.5.4 distance/occlusion grid (with -1 for sky) and the 0.5.3 grid (0 for sky/outside cone).
            var grid = new float[BeamGridWidth * BeamGridHeight];
            var oldGrid = new float[BeamGridWidth * BeamGridHeight];
            var lampForward = camFwd;
            for (int gy = 0; gy < BeamGridHeight; gy++)
            {
                float gsy = ((gy + .5f) / BeamGridHeight - .5f) * 2f * tanHalf;
                for (int gx = 0; gx < BeamGridWidth; gx++)
                {
                    int cell = gy * BeamGridWidth + gx;
                    grid[cell] = -1f;
                    oldGrid[cell] = 0f;
                    float gsx = ((gx + .5f) / BeamGridWidth - .5f) * 2f * tanHalf * aspect;
                    var rayDir = Vector3.Normalize(camFwd + cam.Right * gsx + cam.Up * gsy);
                    float dist = float.PositiveInfinity;
                    if (TraceTank(camPos, rayDir, out float tankT, out _, out _, out _)) dist = tankT;
                    else if (rayDir.Y < -.0002f) dist = -CameraHeightM / rayDir.Y;
                    if (!float.IsFinite(dist)) continue;
                    grid[cell] = dist >= beamRangeM ? 0f : IrBeamPolicy.DistanceFalloff(dist, beamRangeM);
                    var offset = rayDir * dist;
                    oldGrid[cell] = IrBeamPolicy.Illumination(offset.X, offset.Y, offset.Z, lampForward.X, lampForward.Y, lampForward.Z, beamRangeM, beamAngleDeg);
                }
            }

            // Per-pixel 0.5.4 illumination (per-pixel cone * normalised bilinear grid) and 0.5.3 bilinear stretch.
            for (int y = 0; y < height; y++)
            {
                float vy = ((y + .5f) / height - .5f) * 2f * tanHalf;
                float gsy = (y + .5f) * BeamGridHeight / height - .5f;
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    float vx = ((x + .5f) / width - .5f) * 2f * tanHalf * aspect;
                    float gsx = (x + .5f) * BeamGridWidth / width - .5f;
                    var rayDir = Vector3.Normalize(camFwd + cam.Right * vx + cam.Up * vy);
                    float cone = IrBeamPolicy.PixelWeight(rayDir.X, rayDir.Y, rayDir.Z, lampForward.X, lampForward.Y, lampForward.Z, beamAngleDeg);
                    f.Cone[i] = cone;
                    // Pure sky has no surface to reflect the lamp; the grid handles distance/occlusion without darkening ground next to sky.
                    float gridSample = f.Sky[i] ? 0f : IrBeamPolicy.SampleGrid(grid, BeamGridWidth, BeamGridHeight, gsx, gsy);
                    f.Illumination[i] = IrBeamPolicy.Combine(cone, gridSample);

                    // 0.5.3 unmasked bilinear stretch for the "before" comparison images.
                    float csx = Math.Clamp(gsx, 0, BeamGridWidth - 1), csy = Math.Clamp(gsy, 0, BeamGridHeight - 1);
                    int x0 = (int)csx, y0 = (int)csy, x1 = Math.Min(x0 + 1, BeamGridWidth - 1), y1 = Math.Min(y0 + 1, BeamGridHeight - 1);
                    float tx = csx - x0, ty = csy - y0;
                    float a = oldGrid[y0 * BeamGridWidth + x0] * (1 - tx) + oldGrid[y0 * BeamGridWidth + x1] * tx;
                    float b = oldGrid[y1 * BeamGridWidth + x0] * (1 - tx) + oldGrid[y1 * BeamGridWidth + x1] * tx;
                    f.OldIllumination[i] = a * (1 - ty) + b * ty;
                }
            }
        }
        return f;
    }

    // Smooth value noise in world metres: rolling earth patches without any repeating pixel pattern.
    private static float SmoothTerrain(float wx, float wz)
    {
        float v1 = ValueNoise(wx * .045f, wz * .045f);
        float v2 = ValueNoise(wx * .13f + 17.3f, wz * .13f - 9.1f);
        float v3 = ValueNoise(wx * .35f - 5.7f, wz * .35f + 23.4f);
        return .55f * v1 + .30f * v2 + .15f * v3;
    }
    private static float ValueNoise(float x, float z)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = x - ix, fz = z - iz;
        fx = fx * fx * (3 - 2 * fx);
        fz = fz * fz * (3 - 2 * fz);
        float a = Hash01(ix, iz), b = Hash01(ix + 1, iz), c = Hash01(ix, iz + 1), d = Hash01(ix + 1, iz + 1);
        return (a * (1 - fx) + b * fx) * (1 - fz) + (c * (1 - fx) + d * fx) * fz;
    }
    private static float Hash01(int x, int z)
    {
        uint h = (uint)(x * 374761393 + z * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    private static bool IntersectBox(Vector3 origin, Vector3 dir, Vector3 half, out float tHit, out Vector3 normal)
    {
        tHit = 0; normal = Vector3.Zero;
        float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
        int hitAxis = -1; float hitSign = 1;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
            float h = axis == 0 ? half.X : axis == 1 ? half.Y : half.Z;
            if (MathF.Abs(d) < 1e-7f) { if (o < -h || o > h) return false; continue; }
            float inv = 1f / d;
            float t1 = (-h - o) * inv, t2 = (h - o) * inv;
            float sign = -1;
            if (t1 > t2) { (t1, t2) = (t2, t1); sign = 1; }
            if (t1 > tmin) { tmin = t1; hitAxis = axis; hitSign = sign; }
            tmax = MathF.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        if (tmax < 0) return false;
        tHit = tmin >= 0 ? tmin : tmax;
        normal = hitAxis == 0 ? new Vector3(hitSign, 0, 0) : hitAxis == 1 ? new Vector3(0, hitSign, 0) : new Vector3(0, 0, hitSign);
        return true;
    }

    // Faithful 0.5.3 "before" processor for activeIR and imageIntensifier (linear gain, old noise),
    // so review *-before.png images genuinely show 0.5.3 behaviour before 0.5.4 changes.
    internal static byte[] Process053(ThermalProfile p, Frame frame, uint seed)
    {
        if (p.SensorType == "thermal")
            return new SensorWorkspace(p).Process(frame.Scene, frame.Mask, seed, null, frame.Heat).ToArray();
        int count = p.Width * p.Height;
        var sig = new float[count];
        var bl = new float[count];
        float gain = 1;
        if (p.SensorType == "imageIntensifier")
        {
            double mean = 0;
            for (int i = 0; i < count; i++) mean += Luma(frame.Scene, i * 4);
            gain = Math.Clamp(.45f / Math.Max((float)(mean / count), .0001f), .25f, 64);
        }
        for (int i = 0; i < count; i++)
        {
            float luma = Luma(frame.Scene, i * 4);
            if (p.SensorType == "imageIntensifier")
            {
                sig[i] = Math.Clamp(luma * gain, 0, 1);
                bl[i] = Math.Max(0, luma * gain - .75f);
            }
            else
                sig[i] = luma * (.002f + 20 * frame.OldIllumination[i]);
        }
        var lut = SensorProcessor.BuildPalette(p);
        var output = new byte[count * 4];
        float noise = p.SensorType == "imageIntensifier" ? p.Noise * MathF.Sqrt(gain) : p.Noise;
        float filter = p.Blur - p.Sharpen;
        bool neighbourhood = p.Blur > 0 || p.Sharpen > 0;
        uint state = seed == 0 ? 1u : seed;
        for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++)
        {
            int i = y * p.Width + x;
            float sum = 0; int samples = 0;
            if (neighbourhood) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            { int xx = Math.Clamp(x + dx, 0, p.Width - 1), yy = Math.Clamp(y + dy, 0, p.Height - 1); sum += sig[yy * p.Width + xx]; samples++; }
            float v = !neighbourhood ? sig[i] : sig[i] + (sum / samples - sig[i]) * filter;
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            if (p.SensorType == "imageIntensifier")
            {
                float glow = 0;
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                    glow += bl[Math.Clamp(y + dy, 0, p.Height - 1) * p.Width + Math.Clamp(x + dx, 0, p.Width - 1)];
                v += glow * .08f;
            }
            v += ((state & 0xffff) / 65535f * 2 - 1) * noise;
            v = p.SensorType == "activeIR" ? Math.Clamp(v * p.Contrast + p.Brightness, 0, 1)
                : Math.Clamp((v - .5f) * p.Contrast + .5f + p.Brightness, 0, 1);
            v = MathF.Pow(v, 1 / p.Gamma);
            v = MathF.Round(v * (p.GrayLevels - 1)) / (p.GrayLevels - 1);
            if (p.Palette == "blackHot") v = 1 - v;
            int target = (y * p.Width + x) * 4;
            byte b = (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
            output[target] = lut[b * 3]; output[target + 1] = lut[b * 3 + 1]; output[target + 2] = lut[b * 3 + 2]; output[target + 3] = 255;
        }
        return output;
    }

    private static float Luma(byte[] scene, int k) => (.2126f * scene[k] + .7152f * scene[k + 1] + .0722f * scene[k + 2]) / 255f;

    internal sealed record Stats(double Background, double Vehicle, double Hot, double Clipped, double Noise, double Separation, double Detail);

    // Numbers on the 0..255 image (green channel for greenHot tubes, red/luma for thermal),
    // mirrored for black-hot so the targets read the same way.
    internal static Stats Measure(byte[] image, Frame f, bool blackHot, bool greenChannel = false)
    {
        int ch = greenChannel ? 1 : 0;
        double bg = 0, veh = 0, hot = 0; int nb = 0, nh = 0, clipped = 0, nveh = 0;
        var vehValues = new List<double>();
        for (int i = 0; i < f.Vehicle.Length; i++)
        {
            double v = image[i * 4 + ch];
            if (blackHot) v = 255 - v;
            if (f.Vehicle[i])
            {
                nveh++; veh += v; if (v >= 254) clipped++;
                vehValues.Add(v);
                if (f.Hot[i]) { hot += v; nh++; }
            }
            else if (f.Ground[i]) { bg += v; nb++; }
        }
        // Noise: mean absolute difference between horizontally adjacent ground pixels.
        double noise = 0; int pairs = 0;
        for (int y = 0; y < f.Height; y++) for (int x = 1; x < f.Width; x++)
        {
            int i = y * f.Width + x;
            if (!f.Ground[i] || !f.Ground[i - 1]) continue;
            double a = image[i * 4 + ch], b = image[(i - 1) * 4 + ch];
            if (blackHot) { a = 255 - a; b = 255 - b; }
            noise += Math.Abs(a - b); pairs++;
        }
        double bgMean = nb > 0 ? bg / nb : 0;
        double vehMean = nveh > 0 ? veh / nveh : 0;
        double detail = vehValues.Count > 1 ? Math.Sqrt(vehValues.Average(v => (v - vehMean) * (v - vehMean))) : 0;
        return new Stats(bgMean, vehMean, nh > 0 ? hot / nh : 0,
            nveh > 0 ? 100.0 * clipped / nveh : 100, pairs > 0 ? noise / pairs : 0, vehMean - bgMean, detail);
    }

    // PNG writer (RGBA32). Default flip=true converts bottom-up sensor rows to top-down PNG rows.
    internal static void Png(string path, byte[] rgba, int width, int height, bool flip = true)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk(file, "IHDR", header);
        using var data = new MemoryStream();
        using (var zip = new System.IO.Compression.ZLibStream(data, System.IO.Compression.CompressionLevel.Optimal, true))
            for (int y = 0; y < height; y++)
            {
                zip.WriteByte(0);
                int row = (flip ? height - 1 - y : y) * width * 4;
                zip.Write(rgba, row, width * 4);
            }
        Chunk(file, "IDAT", data.ToArray());
        Chunk(file, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string name, byte[] data)
    {
        Span<byte> n = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(n, data.Length); s.Write(n);
        var type = System.Text.Encoding.ASCII.GetBytes(name); s.Write(type); s.Write(data);
        uint crc = 0xffffffff;
        foreach (byte b in type.Concat(data)) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1; }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(n, ~crc); s.Write(n);
    }

    // The 0.5.3 catalogue, read from the untouched baseline copy, for the "before" images.
    internal static ThermalProfile? OldProfile(string id)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string path = Path.Combine(root, "data/thermal-models-0.5.3.json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var model in doc.RootElement.GetProperty("models").EnumerateArray())
            if (model.GetProperty("componentId").GetString() == id)
                return JsonSerializer.Deserialize<ThermalProfile>(model.GetRawText(), ThermalCatalog.JsonOptions);
        return null;
    }
}
