using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

internal static class Fix02217Tests
{
    private static string Html(CombatReportShot[] shots){var h=new CombatReportHistory();h.Begin();foreach(var s in shots)h.Add(s);return CombatReportHtml.Render(h);}
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var assert=check;check=(ok,text)=>{assert(ok,"fix02217: "+text);count++;};
        var incoming=new CombatReportShot(1,"Incoming","HEAT",900,Array.Empty<CombatReportEvent>(),Shooter:"T-80U");
        check(CombatReportTruth.Normalize(incoming).Shooter=="T-80U","shooter name retained");
        foreach(var name in new string?[]{null,"","  ","\n\t"})
        {
            var shot=CombatReportTruth.Normalize(incoming with{Shooter=name});
            check(shot.Shooter=="unknown","unresolved shooter fallback");
            check(HitBlockBuilder.Build(shot).Shooter=="From: unknown","HUD fallback");
            check(Html(new[]{shot}).Contains("From: unknown"),"HTML fallback");
        }
        check(HitBlockBuilder.Build(incoming).Shooter=="From: T-80U","HUD shooter");
        check(CombatReportView.Build(new[]{incoming})[0].Summary.StartsWith("From: T-80U",StringComparison.Ordinal),"Kit shooter");
        check(Html(new[]{incoming}).Contains("From: T-80U"),"HTML shooter");
        check(CombatReportTruth.Normalize(incoming with{Shooter="  T-80U\n"}).Shooter=="T-80U","name sanitized");
        check(!Html(new[]{incoming with{Shooter="<script>"}}).Contains("<script>"),"HTML shooter escaped");
        check(!HitBlockBuilder.Build(incoming with{Direction="Outgoing"}).Shooter.Any(),"outgoing HUD does not claim incoming shooter");
        var round=JsonSerializer.Deserialize<CombatReportShot>(JsonSerializer.Serialize(incoming))!;
        check(round.Shooter==incoming.Shooter,"shooter persists in saved report");
        check(!CombatReportTruth.LaunchMatches(1,2,"a",1,3,"a")&&!CombatReportTruth.LaunchMatches(1,2,"a",2,2,"a")&&!CombatReportTruth.LaunchMatches(1,2,"a",1,2,"b"),"pooled projectile identity rejects stale owner");

