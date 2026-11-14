using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SprocketJsonEditor;
namespace SprocketExpandedBallistics;

/// <summary>Cannon numbers for the live column. The delegates wrap native calls at runtime; tests pass fakes.</summary>
internal sealed record ShellPreviewCannon(double CalibreMm,double MuzzleVelocity,ushort PenetratorConstant,double PropellantLengthMm,
    Func<DartBallistics,double>? Penetration=null,Func<ShellProfile,double>? ChemicalBudget=null,string? VehicleEra=null);
internal enum ShellIssueKind{Error,Warning,Info}
internal sealed record ShellIssue(string Text,ShellIssueKind Kind,string? Group=null,string? Field=null);
internal sealed record ShellRowView(string Id,string Title,string Subtitle,string Tag,string? State,int Errors,bool Draft,string Chain="",string Reason="",bool Unavailable=false,DateTime? Date=null);
/// <summary>Availability of a saved shell on the opening cannon's vehicle (from <c>ShellEditorAvailability.For</c>); Date only sorts the list.</summary>
internal readonly record struct ShellAvailability(bool Available,string Reason,DateTime? Date);
internal sealed record ShellSaveResult(bool Ready,int Shells,int Pending,string Message);

/// <summary>
/// View model of the Kit shell editor (wave 2b). Pure: no Unity, no Kit. It wraps the existing
/// <see cref="ModularShellDraft"/> (fields, ranges, defaults, visibility and compile stay there) and adds
/// per-field checks while typing, hidden-field exclusion, the era picker, delete/move of unsaved drafts,
/// dirty tracking and the transactional save. Every getter the panel polls per frame reads a cache that
/// is rebuilt only after a change (<see cref="Version"/>).
/// </summary>
internal sealed class ShellEditorModel
{
    internal static readonly IReadOnlyDictionary<string,ModularShellDraft.Field> FieldsByKey=ModularShellDraft.Fields.ToDictionary(f=>f.Key,StringComparer.Ordinal);
    /// <summary>MSD groups first, then any group a future field introduces.</summary>
    internal static readonly IReadOnlyList<string> Groups=ModularShellDraft.GroupNames.Concat(ModularShellDraft.Fields.Select(f=>f.Group)).Distinct(StringComparer.Ordinal).ToArray();
    internal static string? ModuleKey(string group){var i=Array.IndexOf(ModularShellDraft.GroupNames,group);return i>=0&&i<ModularShellDraft.ModuleKeys.Length?ModularShellDraft.ModuleKeys[i]:null;}
    internal static IEnumerable<ModularShellDraft.Field> FieldsIn(string group)=>ModularShellDraft.Fields.Where(f=>f.Group==group);
    internal const string EraKey="minimumDate",LabelKey="label";
    internal const double LiveDebounceSeconds=.15;

    private sealed class ShellState
    {
        internal int Stamp=-1;internal string Id="",Label="",Json="";internal string? Behavior;
        internal readonly Dictionary<string,bool> Active=new(StringComparer.Ordinal);
        internal readonly Dictionary<string,double> Numbers=new(StringComparer.Ordinal);
        internal readonly Dictionary<string,string> Choices=new(StringComparer.Ordinal);
        internal readonly Dictionary<string,string> Texts=new(StringComparer.Ordinal);
        internal readonly Dictionary<string,string> Errors=new(StringComparer.Ordinal);
        internal readonly Dictionary<string,string> Modules=new(StringComparer.Ordinal);
        internal readonly Dictionary<string,(string[] Values,string[] Labels,int Index)> ModuleOptions=new(StringComparer.Ordinal);
        internal readonly Dictionary<string,int> ChoiceIndex=new(StringComparer.Ordinal);
        internal readonly Dictionary<string,string> Descriptions=new(StringComparer.Ordinal);
        internal string MinimumDate="";internal IReadOnlyList<(string Label,string? Group,string Value)> EraOptions=Array.Empty<(string,string?,string)>();
        internal int EraIndex;internal string EraAvailability="";
        internal string Subtitle="",Chain="";
    }

    private string runtimeJson;private string? authoringJson;
    private readonly string runtimePath,authoringPath;
    private HashSet<string> runtimeIds=new(StringComparer.Ordinal);
    private List<(string Id,string Json)> snapshot=new();
    private bool unsavedImport;
    private readonly Dictionary<(string Id,string Key),(string Text,string Message)> pending=new();
    private readonly Dictionary<string,int> stamps=new(StringComparer.Ordinal);
    private readonly Dictionary<string,ShellState> states=new(StringComparer.Ordinal);
    private readonly Func<double> clock;
    private int selected=-1;
    internal ModularShellDraft Draft{get;private set;}
    internal IReadOnlyList<ShellEra> Eras{get;}
    /// <summary>Cannon of the inspector that opened the editor (numbers and the vehicle era); null = none.</summary>
    internal ShellPreviewCannon? Cannon{get=>cannon;set{cannon=value;states.Clear();liveKey=(-1,-1);Version++;}}
    /// <summary>
    /// Saved-shell availability on the opening cannon's vehicle (runtime: <c>ShellEditorAvailability.For</c>);
    /// null = no vehicle context: nothing is dimmed. Asked once per shell per catalogue/context, never per frame.
    /// </summary>
    internal Func<string,ShellAvailability?>? Availability{get=>availability;set{availability=value;known.Clear();Version++;}}
    private Func<string,ShellAvailability?>? availability;
    private readonly Dictionary<string,ShellAvailability?> known=new(StringComparer.Ordinal);
    private ShellAvailability? AvailabilityOf(string id)
    {
        if(availability==null||!runtimeIds.Contains(id))return null; // drafts are not in the live catalogue yet
        if(!known.TryGetValue(id,out var a)){try{a=availability(id);}catch(Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException){a=null;}known[id]=a;}
        return a;
    }
    /// <summary>Display-only sort of the list by availability date (draft order and IDs never change).</summary>
    internal bool SortByDate{get=>sortByDate;set{if(sortByDate==value)return;sortByDate=value;filteredKey=(-1,"");}}
    private bool sortByDate;
    private ShellPreviewCannon? cannon;
    /// <summary>Raised once per change; the panel uses it for nothing per frame.</summary>
    internal int Version{get;private set;}
    internal double LastChange{get;private set;}
    internal string? Notice{get;set;}

