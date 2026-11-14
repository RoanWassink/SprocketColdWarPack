using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class Feat02275FollowWallTests
{
    internal static bool IsPlanned(string rel)=>new[]{"ComposerFill.cs","CompositePlate.cs","CassetteComposerModel.cs","CassetteComposerPanel.cs","RuntimeComposerFill.cs","RuntimeCompositePlateEdit.cs","Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md"}.Contains(rel);
    static double Area(Vector3[] p)=>CheekShellFill.Newell(p).Length()*.5;
    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT75: "+message);}
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        CassetteModel Recipe(string id)=>new(presets.First(p=>p.Id==id),materials,reactions);
        double Density(string id)=>materials[id].Density;
        using var bp=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-292-follow-wall.blueprint")));
        var json=bp.RootElement.GetProperty("meshes").EnumerateArray().First(m=>m.GetProperty("vuid").GetInt32()==2).GetProperty("meshData").GetProperty("mesh").GetRawText();
        var mesh=LayerMesh.Read(json);var polys=mesh.Polygons;var normals=polys.Select(p=>Vector3.Normalize(CheekShellFill.Newell(p))).ToArray();
        var mid=polys.Select(p=>p.Aggregate(Vector3.Zero,(a,b)=>a+b)/p.Length).ToArray();
        var measurements=new List<object>();var rows=new StringBuilder("selection\trecipe\tcheek\tclosed_m3\tfilled_union_fraction\tsolid_union_fraction\tfilled_union_m3\tsolid_union_m3\tlayered_area_fraction\twall_faces\tmax_outside_mm\n");
        File.WriteAllText(Path.Combine(output,"object292-original.json"),json);
        foreach(var selection in new[]{"outer","forward"})
        {
            // Mesh +Y = vehicle forward, +Z = up. Forward Ny>.3: exactly 25 faces per cheek.
            // Outer abs(Nz)<.7 and (Ny>.3 or Nx*sign(centre.X)>.3): exactly 31 per cheek.
            var selected=Enumerable.Range(0,polys.Length).Select(i=>selection=="forward"?normals[i].Y>.3f:Math.Abs(normals[i].Z)<.7f&&(normals[i].Y>.3f||normals[i].X*Math.Sign(mid[i].X)>.3f)).ToArray();
            Check(selected.Count(x=>x)==(selection=="forward"?50:62),selection+" reproducible selection");
            File.WriteAllText(Path.Combine(output,"selection-"+selection+".json"),JsonSerializer.Serialize(selected));
            foreach(var id in new[]{"t80u","t80u_hull","t72m1"})
            {
                var recipe=Recipe(id);LayerConversion r;
                try{r=LayerFaces.Convert(json,recipe,null,Density,Vector3.UnitY,5,null,selected);}
                catch(InvalidOperationException ex){Check(false,selection+" "+id+" refused: "+ex.Message);continue;}
                Check(true,selection+" "+id+" converts");
                File.WriteAllText(Path.Combine(output,selection+"-"+id+".json"),r.Plan.Json);
                var coverage=FollowWallCoverage.Measure(json,r);var deviation=LayerShape.Deviation(polys,r.Plan.Json);
                FollowWallCoverage.ReviewMeasure(json,r,selection,id,output);
                File.WriteAllText(Path.Combine(output,selection+"-"+id+"-coverage.json"),JsonSerializer.Serialize(coverage,new JsonSerializerOptions{WriteIndented=true}));
                Check(deviation.Mm<=1,selection+" "+id+" every front/native back corner <=1 mm outside");
                Check(r.Faces.All(f=>f.Thickness.All(t=>t>=3)),selection+" "+id+" minimum thickness >=3 mm");
                Check(r.Faces.All(f=>f.Vertices.Distinct().Count()==f.Vertices.Length&&f.Front.All(ComposerFill.Finite)&&CheekShellFill.Newell(f.Front).LengthSquared()>1e-14f),selection+" "+id+" game front-face validity stand-in: finite, distinct corners, area");
                Check(r.Faces.Where(f=>f.Rank>=0).All(f=>Vector3.Dot(CheekShellFill.Newell(f.Back),f.Normal)>0),selection+" "+id+" no inverted layer backs");
                var saved=LayerMesh.Read(r.Plan.Json);var ranks=LayerRanks.FromJson(r.Plan.Json);
                Check(r.Faces.Select((f,i)=>f.Rank<0||ranks[i]==f.Rank).All(x=>x),selection+" "+id+" exact layer ranks after JSON read-back");
                Check(r.Plan.CopyCheck!(r.Plan.Json)==null,selection+" "+id+" native copy checker accepts JSON");
                var weight=LayerWeight.Of(saved,ranks,r.Layout,recipe.Preset,Density,recipe.Passive.Density);
                Check(double.IsFinite(weight.MassKg)&&weight.MassKg>0&&Math.Abs(weight.MassKg-r.TrueMassKg!.Value)<.01,selection+" "+id+" mass derives from emitted native face volumes/densities");
                ComposerFillBlueprintEvidence.FollowWallOverlay(r,Check);
                // Actual blueprint mesh wrapper shape and name are written/read from disk; native clean reload remains a game gate.
                var wrapper=(JsonObject)JsonNode.Parse("{\"meshes\":[{\"vuid\":2,\"meshData\":{}}]}")!;
                var data=wrapper["meshes"]![0]!["meshData"]!;data["name"]=r.Plan.MeshName;data["mesh"]=JsonNode.Parse(r.Plan.Json);
                var path=Path.Combine(output,selection+"-"+id+"-roundtrip.blueprint");File.WriteAllText(path,wrapper.ToJsonString());
                var loaded=JsonNode.Parse(File.ReadAllText(path))!["meshes"]![0]!["meshData"]!;var name=loaded["name"]!.GetValue<string>();
                Check(CompositePlate.LayoutFromName(name)==r.Layout.Kind&&CompositePlate.ModeFromName(name)==r.Mode,"saved name retains layered uniform-model identity");
                var box=LayerFaces.RestoreFront(name,loaded["mesh"]!.ToJsonString());var front=LayerFaces.LoadFront(name)!.Value;
                Check(box.Json==json&&front.Front.SequenceEqual(selected),"Back to box restores exact original mesh and selection");
                var again=LayerFaces.Convert(box.Json,Recipe(id=="t72m1"?"t80u":"t72m1"),null,Density,Vector3.UnitY,5,null,front.Front);
                Check(again.ConvertedFaces>0,"reapply different recipe after blueprint round-trip");
                var model=new CassetteComposerModel(new CassetteComposer(materials,reactions,"","",_=>false),Array.Empty<ComposerMaterial>(),Array.Empty<ComposerMaterial>(),()=>Array.Empty<CassettePreset>());
                model.SetTarget(new("test",176,"ebCassette_t80u","T80U",true,20,20,ConvertBlocker:null,FrontNote:LayerFaces.SelectionNote(json,selected)));model.SetApplyChoice(ComposerApplyChoice.ConvertToLayers);
                Check(model.ApplyNote.Contains("Front: "+selected.Count(x=>x)+" selected faces in 2 closed pieces")&&model.ApplyChoiceHelp.Contains("With faces selected, those are the front and the layers follow them inward."),"window note/help carry selection meaning");
                foreach(var cv in coverage)
                {
                    int side=cv.cheek;var indices=Enumerable.Range(0,polys.Length).Where(i=>selected[i]&&polys[i][0].X*side>0).ToArray();var converted=r.Sources.Where(s=>s.Converted).Select(s=>s.Index).ToHashSet();
                    double area=indices.Sum(i=>Area(polys[i])),layered=indices.Where(converted.Contains).Sum(i=>Area(polys[i]));
                    double closed=polys.Where(p=>p[0].X*side>0).Sum(p=>Enumerable.Range(1,p.Length-2).Sum(k=>Vector3.Dot(p[0],Vector3.Cross(p[k],p[k+1]))/6.0));
                    int walls=indices.Count(i=>!converted.Contains(i));
                    double cheekDeviation=LayerShape.Deviation(polys,r.Plan.Json,fi=>r.Plan.After.Faces[fi].Corners[0].X*side>0?0:double.PositiveInfinity).Mm;
                    measurements.Add(new{selection,recipe=id,cheek=side,closedVolumeM3=closed,cv.coverage,cv.solidFraction,cv.inside,cv.covered,cv.solid,layeredAreaFraction=layered/area,wallFaces=walls,maxOutsideMm=cheekDeviation,markerLength=name.Length,reducedSelectedWalls=r.ReducedSelectedWalls});
                    rows.AppendLine(string.Join("\t",selection,id,side,F(closed),F(cv.coverage),F(cv.solidFraction),F(cv.coverage*closed),F(cv.solidFraction*closed),F(layered/area),walls,F(cheekDeviation)));
                    if(selection=="outer"&&id=="t80u")Check(cv.coverage>=.6,"outer T80U air-inclusive union >=60% for cheek "+side);
                }
                Console.WriteLine(selection+" "+id+": "+r.SelectedWalls+" selected faces kept as steel wall; "+r.SelectedWallReasons);
            }
        }
        // Unselected component remains byte-equivalent in its face data and positions after compaction.
        var oneSide=Enumerable.Range(0,polys.Length).Select(i=>polys[i][0].X<0&&Math.Abs(normals[i].Z)<.7f&&(normals[i].Y>.3f||-normals[i].X>.3f)).ToArray();
        var single=LayerFaces.Convert(json,Recipe("t80u_hull"),null,Density,Vector3.UnitY,5,null,oneSide);
        Check(single.Inspection.Contains("1 closed piece has no selected faces and stays as it is"),"unselected piece note");
        Check(single.Faces.Select((f,i)=>(f,i)).Where(x=>x.f.Front[0].X>0).All(x=>LayerMesh.Read(single.Plan.Json).Polygons[x.i].SequenceEqual(polys[x.f.Source])&&single.Plan.After.Faces[x.i].Thickness.SequenceEqual(ComposerFill.Read(json,true).Faces[x.f.Source].Thickness)),"unselected cheek points and original thickness retained");
        Check(LayerFaces.SelectionNote(json,null)=="This plate has 2 closed pieces: select the outer faces to choose the front.","multi-piece no-selection note");
        ComposerFillBlueprintEvidence.FollowWallBoxes(Recipe("t80u_hull"),Density,Check);
        // B: the real folded faces are downgraded; deliberately choosing the entire closed surface exceeds one third and must refuse atomically.
        Check(single.SelectedWalls>0&&single.SelectedWallReasons!.Contains("face "),"B: bad selected faces become counted/logged steel walls");
        string refusal="";try{LayerFaces.Convert(json,Recipe("t80u_hull"),null,Density,Vector3.UnitY,5,null,Enumerable.Repeat(true,polys.Length).ToArray());}catch(InvalidOperationException e){refusal=e.Message;}
        Check(refusal.Contains("None of the selected faces"),"B: no selected face can form layers refuses without plan");
        File.WriteAllText(Path.Combine(output,"OBJECT292-MEASUREMENTS.tsv"),rows.ToString());
        File.WriteAllText(Path.Combine(output,"OBJECT292-MEASUREMENTS.json"),JsonSerializer.Serialize(new{method="12 mm fixed lattice; seed 0, cell offset (.37,.43,.61); parity in original closed surface, union of tetrahedralized full front/back prisms including air plus native solid prisms; each inside point counted once",cases=measurements},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"FEAT75-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures,liveNativeTested=false}));
        Console.WriteLine($"FEAT75 PASS: {checks} checks, {failures} failures");
        static string F(double x)=>x.ToString("R",CultureInfo.InvariantCulture);
    }
}
internal static partial class ComposerFillBlueprintEvidence
{
    internal static void FollowWallOverlay(LayerConversion r,Action<bool,string> check)
    {
        var m=LayerMesh.Read(r.Plan.Json) with{FollowWall=r.Plan.MeshName?.Contains(CompositePlate.FrontMarker,StringComparison.Ordinal)==true};var rank=LayerRanks.FromJson(r.Plan.Json);var (copy,_)=SyntheticOverlay(r,true);
        foreach(bool painted in new[]{false,true})
        {
            var result=painted?LayerOverlay.RemapPainted(copy,m,rank,r.Layout):LayerOverlay.Remap(copy,m,rank,r.Layout);
            check(result.Triangles.Length==copy.Triangles.Length&&result.Vertices.All(ComposerFill.Finite)&&result.Uv.All(v=>float.IsFinite(v.X)&&float.IsFinite(v.Y)),"both cheeks pure overlay "+(painted?"painted":"layers")+" preserves triangle coverage and finite coordinates");
        }
        var (variant,owners,parts,_) = MakeCopy(m,CopyVariants[0]);
        foreach(bool painted in new[]{false,true})
        {
            var drawn=painted?LayerOverlay.RemapPainted(variant,m,rank,r.Layout):LayerOverlay.Remap(variant,m,rank,r.Layout);
            var holes=HolesOf(m,rank,variant,owners,parts,drawn);
            check(holes.Fronts==0&&holes.Backs==0&&(!painted||holes.Walls==0),$"overlay exterior coverage {painted}: missing fronts/backs/walls {holes.Fronts}/{holes.Backs}/{holes.Walls}");
        }
    }
    internal static void FollowWallBoxes(CassetteModel recipe,Func<string,double> density,Action<bool,string> check)
    {
        foreach(var (tag,json,fwd,centre) in new[]{("straight",Box75(),Vector3.UnitZ,(Vector3?)null),("T90A",T90AClosed(),T90AForward,(Vector3?)T90ACentre)})
        {
            File.WriteAllText(Path.Combine(Fix02228Tests.OutputRoot(),"box-fixture-"+tag+".json"),json);
            var old=LayerFaces.Convert(json,recipe,null,density,fwd,5,centre);var mask=new bool[ComposerFill.Read(json,true).FaceCount];foreach(var s in old.Sources.Where(s=>s.Converted))mask[s.Index]=true;
            var selected=LayerFaces.Convert(json,recipe,null,density,fwd,5,centre,mask);
            var a=old.Faces.Where(f=>f.Rank==0).SelectMany(f=>f.Front).Concat(old.Faces.Where(f=>f.Rank==old.Layout.Groups.Length-1).SelectMany(f=>f.Back)).ToArray();var b=selected.Faces.Where(f=>f.Rank==0).SelectMany(f=>f.Front).Concat(selected.Faces.Where(f=>f.Rank==selected.Layout.Groups.Length-1).SelectMany(f=>f.Back)).ToArray();
            check(a.Max(p=>b.Min(q=>Vector3.Distance(p,q)))*1000<=1&&b.Max(p=>a.Min(q=>Vector3.Distance(p,q)))*1000<=1,tag+" selected/no-selection outer front/back layers <=1 mm");
        }
    }
    private static string Box75()
    {
        var v=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0),new Vector3(-.5f,-.5f,-.4f),new Vector3(.5f,-.5f,-.4f),new Vector3(.5f,.5f,-.4f),new Vector3(-.5f,.5f,-.4f)};
        int[][] f={new[]{0,1,2,3},new[]{5,4,7,6},new[]{4,5,1,0},new[]{3,2,6,7},new[]{4,0,3,7},new[]{1,5,6,2}};
        var edges=f.SelectMany(p=>Enumerable.Range(0,p.Length).Select(i=>(Math.Min(p[i],p[(i+1)%p.Length]),Math.Max(p[i],p[(i+1)%p.Length])))).Distinct().SelectMany(e=>new[]{e.Item1,e.Item2}).ToArray();
        return JsonSerializer.Serialize(new{majorVersion=0,minorVersion=3,vertices=v.SelectMany(p=>new[]{p.X,p.Y,p.Z}),edges,edgeFlags=new int[edges.Length/2],faces=f.Select(v=>new{v,t=new[]{20,20,20,20},tm=0,te=0})});
    }
}
