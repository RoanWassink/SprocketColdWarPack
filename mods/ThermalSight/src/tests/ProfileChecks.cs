using System.Text.Json;
using System.Text.Json.Nodes;

namespace SprocketThermalSight.Tests;

// 0.5.4: the nine standardised profiles, the legacy aliases, the repaired active IR (exposure curve,
// beam falloff, grain, normalised distance/occlusion grid) and the white-hot/black-hot switch.
internal static class ProfileChecks
{
    private static readonly string[] Modern =
    {
        "irActiveL1", "iiGen1", "iiGen2", "tiGen1", "tiGen1Late", "tiGen2", "tiGen3", "tiGen3Uncooled", "tiGen3HD", "ti2s38OesOp"
    };
    private static readonly string[] ThermalLine = { "tiGen1", "tiGen1Late", "tiGen2", "tiGen3", "tiGen3Uncooled", "tiGen3HD" };
    private static readonly string[] IntensifierLine = { "iiGen1", "iiGen2" };

    internal static void Run(Action<bool, string> check, Action<Action, string> reject, ThermalCatalog catalog, string root)
    {
        Catalogue(check, reject, catalog, root);
        Aliases(check, reject, catalog, root);
        Exposure(check, catalog);
        Beam(check);
        Images(check, catalog, root);
        Polarity(check, catalog);
    }

