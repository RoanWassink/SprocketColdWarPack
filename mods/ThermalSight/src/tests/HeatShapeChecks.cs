extern alias baseline;
using SprocketThermalSight;
using Old = baseline::SprocketThermalSight;
using System.Numerics;
using System.Diagnostics;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;

static class HeatShapeChecks
{
    static int checks;
    static string root = "";
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    static void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { Check(true, name); return; } Check(false, name); }
    static void Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); Directory.CreateDirectory(Path.Combine(root, "review-0.5.2"));
        Projection(); Surface(); Ammo();
        var catalog = ThermalCatalog.Read(Path.Combine(root, "package/BepInEx/plugins/SprocketThermalSight/thermal-models.json"));
        Check(catalog.Resolve("tiGen3Uncooled").Contrast == 1.6f && catalog.Resolve("tiGen3Uncooled").BackgroundLevel == .28f, "uncooled catalog (0.5.4 standard)");
        Check(catalog.Map("thermalSightMk3BlackHot") == "tiGen2" && catalog.AliasBlackHot("thermalSightMk3BlackHot") && !catalog.AliasBlackHot("thermalSightModel3"), "legacy black-hot part maps to Gen 2 and keeps its polarity");
        Check(catalog.Resolve("tiGen2").Contrast == 1.7f, "Gen2 explicit contrast");
        Render(catalog, "tiGen3HD", "hd-whiteHot"); Render(catalog, "tiGen3Uncooled", "uncooled-whiteHot");
        Benchmark(catalog);
        Console.WriteLine($"PASS: {checks} targeted 0.5.2 checks; PNGs and timings in review-0.5.2. No native/gameplay claims.");
    }
    static SpotCamera Camera(int w, int h) => SpotCamera.Perspective(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 6, (float)w / h, .1f, 5000);
    static void Projection()
    {
        var cam = Camera(640, 480); var c = new Vector3(0, 0, 300);
        foreach (float yaw in new[] { 0f, .4f, 1.2f, 1.57f, 2.4f }) foreach (float roll in new[] { 0f, .3f, 1.1f })
        {
            var rot = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationZ(roll);
            var x = Vector3.TransformNormal(new Vector3(2, 0, 0), rot);
            var y = Vector3.TransformNormal(new Vector3(0, .6f, 0), rot);
            var z = Vector3.TransformNormal(new Vector3(0, 0, .9f), rot);
            Check(HeatProjection.TryBox(cam, c, x, y, z, 640, 480, false, out var s), "rotated box projects");
            Check(s.Polygon.Count >= 4 && s.Polygon.Count <= 6, "box hull vertex count");
            Check(cam.ToViewport(c, out var vx, out var vy, out _), "box center projects");
            Check(s.Polygon.Interior(vx * 640 - .5f, vy * 480 - .5f) > .7f, "rotated hull contains center");
            for (int i = 0; i < 8; i++)
            {
                var corner = c + ((i & 1) == 0 ? -x : x) + ((i & 2) == 0 ? -y : y) + ((i & 4) == 0 ? -z : z);
                cam.ToViewport(corner, out vx, out vy, out _);
                for (int j = 0; j < s.Polygon.Count; j++) { var e = s.Polygon.Edge(j); Check(e.X * (vx * 640 - .5f) + e.Y * (vy * 480 - .5f) + e.Z > -.0001f, "all eight corners inside hull"); }
            }
            Check(HeatProjection.TryBox(cam, c, x, y, z, 640, 480, true, out var flipped) && Math.Abs(flipped.Y + s.Y - 479) < .001f, "invert box Y");
        }
        HeatProjection.TryBox(cam, c, new(2, 0, 0), new(0, .5f, 0), new(0, 0, 1), 640, 480, false, out var rectangle);
        Check(rectangle.Polygon.Interior(rectangle.X + .8f * rectangle.Rx, rectangle.Y + .8f * rectangle.Ry) > 0, "rectangle corner is filled outside old ellipse");
        foreach (var bad in new[] { Vector3.Zero, new Vector3(float.NaN, 0, 0), new Vector3(1000, 0, 0) })
        {
            var r = HeatProjection.EvaluateBox(cam, c, bad, Vector3.UnitY, Vector3.UnitZ, .9f, .5f, c, .35f, 4000, 640, 480, false, out var s, out _, out _, out _);
            var old = HeatProjection.Evaluate(cam, c, .9f, .5f, true, c, .35f, 4000, 640, 480, false, false, out var sphere, out _, out _, out _);
            Check(r == old && s.Polygon.Count == 0 && s.Rx == sphere.Rx && s.Amp == sphere.Amp, "invalid box falls back exactly to configured sphere");
        }
        Check(!HeatProjection.TryBox(cam, new(0, 0, .1f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 640, 480, false, out _), "near-plane box safely falls back");
        Check(HeatProjection.EvaluateBox(cam, new(0, 0, -300), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, .9f, 1, c, .35f, 4000, 640, 480, false, out _, out _, out _, out _) == SpotReject.BehindCamera, "behind camera");
        Check(HeatProjection.EvaluateBox(cam, c, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, .9f, 1, c, .35f, 100, 640, 480, false, out _, out _, out _, out _) == SpotReject.OutOfRange, "range bound preserved");
        var field = new HeatField(640, 480); rectangle.Amp = .5f; field.AddSpot(rectangle);
        Check(field.Add.Any(v => v > 0), "polygon rasterized");
        for (int i = 0; i < field.Add.Length; i++) if (rectangle.Polygon.Interior(i % 640, i / 640) <= 0) Check(field.Add[i] == 0, "polygon raster stays inside hull");
    }
    static void Surface()
    {
        var s = new HeatSettings(); Check(s.HullBaseK == 3 && s.SpotSoftness == .75f, "new defaults");
        var keys = new List<string>(); s.ReadSurface((key, def, help) => { keys.Add(key); return key == "HullBaseK" ? 5 : .4f; });
        Check(keys.SequenceEqual(new[] { "HullBaseK", "SpotSoftness" }) && s.HullBaseK == 5 && s.SpotSoftness == .4f, "runtime binding delegate reads exact config keys"); s.Validate();
        s.HullBaseK = -1; Reject(s.Validate, "reject negative hull base"); s.HullBaseK = 0;
        foreach (float v in new[] { -.1f, 1.1f, float.NaN }) { s.SpotSoftness = v; Reject(s.Validate, "invalid softness rejected"); }
        foreach (float soft in new[] { 0f, .1f, .75f, 1f })
        {
            Check(HeatField.EdgeWeight(0, soft) == 0 && HeatField.EdgeWeight(1, soft) == 1, "edge zero center full");
            float prev = 0; for (int i = 0; i <= 100; i++) { float w = HeatField.EdgeWeight(i / 100f, soft); Check(w >= prev && w <= 1, "monotone edge"); prev = w; }
        }
        Check(HeatField.EdgeWeight(.001f, .75f) < .00001f, "continuous zero edge");
        var p = new ThermalProfile { ComponentId = "test", DisplayName = "test", Width = 80, Height = 60, Blur = 0, Noise = 0, Contrast = 1, GrayLevels = 256 };
        var scene = new byte[80 * 60 * 4]; var mask = new byte[scene.Length]; for (int i = 0; i < 80 * 60; i++) { scene[i * 4] = 80; if (i % 80 > 40) mask[i * 4] = 255; }
        var f = new HeatField(80, 60); var before = new Old.HeatField(80, 60);
        var oldp = System.Text.Json.JsonSerializer.Deserialize<Old.ThermalProfile>(System.Text.Json.JsonSerializer.Serialize(p))!;
        var expected = new Old.SensorWorkspace(oldp).Process(scene, mask, 1, heat: before);
        Check(new SensorWorkspace(p).Process(scene, mask, 1, heat: f).SequenceEqual(expected), "HullBaseK=0 exactly matches 0.5.1 on same field");
        f.HullBaseSignal = 3 / 60f;
        var after = new SensorWorkspace(p).Process(scene, mask, 1, heat: f);
        for (int i = 0; i < 80 * 60; i++) Check(mask[i * 4] == 0 ? after[i * 4] == expected[i * 4] : after[i * 4] > expected[i * 4], "hull base only on mask");
        var wreck = new HeatSpot { X = 30, Y = 30, Rx = 12, Ry = 8, Amp = .4f, Plateau = true };
        f.Clear(); before.Clear(); f.AddSpot(wreck); before.AddSpot(new Old.HeatSpot { X = 30, Y = 30, Rx = 12, Ry = 8, Amp = .4f, Plateau = true });
        Check(f.Add.SequenceEqual(before.Add), "wreck plateau byte-for-byte unchanged");
        foreach (var soft in new[] { .2f, .75f, 1f }) { var field = new HeatField(31, 31) { SpotSoftness = soft }; field.AddSpot(new HeatSpot { X = 15, Y = 15, Rx = 10, Ry = 10, Amp = 1 }); Check(field.Add[15 * 31 + 15] == 1 && field.Add[15 * 31 + 25] == 0, "ellipse center and edge"); }
    }
    public sealed record Round(string? Label, string? Code, int Rounds, bool Chambered);
    public sealed record State(bool Frozen, bool ChamberedConfirmed, string? ChamberedLabel, Round[] Rounds);
    static void Ammo()
    {
        var list = new List<(string Code, int Count, bool Chambered)>();
        Check(AmmoSnapshotReader.Read(null, list, out var status) == null && list.Count == 0 && status == "no-snapshot", "no EB data");
        var state = new State(false, true, "APFSDS", new[] { new Round("Long dart", "APFSDS", 12, true), new Round("HE shell", "HE", 5, false) });
        Check(AmmoSnapshotReader.Read(state, list, out status) == "Long dart" && list[0] == ("Long dart", 12, true) && list[1] == ("HE shell", 5, false), "confirmed EB names/counts");
        Check(AmmoSnapshotReader.Read(state with { ChamberedConfirmed = false }, list, out status) == null && list.Count == 2 && list.All(r => !r.Chambered) && status == "chamber-unconfirmed", "stock list independent of confirmed chamber");
        Check(AmmoSnapshotReader.Read(state with { Frozen = true }, list, out status) == null && list.Count == 0 && status == "frozen", "frozen data hidden");
        var incomplete = new State(false, true, null, new[] { new Round(" ", "HE", 0, true), new Round(null, null, 4, false), new Round("Bad", "X", -1, false) });
        Check(AmmoSnapshotReader.Read(incomplete, list, out status) == "HE" && list.Count == 1 && list[0].Count == 0, "fallback code, zero count, no invented names/counts");
        var d = new HudData { Style = HudStyle.Gen3Hd }; var raster = new HudRaster(1);
        HudPainter.Paint(raster, d, false); Check(raster.Texts.Any(t => t.Text == "----" && t.X == 28 && t.Size >= 14), "HD missing list placeholder");
        d.Rounds.AddRange(new[] { ("Long dart", 12, true), ("HE shell", 5, false) }); HudPainter.Paint(raster, d, false);
        Check(raster.Texts.Any(t => t.Text == "Long dart" && t.Size == 14 && t.Color.A == 1) && raster.Texts.Any(t => t.Text == "12"), "HD readable name and count");
        Check(raster.Alpha(24, 400) >= 200, "HD dark backing");
        for (int i = 0; i < 4; i++) d.Rounds.Add(("Extra type " + i, i, false));
        HudPainter.Paint(raster, d, false);
        Check(raster.Texts.Count(t => t.X == 28 && t.Size == 14) == 6, "all six EB types displayed");
    }
    static (byte[] scene, byte[] mask, HeatSpot[] spots, Old.HeatSpot[] oldSpots) Fixture(int w, int h)
    {
        var cam = Camera(w, h); var scene = new byte[w * h * 4]; var mask = new byte[scene.Length];
        // Side silhouette at 300 m: hull, turret, tracks, continuous barrel. Only mask pixels receive heat.
        foreach (var (c, e) in new[] { (new Vector3(0, 0, 300), new Vector3(3, .7f, 1.4f)), (new Vector3(0, 1, 300), new Vector3(1.2f, .5f, 1)), (new Vector3(0, -.75f, 300), new Vector3(3.2f, .25f, 1.5f)), (new Vector3(2.5f, 1, 300), new Vector3(2, .12f, .12f)) })
        {
            HeatProjection.TryBox(cam, c, new(e.X, 0, 0), new(0, e.Y, 0), new(0, 0, e.Z), w, h, false, out var shape);
            for (int y = Math.Max(0, (int)(shape.Y - shape.Ry)); y <= Math.Min(h - 1, (int)(shape.Y + shape.Ry + 1)); y++) for (int x = Math.Max(0, (int)(shape.X - shape.Rx)); x <= Math.Min(w - 1, (int)(shape.X + shape.Rx + 1)); x++) if (shape.Polygon.Interior(x, y) > 0) mask[(y * w + x) * 4] = 255;
        }
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int k = (y * w + x) * 4; byte v = (byte)Math.Clamp(55 + 25 * Math.Sin(x * .018) * Math.Cos(y * .023) + (mask[k] > 0 ? 10 * Math.Sin(x * .1) : 0), 0, 255); scene[k] = scene[k + 1] = scene[k + 2] = v; scene[k + 3] = 255; }
        var spots = new HeatSpot[5]; var oldSpots = new Old.HeatSpot[5];
        var positions = new[] { new Vector3(-1.8f, 0, 298.8f), new Vector3(-2.6f, .4f, 298.5f), new Vector3(.7f, 1, 298.8f), new Vector3(2.6f, 1, 298.8f), new Vector3(4.3f, 1, 298.8f) };
        for (int i = 0; i < 5; i++)
        {
            float radius = i == 0 ? new Vector3(.95f, .55f, .65f).Length() * .6f : i == 1 ? .35f : .16f, amp = i == 0 ? .5f : i == 1 ? 2 : .18f;
            HeatProjection.Evaluate(cam, positions[i], radius, amp, false, Vector3.Zero, .35f, 4000, w, h, false, false, out spots[i], out _, out _, out _);
            oldSpots[i] = new Old.HeatSpot { X = spots[i].X, Y = spots[i].Y, Rx = spots[i].Rx, Ry = spots[i].Ry, Amp = spots[i].Amp };
        }
        var rot = Matrix4x4.CreateRotationY(.5f) * Matrix4x4.CreateRotationZ(.08f);
        HeatProjection.TryBox(cam, positions[0], Vector3.TransformNormal(new(.95f, 0, 0), rot), Vector3.TransformNormal(new(0, .55f, 0), rot), Vector3.TransformNormal(new(0, 0, .65f), rot), w, h, false, out spots[0]); spots[0].Amp = .5f;
        return (scene, mask, spots, oldSpots);
    }
    static void Render(ThermalCatalog catalog, string id, string name)
    {
        var p = catalog.Resolve(id); var oldCatalog = Old.ThermalCatalog.Read(Path.Combine(root, "data/thermal-models-0.5.1.json")); var op = oldCatalog.Resolve(id);
        var (scene, mask, spots, oldSpots) = Fixture(p.Width, p.Height);
        var field = new HeatField(p.Width, p.Height) { HullBaseSignal = 3 / 60f }; foreach (var s in spots) field.AddSpot(s);
        var oldField = new Old.HeatField(p.Width, p.Height); foreach (var s in oldSpots) oldField.AddSpot(s);
        var before = new Old.SensorWorkspace(op).Process(scene, mask, 7, heat: oldField); var after = new SensorWorkspace(p).Process(scene, mask, 7, heat: field);
        Png(Path.Combine(root, $"review-0.5.2/{name}-before.png"), before, p.Width, p.Height, true);
        Png(Path.Combine(root, $"review-0.5.2/{name}-after.png"), after, p.Width, p.Height, true);
        Png(Path.Combine(root, $"review-0.5.2/{name}-mask.png"), mask, p.Width, p.Height, true, true);
        var bg = new List<byte>(); var hull = new List<byte>(); var motor = new List<byte>();
        for (int i = 0; i < p.Width * p.Height; i++) { if (mask[i * 4] == 0) bg.Add(after[i * 4]); else if (spots[0].Polygon.Interior(i % p.Width, i / p.Width) > .8f) motor.Add(after[i * 4]); else if (field.Add[i] < .01f) hull.Add(after[i * 4]); }
        string stats = $"{id}: background={bg.Average(v => v):F1}, hull={hull.Average(v => v):F1}, engine={motor.Average(v => v):F1}, hull range={hull.Min()}..{hull.Max()}";
        File.AppendAllText(Path.Combine(root, "review-0.5.2/image-stats.txt"), stats + Environment.NewLine); Console.WriteLine(stats);
        // 0.5.4: the uncooled Gen 3 is white-hot by default (the polarity is a switch), so hull > background.
        if (id == "tiGen3Uncooled") { Check(bg.Average(v => v) > 15 && bg.Average(v => v) < 115, "uncooled mid-dark background"); Check(hull.Average(v => v) > bg.Average(v => v) + 25 && motor.Average(v => v) > hull.Average(v => v) && motor.Average(v => v) < 254, "uncooled background < hull < engine, unsaturated"); Check(hull.Max() - hull.Min() > 8, "uncooled hull tonal detail"); }
    }
    static (double median, double p95) Time(Action action, int n = 200)
    {
        for (int i = 0; i < 35; i++) action(); var samples = new double[n];
        for (int i = 0; i < n; i++) { long start = Stopwatch.GetTimestamp(); action(); samples[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency; }
        Array.Sort(samples); return (samples[n / 2], samples[(int)(n * .95)]);
    }
    static void Benchmark(ThermalCatalog catalog)
    {
        var report = new StringBuilder("CPU managed Release; 300 m synthetic tank, 5 spots; 35 warm-up + 200 measured frames; median/p95 ms; excludes Unity, GPU/readback and native polling.\n");
        report.AppendLine($"Runtime={Environment.Version}; logical CPUs={Environment.ProcessorCount}; OS={Environment.OSVersion}; before is compiled src-0.5.1, not a reimplementation.");
        foreach (var (w,h) in new[] { (640,480), (1280,1024) })
        {
            var (scene,mask,spots,oldSpots) = Fixture(w,h); var field = new HeatField(w,h) { HullBaseSignal = .05f }; var old = new Old.HeatField(w,h);
            Action a = () => { old.Clear(); foreach (var s in oldSpots) old.AddSpot(s); }; Action b = () => { field.Clear(); foreach (var s in spots) field.AddSpot(s); };
            var ta = Time(a); var tb = Time(b);
            var p = System.Text.Json.JsonSerializer.Deserialize<ThermalProfile>(System.Text.Json.JsonSerializer.Serialize(catalog.Resolve("tiGen3HD")))!; p.Width = w; p.Height = h;
            var op = System.Text.Json.JsonSerializer.Deserialize<Old.ThermalProfile>(System.Text.Json.JsonSerializer.Serialize(p))!;
            var ws = new SensorWorkspace(p); var ows = new Old.SensorWorkspace(op);
            var fa = Time(() => { a(); ows.Process(scene,mask,9,heat:old); }); var fb = Time(() => { b(); ws.Process(scene,mask,9,heat:field); });
            report.AppendLine($"{w}x{h} clear+raster BEFORE {ta.median:F4}/{ta.p95:F4}; AFTER {tb.median:F4}/{tb.p95:F4}; full CPU sensor BEFORE {fa.median:F3}/{fa.p95:F3}; AFTER {fb.median:F3}/{fb.p95:F3}");
            var cam = Camera(w,h); var c = new Vector3(-1.8f,0,298.8f);
            var proj = Time(() => { for(int i=0;i<100;i++) HeatProjection.EvaluateBox(cam,c,new(.95f,0,0),new(0,.55f,0),new(0,0,.65f),.9f,.5f,c,.35f,4000,w,h,false,out _,out _,out _,out _); });
            report.AppendLine($"{w}x{h} pure projection per box AFTER {proj.median / 100:F6} ms (100 projections/sample; no native Transform reads).");
        }
        File.WriteAllText(Path.Combine(root, "review-0.5.2/PERF.txt"), report.ToString()); Console.Write(report);
    }
    static void Png(string path, byte[] rgba, int w, int h, bool flip, bool mask = false)
    {
        using var file = File.Create(path); file.Write(new byte[] { 137,80,78,71,13,10,26,10 });
        byte[] header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header,w); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),h); header[8]=8; header[9]=6; Chunk(file,"IHDR",header);
        using var data = new MemoryStream();
        using (var zip = new ZLibStream(data,CompressionLevel.Optimal,true)) for(int y=0;y<h;y++) { zip.WriteByte(0); int row=(flip?h-1-y:y)*w*4; if (!mask) zip.Write(rgba,row,w*4); else for(int x=0;x<w;x++) { byte v=rgba[row+x*4]; zip.Write(new byte[] {v,v,v,255}); } }
        Chunk(file,"IDAT",data.ToArray()); Chunk(file,"IEND",Array.Empty<byte>());
    }
    static void Chunk(Stream s,string name,byte[] data)
    {
        Span<byte> n=stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(n,data.Length); s.Write(n); var type=Encoding.ASCII.GetBytes(name); s.Write(type); s.Write(data);
        uint crc=0xffffffff; foreach(byte b in type.Concat(data)) { crc^=b; for(int i=0;i<8;i++) crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1; } BinaryPrimitives.WriteUInt32BigEndian(n,~crc); s.Write(n);
    }
}
