using System.Globalization;
using System.Numerics;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.16 acceptance: a composite plate that native skips behind an air block, an
/// overlap that cuts the cassette block short, the effective cassette loss after the
/// native speed ratio, and the report texts for both reasons.
/// </summary>
internal static class Fix02216Tests
{
    internal static void Run(Action<bool,string> check)
    {
        DecisionTable(check);
        NativeLaw(check);
        
        Overlap(check);
        
        Reasons(check);
        ReportRows(check);
        Console.WriteLine("0.22.16 checks PASS");
    }

    private static void DecisionTable(Action<bool,string> check)
    {
        check(CassetteImpactPath.AirGap(false,false,false,false)==CassetteImpactPath.AirGapOutcome.None,"fix02216: no pending plate: nothing to pay");
        check(CassetteImpactPath.AirGap(true,false,true,true)==CassetteImpactPath.AirGapOutcome.SamePlate,"fix02216: next solid block is the remembered plate: normal path");
        check(CassetteImpactPath.AirGap(true,false,true,false)==CassetteImpactPath.AirGapOutcome.Skipped,"fix02216: next solid block is another object: native skipped the plate");
        check(CassetteImpactPath.AirGap(true,false,false,false)==CassetteImpactPath.AirGapOutcome.Ended,"fix02216: lineage ends without another block: report a reason");
        check(CassetteImpactPath.AirGap(true,true,true,false)==CassetteImpactPath.AirGapOutcome.Claimed,"fix02216: an already claimed plate is never paid twice");
    }

    private static void NativeLaw(Action<bool,string> check)
    {
        // The four native rows measured in 0.22.15 (MAP table).
        var rows=new[]{(775.0,165.3,652.9),(483.1,100.1,409.3),(292.1,104.3,213.0),(212.2,50.2,175.0)};
        foreach(var (before,resistance,expected) in rows)
            check(Math.Abs(CassetteImpactPath.NativePassive(before,resistance)-expected)<1,"fix02216: native passive law keeps "+before+" - "+resistance+" = "+expected+" mm");
        check(CassetteImpactPath.NativePassive(500,0)==500&&CassetteImpactPath.NativePassive(500,-3)==500,"fix02216: no resistance leaves the budget untouched");
        check(CassetteImpactPath.NativePassive(500,500)==0&&CassetteImpactPath.NativePassive(500,900)==0,"fix02216: resistance at or above the budget stops it");
        check(CassetteImpactPath.NativePassive(0,100)==0&&CassetteImpactPath.NativePassive(double.NaN,100)==0,"fix02216: invalid budget gives no passive result");
    }

    private static void Overlap(Action<bool,string> check)
    {
        var face=250d;var cosine=Math.Cos(62*Math.PI/180);var span=267.108;var plateRha=.67;var otherRha=.1;
        var missing=CassetteImpactPath.OverlapMissingMm(face,cosine,span,plateRha,otherRha);
        var full=CassetteImpactPath.OverlapMissingMm(face,cosine,span,plateRha,0);
        var remaining=face/cosine-span;
        check(Math.Abs(missing-151.3)<1,"fix02216: missing overlap resistance is the uncharged plate path at plate RHA minus the other component ("+missing.ToString("0.0",CultureInfo.InvariantCulture)+" mm)");
        check(Math.Abs((full-missing)-remaining*otherRha)<1e-6,"fix02216: what native already charged as the other component is not counted twice");
        check(CassetteImpactPath.OverlapMissingMm(face,cosine,span,plateRha,2*plateRha)==0,"fix02216: a component at least as resistant as the plate leaves nothing to add");
        check(CassetteImpactPath.OverlapMissingMm(face,cosine,1000,plateRha,otherRha)==0,"fix02216: a block covering the whole plate path leaves nothing to add");
        check(CassetteImpactPath.OverlapMissingMm(0,cosine,span,plateRha,otherRha)==0,"fix02216: no drawn face means no overlap compensation");
    }

