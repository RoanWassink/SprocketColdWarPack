using System.Numerics;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

// 0.22.12 addenda: a cassette in any combined-block member pays at its first
// entry; direct and blended paths share one threat input; every original
// projectile above the calibre gate detonates ERA with a per-type effect.
internal static class Fix02212AddendaTests
{
    internal static void Run(Action<bool,string> check)
    {
        
        
        NeraTilt(check);
        EraClassification(check);
        EraEffects(check);
        EraReport(check);
        EraConfig(check);
        CassetteEffectiveAngle(check);
    }

    private static CassettePreset Preset(params CassetteLayer[] layers)=>
        new("fix02212","0.22.12 test","1980.01.01",1,4000,layers);

    private static void NeraTilt(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var nera=new CassetteModel(Preset(new("rha",3,30,"up"),new("elastomerNERA",15,30,"up"),
            new("rha",3,30,"up"),new("air",20,0,"up")),materials,reactions);
        var apds=new MaterialThreatTags("ap","unknown",new[]{"shortRod"},null,true,"apds");
        var energy=.5*19*1187*1187;
        var s40=MathF.Sin(40*MathF.PI/180);var c40=MathF.Cos(40*MathF.PI/180);
        // 40 degrees from the plate normal, but the NERA layers are tilted 30 up.
        var withTilt=Vector3.Normalize(new Vector3(0,-s40,-c40));
        var againstTilt=Vector3.Normalize(new Vector3(0,s40,-c40));
        var turned=Vector3.Normalize(new Vector3(s40,0,-c40));
        CassetteEvaluation Eval(Vector3 direction)=>nera.Evaluate(nera.DepthMm,direction,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,
            apds,.056,energy,true);
        var with=Eval(withTilt);var against=Eval(againstTilt);var side=Eval(turned);
        check(Math.Abs(with.EffectiveAngleDegrees-10)<.1&&with.WorkJ<against.WorkJ*.05,
            "0.22.12: a shot turned with the NERA tilt sees theta_eff 10 and almost no work ("+with.EffectiveAngleDegrees+")");
        check(Math.Abs(against.EffectiveAngleDegrees-70)<.1&&against.WorkJ>0,
            "0.22.12: a shot turned against the tilt sees theta_eff 70 and pays ("+against.EffectiveAngleDegrees+")");
        check(Math.Abs(side.EffectiveAngleDegrees-48.5)<.2&&side.WorkJ>0&&against.WorkJ>with.WorkJ,
            "0.22.12: theta_eff is per tilt group, not the 40 degree plate angle ("+side.EffectiveAngleDegrees+")");
        check(against.ScaleNote=="NERA \u00D70.5","0.22.12: NERA applies the APDS 0.5 scale exactly once");
        // Same inputs through the same shared entry give the same result.
        var repeat=Eval(againstTilt);
        check(repeat.WorkJ==against.WorkJ&&repeat.EffectiveAngleDegrees==against.EffectiveAngleDegrees,
            "0.22.12: repeated evaluation through the shared input is identical");
    }

    private static void EraClassification(Action<bool,string> check)
    {
        MaterialThreatTags T(string behavior,IReadOnlyList<string>? tags=null,string? kind=null,bool original=true)=>
            new(behavior,"unknown",tags,null,original,kind);
        check(ArmourResponseModel.EraThreat(T("ap",new[]{"shortRod"},kind:"apcr"))=="shortRod","APCR is shortRod");
        check(ArmourResponseModel.EraThreat(T("ap",new[]{"shortRod"},kind:"apds"))=="shortRod","APDS is shortRod");
        check(ArmourResponseModel.EraThreat(T("ap",new[]{"longRod"},kind:"apfsds"))=="longRod","APFSDS is longRod");
        check(ArmourResponseModel.EraThreat(T("aphe"))=="kineticFullBore","APHE is kineticFullBore");
        check(ArmourResponseModel.EraThreat(T("ap"))=="kineticFullBore","vanilla AP is kineticFullBore");
        check(ArmourResponseModel.EraThreat(T("heat"))=="chemicalJet"&&ArmourResponseModel.EraThreat(T("hesh"))=="hesh"&&
            ArmourResponseModel.EraThreat(T("he"))=="he","HEAT/HESH/HE buckets are distinct");
        check(ArmourResponseModel.EraThreat(T("ap",new[]{"shortRod"},kind:"apcr",original:false))=="fragment",
            "a spall fragment is never a trigger threat");
        check(T("ap",new[]{"shortRod"},kind:"apcr").ShellLabel()=="APCR"&&T("ap",new[]{"shortRod"},kind:"apds").ShellLabel()=="APDS"&&
            T("apfsds").ShellLabel()=="APFSDS"&&T("aphe").ShellLabel()=="APHE"&&
            T("ap").ShellLabel()=="AP"&&T("ap",kind:"apcbc").ShellLabel()=="APCBC"&&T("heat").ShellLabel()=="HEAT"&&
            T("hesh").ShellLabel()=="HESH"&&T("he").ShellLabel()=="HE",
            "report labels use the shell type, not the tag name");
        check(ArmourResponseModel.EraTriggerCalibre(37,20)&&ArmourResponseModel.EraTriggerCalibre(20,20)&&
            !ArmourResponseModel.EraTriggerCalibre(14.5,20)&&!ArmourResponseModel.EraTriggerCalibre(12.7,20),
            "0.22.12: 37/20 mm trigger an ERA cell; 14.5/12.7 mm stay native-only");
    }

