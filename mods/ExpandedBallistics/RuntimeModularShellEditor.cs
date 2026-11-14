using BepInEx;
using System.Text.Json.Nodes;
using SprocketJsonEditor;
namespace SprocketExpandedBallistics;

internal static class RuntimeShellEditor
{
    internal static string? LastSaveMessage;
    internal static void Open(Action refresh,Action? redraw=null,Sprocket.Vehicles.Cannons.Cannon? cannon=null)=>TryOpen(refresh,false,redraw,cannon);
    internal static void Import(Action refresh,Action? redraw=null,Sprocket.Vehicles.Cannons.Cannon? cannon=null)=>TryOpen(refresh,true,redraw,cannon);
    private static void TryOpen(Action refresh,bool import,Action? redraw,Sprocket.Vehicles.Cannons.Cannon? cannon=null)
    {
        // Wave 2b: the UI Kit panel by default; the JSON Editor session stays as the fallback ([Shell editor] Use UI Kit = false).
        if(ShellEditorPanel.UseKit&&ShellEditorPanel.TryOpen(cannon,refresh,import,redraw))return;
        try{JsonEditor.Open(new ShellEditorSession(refresh,import));}
        catch(Exception ex)
        {
            LastSaveMessage=ex.Message;
            Plugin.ModLog.LogWarning("[Modular shells] "+ex.Message);
            try{redraw?.Invoke();}catch(Exception redrawError){Plugin.ModLog.LogWarning("[Modular shells] Cannot refresh error display: "+redrawError.Message);}
        }
    }
}
internal sealed class ShellEditorSession:EditorSession
{
    private readonly ModularShellDraft draft;
    private readonly Action refresh;
    private readonly string runtimePath=Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.shells.json");
    private readonly string authoringPath=Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.modules.json");
    internal ShellEditorSession(Action refresh,bool import)
    {
        this.refresh=refresh;
        var importPath=Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.modules.import.json");
        var runtime=File.ReadAllText(runtimePath);
        var authoring=File.Exists(authoringPath)?File.ReadAllText(authoringPath):null;
        var imported=import?(File.Exists(importPath)?File.ReadAllText(importPath):ModularShellDraft.ImportCurrent(runtime,authoring)):null;
        if(import)draft=new ModularShellDraft(runtime,authoring,imported);
        else
        {
            draft=ModularShellDraft.OpenCurrent(runtime,authoring,out var added);
            if(added)RuntimeShellEditor.LastSaveMessage="New shell profiles added to the editor draft. Existing designs are retained; files change only when you Save.";
        }
        if(import&&!File.Exists(importPath))
            RuntimeShellEditor.LastSaveMessage="Current shell JSON imported as a draft. Edited runtime profiles rebuilt; unchanged modular designs and draft-only IDs retained. Files change only on Save, with backups.";
    }
    public override string Title=>"Shell editor";
    public override string ItemName=>"shell";
    public override JsonArray Items=>draft.Profiles;
    public override JsonArray Templates=>new(ShellExampleProfiles.Templates()
        .Select(p=>(JsonNode)ModularShellDraft.FromLegacy(p!.AsObject())).ToArray());
    public override IEnumerable<string> Groups(int index)=>ModularShellDraft.GroupNames;
    public override IEnumerable<string> Fields(int index)
    {
        yield return "label";
        foreach(var key in ModularShellDraft.ModuleKeys)yield return "modules."+key+".kind";
        foreach(var field in ModularShellDraft.Fields.Where(f=>Active(index,f.Key)))yield return "modifiers."+field.Key;
        yield return "modifiers.minimumDate";
    }
    private bool Active(int index,string key)=>draft.Active(index,key);
    public override string FieldGroup(int index,string field)
    {
        if(field.StartsWith("modules.",StringComparison.Ordinal))return ModularShellDraft.GroupNames[Array.IndexOf(ModularShellDraft.ModuleKeys,field.Split('.')[1])];
        if(field=="modifiers.minimumDate")return "Carrier";
        return ModularShellDraft.Fields.FirstOrDefault(f=>"modifiers."+f.Key==field)?.Group??"";
    }
    public override string? GroupDescription(int index,string group)
    {
        var p=Items[index]!.AsObject();var behavior=ModularShellDraft.Behavior(p);
        var status=behavior==null?"Pending unknown module — live catalogue cannot change. ":"";
        return status+(group switch
        {
            "Tail"=>draft.Value(index,"modifiers.guidanceTransport").GetValue<string>()=="wire"
                ?"Visible wire from the fixed launch site; break stops steering only. Wire can also accompany unguided flight."
                :"Guided steers the projectile. Spin, fins and none use native motion without a new stability or drag bonus.",
            "Propulsion"=>"Choose ballistic launch or a rocket motor. Speed, delay and burn time determine the flight profile.",
            "Body"=>"Dimensions and density define the native penetrator proxy. A shaped-charge body alone does not add a HEAT jet.",
            "Effect"=>p["modules"]!["effect"]!["kind"]!.GetValue<string>() switch
            {
                "spall"=>"An internal fragment burst. Its burst size and cone currently follow the shared APHE settings.",
                "heat"=>"Set precursor penetration above zero for tandem HEAT. Both charge values use the reference calibre; zero precursor means a single charge.",
                _=>"The selected effect determines impact; rocket propulsion and guidance do not change it."
            },
            "Carrier"=>behavior==null?"The combination cannot be applied. Save keeps it as a draft; all live shells stay unchanged."
                :"Carrier chooses launch context and native technology requirements, without an additional stability bonus.",
            _=>""
        });
    }
    public override JsonNode Value(int index,string field)=>draft.Value(index,field);
    public override string Caption(int index,string field)
    {
        if(field=="label")return "Shell name — editable; saved ID stays unchanged";
        if(field.StartsWith("modules.",StringComparison.Ordinal))return "Module";
        var key=field[10..];
        if(key=="minimumDate")return "Additional availability date (optional, yyyy.MM.dd)";
        var setting=ModularShellDraft.Fields.Single(f=>f.Key==key);
        var words=System.Text.RegularExpressions.Regex.Replace(key,"([a-z])([A-Z])","$1 $2");
        if(key=="secondPlatePenetrationFactor")words="Minimum spaced-armour retention";
        if(key=="tandemPrecursorPenetrationMm")words="Precursor penetration (0 = single charge)";
        if(key=="chemicalPenetrationMm"&&Items[index]!["modules"]!["effect"]!["kind"]!.GetValue<string>()=="heat")words="Main-charge penetration";
        if(key=="motorBurnTime")words="Motor burn time (0 = unlimited)";
        if(key=="guidanceTransport")words="Command link / visible wire";
        if(key=="wireDisplayWidth")words="Cable display width (visual approximation)";
        return char.ToUpperInvariant(words[0])+words[1..]+(setting.Unit==""?"":" ("+setting.Unit+")");
    }
    public override EditorNumberRange? NumberRange(int index,string field)
    {
        var setting=ModularShellDraft.Fields.FirstOrDefault(f=>"modifiers."+f.Key==field);
        if(setting==null||setting.Choices!=null)return null;
        return new(setting.Min,setting.Key=="launchSpeed"?draft.Number(index,"flightSpeed"):setting.Max,setting.Step);
    }
    public override string[]? Choices(string field)
    {
        if(field.StartsWith("modules.",StringComparison.Ordinal))
        {
            var group=field.Split('.')[1];
            return ModularShellDraft.Options[group].Concat(Items.Select(p=>p!["modules"]![group]!["kind"]!.GetValue<string>())).Distinct().ToArray();
        }
        return ModularShellDraft.Fields.FirstOrDefault(f=>"modifiers."+f.Key==field)?.Choices;
    }
    public override string ChoiceLabel(string field,string value)=>value switch
    {
        "spin"=>"Spin / native architecture", "fins"=>"Fins / native architecture", "guided"=>"Guidance module",
        "none"=>"None / unguided", "ballistic"=>"Ballistic launch", "rocket"=>"Rocket motor",
        "fullBore"=>"Full-bore", "longRod"=>"Long rod (current approximation)", "shapedCharge"=>"Shaped-charge body",
        "kinetic"=>"Kinetic penetration", "spall"=>"Internal APHE burst", "blast"=>"Native HE blast", "heat"=>"HEAT impact", "hesh"=>"HESH spall proxy",
        "sabot"=>"Sabot", "launcher"=>"Dedicated launcher", "gunLaunch"=>"Gun launch",
        "sight"=>"Sight guidance (SACLOS)", "keyboard"=>"Manual guidance (MCLOS)", "topattack"=>"Top-attack (lock, then steep dive)", "fixed"=>"Fixed launch speed", "cannon"=>"Cannon-dependent launch speed",
        "current"=>"Current link (no cable)","wire"=>"Wire guided / visible cable",
        _=>value+" (pending)"
    };
    public override void Set(int index,string field,string value)=>draft.Set(index,field,value);
    public override int Duplicate(JsonObject template)=>draft.Duplicate(template);
    public override void Save()
    {
        var compilation=draft.Save(runtimePath,authoringPath);
        if(compilation.Ready)refresh();
        RuntimeShellEditor.LastSaveMessage=compilation.Ready
            ?"Saved modular designs and compiled shells; live catalogue refreshed."
            :"Saved modular draft only: "+compilation.Pending.Count+" pending design(s). All live shells remain unchanged.";
        Plugin.ModLog.LogInfo("[Modular shells] "+RuntimeShellEditor.LastSaveMessage);
    }
}

