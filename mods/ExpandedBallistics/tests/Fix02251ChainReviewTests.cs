using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.51 part D: one shot through the whole "damage through thicknesses" chain, as far as it can be proven without the game:
/// the interval ledger (every millimetre once, with its own factor), the factor write/restore protocol, reads after writes,
/// face thickness, ERA end to end and the report sums.
/// </summary>
internal static class Fix02251ChainReviewTests
{
    private struct Rha:IRhaArray
    {
        internal readonly float[] Values;internal int Writes;internal bool ThrowOnSecond;
        internal Rha(params float[] v){Values=v;Writes=0;ThrowOnSecond=false;}
        public float Read(int index)=>Values[index];
        public void Write(int index,float value){if(ThrowOnSecond&&++Writes==2)throw new InvalidOperationException("native accessor");Values[index]=value;}
    }
    /// <summary>The same shot without the 0.22.51 merge of settled blocks (their ledger lines removed): the 0.22.50 row structure.</summary>
    internal static CombatReportShot Unmerged(CombatReportShot shot)
    {
        var a=CombatLayerContinuity.Analyse(shot);
        return shot with{Events=shot.Events.Select(e=>e.HitId!=null&&a.IsSettled(e.Stage,e.HitId)&&e.PaidIntervals!=null?e with{PaidIntervals=null}:e).ToArray()};
    }
    internal static bool OriginalProtectionCode(string root,string baseline)
    {
        string Normalize(string t)=>t.Replace("\r\n","\n");
        var current=Normalize(File.ReadAllText(Path.Combine(root,"ProtectionFactorTable.cs")));
        int begin=current.IndexOf("    // Always attempt both native arrays:",StringComparison.Ordinal);
        int end=current.IndexOf("    internal static float Set<T>",begin,StringComparison.Ordinal);
        return begin>=0&&end>begin&&current.Remove(begin,end-begin)==Normalize(File.ReadAllText(Path.Combine(baseline,"ProtectionFactorTable.cs")));
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,msg)=>{count++;sink(ok,"fix02251-D: "+msg);};
        string root=Fix02228Tests.SourceRoot(),parent=Fix02228Tests.SessionRoot(),output=Fix02228Tests.OutputRoot();
        string Read(string rel)=>File.ReadAllText(Path.Combine(root,rel));
        // ---- D1: every millimetre once, with its own factor ----
        var ledger=new MaterialIntervalLedger();
        // A folded plate (entity 516) crossed twice with air between: both chords are paid, each once.
        var first=ledger.Plan(0,.006,new[]{new MaterialInterval(516,"rha","rha",0,.006,1,28)})!;ledger.Commit(first);
        var second=ledger.Plan(.0374,.0496,new[]{new MaterialInterval(516,"rha","rha",.0374,.0496,1,65.7)})!;ledger.Commit(second);
        check(Math.Abs(first.RhaMm-6)<1e-9&&Math.Abs(second.RhaMm-12.2)<1e-9,"two disjoint crossings of one object are each paid once");
        // Native reports the second chord again inside a later block: nothing is left to pay (the 0.22.51 settled block).
        var again=ledger.Plan(.0374,.0496,new[]{new MaterialInterval(516,"rha","rha",.0374,.0496,1,65.7)})!;
        check(again.Pieces.Length==0&&again.RhaMm==0&&again.Factor==0,"a block whose material is already paid pays 0 mm (merged into the paying row in the report)");
        // Grouped block: frame (RHA 1.0) and HHS (1.16) overlapping 1 mm, cassette (0.85) behind; overlap counted once at the highest factor.
        var grouped=new MaterialIntervalLedger();
        var g=grouped.Plan(0,.010,new[]{new MaterialInterval(1225,"rha","rha",0,.005,1,68),new MaterialInterval(1,"highHardnessSteel","HHS",.004,.008,1.16,68),
            new MaterialInterval(336,"ebCassette","T-80U",.008,.010,.85,69)},.010)!;
        check(g.Ambiguous&&Math.Abs(g.RhaMm-(4*1+4*1.16+2*.85))<1e-9,"overlap paid once with the highest factor; each member with its own factor");
        grouped.Commit(g);
        var later=grouped.Plan(.002,.012,new[]{new MaterialInterval(1225,"rha","rha",.002,.005,1,68),new MaterialInterval(336,"ebCassette","T-80U",.008,.012,.85,69)},.010)!;
        check(Math.Abs(later.RhaMm-2*.85)<1e-9,"a later block pays only the unpaid remainder (2 mm cassette), never the frame again");
        // The losing member of an overlap cannot be paid later under its own entity.
        var loser=grouped.Plan(.004,.005,new[]{new MaterialInterval(1225,"rha","rha",.004,.005,1,68)},.001)!;
        check(loser.RhaMm==0,"an ambiguous overlap claimed by the winner is not paid again for the loser");
        // A face scaled to 70 %: same factor, 0.7 x the path (0.22.50).
        var full=new MaterialIntervalLedger().Plan(0,.2,new[]{new MaterialInterval(9,"c","c",0,.2,.85,30)})!;
        var scaled=new MaterialIntervalLedger().Plan(0,.14,new[]{new MaterialInterval(9,"c","c",0,.14,.85,30)})!;
        check(full.Factor==scaled.Factor&&Math.Abs(scaled.RhaMm/full.RhaMm-.7)<1e-12,"a scaled face keeps its factor and pays its own path length");
        // A continuation (child fragment) on the same ray shares the ledger through the persistent ray coordinates.
        var coordinates=new MaterialRayCoordinates(new(0,0,0),new(0,0,1));
        check(Math.Abs(coordinates.Project(new(0,0,.1f),new(0,0,1),.05)-.15)<1e-7,"child spawn + distance maps to the same ray coordinate as the parent");
        var native=Read("NativeMaterialIntervals.cs");
        check(native.Contains("IntervalLineageRoot(sim,reached.Index)")&&native.Contains("rays.LastOrDefault(r=>r.Contains(fragment.spawnPoint,direction))"),"one ledger per penetrator line (lineage root, persistent ray)");
        check(native.Contains("FragmentFlag.OriginalPenetrator|FragmentFlag.Killed))!=FragmentFlag.OriginalPenetrator"),"spall fragments never pay or write (native charges them itself)");
        check(native.Contains("if(!solid)return; // Air confirms geometry but never pays it."),"air confirms geometry only");
        // ---- D2: every EB write is restored ----
        var reactions=Read("NativeMaterialReactions.cs");
        check(Regex.Matches(reactions,@"\bSetRha\(").Count==5&&reactions.Contains("MaterialFactorWrite.RestorePair(ref primary,ref extra"),"SetRha: definition + 4 writes; restore delegates both arrays to the exercised production helper");
        var write=reactions.Substring(reactions.IndexOf("private static void WriteFactor(",StringComparison.Ordinal));write=write[..write.IndexOf("internal static void CaptureBlockBudget",StringComparison.Ordinal)];
        check(write.IndexOf("(writes??=new()).Add(",StringComparison.Ordinal)<write.IndexOf("SetRha(materials",StringComparison.Ordinal)&&write.Contains("catch\n        {\n            Restore(sim.Pointer,reached.Index);\n            throw;")||
              write.IndexOf("(writes??=new()).Add(",StringComparison.Ordinal)<write.IndexOf("SetRha(materials",StringComparison.Ordinal)&&Regex.IsMatch(write,@"catch\s*\{\s*Restore\(sim\.Pointer,reached\.Index\);\s*throw;"),
            "WriteFactor registers the restore before the first mutation and restores on an exception");
        var refresh=reactions.Substring(reactions.IndexOf("internal static void CaptureBlockBudget",StringComparison.Ordinal));refresh=refresh[..refresh.IndexOf("// The block carries another material",StringComparison.Ordinal)];
        check(Regex.Matches(refresh,@"if\(read!=value\)\{Restore\(sim\.Pointer,index\);return;\}").Count==2&&Regex.Matches(refresh,@"if\(refreshed==null\)\{Restore\(sim\.Pointer,index\);return;\}").Count==2,
            "every refresh write is read back and restores on mismatch");
        var close=reactions.Substring(reactions.IndexOf("internal static void Close(",StringComparison.Ordinal));
        check(Regex.IsMatch(close,@"^internal static void Close\(PenetrationSimulation sim,short index,int countBefore\)\s*\{\s*var write=Restore\(sim\.Pointer,index\);"),"Close restores before anything else");
        var responses=Read("RuntimeArmourResponses.cs");
        check(Regex.IsMatch(Feat02278HeshTests.Unhesh(responses),@"finally\s*\{\s*// 0\.22\.28: the block is done[^\n]*\n\s*try\{if\(current!=null\)NativeMaterialReactions\.Close\(current\.Sim,current\.Index,current\.CountBefore\);\}"),
            "the SimulateFragment finalizer restores in a finally block (exception, early return, native stop or deflection)");
        check(responses.Contains("[HarmonyFinalizer,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]"),"restore runs in a Harmony finalizer, which also runs when native throws");
        var sources=Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories).Where(p=>!p.Contains(Path.DirectorySeparatorChar+"tests"+Path.DirectorySeparatorChar)&&!p.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar)).ToArray();
        var writers=sources.Where(p=>Regex.IsMatch(File.ReadAllText(p),@"Unsafe\.AsRef\(in [^)]*\.(rhaFactor|density|spallFactor)\)|\.(rhaFactor|density|spallFactor)\s*=[^=]")).Select(p=>Path.GetFileName(p)).ToArray();
        check(writers.SequenceEqual(new[]{"NativeMaterialReactions.cs"}),"only NativeMaterialReactions writes a MaterialInfo field ("+string.Join(",",writers)+")");
        check(Regex.Matches(reactions,@"Unsafe\.AsRef\(in element\.(\w+)\)").Cast<Match>().All(m=>m.Groups[1].Value=="rhaFactor"),"EB never writes density or spallFactor");
        // Managed model of the native accessor: nested writes restore LIFO to the true original; a throwing accessor leaves no write behind.
        var array=new Rha(1f,.15f);
        var a=MaterialFactorWrite.Set(ref array,1,.139f,out var readA);var b=MaterialFactorWrite.Set(ref array,1,.2f,out var readB);
        MaterialFactorWrite.Set(ref array,1,b,out _);MaterialFactorWrite.Set(ref array,1,a,out _);
        check(readA==.139f&&readB==.2f&&array.Values[1]==.15f,"nested writes restore last-in first-out to the recipe value");
        var fragile=new Rha(.85f){ThrowOnSecond=true};var original=MaterialFactorWrite.Set(ref fragile,0,.9f,out _);bool threw=false;
        try{MaterialFactorWrite.Set(ref fragile,0,.95f,out _);}catch(InvalidOperationException){threw=true;}
        fragile.ThrowOnSecond=false;MaterialFactorWrite.Set(ref fragile,0,original,out _);
        check(threw&&fragile.Values[0]==.85f,"an accessor failure mid-write is followed by an exact restore");
        var broken=new Rha(.2f){ThrowOnSecond=true,Writes=1};var argument=new Rha(.3f);int restoreErrors=0;
        check(!MaterialFactorWrite.RestorePair(ref broken,ref argument,true,0,.15f,.16f,_=>restoreErrors++)&&argument.Values[0]==.16f&&restoreErrors==1,
            "production restore helper restores the argument array even if the primary accessor throws");
        broken.ThrowOnSecond=false;
        check(MaterialFactorWrite.RestorePair(ref broken,ref argument,true,0,.15f,.16f)&&broken.Values[0]==.15f&&argument.Values[0]==.16f,
            "production restore helper verifies byte-exact original values for both arrays");
        check(reactions.Contains("while(writes is {Count:>0} list)")&&!reactions.Contains("writes?.Clear()"),"Edit reset drains active restore records rather than discarding temporary material writes");
        // ---- D3: no check reads a value EB wrote ----
        var w1=new MaterialFingerprintWrite((IntPtr)10,3,new(4000,.15f,.25f),IntPtr.Zero,default);
        var w2=new MaterialFingerprintWrite((IntPtr)10,3,new(4000,.139f,.25f),(IntPtr)20,new(4000,.139f,.25f));
        check(MaterialFingerprint.BeforeWrites((IntPtr)10,3,new(4000,.2f,.25f),new[]{w1,w2}).RhaFactor==.15f,"the oldest pre-write value wins for nested writes on one array");
        check(MaterialFingerprint.BeforeWrites((IntPtr)20,3,new(4000,.2f,.25f),new[]{w2}).RhaFactor==.139f,"a separately passed array keeps its own original");
        check(MaterialFingerprint.BeforeWrites((IntPtr)10,4,new(1,1,1),new[]{w1}).RhaFactor==1,"another material index reads live (no write)");
        check(native.Contains("OriginalMaterial(sim,obj.materialIndex).RhaFactor")&&!Regex.IsMatch(native,@"sim\.materials\[obj\.materialIndex\]\.rhaFactor"),
            "PerInterval pays a simple member at its pre-write native factor (0.22.51)");
        check(responses.Contains("var passive=NativeMaterialReactions.OriginalMaterial(sim,passiveIndex);")&&!responses.Contains("livePassive"),"every recipe check compares the pre-write material (0.22.51; ERA since 0.22.49)");
        var spall=Read("RuntimeSpall.cs");
        check(spall.Contains("length*=Math.Max(0,__5[__result.MaterialIndex].rhaFactor);")&&responses.Contains("[HarmonyPriority(Priority.Last)]\n    private static void Layer(")||responses.Contains("[HarmonyPriority(Priority.Last)]\r\n    private static void Layer("),
            "the spaced-armour reader runs before the block write (factor postfix has Priority.Last)");
        // ---- D5: ERA end to end ----
        var catalogue=SprocketExpandedBallistics.ArmourResponses.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sprocket.expandedballistics.responses.json")));
        var relikt=catalogue.Responses.First(r=>r.ResponseId=="reliktActive");
        var shotState=new ArmourShotState();var key=new ArmourHitKey(1,984,5,11.5146,11.5369);
        // The dynamic rod model supplies the loss for Relikt (log #984: factor 0.760, cap 0.65).
        var fresh=ArmourResponseModel.Apply(catalogue,relikt,shotState,key,"apfsds",true,true,13,54.29,double.NaN,true,.24,.65,threat:"longRod");
        var repeat=ArmourResponseModel.Apply(catalogue,relikt,shotState,key,"apfsds",true,true,13,54.29,double.NaN,true,.24,.65,threat:"longRod");
        var spentState=new ArmourShotState();var spentCell=ArmourResponseModel.Apply(catalogue,relikt,spentState,key,"apfsds",true,true,13,54.29,double.NaN,false,.24,.65,threat:"longRod");
        check(fresh<1&&repeat==1&&spentCell==1,"fresh element reduces once per block; a spent element gives no second reaction");
        check(ArmourResponseModel.EraAngularWeight(relikt,"longRod",54.29) is var weight&&Math.Abs(weight-.686)<.002,"angle weight at 54.3 degrees matches the logged 68.6 % (#984)");
        // Housing statement: once per housing per shot (runtime) and never repeated on a later wall (report).
        check(responses.Contains("if(scope.Shot.HousingsEvaluated.Count>=256||!scope.Shot.HousingsEvaluated.Add(entryEntity))return;"),"runtime decides the housing-only statement once per housing per shot");
        var d=Fix02251DuplicateRowTests.Load(parent,"era-1108d");
        foreach(var s in d)
        {
            var rows=CombatHitProjection.Build(s);
            foreach(var group in rows.Where(r=>r.Reaction.Contains(EraRejectionReport.Housing,StringComparison.Ordinal)).GroupBy(r=>r.Layer?.Parts.FirstOrDefault()?.Entity))
                check(group.Count()==1,$"era-1108d #{s.Number}: housing statement shown once for housing {group.Key}");
        }
        var s5=d.Single(s=>s.Number==5);var r5=CombatHitProjection.Build(s5);
        check(!r5.Any(r=>r.Layer?.Parts.Any(p=>p.Entity==905)==true&&r.Reaction.Contains(EraRejectionReport.Housing,StringComparison.Ordinal)),"era-1108d #5: housing #905 (whose element #907 initiated) is not called 'between explosive elements'");
        var s2=CombatHitProjection.Build(d.Single(s=>s.Number==2));
        check(s2.Count(r=>r.Reaction.Contains(EraRejectionReport.Housing,StringComparison.Ordinal))==1,"era-1108d #2: the housing-only statement stays on the first crossing of #1037");
        check(s2.Any(r=>r.Layer?.Parts.Any(p=>p.Entity==1037)==true&&r.Crossing==2&&r.Before>r.After&&r.Name.EndsWith("(second crossing)",StringComparison.Ordinal)),
            "era-1108d #2: suppressing a repeated housing explanation retains the second wall and its native loss");
        // ---- D6: report sums and verdict over every fixture ----
        var replay=new StringBuilder("fixture\tshot\tinitial_mm\tresidual_mm\tlayer_losses_mm\tbetween_mm\tunmerged_layer_losses_mm\tunmerged_between_mm\toutcome\tunmerged_outcome\tblock_result\tpaid_rha_eq_mm\tera\tera_reason\n");
        int shots=0;
        foreach(var fixture in Fix02251DuplicateRowTests.Fixtures)
            foreach(var s in Fix02251DuplicateRowTests.Load(parent,fixture))
            {
                shots++;var m=CombatForensics.Build(s);var o=CombatForensics.Build(Unmerged(s));var block=HitBlockBuilder.Build(CombatReportTruth.Normalize(s));
                double Losses(ForensicModel x)=>x.Layers.Where(l=>l.Stage!="Precursor").Sum(l=>l.AbsorbedMm??0);
                var absorbed=Losses(m);var before=Losses(o);
                // Merging moves a settled block's native loss into the row that paid its material: the total, the residual and the verdict stay.
                check(m.Residual==o.Residual&&m.Initial==o.Initial,$"residual unchanged by the merge: {fixture} #{s.Number}");
                check(Math.Abs(absorbed+(m.UnattributedMm??0)-before-(o.UnattributedMm??0))<1e-6,$"sum of the losses unchanged (rows + between): {fixture} #{s.Number}");
                check(Math.Abs(m.UnattributedMm??0)<=Math.Abs(o.UnattributedMm??0)+1e-6,$"no loss moves into 'between layers': {fixture} #{s.Number}");
                check(m.Outcome==o.Outcome&&m.Protection.Perforated==o.Protection.Perforated,$"verdict unchanged (0.22.43 envelope rule): {fixture} #{s.Number}");
                check(!m.Protection.Perforated||m.Outcome==HitBlockBuilder.Penetrated||m.Outcome==block.Result||m.Outcome=="RICOCHET","perforated envelope gives PENETRATED: "+fixture+" #"+s.Number);
                var paid=s.Events.SelectMany(x=>x.PaidIntervals??Array.Empty<PaidMaterialPiece>()).Sum(p=>p.RhaMm);
                var era=s.Events.Any(x=>x.Kind is "heavyEra" or "lightEra")?"yes":"no";
                var reasons=string.Join(" | ",CombatHitProjection.Build(s).SelectMany(r=>r.Reaction.Split('\n')).Where(l=>l.StartsWith("Explosive",StringComparison.Ordinal)||l.StartsWith("Module housing",StringComparison.Ordinal)||l.StartsWith("ERA ",StringComparison.Ordinal)).Distinct());
                replay.AppendLine(FormattableString.Invariant($"{fixture}\t{s.Number}\t{m.Initial:0.###}\t{m.Residual:0.###}\t{absorbed:0.###}\t{m.UnattributedMm:0.###}\t{before:0.###}\t{o.UnattributedMm:0.###}\t{m.Outcome}\t{o.Outcome}\t{block.Result}\t{paid:0.###}\t{era}\t{reasons}"));
            }
        File.WriteAllText(Path.Combine(output,"CHAIN-02251-NEW.tsv"),replay.ToString());
        File.WriteAllText(Path.Combine(output,"FIX51D-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=count,shots}));
        Console.WriteLine($"FIX02251 D PASS: {count} checks; {shots} fixture shots replayed through the report chain.");
    }
}
