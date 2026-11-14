using System.Text.Json.Nodes;
using SprocketExpandedBallistics.ExperimentalArmour;
namespace SprocketExpandedBallistics;
internal static class GuardrailTests
{
 internal static void Run(Action<bool,string> check)
 {
  var oldText=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"physical-era-template.json"));
  var root=JsonNode.Parse(oldText)!.AsObject();root["modelVersion"]="2.0.5";root["maximumAdditionalKineticLoss"]=.95;
  var sample=root["recipes"]![5]!.DeepClone();sample["responseId"]="dynaActive";sample["baseLoss"]=.12;sample["maximumLoss"]=.25;sample["maximumSusceptibility"]=2;
  root["recipes"]!.AsArray().Add(sample);
  var parsed=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(root.ToJsonString());
  check(parsed.Recipes.Count==19&&parsed.Recipes.ContainsKey("dynaActive"),"new model exactDYNA19 identities");
  check(parsed.EffectiveKineticCap(.95)==.95&&parsed.EffectiveKineticCap(.1)==.1,"new cap honors both usercatalogues");
  check(SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(oldText).EffectiveKineticCap(.95)==.35,"historical model pairednewresponse cap stays35");
  foreach(var old in SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(oldText).Recipes){var current=parsed.Recipes[old.Key];check(old.Value.BaseLoss==current.BaseLoss&&old.Value.MaximumLoss==current.MaximumLoss&&old.Value.ReferenceLD==current.ReferenceLD,"old recipe unchanged "+old.Key);}
  void RejectExperimental(Action<JsonObject> edit,string label){var value=(JsonObject)root.DeepClone();edit(value);bool failed=false;try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(value.ToJsonString());}catch(FormatException){failed=true;}check(failed,label);}
  RejectExperimental(x=>x["maximumAdditionalKineticLoss"]=.950001,"new cap above95 rejected");
  RejectExperimental(x=>x["maximumAdditionalKineticLoss"]=-.01,"negative cap rejected");
  RejectExperimental(x=>x["recipes"]![0]!["baseLoss"]=.950001,"base loss above95 rejected");
  RejectExperimental(x=>x["recipes"]![0]!["maximumLoss"]=.950001,"perrecipe maximum above95 rejected");
  RejectExperimental(x=>{x["recipes"]![0]!["baseLoss"]=.8;x["recipes"]![0]!["maximumLoss"]=.7;},"unordered losses rejected");
  RejectExperimental(x=>x["recipes"]![18]!["responseId"]="westernHeavyGuess","unknown heavy ID rejected");
  RejectExperimental(x=>x["recipes"]!.AsArray().Add(sample.DeepClone()),"duplicate orovercapacity rejected");
  RejectExperimental(x=>x["recipes"]![0]!["maximumSusceptibility"]=4.01,"susceptibilityhard4 remains");
  foreach(var model in new[]{"2.0.2","2.0.3","2.0.4"}){
   var oldRoot=JsonNode.Parse(oldText)!.AsObject();oldRoot["modelVersion"]=model;oldRoot["recipes"]=new JsonArray(oldRoot["recipes"]![0]!.DeepClone());
   check(SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(oldRoot.ToJsonString()).Recipes.Count==1,"historical model stillreadable"+model);
   oldRoot["maximumAdditionalKineticLoss"]=.36;bool failed=false;try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(oldRoot.ToJsonString());}catch(FormatException){failed=true;}check(failed,"historical capceiling retained"+model);
   oldRoot["maximumAdditionalKineticLoss"]=.35;oldRoot["recipes"]![0]!["maximumLoss"]=.36;failed=false;try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(oldRoot.ToJsonString());}catch(FormatException){failed=true;}check(failed,"historical recipeceiling retained"+model);
   oldRoot["recipes"]=new JsonArray(sample.DeepClone());failed=false;try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(oldRoot.ToJsonString());}catch(FormatException){failed=true;}check(failed,"oldmodel excludesDYNA"+model);
  }
  var responseText=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"physical-era-responses.json"));
  var response=JsonNode.Parse(responseText)!.AsObject();response["schemaVersion"]=2;response["maximumAdditionalKineticLoss"]=.95;response["maximumAdditionalHeatLoss"]=.95;
  var responseCfg=ArmourResponses.Parse(response.ToJsonString());check(responseCfg.MaximumAdditionalKineticLoss==.95&&responseCfg.MaximumAdditionalHeatLoss==.95,"response schema2 both95allowed");
  void RejectResponse(Action<JsonObject> edit,string label){var value=(JsonObject)response.DeepClone();edit(value);bool failed=false;try{ArmourResponses.Parse(value.ToJsonString());}catch(FormatException){failed=true;}check(failed,label);}
  RejectResponse(x=>x["maximumAdditionalKineticLoss"]=.950001,"responseKE above95 rejected");
  RejectResponse(x=>x["maximumAdditionalHeatLoss"]=.950001,"responseHEAT above95 rejected");
  RejectResponse(x=>x["schemaVersion"]=3,"future response schema rejected");
  RejectResponse(x=>{x["schemaVersion"]=1;x["maximumAdditionalKineticLoss"]=.36;x["maximumAdditionalHeatLoss"]=.6;},"old responseKE35 retained");
  RejectResponse(x=>{x["schemaVersion"]=1;x["maximumAdditionalKineticLoss"]=.35;x["maximumAdditionalHeatLoss"]=.61;},"old responseHEAT60 retained");
  var strict=ArmourResponses.Parse(responseText);check(strict.MaximumAdditionalKineticLoss==.35&&strict.MaximumAdditionalHeatLoss==.6,"legacyresponse defaults untouched");
  var geometry=new ImpactGeometry(.9,.03,5,.125);
  var dyna=parsed.Recipes["dynaActive"];check(Math.Abs(ExperimentalRodModel.Loss(dyna,geometry,1)-.12)<1e-12,"DYNA reference12 notguardrail95");
  check(Math.Abs(ExperimentalRodModel.Loss(dyna,geometry with{LengthM=3.6},1)-.24)<1e-12,"DYNA susceptibilitymax2");
  check(ExperimentalRodModel.Loss(dyna,geometry,0)==0,"anglezero retained");
  var maximum=(JsonObject)root.DeepClone();maximum["recipes"]![0]!["baseLoss"]=.95;maximum["recipes"]![0]!["maximumLoss"]=.95;
  var highRecipe=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(maximum.ToJsonString()).Recipes.Values.First();check(ExperimentalRodModel.Loss(highRecipe,geometry,1)==.95,"95perrecipe boundary permitted");
  foreach(var cap in new[]{0d,.1,.35,.65,.95})foreach(var loss in new[]{0d,.12,.35,.6,.95}){
   var one=ExperimentalRodModel.Next(1,loss,cap);var two=ExperimentalRodModel.Next(one,loss,cap);
   check(one<=1&&two<=one&&two>=1-cap-1e-12,"newguardrailbudget monotonic neverzero");
  }
  check(ExperimentalRodModel.Next(.2,.8,.35)==.2,"mixed lowercap neverrefund");
  var heavy=responseCfg.Responses.Single(r=>r.ResponseId=="reliktActive");
  var shot=new ArmourShotState();var first=ArmourResponseModel.Apply(responseCfg,heavy,shot,"first","apfsds",true,true,13,30,0,true,.6,.95);
  var second=ArmourResponseModel.Apply(responseCfg,heavy,shot,"second","apfsds",true,true,13,30,0,true,.6,.95);
  check(Math.Abs(first*second-.16)<1e-12,"newcapspermitreviewedloss beyondold35");
  check(ArmourResponseModel.Apply(responseCfg,heavy,shot,"second","apfsds",true,true,13,30,0,true,.6,.95)==1,"duplicate callback stillneutral");
  var heatShot=new ArmourShotState();var heat=heavy with{Calibration=heavy.Calibration with{HeatRetention=.05}};
  check(Math.Abs(ArmourResponseModel.Apply(responseCfg,heat,heatShot,"heat","heat",true,true,13,30,0,true)-.05)<1e-12,"HEAT95boundary actualresponse");
  static float NativeBudget(float d,float m,float v,ushort k)=>(float)Math.Pow((float)Math.Sqrt(m)*v/((float)Math.Pow(d*1000f*.01f,.75f)*k),1.43f)*100f;
  static float NativeInverse(float d,float m,float target,ushort k)=>(float)Math.Pow(d*1000f*.01f,.75f)*k*(float)Math.Pow(target*.01f,.6993007063865662f)/(float)Math.Sqrt(m);
  foreach(var velocity in new[]{500f,1650f,2000f})foreach(var factor in new[]{.05f,.2f,.35f,.65f,.88f}){
   var incoming=NativeBudget(.03f,5f,velocity,1800);var speed=NativeInverse(.03f,5f,incoming*factor,1800);var outgoing=NativeBudget(.03f,5f,speed,1800);
   check(ExperimentalRodModel.ValidConversion(incoming,incoming*factor,outgoing,speed,velocity),"inversefloatconversion unchanged through2000m/s and95loss");
  }
 }
}