    private static ArmourResponse Response(string kind,double intact,double disturbed)=>
        new("test_"+kind,kind,new[]{"test_"+kind},new DateTime(1980,1,1),"1980.01.01","candidate",
            new(4000,.5,.25,1),new(.5,intact,disturbed,0),
            new("declaredCassette",1,1000,0,0,new[]{new CurvePoint(0,1),new CurvePoint(90,1)},
                Array.Empty<CurvePoint>(),new[]{new CurvePoint(0,1),new CurvePoint(90,1)}),
            .1,null,null,null,EraThreatScale.DefaultFor(kind));
    private static ArmourResponseCatalogue Catalogue(params ArmourResponse[] responses)=>
        new(true,.95,.95,responses,new(new[]{"rha"},3,0,89,45,100,.35,.6),null,null,20);

    private static void EraEffects(Action<bool,string> check)
    {
        var heavy=Response("heavyEra",.8,.8);var light=Response("lightEra",.8,.8);
        var cfg=Catalogue(heavy,light);var shot=new ArmourShotState();
        double Apply(ArmourResponse response,string key,string threat,bool intact=true)=>
            ArmourResponseModel.Apply(cfg,response,shot,key,"ap",true,true,13,40,0,intact,threat:threat);
        check(Math.Abs(Apply(heavy,"l","longRod")-.8)<1e-9,"heavy ERA keeps the full long-rod recipe");
        check(Math.Abs(Apply(heavy,"s","shortRod")-.9)<1e-9,"heavy ERA pays half the rod loss against APDS");
        check(Apply(heavy,"f","kineticFullBore")==1&&shot.Applied.Contains("f"),
            "heavy ERA default pays nothing against full-bore AP: the cell is consumed, retention stays 1");
        var quarter=heavy with{ThreatScale=EraThreatScale.DefaultFor("heavyEra") with{KineticFullBore=.25}};
        check(Math.Abs(Apply(quarter,"fq","kineticFullBore")-.95)<1e-9,
            "an explicit kineticFullBore 0.25 still pays a quarter of the rod loss against full-bore");
        // Weight 0 (outside the effective angle): the cell still detonates and
        // is consumed, but the model adds no effect.
        var narrow=heavy with{Geometry=heavy.Geometry with{KineticAngularCurve=new[]{new CurvePoint(0,1),new CurvePoint(60,1)}}};
        var narrowShot=new ArmourShotState();
        double Narrow(string key,string threat,double angle)=>
            ArmourResponseModel.Apply(cfg,narrow,narrowShot,key,"ap",true,true,13,angle,0,true,threat:threat);
        check(Narrow("in","shortRod",40)<1,"inside the effective angle heavy ERA pays against a short rod");
        check(Narrow("out","shortRod",75)==1&&narrowShot.Applied.Contains("out"),
            "heavy ERA with a kinetic threat at weight 0 is consumed with retention 1 (no effect)");
        check(Apply(light,"la","longRod")==1&&Apply(light,"sa","shortRod")==1&&Apply(light,"fa","kineticFullBore")==1,
            "light ERA detonates but adds no kinetic penetration effect");
        check(Apply(heavy,"h","hesh")==1&&Apply(heavy,"e","he")==1,"HESH and HE detonate without an extra penetration effect");
        check(Apply(heavy,"spent","shortRod",false)==1,"a spent heavy cell gives no second kinetic effect");
        var scale=EraThreatScale.DefaultFor("heavyEra");var lightScale=EraThreatScale.DefaultFor("lightEra");
        check(scale.ShortRod==.5&&scale.KineticFullBore==0&&scale.LongRod==1&&
            lightScale.ShortRod==0&&lightScale.KineticFullBore==0,"per-kind ERA threat scales are the documented defaults");
        // A cell spent by APDS cannot pay again against HEAT.
        var cells=new EraCells();var cell=new EraCell(2212,296,1,0,0);
        check(cells.Consume(cell)&&!cells.Consume(cell),"a kinetic detonation consumes the cell exactly once");
        check(ArmourResponseModel.Apply(cfg,heavy,new(),"heat","heat",true,true,13,40,0,false)==1,
            "a cell spent by APDS gives no later HEAT effect");
        // The kinetic detonation claims the spaced seam: no second spaced rule.
        var gate=new SpacedKineticGate();var sim=new IntPtr(2212);
        gate.TryClaim(sim);
        var seam=SpacedKineticModel.Evaluate(SpacedKineticSettings.Default,"ap","apcr",37,25,300);
        check(!SpacedKineticModel.TryClaim(gate,sim,seam),"APCR through heavy ERA plus an air gap does not also pay the spaced plate");
        var fresh=new SpacedKineticGate();
        check(SpacedKineticModel.TryClaim(fresh,sim,seam),"a shot without an ERA claim still pays a real spaced plate");
        // Only a detonation that has an effect claims the seam. This mirrors the condition in
        // RuntimeArmourResponses (the runtime call itself needs the game); scale 0 leaves the spaced plate standing.
        bool ClaimsSeam(ArmourResponse eraResponse,string eraThreat,bool eraIntact)=>
            eraIntact&&ArmourResponseModel.KineticEraThreat(eraThreat)&&(eraResponse.ThreatScale?.For(eraThreat)??1)>0;
        var seamSim=new IntPtr(2213);
        var legacy=heavy with{ThreatScale=null};
        foreach(var (eraResponse,eraThreat,claims,label) in new[]
        {
            (light,"longRod",false,"light ERA against a long rod"),(light,"shortRod",false,"light ERA against a short rod"),
            (light,"kineticFullBore",false,"light ERA against full-bore"),(heavy,"longRod",true,"heavy ERA against a long rod"),
            (heavy,"shortRod",true,"heavy ERA against a short rod"),(heavy,"kineticFullBore",false,"heavy ERA against full-bore"),
            (quarter,"kineticFullBore",true,"heavy ERA with an explicit full-bore scale"),
            (legacy,"kineticFullBore",true,"an ERA response without a threatScale"),
            (heavy,"chemicalJet",false,"HEAT (not a kinetic threat)")
        })
        {
            var armed=new SpacedKineticGate();
            if(ClaimsSeam(eraResponse,eraThreat,true))armed.TryClaim(seamSim);
            check(SpacedKineticModel.TryClaim(armed,seamSim,seam)==!claims,
                label+(claims?" claims the spaced seam":" leaves the spaced plate standing"));
        }
        var spentGate=new SpacedKineticGate();
        if(ClaimsSeam(heavy,"longRod",false))spentGate.TryClaim(seamSim);
        check(SpacedKineticModel.TryClaim(spentGate,seamSim,seam),"a spent cell never claims the spaced seam");
    }

