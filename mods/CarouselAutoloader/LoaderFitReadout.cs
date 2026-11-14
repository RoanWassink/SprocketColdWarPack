using System.Globalization;

namespace SprocketCarouselAutoloader;

internal static class LoaderFitReadout
{
    private static string Mm(double value) => Math.Ceiling(value).ToString("0",CultureInfo.CurrentCulture)+" mm";
    internal static string[] Carousel(CarouselSize size)
    {
        if(size.StoredProjectileMm<=0||size.RequiredDepthMm<=0||size.RequiredDiameterMm<=0)
            return new[]{"Ammunition unavailable"};
        var rows=new List<string>{"Shell length: "+Mm(size.StoredProjectileMm),"Charge length: "+Mm(size.StoredChargeMm),
            "Required depth: "+Mm(size.RequiredDepthMm),"Required diameter: "+Mm(size.RequiredDiameterMm)};
        if(size.DepthMm<size.RequiredDepthMm) rows.Add("Depth insufficient");
        if(size.DiameterMm<size.RequiredDiameterMm) rows.Add("Diameter insufficient");
        if(size.Capacity>0) rows.Add($"Shells: {size.Capacity}");
        else if(rows.Count==4) rows.Add("Ammunition does not fit");
        return rows.ToArray();
    }
    internal static string[] Magazine(double projectile,double charge,int capacity)
    {
        if(!double.IsFinite(projectile)||projectile<=0||!double.IsFinite(charge)||charge<0)
            return new[]{"Ammunition unavailable"};
        return new[]{"Shell length: "+Mm(projectile),"Charge length: "+Mm(charge),
            "Required length: "+Mm(projectile+charge),capacity>0?$"Shells: {capacity}":"Ammunition does not fit"};
    }
}

