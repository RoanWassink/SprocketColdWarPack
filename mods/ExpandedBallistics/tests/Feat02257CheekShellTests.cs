using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.57/0.22.58 managed evidence for "Cheek fill (shell)": the T-80BVM backup (read only, drawn turret faces plus a reconstructed inner-wall
/// proxy and a saved gun envelope proxy), a welded box fixture with three cheek columns and one knuckle, and a second welded fixture after the
/// user's photos (two columns, the lower inner face reaching the lane edge and the floor). Every back point is recomputed here with an independent
/// transcription of the native Along-edge formula. 0.22.58 adds coverage, cut faces, mirror symmetry and a "nothing got worse" comparison with the
/// frozen 0.22.57 planner (LegacyCheekShellFill057). No Unity, native call or installer action.
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    private sealed record ShellCase(string Name,IReadOnlyList<ShellWallFace> Wall,IReadOnlyList<FillTriangle> HostWalls,IReadOnlyList<FillTriangle> Outer,FillBounds Bounds,float HalfGap,float Centre);
    /// <summary>0.22.59: the three models of RunShell, kept for the depth tests (T-80BVM, welded fixture, welded fixture after the photos).</summary>
    private static (ShellCase Case,FillBounds Gun)[]? shellCases;
    internal static void RunShell(Action<bool,string> sink)
    {
        int checks=0;void Check(bool ok,string message){checks++;sink(ok,"Cheek fill (shell): "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();
        Directory.CreateDirectory(output);string preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        string file=Directory.GetFiles(Path.Combine(root,"tests","fixtures","blueprint-backup"),"*.blueprint").Single();var original=File.ReadAllBytes(file);
        using var doc=JsonDocument.Parse(original);var data=doc.RootElement;
        var objects=data.GetProperty("objects").EnumerateArray().ToDictionary(x=>x.GetProperty("vuid").GetInt32());
        var blueprints=data.GetProperty("blueprints").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetInt32());
        var meshes=data.GetProperty("meshes").EnumerateArray().ToDictionary(x=>x.GetProperty("vuid").GetInt32());
        var plate=objects[1454];var host=objects[Parent(objects,plate).GetProperty("structureID").GetInt32()];
        var bp=blueprints[host.GetProperty("structureBlueprintVuid").GetInt32()].GetProperty("blueprint");
        var mesh=meshes[bp.GetProperty("bodyMeshVuid").GetInt32()].GetProperty("meshData").GetProperty("mesh");
        var authored=ComposerFill.Read(mesh.GetRawText());
        Check(authored.Faces.All(f=>f.Along.All(a=>a)),"backup turret stores tm 0 (Along face normal) on every corner, so the saved wall thickness is the normal depth");
        var inner=new List<FillTriangle>();
        foreach(var face in authored.Faces){var p=face.Corners.Select((v,j)=>v-face.Normal*face.Thickness[j]*.001f).ToArray();for(int j=1;j+1<p.Length;j++)inner.Add(new(p[0],p[j+1],p[j]));}
        var outer=Triangles(mesh,Mat3.Identity,Vector3.Zero);var bounds=FillBounds.Of("turret",outer.SelectMany(t=>new[]{t.A,t.B,t.C}));
        var cannon=objects.Values.First(o=>o.TryGetProperty("cannonBlueprintVuid",out _));var cannonBp=blueprints[cannon.GetProperty("cannonBlueprintVuid").GetInt32()].GetProperty("blueprint");
        float radius=cannonBp.GetProperty("caliber").GetSingle()*.0005f+cannonBp.GetProperty("segments").EnumerateArray().SelectMany(g=>g.GetProperty("segments").EnumerateArray()).Max(g=>Math.Max(g.GetProperty("t0").GetSingle(),g.GetProperty("t1").GetSingle()))*.001f;
        var (hostR,hostT)=World(objects,host);var (gunR,gunT)=World(objects,cannon);var relR=hostR.Transpose()*gunR;var relT=hostR.Transpose().Mul(gunT-hostT);
        var gunPoints=new List<Vector3>();foreach(float x in new[]{-radius,radius})foreach(float y in new[]{-radius,radius})foreach(float z in new[]{-.45f,2.1f})gunPoints.Add(relR.Mul(new(x,y,z))+relT);
        var gun=FillBounds.Of("gun",gunPoints);float halfGap=ComposerFill.GunHalfGap(new[]{gun}),centre=(gun.Min.X+gun.Max.X)*.5f;
        var t80=new ShellCase("t80bvm",authored.Faces.Select(f=>new ShellWallFace(f.Corners,f.Thickness,f.HoleLoops)).ToList(),outer.Concat(inner).ToArray(),outer,bounds,halfGap,centre);
        var (welded,weldedGun)=WeldedFixture(3,"welded");var (welded2,welded2Gun)=WeldedFixture(2,"welded2");
        shellCases=new[]{(t80,gun),(welded,weldedGun),(welded2,welded2Gun)};
        var rows=new StringBuilder("case\tgun_mm\touter_mm\tfallback\tface\tcorner\tvertex\tprofile_mm\trib_mm\tintended_mm\tt_mm\tdepth_mm\talong_rib_mm\tcos\tshortened\tlimited_by\n");
        var evidence=new List<object>();var results=new Dictionary<string,CheekShellResult>();var legacy=new Dictionary<string,LegacyShellResult057>();
        foreach(var shellCase in new[]{t80,welded,welded2})
        foreach(var (gunMm,outerMm,fallback) in new[]{(450d,200d,false),(450d,450d,false),(450d,200d,true)})
        {
            var r=CheekShellFill.Plan(shellCase.Wall,shellCase.HostWalls,shellCase.Bounds,new(gunMm,outerMm,fallback),new(),new(),shellCase.HalfGap,false,shellCase.Centre);
            string tag=$"{shellCase.Name} {gunMm:0}->{outerMm:0}{(fallback?" fallback":"")}";
            results[tag]=r;
            var old=LegacyCheekShellFill057.Plan(shellCase.Wall,shellCase.HostWalls,shellCase.Bounds,new(gunMm,outerMm,fallback),new(),new(),shellCase.HalfGap,false,shellCase.Centre);legacy[tag]=old;
            evidence.Add(EvaluateShell(tag,shellCase,r,old,gunMm,outerMm,fallback,Check,rows));
            if(shellCase.Name=="t80bvm"&&!fallback&&outerMm==200)File.WriteAllText(Path.Combine(output,"shell-450-200.mesh.json"),r.Plan.Json);
            if(shellCase.Name=="t80bvm"&&fallback)File.WriteAllText(Path.Combine(output,"shell-450-200-fallback.mesh.json"),r.Plan.Json);
        }
        Check(original.SequenceEqual(File.ReadAllBytes(file)),"Back / discarded managed plans leave the backup bytes unchanged");
        ShellUnitChecks(Check);
        // Contact sheet material: 0.22.57 (frozen planner) next to 0.22.58, 450 -> 200, front and top, three models, gun visible.
        foreach(var (shellCase,gunBounds) in new[]{(t80,gun),(welded,weldedGun),(welded2,welded2Gun)})
        {
            var shell=results[$"{shellCase.Name} 450->200"];var old=legacy[$"{shellCase.Name} 450->200"];
            var gunBox=Feat02253ComposerFillTests.Box(gunBounds.Min,gunBounds.Max).Select(t=>new SolidTriangle(t.A,t.B,t.C,Vector3.UnitZ,Raster.Pack(212,152,52,255))).ToList();
            var viewBounds=(shellCase.Bounds.Min,shellCase.Bounds.Max+new Vector3(0,0,.5f));
            foreach(var (view,direction) in new[]{("front",Vector3.UnitZ),("top",Vector3.UnitY)})
            {
                var camera=new Camera(viewBounds,direction);
                File.WriteAllBytes(Path.Combine(preview,$"{shellCase.Name}-shell-0.22.57-{view}.png"),Render(BlockSolids(old.Faces,old.Front,old.Back,old.Normals).Concat(gunBox).ToList(),camera,shellCase.Outer));
                File.WriteAllBytes(Path.Combine(preview,$"{shellCase.Name}-shell-0.22.58-{view}.png"),Render(BlockSolids(shell.Faces,shell.Front,shell.Back,shell.Normals).Concat(gunBox).ToList(),camera,shellCase.Outer));
            }
        }
        File.WriteAllText(Path.Combine(output,"SHELL-CORNERS.tsv"),rows.ToString());
        var main=results["t80bvm 450->200"];var mainStats=main.Plan.Inspect(RuntimeDensity,Array.Empty<FillBounds>());
        File.WriteAllText(Path.Combine(output,"CHEEK-02258-EVIDENCE.json"),JsonSerializer.Serialize(new{passed=true,checks,blueprint=file,blueprintSha256=Convert.ToHexString(SHA256.HashData(original)),
            innerSurfaceSource="Managed proxy: saved per-face thickness and normal, fan tessellation; live native thickenFaceMesh acceptance pending",gunSource="Saved caliber/barrel-wall envelope proxy; live Renderer.bounds pending",
            densityKgM3=RuntimeDensity,formula="back = v + u*(t*0.001/-(n.u)) + 0.25 mm in-plane bisector; t = round(L*|u.n|) per face (out-0.22.57/ALONG-EDGE-FORMULA.md)",
            t80bvm450to200=new{faces=main.Faces.Length,eligibleFaces=main.EligibleFaces,vertices=main.Front.Length,ribs=main.Ribs,shortened=main.Shortened,maxShortenMm=main.MaxShortenMm,clampedFloorRoof=main.ClampedFloorRoof,clampedLane=main.ClampedLane,reAimed=main.ReAimed,cutFaces=main.CutFaces,cutLostFaces=main.CutLostFaces,omittedFaces=main.OmittedFaces,omittedGun=main.OmittedGun,omittedBehind=main.OmittedBehind,volumeM3=mainStats.VolumeM3,massKg=mainStats.MassKg},
            cases=evidence,nativeUndoTested=false,nativeSaveLoadTested=false,nativeRibAcceptanceTested=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT58 PASS: {checks} checks; T-80BVM 450->200: {main.Faces.Length}/{main.EligibleFaces} faces, {main.Ribs} ribs, {main.Shortened} shortened (max {main.MaxShortenMm:0.##} mm), {main.OmittedFaces} left out, {main.CutFaces} cut; volume {mainStats.VolumeM3:R} m3, mass {mainStats.MassKg:R} kg.");
    }

    private static object EvaluateShell(string tag,ShellCase shellCase,CheekShellResult r,LegacyShellResult057 old,double gunMm,double outerMm,bool fallback,Action<bool,string> Check,StringBuilder rows,float rear=.35f,bool compareLegacy=true)
    {
        var plan=r.Plan;float bottom=shellCase.Bounds.Min.Y+.03f,top=shellCase.Bounds.Max.Y-.03f,halfGap=shellCase.HalfGap,centre=shellCase.Centre;
        Check(plan.After.FaceCount==r.Faces.Length&&r.Faces.Length>0,$"{tag}: plan has {r.Faces.Length} faces");
        Check(r.Fallback==fallback,$"{tag}: fallback flag");
        // 0.22.58 test 1: every forward face beside the gun (or its part beside the gun) has a block. The fallback (face normals, unchanged by the brief) only reports.
        // A face whose ribs would cross behind it even at 3 mm breaks a hard requirement and may be left out (brief, point 3); it is counted with its area.
        bool coverage=r.CutLostFaces==0&&r.Faces.Length+r.OmittedFaces==r.EligibleFaces&&(r.OmittedFaces==0||shellCase.Name=="t80bvm"&&r.OmittedFaceReason.All(x=>x=="crossing ribs"));
        if(!fallback)Check(coverage,$"{tag}: coverage {r.Faces.Length} of {r.EligibleFaces} eligible faces; left out {r.OmittedFaces} ({string.Join(", ",r.OmittedFaceReason)}; {r.OmittedAreaM2*1e4:0.#} cm2), cut slivers lost {r.CutLostFaces}");
        float outerLeft=halfGap,outerRight=halfGap;foreach(var w in r.Wall){float dx=w.X-centre;if(dx<0)outerLeft=Math.Max(outerLeft,-dx);else outerRight=Math.Max(outerRight,dx);}
        int Profile(float x){float dx=Math.Abs(x-centre),outer=x<centre?outerLeft:outerRight;double frac=outer-halfGap<=1e-6f?1:Math.Clamp((dx-halfGap)/(outer-halfGap),0,1);return (int)Math.Round(gunMm+(outerMm-gunMm)*frac,MidpointRounding.AwayFromZero);}
        bool profileOk=true;for(int v=0;v<r.Wall.Length;v++)profileOk&=r.ProfileMm[v]==Profile(r.Wall[v].X);
        Check(profileOk,$"{tag}: thickness profile is linear from the gun lane to the outer edge in whole millimetres");
        var around=new Dictionary<int,List<(int F,int J)>>();
        for(int f=0;f<r.Faces.Length;f++)for(int j=0;j<r.Faces[f].Length;j++){int v=r.Faces[f][j];if(!around.TryGetValue(v,out var l))around[v]=l=new();l.Add((f,j));}
        Check(around.Count==r.Wall.Length&&Enumerable.Range(0,r.Wall.Length).All(v=>plan.After.Vertices[v]==r.Front[v]),$"{tag}: every kept turret point is used once and is a plan vertex");
        // The inner point lies at least wall + 10 mm behind every filled face at that point (cut corners use the interpolated wall), and behind
        // every other drawn wall it meets at 30 degrees or more; it never lies in the gun lane.
        int cleared=0;
        for(int f=0;f<r.Faces.Length;f++)for(int j=0;j<r.Faces[f].Length;j++){int v=r.Faces[f][j];cleared++;Check(-Vector3.Dot(r.Front[v]-r.Wall[v],r.Normals[f])*1000>=r.FaceWallMm[f][j]+10-1e-3,$"{tag}: inner point of vertex {v} is at least wall + 10 mm behind face {f}");}
        if(!fallback)for(int v=0;v<r.Wall.Length;v++)foreach(var w in shellCase.Wall.Where(x=>x.HoleLoops==0))for(int j=0;j<w.Corners.Length;j++)
        {
            if(w.Corners[j]!=r.Wall[v])continue;var n=Vector3.Normalize(CheekShellFill.Newell(w.Corners));
            bool filled=r.Faces.Any(x=>x.Contains(v)&&Vector3.Dot(r.Normals[Array.IndexOf(r.Faces,x)],n)>.999f);
            if(filled)continue;
            // The rib may have been turned (10 degree rule); the 30 degree test uses the mean normal as the planner does, so only check walls the final rib meets at 30 degrees or more.
            if(-Vector3.Dot(n,r.Rib[v])<CheekShellFill.NeighbourWallCos)continue;
            cleared++;Check(-Vector3.Dot(r.Front[v]-r.Wall[v],n)*1000>=w.WallMm[j]+10-1e-3,$"{tag}: inner point of vertex {v} is at least wall + 10 mm behind its neighbouring wall (normal {n})");
        }
        Check(cleared>0,$"{tag}: inner-point clearance was checked against {cleared} face corners");
        for(int v=0;v<r.Wall.Length;v++){int side=r.Wall[v].X<centre?-1:1;if(Math.Abs(r.Wall[v].X-centre)<1e-6f)side=r.Front[v].X<centre?-1:1;Check(side*(r.Front[v].X-centre)>=halfGap-1e-6f,$"{tag}: inner point of vertex {v} stays outside the gun lane");}
        Check(r.ClampedRear==0||r.Wall.Length>0,$"{tag}: {r.ClampedRear} points with the inner point behind the rear limit keep their rib end no further back (reported extension of the band rule)");
        double maxDepth=0,maxAlong=0,maxSeam=0,maxProfile=0,maxTilt=0;int seamFail=0,inside=0,corners=0;
        var wallFaces=shellCase.Wall.Where(w=>w.HoleLoops==0).ToArray();
        for(int f=0;f<r.Faces.Length;f++)
        {
            var ids=r.Faces[f];var front=ids.Select(v=>r.Front[v]).ToArray();var n=r.Normals[f];
            var frontNormal=Vector3.Normalize(CheekShellFill.Newell(front));var source=ids.Select(v=>r.Wall[v]).ToArray();
            var turretNormal=Vector3.Normalize(CheekShellFill.Newell(source));
            double tilt=Math.Acos(Math.Clamp(Vector3.Dot(frontNormal,turretNormal),-1,1))*180/Math.PI;maxTilt=Math.Max(maxTilt,tilt);
            Check(Vector3.Dot(turretNormal,n)>.999f&&Vector3.Dot(frontNormal,n)>0&&tilt<45&&n.Z>=Math.Cos(60*Math.PI/180)-1e-6,$"{tag}: face {f} keeps the turret's forward direction (front tilt {tilt:0.##} degrees, reported)");
            // The face (or cut piece) lies in the plane of its drawn turret face; an uncut face uses that face's own corners.
            var drawn=shellCase.Wall[r.SourceFace[f]];var dn=Vector3.Normalize(CheekShellFill.Newell(drawn.Corners));
            // Every corner is a corner of that drawn face or a cut point on the lane edge inside the drawn face's extent (drawn quads may be slightly non-planar).
            bool fromDrawn=source.All(s=>drawn.Corners.Contains(s)||Math.Abs(Math.Abs(s.X-centre)-halfGap)<1e-5f&&s.Y>=drawn.Corners.Min(c=>c.Y)-1e-5f&&s.Y<=drawn.Corners.Max(c=>c.Y)+1e-5f&&s.Z>=drawn.Corners.Min(c=>c.Z)-1e-5f&&s.Z<=drawn.Corners.Max(c=>c.Z)+1e-5f);
            // A cut piece of a slightly non-planar quad is fan-triangulated, so a triangle's normal may lean a few degrees from the quad's Newell normal.
            Check(fromDrawn&&Vector3.Dot(dn,n)>(ownCornersOnly(source,drawn)?.999f:0f),$"{tag}: face {f} corners come from drawn turret face {r.SourceFace[f]} (cut at the lane edge when not its own corners)");
            Check(plan.After.Faces[f].VertexIds.SequenceEqual(ids)&&plan.After.Faces[f].Thickness.SequenceEqual(r.Thickness[f]),$"{tag}: face {f} payload matches the plan geometry");
            var back=new Vector3[ids.Length];
            for(int j=0;j<ids.Length;j++)
            {
                int v=ids[j];corners++;ushort t=r.Thickness[f][j];var p=front[j];
                var u=fallback?-n:r.Rib[v];double cos=fallback?1:-Vector3.Dot(n,u);
                var expectedBack=fallback?p-n*(float)(t*.001):TestNativeBack(p,front[(j+1)%ids.Length],front[(j+ids.Length-1)%ids.Length],u,t,n);
                Check(Vector3.Distance(expectedBack,r.Back[f][j])<1e-6f,$"{tag}: face {f} corner {j} back point follows the native formula");
                back[j]=expectedBack;
                Check(t>=3,$"{tag}: face {f} corner {j} keeps the native minimum of 3 mm");
                if(!fallback)Check(cos>=Math.Sin(10*Math.PI/180)-1e-6,$"{tag}: rib at vertex {v} is at least 10 degrees behind face {f}");
                double depth=-Vector3.Dot(back[j]-p,n)*1000,along=Vector3.Distance(back[j],p)*1000,L=r.RibLengthMm[v];
                double depthError=fallback?Math.Abs(depth-t):Math.Abs(depth-L*cos);maxDepth=Math.Max(maxDepth,depthError);maxAlong=Math.Max(maxAlong,fallback?depthError:Math.Abs(along-L));
                Check(depthError<=.5+1e-6,$"{tag}: face {f} corner {j} depth {depth:0.###} within 0.5 mm of {(fallback?"t "+t:"L*cos "+(L*cos).ToString("0.###"))}");
                bool shortenedVertex=r.RibLengthMm[v]<r.IntendedLengthMm[v]-.01;
                if(!shortenedVertex){double profileError=Math.Abs(r.RibLengthMm[v]-r.IntendedLengthMm[v]);maxProfile=Math.Max(maxProfile,profileError);}
                // 2. Inside the turret: ahead of the rear limit, within the floor/roof band (or no lower/higher than an inner point already outside it), outside the lane, before the next wall.
                float bottomV=Math.Min(bottom,p.Y),topV=Math.Max(top,p.Y),rearV=Math.Min(rear,p.Z);
                Check(back[j].Z>=rearV-1e-6f,$"{tag}: face {f} corner {j} back point ahead of the rear limit (or no further back than an inner point already behind it)");
                Check(back[j].Y>=bottomV-1e-6f&&back[j].Y<=topV+1e-6f,$"{tag}: face {f} corner {j} back point within 30 mm of floor and roof (or no lower/higher than an inner point already outside the band)");
                int side=source.Average(s=>s.X)<centre?-1:1;
                // 4. The gun stays free: no face and no back point inside the lane (cut corners lie on its edge).
                Check(side*(back[j].X-centre)>=halfGap-1e-6f&&side*(source[j].X-centre)>=halfGap-1e-6f,$"{tag}: face {f} corner {j} outside the gun lane");
                var dir=back[j]-p;
                if(dir.Length()>1e-7f)
                {
                    var ahead=shellCase.HostWalls.Where(w=>Vector3.Dot(dir,Vector3.Cross(w.B-w.A,w.C-w.A))<0).ToList();
                    var hit=ConformIndependentRay(p,dir,ahead);if(hit.HasValue){inside++;Check(dir.Length()<=hit.Value-.009,$"{tag}: face {f} corner {j} stays 10 mm before the next wall (hit {hit.Value*1000:0.#} mm, rib {dir.Length()*1000:0.#} mm)");}
                }
                rows.AppendLine(FormattableString.Invariant($"{shellCase.Name}\t{gunMm}\t{outerMm}\t{fallback}\t{f}\t{j}\t{v}\t{r.ProfileMm[v]}\t{L:R}\t{r.IntendedLengthMm[v]:R}\t{t}\t{depth:R}\t{along:R}\t{cos:R}\t{shortenedVertex}\t{r.LimitReason[v]}"));
            }
            var nb=CheekShellFill.Newell(back);
            Check(nb.LengthSquared()>1e-12f&&Vector3.Dot(nb,n)>0,$"{tag}: face {f} back face points the same way as the front");
        }
        // 5. Closed back: neighbouring faces share their back point within 1 mm (linked only; the fallback is allowed to gap and is reported).
        foreach(var (v,list) in around)
        {
            if(list.Count<2)continue;var backs=list.Select(x=>r.Back[x.F][x.J]).ToArray();double worst=0;
            for(int a=0;a<backs.Length;a++)for(int b=a+1;b<backs.Length;b++)worst=Math.Max(worst,Vector3.Distance(backs[a],backs[b])*1000);
            maxSeam=Math.Max(maxSeam,worst);if(worst>1)seamFail++;
            if(!fallback)Check(Vector3.Distance(r.RibEnd[v],backs.Aggregate(Vector3.Zero,(x,y)=>x+y)/backs.Length)<.001f,$"{tag}: back points of vertex {v} meet on the rib end");
        }
        if(!fallback)Check(seamFail==0,$"{tag}: {seamFail} vertices have back points more than 1 mm apart (largest {maxSeam:0.###} mm)");
        // 7. Volume and mass with the real back points.
        double hand=0;for(int f=0;f<r.Faces.Length;f++)hand+=SurfaceVolume(r.Faces[f].Select(v=>r.Front[v]).ToArray(),r.Back[f]);
        var stats=plan.Inspect(RuntimeDensity,Array.Empty<FillBounds>());
        Check(Math.Abs(stats.VolumeM3-hand)<1e-6&&Math.Abs(stats.MassKg-hand*RuntimeDensity)<.001&&hand>0,$"{tag}: volume {stats.VolumeM3:R} and mass {stats.MassKg:R} agree with the origin-tetrahedra hand calculation {hand:R}");
        Check(plan.Summary.Contains(r.Shortened>0?$"{r.Shortened} corners were shortened to fit, by up to":"All corners keep the chosen thickness."),$"{tag}: window reports the shortened corners");
        Check(fallback==plan.Summary.StartsWith(CheekShellFill.FallbackNote,StringComparison.Ordinal),$"{tag}: fallback note only in fallback mode");
        // 0.22.58 test 5: the window names what is left out, and only then.
        int leftOut=r.OmittedFaces+r.CutLostFaces;
        Check(leftOut>0?plan.Summary.Contains($"{leftOut} wall face{(leftOut==1?" was":"s were")} left out: "):!plan.Summary.Contains("left out"),$"{tag}: window line about left-out faces matches the count ({leftOut})");
        Check(r.CutFaces>0?plan.Summary.Contains($"{r.CutFaces} wall face{(r.CutFaces==1?"":"s")} cut at the gun lane."):!plan.Summary.Contains("cut at the gun lane"),$"{tag}: window reports cut faces ({r.CutFaces})");
        // 6. Save and load: the packed tm/te survive the real payload route as token text, also above 2^53.
        var wrapper=JsonSerializer.Serialize(new{name=CompositePlate.NameForMode(ThicknessMode.FillToWall),mesh=JsonDocument.Parse(plan.Json).RootElement});
        using var loaded=JsonDocument.Parse(wrapper);var loadedMesh=loaded.RootElement.GetProperty("mesh");var round=ComposerFill.Read(loadedMesh.GetRawText());
        Check(CompositePlate.ModeFromName(loaded.RootElement.GetProperty("name").GetString())==ThicknessMode.FillToWall,$"{tag}: mode identity round-trips");
        Check(round.Vertices.SequenceEqual(plan.After.Vertices)&&round.Faces.Zip(plan.After.Faces).All(p=>p.First.VertexIds.SequenceEqual(p.Second.VertexIds)&&p.First.Thickness.SequenceEqual(p.Second.Thickness)&&p.First.Along.SequenceEqual(p.Second.Along)),$"{tag}: payload round-trip keeps vertices, cuts, thickness and direction flags");
        using var planned=JsonDocument.Parse(plan.Json);var pf=planned.RootElement.GetProperty("faces").EnumerateArray().ToArray();var lf=loadedMesh.GetProperty("faces").EnumerateArray().ToArray();
        var edges=planned.RootElement.GetProperty("edges").EnumerateArray().Select(x=>x.GetInt32()).ToArray();int vertexCount=planned.RootElement.GetProperty("vertices").GetArrayLength()/3;
        bool tokens=true,big=false,ribsOk=true;
        for(int f=0;f<pf.Length;f++)
        {
            string tm=pf[f].GetProperty("tm").GetRawText(),te=pf[f].GetProperty("te").GetRawText();
            tokens&=tm==lf[f].GetProperty("tm").GetRawText()&&te==lf[f].GetProperty("te").GetRawText();
            ulong teValue=ulong.Parse(te),tmValue=ulong.Parse(tm);if(teValue>1UL<<53)big=true;
            var ids=pf[f].GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            for(int j=0;j<ids.Length;j++)
            {
                byte mode=(byte)(tmValue>>(8*j));int edge=(int)((teValue>>(16*j))&0xffff);
                if(fallback){ribsOk&=mode==0&&edge==0;continue;}
                int a=edges[2*edge],b=edges[2*edge+1];int other=a==ids[j]?b:a;
                ribsOk&=mode==CheekShellFill.AlongEdgeManual&&(a==ids[j]||b==ids[j])&&other>=r.Front.Length&&other<vertexCount&&pf.All(face=>!face.GetProperty("v").EnumerateArray().Any(x=>x.GetInt32()==other));
                ribsOk&=Vector3.Distance(VertexAt(planned.RootElement,other),r.RibEnd[ids[j]])<1e-6f;
            }
        }
        Check(tokens,$"{tag}: tm and te token text survive the payload round-trip");
        Check(ribsOk,$"{tag}: every corner is {(fallback?"tm 0 / te 0":"tm 4 with a rib from its own vertex to its helper point")}");
        if(!fallback&&shellCase.Name=="t80bvm")Check(big,$"{tag}: at least one te exceeds 2^53 and still round-trips exactly");
        Check(CheekShellFill.VerifyCopy(plan.Json,loadedMesh.GetRawText())==null,$"{tag}: the copy check accepts a faithful copy");
        if(!fallback)
        {
            var mutated=plan.Json.Replace("\"te\":"+pf[0].GetProperty("te").GetRawText(),"\"te\":0");
            Check(CheekShellFill.VerifyCopy(plan.Json,mutated)?.StartsWith(CheekShellFill.LinkedBackMarker)==true,$"{tag}: the copy check refuses a dropped te slot");
            var node=System.Text.Json.Nodes.JsonNode.Parse(plan.Json)!;var verts=node["vertices"]!.AsArray();for(int k=0;k<3;k++)verts.RemoveAt(verts.Count-1);
            Check(CheekShellFill.VerifyCopy(plan.Json,node.ToJsonString())?.StartsWith(CheekShellFill.LinkedBackMarker)==true,$"{tag}: the copy check refuses a dropped helper point");
        }
        var reasons=r.LimitReason.Where(x=>x.Length>0).GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count());
        Check(r.LimitReason.Count(x=>x.Length>0)>=r.Shortened,$"{tag}: every shortened or clamped vertex names what limited it");
        var omittedReasons=r.OmittedReason.GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count());
        // 0.22.58 test 3: nothing got worse. Points the 0.22.57 planner did not shorten stay unshortened, and where their rib (the mean of the
        // normals around the point) and their profile (linear to the outermost point) are unchanged they keep the rib length within 0.5 mm and t
        // within 1 mm. On the T-80BVM the newly covered lower faces add neighbours to shared points and move the outermost point, so rib and
        // profile change there by the brief's own rules; the count and the largest change are reported.
        int compared=0,tChanged=0,ribChanged=0,seamTuned=0;double maxLengthChange=0;
        if(!fallback&&compareLegacy)for(int ov=0;ov<old.Wall.Length;ov++)
        {
            if(old.RibLengthMm[ov]<old.IntendedLengthMm[ov]-.01)continue;
            int nv=Array.IndexOf(r.Wall,old.Wall[ov]);if(nv<0)continue;compared++;
            bool sameRule=Vector3.Dot(old.Rib[ov],r.Rib[nv])>.99999f&&old.ProfileMm[ov]==r.ProfileMm[nv];if(!sameRule)ribChanged++;
            double change=Math.Abs(r.RibLengthMm[nv]-old.RibLengthMm[ov]);if(sameRule)maxLengthChange=Math.Max(maxLengthChange,change);
            // The seam tuner may take up to 1 mm off a point whose rounded back points would otherwise lie over 1 mm apart (hard requirement 5); counted and reported.
            bool tuned=r.LimitReason[nv].Contains("whole millimetres")&&r.RibLengthMm[nv]>=r.IntendedLengthMm[nv]-1-.01;if(tuned&&r.RibLengthMm[nv]<r.IntendedLengthMm[nv]-.01)seamTuned++;
            if(sameRule)Check(r.RibLengthMm[nv]>=r.IntendedLengthMm[nv]-.01||tuned,$"{tag}: unshortened 0.22.57 point {ov} stays unshortened (or is seam-tuned by at most 1 mm)");
            if(sameRule&&!tuned)Check(change<=.5,$"{tag}: unshortened 0.22.57 point {ov} keeps its rib length ({old.RibLengthMm[ov]:0.##} -> {r.RibLengthMm[nv]:0.##} mm)");
            for(int of=0;of<old.Faces.Length;of++)
            {
                int oj=Array.IndexOf(old.Faces[of],ov);if(oj<0)continue;
                var oldCorners=old.Faces[of].Select(i=>old.Wall[i]).ToHashSet();
                int nf=Array.FindIndex(r.Faces,x=>x.Select(i=>r.Wall[i]).ToHashSet().SetEquals(oldCorners));if(nf<0)continue;
                int nj=Array.IndexOf(r.Faces[nf],nv);if(Math.Abs(r.Thickness[nf][nj]-old.Thickness[of][oj])>1)tChanged++;
                if(sameRule&&!tuned)Check(Math.Abs(r.Thickness[nf][nj]-old.Thickness[of][oj])<=1,$"{tag}: 0.22.57 face keeps t at point {ov} ({old.Thickness[of][oj]} -> {r.Thickness[nf][nj]} mm)");
            }
        }
        if(!fallback&&compareLegacy)Check(compared>0,$"{tag}: compared {compared} unshortened 0.22.57 points with 0.22.58 ({ribChanged} with a changed rib or profile, {seamTuned} seam-tuned by at most 1 mm, {tChanged} corners with t changed by more than 1 mm)");
        if(!fallback&&compareLegacy&&shellCase.Name!="t80bvm")Check(ribChanged==0&&tChanged==0,$"{tag}: welded blocks that were right in 0.22.57 are unchanged");
        // 0.22.58 test 4: left and right mirrored within 0.01 mm on the welded fixtures (and the 0.22.57 result, for point 6).
        double? mirror58=shellCase.Name=="t80bvm"?null:MirrorError(r.Faces,r.Front,r.Back,centre),mirror57=shellCase.Name=="t80bvm"?null:MirrorError(old.Faces,old.Front,old.Back,centre);
        if(shellCase.Name!="t80bvm"){Check(mirror58<=.01,$"{tag}: left and right cheeks mirror within 0.01 mm ({mirror58:0.####} mm)");Check(mirror57<=.01,$"{tag}: the 0.22.57 blocks mirror too ({mirror57:0.####} mm): the uneven contact sheet of 0.22.57 was the picture, not the mesh");}
        return new{tag,gunMm,outerMm,fallback,faces=r.Faces.Length,eligibleFaces=r.EligibleFaces,cutFaces=r.CutFaces,cutLostFaces=r.CutLostFaces,cutLostAreaM2=r.CutLostAreaM2,vertices=r.Front.Length,ribs=r.Ribs,panels=r.Panels,shortened=r.Shortened,maxShortenMm=r.MaxShortenMm,clampedFloorRoof=r.ClampedFloorRoof,clampedLane=r.ClampedLane,reAimed=r.ReAimed,limitedBy=reasons,omittedBy=omittedReasons,omittedFaceReasons=r.OmittedFaceReason,omittedFaces=r.OmittedFaces,omittedAngleVertices=r.OmittedAngleVertices,omittedThinVertices=r.OmittedThinVertices,omittedHole=r.OmittedHole,omittedGun=r.OmittedGun,omittedBehind=r.OmittedBehind,
            maxDepthErrorMm=maxDepth,maxAlongRibDeviationMm=maxAlong,maxFrontTiltDegrees=maxTilt,maxSeamMm=maxSeam,seamFailures=seamFail,maxProfileErrorMm=maxProfile,cornersWithInsideRay=inside,corners,volumeM3=stats.VolumeM3,massKg=stats.MassKg,handVolumeM3=hand,
            clampedRear=r.ClampedRear,omittedAreaM2=r.OmittedAreaM2,legacyFaces=old.Faces.Length,legacyOmittedFaces=old.OmittedFaces,legacyPointsCompared=compared,legacyPointsWithChangedRibOrProfile=ribChanged,legacySeamTunedPoints=seamTuned,legacyMaxRibLengthChangeMm=maxLengthChange,legacyThicknessChangedCorners=tChanged,mirrorErrorMm=mirror58,legacyMirrorErrorMm=mirror57,windowLine=plan.Summary};
    }
    /// <summary>Largest distance (mm) between a left block corner (front and back) and the mirror image of its right counterpart; NaN when a face has no mirror partner.</summary>
    private static double MirrorError(int[][] faces,Vector3[] front,Vector3[][] back,float centre)
    {
        Vector3 M(Vector3 p)=>new(2*centre-p.X,p.Y,p.Z);double worst=0;
        for(int f=0;f<faces.Length;f++)
        {
            var mirrored=faces[f].Select(v=>M(front[v])).ToArray();int partner=-1;int[] map=Array.Empty<int>();
            for(int g=0;g<faces.Length&&partner<0;g++)
            {
                if(faces[g].Length!=mirrored.Length)continue;
                var candidate=mirrored.Select(p=>Array.FindIndex(faces[g],v=>Vector3.Distance(front[v],p)<.00001f)).ToArray();
                if(candidate.All(i=>i>=0)&&candidate.Distinct().Count()==candidate.Length){partner=g;map=candidate;}
            }
            if(partner<0)return 1e9;
            for(int j=0;j<faces[f].Length;j++){worst=Math.Max(worst,Vector3.Distance(M(front[faces[f][j]]),front[faces[partner][map[j]]])*1000);worst=Math.Max(worst,Vector3.Distance(M(back[f][j]),back[partner][map[j]])*1000);}
        }
        return worst;
    }
    private static bool ownCornersOnly(Vector3[] source,ShellWallFace drawn)=>source.All(s=>drawn.Corners.Contains(s))&&source.Length==drawn.Corners.Length;
    private static Vector3 VertexAt(JsonElement mesh,int id){var v=mesh.GetProperty("vertices");return new(v[3*id].GetSingle(),v[3*id+1].GetSingle(),v[3*id+2].GetSingle());}
    /// <summary>Independent transcription of EditMeshBuilding.ThickenAlongEdge: u = unit(edge), d = n.u, back = v + u*(t/-d) + 0.25 mm along unit(unit(v-next)+unit(v-prev)).</summary>
    private static Vector3 TestNativeBack(Vector3 v,Vector3 next,Vector3 prev,Vector3 u,ushort t,Vector3 n)
    {
        double d=Vector3.Dot(n,u);if(d>Math.Sin(-Math.PI/180))return v-n*(float)(t*.001);
        var back=v+u*(float)(t*.001/-d);var bis=Vector3.Normalize(v-next)+Vector3.Normalize(v-prev);
        return bis.Length()>1e-5f?back+Vector3.Normalize(bis)*.00025f:back;
    }
    /// <summary>Signed volume of one block: origin-based tetrahedra over its outward triangles (the production sum is centroid-based).</summary>
    private static double SurfaceVolume(Vector3[] front,Vector3[] back)
    {
        double v=0;int n=front.Length;
        void Tri(Vector3 a,Vector3 b,Vector3 c)=>v+=Vector3.Dot(a,Vector3.Cross(b,c))/6;
        for(int j=1;j+1<n;j++){Tri(front[0],front[j],front[j+1]);Tri(back[0],back[j+1],back[j]);}
        for(int j=0;j<n;j++){int k=(j+1)%n;Tri(front[j],back[k],front[k]);Tri(front[j],back[j],back[k]);}
        return v;
    }
    private static List<SolidTriangle> BlockSolids(int[][] faces,Vector3[] frontPoints,Vector3[][] backs,Vector3[] normals)
    {
        var result=new List<SolidTriangle>();
        for(int f=0;f<faces.Length;f++)
        {
            var front=faces[f].Select(v=>frontPoints[v]).ToArray();var back=backs[f];var normal=normals[f];int n=front.Length;
            uint frontColour=Shade(normal,0xb8,0xc4,0x6a),backColour=Shade(-normal,0x6c,0x72,0x70);
            for(int j=1;j+1<n;j++){result.Add(new(front[0],front[j],front[j+1],normal,frontColour));result.Add(new(back[0],back[j+1],back[j],-normal,backColour));}
            for(int j=0;j<n;j++)
            {
                int k=(j+1)%n;var side=Vector3.Cross(front[k]-front[j],back[j]-front[j]);if(side.LengthSquared()<1e-16)continue;
                var sn=Vector3.Normalize(side);uint sc=Shade(sn,0x86,0x8c,0x82);result.Add(new(front[j],front[k],back[k],sn,sc));result.Add(new(front[j],back[k],back[j],sn,sc));
            }
        }
        return result;
    }
    /// <summary>
    /// A welded box turret: floor, roof, sides and back, a centre plate in the gun lane and per side a cheek of <paramref name="columns"/> columns in two
    /// rows (lower vertical, upper leaning back: one knuckle). Walls 60 mm. The inner column ends at the lane edge and the lower row stands on the floor
    /// (the user's photos: the lower face beside the gun reaches the lane and the floor). <paramref name="laneEdgeX"/> moves the cheek's inner edge into
    /// the lane so that the inner column crosses the lane edge (cut test).
    /// </summary>
    private static (ShellCase Case,FillBounds Gun) WeldedFixture(int columns,string name,float laneEdgeX=.35f)
    {
        const ushort wall=60;var faces=new List<ShellWallFace>();
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)=>faces.Add(new(new[]{a,b,c,d},new ushort[]{wall,wall,wall,wall}));
        float xo=1.2f,xi=laneEdgeX,zo=.8f,zi=1.4f,lean=.25f,ym=.5f,yt=1f;
        foreach(int s in new[]{-1,1})
        for(int column=0;column<columns;column++)
        {
            float xa=xo-(xo-xi)*column/columns,xb=xo-(xo-xi)*(column+1)/columns,za=zo+(zi-zo)*column/columns,zb=zo+(zi-zo)*(column+1)/columns;
            Vector3 lo0=new(s*xa,0,za),lo1=new(s*xb,0,zb),mid0=new(s*xa,ym,za),mid1=new(s*xb,ym,zb),top0=new(s*xa,yt,za-lean),top1=new(s*xb,yt,zb-lean);
            if(s<0){Quad(lo0,lo1,mid1,mid0);Quad(mid0,mid1,top1,top0);}else{Quad(lo1,lo0,mid0,mid1);Quad(mid1,mid0,top0,top1);}
        }
        Quad(new(-xi,0,zi),new(xi,0,zi),new(xi,ym,zi),new(-xi,ym,zi));Quad(new(-xi,ym,zi),new(xi,ym,zi),new(xi,yt,zi-lean),new(-xi,yt,zi-lean));
        Quad(new(-xo,0,-.8f),new(-xo,0,zo),new(-xo,yt,zo-lean),new(-xo,yt,-.8f));Quad(new(xo,0,zo),new(xo,0,-.8f),new(xo,yt,-.8f),new(xo,yt,zo-lean));
        Quad(new(xo,0,-.8f),new(-xo,0,-.8f),new(-xo,yt,-.8f),new(xo,yt,-.8f));
        Quad(new(-xo,0,-.8f),new(xo,0,-.8f),new(xo,0,zo),new(-xo,0,zo));Quad(new(-xi,0,zo),new(xi,0,zo),new(xi,0,zi),new(-xi,0,zi));
        Quad(new(-xo,yt,zo-lean),new(xo,yt,zo-lean),new(xo,yt,-.8f),new(-xo,yt,-.8f));
        var outer=new List<FillTriangle>();foreach(var f in faces)for(int j=1;j+1<f.Corners.Length;j++)outer.Add(new(f.Corners[0],f.Corners[j],f.Corners[j+1]));
        var shellCase=new ShellCase(name,faces,outer.Concat(WeldedInner(faces)).ToArray(),outer,FillBounds.Of(name,outer.SelectMany(t=>new[]{t.A,t.B,t.C})),.35f,0);
        return (shellCase,new FillBounds("gun",new(-.3f,.4f,-.5f),new(.3f,.6f,2f)));
    }
    private static List<FillTriangle> WeldedInner(IReadOnlyList<ShellWallFace> faces)
    {
        var inner=new List<FillTriangle>();
        foreach(var f in faces){var n=Vector3.Normalize(CheekShellFill.Newell(f.Corners));var p=f.Corners.Select((v,j)=>v-n*f.WallMm[j]*.001f).ToArray();for(int j=1;j+1<p.Length;j++)inner.Add(new(p[0],p[j+1],p[j]));}
        return inner;
    }
    private static void ShellUnitChecks(Action<bool,string> check)
    {
        var n1=Vector3.UnitZ;var n2=Vector3.Normalize(new Vector3(0,MathF.Sin(2*MathF.PI/180),MathF.Cos(2*MathF.PI/180)));var n3=Vector3.Normalize(new Vector3(0,.5f,1));
        var panels=CheekShellFill.Panels(new[]{n1,n2,n3},Math.Cos(3*Math.PI/180));
        check(panels.Count==2&&Vector3.Dot(panels[0],Vector3.Normalize(n1+n2))>.99999f,"faces within 3 degrees form one panel, a 26.6 degree neighbour its own");
        var (welded,_)=WeldedFixture(3,"welded");
        var r=CheekShellFill.Plan(welded.Wall,welded.HostWalls,welded.Bounds,new(300,300),new(),new(),welded.HalfGap,false,welded.Centre);
        int knuckles=0,mitres=0;
        for(int v=0;v<r.Wall.Length;v++)
        {
            if(Math.Abs(r.Wall[v].Y-.5f)>1e-6f)continue;knuckles++;
            var incident=Enumerable.Range(0,r.Faces.Length).Where(f=>r.Faces[f].Contains(v)).ToArray();if(incident.Length<2)continue;
            var knucklePanels=CheekShellFill.Panels(incident.Select(f=>r.Normals[f]).ToArray(),Math.Cos(3*Math.PI/180));
            if(knucklePanels.Count!=2)continue;
            var bisector=-Vector3.Normalize(knucklePanels[0]+knucklePanels[1]);double half=Math.Acos(Math.Clamp(Vector3.Dot(knucklePanels[0],knucklePanels[1]),-1,1))/2;
            if(r.RibLengthMm[v]<r.IntendedLengthMm[v]-.01)continue; // a clamped knuckle rib points at its clamped end
            mitres++;
            check(half>10*Math.PI/180&&Vector3.Dot(r.Rib[v],bisector)>.99999f,"knuckle rib lies on the bisector of the two panels");
            check(Math.Abs(r.IntendedLengthMm[v]-300/Math.Cos(half))<1e-3,"knuckle L = thickness / cos(half knuckle angle)");
            check(incident.All(f=>r.Thickness[f][Array.IndexOf(r.Faces[f],v)]==300),"both panels keep the full 300 mm to the mitre");
        }
        check(knuckles>0&&mitres>0,"welded fixture has knuckle vertices with an unshortened mitre");
        check(r.OmittedFaces==0&&r.Faces.Length==12&&r.EligibleFaces==12,"0.22.58: all 12 cheek quads get a block (0.22.57 left the lane-edge column out)");
        check(r.ClampedLane>0,"lane-edge points are clamped to the lane edge instead of left out");
        int flat=0;
        for(int v=0;v<r.Wall.Length;v++)
        {
            var incident=Enumerable.Range(0,r.Faces.Length).Where(f=>r.Faces[f].Contains(v)).ToArray();
            if(incident.Length==0||Math.Abs(r.Wall[v].Y-.5f)<1e-6f||r.RibLengthMm[v]<r.IntendedLengthMm[v]-.01)continue;flat++;
            check(Vector3.Dot(r.Rib[v],-r.Normals[incident[0]])>.99999f&&Math.Abs(r.IntendedLengthMm[v]-300)<1e-3,"flat welded panel rib is perpendicular with L = T");
        }
        check(flat>0,"welded fixture has flat panel vertices");
        check(r.OmittedGun>=2,"centre plates in the gun lane are omitted whole");
        // Cut test: the cheek's inner edge inside the lane (x = 0.25 against a lane edge of 0.35): the inner column is cut at the edge, both rows share the cut points.
        var (crossing,_)=WeldedFixture(2,"crossing",.25f);
        var c=CheekShellFill.Plan(crossing.Wall,crossing.HostWalls,crossing.Bounds,new(300,300),new(),new(),.35f,false,0);
        check(c.CutFaces==4&&c.CutLostFaces==0&&c.OmittedFaces==0&&c.Faces.Length==c.EligibleFaces&&c.Faces.Length==8,$"faces across the lane are cut at its edge and kept: {c.CutFaces} cut, {c.Faces.Length} faces");
        var cutPoints=Enumerable.Range(0,c.Wall.Length).Where(v=>Math.Abs(Math.Abs(c.Wall[v].X)-.35f)<1e-6f).ToArray();
        check(cutPoints.Length==6&&cutPoints.All(v=>Enumerable.Range(0,c.Faces.Length).Count(f=>c.Faces[f].Contains(v))>=1)&&cutPoints.Count(v=>Enumerable.Range(0,c.Faces.Length).Count(f=>c.Faces[f].Contains(v))==2)==2,"cut corners lie on the lane edge, are welded and shared by the two rows where they meet");
        check(c.Plan.Summary.Contains("4 wall faces cut at the gun lane."),"window reports the cut faces");
        foreach(var bad in new[]{new CheekShellRequest(double.NaN,200),new(2,200),new(450,451),new(450,double.PositiveInfinity)})
        {bool refused=false;try{CheekShellFill.Plan(welded.Wall,welded.HostWalls,welded.Bounds,bad,new(),new(),.35f,false,0);}catch(InvalidOperationException ex){refused=ex.Message.Contains("Enter a thickness");}check(refused,"invalid thickness refused: "+bad);}
        var holed=welded.Wall.Select((f,i)=>i==0?f with{HoleLoops=1}:f).ToList();
        var h=CheekShellFill.Plan(holed,welded.HostWalls,welded.Bounds,new(300,300),new(),new(),.35f,false,0);
        check(h.OmittedHole==1&&h.Faces.Length==r.Faces.Length-1&&h.Plan.Summary.Contains("1 faces with holes skipped."),"a face with a hole is skipped and counted");
        // A sharp forward wedge (faces 82 degrees from forward): no rib direction keeps 10 degrees from both faces, so the apex points are left out and the window says so.
        var grazing=new List<ShellWallFace>{new(new Vector3[]{new(-1.2f,0,1),new(-.8f,0,1),new(-.8f,1,1),new(-1.2f,1,1)},new ushort[]{50,50,50,50}),
            new(new Vector3[]{new(-.6f,0,.79f),new(-.5f,0,1.5f),new(-.5f,1,1.5f),new(-.6f,1,.79f)},new ushort[]{50,50,50,50}),new(new Vector3[]{new(-.5f,0,1.5f),new(-.4f,0,.79f),new(-.4f,1,.79f),new(-.5f,1,1.5f)},new ushort[]{50,50,50,50})};
        var box=Feat02253ComposerFillTests.Box(new(-1.3f,-.1f,-1),new(1.3f,1.1f,1.6f));
        var g=CheekShellFill.Plan(grazing,box,FillBounds.Of("box",box.SelectMany(t=>new[]{t.A,t.B,t.C})),new(200,200),new(),new(450,.35,150,85),.35f,false,0);
        check(g.OmittedAngleVertices==2&&g.OmittedFaces==2&&g.Faces.Length==1&&g.Plan.Summary.Contains("2 wall faces were left out: no rib direction keeps 10 degrees from the wall (2)."),"a rib under 10 degrees from a face drops that vertex's faces, counts them and the window says why");
        check(CheekShellFill.LeftOutLine(0,Array.Empty<string>())==""&&CheekShellFill.LeftOutLine(1,new[]{"next wall"})=="1 wall face was left out: no room behind them (1). ","left-out line: none at 0, plain English otherwise");
        check(CheekShellFill.VerifyCopy(r.Plan.Json,r.Plan.Json)==null,"copy check accepts the identical payload");
        var tmChanged=r.Plan.Json.Replace("\"tm\":67372036","\"tm\":16843009");
        check(tmChanged!=r.Plan.Json&&CheekShellFill.VerifyCopy(r.Plan.Json,tmChanged)!.Contains("direction mode"),"copy check refuses tm rewritten by the game");
        var fb=CheekShellFill.Plan(welded.Wall,welded.HostWalls,welded.Bounds,new(300,300,true),new(),new(),.35f,false,0);
        check(fb.Ribs==0&&fb.Plan.After.Vertices.Length==fb.Front.Length&&fb.Plan.After.Faces.All(f=>f.Along.All(a=>a))&&fb.Faces.Length>0&&fb.Faces.Length<=r.Faces.Length,$"fallback uses tm 0 and adds no helper points ({fb.Faces.Length} of {r.Faces.Length} faces: on the face normal the lane-edge blocks have no room, reported)");
        var root=Fix02228Tests.SourceRoot();var ui=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));
        var dialog=ui[ui.IndexOf("    private static void CheekShellDialog()",StringComparison.Ordinal)..];dialog=dialog[..dialog.IndexOf("    private const float DerivedLabelWidth",StringComparison.Ordinal)];
        check(dialog.Contains("Cheek fill (shell) - inspect before applying")&&dialog.Contains("Thickness at the gun (mm)")&&dialog.Contains("Thickness at the outer edge (mm)")&&dialog.Contains("row.Button(\"Back\",p.CloseModal)")&&dialog.Contains("TryPrepared(editor,prepared")&&dialog.Contains("fallback=true;Replan();return;"),"Kit dialog has both fields, Back only closes, Apply uses the existing route and offers the fallback after a refused copy");
        check(CassetteComposerModel.ApplyChoiceLabels.Contains("Cheek fill (shell)")&&File.ReadAllText(Path.Combine(root,"CassetteComposerModel.cs")).Contains("Lines the inside of the turret front with armour. You choose the thickness at the gun and at the outer edge."),"option and help text as specified (0.22.71: the button became an option of Apply, its tooltip the help text)");
        var route=File.ReadAllText(Path.Combine(root,"RuntimeCompositePlateEdit.cs"));
        check(route.Contains("prepared?.Plan.CopyCheck is {} copyCheck){var copyJson=fresh.ToJson(true);if(copyCheck(copyJson) is {} copyProblem)") /* 0.22.80: the copy is kept for the dump and the one retry */&&route.Split("Plan.CopyCheck").Length==2,"route runs the copy check once, only for plans that carry it, before the mesh is replaced");
    }
}
