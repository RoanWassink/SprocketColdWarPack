using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
/// <summary>
/// 0.22.80 "Layers follow the front": the layers follow the selected front inward to the back of the same closed piece, or to the chosen thickness where a corner
/// line finds no back (an open sheet: always); one face that cannot hold layers becomes a wall or is left out instead of refusing the plate; a copy the game does
/// not keep gets one more plan. Tests on the user's fixtures (Object 293 at 20:38 and the dump of 21:14, Object 292 of 17:21) and on open sheets.
/// </summary>
internal static class Feat02280FollowFrontTests
{
    /// <summary>The production files 0.22.80 may change on top of 0.22.79.</summary>
    internal static bool IsPlanned(string rel)=>rel is "ComposerFill.cs" or "RuntimeComposerFill.cs" or "RuntimeCompositePlateEdit.cs" or "CassetteComposerModel.cs" or "CassetteComposerPanel.cs" or "CheekShellFill.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    /// <summary>RuntimeComposerFill.cs without the two 0.22.80 edits (the chosen thickness passed to Convert), for the 0.22.79 ledger check of that file.</summary>
    internal static string Without80Fill(string s)=>s.Replace(",bool diagnosticApply=false,double? layerDepthMm=null)",",bool diagnosticApply=false)",StringComparison.Ordinal).Replace("RuntimeLayerFaces.Centre(plate),selected,layerDepthMm);}","RuntimeLayerFaces.Centre(plate),selected);}",StringComparison.Ordinal);
    private static string F(double v,string format="0.###")=>v.ToString(format,CultureInfo.InvariantCulture);

