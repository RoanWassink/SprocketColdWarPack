using System.Numerics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

internal static partial class ComposerFillBlueprintEvidence
{
    internal static void RunConform(Action<bool,string> sink)
    {
        int checks=0;void Check(bool ok,string message){checks++;sink(ok,"Conform cheek fill: "+message);}
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
        Check(authored.Faces.All(f=>f.Along.All(a=>a)),"backup turret uses only Along face normal; no unproven edge-direction reconstruction");
        // Saved payload has no tessellated backMesh. Reconstruct per-face inner surfaces from
        // authored positions, normals and saved millimetres. This is a labelled managed proxy.
        var inner=new List<FillTriangle>();
        foreach(var face in authored.Faces)
        {
            var p=face.Corners.Select((v,j)=>v-face.Normal*face.Thickness[j]*.001f).ToArray();
            for(int j=1;j+1<p.Length;j++)inner.Add(new(p[0],p[j+1],p[j]));
        }
        var outer=Triangles(mesh,Mat3.Identity,Vector3.Zero);var bounds=FillBounds.Of("turret",outer.SelectMany(t=>new[]{t.A,t.B,t.C}));
        var cannon=objects.Values.First(o=>o.TryGetProperty("cannonBlueprintVuid",out _));var cannonBp=blueprints[cannon.GetProperty("cannonBlueprintVuid").GetInt32()].GetProperty("blueprint");
        float radius=cannonBp.GetProperty("caliber").GetSingle()*.0005f+cannonBp.GetProperty("segments").EnumerateArray().SelectMany(g=>g.GetProperty("segments").EnumerateArray()).Max(g=>Math.Max(g.GetProperty("t0").GetSingle(),g.GetProperty("t1").GetSingle()))*.001f;
        var (hostR,hostT)=World(objects,host);var (gunR,gunT)=World(objects,cannon);var relR=hostR.Transpose()*gunR;var relT=hostR.Transpose().Mul(gunT-hostT);
        var gunPoints=new List<Vector3>();foreach(float x in new[]{-radius,radius})foreach(float y in new[]{-radius,radius})foreach(float z in new[]{-.45f,2.1f})gunPoints.Add(relR.Mul(new(x,y,z))+relT);
        var gun=FillBounds.Of("gun",gunPoints);float halfGap=ComposerFill.GunHalfGap(new[]{gun}),centre=(gun.Min.X+gun.Max.X)*.5f;
        var rows=new StringBuilder("x_mm\tface\tcorner\tvertex\toffset_error_mm\trear_clamped\tray_miss\tend_clearance_error_mm\tthickness_mm\n");
        var evidence=new List<object>();ConformCheekResult? result450=null;double maximumFit=0,maximumEnd=0;
        foreach(double offset in new[]{300d,450d})
        {
            var shell=outer.Concat(inner).ToArray();
            var result=ConformCheekFill.Plan(inner,offset,new(),new(),halfGap,false,centre,shell);var plan=result.Plan;
            if(offset==450)result450=result;
            int misses=0;double maxFit=0,maxEnd=0,hand=0;
            Check(plan.After.FaceCount==result.SourceTriangles.Length,"exactly one output face per selected turret triangle at "+offset);
            Check(plan.After.Vertices.Length<plan.After.FaceCount*3,"shared wall vertices remain shared at "+offset);
            for(int i=0;i<result.WallVertices.Length;i++)
            {
                var source=result.WallVertices[i];var position=plan.After.Vertices[i];
                double expected=result.RearClamped[i]?(source.Z-.35)/-result.InwardNormals[i].Z:offset*.001;
                double err=Vector3.Distance(position,source+result.InwardNormals[i]*(float)expected)*1000;
                maxFit=Math.Max(maxFit,err);Check(err<=1,"wall offset along mean normal / vertex "+i+" at "+offset);
                if(result.RearClamped[i])Check(Math.Abs(position.Z-.35f)<1e-6,"rear-clamped vertex is exactly on configured plane");
            }
            for(int i=0;i<plan.After.FaceCount;i++)
            {
                var face=plan.After.Faces[i];var wall=inner[result.SourceTriangles[i]];
                Check(face.Normal.Z<0,"normal points backward; native minus-normal extrusion points forward (documented deviation)");
                Check(face.Corners.All(p=>p.X<=centre-halfGap+1e-6)||face.Corners.All(p=>p.X>=centre+halfGap-1e-6),"whole face outside gun opening");
                var chosen=new[]{wall.A,wall.B,wall.C};
                Check(face.VertexIds.Select(id=>result.WallVertices[id]).ToHashSet().SetEquals(chosen),"selected wall triangle retains all three original vertices and edges");
                double a=Vector3.Distance(face.Corners[0],face.Corners[1]),b=Vector3.Distance(face.Corners[1],face.Corners[2]),c=Vector3.Distance(face.Corners[2],face.Corners[0]),s=(a+b+c)/2;
                hand+=Math.Sqrt(Math.Max(0,s*(s-a)*(s-b)*(s-c)))*face.Thickness.Average(t=>(double)t)*.001;
                for(int j=0;j<3;j++)
                {
                    int id=face.VertexIds[j];var p=face.Corners[j];var hit=ConformIndependentRay(p,-face.Normal,inner);var end=p-face.Normal*face.Thickness[j]*.001f;
                    var nearest=ConformIndependentRay(p,-face.Normal,shell);
                    if(hit.HasValue&&nearest.HasValue&&hit.Value>nearest.Value+.00001)hit=null;
                    double expected=hit.HasValue?hit.Value*1000-10:Vector3.Distance(p,result.WallVertices[id])*1000-10;
                    double error=Math.Abs(face.Thickness[j]-expected);if(hit.HasValue)maxEnd=Math.Max(maxEnd,error);else misses++;
                    Check(error<=1, $"independent ray X={offset} face={i} corner={j} error={error:R} expected={expected:R} actual={face.Thickness[j]} hit={hit}");
                    if(hit.HasValue)Check(face.Thickness[j]*.001<hit.Value,"extruded corner stops inside inner wall, never through it");
                    var outerHit=ConformIndependentRay(p,-face.Normal,outer);
                    Check(!outerHit.HasValue||face.Thickness[j]*.001<outerHit.Value+1e-6,$"extruded corner before authored outer skin X={offset} face={i} corner={j}: outer distance {outerHit} thickness {face.Thickness[j]}");
                    Check(end.Z>=.35f-1e-6,"extruded corner remains ahead of rear limit");
                    Check(p.X<=centre-halfGap?end.X<=centre-halfGap+1e-6:end.X>=centre+halfGap-1e-6,"extruded corner outside gun opening");
                    rows.AppendLine(FormattableString.Invariant($"{offset}\t{i}\t{j}\t{id}\t{Vector3.Distance(p,result.WallVertices[id])*1000-offset:R}\t{result.RearClamped[id]}\t{!hit.HasValue}\t{error:R}\t{face.Thickness[j]}"));
                }
            }
            var stats=plan.Inspect(RuntimeDensity,Array.Empty<FillBounds>());
            Check(Math.Abs(stats.VolumeM3-hand)<1e-6&&Math.Abs(stats.MassKg-hand*RuntimeDensity)<.001,"Heron area times mean thickness agrees with volume and mass at "+offset);
            Check(misses==plan.Misses,$"ray miss count agrees with independent ray at {offset}: independent {misses}, production {plan.Misses}");
            var wrapper=JsonSerializer.Serialize(new{name=CompositePlate.NameForMode(ThicknessMode.FillToWall),mesh=JsonDocument.Parse(plan.Json).RootElement});
            using var loaded=JsonDocument.Parse(wrapper);var round=ComposerFill.Read(loaded.RootElement.GetProperty("mesh").GetRawText());
            Check(round.Vertices.SequenceEqual(plan.After.Vertices)&&round.Faces.Zip(plan.After.Faces).All(f=>f.First.VertexIds.SequenceEqual(f.Second.VertexIds)&&f.First.Thickness.SequenceEqual(f.Second.Thickness)&&f.First.Along.SequenceEqual(f.Second.Along)),"payload round-trip keeps vertices, cuts, winding, thickness and tm");
            Check(CompositePlate.ModeFromName(loaded.RootElement.GetProperty("name").GetString())==ThicknessMode.FillToWall,"mode identity round-trips");
            Check(original.SequenceEqual(File.ReadAllBytes(file)),"Back / discarded managed plans leave backup bytes unchanged");
            evidence.Add(new{xMm=offset,triangles=plan.After.FaceCount,vertices=plan.After.Vertices.Length,rearClamped=result.RearClamped.Count(x=>x),rayMisses=misses,omittedAtGun=result.OmittedAtGun,maxOffsetErrorMm=maxFit,maxClearanceErrorMm=maxEnd,volumeM3=stats.VolumeM3,massKg=stats.MassKg,handVolumeM3=hand,handMassKg=hand*RuntimeDensity});
            maximumFit=Math.Max(maximumFit,maxFit);maximumEnd=Math.Max(maximumEnd,maxEnd);
            File.WriteAllText(Path.Combine(output,$"conform-{offset:0}.mesh.json"),plan.Json);
        }
        var previous=ComposerFill.Cheeks(outer,bounds,new(),new(),halfGap,false,centre);
        var gunBox=Feat02253ComposerFillTests.Box(gun.Min,gun.Max).Select(t=>new SolidTriangle(t.A,t.B,t.C,Vector3.UnitZ,Raster.Pack(212,152,52,255))).ToList();
        foreach(var (view,direction) in new[]{("front",Vector3.UnitZ),("top",Vector3.UnitY)})
        {
            var camera=new Camera((bounds.Min,bounds.Max+new Vector3(0,0,.5f)),direction);
            File.WriteAllBytes(Path.Combine(preview,"preset-0.22.55-"+view+".png"),Render(Solids(previous.After).Concat(gunBox).ToList(),camera,outer));
            File.WriteAllBytes(Path.Combine(preview,"conform-0.22.56-"+view+".png"),Render(Solids(result450!.Plan.After).Concat(gunBox).ToList(),camera,outer));
        }
        ConformFixtures(Check);
        File.WriteAllText(Path.Combine(output,"CONFORM-CORNERS.tsv"),rows.ToString());
        File.WriteAllText(Path.Combine(output,"CHEEK-02256-EVIDENCE.json"),JsonSerializer.Serialize(new{passed=true,checks,blueprint=file,blueprintSha256=Convert.ToHexString(SHA256.HashData(original)),innerSurfaceSource="Managed proxy: saved per-face thickness and normal, fan tessellation; live native thickenFaceMesh acceptance pending",gunSource="Saved caliber/barrel-wall envelope proxy; live Renderer.bounds pending",densityKgM3=RuntimeDensity,maximumFitErrorMm=maximumFit,maximumClearanceErrorMm=maximumEnd,cases=evidence,forwardNormals=false,directionDeviation=ConformCheekFill.DirectionNote,nativeUndoTested=false,nativeSaveLoadTested=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT56 PASS: {checks} checks; fit {maximumFit:R} mm; clearance {maximumEnd:R} mm.");
    }
    // Independent intersection: plane distance plus dot-product barycentric containment.
    private static double? ConformIndependentRay(Vector3 p,Vector3 direction,IReadOnlyList<FillTriangle> walls)
    {
        var d=Vector3.Normalize(direction);double best=double.PositiveInfinity;
        foreach(var t in walls)
        {
            double ux=(double)t.B.X-t.A.X,uy=(double)t.B.Y-t.A.Y,uz=(double)t.B.Z-t.A.Z,vx=(double)t.C.X-t.A.X,vy=(double)t.C.Y-t.A.Y,vz=(double)t.C.Z-t.A.Z;
            double nx=uy*vz-uz*vy,ny=uz*vx-ux*vz,nz=ux*vy-uy*vx,den=nx*d.X+ny*d.Y+nz*d.Z;if(Math.Abs(den)<1e-14)continue;
            double distance=(nx*((double)t.A.X-p.X)+ny*((double)t.A.Y-p.Y)+nz*((double)t.A.Z-p.Z))/den;if(distance<=1e-5||distance>=best)continue;
            double qx=p.X+d.X*distance-t.A.X,qy=p.Y+d.Y*distance-t.A.Y,qz=p.Z+d.Z*distance-t.A.Z;
            double uu=ux*ux+uy*uy+uz*uz,uv=ux*vx+uy*vy+uz*vz,vv=vx*vx+vy*vy+vz*vz,qu=qx*ux+qy*uy+qz*uz,qv=qx*vx+qy*vy+qz*vz,det=uu*vv-uv*uv;
            if(det<=1e-18)continue;double a=(qu*vv-qv*uv)/det,b=(qv*uu-qu*uv)/det;
            if(a>=-1e-5&&b>=-1e-5&&a+b<=1+1e-5)best=distance;
        }
        return double.IsPositiveInfinity(best)?null:best;
    }
    private static void ConformFixtures(Action<bool,string> check)
    {
        var wall=new[]{new FillTriangle(new(.5f,0,1.5f),new(.5f,1,1.5f),new(1.5f,1,1.5f)),new FillTriangle(new(.5f,0,1.5f),new(1.5f,1,1.5f),new(1.5f,0,1.5f))};
        foreach(double x in new[]{300d,450d})
        {
            var r=ConformCheekFill.Plan(wall,x,new(),new());var stats=r.Plan.Inspect(4000,Array.Empty<FillBounds>());double hand=(x-10)*.001;
            check(r.Plan.After.FaceCount==2&&r.Plan.After.Vertices.Length==4&&r.Plan.Misses==0,"flat fixture preserves two triangles, shared edge and four vertices");
            check(Math.Abs(stats.VolumeM3-hand)<1e-6&&Math.Abs(stats.MassKg-hand*4000)<.01,"analytic 1 m2 times (X - 10 mm) at "+x);
            check(r.Plan.Inspect(4000,new[]{new FillBounds("crew seat",new(.6f,.1f,1.2f),new(.8f,.3f,1.49f))}).Warnings.Contains("crew seat"),"same overlap warning finds crew fixture");
        }
        var clipped=ConformCheekFill.Plan(wall.Select(t=>new FillTriangle(t.A-new Vector3(0,0,.8f),t.B-new Vector3(0,0,.8f),t.C-new Vector3(0,0,.8f))).ToArray(),450,new(),new());
        check(clipped.RearClamped.All(x=>x)&&clipped.Plan.After.Faces.All(f=>f.Thickness.All(t=>t==340)),"rear plane reduces 450 mm offset to 350 and thickness to 340 mm");
        var output=ConformCheekFill.Plan(wall,450,new(),new());
        using(var packed=JsonDocument.Parse(output.Plan.Json))
        {
            var e=packed.RootElement.GetProperty("edges").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            var expected=output.Triangles.SelectMany(v=>Enumerable.Range(0,3).Select(j=>(Math.Min(v[j],v[(j+1)%3]),Math.Max(v[j],v[(j+1)%3])))).ToHashSet();
            var actual=Enumerable.Range(0,e.Length/2).Select(i=>(Math.Min(e[2*i],e[2*i+1]),Math.Max(e[2*i],e[2*i+1]))).ToHashSet();
            check(e.Length==10&&actual.SetEquals(expected),"payload has exactly the five inherited unique triangle edges");
        }
        var reversed=wall.Select(t=>new FillTriangle(t.A,t.C,t.B)).ToArray();bool wrongSide=false;
        try{ConformCheekFill.Plan(reversed,450,new(),new());}catch(InvalidOperationException){wrongSide=true;}
        check(wrongSide,"outer/forward-facing triangles are refused as inner-wall input");
        bool shellRefused=false;
        var crossing=new[]{new FillTriangle(new(0,-1,1.2f),new(2,-1,1.2f),new(2,2,1.2f)),new FillTriangle(new(0,-1,1.2f),new(2,2,1.2f),new(0,2,1.2f))};
        try{ConformCheekFill.Plan(wall,450,new(),new(),shell:crossing);}catch(InvalidOperationException){shellRefused=true;}
        check(shellRefused,"fallback that would cross an earlier turret surface refuses Apply");
        var root=Fix02228Tests.SourceRoot();var ui=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));
        var dialog=ui[ui.IndexOf("    private static void ConformDialog()",StringComparison.Ordinal)..];dialog=dialog[..dialog.IndexOf("    private const float DerivedLabelWidth",StringComparison.Ordinal)];
        check(dialog.Contains("Replan(distance)")&&dialog.Contains(".OnChange(Replan)")&&dialog.Contains("row.Button(\"Back\",p.CloseModal)")&&dialog.Contains("TryPrepared(editor,prepared"),"Kit dialog recomputes preview and Back only closes; Apply uses existing native route");
        foreach(double bad in new[]{double.NaN,12,451}){bool refused=false;try{ConformCheekFill.Plan(wall,bad,new(),new());}catch(InvalidOperationException){refused=true;}check(refused,"invalid X refused: "+bad);}
    }
}
