using System.Numerics;

namespace SprocketCarouselAutoloader;

public enum ModelKind { Bustle, Revolver }

// Rack bounds in metres (x width, y height, z length toward the gun), shell envelope in mm as
// ShellLoaderGeometry reports it (two-piece: the projectile and charge piece lengths), and the
// capacity the rack actually has in the game. LoadX/Y/Z: the cannon's load point in rack-local
// metres (bounds centre); NaN when unknown, then the arm keeps the 0.2.23 MirrorFeedArm outlet.
public record Spec(string Name, ModelKind Kind, double X, double Y, double Z, double Caliber, double Projectile,
    double Charge, int Capacity, bool Mirror = false, bool TwoPiece = false,
    double LoadX = double.NaN, double LoadY = double.NaN, double LoadZ = double.NaN);

public enum PartRole { Round, Frame, Drum, Arm }

// A static piece of the prop, already in rack-local coordinates (origin = bounds centre).
// Rounds carry their slot: a round is shown while Slot < AmountStored, so the highest slot
// (the round at the outlet) is the first to disappear.
public sealed class MeshPart
{
    public string Name { get; set; } = "";
    public PartRole Role { get; set; }
    public int Slot { get; set; } = -1;
    public List<float> Vertices { get; set; } = new();
    public List<float> Normals { get; set; } = new();
    public List<int> Indices { get; set; } = new();
    public List<int> Materials { get; set; } = new(); // one per triangle
    public int Triangles => Indices.Count / 3;
}

public sealed class LoaderModel
{
    public Spec Spec { get; set; } = null!;
    public List<MeshPart> Meshes { get; set; } = new();
    public int Slots { get; set; }            // round places built (== Spec.Capacity when Complete)
    public int LayoutSlots { get; set; }      // places in the packer grid or the drum
    public bool Complete => Slots == Spec.Capacity && Slots > 0;
    public double DiameterM { get; set; }     // true round diameter and length (packer units, mm rounded up)
    public double LengthM { get; set; }
    public int Columns { get; set; }          // bustle grid; Rows is also the revolver rounds per chamber
    public int Layers { get; set; }
    public int Rows { get; set; }
    public RevolverDrum.Layout? Drum { get; set; }
    public int Pieces { get; set; }           // two-piece: projectile + charge places (2 per complete round)
    public double ProjectileM { get; set; }   // two-piece piece lengths (true size)
    public double ChargeM { get; set; }
    public FeedArm Arm { get; set; }          // arm axis and the gameplay measuring point
    public double ArmProtrusionM { get; set; } // part of the feed arm beyond the +z bound
    public string Detail { get; set; } = "";
    public int Triangles => Meshes.Sum(p => p.Triangles);
}

// 0.2.23: a static prop at true size (BESLUITEN 2026-11-02 "Prop, geen simulatie", "Ware maat",
// "Revolver = één trommel"). The rounds come first, in the native packer grid or in the drum,
// at exactly the packer diameter and length; frame, drum and feed arm fill what is left.
// Nothing moves: the only runtime change is which round slots are shown.
public static class AutoloaderModelGeometry
{
    public const int Dark = 0, Steel = 1, Bright = 2, Brass = 3, Red = 4, MaterialCount = 5;

    // Round zones of the OpenSCAD round (Models/scad/parts/round.scad), canonical d = 1.
    public const double CaseZone = 6, ProjectileZone = 3, RimZone = .065, BandZone = .05, NoseZone = .8;

    // Two-piece zones (projectile.scad, charge.scad): projectile base/band and nose, charge plug.
    public const double PieceBaseZone = .05, PlugZone = .08;