    internal static void Run(Action<bool,string> check)
    {
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        CassetteModel Recipe(string id)=>new(presets.First(p=>p.Id==id),materials,reactions);double Density(string id)=>materials[id].Density;
        int count=0,fail=0;var rows=new List<string>{string.Join("\t","case","recipe","result","selected","layered","area %","walls","left out","wall triangles left out","corners without back","chosen mm","max outside mm (layers)","source face 145","source face 71","0.22.74 refusal stopped at source face","summary / refusal")};
        void C(bool ok,string text){count++;if(!ok)fail++;check(ok,"FEAT80 "+text);}
        var panels=new List<Panel>();

        // ---------- (1) Object 293, plate #324: the user's three cases ----------
        using var dump=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-293-follow-wall-plate-324.json")));
        string dumpMesh=dump.RootElement.GetProperty("mesh").GetRawText();var userSelection=dump.RootElement.GetProperty("selection").EnumerateArray().Select(x=>x.GetBoolean()).ToArray();
        using var bp=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-293-2038.blueprint")));
        string bpMesh=bp.RootElement.GetProperty("meshes").EnumerateArray().First(m=>m.GetProperty("vuid").GetInt32()==2).GetProperty("meshData").GetProperty("mesh").GetRawText();
        var objects=bp.RootElement.GetProperty("objects").EnumerateArray().ToDictionary(o=>o.GetProperty("vuid").GetInt32(),o=>o);
        C(objects[323].GetProperty("plateStructure").GetInt32()==324&&objects[323].GetProperty("structureBlueprintVuid").GetInt32()==36,"Object 293: object 323 is plate #324 (structure blueprint 36, mesh vuid 2)");
        var (rot,pos)=World(objects,objects[323]);var inv=Transpose(rot);
        var forward=Vector3.Normalize(Mul(inv,Vector3.UnitZ));var centre=Mul(inv,-pos);
        C(Enumerable.Range(0,userSelection.Length).Where(i=>userSelection[i]).Select(i=>i+1).SequenceEqual(new[]{15,30,119}),"the dump selects faces 15, 30 and 119");
        File.WriteAllText(Path.Combine(output,"object293-dump-mesh.json"),dumpMesh);File.WriteAllText(Path.Combine(output,"object293-2038-mesh.json"),bpMesh);
        // 0.22.79 behaviour, reproduced with the base source compiled in (LegacyLayerFaces079 below is not available: the refusals are recorded from the base run, see FEAT80-BASE.txt).
        LayerConversion? userCase=null,noneDump=null,noneBp=null;
        foreach(var id in new[]{"t72m1","t80u","t80u_hull"})
        {
            var recipe=Recipe(id);
            foreach(var (name,json,mask) in new[]{("Object 293 dump, faces 15/30/119",dumpMesh,(bool[]?)userSelection),("Object 293 dump, no selection",dumpMesh,null),("Object 293 20:38, no selection",bpMesh,null)})
            {
                try
                {
                    var watch=System.Diagnostics.Stopwatch.StartNew();var r=LayerFaces.Convert(json,recipe,null,Density,forward,5,centre,mask);watch.Stop();Console.WriteLine($"FEAT80 timing: {name} {id}: {watch.ElapsedMilliseconds} ms");
                    C(r.Sources.Any(s=>s.Converted),$"{name} {id}: a conversion, no refusal ({r.Plan.Summary})");
                    bool closed=name.Contains("dump",StringComparison.Ordinal);
                    var maxOut=Validate(C,name+" "+id,json,r,recipe,closed);
                    var sel=mask??r.Plan.FollowWall?.Selected??r.Sources.Select(x=>x.Converted).ToArray();
                    int layered=sel.Where((b,i)=>b&&r.Sources[i].Converted).Count();double area=Area(json,sel,i=>r.Sources[i].Converted);
                    string Face(int k)=>k<r.Sources.Length?r.Sources[k].Converted?"layers":r.LeftOutWallTriangleFaces.Contains(k)?$"{r.Sources[k].Skip}, a triangle left out":r.LeftOutFaces.Contains(k)?"front face left out (kept as it was)":r.SelectedWallFaces.Contains(k)?"front face kept as steel wall":r.Sources[k].Skip=="other face"?"not front, kept as it was":r.Sources[k].Skip??"?":"-";
                    rows.Add(string.Join("\t",name,id,"converted",sel.Count(b=>b),layered,F(100*area,"0.0"),r.SelectedWallFaces.Length,r.LeftOutFaces.Length,r.LeftOutWallTriangles,r.DepthCorners,"",F(maxOut,"0.00"),Face(144),Face(70),r.AutoFrontRefusedFace is {} rf?$"{rf+1} ({Face(rf)})":"",r.Plan.Summary));
                    if(id=="t72m1"){if(mask!=null)userCase=r;else if(closed)noneDump=r;else noneBp=r;}
                    if(mask!=null)C(layered==3,$"{name} {id}: all three selected faces get layers ({layered})");
                    C(r.Plan.Summary.StartsWith("Convert to layers: ",StringComparison.Ordinal)&&!r.Plan.Summary.Contains('\n')&&r.Plan.Summary.Contains("got layers",StringComparison.Ordinal),$"{name} {id}: one-line message with the counts");
                    if(r.Plan.FollowWall!=null)C(r.Plan.FollowWall.Warning==(area<2.0/3),$"{name} {id}: yellow exactly when more than a third of the front area got no layers ({F(100*area,"0")} %)");
                }
                catch(Exception e){rows.Add(string.Join("\t",name,id,"refused","","","","","","","","","","","","",e.Message));C(false,$"{name} {id}: refused: {e.Message}");}
            }
        }
        if(userCase!=null)
        {
            C(userCase.LeftOutWallTriangleFaces.Contains(144),"face 145 (the wall of the refusal on 14-11) is now a local fix: its triangle that cannot hold 3 mm is left out, the conversion goes on");
            C(userCase.Sources[144].Skip=="steel wall","face 145 stays a steel wall apart from that triangle");
        }
        if(noneBp!=null)C(noneBp.AutoFrontRefusedFace is {} stopped&&noneBp.Inspection.Contains($"it stopped at source face {stopped+1}",StringComparison.Ordinal),"Object 293 20:38: the window names the source face where the 0.22.74 planner stopped (output face 71 of its plan)");
        if(noneBp!=null)C(noneBp.Inspection.Contains("box rules could not convert",StringComparison.Ordinal)&&noneBp.Inspection.Contains("stick out of the box by 205.3 mm (face 71, corner 1, back point)",StringComparison.Ordinal),"Object 293 20:38 without a selection: the 0.22.74 refusal (205.3 mm, face 71) is named and the faces that face the front are the front instead");

        // ---------- (2) Object 292 (17:21): converted and validated (herpak: the comparison with the 0.22.76/0.22.79 output folder is removed) ----------
        {
            var original=File.ReadAllText(Path.Combine(output,"user-original.json"));
            static double SolidVolume(string payload){var m=LayerMesh.Read(payload);var ranks=LayerRanks.FromJson(payload);double v=0;for(int i=0;i<m.Polygons.Length;i++)if(ranks[i]>=0)v+=ComposerFill.PrismVolume(m.Polygons[i],m.Backs[i]);return v;}
            foreach(var sel in new[]{"forward","outer"})foreach(var th in new[]{"saved","5mm"})foreach(var id in new[]{"t80u","t80u_hull","t72m1"})
            {
                var name=$"{sel}-{th}-{id}";var json=th=="saved"?original:File.ReadAllText(Path.Combine(output,"user-original.json"));
                if(th=="5mm"){var node=JsonNode.Parse(json)!;foreach(var face in node["faces"]!.AsArray())for(int j=0;j<face!["t"]!.AsArray().Count;j++)face["t"]![j]=5;json=node.ToJsonString();}
                var mask=JsonSerializer.Deserialize<bool[]>(File.ReadAllText(Path.Combine(root,"tests/fixtures/follow-wall/user-selection-"+sel+".json")))!;
                try
                {
                    var watch=System.Diagnostics.Stopwatch.StartNew();var r=LayerFaces.Convert(json,Recipe(id),null,Density,Vector3.UnitY,5,null,mask);watch.Stop();Console.WriteLine($"FEAT80 timing: Object 292 {name}: {watch.ElapsedMilliseconds} ms");int layered=mask.Where((b,i)=>b&&r.Sources[i].Converted).Count();
                    double area=Area(original,mask,i=>r.Sources[i].Converted);
                    double vol=SolidVolume(r.Plan.Json);
                    C(r.Plan.Json==File.ReadAllText(Path.Combine(output,"user-"+name+".json")),$"Object 292 {name}: the FEAT76 run of this suite wrote the same layers");
                    var maxOut=Validate(C,"Object 292 "+name,json,r,Recipe(id),true);
                    if(th=="saved")rows.Add(string.Join("\t","Object 292 "+sel,id,"converted",mask.Count(b=>b),layered,F(100*area,"0.0"),r.SelectedWallFaces.Length,r.LeftOutFaces.Length,r.LeftOutWallTriangles,r.DepthCorners,"",F(maxOut,"0.00"),"","","",r.Plan.Summary+" Layer volume "+F(vol*1000,"0.0")+" l."));
                }
                catch(Exception e){C(false,$"Object 292 {name}: refused: {e.Message}");}
            }
        }

        // ---------- (3) open sheets: flat (1 face), kinked (3 faces), curved (12 faces); no selection and a partial one; a recipe with air and one without ----------
        LayerConversion? curvedShown=null;string curvedJson="";
        foreach(var (sheet,json) in new[]{("flat sheet (1 face)",Sheet(1,0)),("kinked sheet (3 faces)",Sheet(3,15)),("curved sheet (12 faces)",Sheet(12,7.5))})
        foreach(var id in new[]{"t80u","t72m1"})
        foreach(var partial in new[]{false,true})
        foreach(double? chosen in new double?[]{null,300})
        {
            int n=JsonNode.Parse(json)!["faces"]!.AsArray().Count;bool[]? mask=null;
            if(partial){if(n==1)continue;mask=new bool[n];for(int i=n/3;i<n-n/3;i++)mask[i]=true;}
            var recipe=Recipe(id);var layout=LayerLayout.Build(recipe.Preset,LayerLayoutKind.PerLayer);double want=chosen??layout.DepthMm;
            string name=$"{sheet}, {(partial?"partial selection":"no selection")}, {id}, thickness {(chosen==null?"default":F(want,"0"))}";
            try
            {
                var r=LayerFaces.Convert(json,recipe,null,Density,Vector3.UnitZ,5,null,mask,chosen);
                var sel=mask??Enumerable.Repeat(true,n).ToArray();
                C(sel.Where((b,i)=>b).Count()==sel.Where((b,i)=>b&&r.Sources[i].Converted).Count(),name+": every front face gets layers");
                C(Math.Abs(want-(chosen??Math.Round(recipe.Preset.Layers.Sum(l=>LayerLayout.LayerDepth(l)),9)))<1e-6,name+$": the chosen thickness is {F(want,"0.#")} mm (default: the recipe depth with air)");
                // the depth along every corner line = the chosen thickness (+-1 mm); no point of any layer outside 0..thickness along its line
                double worst=0,outside=0;int G=r.Layout.Groups.Length;
                foreach(var s in r.Sources.Where(s=>s.Converted))
                    for(int j=0;j<s.Front.Length;j++)
                    {
                        var u=s.LineDirection[j];
                        foreach(var f in r.Faces.Where(f=>f.Source==s.Index&&f.Rank>=0))
                        {
                            double a=Vector3.Dot(f.Front[j]-s.Front[j],u)*1000,b=Vector3.Dot(f.Back[j]-s.Front[j],u)*1000;
                            outside=Math.Max(outside,Math.Max(Math.Max(-a,-b),Math.Max(a-want,b-want)));
                            if(f.Rank==G-1)worst=Math.Max(worst,Math.Abs(b-want));
                        }
                    }
                C(worst<=1,name+$": the depth along every corner line is the chosen thickness within 1 mm (worst {F(worst,"0.00")} mm)");
                C(outside<=1,name+$": no layer point lies outside 0..thickness along its line ({F(Math.Max(0,outside),"0.00")} mm)");
                C(r.DepthCorners==r.Sources.Where(s=>s.Converted).SelectMany(s=>s.Front).Distinct().Count(),name+": every front corner runs to the chosen thickness (an open sheet has no back)");
                C(r.Faces.Where(f=>f.Rank<0).All(f=>!sel[f.Source]),name+": an open sheet gets no walls; the faces that are not the front stay as they were");
                Validate(C,name,json,r,recipe,false);
                C(r.Plan.Summary.Contains($"run to the chosen thickness ({F(want,"0")} mm)",StringComparison.Ordinal),name+": the message says the pack ran to the chosen thickness");
                if(!partial)C(r.Inspection.Contains("open sheet: the whole sheet is the front",StringComparison.Ordinal),name+": without a selection the whole sheet is the front");
                rows.Add(string.Join("\t",name,id,"converted",sel.Count(b=>b),sel.Where((b,i)=>b&&r.Sources[i].Converted).Count(),"100",r.SelectedWallFaces.Length,r.LeftOutFaces.Length,r.LeftOutWallTriangles,r.DepthCorners,F(want,"0"),"","","","",r.Plan.Summary));
                if(sheet.StartsWith("curved",StringComparison.Ordinal)&&id=="t80u"&&!partial&&chosen==null){curvedShown=r;curvedJson=json;}
            }
            catch(Exception e){rows.Add(string.Join("\t",name,id,"refused","","","","","","","","","","","","",e.Message));C(false,name+": refused: "+e.Message);}
        }

        // ---------- (4) mixed: a closed box (front split in two, back split in two) with the front and the right half of the back selected: the corners on the
        // left and in the middle run to the back (600 mm), the two on the right meet only the selected right half of the back and stop at the chosen 300 mm ----------
        {
            float w=.2f,d=.6f;
            var p=new[]{new Vector3(-w,-w,0),new Vector3(w,-w,0),new Vector3(w,w,0),new Vector3(-w,w,0),new Vector3(-w,-w,-d),new Vector3(w,-w,-d),new Vector3(w,w,-d),new Vector3(-w,w,-d),new Vector3(0,-w,-d),new Vector3(0,w,-d),new Vector3(0,-w,0),new Vector3(0,w,0)};
            // front-left, bottom-left, bottom-right, right, top-right, top-left, left, back-left, back-right, front-right
            var box=Mesh(p,new[]{new[]{0,10,11,3},new[]{0,4,8,10},new[]{10,8,5,1},new[]{1,5,6,2},new[]{2,6,9,11},new[]{11,9,7,3},new[]{3,7,4,0},new[]{4,7,9,8},new[]{8,9,6,5},new[]{10,1,2,11}});
            var mask=new[]{true,false,false,false,false,false,false,false,true,true};
            foreach(var id in new[]{"t80u","t72m1"})
            {
                var r=LayerFaces.Convert(box,Recipe(id),null,Density,Vector3.UnitZ,5,null,mask,300);
                var ends=new Dictionary<int,(double Length,float Z)>();var ids=new[]{new[]{0,10,11,3},new[]{10,1,2,11}};
                foreach(var (fi,k) in new[]{(0,0),(9,1)})if(r.Sources[fi].Converted)for(int j2=0;j2<4;j2++)ends[ids[k][j2]]=(r.Sources[fi].LineLengthMm[j2],r.Sources[fi].Back[j2].Z);
                C(ends.Count==6&&new[]{0,3,10,11}.All(v=>Math.Abs(ends[v].Z+.6f)<=.001f)&&new[]{1,2}.All(v=>Math.Abs(ends[v].Length-300)<=1&&Math.Abs(ends[v].Z+.3f)<=.001f),$"mixed box {id}: the left and middle front corners run to the back (z = -600 mm), the right ones find no back and stop at the chosen 300 mm ({string.Join(", ",ends.OrderBy(x=>x.Key).Select(x=>x.Key+": "+F(x.Value.Length,"0.#")+" mm to z "+F(x.Value.Z*1000,"0")))}; {r.Plan.Summary})");
                C(r.DepthCorners>=2,$"mixed box {id}: the corners without a back are counted ({r.DepthCorners})");
                var maxOut=Validate(C,"mixed box "+id,box,r,Recipe(id),true);
                rows.Add(string.Join("\t","mixed box (front and right half of the back selected)",id,"converted",mask.Count(b=>b),mask.Where((b,i)=>b&&r.Sources[i].Converted).Count(),"",r.SelectedWallFaces.Length,r.LeftOutFaces.Length,r.LeftOutWallTriangles,r.DepthCorners,"300",F(maxOut,"0.00"),"","","",r.Plan.Summary));
            }
        }

        // ---------- (5) no selection on a closed plate: the 0.22.74 planner, with the advice in the window ----------
        {
            var straight=File.ReadAllText(Path.Combine(root,"tests/fixtures/follow-wall/box-fixture-straight.json"));var recipe=Recipe("t80u_hull");
            var r=LayerFaces.Convert(straight,recipe,null,Density,Vector3.UnitZ,5,null);
            C(!r.Inspection.Contains("Tip:",StringComparison.Ordinal)&&r.Plan.FollowWall==null,"closed box without a selection: the box planner and its window as before");
            C(File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs")).Contains("if(selected==0&&storedFront==null&&layerSnapshot!=null)frontNote=(string.IsNullOrEmpty(frontNote)?\"\":frontNote+\". \")+LayerFaces.SelectFrontAdvice.TrimEnd('.');",StringComparison.Ordinal),"without a selection the note of the composer advises to select the front");
        }

        // ---------- (6) the game rewrites te on corners that are not AlongEdgeManual; a face it does not keep gets one more plan ----------
        if(userCase!=null)
        {
            var planned=userCase.Plan.Json;var game=GameCopy(planned);
            C(game!=planned&&LayerFaces.VerifyCopy(planned,game)==null&&userCase.Plan.CopyCheck!(game)==null,"Object 293 faces 15/30/119: a copy with te rewritten on every corner that is not tm 4 (as the game does at rebuild) passes the copy check");
            C(LayerFaces.CopyDifferences(planned,game).Length==0,"no face differs for that copy");
            var broken=BreakManualSlot(planned,out int brokenFace);
            var different=LayerFaces.CopyDifferences(planned,broken);
            C(LayerFaces.VerifyCopy(planned,broken)!=null&&different.SequenceEqual(new[]{brokenFace}),$"a copy with a changed rib slot on output face {brokenFace+1} is refused and exactly that face differs");
            var second=userCase.Plan.CopyRetry!(broken);var source=userCase.Faces[brokenFace].Source;
            C(second!=null&&second.FollowWall!.Faces.Any(f=>f.Face==source+1&&!f.Layered)&&second.Summary.Contains("got layers",StringComparison.Ordinal),$"the one retry plans source face {source+1} as wall and still converts ({second?.Summary})");
            C(second!=null&&second.CopyRetry==null,"the second plan has no further retry (one retry only)");
            C(second!=null&&LayerFaces.VerifyCopy(second.Json,GameCopy(second.Json))==null,"the second plan passes the copy check on its own game copy");
            var attempt=userCase.Plan.FollowWall! with{PlannedJson=planned,GameJson=broken,CopyProblem=LayerFaces.VerifyCopy(planned,broken),DifferentFaces=different.Select(i=>i+1).ToArray()};
            string dir=Path.Combine(output,"diagnostic-test-80");if(Directory.Exists(dir))Directory.Delete(dir,true);var logs=new List<string>();
            FollowWallDump.TryWrite(dir,324,attempt,logs.Add);
            using(var d=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"follow-wall-plate-324.json"))))
                C(logs.Count==0&&d.RootElement.GetProperty("planned").GetProperty("faces").GetArrayLength()==JsonDocument.Parse(planned).RootElement.GetProperty("faces").GetArrayLength()&&d.RootElement.GetProperty("game").ValueKind==JsonValueKind.Object&&d.RootElement.GetProperty("differentOutputFaces")[0].GetInt32()==brokenFace+1&&d.RootElement.GetProperty("copyProblem").GetString()!.Contains("rib slot",StringComparison.Ordinal),"the dump holds the planned net, the game's copy, the reason and the faces that differ");
            C(FollowWallDump.Build(userCase.Plan.FollowWall!).Contains("\"planned\"",StringComparison.Ordinal)==false,"a dump without a game copy keeps the 0.22.76 format");
            var edit=File.ReadAllText(Path.Combine(root,"RuntimeCompositePlateEdit.cs"));
            C(edit.Contains("prepared.Plan.CopyRetry!(refusal.Json)",StringComparison.Ordinal)&&edit.Contains("CopyRefusal=(copyJson,copyProblem);throw new InvalidOperationException(copyProblem);",StringComparison.Ordinal)&&edit.Contains("ok=Run(editor,again.Material,null,again.Mode,wasComposite,again.Plan.Label,\"\",out message,again);",StringComparison.Ordinal),"TryPrepared keeps the game's copy, dumps it and applies the second plan once");
        }

