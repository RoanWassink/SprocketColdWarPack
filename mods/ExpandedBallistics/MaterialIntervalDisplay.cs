using System.Globalization;
namespace SprocketExpandedBallistics;

// Presentation only. Never replace diagnostic Text or the persisted PaidIntervals.
internal static class MaterialIntervalDisplay
{
    internal static bool Is(CombatReportEvent e)=>e.PaidIntervals!=null||e.Text.StartsWith("Confirmed material intervals:",StringComparison.Ordinal);
    internal static string Mm(double x)=>x.ToString(x<10?"0.0":"0",CultureInfo.InvariantCulture);
    internal static string Name(PaidMaterialPiece p)
    {
        if(p.Material=="rha")return "Rolled homogeneous armour";
        if(p.Material=="highHardnessSteel")return "High-hardness steel";
        if(p.Material=="sheetMetal")return "Sheet metal";
        if(p.Material.StartsWith("native:",StringComparison.Ordinal))
        {
            var label=System.Text.RegularExpressions.Regex.Replace(CombatReportLanguage.Module(p.Label),@"\s*#\d+", "");
            return label.StartsWith("Crew",StringComparison.OrdinalIgnoreCase)?"Crew tissue":label.Contains("native:",StringComparison.OrdinalIgnoreCase)?"Internal material":label;
        }
        return CombatReportLanguage.Material(p.Label==p.Material?EraDisplayName.Material(p.Material):p.Label,p.Material);
    }
    internal static string Piece(PaidMaterialPiece p,bool overlap=false)=>
        (p.Factor==1?$"{Name(p)}: {Mm(p.LengthMm)} mm RHA":
        $"{Name(p)}: {Mm(p.LengthMm)} mm × {p.Factor.ToString("0.##",CultureInfo.InvariantCulture)} = {Mm(p.RhaMm)} mm RHA-eq")+
        (overlap?" (overlap counted once)":"");
    private static bool Grouped(CombatReportEvent e,CombatLayerInfo? layer)=>layer?.Parts.Count>1&&e.PaidIntervals is {} paid&&paid.Select(p=>p.Entity).Distinct().Count()>1;
    internal static string[] Sublayers(CombatReportEvent e,CombatLayerInfo? layer,IEnumerable<CombatReportEvent> events)=>Grouped(e,layer)?Visible(e,events):Array.Empty<string>();
    private static string[] Visible(CombatReportEvent e,IEnumerable<CombatReportEvent> events)
    {
        var overlap=events.Where(x=>x.Kind=="materialIntervalOverlap"&&x.HitId==e.HitId&&x.Stage==e.Stage).ToArray();
        return (e.PaidIntervals??Array.Empty<PaidMaterialPiece>()).Where(p=>p.RhaMm>=.5)
            .Select(p=>Piece(p,overlap.Any(x=>x.RelatedEntity==p.Entity))).ToArray();
    }
    internal static string Reaction(CombatReportEvent e,CombatLayerInfo? layer,IEnumerable<CombatReportEvent> events)
    {
        if(e.PaidIntervals is not {Length:>0} paid)return "";
        if(Grouped(e,layer))return string.Join("\n",Visible(e,events));
        // Ordinary plate response: plain RHA needs no duplicate line beside entry/exit penetration.
        // A multi-part group with only one paid member likewise needs no per-member breakdown.
        if(layer?.Parts.Count!=1)return "";
        return string.Join("\n",paid.Where(p=>p.RhaMm>=.5&&p.Material!="rha"&&!p.Material.StartsWith("native:",StringComparison.Ordinal)).Select(p=>Piece(p)));
    }
}
