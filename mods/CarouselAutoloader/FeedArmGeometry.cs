namespace SprocketCarouselAutoloader;

// Bustle grid in the rack: rounds (or two-piece pieces) on the native packer grid, centred in x,
// on the floor plate in y and against the rear in z; frame parts use the space the grid leaves.
// Shared by the model and the feed-arm placement so both use the same columns and layers.
public sealed record BustleLayout(int Columns, int Layers, int Rows, double D, double Cell, double Plate, double Gap,
    double Floor, double Strap, double Rear, double X0, double Y0, double Z0, double Zf, double YTop)
{
    public int Places => Columns * Layers * Rows;
    public double ColumnX(int column) => X0 + (column + .5) * D;
    public double LayerY(int layer) => Y0 + (layer + .5) * D;

    public static BustleLayout Of(double x, double y, double z, int diameterMm, int cellMm)
    {
        int w = (int)Math.Round(x * 1000), h = (int)Math.Round(y * 1000), l = (int)Math.Round(z * 1000);
        int cols = diameterMm > 0 ? w / diameterMm : 0, layers = diameterMm > 0 ? h / diameterMm : 0;
        int rows = cellMm > 0 && l >= cellMm ? l / cellMm : 0;
        double d = diameterMm / 1000.0, cell = cellMm / 1000.0, X = x / 2, Y = y / 2, Z = z / 2;
        double fx = x - cols * d, fy = y - layers * d, fz = z - rows * cell;
        double side = fx / 2, plate = Math.Min(side, .08 * d), gap = side - plate;
        double floor = Math.Min(fy / 2, .05 * d), strap = Math.Min(fy - Math.Max(floor, 0), .05 * d);
        double rear = Math.Min(fz / 3, .06 * d);
        if (floor < .002) floor = 0;
        if (strap < .002) strap = 0;
        if (rear < .002) rear = 0;
        double x0 = -cols * d / 2, y0 = -Y + floor, z0 = -Z + rear;
        return new(cols, layers, rows, d, cell, plate, gap, floor, strap, rear, x0, y0, z0, z0 + rows * cell, y0 + layers * d);
    }
}

// Feed arm axis (trough centre) and the point the game measures the feed distance from: the point
// of the arm's front face nearest the cannon's load point. Rack-local, origin at the bounds centre.
public readonly record struct FeedArm(double X, double Y, int Inward, double ScaleY, double Housing, double Z0, double Z1,
    double MeasureX, double MeasureY, double MeasureZ, bool Auto, double DrumX, double DrumY, double Angle)
{
    public double Distance(double x, double y, double z) => Math.Sqrt(Math.Pow(MeasureX - x, 2) + Math.Pow(MeasureY - y, 2) + Math.Pow(MeasureZ - z, 2));
}

public static class FeedArmGeometry
{
    public static bool HasLoadPoint(Spec s) => double.IsFinite(s.LoadX) && double.IsFinite(s.LoadY) && double.IsFinite(s.LoadZ);

    public static int CellMm(Spec s) => s.TwoPiece ? TwoPiecePolicy.LongestMm(s.Projectile, s.Charge) : (int)Math.Ceiling(s.Projectile + s.Charge);

    // Arm cross-section (trough 1.1 d wide, 0.64 d deep, housing 0.34 d beside it), clamped into the
    // rack so nothing sticks out sideways. The front face spans the trough and the round it carries.
    static FeedArm Fit(Spec s, double xt, double yt, int inward, double z0, double z1, bool auto, double drumX, double drumY, double angle)
    {
        double d = Math.Ceiling(s.Caliber) / 1000, X = s.X / 2, Y = s.Y / 2;
        double half = .55 * d, depth = .64 * d;
        double housing = Math.Min(.34 * d, s.X - 2 * half);
        if (housing < .3 * d) housing = 0;
        double inner = half + housing, outer = half;
        double lo = -X + (inward < 0 ? inner : outer), hi = X - (inward < 0 ? outer : inner);
        xt = lo <= hi ? Math.Clamp(xt, lo, hi) : 0;
        double ylo = -Y + depth, yhi = Y - .5 * d, sy = 1;
        if (ylo <= yhi) yt = Math.Clamp(yt, ylo, yhi);
        else { yt = yhi; sy = Math.Max(.1, (yt + Y) / depth); }
        double mx = xt, my = yt, mz = z1;
        if (HasLoadPoint(s))
        {
            mx = Math.Clamp(s.LoadX, xt - half, xt + half);
            my = Math.Clamp(s.LoadY, yt - depth * sy, Math.Min(yt + .5 * d, Y));
        }
        return new(xt, yt, inward, sy, housing, z0, z1, mx, my, mz, auto, drumX, drumY, angle);
    }

    static FeedArm Nearest(Spec s, IEnumerable<FeedArm> candidates)
    {
        FeedArm best = default; double bestDistance = double.MaxValue, bestAxis = double.MaxValue; bool any = false;
        foreach (var c in candidates)
        {
            double dist = c.Distance(s.LoadX, s.LoadY, s.LoadZ);
            double axis = Math.Sqrt(Math.Pow(c.X - s.LoadX, 2) + Math.Pow(c.Y - s.LoadY, 2));
            if (!any || dist < bestDistance - 1e-9 || Math.Abs(dist - bestDistance) <= 1e-9 && axis < bestAxis - 1e-9)
            { best = c; bestDistance = dist; bestAxis = axis; any = true; }
        }
        return best;
    }

