namespace SprocketExpandedBallistics;

internal static class EraRejectionReport
{
    internal sealed record PathPart(int Entity,string? ComponentId,bool Active,MaterialBoundary[] Boundaries);
    internal static bool HousingBetweenElements(SprocketExpandedBallistics.Era.EraPartBindingCatalogue bindings,IReadOnlyList<PathPart> parts,int entryEntity,double start,double end)
    {
        if(!double.IsFinite(start)||!double.IsFinite(end)||end<=start||parts.Any(p=>p.ComponentId==null))return false;
        // Any active boundary in the registered trace makes a housing-only claim
        // unsafe, even if the other boundary was omitted by native.
        if(parts.Any(p=>p.Active||bindings.TryGetCassette(p.ComponentId,out _)))return false;
        var housing=parts.FirstOrDefault(p=>p.Entity==entryEntity&&bindings.TryGetMount(p.ComponentId,out _));
        return housing!=null&&ConfirmedMaterialIntervals.Pair(housing.Boundaries).Any(i=>i.End>start&&i.Start<end);
    }
    internal const string Spent="Explosive reactive element already initiated; no further reaction";
    internal const string Material="Explosive reactive element not initiated: material properties do not match the declared cassette";
    internal const string Housing="Module housing struck between explosive elements; no explosive element in the path";
    internal static string Reason(string reason)=>reason switch
    {
        "material"=>Material,
        "charge already spent"=>Spent,
        "native-tech-context"=>"Explosive reactive element not initiated: material technology could not be verified",
        "vehicle-era"=>"Explosive reactive element not initiated: vehicle date precedes the declared technology",
        "not-reached"=>"Explosive reactive element not initiated: no confirmed entry into the active material",
        "normal"=>"Explosive reactive element not initiated: incidence geometry could not be resolved",
        "unsupported-threat" or "projectile type unsupported"=>"Explosive reactive element not initiated: projectile does not satisfy the initiation criteria",
        "layout"=>"Explosive reactive element not initiated: explosive element geometry could not be resolved",
        "duplicate"=>"Explosive reactive element already evaluated along this projectile path; no further reaction",
        "rear"=>"Explosive reactive element not initiated: preceding explosive element intercepted the precursor",
        "sequential stack unavailable"=>"Explosive reactive element not initiated: sequential traversal of explosive layers could not be verified",
        "no complete ERA charge"=>"Explosive reactive element not initiated: no complete explosive layer in the material thickness",
        _ when reason.StartsWith("face ",StringComparison.Ordinal)=>"Explosive reactive element not initiated: material thickness does not match the declared explosive layer",
        _ when reason.StartsWith("below trigger calibre",StringComparison.Ordinal)=>"Explosive reactive element not initiated: projectile calibre is below the initiation threshold",
        _=>"Explosive reactive element not initiated: active material geometry could not be verified"
    };
    internal static bool IsReason(CombatReportEvent e)=>e.Kind is "eraSpent" or "eraUnavailable";
    internal static string Text(CombatReportEvent e)=>e.Kind=="eraSpent"?Spent:
        e.Text.StartsWith("Explosive reactive element",StringComparison.Ordinal)||e.Text==Housing?e.Text:Reason(e.Text);
    internal static string[] Lines(IEnumerable<CombatReportEvent> events)=>events.Where(IsReason).Select(Text).Distinct(StringComparer.Ordinal).ToArray();
}
