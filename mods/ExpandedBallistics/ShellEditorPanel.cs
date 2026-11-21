using System.Text.Json.Nodes;
using BepInEx;
using BepInEx.Configuration;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// Shell editor on a Sprocket UI Kit panel (wave 2b, SHELL-EDITOR.md): list with tags and error lamps,
/// the five module groups in the middle, live numbers and messages on the right, and a template page.
/// Built once; every value comes from <see cref="ShellEditorModel"/> getters that read caches.
/// The JSON Editor session stays available behind <c>[Shell editor] Use UI Kit = false</c>.
/// </summary>
internal static class ShellEditorPanel
{
    private static ConfigEntry<bool>? useKit;
    internal static bool UseKit=>useKit?.Value!=false;
    internal static void Configure(ConfigFile config)=>useKit=config.Bind("Shell editor","Use UI Kit",true,
        "Open the shell editor as a UI Kit panel. false = the older JSON Editor session (kept as a fallback).");

    private static string RuntimePath=>Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.shells.json");
    private static string AuthoringPath=>Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.modules.json");
    private static string ImportPath=>Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.modules.import.json");

    private static Panel? panel;
    private static ShellEditorModel? model;
    private static string? conflict,loadError,eraWarning;
    private static Action? refresh,redraw;
    private static Sprocket.Vehicles.Cannons.Cannon? lastCannon;
    private static ShellPreviewCannon? cannonContext;
    private static IReadOnlyList<ShellEra>? eras;
    private static string search="";
    private static string? shownId;
    private static readonly Dictionary<string,Collapsible> groupViews=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,bool[]> openState=new(StringComparer.Ordinal);
    private static IReadOnlyList<(string Label,string? Group,string Value)>? eraOptions;
    private static Choice[] eraChoices=Array.Empty<Choice>();
    private static JsonArray? templates;
    private static readonly string[] SortLabels={"List order","By date"};
    private static readonly UnityEngine.Color UnavailableTint=new(.435f,.408f,.341f,1); // #6f6857, the kit's disabled tone

    /// <summary>Open from the inspector buttons. False = not opened (the caller falls back to the JSON Editor).</summary>
    internal static bool TryOpen(Sprocket.Vehicles.Cannons.Cannon? cannon,Action refreshAction,bool import,Action? redrawAction)
    {
        try
        {
            Ensure();
            var p=panel!;
            if(p.IsOpen&&model?.Dirty==true)
            {
                p.Open();
                if(import)p.Confirm("Discard changes and import?",model.DirtySummary+". Import the current shell JSON instead?","Import",()=>Load(cannon,refreshAction,true,redrawAction),ButtonStyle.Danger,"Back");
                else p.SetStatus("The editor has unsaved changes: save or close it first.",StatusKind.Warning);
                return true;
            }
            Load(cannon,refreshAction,import,redrawAction);
            if(!p.Open()){model=null;return false;}   // also brings an open panel to the front
            return true;
        }
        catch(Exception ex)
        {
            Plugin.ModLog.LogWarning("[Shell editor] UI Kit panel unavailable; using the JSON Editor session: "+ex);
            return false;
        }
    }

