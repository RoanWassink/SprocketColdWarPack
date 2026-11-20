using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class Feat02276RobustTests
{
 internal static void Run(Action<bool,string> check,bool before=false,bool measure=true)
 {
  var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
  var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
  var rows=new List<object>();var payloads=new Dictionary<string,string>();var layerSigs=new Dictionary<string,string>();int count=0,fail=0;void C(bool b,string text){count++;if(!b)fail++;check(b,"FEAT76 "+text);}
  using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-292-1721.blueprint")));
  string original=fixture.RootElement.GetProperty("meshes").EnumerateArray().First(m=>m.GetProperty("vuid").GetInt32()==22).GetProperty("meshData").GetProperty("mesh").GetRawText();
  File.WriteAllText(Path.Combine(output,"user-original.json"),original);
  foreach(string selection in new[]{"forward","outer"})foreach(string thickness in new[]{"saved","5mm"})foreach(string id in new[]{"t80u","t80u_hull","t72m1"})
  {
   string json=original;if(thickness=="5mm"){var n=JsonNode.Parse(json)!;foreach(var face in n["faces"]!.AsArray())for(int j=0;j<face!["t"]!.AsArray().Count;j++)face["t"]![j]=5;json=n.ToJsonString();}
   var mask=JsonSerializer.Deserialize<bool[]>(File.ReadAllText(Path.Combine(root,"tests/fixtures/follow-wall/user-selection-"+selection+".json")))!;var recipe=new CassetteModel(presets.First(p=>p.Id==id),materials,reactions);string name=selection+"-"+thickness+"-"+id;
   try
   {
    var r=LayerFaces.Convert(json,recipe,null,id=>materials[id].Density,Vector3.UnitY,5,null,mask);var old=LayerMesh.Read(json).Polygons;var converted=r.Sources.Where(s=>s.Converted).Select(s=>s.Index).ToHashSet();double Area(int i)=>CheekShellFill.Newell(old[i]).Length()*.5;double fraction=Enumerable.Range(0,mask.Length).Where(i=>mask[i]&&converted.Contains(i)).Sum(Area)/Enumerable.Range(0,mask.Length).Where(i=>mask[i]).Sum(Area);
    var deviation=LayerShape.Deviation(old,r.Plan.Json).Mm;File.WriteAllText(Path.Combine(output,"user-"+name+".json"),r.Plan.Json);
    payloads[name]=r.Plan.Json;layerSigs[name]=JsonSerializer.Serialize(r.Faces.Where(f=>f.Rank>=0).Select(f=>new{f.Source,f.Rank,front=f.Front.SelectMany(p=>new[]{p.X,p.Y,p.Z}),back=f.Back.SelectMany(p=>new[]{p.X,p.Y,p.Z}),f.Thickness,f.Modes}));rows.Add(new{name,selection,thickness,recipe=id,passed=true,selected=mask.Count(b=>b),layered=mask.Where((b,i)=>b&&converted.Contains(i)).Count(),layeredArea=fraction,maxOutsideMm=deviation,summary=r.Plan.Summary,walls=r.SelectedWallReasons,outcomes=r.Plan.FollowWall?.Faces,cheeks=new[]{-1,1}.Select(side=>new{cheek=side,selected=Enumerable.Range(0,mask.Length).Count(i=>mask[i]&&old[i][0].X*side>0),layered=Enumerable.Range(0,mask.Length).Count(i=>mask[i]&&old[i][0].X*side>0&&converted.Contains(i)),layeredArea=Enumerable.Range(0,mask.Length).Where(i=>mask[i]&&old[i][0].X*side>0&&converted.Contains(i)).Sum(Area)/Enumerable.Range(0,mask.Length).Where(i=>mask[i]&&old[i][0].X*side>0).Sum(Area)})});
    C(fraction>=.85,name+" >=85% selected area ("+fraction.ToString("P1")+")");C(deviation<=1,name+" shape guard");
    if(!before)
    {
     C(r.Faces.All(f=>f.Thickness.All(t=>t>=3)),name+" all face thickness >=3 mm");
     C(r.Faces.All(f=>f.Vertices.Distinct().Count()==f.Vertices.Length&&f.Front.All(ComposerFill.Finite)&&CheekShellFill.Newell(f.Front).LengthSquared()>1e-14f),name+" game front-face validity stand-in");
     C(r.Faces.Where(f=>f.Rank>=0).All(f=>Vector3.Dot(CheekShellFill.Newell(f.Back),f.Normal)>0),name+" no inverted native layer backs");
     var ranks=LayerRanks.FromJson(r.Plan.Json);C(r.Faces.Select((f,i)=>f.Rank<0||ranks[i]==f.Rank).All(x=>x),name+" exact saved layer ranks");
     C(r.Plan.CopyCheck!(r.Plan.Json)==null,name+" native copy validator");
     var weight=LayerWeight.Of(LayerMesh.Read(r.Plan.Json),ranks,r.Layout,recipe.Preset,id=>materials[id].Density,recipe.Passive.Density);C(double.IsFinite(weight.MassKg)&&weight.MassKg>0&&Math.Abs(weight.MassKg-r.TrueMassKg!.Value)<.01,name+" weight from emitted face volumes");
     C(LayerFaces.RestoreFront(r.Plan.MeshName!,r.Plan.Json).Json==json,name+" exact outer restore after layer serialization");
     ComposerFillBlueprintEvidence.FollowWallOverlay(r,C);
    }

    if(!before&&measure){FollowWallCoverage.Measure(json,r);FollowWallCoverage.ReviewMeasure(json,r,"user-"+selection+"-"+thickness,id,output);}
   }
   catch(Exception e){rows.Add(new{name,selection,thickness,recipe=id,passed=false,reason=e.Message});Console.WriteLine(name+": "+e.Message);if(!before)C(false,name+" conversion: "+e.Message);}
  }
  if(!before)
  {
   C(File.ReadAllBytes(Path.Combine(root,"tests/fixtures/Object-292-latest.blueprint")).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"tests/fixtures/Object-292-1721.blueprint"))),"latest-at-start save is byte-identical to the 17:21 fixture (same tested mesh)");
   foreach(string selection in new[]{"forward","outer"})foreach(string id in new[]{"t80u","t80u_hull","t72m1"})C(layerSigs.GetValueOrDefault(selection+"-saved-"+id)==layerSigs.GetValueOrDefault(selection+"-5mm-"+id),selection+" "+id+" layer geometry/thickness ignores original input thickness");
   var recipe=new CassetteModel(presets.First(p=>p.Id=="t80u_hull"),materials,reactions);double Density(string id)=>materials[id].Density;
   var altered=JsonNode.Parse(original)!;foreach(var f in altered["faces"]!.AsArray())f!["tm"]=0x08080808;
   var selected=JsonSerializer.Deserialize<bool[]>(File.ReadAllText(Path.Combine(root,"tests/fixtures/follow-wall/user-selection-forward.json")))!;
   C(LayerFaces.Convert(altered.ToJsonString(),recipe,null,Density,Vector3.UnitY,5,null,selected).Plan.Json==payloads["forward-saved-t80u_hull"],"original thicken modes do not affect selected layers");
   var shape=LayerFaces.ClosedSurface(original);foreach(int side in new[]{-1,1})C(shape.Where(p=>p[0].X*side>0).Sum(p=>Enumerable.Range(1,p.Length-2).Sum(k=>Vector3.Dot(p[0],Vector3.Cross(p[k],p[k+1]))/6.0))>.18,"closed shell oriented independently for cheek "+side);
   var first=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/follow-wall/box-fixture-straight.json")))!;var second=first.DeepClone();int offset=first["vertices"]!.AsArray().Count/3;
   for(int j=0;j<second["vertices"]!.AsArray().Count;j++)first["vertices"]!.AsArray().Add(second["vertices"]![j]!.GetValue<float>()+(j%3==0?3:0));
   foreach(var e in second["edges"]!.AsArray())first["edges"]!.AsArray().Add(e!.GetValue<int>()+offset);foreach(var e in second["edgeFlags"]!.AsArray())first["edgeFlags"]!.AsArray().Add(e!.GetValue<int>());
   foreach(var f in second["faces"]!.AsArray()){var copy=f!.DeepClone();for(int j=0;j<copy["v"]!.AsArray().Count;j++)copy["v"]![j]=copy["v"]![j]!.GetValue<int>()+offset;first["faces"]!.AsArray().Add(copy);}
   var mask=new bool[12];for(int i=0;i<6;i++)mask[i]=true;mask[6]=true;
   var partial=LayerFaces.Convert(first.ToJsonString(),recipe,null,Density,Vector3.UnitZ,5,null,mask);C(partial.Plan.FollowWall!=null&&partial.Sources.Any(s=>s.Converted),"a partial selection produces a plan with its diagnostic (0.22.80: the fully selected box has no back and runs to the chosen thickness; the yellow warning is tested in FEAT80)");
   C(partial.Plan.Summary.Contains("of 7 selected faces got layers")&&partial.Plan.Summary.Contains("kept as steel wall"),"honest summary with count and selected area");
   var thin=first.DeepClone();foreach(var f in thin["faces"]!.AsArray())for(int j=0;j<f!["t"]!.AsArray().Count;j++)f["t"]![j]=1;
   var thinResult=LayerFaces.Convert(thin.ToJsonString(),recipe,null,Density,Vector3.UnitZ,5,null,mask);
   C(thinResult.Sources.Any(s=>s.Converted)&&thinResult.Faces.Where(f=>f.Rank<0&&mask[f.Source]).All(f=>f.Thickness.All(t=>t<=1)),"selected fallback walls never exceed even a 1 mm original corner");

   for(int bits=0;bits<16;bits++){bool designer=(bits&1)!=0,applying=(bits&2)!=0,refused=(bits&4)!=0,warning=(bits&8)!=0;C(FollowWallDump.ShouldWrite(designer,applying,refused,warning)==(designer&&applying&&(refused||warning)),"diagnostic gating "+bits);}
   var attempt=partial.Plan.FollowWall!;string dir=Path.Combine(output,"diagnostic-test");var messages=new List<string>();FollowWallDump.TryWrite(dir,324,attempt,messages.Add);string path=Path.Combine(dir,"follow-wall-plate-324.json");using(var d=JsonDocument.Parse(File.ReadAllText(path)))C(d.RootElement.GetProperty("faces").GetArrayLength()==7&&d.RootElement.GetProperty("selection").GetArrayLength()==12&&d.RootElement.GetProperty("recipe").ValueKind==JsonValueKind.Object,"dump contains mesh, mask, recipe and every selected outcome");
   FollowWallDump.TryWrite(dir,324,attempt with{Summary="overwritten"},messages.Add);C(messages.Count==0&&Directory.GetFiles(dir,"*.json").Length==1&&File.ReadAllText(path).Contains("overwritten"),"one diagnostic file overwritten per plate");
   FollowWallDump.TryWrite(path,324,attempt,messages.Add);C(messages.Count==1,"dump write failure logs once and does not throw");
   var panel=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));C(panel.Contains("prepared.Plan.FollowWall?.Warning!=true?StatusKind.Success:StatusKind.Warning"),"partial high-wall success is visibly yellow in panel");
   bool none=false;try{LayerFaces.Convert(Feat02280FollowFrontTests.Pentagon(),recipe,null,Density,Vector3.UnitZ,5,null,new[]{true});}catch(FollowWallFailure e){none=e.Message.Contains("None of the selected faces")&&e.Faces.Length==1;} // 0.22.80: a fully selected box now converts; no face of a pentagon can hold layersC(none,"zero-layer refusal keeps typed outcomes for every selected face");
  }
  File.WriteAllText(Path.Combine(output,"FEAT76-"+(before?"BEFORE":"RESULT")+".json"),JsonSerializer.Serialize(new{passed=fail==0,checks=count,failures=fail,cases=rows},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"FEAT76 {(before?"BEFORE":"PASS")}: {count} checks, {fail} failures");
 }
}
