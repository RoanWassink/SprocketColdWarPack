using V = System.Numerics.Vector3;

namespace SprocketThermalSight.Tests;

// 0.4.3: projection with a real camera set-up (Unity matrices) and the per-frame shot counter.
internal static class HeatFixChecks
{
    internal static void Run(Action<bool, string> check)
    {
        bool Near(float a, float b, float tol) => Math.Abs(a - b) <= tol;
        var s = new HeatSettings();
        const int W = 400, H = 300;

        // --- Projection: camera at (10, 2, 5) looking along +z, 8 deg vertical FOV, 400x300 sensor (4:3).
        var pos = new V(10, 2, 5); var fwd = new V(0, 0, 1); var up = V.UnitY;
        var cam = SpotCamera.Perspective(pos, fwd, up, 8, 4 / 3f, .1f, 5000);
        check(Near(cam.Right.X, 1, 1e-5f) && Near(cam.Up.Y, 1, 1e-5f), "Unity left-handed axes: right = +x for forward +z");
        var ahead = pos + fwd * 500;
        var r = HeatProjection.Evaluate(cam, ahead, 1, .5f, false, ahead, s.FarSideWeight, s.MaxRangeM, W, H, false, false, out var spot, out var vx, out var vy, out var depth);
        check(r == SpotReject.None, "hot spot straight ahead at 500 m is in view");
        check(Near(vx, .5f, 1e-4f) && Near(vy, .5f, 1e-4f) && Near(depth, 500, .01f), "viewport centre and depth 500 m");
        check(Near(spot.X, W / 2f - .5f, .05f) && Near(spot.Y, H / 2f - .5f, .05f), "lands in the middle of the 400x300 image");
        float expectRy = 1f * H / (2 * 500 * MathF.Tan(4 * MathF.PI / 180));
        check(Near(spot.Ry, expectRy, .01f * expectRy) && Near(spot.Rx, expectRy, .01f * expectRy), "pixel radius from the projected radius (square pixels at 4:3)");
        check(HeatProjection.TryProject(.5f, .5f, 500, 1, MathF.Tan(4 * MathF.PI / 180), 4 / 3f, W, H, .5f, false, false, out var legacy)
            && Near(legacy.Rx, spot.Rx, .01f * expectRy) && Near(legacy.Ry, spot.Ry, .01f * expectRy), "matrix projection agrees with the FOV formula for a plain camera");

        r = HeatProjection.Evaluate(cam, pos - fwd * 500, 1, .5f, false, pos, s.FarSideWeight, s.MaxRangeM, W, H, false, false, out _, out _, out _, out _);
        check(r == SpotReject.BehindCamera, "spot behind the camera rejected (behind-camera)");
        r = HeatProjection.Evaluate(cam, pos + fwd * .01f, .5f, .5f, false, pos, s.FarSideWeight, s.MaxRangeM, W, H, false, false, out _, out _, out _, out _);
        check(r == SpotReject.BehindCamera, "spot at the camera plane rejected");
        r = HeatProjection.Evaluate(cam, ahead + V.UnitX * 200, 1, .5f, false, ahead, s.FarSideWeight, s.MaxRangeM, W, H, false, false, out _, out _, out _, out _);
        check(r == SpotReject.OffScreen, "spot 200 m to the side at 500 m is off-screen");
        r = HeatProjection.Evaluate(cam, pos + fwd * 4500, 1, .5f, false, pos, s.FarSideWeight, s.MaxRangeM, W, H, false, false, out _, out _, out _, out _);
        check(r == SpotReject.OutOfRange, "spot beyond MaxRangeM is out-of-range");

        // Y direction: row 0 of the readback is the image bottom (viewport y 0); above the axis = higher row.
        HeatProjection.Evaluate(cam, ahead + up * 10, 1, .5f, false, ahead, s.FarSideWeight, s.MaxRangeM, W, H, false, false, out var high, out _, out var vyHigh, out _);
        HeatProjection.Evaluate(cam, ahead + V.UnitX * 10, 1, .5f, false, ahead, s.FarSideWeight, s.MaxRangeM, W, H, false, false, out var right, out _, out _, out _);
        check(vyHigh > .5f && high.Y > spot.Y, "a point above the axis lands on a higher row (row 0 = bottom, as the mask readback)");
        check(right.X > spot.X && Near(right.Y, spot.Y, .05f), "a point to the right lands right of centre");
        float rowsPerMetre = H / (2 * 500 * MathF.Tan(4 * MathF.PI / 180));
        check(Near(high.Y - spot.Y, 10 * rowsPerMetre, .02f * 10 * rowsPerMetre), "10 m above at 500 m is the expected number of rows");
        HeatProjection.Evaluate(cam, ahead + up * 10, 1, .5f, false, ahead, s.FarSideWeight, s.MaxRangeM, W, H, false, true, out var flipped, out _, out _, out _);
        check(Near(flipped.Y, H - 1 - high.Y, .05f), "InvertSpotY mirrors the row");

        // Rotated camera: matrix result equals an independent angle computation.
        float yaw = 30 * MathF.PI / 180, pitch = -5 * MathF.PI / 180;
        var f2 = new V(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        var cam2 = SpotCamera.Perspective(pos, f2, up, 12, 16 / 9f, .1f, 5000);
        var target = pos + f2 * 800 + cam2.Right * 20 - cam2.Up * 7;
        cam2.ToViewport(target, out var ax, out var ay, out var ad);
        var rel = target - pos; float zc = V.Dot(rel, V.Normalize(f2)), xc = V.Dot(rel, cam2.Right), yc = V.Dot(rel, cam2.Up);
        float t12 = MathF.Tan(6 * MathF.PI / 180);
        check(Near(ad, zc, .05f) && Near(ax, .5f + xc / zc / (2 * t12 * 16 / 9f), 1e-4f) && Near(ay, .5f + yc / zc / (2 * t12), 1e-4f), "rotated camera: matrix projection equals the pinhole formula");

        // Far side: the user's case, a tank at 300 m facing the camera with its engine and exhausts at the rear.
        var tank = pos + fwd * 300; var engine = tank + fwd * 2.5f; var exL = tank + fwd * 3 + V.UnitX * .8f; var exR = tank + fwd * 3 - V.UnitX * .8f;
        int Visible(float farSide)
        {
            int n = 0;
            foreach (var (w, radius, amp) in new[] { (engine, .68f, .7f), (exL, .35f, 2.9f), (exR, .35f, 2.9f) })
                if (HeatProjection.Evaluate(cam, w, radius, amp, true, tank, farSide, s.MaxRangeM, W, H, false, false, out _, out _, out _, out _) == SpotReject.None) n++;
            return n;
        }
        check(Visible(0) == 0, "0.4.2 rule reproduces the log: front-facing tank gives spots in view 0/3");
        check(Visible(s.FarSideWeight) == 3, "0.4.3 default: front-facing tank gives 3/3 spots in view");
        r = HeatProjection.Evaluate(cam, engine, .68f, .7f, true, tank, 0, s.MaxRangeM, W, H, false, false, out _, out _, out _, out _);
        check(r == SpotReject.FarSide, "far side with weight 0 is reported as far-side");
        HeatProjection.Evaluate(cam, engine, .68f, .7f, true, tank, s.FarSideWeight, s.MaxRangeM, W, H, false, false, out var far, out _, out _, out _);
        HeatProjection.Evaluate(cam, tank - fwd * 2.5f, .68f, .7f, true, tank, s.FarSideWeight, s.MaxRangeM, W, H, false, false, out var nearSide, out _, out _, out _);
        check(far.Amp > 0 && Near(far.Amp / nearSide.Amp, s.FarSideWeight, .02f), "far-side spot keeps FarSideWeight of its amplitude");
        check(Near(HeatProjection.Facing(0, 0, -3, 0, 0, 500, .35f), .35f, 1e-6f) && Near(HeatProjection.Facing(0, 0, -3, 0, 0, -500, .35f), 1, 1e-6f), "Facing floor and full weight");
        new HeatSettings { FarSideWeight = 0 }.Validate(); new HeatSettings { FarSideWeight = 1 }.Validate(); check(true, "FarSideWeight 0 and 1 accepted");
        bool bad = false; try { new HeatSettings { FarSideWeight = 1.5f }.Validate(); } catch (InvalidDataException) { bad = true; }
        check(bad, "FarSideWeight above 1 rejected");

        // Vehicle pixels under a spot (diagnostics).
        var mask = new byte[W * H * 4];
        for (int y = 140; y < 160; y++) for (int x = 190; x < 210; x++) mask[(y * W + x) * 4] = 255;
        check(HeatProjection.MaskPixels(mask, W, H, spot) > 0, "centre spot covers vehicle pixels");
        check(HeatProjection.MaskPixels(mask, W, H, new HeatSpot { X = 20, Y = 20, Rx = 3, Ry = 3, Amp = 1 }) == 0, "spot over the background has no vehicle pixels");
        check(HeatProjection.MaskPixels(new byte[4], W, H, spot) == 0, "short mask buffer is safe");

        // --- Shot counter: one per shot, observed every frame.
        const int L = ShotCounter.Loaded, Ld = ShotCounter.Loading, U = ShotCounter.Unloaded;
        var c = new ShotCounter();
        check(c.Observe(12.5f, L) == 0, "first observation is the baseline (no phantom shot)");
        check(c.Observe(12.5f, L) == 0, "no change, no shot");
        check(c.Observe(20f, Ld) == 1 && c.Total == 1, "one shot: time change and Loaded->Loading on the same frame count once");
        check(c.Observe(20f, Ld) == 0 && c.Observe(20f, L) == 0, "reloading is not a shot");
        check(c.Observe(30f, L) == 1 && c.Observe(30f, Ld) == 0 && c.Total == 2, "second shot: time first, state drop a frame later counts once");
        c.Observe(30f, L);
        check(c.Observe(30f, U) == 1 && c.Observe(41f, U) == 0 && c.Total == 3, "third shot: state drop first, late time change counts once");
        var auto = new ShotCounter(); auto.Observe(0, L);
        int burst = 0; for (int i = 1; i <= 3; i++) burst += auto.Observe(i * .1f, L);
        check(burst == 3, "automatic cannon staying Loaded: every time change is a shot");
        var dropOnly = new ShotCounter(); dropOnly.Observe(5, L);
        check(dropOnly.Observe(5, Ld) == 1 && dropOnly.Observe(5, L) == 0 && dropOnly.Observe(5, Ld) == 1, "drop-only signal: one shot per load cycle");
        var nan = new ShotCounter(); nan.Observe(float.NaN, 3);
        check(nan.Observe(float.NaN, 3) == 0, "NaN fire time and NeverLoaded state stay quiet");

        // Shots within one poll: counted per frame, consumed together by the heat step.
        var g = new ShotCounter(); g.Observe(0, L); int pending = 0;
        pending += g.Observe(1, Ld); pending += g.Observe(1, L); pending += g.Observe(2, Ld); // two shots between two 5 Hz polls
        var heat = new VehicleHeat(); var off = new HeatInputs(false, 0, 0, false, 0); heat.Initialize(off, s);
        heat.Step(0, off with { Shots = pending }, s);
        check(pending == 2 && heat.TotalShots == 2 && Near(heat.BarrelK, 2 * s.BarrelShotRiseK, 1e-4f), "two shots within one poll both heat the barrel");
        float barrelC = s.AmbientC + heat.BarrelK;
        check(barrelC > s.AmbientC, "barrel reads above 15 C after shots");

        // First sighting keeps shots counted before it (0.4.2 initialised with Shots = 0: 'barrel 15 C after 0 shots').
        var first = new VehicleHeat(); first.Initialize(off with { Shots = 1 }, s);
        check(first.TotalShots == 1 && Near(first.BarrelK, s.BarrelShotRiseK, 1e-4f), "shot before the first sighting is kept");
        var old = new VehicleHeat(); old.Initialize((off with { Shots = 1 }) with { Shots = 0 }, s);
        check(old.TotalShots == 0 && old.BarrelK == 0, "0.4.2 path reproduces the log line (0 shots, 15 C)");
        // One shot per poll across polls.
        var p = new VehicleHeat(); p.Initialize(off, s); var one = new ShotCounter(); one.Observe(0, L);
        p.Step(.2f, off with { Shots = one.Observe(3, Ld) }, s); one.Observe(3, L);
        p.Step(.2f, off with { Shots = one.Observe(9, Ld) }, s);
        check(p.TotalShots == 2 && p.BarrelK > 1.9f * s.BarrelShotRiseK, "one shot in each of two polls adds up");
    }
}
