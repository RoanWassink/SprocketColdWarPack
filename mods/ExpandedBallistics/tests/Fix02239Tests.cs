using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Security.Cryptography;
using SprocketExpandedBallistics.ExperimentalArmour;
using Old=SprocketExpandedBallistics.Golden02238;
namespace SprocketExpandedBallistics;

internal static class Fix02239Tests
{
    private static int evaluated;
    private static string Probe(){evaluated++;return "must not evaluate";}
    private static void Trace(ref PlacedEraTraceHandler detail)=>PlacedEraTrace.Write(ref detail);
    internal static void Run(Action<bool,string> check)
    {
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        int count=0;var sink=check;check=(ok,message)=>{count++;sink(ok,"fix02239: "+message);};
        string Read(string rel)=>File.ReadAllText(Path.Combine(root,rel));
        PlacedEraTrace.Enabled=()=>false;PlacedEraTrace.Live=()=>true;
        PlacedEraTrace.Built=PlacedEraTrace.Written=0;evaluated=0;int lines=0;PlacedEraTrace.Sink=_=>lines++;
        for(int i=0;i<1000;i++)Trace($"stage=hit value={Probe()} i={i:0.000}");
        var bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++)Trace($"stage=hit value={Probe()} i={i:0.000}");
        var allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
        check(allocated==0&&evaluated==0&&lines==0&&PlacedEraTrace.Built==0&&PlacedEraTrace.Written==0,"logging off: 100000 calls, zero bytes, trace lines, string builds and interpolation evaluations");
        check(Read("ReleaseDiagnostics.cs").Contains("\"Detailed logging\",false"),"production detailed logging defaults off");
        PlacedEraTrace.Enabled=()=>true;PlacedEraTrace.LiveBudget=1;
        Trace($"enabled={7}");Trace($"capped={Probe()}");
        check(lines==1&&evaluated==0&&PlacedEraTrace.Built==1,"enabled trace emitted; exhausted budget skips interpolation");
        PlacedEraTrace.Enabled=()=>false;
        File.WriteAllText(Path.Combine(output,"TRACE-02239.json"),JsonSerializer.Serialize(new{calls=100000,allocatedBytes=allocated,traceLines=0,stringBuilds=0,interpolationEvaluations=0}));

        var cells=new EraCells();var old=new Old.EraCells();var rng=new Random(2239);
        // Slot-reuse ordering is part of the old observable Cells contract, not merely set equality.
        for(int i=0;i<5000;i++)
        {
            long spawn=rng.Next(1,5);int element=rng.Next(1,17);var cell=new EraCell(spawn,element,rng.Next(2),rng.Next(4),rng.Next(3));
            var prior=new Old.EraCell(cell.Spawn,cell.Element,cell.Surface,cell.X,cell.Y);
            switch(rng.Next(16))
            {
                case 0:cells.RemoveSpawn(spawn);old.RemoveSpawn(spawn);break;
                case 1:cells.RemoveElement(spawn,element);old.RemoveElement(spawn,element);break;
                case 2:cells.Remove(cell);old.Remove(prior);break;
                case 3:if(i%101==0){cells.Clear();old.Clear();}break;
                default:check(cells.Consume(cell)==old.Consume(prior),"random consume parity");break;
            }
            check(cells.Count==old.Count&&cells.Contains(cell)==old.Contains(prior)&&cells.Any(spawn,element)==old.Any(spawn,element),"random membership/count parity");
            for(long s=1;s<=4;s++)for(int e=1;e<=16;e++)
                check(cells.Cells(s,e).Select(c=>(c.Spawn,c.Element,c.Surface,c.X,c.Y)).SequenceEqual(old.Cells(s,e).Select(c=>(c.Spawn,c.Element,c.Surface,c.X,c.Y))),"random exact enumeration order");
        }
        cells.Clear();var c0=new EraCell(10,1,0,0,0);var v0=cells.Version(10,1);cells.Consume(c0);var v1=cells.Version(10,1);cells.Consume(c0);
        check(v0==0&&v1>0&&cells.Version(10,1)==v1,"version changes on committed consume only");
        cells.Consume(new(11,1,0,0,0));check(cells.Version(10,1)==v1,"unrelated vehicle cannot invalidate entity version");
        cells.Remove(c0);cells.Consume(c0);check(cells.Version(10,1)>v1,"remove/reconsume has fresh version, prevents stale visuals");
        // 0.22.51 (user 08-11, "Ja, haal weg"): no spent-cell render at all; the ledger version is no longer read by any visual (Fix02251EraPlayStabilityTests).
        check(!Read("Material/ReliktSpentVisuals.cs").Contains("PlacedEra.SpentVersion")&&!Read("Material/ReliktSpentVisuals.cs").Contains("cells.OrderBy"),"0.22.51: no visual reads the spent ledger");

