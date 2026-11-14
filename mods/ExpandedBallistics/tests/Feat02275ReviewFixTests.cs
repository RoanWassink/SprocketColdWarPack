using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;
internal static class Feat02275ReviewFixTests
{
    const BindingFlags F=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    internal static void Run(Action<bool,string> sink,string tag="after")
    {
        int count=0,failures=0;var rows=new List<object>();
        void Check(bool ok,string text){count++;if(!ok)failures++;rows.Add(new{passed=ok,test=text});sink(ok,"REVIEW75: "+text);}
        void Test(string text,Func<bool> body){try{Check(body(),text);}catch(Exception e){Check(false,text+" ("+(e.InnerException??e).GetType().Name+")");}}
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        var recipe=new CassetteModel(presets.First(p=>p.Id=="t80u_hull"),materials,reactions);double Density(string id)=>materials[id].Density;
        string original=File.ReadAllText(Path.Combine(output,"object292-original.json"));bool[] selection=JsonSerializer.Deserialize<bool[]>(File.ReadAllText(Path.Combine(output,"selection-forward.json")))!;
        var r=LayerFaces.Convert(original,recipe,null,Density,Vector3.UnitY,5,null,selection);string name=r.Plan.MeshName!;
        foreach(var bad in new[]{name[..^7],CompositePlate.LayerBlocksName+CompositePlate.FrontMarker+"%%%]"})
            Test("R1 corrupt/truncated metadata returns no saved front",()=>LayerFaces.LoadFront(bad)==null);
        Test("R1 once-per-plate warning gate",()=>{var type=typeof(LayerFaces).Assembly.GetType("SprocketExpandedBallistics.FrontWarningOnce");if(type==null)return false;var method=type.GetMethod("First",F)!;return (bool)method.Invoke(null,new object[]{"review-plate-A"})!&&!(bool)method.Invoke(null,new object[]{"review-plate-A"})!&&(bool)method.Invoke(null,new object[]{"review-plate-B"})!;});
        Test("R1 panel shares guarded/cached MeshName path",()=>{var s=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));return !s.Contains("LoadFront(plate.Mesh.ToMeshBlueprint(false)?.Name)")&&s.Contains("ReadSavedFront(plate");});
        // A real native-formula back is 1.2 mm outside a 20 mm closed slab. Output face 7 belongs to source 2, not source 7.
        Test("R2 protruding back attributes output 7 to source 2",()=>
        {
            string box=Box();using var doc=JsonDocument.Parse(box);var old=LayerMesh.Read(box).Polygons;
            var front=new[]{new Vector3(-.1f,-.1f,-.0182f),new Vector3(.1f,-.1f,-.0182f),new Vector3(.1f,.1f,-.0182f),new Vector3(-.1f,.1f,-.0182f)};
            var payload=JsonSerializer.Serialize(new{vertices=front.SelectMany(p=>new[]{p.X,p.Y,p.Z}),edges=new[]{0,1,1,2,2,3,3,0},edgeFlags=new int[4],faces=Enumerable.Range(0,8).Select(_=>new{v=new[]{0,1,2,3},t=new[]{3,3,3,3},tm=0,te=0})});
            var fs=Enumerable.Range(0,8).Select(i=>new LayerFaceInfo(i==7?2:1,0,new[]{0,1,2,3},front,front.Select(p=>p-Vector3.UnitZ*.003f).ToArray(),new ushort[]{3,3,3,3},new byte[4],new int[4],Vector3.UnitZ)).ToArray();
            var detail=typeof(LayerShape).GetMethod("DeviationDetails",F);var mapper=typeof(LayerFaces).GetMethod("OutsideFailure",F);
            if(detail==null||mapper==null){var legacy=LayerShape.Deviation(old,payload,i=>i==7?1:double.PositiveInfinity);return int.Parse(Regex.Match(legacy.Where,@"face (\d+)").Groups[1].Value)-1==2;}
            var d=detail.Invoke(null,new object?[]{old,payload,(Func<int,double>)(i=>i==7?1:double.PositiveInfinity)})!;
            double mm=(double)d.GetType().GetProperty("Mm",F)!.GetValue(d)!;var result=mapper.Invoke(null,new object?[]{d,fs})!;
            return mm>.19&&mm<.21&&(int?)result.GetType().GetProperty("SourceFace",F)!.GetValue(result)==2;
        });
        var emitted=r.Faces.Where(f=>new[]{101,102,103,117,118,119}.Contains(f.Source)).ToArray();
        Test("R3 rejected Object292 faces retain original corner thickness or report forced reduction",()=>
        {
            var old=ComposerFill.Read(original,true);var reduction=typeof(LayerConversion).GetProperty("ReducedSelectedWalls",F);int reduced=reduction==null?0:(int)reduction.GetValue(r)!;
            var thinner=emitted.Where(f=>f.Rank<0).Where(f=>f.Thickness.Where((t,j)=>t<old.Faces[f.Source].Thickness[Array.FindIndex(old.Faces[f.Source].Corners,p=>Vector3.Distance(p,f.Front[j])<1e-7)]).Any()).Select(f=>f.Source).Distinct().Count();
            return emitted.Select(f=>f.Source).Distinct().Count()==6&&thinner==reduced&&(reduced==0||r.Inspection.Contains("thinner"));
        });
        var wallRows=emitted.Select(f=>new{face=f.Source+1,thickness=f.Thickness,rank=f.Rank}).ToArray();File.WriteAllText(Path.Combine(output,"review-round3","wall-thickness-"+tag+".json"),JsonSerializer.Serialize(wallRows));
        string open=Open();
        Test("R4 selected open plate ignores selection and matches legacy payload",()=>{var plain=LayerFaces.Convert(open,recipe,null,Density,Vector3.UnitZ);var selected=LayerFaces.Convert(open,recipe,null,Density,Vector3.UnitZ,5,null,new[]{true});return plain.Plan.Json==selected.Plan.Json&&selected.Inspection.Contains("The selection is ignored: this plate is not a closed shape.");});
        Test("R4 mixed selected closed/open pieces refuse",()=>{var closed=JsonNode.Parse(Box())!;var loose=JsonNode.Parse(open)!;var vs=closed["vertices"]!.AsArray();int offset=vs.Count/3;var extra=loose["vertices"]!.AsArray();for(int i=0;i<extra.Count;i++)vs.Add(extra[i]!.GetValue<float>()+(i%3==0?3:0));var edges=closed["edges"]!.AsArray();foreach(var e in loose["edges"]!.AsArray())edges.Add(e!.GetValue<int>()+offset);foreach(var e in loose["edgeFlags"]!.AsArray())closed["edgeFlags"]!.AsArray().Add(e!.GetValue<int>());var face=loose["faces"]![0]!.DeepClone();foreach(var index in Enumerable.Range(0,face["v"]!.AsArray().Count))face["v"]![index]=face["v"]![index]!.GetValue<int>()+offset;closed["faces"]!.AsArray().Add(face);var mask=new bool[7];mask[0]=mask[6]=true;try{LayerFaces.Convert(closed.ToJsonString(),recipe,null,Density,Vector3.UnitZ,5,null,mask);return false;}catch(InvalidOperationException e){return e.Message.Contains("closed");}});
        Test("R5 moved layered vertex refuses saved outer restore",()=>{var changed=JsonNode.Parse(r.Plan.Json)!;changed["vertices"]![0]=changed["vertices"]![0]!.GetValue<float>()+.01f;try{Restore(name,changed.ToJsonString());return false;}catch(Exception e){return (e.InnerException??e).Message.Contains("This plate was edited after it was turned into layers. Undo the edit, or shape a new box.");}});
        Test("R5 unchanged mesh wrapper restores exact original",()=>Restore(name,r.Plan.Json).Json==original);
        Test("R5 excessive saved name refuses before write",()=>{var node=JsonNode.Parse(Box())!;var bytes=new byte[60000];new Random(75).NextBytes(bytes);node["nonce"]=Convert.ToBase64String(bytes);var method=typeof(LayerFaces).GetMethod("SaveFront",F)!;try{method.Invoke(null,method.GetParameters().Length==4?new object?[]{ThicknessMode.Layers,node.ToJsonString(),new[]{true,false,false,false,false,false},r.Plan.Json}:new object?[]{ThicknessMode.Layers,node.ToJsonString(),new[]{true,false,false,false,false,false}});return false;}catch(Exception e){return (e.InnerException??e).Message.Contains("32,000")||(e.InnerException??e).Message.Contains("32000");}});
        Directory.CreateDirectory(Path.Combine(output,"review-round3"));File.WriteAllText(Path.Combine(output,"review-round3","tests-"+tag+".json"),JsonSerializer.Serialize(new{passed=failures==0,checks=count,failures,cases=rows},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"REVIEW75 {tag}: {count} checks, {failures} failures");
    }
    private static LayerBoxBack Restore(string name,string current){var method=typeof(LayerFaces).GetMethod("RestoreFront",F)!;return (LayerBoxBack)method.Invoke(null,method.GetParameters().Length==2?new object[]{name,current}:new object[]{name})!;}
    private static string Open()=>"{\"vertices\":[-0.5,-0.5,0,0.5,-0.5,0,0.5,0.5,0,-0.5,0.5,0],\"edges\":[0,1,1,2,2,3,3,0],\"edgeFlags\":[0,0,0,0],\"faces\":[{\"v\":[0,1,2,3],\"t\":[400,400,400,400],\"tm\":0,\"te\":0}]}";
    private static string Box(){var n=JsonNode.Parse(File.ReadAllText(Path.Combine(Fix02228Tests.OutputRoot(),"box-fixture-straight.json")))!;var vs=n["vertices"]!.AsArray();for(int i=2;i<vs.Count;i+=3)if(vs[i]!.GetValue<float>()<0)vs[i]=-.02f;return n.ToJsonString();}
}
