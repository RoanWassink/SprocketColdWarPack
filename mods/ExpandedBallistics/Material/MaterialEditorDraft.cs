using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using SprocketJsonEditor;
namespace SprocketExpandedBallistics.Materials;
internal sealed class MaterialEditorDraft
{
    private readonly MaterialReactionDraft? materialReactions;
    internal IEnumerable<(string Id,string Label)> ReactionChoices=>materialReactions?.Recipes??Array.Empty<(string,string)>();
    private readonly HashSet<string> dynamicAliases=new(StringComparer.Ordinal);
    internal bool IsExperimental(string id)
    {
        if(SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.LegacyMaterialResponseIds.Contains(id,StringComparer.Ordinal))
        {
            return dynamicAliases.Contains(id);
        }
        return EraGeneratedDefinitions.MaterialRoutes.TryGetValue(id,out var route)&&route.NeutralKinetic;
    }
    internal JsonArray Items {get;}=new();
    internal JsonArray Templates {get;}=new();
    private readonly string technologyDirectory,responsePath,labelPath;
    private readonly string? originalResponses,originalLabels;
    private readonly JsonObject catalogue;
    private readonly Dictionary<string,(string Path,string Text,JsonObject Root)> sources=new();
    internal double MinimumHeatRetention=>1-catalogue["maximumAdditionalHeatLoss"]!.GetValue<double>();
    internal double MaximumHeatLoss=>catalogue["maximumAdditionalHeatLoss"]!.GetValue<double>();
    internal double MinimumRodRetention=>1-catalogue["maximumAdditionalKineticLoss"]!.GetValue<double>();
    internal bool EffectsEnabled=>catalogue["enabled"]!.GetValue<bool>();
    internal bool EffectsEnabledFor(int index)=>Items[index]!["reaction"]!=null?materialReactions?.Enabled??false:EffectsEnabled;
    internal MaterialEditorDraft(string technologyDirectory,string responsePath,string labelPath,IEnumerable<(string Id,string Label,string Source)> materials,string fallback,string? reactionPath=null,string? reactionFallback=null)
    {
        this.technologyDirectory=technologyDirectory;this.responsePath=responsePath;this.labelPath=labelPath;
        originalResponses=File.Exists(responsePath)?File.ReadAllText(responsePath):null;
        catalogue=JsonNode.Parse(originalResponses??fallback)!.AsObject();
        // Persist the 0.22.8 minimumEra as a real date on the next Save as well.
        if(catalogue["responses"] is JsonArray rows)
            foreach(var row in rows.OfType<JsonObject>())
                if(row["minimumEra"] is JsonValue era)
                {
                    var date=string.Equals(era.GetValue<string>().Trim(),"coldwar",StringComparison.OrdinalIgnoreCase)
                        ?"1945.09.03"
                        :throw new FormatException("Unsupported legacy minimumEra; use minimumDate.");
                    row["minimumDate"]=JsonValue.Create(date)!;
                    row["historicalDate"]=JsonValue.Create(date)!;
                    row.Remove("minimumEra");
                }
        var loadedResponses=ArmourResponses.Parse(catalogue.ToJsonString());
        // Decide from this session's freshly read file, never from older live responses.
        var experimental=MaterialFeature.ArmourSnapshot.Experimental;
        foreach(var response in loadedResponses.Responses)
            if(SprocketExpandedBallistics.ExperimentalRodModel.UsesDynamic(response,experimental))
                foreach(var id in response.CompatibleMaterialIds)
                    if(SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.LegacyMaterialResponseIds.Contains(id,StringComparer.Ordinal))dynamicAliases.Add(id);
        originalLabels=File.Exists(labelPath)?File.ReadAllText(labelPath):null;
        var labels=originalLabels==null?new JsonObject():ReadLabels(originalLabels);
        foreach(var material in materials)
        {
            // The native signature of a cassette is derived from its immutable
            // sidecar. Versioned composition editing belongs to the composer.
            if(material.Id.StartsWith("ebCassette_",StringComparison.Ordinal))continue;
            var text=File.ReadAllText(material.Source);var root=JsonNode.Parse(text,documentOptions:new JsonDocumentOptions{AllowTrailingCommas=true})!.AsObject();
            if(sources.ContainsKey(material.Id))continue;
            sources[material.Id]=(material.Source,text,root);
            var response=catalogue["responses"]!.AsArray().FirstOrDefault(r=>r!["compatibleMaterialIds"]!.AsArray().Any(x=>x!.GetValue<string>()==material.Id));
            Items.Add(Entry(material.Id,labels[material.Id]?.GetValue<string>()??material.Label,root,response));
        }
        var passive=new JsonObject { ["v"]="0.0",["id"]="",["type"]="template",["date"]="1945.09.03",["properties"]=new JsonObject{["rhaFactor"]=1d,["density"]=7850d,["spallFactor"]=.0005,["costMultiplier"]=2d}};
        Templates.Add(Entry("template","Passive material",passive,null));
        foreach(var r in catalogue["responses"]!.AsArray().Concat(JsonNode.Parse(fallback)!["responses"]!.AsArray()).GroupBy(r=>r!["kind"]!.GetValue<string>()).Select(g=>g.First()))
        {
            var recipe=JsonNode.Parse(passive.ToJsonString())!.AsObject();var physical=r!["passiveMaterial"]!;
            recipe["properties"]=new JsonObject{["rhaFactor"]=physical["rhaFactor"]!.GetValue<double>(),["density"]=physical["density"]!.GetValue<double>(),["spallFactor"]=physical["spallFactor"]!.GetValue<double>(),["costMultiplier"]=physical["requestedCostMultiplier"]!.GetValue<double>()};
            Templates.Add(Entry("template",BehaviorLabel(r["kind"]!.GetValue<string>()),recipe,r));
        }
        if(reactionPath!=null&&reactionFallback!=null)
        {
            materialReactions=new(reactionPath,reactionFallback);
            foreach(var item in Items.OfType<JsonObject>())materialReactions.Attach(item);
            foreach(var template in Templates.OfType<JsonObject>())materialReactions.Attach(template);
            foreach(var role in materialReactions.Recipes)
            {
                var source=Items.FirstOrDefault(p=>p!["reactionRecipe"]?.GetValue<string>()==role.Id)??Templates[0]!;
                var template=JsonNode.Parse(source.ToJsonString())!.AsObject();template["id"]="template";template["label"]=role.Label;
                template["behavior"]="passive";template["response"]=null;materialReactions.Select(template,role.Id);Templates.Add(template);
            }
        }
    }
    private static JsonObject Entry(string id,string label,JsonObject root,JsonNode? response)=>new()
    {
        ["id"]=id,["label"]=label,["behavior"]=response?["kind"]?.GetValue<string>()??"passive",["date"]=root["date"]?.GetValue<string>()??"0000.01.01",
        ["rhaFactor"]=root["properties"]!["rhaFactor"]!.GetValue<double>(),["density"]=root["properties"]!["density"]!.GetValue<double>(),
        ["spallFactor"]=root["properties"]?["spallFactor"]?.GetValue<double>()??0,["costMultiplier"]=root["properties"]?["costMultiplier"]?.GetValue<double>()??0,
        ["response"]=response==null?null:JsonNode.Parse(response.ToJsonString())
    };
    internal static string BehaviorLabel(string kind)=>kind switch {"nera"=>"NERA","lightEra"=>"Light ERA","heavyEra"=>"Heavy ERA","glassTextolite"=>"Glass / textolite","passiveComposite"=>"Passive composite",_=>"Passive"};
    internal JsonNode Value(int index,string key)
    {
        if(key=="behavior"&&Items[index]!["reactionRecipe"]?.GetValue<string>() is {} role&&role!="none")return JsonValue.Create("material:"+role)!;
        JsonNode node=Items[index]!;foreach(var part in key.Split('.'))node=node is JsonArray a?a[int.Parse(part)]!:node[part]!;return node;
    }
    internal void Set(int index,string field,string value)
    {
        if(IsExperimental(Items[index]!["id"]!.GetValue<string>())&&field=="response.geometry.mode"&&value!="declaredCassette")throw new FormatException("ERA rod recipes require cassette geometry.");
        if(field=="effectsEnabled"){catalogue["enabled"]=bool.Parse(value);if(materialReactions!=null)materialReactions.Enabled=bool.Parse(value);return;}
        if(field=="id")throw new FormatException("Material IDs cannot change.");
        var parts=field.Split('.');JsonNode node=Items[index]!;foreach(var part in parts.SkipLast(1))node=node is JsonArray a?a[int.Parse(part)]!:node[part]!;
        JsonNode updated;
        if(field is "label" or "date" or "behavior" || field.EndsWith(".mode")||field.Contains(".threatTags."))updated=JsonValue.Create(value)!;
        else{if(!double.TryParse(value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n))throw new FormatException("Enter a finite number.");updated=JsonValue.Create(n)!;}
        if(node is JsonArray arr)arr[int.Parse(parts[^1])]=updated;else node[parts[^1]]=updated;
    }
    internal void Behavior(int index,string kind)
    {
        if(EraGeneratedDefinitions.MaterialRoutes.TryGetValue(Items[index]!["id"]!.GetValue<string>(),out var registered))
        {
            if(kind!=registered.Kind)throw new FormatException("Placed ERA requires its registered response behavior; duplicate it to author another material.");
            return; // Preserve this insert's own thickness/angle/calibration recipe.
        }
        var p=Items[index]!.AsObject();
        if(kind.StartsWith("material:",StringComparison.Ordinal))
        {
            if(materialReactions==null||p["id"]!.GetValue<string>() is "rha" or "sheetMetal")throw new FormatException("Duplicate this native material before adding a material response.");
            materialReactions.Select(p,kind.Substring(9));p["behavior"]="passive";p["response"]=null;return;
        }
        if(p["id"]!.GetValue<string>() is "rha" or "sheetMetal" && kind!="passive")throw new FormatException("Duplicate this native material before adding special behavior.");
        JsonNode? template=null;
        if(kind!="passive")template=Templates.FirstOrDefault(r=>r!["behavior"]!.GetValue<string>()==kind)?["response"]??throw new FormatException("Unsupported material behavior.");
        if(materialReactions!=null)materialReactions.Select(p,"none");
        p["behavior"]=kind;p["response"]=template==null?null:JsonNode.Parse(template.ToJsonString());
    }
    internal int Duplicate(JsonObject template)
    {
        int i=1;while(Items.Any(p=>p!["id"]!.GetValue<string>()=="sprocketCustomMaterial"+i)||File.Exists(Path.Combine(technologyDirectory,"SprocketCustomMaterial"+i+".json")))i++;
        var item=JsonNode.Parse(template.ToJsonString())!.AsObject();item["id"]="sprocketCustomMaterial"+i;item["label"]="Custom material "+i;Items.Add(item);return Items.Count-1;
    }
    internal void Save()
    {
        var changes=new List<JsonFileChange>();var labels=originalLabels==null?new JsonObject():JsonNode.Parse(JsonNode.Parse(originalLabels)!["labels"]!.ToJsonString())!.AsObject();var responses=JsonNode.Parse(catalogue.ToJsonString())!.AsObject();var list=responses["responses"]!.AsArray();
        var editedIds=Items.Select(p=>p!["id"]!.GetValue<string>()).ToHashSet();
        // Remove only edited bindings. Preserve responses for unavailable/unloaded materials.
        foreach(var r in list.ToArray())
        {
            var bindings=r!["compatibleMaterialIds"]!.AsArray();foreach(var b in bindings.ToArray())if(editedIds.Contains(b!.GetValue<string>()))bindings.Remove(b);
            if(bindings.Count==0)list.Remove(r);
        }
        foreach(var node in Items)
        {
            var p=node!.AsObject();var id=p["id"]!.GetValue<string>();var label=p["label"]!.GetValue<string>();
            if(string.IsNullOrWhiteSpace(label)||label.Length>80||label.Any(char.IsControl))throw new FormatException("Material names must contain 1–80 printable characters.");
            if(!TryDate(p["date"]!.GetValue<string>()))throw new FormatException("Invalid material date; use yyyy.MM.dd.");
            var density=p["density"]!.GetValue<double>();var rha=p["rhaFactor"]!.GetValue<double>();var spall=p["spallFactor"]!.GetValue<double>();var cost=p["costMultiplier"]!.GetValue<double>();
            if(!MaterialBalance.Calculate((float)rha,(float)density,(float)cost).IsValid||spall<0||!double.IsFinite(spall))throw new FormatException("Invalid passive material values: "+label);
            var existing=sources.TryGetValue(id,out var source);var recipe=existing?JsonNode.Parse(source.Root.ToJsonString())!.AsObject():new JsonObject{["v"]="0.0",["id"]="",["type"]=id,["properties"]=new JsonObject()};
            recipe["date"]=p["date"]!.GetValue<string>();foreach(var key in new[]{"density","rhaFactor","spallFactor","costMultiplier"})recipe["properties"]![key]=p[key]!.GetValue<double>();
            var formatted=recipe.ToJsonString(new JsonSerializerOptions{WriteIndented=true});
            // Preserve native bytes when the semantic recipe has not changed.
            changes.Add(new(existing?source.Path:Path.Combine(technologyDirectory,"SprocketCustomMaterial"+id.Substring("sprocketCustomMaterial".Length)+".json"),existing?source.Text:null,existing&&recipe.ToJsonString()==source.Root.ToJsonString()?source.Text:formatted));
            labels[id]=label;
            if(p["behavior"]!.GetValue<string>()!="passive")
            {
                var response=JsonNode.Parse(p["response"]!.ToJsonString())!.AsObject();
                // Placed ERA binds exact response identities. Preserve the loaded
                // route (including customized binding IDs), not an editor alias.
                if(!EraGeneratedDefinitions.MaterialRoutes.ContainsKey(id))response["responseId"]="editor_"+id;
                response["compatibleMaterialIds"]=new JsonArray(JsonValue.Create(id));
                if(IsExperimental(id))
                {
                    if(p["behavior"]!.GetValue<string>()!="heavyEra")throw new FormatException("ERA rod response requires Heavy ERA behavior; duplicate it to author another material.");
                    response["calibration"]!["intactRodRetention"]=1d;
                    response["calibration"]!["disturbedRodRetention"]=1d;
                    response["calibration"]!["disturbanceContribution"]=0d;
                }
                response["passiveMaterial"]=new JsonObject{["density"]=density,["rhaFactor"]=rha,["spallFactor"]=spall,["requestedCostMultiplier"]=cost};
                list.Add(response);
            }
        }
        // Keep the catalogue's global enable switch and limits unchanged.
        if(list.Count==0)throw new FormatException("The existing catalogue requires at least one response.");
        var json=responses.ToJsonString(new JsonSerializerOptions{WriteIndented=true});ArmourResponses.Parse(json);
        changes.Add(new(responsePath,originalResponses,json));
        changes.Add(new(labelPath,originalLabels,new JsonObject{["schemaVersion"]=1,["labels"]=labels}.ToJsonString(new JsonSerializerOptions{WriteIndented=true})));
        if(materialReactions!=null)changes.Add(materialReactions.SaveChange(Items));
        JsonFileTransaction.Commit(changes);
    }
    internal static JsonObject ReadLabels(string json)
    {
        var root=JsonNode.Parse(json)!;
        if(root["schemaVersion"]?.GetValue<int>()!=1)throw new FormatException("Unsupported material editor label schema.");
        var labels=root["labels"]!.AsObject();
        foreach(var pair in labels){var label=pair.Value!.GetValue<string>();if(string.IsNullOrWhiteSpace(label)||label.Length>80||label.Any(char.IsControl))throw new FormatException("Invalid material name.");}
        return labels;
    }
    internal static bool TryDate(string text)
    {
        var bits=text.Split('.');return bits.Length==3&&int.TryParse(bits[0],out var y)&&int.TryParse(bits[1],out var m)&&int.TryParse(bits[2],out var d)&&MaterialAvailability.HasValidCalendarDate(y,m,d);
    }
}






