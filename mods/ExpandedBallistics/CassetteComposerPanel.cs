using BepInEx;
using SprocketKeybinds;
using SprocketUiKit;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using UVec2=UnityEngine.Vector2;
namespace SprocketExpandedBallistics;

/// <summary>
/// "Cassette composer" on a Sprocket UI Kit panel (COMPOSER.md): presets left, the layer stack in the
/// middle, the to-scale cross-section, derived values, protection table and Apply on the right.
/// Built once; every value comes from <see cref="CassetteComposerModel"/> caches. Designer only.
/// Opened by the "Open composer" button in the Armour material inspector block (0.22.14) or the
/// Keybinds action "Cassette composer" (unbound by default).
/// </summary>
internal static class CassetteComposerPanel
{
    private const string Action="cassette-composer";
    private static Panel? panel;
    private static CassetteComposerModel? model;
    private static ModKeybind? key;
    private static PlateStructureEditor? tracked;
    private static float nextTarget,retryKey;
    private static int warnings;
    private static readonly HashSet<string> used=new(StringComparer.Ordinal);
    private static object? materialOptions;
    private static Choice[] materialChoices=Array.Empty<Choice>();
    private static string ShareDirectory=>Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.cassettes-share");
    private static readonly UnityEngine.Color UnavailableTint=new(.435f,.408f,.341f,1); // #6f6857, the kit disabled tone (as the shell editor)
    private static readonly string[] AngleLabels={"0°","30°","60°"},SideLabels={"With tilt","Against tilt"},DirectionLabels={"Up","Down","Left","Right"};

    // ---------------- lifecycle ----------------
    /// <summary>Per frame from <see cref="DamageFeedBehaviour"/>: lazy panel/keybind setup, and the target plate while open (4×/s).</summary>
    internal static void Update()
    {        if(key==null&&UnityEngine.Time.unscaledTime>=retryKey)
        {
            retryKey=UnityEngine.Time.unscaledTime+2;
            try{Ensure();key=Keybinds.RegisterButton(BallisticsKeybindMetadata.Owner,BallisticsKeybindMetadata.Name,Action,"Cassette composer","");UiKit.BindToggle(panel!,key);}
            catch(Exception ex){key=null;Warn("Keybind unavailable: "+ex.Message);}
        }
        if(panel==null||!panel.IsOpen||UnityEngine.Time.unscaledTime<nextTarget)return;
        nextTarget=UnityEngine.Time.unscaledTime+.25f;
        // 0.22.15: the size follows the screen and the UI scale while open (the kit scale is only right once its canvas exists).
        model?.SetTarget(ReadTarget(out var unavailable),unavailable);
    }
    /// <summary>The designer plate inspector reports itself here; picking another plate retargets the composer.</summary>
    internal static void Track(PlateStructureEditor editor){if(tracked==null||tracked.Pointer!=editor.Pointer){tracked=editor;nextTarget=0;}}
    /// <summary>The inspector button "Open composer".</summary>
    internal static void Open(PlateStructureEditor editor)
    {
        try{Track(editor);Ensure();if(!panel!.Open())Warn("The composer opens in the vehicle designer only.");}
        catch(Exception ex){Warn("Composer unavailable: "+ex);}
    }
    internal static void Clear()
    {
        try{Keybinds.Unregister(BallisticsKeybindMetadata.Owner,Action);}catch(Exception){}
        try{if(panel!=null)UiKit.DestroyPanel(panel);}catch(Exception){}
        panel=null;model=null;key=null;tracked=null;used.Clear();
    }
    private static string N(double v,string format)=>v.ToString(format,System.Globalization.CultureInfo.InvariantCulture);
    private static void Warn(string text){if(warnings++<6)Plugin.ModLog.LogWarning("[Cassette composer] "+text);}

