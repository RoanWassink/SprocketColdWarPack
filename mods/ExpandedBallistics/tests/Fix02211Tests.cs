using System.Numerics;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

// 0.22.11 acceptance: restored cassette date gate, blended-block cassette,
// per-threat cassette scales, HESH gaps and the WW2 spaced-AP response.
internal static class Fix02211Tests
{
    internal static void Run(Action<bool,string> check)
    {
        DateGate(check);
        
        ThreatScales(check);
        HeshGap(check);
        SpacedKinetic(check);
        SpallConfig(check);
    }

    private static void DateGate(Action<bool,string> check)
    {
        var before=CassetteAvailability.Key(2008,12,31);
        var on=CassetteAvailability.Key(2009,1,1);
        check(!CassetteAvailability.AvailableOnVehicle("2009.01.01",before),"date gate rejects one day before the cassette");
        check(CassetteAvailability.AvailableOnVehicle("2009.01.01",on),"date gate allows the cassette on its date");
        check(CassetteAvailability.AvailableOnVehicle("2009.01.01",CassetteAvailability.Sentinel),"Coldwar3 sentinel allows every cassette");
        check(CassetteAvailability.HitBlockReason("2009.01.01",before,true)?.Contains("native resistance only")==true,
            "too-early cassette reports a concrete native-only reason");
        check(CassetteAvailability.HitBlockReason("2009.01.01",CassetteAvailability.Sentinel,true)==null,
            "sentinel has no hit block reason");
        check(CassetteAvailability.HitBlockReason("2009.01.01",on,false)?.Contains("Technology file")==true,
            "missing Technology file reports a concrete native-only reason");
        check(CassetteAvailability.HitBlockReason("2009.01.01",on,true)==null,"available date and frame pass the hit gate");
    }

    private static void ThreatScales(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var nera=new CassetteModel(Preset(new("rha",3,30,"up"),new("elastomerNERA",15,30,"up"),new("rha",3,30,"up"),new("air",20,0,"up")),materials,reactions);
        var perforated=new CassetteModel(Preset(new("perforatedHhs",20,0,"up"),new("air",50,0,"up"),new("rha",20,0,"up")),materials,reactions);
        var ray=new Vector3(0,MathF.Sqrt(.5f),-MathF.Sqrt(.5f));
        var energy=.5*17500*.6*1600*1600*Math.PI*.25*.02*.02;
        CassetteEvaluation Eval(CassetteModel model,MaterialThreatTags tags)=>model.Evaluate(model.DepthMm,ray,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,tags,.02,energy,true);
        var ap=new MaterialThreatTags("ap","unknown");
        var aphe=new MaterialThreatTags("aphe","unknown");
        var apds=new MaterialThreatTags("ap","unknown",new[]{"shortRod"});
        var apResult=Eval(nera,ap);var apheResult=Eval(nera,aphe);var apdsResult=Eval(nera,apds);
        check(apResult.WorkJ>0&&apResult.ScaleNote=="NERA \u00D70.25","NERA full-bore AP pays the 0.25 scale and reports it");
        check(apheResult.WorkJ>0&&apheResult.ScaleNote=="NERA \u00D70.25","NERA APHE pays the same full-bore scale");
        check(apdsResult.WorkJ>0&&apdsResult.ScaleNote=="NERA \u00D70.5","NERA APDS pays the 0.5 scale and reports it");
        var full=reactions with{Recipes=reactions.Recipes.ToDictionary(p=>p.Key,p=>p.Key=="elastomerSandwich"?p.Value with{ThreatScale=null}:p.Value)};
        var fullNera=new CassetteModel(nera.Preset,materials,full);
        check(Eval(fullNera,ap).WorkJ>apResult.WorkJ,"scaled NERA full-bore work is lower than the unscaled recipe");
        var headOn=CassetteModel.TiltNormal(Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,nera.Groups[0]);
        var apHeadOn=nera.Evaluate(nera.DepthMm,-headOn,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,ap,.02,energy,true);
        check(apHeadOn.ActiveLayers==0,"head-on NERA has no full-bore reaction inside its angle curve");
        var perfResult=Eval(perforated,ap);
        check(perfResult.WorkJ>0&&perfResult.ScaleNote=="perforated HHS \u00D70.5","perforated HHS halves full-bore AP and reports it");
        var fullPerf=new CassetteModel(perforated.Preset,materials,full with{Recipes=reactions.Recipes.ToDictionary(p=>p.Key,p=>p.Key=="perforatedHhs"?p.Value with{ThreatScale=null}:p.Value)});
        check(Eval(fullPerf,ap).WorkJ>perfResult.WorkJ,"scaled perforated work is lower than the unscaled recipe");
        check(CassetteModel.ScaleFor(reactions.Recipes["textoliteSandwich"],"kineticFullBore")==null,
            "a missing threatScale bucket means no reaction for that threat");
        check(CassetteModel.ScaleFor(reactions.Recipes["advancedSteel"],"longRod")==1,
            "a recipe without threatScale keeps the legacy threatTags behaviour");
        var he=new MaterialThreatTags("he","unknown");
        check(Eval(nera,he).ActiveLayers==0,"HE stays native-only against a cassette");
    }

