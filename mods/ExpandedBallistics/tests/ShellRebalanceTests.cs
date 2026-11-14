using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SprocketExpandedBallistics;

internal static class ShellRebalanceTests
{
    private static int checks;
    private static void Check(bool condition,string message)
    {
        checks++;
        if(!condition)throw new Exception(message);
    }
    private static readonly (string Name,double Calibre,double V0,ushort K)[] Guns=
    {
        ("R1",88,773,1900),("R2",76.2,884,1800),("R4",105,950,1800),("R5",125,950,1800)
    };
    private static double VanillaPenetration(double calibre,double v0,ushort k)
    {
        var mass=(float)(1.59e-5*calibre*calibre*calibre);
        var dart=new DartBallistics((float)(calibre*.001),1f,mass,(float)v0,k,1f);
        return LongRodPenetration.NativeBudgetMm(dart,v0);
    }
    private static double Penetration(ShellProfile profile,double calibre,double v0,ushort k)
    {
        var dart=ShellBalance.Calculate(calibre,v0,k,profile);
        return LongRodPenetration.NativeBudgetMm(dart,dart.Velocity);
    }
    private static string F(double value)=>value.ToString("0.##",CultureInfo.InvariantCulture);

    internal static int Run()
    {
        var defaults=ShellProfiles.Parse(ReleaseProfiles.Defaults());
        var spall=new SpallSettings();
        var kinetic=defaults.Where(p=>p.Behavior is "ap" or "aphe" or "apfsds").ToArray();
        var targets=new Dictionary<string,(string Gun,double Ratio)>
        {
            ["aphe"]=("R1",.90),["apcbc"]=("R1",1.03),["apcr"]=("R1",1.40),
            ["apds"]=("R2",1.20),["apds_late"]=("R4",1.30),
            ["apfsds_short"]=("R4",1.38),["apfsds_gen2"]=("R4",1.52),
            ["apfsds_long"]=("R5",1.65),["apfsds_gen4"]=("R5",2.05)
        };
        Check(kinetic.Length==targets.Count,"release kinetic catalogue covers every calibrated profile");
        Console.WriteLine("REBALANCE TABLE profile,gun,calibre,v0,vanillaAP,P0,ratio,plate60");
        foreach(var profile in kinetic)
        {
            Check(targets.ContainsKey(profile.Id),"unexpected uncalibrated kinetic profile "+profile.Id);
            foreach(var gun in Guns)
            {
                var vanilla=VanillaPenetration(gun.Calibre,gun.V0,gun.K);
                var p0=Penetration(profile,gun.Calibre,gun.V0,gun.K);
                var ratio=p0/vanilla;
                var exponent=SpallBalance.ObliquityExponent(profile.Behavior,profile.EffectiveKineticKind,spall);
                var plate=p0*Math.Pow(Math.Cos(Math.PI/3),exponent+1);
                Console.WriteLine($"REBALANCE TABLE {profile.Id},{gun.Name},{F(gun.Calibre)},{F(gun.V0)},{F(vanilla)},{F(p0)},{F(ratio)},{F(plate)}");
                var target=targets[profile.Id];
                if(gun.Name==target.Gun)
                    Check(Math.Abs(ratio-target.Ratio)<=target.Ratio*.05,
                        $"{profile.Id} on {gun.Name} ratio {ratio:0.###}; expected {target.Ratio:0.###} +/-5%");
            }
        }
        // Ladder per reference gun: early APDS < late APDS < every APFSDS generation, in date order.
        foreach(var gunName in new[]{"R1","R2","R4","R5"})
        {
            var gun=Guns.Single(g=>g.Name==gunName);
            var ladder=new[]{"apds","apds_late","apfsds_short","apfsds_gen2","apfsds_long","apfsds_gen4"};
            var ratios=ladder.Select(id=>Penetration(defaults.Single(p=>p.Id==id),gun.Calibre,gun.V0,gun.K)/VanillaPenetration(gun.Calibre,gun.V0,gun.K)).ToArray();
            for(var i=1;i<ratios.Length;i++)
                Check(ratios[i]>ratios[i-1],$"{gunName} kinetic ladder is not monotonic at {ladder[i]}");
        }
        // Geometry limits and the APFSDS simulator/live shared bound.
        var ld=new Dictionary<string,(double Min,double Max)>{{"apfsds_short",(10,15)},{"apfsds_gen2",(14,20)},{"apfsds_long",(20,28)},{"apfsds_gen4",(25,35)}};
        foreach(var profile in defaults.Where(p=>p.Behavior=="apfsds"))
        {
            var rodDart=ShellBallistics.Calculate(105,950,1800,profile.Settings);
            var lengthToDiameter=rodDart.Length/rodDart.Diameter;
            var limits=ld[profile.Id];
            Check(lengthToDiameter>=limits.Min-1e-4&&lengthToDiameter<=limits.Max+1e-4,
                $"{profile.Id} L/D {lengthToDiameter:0.###} outside {limits.Min}-{limits.Max}");
            var bounded=ShellBalance.Calculate(105,950,1800,profile);
            Check(bounded.PenetratorConstant>=rodDart.PenetratorConstant,"APFSDS bound only raises K");
            Check(LongRodPenetration.NativeBudgetMm(bounded,bounded.Velocity)<=LongRodPenetration.EnvelopeMm(bounded.Length,profile.Settings.Density,bounded.Velocity)+.5,
                "APFSDS muzzle budget obeys the shared long-rod envelope");
        }
        Check(defaults.Single(p=>p.Id=="apfsds_gen4").Settings.Density<=19000,"gen4 density stays at or below 19000");

        // Obliquity defaults, optional nested configuration and fallbacks.
        Check(SpallBalance.ObliquityExponent("ap",null,spall)==1.33,"custom ap defaults to vanilla 1.33");
        Check(SpallBalance.ObliquityExponent("ap","apc",spall)==1.33,"apc defaults to 1.33");
        Check(SpallBalance.ObliquityExponent("aphe",null,spall)==1.33,"aphe defaults to 1.33");
        Check(SpallBalance.ObliquityExponent("ap","apcbc",spall)==1.15,"apcbc defaults to 1.15");
        Check(SpallBalance.ObliquityExponent("ap","apcr",spall)==1.60,"apcr defaults to 1.60");
        Check(SpallBalance.ObliquityExponent("ap","apds",spall)==1.00,"apds defaults to 1.00");
        Check(SpallBalance.ObliquityExponent("apfsds",null,spall)==1,"apfsds defaults to line of sight");
        Check(SpallBalance.ObliquityExponent("heat",null,spall)==1&&SpallBalance.ObliquityExponent("atgm",null,spall)==1,"heat/atgm defaults to native LOS");
        Check(SpallBalance.ObliquityExponent("hesh",null,spall)==0,"hesh defaults to zero");
        var exponentJson=SpallBalance.ToJson(spall).Replace("\"apcbc\": 1.15","\"apcbc\": 0.5").Replace("\"apcr\": 1.6","\"apcr\": 1.9");
        var exponentSettings=SpallBalance.Parse(exponentJson);
        Check(exponentSettings.ApcbcObliquityExponent==.5&&exponentSettings.ApcrObliquityExponent==1.9,"nested obliquity override parses");
        Check(exponentSettings.ApdsObliquityExponent==1&&exponentSettings.HeatObliquityExponent==1,"missing obliquity keys fall back to defaults");
        foreach(var invalid in new[]{"\"apfsds\": -0.1","\"apfsds\": 2.1","\"unknown\": 1"})
        {
            var bad=SpallBalance.ToJson(spall);
            var marker=bad.IndexOf("\"apfsds\":",StringComparison.Ordinal);
            var end=bad.IndexOfAny(new[]{',','}'},marker);
            bad=bad[..marker]+invalid+bad[end..];
            var rejected=false;try{SpallBalance.Parse(bad);}catch(FormatException){rejected=true;}
            Check(rejected,"invalid obliquity exponent rejected: "+invalid);
        }
        var roundTrip=SpallBalance.Parse(SpallBalance.ToJson(exponentSettings));
        Check(roundTrip.ApcbcObliquityExponent==.5&&roundTrip.ApcrObliquityExponent==1.9,"obliquity round-trip");
        Check(!SpallBalance.ToJson(spall).Contains("\"apheFragmentCount\"")&&!SpallBalance.ToJson(spall).Contains("\"apfsdsPlateDeflection\"")&&!SpallBalance.ToJson(spall).Contains("\"apheFuseRhaMm\""),
            "new spall defaults omit retired fields");
        Check(SpallBalance.ToJson(spall).Contains("\"obliquityExponents\""),"new spall defaults carry the nested exponent object");

        // Vanilla AP is never eligible for the scoped exponent write.
        Check(!ShellObliquity.Applies(null),"vanilla AP has no profile and no obliquity override");
        Check(!ShellObliquity.TryTarget(null,1.33f,spall,out _),"vanilla AP native exponent stays untouched");
        var apcbc=defaults.Single(p=>p.Id=="apcbc");
        Check(ShellObliquity.TryTarget(apcbc,1.33f,spall,out var apcbcTarget)&&Math.Abs(apcbcTarget-1.15)<1e-9,"apcbc override targets 1.15");
        var ap=apcbc with {KineticKind="ap"};
        Check(!ShellObliquity.TryTarget(ap,1.33f,spall,out _),"custom ap at the native default is a no-op");
        Check(ShellObliquity.TryTarget(defaults.Single(p=>p.Id=="hesh"),1.33f,spall,out var heshTarget)&&heshTarget==0,"hesh override targets zero");

        // Visibility follows the dated native technology data, not labels.
        var tech=ReadTech();
        var timeline=new[]
        {
            (new ShellNativeDate(1900,1,1),"Early"),(new ShellNativeDate(1945,9,3),"Coldwar"),
            (new ShellNativeDate(1960,1,1),"Coldwar2"),(new ShellNativeDate(1975,1,1),"Coldwar3")
        };
        string[] Visible(int year)
        {
            var date=new ShellNativeDate(year,1,1);
            var names=timeline.Select(x=>x.Item2).ToArray();var starts=timeline.Select(x=>x.Item1).ToArray();
            var index=Array.FindLastIndex(starts,s=>date>=s);
            var availableBehaviors=new HashSet<string>(StringComparer.Ordinal);
            var availableTechnologies=new HashSet<string>(StringComparer.Ordinal);
            var heatFactor=(double?)null;
            foreach(var behavior in new[]{"ap","he","aphe","heat","hesh","apfsds","atgm","atgm_gun"})
                if(Latest(tech,"expandedBallistics_"+behavior,date) is {} found&&found.Enabled)
                {
                    availableBehaviors.Add(behavior);availableTechnologies.Add("expandedBallistics_"+behavior);
                    if(behavior=="heat")heatFactor=found.Factor;
                }
            foreach(var profile in defaults)
                foreach(var requirement in ShellBalance.RequiredTechnologyIds(profile))
                    if(Latest(tech,requirement,date) is {} required&&required.Enabled)availableTechnologies.Add(requirement);
            var context=new ShellEraContext(date,names,starts,index,availableBehaviors,heatFactor,availableTechnologies);
            Check(ShellEraPolicy.Valid(context),"synthetic tech context is valid for "+year);
            return defaults.Where(p=>ShellEraPolicy.Allowed(p,context)&&(!ShellChemicalBudget.IsStock(p)||context.HeatFactor!=null))
                .Select(p=>p.Id).ToArray();
        }
        var expected= new Dictionary<int,string[]>
        {
            [1938]=new[]{"he"},
            [1943]=new[]{"he","heat","aphe","apcbc","apcr"},
            [1950]=new[]{"he","heat","aphe","apcbc","apcr","apds"},
            [1965]=new[]{"apfsds_short","he","heat","hesh","aphe","apcbc","apcr","apds","apds_late"},
            [1990]=defaults.Where(p=>p.Id is not ("apfsds_gen4" or "atgm_topattack_test")).Select(p=>p.Id).ToArray()
        };
        foreach(var pair in expected)
        {
            var visible=Visible(pair.Key);
            Check(visible.SequenceEqual(pair.Value),"visible list for "+pair.Key+": "+string.Join(",",visible));
        }
        Check(Latest(tech,"expandedBallistics_heat",new ShellNativeDate(1943,1,1))?.Factor==1.2,"1943 uses the 1939 HEAT factor");
        Check(Latest(tech,"expandedBallistics_heat",new ShellNativeDate(1950,1,1))?.Factor==2.0,"1950 uses the 1945 HEAT factor");
        Check(Latest(tech,"expandedBallistics_heat",new ShellNativeDate(1965,1,1))?.Factor==3.8,"1965 uses the 1960 HEAT factor");
        Check(Latest(tech,"expandedBallistics_heat",new ShellNativeDate(1990,1,1))?.Factor==5.0,"1990 uses the 1985 HEAT factor");
        Check(Latest(tech,"expandedBallistics_atgm",new ShellNativeDate(1954,12,31))==null,"no ATGM behavior before 1955");
        Check(Latest(tech,"expandedBallistics_apfsds",new ShellNativeDate(1950,1,1))==null,"no APFSDS behavior before 1961");

        // chemicalSource migration and user edits.
        var temp=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"shell-rebalance-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var path=Path.Combine(temp,"sprocket.expandedballistics.shells.json");
            var legacy=ReleaseProfiles.LegacyDefaults();
            File.WriteAllText(path,legacy);
            Check(ReleaseProfiles.Upgrade(path),"legacy default catalogue migrates");
            var migrated=ShellProfiles.Parse(File.ReadAllText(path));
            Check(migrated.Count==21,"migration keeps 21 profiles");
            Check(migrated.Single(p=>p.Id=="heat").ChemicalSource=="tech"&&migrated.Single(p=>p.Id=="heat").Label=="HEAT / HEAT-FS","stock heat opts into native tech");
            Check(migrated.Single(p=>p.Id=="hesh").ChemicalPenetrationMm==140,"HESH 140 mm default");
            Check(Math.Abs(migrated.Single(p=>p.Id=="aphe").Settings.PenetrationQuality-1.125)<1e-9,"APHE 0.90 quality");
            Check(migrated.Single(p=>p.Id=="apfsds_short").Label=="APFSDS gen 1"&&migrated.Single(p=>p.Id=="apfsds_long").Label=="APFSDS gen 3","APFSDS labels update");
            Check(migrated.Single(p=>p.Id=="heat_tandem").TandemPrecursorPenetrationMm==100&&migrated.Single(p=>p.Id=="heat_tandem").ChemicalPenetrationMm==500,"tandem profile added");
            Check(migrated.Single(p=>p.Id=="atgm_saclos_direct_v1").NativeTechnologyIds!.Contains("expandedBallistics_atgm_itow"),"ATGM per-missile technology added");
            Check(File.Exists(path+".pre-v094-backup")&&File.ReadAllText(path+".pre-v094-backup")==legacy,"migration backs up exact legacy bytes");
            var afterFirst=File.ReadAllText(path);
            Check(!ReleaseProfiles.Upgrade(path)&&File.ReadAllText(path)==afterFirst,"second migration is idempotent");
            // A user-edited default must survive byte-for-byte in meaning.
            var edited=JsonNode.Parse(legacy)!;
            var heatNode=edited["profiles"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()=="heat")!;
            heatNode["chemicalPenetrationMm"]=555;heatNode["label"]="My HEAT";
            File.WriteAllText(path,edited.ToJsonString());
            Check(ReleaseProfiles.Upgrade(path),"edited catalogue still gains new presets");
            var kept=ShellProfiles.Parse(File.ReadAllText(path));
            Check(kept.Single(p=>p.Id=="heat").ChemicalPenetrationMm==555&&kept.Single(p=>p.Id=="heat").ChemicalSource=="profile","edited heat stays custom");
            Check(kept.Single(p=>p.Id=="heat").Label=="My HEAT","edited label retained");
            Check(kept.Count==21,"edited catalogue still reaches 21 profiles");
            // Read-only check against the current live catalogue, when this build host has one.
            var livePath=@"C:\Program Files (x86)\Steam\steamapps\common\Sprocket\BepInEx\config\sprocket.expandedballistics.shells.json";
            if(File.Exists(livePath))
            {
                var liveFile=Path.Combine(temp,"live-shells.json");
                var liveOriginal=File.ReadAllText(livePath);
                File.WriteAllText(liveFile,liveOriginal);
                // The live file may already be migrated by an earlier game start: accept both,
                // but require idempotence and the final content either way.
                var liveMigrated=ReleaseProfiles.Upgrade(liveFile);
                Check(!ReleaseProfiles.Upgrade(liveFile),"live catalogue copy migrates or is already current, then stays idempotent");
                var live=ShellProfiles.Parse(File.ReadAllText(liveFile));
                Check(live.Select(p=>p.Id).ToHashSet().SetEquals(ShellProfiles.Parse(liveOriginal).Select(p=>p.Id).Union(defaults.Select(p=>p.Id))),"live catalogue contains every stock and user-added profile after additive migration");
                Check(live.Single(p=>p.Id=="heat").ChemicalSource=="tech","live exact-default heat adopts native tech");
                Check(live.Single(p=>p.Id=="aphe").RetiredAfter==new ShellNativeDate(1960,1,1),"live exact-default APHE gains retirement");
                Check(live.Single(p=>p.Id=="apfsds_short").RetiredAfter==new ShellNativeDate(1985,1,1),"live exact-default APFSDS gen 1 gains retirement");
                Check(live.Single(p=>p.Id=="hesh").ChemicalPenetrationMm==140,"live exact-default HESH becomes 140 mm");
                Check(live.Single(p=>p.Id=="atgm_topattack_test").TandemPrecursorPenetrationMm==200,"live user-added top-attack tandem is preserved");
                Check(live.Single(p=>p.Id=="atgm_konkurs_9m113").Wire?.Transport=="wire","live user wire Konkurs is preserved");
                Check(live.Single(p=>p.Id=="atgm_mclos_manual_v1").NativeTechnologyIds!.Contains("expandedBallistics_atgm_malyutka_m"),"live numeric-format-only ATGM gains its dated technology");
                Check(!liveMigrated||File.Exists(liveFile+".pre-v094-backup"),"live migration writes a backup when it changes the file");
            }
        }
        finally{DiretoryDelete(temp);}