    internal ShellEditorModel(string runtimeJson,string? authoringJson,ModularShellDraft draft,string runtimePath,string authoringPath,IReadOnlyList<ShellEra>? eras=null,Func<double>? clock=null,bool unsavedImport=false)
    {
        this.runtimeJson=runtimeJson;this.authoringJson=authoringJson;Draft=draft;
        this.runtimePath=runtimePath;this.authoringPath=authoringPath;
        Eras=eras??ShellEraCatalogue.BuiltIn;
        this.clock=clock??(()=>Environment.TickCount64/1000d);
        this.unsavedImport=unsavedImport;
        Baseline();
        selected=Draft.Profiles.Count>0?0:-1;
        LastChange=double.NegativeInfinity;
    }
    /// <summary>Reads shells.json (required) and modules.json (optional), like the JSON Editor session did.</summary>
    internal static ShellEditorModel Open(string runtimePath,string authoringPath,string importPath,bool import,IReadOnlyList<ShellEra>? eras=null,Func<double>? clock=null)
    {
        var runtime=File.ReadAllText(runtimePath);
        var authoring=File.Exists(authoringPath)?File.ReadAllText(authoringPath):null;
        string? notice=null;ModularShellDraft draft;bool unsaved=false;
        if(import)
        {
            var file=File.Exists(importPath);
            draft=new ModularShellDraft(runtime,authoring,file?File.ReadAllText(importPath):ModularShellDraft.ImportCurrent(runtime,authoring));
            notice=file?"Imported modules.import.json as a draft. Files change only when you Save, with backups."
                :"Current shell JSON imported as a draft: edited runtime profiles rebuilt, unchanged designs and draft-only IDs kept. Files change only when you Save.";
            unsaved=true;
        }
        else
        {
            draft=ModularShellDraft.OpenCurrent(runtime,authoring,out var added);
            if(added){notice="New shell profiles were added to the editor draft. Existing designs are kept; files change only when you Save.";unsaved=true;}
        }
        return new(runtime,authoring,draft,runtimePath,authoringPath,eras,clock,unsaved){Notice=notice};
    }
    /// <summary>True for the "catalogue changed outside the editor" refusal of <see cref="ModularShellDraft.OpenCurrent"/> (P14).</summary>
    internal static bool IsConflict(Exception ex)=>ex is IOException&&ex.Message.StartsWith("The runtime shell catalogue changed",StringComparison.Ordinal);

    private void Baseline()
    {
        runtimeIds=ShellProfiles.Parse(runtimeJson).Select(p=>p.Id).ToHashSet(StringComparer.Ordinal);
        snapshot=Draft.Profiles.Select(p=>(p!["id"]!.GetValue<string>(),p.ToJsonString())).ToList();
        pending.Clear();states.Clear();stamps.Clear();known.Clear();liveKey=(-1,-1);
        Changed(null);
    }

    // ---------------- selection ----------------
    internal int Count=>Draft.Profiles.Count;
    internal int Selected=>selected>=0&&selected<Count?selected:-1;
    /// <summary>Cached per version: JsonNode string reads allocate, and the list polls this per row per frame.</summary>
    internal string? SelectedId{get{Header();return header.Id;}}
    /// <summary>"custom_3 · draft" next to the section heading.</summary>
    internal string IdNote{get{Header();return header.Note;}}
    internal string BehaviourLine{get{Header();return header.Line;}}
    private (int Version,int Selected,string? Id,string Note,string Line) header=(-1,-2,null,"","");
    private void Header()
    {
        if(header.Version==Version&&header.Selected==selected)return;
        string? id=Selected<0?null:IdAt(Selected);
        var saved=Selected>=0&&IsSaved(Selected);
        var row=Selected>=0?Rows[Selected]:null;
        var era=row==null||row.Reason.Length==0?"":row.Unavailable?" · not available on this vehicle ("+row.Reason+")":" · "+row.Reason;
        header=(Version,selected,id,id==null?"":id+(saved?" · saved":" · draft"),
            id==null?"":$"{ShellEditorText.Behaviour(State(Selected).Behavior)}: {row?.Chain}{era} · "+(saved?"saved ID: vehicles use it.":"draft: not live until you save."));
    }
    internal string IdAt(int index)=>Draft.Profiles[index]!["id"]!.GetValue<string>();
    internal int IndexOf(string id){for(int i=0;i<Count;i++)if(IdAt(i)==id)return i;return -1;}
    internal void Select(int index){if(index<-1||index>=Count||index==selected)return;selected=index;Version++;}
    internal void Select(string id)=>Select(IndexOf(id));
    internal bool CanAdd=>Count<ShellProfiles.MaximumProfiles;
    /// <summary>"6 / 32", cached (a section note getter runs every frame).</summary>
    internal string CountText{get{if(countText.Version!=Version)countText=(Version,$"{Count} / {ShellProfiles.MaximumProfiles}");return countText.Text;}}
    private (int Version,string Text) countText=(-1,"");
    internal string IssueCountText{get{var list=Issues;if(!ReferenceEquals(list,issueCountFor)){issueCountFor=list;issueCountText=list.Count.ToString(CultureInfo.InvariantCulture);}return issueCountText;}}
    private object? issueCountFor;private string issueCountText="0";
    /// <summary>Read from the cached rows (button EnabledWhen getters poll this every frame).</summary>
    internal bool IsSaved(int index)=>index>=0&&index<Count&&!Rows[index].Draft;