    // --- Every field explicit, no duplicates, progression by date within each family.
    private static void Catalogue(Action<bool, string> check, Action<Action, string> reject, ThermalCatalog catalog, string root)
    {
        string path = Path.Combine(root, "package/BepInEx/plugins/SprocketThermalSight/thermal-models.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        check(doc.RootElement.GetProperty("version").GetInt32() == 3, "catalogue is version 3");
        check(catalog.DefaultProfileId == "tiGen1", "defaultProfileId is tiGen1 as in 0.5.3");
        var ids = doc.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("componentId").GetString()!).ToArray();
        check(ids.SequenceEqual(Modern), "exactly the ten modern profiles (including 2S38 OES-OP), legacy parts removed");
        check(catalog.Models.Count == 10 && catalog.Aliases.Count == 5, "ten profiles and five legacy aliases");
        foreach (var model in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            string id = model.GetProperty("componentId").GetString()!;
            foreach (var field in ThermalProfile.RequiredFields)
                check(model.TryGetProperty(field, out _), id + " states " + field + " explicitly");
            if (model.GetProperty("sensorType").GetString() == "activeIR")
                foreach (var field in ThermalProfile.RequiredBeamFields)
                    check(model.TryGetProperty(field, out _), id + " states " + field + " explicitly");
            check(!model.TryGetProperty("partGuid", out _), id + " has no legacy part GUID");
        }
        // A v3 profile that omits a field is refused, so no implicit default can creep back in.
        string one = "{\"componentId\":\"x\",\"displayName\":\"X\",\"sensorType\":\"thermal\",\"availableFrom\":\"1977.01.01\",\"width\":320,\"height\":240,\"refreshHz\":15,\"noise\":0.02,\"blur\":0.2,\"sharpen\":0,\"contrast\":1.4,\"brightness\":0,\"gamma\":1,\"grayLevels\":256,\"smoothPixels\":false,\"backgroundLevel\":0.3,\"backgroundDetail\":0.3,\"backgroundGamma\":0.6,\"vehicleHeat\":0.8,\"palette\":\"whiteHot\",\"cooled\":false,\"extraMassKg\":15,\"extraAssemblyCost\":300}";
        string V3(string model) => "{\"version\":3,\"defaultProfileId\":\"x\",\"models\":[" + model + "]}";
        ReadText(V3(one), c => check(c.Models.Count == 1, "complete v3 profile accepted"));
        foreach (var field in ThermalProfile.RequiredFields)
        {
            var node = JsonNode.Parse(one)!.AsObject(); node.Remove(field);
            reject(() => ReadText(V3(node.ToJsonString()), _ => { }), "v3 profile without " + field + " refused");
        }
        var ir = JsonNode.Parse(one)!.AsObject();
        ir["sensorType"] = "activeIR"; ir["beamRangeM"] = 800; ir["beamAngleDeg"] = 6; ir["exposureK"] = 6;
        ReadText(V3(ir.ToJsonString()), c => check(c.Models[0].ExposureK > 0, "activeIR exposureK has a value"));
        ir.Remove("exposureK");
        reject(() => ReadText(V3(ir.ToJsonString()), _ => { }), "v3 activeIR without exposureK refused");

        // Progression by date within each family: resolution up, noise down, refresh up.
        // The uncooled Gen 3 is the deliberate exception: it trades noise and frame rate for mass and cost.
        foreach (var family in new[] { ThermalLine, IntensifierLine })
            for (int i = 1; i < family.Length; i++)
            {
                var a = catalog.Resolve(family[i - 1]); var b = catalog.Resolve(family[i]);
                check(b.AvailableDate > a.AvailableDate, family[i] + " is later than " + family[i - 1]);
                if (family[i] == "tiGen3Uncooled")
                {
                    check(b.Width * b.Height == a.Width * a.Height && b.Noise > a.Noise && b.RefreshHz < a.RefreshHz, "uncooled Gen 3 keeps the array but is noisier and slower");
                    continue;
                }
                if (family[i] == "tiGen3") check(b.Width * b.Height == a.Width * a.Height && b.RefreshHz > a.RefreshHz && b.Noise < a.Noise && b.Cooled, "Gen 3 keeps the array, faster and quieter, cooled");
                else check(b.Width * b.Height > a.Width * a.Height, family[i] + " has more pixels");
                check(b.Noise < a.Noise, family[i] + " is less noisy");
                check(b.RefreshHz >= a.RefreshHz, family[i] + " refreshes at least as fast");
            }
        var irp = catalog.Resolve("irActiveL1"); var ii1 = catalog.Resolve("iiGen1"); var ii2 = catalog.Resolve("iiGen2");
        check(irp.AvailableDate < ii1.AvailableDate && ii1.AvailableDate < ii2.AvailableDate, "active IR, II Gen 1, II Gen 2 in date order");
        check(ii2.Width * ii2.Height > ii1.Width * ii1.Height && ii2.Noise < ii1.Noise && ii2.ExtraMassKg < ii1.ExtraMassKg && ii2.ExtraAssemblyCost > ii1.ExtraAssemblyCost,
            "II Gen 2: more pixels, less noise, lighter, more expensive");
        var g3 = catalog.Resolve("tiGen3"); var g3u = catalog.Resolve("tiGen3Uncooled"); var hd = catalog.Resolve("tiGen3HD");
        check(g3u.ExtraMassKg < g3.ExtraMassKg && g3u.ExtraAssemblyCost < g3.ExtraAssemblyCost && g3u.Noise > g3.Noise && g3u.RefreshHz < g3.RefreshHz,
            "uncooled Gen 3: lighter, cheaper, noisier, slower than the cooled Gen 3");
        check(hd.Width * hd.Height > g3.Width * g3.Height && hd.RefreshHz > g3.RefreshHz && hd.Noise < g3.Noise && hd.ExtraAssemblyCost > g3.ExtraAssemblyCost,
            "Gen 3 HD: largest, fastest, quietest, most expensive");
        check(catalog.Resolve("tiGen1").ExtraMassKg > catalog.Resolve("tiGen2").ExtraMassKg && catalog.Resolve("tiGen1").ExtraAssemblyCost < catalog.Resolve("tiGen2").ExtraAssemblyCost,
            "Gen 1 is heavier and cheaper than Gen 2");
        check(catalog.Models.All(m => m.SensorType == "thermal" ? m.Palette is "whiteHot" or "blackHot" : m.Palette == "greenHot"),
            "thermal profiles are white-hot by default, night-vision tubes stay green");
        check(catalog.Models.Where(m => m.SensorType != "thermal").All(m => !m.PolaritySwitch) && catalog.Models.Where(m => m.SensorType == "thermal").All(m => m.PolaritySwitch),
            "the polarity switch exists for thermal profiles only");
        check(catalog.Models.All(m => m.CustomColors is { Length: 2 }), "custom colours present on every profile");
    }

