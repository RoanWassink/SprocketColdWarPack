using SprocketThermalSight;

namespace SprocketThermalSight.Tests;

// 0.5.0: HUD helpers, cooldown timer, new Gen 3 profiles and the HD processing path.
internal static class HudChecks
{
    internal static void Run(Action<bool, string> check, Action<Action, string> reject, ThermalCatalog catalog)
    {
        // Heading rounding and wrap.
        check(HudMath.FormatHeading(359.6f) == "000", "359.6 rounds to 000");
        check(HudMath.FormatHeading(359.4f) == "359", "359.4 stays 359");
        check(HudMath.FormatHeading(-0.4f) == "000" && HudMath.FormatHeading(-1f) == "359", "negative heading wraps");
        check(HudMath.FormatHeading(720.2f) == "000" && HudMath.FormatHeading(null) == "---", "multi-turn and missing heading");
        check(HudMath.Heading(0, 1) == 0 && Near(HudMath.Heading(1, 0), 90) && Near(HudMath.Heading(0, -1), 180) && Near(HudMath.Heading(-1, 0), 270), "compass from world direction");
        check(HudMath.Heading(0, 0) == null && HudMath.Heading(float.NaN, 1) == null, "degenerate direction has no heading");
        // Turret relative to hull.
        check(Near(HudMath.Relative(10, 350), 20) && Near(HudMath.Relative(350, 10), 340) && HudMath.Relative(null, 5) == null, "relative heading wraps");
        check(Near(HudMath.RelativeBearing(1, 0, 0, 0, 0, 1, 0, 1, 0), 90), "turret right of hull = 090");
        check(Near(HudMath.RelativeBearing(-1, 0, 0, 0, 0, 1, 0, 1, 0), 270), "turret left of hull = 270");
        check(Near(HudMath.RelativeBearing(0, 0, -1, 0, 0, 1, 0, 1, 0), 180) && Near(HudMath.RelativeBearing(0, 0, 1, 0, 0, 1, 0, 1, 0), 0), "rear and front");
        // Hull pitched 20 degrees nose-up: turret straight ahead along the hull remains 000.
        float c = MathF.Cos(20 * MathF.PI / 180), s = MathF.Sin(20 * MathF.PI / 180);
        check(Near(HudMath.RelativeBearing(0, s, c, 0, s, c, 0, c, -s), 0), "pitched hull keeps 000");
        check(HudMath.RelativeBearing(0, 1, 0, 0, 0, 1, 0, 1, 0) == null, "vertical optic has no bearing");
        check(Near(HudMath.Elevation(0, s, c, 0, 1, 0), 20) && Near(HudMath.Elevation(0, -s, c, 0, 1, 0), -20), "gun elevation against hull plane");
        check(HudMath.FormatSigned(3.24f) == "+3.2°" && HudMath.FormatSigned(-5f) == "-5.0°" && HudMath.FormatSigned(null) == "----", "signed elevation text");
        // Range text.
        check(HudMath.FormatRange(1238) == "1 238" && HudMath.FormatRange(1237.6) == "1 238", "range with thousands space");
        check(HudMath.FormatRange(850) == "850" && HudMath.FormatRange(1000) == "1 000" && HudMath.FormatRange(4000) == "4 000", "range below and at thousands");
        check(HudMath.FormatRange(null) == "----" && HudMath.FormatRange(double.NaN) == "----" && HudMath.FormatRange(-1) == "----", "no reading is ----");
        check(HudMath.FormatRange4(850) == "0850" && HudMath.FormatRange4(1240) == "1240" && HudMath.FormatRange4(null) == "----" && HudMath.FormatRange4(12000) == "----", "four-digit range");
        check(RangefinderPolicy.Rounded(1238) == 1240 && RangefinderPolicy.Readout(1238, 50) == "1240 m", "HUD value equals the 10 m text readout");
        // Echoes: one object of several colliders is one echo; terrain behind it a second.
        check(HudMath.Echoes(new double[] { 1238, 1240.5, 1244 }, 50, 4000) == 1, "one target, one echo");
        check(HudMath.Echoes(new double[] { 1238, 1241, 1900 }, 50, 4000) == 2, "target plus ground behind");
        check(HudMath.Echoes(new double[] { 20, 5000, double.NaN }, 50, 4000) == 0, "out-of-window returns ignored");
        // Layout per resolution: centred 4:3 design frame.
        var hd = HudLayout.For(1920, 1080);
        check(Near(hd.Scale, 2.25f) && Near(hd.OriginX, 240) && Near(hd.OriginY, 0), "1920x1080 layout");
        check(Near(hd.X(320), 960) && Near(hd.Y(240), 540), "design centre is screen centre");
        var small = HudLayout.For(1280, 720); check(Near(small.Scale, 1.5f) && Near(small.OriginX, 160), "1280x720 layout");
        var uw = HudLayout.For(3440, 1440); check(Near(uw.Scale, 3) && Near(uw.OriginX, 760), "ultrawide layout");
        var sq = HudLayout.For(1280, 1024); check(Near(sq.Scale, 2) && Near(sq.OriginX, 0) && Near(sq.OriginY, 32), "5:4 fits by width");
        var four = HudLayout.For(2560, 1440); check(Near(four.Scale, 3) && Near(four.X(320), 1280), "1440p layout");
        check(HudLayout.For(0, 1080).Scale == 0, "no screen, no layout");
        // Zoom.
        check(Near(HudMath.Magnification(60), 1) && HudMath.Magnification(5) > 13.1f && HudMath.Magnification(5) < 13.35f, "magnification against 60 degree reference");
        check(HudMath.Magnification(0) == null && HudMath.Magnification(float.NaN) == null, "invalid FOV");
        check(HudMath.Narrow(80, 20, 100) == true && HudMath.Narrow(30, 20, 100) == false && HudMath.Narrow(50, 50, 50) == null, "NFOV/WFOV from zoom range; fixed optic unknown");
        check(HudMath.GainFraction(.25f) == 0 && Near(HudMath.GainFraction(64), 1) && HudMath.GainFraction(4) > .4f && HudMath.GainFraction(4) < .6f, "II gain bar");
        // Style per profile.
        check(HudMath.StyleFor("irActiveL1", "activeIR", 320) == HudStyle.ActiveIr && HudMath.StyleFor("iiGen2", "imageIntensifier", 480) == HudStyle.Intensifier, "IR/II styles");
        check(HudMath.StyleFor("tiGen1", "thermal", 240) == HudStyle.Gen1 && HudMath.StyleFor("tiGen1Late", "thermal", 400) == HudStyle.Gen1Late && HudMath.StyleFor("tiGen2", "thermal", 640) == HudStyle.Gen2, "Gen 1/2 styles");
        check(HudMath.StyleFor("tiGen3", "thermal", 640) == HudStyle.Gen3 && HudMath.StyleFor("tiGen3Uncooled", "thermal", 640) == HudStyle.Gen3Uncooled && HudMath.StyleFor("tiGen3HD", "thermal", 1280) == HudStyle.Gen3Hd, "Gen 3 styles");
        check(HudMath.Polarity("blackHot") == "BH" && HudMath.Polarity("whiteHot") == "WH", "polarity");
        // Cooldown timer.
        var t = new CooldownTimer();
        check(!t.Active(5) && t.Remaining(5) == 0, "idle timer");
        t.Start(100, 20);
        check(t.Active(100) && Near(t.Remaining(106), 14) && CooldownTimer.Format(t.Remaining(106)) == "00:14", "counting down");
        check(CooldownTimer.Format(t.Remaining(119.9f)) == "00:01" && !t.Active(120) && CooldownTimer.Format(t.Remaining(130)) == "00:00", "ends at 20 s");
        check(CooldownTimer.Format(75) == "01:15" && CooldownTimer.Format(20) == "00:20" && CooldownTimer.Format(float.NaN) == "00:00", "mm:ss");
        t.Start(0, 0); check(!t.Active(0), "zero seconds = instant");
        t.Start(0, 20); t.Reset(); check(!t.Active(1), "reset on switch-off");
        // New profiles and their date gate.
        check(catalog.Models.Count == 9, "0.5.4: nine standardised profiles");
        var g2 = catalog.Resolve("tiGen2"); var g3 = catalog.Resolve("tiGen3"); var g3u = catalog.Resolve("tiGen3Uncooled"); var g3hd = catalog.Resolve("tiGen3HD");
        check(g3.DisplayName == "Thermal Gen 3" && g3.AvailableFrom == "2004.01.01" && g3.Width == 640 && g3.Height == 480 && g3.RefreshHz == 50 && Near(g3.Noise, .006f) && Near(g3.Blur, .15f) && g3.Palette == "whiteHot" && g3.Cooled, "Gen 3 profile");
        check(g3u.DisplayName == "Thermal Gen 3 (uncooled)" && g3u.AvailableFrom == "2010.01.01" && g3u.RefreshHz == 30 && Near(g3u.Noise, .018f) && Near(g3u.Blur, .3f) && g3u.Palette == "whiteHot" && !g3u.Cooled, "uncooled profile (0.5.4: white-hot default, polarity is a switch)");
        check(g3hd.DisplayName == "Thermal Gen 3 HD" && g3hd.AvailableFrom == "2015.01.01" && g3hd.Width == 1280 && g3hd.Height == 1024 && g3hd.RefreshHz == 60 && Near(g3hd.Noise, .003f) && Near(g3hd.Blur, .1f) && g3hd.Cooled && g3hd.Sharpen > 0, "HD profile");
        check(g3.ExtraMassKg >= g2.ExtraMassKg && g3.ExtraAssemblyCost > g2.ExtraAssemblyCost && g3hd.ExtraAssemblyCost > g3.ExtraAssemblyCost && g3hd.ExtraMassKg >= g3.ExtraMassKg, "cooled line ascending from Gen 2");
        check(g3u.ExtraMassKg < g3.ExtraMassKg && g3u.ExtraAssemblyCost < g3.ExtraAssemblyCost, "uncooled lighter and cheaper than Gen 3");
        check(!catalog.Models.Where(m => m.ComponentId is not ("tiGen3" or "tiGen3HD")).Any(m => m.Cooled) && catalog.Models.All(m => m.ComponentId is "tiGen3" or "tiGen3HD" || m.Sharpen == 0), "only the cooled profiles use cooling and sharpen");
        var eras = new[] { new DateTime(1900, 1, 1), new DateTime(1945, 9, 3), new DateTime(1992, 1, 1) };
        foreach (var (p, date) in new[] { (g3, new DateTime(2004, 1, 1)), (g3u, new DateTime(2010, 1, 1)), (g3hd, new DateTime(2015, 1, 1)) })
        {
            check(!ThermalEraPolicy.Allows(date.AddDays(-1), eras, new DateTime(1945, 9, 3), p.AvailableDate), "date gate closed the day before " + p.ComponentId);
            check(ThermalEraPolicy.Allows(date, eras, new DateTime(1945, 9, 3), p.AvailableDate), "date gate open on " + p.ComponentId);
        }
        ThermalProfile? Pick(int year) => ProfileSelectionPolicy.Default(catalog.Models, catalog.DefaultProfileId, m => m.AvailableDate.Year <= year);
        check(Pick(2020)?.ComponentId == "tiGen1", "configured default unchanged by new profiles");
        check(ProfileSelectionPolicy.Default(catalog.Models.Where(m => m.ComponentId != "tiGen1"), "tiGen1", m => m.AvailableDate.Year <= 2016)?.ComponentId == "tiGen3HD", "newest-fallback reaches HD");
        // Resolution bounds: HD is allowed, larger is still refused.
        var big = new ThermalProfile { ComponentId = "big", DisplayName = "big", Width = 1280, Height = 1024 };
        big.Validate();
        big.Width = 1281; reject(big.Validate, "wider than 1280 refused");
        big.Width = 1280; big.Height = 1025; reject(big.Validate, "taller than 1024 refused");
        big.Height = 1024; big.Sharpen = 1.5f; reject(big.Validate, "sharpen outside 0..1 refused");

        // Processing: sharpen, cooling and the parallel HD path.
        var p0 = new ThermalProfile { ComponentId = "s", DisplayName = "s", Width = 80, Height = 60, Noise = 0, Blur = 0, Contrast = 1, Gamma = 1, GrayLevels = 256 };
        int n = p0.Width * p0.Height; var scene = new byte[n * 4]; var mask = new byte[n * 4];
        for (int i = 0; i < n; i++) { scene[i * 4 + 3] = 255; if (i % p0.Width >= 40) mask[i * 4] = mask[i * 4 + 1] = mask[i * 4 + 2] = 255; }
        var plain = SensorProcessor.Process(scene, mask, p0, 1);
        p0.Sharpen = .5f; var sharp = SensorProcessor.Process(scene, mask, p0, 1);
        check(sharp[39 * 4] < plain[39 * 4] && sharp[40 * 4] > plain[40 * 4], "sharpen raises edge contrast");
        check(sharp[10 * 4] == plain[10 * 4] && sharp[70 * 4] == plain[70 * 4], "sharpen leaves flat areas alone");
        p0.Sharpen = 0;
        var work = new SensorWorkspace(p0);
        var grey = work.Process(scene, mask, 3, cooling: true).ToArray();
        check(Enumerable.Range(0, n).All(i => grey[i * 4] == grey[i * 4 + 1] && grey[i * 4 + 1] == grey[i * 4 + 2] && grey[i * 4 + 3] == 255), "cooling image is grey");
        var greyValues = Enumerable.Range(0, n).Select(i => (int)grey[i * 4]).ToArray();
        check(greyValues.Max() - greyValues.Min() > 40 && Math.Abs(greyValues.Average() - 115) < 15, "cooling image is noisy mid-grey");
        check(!grey.SequenceEqual(work.Process(scene, mask, 3)), "live image differs after cooling");
        p0.Palette = "blackHot"; var greyBlack = new SensorWorkspace(p0).Process(scene, mask, 3, cooling: true);
        check(greyBlack.SequenceEqual(grey), "cooling ignores palette");
        var hdp = new ThermalProfile { ComponentId = "hd", DisplayName = "hd", Width = 1280, Height = 1024, Noise = .003f, Blur = .1f, Sharpen = .35f, Contrast = 1.4f, GrayLevels = 256 };
        int hn = hdp.Width * hdp.Height; var hs = new byte[hn * 4]; var hm = new byte[hn * 4];
        for (int i = 0; i < hn; i++) { hs[i * 4] = hs[i * 4 + 1] = hs[i * 4 + 2] = (byte)(i % 251); if (i % hdp.Width > 600 && i % hdp.Width < 700) hm[i * 4] = 255; }
        var hw = new SensorWorkspace(hdp);
        var a1 = hw.Process(hs, hm, 9).ToArray(); var a2 = hw.Process(hs, hm, 9).ToArray();
        check(a1.SequenceEqual(a2), "parallel HD path is repeatable");
        check(!a1.SequenceEqual(hw.Process(hs, hm, 10)), "parallel HD noise changes per frame");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 5; i++) hw.Process(hs, hm, (uint)i);
        double ms = sw.Elapsed.TotalMilliseconds / 5;
        Console.WriteLine($"HD 1280x1024 process: {ms:F1} ms per frame ({Environment.ProcessorCount} logical CPUs, parallel rows).");
        check(ms < 40, "HD processing stays below 40 ms per captured frame");
    }
    private static bool Near(float? value, float expected) => value is { } v && Math.Abs(v - expected) < .01f;
    private static bool Near(double value, double expected) => Math.Abs(value - expected) < .01;
}