    private void Changed(string? id)
    {
        Version++;LastChange=clock();
        if(id!=null)stamps[id]=Version;
    }

    // ---------------- per-shell cache ----------------
    private ShellState State(int index)
    {
        var id=IdAt(index);
        stamps.TryGetValue(id,out var stamp);
        if(states.TryGetValue(id,out var cached)&&cached.Stamp==stamp)return cached;
        var s=new ShellState{Stamp=stamp,Id=id};
        var p=Draft.Profiles[index]!.AsObject();
        s.Label=p["label"]?.GetValue<string>()??"";s.Json=p.ToJsonString();
        s.Behavior=Safe(()=>ModularShellDraft.Behavior(p),null);
        foreach(var key in ModularShellDraft.ModuleKeys)s.Modules[key]=Safe(()=>p["modules"]![key]!["kind"]!.GetValue<string>(),"")!;
        foreach(var f in ModularShellDraft.Fields)
        {
            s.Active[f.Key]=Safe(()=>Draft.Active(index,f.Key),false);
            if(f.Choices!=null)
            {
                var v=Safe(()=>Draft.Value(index,"modifiers."+f.Key).GetValue<string>(),"")!;
                s.Choices[f.Key]=v;s.ChoiceIndex[f.Key]=Array.IndexOf(f.Choices,v);
            }
            else
            {
                var v=Safe(()=>Draft.Value(index,"modifiers."+f.Key).GetValue<double>(),double.NaN);
                s.Numbers[f.Key]=v;s.Texts[f.Key]=ShellEditorText.Number(v,f.Step);
            }
        }
        s.MinimumDate=Safe(()=>Draft.Value(index,"modifiers."+EraKey).GetValue<string>(),"")!;
        // Invalid typed text stays visible and selectable as the custom option
        // until it is corrected; it is never written to the draft.
        if(pending.TryGetValue((s.Id,EraKey),out var typedDate))s.MinimumDate=typedDate.Text;
        s.EraOptions=ShellEraCatalogue.Options(Eras,s.MinimumDate);s.EraIndex=ShellEraCatalogue.OptionIndex(Eras,s.MinimumDate);
        s.EraAvailability=ShellEraCatalogue.Availability(Eras,s.MinimumDate,Cannon?.VehicleEra);
        foreach(var group in Groups)
        {
            if(ModuleKey(group) is not {} key)continue;
            var kind=s.Modules[key];var known=ModularShellDraft.Options[key];
            var values=known.Contains(kind)?known:known.Append(kind).ToArray();
            s.ModuleOptions[group]=(values,values.Select(v=>ShellEditorText.Module(key,v)).ToArray(),Array.IndexOf(values,kind));
        }
        var transport=s.Choices.GetValueOrDefault("guidanceTransport","current");
        foreach(var group in Groups)s.Descriptions[group]=ShellEditorText.GroupDescription(group,s.Behavior,transport,s.Modules.GetValueOrDefault("effect",""));
        s.Chain=string.Join(" · ",ModularShellDraft.ModuleKeys.Select(k=>ShellEditorText.Module(k,s.Modules[k])));
        s.Subtitle=ShellEditorText.ShortChain(s.Modules);
        Rules(s);
        states[id]=s;
        return s;
    }
    private static T Safe<T>(Func<T> get,T fallback){try{return get();}catch(Exception ex) when (ex is InvalidOperationException or FormatException or NullReferenceException or KeyNotFoundException or ArgumentException or JsonException){return fallback;}}
    private double MaxFor(ShellState s,ModularShellDraft.Field f)=>f.Key=="launchSpeed"&&s.Numbers.TryGetValue("flightSpeed",out var fs)&&double.IsFinite(fs)?Math.Min(f.Max,fs):f.Max;

    /// <summary>Checks on visible fields only (P3, P9). These match the runtime validators exactly; the full compile check follows in the live column.</summary>
    private void Rules(ShellState s)
    {
        foreach(var f in ModularShellDraft.Fields)
        {
            if(!s.Active[f.Key]||f.Choices!=null)continue;
            var v=s.Numbers[f.Key];var max=MaxFor(s,f);
            if(!double.IsFinite(v))s.Errors[f.Key]="Enter a number.";
            else if(f.Key=="launchSpeed"&&v>max&&v<=f.Max)s.Errors[f.Key]=$"At most the flight speed ({ShellEditorText.Number(max,f.Step)} m/s).";
            else if(v<f.Min||v>max)s.Errors[f.Key]=ShellEditorText.Range(f.Min,max,f.Step,ShellEditorText.Unit(f));
        }
        bool Visible(string key)=>s.Active.TryGetValue(key,out var a)&&a;
        double N(string key)=>s.Numbers.TryGetValue(key,out var v)?v:double.NaN;
        if(Visible("chemicalPenetrationMm")&&s.Behavior is "heat" or "hesh" or "atgm" or "atgm_gun"&&!(N("chemicalPenetrationMm")>0))
            s.Errors.TryAdd("chemicalPenetrationMm",$"Must be above 0 for {ShellEditorText.Behaviour(s.Behavior)}.");
        if(Visible("nativeExplosivePower")&&s.Behavior=="he"&&!(N("nativeExplosivePower")>0))
            s.Errors.TryAdd("nativeExplosivePower","Must be above 0 for HE.");
        if(s.Modules.GetValueOrDefault("tail")=="guided"&&s.Choices.GetValueOrDefault("guidanceMode")=="topattack"&&s.Choices.GetValueOrDefault("guidanceTransport")=="wire")
            s.Errors.TryAdd("guidanceTransport","Top attack is fire-and-forget: use the radio / laser link, not wire.");
        if(Visible("shortRangeGuidanceDelay")&&Visible("maximumFlightTime")&&N("shortRangeGuidanceDelay")>=N("maximumFlightTime"))
            s.Errors.TryAdd("shortRangeGuidanceDelay",$"Must be below the max flight time ({ShellEditorText.Number(N("maximumFlightTime"),.1)} s).");
        if(ShellEraCatalogue.Error(Eras,s.MinimumDate) is {} date)s.Errors[EraKey]=date;
    }