        var cat=ArmourResponses.Parse(Read("Material/examples/sprocket.expandedballistics.responses.json"));
        var exp=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(Read("Material/defaults/sprocket.expandedballistics.experimental.json"));
        var transcript=new StringBuilder("family\tthreat\tstep\tspawn\tentity\tcell\tintact\tretention_bits\tcomposite_bits\tspent_order\n");
        foreach(var family in new[]{"duplet","relikt","tes"})foreach(var heat in new[]{false,true})
        {
            cells=new();old=new();var owned=new PlacedEraState();var priorOwned=new Old.PlacedEraState();
            string responseId=family=="tes"?CompositeModule.Current.Era.ResponseId:family=="duplet"?"duplet2MActive":"reliktActive";
            var response=cat.ForResponse(responseId)??throw new Exception("Missing golden response "+responseId);
            var threat=heat?"chemicalJet":"longRod";int step=0;
            var table=ProtectionFactorTable.BuildModule(CompositeModuleEraGeometry.Effective(CompositeModule.Current));
            void Hit(long spawn,int entity,int x,int y,int stage=0,ArmourShotState? state=null,Old.ArmourShotState? priorState=null)
            {
                state??=new();priorState??=new();var cell=new EraCell(spawn,entity,0,x,y);var prior=new Old.EraCell(spawn,entity,0,x,y);
                var a=ArmourResponseModel.ConsumeStacked(cells,state,cell,stage,99,stage>0,out var rear);
                var b=Old.ArmourResponseModel.ConsumeStacked(old,priorState,prior,stage,99,stage>0,out var oldRear);
                owned.Track(cell);priorOwned.Track(prior);
                var weight=ArmourResponseModel.EraAngularWeight(response,threat,60);
                var oldWeight=Old.ArmourResponseModel.EraAngularWeight(response,threat,60);
                double? loss=heat?null:ExperimentalRodModel.Loss(exp.Recipes[responseId],new(.6,.02,3.3,.12),weight);
                double? oldLoss=heat?null:ExperimentalRodModel.Loss(exp.Recipes[responseId],new(.6,.02,3.3,.12),oldWeight);
                double thickness=family=="tes"?CompositeModule.Current.Era.ActiveThicknessMm:family=="duplet"?12:13;
                var value=ArmourResponseModel.Apply(cat,response,state,new ArmourHitKey(spawn,entity,0,0,.327),heat?"heat":"apfsds",true,true,thickness,60,double.NaN,a,loss,exp.EffectiveKineticCap(cat.MaximumAdditionalKineticLoss),"unknown",threat);
                var before=Old.ArmourResponseModel.Apply(cat,response,priorState,new Old.ArmourHitKey(spawn,entity,0,0,.327),heat?"heat":"apfsds",true,true,thickness,60,double.NaN,b,oldLoss,exp.EffectiveKineticCap(cat.MaximumAdditionalKineticLoss),"unknown",threat);
                check(a==b&&rear==oldRear&&BitConverter.DoubleToInt64Bits(value)==BitConverter.DoubleToInt64Bits(before)&&BitConverter.DoubleToInt64Bits(state.AdditionalRetention)==BitConverter.DoubleToInt64Bits(priorState.AdditionalRetention),"golden hit: identical consumption/reaction order/retention bits");
                var order=string.Join(",",cells.Cells(spawn,entity).Select(c=>$"{c.Surface}/{c.X}/{c.Y}"));
                check(order==string.Join(",",old.Cells(spawn,entity).Select(c=>$"{c.Surface}/{c.X}/{c.Y}")),"golden exact spent cell order");
                var composite=family=="tes"?table.Factor(heat?ProtectionThreat.Jet:ProtectionThreat.LongRod,60):1;
                transcript.AppendLine($"{family}\t{threat}\t{step++}\t{spawn}\t{entity}\t{x}/{y}\t{a}\t{BitConverter.DoubleToInt64Bits(value):X16}\t{BitConverter.DoubleToInt64Bits(composite):X16}\t{order}");
            }
            Hit(1,7,0,0);Hit(2,7,0,0);Hit(1,7,0,0);Hit(1,7,1,0);Hit(3,8,0,0);Hit(4,8,0,0);
            // Existing managed load/reset contract: editor reset rearms placed cells.
            owned.Reset(cells);priorOwned.Reset(old);check(cells.Count==old.Count&&cells.Count==0,"save/load editor reset rearms exactly like .38");Hit(1,7,0,0);
            cells.RemoveSpawn(1);old.RemoveSpawn(1);owned.RemoveSpawn(1);priorOwned.RemoveSpawn(1);Hit(5,7,0,0);Hit(2,7,0,0);
            cells.RemoveElement(2,7);old.RemoveElement(2,7);owned.RemoveElement(2,7);priorOwned.RemoveElement(2,7);Hit(2,7,0,0);
            var shot=new ArmourShotState();var priorShot=new Old.ArmourShotState();Hit(6,12,1,1,2,shot,priorShot);Hit(6,13,1,1,1,shot,priorShot);
        }
        File.WriteAllText(Path.Combine(output,"GOLDEN-02239.tsv"),transcript.ToString());