    private static void Load(Sprocket.Vehicles.Cannons.Cannon? cannon,Action refreshAction,bool import,Action? redrawAction)
    {
        refresh=refreshAction;redraw=redrawAction;lastCannon=cannon;
        cannonContext=Capture(cannon);
        eras??=ShellEraCatalogue.Load(Path.Combine(UnityEngine.Application.streamingAssetsPath,"Eras"));
        RuntimeShellEra.RefreshDiagnostics();
        eraWarning=RuntimeShellEra.DiagnosticWarning(cannon?.Vehicle);
        conflict=null;loadError=null;model=null;shownId=null;openState.Clear();
        var p=panel!;
        if(p.ModalOpen)p.CloseModal();
        if(p.CurrentPage!=null)p.PopToRoot();
        try
        {
            var m=ShellEditorModel.Open(RuntimePath,AuthoringPath,ImportPath,import,eras);
            m.Cannon=cannonContext;
            var vehicleCannon=cannon;
            m.Availability=id=>RuntimeShellSelection.Profiles.FirstOrDefault(x=>x.Id==id) is {} profile
                ?ShellEditorAvailability.For(profile,vehicleCannon) is var a?new ShellAvailability(a.Available,a.Reason,ShellEditorAvailability.Date(profile)):null:null;
            model=m;
            if(!string.IsNullOrEmpty(eraWarning))p.SetStatus(eraWarning,StatusKind.Warning);
            else if(m.Notice is {} notice){p.SetStatus(notice,StatusKind.Info);RuntimeShellEditor.LastSaveMessage=notice;}
            else p.SetStatus($"{m.Count} shells loaded. Changes stay in the editor until you save.",StatusKind.Info);
        }
        catch(Exception ex) when (ShellEditorModel.IsConflict(ex))
        {
            conflict="The shell catalogue changed outside the editor. Import the current JSON to continue; nothing was changed.";
            p.SetStatus(conflict,StatusKind.Warning);
            Plugin.ModLog.LogWarning("[Shell editor] "+ex.Message);
        }
        catch(Exception ex)
        {
            loadError=ex.Message;RuntimeShellEditor.LastSaveMessage=ex.Message;
            p.SetStatus("Cannot open the shell catalogue: "+ex.Message,StatusKind.Error);
            Plugin.ModLog.LogWarning("[Shell editor] "+ex.Message);
        }
        p.Subtitle="Expanded Ballistics"+(cannonContext is {} c?$" · {c.CalibreMm:0} mm cannon":"");
        SyncSelection();
    }
    private static ShellPreviewCannon? Capture(Sprocket.Vehicles.Cannons.Cannon? cannon)
    {
        if(cannon==null)return null;
        try
        {
            var blueprint=cannon.Blueprint;
            double calibre=blueprint.Caliber,velocity=blueprint.MuzzleVelocity,propellant=cannon.ShellBlueprint.PropellantLength;
            ushort k=blueprint.PenetratorConstant;
            var vehicle=cannon.Vehicle;
            string? era=null;try{era=RuntimeShellEra.Era(vehicle);}catch(Exception){}
            return new(calibre,velocity,k,propellant,
                d=>Sprocket.DamageModelling.PenetrationUtils.ComputePenetration(d.Diameter*1000,d.Mass,d.Velocity,d.PenetratorConstant),
                profile=>RuntimeShellEra.ChemicalBudget(profile,calibre,vehicle),era);
        }
        catch(Exception ex){Plugin.ModLog.LogWarning("[Shell editor] Cannon numbers unavailable: "+ex.Message);return null;}
    }

    // ---------------- construction (once) ----------------
    private static void Ensure()
    {
        if(panel!=null)return;
        var p=UiKit.CreatePanel(BallisticsKeybindMetadata.Owner,"shells","Shell editor","Expanded Ballistics",1600,900);
        panel=p;
        p.AllowedContexts=UiContexts.Designer;
        p.StayOpenWhilePaused=true;
        p.OnCloseRequest=ConfirmIfDirty;
        p.OnClose+=()=>{model=null;conflict=null;loadError=null;eraWarning=null;shownId=null;};
        p.DefaultAction(Save);
        p.AddKeyHint("Enter","save");
        p.AddShortcut("Ctrl+N",NewShell);
        p.AddShortcut("Ctrl+D",Duplicate,"duplicate");
        p.AddShortcut("Delete",DeleteSelected,"delete draft");
        p.AddShortcut("Alt+UpArrow",()=>Move(-1));
        p.AddShortcut("Alt+DownArrow",()=>Move(1));
        var diagBanner=p.Body.Stack(0);
        diagBanner.Padding(8,12,0,12).VisibleWhen(()=>!string.IsNullOrEmpty(eraWarning));
        diagBanner.Warning(()=>eraWarning??"").Severity(StatusKind.Warning);
        // 0.22.3: a wider rail so names and short subtitles fit at 1920x1080 (the middle column had spare room).
        var cols=p.Columns(440,0,400);
        BuildList(cols[0]);BuildForm(cols[1]);BuildLive(cols[2]);
        BuildTemplates(p.Page("templates","New shell"));
        Plugin.ModLog.LogInfo("[Shell editor] UI Kit panel created.");
    }
    private static Choice[] EraChoices()
    {
        if(model==null)return Array.Empty<Choice>();
        var options=model.EraOptions;
        if(!ReferenceEquals(eraOptions,options)){eraOptions=options;eraChoices=options.Select(o=>new Choice(o.Label,o.Group)).ToArray();}
        return eraChoices;
    }

