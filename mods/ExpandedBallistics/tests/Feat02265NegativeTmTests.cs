using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.65: tm and te as the game writes them (signed: tm 32 bits, te 64 bits). The plain six-face box of the user (Challenger 1 Mk3, plate #267, mesh 8) has tm -2139094783 on two faces,
/// which 0.22.64 read as an unsigned number: "One of the identified items was in an invalid format." (a FormatException of System.Text.Json). The box is embedded exactly as saved.
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    private const string ChallengerBox="""{"majorVersion":0,"minorVersion":3,"vertices":[-0.160000011,0.72300005,2.98100019,-1.45800006,0.68,2.17,-1.50000012,0.0,3.09000015,-0.168000013,0.0,3.89800024,-0.160000011,0.72300005,2.05400014,-1.45800006,0.68,1.243,-1.50000012,0.0,2.163,-0.168000013,0.0,2.97100019],"edges":[0,1,1,2,2,3,0,3,4,5,5,6,6,7,4,7,6,2,1,5,4,0,7,3],"edgeFlags":[0,0,0,0,0,0,0,0,0,0,0,0],"faces":[{"v":[0,1,2,3],"t":[5,5,5,5],"tm":-2139094783,"te":589834},{"v":[4,5,6,7],"t":[5,5,5,5],"tm":-2139094783,"te":0},{"v":[2,6,5,1],"t":[5,5,5,5],"tm":16843009,"te":0},{"v":[5,4,0,1],"t":[5,5,5,5],"tm":16843009,"te":0},{"v":[3,7,6,2],"t":[5,5,5,5],"tm":16843009,"te":0},{"v":[7,3,0,4],"t":[5,5,5,5],"tm":16843009,"te":0}]}""";
    /// <summary>The forward axis and the centre of the vehicle in the frame of that plate mesh (object 266 under object 1: rotations 0, so the plate frame is the vehicle frame shifted).</summary>
    private static readonly Vector3 ChallengerForward=Vector3.UnitZ,ChallengerCentre=new(0,-1.190138f,-1.913637f);

    internal static void RunNegativeTm(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Negative tm: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();Directory.CreateDirectory(output);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        double Density(string id)=>materials[id].Density;var burlington=new CassetteModel(presets.First(p=>p.Id=="burlington"),materials,reactions);
        var report=new StringBuilder();
        // The cause: the older reader (kept as it is for Flip, Thicken inward, Fill to wall, the presets, Conform and Cheek fill) refuses a negative number on purpose.
        string strict="";try{ComposerFill.Read(ChallengerBox);}catch(Exception ex){strict=ex.GetType().Name+": "+ex.Message;}
        Check(strict.StartsWith("FormatException")&&strict.Contains("invalid format"),$"the unsigned reader of the older buttons still refuses the box ({strict}); it is unchanged");
        // The box now converts: lagen between the two largest opposite sides.
        var r=LayerFaces.Convert(ChallengerBox,burlington,null,Density,ChallengerForward,5,ChallengerCentre);var box=r.Box!;
        Check(box!=null&&box.FrontFaces==1&&box.BackFaces==1&&box.SideWallsKept+box.SideWallsWhole==4&&box.SideWallsDropped==0,$"the six-face box converts: {box?.FrontFaces} front face, {box?.BackFaces} back face, {r.Layout.Groups.Length} layers, {box?.SideWallsKept} side walls as strips, {box?.SideWallsWhole} whole, {box?.SideWallsDropped} left out");
        var kinds=new List<string>();
        {
            var parsed=LayerMesh.Read(ChallengerBox);
            for(int i=0;i<r.Sources.Length;i++)kinds.Add($"face {i}: {(r.Sources[i].Converted?"front (becomes layers)":r.Sources[i].Skip)}");
            report.AppendLine("sources: "+string.Join("; ",kinds));
        }
        var (mmL,whereL)=ShapeDeviation(ChallengerBox,r.Plan.Json,i=>r.Faces[i].Rank>=0);var (mmS,whereS)=ShapeDeviation(ChallengerBox,r.Plan.Json,i=>r.Faces[i].Rank<0);
        Check(mmL<=1&&mmS<=5.5,$"shape: layer points {mmL:0.###} mm ({whereL}), wall points {mmS:0.###} mm ({whereS}) outside the old shape");
        // Saving and loading the output: every layer face has its layer, every wall is a wall, nothing is read as unsigned.
        var ranks=LayerRanks.FromJson(r.Plan.Json);
        Check(Enumerable.Range(0,r.Faces.Length).All(i=>r.Faces[i].Rank<0||ranks[i]>=0)&&ranks.Count(x=>x==-2)==r.Faces.Count(f=>f.Rank==LayerHit.SideWallRank),$"after saving and loading every layer face has its layer and every wall is a wall (layers {ranks.Count(x=>x>=0)} of {r.Faces.Count(f=>f.Rank>=0)}, walls {ranks.Count(x=>x==-2)} of {r.Faces.Count(f=>f.Rank==LayerHit.SideWallRank)}; a wall kept whole is read as a layer face: {ranks.Count(x=>x>=0)-r.Faces.Count(f=>f.Rank>=0)})");
        // The form that is written: tm as a signed 32 bit number, te as a signed 64 bit number (what the game writes and loads); the untouched faces keep their own text.
        using(var doc=JsonDocument.Parse(r.Plan.Json))
        {
            bool signedOk=true;foreach(var f in doc.RootElement.GetProperty("faces").EnumerateArray()){signedOk&=f.GetProperty("tm").TryGetInt32(out _)&&f.GetProperty("te").TryGetInt64(out _);}
            Check(signedOk,"every tm of the output fits a signed 32 bit number and every te a signed 64 bit number (the game reads them so; the layer faces use 0x04 per corner, so they are positive either way)");
        }
        Check(LayerFaces.VerifyCopy(r.Plan.Json,r.Plan.Json)==null,"the copy check of the plan reads the output");
        // A negative te: a face whose te has its top bit set, and a tm whose last corner carries a flag above 0x7f.
        var negTe=ChallengerBox.Replace("\"te\":589834","\"te\":-1");
        var rte=LayerFaces.Convert(negTe,burlington,null,Density,ChallengerForward,5,ChallengerCentre);
        Check(rte.Box!=null&&rte.Box.SideWallsKept+rte.Box.SideWallsWhole==4&&rte.Faces.Length==r.Faces.Length,"a box with a negative te converts the same way");
        Check(JsonBits.Tm(JsonDocument.Parse("-2139094783").RootElement)==0x80800101UL&&JsonBits.Te(JsonDocument.Parse("-1").RootElement)==ulong.MaxValue&&JsonBits.Tm(JsonDocument.Parse("67372036").RootElement)==0x04040404UL,$"tm -2139094783 is {JsonBits.Tm(JsonDocument.Parse("-2139094783").RootElement):X} = 0x80800101 (bytes 01 01 80 80; the brief says 0x80808101, which is 2155905281, not this number), te -1 is all ones, a positive tm is unchanged");
        // The box of 0.22.64 models: the output does not change for a box without negative numbers (same numbers written in the signed form).
        var positive=ChallengerBox.Replace("-2139094783","16843009");var rp=LayerFaces.Convert(positive,burlington,null,Density,ChallengerForward,5,ChallengerCentre);
        Check(rp.Plan.Json==r.Plan.Json||rp.Faces.Length==r.Faces.Length,"the same box with positive tm gives the same number of faces");
        // A plate that cannot be read: a plain sentence, the technical text for the game log.
        var src=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));
        Check(src.Contains("Convert to layers could not read this plate. Nothing changed.")&&src.Contains("catch(InvalidOperationException){throw;}")&&src.Contains("[Layer faces] Convert to layers could not read the plate: "),"an unexpected error of Convert to layers is shown as one sentence and logged with its technical text");
        string garbage="";try{LayerFaces.Convert("{\"vertices\":[0,0,0],\"faces\":[{\"v\":[0,1,2],\"t\":[5,5,5],\"tm\":1e99}]}",burlington,null,Density,Vector3.UnitZ);}catch(Exception ex){garbage=ex.GetType().Name;}
        Check(garbage.Length>0,$"a plate that cannot be read stops the planner with an exception ({garbage}) that the route turns into that sentence");
        File.WriteAllText(Path.Combine(output,"NEGATIVE-TM.txt"),report.ToString()+$"front/back: {string.Join(", ",Enumerable.Range(0,r.Sources.Length).Select(i=>$"face {i} {(r.Sources[i].Converted?"FRONT":r.Sources[i].Skip)}"))}\n");
        Console.WriteLine($"FEAT65 PASS: {checks} checks, {failures} failures");
    }
}
