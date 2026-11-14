using System.Globalization;
using System.Numerics;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>One drawn face of the turret wall in the host frame: corners in order, the wall thickness (mm) at each corner, hole-loop count.</summary>
internal sealed record ShellWallFace(Vector3[] Corners,ushort[] WallMm,int HoleLoops=0);
/// <summary>The two numbers of the window. Fallback: separate blocks on the face normal (tm 0) after the game refused the ribs.</summary>
internal sealed record CheekShellRequest(double GunMm,double OuterMm,bool Fallback=false,double? RearLimitMm=null);
/// <summary>Host-frame geometry of a plan, for the tests and the preview. Vertex arrays are indexed by the output vertex id of the front points.</summary>
internal sealed record CheekShellResult(ComposerMeshPlan Plan,Vector3[] Wall,Vector3[] Front,Vector3[] Rib,Vector3[] RibEnd,double[] RibLengthMm,double[] IntendedLengthMm,int[] ProfileMm,int[][] Faces,Vector3[] Normals,ushort[][] Thickness,Vector3[][] Back,
    int Ribs,int Shortened,double MaxShortenMm,int OmittedHole,int OmittedBehind,int OmittedGun,int OmittedAngleVertices,int OmittedThinVertices,int OmittedFaces,int Panels,bool Fallback,string[] LimitReason,string[] OmittedReason,
    int EligibleFaces,int CutFaces,int CutLostFaces,double CutLostAreaM2,int ClampedFloorRoof,int ClampedLane,int ReAimed,ushort[][] FaceWallMm,int[] SourceFace,string[] OmittedFaceReason,int ClampedRear,double OmittedAreaM2);

/// <summary>
/// 0.22.57/0.22.58 "Cheek fill (shell)": a copy of the turret's own front faces, laid against the inside of the wall, with one rib per vertex.
/// Every corner at a vertex follows that rib (native AlongEdgeManual, tm 4, te = rib edge), so the back is one closed surface with the
/// shape of the front. Native measures Along edge perpendicular to the face (out-0.22.57/ALONG-EDGE-FORMULA.md), so each face gets its
/// own whole-millimetre t = round(L * |u.n|) and all back points of a vertex meet at the rib end. 0.22.58: the floor/roof band and the gun
/// lane clamp the rib end instead of dropping the point, faces across the lane are cut at its edge, and a rib that loses the 10 degree rule
/// by clamping is turned back toward the mean normal as little as possible. Managed planning only.
/// </summary>
internal static class CheekShellFill
{
    internal const string LinkedBackMarker="This turret cannot take a linked back surface.";
    internal const string FallbackNote=LinkedBackMarker+" Apply now uses separate blocks; small gaps at the back are possible.";
    /// <summary>0.22.59: range of the window's rear limit (mm in front of the turret centre); the same as the config setting Cheek rear boundary (m).</summary>
    internal const double RearLimitMinMm=-2000,RearLimitMaxMm=5000;
    /// <summary>0.22.59: the limits for this one button: its own thickness maximum and, when the window gives one, its own rear limit. The configured limits are never changed.</summary>
    internal static CheekSettings ShellLimits(CheekSettings configured,double shellMaximumMm,double? rearLimitMm)
    {
        var limits=configured with{MaximumMm=shellMaximumMm};
        if(rearLimitMm is {} mm)
        {
            if(!double.IsFinite(mm)||mm<RearLimitMinMm||mm>RearLimitMaxMm)throw new InvalidOperationException($"Enter a rear limit from {RearLimitMinMm:0} to {RearLimitMaxMm:0} mm in front of the turret centre.");
            limits=limits with{RearBoundaryM=mm/1000.0};
        }
        return limits;
    }
    internal const double MinimumRibAngleDegrees=10,PanelAngleDegrees=3,NativeOffsetM=.00025;
    /// <summary>A neighbouring (unfilled) wall is cleared by the inner point only when the rib meets it at 30 degrees or more; a shallower wall would push the point far and tilt the front.</summary>
    internal const double NeighbourWallCos=.5;
    internal const byte AlongEdgeManual=4;
    /// <summary>A clamped inner point or rib end stays this far inside the limit, so the native 0.25 mm in-plane offset cannot carry the back point across it.</summary>
    internal const double ClampMarginM=.00035;
    private static readonly double SinOneDegree=Math.Sin(-Math.PI/180);
    private sealed record Face(int[] V,ushort[] W,Vector3 N,int Source,bool Cut);

