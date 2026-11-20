using System.Globalization;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

internal sealed record ComposerMaterial(string Id,string Label,string Group);
internal enum ComposerSource{Empty,Historical,Own,Legacy,Selection,Import}
/// <summary><paramref name="Available"/> false (0.22.7): the target vehicle cannot use it yet; <paramref name="Reason"/> is the short tag ("From 1985").</summary>
internal sealed record ComposerPresetRow(string Key,string Title,string Subtitle,string? Tag,ComposerSource Kind,bool Available=true,string? Reason=null,string Location=CassettePreset.Turret);
internal sealed record ComposerLayerRow(int Index,string Title,string Subtitle,string Tag,string Value,string Swatch,bool Error,bool Warning,string? Note=null);
/// <summary>Detail: the message without the "Layer n" prefix (the short note on the layer row).</summary>
internal sealed record ComposerIssue(string Text,bool Error,int Layer,string? FixLabel=null,string? Detail=null);
/// <summary>The designer plate the composer applies to (refreshed by the panel, compared by value).</summary>
/// <remarks>0.22.7: <paramref name="VehicleDateKey"/> is the owning vehicle's native TechDate (year*10000+month*100+day); <paramref name="MaterialKnown"/> is the native Tech.TryGetTech answer for the plate's current material (null: not asked).
/// 0.22.14: <paramref name="ConvertBlocker"/> is null when an ordinary plate can be converted (<see cref="CompositeConversion.Blocker"/>; the default means "not read");
/// <paramref name="LinkedCopies"/> counts Alt duplicates sharing its mesh; <paramref name="SelectedFaces"/> counts the faces selected in the designer (0: none).</remarks>
internal sealed record ComposerTarget(string Name,int Faces,string MaterialId,string MaterialLabel,bool Composite,double MinFaceMm,double MaxFaceMm,int? VehicleDateKey=null,bool? MaterialKnown=null,
    string? ConvertBlocker=CompositeConversion.NotRead,int LinkedCopies=0,int SelectedFaces=0,string? LayerLine=null,string? FrontNote=null,bool SolidMark=false,SolidTargetInfo? Solid=null);
/// <summary>0.22.82: the plate as a closed shape, read by the panel from the gated snapshot (compared by value): closed pieces and volume, or the refusal.</summary>
internal readonly record struct SolidTargetInfo(int Pieces,double VolumeM3,string? Refusal,int ThickFaces=0,int MaxThicknessMm=0)
{
    internal static SolidTargetInfo Of(SolidShapeResult s,(int Faces,int MaxMm) thick=default)=>new(s.Closed?s.Pieces:0,s.Closed?s.VolumeM3:0,s.Refusal,thick.Faces,thick.MaxMm);
}
internal sealed record ComposerSaveResult(CassettePreset Preset,bool RestartRequired,string Status);
/// <summary>0.22.71: what the button Apply does on the plate besides the recipe: one choice at a time (Thickness only = what Apply did before). 0.22.82: Solid block.</summary>
internal enum ComposerApplyChoice{ThicknessOnly,Flip,Inward,FillToWall,CheekPreset,ConformCheek,CheekShell,ConvertToLayers,BackToBox,SolidBlock}

/// <summary>One band of the cross-section in diagram units (612×250 view box).</summary>
internal sealed record SectionBand(int Layer,string Material,bool Air,double TiltDegrees,double DepthMm,float X0,float X1,float[] Polygon,string Colour);
internal sealed record CassetteSectionGeometry(double DepthMm,double AngleDegrees,double Scale,float FrontX,float BackX,float ShotY,SectionBand[] Bands)
{
    internal double PathMm=>DepthMm/Math.Cos(AngleDegrees*Math.PI/180);
    internal float PathPx=>BackX-FrontX;
}

/// <summary>
/// Cross-section to scale (COMPOSER.md "Diagram content"): the cassette turns by the shot angle so the
/// shot is horizontal; depth_i = t_i / cos tilt_i (air: t_i). The scale is fixed per draft: the path at
/// the steepest selectable angle (60°) fits 480 px, so changing the angle never rescales.
/// </summary>
internal static class CassetteSection
{
    internal const float Width=612,Height=250,FitPx=480;
    internal const double SteepestDegrees=60;
    internal static double Depth(CassetteLayer l)=>l.Material=="air"?l.ThicknessMm:l.ThicknessMm/Math.Cos(l.TiltDegrees*Math.PI/180);
    /// <summary>Depth for drawing only: finite and non-negative, so a bad layer (NaN, 90° tilt) can never poison the geometry.</summary>
    private static double DrawDepth(CassetteLayer l){var d=Depth(l);return double.IsFinite(d)&&d>0?Math.Min(d,1e7):0;}
    /// <summary>Pixels per millimetre for a cassette of depth D (the steepest path fits <paramref name="fitPx"/> px).</summary>
    internal static double Scale(double depthMm,double fitPx=FitPx)=>depthMm>0&&double.IsFinite(depthMm)?fitPx*Math.Cos(SteepestDegrees*Math.PI/180)/depthMm:0;
    private static float Coord(double v)=>double.IsFinite(v)?(float)Math.Clamp(v,-1e5,1e5):0;
    /// <summary>
    /// Geometry for a canvas of <paramref name="width"/> x <paramref name="height"/> (default: the 612 x 250 design box).
    /// The drawing is guarded: non-finite input gives a finite (possibly empty) drawing, the shot angle is clamped to
    /// 0-80 degrees so cos never reaches 0, and every coordinate is clamped.
    /// </summary>
    internal static CassetteSectionGeometry Build(IReadOnlyList<CassetteLayer> layers,double angleDegrees,float width=Width,float height=Height)
    {
        if(!float.IsFinite(width)||width<8)width=Width;if(!float.IsFinite(height)||height<8)height=Height;
        width=Math.Min(width,8192);height=Math.Min(height,8192);
        if(!double.IsFinite(angleDegrees))angleDegrees=0;angleDegrees=Math.Clamp(angleDegrees,0,80);
        double depth=0;foreach(var l in layers)depth+=DrawDepth(l);
        var scale=Scale(depth,FitPx*width/Width);var a=angleDegrees*Math.PI/180;var cos=Math.Cos(a);
        float shotY=height/2,front=Coord((width-depth/cos*scale)/2);
        // Layer boundaries are perpendicular to the plate normal, which is turned by the angle.
        float dx=(float)-Math.Sin(a),dy=(float)cos,reach=height;
        var bands=new SectionBand[layers.Count];double s=0;
        for(int i=0;i<layers.Count;i++)
        {
            var l=layers[i];var d=DrawDepth(l);
            float x0=Coord(front+s/cos*scale),x1=Coord(front+(s+d)/cos*scale);s+=d;
            var poly=new[]{x0-dx*reach,shotY-dy*reach,x1-dx*reach,shotY-dy*reach,x1+dx*reach,shotY+dy*reach,x0+dx*reach,shotY+dy*reach};
            bands[i]=new(i,l.Material,l.Material=="air",l.TiltDegrees,d,x0,x1,poly,CassettePalette.Hex(l.Material));
        }
        return new(depth,angleDegrees,scale,front,Coord(front+depth/cos*scale),shotY,bands);
    }
    /// <summary>Scale bar length in mm that draws 40–160 px.</summary>
    internal static double ScaleBarMm(double scale)
    {
        foreach(var mm in new[]{10d,20,50,100,200,500,1000})if(mm*scale>=40)return mm;
        return 1000;
    }
}

/// <summary>
/// View model of the cassette composer (pure; tests drive it without Unity or the kit). It owns the
/// draft (layers, name, date), forks read-only presets on the first edit, keeps 20 undo steps, and caches
/// everything the panel polls per frame per <see cref="Version"/>. <see cref="CassetteComposer.Validate"/>
/// and <see cref="CassetteComposer.Preview"/> run once per change, 150 ms after the last edit.
/// </summary>
internal sealed class CassetteComposerModel
{
    internal const int MaxLayers=64,UndoSteps=20;
    internal const double DebounceSeconds=.15,DefaultLegacyMm=100;
    internal static readonly double[] Angles={0,30,60};
    internal static readonly string[] Directions={"up","down","left","right"};
    internal static readonly string[] DerivedLabels={"Depth D","Window","Mass","Density","Native rha","Cost","Spall source","LOS thickness"};

    private readonly CassetteComposer? backend;
    private readonly Func<IReadOnlyList<CassettePreset>> readCatalogue;
    private readonly Func<string,bool> inUse,nativeAvailable;
    private readonly Func<double> clock;
    private readonly Func<string,DateTime?> materialDate;
    private readonly Dictionary<string,ComposerMaterial> byId;
    internal IReadOnlyList<ComposerMaterial> Materials{get;}
    internal IReadOnlyList<ComposerMaterial> Legacy{get;}
    internal bool Writable{get;}
    internal IReadOnlyList<CassettePreset> Catalogue{get;private set;}

    // ---- draft ----
    private List<CassetteLayer> layers=new();
    private string label="New cassette",date="1975.01.01",baseline="",location=CassettePreset.Turret;
    // 0.22.7: the saved date follows the newest layer material until the user types a date of their own.
    private bool dateManual;
    private CassettePreset? origin;private bool forked;
    internal ComposerSource Source{get;private set;}=ComposerSource.Empty;
    internal string? SelectedPresetKey{get;private set;}
    private readonly List<(CassetteLayer[] Layers,string Label,string Date,int Selected,bool Forked,bool DateManual,int[] Picked)> undo=new();
    private int selected=-1;
    // 0.22.71: several layers can be chosen at once. selected stays the layer last chosen (the one the single-layer fields were built for); picked holds every chosen layer, selected included.
    private SortedSet<int> picked=new();private int anchor=-1;
    private void Choose(int index){selected=index;picked.Clear();if(index>=0)picked.Add(index);anchor=index;}
    internal int Version{get;private set;}
    internal double LastChange{get;private set;}=double.NegativeInfinity;
    internal string? Status{get;private set;}
    internal bool RestartNotice{get;private set;}