    /// <summary>Panel size in kit units: almost the whole screen, scaling with the screen and the UI scale.</summary>
    private static (float W,float H) ScreenSize()
    {
        try{return ComposerLayout.Size(UnityEngine.Screen.width,UnityEngine.Screen.height,UiKit.Scale,KitFactor());}
        catch(Exception ex){Warn("Screen size unavailable: "+ex.Message);return ComposerLayout.PanelSize(ComposerLayout.MinWidth,ComposerLayout.MinHeight);}
    }
    private static BepInEx.Configuration.ConfigEntry<float>? kitFactor;
    /// <summary>The kit's user setting Display.Scale (public config entry), so the size is right before the kit canvas exists. Null: unknown.</summary>
    private static float? KitFactor()
    {
        try
        {
            if(kitFactor==null&&BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.TryGetValue(UiKit.PluginGuid,out var info)
                &&info.Instance is BepInEx.Unity.IL2CPP.BasePlugin kit&&kit.Config.TryGetEntry<float>("Display","Scale",out var entry))kitFactor=entry;
            return kitFactor?.Value;
        }
        catch(Exception){return null;}
    }
    /// <summary>
    /// 0.22.19: on every open, a panel larger than the screen (first built before the kit scale was known, or after a resolution or
    /// UI scale change) is shrunk to the screen size from <see cref="ComposerLayout"/>. A smaller or user-resized panel is never touched.
    /// </summary>
    private static void FitToScreen()
    {
        try
        {
            if(panel==null||panel.IsMaximized)return;
            var (w,h)=ScreenSize();
            var fit=ComposerLayout.ShrinkTo(panel.Width,panel.Height,w,h);
            if(fit is not {} size)return;
            panel.SetSize(size.W,size.H);
            Plugin.ModLog.LogInfo($"[Cassette composer] Size {N(size.W,"0")}x{N(size.H,"0")} units to fit the screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height} (kit scale {N(UiKit.Scale,"0.###")}).");
        }
        catch(Exception ex){Warn("Fit to screen: "+ex.Message);}
    }
    private static void Opened()
    {
        FitToScreen();
        try{model??=CreateModel();}
        catch(Exception ex){panel?.SetStatus("Composer unavailable: "+ex.Message,StatusKind.Error);Warn(ex.ToString());return;}
        ScanUsage();model.ResetApplyChoice();model.Invalidate();
        var target=ReadTarget(out var unavailable);model.SetTarget(target,unavailable);
        if(target!=null&&CompositePlate.IsCassette(target.MaterialId)&&model.LoadFromMaterial(target.MaterialId))Status();
        else if(model.Dirty)panel!.SetStatus("Unsaved draft from this session: "+model.Label,StatusKind.Info);
        else Status();
    }
    private static CassetteComposerModel CreateModel()
    {
        var snapshot=RuntimeArmourResponses.Snapshot;
        var reactions=snapshot.MaterialReactions??throw new InvalidOperationException("Material reactions unavailable.");
        var reactive=new HashSet<string>(StringComparer.Ordinal);
        if(snapshot.Catalogue!=null)foreach(var r in snapshot.Catalogue.Responses)if(r.Kind is "lightEra" or "heavyEra")foreach(var id in r.CompatibleMaterialIds)reactive.Add(id);
        var technology=Path.Combine(Paths.GameRootPath,"Sprocket_Data/StreamingAssets/Technology");
        var sidecar=Path.Combine(Paths.ConfigPath,CassetteCatalogue.FileName);
        var materials=CassetteTechnology.ReadDirectory(technology);
        var labels=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var known in SprocketExpandedBallistics.Materials.MaterialDatabase.Materials)labels.TryAdd(known.Id,SprocketExpandedBallistics.Materials.MaterialDatabase.MenuLabel(known));
        string Label(string id)=>labels.TryGetValue(id,out var l)?l:System.Text.RegularExpressions.Regex.Replace(id,"(?<=[a-z0-9])([A-Z])"," $1");
        bool Excluded(string id)=>CompositePlate.IsCassette(id)||reactive.Contains(id)||SprocketExpandedBallistics.Materials.MaterialDatabase.RejectedIds.Contains(id);
        var list=materials.Keys.Where(id=>!Excluded(id)&&!SprocketExpandedBallistics.Materials.MaterialMenuVisibility.RetiredPresets.Contains(id))
            .Select(id=>new ComposerMaterial(id,Label(id),CassettePalette.Group(id))).ToArray();
        var legacy=materials.Keys.Where(id=>!Excluded(id)&&(SprocketExpandedBallistics.Materials.MaterialMenuVisibility.RetiredPresets.Contains(id)||SprocketExpandedBallistics.Materials.MaterialMenuVisibility.ComposerOnlyMaterials.Contains(id)))
            .Select(id=>new ComposerMaterial(id,Label(id),"Legacy")).OrderBy(m=>m.Label,StringComparer.OrdinalIgnoreCase).ToArray();
        var writable=File.Exists(sidecar);
        var backend=new CassetteComposer(materials,reactions,sidecar,technology,used.Contains,reactive);
        IReadOnlyList<CassettePreset> Read()=>File.Exists(sidecar)?CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets
            :RuntimeArmourResponses.Snapshot.CassetteJson is {} json?CassetteCatalogue.Parse(json).Presets:Array.Empty<CassettePreset>();
        // 0.22.7: a saved cassette is never dated earlier than its newest material.
        var dates=CassetteTechnology.ReadDates(technology);
        var m=new CassetteComposerModel(backend,list,legacy,Read,used.Contains,id=>RuntimeArmourResponses.Snapshot.Cassettes?.ContainsKey(id)==true&&RuntimeLiveTechnology.ReachesPlay(id),writable,
            materialDate:id=>dates.TryGetValue(id,out var d)?d:null);
        m.FrameCheck=FrameCheck;
        if(!writable)panel?.SetStatus("Cassette file missing: read-only until the defaults are installed.",StatusKind.Error);
        Plugin.ModLog.LogInfo($"[Cassette composer] {list.Length} materials, {legacy.Length} legacy, {m.Catalogue.Count} presets; sidecar {(writable?"writable":"absent")}.");
        return m;
    }
    private static bool Valid(out PlateStructure plate)
    {
        plate=null!;
        try{if(tracked==null||!tracked.IsValid()||tracked.Component==null){tracked=null;return false;}plate=tracked.Component;return true;}
        catch(Exception){tracked=null;return false;}
    }
    private static (IntPtr Plate,ComposerTarget Target)? lastTarget;
    /// <summary>The target plate, or null; <paramref name="unavailable"/> says why when a plate IS selected but cannot be read (0.22.27).</summary>
    private static ComposerTarget? ReadTarget(out string? unavailable)
    {
        unavailable=null;
        if(!Valid(out var plate))return null;
        var name="Plate";
        try
        {
            name="Plate #"+plate.VUID.Value;
            int faces=0;double min=double.PositiveInfinity,max=0;PlateMeshSnapshot? layerSnapshot=null;
            // 0.22.24: the face walk comes from the snapshot gate. While the mesh is being edited the last good reading of this plate is kept,
            // so the panel neither flickers nor touches a half-deleted mesh.
            if(plate.HasMesh)
            {
                var snapshot=PlateMeshAccess.Read(plate,"composer target",SnapshotUse.Display,out var reason);layerSnapshot=snapshot;
                if(snapshot==null)
                {
                    if(lastTarget is {} kept&&kept.Plate==plate.Pointer)return kept.Target;
                    unavailable=CassetteComposerModel.PlateBusy(name,reason);
                    return null;
                }
                faces=snapshot.Faces.Length;
                foreach(var face in snapshot.Faces){var t=face.MeanThickness();min=Math.Min(min,t);max=Math.Max(max,t);}
            }
            var id=plate.armourTechID??"";
            var label=RuntimeArmourResponses.Snapshot.Cassettes?.TryGetValue(id,out var c)==true?c.Preset.Label
                :SprocketExpandedBallistics.Materials.MaterialDatabase.Materials.FirstOrDefault(m=>m.Id==id)?.Label??id;
            // 0.22.14: eligibility for "Convert to composite plate", linked Alt copies and the face selection (read 4x/s, cheap).
            var composite=RuntimeCompositePlate.Is(plate);
            var faceSelection=RuntimeCompositePlateEdit.SelectedFaces(tracked!);
            var selected=CompositeThicknessPlan.SelectedCount(faceSelection,faces);
            string? layerLine=null;
            try{if(composite&&layerSnapshot!=null)layerLine=RuntimeLayerFaces.Line(plate,layerSnapshot);}catch(Exception ex){Warn("Layer line unavailable: "+ex.Message);}
            var frontNote=layerSnapshot==null?null:LayerFaces.SelectionNote(layerSnapshot,faceSelection);
            var storedFront=RuntimeLayerFaces.ReadSavedFront(plate);
            if(layerLine!=null&&storedFront is {} saved)frontNote=LayerFaces.SelectionNote(saved.Json,saved.Front);
            var target=new ComposerTarget(name,faces,id,label,composite,faces>0?min:0,max,VehicleDateKey(plate),KnownMaterial(plate,id),
                RuntimeCompositePlateEdit.Eligibility(plate),composite?0:RuntimeCompositePlateEdit.LinkedCopies(plate),selected,layerLine,frontNote);
            lastTarget=(plate.Pointer,target);
            return target;
        }
        catch(Exception ex){Warn("Target unreadable: "+ex.Message);unavailable=CassetteComposerModel.PlateBusy(name,"target unreadable: "+ex.Message);return null;}
    }
    // ---------------- vehicle date and native technology frame (0.22.7) ----------------
    /// <summary>The native date of the owning vehicle (year*10000+month*100+day); null when unreadable.</summary>
    internal static int? VehicleDateKey(PlateStructure plate)
    {
        try{var d=plate.Vehicle.Tech.Date;return CassetteAvailability.Key(d.Year,d.Month,d.Day);}catch(Exception){}
        try{var d=plate.Vehicle.DesignInfo.Date;return CassetteAvailability.Key(d.Year,d.Month,d.Day);}catch(Exception){}
        return null;
    }
    /// <summary>Does the vehicle technology frame hold this material (the same lookup the material dropdown uses)? Null: unknown.</summary>
    private static bool? KnownMaterial(PlateStructure plate,string id)
    {
        try{return plate.Vehicle.Tech.TryGetTech(id,out _);}catch(Exception){return null;}
    }
    private static bool? FrameCheck(string materialId)=>Valid(out var plate)?KnownMaterial(plate,materialId):null;
    internal static void MaterialsReloaded(){model?.Invalidate();unknownCache=default;nextTarget=0;}
    private static (IntPtr Plate,string Id,int? Date,float Until,string? Text) unknownCache;
    private static (string? Json,CassettePreset[] Presets) catalogueCache;
    private static CassettePreset? FindPreset(string materialId)
    {
        try
        {
            var json=RuntimeArmourResponses.Snapshot.CassetteJson;if(json==null)return null;
            if(!ReferenceEquals(catalogueCache.Json,json))catalogueCache=(json,CassetteCatalogue.Parse(json).Presets);
            return catalogueCache.Presets.FirstOrDefault(p=>p.MaterialId==materialId);
        }
        catch(Exception){return null;}
    }
    /// <summary>
    /// For the plate inspector: the warning line when this plate carries a cassette the game does not know on its vehicle
    /// (date later than the vehicle, or a Technology file that is missing or not loaded). Null when all is well. Throttled.
    /// </summary>
    internal static string? UnknownMaterialLine(PlateStructure plate)
    {
        try
        {
            var id=plate.armourTechID??"";if(!CompositePlate.IsCassette(id))return null;
            var date=VehicleDateKey(plate);var now=UnityEngine.Time.unscaledTime;
            if(unknownCache.Plate==plate.Pointer&&unknownCache.Id==id&&unknownCache.Date==date&&now<unknownCache.Until)return unknownCache.Text;
            string? text=null;
            if(KnownMaterial(plate,id)==false)
            {
                var preset=FindPreset(id);
                text=CassetteAvailability.UnknownMaterial(id,preset?.Label??SprocketExpandedBallistics.Materials.MaterialAvailability.Label(id,id),preset?.Date,date,true);
            }
            unknownCache=(plate.Pointer,id,date,now+1f,text);return text;
        }
        catch(Exception){return null;}
    }
    /// <summary>Cassettes on the current vehicle (the "IN USE" tag). Saved blueprint files are not scanned here.</summary>
    private static void ScanUsage()
    {
        used.Clear();
        if(!Valid(out var plate))return;
        try
        {
            var items=plate.Vehicle.ObjectReader.Items;
            int count=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.Vehicles.VehicleObject>>().Count;
            for(int i=0;i<count;i++)
            {
                var components=items[i].Components;
                int n=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.Vehicles.VehicleComponent>>().Count;
                for(int j=0;j<n;j++)if(components[j]?.TryCast<PlateStructure>() is {} p&&CompositePlate.IsCassette(p.armourTechID))used.Add(p.armourTechID);
            }
        }
        catch(Exception ex){Warn("Usage scan: "+ex.Message);}
    }