    private static void BuildList(Column c)
    {
        var head=c.Section("Shells",()=>model?.CountText??"");
        head.Warning(()=>conflict??loadError??"").VisibleWhen(()=>conflict!=null||loadError!=null);
        head.Button("Import current JSON",()=>Reload(true),ButtonStyle.Primary).VisibleWhen(()=>conflict!=null)
            .Tooltip("Rebuild the draft from the current shells.json. Unchanged modular designs and draft-only IDs are kept; files change only on Save.");
        head.TextField(()=>search,v=>search=v??"","Search shells").OnChange(v=>search=v??"").MaxChars(40);
        head.Segmented(SortLabels,()=>model?.SortByDate==true?1:0,i=>{if(model!=null)model.SortByDate=i==1;})
            .Tooltip("Display order only. By date sorts by the date the shell becomes available; saved IDs and the catalogue order never change.");
        c.List<ShellRowView>(()=>model?.Filtered(search)??Array.Empty<ShellRowView>(),(r,row)=>
            {
                row.Title=r.Title;row.Subtitle=r.Subtitle;row.Tag=r.Tag;row.TagChip=false;row.Key=r.Id; // plain tag: right-aligned, never mid-row
                row.Value=r.State;row.ValueAccent=r.Errors==0&&r.Draft;
                row.Lamp=r.Errors>0;row.LampKind=LampKind.Error;
                // TEMP: vervalt bij Kit 0.1.3 (ListRow dim/alpha): a grey accent bar marks shells this vehicle cannot use yet.
                if(r.Unavailable)row.AccentColor=UnavailableTint;
                row.Removable=r.Draft;row.Selected=model!=null&&r.Id==model.SelectedId;
            })
            .OnSelect(r=>{model?.Select(r.Id);SyncSelection();})
            .OnRemove(r=>{if(model==null)return;var i=model.IndexOf(r.Id);if(i>=0)Delete(i);})
            .Empty("No shells loaded.").Fill();
        var foot=c.Footer("New");
        var first=foot.Row(8);
        first.Button("New shell",NewShell,ButtonStyle.Primary).WithIcon(Icon.Plus).EnabledWhen(()=>model?.CanAdd==true)
            .Tooltip(()=>model?.CanAdd==false?"32 shells: delete a draft first.":"Pick a template (Ctrl+N).");
        first.Button("Duplicate",Duplicate).EnabledWhen(()=>model!=null&&model.Selected>=0&&model.CanAdd).Tooltip("Copy the selected shell as a new draft below it (Ctrl+D).");
        var second=foot.Row(8);
        second.IconButton(Icon.ChevronUp,()=>Move(-1),"Move draft up (Alt+Up)").EnabledWhen(()=>model!=null&&model.CanMove(model.Selected,-1));
        second.IconButton(Icon.ChevronDown,()=>Move(1),"Move draft down (Alt+Down)").EnabledWhen(()=>model!=null&&model.CanMove(model.Selected,1));
        second.Button("Delete",DeleteSelected,ButtonStyle.Danger).Compact().EnabledWhen(()=>model!=null&&model.CanDelete(model.Selected))
            .Tooltip(()=>model!=null&&model.Selected>=0&&!model.CanDelete(model.Selected)?"Saved shells stay: vehicles use their ID.":"Delete the selected unsaved draft (Del).");
        second.Spacer();
        second.Button("Import JSON",()=>Reload(true),ButtonStyle.Quiet).Compact()
            .Tooltip("Import modules.import.json, or rebuild from the current shells.json. Files change only on Save, with backups.");
    }

