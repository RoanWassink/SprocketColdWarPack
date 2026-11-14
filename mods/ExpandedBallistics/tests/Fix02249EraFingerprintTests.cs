using System.Text;
using System.Text.Json;
using SprocketExpandedBallistics.Era;
using Old=SprocketExpandedBallistics.Golden02246;
namespace SprocketExpandedBallistics;

internal static class Fix02249EraFingerprintTests
{
    internal static void Run(Action<bool,string> check)
    {
        int checks=0;var sink=check;check=(ok,msg)=>{checks++;sink(ok,"fix02249: "+msg);};
        var root=Fix02228Tests.SourceRoot();var parent=Fix02228Tests.SessionRoot();var output=Fix02228Tests.OutputRoot();
        var catalogue=ArmourResponses.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults/sprocket.expandedballistics.responses.json")));
        var response=catalogue.ForMaterial("reliktActive")!;
        var experimental=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults/sprocket.expandedballistics.experimental.json")));
        var original=new MaterialFingerprint((float)response.PassiveMaterial.Density,(float)response.PassiveMaterial.RhaFactor,(float)response.PassiveMaterial.SpallFactor);
        bool Matches(MaterialFingerprint p)=>ArmourResponses.PassiveMatches(response,p.Density,p.RhaFactor,p.SpallFactor);
        var array=(IntPtr)101;var extra=(IntPtr)102;var writes=new[]{new MaterialFingerprintWrite(array,8,original,extra,original)};
        var cases=new StringBuilder("case\tentity\tangle_deg\tdepth_mm\twritten_rha\toriginal_rha\tinitiated\tevidence\n");
        foreach(float factor in new[]{.139f,.15f-(float)(.001/13*.15),0f,1f})
        {
            var live=original with{RhaFactor=factor};
            check(!Matches(live),"changed live resistance fails strict recipe "+factor);
            var restored=MaterialFingerprint.BeforeWrites(array,8,live,writes);
            check(restored==original&&Matches(restored),"pre-write fingerprint passes without widening tolerance");
            var cells=new EraCells();var cell=new EraCell(1,907,0,0,0);var shot=new ArmourShotState();
            check(Matches(restored)&&ArmourResponseModel.ConsumeStacked(cells,shot,cell,0,0,false,out _),"fresh cell initiates after PerInterval write");
            check(!ArmourResponseModel.ConsumeStacked(cells,new ArmourShotState(),cell,0,0,false,out _),"second shot same spawn cannot consume again");
            check(cells.Consume(cell with{Spawn=2}),"fresh spawn gets a fresh element");
            check(MaterialFingerprint.BeforeWrites(extra,8,live,writes)==original,"separate native argument array fingerprint");
            check(MaterialFingerprint.BeforeWrites((IntPtr)103,8,live,writes)==live,"unrelated array is not substituted");
            check(MaterialFingerprint.BeforeWrites(array,9,live,writes)==live,"unrelated material index is not substituted");
            var nested=writes.Append(new MaterialFingerprintWrite(array,8,live,IntPtr.Zero,default));
            check(MaterialFingerprint.BeforeWrites(array,8,live,nested)==original,"oldest shared-array write wins across nested simulations");
        }
        foreach(var wrong in new[]{original with{Density=7850},original with{RhaFactor=1},original with{SpallFactor=1},original with{RhaFactor=float.NaN}})
        {
            var saved=new[]{new MaterialFingerprintWrite(array,8,wrong,IntPtr.Zero,default)};
            check(!Matches(MaterialFingerprint.BeforeWrites(array,8,original,saved)),"genuine pre-write mismatch remains rejected");
        }
        check(MaterialFingerprint.BeforeWrites(array,8,original,Array.Empty<MaterialFingerprintWrite>())==original,"after restore history is empty and live original is retained");
        CombatReportShot[] Load(string fixture)
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(parent,fixture,"last-session.json")));
            return JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        }
        void Regress(string fixture,int number,int entity)
        {
            var shot=Load(fixture).Single(s=>s.Number==number);
            var part=shot.Events.Where(e=>e.Kind=="layerStart").SelectMany(e=>e.Layer?.Parts??Array.Empty<CombatLayerPart>()).First(p=>p.Entity==entity&&p.Armour&&p.AngleDegrees!=null&&p.ThicknessMm!=null);
            var r=catalogue.ForMaterial(part.MaterialId!)!;
            check(r!=null&&part.ThicknessMm>=r.Geometry.MinNormalThicknessMm&&part.ThicknessMm<=r.Geometry.MaxNormalThicknessMm,"recorded active depth satisfies unchanged geometry "+entity);
            var fp=MaterialFingerprint.BeforeWrites(array,8,original with{RhaFactor=.139f},writes);
            var cells=new EraCells();bool initiated=ArmourResponses.PassiveMatches(r!,fp.Density,fp.RhaFactor,fp.SpallFactor)&&cells.Consume(new(1,entity,0,0,0));
            check(initiated,"historical confirmed active entry initiates "+fixture+" #"+number+" / "+entity);
            cases.AppendLine(FormattableString.Invariant($"{fixture} #{number}\t{entity}\t{part.AngleDegrees:R}\t{part.ThicknessMm:R}\t0.139\t{fp.RhaFactor:R}\t{initiated}\trecorded active entry; managed recipe and consumption replay"));
        }
        Regress("era-1108c",1,907);Regress("era-1108c",1,972);Regress("t80u-1108b",4,318);
        var log=File.ReadAllLines(Path.Combine(root,"tests","fixtures","LOG-EVIDENCE.txt"));
        check(log.Any(l=>l.Contains("id=984 ")&&l.Contains("reject=passive-mismatch")&&l.Contains("rha=0,139")),"archived log case 984 proves changed resistance");
        check(log.Any(l=>l.Contains("id=984 ")&&l.Contains("physicalDepth=13,000")),"log case 984 confirms reached 13 mm cell");
        check(Matches(MaterialFingerprint.BeforeWrites(array,8,original with{RhaFactor=.139f},writes))&&new EraCells().Consume(new(1,984,0,0,0)),"984 initiates after original fingerprint lookup");
        cases.AppendLine("log #984\t984\t54.30\t13\t0.139\t0.15\tTrue\tarchived log geometry; managed recipe and consumption replay");

        // Replay the recorded reached active entries against separately compiled 0.22.46
        // cell accounting and response model. Each shot uses its recorded intact/spent
        // precondition; saved reports do not retain spawn or partition-cell identities.
        var currentCells=new EraCells();var oldCells=new Old.EraCells();int comparisons=0;
        var historical=new StringBuilder("shot\tentity\tinitiated_02246\tinitiated_02249\tretention_bits_02246\tretention_bits_02249\n");
        foreach(var shot in Load("t80u-1108").Where(s=>new[]{1,3,6,7,8,9,10}.Contains(s.Number)))
        {
            currentCells.Clear();oldCells.Clear();
            var state=new ArmourShotState();var oldState=new Old.ArmourShotState();
            var reachedEntities=shot.Number switch{1=>new[]{272},3 or 9=>new[]{906,907},6=>new[]{273,272},7 or 8=>new[]{908,909},10=>new[]{908,907},_=>Array.Empty<int>()};
            check(shot.Number is 8 or 10?shot.Events.Count(e=>e.Kind=="eraSpent")==2:shot.Events.Where(e=>e.Kind is "heavyEra" or "lightEra").Select(e=>e.RelatedEntity!.Value).OrderBy(i=>i).SequenceEqual(reachedEntities.OrderBy(i=>i)),"recorded historical initiation/spent evidence #"+shot.Number);
            foreach(var part in shot.Events.Where(e=>e.Kind=="layerStart").SelectMany(e=>e.Layer?.Parts??Array.Empty<CombatLayerPart>()).Where(p=>p.Armour&&reachedEntities.Contains(p.Entity)&&p.MaterialId is {} id&&catalogue.ForMaterial(id)?.Kind is "heavyEra" or "lightEra").DistinctBy(p=>p.Entity))
            {
                var r=catalogue.ForMaterial(part.MaterialId!)!;var cell=new EraCell(1,part.Entity,0,0,0);
                var expected=shot.Number is not (8 or 10);
                if(!expected){currentCells.Consume(cell);oldCells.Consume(new(1,part.Entity,0,0,0));}
                bool now=currentCells.Consume(cell),before=oldCells.Consume(new(1,part.Entity,0,0,0));
                check(now==before&&now==expected,"historical initiation outcome 0.22.46 parity #"+shot.Number+" / "+part.Entity);
                var key=new ArmourHitKey(1,part.Entity,0,0,1);var oldKey=new Old.ArmourHitKey(1,part.Entity,0,0,1);
                // Reference rod geometry is explicit: reports omit the original rod dimensions.
                var loss=ExperimentalRodModel.Loss(experimental.Recipes[r.ResponseId],new(.6,.02,3.3,.12),ArmourResponseModel.EraAngularWeight(r,"longRod",part.AngleDegrees!.Value));
                var cap=experimental.EffectiveKineticCap(catalogue.MaximumAdditionalKineticLoss);
                double a=ArmourResponseModel.Apply(catalogue,r,state,key,"apfsds",true,true,13,part.AngleDegrees.Value,double.NaN,now,loss,cap,threatClass:"longRod",threat:"longRod");
                double b=Old.ArmourResponseModel.Apply(catalogue,r,oldState,oldKey,"apfsds",true,true,13,part.AngleDegrees.Value,double.NaN,before,loss,cap,threatClass:"longRod",threat:"longRod");
                check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),"historical response retention bits 0.22.46 parity");comparisons++;
                check(expected?a<1:a==1,"intact reference rod response reduces penetration; spent element has no reaction");
                historical.AppendLine($"{shot.Number}\t{part.Entity}\t{before}\t{now}\t{BitConverter.DoubleToInt64Bits(b):X16}\t{BitConverter.DoubleToInt64Bits(a):X16}");
            }
        }
        check(comparisons==13,"all requested historical active entries covered");

        var binding=EraPartBindings.Parse("{\"schemaVersion\":1,\"matchMode\":\"componentId\",\"bindings\":[{\"cassetteComponentId\":\"active\",\"mountComponentId\":\"housing\",\"materialId\":\"reliktActive\",\"responseId\":\"reliktActive\",\"kind\":\"heavyEra\"}]}");
        var housing=new EraRejectionReport.PathPart(10,"housing",false,new[]{new MaterialBoundary(1,true,false,0),new(1.1,false,true,0)});
        check(EraRejectionReport.HousingBetweenElements(binding,new[]{housing},10,1,1.1),"housing-only confirmed own chord produces reason");
        check(!EraRejectionReport.HousingBetweenElements(binding,new[]{housing with{Boundaries=new[]{new MaterialBoundary(1,true,false,0)}}},10,1,1.1),"membership/unpaired entry cannot claim housing-only path");
        check(!EraRejectionReport.HousingBetweenElements(binding,new[]{housing,new(20,"active",true,new[]{new MaterialBoundary(1.05,true,false,0)})},10,1,1.1),"active boundary suppresses housing-only claim even without exit");
        check(!EraRejectionReport.HousingBetweenElements(binding,new[]{housing,new(30,null,false,Array.Empty<MaterialBoundary>())},10,1,1.1),"unresolved component suppresses housing claim");
        check(!EraRejectionReport.HousingBetweenElements(binding,new[]{housing},10,2,3),"housing outside reached span adds no reason");
        var layer=new CombatLayerInfo("hit",new[]{new CombatLayerPart(907,"Relikt","reliktActive","Explosive reactive armour",13,45,true)},false);
        var start=new CombatReportEvent("layerStart","","Reached",600,HitId:"hit",Layer:layer);
        var payment=new CombatReportEvent("materialReaction","","diagnostic",600,590,"hit"){PaidIntervals=new[]{new PaidMaterialPiece(907,"reliktActive","Relikt",0,.013,.15,45)}};
        foreach(var reason in new[]{EraRejectionReport.Spent,EraRejectionReport.Material,EraRejectionReport.Housing,EraRejectionReport.Reason("layout")})
        foreach(bool before in new[]{true,false})
        {
            var rejection=new CombatReportEvent(reason==EraRejectionReport.Spent?"eraSpent":"eraUnavailable","",reason,HitId:"hit",RelatedEntity:907);
            var shot=new CombatReportShot(1,"Incoming","APFSDS",1000,before?new[]{start,rejection,payment,rejection}:new[]{start,payment,rejection,rejection});
            var reactions=CombatHitProjection.Build(shot).Select(h=>h.Reaction).ToArray();
            check(reactions.Count(t=>t.Contains(reason,StringComparison.Ordinal))==1&&reactions.Sum(t=>t.Split(reason).Length-1)==1,"one scientific reason survives payment order and duplicate events");
            check(CombatReportView.Build(new[]{shot},1).Single().Layers.Any(l=>l.Reaction.Contains(reason,StringComparison.Ordinal)),"final user-facing Reaction preserves exact scientific reason");
        }
        var housingLayer=layer with{Parts=new[]{new CombatLayerPart(10,"Module housing",null,null,null,null,false)}};
        var housingShot=new CombatReportShot(1,"Incoming","APFSDS",1000,new[]{start with{Layer=housingLayer},new CombatReportEvent("eraUnavailable","",EraRejectionReport.Housing,HitId:"hit",RelatedEntity:10)});
        check(CombatHitProjection.Build(housingShot).Any(h=>h.Layer?.Parts.Any(p=>p.Entity==10)==true&&h.Reaction==EraRejectionReport.Housing),"non-armour housing reason remains on its own visible report row");
        var fallback=payment with{PaidIntervals=null,Text=ProtectionLine.Text("Relikt",13,.15,ProtectionThreat.LongRod,45)};
        var fallbackShot=new CombatReportShot(1,"Incoming","APFSDS",1000,new[]{start,new CombatReportEvent("eraSpent","",EraRejectionReport.Spent,HitId:"hit",RelatedEntity:907),fallback});
        check(CombatReportView.Build(new[]{fallbackShot},1).Single().Layers.Any(l=>l.Reaction.Contains(EraRejectionReport.Spent,StringComparison.Ordinal)),"legacy protection-line formatting cannot erase spent reason");
        // Exact native integration and unchanged protection contract are verified with the compiled baseline runner too.
        var native=File.ReadAllText(Path.Combine(root,"NativeMaterialReactions.cs"));var runtime=File.ReadAllText(Path.Combine(root,"RuntimeArmourResponses.cs"));
        check(native.IndexOf("fingerprint,module,interval,ray",StringComparison.Ordinal)<native.IndexOf("SetRha(materials",StringComparison.Ordinal),"snapshot registered before first mutation");
        check(runtime.Contains("NativeMaterialReactions.OriginalMaterial(sim,passiveIndex)")&&!runtime.Contains("ArmourResponses.PassiveMatches(response,passive.density"),"runtime recipe reads original fingerprint");
        // 0.22.51 (part D.3): every recipe check, not only ERA, compares the pre-write material (Fix02251ChainReviewTests).
        check(runtime.Contains("var passive=NativeMaterialReactions.OriginalMaterial(sim,passiveIndex);"),"0.22.51: every recipe check reads the pre-write material");
        File.WriteAllText(Path.Combine(output,"ERA-FINGERPRINT-CASES.tsv"),cases.ToString());File.WriteAllText(Path.Combine(output,"ERA-02246-PARITY.tsv"),historical.ToString());
        File.WriteAllText(Path.Combine(output,"FIX49-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks,historicalActiveEntries=comparisons,regressionCases=4,liveValidation=false,limits="Managed recipe/geometry/cell replay; historical native rays and full runtime replay are unavailable"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FIX02249 PASS: {checks} checks; 4 regression cases; {comparisons} historical active entries.");
    }
}

