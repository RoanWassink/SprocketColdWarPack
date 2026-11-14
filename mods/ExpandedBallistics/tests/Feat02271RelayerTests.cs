using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.71. A: a plate in layers goes back to its box and is layered again (round trips on every earlier model, another recipe, hand edits, open boxes, save and load).
/// B: Apply carries out the chosen option. C: several layers chosen at once in the composer. D: Forensic draws a band only as far as it reaches another band. E: the log line per hit is behind a setting.
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    // ---- helpers on mesh JSON ----
    private static Vector3[] MeshPoints(string json)
    {
        using var doc=JsonDocument.Parse(json);var v=doc.RootElement.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        return Enumerable.Range(0,v.Length/3).Select(i=>new Vector3(v[3*i],v[3*i+1],v[3*i+2])).ToArray();
    }
    private static (int Faces,int[][] Loops) MeshFaces(string json)
    {
        using var doc=JsonDocument.Parse(json);var loops=doc.RootElement.GetProperty("faces").EnumerateArray().Select(f=>f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray()).ToArray();
        return (loops.Length,loops);
    }
    private static Vector3[] UsedPoints(string json){var p=MeshPoints(json);return MeshFaces(json).Loops.SelectMany(l=>l).Distinct().Select(i=>p[i]).ToArray();}
    /// <summary>Largest distance from a point of <paramref name="a"/> to the nearest point of <paramref name="b"/>.</summary>
    private static double Reach(IReadOnlyList<Vector3> a,IReadOnlyList<Vector3> b)=>a.Count==0?0:a.Max(p=>b.Count==0?double.PositiveInfinity:b.Min(q=>Vector3.Distance(p,q)))*1000;
    /// <summary>Points that no face uses, and edges that no face boundary contains (a thickness rib counts as used).</summary>
    private static (int Points,int Edges) Stray(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;var loops=MeshFaces(json).Loops;
        var usedPoints=loops.SelectMany(l=>l).ToHashSet();int points=MeshPoints(json).Length-usedPoints.Count;
        var used=new HashSet<(int,int)>();foreach(var l in loops)for(int j=0;j<l.Length;j++)used.Add((Math.Min(l[j],l[(j+1)%l.Length]),Math.Max(l[j],l[(j+1)%l.Length])));
        var e=root.GetProperty("edges").EnumerateArray().Select(x=>x.GetInt32()).ToArray();int stray=0;
        for(int i=0;i+1<e.Length;i+=2)if(!used.Contains((Math.Min(e[i],e[i+1]),Math.Max(e[i],e[i+1]))))stray++;
        return (points,stray);
    }
    private static string MovePoint(string json,int vertex,Vector3 delta)
    {
        var node=JsonNode.Parse(json)!;var v=node["vertices"]!.AsArray();
        v[3*vertex]=v[3*vertex]!.GetValue<float>()+delta.X;v[3*vertex+1]=v[3*vertex+1]!.GetValue<float>()+delta.Y;v[3*vertex+2]=v[3*vertex+2]!.GetValue<float>()+delta.Z;
        return node.ToJsonString();
    }
    private static string RemoveFace(string json,int face)
    {
        var node=JsonNode.Parse(json)!;node["faces"]!.AsArray().RemoveAt(face);return node.ToJsonString();
    }
    private static bool Convex(float[] a,float[] b,out float depth)
    {
        // separating axis test of two convex polygons; depth = smallest overlap along the tested axes (<= 0 when they only touch or are apart)
        depth=float.MaxValue;
        foreach(var poly in new[]{a,b})
            for(int i=0;i<poly.Length;i+=2)
            {
                int j=(i+2)%poly.Length;var ax=new Vector2(-(poly[j+1]-poly[i+1]),poly[j]-poly[i]);if(ax.LengthSquared()<1e-9f)continue;ax=Vector2.Normalize(ax);
                float Min(float[] p,bool max){float r=max?float.MinValue:float.MaxValue;for(int k=0;k<p.Length;k+=2){var d=Vector2.Dot(new(p[k],p[k+1]),ax);r=max?Math.Max(r,d):Math.Min(r,d);}return r;}
                float overlap=Math.Min(Min(a,true),Min(b,true))-Math.Max(Min(a,false),Min(b,false));
                depth=Math.Min(depth,overlap);if(overlap<=0)return false;
            }
        return true;
    }
    /// <summary>The x range of a convex polygon on the horizontal line y.</summary>
    private static (float Min,float Max)? Across(float[] poly,float y)
    {
        var xs=new List<float>();
        for(int i=0;i<poly.Length;i+=2)
        {
            int j=(i+2)%poly.Length;float y0=poly[i+1],y1=poly[j+1];if((y0-y)*(y1-y)>0||Math.Abs(y1-y0)<1e-6f&&Math.Abs(y0-y)>1e-4f)continue;
            if(Math.Abs(y1-y0)<1e-6f){xs.Add(poly[i]);xs.Add(poly[j]);continue;}
            xs.Add(poly[i]+(poly[j]-poly[i])*(y-y0)/(y1-y0));
        }
        return xs.Count==0?null:(xs.Min(),xs.Max());
    }
    private static bool Inside(float[] poly,float x,float y,float margin)
    {
        var r=Across(poly,y);return r is {} s&&x>=s.Min-margin&&x<=s.Max+margin&&y>=poly.Where((_,i)=>i%2==1).Min()-margin&&y<=poly.Where((_,i)=>i%2==1).Max()+margin;
    }

    internal static void RunRelayer(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Relayer: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        double Density(string id)=>materials[id].Density;CassetteModel Recipe(string id)=>new(presets.First(p=>p.Id==id),materials,reactions);
        LayerBoxBack ToBoxChecked(string layers,LayerLayout layout,CassetteModel recipe,Vector3 fwd,Vector3? centre)=>LayerFaces.ToBox(layers,layout,fwd,LayerFaces.BoxCheck(layers,recipe,Density,fwd,5,centre));
        var counts=new StringBuilder("model\trecipe\toriginal faces\toriginal points\tlayers: faces\tlayers: points\tbox: faces\tbox: points\tbox: edges\tfront/sides/back\topen back\tbox shape vs original (mm)\tstray points\tstray edges\tlayers again: faces\tlayers again: points\tlayers again vs first (mm)\tbox again vs box (mm)\n");
        var models=new List<(string Tag,string Json,Vector3 Forward,Vector3? Centre,string Recipe)>
        {
            ("user block (mesh 51)",UserBlock(),UserBlockForward,UserBlockCentre,"leopard2a4"),("user box 15:57",NewUserBox,Vector3.UnitZ,null,"leopard2a4"),("user box 0.22.61",UserBoxBefore(),Vector3.UnitZ,null,"t90"),
            ("three pieces",ThreePieces(0,5),Vector3.UnitZ,null,"t90"),("Challenger box (negative tm)",ChallengerBox,ChallengerForward,ChallengerCentre,"burlington"),("T-90A open (mesh 20)",T90AOpen,T90AForward,T90ACentre,"t90a"),
            ("T-90A closed (mesh 20)",T90AClosed(),T90AForward,T90ACentre,"t90a"),("box 1000 x 800 x 803",BoxJson(Corners(1,.8f,.803f,0)),Vector3.UnitZ,null,"t90"),("wedge",WedgeJson(),Vector3.UnitZ,null,"t90"),
            ("kinked beam",ChainJson(new[]{new Vector2(-1.0f,-.2f),new Vector2(-.1f,0f),new Vector2(.8f,-.3f)},new[]{new Vector2(-1.0f,-.45f),new Vector2(-.1f,-.55f),new Vector2(.8f,-.85f)},.3f,0,5),Vector3.UnitZ,null,"t90"),
            ("open box (front and four sides)",BoxJson(Corners(1,.8f,.803f,0),withBack:false),Vector3.UnitZ,null,"t90")
        };
        string t90aLayers="",t90aBox="",t90aAgain="";
        // ---------- A.1 box -> layers -> box -> layers ----------
        foreach(var (tag,json,fwd,centre,recipeId) in models)
        {
            var recipe=Recipe(recipeId);
            LayerConversion? first=null;try{first=LayerFaces.Convert(json,recipe,null,Density,fwd,5,centre);}catch(InvalidOperationException ex){Check(false,$"{tag}: must be converted first: {ex.Message}");continue;}
            LayerBoxBack box1;try{box1=ToBoxChecked(first.Plan.Json,first.Layout,recipe,fwd,centre);}catch(InvalidOperationException ex){Check(false,$"{tag}: back to the box: {ex.Message}");continue;}
            LayerConversion? second=null;string refused="";
            try{second=LayerFaces.Convert(box1.Json,recipe,null,Density,fwd,5,centre);}catch(InvalidOperationException ex){refused=ex.Message;}
            if(second==null){Check(false,$"{tag}: the box must be layered again: {refused}");continue;}
            LayerBoxBack box2;try{box2=ToBoxChecked(second.Plan.Json,second.Layout,recipe,fwd,centre);}catch(InvalidOperationException ex){Check(false,$"{tag}: the second plate in layers back to its box: {ex.Message}");continue;}
            var origPts=UsedPoints(json);var boxPts=UsedPoints(box1.Json);var box2Pts=UsedPoints(box2.Json);
            double shape=Math.Max(Reach(boxPts,origPts),Reach(origPts,boxPts)),boxAgain=Math.Max(Reach(box2Pts,boxPts),Reach(boxPts,box2Pts));
            var layers1=UsedPoints(first.Plan.Json);var layers2=UsedPoints(second.Plan.Json);double again=Math.Max(Reach(layers2,layers1),Reach(layers1,layers2));
            var stray=Stray(box1.Json);var origFaces=MeshFaces(json).Faces;var box=first.Box;
            counts.AppendLine(string.Join("\t",tag,recipeId,origFaces,MeshPoints(json).Length,MeshFaces(first.Plan.Json).Faces,MeshPoints(first.Plan.Json).Length,MeshFaces(box1.Json).Faces,MeshPoints(box1.Json).Length,box1.Edges,$"{box1.FrontFaces}/{box1.SideWalls}/{box1.BackFaces}",box1.OpenBack,shape.ToString("0.###",CultureInfo.InvariantCulture),stray.Points,stray.Edges,MeshFaces(second.Plan.Json).Faces,MeshPoints(second.Plan.Json).Length,again.ToString("0.###",CultureInfo.InvariantCulture),boxAgain.ToString("0.###",CultureInfo.InvariantCulture)));
            Check(shape<=Math.Max(1,(box?.OuterDeviationMm??0)+.1),$"{tag}: the box has the shape of the original box within 1 mm, or within the deviation the conversion itself reports ({box?.OuterDeviationMm:0.###} mm) ({shape:0.###} mm)");
            Check(box==null||box1.FrontFaces==box.FrontFaces&&box1.SideWalls==box.SideFaces-box.SideWallsDropped,$"{tag}: as many front faces and sides as the original ({box1.FrontFaces}/{box1.SideWalls} against {box?.FrontFaces}/{box?.SideFaces})");
            Check(MeshFaces(box1.Json).Faces==origFaces||box!=null&&box1.BackFaces==0&&MeshFaces(box1.Json).Faces==origFaces-box.BackFaces,$"{tag}: {MeshFaces(box1.Json).Faces} faces against {origFaces} in the original ({(box1.OpenBack?"a back is not needed to keep it a box":"back faces kept")})");
            Check(stray.Points==0&&stray.Edges==0,$"{tag}: no stray points ({stray.Points}) and no stray edges ({stray.Edges}) in the box");
            Check(MeshFaces(second.Plan.Json).Faces==MeshFaces(first.Plan.Json).Faces&&again<=.1,$"{tag}: the second plate in layers equals the first: {MeshFaces(second.Plan.Json).Faces} faces, points within {again:0.####} mm");
            Check(boxAgain<=.1&&MeshFaces(box2.Json).Faces==MeshFaces(box1.Json).Faces,$"{tag}: the second box equals the first ({MeshFaces(box2.Json).Faces} faces, {boxAgain:0.####} mm)");
            // A.5 save and load: the mesh JSON through a rewrite (the read and write route of the mesh) changes nothing
            var reloaded=JsonSerializer.Serialize(JsonDocument.Parse(first.Plan.Json).RootElement);
            var viaSave=ToBoxChecked(reloaded,first.Layout,recipe,fwd,centre);
            Check(MeshFaces(viaSave.Json).Faces==MeshFaces(box1.Json).Faces&&Math.Max(Reach(UsedPoints(viaSave.Json),boxPts),Reach(boxPts,UsedPoints(viaSave.Json)))<=.001,$"{tag}: saving and loading between the steps changes nothing");
            if(tag=="T-90A open (mesh 20)"){t90aLayers=first.Plan.Json;t90aBox=box1.Json;t90aAgain=second.Plan.Json;}
        }
        File.WriteAllText(Path.Combine(output,"RELAYER-COUNTS.tsv"),counts.ToString());
        // the plate in layers of the T-90A save (21:32) as the user saved it: layers -> box -> layers -> box -> layers
        {
            try
            {
            var t90a=Recipe("t90a");var layout=LayerLayout.Build(t90a.Preset,LayerLayoutKind.PerBlock,null);
            var box1=ToBoxChecked(T90AMesh2132,layout,t90a,T90AForward,T90ACentre);var l2=LayerFaces.Convert(box1.Json,t90a,null,Density,T90AForward,5,T90ACentre);
            var box2=ToBoxChecked(l2.Plan.Json,l2.Layout,t90a,T90AForward,T90ACentre);var l3=LayerFaces.Convert(box2.Json,t90a,null,Density,T90AForward,5,T90ACentre);
            double again=Math.Max(Reach(UsedPoints(l3.Plan.Json),UsedPoints(l2.Plan.Json)),Reach(UsedPoints(l2.Plan.Json),UsedPoints(l3.Plan.Json)));
            var stray=Stray(box1.Json);
            Check(MeshFaces(l3.Plan.Json).Faces==MeshFaces(l2.Plan.Json).Faces&&again<=.1&&stray.Points==0&&stray.Edges==0,$"T-90A plate of the user (21:32): layers {MeshFaces(T90AMesh2132).Faces} faces / {MeshPoints(T90AMesh2132).Length} points, box {MeshFaces(box1.Json).Faces} faces / {MeshPoints(box1.Json).Length} points ({box1.FrontFaces} front, {box1.SideWalls} sides, {box1.BackFaces} back), layers again {MeshFaces(l2.Plan.Json).Faces} faces / {MeshPoints(l2.Plan.Json).Length} points, and once more the same ({again:0.####} mm)");
            counts.AppendLine($"# T-90A plate of the user (21:32): layers {MeshFaces(T90AMesh2132).Faces} faces / {MeshPoints(T90AMesh2132).Length} points -> box {MeshFaces(box1.Json).Faces} faces / {MeshPoints(box1.Json).Length} points / {box1.Edges} edges -> layers {MeshFaces(l2.Plan.Json).Faces} faces / {MeshPoints(l2.Plan.Json).Length} points");
            File.WriteAllText(Path.Combine(output,"RELAYER-COUNTS.tsv"),counts.ToString());
            }
            catch(InvalidOperationException ex){Check(false,"T-90A plate of the user (21:32): "+ex.Message);}
        }
        // ---------- A.2 another recipe ----------
        {
            var rows=new StringBuilder("model\tfrom\tto\tdirect: faces\tvia the box: faces\tlargest distance (mm)\tlayout direct\tlayout via the box\n");
            foreach(var (tag,json,fwd,centre,from) in new[]{("box 1000 x 800 x 803",BoxJson(Corners(1,.8f,.803f,0)),Vector3.UnitZ,(Vector3?)null,"t90"),("T-90A closed (mesh 20)",T90AClosed(),T90AForward,(Vector3?)T90ACentre,"t90a"),("user block (mesh 51)",UserBlock(),UserBlockForward,(Vector3?)UserBlockCentre,"leopard2a4")})
            foreach(var to in new[]{"t90","t90a","leopard2a4","burlington"})
            {
                if(to==from)continue;
                LayerConversion? direct=null,start=null;string directError="",viaError="";
                try{direct=LayerFaces.Convert(json,Recipe(to),null,Density,fwd,5,centre);}catch(InvalidOperationException ex){directError=ex.Message;}
                try{start=LayerFaces.Convert(json,Recipe(from),null,Density,fwd,5,centre);}catch(InvalidOperationException ex){viaError=ex.Message;}
                if(start==null)continue;
                LayerConversion? via=null;
                try{var back=ToBoxChecked(start.Plan.Json,start.Layout,Recipe(from),fwd,centre);via=LayerFaces.Convert(back.Json,Recipe(to),null,Density,fwd,5,centre);}catch(InvalidOperationException ex){viaError=ex.Message;}
                if(direct==null){Check(via==null,$"{tag}: {from} -> {to}: refused directly ({directError.Split('.')[0]}), so it is refused after the way back too");rows.AppendLine($"{tag}\t{from}\t{to}\trefused\t{(via==null?"refused":"converted")}\t\t\t");continue;}
                if(via==null){Check(false,$"{tag}: {from} -> {to}: converted directly, but not after the way back: {viaError}");continue;}
                double far=Math.Max(Reach(UsedPoints(via.Plan.Json),UsedPoints(direct.Plan.Json)),Reach(UsedPoints(direct.Plan.Json),UsedPoints(via.Plan.Json)));
                Check(MeshFaces(via.Plan.Json).Faces==MeshFaces(direct.Plan.Json).Faces&&far<=.1&&via.Layout.Kind==direct.Layout.Kind,$"{tag}: {from} ({start.Layout.Kind}) -> {to} ({direct.Layout.Kind}) through the box equals converting the box directly: {MeshFaces(via.Plan.Json).Faces} faces, {far:0.####} mm");
                rows.AppendLine($"{tag}\t{from}\t{to}\t{MeshFaces(direct.Plan.Json).Faces}\t{MeshFaces(via.Plan.Json).Faces}\t{far.ToString("0.####",CultureInfo.InvariantCulture)}\t{direct.Layout.Kind}\t{via.Layout.Kind}");
            }
            File.WriteAllText(Path.Combine(output,"RELAYER-RECIPES.tsv"),rows.ToString());
        }
        // ---------- A.3 edited by hand ----------
        {
            try
            {
            var t90=Recipe("t90");var json=BoxJson(Corners(1,.8f,.803f,0));var r=LayerFaces.Convert(json,t90,null,Density,Vector3.UnitZ,5,null);var layers=r.Plan.Json;
            var baseBox=ToBoxChecked(layers,r.Layout,t90,Vector3.UnitZ,null);var mesh=LayerMesh.Read(layers);var ranks=LayerRanks.FromJson(layers);
            // one outer point 50 mm
            int front=Array.FindIndex(ranks,x=>x==0);int outer=mesh.Ids[front][0];var before=MeshPoints(layers)[outer];
            var moved=MovePoint(layers,outer,new Vector3(.05f,0,0));
            var boxMoved=ToBoxChecked(moved,r.Layout,t90,Vector3.UnitZ,null);var pts=UsedPoints(boxMoved.Json);
            Check(pts.Any(p=>Vector3.Distance(p,before+new Vector3(.05f,0,0))<.0005f)&&!pts.Any(p=>Vector3.Distance(p,before)<.0005f)&&MeshFaces(boxMoved.Json).Faces==MeshFaces(baseBox.Json).Faces,"one outer point moved 50 mm: the box follows it (the point is at its new place, nothing is left at the old one, the same faces)");
            // one point of an inner layer 20 mm
            var firstLast=mesh.Ids.Where((_,i)=>ranks[i]==0||ranks[i]==r.Layout.Groups.Length-1).SelectMany(x=>x).ToHashSet();
            int innerFace=Array.FindIndex(ranks,x=>x==5);int inner=mesh.Ids[innerFace].First(v=>!firstLast.Contains(v));
            var innerMoved=MovePoint(layers,inner,new Vector3(0,.02f,0));
            var boxInner=ToBoxChecked(innerMoved,r.Layout,t90,Vector3.UnitZ,null);
            double same=Math.Max(Reach(UsedPoints(boxInner.Json),UsedPoints(baseBox.Json)),Reach(UsedPoints(baseBox.Json),UsedPoints(boxInner.Json)));
            Check(same<=.001&&MeshFaces(boxInner.Json).Faces==MeshFaces(baseBox.Json).Faces,$"one point of an inner layer moved 20 mm: the box does not change ({same:0.####} mm)");
            // a layer face removed
            int victim=Array.FindIndex(ranks,x=>x==3);string message="";
            try{ToBoxChecked(RemoveFace(layers,victim),r.Layout,t90,Vector3.UnitZ,null);}catch(InvalidOperationException ex){message=ex.Message;}
            Check(message.Contains("Back to box:",StringComparison.Ordinal)&&message.Contains("Nothing changed.",StringComparison.Ordinal)&&message.EndsWith(LayerFaces.BackToBoxAdvice,StringComparison.Ordinal),"a layer face removed: refused in plain words, nothing changed, with the advice: "+message);
            string notLayers="";try{ToBoxChecked(json,r.Layout,t90,Vector3.UnitZ,null);}catch(InvalidOperationException ex){notLayers=ex.Message;}
            Check(notLayers.Contains("Nothing changed.",StringComparison.Ordinal),"a plate that is not in layers is refused too: "+notLayers);
            }
            catch(InvalidOperationException ex){Check(false,"hand edits: "+ex.Message);}
        }
        // ---------- pictures: the T-90A block of the user as layers, brought back to its box, layered again with another recipe ----------
        {
            var other=Recipe("leopard2a4");
            string again="";try{again=LayerFaces.Convert(t90aBox,other,null,Density,T90AForward,5,T90ACentre).Plan.Json;}catch(InvalidOperationException ex){again=t90aAgain;Check(false,"picture: layered again with leopard2a4: "+ex.Message);}
            foreach(var (stem,js) in new[]{("layers",t90aLayers),("box",t90aBox),("other",again)})
            {
                var tris=NativeTriangles(js);var pts=tris.SelectMany(q=>new[]{q.A,q.B,q.C}).ToArray();var pad=new Vector3(.05f);var bounds=(pts.Aggregate(Vector3.Min)-pad,pts.Aggregate(Vector3.Max)+pad);
                foreach(var (view,dir) in new[]{("oblique",new Vector3(-.5f,.5f,1)),("top",new Vector3(0,1,.001f))})File.WriteAllBytes(Path.Combine(preview,$"relayer-{stem}-{view}.png"),Render(tris,new Camera(bounds,dir)));
            }
        }

        // ---------- B. Apply carries out the chosen option ----------
        {
            var preset=new CassettePreset("user_choice_v1","Choice test","1975.01.01",150,250,new[]{new CassetteLayer("rha",100,0,"up"),new("air",20,0,"up"),new("rha",80,0,"up")});
            var backend=new CassetteComposer(materials,reactions,"","",_=>false);
            var model=new CassetteComposerModel(backend,materials.Keys.Select(id=>new ComposerMaterial(id,id,CassettePalette.Group(id))).ToArray(),Array.Empty<ComposerMaterial>(),()=>new[]{preset});
            model.LoadPreset(preset.Id);
            Check(CassetteComposerModel.ApplyChoiceLabels.SequenceEqual(new[]{"Thickness only","Flip selected faces","Thicken inward","Fill to wall","T-80U preset","Conform cheek fill","Cheek fill (shell)","Convert to layers","Back to box"})&&Enum.GetValues<ComposerApplyChoice>().Length==9,"the nine options of Apply, in the order of the brief");
            var plain=new ComposerTarget("Plate #7",4,"rha","Steel",true,20,20,ConvertBlocker:null);
            model.SetTarget(plain);
            Check(model.ApplyChoice==ComposerApplyChoice.ThicknessOnly&&!model.ChoiceAvailable(ComposerApplyChoice.BackToBox)&&model.ApplyButton=="Apply to Plate #7","a plate that is not in layers starts on Thickness only; Back to box cannot be chosen; the button reads as before");
            int version=model.Version;var layersBefore=model.Layers.ToArray();var thicknessBefore=model.ApplyDepthMm;
            foreach(var choice in Enum.GetValues<ComposerApplyChoice>())
            {
                model.SetApplyChoice(choice);
                if(choice==ComposerApplyChoice.BackToBox){Check(model.ApplyChoice!=ComposerApplyChoice.BackToBox,"Back to box is refused for a plate that is not in layers");continue;}
                Check(model.ApplyChoice==choice&&model.ApplyButton==(choice==ComposerApplyChoice.ThicknessOnly?"Apply to Plate #7":"Apply: "+CassetteComposerModel.ApplyChoiceLabels[(int)choice]),$"choosing {choice} marks it and the button says {model.ApplyButton}");
                Check(model.Layers.SequenceEqual(layersBefore)&&model.ApplyDepthMm==thicknessBefore&&!model.Dirty,$"choosing {choice} changes nothing in the draft or the thickness");
                var expected=choice switch{ComposerApplyChoice.Flip=>ComposerMeshAction.Flip,ComposerApplyChoice.Inward=>ComposerMeshAction.Inward,ComposerApplyChoice.FillToWall=>ComposerMeshAction.Fill,ComposerApplyChoice.CheekPreset=>ComposerMeshAction.CheekPreset,ComposerApplyChoice.ConformCheek=>ComposerMeshAction.ConformCheek,ComposerApplyChoice.CheekShell=>ComposerMeshAction.CheekShell,ComposerApplyChoice.ConvertToLayers=>ComposerMeshAction.LayerConvert,_=>(ComposerMeshAction?)null};
                Check(CassetteComposerModel.MeshActionOf(choice)==expected,$"{choice} runs the action of the former button ({expected?.ToString()??"the recipe and the thickness, as before"})");
            }
            model.SetApplyChoice(ComposerApplyChoice.FillToWall);Check(model.ThicknessModeIndex==2&&model.FillToWall,"choosing Fill to wall sets the thickness mode, as its button did");
            Check(CassetteComposerModel.ThicknessModeLabels.SequenceEqual(new[]{"Uniform","To back plane"}),"the row Thickness mode keeps Uniform and To back plane");
            model.SetApplyChoice(ComposerApplyChoice.CheekShell);Check(model.ApplyBlocker==null||model.ApplyBlocker.Length>0,"an option on a composite plate asks for the same conditions as Apply (a saved recipe, a loaded cassette)");
            model.SetTarget(new ComposerTarget("Plate #8",4,"rha","Steel",false,20,20,ConvertBlocker:null));
            Check(model.ApplyChoice==ComposerApplyChoice.CheekShell,"0.22.74: another plate is selected and the choice stays (it changed in 0.22.71: the plate decided)");
            model.SetApplyChoice(ComposerApplyChoice.Inward);Check(model.ApplyBlocker==null&&model.ApplyButton=="Apply: Thicken inward (converts the plate first)","0.22.74: an option other than Thickness only on a plate that is not composite is possible: Apply converts the plate first, in the same step (it said to convert it first in 0.22.71)");
            var inLayers=new ComposerTarget("Plate #9",900,"ebCassette_user_choice_v1","Choice test",true,20,20,ConvertBlocker:null,LayerLine:"Layers: 900 faces assigned, 0 not assigned.");
            model.ResetApplyChoice();model.SetTarget(inLayers); // 0.22.74: the starting choice is only set by the first plate of a window
            Check(model.ApplyChoice==ComposerApplyChoice.ConvertToLayers&&model.ChoiceAvailable(ComposerApplyChoice.BackToBox)&&model.ApplyButton=="Apply: Convert to layers","a plate in layers starts on Convert to layers, so Apply with a changed recipe rebuilds it; Back to box can be chosen");
            model.SetApplyChoice(ComposerApplyChoice.ThicknessOnly);Check(model.ApplyBlocker is {} blocked&&blocked.Contains("in layers",StringComparison.Ordinal),"Thickness only on a plate in layers says to rebuild it or to go back to its box: "+model.ApplyBlocker);
            model.SetApplyChoice(ComposerApplyChoice.BackToBox);Check(model.ApplyChoice==ComposerApplyChoice.BackToBox&&model.ApplyBlocker==null&&model.ApplyButton=="Apply: Back to box","Back to box needs only the plate in layers");
            model.SetTarget(new ComposerTarget("Plate #9",900,"ebCassette_user_choice_v1","Choice test",true,20,20,ConvertBlocker:null));
            Check(model.ApplyChoice==ComposerApplyChoice.ThicknessOnly,"after Back to box the plate is no longer in layers: the choice falls back to Thickness only");
            model.SetApplyChoice(ComposerApplyChoice.Flip);model.SetTarget(new ComposerTarget("Plate #9",900,"ebCassette_user_choice_v1","Choice test",true,20,20,ConvertBlocker:null,SelectedFaces:2));
            Check(model.ApplyChoice==ComposerApplyChoice.Flip,"the choice stays after Apply (the same plate is read again)");
            model.ResetApplyChoice();model.SetTarget(new ComposerTarget("Plate #9",900,"ebCassette_user_choice_v1","Choice test",true,20,20,ConvertBlocker:null,SelectedFaces:3));
            Check(model.ApplyChoice==ComposerApplyChoice.ThicknessOnly,"a new window starts on Thickness only");
            var panel=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));
            Check(!panel.Contains("meshActions.Button(",StringComparison.Ordinal)&&panel.Contains("MeshAction(action,recipe)",StringComparison.Ordinal)&&panel.Contains("ApplyChoiceLabels.Skip(from).Take(3)",StringComparison.Ordinal),"the seven buttons are gone: one choice (three rows of three) and the button Apply, which applies the recipe and the chosen option in one step");
            var plan=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));
            Check(plan.Contains("RuntimeCompositePlateEdit.TryPrepared",StringComparison.Ordinal)==false&&plan.Contains("LayerFaces.ToBox(",StringComparison.Ordinal)&&plan.Contains("newMaterial",StringComparison.Ordinal),"Back to box and the rebuild are prepared in RuntimeComposerFill.Prepare; the existing step (TryPrepared) commits them as one step for Undo");
            // what the window says
            File.WriteAllText(Path.Combine(output,"APPLY-CHOICE-MODEL.json"),JsonSerializer.Serialize(new{labels=CassetteComposerModel.ApplyChoiceLabels,defaultPlain="Thickness only",defaultInLayers="Convert to layers",buttonTexts=Enum.GetValues<ComposerApplyChoice>().Select(c=>c==ComposerApplyChoice.ThicknessOnly?"Apply to <plate>":"Apply: "+CassetteComposerModel.ApplyChoiceLabels[(int)c])},new JsonSerializerOptions{WriteIndented=true}));
        }

        // ---------- C. several layers at once ----------
        {
            var t90a=presets.First(p=>p.Id=="t90a");var backend=new CassetteComposer(materials,reactions,"","",_=>false);
            CassetteComposerModel Make(CassettePreset p){var m=new CassetteComposerModel(backend,materials.Keys.Select(id=>new ComposerMaterial(id,id,CassettePalette.Group(id))).ToArray(),Array.Empty<ComposerMaterial>(),()=>new[]{p});m.LoadPreset(p.Id);return m;}
            var model=Make(t90a);
            Check(model.SelectionCount==1&&model.SelectedLayer==0,"a preset loads with its first layer chosen");
            model.SelectLayer(3);Check(model.SelectedLayers.SequenceEqual(new[]{3}),"a click chooses one layer");
            model.ToggleLayer(5);model.ToggleLayer(9);Check(model.SelectedLayers.SequenceEqual(new[]{3,5,9})&&model.SelectedLayer==9,"Ctrl-click adds layers");
            model.ToggleLayer(5);Check(model.SelectedLayers.SequenceEqual(new[]{3,9}),"Ctrl-click on a chosen layer takes it out");
            model.SelectLayer(2);model.SelectRange(6);Check(model.SelectedLayers.SequenceEqual(new[]{2,3,4,5,6}),"Shift-click chooses everything between the layer chosen last and this one");
            model.SelectRange(0);Check(model.SelectedLayers.SequenceEqual(new[]{0,1,2}),"Shift-click upward chooses the range up to this layer from the same anchor");
            Check(model.LayerTitle.StartsWith("3 layers selected",StringComparison.Ordinal),"the title says how many layers are chosen: "+model.LayerTitle);
            // fields on three chosen layers, one of them air: layers 2 (rha tilt 45), 3 (nera tilt 45), 5 (air)
            model.SelectLayer(1);model.ToggleLayer(2);model.ToggleLayer(4);
            Check(model.SelectedLayers.SequenceEqual(new[]{1,2,4})&&model.LayerIsAir==false&&double.IsNaN(model.LayerThickness)&&model.MaterialChoice==-1&&model.LayerTilt==45&&model.LayerDirection>=0,$"mixed thickness and material show nothing, the tilt (the same on the solid layers) shows {model.LayerTilt}");
            model.SetThickness(7);Check(model.Layers[1].ThicknessMm==7&&model.Layers[2].ThicknessMm==7&&model.Layers[4].ThicknessMm==7&&model.LayerThickness==7,"Thickness t applies to all three chosen layers, the air layer included");
            model.SetTilt(10);Check(model.Layers[1].TiltDegrees==10&&model.Layers[2].TiltDegrees==10&&model.Layers[4].TiltDegrees==0&&model.Layers[4].Material=="air","Tilt applies to the solid layers and skips the air layer");
            model.SetDirection(1);Check(model.Layers[1].TiltDirection==model.Layers[2].TiltDirection&&model.Layers[4].TiltDirection==t90a.Layers[4].TiltDirection,"Tilt toward applies to the solid layers and skips the air layer");
            var ids=model.MaterialChoiceIds;int steel=ids.ToList().IndexOf("rha");model.SetMaterialChoice(steel);
            Check(model.Layers[1].Material=="rha"&&model.Layers[2].Material=="rha"&&model.Layers[4].Material=="rha"&&model.Layers[4].TiltDegrees==0&&model.MaterialChoice==steel,"Material applies to all three chosen layers; the field shows it now that it is the same");
            Check(model.Dirty&&model.CanUndo,"the edits made a copy of the preset");
            for(int i=0;i<4;i++)model.Undo();Check(model.Layers.SequenceEqual(t90a.Layers)&&model.SelectedLayers.SequenceEqual(new[]{1,2,4}),"four undo steps (one per field) bring the preset back, with the choice of three layers");
            // Move on a choice that is not contiguous
            var plain=new CassettePreset("user_move_v1","Move","1975.01.01",1,4000,Enumerable.Range(0,8).Select(i=>new CassetteLayer("rha",10+i,0,"up")).ToArray());
            var move=Make(plain);move.SelectLayer(1);move.ToggleLayer(2);move.ToggleLayer(5);
            string Order(CassetteComposerModel m)=>string.Join(",",m.Layers.Select(l=>(l.ThicknessMm-10).ToString("0",CultureInfo.InvariantCulture)));
            move.MoveLayer(-1);Check(Order(move)=="1,2,0,3,5,4,6,7"&&move.SelectedLayers.SequenceEqual(new[]{0,1,4}),$"Move up shifts layers 2, 3 and 6 together one place and keeps their order: {Order(move)}");
            move.MoveLayer(-1);Check(Order(move)=="1,2,0,5,3,4,6,7"&&move.SelectedLayers.SequenceEqual(new[]{0,1,3}),$"a layer at the top stays and holds the one behind it; the other moves on: {Order(move)}");
            move.Undo();move.Undo();Check(Order(move)=="0,1,2,3,4,5,6,7","two undo steps bring the order back");
            move.SelectLayer(6);move.ToggleLayer(7);Check(!move.CanMoveLayer(1)&&move.SelectedLayers.SequenceEqual(new[]{6,7}),"layers at the bottom cannot move down");
            // Duplicate: four layers eleven times gives the T-90A package
            var seed=new CassettePreset("user_package_v1","Package","1975.01.01",1,4000,new[]{t90a.Layers[0]}.Concat(t90a.Layers.Skip(1).Take(4)).Append(t90a.Layers[^1]).ToArray());
            var dup=Make(seed);dup.SelectLayer(1);dup.SelectRange(4);
            Check(dup.SelectedLayers.SequenceEqual(new[]{1,2,3,4}),"the package of four layers is chosen");
            for(int i=0;i<11;i++)
            {
                int count=dup.Layers.Count;dup.DuplicateLayer();
                Check(dup.Layers.Count==count+4&&dup.SelectedLayers.SequenceEqual(Enumerable.Range(5+4*i,4))&&dup.Layers.Skip(1+4*(i+1)).Take(4).SequenceEqual(dup.Layers.Skip(1+4*i).Take(4)),$"Duplicate {i+1}: a copy of the four layers directly behind them, in the same order, and the copy is chosen");
            }
            Check(dup.Layers.Count==50&&dup.Layers.SequenceEqual(t90a.Layers),"Duplicate of four layers eleven times gives the T-90A package (50 layers, the same as the preset)");
            for(int i=0;i<11;i++)dup.Undo();Check(dup.Layers.Count==6,"eleven undo steps remove the copies again");
            // Delete everything but one
            var del=Make(t90a);del.SelectLayer(0);del.SelectRange(del.Layers.Count-1);
            Check(del.SelectionCount==50&&del.CanDeleteLayer,"all layers are chosen");
            del.DeleteLayer();Check(del.Layers.Count==1,"Delete of everything leaves one layer");
            Check(!del.CanDeleteLayer,"the last layer cannot be deleted");
            del.Undo();Check(del.Layers.SequenceEqual(t90a.Layers)&&del.SelectionCount==50,"Undo brings every layer and the choice back");
            del.SelectLayer(2);del.ToggleLayer(4);del.ToggleLayer(10);del.DeleteLayer();Check(del.Layers.Count==47,"Delete removes the chosen layers (47 left)");
            del.Undo();Check(del.Layers.Count==50,"Undo brings them back");
            var section=File.ReadAllText(Path.Combine(root,"CassetteSectionDrawing.cs"));var panel=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));
            // the window as the model shows it (for the picture): the layer list with three chosen layers, the choice and the button Apply on a plate in layers
            {
                var window=Make(t90a);window.SelectLayer(1);window.ToggleLayer(2);window.ToggleLayer(4);
                window.SetTarget(new ComposerTarget("Plate #366",201,"ebCassette_t90a","T-90A welded turret",true,20,20,ConvertBlocker:null,LayerLine:"Layers: 26 faces assigned, 0 not assigned."));
                var rows=window.LayerRows.Take(9).Select(r=>new{r.Title,r.Subtitle,r.Tag,r.Swatch,chosen=window.IsLayerSelected(r.Index)}).ToArray();
                File.WriteAllText(Path.Combine(preview,"composer-window.json"),JsonSerializer.Serialize(new{layerTitle=window.LayerTitle,layerDepth=window.LayerDepthText,layerNote=window.LayersNote,rows,choices=CassetteComposerModel.ApplyChoiceLabels,chosen=(int)window.ApplyChoice,choiceHelp=window.ApplyChoiceHelp,button=window.ApplyButton,blocker=window.ApplyBlocker,note=window.ApplyNote,target=window.TargetChip}));
                Check(window.SelectedLayers.SequenceEqual(new[]{1,2,4})&&window.ApplyButton=="Apply: Convert to layers"&&window.LayerTitle.StartsWith("3 layers selected",StringComparison.Ordinal),"the window picture is drawn from a model with three chosen layers and a plate in layers: "+window.ApplyButton);
            }
            Check(section.Contains("foreach(var chosen in m.SelectedLayers)",StringComparison.Ordinal),"the cross-section outlines every chosen layer");
            Check(panel.Contains("keyboard?.ctrlKey.isPressed==true",StringComparison.Ordinal)&&panel.Contains("keyboard?.shiftKey.isPressed==true",StringComparison.Ordinal)&&panel.Contains("model.IsLayerSelected(r.Index)",StringComparison.Ordinal),"Ctrl and Shift are read in the window of Expanded Ballistics at the moment of the click (Input System); the kit is not changed");
        }

        // ---------- D. Forensic bands stop where they reach another band ----------
        {
            var presetLookup=presets.ToDictionary(p=>p.MaterialId,p=>p);CombatCompositeLayers.Configure(id=>id!=null&&presetLookup.TryGetValue(id,out var found)?found:null);
            try
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests","fixtures","last-session-20261111-2334-0.22.69.json")));
                var shots=JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
                var rows=new StringBuilder("shot\tbands\tarray plates\toverlapping pairs before\toverlapping pairs now\tbands cut\tnumbers\tnumbers inside their band\n");
                int overlapBefore10=0;
                foreach(int number in new[]{10,3,12,1,7})
                {
                    var shot=shots.Single(s=>s.Number==number);var model=CombatForensics.Build(shot);
                    var plain=CombatForensicGeometry.Build(model,false);var clipped=CombatForensicGeometry.Build(model);
                    IEnumerable<(string Name,float[] Polygon,float Entry,float Exit)> Shapes(ForensicSection g)=>g.Bands.Where(b=>b.Polygon.Length>0&&!(b.Layer.Internal&&!b.Layer.Grouped)).Select(b=>("band "+b.Layer.Number,b.Polygon,b.EntryX,b.ExitX))
                        .Concat(g.Arrays.SelectMany(a=>a.Plates.Select(p=>($"array {a.Array.First} plate {p.Plate.Order}",p.Polygon,p.EntryX,p.ExitX))));
                    int Pairs(ForensicSection g,float tolerance){var s=Shapes(g).ToArray();int n=0;for(int i=0;i<s.Length;i++)for(int j=i+1;j<s.Length;j++)if(Convex(s[i].Polygon,s[j].Polygon,out var depth)&&depth>tolerance)n++;return n;}
                    int before=Pairs(plain,.5f),now=Pairs(clipped,.5f);if(number==10)overlapBefore10=before;
                    Check(now==0,$"shot {number}: no drawn band runs through the area of another band ({before} pairs did in 0.22.69, {now} now)");
                    Check(clipped.Bands.Count==plain.Bands.Count&&clipped.Arrays.Count==plain.Arrays.Count&&clipped.Arrays.Zip(plain.Arrays).All(p=>p.First.Plates.Count==p.Second.Plates.Count)&&clipped.Bands.Select(b=>b.Layer.Number).SequenceEqual(plain.Bands.Select(b=>b.Layer.Number)),$"shot {number}: the same bands and the same numbers ({clipped.Bands.Count} bands, {clipped.Arrays.Sum(a=>a.Plates.Count)} array plates)");
                    // on the shot line every band is where it was, with its own thickness and angle
                    bool online=true;float y=clipped.ShotY;
                    foreach(var (c,p) in Shapes(clipped).Zip(Shapes(plain)))
                    {
                        var a=Across(c.Polygon,y);var b=Across(p.Polygon,y);
                        online&=a is {} ra&&b is {} rb&&Math.Abs(ra.Min-rb.Min)<.01&&Math.Abs(ra.Max-rb.Max)<.01&&Math.Abs(c.Entry-p.Entry)<1e-4&&Math.Abs(c.Exit-p.Exit)<1e-4;
                    }
                    Check(online,$"shot {number}: every band crosses the shot line at the same place with the same thickness as in 0.22.69");
                    bool angles=clipped.Bands.Zip(plain.Bands).All(p=>p.First.Angle==p.Second.Angle&&p.First.NormalMm==p.Second.NormalMm);
                    int cut=clipped.Bands.Count(b=>b.Top!=null)+clipped.Arrays.SelectMany(a=>a.Plates).Count(p=>p.Top!=null);
                    bool untouched=clipped.Bands.Zip(plain.Bands).Where(p=>p.First.Top==null).All(p=>p.First.Polygon.SequenceEqual(p.Second.Polygon)&&p.First.Subs.Count==p.Second.Subs.Count);
                    Check(angles&&untouched,$"shot {number}: bands that touch no other band are drawn exactly as before; the angles and the normal thicknesses are unchanged ({cut} bands cut)");
                    // a cut band stays inside the full band (it only loses the part beyond the band it meets)
                    bool slope=true;
                    foreach(var (b,p) in clipped.Bands.Zip(plain.Bands).Where(q=>q.First.Top!=null))
                        for(int k=0;k+1<b.Polygon.Length;k+=2){var full=Across(p.Polygon,b.Polygon[k+1]);slope&=full is {} f&&b.Polygon[k]>=f.Min-.05&&b.Polygon[k]<=f.Max+.05;}
                    Check(slope,$"shot {number}: a band that is cut stays inside the full band (it only loses what lies beyond the band it meets)");
                    var badges=CombatForensicGeometry.Badges(clipped,model);int insideCount=0,needed=0;
                    foreach(var (layer,x,by) in badges)
                    {
                        var band=clipped.Bands.FirstOrDefault(q=>ReferenceEquals(q.Layer,layer));float[]? poly=band is {Polygon.Length:>0}?band.Polygon:clipped.Arrays.SelectMany(a=>a.Plates).FirstOrDefault(p=>clipped.Drawn(layer)&&p.EntryX<=clipped.X(layer.StartMm+(layer.PathMm??0)/2)&&clipped.X(layer.StartMm+(layer.PathMm??0)/2)<=p.ExitX)?.Polygon;
                        if(poly==null||x<=16.5f||x>=1060-16.5f)continue;needed++;var ys=Enumerable.Range(0,poly.Length/2).Select(q=>poly[2*q+1]).ToArray();if(Inside(poly,x,by,2)||ys.Max()-ys.Min()<24&&Math.Abs(by-(ys.Max()+ys.Min())/2)<=24)insideCount++;
                    }
                    Check(badges.Count==model.Layers.Count(l=>!(l.Internal&&!l.Grouped))&&insideCount==needed,$"shot {number}: {badges.Count} numbers, one per layer (0.22.72: an internal module on its own has its number in its label instead); {insideCount} of {needed} lie inside their band");
                    var sinkOps=new RecordSink();CombatForensicDrawing.Draw(sinkOps,model,false);
                    Check(JsonSerializer.Serialize(sinkOps.Ops).Contains("STRIKE FACE",StringComparison.Ordinal)&&(!model.Arrested||JsonSerializer.Serialize(sinkOps.Ops).Contains("Arrested in layer",StringComparison.Ordinal)),$"shot {number}: the marks (shot line, angle, arrest, residual) are still drawn");
                    rows.AppendLine($"{number}\t{plain.Bands.Count}\t{plain.Arrays.Sum(a=>a.Plates.Count)}\t{before}\t{now}\t{cut}\t{badges.Count}\t{insideCount}/{needed}");
                    if(number is 10 or 3)
                    {
                        foreach(var (stem,g,clip) in new[]{("0.22.69",plain,false),("0.22.71",clipped,true)})
                        {
                            var ops=new RecordSink();CombatForensicDrawing.Draw(ops,model,false,clip);
                            File.WriteAllText(Path.Combine(preview,$"forensic-bands-shot{number}-{stem}.ops.json"),JsonSerializer.Serialize(new{title=$"Forensic, shot {number} of the report of the user, drawn as in {stem}",ops=ops.Ops}));
                        }
                    }
                }
                Check(overlapBefore10>0,"the test shot (10) shows the fault in 0.22.69: at least one pair of bands ran through each other ("+overlapBefore10+")");
                File.WriteAllText(Path.Combine(output,"FORENSIC-BANDS.tsv"),rows.ToString());
                // an ordinary composite plate (one band, no other band touches it) is drawn as before
                var ordinary=new CombatReportShot(900,"Outgoing","APFSDS gen 3",1500,new[]
                {
                    new CombatReportEvent("layerStart","","Reached layer",500,null,"a",new CombatLayerInfo("a",new[]{new CombatLayerPart(1,"Composite armour plate","ebCassette_t90a","T-90A welded turret",200,30,true)},false)),
                    new CombatReportEvent("plateResidual","","Traversed plate",500,300,"a")
                },0,500,"T-90A",3,3,"T-90A");
                var om=CombatForensics.Build(ordinary);var og=CombatForensicGeometry.Build(om);var op=CombatForensicGeometry.Build(om,false);
                Check(og.Bands.Count==op.Bands.Count&&og.Bands.Zip(op.Bands).All(p=>p.First.Polygon.SequenceEqual(p.Second.Polygon)&&p.First.Top==null),"an ordinary composite plate is drawn exactly as in 0.22.69");
                // two thin slanted plates and a block, as in the report: the thin plates stop at the block
                var syn=new CombatReportShot(901,"Outgoing","APFSDS gen 3",1500,new[]
                {
                    new CombatReportEvent("layerStart","","Reached layer",500,null,"a",new CombatLayerInfo("a",new[]{new CombatLayerPart(1,"Plate","rha","RHA",8,60,true)},false)),
                    new CombatReportEvent("plateResidual","","Traversed",500,480,"a"),
                    new CombatReportEvent("layerStart","","Reached layer",480,null,"b",new CombatLayerInfo("b",new[]{new CombatLayerPart(2,"Plate","rha","RHA",8,60,true)},false)),
                    new CombatReportEvent("plateResidual","","Traversed",480,460,"b"),
                    new CombatReportEvent("layerStart","","Reached layer",460,null,"c",new CombatLayerInfo("c",new[]{new CombatLayerPart(3,"Block","rha","RHA",300,20,true)},false)),
                    new CombatReportEvent("plateResidual","","Traversed",460,300,"c")
                },0,500,"T-90A",3,3,"T-90A");
                var sm=CombatForensics.Build(syn);var sc=CombatForensicGeometry.Build(sm);var sp=CombatForensicGeometry.Build(sm,false);
                int PairsOf(ForensicSection g){var s=g.Bands.Where(b=>b.Polygon.Length>0).ToArray();int n=0;for(int i=0;i<s.Length;i++)for(int j=i+1;j<s.Length;j++)if(Convex(s[i].Polygon,s[j].Polygon,out var d)&&d>.5f)n++;return n;}
                Check(PairsOf(sp)>0&&PairsOf(sc)==0&&sc.Bands.Count(b=>b.Top!=null)>=1&&sc.Bands.OrderByDescending(b=>b.ExitX-b.EntryX).First().Top==null&&sc.Bands.All(b=>b.Polygon.Length>=6),$"two thin plates at 60 degrees in front of a thick block at 20 degrees: they ran through the block ({PairsOf(sp)} pairs) and now stop at it ({PairsOf(sc)} pairs); the block keeps its full height");
            }
            finally{CombatCompositeLayers.Configure(null,null);}
        }

        // ---------- E. the log line per hit ----------
        {
            var gp=File.ReadAllText(Path.Combine(root,"RuntimeGroupedProtection.cs"));
            Check(gp.Contains("config.Bind(\"Composite fill\",\"Layer hit log\",false,\"Writes one line per hit on a plate in layers. For troubleshooting.\")",StringComparison.Ordinal),"the setting Composite fill / Layer hit log exists, off by default, with the description of the brief");
            var calls=System.Text.RegularExpressions.Regex.Matches(gp,@"[^\n]*LayerHitLogText\.[^\n]*").Select(m=>m.Value).ToArray();
            Check(calls.Length==2&&calls.All(c=>c.TrimStart().StartsWith("if(RuntimeGroupedProtection.LayerHitLog)",StringComparison.Ordinal)),"both log lines (uniform and per layer) are built only inside the check of the setting: with it off a hit builds no text");
            Check(!gp.Contains("[Layer faces] plate #",StringComparison.Ordinal),"no text of the line is built outside LayerHitLogText");
            var chords=new[]{new LayerChord(1.0705,1.1532,14.2,-1,false),new LayerChord(1.1625,1.1815,17.1,-1,false)};var f=new[]{.8674,.8566};
            var text=LayerHitLogText.Uniform(366,chords,f);
            Check(text=="[Layer faces] plate #366 (uniform): [1.0705,1.1532] 14.2° x 0.8674; [1.1625,1.1815] 17.1° x 0.8566.","with the setting on the line is the same as in 0.22.70: "+text);
            Check(LayerHitLogText.PerLayer(366,chords,f,1).StartsWith("[Layer faces] plate #366: rank -1 [1.0705,1.1532] x 0.8674;",StringComparison.Ordinal),"the line of the per layer model is unchanged");
        }
        Console.WriteLine($"FEAT71 PASS: {checks} checks, {failures} failures");
    }
}