    private static void EraReport(Action<bool,string> check)
    {
        var hit=ArmourResponseModel.EraReactionText("Duplet panel (turret) \u00B7 layer 1","APDS","shortRod","heavyEra",.9,.5);
        check(hit=="ERA Duplet panel (turret) \u00B7 layer 1: APDS \u221210 % (\u00D70.5)","ERA report names the shell and scale: "+hit);
        var custom=ArmourResponseModel.EraReactionText("Kontakt-5","APDS","shortRod","heavyEra",.93,.7);
        check(custom=="ERA Kontakt-5: APDS \u22127 % (\u00D70.7)","ERA report shows the configured factor, not a fixed one: "+custom);
        var quarterText=ArmourResponseModel.EraReactionText("Kontakt-5","APCBC","kineticFullBore","heavyEra",.95,.25);
        check(quarterText=="ERA Kontakt-5: APCBC \u22125 % (\u00D70.25)","an explicit 0.25 full-bore factor is shown: "+quarterText);
        var plain=ArmourResponseModel.EraReactionText("Kontakt-5","APFSDS","longRod","heavyEra",.8,1);
        check(plain=="ERA Kontakt-5: APFSDS \u221220 %","a factor of 1 shows no factor text: "+plain);
        var jet=ArmourResponseModel.EraReactionText("Kontakt-5","HEAT","chemicalJet","heavyEra",.5,.5);
        check(jet=="ERA Kontakt-5: HEAT \u221250 %","a jet never shows a kinetic factor: "+jet);
        var fullBoreZero=ArmourResponseModel.EraReactionText("Kontakt-5","APCBC","kineticFullBore","heavyEra",1,0);
        check(fullBoreZero=="ERA Kontakt-5 detonated: no effect vs APCBC","scale 0 reports a detonation without effect: "+fullBoreZero);
        var no=ArmourResponseModel.EraReactionText("K-1","APFSDS","longRod","lightEra",1,0);
        check(no=="ERA K-1 detonated: no effect vs APFSDS","light ERA against KE reports a detonation, not unsupported: "+no);
        var hesh=ArmourResponseModel.EraReactionText("Kontakt-5","HESH","hesh","heavyEra",1,0);
        check(hesh=="ERA Kontakt-5 detonated: no effect vs HESH","HESH detonation text");
    }