    private static void HeshGap(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var decoupled=new CassetteModel(Preset(new("rha",3,0,"up"),new("air",20,0,"up"),new("elastomerNERA",15,0,"up"),new("rha",3,0,"up")),materials,reactions);
        var gap=decoupled.HeshRetention(120);
        check(Math.Abs(gap.GapMm-35)<1e-9&&Math.Abs(gap.Retention-.1)<1e-12,"HESH gap counts air and a >=10 mm non-steel layer before the last steel");
        var thin=new CassetteModel(Preset(new("rha",3,0,"up"),new("elastomerNERA",5,0,"up"),new("rha",3,0,"up")),materials,reactions);
        check(thin.HeshRetention(120).Retention==1&&thin.HeshRetention(120).GapMm==0,"a thin non-steel layer is not a HESH decoupling gap");
        var after=new CassetteModel(Preset(new("rha",3,0,"up"),new("rha",3,0,"up"),new("air",50,0,"up")),materials,reactions);
        check(after.HeshRetention(120).Retention==1,"air after the last steel layer does not weaken HESH");
        var wide=new CassetteModel(Preset(new("rha",3,0,"up"),new("air",30,0,"up"),new("rha",3,0,"up")),materials,reactions);
        check(Math.Abs(wide.HeshRetention(120).Retention-.1)<1e-12,"HESH retention floors at ten percent");
        check(wide.HeshRetention(240).Retention>wide.HeshRetention(120).Retention,"a larger calibre keeps more HESH penetration through the same gap");
    }