    // ---------------- construction (once) ----------------
    private static void Ensure()
    {
        if(panel!=null)return;
        // New id: the old "composer" panel has a remembered position from the 1600 x 900 window, which would push a
        // nearly full-screen window off the right edge. A fresh id is centred on first open.
        var (width,height)=ScreenSize();
        var p=UiKit.CreatePanel(BallisticsKeybindMetadata.Owner,"composer-wide","Cassette composer","Expanded Ballistics",width,height);
        panel=p;
        p.AllowedContexts=UiContexts.Designer;
        p.OnOpen+=Opened;
        p.AddShortcut("Ctrl+S",()=>Save(false),"save");
        p.AddShortcut("Ctrl+Z",()=>Edit(m=>m.Undo()),"undo");
        p.AddShortcut("Ctrl+D",()=>Edit(m=>m.DuplicateLayer()),"duplicate");
        p.AddShortcut("Ctrl+N",()=>Edit(m=>m.AddLayer()));
        p.AddShortcut("Delete",()=>Edit(m=>m.DeleteLayer()),"delete layer");
        p.AddShortcut("Alt+UpArrow",()=>Edit(m=>m.MoveLayer(-1)));
        p.AddShortcut("Alt+DownArrow",()=>Edit(m=>m.MoveLayer(1)));
        for(int i=0;i<AngleLabels.Length;i++){var a=i;p.AddShortcut((a+1).ToString(System.Globalization.CultureInfo.InvariantCulture),()=>{if(model!=null)model.AngleIndex=a;});}
        p.AddKeyHint("1-3","angle");
        p.MinSize(960,560); // the user may shrink it (default minimum: the creation size); the kit still never lets it exceed the screen
        var c=p.Columns(340,-1f,-1.35f);
        // The Kit adds Fill surplus to the measured minimum; a ColumnSet otherwise inherits the
        // tallest column minimum (including its footer). Let the panel allocate its actual height.
        foreach(var column in c)column.Height(0);
        Presets(c[0]);Layers(c[1]);Preview(c[2]);
        Plugin.ModLog.LogInfo("[Cassette composer] UI Kit panel created.");
    }
    private static void Edit(Action<CassetteComposerModel> action){if(model!=null&&panel!=null&&!panel.ModalOpen){action(model);Status();}}
    private static void Status(StatusKind kind=StatusKind.Info){if(panel!=null&&model?.Status is {} s)panel.SetStatus(s,kind);}
    // Grouped materials use Choice; Kit's string overload is used for ungrouped shell modules.
    private static Choice[] MaterialChoices()
    {
        if(model==null)return Array.Empty<Choice>();
        var labels=model.MaterialChoiceLabels;
        if(!ReferenceEquals(materialOptions,labels)){materialOptions=labels;materialChoices=labels.Select(l=>new Choice(l.Label,l.Group)).ToArray();}
        return materialChoices;
    }

    private static void Presets(Column c)
    {
        var s=c.Section("Presets",()=>model?.PresetRows.Count??0);
        s.Tabs(new[]{"Historical","Own","Legacy"},()=>model?.Tab??0,i=>{if(model!=null)model.Tab=i;});
        s.Segmented(CassetteComposerModel.LocationLabels,()=>model?.LocationIndex??0,i=>{if(model!=null)model.LocationIndex=i;})
            .Tooltip("Where the armour sits. It filters the historical presets; own presets always show, each with its own location. New cassettes take the location selected here.");
        s.TextField(()=>model?.Search??"",v=>{if(model!=null)model.Search=v;},"Search preset or material").OnChange(v=>{if(model!=null)model.Search=v;}).MaxChars(40);
        c.List<ComposerPresetRow>(()=>model?.PresetRows??Array.Empty<ComposerPresetRow>(),(r,row)=>
            {
                row.Title=r.Title;row.Subtitle=r.Subtitle;row.Tag=r.Tag;row.TagChip=true;row.Selected=model!=null&&r.Key==model.SelectedPresetKey;row.Key=r.Key;
                // TEMP: vervalt bij Kit met ListRow dim/alpha: a grey accent bar and the reason ("From 1985") mark a preset the target vehicle cannot use yet.
                if(!r.Available)row.AccentColor=UnavailableTint;
            })
            .OnSelect(r=>{if(model!=null&&model.LoadPreset(r.Key))Status();})
            .Empty("Nothing here. Own cassettes appear after a save; edit a historical preset or use From selection.").Fill();
        var f=c.Footer("Cassette");
        var head=f.Row(8);
        head.Label(()=>model?.Label??"",TextStyle.Label).Flex();
        head.Chip(()=>model?.StateChip??"",ChipStyle.Tag);
        var name=f.Labeled("Name");
        name.TextField(()=>model?.Label??"",v=>Edit(m=>m.SetLabel(v)),"Cassette name").MaxChars(120);
        name.Error(()=>model?.LabelError).Tooltip("Editing an unchanged preset creates an own copy first.");
        var date=f.Labeled("Technology date");
        date.TextField(()=>model?.Date??"",v=>Edit(m=>m.SetDate(v)),"yyyy.mm.dd").MaxChars(10);
        date.Error(()=>model?.DateError).Tooltip("Native Technology date of the saved material (yyyy.mm.dd). It follows the newest layer material and can be set later, never earlier: a vehicle dated before it does not know the cassette.");
        f.Label(()=>model?.DateNote??"",TextStyle.Small).Wrap();
        f.Label(()=>model?.MetaText??"",TextStyle.Small).Wrap();
        var b1=f.Row(8);
        b1.Button("New cassette",()=>Edit(m=>m.NewCassette())).Compact();
        b1.Button("From selection",FromSelection).Compact().EnabledWhen(()=>tracked!=null)
            .Tooltip("Parallel plates selected in the designer become one layer each (material, thickness); gaps become air.");
        var b2=f.Row(8);
        b2.Button("Import...",ImportDialog).Compact().Tooltip(()=>"Load a .cassette.json share file from "+ShareDirectory);
        b2.Button("Export",Export).Compact().EnabledWhen(()=>model?.CanExport==true).Tooltip(()=>"Write a .cassette.json share file to "+ShareDirectory);
    }

