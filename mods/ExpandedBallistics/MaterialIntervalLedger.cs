using System.Globalization;
namespace SprocketExpandedBallistics;

internal enum GroupedBlockMode { PerInterval, Legacy }
internal static class GroupedProtection
{
    internal static GroupedBlockMode Mode { get; set; } = GroupedBlockMode.PerInterval;
    private static double scale=1;
    internal static double CompositeProtectionScale
    {
        get=>scale;
        set=>scale=double.IsFinite(value)&&value>=0?value:1;
    }
    internal static double Scale(double factor,bool composer)=>!composer||scale==1?factor:factor*scale;
}

// Metres on one persistent simulation-space ray, not distances from a child fragment's spawn.
internal sealed record MaterialInterval(int Entity,string Material,string Label,double StartM,double EndM,double Factor,double Angle,double? NativeRha=null);
internal sealed record PaidMaterialPiece(int Entity,string Material,string Label,double StartM,double EndM,double Factor,double Angle)
{
    internal double LengthMm=>(EndM-StartM)*1000;
    internal double RhaMm=>LengthMm*Factor;
    internal string Text=>string.Create(CultureInfo.InvariantCulture,
        $"{Label} [{Material}, entity {Entity}]: confirmed unpaid path {LengthMm:0.###} mm × {Factor:0.#####} = {RhaMm:0.###} mm RHA-eq; ray [{StartM:0.######}, {EndM:0.######}] m; incidence {Angle:0.###}°");
}
internal sealed record MaterialIntervalPlan(double StartM,double EndM,double Factor,PaidMaterialPiece[] Pieces,
    MaterialInterval[] Claims,bool Ambiguous)
{
    internal double RhaMm=>Pieces.Sum(p=>p.RhaMm);
    internal string Text=>"Confirmed material intervals: "+(Pieces.Length==0?"no unpaid material path; 0 mm RHA-eq":string.Join("; ",Pieces.Select(p=>p.Text)));
}

// Planning is read-only. Commit only after a verified native factor write.
// Claims include losing overlap members: the same ambiguous volume cannot be paid later under another entity.
internal sealed class MaterialIntervalLedger
{
    private readonly Dictionary<int,List<(double Start,double End)>> paid=new();
    private readonly List<(double Start,double End)> occupied=new();
    internal MaterialIntervalPlan? Plan(double start,double end,IEnumerable<MaterialInterval> source,double? nativeLengthM=null)
    {
        double length=nativeLengthM??(end-start);
        if(!double.IsFinite(start)||!double.IsFinite(end)||end<=start||!double.IsFinite(length)||length<=0)return null;
        var claims=source.Where(i=>double.IsFinite(i.StartM)&&double.IsFinite(i.EndM)&&double.IsFinite(i.Factor)&&i.Factor>=0&&double.IsFinite(i.Angle))
            .Select(i=>i with {StartM=Math.Max(start,i.StartM),EndM=Math.Min(end,i.EndM)}).Where(i=>i.EndM>i.StartM)
            .OrderBy(i=>i.StartM).ThenBy(i=>i.Entity).ToArray();
        var candidates=new List<MaterialInterval>();
        foreach(var claim in claims)
        {
            IEnumerable<(double Start,double End)> cuts=occupied;
            if(paid.TryGetValue(claim.Entity,out var own))cuts=cuts.Concat(own);
            foreach(var piece in Subtract(claim.StartM,claim.EndM,cuts))candidates.Add(claim with {StartM=piece.Start,EndM=piece.End});
        }
        var edges=candidates.SelectMany(i=>new[]{i.StartM,i.EndM}).Distinct().OrderBy(v=>v).ToArray();
        var pieces=new List<PaidMaterialPiece>();bool ambiguous=false;
        for(int i=1;i<edges.Length;i++)
        {
            double a=edges[i-1],b=edges[i];
            var active=candidates.Where(c=>c.StartM<=a&&c.EndM>=b).OrderByDescending(c=>c.Factor).ThenBy(c=>c.Entity).ThenBy(c=>c.Material,StringComparer.Ordinal).ToArray();
            if(active.Length==0)continue;
            ambiguous|=active.Select(c=>c.Entity).Distinct().Skip(1).Any();
            var winner=active[0];
            if(pieces.LastOrDefault() is {} previous&&previous.Entity==winner.Entity&&previous.Material==winner.Material&&previous.Factor==winner.Factor&&previous.Angle==winner.Angle&&previous.EndM==a)
                pieces[^1]=previous with {EndM=b};
            else pieces.Add(new(winner.Entity,winner.Material,winner.Label,a,b,winner.Factor,winner.Angle));
        }
        // Avoid multiply/divide rounding for a solitary complete native interval.
        double factor=pieces.Count==1&&pieces[0].StartM==start&&pieces[0].EndM==end&&Math.Abs(length-(end-start))<=Math.Max(1e-12,length*1e-12)
            ?pieces[0].Factor:pieces.Sum(p=>(p.EndM-p.StartM)*p.Factor)/length;
        return new(start,end,factor,pieces.ToArray(),claims,ambiguous);
    }
    internal void Commit(MaterialIntervalPlan plan)
    {
        foreach(var c in plan.Claims)
        {
            if(!paid.TryGetValue(c.Entity,out var own))paid[c.Entity]=own=new();
            Merge(own,c.StartM,c.EndM);Merge(occupied,c.StartM,c.EndM);
        }
    }
    private static IEnumerable<(double Start,double End)> Subtract(double start,double end,IEnumerable<(double Start,double End)> cuts)
    {
        double cursor=start;
        foreach(var cut in cuts.OrderBy(c=>c.Start))
        {
            if(cut.End<=cursor||cut.Start>=end)continue;
            if(cut.Start>cursor)yield return(cursor,Math.Min(end,cut.Start));
            cursor=Math.Max(cursor,cut.End);if(cursor>=end)yield break;
        }
        if(cursor<end)yield return(cursor,end);
    }
    private static void Merge(List<(double Start,double End)> list,double start,double end)
    {
        for(int i=list.Count-1;i>=0;i--)
            if(list[i].End>=start&&list[i].Start<=end){start=Math.Min(start,list[i].Start);end=Math.Max(end,list[i].End);list.RemoveAt(i);}
        list.Add((start,end));
    }
}