    // Detail levels, finest first; Create takes the first one inside the triangle budget.
    sealed record Detailing(string Name, UnitMesh Round, int HoleSegments, UnitMesh Cradle, UnitMesh Clamp, UnitMesh Projectile, UnitMesh Charge);
    static readonly Detailing[] Details =
    {
        new("fine", AutoloaderMeshData.Round12, 12, AutoloaderMeshData.Cradle, AutoloaderMeshData.Clamp, AutoloaderMeshData.Projectile12, AutoloaderMeshData.Charge12),
        new("medium", AutoloaderMeshData.Round8, 12, AutoloaderMeshData.Cradle, AutoloaderMeshData.Clamp, AutoloaderMeshData.Projectile8, AutoloaderMeshData.Charge8),
        new("medium-lite", AutoloaderMeshData.Round8, 8, AutoloaderMeshData.CradleLite, AutoloaderMeshData.ClampLite, AutoloaderMeshData.Projectile8, AutoloaderMeshData.Charge8),
        new("coarse", AutoloaderMeshData.Round8Coarse, 8, AutoloaderMeshData.CradleLite, AutoloaderMeshData.ClampLite, AutoloaderMeshData.Projectile8, AutoloaderMeshData.Charge8)
    };

    static int Mm(double metres) => (int)Math.Round(metres * 1000);

    public static LoaderModel Create(Spec s)
    {
        if (!(double.IsFinite(s.X) && double.IsFinite(s.Y) && double.IsFinite(s.Z) && s.X > 0 && s.Y > 0 && s.Z > 0) ||
            !(double.IsFinite(s.Caliber) && s.Caliber > 0) || !(double.IsFinite(s.Projectile) && s.Projectile > 0) ||
            !(double.IsFinite(s.Charge) && s.Charge >= 0) || s.Capacity < 0)
            throw new ArgumentException("Invalid spec");
        LoaderModel? model = null;
        foreach (var detail in Details)
        {
            model = new LoaderModel { Spec = s, Detail = detail.Name };
            if (s.Kind == ModelKind.Bustle) Bustle(model, detail); else Revolver(model, detail);
            if (model.Triangles <= AutoloaderVisualPolicy.TriangleBudget) break;
        }
        return model!;
    }

    // ---------------------------------------------------------------- mesh helpers
    static Vector3 P(double x, double y, double z) => new((float)x, (float)y, (float)z);
    static float Q(double v) => (float)Math.Round(v, 6);

    static MeshPart New(LoaderModel m, string name, PartRole role, int slot = -1)
    {
        var part = new MeshPart { Name = name, Role = role, Slot = slot };
        m.Meshes.Add(part);
        return part;
    }

    static int Vertex(MeshPart m, Vector3 p, Vector3 n)
    {
        if (n.LengthSquared() > 1e-20f) n = Vector3.Normalize(n);
        m.Vertices.Add(Q(p.X)); m.Vertices.Add(Q(p.Y)); m.Vertices.Add(Q(p.Z));
        m.Normals.Add(Q(n.X)); m.Normals.Add(Q(n.Y)); m.Normals.Add(Q(n.Z));
        return m.Vertices.Count / 3 - 1;
    }

    // Flat triangle, wound so that Cross(b-a, c-a) points along the requested outward normal.
    static void Tri(MeshPart m, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, int material)
    {
        var n = Vector3.Cross(b - a, c - a);
        if (n.LengthSquared() < 1e-18f) return;
        if (Vector3.Dot(n, outward) < 0) (b, c) = (c, b);
        n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        m.Indices.Add(Vertex(m, a, n)); m.Indices.Add(Vertex(m, b, n)); m.Indices.Add(Vertex(m, c, n));
        m.Materials.Add(material);
    }

    static void Quad(MeshPart m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int material)
    { Tri(m, a, b, c, outward, material); Tri(m, a, c, d, outward, material); }

    static void Box(MeshPart m, double x0, double y0, double z0, double x1, double y1, double z1, int material)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        Vector3 V(int i) => P((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1);
        Quad(m, V(1), V(3), V(7), V(5), Vector3.UnitX, material); Quad(m, V(0), V(4), V(6), V(2), -Vector3.UnitX, material);
        Quad(m, V(2), V(6), V(7), V(3), Vector3.UnitY, material); Quad(m, V(0), V(1), V(5), V(4), -Vector3.UnitY, material);
        Quad(m, V(4), V(5), V(7), V(6), Vector3.UnitZ, material); Quad(m, V(0), V(2), V(3), V(1), -Vector3.UnitZ, material);
    }

