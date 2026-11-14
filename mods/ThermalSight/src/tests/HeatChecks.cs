using System.Diagnostics;

namespace SprocketThermalSight.Tests;

// 0.4.0 heat model: pure checks for warming/cooling, shots, wreck curve, limits, config
// validation, projection, the mask-gated sum and a CPU measurement of the extra step.
internal static class HeatChecks
{
    internal static void Run(Action<bool, string> check, string perfPath)
    {
        var s = new HeatSettings();
        bool Near(float a, float b, float tol) => Math.Abs(a - b) <= tol;
        void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } check(rejected, name); }

        // Warm-up: first-order towards the idle/full target with the warm time constant.
        var h = new VehicleHeat(); var off = new HeatInputs(false, 0, 0, false, 0); var idle = new HeatInputs(true, 0, 0, false, 0);
        h.Initialize(off, s);
        check(h.EngineK == 0 && h.ExhaustK == 0 && h.BarrelK == 0 && h.WreckK == 0, "cold start at ambient");
        h.Step(s.EngineWarmTauS, idle, s);
        check(Near(h.EngineK, s.EngineIdleRiseK * (1 - MathF.Exp(-1)), .01f), "engine reaches 63 % of idle rise after one warm tau");
        for (int i = 0; i < 100; i++) h.Step(60, idle, s);
        check(Near(h.EngineK, s.EngineIdleRiseK, .01f) && Near(h.ExhaustK, s.ExhaustIdleRiseK, .01f), "idle steady state");
        var full = new HeatInputs(true, 1, 0, false, 0);
        for (int i = 0; i < 100; i++) h.Step(60, full, s);
        check(Near(h.EngineK, s.EngineFullRiseK, .01f) && Near(h.ExhaustK, s.ExhaustFullRiseK, .01f), "full-load steady state is hotter");
        // Cool-down uses the slower cool time constant.
        float hot = h.EngineK; h.Step(s.EngineCoolTauS, off, s);
        check(Near(h.EngineK, hot * MathF.Exp(-1), .01f), "engine cools with its cool tau after shutdown");
        check(h.ExhaustK < h.EngineK, "exhaust cools faster than the engine deck");
        var split = new VehicleHeat(); split.Initialize(full, s); var single = new VehicleHeat(); single.Initialize(full, s);
        for (int i = 0; i < 10; i++) split.Step(30, off, s);
        single.Step(300, off, s);
        check(Near(split.EngineK, single.EngineK, .01f) && Near(split.ExhaustK, single.ExhaustK, .01f), "exact exponential: step size does not change the curve");
        check(Near(VehicleHeat.EngineLoad(.2f, 1600, 600, 2600), .5f, 1e-4f) && VehicleHeat.EngineLoad(-1, 0, 600, 2600) == 1
            && VehicleHeat.EngineLoad(float.NaN, float.NaN, 0, 0) == 0, "engine load from throttle/rpm with guards");
        var starter = new VehicleHeat(); starter.Initialize(new HeatInputs(true, .5f, 0, false, 0), s);
        check(starter.EngineK > s.EngineIdleRiseK && starter.EngineK < s.EngineFullRiseK, "first sighting starts at the steady state of current inputs");

        // Shots add up, cap, then cool within a few minutes.
        var b = new VehicleHeat(); b.Initialize(off, s);
        b.Step(0, off with { Shots = 1 }, s); check(Near(b.BarrelK, s.BarrelShotRiseK, 1e-4f), "one shot heats the barrel");
        b.Step(0, off with { Shots = 3 }, s); check(Near(b.BarrelK, 4 * s.BarrelShotRiseK, 1e-4f) && b.TotalShots == 4, "shots accumulate");
        b.Step(0, off with { Shots = 1000 }, s); check(b.BarrelK == s.BarrelMaxRiseK, "barrel heat is capped");
        b.Step(0, off with { Shots = -5 }, s); check(b.BarrelK == s.BarrelMaxRiseK, "negative shot counts ignored");
        b.Step(3 * s.BarrelCoolTauS, off, s); check(b.BarrelK < .06f * s.BarrelMaxRiseK, "barrel ~95 % cooled after 3 tau (a few minutes)");

        // Wreck: hot at knock-out, hotter while burning, ~10 minutes to just above ambient.
        var w = new VehicleHeat(); w.Initialize(off, s);
        var dead = new HeatInputs(false, 0, 0, true, 0);
        w.Step(1, dead, s); check(w.WreckK > .98f * s.WreckStartRiseK, "knock-out starts hot");
        var burning = dead with { Fire = 1 };
        for (int i = 0; i < 60; i++) w.Step(5, burning, s);
        check(Near(w.WreckK, s.WreckFireRiseK, 1), "burning wreck approaches fire temperature");
        var half = new VehicleHeat(); half.Initialize(off, s); for (int i = 0; i < 60; i++) half.Step(5, dead with { Fire = .5f }, s);
        check(half.WreckK < w.WreckK && half.WreckK > s.WreckStartRiseK * .5f, "fire intensity scales the burn");
        float burnt = w.WreckK; w.Step(s.WreckCoolSeconds, dead, s);
        check(w.WreckK < .06f * burnt + s.WreckResidualK && w.WreckK >= s.WreckResidualK, "wreck cools in ~WreckCoolSeconds to just above ambient");
        w.Step(36000, dead, s); check(Near(w.WreckK, s.WreckResidualK, .01f), "cold wreck keeps residual rise");
        var revived = new VehicleHeat(); revived.Initialize(dead, s); check(revived.WreckK == s.WreckStartRiseK, "first sighting of a wreck assumes it is fresh");
        var liveFire = new VehicleHeat(); liveFire.Initialize(off, s); for (int i = 0; i < 60; i++) liveFire.Step(5, off with { Fire = 1 }, s);
        check(liveFire.WreckK > s.WreckFireRiseK * .9f, "a burning live vehicle heats too");
        for (int i = 0; i < 60; i++) liveFire.Step(60, off, s); check(liveFire.WreckK < 1, "an extinguished live vehicle cools to ambient");

        // Limits and robustness.
        var g = new VehicleHeat(); g.Initialize(full, s);
        g.Step(float.NaN, full, s); check(float.IsFinite(g.EngineK) && Near(g.EngineK, s.EngineFullRiseK, .01f), "NaN dt is a zero step");
        g.Step(-50, off, s); check(Near(g.EngineK, s.EngineFullRiseK, .01f), "negative dt is a zero step");
        g.Step(1e9f, off, s); check(g.EngineK >= 0 && g.EngineK < 1e-3f, "huge dt clamps and converges");
        g.Step(1, new HeatInputs(true, float.PositiveInfinity, 0, false, float.NaN), s); check(float.IsFinite(g.EngineK + g.WreckK), "non-finite inputs are clamped");
        var uninit = new VehicleHeat(); uninit.Step(10, full, s); check(uninit.Initialized && Near(uninit.EngineK, s.EngineFullRiseK, .01f), "step before initialise initialises");

        // Config validation: defaults valid; out-of-range or non-finite values rejected.
        new HeatSettings().Validate(); check(true, "defaults validate");
        Reject(() => new HeatSettings { PollHz = 11 }.Validate(), "poll rate above 10 Hz rejected");
        Reject(() => new HeatSettings { PollHz = 0 }.Validate(), "zero poll rate rejected");
        Reject(() => new HeatSettings { EngineCoolTauS = 0 }.Validate(), "zero time constant rejected");
        Reject(() => new HeatSettings { FullScaleKelvin = float.NaN }.Validate(), "NaN full scale rejected");
        Reject(() => new HeatSettings { HullContrast = 1.5f }.Validate(), "hull contrast above 1 rejected");
        Reject(() => new HeatSettings { WreckScale = -1 }.Validate(), "negative scale rejected");
        Reject(() => new HeatSettings { WreckCoolSeconds = 5 }.Validate(), "too short wreck cooling rejected");
        Reject(() => new HeatSettings { AmbientC = 80 }.Validate(), "ambient outside -60..60 rejected");
        new HeatSettings { EngineScale = 0, BarrelScale = 10, WreckFireRiseK = 2000, PollHz = 10, MaxRangeM = 10000 }.Validate(); check(true, "edge values accepted");

        // Projection: a spot in view lands on the grid, behind the camera or off-grid it does not.
        const int W = 640, H = 480; float tan3 = MathF.Tan(3 * MathF.PI / 180);
        check(HeatProjection.TryProject(.5f, .5f, 1000, 1, tan3, 4 / 3f, W, H, 1, false, false, out var spot), "centre spot projects");
        check(Near(spot.X, 319.5f, 1e-3f) && Near(spot.Y, 239.5f, 1e-3f), "centre maps to the grid centre");
        check(Near(spot.Ry, 1 * H / (2 * 1000 * tan3), 1e-3f) && Near(spot.Rx, spot.Ry, 1e-3f), "radius from range and field of view");
        check(HeatProjection.TryProject(.5f, .5f, 500, 1, tan3, 16 / 9f, W, H, 1, false, false, out var wide) && wide.Rx < wide.Ry, "non-square sensor pixels give an ellipse");
        check(Near(wide.Ry, 2 * spot.Ry, 1e-3f), "half the range doubles the radius");
        check(!HeatProjection.TryProject(.5f, .5f, -10, 1, tan3, 1, W, H, 1, false, false, out _), "spot behind the camera rejected");
        check(!HeatProjection.TryProject(.5f, .5f, 0, 1, tan3, 1, W, H, 1, false, false, out _), "spot at the camera plane rejected");
        check(!HeatProjection.TryProject(1.5f, .5f, 1000, 1, tan3, 1, W, H, 1, false, false, out _), "spot off the grid rejected");
        check(HeatProjection.TryProject(1.001f, .5f, 100, 1, tan3, 1, W, H, 1, false, false, out _), "spot overlapping the edge kept");
        check(HeatProjection.TryProject(.25f, .25f, 1000, 1, tan3, 1, W, H, 1, false, true, out var flipped) && Near(flipped.Y, .75f * H - .5f, 1e-3f), "InvertSpotY flips rows");
        check(HeatProjection.TryProject(.5f, .5f, 3000, .2f, tan3, 1, W, H, 1, false, false, out var tiny) && tiny.Rx == 1 && tiny.Amp < 1, "sub-pixel spot keeps one pixel with reduced amplitude");
        check(!HeatProjection.TryProject(.5f, .5f, 1000, 1, tan3, 1, W, H, 0, false, false, out _), "zero amplitude rejected");
        check(!HeatProjection.TryProject(float.NaN, .5f, 1000, 1, tan3, 1, W, H, 1, false, false, out _), "NaN viewport rejected");
        // See-through approximation: camera side 1, far side 0, centre spot always visible.
        check(HeatProjection.Facing(0, 0, -3, 0, 0, -500) == 1, "rear engine seen from behind counts");
        check(HeatProjection.Facing(0, 0, -3, 0, 0, 500) == 0, "rear engine seen from the front (wrong side) dropped");
        float sideOn = HeatProjection.Facing(0, 0, -3, 500, 0, 0); check(sideOn > 0 && sideOn < 1, "side view is partial");
        check(HeatProjection.Facing(0, 0, .1f, 0, 0, 500) == 1, "spot at the vehicle centre always counts");

        // Sum on the mask: spots only change vehicle pixels.
        var p = new ThermalProfile { Width = 80, Height = 60, Noise = 0, Blur = 0, GrayLevels = 256, Contrast = 1, Gamma = 1 };
        int n = p.Width * p.Height; var scene = new byte[n * 4]; var mask = new byte[n * 4];
        for (int i = 0; i < n; i++) { scene[i * 4] = scene[i * 4 + 1] = scene[i * 4 + 2] = 90; scene[i * 4 + 3] = 255; }
        for (int y = 20; y < 40; y++) for (int x = 20; x < 60; x++) { int k = (y * p.Width + x) * 4; mask[k] = mask[k + 1] = mask[k + 2] = 255; }
        var work = new SensorWorkspace(p); var field = new HeatField(p.Width, p.Height);
        var cold = (byte[])work.Process(scene, mask, 1, null, field).Clone();
        field.AddSpot(new HeatSpot { X = 30, Y = 30, Rx = 25, Ry = 25, Amp = .6f });
        var warm = work.Process(scene, mask, 1, null, field);
        int bg = (5 * p.Width + 30) * 4, inside = (30 * p.Width + 30) * 4, nearEdge = (30 * p.Width + 15) * 4;
        check(warm[bg] == cold[bg] && warm[nearEdge] == cold[nearEdge], "spot does not change background pixels inside its radius");
        check(warm[inside] > cold[inside], "spot heats vehicle pixels");
        check(cold[inside] > cold[bg], "cold hull stays just above its surroundings");
        int seen = 0; for (int i = 0; i < n; i++) if (warm[i * 4] != cold[i * 4]) { seen++; check(mask[i * 4] > 0, "changed pixel is a vehicle pixel"); }
        check(seen > 0, "spot changed some pixels");
        field.Clear(); check(field.Add.All(v => v == 0) && field.Spots == 0, "field clears");
        field.AddSpot(new HeatSpot { X = 40, Y = 30, Rx = 10, Ry = 10, Amp = 1, Plateau = true });
        check(field.Add[30 * p.Width + 40] == 1 && field.Add[30 * p.Width + 47] == 1 && field.Add[30 * p.Width + 49] < 1, "plateau spot is flat inside 80 %");
        field.Clear(); field.AddSpot(new HeatSpot { X = 40, Y = 30, Rx = 10, Ry = 10, Amp = 1 });
        check(field.Add[30 * p.Width + 40] == 1 && field.Add[30 * p.Width + 45] < 1 && field.Add[30 * p.Width + 50] == 0, "soft spot falls off to zero at its radius");
        field.Clear(); field.AddSpot(new HeatSpot { X = -100, Y = -100, Rx = 5, Ry = 5, Amp = 1 }); check(field.Add.All(v => v == 0), "off-grid spot ignored safely");
        field.AddSpot(new HeatSpot { X = 40, Y = 30, Rx = 1e6f, Ry = 1e6f, Amp = 1 }); check(field.Add[0] > 0, "huge spot is clipped to the grid");
        var legacy = new SensorWorkspace(p).Process(scene, mask, 1);
        var legacyAgain = new SensorWorkspace(p).Process(scene, mask, 1, null, null);
        check(legacy.SequenceEqual(legacyAgain), "no heat field keeps 0.3.x output");
        bool sizeRejected = false; try { work.Process(scene, mask, 1, null, new HeatField(10, 10)); } catch (ArgumentException) { sizeRejected = true; }
        check(sizeRejected, "wrong heat field size rejected");

        // CPU cost of the extra step at the largest shipped profile (640x480), 8 vehicles / 24 spots.
        var big = new ThermalProfile { Width = 640, Height = 480 };
        int bn = big.Width * big.Height; var bscene = new byte[bn * 4]; var bmask = new byte[bn * 4]; var rnd = new Random(4);
        rnd.NextBytes(bscene); for (int i = 0; i < bn; i++) if ((i / big.Width) % 3 == 0) bmask[i * 4] = 255;
        var bwork = new SensorWorkspace(big); var bfield = new HeatField(big.Width, big.Height);
        var spots = new HeatSpot[24]; float tan = MathF.Tan(4 * MathF.PI / 180);
        for (int v = 0; v < 8; v++)
        {
            float vx = .1f + v * .11f, z = 500 + v * 125;
            HeatProjection.TryProject(vx, .45f, z, 1.0f, tan, 16 / 9f, big.Width, big.Height, .8f, false, false, out spots[v * 3]);       // engine
            HeatProjection.TryProject(vx + .01f, .44f, z, .35f, tan, 16 / 9f, big.Width, big.Height, 3f, false, false, out spots[v * 3 + 1]); // exhaust
            HeatProjection.TryProject(vx - .02f, .5f, z, v == 0 ? 4f : .3f, tan, 16 / 9f, big.Width, big.Height, .5f, v == 0, false, out spots[v * 3 + 2]); // barrel or wreck
        }
        double Measure(Action a, int reps) { a(); var times = new double[reps]; for (int r = 0; r < reps; r++) { var t = Stopwatch.GetTimestamp(); a(); times[r] = (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency; } Array.Sort(times); return times[reps / 2]; }
        void Raster() { bfield.Clear(); foreach (var sp in spots) bfield.AddSpot(sp); }
        double raster = Measure(Raster, 300);
        double plain = Measure(() => bwork.Process(bscene, bmask, 1), 60);
        double heated = Measure(() => { Raster(); bwork.Process(bscene, bmask, 1, null, bfield); }, 60);
        // Worst case: 24 large close-range spots (radius 150 px).
        var close = Enumerable.Range(0, 24).Select(i => new HeatSpot { X = 40 + i * 25, Y = 240, Rx = 150, Ry = 150, Amp = .5f }).ToArray();
        double worst = Measure(() => { bfield.Clear(); foreach (var sp in close) bfield.AddSpot(sp); }, 100);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5; i++) { Raster(); bwork.Process(bscene, bmask, (uint)i, null, bfield); }
        check(GC.GetAllocatedBytesForCurrentThread() == before, "heat step allocates nothing per frame");
        check(raster < 4, "24-spot raster below 4 ms");
        string report = $"Heat 0.4.0 CPU measurement ({Environment.ProcessorCount} logical CPUs, Release, median)\n" +
            $"Profile 640x480, 8 vehicles, 24 hot spots at 500-1375 m (4 deg vertical FOV):\n" +
            $"  clear + raster 24 spots: {raster:F3} ms\n" +
            $"  Process without heat: {plain:F3} ms; with heat (raster + sum): {heated:F3} ms; extra: {heated - plain:F3} ms per captured frame\n" +
            $"Worst case 24 close spots of radius 150 px: clear + raster {worst:F3} ms\n";
        File.WriteAllText(perfPath, report);
        Console.Write(report);
    }
}
