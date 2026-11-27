using System.Globalization;
using System.Security.Cryptography;
using SprocketCarouselAutoloader;

// 0.2.34: lift height at the T-80BVM depth window, path None (WindowTests). 0.2.33: ring parts in Play, lift target cache (PartTests). 0.2.32: ring rule f - 1, smooth turn, version constant (RingTests, TurnTests). 0.2.31: the ring turns per shot (RingTests). 0.2.30: the lift behind the cannon (LiftTests) on top of the 0.2.29 models. 0.2.29: the approved T-72/T-80 carousel models in the visual geometry (BESLUITEN 2026-11-05 "Goed, bouw ze in").
// Mesh data against the OBJ files, style choice, slot wedges, basket bounds or the Simple fallback, nose and charge
// orientation, visible-round indexing, Simple = the 0.2.28 model, and the triangle budget.
internal static class CarouselModelTests
{
    // CarouselVisualGeometry.cs of 0.2.28: the Simple model, and the source of the native damage mesh.
    const string LegacyGeometrySha = "87C73BDE5D61A77513AC075739CAFD7998FC41E288CCAFFDF23D6B40B2CD0EF1";
    static readonly Dictionary<string, int[]> BriefTriangles = new()
    {
        ["t72"] = new[] { 224, 118, 92, 1484, 1316 },
        ["t80"] = new[] { 252, 118, 92, 1484, 1600 },
    };
    static readonly string[] GroupNames = { "cassette", "projectiel", "lading", "naaf", "lift" };
    const double Eps = 1e-9;

    internal sealed record Case(string Name, double Diameter, double Depth, int Slots, double Calibre, double Projectile, double Charge, bool Vertical);
    // The render cases of the brief: T-72 (2.05 m, 22 slots, 125 mm), T-80 (2.10 m, 28 slots) and one deviating case.
    internal static readonly Case[] Renders =
    {
        new("t72-2050-22x125", 2.05, .55, 22, .125, .680, .408, false),
        new("t80-2100-28x125", 2.10, .75, 28, .125, .680, .408, true),
        new("t80-1900-31x100", 1.90, .70, 31, .100, .600, .400, true),
    };

    internal static void Run(Action<bool, string> check)
    {
        var root = FindRoot();
        check(root != null, "project folder found for the carousel model tests");
        if (root == null) return;
        var savedModel = CarouselModelGeometry.Model;
        var savedLift = CarouselModelGeometry.Lift;
        var report = new List<string> { "Carousel Autoloader 0.2.32 carousel models, lift and ring (CarouselModelGeometry.Build, metres)." };
        try
        {
            CarouselModelGeometry.Model = CarouselVisualModel.Detailed;
            CarouselModelGeometry.Lift = true;
            check(savedModel == CarouselVisualModel.Detailed && savedLift, "config defaults: CarouselModel Detailed, CarouselLift true");
            Data(check, root, report);
            Sources(check, root);
            SimpleIsOld(check);
            Cases(check, report);
            Rounds(check);
            Budget(check, report);
            Sweep(check, report);
            LiftTests(check, report);
            RingTests(check, report);
            TurnTests(check, report);
            PartTests(check, report);
            WindowTests(check, report);
            Exports(check, report);
        }
        finally
        {
            CarouselModelGeometry.Model = savedModel;
            CarouselModelGeometry.Lift = savedLift;
        }
        Directory.CreateDirectory("logs");
        File.WriteAllLines("logs/0.2.32-carousel-models.txt", report);
    }

    // 1. Embedded data = the OBJ files in Models/ (SHA256 and an independent re-parse), triangle counts of the brief.
    static void Data(Action<bool, string> check, string root, List<string> report)
    {
        foreach (var style in CarouselModelData.All)
        {
            var path = Path.Combine(root, "Models", style.ObjFile);
            check(File.Exists(path), style.Name + " OBJ present in Models");
            if (!File.Exists(path)) continue;
            var bytes = File.ReadAllBytes(path);
            check(Convert.ToHexString(SHA256.HashData(bytes)) == style.ObjSha256, style.Name + " embedded data belongs to the OBJ (SHA256)");
            var parsed = ParseObj(System.Text.Encoding.UTF8.GetString(bytes));
            check(parsed.Select(p => p.Name).SequenceEqual(GroupNames), style.Name + " OBJ has exactly the five groups");
            for (int i = 0; i < 5; i++)
            {
                var part = style.Parts[i];
                var obj = parsed[i];
                check(part.Name == GroupNames[i] && part.Positions.SequenceEqual(obj.Positions) && part.Normals.SequenceEqual(obj.Normals) &&
                      part.Indices.SequenceEqual(obj.Indices), $"{style.Name} {part.Name}: positions, normals and triangles equal the OBJ");
                check(part.Triangles == BriefTriangles[style.Name][i], $"{style.Name} {part.Name}: {BriefTriangles[style.Name][i]} triangles as in the brief");
                check(part.Indices.All(x => x >= 0 && x < part.Vertices) && part.Positions.Length == part.Normals.Length, $"{style.Name} {part.Name}: indices in range");
                bool unit = true;
                for (int v = 0; v < part.Vertices; v++)
                {
                    var n = Math.Sqrt(part.Normals[3 * v] * part.Normals[3 * v] + part.Normals[3 * v + 1] * part.Normals[3 * v + 1] + part.Normals[3 * v + 2] * part.Normals[3 * v + 2]);
                    unit &= Math.Abs(n - 1) < 1e-3;
                }
                check(unit, $"{style.Name} {part.Name}: unit normals");
            }
            report.Add(Inv($"{style.Name}: {style.ObjFile} SHA256 {style.ObjSha256}; triangles {string.Join("/", style.Parts.Select(p => p.Triangles))}, vertices {string.Join("/", style.Parts.Select(p => p.Vertices))}"));
        }
        // Fixed points the scaling relies on (claude-fix geom.py), measured from the data.
        var t72 = CarouselModelData.T72; var t80 = CarouselModelData.T80;
        check(Near(Min(t72.Projectile, 2), -.708, 1e-6) && Near(Max(t72.Projectile, 2), -.028, 1e-6) && Near(Max(t72.Projectile, 0), .0655, 1e-6),
            "projectile: base at z -0.028, nose at -0.708 (0.680 m), radius 0.0655");
        check(Near(Min(t72.Cassette, 2), -.738, 1e-6) && Near(Max(t72.Cassette, 2), 0, 1e-6) && Near(Min(t80.Cassette, 2), -.738, 1e-6) && Near(Max(t72.Cassette, 1), .3725, 1e-6) && Near(Max(t80.Cassette, 1), .620, 1e-6),
            "cassettes: z -0.738..0, T-72 height 0.3725, T-80 height 0.620");
        check(Near(Min(t72.Charge, 2), -.440, 1e-6) && Near(Max(t72.Charge, 2), -.027, 1e-6) && Near(Min(t72.Charge, 1), .2035, 1e-3),
            "T-72 charge lies from z -0.027 to -0.440 above the projectile");
        check(Near(Min(t80.Charge, 1), .188, 1e-6) && Near(Max(t80.Charge, 1), .601, 1e-6) && Near(Min(t80.Charge, 2), -.1875, 1e-3) && Near(Max(t80.Charge, 2), -.0285, 1e-3),
            "T-80 charge stands from y 0.188 to 0.601 around z -0.108");
        check(CarouselModelGeometry.T72.Gear.Count(g => g) == 640 && CarouselModelGeometry.T80.Gear.Count(g => g) == 640,
            "hub ring gear (DriveTrim detail): 640 triangles in both styles");
        check(Near(CarouselModelGeometry.T72.HubRadius, .2512, 1e-3) && Near(CarouselModelGeometry.T72.HubHeight, .412, 1e-6) && Near(CarouselModelGeometry.T80.HubHeight, .592, 1e-6),
            "hub: radius 0.251, height 0.412 (T-72) / 0.592 (T-80)");
    }