    // ---- view state ----
    private int tab,angle;private bool withTilt=true;private string search="",filter=CassettePreset.Turret;
    // 0.22.16.1 fork: composer choice, not part of the saved draft.
    private int thicknessMode;
    internal static readonly string[] ThicknessModeLabels={"Uniform","To back plane"};
    internal int ThicknessModeIndex=>thicknessMode;
    internal bool ToBackPlane=>thicknessMode==1;
    internal bool FillToWall=>thicknessMode==2;
    internal void SetThicknessMode(int index){if(index is <0 or >2||index==thicknessMode)return;thicknessMode=index;Version++;}
    private double applyPercent=100;
    internal double ApplyDepthMm=>DepthMm>0?ApplyThickness.FromPercent(DepthMm,applyPercent):0;
    internal double ApplyScale=>DepthMm>0?ApplyDepthMm/DepthMm:1;
    internal string ThicknessInput=>N(ApplyDepthMm,"0.##")+" mm";
    internal string ThicknessText=>DepthMm>0?ApplyThickness.Text(DepthMm,ApplyDepthMm):"Pick a preset first.";
    internal string ThicknessTooltip=>"Fill to wall: inward rays to the host wall, with configured clearance, cap and reserve; corner interpolation is linear. Uniform: every treated face gets "+ThicknessText+". To back plane: one plane this far behind the frontmost point; corner thickness is clamped to 1 mm-2x the chosen thickness. Enter mm or % of preset D (10-300 %, minimum 1 mm). Native thickness uses whole millimetres.";
    internal void SetApplyPercent(double percent)
    {
        if(DepthMm<=0||!double.IsFinite(percent))return;
        var next=ApplyThickness.FromPercent(DepthMm,percent)/DepthMm*100;
        if(next==applyPercent)return;applyPercent=next;Version++;Refresh(true);
    }
    internal void SetApplyThickness(string text)
    {
        if(ApplyThickness.TryParse(text,DepthMm,out var mm))SetApplyPercent(mm/DepthMm*100);
    }
    private ComposerTarget? target;
    internal int TargetVersion{get;private set;}
    /// <summary>
    /// Native "is this material in the target vehicle's technology frame" (Tech.TryGetTech), asked per preset when the
    /// list is built; null result or no delegate: unknown, nothing is dimmed for that reason.
    /// </summary>
    internal Func<string,bool?>? FrameCheck{get;set;}