        // Gap curve standoff.
        var heat=defaults.Single(p=>p.Id=="heat");
        Check(heat.StandoffFreeCalibres==2,"heat default standoff is 2 calibres");
        Check(ShellBalance.SpacedRetention(heat,100,10,5)==1,"HEAT ignores gaps inside two calibres");
        Check(ShellBalance.SpacedRetention(heat,100,10,100)==1,"HEAT one-calibre gap is still free");
        Check(ShellBalance.SpacedRetention(heat,100,10,250)<1,"HEAT beyond the standoff loses budget");
        Check(ShellBalance.SpacedRetention(heat with {StandoffFreeCalibres=.1},100,10,100)<1,"old 0.1-calibre onset still loses");
        Check(ShellBalance.SpacedRetention(defaults.Single(p=>p.Id=="atgm_tow_bgm71a"),100,10,100)==1,"ATGM shares the two-calibre standoff");
        var hesh=defaults.Single(p=>p.Id=="hesh");
        Check(ShellBalance.SpacedRetention(hesh,100,10,50)<.2,"HESH gap curve ignores standoff");
        Check(Math.Abs(ShellBalance.SpacedRetention(hesh,100,10,50)-ShellBalance.SpacedRetention(hesh with {StandoffFreeCalibres=2},100,10,50))<1e-12,"HESH standoff field does not change its curve");
        Check(ShellPayloadRejects(heat with {StandoffFreeCalibres=10.1})&&ShellPayloadRejects(heat with {StandoffFreeCalibres=-.1}),"standoff is validated 0-10");

