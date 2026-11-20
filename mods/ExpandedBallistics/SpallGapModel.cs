namespace SprocketExpandedBallistics;

// 0.22.22: fragments in thin gaps between armour layers.
// Source of the gap: the native intersection list of the very path the fragment flies (the hits array that GetNextMaterialBlock reads, the same
// source the chemical gap ledger uses for its air gap). For a solid block that ends at hit index "exit", the gap is the distance from that exit
// to the next Enter hit of an object with a solid material (materialIndex >= 0), in mm; no later solid object means "no solid ahead" (infinity).
// Pure rules only; the hooks are in RuntimeSpall.
internal static class SpallGapModel
{
    internal const double DefaultMinSpallGapMm=50;
    // Only the chemical and sub-calibre cone bursts follow the rule (HEAT/HESH jets and APFSDS cones). Vanilla AP has no impact context and never gets here.
    internal static bool Applies(string? behavior)=>behavior is "heat" or "hesh" or "apfsds";
    // Distance in mm from the exit of a block to the next solid Enter hit. distanceM is in metres along the path, as the native hits are.
    // Scans the full native buffer because native StructureIntersection arrays are not guaranteed to be sorted by distance.
    internal static double NextSolidGapMm(IReadOnlyList<float> distanceM,IReadOnlyList<bool> enter,IReadOnlyList<bool> solid,int exitIndex,int entryIndex=-1)
    {
        if(exitIndex<0||exitIndex>=distanceM.Count||!float.IsFinite(distanceM[exitIndex]))return double.PositiveInfinity;
        var exitDist=distanceM[exitIndex];
        var best=double.PositiveInfinity;
        var n=Math.Min(distanceM.Count,Math.Min(enter.Count,solid.Count));
        for(int k=0;k<n;k++)
        {
            if(k==exitIndex||k==entryIndex||!enter[k]||!solid[k]||!float.IsFinite(distanceM[k]))continue;
            var d=distanceM[k];
            if(d<exitDist||(d==exitDist&&entryIndex<0&&k<=exitIndex))continue;
            var gapMm=Math.Max(0,(d-exitDist)*1000.0);
            if(gapMm<best)best=gapMm;
        }
        return best;
    }
    // A burst is suppressed when a solid block follows closer than the minimum gap. 0 or less turns the rule off; an unknown gap never suppresses.
    internal static bool SuppressBurst(double minGapMm,double gapMm)=>minGapMm>0&&double.IsFinite(minGapMm)&&!double.IsNaN(gapMm)&&gapMm<minGapMm;
    // A ricocheting fragment dies when the next solid block along its new direction is closer than thinGapKillMm (0 = off).
    internal static bool KillThinGap(double thinGapKillMm,double gapMm)=>thinGapKillMm>0&&double.IsFinite(thinGapKillMm)&&!double.IsNaN(gapMm)&&gapMm<thinGapKillMm;
}
internal sealed class SpallGapLedger
{
    private readonly record struct Entry(IntPtr Simulation,int Fragment,double ExitM,double GapMm);
    private readonly List<Entry> entries=new();
    internal int Count=>entries.Count;
    internal void Clear()=>entries.Clear();
    internal void Record(IntPtr simulation,int fragment,double exitM,double gapMm)
    {
        if(!double.IsFinite(exitM)||double.IsNaN(gapMm))return;
        if(entries.Count>=512)entries.RemoveRange(0,256);
        entries.Add(new(simulation,fragment,exitM,gapMm));
    }
    // The gap of the block the fragment left at spawnM (metres along its path). When spawnM is unknown or no recorded exit lies within the tolerance, the
    // latest block of that fragment is used. Returns null when nothing was recorded for the fragment.
    internal double? Match(IntPtr simulation,int fragment,double? spawnM,out bool byDistance,double toleranceM=.02)
    {
        byDistance=false;Entry? best=null,latest=null;
        foreach(var e in entries)
        {
            if(e.Simulation!=simulation||e.Fragment!=fragment)continue;
            latest=e;
            if(spawnM is {} d&&double.IsFinite(d)&&Math.Abs(e.ExitM-d)<=toleranceM&&(best==null||Math.Abs(e.ExitM-d)<=Math.Abs(best.Value.ExitM-d)))best=e;
        }
        if(best!=null){byDistance=true;return best.Value.GapMm;}
        return latest?.GapMm;
    }
}