    internal CassetteComposerModel(CassetteComposer? backend,IReadOnlyList<ComposerMaterial> materials,IReadOnlyList<ComposerMaterial> legacy,
        Func<IReadOnlyList<CassettePreset>> readCatalogue,Func<string,bool>? inUse=null,Func<string,bool>? nativeAvailable=null,bool writable=true,Func<double>? clock=null,
        Func<string,DateTime?>? materialDate=null)
    {
        this.backend=backend;this.readCatalogue=readCatalogue;
        this.inUse=inUse??(_=>false);this.nativeAvailable=nativeAvailable??(_=>true);
        this.clock=clock??(()=>Environment.TickCount64/1000d);this.materialDate=materialDate??(_=>null);
        Materials=materials.OrderBy(m=>CassettePalette.GroupIndex(m.Group)).ThenBy(m=>m.Label,StringComparer.OrdinalIgnoreCase).ToArray();
        byId=Materials.GroupBy(m=>m.Id,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
        Legacy=legacy;Writable=writable&&backend!=null;
        Catalogue=Safe(readCatalogue);
        baseline=Signature();
    }
    private static IReadOnlyList<CassettePreset> Safe(Func<IReadOnlyList<CassettePreset>> read){try{return read();}catch(Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException){return Array.Empty<CassettePreset>();}}

    // ---------------- identity ----------------
    internal string Label=>label;
    /// <summary>The date a save writes (0.22.7): the newest layer material, unless a later date was typed.</summary>
    internal string Date=>EffectiveDate;
    /// <summary>"preset" location of the draft: turret or hull (metadata only).</summary>
    internal string DraftLocation=>location;
    internal IReadOnlyList<CassetteLayer> Layers=>layers;
    internal int SelectedLayer=>selected>=0&&selected<layers.Count?selected:-1;
    internal CassettePreset? Origin=>origin;
    internal bool Forked=>forked;
    /// <summary>An unchanged historical or own preset: editing forks it.</summary>
    internal bool ReadOnly=>origin!=null&&!forked&&Source is ComposerSource.Historical or ComposerSource.Own;
    /// <summary>Unsaved work: a forked preset that differs from its origin, or any non-empty draft without a saved origin.</summary>
    internal bool Dirty{get{if(dirty.Version!=Version)dirty=(Version,origin!=null?forked&&Signature()!=baseline:layers.Count>0);return dirty.Value;}}
    private (int Version,bool Value) dirty=(-1,false);
    private string Signature()=>label+"|"+EffectiveDate+"|"+location+"|"+string.Join(";",layers.Select(l=>FormattableString.Invariant($"{l.Material},{l.ThicknessMm},{l.TiltDegrees},{l.TiltDirection}")));
    /// <summary>Numbers always with a decimal point (the game and kit text use invariant formatting).</summary>
    private static string N(double v,string format)=>v.ToString(format,CultureInfo.InvariantCulture);
    internal static string Slug(string text)
    {
        var slug=Regex.Replace((text??"").ToLowerInvariant(),"^user_","");slug=Regex.Replace(slug,"_v[0-9]+$","");
        slug=Regex.Replace(slug,"[^a-z0-9_-]+","_").Trim('_','-');
        if(slug.Length==0)slug="cassette";if(slug.Length>32)slug=slug[..32].Trim('_','-');
        return slug.Length==0?"cassette":slug;
    }
    /// <summary>Id the next save uses: a fork keeps its family (next version), anything else takes the name.</summary>
    internal string SaveId(bool asNew)=>!asNew&&origin!=null&&Source is ComposerSource.Historical or ComposerSource.Own?Slug(origin.Id):Slug(label);
    internal (double Min,double Max) Window
    {
        get
        {
            if(origin!=null&&!forked)return (origin.MinimumDepthMm,origin.MaximumDepthMm);
            var d=DepthMm;return d>0?(Math.Max(1,d*.75),Math.Min(4000,d*1.25)):(1,4000);
        }
    }
    internal double DepthMm{get{double d=0;foreach(var l in layers)d+=CassetteSection.Depth(l);return d;}}
    /// <summary>The draft as a catalogue preset (identity and window as the next save would write them).</summary>
    internal CassettePreset Draft(bool asNew=false)
    {
        if(origin!=null&&!forked)return origin;
        var w=Window;
        return new(SaveId(asNew),label,EffectiveDate,w.Min,w.Max,layers.ToArray(),location);
    }
    // ---------------- date (0.22.7: never earlier than the newest layer material) ----------------
    private (int Version,(DateTime Date,string Material)? Value) newest=(-1,null);
    /// <summary>Newest Technology date among the solid layers, with that material; null when none is known.</summary>
    internal (DateTime Date,string Material)? NewestMaterial
    {
        get
        {
            if(newest.Version!=Version)newest=(Version,CassetteAvailability.Newest(layers,materialDate));
            return newest.Value;
        }
    }
    private string EffectiveDate=>!dateManual&&NewestMaterial is {} n?CassetteAvailability.DateText(n.Date):date;
    /// <summary>One line under the date field: where the date comes from.</summary>
    internal string DateNote
    {
        get
        {
            if(NewestMaterial is not {} n)return "";
            var name=MaterialLabel(n.Material);
            return !dateManual||EffectiveDate==CassetteAvailability.DateText(n.Date)?$"Newest material: {name}, {CassetteAvailability.DateText(n.Date)}."
                :$"Set by you; newest material is {name}, {CassetteAvailability.DateText(n.Date)}.";
        }
    }

    // ---------------- loading ----------------
    private void Reset(ComposerSource source,CassettePreset? from,string name,string day,IEnumerable<CassetteLayer> list,bool fork,string? key,string? status,string? place=null,bool manualDate=false)
    {
        applyPercent=100;Source=source;origin=from;forked=fork;label=name;date=day;dateManual=manualDate;location=place??filter;layers=list.ToList();selected=layers.Count>0?0:-1;picked.Clear();if(selected>=0)picked.Add(0);anchor=selected;
        undo.Clear();SelectedPresetKey=key;Status=status;RestartNotice=false;
        baseline=Signature();
        Version++;LastChange=double.NegativeInfinity;evalKey=-1;
    }
    internal void NewCassette()=>Reset(ComposerSource.Empty,null,"New cassette","1975.01.01",Array.Empty<CassetteLayer>(),true,null,"New cassette: add layers, then save.");
    /// <summary>Historical/own id, or "legacy:&lt;material&gt;".</summary>
    internal bool LoadPreset(string key)
    {
        if(key.StartsWith("legacy:",StringComparison.Ordinal))
        {
            var id=key[7..];var m=Legacy.FirstOrDefault(x=>x.Id==id);if(m==null)return false;
            Reset(ComposerSource.Legacy,null,m.Label+" (from legacy)","1975.01.01",new[]{new CassetteLayer(id,DefaultLegacyMm,0,"up")},true,key,
                $"Legacy material {m.Label} is homogeneous: this draft uses it as one {DefaultLegacyMm:0} mm layer.");
            return true;
        }
        var preset=Catalogue.FirstOrDefault(p=>p.Id==key);if(preset==null)return false;
        var own=preset.Id.StartsWith("user_",StringComparison.Ordinal);
        Reset(own?ComposerSource.Own:ComposerSource.Historical,preset,preset.Label,preset.Date,preset.Layers,false,key,
            own?$"{preset.Label} ({preset.Id}) loaded. Editing creates the next version; plates keep {preset.Id}.":$"{preset.Label} loaded · historical, read-only. Editing creates an own copy.",preset.Location,true);
        filter=preset.Location; // the Turret / Hull switch follows the loaded preset
        return true;
    }
    /// <summary>A draft from "From selection" or an imported share file.</summary>
    internal void LoadDraft(CassettePreset preset,ComposerSource source,string status)
    {
        // An imported share file keeps its own date and location; a selection or a from-scratch draft follows the
        // newest material and the switch. A file date earlier than its newest material is raised to it.
        var imported=source==ComposerSource.Import;
        var newestDate=CassetteAvailability.Newest(preset.Layers,materialDate);
        var keep=imported&&(newestDate==null||CassetteAvailability.Key(preset.Date) is {} k&&k>=CassetteAvailability.Key(CassetteAvailability.DateText(newestDate.Value.Date)));
        Reset(source,null,preset.Label,preset.Date,preset.Layers,true,null,status,imported?preset.Location:filter,keep);
        filter=location;
    }
    /// <summary>The target plate's cassette, if it has one and nothing unsaved is open.</summary>
    internal bool LoadFromMaterial(string materialId)
    {
        if(Dirty)return false;
        var preset=Catalogue.FirstOrDefault(p=>p.MaterialId==materialId);
        return preset!=null&&(origin?.Id==preset.Id&&!forked||LoadPreset(preset.Id));
    }
    internal void Reload()
    {
        Catalogue=Safe(readCatalogue);presetsKey=(-1,-1,"","",-1);Version++;
    }
    /// <summary>Usage or native availability changed (after Apply or a reload): rebuild the preset tags.</summary>
    internal void Invalidate(){presetsKey=(-1,-1,"","",-1);Version++;}
    internal void RegistrationCompleted(CassetteSaveResult result)
    {RestartNotice=result.RestartRequired;Status=result.Status;Invalidate();}

    // ---------------- editing ----------------
    private void BeginEdit()
    {
        undo.Add((layers.ToArray(),label,date,selected,forked,dateManual,picked.ToArray()));if(undo.Count>UndoSteps)undo.RemoveAt(0);
        if(ReadOnly)
        {
            forked=true;dateManual=false; // an own copy follows its newest material from the first edit
            if(Source==ComposerSource.Historical){label=origin!.Label+" (own)";Status=$"Editing creates an own copy: '{label}'. The historical preset stays unchanged.";}
            else Status=$"Editing creates the next version of {Slug(origin!.Id)}; plates keep {origin.Id}.";
        }
    }
    private void Changed(){Version++;LastChange=clock();}
    internal bool CanUndo=>undo.Count>0;
    internal void Undo()
    {
        if(undo.Count==0)return;var u=undo[^1];undo.RemoveAt(undo.Count-1);
        layers=u.Layers.ToList();label=u.Label;date=u.Date;dateManual=u.DateManual;selected=u.Selected;picked=new(u.Picked.Where(i=>i>=0&&i<layers.Count));if(selected>=0&&selected<layers.Count)picked.Add(selected);anchor=selected;forked=u.Forked;
        if(!forked&&origin!=null)label=origin.Label;
        Changed();
    }
    /// <summary>A plain click: this layer alone.</summary>
    internal void SelectLayer(int index){if(index<-1||index>=layers.Count||index==selected&&picked.Count<=1)return;Choose(index);Version++;}
    /// <summary>Ctrl-click: the layer joins the choice, or leaves it when it was chosen.</summary>
    internal void ToggleLayer(int index)
    {
        if(index<0||index>=layers.Count)return;
        if(picked.Contains(index)){picked.Remove(index);if(selected==index)selected=picked.Count>0?picked.Max:-1;}
        else{picked.Add(index);selected=index;}
        anchor=index;Version++;
    }
    /// <summary>Shift-click: every layer between the layer chosen last (the anchor) and this one.</summary>
    internal void SelectRange(int index)
    {
        if(index<0||index>=layers.Count)return;
        var from=anchor>=0&&anchor<layers.Count?anchor:selected>=0?selected:index;
        picked=new(Enumerable.Range(Math.Min(from,index),Math.Abs(index-from)+1));selected=index;anchor=from;Version++;
    }
    internal bool IsLayerSelected(int index)=>picked.Contains(index);
    /// <summary>The chosen layers, front to back.</summary>
    internal IReadOnlyList<int> SelectedLayers=>picked.Where(i=>i>=0&&i<layers.Count).ToArray();
    internal int SelectionCount=>picked.Count(i=>i>=0&&i<layers.Count);
    /// <summary>One edit of every chosen layer (one undo step); a layer the change does not apply to is returned unchanged by <paramref name="change"/>.</summary>
    private void ReplaceChosen(Func<CassetteLayer,CassetteLayer> change)
    {
        var chosen=SelectedLayers;if(chosen.Count==0)return;
        var next=chosen.Select(i=>change(layers[i])).ToArray();
        if(chosen.Select((i,k)=>layers[i]==next[k]).All(x=>x))return;
        BeginEdit();for(int k=0;k<chosen.Count;k++)layers[chosen[k]]=next[k];Changed();
    }
    internal void SetMaterialChoice(int choice)
    {
        var ids=MaterialChoiceIds;if(SelectionCount==0||choice<0||choice>=ids.Count)return;
        var id=ids[choice];
        ReplaceChosen(l=>l.Material==id?l:l with{Material=id,TiltDegrees=id=="air"?0:l.TiltDegrees});
    }
    internal void SetThickness(double mm){if(double.IsFinite(mm))ReplaceChosen(l=>l with{ThicknessMm=Math.Round(Math.Clamp(mm,.1,2000),2)});}
    /// <summary>Tilt and the direction of the tilt skip the air layers of the choice.</summary>
    internal void SetTilt(double degrees){if(double.IsFinite(degrees))ReplaceChosen(l=>l.Material=="air"?l:l with{TiltDegrees=Math.Round(Math.Clamp(degrees,0,75),1)});}
    internal void SetDirection(int index){if(index>=0&&index<Directions.Length)ReplaceChosen(l=>l.Material=="air"?l:l with{TiltDirection=Directions[index]});}
    internal bool CanAddLayer=>layers.Count<MaxLayers;
    internal bool CanDeleteLayer=>SelectionCount>0&&layers.Count>1;
    internal bool CanDuplicateLayer=>SelectionCount>0&&layers.Count+SelectionCount<=MaxLayers;
    internal bool CanMoveLayer(int delta)
    {
        if(delta==0||SelectionCount==0)return false;
        foreach(var i in picked)if(i>=0&&i<layers.Count&&i+Math.Sign(delta)>=0&&i+Math.Sign(delta)<layers.Count&&!picked.Contains(i+Math.Sign(delta)))return true;
        return false;
    }
    private void Insert(CassetteLayer layer){if(!CanAddLayer)return;BeginEdit();var at=SelectionCount==0?layers.Count:SelectedLayers.Max()+1;layers.Insert(at,layer);Choose(at);Changed();}
    /// <summary>"+ Layer": steel 10 mm after the selection.</summary>
    internal void AddLayer()=>Insert(new("rha",10,0,"up"));
    /// <summary>"+ Air": a 20 mm gap after the selection.</summary>
    internal void AddAir()=>Insert(new("air",20,0,"up"));
    /// <summary>A copy of the chosen layers, in the same order, directly after the last chosen layer; the copy is chosen.</summary>
    internal void DuplicateLayer()
    {
        if(!CanDuplicateLayer)return;
        var chosen=SelectedLayers;BeginEdit();var at=chosen.Max()+1;
        layers.InsertRange(at,chosen.Select(i=>layers[i]).ToArray());
        picked=new(Enumerable.Range(at,chosen.Count));selected=at+chosen.Count-1;anchor=at;Changed();
    }
    internal void DeleteLayer(int index=-2)
    {
        if(index==-2)
        {
            // every chosen layer; at least one layer always stays (the layer chosen last when all are chosen)
            var chosen=SelectedLayers.ToList();if(chosen.Count==0||layers.Count<=1)return;
            if(chosen.Count>=layers.Count)chosen.Remove(selected>=0?selected:chosen[0]);
            if(chosen.Count==0)return;
            BeginEdit();int first=chosen.Min();
            foreach(var i in chosen.OrderByDescending(x=>x))layers.RemoveAt(i);
            Choose(Math.Min(first,layers.Count-1));Changed();return;
        }
        if(index<0||index>=layers.Count||layers.Count<=1)return;
        BeginEdit();layers.RemoveAt(index);Choose(Math.Min(selected,layers.Count-1));Changed();
    }
    /// <summary>The chosen layers one place up or down together, keeping their order; a layer that cannot move stays and holds the ones behind it.</summary>
    internal void MoveLayer(int delta)
    {
        if(!CanMoveLayer(delta))return;
        BeginEdit();int n=layers.Count,step=Math.Sign(delta);var flag=new bool[n];foreach(var i in picked)if(i>=0&&i<n)flag[i]=true;
        var order=Enumerable.Range(0,n).ToArray();
        if(step<0){for(int i=1;i<n;i++)if(flag[i]&&!flag[i-1]){(layers[i],layers[i-1])=(layers[i-1],layers[i]);(order[i],order[i-1])=(order[i-1],order[i]);flag[i-1]=true;flag[i]=false;}}
        else{for(int i=n-2;i>=0;i--)if(flag[i]&&!flag[i+1]){(layers[i],layers[i+1])=(layers[i+1],layers[i]);(order[i],order[i+1])=(order[i+1],order[i]);flag[i+1]=true;flag[i]=false;}}
        int primary=selected>=0?Array.IndexOf(order,selected):-1;
        picked=new(Enumerable.Range(0,n).Where(i=>flag[i]));selected=primary>=0&&flag[primary]?primary:picked.Count>0?picked.Max:-1;anchor=selected;Changed();
    }
    /// <summary>Fix for imported presets: tilt above 75° blocks saving.</summary>
    internal void ClampTilts(){if(layers.Any(l=>l.TiltDegrees>75)){BeginEdit();layers=layers.Select(l=>l.TiltDegrees>75?l with{TiltDegrees=75}:l).ToList();Changed();}}
    internal void SetLabel(string text){var t=(text??"").Trim();if(t==label)return;BeginEdit();label=t;Changed();}
    internal void SetDate(string text)
    {
        var t=(text??"").Trim();if(t==EffectiveDate)return;
        BeginEdit();date=t;dateManual=!(NewestMaterial is {} n&&t==CassetteAvailability.DateText(n.Date));Changed();
    }
    internal string? LabelError=>string.IsNullOrWhiteSpace(label)?"Enter a name.":label.Length>120?"At most 120 characters.":label.Any(char.IsControl)?"No control characters.":null;
    internal string? DateError
    {
        get
        {
            var text=EffectiveDate;
            if(!DateTime.TryParseExact(text,"yyyy.MM.dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed))return "Use yyyy.mm.dd, e.g. 1979.01.01.";
            return NewestMaterial is {} n&&parsed<n.Date?$"Earlier than {MaterialLabel(n.Material)} ({CassetteAvailability.DateText(n.Date)}): use that date or later.":null;
        }
    }

    // ---------------- view state ----------------
    internal int Tab{get=>tab;set{if(value<0||value>2||value==tab)return;tab=value;}}
    internal string Search{get=>search;set=>search=(value??"").Trim();}
    internal int AngleIndex{get=>angle;set{if(value<0||value>=Angles.Length||value==angle)return;angle=value;viewStamp++;}}
    internal bool WithTilt{get=>withTilt;set{if(value==withTilt)return;withTilt=value;viewStamp++;}}
    /// <summary>Cached per version (the with/against segment polls it every frame).</summary>
    internal bool AnyTilted{get{if(tilted.Version!=Version){var any=false;foreach(var l in layers)any|=l.TiltDegrees>0;tilted=(Version,any);}return tilted.Any;}}
    private (int Version,bool Any) tilted=(-1,false);
    private int viewStamp;
    internal ComposerTarget? Target=>target;
    /// <summary>0.22.27: a plate is selected but its mesh could not be read ("Plate busy: ..."); null when there is a target or no plate at all.</summary>
    internal string? TargetUnavailable{get;private set;}
    internal void SetTarget(ComposerTarget? next,string? unavailable=null)
    {
        if(next!=null)unavailable=null;
        if(next==target&&unavailable==TargetUnavailable)return;
        target=next;TargetUnavailable=unavailable;TargetVersion++;
        if(next!=null)
        {
            // 0.22.71: another plate starts on Thickness only, a plate in layers on Convert to layers; the choice stays after Apply, except Back to box once the plate is no longer in layers.
            // 0.22.74: the choice is made first and Apply does it, so it stays while the window is open and does not change with the plate (a mirrored pair is two plates that are read in turn).
            // Only the first plate of a window sets the starting choice, and Back to box falls back to Thickness only on a plate that is not in layers, with a short note.
            // 0.22.82: a plate that is a Solid block starts on Solid block, so Apply with another recipe keeps the mark.
            if(!choiceSet){choiceSet=true;applyChoice=next.LayerLine!=null?ComposerApplyChoice.ConvertToLayers:next.SolidMark?ComposerApplyChoice.SolidBlock:ComposerApplyChoice.ThicknessOnly;choiceNote="";Version++;}
            else if(next.LayerLine==null&&applyChoice==ComposerApplyChoice.BackToBox){applyChoice=ComposerApplyChoice.ThicknessOnly;choiceNote="Back to box was cleared: "+next.Name+" is not in layers, so Apply does Thickness only.";Version++;}
        }
    }
    private bool choiceSet;private string choiceNote="";private ComposerApplyChoice applyChoice;
    /// <summary>A new window: the choice starts again (the next target read sets it).</summary>
    internal void ResetApplyChoice(){choiceSet=false;choiceNote="";applyChoice=ComposerApplyChoice.ThicknessOnly;Version++;}
    internal static readonly string[] ApplyChoiceLabels={"Thickness only","Flip selected faces","Thicken inward","Fill to wall","T-80U preset","Conform cheek fill","Cheek fill (shell)","Convert to layers","Back to box",SolidBlockApply.Label};
    /// <summary>0.22.82: the target carries the mark [Solid block].</summary>
    internal bool IsSolidBlock=>target?.SolidMark==true;
    /// <summary>0.22.82: the density of a recipe over its nominal depth, air included (the panel gives CassetteModel.Passive.Density); null: unknown.</summary>
    internal Func<string,double?>? RecipeDensity{get;set;}
    /// <summary>0.22.82: choices whose Apply names the mesh by another mode and so removes the mark of a Solid block (Flip and Thicken inward keep the mode).</summary>
    internal static bool EndsSolidBlock(ComposerApplyChoice choice)=>choice is not (ComposerApplyChoice.SolidBlock or ComposerApplyChoice.Flip or ComposerApplyChoice.Inward or ComposerApplyChoice.BackToBox);
    internal ComposerApplyChoice ApplyChoice=>applyChoice;
    /// <summary>The target is a plate in layers (its mesh carries the layers mark).</summary>
    internal bool InLayers=>target?.LayerLine!=null;
    internal bool ChoiceAvailable(ComposerApplyChoice choice)=>choice!=ComposerApplyChoice.BackToBox||InLayers;
    /// <summary>Choosing changes nothing on the plate; Fill to wall also sets the thickness mode, as its button did.</summary>
    internal void SetApplyChoice(ComposerApplyChoice choice)
    {
        if(!ChoiceAvailable(choice)||choice==applyChoice)return;
        applyChoice=choice;choiceNote="";if(choice==ComposerApplyChoice.FillToWall)SetThicknessMode(2);Version++;
    }
    internal void SetApplyChoice(int index){if(index>=0&&index<ApplyChoiceLabels.Length)SetApplyChoice((ComposerApplyChoice)index);}
    /// <summary>The mesh action of a choice (the action the former button ran); null for Thickness only.</summary>
    internal static ComposerMeshAction? MeshActionOf(ComposerApplyChoice choice)=>choice switch
    {
        ComposerApplyChoice.Flip=>ComposerMeshAction.Flip,ComposerApplyChoice.Inward=>ComposerMeshAction.Inward,ComposerApplyChoice.FillToWall=>ComposerMeshAction.Fill,
        ComposerApplyChoice.CheekPreset=>ComposerMeshAction.CheekPreset,ComposerApplyChoice.ConformCheek=>ComposerMeshAction.ConformCheek,ComposerApplyChoice.CheekShell=>ComposerMeshAction.CheekShell,
        ComposerApplyChoice.ConvertToLayers=>ComposerMeshAction.LayerConvert,ComposerApplyChoice.BackToBox=>ComposerMeshAction.BackToBox,_=>null
    };
    /// <summary>What the chosen option does (the tooltip of the former button).</summary>
    internal string ApplyChoiceHelp=>applyChoice switch
    {
        ComposerApplyChoice.Flip=>"Turns the selected faces around so the plate grows the other way, keeping its thickness and material.",
        ComposerApplyChoice.Inward=>"Turns the selected faces so the plate grows toward the inside of the turret.",
        ComposerApplyChoice.FillToWall=>"Place the plate where the filling should end; each corner grows the way the plate faces until 10 mm before the wall in front of it.",
        ComposerApplyChoice.CheekPreset=>"Adds two cheek fillers beside the gun and lets you check their weight and possible contact with equipment before applying.",
        ComposerApplyChoice.ConformCheek=>"Places the plate X mm behind the turret's inner front wall, faces pointing forward, and fills the gap up to the wall.",
        ComposerApplyChoice.CheekShell=>"Lines the inside of the turret front with armour. You choose the thickness at the gun and at the outer edge.",
        ComposerApplyChoice.ConvertToLayers=>LayerFaces.Tooltip+(InLayers?" This plate is already in layers: it goes back to its box first and is then split again by the recipe.":""),
        ComposerApplyChoice.BackToBox=>"Brings a plate in layers back to its outer faces (front, sides, and a back where the box needs one) so you can reshape the box. Convert to layers makes the layers again.",
        ComposerApplyChoice.SolidBlock=>SolidBlockApply.Help,
        _=>"Applies the recipe and the thickness chosen above to the plate."
    };
    /// <summary>"Plate busy: Plate #464 is not readable yet (dirty flags 2). Try again in a moment."</summary>
    internal static string PlateBusy(string plate,string reason)
    {
        var why=reason switch
        {
            "quiet period"=>"it was just edited",
            "mouse button held"=>"a mouse button is held",
            ""=>"the mesh is not readable",
            _=>reason
        };
        return $"Plate busy: {plate} is not readable yet ({why}). Try again in a moment.";
    }

    // ---------------- material choices ----------------
    private (int Version,IReadOnlyList<string> Ids,IReadOnlyList<(string Label,string? Group)> Labels) choices=(-1,Array.Empty<string>(),Array.Empty<(string,string?)>());
    private void Choices()
    {
        if(choices.Version==Version)return;
        var ids=new List<string>{"air"};var labels=new List<(string,string?)>{("Air (gap)","Air")};
        foreach(var m in Materials){ids.Add(m.Id);labels.Add((m.Label,m.Group));}
        foreach(var l in layers)if(!ids.Contains(l.Material)){ids.Add(l.Material);labels.Add((l.Material+" (missing)","Missing"));}
        // Keep the same list instances while nothing changed (the dropdown polls them every frame).
        if(ids.SequenceEqual(choices.Ids))choices=(Version,choices.Ids,choices.Labels);else choices=(Version,ids,labels);
    }
    internal IReadOnlyList<string> MaterialChoiceIds{get{Choices();return choices.Ids;}}
    internal IReadOnlyList<(string Label,string? Group)> MaterialChoiceLabels{get{Choices();return choices.Labels;}}
    /// <summary>The material of the chosen layers when it is the same for all of them; otherwise -1 (the field shows nothing).</summary>
    internal int MaterialChoice{get{var chosen=SelectedLayers;if(chosen.Count==0)return -1;var id=layers[chosen[0]].Material;if(chosen.Any(i=>layers[i].Material!=id))return -1;var ids=MaterialChoiceIds;for(int i=0;i<ids.Count;i++)if(ids[i]==id)return i;return -1;}}
    internal string MaterialLabel(string id)=>id=="air"?"Air":byId.TryGetValue(id,out var m)?m.Label:Legacy.FirstOrDefault(x=>x.Id==id)?.Label??id;

    // ---------------- selected layer (cached per version) ----------------
    private (int Version,string Title,string Depth,double Thickness,double Tilt,int Direction,bool Air) layerView=(-1,"","",double.NaN,double.NaN,-1,false);
    private void LayerView()
    {
        if(layerView.Version==Version)return;
        var chosen=SelectedLayers;
        if(chosen.Count==0){layerView=(Version,"No layer selected","",double.NaN,double.NaN,-1,false);return;}
        if(chosen.Count==1)
        {
            var l=layers[chosen[0]];var d=CassetteSection.Depth(l);
            layerView=(Version,$"Layer {chosen[0]+1} · {MaterialLabel(l.Material)}",
                l.Material=="air"?$"Air gap: depth {N(d,"0.#")} mm (no tilt)":l.TiltDegrees>0?$"Depth t / cos tilt = {N(d,"0.#")} mm":$"Depth = t = {N(d,"0.#")} mm",
                l.ThicknessMm,l.TiltDegrees,Array.IndexOf(Directions,l.TiltDirection),l.Material=="air");
            return;
        }
        // several layers: a field shows its value when it is the same for all of them, otherwise nothing (NaN, -1)
        var picks=chosen.Select(i=>layers[i]).ToArray();var solid=picks.Where(l=>l.Material!="air").ToArray();
        double Same(IEnumerable<double> v){var list=v.ToArray();return list.Length>0&&list.All(x=>x==list[0])?list[0]:double.NaN;}
        int SameDirection(){var list=solid.Select(l=>Array.IndexOf(Directions,l.TiltDirection)).ToArray();return list.Length>0&&list.All(x=>x==list[0])?list[0]:-1;}
        var mixed=new List<string>();
        if(picks.Select(l=>l.Material).Distinct().Count()>1)mixed.Add("material");
        if(double.IsNaN(Same(picks.Select(l=>l.ThicknessMm))))mixed.Add("thickness");
        if(solid.Length>0&&double.IsNaN(Same(solid.Select(l=>l.TiltDegrees))))mixed.Add("tilt");
        if(solid.Length>0&&SameDirection()<0)mixed.Add("tilt toward");
        var list2=string.Join(", ",chosen.Take(6).Select(i=>(i+1).ToString(System.Globalization.CultureInfo.InvariantCulture)))+(chosen.Count>6?", ...":"");
        layerView=(Version,$"{chosen.Count} layers selected ({list2})",
            (mixed.Count==0?"The same values in all chosen layers.":"Mixed: "+string.Join(", ",mixed)+" (the field stays empty until you set a value; it then applies to all chosen layers).")+(picks.Any(l=>l.Material=="air")&&solid.Length>0?" Tilt skips the air layers.":""),
            Same(picks.Select(l=>l.ThicknessMm)),solid.Length==0?double.NaN:Same(solid.Select(l=>l.TiltDegrees)),solid.Length==0?-1:SameDirection(),picks.All(l=>l.Material=="air"));
    }
    internal string LayerTitle{get{LayerView();return layerView.Title;}}
    internal string LayerDepthText{get{LayerView();return layerView.Depth;}}
    /// <summary>The thickness of the chosen layers; NaN when they differ.</summary>
    internal double LayerThickness{get{LayerView();return layerView.Thickness;}}
    internal double LayerTilt{get{LayerView();return layerView.Tilt;}}
    internal int LayerDirection{get{LayerView();return layerView.Direction;}}
    /// <summary>True when every chosen layer is air (tilt cannot be set).</summary>
    internal bool LayerIsAir{get{LayerView();return layerView.Air;}}
    internal bool LayerEditable=>SelectionCount>0;
    internal string? LayerTiltError=>SelectedLayers.Select(i=>layers[i]).FirstOrDefault(l=>l.TiltDegrees>75) is {} l?$"{N(l.TiltDegrees,"0.#")}° > 75°: save blocked. Use Clamp to 75°.":null;

    // ---------------- validation and preview (debounced) ----------------
    private int evalKey=-1,evalStamp;
    private ComposerIssue[] issues=Array.Empty<ComposerIssue>();
    private CassettePreview? preview;private string? previewError;
    /// <summary>Validate + Preview once per change, 150 ms after the last edit (a load is immediate).</summary>
    internal void Refresh(bool force=false)
    {
        if(evalKey==Version)return;
        if(!force&&clock()-LastChange<DebounceSeconds&&evalKey>=0)return;
        evalKey=Version;evalStamp++;Evaluate();
    }
    private void Evaluate()
    {
        var list=new List<ComposerIssue>();preview=null;previewError=null;
        if(layers.Count==0){list.Add(new("No layers yet. Pick a preset or add a layer.",true,-1));issues=list.ToArray();return;}
        if(layers.All(l=>l.Material=="air"))list.Add(new("Cassette has no material layer.",true,-1));
        if(LabelError is {} le)list.Add(new("Name: "+le,true,-1));
        if(DateError is {} de)list.Add(new("Date: "+de,true,-1));
        if(backend==null){list.Add(new("Materials unavailable: no checks or preview. See LogOutput.log.",true,-1));issues=list.ToArray();return;}
        var draft=Draft();lastDraft=draft;
        CassetteIssue[] found;
        try{found=backend.Validate(draft);}
        catch(Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException){found=new[]{new CassetteIssue("model",ex.Message,true)};}
        foreach(var i in found)
        {
            if(i.Code=="schema"&&(LabelError!=null||DateError!=null))continue; // already named per field
            string? fix=i.Code=="tilt"?"Clamp to 75°":null;
            list.Add(new(Where(i.Layer)+i.Text,i.Error,i.Layer,fix,i.Text));
        }
        if(!list.Any(i=>i.Error))
        {
            try{preview=backend.Preview(draft,ApplyDepthMm);}
            catch(Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException){previewError=ex.Message;list.Add(new("No preview: "+ex.Message,true,-1));}
        }
        issues=list.ToArray();
    }
    private CassettePreset? lastDraft;
    /// <summary>"Layer 2 · NERA 15: " for a message about one layer; empty for a message about the whole cassette.</summary>
    private string Where(int layer)=>layer>=0&&layer<layers.Count?$"Layer {layer+1} · {MaterialLabel(layers[layer].Material)} {N(layers[layer].ThicknessMm,"0.#")}: ":"";
    private (int Stamp,int View,ComposerIssue[] Items) shown=(-1,-1,Array.Empty<ComposerIssue>());
    /// <summary>
    /// 0.22.19: the checks that belong to the cassette itself, plus the messages that apply at the selected shot angle and
    /// side (e.g. a cooperative layer whose effective angle passes the recipe maximum). Nothing generic: a message is only
    /// here when it concretely applies to this cassette now. Cached per evaluation and per angle/side choice.
    /// </summary>
    internal IReadOnlyList<ComposerIssue> Issues
    {
        get
        {
            Refresh();
            if(shown.Stamp==evalStamp&&shown.View==viewStamp)return shown.Items;
            var items=issues;
            if(backend!=null&&lastDraft!=null&&!issues.Any(i=>i.Error))
            {
                try
                {
                    var extra=backend.AngleNotes(lastDraft,Angles[angle],AnyTilted?withTilt:true,AnyTilted);
                    if(extra.Length>0)items=issues.Concat(extra.Select(n=>new ComposerIssue(Where(n.Layer)+n.Text,false,n.Layer,null,n.Text))).ToArray();
                }
                catch(Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException){}
            }
            shown=(evalStamp,viewStamp,items);return items;
        }
    }
    /// <summary>The Messages list: errors first, at most <see cref="MaxShownMessages"/>, then one summary line for the rest.</summary>
    internal const int MaxShownMessages=4;
    internal IReadOnlyList<ComposerIssue> ShownIssues
    {
        get
        {
            var all=Issues;if(all.Count<=MaxShownMessages)return all;
            if(ReferenceEquals(shownCap.Source,all))return shownCap.Items;
            var ordered=all.OrderByDescending(i=>i.Error).ToList();
            var items=ordered.Take(MaxShownMessages-1).Append(new ComposerIssue($"+{ordered.Count-(MaxShownMessages-1)} more messages",false,-1)).ToArray();
            shownCap=(all,items);return items;
        }
    }
    private (IReadOnlyList<ComposerIssue>? Source,ComposerIssue[] Items) shownCap;
    internal bool HasErrors{get{Refresh();foreach(var i in issues)if(i.Error)return true;return false;}}
    internal CassettePreview? Preview{get{Refresh();return preview;}}
    internal int EvalStamp{get{Refresh();return evalStamp;}}
    private (int Version,int View,string Count) issueCount=(-1,-1,"");
    internal string IssueCountText{get{var s=EvalStamp;var all=Issues;if(issueCount.Version!=s||issueCount.View!=viewStamp)issueCount=(s,viewStamp,all.Count==0?"none":all.Count.ToString(CultureInfo.InvariantCulture));return issueCount.Count;}}

    // ---------------- derived values and protection table ----------------
    private (int Stamp,int Version,int View,string[] Derived,string[] Cells) numbers=(-1,-1,-1,Array.Empty<string>(),Array.Empty<string>());
    private void Numbers()
    {
        var stamp=EvalStamp;if(numbers.Stamp==stamp&&numbers.Version==Version&&numbers.View==viewStamp)return;
        var d=new string[DerivedLabels.Length];var dash="—";
        var depth=ApplyDepthMm;var w=Window;
        d[0]=layers.Count==0?dash:$"{N(depth,"0.#")} mm"+(preview!=null&&Math.Abs(preview.DepthMm-depth)>1e-6?$" ({N(preview.DepthMm,"0.#")})":"");
        d[1]=layers.Count==0?dash:$"{w.Min:0}–{w.Max:0} mm"+(origin!=null&&!forked?"":" (±25 %)");
        if(preview is {} p)
        {
            d[2]=$"{p.KgM2:0} kg/m²";d[3]=$"{p.KgM2/p.DepthMm*1000:0} kg/m³";d[4]=p.NativeRha.ToString("0.###",CultureInfo.InvariantCulture);
            d[5]=$"{p.CostM2:0} /m² · {N(p.CostM2/Math.Max(1e-9,p.KgM2),"0.00")} /kg";
            var last=layers.FindLastIndex(l=>l.Material!="air");
            d[6]=last<0?dash:$"layer {last+1} · {MaterialLabel(layers[last].Material)} {N(layers[last].ThicknessMm*ApplyScale,"0.#")} (spall {N(p.SpallFactor,"0.##")})";
        }
        else for(int i=2;i<d.Length;i++)d[i]=dash;
        d[7]=layers.Count==0?dash:$"{N(LosThicknessMm,"0.#")} mm";
        var cells=new string[Angles.Length*4];
        for(int r=0;r<Angles.Length;r++)for(int c=0;c<4;c++)cells[r*4+c]=TableValue(r,c) is {} v?v.ToString("0",CultureInfo.InvariantCulture):dash;
        numbers=(stamp,Version,viewStamp,d,cells);
    }
    internal string Derived(int line){Numbers();return line>=0&&line<numbers.Derived.Length?numbers.Derived[line]:"";}
    /// <summary>Row = angle 0/30/60; column 0 KE with, 1 KE against, 2 HEAT with, 3 HEAT against (mm RHA along the shot path).</summary>
    internal double? TableValue(int row,int column)
    {
        var p=Preview;if(p==null||row<0||row>=Angles.Length||column<0||column>3)return null;
        var with=column%2==0;var cell=p.Protection.FirstOrDefault(x=>x.Angle==Angles[row]&&x.WithTilt==with);
        return cell==null?null:column<2?cell.KeMm:cell.HeatMm;
    }
    internal string Cell(int row,int column){Numbers();var i=row*4+column;return i>=0&&i<numbers.Cells.Length?numbers.Cells[i]:"";}
    internal double LosThicknessMm=>ApplyDepthMm/Math.Cos(Angles[AngleIndex]*Math.PI/180);
    internal double? ProtectionValue(bool heat,bool withTilt,int metric)
    {
        var value=TableValue(AngleIndex,(heat?2:0)+(withTilt?0:1));
        return value is {} v&&Preview is {} p?CassetteProtectionComparison.Value(v,p.DepthMm,p.KgM2,Angles[AngleIndex],metric,backend?.RhaDensity??7850):null;
    }
    /// <summary>"vs 430 mm RHA": what the factor of this row is compared against at the selected angle (empty for the absolute row).</summary>
    internal string ProtectionBasis(int metric)=>Preview is {} p?ProtectionTable.BasisText(CassetteProtectionComparison.Reference(p.DepthMm,p.KgM2,Angles[AngleIndex],metric,backend?.RhaDensity??7850)):"";
    internal string ProtectionCell(bool heat,bool withTilt,int metric)=>ProtectionValue(heat,withTilt,metric)?.ToString(metric==0?"0":"0.00",CultureInfo.InvariantCulture)??"—";
    // ---------------- 0.22.23: table rows and the protection-versus-angle chart (both cached; nothing is computed per frame) ----------------
    private (int Stamp,int Version,int View,ProtectionRow[][] Rows) protectionRows=(-1,-1,-1,Array.Empty<ProtectionRow[]>());
    internal IReadOnlyList<ProtectionRow> ProtectionRows(bool heat)=>ProtectionRows(heat?ProtectionThreat.Jet:ProtectionThreat.LongRod);
    internal IReadOnlyList<ProtectionRow> ProtectionRows(ProtectionThreat threat)
    {
        var stamp=EvalStamp;
        if(protectionRows.Stamp!=stamp||protectionRows.Version!=Version||protectionRows.View!=viewStamp)
        {
            var rows=new ProtectionRow[4][];
            foreach(var t in ProtectionFactorTable.Threats)
            {
                string Cell(bool side,int metric)
                {
                    var p=Preview;var cell=p?.Protection.FirstOrDefault(x=>x.Angle==Angles[AngleIndex]&&x.WithTilt==side);
                    if(cell==null||p==null)return "—";
                    var mm=t switch{ProtectionThreat.LongRod=>cell.KeMm,ProtectionThreat.FullBoreKE=>cell.FullBoreMm,ProtectionThreat.Jet=>cell.HeatMm,_=>cell.HeshMm};
                    return CassetteProtectionComparison.Value(mm,p.DepthMm,p.KgM2,Angles[AngleIndex],metric,backend?.RhaDensity??7850)?.ToString(metric==0?"0":"0.00",CultureInfo.InvariantCulture)??"—";
                }
                rows[(int)t]=Enumerable.Range(0,ProtectionTable.Metrics.Length).Select(m=>new ProtectionRow(ProtectionTable.Metrics[m],m>0?ProtectionBasis(m):"",Cell(true,m),Cell(false,m))).ToArray();
            }
            protectionRows=(stamp,Version,viewStamp,rows);
        }
        return protectionRows.Rows[(int)threat];
    }
    private (int Stamp,CassetteProtection[] Points) curve=(-1,Array.Empty<CassetteProtection>());
    /// <summary>
    /// Protection at 0-85 degrees in steps of 5 for the chart: path × the factor of the game's table (<see cref="CassetteComposer.ProtectionCurve"/>),
    /// computed once per evaluated draft (so 150 ms after the last edit, never per frame). Empty when the draft has errors or no materials backend.
    /// </summary>
    internal IReadOnlyList<CassetteProtection> Curve
    {
        get
        {
            var stamp=EvalStamp;
            if(curve.Stamp==stamp)return curve.Points;
            var points=Array.Empty<CassetteProtection>();
            if(backend!=null&&lastDraft!=null&&preview!=null)
            {
                try{points=backend.ProtectionCurve(lastDraft,ApplyDepthMm);}
                catch(Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException){points=Array.Empty<CassetteProtection>();}
            }
            curve=(stamp,points);return points;
        }
    }
    /// <summary>Chart points (angle, mm RHA) for KE or HEAT on one side of the tilt; NaN-free, in angle order.</summary>
    internal (double Angle,double Mm)[] CurveSeries(bool heat,bool withTilt)=>CurveSeries(heat?ProtectionThreat.Jet:ProtectionThreat.LongRod,withTilt);
    /// <summary>0.22.28: one threat of the game's table (long rod, full-bore KE, jet, HESH) as chart points.</summary>
    internal (double Angle,double Mm)[] CurveSeries(ProtectionThreat threat,bool withTilt)=>Curve.Where(c=>c.WithTilt==withTilt).OrderBy(c=>c.Angle)
        .Select(c=>(c.Angle,threat switch{ProtectionThreat.Jet=>c.HeatMm,ProtectionThreat.LongRod=>c.KeMm,ProtectionThreat.FullBoreKE=>c.FullBoreMm,_=>c.HeshMm}))
        .Where(p=>double.IsFinite(p.Item2)).ToArray();
    /// <summary>The RHA reference lines at the selected angle: metric 1 = same thickness (D / cos), 2 = same weight; NaN when it cannot be read.</summary>
    internal double RhaReference(int metric)=>Preview is {} p?CassetteProtectionComparison.Reference(p.DepthMm,p.KgM2,Angles[AngleIndex],metric,backend?.RhaDensity??7850)??double.NaN:double.NaN;
    /// <summary>For tilted layers, explain the mean and the section-only side control.</summary>
    internal string TableNote=>AnyTilted?"Game protection = average of with and against tilt. Side control changes the section illustration only.":"";

    // ---------------- lists (cached) ----------------
    private (int Version,int Stamp,int View,ComposerLayerRow[] Rows) layerRows=(-1,-1,-1,Array.Empty<ComposerLayerRow>());
    internal IReadOnlyList<ComposerLayerRow> LayerRows
    {
        get
        {
            var stamp=EvalStamp;
            if(layerRows.Version==Version&&layerRows.Stamp==stamp&&layerRows.View==viewStamp)return layerRows.Rows;
            var rows=new ComposerLayerRow[layers.Count];var all=Issues;
            for(int i=0;i<layers.Count;i++)
            {
                var l=layers[i];var d=CassetteSection.Depth(l);
                // 0.22.19: a row only shows its lamp when a message names this layer; the row then carries that message as its note
                // (a row with a note has no subtitle line, so thickness and tilt move into the title).
                var mine=all.Where(x=>x.Layer==i).OrderByDescending(x=>x.Error).FirstOrDefault();
                var title=mine==null?$"{i+1} · {MaterialLabel(l.Material)}"
                    :$"{i+1} · {MaterialLabel(l.Material)} · {(l.Material=="air"?"gap ":"t ")}{N(l.ThicknessMm,"0.#")} mm"+(l.TiltDegrees>0?$" · tilt {N(l.TiltDegrees,"0.#")}° {l.TiltDirection}":"");
                rows[i]=new(i,title,
                    l.Material=="air"?$"gap {N(l.ThicknessMm,"0.#")} mm":$"t {N(l.ThicknessMm,"0.#")} mm · tilt {N(l.TiltDegrees,"0.#")}°{(l.TiltDegrees>0?" "+l.TiltDirection:"")} · depth {N(d,"0.#")} mm",
                    CassettePalette.Group(l.Material).ToUpperInvariant(),$"{N(d,"0.#")} mm",CassettePalette.Hex(l.Material),
                    all.Any(x=>x.Layer==i&&x.Error),all.Any(x=>x.Layer==i&&!x.Error),mine==null?null:(mine.Detail??mine.Text));
            }
            layerRows=(Version,stamp,viewStamp,rows);return rows;
        }
    }
    private (int Stamp,int Tab,string Search,string Filter,int Target) presetsKey=(-1,-1,"","",-1);private ComposerPresetRow[] presetRows=Array.Empty<ComposerPresetRow>();
    /// <summary>
    /// Can the target vehicle use this preset (0.22.7)? Its date against the vehicle date, and the native technology frame.
    /// No vehicle (or an unreadable one) leaves everything available. Asked once per list build, never per frame.
    /// </summary>
    internal CassetteAvailabilityResult AvailabilityOf(CassettePreset p)
    {
        bool? inFrame=null;
        try{inFrame=FrameCheck?.Invoke(p.MaterialId);}catch(Exception){}
        return FrameCheck!=null&&target!=null
            ?CassetteAvailability.ForUse(p.Date,target.VehicleDateKey,nativeAvailable(p.MaterialId),inFrame)
            :CassetteAvailability.Evaluate(p.Date,target?.VehicleDateKey,inFrame);
    }
    private ComposerPresetRow PresetRow(CassettePreset p,bool own)
    {
        var a=AvailabilityOf(p);var native=nativeAvailable(p.MaterialId);
        string tag=!native?CassetteAvailability.Restart:!a.Available?a.Reason:own&&inUse(p.MaterialId)?"IN USE":p.Date[..4];
        var title=own?p.Label+" · "+VersionTag(p.Id):p.Label;
        var subtitle=Summary(p)+(own?" · "+p.Location:"");
        return new(p.Id,title,subtitle,tag,own?ComposerSource.Own:ComposerSource.Historical,native&&a.Available,native?a.Available?null:a.Reason:tag,p.Location);
    }
    internal IReadOnlyList<ComposerPresetRow> PresetRows
    {
        get
        {
            var key=(catalogueStamp,tab,search,filter,TargetVersion);
            if(presetsKey==key)return presetRows;
            IEnumerable<ComposerPresetRow> rows=tab switch
            {
                // The switch filters the historical list; own presets always show, each with its own location.
                0=>Catalogue.Where(p=>!p.Id.StartsWith("user_",StringComparison.Ordinal)&&p.Location==filter).Select(p=>PresetRow(p,false)),
                1=>Catalogue.Where(p=>p.Id.StartsWith("user_",StringComparison.Ordinal)).Select(p=>PresetRow(p,true)),
                _=>Legacy.Select(m=>new ComposerPresetRow("legacy:"+m.Id,m.Label,"homogeneous material · loads as one layer","LEGACY",ComposerSource.Legacy))
            };
            if(search.Length>0)rows=rows.Where(r=>r.Title.Contains(search,StringComparison.OrdinalIgnoreCase)||r.Subtitle.Contains(search,StringComparison.OrdinalIgnoreCase));
            presetRows=rows.ToArray();presetsKey=key;return presetRows;
        }
    }
    internal static readonly string[] LocationLabels={"Turret","Hull"};
    /// <summary>The Turret | Hull switch above the preset list (0 turret, 1 hull).</summary>
    internal int LocationIndex{get=>filter==CassettePreset.Hull?1:0;set=>Location=value==1?CassettePreset.Hull:CassettePreset.Turret;}
    internal string Location{get=>filter;set{if(!CassettePreset.ValidLocation(value)||value==filter)return;filter=value;}}
    private int catalogueStamp=>Catalogue.Count*7919+(Catalogue.Count>0?Catalogue[^1].Id.GetHashCode(StringComparison.Ordinal):0);
    private static string VersionTag(string id){var m=Regex.Match(id,"_v([0-9]+)$");return m.Success?"v"+m.Groups[1].Value:"v1";}
    internal string Summary(CassettePreset p)
    {
        var parts=p.Layers.Take(4).Select(l=>$"{MaterialLabel(l.Material).ToLowerInvariant()} {l.ThicknessMm:0}");
        var d=p.Layers.Sum(CassetteSection.Depth);
        return string.Join(" / ",parts)+(p.Layers.Length>4?$" +{p.Layers.Length-4}":"")+$" · D {d:0}";
    }
    internal string PresetCountText{get{var rows=PresetRows;if(!ReferenceEquals(rows,presetCountFor)){presetCountFor=rows;presetCountText=rows.Count.ToString(CultureInfo.InvariantCulture);}return presetCountText;}}
    private object? presetCountFor;private string presetCountText="0";
    internal string EmptyText=>tab==1?"No own cassettes yet. Edit a historical preset or use From selection.":tab==2?"No legacy cassette materials in this game.":$"No {filter} presets found.";

    // ---------------- header texts ----------------
    private (int Version,string Layers,string Meta,string Chip) header=(-1,"","","");
    private void Header()
    {
        if(header.Version==Version)return;
        var w=Window;
        var source=Source switch
        {
            ComposerSource.Historical=>forked?"Own copy of a historical preset":"Historical preset · read-only",
            ComposerSource.Own=>forked?$"Next version of {Slug(origin!.Id)}":$"Own preset {origin!.Id}"+(inUse(origin.MaterialId)?" · in use":""),
            ComposerSource.Legacy=>"Draft from a legacy material",ComposerSource.Selection=>$"FROM SELECTION · {layers.Count(l=>l.Material!="air")} plates",
            ComposerSource.Import=>"Imported share file (draft)",_=>"New draft"
        };
        header=(Version,layers.Count==0?"no layers":$"D {N(DepthMm,"0.#")} mm · {layers.Count} layer{(layers.Count==1?"":"s")}",
            $"{source} · {location} · date {EffectiveDate} · window {w.Min:0}–{w.Max:0} mm"+(Dirty?" · unsaved":""),ReadOnly?"READ-ONLY":forked?"DRAFT":"SAVED");
    }
    internal string LayersNote{get{Header();return header.Layers;}}
    internal string MetaText{get{Header();return header.Meta;}}
    internal string StateChip{get{Header();return header.Chip;}}

    // ---------------- buttons ----------------
    /// <summary>"Save as new version": a valid draft with something to save.</summary>
    internal bool CanSave=>Writable&&layers.Count>0&&!HasErrors&&Dirty;
    internal bool CanSaveAsNew=>Writable&&layers.Count>0&&!HasErrors;
    internal bool CanExport=>backend!=null&&layers.Count>0&&!HasErrors;
    /// <summary>The saved preset Apply would use: an unchanged historical or own preset.</summary>
    internal CassettePreset? ApplyPreset=>origin!=null&&!forked&&!Dirty?origin:null;
    internal bool CanApply=>ApplyBlocker==null;
    /// <summary>0.22.74: with an option chosen and no recipe open in the composer, a composite plate keeps the cassette it already has.</summary>
    internal bool OwnRecipe=>applyChoice!=ComposerApplyChoice.ThicknessOnly&&applyChoice!=ComposerApplyChoice.BackToBox&&ApplyPreset==null&&layers.Count==0&&target is {Composite:true} t&&CompositePlate.IsCassette(t.MaterialId)&&t.MaterialKnown!=false;
    /// <summary>0.22.74: the material id Apply writes with an option: the saved recipe, or the plate's own cassette (see <see cref="OwnRecipe"/>); null when neither.</summary>
    internal string? ApplyRecipeId=>ApplyPreset?.MaterialId??(OwnRecipe?target!.MaterialId:null);
    /// <summary>Just saved, and the game has not loaded the new material yet: show "works after restarting".</summary>
    internal bool RestartPending=>origin!=null&&(!nativeAvailable(origin.MaterialId)||ApplyAvailability is {Available:false,Reason:CassetteAvailability.Restart});
    /// <summary>
    /// 0.22.14: the target is an ordinary add-on plate that can be converted. Apply then becomes "Convert to composite plate":
    /// one undo step that converts the plate and applies the chosen preset.
    /// </summary>
    internal bool Converts=>target is {Composite:false,ConvertBlocker:null};
    internal string? ApplyBlocker
    {
        get
        {
            if(target==null)return TargetUnavailable??"Select a plate in the designer.";
            // 0.22.71: Back to box needs only the plate; the other options besides Thickness only work on a composite plate.
            if(applyChoice==ComposerApplyChoice.BackToBox)return !target.Composite?"Select a composite plate.":!InLayers?"This plate is not in layers.":null;
            if(applyChoice==ComposerApplyChoice.ThicknessOnly&&target.Composite&&InLayers)return "This plate is in layers: choose Convert to layers to rebuild it with the recipe, or Back to box to reshape it.";
            if(!target.Composite&&target.ConvertBlocker is {} why)return why;
            // 0.22.82: Solid block needs a closed shape and a plate that is not in layers (the reason and the advice are shown; nothing is changed).
            if(applyChoice==ComposerApplyChoice.SolidBlock)
            {
                if(InLayers)return SolidBlockApply.InLayers;
                if(target.Solid is {Refusal:{} open})return open;
            }
            // 0.22.74: an option can be chosen before anything else and Apply does it all: a plate that is not composite is converted in the same step (the recipe is needed for that);
            // a composite plate with no recipe open keeps its own recipe. Only what really cannot be done refuses (the reason is shown).
            if(ApplyPreset is not {} p)return OwnRecipe?null:layers.Count==0?Converts?applyChoice!=ComposerApplyChoice.ThicknessOnly?"Pick a preset first: Apply converts the plate with it.":"Pick a preset first: Convert to composite plate applies it.":"Pick a preset first.":"Save the draft first.";
            if(!nativeAvailable(p.MaterialId))return CassetteAvailability.Restart+": the cassette model is not loaded.";
            // The vehicle date decides (0.22.7). Whatever the current plate carries never blocks: re-applying and switching always work.
            if(ApplyAvailability is {} blocked&&!blocked.Available)return blocked.Reason==CassetteAvailability.Restart?CassetteAvailability.Restart+": native registration is not confirmed.":CassetteAvailability.Explain(p.Label,p.Date,target.VehicleDateKey,blocked);
            if(HasErrors)return "Fix the errors first.";
            return null;
        }
    }
    // Availability of the preset Apply would use on the target vehicle, once per change (not per frame: it asks the native frame).
    private (int Version,int Target,CassetteAvailabilityResult? Value) applyAvailability=(-1,-1,null);
    private CassetteAvailabilityResult? ApplyAvailability
    {
        get
        {
            if(applyAvailability.Version!=Version||applyAvailability.Target!=TargetVersion)
                applyAvailability=(Version,TargetVersion,ApplyPreset is {} p&&target!=null?AvailabilityOf(p):null);
            return applyAvailability.Value;
        }
    }
    /// <summary>
    /// The plate carries a cassette the game does not know on this vehicle (date or missing Technology): a warning
    /// replaces any "not a composite plate" talk, because the plate IS a composite plate. Null when nothing is wrong.
    /// </summary>
    internal string? TargetWarning{get{ApplyTexts();return apply.Warning;}}
    private string? UnknownMaterialText(ComposerTarget t)
    {
        if(!CompositePlate.IsCassette(t.MaterialId)||t.MaterialKnown!=false)return null;
        var preset=Catalogue.FirstOrDefault(p=>p.MaterialId==t.MaterialId);
        return CassetteAvailability.UnknownMaterial(t.MaterialId,preset?.Label??t.MaterialLabel,preset?.Date,t.VehicleDateKey,false);
    }
    private (int Version,int Target,int Stamp,string Chip,string Note,string Button,string? Warning,string Scope,string Confirm) apply=(-1,-1,-1,"","","",null,"","");
    private void ApplyTexts()
    {
        var stamp=EvalStamp;
        if(apply.Version==Version&&apply.Target==TargetVersion&&apply.Stamp==stamp)return;
        var t=target;var warning=t==null?null:UnknownMaterialText(t);
        var chip=t==null?TargetUnavailable!=null?"Plate selected · not readable yet":"No plate selected":$"{t.Name} · {t.Faces} face{(t.Faces==1?"":"s")} · now: {t.MaterialLabel}"+(t.Composite?"":" · not a composite plate")+(warning!=null?" · unknown material":"");
        var selected=t!=null&&t.SelectedFaces>0&&t.SelectedFaces<=t.Faces?t.SelectedFaces:0;
        var scope=t==null||t.Faces<=0?"":CompositeThicknessPlan.Scope(t.Faces,selected);
        string note,confirm="";
        if(ApplyPreset is {} p&&t!=null)
        {
            var depth=ApplyDepthMm;
            // One unambiguous sentence: what Apply writes (this preset), then what the plate has now.
            var faces=t.Faces<=0?"":Math.Round(t.MinFaceMm)==Math.Round(t.MaxFaceMm)?$"; faces are now {t.MinFaceMm:0} mm":$"; faces are now {t.MinFaceMm:0}–{t.MaxFaceMm:0} mm";
            // 0.22.14: with selected faces only those get the depth; the others keep their own thickness.
            var which=selected>0?$"the {selected} selected face{(selected==1?"":"s")}":"all faces";
            // 0.22.16.1 fork: Uniform keeps the old sentences byte for byte; ToBackPlane describes one back plane.
            var thickness=FillToWall?"the host wall (configured clearance, cap and reserve)":ToBackPlane?$"one back plane, {ThicknessText}":ThicknessText;
            note=Converts
                ?(ToBackPlane?$"Converts {t.Name} to a composite plate, then sets the material and thickens {which} to {thickness} (this preset){faces}."
                    :$"Converts {t.Name} to a composite plate, then sets the material and {which} to {thickness} (this preset){faces}.")
                :ToBackPlane?$"Sets the material and thickens {which} to {thickness} (this preset){faces}."
                :$"Sets the material and {which} to {thickness} (this preset){faces}.";
            if(Converts)
            {
                var linked=CompositeConversion.LinkedNote(t.LinkedCopies);
                confirm=$"{t.Name} becomes a Composite armour plate with {p.Label}. Material now: {t.MaterialLabel}. {scope} ({thickness})"+(selected>0?"; the other faces keep their thickness.":".")
                    +(linked.Length>0?" "+linked:"")+" One undo step: Ctrl+Z makes it a normal plate again.";
            }
        }
        else note=ApplyBlocker??"";
        if(applyChoice==ComposerApplyChoice.BackToBox)note=ApplyBlocker??"Brings the plate back to its outer faces (the recipe is not changed).";
        if(applyChoice!=ComposerApplyChoice.ThicknessOnly&&applyChoice!=ComposerApplyChoice.BackToBox&&Converts&&t!=null)
        {
            // 0.22.74: the choice comes first; Apply converts the plate, applies the recipe and does the option in one step for Undo.
            var recipe=ApplyPreset is {} chosen?chosen.Label+" ":"";var linked=CompositeConversion.LinkedNote(t.LinkedCopies);
            note=$"Converts {t.Name} to a composite plate, applies {recipe}and then: {ApplyChoiceLabels[(int)applyChoice]}. One undo step."+(linked.Length>0?" "+linked:"");
            if(ApplyPreset==null)note=ApplyBlocker??note;
        }
        if(applyChoice==ComposerApplyChoice.SolidBlock&&t!=null)
        {
            // 0.22.82: the recipe and the mark in one step; the geometry stays as drawn. Then the closed pieces, the volume, the new weight and the line about fragments.
            if(!Converts)note=ApplyBlocker??$"Applies {ApplyPreset?.Label??t.MaterialLabel} to {t.Name} as a Solid block; the shape stays as drawn. One undo step.";
            if(ApplyBlocker==null&&t.Solid is {Refusal:null,Pieces:>0} s)note+=" "+SolidBlockApply.Note(s.Pieces,s.VolumeM3,ApplyRecipeId is {} recipeId?RecipeDensity?.Invoke(recipeId):null,s.ThickFaces,s.MaxThicknessMm);
        }
        // 0.22.82: a choice that names the mesh by another mode ends the Solid block; the note says so.
        if(t?.SolidMark==true&&EndsSolidBlock(applyChoice))note=SolidBlockApply.Leaves+(note.Length>0?" "+note:"");
        var button=applyChoice!=ComposerApplyChoice.ThicknessOnly?"Apply: "+ApplyChoiceLabels[(int)applyChoice]+(applyChoice!=ComposerApplyChoice.BackToBox&&Converts?" (converts the plate first)":""):t==null?"Apply to plate":Converts?"Convert to composite plate":"Apply to "+t.Name;
        if(applyChoice==ComposerApplyChoice.ConvertToLayers&&t?.FrontNote is {Length:>0} frontNote)note=frontNote+". "+note;
        if(choiceNote.Length>0)note=choiceNote+(note.Length>0?" "+note:"");
        apply=(Version,TargetVersion,stamp,chip,note,button,warning,scope,confirm);
    }
    internal string TargetChip{get{ApplyTexts();return apply.Chip;}}
    /// <summary>0.22.60: for a plate in layers, "Layers: N faces assigned, M not assigned." and the estimate from the hit calculation; null otherwise.</summary>
    internal string? LayerLine=>target?.LayerLine;
    internal string ApplyNote{get{ApplyTexts();return apply.Note;}}
    internal string ApplyButton{get{ApplyTexts();return apply.Button;}}
    /// <summary>0.22.14: "Applies to 4 selected faces" or "Applies to all 16 faces"; empty without a plate.</summary>
    internal string ApplyScope{get{ApplyTexts();return apply.Scope;}}
    /// <summary>0.22.14: the confirmation text of "Convert to composite plate" (plate, preset, current material, faces, linked copies, undo).</summary>
    internal string ConvertConfirm{get{ApplyTexts();return apply.Confirm;}}

    // ---------------- cross-section (cached; the diagram redraws only when DiagramVersion changes) ----------------
    private (int Version,int View,int Stamp,CassetteSectionGeometry? Geometry) section=(-1,-1,-1,null);
    private int diagramVersion;
    internal CassetteSectionGeometry Section
    {
        get
        {
            var stamp=EvalStamp;
            if(section.Geometry!=null&&section.Version==Version&&section.View==viewStamp&&section.Stamp==stamp)return section.Geometry;
            section=(Version,viewStamp,stamp,CassetteSection.Build(ApplyThickness.Layers(layers,ApplyScale),Angles[angle]));diagramVersion++;
            return section.Geometry!;
        }
    }
    /// <summary>Changes only when the layers, the selection, the angle/side or the issue marks change.</summary>
    internal int DiagramVersion{get{_=Section;return diagramVersion;}}
    internal string AngleText=>$"{Angles[angle]:0}° · path {Section.PathMm:0} mm (D {Section.DepthMm:0} / cos {Angles[angle]:0}°)";
    internal string? BandTooltip(int index)
    {
        if(index<0||index>=layers.Count)return null;var l=layers[index] with{ThicknessMm=layers[index].ThicknessMm*ApplyScale};var d=CassetteSection.Depth(l);
        var eff=l.TiltDegrees>0?$" · effective {Math.Abs(Angles[angle]+(withTilt?-1:1)*l.TiltDegrees):0}°":"";
        return l.Material=="air"?$"Layer {index+1} · air gap {N(l.ThicknessMm,"0.#")} mm":$"Layer {index+1} · {MaterialLabel(l.Material)} {N(l.ThicknessMm,"0.#")} mm · tilt {N(l.TiltDegrees,"0.#")}° {l.TiltDirection}{eff} · depth {N(d,"0.#")} mm";
    }

    // ---------------- save / export / import ----------------
    /// <summary>"Save as new version" (fork family) or "Save as new cassette" (name family): always a new id via <see cref="CassetteComposer.SaveNewVersion"/>.</summary>
    internal ComposerSaveResult Save(bool asNew)
    {
        Refresh(true);
        if(backend==null||!Writable)throw new InvalidOperationException("The cassette file is not writable; see LogOutput.log.");
        if(layers.Count==0||HasErrors)throw new InvalidOperationException(issues.FirstOrDefault(i=>i.Error)?.Text??"Add a layer first.");
        if(!asNew&&!Dirty)throw new InvalidOperationException("Nothing changed: edit the layers or use Save as new cassette.");
        var draft=Draft(asNew);
        if(origin!=null&&!forked)draft=draft with{Id=Slug(asNew?label:origin.Id)};
        var saved=backend.SaveNewVersion(draft);
        Reload();
        LoadPreset(saved.Preset.Id);
        RestartNotice=saved.RestartRequired;
        Status=$"Saved {saved.Preset.Label} as {saved.Preset.Id}."+(saved.RestartRequired?" Native registration pending.":"");
        return new(saved.Preset,saved.RestartRequired,Status);
    }
    internal string Export(string directory)
    {
        Refresh(true);
        if(backend==null||!CanExport)throw new InvalidOperationException(issues.FirstOrDefault(i=>i.Error)?.Text??"Nothing to export.");
        Directory.CreateDirectory(directory);
        var preset=Draft();var path=Path.Combine(directory,Slug(preset.Label)+".cassette.json");
        backend.Export(preset,path);Status="Exported to "+path;return path;
    }
    internal void Import(string path)
    {
        if(backend==null)throw new InvalidOperationException("Materials unavailable.");
        var preset=backend.Import(path);
        LoadDraft(preset,ComposerSource.Import,$"Imported {Path.GetFileName(path)} as a draft. Save to give it a local id.");
    }
}