        // APCR carrier flight and core impact rewrite.
        var apcr=defaults.Single(p=>p.Id=="apcr");
        var core=ShellBallistics.CoreGeometry(88,apcr.Settings);
        var dart=ShellBallistics.Calculate(88,773,1900,apcr.Settings);
        Check(dart.CarrierDiameter>0&&dart.CarrierMass>0,"APCR records a carrier");
        Check(Math.Abs(dart.Diameter-(float)core.Diameter)<1e-6&&Math.Abs(dart.Mass-(float)core.Mass)<1e-4,"APCR core geometry is retained");
        Check(Math.Abs(dart.CarrierDiameter-(float)(88*.001))<1e-6,"APCR carrier is full bore");
        Check(Math.Abs(dart.CarrierMass-(float)(.6*1.59e-5*Math.Pow(88,3)))<.01f,"APCR carrier mass fraction is 0.6 vanilla");
        Check(dart.FlightDiameter==dart.CarrierDiameter&&dart.FlightMass==dart.CarrierMass,"flight uses carrier diameter and mass");
        var vanillaMass=1.59e-5*Math.Pow(88,3);
        var expectedVelocity=Math.Min(773*Math.Clamp(Math.Sqrt(vanillaMass/dart.CarrierMass)*apcr.Settings.VelocityEfficiency*apcr.Settings.VelocityMultiplier,1,apcr.Settings.MaxVelocityFactor),apcr.Settings.MaxVelocity);
        Check(Math.Abs(dart.Velocity-expectedVelocity)<.05,"velocity factor uses carrier mass");
        var noCarrier=apcr.Settings with {CarrierDiameterFactor=null,CarrierMassFraction=null};
        var coreOnly=ShellBallistics.Calculate(88,773,1900,noCarrier);
        Check(dart.Velocity<coreOnly.Velocity,"light carrier drag lowers muzzle velocity versus the core");
        var impact=ShellBalance.CoreImpactGeometry(apcr,.088);
        Check(Math.Abs(impact.CoreDiameterM-core.Diameter)<1e-9&&Math.Abs(impact.MassKg-core.Mass)<1e-6&&Math.Abs(impact.LengthM-core.Length)<1e-9,"impact rewrite uses core diameter, mass and length");
        Check(Math.Abs(impact.BoreDiameterM-.088)<1e-9,"impact geometry keeps the gun calibre");
        var apcrTags=new MaterialThreatTags("ap","unknown",apcr.ArmourThreatTags);
        Check(apcrTags.Contains("shortRod")&&!apcrTags.Contains("kineticFullBore"),"APCR maps to shortRod and not kineticFullBore");
        var apdsTags=new MaterialThreatTags("ap","unknown",defaults.Single(p=>p.Id=="apds").ArmourThreatTags);
        Check(apdsTags.Contains("shortRod")&&!apdsTags.Contains("kineticFullBore"),"APDS maps to shortRod and not kineticFullBore");
        var apcbcTags=new MaterialThreatTags("ap","unknown",apcbc.ArmourThreatTags);
        Check(apcbcTags.Contains("kineticFullBore")&&!apcbcTags.Contains("shortRod"),"APCBC keeps kineticFullBore");
        Check(ShellPayloadRejects(apcr with {Behavior="aphe"}),"carrier fields require ap behavior");
        Check(ShellPayloadRejects(apcr with {KineticKind="zzz"}),"unknown kinetic kind rejected");
        Check(ShellPayloadRejects(apcr with {ChemicalSource="tech"}),"tech source is heat-only");
        Check(ShellBallisticsRejects(new DartSettings(CarrierDiameterFactor:.04))&&ShellBallisticsRejects(new DartSettings(CarrierMassFraction:1.6)),"carrier ranges validated");

