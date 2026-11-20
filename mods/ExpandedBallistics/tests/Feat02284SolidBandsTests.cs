using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using F83=SprocketExpandedBallistics.Feat02283SolidOverlayTests;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.84: the layer view (Ctrl+L) of a plate marked [Solid block] shows the layers of the recipe as bands from the front to the back of the block, along the front of
/// the vehicle (SolidBlockOverlay.ApplyBands, called from RuntimeLayerFaces.SolidOverlay). The native surfaces are the per-face stand-in of FEAT74 through the production
/// CassetteNativeOverlay.Copy (as FEAT83). Checks: 0 &lt;= d &lt;= 1 on every point, d about 0 on the front and about 1 on the back (front and back told by an independent
/// inside test), the band boundaries on the side walls of the straight boxes at the fractions of the recipe (within 1 % of the local depth), an independent per-point depth on
/// Object 292, the time per rebuild, the fallbacks (one colour of 0.22.83, bit for bit), the number of computations per poll, and plates without the mark against the 0.22.83
/// code itself (its compiled test assembly, by reflection).
/// </summary>
internal static class Feat02284SolidBandsTests
{
    /// <summary>The production files 0.22.84 may change on top of 0.22.83.</summary>
    internal static bool IsPlanned(string rel)=>rel is "RuntimeComposerFill.cs" or "SolidBlockOverlay.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    private static string F(double v,string f="0.###")=>v.ToString(f,CultureInfo.InvariantCulture);

    /// <summary>The glue of RuntimeLayerFaces.Overlay in 0.22.84: a marked plate goes to ApplyBands (with the front of the vehicle in the frame of the mesh), any other plate as before.</summary>
    internal static OverlayCopy? Overlay84(string? name,OverlayCopy data,string json,CassettePreset? preset,bool painted,Vector3? forward,SolidBlockOverlay.Report? report=null)
        =>CompositePlate.IsSolidBlock(name)?SolidBlockOverlay.ApplyBands(data,json,preset,forward,report):F83.Overlay83(name,data,json,preset,painted);

    // ---------------- independent per-point depth (all fan triangles of the drawn faces, six lines 0.5 mm beside the point, full-line crossings) ----------------
    private static double IndependentD(Vector3 p,Vector3[] pts,int[][] faces,Vector3 f)=>IndependentSpan(p,pts,faces,f).D;
    /// <summary>The independent d and the local depth of the block (m) along the line through p.</summary>
    private static (double D,double Depth) IndependentSpan(Vector3 p,Vector3[] pts,int[][] faces,Vector3 f)
    {
        var a0=Math.Abs(f.X)<.6f?Vector3.UnitX:Vector3.UnitY;var e1=Vector3.Normalize(Vector3.Cross(f,a0));var e2=Vector3.Cross(f,e1);var found=new List<double>();var depths=new List<double>();
        for(int k=0;k<6;k++)
        {
            double ang=(k*60+31)*Math.PI/180;var o=p+(e1*(float)Math.Cos(ang)+e2*(float)Math.Sin(ang))*.0005f;var ts=new List<double>();
            foreach(var face in faces)for(int j=1;j+1<face.Length;j++)if(Line(o,f,pts[face[0]],pts[face[j]],pts[face[j+1]]) is {} t)ts.Add(-t);
            ts.Sort();var u=new List<double>();foreach(var t in ts)if(u.Count==0||t-u[^1]>1e-7)u.Add(t);
            if(u.Count%2==1)continue;double gap=double.PositiveInfinity,a=0,b=0;
            for(int i=0;i+1<u.Count;i+=2){double g=0<u[i]?u[i]:0>u[i+1]?-u[i+1]:0;if(g<gap){gap=g;a=u[i];b=u[i+1];}}
            if(gap>.002||b-a<1e-6)continue;found.Add(Math.Clamp((0-a)/(b-a),0,1));depths.Add(b-a);
        }
        if(found.Count==0)return (double.NaN,double.NaN);found.Sort();depths.Sort();return (found[found.Count/2],depths[depths.Count/2]);
    }
    /// <summary>The parameter t of the crossing of the full line o + t d with triangle abc (double precision), or null.</summary>
    private static double? Line(Vector3 o,Vector3 d,Vector3 a,Vector3 b,Vector3 c)
    {
        double e1x=b.X-a.X,e1y=b.Y-a.Y,e1z=b.Z-a.Z,e2x=c.X-a.X,e2y=c.Y-a.Y,e2z=c.Z-a.Z;
        double px=d.Y*e2z-d.Z*e2y,py=d.Z*e2x-d.X*e2z,pz=d.X*e2y-d.Y*e2x;double det=e1x*px+e1y*py+e1z*pz;if(Math.Abs(det)<1e-14)return null;
        double inv=1/det,tx=o.X-a.X,ty=o.Y-a.Y,tz=o.Z-a.Z;double u=(tx*px+ty*py+tz*pz)*inv;if(u<0||u>1)return null;
        double qx=ty*e1z-tz*e1y,qy=tz*e1x-tx*e1z,qz=tx*e1y-ty*e1x;double v=(d.X*qx+d.Y*qy+d.Z*qz)*inv;if(v<0||u+v>1)return null;
        return (e2x*qx+e2y*qy+e2z*qz)*inv;
    }