    private static void EraConfig(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;
        var json=File.ReadAllText(Path.Combine(root,"sprocket.expandedballistics.responses.json"));
        var parsed=ArmourResponses.Parse(json);
        check(parsed.MinimumTriggerCalibreMm==20,"a missing trigger calibre defaults to 20 mm");
        var heavy=parsed.Responses.Single(r=>r.ResponseId=="kontakt5Active");
        check(heavy.ThreatScale is {} ts&&ts.ShortRod==.5&&ts.KineticFullBore==0&&ts.LongRod==1,
            "heavy responses carry the live shortRod 0.5 / kineticFullBore 0 defaults");
        var light=parsed.Responses.Single(r=>r.ResponseId=="kontakt1Active");
        check(light.ThreatScale is {} ls&&ls.ShortRod==0&&ls.KineticFullBore==0,"light responses default to no KE effect");
        var node=JsonNode.Parse(json)!;
        node["minimumTriggerCalibreMm"]=25;
        var entry=node["responses"]!.AsArray().First(n=>n!["responseId"]!.GetValue<string>()=="kontakt5Active")!;
        entry["threatScale"]=new JsonObject{["shortRod"]=.7};
        var merged=ArmourResponses.Parse(node.ToJsonString());
        check(merged.MinimumTriggerCalibreMm==25,"minimumTriggerCalibreMm is configurable");
        check(merged.Responses.Single(r=>r.ResponseId=="kontakt5Active").ThreatScale is {} m&&
            m.ShortRod==.7&&m.KineticFullBore==0&&m.LongRod==1,
            "a live threatScale bucket overrides only itself, defaults stay merged");
        entry["threatScale"]=new JsonObject{["shortRod"]=.7,["kineticFullBore"]=.25};
        var kept=ArmourResponses.Parse(node.ToJsonString());
        check(kept.Responses.Single(r=>r.ResponseId=="kontakt5Active").ThreatScale is {} kp&&
            kp.ShortRod==.7&&kp.KineticFullBore==.25&&kp.LongRod==1,
            "an explicit user kineticFullBore 0.25 survives the new default of 0");
        var bad=JsonNode.Parse(json)!;
        bad["responses"]!.AsArray().First(n=>n!["responseId"]!.GetValue<string>()=="kontakt5Active")!["threatScale"]=
            new JsonObject{["shortRod"]=2};
        var rejected=false;try{ArmourResponses.Parse(bad.ToJsonString());}catch(FormatException){rejected=true;}
        check(rejected,"an out-of-range threatScale override is rejected");
    }

    private static void CassetteEffectiveAngle(Action<bool,string> check)
    {
        var part=new CombatLayerPart(296,"Composite armour plate","ebCassette_nera","NERA - NERA cassette",133,40,true);
        var shot=new CombatReportShot(1,"Outgoing","APDS (tungsten, late)",1265,new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",446,null,"A",new CombatLayerInfo("A",new[]{part},false)),
            new CombatReportEvent("materialReaction","","Cassette NERA cassette: APDS \u221217 % (NERA \u00D70.5); \u03B8_eff \u2248 48,5\u00B0; front, t = 133 mm, \u03B8 = 40\u00B0",446,370,"A",null,296)
        },0,529.94);
        var view=CombatHitProjection.Build(CombatReportTruth.Normalize(shot)).Single();
        check(view.Reaction.Contains("\u03B8_eff 48,5\u00B0",StringComparison.Ordinal),
            "0.22.12: the cassette report shows theta_eff: "+view.Reaction);
        check(view.Reaction.Contains("front 133 mm @40\u00B0",StringComparison.Ordinal),
            "0.22.12: the same row keeps the first-entry side, thickness and angle");
    }
}