    private static void Layers(Column c)
    {
        c.Section("Layers",()=>model?.LayersNote??"");
        c.List<ComposerLayerRow>(()=>model?.LayerRows??Array.Empty<ComposerLayerRow>(),(r,row)=>
            {
                row.Title=r.Title;row.Subtitle=r.Subtitle;row.Tag=r.Tag;row.Value=r.Value;row.Swatch=r.Swatch;
                row.Selected=model!=null&&model.IsLayerSelected(r.Index);row.Removable=model!=null&&model.Layers.Count>1;
                // 0.22.19: the lamp only shows when a message names this layer, and the row then carries that message as its note.
                if(r.Error||r.Warning){row.Lamp=true;row.LampKind=r.Error?LampKind.Error:LampKind.Warning;row.Note=r.Note;}
            })
            .OnSelect(r=>ChooseLayer(r.Index)).OnRemove(r=>Edit(m=>m.DeleteLayer(r.Index)))
            .Empty("No layers yet. Pick a preset or add a layer.").Fill();
        c.Section("Messages",()=>model?.Issues.Count??0);
        // 0.22.19: a plain stack of at most four messages (errors first, then "+N more"), no inner scroll view that could hide one.
        var messages=c.Stack(6);
        messages.Label("No warnings.",TextStyle.Small).VisibleWhen(()=>model!=null&&model.Issues.Count==0);
        messages.Repeat<ComposerIssue>(()=>model?.ShownIssues??Array.Empty<ComposerIssue>(),(issue,s)=>
        {
            var row=s.Row(6);
            var w=row.Warning(issue.Text).Severity(issue.Error?StatusKind.Error:StatusKind.Warning).Tooltip(issue.Text).Flex();
            if(issue.Layer>=0)w.OnClick(()=>model?.SelectLayer(issue.Layer));
            if(issue.FixLabel!=null)row.Button(issue.FixLabel,()=>Edit(m=>m.ClampTilts())).Compact();
        },4);
        var e=c.Footer("Layer");
        e.Label(()=>model?.LayerTitle??"",TextStyle.Label);
        Func<bool> editable=()=>model?.LayerEditable==true;Func<bool> solid=()=>model?.LayerEditable==true&&!model.LayerIsAir;
        var first=e.Row(10);
        first.Labeled("Material").Dropdown(MaterialChoices,()=>model?.MaterialChoice??-1,i=>Edit(m=>m.SetMaterialChoice(i))).EnabledWhen(editable);
        var thickness=first.Labeled("Thickness t");
        var t=thickness.Row(6);
        t.Stepper(()=>model?.LayerThickness??0,v=>Edit(m=>m.SetThickness(v)),1,2000,1,"mm").FastStep(10,100).EnabledWhen(editable);
        t.Slider(()=>model?.LayerThickness??0,v=>Edit(m=>m.SetThickness(v)),1,600,1).Format(_=>"").Flex().EnabledWhen(editable);
        var second=e.Row(10);
        var tilt=second.Labeled("Tilt");
        tilt.Stepper(()=>model?.LayerTilt??0,v=>Edit(m=>m.SetTilt(v)),0,75,1,"°").FastStep(5).EnabledWhen(solid);
        tilt.Error(()=>model?.LayerTiltError).Width(170);
        second.Labeled("Tilt toward (vehicle)").Segmented(DirectionLabels,()=>model?.LayerDirection??-1,i=>Edit(m=>m.SetDirection(i))).EnabledWhen(solid)
            .Tooltip("The layer normal turns toward the vehicle's up, down, left or right, projected onto each face plane, so it means the same on a curved plate.");
        e.Label(()=>model?.LayerDepthText??"",TextStyle.Small);
        var buttons=e.Row(6);
        buttons.Button("Layer",()=>Edit(m=>m.AddLayer())).WithIcon(Icon.Plus).Compact().EnabledWhen(()=>model?.CanAddLayer==true).Tooltip("Steel 10 mm after the selection (Ctrl+N).");
        buttons.Button("Air",()=>Edit(m=>m.AddAir())).WithIcon(Icon.Plus).Compact().EnabledWhen(()=>model?.CanAddLayer==true).Tooltip("Air gap 20 mm after the selection.");
        buttons.Spacer();
        buttons.IconButton(Icon.ChevronUp,()=>Edit(m=>m.MoveLayer(-1)),"Move up (Alt+Up)").EnabledWhen(()=>model?.CanMoveLayer(-1)==true);
        buttons.IconButton(Icon.ChevronDown,()=>Edit(m=>m.MoveLayer(1)),"Move down (Alt+Down)").EnabledWhen(()=>model?.CanMoveLayer(1)==true);
        buttons.Button("Duplicate",()=>Edit(m=>m.DuplicateLayer())).Compact().EnabledWhen(()=>model?.CanDuplicateLayer==true).Tooltip("Ctrl+D");
        buttons.Button("Delete",()=>Edit(m=>m.DeleteLayer()),ButtonStyle.Danger).Compact().EnabledWhen(()=>model?.CanDeleteLayer==true).Tooltip("Del");
        buttons.Button("Undo",()=>Edit(m=>m.Undo()),ButtonStyle.Quiet).Compact().EnabledWhen(()=>model?.CanUndo==true).Tooltip("Ctrl+Z (20 steps)");
    }

    /// <summary>
    /// 0.22.71: a click on a layer row. Ctrl and Shift are read from the keyboard at the moment of the click (Input System, the same source the kit reads): Ctrl adds or removes the layer,
    /// Shift chooses everything between the layer chosen last and this one, a plain click chooses this layer alone.
    /// </summary>
    private static void ChooseLayer(int index)
    {
        if(model==null)return;
        bool ctrl=false,shift=false;
        try{var keyboard=UnityEngine.InputSystem.Keyboard.current;ctrl=keyboard?.ctrlKey.isPressed==true;shift=keyboard?.shiftKey.isPressed==true;}catch(Exception){}
        if(shift)model.SelectRange(index);else if(ctrl)model.ToggleLayer(index);else model.SelectLayer(index);
    }

