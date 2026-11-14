using System.Globalization;
namespace SprocketExpandedBallistics;

/// <summary>0.22.21: the protection table labels and the shipped defaults (0.22.28: the cassette report chain is gone with the chain itself).</summary>
internal static class Fix02221Tests
{
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var assert=check;check=(ok,text)=>{assert(ok,"fix02221: "+text);count++;};
        Labels(check);Defaults(check);
        Console.WriteLine($"0.22.21 checks PASS: {count} checks.");
    }
    private static void Labels(Action<bool,string> check)
    {
        check(ProtectionTable.Metrics.Length==3&&ProtectionTable.Metrics[0]=="mm RHA","three metric rows, the first is the absolute mm RHA");
        check(ProtectionTable.Metrics[1]=="\u00D7 RHA (same thickness)"&&ProtectionTable.Metrics[2]=="\u00D7 RHA (same weight)","clear names for the two factors");
        check(!ProtectionTable.Metrics.Any(m=>m.Contains("LOS",StringComparison.Ordinal)||m.StartsWith("x ",StringComparison.Ordinal)),"the old 'x same LOS thickness' wording is gone");
        check(ProtectionTable.Legend=="Game uses the mean of with/against tilt \u00B7 1.00 = solid RHA \u00B7 higher is better","legend identifies runtime mean and RHA reference");
        check(ProtectionTable.BasisText(430)=="vs 430 mm RHA"&&ProtectionTable.BasisText(318.6)=="vs 319 mm RHA"&&ProtectionTable.BasisText(null)=="","basis text");
        // Basis: same thickness = depth / cos, same weight = kg/m2 / density / cos.
        var thickness=CassetteProtectionComparison.Reference(215,500,62,1);
        check(thickness is {} t&&Math.Abs(t-215/Math.Cos(62*Math.PI/180))<1e-9,"same-thickness basis is the line-of-sight thickness");
        var weight=CassetteProtectionComparison.Reference(215,500,60,2);
        check(weight is {} w&&Math.Abs(w-500*1000/7850.0/Math.Cos(60*Math.PI/180))<1e-9,"same-weight basis is the equal-mass RHA path");
        check(CassetteProtectionComparison.Reference(215,500,60,0)==null&&CassetteProtectionComparison.Reference(215,500,90,1)==null,"the absolute row and an impossible angle have no basis");
        var value=CassetteProtectionComparison.Value(386,215,500,60,1);
        check(value is {} v&&Math.Abs(v-386/(215/Math.Cos(60*Math.PI/180)))<1e-9,"the factor still divides by the same basis");
    }
    private static void Defaults(Action<bool,string> check)
    {
        var defaults=ShellProfiles.Parse(ReleaseProfiles.Defaults());
        check(defaults.Single(p=>p.Id=="atgm_topattack_test").TandemPrecursorPenetrationMm==200,"shipped default: top-attack test ATGM tandem precursor is 200 mm");
        check(defaults.Single(p=>p.Id=="atgm_konkurs_9m113").Wire?.Transport=="wire","shipped default: 9M113 Konkurs guides by wire");
    }
}
