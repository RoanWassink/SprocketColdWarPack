namespace SprocketExpandedBallistics;

// Pure acquisition rules, separate from native ray queries and flight integration.
internal static class TopAttackLockPolicy
{
    internal const double RangeM=4000, LifetimeSeconds=30;
    internal static bool ValidHit(double distance,double x,double y,double z)=>double.IsFinite(distance)&&distance>.01&&distance<=RangeM&&double.IsFinite(x)&&double.IsFinite(y)&&double.IsFinite(z);
    internal static bool SameTarget(IntPtr previous,IntPtr next,double localDistance)=>previous!=IntPtr.Zero&&previous==next&&double.IsFinite(localDistance)&&localDistance<=8;
    internal static bool Fresh(double now,double last)=>double.IsFinite(now)&&double.IsFinite(last)&&now>=last&&now-last<=LifetimeSeconds;
    internal static IReadOnlyDictionary<string,string> LegacyConfig(string text)
    {
        var values=new Dictionary<string,string>(StringComparer.Ordinal);var section="";
        foreach(var raw in text.Split('\n'))
        {
            var line=raw.Trim();if(line.StartsWith("[",StringComparison.Ordinal)&&line.EndsWith("]",StringComparison.Ordinal)){section=line[1..^1];continue;}
            if(section!="ATGM Experimental"||line.StartsWith("#",StringComparison.Ordinal)||line.StartsWith(";",StringComparison.Ordinal))continue;
            var split=line.IndexOf('=');if(split<0)continue;var name=line[..split].Trim();
            if(name is "Enabled" or "DiagnosticLogging" or "TopAttackLockSeconds")values[name]=line[(split+1)..].Trim();
        }
        return values;
    }
    internal static bool Supersede(string oldMode)=>oldMode!="topattack";
}