        // ---------- (7) refusals that remain: no face can get layers ----------
        {
            bool refused=false;string text="";
            try{LayerFaces.Convert(Pentagon(),Recipe("t80u"),null,Density,Vector3.UnitZ,5,null,new[]{true});}catch(FollowWallFailure e){refused=e.Message.StartsWith("None of the selected faces can hold layers",StringComparison.Ordinal)&&e.Faces.Length==1;text=e.Message;}
            C(refused,"a selection where no face can get layers is still refused, with every selected face in the dump ("+text+")");
            var warn=LayerFaces.Convert(PentagonAndQuad(),Recipe("t80u"),null,Density,Vector3.UnitZ,5,null,new[]{true,true});
            C(warn.Plan.FollowWall!.Warning&&warn.Sources[1].Converted&&!warn.Sources[0].Converted,"more than a third of the selected area without layers: a yellow warning and a plan");
        }

        // ---------- (8) source: no new hook, the Solid mark line in Weigh, the thickness of the window reaches the planner ----------
        {
            int hooks=Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories).Where(f=>!f.Contains(Path.DirectorySeparatorChar+"tests"+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!f.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar,StringComparison.Ordinal)).Sum(f=>System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f),@"\[HarmonyPatch").Count);
            C(hooks==47,$"[HarmonyPatch count stays 47 ({hooks})");
            var fill=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));var weigh=fill[fill.IndexOf("internal static void Weigh(PlateStructure plate)",StringComparison.Ordinal)..];
            C(weigh.Contains("        try\r\n        {\r\n            NativeMaterialReactions.SolidMarkBuilt(plate);",StringComparison.Ordinal),"Weigh still calls NativeMaterialReactions.SolidMarkBuilt(plate) first");
            C(File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs")).Contains("layerDepthMm:action==ComposerMeshAction.LayerConvert&&model?.DepthMm>0?model.ApplyDepthMm:null",StringComparison.Ordinal)&&fill.Contains("RuntimeLayerFaces.Centre(plate),selected,layerDepthMm);}",StringComparison.Ordinal),"the Thickness of the window reaches Convert to layers");
        }

        // ---------- contact sheet ----------
        if(userCase!=null)
        {
            var polys=Polys(dumpMesh);var sel=userSelection;
            var layer=userCase.Faces.Where(f=>f.Rank>=0).ToArray();
            var c0=Enumerable.Range(0,sel.Length).Where(i=>sel[i]).SelectMany(i=>polys[i]).Aggregate(Vector3.Zero,(a,b)=>a+b)/Enumerable.Range(0,sel.Length).Where(i=>sel[i]).Sum(i=>polys[i].Length);
            var view=Vector3.Normalize(-forward+new Vector3(.35f,.25f,0));
            panels.Add(View("Object 293 #324 before: faces 15, 30, 119 selected (orange)",polys.Select((p,i)=>(p,sel[i]?"#ec943e":"#c5d0d8")).ToList(),view));
            panels.Add(View("after (t72m1): layer fronts by layer, walls grey",polys.Select((p,i)=>(p,"#e3e8ec")).Concat(userCase.Faces.Where(f=>f.Rank<0&&f.Source>=0&&!sel[f.Source]).Select(f=>(f.Front,"#9aa6b0"))).Concat(layer.OrderBy(f=>-f.Rank).Select(f=>(f.Front,Rank(f.Rank)))).ToList(),view));
            panels.Add(Section("after: section through the selected faces (layers in colour, old shape black)",polys,userCase,c0,Vector3.UnitX));
            panels.Add(Section("after: second section (across)",polys,userCase,c0,Vector3.Normalize(Vector3.Cross(forward,Vector3.UnitX))));
        }
        if(curvedShown!=null)
        {
            var polys=Polys(curvedJson);
            panels.Add(View("open curved sheet (12 faces), no selection: before",polys.Select(p=>(p,"#ec943e")).ToList(),Vector3.Normalize(new Vector3(.4f,.5f,1))));
            panels.Add(View("after (t80u, 803 mm): layer fronts",polys.Select(p=>(p,"#e3e8ec")).Concat(curvedShown.Faces.Where(f=>f.Rank>=0).OrderBy(f=>-f.Rank).Select(f=>(f.Front,Rank(f.Rank)))).ToList(),Vector3.Normalize(new Vector3(.4f,.5f,1))));
            panels.Add(Section("after: section across the sheet (to the chosen thickness)",polys,curvedShown,new Vector3(0,0,0),Vector3.UnitY));
        }
        {
            var original=File.ReadAllText(Path.Combine(output,"user-original.json"));var polys=Polys(original);
            foreach(var (label,file) in new[]{("Object 292 outer t80u, 0.22.80",Path.Combine(output,"user-outer-saved-t80u.json"))})
            {
                var payload=File.ReadAllText(file);var mesh=LayerMesh.Read(payload);var ranks=LayerRanks.FromJson(payload);
                var faces=mesh.Polygons.Select((p,i)=>new LayerFaceInfo(-1,ranks[i],mesh.Ids[i],p,mesh.Backs[i],Array.Empty<ushort>(),Array.Empty<byte>(),Array.Empty<int>(),mesh.Normals[i])).ToArray();
                panels.Add(SectionFaces(label+": section z = -0.2 m",polys,faces,new Vector3(0,0,-.2f),Vector3.UnitZ));
            }
        }
        File.WriteAllText(Path.Combine(output,"preview","layers-follow-front-panels.json"),JsonSerializer.Serialize(panels.Select(p=>new{title=p.Title,shapes=p.Shapes.Select(s=>new{points=s.Points.Select(q=>new[]{q.X,q.Y}),fill=s.Fill,line=s.Line,width=s.Width})})));
        File.WriteAllText(Path.Combine(output,"FOLLOW-FRONT-CASES.tsv"),string.Join("\n",rows)+"\n");
        File.WriteAllText(Path.Combine(output,"FEAT80-RESULT.json"),JsonSerializer.Serialize(new{passed=fail==0,checks=count,failures=fail},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT80 {(fail==0?"PASS":"FAIL")}: {count} checks, {fail} failures");
    }

    /// <summary>The checks every conversion must pass (the game's view where we can imitate it). Returns the largest distance (mm) of a layer point outside the old closed shape.</summary>
    private static double Validate(Action<bool,string> C,string name,string json,LayerConversion r,CassetteModel recipe,bool closed)
    {
        var payload=r.Plan.Json;var ranks=LayerRanks.FromJson(payload);
        C(r.Faces.Select((f,i)=>f.Rank<0||ranks[i]==f.Rank).All(x=>x),name+": the saved mesh reads back the planned layer of every layer face");
        C(r.Faces.Where(f=>f.Rank>=0).All(f=>f.Thickness.All(t=>t>=3)),name+": every layer corner is at least 3 mm");
        C(r.Faces.Where(f=>f.Rank>=0).All(f=>Vector3.Dot(CheekShellFill.Newell(f.Back),f.Normal)>0&&CheekShellFill.Newell(f.Front).LengthSquared()>1e-14f),name+": no layer turns over or is degenerate");
        C(LayerFaces.VerifyCopy(payload,GameCopy(payload))==null,name+": the copy check accepts a game copy with te rewritten on non-manual corners");
        if(r.Plan.MeshName!=null)C(LayerFaces.RestoreFront(r.Plan.MeshName,payload).Json==json,name+": Remove layers (Back to box) gives the original plate back");
        C(double.IsFinite(r.TrueMassKg??double.NaN)&&r.TrueMassKg>0,name+": the weight comes from the faces");
        if(!closed)return 0;
        var shape=LayerFaces.ClosedSurface(json);
        var detail=LayerShape.DeviationDetails(shape,payload,fi=>fi<r.Faces.Length&&r.Faces[fi].Rank>=0?0:double.PositiveInfinity);
        C(detail.Mm<=1,name+$": every layer point lies within 1 mm of the old outer shape ({F(detail.Mm,"0.00")} mm{(detail.Mm>0?", "+detail.Where:"")})");
        return detail.Mm;
    }
    private static double Area(string json,bool[] mask,Func<int,bool> layered)
    {
        var p=Polys(json);double all=0,got=0;for(int i=0;i<mask.Length;i++)if(mask[i]){var a=CheekShellFill.Newell(p[i]).Length()*.5;all+=a;if(layered(i))got+=a;}return all>0?got/all:0;
    }
    internal static Vector3[][] Polys(string json)
    {
        var n=JsonNode.Parse(json)!;var v=n["vertices"]!.AsArray().Select(x=>x!.GetValue<float>()).ToArray();
        return n["faces"]!.AsArray().Select(f=>f!["v"]!.AsArray().Select(x=>{int i=x!.GetValue<int>();return new Vector3(v[3*i],v[3*i+1],v[3*i+2]);}).ToArray()).ToArray();
    }
    /// <summary>The game at rebuild: te of a corner that is not AlongEdgeManual is whatever edge it picks (here: the edge at that vertex, not of the face, with the lowest n.u, else 0); the numbers go through float.</summary>
    internal static string GameCopy(string payload)
    {
        var n=JsonNode.Parse(payload)!;var v=n["vertices"]!.AsArray().Select(x=>x!.GetValue<float>()).ToArray();var e=n["edges"]!.AsArray().Select(x=>x!.GetValue<int>()).ToArray();
        Vector3 P(int i)=>new(v[3*i],v[3*i+1],v[3*i+2]);
        var at=new Dictionary<int,List<int>>();for(int k=0;k+1<e.Length;k+=2)foreach(var x in new[]{e[k],e[k+1]}){if(!at.TryGetValue(x,out var l))at[x]=l=new();l.Add(k/2);}
        foreach(var face in n["faces"]!.AsArray())
        {
            var ids=face!["v"]!.AsArray().Select(x=>x!.GetValue<int>()).ToArray();if(ids.Length>4)continue;
            if(!long.TryParse(face["tm"]?.ToJsonString()??"0",NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var tm)||!long.TryParse(face["te"]?.ToJsonString()??"0",NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var te))continue;
            var normal=Vector3.Normalize(CheekShellFill.Newell(ids.Select(P).ToArray()));ulong bits=(ulong)te;
            for(int j=0;j<ids.Length;j++)
            {
                if((((ulong)tm>>(8*j))&0xFF)==CheekShellFill.AlongEdgeManual)continue;
                int pick=0;double best=double.MaxValue;
                foreach(var edge in at.GetValueOrDefault(ids[j])??new List<int>())
                {
                    int w=e[2*edge]==ids[j]?e[2*edge+1]:e[2*edge];if(ids.Contains(w))continue;var d=P(w)-P(ids[j]);if(d.LengthSquared()<1e-14f)continue;
                    double dot=Vector3.Dot(normal,Vector3.Normalize(d));if(dot<best){best=dot;pick=edge;}
                }
                if(pick==(int)((bits>>(16*j))&0xFFFF))pick=(pick+1)&0x7FFF;
                bits=bits&~(0xFFFFUL<<(16*j))|((ulong)(uint)pick<<(16*j));
            }
            face["te"]=JsonValue.Create(unchecked((long)bits));
        }
        n["vertices"]=new JsonArray(v.Select(x=>(JsonNode)JsonValue.Create(float.Parse(x.ToString("R",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture))!).ToArray());
        return n.ToJsonString();
    }
    private static string BreakManualSlot(string payload,out int face)
    {
        var n=JsonNode.Parse(payload)!;face=-1;var faces=n["faces"]!.AsArray();
        for(int i=0;i<faces.Count;i++)
        {
            long tm=long.Parse(faces[i]!["tm"]!.ToJsonString(),CultureInfo.InvariantCulture);if((tm&0xFF)!=CheekShellFill.AlongEdgeManual)continue;
            long te=long.Parse(faces[i]!["te"]!.ToJsonString(),CultureInfo.InvariantCulture);faces[i]!["te"]=JsonValue.Create(te^1);face=i;break;
        }
        return n.ToJsonString();
    }
    /// <summary>An open sheet in the plane z = 0 (flat) or bent about the y axis (kinked/curved: <paramref name="degrees"/> per face), normals toward +z (the outside); 0.6 m tall, faces 0.3 m wide.</summary>
    internal static string Sheet(int faces,double degrees)
    {
        var verts=new List<float>();var edges=new List<int>();var fs=new List<string>();
        int Add(Vector3 p){verts.Add(p.X);verts.Add(p.Y);verts.Add(p.Z);return verts.Count/3-1;}
        double radius=degrees>0?.3/(2*Math.Sin(degrees*Math.PI/360)):0;double start=-(faces*degrees)/2;
        Vector3 At(int k,float y)
        {
            if(degrees==0)return new((float)(-.15*faces+.3*k),y,0);
            double a=(start+k*degrees)*Math.PI/180;return new((float)(radius*Math.Sin(a)),y,(float)(radius*Math.Cos(a)-radius));
        }
        var low=Enumerable.Range(0,faces+1).Select(k=>Add(At(k,-.3f))).ToArray();var high=Enumerable.Range(0,faces+1).Select(k=>Add(At(k,.3f))).ToArray();
        var edgeSet=new Dictionary<(int,int),int>();int E(int a,int b){var k=(Math.Min(a,b),Math.Max(a,b));if(!edgeSet.TryGetValue(k,out var i)){i=edgeSet.Count;edgeSet[k]=i;edges.Add(a);edges.Add(b);}return i;}
        for(int k=0;k<faces;k++)
        {
            var ids=new[]{low[k],low[k+1],high[k+1],high[k]};for(int j=0;j<4;j++)E(ids[j],ids[(j+1)%4]);
            fs.Add("{\"v\":["+string.Join(",",ids)+"],\"t\":[20,20,20,20],\"tm\":16843009,\"te\":0}");
        }
        return "{\"majorVersion\":0,\"minorVersion\":3,\"vertices\":["+string.Join(",",verts.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+"],\"edges\":["+string.Join(",",edges)+"],\"edgeFlags\":["+string.Join(",",Enumerable.Repeat(0,edges.Count/2))+"],\"faces\":["+string.Join(",",fs)+"]}";
    }
    private static string Mesh(Vector3[] p,int[][] faces)
    {
        var edges=new List<int>();var set=new HashSet<(int,int)>();
        foreach(var f in faces)for(int j=0;j<f.Length;j++){var k=(Math.Min(f[j],f[(j+1)%f.Length]),Math.Max(f[j],f[(j+1)%f.Length]));if(set.Add(k)){edges.Add(k.Item1);edges.Add(k.Item2);}}
        return "{\"majorVersion\":0,\"minorVersion\":3,\"vertices\":["+string.Join(",",p.SelectMany(q=>new[]{q.X,q.Y,q.Z}).Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+"],\"edges\":["+string.Join(",",edges)+"],\"edgeFlags\":["+string.Join(",",Enumerable.Repeat(0,edges.Count/2))+"],\"faces\":["+
            string.Join(",",faces.Select(f=>"{\"v\":["+string.Join(",",f)+"],\"t\":["+string.Join(",",f.Select(_=>20))+"],\"tm\":0,\"te\":0}"))+"]}";
    }
    internal static string Pentagon()=>Mesh(Enumerable.Range(0,5).Select(k=>new Vector3(MathF.Cos(k*2*MathF.PI/5)*.3f,MathF.Sin(k*2*MathF.PI/5)*.3f,0)).ToArray(),new[]{new[]{0,1,2,3,4}});
    private static string PentagonAndQuad()
    {
        var p=Enumerable.Range(0,5).Select(k=>new Vector3(MathF.Cos(k*2*MathF.PI/5)*.3f,MathF.Sin(k*2*MathF.PI/5)*.3f,0)).Concat(new[]{new Vector3(1,0,0),new Vector3(1.1f,0,0),new Vector3(1.1f,.1f,0),new Vector3(1,.1f,0)}).ToArray();
        return Mesh(p,new[]{new[]{0,1,2,3,4},new[]{5,6,7,8}});
    }

    // ---------- the plate frame of Object 293 (Unity Z-X-Y Euler, as ComposerFillBlueprintEvidence) ----------
    private static float[] Euler(float x,float y,float z)
    {
        float rx=x*MathF.PI/180,ry=y*MathF.PI/180,rz=z*MathF.PI/180;float cx=MathF.Cos(rx),sx=MathF.Sin(rx),cy=MathF.Cos(ry),sy=MathF.Sin(ry),cz=MathF.Cos(rz),sz=MathF.Sin(rz);
        return MM(MM(new[]{cy,0,sy,0,1,0,-sy,0,cy},new[]{1,0,0,0,cx,-sx,0,sx,cx}),new[]{cz,-sz,0,sz,cz,0,0,0,1});
    }
    private static float[] MM(float[] a,float[] b){var r=new float[9];for(int i=0;i<3;i++)for(int j=0;j<3;j++){float s=0;for(int k=0;k<3;k++)s+=a[3*i+k]*b[3*k+j];r[3*i+j]=s;}return r;}
    private static Vector3 Mul(float[] m,Vector3 v)=>new(m[0]*v.X+m[1]*v.Y+m[2]*v.Z,m[3]*v.X+m[4]*v.Y+m[5]*v.Z,m[6]*v.X+m[7]*v.Y+m[8]*v.Z);
    private static float[] Transpose(float[] m)=>new[]{m[0],m[3],m[6],m[1],m[4],m[7],m[2],m[5],m[8]};
    private static (float[] R,Vector3 T) World(Dictionary<int,JsonElement> objects,JsonElement o)
    {
        var ts=o.GetProperty("transform");var rot=ts.GetProperty("rot");var pos=ts.GetProperty("pos");
        var r=Euler(rot[0].GetSingle(),rot[1].GetSingle(),rot[2].GetSingle());var p=new Vector3(pos[0].GetSingle(),pos[1].GetSingle(),pos[2].GetSingle());
        int parent=o.GetProperty("pvuid").GetInt32();if(parent<0||!objects.TryGetValue(parent,out var po))return (r,p);
        var (pr,pt)=World(objects,po);return (MM(pr,r),Mul(pr,p)+pt);
    }

    // ---------- pictures: orthographic views and sections, drawn by preview/Render-Follow-Front.ps1 ----------
    private sealed record Shape(Vector2[] Points,string? Fill,string? Line,float Width);
    private sealed record Panel(string Title,List<Shape> Shapes);
    private static readonly string[] Colours={"#e6ad61","#5b8bb7","#56b2ab","#ba789e","#d6cf7c","#ce8974","#84ad70","#8f80b7","#ddab9b","#4e9d94","#e3b957","#818fa6"};
    private static string Rank(int r)=>Colours[((r%Colours.Length)+Colours.Length)%Colours.Length];
    private static (Vector3 Right,Vector3 Up) Basis(Vector3 d)
    {
        var up=Math.Abs(d.Y)<.9f?Vector3.UnitY:Vector3.UnitX;var right=Vector3.Normalize(Vector3.Cross(up,d));return (right,Vector3.Cross(d,right));
    }
    private static Panel View(string title,List<(Vector3[] Poly,string Fill)> polys,Vector3 d)
    {
        var (right,up)=Basis(d);
        var shapes=polys.Select((x,i)=>(x,i,depth:x.Poly.Average(p=>Vector3.Dot(p,d)))).OrderBy(x=>x.depth).ThenBy(x=>x.i).Select(x=>new Shape(x.x.Poly.Select(p=>new Vector2(Vector3.Dot(p,right),Vector3.Dot(p,up))).ToArray(),x.x.Fill,"#33414d",.6f)).ToList();
        return new(title,shapes);
    }
    private static Panel Section(string title,Vector3[][] oldShape,LayerConversion r,Vector3 point,Vector3 normal)=>SectionFaces(title,oldShape,r.Faces,point,normal);
    private static Panel SectionFaces(string title,Vector3[][] oldShape,IReadOnlyList<LayerFaceInfo> faces,Vector3 point,Vector3 normal)
    {
        var (right,up)=Basis(normal);Vector2 Q(Vector3 p)=>new(Vector3.Dot(p,right),Vector3.Dot(p,up));
        var shapes=new List<Shape>();
        foreach(var f in faces)
        {
            if(f.Front.Length<3||f.Back.Length!=f.Front.Length)continue;var colour=f.Rank>=0?Rank(f.Rank):"#9aa6b0";
            var a=f.Front;var b=f.Back;
            for(int k=1;k+1<a.Length;k++)
                foreach(var tet in new[]{new[]{a[0],a[k],a[k+1],b[k+1]},new[]{a[0],a[k],b[k],b[k+1]},new[]{a[0],b[0],b[k],b[k+1]}})
                {
                    var pts=new List<Vector3>();
                    for(int i=0;i<4;i++)for(int j=i+1;j<4;j++){double di=Vector3.Dot(tet[i]-point,normal),dj=Vector3.Dot(tet[j]-point,normal);if(di*dj<0){var t=(float)(di/(di-dj));pts.Add(tet[i]+(tet[j]-tet[i])*t);}}
                    if(pts.Count<3)continue;var flat=pts.Select(Q).ToArray();var c=flat.Aggregate(Vector2.Zero,(x,y)=>x+y)/flat.Length;
                    shapes.Add(new(flat.OrderBy(q=>Math.Atan2(q.Y-c.Y,q.X-c.X)).ToArray(),colour,null,0));
                }
        }
        foreach(var poly in oldShape)
        {
            var pts=new List<Vector3>();
            for(int i=0;i<poly.Length;i++){var p=poly[i];var q=poly[(i+1)%poly.Length];double dp=Vector3.Dot(p-point,normal),dq=Vector3.Dot(q-point,normal);if(dp*dq<0){var t=(float)(dp/(dp-dq));pts.Add(p+(q-p)*t);}}
            if(pts.Count>=2)shapes.Add(new(pts.Take(2).Select(Q).ToArray(),null,"#000000",1.6f));
        }
        return new(title,shapes);
    }
}
