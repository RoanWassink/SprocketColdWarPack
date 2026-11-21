namespace SprocketExpandedBallistics;
internal sealed record ShellEraContext(ShellNativeDate Date,string[] Names,ShellNativeDate[] Starts,int Index,IReadOnlySet<string> AvailableBehaviors,double? HeatFactor=null,IReadOnlySet<string>? AvailableTechnologies=null);
internal static class ShellEraPolicy
{
    /// <summary>
    /// Places the open-ended final-era sentinel 9999.12.31 in the last valid era
    /// even when vanilla GetEraIndex returns out-of-range without an external era patch.
    /// </summary>
    internal static int ResolveIndex(ShellNativeDate date,int eraCount,int nativeIndex)
        =>date==ShellNativeDate.Maximum&&eraCount>0?eraCount-1:nativeIndex;
    internal static bool Valid(ShellEraContext? c)
    {
        if(c==null||!c.Date.Valid||c.Names.Length!=c.Starts.Length||!ShellDatePolicy.ValidTimeline(c.Starts)||c.Index<0||c.Index>=c.Names.Length)return false;
        if(c.Names.Any(string.IsNullOrWhiteSpace)||c.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=c.Names.Length)return false;
        return c.Date==ShellNativeDate.Maximum ? c.Index==c.Names.Length-1 : c.Date>=c.Starts[c.Index]&&(c.Index==c.Starts.Length-1||c.Date<c.Starts[c.Index+1]);
    }
    internal static bool Allowed(ShellProfile p,ShellEraContext? c)
    {
        if(!Valid(c))return false;
        var technologies=c!.AvailableTechnologies??c.AvailableBehaviors.Select(b=>"expandedBallistics_"+b).ToHashSet(StringComparer.Ordinal);
        if(!ShellBalance.RequiredTechnologyIds(p).All(technologies.Contains))return false;
        // A date is the only additional availability floor. The exact final-era
        // sentinel 9999.12.31 is later than every finite profile date.
        return p.MinimumDate is not {} minimum||c.Date>=minimum;
    }
    internal static ShellProfile? Effective(ShellProfile? stored,ShellEraContext? c)=>stored!=null&&Allowed(stored,c)?stored:null;
}
