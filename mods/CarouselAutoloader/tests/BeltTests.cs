using System.Text.Json;
using SprocketCarouselAutoloader;

internal static class BeltTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var calibre in new[] { 8d, 13, 20, 57 }) check(Belt.Fits(calibre, 57), $"Belt fits {calibre} mm without a lower limit");
        check(!Belt.Fits(58, 57) && Belt.Fits(580, 0), "Belt upper limit and zero means unlimited");
        check(!Belt.Fits(double.NaN, 0) && !Belt.Fits(0, 0), "invalid physical diameter rejected");
        check(Belt.SetTable(Belt.DefaultTable), "default rpm table valid");
        foreach (var (calibre, rpm) in new[] { (9d, 750d), (10d, 550d), (23d, 900d), (30d, 200d), (57d, 120d), (60d, 120d) })
            check(Belt.Rpm(calibre) == rpm && Math.Abs(Belt.Cycle(calibre) - 60 / rpm) < 1e-12, $"Belt table boundary {calibre}");
        check(Belt.SetTable(" 9:1500, 57:5000 "), "valid configurable rpm table");
        Belt.MaxRpm = 5000;
        check(Belt.Rpm(8) == 1200 && Belt.Rpm(57) == 1200, "hard 1200 ceiling even with MaxRpm 5000");
        Belt.MaxRpm = 600;
        check(Belt.Rpm(8) == 600, "configured lower ceiling");
        Belt.MaxRpm = 1200;
        foreach (var invalid in new[] { "", "bad", "9:0", "9:-1", "NaN:2", "9:Infinity", "9:7,9:8", "15:7,9:8", "0:4", "9:7,", "9:7:8" })
        {
            check(!Belt.SetTable(invalid), "invalid table reported once to config caller");
            check(Belt.Rpm(9) == 750 && Belt.Rpm(10) == 550 && Belt.Rpm(60) == 120, "invalid table restores complete default");
        }
        check(Belt.MechanismMassKg(0) == 25 && Belt.MechanismMassKg(288) == 53.8, "Belt mass: 25 + 0.1 per capacity slot");
        check(CarouselDatePolicy.For(LoaderKind.Belt) == new DateTime(1965, 1, 1) &&
            CarouselDatePolicy.For(LoaderKind.Belt) != CarouselDatePolicy.BustleDate, "explicit 1965 Belt date, never bustle 1952");
        check(!CarouselDatePolicy.Allows(new DateTime(1964, 12, 31), CarouselDatePolicy.For(LoaderKind.Belt)) &&
            CarouselDatePolicy.Allows(new DateTime(1965, 1, 1), CarouselDatePolicy.For(LoaderKind.Belt)), "Belt date boundary");
        check(LoaderIdentity.Kind(Belt.PartGuid) == LoaderKind.Belt && Guid.TryParse(Belt.PartGuid, out _), "Belt GUID resolves to Belt");
        var root = FindRoot();
        var part = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Parts", "sprocketBeltAutoloaderPart.json"))).RootElement;
        check(part.GetProperty("guid").GetString() == Belt.PartGuid && part.GetProperty("name").GetString() == "Belt feed box" &&
            part.GetProperty("date").GetString() == "1965.01.01" && part.GetProperty("components")[0].GetProperty("type").GetString() == "ammoRack",
            "Belt part identity, date, native rack damage carrier");
        foreach (bool enabled in new[] { false, true })
        foreach (bool two in new[] { false, true })
        foreach (int cannon in new[] { -1, 17, 900 })
        {
            var fields = new Dictionary<string, object> { ["sprocketBustleEnabledV1"] = enabled, ["sprocketBustleCannonV1"] = cannon,
                ["sprocketBustleMirrorFeedArmV1"] = false, ["sprocketBustleTwoPieceV1"] = two };
            var loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(fields))!;
            bool loadedTwo = Belt.TwoPiece(loaded["sprocketBustleTwoPieceV1"].GetBoolean());
            check(loaded["sprocketBustleEnabledV1"].GetBoolean() == enabled && loaded["sprocketBustleCannonV1"].GetInt32() == cannon && !loadedTwo,
                "existing save fields round-trip; Belt ignores TwoPiece");
            fields.Remove("sprocketBustleTwoPieceV1");
            check(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(fields))!.Count == 3 && !Belt.TwoPiece(loadedTwo),
                "normalized Belt saves no extra field and TwoPiece stays off");
        }
        Refill(check);
        Cycles(check);
        Allocation(check);
        Hud(check);
        foreach (var (status, expected) in new[] {
            (Belt.Status(true, 1, 2, false, false, false), "vuurt"), (Belt.Status(false, 2, 2, false, false, false), "vol"),
            (Belt.Status(false, 1, 2, true, true, true), "bijvullen"), (Belt.Status(false, 1, 2, false, false, true), "rijdt"),
            (Belt.Status(false, 0, 2, false, true, false), "geen lader"), (Belt.Status(false, 0, 2, false, true, true), "leeg") })
            check(status == expected, "Belt HUD status " + expected);
        var runtime = File.ReadAllText(Path.Combine(root, "BustleRuntime.cs"));
        var adapter = File.ReadAllText(Path.Combine(root, "BeltRuntime.cs"));
        check(runtime.Contains("rack.VehicleObject?.GUID == Belt.PartGuid") && runtime.Contains("Kind = LoaderIdentity.Kind("), "native part whitelist and kind wiring");
        check(runtime.Contains("!Semi && !BeltFeed") && runtime.Contains("state.BeltFeed ? Belt.TwoPiece("), "runtime suppresses Belt two-piece on load and use");
        check(!adapter.Contains("OrderBy") && !adapter.Contains(".ToList(") && !adapter.Contains("TakeShell("), "Belt source path has no LINQ lists, sort or extra shell debit");
        check(!File.ReadAllText(Path.Combine(root, "BustleVisuals.cs")).Contains("__instance.VehicleObject?.GUID == Belt.PartGuid) return;") &&
            File.ReadAllText(Path.Combine(root, "AutoloaderVisuals.cs")).Contains("or Belt.PartGuid"), "Belt custom renderer uses the existing visuals path");
        Console.WriteLine("Belt fits/rpm/mass/date/GUID/save normalization/refill/conservation/HUD/source guards PASS; native save/reload remains live-only.");
    }

    internal static void Hud(Action<bool, string> check)
    {
        var card = new HudCard();
        card.UpdateBelt(22, 42, 200, "bijvullen");
        check(card.Heading == "Belt 22/42" && card.IsBelt && card.Shown && !card.TwoPiece &&
            !card.ShowCycle && !card.ShowRefill, "Belt heading and persistent visibility");
        check(card.BeltRate == "200 rpm", "Belt small rpm line");
        check(Math.Abs(card.BeltFraction - 22f / 42) < 1e-6, "Belt stock bar fraction");
        foreach (var (status, colour) in new[] { ("vuurt", "#8fd99c"), ("vol", "#8fd99c"),
            ("bijvullen", "#f3cf86"), ("leeg", "#ffcbb3"), ("rijdt", "#ffcbb3"), ("geen lader", "#ffcbb3") })
        {
            card.UpdateBelt(22, 42, 200, status);
            check(card.BeltStatus == status && card.BeltStatusColour == colour, "Belt status and colour: " + status);
        }
        foreach (var (stock, slots, fraction) in new[] { (0, 42, 0f), (42, 42, 1f), (99, 42, 1f), (-1, 42, 0f), (0, 0, 0f) })
        {
            card.UpdateBelt(stock, slots, 200, "leeg");
            check(card.BeltFraction == fraction, "Belt bar empty/full/clamped/zero capacity");
        }
        card.UpdateBelt(999, 999, 200, "geen lader");
        // Conservative simple estimate: Section 15 px font, 0.6 em = 9 px per uppercase character.
        // Include both 8 px row gaps around Spacer; hidden two-piece chip takes no space.
        float used = (card.Heading.Length + card.BeltStatus.Length) * 9f + 2 * 8;
        float margin = AutoloaderHudLayout.Width - 2 * AutoloaderHudLayout.Pad - used;
        check(card.Heading == "Belt 999/999" && margin == 66, "longest Belt header fits in 300 px with 66 px estimated margin");
        Console.WriteLine($"Belt heading width estimate: {used} px in 280 px content, margin {margin} px (9 px/character).");
        foreach (bool rings in new[] { false, true })
        {
            check(AutoloaderHudLayout.CardHeight(card.Shape, rings) == 44, "Belt card height includes rpm and bar in either mode");
            check(AutoloaderHudLayout.Height(new[] { card.Shape }, rings) == 64, "Belt block height includes padding");
            var spot = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, default, true, "BottomRight", 20, 300, 8, 64);
            check(spot.Y + 64 + 8 == 300, "Belt block top stays exactly below AMMO");
        }
        string heading = card.Heading, rate = card.BeltRate;
        for (int i = 0; i < 10000; i++) card.UpdateBelt(999, 999, 200, "geen lader");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++)
        {
            card.UpdateBelt(999, 999, 200, "geen lader");
            _ = card.Heading; _ = card.BeltStatus; _ = card.BeltStatusColour;
            _ = card.BeltRate; _ = card.BeltFraction; _ = card.Shape;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        check(bytes == 0 && ReferenceEquals(heading, card.Heading) && ReferenceEquals(rate, card.BeltRate),
            "unchanged Belt samples and per-frame getters allocate zero bytes");
        Console.WriteLine($"Belt HUD allocation: 100000 unchanged updates/getter reads, {bytes} bytes.");
        foreach (bool revolver in new[] { false, true })
        {
            card.Update(revolver, true, 5, 18, true, false, .5, 2, RefillReason.None, 0, double.NaN, 0, 5);
            check(!card.IsBelt && !card.Shape.Belt && card.Heading == HudText.Title(revolver, false, 5, 18) && card.TwoPiece,
                "reused bustle/revolver card hides Belt elements and retains original text");
            check(AutoloaderHudLayout.CardHeight(card.Shape, false) == 44 &&
                AutoloaderHudLayout.CardHeight(card.Shape, true) == 62, "bustle/revolver heights unchanged opposite Belt 44 px");
            card.UpdateBelt(999, 999, 200, "geen lader");
        }
        card.Reset();
        check(!card.Shown, "reset hides Belt card");
    }

    static void Refill(Action<bool, string> check)
    {
        double progress = 0, still = 0;
        int reserve = 10, stock = 0, capacity = 3;
        bool Tick(bool crew, double speed, double dt)
        {
            still = Belt.Still(still, speed, dt);
            bool ready = Belt.AdvanceRefill(ref progress, crew, Belt.Stationary(still), stock >= capacity, reserve > 0, dt, 1);
            if (ready && reserve > 0 && stock < capacity) { reserve--; stock++; }
            return ready;
        }
        for (int i = 0; i < 50; i++) Tick(false, 0, .1);
        check(stock == 0 && reserve == 10 && progress == 0, "Belt cannot refill without loader");
        for (int i = 0; i < 50; i++) Tick(true, 1, .1);
        check(stock == 0 && reserve == 10 && progress == 0 && still == 0, "driving forbids refill");
        Tick(true, 0, 2.9);
        check(stock == 0 && progress == 0 && !Belt.Stationary(still), "before three stationary seconds no refill");
        Tick(true, 0, .2);
        check(stock == 0 && progress > 0 && Belt.Stationary(still), "after three stationary seconds crew begins");
        Tick(true, 0, .3);
        check(progress > .49 && stock == 0, "partial round takes no reserve yet");
        Tick(true, 2, .1);
        check(progress == 0 && stock == 0 && reserve == 10, "motion loses partial work without losing ammunition");
        for (int i = 0; i < 100; i++) { Tick(true, 0, .1); check(stock + reserve == 10, "finite reserve plus box conserved"); }
        check(stock == capacity && reserve == 7 && progress == 0, "full box stops refilling");
        for (int i = 0; i < 100; i++) Tick(true, 0, .1);
        check(stock == capacity && reserve == 7 && progress == 0, "full box remains unchanged");
        stock = 0; reserve = 0;
        for (int i = 0; i < 100; i++) Tick(true, 0, .1);
        check(stock == 0 && reserve == 0, "empty reserves cannot create rounds");
        Belt.RefillSecondsPerRound = 0;
        check(Belt.RefillTime(1) == .5 && Belt.RefillTime(.5) == 1, "refill floor .5 seconds divided by efficiency");
        Belt.RefillSecondsPerRound = 1;
        check(Belt.RefillTime(.5) == 2 && Belt.RefillTime(0) == 1, "idle assigned loader counts, working efficiency scales time");
        check(Belt.Still(3, .5, .1) == 0 && Belt.Still(3, double.NaN, .1) == 0, "speed boundary and unknown speed reset stillness");
    }

    // Simulated native Update: one exclusive phase per call; float progress and >=, no overshoot carry.
    // One fired/reload request per render-frame boundary; unused substeps cannot fire another round.
    static (double Rpm, int Calls) Simulate(int fps, double rpm, bool subdivide, int rounds = 20000)
    {
        double frame = (double)(float)(1d / fps), cycle = 60 / rpm;
        int steps = subdivide ? BeltCycleClock.Steps(cycle, frame) : 1;
        float h = (float)(frame / steps);
        var clock = new BeltCycleClock();
        double time = 0;
        int calls = 0;
        for (int round = 0; round < rounds; round++)
        {
            int phase = 0; float prepare = 0, pickup = 0, travel = 0, ram = 0;
            clock.Begin(cycle, h, frame);
            while (phase < 4)
            {
                for (int k = 0; k < steps; k++)
                {
                    float work = clock.RamWork(ram, h);
                    // Mirrors native branch shape, not the implementation's scheduling formula.
                    switch (phase)
                    {
                        case 0: prepare += h; if (prepare >= 0) phase = 1; break;
                        case 1: pickup += h; if (pickup >= 0) phase = 2; break;
                        case 2: travel += h; if (travel >= 0) phase = 3; break;
                        case 3: ram += h; if (ram >= work) phase = 4; break;
                    }
                    calls++; clock.Step(h, phase == 4);
                    if (phase == 4) { clock.FinishFrame((steps - k - 1) * (double)h); break; }
                }
                time += frame;
            }
        }
        return (rounds * 60 / time, calls);
    }

    static void Cycles(Action<bool, string> check)
    {
        Console.WriteLine("BELT CYCLE SIMULATION (native phases only, frame-boundary firing; no Unity/EB/input simulation)");
        Console.WriteLine("fps | target rpm | achieved rpm | native calls/frame (substeps) | error % | rpm without substeps");
        foreach (int fps in new[] { 30, 60, 144 })
        {
            foreach (double target in new[] { 120d, 200, 550, 900, 1200 })
            {
                var result = Simulate(fps, target, true);
                var single = Simulate(fps, target, false);
                int steps = BeltCycleClock.Steps(60 / target, (float)(1d / fps));
                double error = Math.Abs(result.Rpm / target - 1);
                check(error <= .1, $"Belt cycle within 10% at {fps} fps / {target} rpm");
                check(60 / target < 4d / fps + 1e-6 || steps == 1, "no substeps for cycles achievable with one call per frame");
                Console.WriteLine(FormattableString.Invariant($"{fps} | {target:0} | {result.Rpm:0.000} | {steps} ({steps - 1} extra) | {error * 100:0.000} | {single.Rpm:0.000}"));
            }
            Console.WriteLine($"{fps} fps: maximum without substeps {fps * 60 / 4} rpm; 1200/min requires {BeltCycleClock.Steps(.05, (float)(1d / fps))} native calls/frame.");
        }
        check(BeltCycleClock.Steps(.05, 1d / 30) == 4 && BeltCycleClock.Steps(.05, 1d / 60) == 2 && BeltCycleClock.Steps(.05, 1d / 144) == 1,
            "minimal substeps account for frame-boundary firing");
        check(BeltCycleClock.Steps(.05, 0) == 1 && BeltCycleClock.Steps(.05, double.NaN) == 1, "invalid frame time never substeps");
    }

    sealed class Source { public int Stock; }
    static double Sink;
    static void Allocation(Action<bool, string> check)
    {
        var source = new Source { Stock = 1 };
        var cache = new BeltSourceCache<Source>();
        cache.Store((IntPtr)123, 7, source);
        check(cache.TryGet((IntPtr)123, 7, out var found) && ReferenceEquals(found, source) &&
            !cache.TryGet((IntPtr)124, 7, out _) && !cache.TryGet((IntPtr)123, 8, out _), "single source cache keys owner and type");
        var clock = new BeltCycleClock();
        double progress = 0;
        void Round()
        {
            if (!cache.TryGet((IntPtr)123, 7, out var s)) throw new Exception();
            s!.Stock = 1; s.Stock--; // native storage is not invoked by a pure benchmark
            double cycle = Belt.Cycle(23), frame = 1d / 60;
            clock.Begin(cycle, frame, frame);
            float ram = 0;
            for (int phase = 0; phase < 4; phase++) { float work = clock.RamWork(ram, frame); if (phase == 3) ram += work; clock.Step(frame, phase == 3); }
            Belt.AdvanceRefill(ref progress, true, true, false, true, frame, 1);
            Sink += cycle + s.Stock;
        }
        for (int i = 0; i < 10000; i++) Round();
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int rounds = 100000;
        for (int i = 0; i < rounds; i++) Round();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        check(bytes == 0, "steady managed Belt policy/cache/refill path allocates zero bytes per round");
        Console.WriteLine($"Belt allocation microbenchmark: {rounds} rounds; {bytes} bytes; {bytes / (double)rounds:0.000} bytes/round, 0 managed allocation events/round (zero bytes). Scope: shared pure policy, clock, single-source cache and refill policy. Native wrappers, inherited hooks and EB require a live profiler.");
    }
    static string FindRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SprocketCarouselAutoloader.csproj"))) return dir.FullName;
        throw new InvalidOperationException("project root missing");
    }
}
