using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SprocketExpandedBallistics.Era;
using SprocketExpandedBallistics.Materials;
using SprocketExpandedBallistics;

namespace SprocketExpandedBallistics.Tests;

// Modular Nizh panel and Duplet panels/side module, v0.20.0.
internal static class DupletModularTests
{
    private sealed record PartInfo(string Key, string Menu, ModularCellSpec Spec, string Variant, string Guid, string Prefix,
        string PartName, int Layers, double TargetKgM2, string Response, double LayerMm, double FrontMm, double InterMm, double Depth);

    private static readonly PartInfo[] Parts =
    {
        new("nizh", "Nizh panel", ModularCellSpec.NizhPanel, "nizhPanel", "d706e182-a7c5-5a2a-9231-681480af74cc", "nizhPanel",
            "nizhPanelV1", 1, 180, "nizh34Active", 36, 8, 0, .0465),
        new("turret", "Duplet panel (turret)", ModularCellSpec.DupletTurret, "dupletTurret", "ea9df1d4-18f5-5c8f-a6d0-d2c20cd3bc91",
            "dupletPanelTurret", "dupletPanelTurretV1", 2, 400, "dupletHullActive", 12, 20, 12, .060),
        new("hull", "Duplet panel (hull)", ModularCellSpec.DupletHull, "dupletHull", "3532720a-7150-51da-85a5-00d94530dd46",
            "dupletPanelHull", "dupletPanelHullV1", 2, 350, "dupletHullActive", 12, 15, 10, .053),
        new("side", "Duplet side module", ModularCellSpec.DupletSide, "dupletSide", "ac5d72a9-cbfc-5fa5-80da-061b75ea8c4c",
            "dupletSideModule", "dupletSideModuleV1", 2, 300, "dupletHullActive", 12, 10, 10, .048),
    };

