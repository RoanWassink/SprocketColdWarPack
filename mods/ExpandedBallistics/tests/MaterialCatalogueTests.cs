using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class MaterialCatalogueTests
{
    internal static void Run(Action<bool,string> check)
    {
        var json=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName));
        var catalogue=MaterialReactionCatalogue.Parse(json);
        var workJson=JsonNode.Parse(json)!.AsObject();
        workJson["workCalibration"]=new JsonObject{["rodDensityKgM3"]=18000,["rodLengthM"]=.5,["rodSpeedMps"]=1500,["jetBudgetMm"]=700,["interfaceWeight"]=.4,["steelErosionShare"]=.7,["otherErosionShare"]=.2,["directErosionShare"]=.6,["backedErosionShare"]=.4,["sandwichErosionShare"]=.1};
        workJson["recipes"]![0]!["workErosionShare"]=.25;
        var calibrated=MaterialReactionCatalogue.Parse(workJson.ToJsonString());
        check(calibrated.Work.RodDensityKgM3==18000&&calibrated.Work.JetBudgetMm==700&&calibrated.Recipes["hardSteel"].WorkErosionShare==.25,"optional work calibration and per-recipe erosion share parse explicitly");
        check(MaterialReactionCatalogue.Parse(json).Work.RodLengthM==.12,"production catalogue carries the live 0.12m rod work calibration");
        foreach(var field in new[]{"interfaceWeight","steelErosionShare","sandwichErosionShare"})
        {
            var invalid=JsonNode.Parse(workJson.ToJsonString())!.AsObject();invalid["workCalibration"]![field]=1.1;
            bool rejected=false;try{MaterialReactionCatalogue.Parse(invalid.ToJsonString());}catch(FormatException){rejected=true;}
            check(rejected,"out-of-range work calibration rejected: "+field);
        }
        var presentation=SprocketExpandedBallistics.Materials.MaterialReactionPresentation.Summary(catalogue.Recipes["fragmentLiner"],true);
        check(presentation.Any(s=>s.Contains("unavailable"))&&!presentation.Any(s=>s.StartsWith("Absorbs")),"dead fragment hook never advertises working absorption");
        check(SprocketExpandedBallistics.Materials.MaterialReactionPresentation.Summary(catalogue.Recipes["elastomerSandwich"],true).Any(s=>s.Contains("cassette recipe")),"tooltip explains precomputed support");
        // Authoring sidecar preserves unavailable materials, isolates customized shared
        // roles, and supplies optimistic transaction bytes instead of writing directly.
        var roleDraft=new SprocketExpandedBallistics.Materials.MaterialReactionDraft(Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"Sprocket-material-reactions-"+Guid.NewGuid().ToString("N")+".json"),json);
        var item=new JsonObject{["id"]="highHardnessSteel",["density"]=7850d,["rhaFactor"]=1.16d,["spallFactor"]=.00065d};
        roleDraft.Attach(item);
        check(item["reactionRecipe"]!.GetValue<string>()=="hardSteel","editor attaches existing role without changing saved material IDs");
        item["reaction"]!["lossPerReference"]=.18d;
        var authored=roleDraft.SaveChange(new JsonArray(item));var edited=MaterialReactionCatalogue.Parse(authored.Updated);
        check(authored.Original==null&&edited.Bindings["highHardnessSteel"].RecipeId!="hardSteel"&&edited.Recipes["hardSteel"].LossPerReference==.03,"customizing one role does not rewrite shared recipe");
        check(edited.Bindings.ContainsKey("ceramicArray")&&edited.Bindings.ContainsKey("aramidSpallLiner"),"saving loaded materials preserves unavailable role bindings");
        roleDraft.Select(item,"none");
        var removed=MaterialReactionCatalogue.Parse(roleDraft.SaveChange(new JsonArray(JsonNode.Parse(item.ToJsonString()))).Updated);
        check(!removed.Bindings.ContainsKey("highHardnessSteel")&&removed.Bindings.ContainsKey("ceramicArray"),"passive selection removes only edited material role");
        roleDraft.Enabled=false;
        check(!MaterialReactionCatalogue.Parse(roleDraft.SaveChange(new JsonArray()).Updated).Enabled,"editor sidecar enable switch persists independently of native materials");
        foreach(var mutation in new Action<JsonObject>[] {r=>r["unknown"]=true,r=>r["maximumInteractionLoss"]=-1,r=>r["bindings"]!.AsArray().Add(JsonNode.Parse(r["bindings"]![0]!.ToJsonString()))})
        {
            var bad=JsonNode.Parse(json)!.AsObject();mutation(bad);bool rejected=false;try{MaterialReactionCatalogue.Parse(bad.ToJsonString());}catch{rejected=true;}check(rejected,"malformed reaction catalogue rejected without partial activation");
        }
    }
}