        // Performance caches: identical resolution and explicit source boundaries.
        var responsePath=Path.Combine(AppContext.BaseDirectory,"armour-response-fixture.json");
        var catalogue=ArmourResponses.Parse(File.ReadAllText(responsePath));
        Check(catalogue.ByResponseId!=null&&catalogue.ByMaterialId!=null,"response lookups are built at load");
        foreach(var response in catalogue.Responses)
        {
            Check(ReferenceEquals(catalogue.ForResponse(response.ResponseId),response),"responseId cache equals linear lookup");
            foreach(var material in response.CompatibleMaterialIds)
                Check(ReferenceEquals(catalogue.ForMaterial(material),response),"materialId cache equals linear lookup");
        }
        var reactionPath=Path.Combine(AppContext.BaseDirectory,"sprocket.expandedballistics.material.reactions.json");
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(reactionPath));
        var light=catalogue.Responses.First(r=>r.Kind=="lightEra");
        var activeCatalogue=catalogue with {Enabled=true};
        var shot=new ArmourShotState();
        var firstKey=new ArmourHitKey(1,2,3,10,20);
        var first=ArmourResponseModel.Apply(activeCatalogue,light,shot,firstKey,"heat",true,true,light.Geometry.MinNormalThicknessMm,0,0,true);
        var second=ArmourResponseModel.Apply(activeCatalogue,light,shot,firstKey,"heat",true,true,light.Geometry.MinNormalThicknessMm,0,0,true);
        Check(first<1&&second==1&&shot.AppliedKeys.Count==1,"struct hit key charges exactly once");
        var textShot=new ArmourShotState();
        ArmourResponseModel.Apply(activeCatalogue,light,textShot,"same","heat",true,true,light.Geometry.MinNormalThicknessMm,0,0,true);
        Check(textShot.Applied.Count==1&&textShot.AppliedKeys.Count==0,"legacy string key remains isolated for tests");

        // Modular authoring preserves all new profile fields.
        var draft=new ModularShellDraft(ReleaseProfiles.Defaults());
        var compiled=draft.Compile();
        Check(compiled.Ready,"unmodified modular draft compiles");
        var compiledProfiles=ShellProfiles.Parse(compiled.RuntimeJson!);
        var compiledApcr=compiledProfiles.Single(p=>p.Id=="apcr");
        Check(compiledApcr.KineticKind=="apcr"&&compiledApcr.Settings.CarrierDiameterFactor==1&&compiledApcr.Settings.CarrierMassFraction==.6,"modular draft keeps APCR carrier fields");
        var compiledTandem=compiledProfiles.Single(p=>p.Id=="heat_tandem");
        Check(compiledTandem.ChemicalSource=="profile"&&compiledTandem.StandoffFreeCalibres==2&&compiledTandem.TandemPrecursorPenetrationMm==100,"modular draft keeps chemical/tandem fields");
        Check(compiledProfiles.Single(p=>p.Id=="heat").ChemicalSource=="tech","modular draft keeps stock heat source");
        Check(compiledProfiles.Single(p=>p.Id=="apcbc").KineticKind=="apcbc","modular draft keeps kinetic kind");

        Console.WriteLine($"SHELL REBALANCE PASS: {checks} checks.");
        return checks;
    }
    private static bool ShellPayloadRejects(ShellProfile profile)
    {
        try{ShellPayload.Validate(profile);return false;}catch(FormatException){return true;}
    }
    private static bool ShellBallisticsRejects(DartSettings settings)
    {
        try{ShellBallistics.ValidateSettings(settings);return false;}catch(ArgumentOutOfRangeException){return true;}
    }
    private sealed record TechRecord(string Type,DateTime Date,bool Enabled,double? Factor);
    private static List<TechRecord> ReadTech()
    {
        var roots=new[]{Path.Combine(AppContext.BaseDirectory,"NativeData"),Path.Combine(AppContext.BaseDirectory,"NativeData","Technology")};
        var dir=roots.First(Directory.Exists);
        var records=new List<TechRecord>();
        foreach(var file in Directory.GetFiles(dir,"ExpandedBallistics-*.json"))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(file));
            var root=doc.RootElement;
            var type=root.GetProperty("type").GetString()??"";
            var date=DateTime.ParseExact(root.GetProperty("date").GetString()??"", "yyyy.MM.dd",CultureInfo.InvariantCulture);
            var properties=root.GetProperty("properties");
            var enabled=properties.TryGetProperty("enabled",out var enabledNode)&&enabledNode.GetBoolean();
            double? factor=properties.TryGetProperty("penetrationPerCalibre",out var factorNode)&&factorNode.TryGetDouble(out var value)?value:null;
            records.Add(new(type,date,enabled,factor));
        }
        Check(records.Count>=20,"dated shell technology catalogue copied for visibility test");
        return records;
    }
    private static TechRecord? Latest(List<TechRecord> records,string type,ShellNativeDate date)
    {
        var key=date.Key;
        TechRecord? best=null;
        foreach(var record in records)
            if(record.Type==type&&record.Date.Year*10000+record.Date.Month*100+record.Date.Day<=key&&
                (best==null||record.Date>best.Date))best=record;
        return best;
    }
    private static void DiretoryDelete(string path)
    {
        try{Directory.Delete(path,true);}catch{/* test cleanup only */}
    }
}