    internal static CheekShellResult Plan(IReadOnlyList<ShellWallFace> wall,IReadOnlyList<FillTriangle> hostWalls,FillBounds host,CheekShellRequest request,FillSettings settings,CheekSettings limits,float gunHalfGap=.35f,bool gunFallback=true,float gunCentre=0,Func<Vector3,Vector3>? toPlate=null)
    {
        settings.Validate();limits.Validate();
        foreach(var (value,name) in new[]{(request.GunMm,"at the gun"),(request.OuterMm,"at the outer edge")})
            if(!double.IsFinite(value)||value<3||value>limits.MaximumMm)throw new InvalidOperationException($"Enter a thickness {name} from 3 to {limits.MaximumMm:0.#} mm.");
        if(!float.IsFinite(gunHalfGap)||gunHalfGap<=0||!float.IsFinite(gunCentre))throw new InvalidOperationException("Gun opening unavailable.");
        bool fallback=request.Fallback;
        float rear=(float)limits.RearBoundaryM,bottom=host.Min.Y+.03f,top=host.Max.Y-.03f;
        double forward=Math.Cos(limits.WallAngleDegrees*Math.PI/180),clearance=settings.ClearanceMm*.001;
        // 1. Weld the turret vertices; keep the forward faces ahead of the rear limit. A face wholly in the gun lane is skipped; a face across the
        // lane edge is cut there and the part(s) beside the gun are kept. More than four corners become triangles (te packs four slots).
        var points=new List<Vector3>();var ids=new Dictionary<Vector3,int>();
        int Id(Vector3 p){if(!ids.TryGetValue(p,out int id)){id=points.Count;ids.Add(p,id);points.Add(p);}return id;}
        var faces=new List<Face>();int omittedHole=0,omittedBehind=0,omittedGun=0,cutFaces=0,cutLost=0;double cutLostArea=0;
        for(int source=0;source<wall.Count;source++)
        {
            var face=wall[source];
            if(face.Corners.Length<3||face.Corners.Length!=face.WallMm.Length)throw new InvalidOperationException("Turret wall face data is incomplete.");
            if(face.Corners.Any(v=>!ComposerFill.Finite(v)))throw new InvalidOperationException("Turret wall has invalid vertices.");
            if(face.HoleLoops!=0){omittedHole++;continue;}
            var n=Newell(face.Corners);if(n.LengthSquared()<1e-14f)continue;n=Vector3.Normalize(n);
            if(n.Z<forward)continue;
            if(face.Corners.Any(v=>v.Z<rear-1e-6f)){omittedBehind++;continue;}
            bool leftOut=face.Corners.Any(v=>v.X<gunCentre-gunHalfGap-1e-6f),rightOut=face.Corners.Any(v=>v.X>gunCentre+gunHalfGap+1e-6f);
            bool inLane=face.Corners.Any(v=>v.X>gunCentre-gunHalfGap+1e-6f&&v.X<gunCentre+gunHalfGap-1e-6f);
            if(!leftOut&&!rightOut){omittedGun++;continue;}
            var pieces=new List<(Vector3[] P,ushort[] W)>();
            if(!inLane&&!(leftOut&&rightOut))pieces.Add((face.Corners,face.WallMm));
            else
            {
                // 3. Cut at the lane edge: the part beside the gun stays, with its own corners on the cut line (shared by the neighbour through the same canonical cut point).
                cutFaces++;
                foreach(int side in new[]{-1,1})
                {
                    if(side<0?!leftOut:!rightOut)continue;
                    var piece=Clip(face.Corners,face.WallMm,gunCentre+side*gunHalfGap,side);
                    if(piece.P.Length<3||Newell(piece.P).Length()*.5<1e-6){cutLost++;cutLostArea+=Newell(piece.P).Length()*.5;continue;}
                    pieces.Add(piece);
                }
            }
            foreach(var (p,w) in pieces)
            {
                var v=p.Select(Id).ToArray();if(v.Distinct().Count()!=v.Length)continue;
                bool cut=inLane||leftOut&&rightOut;
                if(v.Length<=4){faces.Add(new(v,w.ToArray(),n,source,cut));continue;}
                for(int k=1;k+1<v.Length;k++)
                {
                    var tri=new[]{v[0],v[k],v[k+1]};var tn=Newell(tri.Select(i=>points[i]).ToArray());
                    if(tn.LengthSquared()<1e-14f||Vector3.Dot(tn,n)<=0)continue;
                    faces.Add(new(tri,new[]{w[0],w[k],w[k+1]},Vector3.Normalize(tn),source,cut));
                }
            }
        }
        if(faces.Count==0)throw new InvalidOperationException("No turret front faces beside the gun. Check the turret attachment and wall angle.");
        int eligible=faces.Count;
        double sinMin=Math.Sin(MinimumRibAngleDegrees*Math.PI/180),cosPanel=Math.Cos(PanelAngleDegrees*Math.PI/180);
        // Every drawn face at a turret point (also the side, roof, floor and port faces that are not filled) counts for the inner point:
        // it must lie at least wall + clearance behind each of them, or the rib would start inside the neighbouring wall.
        var allIncident=new Dictionary<Vector3,List<(Vector3 N,ushort T)>>();
        foreach(var face in wall)
        {
            if(face.Corners.Length<3)continue;var n=Newell(face.Corners);if(n.LengthSquared()<1e-14f)continue;n=Vector3.Normalize(n);
            for(int j=0;j<face.Corners.Length;j++){if(!allIncident.TryGetValue(face.Corners[j],out var list))allIncident[face.Corners[j]]=list=new();list.Add((n,face.WallMm[Math.Min(j,face.WallMm.Length-1)]));}
        }
        var active=Enumerable.Range(0,faces.Count).ToList();var omittedReason=new List<string>();var omittedFaceReason=new List<string>();var overlapPoints=new HashSet<int>();
        int omittedAngle=0,omittedThin=0,omittedFaces=0,panelsTotal=0,shortened=0,clampedBand=0,clampedLane=0,clampedRear=0,reAimed=0;double maxShorten=0,omittedArea=0;
        Dictionary<int,Vector3> dir=new(),front=new();Dictionary<int,double> intended=new(),startLength=new();Dictionary<int,int> profile=new();var clampReason=new Dictionary<int,List<string>>();
        var length=new Dictionary<int,double>();var start=new Dictionary<int,double>();
        int[] used=Array.Empty<int>();
        int Side(int v,IEnumerable<int> faceIds){float x=points[v].X-gunCentre;if(Math.Abs(x)>1e-6f)return x<0?-1:1;float mean=faceIds.SelectMany(f=>faces[f].V).Average(i=>points[i].X)-gunCentre;return mean<0?-1:1;}
        for(int pass=0;;pass++)
        {
            if(pass>32)throw new InvalidOperationException("Cheek fill (shell) could not settle; use smaller thicknesses.");
            var around=new Dictionary<int,List<(int F,int J)>>();
            foreach(int f in active)for(int j=0;j<faces[f].V.Length;j++){int v=faces[f].V[j];if(!around.TryGetValue(v,out var list))around[v]=list=new();list.Add((f,j));}
            if(around.Count==0)throw new InvalidOperationException("No cheek faces remain after fitting. Use smaller thicknesses or check the turret attachment.");
            float outerLeft=gunHalfGap,outerRight=gunHalfGap;
            foreach(int v in around.Keys){float dx=points[v].X-gunCentre;if(dx<0)outerLeft=Math.Max(outerLeft,-dx);else outerRight=Math.Max(outerRight,dx);}
            // 4. Thickness profile: linear from the edge of the gun lane to the outermost vertex on that side, whole millimetres.
            int Profile(float x){float dx=Math.Abs(x-gunCentre),outer=x<gunCentre?outerLeft:outerRight;double frac=outer-gunHalfGap<=1e-6f?1:Math.Clamp((dx-gunHalfGap)/(outer-gunHalfGap),0,1);return (int)Math.Round(request.GunMm+(request.OuterMm-request.GunMm)*frac,MidpointRounding.AwayFromZero);}
            dir.Clear();front.Clear();intended.Clear();startLength.Clear();profile.Clear();panelsTotal=0;overlapPoints.Clear();clampedBand=0;clampedLane=0;clampedRear=0;reAimed=0;clampReason.Clear();
            var dropFaces=new HashSet<int>();var dropAngle=new HashSet<int>();var dropThin=new HashSet<int>();var dropReason=new Dictionary<int,string>();
            // 2+3+7. One rib per vertex: faces within 3 degrees form one panel; the rib is the inward mean of the panel normals, so a knuckle
            // between two panels gets the bisector. Cast walls (every face its own panel) fan the ribs with the curvature.
            foreach(var (v,list) in around)
            {
                var normals=list.Select(x=>faces[x.F].N).ToArray();var panels=Panels(normals,cosPanel);panelsTotal+=panels.Count;
                var sum=panels.Aggregate(Vector3.Zero,(a,b)=>a+b);
                var u=sum.LengthSquared()<1e-12f?Vector3.Zero:Vector3.Normalize(-sum);
                if(u==Vector3.Zero||normals.Any(n=>-Vector3.Dot(n,u)<sinMin)){dropAngle.Add(v);dropReason[v]="rib angle";foreach(var x in list)dropFaces.Add(x.F);continue;}
                int side=Side(v,list.Select(x=>x.F));
                // Inner point: at least wall + clearance behind every filled face around the vertex, measured on that face's own normal, and
                // behind every other drawn wall at that point that the rib meets at 30 degrees or more. A shallower neighbouring wall is not
                // cleared (the point may start inside its armour; counted), because clearing it would move the point far along the rib.
                // 0.22.58: the inner point never enters the gun lane; it is pushed sideways to the lane edge, and the depth is kept by a longer offset.
                var walls=list.Select(x=>(faces[x.F].N,(double)faces[x.F].W[x.J])).ToList();bool overlaps=false;
                if(allIncident.TryGetValue(points[v],out var others))foreach(var (on,ot) in others){double dn=-Vector3.Dot(on,u);if(dn>=NeighbourWallCos)walls.Add((on,ot));else if(dn>1e-6&&(ot*.001+clearance)/dn>walls.Max(w=>(w.Item2*.001+clearance)/-Vector3.Dot(w.Item1,u)))overlaps=true;}
                if(overlaps)overlapPoints.Add(v);
                double s=0,k=0,laneEdge=gunHalfGap+ClampMarginM;bool laneClamped=false,failed=false;
                for(int iteration=0;;iteration++)
                {
                    s=walls.Max(w=>(w.Item2*.001+clearance+w.Item1.X*k)/-Vector3.Dot(w.Item1,u));
                    double x=points[v].X+u.X*s+k,shortfall=laneEdge-side*(x-gunCentre);
                    if(shortfall<=1e-7||iteration>64){failed=shortfall>1e-7||s>2;break;}
                    k+=side*shortfall;laneClamped=true;
                }
                if(failed){dropThin.Add(v);dropReason[v]="gun lane";continue;}
                var p0=points[v]+u*(float)s+new Vector3((float)k,0,0);
                if(laneClamped)p0.X=(float)(gunCentre+side*laneEdge);
                double cosMean=panels.Average(p=>-Vector3.Dot(p,u));int t=Profile(points[v].X);double l0=t/cosMean;
                // 1+2 (0.22.58): clamp the intended rib end to the floor/roof band (or to the inner point's own height when that is outside the band),
                // to the lane edge, and (inner point already behind the rear limit) no further back than the inner point; the rib then points at the
                // clamped end instead of the point being left out. A small margin keeps the native 0.25 mm offset inside the limit.
                var p1=p0+u*(float)(l0*.001);float bottomV=Math.Min(bottom,p0.Y),topV=Math.Max(top,p0.Y),rearV=Math.Min(rear,p0.Z),margin=(float)ClampMarginM;bool band=false,lane=false,rearClamp=false;
                if(p1.Y<bottomV+margin){p1.Y=bottomV+margin;band=true;}if(p1.Y>topV-margin){p1.Y=topV-margin;band=true;}
                if(side*(p1.X-gunCentre)<laneEdge){p1.X=(float)(gunCentre+side*laneEdge);lane=true;}
                if(p0.Z<rear&&p1.Z<rearV+margin){p1.Z=rearV+margin;rearClamp=true;}
                var rib=p1-p0;double l=rib.Length()*1000;
                if(l<3-1e-9){dropThin.Add(v);dropReason[v]=band?(p1.Y<=bottomV+2*margin?"floor":"roof"):lane?"gun lane":"rear limit";continue;}
                var d0=Vector3.Normalize(rib);var d=d0;
                if(normals.Any(n=>-Vector3.Dot(n,d)<sinMin))
                {
                    // 4 (0.22.58): the clamped rib lost the 10 degree rule; turn it back toward the mean inward normal as little as possible.
                    double lo=0,hi=1;
                    for(int step=0;step<40;step++){double mid=(lo+hi)/2;var cand=Vector3.Normalize(Vector3.Lerp(d0,u,(float)mid));if(normals.All(n=>-Vector3.Dot(n,cand)>=sinMin))hi=mid;else lo=mid;}
                    d=Vector3.Normalize(Vector3.Lerp(d0,u,(float)hi));reAimed++;
                    if(normals.Any(n=>-Vector3.Dot(n,d)<sinMin)){dropAngle.Add(v);dropReason[v]="rib angle";foreach(var x in list)dropFaces.Add(x.F);continue;}
                }
                var why=new List<string>();
                if(band){clampedBand++;why.Add("floor/roof band");}if(lane||laneClamped){clampedLane++;why.Add("gun lane");}if(rearClamp){clampedRear++;why.Add("rear limit");}
                if(d!=d0)why.Add("turned toward the mean normal (10 degrees)");
                if(why.Count>0)clampReason[v]=why;
                dir[v]=d;front[v]=p0;profile[v]=t;intended[v]=l0;startLength[v]=l;
            }
            if(dropFaces.Count>0||dropThin.Count>0)
            {
                foreach(int v in dropThin)foreach(var x in around[v])dropFaces.Add(x.F);
                omittedAngle+=dropAngle.Count;omittedThin+=dropThin.Count;omittedFaces+=dropFaces.Count;
                foreach(int v in dropAngle.Concat(dropThin))omittedReason.Add(dropReason[v]);
                foreach(int f in dropFaces){omittedFaceReason.Add(dropReason[faces[f].V.First(v=>dropReason.ContainsKey(v))]);omittedArea+=Newell(faces[f].V.Select(i=>points[i]).ToArray()).Length()*.5;}
                active.RemoveAll(dropFaces.Contains);continue;
            }
            // Corner model. Linked: the length key is the vertex and the direction the rib. Fallback: the key is the corner and the direction the face normal.
            var corners=new List<(int F,int J,int V,int Key,Vector3 D,double Cos,int Side)>();int keyCount=0;
            foreach(int f in active)
            {
                int side=Side(faces[f].V[0],new[]{f});
                for(int j=0;j<faces[f].V.Length;j++)
                {
                    int v=faces[f].V[j];
                    if(fallback)corners.Add((f,j,v,keyCount++,-faces[f].N,1,side));
                    else corners.Add((f,j,v,v,dir[v],-Vector3.Dot(faces[f].N,dir[v]),side));
                }
            }
            length.Clear();start.Clear();var reason=new Dictionary<int,string>();
            foreach(var c in corners){double l0=fallback?profile[c.V]:startLength[c.V];length[c.Key]=l0;start[c.Key]=fallback?profile[c.V]:intended[c.V];}
            // 5. Shorten along the rib: ahead of the rear limit, outside the gun lane, within the floor/roof band, and inside the turret
            // (ray against the other walls, minus the clearance). First the analytic bound, then the whole-millimetre check below.
            foreach(var c in corners)
            {
                var p=front[c.V];var d=c.D;double bound=double.PositiveInfinity;string why="";float bottomV=Math.Min(bottom,p.Y),topV=Math.Max(top,p.Y),rearV=Math.Min(rear,p.Z);
                void Limit(double value,string name){if(value<bound){bound=value;why=name;}}
                if(d.Z<-1e-9)Limit((p.Z-rearV)/-d.Z,"rear limit");
                if(c.Side*d.X<-1e-9)Limit((c.Side*(p.X-gunCentre)-gunHalfGap)/(-c.Side*d.X),"gun lane");
                if(d.Y<-1e-9)Limit((p.Y-bottomV)/-d.Y,"floor");
                if(d.Y>1e-9)Limit((topV-p.Y)/d.Y,"roof");
                var hit=RayEntering(p,d,hostWalls,10);
                if(hit.HasValue)Limit(hit.Value-clearance,"next wall");
                if(double.IsFinite(bound)&&Math.Max(0,bound)*1000<length[c.Key]-1e-9){length[c.Key]=Math.Max(0,bound)*1000;reason[c.Key]=why;}
            }
            // Whole millimetres with the native formula (including the 0.25 mm in-plane offset); then 6, the crossing check per face. Loop until stable.
            var thickness=new Dictionary<int,ushort[]>();var back=new Dictionary<int,Vector3[]>();
            foreach(int f in active){thickness[f]=new ushort[faces[f].V.Length];back[f]=new Vector3[faces[f].V.Length];}
            Vector3 Front(int f,int j)=>front[faces[f].V[j]];
            // Seam: the whole-millimetre t of each face puts its back point at t/|u.n| along the rib, so faces at different angles can land up to
            // 0.5/|u.n| mm apart. Within 1 mm below the limit, choose the L whose rounded t's cluster tightest on the rib (never longer, never over a limit).
            // 0.22.59: the tuning is a local function so that it can run again for a point the crossing-ribs loop below has shortened; the rule is unchanged.
            bool Tune(int v,double threshold)
            {
                var mine=corners.Where(c=>c.V==v).ToArray();if(mine.Length<2)return false;
                double Spread(double trial)
                {
                    // The second pass (threshold 1) measures the real back points, as the delivery check does (the in-plane offset differs per face).
                    if(threshold>.9)
                    {
                        var ends=new List<Vector3>();
                        foreach(var c in mine){int n=faces[c.F].V.Length;double off=Vector3.Dot(Offset(front[v],Front(c.F,(c.J+1)%n),Front(c.F,(c.J+n-1)%n)),faces[c.F].N)*1000;int t=(int)Math.Round(trial*c.Cos+off,MidpointRounding.AwayFromZero);if(t<3)return double.PositiveInfinity;ends.Add(NativeBack(front[v],Front(c.F,(c.J+1)%n),Front(c.F,(c.J+n-1)%n),c.D,(ushort)t,faces[c.F].N));}
                        double worst=0;for(int i=0;i<ends.Count;i++)for(int j=i+1;j<ends.Count;j++)worst=Math.Max(worst,Vector3.Distance(ends[i],ends[j])*1000);
                        return worst;
                    }
                    double lo=double.PositiveInfinity,hi=double.NegativeInfinity;
                    foreach(var c in mine){int n=faces[c.F].V.Length;double off=Vector3.Dot(Offset(front[v],Front(c.F,(c.J+1)%n),Front(c.F,(c.J+n-1)%n)),faces[c.F].N)*1000;double along=Math.Round(trial*c.Cos+off,MidpointRounding.AwayFromZero)/c.Cos;lo=Math.Min(lo,along);hi=Math.Max(hi,along);}
                    return hi-lo;
                }
                // Only a point whose rounded back points would already lie more than 0.75 mm apart (the native offset adds up to 0.25 mm) is tuned.
                if(Spread(length[v])<=threshold)return false;
                double best=length[v],bestSpread=Spread(length[v]);
                for(double trial=length[v]-.02;trial>=length[v]-1-1e-9&&trial>=3;trial-=.02){double s=Spread(trial);if(s<bestSpread-1e-6){bestSpread=s;best=trial;}}
                if(best<length[v]-1e-9){length[v]=best;reason.TryAdd(v,"whole millimetres");return true;}
                return false;
            }
            if(!fallback)foreach(int v in around.Keys)Tune(v,.75);
            var crossed=new HashSet<int>();
            for(int guard=0;;guard++)
            {
                if(guard>20000)throw new InvalidOperationException("Cheek fill (shell) could not fit the thickness; use smaller thicknesses.");
                bool changed=false;
                foreach(var c in corners)
                {
                    if(dropThin.Contains(c.V))continue;
                    double l=length[c.Key];var p=front[c.V];int n=faces[c.F].V.Length;float bottomV=Math.Min(bottom,p.Y),topV=Math.Max(top,p.Y),rearV=Math.Min(rear,p.Z);
                    // The native 0.25 mm offset lies in the plane of the corner's two edges; on a non-planar quad it has a small normal part.
                    // Choose t so the perpendicular depth (t minus that part) is the whole millimetre nearest to L * |u.n|.
                    double offsetDepth=fallback?0:Vector3.Dot(Offset(p,Front(c.F,(c.J+1)%n),Front(c.F,(c.J+n-1)%n)),faces[c.F].N)*1000;
                    int t=(int)Math.Round(l*c.Cos+offsetDepth,MidpointRounding.AwayFromZero);
                    if(l<3-1e-9||t<3){if(dropThin.Add(c.V))dropReason[c.V]=reason.TryGetValue(c.Key,out var why)?why:"less than 3 mm";continue;}
                    var b=fallback?p-faces[c.F].N*(float)(t*.001):NativeBack(p,Front(c.F,(c.J+1)%n),Front(c.F,(c.J+n-1)%n),c.D,(ushort)t,faces[c.F].N);
                    thickness[c.F][c.J]=(ushort)t;back[c.F][c.J]=b;
                    bool bad=b.Z<rearV-1e-6f||c.Side*(b.X-gunCentre)<gunHalfGap-1e-6f;
                    if(b.Y<bottomV-1e-6f||b.Y>topV+1e-6f)bad=true;
                    var hit=RayEntering(p,c.D,hostWalls,10);
                    if(hit.HasValue&&Vector3.Distance(p,b)>hit.Value-clearance+1e-6)bad=true;
                    if(bad){length[c.Key]=l-1;changed=true;reason.TryAdd(c.Key,"whole millimetres");}
                }
                if(changed)continue;
                foreach(int f in active)
                {
                    if(faces[f].V.Any(dropThin.Contains))continue;
                    var nb=Newell(back[f]);
                    if(nb.LengthSquared()<1e-12f||Vector3.Dot(nb,faces[f].N)<=0)
                    {
                        // The ribs cross behind this face: lower its corners until the back faces the same way as the front.
                        foreach(var c in corners)if(c.F==f){length[c.Key]-=1;reason[c.Key]="crossing ribs";crossed.Add(c.V);}
                        changed=true;
                    }
                }
                // 0.22.59: a point shortened by the crossing check was not tuned for the seam afterwards; tune it again, only when its back points lie more than 1 mm apart (the hard requirement; the window is the same, at most 1 mm below its length). The loop then checks the crossing again.
                if(!changed&&!fallback){foreach(int v in crossed.ToArray()){crossed.Remove(v);if(Tune(v,1.0))changed=true;}}
                if(!changed)break;
            }
            if(dropThin.Count>0)
            {
                var thinFaces=new HashSet<int>();foreach(int f in active)if(faces[f].V.Any(dropThin.Contains))thinFaces.Add(f);
                omittedThin+=dropThin.Count;omittedFaces+=thinFaces.Count;
                foreach(int v in dropThin)omittedReason.Add(dropReason[v]);
                foreach(int f in thinFaces){omittedFaceReason.Add(dropReason[faces[f].V.First(v=>dropReason.ContainsKey(v))]);omittedArea+=Newell(faces[f].V.Select(i=>points[i]).ToArray()).Length()*.5;}
                active.RemoveAll(thinFaces.Contains);continue;
            }
            // Settled. Count the shortened vertices (any key of a vertex below its intended length: clamped or shortened).
            var shortenedVertices=new HashSet<int>();maxShorten=0;
            foreach(var c in corners){double cut=start[c.Key]-length[c.Key];if(cut>.01){shortenedVertices.Add(c.V);maxShorten=Math.Max(maxShorten,cut);}}
            shortened=shortenedVertices.Count;
            used=around.Keys.OrderBy(v=>v).ToArray();
            var limitReason=used.Select(v=>string.Join("; ",(clampReason.TryGetValue(v,out var clamps)?clamps:new List<string>()).Concat(corners.Where(c=>c.V==v&&reason.ContainsKey(c.Key)).Select(c=>reason[c.Key])).Distinct().Concat(overlapPoints.Contains(v)?new[]{"starts inside a shallow neighbouring wall"}:Array.Empty<string>()))).ToArray();
            return Build(faces,active,corners,used,points,front,dir,length,intended,profile,thickness,back,fallback,toPlate,limits,gunFallback,request,
                shortened,maxShorten,omittedHole,omittedBehind,omittedGun,omittedAngle,omittedThin,omittedFaces,panelsTotal,limitReason,omittedReason.ToArray(),
                eligible,cutFaces,cutLost,cutLostArea,clampedBand,clampedLane,reAimed,omittedFaceReason.ToArray(),clampedRear,omittedArea);
        }
    }

