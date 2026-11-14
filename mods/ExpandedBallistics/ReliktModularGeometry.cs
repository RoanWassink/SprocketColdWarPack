using System.Numerics;

namespace SprocketExpandedBallistics;

// Procedural geometry for the modular Relikt parts (turret panel, hull panel,
// side box). One active cell reproduces the original Relikt cell exactly: a
// 250 x 130 x 13 mm closed steel slab inclined 25 degrees about Z (rising
// towards +X), top edge at Y = 127.5 mm, centred in a 270 x 150 mm cell.
// All decoration (rounded lids, hex bolts, curled rims, lips, hinge) is render
// only and never enters a collision or mass mesh.
//
// TriangleCells: -1 = shared frame, otherwise the owning cell (x + y*CountU).
// Every shell is closed; shells may overlap but never share a partial face.
internal static class ReliktModularGeometry
{
    internal const double PlateLengthM = .250, PlateWidthM = .130, PlateThicknessM = .013;
    internal const double PlateAngleDegrees = 25, PlateMaxYM = .1275;
    internal const double ActiveVolumeM3 = PlateLengthM * PlateWidthM * PlateThicknessM;
    internal const int MaxTriangleIndices = 24000, MaxVertices = 8000;

    private const double BoltHeadRadiusM = .005, WasherRadiusM = .008, BoltInsetM = .010;
    private const double SheetM = .0015;

    // Plate centre height so that the highest corner is exactly PlateMaxYM.
    internal static double PlateCentreYM
    {
        get
        {
            var a = PlateAngleDegrees * Math.PI / 180;
            var halfHeight = (PlateLengthM * Math.Sin(a) + PlateThicknessM * Math.Cos(a)) / 2;
            return PlateMaxYM - halfHeight;
        }
    }

