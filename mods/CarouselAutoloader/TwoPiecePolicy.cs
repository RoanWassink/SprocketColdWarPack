namespace SprocketCarouselAutoloader;

/// <summary>
/// Two-piece ammunition for the bustle and the revolver (0.2.24, BESLUITEN 2026-11-02 "Ja, jouw getallen",
/// "Ook op de revolver"). Projectile and charge are stored as separate pieces: the rack only needs the
/// longest piece, it holds floor(pieces / 2) complete rounds, and the cycle is 1.25 x slower (two ram
/// strokes). Stock still counts complete rounds with the native size/type identity.
/// </summary>
public static class TwoPiecePolicy
{
    public const double CycleFactor = 1.25;

    /// <summary>Piece lengths in mm. Native rounds (status 0) split like the carousel
    /// (CarouselGeometry.StorageLengths, 125 mm: 680 + 408); the shell API (status 1) already
    /// reports projectile and charge separately and is used as is, also like the carousel.</summary>
    public static (double Projectile, double Charge) Pieces(int status, double projectileMm, double chargeMm, double caliberMm)
    {
        if (status == 1) return (projectileMm, chargeMm);
        var full = projectileMm + chargeMm;
        return full >= 3 * caliberMm ? CarouselGeometry.StorageLengths(caliberMm, full) : (projectileMm, chargeMm);
    }

    public static int LongestMm(double projectileMm, double chargeMm) => (int)Math.Ceiling(Math.Max(projectileMm, chargeMm));

    public static int Shots(int pieces) => Math.Max(0, pieces) / 2;

    public static double CycleSeconds(double seconds) => CycleFactor * seconds;
}
