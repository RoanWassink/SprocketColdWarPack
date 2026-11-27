using SprocketCarouselAutoloader;
int count = 0;
void Check(bool condition, string label) { count++; if (!condition) throw new Exception(label); }
// Focused HUD runner: does not execute unrelated model/refill suites.
if (args.Contains("--hud"))
{
    HudLayoutTests.Run(Check);
    BeltTests.Hud(Check);
    Console.WriteLine($"HUD ALL PASS: {count} assertions.");
    return;
}
// Per-mechanism dates from ERA-MAP. Day-before, on-date and sentinel are the
// same contract the runtime/UI uses; saved state is never deleted by the gate.
Check(!CarouselDatePolicy.Allows(new DateTime(1951, 12, 31), CarouselDatePolicy.BustleDate), "Bustle unavailable one day early");
Check(CarouselDatePolicy.Allows(new DateTime(1952, 1, 1), CarouselDatePolicy.BustleDate), "Bustle available on 1952.01.01");
Check(!CarouselDatePolicy.Allows(new DateTime(1952, 12, 31), CarouselDatePolicy.SemiAutoloaderDate), "Semi unavailable one day early");
Check(CarouselDatePolicy.Allows(new DateTime(1953, 1, 1), CarouselDatePolicy.SemiAutoloaderDate), "Semi available on 1953.01.01");
Check(!CarouselDatePolicy.Allows(new DateTime(1966, 12, 31), CarouselDatePolicy.T64CarouselDate), "T-64 carousel unavailable one day early");
Check(CarouselDatePolicy.Allows(new DateTime(1967, 1, 1), CarouselDatePolicy.T64CarouselDate), "T-64 carousel available on 1967.01.01");
Check(!CarouselDatePolicy.Allows(new DateTime(1972, 12, 31), CarouselDatePolicy.T72CarouselDate), "T-72 carousel unavailable one day early");
Check(CarouselDatePolicy.Allows(new DateTime(1973, 1, 1), CarouselDatePolicy.T72CarouselDate), "T-72 carousel available on 1973.01.01");
Check(CarouselDatePolicy.ForLayout(CarouselLayout.VerticalCharge) == CarouselDatePolicy.T64CarouselDate, "vertical layout uses T-64 date");
Check(CarouselDatePolicy.ForLayout(CarouselLayout.HorizontalCassette) == CarouselDatePolicy.T72CarouselDate, "horizontal layout uses T-72 date");
Check(!CarouselDatePolicy.Allows(new DateTime(1979, 12, 31), CarouselDatePolicy.StackedCarouselDate), "2S38 stacked carousel unavailable one day early");
Check(CarouselDatePolicy.Allows(new DateTime(1980, 1, 1), CarouselDatePolicy.StackedCarouselDate), "2S38 stacked carousel available on 1980.01.01");
Check(CarouselDatePolicy.ForLayout(CarouselLayout.StackedUnitary) == CarouselDatePolicy.StackedCarouselDate, "stacked unitary layout uses 1980 date");
foreach (var kind in new[] { CarouselDatePolicy.BustleDate, CarouselDatePolicy.SemiAutoloaderDate,
             CarouselDatePolicy.T64CarouselDate, CarouselDatePolicy.T72CarouselDate, CarouselDatePolicy.StackedCarouselDate })
    Check(CarouselDatePolicy.Allows(new DateTime(9999, 12, 31), kind), "final-era sentinel makes every loader date available");
Check(!CarouselDatePolicy.Allows(null, CarouselDatePolicy.BustleDate), "missing vehicle date fails closed");
var savedLayout = CarouselLayout.VerticalCharge;
Check(!CarouselDatePolicy.Allows(new DateTime(1960, 1, 1), CarouselDatePolicy.ForLayout(savedLayout)) && savedLayout == CarouselLayout.VerticalCharge,
    "too-early saved layout keeps its identity and stays inactive");
// Revolver drum: pure limits, cycle, stationary refill, mass and drum readout.
// 0.2.24 (BESLUITEN "Instelbaar, ruimer"): defaults 155 mm and no fixed length limit; the old fixed
// 105 mm / 1100 mm tests now run with those values set in the config properties.
Check(RevolverPolicy.MaxCaliberMm == 155 && RevolverPolicy.MaxRoundLengthMm == 0, "Revolver default limits 155 mm, no length limit");
Check(RevolverPolicy.Fits(155, 5000) && RevolverPolicy.Fits(100, 1860) && !RevolverPolicy.Fits(155.1, 500), "Revolver default: calibre limit only");
Check(RevolverPolicy.Refusal(100, 1860, 2000, 6, false) == null, "A round that fits the drum is never refused by a length limit when MaxRoundLengthMm = 0");
RevolverPolicy.MaxCaliberMm = 105; RevolverPolicy.MaxRoundLengthMm = 1100;
Check(RevolverPolicy.Fits(105, 1100), "Revolver accepts a 105 mm / 1100 mm round exactly on a configured limit");
Check(RevolverPolicy.Fits(105, 1099.9) && RevolverPolicy.Fits(104.9, 1100), "Revolver accepts rounds below either configured limit");
Check(!RevolverPolicy.Fits(105.1, 1100) && !RevolverPolicy.Fits(105, 1100.1), "Revolver rejects 0.1 mm over either configured limit");
Check(RevolverPolicy.Refusal(100, 1860, 2000, 6, false) == "Round 1860 mm above the revolver limit 1100 mm (config Revolver.MaxRoundLengthMm)" &&
      RevolverPolicy.Refusal(120, 900, 2000, 6, false) == "Calibre 120 mm above the revolver limit 105 mm (config Revolver.MaxCaliberMm)",
    "A configured limit that refuses names the config key and value");
RevolverPolicy.MaxCaliberMm = RevolverPolicy.DefaultMaxCaliberMm; RevolverPolicy.MaxRoundLengthMm = RevolverPolicy.DefaultMaxRoundLengthMm;
Check(RevolverPolicy.Refusal(105, 1893, 1150, 17, false) == "Rack shorter than the round (1893 mm): lengthen it or use two-piece" &&
      RevolverPolicy.Refusal(105, 1200, 1150, 17, true) == "Rack shorter than the longest piece (1200 mm): lengthen it" &&
      RevolverPolicy.Refusal(75, 870, 920, 0, false) == RevolverPolicy.TooSmallText &&
      RevolverPolicy.Refusal(75, 870, 920, 18, false) == null,
    "Revolver inspector gives exactly one reason: limit, rack length or too small");
foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0d, -1d })
{
    Check(!RevolverPolicy.Fits(invalid, 500), "Revolver rejects an invalid caliber");
    Check(!RevolverPolicy.Fits(75, invalid), "Revolver rejects an invalid round length");
}
Check(Math.Abs(RevolverPolicy.CycleSeconds(3.8) - 2.28) < 1e-12, "Revolver cycle is 0.6x the bustle cycle");
Check(RevolverPolicy.RefillSecondsPerRound(2) == 10 && Math.Abs(RevolverPolicy.RefillSecondsPerRound(10d / 3) - 10) < 1e-12,
    "Revolver refill floor is ten seconds");
Check(Math.Abs(RevolverPolicy.RefillSecondsPerRound(3.8) - 11.4) < 1e-12, "Revolver refill scales three times a slow bustle cycle");
// 0.2.25 (BESLUITEN 2026-11-02 "Ja, maak hem sneller"): the revolver fires at 0.6 x the bustle cycle; the refill is
// computed from the BUSTLE cycle (two-piece x 1.25 included), so the faster revolver factor cannot speed it up.
Check(RevolverPolicy.CycleFactor == 0.6 && RevolverPolicy.RefillCycleFactor == 3 && RevolverPolicy.MinimumRefillSeconds == 10,
    "Revolver timing defaults are 0.6 / 3 / 10 s");
