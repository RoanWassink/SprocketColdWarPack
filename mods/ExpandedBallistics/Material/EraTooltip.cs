using System.Globalization;

namespace SprocketExpandedBallistics.Materials;

// Presentation only: loss is additional remaining-penetration loss, never RHA thickness.
internal static class EraTooltip
{
    internal readonly record struct Point(double Angle, double Weight);
    private static string N(double n) => n.ToString("0.#", CultureInfo.InvariantCulture);
    private static (double Low,double High,double Reference) Peak(IReadOnlyList<Point> curve)
    {
        if(curve.Count==0)return (0,0,0);
        var max=curve.Max(p=>p.Weight);
        var peak=curve.Where(p=>p.Weight==max).ToArray();
        var low=peak[0].Angle;var high=peak[^1].Angle;
        return (low,high,Math.Clamp(60,low,high));
    }
    private static double Weight(IReadOnlyList<Point> curve,double angle)
    {
        if(curve.Count==0||angle<curve[0].Angle||angle>curve[^1].Angle)return 0;
        for(var i=1;i<curve.Count;i++)if(angle<=curve[i].Angle)
            return curve[i-1].Weight+(curve[i].Weight-curve[i-1].Weight)*(angle-curve[i-1].Angle)/(curve[i].Angle-curve[i-1].Angle);
        return curve[^1].Weight;
    }
    internal static string Build(string name,int? plates,int layers,IReadOnlyList<Point> kinetic,IReadOnlyList<Point> chemical,
        double kineticReferenceLoss,double heatLoss,double kineticCap,double heatCap,double? referenceLD,
        string usedOn,bool enabled=true,bool rpgOnly=false)
    {
        var lines=new List<string>{name};
        if(plates is >0)lines.Add("Plates: "+plates+(layers>1?" | Layers: "+layers:""));
        if(!enabled){lines.Add("Extra protection: disabled");return string.Join("\n",lines);}
        var k=Peak(kinetic);var h=Peak(chemical);
        lines.Add(kineticReferenceLoss>0 ? "Effective angles: "+N(k.Low)+"-"+N(k.High)+"° (best APFSDS)" : "Effective angles: "+N(h.Low)+"-"+N(h.High)+"° (best HEAT)");
        string Loss(double loss,double cap,double weight)=>N(100*Math.Min(cap,1-Math.Pow(1-Math.Clamp(loss*weight,0,1),Math.Max(1,layers))))+"% less pen.";
        lines.Add(kineticReferenceLoss>0?"Kinetic (APFSDS) at "+N(k.Reference)+"°: "+Loss(kineticReferenceLoss,kineticCap,Weight(kinetic,k.Reference)):"Kinetic (APFSDS): no added protection");
        lines.Add("Chemical ("+(rpgOnly?"RPG HEAT":"HEAT")+") at "+N(h.Reference)+"°: "+Loss(heatLoss,heatCap,Weight(chemical,h.Reference)));
        lines.Add("Tandem: no guaranteed protection");
        lines.Add(referenceLD.HasValue?"Extra loss, reference L/D "+N(referenceLD.Value)+(layers>1?"; all layers intact":""):"Values: extra remaining-penetration loss");
        if(!string.IsNullOrEmpty(usedOn))lines.Add("Used on: "+usedOn);
        return string.Join("\n",lines);
    }
    internal static string Application(string id) => id switch
    {
        "reliktActive" or "reliktCassette" or "experimentalReliktCassette" => "T-90M, T-80BVM (family)",
        "reliktTurretActive" => "T-80BVM turret (family)",
        "kontakt5Active" or "kontakt5Cassette" or "experimentalKontakt5Cassette" => "T-72B, T-90 (family)",
        "nizhLegacyActive" or "nizh1MActive" or "nizhCassette" => "Bulat (family)",
        "duplet2MActive" or "duplet2MHullActive" => "Oplot (family)",
        _ => ""
    };
}
