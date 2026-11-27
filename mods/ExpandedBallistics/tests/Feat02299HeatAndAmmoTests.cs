namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.99: HEAT burst taper/cap, unsorted StructureIntersection gap scanning,
/// zero-allocation hot paths, inspector load order/arrows, spacing-aware rack grid,
/// HUD "+1 gun" formatting, and mixed autoloader bridge checks.
/// </summary>
internal static class Feat02299HeatAndAmmoTests
{
    internal static void Run(Action<bool,string> check)
    {
        var count=0;
        void Check(bool ok,string text){count++;check(ok,"feat99: "+text);}

        // 1. HEAT burst taper & cap (while 3-arg SuppressPayloadBurst stays intact)
        Check(ShellBalance.HeatBurstTarget(32,0)==32,"1st HEAT burst keeps 32 target");
        Check(ShellBalance.HeatBurstTarget(32,1)==16,"2nd HEAT burst halves to 16");
        Check(ShellBalance.HeatBurstTarget(32,2)==8,"3rd HEAT burst quarters to 8");
        Check(ShellBalance.HeatBurstTarget(32,3)==0,"4th HEAT burst suppressed");
        Check(ShellBalance.HeatBurstTarget(10,2)==4,"minimum clamped to 4 on active tier");
        Check(ShellBalance.HeatBurstTarget(0,0)==0&&ShellBalance.HeatBurstTarget(double.NaN,0)==0&&ShellBalance.HeatBurstTarget(32,-1)==0,"invalid HEAT burst inputs return 0");
        Check(!ShellBalance.SuppressPayloadBurst("heat",true,true)&&ShellBalance.SuppressPayloadBurst("heat",false,true),"legacy 3-arg SuppressPayloadBurst contract preserved");

        // 2. Unsorted StructureIntersection buffer scanning in SpallGapModel.NextSolidGapMm
        // Simulate unsorted buffer: block entry=11 (dist 4.8228), exit=10 (dist 4.8938), next block entry=9 (dist 4.9348, gap=41mm), earlier block entry=12 (dist 4.700)
        var dist=new float[13];var enter=new bool[13];var solid=new bool[13];
        dist[11]=4.8228f;enter[11]=true;solid[11]=true;
        dist[10]=4.8938f;enter[10]=false;solid[10]=true;
        dist[9]=4.9348f;enter[9]=true;solid[9]=true;
        dist[12]=4.7000f;enter[12]=true;solid[12]=true;
        var unsortedGap=SpallGapModel.NextSolidGapMm(dist,enter,solid,exitIndex:10,entryIndex:11);
        Check(Math.Abs(unsortedGap-41.0)<0.2,"unsorted buffer finds 41mm forward gap at earlier index ("+unsortedGap.ToString("0.0")+")");
        Check(SpallGapModel.SuppressBurst(50,unsortedGap),"41mm gap suppressed under 50mm threshold");
        // Touching surfaces at same distance in unsorted order (entry index skipped, other enter at same dist caught)
        var tDist=new[]{4.50f,4.50f,4.40f};
        var tEnter=new[]{true,false,true};
        var tSolid=new[]{true,true,true};
        Check(SpallGapModel.NextSolidGapMm(tDist,tEnter,tSolid,exitIndex:1,entryIndex:2)==0,"unsorted zero gap detected while skipping own entry index");

        // 3. AmmoInspectorPolicy reordering & Load order formatting
        var baseMix=new[]{
            new AmmoLoadoutEntry("apfsds",10),
            new AmmoLoadoutEntry("heat",6),
            new AmmoLoadoutEntry("he",4),
            new AmmoLoadoutEntry("smoke_zero",0)
        };
        var firstHe=AmmoInspectorPolicy.MoveFirst(baseMix,2);
        Check(firstHe.Select(e=>e.ProfileId).SequenceEqual(new[]{"he","apfsds","heat","smoke_zero"}),"MoveFirst puts chosen shell at index 0 keeping relative order");
        Check(ReferenceEquals(AmmoInspectorPolicy.MoveFirst(baseMix,0),baseMix),"MoveFirst on index 0 is no-op");
        var swapped=AmmoInspectorPolicy.Move(baseMix,1,1);
        Check(swapped.Select(e=>e.ProfileId).SequenceEqual(new[]{"apfsds","he","heat","smoke_zero"}),"Move down swaps rows");
        var swappedUp=AmmoInspectorPolicy.Move(swapped,2,-1);
        Check(swappedUp.Select(e=>e.ProfileId).SequenceEqual(baseMix.Select(e=>e.ProfileId)),"Move up restores order");
        Check(ReferenceEquals(AmmoInspectorPolicy.Move(baseMix,0,-1),baseMix)&&ReferenceEquals(AmmoInspectorPolicy.Move(baseMix,3,1),baseMix),"boundary Moves are no-ops");

        var order=AmmoInspectorPolicy.LoadOrder(baseMix,"heat");
        Check(order.Select(e=>e.ProfileId).SequenceEqual(new[]{"heat","he","apfsds"}),"LoadOrder skips zero rows and wraps from starting shell");
        var formatted=AmmoInspectorPolicy.FormatLoadOrder(baseMix,"heat",id=>id.ToUpperInvariant());
        Check(formatted=="Load order: 1. HEAT (6) \u2192 2. HE (4) \u2192 3. APFSDS (10)","FormatLoadOrder shows explicit loading sequence: "+formatted);

        // 4. AmmoRackGrid spacing & padding
        Check(AmmoRackGrid.Capacity(200,200,1000,100,500)==8,"zero-spacing integer division 2x2x2 = 8");
        Check(AmmoRackGrid.Capacity(248,221,308,25,212,0.01f,0.01f,0.01f)==42,"Bradley native spacing 7x6x1 = 42");
        Check(AmmoRackGrid.Capacity(248,221,308,25,212,0.01f,0.01f,0.01f,0.02f,0.02f,0.02f,0.01f,0.01f,0.01f)==30,"Bradley asymmetric padding 6x5x1 = 30");

        // 5. HUD "+1 gun" format
        var snap=new AmmoSnapshot("heat","HEAT","HEAT","apfsds","APFSDS","APFSDS",true,12,new[]{
            new AmmoSnapshotRound("heat","HEAT","HEAT",9,false,true),
            new AmmoSnapshotRound("apfsds","APFSDS","APFSDS",3,true,false)
        },false,null);
        var hud=AmmoHudFormat.Mix(snap,AmmoLoadState.Loaded);
        Check(hud.Rows[0].Count=="9"&&hud.Rows[1].Count=="2 +1 gun","HUD displays storage +1 gun instead of redundant total+gun");
        var lastInGun=new AmmoSnapshot("heat","HEAT","HEAT","apfsds","APFSDS","APFSDS",true,1,new[]{
            new AmmoSnapshotRound("apfsds","APFSDS","APFSDS",1,true,true)
        },false,null);
        Check(AmmoHudFormat.Mix(lastInGun,AmmoLoadState.Loaded).Rows[0].Count=="0 +1 gun","last round in gun reads 0 +1 gun");

        // 6. Source/allocation/version/hook-count checks
        var root=FindRoot();
        string Read(string rel)=>File.ReadAllText(Path.Combine(root,rel));
        Check(Read("Plugin.cs").Contains("\"0.22.112\"",StringComparison.Ordinal)
            &&Read("SprocketExpandedBallistics.csproj").Contains("<Version>0.22.112</Version>",StringComparison.Ordinal)
            &&Read("RELEASE-NOTES.md").Contains("## 0.22.112",StringComparison.Ordinal),"version 0.22.112 in Plugin.cs, csproj, RELEASE-NOTES");
        var spallSrc=Read("RuntimeSpall.cs");
        Check(spallSrc.Contains("record struct LayerScope",StringComparison.Ordinal)
            &&spallSrc.Contains("record struct BurstState",StringComparison.Ordinal)
            &&!spallSrc.Contains("new float[n]",StringComparison.Ordinal)
            &&!spallSrc.Contains("new bool[n]",StringComparison.Ordinal),
            "RuntimeSpall hot path uses value structs and zero-alloc GapLookahead");
        var fxSrc=Read("RuntimeShellEffects.cs");
        Check(fxSrc.Contains("class BallisticsEffectApi",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionKind",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionVisualOnly",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionHasData",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionCalibreMm",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionPower",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionBreachMm",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionPerforated",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionSpaced",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionPerforatedRegister",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionSeq",StringComparison.Ordinal)
            &&fxSrc.Contains("internal static void Clear()",StringComparison.Ordinal),
            "BallisticsEffectApi 0.22.110 telemetry properties & Clear() exposed in RuntimeShellEffects.cs");
        var feedSrc=Read("RuntimeDamageFeed.cs");
        Check(feedSrc.Contains("BallisticsEffectApi.Set(BlastTelemetryMath.ForExplosion(\"he\"",StringComparison.Ordinal)
            &&feedSrc.Contains("BallisticsEffectApi.Clear();",StringComparison.Ordinal)
            &&fxSrc.Contains("!BallisticsEffectApi.CurrentExplosionHasData",StringComparison.Ordinal)
            &&fxSrc.Contains("if(__state.ClearTelemetry)BallisticsEffectApi.Clear();",StringComparison.Ordinal),
            "HeBlast.Begin/End and Scale/RestoreScale fallback wire BallisticsEffectApi cleanly");

        // 7. 0.22.110 BlastTelemetryMath unit checks (HE, HESH, HEAT, APHE, ERA)
        var dummyDart=new DartSettings(0.9,2,1800,0.1,1,600,0.45,1,1);
        var heProfile=new ShellProfile("he","HE",dummyDart,Behavior:"he",NativeExplosivePower:40);
        var heshDefaultProfile=new ShellProfile("hesh","HESH",dummyDart,Behavior:"hesh",NativeExplosivePower:0);
        var heshCustomProfile=new ShellProfile("hesh_custom","HESH+",dummyDart,Behavior:"hesh",NativeExplosivePower:25);
        var heatProfile=new ShellProfile("heat","HEAT",dummyDart,Behavior:"heat",ChemicalPenetrationMm:400);
        var apheProfile=new ShellProfile("aphe","APHE",dummyDart,Behavior:"aphe");

        var he100=BlastTelemetryMath.ForExplosion("he",heProfile,100.0,HeSettings.Default,knownPower:40.0,perforated:true,spaced:false,perforatedRegister:123456789L);
        Check(he100.Kind=="he"&&!he100.VisualOnly&&he100.CalibreMm==100.0&&Math.Abs(he100.Power-40.0)<1e-6&&Math.Abs(he100.BreachMm-20.0)<1e-6&&he100.Perforated&&!he100.Spaced&&he100.PerforatedRegister==123456789L,
            "HE 100mm telemetry: breach=20mm, power=40, visualOnly=false, perforated+register preserved");
        var he125=BlastTelemetryMath.ForExplosion("he",heProfile,125.0,HeSettings.Default,spaced:true);
        Check(Math.Abs(he125.BreachMm-31.25)<1e-6&&Math.Abs(he125.Power-ShellBalance.BlastPower(heProfile,125.0))<1e-6&&!he125.Perforated&&he125.Spaced&&he125.PerforatedRegister==0L,
            "HE 125mm telemetry: breach=31.25mm, fallback BlastPower, spaced flag preserved");
        var he152Breach=BlastTelemetryMath.BreachMm("he",152.0,HeSettings.Default);
        Check(Math.Abs(he152Breach-46.208)<1e-3,$"HE 152mm breach=46.208mm (actual={he152Breach:0.###})");

        var hesh105=BlastTelemetryMath.ForExplosion("hesh",heshDefaultProfile,105.0);
        var expectedHesh105Power=Math.Clamp(15.0*Math.Pow(1.05,3.0),0.1,500.0);
        var expectedHesh105Breach=14.0*Math.Pow(1.05,1.5);
        Check(hesh105.Kind=="hesh"&&!hesh105.VisualOnly&&Math.Abs(hesh105.Power-expectedHesh105Power)<1e-6&&Math.Abs(hesh105.BreachMm-expectedHesh105Breach)<1e-6&&!hesh105.Perforated&&!hesh105.Spaced&&hesh105.PerforatedRegister==0L,
            "HESH 105mm default telemetry: visualOnly=false, cubic fallback power, 1.5-exp thin-plate breach");
        var heshCustom=BlastTelemetryMath.ForExplosion("hesh",heshCustomProfile,120.0);
        Check(Math.Abs(heshCustom.Power-ShellBalance.BlastPower(heshCustomProfile,120.0))<1e-6,
            "HESH with NativeExplosivePower>0 uses ShellBalance.BlastPower");

        var heat125=BlastTelemetryMath.ForExplosion("heat",heatProfile,125.0);
        Check(heat125.Kind=="heat"&&heat125.VisualOnly&&Math.Abs(heat125.Power-6.25)<1e-6&&heat125.BreachMm==0.0&&!heat125.Perforated&&!heat125.Spaced,
            "HEAT 125mm telemetry: visualOnly=true, power=6.25, breach=0");

        var aphe88=BlastTelemetryMath.ForExplosion("aphe",apheProfile,88.0);
        Check(aphe88.Kind=="aphe"&&aphe88.VisualOnly&&aphe88.CalibreMm==88.0&&aphe88.Power==0.0&&aphe88.BreachMm==0.0&&aphe88.Perforated&&!aphe88.Spaced,
            "APHE 88mm telemetry: visualOnly=true, power=0, breach=0, perforated=true");

        var era=BlastTelemetryMath.ForExplosion("era",null,35.0);
        Check(era.Kind=="era"&&era.VisualOnly&&era.CalibreMm==35.0&&era.Power==0.0&&era.BreachMm==0.0&&!era.Perforated&&!era.Spaced&&era.PerforatedRegister==0L,
            "ERA telemetry: calibre=35, visualOnly=true, power=0, breach=0, perforated=false");

        var invalid=BlastTelemetryMath.ForExplosion("he",heProfile,double.NaN);
        Check(invalid.CalibreMm==0.0&&invalid.Power==0.0&&invalid.BreachMm==0.0,"non-finite calibre produces safe zero telemetry");

        Check(Read("RuntimeMixedAmmoStorage.cs").Contains("PrepareRequest(LoadTask task)",StringComparison.Ordinal),
            "AmmoLoadoutApi.PrepareRequest exposed for autoloader sync");

        // 8. 0.22.111 Plate Destruction 1.0.2 back-channel & dedicated HE / HESH presentation
        RunBlastBackchannelAndPresentationTests(Check,fxSrc,feedSrc,Read("CombatReportPanel.cs"));

        var hooks=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)
            .Where(f=>{var r=f.Replace('\\','/');return !r.Contains("/tests/")&&!r.Contains("/bin/")&&!r.Contains("/obj/");})
            .Sum(f=>File.ReadAllLines(f).Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)));
        Check(hooks==48,$"exact 48 [HarmonyPatch annotations preserved (actual={hooks})");

        Console.WriteLine($"0.22.99/0.22.111 HEAT, ammo, blast back-channel & HE/HESH report checks PASS: {count} checks.");
    }

    private static void RunBlastBackchannelAndPresentationTests(Action<bool,string> Check,string fxSrc,string feedSrc,string reportPanelSrc)
    {
        // 8a. Exact public static method signatures on BallisticsEffectApi
        var apiType=typeof(BallisticsEffectApi);
        var mPlate=apiType.GetMethod("ReportBlastPlate",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
        var mEra=apiType.GetMethod("ReportBlastEraStripped",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
        var mDmg=apiType.GetMethod("ReportBlastBreachDamage",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
        Check(mPlate!=null&&mPlate.GetParameters().Select(p=>(p.Name,p.ParameterType)).SequenceEqual(new (string?,Type)[]{
            ("seq",typeof(int)),("plateVuid",typeof(int)),("localThicknessMm",typeof(double)),("breachCapacityMm",typeof(double)),
            ("breached",typeof(bool)),("holeRadiusM",typeof(double)),("isSolidBlock",typeof(bool)),("isDirect",typeof(bool))
        }),"BallisticsEffectApi.ReportBlastPlate has exact 8-parameter public static signature");
        Check(mEra!=null&&mEra.GetParameters().Select(p=>(p.Name,p.ParameterType)).SequenceEqual(new (string?,Type)[]{
            ("seq",typeof(int)),("count",typeof(int)),("radiusM",typeof(double))
        }),"BallisticsEffectApi.ReportBlastEraStripped has exact 3-parameter public static signature");
        Check(mDmg!=null&&mDmg.GetParameters().Select(p=>(p.Name,p.ParameterType)).SequenceEqual(new (string?,Type)[]{
            ("seq",typeof(int)),("plateVuid",typeof(int)),("entityVuid",typeof(int)),("componentType",typeof(string)),
            ("distM",typeof(double)),("damageFraction",typeof(double)),("alive",typeof(bool)),("crewAliveAfter",typeof(int)),("crewTotal",typeof(int))
        }),"BallisticsEffectApi.ReportBlastBreachDamage has exact 9-parameter public static signature");

        // Source wiring checks for runtime back-channel & UI Kit panel Revision tracking
        Check(feedSrc.Contains("RuntimeDamageFeed.RecordBlastTelemetry(BallisticsEffectApi.CurrentExplosionSeq",StringComparison.Ordinal)
            &&feedSrc.Contains("History.RegisterBlastSeq(shot.BlastSeq,report.Number)",StringComparison.Ordinal)
            &&feedSrc.Contains("internal static void AcceptBlastEvent(",StringComparison.Ordinal)
            &&fxSrc.Contains("RuntimeDamageFeed.RecordBlastTelemetry(BallisticsEffectApi.CurrentExplosionSeq,telemetry)",StringComparison.Ordinal)
            &&reportPanelSrc.Contains("RuntimeDamageFeed.History.Revision",StringComparison.Ordinal),
            "RuntimeDamageFeed, RuntimeShellEffects (HE+HESH), and CombatReportPanel Revision wiring present");

        // 8b. Live & post-commit History routing via BallisticsEffectApi, crew updates, dedup, bounded 16-entry map, exception safety
        var prevSink=BallisticsEffectApi.ReportSink;
        try
        {
            // Exception safety: throwing sink must never propagate to caller
            BallisticsEffectApi.ReportSink=(_,_,_,_)=>throw new InvalidOperationException("simulated sink error");
            bool threw=false;
            try
            {
                BallisticsEffectApi.ReportBlastPlate(1,10,20,40,true,0.2,false,true);
                BallisticsEffectApi.ReportBlastEraStripped(1,2,1.5);
                BallisticsEffectApi.ReportBlastBreachDamage(1,10,20,"CrewSeat",0.5,1.0,false,2,3);
            }
            catch{threw=true;}
            Check(!threw,"BallisticsEffectApi.ReportBlast* never throws to caller even when sink fails");

            var history=new CombatReportHistory();
            var buffer=new HitPanelBuffer();
            history.Begin();
            int? liveSeq=null;
            var liveEvents=new List<CombatReportEvent>();
            int? liveCrewTotal=null,liveCrewAlive=null;
            BallisticsEffectApi.ReportSink=(seq,evt,crewAlive,crewTotal)=>
            {
                if(liveSeq==seq)
                {
                    if(!CombatBlastReport.HasEvent(liveEvents,evt))liveEvents.Add(evt);
                    if(crewTotal is >0){liveCrewTotal=crewTotal;liveCrewAlive=liveCrewAlive is {} ex?Math.Min(ex,crewAlive??crewTotal.Value):crewAlive;}
                    return;
                }
                var updated=history.ApplyBlastEvent(seq,evt,crewAlive,crewTotal);
                if(updated!=null)buffer.Replace(updated.Number,HitBlockBuilder.Build(updated));
            };

            // Live shot path: event arrives before EndImpact commits the shot
            BallisticsEffectApi.Set(BlastTelemetryMath.ForExplosion("he",null,152.0,HeSettings.Default,knownPower:140.0));
            int seqLive=BallisticsEffectApi.CurrentExplosionSeq;
            liveSeq=seqLive;
            liveEvents.Add(CombatBlastReport.SummaryEvent("he",152.0,140.0,46.2));
            BallisticsEffectApi.ReportBlastPlate(seqLive,101,25.0,46.2,true,0.22,false,true);
            BallisticsEffectApi.ReportBlastPlate(seqLive,101,25.0,46.2,true,0.22,false,true); // duplicate
            BallisticsEffectApi.ReportBlastEraStripped(seqLive,1,1.25);
            BallisticsEffectApi.ReportBlastBreachDamage(seqLive,101,501,"CrewSeat",0.6,1.0,false,2,3);
            Check(liveEvents.Count==4&&liveCrewTotal==3&&liveCrewAlive==2
                &&liveEvents.Any(e=>e.Kind=="blastEra"&&e.Text=="Blast stripped 1 ERA block within 1.25 m"),
                "live shot receives plate, singular ERA, breach damage without duplicates and tracks crew");
            liveSeq=null;

            // Post-commit path (Plate Destruction drains in Runtime.Update after EndImpact)
            BallisticsEffectApi.Set(BlastTelemetryMath.ForExplosion("he",null,152.0,HeSettings.Default,knownPower:140.0));
            int seqGlacis=BallisticsEffectApi.CurrentExplosionSeq;
            var glacisLayer=new CombatLayerInfo("g:0:0:1",new[]{new CombatLayerPart(311,"Plate #311","rha","Rolled homogeneous armour",28.7,55,true)},false);
            var glacisShot=new CombatReportShot(1,"Outgoing","HE",760,new[]{
                new CombatReportEvent("layerStart","","Reached layer",46.2,null,"g:0:0:1",glacisLayer),
                new CombatReportEvent("nativeStop","","Native stop in Plate #311 [Material: Rolled homogeneous armour]; no outgoing original core recorded.",46.2,null,"g:0:0:1"),
                CombatBlastReport.SummaryEvent("he",152.0,140.0,46.2)
            },0,46.2,"Target glacis",4,4,"Own tank");
            history.Add(glacisShot);
            history.RegisterBlastSeq(seqGlacis,glacisShot.Number);
            buffer.Add(10.0,HitBlockBuilder.Build(glacisShot));
            int revBefore=history.Revision,bufVerBefore=buffer.Version;

            BallisticsEffectApi.ReportBlastPlate(seqGlacis,311,28.7,46.2,true,0.21,false,true);
            BallisticsEffectApi.ReportBlastPlate(seqGlacis,311,28.7,46.2,true,0.21,false,true); // exact duplicate ignored
            BallisticsEffectApi.ReportBlastBreachDamage(seqGlacis,311,601,"LayingDrive",0.8,1.0,false,3,4);
            BallisticsEffectApi.ReportBlastBreachDamage(seqGlacis,311,602,"AmmoRack",0.9,0.5,true,3,4);
            BallisticsEffectApi.ReportBlastBreachDamage(seqGlacis,311,603,"CrewSeat",0.7,1.0,false,3,4);
            var committed1=history.Shots[0];
            Check(history.Revision>revBefore&&buffer.Version>bufVerBefore
                &&committed1.Events.Count(e=>e.Kind=="blastPlate")==1
                &&committed1.CrewTotal==4&&committed1.CrewAlive==3
                &&buffer.Shown[0].Result==HitBlockBuilder.Breached,
                "post-commit glacis shot updated by seq: deduped blastPlate, crew 3/4, History.Revision & HitPanelBuffer updated to BREACHED");

            // Fallback lookup when seq is unmapped and last shot is HESH
            BallisticsEffectApi.Set(BlastTelemetryMath.ForExplosion("hesh",null,120.0));
            int seqHeshUnmapped=BallisticsEffectApi.CurrentExplosionSeq;
            var heshShot=new CombatReportShot(2,"Outgoing","HESH",620,new[]{CombatBlastReport.SummaryEvent("hesh",120.0,25.9,18.4)},0,168,"Target light",3,3,"Own tank");
            history.Add(heshShot); // intentionally not calling RegisterBlastSeq
            BallisticsEffectApi.ReportBlastPlate(seqHeshUnmapped,777,12.0,18.4,true,0.18,false,true);
            Check(history.Shots[^1].Events.Any(e=>e.Kind=="blastPlate"&&e.RelatedEntity==777),
                "unmapped seq falls back to latest HE/HESH shot in History");

            // Bounded 16-entry map & Begin() reset
            var bounded=new CombatReportHistory();
            bounded.Begin();
            for(int i=1;i<=20;i++)
            {
                bounded.Add(new CombatReportShot(i,"Outgoing","APFSDS",1500,Array.Empty<CombatReportEvent>()));
                bounded.RegisterBlastSeq(1000+i,i);
            }
            Check(!bounded.TryFindShotIndexForBlast(1001,out _)&&bounded.TryFindShotIndexForBlast(1020,out int idx20)&&bounded.Shots[idx20].Number==20,
                "blastSeqToShotNumber keeps bounded last 16 entries");
            bounded.End();
            bounded.Begin();
            Check(bounded.Shots.Count==0&&!bounded.TryFindShotIndexForBlast(1020,out _),
                "History.Begin() clears shots and blastSeqToShotNumber mappings");

            // 8c. CombatDamageSummary cause-grouping [breach overpressure], [shock], [exterior] without bracket spam
            var moduleMapEvents=new[]{
                CombatBlastReport.BreachDamageEvent(11,"CrewSeat",false),
                CombatBlastReport.BreachDamageEvent(12,"CrewSeat",true),
                CombatBlastReport.BreachDamageEvent(13,"Cannon",true),
                CombatBlastReport.BreachDamageEvent(14,"LayingDrive",false),
                CombatBlastReport.BreachDamageEvent(15,"TraverseMotor",false),
                CombatBlastReport.BreachDamageEvent(16,"FuelTank",true),
                CombatBlastReport.BreachDamageEvent(17,"AmmoRack",false),
                CombatBlastReport.BreachDamageEvent(18,"CombustionEngine",false),
                CombatBlastReport.BreachDamageEvent(19,"TransmissionBlock",true),
            };
            var dmgSum=CombatDamageSummary.From(moduleMapEvents);
            Check(dmgSum.CrewKilled==1&&dmgSum.CrewWounded==1&&dmgSum.HasCauseGroups
                &&dmgSum.CrewLine(4)=="Crew 1/4 killed, 1 wounded"
                &&dmgSum.Line(4).StartsWith("Breach overpressure: Crew 1/4 killed, 1 wounded · ",StringComparison.Ordinal)
                &&!dmgSum.Line(4).Contains('[')
                &&dmgSum.PartsDestroyed.SequenceEqual(new[]{"laying drive","motor","ammunition stowage","engine"})
                &&dmgSum.PartsDamaged.SequenceEqual(new[]{"main gun","fuel tank","transmission"}),
                "CombatDamageSummary groups [breach overpressure] crew and all mapped native component types cleanly without bracket spam");

            var shockAndExterior=new[]{
                new CombatReportEvent("interaction","","Crew member #448 [shock 1.3 %] wounded",RelatedEntity:448),
                new CombatReportEvent("interaction","","Crew member #451 [shock 1.3 %] wounded",RelatedEntity:451),
                new CombatReportEvent("interaction","","Crew member #260 [shock 1.3 %] wounded",RelatedEntity:260),
                new CombatReportEvent("interaction","","engine [shock 1.3 %] damaged",RelatedEntity:10),
                new CombatReportEvent("interaction","","fuel tank [shock 1.3 %] damaged",RelatedEntity:277),
                new CombatReportEvent("interaction","","main gun [exterior] damaged",RelatedEntity:432)
            };
            var shockLine=CombatDamageSummary.From(shockAndExterior).Line(3);
            Check(shockLine=="Interior shock (1.3 %): Crew 3/3 wounded · engine, fuel tank damaged | Exterior: main gun damaged",
                $"CombatDamageSummary groups shock & exterior cleanly without repeating tags (actual: {shockLine})");

            // 8d. Three full end-to-end HE scenarios (Report View, Hit Panel, Forensic, Blast SVG, HTML XML & jargon check)
            // Scenario 1: Glacis breach (from committed1 above)
            var view1=CombatReportView.Build(new[]{committed1})[0];
            var fx1=CombatForensics.Build(committed1);
            Check(view1.Block.Result==HitBlockBuilder.Breached&&!view1.Destroyed
                &&view1.Block.Waterfall==null&&view1.Block.Values.Contains("0.42 m",StringComparison.Ordinal)
                &&view1.Blast is {IsPureBlastDetonation:true,Breached:true}
                &&view1.BlastKpis.Length==4&&view1.BlastRows.Length==1
                &&view1.SummaryLine.StartsWith("Breach overpressure: Crew 1/4 killed",StringComparison.Ordinal)&&!view1.SummaryLine.Contains('[')
                &&fx1.Outcome==HitBlockBuilder.Breached&&fx1.IsPureBlast
                &&fx1.Explanation.Contains("Plate #311",StringComparison.Ordinal)&&fx1.Explanation.Contains("0.42 m",StringComparison.Ordinal)
                &&fx1.Findings[0]==fx1.Explanation&&fx1.BlastSummary==null
                &&fx1.Advice[0].Contains("46.2 mm",StringComparison.Ordinal),
                "Scenario 1 (glacis breach): BREACHED header, 4 KPI tiles, blast table, no AP waterfall, clean overpressure summary & Forensic");

            // Scenario 2: Turret cheek [Solid block] dent-only + stripped ERA
            var cheekLayer=new CombatLayerInfo("c:0:0:1",new[]{new CombatLayerPart(350,"Composite armour plate [Solid block]","ebCassette_t80u","T-80U cellular cast armour",120,30,true)},false);
            var cheekShot=new CombatReportShot(3,"Outgoing","HE",760,new[]{
                new CombatReportEvent("layerStart","","Reached layer",46.2,null,"c:0:0:1",cheekLayer),
                new CombatReportEvent("nativeStop","","Native stop in Composite armour plate [Solid block]; no outgoing original core recorded.",46.2,null,"c:0:0:1"),
                new CombatReportEvent("materialNote","",HeRules.OutsideText(HeRules.Shock(HeSettings.Default,152)),HitId:"c:0:0:1"),
                CombatBlastReport.SummaryEvent("he",152.0,140.0,46.2),
                CombatBlastReport.PlateEvent(350,120.0,46.2,false,0.0,true,true),
                CombatBlastReport.EraStrippedEvent(3,1.80)!,
                new CombatReportEvent("interaction","",CombatReportLanguage.Module("cannon")+" [exterior] damaged",RelatedEntity:401)
            },0,46.2,"T-80BVM",3,3,"Own tank");
            var view2=CombatReportView.Build(new[]{cheekShot})[0];
            var fx2=CombatForensics.Build(cheekShot);
            Check(view2.Block.Result==HitBlockBuilder.StoppedBlast&&!view2.Destroyed
                &&view2.Block.Waterfall==null&&view2.BlastKpis.Length==4&&view2.BlastRows.Length==2
                &&view2.BlastRows.Any(r=>r.Result.Contains("dent only",StringComparison.Ordinal))
                &&view2.BlastRows.Any(r=>r.Index=="ERA"&&r.Result.Contains("3 ERA elements",StringComparison.Ordinal))
                &&fx2.Outcome==HitBlockBuilder.StoppedBlast&&fx2.IsPureBlast
                &&!fx2.Explanation.Contains("arrested",StringComparison.OrdinalIgnoreCase)
                &&fx2.Explanation.Contains("surface dent only",StringComparison.Ordinal)
                &&fx2.Explanation.Contains("3 ERA elements",StringComparison.Ordinal)
                &&fx2.Advice[0].Contains("resisted blast rupture",StringComparison.Ordinal)&&fx2.Advice[0].Contains("ERA elements",StringComparison.Ordinal),
                "Scenario 2 (turret cheek [Solid block] dent-only + stripped ERA): clean KPIs, blast+ERA rows, explosive Forensic explanation & ERA advice");

            // Scenario 3: Turret roof breach with crew killed despite native oblique stop
            var roofLayer=new CombatLayerInfo("r:0:0:1",new[]{new CombatLayerPart(412,"Plate #412","rha","Rolled homogeneous armour",25.0,72.0,true)},false);
            var roofShot=new CombatReportShot(4,"Outgoing","HE",760,new[]{
                new CombatReportEvent("layerStart","","Reached layer",46.2,null,"r:0:0:1",roofLayer),
                new CombatReportEvent("nativeStop","","Native stop in Plate #412 [Material: Rolled homogeneous armour]; no outgoing original core recorded.",46.2,null,"r:0:0:1"),
                CombatBlastReport.SummaryEvent("he",152.0,140.0,46.2),
                CombatBlastReport.PlateEvent(412,25.0,46.2,true,0.24,false,true),
                CombatBlastReport.BreachDamageEvent(701,"CrewSeat",false),
                CombatBlastReport.BreachDamageEvent(702,"CrewSeat",false),
                CombatBlastReport.BreachDamageEvent(703,"CrewSeat",false)
            },0,46.2,"Target roof",3,0,"Own tank");
            var view3=CombatReportView.Build(new[]{roofShot})[0];
            var fx3=CombatForensics.Build(roofShot);
            Check(view3.Block.Result==HitBlockBuilder.Breached&&view3.Destroyed&&view3.Head.Contains(CombatReportView.DestroyedText,StringComparison.Ordinal)
                &&view3.SummaryLine=="Breach overpressure: Crew 3/3 killed"
                &&fx3.Outcome==HitBlockBuilder.Breached&&fx3.IsPureBlast
                &&fx3.Explanation.Contains("Plate #412",StringComparison.Ordinal)&&fx3.Explanation.Contains("0.48 m",StringComparison.Ordinal)
                &&!fx3.Explanation.Contains("arrested",StringComparison.OrdinalIgnoreCase)
                &&fx3.Findings.Any(f=>f.Contains("exceeded the local plate thickness",StringComparison.Ordinal))
                &&fx3.Findings.Count(f=>f.Contains("Crew 3/3 killed",StringComparison.Ordinal))==1
                &&fx3.Advice[0].Contains("thin roof and side plates",StringComparison.Ordinal),
                "Scenario 3 (turret roof breach with crew killed despite native oblique stop): BREACHED + TANK DESTROYED + zero duplicate findings");

            // HTML export validity, dedicated HE blast SVG + KPI grid (no AP waterfall, no heat-map toggle, no duplicate blast boxes) & zero jargon/uncertainty
            var htmlHist=new CombatReportHistory();
            htmlHist.Restore(new[]{committed1,cheekShot,roofShot},0);
            var html=CombatReportHtml.Render(htmlHist);
            if(Environment.GetEnvironmentVariable("EB_PREVIEW_HTML") is {Length:>0} previewOut)File.WriteAllText(previewOut,html);
            if(Environment.GetEnvironmentVariable("EB_PREVIEW_LIVE_IN") is {Length:>0} liveIn&&Environment.GetEnvironmentVariable("EB_PREVIEW_LIVE_OUT") is {Length:>0} liveOut&&File.Exists(liveIn))
            {
                using var jdoc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(liveIn));
                var liveShots=System.Text.Json.JsonSerializer.Deserialize<CombatReportShot[]>(jdoc.RootElement.GetProperty("shots").GetRawText(),new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
                var liveHist=new CombatReportHistory();liveHist.Restore(liveShots,jdoc.RootElement.GetProperty("omittedShots").GetInt32());
                File.WriteAllText(liveOut,CombatReportHtml.Render(liveHist));
            }
            var xdoc=System.Xml.Linq.XDocument.Parse(html);
            Check(html.Contains("<div class='blast-kpis'>",StringComparison.Ordinal)
                &&html.Contains("<div class='fx-blast-diagram'>",StringComparison.Ordinal)
                &&html.Contains("EXTERIOR",StringComparison.Ordinal)
                &&html.Contains("Blast capacity vs. armour thickness",StringComparison.Ordinal)
                &&!html.Contains("<div class='blast'>",StringComparison.Ordinal)
                &&!html.Contains("<div class='fx-blast'>",StringComparison.Ordinal)
                &&!html.Contains("svg class='wf'",StringComparison.Ordinal)
                &&!html.Contains("class='fx-mode'",StringComparison.Ordinal)
                &&xdoc.Root!=null,
                "CombatReportHtml & CombatForensicHtml render clean KPI grid, HE blast SVG & capacity comparison with valid XML and zero AP/box clutter");
            Check(CombatReportView.ResultChipWidth>=CombatReportView.ChipWidth(HitBlockBuilder.Breached)
                &&CombatReportView.ResultChipWidth>=CombatReportView.ChipWidth(HitBlockBuilder.StoppedBlast),
                "ResultChipWidth fits BREACHED and STOPPED · blast damage without truncation");

            var exempt=new[]{"Target glacis","T-80BVM","Target roof","Own tank","HE","Plate #311","Plate #350","Plate #412","Rolled homogeneous armour","T-80U cellular cast armour","Composite armour plate [Solid block]","[Solid block]"};
            foreach(var fx in new[]{fx1,fx2,fx3})
            {
                var fxTexts=new[]{fx.Outcome,fx.Explanation,fx.Context}.Concat(fx.Findings).Concat(fx.Advice);
                var jargon=fxTexts.SelectMany(t=>CombatReportLanguage.JargonIn(t,exempt).Select(j=>j+" in: "+t)).ToArray();
                var unc=fxTexts.SelectMany(t=>CombatReportLanguage.UncertaintyIn(t,exempt).Select(u=>u+" in: "+t)).ToArray();
                Check(jargon.Length==0&&unc.Length==0,$"Forensic HE scenario #{fx.Number} has 0 jargon and 0 uncertainty"+(jargon.Length+unc.Length>0?": "+string.Join(" | ",jargon.Concat(unc).Take(4)):""));
            }
        }
        finally{BallisticsEffectApi.ReportSink=prevSink;BallisticsEffectApi.Clear();}
    }

    private static string FindRoot()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"SprocketExpandedBallistics.csproj")))return d.FullName;
        return Directory.GetCurrentDirectory();
    }
}