Check(RevolverPolicy.DefaultCycleFactor == 0.6 && RevolverPolicy.DefaultRefillCycleFactor == 3 && RevolverPolicy.DefaultMinimumRefillSeconds == 10,
    "Revolver timing default constants");
foreach (var bustle in new[] { .06, .5, 2.0, 3.8, 6.0, 13.5, 40.0 })
{
    var revolver = RevolverPolicy.FireCycleSeconds(bustle, true, false);
    Check(RevolverPolicy.FireCycleSeconds(bustle, false, false) == bustle, "Bustle fire cycle never gets the revolver factor");
    Check(Math.Abs(revolver - 0.6 * bustle) < 1e-12 && revolver < bustle, "Revolver fire cycle is 0.6 x the bustle cycle");
    var bustleTwo = RevolverPolicy.FireCycleSeconds(bustle, false, true);
    var revolverTwo = RevolverPolicy.FireCycleSeconds(bustle, true, true);
    Check(Math.Abs(bustleTwo - 1.25 * bustle) < 1e-12 && Math.Abs(revolverTwo - 1.25 * 0.6 * bustle) < 1e-12,
        "Two-piece is x 1.25 on both the bustle and the revolver cycle");
    Check(Math.Abs(revolverTwo / bustleTwo - 0.6) < 1e-12, "A two-piece revolver is still 0.6 x the two-piece bustle");
    foreach (var two in new[] { false, true })
    {
        var expectedRefill = Math.Max(10, 3 * (two ? 1.25 * bustle : bustle));
        foreach (var factor in new[] { .1, .6, 1.5, 3, 10 })
        {
            RevolverPolicy.CycleFactor = factor;
            Check(Math.Abs(RevolverPolicy.RefillSeconds(bustle, two) - expectedRefill) < 1e-12 &&
                  Math.Abs(RevolverPolicy.RefillSecondsPerRound(RevolverPolicy.BaseCycleSeconds(bustle, two)) - expectedRefill) < 1e-12,
                "Refill per round (per pair with two-piece) is max(10 s, 3 x bustle cycle), independent of the revolver factor");
        }
        RevolverPolicy.CycleFactor = RevolverPolicy.DefaultCycleFactor;
        Check(RevolverPolicy.RefillSeconds(bustle, two) >= RevolverPolicy.FireCycleSeconds(bustle, true, two),
            "Refilling is never faster than firing at the default factors");
    }
}
RevolverPolicy.CycleFactor = 1.5; RevolverPolicy.RefillCycleFactor = 2; RevolverPolicy.MinimumRefillSeconds = 5;
Check(Math.Abs(RevolverPolicy.FireCycleSeconds(4, true, false) - 6) < 1e-12 && RevolverPolicy.RefillSeconds(1, false) == 5 &&
      Math.Abs(RevolverPolicy.RefillSeconds(4, false) - 8) < 1e-12 && Math.Abs(RevolverPolicy.RefillSeconds(4, true) - 10) < 1e-12,
    "Configured revolver timing values are used");
foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0d, -0d, -1d, double.MinValue })
{
    RevolverPolicy.CycleFactor = 1.5; RevolverPolicy.RefillCycleFactor = 2; RevolverPolicy.MinimumRefillSeconds = 5;
    RevolverPolicy.CycleFactor = invalid; RevolverPolicy.RefillCycleFactor = invalid; RevolverPolicy.MinimumRefillSeconds = invalid;
    Check(RevolverPolicy.CycleFactor == 0.6 && RevolverPolicy.RefillCycleFactor == 3 && RevolverPolicy.MinimumRefillSeconds == 10,
        "An invalid revolver timing config value (<= 0, NaN, infinite) falls back to the default");
    Check(RevolverPolicy.Positive(invalid, 7) == 7 && RevolverPolicy.Positive(.001, 7) == .001, "Positive() accepts only finite values above zero");
}
RevolverPolicy.CycleFactor = RevolverPolicy.DefaultCycleFactor; RevolverPolicy.RefillCycleFactor = RevolverPolicy.DefaultRefillCycleFactor;
RevolverPolicy.MinimumRefillSeconds = RevolverPolicy.DefaultMinimumRefillSeconds;
Check(Math.Abs(RevolverPolicy.StillSeconds(2.9, 0, .2) - 3.1) < 1e-12 && RevolverPolicy.IsStationary(3) && !RevolverPolicy.IsStationary(2.999),
    "Revolver stationary threshold is exactly three seconds");
Check(RevolverPolicy.StillSeconds(2.9, .5, .2) == 0 && RevolverPolicy.StillSeconds(2.9, 2.4, .2) == 0,
    "Revolver still timer resets at or above 0.5 m/s");
Check(Math.Abs(RevolverPolicy.StillSeconds(1.2, .49, .3) - 1.5) < 1e-12, "Revolver still timer accumulates below 0.5 m/s");
Check(RevolverPolicy.MechanismMassKg(0) == 60 && RevolverPolicy.MechanismMassKg(12) == 120 && RevolverPolicy.MechanismMassKg(22) == 170,
    "Revolver mechanism mass is 60 + 5 per round");
// 0.2.23: one drum (BESLUITEN "Revolver = één trommel"); the two-drum text test is replaced.
Check(RevolverPolicy.DrumText(18, 1, 18) == "1 drum: 18 chambers x 1 row = 18 rounds" &&
      RevolverPolicy.DrumText(6, 2, 12) == "1 drum: 6 chambers x 2 rows = 12 rounds" &&
      RevolverPolicy.DrumText(1, 1, 1) == "1 drum: 1 chamber x 1 row = 1 round" &&
      RevolverPolicy.DrumText(-3, -1, -2) == "1 drum: 0 chambers x 0 rows = 0 rounds",
    "Revolver drum text names one drum with chambers x rows");
Check(RevolverPolicy.TooSmallText == "Too small for a revolver drum", "Revolver too-small inspector text");
Check(!CarouselDatePolicy.Allows(new DateTime(1951, 12, 31), CarouselDatePolicy.RevolverDate) &&
      CarouselDatePolicy.Allows(new DateTime(1952, 1, 1), CarouselDatePolicy.RevolverDate) &&
      CarouselDatePolicy.Allows(new DateTime(9999, 12, 31), CarouselDatePolicy.RevolverDate),
    "Revolver date gate: one day early, on date and final-era sentinel");
Check(CarouselDatePolicy.For(LoaderKind.Revolver) == CarouselDatePolicy.RevolverDate &&
      CarouselDatePolicy.For(LoaderKind.Bustle) == CarouselDatePolicy.BustleDate &&
      CarouselDatePolicy.For(LoaderKind.Semi) == CarouselDatePolicy.SemiAutoloaderDate,
    "Each loader kind selects its own date");
var revolverPart = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Parts", "sprocketRevolverAutoloaderPart.json"))).RootElement;
Check(revolverPart.GetProperty("guid").GetString() == "3189ea9f-9b42-4204-977f-8cebdcf4f77c" &&
      revolverPart.GetProperty("name").GetString() == "Revolver autoloader" &&
      revolverPart.GetProperty("date").GetString() == "1952.01.01",
    "Revolver part identity and date");
Check(revolverPart.GetProperty("tags").EnumerateArray().Any(t => t.GetString() == "revolverAutoloader") &&
      !revolverPart.GetProperty("tags").EnumerateArray().Any(t => t.GetString() == "bustleAutoloader"),
    "Revolver part uses its own tag only");