        var depth=new PlacedDepthCache<(int Model,int Mesh,long Spawn)>();
        for(int i=0;i<800;i++)depth[(i,i+1000,i/200+1)]=i*.125;
        for(int pass=0;pass<3;pass++)for(int i=0;i<800;i++)check(depth.TryGetValue((i,i+1000,i/200+1),out var value)&&value==i*.125,"800-cell warm cache no capacity clear");
        check(depth.Count==800,"800 unique model/mesh keys retained");
        check(!depth.TryGetValue((0,9999,1),out _),"transform-generated mesh replacement invalidates exact key (existing policy)");
        depth.Clear();check(depth.Count==0,"existing release/editor-reset clear still clears cache");
        var runtime=Read("RuntimeArmourResponses.cs");
        for(int i=0;i<=PlacedDepthCache<int>.Capacity;i++)depth[(i,i,1)]=i;
        check(depth.Count==1,"capacity remains bounded with prior full-clear policy at 1024");
        Benchmark(output,check);

        var xml=XDocument.Parse(Read("Material/files/Sprocket_Data/StreamingAssets/Localization/en-UK/Parts/compositeSideModule.xml"));
        check(xml.Root!.Element("name")!.Value==CompositeModule.DisplayName&&SprocketExpandedBallistics.EraDisplayName.Component(CompositeModule.ComponentId)==CompositeModule.DisplayName,"XML and native part-list name exactly match TES display name");
        var description=xml.Root.Element("description")!.Value;
        check(description.Contains("Theatre Entry Standard")&&description.Contains("ASPRO-HMT")&&description.Contains("passive composite core")&&description.Contains("steel backplate")&&description.Contains("own estimates, not factory data")&&!description.Contains("Passive"),"description states TES construction and estimate provenance without Passive title");
        check(Read("tools/banner_composite_icon.ps1").Contains("'CR2 TES'")&&Read("tools/banner_composite_icon.ps1").Contains("'SIDE MODULE'")&&!Read("tools/banner_composite_icon.ps1").Contains("PASSIVE"),"banner CR2 TES SIDE MODULE");
        Console.WriteLine($"0.22.39 PASS: {count} checks.");
    }
    private static void Benchmark(string output,Action<bool,string> check)
    {
        var rows=new StringBuilder("cells\toperation\titerations\told_median_ns\tindex_median_ns\tspeedup\n");
        foreach(int size in new[]{200,800})
        {
            var old=new Old.EraCells();var current=new EraCells();
            for(int i=0;i<size;i++){old.Consume(new(i/200+1,i,0,0,0));current.Consume(new(i/200+1,i,0,0,0));}
            foreach(bool list in new[]{false,true})
            {
                const int iterations=20000;var before=new List<double>();var after=new List<double>();long sum=0;
                void Work(bool index){for(int j=0;j<iterations;j++){int e=(j*193)%size;long s=e/200+1;sum+=list?(index?current.Cells(s,e).Length:old.Cells(s,e).Length):(index?current.Any(s,e)?1:0:old.Any(s,e)?1:0);}}
                Work(false);Work(true);
                for(int round=0;round<7;round++)foreach(bool index in round%2==0?new[]{false,true}:new[]{true,false})
                {var watch=Stopwatch.StartNew();Work(index);watch.Stop();(index?after:before).Add(watch.Elapsed.TotalNanoseconds/iterations);}
                before.Sort();after.Sort();check(sum==iterations*16,"benchmark equal query results");
                rows.AppendLine(FormattableString.Invariant($"{size}\t{(list?"Cells":"Any")}\t{iterations}\t{before[3]:F2}\t{after[3]:F2}\t{before[3]/after[3]:F2}"));
            }
        }
        File.WriteAllText(Path.Combine(output,"BENCHMARK-02239.tsv"),rows.ToString());
    }
}
