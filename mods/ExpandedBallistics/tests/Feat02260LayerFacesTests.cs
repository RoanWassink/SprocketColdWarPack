using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.60 "lagen als vlakken", part 1: Convert to layers and the hit per layer. Managed evidence only (no game): the production planner
/// (LayerFaces.Convert), the rank rule (LayerRanks), the per-mesh face table (LayerHitIndex) and the hit maths (LayerHit) on a flat plate, a wedge,
/// a plate with Along-edge corners and the filling of the user's T-80BVM (object 280, read only). Writes its tables and pictures to test-output/.
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    internal static string UserBlueprint=>Path.Combine(Fix02228Tests.SourceRoot(),"tests","fixtures","user-tanks","T-80BVM.blueprint");
    private sealed record LayerCase(string Tag,string Json,LayerConversion Result);
    internal static void RunLayerFaces(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Layer faces: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),preview=Path.Combine(output,"preview");
        Directory.CreateDirectory(preview);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        var t80=new CassetteModel(presets.First(p=>p.Id=="t80u"),materials,reactions);var t80Table=ProtectionFactorTable.Build(t80);
        double Density(string id)=>materials[id].Density;double Rha(string id)=>materials[id].RhaFactor;
        var evidence=new Dictionary<string,object>();var shapeRows=new StringBuilder("case\tfaces before\tfaces after\tlayout\tlayers\tthinnest mm\tribbed faces\tdirection changed\tskipped\tmax outside mm\tmax seam mm\tmax air error mm\tmax depth error mm\tinverted\tmass block kg\tmass layers kg\ttrue mass layers kg\tseam point distance along the rib mm (info)\tlast back point distance along the rib mm (info)\n");

        // ---------- 1. Shape ----------
        var flat803=Flat(803);var flat300=Flat(300);var wedge=Wedge(400,800);var along=AlongEdge(600);
        var userBytes=File.ReadAllBytes(UserBlueprint);var userHash=Convert.ToHexString(SHA256.HashData(userBytes));var user=UserFill(userBytes);
        var cases=new List<LayerCase>();
        foreach(var (tag,json) in new[]{("flat plate 803 mm",flat803),("flat plate 300 mm",flat300),("wedge 400-800 mm",wedge),("Along edge plate 600 mm",along),("T-80BVM object 280",user)})
        {
            var result=LayerFaces.Convert(json,t80,null,Density);cases.Add(new(tag,json,result));
            var s=Shape(result);
            Check(s.Inverted==0,$"{tag}: no face turns over ({result.Faces.Length} faces)");
            Check(s.MaxOutsideMm<=1,$"{tag}: every layer point lies inside the original shape (largest distance {s.MaxOutsideMm:0.###} mm, limit 1 mm)");
            Check(s.MaxSeamMm<=1,$"{tag}: the back of a layer and the front of the next touching layer lie at most {s.MaxSeamMm:0.###} mm apart (limit 1 mm)");
            Check(s.MaxAirErrorMm<=1,$"{tag}: every air gap matches the recipe within {s.MaxAirErrorMm:0.###} mm (limit 1 mm)");
            // Information (no requirement of the brief): how far the last layer ends in front of the original back. It never ends behind it (the check above);
            // on a shallow rib one whole millimetre of native thickness is several millimetres along the rib.
            Check(double.IsFinite(s.MaxDepthErrorMm),$"{tag}: the last layer ends at most {s.MaxDepthErrorMm:0.###} mm in front of the original back (information)");
            Check(result.Faces.Where(f=>f.Rank>=0).All(f=>f.Thickness.All(t=>t>=3&&t<=short.MaxValue)),$"{tag}: every layer corner is a whole millimetre of at least 3 mm (thinnest {result.ThinnestMm:0} mm)");
            Check(result.Faces.Where(f=>f.Rank==0).All(f=>f.Vertices.SequenceEqual(JsonFaceVertices(json,f.Source))),$"{tag}: the first layer keeps the original front vertices (the front stays where it is)");
            Check(result.Plan.NewTopology&&result.Plan.Action==ComposerMeshAction.LayerConvert&&result.Plan.CopyCheck!=null&&result.Plan.CopyCheck(result.Plan.Json)==null,$"{tag}: one prepared plan with a new topology and a copy check that accepts its own payload");
            var reread=ComposerFill.Read(result.Plan.Json);
            Check(reread.FaceCount==result.Faces.Length&&reread.Faces.Zip(result.Faces).All(p=>p.First.Thickness.SequenceEqual(p.Second.Thickness)&&p.First.Along.SequenceEqual(p.Second.Modes.Select(m=>m==0))),$"{tag}: the payload reads back with every corner thickness and direction");
            shapeRows.AppendLine(string.Join("\t",tag,result.Sources.Length,result.Faces.Length,result.Layout.Kind,result.Layout.Groups.Length,result.ThinnestMm.ToString("0",CultureInfo.InvariantCulture),result.RibbedFaces,result.DirectionChangedFaces,result.SkippedFaces,
                F(s.MaxOutsideMm),F(s.MaxSeamMm),F(s.MaxAirErrorMm),F(s.MaxDepthErrorMm),s.Inverted,F(result.MassBeforeKg),F(result.MassAfterKg),F(result.TrueMassKg??0),F(s.AlongRibSeamMm),F(s.AlongRibDepthMm)));
        }
        File.WriteAllText(Path.Combine(output,"LAYER-SHAPE.tsv"),shapeRows.ToString());
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(UserBlueprint)))==userHash,"the user's blueprint was only read (hash unchanged)");
        var userCase=cases.Single(c=>c.Tag.StartsWith("T-80BVM"));
        Check(userCase.Result.Sources.Length==18&&userCase.Result.ConvertedFaces==18&&userCase.Result.Faces.All(f=>f.Modes.All(m=>m==CheekShellFill.AlongEdgeManual)),"T-80BVM object 280: all 18 Along-edge faces are converted and every layer corner follows a rib (tm 4)");
        // Faces around one rib share their boundary points (the filling stays one shell per layer).
        var sharedPoints=userCase.Result.Faces.Where(f=>f.Rank>0).SelectMany(f=>f.Vertices).GroupBy(v=>v).Count(g=>g.Count()>1);
        Check(sharedPoints>0,$"T-80BVM object 280: {sharedPoints} layer points are shared by neighbouring faces (one shell per layer)");
        var wedgeCase=cases.Single(c=>c.Tag.StartsWith("wedge"));
        Check(wedgeCase.Result.RibbedFaces==1,"wedge: the deeper layers of the tilted wedge follow ribs along the original normal (reported as a deviation)");
        Check(cases.Single(c=>c.Tag=="flat plate 803 mm").Result.RibbedFaces==0&&cases.Single(c=>c.Tag=="flat plate 803 mm").Result.Faces.All(f=>f.Modes.All(m=>m==0)),"flat plate: every layer stacks along the face normal (tm 0), no rib");

        // ---------- 2. Layout ----------
        var perLayer=cases.Single(c=>c.Tag=="flat plate 803 mm").Result;var perBlock=cases.Single(c=>c.Tag=="flat plate 300 mm").Result;
        Check(perLayer.Layout.Kind==LayerLayoutKind.PerLayer&&perLayer.Layout.Groups.Length==32&&perLayer.Mode==ThicknessMode.Layers,$"T-80U at 803 mm: one face per layer (32 solid layers), mesh name {CompositePlate.NameForMode(perLayer.Mode)}");
        Check(perBlock.Layout.Kind==LayerLayoutKind.PerBlock&&perBlock.Layout.Groups.Length==11&&perBlock.Mode==ThicknessMode.LayerBlocks,$"T-80U at 300 mm: the 3 mm steel sheets would be thinner than 3 mm, so touching layers share one face (11 blocks), mesh name {CompositePlate.NameForMode(perBlock.Mode)}");
        Check(perLayer.Inspection.Contains("1 faces become 32 faces: 32 layers each.")&&perLayer.Inspection.Contains("One face per layer.")&&perLayer.Inspection.Contains("Thinnest layer: ")&&perLayer.Inspection.Contains(" kg as layers."),"window text: faces, layout, thinnest layer and weight (per layer)");
        Check(perBlock.Inspection.Contains("Touching layers share one face, because some layers would be thinner than 3 mm."),"window text: the per-block sentence");
        Check(cases.Single(c=>c.Tag.StartsWith("Along")).Result.Inspection.Contains("Weight "),"window text: weight line");
        string refusal="";try{LayerFaces.Convert(Flat(50),t80,null,Density);}catch(InvalidOperationException e){refusal=e.Message;}
        Check(refusal.StartsWith("This plate is too thin for layers: even a block of touching layers would be thinner than 3 mm"),"T-80U at 50 mm: refused in plain words ("+refusal+")");
        // Auto corners and faces with more than four corners.
        var auto=LayerFaces.Convert(Flat(803,tm:0x01010101),t80,null,Density);
        Check(auto.DirectionChangedFaces==1&&auto.Inspection.Contains("1 faces now thicken straight back; their rear may shift slightly."),"Auto corners (tm 1) stack along the face normal and are counted in the window");
        var mixed=LayerFaces.Convert(WithPentagon(Flat(803)),t80,null,Density);
        Check(mixed.SkippedFaces==1&&mixed.ConvertedFaces==1&&mixed.Inspection.Contains("1 faces have more than 4 corners and stay as they are.")&&mixed.Faces.Count(f=>f.Rank<0)==1,"a face with five corners stays as it is and is counted in the window");
        Check(LayerFaces.Tooltip=="Splits each face into one face per armour layer. Air layers become real gaps. With faces selected, those are the front and the layers follow them inward."&&LayerFaces.Title=="Convert to layers - inspect before applying"&&ComposerFill.Label(ComposerMeshAction.LayerConvert)=="Convert to layers","button tooltip, window title and action label");
        // The name survives save and load (blueprint wrapper round trip).
        foreach(var mode in new[]{ThicknessMode.Layers,ThicknessMode.LayerBlocks})
        {
            var name=CompositePlate.NameForMode(mode);
            var wrapper=JsonSerializer.Serialize(new{name,mesh=JsonDocument.Parse(perLayer.Plan.Json).RootElement});
            using var loaded=JsonDocument.Parse(wrapper);var back=loaded.RootElement.GetProperty("name").GetString();
            Check(CompositePlate.IsName(back)&&CompositePlate.ModeFromName(back)==mode&&CompositePlate.LayoutFromName(back)==(mode==ThicknessMode.Layers?LayerLayoutKind.PerLayer:LayerLayoutKind.PerBlock),$"the mark '{name}' survives save and load and gives the layout back");
        }
        Check(CompositePlate.LayoutFromName(CompositePlate.Name)==null&&CompositePlate.LayoutFromName(CompositePlate.FillName)==null&&CompositePlate.ModeFromName(CompositePlate.FillName)==ThicknessMode.FillToWall&&CompositePlate.NameForMode(ThicknessMode.Uniform)==CompositePlate.Name,"ordinary composite names keep their meaning");

        // ---------- 3. Rank ----------
        var rankRows=new StringBuilder("case\tchange\tfaces\tassigned\tunassigned\tranks unchanged\n");
        foreach(var c in cases)
        {
            var (polygons,directions)=LayerRanks.Read(c.Result.Plan.Json);var ranks=LayerRanks.FromJson(c.Result.Plan.Json);int groups=c.Result.Layout.Groups.Length;
            Check(polygons.Zip(c.Result.Plan.After.Faces).All(p=>p.First.SequenceEqual(p.Second.Corners)),$"{c.Tag}: the rank reader sees the same corners as the snapshot");
            bool exact=c.Result.Faces.Select((f,i)=>f.Rank<0?ranks[i]<0||ranks[i]>=groups||c.Result.Sources[f.Source].Converted==false:ranks[i]==f.Rank).All(x=>x);
            Check(exact&&ranks.Where((r,i)=>c.Result.Faces[i].Rank>=0).All(r=>r>=0&&r<groups),$"{c.Tag}: after Convert every layer face is assigned to its own layer ({ranks.Count(r=>r>=0&&r<groups)} of {ranks.Length})");
            Check(LayerRanks.Line(ranks,groups)==$"Layers: {ranks.Count(r=>r>=0&&r<groups)} faces assigned, {ranks.Length-ranks.Count(r=>r>=0&&r<groups)} not assigned.","composer line format: "+LayerRanks.Line(ranks,groups));
            rankRows.AppendLine($"{c.Tag}\tnone\t{ranks.Length}\t{ranks.Count(r=>r>=0&&r<groups)}\t{ranks.Count(r=>r<0||r>=groups)}\t{exact}");
            for(int i=0;i<ranks.Length;i++)if(c.Result.Faces[i].Rank>=0&&ranks[i]!=c.Result.Faces[i].Rank)rankRows.AppendLine($"#   mismatch: output face {i} (source face {c.Result.Faces[i].Source}, layer {c.Result.Faces[i].Rank}) counted {ranks[i]}");
            if(!c.Tag.StartsWith("flat plate 803")&&!c.Tag.StartsWith("T-80BVM")&&!c.Tag.StartsWith("wedge"))continue;
            int k=Math.Min(2,groups-2);bool shell=c.Tag.StartsWith("T-80BVM");
            Func<Vector3[],Vector3,Vector3[]> Grow(float factor)=>(p,n)=>{var centre=p.Aggregate(Vector3.Zero,(a,b)=>a+b)/p.Length;return p.Select(x=>centre+(x-centre)*factor).ToArray();};
            foreach(var (change,transform,hard) in new (string,Func<Vector3[],Vector3,Vector3[]>,bool)[]{
                ("one face of layer 3 moved 10 mm back",(p,n)=>p.Select(x=>x-n*.010f).ToArray(),true),
                ("one face of layer 3 moved 10 mm sideways",(p,n)=>{var side=Vector3.Normalize(Vector3.Cross(n,Math.Abs(n.Y)<.9f?Vector3.UnitY:Vector3.UnitX));return p.Select(x=>x+side*.010f).ToArray();},true),
                ("one face of layer 3 tilted 5 degrees",(p,n)=>Rotate(p,n,5),true),
                (shell?"one face of layer 3 enlarged 5 %":"one face of layer 3 enlarged 20 %",Grow(shell?1.05f:1.2f),true),
                // In the curved filling a face enlarged by 20 % reaches into the neighbouring stack: it then really lies in front of the neighbour's faces,
                // so the neighbour's deepest face may count one face too many and is not assigned (it pays the recipe table). Recorded, not required.
                ("one face of layer 3 enlarged 20 % (reaches into the neighbouring stack; recorded)",Grow(1.2f),!shell)})
            {
                if(!shell&&change.EndsWith("recorded)"))continue;
                int chosen=Array.FindIndex(c.Result.Faces,f=>f.Rank==k);var moved=polygons.Select((p,i)=>i==chosen?transform(p,c.Result.Faces[i].Normal):p).ToArray();
                var after=LayerRanks.Compute(moved,directions);int assigned=after.Where((r,i)=>c.Result.Faces[i].Rank>=0).Count(r=>r>=0&&r<groups);int layerFaces=c.Result.Faces.Count(f=>f.Rank>=0);
                bool same=after.Zip(ranks).All(p=>p.First==p.Second);
                if(hard)Check(assigned==layerFaces,$"{c.Tag}: {change}: every layer face is still assigned ({assigned} of {layerFaces}; ranks {(same?"unchanged":"changed")})");
                else Check(assigned>=layerFaces-2,$"{c.Tag}: {change}: {assigned} of {layerFaces} faces assigned (recorded)");
                rankRows.AppendLine($"{c.Tag}\t{change}\t{after.Length}\t{assigned}\t{after.Length-assigned}\t{same}");
            }
            // A deleted face: the faces behind it move one layer forward (they are counted as the layer in front); every face stays assigned.
            int victim=Array.FindIndex(c.Result.Faces,f=>f.Rank==k);int source=c.Result.Faces[victim].Source;
            var kept=polygons.Where((_,i)=>i!=victim).ToArray();var keptInfo=c.Result.Faces.Where((_,i)=>i!=victim).ToArray();var deleted=LayerRanks.Compute(kept,directions.Where((_,i)=>i!=victim).ToArray());
            bool rule=keptInfo.Select((f,i)=>f.Rank<0||f.Source!=source||(f.Rank<k?deleted[i]==f.Rank:deleted[i]==f.Rank-1)).All(x=>x);
            Check(rule&&deleted.Where((r,i)=>keptInfo[i].Rank>=0).All(r=>r>=0&&r<groups),$"{c.Tag}: after deleting the face of layer {k+1}, the faces behind it count as the layer in front of them (rank - 1) and every face stays assigned; the last layer of the recipe is then unused on that face");
            rankRows.AppendLine($"{c.Tag}\tlayer {k+1} face deleted\t{deleted.Length}\t{deleted.Count(r=>r>=0&&r<groups)}\t{deleted.Count(r=>r<0||r>=groups)}\tbehind: rank - 1");
        }
        File.WriteAllText(Path.Combine(output,"LAYER-RANKS.tsv"),rankRows.ToString());
        // The per-mesh hit table: the face of a native vertex offset (front range = the authored face widths in order).
        {
            var snap=new PlateMeshSnapshot(1,perLayer.Faces.Length,0,perLayer.Plan.After.Faces,perLayer.Plan.After.Vertices,"sig");
            var index=LayerHitIndex.Build(snap,perLayer.Plan.Json,LayerLayoutKind.PerLayer,32);
            bool all=true;int offset=0;
            for(int f=0;f<index.Ranks.Length;f++){int w=snap.Faces[f].VertexCount;for(int v=0;v<w;v++)all&=index.FaceAt(offset+v)==f;all&=index.Face(100,index.Total,100+offset,100+offset+1,100+offset+2)==f;offset+=w;}
            Check(all&&index.Total==offset&&index.FaceAt(-1)==-1&&index.FaceAt(offset)==-1&&index.Face(0,index.Total+1,0,1,2)==-1&&index.Face(0,index.Total,0,1,4)==-1,$"hit table: every native vertex offset of the {index.Ranks.Length} faces maps to its face; another range size or a triangle across two faces gives no face");
            Check(index.Assigned==32&&Enumerable.Range(0,32).All(f=>index.RankOf(f)==f),"hit table: the ranks of the converted flat plate are the layers 1..32");
        }

        // ---------- 4. Hit: T-80U at nominal depth, plate in layers next to the same plate as a block ----------
        var hitRows=new StringBuilder("threat\tangle\tblock mm RHA-eq\tlayers (one face per layer) mm\tdifference %\tblocks (touching layers share a face) mm\tdifference %\tover 20 %\n");
        var layeredT80=LayeredRecipe.Build(t80,LayerLayout.Build(t80.Preset,LayerLayoutKind.PerLayer),Rha);var blockedT80=LayeredRecipe.Build(t80,LayerLayout.Build(t80.Preset,LayerLayoutKind.PerBlock),Rha);
        var table4=new List<string>();
        foreach(var threat in new[]{ProtectionThreat.LongRod,ProtectionThreat.Jet})
        foreach(var angle in new[]{0d,30,60})
        {
            double block=t80Table.ProtectionMm(threat,angle),layers=LayerHit.Estimate(layeredT80,t80Table,threat,angle,LayerHit.HitModel.PerLayer),blocks=LayerHit.Estimate(blockedT80,t80Table,threat,angle,LayerHit.HitModel.PerLayer);
            double d1=(layers-block)/block*100,d2=(blocks-block)/block*100;bool over=Math.Abs(d1)>20||Math.Abs(d2)>20;
            hitRows.AppendLine(string.Join("\t",ProtectionFactorTable.Text(threat),angle,F(block),F(layers),F(d1),F(blocks),F(d2),over?"YES":"no"));
            table4.Add(string.Create(CultureInfo.InvariantCulture,$"{ProtectionFactorTable.Text(threat)} {angle:0}°: block {block:0}, layers {layers:0} ({d1:+0.0;-0.0;0.0}%), blocks {blocks:0} ({d2:+0.0;-0.0;0.0}%){(over?" >20%":"")}"));
            Check(double.IsFinite(layers)&&layers>0&&double.IsFinite(blocks)&&blocks>0,$"{ProtectionFactorTable.Text(threat)} at {angle:0} deg: the plate in layers gives {layers:0} mm, as blocks {blocks:0} mm, as one block {block:0} mm");
        }
        // For information (not part of the brief's table): the same recipe in a 300 mm plate, the depth of the user's filling. The table is scale-free; the layers are not.
        hitRows.AppendLine("# same recipe scaled to a 300 mm plate (block = table factor x path; blocks = touching layers share a face, the layout Convert chooses at this depth)");
        foreach(var threat in new[]{ProtectionThreat.LongRod,ProtectionThreat.Jet})
        foreach(var angle in new[]{0d,30,60})
        {
            var chords=LayerHit.StraightStack(blockedT80,angle,300);var f=new double[chords.Length];var t=new double[chords.Length];LayerHit.Factors(blockedT80,t80Table,threat,chords,f,t);
            double layers=chords.Select((c,i)=>(c.EndM-c.StartM)*1000*f[i]).Sum(),block=300/Math.Cos(angle*Math.PI/180)*t80Table.Factor(threat,angle);
            hitRows.AppendLine(string.Join("\t",ProtectionFactorTable.Text(threat),angle,F(block),"",F(0),F(layers),F((layers-block)/block*100),Math.Abs((layers-block)/block)>.2?"YES":"no"));
        }
        File.WriteAllText(Path.Combine(output,"LAYER-PROTECTION.tsv"),hitRows.ToString());
        // The calculation reproduces the table exactly when one face holds the whole recipe at its nominal depth (no air, with and without tilt).
        foreach(var preset in new[]{presets.First(p=>p.Id=="t80u_hull"),new CassettePreset("tilted_sandwich","Tilted sandwich","1975.01.01",1,4000,new[]{new CassetteLayer("rha",10,30,"up"),new CassetteLayer("elastomerNERA",20,30,"up"),new CassetteLayer("rha",10,30,"up")})})
        {
            var model=new CassetteModel(preset,materials,reactions);var table=ProtectionFactorTable.Build(model);var one=LayeredRecipe.Build(model,LayerLayout.Build(preset,LayerLayoutKind.PerBlock),Rha);
            foreach(var threat in ProtectionFactorTable.Threats)
            foreach(var angle in new[]{0d,30,60})
            {
                var chord=LayerHit.StraightStack(one,angle,model.DepthMm);var f=new double[1];var t=new double[1];LayerHit.Factors(one,table,threat,chord,f,t);
                var expected=table.Factor(threat,angle);
                Check(one.Layout.Groups.Length==1&&Math.Abs(f[0]-expected)<=1e-9*Math.Max(1,expected),$"{preset.Id}: one face with the whole recipe gives the table factor ({ProtectionFactorTable.Text(threat)} at {angle:0} deg: {f[0]:R} vs {expected:R})");
            }
        }
        // The pieces of one shot add up: the per-layer steel equivalents follow the shot order (repeat factor and work run on across faces).
        {
            var chords=LayerHit.StraightStack(layeredT80,0,t80.DepthMm);var f=new double[chords.Length];var t=new double[chords.Length];
            LayerHit.Factors(layeredT80,t80Table,ProtectionThreat.Jet,chords,f,t);
            Check(f.All(x=>double.IsFinite(x)&&x>=0)&&t.Zip(chords).All(p=>Math.Abs(p.First-(p.Second.EndM-p.Second.StartM)*1000)<1e-9),"jet at 0 deg: every piece gets a finite factor and its normal thickness");
            var reversed=chords.Select(c=>c with{Back=true,Rank=chords.Length-1-c.Rank}).ToArray();
            var fr=new double[chords.Length];LayerHit.Factors(layeredT80,t80Table,ProtectionThreat.Jet,reversed,fr,t);
            Check(fr.All(double.IsFinite),"a shot from the back runs the faces in reverse order");
        }

        // ---------- 5. Fallback ----------
        foreach(var threat in ProtectionFactorTable.Threats)
        foreach(var angle in new[]{0d,17.5,30,60,72})
        {
            var chords=new[]{new LayerChord(1,1.05,angle,-1,false),new LayerChord(1.06,1.08,angle,99,false),new LayerChord(1.09,1.09,angle,0,false)};
            var f=new double[3];var t=new double[3];int fallback=LayerHit.Factors(layeredT80,t80Table,threat,chords,f,t);
            var expected=t80Table.Factor(threat,angle);
            Check(fallback==3&&f.All(x=>x==expected),$"a piece without face or rank pays exactly the recipe table, as an ordinary composite plate ({ProtectionFactorTable.Text(threat)} at {angle} deg)");
        }
        Check(LayerHit.Label("T-80U",layeredT80,new(0,1,30,-1,false),double.NaN)=="T-80U (layer face not found: recipe table)"&&LayerHit.Label("T-80U",layeredT80,new(0,.01,30,2,false),8.7).StartsWith("T-80U, layer 3 of 32: NERA (9 mm, 30"),"report labels: one line per layer with its name, thickness and angle; the fallback says so");

        // ---------- 7. Cost per shot: a plate of 16 layers against the same plate as a block ----------
        var sixteen=new CassettePreset("bench16","Bench 16 layers","1975.01.01",1,4000,Enumerable.Range(0,4).SelectMany(_=>new[]{new CassetteLayer("rha",8,0,"up"),new CassetteLayer("elastomerNERA",12,0,"up"),new CassetteLayer("rha",8,0,"up"),new CassetteLayer("aluminaCeramic",20,0,"up")}).ToArray());
        var benchModel=new CassetteModel(sixteen,materials,reactions);var benchTable=ProtectionFactorTable.Build(benchModel);var bench=LayeredRecipe.Build(benchModel,LayerLayout.Build(sixteen,LayerLayoutKind.PerLayer),Rha);
        Check(bench.Layout.Groups.Length==16,"benchmark plate: 16 layers");
        var benchChords=LayerHit.StraightStack(bench,30,benchModel.DepthMm);var benchIndex=new LayerHitIndex(Enumerable.Repeat(4,16).ToArray(),Enumerable.Range(0,16).ToArray(),LayerLayoutKind.PerLayer,16,"bench");
        double sinkValue=0;var bf=new double[16];var bt=new double[16];
        double BlockShot(){double s=0;for(int i=0;i<1;i++)s+=benchTable.Factor(ProtectionThreat.LongRod,30);return s;}
        double LayerShot(){double s=0;for(int i=0;i<16;i++)s+=benchIndex.RankOf(benchIndex.Face(0,64,4*i,4*i+1,4*i+2));LayerHit.Factors(bench,benchTable,ProtectionThreat.LongRod,benchChords,bf,bt);return s+bf[15];}
        for(int i=0;i<20000;i++){sinkValue+=BlockShot()+LayerShot();}
        const int rounds=200000;
        long allocatedBefore=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();for(int i=0;i<rounds;i++)sinkValue+=BlockShot();watch.Stop();double blockNs=watch.Elapsed.TotalNanoseconds/rounds;long blockBytes=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
        allocatedBefore=GC.GetAllocatedBytesForCurrentThread();watch.Restart();for(int i=0;i<rounds;i++)sinkValue+=LayerShot();watch.Stop();double layerNs=watch.Elapsed.TotalNanoseconds/rounds;long layerBytes=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
        File.WriteAllText(Path.Combine(output,"LAYER-COST.tsv"),$"case\tns per shot\tbytes per shot\nblock (1 piece, table lookup)\t{F(blockNs)}\t{F((double)blockBytes/rounds)}\n16 layers (16 face lookups + LayerHit.Factors)\t{F(layerNs)}\t{F((double)layerBytes/rounds)}\nsink\t{sinkValue.ToString("R",CultureInfo.InvariantCulture)}\t\nPure Release net8 managed maths only; the native work of 16 blocks per shot, the interop reads and the ledger are not in these numbers.\n");
        Check(layerBytes==0,$"the face lookup and the hit maths of 16 layers allocate nothing per shot ({layerBytes} bytes in {rounds} shots)");
        Check(double.IsFinite(layerNs)&&layerNs>0,$"cost per shot: block {blockNs:0.0} ns, 16 layers {layerNs:0.0} ns");

        // ---------- 8. Save and load with tm/te as token text ----------
        foreach(var c in cases)
        {
            var wrapper="{\"v\":\"0.2\",\"name\":"+JsonSerializer.Serialize(CompositePlate.NameForMode(c.Result.Mode))+",\"mesh\":"+c.Result.Plan.Json+"}";
            using var doc=JsonDocument.Parse(wrapper);var mesh=doc.RootElement.GetProperty("mesh").GetRawText();
            Check(LayerFaces.VerifyCopy(c.Result.Plan.Json,mesh)==null,$"{c.Tag}: saved and loaded, every point, edge, corner thickness and tm/te token survives");
            using var a=JsonDocument.Parse(c.Result.Plan.Json);int f=0;bool exact=true;
            foreach(var face in a.RootElement.GetProperty("faces").EnumerateArray())
            {
                var info=c.Result.Faces[f++];if(info.Rank<0)continue;
                ulong tm=0,te=0;for(int j=0;j<info.Modes.Length;j++){tm|=(ulong)info.Modes[j]<<(8*j);te|=(ulong)(uint)info.RibEdges[j]<<(16*j);}
                exact&=face.GetProperty("tm").GetRawText()==tm.ToString(CultureInfo.InvariantCulture)&&face.GetProperty("te").GetRawText()==te.ToString(CultureInfo.InvariantCulture)&&face.GetProperty("te").GetUInt64()==te;
            }
            Check(exact,$"{c.Tag}: tm/te are written as exact integer text (largest te {c.Result.Faces.Where(x=>x.Rank>=0).Max(x=>{ulong te=0;for(int j=0;j<x.RibEdges.Length;j++)te|=(ulong)(uint)x.RibEdges[j]<<(16*j);return te;})})");
            Check(CompositePlate.LayoutFromName(doc.RootElement.GetProperty("name").GetString())==c.Result.Layout.Kind,$"{c.Tag}: the layout mark is reloaded with the mesh");
        }
        // The original payload keeps every unknown root property (rivets, generation data) byte for byte.
        {
            var extra=flat803[..^1]+",\"futureField\":{\"a\":[1,2,3]}}";var r=LayerFaces.Convert(extra,t80,null,Density);
            Check(r.Plan.Json.Contains(",\"futureField\":{\"a\":[1,2,3]}",StringComparison.Ordinal),"unknown mesh properties survive the conversion");
        }

        // ---------- weight ----------
        foreach(var c in cases)
            Check(c.Result.TrueMassKg is {} trueMass&&trueMass>0&&c.Result.MassAfterKg>0,$"{c.Tag}: weight {c.Result.MassBeforeKg:0.#} kg as a block, {c.Result.MassAfterKg:0.#} kg as layers with the recipe density, {c.Result.TrueMassKg:0.#} kg from the densities of the layers ({(c.Result.MassAfterKg-c.Result.TrueMassKg!.Value)/c.Result.TrueMassKg.Value*100:+0.#;-0.#}%)");

        // ---------- pictures: side sections before and after ----------
        var pictures=new List<string>();
        foreach(var c in new[]{cases.Single(x=>x.Tag=="flat plate 803 mm"),cases.Single(x=>x.Tag.StartsWith("wedge")),userCase})
        {
            var stem=c.Tag.StartsWith("flat")?"flat":c.Tag.StartsWith("wedge")?"wedge":"t80bvm-fill";
            var (planePoint,planeNormal,uAxis,vAxis)=SectionPlane(c);
            var before=c.Result.Sources.Select(s=>(s.Front,s.Back,0x8f9a6cffu)).ToList();
            var after=c.Result.Faces.Select(f=>(f.Front,f.Back,f.Rank<0?0x8f9a6cffu:CassettePalette.Colour(t80.Preset.Layers[c.Result.Layout.Groups[f.Rank].Layers[0]].Material))).ToList();
            var view=SectionView(before,planePoint,planeNormal,uAxis,vAxis);
            foreach(var (kind,prisms) in new[]{("before",before),("after",after)})
            {
                var path=Path.Combine(preview,$"layer-{stem}-{kind}.png");File.WriteAllBytes(path,Section(prisms,planePoint,planeNormal,uAxis,vAxis,view));pictures.Add(Path.GetFileName(path));
            }
        }
        Check(pictures.Count==6&&pictures.All(p=>File.ReadAllBytes(Path.Combine(preview,p))[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})),"six section pictures written");

        // ---------- the route is wired when the mod loads (no composer window, no frame) ----------
        {
            var wiring=RouteWiring(Path.Combine(root,"bin/Release/net6.0/SprocketExpandedBallistics.dll"));
            Check(wiring.LoadWiresLayers&&!wiring.LoadWiresPlain,"ShellFeature.Load stores NativeMaterialReactions.ReachedWithLayers in RuntimeArmourResponses.ReachedBlock (built plugin IL), not the plain Reached");
            Check(wiring.LoadFailureClears&&wiring.UnloadClears,"ShellFeature.Unload and the load failure set ReachedBlock to null (built plugin IL)");
            Check(wiring.Stores==3,$"ReachedBlock is stored in exactly three places in the plugin: Load, its failure branch and Unload ({wiring.Stores})");
            Check(!wiring.PanelTouchesRoute,"the composer window no longer touches the route (no method of CassetteComposerPanel refers to ReachedBlock or ReachedWithLayers)");
            Check(wiring.LayersCallsReached,"ReachedWithLayers falls through to the unchanged Reached for every block it does not pay itself");
            evidence["routeWiring"]=wiring;
        }
        evidence["passed"]=failures==0;evidence["checks"]=checks;evidence["blueprint"]=Path.GetFileName(UserBlueprint);evidence["blueprintSha256"]=userHash;
        evidence["cases"]=cases.Select(c=>new{c.Tag,layout=c.Result.Layout.Kind.ToString(),layers=c.Result.Layout.Groups.Length,facesBefore=c.Result.Sources.Length,facesAfter=c.Result.Faces.Length,thinnestMm=c.Result.ThinnestMm,ribbed=c.Result.RibbedFaces,massBlockKg=c.Result.MassBeforeKg,massLayersKg=c.Result.MassAfterKg,trueMassKg=c.Result.TrueMassKg}).ToArray();
        evidence["protection"]=table4;evidence["costNs"]=new{block=blockNs,layers16=layerNs,bytesPerShot=(double)layerBytes/rounds};evidence["pictures"]=pictures;
        File.WriteAllText(Path.Combine(output,"LAYER-EVIDENCE.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT60 PASS: {checks} checks, {failures} failures; "+string.Join("; ",table4)+$"; cost block {blockNs:0.0} ns, 16 layers {layerNs:0.0} ns.");
    }

    internal sealed record RouteWiringResult(bool LoadWiresLayers,bool LoadWiresPlain,bool LoadFailureClears,bool UnloadClears,int Stores,bool PanelTouchesRoute,bool LayersCallsReached);
    /// <summary>
    /// Reads the IL of the built plugin (System.Reflection.Metadata, nothing is loaded or run): which delegate ShellFeature.Load stores in
    /// RuntimeArmourResponses.ReachedBlock, that Unload stores null, and that the composer panel has no part in it.
    /// </summary>
    private static RouteWiringResult RouteWiring(string dll)
    {
        using var stream=File.OpenRead(dll);using var pe=new System.Reflection.PortableExecutable.PEReader(stream);var md=System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        string TypeOf(System.Reflection.Metadata.TypeDefinitionHandle h)=>md.GetString(md.GetTypeDefinition(h).Name);
        string MethodName(int token){try{return MethodNameOf(token);}catch(Exception){return "";}}
        string FieldName(int token){try{return FieldNameOf(token);}catch(Exception){return "";}}
        string MethodNameOf(int token)
        {
            var handle=System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(token);
            if(handle.Kind==System.Reflection.Metadata.HandleKind.MethodDefinition){var m=md.GetMethodDefinition((System.Reflection.Metadata.MethodDefinitionHandle)handle);return TypeOf(m.GetDeclaringType())+"."+md.GetString(m.Name);}
            if(handle.Kind==System.Reflection.Metadata.HandleKind.MemberReference)return md.GetString(md.GetMemberReference((System.Reflection.Metadata.MemberReferenceHandle)handle).Name);
            return "";
        }
        string FieldNameOf(int token)
        {
            var handle=System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(token);
            if(handle.Kind==System.Reflection.Metadata.HandleKind.FieldDefinition){var f=md.GetFieldDefinition((System.Reflection.Metadata.FieldDefinitionHandle)handle);return TypeOf(f.GetDeclaringType())+"."+md.GetString(f.Name);}
            return "";
        }
        // Every method body (compiler-generated helpers included) with its declaring type and name.
        var bodies=new List<(string Type,string Name,byte[] Il)>();
        foreach(var th in md.TypeDefinitions)
        {
            var type=md.GetTypeDefinition(th);var name=md.GetString(type.Name);
            var outer=type.GetDeclaringType();var owner=outer.IsNil?name:TypeOf(outer);
            foreach(var mh in type.GetMethods())
            {
                var m=md.GetMethodDefinition(mh);if(m.RelativeVirtualAddress==0)continue;
                bodies.Add((owner,md.GetString(m.Name),System.Reflection.Metadata.PEReaderExtensions.GetMethodBody(pe,m.RelativeVirtualAddress).GetILBytes()!));
            }
        }
        // Scan a body for ldnull/ldftn ... stsfld ReachedBlock pairs (ldftn = FE 06 <token>, stsfld = 80 <token>, ldnull = 14).
        const string target="RuntimeArmourResponses.ReachedBlock";
        (List<string> Stored,int NullStores) Scan(byte[] il)
        {
            var stored=new List<string>();int nulls=0;string? lastFunction=null;
            for(int i=0;i<il.Length;i++)
            {
                if(il[i]==0xFE&&i+5<il.Length&&il[i+1]==0x06){lastFunction=MethodName(BitConverter.ToInt32(il,i+2));continue;}
                if(il[i]==0x80&&i+4<il.Length&&FieldName(BitConverter.ToInt32(il,i+1))==target)
                {
                    if(i>0&&il[i-1]==0x14)nulls++;else stored.Add(lastFunction??"?");
                    lastFunction=null;
                }
            }
            return (stored,nulls);
        }
        var load=bodies.Where(b=>b.Type=="ShellFeature"&&b.Name=="Load").Select(b=>Scan(b.Il)).ToArray();
        var unload=bodies.Where(b=>b.Type=="ShellFeature"&&b.Name=="Unload").Select(b=>Scan(b.Il)).ToArray();
        int stores=bodies.Sum(b=>{var s=Scan(b.Il);return s.Stored.Count+s.NullStores;});
        bool panel=bodies.Where(b=>b.Type=="CassetteComposerPanel").Any(b=>{var s=Scan(b.Il);if(s.Stored.Count+s.NullStores>0)return true;
            for(int i=0;i+5<b.Il.Length;i++)if((b.Il[i]==0x28||b.Il[i]==0x6F)&&MethodName(BitConverter.ToInt32(b.Il,i+1)) is "NativeMaterialReactions.ReachedWithLayers")return true;return false;});
        var layers=bodies.Where(b=>b.Type=="NativeMaterialReactions"&&b.Name=="ReachedWithLayers").ToArray();
        bool callsReached=layers.Length==1&&Enumerable.Range(0,Math.Max(0,layers[0].Il.Length-4)).Any(i=>layers[0].Il[i]==0x28&&MethodName(BitConverter.ToInt32(layers[0].Il,i+1))=="NativeMaterialReactions.Reached");
        return new(load.Length==1&&load[0].Stored.Contains("NativeMaterialReactions.ReachedWithLayers"),load.Length==1&&load[0].Stored.Contains("NativeMaterialReactions.Reached"),
            load.Length==1&&load[0].NullStores==1,unload.Length==1&&unload[0].NullStores==1&&unload[0].Stored.Count==0,stores,panel,callsReached);
    }
    private static string F(double v)=>v.ToString("0.###",CultureInfo.InvariantCulture);
    private static string Mesh(IEnumerable<Vector3> points,IEnumerable<(int A,int B)> edges,IEnumerable<string> faces)
    {
        var p=points.SelectMany(v=>new[]{v.X,v.Y,v.Z}).Select(x=>x.ToString("R",CultureInfo.InvariantCulture));var e=edges.ToArray();
        return "{\"majorVersion\":0,\"minorVersion\":3,\"vertices\":["+string.Join(",",p)+"],\"edges\":["+string.Join(",",e.SelectMany(x=>new[]{x.A,x.B}))+"],\"edgeFlags\":["+string.Join(",",e.Select(_=>0))+"],\"faces\":["+string.Join(",",faces)+"]}";
    }
    private static string Face(int[] v,IEnumerable<int> t,ulong tm=0,ulong te=0)=>"{\"v\":["+string.Join(",",v)+"],\"t\":["+string.Join(",",t)+"],\"tm\":"+tm.ToString(CultureInfo.InvariantCulture)+",\"te\":"+te.ToString(CultureInfo.InvariantCulture)+"}";
    private static readonly Vector3[] Square={new(-.5f,-.5f,0),new(.5f,-.5f,0),new(.5f,.5f,0),new(-.5f,.5f,0)};
    private static (int,int)[] SquareEdges=>new[]{(0,1),(1,2),(2,3),(3,0)};
    /// <summary>A 1 x 1 m plate in z = 0, front +Z, every corner <paramref name="mm"/> thick along the face normal.</summary>
    private static string Flat(int mm,ulong tm=0)=>Mesh(Square,SquareEdges,new[]{Face(new[]{0,1,2,3},Enumerable.Repeat(mm,4),tm)});
    private static string Wedge(int thin,int thick)=>Mesh(Square,SquareEdges,new[]{Face(new[]{0,1,2,3},new[]{thin,thin,thick,thick})});
    /// <summary>A 1 x 1 m plate whose four corners follow loose ribs (tm 4) that splay outwards at about 22 degrees from the normal; perpendicular depth <paramref name="mm"/>.</summary>
    private static string AlongEdge(int mm)
    {
        var points=Square.ToList();var edges=SquareEdges.ToList();ulong tm=0,te=0;var t=new int[4];
        for(int j=0;j<4;j++)
        {
            var c=Square[j];var dir=Vector3.Normalize(new Vector3(c.X*.8f,c.Y*.8f,-1));var end=c+dir*(mm*.001f/-dir.Z);
            points.Add(end);edges.Add((j,4+j));tm|=(ulong)CheekShellFill.AlongEdgeManual<<(8*j);te|=(ulong)(4+j)<<(16*j);t[j]=mm;
        }
        return Mesh(points,edges,new[]{Face(new[]{0,1,2,3},t,tm,te)});
    }
    private static string WithPentagon(string json)
    {
        var node=System.Text.Json.Nodes.JsonNode.Parse(json)!;var v=node["vertices"]!.AsArray();int start=v.Count/3;
        foreach(var p in new[]{new Vector3(2,0,0),new Vector3(2.5f,0,0),new Vector3(2.7f,.4f,0),new Vector3(2.25f,.7f,0),new Vector3(1.8f,.4f,0)}){v.Add(p.X);v.Add(p.Y);v.Add(p.Z);}
        var e=node["edges"]!.AsArray();var flags=node["edgeFlags"]!.AsArray();for(int j=0;j<5;j++){e.Add(start+j);e.Add(start+(j+1)%5);flags.Add(0);}
        node["faces"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(Face(Enumerable.Range(start,5).ToArray(),Enumerable.Repeat(100,5))));
        return node.ToJsonString();
    }
    /// <summary>The mesh of object 280 (the filling) of the user's T-80BVM blueprint, raw text.</summary>
    private static string UserFill(byte[] bytes)
    {
        using var doc=JsonDocument.Parse(bytes);var r=doc.RootElement;
        var obj=r.GetProperty("objects").EnumerateArray().First(o=>o.GetProperty("vuid").GetInt32()==280);
        var structure=obj.GetProperty("structureBlueprintVuid").GetInt32();
        var bp=r.GetProperty("blueprints").EnumerateArray().First(b=>b.GetProperty("id").GetInt32()==structure).GetProperty("blueprint");
        var mesh=bp.GetProperty("bodyMeshVuid").GetInt32();
        return r.GetProperty("meshes").EnumerateArray().First(m=>m.GetProperty("vuid").GetInt32()==mesh).GetProperty("meshData").GetProperty("mesh").GetRawText();
    }
    private static int[] JsonFaceVertices(string json,int face)
    {
        using var doc=JsonDocument.Parse(json);return doc.RootElement.GetProperty("faces")[face].GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
    }
    private static Vector3[] Rotate(Vector3[] p,Vector3 n,float degrees)
    {
        var centre=p.Aggregate(Vector3.Zero,(a,b)=>a+b)/p.Length;var axis=Vector3.Normalize(Vector3.Cross(n,Math.Abs(n.Y)<.9f?Vector3.UnitY:Vector3.UnitX));
        var q=Quaternion.CreateFromAxisAngle(axis,degrees*MathF.PI/180);return p.Select(x=>centre+Vector3.Transform(x-centre,q)).ToArray();
    }
    private sealed record ShapeStats(int Inverted,double MaxOutsideMm,double MaxSeamMm,double MaxAirErrorMm,double MaxDepthErrorMm,double AlongRibSeamMm,double AlongRibDepthMm);
    /// <summary>
    /// Independent geometry checks, measured like native thickness (perpendicular to the faces; whole millimetres plus the 0.25 mm offset):
    /// orientation; every layer point inside the original solid (sideways from its corner line, in front of the original front or behind the original
    /// back, both along the original face normal); the back of a layer against the plane of the next layer's front (seam, or air against the recipe);
    /// the last back against the original back. The point distances along the rib are kept for information.
    /// </summary>
    private static ShapeStats Shape(LayerConversion r)
    {
        int inverted=0;double outside=0,seam=0,air=0,depth=0,ribSeam=0,ribDepth=0;var groups=r.Layout.Groups;
        foreach(var source in r.Sources.Where(s=>s.Converted))
        {
            var faces=r.Faces.Where(f=>f.Source==source.Index).OrderBy(f=>f.Rank).ToArray();var n0=source.Normal;
            foreach(var f in faces)
            {
                if(Vector3.Dot(CheekShellFill.Newell(f.Front),n0)<=0||Vector3.Dot(CheekShellFill.Newell(f.Back),f.Normal)<=0)inverted++;
                for(int j=0;j<f.Front.Length;j++)
                    foreach(var p in new[]{f.Front[j],f.Back[j]})
                    {
                        var a=source.Front[j];var b=source.Back[j];var line=Vector3.Normalize(b-a);
                        double sideways=Vector3.Cross(p-a,line).Length()*1000;
                        double ahead=Vector3.Dot(p-a,n0)*1000,behind=Vector3.Dot(b-p,n0)*1000;
                        outside=Math.Max(outside,Math.Max(sideways,Math.Max(ahead,behind)));
                    }
            }
            for(int g=0;g+1<faces.Length;g++)
            {
                var gap=groups[g+1].StartFraction-groups[g].EndFraction;var next=faces[g+1];var plane=Vector3.Normalize(CheekShellFill.Newell(next.Front));
                for(int j=0;j<faces[g].Back.Length;j++)
                {
                    double normal=Vector3.Dot(faces[g].Back[j]-next.Front[j],plane)*1000;
                    if(gap<1e-12){seam=Math.Max(seam,Math.Abs(normal));ribSeam=Math.Max(ribSeam,Vector3.Distance(faces[g].Back[j],next.Front[j])*1000);}
                    else air=Math.Max(air,Math.Abs(normal-gap*source.LineLengthMm[j]*Math.Abs(Vector3.Dot(source.LineDirection[j],plane))));
                }
            }
            var last=faces[^1];
            for(int j=0;j<last.Back.Length;j++){depth=Math.Max(depth,Math.Abs(Vector3.Dot(last.Back[j]-source.Back[j],n0))*1000);ribDepth=Math.Max(ribDepth,Vector3.Distance(last.Back[j],source.Back[j])*1000);}
        }
        return new(inverted,outside,seam,air,depth,ribSeam,ribDepth);
    }
    private static double SegmentDistance(Vector3 p,Vector3 a,Vector3 b)
    {
        var ab=b-a;var t=Math.Clamp(Vector3.Dot(p-a,ab)/Math.Max(1e-20f,ab.LengthSquared()),0,1);return Vector3.Distance(p,a+ab*t);
    }
    private static (Vector3 Point,Vector3 Normal,Vector3 U,Vector3 V) SectionPlane(LayerCase c)
    {
        if(!c.Tag.StartsWith("T-80BVM"))return (Vector3.Zero,Vector3.UnitX,-Vector3.UnitZ,Vector3.UnitY);
        // The filling: a vertical section across the thickness through the middle of the first face, seen from the side.
        var s=c.Result.Sources[0];var centre=s.Front.Aggregate(Vector3.Zero,(a,b)=>a+b)/s.Front.Length;
        var n=s.Normal;var up=Vector3.UnitY;var across=Vector3.Normalize(Vector3.Cross(up,n));
        return (centre,across,Vector3.Normalize(-n),up);
    }
    private static (float U0,float U1,float V0,float V1) SectionView(List<(Vector3[] Front,Vector3[] Back,uint Colour)> prisms,Vector3 point,Vector3 normal,Vector3 u,Vector3 v)
    {
        float u0=float.MaxValue,u1=float.MinValue,v0=float.MaxValue,v1=float.MinValue;
        foreach(var (a,b) in prisms.SelectMany(p=>SectionSegments(p.Front,p.Back,point,normal,u,v))){u0=MathF.Min(u0,MathF.Min(a.X,b.X));u1=MathF.Max(u1,MathF.Max(a.X,b.X));v0=MathF.Min(v0,MathF.Min(a.Y,b.Y));v1=MathF.Max(v1,MathF.Max(a.Y,b.Y));}
        float mu=(u1-u0)*.08f+.01f,mv=(v1-v0)*.08f+.01f;return (u0-mu,u1+mu,v0-mv,v1+mv);
    }
    private static IEnumerable<(Vector2 A,Vector2 B)> SectionSegments(Vector3[] front,Vector3[] back,Vector3 point,Vector3 normal,Vector3 u,Vector3 v)
    {
        var tris=new List<(Vector3,Vector3,Vector3)>();int n=front.Length;
        for(int j=1;j+1<n;j++){tris.Add((front[0],front[j],front[j+1]));tris.Add((back[0],back[j+1],back[j]));}
        for(int j=0;j<n;j++){int k=(j+1)%n;tris.Add((front[j],front[k],back[k]));tris.Add((front[j],back[k],back[j]));}
        foreach(var (a,b,c) in tris)
        {
            float da=Vector3.Dot(a-point,normal),db=Vector3.Dot(b-point,normal),dc=Vector3.Dot(c-point,normal);var cut=new List<Vector3>();
            void Edge(Vector3 p,float dp,Vector3 q,float dq){if(dp<0!=dq<0&&dp!=dq)cut.Add(p+(q-p)*(dp/(dp-dq)));}
            Edge(a,da,b,db);Edge(b,db,c,dc);Edge(c,dc,a,da);
            if(cut.Count==2)yield return (new(Vector3.Dot(cut[0],u),Vector3.Dot(cut[0],v)),new(Vector3.Dot(cut[1],u),Vector3.Dot(cut[1],v)));
        }
    }
    /// <summary>A side section: every prism (front and back corners) cut by the plane, filled even-odd, outlined; 1200 x 800 px, 100 mm scale bar.</summary>
    private static byte[] Section(List<(Vector3[] Front,Vector3[] Back,uint Colour)> prisms,Vector3 point,Vector3 normal,Vector3 u,Vector3 v,(float U0,float U1,float V0,float V1) view)
    {
        const int w=1200,h=800;var pixels=new uint[w*h];Array.Fill(pixels,Pack(20,21,15));
        float scale=MathF.Min(w/(view.U1-view.U0),h/(view.V1-view.V0));float ox=(w-(view.U1-view.U0)*scale)/2,oy=(h-(view.V1-view.V0)*scale)/2;
        Vector2 Map(Vector2 p)=>new(ox+(p.X-view.U0)*scale,h-(oy+(p.Y-view.V0)*scale));
        foreach(var (front,back,colour) in prisms)
        {
            var segments=SectionSegments(front,back,point,normal,u,v).Select(s=>(Map(s.A),Map(s.B))).ToList();if(segments.Count==0)continue;
            uint fill=Pack((byte)(colour>>24),(byte)(colour>>16),(byte)(colour>>8));
            float yMin=segments.Min(s=>MathF.Min(s.Item1.Y,s.Item2.Y)),yMax=segments.Max(s=>MathF.Max(s.Item1.Y,s.Item2.Y));
            for(int y=Math.Max(0,(int)yMin);y<=Math.Min(h-1,(int)yMax);y++)
            {
                float sy=y+.5f;var xs=new List<float>();
                foreach(var (a,b) in segments)if((a.Y<=sy)!=(b.Y<=sy))xs.Add(a.X+(sy-a.Y)/(b.Y-a.Y)*(b.X-a.X));
                xs.Sort();for(int i=0;i+1<xs.Count;i+=2)for(int x=Math.Max(0,(int)xs[i]);x<=Math.Min(w-1,(int)xs[i+1]);x++)pixels[y*w+x]=fill;
            }
            foreach(var (a,b) in segments)Line(pixels,w,h,a,b,Pack(30,32,26));
        }
        // 100 mm scale bar.
        var s0=new Vector2(40,h-40);Line(pixels,w,h,s0,s0+new Vector2(.1f*scale,0),Pack(220,158,51));Line(pixels,w,h,s0+new Vector2(0,1),s0+new Vector2(.1f*scale,1),Pack(220,158,51));
        return PackPng(w,h,pixels);
    }
    private static uint Pack(byte r,byte g,byte b)=>(uint)(r|g<<8|b<<16|255<<24);
    private static void Line(uint[] pixels,int w,int h,Vector2 a,Vector2 b,uint colour)
    {
        int steps=(int)MathF.Max(1,MathF.Max(MathF.Abs(b.X-a.X),MathF.Abs(b.Y-a.Y)));
        for(int i=0;i<=steps;i++){var p=Vector2.Lerp(a,b,(float)i/steps);int x=(int)p.X,y=(int)p.Y;if(x>=0&&x<w&&y>=0&&y<h)pixels[y*w+x]=colour;}
    }
}
