using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

internal static class Fix02247IntervalTests
{
    internal static readonly string[] Changed={"Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md","NativeMaterialReactions.cs","NativeMaterialIntervals.cs","MaterialIntervalLedger.cs","RuntimeGroupedProtection.cs","ProtectionFactorTable.cs","CombatHitProjection.cs","CombatForensics.cs","CombatForensicHtml.cs","CombatForensicPanel.cs","CombatReport.cs","RuntimeDamageFeed.cs","MaterialIntervalDisplay.cs","RuntimeArmourResponses.cs","MaterialFingerprint.cs","EraRejectionReport.cs","CombatReportView.cs","CombatReportPanel.cs","CombatReportHtml.cs"};
    private static long Bits(double x)=>BitConverter.DoubleToInt64Bits(x);
    private static MaterialInterval I(int entity,double a,double b,double factor,string material="rha")=>new(entity,material,material,a,b,factor,0);
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,msg)=>{count++;sink(ok,"fix02247: "+msg);};
        string root=Fix02228Tests.SourceRoot(),parent=Fix02228Tests.SessionRoot(),output=Fix02228Tests.OutputRoot();
        check(GroupedProtection.Mode==GroupedBlockMode.PerInterval&&GroupedProtection.CompositeProtectionScale==1,"defaults PerInterval / 1");
        var ray=new MaterialRayCoordinates(new(1000,1000,1000),System.Numerics.Vector3.UnitZ);
        check(ray.Project(new(1000,1000,1000),System.Numerics.Vector3.UnitZ,10)==ray.Project(new(1000,1000,1007),System.Numerics.Vector3.UnitZ,3),"stable ray coordinate survives child spawn shift");
        var own=ConfirmedMaterialIntervals.Pair(new[]{new MaterialBoundary(1,true,false,65),new(1.123,false,true,0),new(2,true,false,60),new(2.2,false,true,0)});
        check(own.Length==2&&own[0]==(1,1.123,65)&&own[1]==(2,2.2,60),"own entry/exit pairs preserve separate chords and entry angle");
        check(ConfirmedMaterialIntervals.Pair(new[]{new MaterialBoundary(1,true,false,65)}).Length==0,"membership/entry without own exit confirms nothing");
        check(ConfirmedMaterialIntervals.Pair(new[]{new MaterialBoundary(1,false,true,65)}).Length==0,"exit without own entry confirms nothing");
        check(ConfirmedMaterialIntervals.Pair(new[]{new MaterialBoundary(1,true,true,65)}).Length==0,"zero contact confirms no material");
        var ledger=new MaterialIntervalLedger();var all=new[]{I(1,0,.123,1.16,"highHardnessSteel"),I(336,.2,.897,.85,"ebCassette_t80u_hull"),I(1225,-.012,0,1)};
        var a=ledger.Plan(-.012,.123,all)!;ledger.Commit(a);
        var b=ledger.Plan(0,.3,all)!;ledger.Commit(b);
        var c=ledger.Plan(.2,.897,all)!;ledger.Commit(c);
        var pieces=a.Pieces.Concat(b.Pieces).Concat(c.Pieces).ToArray();
        check(Math.Abs(pieces.Where(p=>p.Entity==1).Sum(p=>p.LengthMm)-123)<1e-10,"T80 HHS paid once, 123 mm");
        check(Math.Abs(pieces.Where(p=>p.Entity==336).Sum(p=>p.LengthMm)-697)<1e-10,"T80 cassette paid once, 697 mm");
        double total=pieces.Sum(p=>p.RhaMm);
        check(Math.Abs(total-747.13)<1e-9&&total<880,"conditional T80 total 747.13 including 12 mm frame, below old 880–930 range");
        check(ledger.Plan(-.012,.897,all)!.Factor==0,"repeated block pays nothing");
        check(new MaterialIntervalLedger().Plan(0,1,Array.Empty<MaterialInterval>())!.Factor==0,"uncovered material and air zero");
        var overlap=new MaterialIntervalLedger();var p=overlap.Plan(0,1,new[]{I(2,0,1,.5),I(1,.2,.8,1.5)})!;
        check(p.Ambiguous&&Math.Abs(p.Factor-1.1)<1e-12,"ambiguous overlap highest factor once");overlap.Commit(p);
        check(overlap.Plan(0,1,new[]{I(2,0,1,.5)})!.Factor==0,"losing overlap member cannot be charged later");
        check(new MaterialIntervalLedger().Plan(0,1,new[]{I(1,0,.25,2),I(1,.75,1,2)})!.Factor==1,"disconnected own volumes do not fill the gap");
        check(new MaterialIntervalLedger().Plan(0,0,all)==null,"zero native block no division and no claim");
        check(new MaterialIntervalLedger().Plan(double.NaN,1,all)==null,"invalid native span rejected");
        // Random interval union oracle on an integer grid, independent from the sweep implementation.
        var random=new Random(2247);
        for(int trial=0;trial<400;trial++)
        {
            var line=new MaterialIntervalLedger();var intervals=Enumerable.Range(0,8).Select(id=>{int start=random.Next(0,64),end=random.Next(start+1,65);return I(id,start,end,random.Next(0,9)*.25);}).ToArray();
            var paid=new bool[64];
            for(int block=0;block<8;block++)
            {
                int lo=random.Next(0,64),hi=random.Next(lo+1,65);double expected=0;
                for(int cell=lo;cell<hi;cell++)if(!paid[cell])expected+=intervals.Where(i=>i.StartM<=cell&&i.EndM>=cell+1).Select(i=>i.Factor).DefaultIfEmpty(0).Max();
                var plan=line.Plan(lo,hi,intervals)!;
                check(Math.Abs(plan.Factor*(hi-lo)-expected)<1e-10,"random grid union/max/unpaid oracle");line.Commit(plan);
                for(int cell=lo;cell<hi;cell++)if(intervals.Any(i=>i.StartM<=cell&&i.EndM>=cell+1))paid[cell]=true;
            }
        }
        // A solitary full native interval returns its factor without a multiply/divide round trip.
        for(int k=0;k<2000;k++)
        {
            double start=random.NextDouble()*100,end=start+.000001+random.NextDouble()*10,factor=random.NextDouble()*3;
            var plan=new MaterialIntervalLedger().Plan(start,end,new[]{I(1,start,end,factor)})!;
            check(Bits(plan.Factor)==Bits(factor),"ungrouped confirmed single-member factor bit-exact");
            foreach(double e in new[]{0d,1,1.33,2})check(BitConverter.SingleToInt32Bits((float)ProtectionFactorTable.NativeRhaFactor(plan.Factor,65,e,.3,.75))==BitConverter.SingleToInt32Bits((float)ProtectionFactorTable.NativeRhaFactor(factor,65,e,.3,.75)),"ungrouped native write bit-exact");
        }
        var mats=CassetteTechnology.ReadDirectory(Path.Combine(root,"native-data/Technology"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults",MaterialReactionCatalogue.FileName)));
        var catalogue=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults",CassetteCatalogue.FileName)));
        var models=catalogue.Presets.ToDictionary(preset=>preset.MaterialId,preset=>new CassetteModel(preset,mats,reactions));
        var current=ProtectionFactorTable.ForMaterials(models,reactions,mats,id=>id);
        var golden=Golden02246ProtectionFactorTable.ForMaterials(models,reactions,mats,id=>id);
        foreach(var id in current.Keys)foreach(var threat in ProtectionFactorTable.Threats)foreach(double angle in Enumerable.Range(0,681).Select(i=>i*.125))
        {
            double old=golden[id].Factor(threat,angle),now=current[id].Factor(threat,angle);
            check(Bits(old)==Bits(now),"scale 1 factor bit-exact frozen 0.22.46");
            GroupedProtection.CompositeProtectionScale=1.2;
            check(Bits(current[id].Factor(threat,angle))==Bits(models.ContainsKey(id)?old*1.2:old),"scale 1.2 only actual composer materials");
            GroupedProtection.CompositeProtectionScale=1;
        }
        foreach(var mode in new[]{GroupedBlockMode.PerInterval,GroupedBlockMode.Legacy})
        {
            GroupedProtection.Mode=mode;
            foreach(var id in current.Keys)foreach(var threat in ProtectionFactorTable.Threats)
                check(Bits(current[id].Factor(threat,69.2))==Bits(golden[id].Factor(threat,69.2)),"mode retains default-scale member mean factor");
        }
        GroupedProtection.Mode=GroupedBlockMode.PerInterval;
        // Native simple members retain their original float even when ERA changes the overmatch input before native runs.
        foreach(float raw in new[]{0f,.1f,.7f,1f,1.16f,2f})foreach(double exponent in new[]{0d,1,1.33,2})foreach(double om in new[]{0d,.25,.9,1})
        {
            double multiplier=ProtectionFactorTable.NativeRhaFactor(1,65,exponent,om,.75);
            var nativePlan=new MaterialIntervalLedger().Plan(0,.1,new[]{I(1,0,.1,raw/multiplier)})!;
            check(BitConverter.SingleToInt32Bits((float)ProtectionFactorTable.NativeRhaFactor(nativePlan.Factor,65,exponent,om,.75))==BitConverter.SingleToInt32Bits(raw),"simple native float preserved after overmatch refresh");
        }
        string[] fixtures={"zero-loss-1107","forensic-fixtures","forensic-fixtures-1108","forensic-fixtures-1108b","forensic-fixtures-1108c","t80u-1108"};
        var impact=new StringBuilder("fixture\tshot\told_total_RHAeq_mm\tnew_total_RHAeq_mm\tdifference_mm\told_reported_factor_lines_mm\tstatus\n");
        int shots=0,legacyFactors=0;
        foreach(string fixture in fixtures)
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(parent,fixture,"last-session.json")));
            var recorded=JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            foreach(var shot in recorded)
            {
                shots++;double reported=0;
                foreach(var evt in shot.Events)
                {
                    if(evt.Layer is {} layer)foreach(var part in layer.Parts)
                        if(part.MaterialId is {} id&&current.ContainsKey(id)&&part.AngleDegrees is {} angle)
                            foreach(var threat in ProtectionFactorTable.Threats){check(Bits(current[id].Factor(threat,angle))==Bits(golden[id].Factor(threat,angle)),"fixture Legacy lookup frozen parity");legacyFactors++;}
                    var match=Regex.Match(evt.Text,@"= ([0-9]+) mm RHA(?:;|$)");
                    if(evt.Kind is "materialReaction" or "materialStop"&&match.Success)reported+=double.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
                }
                // Recorded reports do not contain per-member positions/flags, nor all native passive RHA charges.
                impact.AppendLine(FormattableString.Invariant($"{fixture}\t{shot.Number}\t\t\t\t{reported}\tUNDETERMINED: own entry/exit coordinates and full native material charges absent"));
            }
        }
        File.WriteAllText(Path.Combine(output,"INTERVAL-IMPACT.tsv"),impact.ToString());
        check(shots>=150&&legacyFactors>0,"all requested saved shots covered by lookup parity, no fabricated geometry replay");
        // 0.22.36 wedge / #165: output is conditional because their own coordinates were never recorded.
        var wedge=new MaterialIntervalLedger().Plan(0,2.366,new[]{I(415,0,.73371,.68),I(278,.9,1.832,.52527)})!;
        double wedgeScenario=wedge.RhaMm;
        check(wedgeScenario<2366*.68,"conditional wedge example does not give 2366 mm to the wedge");
        var boundary=new MaterialIntervalLedger().Plan(0,.437,Array.Empty<MaterialInterval>())!;
        check(boundary.RhaMm==0,"#165 zero/unconfirmed own chord creates no synthetic full-face resistance");
        // Report display test uses an actual saved layer identity with one new planner result.
        using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(parent,"t80u-1108/last-session.json"))))
        {
            var shot=JsonSerializer.Deserialize<CombatReportShot>(doc.RootElement.GetProperty("shots")[3].GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            var startEvent=shot.Events.First(e=>e.Layer?.Parts.Any(q=>q.Entity==1)==true);
            var changed=shot with {Events=shot.Events.Append(new CombatReportEvent("materialReaction",startEvent.Stage,a.Text,startEvent.BeforeMm,500,startEvent.HitId){PaidIntervals=a.Pieces}).ToArray()};
            var forensic=CombatForensics.Build(changed);
            check(forensic.Layers.Any(l=>l.PaidIntervals?.Contains(MaterialIntervalDisplay.Piece(a.Pieces[0]))==true),"forensics retains exact block paid intervals");
            var html=CombatForensicHtml.Render(forensic,"interval-2247");
            check(html.Contains("High-hardness steel")&&html.Contains("RHA"),"forensic HTML displays own lengths, factors and RHA-eq");
            check(CombatHitProjection.Build(changed).Any(h=>h.Reaction.Contains("High-hardness steel:")),"combat report preserves scientific interval attribution");
            var longPieces=Enumerable.Range(0,20).Select(i=>new PaidMaterialPiece(i,"rha","Rolled homogeneous armour",i*.01,(i+1)*.01,1,0)).ToArray();
            var longEvent=new CombatReportEvent("materialReaction",startEvent.Stage,"Confirmed material intervals: long block",startEvent.BeforeMm,500,startEvent.HitId){PaidIntervals=longPieces};
            var normalized=CombatReportTruth.Normalize(shot with{Events=new[]{startEvent,longEvent}});
            var roundtrip=JsonSerializer.Deserialize<CombatReportShot>(JsonSerializer.Serialize(normalized))!;
            check(roundtrip.Events[1].PaidIntervals!.Length==20&&roundtrip.Events[1].PaidIntervals![19]==longPieces[19],"all paid pieces survive normalization and JSON roundtrip beyond the 512-character summary");
            var longView=CombatForensics.Build(roundtrip);
            check(longView.Layers[0].PaidIntervals!.Count==20,"forensic view displays every nonzero paid piece of a long block");
            File.WriteAllText(Path.Combine(output,"preview/interval-attribution.html"),html);
        }
        double p0=604,afterA=CassetteImpactPath.NativePassive(p0,a.RhaMm),afterB=CassetteImpactPath.NativePassive(afterA,b.RhaMm),margin=c.RhaMm-afterB;
        check(margin>0&&CassetteImpactPath.NativePassive(afterB,c.RhaMm)==0,"conditional 604 mm rod stops in cassette");
        File.WriteAllText(Path.Combine(output,"FIX47-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=count,fixtureShots=shots,legacyFactorComparisons=legacyFactors,t80Conditional=new{hhsMm=123,cassetteMm=697,frameMm=12,totalRhaMm=total,rodMm=p0,linearExcessMm=total-p0,blockARhaMm=a.RhaMm,blockBRhaMm=b.RhaMm,blockCRhaMm=c.RhaMm,afterAMm=afterA,afterBMm=afterB,stopMarginMm=margin,outcome="STOP"},wedgeConditionalRhaMm=wedgeScenario,changedShots=(int?)null,meanChangeRhaMm=(double?)null,maximumChangeRhaMm=(double?)null,fixtureImpact="undetermined: saved data lacks own intervals",liveValidation=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FIX02247 PASS: {count} checks; {shots} saved shots; {legacyFactors} Legacy factor comparisons; full geometry replay unavailable.");
    }
    private static string Method(string source,string name)
    {
        var match=Regex.Match(source,@"(?:private|internal) static [^\n]+\b"+name+@"\(");
        if(!match.Success)throw new Exception("method unavailable "+name);
        int a=source.IndexOf('{',match.Index),depth=0;
        // Method bodies in this comparison contain interpolation braces in balanced pairs.
        for(int i=a;i<source.Length;i++){if(source[i]=='{')depth++;if(source[i]=='}'&&--depth==0)return source[a..(i+1)];}
        throw new Exception("unbalanced method "+name);
    }
}
