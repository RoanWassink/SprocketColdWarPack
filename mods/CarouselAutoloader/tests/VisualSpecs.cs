using SprocketCarouselAutoloader;

// 0.2.23/0.2.24 test and viewer specs. Rack sizes in metres, shell envelope in mm as
// ShellLoaderGeometry reports it (api = Expanded Ballistics status 1, else native status 0).
// Capacity: bustle = native packer; revolver = min(native, drum); two-piece = floor(pieces / 2).
// Load: the cannon load point assumed for the rack (rack-local metres, bounds centre), or null.
public static class VisualSpecs
{
    public sealed record Entry(string Id, Spec Spec, int NativeCapacity, string Source, double FullProjectile, double FullCharge);

    public static Entry Make(string id, string name, ModelKind kind, double x, double y, double z, double cal, double projectile,
        double charge, string source, bool mirror = false, bool twoPiece = false, bool api = false, (double X, double Y, double Z)? load = null)
    {
        int d = (int)Math.Ceiling(cal), full = (int)Math.Ceiling(projectile + charge);
        int w = (int)Math.Round(x * 1000), h = (int)Math.Round(y * 1000), l = (int)Math.Round(z * 1000);
        int native = RackPacking.NativeCapacity(w, h, l, d, full);
        var (p, c) = twoPiece ? TwoPiecePolicy.Pieces(api ? 1 : 0, projectile, charge, cal) : (projectile, charge);
        int longest = TwoPiecePolicy.LongestMm(p, c);
        int capacity;
        if (twoPiece)
        {
            int pieces = RackPacking.NativeCapacity(w, h, l, d, longest);
            if (kind == ModelKind.Revolver)
                pieces = RevolverPolicy.Fits(cal, longest) ? Math.Min(pieces, RevolverDrum.Capacity(w, h, l, d, longest)) : 0;
            capacity = TwoPiecePolicy.Shots(pieces);
        }
        else capacity = kind == ModelKind.Bustle ? native :
            RevolverPolicy.Fits(cal, projectile + charge) ? RevolverDrum.Limit(native, w, h, l, d, full) : 0;
        var spec = new Spec(name, kind, x, y, z, cal, p, c, capacity, mirror, twoPiece,
            load?.X ?? double.NaN, load?.Y ?? double.NaN, load?.Z ?? double.NaN);
        return new(id, spec, native, source, projectile, charge);
    }

    // T-84 surblinde (Addon), blueprint of today: rack x/y/z 1361/310/1665 mm, child of the 155 mm cannon at
    // (-0.015, -0.102, -2.865) m. Load point estimated from the 1.13 m the game showed for the 0.2.23 outlet,
    // assuming it lies on the gun axis: cannon-local (0, 0, -0.667) m = rack-local (0.015, 0.102, 2.198) m.
    public static readonly (double X, double Y, double Z) T84Load = (.015, .102, 2.198);
    public static readonly (double X, double Y, double Z) T84OldOutlet = (.6495, .062, 1.2635);

    public static readonly Entry[] Viewer =
    {
        // Shell profile apfsds_gen4 through Expanded Ballistics 0.22.19: 1023 mm projectile + 1200 mm charge, 155 mm.
        Make("t84-bustle", "T-84 surblinde (Addon) bustle 155 mm, apfsds_gen4, complete rounds", ModelKind.Bustle,
            1.361, .310, 1.665, 155, 6.6 * 155, 1200, "blueprint 2026-11-02 + Expanded Ballistics 0.22.19", api: true, load: T84Load),
        Make("t84-bustle-tweedelig", "T-84 surblinde (Addon) bustle 155 mm, apfsds_gen4, two-piece", ModelKind.Bustle,
            1.361, .310, 1.665, 155, 6.6 * 155, 1200, "blueprint 2026-11-02 + Expanded Ballistics 0.22.19", twoPiece: true, api: true, load: T84Load),
        // 125 mm native round 375 + 713 = 1088 mm in a 750 mm rack: only two-piece (680 + 408 mm) fits.
        Make("bustle-125-tweedelig", "Bustle 125 mm two-piece in a 750 mm rack", ModelKind.Bustle, 1.30, .40, .75, 125, 375, 713,
            "own", twoPiece: true, load: (.10, .05, .375 + .40)),
        Make("revolver-75-amx13", "Revolver 75 mm, AMX-13 style", ModelKind.Revolver, .42, .42, .92, 75, 225, 645, "own",
            load: (.05, .12, .46 + .30)),
        // User's case: 105 mm, 693 mm projectile + 1200 mm charge (1893 mm round) from the shell API.
        Make("revolver-105-tweedelig", "Revolver 105 mm two-piece, 693 + 1200 mm", ModelKind.Revolver, .56, .56, 1.25, 105, 693, 1200,
            "user case", twoPiece: true, api: true, load: (0, .15, .625 + .35)),
        Make("revolver-155-t58", "Revolver 155 mm two-piece, T58 style", ModelKind.Revolver, .90, .90, 1.20, 155, 465, 900,
            "own", twoPiece: true, load: (0, .30, .60 + .45)),
    };

    public static readonly Entry[] Extra =
    {
        Make("t84-bustle-vanilla", "T-84 bustle, 3-calibre round (0.2.23 look)", ModelKind.Bustle, 1.361, .310, 1.665, 155, 465, 1200, "0.2.23"),
        Make("t84-bustle-vanilla-load", "T-84 bustle, 3-calibre round, auto arm", ModelKind.Bustle, 1.361, .310, 1.665, 155, 465, 1200, "0.2.24", load: T84Load),
        Make("t84-bustle-mirror", "T-84 bustle, mirrored feed arm", ModelKind.Bustle, 1.361, .310, 1.665, 155, 465, 1200, "test", true),
        Make("revolver-105", "Revolver 105 mm", ModelKind.Revolver, .56, .56, 1.15, 105, 315, 785, "own"),
        Make("bustle-120", "Bustle 120 mm", ModelKind.Bustle, 1.10, .40, 1.62, 120, 360, 620, "own"),
        Make("revolver-75-mirror", "Revolver 75 mm, mirrored", ModelKind.Revolver, .42, .42, .92, 75, 225, 645, "test", true),
        Make("revolver-75-wide", "Revolver 75 mm in a wide rack", ModelKind.Revolver, .90, .45, .95, 75, 225, 645, "0.2.22 test spec", load: (-.30, 0, .80)),
        Make("bustle-105", "Bustle 105 mm", ModelKind.Bustle, 1.40, .50, 1.05, 105, 315, 650, "0.2.22 test spec", load: (-.5, -.1, .9)),
        Make("bustle-35", "Bustle 35 mm (Autosave 2026-10-07)", ModelKind.Bustle, .538, .249, .545, 35, 105, 440, "blueprint"),
        Make("revolver-105-1893", "Revolver 105 mm, 1893 mm round, complete rounds", ModelKind.Revolver, .56, .56, 1.25, 105, 693, 1200, "user case", api: true),
        Make("revolver-100-1860", "Revolver 100 mm, 1860 mm round in a long drum", ModelKind.Revolver, .50, .50, 1.95, 100, 660, 1200, "user fire bug", api: true),
        Make("bustle-125-full", "Bustle 125 mm complete rounds in a 750 mm rack", ModelKind.Bustle, 1.30, .40, .75, 125, 375, 713, "own"),
    };
}
