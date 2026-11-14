using System.Text.Json;
namespace SprocketExpandedBallistics;

// The mechanism is recipe-driven; material IDs only bind native passive assets.
internal sealed record MaterialReactionRecipe(string Id,string Label,string Mode,string[] ThreatTags,
    double LossPerReference,double ReferenceThicknessMm,double MaximumLoss,double MinimumThicknessMm,
    double MaximumThicknessMm,double MinimumAngleDegrees,double FullAngleDegrees,double MaximumAngleDegrees,
    double MinimumSteelThicknessMm,double MaximumGapMm,double CellPitchM,double DegradedFraction,
    double SpecificAbsorptionJPerKg,double? WorkErosionShare=null,double RodDisturbanceShare=0,double RodYawDegreesAtReference=0,
    IReadOnlyDictionary<string,double>? ThreatScale=null);
internal sealed record MaterialWorkCalibration(double RodDensityKgM3=17500,double RodLengthM=.6,double RodSpeedMps=1600,
    double JetBudgetMm=600,double InterfaceWeight=.5,double SteelErosionShare=.75,double OtherErosionShare=.30,
    double DirectErosionShare=.75,double BackedErosionShare=.65,double SandwichErosionShare=.15);
internal sealed record MaterialReactionBinding(string MaterialId,string RecipeId,double Density,double RhaFactor,double SpallFactor);
internal sealed record MaterialReactionCatalogue(bool Enabled,double MaximumInteractionLoss,string[] SteelMaterialIds,
    IReadOnlyDictionary<string,MaterialReactionRecipe> Recipes,IReadOnlyDictionary<string,MaterialReactionBinding> Bindings,
    MaterialWorkCalibration? WorkCalibration=null)
{
    private static readonly MaterialWorkCalibration DefaultWork=new();
    internal MaterialWorkCalibration Work=>WorkCalibration??DefaultWork;
    internal const string FileName="sprocket.expandedballistics.material.reactions.json";
    internal static MaterialReactionCatalogue Parse(string json)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        ObjectOptional(root,new[]{"schemaVersion","enabled","maximumInteractionLoss","steelMaterialIds","recipes","bindings"},"workCalibration");
        if(root.GetProperty("schemaVersion").GetInt32()!=1)throw new FormatException("Unsupported material reaction schema.");
        var cap=Number(root,"maximumInteractionLoss",0,.75);
        var steel=Strings(root.GetProperty("steelMaterialIds"));
        var recipes=new Dictionary<string,MaterialReactionRecipe>(StringComparer.Ordinal);
        foreach(var r in root.GetProperty("recipes").EnumerateArray())
        {
            ObjectOptional(r,new[]{"id","label","mode","threatTags","lossPerReference","referenceThicknessMm","maximumLoss","minimumThicknessMm","maximumThicknessMm","minimumAngleDegrees","fullAngleDegrees","maximumAngleDegrees","minimumSteelThicknessMm","maximumGapMm","cellPitchM","degradedFraction","specificAbsorptionJPerKg"},"workErosionShare","rodDisturbanceShare","rodYawDegreesAtReference","threatScale");
            var mode=Text(r,"mode");if(mode is not ("direct" or "backed" or "sandwich" or "fragmentAbsorber"))throw new FormatException("Unknown material reaction mechanism.");
            var threatScale=r.TryGetProperty("threatScale",out var scaleNode)?Scales(scaleNode,mode):null;
            var recipe=new MaterialReactionRecipe(Text(r,"id"),Text(r,"label"),mode,Strings(r.GetProperty("threatTags")),
                Number(r,"lossPerReference",0,.75),Number(r,"referenceThicknessMm",.01,1000),Number(r,"maximumLoss",0,1),
                Number(r,"minimumThicknessMm",.01,1000),Number(r,"maximumThicknessMm",.01,1000),
                Number(r,"minimumAngleDegrees",0,89.9),Number(r,"fullAngleDegrees",0,89.9),Number(r,"maximumAngleDegrees",0,89.9),
                Number(r,"minimumSteelThicknessMm",.01,1000),Number(r,"maximumGapMm",0,1000),
                Number(r,"cellPitchM",.01,10),Number(r,"degradedFraction",0,1),Number(r,"specificAbsorptionJPerKg",0,1e8),
                r.TryGetProperty("workErosionShare",out _)?Number(r,"workErosionShare",0,1):null,
                r.TryGetProperty("rodDisturbanceShare",out _)?Number(r,"rodDisturbanceShare",0,1):0,
                r.TryGetProperty("rodYawDegreesAtReference",out _)?Number(r,"rodYawDegreesAtReference",0,45):0,threatScale);
            if(recipe.MinimumThicknessMm>recipe.MaximumThicknessMm||recipe.MinimumAngleDegrees>recipe.FullAngleDegrees||recipe.FullAngleDegrees>recipe.MaximumAngleDegrees)
                throw new FormatException("Material reaction range is inverted.");
            if(mode!="fragmentAbsorber"&&recipe.MaximumLoss>.75)throw new FormatException("Original-impact material loss exceeds supported range.");
            if(mode=="fragmentAbsorber"&&(recipe.ThreatTags.Length!=1||recipe.ThreatTags[0]!="fragment"||recipe.SpecificAbsorptionJPerKg<=0))
                throw new FormatException("Fragment absorber requires fragment-only energy absorption.");
            if(!recipes.TryAdd(recipe.Id,recipe))throw new FormatException("Duplicate reaction recipe.");
        }
        var bindings=new Dictionary<string,MaterialReactionBinding>(StringComparer.Ordinal);
        foreach(var b in root.GetProperty("bindings").EnumerateArray())
        {
            Object(b,"materialId","recipeId","density","rhaFactor","spallFactor");
            var binding=new MaterialReactionBinding(Text(b,"materialId"),Text(b,"recipeId"),Number(b,"density",1,30000),Number(b,"rhaFactor",.00001,20),Number(b,"spallFactor",0,10));
            if(!recipes.ContainsKey(binding.RecipeId)||binding.MaterialId is "rha" or "sheetMetal"||!bindings.TryAdd(binding.MaterialId,binding))
                throw new FormatException("Invalid/duplicate material reaction binding.");
        }
        if(recipes.Count>128||bindings.Count>512)throw new FormatException("Material reaction catalogue is too large.");
        MaterialWorkCalibration? work=null;
        if(root.TryGetProperty("workCalibration",out var w))
        {
            Object(w,"rodDensityKgM3","rodLengthM","rodSpeedMps","jetBudgetMm","interfaceWeight","steelErosionShare","otherErosionShare","directErosionShare","backedErosionShare","sandwichErosionShare");
            work=new(Number(w,"rodDensityKgM3",1000,30000),Number(w,"rodLengthM",.01,10),Number(w,"rodSpeedMps",100,5000),
                Number(w,"jetBudgetMm",1,10000),Number(w,"interfaceWeight",0,1),Number(w,"steelErosionShare",0,1),
                Number(w,"otherErosionShare",0,1),Number(w,"directErosionShare",0,1),Number(w,"backedErosionShare",0,1),Number(w,"sandwichErosionShare",0,1));
        }
        return new(root.GetProperty("enabled").GetBoolean(),cap,steel,recipes,bindings,work);
    }
    private static void ObjectOptional(JsonElement node,string[] required,params string[] optional)
    {
        if(node.ValueKind!=JsonValueKind.Object)throw new FormatException("Expected material reaction object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in node.EnumerateObject())if(!required.Contains(p.Name,StringComparer.Ordinal)&&!optional.Contains(p.Name,StringComparer.Ordinal)||!seen.Add(p.Name))throw new FormatException("Unknown/duplicate material reaction field: "+p.Name);
        if(required.Any(f=>!seen.Contains(f)))throw new FormatException("Missing material reaction field.");
    }
    private static void Object(JsonElement node,params string[] fields)
    {
        if(node.ValueKind!=JsonValueKind.Object)throw new FormatException("Expected material reaction object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in node.EnumerateObject())if(!fields.Contains(property.Name,StringComparer.Ordinal)||!seen.Add(property.Name))throw new FormatException("Unknown/duplicate material reaction field: "+property.Name);
        if(fields.Any(f=>!seen.Contains(f)))throw new FormatException("Missing material reaction field.");
    }
    private static string Text(JsonElement node,string field)
    {
        var text=node.GetProperty(field).GetString();
        if(string.IsNullOrWhiteSpace(text)||text.Length>80||text.Any(char.IsControl))throw new FormatException("Invalid material reaction text.");return text;
    }
    private static double Number(JsonElement node,string field,double min,double max)
    {var value=node.GetProperty(field).GetDouble();if(!double.IsFinite(value)||value<min||value>max)throw new FormatException("Invalid material reaction range: "+field);return value;}
    private static string[] Strings(JsonElement node)
    {
        if(node.ValueKind!=JsonValueKind.Array)throw new FormatException("Expected material reaction list.");
        var values=node.EnumerateArray().Select(v=>v.GetString()??"").ToArray();
        if(values.Length==0||values.Length>64||values.Any(v=>string.IsNullOrWhiteSpace(v)||v.Length>80||v.Any(char.IsControl))||values.Distinct(StringComparer.Ordinal).Count()!=values.Length)throw new FormatException("Invalid material reaction list.");return values;
    }
    // Optional per-threat multiplier. Absent object: legacy threatTags behaviour.
    // Present object: only the listed threat buckets react at all.
    private static IReadOnlyDictionary<string,double>? Scales(JsonElement node,string mode)
    {
        if(node.ValueKind!=JsonValueKind.Object)throw new FormatException("threatScale must be an object.");
        if(mode=="fragmentAbsorber")throw new FormatException("Fragment absorbers cannot declare threatScale.");
        var scales=new Dictionary<string,double>(StringComparer.Ordinal);
        foreach(var p in node.EnumerateObject())
        {
            if(p.Name is not ("jet" or "longRod" or "shortRod" or "kineticFullBore")||scales.ContainsKey(p.Name))
                throw new FormatException("Unknown or duplicate threatScale bucket: "+p.Name);
            if(p.Value.ValueKind!=JsonValueKind.Number||!p.Value.TryGetDouble(out var value)||!double.IsFinite(value)||value<0||value>1)
                throw new FormatException("threatScale is outside 0-1: "+p.Name);
            scales[p.Name]=value;
        }
        return scales;
    }
}
