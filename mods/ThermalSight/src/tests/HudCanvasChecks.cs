using SprocketThermalSight;

namespace SprocketThermalSight.Tests;

// 0.5.1: once-per-session cooling, CPU rasterising of the canvas HUD, HD image after cooling and the
// Gen 3 contrast values.
internal static class HudCanvasChecks
{
    internal static void Run(Action<bool, string> check, ThermalCatalog catalog)
    {
        Cooling(check);
        Raster(check);
        HdAfterCooling(check, catalog);
        Contrast(check, catalog);
    }

    private static void Cooling(Action<bool, string> check)
    {
        var ledger = new CoolingLedger();
        check(ledger.SetVehicle(11) && !ledger.SetVehicle(11), "vehicle set once");
        // First switch-on: cooling starts, 20 s.
        check(Near(ledger.SwitchOn(1, "HD", 100, 20, out bool started), 20) && started, "first switch-on starts cooling");
        check(Near(ledger.Remaining(1, 105), 15) && ledger.Finished(105) == null, "counting down");
        // Toggle during cooling: no restart, no pause; the clock kept running while off.
        check(Near(ledger.SwitchOn(1, "HD", 112, 20, out started), 8) && !started, "re-enable during cooling continues, no restart");
        check(Near(ledger.Remaining(1, 119), 1), "switched-off time counted");
        check(ledger.Finished(119.9f) == null && ledger.Finished(120) == "HD" && ledger.Finished(121) == null, "Cooled reported exactly once");
        // Already cooled: ready at once, no new log.
        check(ledger.SwitchOn(1, "HD", 300, 20, out started) == 0 && !started && ledger.Remaining(1, 300) == 0, "re-enable after cooled is instant");
        // Cooled while switched off: still reported once at the end.
        check(Near(ledger.SwitchOn(2, "Gen 3", 300, 20, out started), 20) && started, "second sight cools on its own");
        check(ledger.Finished(330) == "Gen 3" && ledger.Remaining(2, 340) == 0, "cooled while off");
        // Another vehicle: every sight cools again.
        check(ledger.SetVehicle(22) && ledger.Count == 0, "new vehicle clears the ledger");
        check(Near(ledger.SwitchOn(1, "HD", 400, 20, out started), 20) && started, "new vehicle cools again");
        // New mission.
        ledger.Reset();
        check(ledger.Vehicle == 0 && ledger.Count == 0 && Near(ledger.SwitchOn(1, "HD", 500, 20, out started), 20) && started, "new mission cools again");
        // CooldownSeconds = 0: instant, nothing recorded or logged.
        var instant = new CoolingLedger();
        check(instant.SwitchOn(5, "x", 1, 0, out started) == 0 && !started && instant.Count == 0, "zero seconds = instant");
        check(instant.SwitchOn(5, "x", float.NaN, 20, out started) == 0 && !started, "invalid clock ignored");
    }

