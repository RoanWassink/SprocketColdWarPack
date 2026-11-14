using System.Numerics;
namespace SprocketExpandedBallistics;

/// <summary>Geometry of a 0.22.57 plan (test-only reference): front points, ribs, thickness and back points per face.</summary>
internal sealed record LegacyShellResult057(Vector3[] Wall,Vector3[] Front,Vector3[] Rib,double[] RibLengthMm,double[] IntendedLengthMm,int[] ProfileMm,int[][] Faces,Vector3[] Normals,ushort[][] Thickness,Vector3[][] Back,int OmittedFaces,int Shortened);

/// <summary>
/// Frozen copy of the 0.22.57 planner (src-0.22.57/CheekShellFill.cs, Plan body unchanged; Build reduced to geometry). Test-only: it gives the
/// 0.22.57 blocks for "nothing got worse" and for the 0.22.57 column of the contact sheet. The 0.22.57 rules drop a point when no rib length keeps
/// the back point in the floor/roof band or outside the gun lane; 0.22.58 clamps instead.
/// </summary>
internal static class LegacyCheekShellFill057
{
    internal static LegacyShellResult057 Plan(IReadOnlyList<ShellWallFace> wall,IReadOnlyList<FillTriangle> hostWalls,FillBounds host,CheekShellRequest request,FillSettings settings,CheekSettings limits,float gunHalfGap=.35f,bool gunFallback=true,float gunCentre=0)
    {
        settings.Validate();limits.Validate();bool fallback=request.Fallback;
        float rear=(float)limits.RearBoundaryM,bottom=host.Min.Y+.03f,top=host.Max.Y-.03f;
        double forward=Math.Cos(limits.WallAngleDegrees*Math.PI/180),clearance=settings.ClearanceMm*.001;
        bool Beside(IReadOnlyList<Vector3> p)=>p.All(v=>v.X<=gunCentre-gunHalfGap+1e-6f)||p.All(v=>v.X>=gunCentre+gunHalfGap-1e-6f);
        var points=new List<Vector3>();var ids=new Dictionary<Vector3,int>();
        int Id(Vector3 p){if(!ids.TryGetValue(p,out int id)){id=points.Count;ids.Add(p,id);points.Add(p);}return id;}
        var faces=new List<(int[] V,ushort[] W,Vector3 N)>();
        foreach(var face in wall)
        {
            if(face.HoleLoops!=0)continue;
            var n=CheekShellFill.Newell(face.Corners);if(n.LengthSquared()<1e-14f)continue;n=Vector3.Normalize(n);
            if(n.Z<forward)continue;
            if(face.Corners.Any(v=>v.Z<rear-1e-6f))continue;
            if(!Beside(face.Corners))continue;
            var v=face.Corners.Select(Id).ToArray();if(v.Distinct().Count()!=v.Length)continue;
            if(v.Length<=4){faces.Add((v,face.WallMm.ToArray(),n));continue;}
            for(int k=1;k+1<v.Length;k++)
            {
                var tri=new[]{v[0],v[k],v[k+1]};var tn=CheekShellFill.Newell(tri.Select(i=>points[i]).ToArray());
                if(tn.LengthSquared()<1e-14f||Vector3.Dot(tn,n)<=0)continue;
                faces.Add((tri,new[]{face.WallMm[0],face.WallMm[k],face.WallMm[k+1]},Vector3.Normalize(tn)));
            }
        }
        if(faces.Count==0)throw new InvalidOperationException("No turret front faces beside the gun.");
        double sinMin=Math.Sin(CheekShellFill.MinimumRibAngleDegrees*Math.PI/180),cosPanel=Math.Cos(CheekShellFill.PanelAngleDegrees*Math.PI/180);
        var allIncident=new Dictionary<Vector3,List<(Vector3 N,ushort T)>>();
        foreach(var face in wall)
        {
            if(face.Corners.Length<3)continue;var n=CheekShellFill.Newell(face.Corners);if(n.LengthSquared()<1e-14f)continue;n=Vector3.Normalize(n);
            for(int j=0;j<face.Corners.Length;j++){if(!allIncident.TryGetValue(face.Corners[j],out var list))allIncident[face.Corners[j]]=list=new();list.Add((n,face.WallMm[Math.Min(j,face.WallMm.Length-1)]));}
        }
        var active=Enumerable.Range(0,faces.Count).ToList();int omittedFaces=0,shortened=0;
        Dictionary<int,Vector3> dir=new(),front=new();Dictionary<int,double> intended=new();Dictionary<int,int> profile=new();
        var length=new Dictionary<int,double>();var start=new Dictionary<int,double>();
        for(int pass=0;;pass++)
        {
            if(pass>32)throw new InvalidOperationException("legacy planner did not settle");
            var around=new Dictionary<int,List<(int F,int J)>>();
            foreach(int f in active)for(int j=0;j<faces[f].V.Length;j++){int v=faces[f].V[j];if(!around.TryGetValue(v,out var list))around[v]=list=new();list.Add((f,j));}
            if(around.Count==0)throw new InvalidOperationException("No cheek faces remain after fitting.");
            float outerLeft=gunHalfGap,outerRight=gunHalfGap;
            foreach(int v in around.Keys){float dx=points[v].X-gunCentre;if(dx<0)outerLeft=Math.Max(outerLeft,-dx);else outerRight=Math.Max(outerRight,dx);}
            int Profile(float x){float dx=Math.Abs(x-gunCentre),outer=x<gunCentre?outerLeft:outerRight;double frac=outer-gunHalfGap<=1e-6f?1:Math.Clamp((dx-gunHalfGap)/(outer-gunHalfGap),0,1);return (int)Math.Round(request.GunMm+(request.OuterMm-request.GunMm)*frac,MidpointRounding.AwayFromZero);}
            dir.Clear();front.Clear();intended.Clear();profile.Clear();
            var dropFaces=new HashSet<int>();var dropThin=new HashSet<int>();
            foreach(var (v,list) in around)
            {
                var normals=list.Select(x=>faces[x.F].N).ToArray();var panels=CheekShellFill.Panels(normals,cosPanel);
                var sum=panels.Aggregate(Vector3.Zero,(a,b)=>a+b);
                var u=sum.LengthSquared()<1e-12f?Vector3.Zero:Vector3.Normalize(-sum);
                if(u==Vector3.Zero||normals.Any(n=>-Vector3.Dot(n,u)<sinMin)){foreach(var x in list)dropFaces.Add(x.F);continue;}
                double s=0;foreach(var (f,j) in list)s=Math.Max(s,(faces[f].W[j]*.001+clearance)/-Vector3.Dot(faces[f].N,u));
                if(allIncident.TryGetValue(points[v],out var others))foreach(var (on,ot) in others){double d=-Vector3.Dot(on,u);if(d>=CheekShellFill.NeighbourWallCos)s=Math.Max(s,(ot*.001+clearance)/d);}
                double cosMean=panels.Average(p=>-Vector3.Dot(p,u));int t=Profile(points[v].X);
                dir[v]=u;front[v]=points[v]+u*(float)s;profile[v]=t;intended[v]=t/cosMean;
            }
            if(dropFaces.Count>0){omittedFaces+=dropFaces.Count;active.RemoveAll(dropFaces.Contains);continue;}
            var corners=new List<(int F,int J,int V,int Key,Vector3 D,double Cos,int Side)>();int keyCount=0;
            foreach(int f in active)
            {
                int side=points[faces[f].V[0]].X<gunCentre?-1:1;
                for(int j=0;j<faces[f].V.Length;j++)
                {
                    int v=faces[f].V[j];
                    if(fallback)corners.Add((f,j,v,keyCount++,-faces[f].N,1,side));
                    else corners.Add((f,j,v,v,dir[v],-Vector3.Dot(faces[f].N,dir[v]),side));
                }
            }
            length.Clear();start.Clear();
            foreach(var c in corners){double l0=fallback?profile[c.V]:intended[c.V];length[c.Key]=l0;start[c.Key]=l0;}
            foreach(var c in corners)
            {
                var p=front[c.V];var d=c.D;double bound=double.PositiveInfinity;
                if(d.Z<0)bound=Math.Min(bound,(p.Z-rear)/-d.Z);
                if(c.Side*d.X<0)bound=Math.Min(bound,(c.Side*(p.X-gunCentre)-gunHalfGap)/(-c.Side*d.X));
                if(d.Y<0)bound=Math.Min(bound,(p.Y-bottom)/-d.Y);
                if(d.Y>0)bound=Math.Min(bound,(top-p.Y)/d.Y);
                var hit=CheekShellFill.RayEntering(p,d,hostWalls,10);
                if(hit.HasValue)bound=Math.Min(bound,hit.Value-clearance);
                if(double.IsFinite(bound))length[c.Key]=Math.Min(length[c.Key],Math.Max(0,bound)*1000);
            }
            var thickness=new Dictionary<int,ushort[]>();var back=new Dictionary<int,Vector3[]>();
            foreach(int f in active){thickness[f]=new ushort[faces[f].V.Length];back[f]=new Vector3[faces[f].V.Length];}
            Vector3 Front(int f,int j)=>front[faces[f].V[j]];
            for(int guard=0;;guard++)
            {
                if(guard>20000)throw new InvalidOperationException("legacy planner could not fit");
                bool changed=false;
                foreach(var c in corners)
                {
                    if(dropThin.Contains(c.V))continue;
                    double l=length[c.Key];var p=front[c.V];int n=faces[c.F].V.Length;
                    double offsetDepth=fallback?0:Vector3.Dot(CheekShellFill.Offset(p,Front(c.F,(c.J+1)%n),Front(c.F,(c.J+n-1)%n)),faces[c.F].N)*1000;
                    int t=(int)Math.Round(l*c.Cos+offsetDepth,MidpointRounding.AwayFromZero);
                    if(l<3-1e-9||t<3){dropThin.Add(c.V);continue;}
                    var b=fallback?p-faces[c.F].N*(float)(t*.001):CheekShellFill.NativeBack(p,Front(c.F,(c.J+1)%n),Front(c.F,(c.J+n-1)%n),c.D,(ushort)t,faces[c.F].N);
                    thickness[c.F][c.J]=(ushort)t;back[c.F][c.J]=b;
                    bool bad=b.Z<rear-1e-6f||c.Side*(b.X-gunCentre)<gunHalfGap-1e-6f;
                    if(c.D.Y<0&&b.Y<bottom-1e-6f)bad=true;
                    if(c.D.Y>0&&b.Y>top+1e-6f)bad=true;
                    var hit=CheekShellFill.RayEntering(p,c.D,hostWalls,10);
                    if(hit.HasValue&&Vector3.Distance(p,b)>hit.Value-clearance+1e-6)bad=true;
                    if(bad){length[c.Key]=l-1;changed=true;}
                }
                if(changed)continue;
                foreach(int f in active)
                {
                    if(faces[f].V.Any(dropThin.Contains))continue;
                    var nb=CheekShellFill.Newell(back[f]);
                    if(nb.LengthSquared()<1e-12f||Vector3.Dot(nb,faces[f].N)<=0){foreach(var c in corners)if(c.F==f)length[c.Key]-=1;changed=true;}
                }
                if(!changed)break;
            }
            if(dropThin.Count>0)
            {
                var thinFaces=new HashSet<int>();foreach(int f in active)if(faces[f].V.Any(dropThin.Contains))thinFaces.Add(f);
                omittedFaces+=thinFaces.Count;active.RemoveAll(thinFaces.Contains);continue;
            }
            var shortenedVertices=new HashSet<int>();
            foreach(var c in corners)if(start[c.Key]-length[c.Key]>1e-9)shortenedVertices.Add(c.V);
            shortened=shortenedVertices.Count;
            var used=around.Keys.OrderBy(v=>v).ToArray();var remap=used.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i);
            double Len(int v)=>fallback?corners.Where(c=>c.V==v).Average(c=>length[c.Key]):length[v];
            return new(used.Select(v=>points[v]).ToArray(),used.Select(v=>front[v]).ToArray(),used.Select(v=>fallback?-faces[corners.First(c=>c.V==v).F].N:dir[v]).ToArray(),used.Select(Len).ToArray(),
                used.Select(v=>fallback?(double)profile[v]:intended[v]).ToArray(),used.Select(v=>profile[v]).ToArray(),active.Select(f=>faces[f].V.Select(id=>remap[id]).ToArray()).ToArray(),
                active.Select(f=>faces[f].N).ToArray(),active.Select(f=>thickness[f]).ToArray(),active.Select(f=>back[f]).ToArray(),omittedFaces,shortened);
        }
    }
}