    private static void Preview(Column c)
    {
        c.Section("Cross-section",()=>model?.AngleText??"");
        // Scroll only the preview body; the action footer keeps its own bounded height.
        var body=c.ScrollView().Fill();
        body.Canvas(CassetteSection.Height,DrawSection).Version(()=>model?.DiagramVersion??0)
            .RegionTooltip(id=>id.Length>1&&id[0]=='L'&&int.TryParse(id.AsSpan(1),out var i)?model?.BandTooltip(i):null);
        var angles=body.Row(8);
        angles.Segmented(AngleLabels,()=>model?.AngleIndex??0,i=>{if(model!=null)model.AngleIndex=i;}).Width(240).Tooltip("Shot angle to the plate normal (keys 1-3).");
        angles.Segmented(SideLabels,()=>model==null||model.WithTilt?0:1,i=>{if(model!=null)model.WithTilt=i==0;}).Width(260).VisibleWhen(()=>model?.AnyTilted==true)
            .Tooltip(()=>model?.TableNote??"");
        var derived=body.Stack(3);
        derived.Label("DERIVED",TextStyle.Section);
        var grid=derived.Row(24);
        var half=(CassetteComposerModel.DerivedLabels.Length+1)/2;
        for(int g=0;g<2;g++)
        {
            var side=grid.Stack(3).Flex();
            for(int i=g*half;i<Math.Min(CassetteComposerModel.DerivedLabels.Length,(g+1)*half);i++)
            {
                var line=i;var r=side.Row(6);
                r.Label(CassetteComposerModel.DerivedLabels[line],TextStyle.Small).Colour("#b8ad94").Width(DerivedLabelWidth);
                r.Label(()=>model?.Derived(line)??"",TextStyle.Small).AlignRight().Flex();
            }
        }
        var table=body.Stack(3);
        table.Label("PROTECTION / selected angle",TextStyle.Section);
        // One aligned table, four threats: the unconditional runtime mean.
        table.Table(()=>ProtectionMetrics)
            .Column("RHA comparison",i=>ProtectionTable.Metrics[i],ColumnWidth.Fill(2f),CellAlign.Left)
            .Column("longRod",i=>ProtectionCell(ProtectionThreat.LongRod,i),ColumnWidth.Fill(),CellAlign.Right)
            .Column("fullBoreKE",i=>ProtectionCell(ProtectionThreat.FullBoreKE,i),ColumnWidth.Fill(),CellAlign.Right)
            .Column("jet",i=>ProtectionCell(ProtectionThreat.Jet,i),ColumnWidth.Fill(),CellAlign.Right)
            .Column("hesh",i=>ProtectionCell(ProtectionThreat.Hesh,i),ColumnWidth.Fill(),CellAlign.Right)
            .Zebra().RowTooltip(i=>i==0?"Equivalent mm RHA along the shot path.":i==1?"Compared with solid RHA of the same thickness.":"Compared with solid RHA of the same weight per square metre.");
        table.Label(ProtectionTable.Legend,TextStyle.Small).Colour("#b8ad94");
        // 0.22.19: the explanation of with/against only shows when a layer is tilted (otherwise there is nothing to explain).
        table.Label(()=>model?.TableNote??"",TextStyle.Small).Wrap().VisibleWhen(()=>!string.IsNullOrEmpty(model?.TableNote));
        // Four mean series stay within the Kit limit.
        var chart=body.Stack(3);
        chart.Label("PROTECTION vs impact angle",TextStyle.Section);
        AngleChart(chart);
        chart.Label(()=>model==null?"":$"Dashed lines: RHA of the same thickness and of the same weight at the selected {CassetteComposerModel.Angles[model.AngleIndex]:0}\u00B0 (angle buttons above). Hover for values.",TextStyle.Small).Colour("#b8ad94").Wrap();
        var a=body.Stack(8);
        a.Label("TARGET / APPLY",TextStyle.Section);
        a.Chip(()=>model?.TargetChip??"No plate selected",ChipStyle.Context);
        a.Warning(()=>model?.TargetWarning??"").Severity(StatusKind.Warning).VisibleWhen(()=>model?.TargetWarning!=null);
        // 0.22.60: a plate in layers: how many faces have a layer, and the estimate from the hit calculation.
        a.Label(()=>model?.LayerLine??"",TextStyle.Small).Wrap().VisibleWhen(()=>!string.IsNullOrEmpty(model?.LayerLine));
        a.Label(()=>model?.ApplyNote??"",TextStyle.Small).Wrap();
        // 0.22.24: To back plane edits the plate shape's thickness directions and values, so the shape should be finished first.
        a.Label("Finish the shape (holes, deleted faces) before To back plane.",TextStyle.Small).Colour("#b8ad94").Wrap().VisibleWhen(()=>model?.ToBackPlane==true)
            .Tooltip("To back plane sets the selected faces (every face without a selection) to Along face normal and gives each corner its thickness. A hole or a face delete after it works on those corner values and can hit the native thickener's limits.");
        a.Label(()=>model?.ApplyScope??"",TextStyle.Small).VisibleWhen(()=>!string.IsNullOrEmpty(model?.ApplyScope))
            .Tooltip("Select faces of the plate in the designer to set only those; without a selection every face is treated. To back plane sets those faces to Along face normal and gives each corner the thickness that reaches one plane using the chosen thickness on every face.");
        var thickness=a.Row(8);
        thickness.Label("Thickness",TextStyle.Small).Width(78);
        thickness.TextField(()=>model?.ThicknessInput??"",v=>model?.SetApplyThickness(v),"mm or %").Width(120).MaxChars(24)
            .Tooltip(()=>model?.ThicknessTooltip??"");
        thickness.Label(()=>model?.ThicknessText??"",TextStyle.Small).Flex();
        var quick=a.Row(8);
        foreach(var percent in new[]{50,75,100,125})
        {
            var value=percent;
            quick.Button(value+" %",()=>model?.SetApplyPercent(value)).Flex().Tooltip(()=>model==null?"":ApplyThickness.Text(model.DepthMm,ApplyThickness.FromPercent(model.DepthMm,value)));
        }
        var mode=a.Row(8);
        mode.Label("Thickness mode",TextStyle.Small);
        mode.Segmented(CassetteComposerModel.ThicknessModeLabels,()=>model?.ThicknessModeIndex??0,i=>{if(model!=null)model.SetThicknessMode(i);})
            .Tooltip(()=>model?.ThicknessTooltip??"");
        a.Warning("Restart Sprocket to use: native registration is not confirmed. Apply is blocked.").Severity(StatusKind.Warning).VisibleWhen(()=>model?.RestartPending==true);
        a.Label("Fill to wall follows the real host wall; curved walls are linear between corners.",TextStyle.Small).Wrap().VisibleWhen(()=>model?.FillToWall==true);
        // 0.22.71: one choice at a time; the button Apply below applies the recipe and then does what is chosen. Choosing changes nothing on the plate.
        var choice=a.Stack(4);
        choice.Label("Apply also does",TextStyle.Small);
        for(int r=0;r<3;r++)
        {
            int from=r*3;var labels=CassetteComposerModel.ApplyChoiceLabels.Skip(from).Take(3).ToArray();
            choice.Segmented(labels,()=>{var chosen=(int)(model?.ApplyChoice??0);return chosen>=from&&chosen<from+3?chosen-from:-1;},i=>model?.SetApplyChoice(from+i)).Tooltip(()=>model?.ApplyChoiceHelp??"");
        }
        choice.Label(()=>model?.ApplyChoiceHelp??"",TextStyle.Small).Wrap().Colour("#b8ad94");
        var actions=c.Footer("Apply");
        var row2=actions.Row(8);
        row2.Button("Save as new version",()=>Save(false),ButtonStyle.Primary).Flex().EnabledWhen(()=>model?.CanSave==true&&panel?.HasErrors!=true)
            .Tooltip("Saves the next version (Ctrl+S). A saved version never changes: plates keep the version they use.");
        actions.Row(8).Button("Save as new cassette",()=>Save(true)).Flex().EnabledWhen(()=>model?.CanSaveAsNew==true&&panel?.HasErrors!=true)
            .Tooltip("Saves under the name as a new cassette family.");
        actions.Row(8).Button("Apply",Apply,ButtonStyle.Primary).Flex().Text(()=>model?.ApplyButton??"Apply to plate").EnabledWhen(()=>model?.CanApply==true)
            .Tooltip(()=>model?.ApplyBlocker??(model?.ApplyChoice==ComposerApplyChoice.ThicknessOnly?"Applies "+(model?.ThicknessText??"")+" in one undoable step. "+(model?.ThicknessTooltip??""):"Applies the recipe, then: "+(model?.ApplyChoiceHelp??"")));
    }

