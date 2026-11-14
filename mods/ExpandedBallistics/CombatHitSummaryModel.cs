using System.Globalization;
namespace SprocketExpandedBallistics;

internal sealed record CombatHitSummary(string Header,string Detail,string Colour)
{ internal string RichText=>CombatReportPresentation.Colour(Colour,Header)+"\n"+CombatReportPresentation.Colour(Colour,Detail); }
internal static class CombatHitSummaryModel
{
    private static int Priority(CombatReportEvent e)=>e.Kind switch
    {"eraResidual" or "lightEra" or "heavyEra"=>5,"materialReaction" or "materialStop"=>4,"nativeStop"=>3,"plateResidual"=>2,
     "outcome" when e.Text.StartsWith("Stopped",StringComparison.OrdinalIgnoreCase)=>3,
     "outcome" when e.Text.StartsWith("Deflected",StringComparison.OrdinalIgnoreCase)=>1,_=>0};
    internal static (string Label,string Colour) Category(CombatHitView hit)
    {
        var category=Category(hit.Details);
        return category.Label.Length>0?category:CombatReportPresentation.Category(new(hit.Status.StartsWith("Stopped",StringComparison.Ordinal)?"nativeStop":hit.Status=="Deflected"?"outcome":"plateResidual","",hit.Status));
    }
    internal static (string Label,string Colour) Category(IEnumerable<CombatReportEvent> events)
    {
        CombatReportEvent? best=null;int priority=0;
        foreach(var e in events)if(Priority(e)>priority){best=e;priority=Priority(e);}
        return best==null?("","#A5ADB5"):CombatReportPresentation.Category(best);
    }
    internal static string Fit(string text,int columns)
    {
        columns=Math.Max(1,columns);text=text.Replace('\n',' ').Replace('\r',' ');
        return text.Length<=columns?text:columns==1?"\u2026":text[..(columns-1)]+"\u2026";
    }
    private static double Reduction(CombatReportEvent e)=>e.BeforeMm is {} before&&e.AfterMm is {} after?Math.Max(0,before-after):0;
    private static string N(double? v)=>v?.ToString("0",CultureInfo.InvariantCulture)??"?";
    internal static CombatHitSummary Build(CombatReportShot shot,int columns=82)
    {
        var hits=CombatHitProjection.Build(shot);
        var last=hits.LastOrDefault(h=>h.Stage!="Precursor"&&h.Status!="Diagnostic only")??hits.LastOrDefault();
        string outcome=last?.Status.StartsWith("Stopped",StringComparison.Ordinal)==true?"GESTOPT":last?.Status=="Deflected"?"AFGEKETST":last?.Status=="Passed"?"DOOR":"ONBEKEND";
        double? after=last?.After;if(outcome=="GESTOPT")after=0;
        var before=shot.InitialPenetrationMm??hits.FirstOrDefault()?.Before;
        var header=$"#{shot.Number} {Fit(shot.Profile,24)} {N(before)} \u2192 {N(after)} mm {outcome}";
        double era=0,material=0,steel=0;string eraName="",materialName="";
        // Projection can associate one backing reaction with both support and
        // insert. Aggregate the authoritative event list once, not both views.
        foreach(var e in shot.Events)
        {
            if(e.Kind is "eraResidual"){era+=Reduction(e);}
            if(e.Kind is "lightEra" or "heavyEra")eraName=Fit(e.Text,22);
            if(e.Kind is "materialReaction" or "materialStop")
            {material+=Reduction(e);var name=e.Text.Split(':',';')[0];materialName=Fit(name,25);}
        }
        var measuredBlocks=new HashSet<string>(StringComparer.Ordinal);
        for(int i=shot.Events.Count-1;i>=0;i--)
        {
            // Block exits include extra reactions; subtract only measured extra
            // events belonging to that exact block. Unmeasured stops stay '?'.
            var residual=shot.Events[i];
            if(residual.Kind=="plateResidual"&&(residual.HitId==null||measuredBlocks.Add(residual.HitId)))
            {
                double extra=residual.HitId==null||residual.Text.Contains("Skipped composite plate",StringComparison.Ordinal)?0:
                    shot.Events.Where(e=>e.HitId==residual.HitId&&e.Kind is "eraResidual" or "materialReaction" or "materialStop").Sum(Reduction);
                steel+=Math.Max(0,Reduction(residual)-extra);
            }
        }
        var parts=new List<string>();
        if(era>0||eraName.Length>0)parts.Add($"ERA{(eraName.Length>0?" "+eraName:"")} \u2212{era:0}");
        if(material>0||materialName.Length>0)parts.Add($"{(materialName.Length>0?materialName:"materiaal")} \u2212{material:0}");
        if(steel>0)parts.Add($"staal \u2212{steel:0}");
        if(outcome=="GESTOPT")
        {
            var armour=last?.Layer?.Parts.Where(p=>p.Armour).ToArray();
            parts.Add(armour?.Length==1?$"gestopt in Plaat #{armour[0].Entity}":"gestopt in pantserblok (plaat onbekend)");
        }
        else if(outcome=="AFGEKETST")parts.Add("afgeketst");
        if(parts.Count==0)parts.Add("geen gemeten laagverlies");
        var category=Category(shot.Events);
        return new(Fit(header,columns),Fit(string.Join(" \u00b7 ",parts),columns),category.Colour);
    }
}