    private static void ReadText(string json, Action<ThermalCatalog> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "thermal-catalog-" + Guid.NewGuid() + ".json");
        try { File.WriteAllText(path, json); check(ThermalCatalog.Read(path)); }
        finally { File.Delete(path); }
    }

    // --- Legacy part GUIDs and old profile ids map to the modern line, read from the fixtures.
    private static void Aliases(Action<bool, string> check, Action<Action, string> reject, ThermalCatalog catalog, string root)
    {
        var expected = new Dictionary<string, string>
        {
            ["thermalSightModel1"] = "tiGen1", ["thermalSightModel2"] = "tiGen1Late", ["thermalSightModel3"] = "tiGen2",
            ["thermalSightMk3WhiteHot"] = "tiGen2", ["thermalSightMk3BlackHot"] = "tiGen2"
        };
        var guids = new Dictionary<string, string>
        {
            ["e1e0a2bf-5316-416a-b45e-b8e70b21a151"] = "tiGen1", ["40b644a7-90ed-47c7-b78f-e2f7310b80a2"] = "tiGen1Late",
            ["39614761-b743-408d-b112-9d4c24673c03"] = "tiGen2", ["37d49a2d-57ab-598a-8622-e909c48b2ad9"] = "tiGen2",
            ["ae0c73ac-6831-5c43-abf4-118b5352914d"] = "tiGen2"
        };
        // Every alias is validated against the canonical legacy GUID list, and every legacy GUID has an alias.
        check(catalog.Aliases.All(a => ThermalAlias.LegacyPartGuids.Contains(a.PartGuid)), "all aliases belong to the legacy GUID list");
        check(ThermalAlias.LegacyPartGuids.All(g => catalog.Alias(g) != null), "every legacy GUID is covered by the catalogue");
        string one = "{\"componentId\":\"tiGen2\",\"displayName\":\"G2\",\"sensorType\":\"thermal\",\"availableFrom\":\"1992.01.01\",\"width\":640,\"height\":480,\"refreshHz\":30,\"noise\":0.012,\"blur\":0.12,\"sharpen\":0,\"contrast\":1.7,\"brightness\":0,\"gamma\":1,\"grayLevels\":256,\"smoothPixels\":false,\"backgroundLevel\":0.28,\"backgroundDetail\":0.18,\"backgroundGamma\":0.9,\"vehicleHeat\":0.8,\"palette\":\"whiteHot\",\"cooled\":false,\"extraMassKg\":35,\"extraAssemblyCost\":3500}";
        string badAlias = "{\"version\":3,\"defaultProfileId\":\"tiGen2\",\"models\":[" + one + "],\"aliases\":[{\"partGuid\":\"11111111-2222-3333-4444-555555555555\",\"legacyId\":\"fakeLegacy\",\"profileId\":\"tiGen2\"}]}";
        reject(() => ReadText(badAlias, _ => { }), "alias with unknown non-legacy GUID rejected");

        foreach (var (legacy, modern) in expected)
        {
            check(catalog.Map(legacy) == modern, "legacy id " + legacy + " maps to " + modern);
            check(catalog.Models.All(m => m.ComponentId != legacy), legacy + " is no longer selectable");
        }
        foreach (var (guid, modern) in guids)
        {
            check(catalog.Map(guid) == modern, "legacy part GUID " + guid + " maps to " + modern);
            check(catalog.Map(guid.ToUpperInvariant()) == modern, "legacy part GUID is case-insensitive");
        }
        check(catalog.Map("tiGen2") == "tiGen2" && catalog.Map("not-a-profile") == null && catalog.Map(null) == null, "current ids pass through, unknown ids stay unknown");
        check(catalog.Alias("thermalSightMk3BlackHot")!.ProfileId == "tiGen2" && catalog.AliasBlackHot("thermalSightMk3BlackHot")
            && catalog.AliasBlackHot("ae0c73ac-6831-5c43-abf4-118b5352914d"), "black-hot legacy id and GUID map to tiGen2 with black-hot polarity");
        check(!catalog.AliasBlackHot("thermalSightMk3WhiteHot") && !catalog.AliasBlackHot("thermalSightModel3"), "white-hot legacy parts stay white-hot");

        // The real blueprint fixture: the sight part GUID and the saved profile id both resolve.
        string fixture = Path.Combine(root, "tmp-test/fixtures/Object 292.blueprint");
        check(File.Exists(fixture), "blueprint fixture present");
        var blueprint = JsonNode.Parse(File.ReadAllText(fixture))!.AsObject();
        var sightObject = blueprint["objects"]!.AsArray().First(o => o!.AsObject().ContainsKey("thermalSight"))!.AsObject();
        string partGuid = sightObject["guid"]!.GetValue<string>();
        check(partGuid == "c9f96b83-cd58-47d1-999b-83c5e71a981f", "fixture carries the configurable thermal sight part");
        check(catalog.Map(partGuid) == null, "the current part GUID is not an alias");
        foreach (var (guid, modern) in guids)
        {
            var old = JsonNode.Parse(File.ReadAllText(fixture))!.AsObject();
            old["objects"]![0]!["guid"] = guid;
            string componentId = old["objects"]![0]!["guid"]!.GetValue<string>();
            check(catalog.Map(componentId) == modern, "old tank with part " + guid + " resolves to " + modern);
        }
        string saved = Path.Combine(root, "tmp-test/fixtures/Object 292.profile-0.4.2.blueprint");
        check(File.Exists(saved), "saved-profile fixture present");
        var savedObject = JsonNode.Parse(File.ReadAllText(saved))!.AsObject()["objects"]!.AsArray()
            .First(o => o!.AsObject().ContainsKey(ThermalProfileData.Key))!.AsObject();
        var rows = ThermalProfileData.Decode(savedObject[ThermalProfileData.Key]!.GetValue<string>());
        check(rows.Length == 1 && rows[0].Profile == "tiGen2" && catalog.Map(rows[0].Profile) == "tiGen2", "fixture profile id still resolves");
        foreach (var (legacy, modern) in expected)
        {
            var document = ThermalProfileData.Encode(new[] { new ThermalProfileEntry(rows[0].Sight, legacy) });
            var read = ThermalProfileData.Decode(document);
            check(read.Length == 1 && catalog.Map(read[0].Profile) == modern, "saved id " + legacy + " in " + ThermalProfileData.Key + " maps to " + modern);
            check(SavedProfileKeys.Resolve(new[] { SavedProfileKeys.Canonical }) == SavedProfileKeys.Canonical, "legacy single-profile key still read");
            check(catalog.Map(legacy) == modern, "legacy single-profile value " + legacy + " maps to " + modern);
        }
        var eras = new[] { new DateTime(1900, 1, 1), new DateTime(1945, 9, 3), new DateTime(1992, 1, 1) };
        var mapped = catalog.Resolve(catalog.Map("thermalSightModel3")!);
        check(!ThermalEraPolicy.Allows(new DateTime(1991, 12, 31), eras, new DateTime(1945, 9, 3), mapped.AvailableDate)
            && ThermalEraPolicy.Allows(new DateTime(1992, 1, 1), eras, new DateTime(1945, 9, 3), mapped.AvailableDate), "date gate unchanged for a mapped profile");
    }

    // --- Active IR exposure: soft saturation, monotonic, no clipping, shading kept.
    private static void Exposure(Action<bool, string> check, ThermalCatalog catalog)
    {
        var p = catalog.Resolve("irActiveL1");
        check(p.ExposureK > 0 && p.Width == 480 && p.Height == 360 && p.SmoothPixels && p.Noise <= .05f, "active IR is 480x360, smoothed, fine grain");
        float previous = -1;
        for (int i = 0; i <= 1000; i++)
        {
            float x = i / 100f;
            float v = SensorWorkspace.SoftSaturation(x);
            check(v >= previous && v >= 0 && v <= 1, "soft saturation is monotonic and bounded");
            previous = v;
        }
        check(SensorWorkspace.SoftSaturation(0) == 0 && SensorWorkspace.SoftSaturation(-1) == 0 && SensorWorkspace.SoftSaturation(float.NaN) == 0, "no light, no signal");
        check(SensorWorkspace.SoftSaturation(.1f) < .1f && SensorWorkspace.SoftSaturation(10) > .99f, "linear near zero, asymptotic to one");
        // At 300 m, the old linear gain of 20 * 0.391 = 7.81 saturated every surface with luma >= 0.13; the curve keeps shading.
        float illum300 = IrBeamPolicy.DistanceFalloff(300, p.BeamRangeM);
        check(.35f * 20 * .39f > 1 && SensorWorkspace.SoftSaturation(p.ExposureK * .35f * illum300) < .85f, "a 300 m tank surface no longer saturates");
        check(SensorWorkspace.SoftSaturation(p.ExposureK * .65f * illum300) > SensorWorkspace.SoftSaturation(p.ExposureK * .35f * illum300) + .1f, "brighter surfaces stay brighter");

        // A lit tank at 300 m: less than 5 % of its pixels at full white, and the shading survives.
        var frame = TestScene.Build(p.Width, p.Height, 300, beam: true, beamRangeM: p.BeamRangeM, beamAngleDeg: p.BeamAngleDeg);
        var work = new SensorWorkspace(p);
        var image = work.Process(frame.Scene, frame.Mask, 7, frame.Illumination).ToArray();
        var stats = TestScene.Measure(image, frame, false, greenChannel: true);
        Console.WriteLine($"IR 300 m: vehicle mean {stats.Vehicle:F0}, clipped {stats.Clipped:F2} %, ground {stats.Background:F0}, detail {stats.Detail:F1}, noise {stats.Noise:F1}.");
        check(stats.Clipped < 5, "less than 5 % of the tank is clipped at 300 m");
        check(stats.Vehicle > stats.Background + 25, "the lit tank stands out from the ground");
        check(stats.Detail > 4, "the tank keeps internal shading");

        // Outside the 6 degree beam cone, the ground stays near-black with faint grain.
        double outside = 0; int outsideCount = 0;
        double insideGround = 0; int insideCount = 0;
        double skyMean = 0; int skyCount = 0;
        for (int i = 0; i < frame.Ground.Length; i++)
        {
            byte g = image[i * 4 + 1];
            if (frame.Ground[i] && frame.Cone[i] == 0) { outside += g; outsideCount++; }
            else if (frame.Ground[i] && frame.Cone[i] > .5f) { insideGround += g; insideCount++; }
            else if (frame.Sky[i]) { skyMean += g; skyCount++; }
        }
        outside /= Math.Max(1, outsideCount);
        insideGround /= Math.Max(1, insideCount);
        skyMean /= Math.Max(1, skyCount);
        Console.WriteLine($"IR 300 m regions (green 0..255): beam ground {insideGround:F1}, outside beam {outside:F1}, sky {skyMean:F1}.");
        check(outsideCount > 10000 && outside < 18, "ground outside the 6 degree beam is near-black");
        check(insideCount > 5000 && insideGround > outside + 35, "beam ellipse on the ground is clearly lit");
        check(skyCount > 10000 && skyMean < 18, "sky above the horizon stays near-black");

        // Unlit frame (lamp off / beyond range): near-black with faint per-pixel grain.
        var dark = new float[frame.Illumination.Length];
        var unlit = work.Process(frame.Scene, frame.Mask, 7, dark).ToArray();
        double unlitMean = 0; for (int i = 0; i < dark.Length; i++) unlitMean += unlit[i * 4 + 1];
        unlitMean /= dark.Length;
        check(unlitMean < 12, "without lamp the tube is near-black");
        var spread = Enumerable.Range(0, dark.Length).Select(i => (int)unlit[i * 4 + 1]).ToArray();
        check(spread.Max() - spread.Min() > 0 && spread.Max() < 45, "faint grain remains outside the beam");
        check(stats.Noise < 20 && p.Noise <= .05f && p.Noise >= .02f, "fine per-pixel grain around 0.04");
    }

    // --- Beam: per-pixel cone falloff, smooth edge, normalised distance grid (no sky darkening, no grid edge).
    private static void Beam(Action<bool, string> check)
    {
        float previous = 2;
        for (int i = 0; i <= 200; i++)
        {
            float degrees = i / 200f * 3f;   // half of the 6 degree cone
            float radians = degrees * MathF.PI / 180;
            float weight = IrBeamPolicy.PixelWeight(MathF.Sin(radians), 0, MathF.Cos(radians), 0, 0, 1, 6);
            check(weight >= 0 && weight <= 1 && weight <= previous + 1e-6f, "cone falloff is monotonic towards the edge");
            previous = weight;
        }
        check(IrBeamPolicy.PixelWeight(0, 0, 1, 0, 0, 1, 6) == 1, "the axis is fully lit");
        check(IrBeamPolicy.PixelWeight(MathF.Sin(3 * MathF.PI / 180), 0, MathF.Cos(3 * MathF.PI / 180), 0, 0, 1, 6) == 0, "the cone edge is dark");
        check(IrBeamPolicy.PixelWeight(1, 0, 0, 0, 0, 1, 6) == 0 && IrBeamPolicy.PixelWeight(0, 0, -1, 0, 0, 1, 6) == 0, "outside the cone and behind the lamp are dark");
        check(IrBeamPolicy.PixelWeight(float.NaN, 0, 1, 0, 0, 1, 6) == 0, "invalid geometry is dark");
        float maxStep = 0;
        for (int i = 1; i <= 600; i++)
        {
            float a = (i - 1) / 600f * 3f * MathF.PI / 180, b = i / 600f * 3f * MathF.PI / 180;
            float wa = IrBeamPolicy.PixelWeight(MathF.Sin(a), 0, MathF.Cos(a), 0, 0, 1, 6);
            float wb = IrBeamPolicy.PixelWeight(MathF.Sin(b), 0, MathF.Cos(b), 0, 0, 1, 6);
            maxStep = MathF.Max(maxStep, MathF.Abs(wa - wb));
        }
        check(maxStep < .02f, "the cone edge has no hard step");

        // Distance falloff: inverse square, clamped in the near field, fading smoothly to zero at the range.
        float prevDist = 2;
        for (int d = 0; d <= 900; d += 10)
        {
            float f = IrBeamPolicy.DistanceFalloff(d, 800);
            check(f >= 0 && f <= 1 && f <= prevDist + 1e-6f, "distance falloff is monotonic");
            prevDist = f;
        }
        check(IrBeamPolicy.DistanceFalloff(50, 800) > IrBeamPolicy.DistanceFalloff(400, 800), "distance falloff decreases with range");
        check(IrBeamPolicy.DistanceFalloff(1, 800) <= IrBeamPolicy.DistanceFalloff(25, 800) + 1e-6f, "near field is clamped");
        check(IrBeamPolicy.DistanceFalloff(300, 800) > .25f && IrBeamPolicy.DistanceFalloff(300, 800) < .65f, "300 m has usable illumination");
        check(IrBeamPolicy.DistanceFalloff(800, 800) == 0 && IrBeamPolicy.DistanceFalloff(900, 800) == 0, "beyond the range is dark");
        check(IrBeamPolicy.Combine(0, 1) == 0 && IrBeamPolicy.Combine(1, 0) == 0 && IrBeamPolicy.Combine(1, 1) == 1, "cone and grid combine");

        // Normalised grid sample: missed sky cells (-1) never darken ground next to them; pure sky is 0; occluded (0) stays dark.
        var horizonGrid = new float[] { .6f, .6f, -1f, -1f }; // 2x2: bottom row ground=0.6, top row sky=-1
        check(MathF.Abs(IrBeamPolicy.SampleGrid(horizonGrid, 2, 2, .5f, .4f) - .6f) < 1e-5f, "missed sky cell does not darken adjacent ground");
        var allSky = new float[] { -1f, -1f, -1f, -1f };
        check(IrBeamPolicy.SampleGrid(allSky, 2, 2, .5f, .5f) == 0, "pure sky returns zero");
        var occludedGrid = new float[] { .6f, 0f, .6f, 0f };
        check(MathF.Abs(IrBeamPolicy.SampleGrid(occludedGrid, 2, 2, .5f, .5f) - .3f) < 1e-5f, "occluded cells keep shadow weight");

        // On the realistic 300 m scene: smooth elliptical beam on the ground, no hard grid edge.
        var frame = TestScene.Build(TestScene.Width, TestScene.Height, 300, beam: true);
        int w = frame.Width, h = frame.Height;
        float groundRowStep = 0, groundColumnStep = 0;
        for (int y = 0; y < h; y++) for (int x = 1; x < w; x++)
        {
            int i = y * w + x;
            if (frame.Ground[i] && frame.Ground[i - 1])
                groundRowStep = MathF.Max(groundRowStep, MathF.Abs(frame.Illumination[i] - frame.Illumination[i - 1]));
        }
        for (int y = 1; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            if (frame.Ground[i] && frame.Ground[i - w])
                groundColumnStep = MathF.Max(groundColumnStep, MathF.Abs(frame.Illumination[i] - frame.Illumination[i - w]));
        }
        check(groundRowStep < .05f && groundColumnStep < .05f, "no hard grid edge in ground illumination");
        // Verify ellipse geometry: centre ground lit, left/right flanks and near-bottom outside cone dark.
        float centreGround = frame.Illumination[(int)(h * .30f) * w + w / 2];
        float leftFlank = frame.Illumination[(int)(h * .30f) * w + 10];
        float rightFlank = frame.Illumination[(int)(h * .30f) * w + w - 11];
        float nearBottom = frame.Illumination[4 * w + w / 2];
        check(centreGround > .25f && leftFlank == 0 && rightFlank == 0 && nearBottom == 0, "6 degree beam forms a centred ellipse on the ground with dark margins");
    }

    private static void Images(Action<bool, string> check, ThermalCatalog catalog, string root)
    {
        string review = Path.Combine(root, "review-0.5.4");
        Directory.CreateDirectory(review);
        var report = new List<string> { "profile, distance, background, vehicle, hot, clipped %, noise, separation, detail" };
        var sheets = new List<(string Id, byte[] Image, int W, int H)>();
        foreach (var id in Modern)
        {
            var p = catalog.Resolve(id);
            foreach (int distance in new[] { 300, 1000 })
            {
                var frame = TestScene.Build(p.Width, p.Height, distance, beam: p.SensorType == "activeIR", beamRangeM: p.BeamRangeM, beamAngleDeg: p.BeamAngleDeg);
                var work = new SensorWorkspace(p);
                var image = work.Process(frame.Scene, frame.Mask, 11, p.SensorType == "activeIR" ? frame.Illumination : null,
                    p.SensorType == "thermal" ? frame.Heat : null).ToArray();
                bool blackHot = p.Palette == "blackHot";
                bool green = p.Palette == "greenHot";
                var s = TestScene.Measure(image, frame, blackHot, green);
                report.Add($"{id}, {distance} m, {s.Background:F0}, {s.Vehicle:F0}, {s.Hot:F0}, {s.Clipped:F2}, {s.Noise:F1}, {s.Separation:F0}, {s.Detail:F1}");
                Console.WriteLine($"{id} {distance} m: background {s.Background:F0}, vehicle {s.Vehicle:F0}, hot {s.Hot:F0}, clipped {s.Clipped:F2} %, noise {s.Noise:F1}, separation {s.Separation:F0}, detail {s.Detail:F1}.");
                TestScene.Png(Path.Combine(review, $"{id}-{distance}m-after.png"), image, frame.Width, frame.Height, flip: true);
                var old = TestScene.OldProfile(id);
                if (old != null)
                {
                    var oldFrame = old.Width == frame.Width && old.Height == frame.Height
                        ? frame : TestScene.Build(old.Width, old.Height, distance, beam: old.SensorType == "activeIR", beamRangeM: old.BeamRangeM, beamAngleDeg: old.BeamAngleDeg);
                    var before = TestScene.Process053(old, oldFrame, 11);
                    TestScene.Png(Path.Combine(review, $"{id}-{distance}m-before.png"), before, oldFrame.Width, oldFrame.Height, flip: true);
                }
                if (distance == 300) sheets.Add((id, image, frame.Width, frame.Height));

                if (p.SensorType == "thermal")
                {
                    // Brief: background mean between 0.15 (38) and 0.45 (115) for white-hot (mirrored for black-hot).
                    check(s.Background >= 38 && s.Background <= 115, id + " " + distance + " m: background mid-dark (0.15..0.45), not clipped");
                    check(s.Separation > 25, id + " " + distance + " m: the vehicle is separated from the background");
                    check(s.Detail > 2, id + " " + distance + " m: the vehicle keeps internal shading");
                    check(s.Clipped < 5, id + " " + distance + " m: less than 5 % of the vehicle is clipped");
                    check(s.Noise > .5, id + " " + distance + " m: noise is visible");
                    if (distance == 1000 && id is "tiGen2" or "tiGen3" or "tiGen3Uncooled" or "tiGen3HD")
                        check(s.Separation > 30 && s.Detail > 2, id + " 1000 m: the vehicle stays identifiable");
                }
                else if (p.SensorType == "imageIntensifier")
                {
                    check(s.Vehicle > s.Background + 10, id + " " + distance + " m: the vehicle is brighter than the ground");
                    check(s.Clipped < 5, id + " " + distance + " m: less than 5 % of the vehicle is clipped");
                    check(s.Noise > .5, id + " " + distance + " m: grain is visible");
                }
                else
                {
                    check(s.Clipped < 5, id + " " + distance + " m: less than 5 % of the tank is clipped");
                    if (distance <= p.BeamRangeM)
                    {
                        check(s.Vehicle > s.Background + 25, id + " " + distance + " m: the lit tank stands out");
                        check(s.Detail > 4, id + " " + distance + " m: the lit tank keeps internal shading");
                    }
                    else
                    {
                        check(s.Vehicle < 60, id + " " + distance + " m: tank beyond 800 m beam range stays dim/dark");
                    }
                }
            }
        }
        File.WriteAllLines(Path.Combine(review, "image-targets.txt"), report);
        ContactSheet(Path.Combine(review, "contact-sheet.png"), sheets);
        check(File.Exists(Path.Combine(review, "contact-sheet.png")), "contact sheet written");
    }

    // All nine profiles side by side at 300 m, each scaled to 320x240, with a crisp readable label bar.
    private static void ContactSheet(string path, List<(string Id, byte[] Image, int W, int H)> tiles)
    {
        const int tw = 320, th = 240, label = 18;
        int width = tw * 3, height = (th + label) * 3;
        var sheet = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++) { sheet[i * 4] = 20; sheet[i * 4 + 1] = 20; sheet[i * 4 + 2] = 20; sheet[i * 4 + 3] = 255; }
        for (int t = 0; t < tiles.Count && t < 9; t++)
        {
            var (id, image, w, h) = tiles[t];
            int ox = (t % 3) * tw, oy = (t / 3) * (th + label) + label;
            for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
            {
                // Source image is bottom-up; sheet is top-down.
                int sx = x * w / tw, sy = h - 1 - (y * h / th);
                int src = (sy * w + sx) * 4, dst = ((oy + y) * width + ox + x) * 4;
                sheet[dst] = image[src]; sheet[dst + 1] = image[src + 1]; sheet[dst + 2] = image[src + 2]; sheet[dst + 3] = 255;
            }
            DrawLabel(sheet, width, height, ox + 6, oy - label + 5, id);
        }
        TestScene.Png(path, sheet, width, height, flip: false);
    }

    private static void DrawLabel(byte[] sheet, int width, int height, int x0, int y0, string text)
    {
        foreach (char raw in text)
        {
            var rows = Glyph(char.ToUpperInvariant(raw));
            for (int r = 0; r < 7; r++)
                for (int c = 0; c < 5; c++)
                    if (((rows[r] >> (4 - c)) & 1) != 0)
                    {
                        int px = x0 + c, py = y0 + r;
                        if (px < 0 || px >= width || py < 0 || py >= height) continue;
                        int dst = (py * width + px) * 4;
                        sheet[dst] = sheet[dst + 1] = sheet[dst + 2] = 230;
                    }
            x0 += 6;
        }
    }

    private static byte[] Glyph(char c) => c switch
    {
        'A' => new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
        'C' => new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E },
        'D' => new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E },
        'E' => new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F },
        'G' => new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0E },
        'H' => new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
        'I' => new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E },
        'L' => new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
        'N' => new byte[] { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 },
        'O' => new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
        'R' => new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
        'T' => new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
        'U' => new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
        'V' => new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 },
        '1' => new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
        '2' => new byte[] { 0x0E, 0x11, 0x01, 0x06, 0x08, 0x10, 0x1F },
        '3' => new byte[] { 0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E },
        _ => new byte[] { 0, 0, 0, 0, 0, 0, 0 }
    };

    // --- The white-hot/black-hot switch: per sight, session only, HUD text follows it, not for IR/II.
    private static void Polarity(Action<bool, string> check, ThermalCatalog catalog)
    {
        var thermal = catalog.Resolve("tiGen2");
        check(thermal.PolaritySwitch && thermal.EffectivePalette(false) == "whiteHot" && thermal.EffectivePalette(true) == "blackHot", "thermal profile switches polarity");
        var blackDefault = new ThermalProfile { ComponentId = "bh", DisplayName = "BH", SensorType = "thermal", Palette = "blackHot" };
        check(blackDefault.BlackHotDefault && blackDefault.EffectivePalette(false) == "whiteHot" && blackDefault.EffectivePalette(true) == "blackHot", "the switch overrides a black-hot catalogue default both ways");
        foreach (var id in new[] { "irActiveL1", "iiGen1", "iiGen2" })
        {
            var p = catalog.Resolve(id);
            check(!p.PolaritySwitch && p.EffectivePalette(true) == "greenHot", id + " stays green");
        }
        check(HudMath.Polarity(thermal.EffectivePalette(false)) == "WH" && HudMath.Polarity(thermal.EffectivePalette(true)) == "BH", "HUD text follows the actual polarity");
        check(HudMath.Polarity(catalog.Resolve("irActiveL1").EffectivePalette(true)) == "WH", "the IR tube never shows BH");

        // The image really inverts, and the palette follows the switch.
        var frame = TestScene.Build(thermal.Width, thermal.Height, 300);
        var work = new SensorWorkspace(thermal);
        var white = work.Process(frame.Scene, frame.Mask, 5, null, frame.Heat).ToArray();
        work.BlackHot = true;
        var black = work.Process(frame.Scene, frame.Mask, 5, null, frame.Heat).ToArray();
        int inverted = 0, total = 0;
        for (int i = 0; i < frame.Vehicle.Length; i++) if (frame.Vehicle[i]) { total++; if (Math.Abs(white[i * 4] + black[i * 4] - 255) <= 1) inverted++; }
        check(total > 0 && inverted == total, "black-hot inverts the vehicle pixels");
        work.BlackHot = false;
        check(work.Process(frame.Scene, frame.Mask, 5, null, frame.Heat).SequenceEqual(white), "switching back restores white-hot");

        // Per sight, in memory for the session only: the real state holder, keyed by the sight, not by the profile.
        PolarityState.Clear();
        check(!PolarityState.Seen(101) && !PolarityState.Effective(101, false) && PolarityState.Effective(101, true), "unseen sight follows profile/alias default");
        // Switching before the first scope frame records the choice for that sight and persists when the scope opens.
        check(PolarityState.Toggle(101, false) && PolarityState.Get(101) && PolarityState.Seen(101), "toggle before first scope frame switches to black-hot");
        check(!PolarityState.Get(102) && !PolarityState.Seen(102), "the choice belongs to one sight only");
        check(!PolarityState.Toggle(101, false) && !PolarityState.Get(101) && PolarityState.Count == 0 && PolarityState.Seen(101), "toggling back restores white-hot and clears the entry");
        // A legacy black-hot sight starts black-hot and toggles cleanly to white-hot.
        check(PolarityState.Effective(202, true) && !PolarityState.Toggle(202, true) && !PolarityState.Effective(202, true), "legacy black-hot sight starts black-hot and toggles to white-hot");
        PolarityState.Set(101, true); PolarityState.Set(102, true);
        PolarityState.Clear();
        check(PolarityState.Count == 0 && !PolarityState.Seen(101), "a new session resets polarity state");
        check(!ThermalProfileData.Encode(new[] { new ThermalProfileEntry(101, "tiGen2") }).Contains("blackHot"), "the save format is unchanged");
        check(catalog.AliasBlackHot("thermalSightMk3BlackHot") && !catalog.AliasBlackHot("thermalSightMk3WhiteHot")
            && !catalog.AliasBlackHot("tiGen2") && !catalog.AliasBlackHot(null), "only the old black-hot part starts black-hot");
        check(catalog.Resolve("tiGen2").EffectivePalette(catalog.AliasBlackHot("thermalSightMk3BlackHot")) == "blackHot", "the mapped part shows black-hot");
    }
}
