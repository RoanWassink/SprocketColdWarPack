using System.Text.Json.Nodes;
using SprocketExpandedBallistics.ExperimentalArmour;
namespace SprocketExpandedBallistics;
internal static class UnifiedEraTests
{
 internal static void Run(Action<bool,string> check)
 {
  var response=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"physical-era-responses.json"));
  var sidecar=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"physical-era-template.json"));
  var before=ArmourCatalogueSnapshot.Validated(response,sidecar);
  check(before.Catalogue!=null&&before.Experimental!=null,"pairedsnapshotvalidatesboth");
  var startup=ArmourCatalogueSnapshot.Stage(response,"{invalid",true);
  check(startup.Catalogue!=null&&startup.Experimental==null&&startup.ExperimentalError!=null,"startup keepslegacycatalogue with explicitexperimentalerror");
  var startupIo=ArmourCatalogueSnapshot.Read(()=>response,()=>throw new IOException("sidecar locked"),true);
  check(startupIo.Catalogue!=null&&startupIo.Experimental==null&&startupIo.ExperimentalError=="sidecar locked","initial sidecar IO preserves valid legacy responses");
  var ioCurrent=before;var ioFailed=false;
  try{ioCurrent=ArmourCatalogueSnapshot.Read(()=>response,()=>throw new IOException("sidecar locked"),false);}catch(IOException){ioFailed=true;}
  check(ioFailed&&ReferenceEquals(ioCurrent,before),"reload sidecar IO retains complete previous pair");
  var current=before;bool failed=false;try{current=ArmourCatalogueSnapshot.Validated(response,"{invalid");}catch(FormatException){failed=true;}
  check(failed&&ReferenceEquals(current,before),"invalidsidecar retainsentireoldpair");
  failed=false;try{current=ArmourCatalogueSnapshot.Validated("{invalid",sidecar);}catch(FormatException){failed=true;}
  check(failed&&ReferenceEquals(current,before),"invalidresponse retainsentireoldpair");
  var newJson=JsonNode.Parse(response)!;newJson["enabled"]=!before.Catalogue!.Enabled;
  var after=ArmourCatalogueSnapshot.Validated(newJson.ToJsonString(),sidecar);
  var latched=before;current=after;
  check(latched.Catalogue!.Enabled!=current.Catalogue!.Enabled&&!ReferenceEquals(current,latched),"capturedprojectilepair old/new distinct");
  check(before.Revision!=after.Revision,"response-onlyedit changesregistryrevision");
  var materials=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sprocket.expandedballistics.material.reactions.json"));
  var triple=ArmourCatalogueSnapshot.Validated(response,sidecar,materials);
  check(triple.MaterialReactions is {Enabled:true},"material reactions captured with armour snapshot");
  var materialStartup=ArmourCatalogueSnapshot.Stage(response,sidecar,true,"{invalid");
  check(materialStartup.Catalogue!=null&&materialStartup.Experimental!=null&&materialStartup.MaterialReactions==null&&materialStartup.MaterialReactionError!=null,"invalid startup material file preserves established armour");
  var oldTriple=triple;failed=false;
  try{triple=ArmourCatalogueSnapshot.Validated(response,sidecar,"{invalid");}catch(FormatException){failed=true;}
  check(failed&&ReferenceEquals(triple,oldTriple),"material parse failure retains entire snapshot on reload");
  failed=false;
  try{triple=ArmourCatalogueSnapshot.Read(()=>response,()=>sidecar,false,()=>throw new IOException("material locked"));}catch(IOException){failed=true;}
  check(failed&&ReferenceEquals(triple,oldTriple),"material IO failure retains entire snapshot on reload");
  var materialIo=ArmourCatalogueSnapshot.Read(()=>response,()=>sidecar,true,()=>throw new IOException("material locked"));
  check(materialIo.Catalogue!=null&&materialIo.Experimental!=null&&materialIo.MaterialReactionError=="material locked","startup material IO error preserves existing adapters");
  var changedMaterial=JsonNode.Parse(materials)!;changedMaterial["enabled"]=false;
  var disabledMaterials=ArmourCatalogueSnapshot.Validated(response,sidecar,changedMaterial.ToJsonString());
  check(disabledMaterials.MaterialReactions is {Enabled:false}&&oldTriple.MaterialReactions is {Enabled:true},"captured flight keeps original material rules across reload");
  check(disabledMaterials.Revision!=oldTriple.Revision,"material-only change updates shared revision");
  var recipeRoot=JsonNode.Parse(sidecar)!.AsObject();recipeRoot["modelVersion"]="2.0.6";recipeRoot["enabled"]=true;recipeRoot["maximumAdditionalKineticLoss"]=.65;
  var ids=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.LegacyMaterialResponseIds;
  var sourceMap=new Dictionary<string,string>{{"reliktCassette","reliktActive"},{"kontakt5Cassette","kontakt5Active"},{"nizhCassette","nizhLegacyActive"},{"dupletCassette","dupletLegacySingleActive"}};
  foreach(var alias in sourceMap){var original=recipeRoot["recipes"]!.AsArray().Single(r=>r!["responseId"]!.GetValue<string>()==alias.Value)!.DeepClone();original["responseId"]=alias.Key;recipeRoot["recipes"]!.AsArray().Add(original);}
  var exp=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(recipeRoot.ToJsonString());check(exp.Recipes.Count==22,"historical18 plus4mappedmaterials");
  var responseCfg=ArmourResponses.Parse(response);
  foreach(var alias in sourceMap){
   var source=exp.Recipes[alias.Value];var ordinary=exp.Recipes[alias.Key];check(ordinary.BaseLoss==source.BaseLoss&&ordinary.MaximumLoss==source.MaximumLoss,"mappedonecellfamilycoeffs");
   var sourceRow=responseCfg.Responses.Single(r=>r.ResponseId==alias.Value);
   var retained=new Dictionary<string,double>{{"reliktCassette",.85},{"kontakt5Cassette",.90},{"nizhCassette",.88},{"dupletCassette",.80}};
   var row=sourceRow with{ResponseId=alias.Key,Calibration=sourceRow.Calibration with{IntactRodRetention=retained[alias.Key],DisturbedRodRetention=retained[alias.Key]},Geometry=sourceRow.Geometry with{MinNormalThicknessMm=60,MaxNormalThicknessMm=80}};
   var neutral=row with{Calibration=row.Calibration with{IntactRodRetention=1,DisturbedRodRetention=1,DisturbanceContribution=0}};
   check(ExperimentalRodModel.UsesDynamic(neutral,exp),"ordinaryneutralconfigured usesdynamic");
   check(!ExperimentalRodModel.UsesDynamic(row,exp),"ordinarycustomlegacy calibration retainslegacyKE");
   check(!ExperimentalRodModel.UsesDynamic(neutral,SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(sidecar)),"oldsidecarnorecipe excludesordinarydynamic");
   check(!ExperimentalRodModel.UsesDynamic(neutral,null),"missingexp ordinarydynamicoff");
   foreach(var ld in new[]{30d,40d,120d}){
    var g=new ImpactGeometry(ld*.03,.03,5,.12);
    check(ExperimentalRodModel.Loss(source,g,1)==ExperimentalRodModel.Loss(ordinary,g,1),"ordinarysamefamilyLDresponse");
    var shot=new ArmourShotState();var loss=ExperimentalRodModel.Loss(ordinary,g,1);
    var value=ArmourResponseModel.Apply(responseCfg,neutral,shot,"cell","apfsds",true,true,70,30,0,true,loss,.35);
    check(value<=1&&value>=.65,"ordinaryactual60to80depth acceptedbounded");
    check(ArmourResponseModel.Apply(responseCfg,neutral,shot,"cell","apfsds",true,true,70,30,0,true,loss,.35)==1,"ordinarysamecell nodoublecharge");
    var invalid=new ArmourShotState();check(ArmourResponseModel.Apply(responseCfg,neutral,invalid,"thin","apfsds",true,true,13,30,0,true,loss,.35)==1,"physical13mmgate NOT copiedtoordinary70mmmaterial");
   }
   var custom=row with{Calibration=row.Calibration with{IntactRodRetention=.7,DisturbedRodRetention=.7}};
   check(!ExperimentalRodModel.UsesDynamic(custom,exp),"editedordinaryretention70 stayslegacyroute");
   var customShot=new ArmourShotState();var customFactor=ArmourResponseModel.Apply(responseCfg,custom,customShot,"custom","apfsds",true,true,70,65,0,true);
   check(Math.Abs(customFactor-(1-.3*ArmourResponseModel.AngularWeight(custom,"apfsds",65)))<1e-12,"editedordinary65degree legacyKE unchanged");
   var legacyShot=new ArmourShotState();check(ArmourResponseModel.Apply(responseCfg,row,legacyShot,"old","apfsds",true,true,70,30,0,true)<1,"nonneutralcustom actuallegacyresponse active");
  }
  foreach(var model in new[]{"2.0.4","2.0.5"}){
   var reject=(JsonObject)recipeRoot.DeepClone();reject["modelVersion"]=model;reject["maximumAdditionalKineticLoss"]=.35;reject["recipes"]=new JsonArray(reject["recipes"]!.AsArray().Last()!.DeepClone());
   failed=false;try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(reject.ToJsonString());}catch(FormatException){failed=true;}check(failed,"oldmodel excludesordinaryIDs "+model);
  }
  var full=(JsonObject)recipeRoot.DeepClone();var dyna=full["recipes"]![5]!.DeepClone();dyna["responseId"]="dynaActive";full["recipes"]!.AsArray().Add(dyna);
  check(SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(full.ToJsonString()).Recipes.Count==23,"model2.0.6 exact23 boundary");
  full["recipes"]!.AsArray().Add(dyna.DeepClone());failed=false;try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(full.ToJsonString());}catch(FormatException){failed=true;}check(failed,"24recipes capacity rejected");
  var invalidPrice=JsonNode.Parse(response)!.AsObject();invalidPrice["responses"]![0]!["passiveMaterial"]!["density"]=100;invalidPrice["responses"]![0]!["passiveMaterial"]!["rhaFactor"]=5;
  failed=false;try{ArmourResponses.Parse(invalidPrice.ToJsonString());}catch(FormatException){failed=true;}check(failed,"canonicalproducer nativepriceoverflowguard retained");
  var future=(JsonObject)recipeRoot.DeepClone();future["modelVersion"]="2.0.7";failed=false;try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(future.ToJsonString());}catch(FormatException){failed=true;}check(failed,"unreviewedfuturemodel rejected");
  if(Environment.GetEnvironmentVariable("SPROCKET_ERA_REVIEW_DIRECTORY") is {Length:>0} reviewDirectory)
  {
   var staged=ArmourCatalogueSnapshot.Validated(File.ReadAllText(Path.Combine(reviewDirectory,"sprocket.expandedballistics.responses.json")),File.ReadAllText(Path.Combine(reviewDirectory,"sprocket.expandedballistics.experimental.json")));
   check(staged.Catalogue!=null&&staged.Experimental?.Recipes.Count==23,"actual staged private catalogues parse together");
   foreach(var alias in sourceMap)
   {
    var material=staged.Catalogue!.Responses.Single(r=>r.ResponseId==alias.Key);
    check(ExperimentalRodModel.UsesDynamic(material,staged.Experimental),"actual staged material uses single-stage dynamic family");
    check(ArmourResponseModel.AngularWeight(material,"apfsds",65)==1,"actual staged material retains full effect at65degrees");
   }
   var temp=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"sprocket-material-editor-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
   try
   {
    SprocketExpandedBallistics.Materials.MaterialFeature.ArmourSnapshot=staged;
    var customJson=JsonNode.Parse(staged.ResponseJson!)!;
    var customRow=customJson["responses"]!.AsArray().Single(r=>r!["responseId"]!.GetValue<string>()=="reliktCassette")!;
    customRow["calibration"]!["intactRodRetention"]=.7;customRow["calibration"]!["disturbedRodRetention"]=.75;
    var path=Path.Combine(temp,"responses.json");File.WriteAllText(path,customJson.ToJsonString());
    var material=customRow["passiveMaterial"]!;
    var technology=new JsonObject{["v"]="0.0",["id"]="",["type"]="reliktCassette",["date"]="2006.01.01",["properties"]=new JsonObject{["density"]=material["density"]!.DeepClone(),["rhaFactor"]=material["rhaFactor"]!.DeepClone(),["spallFactor"]=material["spallFactor"]!.DeepClone(),["costMultiplier"]=material["requestedCostMultiplier"]!.DeepClone()}};
    var techPath=Path.Combine(temp,"technology.json");File.WriteAllText(techPath,technology.ToJsonString());var techBefore=File.ReadAllText(techPath);
    var draft=new SprocketExpandedBallistics.Materials.MaterialEditorDraft(temp,path,Path.Combine(temp,"labels.json"),new[]{("reliktCassette","Relikt",techPath)},staged.ResponseJson!);
    check(!draft.IsExperimental("reliktCassette"),"draft custom disk calibration overrides stale neutral live snapshot");
    check(draft.IsExperimental("experimentalReliktFrontCassette"),"physical recipe retains strict dynamic guard");
    draft.Set(0,"label","Custom Relikt");draft.Save();
    var saved=ArmourResponses.Parse(File.ReadAllText(path)).Responses.Single(r=>r.ResponseId=="reliktCassette");
    check(saved.Calibration.IntactRodRetention==.7&&saved.Calibration.DisturbedRodRetention==.75,"label-only real draft Save preserves manually customized KE");
    check(saved.Calibration.DisturbanceContribution==staged.Catalogue!.Responses.Single(r=>r.ResponseId=="reliktCassette").Calibration.DisturbanceContribution,"label-only save preserves disturbance");
    check(File.ReadAllText(techPath)==techBefore,"label-only save leaves native material bytes unchanged");
    File.WriteAllText(path,staged.ResponseJson!);
    var neutralDraft=new SprocketExpandedBallistics.Materials.MaterialEditorDraft(temp,path,Path.Combine(temp,"labels.json"),new[]{("reliktCassette","Relikt",techPath)},staged.ResponseJson!);
    check(neutralDraft.IsExperimental("reliktCassette"),"freshly loaded neutral alias retains dynamic guard");
   }
   finally{Directory.Delete(temp,true);}
  }
 }
}
