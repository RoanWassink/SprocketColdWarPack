using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SprocketExpandedBallistics.Era;
using SprocketExpandedBallistics;

namespace SprocketExpandedBallistics.Tests;

internal static class ModularDupletTests
{
    private static readonly ModularCellSpec Spec = ModularCellSpec.DupletV1;

    internal static void Run(Action<bool, string> check)
    {
        // Slim-v8 standard envelope, matching KhSChKV-34. These are gameplay
        // dimensions, not a factory drawing: X width, Z length, Y depth.
        check(Math.Abs(Spec.CellWidthM - 0.125) < 1e-12 &&
              Math.Abs(Spec.CellLengthM - 0.250) < 1e-12 &&
              Math.Abs(Spec.DepthM - 0.036) < 1e-12,
            "canonical standard modular cell is 125 x 250 x 36 mm");
        check(Math.Abs(Spec.CellWidthM / Spec.CellLengthM - 1.0 / 2.0) < 1e-12,
            "standard cell width:length is exactly 1:2");
        check(Math.Abs((Spec.Stage1TopM - Spec.Stage1BottomM) - 0.012) < 1e-12 &&
              Math.Abs((Spec.Stage2TopM - Spec.Stage2BottomM) - 0.012) < 1e-12,
            "both standard paid active slabs remain exactly 12 mm thick");
        check(Spec.Stage1TopM <= Spec.Stage2BottomM &&
              Spec.Stage2TopM <= Spec.Stage1CoverBottomM &&
              Spec.Stage1CoverTopM <= Spec.Stage2CoverBottomM,
            "coherent two-layer packing keeps inner Stage1 below outer Stage2");
        check(Spec.Stage1BottomM >= Spec.HousingBottomM &&
              Spec.Stage2CoverTopM <= Spec.DepthM + 1e-12,
            "every paid layer stays inside the 36 mm housing envelope");
        check(Math.Abs(2 * Spec.CoverGapM - 0.011) < 1e-12,
            "visible cover seam stays 11 mm");
        check(Math.Abs((Spec.Stage2CoverTopM - (Spec.DepthM - Spec.DividerRecessM)) - 0.002) < 1e-12,
            "standard divider trough stays 2 mm below the outer cover");

        // Exact whole-cell count contract: native scale is only an input signal.
        foreach (var (sx, sz, cu, cv) in new[]
        {
            (1.0, 1.0, 1, 1), (1.0, 2.0, 1, 2), (2.0, 1.0, 2, 1),
            (2.0, 2.0, 2, 2), (3.0, 4.0, 3, 4), (8.0, 8.0, 8, 8),
            (1.49, 1.49, 1, 1), (1.5, 1.5, 2, 2), (7.6, 7.6, 8, 8),
        })
        {
            check(ModularCellGeometry.TryLayout(sx, sz, Spec, 8, 8, out var layout)
                && layout.CountU == cu && layout.CountV == cv,
                $"scale {sx}x{sz} selects exactly {cu}x{cv} whole cells");
        }
        check(ModularCellGeometry.TryLayout(-2, -1, Spec, 8, 8, out var mirrored)
            && mirrored.CountU == 2 && mirrored.CountV == 1,
            "negative native scale is a mirror, not a different cell count");
        check(!ModularCellGeometry.TryLayout(0, 1, Spec, 8, 8, out _),
            "zero scale fails closed");
        check(!ModularCellGeometry.TryLayout(double.NaN, 1, Spec, 8, 8, out _),
            "non-finite scale fails closed");
        check(!ModularCellGeometry.TryLayout(9, 1, Spec, 8, 8, out _) ||
              ModularCellGeometry.TryLayout(9, 1, Spec, 8, 8, out var clamped) && clamped.CountU == 8,
            "above-maximum scale is rejected or clamped to the authorised cap");
        check(EraCellPartition.TryModular(2, 2, 0.125, 0.25, 8, 8, out var eraLayout, out _)
            && eraLayout.CountU == 2 && eraLayout.CountV == 2
            && Math.Abs(eraLayout.PitchUM - 0.125) < 1e-12
            && Math.Abs(eraLayout.PitchVM - 0.25) < 1e-12,
            "canonical EraCellLayout keeps distinct width and length pitches");

        // This is the key regression: geometry at each count is rebuilt, not a
        // single max-grid mesh stretched by the native transform.
        foreach (var (cu, cv) in new[] { (1, 1), (1, 2), (2, 1), (2, 2), (8, 8) })
        {
            var layout = new ModularCellLayout(cu, cv);
            var expectedWidth = cu * Spec.CellWidthM;
            var expectedLength = cv * Spec.CellLengthM;
            foreach (var kind in new[] { ModularPartKind.Stage1Collision, ModularPartKind.Stage1Visual,
                         ModularPartKind.Stage2Collision, ModularPartKind.Stage2Visual })
            {
                var mesh = ModularCellGeometry.Build(kind, layout, Spec);
                check(mesh.Triangles.Length % 3 == 0 && mesh.Triangles.Length > 0,
                    $"{kind} {cu}x{cv} has complete generated triangles");
                var bounds = Bounds(mesh);
                var visual = kind is ModularPartKind.Stage1Visual or ModularPartKind.Stage2Visual;
                check(Math.Abs(bounds.X.Min + expectedWidth / 2) < 1e-5 &&
                      Math.Abs(bounds.X.Max - expectedWidth / 2) < 1e-5 &&
                      Math.Abs(bounds.Z.Min + expectedLength / 2) < 1e-5 &&
                      Math.Abs(bounds.Z.Max - expectedLength / 2) < 1e-5,
                    $"{kind} {cu}x{cv} outer footprint snaps to {expectedWidth:0.00} x {expectedLength:0.00} m");
                check(visual || ModularCellGeometry.IsWatertightByPosition(mesh),
                    $"{kind} {cu}x{cv} physical collision is closed; visual sides use the shared housing");
                check(ModularCellGeometry.SignedVolume(mesh) > 0,
                    $"{kind} {cu}x{cv} winding is outward (positive volume)");
                if (!visual)
                {
                    var depth = PlacedPartDepth.Measure(mesh.Vertices, mesh.Triangles, Vector3.One);
                    check(Math.Abs(depth - 12.0) < 1e-5,
                        $"{kind} {cu}x{cv} production depth helper measures the 12 mm slab");
                    check(Math.Abs(ModularCellGeometry.SignedVolume(mesh) -
                                   ModularCellGeometry.ExpectedSlabVolume(layout, Spec)) < 1e-6,
                        $"{kind} {cu}x{cv} paid active volume equals {cu * cv} cells");
                }
            }

            var visualMesh = ModularCellGeometry.Build(ModularPartKind.Stage1Visual, layout, Spec);
            var outerVisual = ModularCellGeometry.Build(ModularPartKind.Stage2Visual, layout, Spec);
            var seam = 2 * Spec.CoverGapM;
            check(seam >= 0.010 - 1e-9 && seam <= 0.012 + 1e-9,
                $"{cu}x{cv} adjacent covers have a 10-12 mm real seam ({seam * 1000:0.##} mm)");
            check(seam > Spec.HousingWallM,
                $"{cu}x{cv} seam is wider than the 4 mm divider, so air is visible beside it");
            for (var x = 0; x < cu; x++)
                for (var y = 0; y < cv; y++)
                {
                    var cell = layout.Index(x, y);
                    var triangles = Enumerable.Range(0, visualMesh.TriangleCells.Length)
                        .Count(i => visualMesh.TriangleCells[i] == cell);
                    check(triangles == 16, $"{cu}x{cv} cell {x},{y} owns exactly its 4 slab + 12 cover triangles");
                    var b = ModularCellGeometry.CellBounds(layout, Spec, x, y);
                    for (var i = 0; i < visualMesh.Triangles.Length; i += 3)
                    {
                        if (visualMesh.TriangleCells[i / 3] != cell) continue;
                        for (var j = 0; j < 3; j++)
                        {
                            var v = visualMesh.Vertices[visualMesh.Triangles[i + j]];
                            check(v.X >= b.X0 - 1e-6 && v.X <= b.X1 + 1e-6 &&
                                  v.Z >= b.Z0 - 1e-6 && v.Z <= b.Z1 + 1e-6,
                                $"{cu}x{cv} cell {x},{y} geometry stays inside its own footprint");
                        }
                    }
                    foreach (var (mesh, top, label) in new[]
                             { (visualMesh, Spec.Stage1CoverTopM, "stage1"), (outerVisual, Spec.Stage2CoverTopM, "stage2") })
                    {
                        var cover = TopBounds(mesh, cell, top);
                        check(Math.Abs((cover.MaxX - cover.MinX) - (Spec.CellWidthM - seam)) < 1e-6 &&
                              Math.Abs((cover.MaxZ - cover.MinZ) - (Spec.CellLengthM - seam)) < 1e-6,
                            $"{cu}x{cv} cell {x},{y} {label} cover is inset {Spec.CoverGapM * 1000:0.##} mm per side");
                        check(Math.Abs(cover.MinX - (b.X0 + Spec.CoverGapM)) < 1e-6 &&
                              Math.Abs(cover.MaxX - (b.X1 - Spec.CoverGapM)) < 1e-6 &&
                              Math.Abs(cover.MinZ - (b.Z0 + Spec.CoverGapM)) < 1e-6 &&
                              Math.Abs(cover.MaxZ - (b.Z1 - Spec.CoverGapM)) < 1e-6,
                            $"{cu}x{cv} cell {x},{y} {label} cover stays inside its own damage cell");
                    }
                }

            var housing = ModularCellGeometry.Build(ModularPartKind.Housing, layout, Spec);
            var housingVisual = ModularCellGeometry.Build(ModularPartKind.HousingVisual, layout, Spec);
            var hb = Bounds(housing);
            var vb = Bounds(housingVisual);
            check(Math.Abs(hb.Y.Min) < 1e-9 && Math.Abs(hb.Y.Max - Spec.DepthM) < 1e-6,
                $"{cu}x{cv} housing external depth is exactly {Spec.DepthM * 1000:0.##} mm");
            check(Math.Abs(hb.X.Min - vb.X.Min) < 1e-7 && Math.Abs(hb.X.Max - vb.X.Max) < 1e-7 &&
                  Math.Abs(hb.Y.Min - vb.Y.Min) < 1e-7 && Math.Abs(hb.Y.Max - vb.Y.Max) < 1e-7 &&
                  Math.Abs(hb.Z.Min - vb.Z.Min) < 1e-7 && Math.Abs(hb.Z.Max - vb.Z.Max) < 1e-7,
                $"{cu}x{cv} HousingVisual keeps the unchanged outer housing envelope");
            if (cu > 1 || cv > 1)
            {
                var dividerTop = Spec.DepthM - Spec.DividerRecessM;
                var recess = Spec.Stage2CoverTopM - dividerTop;
                check(!housing.Vertices.Any(v => Math.Abs(v.Y - dividerTop) < 1e-6),
                    $"{cu}x{cv} physical Housing dividers still reach the original depth");
                check(housingVisual.Vertices.Any(v => Math.Abs(v.Y - dividerTop) < 1e-6),
                    $"{cu}x{cv} HousingVisual divider tops are actually recessed");
                check(Math.Abs(recess - Spec.DividerRecessM) < 1e-12 &&
                      dividerTop < Spec.Stage2CoverTopM,
                    $"{cu}x{cv} visual trough floor follows the spec ({recess * 1000:0.##} mm)");
                for (var x = 1; x < cu; x++)
                {
                    var left = TopBounds(outerVisual, layout.Index(x - 1, 0), Spec.Stage2CoverTopM);
                    var right = TopBounds(outerVisual, layout.Index(x, 0), Spec.Stage2CoverTopM);
                    check(Math.Abs((right.MinX - left.MaxX) - seam) < 1e-6,
                        $"{cu}x{cv} X seam {x} is an open 10-12 mm gap between whole cover tiles");
                }
                for (var y = 1; y < cv; y++)
                {
                    var near = TopBounds(outerVisual, layout.Index(0, y - 1), Spec.Stage2CoverTopM);
                    var far = TopBounds(outerVisual, layout.Index(0, y), Spec.Stage2CoverTopM);
                    check(Math.Abs((far.MinZ - near.MaxZ) - seam) < 1e-6,
                        $"{cu}x{cv} Z seam {y} is an open 10-12 mm gap between whole cover tiles");
                }
            }

            // Exact spent removal: one cell disappears, its neighbours stay.
            var spent = new HashSet<int> { layout.Index(cu - 1, cv - 1) };
            var cut = ModularCellGeometry.Build(ModularPartKind.Stage1Visual, layout, Spec, spent);
            check(cut.TriangleCells.All(cell => cell != layout.Index(cu - 1, cv - 1)),
                $"{cu}x{cv} exact spent cell has no render/cover triangles");
            check(cut.Triangles.Length / 3 == visualMesh.Triangles.Length / 3 - 16 || cu * cv == 1,
                $"{cu}x{cv} spent cut removes exactly one cell (full={visualMesh.Triangles.Length / 3}, cut={cut.Triangles.Length / 3})");
            if (cu * cv > 1)
                check(cut.TriangleCells.Count(cell => cell == layout.Index(0, 0)) == 16,
                    $"{cu}x{cv} neighbouring cell remains intact");

            // Native scale compensation: local vertices divided by scale must
            // land on the exact canonical footprint and keep 12 mm thickness.
            foreach (var scale in new[] { 1.0, 1.5, 2.0, 3.7, 8.0 })
            {
                var collision = ModularCellGeometry.Build(ModularPartKind.Stage1Collision, layout, Spec,
                    null, scale, scale);
                var scaled = BoundsScaled(collision, scale);
                check(Math.Abs(scaled.X.Min + expectedWidth / 2) < 1e-4 &&
                      Math.Abs(scaled.X.Max - expectedWidth / 2) < 1e-4 &&
                      Math.Abs(scaled.Z.Min + expectedLength / 2) < 1e-4 &&
                      Math.Abs(scaled.Z.Max - expectedLength / 2) < 1e-4,
                    $"{cu}x{cv} native scale {scale} compensates to the same world footprint");
                check(Math.Abs(PlacedPartDepth.Measure(collision.Vertices, collision.Triangles,
                        new Vector3((float)scale, 1, (float)scale)) - 12.0) < 1e-4,
                    $"{cu}x{cv} native scale {scale} keeps the 12 mm paid thickness");
            }
        }

        // Mirror/rotation mapping through the same canonical cell policy.
        var baseLayout = new ModularCellLayout(2, 2);
        var centre = CellCentre(baseLayout, 1, 0);
        foreach (var mirror in new[] { Vector3.One, new Vector3(-1, 1, 1), new Vector3(1, 1, -1) })
        {
            foreach (var angle in new[] { 0f, 45f, 90f, 180f })
            {
                var rotation = Matrix4x4.CreateRotationY(angle * MathF.PI / 180f);
                var world = Vector3.Transform(new Vector3((float)(centre.X * mirror.X), 0, (float)(centre.Z * mirror.Z)), rotation);
                // Undo the runtime transform exactly as PlacedEraCells does.
                var inverse = Matrix4x4.CreateRotationY(-angle * MathF.PI / 180f);
                var canonical = Vector3.Transform(world, inverse) * mirror;
                check(EraCellPartition.TryCell(ToEraLayout(baseLayout),
                        canonical.X, canonical.Z, out var cellX, out var cellY)
                    && cellX == 1 && cellY == 0,
                    $"mirror {mirror} rotation {angle} maps back to the same cell");
            }
        }

        // Focused standard-cell layering: each paid collision mesh occupies its
        // own documented Y band, and the frame matches the actual shell volume.
        foreach (var (kind, label, bottom, top) in new[]
                 {
                     (ModularPartKind.Stage1Collision, "inner Stage1", Spec.Stage1BottomM, Spec.Stage1TopM),
                     (ModularPartKind.Stage2Collision, "outer Stage2", Spec.Stage2BottomM, Spec.Stage2TopM),
                 })
        {
            var mesh = ModularCellGeometry.Build(kind, new ModularCellLayout(1, 1), Spec);
            var band = Bounds(mesh);
            check(Math.Abs(band.Y.Min - bottom) < 1e-6 && Math.Abs(band.Y.Max - top) < 1e-6,
                $"{label} slab stays on its repacked Y band {bottom * 1000:0.#}-{top * 1000:0.#} mm");
            check(Math.Abs(PlacedPartDepth.Measure(mesh.Vertices, mesh.Triangles, Vector3.One) - 12.0) < 1e-5,
                $"{label} production depth helper still measures 12 mm");
        }
        var oneCell = new ModularCellLayout(1, 1);
        check(Math.Abs(ModularCellGeometry.ExpectedSlabVolume(oneCell, Spec) - 0.000375) < 1e-12,
            "one standard paid cell slab volume is 0.125 x 0.250 x 0.012 m");
        var frameVolume = ModularCellGeometry.SignedVolume(
            ModularCellGeometry.Build(ModularPartKind.Housing, oneCell, Spec));
        check(Math.Abs(frameVolume - 0.000218952) < 1e-9,
            $"one 1x1 standard frame physical shell volume follows the 36 mm housing ({frameVolume:0.##########})");

        // Mass/cost use paid whole-cell area, never the continuous scale and
        // never depth. One base cell is the paid unit.
        foreach (var (cu, cv) in new[] { (1, 1), (1, 2), (2, 1), (2, 2), (8, 8) })
            check(cu * cv == ModularMassFactor(cu, cv),
                $"{cu}x{cv} mass/cost factor is the exact paid cell count");
        check(Math.Abs(ModularCellGeometry.BaseStageMass(Spec) - 1.6125) < 1e-6,
            "one standard paid active cell weighs 1.6125 kg at 4300 kg/m3");
        var housingMass = ModularCellGeometry.BaseHousingMass(Spec);
        check(Math.Abs(housingMass - 1.7187732) < 1e-4,
            $"one standard 1x1 frame is counted once with a 4 mm shell volume (mass={housingMass:0.######})");

        // P1: only a real generated-collision identity change requests a native
        // damage-collider rebuild; an unchanged scale/count must be a no-op.
        var baseIdentity = ModularGeometryIdentity.Create(1, 1, 1f, 1f);
        check(!ModularColliderPolicy.NeedsNativeColliderRebuild(baseIdentity,
                ModularGeometryIdentity.Create(1, 1, 1f, 1f)),
            "unchanged geometry identity does not request a native collider rebuild");
        check(ModularColliderPolicy.NeedsNativeColliderRebuild(baseIdentity,
                ModularGeometryIdentity.Create(1, 2, 1f, 1f)),
            "whole-cell count change requests a native collider rebuild");
        check(ModularColliderPolicy.NeedsNativeColliderRebuild(baseIdentity,
                ModularGeometryIdentity.Create(1, 1, 1.7f, 1f)),
            "changed scale compensation (same count) requests a native collider rebuild");
        check(ModularColliderPolicy.NeedsNativeColliderRebuild(null, baseIdentity),
            "first geometry identity requests a native collider rebuild");
        check(!ModularColliderPolicy.NeedsNativeColliderRebuild(baseIdentity,
                new ModularGeometryIdentity(1, 1, float.NaN, 1f, 1)),
            "invalid geometry identity never requests a rebuild");

        // P2: ownership/refcount ledger for generated meshes. Current keys keep
        // a user; retired keys stay referenced until the native build boundary.
        var ledger = new ModularMeshOwnership();
        var ownerA = new IntPtr(11);
        ledger.Commit(ownerA, new[] { "v1", "c1" }, 4);
        check(ledger.Users("v1") == 1 && ledger.Users("c1") == 1 && ledger.RetiredCount(ownerA) == 0,
            "owner commit takes one reference per generated mesh key");
        ledger.Commit(ownerA, new[] { "v1", "c1" }, 4);
        check(ledger.Users("v1") == 1 && ledger.Users("c1") == 1,
            "re-applying the same layout does not double-count shared mesh users");
        ledger.Commit(ownerA, new[] { "v2", "c1" }, 4);
        check(ledger.Users("v1") == 1 && ledger.Users("c1") == 1 && ledger.RetiredCount(ownerA) == 1,
            "replaced visual key remains referenced until its safe release while the current collision key stays owned");
        ledger.ReleaseRetired(ownerA, key => key.StartsWith("v", StringComparison.Ordinal));
        check(ledger.Users("v1") == 0 && ledger.RetiredCount(ownerA) == 0,
            "safe visual retirement releases only the replaced visual reference");
        ledger.Commit(ownerA, new[] { "v3", "c2" }, 4);
        check(ledger.Users("c1") == 1 && ledger.Users("v2") == 1 && ledger.RetiredCount(ownerA) == 2 &&
              ledger.IsCurrent(ownerA, "c2"),
            "replaced visual/collision keys remain referenced until the native build boundary");
        ledger.ReleaseRetired(ownerA);
        check(ledger.Users("c1") == 0 && ledger.RetiredCount(ownerA) == 0,
            "native collider build boundary releases the retired collision generation");
        var ownerB = new IntPtr(12);
        ledger.Commit(ownerB, new[] { "v3" }, 4);
        check(ledger.Users("v3") == 2, "a second live owner shares the same generated mesh");
        ledger.ReleaseOwner(ownerA);
        check(ledger.Users("v3") == 1, "releasing one owner preserves the shared mesh for the other");
        ledger.ReleaseOwner(ownerB);
        check(ledger.Users("v3") == 0, "releasing the final owner drops the shared reference");
        var ownerC = new IntPtr(13);
        for (var i = 0; i < 12; i++) ledger.Commit(ownerC, new[] { "c" + i }, 4);
        check(ledger.RetiredCount(ownerC) == 11 && ledger.Users("c0") == 1,
            "retirement threshold never frees a mesh before native replacement is confirmed");
        ledger.ReleaseRetired(ownerC);
        check(ledger.RetiredCount(ownerC)==0&&ledger.Users("c0")==0,
            "confirmed native replacement releases every retained generation");
        check(ledger.Users("c11") == 1, "current generation remains referenced after retirement cap");
        ledger.Clear();
        check(ledger.Users("c11") == 0, "teardown clears every ownership reference exactly once");

        // Persistence contract: the part JSON's saved scale array reconstructs
        // the same count and geometry on load.
        var partPath = Path.Combine(AppContext.BaseDirectory, "DupletAssets", "dupletHullModularV1Part.json");
        using (var document = JsonDocument.Parse(File.ReadAllText(partPath)))
        {
            var root = document.RootElement;
            check(root.GetProperty("guid").GetString() == "b3de0597-85c1-5720-bf38-463d6900058c",
                "new modular part has a distinct stable GUID");
            var components = root.GetProperty("components").EnumerateArray().ToArray();
            check(components.Select(c => c.GetProperty("fileID").GetString()).SequenceEqual(
                    new[] { "dupletModularHousing", "dupletModularStage1Cell", "dupletModularStage2Cell" }),
                "new modular part uses new component identities");
            check(components.All(c => c.GetProperty("info").GetProperty("scaling").GetBoolean()),
                "all modular components permit native saved scaling");
            check(Math.Abs(components[1].GetProperty("info").GetProperty("mass").GetDouble() -
                           ModularCellGeometry.BaseStageMass(Spec)) < 1e-6 &&
                  Math.Abs(components[2].GetProperty("info").GetProperty("mass").GetDouble() -
                           ModularCellGeometry.BaseStageMass(Spec)) < 1e-6,
                "each paid depth layer JSON mass equals the new 12 mm active area * density");
            check(Math.Abs(components[0].GetProperty("info").GetProperty("mass").GetDouble() -
                           ModularCellGeometry.BaseHousingMass(Spec)) < 1e-4,
                "housing JSON mass equals the actual new physical frame shell volume * density");
            var transform = root.GetProperty("transform");
            check(transform.GetProperty("minScale").EnumerateArray().Select(x => x.GetDouble())
                    .SequenceEqual(new[] { 1d, 1d, 1d }),
                "modular part starts at exactly 1x1");
            check(transform.GetProperty("maxScale").EnumerateArray().Select(x => x.GetDouble())
                    .SequenceEqual(new[] { 8d, 1d, 8d }),
                "modular part caps at 8x8 and locks Y depth");
            foreach (var (sx, sz, cu, cv) in new[] { (1d, 1d, 1, 1), (1d, 2d, 1, 2), (2d, 1d, 2, 1), (2d, 2d, 2, 2), (8d, 8d, 8, 8) })
                check(ModularCellGeometry.TryLayout(sx, sz, Spec, 8, 8, out var saved)
                    && saved.CountU == cu && saved.CountV == cv,
                    $"saved scale {sx}x{sz} reconstructs {cu}x{cv} geometry after load");
        }

        // Production preset metadata must agree with the canonical spec and the
        // native part JSON; neither may keep the old 300 mm/old mass values.
        var modularPresets = SprocketExpandedBallistics.Materials.EraPresetRegistry.Presets
            .Where(p => p.Cassette is "dupletModularStage1Cell" or "dupletModularStage2Cell").ToArray();
        check(modularPresets.Length == 2, "both modular depth presets are present");
        foreach (var preset in modularPresets)
        {
            check(preset.WholeCells && preset.MaxCountU == 8 && preset.MaxCountV == 8 &&
                  preset.ModularVariant == "standard" &&
                  Math.Abs(preset.CellWidthM - Spec.CellWidthM) < 1e-12 &&
                  Math.Abs(preset.CellLengthM - Spec.CellLengthM) < 1e-12,
                $"{preset.Cassette} preset pitches/count caps match the 125 x 250 mm spec");
            check(Math.Abs(preset.CellMinU + 0.0625) < 1e-12 && Math.Abs(preset.CellMaxU - 0.0625) < 1e-12 &&
                  Math.Abs(preset.CellMinV + 0.125) < 1e-12 && Math.Abs(preset.CellMaxV - 0.125) < 1e-12,
                $"{preset.Cassette} preset base bounds match the new 125 x 250 mm footprint");
            check(Math.Abs(preset.CassetteMass - ModularCellGeometry.BaseStageMass(Spec)) < 1e-5 &&
                  Math.Abs(preset.MountMass - ModularCellGeometry.BaseHousingMass(Spec)) < 1e-4,
                $"{preset.Cassette} preset paid/frame masses follow the new geometry");
        }

        var bindings = EraPartBindings.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "physical-era-bindings.json")));
        var hasStage1 = bindings.TryGetCassette("dupletModularStage1Cell", out var stage1);
        var hasStage2 = bindings.TryGetCassette("dupletModularStage2Cell", out var stage2);
        var hasHousing = bindings.TryGetMount("dupletModularHousing", out _);
        check(hasStage1 && hasStage2 && hasHousing,
            "both modular depth layers and the shared housing are bound");
        check(stage1!.MaterialId == "dupletHullActive" && stage2!.MaterialId == "dupletHullActive" &&
              stage1.ResponseId == "dupletHullActive" && stage2.ResponseId == "dupletHullActive",
            "modular layers reuse the existing Duplet material/response rules");
        check(stage1!.CassetteComponentId != stage2!.CassetteComponentId,
            "the two paid depth layers have separate exact identities");

        // Regression: native mirroring reports a NEGATIVE signed model scale.
        // PlacedEraCells.TryLayout feeds model.GetModelScale().x/z straight into
        // EraCellPartition.TryModular, so this production count path must treat
        // -magnitude exactly like +magnitude instead of rejecting it before the
        // absolute value. ModularCellGeometry.TryLayout already normalises the
        // sign, which is why the earlier geometry checks stayed green.
        var mirrorSample = new[] { -4.563764, 1.0, 2.61656046 };
        var mirrorCounts = new[] { (5, 3) };
        check(EraCellPartition.CountForScale(mirrorSample[0], 8) == 5 &&
              EraCellPartition.CountForScale(-mirrorSample[2], 8) == 3,
            "signed negative production scale counts by magnitude (5x3 sample)");
        check(EraCellPartition.CountForScale(4.563764, 8) == EraCellPartition.CountForScale(-4.563764, 8) &&
              EraCellPartition.CountForScale(2.61656046, 8) == EraCellPartition.CountForScale(-2.61656046, 8),
            "CountForScale is sign-independent for finite nonzero scale");

        foreach (var (sx, sz) in new[]
                 {
                     (4.563764, 2.61656046), (-4.563764, 2.61656046),
                     (4.563764, -2.61656046), (-4.563764, -2.61656046),
                 })
        {
            check(EraCellPartition.TryModular(sx, sz, 0.125, 0.25, 8, 8,
                    out var signedLayout, out var signedError),
                $"production TryModular accepts signed scale {sx}x{sz} (error='{signedError}')");
            check(signedLayout.CountU == mirrorCounts[0].Item1 &&
                  signedLayout.CountV == mirrorCounts[0].Item2 &&
                  signedLayout.CellCount == mirrorCounts[0].Item1 * mirrorCounts[0].Item2,
                $"signed scale {sx}x{sz} yields the same 5x3 production cell count/mass zones");
            check(Math.Abs(signedLayout.MinU + 0.3125) < 1e-12 &&
                  Math.Abs(signedLayout.MinV + 0.375) < 1e-12 &&
                  Math.Abs(signedLayout.PitchUM - 0.125) < 1e-12 &&
                  Math.Abs(signedLayout.PitchVM - 0.25) < 1e-12,
                $"signed scale {sx}x{sz} keeps the same canonical 5x3 layout bounds/pitches");
        }

        foreach (var (negative, expectedU, expectedV) in new[]
                 {
                     (-1.0, 1, 1), (-1.49, 1, 1), (-1.5, 2, 2),
                     (-7.6, 8, 8), (-8.0, 8, 8), (-9.0, 8, 8),
                 })
            check(EraCellPartition.TryModular(negative, negative, 0.125, 0.25, 8, 8,
                      out var fractional, out _) &&
                  fractional.CountU == expectedU && fractional.CountV == expectedV,
                $"negative scale {negative} counts {expectedU}x{expectedV} with the same thresholds");

        check(Math.Abs(EraCellPartition.MassScale(-4.563764, 2.61656046, 4096) -
                       EraCellPartition.MassScale(4.563764, 2.61656046, 4096)) < 1e-12 &&
              Math.Abs(EraCellPartition.AreaScale(-4.563764, -2.61656046) -
                       EraCellPartition.AreaScale(4.563764, 2.61656046)) < 1e-12,
            "mirrored signed scale charges the same paid mass/area");
        check(!EraCellPartition.TryModular(0, 1, 0.125, 0.25, 8, 8, out _, out _) &&
              !EraCellPartition.TryModular(-1, 0, 0.125, 0.25, 8, 8, out _, out _) &&
              !EraCellPartition.TryModular(double.NaN, -1, 0.125, 0.25, 8, 8, out _, out _) &&
              !EraCellPartition.TryModular(-1, double.PositiveInfinity, 0.125, 0.25, 8, 8, out _, out _) &&
              !EraCellPartition.TryModular(-1, -1, 0.125, 0.25, 0, 8, out _, out _) &&
              !EraCellPartition.TryModular(-1, -1, 0.125, 0.25, 8, 65, out _, out _),
            "zero, non-finite and out-of-bound signed scales still fail closed");
        check(EraCellPartition.CountForScale(0, 8) == 0 &&
              EraCellPartition.CountForScale(double.NaN, 8) == 0 &&
              EraCellPartition.CountForScale(double.PositiveInfinity, 8) == 0 &&
              EraCellPartition.CountForScale(-1, 0) == 0 &&
              EraCellPartition.CountForScale(-1, 65) == 0 &&
              EraCellPartition.DepthLocked(-1) && !EraCellPartition.DepthLocked(-1.5),
            "signed fix does not weaken zero/finite/maximum/depth guards");

        ExportPreviews(check);
    }

    private static double ModularMassFactor(int cu, int cv) => cu * cv;

    private static EraCellLayout ToEraLayout(ModularCellLayout layout)
        => new(-layout.CountU * Spec.CellWidthM / 2, -layout.CountV * Spec.CellLengthM / 2,
            Spec.CellWidthM, layout.CountU, layout.CountV, EraCellPartition.BorderToleranceM, Spec.CellLengthM);

    private static (double X, double Z) CellCentre(ModularCellLayout layout, int x, int y)
    {
        var b = ModularCellGeometry.CellBounds(layout, Spec, x, y);
        return ((b.X0 + b.X1) / 2, (b.Z0 + b.Z1) / 2);
    }

    private static (double Min, double Max) X(ModularMeshData mesh) => (mesh.Vertices.Min(v => v.X), mesh.Vertices.Max(v => v.X));
    private static (double Min, double Max) Y(ModularMeshData mesh) => (mesh.Vertices.Min(v => v.Y), mesh.Vertices.Max(v => v.Y));
    private static (double Min, double Max) Z(ModularMeshData mesh) => (mesh.Vertices.Min(v => v.Z), mesh.Vertices.Max(v => v.Z));
    private static ((double Min, double Max) X, (double Min, double Max) Y, (double Min, double Max) Z) Bounds(ModularMeshData mesh)
        => (X(mesh), Y(mesh), Z(mesh));
    private static ((double Min, double Max) X, (double Min, double Max) Y, (double Min, double Max) Z) BoundsScaled(ModularMeshData mesh, double scale)
        => ((mesh.Vertices.Min(v => v.X) * scale, mesh.Vertices.Max(v => v.X) * scale),
            (mesh.Vertices.Min(v => v.Y), mesh.Vertices.Max(v => v.Y)),
            (mesh.Vertices.Min(v => v.Z) * scale, mesh.Vertices.Max(v => v.Z) * scale));

    private static (double MinX, double MaxX, double MinZ, double MaxZ) TopBounds(ModularMeshData mesh, int cell, double top)
    {
        var minX = double.PositiveInfinity; var maxX = double.NegativeInfinity;
        var minZ = double.PositiveInfinity; var maxZ = double.NegativeInfinity;
        for (var i = 0; i < mesh.Triangles.Length; i += 3)
        {
            if (mesh.TriangleCells[i / 3] != cell) continue;
            for (var j = 0; j < 3; j++)
            {
                var v = mesh.Vertices[mesh.Triangles[i + j]];
                if (Math.Abs(v.Y - top) > 1e-6) continue;
                minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
                minZ = Math.Min(minZ, v.Z); maxZ = Math.Max(maxZ, v.Z);
            }
        }
        return (minX, maxX, minZ, maxZ);
    }

    internal static void RunSlim(Action<bool, string> check)
    {
        var spec = ModularCellSpec.DupletSlim;
        check(Math.Abs(spec.CellWidthM - 0.125) < 1e-12 &&
              Math.Abs(spec.CellLengthM - 0.250) < 1e-12 &&
              Math.Abs(spec.DepthM - 0.026) < 1e-12,
            "slim modular cell is 125 x 250 x 26 mm");
        check(Math.Abs(spec.CellWidthM / spec.CellLengthM - 0.5) < 1e-12,
            "slim cell width:length is exactly 1:2");
        check(Math.Abs((spec.Stage1TopM - spec.Stage1BottomM) - 0.010) < 1e-12 &&
              Math.Abs((spec.Stage2TopM - spec.Stage2BottomM) - 0.010) < 1e-12,
            "both slim paid active slabs are exactly 10 mm thick");
        check(spec.Stage1TopM <= spec.Stage2BottomM &&
              spec.Stage2TopM <= spec.Stage1CoverBottomM &&
              spec.Stage1CoverTopM <= spec.Stage2CoverBottomM &&
              spec.Stage2CoverTopM <= spec.DepthM + 1e-12,
            "slim two-layer packing is coherent and inside the 26 mm envelope");
        check(Math.Abs(2 * spec.CoverGapM - 0.011) < 1e-12,
            "slim visible cover seam stays 11 mm");
        check(Math.Abs((spec.Stage2CoverTopM - (spec.DepthM - spec.DividerRecessM)) - 0.001) < 1e-12,
            "slim divider trough stays 1 mm below the outer cover");

        foreach (var (cu, cv) in new[] { (1, 1), (1, 2), (2, 1), (2, 2), (8, 8) })
        {
            var layout = new ModularCellLayout(cu, cv);
            var expectedWidth = cu * spec.CellWidthM;
            var expectedLength = cv * spec.CellLengthM;
            foreach (var kind in new[] { ModularPartKind.Stage1Collision, ModularPartKind.Stage2Collision })
            {
                var mesh = ModularCellGeometry.Build(kind, layout, spec);
                var bounds = Bounds(mesh);
                check(Math.Abs(bounds.X.Min + expectedWidth / 2) < 1e-5 &&
                      Math.Abs(bounds.X.Max - expectedWidth / 2) < 1e-5 &&
                      Math.Abs(bounds.Z.Min + expectedLength / 2) < 1e-5 &&
                      Math.Abs(bounds.Z.Max - expectedLength / 2) < 1e-5,
                    $"{kind} {cu}x{cv} slim footprint snaps to {expectedWidth:0.00} x {expectedLength:0.00} m");
                check(ModularCellGeometry.IsWatertightByPosition(mesh),
                    $"{kind} {cu}x{cv} slim mesh is closed");
                check(Math.Abs(PlacedPartDepth.Measure(mesh.Vertices, mesh.Triangles, Vector3.One) - 10.0) < 1e-5,
                    $"{kind} {cu}x{cv} slim production depth helper measures 10 mm");
                check(Math.Abs(ModularCellGeometry.SignedVolume(mesh) -
                               ModularCellGeometry.ExpectedSlabVolume(layout, spec)) < 1e-6,
                    $"{kind} {cu}x{cv} slim paid active volume equals {cu * cv} cells");
            }
            var visual = ModularCellGeometry.Build(ModularPartKind.Stage1Visual, layout, spec);
            var outer = ModularCellGeometry.Build(ModularPartKind.Stage2Visual, layout, spec);
            var seam = 2 * spec.CoverGapM;
            check(seam >= 0.010 - 1e-9 && seam <= 0.012 + 1e-9,
                $"{cu}x{cv} slim adjacent covers keep the 10-12 mm seam ({seam * 1000:0.##} mm)");
            for (var x = 0; x < cu; x++)
                for (var y = 0; y < cv; y++)
                {
                    var cell = layout.Index(x, y);
                    check(Enumerable.Range(0, visual.TriangleCells.Length)
                            .Count(i => visual.TriangleCells[i] == cell) == 16,
                        $"{cu}x{cv} slim cell {x},{y} owns exactly its 4 slab + 12 cover triangles");
                    var b = ModularCellGeometry.CellBounds(layout, spec, x, y);
                    foreach (var (mesh, top, label) in new[]
                             { (visual, spec.Stage1CoverTopM, "stage1"), (outer, spec.Stage2CoverTopM, "stage2") })
                    {
                        var cover = TopBounds(mesh, cell, top);
                        check(Math.Abs((cover.MaxX - cover.MinX) - (spec.CellWidthM - seam)) < 1e-6 &&
                              Math.Abs((cover.MaxZ - cover.MinZ) - (spec.CellLengthM - seam)) < 1e-6 &&
                              Math.Abs(cover.MinX - (b.X0 + spec.CoverGapM)) < 1e-6 &&
                              Math.Abs(cover.MaxX - (b.X1 - spec.CoverGapM)) < 1e-6 &&
                              Math.Abs(cover.MinZ - (b.Z0 + spec.CoverGapM)) < 1e-6 &&
                              Math.Abs(cover.MaxZ - (b.Z1 - spec.CoverGapM)) < 1e-6,
                            $"{cu}x{cv} slim cell {x},{y} {label} cover stays inside its own cell");
                    }
                }
            var housing = ModularCellGeometry.Build(ModularPartKind.Housing, layout, spec);
            var hb = Bounds(housing);
            check(Math.Abs(hb.Y.Min) < 1e-9 && Math.Abs(hb.Y.Max - spec.DepthM) < 1e-6,
                $"{cu}x{cv} slim housing external depth is exactly {spec.DepthM * 1000:0.##} mm");
        }

        var oneCell = new ModularCellLayout(1, 1);
        check(Math.Abs(ModularCellGeometry.ExpectedSlabVolume(oneCell, spec) - 0.0003125) < 1e-12,
            "one slim paid cell slab volume is 0.125 x 0.250 x 0.010 m");
        var frameVolume = ModularCellGeometry.SignedVolume(
            ModularCellGeometry.Build(ModularPartKind.Housing, oneCell, spec));
        check(Math.Abs(frameVolume - 0.000098116) < 1e-9,
            $"one 1x1 slim frame physical shell volume follows the 26 mm housing ({frameVolume:0.##########})");
        check(Math.Abs(ModularCellGeometry.BaseStageMass(spec) - 1.34375) < 1e-6,
            "one slim paid active cell weighs 1.34375 kg at 4300 kg/m3");
        check(Math.Abs(ModularCellGeometry.BaseHousingMass(spec) - 0.7702106) < 1e-4,
            "one slim 1x1 frame weighs 0.7702106 kg with the 2 mm shell");

        var slimPartPath = Path.Combine(AppContext.BaseDirectory, "DupletAssets", "dupletHullModularSlimV1Part.json");
        using (var document = JsonDocument.Parse(File.ReadAllText(slimPartPath)))
        {
            var root = document.RootElement;
            check(root.GetProperty("guid").GetString() == "1873e6e7-451d-53a7-ad0b-1c496d0c1205",
                "slim modular part has its own stable GUID");
            var components = root.GetProperty("components").EnumerateArray().ToArray();
            check(components.Select(c => c.GetProperty("fileID").GetString()).SequenceEqual(
                    new[] { "dupletModularSlimHousing", "dupletModularSlimStage1Cell", "dupletModularSlimStage2Cell" }),
                "slim modular part uses its own component identities");
            check(components.All(c => c.GetProperty("info").GetProperty("scaling").GetBoolean()),
                "all slim modular components permit native saved scaling");
            check(Math.Abs(components[1].GetProperty("info").GetProperty("mass").GetDouble() -
                           ModularCellGeometry.BaseStageMass(spec)) < 1e-6 &&
                  Math.Abs(components[2].GetProperty("info").GetProperty("mass").GetDouble() -
                           ModularCellGeometry.BaseStageMass(spec)) < 1e-6,
                "each slim paid layer JSON mass equals the 10 mm active area * density");
            check(Math.Abs(components[0].GetProperty("info").GetProperty("mass").GetDouble() -
                           ModularCellGeometry.BaseHousingMass(spec)) < 1e-4,
                "slim housing JSON mass equals the actual 26 mm frame shell volume * density");
            var transform = root.GetProperty("transform");
            check(transform.GetProperty("maxScale").EnumerateArray().Select(x => x.GetDouble())
                    .SequenceEqual(new[] { 8d, 1d, 8d }),
                "slim modular part caps at 8x8 and locks Y depth");
        }

        var slimPresets = SprocketExpandedBallistics.Materials.EraPresetRegistry.Presets
            .Where(p => p.Cassette is "dupletModularSlimStage1Cell" or "dupletModularSlimStage2Cell").ToArray();
        check(slimPresets.Length == 2, "both slim modular depth presets are present");
        foreach (var preset in slimPresets)
        {
            check(preset.WholeCells && preset.MaxCountU == 8 && preset.MaxCountV == 8 &&
                  preset.ModularVariant == "slim" &&
                  Math.Abs(preset.CellWidthM - spec.CellWidthM) < 1e-12 &&
                  Math.Abs(preset.CellLengthM - spec.CellLengthM) < 1e-12,
                $"{preset.Cassette} preset pitches/count caps match the 125 x 250 mm slim spec");
            check(Math.Abs(preset.CellMinU + 0.0625) < 1e-12 && Math.Abs(preset.CellMaxU - 0.0625) < 1e-12 &&
                  Math.Abs(preset.CellMinV + 0.125) < 1e-12 && Math.Abs(preset.CellMaxV - 0.125) < 1e-12,
                $"{preset.Cassette} preset base bounds match the 125 x 250 mm slim footprint");
            check(Math.Abs(preset.CassetteMass - ModularCellGeometry.BaseStageMass(spec)) < 1e-5 &&
                  Math.Abs(preset.MountMass - ModularCellGeometry.BaseHousingMass(spec)) < 1e-4,
                $"{preset.Cassette} preset paid/frame masses follow the slim geometry");
        }

        var slimBindings = EraPartBindings.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "physical-era-bindings.json")));
        check(slimBindings.TryGetCassette("dupletModularSlimStage1Cell", out var slimStage1),
            "slim inner depth layer is bound");
        check(slimBindings.TryGetCassette("dupletModularSlimStage2Cell", out var slimStage2),
            "slim outer depth layer is bound");
        check(slimBindings.TryGetMount("dupletModularSlimHousing", out _),
            "slim housing is bound");
        check(slimStage1!.MaterialId == "dupletHullActive" && slimStage2!.MaterialId == "dupletHullActive" &&
              slimStage1.ResponseId == "dupletHullActive" && slimStage2.ResponseId == "dupletHullActive",
            "slim modular layers reuse the existing Duplet material/response rules");
    }

    private static void ExportPreviews(Action<bool, string> check)
    {
        var directory = Environment.GetEnvironmentVariable("MODULAR_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var metrics = new List<object>();
        foreach (var (spec, prefix, variant) in new[]
                 {
                     (ModularCellSpec.DupletV1, "duplet-modular", "standard"),
                     (ModularCellSpec.DupletSlim, "duplet-modular-slim", "slim"),
                 })
        {
            foreach (var (cu, cv) in new[] { (1, 1), (1, 2), (2, 1), (2, 2), (8, 8) })
            {
                var layout = new ModularCellLayout(cu, cv);
                var parts = new[]
                {
                    ("housing", ModularCellGeometry.Build(ModularPartKind.HousingVisual, layout, spec)),
                    ("stage1", ModularCellGeometry.Build(ModularPartKind.Stage1Visual, layout, spec)),
                    ("stage2", ModularCellGeometry.Build(ModularPartKind.Stage2Visual, layout, spec,mountingDetail:true)),
                };
                var path = Path.Combine(directory, $"{prefix}-{cu}x{cv}.obj");
                using (var writer = new StreamWriter(path))
                {
                    writer.WriteLine("# Generated by the production ModularCellGeometry builder.");
                    writer.WriteLine($"# {variant} layout {cu}x{cv}; canonical metres; X width, Y depth, Z length");
                    var offset = 1;
                    foreach (var (name, mesh) in parts)
                    {
                        writer.WriteLine("o " + name);
                        for (var i = 0; i < mesh.Vertices.Length; i++)
                        {
                            var v = mesh.Vertices[i];
                            writer.WriteLine(FormattableString.Invariant($"v {v.X:0.########} {v.Y:0.########} {v.Z:0.########}"));
                        }
                        for (var i = 0; i < mesh.Triangles.Length; i += 3)
                        {
                            var cell = mesh.TriangleCells[i / 3];
                            var group = cell < 0 ? name + "_frame" : $"{name}_cell_{cell % cu}_{cell / cu}";
                            writer.WriteLine("g " + group);
                            writer.WriteLine(FormattableString.Invariant($"f {mesh.Triangles[i] + offset} {mesh.Triangles[i + 1] + offset} {mesh.Triangles[i + 2] + offset}"));
                        }
                        offset += mesh.Vertices.Length;
                    }
                }
                var collision = ModularCellGeometry.Build(ModularPartKind.Stage1Collision, layout, spec);
                var visual = ModularCellGeometry.Build(ModularPartKind.Stage1Visual, layout, spec);
                metrics.Add(new
                {
                    variant,
                    layout = $"{cu}x{cv}",
                    cells = cu * cv,
                    widthM = cu * spec.CellWidthM,
                    lengthM = cv * spec.CellLengthM,
                    depthM = spec.DepthM,
                    triangles = visual.Triangles.Length / 3,
                    cellTriangles = visual.TriangleCells.Count(c => c >= 0),
                    collisionTriangles = collision.Triangles.Length / 3,
                    collisionDepthMm = PlacedPartDepth.Measure(collision.Vertices, collision.Triangles, Vector3.One),
                    collisionVolumeM3 = ModularCellGeometry.SignedVolume(collision),
                    watertight = ModularCellGeometry.IsWatertightByPosition(collision),
                    obj = Path.GetFileName(path),
                });
            }
        }
        File.WriteAllText(Path.Combine(directory, "generated-metrics.json"),
            JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        check(File.Exists(Path.Combine(directory, "duplet-modular-1x1.obj")) &&
              File.Exists(Path.Combine(directory, "duplet-modular-8x8.obj")) &&
              File.Exists(Path.Combine(directory, "duplet-modular-slim-1x1.obj")) &&
              File.Exists(Path.Combine(directory, "duplet-modular-slim-8x8.obj")),
            "production geometry previews were exported for both standard and slim layouts");
    }
}
