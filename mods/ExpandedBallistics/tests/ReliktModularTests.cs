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

// Modular Relikt (turret panel, hull panel, side box), v0.19.0.
internal static class ReliktModularTests
{
    private static readonly (string Name, ModularCellSpec Spec, string Variant, string Guid, string Prefix,
        double Depth, double LidBottom)[] Parts =
    {
        ("turret", ModularCellSpec.ReliktTurret, "reliktTurret", "6b4e1dc7-9020-5443-90c2-0f52c80933f9", "reliktPanelTurret", .150, .135),
        ("hull", ModularCellSpec.ReliktHull, "reliktHull", "c8bc7cd9-1e9a-5a53-a613-306846df488a", "reliktPanelHull", .136, .128),
        ("side", ModularCellSpec.ReliktSide, "reliktSide", "dd04b5c1-ed08-5406-bec5-de39cfe21814", "reliktSideBox", .136, .128),
    };

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "ReliktAssets", name);

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
        // .NET stores the first three groups little-endian; RFC 4122 hashes network order.
        Array.Reverse(ns, 0, 4); Array.Reverse(ns, 4, 2); Array.Reverse(ns, 6, 2);
        var hash = SHA1.HashData(ns.Concat(Encoding.UTF8.GetBytes(name)).ToArray());
        var bytes = hash.Take(16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        Array.Reverse(bytes, 0, 4); Array.Reverse(bytes, 4, 2); Array.Reverse(bytes, 6, 2);
        return new Guid(bytes);
    }

    internal static void Run(Action<bool, string> check)
    {
        // Optional: RELIKT_VIEWER_DIR writes the viewer/preview OBJ set before any check runs.
        var export = Environment.GetEnvironmentVariable("RELIKT_VIEWER_DIR");
        if (!string.IsNullOrEmpty(export)) ExportViewer(export);
        // ---- Original active cell, read from the shipped 0.18.x OBJ.
        var original = new List<Vector3>();
        foreach (var line in File.ReadLines(Asset("reliktCompact-active-stage-1-cell-1.obj")))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 4 && p[0] == "v")
                original.Add(new(float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture),
                    float.Parse(p[3], CultureInfo.InvariantCulture)));
        }
        var originalCorners = original.Select(v => (X: Math.Round(v.X, 6), Y: Math.Round(v.Y, 6), Z: Math.Round(v.Z + .075, 6))).Distinct().OrderBy(v => v).ToArray();
        check(originalCorners.Length == 8, "original Relikt cell has eight corners");

        check(ReliktModularGeometry.ActiveVolumeM3 == .25 * .13 * .013 && Math.Abs(ReliktModularGeometry.ActiveVolumeM3 - 0.0004225) < 1e-12,
            "active cell volume is 250 x 130 x 13 mm = 422.5 cm3");
        check(Math.Abs(ReliktModularGeometry.PlateAngleDegrees - 25) < 1e-12 && Math.Abs(ReliktModularGeometry.PlateMaxYM - .1275) < 1e-12,
            "active plate angle 25 degrees, top edge 127.5 mm");

        foreach (var (name, spec, variant, guid, prefix, depth, lidBottom) in Parts)
        {
            var side = spec.ReliktStyle == 3;
            var layouts = side
                ? new[] { (1, 2), (2, 2), (3, 2), (4, 2), (8, 2) }
                : new[] { (1, 1), (1, 2), (2, 1), (2, 2), (3, 3), (2, 6), (8, 8) };
            var tag = $"{name}";

            // Cell envelope and depths.
            check(Math.Abs(spec.CellWidthM - .270) < 1e-12 && Math.Abs(spec.CellLengthM - .150) < 1e-12 && Math.Abs(spec.DepthM - depth) < 1e-12,
                $"{tag} cell pitch 270 x 150 mm, depth {depth * 1000:0} mm");
            check(spec.Valid && spec.ReliktStyle is >= 1 and <= 3, $"{tag} spec is valid");
            check(lidBottom - ReliktModularGeometry.PlateMaxYM >= .0005 - 1e-12 && Math.Abs(spec.Stage1CoverBottomM - lidBottom) < 1e-12 &&
                  Math.Abs(spec.Stage1CoverTopM - depth) < 1e-12,
                $"{tag} lid clears the inclined plate by at least 0.5 mm ({(lidBottom - ReliktModularGeometry.PlateMaxYM) * 1000:0.0} mm)");
            check(ModularCellSpec.ForVariant(variant) == spec, $"{tag} variant maps to its spec");
            check(spec.FixedRows == (side ? 2 : 0), $"{tag} fixed rows {(side ? 2 : 0)}");

            foreach (var (u, v) in layouts)
            {
                var layout = new ModularCellLayout(u, v);
                var l = $"{tag} {u}x{v}";
                var width = u * spec.CellWidthM;
                var length = v * spec.CellLengthM;

                // Collision: closed, exact paid volume, 13 mm depth, exact original geometry per cell.
                var collision = Build(ModularPartKind.Stage1Collision, spec, u, v);
                check(ModularCellGeometry.IsWatertightByPosition(collision) && ConsistentWinding(collision), $"{l} collision closed with consistent winding");
                check(Math.Abs(ModularCellGeometry.SignedVolume(collision) - u * v * ReliktModularGeometry.ActiveVolumeM3) < 1e-8 &&
                      Math.Abs(ModularCellGeometry.SignedVolume(collision) - ModularCellGeometry.ExpectedSlabVolume(layout, spec)) < 1e-8,
                    $"{l} paid active volume equals {u * v} original cells");
                check(Tris(collision) == 12 * u * v && collision.Vertices.Length == 8 * u * v, $"{l} collision is exactly {u * v} cuboids (12 tri / 8 vertices each)");
                check(Math.Abs(PlacedPartDepth.Measure(collision.Vertices, collision.Triangles, Vector3.One) - 13.0) < 1e-3,
                    $"{l} production depth helper measures the 13 mm plate");
                var cb = Bounds(collision);
                check(Math.Abs(cb.Y1 - .1275) < 2e-6 && cb.Y0 > spec.HousingBottomM, $"{l} plates top at 127.5 mm and clear the pan");
                for (var y = 0; y < v; y++)
                    for (var x = 0; x < u; x++)
                    {
                        var b = ModularCellGeometry.CellBounds(layout, spec, x, y);
                        var cell = layout.Index(x, y);
                        var corners = Enumerable.Range(0, collision.Triangles.Length / 3).Where(t => collision.TriangleCells[t] == cell)
                            .SelectMany(t => Enumerable.Range(0, 3).Select(k => collision.Vertices[collision.Triangles[t * 3 + k]]))
                            .Select(p => (X: Math.Round(p.X - (b.X0 + b.X1) / 2, 6), Y: Math.Round(p.Y, 6), Z: Math.Round(p.Z - (b.Z0 + b.Z1) / 2, 6)))
                            .Distinct().OrderBy(p => p).ToArray();
                        check(corners.Length == 8 && corners.Zip(originalCorners).All(pair =>
                                Math.Abs(pair.First.X - pair.Second.X) < 3e-6 && Math.Abs(pair.First.Y - pair.Second.Y) < 3e-6 && Math.Abs(pair.First.Z - pair.Second.Z) < 3e-6),
                            $"{l} cell {x},{y} equals the original Relikt cell corner for corner");
                    }

                // Inverse scale compensation keeps world metres on a scaled native part.
                var scaled = Build(ModularPartKind.Stage1Collision, spec, u, v, null, 2.0, side ? 2.0 : 3.0);
                check(Math.Abs(PlacedPartDepth.Measure(scaled.Vertices, scaled.Triangles, new Vector3(2f, 1f, side ? 2f : 3f)) - 13.0) < 1e-3,
                    $"{l} scale-compensated collision still measures 13 mm in world space");

                // Housing and visual footprint.
                var housing = Build(ModularPartKind.Housing, spec, u, v);
                var hb = Bounds(housing);
                check(Math.Abs(hb.X0 + width / 2) < 1e-6 && Math.Abs(hb.X1 - width / 2) < 1e-6 && Math.Abs(hb.Z0 + length / 2) < 1e-6 &&
                      Math.Abs(hb.Z1 - length / 2) < 1e-6 && Math.Abs(hb.Y0) < 1e-9 && Math.Abs(hb.Y1 - depth) < 1e-6,
                    $"{l} housing footprint snaps to {width * 1000:0} x {length * 1000:0} mm, {depth * 1000:0} mm deep");
                check(ModularCellGeometry.IsWatertightByPosition(housing) && ConsistentWinding(housing) && ModularCellGeometry.SignedVolume(housing) > 0,
                    $"{l} housing is closed and outward");
                var housingVisual = Build(ModularPartKind.HousingVisual, spec, u, v);
                check(ModularCellGeometry.IsWatertightByPosition(housingVisual) && ConsistentWinding(housingVisual) && Tris(housingVisual) <= 8000 &&
                      housingVisual.Vertices.Length <= 8000, $"{l} housing visual is closed and inside the budget");

                // Cell visuals: every shell closed, inside the budget, every cell owns detail.
                var visual = Build(ModularPartKind.Stage1Visual, spec, u, v);
                check(ModularCellGeometry.IsWatertightByPosition(visual) && ConsistentWinding(visual) && ModularCellGeometry.SignedVolume(visual) > 0,
                    $"{l} cell visual is closed and outward");
                check(Tris(visual) <= 8000 && visual.Vertices.Length <= 8000 && visual.Triangles.Length <= 24000, $"{l} cell visual within 8000 tri / 8000 vertices ({Tris(visual)})");
                var coarse = Build(ModularPartKind.Stage1Visual, spec, u, v, null, 1, 1, true);
                check(Tris(coarse) < Tris(visual) && ModularCellGeometry.IsWatertightByPosition(coarse), $"{l} coarse LOD is lighter and closed");
                for (var y = 0; y < v; y++)
                    for (var x = 0; x < u; x++)
                        check(Enumerable.Range(0, Tris(visual)).Count(t => visual.TriangleCells[t] == layout.Index(x, y)) >= 28,
                            $"{l} cell {x},{y} owns real lid detail");
            }

            // Budgets in every spent state at the largest size.
            var (mu, mv) = side ? (8, 2) : (8, 8);
            var maxLayout = new ModularCellLayout(mu, mv);
            var allSpent = new HashSet<int>(Enumerable.Range(0, mu * mv));
            var half = new HashSet<int>(Enumerable.Range(0, mu * mv).Where(i => i % 2 == 0));
            var intactMax = Build(ModularPartKind.Stage1Visual, spec, mu, mv);
            foreach (var (set, label) in new[] { (allSpent, "all spent"), (half, "half spent") })
            {
                var m = Build(ModularPartKind.Stage1Visual, spec, mu, mv, set);
                check(Tris(m) <= 8000 && m.Vertices.Length <= 8000 && Tris(m) > 0 && ModularCellGeometry.IsWatertightByPosition(m) && ConsistentWinding(m),
                    $"{tag} {mu}x{mv} {label}: {Tris(m)} tri / {m.Vertices.Length} vertices, closed");
            }
            var oneSpent = Build(ModularPartKind.Stage1Visual, spec, 3, side ? 2 : 3, new HashSet<int> { 1 });
            var noneSpent = Build(ModularPartKind.Stage1Visual, spec, 3, side ? 2 : 3);
            var lay3 = new ModularCellLayout(3, side ? 2 : 3);
            int Owned(ModularMeshData m, int c) => Enumerable.Range(0, Tris(m)).Count(t => m.TriangleCells[t] == c);
            check(Owned(oneSpent, 1) != Owned(noneSpent, 1) &&
                  Enumerable.Range(0, lay3.CellCount).Where(c => c != 1).All(c => Owned(oneSpent, c) == Owned(noneSpent, c)),
                $"{tag} spending one cell changes only that cell's triangles");
            // The lid top of a spent cell is gone; neighbours keep theirs.
            bool HasLidTop(ModularMeshData m, int cell)
            {
                var b = ModularCellGeometry.CellBounds(lay3, spec, cell % 3, cell / 3);
                var cx = (b.X0 + b.X1) / 2; var cz = (b.Z0 + b.Z1) / 2;
                return Enumerable.Range(0, Tris(m)).Any(t => m.TriangleCells[t] == cell && Enumerable.Range(0, 3).All(k =>
                {
                    var p = m.Vertices[m.Triangles[t * 3 + k]];
                    return Math.Abs(p.Y - spec.Stage1CoverTopM) < 1e-6 && Math.Abs(p.X - cx) < .14 && Math.Abs(p.Z - cz) < .08;
                }));
            }
            check(HasLidTop(noneSpent, 1) && !HasLidTop(oneSpent, 1) && HasLidTop(oneSpent, 0) && HasLidTop(oneSpent, 2),
                $"{tag} spent cell loses its lid, neighbours keep theirs");
            var spentBox = Bounds(Build(ModularPartKind.Stage1Visual, spec, 1, side ? 2 : 1, new HashSet<int>(Enumerable.Range(0, side ? 2 : 1))));
            check(spentBox.Y1 < spec.Stage1CoverTopM + .010 && spentBox.Y1 > spec.Stage1CoverBottomM, $"{tag} fully spent cell keeps rim and bolt stubs below the lid line");
            check(Tris(Build(ModularPartKind.Stage1Visual, spec, 1, side ? 2 : 1, new HashSet<int>(Enumerable.Range(0, side ? 2 : 1)))) > 0,
                $"{tag} fully spent part still has visible damage geometry");

            // Single stage only.
            var rejected = false;
            try { Build(ModularPartKind.Stage2Visual, spec, 1, side ? 2 : 1); } catch (InvalidDataException) { rejected = true; }
            check(rejected, $"{tag} has no second stage");

            // Mass.
            var preset = EraGeneratedDefinitions.Presets.Single(p => p.PartGuid == guid);
            var baseHousing = ModularCellGeometry.BaseHousingMass(spec);
            check(Math.Abs(baseHousing - ModularCellGeometry.SignedVolume(Build(ModularPartKind.Housing, spec, 1, 1)) * 7850) < 1e-9 &&
                  Math.Abs(preset.MountMass - baseHousing) < 1e-4 && Math.Abs(preset.CassetteMass - 1.4) < 1e-6 &&
                  Math.Abs(ModularCellGeometry.BaseStageMass(spec) - 1.4) < 1e-12,
                $"{tag} preset masses are the 1x1 housing volume x 7850 and 1.4 kg per cell ({preset.MountMass:0.0000} / {preset.CassetteMass:0.0})");
            check(Math.Abs(1.4 / ReliktModularGeometry.ActiveVolumeM3 - 3313.609467455621) < 1e-6,
                $"{tag} 1.4 kg per cell keeps the reliktActive density");

            // Preset wiring.
            check(preset.WholeCells && preset.Resizable && preset.ModularVariant == variant && preset.MaxCountU == 8 &&
                  preset.MaxCountV == (side ? 2 : 8) && preset.FixedCountV == (side ? 2 : 0) && preset.StageIndex == 1 &&
                  Math.Abs(preset.CellWidthM - .27) < 1e-12 && Math.Abs(preset.CellLengthM - .15) < 1e-12 &&
                  preset.Cassette == prefix + "Cell" && preset.Mount == prefix + "Housing" && preset.SpentVisual == null,
                $"{tag} preset describes a single-stage whole-cell panel");
            check(!EraGeneratedDefinitions.LegacyPartGuids.Contains(guid) && EraMenuOrder.Ranks.ContainsKey(guid), $"{tag} is visible and ranked");

            // GUID derivation.
            var partName = prefix + "V1";
            check(UuidV5("sprocket-expanded-ballistics:era:" + partName).ToString() == guid, $"{tag} GUID is UUIDv5(URL namespace, sprocket-expanded-ballistics:era:{partName})");

            // Parts and localization files.
            using var json = JsonDocument.Parse(File.ReadAllText(Asset(partName + "Part.json")));
            var root = json.RootElement;
            var components = root.GetProperty("components").EnumerateArray().ToArray();
            check(root.GetProperty("guid").GetString() == guid && root.GetProperty("name").GetString() == partName && components.Length == 2 &&
                  components[0].GetProperty("fileID").GetString() == prefix + "Housing" && components[1].GetProperty("fileID").GetString() == prefix + "Cell" &&
                  components[0].GetProperty("info").GetProperty("durableMaterialID").GetString() == "rha" &&
                  components[1].GetProperty("info").GetProperty("durableMaterialID").GetString() == "reliktActive" &&
                  Math.Abs(components[0].GetProperty("info").GetProperty("mass").GetDouble() - preset.MountMass) < 1e-5 &&
                  Math.Abs(components[1].GetProperty("info").GetProperty("mass").GetDouble() - 1.4) < 1e-12,
                $"{tag} Parts json carries GUID, two components, reliktActive and the preset masses");
            var transform = root.GetProperty("transform");
            double[] Triple(string key) => transform.GetProperty(key).EnumerateArray().Select(e => e.GetDouble()).ToArray();
            check(Triple("minScale").SequenceEqual(new[] { 1.0, 1.0, side ? 2.0 : 1.0 }) && Triple("maxScale").SequenceEqual(new[] { 8.0, 1.0, side ? 2.0 : 8.0 }) &&
                  Triple("scale").SequenceEqual(new[] { 1.0, 1.0, side ? 2.0 : 1.0 }) && transform.GetProperty("scaling").GetBoolean(),
                $"{tag} Parts json scale limits {(side ? "1..8 x fixed 2" : "1..8 x 1..8")}");
            var xml = File.ReadAllText(Asset(partName + ".xml"));
            check(xml.Contains("<name>" + (side ? "Relikt side box" : name == "turret" ? "Relikt panel (turret)" : "Relikt panel (hull)") + "</name>"),
                $"{tag} localization names the menu entry");

            // Icon.
            var icon = File.ReadAllBytes(Asset(prefix + "-icon.png"));
            check(icon.Length > 1000 && icon[1] == (byte)'P' && ReadBigEndian(icon, 16) == 256 && ReadBigEndian(icon, 20) == 256 && icon[24] == 8 && icon[25] == 6,
                $"{tag} icon is 256x256 8-bit RGBA");

            // Binding.
            var bindings = EraPartBindings.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "physical-era-bindings.json")));
            check(bindings.TryGetCassette(prefix + "Cell", out var binding) && binding.MountComponentId == prefix + "Housing" &&
                  binding.ResponseId == "reliktActive" && binding.MaterialId == "reliktActive" && binding.Kind == "heavyEra" && !bindings.TryGetCassette(prefix + "Housing", out _),
                $"{tag} cell is bound to the unchanged reliktActive response, housing passive");
        }

        // 1x1 + 1x1 = 1x2 in dimension and in plate positions.
        var one = Build(ModularPartKind.Stage1Collision, ModularCellSpec.ReliktTurret, 1, 1);
        var two = Build(ModularPartKind.Stage1Collision, ModularCellSpec.ReliktTurret, 2, 1);
        var shifted = one.Vertices.Select(v => (Math.Round(v.X - .135, 5), Math.Round(v.Y, 5), Math.Round(v.Z, 5)))
            .Concat(one.Vertices.Select(v => (Math.Round(v.X + .135, 5), Math.Round(v.Y, 5), Math.Round(v.Z, 5)))).OrderBy(v => v).ToArray();
        var merged = two.Vertices.Select(v => (Math.Round(v.X, 5), Math.Round(v.Y, 5), Math.Round(v.Z, 5))).OrderBy(v => v).ToArray();
        check(shifted.SequenceEqual(merged), "two 1x1 turret panels side by side have exactly the plates of one 2x1 panel");
        var h1 = Bounds(Build(ModularPartKind.Housing, ModularCellSpec.ReliktTurret, 1, 1));
        var h2 = Bounds(Build(ModularPartKind.Housing, ModularCellSpec.ReliktTurret, 2, 1));
        check(Math.Abs((h2.X1 - h2.X0) - 2 * (h1.X1 - h1.X0)) < 1e-6 && Math.Abs((h2.Z1 - h2.Z0) - (h1.Z1 - h1.Z0)) < 1e-6,
            "1x1 + 1x1 housing envelope equals the 2x1 envelope");

        // Side box: height is fixed at two rows.
        var sideSpec = ModularCellSpec.ReliktSide;
        foreach (var sz in new[] { 1.0, 2.0, 3.0, 5.0, 0.3, -1.0, -4.0 })
            check(ModularCellGeometry.TryLayout(3, sz, sideSpec, 8, 2, out var fixedLayout) && fixedLayout.CountU == 3 && fixedLayout.CountV == 2,
                $"side box scale Z {sz} clamps to exactly two rows");
        check(!ModularCellGeometry.TryLayout(3, 0, sideSpec, 8, 2, out _) && !ModularCellGeometry.TryLayout(double.NaN, 2, sideSpec, 8, 2, out _) &&
              !ModularCellGeometry.TryLayout(3, double.PositiveInfinity, sideSpec, 8, 2, out _), "side box still rejects zero and non-finite scale");
        foreach (var sz in new[] { 1.0, 2.0, 5.0, -3.0, 0.3 })
            check(EraCellPartition.TryModular(4, sz, .27, .15, 8, 2, out var eraLayout, out _, 2) && eraLayout.CountU == 4 && eraLayout.CountV == 2 &&
                  Math.Abs(eraLayout.PitchUM - .27) < 1e-12 && Math.Abs(eraLayout.PitchVM - .15) < 1e-12,
                $"shared cell partition keeps two rows for scale Z {sz}");
        check(!EraCellPartition.TryModular(4, 2, .27, .15, 8, 2, out _, out _, 3) && !EraCellPartition.TryModular(4, 2, .27, .15, 8, 2, out _, out _, -1),
            "shared cell partition rejects an invalid fixed row count");
        check(EraCellPartition.TryModular(3, 5, .125, .25, 8, 8, out var dupletLayout, out _) && dupletLayout.CountU == 3 && dupletLayout.CountV == 5,
            "Duplet partition without the optional argument is unchanged");
        check(ModularCellGeometry.TryLayout(3, 5, ModularCellSpec.DupletV1, 8, 8, out var dupletCells) && dupletCells.CountV == 5,
            "Duplet layout is unchanged");

        // Legacy aliases: hidden, still defined with their original assets and masses.
        var legacy = new (string Guid, string Cassette, float Mount)[]
        {
            ("ad460401-467a-548f-97d3-875666eaf742", "reliktTurretCompact11Stage1Cell1", 3.601831f),
            ("de46580b-dc12-5004-855a-ce85694cef85", "reliktCompactStage1Cell1", 5.495251f),
            ("ade97085-9eb6-5b71-a12a-38dea4d7351f", "reliktTurret2Plus1Stage1Cell1", 7.564134f),
            ("5a88679a-ee60-5a12-93b3-5acbe780854b", "reliktHull22Stage1Cell1", 9.34263f),
        };
        foreach (var (guid, cassette, mount) in legacy)
        {
            var preset = EraGeneratedDefinitions.Presets.Single(p => p.Cassette == cassette);
            check(EraGeneratedDefinitions.LegacyPartGuids.Contains(guid) && !EraMenuOrder.Ranks.ContainsKey(guid) && preset.PartGuid == guid &&
                  preset.MountMass == mount && preset.CassetteMass == 1.4f && !preset.WholeCells && preset.Collision.EndsWith("-active-stage-1-cell-1.obj"),
                $"old Relikt {guid[..8]} is hidden but still defined with its original preset");
        }
        check(EraMenuOrder.Ranks["6b4e1dc7-9020-5443-90c2-0f52c80933f9"] == 300 && EraMenuOrder.Ranks["c8bc7cd9-1e9a-5a53-a613-306846df488a"] == 301 &&
              EraMenuOrder.Ranks["dd04b5c1-ed08-5406-bec5-de39cfe21814"] == 302, "Relikt menu ranks are 300, 301, 302");
        check(EraGeneratedDefinitions.Presets.Count(p => p.WholeCells) == 4 + 3 + 7, "only the four old Duplet modular, three Relikt modular and seven Nizh/Duplet panel presets are whole-cell");
        var hiddenGuids = new[] { "ad460401-467a-548f-97d3-875666eaf742", "de46580b-dc12-5004-855a-ce85694cef85", "ade97085-9eb6-5b71-a12a-38dea4d7351f", "5a88679a-ee60-5a12-93b3-5acbe780854b" };
        foreach (var g in hiddenGuids)
            check(EraGeneratedDefinitions.MenuLabels.ContainsKey(g), "legacy Relikt menu label retained for saved-blueprint lookups");

    }

    private static int ReadBigEndian(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static void WriteObj(string path, string comment, params (string Group, ModularMeshData Mesh)[] groups)
    {
        var sb = new StringBuilder("# " + comment + "\n# metres; Y is the mounting normal; X rises with the plate\n");
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

    // Viewer inputs: housing / cells / plates per model, plus one combined OBJ for each.
    private static void ExportViewer(string directory)
    {
        Directory.CreateDirectory(directory);
        var models = new (string Key, string Title, ModularCellSpec Spec, int U, int V, int[] Spent)[]
        {
            ("turret-1x1", "RELIKT TURRET 1x1", ModularCellSpec.ReliktTurret, 1, 1, Array.Empty<int>()),
            ("turret-3x3", "RELIKT TURRET 3x3", ModularCellSpec.ReliktTurret, 3, 3, Array.Empty<int>()),
            ("hull-2x6", "RELIKT HULL 2x6", ModularCellSpec.ReliktHull, 2, 6, Array.Empty<int>()),
            ("side-4", "RELIKT SIDE BOX 4 long", ModularCellSpec.ReliktSide, 4, 2, Array.Empty<int>()),
            ("turret-3x3-2spent", "RELIKT TURRET 3x3, 2 cells spent", ModularCellSpec.ReliktTurret, 3, 3, new[] { 4, 7 }),
            ("icon-turret", "icon source: turret 2x2", ModularCellSpec.ReliktTurret, 2, 2, Array.Empty<int>()),
            ("icon-hull", "icon source: hull 2x2", ModularCellSpec.ReliktHull, 2, 2, Array.Empty<int>()),
            ("icon-side", "icon source: side box 3 long", ModularCellSpec.ReliktSide, 3, 2, Array.Empty<int>()),
        };
        var summary = new StringBuilder();
        foreach (var (key, title, spec, u, v, spent) in models)
        {
            var set = spent.Length == 0 ? null : new HashSet<int>(spent);
            var housing = Build(ModularPartKind.HousingVisual, spec, u, v);
            var cells = Build(ModularPartKind.Stage1Visual, spec, u, v, set);
            var plates = Build(ModularPartKind.Stage1Collision, spec, u, v);
            WriteObj(Path.Combine(directory, key + ".obj"), title + " (housing visual + cell visuals)", ("housing", housing), ("cells", cells));
            WriteObj(Path.Combine(directory, key + "-housing.obj"), title + " housing", ("housing", housing));
            WriteObj(Path.Combine(directory, key + "-cells.obj"), title + " cell visuals", ("cells", cells));
            WriteObj(Path.Combine(directory, key + "-plates.obj"), title + " active plates (collision)", ("plates", plates));
            summary.Append($"{key}\t{u}x{v}\thousingTri={Tris(housing)}\tcellTri={Tris(cells)}\tplateTri={Tris(plates)}\n");
        }
        File.WriteAllText(Path.Combine(directory, "export-summary.txt"), summary.ToString());
    }
}
