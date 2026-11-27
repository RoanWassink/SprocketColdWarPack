namespace SprocketCarouselAutoloader;

public readonly record struct Segment(double DiameterMm, double DepthMm);
public enum CarouselLayout { HorizontalCassette, VerticalCharge, StackedUnitary }
public readonly record struct CarouselSize(int Capacity, double MaxLengthMm, double DiameterMm,
    double DepthMm, double MechanismMassKg, double ReloadSeconds, string Reason,
    double RequiredDepthMm = 0, double MaxChargeLengthMm = 0, double RequiredDiameterMm = 0,
    double StoredProjectileMm = 0, double StoredChargeMm = 0,
    int Tiers = 1, int SlotsPerTier = 0);

public static class CarouselGeometry
{
    public const int MaximumStackedCapacity = 256;
    public const double StackedBaseDepthMm = 120;
    public const double DefaultStackedCycleFactor = 0.57;
    private static double stackedCycleFactor = DefaultStackedCycleFactor;
    public static double StackedCycleFactor
    {
        get => stackedCycleFactor;
        set => stackedCycleFactor = double.IsFinite(value) && value > 0 ? value : DefaultStackedCycleFactor;
    }
    public static Segment[] ResolveBasket(IEnumerable<Segment> source, double ringDiameterMm)
    {
        var segments = source.ToArray();
        if (!double.IsFinite(ringDiameterMm) || ringDiameterMm <= 0) return Array.Empty<Segment>();
        return segments.Select((s, i) => new Segment(i == 0 && s.DiameterMm == 0 ? ringDiameterMm : Math.Min(s.DiameterMm, ringDiameterMm), s.DepthMm)).ToArray();
    }
    public static int FullRoundLength(int caliberMm, int propellantLengthMm) => checked(caliberMm * 3 + propellantLengthMm);
    // A calibre-scaled separate-charge allocation, not a replacement for native ballistics.
    public static (double Projectile, double Charge) StorageLengths(double caliber, double nativeLength,
        CarouselLayout layout = CarouselLayout.HorizontalCassette)
    {
        if (layout == CarouselLayout.StackedUnitary) return (nativeLength, 0);
        var charge = Math.Min(nativeLength - caliber * 3, caliber * (408.0 / 125));
        if (layout == CarouselLayout.VerticalCharge)
        {
            var body = caliber * (680.0 / 125);
            // A fixed radial body; budget above the reference shot becomes upright charge height.
            var overflow = Math.Max(0, nativeLength - body - caliber * (408.0 / 125));
            return (body, charge + overflow);
        }
        return (nativeLength - charge, charge);
    }
    public static double StackedTierHeightMm(double caliber) => caliber * 1.18 + 8;
    private static double Width(double caliber, CarouselLayout layout) =>
        layout == CarouselLayout.StackedUnitary ? caliber * 1.18 + 8 : caliber * 1.28 + 12;
    private static double Pitch(double caliber, CarouselLayout layout) => Width(caliber, layout) * layout switch
    {
        CarouselLayout.HorizontalCassette => 1.40,
        CarouselLayout.StackedUnitary => 1.08,
        _ => 1.12
    };
    private static double RadialEnd(double diameter, double caliber, CarouselLayout layout)
    {
        var outer = diameter / 2 - 40;
        var halfWidth = Width(caliber, layout) / 2;
        return outer > halfWidth ? Math.Sqrt(outer * outer - halfWidth * halfWidth) : 0;
    }
    private static double RadialLimit(double diameter, double caliber, CarouselLayout layout)
    {
        var end = RadialEnd(diameter, caliber, layout);
        // Storage reaches inward from the outer wall. Reserve the drive and at least three cassette positions.
        return Math.Max(0, Math.Min(end - Math.Max(180, diameter * .12),
            2 * (end - Pitch(caliber, layout) / Math.Sqrt(3))));
    }
    private static double RequiredDiameter(double caliber, double storedLength, CarouselLayout layout)
    {
        double low = 0, high = Math.Max(1000, storedLength * 4 + caliber * 4);
        while (RadialLimit(high, caliber, layout) < storedLength) high *= 2;
        for (int i = 0; i < 60; i++)
        {
            var mid = (low + high) / 2;
            if (RadialLimit(mid, caliber, layout) >= storedLength) high = mid;
            else low = mid;
        }
        var ceil = Math.Ceiling(high);
        return RadialLimit(ceil, caliber, layout) >= storedLength ? ceil : ceil + 1;
    }
    // Cassette pitch at the mean storage radius is calibrated, not a collision model for rectangular rounds.
    // Reference: 2.5 m diameter, 125 mm, 680 mm projectile + 408 mm charge => AZ 22, MZ 28.
    public static CarouselSize Calculate(IEnumerable<Segment> source, double caliberMm, double lengthMm, double shellMassKg,
        CarouselLayout layout = CarouselLayout.HorizontalCassette,double? actualProjectileMm=null,double? actualChargeMm=null)
    {
        var segments = source.ToArray();
        if (segments.Length == 0 || segments.Any(s => !double.IsFinite(s.DiameterMm) ||
            !double.IsFinite(s.DepthMm) || s.DiameterMm <= 0 || s.DepthMm <= 0) ||
            !double.IsFinite(caliberMm) || !double.IsFinite(lengthMm) || !double.IsFinite(shellMassKg) ||
            caliberMm <= 0 || lengthMm < caliberMm * 3 || shellMassKg <= 0 || !Enum.IsDefined(typeof(CarouselLayout), layout))
            return new(0, 0, 0, 0, 0, 0, "Invalid basket or ammunition dimensions");
        var diameter = segments.Min(s => s.DiameterMm);
        var depth = segments.Sum(s => s.DepthMm);
        var unitary = layout == CarouselLayout.StackedUnitary;
        var (projectileLength, chargeLength) = StorageLengths(caliberMm, lengthMm, layout);
        var custom=actualProjectileMm.HasValue || actualChargeMm.HasValue;
        if(custom)
        {
            if(!actualProjectileMm.HasValue || !actualChargeMm.HasValue || !double.IsFinite(actualProjectileMm.Value) ||
                actualProjectileMm.Value<=0 || !double.IsFinite(actualChargeMm.Value) || actualChargeMm.Value<0)
                return new(0,0,diameter,depth,0,0,"Invalid custom projectile geometry");
            if(unitary) { projectileLength=actualProjectileMm.Value+actualChargeMm.Value; chargeLength=0; }
            else { projectileLength=actualProjectileMm.Value; chargeLength=actualChargeMm.Value; }
        }
        var horizontal = layout == CarouselLayout.HorizontalCassette;
        var pitch = Pitch(caliberMm, layout);
        var end = RadialEnd(diameter, caliberMm, layout);
        var maxLength = RadialLimit(diameter, caliberMm, layout);
        // Two horizontal thicknesses for AZ; projectile thickness + upright charge height for MZ; one tier for stacked unitary.
        var tierHeight = StackedTierHeightMm(caliberMm);
        var requiredDepth = unitary
            ? StackedBaseDepthMm + tierHeight
            : 150 + 18 + caliberMm + (horizontal ? caliberMm * 1.28 : Math.Max(chargeLength, caliberMm * 1.28));
        var chargeCap = caliberMm * (408.0 / 125);
        var verticalChargeLimit = Math.Max(0, depth - 150 - 18 - caliberMm);
        // Invert the storage allocation back to the cannon's native PropellantLength slider.
        var maxNativePropellant = unitary
            ? maxLength - caliberMm * 3
            : horizontal
                ? (maxLength >= chargeCap ? maxLength - caliberMm * 3 + chargeCap : Math.Min(maxLength, chargeCap))
                : (verticalChargeLimit >= chargeCap ? projectileLength + verticalChargeLimit - caliberMm * 3 : verticalChargeLimit);
        if(custom) maxNativePropellant = unitary || horizontal ? maxLength : verticalChargeLimit;
        var storedLength = horizontal ? Math.Max(projectileLength, chargeLength) : projectileLength;
        var centerRadius = end - storedLength / 2;
        int capacity = 0, tiers = 0, slotsPerTier = 0;
        string reason = "";
        if (projectileLength > maxLength) reason = unitary ? "Complete round is too long for the basket diameter" : "Storage projectile is too long for the basket diameter";
        else if (horizontal && chargeLength > maxLength) reason = "Storage charge is too long for the basket diameter";
        else if (depth < requiredDepth) reason = unitary ? "Basket is too shallow for one stacked tier and mechanism" : "Basket is too shallow for both components and mechanism";
        else if (centerRadius <= pitch / 2) reason = "Basket is too narrow for the drive and cassettes";
        else
        {
            slotsPerTier = Math.Min(64, (int)Math.Floor(Math.PI / Math.Asin(pitch / (2 * centerRadius))));
            if (slotsPerTier < 3) { slotsPerTier = 0; capacity = 0; reason = unitary ? "Fewer than three round positions fit per tier" : "Fewer than three cassette positions fit"; }
            else if (unitary)
            {
                tiers = Math.Max(1, (int)Math.Floor((depth - StackedBaseDepthMm) / tierHeight));
                capacity = Math.Min(MaximumStackedCapacity, checked(slotsPerTier * tiers));
                tiers = (capacity + slotsPerTier - 1) / slotsPerTier;
            }
            else
            {
                tiers = 1;
                capacity = slotsPerTier;
            }
        }
        var perRoundMass = unitary ? Math.Max(0.4, 5.0 * Math.Pow(caliberMm / 125.0, 2)) : 5.0;
        var mass = 120 + diameter / 1000 * 40 + capacity * perRoundMass;
        var baseReload = BustleTiming.ReloadSeconds(caliberMm, shellMassKg, lengthMm, diameter / 1000 * .35);
        var reload = unitary ? Math.Max(.06, baseReload * StackedCycleFactor) : baseReload;
        return new(capacity, maxLength, diameter, depth, mass, reload, reason, requiredDepth,
            Math.Max(0, maxNativePropellant), RequiredDiameter(caliberMm, storedLength, layout), projectileLength, chargeLength,
            tiers, slotsPerTier);
    }
}