    private static readonly string[] HiddenGuids =
    {
        // Duplet, ranks 400-407 before 0.20.0
        "1873e6e7-451d-53a7-ad0b-1c496d0c1205", "b3de0597-85c1-5720-bf38-463d6900058c", "c2babd87-5e84-52ef-b0cb-8ff123b4da96",
        "0ec4fcf7-13ca-572b-aee5-9c6850a65714", "bf3d681b-977f-57ec-8ee4-a7e09aabc28f", "8a0554d3-70cd-5a76-9dcf-570cb6cfe8fb",
        "169ec329-b26c-5996-81b9-add749f1703f", "8ab93175-8804-53d3-8256-4ff0e8239d97",
        // Nizh-1M, Nizh-19, Nizh-34, Nizh older cassette (ranks 500, 501, 502, 504)
        "c8a885b3-68bd-5557-8884-3000201281dd", "378938f3-cbe0-5c87-a87b-2aaf59accb76", "0497c36a-1772-54ff-8342-82ded394e6af",
        "1ad42e4d-a85e-5a22-8fea-82c2e83d3b51",
    };

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "DupletPanelAssets", name);

    private static (double X0, double X1, double Y0, double Y1, double Z0, double Z1) Bounds(ModularMeshData mesh)
    {
        var b = (X0: double.MaxValue, X1: double.MinValue, Y0: double.MaxValue, Y1: double.MinValue, Z0: double.MaxValue, Z1: double.MinValue);
        foreach (var v in mesh.Vertices)
            b = (Math.Min(b.X0, v.X), Math.Max(b.X1, v.X), Math.Min(b.Y0, v.Y), Math.Max(b.Y1, v.Y), Math.Min(b.Z0, v.Z), Math.Max(b.Z1, v.Z));
        return b;
    }

    // Directed edges must pair up exactly (consistent outward winding of every shell).
    private static bool ConsistentWinding(ModularMeshData mesh)
    {
        long K(float f) => (long)Math.Round(f * 1e7);
        var directed = new Dictionary<(long, long, long, long, long, long), int>();
        for (var i = 0; i < mesh.Triangles.Length; i += 3)
            for (var j = 0; j < 3; j++)
            {
                var a = mesh.Vertices[mesh.Triangles[i + j]];
                var b = mesh.Vertices[mesh.Triangles[i + (j + 1) % 3]];
                var e = (K(a.X), K(a.Y), K(a.Z), K(b.X), K(b.Y), K(b.Z));
                directed[e] = directed.GetValueOrDefault(e) + 1;
            }
        foreach (var (e, n) in directed)
        {
            var reverse = (e.Item4, e.Item5, e.Item6, e.Item1, e.Item2, e.Item3);
            if (directed.GetValueOrDefault(reverse) != n) return false;
        }
        return true;
    }

    private static int Tris(ModularMeshData m) => m.Triangles.Length / 3;

    private static ModularMeshData Build(ModularPartKind kind, ModularCellSpec spec, int u, int v,
        IReadOnlySet<int>? spent = null, double cx = 1, double cz = 1, bool coarse = false)
        => ModularCellGeometry.Build(kind, new ModularCellLayout(u, v), spec, spent, cx, cz, coarse: coarse);

    private static Guid UuidV5(string name)
    {
        var ns = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8").ToByteArray();
        Array.Reverse(ns, 0, 4); Array.Reverse(ns, 4, 2); Array.Reverse(ns, 6, 2);
        var hash = SHA1.HashData(ns.Concat(Encoding.UTF8.GetBytes(name)).ToArray());
        var bytes = hash.Take(16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        Array.Reverse(bytes, 0, 4); Array.Reverse(bytes, 4, 2); Array.Reverse(bytes, 6, 2);
        return new Guid(bytes);
    }

    // Independent analytic steel volume of the physical housing.
    private static double ExpectedHousingVolume(ModularCellSpec s, int u, int v)
    {
        double width = u * s.CellWidthM, length = v * s.CellLengthM, w = s.HousingWallM, pb = s.HousingBottomM, d = s.DepthM;
        var vol = width * length * pb + 2 * (d - pb) * w * length + 2 * (d - pb) * w * (width - 2 * w);
        foreach (var (zb, zt) in DupletModularGeometry.LayerZones(s))
            vol += (zt - zb) * w * (length - 2 * w) * (u - 1) + (zt - zb) * w * (width - 2 * w - (u - 1) * w) * (v - 1);
        foreach (var (pbm, ptm) in DupletModularGeometry.PlateZones(s))
            vol += (width - 2 * w) * (length - 2 * w) * (ptm - pbm);
        return vol;
    }

    internal static void Run(Action<bool, string> check)
    {
        var export = Environment.GetEnvironmentVariable("DUPLET_VIEWER_DIR");
        if (!string.IsNullOrEmpty(export)) ExportViewer(export);

        var catalogue = SprocketExpandedBallistics.ArmourResponses.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "physical-era-responses.json")));
        var bindingsText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "physical-era-bindings.json"));
        var bindings = EraPartBindings.Parse(bindingsText);
        var bindingRoot = JsonNode.Parse(bindingsText)!;

        foreach (var info in Parts)
        {
            var (key, menu, spec, variant, guid, prefix, partName, layers, target, responseId, layerMm, frontMm, interMm, depth) = info;
            var side = spec.DupletStyle == DupletModularGeometry.StyleSide;
            var single = layers == 1;
            var layouts = side
                ? new[] { (1, 2), (2, 2), (3, 2), (4, 2), (8, 2) }
                : new[] { (1, 1), (1, 2), (2, 1), (2, 2), (3, 3), (2, 4), (8, 8) };
            var response = catalogue.Responses.Single(r => r.ResponseId == responseId);

            // ---- spec: pitch, layer stack, depths, response window.
            check(Math.Abs(spec.CellWidthM - .125) < 1e-12 && Math.Abs(spec.CellLengthM - .250) < 1e-12 && Math.Abs(spec.DepthM - depth) < 1e-9,
                $"{key} cell pitch 125 x 250 mm, depth {depth * 1000:0.0} mm");
            check(spec.Valid && DupletModularGeometry.IsPanel(spec) && spec.ReliktStyle == 0, $"{key} spec is valid and not a Relikt style");
            check(ModularCellSpec.ForVariant(variant) == spec, $"{key} variant maps to its spec");
            check(spec.FixedRows == (side ? 2 : 0), $"{key} fixed rows {(side ? 2 : 0)}");
            var zones = DupletModularGeometry.LayerZones(spec);
            check(zones.Length == layers && zones.All(z => Math.Abs((z.Top - z.Bottom) * 1000 - layerMm) < 1e-6),
                $"{key} has {layers} active layer(s) of exactly {layerMm} mm");
            var plates = DupletModularGeometry.PlateZones(spec);
            check(Math.Abs((plates[^1].Top - plates[^1].Bottom) * 1000 - frontMm) < 1e-6 && (single || Math.Abs((plates[0].Top - plates[0].Bottom) * 1000 - interMm) < 1e-6) &&
                  Math.Abs(spec.HousingBottomM - spec.Stage1BottomM) < 1e-12,
                $"{key} front plate {frontMm} mm{(single ? "" : $", intermediate plate {interMm} mm")}, layers sit directly on the pan");
            check(response.Kind == "heavyEra" && response.Geometry.MinNormalThicknessMm <= layerMm && layerMm <= response.Geometry.MaxNormalThicknessMm,
                $"{key} layer thickness {layerMm} mm lies inside the {responseId} window {response.Geometry.MinNormalThicknessMm}..{response.Geometry.MaxNormalThicknessMm} mm");
            var otherResponse = responseId == "nizh34Active" ? "dupletHullActive" : "nizh34Active";
            var other = catalogue.Responses.Single(r => r.ResponseId == otherResponse);
            check(layerMm < other.Geometry.MinNormalThicknessMm || layerMm > other.Geometry.MaxNormalThicknessMm,
                $"{key} layer thickness is outside the other family's window, so the response choice is unambiguous");

            foreach (var (u, v) in layouts)
            {
                var layout = new ModularCellLayout(u, v);
                var l = $"{key} {u}x{v}";
                var width = u * spec.CellWidthM;
                var length = v * spec.CellLengthM;

                // Collision slabs: closed, exact paid volume, measured thickness equals the layer.
                foreach (var stage in single ? new[] { ModularPartKind.Stage1Collision } : new[] { ModularPartKind.Stage1Collision, ModularPartKind.Stage2Collision })
                {
                    var collision = Build(stage, spec, u, v);
                    var sl = $"{l} {stage}";
                    check(ModularCellGeometry.IsWatertightByPosition(collision) && ConsistentWinding(collision), $"{sl} closed with consistent winding");
                    check(Math.Abs(ModularCellGeometry.SignedVolume(collision) - ModularCellGeometry.ExpectedSlabVolume(layout, spec)) < 1e-6 * ModularCellGeometry.ExpectedSlabVolume(layout, spec) &&
                          Math.Abs(ModularCellGeometry.SignedVolume(collision) - u * v * .125 * .25 * layerMm / 1000) < 1e-6 * u * v * .125 * .25 * layerMm / 1000,
                        $"{sl} paid layer volume = {u * v} cells x 125 x 250 x {layerMm} mm");
                    check(Math.Abs(PlacedPartDepth.Measure(collision.Vertices, collision.Triangles, Vector3.One) - layerMm) < 1e-3,
                        $"{sl} production depth helper measures {layerMm} mm");
                    var scaled = Build(stage, spec, u, v, null, 2.0, side ? 2.0 : 3.0);
                    check(Math.Abs(PlacedPartDepth.Measure(scaled.Vertices, scaled.Triangles, new Vector3(2f, 1f, side ? 2f : 3f)) - layerMm) < 1e-3,
                        $"{sl} scale-compensated collision still measures {layerMm} mm in world space");
                    check(collision.Vertices.Length <= 2000 && collision.Triangles.Length <= 6000, $"{sl} stays inside the depth helper's mesh limits");
                    var cb = Bounds(collision);
                    var zone = zones[stage == ModularPartKind.Stage1Collision ? 0 : 1];
                    check(Math.Abs(cb.Y0 - zone.Bottom) < 1e-6 && Math.Abs(cb.Y1 - zone.Top) < 1e-6, $"{sl} sits in its layer zone");
                }

                // Physical housing: closed, non-overlapping steel, exact analytic volume.
                var housing = Build(ModularPartKind.Housing, spec, u, v);
                var hb = Bounds(housing);
                check(Math.Abs(hb.X0 + width / 2) < 1e-6 && Math.Abs(hb.X1 - width / 2) < 1e-6 && Math.Abs(hb.Z0 + length / 2) < 1e-6 &&
                      Math.Abs(hb.Z1 - length / 2) < 1e-6 && Math.Abs(hb.Y0) < 1e-9 && Math.Abs(hb.Y1 - depth) < 1e-6,
                    $"{l} housing footprint snaps to {width * 1000:0} x {length * 1000:0} mm, {depth * 1000:0.0} mm deep");
                check(ModularCellGeometry.IsWatertightByPosition(housing) && ConsistentWinding(housing), $"{l} housing is closed with consistent winding");
                check(Math.Abs(ModularCellGeometry.SignedVolume(housing) - ExpectedHousingVolume(spec, u, v)) < 1e-6 * ExpectedHousingVolume(spec, u, v),
                    $"{l} housing equals the analytic steel volume ({ModularCellGeometry.SignedVolume(housing):0.000000000} vs {ExpectedHousingVolume(spec, u, v):0.000000000})");
                check(Tris(housing) <= 8000 && housing.Vertices.Length <= 8000, $"{l} housing within the budget ({Tris(housing)} tri)");

                var housingVisual = Build(ModularPartKind.HousingVisual, spec, u, v);
                check(ModularCellGeometry.IsWatertightByPosition(housingVisual) && ConsistentWinding(housingVisual) && ModularCellGeometry.SignedVolume(housingVisual) > 0 &&
                      Tris(housingVisual) <= 8000 && housingVisual.Vertices.Length <= 8000, $"{l} housing visual is closed and inside the budget ({Tris(housingVisual)} tri)");

                // Cell visuals of every stage.
                foreach (var kind in single ? new[] { ModularPartKind.Stage1Visual } : new[] { ModularPartKind.Stage1Visual, ModularPartKind.Stage2Visual })
                {
                    var visual = Build(kind, spec, u, v);
                    var vl = $"{l} {kind}";
                    check(ModularCellGeometry.IsWatertightByPosition(visual) && ConsistentWinding(visual) && ModularCellGeometry.SignedVolume(visual) > 0,
                        $"{vl} is closed and outward");
                    check(Tris(visual) <= 8000 && visual.Vertices.Length <= 8000 && visual.Triangles.Length <= 24000, $"{vl} within 8000 tri / 8000 vertices ({Tris(visual)})");
                    var coarse = Build(kind, spec, u, v, null, 1, 1, true);
                    check(Tris(coarse) < Tris(visual) && ModularCellGeometry.IsWatertightByPosition(coarse), $"{vl} coarse LOD is lighter and closed");
                    for (var y = 0; y < v; y++)
                        for (var x = 0; x < u; x++)
                            check(Enumerable.Range(0, Tris(visual)).Count(t => visual.TriangleCells[t] == layout.Index(x, y)) >= 12, $"{vl} cell {x},{y} owns real detail");
                }

                // Groove relief of the active layers is closed (viewer/spent detail), render only.
                foreach (var front in single ? new[] { true } : new[] { true, false })
                {
                    var relief = DupletModularGeometry.LayerRelief(layout, spec, front);
                    check(ModularCellGeometry.IsWatertightByPosition(relief) && ConsistentWinding(relief) && ModularCellGeometry.SignedVolume(relief) > 0,
                        $"{l} {(front ? "front" : "rear")} layer groove relief is closed");
                }
            }

            // ---- budgets and spent states at the largest size, for every stage.
            var (mu, mv) = side ? (8, 2) : (8, 8);
            var visualKinds = single ? new[] { ModularPartKind.Stage1Visual } : new[] { ModularPartKind.Stage1Visual, ModularPartKind.Stage2Visual };
            var allSpent = new HashSet<int>(Enumerable.Range(0, mu * mv));
            var half = new HashSet<int>(Enumerable.Range(0, mu * mv).Where(i => i % 2 == 0));
            foreach (var kind in visualKinds)
                foreach (var (set, label) in new[] { ((HashSet<int>?)null, "intact"), (allSpent, "all spent"), (half, "half spent") })
                {
                    var m = Build(kind, spec, mu, mv, set);
                    check(Tris(m) <= 8000 && m.Vertices.Length <= 8000 && Tris(m) > 0 && ModularCellGeometry.IsWatertightByPosition(m) && ConsistentWinding(m),
                        $"{key} {kind} {mu}x{mv} {label}: {Tris(m)} tri / {m.Vertices.Length} vertices, closed");
                    var c = Build(kind, spec, mu, mv, set, 1, 1, true);
                    check(Tris(c) <= 8000 && Tris(c) > 0 && ModularCellGeometry.IsWatertightByPosition(c), $"{key} {kind} {mu}x{mv} {label} coarse within the budget");
                }

            // Spending one cell of a layer changes only that cell's triangles, and the visible state differs per layer.
            var sz = side ? 2 : 3;
            var lay3 = new ModularCellLayout(3, sz);
            int Owned(ModularMeshData m, int c) => Enumerable.Range(0, Tris(m)).Count(t => m.TriangleCells[t] == c);
            foreach (var kind in visualKinds)
            {
                var intact = Build(kind, spec, 3, sz);
                var one = Build(kind, spec, 3, sz, new HashSet<int> { 1 });
                check(Owned(one, 1) != Owned(intact, 1) && Enumerable.Range(0, lay3.CellCount).Where(c => c != 1).All(c => Owned(one, c) == Owned(intact, c)),
                    $"{key} {kind}: spending one cell changes only that cell's triangles");
                var spentFull = Build(kind, spec, 1, side ? 2 : 1, new HashSet<int>(Enumerable.Range(0, side ? 2 : 1)));
                check(Tris(spentFull) > 0 && ModularCellGeometry.IsWatertightByPosition(spentFull), $"{key} {kind}: fully spent part keeps visible damage geometry");
            }
            {
                // Front layer: lid gone, rim stays below the lid line + curl; rear layer: bulged pad.
                var frontKind = single ? ModularPartKind.Stage1Visual : ModularPartKind.Stage2Visual;
                var lidTop = spec.DepthM;
                bool HasLidTop(ModularMeshData m, int cell)
                {
                    var b = ModularCellGeometry.CellBounds(lay3, spec, cell % 3, cell / 3);
                    var cx = (b.X0 + b.X1) / 2; var cz = (b.Z0 + b.Z1) / 2;
                    return Enumerable.Range(0, Tris(m)).Any(t => m.TriangleCells[t] == cell && Enumerable.Range(0, 3).All(k =>
                    {
                        var p = m.Vertices[m.Triangles[t * 3 + k]];
                        return Math.Abs(p.Y - lidTop) < 1e-6 && Math.Abs(p.X - cx) < .07 && Math.Abs(p.Z - cz) < .13;
                    }));
                }
                var frontIntact = Build(frontKind, spec, 3, sz);
                var frontSpent = Build(frontKind, spec, 3, sz, new HashSet<int> { 1 });
                check(HasLidTop(frontIntact, 1) && !HasLidTop(frontSpent, 1) && HasLidTop(frontSpent, 0) && HasLidTop(frontSpent, 2),
                    $"{key} spent cell loses its front lid, neighbours keep theirs");
                var fb = Bounds(Build(frontKind, spec, 1, side ? 2 : 1, new HashSet<int>(Enumerable.Range(0, side ? 2 : 1))));
                var frontZoneTop = single ? spec.Stage1TopM : spec.Stage2TopM;
                check(fb.Y1 < spec.DepthM + .010 && fb.Y1 > frontZoneTop,
                    $"{key} fully spent part keeps rim and bolt stubs near the lid line");
                if (!single)
                {
                    // Rear layer spent: pad bulges above the intermediate plate; front layer spent: it does not.
                    var padTop = spec.Stage1CoverTopM;
                    var rearIntact = Bounds(Build(ModularPartKind.Stage1Visual, spec, 1, side ? 2 : 1));
                    var rearSpent = Bounds(Build(ModularPartKind.Stage1Visual, spec, 1, side ? 2 : 1, new HashSet<int>(Enumerable.Range(0, side ? 2 : 1))));
                    check(rearSpent.Y1 > rearIntact.Y1 + .003 && rearSpent.Y1 > padTop + .005,
                        $"{key} rear layer spent is visibly different (bulged intermediate plate)");
                    var frontSpentAll = Build(ModularPartKind.Stage2Visual, spec, 1, side ? 2 : 1, new HashSet<int>(Enumerable.Range(0, side ? 2 : 1)));
                    var rearSpentAll = Build(ModularPartKind.Stage1Visual, spec, 1, side ? 2 : 1, new HashSet<int>(Enumerable.Range(0, side ? 2 : 1)));
                    check(frontSpentAll.Vertices.Length != rearSpentAll.Vertices.Length && Bounds(frontSpentAll).Y1 > Bounds(rearSpentAll).Y1,
                        $"{key} front-layer and rear-layer spent states look different");
                }
            }

            // Single-stage Nizh has no second stage.
            if (single)
            {
                var rejected = false;
                try { Build(ModularPartKind.Stage2Visual, spec, 1, 1); } catch (InvalidDataException) { rejected = true; }
                var rejectedCollision = false;
                try { Build(ModularPartKind.Stage2Collision, spec, 1, 1); } catch (InvalidDataException) { rejectedCollision = true; }
                check(rejected && rejectedCollision, $"{key} has no second stage");
            }

            // ---- mass.
            var presets = EraGeneratedDefinitions.Presets.Where(p => p.PartGuid == guid).ToArray();
            var baseHousing = ModularCellGeometry.BaseHousingMass(spec);
            var housingMass = ModularCellGeometry.SignedVolume(Build(ModularPartKind.Housing, spec, 1, 1)) * 7850;
            var density = response.PassiveMaterial.Density;
            var stageMass = ModularCellGeometry.BaseStageMass(spec, density);
            check(Math.Abs(baseHousing - housingMass) < 1e-6 && presets.All(p => Math.Abs(p.MountMass - baseHousing) < 1e-4) &&
                  presets.All(p => Math.Abs(p.CassetteMass - stageMass) < 1e-4),
                $"{key} preset masses: housing {presets[0].MountMass:0.0000} kg (1x1 volume x 7850), layer {presets[0].CassetteMass:0.0000} kg (volume x response density)");
            var perCell = presets[0].MountMass + presets.Length * presets[0].CassetteMass;
            var kgPerM2 = perCell / (spec.CellWidthM * spec.CellLengthM);
            check(Math.Abs(kgPerM2 / target - 1) <= .10, $"{key} {kgPerM2:0.0} kg/m2 is within 10% of the {target} kg/m2 target ({perCell:0.000} kg per 1x1 cell, {4 * perCell:0.000} kg for 2x2)");
            check(presets.Length == layers && presets.Select(p => p.StageIndex).OrderBy(i => i).SequenceEqual(Enumerable.Range(1, layers)),
                $"{key} has one preset per active layer");

            // ---- preset wiring.
            var expectedIds = single ? new[] { prefix + "Cell" } : new[] { prefix + "Stage1Cell", prefix + "Stage2Cell" };
            check(presets.All(p => p.WholeCells && p.Resizable && p.ModularVariant == variant && p.MaxCountU == 8 && p.MaxCountV == (side ? 2 : 8) &&
                      p.FixedCountV == (side ? 2 : 0) && Math.Abs(p.CellWidthM - .125) < 1e-12 && Math.Abs(p.CellLengthM - .25) < 1e-12 &&
                      p.Mount == prefix + "Housing" && p.SpentVisual == null && p.HousingActiveIds!.SequenceEqual(expectedIds)) &&
                  presets.Select(p => p.Cassette).OrderBy(i => i, StringComparer.Ordinal).SequenceEqual(expectedIds.OrderBy(i => i, StringComparer.Ordinal)),
                $"{key} presets describe a whole-cell panel with the right component ids");
            check(!EraGeneratedDefinitions.LegacyPartGuids.Contains(guid) && EraMenuOrder.Ranks.ContainsKey(guid), $"{key} is visible and ranked");
            check(!EraGeneratedDefinitions.MenuLabels.ContainsKey(guid), $"{key} takes its menu name from localization");

            // ---- GUID derivation.
            check(UuidV5("sprocket-expanded-ballistics:era:" + partName).ToString() == guid,
                $"{key} GUID is UUIDv5(URL namespace, sprocket-expanded-ballistics:era:{partName})");

            // ---- Parts and localization files.
            using var json = JsonDocument.Parse(File.ReadAllText(Asset(partName + "Part.json")));
            var root = json.RootElement;
            var components = root.GetProperty("components").EnumerateArray().ToArray();
            check(root.GetProperty("guid").GetString() == guid && root.GetProperty("name").GetString() == partName && components.Length == 1 + layers &&
                  components[0].GetProperty("fileID").GetString() == prefix + "Housing" &&
                  components.Skip(1).Select(c => c.GetProperty("fileID").GetString()).SequenceEqual(expectedIds) &&
                  components[0].GetProperty("info").GetProperty("durableMaterialID").GetString() == "rha" &&
                  components.Skip(1).All(c => c.GetProperty("info").GetProperty("durableMaterialID").GetString() == responseId &&
                      Math.Abs(c.GetProperty("info").GetProperty("mass").GetDouble() - presets[0].CassetteMass) < 1e-5) &&
                  Math.Abs(components[0].GetProperty("info").GetProperty("mass").GetDouble() - presets[0].MountMass) < 1e-5,
                $"{key} Parts json carries GUID, {1 + layers} components, {responseId} and the preset masses");
            var transform = root.GetProperty("transform");
            double[] Triple(string k) => transform.GetProperty(k).EnumerateArray().Select(e => e.GetDouble()).ToArray();
            check(Triple("minScale").SequenceEqual(new[] { 1.0, 1.0, side ? 2.0 : 1.0 }) && Triple("maxScale").SequenceEqual(new[] { 8.0, 1.0, side ? 2.0 : 8.0 }) &&
                  Triple("scale").SequenceEqual(new[] { 1.0, 1.0, side ? 2.0 : 1.0 }) && transform.GetProperty("scaling").GetBoolean(),
                $"{key} Parts json scale limits {(side ? "1..8 x fixed 2" : "1..8 x 1..8")}");
            check(File.ReadAllText(Asset(partName + ".xml")).Contains("<name>" + menu + "</name>"), $"{key} localization names the menu entry '{menu}'");

            // ---- Icon.
            var icon = File.ReadAllBytes(Asset(prefix + "-icon.png"));
            check(icon.Length > 1000 && icon[1] == (byte)'P' && ReadBigEndian(icon, 16) == 256 && ReadBigEndian(icon, 20) == 256 && icon[24] == 8 && icon[25] == 6,
                $"{key} icon is 256x256 8-bit RGBA");

            // ---- Bindings: every layer bound to the existing response; housing passive.
            foreach (var cell in expectedIds)
                check(bindings.TryGetCassette(cell, out var binding) && binding.MountComponentId == prefix + "Housing" && binding.ResponseId == responseId &&
                      binding.MaterialId == responseId && binding.Kind == "heavyEra" && binding.MatchesRecipe(response.ResponseId, response.Kind, response.CompatibleMaterialIds),
                    $"{cell} is bound to the unchanged {responseId} response");
            check(!bindings.TryGetCassette(prefix + "Housing", out _), $"{key} housing is passive");
            var group = bindingRoot["sharedHousings"]!.AsArray().FirstOrDefault(g => g!["mountComponentId"]!.GetValue<string>() == prefix + "Housing");
            check(single ? group == null : group != null && group["cassetteComponentIds"]!.AsArray().Select(x => x!.GetValue<string>()).SequenceEqual(expectedIds),
                $"{key} shared-housing declaration {(single ? "absent (one cell component)" : "lists both layers")}");

            // ---- Tandem behaviour out of game: each layer is its own VUID cell and spends independently, front layer first.
            {
                const double angle = 60;
                var heat = response.Calibration.HeatRetention;
                var weight = ArmourResponseModel.AngularWeight(response, "heat", angle);
                var single1 = 1 - weight * (1 - heat);
                var cap = catalogue.MaximumAdditionalHeatLoss;
                check(weight > 0 && single1 < 1, $"{key} {responseId} reacts at {angle} degrees (angular weight {weight:0.000}, factor {single1:0.0000})");
                double Hit(ArmourShotState shot, string hitKey, bool intact)
                    => ArmourResponseModel.Apply(catalogue, response, shot, hitKey, "heat", true, true, layerMm, angle, 0, intact);
                var cells = new EraCells();
                var front = PlacedEraState.Key(7, 101, 0, 2, 1);
                var rear = PlacedEraState.Key(7, 102, 0, 2, 1);
                if (single)
                {
                    var first = Hit(new ArmourShotState(), "nizh", cells.Consume(front));
                    var again = Hit(new ArmourShotState(), "nizh-2", cells.Consume(front));
                    check(Math.Abs(first - single1) < 1e-9, $"{key} cell reacts once with the {responseId} heat retention ({first:0.0000} vs {single1:0.0000})");
                    check(again == 1, $"{key} spent cell holds passive steel only");
                }
                else
                {
                    // Shot 1 spends the front layer and is stopped before the rear one.
                    var f1 = cells.Consume(front);
                    var r1 = Hit(new ArmourShotState(), "front-1", f1);
                    check(f1 && Math.Abs(r1 - single1) < 1e-9 && cells.Contains(front) && !cells.Contains(rear), $"{key} shot 1: front layer reacts and spends, rear layer untouched");
                    // Shot 2 at the same cell: front layer spent (no effect), rear layer reacts.
                    var shot2 = new ArmourShotState();
                    var f2 = cells.Consume(front);
                    var f2r = Hit(shot2, "front-2", f2);
                    var r2 = cells.Consume(rear);
                    var r2r = Hit(shot2, "rear-2", r2);
                    check(!f2 && f2r == 1 && r2 && Math.Abs(r2r - single1) < 1e-9, $"{key} shot 2: spent front layer does nothing, intact rear layer reacts");
                    // Shot 3: both spent.
                    var shot3 = new ArmourShotState();
                    var f3 = Hit(shot3, "front-3", cells.Consume(front));
                    var r3 = Hit(shot3, "rear-3", cells.Consume(rear));
                    check(f3 == 1 && r3 == 1, $"{key} shot 3: both layers spent, passive steel only");
                    // One projectile that reaches both layers spends both (as-is behaviour, see FEASIBILITY.md).
                    var cells2 = new EraCells();
                    var both = new ArmourShotState();
                    var a = Hit(both, "front", cells2.Consume(front));
                    var b = Hit(both, "rear", cells2.Consume(rear));
                    check(Math.Abs(a - single1) < 1e-9 && b < 1 && Math.Abs(both.AdditionalRetention - Math.Max(1 - cap, single1 * single1)) < 1e-9,
                        $"{key} one shot reaching both layers applies both reactions (shared heat cap {cap:0.##})");
                }
            }
        }

        // ---- 1x1 + 1x1 = 1x2 (dimension and plate positions), for every part but the side module (fixed rows).
        foreach (var info in Parts.Where(p => p.Spec.DupletStyle != DupletModularGeometry.StyleSide))
        {
            var spec = info.Spec;
            var kinds = info.Layers == 1 ? new[] { ModularPartKind.Stage1Collision } : new[] { ModularPartKind.Stage1Collision, ModularPartKind.Stage2Collision };
            foreach (var kind in kinds)
            {
                var one = Build(kind, spec, 1, 1);
                var two = Build(kind, spec, 2, 1);
                var shifted = one.Vertices.Select(v => (Math.Round(v.X - .0625, 5), Math.Round(v.Y, 5), Math.Round(v.Z, 5)))
                    .Concat(one.Vertices.Select(v => (Math.Round(v.X + .0625, 5), Math.Round(v.Y, 5), Math.Round(v.Z, 5)))).Distinct().OrderBy(v => v).ToArray();
                var merged = two.Vertices.Select(v => (Math.Round(v.X, 5), Math.Round(v.Y, 5), Math.Round(v.Z, 5))).Distinct().OrderBy(v => v).ToArray();
                check(shifted.SequenceEqual(merged), $"{info.Key} two 1x1 {kind} slabs side by side span exactly one 2x1 slab");
            }
            var h1 = Bounds(Build(ModularPartKind.Housing, spec, 1, 1));
            var h2 = Bounds(Build(ModularPartKind.Housing, spec, 2, 1));
            var h3 = Bounds(Build(ModularPartKind.Housing, spec, 1, 2));
            check(Math.Abs((h2.X1 - h2.X0) - 2 * (h1.X1 - h1.X0)) < 1e-6 && Math.Abs((h2.Z1 - h2.Z0) - (h1.Z1 - h1.Z0)) < 1e-6 &&
                  Math.Abs((h3.Z1 - h3.Z0) - 2 * (h1.Z1 - h1.Z0)) < 1e-6 && Math.Abs((h3.X1 - h3.X0) - (h1.X1 - h1.X0)) < 1e-6,
                $"{info.Key} 1x1 + 1x1 housing envelope equals the 2x1 and 1x2 envelopes");
        }

        // ---- Side module: height is fixed at two rows.
        var sideSpec = ModularCellSpec.DupletSide;
        foreach (var sz in new[] { 1.0, 2.0, 3.0, 5.0, 0.3, -1.0, -4.0 })
            check(ModularCellGeometry.TryLayout(3, sz, sideSpec, 8, 2, out var fixedLayout) && fixedLayout.CountU == 3 && fixedLayout.CountV == 2,
                $"side module scale Z {sz} clamps to exactly two rows");
        check(!ModularCellGeometry.TryLayout(3, 0, sideSpec, 8, 2, out _) && !ModularCellGeometry.TryLayout(double.NaN, 2, sideSpec, 8, 2, out _),
            "side module still rejects zero and non-finite scale");
        var sidePreset = EraGeneratedDefinitions.Presets.First(p => p.PartGuid == "ac5d72a9-cbfc-5fa5-80da-061b75ea8c4c");
        foreach (var sz in new[] { 1.0, 2.0, 5.0, -3.0, 0.3 })
            check(EraCellPartition.TryModular(4, sz, sidePreset.CellWidthM, sidePreset.CellLengthM, sidePreset.MaxCountU, sidePreset.MaxCountV, out var eraLayout, out _, sidePreset.FixedCountV) &&
                  eraLayout.CountU == 4 && eraLayout.CountV == 2 && Math.Abs(eraLayout.PitchUM - .125) < 1e-12 && Math.Abs(eraLayout.PitchVM - .25) < 1e-12,
                $"shared cell partition keeps two rows of 250 mm for scale Z {sz}");
        var sideBox = Bounds(Build(ModularPartKind.Housing, sideSpec, 3, 2));
        check(Math.Abs((sideBox.Z1 - sideBox.Z0) - .5) < 1e-9, "side module is 0.5 m high (two 250 mm rows) and scales in length only (X, 125 mm per cell)");

        // ---- Existing Duplet and Relikt behaviour is unchanged.
        check(EraCellPartition.TryModular(3, 5, .125, .25, 8, 8, out var dupletLayout, out _) && dupletLayout.CountU == 3 && dupletLayout.CountV == 5,
            "Duplet partition without the optional argument is unchanged");
        check(ModularCellGeometry.TryLayout(3, 5, ModularCellSpec.DupletV1, 8, 8, out var dupletCells) && dupletCells.CountV == 5 && ModularCellSpec.ForVariant(null) == ModularCellSpec.DupletV1 &&
              ModularCellSpec.ForVariant("slim") == ModularCellSpec.DupletSlim && !ModularCellSpec.DupletV1.ProceduralPanel && !ModularCellSpec.DupletSlim.ProceduralPanel &&
              ModularCellSpec.ReliktTurret.ProceduralPanel,
            "standard and slim Duplet specs and layout are unchanged");

        // ---- Tandem guard (precursor pass spends only the front layer of a stack), v0.20.0.
        {
            var resp = catalogue.Responses.Single(r => r.ResponseId == "dupletHullActive");
            var weight = ArmourResponseModel.AngularWeight(resp, "heat", 60);
            var factor = 1 - weight * (1 - resp.Calibration.HeatRetention);
            double Hit(ArmourShotState s, string k, bool intact) => ArmourResponseModel.Apply(catalogue, resp, s, k, "heat", true, true, 12, 60, 0, intact);
            foreach (var p in Parts.Where(p => p.Layers == 2))
            {
                var front = EraGeneratedDefinitions.Presets.Single(x => x.Cassette == p.Prefix + "Stage2Cell");
                var rear = EraGeneratedDefinitions.Presets.Single(x => x.Cassette == p.Prefix + "Stage1Cell");
                var mount = front.Mount.GetHashCode();
                var ledger = new EraCells();
                var cF = PlacedEraState.Key(9, 201, 0, 0, 0); var cR = PlacedEraState.Key(9, 202, 0, 0, 0);
                // Precursor pass: layer 1 (front) spends, layer 2 (rear) is kept.
                var pre = new ArmourShotState();
                var fi = ArmourResponseModel.ConsumeStacked(ledger, pre, cF, front.StageIndex, mount, true, out var fg);
                var rf = Hit(pre, "pf", fi);
                var ri = ArmourResponseModel.ConsumeStacked(ledger, pre, cR, rear.StageIndex, mount, true, out var rg);
                var rr = Hit(pre, "pr", ri);
                check(fi && !fg && Math.Abs(rf - factor) < 1e-9 && !ri && rg && rr == 1 && ledger.Contains(cF) && !ledger.Contains(cR),
                    $"{p.Key} precursor: layer 1 spent and reacts, layer 2 intact and does not react");
                // Main charge afterwards (own shot state, no guard): layer 1 spent, layer 2 reacts.
                var main = new ArmourShotState();
                var mf = ArmourResponseModel.ConsumeStacked(ledger, main, cF, front.StageIndex, mount, false, out _);
                var mfr = Hit(main, "mf", mf);
                var mr = ArmourResponseModel.ConsumeStacked(ledger, main, cR, rear.StageIndex, mount, false, out _);
                var mrr = Hit(main, "mr", mr);
                check(!mf && mfr == 1 && mr && Math.Abs(mrr - factor) < 1e-9, $"{p.Key} main charge after the precursor reacts with layer 2");
                // Non-tandem shot (rod or plain HEAT): both layers react.
                var ledger2 = new EraCells(); var plain = new ArmourShotState();
                var a = Hit(plain, "a", ArmourResponseModel.ConsumeStacked(ledger2, plain, cF, front.StageIndex, mount, false, out _));
                var b = Hit(plain, "b", ArmourResponseModel.ConsumeStacked(ledger2, plain, cR, rear.StageIndex, mount, false, out _));
                check(a < 1 && b < 1 && ledger2.Contains(cF) && ledger2.Contains(cR), $"{p.Key} non-tandem shot still reacts with both layers");
                // A second panel hit by the same pass keeps its own rear layer until its front layer was spent.
                var pre2 = new ArmourShotState(); var l3 = new EraCells();
                ArmourResponseModel.ConsumeStacked(l3, pre2, PlacedEraState.Key(9, 201, 0, 0, 0), 2, mount, true, out _);
                var otherRear = ArmourResponseModel.ConsumeStacked(l3, pre2, PlacedEraState.Key(9, 202, 0, 1, 0), 1, mount, true, out var og);
                check(otherRear && !og, $"{p.Key} guard is per grid cell: a neighbouring cell's rear layer is not suppressed");
            }
            // The guard condition is false for every other part: single-layer Nizh, Relikt, Kontakt, old two-stage legacy types.
            bool Guarded(EraVisualPreset x) => x.StageIndex > 0 && x.HousingActiveIds is { Length: >= 2 };
            var guarded = EraGeneratedDefinitions.Presets.Where(Guarded).Select(x => x.Cassette).ToHashSet();
            check(guarded.Count == 4 + 6 && EraGeneratedDefinitions.Presets.Where(x => !x.Cassette.StartsWith("duplet", StringComparison.Ordinal)).All(x => !Guarded(x)),
                "guard applies only to the modular Duplet stage cells (Nizh, Relikt, Kontakt and all legacy presets take the unchanged path)");
            var ld = new EraCells(); var st = new ArmourShotState(); var cc = PlacedEraState.Key(9, 300, 0, 0, 0);
            check(ArmourResponseModel.ConsumeStacked(ld, st, cc, 1, 0, false, out var none) && !none && !ArmourResponseModel.ConsumeStacked(ld, st, cc, 1, 0, false, out _) && st.StackGuard == null,
                "without the guard ConsumeStacked is exactly EraCells.Consume and never allocates guard state");

            // Micro-benchmark of the non-tandem path (informational, printed to the log).
            const int n = 1_000_000;
            var bl = new EraCells(); var bs = new ArmourShotState(); var bc = PlacedEraState.Key(9, 400, 0, 0, 0); bl.Consume(bc);
            var plainPreset = EraGeneratedDefinitions.Presets.First(x => x.Cassette == "reliktPanelTurretCell");
            long sink = 0;
            for (var w = 0; w < 200_000; w++) { sink += bl.Consume(bc) ? 1 : 0; sink += ArmourResponseModel.ConsumeStacked(bl, bs, bc, 0, 0, false, out _) ? 1 : 0; }
            // Alternate measurement order and take the median paired delta. A single
            // sequential pair measured scheduler/tiered-JIT drift as guard overhead.
            double Measure(bool guard)
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                if(!guard){for(int i=0;i<n;i++)sink+=bl.Consume(bc)?1:0;}
                else for(int i=0;i<n;i++)
                {
                    var g=plainPreset.StageIndex>0&&plainPreset.HousingActiveIds is {Length:>=2};
                    sink+=ArmourResponseModel.ConsumeStacked(bl,bs,bc,g?plainPreset.StageIndex:0,0,g,out _)?1:0;
                }
                return timer.Elapsed.TotalMilliseconds*1e6/n;
            }
            Measure(false);Measure(true);
            var directTimes=new List<double>();var guardTimes=new List<double>();var deltas=new List<double>();
            for(int sample=0;sample<9;sample++)
            {
                double direct,guarded2;
                if(sample%2==0){direct=Measure(false);guarded2=Measure(true);}
                else{guarded2=Measure(true);direct=Measure(false);}
                directTimes.Add(direct);guardTimes.Add(guarded2);deltas.Add(guarded2-direct);
            }
            var delta=deltas.OrderBy(x=>x).ElementAt(4);
            Console.WriteLine($"TANDEM GUARD BENCH non-tandem path: 9 alternating pairs; before median {directTimes.OrderBy(x=>x).ElementAt(4):0.0} ns/call, after median {guardTimes.OrderBy(x=>x).ElementAt(4):0.0}; paired median delta {delta:0.0} ns, range {deltas.Min():0.0}..{deltas.Max():0.0}; sink {sink}");
            check(delta<25,$"non-tandem guard overhead stays below 25 ns per consume (paired median {delta:0.0} ns)");
        }

        // ---- Aliases: hidden, unranked, still defined and loadable with their original presets.
        foreach (var guid in HiddenGuids)
        {
            var presets = EraGeneratedDefinitions.Presets.Where(p => p.PartGuid == guid).ToArray();
            check(EraGeneratedDefinitions.LegacyPartGuids.Contains(guid) && !EraMenuOrder.Ranks.ContainsKey(guid) && presets.Length >= 1,
                $"old part {guid[..8]} is hidden and unranked but still has {presets.Length} preset(s)");
        }
        check(HiddenGuids.Distinct().Count() == 12, "twelve old Duplet and Nizh parts are hidden");
        check(EraGeneratedDefinitions.Presets.Where(p => p.PartGuid is "1873e6e7-451d-53a7-ad0b-1c496d0c1205" or "b3de0597-85c1-5720-bf38-463d6900058c").Count() == 4 &&
              EraGeneratedDefinitions.Presets.Count(p => p.WholeCells) == 4 + 3 + 1 + 6, "all fourteen whole-cell presets are defined (4 old modular Duplet, 3 Relikt, 7 new)");
        var lm = "0f0f3f44-3931-5d72-bd5c-5d2159b13dc6";
        check(!EraGeneratedDefinitions.LegacyPartGuids.Contains(lm) && EraMenuOrder.Ranks[lm] == 404, "Nizh-LM stays visible (rank 404)");
        check(EraMenuOrder.Ranks["d706e182-a7c5-5a2a-9231-681480af74cc"] == 400 && EraMenuOrder.Ranks["ea9df1d4-18f5-5c8f-a6d0-d2c20cd3bc91"] == 401 &&
              EraMenuOrder.Ranks["3532720a-7150-51da-85a5-00d94530dd46"] == 402 && EraMenuOrder.Ranks["ac5d72a9-cbfc-5fa5-80da-061b75ea8c4c"] == 403,
            "menu ranks: Nizh panel 400, Duplet turret 401, hull 402, side 403");
    }

    private static int ReadBigEndian(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static void WriteObj(string path, string comment, params (string Group, ModularMeshData Mesh)[] groups)
    {
        var sb = new StringBuilder("# " + comment + "\n# metres; Y is the mounting normal (front plate at +Y)\n");
        var offset = 0;
        foreach (var (group, mesh) in groups)
        {
            foreach (var v in mesh.Vertices)
                sb.Append("v ").Append(v.X.ToString("0.000000", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(v.Y.ToString("0.000000", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(v.Z.ToString("0.000000", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("g ").Append(group).Append('\n');
            for (var i = 0; i < mesh.Triangles.Length; i += 3)
                sb.Append("f ").Append(mesh.Triangles[i] + 1 + offset).Append(' ').Append(mesh.Triangles[i + 1] + 1 + offset).Append(' ')
                    .Append(mesh.Triangles[i + 2] + 1 + offset).Append('\n');
            offset += mesh.Vertices.Length;
        }
        File.WriteAllText(path, sb.ToString());
    }

    // Viewer inputs. Groups per model: housing (frame), front (lids), rear (intermediate plate pads, Duplet only),
    // grooves1 (front Nozh layer relief), grooves2 (rear layer relief, Duplet only), plates1/plates2 (active slabs, collision).
    private static void ExportViewer(string directory)
    {
        Directory.CreateDirectory(directory);
        var nizh = ModularCellSpec.NizhPanel; var turret = ModularCellSpec.DupletTurret; var hull = ModularCellSpec.DupletHull; var sideSpec = ModularCellSpec.DupletSide;
        var models = new (string Key, string Title, ModularCellSpec Spec, int U, int V, int[] FrontSpent, int[] RearSpent)[]
        {
            ("nizh-2x2", "NIZH PANEL 2x2", nizh, 2, 2, Array.Empty<int>(), Array.Empty<int>()),
            ("turret-1x1", "DUPLET TURRET 1x1", turret, 1, 1, Array.Empty<int>(), Array.Empty<int>()),
            ("turret-3x3", "DUPLET TURRET 3x3", turret, 3, 3, Array.Empty<int>(), Array.Empty<int>()),
            ("hull-2x4", "DUPLET HULL 2x4", hull, 2, 4, Array.Empty<int>(), Array.Empty<int>()),
            ("side-4", "DUPLET SIDE 4 long", sideSpec, 4, 2, Array.Empty<int>(), Array.Empty<int>()),
            ("turret-3x3-spent", "DUPLET TURRET 3x3: cells 1+4 layer 1 spent, cell 7 both layers spent", turret, 3, 3, new[] { 1, 4, 7 }, new[] { 7 }),
            ("icon-nizh", "icon source: Nizh panel 2x2", nizh, 2, 2, Array.Empty<int>(), Array.Empty<int>()),
            ("icon-turret", "icon source: Duplet turret 2x2", turret, 2, 2, Array.Empty<int>(), Array.Empty<int>()),
            ("icon-hull", "icon source: Duplet hull 2x2", hull, 2, 2, Array.Empty<int>(), Array.Empty<int>()),
            ("icon-side", "icon source: Duplet side 3 long", sideSpec, 3, 2, Array.Empty<int>(), Array.Empty<int>()),
        };
        var summary = new StringBuilder();
        foreach (var (key, title, spec, u, v, frontSpent, rearSpent) in models)
        {
            var single = DupletModularGeometry.SingleLayer(spec);
            var layout = new ModularCellLayout(u, v);
            var frontSet = frontSpent.Length == 0 ? null : new HashSet<int>(frontSpent);
            var rearSet = rearSpent.Length == 0 ? null : new HashSet<int>(rearSpent);
            var housing = Build(ModularPartKind.HousingVisual, spec, u, v);
            var groups = new List<(string, ModularMeshData)> { ("housing", housing) };
            groups.Add(("front", Build(single ? ModularPartKind.Stage1Visual : ModularPartKind.Stage2Visual, spec, u, v, frontSet)));
            if (!single) groups.Add(("rear", Build(ModularPartKind.Stage1Visual, spec, u, v, rearSet)));
            groups.Add(("grooves1", DupletModularGeometry.LayerRelief(layout, spec, true)));
            if (!single) groups.Add(("grooves2", DupletModularGeometry.LayerRelief(layout, spec, false)));
            groups.Add(("plates1", Build(single ? ModularPartKind.Stage1Collision : ModularPartKind.Stage2Collision, spec, u, v)));
            if (!single) groups.Add(("plates2", Build(ModularPartKind.Stage1Collision, spec, u, v)));
            // The combined OBJ shows what the game draws: frame + lids + pads (never the relief or the slabs).
            WriteObj(Path.Combine(directory, key + ".obj"), title + " (game visuals)", groups.Where(g => g.Item1 is "housing" or "front" or "rear").ToArray());
            foreach (var (group, mesh) in groups)
                WriteObj(Path.Combine(directory, key + "-" + group + ".obj"), title + " " + group, (group, mesh));
            summary.Append($"{key}\t{u}x{v}\t").Append(string.Join('\t', groups.Select(g => $"{g.Item1}Tri={Tris(g.Item2)}"))).Append('\n');
        }
        // Budget table for the report: triangles at 1x1 and 8x8 per mesh, intact and all spent.
        foreach (var info in Parts)
        {
            var (mu, mv) = info.Spec.FixedRows > 0 ? (8, 2) : (8, 8);
            foreach (var (u, v) in new[] { (1, info.Spec.FixedRows > 0 ? 2 : 1), (mu, mv) })
            {
                var all = new HashSet<int>(Enumerable.Range(0, u * v));
                var parts = new List<string> { $"housing={Tris(Build(ModularPartKind.Housing, info.Spec, u, v))}", $"housingVisual={Tris(Build(ModularPartKind.HousingVisual, info.Spec, u, v))}" };
                foreach (var kind in info.Layers == 1 ? new[] { ModularPartKind.Stage1Visual } : new[] { ModularPartKind.Stage1Visual, ModularPartKind.Stage2Visual })
                    parts.Add($"{kind}={Tris(Build(kind, info.Spec, u, v))}/spent={Tris(Build(kind, info.Spec, u, v, all))}");
                foreach (var kind in info.Layers == 1 ? new[] { ModularPartKind.Stage1Collision } : new[] { ModularPartKind.Stage1Collision, ModularPartKind.Stage2Collision })
                    parts.Add($"{kind}={Tris(Build(kind, info.Spec, u, v))}");
                summary.Append($"budget\t{info.Key}\t{u}x{v}\t").Append(string.Join('\t', parts)).Append('\n');
            }
        }
        File.WriteAllText(Path.Combine(directory, "export-summary.txt"), summary.ToString());
    }
}