    private static void SpacedKinetic(Action<bool,string> check)
    {
        var s=SpacedKineticSettings.Default;
        ShellProfile Profile(string behavior,string kind="")=>new("test_"+behavior+kind,"test",new DartSettings(),Behavior:behavior,KineticKind:kind);
        check(SpallBalance.FollowsLayers(Profile("ap","apcbc")),"APCBC follows material layers");
        check(SpallBalance.FollowsLayers(Profile("ap","apcr")),"APCR follows material layers");
        check(SpallBalance.FollowsLayers(Profile("aphe")),"APHE follows material layers");
        check(SpallBalance.FollowsLayers(Profile("heat")),"HEAT still follows material layers");
        check(!SpallBalance.FollowsLayers(null),"vanilla AP (no mod profile) never follows material layers");
        check(!SpallBalance.FollowsLayers(Profile("he")),"HE has no layer follower");
        check(!SpacedKineticModel.Applies((ShellProfile?)null),"vanilla AP (no mod profile) is not in the spaced-kinetic path");
        check(SpacedKineticModel.Evaluate(s,"apfsds","apfsds",100,50,300).Retention==1,"APFSDS retention is one");
        check(SpacedKineticModel.Evaluate(s,"ap","apcr",100,50,0).Retention==1,"zero gap retention is one");
        check(SpacedKineticModel.Evaluate(s,"ap","ap",100,50,300).Retention==1,"bare AP and APHE have k zero");
        check(SpacedKineticModel.Evaluate(s,"aphe","aphe",100,50,300).Retention==1,"APHE has k zero");
        check(SpacedKineticModel.Evaluate(s,"heat","heat",100,50,300).Retention==1,"HEAT is unchanged");
        check(SpacedKineticModel.Evaluate(s,"hesh","hesh",100,50,300).Retention==1,"HESH is unchanged");
        var seam=SpacedKineticModel.Evaluate(s,"ap","apcbc",75,20,1);
        check(!seam.CapStrip&&seam.Retention==1,"a 1 mm weld seam gives retention one and no cap strip");
        var seamGate=new SpacedKineticGate();var seamShot=new IntPtr(7712);
        check(!SpacedKineticModel.TryClaim(seamGate,seamShot,seam),"a 1 mm weld seam does not claim the once-per-shot gate");
        var thinSkirt=SpacedKineticModel.Evaluate(s,"ap","apcbc",75,8,225);
        check(!thinSkirt.CapStrip&&Math.Abs(thinSkirt.Retention-(1-s.Apcbc*SpacedKineticModel.Scale(75,8,225)))<1e-12,
            "an 8 mm skirt versus 75 mm (plate scale 0.43) gives yaw/k but keeps the APCBC cap");
        var capPlate=SpacedKineticModel.Evaluate(s,"ap","apcbc",75,20,112.5);
        check(capPlate.CapStrip&&capPlate.Retention==1,"a 20 mm plate with a 1.5-calibre gap reaches scale 0.5 and strips the cap");
        check(Math.Abs(SpacedKineticModel.Scale(75,20,112.5)-.5)<1e-12,"the cap-strip example is exactly at the half-scale threshold");
        var apcbc=Profile("ap","apcbc");var spall=new SpallSettings();
        check(Math.Abs(SpacedKineticModel.KineticObliquityExponent(apcbc,spall,false)-1.15)<1e-12,
            "an unstripped APCBC keeps its own obliquity exponent");
        check(Math.Abs(SpacedKineticModel.KineticObliquityExponent(apcbc,spall,true)-spall.ApObliquityExponent)<1e-12,
            "a cap-stripped shot uses the AP exponent for the rest of the shot");
        var apcr=SpacedKineticModel.Evaluate(s,"ap","apcr",100,25,300);
        check(Math.Abs(apcr.Retention-.85)<1e-12&&!apcr.CapStrip,"APCR reaches its 0.15 floor at a fully developed gap");
        check(SpacedKineticModel.TryClaim(seamGate,seamShot,apcr),"a real skirt plate after the seam still claims the gate");
        var apds=SpacedKineticModel.Evaluate(s,"ap","apds",100,25,300);
        check(Math.Abs(apds.Retention-.97)<1e-12,"APDS reaches its 0.03 floor at a fully developed gap");
        var floorOk=true;var monotone=true;var previous=1d;
        for(var i=0;i<=100;i++)
        {
            var gap=i*3d;var effect=SpacedKineticModel.Evaluate(s,"ap","apcr",100,25,gap);
            floorOk&=effect.Retention>=1-s.Apcr-1e-12;
            monotone&=effect.Retention<=previous+1e-12;previous=effect.Retention;
        }
        check(floorOk&&monotone,"APCR retention is monotone and never below one minus k");
        var cap=SpacedKineticModel.Evaluate(s,"ap","apcbc",100,25,300);
        check(cap.CapStrip&&cap.Retention==1,"APCBC loses its cap instead of taking a flat loss");
        var fallback=SpacedKineticModel.Evaluate(s with{CapStrip=false},"ap","apcbc",100,25,300);
        check(!fallback.CapStrip&&fallback.Retention>=.97-1e-12,"APCBC flat fallback is at most 0.03");
        var gate=new SpacedKineticGate();var sim=new IntPtr(7711);
        check(gate.TryClaim(sim)&&!gate.TryClaim(sim),"spaced kinetic pays once per shot");
        gate.Clear();check(gate.TryClaim(sim),"spaced kinetic gate resets with a new impact session");
    }

    private static void SpallConfig(Action<bool,string> check)
    {
        var withObject=new SpallSettings{SpacedKinetic=new()};
        var json=SpallBalance.ToJson(withObject);
        check(json.Contains("\"spacedKinetic\"",StringComparison.Ordinal),"spall JSON writes the optional spacedKinetic object");
        check(SpallBalance.Parse(json)==withObject,"spacedKinetic survives a full spall JSON round trip");
        var without=JsonNode.Parse(SpallBalance.ToJson(new SpallSettings()))!.AsObject();
        check(!without.ContainsKey("spacedKinetic"),"a plain spall JSON omits the optional spacedKinetic object");
        check(SpallBalance.Parse(without.ToJsonString()).SpacedKinetic==null,"a missing spacedKinetic object parses as defaults-in-code");
        var invalid=JsonNode.Parse(json)!.AsObject();invalid["spacedKinetic"]!["ap"]=2;
        bool rejected=false;try{SpallBalance.Parse(invalid.ToJsonString());}catch(FormatException){rejected=true;}
        check(rejected,"out-of-range spacedKinetic value is rejected");
        invalid=JsonNode.Parse(json)!.AsObject();invalid["spacedKinetic"]!["unknown"]=1;
        rejected=false;try{SpallBalance.Parse(invalid.ToJsonString());}catch(FormatException){rejected=true;}
        check(rejected,"unknown spacedKinetic field is rejected");
    }

    private static CassettePreset Preset(params CassetteLayer[] layers)=>new("fix02211","0.22.11 test","1980.01.01",1,4000,layers);
}
