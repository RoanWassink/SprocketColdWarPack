using System.Numerics;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.16.1 fork acceptance: one back plane for every treated face, per-corner thickness along the
/// native face normal, clamps, selection, undo, and the cassette depth at the hit following the face.
/// </summary>
internal static class ForkBackplaneTests
{
    private static readonly double Sin20=Math.Sin(20*Math.PI/180),Cos20=Math.Cos(20*Math.PI/180),Tan20=Math.Tan(20*Math.PI/180);
    internal static void Run(Action<bool,string> check)
    {
        var total=0;var assert=check;check=(ok,message)=>{assert(ok,message);total++;};
        Flat(check);
        Wedge(check);
        Clamp(check);
        AwayFacing(check);
        Direction(check);
        NativeDirection(check);
        Selection(check);
        Undo(check);
        
        DepthLimits(check);
        UniformParity(check);
        SavedMode(check);
        Overlay(check);
        Console.WriteLine($"0.22.16.1 back-plane checks PASS: {total} checks");
    }
    private static Vector3[] Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)=>new[]{a,b,c,d};
    private static bool Same(ushort[][] a,ushort[][] b)=>a.Length==b.Length&&a.Zip(b).All(p=>p.First.SequenceEqual(p.Second));
    private static ushort[][] Old(params ushort[][] values)=>values;

    // A flat plate perpendicular to the plane must get D at every corner.
    private static void Flat(Action<bool,string> check)
    {
        var face=Quad(new(-.5f,-.5f,0),new(-.5f,.5f,0),new(.5f,.5f,0),new(.5f,-.5f,0));
        var old=Old(new ushort[]{20,20,20,20});
        var plan=BackplaneThickness.Create("ebCassette_test",old,new[]{face},new[]{Vector3.UnitZ},null,100);
        check(plan.Plan.Thicknesses[0].All(t=>t==100),"fork-backplane: a flat plate gets D on every corner");
        check(plan.Faces==1&&plan.ClampedCorners==0&&plan.MinMm==100&&plan.MaxMm==100,"fork-backplane: flat plate reports one face, 100 mm, no clamps");
        check(plan.Summary=="1 face, 100–100 mm t, 0 clamped","fork-backplane: the Apply message names faces, range and clamps ("+plan.Summary+")");
        var thrown=false;try{BackplaneThickness.Create("x",old,new[]{face},new[]{Vector3.UnitZ},null,0);}catch(ArgumentOutOfRangeException){thrown=true;}
        check(thrown,"fork-backplane: an invalid back-plane depth is refused");
    }

    // Two faces at +/-20 degrees must end on one plane, measured on the built back points.
    private static void Wedge(Action<bool,string> check)
    {
        var a=Quad(new(0,-.5f,0),new(0,.5f,0),new(1,.5f,(float)-Tan20),new(1,-.5f,(float)-Tan20));
        var b=Quad(new(0,-.5f,0),new(0,.5f,0),new(-1,.5f,(float)-Tan20),new(-1,-.5f,(float)-Tan20));
        var nA=new Vector3((float)Sin20,0,(float)Cos20);var nB=new Vector3((float)-Sin20,0,(float)Cos20);
        var old=Old(new ushort[]{20,20,20,20},new ushort[]{20,20,20,20});
        var plan=BackplaneThickness.Create("ebCassette_test",old,new[]{a,b},new[]{nA,nB},null,500);
        check(plan.Faces==2&&plan.ClampedCorners==0,"fork-backplane: the +/-20 degree wedge treats both faces without clamps");
        var normals=new[]{nA,nB};var faces=new[]{a,b};var plane=-500d;
        for(int f=0;f<2;f++)
            for(int j=0;j<4;j++)
            {
                var t=plan.Plan.Thicknesses[f][j]/1000f;
                var back=faces[f][j]-normals[f]*t;
                var signedMm=Vector3.Dot(back,Vector3.UnitZ)*1000d;
                check(Math.Abs(signedMm-plane)<=.51,$"fork-backplane: wedge back corner {f}/{j} is on the plane ({signedMm:0.###} mm)");
            }
        check(plan.MinMm<plan.MaxMm,"fork-backplane: the wedge thickness really varies per corner ("+plan.MinMm+"-"+plan.MaxMm+" mm)");
    }

    // A face nearly perpendicular to the back plane must clamp instead of shooting to infinity.
    private static void Clamp(Action<bool,string> check)
    {
        var flat=Quad(new(-.5f,-.5f,0),new(-.5f,.5f,0),new(.5f,.5f,0),new(.5f,-.5f,0));
        var side=Quad(new(0,0,0),new(0,.1f,0),new(0,.1f,-.1f),new(0,0,-.1f));
        var old=Old(new ushort[]{20,20,20,20},new ushort[]{20,20,20,20});
        var plan=BackplaneThickness.Create("ebCassette_test",old,new[]{flat,side},new[]{Vector3.UnitZ,Vector3.UnitX},null,100);
        check(plan.ClampedFaces>=1&&plan.ClampedCorners>0,"fork-backplane: a face almost perpendicular to the back plane is clamped");
        check(plan.Plan.Thicknesses[1].All(t=>t<=200),"fork-backplane: clamped corners never exceed 2xD ("+string.Join(",",plan.Plan.Thicknesses[1])+")");
        check(plan.Plan.Thicknesses[1].Max()==200,"fork-backplane: the perpendicular face reaches the 2xD clamp");
    }

    // With a selection only those faces change; the others keep every corner.
    private static void Selection(Action<bool,string> check)
    {
        var a=Quad(new(0,-.5f,0),new(0,.5f,0),new(1,.5f,(float)-Tan20),new(1,-.5f,(float)-Tan20));
        var b=Quad(new(0,-.5f,0),new(0,.5f,0),new(-1,.5f,(float)-Tan20),new(-1,-.5f,(float)-Tan20));
        var nA=new Vector3((float)Sin20,0,(float)Cos20);
        var old=Old(new ushort[]{20,20,20,20},new ushort[]{7,8,9,10});
        var plan=BackplaneThickness.Create("ebCassette_test",old,new[]{a,b},new[]{nA,nA},new[]{true,false},500);
        check(plan.Faces==1,"fork-backplane: with a selection only the selected face is treated");
        check(plan.Plan.Thicknesses[0].All(t=>t==500),"fork-backplane: the selected face gets the back-plane thickness");
        check(plan.Plan.Thicknesses[1].SequenceEqual(old[1]),"fork-backplane: the unselected face keeps its own per-corner thickness");
        check(!ReferenceEquals(plan.Plan.Thicknesses[1],old[1]),"fork-backplane: the plan is a value snapshot");
    }

    // Undo restores every old thickness, redo restores the back-plane plan.
    private static void Undo(Action<bool,string> check)
    {
        var a=Quad(new(0,-.5f,0),new(0,.5f,0),new(1,.5f,(float)-Tan20),new(1,-.5f,(float)-Tan20));
        var b=Quad(new(0,-.5f,0),new(0,.5f,0),new(-1,.5f,(float)-Tan20),new(-1,-.5f,(float)-Tan20));
        var nA=new Vector3((float)Sin20,0,(float)Cos20);var nB=new Vector3((float)-Sin20,0,(float)Cos20);
        var old=Old(new ushort[]{20,20,20,20},new ushort[]{7,8,9,10});
        var plan=BackplaneThickness.Create("ebCassette_test",old,new[]{a,b},new[]{nA,nB},null,500);
        var snapshot=new CompositeUndoSnapshot("rha","ebCassette_test",true,old,plan.Plan);
        check(Same(snapshot.After(false).Thicknesses,old),"fork-backplane: undo restores every old corner thickness");
        check(Same(snapshot.After(true).Thicknesses,plan.Plan.Thicknesses),"fork-backplane: redo restores the back-plane plan");
    }

    // The hit depth uses the hit face's own averaged thickness, not the nominal D.
    private static Vector3[] FlatAt(float z,float half=0.5f)=>
        Quad(new(-half,-half,z),new(-half,half,z),new(half,half,z),new(half,-half,z));

    private static void AwayFacing(Action<bool,string> check)
    {
        var old=Old(new ushort[]{20,20,20,20},new ushort[]{20,20,20,20});
        var normals=new[]{Vector3.UnitZ,-Vector3.UnitZ};
        var valid=BackplaneThickness.Create("x",old,new[]{FlatAt(0,1),FlatAt(-.2f)},normals,null,100);
        check(valid.Plan.Thicknesses[1].All(t=>t==100)&&valid.ClampedCorners==0,
            "fork-backplane fix: away-facing face behind the back plane gets valid 100 mm, not 2D");
        check(FlatAt(-.2f).Zip(valid.Plan.Thicknesses[1]).All(p=>Math.Abs((p.First+Vector3.UnitZ*p.Second*.001f).Z+.1)<.0005),
            "fork-backplane fix: the away-facing back corners reach the same back plane");
        var negative=BackplaneThickness.Create("x",old,new[]{FlatAt(0,1),FlatAt(0)},normals,null,100);
        check(negative.Plan.Thicknesses[1].All(t=>t==1)&&negative.ClampedCorners==4,
            "fork-backplane fix: a negative solved thickness clamps to 1 mm");
        var excessive=BackplaneThickness.Create("x",old,new[]{FlatAt(0,1),FlatAt(-.5f)},normals,null,100);
        check(excessive.Plan.Thicknesses[1].All(t=>t==200)&&excessive.ClampedCorners==4,
            "fork-backplane fix: excessive away-facing thickness clamps to 2D");
    }

    private static void Direction(Action<bool,string> check)
    {
        var old=Old(new ushort[]{20,20,20,20},new ushort[]{20,20,20,20});
        var a=BackplaneThickness.Create("x",old,new[]{FlatAt(0,1),FlatAt(-.2f)},new[]{Vector3.UnitZ,-Vector3.UnitZ},null,100);
        var b=BackplaneThickness.Create("x",old,new[]{FlatAt(-.2f),FlatAt(0,1)},new[]{-Vector3.UnitZ,Vector3.UnitZ},null,100);
        check(a.Plan.Thicknesses[0].SequenceEqual(b.Plan.Thicknesses[1])&&a.Plan.Thicknesses[1].SequenceEqual(b.Plan.Thicknesses[0]),
            "fork-backplane fix: area-weighted direction and solved corners are independent of face order");
        string Refusal(Vector3[] normals)
        {
            try{BackplaneThickness.Create("x",old,new[]{FlatAt(0),FlatAt(-.2f)},normals,null,100);return "";}
            catch(ArgumentException ex){return ex.Message;}
        }
        var forward=Refusal(new[]{Vector3.UnitZ,-Vector3.UnitZ});
        var reverse=Refusal(new[]{-Vector3.UnitZ,Vector3.UnitZ});
        check(forward.Contains("average face normal cancels out")&&forward==reverse,
            "fork-backplane fix: cancelling normals are refused clearly in either face order");
    }

    private static void NativeDirection(Action<bool,string> check)
    {
        string Refusal(ThicknessMode mode,bool selected,bool alongFaceNormal)
        {
            try{BackplaneThickness.RequireFaceNormal(mode,selected,alongFaceNormal,2,1);return "";}
            catch(InvalidOperationException ex){return ex.Message;}
        }
        var message=Refusal(ThicknessMode.ToBackPlane,true,false);
        check(message.Contains("AlongFaceNormal")&&message.Contains("face 3, corner 2"),
            "fork-backplane fix: a plate with a different native thickenMode is refused before mutation, with face and corner");
        check(Refusal(ThicknessMode.ToBackPlane,true,true)=="","fork-backplane fix: AlongFaceNormal is accepted");
        check(Refusal(ThicknessMode.ToBackPlane,false,false)=="","fork-backplane fix: unselected faces may keep other native directions");
        check(Refusal(ThicknessMode.Uniform,true,false)=="","fork-backplane fix: Uniform retains native direction behavior");
    }

    private static void DepthLimits(Action<bool,string> check)
    {
        const ThicknessMode mode=ThicknessMode.ToBackPlane;
        var root=AppContext.BaseDirectory;
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var preset=new CassettePreset("fork_limits","Fork limits","1980.01.01",90,110,
            new[]{new CassetteLayer("rha",20,0,"up"),new CassetteLayer("elastomerNERA",60,0,"up"),new CassetteLayer("rha",20,0,"up")});
        var cassette=new CassetteModel(preset,materials,reactions);
        var tags=new MaterialThreatTags("heat","unknown");
        CassetteEvaluation At(double depth,ThicknessMode thicknessMode)=>cassette.Evaluate(depth,Vector3.Normalize(new Vector3(0,.5f,-.8660254f)),Vector3.UnitZ,Vector3.UnitY,Vector3.UnitZ,
            tags,.02,1e6,true,false,thicknessMode);
        foreach(var t in new[]{1d,200d})
        {
            var evaluated=At(t,mode);
            check(evaluated.InWindow&&double.IsFinite(evaluated.JetWorkMm)&&(t==1?evaluated.JetWorkMm==0:evaluated.JetWorkMm>0),
                $"fork-backplane fix: evaluation accepts {t} mm while retaining the recipes' layer-thickness limits");
            check(!At(t,ThicknessMode.Uniform).InWindow,$"fork-backplane fix: Uniform still enforces the preset window at {t} mm");
        }
        check(!At(.99,mode).InWindow&&!At(200.01,mode).InWindow,"fork-backplane fix: evaluation also enforces 1 mm..2D");
    }

    private static void UniformParity(Action<bool,string> check)
    {
        check(!DeclaredFacePolicy.MatchesConfiguredThickness(20,21),"fork-backplane fix: configured=20/native=21 does not match");
        check(DeclaredFacePolicy.MatchesConfiguredThickness(20.75,20)&&!DeclaredFacePolicy.MatchesConfiguredThickness(20.75,21),
            "fork-backplane fix: truncated corner average matches exactly, with no 1 mm tolerance");
        foreach(var configured in new[]{1d,20,20.25,20.999,32767})
            foreach(var native in new[]{(short)configured,(short)((short)configured-1),(short)((short)configured+1),(short)-1})
                check(DeclaredFacePolicy.MatchesConfiguredThickness(configured,native)==(native==-1||(short)configured==native),
                    $"fork-backplane fix: marker comparison equals 0.22.16 for {configured}/{native}");
        check(!DeclaredFacePolicy.ResolveSinglePlanarFace(1,4,false,20,1,21,21,21,out _),
            "fork-backplane fix: single-face fallback also rejects configured=20/native=21");

    }

    private static void SavedMode(Action<bool,string> check)
    {
        var name=CompositePlate.NameForMode(ThicknessMode.ToBackPlane);
        var saved=System.Text.Json.JsonSerializer.Serialize(name);
        var loaded=System.Text.Json.JsonSerializer.Deserialize<string>(saved);
        check(CompositePlate.IsName(loaded)&&CompositePlate.ModeFromName(loaded)==ThicknessMode.ToBackPlane,
            "fork-backplane fix: native blueprint name can carry the mode without a process-local cache");
        check(CompositePlate.NameForMode(ThicknessMode.Uniform)==CompositePlate.Name&&CompositePlate.ModeFromName(CompositePlate.Name)==ThicknessMode.Uniform,
            "fork-backplane fix: Uniform retains the exact existing native mesh name");
        check(CompositePlate.ModeFromName(null)==ThicknessMode.Uniform&&CompositePlate.ModeFromName("ordinary plate")==ThicknessMode.Uniform,
            "fork-backplane fix: existing meshes default to Uniform");
    }

    // The designer layer overlay follows the same per-corner depths, not one face average.
    private static void Overlay(Action<bool,string> check)
    {
        var quad=Quad(new(-.5f,-.5f,0),new(-.5f,.5f,0),new(.5f,.5f,0),new(.5f,-.5f,0));
        var face=new CassetteVisualFace(new[]{0,1,2,3},25,new double[]{10,20,30,40});
        var mesh=CassetteEdgeMesh.Build(quad,new[]{face});
        var deepest=mesh.Edges.Vertices.Max(v=>Math.Abs(v.Z));
        check(Math.Abs(deepest-.040)<1e-4,"fork-backplane: the layer overlay reaches the deepest corner ("+deepest+" m)");
    }
}
