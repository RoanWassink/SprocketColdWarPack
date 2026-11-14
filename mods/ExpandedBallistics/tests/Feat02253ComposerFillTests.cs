using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

internal static class Feat02253ComposerFillTests
{
    internal static readonly string[] Changed={"CheekShellFill.cs","ConformCheekFill.cs","ComposerFill.cs","RuntimeComposerFill.cs","RuntimeCompositePlateEdit.cs","CompositePlate.cs","CassetteComposerModel.cs","CassetteComposerPanel.cs","RuntimeCompositePlate.cs","RuntimeCassettePlateVisual.cs","LayeredPlateReport.cs","CombatForensicDrawing.cs","CombatForensicGeometry.cs","CassetteSectionDrawing.cs","RuntimeGroupedProtection.cs","CombatForensics.cs","Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md"};
    internal static void Run(Action<bool,string> sink)
    {
        int count=0;void Check(bool ok,string message){count++;sink(ok,"Composer fill: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();
        var rows=new StringBuilder("fixture\tcorner\texpected_mm\tactual_mm\tmisses\n");
        var settings=new FillSettings();
        string source="{\n\"majorVersion\":0,\"minorVersion\":3,\"vertices\":[0,0,0,0,1,0,1,1,0,1,0,0],\"edges\":[0,1,1,2,2,3,3,0],\"edgeFlags\":[0,0,0,0],\"faces\":[{\"v\": [0, 1, 2, 3],\"t\": [20, 30, 40, 50],\"tm\":16777217,\"te\":2533274790395909,\"kept\":\"opaque face data\"}],\"generation\":{\"kept\":true},\"rivets\":[13]}";
        // Native save packing (T-80BVM backup, 851 faces): tm one byte per corner (0 = Along face normal, 1 = Along edge),
        // te 16 bits per corner (edge index). This face: corners 0 and 3 follow edges 5 and 9.
        var before=ComposerFill.Read(source);var turned=ComposerFill.Plan(source,before,null,ComposerMeshAction.Flip,Vector3.Zero,Array.Empty<FillTriangle>(),settings);var reference=new Vector3(.5f,.5f,-.5f);
        // This winding starts at -Z: native -normal points out, so Inward must flip it.
        Check(before.Faces[0].Normal.Z<-.99,"fixture starts thickening outward");
        foreach(string fixture in new[]{"wedge","curved","hatch"})
        {
            float Depth(float x)=>fixture=="curved"?.3f+.08f*x*x:.2f+.1f*x;
            var walls=new List<FillTriangle>();
            for(int i=0;i<10;i++)
            {
                if(fixture=="hatch"&&i>=8)continue;
                float x=i*.1f,x2=(i+1)*.1f;
                AddQuad(walls,new(x,-1,-Depth(x)),new(x2,-1,-Depth(x2)),new(x2,2,-Depth(x2)),new(x,2,-Depth(x)));
            }
            var inward=ComposerFill.Plan(source,before,null,ComposerMeshAction.Inward,reference,walls,settings);
            Check(inward.Flipped==1&&inward.After.Faces[0].Normal.Z>.99,"correct inward direction / "+fixture);
            Check(inward.After.Faces[0].Thickness.SequenceEqual(before.Faces[0].Thickness.Reverse()),"inward retains thickness by vertex / "+fixture);
            var fill=ComposerFill.Plan(turned.Json,turned.After,null,ComposerMeshAction.Fill,reference,walls,settings);
            var f=fill.After.Faces[0];
            for(int j=0;j<f.VertexCount;j++)
            {
                double expected=fixture=="hatch"&&f.Corners[j].X>.8?50:Depth(f.Corners[j].X)*1000-10;
                Check(Math.Abs(f.Thickness[j]-expected)<=1,"within 1 mm / "+fixture+" / "+j);
                rows.AppendLine(FormattableString.Invariant($"{fixture}\t{j}\t{expected:R}\t{f.Thickness[j]}\t{fill.Misses}"));
            }
            Check(fill.Misses==(fixture=="hatch"?2:0),"hatch fallback is local to missed corners / "+fixture);
            Check(fill.Summary.Contains("without a wall")&&fill.Summary.Contains("clearance 10 mm"),"fallback and clearance reported / "+fixture);
            var once=ComposerFill.Plan(source,before,null,ComposerMeshAction.Flip,reference,walls,settings);
            var twice=ComposerFill.Plan(once.Json,once.After,null,ComposerMeshAction.Flip,reference,walls,settings);
            Check(Encoding.UTF8.GetBytes(twice.Json).SequenceEqual(Encoding.UTF8.GetBytes(source)),"two flips bit-exact including tm/te and metadata / "+fixture);
            // Same immutable old/new values consumed by the single native CreateOp route.
            foreach(var p in new[]{once,inward,fill})
            {
                var undo=new CompositeUndoSnapshot("ebCassette_test","ebCassette_test",true,before.ThicknessList().ToArray(),new("ebCassette_test",p.After.ThicknessList().ToArray()));
                Check(undo.After(false).Thicknesses[0].SequenceEqual(before.Faces[0].Thickness)&&undo.After(false).Material=="ebCassette_test","one Revert restores all corners/material / "+fixture);
                Check(undo.After(true).Thicknesses[0].SequenceEqual(p.After.Faces[0].Thickness),"one Execute/redo sets every corner / "+fixture);
            }
        }
        var ambiguityWalls=new List<FillTriangle>();AddQuad(ambiguityWalls,new(-2,-2,-.3f),new(2,-2,-.3f),new(2,2,-.3f),new(-2,2,-.3f));
        Check(ComposerFill.InwardFlip(before.Faces[0],new(2,.5f,0),ambiguityWalls),"near-perpendicular reference uses the one-metre ray");
        Check(!ComposerFill.InwardFlip(before.Faces[0],new(2,.5f,.01f),Array.Empty<FillTriangle>()),"opening ambiguity falls back to the reference point");
        var capWalls=new List<FillTriangle>();AddQuad(capWalls,new(-2,-2,-2),new(2,-2,-2),new(2,2,-2),new(-2,2,-2));
        var capped=ComposerFill.Plan(turned.Json,turned.After,null,ComposerMeshAction.Fill,reference,capWalls,settings);
        Check(capped.Limited==4&&capped.Misses==0&&capped.After.Faces[0].Thickness.All(t=>t==500),"far wall caps at 500 mm instead of becoming a miss");
        var custom=ComposerFill.Plan(turned.Json,turned.After,null,ComposerMeshAction.Fill,reference,ambiguityWalls,new(15,400,25));
        Check(custom.After.Faces[0].Thickness.All(t=>t==285),"configurable clearance");
        Check(ComposerFill.Ray(Vector3.Zero,Vector3.UnitX,ambiguityWalls,1)==null,"parallel rays miss");
        Check(ComposerFill.Ray(new(10,0,0),-Vector3.UnitZ,ambiguityWalls,1)==null,"outside-triangle rays miss");
        Check(before.Faces[0].Along.SequenceEqual(new[]{false,true,true,false}),"packed tm decodes one byte per corner");
        {
            var one=ComposerFill.Plan(source,before,null,ComposerMeshAction.Flip,reference,Array.Empty<FillTriangle>(),settings);
            using var o=JsonDocument.Parse(one.Json);var face=o.RootElement.GetProperty("faces")[0];
            Check(face.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(new[]{3,2,1,0}),"flip reverses the corner order");
            Check(face.GetProperty("t").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(new[]{50,40,30,20}),"flip: thickness follows its corner");
            Check(face.GetProperty("tm").GetUInt64()==16777217UL&&one.After.Faces[0].Along.SequenceEqual(new[]{false,true,true,false}),"flip: packed thicken mode follows its corner (symmetric case)");
            Check(face.GetProperty("te").GetUInt64()==(9UL|5UL<<48),"flip: packed edge index follows its corner (5 and 9 swap ends)");
            Check(one.EdgeCorners==2&&one.Summary.Contains("2 corners follow an edge"),"flip reports corners whose direction follows an edge");
            Check(ComposerFill.ReversePacked("65793",4,8,0)=="16843008"&&ComposerFill.ReversePacked("16843008",4,8,0)=="65793","packed mixed modes reverse per byte (0x00010101 <-> 0x01010100)");
            Check(ComposerFill.ReversePacked("0",7,16,0)=="0","zero packing is valid on any face size");
            bool Refused(Func<string> f){try{f();return false;}catch(InvalidOperationException){return true;}}
            Check(Refused(()=>ComposerFill.ReversePacked("1",5,8,0)),"packed data on a 5-corner face is refused (no proven layout)");
            Check(Refused(()=>ComposerFill.ReversePacked("16777216",3,8,0)),"stray bits beyond the corners are refused");
            Check(Refused(()=>ComposerFill.ReversePacked("-1",4,16,0))&&Refused(()=>ComposerFill.ReversePacked("01",4,8,0)),"signed or non-canonical packing is refused");
            var tightWalls=new List<FillTriangle>();AddQuad(tightWalls,new(-2,-2,-.012f),new(2,-2,-.012f),new(2,2,-.012f),new(-2,2,-.012f));
            var tight=ComposerFill.Plan(turned.Json,turned.After,null,ComposerMeshAction.Fill,reference,tightWalls,settings);
            Check(tight.Tight==4&&tight.After.Faces[0].Thickness.All(t=>t==3)&&tight.Summary.Contains("4 kept at 3 mm"),"a wall within clearance keeps the native 3 mm minimum and is reported");
            Check(!tight.NewTopology&&ComposerFill.Cheeks(Box(new(-1.3f,0,-1),new(1.3f,.8f,1.4f)),new("turret",new(-1.3f,0,-1),new(1.3f,.8f,1.4f)),settings).NewTopology,"only the cheek preset replaces the topology");
        }
        string arrays=source.Replace("\"tm\":16777217,\"te\":2533274790395909","\"tm\":[0,1,2,3],\"te\":[4,5,6,7]");
        Check(arrays!=source,"array fixture differs from the packed one");
        var a=ComposerFill.Plan(arrays,ComposerFill.Read(arrays),null,ComposerMeshAction.Flip,reference,ambiguityWalls,settings);
        Check(ComposerFill.Plan(a.Json,a.After,null,ComposerMeshAction.Flip,reference,ambiguityWalls,settings).Json==arrays,"per-corner tm/te also double-flip exactly");
        var both=JsonNode.Parse(source)!;both["faces"]!.AsArray().Add(both["faces"]![0]!.DeepClone());
        var selective=ComposerFill.Plan(both.ToJsonString(),ComposerFill.Read(both.ToJsonString()),new[]{true,false},ComposerMeshAction.Fill,reference,ambiguityWalls,settings);
        Check(selective.After.Faces[1].Thickness.SequenceEqual(before.Faces[0].Thickness)&&selective.After.Faces[1].Along.SequenceEqual(before.Faces[0].Along)&&selective.After.Faces[0].Along.All(x=>x),"unselected face thickness and direction unchanged; treated face Along face normal");
        var unit=ComposerFill.Read(source.Replace("[20, 30, 40, 50]","[200, 200, 200, 200]"));
        var unitPlan=new ComposerMeshPlan(source,unit,0,0,0,"");
        var inspection=unitPlan.Inspect(4000,new[]{new FillBounds("simulated crew",new(.2f,.2f,0),new(.8f,.8f,.2f))});
        Check(Math.Abs(inspection.VolumeM3-.2)<1e-7&&Math.Abs(inspection.MassKg-800)<1e-4,"hand calculation: 1 m2 * 0.2 m = 0.2 m3, * 4000 = 800 kg");
        Check(inspection.Warnings.Single().Contains("simulated crew"),"simulated crew bound gives warning");
        Check(unitPlan.Inspect(4000,new[]{new FillBounds("far cannon",new(5,5,5),new(6,6,6))}).Warnings.Length==0,"disjoint component has no warning");
        var box=Box(new(-1.3f,0,-1),new(1.3f,.8f,1.4f));
        var cheeks=ComposerFill.Cheeks(box,new("turret",new(-1.3f,0,-1),new(1.3f,.8f,1.4f)),settings);
        Check(cheeks.After.FaceCount==320,"preset builds two cheeks on a size-derived 100 mm grid");
        Check(cheeks.After.Vertices.All(p=>Math.Abs(p.X)>=.35f-1e-6&&p.Y>=.03f-1e-6&&p.Z>1),"preset keeps gun corridor and crew/rear region clear");
        Check(cheeks.After.Faces.All(f=>f.Thickness.All(t=>t<=450)),"preset capped at 450 mm within the front cavity");
        Check(cheeks.Inspect(4000,Array.Empty<FillBounds>()).VolumeM3<.7,"cheek preset is a bounded insert");
        var model=typeof(CassetteComposerModel); // mode contract lives in the production model
        Check(CassetteComposerModel.ThicknessModeLabels.SequenceEqual(new[]{"Uniform","To back plane"})&&CassetteComposerModel.ApplyChoiceLabels.Contains("Fill to wall"),"0.22.71: the thickness mode row holds Uniform and To back plane; Fill to wall is one of the choices of Apply");
        Check(CompositePlate.ModeFromName(CompositePlate.NameForMode(ThicknessMode.FillToWall))==ThicknessMode.FillToWall&&CompositePlate.IsName(CompositePlate.FillName),"Fill mode identity survives mesh name payload");
        Check(new FillSettings() is {ClearanceMm:10,MaximumMm:500,ReserveMm:50},"Fill to wall defaults 10 mm clearance and 500 mm cap");
        Check(ComposerFill.IsObstacleType("CrewComponent")&&ComposerFill.IsObstacleType("Cannon")&&ComposerFill.IsObstacleType("AmmoRack")&&ComposerFill.IsObstacleType("Carousel")&&!ComposerFill.IsObstacleType("Hull"),"overlap warning filter names crew, gun, ammunition and carousel");
        var runtime=File.ReadAllText(Path.Combine(root,"RuntimeCompositePlateEdit.cs"));
        Check(System.Text.RegularExpressions.Regex.Matches(runtime,@"creator\.CreateOp\(").Count==1&&runtime.Contains("prepared);")&&runtime.Contains("pending.Revert()"),"all four actions share exactly one native operation and Revert callback");
        Check(runtime.Contains("if(prepared!=null){plan=new(materialId,prepared.Plan.After.ThicknessList().ToArray());")&&runtime.Contains("else plan=ComputePlan("),"a prepared action never runs the depth planners (Inward on a To back plane plate)");
        Check(runtime.Contains("!newTopology&&(beforeEdit.VertexCount!=after.VertexCount"),"topology counts stay strict except for the cheek preset");
        Check(File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs")).Contains("plate.transform.worldToLocalMatrix"),"overlap bounds use the plate's Unity frame like the plate visual");
        Check(runtime.Contains("component.RequestMeshRefresh();component.RequestRebuild();")&&runtime.Contains("PlateMeshDeferred.WhenReady"),"render/collider refresh and mesh leases retained");
        var side=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));
        Check(side.Contains("if(!Designer")&&side.Contains("PlateOverlaySnapshotReader.Read")&&!side.Contains("Physics."),"designer-only gated mesh reads, no physics rays");
        BlueprintAndPreview(output,Check);
        File.WriteAllText(Path.Combine(output,"FILL-FIXTURES.tsv"),rows.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT53-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=count,nativeUndoTested=false,nativeSaveReloadTested=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT53 PASS: {count} checks.");
    }
    internal static void AddQuad(List<FillTriangle> walls,Vector3 a,Vector3 b,Vector3 c,Vector3 d){walls.Add(new(a,b,c));walls.Add(new(a,c,d));}
    internal static List<FillTriangle> Box(Vector3 min,Vector3 max)
    {
        var t=new List<FillTriangle>();
        AddQuad(t,new(min.X,min.Y,min.Z),new(max.X,min.Y,min.Z),new(max.X,max.Y,min.Z),new(min.X,max.Y,min.Z));
        AddQuad(t,new(min.X,min.Y,max.Z),new(max.X,min.Y,max.Z),new(max.X,max.Y,max.Z),new(min.X,max.Y,max.Z));
        AddQuad(t,new(min.X,min.Y,min.Z),new(min.X,min.Y,max.Z),new(min.X,max.Y,max.Z),new(min.X,max.Y,min.Z));
        AddQuad(t,new(max.X,min.Y,min.Z),new(max.X,min.Y,max.Z),new(max.X,max.Y,max.Z),new(max.X,max.Y,min.Z));
        AddQuad(t,new(min.X,min.Y,min.Z),new(max.X,min.Y,min.Z),new(max.X,min.Y,max.Z),new(min.X,min.Y,max.Z));
        AddQuad(t,new(min.X,max.Y,min.Z),new(max.X,max.Y,min.Z),new(max.X,max.Y,max.Z),new(min.X,max.Y,max.Z));return t;
    }
    private static void BlueprintAndPreview(string output,Action<bool,string> check)=>ComposerFillBlueprintEvidence.Run(output,check);
}