    /// <summary>Sutherland-Hodgman cut of one face by the lane edge plane x = edge, keeping the side where side*(x - edge) >= 0. Cut points are canonical, so neighbours share them.</summary>
    internal static (Vector3[] P,ushort[] W) Clip(Vector3[] corners,ushort[] wall,float edge,int side)
    {
        var outP=new List<Vector3>();var outW=new List<ushort>();int n=corners.Length;
        bool Inside(Vector3 p)=>side*(p.X-edge)>=-1e-7f;
        for(int i=0;i<n;i++)
        {
            Vector3 a=corners[i],b=corners[(i+1)%n];ushort wa=wall[i],wb=wall[(i+1)%n];bool ia=Inside(a),ib=Inside(b);
            if(ia){outP.Add(a);outW.Add(wa);}
            if(ia!=ib)
            {
                // Canonical order of the two endpoints, so the neighbour face computes the identical cut point.
                bool swap=a.X>b.X||a.X==b.X&&(a.Y>b.Y||a.Y==b.Y&&a.Z>b.Z);
                Vector3 p=swap?b:a,q=swap?a:b;ushort wp=swap?wb:wa,wq=swap?wa:wb;
                float t=(edge-p.X)/(q.X-p.X);var cut=p+(q-p)*t;cut.X=edge;
                outP.Add(cut);outW.Add((ushort)Math.Round(wp+(wq-wp)*t,MidpointRounding.AwayFromZero));
            }
        }
        // Drop repeated points (a corner exactly on the edge produces its own copy).
        var P=new List<Vector3>();var W=new List<ushort>();
        for(int i=0;i<outP.Count;i++){if(P.Count>0&&Vector3.Distance(P[^1],outP[i])<1e-7f)continue;if(i==outP.Count-1&&P.Count>0&&Vector3.Distance(P[0],outP[i])<1e-7f)continue;P.Add(outP[i]);W.Add(outW[i]);}
        return (P.ToArray(),W.ToArray());
    }

