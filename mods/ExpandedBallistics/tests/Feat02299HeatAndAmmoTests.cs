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
        Check(Read("Plugin.cs").Contains("\"0.22.109\"",StringComparison.Ordinal)
            &&Read("SprocketExpandedBallistics.csproj").Contains("<Version>0.22.109</Version>",StringComparison.Ordinal)
            &&Read("RELEASE-NOTES.md").Contains("## 0.22.109",StringComparison.Ordinal),"version 0.22.109 in Plugin.cs, csproj, RELEASE-NOTES");
        var spallSrc=Read("RuntimeSpall.cs");
        Check(spallSrc.Contains("record struct LayerScope",StringComparison.Ordinal)
            &&spallSrc.Contains("record struct BurstState",StringComparison.Ordinal)
            &&!spallSrc.Contains("new float[n]",StringComparison.Ordinal)
            &&!spallSrc.Contains("new bool[n]",StringComparison.Ordinal),
            "RuntimeSpall hot path uses value structs and zero-alloc GapLookahead");
        var fxSrc=Read("RuntimeShellEffects.cs");
        Check(fxSrc.Contains("class BallisticsEffectApi",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionKind",StringComparison.Ordinal)
            &&fxSrc.Contains("CurrentExplosionVisualOnly",StringComparison.Ordinal),
            "BallisticsEffectApi exposed in RuntimeShellEffects.cs");
        Check(Read("RuntimeMixedAmmoStorage.cs").Contains("PrepareRequest(LoadTask task)",StringComparison.Ordinal),
            "AmmoLoadoutApi.PrepareRequest exposed for autoloader sync");
        var hooks=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)
            .Where(f=>{var r=f.Replace('\\','/');return !r.Contains("/tests/")&&!r.Contains("/bin/")&&!r.Contains("/obj/");})
            .Sum(f=>File.ReadAllLines(f).Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)));
        Check(hooks==48,$"exact 48 [HarmonyPatch annotations preserved (actual={hooks})");

        Console.WriteLine($"0.22.99 HEAT & ammo checks PASS: {count} checks.");
    }

    private static string FindRoot()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"SprocketExpandedBallistics.csproj")))return d.FullName;
        return Directory.GetCurrentDirectory();
    }
}