    /// <summary>Bustle: without a load point the 0.2.23 outlet on the MirrorFeedArm side; with one, the
    /// grid column and layer whose arm face lies nearest. Both mirror-side outlets stay candidates, so the
    /// measured point is never farther from the load point than the old outlet.</summary>
    public static FeedArm Bustle(Spec s, BustleLayout layout)
    {
        double len = layout.Cell;
        var outlet = BustleFeedGeometry.Outlet(s.X, s.Z, s.Caliber, s.Mirror);
        double z1 = outlet.Z, z0 = Math.Max(layout.Zf, z1 - len);
        FeedArm Side(bool mirror)
        {
            var o = BustleFeedGeometry.Outlet(s.X, s.Z, s.Caliber, mirror);
            return Fit(s, o.X, o.Y, mirror ? 1 : -1, z0, z1, false, 0, 0, 0);
        }
        if (!HasLoadPoint(s))
        {
            var old = Side(s.Mirror);
            // Gameplay keeps measuring from the raw 0.2.23 outlet in this fallback.
            return old with { MeasureX = outlet.X, MeasureY = outlet.Y, MeasureZ = outlet.Z };
        }
        var candidates = new List<FeedArm>();
        for (int layer = 0; layer < layout.Layers; layer++)
            for (int col = 0; col < layout.Columns; col++)
            {
                double xc = layout.ColumnX(col);
                candidates.Add(Fit(s, xc, layout.LayerY(layer), xc >= 0 ? -1 : 1, z0, z1, true, 0, 0, 0));
            }
        candidates.Add(Side(false) with { Auto = true });
        candidates.Add(Side(true) with { Auto = true });
        return Nearest(s, candidates);
    }

    /// <summary>Revolver: the drum moves toward the load point within the rack and the ram sits on the
    /// outer chamber ring at the point nearest the load point; the old outlet placements stay candidates.</summary>
    public static FeedArm Revolver(Spec s, RevolverDrum.Layout drum)
    {
        double d = Math.Ceiling(s.Caliber) / 1000, D = Math.Min(s.X, s.Y), Z = s.Z / 2, ep = RevolverDrum.EndPlateMm / 1000;
        var outlet = BustleFeedGeometry.Outlet(s.X, s.Z, s.Caliber, s.Mirror);
        double z0 = Z - .34 * ep, z1 = Math.Max(outlet.Z, Z + .3 * d), r0 = drum.Rings.Count > 0 ? drum.Rings[0].RadiusMm / 1000 : 0;
        FeedArm Toward(double tx, double ty, bool mirror, bool auto)
        {
            double cx = Math.Clamp(tx, -(s.X - D) / 2, (s.X - D) / 2), cy = Math.Clamp(ty, -(s.Y - D) / 2, (s.Y - D) / 2);
            double a = Math.Abs(tx - cx) + Math.Abs(ty - cy) > 1e-6 ? Math.Atan2(ty - cy, tx - cx) : mirror ? Math.PI : 0;
            double xt = cx + r0 * Math.Cos(a), yt = cy + r0 * Math.Sin(a);
            return Fit(s, xt, yt, xt >= cx + 1e-9 ? -1 : xt <= cx - 1e-9 ? 1 : mirror ? 1 : -1, z0, z1, auto, cx, cy, a);
        }
        FeedArm Side(bool mirror)
        {
            var o = BustleFeedGeometry.Outlet(s.X, s.Z, s.Caliber, mirror);
            return Toward(o.X, o.Y, mirror, false);
        }
        if (!HasLoadPoint(s))
        {
            var old = Side(s.Mirror);
            return old with { MeasureX = outlet.X, MeasureY = outlet.Y, MeasureZ = outlet.Z };
        }
        // Also the arm straight at either old outlet (drum as in 0.2.23): its face contains the old
        // measuring point, so the new point is never farther than before, even for small calibres.
        FeedArm AtOutlet(bool mirror)
        {
            var o = BustleFeedGeometry.Outlet(s.X, s.Z, s.Caliber, mirror);
            var side = Side(mirror);
            return Fit(s, o.X, o.Y, mirror ? 1 : -1, z0, z1, true, side.DrumX, side.DrumY, side.Angle);
        }
        return Nearest(s, new[] { Toward(s.LoadX, s.LoadY, s.Mirror, true), Side(false) with { Auto = true }, Side(true) with { Auto = true },
            AtOutlet(false), AtOutlet(true) });
    }

    public static FeedArm For(Spec s)
    {
        int dMm = (int)Math.Ceiling(s.Caliber), cell = CellMm(s);
        if (s.Kind == ModelKind.Bustle) return Bustle(s, BustleLayout.Of(s.X, s.Y, s.Z, dMm, cell));
        return Revolver(s, RevolverDrum.Plan(Math.Round(s.X * 1000), Math.Round(s.Y * 1000), Math.Round(s.Z * 1000), dMm, cell));
    }
}
