using System.Numerics;
using System.Text.Json;
namespace SprocketExpandedBallistics;
internal static class Feat02254ComposerFixTests
{
    internal static void Run(Action<bool,string> sink)
    {
        int checks=0;void Check(bool b,string m){checks++;sink(b,"Composer fixes: "+m);}
        const string json="{\"majorVersion\":0,\"minorVersion\":3,\"vertices\":[0,0,0,0,1,0,1,1,0,1,0,0],\"edges\":[0,1,1,2,2,3,3,0],\"edgeFlags\":[0,0,0,0],\"faces\":[{\"v\":[0,1,2,3],\"t\":[20,30,40,50],\"tm\":0,\"te\":0}]}";
        var before=ComposerFill.Read(json);var wall=new List<FillTriangle>();
        Feat02253ComposerFillTests.AddQuad(wall,new(-2,-2,.3f),new(2,-2,.3f),new(2,2,.3f),new(-2,2,.3f));
        var fill=ComposerFill.Plan(json,before,null,ComposerMeshAction.Fill,new(.5f,.5f,-1),wall,new());
        Check(fill.Flipped==0&&fill.After.Faces[0].Normal==before.Faces[0].Normal,"Fill keeps direction even when reference is behind the plate");
        Check(fill.After.Faces[0].Thickness.All(t=>Math.Abs(t-290)<=1),"each forward corner ends 10 mm short of wall within 1 mm");
        Check(!fill.NeedsConfirmation&&fill.CanCommit,"all corners within maximum apply directly");
        Check(!fill.Summary.Contains("faces flipped"),"Fill omits flips from message");
        var reversed=ComposerFill.Plan(json,before,null,ComposerMeshAction.Flip,Vector3.Zero,wall,new());
        var miss=ComposerFill.Plan(reversed.Json,reversed.After,null,ComposerMeshAction.Fill,Vector3.Zero,wall,new());
        Check(miss.Flipped==0&&miss.Misses==4&&miss.Total==4&&miss.Limited==0,"reversed plate has exactly four missing corners");
        Check(miss.NeedsConfirmation&&!miss.CanCommit,"reversed plate cannot commit until Apply");
        Check(miss.FillInspection.Contains("0 of 4")&&miss.FillInspection.Contains("4 corners found no wall"),"inspection counts are plain and correct");
        var untouched=reversed.Json;var discarded=miss; // Back drops this immutable plan, never calls native operation.
        Check(untouched==reversed.Json&&ComposerFill.Read(untouched).Faces[0].Thickness.SequenceEqual(reversed.After.Faces[0].Thickness),"Back keeps source payload byte exact");
        var accepted=miss.Accept(123);
        Check(accepted.CanCommit&&accepted.After.Faces[0].Thickness.All(t=>t==123),"Apply uses entered 123 mm for every missing corner");
        Check(!miss.CanCommit&&miss.After.Faces[0].Thickness.All(t=>t==50),"acceptance does not mutate the original preview or mesh");
        foreach(double invalid in new[]{2d,501d,double.NaN,double.PositiveInfinity})
        {bool refused=false;try{miss.Accept(invalid);}catch(InvalidOperationException){refused=true;}Check(refused,"invalid reserve is refused: "+invalid);}
        var far=wall.Select(t=>new FillTriangle(t.A*3,t.B*3,t.C*3)).ToArray();
        var capped=ComposerFill.Plan(json,before,null,ComposerMeshAction.Fill,Vector3.Zero,far,new());
        Check(capped.Total==4&&capped.Misses==0&&capped.Limited==4&&capped.NeedsConfirmation&&!capped.CanCommit,"all four far corners are found, capped and require review");
        Check(capped.Accept(77).After.Faces[0].Thickness.All(t=>t==500),"reserve entry never changes capped wall corners");
        var halfWall=new List<FillTriangle>();Feat02253ComposerFillTests.AddQuad(halfWall,new(-1,-2,.3f),new(.5f,-2,.3f),new(.5f,2,.3f),new(-1,2,.3f));
        var mixed=ComposerFill.Plan(json,before,null,ComposerMeshAction.Fill,Vector3.Zero,halfWall,new());
        Check(mixed.Total==4&&mixed.Misses==2&&mixed.Limited==0,"mixed plan counts two hits and two misses");
        Check(mixed.Accept(87).After.Faces[0].Thickness.SequenceEqual(new ushort[]{290,290,87,87}),"reserve changes missing corners only");
        var gun=new FillBounds("gun",new(-.14f,0,0),new(.14f,.4f,1));
        Check(Math.Abs(ComposerFill.GunHalfGap(new[]{gun})-.19f)<1e-6,"gun corridor uses renderer width plus 50 mm each side");
        Check(ComposerFill.GunHalfGap(Array.Empty<FillBounds>())==.35f,"missing gun falls back to 700 mm opening");
        var box=Feat02253ComposerFillTests.Box(new(-1.3f,0,-1),new(1.3f,.8f,1.4f));var host=new FillBounds("turret",new(-1.3f,0,-1),new(1.3f,.8f,1.4f));
        var cheeks=ComposerFill.Cheeks(box,host,new(),new(),.19f,false);
        Check(cheeks.After.Faces.All(f=>f.Thickness.All(t=>t==450)),"cheek default fills to 450 mm even with no interior wall behind it");
        Check(cheeks.After.Vertices.All(v=>Math.Abs(v.X)>=.19f-1e-6),"cheeks stay outside gun opening");
        double width=2*(1.3-.19),height=.74,hand=width*height*.45;
        var stats=cheeks.Inspect(4000,Array.Empty<FillBounds>());
        Check(Math.Abs(stats.VolumeM3-hand)<1e-6&&Math.Abs(stats.MassKg-hand*4000)<.01,"rectangular hand calculation: 2 x 1.11 x .74 x .45 m3, mass x 4000");
        var custom=ComposerFill.Cheeks(box,host,new(),new(330,1.1),.19f,false);
        Check(custom.After.Faces.All(f=>f.Thickness.All(t=>t<=330))&&custom.After.Faces.All(f=>f.Corners.Where((v,j)=>v.Z-f.Normal.Z*f.Thickness[j]*.001f<1.1f-1e-5).Count()==0),"custom maximum and rear boundary respected");
        // A forwards-facing side bevel must not be accepted just because a +Z sample reaches it.
        var sideWalls=new List<FillTriangle>();
        Feat02253ComposerFillTests.AddQuad(sideWalls,new(.5f,-1,1),new(1.3f,-1,-.6f),new(1.3f,1,-.6f),new(.5f,1,1));
        var sideHit=ComposerFill.RaySurface(new(.6f,.2f,.35f),sideWalls);
        Check(sideHit.HasValue&&Math.Abs(sideHit.Value.Normal.X)>Math.Abs(sideHit.Value.Normal.Z),"side wall fixture exposes sideways normal");
        bool noSide=false;try{ComposerFill.Cheeks(sideWalls,host,new(),new(),.5f,false);}catch(InvalidOperationException){noSide=true;}
        Check(noSide,"preset rejects all cells whose forward hit is a side wall");
        var jumpWalls=new List<FillTriangle>();
        foreach(int side in new[]{-1,1})
        {
            Feat02253ComposerFillTests.AddQuad(jumpWalls,new(side*.19f,-1,1),new(side*.6f,-1,1),new(side*.6f,1,1),new(side*.19f,1,1));
            Feat02253ComposerFillTests.AddQuad(jumpWalls,new(side*.6f,-1,1.8f),new(side*1.3f,-1,1.8f),new(side*1.3f,1,1.8f),new(side*.6f,1,1.8f));
        }
        var safeRows=ComposerFill.Cheeks(jumpWalls,host,new(),new(450,.35,150),.19f,false);
        var looseRows=ComposerFill.Cheeks(jumpWalls,host,new(),new(450,.35,1000),.19f,false);
        Check(safeRows.After.Vertices.All(v=>Math.Abs(v.X)<=.6f)&&looseRows.After.Vertices.Any(v=>Math.Abs(v.X)>.7f),"outer rows stop at configured depth jump");
        var shiftGun=new FillBounds("gun",new(.06f,0,0),new(.34f,.4f,1));
        var shifted=ComposerFill.Cheeks(box,host,new(),new(),ComposerFill.GunHalfGap(new[]{shiftGun}),false,.2f);
        Check(Math.Abs(ComposerFill.GunHalfGap(new[]{shiftGun})-.19f)<1e-6&&shifted.After.Vertices.All(v=>v.X<=.01f+1e-6||v.X>=.39f-1e-6),"off-centre gun keeps its exact width plus 50 mm each side");
        var matrix=Matrix4x4.CreateRotationZ(.7f)*Matrix4x4.CreateTranslation(2,3,4);
        var mappedCorners=FillBounds.RendererCorners(new(1,2,3),new(.14f,.2f,.3f),v=>Vector3.Transform(v,matrix)).ToArray();
        Check(mappedCorners.Length==8&&mappedCorners.All(v=>ComposerFill.Finite(v)),"renderer mapping transforms all eight world corners (same helper as overlap and gun sizing)");
        var mapped=FillBounds.Of("gun",mappedCorners);
        double expectedWidth=2*(Math.Abs(Math.Cos(.7))*.14+Math.Abs(Math.Sin(.7))*.2);
        Check(Math.Abs(ComposerFill.GunHalfGap(new[]{mapped})*2-(expectedWidth+.1))<1e-6,"rotated renderer width uses plate-frame bounds plus 100 mm");
        var root=Fix02228Tests.SourceRoot();var ui=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));var runtime=File.ReadAllText(Path.Combine(root,"RuntimeCompositePlateEdit.cs"));
        Check(ui.Contains("prepared.Plan.NeedsConfirmation")&&ui.Contains("row.Button(\"Back\",p.CloseModal)")&&ui.Contains("prepared.Plan.Accept(mm)"),"Kit preview exposes Back, mm input and explicit Apply");
        Check(runtime.IndexOf("!prepared.Plan.CanCommit",StringComparison.Ordinal)<runtime.IndexOf("return Run(editor,prepared.Material",StringComparison.Ordinal),"native operation is gated before mutation");
        Check(ui.Contains("if(model.FillToWall){MeshAction(ComposerMeshAction.Fill,preset.MaterialId);return;}"),"regular Apply in Fill mode also uses the confirmation route");
        Check(File.ReadAllText(Path.Combine(root,"CassetteComposerModel.cs")).Contains("Place the plate where the filling should end; each corner grows the way the plate faces until 10 mm before the wall in front of it."),"requested Fill tooltip exact (0.22.71: the tooltip of the former button is the help text of the option Fill to wall in the composer model)");
        foreach(var action in Enum.GetValues<ComposerMeshAction>())Check(!string.IsNullOrWhiteSpace(ComposerFill.Label(action))&&runtime.Contains("prepared.Plan.Label+\": \"+prepared.Plan.Summary"),"status uses action label: "+ComposerFill.Label(action));
        var nativeSource=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));
        Check(nativeSource.Contains("var gunBounds=Obstacles(plate,plate.transform.worldToLocalMatrix")&&nativeSource.Contains("ComposerFill.GunHalfGap(gunBounds)"),"gun opening width uses the exact plate-frame overlap bounds");
        var output=Fix02228Tests.OutputRoot();
        File.WriteAllText(Path.Combine(output,"FEAT54-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks,nativeUndoTested=false,nativeRendererBoundsTested=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT54 PASS: {checks} checks.");
    }
}