internal readonly record struct MaterialBoundary(double Distance,bool Enter,bool Exit,double Angle);
internal sealed class MaterialRayCoordinates
{
    private readonly System.Numerics.Vector3 origin,direction;
    private readonly double norm;
    internal MaterialRayCoordinates(System.Numerics.Vector3 origin,System.Numerics.Vector3 direction)
    {this.origin=origin;this.direction=direction;norm=Math.Sqrt((double)direction.X*direction.X+(double)direction.Y*direction.Y+(double)direction.Z*direction.Z);}
    internal double Project(System.Numerics.Vector3 spawn,System.Numerics.Vector3 current,double distance)
    {
        double offset=(((double)spawn.X-origin.X)*direction.X+((double)spawn.Y-origin.Y)*direction.Y+((double)spawn.Z-origin.Z)*direction.Z)/norm;
        double cosine=current==direction?1:((double)current.X*direction.X+(double)current.Y*direction.Y+(double)current.Z*direction.Z)/
            (norm*Math.Sqrt((double)current.X*current.X+(double)current.Y*current.Y+(double)current.Z*current.Z));
        return offset+distance*cosine;
    }
}
internal static class ConfirmedMaterialIntervals
{
    // Pair actual own Enter/Exit hits. Multiple disjoint volumes remain disjoint; no min/max hull or thickness/cos inference.
    internal static (double Start,double End,double Angle)[] Pair(IEnumerable<MaterialBoundary> boundaries)
    {
        var result=new List<(double,double,double)>();MaterialBoundary? entry=null;
        foreach(var hit in boundaries.Where(h=>double.IsFinite(h.Distance)).OrderBy(h=>h.Distance).ThenBy(h=>h.Enter?1:0))
        {
            if(hit.Exit&&entry is {} first){if(hit.Distance>first.Distance&&double.IsFinite(first.Angle))result.Add((first.Distance,hit.Distance,first.Angle));entry=null;}
            if(hit.Enter&&!hit.Exit)entry??=hit;
        }
        return result.ToArray();
    }
}