        foreach(var angle in new[]{0d,30,60})
        {
            double cos=Math.Cos(angle*Math.PI/180),los=100/cos;
            check(Math.Abs(CassetteProtectionComparison.Value(los,100,785,angle,1)!.Value-1)<1e-12,"same LOS thickness RHA = 1");
            check(Math.Abs(CassetteProtectionComparison.Value(los,100,785,angle,2)!.Value-1)<1e-12,"same weight RHA = 1 at selected angle");
            check(Math.Abs(CassetteProtectionComparison.Value(los,100,392.5,angle,2)!.Value-2)<1e-12,"half mass doubles mass efficiency");
        }
        check(CassetteProtectionComparison.Value(150,100,785,60,0)==150,"mm RHA is unchanged");
        check(CassetteProtectionComparison.Value(1,0,0,0,1)==null&&CassetteProtectionComparison.Value(1,100,0,0,2)==null,"empty references unavailable");
        check(CassetteProtectionComparison.Value(1,100,100,90,1)==null,"invalid angle unavailable");
        var root=AppContext.BaseDirectory;
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName)));
        var backend=new CassetteComposer(materials,reactions,"unused","unused",_=>false);
        var model=new CassetteComposerModel(backend,materials.Keys.Select(id=>new ComposerMaterial(id,id,"Other")).ToArray(),Array.Empty<ComposerMaterial>(),()=>presets.Presets,_=>false,_=>true,false);
        model.LoadPreset("t64k");
        foreach(int index in new[]{0,1,2})
        {
            model.AngleIndex=index;var p=model.Preview!;var angle=CassetteComposerModel.Angles[index];
            double los=p.DepthMm/Math.Cos(angle*Math.PI/180);
            check(Math.Abs(model.LosThicknessMm-los)<1e-8,"derived LOS D/cos selected angle");
            check(model.Derived(7)==los.ToString("0.#",CultureInfo.InvariantCulture)+" mm","derived LOS row follows angle cache");
            foreach(bool heat in new[]{false,true})foreach(bool side in new[]{false,true})
            {
                var raw=model.TableValue(index,(heat?2:0)+(side?0:1))!.Value;
                check(model.ProtectionValue(heat,side,0)==raw,"selected angle and threat in table");
                check(Math.Abs(model.ProtectionValue(heat,side,1)!.Value-raw/los)<1e-10,"table thickness efficiency");
                check(Math.Abs(model.ProtectionValue(heat,side,2)!.Value-raw/(p.KgM2*1000/backend.RhaDensity/Math.Cos(angle*Math.PI/180)))<1e-10,"table mass efficiency");
            }
        }
        check(!model.AnyTilted,"untilted preset hides tilt columns");
        model.LoadPreset("nera");check(model.AnyTilted,"tilted preset exposes tilt columns");

        CombatReportEvent Noise(int entity,string hit="h",string stage="")=>new("materialUnavailable",stage,$"Skipped composite plate #{entity}: line ended before another solid block; cassette and passive resistance not applied.",HitId:hit,RelatedEntity:entity);
        var events=Enumerable.Range(0,30).Select(i=>Noise(296,i.ToString())).ToArray();
        var collapsed=CombatReportNoise.Collapse(events);
        check(collapsed.Count==1&&collapsed[0].Occurrences==30&&collapsed[0].Text.EndsWith("(30 occurrences)",StringComparison.Ordinal),"30 diagnostics become one counted row");
        check(collapsed[0].HitId==null&&collapsed[0].RelatedEntity==296,"aggregate does not claim one fragment hit");
        check(CombatReportNoise.Collapse(collapsed).SequenceEqual(collapsed),"collapse idempotent");
        check(CombatReportNoise.Collapse(new[]{Noise(296),Noise(297),Noise(296,stage:"Precursor")}).Count==3,"separate plate and tandem stage retained");
        var real=new CombatReportEvent("nativeStop","","Stopped in plate");
        check(CombatReportNoise.Collapse(new[]{Noise(296),real,Noise(296)}).SequenceEqual(new[]{CombatReportNoise.Collapse(new[]{Noise(296),Noise(296)})[0],real}),"other events and ordering retained");
        var normalized=CombatReportTruth.Normalize(incoming with{Events=Enumerable.Repeat(Noise(296),300).Append(real).ToArray()});
        check(normalized.Events.Count==2&&normalized.Events[0].Occurrences==300&&normalized.OmittedEvents==0,"collapse before cap preserves real outcome");
        check(CombatReportTruth.Normalize(normalized).Events.SequenceEqual(normalized.Events),"history normalization keeps count");
        check(Html(new[]{normalized}).Contains("300 occurrences"),"HTML shows count");
        Tilt(check);Ricochet(check);Muzzle(check);
        Console.WriteLine($"0.22.17 checks PASS: {count} checks (points 1-9, round 2 included).");
    }
    private static CassettePreset Preset(params CassetteLayer[] layers)=>new("fix02217","0.22.17 test","1980.01.01",1,4000,layers);
    private static (IReadOnlyDictionary<string,CassetteMaterial> Materials,MaterialReactionCatalogue Reactions) Data()
    {
        var root=AppContext.BaseDirectory;
        return (CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData")),MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName))));
    }
    // ---------------- point 4: tilt, raw angle, option A taper ----------------
    private static void Tilt(Action<bool,string> check)
    {
        var (materials,reactions)=Data();var heat=new MaterialThreatTags("heat","unknown");
        double energy=.5*17500*.6*1600*1600*Math.PI*.25*.02*.02;
        check(CassetteModel.AngleText(79.4)=="79.4\u00B0"&&CassetteModel.AngleText(80)=="\u2265 80\u00B0"&&CassetteModel.AngleText(97)=="\u2265 80\u00B0","angle text: raw below 80, at least 80 above");
        var nera=new CassetteModel(Preset(new("rha",3,57,"up"),new("elastomerNERA",15,57,"up"),new("rha",3,57,"up"),new("air",20,0,"up")),materials,reactions);
        CassetteEvaluation Eval(CassetteModel m,double a)
        {
            var r=a*Math.PI/180;
            return m.Evaluate(m.DepthMm,new Vector3(0,(float)Math.Sin(r),(float)-Math.Cos(r)),Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,heat,.02,energy,true);
        }
        var steep=Eval(nera,40);
        check(steep.RawAngleDegrees>80&&steep.EffectiveAngleDegrees==80,"raw angle above 80 is kept while the physics stays capped ("+steep.RawAngleDegrees.ToString("0.#",CultureInfo.InvariantCulture)+")");
        var report=CassetteModel.AngleReport(steep);
        check(report.StartsWith("\u03B8_eff \u2265 80\u00B0",StringComparison.Ordinal),"report shows >= 80 above the cap: "+report);
        check(Math.Abs(steep.AngleGain-.75)<1e-9&&report.Contains("above max 75\u00B0: gain reduced to 75%",StringComparison.Ordinal),"report names the taper above the recipe maximum: "+report);
        var mild=Eval(nera,10);
        check(mild.RawAngleDegrees>0&&mild.RawAngleDegrees<80&&mild.AngleGain==1&&CassetteModel.AngleReport(mild).StartsWith("\u03B8_eff \u2248 ",StringComparison.Ordinal),"below the maximum the report is unchanged: "+CassetteModel.AngleReport(mild));
        // Two sandwiches with different tilts: one angle per tilt group.
        var two=new CassetteModel(Preset(new("rha",3,30,"up"),new("elastomerNERA",15,30,"up"),new("rha",3,30,"up"),new("air",20,0,"up"),
            new("rha",3,57,"up"),new("elastomerNERA",15,57,"up"),new("rha",3,57,"up"),new("air",20,0,"up")),materials,reactions);
        var groups=Eval(two,20);
        check(groups.TiltGroupText!=null&&groups.TiltGroupText.Contains("30\u00B0 up")&&groups.TiltGroupText.Contains("57\u00B0 up")&&CassetteModel.AngleReport(groups).Contains("tilt groups [",StringComparison.Ordinal),"report lists the angle per tilt group: "+groups.TiltGroupText);
        check(steep.TiltGroupText==null,"a single tilt group adds no group list");
        // Composer note for steel plates that lean differently from their core.
        var mixed=Preset(new("rha",3,30,"up"),new("elastomerNERA",15,57,"up"),new("rha",3,30,"up"),new("air",20,0,"up"));
        var model=new CassetteModel(mixed with{MinimumDepthMm=1,MaximumDepthMm=4000},materials,reactions);
        var notes=model.TiltNotes();
        check(notes.Count==1&&notes[0].Layer==1&&notes[0].Text.Contains("core tilts 57",StringComparison.Ordinal),"mismatched sandwich tilt gives one note on the core");
        check(new CassetteModel(Preset(new("rha",3,57,"up"),new("elastomerNERA",15,57,"up"),new("rha",3,57,"up"),new("air",20,0,"up")),materials,reactions).TiltNotes().Count==0,"equal tilts give no note");
        var backend=new CassetteComposer(materials,reactions,"unused","unused",_=>false);
        check(backend.Validate(mixed).Any(i=>i.Code=="sandwich-tilt"&&!i.Error&&i.Layer==1),"composer shows the sandwich tilt warning");
        check(backend.Validate(Preset(new("rha",3,30,"up"),new("elastomerNERA",15,30,"up"),new("rha",3,30,"up"),new("air",20,0,"up"))).All(i=>i.Code!="sandwich-tilt"),"composer shows no warning for equal tilts");
        // Option A: cooperative gain tapers above maximumAngleDegrees, data-driven.
        var recipe=reactions.Recipes["elastomerSandwich"];
        double Plain(MaterialReactionRecipe r,double angle)
        {
            double t=Math.Clamp((angle-r.MinimumAngleDegrees)/(r.FullAngleDegrees-r.MinimumAngleDegrees),0,1);var s=Math.Sin(angle*Math.PI/180);
            return t*t*(3-2*t)*(1+s*s);
        }
        check(recipe.MaximumAngleDegrees==75,"NERA recipe maximum is 75");
        foreach(var a in new[]{20d,45,60,75})check(Math.Abs(MaterialReactionModel.CooperativeAngle(recipe,a)-Plain(recipe,a))<1e-12,"unchanged at or below max "+a);
        var g76=MaterialReactionModel.CooperativeAngle(recipe,76);var g82=MaterialReactionModel.CooperativeAngle(recipe,82);var g89=MaterialReactionModel.CooperativeAngle(recipe,89.9);
        check(g76<Plain(recipe,76)&&g76>g82&&g82>g89&&g89>0,"gain tapers monotonically above max");
        check(Math.Abs(MaterialReactionModel.TaperFactor(recipe,82.5)-.5)<1e-12&&MaterialReactionModel.TaperFactor(recipe,90)==0&&MaterialReactionModel.CooperativeAngle(recipe,90)==0,"half way is 50 % and 90 degrees is 0");
        var open=recipe with{MaximumAngleDegrees=90};
        foreach(var a in new[]{76d,82,89})check(Math.Abs(MaterialReactionModel.CooperativeAngle(open,a)-Plain(open,a))<1e-12,"max 90 is never tapered "+a);
        var backed=reactions.Recipes.Values.FirstOrDefault(r=>r.Mode=="backed"&&r.MaximumAngleDegrees<90);
        check(backed==null||MaterialReactionModel.CooperativeAngle(backed,89)<Plain(backed,89),"backed recipes taper too");
        check(MaterialReactionPresentation_Text(recipe).Contains("tapers",StringComparison.Ordinal),"the recipe summary mentions the taper");
        var neraPreset=Preset(new("rha",3,30,"up"),new("elastomerNERA",15,30,"up"),new("rha",3,30,"up"),new("air",20,0,"up"));
        // 0.22.19: the composer no longer shows an always-on taper rule; it names the reduction only where it applies (Fix02219Tests).
        check(backend.AngleNotes(neraPreset,60,true,true).Length==1&&backend.AngleNotes(neraPreset,60,true,true)[0].Text.Contains("above the recipe maximum 75",StringComparison.Ordinal),"the composer names the taper for a NERA cassette at an angle where it applies");
        check(backend.AngleNotes(Preset(new CassetteLayer("rha",20,0,"up")),60,true,false).Length==0,"no taper note without a cooperative recipe");
    }
    private static string MaterialReactionPresentation_Text(MaterialReactionRecipe r)=>string.Join("\n",SprocketExpandedBallistics.Materials.MaterialReactionPresentation.Summary(r,true));
    // ---------------- point 5: thickness source and nearest face ----------------
    // ---------------- point 6: A3 chain ----------------
    // ---------------- point 7: fragment ricochet ----------------
    private static void Ricochet(Action<bool,string> check)
    {
        var d=FragmentRicochetSettings.Default;
        check(Math.Abs(FragmentRicochetModel.SpeedFactor(.5)-Math.Sqrt(.5))<1e-12,"loss 0.5 keeps 0.707 of the speed");
        double v=1;for(int i=0;i<3;i++)v*=FragmentRicochetModel.SpeedFactor(.5);
        check(Math.Abs(v*v-.125)<1e-12,"energy after three ricochets is (1-loss)^3");
        check(FragmentRicochetModel.SpeedFactor(double.NaN)==1&&FragmentRicochetModel.SpeedFactor(2)==0,"speed factor is bounded");
        check(!FragmentRicochetModel.Applies(d,false,"heat",null),"vanilla (no impact context) stays native");
        foreach(var b in new[]{"heat","hesh","he","aphe","apfsds"})check(FragmentRicochetModel.Applies(d,true,b,null),"mod "+b+" ricochets lose energy");
        check(FragmentRicochetModel.Applies(d,true,"ap","apds")&&FragmentRicochetModel.Applies(d,true,"ap","apcr"),"apds and apcr follow the kind list");
        check(!FragmentRicochetModel.Applies(d,true,"ap","apcbc")&&!FragmentRicochetModel.Applies(d,true,"ap",null)&&!FragmentRicochetModel.Applies(d,true,"unknown",null),"plain AP kinds and unknown behaviours stay native");
        check(!FragmentRicochetModel.Applies(d with{Enabled=false},true,"heat",null),"master switch");
        check(FragmentRicochetModel.Applies(d with{KineticKinds=new[]{"apcbc"}},true,"ap","apcbc"),"filters are configurable");
        var parents=new[]{-1,0,1,2,3};var bounced=new[]{true,true,true,false,false};
        check(FragmentRicochetModel.CountChain(j=>parents[j],j=>bounced[j],3,5)==4,"chain counts the parent and its deflected ancestors");
        check(FragmentRicochetModel.CountChain(j=>parents[j],j=>bounced[j],0,5)==1,"first fragment counts itself");
        check(FragmentRicochetModel.CountChain(j=>j,j=>true,2,5)==1,"a self link ends the walk");
        check(FragmentRicochetModel.CountChain(j=>j+1,j=>true,2,5)==1,"a forward link ends the walk");
        check(FragmentRicochetModel.CountChain(j=>j-1,j=>true,1000,2000)<=65,"a long chain is bounded");
        check(FragmentRicochetModel.Next(3,3)==RicochetOutcome.Scale&&FragmentRicochetModel.Next(4,3)==RicochetOutcome.Kill,"kill beyond the maximum");
        var baseJson=SpallBalance.ToJson(new SpallSettings{SpacedKinetic=SpacedKineticSettings.Default});
        var full=new SpallSettings{SpacedKinetic=SpacedKineticSettings.Default,FragmentRicochet=d with{EnergyLoss=.4,MaxRicochets=5}};
        var json=SpallBalance.ToJson(full);var back=SpallBalance.Parse(json);
        check(back.FragmentRicochet is {} r&&r.EnergyLoss==.4&&r.MaxRicochets==5&&r.KindList.SequenceEqual(d.KindList)&&SpallBalance.ToJson(back)==json,"fragmentRicochet round-trips through Parse and ToJson");
        check(SpallBalance.Parse(baseJson).FragmentRicochet==null,"a file without fragmentRicochet parses with code defaults");
        var merged=FragmentRicochetModel.MergeMissing(baseJson);
        check(merged!=baseJson&&SpallBalance.Parse(merged).FragmentRicochet is {} m&&m.EnergyLoss==.5&&m.MaxRicochets==3,"merge adds the missing object with the defaults");
        check(FragmentRicochetModel.MergeMissing(merged)==merged,"merge is idempotent");
        var custom=JsonNode.Parse(baseJson)!.AsObject();custom["fragmentRicochet"]=new JsonObject{["energyLoss"]=.3};custom["coneMultiplier"]=.2;
        var customMerged=JsonNode.Parse(FragmentRicochetModel.MergeMissing(custom.ToJsonString()))!.AsObject();
        check(customMerged["fragmentRicochet"]!["energyLoss"]!.GetValue<double>()==.3&&customMerged["fragmentRicochet"]!["maxRicochets"]!.GetValue<int>()==3&&customMerged["coneMultiplier"]!.GetValue<double>()==.2,"merge keeps user values and adds only the missing keys");
        var foreign=JsonNode.Parse("{\"customFixture\":true}")!.AsObject();
        check(JsonNode.Parse(FragmentRicochetModel.MergeMissing(foreign.ToJsonString()))!["customFixture"]!.GetValue<bool>(),"merge keeps unknown top-level keys");
        void Rejects(string field,JsonNode? value,string name)
        {
            var node=JsonNode.Parse(baseJson)!.AsObject();node["fragmentRicochet"]=new JsonObject{[field]=value};
            bool rejected=false;try{SpallBalance.Parse(node.ToJsonString());}catch(FormatException){rejected=true;}
            check(rejected,"rejects "+name);
        }
        Rejects("energyLoss",.99,"energyLoss above 0.95");Rejects("energyLoss",-.1,"negative energyLoss");Rejects("maxRicochets",0,"maxRicochets 0");
        Rejects("maxRicochets",21,"maxRicochets 21");Rejects("maxRicochets",2.5,"fractional maxRicochets");Rejects("bogus",1,"an unknown field");
        Rejects("behaviors",new JsonArray("laser"),"an unknown behavior");Rejects("kineticKinds",new JsonArray("plasma"),"an unknown kind");Rejects("enabled","yes","a text boolean");
    }
    // ---------------- point 9: muzzle velocity of the loaded shell ----------------
    private static void Muzzle(Action<bool,string> check)
    {
        var profile=ShellProfiles.Parse(ShellProfiles.CreateDefault(new DartSettings()))[0];
        var expected=ShellBalance.Calculate(120,800,1800,profile).Velocity;
        check(AmmoMuzzle.Velocity(profile,120,800,1800)==expected&&expected!=800,"a custom shell reports its balanced velocity, not the blueprint velocity");
        check(AmmoMuzzle.Velocity(null,120,800,1800)==800,"a vanilla shell keeps the blueprint velocity");
        check(AmmoMuzzle.Velocity(profile,120,double.NaN,1800)==null&&AmmoMuzzle.Velocity(null,120,0,1800)==null,"an unreadable blueprint gives no velocity");
        var snapshot=new AmmoSnapshot("a","A","A","b","B","B",true,1,Array.Empty<AmmoSnapshotRound>(),false,null);
        check(snapshot.ChamberedMuzzleVelocityMps==null&&(snapshot with{ChamberedMuzzleVelocityMps=1187}).ChamberedMuzzleVelocityMps==1187,"snapshot carries the chambered velocity and defaults to null");
        check(ChamberIdentity.Resolve(true,5,9)==5&&ChamberIdentity.Resolve(false,5,9)==9&&ChamberIdentity.Resolve(true,-1,9)==9,"chamber identity prefers the captured round while Loaded");
        int captured=-1;
        captured=ChamberIdentity.Track(false,true,captured,5);check(captured==-1,"not loaded: nothing captured");
        captured=ChamberIdentity.Track(true,false,captured,5);check(captured==5,"first Loaded frame captures the request");
        captured=ChamberIdentity.Track(true,false,captured,9);check(captured==5&&ChamberIdentity.Resolve(true,captured,9)==5,"a later request for the next shell does not change the loaded round");
        captured=ChamberIdentity.Track(false,true,captured,9);check(captured==-1,"firing clears the captured round");
        captured=ChamberIdentity.Track(false,false,5,9);check(captured==5,"Loading keeps what was captured until Loaded ends");
    }
}