    // ---------------- field getters (selected shell, cached) ----------------
    /// <summary>Selected shell's state, cached per version so ~70 field getters per frame do no JSON reads.</summary>
    private ShellState? Sel
    {
        get
        {
            if(selCache.Version==Version&&selCache.Index==selected)return selCache.State;
            selCache=(Version,selected,Selected<0?null:State(Selected));
            return selCache.State;
        }
    }
    private (int Version,int Index,ShellState? State) selCache=(-1,-2,null);
    internal bool Active(string key)=>key is EraKey or LabelKey?Sel!=null:Sel?.Active.GetValueOrDefault(key)==true;
    internal double Number(string key)=>Sel?.Numbers.GetValueOrDefault(key,double.NaN)??double.NaN;
    internal double Max(string key)=>Sel is {} s&&FieldsByKey.TryGetValue(key,out var f)?MaxFor(s,f):FieldsByKey.TryGetValue(key,out var g)?g.Max:1;
    internal string FieldText(string key)
    {
        var s=Sel;if(s==null)return "";
        if(pending.TryGetValue((s.Id,key),out var p))return p.Text;
        if(key==EraKey)return s.MinimumDate;
        return key==LabelKey?s.Label:s.Texts.GetValueOrDefault(key,"");
    }
    internal string Choice(string key)=>Sel?.Choices.GetValueOrDefault(key,"")??"";
    internal int ChoiceIndex(string key)=>Sel?.ChoiceIndex.GetValueOrDefault(key,-1)??-1;
    internal string Label=>Sel?.Label??"";
    internal string? Behavior=>Sel?.Behavior;
    internal string Description(string group)=>Sel?.Descriptions.GetValueOrDefault(group,"")??"";
    internal IReadOnlyList<string> ModuleLabels(string group)=>Sel is {} s&&s.ModuleOptions.TryGetValue(group,out var o)?o.Labels:Array.Empty<string>();
    internal int ModuleIndex(string group)=>Sel is {} s&&s.ModuleOptions.TryGetValue(group,out var o)?o.Index:-1;
    internal IReadOnlyList<(string Label,string? Group,string Value)> EraOptions=>Sel?.EraOptions??Array.Empty<(string,string?,string)>();
    internal int EraIndex=>Sel?.EraIndex??0;
    internal string EraAvailability=>Sel?.EraAvailability??"";
    /// <summary>Error for a visible field of the selected shell: typed text first, then the rules.</summary>
    internal string? FieldError(string key)
    {
        var s=Sel;if(s==null||!Active(key))return null;
        if(pending.TryGetValue((s.Id,key),out var p))return p.Message;
        if(key==LabelKey)return LabelError(Selected);
        return s.Errors.TryGetValue(key,out var e)?e:null;
    }

    // ---------------- edits ----------------
    private string Profile(int index)=>IdAt(index);
    /// <summary>Typing in a numeric field (every keystroke): a valid value applies at once, an invalid one stays as text with an error.</summary>
    internal void EditText(string key,string text)
    {
        if(key==LabelKey){SetLabel(text);return;}
        if(key==EraKey){SetMinimumDate(text);return;}
        var s=Sel;if(s==null||!FieldsByKey.TryGetValue(key,out var f)||f.Choices!=null)return;
        var t=(text??"").Trim();
        string? error=null;double v=double.NaN;
        if(!double.TryParse(t.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out v)||!double.IsFinite(v))error="Enter a number.";
        else if(v<f.Min||v>MaxFor(s,f))error=f.Key=="launchSpeed"&&v>MaxFor(s,f)&&v<=f.Max?$"At most the flight speed ({ShellEditorText.Number(MaxFor(s,f),f.Step)} m/s).":ShellEditorText.Range(f.Min,MaxFor(s,f),f.Step,ShellEditorText.Unit(f));
        if(error!=null){pending[(s.Id,key)]=(text??"",error);Changed(null);return;}
        var had=pending.Remove((s.Id,key));
        if(v==s.Numbers.GetValueOrDefault(key)){if(had)Changed(null);return;}
        Apply(key,v,f);
    }
    /// <summary>Slider drag/wheel: already snapped by the kit; rounded here to the step decimals.</summary>
    internal void SetNumber(string key,double value)
    {
        var s=Sel;if(s==null||!FieldsByKey.TryGetValue(key,out var f)||f.Choices!=null||!double.IsFinite(value))return;
        value=Math.Clamp(Math.Round(value,ShellEditorText.Decimals(f.Step)),f.Min,Math.Max(f.Min,MaxFor(s,f)));
        var had=pending.Remove((s.Id,key));
        if(value==s.Numbers.GetValueOrDefault(key)){if(had)Changed(null);return;}
        Apply(key,value,f);
    }
    private void Apply(string key,double value,ModularShellDraft.Field f)
    {
        var index=Selected;
        try{Draft.Set(index,"modifiers."+key,value.ToString("R",CultureInfo.InvariantCulture));}
        catch(FormatException ex){pending[(IdAt(index),key)]=(ShellEditorText.Number(value,f.Step),ShellEditorText.Humane(ex.Message,IdAt(index),Label));Changed(null);return;}
        Normalize(index);
        Changed(Profile(index));
    }
    internal void SetChoice(string key,string value)
    {
        var s=Sel;if(s==null||!FieldsByKey.TryGetValue(key,out var f)||f.Choices==null||!f.Choices.Contains(value)||s.Choices.GetValueOrDefault(key)==value)return;
        var index=Selected;Draft.Set(index,"modifiers."+key,value);Normalize(index);Changed(Profile(index));
    }
    internal void SetChoiceIndex(string key,int option){if(FieldsByKey.TryGetValue(key,out var f)&&f.Choices!=null&&option>=0&&option<f.Choices.Length)SetChoice(key,f.Choices[option]);}
    /// <summary>Module change: sets the model only; visibility getters do the rest (no rebuild, P12).</summary>
    internal void SetModule(string group,int option)
    {
        var s=Sel;if(s==null||ModuleKey(group) is not {} key||!s.ModuleOptions.TryGetValue(group,out var o)||option<0||option>=o.Values.Length)return;
        if(s.Modules[key]==o.Values[option])return;
        var index=Selected;Draft.Set(index,"modules."+key,o.Values[option]);Normalize(index);Changed(Profile(index));
    }
    /// <summary>Typing the optional minimum date; a blank value removes the restriction.</summary>
    internal void SetMinimumDate(string? text)
    {
        var s=Sel;if(s==null)return;
        var value=(text??"").Trim();
        if(ShellEraCatalogue.Error(Eras,value) is {} error){pending[(s.Id,EraKey)]=(text??"",error);Changed(s.Id);return;}
        var had=pending.Remove((s.Id,EraKey));
        if(value==s.MinimumDate){if(had)Changed(null);return;}
        var index=Selected;Draft.Set(index,"modifiers."+EraKey,value);Changed(Profile(index));
    }
    /// <summary>Era helper: stores the era's start date as a plain date.</summary>
    internal void SetEra(int option)
    {
        var s=Sel;if(s==null||option<0||option>=s.EraOptions.Count)return;
        var value=s.EraOptions[option].Value;
        if(value==s.MinimumDate)return;
        pending.Remove((s.Id,EraKey));
        var index=Selected;Draft.Set(index,"modifiers."+EraKey,value);Changed(Profile(index));
    }
    /// <summary>Rocket launch speed may not exceed flight speed even while hidden (MSD identity rule); clamp the hidden value only.</summary>
    private void Normalize(int index)
    {
        try
        {
            var p=Draft.Profiles[index]!.AsObject();
            if(p["modules"]?["propulsion"]?["kind"]?.GetValue<string>()!="rocket"||Draft.Active(index,"launchSpeed"))return;
            var fs=Draft.Number(index,"flightSpeed");
            if(Draft.Number(index,"launchSpeed")>fs){Draft.Set(index,"modifiers.launchSpeed",fs.ToString("R",CultureInfo.InvariantCulture));stamps[IdAt(index)]=++Version;}
        }
        catch(Exception ex) when (ex is FormatException or InvalidOperationException){}
    }

