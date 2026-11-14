namespace SprocketCarouselAutoloader;

/// <summary>
/// Native ammo-rack packer, read from the disassembly of AmmoRackUtils.CalculateCapacity
/// (logs\0.2.23-native-packer.txt): floor(W/d) x floor(H/d) x floor(L/len), unsigned integer
/// division in mm, 0 for d = 0 or L &lt; len. Used only to lay out the bustle model on the same grid;
/// gameplay keeps calling the native method.
/// </summary>
public static class RackPacking
{
    public static int NativeCapacity(int widthMm, int heightMm, int lengthMm, int diameterMm, int roundLengthMm)
    {
        if (diameterMm <= 0 || roundLengthMm <= 0 || widthMm < 0 || heightMm < 0 || lengthMm < roundLengthMm) return 0;
        return widthMm / diameterMm * (heightMm / diameterMm) * (lengthMm / roundLengthMm);
    }
}

/// <summary>
/// One revolver drum along the gun axis (0.2.23, BESLUITEN 2026-11-02 "Volgt de trommel").
/// The drum diameter is min(W,H) of the rack and its length the rack length. Chambers lie on
/// concentric rings, outermost first: r_k = R - wall - d/2 - k(d + clearance), n_k = floor(pi / asin((d + clearance) / (2 r_k))),
/// stopping once r_k - d/2 drops below the hub radius. Each chamber tube holds
/// floor((L - 2 end plates) / len) rounds one behind the other. Pure; shared by the gameplay
/// capacity, the inspector and the model.
/// </summary>
public static class RevolverDrum
{
    public const double WallMm = 10, ClearanceMm = 5, MinimumHubMm = 30, HubCaliberFactor = .2, EndPlateMm = 15;

    public readonly record struct Ring(double RadiusMm, int Chambers);

    public sealed record Layout(double DiameterMm, double HubMm, IReadOnlyList<Ring> Rings, int Rows)
    {
        public int Chambers => Rings.Sum(r => r.Chambers);
        public int Capacity => Chambers * Rows;
    }

    public static double HubRadiusMm(double diameterMm) => Math.Max(MinimumHubMm, HubCaliberFactor * diameterMm);

    static bool Valid(double w, double h, double l, double d, double len) =>
        double.IsFinite(w) && double.IsFinite(h) && double.IsFinite(l) && double.IsFinite(d) && double.IsFinite(len) &&
        w > 0 && h > 0 && l > 0 && d > 0 && len > 0;

    public static int Rows(double lengthMm, double roundLengthMm) =>
        roundLengthMm > 0 && lengthMm > 2 * EndPlateMm ? (int)Math.Floor((lengthMm - 2 * EndPlateMm) / roundLengthMm + 1e-9) : 0;

    // Ring k radius and chamber count; false once the ring would cut into the hub.
    static bool RingAt(double radiusMm, double diameterMm, int k, out double r, out int chambers)
    {
        chambers = 0;
        r = radiusMm - WallMm - diameterMm / 2 - k * (diameterMm + ClearanceMm);
        if (r - diameterMm / 2 < HubRadiusMm(diameterMm)) return false;
        double half = (diameterMm + ClearanceMm) / (2 * r);
        if (!(half > 0 && half < 1)) return false;
        chambers = (int)Math.Floor(Math.PI / Math.Asin(half) + 1e-9);
        return chambers >= 1;
    }

    /// <summary>Allocation-free capacity, used by the per-frame gameplay checks.</summary>
    public static int Capacity(double widthMm, double heightMm, double lengthMm, double diameterMm, double roundLengthMm)
    {
        if (!Valid(widthMm, heightMm, lengthMm, diameterMm, roundLengthMm)) return 0;
        int rows = Rows(lengthMm, roundLengthMm);
        if (rows <= 0) return 0;
        double radius = Math.Min(widthMm, heightMm) / 2;
        int chambers = 0;
        for (int k = 0; k < 1000 && RingAt(radius, diameterMm, k, out _, out var n); k++) chambers += n;
        return chambers * rows;
    }

    public static Layout Plan(double widthMm, double heightMm, double lengthMm, double diameterMm, double roundLengthMm)
    {
        var rings = new List<Ring>();
        if (!Valid(widthMm, heightMm, lengthMm, diameterMm, roundLengthMm))
            return new(0, 0, rings, 0);
        double radius = Math.Min(widthMm, heightMm) / 2;
        for (int k = 0; k < 1000 && RingAt(radius, diameterMm, k, out var r, out var n); k++) rings.Add(new(r, n));
        return new(2 * radius, HubRadiusMm(diameterMm), rings, Rows(lengthMm, roundLengthMm));
    }

    /// <summary>Revolver capacity = min(native capacity, drum capacity); never negative.</summary>
    public static int Limit(int nativeCapacity, double widthMm, double heightMm, double lengthMm, double diameterMm, double roundLengthMm) =>
        Math.Max(0, Math.Min(nativeCapacity, Capacity(widthMm, heightMm, lengthMm, diameterMm, roundLengthMm)));
}
