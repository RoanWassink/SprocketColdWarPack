using BepInEx;
using SprocketJsonEditor;
using System.Text.Json.Nodes;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.TechTrees;
namespace SprocketExpandedBallistics.Materials;
internal sealed class MaterialEditorSession:EditorSession
{
    private readonly MaterialEditorDraft draft;
    private readonly Action refresh;
    internal MaterialEditorDraft Draft=>draft;
    internal MaterialEditorSession(Action refresh)
    {
        this.refresh=refresh;
        using var stream=typeof(MaterialEditorSession).Assembly.GetManifestResourceStream("ExpandedBallistics.Materials.ResponseTemplates")!;
        using var reader=new StreamReader(stream);
        using var reactions=typeof(MaterialEditorSession).Assembly.GetManifestResourceStream("ExpandedBallistics.Materials.ReactionTemplates")!;
        using var reactionReader=new StreamReader(reactions);
        var playerMaterials=MaterialDatabase.MenuItems(MaterialDatabase.Materials,"");
        draft=new(TechTreeLoader.TechDirectory,Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.responses.json"),Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.materials.editor.json"),playerMaterials.Select(m=>(m.Id,MaterialDatabase.MenuLabel(m),m.SourceFile)),reader.ReadToEnd(),Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.material.reactions.json"),reactionReader.ReadToEnd());
        foreach(var item in draft.Items)
        {
            var material=playerMaterials.First(m=>m.Id==item!["id"]!.GetValue<string>());
            item!["label"]=MaterialDatabase.MenuLabel(material);
        }
    }
    public override string Title=>"Material editor";
    public override string ItemName=>"material";
    public override JsonArray Items=>draft.Items;
    public override JsonArray Templates=>draft.Templates;
    public override IEnumerable<string> Fields(int index)
    {
        foreach(var field in new[]{"behavior","label","date","rhaFactor","density","spallFactor","costMultiplier"})yield return field;
        if(Items[index]!["reaction"] is {} role)
        {
            yield return "effectsEnabled";
            foreach(var key in Scalars(role,"reaction"))
                if(key is not ("reaction.id" or "reaction.label" or "reaction.mode"))yield return key;
        }
        else if(Items[index]!["response"] is {} r)
        {
            yield return "effectsEnabled";
            foreach(var key in Scalars(r["calibration"]!,"response.calibration"))
                if(!draft.IsExperimental(Items[index]!["id"]!.GetValue<string>()) || key is not ("response.calibration.intactRodRetention" or "response.calibration.disturbedRodRetention" or "response.calibration.disturbanceContribution"))yield return key;
            foreach(var key in Scalars(r["geometry"]!,"response.geometry"))if(!draft.IsExperimental(Items[index]!["id"]!.GetValue<string>())||key!="response.geometry.mode")yield return key;
            if(Items[index]!["behavior"]!.GetValue<string>() is "lightEra" or "heavyEra")yield return "response.era.cellPitchM";
        }
    }
    private static IEnumerable<string> Scalars(JsonNode node,string path)
    {
        if(node is JsonObject o){foreach(var field in o)if(field.Value!=null)foreach(var s in Scalars(field.Value,path+"."+field.Key))yield return s;}
        else if(node is JsonArray a){for(int i=0;i<a.Count;i++)foreach(var s in Scalars(a[i]!,path+"."+i))yield return s;}
        else yield return path;
    }
    public override JsonNode Value(int index,string field)=>field=="effectsEnabled"?JsonValue.Create(draft.EffectsEnabledFor(index)?"true":"false")!:draft.Value(index,field);
    public override string Caption(int index,string field)
    {
        var last=field.Split('.').Last();
        var suffix=last switch {"lossPerReference"=>" (reference response calibration; 0–0.75)","referenceThicknessMm"=>" (mm; >0–1000)","maximumLoss"=>" (direct response cap; ignored by backed/sandwich; absorber 0–1)","minimumThicknessMm" or "maximumThicknessMm"=>" (mm; >0–1000; ordered)","minimumAngleDegrees" or "fullAngleDegrees" or "maximumAngleDegrees"=>" (degrees; 0–89.9; ordered)","minimumSteelThicknessMm"=>" (mm; >0–1000)","maximumGapMm"=>" (mm; half-response distance beyond 50 mm free gap)","degradedFraction"=>" (remaining local response; 0–1)","rodDisturbanceShare"=>" (material work reserved for next-plate disturbance; 0–1)","rodYawDegreesAtReference"=>" (calibrated rod tilt sensitivity; 0–45 degrees)","workErosionShare"=>" (core work spent on erosion; 0–1)","specificAbsorptionJPerKg"=>" (J/kg; 0–10000000)","rhaFactor"=>" (× RHA; >0)","density"=>" (kg/m\u00B3 >0; special materials 100–25000)","spallFactor"=>" (≥0; special materials 0–1)","costMultiplier"=>" (≥0; balanced minimum applies)","date"=>" (yyyy.MM.dd)","heatRetention"=>$" (remaining HEAT fraction; 0–1; effective extra loss capped at {draft.MaximumHeatLoss:0.###})","intactRodRetention" or "disturbedRodRetention"=>$" (remaining rod fraction; {draft.MinimumRodRetention:0.###}–1)","disturbanceContribution"=>" (0–0.35)","minNormalThicknessMm"=>" (mm; 0.1–1000)","maxNormalThicknessMm"=>" (mm; minimum thickness–1000)","minMeasuredGapMm"=>" (mm; 0–1000)","maxMeasuredGapMm"=>" (mm; minimum gap–1000)","angleDegrees"=>" (°; 0–89.9; ascending)","distanceMm"=>" (mm; 0–1000; ascending)","weight"=>" (0–1)","cellPitchM"=>" (m; 0.01–10)",_=>""};
        if(field.Contains(".threatTags."))return "Eligible threat tag "+(int.Parse(last)+1);
        if(field=="effectsEnabled")return "Special effects enabled (shared; requires the shell module)";
        var label=System.Text.RegularExpressions.Regex.Replace(last,"([a-z])([A-Z])","$1 $2");
        if(field.Contains("Curve")){var parts=field.Split('.');label=parts[^3]+" point "+(int.Parse(parts[^2])+1)+": "+label;}
        return char.ToUpperInvariant(label[0])+label.Substring(1)+suffix;
    }
    public override string[]? Choices(string field)=>field switch {"behavior"=>new[]{"passive","glassTextolite","nera","lightEra","heavyEra","passiveComposite"}.Concat(draft.ReactionChoices.Select(r=>"material:"+r.Id)).ToArray(),"effectsEnabled"=>new[]{"true","false"},"response.geometry.mode"=>new[]{"declaredCassette","resolvedLayers"},_=>null};
    public override string ChoiceLabel(string field,string value)=>field switch {"behavior"=>value.StartsWith("material:")?draft.ReactionChoices.First(r=>r.Id==value.Substring(9)).Label:MaterialEditorDraft.BehaviorLabel(value),"effectsEnabled"=>value=="true"?"Enabled":"Disabled",_=>value=="declaredCassette"?"Cassette thickness":"Resolved layers and measured gaps"};
    public override void Set(int index,string field,string value)=>draft.Set(index,field,value);
    public override void SetChoice(int index,string field,string value){if(field=="behavior")draft.Behavior(index,value);else draft.Set(index,field,value);}
    public override int Duplicate(JsonObject template)=>draft.Duplicate(template);
    public override void Save(){draft.Save();try{refresh();}catch(Exception ex){MaterialFeature.ModLog.LogError("[Material editor] Saved files; native refresh failed: "+ex);throw new IOException("Materials saved, but refresh failed: "+ex.GetBaseException().Message.Split('\n')[0],ex);}}
}