    private static void Raster(Action<bool, string> check)
    {
        check(HudPainter.RasterScale(3) == 2 && HudPainter.RasterScale(1.5f) == 1.5f && HudPainter.RasterScale(.5f) == 1 && HudPainter.RasterScale(float.NaN) == 1, "raster scale 1..2");
        var r = new HudRaster(2);
        check(r.Width == 1280 && r.Height == 960 && r.Pixels.Length == 1280 * 960 * 4, "raster size 640x480 x 2");
        var d = new HudData { Style = HudStyle.Gen3Uncooled, Palette = "whiteHot", Heading = 0, Range = 1238, Magnification = 8 };
        HudPainter.Paint(r, d, true);
        // Reticle: horizontal bar Cx-60..Cx-10 at Cy, 1.8 units wide -> 3.6 px.
        int cy = 240 * 2;
        check(r.Alpha(2 * 290, cy) > 230 && r.Alpha(2 * 290, cy - 1) > 230, "line covers its pixels");
        check(r.Alpha(2 * 290, cy + 4) == 0 && r.Alpha(2 * 320, cy) == 0, "gap at the centre and outside the line width");
        check(r.Pixels[(cy * r.Width + 580) * 4] > 240 && r.Pixels[(cy * r.Width + 580) * 4 + 1] > 240, "white ink");
        check(r.Alpha(10, 10) == 0 && !r.Eyepiece, "transparent elsewhere, no eyepiece");
        check(r.Texts.Any(t => t.Text == "1 238 M" && t.Anchor == HudAnchor.Center) && r.Texts.Any(t => t.Text == "WH  ×8.0"), "texts as items for TextMeshPro");
        check(r.Texts.Any(t => t.Text == "000"), "heading tape label");
        // Every style paints without exceptions and leaves something visible.
        foreach (HudStyle style in Enum.GetValues(typeof(HudStyle)))
        {
            if (style == HudStyle.None) continue;
            var all = new HudData { Style = style, Palette = style is HudStyle.ActiveIr or HudStyle.Intensifier ? "greenHot" : "whiteHot", Heading = 359.7f, Turret = 45, Elevation = 3, Magnification = 4, Narrow = true, Loaded = true, Range = 850, RangeStatus = RangeStatus.Ok, Echoes = 2, TargetCentred = true, IrLamp = true, Gain = 8, Cooling = 15, Ammo = "APFSDS" };
            all.Rounds.Add(("APFSDS", 12, true)); all.Rounds.Add(("HEAT", 8, false));
            var small = new HudRaster(1);
            HudPainter.Paint(small, all, true);
            int lit = 0; for (int i = 3; i < small.Pixels.Length; i += 4) if (small.Pixels[i] > 0) lit++;
            check(lit > 200 && small.Texts.Count > 0, "style paints " + style);
            check(small.Texts.Count(t => t.Text == "COOLING 00:15") == 1, "one COOLING line for " + style);
        }
        // Eyepiece: black outside the circle, clear in the middle, ring in the ink colour.
        var eye = new HudRaster(1);
        HudPainter.Paint(eye, new HudData { Style = HudStyle.ActiveIr, Palette = "greenHot" }, false);
        check(eye.Eyepiece && eye.Alpha(20, 240) == 255 && eye.Pixels[(240 * 640 + 20) * 4 + 1] == 0, "black outside the eyepiece");
        check(eye.Alpha(320, 300) == 0 && eye.Alpha(320, 240 - 230) == 255, "clear centre, black beyond 215");
        int ring = (240 * 640 + 320 + 214) * 4;
        check(eye.Pixels[ring + 1] > eye.Pixels[ring] + 60, "green ring");
        // Signature drives repaints: same data same value, a visible change a new value.
        var s1 = new HudData { Style = HudStyle.Gen3, Heading = 10.01f, Cooling = 14.2f };
        var s2 = new HudData { Style = HudStyle.Gen3, Heading = 10.02f, Cooling = 14.9f };
        check(s1.Signature(true) == s2.Signature(true), "sub-step changes do not repaint");
        s2.Cooling = 13.5f; check(s1.Signature(true) != s2.Signature(true), "COOLING second change repaints");
        check(s1.Signature(true) != s1.Signature(false), "reticle flag repaints");
        // Timing of one HD-screen repaint (raster 1280x960).
        var hd = new HudRaster(2); var data = new HudData { Style = HudStyle.Gen3Hd, Heading = 123, Turret = 30, Range = 1500, Magnification = 6 };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) { data.Heading = 123 + i; HudPainter.Paint(hd, data, true); }
        double ms = sw.Elapsed.TotalMilliseconds / 10;
        Console.WriteLine($"HUD raster 1280x960 (Gen 3 HD): {ms:F2} ms per repaint (at most 20 per second).");
        check(ms < 15, "HUD repaint below 15 ms");
    }

    // The HD profile after cooling shows the thermal image: vehicle columns brighter than the background.
    private static void HdAfterCooling(Action<bool, string> check, ThermalCatalog catalog)
    {
        var p = catalog.Resolve("tiGen3HD");
        var (scene, mask, heat) = Scene(p, out int w, out int h);
        var work = new SensorWorkspace(p);
        var grey = work.Process(scene, mask, 1, null, heat, cooling: true).ToArray();
        var live = work.Process(scene, mask, 2, null, heat, cooling: false);
        var (bg, hull, engine, _) = Means(live, w, h);
        var (gbg, ghull, _, _) = Means(grey, w, h);
        check(Math.Abs(gbg - ghull) < 6, "cooling frame shows no vehicle");
        check(hull > bg + 40 && engine > hull + 40, "HD live frame after cooling shows the thermal image");
    }

    // Gen 3: dark cool background < hull < engine, visible terrain texture; HD with the strongest separation.
    private static void Contrast(Action<bool, string> check, ThermalCatalog catalog)
    {
        double previousHd = 0;
        foreach (var id in new[] { "tiGen3", "tiGen3Uncooled", "tiGen3HD" })
        {
            var p = catalog.Resolve(id);
            var (scene, mask, heat) = Scene(p, out int w, out int h);
            var output = new SensorWorkspace(p).Process(scene, mask, 3, null, heat);
            var (bg, hull, engine, texture) = Means(output, w, h);
            double sky = 0; for (int y = 0; y < h / 5; y++) for (int x = 0; x < w; x++) sky += output[(y * w + x) * 4];
            sky /= w * (h / 5);
            bool blackHot = p.Palette == "blackHot";
            if (blackHot) { bg = 255 - bg; hull = 255 - hull; engine = 255 - engine; sky = 255 - sky; }
            Console.WriteLine($"{id}: background {bg:F0}, sky {sky:F0}, hull {hull:F0}, engine {engine:F0}, terrain texture sd {texture:F1} (0..255{(blackHot ? ", inverted for black-hot" : "")}).");
            check(sky < hull, id + " bright sky stays below the vehicle hull");
            check(bg < 120, id + " dark, cool background");
            check(hull > bg + 35 && engine > hull + 60, id + " background < hull < engine clearly separated");
            // 0.5.4: the uncooled Gen 3 has 128 gray levels and a softer blur, so its texture is a little flatter.
            check(texture > (id == "tiGen3Uncooled" ? 6 : 8), id + " terrain texture visible");
            if (id == "tiGen3HD") check(hull - bg > previousHd, "HD has the strongest hull separation");
            else previousHd = Math.Max(previousHd, hull - bg);
        }
        // The 0.5.0 values (catalogue defaults) were the washed-out image of the report.
        var old = new ThermalProfile { ComponentId = "old", DisplayName = "old", Width = 640, Height = 480, Noise = .003f, Blur = .1f };
        var (s, m, ht) = Scene(old, out int ow, out int oh);
        var (obg, ohull, _, _) = Means(new SensorWorkspace(old).Process(s, m, 3, null, ht), ow, oh);
        Console.WriteLine($"0.5.0 Gen 3 values (catalogue defaults): background {obg:F0}, hull {ohull:F0}.");
        check(obg > 100 && ohull - obg < 40, "0.5.0 defaults reproduce the flat light-grey image");
        // 0.5.4: the catalogue values keep the background mid-dark and the vehicle separated.
        foreach (var id in new[] { "tiGen1", "tiGen1Late", "tiGen2", "tiGen3", "tiGen3Uncooled", "tiGen3HD" })
        {
            var q = catalog.Resolve(id); var (qs, qm, qh) = Scene(q, out int qw, out int qh2);
            var (qbg, qhull, _, _) = Means(new SensorWorkspace(q).Process(qs, qm, 3, null, qh), qw, qh2);
            if (q.Palette == "blackHot") { qbg = 255 - qbg; qhull = 255 - qhull; }
            check(qbg >= 15 && qbg <= 115 && qhull > qbg + 25, id + " 0.5.4 background mid-dark, hull separated");
        }
    }

    // Terrain with texture (luma 0.25..0.6), sky in the top fifth (saturated), a vehicle with an engine spot.
    private static (byte[] Scene, byte[] Mask, HeatField Heat) Scene(ThermalProfile p, out int w, out int h)
    {
        w = p.Width; h = p.Height; int n = w * h;
        var scene = new byte[n * 4]; var mask = new byte[n * 4]; var heat = new HeatField(w, h) { HullContrast = .1f };
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            float luma = y < h / 5 ? 1f : .25f + .35f * ((((x / 6) * 7 + (y / 6) * 13) % 17) / 16f);
            byte b = (byte)(luma * 255);
            scene[i * 4] = scene[i * 4 + 1] = scene[i * 4 + 2] = b; scene[i * 4 + 3] = 255;
            if (InHull(x, y, w, h)) { mask[i * 4] = mask[i * 4 + 1] = mask[i * 4 + 2] = 255; if (InEngine(x, y, w, h)) heat.Add[i] = .5f; }
        }
        return (scene, mask, heat);
    }
    private static bool InHull(int x, int y, int w, int h) => x > w * 2 / 5 && x < w * 3 / 5 && y > h / 2 && y < h * 7 / 10;
    private static bool InEngine(int x, int y, int w, int h) => x > w * 52 / 100 && x < w * 57 / 100 && y > h * 55 / 100 && y < h * 65 / 100;
    private static (double Background, double Hull, double Engine, double TextureSd) Means(byte[] image, int w, int h)
    {
        double bg = 0, hull = 0, engine = 0; int nb = 0, nh = 0, ne = 0; var terrain = new List<double>();
        for (int y = h / 5; y < h; y++) for (int x = 0; x < w; x++)
        {
            double v = image[(y * w + x) * 4];
            if (InEngine(x, y, w, h)) { engine += v; ne++; }
            else if (InHull(x, y, w, h)) { hull += v; nh++; }
            else if (y > h * 3 / 4) { bg += v; nb++; terrain.Add(v); }
        }
        double mean = terrain.Average();
        return (bg / nb, hull / nh, engine / ne, Math.Sqrt(terrain.Average(v => (v - mean) * (v - mean))));
    }
    private static bool Near(float value, float expected) => Math.Abs(value - expected) < .01f;
}