    private static void BuildForm(Column c)
    {
        var head=c.Section("Shell",()=>model?.IdNote??"");
        var name=head.Row(8);
        name.Label("Name",TextStyle.Value).Colour("#b8ad94").Width(120);
        name.TextField(()=>model?.FieldText(ShellEditorModel.LabelKey)??"",v=>model?.SetLabel(v),"Shell name").MaxChars(80).Flex();
        name.VisibleWhen(()=>model?.Selected>=0).Error(()=>model?.FieldError(ShellEditorModel.LabelKey))
            .Tooltip("Editable name. The saved ID next to the heading never changes.");
        head.Label(()=>model?.BehaviourLine??"",TextStyle.Small).VisibleWhen(()=>model?.Selected>=0);
        head.Label(()=>conflict??loadError??(model==null?"Open the editor from a cannon in the vehicle designer.":"Select a shell on the left."),TextStyle.Body).Wrap()
            .VisibleWhen(()=>model==null||model.Selected<0);
        var scroll=c.ScrollView().Fill();
        scroll.VisibleWhen(()=>model?.Selected>=0);
        foreach(var group in ShellEditorModel.Groups)
        {
            var view=scroll.Collapsible(ShellEditorText.GroupTitle(group),true);
            groupViews[group]=view;
            var body=view.Body;
            body.Label(()=>model?.Description(group)??"",TextStyle.Small).Wrap();
            if(ShellEditorModel.ModuleKey(group)!=null)
            {
                var row=body.Row(8);
                row.Label(group+" module",TextStyle.Value).Colour("#b8ad94").Flex();
                row.Dropdown(()=>model==null?Array.Empty<string>():model.ModuleLabels(group),()=>model?.ModuleIndex(group)??-1,i=>model?.SetModule(group,i))
                    .Accent().Width(360).Tooltip("Changing a module keeps all values; fields the module does not use are hidden and not checked or compiled.");
            }
            foreach(var field in ShellEditorModel.FieldsIn(group))Field(body,field);
            if(group=="Carrier")Era(body);
        }
    }
    private static void Field(Stack body,ModularShellDraft.Field f)
    {
        var key=f.Key;var unit=ShellEditorText.Unit(f);
        var row=body.Row(8);
        row.VisibleWhen(()=>model?.Active(key)==true).Error(()=>model?.FieldError(key))
            .Tooltip(f.Choices!=null?$"{ShellEditorText.Caption(key)} ({key})":$"{ShellEditorText.Caption(key)}: {ShellEditorText.Number(f.Min,f.Step)} to {ShellEditorText.Number(f.Max,f.Step)} {unit}, step {ShellEditorText.Number(f.Step,f.Step)} ({key}).");
        row.Label(ShellEditorText.Caption(key),TextStyle.Value).Colour("#b8ad94").Flex();
        if(f.Choices!=null)
        {
            // 0.22.3: any two-option no/yes field (e.g. keepAfterRetirement) is a Toggle.
            if(ShellEditorText.IsYesNo(f.Choices)){row.Toggle(()=>model?.Choice(key)=="yes",v=>model?.SetChoice(key,v?"yes":"no"));return;}
            var labels=f.Choices.Select(v=>ShellEditorText.Option(key,v)).ToArray();
            if(labels.Length<=3&&labels.All(l=>l.Length<=18))
                row.Segmented(labels,()=>model?.ChoiceIndex(key)??-1,i=>model?.SetChoiceIndex(key,i)).Width(360);
            else
            {
                var choices=labels.Select(l=>new Choice(l)).ToArray();
                row.Dropdown(()=>choices,()=>model?.ChoiceIndex(key)??-1,i=>model?.SetChoiceIndex(key,i)).Width(360);
            }
            return;
        }
        row.Slider(()=>model?.Number(key)??double.NaN,v=>model?.SetNumber(key,v),()=>f.Min,()=>model?.Max(key)??f.Max,f.Step)
            .WithField(84).OnFieldChange(v=>model?.EditText(key,v)).Width(342);
        row.Label(unit,TextStyle.Small).Width(64);
    }
    private static void Era(Stack body)
    {
        var row=body.Row(8);
        row.VisibleWhen(()=>model?.Selected>=0).Error(()=>model?.FieldError(ShellEditorModel.EraKey))
            .Tooltip("Optional extra availability date on top of the native technology dates. Use yyyy.MM.dd.");
        row.Label(ShellEditorText.Caption(ShellEditorModel.EraKey),TextStyle.Value).Colour("#b8ad94").Flex();
        row.TextField(()=>model?.FieldText(ShellEditorModel.EraKey)??"",v=>model?.SetMinimumDate(v),"yyyy.MM.dd")
            .MaxChars(10).Width(130);
        row.Dropdown(EraChoices,()=>model?.EraIndex??0,i=>model?.SetEra(i)).Width(220)
            .Tooltip("Insert an era start date. The saved value is always a date, never an era name.");
        body.Label(()=>model?.EraAvailability??"",TextStyle.Small).VisibleWhen(()=>model?.Selected>=0);
    }