    // ---------------- the picture: every drawn triangle cut at the band boundaries, as the point-sampled strip shows it ----------------
    private static List<(Vector3 P,double D)> Clip(List<(Vector3 P,double D)> poly,Func<double,double> signed)
    {
        var output=new List<(Vector3 P,double D)>();if(poly.Count==0)return output;var prev=poly[^1];double before=signed(prev.D);
        foreach(var cur in poly)
        {
            double after=signed(cur.D);bool inside=after>=0,was=before>=0;
            if(inside!=was){double t=before/(before-after);output.Add((Vector3.Lerp(prev.P,cur.P,(float)t),prev.D+(cur.D-prev.D)*t));}
            if(inside)output.Add(cur);prev=cur;before=after;
        }
        return output;
    }
    private static void Draw(F83.Panel panel,OverlayCopy copy,CassettePreset? preset,Func<Vector3,Vector3> world,double x0,double y0,double w,double h,string caption,Vector3 viewDir)
    {
        var strip=CassetteDepthStrip.Build(preset);var levels=CassetteDepthStrip.Boundaries(preset);
        var view=Vector3.Normalize(viewDir);var up=Vector3.UnitY;var right=Vector3.Normalize(Vector3.Cross(up,view));var vup=Vector3.Cross(view,right);var light=Vector3.Normalize(new Vector3(.3f,.8f,.6f));
        var pts=copy.Vertices.Select(world).ToArray();
        (double X,double Y,double Z) Proj(Vector3 q)=>(Vector3.Dot(q,right),Vector3.Dot(q,vup),Vector3.Dot(q,view));
        var proj=pts.Select(Proj).ToArray();
        double minX=proj.Min(p=>p.X),maxX=proj.Max(p=>p.X),minY=proj.Min(p=>p.Y),maxY=proj.Max(p=>p.Y);double sc=Math.Min((w-20)/(maxX-minX),(h-34)/(maxY-minY));
        double ox=x0+10+((w-20)-(maxX-minX)*sc)/2;
        var tris=new List<(double Z,int T)>();
        for(int t=0;t+2<copy.Triangles.Length;t+=3){int a=copy.Triangles[t],b=copy.Triangles[t+1],c=copy.Triangles[t+2];if(F83.Area(pts[a],pts[b],pts[c])<1e-12)continue;tris.Add(((proj[a].Z+proj[b].Z+proj[c].Z)/3,t));}
        panel.Rect(x0,y0,w,h,"#0d1115");
        foreach(var (_,t) in tris.OrderByDescending(x=>x.Z))
        {
            int a=copy.Triangles[t],b=copy.Triangles[t+1],c=copy.Triangles[t+2];
            var n=Vector3.Normalize(Vector3.Cross(pts[b]-pts[a],pts[c]-pts[a]));double shade=.45+.55*Math.Abs(Vector3.Dot(n,light));
            var poly=new List<(Vector3 P,double D)>{(pts[a],copy.Uv[a].Y),(pts[b],copy.Uv[b].Y),(pts[c],copy.Uv[c].Y)};
            for(int k=0;k+1<levels.Length;k++)
            {
                double lo=levels[k],hi=levels[k+1];bool last=k+2==levels.Length;
                if(!last&&poly.All(q=>Math.Abs(q.D-hi)<1e-9))continue;
                var piece=Clip(Clip(poly,d=>d-lo),d=>hi-d);if(piece.Count<3)continue;
                uint colour=strip[Math.Clamp((int)((lo+hi)/2*CassetteDepthStrip.Height),0,CassetteDepthStrip.Height-1)];
                int R=(int)((colour>>24&0xff)*shade),G=(int)((colour>>16&0xff)*shade),B=(int)((colour>>8&0xff)*shade);var hex=$"#{R:x2}{G:x2}{B:x2}";
                panel.Poly(piece.Select(q=>{var r=Proj(q.P);return (ox+(r.X-minX)*sc,y0+h-10-(r.Y-minY)*sc);}),hex,hex,.5);
            }
        }
        panel.Text(x0+8,y0+12,caption,"#e8e2d4","MiddleLeft","#0d1115cc",8.5);
    }

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT84: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),parent=Fix02228Tests.SessionRoot(),preview=Path.Combine(output,"preview");
        Directory.CreateDirectory(preview);
        var catalogueText=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName));var presets=CassetteCatalogue.Parse(catalogueText).Presets;
        var userText=File.ReadAllText(Path.Combine(root,"tests/fixtures/object292-recipe.cassette.json"));var userPreset=CassetteCatalogue.Parse(userText).Presets.Single();
        var t80u=presets.First(p=>p.Id=="t80u");var burlington=presets.First(p=>p.Id=="burlington");
        var cases=new StringBuilder("plate\trecipe\tpieces\tdistinct points\tpoints from the nearest neighbour\tband ms (first)\tband ms (median of 5 more)\toverlay triangles\tnot drawn\tfront triangles\tmax d front\tback triangles\tmin d back\tside corners\tmax side error (of depth)\tband boundary points\tmax boundary error (of depth)\tindependent points\tmedian |d - independent|\t95th percentile\tshare within 0.05\n");
        var timing=new StringBuilder("plate\trun\tband ms\tApplyBands total ms\n");
        var edges=new StringBuilder("plate\tside\tcentre (mesh frame, m)\t|n.f|\td at the centre (mean of the corners)\tindependent d at the centre\tlocal depth mm\tcorner 1\tcorner 2\tcorner 3\n");
        var panels=new List<F83.Panel>();
        try
        {
            using var proof77=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/solid-block/Solid block proof.blueprint")));
            using var challenger=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/solid-block/Solid block proof - Challenger 1 Mk3.blueprint")));
            using var bp292=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-292-1721.blueprint")));
            using var bp293=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-293-2038.blueprint")));
            var (t80Json,_)=F83.Mesh(proof77.RootElement,4);var (boxJson,_)=F83.Mesh(challenger.RootElement,4);var (json292,_)=F83.Mesh(bp292.RootElement,22);var thin292=F83.Thin(json292,5);
            var objects292=bp292.RootElement.GetProperty("objects").EnumerateArray().ToDictionary(o=>o.GetProperty("vuid").GetInt32(),o=>o);var (rot292,pos292)=F83.World(objects292,objects292[323]);
            var inv292=new[]{rot292[0],rot292[3],rot292[6],rot292[1],rot292[4],rot292[7],rot292[2],rot292[5],rot292[8]};
            var forward292=Vector3.Normalize(F83.Mul(inv292,Vector3.UnitZ));
            // the proof boxes sit without rotation in the vehicle (every plate object of both blueprints has rot 0,0,0): the front of the vehicle is +Z in their mesh frame
            var plates=new (string Tag,string Json,CassettePreset Preset,Vector3 Forward,Func<Vector3,Vector3> World,bool Straight)[]{
                ("Challenger 1 box (mesh 4)",boxJson,burlington,Vector3.UnitZ,p=>p,true),("T-80U proof box (mesh 4)",t80Json,t80u,Vector3.UnitZ,p=>p,true),
                ("Object 292 mesh 22, Thickness only 5 mm + Solid block",thin292,userPreset,forward292,p=>F83.Mul(rot292,p)+pos292,false)};
            var drawn=new Dictionary<string,(OverlayCopy One,OverlayCopy Bands)>();
            // ================= 1. the bands on the three proof plates =================
            foreach(var (tag,json,preset,forward,world,straight) in plates)
            {
                var s=F83.StandIn(json);var data=CassetteNativeOverlay.Copy(s.Front,s.Back,s.Sides,preset);
                var one=SolidBlockOverlay.Apply(data,json,preset,new SolidBlockOverlay.Report());
                var report=new SolidBlockOverlay.Report();var total=System.Diagnostics.Stopwatch.StartNew();
                var result=Overlay84(CompositePlate.SolidBlockName,data,json,preset,false,forward,report)!;total.Stop();
                double first=report.BandMs;timing.AppendLine($"{tag}\t1\t{F(first,"0.###")}\t{F(total.Elapsed.TotalMilliseconds,"0.###")}");
                var more=new List<double>();
                for(int k=2;k<=6;k++){var r=new SolidBlockOverlay.Report();var w=System.Diagnostics.Stopwatch.StartNew();var again=Overlay84(CompositePlate.SolidBlockName,data,json,preset,false,forward,r)!;w.Stop();more.Add(r.BandMs);timing.AppendLine($"{tag}\t{k}\t{F(r.BandMs,"0.###")}\t{F(w.Elapsed.TotalMilliseconds,"0.###")}");if(k==2)Check(F83.Hash(again)==F83.Hash(result),$"{tag}: the same bands on every rebuild (deterministic)");}
                more.Sort();double median=more[2];
                Console.WriteLine($"FEAT84 timing: {tag}: {data.Triangles.Length/3} triangles, {report.BandPoints} points, band depth {F(first,"0.#")} ms first, {F(median,"0.#")} ms median");
                drawn[tag]=(one,result);
                Check(report.Bands&&report.BandRefusal==null,$"{tag}: bands made ({report.BandPoints} distinct points, {report.BandPieces} piece(s), {report.BandNeighbour} from the nearest point)");
                Check(result.Vertices.SequenceEqual(one.Vertices)&&result.Normals.SequenceEqual(one.Normals)&&result.Triangles.SequenceEqual(one.Triangles)&&result.FallbackTriangles==one.FallbackTriangles&&result.SideArea==one.SideArea&&result.FallbackArea==one.FallbackArea,
                    $"{tag}: points, normals, triangles and the triangles off the drawn surface ({report.Hidden}) are those of 0.22.83; only the depth coordinate differs");
                Check(result.Uv.All(q=>q.X==.5f&&float.IsFinite(q.Y)&&q.Y>=0&&q.Y<=1),$"{tag}: every point has 0 <= d <= 1 ({result.Uv.Length} points)");
                Check(Overlay84(CompositePlate.SolidBlockName,data,json,preset,true,forward) is {} painted&&F83.Hash(painted)==F83.Hash(result),$"{tag}: the look of the layer box (layers or painted) does not matter for a Solid block");
                var (p,faces)=F83.Read(json);
                // front and back: the centre of a drawn triangle facing (anti)parallel to the front, told by an independent inside test 1 mm ahead and 1 mm behind
                int fronts=0,backs=0,frontsOff=0,backsOff=0,cornersChecked=0;double maxFront=0,minBack=1,frontMm=0,backMm=0,cornerMm=0,cornerErrMm=0;string worstFront="",worstBack="";
                int sideCorners=0,boundaryPoints=0;double maxSide=0,maxBoundary=0;
                double sFront=p.Min(q=>-Vector3.Dot(q,forward)),sBack=p.Max(q=>-Vector3.Dot(q,forward));var bounds=CassetteDepthStrip.Boundaries(preset);
                var drawnPoints=new HashSet<int>();
                for(int t=0;t+2<result.Triangles.Length;t+=3)
                {
                    int a=result.Triangles[t],b=result.Triangles[t+1],c=result.Triangles[t+2];if(a==b&&b==c)continue;
                    var pa=result.Vertices[a];var pb=result.Vertices[b];var pc=result.Vertices[c];var cross=Vector3.Cross(pb-pa,pc-pa);if(cross.Length()<1e-10f)continue;
                    drawnPoints.Add(a);drawnPoints.Add(b);drawnPoints.Add(c);
                    var n=Vector3.Normalize(cross);double along=Math.Abs(Vector3.Dot(n,forward));var centre=(pa+pb+pc)/3;double dc=(result.Uv[a].Y+result.Uv[b].Y+result.Uv[c].Y)/3;
                    if(along>.5)
                    {
                        bool ahead=F83.InsideIndependent(centre+forward*.001f,p,faces),behind=F83.InsideIndependent(centre-forward*.001f,p,faces);
                        double span=(!ahead&&behind)||(ahead&&!behind)?IndependentSpan(centre,p,faces,forward).Depth:double.NaN;
                        if(!ahead&&behind&&dc>.02){frontsOff++;frontMm=Math.Max(frontMm,dc*span*1000);}
                        if(ahead&&!behind&&dc<.98){backsOff++;backMm=Math.Max(backMm,(1-dc)*span*1000);}
                        if((!ahead&&behind&&dc>.02)||(ahead&&!behind&&dc<.98))
                        {
                            string Corner(int i){var (di,si)=IndependentSpan(result.Vertices[i],p,faces,forward);return $"{F(result.Uv[i].Y,"0.###")} (independent {F(di,"0.###")}, depth {F(si*1000,"0.#")} mm)";}
                            var (dCentre,_)=IndependentSpan(centre,p,faces,forward);
                            edges.AppendLine(string.Join("\t",tag,!ahead?"front":"back",$"{centre.X:0.####} {centre.Y:0.####} {centre.Z:0.####}",F(along,"0.##"),F(dc,"0.###"),F(dCentre,"0.###"),F(span*1000,"0.#"),Corner(a),Corner(b),Corner(c)));
                        }
                        if(!ahead&&behind||ahead&&!behind)foreach(var i in new[]{a,b,c})
                        {
                            // every corner: its distance from the front (or the back) along its own line through the block, d x the local depth there
                            var (di,si)=IndependentSpan(result.Vertices[i],p,faces,forward);if(double.IsNaN(si))continue;
                            cornersChecked++;cornerMm=Math.Max(cornerMm,(!ahead?result.Uv[i].Y:1-result.Uv[i].Y)*si*1000);cornerErrMm=Math.Max(cornerErrMm,Math.Abs(result.Uv[i].Y-di)*si*1000);
                        }
                        if(!ahead&&behind){fronts++;if(dc>maxFront){maxFront=dc;worstFront=$"centre {centre}, corners d {F(result.Uv[a].Y,"0.###")}/{F(result.Uv[b].Y,"0.###")}/{F(result.Uv[c].Y,"0.###")}, |n.f| {F(along,"0.##")}, independent at the centre {F(IndependentD(centre,p,faces,forward),"0.###")}";}}
                        else if(ahead&&!behind){backs++;if(dc<minBack){minBack=dc;worstBack=$"centre {centre}, corners d {F(result.Uv[a].Y,"0.###")}/{F(result.Uv[b].Y,"0.###")}/{F(result.Uv[c].Y,"0.###")}, |n.f| {F(along,"0.##")}, independent at the centre {F(IndependentD(centre,p,faces,forward),"0.###")}";}}
                    }
                    if(straight&&along<.05)
                    {
                        // a side wall of a straight box: d against the place between the front and the back plane, and the iso-lines of the recipe boundaries
                        double Expected(Vector3 q)=>(-Vector3.Dot(q,forward)-sFront)/(sBack-sFront);
                        foreach(var i in new[]{a,b,c}){sideCorners++;maxSide=Math.Max(maxSide,Math.Abs(result.Uv[i].Y-Expected(result.Vertices[i])));}
                        var corners=new[]{(pa,(double)result.Uv[a].Y),(pb,(double)result.Uv[b].Y),(pc,(double)result.Uv[c].Y)};
                        foreach(var level in bounds[1..^1])
                            for(int e=0;e<3;e++)
                            {
                                var (q0,d0)=corners[e];var (q1,d1)=corners[(e+1)%3];if((d0-level)*(d1-level)>0||d0==d1)continue;
                                var q=Vector3.Lerp(q0,q1,(float)((level-d0)/(d1-d0)));boundaryPoints++;maxBoundary=Math.Max(maxBoundary,Math.Abs(Expected(q)-level));
                            }
                    }
                }
                Check(fronts>0&&backs>0&&cornersChecked>0&&cornerMm<=(straight?.001:5.5)&&cornerErrMm<=1,$"{tag}: the front faces ({fronts} triangles) have d about 0 and the back faces ({backs}) d about 1: every corner ({cornersChecked}) lies within {F(cornerMm,"0.###")} mm of the front (back) along its own line through the block (limit {(straight?"0.001 mm":"5.5 mm: the corners of the 5 mm skin")}); its d is that of an independent line within {F(cornerErrMm,"0.###")} mm (limit 1 mm)");
                Console.WriteLine($"FEAT84 front/back: {tag}: centre of a triangle (mean of its corners): {fronts-frontsOff} of {fronts} front triangles d <= 0.02 (others up to {F(frontMm,"0.#")} mm, largest d {F(maxFront,"0.###")}), {backs-backsOff} of {backs} back triangles d >= 0.98 (others up to {F(backMm,"0.#")} mm, smallest d {F(minBack,"0.###")})");
                if(straight)
                {
                    Check(frontsOff==0&&backsOff==0,$"{tag}: a straight box: every front triangle d <= 0.02 and every back triangle d >= 0.98");
                    Check(sideCorners>0&&maxSide<=.01,$"{tag}: on the side walls d follows the place between front and back ({sideCorners} corners, largest error {F(maxSide*100,"0.###")} % of the depth {F((sBack-sFront)*1000,"0")} mm)");
                    Check(boundaryPoints>=4*(bounds.Length-2)&&maxBoundary<=.01,$"{tag}: the band boundaries on the side walls lie at the fractions of the recipe ({bounds.Length-2} boundaries, {boundaryPoints} points, largest error {F(maxBoundary*100,"0.###")} % of the depth)");
                }
                // an independent depth per drawn point
                var diffs=new List<double>();
                foreach(var i in drawnPoints){var ind=IndependentD(result.Vertices[i],p,faces,forward);if(!double.IsNaN(ind))diffs.Add(Math.Abs(ind-result.Uv[i].Y));}
                diffs.Sort();double med=diffs.Count>0?diffs[diffs.Count/2]:double.NaN,p95=diffs.Count>0?diffs[(int)(diffs.Count*.95)]:double.NaN,share=diffs.Count>0?diffs.Count(x=>x<=.05)/(double)diffs.Count:0;
                Check(diffs.Count>=drawnPoints.Count*.8&&share>=.9,$"{tag}: an independent depth (six other lines, all faces) agrees within 0.05 on {F(share*100,"0.#")} % of {diffs.Count} drawn points (median difference {F(med,"0.####")}, 95th percentile {F(p95,"0.####")})");
                if(tag.StartsWith("Object 292",StringComparison.Ordinal))Check(median<=50,$"{tag}: the depth per point costs {F(median,"0.#")} ms per rebuild (first {F(first,"0.#")} ms; limit 50 ms)");
                cases.AppendLine(string.Join("\t",tag,preset.Id,report.BandPieces,report.BandPoints,report.BandNeighbour,F(first,"0.###"),F(median,"0.###"),report.Triangles,report.Hidden,fronts,F(maxFront,"0.####"),backs,F(minBack,"0.####"),straight?sideCorners.ToString(CultureInfo.InvariantCulture):"-",straight?F(maxSide,"0.######"):"-",straight?boundaryPoints.ToString(CultureInfo.InvariantCulture):"-",straight?F(maxBoundary,"0.######"):"-",diffs.Count,F(med,"0.####"),F(p95,"0.####"),F(share,"0.###")));
            }

            // ================= 2. the fallbacks: the one colour of 0.22.83, bit for bit; nothing thrown =================
            {
                var (_,json,preset,forward,_,_)=plates[0];var s=F83.StandIn(json);var data=CassetteNativeOverlay.Copy(s.Front,s.Back,s.Sides,preset);
                string one=F83.Hash(SolidBlockOverlay.Apply(data,json,preset,new SolidBlockOverlay.Report()));
                foreach(var (label,f,mesh,expect) in new (string,Vector3?,string?,string)[]{("the mesh cannot be read now",forward,null,"could not be read"),("no front direction",null,json,"no front direction"),("a front direction of NaN",new Vector3(float.NaN,0,1),json,"no front direction"),("a front direction of length 0",Vector3.Zero,json,"no front direction")})
                {
                    var r=new SolidBlockOverlay.Report();OverlayCopy? got=null;Exception? thrown=null;try{got=SolidBlockOverlay.ApplyBands(data,mesh,preset,f,r);}catch(Exception e){thrown=e;}
                    string reference=mesh==null?F83.Hash(SolidBlockOverlay.Apply(data,null,preset,new SolidBlockOverlay.Report())):one;
                    Check(thrown==null&&got!=null&&F83.Hash(got)==reference&&!r.Bands&&r.BandRefusal!.Contains(expect,StringComparison.Ordinal),$"fallback, {label}: the one colour of 0.22.83 bit for bit, no exception; reason '{r.BandRefusal}'");
                }
                // the overlay does not match the mesh (moved 10 m away): no point lies on a line through the shape
                var moved=data with{Vertices=data.Vertices.Select(v=>v+new Vector3(10,0,0)).ToArray()};var rm=new SolidBlockOverlay.Report();
                var gm=SolidBlockOverlay.ApplyBands(moved,json,preset,forward,rm);
                Check(F83.Hash(gm)==F83.Hash(SolidBlockOverlay.Apply(moved,json,preset,new SolidBlockOverlay.Report()))&&!rm.Bands&&rm.BandRefusal!.Contains("no point",StringComparison.Ordinal),$"fallback, an overlay that does not fit the mesh: the one colour; reason '{rm.BandRefusal}'");
                // a marked plate that is not closed (Object 293 20:38): one colour, every triangle drawn, as 0.22.83
                var (json293,_)=F83.Mesh(bp293.RootElement,2);var s3=F83.StandIn(json293);var d3=CassetteNativeOverlay.Copy(s3.Front,s3.Back,s3.Sides,userPreset);var r3=new SolidBlockOverlay.Report();
                var g3=SolidBlockOverlay.ApplyBands(d3,json293,userPreset,Vector3.UnitZ,r3);
                Check(F83.Hash(g3)==F83.Hash(SolidBlockOverlay.Apply(d3,json293,userPreset,new SolidBlockOverlay.Report()))&&!r3.Bands&&r3.BandRefusal=="not a closed shape"&&r3.Hidden==0,"fallback, a marked plate that is not closed (Object 293 20:38): the one colour and every triangle, as 0.22.83");
            }

            // ================= 3. computed once per rebuild: a poll with the same signature computes 0 times =================
            {
                var visual84=File.ReadAllBytes(Path.Combine(root,"RuntimeCassettePlateVisual.cs")); // herpak: the byte comparison with src-0.22.83 is removed
                var text=Encoding.UTF8.GetString(visual84);int same=text.IndexOf("OverlayContent.Same(built,signature))",StringComparison.Ordinal),call=text.IndexOf("data=RuntimeLayerFaces.Overlay(plate,data)??data;",StringComparison.Ordinal);
                int ret=same<0?-1:text.Replace("\r\n","\n").IndexOf("            return;\n        }",text[..same].Replace("\r\n","\n").Length,StringComparison.Ordinal);call=text.Replace("\r\n","\n").IndexOf("data=RuntimeLayerFaces.Overlay(plate,data)??data;",StringComparison.Ordinal);same=text[..same].Replace("\r\n","\n").Length;
                Check(same>0&&ret>same&&call>ret,"in Rebuild the overlay (and so the band depth) is only made after the cached view with the same signature has returned");
                var production=Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories).Where(f=>!f.Contains(Path.DirectorySeparatorChar+"tests"+Path.DirectorySeparatorChar)&&!f.Contains(Path.DirectorySeparatorChar+"bin"+Path.DirectorySeparatorChar)&&!f.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar)).ToArray();
                int callsBands=production.Sum(f=>System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f),@"SolidBlockOverlay\.ApplyBands\(").Count);
                int callsSolid=production.Sum(f=>System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f),@"[^a-zA-Z.]SolidOverlay\(plate,data\)").Count);
                int callsOverlay=production.Sum(f=>System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f),@"RuntimeLayerFaces\.Overlay\(").Count);
                Check(callsBands==1&&callsSolid==1&&callsOverlay==1,$"one call each: ApplyBands only in SolidOverlay ({callsBands}), SolidOverlay only in Overlay ({callsSolid}), Overlay only in Rebuild ({callsOverlay})");
                // the cache of the overlay, replayed (as FEAT83), with the real glue on the Challenger box at every build
                var (_,json,preset,forward,_,_)=plates[0];var s=F83.StandIn(json);var data=CassetteNativeOverlay.Copy(s.Front,s.Back,s.Sides,preset);
                var queue=new OverlayBuildQueue<int>();var cache=new Dictionary<int,string>();float now=0;var presetText=JsonSerializer.Serialize(preset);int builds=0;
                string Sig(string? name)=>OverlayContent.Signature("mesh-content-A","ebCassette_burlington",presetText)+(CompositePlate.IsSolidBlock(name)?SolidBlockOverlay.Mark:"");
                (int Builds,int Bands) Step(string? name,int polls=3)
                {
                    int b0=builds,c0=SolidBlockOverlay.BandComputations;var current=Sig(name);
                    for(int k=0;k<polls;k++){queue.Schedule(1,1,now,0);now+=1.1f;foreach(var job in queue.TakeDue(now,8)){if(cache.TryGetValue(1,out var old)&&OverlayContent.Same(old,current))continue;cache[1]=current;builds++;Overlay84(name,data,json,preset,false,forward);}}
                    return (builds-b0,SolidBlockOverlay.BandComputations-c0);
                }
                var steps=new (string Label,string? Name,int Builds,int Bands)[]{("first draw, no mark",CompositePlate.Name,1,0),("polls, no change",CompositePlate.Name,0,0),("Apply Solid block (only the mark)",CompositePlate.SolidBlockName,1,1),("polls, no change",CompositePlate.SolidBlockName,0,0),("Undo (mark off)",CompositePlate.Name,1,0),("Redo (mark on)",CompositePlate.SolidBlockName,1,1),("polls, no change",CompositePlate.SolidBlockName,0,0),("Thickness only (mark off)",CompositePlate.Name,1,0)};
                var log=new List<string>();
                foreach(var (label,name,eb,ec) in steps){var (gb,gc)=Step(name);log.Add($"{label}: {gb} rebuild(s), {gc} band computation(s)");Check(gb==eb&&gc==ec,$"overlay cache: {label} -> {gb} rebuild(s), {gc} band computation(s) (expected {eb}, {ec})");}
                File.WriteAllText(Path.Combine(output,"SOLID-BANDS-REBUILDS.txt"),string.Join("\n",log)+"\n");
                // the runtime glue and the pure code
                var fill=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));var region=fill[fill.IndexOf(F83.FillBegin,StringComparison.Ordinal)..fill.IndexOf(F83.FillEnd,StringComparison.Ordinal)];
                Check(region.Contains("NVector? forward=null;try{forward=Forward(plate);}catch(Exception){}",StringComparison.Ordinal)&&region.Contains("SolidBlockOverlay.ApplyBands(data,json,model?.Preset,forward,report)",StringComparison.Ordinal),"SolidOverlay passes the front of the vehicle in the frame of the mesh (RuntimeLayerFaces.Forward, as Convert to layers); no direction: the one colour");
                var pure=File.ReadAllText(Path.Combine(root,"SolidBlockOverlay.cs"));
                Check(!pure.Contains("UnityEngine",StringComparison.Ordinal)&&!pure.Contains("[HarmonyPatch",StringComparison.Ordinal)&&!region.Contains("[HarmonyPatch",StringComparison.Ordinal),"no new hook: SolidBlockOverlay.cs stays pure code");
                Check(!System.Text.RegularExpressions.Regex.IsMatch(region,@"plate\.\w+\s*=(?!=)")&&!region.Contains("SetMass",StringComparison.Ordinal)&&!region.Contains("RaiseDirtyFlags",StringComparison.Ordinal),"the solid overlay still writes nothing to the plate");
            }

            // ================= 4. (herpak) plates without the mark and in layers against the 0.22.83 build, bit for bit: removed (process check against an earlier build) =================

            // ================= 5. the contact sheet: Ctrl+L on the three proof plates, obliquely from the front and from the side, 0.22.83 beside 0.22.84 =================
            {
                var front=new Vector3(-.55f,-.42f,-1f);var side=new Vector3(-1f,-.32f,-.22f);
                foreach(var (tag,json,preset,forward,world,straight) in plates)
                {
                    var (one,bands)=drawn[tag];bool big=tag.StartsWith("Object 292",StringComparison.Ordinal);int ph=big?380:270;
                    var all=preset.Layers??Array.Empty<CassetteLayer>();var layers=string.Join(", ",all.Take(7).Select(l=>l.Material+" "+F(l.ThicknessMm,"0.#")))+(all.Length>7?$", ... ({all.Length} layers, last {all[^1].Material} {F(all[^1].ThicknessMm,"0.#")})":"");
                    var panel=new F83.Panel{title=tag+": Ctrl+L, 0.22.83 (left) and 0.22.84 (right)",explanation=$"Recipe {preset.Id} (mm): {layers}. Top: obliquely from the front; bottom: from the right side, the front of the vehicle to the right.",width=980,height=2*ph+10};
                    Draw(panel,one,preset,world,0,0,485,ph,"0.22.83: one colour, from the front",front);
                    Draw(panel,bands,preset,world,495,0,485,ph,"0.22.84: bands, from the front",front);
                    Draw(panel,one,preset,world,0,ph+10,485,ph,"0.22.83: one colour, from the side",side);
                    Draw(panel,bands,preset,world,495,ph+10,485,ph,"0.22.84: bands, from the side (front of the vehicle to the right)",side);
                    panels.Add(panel);
                }
            }
        }
        catch(Exception error){Check(false,"FEAT84 threw "+error.GetType().Name+": "+error.Message+" "+error.StackTrace?.Split('\n').FirstOrDefault());}
        File.WriteAllText(Path.Combine(output,"SOLID-BANDS-CASES.tsv"),cases.ToString());
        File.WriteAllText(Path.Combine(output,"SOLID-BANDS-TIMING.tsv"),timing.ToString());
        File.WriteAllText(Path.Combine(output,"SOLID-BANDS-EDGES.tsv"),edges.ToString());
        File.WriteAllText(Path.Combine(preview,"solid-bands-panels.json"),JsonSerializer.Serialize(panels,new JsonSerializerOptions{IncludeFields=true}));
        File.WriteAllText(Path.Combine(output,"FEAT84-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT84 PASS: {checks} checks, {failures} failures");
    }
}
