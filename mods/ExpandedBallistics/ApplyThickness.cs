using System.Globalization;
namespace SprocketExpandedBallistics;

// Apply-only view state. The recipe and its factor table never change; native mesh loops own face thickness.
internal static class ApplyThickness
{
    internal static double Clamp(double depth,double mm)
    {
        if(!double.IsFinite(depth)||depth<=0||!double.IsFinite(mm))throw new ArgumentOutOfRangeException(nameof(mm));
        return Math.Clamp(mm,Math.Max(1,.1*depth),Math.Max(1,3*depth));
    }
    internal static double FromPercent(double depth,double percent)=>Clamp(depth,percent==100?depth:depth*(percent/100));
    internal static bool TryParse(string text,double depth,out double mm)
    {
        mm=0;var s=text.Trim();bool percent=s.EndsWith('%');
        s=percent?s[..^1].Trim():s.EndsWith("mm",StringComparison.OrdinalIgnoreCase)?s[..^2].Trim():s;
        if(!double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||depth<=0)return false;
        mm=percent?FromPercent(depth,value):Clamp(depth,value);return true;
    }
    internal static string Text(double depth,double mm)=>string.Create(CultureInfo.InvariantCulture,$"{mm:0.##} mm ({mm/depth*100:0.##} %)");
    internal static CassetteLayer[] Layers(IReadOnlyList<CassetteLayer> layers,double scale)=>scale==1?layers.ToArray():layers.Select(l=>l with{ThicknessMm=l.ThicknessMm*scale}).ToArray();
}
