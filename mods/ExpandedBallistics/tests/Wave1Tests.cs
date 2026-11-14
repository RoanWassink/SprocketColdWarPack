using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class Wave1Tests
{
    /// <summary>0.22.20: the rhaFactor the installer wrote for a user cassette equals the C# model's value exactly.</summary>
    internal static void VerifyUserRha(string game,string id)
    {
        var config=Path.Combine(game,"BepInEx/config");var techDir=Path.Combine(game,"Sprocket_Data/StreamingAssets/Technology");
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(config,MaterialReactionCatalogue.FileName)));
        var preset=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(config,CassetteCatalogue.FileName))).Presets.Single(p=>p.Id==id);
        var materials=CassetteTechnology.ReadDirectory(techDir.Replace("Technology","Technology"));
        var model=new CassetteModel(preset,materials,reactions);
        using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(techDir,preset.MaterialId+".json")));
        var written=doc.RootElement.GetProperty("properties").GetProperty("rhaFactor").GetDouble();
        if(written!=model.Passive.RhaFactor)throw new Exception($"user rha mismatch {id}: file {written:R} model {model.Passive.RhaFactor:R}");
        Console.WriteLine("USER RHA MATCH "+id+" "+written.ToString("R"));
    }
    internal static void VerifyInstall(string game)
    {
        var config=Path.Combine(game,"BepInEx/config");
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(config,MaterialReactionCatalogue.FileName)));
        var responses=ArmourResponses.Parse(File.ReadAllText(Path.Combine(config,"sprocket.expandedballistics.responses.json")));
        _=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(File.ReadAllText(Path.Combine(config,"sprocket.expandedballistics.experimental.json")));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(config,CassetteCatalogue.FileName)));
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(game,"Sprocket_Data/StreamingAssets/Technology"));
        // 0.22.20: the reset files must parse with the strict parsers too.
        var shells=Path.Combine(config,"sprocket.expandedballistics.shells.json");if(File.Exists(shells))_=ShellProfiles.Parse(File.ReadAllText(shells));
        var spall=Path.Combine(config,"sprocket.expandedballistics.spall.json");if(File.Exists(spall)){var s=SpallBalance.Parse(File.ReadAllText(spall));if(s.FragmentRicochet==null)throw new Exception("spall config has no fragmentRicochet");}
        foreach(var preset in presets.Presets)
        {var model=new CassetteModel(preset,materials,reactions);if(materials[preset.MaterialId]!=model.Passive)throw new Exception("Installed cassette passive mismatch: "+preset.Id);}
        foreach(var binding in reactions.Bindings.Values)if(materials.TryGetValue(binding.MaterialId,out var m)&&
            (m.Density!=binding.Density||m.RhaFactor!=binding.RhaFactor||m.SpallFactor!=binding.SpallFactor))throw new Exception("Installed binding mismatch: "+binding.MaterialId);
        foreach(var response in responses.Responses)foreach(var id in response.CompatibleMaterialIds)if(materials.TryGetValue(id,out var m)&&
            (m.Density!=response.PassiveMaterial.Density||m.RhaFactor!=response.PassiveMaterial.RhaFactor||m.SpallFactor!=response.PassiveMaterial.SpallFactor))throw new Exception("Installed response mismatch: "+id);
        Console.WriteLine($"INSTALL VERIFY PASS: {presets.Presets.Length} cassettes, {materials.Count} materials, {reactions.Bindings.Count} bindings, {responses.Responses.Count} responses; strict parsers and passive signatures.");
    }
    internal static void Run(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;
        var json=File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName));
        var catalogue=CassetteCatalogue.Parse(json);
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var responses=ArmourResponses.Parse(File.ReadAllText(Path.Combine(root,"sprocket.expandedballistics.responses.json")));
        check(responses.MaximumAdditionalHeatLoss==.85&&responses.MaximumAdditionalKineticLoss==.65,"wave1: production caps agree with live ERA balance");
        check(reactions.Work.RodLengthM==.12&&reactions.Recipes["textoliteSandwich"].ThreatTags.SequenceEqual(new[]{"chemicalJet"}),"wave1: live material calibration and textolite HEAT-only role");
        check(reactions.Recipes["hardSteel"].LossPerReference==.03&&reactions.Recipes["hardSteel"].MaximumLoss==.06,"wave1: HHS native hardness has only a small extra modifier");
        foreach(var id in new[]{"castArmour","highHardnessSteel","advancedArmourSteel","structuralSteel"})check(responses.Preconditioning.SteelIds.Contains(id)&&reactions.SteelMaterialIds.Contains(id),"wave1: aligned steel eligibility "+id);
        foreach(var b in reactions.Bindings.Values)
        {var m=materials[b.MaterialId];check(b.Density==m.Density&&b.RhaFactor==m.RhaFactor&&b.SpallFactor==m.SpallFactor,"wave1: exact Technology/binding "+b.MaterialId);}
        foreach(var r in responses.Responses)foreach(var id in r.CompatibleMaterialIds)if(materials.TryGetValue(id,out var m))
            check(r.PassiveMaterial.Density==m.Density&&r.PassiveMaterial.RhaFactor==m.RhaFactor&&r.PassiveMaterial.SpallFactor==m.SpallFactor,"wave1: exact Technology/response "+id);
        foreach(var id in new[]{"cwepNeraCassette","cwepPassiveComposite","varmaComposite"})check(materials[id].SpallFactor==.0008,"wave1: steel rear-plate spall "+id);
        void Reject(Action<JsonObject> edit,string name)
        {
            var node=JsonNode.Parse(json)!.AsObject();edit(node);bool failed=false;
            try{CassetteCatalogue.Parse(node.ToJsonString());}catch(FormatException){failed=true;}
            check(failed,"cassette parser rejects "+name);
        }
        Reject(x=>x["schemaVersion"]=2,"schema");Reject(x=>x["surprise"]=true,"unknown property");
        Reject(x=>x["presets"]![0]!["window"]!["minDepthMm"]=3999,"inverted window");
        Reject(x=>x["presets"]![0]!["layers"]![0]!["tiltDegrees"]=76,"tilt above 75");
        Reject(x=>x["presets"]![0]!["layers"]![0]!["thicknessMm"]=0,"zero thickness");
        Reject(x=>x["presets"]![0]!["layers"]![0]!["tiltDirection"]="north","unknown direction");
        Reject(x=>x["presets"]![0]!["date"]="1980.02.30","calendar date");
        Reject(x=>x["presets"]![0]!["id"]="../evil","path id");
        Reject(x=>x["presets"]!.AsArray().Add(x["presets"]![0]!.DeepClone()),"duplicate id");
        Reject(x=>x["presets"]![0]!["layers"]=new JsonArray(new JsonObject{["material"]="air",["thicknessMm"]=10,["tiltDegrees"]=0,["tiltDirection"]="up"}),"air only");
        bool duplicate=false;try{CassetteCatalogue.Parse(json.Replace("\"schemaVersion\": 1","\"schemaVersion\": 1, \"schemaVersion\": 1"));}catch(FormatException){duplicate=true;}
        check(duplicate,"cassette parser rejects duplicate fields");
        check(catalogue.Presets.Length==35,"nine required cassette presets plus the six hull packages (0.22.7) plus twenty more (0.22.19)");
        var immutableId="ebCassette_nera";
        var editor=new SprocketExpandedBallistics.Materials.MaterialEditorDraft(Path.Combine(root,"NativeData"),Path.Combine(root,"nonexistent-response-immutable.json"),Path.Combine(root,"nonexistent-label-immutable.json"),
            new[]{(immutableId,"NERA cassette",Path.Combine(root,"NativeData",immutableId+".json"))},File.ReadAllText(Path.Combine(root,"sprocket.expandedballistics.responses.json")));
        check(editor.Items.Count==0,"ordinary scalar material editor cannot mutate immutable cassette identities");
        CassettePreset Preset(CassetteLayer[] layers)=>new("test","Test","1980.01.01",1,4000,layers);
        var sample=new CassetteModel(Preset(new[]{new CassetteLayer("rha",3,30,"up"),new("elastomerNERA",15,30,"up"),new("rha",3,30,"up"),new("air",20,0,"up")}),materials,reactions);
        double depth=21/Math.Cos(Math.PI/6)+20,mass=(6*7850+15*1200)/Math.Cos(Math.PI/6)/1000;
        check(Math.Abs(sample.DepthMm-depth)<1e-10&&Math.Abs(sample.MassKgM2-mass)<1e-10,"cassette depth and projected mass include tilt and air");
        check(Math.Abs(sample.Passive.RhaFactor-(6+15*.07)/Math.Cos(Math.PI/6)/depth)<1e-10&&sample.Passive.SpallFactor==materials["rha"].SpallFactor,"cassette depth-weighted RHA and rear solid spall");
        double expectedCost=1.2*(6*7850*materials["rha"].CostMultiplier+15*1200*materials["elastomerNERA"].CostMultiplier)/(6*7850+15*1200);
        check(Math.Abs(sample.Passive.CostMultiplier-expectedCost)<1e-10,"cassette mass-weighted cost and assembly premium");
        check(sample.Layers[1].Supported&&sample.Layers[1].NeraAirFactor==1,"NERA steel skins and following cavity prepared once");
        check(CassetteModel.NeraAirFactor(0,15)==.3&&Math.Abs(CassetteModel.NeraAirFactor(7.5,15)-.65)<1e-12&&CassetteModel.NeraAirFactor(30,15)==1,"NERA air factor 30%, linear, saturated");
        var tilt=new CassetteTilt(.5,Math.Sqrt(.75),"up");var normal=CassetteModel.TiltNormal(Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,tilt);
        check(normal.Y>.49&&Math.Abs(normal.Length()-1)<1e-6,"positive up tilt rotates normal toward vehicle up");
        var with=new Vector3(0,.5f,-MathF.Sqrt(.75f));var against=new Vector3(0,-.5f,-MathF.Sqrt(.75f));
        check(Math.Abs(CassetteModel.EffectiveAngle(with,normal)-60)<.001&&CassetteModel.EffectiveAngle(against,normal)<.03,"with/against tilt uses effective 60/0 degree incidence");
        var roof=CassetteModel.TiltNormal(Vector3.UnitY,Vector3.UnitY,Vector3.UnitZ,tilt);
        check(float.IsFinite(roof.Length())&&Math.Abs(roof.Length()-1)<1e-6,"roof fallback axis remains normalized");
        check(CassetteModel.EffectiveAngle(Vector3.UnitX,Vector3.UnitZ)==80,"cassette grazing angle capped at 80 degrees");
        var left=CassetteModel.TiltNormal(Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,tilt with{Direction="left"});
        var right=CassetteModel.TiltNormal(Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,tilt with{Direction="right"});
        check(Math.Abs(left.X+right.X)<1e-7&&Math.Abs(left.X)>.49&&Math.Abs(left.Y)<1e-7,"left/right use the orthogonal in-plane tilt axis with mirrored signs");
        double energy=.5*17500*.6*1600*1600*Math.PI*.25*.02*.02;
        CassetteEvaluation Eval(CassetteModel m,string threat="chemicalJet",bool intact=true,double? d=null)=>m.Evaluate(d??m.DepthMm,Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,threat,.02,energy,intact);
        var noAir=new CassetteModel(Preset(sample.Preset.Layers.Take(3).ToArray()),materials,reactions);
        check(Math.Abs(Eval(noAir).JetWorkMm/Eval(sample).JetWorkMm-.3)<1e-10,"no-air NERA retains exactly 30 percent capacity");
        var repeat=new CassetteModel(Preset(sample.Preset.Layers.Concat(sample.Preset.Layers).ToArray()),materials,reactions);
        check(Math.Abs(repeat.Layers[4].RepeatFactor-.85)<1e-12&&Math.Abs(Eval(repeat).JetWorkMm/Eval(sample).JetWorkMm-1.85)<1e-10,"successive same recipe diminishes by 0.85");
        var noBacking=new CassetteModel(Preset(new[]{new CassetteLayer("elastomerNERA",15,30,"up"),new("rha",3,30,"up")}),materials,reactions);
        check(!noBacking.Layers[0].Supported&&Eval(noBacking).ActiveLayers==0,"missing front steel disables sandwich extra work");
        var backed=new CassetteModel(Preset(new[]{new CassetteLayer("ceramicArray",40,0,"up"),new("rha",20,0,"up")}),materials,reactions);
        check(backed.HasBacked&&Math.Abs(Eval(backed,intact:false).JetWorkMm/Eval(backed).JetWorkMm-.2)<1e-12,"backed cassette damage uses ceramic degradedFraction");
        var classical=Eval(backed,"kineticFullBore");
        var dose=MaterialWorkMath.ClassicDose(reactions.Recipes["ceramicStrikeFace"],40,0,1,true);
        check(classical.JetWorkMm==0&&Math.Abs(classical.TotalRodWorkJ/energy-(1-Math.Pow(1/(1+dose),2/1.43)))<1e-12,"classical AP cassette uses the loose-layer cooperative dose and energy conversion");
        bool rejectedEra=false;try{_=new CassetteModel(Preset(new[]{new CassetteLayer("rha",20,0,"up")}),materials,reactions,new HashSet<string>{"rha"});}catch(FormatException){rejectedEra=true;}
        check(rejectedEra,"ERA constituents rejected by exact response classification");
        bool unknownMaterial=false;try{_=new CassetteModel(Preset(new[]{new CassetteLayer("missing",20,0,"up")}),materials,reactions);}catch(FormatException){unknownMaterial=true;}
        check(unknownMaterial,"unknown cassette layer material fails loading");
        var limited=sample.Preset with{MinimumDepthMm=sample.DepthMm*.75,MaximumDepthMm=sample.DepthMm*1.25};var windowed=new CassetteModel(limited,materials,reactions);
        check(!Eval(windowed,d:sample.DepthMm*.7).InWindow&&Eval(windowed,d:sample.DepthMm*1.1).InWindow,"cassette depth window fails native-only outside range");
        foreach(var p in catalogue.Presets)
        {
            var m=new CassetteModel(p,materials,reactions);var native=materials[p.MaterialId];
            check(native==m.Passive,"generator exact native signature "+p.Id);
            var generated=JsonNode.Parse(CassetteTechnology.Generate(m))!;check(generated["type"]!.GetValue<string>()==p.MaterialId&&generated["date"]!.GetValue<string>()==p.Date,"generator identity/date "+p.Id);
            var jet=Eval(m);var rod=Eval(m,"longRod");check(jet.JetWorkMm<=.75*reactions.Work.JetBudgetMm+1e-9&&rod.TotalRodWorkJ<=.75*energy+1e-9,"cassette global work caps "+p.Id);
            (double k0,double k1,double h0,double h1,double angle) band=p.Id switch{"t64k"=>(400,480,500,620,68),"burlington"=>(480,560,640,720,0),"leopard2a4"=>(400,500,700,800,0),"t72b"=>(450,520,800,950,0),"t80u"=>(500,550,850,950,0),"m1a1ha"=>(550,650,850,1000,0),"t72m1"=>(480,560,600,700,0),_=>(0,0,0,0,0)};
            if(band.k1>0)
            {
                var radians=band.angle*Math.PI/180;var ray=new Vector3(0,(float)Math.Sin(radians),(float)-Math.Cos(radians));
                var hw=m.Evaluate(m.DepthMm,ray,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,"chemicalJet",.02,energy,true);
                var kw=m.Evaluate(m.DepthMm,ray,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,"longRod",.02,energy,true);
                var nativeProtection=m.DepthMm*m.Passive.RhaFactor/Math.Cos(radians);var heat=nativeProtection+hw.JetWorkMm;
                var ke=600*(1-Math.Pow(Math.Max(0,Math.Pow(Math.Max(0,1-nativeProtection/600),2/1.43)-kw.TotalRodWorkJ/energy),1.43/2));
                bool fits=heat>=band.h0&&heat<=band.h1&&ke>=band.k0&&ke<=band.k1;
                check(fits==(p.Id is not ("t72b" or "t80u")),"preset meets band or explicit PRESETS/TUNING deviation: "+p.Id+" (heat "+heat.ToString("0")+", ke "+ke.ToString("0")+")");
            }
        }
        var recipe=reactions.Recipes["elastomerSandwich"];
        var maximum=MaterialWorkMath.Calculate(recipe,reactions.Work,true,1000,80,1,true,.02);
        double referenceEnergy=.5*reactions.Work.RodDensityKgM3*reactions.Work.RodLengthM*Math.Pow(reactions.Work.RodSpeedMps,2)*Math.PI*.25*.02*.02;
        check(Math.Abs(maximum.Joules/referenceEnergy-recipe.MaximumLoss/(1-recipe.MaximumLoss))<1e-12,"maximumLoss bounds cooperative f*S in dose units");
        double referenceLoss=(recipe.LossPerReference/(1-recipe.LossPerReference)*1.6)/(1+recipe.LossPerReference/(1-recipe.LossPerReference)*1.6);
        double old=referenceEnergy*(1-Math.Pow(1-referenceLoss,2/1.43))*MaterialReactionModel.CooperativeAngle(recipe,60);
        check(Math.Abs(MaterialWorkMath.Calculate(recipe,reactions.Work,true,15,60,1,true,.02).Joules-old)<1e-9,"maximumLoss preserves live work at reference thickness");
        foreach(var cooperative in reactions.Recipes.Values.Where(r=>r.Mode is "backed" or "sandwich"))foreach(bool isRod in new[]{false,true})
        {
            double rd=cooperative.LossPerReference/(1-cooperative.LossPerReference)*(isRod?1.6:1),rl=rd/(1+rd),f=isRod?1-Math.Pow(1-rl,2/1.43):rl;
            double oldCapacity=(isRod?referenceEnergy:reactions.Work.JetBudgetMm)*f*MaterialReactionModel.CooperativeAngle(cooperative,60);
            var computed=MaterialWorkMath.Calculate(cooperative,reactions.Work,isRod,cooperative.ReferenceThicknessMm,60,1,true,.02);
            check(Math.Abs((isRod?computed.Joules:computed.JetMm)-oldCapacity)<1e-9,"reference work unchanged by cap: "+cooperative.Id+(isRod?" rod":" jet"));
        }
        var timeline=new[]{new VehicleEraBoundary("Coldwar1",new(1945,9,4)),new("Coldwar2",new(1960,1,1)),new("Coldwar3",new(1975,1,1))};
        check(VehicleEraAvailability.AllowsMaterial(DateTime.MaxValue,timeline,new DateTime(2009,1,1),true,true)&&!VehicleEraAvailability.AllowsMaterial(new DateTime(2008,12,31),timeline,new DateTime(2009,1,1),true,true),"final Coldwar3 sentinel allows Duplet 2009 while 2008.12.31 does not");
        var names=timeline.Select(e=>e.Name).ToArray();var starts=timeline.Select(e=>new ShellNativeDate(e.Start.Year,e.Start.Month,e.Start.Day)).ToArray();
        var context=new ShellEraContext(ShellNativeDate.Maximum,names,starts,2,new HashSet<string>());
        check(ShellEraPolicy.Valid(context)&&!ShellEraPolicy.Valid(context with{Index=0}),"shell sentinel follows Coldwar3 and rejects old final index");
        check(!ShellDatePolicy.ValidTimeline(new[]{starts[1],starts[0]})&&!ShellDatePolicy.ValidTimeline(new[]{ShellNativeDate.Maximum}),"era order and finite boundaries are mandatory");
        SummaryTests(check);
        CassetteFixTests.Run(check,materials,reactions);
        CassetteImpactPathTests.Run(check,materials,reactions);
        // Warm JIT and measure only evaluation, excluding report strings/native IO.
        var bench=new CassetteModel(catalogue.Presets.Single(p=>p.Id=="leopard2a4"),materials,reactions);
        double sink=0;for(int i=0;i<20000;i++)sink+=Eval(bench,"longRod").WorkJ;
        long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
        for(int i=0;i<200000;i++)sink+=Eval(bench,"longRod").WorkJ;
        watch.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        check(allocated<=64,"cassette evaluation allocates zero bytes after JIT (stopwatch only)");
        var ns=watch.Elapsed.TotalMilliseconds*1e6/200000;
        string result=$"12 NERA cores / {bench.Layers.Length} solid layers, 200000 evaluations, {ns:0.0} ns/evaluation, {allocated} bytes total including Stopwatch; sink={sink:R}. Pure Release net8 evaluation, no native bridge or feed formatting.\n";
        var output=Environment.GetEnvironmentVariable("WAVE1_OUT");if(output!=null){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"MICROBENCHMARK.txt"),result);}Console.Write(result);
    }
    private static void SummaryTests(Action<bool,string> check)
    {
        var events=new List<CombatReportEvent>();
        void Layer(string id,int entity,double before,double after)
        {
            var layer=new CombatLayerInfo(id,new[]{new CombatLayerPart(entity,"Plate #"+entity,"rha","RHA",20,0,true)},false);
            events.Add(new("layerStart","","Reached",before,HitId:id,Layer:layer));events.Add(new("plateResidual","","Passed",before,after,id));
        }
        Layer("a",1,600,550);Layer("b",2,550,500);
        var shot=new CombatReportShot(14,"Incoming","APFSDS <size=99>unsafe",1600,events,InitialPenetrationMm:600);
        var summary=CombatHitSummaryModel.Build(shot);
        check(summary.Detail=="staal \u2212100"&&summary.Header.Contains("600 \u2192 500 mm DOOR"),"hit summary merges Passed rows into one native steel loss");
        var legacy=shot with{Events=new[]{new CombatReportEvent("plateResidual","","Passed A",600,550),new("plateResidual","","Passed B",550,500)}};
        check(CombatHitSummaryModel.Build(legacy).Detail=="staal \u2212100","older Passed events without hit ids are merged once");
        check(summary.Colour=="#79D8E8"&&!summary.RichText.Contains("<size"),"hit summary applies category and escapes TMP user labels");
        events.Insert(1,new("materialReaction","","Cassette Sample: work",600,580,"a"));
        summary=CombatHitSummaryModel.Build(shot);check(summary.Colour=="#C8A3F3"&&summary.Detail.Contains("staal \u221280"),"material priority and exact block extras do not double-count native loss");
        events.Add(new("nativeStop","","Stopped",500,HitId:"b"));
        summary=CombatHitSummaryModel.Build(shot);check(summary.Colour=="#C8A3F3"&&summary.Detail.Contains("Plaat #2")&&summary.Header.Contains("GESTOPT"),"material priority exceeds native stop and identifies stop plate");
        events.Add(new("eraResidual","","ERA",600,590,"a"));check(CombatHitSummaryModel.Build(shot).Colour=="#F3C66A","ERA priority exceeds material and stop");
        check(CombatHitSummaryModel.Build(shot,35).Header.Length<=35&&CombatHitSummaryModel.Build(shot,35).Detail.Length<=35&&CombatHitSummaryModel.Fit("abc",1)=="\u2026","hit summary truncates to width");
        var ricochet=new CombatReportShot(1,"Incoming","AP",800,new[]{new CombatReportEvent("outcome","","Deflected at plate")});
        check(CombatHitSummaryModel.Build(ricochet).Colour=="#A5ADB5","ricochet category is gray");
        var buffer=new DamageFeedBuffer();for(int i=0;i<8;i++)buffer.AddHit(i*.1,summary);
        check(buffer.Visible(1).Count==6,"HUD holds six two-line hits");long revision=buffer.Revision;buffer.Visible(1);check(buffer.Revision==revision,"HUD unchanged buffer is clean");buffer.Visible(10);check(buffer.Revision>revision&&buffer.Visible(10).Count==0,"HUD expiration marks text/layout dirty");
    }
}