    private static void BuildLive(Column c)
    {
        var live=c.Section("Live",()=>model?.Label??"");
        for(int i=0;i<ShellEditorModel.LiveLabels.Length;i++)
        {
            var line=i;var row=live.Row(8);
            row.Label(ShellEditorModel.LiveLabels[line],TextStyle.Value).Colour("#b8ad94").Width(118);
            row.Label(()=>model?.LiveValue(line)??"",TextStyle.Value).AlignRight().Flex();
        }
        c.Section("Messages",()=>model?.Issues.Count??0);
        var scroll=c.ScrollView().Fill();
        scroll.Repeat<ShellIssue>(()=>model?.Issues??Array.Empty<ShellIssue>(),(issue,s)=>
        {
            var kind=issue.Kind==ShellIssueKind.Error?StatusKind.Error:issue.Kind==ShellIssueKind.Warning?StatusKind.Warning:StatusKind.Info;
            var w=s.Warning(issue.Text).Severity(kind).Tooltip(issue.Text);
            if(issue.Group!=null)w.OnClick(()=>Reveal(issue));
        },4);
        var foot=c.Footer("Save");
        foot.Label(()=>model?.DirtySummary??"",TextStyle.Small);
        var buttons=foot.Row(8);
        buttons.Button("Close",()=>panel?.RequestClose(),ButtonStyle.Quiet).Tooltip("Close; asks first when there are unsaved changes (Esc).");
        buttons.Spacer();
        buttons.Button("Save",Save,ButtonStyle.Primary).EnabledWhen(CanSave)
            .Tooltip(()=>model==null?"Nothing to save.":model.ErrorCount>0||panel?.HasErrors==true?"Fix first: "+(model.FirstError??panel?.FirstError?.Message):"Save modules.json and shells.json together (backup, rollback) and refresh the live catalogue (Enter).");
    }
    private static void BuildTemplates(Stack page)
    {
        var head=page.Row(8);
        head.Padding(12,14,4,14);
        head.Label("Pick a template. The new shell is a draft until you save.",TextStyle.Label).Flex();
        head.Button("Back",()=>panel?.Pop()).WithIcon(Icon.ChevronLeft).Tooltip("Back to the editor (Esc).");
        var cols=page.Columns(0,0,0);
        templates=ShellExampleProfiles.Templates();
        var sets=new[]{("Kinetic",new[]{"ap","aphe","apfsds"}),("Chemical",new[]{"he","heat","hesh"}),("Guided",new[]{"atgm","atgm_gun"})};
        for(int i=0;i<sets.Length;i++)
        {
            var (title,behaviors)=sets[i];
            var picks=templates.Select(t=>t!.AsObject()).Where(t=>behaviors.Contains(ModularShellDraft.Infer(t))).ToArray();
            var count=picks.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            cols[i].Section(title,()=>count);
            var list=cols[i].ScrollView().Fill();
            foreach(var template in picks)
            {
                var label=template["label"]?.GetValue<string>()??"Shell";
                var modules=ModularShellDraft.FromLegacy(template)["modules"]!.AsObject();
                var stats=ShellEditorText.Behaviour(ModularShellDraft.Infer(template))+" · "+string.Join(" · ",ModularShellDraft.ModuleKeys.Select(k=>ShellEditorText.Module(k,modules[k]!["kind"]!.GetValue<string>())));
                var card=list.Card().OnClick(()=>Create(template,label)).MinHeight(56);
                card.EnabledWhen(()=>model?.CanAdd==true).Tooltip(()=>model?.CanAdd==false?"32 shells: delete a draft first.":"Create a draft from "+label+".");
                var text=card.Stack(2);
                text.Label(label,TextStyle.Label);
                text.Label(stats,TextStyle.Small);
            }
        }
    }

