using System.Text.Json;
using System.Text.Json.Nodes;
using SprocketJsonEditor;
using SprocketExpandedBallistics;
namespace SprocketExpandedBallistics.Materials;

// Editor-local authoring snapshot: actual JSON transaction supplies conflict detection.
internal sealed class MaterialReactionDraft
{
    private readonly string path;private readonly string? original;private readonly JsonObject root;
    internal MaterialReactionDraft(string path,string fallback)
    {
        this.path=path;original=File.Exists(path)?File.ReadAllText(path):null;
        root=JsonNode.Parse(original??fallback)!.AsObject();MaterialReactionCatalogue.Parse(root.ToJsonString());
    }
    internal bool Enabled {get=>root["enabled"]!.GetValue<bool>();set=>root["enabled"]=value;}
    internal IEnumerable<(string Id,string Label)> Recipes=>root["recipes"]!.AsArray().Select(r=>(r!["id"]!.GetValue<string>(),r["label"]!.GetValue<string>()));
    internal void Attach(JsonObject item)
    {
        var binding=root["bindings"]!.AsArray().FirstOrDefault(b=>b!["materialId"]!.GetValue<string>()==item["id"]!.GetValue<string>());
        var id=binding?["recipeId"]?.GetValue<string>();
        if(id!=null)Select(item,id);else{item["reactionRecipe"]="none";item["reaction"]=null;}
    }
    internal string? Label(string id)=>Recipes.FirstOrDefault(r=>r.Id==id).Label;
    internal void Select(JsonObject item,string id)
    {
        var recipe=id=="none"?null:(root["recipes"]!.AsArray().FirstOrDefault(r=>r!["id"]!.GetValue<string>()==id)??throw new FormatException("Unsupported material response."));
        item["reactionRecipe"]=id;item["reaction"]=recipe==null?null:JsonNode.Parse(recipe.ToJsonString());
    }
    internal JsonFileChange SaveChange(JsonArray items)
    {
        var candidate=JsonNode.Parse(root.ToJsonString())!.AsObject();var bindings=candidate["bindings"]!.AsArray();var recipes=candidate["recipes"]!.AsArray();
        var edited=items.Select(p=>p!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        foreach(var binding in bindings.ToArray())if(edited.Contains(binding!["materialId"]!.GetValue<string>()))bindings.Remove(binding);
        foreach(var item in items.OfType<JsonObject>())
        {
            var selected=item["reactionRecipe"]?.GetValue<string>();if(selected==null||selected=="none")continue;
            var material=item["id"]!.GetValue<string>();var recipe=JsonNode.Parse(item["reaction"]!.ToJsonString())!.AsObject();
            var loaded=root["recipes"]!.AsArray().FirstOrDefault(r=>r!["id"]!.GetValue<string>()==selected);
            string recipeId=selected;
            if(loaded==null||loaded.ToJsonString()!=recipe.ToJsonString())
            {
                recipeId="materialRole_"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material))).Substring(0,16).ToLowerInvariant();
                // An unrelated author's shared recipe is never silently overwritten.
                int suffix=1;var seed=recipeId;
                while(bindings.Any(b=>b!["recipeId"]!.GetValue<string>()==recipeId&&b["materialId"]!.GetValue<string>()!=material))recipeId=seed+"_"+suffix++;
                recipe["id"]=recipeId;var previous=recipes.FirstOrDefault(r=>r!["id"]!.GetValue<string>()==recipeId);
                if(previous!=null)recipes.Remove(previous);recipes.Add(recipe);
            }
            bindings.Add(new JsonObject{["materialId"]=material,["recipeId"]=recipeId,["density"]=item["density"]!.GetValue<double>(),["rhaFactor"]=item["rhaFactor"]!.GetValue<double>(),["spallFactor"]=item["spallFactor"]!.GetValue<double>()});
        }
        var json=candidate.ToJsonString(new JsonSerializerOptions{WriteIndented=true});MaterialReactionCatalogue.Parse(json);
        return new(path,original,json);
    }
}