    // ---------------- name ----------------
    internal static string? LabelRule(string label,IEnumerable<string> others)
    {
        if(string.IsNullOrWhiteSpace(label))return "Enter a name.";
        if(label.Length>80)return "At most 80 characters.";
        if(label.Any(char.IsControl))return "No control characters.";
        if(string.Equals(label,"Vanilla ammunition",StringComparison.OrdinalIgnoreCase))return "This name is reserved for the native ammunition.";
        if(others.Any(o=>string.Equals(o,label,StringComparison.OrdinalIgnoreCase)))return "Another shell already has this name.";
        return null;
    }
    private IEnumerable<string> LabelsExcept(int index){for(int i=0;i<Count;i++)if(i!=index)yield return Draft.Profiles[i]!["label"]!.GetValue<string>();}
    internal string? LabelError(int index)=>index<0||index>=Count?null:pending.TryGetValue((IdAt(index),LabelKey),out var p)?p.Message:null;
    internal void SetLabel(string text)
    {
        var index=Selected;if(index<0)return;var id=IdAt(index);
        var label=(text??"").Trim();
        if(LabelRule(label,LabelsExcept(index)) is {} error){pending[(id,LabelKey)]=(text??"",error);Changed(null);return;}
        pending.Remove((id,LabelKey));
        if(Draft.Profiles[index]!["label"]!.GetValue<string>()!=label)Draft.Set(index,LabelKey,label);
        Changed(id);
        RetryPendingLabels();
    }
    /// <summary>A name that clashed may become free after another rename or delete.</summary>
    private void RetryPendingLabels()
    {
        foreach(var key in pending.Keys.Where(k=>k.Key==LabelKey).ToArray())
        {
            var i=IndexOf(key.Id);if(i<0){pending.Remove(key);continue;}
            var label=pending[key].Text.Trim();
            if(LabelRule(label,LabelsExcept(i))==null){pending.Remove(key);Draft.Set(i,LabelKey,label);Changed(key.Id);}
        }
    }

    // ---------------- collection: only unsaved drafts move or disappear (P11) ----------------
    /// <summary>Saved IDs stay: vehicles reference them. Drafts are shells not in the runtime catalogue.</summary>
    internal bool CanDelete(int index)=>index>=0&&index<Count&&!IsSaved(index);
    internal bool CanMove(int index,int delta)=>CanDelete(index)&&index+delta>=0&&index+delta<Count&&delta!=0;
    internal bool Delete(int index)
    {
        if(!CanDelete(index))return false;
        var id=IdAt(index);
        Draft.Profiles.RemoveAt(index);
        foreach(var key in pending.Keys.Where(k=>k.Id==id).ToArray())pending.Remove(key);
        states.Remove(id);stamps.Remove(id);
        if(selected>=Count)selected=Count-1;
        Changed(null);RetryPendingLabels();
        return true;
    }
    internal bool Move(int index,int delta)
    {
        if(!CanMove(index,delta))return false;
        var node=Draft.Profiles[index]!;Draft.Profiles.RemoveAt(index);Draft.Profiles.Insert(index+delta,node);
        if(selected==index)selected=index+delta;else if(selected==index+delta)selected=index;
        Changed(null);return true;
    }
    /// <summary>"&lt;name&gt; (copy)" directly below the original; a new draft ID.</summary>
    internal int Duplicate(int index)
    {
        if(index<0||index>=Count||!CanAdd)return -1;
        var created=Draft.Duplicate(Draft.Profiles[index]!.AsObject());
        var copy=Draft.Profiles[index]!["label"]!.GetValue<string>()+" (copy)";
        if(LabelRule(copy,LabelsExcept(created))==null)Draft.Set(created,LabelKey,copy);
        while(created>index+1){var node=Draft.Profiles[created]!;Draft.Profiles.RemoveAt(created);Draft.Profiles.Insert(created-1,node);created--;}
        selected=created;Changed(IdAt(created));
        return created;
    }
    internal int Create(JsonObject legacyTemplate)
    {
        if(!CanAdd)return -1;
        var created=Draft.Duplicate(ModularShellDraft.FromLegacy(legacyTemplate));
        selected=created;Changed(IdAt(created));
        return created;
    }

