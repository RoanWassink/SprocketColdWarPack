using SprocketUiKit;
using System.Globalization;
using System.Numerics;
using System.Text;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.64: the direction of the layers comes from the shape of the block (the two opposite sides with the highest score), and no face disappears without a message.
/// The block of the user (T-84 Oplot-M turret face, object 1116, mesh 51) is rebuilt in code from its saved mesh (the 12 original points, 10 faces, walls 5 mm on Auto);
/// the forward axis and the centre of the vehicle are those of object 1116 under its parents (computed from the saved transforms).
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    /// <summary>The 12 points of the block before it was converted, in millimetres (mesh 51 of the saved blueprint of 11-11 16:36).</summary>
    private static readonly float[] UserBlockPoints={-345,-285,-412, -345,-285,54, 613,-240,-1020, 532,-240,-1447, -345,315,-502, -345,330,-21, 583,285,-1140, 517,300,-1582, 58,270,-21, 103,-255,39, 37,-285,-427, -8,270,-517};
    /// <summary>The forward axis of the designer (0, 0.0133, 0.9999) and the centre of the vehicle in the frame of that plate mesh, from the transforms of object 1116 and its parents.</summary>
    private static readonly Vector3 UserBlockForward=Vector3.Normalize(new Vector3(0,.013273f,.999912f));
    private static readonly Vector3 UserBlockCentre=new(-.887269f,-2.140770f,-1.114863f);

    private static string UserBlock()
    {
        var v=UserBlockPoints.Select(x=>x*.001f).ToArray();Vector3 P(int i)=>new(v[3*i],v[3*i+1],v[3*i+2]);
        // Two pieces: A (points 0,1,4,5,8,9,10,11) and B (9,10,8,11,2,3,6,7).
        var faces=new List<(int[] V,int[] Piece)>
        {
            (new[]{1,9,8,5},new[]{0,1,4,5,8,9,10,11}),(new[]{0,10,11,4},new[]{0,1,4,5,8,9,10,11}),(new[]{0,1,9,10},new[]{0,1,4,5,8,9,10,11}),(new[]{4,5,8,11},new[]{0,1,4,5,8,9,10,11}),(new[]{0,1,5,4},new[]{0,1,4,5,8,9,10,11}),
            (new[]{9,2,6,8},new[]{9,10,8,11,2,3,6,7}),(new[]{10,3,7,11},new[]{9,10,8,11,2,3,6,7}),(new[]{9,10,3,2},new[]{9,10,8,11,2,3,6,7}),(new[]{8,11,7,6},new[]{9,10,8,11,2,3,6,7}),(new[]{2,3,7,6},new[]{9,10,8,11,2,3,6,7})
        };
        var list=new List<int[]>();
        foreach(var (idx,piece) in faces)
        {
            var centre=piece.Select(P).Aggregate(Vector3.Zero,(a,b)=>a+b)/piece.Length;var poly=idx.Select(P).ToArray();var n=CheekShellFill.Newell(poly);
            var mid=poly.Aggregate(Vector3.Zero,(a,b)=>a+b)/poly.Length;list.Add(Vector3.Dot(n,mid-centre)<0?idx.Reverse().ToArray():idx);
        }
        var edges=new List<(int,int)>();foreach(var fc in list)for(int j=0;j<fc.Length;j++){var k=(Math.Min(fc[j],fc[(j+1)%fc.Length]),Math.Max(fc[j],fc[(j+1)%fc.Length]));if(!edges.Contains(k))edges.Add(k);}
        return Mesh(Enumerable.Range(0,12).Select(P),edges,list.Select(fc=>Face(fc,Enumerable.Repeat(5,fc.Length),0x01010101UL)));
    }

    internal static void RunLayerDirection(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Layer direction: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),preview=Path.Combine(output,"preview"),models=Path.Combine(output,"models");
        Directory.CreateDirectory(preview);Directory.CreateDirectory(models);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        double Density(string id)=>materials[id].Density;
        var leopard=new CassetteModel(presets.First(p=>p.Id=="leopard2a4"),materials,reactions);var t90=new CassetteModel(presets.First(p=>p.Id=="t90"),materials,reactions);
        var rows=new StringBuilder("model\trecipe\tstatus\tfront faces\tback faces\tlayer faces\tlayers\twalls kept\twalls whole\twalls left out\tfaces in\tworst layer point mm\tworst wall point mm\n");
        double worstLayer=0,worstWall=0;
        var cases=new List<(string Tag,string Json,Vector3 Forward,Vector3? Centre,CassetteModel Recipe)>
        {
            ("user block (mesh 51)",UserBlock(),UserBlockForward,UserBlockCentre,leopard),
            ("user block (mesh 51), T-90",UserBlock(),UserBlockForward,UserBlockCentre,t90),
            ("user block, forward without centre",UserBlock(),UserBlockForward,null,leopard),
            ("user box of 15:57 (object 279)",NewUserBox,Vector3.UnitZ,null,leopard),
            ("user box of 0.22.61 (object 280)",UserBoxBefore(),Vector3.UnitZ,null,t90),
            ("three pieces",ThreePieces(0,5),Vector3.UnitZ,null,t90),
            ("side armour (big faces across the forward axis)",BoxJson(Corners(.06f,.8f,2.0f,0)),Vector3.UnitZ,new Vector3(-3,0,0),t90),
            ("roof plate (big faces up and down)",BoxJson(Corners(2.0f,.06f,1.5f,0)),Vector3.UnitZ,null,t90),
            ("almost a cube",BoxJson(Corners(.5f,.5f,.52f,0)),Vector3.UnitZ,null,t90),
            ("almost a cube, tilted forward axis",BoxJson(Corners(.5f,.5f,.5f,0)),Vector3.Normalize(new Vector3(.2f,.1f,1)),null,t90),
            ("kinked block, pieces of different depth",ChainJson(new[]{new Vector2(-1.0f,-.2f),new Vector2(-.1f,0f),new Vector2(.8f,-.3f)},new[]{new Vector2(-1.0f,-.45f),new Vector2(-.1f,-.55f),new Vector2(.8f,-.85f)},.3f,0,5),Vector3.UnitZ,null,t90),
            ("kinked block, pieces of different height (box)",BoxJson(Corners(1,.8f,.803f,0)),Vector3.UnitZ,null,t90),
        };
        foreach(var (tag,json,fwd,centre,recipe) in cases)
        {
            File.WriteAllText(Path.Combine(models,Regex(tag)+".json"),json);
            LayerConversion? r=null;string refused="";
            try{r=LayerFaces.Convert(json,recipe,null,Density,fwd,5,centre);}catch(InvalidOperationException ex){refused=ex.Message;}
            if(r==null){Check(false,$"{tag}: must be converted, but was refused: {refused}");rows.AppendLine($"{tag}\t{recipe.Preset.Id}\trefused\t\t\t\t\t\t\t\t\t\t\t{refused}");continue;}
            var box=r.Box;Check(box!=null,$"{tag}: converted as a block");if(box==null)continue;
            var (mmL,whereL)=ShapeDeviation(json,r.Plan.Json,i=>r.Faces[i].Rank>=0);var (mmS,whereS)=ShapeDeviation(json,r.Plan.Json,i=>r.Faces[i].Rank<0);
            worstLayer=Math.Max(worstLayer,mmL);worstWall=Math.Max(worstWall,mmS);
            Check(mmL<=1,$"{tag}: layer points within 1 mm of the old shape (worst {mmL:0.###} mm, {whereL})");
            Check(mmS<=5.5,$"{tag}: wall points within their thickness of the old shape (worst {mmS:0.###} mm, {whereS})");
            Check(box.SideWallsDropped==0,$"{tag}: every side wall stays ({box.SideWallsKept} as strips, {box.SideWallsWhole} whole, {box.SideWallsDropped} left out)");
            // Every face of the old box is replaced by layers (front and back side), present as a wall, or counted as left out.
            int oldFaces=ComposerFill.Read(json).FaceCount;int accounted=box.FrontFaces+box.BackFaces+box.SideWallsKept+box.SideWallsWhole+box.SideWallsDropped+r.SkippedFaces;
            Check(accounted==oldFaces,$"{tag}: every old face is accounted for ({box.FrontFaces} front + {box.BackFaces} back + {box.SideWallsKept} strips + {box.SideWallsWhole} whole + {box.SideWallsDropped} left out + {r.SkippedFaces} other = {accounted} of {oldFaces})");
            Check(r.Inspection.Contains(LayerFaces.DirectionLine),$"{tag}: the window names the direction");
            rows.AppendLine(string.Join("\t",tag,recipe.Preset.Id,"converted",box.FrontFaces,box.BackFaces,r.Faces.Count(f=>f.Rank>=0),r.Layout.Groups.Length,box.SideWallsKept,box.SideWallsWhole,box.SideWallsDropped,oldFaces,mmL.ToString("0.###",CultureInfo.InvariantCulture),mmS.ToString("0.###",CultureInfo.InvariantCulture)));
        }
        // The block of the user: both pieces get layers (2 front faces and 2 back faces; one layer face per piece and layer), and the layers run along the big faces.
        {
            var r=LayerFaces.Convert(UserBlock(),leopard,null,Density,UserBlockForward,5,UserBlockCentre);var box=r.Box!;
            Check(box.FrontFaces==2&&box.BackFaces==2&&r.Faces.Count(f=>f.Rank>=0)==2*r.Layout.Groups.Length,$"user block: both pieces get layers ({box.FrontFaces} front faces, {r.Faces.Count(f=>f.Rank>=0)} layer faces, {r.Layout.Groups.Length} layers per piece), 0.22.63 made one front face and lost 2");
            var ranks=LayerRanks.FromJson(r.Plan.Json);
            Check(ranks.Count(x=>x>=0)==r.Faces.Count(f=>f.Rank>=0)&&ranks.Count(x=>x==-2)==r.Faces.Count(f=>f.Rank==LayerHit.SideWallRank),$"user block: after saving and loading every layer face has its layer and every wall is a wall (layers {ranks.Count(x=>x>=0)} of {r.Faces.Count(f=>f.Rank>=0)}, walls {ranks.Count(x=>x==-2)} of {r.Faces.Count(f=>f.Rank==LayerHit.SideWallRank)}, unassigned {ranks.Count(x=>x==-1)})");
        }
        // The pictures: the user's block before, as 0.22.63 made it (read from the saved mesh 51 of the blueprint, written by the harness of the report), and now.
        {
            var before=UserBlock();var r=LayerFaces.Convert(before,leopard,null,Density,UserBlockForward,5,UserBlockCentre);
            var after63=Path.Combine(output,"MESH51-AFTER.json");
            var shots=new List<(string Stem,string Json)>{("before",before),("0.22.64",r.Plan.Json)};if(File.Exists(after63))shots.Insert(1,("0.22.63",File.ReadAllText(after63)));
            foreach(var (stem,js) in shots)
            {
                var tris=NativeTriangles(js);var pts=tris.SelectMany(q=>new[]{q.A,q.B,q.C}).ToArray();var pad=new Vector3(.05f);var bounds=(pts.Aggregate(Vector3.Min)-pad,pts.Aggregate(Vector3.Max)+pad);
                foreach(var (view,dir) in new[]{("oblique",new Vector3(-.5f,.5f,1)),("top",new Vector3(0,1,.001f))})File.WriteAllBytes(Path.Combine(preview,$"direction-{stem}-{view}.png"),Render(tris,new Camera(bounds,dir)));
            }
            foreach(var (stem,js,fwd) in new[]{("side-armour",cases[6].Json,cases[6].Forward),("roof",cases[7].Json,cases[7].Forward)})
            {
                var rr=LayerFaces.Convert(js,t90,null,Density,fwd,5,null);var tris=NativeTriangles(rr.Plan.Json);var pts=tris.SelectMany(q=>new[]{q.A,q.B,q.C}).ToArray();var pad=new Vector3(.05f);var bounds=(pts.Aggregate(Vector3.Min)-pad,pts.Aggregate(Vector3.Max)+pad);
                File.WriteAllBytes(Path.Combine(preview,$"direction-{stem}-oblique.png"),Render(tris,new Camera(bounds,new Vector3(-.5f,.5f,1))));
            }
        }
        File.WriteAllText(Path.Combine(output,"DIRECTION-MODELS.tsv"),rows.ToString());
        Console.WriteLine($"FEAT64 PASS: {checks} checks, {failures} failures; worst layer point {worstLayer:0.###} mm, worst wall point {worstWall:0.###} mm");
    }
    private static string Regex(string s)=>System.Text.RegularExpressions.Regex.Replace(s,"[^A-Za-z0-9]+","-").Trim('-');
}
