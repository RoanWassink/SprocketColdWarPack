using System.Numerics;
using System.Text.Json;
namespace SprocketExpandedBallistics;
internal static partial class FollowWallCoverage
{
    // Deterministic 12 mm lattice, fixed fractional cell offset, seed 0; unions counted once.
    internal const float Step=.012f;
    private static readonly Dictionary<string,Dictionary<int,Vector3[]>> samples=new();
    private sealed class Tet
    {
        readonly Vector3 min,max;readonly (Vector3 N,float D)[] planes;
        internal Tet(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var v=new[]{a,b,c,d};min=v.Aggregate(Vector3.Min);max=v.Aggregate(Vector3.Max);
            planes=new (Vector3,float)[4];
            for(int i=0;i<4;i++){var p=v.Where((_,j)=>i!=j).ToArray();var n=Vector3.Cross(p[1]-p[0],p[2]-p[0]);float t=Vector3.Dot(n,v[i]-p[0]);if(t<0)n=-n;planes[i]=(n,Vector3.Dot(n,p[0]));}
        }
        internal Vector3 Min=>min;internal Vector3 Max=>max;
        internal (double A,double B)? RayInterval(Vector3 origin,Vector3 direction)
        {
            double lo=double.NegativeInfinity,hi=double.PositiveInfinity;
            foreach(var (n,d) in planes){double speed=Vector3.Dot(n,direction),offset=Vector3.Dot(n,origin)-d;if(Math.Abs(speed)<1e-16){if(offset< -1e-10)return null;continue;}double t=-offset/speed;if(speed>0)lo=Math.Max(lo,t);else hi=Math.Min(hi,t);if(hi<lo)return null;}
            return double.IsFinite(lo)&&double.IsFinite(hi)&&hi>lo?(lo,hi):null;
        }
        internal bool Has(Vector3 p)=>p.X>=min.X&&p.X<=max.X&&p.Y>=min.Y&&p.Y<=max.Y&&p.Z>=min.Z&&p.Z<=max.Z&&planes.All(x=>Vector3.Dot(x.N,p)>=x.D-1e-10f);
    }
    private static Tet[] Prisms(IEnumerable<(Vector3[] Front,Vector3[] Back)> faces)
    {
        var r=new List<Tet>();foreach(var (a,b) in faces)for(int i=1;i+1<a.Length;i++)
        {r.Add(new(a[0],a[i],a[i+1],b[i+1]));r.Add(new(a[0],a[i],b[i],b[i+1]));r.Add(new(a[0],b[0],b[i],b[i+1]));}return r.ToArray();
    }
    internal sealed record CoverageResult(int cheek,long inside,long covered,long solid,double coverage,double solidFraction,double gridVolumeM3);
    internal static CoverageResult[] Measure(string original,LayerConversion r)
    {
        if(!samples.TryGetValue(original,out var sample))
        {
            var shape=LayerFaces.ClosedSurface(original);var tris=shape.SelectMany(p=>Enumerable.Range(1,p.Length-2).Select(i=>new FillTriangle(p[0],p[i],p[i+1]))).ToArray();sample=new();
            foreach(int side in new[]{-1,1})
            {
                var points=shape.Where(p=>p[0].X*side>0).SelectMany(p=>p).ToArray();var min=points.Aggregate(Vector3.Min);var max=points.Aggregate(Vector3.Max);var inside=new List<Vector3>();
                for(int x=0;min.X+(x+.37f)*Step<max.X;x++)for(int y=0;min.Y+(y+.43f)*Step<max.Y;y++)for(int z=0;min.Z+(z+.61f)*Step<max.Z;z++)
                {
                    var point=min+new Vector3((x+.37f)*Step,(y+.43f)*Step,(z+.61f)*Step);int hits=0;
                    foreach(var t in tris)if(ComposerFill.Ray(point,Vector3.UnitX,new[]{t},10) is {} hit&&hit>1e-7)hits++;
                    if(hits%2==1)inside.Add(point);
                }
                sample[side]=inside.ToArray();
            }
            samples[original]=sample;
        }
        var envelopes=Prisms(r.Sources.Where(s=>s.Converted).Select(s=>(s.Front,s.Back)));
        var solids=Prisms(r.Faces.Where(f=>f.Rank>=0).Select(f=>(f.Front,f.Back)));
        var result=new List<CoverageResult>();
        foreach(int side in new[]{-1,1})
        {
            long inside=sample[side].Length,covered=0,solid=0;
            foreach(var p in sample[side]){bool occupied=solids.Any(t=>t.Has(p));if(occupied||envelopes.Any(t=>t.Has(p)))covered++;if(occupied)solid++;}
            result.Add(new(side,inside,covered,solid,(double)covered/inside,(double)solid/inside,inside*Math.Pow(Step,3)));
        }
        return result.ToArray();
    }
}
