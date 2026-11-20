namespace SprocketCarouselAutoloader;

public enum LoaderKind { Bustle, Semi, Revolver, Belt }

/// <summary>
/// One date per loading mechanism/layout. Carousel is an option on a native
/// basket, so these dates are enforced by the runtime/UI rather than by a part
/// definition; Bustle, Semi and Revolver additionally have their own dated part JSON.
/// </summary>
public static class CarouselDatePolicy
{
    public static readonly DateTime BustleDate = new(1952, 1, 1);
    public static readonly DateTime SemiAutoloaderDate = new(1953, 1, 1);
    public static readonly DateTime BeltDate = new(1965, 1, 1);
    public static readonly DateTime RevolverDate = new(1952, 1, 1);
    public static readonly DateTime T64CarouselDate = new(1967, 1, 1);
    public static readonly DateTime T72CarouselDate = new(1973, 1, 1);

    public static DateTime ForLayout(CarouselLayout layout) =>
        layout == CarouselLayout.VerticalCharge ? T64CarouselDate : T72CarouselDate;

    public static DateTime For(LoaderKind kind) => kind switch
    {
        LoaderKind.Semi => SemiAutoloaderDate,
        LoaderKind.Revolver => RevolverDate,
        LoaderKind.Belt => BeltDate,
        _ => BustleDate
    };

    /// <summary>
    /// The exact native final-era sentinel makes everything available. Missing
    /// dates fail closed for a dated mechanism, but callers may choose to keep
    /// native behaviour instead.
    /// </summary>
    public static bool Allows(DateTime? vehicleDate, DateTime availableFrom) =>
        vehicleDate is { } date &&
        (date.Date == DateTime.MaxValue.Date || date.Date >= availableFrom.Date);
}
