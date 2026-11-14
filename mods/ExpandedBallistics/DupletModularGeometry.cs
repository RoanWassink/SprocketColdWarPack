using System.Numerics;

namespace SprocketExpandedBallistics;

// Procedural geometry for the modular Nizh panel and the three modular Duplet
// parts (turret panel, hull panel, side module), v0.20.0.
//
// Layer stack along the mounting normal (Y = 0 at the pan, Y = DepthM at the lid):
//   pan | active layer (Stage1) | intermediate steel plate | active layer (Stage2) | front steel plate
// The Nizh panel has one active layer: pan | active layer (Stage1) | front steel plate.
// Stage2 is the layer struck FIRST (outer, nearest the front plate), Stage1 the rear layer,
// following the existing StageIndex convention of the modular Duplet.
//
// Physical (collision / mass) geometry:
//   Housing        pan, outer walls, dividers (layer zones only), intermediate plate, front plate.
//                  Closed shells that never overlap, so SignedVolume x 7850 is the paid steel mass.
//   Stage1/2Collision  closed flat slabs from the shared ModularCellGeometry slab builder, so the
//                  measured thickness (shortest edge) is exactly the layer thickness. Grooves are
//                  render only and never enter a collision mesh.
// Render only (never in a collision or mass mesh): lids, bolts, rims, ridges, lip, hinge.
//
// TriangleCells: -1 = shared frame, otherwise the owning cell (x + y*CountU).
internal static class DupletModularGeometry
{
    internal const int StyleNizh = 1, StyleTurret = 2, StyleHull = 3, StyleSide = 4;
    internal const int MaxTriangles = 8000, MaxVertices = 8000;

    // Detail tiers: the first tier that fits the 8000 triangle / 8000 vertex budget is used,
    // so the result is deterministic for a layout and spent set.
    private readonly record struct Detail(bool FullLid, bool Extras, int Bolts, int RimStrips, int Ridges);
    private static readonly Detail[] Tiers =
    {
        new(true, true, 2, 4, 10),
        new(true, false, 1, 4, 6),
        new(false, false, 1, 2, 4),
        new(false, false, 0, 2, 2),
    };

    private const double BoltHeadRadiusM = .005, WasherRadiusM = .008, BoltInsetM = .010, SheetM = .0015;

    internal static bool IsPanel(ModularCellSpec spec) => spec.DupletStyle is >= StyleNizh and <= StyleSide;
    internal static bool SingleLayer(ModularCellSpec spec) => spec.DupletStyle == StyleNizh;

    // Active layer zones (bottom, top) in struck-last to struck-first order: [Stage1] or [Stage1, Stage2].
    internal static (double Bottom, double Top)[] LayerZones(ModularCellSpec spec)
        => SingleLayer(spec)
            ? new[] { (spec.Stage1BottomM, spec.Stage1TopM) }
            : new[] { (spec.Stage1BottomM, spec.Stage1TopM), (spec.Stage2BottomM, spec.Stage2TopM) };

    // Steel plates between/over the layers (bottom, top).
    internal static (double Bottom, double Top)[] PlateZones(ModularCellSpec spec)
        => SingleLayer(spec)
            ? new[] { (spec.Stage1TopM, spec.DepthM) }
            : new[] { (spec.Stage1TopM, spec.Stage2BottomM), (spec.Stage2TopM, spec.DepthM) };