    internal static ModularMeshData Build(ModularPartKind kind, ModularCellLayout layout, ModularCellSpec spec,
        IReadOnlySet<int>? spent, double compensateX, double compensateZ, bool coarse = false)
    {
        if (spec.ReliktStyle < 1 || spec.ReliktStyle > 3) throw new InvalidDataException("Not a Relikt modular spec.");
        var m = new Mesh(1 / compensateX, 1 / compensateZ);
        switch (kind)
        {
            case ModularPartKind.Housing:
                Frame(m, layout, spec, spec.DepthM);
                break;
            case ModularPartKind.HousingVisual:
                Frame(m, layout, spec, spec.Stage1CoverBottomM);
                if (spec.ReliktStyle == 3 && !coarse) SideHousingHardware(m, layout, spec);
                break;
            case ModularPartKind.Stage1Collision:
                for (var y = 0; y < layout.CountV; y++)
                    for (var x = 0; x < layout.CountU; x++)
                        Plate(m, ModularCellGeometry.CellBounds(layout, spec, x, y), layout.Index(x, y));
                break;
            case ModularPartKind.Stage1Visual:
                CellVisuals(m, layout, spec, spent, coarse);
                break;
            default:
                throw new InvalidDataException("Relikt modular parts have a single active stage.");
        }
        var data = m.Build();
        if (data.Triangles.Count == 0 || data.Triangles.Count > MaxTriangleIndices || data.Vertices.Count > MaxVertices ||
            data.Vertices.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)))
            throw new InvalidDataException("Generated Relikt geometry is empty or outside the supported bound.");
        return new(data.Vertices.ToArray(), data.Triangles.ToArray(), data.TriangleCells.ToArray());
    }

    // Physical housing frame: pan, outer walls and whole-cell dividers up to topM.
    private static void Frame(Mesh m, ModularCellLayout layout, ModularCellSpec spec, double topM)
    {
        var width = layout.CountU * spec.CellWidthM;
        var length = layout.CountV * spec.CellLengthM;
        double x0 = -width / 2, x1 = width / 2, z0 = -length / 2, z1 = length / 2;
        var w = spec.HousingWallM;
        var hb = spec.HousingBottomM;
        m.Box(x0, x1, 0, hb, z0, z1, -1);
        m.Box(x0, x0 + w, hb, topM, z0, z1, -1);
        m.Box(x1 - w, x1, hb, topM, z0, z1, -1);
        m.Box(x0 + w, x1 - w, hb, topM, z0, z0 + w, -1);
        m.Box(x0 + w, x1 - w, hb, topM, z1 - w, z1, -1);
        for (var x = 1; x < layout.CountU; x++)
        {
            var cx = x0 + x * spec.CellWidthM;
            m.Box(cx - w / 2, cx + w / 2, hb, topM, z0 + w, z1 - w, -1);
        }
        for (var y = 1; y < layout.CountV; y++)
        {
            var cz = z0 + y * spec.CellLengthM;
            m.Box(x0 + w, x1 - w, hb, topM, cz - w / 2, cz + w / 2, -1);
        }
    }

    // Exact original active cell as a closed inclined cuboid.
    private static void Plate(Mesh m, (double X0, double X1, double Z0, double Z1) b, int cell)
    {
        var angle = PlateAngleDegrees * Math.PI / 180;
        double ux = Math.Cos(angle), uy = Math.Sin(angle), vx = -Math.Sin(angle), vy = Math.Cos(angle);
        var cx = (b.X0 + b.X1) / 2;
        var cy = PlateCentreYM;
        var cz = (b.Z0 + b.Z1) / 2;
        double hl = PlateLengthM / 2, ht = PlateThicknessM / 2, hz = PlateWidthM / 2;
        var profile = new List<(double A, double B)> { (-hl, -ht), (hl, -ht), (hl, ht), (-hl, ht) };
        m.Extrude(profile, (a, bb, c) => (cx + a * ux + bb * vx, cy + a * uy + bb * vy, cz + c), -hz, hz, cell);
    }

    private static (double Inset, double Drop)[] LidProfile(int style) => style switch
    {
        // Heavy bevelled front plate (two flats).
        1 => new[] { (.008, 0.0), (.0035, .0015), (0.0, .006) },
        // Formed lid rim: three-segment bend, 5 mm radius.
        2 => Arc(.005),
        // Formed plate: three-segment bend, 6 mm radius.
        _ => Arc(.006),
    };

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

    private static void CellVisuals(Mesh m, ModularCellLayout layout, ModularCellSpec spec,
        IReadOnlySet<int>? spent, bool coarse)
    {
        var style = spec.ReliktStyle;
        var yb = spec.Stage1CoverBottomM;
        var yt = spec.Stage1CoverTopM;
        var gap = spec.CoverGapM;
        for (var y = 0; y < layout.CountV; y++)
            for (var x = 0; x < layout.CountU; x++)
            {
                var cell = layout.Index(x, y);
                var b = ModularCellGeometry.CellBounds(layout, spec, x, y);
                double xa = b.X0 + gap, xb = b.X1 - gap, za = b.Z0 + gap, zb = b.Z1 - gap;
                if (spent != null && spent.Contains(cell))
                {
                    CurledRim(m, cell, xa, xb, za, zb, yb);
                    continue;
                }
                m.Lid(cell, xa, xb, za, zb, yb, yt, LidProfile(style));
                if (coarse) continue;
                switch (style)
                {
                    case 1: // stamped boss
                        m.Lid(cell, xa + .030, xb - .030, za + .030, zb - .030, yt - .0005, yt + .003,
                            new[] { (.004, 0.0), (0.0, .0025) });
                        break;
                    case 2: // raised pad
                        m.Box(xa + .014, xb - .014, yt - .0005, yt + .001, za + .014, zb - .014, cell);
                        break;
                    default: // stiffening rib along X
                        m.Box(xa + .030, xb - .030, yt - .0005, yt + .002, (za + zb) / 2 - .004, (za + zb) / 2 + .004, cell);
                        break;
                }
            }
        if (coarse) return;
        var width = layout.CountU * spec.CellWidthM;
        var length = layout.CountV * spec.CellLengthM;
        double bx0 = -width / 2, bz0 = -length / 2;
        if (style == 3)
        {
            // Mounting bolts on the lip along the top (+Z) edge, one per cell column.
            var top = length / 2;
            for (var x = 0; x < layout.CountU; x++)
            {
                var cell = layout.Index(x, layout.CountV - 1);
                Bolt(m, cell, bx0 + (x + .5) * spec.CellWidthM, top + .013, spec.HousingBottomM,
                    spent != null && spent.Contains(cell));
            }
            return;
        }
        for (var j = 0; j <= layout.CountV; j++)
            for (var i = 0; i <= layout.CountU; i++)
            {
                if (style == 2 && (i + j) % 2 != 0) continue;
                var owner = layout.Index(Math.Min(i, layout.CountU - 1), Math.Min(j, layout.CountV - 1));
                var px = bx0 + i * spec.CellWidthM + (i == 0 ? BoltInsetM : i == layout.CountU ? -BoltInsetM : 0);
                var pz = bz0 + j * spec.CellLengthM + (j == 0 ? BoltInsetM : j == layout.CountV ? -BoltInsetM : 0);
                Bolt(m, owner, px, pz, yt - .002, spent != null && spent.Contains(owner));
            }
    }

    internal static List<(double X, double Z)> Hex(double cx, double cz, double r, double rotation = 0)
    {
        var points = new List<(double, double)>(6);
        for (var i = 0; i < 6; i++)
        {
            var a = rotation + i * Math.PI / 3;
            points.Add((cx + r * Math.Cos(a), cz + r * Math.Sin(a)));
        }
        return points;
    }

    // Hexagonal washer plus hex bolt head; a broken bolt keeps washer and a short stub.
    private static void Bolt(Mesh m, int cell, double cx, double cz, double yBase, bool broken)
    {
        m.PrismY(Hex(cx, cz, WasherRadiusM), yBase, yBase + .003, cell);
        if (broken) m.PrismY(Hex(cx, cz, BoltHeadRadiusM * .85, .3), yBase + .003, yBase + .0042, cell);
        else m.PrismY(Hex(cx, cz, BoltHeadRadiusM), yBase + .003, yBase + .0075, cell);
    }

    internal static double Noise(int cell, int salt)
    {
        unchecked
        {
            var h = (uint)((cell + 1) * 73856093) ^ (uint)((salt + 1) * 19349663);
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return h / (double)uint.MaxValue;
        }
    }

    // Rim of the removed lid, bent outwards and up: closed bent-strip shells.
    private static void CurledRim(Mesh m, int cell, double xa, double xb, double za, double zb, double yb)
    {
        for (var side = 0; side < 4; side++)
        {
            var longSide = side < 2;
            var f = .6 + .8 * Noise(cell, side);
            var h1 = .007 * f;
            var points = longSide
                ? new (double S, double T)[] { (0, 0), (.003 * f, h1), (.010 * f, h1 + .003 * f) }
                : new (double S, double T)[] { (0, 0), (.006 * f, .009 * f) };
            var profile = ThickPolyline(points, SheetM);
            var c0 = longSide ? xa + .030 * Noise(cell, side + 8) : za + .030 * Noise(cell, side + 8);
            var c1 = longSide ? xb - .030 * Noise(cell, side + 12) : zb - .030 * Noise(cell, side + 12);
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

    internal static List<(double A, double B)> ThickPolyline((double S, double T)[] p, double thickness)
    {
        var n = p.Length;
        var normals = new (double, double)[n];
        for (var i = 0; i < n; i++)
        {
            var a = p[Math.Max(i - 1, 0)];
            var b = p[Math.Min(i + 1, n - 1)];
            var ds = b.S - a.S; var dt = b.T - a.T;
            var len = Math.Sqrt(ds * ds + dt * dt);
            normals[i] = (-dt / len, ds / len);
        }
        var left = new List<(double, double)>();
        var right = new List<(double, double)>();
        for (var i = 0; i < n; i++)
        {
            left.Add((p[i].S + normals[i].Item1 * thickness / 2, p[i].T + normals[i].Item2 * thickness / 2));
            right.Add((p[i].S - normals[i].Item1 * thickness / 2, p[i].T - normals[i].Item2 * thickness / 2));
        }
        right.Reverse();
        left.AddRange(right);
        return left;
    }

    // Side box only: slotted mounting lip along +Z and a hinge strip along -Z.
    private static void SideHousingHardware(Mesh m, ModularCellLayout layout, ModularCellSpec spec)
    {
        var width = layout.CountU * spec.CellWidthM;
        var length = layout.CountV * spec.CellLengthM;
        double x0 = -width / 2, x1 = width / 2, z0 = -length / 2, z1 = length / 2;
        var hb = spec.HousingBottomM;
        // Flange continuing the pan, with raised tabs flanking each bolt slot.
        m.Box(x0, x1, 0, hb, z1, z1 + .026, -1);
        for (var x = 0; x < layout.CountU; x++)
        {
            var cx = x0 + (x + .5) * spec.CellWidthM;
            m.Box(cx - .115, cx - .022, hb, hb + .003, z1 + .004, z1 + .022, -1);
            m.Box(cx + .022, cx + .115, hb, hb + .003, z1 + .004, z1 + .022, -1);
        }
        // Hinge strip: leaf plus one knuckle per cell column.
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

    internal sealed class Mesh
    {
        private readonly double inverseX, inverseZ;
        private readonly List<Vector3> vertices = new();
        private readonly Dictionary<(long, long, long), int> lookup = new();
        private readonly List<int> triangles = new();
        private readonly List<int> cells = new();
        private readonly List<((double X, double Y, double Z) A, (double X, double Y, double Z) B,
            (double X, double Y, double Z) C)> shell = new();

        internal Mesh(double inverseX, double inverseZ) { this.inverseX = inverseX; this.inverseZ = inverseZ; }

        internal (List<Vector3> Vertices, List<int> Triangles, List<int> TriangleCells) Build()
            => (vertices, triangles, cells);

        private void Tri((double, double, double) a, (double, double, double) b, (double, double, double) c)
            => shell.Add((a, b, c));

        private void Quad((double, double, double) a, (double, double, double) b,
            (double, double, double) c, (double, double, double) d)
        { Tri(a, b, c); Tri(a, c, d); }

        // Commits the staged closed shell, flipping it when its volume is negative.
        private void End(int cell)
        {
            double volume = 0;
            foreach (var (a, b, c) in shell)
                volume += (a.X * (b.Y * c.Z - b.Z * c.Y) - a.Y * (b.X * c.Z - b.Z * c.X) + a.Z * (b.X * c.Y - b.Y * c.X)) / 6.0;
            if (!(Math.Abs(volume) > 1e-18)) throw new InvalidDataException("Degenerate Relikt shell.");
            var flip = volume < 0;
            foreach (var (a, b, c) in shell)
            {
                var ia = Vertex(a); var ib = Vertex(flip ? c : b); var ic = Vertex(flip ? b : c);
                triangles.Add(ia); triangles.Add(ib); triangles.Add(ic); cells.Add(cell);
            }
            shell.Clear();
        }

        private int Vertex((double X, double Y, double Z) p)
        {
            var v = new Vector3((float)(p.X * inverseX), (float)p.Y, (float)(p.Z * inverseZ));
            var key = ((long)Math.Round(v.X * 1e7), (long)Math.Round(v.Y * 1e7), (long)Math.Round(v.Z * 1e7));
            if (lookup.TryGetValue(key, out var index)) return index;
            index = vertices.Count;
            vertices.Add(v);
            lookup[key] = index;
            return index;
        }

        internal void Box(double x0, double x1, double y0, double y1, double z0, double z1, int cell)
        {
            Quad((x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0));
            Quad((x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1));
            Quad((x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1));
            Quad((x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0));
            Quad((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1));
            Quad((x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0));
            End(cell);
        }

        // Closed convex prism standing on Y; polygon given in the XZ plane.
        internal void PrismY(IList<(double X, double Z)> poly, double y0, double y1, int cell)
        {
            var p = poly.ToList();
            double area = 0;
            for (var i = 0; i < p.Count; i++)
            {
                var n = p[(i + 1) % p.Count];
                area += p[i].X * n.Z - n.X * p[i].Z;
            }
            if (area < 0) p.Reverse();
            for (var i = 1; i + 1 < p.Count; i++)
            {
                Tri((p[0].X, y1, p[0].Z), (p[i + 1].X, y1, p[i + 1].Z), (p[i].X, y1, p[i].Z));
                Tri((p[0].X, y0, p[0].Z), (p[i].X, y0, p[i].Z), (p[i + 1].X, y0, p[i + 1].Z));
            }
            for (var i = 0; i < p.Count; i++)
            {
                var n = p[(i + 1) % p.Count];
                Quad((p[i].X, y0, p[i].Z), (p[i].X, y1, p[i].Z), (n.X, y1, n.Z), (n.X, y0, n.Z));
            }
            End(cell);
        }

        // Rounded/bevelled lid: nested rectangular loops from the inset top down to the full-size
        // side, then the vertical side to the bottom. Profile = (inset, drop) from the top face.
        internal void Lid(int cell, double x0, double x1, double z0, double z1, double yBottom, double yTop,
            (double Inset, double Drop)[] profile)
        {
            var loops = new List<(double X0, double X1, double Z0, double Z1, double Y)>();
            foreach (var (inset, drop) in profile)
                loops.Add((x0 + inset, x1 - inset, z0 + inset, z1 - inset, yTop - drop));
            loops.Add((x0, x1, z0, z1, yBottom));
            (double, double, double) V((double X0, double X1, double Z0, double Z1, double Y) l, int i) => i switch
            {
                0 => (l.X0, l.Y, l.Z0),
                1 => (l.X1, l.Y, l.Z0),
                2 => (l.X1, l.Y, l.Z1),
                _ => (l.X0, l.Y, l.Z1),
            };
            for (var k = 0; k + 1 < loops.Count; k++)
                for (var i = 0; i < 4; i++)
                {
                    var j = (i + 1) % 4;
                    Quad(V(loops[k + 1], i), V(loops[k], i), V(loops[k], j), V(loops[k + 1], j));
                }
            var top = loops[0];
            var bottom = loops[^1];
            Tri(V(top, 0), V(top, 3), V(top, 2)); Tri(V(top, 0), V(top, 2), V(top, 1));
            Tri(V(bottom, 0), V(bottom, 1), V(bottom, 2)); Tri(V(bottom, 0), V(bottom, 2), V(bottom, 3));
            End(cell);
        }

        // Closed prism from a simple polygon in the (A,B) plane swept along C.
        internal void Extrude(IList<(double A, double B)> poly,
            Func<double, double, double, (double, double, double)> map, double c0, double c1, int cell)
        {
            var p = poly.ToList();
            double area = 0;
            for (var i = 0; i < p.Count; i++)
            {
                var n = p[(i + 1) % p.Count];
                area += p[i].A * n.B - n.A * p[i].B;
            }
            if (area < 0) p.Reverse();
            foreach (var (i0, i1, i2) in Triangulate(p))
            {
                Tri(map(p[i0].A, p[i0].B, c1), map(p[i1].A, p[i1].B, c1), map(p[i2].A, p[i2].B, c1));
                Tri(map(p[i0].A, p[i0].B, c0), map(p[i2].A, p[i2].B, c0), map(p[i1].A, p[i1].B, c0));
            }
            for (var i = 0; i < p.Count; i++)
            {
                var n = p[(i + 1) % p.Count];
                Quad(map(p[i].A, p[i].B, c0), map(n.A, n.B, c0), map(n.A, n.B, c1), map(p[i].A, p[i].B, c1));
            }
            End(cell);
        }

        private static List<(int, int, int)> Triangulate(List<(double A, double B)> p)
        {
            var index = Enumerable.Range(0, p.Count).ToList();
            var result = new List<(int, int, int)>();
            double Cross((double A, double B) a, (double A, double B) b, (double A, double B) c)
                => (b.A - a.A) * (c.B - a.B) - (b.B - a.B) * (c.A - a.A);
            bool Inside((double A, double B) q, (double A, double B) a, (double A, double B) b, (double A, double B) c)
                => Cross(a, b, q) >= 0 && Cross(b, c, q) >= 0 && Cross(c, a, q) >= 0;
            while (index.Count > 3)
            {
                var clipped = false;
                for (var i = 0; i < index.Count && !clipped; i++)
                {
                    int i0 = index[(i + index.Count - 1) % index.Count], i1 = index[i], i2 = index[(i + 1) % index.Count];
                    if (Cross(p[i0], p[i1], p[i2]) <= 1e-15) continue;
                    var blocked = index.Any(j => j != i0 && j != i1 && j != i2 && Inside(p[j], p[i0], p[i1], p[i2]));
                    if (blocked) continue;
                    result.Add((i0, i1, i2));
                    index.RemoveAt(i);
                    clipped = true;
                }
                if (!clipped) throw new InvalidDataException("Relikt profile polygon cannot be triangulated.");
            }
            result.Add((index[0], index[1], index[2]));
            return result;
        }
    }
}