CarouselSize Size(double diameter, double depth, double caliber = 125, double nativePropellant = 713, double mass = 33,
    CarouselLayout layout = CarouselLayout.HorizontalCassette) =>
    CarouselGeometry.Calculate(new[] { new Segment(diameter, depth) }, caliber, caliber * 3 + nativePropellant, mass, layout);
var az = Size(2500, 766);
var mz = Size(2500, 766, layout: CarouselLayout.VerticalCharge);
Check(az.StoredProjectileMm == 680 && az.StoredChargeMm == 408, "Reference allocation is 680 + 408");
Check(az.Capacity == 22 && mz.Capacity == 28, "Calibrated reference capacities 22 / 28");
Check(az.RequiredDepthMm == 453 && mz.RequiredDepthMm == 701, "AZ adds thicknesses; MZ adds upright charge and projectile thickness");
var longAz = Size(2716, 1195, nativePropellant: 966);
var longMz = Size(2716, 1195, nativePropellant: 966, layout: CarouselLayout.VerticalCharge);
Check(longAz.StoredProjectileMm == 933 && longAz.StoredChargeMm == 408, "1341 mm is allocated 933 + 408, not falsely 680 + 408");
Check(longAz.Capacity == 22 && longMz.Capacity == 31, "AZ retains overflow radius; MZ uses a fixed body");
Check(Size(3000, 766).Capacity > az.Capacity && Size(3000, 766, layout: CarouselLayout.VerticalCharge).Capacity > mz.Capacity,
    "Larger baskets exceed reference counts");
Check(Size(2500, 5000).Capacity == az.Capacity, "Depth creates no extra rings");
Check(Size(2500, 452).Capacity == 0 && Size(2500, 453).Capacity > 0, "Horizontal exact-depth boundary");
Check(Size(2500, 700, layout: CarouselLayout.VerticalCharge).Capacity == 0 && Size(2500, 701, layout: CarouselLayout.VerticalCharge).Capacity > 0,
    "Vertical exact-depth boundary");
Check(Size(2500, 5000, nativePropellant: 2000).Capacity == 0, "Extreme native propellant still fails diameter despite capped storage charge");
Check(Size(2500, 766, nativePropellant: 30).StoredChargeMm == 30, "Short native charge is not lengthened");
Check(Size(2500, 766, mass: 50).ReloadSeconds > az.ReloadSeconds, "Weight penalty remains");
Check(CarouselGeometry.FullRoundLength(125, 966) == 1341, "Native identity remains unchanged");
foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -1, 0 })
{
 Check(Size(invalid, 766).Capacity == 0, "Invalid diameter");
 Check(Size(2500, invalid).Capacity == 0, "Invalid depth");
 Check(Size(2500, 766, caliber: invalid).Capacity == 0, "Invalid caliber");
 Check(Size(2500, 766, mass: invalid).Capacity == 0, "Invalid mass");
}
Check(Size(2500, 766, layout: (CarouselLayout)99).Capacity == 0, "Invalid layout");
Check(CarouselGeometry.Calculate(new[] { new Segment(2500, 766) }, 125, 374, 33).Capacity == 0, "Invalid native length");
foreach (var layout in Enum.GetValues<CarouselLayout>())
for (int diameter = 600; diameter <= 4000; diameter += 100)
for (int caliber = 20; caliber <= 250; caliber += 20)
for (int propellant = 30; propellant <= 1500; propellant += 150)
{
 var value = Size(diameter, 3000, caliber, propellant, layout: layout);
 if (layout == CarouselLayout.HorizontalCassette) Check(Math.Abs(value.StoredProjectileMm + value.StoredChargeMm - (caliber * 3 + propellant)) < 1e-8, "AZ conserves native allocation");
 else if (layout == CarouselLayout.VerticalCharge) Check(Math.Abs(value.StoredProjectileMm - caliber * 680.0 / 125) < 1e-8, "MZ radial body remains fixed");
 else Check(Math.Abs(value.StoredProjectileMm - (caliber * 3 + propellant)) < 1e-8 && value.StoredChargeMm == 0, "Stacked unitary keeps complete round with zero split charge");
 if (layout == CarouselLayout.HorizontalCassette) Check(value.StoredChargeMm <= caliber * (408.0 / 125) + 1e-8, "AZ scaled charge cap");
 Check(Size(value.RequiredDiameterMm, 3000, caliber, propellant, layout: layout).Capacity > 0, "Displayed minimum diameter fits");
 Check(Size(value.RequiredDiameterMm - 1, 3000, caliber, propellant, layout: layout).Capacity == 0, "One mm below minimum fails");
 if (value.Capacity == 0) continue;
 var width = layout == CarouselLayout.StackedUnitary ? caliber * 1.18 + 8 : caliber * 1.28 + 12;
 var outer = diameter / 2.0 - 40;
 var end = Math.Sqrt(outer * outer - width * width / 4);
 var length = layout == CarouselLayout.HorizontalCassette ? Math.Max(value.StoredProjectileMm, value.StoredChargeMm) : value.StoredProjectileMm;
 var center = end - length / 2;
 var pitch = width * (layout == CarouselLayout.HorizontalCassette ? 1.4 : layout == CarouselLayout.StackedUnitary ? 1.08 : 1.12);
 var ringCount = layout == CarouselLayout.StackedUnitary ? value.SlotsPerTier : value.Capacity;
 Check(2 * center * Math.Sin(Math.PI / ringCount) + 1e-8 >= pitch, "Calibrated cassette pitch respected at mean radius");
 Check(end - length + 1e-8 >= Math.Max(180, diameter * .12), "Drive clearance respected");
 Check(value.RequiredDepthMm <= 3000, "Vertical clearance");
 var limit = Math.Floor(value.MaxChargeLengthMm);
 if (layout != CarouselLayout.StackedUnitary || limit >= 0)
 {
  Check(Size(diameter, 3000, caliber, limit, layout: layout).Capacity > 0, "Displayed maximum native propellant fits");
  Check(Size(diameter, 3000, caliber, limit + 1, layout: layout).Capacity == 0, "One mm past maximum native propellant fails");
 }
}
// 2S38-style stacked unitary carousel: no 2-piece split, vertical stacking, ~0.5 s / 120 rpm at 57 mm, open-ended calibres, max 256.
{
 var s57Shallow = Size(1600, 195, caliber: 57, nativePropellant: 348, mass: 6.5, layout: CarouselLayout.StackedUnitary);
 var s57One = Size(1600, 196, caliber: 57, nativePropellant: 348, mass: 6.5, layout: CarouselLayout.StackedUnitary);
 var s57Four = Size(1600, 450, caliber: 57, nativePropellant: 348, mass: 6.5, layout: CarouselLayout.StackedUnitary);
 var s57Deep = Size(1600, 900, caliber: 57, nativePropellant: 348, mass: 6.5, layout: CarouselLayout.StackedUnitary);
 Check(s57Shallow.Capacity == 0 && s57One.Capacity > 0 && s57One.Tiers == 1, "2S38 stacked exact 1-tier depth boundary");
 Check(s57One.StoredProjectileMm == 519 && s57One.StoredChargeMm == 0, "2S38 stores complete 57x348 round without 2-piece split");
 Check(s57Four.Tiers == 4 && s57Four.Capacity == s57Four.SlotsPerTier * 4 && s57Four.Capacity >= 140 && s57Four.Capacity <= 165,
     "2S38 1.6m x 0.45m basket stacks 4 tiers (~152 rounds)");
 Check(s57Four.ReloadSeconds >= .45 && s57Four.ReloadSeconds <= .55, "2S38 57 mm cycle calibrated around 0.5 s / 120 rpm");
 Check(s57Deep.Capacity == CarouselGeometry.MaximumStackedCapacity, "Stacked carousel caps at 256 rounds");
 var custom57 = CarouselGeometry.Calculate(new[] { new Segment(1600, 450) }, 57, 519, 6.5, CarouselLayout.StackedUnitary, 260, 259);
 Check(custom57.StoredProjectileMm == 519 && custom57.StoredChargeMm == 0 && custom57.Capacity == s57Four.Capacity,
     "Stacked unitary sums custom projectile + charge into one complete round");
 var big120 = Size(2800, 766, caliber: 120, nativePropellant: 640, mass: 35, layout: CarouselLayout.StackedUnitary);
 Check(big120.Capacity > 0 && big120.Tiers >= 3 && big120.ReloadSeconds > 1.5, "Stacked unitary is open-ended for large calibres");
 var readout = LoaderFitReadout.Carousel(s57Four, CarouselLayout.StackedUnitary);
 Check(readout.Any(l => l.Contains("(unitary)")) && readout.Any(l => l.Contains("tiers x")) && readout.Any(l => l.Contains("rpm")),
     "Stacked unitary readout shows unitary round, tiers and rpm");
 var meshes = StackedCarouselGeometry.Build(1.6, .45, s57Four.Capacity, s57Four.SlotsPerTier, .057, .519);
 var proj = meshes.First(m => m.Material == "Projectile");
 Check(proj.SlotPrefixIndexCounts.Length == s57Four.Capacity + 1 && CarouselModelGeometry.Triangles(meshes) < CarouselModelGeometry.TriangleBudget,
     "Stacked procedural mesh has exact per-slot prefixes and stays well under triangle budget");
 var maxMeshes = StackedCarouselGeometry.Build(1.6, .90, s57Deep.Capacity, s57Deep.SlotsPerTier, .057, .519);
 Check(CarouselModelGeometry.Triangles(maxMeshes) < CarouselModelGeometry.TriangleBudget,
     "256-round stacked procedural mesh stays under triangle budget");
 var fullRanges = StackedCarouselGeometry.VisibleRanges(s57Four.Capacity, s57Four.SlotsPerTier, 0, s57Four.Capacity);
 Check(fullRanges.Sum(r => r.Count) == s57Four.Capacity, "VisibleRanges covers all rounds when full");
 var minusOne = StackedCarouselGeometry.VisibleRanges(s57Four.Capacity, s57Four.SlotsPerTier, 5, s57Four.Capacity - 1);
 Check(minusOne.Sum(r => r.Count) == s57Four.Capacity - 1 && minusOne[0] == (0, s57Four.SlotsPerTier * 3),
     "VisibleRanges empties top tier first while keeping lower tiers intact");
 Check(StackedCarouselGeometry.VisibleRanges(s57Four.Capacity, s57Four.SlotsPerTier, 5, 0).Count == 0, "VisibleRanges empty at 0 stock");
}
var resolved = CarouselGeometry.ResolveBasket(new[] { new Segment(0, 606), new Segment(2657, 589) }, 2671);
var screenshot = CarouselGeometry.Calculate(resolved, 125, 1341, 45.8, CarouselLayout.VerticalCharge);
Check(screenshot.DiameterMm == 2657 && screenshot.DepthMm == 1195, "Ring and segment rules remain");
Check(CarouselGeometry.ResolveBasket(resolved, 2000).Min(s => s.DiameterMm) == 2000, "Smaller ring limits every segment");
Check(CarouselGeometry.ResolveBasket(resolved, 0).Length == 0, "Missing ring rejected");
Console.WriteLine($"PASS: {count} assertions. Reference AZ/MZ {az.Capacity}/{mz.Capacity}; 2716 mm example {longAz.Capacity}/{longMz.Capacity}, allocation {longAz.StoredProjectileMm}+{longAz.StoredChargeMm}, required depth {az.RequiredDepthMm}/{mz.RequiredDepthMm}.");

