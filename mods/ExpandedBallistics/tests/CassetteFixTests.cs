using System.Numerics;
namespace SprocketExpandedBallistics;

internal static class CassetteFixTests
{
    internal static void Run(Action<bool,string> check,IReadOnlyDictionary<string,CassetteMaterial> materials,MaterialReactionCatalogue reactions)
    {
        CassettePreset Preset(params CassetteLayer[] layers)=>new("directional","Directional test","1980.01.01",1,4000,layers);
        var nera=new CassetteModel(Preset(new("rha",3,30,"up"),new("elastomerNERA",15,30,"up"),new("rha",3,30,"up"),new("air",20,0,"up")),materials,reactions);
        var ceramic=new CassetteModel(Preset(new("ceramicArray",40,0,"up"),new("rha",20,0,"up")),materials,reactions);
        double energy=.5*17500*.6*1600*1600*Math.PI*.25*.02*.02;
        CassetteEvaluation Eval(CassetteModel model,MaterialThreatTags tags,bool reverse=false)=>model.Evaluate(model.DepthMm,reverse?Vector3.UnitZ:-Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitZ,tags,.02,energy,true,reverse);
        foreach(var behavior in new[]{"heat","atgm","atgm_gun","apfsds","ap","aphe","he","hesh","other"})
        {
            var tags=new MaterialThreatTags(behavior,"unknown",LengthToDiameter:30);
            var all=tags.ToArray();
            foreach(var candidate in new[]{"chemicalJet","longRod","shortRod","kineticFullBore","fragment","custom"})
                check(tags.Contains(candidate)==all.Contains(candidate),"cassette and loose-layer tag union agree: "+behavior+"/"+candidate);
            var result=Eval(ceramic,tags);
            bool chemical=behavior is "heat" or "atgm" or "atgm_gun",rod=behavior=="apfsds",classic=behavior is "ap" or "aphe";
            check((result.JetWorkMm>0)==chemical&&(result.TotalRodWorkJ>0)==(rod||classic),"cassette mechanism per behavior: "+behavior);
        }
        check(new MaterialThreatTags("apfsds","unknown",LengthToDiameter:8).Contains("shortRod"),"short APFSDS behavior maps to shortRod");
        foreach(var behavior in new[]{"he","hesh","other"})
        {
            check(Eval(ceramic,new(behavior,"chemicalJet")).JetWorkMm>0,"ThreatClass chemical tag activates "+behavior);
            check(Eval(ceramic,new(behavior,"unknown",new[]{"chemicalJet"})).JetWorkMm>0,"profile chemical tag activates "+behavior);
            check(Eval(ceramic,new(behavior,"unknown",new[]{"shortRod"})).TotalRodWorkJ>0,"profile rod tag activates "+behavior);
        }
        foreach(var behavior in new[]{"he","hesh"})
            check(Eval(ceramic,new(behavior,"kineticFullBore")).ActiveLayers==0,"HE/HESH without chemical or rod tags stays native: "+behavior);
        check(Eval(ceramic,new("atgm","chemicalJet",new[]{"chemicalJet"})).JetWorkMm==Eval(ceramic,new("heat","unknown")).JetWorkMm,"duplicate chemical tags do not multiply work");
        var bothRodTags=new MaterialThreatTags("apfsds","shortRod",new[]{"longRod"},8);
        check(Eval(nera,bothRodTags).TotalRodWorkJ==Eval(nera,new("apfsds","unknown",LengthToDiameter:30)).TotalRodWorkJ,"every relevant rod tag is considered and each eligible layer works once");
        check(Eval(ceramic,new("heat","chemicalJet",new[]{"longRod"},30,Original:false)).ActiveLayers==0,"secondary fragments do not inherit original cassette tags");

        var front=Eval(ceramic,new("heat","unknown"));var back=Eval(ceramic,new("heat","unknown"),true);
        check(front.JetWorkMm>0&&back.JetWorkMm==0&&back.Reverse,"ceramic front has steel backing; reverse traversal has none");
        check(ceramic.Layers[0].Supported&&!ceramic.ReverseLayers[^1].Supported,"backed support is prepared separately for each side");
        var frontNera=Eval(nera,new("heat","unknown"));var backNera=Eval(nera,new("heat","unknown"),true);
        check(Math.Abs(backNera.JetWorkMm/frontNera.JetWorkMm-.3)<1e-10,"rear expansion air becomes entry air on reverse NERA hit");
        check(nera.Layers[1].NeraAirFactor==1&&nera.ReverseLayers[1].NeraAirFactor==.3,"both NERA air factors are prepared at load time");
        var gaps=new CassetteModel(Preset(new("rha",3,30,"up"),new("air",10,0,"up"),new("elastomerNERA",15,30,"up"),new("air",60,0,"up"),new("rha",3,30,"up")),materials,reactions);
        check(gaps.Layers[1].FrontGapMm==10&&gaps.Layers[1].RearGapMm==60&&gaps.ReverseLayers[1].FrontGapMm==60&&gaps.ReverseLayers[1].RearGapMm==10,"front/back support gaps exchange in reversed preparation");
        var perforated=new CassetteModel(Preset(new("perforatedHhs",20,0,"up"),new("air",50,0,"up"),new("rha",20,0,"up")),materials,reactions);
        var pf=Eval(perforated,new("apfsds","unknown",LengthToDiameter:30));var pb=Eval(perforated,new("apfsds","unknown",LengthToDiameter:30),true);
        check(Math.Abs(pf.YawDegrees-12)<1e-10&&Math.Abs(pb.YawDegrees-6)<1e-10,"only downstream air amplifies yaw in each direction");
        var repeated=new CassetteModel(Preset(new("rha",3,30,"up"),new("elastomerNERA",10,30,"up"),new("rha",3,30,"up"),new("air",20,0,"up"),new("rha",3,30,"up"),new("elastomerNERA",20,30,"up"),new("rha",3,30,"up")),materials,reactions);
        var forwardCores=repeated.Layers.Where(l=>l.Recipe?.Id=="elastomerSandwich").ToArray();
        var backwardCores=repeated.ReverseLayers.Where(l=>l.Recipe?.Id=="elastomerSandwich").ToArray();
        check(forwardCores[0].Layer.ThicknessMm==10&&forwardCores[0].RepeatFactor==1&&backwardCores[0].Layer.ThicknessMm==20&&backwardCores[0].RepeatFactor==1&&backwardCores[1].RepeatFactor==.85,"diminishing order is prepared from the actual entry side");
        check(backwardCores[0].SourceLayerIndex==5&&backwardCores[1].SourceLayerIndex==1,"reversed feed details retain authored layer indices");

        var rootRotation=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY,.6f)*Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.3f));
        static (Vector3 Up,Vector3 Forward) Frame(Quaternion rotation)=>new(Vector3.Transform(Vector3.UnitY,rotation),Vector3.Transform(Vector3.UnitZ,rotation));
        var frame=Frame(rootRotation);
        var plate0=rootRotation;var plate90=rootRotation*Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2);
        var worldNormal=Vector3.Transform(Vector3.UnitZ,rootRotation);
        var localNormal90=Vector3.Transform(worldNormal,Quaternion.Inverse(plate90));
        var world0=Vector3.Normalize(Vector3.Transform(Vector3.UnitZ,plate0));var world90=Vector3.Normalize(Vector3.Transform(localNormal90,plate90));
        var worldRay=Vector3.Transform(new Vector3(0,.5f,-MathF.Sqrt(.75f)),rootRotation);
        var a=nera.Evaluate(nera.DepthMm,worldRay,world0,frame.Up,frame.Forward,"chemicalJet",.02,energy,true);
        var b=nera.Evaluate(nera.DepthMm,worldRay,world90,frame.Up,frame.Forward,"chemicalJet",.02,energy,true);
        var wrongFrame=Frame(plate90);
        var wrong=nera.Evaluate(nera.DepthMm,worldRay,world90,wrongFrame.Up,wrongFrame.Forward,"chemicalJet",.02,energy,true);
        check(Math.Abs(a.JetWorkMm-b.JetWorkMm)<.001&&Math.Abs(a.JetWorkMm-wrong.JetWorkMm)>1,"90-degree plate attachment cannot rotate the vehicle tilt frame");
        var n=Vector3.Normalize(new Vector3(.4f,.2f,1));var tilt=new CassetteTilt(.5,Math.Sqrt(.75),"left");
        var tilted=CassetteModel.TiltNormal(n,Vector3.UnitY,Vector3.UnitZ,tilt);
        var target=-Vector3.UnitX;var tangent=Vector3.Normalize(target-n*Vector3.Dot(target,n));
        check(Vector3.Distance(tilted,n*(float)tilt.Cos+tangent*(float)tilt.Sin)<1e-6,"left means projected vehicle-left on an oblique face");
        var roof=CassetteModel.TiltNormal(Vector3.UnitY,Vector3.UnitY,Vector3.UnitZ,tilt with{Direction="up"});
        check(roof.Z>.49&&Math.Abs(roof.X)<1e-7,"horizontal face up tilt falls back toward vehicle-forward");
        FaceTests(check,ceramic,energy);
        check(materials["structuralSteel"].CostMultiplier==.6&&materials["castArmour"].CostMultiplier==1.3,"cheap passive steel prices are restored");
        check(materials["perforatedHhs"].CostMultiplier==6&&materials["perforatedHhs"].CostMultiplier>=materials["highHardnessSteel"].CostMultiplier,"perforated HHS carries the machining premium");
        var catalogue=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName)));
        var passive=catalogue.Presets.Single(p=>p.Id=="passive");var generic=new CassetteModel(passive,materials,reactions);
        check(passive.Layers.Select(l=>l.Material+":"+l.ThicknessMm).SequenceEqual(new[]{"rha:20","ceramicArray:40","rha:10","cwepGlassTextolite:40","rha:20"}),"generic passive preset includes the 10 mm intermediate steel support");
        check(generic.DepthMm==130&&Math.Abs(generic.MassKgM2-610.5)<1e-9&&Math.Abs(generic.Passive.RhaFactor*generic.DepthMm-112)<1e-9,"generic passive derived depth, mass and native resistance recalculated (0.22.20 values: ceramicArray 0.90, textolite 0.65)");
        check(generic.Layers[1].Supported&&generic.Layers[3].Supported&&generic.ReverseLayers[1].Supported&&generic.ReverseLayers[3].Supported,"generic ceramic/textolite have support in both traversal directions");
        var genericJet=Eval(generic,new("heat","unknown")).JetWorkMm;
        check(genericJet>0&&Math.Abs(genericJet-Eval(generic,new("heat","unknown"),true).JetWorkMm)<1e-9&&112+genericJet>134,"generic passive HEAT estimate is the same from either side and above the 0.22.19 value of 134 mm (0.22.20 calibration)");
    }
    private static void FaceTests(Action<bool,string> check,CassetteModel model,double energy)
    {
        var normals=new[]{Vector3.UnitZ,Vector3.UnitX,Vector3.Normalize(new Vector3(0,1,1))};
        var widths=new[]{4,5,3};var offsets=new[]{0,4,9};var thicknesses=new short[]{60,125,90};
        for(int f=0;f<3;f++)foreach(bool reverse in new[]{false,true})
        {
            int start=(reverse?12:0)+offsets[f];short sample=reverse?(short)-1:thicknesses[f];
            bool mapped=DeclaredFacePolicy.ResolveSamples(f*3+(reverse?9:0),18,24,start,start+1,start+2,sample,sample,sample,
                new uint[]{12,24},new byte[]{1,2},widths,out var face,allowNativeSentinel:true,allowPolygons:true);
            check(mapped&&face.FaceIndex==f&&face.SurfaceType==(reverse?2:1),"multi-face/polygon marker maps exact cap "+f+"/"+reverse);
            // NativeHitFace substitutes the loop-average configured thickness
            // for the native -1 back-cap samples, independently for each face.
            face=face with{NormalThicknessMm=thicknesses[f]};
            var normal=normals[f];var cap=reverse?-normal:normal;var ray=-cap;
            var result=model.Evaluate(face.NormalThicknessMm,ray,normal,Vector3.UnitY,Vector3.UnitZ,"chemicalJet",.02,energy,true,reverse);
            check(result.InWindow&&(result.JetWorkMm>0)==!reverse,"composer traversal keeps directional backing "+f+"/"+reverse);
        }
    }
}