    internal static ModularMeshData Build(ModularPartKind kind, ModularCellLayout layout, ModularCellSpec spec,
        IReadOnlySet<int>? spent, double compensateX, double compensateZ, bool coarse = false)
    {
        if (!IsPanel(spec)) throw new InvalidDataException("Not a modular Nizh/Duplet panel spec.");
        if (kind is ModularPartKind.Stage1Collision or ModularPartKind.Stage2Collision)
            throw new InvalidDataException("Collision slabs come from the shared slab builder.");
        if (SingleLayer(spec) && kind == ModularPartKind.Stage2Visual)
            throw new InvalidDataException("The Nizh panel has a single active layer.");
        var housingKind = kind is ModularPartKind.Housing or ModularPartKind.HousingVisual;
        for (var tier = coarse ? 3 : 0; tier < Tiers.Length; tier++)
        {
            var detail = Tiers[tier];
            var m = new ReliktModularGeometry.Mesh(1 / compensateX, 1 / compensateZ);
            switch (kind)
            {
                case ModularPartKind.Housing:
                    PhysicalHousing(m, layout, spec);
                    break;
                case ModularPartKind.HousingVisual:
                    VisualFrame(m, layout, spec);
                    if (spec.DupletStyle == StyleSide && !coarse) SideHardware(m, layout, spec);
                    break;
                case ModularPartKind.Stage1Visual when !SingleLayer(spec):
                    PadVisual(m, layout, spec, spent, detail);
                    break;
                case ModularPartKind.Stage1Visual:
                case ModularPartKind.Stage2Visual:
                    FrontVisual(m, layout, spec, spent, detail, coarse);
                    break;
                default:
                    throw new InvalidDataException("Unknown modular part kind.");
            }
            var data = m.Build();
            var fits = data.Triangles.Count > 0 && data.Triangles.Count / 3 <= MaxTriangles && data.Vertices.Count <= MaxVertices &&
                data.Vertices.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
            if (fits) return new(data.Vertices.ToArray(), data.Triangles.ToArray(), data.TriangleCells.ToArray());
            if (housingKind) break;
        }
        throw new InvalidDataException("Generated Nizh/Duplet panel geometry is empty or outside the supported bound.");
    }

    // Render-only relief of one active layer: charge slab with the linear shaped-charge grooves.
    // front = the layer struck first (Stage2 of a Duplet, the only layer of the Nizh panel).
    internal static ModularMeshData LayerRelief(ModularCellLayout layout, ModularCellSpec spec, bool front,
        double compensateX = 1, double compensateZ = 1)
    {
        if (!IsPanel(spec)) throw new InvalidDataException("Not a modular Nizh/Duplet panel spec.");
        var zones = LayerZones(spec);
        var (zb, zt) = front ? zones[^1] : zones[0];
        var m = new ReliktModularGeometry.Mesh(1 / compensateX, 1 / compensateZ);
        var gap = spec.CoverGapM;
        for (var y = 0; y < layout.CountV; y++)
            for (var x = 0; x < layout.CountU; x++)
            {
                var cell = layout.Index(x, y);
                var b = ModularCellGeometry.CellBounds(layout, spec, x, y);
                double xa = b.X0 + gap, xb = b.X1 - gap, za = b.Z0 + gap, zb2 = b.Z1 - gap;
                m.Box(xa, xb, zb, zt - .003, za, zb2, cell);
                const int count = 12;
                var pitch = (zb2 - za - .016) / count;
                for (var k = 0; k < count; k++)
                {
                    var zc = za + .008 + (k + .5) * pitch;
                    m.Box(xa + .006, xb - .006, zt - .003, zt, zc - pitch * .27, zc + pitch * .27, cell);
                }
            }
        var data = m.Build();
        return new(data.Vertices.ToArray(), data.Triangles.ToArray(), data.TriangleCells.ToArray());
    }

    // ---- physical housing ---------------------------------------------------------------

