using SprocketUiKit;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>0.22.66: the T-90A shape of the user (mesh 20 of T-90A.blueprint, saved 21:02, object 365, plate #366), open at the back, and the same 12 points closed with the two missing faces.</summary>
internal static partial class ComposerFillBlueprintEvidence
{
    private const string T90AOpen="""{"majorVersion":0,"minorVersion":3,"vertices":[-0.067,-0.003,-0.253000021,-0.067,0.122000009,0.125,0.1,0.135,0.125,0.1,0.0100000007,-0.253000021,-0.450000018,0.537,-0.184000015,-0.428000033,0.537,0.125,0.1,0.559,0.125,0.1,0.559,-0.190000013,-0.840000033,-0.795,-0.253000021,-0.840000033,-0.795,0.125,-1.03700006,-0.191000015,-0.184000015,-1.03700006,-0.191000015,0.127],"edges":[0,3,3,2,2,1,1,0,4,5,5,6,6,7,7,4,1,5,4,0,2,6,3,7,9,8,11,10,8,0,1,9,10,8,9,11,10,4,5,11],"edgeFlags":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],"faces":[{"v":[0,3,2,1],"t":[5,5,5,5],"tm":16843009,"te":2251842764079113},{"v":[4,5,6,7],"t":[559,559,559,559],"tm":16843009,"te":3096267694014473},{"v":[0,1,5,4],"t":[5,5,5,5],"tm":16843009,"te":1970346311942144},{"v":[1,2,6,5],"t":[5,5,5,5],"tm":16843009,"te":1125925676711939},{"v":[2,3,7,6],"t":[5,5,5,5],"tm":16843009,"te":1407404948324354},{"v":[8,9,1,0],"t":[5,5,5,5],"tm":16843009,"te":8589934592},{"v":[10,8,9,11],"t":[5,5,5,5],"tm":16843009,"te":5348088982929426},{"v":[11,5,4,10],"t":[559,559,559,559],"tm":16843009,"te":4503638282600465},{"v":[9,11,5,1],"t":[5,5,5,5],"tm":16843009,"te":0}]}""";
    /// <summary>The same shape closed with the two missing faces (0,3,7,4) and (8,0,4,10).</summary>
    private static string T90AClosed()
    {
        var node=System.Text.Json.Nodes.JsonNode.Parse(T90AOpen)!;var faces=node["faces"]!.AsArray();
        foreach(var v in new[]{new[]{0,3,7,4},new[]{8,0,4,10}})faces.Add(System.Text.Json.Nodes.JsonNode.Parse("{\"v\":["+string.Join(",",v)+"],\"t\":[5,5,5,5],\"tm\":16843009,\"te\":0}"));
        return node.ToJsonString();
    }
    /// <summary>The forward axis of the designer and the centre of the vehicle in the frame of that mesh (object 365 is turned 90 degrees about x; parents 2 and 1 only move it).</summary>
    private static readonly Vector3 T90AForward=Vector3.UnitY,T90ACentre=new(5.484f,-.2369f,2.8004f);

    internal static void RunOpenBox(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Open box: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();Directory.CreateDirectory(output);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        double Density(string id)=>materials[id].Density;var t90a=new CassetteModel(presets.First(p=>p.Id=="t90a"),materials,reactions);
        var rows=new StringBuilder();
        foreach(var (tag,json) in new[]{("open",T90AOpen),("closed",T90AClosed())})
        {
            File.WriteAllText(Path.Combine(output,$"T90A-MESH20-{tag.ToUpperInvariant()}.json"),json);
            LayerConversion? r=null;string refused="";
            try{r=LayerFaces.Convert(json,t90a,null,Density,T90AForward,5,T90ACentre);}catch(InvalidOperationException ex){refused=ex.Message;}
            if(r==null){Check(false,$"{tag}: must be converted, but was refused: {refused}");rows.AppendLine($"{tag}	refused	{refused}");continue;}
            var box=r.Box!;Check(box!=null,$"{tag}: converted as a block");if(box==null)continue;
            var (mmL,whereL)=ShapeDeviation(json,r.Plan.Json,i=>r.Faces[i].Rank>=0);var (mmS,whereS)=ShapeDeviation(json,r.Plan.Json,i=>r.Faces[i].Rank<0);
            Check(mmL<=1&&mmS<=5.5,$"{tag}: layer points {mmL:0.###} mm and wall points {mmS:0.###} mm outside the old shape");
            int oldFaces=ComposerFill.Read(json,true).FaceCount;int accounted=box.FrontFaces+box.BackFaces+box.SideWallsKept+box.SideWallsWhole+box.SideWallsDropped+r.SkippedFaces;
            Check(accounted==oldFaces&&box.SideWallsDropped==0,$"{tag}: every old face is accounted for and every side wall stays ({box.FrontFaces} front, {box.BackFaces} back, {box.SideWallsKept} strips, {box.SideWallsWhole} whole, {box.SideWallsDropped} left out of {oldFaces})");
            // The layers run from the front (z = +125 mm) to the open rim.
            var layerFaces=r.Faces.Where(f=>f.Rank>=0).ToArray();
            float facing=layerFaces.Average(f=>Vector3.Dot(f.Normal,Vector3.UnitZ));
            if(tag=="open")
            {
                Check(facing>.9f&&layerFaces.All(f=>Vector3.Dot(f.Normal,Vector3.UnitZ)>.9f),$"{tag}: the layer faces face +Z, the side with the front faces (z = 125 mm)");
                float deepest=layerFaces.SelectMany(f=>f.Back).Min(p=>p.Z);Check(deepest<-.17f,$"{tag}: the last layer reaches the open rim (z {deepest*1000:0} mm)");
            }
            else
            {
                // Closed: the rule of 0.22.64 (the two largest opposite sides, the pair with the highest score) takes the long faces of the slab, not the small faces at z = 125 mm: the layers run through the slab, not along it. Reported, not changed.
                Check(Math.Abs(facing)<.5f,$"{tag}: the layer faces do not face the z axis (mean cosine {facing:0.##}): the largest opposite pair is the long sides of the slab");
            }
            var ranks=LayerRanks.FromJson(r.Plan.Json);
            Check(Enumerable.Range(0,r.Faces.Length).All(i=>r.Faces[i].Rank<0||ranks[i]>=0)&&ranks.Count(x=>x==-2)==r.Faces.Count(f=>f.Rank==LayerHit.SideWallRank),$"{tag}: after saving and loading every layer face has its layer and every wall is a wall");
            rows.AppendLine($"{tag}	converted	front {box.FrontFaces} back {box.BackFaces} layers {r.Layout.Groups.Length} layer faces {layerFaces.Length} walls kept {box.SideWallsKept} whole {box.SideWallsWhole} left out {box.SideWallsDropped}; layer {mmL:0.###} mm wall {mmS:0.###} mm");
        }
        // Convert to composite plate: the same "is a box" test, so an open block like this one keeps its shape (recipe only, no 559 mm face); a loose plate is not a box.
        Check(LayerFaces.IsBox(T90AOpen,T90AForward,T90ACentre)&&LayerFaces.IsBox(T90AClosed(),T90AForward,T90ACentre),"Convert to composite plate leaves the open and the closed block alone (IsBox is true)");
        Check(!LayerFaces.IsBox(Flat(803),Vector3.UnitZ,null),"a loose plate is not a box (Convert to composite plate stays as it is)");
        var srcText=File.ReadAllText(Path.Combine(root,"ComposerFill.cs"));
        Check(srcText.Contains("This plate has no back and no edges running back, so its own thickness (")&&srcText.Contains("That is too thin for layers. Give it more thickness or shape it into a box."),"the message for a plate without a back and without edges running back is in place; a box that is really too shallow keeps its old message");
        // The overlay: layers (as in 0.22.65) and painted (one closed block with a stripe per layer), on the Challenger box.
        {
            var burlington=new CassetteModel(presets.First(p=>p.Id=="burlington"),materials,reactions);
            var r=LayerFaces.Convert(ChallengerBox,burlington,null,Density,ChallengerForward,5,ChallengerCentre);
            var (copy,owners)=SyntheticOverlay(r,true);var mesh=LayerMesh.Read(r.Plan.Json);var ranks=LayerRanks.FromJson(r.Plan.Json);
            var layers=LayerOverlay.Remap(copy,mesh,ranks,r.Layout);var painted=LayerOverlay.Remap(copy,mesh,ranks,r.Layout,true);
            Check(layers.Triangles.Length==copy.Triangles.Length&&painted.Triangles.Length==copy.Triangles.Length,"both looks keep every triangle (zero area for what is not drawn), so the native coverage check passes");
            int first=r.Faces.Where(f=>f.Rank>=0).Min(f=>f.Rank),last=r.Faces.Where(f=>f.Rank>=0).Max(f=>f.Rank);
            int PartOf(int t){float a=copy.Uv[copy.Triangles[t*3]].Y,b=copy.Uv[copy.Triangles[t*3+1]].Y,c=copy.Uv[copy.Triangles[t*3+2]].Y;return a==0&&b==0&&c==0?0:a==1&&b==1&&c==1?1:2;}
            bool Collapsed(OverlayCopy o,int t){var m0=o.Vertices[o.Triangles[t*3]];return m0==o.Vertices[o.Triangles[t*3+1]]&&m0==o.Vertices[o.Triangles[t*3+2]];}
            int innerDrawn=0,frontDrawn=0,backDrawn=0,wallOther=0,wrongColour=0,loose=0;
            for(int t=0;t<copy.Triangles.Length/3;t++)
            {
                if(Collapsed(painted,t))continue;var face=r.Faces[owners[t]];int part=PartOf(t);
                var ds=new[]{painted.Uv[painted.Triangles[t*3]].Y,painted.Uv[painted.Triangles[t*3+1]].Y,painted.Uv[painted.Triangles[t*3+2]].Y};
                if(face.Rank>=0)
                {
                    if(part==0&&face.Rank==first)frontDrawn++;else if(part==1&&face.Rank==last)backDrawn++;else if(part!=2)innerDrawn++; // 0.22.68: a side of a layer under a wall may stay (only what is certainly inside is left out)
                    var g=r.Layout.Groups[face.Rank];if(ds.Any(d=>d<g.StartFraction-1e-5||d>g.EndFraction+1e-5))wrongColour++;
                }
                else if(face.Rank==LayerHit.SideWallRank){if(part!=0)wallOther++;}
                else loose++;
            }
            Check(innerDrawn==0&&loose==0,$"painted: no front or back of a loose layer inside is drawn ({innerDrawn} inner layer triangles, {loose} others); parts of walls and sides under a wall may stay ({wallOther} other wall triangles)");
            Check(frontDrawn>0&&backDrawn>0&&wrongColour==0,$"painted: the front of the first layer ({frontDrawn} triangles) and the back of the last layer ({backDrawn}) are drawn in the colour of their layer ({wrongColour} wrong)");
            var wallFaces=r.Faces.Select((f,i)=>(f,i)).Where(x=>x.f.Rank==LayerHit.SideWallRank).ToArray();var strips=r.Box!.Strips;
            int stripsDrawn=0,stripsWrong=0;
            foreach(var (f,i) in wallFaces)
            {
                var tri=Enumerable.Range(0,copy.Triangles.Length/3).Where(t=>owners[t]==i&&PartOf(t)==0&&!Collapsed(painted,t)).ToArray();if(tri.Length==0)continue;stripsDrawn++;
                var s=strips.First(q=>q.Face==i);
                foreach(var t in tri){var ds=Enumerable.Range(0,3).Select(k=>painted.Uv[painted.Triangles[t*3+k]].Y);if(ds.Any(d=>d<s.From-1e-4||d>s.To+1e-4))stripsWrong++;}
            }
            Check(stripsDrawn>=wallFaces.Length*9/10&&stripsWrong==0,$"painted: the side walls are drawn, each strip in the colour of the layer or the air it spans ({stripsDrawn} of {wallFaces.Length} strips, {stripsWrong} wrong)");
            int layersInner=0;for(int t=0;t<copy.Triangles.Length/3;t++){if(!Collapsed(layers,t)&&r.Faces[owners[t]].Rank>=0&&PartOf(t)==0&&r.Faces[owners[t]].Rank!=first)layersInner++;}
            Check(layersInner>0,$"layers: the look of 0.22.65 still draws the loose layers ({layersInner} inner front triangles)");
            // A block with a side that has no wall keeps the layers on that side: read the mesh without the walls of one old face and the layers of that side stay.
            int dropSource=wallFaces[0].f.Source;
            var noWall=ranks.Select((x,i)=>x==LayerHit.SideWallRank&&r.Faces[i].Source==dropSource?-1:x).ToArray();
            var openSide=LayerOverlay.Remap(copy,mesh,noWall,r.Layout,true);
            int sideKept=0;for(int t=0;t<copy.Triangles.Length/3;t++)if(!Collapsed(openSide,t)&&r.Faces[owners[t]].Rank>=0&&PartOf(t)==2)sideKept++;
            Check(sideKept>0,$"painted: a side without a wall keeps its layers ({sideKept} side triangles of layers stay)");
            var preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
            foreach(var (stem,data) in new[]{("layers",layers),("painted",painted)})
            {
                var strip=CassetteDepthStrip.Build(burlington.Preset);var b2=(data.Vertices.Aggregate(Vector3.Min)-new Vector3(.05f),data.Vertices.Aggregate(Vector3.Max)+new Vector3(.05f));
                foreach(var (view,dir) in new[]{("top",new Vector3(0,1,.001f)),("oblique",new Vector3(-1,.45f,.55f)),("side",new Vector3(-1,.001f,0))})
                {
                    var tris=new List<SolidTriangle>();
                    for(int t=0;t+2<data.Triangles.Length;t+=3)
                    {
                        var p0=data.Vertices[data.Triangles[t]];var p1=data.Vertices[data.Triangles[t+1]];var p2=data.Vertices[data.Triangles[t+2]];
                        var dmean=(data.Uv[data.Triangles[t]].Y+data.Uv[data.Triangles[t+1]].Y+data.Uv[data.Triangles[t+2]].Y)/3;var px=strip[Math.Clamp((int)(dmean*CassetteDepthStrip.Height),0,CassetteDepthStrip.Height-1)];
                        var n=Vector3.Cross(p1-p0,p2-p0);if(n.LengthSquared()<1e-16f)continue;n=Vector3.Normalize(n);float l=.6f+.4f*MathF.Abs(Vector3.Dot(n,Vector3.Normalize(new Vector3(.4f,.7f,.5f))));
                        tris.Add(new(p0,p1,p2,n,Raster.Pack((byte)Math.Min(255,(px>>24)*l+20),(byte)Math.Min(255,((px>>16)&255)*l+20),(byte)Math.Min(255,((px>>8)&255)*l+20),255)));
                    }
                    File.WriteAllBytes(Path.Combine(preview,$"open-overlay-{stem}-{view}.png"),Render(tris,new Camera(b2,dir)));
                }
            }
            // The T-90A shape before and after (native back points).
            foreach(var (stem,js) in new[]{("before",T90AOpen),("after",LayerFaces.Convert(T90AOpen,t90a,null,Density,T90AForward,5,T90ACentre).Plan.Json)})
            {
                var tris=NativeTriangles(js);var pts=tris.SelectMany(q=>new[]{q.A,q.B,q.C}).ToArray();var pad=new Vector3(.05f);var bounds=(pts.Aggregate(Vector3.Min)-pad,pts.Aggregate(Vector3.Max)+pad);
                foreach(var (view,dir) in new[]{("oblique",new Vector3(-.5f,.5f,1)),("top",new Vector3(0,1,.001f))})File.WriteAllBytes(Path.Combine(preview,$"open-t90a-{stem}-{view}.png"),Render(tris,new Camera(bounds,dir)));
            }
        }
        File.WriteAllText(Path.Combine(output,"OPEN-BOX.tsv"),rows.ToString());
        Console.WriteLine($"FEAT66 PASS: {checks} checks, {failures} failures");
    }
}