Check(longMz.StoredProjectileMm == 680 && longMz.StoredChargeMm == 661 && longMz.RequiredDepthMm == 954,
    "MZ 1341 mm native length uses fixed body and vertical overflow");
var shortMz = Size(2716, 1195, nativePropellant: 408, layout: CarouselLayout.VerticalCharge);
Check(shortMz.RequiredDiameterMm == longMz.RequiredDiameterMm && shortMz.Capacity == longMz.Capacity,
    "MZ bigger propellant does not grow diameter or lower capacity when depth suffices");
var hugeMz = Size(2716, 4000, nativePropellant: 2000, layout: CarouselLayout.VerticalCharge);
Check(hugeMz.RequiredDiameterMm == longMz.RequiredDiameterMm && hugeMz.RequiredDepthMm > longMz.RequiredDepthMm,
    "Extreme MZ propellant consumes vertical height, not radial length");
Check(Size(2716, 953, nativePropellant: 966, layout: CarouselLayout.VerticalCharge).Capacity == 0 &&
    Size(2716, 954, nativePropellant: 966, layout: CarouselLayout.VerticalCharge).Capacity > 0,
    "MZ vertical overflow boundary fits at required depth only");
Console.WriteLine($"MZ refinement PASS: {count} assertions; body {longMz.StoredProjectileMm}, charge {longMz.StoredChargeMm}, depth {longMz.RequiredDepthMm}, capacity {longMz.Capacity}.");
var baseReload = BustleTiming.ReloadSeconds(125, 45.8, 1341, 0);
for (int metres = 0; metres <= 20; metres++)
{
    var reload = BustleTiming.ReloadSeconds(125, 45.8, 1341, metres);
    Check(Math.Abs(reload - baseReload) < 1e-9, "Distance no longer changes firing cycle");
    Check(BustleTiming.ReloadSeconds(125, 55.8, 1341, metres) > reload, "Heavier ammunition loads more slowly");
    Check(BustleTiming.ReloadSeconds(125, 45.8, 1441, metres) > reload, "Longer ammunition loads more slowly");
    Check(Math.Abs(reload * .2 + reload * .2 + reload * .4 + reload * .2 - reload) < 1e-9, "Mechanical phases match advertised reload time");
}
foreach (var invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
{
    bool rejected = false;
    try { BustleTiming.ReloadSeconds(125, 45.8, 1341, invalid); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Reject invalid native path distances");
}
Check(BustleTiming.MechanismMassKg(22) == 230 && BustleTiming.MechanismMassKg(28) == 260, "Bustle mechanism scales with native capacity");
Console.WriteLine($"Bustle timing PASS: {count} total assertions; 125 mm example at 1 m = {BustleTiming.ReloadSeconds(125, 45.8, 1341, 1):0.000} s.");

var compact = BustleTiming.ReloadSeconds(25, .5, 212, .5);
Check(compact > .12 && compact < .18, "Compact 25 mm feed targets over 330 rpm");
Check(Math.Abs(BustleTiming.ReloadSeconds(120, 44.1, 1560, 1) - 6) < .01,
    "120 mm / 1200 mm propellant targets six seconds");
Check(BustleTiming.ReloadSeconds(25, .5, 424, .5) > compact, "Long small-calibre rounds have a penalty");
Check(BustleTiming.ReloadSeconds(50, .5, 212, .5) > compact * 2, "Calibre growth is nonlinear");
for (int c = 5; c <= 300; c++)
{
    var cycle = BustleTiming.ReloadSeconds(c, .5, c * 8, .5);
    Check(double.IsFinite(cycle) && cycle >= .06, "Finite cycle retains mechanical floor");
    Check(BustleTiming.ReloadSeconds(c + 1, .5, (c + 1) * 8, .5) > cycle, "Cycle increases smoothly with ammunition size");
}
Console.WriteLine($"Nonlinear timing PASS: {count} assertions; compact 25 mm = {compact:0.000} s / {60 / compact:0} theoretical rpm.");
var thirty = BustleTiming.ReloadSeconds(30, .8, 240, .5);
Check(thirty < .25 && thirty > .12, "Compact 30 mm cycle supports more than 240 theoretical rpm");
Check(BustleTiming.ReloadSeconds(30, .8, 240, 0) == BustleTiming.ReloadSeconds(30, .8, 240, 50), "Reach and cycle are independent");
Console.WriteLine($"30 mm compact target {thirty:0.000} s / {60 / thirty:0} rpm; {count} assertions.");
var outlet = BustleFeedGeometry.Outlet(.41, 1.56, 120);
Check(Math.Abs(outlet.X - .181) < 1e-9 && Math.Abs(outlet.Y - .048) < 1e-9 && Math.Abs(outlet.Z - 1.1136) < 1e-9,
    "120 mm outlet lies at the front of the actual loading fork");
Check(BustleFeedGeometry.Outlet(.41, 1.56, 30).Z < outlet.Z, "Small-calibre outlet follows smaller arm");
Check(BustleFeedGeometry.Outlet(.41, 2.56, 120).Z == outlet.Z + .5, "Rack length moves outlet with front wall");
Console.WriteLine($"Feed outlet geometry PASS: {count} assertions.");

foreach (var audio in new[] { ("t90", 44100, 283768), ("t64", 44100, 356970), ("bustle", 48000, 301824) })
{
    using var file = File.OpenRead(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Assets/Audio", audio.Item1 + ".wav")));
    var decoded = PcmWave.Read(file);
    Check(decoded.Frequency == audio.Item2 && decoded.Samples.Length == audio.Item3, "Provided WAV retains exact rate and duration");
    Check(decoded.Samples.All(s => float.IsFinite(s) && s >= -1 && s <= 1), "Downmixed WAV is valid normalized PCM");
    Check(decoded.Samples.Any(s => Math.Abs(s) > .01f), "Decoded WAV contains audible samples");
}
foreach (var invalidWave in new[] { Array.Empty<byte>(), new byte[12], File.ReadAllBytes(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Assets/Audio/t90.wav"))).Take(200).ToArray() })
{
    var rejected = false;
    try { PcmWave.Read(new MemoryStream(invalidWave)); }
    catch (Exception ex) when (ex is IOException or InvalidDataException) { rejected = true; }
    Check(rejected, "Malformed/truncated WAV fails safely");
}
Console.WriteLine($"Custom WAV decoder PASS: {count} assertions. 120x1200 reference {BustleTiming.ReloadSeconds(120, 44.1, 1560, 1):0.000} s.");

var frameGate = new OncePerFrame();
int refreshes = 0;
for (int frame = 100; frame < 103; frame++)
    for (int controller = 0; controller < 100; controller++)
        if (frameGate.Enter(frame)) refreshes++;
Check(refreshes == 3, "100 vehicle controllers refresh global bindings only once per frame");
Check(LoadWorkPolicy.Steps(false, false, .14, .016) == 1, "Idle compact autocannon receives one native update");
Check(LoadWorkPolicy.Steps(true, true, .14, .016) == 1, "Empty magazine waiting for crew receives no rapid substeps");
Check(LoadWorkPolicy.Steps(true, false, .14, .016) == 8, "Active rapid reload retains eight native substeps");
Check(LoadWorkPolicy.Steps(true, false, 6, .016) == 1 && LoadWorkPolicy.Steps(true, false, .5, .016) == 1,
    "Tank cycles and threshold do not substep");
foreach (var invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
    Check(LoadWorkPolicy.Steps(true, false, .14, invalid) == 1, "Invalid delta cannot multiply native updates");
var allocatedDelta = .016 / LoadWorkPolicy.Steps(true, false, .14, .016);
Check(Math.Abs(allocatedDelta * 8 - .016) < 1e-12, "Rapid substeps conserve simulation time");
Console.WriteLine($"Load work policy PASS: {count} assertions; 100 controllers x 3 frames => {refreshes} global refreshes.");

// Both arm choices must retain fork height/forward reach and agree under rotation.
foreach (var width in new[]{.025,.5,1d,2d}) foreach (var length in new[]{.025,.5,2d}) foreach (var calibre in new[]{15d,75d,125d,250d})
{
    var a = BustleFeedGeometry.Outlet(width,length,calibre);
    var b = BustleFeedGeometry.Outlet(width,length,calibre,true);
    Check(Math.Abs(a.X+b.X)<1e-12 && a.Y==b.Y && a.Z==b.Z,"Arm mirror changes only lateral position");
    foreach (var yaw in new[]{0d,30d,90d,180d})
    {
        var r=yaw*Math.PI/180;
        var centre=new System.Numerics.Vector3(.2f,.3f,-.4f);
        var rotation=System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY,(float)r);
        var wa=System.Numerics.Vector3.Transform(new((float)a.X,(float)a.Y,(float)a.Z),rotation)+centre;
        var wb=System.Numerics.Vector3.Transform(new((float)b.X,(float)b.Y,(float)b.Z),rotation)+centre;
        Check(Math.Abs(System.Numerics.Vector3.Distance(wa,wb)-2*Math.Abs(a.X))<1e-5,"Mirrored reach remains consistent after turret rotation/centre offset");
    }
}
Console.WriteLine($"FR008 mirror geometry PASS: {count} assertions. Native UI/save-load/automatic feed remains pending.");


var visualAccepted=0;
foreach(var calibre in new[]{15d,25d,30d,50d,75d,100d,120d,125d,152d,250d})
foreach(var propellant in new[]{0d,30d,408d,700d,1200d,2000d})
foreach(var diameter in new[]{800d,1500d,2000d,2500d,2716d,4000d,6000d})
foreach(var depth in new[]{250d,453d,800d,1200d,2500d})
foreach(var layout in new[]{CarouselLayout.HorizontalCassette,CarouselLayout.VerticalCharge})
{
    var size=CarouselGeometry.Calculate(new[]{new Segment(diameter,depth)},calibre,calibre*3+propellant,40,layout);
    if(size.Capacity==0) continue;
    visualAccepted++;
    try {
        var meshes=CarouselVisualGeometry.Build(diameter/1000,depth/1000,size.Capacity,calibre/1000,
            Math.Max(.001,size.StoredProjectileMm/1000),Math.Max(0,size.StoredChargeMm/1000),layout==CarouselLayout.VerticalCharge);
        Check(meshes.Count<=5,"Visual draw groups stay bounded");
        foreach(var mesh in meshes) {
            Check(mesh.Triangles.All(i=>i>=0&&i<mesh.Vertices.Count),"Mesh indices valid");
            Check(mesh.Vertices.Count<65536,"Mesh stays in 16-bit index budget");
            if(mesh.SlotPrefixIndexCounts is { Length: > 0 } prefixes) {
                Check(prefixes.Length==size.Capacity+1&&prefixes[0]==0&&prefixes[^1]==mesh.Triangles.Count,"Ammo prefix covers native stock");
                Check(prefixes.Zip(prefixes.Skip(1),(a,b)=>a<=b&&b%3==0).All(x=>x),"Stock indices monotonic and triangle aligned");
            }
        }
    } catch(Exception ex){throw new Exception($"Visual rejected native-valid caliber={calibre},propellant={propellant},diameter={diameter},depth={depth},layout={layout},capacity={size.Capacity}",ex);}
}
Console.WriteLine($"Visual allocation compatibility PASS: {visualAccepted} native-valid designs; {count} total assertions.");

foreach(var native in new[]{0f,.1f,.4f,1f,2f,10f})
{
    Check(SemiAssistPolicy.Rate(native,1)==native,"No valid human contributor gets no assistance");
    Check(Math.Abs(SemiAssistPolicy.Rate(native,1.5)-native*1.5f)<.0001f,"Rate applies its supplied multiplier exactly once");
}
Check(SemiAssistPolicy.Rate(float.MaxValue,4)==float.MaxValue,"Boost cannot overflow native rate");
var normalWork=5d+6d; var preparation=1d;
Check(Math.Abs(normalWork/1.5+preparation-8.333333333333)<1e-8,"Native preparation remains unboosted; no fake fixed cycle");
foreach(var calibre in new[]{15d,125d,250d,500d,1000d})
{
    var a=SemiAssistPolicy.Outlet(2,3,calibre,false);var b=SemiAssistPolicy.Outlet(2,3,calibre,true);
    Check(a.X==-b.X && a.Y==b.Y && a.Z==b.Z,"Semi mirror changes only lateral feed position");
    Check(Math.Abs(a.Z-(1.5+.3475*SemiAssistPolicy.ArmScale(calibre)))<1e-12,"Visible large-shell arm and feed endpoint agree");
}
Check(SemiAssistPolicy.ArmScale(500)==4 && BustleFeedGeometry.Scale(500)==2,"Semi supports larger shell arm; accepted full-auto geometry unchanged");
var semiJson=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","Parts","sprocketSemiAutoloaderPart.json")));
var fullJson=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","Parts","sprocketBustleAutoloaderPart.json")));
Check(semiJson.RootElement.GetProperty("guid").GetString()!=fullJson.RootElement.GetProperty("guid").GetString(),"New semi part cannot overwrite existing bustle saves");
Check(semiJson.RootElement.GetProperty("components")[0].GetProperty("type").GetString()=="ammoRack","Semi keeps native ammo rack dimensions/capacity");
Console.WriteLine($"Large-shell semi geometry and distinct part contract PASS: {count} total assertions.");
var zeroSize=CarouselGeometry.Calculate(new[]{new Segment(2500,800)},125,375,40);
var zeroMeshes=CarouselVisualGeometry.Build(2.5,.8,zeroSize.Capacity,.125,.375,0,false);
Check(zeroMeshes.Where(m=>m.Material=="Charge").All(m=>m.Triangles.Count==0),"Zero native propellant never creates decorative ammunition");
// Approved mass/length workload curve, independently pinned reference and adversarial inputs.
foreach(var example in new[]{(8d,.6,.00133328,.95173326,.90413316),(12d,.8,.01000944,.96301227,.93102927),(21d,.95,.10763656,1.08992753,1.23367334),(28d,1.1,.31883302,1.36448293,1.88838236),(36d,1.2,.59095959,1.71824747,2.73197473),(60d,1.665,.94633020,2.18022927,3.83362363)})
{
    var b=SemiAssistPolicy.Curve(example.Item1,example.Item2);
    Check(b.Valid && Math.Abs(b.Weight-example.Item3)<1e-8,"Approved reference workload weight");
    Check(Math.Abs(b.Travel-example.Item4)<1e-8 && Math.Abs(b.Handling-example.Item5)<1e-8,"Approved independent rate references");
}
var referenceBalance=SemiAssistPolicy.Native(30,5,1);
Check(referenceBalance.Valid && referenceBalance.MassKg==35 && referenceBalance.LengthM==1,"Uses complete native component mass and metre length");
Check(Math.Abs(referenceBalance.Weight-.5)<1e-14 && Math.Abs(referenceBalance.Travel-1.6)<1e-14 && Math.Abs(referenceBalance.Handling-2.45)<1e-14,"Reference transition precisely pinned");
foreach(var mass in new[]{double.Epsilon,1e-200,.1,8,35,90,1e200,double.MaxValue})
foreach(var length in new[]{double.Epsilon,1e-200,.1,1,4,1e200,double.MaxValue})
{
    var b=SemiAssistPolicy.Curve(mass,length);
    Check(b.Valid && double.IsFinite(b.Weight) && b.Weight>=0 && b.Weight<=1,"Extreme finite workload cannot overflow curve");
    Check(b.Travel>=.95 && b.Travel<=2.25 && b.Handling>=.90 && b.Handling<=4,"Assistance always bounded");
    Check(SemiAssistPolicy.Curve(mass,length* .5).Weight<=b.Weight,"Longer workload monotonically receives more assistance");
}
foreach(var invalid in new[]{0d,-1,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
{
    var b=SemiAssistPolicy.Curve(invalid,1);var c=SemiAssistPolicy.Curve(35,invalid);
    Check(!b.Valid && !c.Valid && b.Travel==1 && c.Handling==1 && b.OverheadWork==0,"Invalid inputs preserve native work/rates");
}
Check(!SemiAssistPolicy.Native(-10,45,1).Valid && !SemiAssistPolicy.Native(35,-2,1).Valid,"Negative component mass cannot hide inside positive sum");
Check(SemiAssistPolicy.Native(float.MaxValue,float.MaxValue,1).Valid,"Native float component sum evaluated without float overflow");
foreach(var crewRate in new[]{.1f,1f,2f})
{
    var basePickup=2f; var extended=SemiAssistPolicy.PickupWithOverhead(basePickup,referenceBalance);
    var boosted=SemiAssistPolicy.Rate(crewRate,referenceBalance.Handling);
    Check(Math.Abs((extended-basePickup)/boosted-.75/crewRate)<1e-6,"One overhead costs 0.75 native handling-seconds scaled by actual crew");
}
Check(SemiAssistPolicy.Rate(0,referenceBalance.Handling)==0 && SemiAssistPolicy.Rate(-1,referenceBalance.Handling)==-1,"Stopped native crew cannot advance overhead");
var invalidBalance=SemiAssistPolicy.Curve(0,1);
Check(SemiAssistPolicy.PickupWithOverhead(3,invalidBalance)==3,"Invalid profile adds no overhead");
Check(float.IsFinite(SemiAssistPolicy.PickupWithOverhead(float.MaxValue,referenceBalance)),"Finite native work stays finite");
Check(SemiAssistPolicy.Rate(float.MaxValue,4)==float.MaxValue,"Rate overflow retains native finite value");
Console.WriteLine($"Approved assisted balance PASS: {count} total assertions; native gameplay pending.");
// Native feature.3 VRRC fixture: correct dropoff work by its cached effective rate.
var countdown=new SemiCountdownMath.Work(0,0,9.628,0,2.708,0,6.660,0,.264*2.232,3.957);
Check(SemiCountdownMath.TryTimes(countdown,out var remain,out var duration),"Valid effective native rates produce display times");
var expected=9.628/3.957+2.708/(.264*2.232)+6.660/3.957;
Check(Math.Abs(remain-expected)<1e-12 && remain==duration,"Independent full phase seconds sum");
var legacyCountdown=9.628/3.957+2.708/(.264*2.232)+6.660;
Check(Math.Abs(legacyCountdown-remain-6.660*(1-1/3.957))<1e-12,"Reproduces native unscaled ramwork display error exactly");
var ramOnly=countdown with { Picked=9.628,Travelled=2.708 };
SemiCountdownMath.TryTimes(ramOnly,out var beforeRam,out _);
Check(Math.Abs(beforeRam-6.66/3.957)<1e-12,"6.66 work is about 1.68 seconds, not 6.66 seconds");
foreach(var t in new[]{.1,.5,1.0})
{
    SemiCountdownMath.TryTimes(ramOnly with { Dropped=t*3.957 },out var afterRam,out _);
    Check(Math.Abs(beforeRam-afterRam-t)<1e-12,"Displayed final phase decreases one second per real second");
}
foreach(var rates in new[]{(.1,.2),(1d,1d),(2.232*.264,3.957),(4d,8d)})
{
    var start=countdown with { Prepare=1,TravelRate=rates.Item1,HandlingRate=rates.Item2 };
    SemiCountdownMath.TryTimes(start,out var full,out var whole);
    var checkpoints=new[]{ start with {Prepared=.5}, start with{Prepared=1,Picked=.3*rates.Item2},start with{Prepared=1,Picked=9.628,Travelled=.2*rates.Item1},start with{Prepared=1,Picked=9.628,Travelled=2.708,Dropped=.4*rates.Item2} };
    var elapsed=new[]{.5,1+.3,1+9.628/rates.Item2+.2,1+9.628/rates.Item2+2.708/rates.Item1+.4};
    for(int i=0;i<checkpoints.Length;i++)
    {
        SemiCountdownMath.TryTimes(checkpoints[i],out var left,out var all);
        Check(Math.Abs(full-left-elapsed[i])<1e-9 && all==whole,"All native phases share the same elapsed time basis");
        Check(Math.Abs(SemiCountdownMath.Fraction(left,all)-elapsed[i]/all)<1e-12,"Load progress uses corrected remaining numerator and same total");
    }
}
var complete=countdown with{Prepared=10,Picked=10,Travelled=3,Dropped=7};
Check(SemiCountdownMath.TryTimes(complete,out var done,out var completedTotal) && done==0 && SemiCountdownMath.Fraction(done,completedTotal)==1,"Native phase overshoot cannot make negative countdown or >100 percent progress");
foreach(var invalid in new[]{0d,-1,double.NaN,double.PositiveInfinity})
{
    Check(!SemiCountdownMath.TryTimes(countdown with{HandlingRate=invalid},out _,out _),"Preserve native stopped/invalid rate sentinel");
    Check(!SemiCountdownMath.TryTimes(countdown with{TravelRate=invalid},out _,out _),"No misleading finite countdown with stopped travel");
}
Check(!SemiCountdownMath.TryTimes(countdown with{Dropoff=double.NaN},out _,out _),"Invalid work preserves native display");
Check(!SemiCountdownMath.TryTimes(countdown with{Pickup=double.MaxValue,HandlingRate=.01},out _,out _),"Overflow does not enter display floats");
Console.WriteLine($"Scoped native countdown correction PASS: {count} total assertions; fixture estimate {remain:0.000}s vs legacy {legacyCountdown:0.000}s, constant rates only.");

// Linked production gate: fresh Edit->Play only, not active-session rebuild or reentry.
var playGate=new PlayBoundaryGate();
Check(playGate.Begin(false),"First editor test starts one fresh supply epoch");
Check(!playGate.Begin(false),"Nested assembly entry cannot reset the pending test a second time");
playGate.End();
for(int repeat=0;repeat<20;repeat++) Check(!playGate.Begin(true),"Active Play/crew/controller rebuild cannot refill consumed ammunition");
Check(playGate.Begin(false),"After returning to Edit, second Play starts a fresh supply epoch");
playGate.End();
var nativeCapacity=19;var simulatedStock=0;var resets=0;
for(int session=0;session<3;session++)
{
    if(playGate.Begin(false)){ simulatedStock=nativeCapacity;resets++; }
    Check(simulatedStock==19,"Each new test begins with its design capacity after previous depletion");
    Check(!playGate.Begin(false),"Recursive startup doesn't replace a newly bound supply");
    playGate.End();
    for(int shot=0;shot<19;shot++)
    {
        simulatedStock--;
        if(playGate.Begin(true)){simulatedStock=nativeCapacity;resets++;}
        Check(simulatedStock==18-shot,"Finite consumption remains conserved throughout this Play");
    }
    Check(simulatedStock==0,"Full depletion remains empty until actual Edit->Play transition");
}
Check(resets==3,"Only three real test-session boundaries reset three depleted magazines");
Check(playGate.Begin(false),"Failed/aborted start may be attempted from Edit again");playGate.End();
Check(playGate.Begin(false),"Finalizer unlocks retry after failed startup");playGate.End();
Console.WriteLine($"Editor replay boundary PASS: {count} total assertions; native two-Play firing still pending.");
// Canonical custom shell geometry must never be replaced by the native 3D length proxy.
var rodAz=CarouselGeometry.Calculate(new[]{new Segment(2500,1400)},125,1658,40,CarouselLayout.HorizontalCassette,1250,408);
var rodMz=CarouselGeometry.Calculate(new[]{new Segment(2500,1400)},125,1658,40,CarouselLayout.VerticalCharge,1250,408);
Check(rodAz.Capacity==0 && rodMz.Capacity==0,"1250 mm rod cannot fit either carousel with radial space below 1250");
Check(rodAz.StoredProjectileMm==1250 && rodMz.StoredProjectileMm==1250,"MZ must not replace the rod with fixed 680 mm proxy");
Check(rodAz.Reason.Contains("projectile") && rodMz.Reason.Contains("projectile"),"Rejection identifies actual projectile");
var shortRod=CarouselGeometry.Calculate(new[]{new Segment(2500,900)},125,1088,40,CarouselLayout.HorizontalCassette,680,408);
Check(shortRod.Capacity==22,"Canonical reference AZ storage remains 22 shots");
Check(LoaderPhysicalFit.NativePackDimensions(1250,408,125,out var packDiameter,out var packLength) && packDiameter==125 && packLength==1658,"Bustle and assisted pass complete physical round to native packer");
Check(LoaderPhysicalFit.NativePackDimensions(1250.01,408.01,125.01,out packDiameter,out packLength)&&packDiameter==126&&packLength==1659,"Native integer dimensions round upward, never squeeze an oversized projectile");
Check(!LoaderPhysicalFit.NativePackDimensions(65535,1,125,out _,out _),"Native encoding overflow blocks managed feed");
Check(!LoaderPhysicalFit.Valid(-1,1250,408,125)&&!LoaderPhysicalFit.Valid(2,1250,408,125),"Invalid or unknown API status cannot fall back to native proxy");
Check(LoaderPhysicalFit.NativePackDimensions(375,0,125,out _,out packLength)&&packLength==375,"Zero charge remains valid");
foreach(var invalid in new[]{double.NaN,double.PositiveInfinity,-1.0,0.0})
    Check(!LoaderPhysicalFit.Valid(1,invalid,408,125),"Invalid physical projectile fails closed");
for(int diameter=1800;diameter<=4000;diameter+=100)
for(int projectile=400;projectile<=2000;projectile+=100)
foreach(var layout in new[]{CarouselLayout.HorizontalCassette,CarouselLayout.VerticalCharge})
{
    var actual=CarouselGeometry.Calculate(new[]{new Segment(diameter,1800)},125,projectile+408,40,layout,projectile,408);
    Check(actual.StoredProjectileMm==projectile && actual.StoredChargeMm==408,"Both layouts retain exact API lengths");
    if(projectile>actual.MaxLengthMm) Check(actual.Capacity==0,"Every overlength projectile fails actual capacity, not only UI");
    else Check(actual.Capacity>0,"A physical fitting projectile remains loadable");
}
Console.WriteLine($"Canonical loader geometry PASS: {count} total assertions; native API/rack source acceptance pending.");
var failureGate=new LoaderFitFailureGate();
Check(failureGate.AllowFallback(1),"Unknown native/manual feed retains native exception fallback");
failureGate.Observe(1,true,true);
Check(!failureGate.AllowFallback(1),"Known assigned custom feed cannot use native fallback after packer exception");
for(int attempt=0;attempt<20;attempt++)
{
    try { throw new InvalidOperationException("native rack enumeration failed"); }
    catch { Check(!failureGate.AllowFallback(1),"Repeated failed queries retain known custom ownership"); }
}
failureGate.Observe(1,true,false);
Check(failureGate.AllowFallback(1),"A completed disable/unassign scan restores manual fallback");
failureGate.Observe(1,true,true);failureGate.Observe(1,false,true);
Check(failureGate.AllowFallback(1),"Native shell selection preserves historical exception fallback");
failureGate.Observe(2,true,true);failureGate.Forget(2);
Check(failureGate.AllowFallback(2),"Native cannon detach clears pointer ownership");
Check(failureGate.AllowFallback(3),"Independent unassigned cannon stays unaffected");
Console.WriteLine($"Custom feed failure gate PASS: {count} assertions; native failure injection still requires live verification.");
var uiAz=CarouselGeometry.Calculate(new[]{new Segment(2500,900)},125,1088,40,CarouselLayout.HorizontalCassette,680,408);
var uiRows=LoaderFitReadout.Carousel(uiAz);
Check(uiRows.SequenceEqual(new[]{"Shell length: 680 mm","Charge length: 408 mm","Required depth: 453 mm","Required diameter: "+Math.Ceiling(uiAz.RequiredDiameterMm).ToString("0")+" mm","Shells: 22"}),"Carousel fit readout uses calculated dimensions and capacity only");
var shallow=CarouselGeometry.Calculate(new[]{new Segment(2500,400)},125,1088,40,CarouselLayout.HorizontalCassette,680,408);
Check(LoaderFitReadout.Carousel(shallow).Contains("Depth insufficient"),"Shallow AZ reports depth rather than a developer reason string");
Check(!LoaderFitReadout.Carousel(shallow).Any(s=>s.StartsWith("Shells:")),"Failed capacity does not promise usable shells");
var uiLong=CarouselGeometry.Calculate(new[]{new Segment(2500,1400)},125,1658,40,CarouselLayout.VerticalCharge,1250,408);
Check(LoaderFitReadout.Carousel(uiLong).Contains("Diameter insufficient"),"Overlong MZ shell gives a diameter warning");
var upright=CarouselGeometry.Calculate(new[]{new Segment(2500,600)},125,1341,40,CarouselLayout.VerticalCharge,680,661);
Check(LoaderFitReadout.Carousel(upright).Contains("Required depth: 954 mm")&&LoaderFitReadout.Carousel(upright).Contains("Depth insufficient"),"Upright charge readout uses its actual height requirement");
Check(LoaderFitReadout.Magazine(1250,408,0).SequenceEqual(new[]{"Shell length: 1250 mm","Charge length: 408 mm","Required length: 1658 mm","Ammunition does not fit"}),"Native magazine axial requirement is labeled length, not guessed diameter");
Check(LoaderFitReadout.Magazine(375,0,10).Last()=="Shells: 10","Fitting zero-charge magazine keeps a clear shell count");
Check(LoaderFitReadout.Magazine(double.NaN,408,10).Single()=="Ammunition unavailable","Invalid data never becomes NaN in player UI");
Check(LoaderFitReadout.Carousel(default).Single()=="Ammunition unavailable","Missing ammo stays understandable");
foreach(var text in uiRows.Concat(LoaderFitReadout.Magazine(680,408,22)))
{
    Check(text.Length<55,"Fit lines remain compact");
    Check(!text.Contains("native",StringComparison.OrdinalIgnoreCase)&&!text.Contains("proxy",StringComparison.OrdinalIgnoreCase)&&!text.Contains("VUID",StringComparison.OrdinalIgnoreCase),"No implementation terms in fit information");
}
Console.WriteLine($"Player fit readout PASS: {count} total assertions.");

var cookoff=new CarouselDamagePolicy();
Check(!cookoff.ShouldApply(0,100,100),"An empty native rack cannot ignite or emit negative unignited propellant");
foreach(var invalid in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
{
    Check(!cookoff.ShouldApply(22,invalid,10),"Invalid health does not invoke native cookoff");
    Check(!cookoff.ShouldApply(22,100,invalid),"Healing/invalid damage does not invoke native cookoff");
}
Check(cookoff.ShouldApply(1,100,1),"A last stored round remains eligible for the native threshold decision");
Check(!cookoff.CommitIgnition(0)&&!cookoff.Ignited,"Empty event cannot latch damage");
Check(cookoff.CommitIgnition(9)&&cookoff.Ignited,"A genuine native event spends remaining supply");
Check(!cookoff.CommitIgnition(9)&&!cookoff.ShouldApply(9,100,100),"Recursive and later pressure/thermal callbacks cannot cook off twice");
cookoff.ResetForNewPlay();
Check(!cookoff.Ignited&&cookoff.ShouldApply(22,100,100),"Explicit new Play rearms the fresh finite rack");
foreach(var layout in new[]{false,true})
foreach(var rounds in new[]{1,22,64})
{
    var geometry=CarouselVisualGeometry.Build(3,1.2,rounds,.125,.68,.408,layout);
    var ammunition=geometry.Where(g=>g.SlotPrefixIndexCounts.Length==rounds+1).ToArray();
    Check(ammunition.Length==2,"Both native projectile and charge geometry are represented");
    Check(ammunition.Sum(g=>g.Triangles.Count)>0,"Native damage mesh is not empty for stored rounds");
    foreach(var mesh in ammunition)
    {
        Check(mesh.Triangles.All(i=>i>=0&&i<mesh.Vertices.Count),"Ammo damage mesh indices remain valid");
        Check(mesh.Vertices.All(v=>double.IsFinite(v.X)&&double.IsFinite(v.Y)&&double.IsFinite(v.Z)),"Ammo damage coordinates remain finite");
    }
}
Console.WriteLine($"Carousel native cookoff policy/geometry PASS: {count} total assertions. Native hit/FX tests pending.");
VisualTests.Run(Check);
RefillTests.Run(Check);
HudLayoutTests.Run(Check);
Console.WriteLine($"HUD placement, visibility, line choice and ring gate PASS: {count} total assertions.");
SafetyTests.Run(Check);
Console.WriteLine($"0.2.28 safety: restore/time-out, switches, Unity version check, early exit and guards PASS: {count} total assertions.");
CarouselModelTests.Run(Check);
Console.WriteLine($"0.2.34 carousel models, lift and ring: OBJ data, style, slot wedges, basket bounds or Simple, nose/charge, visible rounds, Simple = 0.2.28, triangle budget, lift direction/ring turn/place/height, ring turns per shot (f - 1, smooth), version constant, ring parts in Play, lift target cache, lift height at the T-80BVM depth window PASS: {count} total assertions.");
BeltTests.Run(Check);
BeltVisualTests.Run(Check);
RefillRegressionTests.Run(Check);
MixedAutoloaderTests.Run(Check);
Console.WriteLine($"ALL PASS: {count} total assertions.");
