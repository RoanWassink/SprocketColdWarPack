using SprocketExpandedBallistics.ExperimentalArmour;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class PhysicalEraTests
{
 internal static void Run(Action<bool,string> check)
 {
  var text=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"physical-era-template.json"));
  var cfg=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(text);
  var old=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"nizh-template.json")));
  check(cfg.ParsedModelVersion=="2.0.4"&&cfg.Enabled&&cfg.Recipes.Count==18,"model2.0.4 exact18 standard template");
  check(SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.PhysicalResponseIds.Count==13,"13 reviewed physical rod identities");
  foreach(var pair in old.Recipes)
  {
   var r=cfg.Recipes[pair.Key];var o=pair.Value;
   check(r.BaseLoss==o.BaseLoss&&r.MaximumLoss==o.MaximumLoss&&r.ReferenceLD==o.ReferenceLD&&r.MinimumSusceptibility==o.MinimumSusceptibility&&r.MaximumSusceptibility==o.MaximumSusceptibility,"old five calibrations unchanged");
  }
  void Invalid(Action<JsonObject> mutate,string label)
  {
   var r=JsonNode.Parse(text)!.AsObject();mutate(r);var fail=false;
   try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(r.ToJsonString());}catch(FormatException){fail=true;}check(fail,label);
  }
  Invalid(r=>r["modelVersion"]="2.0.3","old model rejects physical identity/count");
  Invalid(r=>r["modelVersion"]="2.0.7","future model rejected");
  Invalid(r=>r["recipes"]![5]!["responseId"]="kontakt5PhysicalInsert","uncontracted alias rejected");
  Invalid(r=>r["recipes"]![5]!["responseId"]="nizhLmInsert","LM no kinetic route");
  Invalid(r=>r["recipes"]![5]!["responseId"]="kontakt1Active","Kontakt1 no kinetic route");
  Invalid(r=>r["recipes"]!.AsArray().Add(r["recipes"]![5]!.DeepClone()),"19 entries rejected");
  foreach(var id in SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.PhysicalResponseIds)
  {
   check(ExperimentalRodModel.Supported(id),"one exact supported physical route");
   foreach(var model in new[]{"2.0.2","2.0.3"})
   {
    var single=JsonNode.Parse(text)!;single["modelVersion"]=model;
    single["recipes"]=new JsonArray(single["recipes"]!.AsArray().Single(x=>x!["responseId"]!.GetValue<string>()==id)!.DeepClone());
    var fail=false;try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(single.ToJsonString());}catch(FormatException){fail=true;}check(fail,"old model rejects new ID even within count");
   }
   var recipe=cfg.Recipes[id];
   foreach(var ld in new[]{20d,30d,40d,80d})foreach(var weight in new[]{0d,.3,1d})foreach(var cap in new[]{0d,.1,.35})
   {
    var geometry=new ImpactGeometry(.02*ld,.02,3,.12);
    var loss=ExperimentalRodModel.Loss(recipe,geometry,weight);
    var expected=Math.Min(recipe.MaximumLoss,recipe.BaseLoss*Math.Clamp(Math.Sqrt(ld/30),.7,1.6)*weight);
    check(Math.Abs(loss-expected)<1e-12,"canonical family LD/angle curve");
    var r1=ExperimentalRodModel.Next(1,loss,cap);var r2=ExperimentalRodModel.Next(r1,loss,cap);
    check(r1<=1&&r2<=r1&&r2>=1-cap,"distinct reached stages share cap without refund");
   }
  }
  check(!ExperimentalRodModel.Supported("kontakt1Active")&&!ExperimentalRodModel.Supported("nizhLmInsert"),"CE-only families not admitted by exact rod table");
  var actualBase=ArmourResponses.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"physical-era-responses.json")));
  foreach(var id in SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.PhysicalResponseIds)
  {
   var response=actualBase.Responses.Single(x=>x.ResponseId==id);var recipe=cfg.Recipes[id];
   check(response.Kind=="heavyEra"&&response.Geometry.Mode=="declaredCassette"&&response.Calibration.IntactRodRetention==1&&response.Calibration.DisturbedRodRetention==1&&response.Calibration.DisturbanceContribution==0,"exact physical recipe neutral legacy KE");
   check(response.CompatibleMaterialIds.Count==1&&response.CompatibleMaterialIds.Single()==id,"exact material/response binding");
   check(ArmourResponses.PassiveMatches(response,(float)response.PassiveMaterial.Density,(float)response.PassiveMaterial.RhaFactor,(float)response.PassiveMaterial.SpallFactor),"exact native fingerprint float roundtrip");
   foreach(var ld in new[]{20d,30d,80d})
   {
    var loss=ExperimentalRodModel.Loss(recipe,new ImpactGeometry(.02*ld,.02,4,.12),1);var shot=new ArmourShotState();
    var depth=(response.Geometry.MinNormalThicknessMm+response.Geometry.MaxNormalThicknessMm)/2;
    var first=ArmourResponseModel.Apply(actualBase,response,shot,"front-vuid","apfsds",true,true,depth,30,0,true,loss,.35);
    check(Math.Abs(first-(1-loss))<1e-12,"single dynamic loss excludes legacy reduction");
    check(ArmourResponseModel.Apply(actualBase,response,shot,"front-vuid","apfsds",true,true,depth,30,0,true,loss,.35)==1,"same VUID no duplicate reaction");
    var second=ArmourResponseModel.Apply(actualBase,response,shot,"rear-vuid","apfsds",true,true,depth,30,0,true,loss,.35);
    check(Math.Abs(first*second-Math.Max(.65,(1-loss)*(1-loss)))<1e-12,"distinct physical layer budget sharecap");
    var invalid=new ArmourShotState();
    check(ArmourResponseModel.Apply(actualBase,response,invalid,"housing","apfsds",true,true,response.Geometry.MaxNormalThicknessMm+1,30,0,true,loss,.35)==1&&invalid.Applied.Count==0,"housing/wrongdepth not active paid layer");
   }
  }
  var lm=actualBase.Responses.Single(x=>x.ResponseId=="nizhLmInsert");
  check(lm.EligibleThreatClasses!.SequenceEqual(new[]{"unitaryAntitankGrenade"})&&!ArmourResponseModel.ReactiveThreat(lm,"heat"),"actual LM generated response strictclassification");
  check(ArmourResponseModel.ReactiveThreat(lm,"heat","unitaryAntitankGrenade"),"actual LM response classified test enabled");
  var k1=actualBase.Responses.Single(x=>x.ResponseId=="kontakt1Active");
  check(k1.Kind=="lightEra"&&!ArmourResponseModel.ReactiveThreat(k1,"apfsds"),"actual Kontakt1 CEonly");
  var bindingsText=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"physical-era-bindings.json"));
  var bindings=SprocketExpandedBallistics.Era.EraPartBindings.Parse(bindingsText);
  var bindingRoot=JsonNode.Parse(bindingsText)!;
  foreach(var entry in bindings.Bindings.Where(x=>actualBase.Responses.Any(r=>r.ResponseId==x.ResponseId)))
  {
   var response=actualBase.Responses.Single(x=>x.ResponseId==entry.ResponseId);
   check(entry.MatchesRecipe(response.ResponseId,response.Kind,response.CompatibleMaterialIds),"actual physical binding matches exact recipe");
   check(bindings.TryGetCassette(entry.CassetteComponentId,out _)&&!bindings.TryGetCassette(entry.MountComponentId,out _),"shared housing always passive classification");
  }
  foreach(var group in bindingRoot["sharedHousings"]!.AsArray())
  {
   var ids=group!["cassetteComponentIds"]!.AsArray().Select(x=>x!.GetValue<string>()).ToArray();
   var cells=new EraCells();var owned=new PlacedEraState();
   for(var n=0;n<ids.Length;n++)
   {
    var c=PlacedEraState.Key(1,n+1);check(cells.Consume(c),"distinct paid layer VUID independently consumed");owned.Track(c);
    check(!cells.Consume(c),"same physical VUID finite once");
   }
   owned.Reset(cells);check(cells.Count==0,"Edit resets all owned layers in sharedhousing");
  }
  void InvalidBinding(Action<JsonNode> mutation,string label)
  {
   var x=JsonNode.Parse(bindingsText)!;mutation(x);var fail=false;
   try{SprocketExpandedBallistics.Era.EraPartBindings.Parse(x.ToJsonString());}catch(FormatException){fail=true;}check(fail,label);
  }
  InvalidBinding(x=>{x["schemaVersion"]=1;x.AsObject().Remove("sharedHousings");},"oldschema1 still rejects duplicated housing");
  InvalidBinding(x=>x["sharedHousings"]!.AsArray().Clear(),"undeclared sharedhousing rejected");
  InvalidBinding(x=>x["sharedHousings"]![0]!["cassetteComponentIds"]!.AsArray().RemoveAt(0),"missing group member rejected");
  InvalidBinding(x=>x["sharedHousings"]![0]!["cassetteComponentIds"]!.AsArray().Add("unknown-cell"),"unknown sharedhousing member rejected");
  InvalidBinding(x=>x["sharedHousings"]![0]!["cassetteComponentIds"]!.AsArray().Add(x["sharedHousings"]![0]!["cassetteComponentIds"]![0]!.DeepClone()),"duplicate sharedhousing member rejected");
  var lower=JsonNode.Parse(text)!;lower["maximumAdditionalKineticLoss"]=.05;
  check(SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(lower.ToJsonString()).EffectiveKineticCap(.35)==.05,"new families honor low usercap");
 }
}
