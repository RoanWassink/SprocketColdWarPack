using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.61 "lagen als vlakken", part 2: Convert to layers on a kneaded box (front to back along the designer's forward axis), side walls as steel strips,
/// the overlay per layer and the weight. Managed evidence only. Models: (a) closed box 1000 x 800 x 803 mm, (b) closed wedge 400 -> 800 mm with a slanted
/// front, (c) box (a) turned 30 degrees about the vertical axis, (d) open box (front and four sides, no back), (e) shallow closed box 300 mm, (f) loose plate,
/// (g) the user's filling (object 280, read only).
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    private sealed record BoxCase(string Tag,string Json,LayerConversion Result,bool Closed);
    internal static void RunLayerBox(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Layer box: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),preview=Path.Combine(output,"preview");
        Directory.CreateDirectory(preview);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        var t80=new CassetteModel(presets.First(p=>p.Id=="t80u"),materials,reactions);var table=ProtectionFactorTable.Build(t80);
        double Density(string id)=>materials[id].Density;double Rha(string id)=>materials[id].RhaFactor;
        var forward=Vector3.UnitZ;var evidence=new Dictionary<string,object>();var rows=new StringBuilder("model\tlayout\tlayers\tfront faces\tside walls kept\tside walls whole\tside walls dropped\tstrips\tcomponents\tdepth mm\touter deviation mm\tthinnest mm\tmass block kg\tweight in game (managed) kg\tsum of layers and walls kg\tdeviation %\n");

        // ---------- models ----------
        var a=BoxJson(Corners(1,.8f,.803f,0));var b=WedgeJson();var c=BoxJson(Corners(1,.8f,.803f,30));var d=BoxJson(Corners(1,.8f,.803f,0),withBack:false);var e=BoxJson(Corners(1,.8f,.3f,0));
        var userBytes=File.ReadAllBytes(UserBlueprint);var userHash=Convert.ToHexString(SHA256.HashData(userBytes));var g=UserFill(userBytes);var f=Flat(803);
        var cases=new List<BoxCase>();
        foreach(var (tag,json,closed) in new[]{("(a) box 1000 x 800 x 803 mm",a,true),("(b) wedge 400 -> 800 mm",b,true),("(c) box turned 30 degrees",c,true),("(d) open box",d,false),("(e) box 300 mm",e,true)})
        {
            var r=LayerFaces.Convert(json,t80,null,Density,forward);cases.Add(new(tag,json,r,closed));
        }
        // ---------- 1. shape ----------
        foreach(var bc in cases)
        {
            var r=bc.Result;var box=r.Box;
            Check(box!=null,$"{bc.Tag}: converted as a box (front faces with side or back faces around them)");if(box==null)continue;
            Check(r.Faces.All(x=>Vector3.Dot(CheekShellFill.Newell(x.Front),x.Normal)>0&&Vector3.Dot(CheekShellFill.Newell(x.Back),x.Normal)>0),$"{bc.Tag}: no face turns over ({r.Faces.Length} faces)");
            Check(r.Faces.All(x=>x.Thickness.All(t=>t>=3)),$"{bc.Tag}: every thickness is a whole millimetre of at least 3 mm (thinnest {r.ThinnestMm:0} mm)");
            Check(box.FrontFaces>=1&&r.Faces.Count(x=>x.Rank>=0)==box.FrontFaces*r.Layout.Groups.Length,$"{bc.Tag}: the front faces become {r.Faces.Count(x=>x.Rank>=0)} layer faces ({r.Layout.Groups.Length} each)");
            Check(r.Sources.Where(s=>s.Skip=="back face").All(s=>!r.Faces.Any(x=>x.Source==s.Index)),$"{bc.Tag}: the back faces are replaced by the layers ({box.BackFaces})");
            // A.4: every layer point lies on its rib at the share of its layer in the recipe.
            double ribError=0;
            foreach(var x in r.Faces.Where(x=>x.Rank>=0))
            {
                var src=r.Sources.First(s=>s.Index==x.Source);var fs=r.Layout.Groups[x.Rank].StartFraction;
                for(int j=0;j<x.Front.Length;j++)ribError=Math.Max(ribError,Vector3.Distance(x.Front[j],src.Front[j]+src.LineDirection[j]*(float)(fs*src.LineLengthMm[j]*.001))*1000);
            }
            Check(ribError<=.01,$"{bc.Tag}: every layer point lies on its rib at the share of its layer ({ribError:0.####} mm)");
            Check(!bc.Closed||box.Components==1,$"{bc.Tag}: the mesh is one connected whole ({box.Components} part(s))");
            Check(box.OuterDeviationMm<=1,$"{bc.Tag}: the back of the last layer lies within {box.OuterDeviationMm:0.###} mm of the old back (limit 1 mm, perpendicular)");
            // The strips lie in the planes of the old side faces (the outer form keeps its sides) and only where a side face was.
            double planeError=0;
            foreach(var s in r.Faces.Where(x=>x.Rank==LayerHit.SideWallRank))
            {
                var src=r.Sources.First(q=>q.Index==s.Source);Check(src.Skip=="side face","strip from a side face");
                foreach(var p in s.Front)planeError=Math.Max(planeError,Math.Abs(Vector3.Dot(p-src.Front[0],src.Normal))*1000);
            }
            Check(planeError<=.01,$"{bc.Tag}: the strips lie in the planes of the old side faces ({planeError:0.####} mm)");
            Check(box.Strips.Length==box.SideWallsKept*(box.Strips.Length/Math.Max(1,box.SideWallsKept))&&box.Strips.All(s=>s.To>s.From),$"{bc.Tag}: {box.SideWallsKept} side faces became {box.Strips.Length} strips, one per layer or air layer");
            // A.6: the strips share their points with the layers (the box stays closed): every layer corner on a side lies on a strip.
            var stripPoints=new HashSet<int>(r.Faces.Where(x=>x.Rank==LayerHit.SideWallRank).SelectMany(x=>x.Vertices));
            Check(box.SideWallsKept==0||r.Faces.Where(x=>x.Rank>=0).All(x=>x.Vertices.Count(stripPoints.Contains)>=2),$"{bc.Tag}: the strips share their points with the layers");
        }
        // The class threshold on the turned box: its front stays front (30 degrees), its sides stay sides (60 degrees).
        var turned=cases[2].Result.Box!;
        Check(turned.FrontFaces==1&&turned.BackFaces==1&&turned.SideFaces==4,$"(c): front/back/side threshold cos 55 deg = {LayerFaces.FrontCos:0.####}: 1 front, 1 back, 4 sides");
        Check(LayerFaces.Classify(Vector3.Normalize(new Vector3(MathF.Sin(.5236f),0,MathF.Cos(.5236f))),forward)==BoxFaceKind.Front&&LayerFaces.Classify(Vector3.Normalize(new Vector3(MathF.Sin(1.0472f),0,MathF.Cos(1.0472f))),forward)==BoxFaceKind.Side&&LayerFaces.Classify(-forward,forward)==BoxFaceKind.Back,"threshold: 30 degrees from forward is a front face, 60 degrees a side face, backward a back face");
        // ---------- 2. side walls ----------
        var open=cases[3].Result;
        Check(open.Box!.BackFaces==0&&open.Box.SideWallsKept==4&&open.Box.Components==1&&open.Box.RibLines==4,"(d) open box: the four side faces lead the ribs to the open rear rim; they become steel strips, no back wall is added, the layers close the box at the rim (recorded behaviour)");
        foreach(var (tag,json) in new[]{("(f) loose plate",f),("(g) T-80BVM object 280",g)})
        {
            var now=LayerFaces.Convert(json,t80,null,Density,forward);var old=LegacyLayerFaces060.Convert(json,t80,null,Density);
            // 0.22.80: a loose plate (an open sheet that is no box and no filling of ribs) is the front of a sheet: its layers run to the chosen thickness; a filling keeps 0.22.60.
            if(tag=="(f) loose plate")Check(now.Inspection.Contains("open sheet: the whole sheet is the front",StringComparison.Ordinal)&&now.Sources.All(x=>x.Converted)&&now.DepthCorners==now.Sources.SelectMany(x=>x.Front).Distinct().Count(),$"{tag}: not a box; the whole sheet is the front and its layers run to the chosen thickness (0.22.80)");
            else Check(now.Box==null&&now.Plan.Json==old.Plan.Json&&now.Inspection==old.Inspection,$"{tag}: not a box; payload and window byte-identical to 0.22.60");
        }
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(UserBlueprint)))==userHash,"the user's blueprint was only read");
        // ---------- 3. recognition after save and load, also after a layer moved 10 mm ----------
        foreach(var bc in cases)
        {
            var r=bc.Result;var wrapper="{\"v\":\"0.2\",\"name\":"+JsonSerializer.Serialize(CompositePlate.NameForMode(r.Mode))+",\"mesh\":"+r.Plan.Json+"}";
            using var doc=JsonDocument.Parse(wrapper);var mesh=doc.RootElement.GetProperty("mesh").GetRawText();
            Check(LayerFaces.VerifyCopy(r.Plan.Json,mesh)==null,$"{bc.Tag}: save and load keep every point, edge, thickness and tm/te token");
            var ranks=LayerRanks.FromJson(mesh);
            bool exact=r.Faces.Select((x,i)=>x.Rank>=0?ranks[i]==x.Rank:x.Rank==LayerHit.SideWallRank?ranks[i]==LayerHit.SideWallRank:true).All(z=>z);
            Check(exact,$"{bc.Tag}: after loading every layer face has its own layer and every strip is a side wall ({ranks.Count(z=>z>=0)} layers, {ranks.Count(z=>z==-2)} side walls)");
            Check(LayerRanks.Line(ranks,r.Layout.Groups.Length).Contains(" side walls.")==(r.Box!.Strips.Length>0),$"{bc.Tag}: composer line {LayerRanks.Line(ranks,r.Layout.Groups.Length)}");
            int k=Math.Min(2,r.Layout.Groups.Length-2);var moved=MoveLayer(mesh,r,k,-forward*.010f);var after=LayerRanks.FromJson(moved);
            bool still=r.Faces.Select((x,i)=>x.Rank>=0?after[i]>=0&&after[i]<r.Layout.Groups.Length:x.Rank==LayerHit.SideWallRank?after[i]==LayerHit.SideWallRank:true).All(z=>z);
            Check(still,$"{bc.Tag}: after layer {k+1} moved 10 mm back every layer face is assigned and every strip still a side wall");
        }
        // ---------- 4. hit ----------
        var hitRows=new StringBuilder("shot\tpiece\tlength mm\tfactor\tmm RHA-eq\n");var hitLines=new List<string>();
        var layered=LayeredRecipe.Build(t80,cases[0].Result.Layout,Rha);var blockMm=table.ProtectionMm(ProtectionThreat.LongRod,0);
        double FrontShot(LayerConversion r,ProtectionThreat threat)
        {
            var chords=ShotChords(r,new Vector3(0,.01f,1),-Vector3.UnitZ);var lr=LayeredRecipe.Build(t80,r.Layout,Rha);
            var clipped=LayerHit.ClipSideWalls(chords);var fs=new double[clipped.Count];var ts=new double[clipped.Count];LayerHit.Factors(lr,table,threat,clipped,fs,ts);
            return clipped.Select((q,i)=>(q.EndM-q.StartM)*1000*fs[i]).Sum();
        }
        foreach(var threat in new[]{ProtectionThreat.LongRod,ProtectionThreat.Jet})
        {
            double block=table.ProtectionMm(threat,0),nominal=LayerHit.Estimate(layered,table,threat,0,LayerHit.HitModel.PerLayer),boxA=FrontShot(cases[0].Result,threat),boxE=FrontShot(cases[4].Result,threat);
            Check(Math.Abs(nominal-block)<=1e-6*block,$"{ProtectionFactorTable.Text(threat)} 0 deg: a plate in layers at the recipe depth equals the block ({nominal:0.#} vs {block:0.#} mm)");
            Check(Math.Abs(boxA-block)/block<.02,$"{ProtectionFactorTable.Text(threat)} 0 deg straight through box (a) (803 mm, real geometry): {boxA:0.#} mm against the block {block:0.#} mm ({(boxA-block)/block*100:+0.0;-0.0}%)");
            Check(boxE<boxA,$"{ProtectionFactorTable.Text(threat)} 0 deg through box (e) (300 mm): {boxE:0.#} mm, {(boxE-boxA)/boxA*100:0.#}% against (a)");
            hitLines.Add(string.Create(CultureInfo.InvariantCulture,$"{ProtectionFactorTable.Text(threat)} 0°: block {block:0}, box (a) {boxA:0} ({(boxA-block)/block*100:+0.0;-0.0}%), box (e) {boxE:0} ({(boxE-boxA)/boxA*100:+0.0;-0.0}% vs a)"));
        }
        // From the side through (a): once at the depth of a layer, once at the depth of an air layer.
        {
            var r=cases[0].Result;var lr=LayeredRecipe.Build(t80,r.Layout,Rha);var grp=r.Layout.Groups;
            int gap=Enumerable.Range(0,grp.Length-1).First(i=>grp[i+1].StartFraction-grp[i].EndFraction>1e-6);
            double zLayer=.4015-(grp[1].StartFraction+grp[1].EndFraction)/2*.803,zAir=.4015-(grp[gap].EndFraction+grp[gap+1].StartFraction)/2*.803;
            foreach(var (name,z) in new[]{("side shot at layer 2",zLayer),("side shot at an air layer",zAir)})
            {
                var raw=ShotChords(r,new Vector3(-1,.013f,(float)z),Vector3.UnitX);var clipped=LayerHit.ClipSideWalls(raw);
                var fs=new double[clipped.Count];var ts=new double[clipped.Count];LayerHit.Factors(lr,table,ProtectionThreat.LongRod,clipped,fs,ts);
                for(int i=0;i<clipped.Count;i++)hitRows.AppendLine(string.Join("\t",name,LayerHit.Label("T-80U",lr,clipped[i],ts[i]),F((clipped[i].EndM-clipped[i].StartM)*1000),F(fs[i]),F((clipped[i].EndM-clipped[i].StartM)*1000*fs[i])));
                bool wallFirst=raw.OrderBy(q=>q.StartM).First().Rank==LayerHit.SideWallRank||raw.Where(q=>q.Rank!=LayerHit.SideWallRank).DefaultIfEmpty().Min(q=>q.StartM)>=raw.Where(q=>q.Rank==LayerHit.SideWallRank).Min(q=>q.StartM);
                if(name.Contains("layer 2"))
                {
                    var walls=clipped.Where(q=>q.Rank==LayerHit.SideWallRank).Sum(q=>q.EndM-q.StartM);var layer=clipped.Where(q=>q.Rank==1);
                    Check(raw.Any(q=>q.Rank==LayerHit.SideWallRank)&&walls<1e-9&&layer.Any()&&Math.Abs(layer.Sum(q=>q.EndM-q.StartM)-1)<.002,$"{name}: the side walls and layer 2 claim the same stretch; it is paid once, by the layer ({layer.Sum(q=>q.EndM-q.StartM)*1000:0.#} mm of layer, {walls*1000:0.#} mm of wall)");
                    hitLines.Add(string.Create(CultureInfo.InvariantCulture,$"side, layer 2: the walls' 2 x 5 mm lie inside the layer and are paid by it; {layer.Sum(q=>q.EndM-q.StartM)*1000:0} mm of layer 2"));
                }
                else
                {
                    var walls=clipped.Where(q=>q.Rank==LayerHit.SideWallRank).ToArray();
                    Check(wallFirst&&walls.Length==2&&walls.All(q=>Math.Abs(q.EndM-q.StartM-.005)<.0005)&&clipped.Select((q,i)=>(q,i)).Where(x=>x.q.Rank==LayerHit.SideWallRank).All(x=>fs[x.i]==1),$"{name}: first the side wall as steel (factor 1, 5 mm, the side wall setting), then nothing (air), then the far side wall");
                    hitLines.Add(string.Create(CultureInfo.InvariantCulture,$"side, air layer: 2 side walls x 5 mm as steel = {walls.Sum(q=>(q.EndM-q.StartM)*1000):0} mm RHA"));
                }
            }
        }
        File.WriteAllText(Path.Combine(output,"BOX-HIT.tsv"),hitRows.ToString());
        Check(LayerHit.Label("T-80U",layered,new(0,.02,0,LayerHit.SideWallRank,false),20)=="T-80U, side wall (20 mm, 0°)","report label of a side wall: side wall, thickness and angle");
        // ---------- 5. overlay ----------
        foreach(var bc in cases.Where(x=>x.Closed))
        {
            var r=bc.Result;var (copy,owners)=SyntheticOverlay(r,remapFaces:true);var mesh=LayerMesh.Read(r.Plan.Json);var ranks=LayerRanks.FromJson(r.Plan.Json);
            var mapped=LayerOverlay.Remap(copy,mesh,ranks,r.Layout);
            int layerTris=0,stripTris=0,bad=0;var why=new List<string>();var airStrips=r.Box!.Strips.Count(s=>s.Air);
            // The remap keeps the order of the triangles it keeps and moves no vertex, so every kept triangle is matched to its source triangle by walking both
            // lists in step. Two neighbouring strips share their end cap (the same quad twice), and the cap of a strip over air is the cap of the layer beside it:
            // such coincident triangles cannot be told apart by position, so a triangle is judged against every face that has a triangle at the same place.
            // It must be kept, showing one of those faces, unless all of them are strips over air; then it must be left out.
            var airFaces=r.Box.Strips.Select(s=>s.Face).ToHashSet(); // 0.22.63: every side wall strip is left out of the overlay, not only the ones over air
            (Vector3 Centre,int Part) Slot(int tri){var ia=copy.Triangles[tri*3];var ib=copy.Triangles[tri*3+1];var ic=copy.Triangles[tri*3+2];float a0=copy.Uv[ia].Y,b0=copy.Uv[ib].Y,c0=copy.Uv[ic].Y;int part=a0==0&&b0==0&&c0==0?0:a0==1&&b0==1&&c0==1?1:2;return ((copy.Vertices[ia]+copy.Vertices[ib]+copy.Vertices[ic])/3,part);}
            // Faces with a polygon of the same part within the overlay tolerance of the triangle (from the conversion's own front and back points, not from the mesh reader).
            var sharingCache=new Dictionary<int,List<int>>();
            List<int> Sharing(int tri)
            {
                if(sharingCache.TryGetValue(tri,out var cached))return cached;
                var (centre,part)=Slot(tri);var list=new List<int>();
                for(int i=0;i<r.Faces.Length;i++)
                {
                    var fr=r.Faces[i].Front;var bk=r.Faces[i].Back;bool near=false;
                    if(part==0)near=LayerOverlay.Distance(centre,fr)<LayerOverlay.ToleranceM;else if(part==1)near=LayerOverlay.Distance(centre,bk)<LayerOverlay.ToleranceM;
                    else for(int j=0;j<fr.Length&&!near;j++)near=LayerOverlay.Distance(centre,new[]{fr[j],fr[(j+1)%fr.Length],bk[(j+1)%fr.Length],bk[j]})<LayerOverlay.ToleranceM;
                    if(near)list.Add(i);
                }
                return sharingCache[tri]=list;
            }
            bool Fits(int face,float[] ds)
            {
                var x=r.Faces[face];
                if(x.Rank>=0){var grp=r.Layout.Groups[x.Rank];return ds.All(q=>q>=grp.StartFraction-1e-5&&q<=grp.EndFraction+1e-5);}
                if(x.Rank==LayerHit.SideWallRank){var s=r.Box.Strips.First(q=>q.Face==face);return !s.Air&&ds.All(q=>q>=s.From-1e-4&&q<=s.To+1e-4);}
                return true;
            }
            // 0.22.62: a strip over air is not removed but collapsed to a zero-area triangle (the native-coverage check counts triangles), so the output has one triangle per source triangle in the same order.
            int dropped=0,coincident=0;
            void Skipped(int q){dropped++;if(Sharing(q).Any(o=>!airFaces.Contains(o))){bad++;if(why.Count<4)why.Add($"a triangle of face {owners[q]} was left out although a face that is not empty space lies there");}}
            if(mapped.Triangles.Length!=copy.Triangles.Length){bad++;why.Add($"the remap changed the triangle count ({copy.Triangles.Length/3} -> {mapped.Triangles.Length/3})");}
            for(int t=0;t+2<mapped.Triangles.Length&&t<copy.Triangles.Length;t+=3)
            {
                int src=t/3;
                var m0=mapped.Vertices[mapped.Triangles[t]];bool collapsed=m0==mapped.Vertices[mapped.Triangles[t+1]]&&m0==mapped.Vertices[mapped.Triangles[t+2]];
                if(collapsed){Skipped(src);continue;}
                var ds=new[]{mapped.Uv[mapped.Triangles[t]].Y,mapped.Uv[mapped.Triangles[t+1]].Y,mapped.Uv[mapped.Triangles[t+2]].Y};
                var candidates=Sharing(src).Distinct().Where(o=>!airFaces.Contains(o)).ToList();int face=owners[src];var x=r.Faces[face];
                if(x.Rank>=0)layerTris++;else if(x.Rank==LayerHit.SideWallRank)stripTris++;
                if(airFaces.Contains(face))coincident++;
                if(candidates.Count==0||!candidates.Any(o=>Fits(o,ds))){bad++;if(why.Count<4)why.Add($"face {face} shows d {string.Join("/",ds.Select(q=>q.ToString("0.####")))}, which fits none of the faces at that place");}
            }
            var airTris=r.Box.Strips.Sum(s=>owners.Count(o=>o==s.Face));
            Check(bad==0&&layerTris>0&&dropped>0&&dropped+coincident==airTris,$"{bc.Tag}: overlay: each layer face shows only its own layer ({layerTris} triangles), each strip the layer it spans ({stripTris}), strips over air are left out ({dropped} triangles of {airStrips} air strips; the other {coincident} are the end caps they share with the layer beside them and show that layer; {bad} wrong: {string.Join(" | ",why)})");
        }
        Check(CompositePlate.LayoutFromName(CompositePlate.Name)==null&&CompositePlate.LayoutFromName(CompositePlate.FillName)==null,"an ordinary composite plate keeps the 0.22.60 overlay (no layer mark, the remap is not used)");
        // ---------- 6. weight ----------
        var weights=new List<object>();
        foreach(var (tag,json,result) in cases.Select(x=>(x.Tag,x.Json,x.Result)).Append(("(g) T-80BVM object 280",g,LayerFaces.Convert(g,t80,null,Density,forward))))
        {
            var mesh=LayerMesh.Read(result.Plan.Json);var ranks=LayerRanks.FromJson(result.Plan.Json);
            var w=LayerWeight.Of(mesh,ranks,result.Layout,t80.Preset,Density,t80.Passive.Density);
            // The game multiplies the plate's density with its own armour volume (here: the volume of the faces read back from the payload).
            double game=w.VolumeM3*w.Density,truth=result.TrueMassKg!.Value,dev=(game-truth)/truth*100;
            if(!tag.StartsWith("(c)")&&!tag.StartsWith("(d)"))Check(Math.Abs(dev)<=3,$"{tag}: weight {game:0.#} kg in the game against {truth:0.#} kg of layers and side walls ({dev:+0.00;-0.00}%, limit 3 %)");
            weights.Add(new{tag,diluted=result.MassAfterKg,game,truth,dev});
            var box=result.Box;
            rows.AppendLine(string.Join("\t",tag,result.Layout.Kind,result.Layout.Groups.Length,box?.FrontFaces??result.ConvertedFaces,box?.SideWallsKept??0,box?.SideWallsWhole??0,box?.SideWallsDropped??0,box?.Strips.Length??0,box?.Components??0,F(box?.DepthMm??0),F(box?.OuterDeviationMm??0),F(result.ThinnestMm),F(result.MassBeforeKg),F(game),F(truth),F(dev)));
        }
        // The weight reaches the game only through the existing PlateStructure.Build postfix (no new patch) and writes only the fields the missing-recipe fallback writes.
        {
            var visual=File.ReadAllText(Path.Combine(root,"RuntimeCassettePlateVisual.cs"));var fill=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));
            var weigh=fill[fill.IndexOf("private static bool TryWeigh(",StringComparison.Ordinal)..fill.IndexOf("0.22.61: the overlay of a plate in layers",StringComparison.Ordinal)];
            var callers=Directory.GetFiles(root,"*.cs").Where(p=>File.ReadAllText(p).Contains("RuntimeLayerFaces.Weigh(")).Select(Path.GetFileName).ToArray();
            Check(visual.Contains("private static void Built(PlateStructure __instance){Enqueue(__instance,true);RuntimeLayerFaces.Weigh(__instance);}")&&callers.Length==1&&callers[0]=="RuntimeCassettePlateVisual.cs","weight: called only from the existing PlateStructure.Build postfix");
            var written=System.Text.RegularExpressions.Regex.Matches(weigh,@"plate\.(armourDensity|cachedMass|damageModelParameters|armourCostMultiplier|armourTechID)\s*=(?!=)|plate\.(SetMass|SetCost|RecalculateCenterOfMass|Vehicle\.RaiseDirtyFlags|SetArmourMaterial|RequestRebuild)\(").Select(m=>m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            Check(string.Join(",",written)=="RecalculateCenterOfMass,SetCost,SetMass,Vehicle.RaiseDirtyFlags,armourDensity,cachedMass","weight: writes only armourDensity, mass, cached mass, centre of mass, cost and the damage-model flag ("+string.Join(", ",written)+"), a subset of the missing-recipe fallback");
        }
        // ---------- 7. window ----------
        foreach(var bc in cases)
        {
            var r=bc.Result;var box=r.Box!;var text=r.Inspection;
            Check(text.Contains(LayerFaces.DirectionLine)&&text.Contains($"{box.FrontFaces} faces become {box.FrontFaces*r.Layout.Groups.Length} faces: {r.Layout.Groups.Length} layers each."),$"{bc.Tag}: window: direction and face count");
            Check((box.SideWallsKept>0)==text.Contains($"{box.SideWallsKept} side walls kept as steel.")&&(box.SideWallsDropped>0)==text.Contains($"{box.SideWallsDropped} side walls could not be kept."),$"{bc.Tag}: window: side walls kept {box.SideWallsKept}, dropped {box.SideWallsDropped}");
            bool shallow=box.DepthMm<r.Layout.DepthMm-.5;
            Check(shallow==text.Contains("Protection scales with the depth.")&&(!shallow||text.Contains($"The box is {box.DepthMm:0} mm deep; the recipe is made for {r.Layout.DepthMm:0} mm.")),$"{bc.Tag}: window: depth line {(shallow?"shown":"absent")}");
        }
        Check(cases[4].Result.Inspection.Contains("The box is 300 mm deep; the recipe is made for 803 mm. Protection scales with the depth."),"(e): the shallow box says so: "+cases[4].Result.Inspection.Split('\n').FirstOrDefault(l=>l.StartsWith("The box"),""));
        File.WriteAllText(Path.Combine(output,"BOX-MODELS.tsv"),rows.ToString());
        // ---------- pictures ----------
        var pictures=new List<string>();var strip=CassetteDepthStrip.Build(t80.Preset);
        foreach(var (stem,bc) in new[]{("a",cases[0]),("b",cases[1]),("e",cases[4])})
        {
            var r=bc.Result;var before=SyntheticOverlay(BeforeOf(bc.Json),false).Copy;var (after,_)=SyntheticOverlay(r,true);
            var mapped=LayerOverlay.Remap(after,LayerMesh.Read(r.Plan.Json),LayerRanks.FromJson(r.Plan.Json),r.Layout);
            var bounds=(new Vector3(-.75f,-.6f,-.6f),new Vector3(.75f,.6f,.6f));
            foreach(var (kind,data) in new[]{("before",before),("after",mapped)})
            foreach(var (view,dir) in new[]{("side",new Vector3(-1,0.001f,0)),("oblique",new Vector3(-.75f,.55f,1))})
            {
                var tris=new List<SolidTriangle>();
                for(int t=0;t<data.Triangles.Length;t+=3)
                {
                    var p0=data.Vertices[data.Triangles[t]];var p1=data.Vertices[data.Triangles[t+1]];var p2=data.Vertices[data.Triangles[t+2]];
                    var dmean=(data.Uv[data.Triangles[t]].Y+data.Uv[data.Triangles[t+1]].Y+data.Uv[data.Triangles[t+2]].Y)/3;
                    var px=strip[Math.Clamp((int)(dmean*CassetteDepthStrip.Height),0,CassetteDepthStrip.Height-1)];
                    var n=Vector3.Cross(p1-p0,p2-p0);if(n.LengthSquared()<1e-16f)continue;n=Vector3.Normalize(n);float l=.6f+.4f*MathF.Abs(Vector3.Dot(n,Vector3.Normalize(new Vector3(.4f,.7f,.5f))));
                    tris.Add(new(p0,p1,p2,n,Raster.Pack((byte)Math.Min(255,(px>>24)*l+20),(byte)Math.Min(255,((px>>16)&255)*l+20),(byte)Math.Min(255,((px>>8)&255)*l+20),255)));
                }
                var path=Path.Combine(preview,$"box-{stem}-{kind}-{view}.png");File.WriteAllBytes(path,Render(tris,new Camera(bounds,dir)));pictures.Add(Path.GetFileName(path));
            }
        }
        Check(pictures.Count==12,"twelve overlay pictures written (before/after, side/oblique, for (a), (b), (e))");
        evidence["passed"]=failures==0;evidence["checks"]=checks;evidence["blueprintSha256"]=userHash;evidence["blueprint"]=UserBlueprint;evidence["weights"]=weights;evidence["hit"]=hitLines;evidence["pictures"]=pictures;
        evidence["models"]=cases.Select(x=>new{x.Tag,layout=x.Result.Layout.Kind.ToString(),layers=x.Result.Layout.Groups.Length,kept=x.Result.Box!.SideWallsKept,whole=x.Result.Box.SideWallsWhole,dropped=x.Result.Box.SideWallsDropped,strips=x.Result.Box.Strips.Length,components=x.Result.Box.Components,depth=x.Result.Box.DepthMm,deviation=x.Result.Box.OuterDeviationMm,window=x.Result.Inspection}).ToArray();
        File.WriteAllText(Path.Combine(output,"BOX-EVIDENCE.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT61 PASS: {checks} checks, {failures} failures; "+string.Join("; ",hitLines));
    }

    private static (int,int,int) Key(Vector3 p)=>((int)MathF.Round(p.X*1e5f),(int)MathF.Round(p.Y*1e5f),(int)MathF.Round(p.Z*1e5f));
    /// <summary>The eight corners of a box (front quad, then back quad), width x height, depth along +Z, turned about the vertical axis.</summary>
    private static Vector3[] Corners(float w,float h,float depth,float turnDegrees)
    {
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,turnDegrees*MathF.PI/180);
        var p=new[]{new Vector3(-w/2,-h/2,depth/2),new Vector3(w/2,-h/2,depth/2),new Vector3(w/2,h/2,depth/2),new Vector3(-w/2,h/2,depth/2),
            new Vector3(-w/2,-h/2,-depth/2),new Vector3(w/2,-h/2,-depth/2),new Vector3(w/2,h/2,-depth/2),new Vector3(-w/2,h/2,-depth/2)};
        return p.Select(x=>Vector3.Transform(x,q)).ToArray();
    }
    /// <summary>A kneaded box as the game saves it: every face 20 mm on Auto (tm 1), outward normals, twelve edges.</summary>
    private static string BoxJson(Vector3[] p,bool withBack=true)
    {
        var faces=new List<int[]>{new[]{0,1,2,3}};if(withBack)faces.Add(new[]{4,5,6,7});
        for(int j=0;j<4;j++)faces.Add(new[]{j,(j+1)%4,4+(j+1)%4,4+j});
        var centre=p.Aggregate(Vector3.Zero,(x,y)=>x+y)/8;
        for(int i=0;i<faces.Count;i++)
        {
            var poly=faces[i].Select(k=>p[k]).ToArray();var n=CheekShellFill.Newell(poly);var mid=poly.Aggregate(Vector3.Zero,(x,y)=>x+y)/poly.Length;
            if(Vector3.Dot(n,mid-centre)<0)faces[i]=faces[i].Reverse().ToArray();
        }
        var edges=new List<(int,int)>();foreach(var fc in faces)for(int j=0;j<fc.Length;j++){var k=(Math.Min(fc[j],fc[(j+1)%fc.Length]),Math.Max(fc[j],fc[(j+1)%fc.Length]));if(!edges.Contains(k))edges.Add(k);}
        return Mesh(p,edges,faces.Select(fc=>Face(fc,Enumerable.Repeat(20,fc.Length),0x01010101UL&((1UL<<(8*fc.Length))-1))));
    }
    /// <summary>A closed wedge: the back in z = 0, the front slanted from 800 mm deep at the bottom to 400 mm at the top.</summary>
    private static string WedgeJson()=>BoxJson(new[]{new Vector3(-.5f,-.4f,.8f),new Vector3(.5f,-.4f,.8f),new Vector3(.5f,.4f,.4f),new Vector3(-.5f,.4f,.4f),
        new Vector3(-.5f,-.4f,0),new Vector3(.5f,-.4f,0),new Vector3(.5f,.4f,0),new Vector3(-.5f,.4f,0)});
    /// <summary>The faces of the original mesh as a conversion-like list (for the before picture): every face with its own 20 mm along its normal.</summary>
    private static LayerConversion BeforeOf(string json)
    {
        var snap=ComposerFill.Read(json);var faces=snap.Faces.Select((x,i)=>new LayerFaceInfo(i,-1,x.VertexIds,x.Corners,x.Corners.Select((q,j)=>q-x.Normal*x.Thickness[j]*.001f).ToArray(),x.Thickness,new byte[x.VertexCount],new int[x.VertexCount],x.Normal)).ToArray();
        return new(null!,null!,ThicknessMode.Uniform,Array.Empty<LayerSourceInfo>(),faces,0,0,0,0,0,0,0,0,0,null,"");
    }
    /// <summary>A stand-in for the native overlay copy: per face its front (d 0), its back (d 1) and its sides (d 0 at the front, 1 at the back), with the owner of every triangle.</summary>
    private static (OverlayCopy Copy,int[] Owners) SyntheticOverlay(LayerConversion r,bool remapFaces)
    {
        var v=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();var owners=new List<int>();
        void Tri(Vector3 a,float da,Vector3 b,float db,Vector3 c,float dc,int owner)
        {
            var nn=Vector3.Cross(b-a,c-a);if(nn.LengthSquared()<1e-16f)return;nn=Vector3.Normalize(nn);
            foreach(var (p,d) in new[]{(a,da),(b,db),(c,dc)}){t.Add(v.Count);v.Add(p);n.Add(nn);uv.Add(new(.5f,d));}owners.Add(owner);
        }
        for(int i=0;i<r.Faces.Length;i++)
        {
            var f=r.Faces[i].Front;var b=r.Faces[i].Back;int k=f.Length;
            for(int j=1;j+1<k;j++){Tri(f[0],0,f[j],0,f[j+1],0,i);Tri(b[0],1,b[j+1],1,b[j],1,i);}
            for(int j=0;j<k;j++){int q=(j+1)%k;Tri(f[j],0,f[q],0,b[q],1,i);Tri(f[j],0,b[q],1,b[j],1,i);}
        }
        return (new(v.ToArray(),t.ToArray(),n.ToArray(),uv.ToArray()),owners.ToArray());
    }
    /// <summary>The pieces of the converted plate along one shot line, from the real face solids (front polygon, real back, sides), with the rank of each face.</summary>
    private static List<LayerChord> ShotChords(LayerConversion r,Vector3 origin,Vector3 direction)
    {
        var ranks=LayerRanks.FromJson(r.Plan.Json);var chords=new List<LayerChord>();
        for(int i=0;i<r.Faces.Length;i++)
        {
            var f=r.Faces[i].Front;var b=r.Faces[i].Back;int k=f.Length;var hits=new List<double>();
            void Tri(Vector3 a,Vector3 c,Vector3 e){if(ComposerFill.Ray(origin,direction,new[]{new FillTriangle(a,c,e)},10) is {} t)hits.Add(t);}
            for(int j=1;j+1<k;j++){Tri(f[0],f[j],f[j+1]);Tri(b[0],b[j+1],b[j]);}
            for(int j=0;j<k;j++){int q=(j+1)%k;Tri(f[j],f[q],b[q]);Tri(f[j],b[q],b[j]);}
            if(hits.Count<2)continue;hits.Sort();
            var angle=Math.Acos(Math.Clamp(Math.Abs(Vector3.Dot(Vector3.Normalize(direction),r.Faces[i].Normal)),0,1))*180/Math.PI;
            chords.Add(new(hits[0],hits[^1],ranks[i]==LayerHit.SideWallRank?angle:angle,ranks[i],false));
        }
        chords.Sort((x,y)=>x.StartM.CompareTo(y.StartM));return chords;
    }
    /// <summary>The saved mesh with the front points of one layer moved (they are shared by its faces, the strips and the ribs).</summary>
    private static string MoveLayer(string json,LayerConversion r,int layer,Vector3 shift)
    {
        var ids=r.Faces.Where(x=>x.Rank==layer).SelectMany(x=>x.Vertices).Distinct().ToHashSet();
        var node=System.Text.Json.Nodes.JsonNode.Parse(json)!;var v=node["vertices"]!.AsArray();
        foreach(var id in ids){v[3*id]=v[3*id]!.GetValue<float>()+shift.X;v[3*id+1]=v[3*id+1]!.GetValue<float>()+shift.Y;v[3*id+2]=v[3*id+2]!.GetValue<float>()+shift.Z;}
        return node.ToJsonString();
    }
}
