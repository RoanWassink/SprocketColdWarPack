using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class ThreatClassTests
{
 internal static void Run(Action<bool,string> check)
 {
  var baseline=ReleaseProfiles.Defaults();
  var root=JsonNode.Parse(baseline)!.AsObject();
  var heat=ShellProfiles.Parse(baseline).First(p=>p.Behavior=="heat");
  var row=root["profiles"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()==heat.Id)!.AsObject();
  check(heat.ThreatClass=="unknown","old HEAT defaults unknown, no single-charge inference");
  row["threatClass"]="unitaryAntitankGrenade";
  var classified=ShellProfiles.Parse(root.ToJsonString()).Single(p=>p.Id==heat.Id);
  check(classified.ThreatClass=="unitaryAntitankGrenade","explicit class accepted");
  var draft=new ModularShellDraft(root.ToJsonString());
  check(ShellProfiles.Parse(draft.Compile().RuntimeJson!).Single(p=>p.Id==heat.Id).ThreatClass==classified.ThreatClass,"modular roundtrip preserves class");
  row["threatClass"]="unknown";
  check(classified.ThreatClass=="unitaryAntitankGrenade","parsed launch profile snapshot immutable across JSON edit");
  foreach(var bad in new JsonNode?[]{JsonValue.Create("tandem"),JsonValue.Create("UNITARY"),JsonValue.Create(1),null})
  {
   row["threatClass"]=bad;var fail=false;try{ShellProfiles.Parse(root.ToJsonString());}catch(FormatException){fail=true;}
   check(fail,"invalid/unknown/wrongtype threat class rejected");
  }
  row["threatClass"]="unitaryAntitankGrenade";
  foreach(var mode in new[]{"ap","apfsds","aphe","hesh","he","atgm","atgm_gun"})
  {
   row["behavior"]=mode;var fail=false;try{ShellProfiles.Parse(root.ToJsonString());}catch(FormatException){fail=true;}
   check(fail,"non-grenade behavior cannot declare grenade class: "+mode);
  }
  var fixture=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"armour-response-fixture.json"));
  var catalogue=ArmourResponses.Parse(fixture);
  var old=catalogue.Responses.First(r=>r.Kind=="lightEra");
  check(old.EligibleThreatClasses==null&&ArmourResponseModel.ReactiveThreat(old,"heat"),"old response preserved");
  var response=old with{ResponseId="nizhLmInsert",EligibleThreatClasses=Array.AsReadOnly(new[]{"unitaryAntitankGrenade"})};
  foreach(var denied in new[]{"unknown","cannonHeat","atgm","tandem", ""})
  {
   var shot=new ArmourShotState();
   check(!ArmourResponseModel.ReactiveThreat(response,"heat",denied),"LM denied before cell gate: "+denied);
   check(ArmourResponseModel.Apply(catalogue with{Enabled=true},response,shot,"cell","heat",true,true,old.Geometry.MinNormalThicknessMm,0,0,true,threatClass:denied)==1,"LM denied response has no loss");
   check(shot.AdditionalRetention==1&&shot.Applied.Count==0,"denied class leaves budget/applied state untouched");
  }
  check(ArmourResponseModel.ReactiveThreat(response,"heat","unitaryAntitankGrenade"),"explicit grenade admitted");
  foreach(var mode in new[]{"apfsds","hesh","ap","aphe","he"})check(!ArmourResponseModel.ReactiveThreat(response,mode,"unitaryAntitankGrenade"),"nonheat remains passive");
  var r=JsonNode.Parse(fixture)!;
  var target=r["responses"]!.AsArray().Single(x=>x!["responseId"]!.GetValue<string>()==old.ResponseId)!;
  target["responseId"]="nizhLmInsert";
  void Invalid(JsonNode? classes,string label)
  {
   target["eligibleThreatClasses"]=classes;var fail=false;try{ArmourResponses.Parse(r.ToJsonString());}catch(FormatException){fail=true;}check(fail,label);
  }
  Invalid(new JsonArray(),"empty eligibility rejected");
  Invalid(new JsonArray("unknown"),"unknown eligibility rejected");
  Invalid(new JsonArray("unitaryAntitankGrenade","unitaryAntitankGrenade"),"duplicate eligibility rejected");
  Invalid(JsonValue.Create("unitaryAntitankGrenade"),"string eligibility rejected");
  target.AsObject().Remove("eligibleThreatClasses");var missing=false;try{ArmourResponses.Parse(r.ToJsonString());}catch(FormatException){missing=true;}check(missing,"LM missing filter fail closed");
  target["eligibleThreatClasses"]=new JsonArray("unitaryAntitankGrenade");
  var parsed=ArmourResponses.Parse(r.ToJsonString()).Responses.Single(x=>x.ResponseId=="nizhLmInsert");
  check(parsed.EligibleThreatClasses!.Single()=="unitaryAntitankGrenade","paired optional response field parsed");
  target["calibration"]!["heatRetention"]=.2;
  r["enabled"]=true;
  foreach(var cap in new[]{0d,.1,.3,.6})
  {
   r["maximumAdditionalHeatLoss"]=cap;
   var limited=ArmourResponses.Parse(r.ToJsonString());
   var lm=limited.Responses.Single(x=>x.ResponseId=="nizhLmInsert");
   var state=new ArmourShotState();
   var angle=lm.Geometry.AngularCurve.OrderByDescending(x=>x.Weight).First().Input;
   var factor=ArmourResponseModel.Apply(limited,lm,state,"physical-cell","heat",true,true,lm.Geometry.MinNormalThicknessMm,angle,0,true,threatClass:"unitaryAntitankGrenade");
   check(Math.Abs(factor-Math.Max(1-cap,1-.8*ArmourResponseModel.AngularWeight(lm,"heat",angle)))<1e-12,"requested80 bounded by actual user CE cap "+cap);
   check(ArmourResponseModel.Apply(limited,lm,state,"physical-cell","heat",true,true,lm.Geometry.MinNormalThicknessMm,angle,0,true,threatClass:"unitaryAntitankGrenade")==1,"same exact cell applied only once");
  }
  r["maximumAdditionalHeatLoss"]=.6;
  foreach(var badRetention in new[]{-.01,1.01})
  {
   target["calibration"]!["heatRetention"]=badRetention;var fail=false;
   try{ArmourResponses.Parse(r.ToJsonString());}catch(FormatException){fail=true;}
   check(fail,"CE coefficient outside0..1 rejected");
  }
  target["calibration"]!["heatRetention"]=.2;
  var blocked=new EraCells();var cell=new EraCell(1,1,1,0,0);
  if(ArmourResponseModel.ReactiveThreat(response,"heat","unknown"))blocked.Consume(cell);
  check(blocked.Count==0,"unknown class cannot consume cell through eligibility gate");
  if(ArmourResponseModel.ReactiveThreat(response,"heat","unitaryAntitankGrenade"))blocked.Consume(cell);
  check(blocked.Count==1,"explicit admitted class can consume exactly once");
  target["eligibleThreatClasses"]![0]="unknown";check(parsed.EligibleThreatClasses!.Single()=="unitaryAntitankGrenade","response class snapshot immutable");
 }
}