    private static bool CanMeshAction()=>RuntimeComposerFill.Designer&&Valid(out var plate)&&RuntimeCompositePlate.Is(plate);
    /// <summary>0.22.74: a plate that is not composite yet and can be converted (the option then converts it in the same step).</summary>
    private static bool CanConvertMeshAction()=>RuntimeComposerFill.Designer&&Valid(out var plate)&&!RuntimeCompositePlate.Is(plate)&&RuntimeCompositePlateEdit.Eligibility(plate)==null;
    private static void MeshAction(ComposerMeshAction action,string? material=null,bool convert=false)
    {
        if(panel==null||panel.ModalOpen||!(convert?CanConvertMeshAction():CanMeshAction()))return;
        dialogMaterial=material;dialogConvert=convert;
        if(action==ComposerMeshAction.ConformCheek){ConformDialog();return;}
        if(action==ComposerMeshAction.CheekShell){CheekShellDialog();return;}
        try
        {
            var prepared=RuntimeComposerFill.Prepare(tracked!,action,newMaterial:material,convert:convert,diagnosticApply:true);
            if(material!=null)prepared=prepared with{Material=material};
            void Commit()
            {
                var ok=RuntimeCompositePlateEdit.TryPrepared(tracked!,prepared,out var message);
                panel?.SetStatus(message.StartsWith(prepared.Plan.Label+":",StringComparison.Ordinal)?message:prepared.Plan.Label+": "+message,ok&&prepared.Plan.FollowWall?.Warning!=true?StatusKind.Success:StatusKind.Warning);
                if(ok){model?.Invalidate();nextTarget=0;tracked?.RequestRedraw();}
            }
            if(prepared.Plan.NeedsConfirmation)
            {
                string reserve=prepared.ReserveMm.ToString(System.Globalization.CultureInfo.InvariantCulture),error="";
                var p=panel;
                p.ShowModal("Fill to wall - check before applying",m=>
                {
                    m.Label(prepared.Plan.FillInspection,TextStyle.Body).Wrap();
                    m.Label("Thickness without a wall (mm)",TextStyle.Small);
                    m.TextField(()=>reserve,v=>reserve=v,"mm").Width(140).MaxChars(16);
                    m.Label(()=>error,TextStyle.Small).Wrap();
                    var row=m.Row(8);row.Spacer();row.Button("Back",p.CloseModal);
                    row.Button("Apply",()=>
                    {
                        if(!double.TryParse(reserve,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var mm)){error="Enter a thickness in mm.";return;}
                        try{prepared=prepared with{Plan=prepared.Plan.Accept(mm)};}
                        catch(Exception ex){error=ex.Message;return;}
                        p.CloseModal();Commit();
                    },ButtonStyle.Primary);
                });
            }
            else if(action==ComposerMeshAction.CheekPreset)panel.Confirm("T-80U preset - inspect before applying",prepared.Inspection,"Apply",Commit,ButtonStyle.Primary);
            else if(action==ComposerMeshAction.BackToBox)
            {
                var p=panel;
                p.ShowModal("Back to box - inspect before applying",m=>
                {
                    m.Label(prepared.Inspection,TextStyle.Body).Wrap();
                    var row=m.Row(8);row.Spacer();row.Button("Back",p.CloseModal);
                    row.Button("Apply",()=>{p.CloseModal();Commit();},ButtonStyle.Primary);
                });
            }
            else if(action==ComposerMeshAction.LayerConvert)
            {
                // 0.22.60: the window of Convert to layers; Back changes nothing.
                var p=panel;
                p.ShowModal(LayerFaces.Title,m=>
                {
                    m.Label(prepared.Inspection,TextStyle.Body).Wrap();
                    var row=m.Row(8);row.Spacer();row.Button("Back",p.CloseModal);
                    row.Button("Apply",()=>{p.CloseModal();Commit();},ButtonStyle.Primary);
                });
            }
            else Commit();
        }
        catch(Exception ex){panel.SetStatus(ComposerFill.Label(action)+": "+ex.Message,StatusKind.Warning);}
    }