    private static void Reasons(Action<bool,string> check)
    {
        var skip=CassetteImpactPath.SkipReason(0.005);
        check(skip=="native skipped plate (air gap 0.005 mm); cassette and passive resistance applied by the mod","fix02216: skip reason text");
        var overlap=CassetteImpactPath.OverlapReason("fuelTank #376",267.108,195.9,.57,151.25,82.6);
        check(overlap=="overlapped by fuelTank #376 after 267 mm: native did not charge 196 mm of plate path; 196 mm \u00D7 0.57 \u0394RHA = L 151.3 mm RHA; actual loss \u221282.6 mm by the native law","fix02216: overlap reason text (0.22.17 wording)");
    }

    private static void ReportRows(Action<bool,string> check)
    {
        var housing=new CombatLayerPart(390,"Duplet panel (turret)","rha","RHA",null,null,false);
        var plate=new CombatLayerPart(296,"Composite armour plate","ebCassette_user_nera_v1","NERA cassette (own)",null,null,false);
        var airLayer=new CombatLayerInfo("air",new[]{housing,plate},true,0.005);
        var skip=CassetteImpactPath.SkipReason(0.005);
        var shot=new CombatReportShot(5,"Outgoing","HEAT / HEAT-FS",593.36,new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",292.086,HitId:"air",Layer:airLayer),
            new CombatReportEvent("materialReaction","","Cassette NERA cassette (own): HEAT \u221230.8 mm; front, t = 153 mm, \u03B8 = 40.3\u00B0",292.086,261.2,"air",RelatedEntity:296),
            new CombatReportEvent("plateResidual","","Skipped composite plate passive resistance applied from the drawn front face.",261.2,236.6,"air",RelatedEntity:296),
            new CombatReportEvent("materialNote","",skip,HitId:"air",RelatedEntity:296)
        });
        var views=CombatHitProjection.Build(CombatReportTruth.Normalize(shot));
        check(views.Length==1,"fix02216: the skipped-plate air row is visible with its reaction and note");
        check(views[0].Name=="Air gap 0.005 mm before Composite armour plate","fix02216: air block is named air gap, not a plate layer: "+views[0].Name);
        check(views[0].Reaction.StartsWith("Cassette NERA",StringComparison.Ordinal)&&views[0].Reaction.Contains("HEAT",StringComparison.Ordinal)&&views[0].Reaction.Contains("air gap 0.005 mm",StringComparison.Ordinal)&&views[0].Reaction.Contains(" \u00B7 ",StringComparison.Ordinal),
            "fix02216: air row reaction is the cassette text plus the skip reason: "+views[0].Reaction);
        var report=CombatReportView.Build(new[]{shot},1);
        check(report.Count==1&&report[0].Layers.Length==1,"fix02216: the air row reaches the Kit report");
        var row=report[0].Layers[0];
        check(row.Name.StartsWith("Air gap 0.005 mm before",StringComparison.Ordinal)&&row.Material=="air gap"&&row.Thickness=="—"&&row.Angle=="—",
            "fix02216: air row has no plate thickness or angle and reads material air gap ("+row.Name+")");
        check(row.Reaction.Contains("plate beyond an air gap",StringComparison.Ordinal),"fix02216: air row carries the reason in the report (0.22.41 wording)");

        var normalPlate=new CombatLayerPart(313,"Composite armour plate","ebCassette_t80u_hull","T-80U glacis",250,62,true);
        var blockLayer=new CombatLayerInfo("block",new[]{normalPlate},false);
        var overlap=CassetteImpactPath.OverlapReason("fuelTank #376",267.108,195.9,.57,151.25,82.6);
        var overlapShot=new CombatReportShot(4,"Outgoing","HEAT / HEAT-FS",593.38,new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",652.04,HitId:"block",Layer:blockLayer),
            new CombatReportEvent("materialReaction","","Cassette T-80U glacis: HEAT \u2212104.4 mm; front, t = 250 mm, \u03B8 = 62\u00B0",652.04,547.63,"block",RelatedEntity:313),
            new CombatReportEvent("materialNote","",overlap,HitId:"block",RelatedEntity:376)
        });
        var overlapViews=CombatHitProjection.Build(CombatReportTruth.Normalize(overlapShot));
        check(overlapViews.Length==1&&overlapViews[0].Reaction.Contains("overlapped by fuelTank #376 after 267 mm",StringComparison.Ordinal)&&overlapViews[0].Reaction.Contains(" \u00B7 ",StringComparison.Ordinal),
            "fix02216: overlap reason is appended to the cassette reaction: "+overlapViews[0].Reaction);
    }
}