    // ---------------- actions ----------------
    private static void SyncSelection()
    {
        var id=model?.SelectedId;
        if(id==shownId)return;
        var groups=ShellEditorModel.Groups;
        if(shownId!=null)openState[shownId]=groups.Select(g=>groupViews.TryGetValue(g,out var v)&&v.IsExpanded).ToArray();
        shownId=id;
        if(id==null)return;
        var state=openState.TryGetValue(id,out var s)?s:null;
        for(int i=0;i<groups.Count;i++)if(groupViews.TryGetValue(groups[i],out var view))view.IsExpanded=state==null||i>=state.Length||state[i];
    }
    private static void Reveal(ShellIssue issue)
    {
        if(issue.Group!=null&&groupViews.TryGetValue(issue.Group,out var view))view.IsExpanded=true;
        panel?.SetStatus(issue.Text,issue.Kind==ShellIssueKind.Error?StatusKind.Error:StatusKind.Warning);
    }
    private static bool Editing=>panel!=null&&model!=null&&panel.CurrentPage==null&&!panel.ModalOpen;
    private static void NewShell(){if(Editing&&model!.CanAdd)panel!.Push("templates");}
    private static void Create(JsonObject template,string label)
    {
        if(model==null||!model.CanAdd)return;
        if(model.Create(template)<0)return;
        panel?.Pop();SyncSelection();
        panel?.SetStatus($"New draft from {label}. It is saved with the next Save.",StatusKind.Success);
    }
    private static void Duplicate()
    {
        if(!Editing||model!.Selected<0)return;
        var source=model.Label;
        if(model.Duplicate(model.Selected)<0){panel!.SetStatus("32 shells: delete a draft first.",StatusKind.Warning);return;}
        SyncSelection();panel!.SetStatus($"Copied {source} as a new draft.",StatusKind.Success);
    }
    private static void DeleteSelected(){if(Editing)Delete(model!.Selected);}
    private static void Delete(int index)
    {
        if(model==null||index<0)return;
        if(!model.CanDelete(index)){panel?.SetStatus("Saved shells stay: vehicles use their ID. Only unsaved drafts can be deleted.",StatusKind.Warning);return;}
        model.Select(index);var label=model.Label;
        model.Delete(index);SyncSelection();
        panel?.SetStatus($"Deleted the draft {label}.",StatusKind.Info);
    }
    private static void Move(int delta)
    {
        if(!Editing||!model!.CanMove(model.Selected,delta))return;
        model.Move(model.Selected,delta);
    }
    private static void Reload(bool import)
    {
        if(panel==null)return;
        if(model?.Dirty==true){panel.Confirm("Discard changes and import?",model.DirtySummary+".","Import",()=>Load(lastCannon,refresh??(()=>{}),import,redraw),ButtonStyle.Danger,"Back");return;}
        Load(lastCannon,refresh??(()=>{}),import,redraw);
    }
    private static bool CanSave()=>panel!=null&&model!=null&&conflict==null&&panel.CurrentPage==null&&!panel.HasErrors&&model.ErrorCount==0;
    private static void Save()
    {
        if(panel==null||model==null||panel.CurrentPage!=null)return;
        if(!CanSave()){panel.SetStatus(model.FirstError??panel.FirstError?.Message??"Correct the marked fields before saving.",StatusKind.Error);return;}
        try
        {
            var result=model.Save();
            var message=result.Message;
            if(result.Ready)
            {
                try{refresh?.Invoke();}
                catch(Exception ex){message+=" Inspector refresh failed: "+ex.Message;Plugin.ModLog.LogWarning("[Shell editor] Refresh after save: "+ex);}
            }
            panel.SetStatus(message,result.Ready?StatusKind.Success:StatusKind.Warning);
            RuntimeShellEditor.LastSaveMessage=message;
            Plugin.ModLog.LogInfo("[Shell editor] "+message);
            try{redraw?.Invoke();}catch(Exception){}
            SyncSelection();
        }
        catch(Exception ex)
        {
            panel.SetStatus("Not saved: "+ex.Message,StatusKind.Error);
            Plugin.ModLog.LogWarning("[Shell editor] Save failed; files unchanged or rolled back: "+ex.Message);
        }
    }
    /// <summary>Esc, ✕ and Close: ask before discarding changes (Esc/Enter in the dialog = Back).</summary>
    private static bool ConfirmIfDirty()
    {
        if(panel==null||model==null||!model.Dirty)return true;
        panel.Confirm("Discard changes?",model.DirtySummary+". Close without saving?","Discard",()=>panel.Close(),ButtonStyle.Danger,"Back");
        return false;
    }
    internal static void Clear()
    {
        try{if(panel!=null)UiKit.DestroyPanel(panel);}catch(Exception){}
        panel=null;model=null;groupViews.Clear();openState.Clear();
    }
}
