using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class TandemTests
{
 internal static void Run(Action<bool,string> check)
 {
  var baseline=JsonNode.Parse(ReleaseProfiles.Defaults())!;var row=baseline["profiles"]!.AsArray().First(x=>x!["behavior"]?.GetValue<string>()=="heat")!;
  var id=row["id"]!.GetValue<string>();
  check(ShellProfiles.Parse(baseline.ToJsonString()).Single(x=>x.Id==id).TandemPrecursorPenetrationMm==0,"legacy single-budget behavior unchanged");
  row["tandemPrecursorPenetrationMm"]=80;
  var frozen=ShellProfiles.Parse(baseline.ToJsonString()).Single(x=>x.Id==id);
  check(frozen.TandemPrecursorPenetrationMm==80&&frozen.ThreatClass=="unknown","explicit independent precursor budget parsed");
  check(ShellProfiles.Parse(new ModularShellDraft(baseline.ToJsonString()).Compile().RuntimeJson!).Single(x=>x.Id==id).TandemPrecursorPenetrationMm==80,"prototype module JSON roundtrip");
  row["tandemPrecursorPenetrationMm"]=20;check(frozen.TandemPrecursorPenetrationMm==80,"launchstage snapshot immutable");
  foreach(var bad in new JsonNode?[]{JsonValue.Create(-1),JsonValue.Create(2001),JsonValue.Create("80"),null})
  {
   row["tandemPrecursorPenetrationMm"]=bad;var fail=false;try{ShellProfiles.Parse(baseline.ToJsonString());}catch(FormatException){fail=true;}check(fail,"invalid precursor input rejected");
  }
  row["tandemPrecursorPenetrationMm"]=80;row["threatClass"]="unitaryAntitankGrenade";
  var failed=false;try{ShellProfiles.Parse(baseline.ToJsonString());}catch(FormatException){failed=true;}check(failed,"tandem cannot masquerade as LM unitary grenade");
  row["threatClass"]="unknown";
  foreach(var behavior in new[]{"ap","apfsds","aphe","he","hesh"})
  {
   row["behavior"]=behavior;failed=false;try{ShellProfiles.Parse(baseline.ToJsonString());}catch(FormatException){failed=true;}check(failed,"nonHEAT precursor rejected");
  }
  foreach(var calibre in new[]{75d,100d,135d,200d})check(Math.Abs(TandemStage.Budget(80,calibre,100)-.8*calibre)<1e-12,"precursor scales with referencecalibre independent main");
  check(TandemStage.Budget(80,0,100)==0&&TandemStage.Budget(double.NaN,100,100)==0,"invalid stage inputs fail beforecommit");
  var stage=new TandemStage();check(stage.CanFallback&&!stage.Committed,"before simulation stockfallback allowed");
  check(stage.BeginSimulation()&&stage.Committed&&!stage.CanFallback,"simulation entry commits before ERA callback");
  check(!stage.BeginSimulation(),"no duplicate precursor simulation");
  check(stage.BeginApply()&&!stage.BeginApply(),"ApplyFragmentHits can enter exactlyonce");
  stage.Complete();check(stage.Phase==TandemPhase.Completed&&!stage.CanFallback,"completed precursor cannot fallback");
  stage.Fail();check(stage.Phase==TandemPhase.Failed&&!stage.CanFallback,"late failure stays committed");
  var miss=new TandemStage();miss.BeginSimulation();miss.Complete();check(miss.Phase==TandemPhase.Completed&&!miss.BeginApply(),"native miss completed without damageapplication");
  var before=new TandemStage();before.Fail();check(before.CanFallback&&!before.BeginSimulation(),"precommit preflightfailure can use stockonce but cannot restartprecursor");
  // A preview pair shares depletion, while combat and the next pointer sample remain independent.
  var previewCells=new EraCells();var combatCells=new EraCells();var cell=new EraCell(3,42,1,0,0);
  var precursorPreview=new TandemStage();check(precursorPreview.BeginSimulation(),"preview precursor starts without live apply");
  check(previewCells.Consume(cell),"preview precursor first cell intact");precursorPreview.Complete();
  check(!previewCells.Consume(cell),"paired preview main sees consumed cell");
  check(!combatCells.Contains(cell)&&combatCells.Count==0,"preview leaves combat ledger unchanged");
  check(new EraCells().Consume(cell),"next preview sample gets fresh cell ledger");
  check(previewCells.Consume(cell with{Element=43}),"precursor does not spend different plate cell");
  check(precursorPreview.Phase==TandemPhase.Completed&&!precursorPreview.BeginApply(),"preview never enters fragment application");

 }
}