    // 2. Sources: the 0.2.28 model stays byte-identical (native damage), the visuals use the new model, the config.
    static void Sources(Action<bool, string> check, string root)
    {
        string Read(string name) => File.ReadAllText(Path.Combine(root, name));
        check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "CarouselVisualGeometry.cs")))) == LegacyGeometrySha,
            "CarouselVisualGeometry.cs is byte-identical to 0.2.28 (Simple model and native damage mesh unchanged)");
        var damage = Read("CarouselNativeDamage.cs");
        check(damage.Contains("CarouselVisualGeometry.Build(") && !damage.Contains("CarouselModelGeometry"), "the native damage mesh still uses the 0.2.28 geometry");
        var visuals = Read("CarouselVisuals.cs");
        check(visuals.Contains("CarouselModelGeometry.Build(") && !visuals.Contains("CarouselVisualGeometry.Build("), "the basket visual uses the new model");
        check(visuals.Contains("ModelNotes.Add(") && visuals.Contains("LogInfo("), "one Info line per basket when the detailed model is not used");
        var plugin = Read("Plugin.cs");
        check(plugin.Contains("Config.Bind(\"Visuals\",\"CarouselModel\",CarouselVisualModel.Detailed,") && plugin.Contains("Config.Bind(\"Visuals\",\"CarouselLift\",true,"),
            "config [Visuals] CarouselModel = Detailed and CarouselLift = true");
        var model = Read("CarouselModelGeometry.cs");
        check(!model.Contains("Harmony") && !model.Contains("UnityEngine") && !model.Contains("Il2Cpp"), "the model geometry is pure C# (no hook, no Unity object)");
        check(File.Exists(Path.Combine(root, "Models", "obj_to_cs.py")) && Read("CarouselModelData.cs").Contains("<auto-generated> by Models/obj_to_cs.py"),
            "the conversion script is kept in the source");
    }

    // 3. Simple = exactly the 0.2.28 model (config, fallback and the same exceptions).
    static void SimpleIsOld(Action<bool, string> check)
    {
        var inputs = new List<(double D, double H, int N, double C, double P, double Q, bool V, int Vis)>
        {
            (2.05, .55, 22, .125, .68, .408, false, -1), (2.10, .75, 28, .125, .68, .408, true, -1), (2.5, .8, 22, .125, .375, 0, false, -1),
            (3, 1.2, 64, .125, .68, .408, true, 10), (1.9, .70, 31, .100, .60, .40, true, 0), (6, 2.5, 40, .25, 1.2, .8, false, 39),
        };
        foreach (var model in new[] { CarouselVisualModel.Simple, CarouselVisualModel.Detailed })
        foreach (var x in inputs)
        {
            CarouselModelGeometry.Model = model;
            var old = CarouselVisualGeometry.Build(x.D, x.H, x.N, x.C, x.P, x.Q, x.V, x.Vis);
            var now = CarouselModelGeometry.Build(x.D, x.H, x.N, x.C, x.P, x.Q, x.V, out var choice, x.Vis);
            if (model == CarouselVisualModel.Simple)
                check(choice.Level == CarouselModelLevel.Simple && choice.Info == null && Same(old, now), "Simple in the config gives exactly the 0.2.28 model");
            else
                check(choice.Level != CarouselModelLevel.Simple || Same(old, now), "a Simple fallback gives exactly the 0.2.28 model");
        }
        CarouselModelGeometry.Model = CarouselVisualModel.Detailed;
        // Capacity 0: nothing to place, the 0.2.28 frame without an Info line.
        var empty = CarouselModelGeometry.Build(2.5, .8, 0, .125, .375, 0, false, out var none);
        check(none.Level == CarouselModelLevel.Simple && none.Info == null && Same(empty, CarouselVisualGeometry.Build(2.5, .8, 0, .125, .375, 0, false)), "no slots: the 0.2.28 frame");
        // A T-80 basket deep enough for the 0.2.28 model (0.653 m) but not for the upright cassettes (0.675 m): Simple, one Info text.
        var shallow = CarouselModelGeometry.Build(2.10, .66, 20, .125, .68, .408, true, out var flat);
        check(flat.Level == CarouselModelLevel.Simple && flat.Info != null && flat.Info.Contains("deep") && Same(shallow, CarouselVisualGeometry.Build(2.10, .66, 20, .125, .68, .408, true)),
            "a basket shallower than the detailed cassettes falls back to the 0.2.28 model with an Info text");
        // Too many slots for the wedge at the cassette's inner end: Simple.
        var narrow = CarouselModelGeometry.Build(2.05, .55, 40, .125, .68, .408, false, out var tight);
        check(tight.Level == CarouselModelLevel.Simple && tight.Info != null && Same(narrow, CarouselVisualGeometry.Build(2.05, .55, 40, .125, .68, .408, false)),
            "slots too narrow for the detailed cassettes fall back to the 0.2.28 model");
        // Invalid input: the same exception as the 0.2.28 model.
        foreach (var bad in new Func<IReadOnlyList<CarouselVisualMesh>>[] {
            () => CarouselModelGeometry.Build(double.NaN, .5, 10, .125, .68, .408, false), () => CarouselModelGeometry.Build(2, .5, 65, .125, .68, .408, false),
            () => CarouselModelGeometry.Build(2, .5, 10, .125, .68, -1, false), () => CarouselModelGeometry.Build(.1, .5, 3, .125, .05, .05, false),
            () => CarouselModelGeometry.Build(1.0, .5, 10, .125, .9, .408, false) })
        {
            bool thrown = false;
            try { bad(); } catch (ArgumentOutOfRangeException) { thrown = true; }
            check(thrown, "inputs the 0.2.28 model rejects throw the same exception");
        }
    }

    // 4. The render cases: style, scaling and placement per slot, hub, lift.
    static void Cases(Action<bool, string> check, List<string> report)
    {
        report.Add("");
        report.Add("case | level | triangles | end | k | cassette x-scale/k | cassette depth | slot top | hub scale | lift foot (x, z), angle | lift beyond basket (radius, top)");
        foreach (var c in Renders.Concat(new[] { new Case("t72-1900-30x100", 1.90, .50, 30, .100, .544, .326, false), new Case("t72-6000-49x125", 6, 1.2, 49, .125, .68, .408, false) }))
        {
            CarouselModelGeometry.Lift = true;
            var meshes = CarouselModelGeometry.Build(c.Diameter, c.Depth, c.Slots, c.Calibre, c.Projectile, c.Charge, c.Vertical, out var choice);
            var plan = CarouselModelGeometry.Fit(c.Diameter, c.Depth, c.Slots, c.Calibre, c.Projectile, c.Charge, c.Vertical, out _);
            check(choice.Level == CarouselModelLevel.Detailed && plan != null, c.Name + " uses the detailed model");
            if (plan == null) continue;
            var style = c.Vertical ? CarouselModelData.T80 : CarouselModelData.T72;
            check(choice.Style == (c.Vertical ? "T-80" : "T-72"), c.Name + ": verticalCharge chooses the T-80 parts, otherwise T-72");
            check(meshes.Select(m => m.Name).SequenceEqual(new[] { "mechanism", "drivetrim", "cassette", "projectile", "charge" }), c.Name + ": five material meshes");
            var cassette = Mesh(meshes, "Cassette"); var projectile = Mesh(meshes, "Projectile"); var charge = Mesh(meshes, "Charge");
            check(cassette.Triangles.Count == 3 * c.Slots * style.Cassette.Triangles && projectile.Triangles.Count == 3 * c.Slots * 118 &&
                  charge.Triangles.Count == 3 * c.Slots * 92 && Mesh(meshes, "DriveTrim").Triangles.Count == 3 * 640, c.Name + ": the parts of its style in every slot");
            SlotChecks(check, c, plan, cassette, projectile, charge, style);
            // Hub: calibre scale, inside the cassettes, not above the basket.
            check(plan.HubScale <= plan.K + Eps && plan.HubScale * plan.Style.HubRadius <= plan.End - plan.CassetteDepth - CarouselModelGeometry.HubMargin + Eps &&
                  plan.Floor + plan.HubScale * plan.Style.HubHeight <= c.Depth + Eps, c.Name + ": hub scaled by calibre, inside the projectile noses and the basket depth");
            check(Mesh(meshes, "DriveTrim").Vertices.All(v => Math.Sqrt(v.X * v.X + v.Z * v.Z) <= .1665 * plan.HubScale + 1e-6), c.Name + ": ring gear at the hub");
            // Lift (0.2.30), no cannon: at -Z, a slot under it, on the rim over the outer end, foot on the cassettes.
            var info = LiftPlacement(check, c, null);
            var origin = info.Origin;
            double k = plan.K;
            report.Add(Inv($"{c.Name} | {choice.Level} | {choice.Triangles} | {plan.End:0.000} | {k:0.000} | {plan.Sx / k:0.000} | {plan.CassetteDepth:0.000} | {info.SlotTop:0.000} of {c.Depth:0.00} | {plan.HubScale:0.000} | ({origin.X:0.000}, {origin.Z:0.000}), ring turned {Math.Round(plan.Rot * 180 / Math.PI, 2) + 0.0:0.00} deg | body {info.BodyBeyondRim:+0.000;-0.000} m beyond the rim, ram head rear {info.HeadBeyondRim:+0.000;-0.000} m, top {info.Top - c.Depth:+0.000;-0.000} m above the basket"));
        }
    }

    // Per slot: own wedge (no neighbour contact), inside the basket, nose inward, charge lying/upright, lengths.
    static void SlotChecks(Action<bool, string> check, Case c, CarouselModelGeometry.Plan plan, CarouselVisualMesh cassette, CarouselVisualMesh projectile, CarouselVisualMesh charge, CarouselStyleData style)
    {
        bool wedge = true, inside = true, nose = true, lengths = true, orientation = true, heights = true;
        double k = plan.K;
        for (int slot = 0; slot < c.Slots; slot++)
        {
            double angle = CarouselModelGeometry.SlotAngle(c.Slots, slot, plan.Rot);
            var cas = Block(cassette.Vertices, slot, style.Cassette.Vertices);
            var pro = Block(projectile.Vertices, slot, style.Projectile.Vertices);
            var chg = Block(charge.Vertices, slot, style.Charge.Vertices);
            foreach (var v in cas.Concat(pro).Concat(chg))
            {
                wedge &= InWedge(v, angle, c.Slots);
                inside &= Math.Sqrt(v.X * v.X + v.Z * v.Z) <= c.Diameter / 2 + Eps && v.Y <= c.Depth + Eps && v.Y >= plan.Floor - Eps;
            }
            var (proZeta, proX, proY) = Local(pro, angle);
            int tip = Array.IndexOf(proZeta, proZeta.Min());
            nose &= Math.Abs(proX[tip]) < 1e-6 && Near(proZeta.Min(), plan.End - (.028 * k + c.Projectile), 1e-6) && Near(proZeta.Max(), plan.End - .028 * k, 1e-6);
            lengths &= Near(proZeta.Max() - proZeta.Min(), c.Projectile, 1e-6) && Near(proX.Max() - proX.Min(), 2 * .0655 * k, 1e-5) && Near(proY.Max() - proY.Min(), (.1703 - .0457) * k, 1e-3 * k);
            var (chgZeta, chgX, chgY) = Local(chg, angle);
            double radial = chgZeta.Max() - chgZeta.Min(), height = chgY.Max() - chgY.Min();
            lengths &= chgX.Length == 0 || Near(chgX.Max() - chgX.Min(), .159 * k, 1e-3 * k);
            if (c.Vertical)
            {
                // Upright at the outer edge (its outer side within 0.03 calibre-scale of the slot outer edge).
                orientation &= height > 2 * radial && Near(height, c.Charge + .005 * k, 1e-5) && Near(radial, .159 * k, 1e-3 * k) && chgZeta.Max() >= plan.End - .03 * k;
                heights &= chgY.Min() >= proY.Max() - .001 * k;  // the upright charge stands on the shelf over the projectile base
            }
            else
            {
                orientation &= radial > 2 * height && Near(radial, c.Charge + .005 * k, 1e-5) && Near(height, .159 * k, 1e-3 * k);
                heights &= chgY.Min() > proY.Max();  // T-72: the charge lies above the projectile
            }
            var (casZeta, _, casY) = Local(cas, angle);
            heights &= casY.Min() <= proY.Min() && casY.Max() >= chgY.Max() - .01 * k && casZeta.Min() <= proZeta.Min() && casZeta.Max() >= proZeta.Max();
        }
        check(wedge, c.Name + ": every slot part stays in its own wedge with 1.5 mm margin (neighbours never touch)");
        check(inside, c.Name + ": slots inside the basket radius and depth, on the floor ring");
        check(nose, c.Name + ": projectile nose on the axis and pointing inward");
        check(lengths, c.Name + ": projectile length = projectile length, projectile and charge diameter scaled with the calibre");
        check(orientation, c.Name + (c.Vertical ? ": T-80 charge upright, height = charge length" : ": T-72 charge lying, length = charge length"));
        check(heights, c.Name + ": projectile and charge heights inside the cassette");
    }

    // 5. Visible rounds: per-slot projectile_xx/charge_xx in slot order, prefix counts per stored round.
    static void Rounds(Action<bool, string> check)
    {
        foreach (var vertical in new[] { false, true })
        foreach (var (n, visible) in new[] { (22, 22), (22, 7), (22, 1), (22, 0), (31, 31), (31, 12) })
        {
            var c = vertical ? new Case("rounds", 2.4, .8, n, .125, .68, .408, true) : new Case("rounds", 2.4, .6, n, .125, .68, .408, false);
            var meshes = CarouselModelGeometry.Build(c.Diameter, c.Depth, n, c.Calibre, c.Projectile, c.Charge, vertical, out var choice, visible);
            var style = vertical ? CarouselModelData.T80 : CarouselModelData.T72;
            check(choice.Level == CarouselModelLevel.Detailed && Mesh(meshes, "Cassette").Triangles.Count == 3 * n * style.Cassette.Triangles,
                $"{n} slots, {visible} visible: every slot keeps its cassette");
            if (visible == 0) { check(meshes.All(m => m.Material is not ("Projectile" or "Charge")), "no visible rounds: no round meshes"); continue; }
            foreach (var (material, part) in new[] { ("Projectile", style.Projectile), ("Charge", style.Charge) })
            {
                var mesh = Mesh(meshes, material);
                var prefix = mesh.SlotPrefixIndexCounts;
                bool ok = prefix.Length == visible + 1 && prefix[0] == 0 && prefix[^1] == mesh.Triangles.Count;
                for (int slot = 0; ok && slot < visible; slot++)
                {
                    ok &= prefix[slot + 1] - prefix[slot] == 3 * part.Triangles;
                    // Every corner of the triangles of stored round i lies in the wedge of slot i.
                    var expected = CarouselModelGeometry.SlotAngle(n, slot, CarouselModelGeometry.RingRotation(n, Math.PI));
                    ok &= mesh.Triangles.Skip(prefix[slot]).Take(prefix[slot + 1] - prefix[slot]).All(i => InWedge(mesh.Vertices[i], expected, n));
                }
                check(ok, $"{material} {n}/{visible}: prefix per stored round, slot i in the wedge of slot i");
            }
            // Slot angles of 0.2.28 (also those of the native damage mesh): round i of the old model lies on the centre line of slot i.
            var old = CarouselVisualGeometry.Build(c.Diameter, c.Depth, n, c.Calibre, c.Projectile, c.Charge, vertical, visible).First(m => m.Material == "Projectile");
            bool aligned = true;
            for (int slot = 0; slot < visible; slot++)
            {
                var block = old.Triangles.Skip(old.SlotPrefixIndexCounts[slot]).Take(old.SlotPrefixIndexCounts[slot + 1] - old.SlotPrefixIndexCounts[slot]).Select(i => old.Vertices[i]).ToList();
                double angle = Math.Atan2(block.Average(v => v.X), block.Average(v => v.Z));
                aligned &= Math.Abs(Math.IEEERemainder(angle - CarouselModelGeometry.SlotAngle(n, slot), 2 * Math.PI)) < 1e-9;
            }
            check(aligned, $"{n} slots: the same slot angles as the 0.2.28 model and the native damage mesh");
            // 0.2.30: without a cannon the lift is at -Z and the ring is turned (half a pitch at most, none for an even count) so that a slot stands under it.
            double rot = CarouselModelGeometry.RingRotation(n, Math.PI);
            check(Math.Abs(rot) <= Math.PI / n + 1e-12 && (n % 2 == 0 ? Near(rot, 0, 1e-12) : Near(Math.Abs(rot), Math.PI / n, 1e-12)) &&
                  Near(Math.IEEERemainder(CarouselModelGeometry.SlotAngle(n, CarouselModelGeometry.LiftSlot(n, Math.PI), rot) - Math.PI, 2 * Math.PI), 0, 1e-9),
                $"{n} slots: a slot stands under the lift at -Z (ring turned half a pitch only with an odd count)");
        }
        // No propellant: empty charge meshes, as in 0.2.28.
        var zero = CarouselModelGeometry.Build(2.5, .8, 16, .125, .375, 0, false, out var zc);
        check(zc.Level == CarouselModelLevel.Detailed && Mesh(zero, "Charge").Triangles.Count == 0 && Mesh(zero, "Charge").SlotPrefixIndexCounts.Length == 17,
            "zero native propellant never creates decorative charges");
    }

    // 6. Triangle budget: totals per style for 22, 28, 40 and 64 slots; above 25000 simple cassettes with the new hub.
    static void Budget(Action<bool, string> check, List<string> report)
    {
        report.Add("");
        report.Add("triangles (6 m basket, 1.2 m deep, 125 mm, 680 + 408 mm; lift on / off) | 22 | 28 | 40 | 64");
        foreach (var vertical in new[] { false, true })
        {
            var style = vertical ? CarouselModelData.T80 : CarouselModelData.T72;
            int perSlot = style.Cassette.Triangles + 118 + 92, fixedPart = 768 + 1484 + style.Lift.Triangles;
            var row = new List<string>();
            foreach (var n in new[] { 22, 28, 40, 64 })
            {
                CarouselModelGeometry.Lift = true;
                var meshes = CarouselModelGeometry.Build(6, 1.2, n, .125, .68, .408, vertical, out var choice);
                CarouselModelGeometry.Lift = false;
                CarouselModelGeometry.Build(6, 1.2, n, .125, .68, .408, vertical, out var noLift);
                CarouselModelGeometry.Lift = true;
                int full = fixedPart + n * perSlot;
                if (full <= CarouselModelGeometry.TriangleBudget)
                    check(choice.Level == CarouselModelLevel.Detailed && choice.Triangles == full && Triangles(meshes) == full, $"{choice.Style} {n} slots: {full} triangles, detailed");
                else
                    check(choice.Level == CarouselModelLevel.Reduced && choice.Triangles <= CarouselModelGeometry.TriangleBudget && choice.Info!.Contains(Inv($"{full} triangles")) &&
                          Mesh(meshes, "DriveTrim").Triangles.Count == 3 * 640 && Mesh(meshes, "Cassette").Triangles.Count == 3 * n * 36,
                        $"{choice.Style} {n} slots: {full} triangles is over budget, simple cassettes with the detailed hub");
                row.Add(Inv($"{(choice.Level == CarouselModelLevel.Detailed ? full : choice.Triangles)}{(choice.Level == CarouselModelLevel.Reduced ? $" (reduced; detailed {full})" : "")} / {noLift.Triangles}"));
            }
            report.Add($"{(vertical ? "T-80" : "T-72")} | {string.Join(" | ", row)}");
            // The switch point: the largest detailed slot count.
            int last = (CarouselModelGeometry.TriangleBudget - fixedPart) / perSlot;
            CarouselModelGeometry.Build(6, 1.2, last, .125, .68, .408, vertical, out var a);
            CarouselModelGeometry.Build(6, 1.2, last + 1, .125, .68, .408, vertical, out var b);
            check(a.Level == CarouselModelLevel.Detailed && b.Level == CarouselModelLevel.Reduced, $"{a.Style}: detailed up to {last} slots, reduced from {last + 1}");
            report.Add(Inv($"{a.Style}: detailed up to {last} slots ({a.Triangles} triangles); from {last + 1} slots simple cassettes with the detailed hub and lift ({b.Triangles})"));
        }
    }

    // 7. The native-valid grid of the 0.2.28 visual test: detailed/reduced inside the basket, otherwise exactly 0.2.28.
    static void Sweep(Action<bool, string> check, List<string> report)
    {
        CarouselModelGeometry.Lift = false;
        var levels = new Dictionary<string, int>();
        var reasons = new Dictionary<string, int>();
        foreach (var calibre in new[] { 15d, 25d, 30d, 50d, 75d, 100d, 120d, 125d, 152d, 250d })
        foreach (var propellant in new[] { 0d, 30d, 408d, 700d, 1200d, 2000d })
        foreach (var diameter in new[] { 800d, 1500d, 2000d, 2500d, 2716d, 4000d, 6000d })
        foreach (var depth in new[] { 250d, 453d, 800d, 1200d, 2500d })
        foreach (var layout in new[] { CarouselLayout.HorizontalCassette, CarouselLayout.VerticalCharge })
        {
            var size = CarouselGeometry.Calculate(new[] { new Segment(diameter, depth) }, calibre, calibre * 3 + propellant, 40, layout);
            if (size.Capacity == 0) continue;
            bool vertical = layout == CarouselLayout.VerticalCharge;
            double d = diameter / 1000, h = depth / 1000, cal = calibre / 1000, p = Math.Max(.001, size.StoredProjectileMm / 1000), q = Math.Max(0, size.StoredChargeMm / 1000);
            string name = Inv($"{calibre} mm, {propellant} mm propellant, {diameter}x{depth} mm, {layout}, {size.Capacity} slots");
            try
            {
                var meshes = CarouselModelGeometry.Build(d, h, size.Capacity, cal, p, q, vertical, out var choice);
                levels[choice.Level.ToString()] = levels.GetValueOrDefault(choice.Level.ToString()) + 1;
                if (choice.Level == CarouselModelLevel.Simple)
                {
                    var key = choice.Reason.Contains("deep") ? "too shallow" : choice.Reason.Contains("wedge too narrow") ? "wedge too narrow for the cassette" :
                        choice.Reason.Contains("rounds") ? "rounds wider than the wedge" : choice.Reason.Contains("hub") ? "no room for the hub" : choice.Reason;
                    reasons[key] = reasons.GetValueOrDefault(key) + 1;
                    check(Same(meshes, CarouselVisualGeometry.Build(d, h, size.Capacity, cal, p, q, vertical)), name + ": fallback is exactly the 0.2.28 model");
                    continue;
                }
                check(meshes.Count <= 5 && meshes.All(m => m.Vertices.Count < 65536 && m.Triangles.All(i => i >= 0 && i < m.Vertices.Count)), name + ": at most five meshes, 16-bit indices valid");
                check(meshes.SelectMany(m => m.Vertices).All(v => Math.Sqrt(v.X * v.X + v.Z * v.Z) <= d / 2 + Eps && v.Y <= h + Eps && v.Y >= -Eps),
                    name + ": everything inside the basket radius and depth");
                check(meshes.SelectMany(m => m.Normals).All(n => double.IsFinite(n.X) && Math.Abs(n.Length - 1) < 1e-6), name + ": finite unit normals");
                check(choice.Triangles <= CarouselModelGeometry.TriangleBudget, name + ": within the triangle budget");
                foreach (var mesh in meshes.Where(m => m.SlotPrefixIndexCounts.Length > 0))
                    check(mesh.SlotPrefixIndexCounts.Length == size.Capacity + 1 && mesh.SlotPrefixIndexCounts[^1] == mesh.Triangles.Count, name + ": prefix covers the native stock");
                if (choice.Level == CarouselModelLevel.Detailed)
                {
                    var style = vertical ? CarouselModelData.T80 : CarouselModelData.T72;
                    bool wedge = true;
                    foreach (var (material, part) in new[] { ("Cassette", style.Cassette), ("Projectile", style.Projectile), ("Charge", style.Charge) })
                    {
                        var verts = Mesh(meshes, material).Vertices;
                        for (int slot = 0; slot * part.Vertices < verts.Count; slot++)
                            foreach (var v in Block(verts, slot, part.Vertices)) wedge &= InWedge(v, CarouselModelGeometry.SlotAngle(size.Capacity, slot, CarouselModelGeometry.RingRotation(size.Capacity, Math.PI)), size.Capacity);
                    }
                    check(wedge, name + ": no neighbouring slots touch");
                }
            }
            catch (Exception ex) { throw new Exception("Carousel model rejected native-valid " + name, ex); }
        }
        CarouselModelGeometry.Lift = true;
        report.Add("");
        report.Add("native-valid grid of the 0.2.28 visual test (lift off): " + string.Join(", ", levels.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}")));
        report.Add("fallback reasons: " + string.Join("; ", reasons.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}")));
    }

    // 8. The render cases as OBJ (game coordinates, one group per material mesh) for the Blender check.
    static void Exports(Action<bool, string> check, List<string> report)
    {
        Directory.CreateDirectory("logs/0.2.32-render");
        // Render cases of the brief (0.2.30): T-72 with the load point at 30 degrees and 1.1 m, T-80 (31 slots, 100 mm) at -Z and 1.2 m.
        var renderTargets = new Dictionary<string, CarouselLiftTarget>
        {
            ["t72-2050-22x125"] = new(.8 * Math.Sin(Math.PI / 6), 1.1, .8 * Math.Cos(Math.PI / 6)),
            ["t80-1900-31x100"] = new(0, 1.2, -.8),
        };
        foreach (var c in Renders)
        {
            CarouselModelGeometry.Lift = true;
            CarouselLiftTarget? target = renderTargets.TryGetValue(c.Name, out var found) ? found : null;
            var meshes = CarouselModelGeometry.Build(c.Diameter, c.Depth, c.Slots, c.Calibre, c.Projectile, c.Charge, c.Vertical, out var choice, -1, target);
            var lines = new List<string> { Inv($"# CarouselModelGeometry.Build({c.Diameter}, {c.Depth}, {c.Slots}, {c.Calibre}, {c.Projectile}, {c.Charge}, {c.Vertical}): {choice.Level} {choice.Triangles} triangles"),
                Inv($"# basket {c.Diameter / 2} {c.Depth}") };
            if (target is { } tt) lines.Add(Inv($"# target {tt.X} {tt.Y} {tt.Z}"));
            int start = 1;
            foreach (var m in meshes)
            {
                lines.Add("g " + m.Name);
                lines.AddRange(m.Vertices.Select(v => Inv($"v {v.X:0.######} {v.Y:0.######} {v.Z:0.######}")));
                lines.AddRange(m.Normals.Select(n => Inv($"vn {n.X:0.######} {n.Y:0.######} {n.Z:0.######}")));
                for (int i = 0; i < m.Triangles.Count; i += 3)
                    lines.Add(Inv($"f {m.Triangles[i] + start}//{m.Triangles[i] + start} {m.Triangles[i + 1] + start}//{m.Triangles[i + 1] + start} {m.Triangles[i + 2] + start}//{m.Triangles[i + 2] + start}"));
                start += m.Vertices.Count;
            }
            File.WriteAllLines($"logs/0.2.32-render/{c.Name}.obj", lines);
            check(choice.Level == CarouselModelLevel.Detailed, c.Name + " exported for the render check");
        }
        // 0.2.31: the T-72 render case with the ring after 0 and 3 shots (stock 22 and 19), cassettes and rounds turned, fired rounds hidden.
        var t72 = Renders[0]; var t72Target = renderTargets[t72.Name];
        foreach (var (label, stock) in new[] { ("full", 22), ("after3", 19) })
        {
            var posed = Posed(t72, t72Target, stock);
            var lines = new List<string> { Inv($"# ring pose stock {stock} of {t72.Slots}"), Inv($"# basket {t72.Diameter / 2} {t72.Depth}"), Inv($"# target {t72Target.X} {t72Target.Y} {t72Target.Z}") };
            int start = 1;
            foreach (var m in posed)
            {
                lines.Add("g " + m.Name);
                lines.AddRange(m.Vertices.Select(v => Inv($"v {v.X:0.######} {v.Y:0.######} {v.Z:0.######}")));
                lines.AddRange(m.Normals.Select(n => Inv($"vn {n.X:0.######} {n.Y:0.######} {n.Z:0.######}")));
                for (int i = 0; i < m.Triangles.Count; i += 3)
                    lines.Add(Inv($"f {m.Triangles[i] + start}//{m.Triangles[i] + start} {m.Triangles[i + 1] + start}//{m.Triangles[i + 1] + start} {m.Triangles[i + 2] + start}//{m.Triangles[i + 2] + start}"));
                start += m.Vertices.Count;
            }
            File.WriteAllLines($"logs/0.2.32-render/ring-{label}.obj", lines);
        }
        report.Add("");
        report.Add("render OBJs: logs/0.2.32-render/" + string.Join(", ", Renders.Select(r => r.Name + ".obj")) + ", ring-full.obj, ring-after3.obj");
    }

    // ---- 0.2.31: the ring turns one slot per shot
    // The display of CarouselVisuals.UpdateStock on plain meshes: rounds of the full slots only (from (L + f) mod n), the ring parts turned about Y.
    static List<CarouselVisualMesh> Posed(Case c, CarouselLiftTarget? target, int stock)
    {
        CarouselModelGeometry.Lift = true;
        var meshes = CarouselModelGeometry.Build(c.Diameter, c.Depth, c.Slots, c.Calibre, c.Projectile, c.Charge, c.Vertical, out _, -1, target);
        int n = c.Slots, L = CarouselModelGeometry.LiftSlotFor(n, target);
        double turn = CarouselModelGeometry.RingTurn(n, stock), cos = Math.Cos(turn), sin = Math.Sin(turn);
        var ranges = CarouselModelGeometry.RingVisibleRanges(n, L, stock);
        var output = new List<CarouselVisualMesh>();
        foreach (var m in meshes)
        {
            var copy = new CarouselVisualMesh(m.Name, m.Material, m.Slot);
            bool turns = CarouselModelGeometry.RotatesWithRing(m.Material);
            copy.Vertices.AddRange(m.Vertices.Select(v => turns ? new CarouselVisualVector(v.X * cos + v.Z * sin, v.Y, -v.X * sin + v.Z * cos) : v));
            copy.Normals.AddRange(m.Normals.Select(v => turns ? new CarouselVisualVector(v.X * cos + v.Z * sin, v.Y, -v.X * sin + v.Z * cos) : v));
            if (m.SlotPrefixIndexCounts.Length > n)
                foreach (var (start, count) in ranges)
                    copy.Triangles.AddRange(m.Triangles.Skip(m.SlotPrefixIndexCounts[start]).Take(m.SlotPrefixIndexCounts[start + count] - m.SlotPrefixIndexCounts[start]));
            else copy.Triangles.AddRange(m.Triangles);
            output.Add(copy);
        }
        return output;
    }

    static void RingTests(Action<bool, string> check, List<string> report)
    {
        // Pure rule: empty set, ring turn, full slot under the lift, for many n, s and L.
        int cases = 0;
        foreach (var n in new[] { 1, 2, 3, 7, 22, 31, 40, 64 })
            for (int L = 0; L < n; L += Math.Max(1, n / 5))
                for (int s = 0; s <= n; s++)
                {
                    int f = n - s;
                    var empty = Enumerable.Range(0, n).Where(i => CarouselModelGeometry.RingEmpty(n, L, s, i)).ToList();
                    var expected = Enumerable.Range(0, n).Where(i => (((i - L) % n) + n) % n < f).ToList();
                    var ranges = CarouselModelGeometry.RingVisibleRanges(n, L, s);
                    var full = ranges.SelectMany(r => Enumerable.Range(r.Start, r.Count)).OrderBy(i => i).ToList();
                    int under = CarouselModelGeometry.RingSlotUnderLift(n, L, s);
                    // 0.2.32 (was f): the ring turns f - 1 slots; the used slot (L + f - 1) is empty under the lift at f >= 1, slot L is full at f = 0.
                    bool ok = empty.SequenceEqual(expected) && empty.Count == f && full.Count == s && full.SequenceEqual(Enumerable.Range(0, n).Except(empty)) &&
                              ranges.Count <= 2 && ranges.All(r => r.Start >= 0 && r.Count > 0 && r.Start + r.Count <= n) && under == (f >= 1 ? (L + f - 1) % n : L) &&
                              (f >= 1 ? CarouselModelGeometry.RingEmpty(n, L, s, under) : !CarouselModelGeometry.RingEmpty(n, L, s, under)) &&
                              Near(CarouselModelGeometry.RingTurn(n, s), -Math.Max(0, f - 1) * 2 * Math.PI / n, 1e-12);
                    if (!ok) check(false, $"ring rule n {n}, L {L}, s {s}");
                    // The turned ring puts the slot (L + f) at the angle of slot L (the lift): angle + turn = angle of L, also modulo a full turn.
                    ok &= Near(Math.IEEERemainder(CarouselModelGeometry.SlotAngle(n, under) + CarouselModelGeometry.RingTurn(n, s) - CarouselModelGeometry.SlotAngle(n, L), 2 * Math.PI), 0, 1e-9);
                    if (!ok) check(false, $"ring turn n {n}, L {L}, s {s}");
                    cases++;
                }
        check(cases > 300, $"ring rule checked for {cases} combinations of n, L and s: empty set, empty used slot under the lift at f >= 1, full at f = 0, turn f - 1");
        // Refilling turns the ring back: the same state for the same stock whatever came before; stock up lowers f and the turn.
        foreach (var n in new[] { 3, 22, 31 })
        {
            bool back = true;
            for (int s = 0; s < n; s++)
            {
                back &= CarouselModelGeometry.RingFired(n, s + 1) == CarouselModelGeometry.RingFired(n, s) - 1;
                back &= Near(CarouselModelGeometry.RingTurn(n, s + 1) - CarouselModelGeometry.RingTurn(n, s), s == n - 1 ? 0 : 2 * Math.PI / n, 1e-12);
                back &= Enumerable.Range(0, n).Count(i => CarouselModelGeometry.RingEmpty(n, 1 % n, s + 1, i)) == Enumerable.Range(0, n).Count(i => CarouselModelGeometry.RingEmpty(n, 1 % n, s, i)) - 1;
            }
            check(back, $"{n} slots: a refilled round lowers f by one, turns the ring one slot back and fills one slot");
            check(CarouselModelGeometry.RingTurn(n, n) == 0 && CarouselModelGeometry.RingTurn(n, n - 1) == 0 && Near(CarouselModelGeometry.RingTurn(n, 0), -(n - 1) * 2 * Math.PI / n, 1e-12),
                $"{n} slots: full stock and the first shot at the base position, an empty ring f - 1 = n - 1 slots on");
        }
        // n = 1 and the stock limits.
        check(CarouselModelGeometry.RingFired(1, 1) == 0 && CarouselModelGeometry.RingFired(1, 0) == 1 && CarouselModelGeometry.RingTurn(1, 1) == 0 && CarouselModelGeometry.RingTurn(1, 0) == 0 &&
              CarouselModelGeometry.RingSlotUnderLift(1, 0, 1) == 0 && CarouselModelGeometry.RingVisibleRanges(1, 0, 1).SequenceEqual(new[] { (0, 1) }) &&
              CarouselModelGeometry.RingVisibleRanges(1, 0, 0).Count == 0 && CarouselModelGeometry.RingEmpty(1, 0, 0, 0) && !CarouselModelGeometry.RingEmpty(1, 0, 1, 0), "n = 1: one slot, no turn, empty at stock 0");
        check(CarouselModelGeometry.RingFired(22, -5) == 22 && CarouselModelGeometry.RingFired(22, 99) == 0 && CarouselModelGeometry.RingFired(22, int.MinValue) == 22 && CarouselModelGeometry.RingFired(22, int.MaxValue) == 0 &&
              CarouselModelGeometry.RingVisibleRanges(22, 5, 99).Sum(r => r.Count) == 22 &&
              CarouselModelGeometry.RingVisibleRanges(22, 5, -3).Count == 0 && CarouselModelGeometry.RingTurn(22, 99) == 0 && CarouselModelGeometry.RingTurn(22, -3) == CarouselModelGeometry.RingTurn(22, 0),
            "stock outside 0..n is limited: below 0 empty, above n full");
        check(CarouselModelGeometry.RingFired(0, 5) == 0 && CarouselModelGeometry.RingVisibleRanges(0, 0, 5).Count == 0 && CarouselModelGeometry.RingTurn(0, 5) == 0 && !CarouselModelGeometry.RingEmpty(0, 0, 0, 0), "no slots: nothing to show, no turn");
        check(CarouselModelGeometry.RingSlotUnderLift(22, -3, 10) == 8 && CarouselModelGeometry.RingSlotUnderLift(22, 25, 10) == 14 && CarouselModelGeometry.RingSlotUnderLift(22, 25, 22) == 3, "lift slot outside 0..n-1 is wrapped");

        // On the real meshes (T-72 22 slots, load point at 30 degrees; T-80 31 slots at -Z): after f shots the rounds of exactly the full slots are drawn, the
        // slot under the lift holds a round at the lift angle once the ring is turned, the lift (mechanism mesh) does not turn, the base meshes are unchanged.
        var t72 = Renders[0]; var t80 = new Case("t80-1900-31x100", 1.90, .70, 31, .100, .600, .400, true);
        var targets = new[] { (t72, (CarouselLiftTarget?)new CarouselLiftTarget(.4, 1.1, .693)), (t80, new CarouselLiftTarget(0, 1.2, -.8)), (t72, null) };
        foreach (var (c, target) in targets)
        {
            int n = c.Slots, L = CarouselModelGeometry.LiftSlotFor(n, target);
            var plan = CarouselModelGeometry.Fit(c.Diameter, c.Depth, n, c.Calibre, c.Projectile, c.Charge, c.Vertical, out _, target)!;
            check(L == CarouselModelGeometry.LiftSlot(n, plan.LiftAngle), c.Name + ": L is the 0.2.30 lift slot");
            var baseMeshes = CarouselModelGeometry.Build(c.Diameter, c.Depth, n, c.Calibre, c.Projectile, c.Charge, c.Vertical, out _, -1, target);
            var style = c.Vertical ? CarouselModelData.T80 : CarouselModelData.T72;
            foreach (int s in new[] { n, n - 1, n - 3, n / 2, 2, 1, 0 })
            {
                var posed = Posed(c, target, s);
                var projectile = Mesh(posed, "Projectile"); var baseProjectile = Mesh(baseMeshes, "Projectile");
                int perSlot = style.Projectile.Vertices;
                var drawn = projectile.Triangles.Select(i => i / perSlot).Distinct().OrderBy(i => i).ToList();
                var wanted = Enumerable.Range(0, n).Where(i => !CarouselModelGeometry.RingEmpty(n, L, s, i)).ToList();
                check(drawn.SequenceEqual(wanted) && projectile.Triangles.Count == 3 * s * 118, $"{c.Name} stock {s}: rounds drawn in exactly the full slots");
                check(Mesh(posed, "Cassette").Triangles.Count == Mesh(baseMeshes, "Cassette").Triangles.Count, $"{c.Name} stock {s}: every cassette stays, only rounds disappear");
                {
                    // 0.2.32: the slot under the lift stands exactly at the lift angle (its cassette); it is empty at f >= 1 and full at f = 0.
                    int under = CarouselModelGeometry.RingSlotUnderLift(n, L, s);
                    var cas = Block(Mesh(posed, "Cassette").Vertices, under, style.Cassette.Vertices);
                    double a = Math.Atan2(cas.Average(v => v.X), cas.Average(v => v.Z));
                    bool underFull = drawn.Contains(under);
                    check(Near(Math.IEEERemainder(a - plan.LiftAngle, 2 * Math.PI), 0, 1e-6) && underFull == (s == n),
                        $"{c.Name} stock {s}: the cassette under the lift stands exactly at the lift angle, empty after a shot (f >= 1), full at a full stock");
                    if (s == n)
                    {
                        var block = projectile.Vertices.Skip(under * perSlot).Take(perSlot).ToList();
                        check(Near(Math.IEEERemainder(Math.Atan2(block.Average(v => v.X), block.Average(v => v.Z)) - plan.LiftAngle, 2 * Math.PI), 0, 1e-6), $"{c.Name}: full stock, the round under the lift is at the lift angle");
                    }
                }
                // The lift does not turn: the mechanism mesh is the same as without the ring turn; only cassette, projectile and charge turn.
                var mech = Mesh(posed, "Mechanism").Vertices; var baseMech = Mesh(baseMeshes, "Mechanism").Vertices;
                check(mech.Count == baseMech.Count && mech.Zip(baseMech).All(z => (z.First - z.Second).Length < 1e-12) &&
                      Mesh(posed, "DriveTrim").Vertices.Zip(Mesh(baseMeshes, "DriveTrim").Vertices).All(z => (z.First - z.Second).Length < 1e-12), $"{c.Name} stock {s}: lift, floor ring and hub do not turn");
            }
            // The lift is in the Mechanism mesh and nothing in the mechanism turns with the ring.
            check(!CarouselModelGeometry.RotatesWithRing("Mechanism") && !CarouselModelGeometry.RotatesWithRing("DriveTrim") && CarouselModelGeometry.RotatesWithRing("Cassette") &&
                  CarouselModelGeometry.RotatesWithRing("Projectile") && CarouselModelGeometry.RotatesWithRing("Charge"), "only cassettes, projectiles and charges turn with the ring");
        }
        // Reduced and Simple models use the same material names and per-slot prefix counts, so the same rule applies.
        var reduced = CarouselModelGeometry.Build(6, 1.2, 64, .125, .68, .408, false, out var rc);
        var simple = CarouselVisualGeometry.Build(2.4, .8, 16, .125, .68, .408, false);
        check(rc.Level == CarouselModelLevel.Reduced && reduced.Where(m => m.Material is "Projectile" or "Charge").All(m => m.SlotPrefixIndexCounts.Length == 65) &&
              simple.Where(m => m.Material is "Projectile" or "Charge").All(m => m.SlotPrefixIndexCounts.Length == 17) &&
              simple.Select(m => m.Material).Distinct().All(m => m is "Mechanism" or "DriveTrim" || CarouselModelGeometry.RotatesWithRing(m)),
            "reduced and Simple models: same material names and per-slot prefix counts, so they turn like the detailed model");
        // Source guards: one ring object, turned only when the stock changes; nothing per frame; no new hook.
        var visuals = File.ReadAllText(Path.Combine(FindRoot()!, "CarouselVisuals.cs"));
        check(visuals.Contains("new GameObject(\"Carousel ring\")") && visuals.Contains("localRotation=Quaternion.Euler") && visuals.Contains("RotatesWithRing(data.Material)") &&
              visuals.Contains("if(remaining==visual.Displayed) return;") && visuals.Split("[HarmonyPostfix").Length == 5 && !visuals.Contains("LateUpdate") && !visuals.Contains("Update()"),
            "ring object turned only on a stock change; no hook or per-frame code added to CarouselVisuals");
        report.Add("");
        report.Add($"ring: rule checked for {cases} combinations; T-72 22 slots after 3 shots: ring turned {CarouselModelGeometry.RingTurn(22, 19) * 180 / Math.PI:0.00} deg (f - 1 = 2 slots), empty slot {CarouselModelGeometry.RingSlotUnderLift(22, CarouselModelGeometry.LiftSlotFor(22, new CarouselLiftTarget(.4, 1.1, .693)), 19)} under the lift");
    }

    // ---- 0.2.34: the T-80BVM depth window. Blueprint "T-80BVM (proporties)": basket 2230 mm wide, 125 mm cannon, mixed loadout (largest carried:
    // charge 900 mm = propellant, projectile 750 mm assumed so that 22 slots come out as in the game log), load point 0.283 m above the basket top
    // (blueprint sum 0.299 m; 0.283 gives the user's 1216). Required depth 150 + 18 + 125 + 900 = 1193 mm; the factor wanted drops under 0.3 below 1216 mm.
    static void WindowTests(Action<bool, string> check, List<string> report)
    {
        const double Charge = .900, Projectile = .750, Offset = .283;
        int rows = 0; double worstRamOver = double.MinValue, worstTopOver = double.MinValue;
        var wanted = new List<string>();
        for (int depth = 1150; depth <= 1260; depth++)
        {
            double d = depth / 1000.0;
            var size = CarouselGeometry.Calculate(new[] { new Segment(2230, depth) }, 125, (Projectile + Charge) * 1000, 25, CarouselLayout.VerticalCharge, Projectile * 1000, Charge * 1000);
            var target = new CarouselLiftTarget(0, d + Offset, -1.0);
            if (depth < 1193)
            {
                // Too shallow: no slots, the Simple model, no lift and no ring parts.
                var simple = CarouselModelGeometry.Build(2.23, d, size.Capacity, .125, Projectile, Charge, true, out var sc, -1, target);
                check(size.Capacity == 0 && sc.Level == CarouselModelLevel.Simple && simple.All(m => m.Material is "Mechanism" or "DriveTrim") && simple.SelectMany(m => m.Vertices).Max(v => v.Y) < .2,
                    $"depth {depth}: too shallow, Simple model, no lift above the basket");
                continue;
            }
            var c = new Case($"t80bvm-{depth}", 2.23, d, size.Capacity, .125, Projectile, Charge, true);
            check(size.Capacity == 22 && size.RequiredDepthMm == 1193, $"depth {depth}: 22 slots, required depth 1193 mm");
            var info = LiftPlacement(check, c, target);
            // The lift ends near the breech, never far above it: the ram at most 5 cm, the lift top (ram head) at most 20 cm above the load point.
            worstRamOver = Math.Max(worstRamOver, info.RamY - target.Y); worstTopOver = Math.Max(worstTopOver, info.Top - target.Y);
            check(info.RamY - target.Y <= .05 && info.Top - target.Y <= .20 && info.RamY - target.Y >= -1e-6, $"depth {depth}: ram {info.RamY:0.000} m and lift top {info.Top:0.000} m at the load point {target.Y:0.000} m, not far above the basket");
            check(info.HeightFactor >= .3 - 1e-12 && info.HeightFactor <= 1.5 + 1e-12, $"depth {depth}: height factor {info.HeightFactor:0.000} inside 0.3..1.5");
            if (depth is 1193 or 1215 or 1216 or 1217 or 1260) wanted.Add(Inv($"{depth}: factor {info.HeightFactor:0.000}, ram {info.RamY:0.000} m, target {target.Y:0.000} m, lift top {info.Top:0.000} m"));
            rows++;
        }
        check(rows == 68, "window sweep: every depth from 1193 to 1260 checked");
        // The turning points: 1193 (carousel fits) and 1216 (factor wanted reaches 0.3), with the formula of the game.
        var s1192 = CarouselGeometry.Calculate(new[] { new Segment(2230, 1192) }, 125, 1650, 25, CarouselLayout.VerticalCharge, 750, 900);
        var s1193 = CarouselGeometry.Calculate(new[] { new Segment(2230, 1193) }, 125, 1650, 25, CarouselLayout.VerticalCharge, 750, 900);
        var style = CarouselModelGeometry.Info(true);
        double slotTop = CarouselModelGeometry.Fit(2.23, 1.193, 22, .125, Projectile, Charge, true, out _)!.SlotTop;
        double wanted1215 = (1.215 + Offset - slotTop) / style.LiftRamY, wanted1216 = (1.216 + Offset - slotTop) / style.LiftRamY;
        check(s1192.Capacity == 0 && s1193.Capacity == 22 && wanted1215 < .3 && wanted1216 >= .3, "turning points: 1193 mm (too shallow below, 22 slots from there) and 1216 mm (factor wanted from 0.2995 to 0.3004)");
        // Old behaviour for the record: 1.0 (default height, far above) in the window, shown by the base values; new: the limit 0.3.
        check(CarouselModelGeometry.HeightFactorFor(1.215 + Offset - slotTop, 1, style.LiftRamY) == .3 && Near(CarouselModelGeometry.HeightFactorFor(1.216 + Offset - slotTop, 1, style.LiftRamY), wanted1216, 1e-12), "window depth: limit 0.3 instead of the default height 1.0");
        // Capacity 0 in the proof line: no ring parts at all.
        var shallow = CarouselModelGeometry.Build(2.23, 1.19, 0, .125, Projectile, Charge, true, out var shallowChoice);
        check(shallowChoice.Level == CarouselModelLevel.Simple && shallow.All(m => m.Slot < 0 && !CarouselModelGeometry.RotatesWithRing(m.Material)), "capacity 0: no ring parts (path None)");
        var visuals = File.ReadAllText(Path.Combine(FindRoot()!, "CarouselVisuals.cs"));
        check(visuals.Contains("capacity reason \\\"{visual.CapacityReason}\\\"") && visuals.Contains("lift top {visual.LiftTop:0.000} / basket top {visual.BasketTop:0.000}") && visuals.Contains("visual.Path=CarouselRingPartPath.None"),
            "proof line: capacity reason, model level, lift top / basket top; path None without Info line");
        report.Add("");
        report.Add(Inv($"T-80BVM depth window: ram at most {worstRamOver * 100:0.0} cm and lift top at most {worstTopOver * 100:0.0} cm above the load point for 1193..1260 mm; ") + string.Join("; ", wanted));
    }

    // ---- 0.2.33: ring parts in Play (moved, cloned, baked) and the lift target cache
    // Minimal 3x3 + translation for the Root frame: world = rootPos + R_y(phi) * (scale * local). Used to check the pose maths on plain numbers.
    sealed record Frame(double Px, double Py, double Pz, double Phi, double Sx, double Sy, double Sz)
    {
        public (double X, double Y, double Z) ToWorld(double x, double y, double z)
        {
            double a = x * Sx, b = y * Sy, c = z * Sz, cos = Math.Cos(Phi), sin = Math.Sin(Phi);
            return (Px + a * cos + c * sin, Py + b, Pz - a * sin + c * cos);
        }
        public (double X, double Y, double Z) ToLocal(double wx, double wy, double wz)
        {
            double dx = wx - Px, dz = wz - Pz, cos = Math.Cos(Phi), sin = Math.Sin(Phi);
            return ((dx * cos - dz * sin) / Sx, (wy - Py) / Sy, (dx * sin + dz * cos) / Sz);
        }
    }

    static void PartTests(Action<bool, string> check, List<string> report)
    {
        // Pose maths: a point of a ring part, turned by RotateAboutY in the Root frame from its base pose, lands where the ring transform puts it
        // (ring = child of Root turned about Y): same world point, with uniform and non-uniform Root scale and a Root that is itself turned and moved.
        int points = 0;
        foreach (var frame in new[] { new Frame(0, 0, 0, 0, 1, 1, 1), new Frame(1.5, .3, -2, 0, .8, .8, .8), new Frame(-3, 1, 4, .7, 1 / 1.25, 1 / 0.6, 1 / 1.25), new Frame(.2, -.4, .9, -2.1, 2, .5, 2) })
            foreach (var theta in new[] { 0, .1, -.285, Math.PI / 2, -3.0, 2 * Math.PI })
                foreach (var (x, y, z) in new[] { (.8, .2, .3), (-.5, .4, -.9), (0.0, 0.0, 1.0), (1.0, .0, 0.0) })
                {
                    // ring transform: turn in the Root frame, then Root to world
                    double rx = x * Math.Cos(theta) + z * Math.Sin(theta), rz = -x * Math.Sin(theta) + z * Math.Cos(theta);
                    var viaRing = frame.ToWorld(rx, y, rz);
                    // the part: base pose from its world position (stored in the Root frame), turned with RotateAboutY, set through the Root transform
                    var baseWorld = frame.ToWorld(x, y, z);
                    var baseLocal = frame.ToLocal(baseWorld.X, baseWorld.Y, baseWorld.Z);
                    var (tx, tz) = CarouselModelGeometry.RotateAboutY(baseLocal.X, baseLocal.Z, theta);
                    var viaPart = frame.ToWorld(tx, baseLocal.Y, tz);
                    check(Near(viaPart.X, viaRing.X, 1e-9) && Near(viaPart.Y, viaRing.Y, 1e-9) && Near(viaPart.Z, viaRing.Z, 1e-9), Inv($"pose maths: Root frame {frame}, turn {theta}, point ({x}, {y}, {z})"));
                    points++;
                }
        // The angle already applied is taken out of the pose when it is stored again: turn by -a, then by +a gives the same pose; turns add up.
        bool addUp = true;
        foreach (var (a, b) in new[] { (.3, -.285), (-1.2, 2.2), (0.0, .5) })
        {
            var (x1, z1) = CarouselModelGeometry.RotateAboutY(.7, -.4, a);
            var (x0, z0) = CarouselModelGeometry.RotateAboutY(x1, z1, -a);
            var (x2, z2) = CarouselModelGeometry.RotateAboutY(x1, z1, b);
            var (x3, z3) = CarouselModelGeometry.RotateAboutY(.7, -.4, a + b);
            addUp &= Near(x0, .7, 1e-12) && Near(z0, -.4, 1e-12) && Near(x2, x3, 1e-12) && Near(z2, z3, 1e-12);
        }
        check(addUp && points == 4 * 6 * 4, "the applied angle can be taken out of a stored pose; turns add up, and a turn of 2 pi is none");
        // The ring turn used by the game parts is the same angle as the ring: RingTurn(22, 19) = -2 pitches about +Y.
        var (qx, qz) = CarouselModelGeometry.RotateAboutY(0, 1, CarouselModelGeometry.RingTurn(22, 19));
        check(Near(Math.Atan2(qx, qz), -2 * 2 * Math.PI / 22, 1e-12), "turning by RingTurn moves a point at angle 0 to angle -(f - 1) pitches (rising slot angles = +Z towards +X)");

        // Path choice: all under the ring / found elsewhere (moved or cloned) / nothing found (baked).
        check(CarouselModelGeometry.RingPartPath(3, 3, 3) == CarouselRingPartPath.UnderRing && CarouselModelGeometry.RingPartPath(3, 3, 0) == CarouselRingPartPath.UnderRing,
            "all ring parts under the ring: Ring.localRotation path");
        check(CarouselModelGeometry.RingPartPath(3, 0, 3) == CarouselRingPartPath.Moved && CarouselModelGeometry.RingPartPath(3, 1, 2) == CarouselRingPartPath.Moved &&
              CarouselModelGeometry.RingPartPath(3, 0, 1) == CarouselRingPartPath.Moved && CarouselModelGeometry.RingPartPath(3, 0, 6) == CarouselRingPartPath.Moved,
            "parts found elsewhere (moved, cloned, or both): turned one by one");
        check(CarouselModelGeometry.RingPartPath(3, 0, 0) == CarouselRingPartPath.Baked && CarouselModelGeometry.RingPartPath(3, 2, 0) == CarouselRingPartPath.Baked &&
              CarouselModelGeometry.RingPartPath(3, 0, 0) == CarouselRingPartPath.Baked, "nothing found (baked into a native mesh): cannot turn");
        // 0.2.34: capacity 0 has no ring parts at all: path None, not "baked".
        check(CarouselModelGeometry.RingPartPath(0, 0, 0) == CarouselRingPartPath.None && CarouselModelGeometry.RingPartPath(0, 0, 4) == CarouselRingPartPath.None && CarouselModelGeometry.RingPartPath(-1, 0, 0) == CarouselRingPartPath.None,
            "no ring parts (capacity 0): path None");

        // Lift target cache: fresh, cached, none; invalid values, too far, no cannon assigned.
        var good = new CarouselLiftTarget(.4, 1.1, .7); var old = new CarouselLiftTarget(-.3, 1.0, -.8);
        var t1 = CarouselModelGeometry.ChooseLiftTarget(good, old, true, out var s1);
        check(t1 == good && s1 == CarouselLiftTargetSource.Fresh, "valid target: fresh");
        var t2 = CarouselModelGeometry.ChooseLiftTarget(null, old, true, out var s2);
        check(t2 == old && s2 == CarouselLiftTargetSource.Cached, "no target (rebuild at the start of Play): the last valid one");
        var t3 = CarouselModelGeometry.ChooseLiftTarget(new CarouselLiftTarget(40, 30, 30), old, true, out var s3);
        var t4 = CarouselModelGeometry.ChooseLiftTarget(new CarouselLiftTarget(double.NaN, 1, 1), old, true, out var s4);
        check(t3 == old && s3 == CarouselLiftTargetSource.Cached && t4 == old && s4 == CarouselLiftTargetSource.Cached, "target more than 50 m away or not finite: the last valid one");
        var t5 = CarouselModelGeometry.ChooseLiftTarget(null, null, true, out var s5);
        var t6 = CarouselModelGeometry.ChooseLiftTarget(null, new CarouselLiftTarget(double.PositiveInfinity, 0, 0), true, out var s6);
        var t7 = CarouselModelGeometry.ChooseLiftTarget(null, old, false, out var s7);
        check(t5 == null && s5 == CarouselLiftTargetSource.None && t6 == null && s6 == CarouselLiftTargetSource.None && t7 == null && s7 == CarouselLiftTargetSource.None,
            "no valid target yet, or no cannon assigned: none (lift at local -Z as before)");
        // The slot under the lift follows the target: the cached target gives the designer's slot, the missing one slot 0 (22 slots, the 0.2.32 log).
        int designer = CarouselModelGeometry.LiftSlotFor(22, new CarouselLiftTarget(.8 * Math.Sin(Math.PI), 1.1, .8 * Math.Cos(Math.PI)));
        check(designer == 11, "lift slot of a -Z target on 22 slots is 11");
        // Source guards: no new hook, no SetParent of parts, no per-frame search; the proof line has the new fields; the baked case is logged once.
        var root = FindRoot()!;
        var visuals = File.ReadAllText(Path.Combine(root, "CarouselVisuals.cs"));
        int tickStart = visuals.IndexOf("internal static void Tick()", StringComparison.Ordinal), tickEnd = visuals.IndexOf("// 0.2.32 proof line", StringComparison.Ordinal);
        var tick = tickStart >= 0 && tickEnd > tickStart ? visuals.Substring(tickStart, tickEnd - tickStart) : "";
        check(visuals.Split("[HarmonyPostfix").Length == 5 && visuals.Split("SetParent(").Length == 4 && !visuals.Contains("GetComponentsInChildren<MeshFilter>(true)\n\n") &&
              !tick.Contains("GetComponentsInChildren") && !tick.Contains("ResolveRingParts") && visuals.Contains("ring meshes baked by the game; ring cannot turn in Play") &&
              visuals.Contains("BakedNoted") && visuals.Contains("LastTargets"), "ring parts: no new hook, parts are never re-parented, no search in Tick, baked case logged once, target cache present");
        check(visuals.Contains("ring parts {underRing}/") && visuals.Contains("moved {moved}, clones {visual.Clones}") && visuals.Contains("first part parents") && visuals.Contains("lift target "), "proof line: ring parts k/m, moved, clones, parents, lift target");
        report.Add("");
        report.Add($"ring parts in Play: pose maths checked on {points} frame/turn/point combinations (uniform and non-uniform Root scale, Root turned and moved)");
    }

    // ---- 0.2.32: smooth turn, config, version constant, source guards
    static void TurnTests(Action<bool, string> check, List<string> report)
    {
        // Single shot or not: exactly one lower, inside 0..n, turn time above 0.
        check(CarouselModelGeometry.IsSingleShot(22, 22, 21, .5) && CarouselModelGeometry.IsSingleShot(22, 1, 0, .5) && CarouselModelGeometry.IsSingleShot(1, 1, 0, .5), "stock one lower: smooth turn");
        check(!CarouselModelGeometry.IsSingleShot(22, 22, 20, .5) && !CarouselModelGeometry.IsSingleShot(22, 22, 0, .5), "stock more than one lower: jump (Delta > 1)");
        check(!CarouselModelGeometry.IsSingleShot(22, 10, 11, .5) && !CarouselModelGeometry.IsSingleShot(22, 0, 22, .5) && !CarouselModelGeometry.IsSingleShot(22, 21, 22, .5), "stock up (refill, Play reset, loading): jump");
        check(!CarouselModelGeometry.IsSingleShot(22, 10, 10, .5) && !CarouselModelGeometry.IsSingleShot(22, 0, -1, .5) && !CarouselModelGeometry.IsSingleShot(22, 23, 22, .5) && !CarouselModelGeometry.IsSingleShot(0, 1, 0, .5), "no change, stock out of range or no slots: jump");
        check(!CarouselModelGeometry.IsSingleShot(22, 22, 21, 0) && !CarouselModelGeometry.IsSingleShot(22, 22, 21, -1) && !CarouselModelGeometry.IsSingleShot(22, 22, 21, double.NaN), "turn time 0 (or unusable): the ring jumps");
        // Config: 0.5 default, 0..5 allowed, nonsense -> default.
        check(CarouselModelGeometry.DefaultTurnSeconds == .5 && CarouselModelGeometry.TurnSeconds == .5 && CarouselModelGeometry.SanitizeTurnSeconds(0) == 0 && CarouselModelGeometry.SanitizeTurnSeconds(.25) == .25 &&
              CarouselModelGeometry.SanitizeTurnSeconds(99) == 5 && CarouselModelGeometry.SanitizeTurnSeconds(-2) == 0 && CarouselModelGeometry.SanitizeTurnSeconds(double.NaN) == .5 && CarouselModelGeometry.SanitizeTurnSeconds(double.PositiveInfinity) == .5,
            "CarouselTurnSeconds: default 0.5, 0 = jump, limited to 0..5, nonsense gives the default");
        // Interpolation: ends, monotonic, smoothstep midpoint, downward turns too.
        double from = 0, to = -2 * Math.PI / 22;
        check(Near(CarouselModelGeometry.TurnAngleAt(from, to, 0, .5), from, 1e-15) && Near(CarouselModelGeometry.TurnAngleAt(from, to, .25, .5), (from + to) / 2, 1e-12) &&
              CarouselModelGeometry.TurnAngleAt(from, to, .5, .5) == to && CarouselModelGeometry.TurnAngleAt(from, to, 7, .5) == to && Near(CarouselModelGeometry.TurnAngleAt(from, to, -1, .5), from, 1e-15),
            "turn angle: start, midpoint, end; before the start it stays, after the end it is the end angle");
        bool monotonic = true; double last = from;
        for (int i = 0; i <= 100; i++)
        {
            double a = CarouselModelGeometry.TurnAngleAt(from, to, i * .005, .5);
            monotonic &= a <= last + 1e-15 && a >= to - 1e-15; last = a;
        }
        check(monotonic, "turn angle runs monotonically from the old to the new angle without overshoot");
        // Safety net: duration 0, a tick gap above 1 s, NaN time -> straight to the end angle; exactly 1 s is still fine.
        check(CarouselModelGeometry.TurnAngleAt(from, to, .1, 0) == to && CarouselModelGeometry.TurnAngleAt(from, to, .1, .5, 1.5) == to && CarouselModelGeometry.TurnAngleAt(from, to, double.NaN, .5) == to &&
              CarouselModelGeometry.TurnAngleAt(from, to, .1, .5, double.NaN) == to && CarouselModelGeometry.TurnAngleAt(from, to, .1, .5, 1.0) != to, "safety net: no tick for 1 s, time up, NaN or no duration: straight to the end angle");
        // The driver of CarouselVisuals on plain numbers: a shot starts a turn, a stock change while turning finishes it at once, a refill jumps.
        const double pitch = 2 * Math.PI / 22;
        int stock = -1; double angle = 0, tFrom = 0, tTo = 0, tStart = 0, tLast = 0; bool turning = false;
        void Change(int n, int s, double now)
        {
            if (turning) { turning = false; angle = tTo; }
            double target = CarouselModelGeometry.RingTurn(n, s);
            if (stock >= 0 && CarouselModelGeometry.IsSingleShot(n, stock, s, CarouselModelGeometry.TurnSeconds) && Math.Abs(target - angle) > 1e-9) { tFrom = angle; tTo = target; tStart = now; tLast = now; turning = true; }
            else angle = target;
            stock = s;
        }
        void Tick(double now)
        {
            if (!turning) return;
            double a = CarouselModelGeometry.TurnAngleAt(tFrom, tTo, now - tStart, CarouselModelGeometry.TurnSeconds, now - tLast); tLast = now;
            if (a == tTo || now - tStart >= CarouselModelGeometry.TurnSeconds) { turning = false; angle = tTo; } else angle = a;
        }
        Change(22, 22, 0);                                   // first display: set
        check(!turning && angle == 0, "first display sets the ring at once");
        Change(22, 21, 1); Tick(1.1);
        check(!turning && angle == 0, "first shot (f = 1): the ring stays, the used cassette is under the lift");
        Change(22, 20, 2); Tick(2.1);
        check(turning && angle < 0 && angle > -pitch, "second shot: the ring turns, half way after a tick");
        Tick(2.3); double mid = angle; Tick(2.4);
        check(turning && angle < mid, "the turn keeps going with the ticks");
        Tick(2.6);
        check(!turning && Near(angle, -pitch, 1e-12), "a turn is exactly one pitch at the end");
        Change(22, 19, 3); Tick(3.1); Change(22, 18, 3.2);   // a stock change while turning: first at the end angle, then the new turn
        check(turning && Near(tFrom, -2 * pitch, 1e-12) && Near(tTo, -3 * pitch, 1e-12), "stock change during a turn: the ring first goes to its end angle, then turns on");
        Tick(3.2 + 1.6);                                     // the ticks stop for 1.6 s: the next tick finishes the turn
        check(!turning && Near(angle, -3 * pitch, 1e-12), "no tick for 1 s: the next tick puts the ring at the end angle");
        Change(22, 22, 6);                                   // refill: jump back
        check(!turning && angle == 0, "refill jumps back to the base position");
        Change(22, 21, 7); Change(22, 5, 7.1);               // more than one down (Play reset or a loaded save): jump
        check(!turning && Near(angle, -(22 - 5 - 1) * pitch, 1e-12), "a change of more than one jumps");
        Change(22, 0, 8); Change(22, 1, 8.5); Change(22, 0, 9); Tick(9.6);
        check(!turning && Near(angle, -21 * pitch, 1e-12), "last round fired: the ring stands n - 1 slots on");
        CarouselModelGeometry.TurnSeconds = 0; Change(22, 22, 10); Change(22, 21, 10); Change(22, 20, 10);
        check(!turning && Near(angle, -pitch, 1e-12), "CarouselTurnSeconds = 0: the ring jumps");
        CarouselModelGeometry.TurnSeconds = .5;

        // Cost of the pure part per call (the Unity calls cannot be measured outside the game).
        var sw = System.Diagnostics.Stopwatch.StartNew(); double sink = 0;
        for (int i = 0; i < 2_000_000; i++) sink += CarouselModelGeometry.TurnAngleAt(0, -.285, (i % 500) * .001, .5, .016);
        sw.Stop();
        check(sink != 1.23456789 && sw.Elapsed.TotalMilliseconds < 2000, "turn angle: 2 million calls without allocation");
        report.Add("");
        report.Add(Inv($"turn cost: TurnAngleAt {sw.Elapsed.TotalMilliseconds * 1e6 / 2e6:0.0} ns per call (2 million calls, release build); the rest of a tick is one Time.unscaledTime read and one Transform.localRotation set per turning ring; idle: one List.Count check per LoadTask.Update call"));

        // Version constant: one constant for the BepInPlugin attribute and the load log line, same as the csproj; the old 0.2.28 text is gone.
        var root = FindRoot()!;
        var plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"));
        var csproj = File.ReadAllText(Path.Combine(root, "SprocketCarouselAutoloader.csproj"));
        var match = System.Text.RegularExpressions.Regex.Match(plugin, "public const string PluginVersion = \"([0-9.]+)\";");
        check(match.Success && match.Groups[1].Value == "0.3.6" && csproj.Contains("<Version>" + match.Groups[1].Value + "</Version>") &&
              plugin.Contains("[BepInPlugin(Guid, \"Sprocket Carousel Autoloader\", PluginVersion)]") && plugin.Contains("Sprocket Autoloaders {PluginVersion} loaded;") && !plugin.Contains("0.2.28 loaded") &&
              System.Text.RegularExpressions.Regex.Matches(plugin, "\"0\\.3\\.6\"").Count == 1, "version constant 0.3.6: one place in Plugin.cs, the same in the csproj, used by the attribute and the load line");
        check(plugin.Contains("\"CarouselTurnSeconds\"") && plugin.Contains("CarouselModelGeometry.DefaultTurnSeconds"), "config [Visuals] CarouselTurnSeconds = 0.5");
        // Source guards: the tick is a call in the existing LoadTask.Update postfix; no allocation per frame; no new hook.
        var visuals = File.ReadAllText(Path.Combine(root, "CarouselVisuals.cs"));
        var refill = File.ReadAllText(Path.Combine(root, "MagazineRefill.cs"));
        int tickStart = visuals.IndexOf("internal static void Tick()", StringComparison.Ordinal), tickEnd = visuals.IndexOf("// 0.2.32 proof line", StringComparison.Ordinal);
        var tick = tickStart >= 0 && tickEnd > tickStart ? visuals.Substring(tickStart, tickEnd - tickStart) : "";
        check(refill.Contains("CarouselVisuals.Tick();") && refill.Split("[HarmonyPostfix, HarmonyPatch(typeof(LoadTask), nameof(LoadTask.Update))]").Length == 2 &&
              tick.Contains("if(Turns.Count==0) return;") && !tick.Contains("new ") && !tick.Contains("=>") && !tick.Contains("Linq") && !tick.Contains("ToList") && !tick.Contains("ToArray") &&
              visuals.Split("[HarmonyPostfix").Length == 5 && !visuals.Contains("LateUpdate"), "ring turn driven by the existing LoadTask.Update postfix: idle check first, no allocation in Tick, no new hook");
        check(visuals.Contains("if(Plugin.DetailedLogging) LogStock(") && visuals.Contains("ring children") && visuals.Contains("slot under lift") && visuals.Contains("localEulerAngles.y"),
            "proof line per stock change (DetailedLogging only): stock, fired, ring angle, applied angle, slot under the lift, ring children");
    }

    // ---- 0.2.30: the lift behind the cannon
    internal sealed record LiftInfo(CarouselVisualVector Origin, double SlotTop, double BodyBeyondRim, double HeadBeyondRim, double Top, double RamY, double HeightFactor);

    static double Along(CarouselVisualVector v, CarouselVisualVector dir) => v.X * dir.X + v.Y * dir.Y + v.Z * dir.Z;

    // Builds the case with and without the lift (and with the same target) and checks the lift: direction, slot under it, place on the rim,
    // foot on the cassettes, ram height. Slot-level checks only for the detailed level.
    internal static LiftInfo LiftPlacement(Action<bool, string> check, Case c, CarouselLiftTarget? target)
    {
        string name = c.Name + (target is { } tt ? Inv($" target ({tt.X:0.###}, {tt.Y:0.###}, {tt.Z:0.###})") : " no cannon");
        CarouselModelGeometry.Lift = true;
        var meshes = CarouselModelGeometry.Build(c.Diameter, c.Depth, c.Slots, c.Calibre, c.Projectile, c.Charge, c.Vertical, out var choice, -1, target);
        var plan = CarouselModelGeometry.Fit(c.Diameter, c.Depth, c.Slots, c.Calibre, c.Projectile, c.Charge, c.Vertical, out _, target)!;
        CarouselModelGeometry.Lift = false;
        var noLift = CarouselModelGeometry.Build(c.Diameter, c.Depth, c.Slots, c.Calibre, c.Projectile, c.Charge, c.Vertical, out _, -1, target);
        CarouselModelGeometry.Lift = true;
        var style = c.Vertical ? CarouselModelData.T80 : CarouselModelData.T72;
        var mech = Mesh(meshes, "Mechanism").Vertices; var mechNoLift = Mesh(noLift, "Mechanism").Vertices;
        var lift = mech.Skip(mechNoLift.Count).ToList();
        double k = plan.K, r = c.Diameter / 2;
        double a = plan.LiftAngle;
        var outward = new CarouselVisualVector(Math.Sin(a), 0, Math.Cos(a));
        var inward = outward * -1; var side = new CarouselVisualVector(-Math.Cos(a), 0, Math.Sin(a)); var up = new CarouselVisualVector(0, 1, 0);
        double slotTop = meshes.Where(m => m.Material is "Cassette" or "Projectile" or "Charge").SelectMany(m => m.Vertices).Max(v => v.Y);

        // The foot plate corners of the part (y 0, x +-0.22, z +-0.15) average to the lift origin.
        var corners = Enumerable.Range(0, style.Lift.Vertices).Where(i => style.Lift.Positions[3 * i + 1] == 0 && Math.Abs(Math.Abs(style.Lift.Positions[3 * i]) - .22) < 1e-6 &&
            Math.Abs(Math.Abs(style.Lift.Positions[3 * i + 2]) - .15) < 1e-6).Select(i => lift[i]).ToList();
        check(corners.Count >= 4, name + ": foot plate corners found");
        var origin = new CarouselVisualVector(corners.Average(v => v.X), corners.Average(v => v.Y), corners.Average(v => v.Z));
        // Same mesh as without the lift plus exactly the lift part, in the same order.
        check(lift.Count == style.Lift.Vertices && Triangles(meshes) - Triangles(noLift) == style.Lift.Triangles, name + ": the lift part is added to the mechanism mesh");
        // Direction: that of the load point, local -Z without one; slot under it; ring turned at most half a pitch.
        double expected = target is { } t ? CarouselModelGeometry.LiftDirection(t.X, t.Z) : Math.PI;
        check(Near(Math.IEEERemainder(a - expected, 2 * Math.PI), 0, 1e-12) &&
              Near(Math.IEEERemainder(Math.Atan2(origin.X, origin.Z) - a, 2 * Math.PI), 0, 1e-6), name + ": the lift stands in the direction of the load point (local -Z without a cannon)");
        check(Math.Abs(plan.Rot) <= Math.PI / c.Slots + 1e-12 &&
              Near(Math.IEEERemainder(CarouselModelGeometry.SlotAngle(c.Slots, CarouselModelGeometry.LiftSlot(c.Slots, a), plan.Rot) - a, 2 * Math.PI), 0, 1e-6),
            name + ": a slot stands exactly under the lift, the ring turned half a pitch at most");
        if (choice.Level == CarouselModelLevel.Detailed)
        {
            var cassette = Mesh(meshes, "Cassette").Vertices;
            var under = Block(cassette, CarouselModelGeometry.LiftSlot(c.Slots, a), style.Cassette.Vertices);
            double lateral = under.Average(v => Along(v, side)), radial = under.Average(v => Along(v, outward));
            check(Math.Abs(lateral) < 1e-6 && radial > 0 && under.All(v => Along(v, outward) <= plan.End + 1e-6 && Along(v, outward) >= plan.End - plan.CassetteDepth - 1e-6),
                name + ": the cassette under the lift lies on the lift axis");
            // The visible rounds keep their order: slot i at the turned angle i.
            bool wedge = true;
            for (int slot = 0; slot < c.Slots; slot++)
                foreach (var v in Block(cassette, slot, style.Cassette.Vertices))
                    wedge &= InWedge(v, CarouselModelGeometry.SlotAngle(c.Slots, slot, plan.Rot), c.Slots);
            check(wedge, name + ": slot i keeps index i (turned by the ring rotation)");
        }
        // Foot on top of the cassettes, nothing of the lift below them.
        if (choice.Level == CarouselModelLevel.Detailed)
            check((origin - plan.LiftOrigin).Length < 1e-6 && (origin - (outward * (plan.End - CarouselModelGeometry.LiftInset * k) + up * slotTop)).Length < 1e-6, name + ": lift origin = slot outer edge minus the foot plate half depth, on top of the slot");
        check(Near(origin.Y, slotTop, 1e-6) && Near(lift.Min(v => v.Y), slotTop, 1e-6),
            name + ": the foot of the lift starts on top of the cassettes");
        // On the rim: foot plate ends on the slot outer edge, all of the lift below the ram head within the rim plus a small margin, claw over the cassette.
        double headBottom = CarouselModelGeometry.Info(c.Vertical).LiftHeadBottom;
        double body = double.NegativeInfinity, head = double.NegativeInfinity, plateOuter = double.NegativeInfinity, plateInner = double.PositiveInfinity, clawInner = double.PositiveInfinity, lat = 0;
        for (int i = 0; i < lift.Count; i++)
        {
            double py = style.Lift.Positions[3 * i + 1], along = Along(lift[i], outward), reach = Math.Sqrt(lift[i].X * lift[i].X + lift[i].Z * lift[i].Z);
            if (py <= headBottom) body = Math.Max(body, reach); else head = Math.Max(head, reach);
            if (py <= .0201) { plateOuter = Math.Max(plateOuter, along); plateInner = Math.Min(plateInner, along); lat = Math.Max(lat, Math.Abs(Along(lift[i], side))); }
            if (py > .05 && py <= CarouselModelGeometry.LiftClawTop) clawInner = Math.Min(clawInner, along);
        }
        check(plateOuter <= plan.End + 1e-6 && plateOuter >= plan.End - 1e-6 && plateInner >= plan.End - .3 * k - 1e-6 && lat <= .23 * k + 1e-6,
            name + Inv($": the foot plate ends on the slot outer edge and stands over the cassette (outer {plateOuter}, inner {plateInner}, End {plan.End}, k {k}, lat {lat})"));
        check(body <= r + CarouselModelGeometry.LiftRimMargin, name + ": the lift body stands within the rim plus a small margin, not outside the ring");
        check(clawInner >= plan.End - plan.CassetteDepth && clawInner <= plan.End, name + ": the claw stands over the outer part of the cassette");
        // Ram: frontmost point faces the centre; its height is the load point's (guides scaled within limits), otherwise the default height x k.
        double frontAlong = lift.Max(v => Along(v, inward));
        var frontVertices = lift.Where(v => Along(v, inward) >= frontAlong - 1e-6).ToList();
        var front = new CarouselVisualVector(frontVertices.Average(v => v.X), frontVertices.Average(v => v.Y), frontVertices.Average(v => v.Z));
        double ramRise = front.Y - slotTop;
        double g = target is { } tg && double.IsFinite(tg.Y) ? (tg.Y - slotTop) / (CarouselModelGeometry.Info(c.Vertical).LiftRamY * k) : double.NaN;
        bool inLimits = g >= CarouselModelGeometry.MinHeightFactor - 1e-9 && g <= CarouselModelGeometry.MaxHeightFactor + 1e-9;
        // 0.2.34 (was: default height outside the limits): the factor wanted, limited to 0.3..1.5; the default height only for an unknown load point.
        double used = double.IsNaN(g) ? 1 : Math.Clamp(g, CarouselModelGeometry.MinHeightFactor, CarouselModelGeometry.MaxHeightFactor);
        check(Near(ramRise, CarouselModelGeometry.Info(c.Vertical).LiftRamY * k * used, 1e-6) && (!inLimits || Near(front.Y, target!.Value.Y, 1e-6)),
            name + (inLimits ? ": the ram ends at the height of the load point" : double.IsNaN(g) ? ": default height x k (load point unknown)" : ": factor limited to 0.3..1.5 (load point outside the height limits)"));
        check(Along(front, inward) > Along(origin, inward) && lift.Count > 0 && mech.All(v => double.IsFinite(v.X + v.Y + v.Z)) &&
              Mesh(meshes, "Mechanism").Normals.All(n => double.IsFinite(n.X) && Math.Abs(n.Length - 1) < 1e-6), name + ": ram points to the centre, finite unit normals");
        return new(origin, slotTop, body - r, head - r, lift.Max(v => v.Y), front.Y, used);
    }

    static void LiftTests(Action<bool, string> check, List<string> report)
    {
        // Direction from a load point: front, back, left, right, 30 degrees; fallback to -Z.
        check(Near(CarouselModelGeometry.LiftDirection(0, 1), 0, 1e-12), "load point in front (+Z): angle 0");
        check(Near(CarouselModelGeometry.LiftDirection(0, -1), Math.PI, 1e-12) && Near(CarouselModelGeometry.LiftDirection(-0.0, -2), Math.PI, 1e-12), "load point behind (-Z): angle pi, also for -0");
        check(Near(CarouselModelGeometry.LiftDirection(1, 0), Math.PI / 2, 1e-12), "load point right (+X): angle +pi/2");
        check(Near(CarouselModelGeometry.LiftDirection(-1, 0), -Math.PI / 2, 1e-12), "load point left (-X): angle -pi/2");
        check(Near(CarouselModelGeometry.LiftDirection(.8 * Math.Sin(Math.PI / 6), .8 * Math.Cos(Math.PI / 6)), Math.PI / 6, 1e-12), "load point at 30 degrees: 30 degrees");
        check(Near(CarouselModelGeometry.LiftDirection(-.3, .3), -Math.PI / 4, 1e-12) && Near(CarouselModelGeometry.LiftDirection(-.3, -.3), -3 * Math.PI / 4, 1e-12), "load point diagonals");
        foreach (var (x, z) in new[] { (double.NaN, 1d), (1d, double.NaN), (double.PositiveInfinity, 0d), (0d, double.NegativeInfinity), (0d, 0d), (.0005, -.0005) })
            check(CarouselModelGeometry.LiftDirection(x, z) == Math.PI, Inv($"no usable load point ({x}, {z}): back to local -Z"));
        // Ring rotation: a slot always stands under the lift, also with an odd count, at most half a pitch; index range valid.
        int checkedRings = 0;
        foreach (var n in Enumerable.Range(3, 78))
            for (int step = -36; step <= 36; step++)
            {
                double angle = step * Math.PI / 36 + (step % 5) * .0137;
                double rot = CarouselModelGeometry.RingRotation(n, angle);
                int slot = CarouselModelGeometry.LiftSlot(n, angle);
                bool ok = slot >= 0 && slot < n && Math.Abs(rot) <= Math.PI / n + 1e-12 &&
                          Near(Math.IEEERemainder(CarouselModelGeometry.SlotAngle(n, slot, rot) - angle, 2 * Math.PI), 0, 1e-9);
                if (!ok) check(false, $"ring {n} slots, lift angle {angle}: slot {slot}, rotation {rot}");
                checkedRings++;
            }
        check(checkedRings == 78 * 73, "ring rotation: always a slot under the lift, at most half a pitch, valid slot index (3 to 80 slots, 73 directions)");
        check(Near(CarouselModelGeometry.RingRotation(22, Math.PI), 0, 1e-12) && Near(Math.Abs(CarouselModelGeometry.RingRotation(31, Math.PI)), Math.PI / 31, 1e-12) &&
              Near(CarouselModelGeometry.RingRotation(22, Math.PI / 6), Math.PI / 6 - 2 * 2 * Math.PI / 22, 1e-12), "ring rotation: none at -Z with 22 slots, half a pitch with 31, 30 degrees with 22 slots");
        // Height factor: inside and on the limits, outside the limits and for unusable input.
        foreach (var style in new[] { CarouselModelGeometry.T72, CarouselModelGeometry.T80 })
            foreach (double k in new[] { .5, 1, 1.4 })
            {
                double ram = style.LiftRamY * k;
                double F(double f) => CarouselModelGeometry.HeightFactorFor(f * ram, k, style.LiftRamY);
                check(Near(F(1), 1, 1e-9) && Near(F(.5), .5, 1e-9) && Near(F(1.2), 1.2, 1e-9) && Near(F(.67), .67, 1e-9), $"{style.Data.Name} k {k}: height factor inside the limits");
                check(Near(F(.3 + 1e-6), .3 + 1e-6, 1e-9) && Near(F(1.5 - 1e-6), 1.5 - 1e-6, 1e-9) && Near(F(.3), .3, 1e-9) && Near(F(1.5), 1.5, 1e-9), $"{style.Data.Name} k {k}: height factor on the limits");
                check(Near(F(.29), .3, 1e-12) && Near(F(1.51), 1.5, 1e-12) && Near(F(-2), .3, 1e-12) && Near(F(9), 1.5, 1e-12), $"{style.Data.Name} k {k}: outside the limits the nearest limit (0.2.34, was the default height)");
            }
        check(CarouselModelGeometry.HeightFactorFor(double.NaN, 1, 1.005) == 1 && CarouselModelGeometry.HeightFactorFor(1, 0, 1.005) == 1 && CarouselModelGeometry.HeightFactorFor(1, 1, 0) == 1, "height factor: unusable input gives the default");

        // The render cases of the brief and a sweep over directions, heights, counts and styles.
        var t72 = Renders[0]; var t80 = new Case("t80-1900-31x100", 1.90, .70, 31, .100, .600, .400, true);
        var t72Target = new CarouselLiftTarget(.8 * Math.Sin(Math.PI / 6), 1.1, .8 * Math.Cos(Math.PI / 6));
        var t80Target = new CarouselLiftTarget(0, 1.2, -.8);
        report.Add("");
        report.Add("lift cases | ring turn | foot (x, y, z) | body beyond the rim | ram head rear beyond the rim | ram height | height factor");
        foreach (var (c, tg) in new[] { (t72, t72Target), (t80, t80Target) })
        {
            var info = LiftPlacement(check, c, tg);
            var plan = CarouselModelGeometry.Fit(c.Diameter, c.Depth, c.Slots, c.Calibre, c.Projectile, c.Charge, c.Vertical, out _, tg)!;
            report.Add(Inv($"{c.Name} target ({tg.X:0.000}, {tg.Y:0.0}, {tg.Z:0.000}) | {plan.Rot * 180 / Math.PI:0.00} deg, lift slot {CarouselModelGeometry.LiftSlot(c.Slots, plan.LiftAngle)} | ({info.Origin.X:0.000}, {info.Origin.Y:0.000}, {info.Origin.Z:0.000}) | {info.BodyBeyondRim:+0.000;-0.000} m | {info.HeadBeyondRim:+0.000;-0.000} m | {info.RamY:0.000} m | {info.HeightFactor:0.000}"));
            // Moving the load point only moves the lift: the same slot indexing and mesh sizes as without a cannon.
            var withT = CarouselModelGeometry.Build(c.Diameter, c.Depth, c.Slots, c.Calibre, c.Projectile, c.Charge, c.Vertical, out _, 7, tg);
            var without = CarouselModelGeometry.Build(c.Diameter, c.Depth, c.Slots, c.Calibre, c.Projectile, c.Charge, c.Vertical, out _, 7);
            check(withT.Count == without.Count && withT.Zip(without).All(z => z.First.Material == z.Second.Material && z.First.Triangles.Count == z.Second.Triangles.Count &&
                  z.First.SlotPrefixIndexCounts.SequenceEqual(z.Second.SlotPrefixIndexCounts)), c.Name + ": visible-round indexing (prefix counts per stored round) unchanged by the load point");
            var rounds = Mesh(withT, "Projectile");
            var cassetteStyle = c.Vertical ? CarouselModelData.T80 : CarouselModelData.T72;
            bool inOwn = true;
            for (int slot = 0; slot < 7; slot++)
                inOwn &= rounds.Triangles.Skip(rounds.SlotPrefixIndexCounts[slot]).Take(rounds.SlotPrefixIndexCounts[slot + 1] - rounds.SlotPrefixIndexCounts[slot])
                    .All(i => InWedge(rounds.Vertices[i], CarouselModelGeometry.SlotAngle(c.Slots, slot, plan.Rot), c.Slots));
            check(inOwn && cassetteStyle.Projectile.Triangles == 118, c.Name + ": round i of the turned ring lies in the wedge of slot i");
        }
        var cases = new[] { t72, Renders[1], t80, new Case("t72-1900-30x100", 1.90, .50, 30, .100, .544, .326, false), new Case("t80-2400-45x105", 2.4, .8, 45, .105, .62, .41, true) };
        int runs = 0;
        foreach (var c in cases)
            for (int deg = -180; deg < 180; deg += 30)
                foreach (var height in new[] { .2, .6, 1.0, 1.2, 2.5 })
                {
                    double ang = (deg + 7) * Math.PI / 180;
                    LiftPlacement(check, c, new CarouselLiftTarget(.9 * Math.Sin(ang), height, .9 * Math.Cos(ang)));
                    runs++;
                }
        check(runs == cases.Length * 12 * 5, "lift sweep ran every direction, height and case");
        // Load point without a height: the default height.
        LiftPlacement(check, t72, new CarouselLiftTarget(.5, double.NaN, -.5));
        // Reduced level (many slots): the lift stands on the simple cassettes, in the load direction, a slot under it.
        var reduced = new Case("reduced-6000-64x125", 6, 1.2, 64, .125, .68, .408, false);
        var info64 = LiftPlacement(check, reduced, new CarouselLiftTarget(-1.4, 1.1, 1.0));
        check(CarouselModelGeometry.Build(reduced.Diameter, reduced.Depth, reduced.Slots, reduced.Calibre, reduced.Projectile, reduced.Charge, false, out var rc, -1,
            new CarouselLiftTarget(-1.4, 1.1, 1.0)).Count == 5 && rc.Level == CarouselModelLevel.Reduced, "reduced level also takes the lift direction");
        // Lift off: nothing of the lift and the same ring turn.
        CarouselModelGeometry.Lift = false;
        var off = CarouselModelGeometry.Build(t72.Diameter, t72.Depth, t72.Slots, t72.Calibre, t72.Projectile, t72.Charge, false, out _, -1, t72Target);
        CarouselModelGeometry.Lift = true;
        check(Triangles(off) == Triangles(CarouselModelGeometry.Build(t72.Diameter, t72.Depth, t72.Slots, t72.Calibre, t72.Projectile, t72.Charge, false, out _, -1, t72Target)) - CarouselModelData.T72.Lift.Triangles,
            "[Visuals] CarouselLift = false: no lift triangles");
        // Source guards: the load point is read once at build time from the same transform pattern as the bustle feed arm; no new hook.
        var visuals = File.ReadAllText(Path.Combine(FindRoot()!, "CarouselVisuals.cs"));
        check(visuals.Contains("cannon.transform.TransformPoint(cannon.LoadLocalPosition)") && visuals.Contains("target: liftTarget") &&
              visuals.Split("[HarmonyPostfix").Length == 5 && !visuals.Contains("Update()") && !visuals.Contains("LateUpdate"), "load point read once in Build; no new hook or per-frame code in CarouselVisuals");
    }


    // ---- helpers
    static bool InWedge(CarouselVisualVector v, double angle, int slots)
    {
        double zeta = v.X * Math.Sin(angle) + v.Z * Math.Cos(angle), x = v.X * Math.Cos(angle) - v.Z * Math.Sin(angle);
        double half = Math.PI / slots;
        return zeta > 0 && Math.Abs(x) <= zeta * Math.Tan(half) - CarouselModelGeometry.WedgeMargin / Math.Cos(half) + Eps;
    }
    static (double[] Zeta, double[] X, double[] Y) Local(List<CarouselVisualVector> block, double angle)
        => (block.Select(v => v.X * Math.Sin(angle) + v.Z * Math.Cos(angle)).ToArray(), block.Select(v => v.X * Math.Cos(angle) - v.Z * Math.Sin(angle)).ToArray(), block.Select(v => v.Y).ToArray());
    static List<CarouselVisualVector> Block(List<CarouselVisualVector> vertices, int slot, int size)
        => vertices.Skip(slot * size).Take(size).ToList();
    static CarouselVisualMesh Mesh(IReadOnlyList<CarouselVisualMesh> meshes, string material) => meshes.First(m => m.Material == material);
    static int Triangles(IReadOnlyList<CarouselVisualMesh> meshes) => meshes.Sum(m => m.Triangles.Count) / 3;
    static bool Near(double a, double b, double eps) => Math.Abs(a - b) <= eps;
    static double Min(CarouselPart p, int axis) { double m = double.MaxValue; for (int i = axis; i < p.Positions.Length; i += 3) m = Math.Min(m, p.Positions[i]); return m; }
    static double Max(CarouselPart p, int axis) { double m = double.MinValue; for (int i = axis; i < p.Positions.Length; i += 3) m = Math.Max(m, p.Positions[i]); return m; }
    static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    static bool Same(IReadOnlyList<CarouselVisualMesh> a, IReadOnlyList<CarouselVisualMesh> b)
        => a.Count == b.Count && a.Zip(b).All(p => p.First.Name == p.Second.Name && p.First.Material == p.Second.Material && p.First.Slot == p.Second.Slot &&
            p.First.Vertices.SequenceEqual(p.Second.Vertices) && p.First.Normals.SequenceEqual(p.Second.Normals) &&
            p.First.Triangles.SequenceEqual(p.Second.Triangles) && p.First.SlotPrefixIndexCounts.SequenceEqual(p.Second.SlotPrefixIndexCounts));

    // Independent OBJ reader: one vertex per (v, vn) pair in order of first use, per group.
    static List<(string Name, float[] Positions, float[] Normals, int[] Indices)> ParseObj(string text)
    {
        var v = new List<float[]>(); var vn = new List<float[]>();
        var groups = new List<(string Name, List<float> P, List<float> N, List<int> I, Dictionary<(int, int), int> Ids)>();
        float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        foreach (var raw in text.Split('\n'))
        {
            var p = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            switch (p[0])
            {
                case "v": v.Add(new[] { F(p[1]), F(p[2]), F(p[3]) }); break;
                case "vn": vn.Add(new[] { F(p[1]), F(p[2]), F(p[3]) }); break;
                case "g": groups.Add((p[1], new(), new(), new(), new())); break;
                case "f":
                    var g = groups[^1];
                    foreach (var corner in p.Skip(1))
                    {
                        var q = corner.Split('/');
                        var key = (int.Parse(q[0]), int.Parse(q[2]));
                        if (!g.Ids.TryGetValue(key, out var id))
                        {
                            id = g.Ids.Count; g.Ids[key] = id;
                            g.P.AddRange(v[key.Item1 - 1]); g.N.AddRange(vn[key.Item2 - 1]);
                        }
                        g.I.Add(id);
                    }
                    break;
            }
        }
        return groups.Select(g => (g.Name, g.P.ToArray(), g.N.ToArray(), g.I.ToArray())).ToList();
    }

    static string? FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "SprocketCarouselAutoloader.csproj"))) return dir.FullName;
        return null;
    }
}
