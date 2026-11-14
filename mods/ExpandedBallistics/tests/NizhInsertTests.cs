using SprocketExpandedBallistics.ExperimentalArmour;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class NizhInsertTests
{
 internal static void Run(Action<bool,string> check)
 {
  var original=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"dynamic-era-template.json"));
  var old=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(original);
  var json=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"nizh-template.json"));
  var cfg=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(json);
  check(cfg.ParsedModelVersion=="2.0.3"&&cfg.Enabled&&cfg.Recipes.Count==5,"new five-recipe template standard");
  check(old.ParsedModelVersion=="2.0.2"&&old.Recipes.Count==3,"old three-recipe model accepted unchanged");
  foreach(var pair in old.Recipes)
  {
   var after=cfg.Recipes[pair.Key];
   check(pair.Value.BaseLoss==after.BaseLoss&&pair.Value.MaximumLoss==after.MaximumLoss&&pair.Value.ReferenceLD==after.ReferenceLD&&pair.Value.MinimumSusceptibility==after.MinimumSusceptibility&&pair.Value.MaximumSusceptibility==after.MaximumSusceptibility,"existing recipe values preserved");
  }
  void Invalid(Action<JsonObject> mutation,string message)
  {
   var root=JsonNode.Parse(json)!.AsObject();mutation(root);var rejected=false;
   try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(root.ToJsonString());}catch(FormatException){rejected=true;}
   check(rejected,message);
  }
  Invalid(r=>r["modelVersion"]="2.0.2","old model rejects five recipes");
  Invalid(r=>{r["modelVersion"]="2.0.2";var rows=r["recipes"]!.AsArray();while(rows.Count>1)rows.RemoveAt(0);},"old model rejects new ID even within old count");
  Invalid(r=>r["modelVersion"]="2.0.7","unknown future model rejected");
  Invalid(r=>r["recipes"]![4]!["responseId"]="experimentalNizhCassette","duplicate ID rejected");
  Invalid(r=>r["recipes"]![4]!["responseId"]="nizh19","unregistered alias rejected");
  Invalid(r=>r["recipes"]!.AsArray().Add(r["recipes"]![4]!.DeepClone()),"six recipes rejected");
  Invalid(r=>r["reactiveLayers"]=2,"unimplemented layer key rejected");
  Invalid(r=>r["recipes"]![4]!["kind"]="tandem","tandem unsupported");
  Invalid(r=>r["recipes"]![4]!["baseLoss"]=.25,"base exceeds maximum rejected");
  Invalid(r=>r["recipes"]![4]!["referenceLD"]=.9,"low referenceLD rejected");
  Invalid(r=>r["recipes"]![4]!["maximumSusceptibility"]=4.1,"high susceptibility rejected");
  Invalid(r=>r.Remove("enabled"),"missing field rejected");
  Invalid(r=>r["enabled"]="true","wrong field type rejected");
  var duplicate=json.Replace("\"enabled\": false","\"enabled\": false, \"enabled\": true");
  var duplicatesRejected=false;try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(duplicate);}catch(FormatException){duplicatesRejected=true;}
  check(duplicatesRejected,"duplicate root keys rejected");
  var lower=JsonNode.Parse(json)!;lower["maximumAdditionalKineticLoss"]=.05;
  check(SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(lower.ToJsonString()).Recipes.Count==5,"local recipe maxima independent of lower global cap");
  using var stream=typeof(ArmourResponses).Assembly.GetManifestResourceStream("EraPreset.kontakt5Cassette-response.json")!;
  var preset=JsonNode.Parse(new StreamReader(stream).ReadToEnd())!;
  var legacyRoot=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"armour-response-fixture.json")))!;
  legacyRoot["responses"]!.AsArray().Add(preset);legacyRoot["enabled"]=true;
  var legacy=ArmourResponses.Parse(legacyRoot.ToJsonString());var oldResponse=legacy.Responses.Single(r=>r.ResponseId=="kontakt5Cassette");
  foreach(var variant in new[]{("experimentalNizh34Insert",36d,.2,.35,2.8,2488.888888889), ("experimentalNizh19Insert",26d,.15,.24,2.1,2584.615384615)})
  {
   var recipe=cfg.Recipes[variant.Item1];
   check(ExperimentalRodModel.Supported(variant.Item1),"new identity routes geometry model");
   var response=oldResponse with{ResponseId=variant.Item1,CompatibleMaterialIds=new[]{variant.Item1},
    Calibration=oldResponse.Calibration with{IntactRodRetention=1,DisturbedRodRetention=1,DisturbanceContribution=0,HeatRetention=variant.Item2==36?.45:.55},
    Geometry=oldResponse.Geometry with{MinNormalThicknessMm=variant.Item2-.5,MaxNormalThicknessMm=variant.Item2+.5},
    PassiveMaterial=oldResponse.PassiveMaterial with{Density=variant.Item6,RhaFactor=.15,SpallFactor=.25}};
   foreach(var ld in new[]{20d,30d,40d,80d,1000d})
   {
    var geometry=new ImpactGeometry(ld*.03,.03,5,.12);
    var loss=ExperimentalRodModel.Loss(recipe,geometry,1);
    var expected=Math.Min(variant.Item4,variant.Item3*Math.Clamp(Math.Sqrt(ld/30),.7,1.6));
    check(Math.Abs(loss-expected)<1e-12,"Nizh calibrated LD endpoint");
    var track=new ArmourShotState();var factor=ArmourResponseModel.Apply(legacy,response,track,"insert","apfsds",true,true,variant.Item2,30,0,true,loss,.35);
    check(Math.Abs(factor-(1-expected))<1e-12,"active thin slab applies one expected reduction");
    check(ArmourResponseModel.Apply(legacy,response,track,"insert","apfsds",true,true,variant.Item2,30,0,true,loss,.35)==1,"same slab duplicate does not charge again");
   }
   foreach(var depth in new[]{variant.Item2-1,variant.Item2+1,60d,70d})
    check(ArmourResponseModel.Apply(legacy,response,new(),"wrong-depth","apfsds",true,true,depth,30,0,true,.2,.35)==1,"outer housing/old depth not active insert");
   check(Math.Abs(variant.Item6*.25*.125*variant.Item2*.001-variant.Item5)<1e-9,"insert mass from actual volume and density");
   check(ArmourResponses.PassiveMatches(response,(float)variant.Item6,.15f,.25f),"exact native passive fingerprint accepted");
   check(!ArmourResponses.PassiveMatches(response,7850,.15f,.25f),"housing steel not active fingerprint");
   check(Math.Abs(ArmourResponseModel.Apply(legacy,response,new(),"heat","heat",true,true,variant.Item2,30,0,true)-response.Calibration.HeatRetention)<1e-12,"base HEAT retained independent of experimental switch");
   check(ArmourResponseModel.Apply(legacy,response,new(),"off","apfsds",true,true,variant.Item2,30,0,true)==1,"missing experimental overlay yields neutral KE");
  }
  check(ArmourResponseModel.Apply(legacy,oldResponse,new(),"old70","apfsds",true,true,70,30,0,true)==.9,"old70mm response unchanged");
  check(old.ParsedModelVersion=="2.0.2"&&cfg.Revision!=old.Revision,"immutable old snapshot survives new catalogue");
 }
}