    private static void PhysicalHousing(ReliktModularGeometry.Mesh m, ModularCellLayout layout, ModularCellSpec spec)
    {
        var width = layout.CountU * spec.CellWidthM;
        var length = layout.CountV * spec.CellLengthM;
        double x0 = -width / 2, x1 = width / 2, z0 = -length / 2, z1 = length / 2;
        double w = spec.HousingWallM, pb = spec.HousingBottomM, top = spec.DepthM;
        m.Box(x0, x1, 0, pb, z0, z1, -1);
        m.Box(x0, x0 + w, pb, top, z0, z1, -1);
        m.Box(x1 - w, x1, pb, top, z0, z1, -1);
        m.Box(x0 + w, x1 - w, pb, top, z0, z0 + w, -1);
        m.Box(x0 + w, x1 - w, pb, top, z1 - w, z1, -1);
        // Dividers only inside the layer zones; the plates fill the rest of the interior.
        // Cross dividers are cut between the long ones, so no steel volume is counted twice.
        var segments = new List<(double A, double B)>();
        var cursor = x0 + w;
        for (var x = 1; x < layout.CountU; x++)
        {
            var cx = x0 + x * spec.CellWidthM;
            segments.Add((cursor, cx - w / 2));
            cursor = cx + w / 2;
        }
        segments.Add((cursor, x1 - w));
        foreach (var (zb, zt) in LayerZones(spec))
        {
            for (var x = 1; x < layout.CountU; x++)
            {
                var cx = x0 + x * spec.CellWidthM;
                m.Box(cx - w / 2, cx + w / 2, zb, zt, z0 + w, z1 - w, -1);
            }
            for (var y = 1; y < layout.CountV; y++)
            {
                var cz = z0 + y * spec.CellLengthM;
                foreach (var (sa, sb) in segments)
                    m.Box(sa, sb, zb, zt, cz - w / 2, cz + w / 2, -1);
            }
        }
        foreach (var (pbm, ptm) in PlateZones(spec))
            m.Box(x0 + w, x1 - w, pbm, ptm, z0 + w, z1 - w, -1);
    }

    // Visible frame: pan, outer walls and dividers (internal dividers a little below the lid line).
    private static void VisualFrame(ReliktModularGeometry.Mesh m, ModularCellLayout layout, ModularCellSpec spec)
    {
        var width = layout.CountU * spec.CellWidthM;
        var length = layout.CountV * spec.CellLengthM;
        double x0 = -width / 2, x1 = width / 2, z0 = -length / 2, z1 = length / 2;
        double w = spec.HousingWallM, pb = spec.HousingBottomM, top = spec.DepthM;
        var dividerTop = top - spec.DividerRecessM;
        m.Box(x0, x1, 0, pb, z0, z1, -1);
        m.Box(x0, x0 + w, pb, top, z0, z1, -1);
        m.Box(x1 - w, x1, pb, top, z0, z1, -1);
        m.Box(x0 + w, x1 - w, pb, top, z0, z0 + w, -1);
        m.Box(x0 + w, x1 - w, pb, top, z1 - w, z1, -1);
        for (var x = 1; x < layout.CountU; x++)
        {
            var cx = x0 + x * spec.CellWidthM;
            m.Box(cx - w / 2, cx + w / 2, pb, dividerTop, z0 + w, z1 - w, -1);
        }
        for (var y = 1; y < layout.CountV; y++)
        {
            var cz = z0 + y * spec.CellLengthM;
            m.Box(x0 + w, x1 - w, pb, dividerTop, cz - w / 2, cz + w / 2, -1);
        }
    }