    private static CheekShellResult Build(List<Face> faces,List<int> active,List<(int F,int J,int V,int Key,Vector3 D,double Cos,int Side)> corners,int[] used,
        List<Vector3> points,Dictionary<int,Vector3> front,Dictionary<int,Vector3> dir,Dictionary<int,double> length,Dictionary<int,double> intended,Dictionary<int,int> profile,
        Dictionary<int,ushort[]> thickness,Dictionary<int,Vector3[]> back,bool fallback,Func<Vector3,Vector3>? toPlate,CheekSettings limits,bool gunFallback,CheekShellRequest request,
        int shortened,double maxShorten,int omittedHole,int omittedBehind,int omittedGun,int omittedAngle,int omittedThin,int omittedFaces,int panels,string[] limitReason,string[] omittedReason,
        int eligible,int cutFaces,int cutLost,double cutLostArea,int clampedBand,int clampedLane,int reAimed,string[] omittedFaceReason,int clampedRear,double omittedArea)
    {
        var remap=used.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i);
        var vertices=used.Select(v=>front[v]).ToList();var edges=new List<int>();var edgeSet=new HashSet<(int,int)>();
        foreach(int f in active)
        {
            var v=faces[f].V;
            for(int e=0;e<v.Length;e++){int a=remap[v[e]],b=remap[v[(e+1)%v.Length]];if(edgeSet.Add((Math.Min(a,b),Math.Max(a,b)))){edges.Add(a);edges.Add(b);}}
        }
        var ribEdge=new Dictionary<int,int>();var ribEnd=new Vector3[used.Length];var ribLength=new double[used.Length];
        for(int i=0;i<used.Length;i++)
        {
            int v=used[i];double l=fallback?corners.Where(c=>c.V==v).Average(c=>length[c.Key]):length[v];ribLength[i]=l;
            ribEnd[i]=front[v]+(fallback?-faces[corners.First(c=>c.V==v).F].N:dir[v])*(float)(l*.001);
            if(fallback)continue;
            // The helper point lies on the intended back point, inside the turret; the rib starts at the corner's own vertex (native requirement).
            ribEdge[v]=edges.Count/2;edges.Add(remap[v]);edges.Add(vertices.Count);vertices.Add(ribEnd[i]);
        }
        if(edges.Count/2>=65535)throw new InvalidOperationException("The turret has too many edges for linked ribs.");
        var outFaces=new List<object>();var faceIds=new List<int[]>();var normals=new List<Vector3>();var outThickness=new List<ushort[]>();var outBack=new List<Vector3[]>();var faceWall=new List<ushort[]>();var sources=new List<int>();
        foreach(int f in active)
        {
            var v=faces[f].V.Select(id=>remap[id]).ToArray();ulong tm=0,te=0;
            if(!fallback)for(int j=0;j<v.Length;j++){tm|=(ulong)AlongEdgeManual<<(8*j);te|=(ulong)ribEdge[faces[f].V[j]]<<(16*j);}
            outFaces.Add(new{v,t=thickness[f],tm,te});faceIds.Add(v);normals.Add(faces[f].N);outThickness.Add(thickness[f]);outBack.Add(back[f]);faceWall.Add(faces[f].W);sources.Add(faces[f].Source);
        }
        Vector3 Map(Vector3 p)=>toPlate?.Invoke(p)??p;
        var json=JsonSerializer.Serialize(new{majorVersion=0,minorVersion=3,vertices=vertices.SelectMany(p=>{var q=Map(p);return new[]{q.X,q.Y,q.Z};}),edges,edgeFlags=Enumerable.Repeat(0,edges.Count/2),faces=outFaces});
        int ribs=fallback?0:used.Length,cornerCount=outThickness.Sum(t=>t.Length);
        var summary=(fallback?FallbackNote+" ":"")+$"Lines the inside of the turret front with {outFaces.Count} faces on {used.Length} turret points"+(fallback?"":$" and {ribs} ribs")+
            $"; {request.GunMm:0.#} mm at the gun, {request.OuterMm:0.#} mm at the outer edge. "+
            (shortened>0?$"{shortened} corners were shortened to fit, by up to {maxShorten:0.#} mm. ":"All corners keep the chosen thickness. ")+
            (cutFaces>0?$"{cutFaces} wall face{(cutFaces==1?"":"s")} cut at the gun lane. ":"")+
            LeftOutLine(omittedFaces+cutLost,omittedFaceReason.Concat(Enumerable.Repeat("cut sliver",cutLost)))+
            (omittedHole>0?$"{omittedHole} faces with holes skipped. ":"")+
            (gunFallback?"Gun size unavailable; using a 700 mm centre opening. ":"")+"Undo removes this change in one step.";
        var backPlate=outBack.Select(b=>b.Select(Map).ToArray()).ToArray();
        var plan=new ComposerMeshPlan(json,ComposerFill.Read(json),0,0,shortened,summary,true){Action=ComposerMeshAction.CheekShell,Total=cornerCount,MaximumMm=limits.MaximumMm,BackCorners=backPlate,CopyCheck=copy=>VerifyCopy(json,copy)};
        return new(plan,used.Select(v=>points[v]).ToArray(),used.Select(v=>front[v]).ToArray(),used.Select(v=>fallback?-faces[corners.First(c=>c.V==v).F].N:dir[v]).ToArray(),ribEnd,ribLength,
            used.Select(v=>fallback?(double)profile[v]:intended[v]).ToArray(),used.Select(v=>profile[v]).ToArray(),faceIds.ToArray(),normals.ToArray(),outThickness.ToArray(),outBack.ToArray(),
            ribs,shortened,maxShorten,omittedHole,omittedBehind,omittedGun,omittedAngle,omittedThin,omittedFaces,panels,fallback,limitReason,omittedReason,
            eligible,cutFaces,cutLost,cutLostArea,clampedBand,clampedLane,reAimed,faceWall.ToArray(),sources.ToArray(),omittedFaceReason,clampedRear,omittedArea);
    }

    /// <summary>5 (0.22.58): "N wall faces were left out: reason (count), ..." in plain English; empty when nothing was left out.</summary>
    internal static string LeftOutLine(int count,IEnumerable<string> reasons)
    {
        if(count<=0)return "";
        static string Plain(string reason)=>reason switch
        {
            "next wall"=>"no room behind them","gun lane"=>"no room beside the gun","floor"=>"no room above the floor","roof"=>"no room below the roof",
            "rear limit"=>"no room before the rear limit","rib angle"=>"no rib direction keeps 10 degrees from the wall","crossing ribs"=>"their ribs would cross",
            "cut sliver"=>"only a sliver remains beside the gun",_=>"less than 3 mm left"
        };
        var groups=reasons.GroupBy(Plain).Select(g=>$"{g.Key} ({g.Count()})");
        return $"{count} wall face{(count==1?" was":"s were")} left out: {string.Join(", ",groups)}. ";
    }
    /// <summary>Faces whose normals lie within <paramref name="cosPanel"/> of a seed share one panel; a panel's normal is the normalised sum.</summary>
    internal static List<Vector3> Panels(IReadOnlyList<Vector3> normals,double cosPanel)
    {
        var seeds=new List<Vector3>();var sums=new List<Vector3>();
        foreach(var n in normals){int k=seeds.FindIndex(s=>Vector3.Dot(s,n)>=cosPanel);if(k<0){seeds.Add(n);sums.Add(n);}else sums[k]+=n;}
        return sums.Select(Vector3.Normalize).ToList();
    }
    internal static Vector3 Newell(IReadOnlyList<Vector3> p){var s=Vector3.Zero;for(int i=0;i<p.Count;i++)s+=Vector3.Cross(p[i],p[(i+1)%p.Count]);return s;}
    /// <summary>
    /// The nearest wall the ray runs INTO: triangles are oriented with their free-space normal (the built surfaces as the tessellator stores them),
    /// so leaving a wall slab the rib starts in does not count, entering a wall or leaving the turret does. Same Moller-Trumbore test as ComposerFill.Ray.
    /// </summary>
    internal static double? RayEntering(Vector3 p,Vector3 direction,IReadOnlyList<FillTriangle> walls,double maximumM)
    {
        var d=Vector3.Normalize(direction);
        var entering=new List<FillTriangle>(walls.Count);
        foreach(var t in walls)if(Vector3.Dot(d,Vector3.Cross(t.B-t.A,t.C-t.A))<0)entering.Add(t);
        return entering.Count==0?null:ComposerFill.Ray(p,d,entering,maximumM);
    }
    /// <summary>
    /// EditMeshBuilding.ThickenAlongEdge (GameAssembly 0.2.56.2, RVA 0x1E441E0): u = unit(edge end - vertex); d = n.u; when d is above sin(-1 deg)
    /// the game falls back to the face normal, otherwise back = v + u * (t/-d) + 0.25 mm along the in-plane corner bisector (unit(v-next)+unit(v-prev)).
    /// </summary>
    internal static Vector3 NativeBack(Vector3 v,Vector3 next,Vector3 prev,Vector3 u,ushort t,Vector3 n)
    {
        double d=Vector3.Dot(n,u);
        if(d>SinOneDegree)return v-n*(float)(t*.001);
        return v+u*(float)(t*.001/-d)+Offset(v,next,prev);
    }
    /// <summary>The native in-plane offset: 0.25 mm along unit(unit(v-next)+unit(v-prev)); zero for a degenerate corner.</summary>
    internal static Vector3 Offset(Vector3 v,Vector3 next,Vector3 prev)
    {
        var a=v-next;var b=v-prev;
        if(a.LengthSquared()<=0||b.LengthSquared()<=0)return Vector3.Zero;
        var bis=Vector3.Normalize(a)+Vector3.Normalize(b);
        return bis.Length()>1e-5f?Vector3.Normalize(bis)*(float)NativeOffsetM:Vector3.Zero;
    }
    /// <summary>
    /// The saved JSON of the native copy must still carry every front and helper point, each rib edge at its planned index with the planned
    /// endpoints, and each face's tm/te token text. Null when it does; otherwise the plain-language refusal (nothing is changed).
    /// </summary>
    internal static string? VerifyCopy(string planned,string copy)
    {
        try
        {
            using var a=JsonDocument.Parse(planned);using var b=JsonDocument.Parse(copy);
            var va=a.RootElement.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();var vb=b.RootElement.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            if(vb.Length<va.Length)return LinkedBackMarker+" (helper points were dropped)";
            for(int i=0;i<va.Length;i++)if(Math.Abs(va[i]-vb[i])>1e-5f)return LinkedBackMarker+" (a vertex moved)";
            var ea=a.RootElement.GetProperty("edges").EnumerateArray().Select(x=>x.GetInt32()).ToArray();var eb=b.RootElement.GetProperty("edges").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            if(eb.Length<ea.Length)return LinkedBackMarker+" (rib edges were dropped)";
            for(int i=0;i<ea.Length;i+=2)if(!(ea[i]==eb[i]&&ea[i+1]==eb[i+1]||ea[i]==eb[i+1]&&ea[i+1]==eb[i]))return LinkedBackMarker+$" (edge {i/2} changed)";
            var fa=a.RootElement.GetProperty("faces").EnumerateArray().ToArray();var fb=b.RootElement.GetProperty("faces").EnumerateArray().ToArray();
            if(fa.Length!=fb.Length)return LinkedBackMarker+" (face count changed)";
            static string Token(JsonElement face,string key)=>face.TryGetProperty(key,out var p)?p.GetRawText().Trim():"0";
            for(int i=0;i<fa.Length;i++)
            {
                if(!fa[i].GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(fb[i].GetProperty("v").EnumerateArray().Select(x=>x.GetInt32())))return LinkedBackMarker+$" (face {i+1} corners changed)";
                if(!fa[i].GetProperty("t").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(fb[i].GetProperty("t").EnumerateArray().Select(x=>x.GetInt32())))return LinkedBackMarker+$" (face {i+1} thickness changed)";
                if(Token(fa[i],"tm")!=Token(fb[i],"tm"))return LinkedBackMarker+$" (face {i+1} direction mode changed)";
                if(Token(fa[i],"te")!=Token(fb[i],"te"))return LinkedBackMarker+$" (face {i+1} rib slot changed)";
            }
            return null;
        }
        catch(Exception ex){return LinkedBackMarker+" (the copy could not be checked: "+ex.Message+")";}
    }
    internal static string Invariant(double value)=>value.ToString(CultureInfo.InvariantCulture);
}