    /// <summary>0.22.71: the recipe that Apply applies together with the option of a window (set by MeshAction).</summary>
    private static string? dialogMaterial;
    private static bool dialogConvert;
    private static void ConformDialog()
    {
        if(panel==null||tracked==null)return;
        var p=panel;var editor=tracked;PreparedComposerFill? prepared=null;
        string distance=RuntimeCompositePlate.CheekLimits.MaximumMm.ToString(System.Globalization.CultureInfo.InvariantCulture),error="",inspection="Choose the distance behind the inner wall.";bool valid=false;
        try
        {
            var plate=editor.Component;
            if(PlateMeshAccess.Read(plate,"Conform cheek fill dialog",SnapshotUse.Fresh)==null)throw new InvalidOperationException(PlateMeshAccess.Busy);
            var platePointer=plate.Pointer;var meshPointer=plate.Mesh.Pointer;var beforeJson=plate.Mesh.ToJson(true);
            void Replan(string value)
            {
                distance=value;valid=false;prepared=null;
                if(!double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var mm)){error="Enter a wall distance in mm.";return;}
                try
                {
                    if(!RuntimeComposerFill.Designer||!editor.IsValid()||editor.Component.Pointer!=platePointer||editor.Component.Mesh.Pointer!=meshPointer||editor.Component.Mesh.ToJson(true)!=beforeJson)
                        throw new InvalidOperationException("The plate changed; go Back and open Conform cheek fill again.");
                    prepared=RuntimeComposerFill.Prepare(editor,ComposerMeshAction.ConformCheek,mm,newMaterial:dialogMaterial,convert:dialogConvert);if(dialogMaterial!=null)prepared=prepared with{Material=dialogMaterial};inspection=prepared.Inspection;error="";valid=true;
                }
                catch(Exception ex){error=ex.Message;}
            }
            Replan(distance); // An invalid default still opens the window so a smaller X can be tried.
            p.ShowModal("Conform cheek fill - inspect before applying",m=>
            {
                m.Label($"Distance behind the inner wall (mm), maximum {RuntimeCompositePlate.CheekLimits.MaximumMm:0.#}",TextStyle.Small).Wrap();
                m.TextField(()=>distance,Replan,"mm").OnChange(Replan).Width(140).MaxChars(16);
                m.Label(()=>inspection,TextStyle.Body).Wrap();m.Label(()=>error,TextStyle.Small).Wrap();
                var row=m.Row(8);row.Spacer();row.Button("Back",p.CloseModal);
                row.Button("Apply",()=>
                {
                    if(!valid||prepared==null)return;
                    p.CloseModal();var ok=RuntimeCompositePlateEdit.TryPrepared(editor,prepared,out var message);
                    p.SetStatus(message.StartsWith(prepared.Plan.Label+":",StringComparison.Ordinal)?message:prepared.Plan.Label+": "+message,ok?StatusKind.Success:StatusKind.Warning);
                    if(ok){model?.Invalidate();nextTarget=0;editor.RequestRedraw();}
                },ButtonStyle.Primary).EnabledWhen(()=>valid);
            });
        }
        catch(Exception ex){p.SetStatus("Conform cheek fill: "+ex.Message,StatusKind.Warning);}
    }

    // 0.22.57: two thicknesses, volume and mass, the shortened-corner line and the overlap warning; Back changes nothing. If the game refuses the
    // ribs (nothing changed, no undo step), the window says so and the next Apply uses separate blocks on the face normal.
    private static void CheekShellDialog()
    {
        if(panel==null||tracked==null)return;
        var p=panel;var editor=tracked;PreparedComposerFill? prepared=null;var limits=RuntimeCompositePlate.CheekLimits;double shellMaximum=RuntimeCompositePlate.CheekShellMaximumMm;
        // 0.22.59: own thickness maximum for this button; the rear limit starts at the config value (shown in mm) and applies to this Apply only.
        string gun=CheekShellFill.Invariant(Math.Min(450,shellMaximum)),outer=CheekShellFill.Invariant(Math.Min(200,shellMaximum)),rear=CheekShellFill.Invariant(Math.Round(limits.RearBoundaryM*1000,3)),error="",inspection="Choose the thickness at the gun and at the outer edge.";
        bool valid=false,fallback=false;
        try
        {
            var plate=editor.Component;
            if(PlateMeshAccess.Read(plate,"Cheek fill (shell) dialog",SnapshotUse.Fresh)==null)throw new InvalidOperationException(PlateMeshAccess.Busy);
            var platePointer=plate.Pointer;var meshPointer=plate.Mesh.Pointer;var beforeJson=plate.Mesh.ToJson(true);
            void Replan()
            {
                valid=false;prepared=null;
                if(!double.TryParse(gun,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var g)||!double.TryParse(outer,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var o)||!double.TryParse(rear,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var r)){error="Enter both thicknesses and the rear limit in mm.";return;}
                try
                {
                    if(!RuntimeComposerFill.Designer||!editor.IsValid()||editor.Component.Pointer!=platePointer||editor.Component.Mesh.Pointer!=meshPointer||editor.Component.Mesh.ToJson(true)!=beforeJson)
                        throw new InvalidOperationException("The plate changed; go Back and open Cheek fill (shell) again.");
                    prepared=RuntimeComposerFill.Prepare(editor,ComposerMeshAction.CheekShell,null,new(g,o,fallback,r),dialogMaterial,dialogConvert);if(dialogMaterial!=null)prepared=prepared with{Material=dialogMaterial};inspection=prepared.Inspection;error=fallback?CheekShellFill.FallbackNote:"";valid=true;
                }
                catch(Exception ex){error=ex.Message;}
            }
            Replan();
            p.ShowModal("Cheek fill (shell) - inspect before applying",m=>
            {
                m.Label($"Thickness at the gun (mm), 3 to {shellMaximum:0.#}",TextStyle.Small).Wrap();
                m.TextField(()=>gun,v=>{gun=v;Replan();},"mm").OnChange(v=>{gun=v;Replan();}).Width(140).MaxChars(16);
                m.Label($"Thickness at the outer edge (mm), 3 to {shellMaximum:0.#}",TextStyle.Small).Wrap();
                m.TextField(()=>outer,v=>{outer=v;Replan();},"mm").OnChange(v=>{outer=v;Replan();}).Width(140).MaxChars(16);
                const string rearTip="How far in front of the turret centre the filling must stop. Lower it for a deeper filling; keep room for the crew and the gun.";
                m.Label("Rear limit (mm in front of the turret centre)",TextStyle.Small).Wrap().Tooltip(rearTip);
                m.TextField(()=>rear,v=>{rear=v;Replan();},"mm").OnChange(v=>{rear=v;Replan();}).Width(140).MaxChars(16).Tooltip(rearTip);
                m.Label(()=>inspection,TextStyle.Body).Wrap();m.Label(()=>error,TextStyle.Small).Wrap();
                var row=m.Row(8);row.Spacer();row.Button("Back",p.CloseModal);
                row.Button("Apply",()=>
                {
                    if(!valid||prepared==null)return;
                    var ok=RuntimeCompositePlateEdit.TryPrepared(editor,prepared,out var message);
                    if(!ok&&!fallback&&message.Contains(CheekShellFill.LinkedBackMarker,StringComparison.Ordinal))
                    {
                        // The copy check refused the ribs before the mesh was replaced: nothing changed. Offer separate blocks; the user presses Apply again.
                        fallback=true;Replan();return;
                    }
                    p.CloseModal();
                    p.SetStatus(message.StartsWith(prepared.Plan.Label+":",StringComparison.Ordinal)?message:prepared.Plan.Label+": "+message,ok?StatusKind.Success:StatusKind.Warning);
                    if(ok){model?.Invalidate();nextTarget=0;editor.RequestRedraw();}
                },ButtonStyle.Primary).EnabledWhen(()=>valid);
            });
        }
        catch(Exception ex){p.SetStatus("Cheek fill (shell): "+ex.Message,StatusKind.Warning);}
    }

    private const float DerivedLabelWidth=84;
    private static readonly string KeHex="#79d8e8",HeatHex="#c8a3f3",FullBoreHex="#8fd99c",HeshHex="#f0c75e";

    private static readonly int[] ProtectionMetrics={0,1,2};
    private static string ProtectionCell(ProtectionThreat threat,int metric)
    {
        if(model==null)return "";
        var row=model.ProtectionRows(threat)[metric];
        return row.With;
    }

    // The Kit polls the series getters every frame; the lists are rebuilt once per evaluated draft (the model's own cache), never per frame.
    private static (CassetteComposerModel? Model,int Stamp,List<UVec2>[] Lists) angleCache=(null,-1,Array.Empty<List<UVec2>>());
    // 0.22.28: one list per threat of the game's table and side (threat*2 + against).
    private static IReadOnlyList<UVec2> AnglePoints(ProtectionThreat threat,bool withTilt)
    {
        var m=model;if(m==null)return Array.Empty<UVec2>();
        var stamp=m.EvalStamp;
        if(!ReferenceEquals(angleCache.Model,m)||angleCache.Stamp!=stamp)
        {
            var lists=new List<UVec2>[ProtectionFactorTable.Threats.Length*2];
            for(int i=0;i<lists.Length;i++)lists[i]=m.CurveSeries((ProtectionThreat)(i/2),i%2==0).Select(p=>new UVec2((float)p.Angle,(float)p.Mm)).ToList();
            angleCache=(m,stamp,lists);
        }
        return angleCache.Lists[(int)threat*2+(withTilt?0:1)];
    }
    private static LineChart AngleChart(Stack parent)
    {
        return parent.LineChart(240)
            .Series("longRod mean (game)",()=>AnglePoints(ProtectionThreat.LongRod,true),UiKit.Colour(KeHex))
            .Series("fullBoreKE mean (game)",()=>AnglePoints(ProtectionThreat.FullBoreKE,true),UiKit.Colour(FullBoreHex))
            .Series("jet mean (game)",()=>AnglePoints(ProtectionThreat.Jet,true),UiKit.Colour(HeatHex))
            .Series("hesh mean (game)",()=>AnglePoints(ProtectionThreat.Hesh,true),UiKit.Colour(HeshHex))
            .XAxis("Impact angle","deg",min:0,max:85,tick:5).YAxis("Protection","mm RHA",0)
            .RefLine("RHA, same thickness",()=>(float)(model?.RhaReference(1)??double.NaN))
            .RefLine("RHA, same weight",()=>(float)(model?.RhaReference(2)??double.NaN),UiKit.Colour("#b8ad94"))
            .Legend().Empty("No protection data yet.");
    }

    // ---------------- drawing (only when DiagramVersion changes or the canvas is resized) ----------------
    /// <summary>The whole cross-section lives in <see cref="CassetteSectionDrawing"/>; this only maps it onto the kit painter.</summary>
    private static void DrawSection(Painter d)
    {
        var size=d.Size;
        CassetteSectionDrawing.Draw(new PainterSink(d),model??throw new InvalidOperationException("Composer model missing."),size.x,size.y);
    }
    private sealed class PainterSink:ISectionSink
    {
        private readonly Painter p;
        internal PainterSink(Painter painter){p=painter;}
        public void ViewBox(float width,float height)=>p.ViewBox(0,0,width,height);
        public void BeginClip(float width,float height){p.Push(0,0);p.Clip(0,0,width,height);}
        public void EndClip()=>p.Pop();
        public void Poly(float[] xy,string fill,string? stroke,float strokePx)=>p.Poly(xy,fill,stroke,strokePx);
        public void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx)=>p.Hatch(xy,colour,angleDeg,spacingPx,widthPx);
        public void Outline(float[] xy,string colour,float widthPx)=>p.Polyline(Points(xy),UiKit.Colour(colour),widthPx,0,true);
        public void Line(float x0,float y0,float x1,float y1,string colour,float widthPx)=>p.Line(x0,y0,x1,y1,colour,widthPx);
        public void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx)=>p.Arrow(x0,y0,x1,y1,colour,widthPx,headPx);
        public void Region(string id,float[] xy)=>p.Region(id,xy);
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background)=>
            p.Text(x,y,text,TextStyle.Small,colour,anchor switch{SectionAnchor.MiddleLeft=>UnityEngine.TextAnchor.MiddleLeft,SectionAnchor.MiddleRight=>UnityEngine.TextAnchor.MiddleRight,_=>UnityEngine.TextAnchor.MiddleCenter},0,background);
    }
    private static List<UnityEngine.Vector2> Points(float[] xy){var list=new List<UnityEngine.Vector2>(xy.Length/2);for(int i=0;i+1<xy.Length;i+=2)list.Add(new(xy[i],xy[i+1]));return list;}

    // ---------------- actions ----------------
    private static void Save(bool asNew)
    {
        if(model==null||panel==null||panel.ModalOpen)return;
        if(asNew?!model.CanSaveAsNew:!model.CanSave){panel.SetStatus(model.Issues.FirstOrDefault(i=>i.Error)?.Text??"Nothing to save.",StatusKind.Warning);return;}
        try
        {
            var result=model.Save(asNew);
            var status=result.Status;
            var blocked=result.RestartRequired;
            if(Valid(out _))
            {
                var reload=RuntimeCassetteComposer.Reload(tracked!,new CassetteSaveResult(result.Preset,result.RestartRequired,result.Status));
                model.RegistrationCompleted(reload);status=reload.Status;blocked=reload.RestartRequired;
            }
            else{status="Saved "+result.Preset.Label+". Restart Sprocket to use; no target vehicle to verify native registration.";Plugin.ModLog.LogInfo("[Cassettes] registered "+result.Preset.Id+" live: no");}
            model.Invalidate();
            panel.SetStatus(status,blocked?StatusKind.Warning:StatusKind.Success);
            if(blocked)panel.Confirm("Cassette saved",status,"OK",()=>{});
            Plugin.ModLog.LogInfo("[Cassette composer] "+status);
        }
        catch(Exception ex){panel.SetStatus("Not saved: "+ex.Message,StatusKind.Error);Warn("Save failed: "+ex.Message);}
    }
    private static void Apply()
    {
        if(model==null||panel==null)return;
        if(!model.CanApply){panel.SetStatus(model.ApplyBlocker??"",StatusKind.Warning);return;}
        if(!Valid(out _)){panel.SetStatus("Select a plate in the designer.",StatusKind.Warning);return;}
        var choice=model.ApplyChoice;
        // 0.22.71: the button Apply applies the recipe and then does what is chosen above; Back to box needs no recipe.
        if(choice==ComposerApplyChoice.BackToBox){MeshAction(ComposerMeshAction.BackToBox);return;}
        if(choice!=ComposerApplyChoice.ThicknessOnly)
        {
            // 0.22.74: the recipe is the saved preset or, with none open, the plate's own cassette. A plate that is not composite is converted in the same step (one undo): Prepare/TryPrepared do it.
            if(model.ApplyRecipeId is not {} recipe)return;
            var action=CassetteComposerModel.MeshActionOf(choice)!.Value;
            if(model.Converts)
            {
                if(!CanConvertMeshAction()){panel.SetStatus("Select an add-on plate in the vehicle designer.",StatusKind.Warning);return;}
                MeshAction(action,recipe,true);return;
            }
            if(!CanMeshAction()){panel.SetStatus("Select a composite plate in the vehicle designer.",StatusKind.Warning);return;}
            MeshAction(action,recipe);return;
        }
        if(model.ApplyPreset is not {} preset)return;
        // 0.22.14: an ordinary add-on plate is converted and gets the preset in the same step, after a confirmation.
        if(model.Converts){panel.Confirm("Convert to composite plate",model.ConvertConfirm,"Convert",()=>Run(preset,true),ButtonStyle.Primary);return;}
        if(model.FillToWall){MeshAction(ComposerMeshAction.Fill,preset.MaterialId);return;}
        Run(preset,false);
    }
    private static void Run(CassettePreset preset,bool convert)
    {
        if(model==null||panel==null)return;
        if(!Valid(out _)){panel.SetStatus("Select a plate in the designer.",StatusKind.Warning);return;}
        double? depth=model.ApplyDepthMm;
        var mode=(ThicknessMode)model.ThicknessModeIndex;
        // 0.22.63: a plate that is a box keeps its shape when it is converted: the plate gets the recipe only, no thickness (no depth, so no face sticks out).
        bool boxKept=convert&&mode!=ThicknessMode.FillToWall&&RuntimeLayerFaces.IsBoxPlate(tracked!.Component);
        if(boxKept){depth=null;mode=ThicknessMode.Uniform;}
        // The same single undoable CreateOp as the inspector's "Reset thickness to cassette depth".
        var ok=convert?RuntimeCompositePlateEdit.TryConvert(tracked!,preset.MaterialId,depth,mode,out var message):RuntimeCompositePlateEdit.TryApply(tracked!,preset.MaterialId,depth,mode,out message);
        try{tracked?.RequestRedraw();}catch(Exception){}
        if(ok){used.Add(preset.MaterialId);model.Invalidate();nextTarget=0;}
        if(convert)Plugin.ModLog.LogInfo("[Composite plate] Convert to composite plate with "+preset.MaterialId+": "+(ok?"done":"refused")+" - "+message);
        panel.SetStatus(ok?(boxKept?$"{preset.Label} applied; the shape of the box is unchanged: {message}":$"{preset.Label} applied at {model.ThicknessText}: {message}"):message,ok?StatusKind.Success:StatusKind.Error);
    }
    private static void FromSelection()
    {
        if(model==null||panel==null)return;
        if(!Valid(out _)){panel.SetStatus("Select the reference plate in the designer first.",StatusKind.Warning);return;}
        try
        {
            var date=model.DateError==null?model.Date:"1975.01.01";
            var preset=RuntimeCassetteComposer.FromSelection(tracked!,"selection","From selection",date);
            model.LoadDraft(preset,ComposerSource.Selection,$"From selection: {preset.Layers.Count(l=>l.Material!="air")} plates as {preset.Layers.Length} layers. Save to keep it.");
            Status(StatusKind.Success);
        }
        catch(Exception ex){panel.SetStatus("From selection: "+ex.Message,StatusKind.Error);}
    }
    private static void Export()
    {
        if(model==null||panel==null)return;
        try{model.Export(ShareDirectory);Status(StatusKind.Success);}
        catch(Exception ex){panel.SetStatus("Export failed: "+ex.Message,StatusKind.Error);}
    }
    // TEMP: vervalt bij Kit 0.1.3 (file picker): a modal lists the share folder instead of a file dialog.
    private static void ImportDialog()
    {
        if(panel==null)return;
        var p=panel;
        p.ShowModal("Import cassette",m=>
        {
            var files=Directory.Exists(ShareDirectory)?Directory.GetFiles(ShareDirectory,"*.cassette.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(10).ToArray():Array.Empty<string>();
            m.Label(files.Length==0?$"No .cassette.json files in {ShareDirectory}. Put a share file there.":"Share files in "+ShareDirectory+":",TextStyle.Small).Wrap();
            foreach(var file in files){var path=file;m.Button(Path.GetFileName(path),()=>{p.CloseModal();Import(path);}).Tooltip(path);}
            var row=m.Row(8);row.Spacer();row.Button("Close",p.CloseModal);
        });
    }
    private static void Import(string path)
    {
        if(model==null||panel==null)return;
        try{model.Import(path);Status(StatusKind.Success);}
        catch(Exception ex){panel.SetStatus("Import failed: "+ex.Message,StatusKind.Error);}
    }
}
