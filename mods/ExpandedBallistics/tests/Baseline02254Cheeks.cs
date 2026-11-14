using System.Numerics;
using System.Text.Json;
using static SprocketExpandedBallistics.ComposerFill;
namespace SprocketExpandedBallistics;
internal static class Baseline02254Cheeks
{
    internal static ComposerMeshPlan Cheeks(IReadOnlyList<FillTriangle> walls,FillBounds host,FillSettings settings,CheekSettings? cheekSettings=null,float gunHalfGap=.35f,bool gunFallback=true,float gunCentre=0)
    {
        settings.Validate();var limits=cheekSettings??new();limits.Validate();
        float outer=Math.Min(Math.Min(host.Max.X,-host.Min.X)-.02f,1.3f);
        float bottom=Math.Max(host.Min.Y+.08f,.10f),top=Math.Min(host.Max.Y-.08f,.45f),rear=(float)limits.RearBoundaryM;
        if(outer<=gunHalfGap+.02f||top-bottom<.10f||host.Max.Z-rear<.10f)throw new InvalidOperationException("No room for cheek fillers beside the gun and ahead of the rear limit.");
        var vertices=new List<float>();var faces=new List<object>();var edges=new List<int>();int rejected=0;
        const int nx=12,ny=4;
        foreach(int side in new[]{-1,1})for(int y=0;y<ny;y++)
        {
            float[]? previous=null;
            for(int x=0;x<nx;x++)
            {
                var corners=new List<Vector3>();var depths=new List<float>();
                foreach(var (dx,dy) in new[]{(0,0),(1,0),(1,1),(0,1)})
                {
                    float edge=gunCentre+side*gunHalfGap;
                    float px=edge+(side*outer-edge)*(x+dx)/nx,py=bottom+(top-bottom)*(y+dy)/ny;
                    var origin=new Vector3(px,py,rear);var hit=RaySurface(origin,walls);
                    if(!hit.HasValue||Math.Abs(hit.Value.Normal.X)>Math.Abs(hit.Value.Normal.Z))break;
                    float depth=(float)hit.Value.Distance;
                    // A cast side/rear surface can face partly forwards. Stop at a sudden depth jump too.
                    if(previous!=null&&depth>previous[dy]+limits.RowDepthMm*.001)break;
                    depths.Add(depth);corners.Add(origin+Vector3.UnitZ*(depth-(float)settings.ClearanceMm*.001f));
                }
                if(corners.Count!=4){rejected++;if(previous==null)continue;break;}
                previous=new[]{depths[1],depths[2]};
                foreach(var triangle in new[]{new[]{0,1,2},new[]{0,2,3}})
                {
                    var p=triangle.Select(i=>corners[i]).ToArray();var n=Vector3.Normalize(Vector3.Cross(p[1]-p[0],p[2]-p[0]));
                    if(n.Z<0){Array.Reverse(p);n=-n;}
                    if(n.Z<.2f||Math.Abs(n.X)>n.Z)continue;
                    var t=new ushort[3];bool valid=true;
                    for(int j=0;j<3;j++)
                    {
                        double bound=(p[j].Z-rear)/n.Z*1000;
                        if(side*n.X>1e-6)bound=Math.Min(bound,(side*(p[j].X-gunCentre)-gunHalfGap)/(side*n.X)*1000);
                        double mm=Math.Floor(Math.Min(bound,limits.MaximumMm));
                        if(mm<3){valid=false;break;}t[j]=(ushort)mm;
                    }
                    if(!valid)continue;
                    var ids=new int[3];
                    for(int j=0;j<3;j++){ids[j]=vertices.Count/3;vertices.AddRange(new[]{p[j].X,p[j].Y,p[j].Z});}
                    for(int j=0;j<3;j++)edges.AddRange(new[]{ids[j],ids[(j+1)%3]});
                    faces.Add(new{v=ids,t,tm=0,te=0});
                }
            }
        }
        if(faces.Count==0)throw new InvalidOperationException("No cheek surface found; check the turret attachment and orientation.");
        var json=JsonSerializer.Serialize(new{majorVersion=0,minorVersion=3,vertices,edges,edgeFlags=edges.Where((_,i)=>i%2==0).Select(_=>0),faces});
        return new(json,Read(json),0,0,0,$"Adds two cheek fillers, left and right of the gun. Maximum thickness {limits.MaximumMm:0.#} mm. "+(gunFallback?"Gun size unavailable; using a 700 mm centre opening. ":"")+"Undo removes it in one step.",true){Action=ComposerMeshAction.CheekPreset,MaximumMm=limits.MaximumMm};
    }
}