    // ---------------- list ----------------
    private ShellRowView[] rows=Array.Empty<ShellRowView>();private int rowsVersion=-1;
    private ShellRowView[] filtered=Array.Empty<ShellRowView>();private (int Version,string Search) filteredKey=(-1,"");
    internal IReadOnlyList<ShellRowView> Rows
    {
        get
        {
            if(rowsVersion==Version)return rows;
            var saved=snapshot.ToDictionary(s=>s.Id,s=>s.Json,StringComparer.Ordinal);
            rows=new ShellRowView[Count];
            for(int i=0;i<Count;i++)
            {
                var s=State(i);var draft=!runtimeIds.Contains(s.Id);
                var edited=!saved.TryGetValue(s.Id,out var json)||json!=s.Json;
                var errors=ErrorsFor(i,s);
                var state=errors>0?(errors==1?"1 ERROR":errors+" ERRORS"):draft?"DRAFT":edited?"EDITED":null;
                var a=AvailabilityOf(s.Id);var reason=a?.Reason??"";var unavailable=a is {} av&&ShellEditorText.Dimmed(av.Available,reason);
                var subtitle=reason.Length>0?s.Subtitle+" · "+reason:s.Subtitle;
                rows[i]=new(s.Id,s.Label,subtitle,(unavailable||reason.StartsWith("Archive",StringComparison.OrdinalIgnoreCase))&&ShellEditorText.AvailabilityTag(reason) is {} tag?tag:ShellEditorText.Tag(s.Behavior),state,errors,draft,s.Chain,reason,unavailable,a?.Date);
            }
            rowsVersion=Version;return rows;
        }
    }
    internal IReadOnlyList<ShellRowView> Filtered(string? search)
    {
        search=(search??"").Trim();
        if(search.Length==0&&!sortByDate)return Rows;
        if(filteredKey==(Version,search))return filtered;
        IEnumerable<ShellRowView> list=Rows;
        if(search.Length>0)list=list.Where(r=>r.Title.Contains(search,StringComparison.OrdinalIgnoreCase)||r.Tag.Contains(search,StringComparison.OrdinalIgnoreCase)||r.Id.Contains(search,StringComparison.OrdinalIgnoreCase)||r.Chain.Contains(search,StringComparison.OrdinalIgnoreCase));
        if(sortByDate)list=list.OrderBy(r=>r.Date??DateTime.MaxValue); // stable: equal dates keep list order
        filtered=list.ToArray();
        filteredKey=(Version,search);return filtered;
    }
    private int ErrorsFor(int index,ShellState s)
    {
        int n=s.Errors.Count(e=>s.Active.GetValueOrDefault(e.Key)||e.Key==EraKey);
        foreach(var p in pending)if(p.Key.Id==s.Id&&(p.Key.Key==LabelKey||s.Active.GetValueOrDefault(p.Key.Key)))n+=s.Errors.ContainsKey(p.Key.Key)?0:1;
        return n;
    }
    /// <summary>Errors in all shells (typed text, rules, names, eras) plus the selected shell's compile error.</summary>
    internal int ErrorCount
    {
        get
        {
            var compile=LiveCompileError;
            if(errorCount.Version==Version&&errorCount.Live==liveStamp)return errorCount.Count;
            var n=0;foreach(var r in Rows)n+=r.Errors;
            errorCount=(Version,liveStamp,n+(compile!=null?1:0));
            return errorCount.Count;
        }
    }
    private (int Version,int Live,int Count) errorCount=(-1,-1,0);
    internal IEnumerable<ShellIssue> AllErrors()
    {
        for(int i=0;i<Count;i++)
        {
            var s=State(i);if(ErrorsFor(i,s)==0)continue;
            foreach(var (key,message) in FieldErrors(s))yield return new($"{s.Label} · {Where(key)}: {message}",ShellIssueKind.Error,GroupOf(key),key);
        }
    }
    private IEnumerable<(string Key,string Message)> FieldErrors(ShellState s)
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in pending)if(p.Key.Id==s.Id&&(p.Key.Key==LabelKey||s.Active.GetValueOrDefault(p.Key.Key))&&seen.Add(p.Key.Key))yield return (p.Key.Key,p.Value.Message);
        foreach(var e in s.Errors)if((s.Active.GetValueOrDefault(e.Key)||e.Key==EraKey)&&seen.Add(e.Key))yield return (e.Key,e.Value);
    }
    internal static string? GroupOf(string key)=>key==LabelKey?null:key==EraKey?"Carrier":FieldsByKey.TryGetValue(key,out var f)?f.Group:null;
    internal static string Where(string key)=>(GroupOf(key) is {} g?g+" · ":"")+ShellEditorText.Caption(key);
    internal string? FirstError=>AllErrors().FirstOrDefault()?.Text??(LiveCompileError is {} c?Label+": "+c:null);

    // ---------------- dirty ----------------
    private (int Version,bool Dirty,string Summary) dirty=(-1,false,"");
    internal bool Dirty=>DirtyState().Dirty;
    internal string DirtySummary=>DirtyState().Summary;
    private (int,bool Dirty,string Summary) DirtyState()
    {
        if(dirty.Version==Version)return dirty;
        var saved=snapshot.ToDictionary(s=>s.Id,s=>s.Json,StringComparer.Ordinal);
        int edited=0,added=0;var order=new List<string>();
        for(int i=0;i<Count;i++)
        {
            var s=State(i);
            if(!saved.TryGetValue(s.Id,out var json))added++;else{order.Add(s.Id);if(json!=s.Json)edited++;}
        }
        var current=Enumerable.Range(0,Count).Select(IdAt).ToHashSet(StringComparer.Ordinal);
        int removed=snapshot.Count(s=>!current.Contains(s.Id));
        bool moved=!order.SequenceEqual(snapshot.Select(s=>s.Id).Where(current.Contains));
        var parts=new List<string>();
        string Plural(int n,string one,string many)=>n+" "+(n==1?one:many);
        if(edited>0)parts.Add(Plural(edited,"shell edited","shells edited"));
        if(added>0)parts.Add(Plural(added,"new draft","new drafts"));
        if(removed>0)parts.Add(Plural(removed,"draft deleted","drafts deleted"));
        if(moved)parts.Add("order changed");
        if(pending.Count>0)parts.Add(Plural(pending.Count,"invalid entry","invalid entries"));
        if(unsavedImport&&parts.Count==0)parts.Add("imported draft not saved yet");
        dirty=(Version,parts.Count>0,parts.Count>0?string.Join(" · ",parts):"No unsaved changes");
        return dirty;
    }

    // ---------------- live column (debounced) ----------------
    internal static readonly string[] LiveLabels={"Behaviour","Status","Penetrator","Length · mass","Loader fit","Velocity","Penetration","Flight"};
    private (int Version,int Selected) liveKey=(-1,-1);
    private string[] liveValues=LiveLabels.Select(_=>"").ToArray();
    private ShellIssue[] liveIssues=Array.Empty<ShellIssue>();
    private ShellIssue[] issues=Array.Empty<ShellIssue>();private (int Version,int Live) issuesKey=(-1,-1);
    private int liveStamp;
    internal string? LiveCompileError{get{Refresh();return compileError;}}
    private string? compileError;
    internal ShellProfile? LiveProfile{get{Refresh();return liveProfile;}}
    private ShellProfile? liveProfile;
    internal string LiveValue(int line){Refresh();return line>=0&&line<liveValues.Length?liveValues[line]:"";}
    /// <summary>Recompute at most once per change, 150 ms after the last edit; a new selection is immediate.</summary>
    internal void Refresh(bool force=false)
    {
        if(liveKey==(Version,Selected))return;
        if(!force&&liveKey.Selected==Selected&&clock()-LastChange<LiveDebounceSeconds)return;
        liveKey=(Version,Selected);liveStamp++;
        ComputeLive();
    }
    private void ComputeLive()
    {
        var values=LiveLabels.Select(_=>"—").ToArray();var found=new List<ShellIssue>();
        compileError=null;liveProfile=null;
        if(Selected<0){liveValues=values;liveIssues=Array.Empty<ShellIssue>();return;}
        var s=State(Selected);
        values[0]=ShellEditorText.Behaviour(s.Behavior);
        try
        {
            var (clone,index)=PreviewDraft(Selected);
            var capability=clone.Analyze(index);
            values[1]=capability.Status;
            try
            {
                var compiled=clone.Compile();
                if(compiled.Ready)liveProfile=ShellProfiles.Parse(compiled.RuntimeJson!).FirstOrDefault(p=>p.Id==s.Id);
                else found.Add(new("No preview: this module combination has no runtime adapter. Save keeps it as a draft only.",ShellIssueKind.Info,"Carrier"));
            }
            catch(Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException)
            {compileError=ShellEditorText.Humane(ex.Message,s.Id,s.Label);found.Add(new(compileError,ShellIssueKind.Error));}
            foreach(var warning in capability.Warnings)
                if(compileError==null||ShellEditorText.Humane(warning,s.Id,s.Label)!=compileError)
                    found.Add(new(ShellEditorText.Humane(warning,s.Id,s.Label),capability.Status.StartsWith("Invalid",StringComparison.Ordinal)?ShellIssueKind.Error:ShellIssueKind.Warning));
            if(capability.Status.StartsWith("Invalid",StringComparison.Ordinal)&&compileError==null)compileError=found.LastOrDefault()?.Text;
        }
        catch(Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or JsonException or IOException or KeyNotFoundException)
        {compileError=ShellEditorText.Humane(ex.Message,s.Id,s.Label);found.Add(new(compileError,ShellIssueKind.Error));}
        if(liveProfile is {} profile)Numbers(profile,values,found);
        liveValues=values;liveIssues=found.ToArray();
    }
    private void Numbers(ShellProfile profile,string[] values,List<ShellIssue> found)
    {
        var c=Cannon;
        if(c==null){values[2]="Open the editor from a cannon to see numbers.";return;}
        try
        {
            var dart=ShellBalance.Calculate(c.CalibreMm,c.MuzzleVelocity,c.PenetratorConstant,profile);
            values[2]=FormattableString.Invariant($"{dart.Diameter*1000:0.0} mm (cannon {c.CalibreMm:0} mm)");
            values[3]=FormattableString.Invariant($"{dart.Length*1000:0} mm · {dart.Mass:0.00} kg");
            var storage=ShellStorageGeometry.Calculate(c.CalibreMm,c.PropellantLengthMm,profile.Settings);
            values[4]=$"{storage.ProjectileLengthMm:0} + {storage.PropellantLengthMm:0} = {storage.CombinedLengthMm:0} mm";
            values[5]=$"{dart.Velocity:0} m/s";
            if(profile.Behavior=="he")values[6]=FormattableString.Invariant($"HE power {ShellBalance.BlastPower(profile,c.CalibreMm):0.0}");
            else if(profile.Behavior is "heat" or "hesh" or "atgm" or "atgm_gun")
            {
                double budget;try{budget=c.ChemicalBudget?.Invoke(profile)??ShellBalance.ChemicalPenetration(profile,c.CalibreMm);}catch(Exception){budget=ShellBalance.ChemicalPenetration(profile,c.CalibreMm);}
                values[6]=$"{budget:0} mm chemical"+(profile.TandemPrecursorPenetrationMm>0?$" · tandem {profile.TandemPrecursorPenetrationMm:0} mm":"");
            }
            else values[6]=c.Penetration is {} pen?$"{pen(dart):0} mm RHA":"RHA: needs the game";
            values[7]=ShellBalance.FlightEnabled(profile)&&profile.Atgm is {} a?$"{a.FlightSpeed:0} m/s · {a.MaxTurnRate:0}°/s · {ShellEditorText.Option("guidanceMode",a.GuidanceMode)}":"Ballistic";
        }
        catch(Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {found.Add(new("No numbers for this cannon: "+ex.Message,ShellIssueKind.Warning));}
    }
    /// <summary>
    /// One-shell compile without touching other shells: the selected shell (hidden modifiers removed)
    /// plus the saved runtime shells in their unchanged form, so an error elsewhere never blocks this preview.
    /// </summary>
    private (ModularShellDraft Draft,int Index) PreviewDraft(int index)
    {
        var id=IdAt(index);
        var mine=WithoutHidden(index,Draft.Profiles[index]!.AsObject());
        var runtime=JsonNode.Parse(runtimeJson)!["profiles"]!.AsArray();
        var labels=runtime.Select(p=>p!["label"]!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var label="Shell preview";for(int n=2;labels.Contains(label)||string.Equals(label,"Vanilla ammunition",StringComparison.OrdinalIgnoreCase);n++)label="Shell preview "+n;
        mine["label"]=label;
        var list=new JsonArray();int at=-1;
        foreach(var entry in runtime)
        {
            var rid=entry!["id"]!.GetValue<string>();
            if(rid==id){at=list.Count;list.Add(mine);}
            else list.Add(ModularShellDraft.FromLegacy(entry.AsObject()));
        }
        if(at<0){at=list.Count;list.Add(mine);}
        var doc=new JsonObject{["schemaVersion"]=1,["purpose"]=ModularShellDraft.Purpose,["profiles"]=list};
        return (new ModularShellDraft(runtimeJson,null,doc.ToJsonString()),at);
    }
    /// <summary>Copy of a profile without modifiers for fields hidden by the current modules (P3, P9).</summary>
    private JsonObject WithoutHidden(int index,JsonObject profile)
    {
        var copy=JsonNode.Parse(profile.ToJsonString())!.AsObject();
        if(copy["modifiers"] is JsonObject modifiers)
            foreach(var key in modifiers.Select(m=>m.Key).ToArray())
                if(key!=EraKey&&FieldsByKey.ContainsKey(key)&&!Draft.Active(index,key))modifiers.Remove(key);
        return copy;
    }
    /// <summary>Messages for the selected shell: field errors (red), compile result and Analyze warnings (amber), other shells' errors.</summary>
    internal IReadOnlyList<ShellIssue> Issues
    {
        get
        {
            Refresh();
            if(issuesKey==(Version,liveStamp))return issues;
            var list=new List<ShellIssue>();
            if(Selected>=0){var s=State(Selected);foreach(var (key,message) in FieldErrors(s))list.Add(new($"{Where(key)}: {message}",ShellIssueKind.Error,GroupOf(key),key));}
            list.AddRange(liveIssues);
            var others=Rows.Where((r,i)=>i!=Selected&&r.Errors>0).Select(r=>r.Title).ToArray();
            if(others.Length>0)list.Add(new($"{others.Length} other shell{(others.Length==1?"":"s")} with errors: {string.Join(", ",others.Take(4))}{(others.Length>4?", ...":"")}",ShellIssueKind.Error));
            issues=list.ToArray();issuesKey=(Version,liveStamp);
            return issues;
        }
    }

    // ---------------- save ----------------
    /// <summary>
    /// Same guarantees as the JSON Editor session: one <see cref="JsonFileTransaction"/> over modules.json and
    /// shells.json (conflict check against the opened text, backup, atomic replace, rollback). Hidden fields
    /// keep their values in modules.json but are not compiled into shells.json.
    /// </summary>
    internal ShellSaveResult Save()
    {
        if(ErrorCount>0)throw new InvalidOperationException(FirstError??"Correct the marked fields before saving.");
        for(int i=0;i<Count;i++)Normalize(i);
        var full=Draft.SerializeAuthoring();
        var root=JsonNode.Parse(full)!.AsObject();var profiles=root["profiles"]!.AsArray();
        for(int i=0;i<profiles.Count;i++)profiles[i]=WithoutHidden(i,profiles[i]!.AsObject());
        var compilation=new ModularShellDraft(runtimeJson,null,root.ToJsonString()).Compile();
        var authoring=Draft.SerializeAuthoring(compilation.RuntimeJson);
        var runtime=compilation.RuntimeJson??runtimeJson;
        JsonFileTransaction.Commit(new[]{new JsonFileChange(authoringPath,authoringJson,authoring),new JsonFileChange(runtimePath,runtimeJson,runtime)});
        var id=SelectedId;
        runtimeJson=runtime;authoringJson=authoring;unsavedImport=false;
        Draft=new ModularShellDraft(runtime,authoring);
        Baseline();
        selected=id==null?-1:IndexOf(id);
        var message=compilation.Ready?$"Saved {Count} shells · live catalogue refreshed."
            :$"Saved the draft only: {compilation.Pending.Count} pending design(s). All live shells stay unchanged.";
        return new(compilation.Ready,Count,compilation.Pending.Count,message);
    }
}