    // Side module only: slotted mounting lip along +Z (the top edge) and a hinge strip along -Z.
    private static void SideHardware(ReliktModularGeometry.Mesh m, ModularCellLayout layout, ModularCellSpec spec)
    {
        var width = layout.CountU * spec.CellWidthM;
        var length = layout.CountV * spec.CellLengthM;
        double x0 = -width / 2, x1 = width / 2, z0 = -length / 2, z1 = length / 2;
        var hb = spec.HousingBottomM;
        m.Box(x0, x1, 0, hb, z1, z1 + .026, -1);
        for (var x = 0; x < layout.CountU; x++)
        {
            var cx = x0 + (x + .5) * spec.CellWidthM;
            m.Box(cx - .052, cx - .012, hb, hb + .003, z1 + .004, z1 + .022, -1);
            m.Box(cx + .012, cx + .052, hb, hb + .003, z1 + .004, z1 + .022, -1);
        }
        m.Box(x0, x1, 0, hb, z0 - .012, z0, -1);
        for (var x = 0; x < layout.CountU; x++)
        {
            var k0 = x0 + x * spec.CellWidthM + .008;
            var k1 = x0 + (x + 1) * spec.CellWidthM - .008;
            var octagon = new List<(double A, double B)>();
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4 + Math.PI / 8;
                octagon.Add((.0055 * Math.Cos(a), .0055 * Math.Sin(a)));
            }
            m.Extrude(octagon, (a, b, c) => (c, hb + .0055 + b, z0 - .006 + a), k0, k1, -1);
        }
    }

    // ---- cell visuals -------------------------------------------------------------------

    private static (double Inset, double Drop)[] LidProfile(int style, bool full)
    {
        if (!full) return new[] { (.004, 0.0) };
        return style switch
        {
            // Thin rolled plate (Nizh).
            StyleNizh => new[] { (.003, 0.0), (0.0, .0025) },
            // Heavy bevelled front plate with two flats (turret).
            StyleTurret => new[] { (.010, 0.0), (.0045, .003), (0.0, .0075) },
            // Formed rounded plate rim (hull).
            StyleHull => Arc(.006),
            // Formed plate (side module).
            _ => Arc(.005),
        };
    }

    private static (double, double)[] Arc(double r)
    {
        var points = new (double, double)[4];
        for (var i = 0; i < 4; i++)
        {
            var theta = i * Math.PI / 6;
            points[i] = (r * (1 - Math.Sin(theta)), r * (1 - Math.Cos(theta)));
        }
        return points;
    }

    private static void LidExtras(ReliktModularGeometry.Mesh m, int cell, int style,
        double xa, double xb, double za, double zb, double yt)
    {
        var zc = (za + zb) / 2;
        switch (style)
        {
            case StyleNizh: // stamped identification plate
                m.Box(xa + .030, xb - .030, yt - .0005, yt + .001, zc - .020, zc + .020, cell);
                break;
            case StyleTurret: // raised stamped boss
                m.Lid(cell, xa + .025, xb - .025, za + .050, zb - .050, yt - .0005, yt + .0025,
                    new[] { (.004, 0.0), (0.0, .002) });
                break;
            default: // plate seam bead across the lid
                m.Box(xa + .020, xb - .020, yt - .0005, yt + .0015, zc - .003, zc + .003, cell);
                break;
        }
    }

    // The visible front plate of every cell (the layer struck first). Intact: lid with bolts.
    // Spent: the lid is gone, curled rim and bolt stubs remain, the burst linear charges stand on the floor.
    private static void FrontVisual(ReliktModularGeometry.Mesh m, ModularCellLayout layout, ModularCellSpec spec,
        IReadOnlySet<int>? spent, Detail d, bool coarse)
    {
        var single = SingleLayer(spec);
        var style = spec.DupletStyle;
        var lidBottom = single ? spec.Stage1CoverBottomM : spec.Stage2CoverBottomM;
        var lidTop = single ? spec.Stage1CoverTopM : spec.Stage2CoverTopM;
        var zoneBottom = single ? spec.Stage1BottomM : spec.Stage2BottomM;
        var zoneTop = single ? spec.Stage1TopM : spec.Stage2TopM;
        var gap = spec.CoverGapM;
        for (var y = 0; y < layout.CountV; y++)
            for (var x = 0; x < layout.CountU; x++)
            {
                var cell = layout.Index(x, y);
                var b = ModularCellGeometry.CellBounds(layout, spec, x, y);
                double xa = b.X0 + gap, xb = b.X1 - gap, za = b.Z0 + gap, zb = b.Z1 - gap;
                if (spent != null && spent.Contains(cell))
                {
                    Rim(m, cell, xa, xb, za, zb, lidBottom, d.RimStrips);
                    Ridges(m, cell, xa, xb, za, zb, zoneBottom, zoneTop, d.Ridges);
                    continue;
                }
                m.Lid(cell, xa, xb, za, zb, lidBottom, lidTop, LidProfile(style, d.FullLid));
                if (d.Extras) LidExtras(m, cell, style, xa, xb, za, zb, lidTop);
            }
        if (d.Bolts == 0 || coarse) return;
        Bolts(m, layout, spec, spent, d, lidTop);
    }

    // Intermediate plate of a Duplet, one pad per cell (the rear layer's cover).
    // Intact: flat pad with a stiffening rib. Rear layer spent: pad bulged and torn.
    private static void PadVisual(ReliktModularGeometry.Mesh m, ModularCellLayout layout, ModularCellSpec spec,
        IReadOnlySet<int>? spent, Detail d)
    {
        double padBottom = spec.Stage1CoverBottomM, padTop = spec.Stage1CoverTopM, gap = spec.CoverGapM;
        for (var y = 0; y < layout.CountV; y++)
            for (var x = 0; x < layout.CountU; x++)
            {
                var cell = layout.Index(x, y);
                var b = ModularCellGeometry.CellBounds(layout, spec, x, y);
                double xa = b.X0 + gap, xb = b.X1 - gap, za = b.Z0 + gap, zb = b.Z1 - gap;
                if (spent != null && spent.Contains(cell))
                {
                    m.Lid(cell, xa + .008, xb - .008, za + .008, zb - .008, padBottom, padTop + .006, new[] { (.030, 0.0) });
                    if (d.RimStrips >= 4) Rim(m, cell, xa + .010, xb - .010, za + .010, zb - .010, padTop, 2);
                    continue;
                }
                m.Lid(cell, xa, xb, za, zb, padBottom, padTop, new[] { (.003, 0.0) });
                if (d.Extras)
                {
                    var zc = (za + zb) / 2;
                    m.Box(xa + .020, xb - .020, padTop - .0005, padTop + .0015, zc - .004, zc + .004, cell);
                }
            }
    }

    private static void Bolts(ReliktModularGeometry.Mesh m, ModularCellLayout layout, ModularCellSpec spec,
        IReadOnlySet<int>? spent, Detail d, double lidTop)
    {
        var style = spec.DupletStyle;
        var width = layout.CountU * spec.CellWidthM;
        var length = layout.CountV * spec.CellLengthM;
        double bx0 = -width / 2, bz0 = -length / 2;
        bool Broken(int cell) => spent != null && spent.Contains(cell);
        if (style == StyleSide)
        {
            // Mounting bolts on the lip along the top (+Z) edge, one per cell column.
            for (var x = 0; x < layout.CountU; x++)
            {
                var cell = layout.Index(x, layout.CountV - 1);
                Bolt(m, cell, bx0 + (x + .5) * spec.CellWidthM, length / 2 + .013, spec.HousingBottomM, Broken(cell), d, 1.0);
            }
            return;
        }
        if (style == StyleNizh)
        {
            // Small bolts on the middle of every seam along Z (they sit on the divider tops).
            for (var j = 0; j < layout.CountV; j++)
                for (var i = 0; i <= layout.CountU; i++)
                {
                    var owner = layout.Index(Math.Min(i, layout.CountU - 1), j);
                    var px = bx0 + i * spec.CellWidthM + (i == 0 ? BoltInsetM : i == layout.CountU ? -BoltInsetM : 0);
                    var pz = bz0 + (j + .5) * spec.CellLengthM;
                    Bolt(m, owner, px, pz, lidTop - .002, Broken(owner), d, .8);
                }
            return;
        }
        for (var j = 0; j <= layout.CountV; j++)
            for (var i = 0; i <= layout.CountU; i++)
            {
                if (style == StyleHull && (i + j) % 2 != 0) continue;
                var owner = layout.Index(Math.Min(i, layout.CountU - 1), Math.Min(j, layout.CountV - 1));
                var px = bx0 + i * spec.CellWidthM + (i == 0 ? BoltInsetM : i == layout.CountU ? -BoltInsetM : 0);
                var pz = bz0 + j * spec.CellLengthM + (j == 0 ? BoltInsetM : j == layout.CountV ? -BoltInsetM : 0);
                Bolt(m, owner, px, pz, lidTop - .002, Broken(owner), d, 1.0);
            }
    }

    // Hex bolt with a hex washer ring (full tier) or a bare hex head; a broken bolt keeps a short stub.
    private static void Bolt(ReliktModularGeometry.Mesh m, int cell, double cx, double cz, double yBase,
        bool broken, Detail d, double scale)
    {
        double washer = WasherRadiusM * scale, head = BoltHeadRadiusM * scale;
        if (d.Bolts >= 2)
        {
            m.PrismY(ReliktModularGeometry.Hex(cx, cz, washer), yBase, yBase + .003, cell);
            if (broken) m.PrismY(ReliktModularGeometry.Hex(cx, cz, head * .85, .3), yBase + .003, yBase + .0042, cell);
            else m.PrismY(ReliktModularGeometry.Hex(cx, cz, head), yBase + .003, yBase + .0075, cell);
        }
        else if (broken) m.PrismY(ReliktModularGeometry.Hex(cx, cz, head * .85, .3), yBase, yBase + .0022, cell);
        else m.PrismY(ReliktModularGeometry.Hex(cx, cz, head * 1.1), yBase, yBase + .0055, cell);
    }

    // Burst linear charges of an exposed (spent) layer: bars standing on the zone floor, some torn short or gone.
    private static void Ridges(ReliktModularGeometry.Mesh m, int cell, double xa, double xb, double za, double zb,
        double floor, double zoneTop, int count)
    {
        var zone = zoneTop - floor;
        var pitch = (zb - za - .016) / count;
        for (var k = 0; k < count; k++)
        {
            if (count >= 6 && ReliktModularGeometry.Noise(cell, 40 + k) < .12) continue;
            var h = Math.Min(.010, zone * (.4 + .5 * ReliktModularGeometry.Noise(cell, 60 + k)));
            var zc = za + .008 + (k + .5) * pitch;
            var half = pitch * .27;
            var xs = xa + .006 + .020 * ReliktModularGeometry.Noise(cell, 80 + k);
            var xe = xb - .006 - .020 * ReliktModularGeometry.Noise(cell, 100 + k);
            m.Box(xs, xe, floor, floor + h, zc - half, zc + half, cell);
        }
    }

    // Rim of the removed plate, bent outwards and up: closed bent-strip shells (long sides first).
    private static void Rim(ReliktModularGeometry.Mesh m, int cell, double xa, double xb, double za, double zb,
        double yb, int strips)
    {
        for (var side = 0; side < strips; side++)
        {
            var longSide = side < 2;
            var f = .6 + .8 * ReliktModularGeometry.Noise(cell, side);
            var h1 = .007 * f;
            var points = longSide
                ? new (double S, double T)[] { (0, 0), (.003 * f, h1), (.010 * f, h1 + .003 * f) }
                : new (double S, double T)[] { (0, 0), (.006 * f, .009 * f) };
            var profile = ReliktModularGeometry.ThickPolyline(points, SheetM);
            var c0 = longSide ? xa + .030 * ReliktModularGeometry.Noise(cell, side + 8) : za + .030 * ReliktModularGeometry.Noise(cell, side + 8);
            var c1 = longSide ? xb - .030 * ReliktModularGeometry.Noise(cell, side + 12) : zb - .030 * ReliktModularGeometry.Noise(cell, side + 12);
            Func<double, double, double, (double, double, double)> map = side switch
            {
                0 => (a, b, c) => (c, yb + b, za - a),
                1 => (a, b, c) => (c, yb + b, zb + a),
                2 => (a, b, c) => (xa - a, yb + b, c),
                _ => (a, b, c) => (xb + a, yb + b, c),
            };
            m.Extrude(profile, map, c0, c1, cell);
        }
    }
}