    // Cylinder or ring along z with smooth radial normals on the curved faces.
    static void Tube(MeshPart m, double cx, double cy, double z0, double z1, double ro, double ri, int sides, int material)
    {
        for (int i = 0; i < sides; i++)
        {
            double a = 2 * Math.PI * i / sides, b = 2 * Math.PI * (i + 1) / sides;
            Vector3 R(double ang) => P(Math.Cos(ang), Math.Sin(ang), 0);
            Vector3 V(double r, double ang, double z) => P(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang), z);
            int Add(double r, double ang, double z, Vector3 n) => Vertex(m, V(r, ang, z), n);
            void Strip(double r, Vector3 na, Vector3 nb, bool flip)
            {
                int p0 = Add(r, a, z0, na), p1 = Add(r, b, z0, nb), p2 = Add(r, b, z1, nb), p3 = Add(r, a, z1, na);
                if (flip) { m.Indices.AddRange(new[] { p0, p2, p1, p0, p3, p2 }); }
                else { m.Indices.AddRange(new[] { p0, p1, p2, p0, p2, p3 }); }
                m.Materials.Add(material); m.Materials.Add(material);
            }
            // Outer face: a->b counter-clockwise around +z, so (p0,p1,p2) faces outward.
            Strip(ro, R(a), R(b), false);
            if (ri > 0) Strip(ri, -R(a), -R(b), true);
            foreach (var (z, n) in new[] { (z1, Vector3.UnitZ), (z0, -Vector3.UnitZ) })
            {
                if (ri > 0) Quad(m, V(ri, a, z), V(ro, a, z), V(ro, b, z), V(ri, b, z), n, material);
                else Tri(m, P(cx, cy, z), V(ro, a, z), V(ro, b, z), n, material);
            }
        }
    }

    // OpenSCAD unit part, mapped into place. Normals are recomputed after the mapping from the
    // shared vertices (the converter keeps creases as separate vertices).
    static void Place(MeshPart m, UnitMesh unit, Func<Vector3, Vector3> map, bool mirrored, Func<Vector3, int> material)
    {
        int count = unit.Positions.Length / 3, first = m.Vertices.Count / 3;
        var source = new Vector3[count];
        var mapped = new Vector3[count];
        var normals = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            source[i] = new(unit.Positions[i * 3], unit.Positions[i * 3 + 1], unit.Positions[i * 3 + 2]);
            mapped[i] = map(source[i]);
        }
        var order = new int[unit.Indices.Length];
        for (int t = 0; t < unit.Indices.Length; t += 3)
        {
            int a = unit.Indices[t], b = unit.Indices[t + 1], c = unit.Indices[t + 2];
            if (mirrored) (b, c) = (c, b);
            order[t] = a; order[t + 1] = b; order[t + 2] = c;
            var n = Vector3.Cross(mapped[b] - mapped[a], mapped[c] - mapped[a]);
            normals[a] += n; normals[b] += n; normals[c] += n;
        }
        for (int i = 0; i < count; i++) Vertex(m, mapped[i], normals[i]);
        for (int t = 0; t < order.Length; t += 3)
        {
            m.Indices.Add(first + order[t]); m.Indices.Add(first + order[t + 1]); m.Indices.Add(first + order[t + 2]);
            m.Materials.Add(material((source[order[t]] + source[order[t + 1]] + source[order[t + 2]]) / 3));
        }
    }

    // ---------------------------------------------------------------- rounds and pieces
    // Piecewise z mapping: only the plain zones stretch, the detailed zones keep calibre proportions.
    static void Zoned(MeshPart part, UnitMesh unit, double x, double y, double zBase, double d, double[] from, double[] to, bool zoned,
        Func<Vector3, int> material)
    {
        double top = from[^1], len = to[^1];
        double Z(double z)
        {
            if (!zoned) return z / top * len;
            for (int i = 1; i < from.Length; i++)
                if (z <= from[i] + 1e-9 || i == from.Length - 1)
                    return to[i - 1] + (z - from[i - 1]) / (from[i] - from[i - 1]) * (to[i] - to[i - 1]);
            return len;
        }
        Place(part, unit, v => P(x + v.X * d, y + v.Y * d, zBase + Z(v.Z)), false, material);
    }

    // Complete round at true size: diameter d, overall length len, case (charge) length charge.
    static void Round(LoaderModel m, UnitMesh unit, int slot, double x, double y, double zBase, double d, double charge, double len)
    {
        var part = New(m, "Granaat " + slot, PartRole.Round, slot);
        double c0 = CaseZone, top = CaseZone + ProjectileZone;
        double c = Math.Clamp(charge, 0, len), p = len - c;
        bool zoned = c >= RimZone * d + 1e-6 && p >= (BandZone + NoseZone) * d + 1e-6;
        Zoned(part, unit, x, y, zBase, d, new[] { 0, RimZone, c0, c0 + BandZone, top - NoseZone, top },
            new[] { 0, RimZone * d, c, c + BandZone * d, len - NoseZone * d, len }, zoned,
            v => v.Z < c0 ? Brass : v.Z < c0 + BandZone ? Red : Dark);
    }

    // Two-piece: a separate projectile (band, body, ogive) or charge (case with rim and plug), each at
    // its own true length. Both pieces of a complete round share its slot and disappear together.
    static void Piece(LoaderModel m, Detailing detail, bool projectile, int slot, double x, double y, double zBase, double d, double len)
    {
        var part = New(m, (projectile ? "Projectiel " : "Lading ") + slot, PartRole.Round, slot);
        if (projectile)
            Zoned(part, detail.Projectile, x, y, zBase, d, new[] { 0, PieceBaseZone, ProjectileZone - NoseZone, ProjectileZone },
                new[] { 0, PieceBaseZone * d, len - NoseZone * d, len }, len >= (PieceBaseZone + NoseZone) * d + 1e-6,
                v => v.Z < PieceBaseZone ? Red : Dark);
        else
            Zoned(part, detail.Charge, x, y, zBase, d, new[] { 0, RimZone, CaseZone, CaseZone + PlugZone },
                new[] { 0, RimZone * d, len - PlugZone * d, len }, len >= (RimZone + PlugZone) * d + 1e-6,
                v => v.Z < CaseZone ? Brass : Red);
    }

    // Places in disappearance order get either a complete round, or (two-piece) projectile and charge
    // pieces alternately; pieces 2k and 2k+1 form complete round k. Returns the complete rounds built.
    static int Fill(LoaderModel m, Detailing detail, List<(double X, double Y, double Front)> places, int shots, double d, double len, double charge)
    {
        var s = m.Spec;
        if (!s.TwoPiece)
        {
            int count = Math.Min(places.Count, shots);
            for (int i = 0; i < count; i++)
                Round(m, detail.Round, count - 1 - i, places[i].X, places[i].Y, places[i].Front - len, d, charge, len);
            return count;
        }
        int pairs = Math.Min(places.Count / 2, shots);
        for (int i = 0; i < 2 * pairs; i++)
        {
            bool projectile = i % 2 == 0;
            double piece = projectile ? m.ProjectileM : m.ChargeM;
            Piece(m, detail, projectile, pairs - 1 - i / 2, places[i].X, places[i].Y, places[i].Front - piece, d, piece);
        }
        return pairs;
    }

    // ---------------------------------------------------------------- feed arm
    // Chunky loading trough on the arm axis plus rammer housing, ram head and brackets. Placement and
    // clamping come from FeedArmGeometry (shared with gameplay); nothing here is thinner than 0.3 d.
    static void Arm(LoaderModel m, FeedArm a, double d, bool bracket)
    {
        var s = m.Spec;
        double Y = s.Y / 2, Z = s.Z / 2, half = .55 * d, depth = .64 * d;
        double xt = a.X, yt = a.Y, sy = a.ScaleY, z0 = a.Z0, z1 = a.Z1, housing = a.Housing;
        int inward = a.Inward;
        var trough = New(m, "Laadgoot", PartRole.Arm);
        Place(trough, AutoloaderMeshData.Trough, v => P(xt + v.X * d, yt + v.Y * d * sy, z0 + v.Z * (z1 - z0)), false, _ => Bright);
        double bottom = yt - depth * sy;
        if (housing > 0)
        {
            var box = New(m, "Rambehuizing", PartRole.Arm);
            double hx0 = xt + inward * half, hx1 = xt + inward * (half + housing);
            double hy0 = bottom, hy1 = bottom + Math.Max(.42 * d * sy, .3 * d);
            Box(box, hx0, hy0, z0, hx1, hy1, z1, Dark);
            // Ram cylinder on top of the housing (diameter 0.3 d).
            var cylinder = New(m, "Ramcilinder", PartRole.Arm);
            double r = .15 * d, cy = Math.Min(hy1 + r, Y - r); // sinks into the housing when the rack is low
            Tube(cylinder, (hx0 + hx1) / 2, cy, z0 + Math.Min(.3 * d, (z1 - z0) / 4), z1, r, 0, 12, Steel);
        }
        var head = New(m, "Ramkop", PartRole.Arm);
        double headLength = Math.Min(.36 * d, (z1 - z0) / 2);
        Place(head, AutoloaderMeshData.RamHead, v => P(xt + v.X * d, yt + v.Y * d, z0 + v.Z / .36 * headLength), false, _ => Red);
        if (bracket)
        {
            // Legs from the rack floor up to the trough, at its rear and front end, when there is room.
            double height = bottom - (-Y);
            if (height >= .3 * d)
            {
                double bx0 = Math.Min(xt - half, xt + inward * (half + housing));
                double bx1 = Math.Max(xt + half, xt + inward * (half + housing));
                double t = .3 * d;
                foreach (var (za, zb) in new[] { (z0, z0 + t), (z1 - t, z1) })
                {
                    var leg = New(m, "Armsteun", PartRole.Arm);
                    Box(leg, bx0, -Y, za, bx1, bottom, zb, Steel);
                }
            }
        }
        double max = double.MinValue;
        foreach (var part in m.Meshes)
            if (part.Role == PartRole.Arm)
                for (int i = 2; i < part.Vertices.Count; i += 3) max = Math.Max(max, part.Vertices[i]);
        m.Arm = a;
        m.ArmProtrusionM = Math.Max(0, max - Z);
    }

    // ---------------------------------------------------------------- bustle
    // Rounds (or two-piece pieces) in the native packer grid floor(W/d) x floor(H/d) x floor(L/len),
    // each in a cradle, with cross clamps over the top, side plates, posts and floor/rear plate in the
    // space the grid leaves. The top stays open.
    static void Bustle(LoaderModel m, Detailing detail)
    {
        var s = m.Spec;
        int dMm = (int)Math.Ceiling(s.Caliber), cellMm = FeedArmGeometry.CellMm(s);
        var g = BustleLayout.Of(s.X, s.Y, s.Z, dMm, cellMm);
        int wMm = Mm(s.X), hMm = Mm(s.Y), lMm = Mm(s.Z);
        m.Columns = g.Columns; m.Layers = g.Layers; m.Rows = g.Rows;
        int places = RackPacking.NativeCapacity(wMm, hMm, lMm, dMm, cellMm);
        m.Pieces = s.TwoPiece ? places : 0;
        m.LayoutSlots = s.TwoPiece ? TwoPiecePolicy.Shots(places) : places;
        double d = dMm / 1000.0, cell = cellMm / 1000.0, charge = Math.Min(s.Charge / 1000, cell);
        m.DiameterM = d; m.LengthM = cell;
        if (s.TwoPiece) { m.ProjectileM = s.Projectile / 1000; m.ChargeM = Math.Max(s.Charge / 1000, (RimZone + PlugZone) * d); }
        double X = s.X / 2, Y = s.Y / 2, Z = s.Z / 2;
        if (m.LayoutSlots <= 0) return;
        double y0 = g.Y0, z0 = g.Z0, zf = g.Zf, yTop = g.YTop, plate = g.Plate, gap = g.Gap, strap = g.Strap;
        int cols = g.Columns, layers = g.Layers, rows = g.Rows;
        int sgn = s.Mirror ? -1 : 1;
        var arm = FeedArmGeometry.Bustle(s, g);
        // Disappearance order: front row first, top layer first, from the arm's side across.
        bool fromRight = arm.Auto ? arm.X >= 0 : sgn > 0;
        var list = new List<(double X, double Y, double Front)>();
        for (int row = rows - 1; row >= 0; row--)
            for (int layer = layers - 1; layer >= 0; layer--)
                for (int k = 0; k < cols; k++)
                {
                    int col = fromRight ? cols - 1 - k : k;
                    list.Add((g.ColumnX(col), g.LayerY(layer), z0 + (row + 1) * cell));
                }
        m.Slots = Fill(m, detail, list, s.Capacity, d, cell, charge);

        var cradles = New(m, "Goten", PartRole.Frame);
        for (int layer = 0; layer < layers; layer++)
            for (int col = 0; col < cols; col++)
            {
                double cx = g.ColumnX(col), cy = g.LayerY(layer);
                Place(cradles, detail.Cradle, v => P(cx + v.X * d, cy + v.Y * d, z0 + v.Z * (zf - z0)), false, _ => Steel);
            }
        double gridLength = zf - z0;
        double[] at = gridLength >= 6 * d ? new[] { .1, .5, .9 } : new[] { .2, .8 };
        double width = Math.Min(.25 * d, gridLength * .08);
        var clamps = New(m, "Dwarsklemmen", PartRole.Frame);
        foreach (var f in at)
        {
            double zc = z0 + f * gridLength, za = zc - width / 2, zb = zc + width / 2;
            for (int col = 0; col < cols; col++)
            {
                double cx = g.ColumnX(col), cy = g.LayerY(layers - 1);
                Place(clamps, detail.Clamp, v => P(cx + v.X * d, cy + v.Y * d, za + v.Z * width), false, _ => Bright);
            }
            if (strap > 0) Box(clamps, -X, yTop, za, X, yTop + strap, zb, Bright);
            if (gap >= .002)
                foreach (int sideSign in new[] { -1, 1 })
                    Box(clamps, sideSign * (cols * d / 2), y0, za, sideSign * (X - plate), yTop, zb, Bright);
        }
        var frame = New(m, "Zijplaten en frame", PartRole.Frame);
        if (plate >= .002)
            foreach (int sideSign in new[] { -1, 1 })
                Box(frame, sideSign * (X - plate), -Y, -Z, sideSign * X, yTop, zf, Dark);
        double inside = plate >= .002 ? X - plate : X;
        if (g.Floor > 0) Box(frame, -inside, -Y, -Z, inside, y0, zf, Dark);
        if (g.Rear > 0) Box(frame, -inside, y0, -Z, inside, yTop, z0, Dark);

        // Feed arm from the front of the grid (or one round/piece length before the outlet) up to the outlet.
        Arm(m, arm, d, true);
    }

    // ---------------------------------------------------------------- revolver
    static void Revolver(LoaderModel m, Detailing detail)
    {
        var s = m.Spec;
        int dMm = (int)Math.Ceiling(s.Caliber), cellMm = FeedArmGeometry.CellMm(s);
        int wMm = Mm(s.X), hMm = Mm(s.Y), lMm = Mm(s.Z);
        var drum = RevolverDrum.Plan(wMm, hMm, lMm, dMm, cellMm);
        m.Drum = drum; m.Rows = drum.Rows;
        m.Pieces = s.TwoPiece ? drum.Capacity : 0;
        m.LayoutSlots = s.TwoPiece ? TwoPiecePolicy.Shots(drum.Capacity) : drum.Capacity;
        double d = dMm / 1000.0, cell = cellMm / 1000.0, charge = Math.Min(s.Charge / 1000, cell);
        m.DiameterM = d; m.LengthM = cell;
        if (s.TwoPiece) { m.ProjectileM = s.Projectile / 1000; m.ChargeM = Math.Max(s.Charge / 1000, (RimZone + PlugZone) * d); }
        if (m.LayoutSlots <= 0) return;
        double Z = s.Z / 2, D = Math.Min(s.X, s.Y), R = D / 2;
        double ep = RevolverDrum.EndPlateMm / 1000, wall = RevolverDrum.WallMm / 1000, pitch = (dMm + RevolverDrum.ClearanceMm) / 1000.0;
        var arm = FeedArmGeometry.Revolver(s, drum);
        double cx = arm.DrumX, cy = arm.DrumY, start = arm.Angle;
        double Angle(int ring, int j) => start + 2 * Math.PI * j / drum.Rings[ring].Chambers + (ring % 2 == 1 ? Math.PI / drum.Rings[ring].Chambers : 0);

        // Disappearance order: the chamber at the ram first, around the outer ring, then the inner
        // rings; front round (piece) first.
        var list = new List<(double X, double Y, double Front)>();
        for (int ring = 0; ring < drum.Rings.Count; ring++)
            for (int j = 0; j < drum.Rings[ring].Chambers; j++)
            {
                double r = drum.Rings[ring].RadiusMm / 1000, a = Angle(ring, j);
                for (int row = 0; row < drum.Rows; row++)
                    list.Add((cx + r * Math.Cos(a), cy + r * Math.Sin(a), Z - ep - row * cell));
            }
        m.Slots = Fill(m, detail, list, s.Capacity, d, cell, charge);

        // End plates: one holed tile per chamber (depends on the layout, so built here), a hub with bolts.
        double hubRadius = drum.Rings[^1].RadiusMm / 1000 - pitch / 2;
        foreach (int end in new[] { 1, -1 })
        {
            var plate = New(m, end > 0 ? "Voorste eindplaat" : "Achterste eindplaat", PartRole.Drum);
            double zIn = end * (Z - ep), zFaceIn = zIn + end * .03 * ep, zFaceOut = zIn + end * .66 * ep;
            for (int ring = 0; ring < drum.Rings.Count; ring++)
            {
                double rc = drum.Rings[ring].RadiusMm / 1000, ri = rc - pitch / 2, ro = ring == 0 ? R : rc + pitch / 2;
                int n = drum.Rings[ring].Chambers;
                for (int j = 0; j < n; j++)
                    Tile(plate, cx, cy, rc, Angle(ring, j), Math.PI / n, ri, ro, .40 * d, detail.HoleSegments, zFaceOut, zFaceIn, ring == 0, Steel);
            }
            var hub = New(m, end > 0 ? "Voorste naaf" : "Achterste naaf", PartRole.Drum);
            double hr = hubRadius * 1.03;
            Place(hub, AutoloaderMeshData.HubCap, v => P(cx + v.X * hr, cy + v.Y * hr, zIn + end * v.Z * ep), end < 0, _ => Bright);
        }
        var axle = New(m, "As", PartRole.Drum);
        Tube(axle, cx, cy, -Z + ep, Z - ep, drum.HubMm / 1000 * .9, 0, 12, Dark);
        var hoops = New(m, "Trommelbanden", PartRole.Drum);
        double inner = Z - ep, hoop = Math.Min(.22 * d, inner / 3);
        foreach (var f in new[] { -1 / 3.0, 1 / 3.0 })
            Tube(hoops, cx, cy, f * inner - hoop / 2, f * inner + hoop / 2, R, R - wall, 24, Bright);

        // Small loading trough with ram in front of the ram chamber, up to the gameplay outlet.
        Arm(m, arm, d, false);
    }

    // Annular-sector tile [ri, ro] x [a - half, a + half] around the drum centre with one round hole
    // of radius rh at (rc, a). Both faces are a zipper between the tile outline and the hole polygon,
    // ordered by angle around the hole centre (the tile is star-shaped from there).
    static void Tile(MeshPart m, double cx, double cy, double rc, double a, double half, double ri, double ro, double rh,
        int segments, double zOut, double zIn, bool rim, int material)
    {
        const int arc = 3;
        var outline = new List<(double X, double Y)>();
        for (int i = 0; i <= arc; i++) { double t = a - half + 2 * half * i / arc; outline.Add((ro * Math.Cos(t), ro * Math.Sin(t))); }
        for (int i = 0; i <= arc; i++) { double t = a + half - 2 * half * i / arc; outline.Add((ri * Math.Cos(t), ri * Math.Sin(t))); }
        double hx = rc * Math.Cos(a), hy = rc * Math.Sin(a);
        double first = Math.Atan2(outline[0].Y - hy, outline[0].X - hx);
        double Around(double x, double y) { double v = Math.Atan2(y - hy, x - hx) - first; while (v < 0) v += 2 * Math.PI; while (v >= 2 * Math.PI) v -= 2 * Math.PI; return v; }
        var outer = outline.Select(p => (p.X, p.Y, A: Around(p.X, p.Y))).ToList();
        outer[0] = (outer[0].X, outer[0].Y, 0);
        var hole = new List<(double X, double Y, double A)>();
        for (int j = 0; j < segments; j++)
        {
            double t = first + 2 * Math.PI * j / segments;
            hole.Add((hx + rh * Math.Cos(t), hy + rh * Math.Sin(t), 2 * Math.PI * j / segments));
        }
        Vector3 At((double X, double Y, double A) p, double z) => P(cx + p.X, cy + p.Y, z);
        var tris = new List<((double X, double Y, double A) A, (double X, double Y, double A) B, (double X, double Y, double A) C)>();
        int i0 = 0, j0 = 0, nb = outer.Count, nh = hole.Count;
        while (i0 < nb || j0 < nh)
        {
            double nextOuter = i0 + 1 < nb ? outer[i0 + 1].A : 2 * Math.PI, nextHole = j0 + 1 < nh ? hole[j0 + 1].A : 2 * Math.PI;
            var bi = outer[i0 % nb]; var hj = hole[j0 % nh];
            if (i0 < nb && (j0 >= nh || nextOuter <= nextHole)) { tris.Add((bi, outer[(i0 + 1) % nb], hj)); i0++; }
            else { tris.Add((bi, hole[(j0 + 1) % nh], hj)); j0++; }
        }
        float sign = zOut > zIn ? 1 : -1;
        foreach (var t in tris)
        {
            Tri(m, At(t.A, zOut), At(t.B, zOut), At(t.C, zOut), new(0, 0, sign), material);
            Tri(m, At(t.A, zIn), At(t.B, zIn), At(t.C, zIn), new(0, 0, -sign), material);
        }
        for (int j = 0; j < nh; j++)
        {
            var p = hole[j]; var q = hole[(j + 1) % nh];
            var inward = P(hx - (p.X + q.X) / 2, hy - (p.Y + q.Y) / 2, 0);
            Quad(m, At(p, zOut), At(q, zOut), At(q, zIn), At(p, zIn), inward, material);
        }
        if (rim)
            for (int i = 0; i < arc; i++)
            {
                var p = outer[i]; var q = outer[i + 1];
                Quad(m, At(p, zOut), At(q, zOut), At(q, zIn), At(p, zIn), P((p.X + q.X) / 2, (p.Y + q.Y) / 2, 0), material);
            }
    }
}
