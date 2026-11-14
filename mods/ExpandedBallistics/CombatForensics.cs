using System.Globalization;
namespace SprocketExpandedBallistics;

/// <summary>
/// One measured layer on the shot line. 0.22.41: layers with a 0 mm measured path that leave the penetration unchanged are not listed,
/// and <paramref name="Number"/> counts the visible layers without gaps. <paramref name="Sublayers"/> is the known build-up of a single
/// composer-preset plate. 0.22.42: a native group of several plates is a layer of a composite array; <paramref name="Plates"/> are its plates
/// front to back with the values measured in this layer, and <paramref name="ArrayFirst"/> is the number of the array's first layer.
/// <paramref name="StartMm"/> and <paramref name="PathMm"/> place the layer along the drawn shot line.
/// </summary>
internal sealed record ForensicLayer(int Number,string HitId,string Stage,string Name,
    IReadOnlyList<CombatLayerPart> Parts,bool Combined,double? GapMm,double? Before,double? After,
    double? AbsorbedMm,double? Percent,string Era,double StartMm,double? PathMm,string Colour,
    IReadOnlyList<CompositeSublayer>? Sublayers=null,double? EraMm=null,string Plate="",
    IReadOnlyList<ForensicArrayPlate>? Plates=null,int ArrayFirst=0,string Module="",IReadOnlyList<string>? PaidIntervals=null,LayerStop? PlateStop=null,[property:System.Text.Json.Serialization.JsonIgnore] string ModuleOutcome="")
{
    /// <summary>Several armour plates in one native group: a layer of a composite array.</summary>
    internal bool Array=>GapMm==null&&Parts.Count(p=>p.Armour)>1;
    /// <summary>0.22.43: the layer holds an internal module (crew position, engine, fuel tank, ammunition stowage, transmission ...); <see cref="Module"/> is its readable name.</summary>
    internal bool Internal=>Module.Length>0;
    // 0.22.72: for an internal module, the recorded result of this impact on it ("destroyed", "incapacitated", "damaged", "wounded"), else empty. Presentation only; not serialised, so the JSON of a model equals 0.22.71.
    /// <summary>0.22.43: an internal module in one native group with a plate. The layer keeps the plate's name, band and constituents (measured armour
    /// is never hidden); the module is an indented row and a grey marker band of its own.</summary>
    internal bool Grouped=>Internal&&Parts.Any(p=>p.Armour);
    /// <summary>"296 mm at 65.4°" per plate with a measured normal thickness and obliquity; "54.5 mm" for an air gap.</summary>
    internal string Geometry=>GapMm is {} g?$"{CombatForensics.N(g)} mm":
        string.Join(" / ",Parts.Where(p=>p.Armour&&p.ThicknessMm!=null&&p.AngleDegrees!=null).Select(p=>$"{CombatForensics.N(p.ThicknessMm)} mm at {CombatForensics.N(p.AngleDegrees)}°"));
}
/// <summary>
/// 0.22.42: one plate of a composite array as measured in one layer: its name, material, normal thickness and obliquity (in <paramref name="Part"/>),
/// and the constituents of its composer preset (built-in or the user's own; the game reads the loaded snapshot). <paramref name="Order"/> counts front to back.
/// </summary>
internal sealed record ForensicArrayPlate(int Order,CombatLayerPart Part,string Name,string Material,string Colour,IReadOnlyList<CompositeSublayer>? Sublayers);
/// <summary>
/// 0.22.42: a composite array, the consecutive layers (native groups of the same stage) that share plates. <paramref name="Plates"/> lists every
/// plate once, front to back, with its first measured normal thickness and obliquity; the cross-section draws each plate once as one continuous
/// band over <paramref name="StartMm"/> … + <paramref name="PathMm"/>. <paramref name="Percent"/> is the share of all its layers together.
/// </summary>
internal sealed record ForensicArray(IReadOnlyList<int> Layers,IReadOnlyList<ForensicArrayPlate> Plates,double StartMm,double PathMm,double? Percent)
{
    internal int First=>Layers[0];
}
/// <summary>0.22.43: one internal module the projectile reached on the shot line, in order: its layer, readable name, and the residual penetration on entry.
/// <paramref name="With"/> names a plate of the same native group ("Plate #508"), empty for a module on its own.</summary>
internal sealed record ForensicModule(int Layer,string Name,double? EntryMm,double? Percent,string With);
/// <summary>
/// 0.22.43: the protective envelope is the sequence of armour layers ahead of the first internal module on the shot line. It is perforated when the
/// projectile reaches an internal module with residual penetration (&gt; 0), also when it is arrested further on (for example in the far-side armour).
/// <paramref name="EntryMm"/> is the residual penetration on entry into the first module's layer (for a module grouped with a plate, the exit of that
/// group: a lower bound); <paramref name="Compartment"/> names it ("engine compartment", "crew compartment", "ammunition stowage").
/// </summary>
internal sealed record ForensicEnvelope(bool Perforated,IReadOnlyList<int> ArmourLayers,int FirstLayer,double? EntryMm,string Compartment,IReadOnlyList<ForensicModule> Modules)
{
    internal static readonly ForensicEnvelope None=new(false,System.Array.Empty<int>(),0,null,"",System.Array.Empty<ForensicModule>());
    /// <summary>The additional protection (mm RHA-eq) that preventing perforation required: the residual on entry into the compartment, else 0.</summary>
    internal double RequiredMm=>Perforated&&EntryMm is {} e?e:0;
}
internal sealed record ForensicModel(int Number,string Outcome,string Colour,string Explanation,string Context,
    IReadOnlyList<ForensicLayer> Layers,double? Initial,double? Residual,double? ResidualPercent,
    double? UnattributedMm,IReadOnlyList<string> Findings,IReadOnlyList<string> Advice,string Damage,int HiddenZeroSpans=0,
    IReadOnlyList<ForensicArray>? Arrays=null,ForensicEnvelope? Envelope=null,bool Arrested=false)
{
    // 0.22.41 (user 08-11): at most one sober line under the drawing and one under the bars; only measured statements.
    internal const string Footnote="Share of initial penetration absorbed per layer; heat-map 0 % (blue) to 100 % (red).";
    internal const string GeometryNote="Cross-section to scale along the shot line; each plate band is its normal thickness at the measured obliquity; framed icons mark the internal modules on the shot line.";
    internal const string Title="Forensic investigation",ModeLabel="Material / Absorption (heat-map)",ListTitle="Penetration absorbed per layer",
        FindingsTitle="Findings",AdviceTitle="Advice",ResidualLabel="Residual penetration behind the last layer",NoDamage="no crew casualties or module damage";
    internal bool HasResidualRow=>Residual!=null&&ResidualPercent!=null;
    internal string BetweenLabel=>UnattributedMm is {} u&&u<0?"Increase between layers":"Reduction between layers";
    internal IReadOnlyList<ForensicArray> ArrayRuns=>Arrays??System.Array.Empty<ForensicArray>();
    internal ForensicEnvelope Protection=>Envelope??ForensicEnvelope.None;
    /// <summary>0.22.43: the residual row names the last layer of the main charge ("Residual penetration behind layer 16").</summary>
    internal string RestLabel=>Layers.LastOrDefault(l=>l.Stage!="Precursor") is {} last?$"Residual penetration behind layer {last.Number}":ResidualLabel;
}

/// <summary>Pure report-only derivation. Does not collect, simulate, or change native state.</summary>
internal static class CombatForensics
{
    internal static string N(double? v)=>v is {} x&&double.IsFinite(x)?x.ToString("0.#",CultureInfo.InvariantCulture):"?";
    internal static double? Path(CombatLayerPart p)=>p.ThicknessMm is {} t&&p.AngleDegrees is {} a&&a<89.9?t/Math.Cos(a*Math.PI/180):null;
    internal static string MaterialColour(string? id)
    {
        if(id is "ebWood" or "ebRubber" or "ebFabric" or "ebGlass")return CassettePalette.Hex(id);
        var s=(id??"").ToLowerInvariant();
        return s.Contains("active")?"#e07a4f":s.Contains("cassette")?"#b69ada":s.Contains("elastomer")?"#68a992":
            s.Contains("glass")||s.Contains("textolite")?"#aea578":s.Contains("ceramic")?"#c8bca0":s.Length==0?"#7f8b97":"#8095a6";
    }
    internal static string Heat(double? percent)
    {
        if(percent==null)return "#68727d";
        double t=Math.Clamp(percent.Value/100,0,1);
        return $"#{(int)(55+200*t):X2}{(int)(125-40*t):X2}{(int)(160-105*t):X2}";
    }
    /// <summary>"composite array", "air gap" or the layer's material: the short name in the context line and in sentences.</summary>
    internal static string Short(ForensicLayer l)=>l.GapMm!=null?"air gap":l.Array?"composite array":l.Name;
    internal static string Ref(ForensicLayer l)=>$"layer {l.Number} ({Short(l)})";
    /// <summary>0.22.69: where a shot stopped: the layer, and for a plate in layers the layer of its recipe ("layer 3 (T-90A welded turret), layer 5 of 13 of the plate").</summary>
    internal static string StopRef(ForensicLayer l)=>Ref(l)+(l.PlateStop is {} s?$", layer {s.Group} of {s.Groups} of the plate":"");
    internal static string LayerList(IReadOnlyList<ForensicLayer> list)=>list.Count==1?"layer "+list[0].Number:
        "layers "+string.Join(", ",list.Take(list.Count-1).Select(l=>l.Number))+" and "+list[^1].Number;
    /// <summary>The measured ERA statement of a layer ("ERA element initiated"), empty when the layer holds no ERA.</summary>
    internal static string EraText(ForensicLayer l)=>l.Era switch
    {
        "ERA activated"=>"ERA element initiated","ERA already spent"=>"ERA element previously initiated",
        "ERA unavailable"=>"ERA element not initiated (unavailable)","ERA not initiated"=>"ERA element not initiated",_=>""
    };
    /// <summary>The measured layers of a normalized shot. 0.22.43: shared by the forensic model and by the envelope rule of the result word
    /// (<see cref="EnvelopePerforated"/>); <paramref name="constituents"/> adds the composer constituents (presentation only).</summary>
    private sealed record Measured(List<ForensicLayer> Layers,int Hidden,double? Residual,bool Stopped,bool Ricochet,ForensicLayer? StopLayer,ForensicLayer? RicochetLayer,int Starts);
    private static Measured Measure(CombatReportShot shot,bool constituents)
    {
        var starts=shot.Events.Where(e=>e.Kind=="layerStart"&&e.Layer!=null).ToArray();
        var layers=new List<ForensicLayer>();double position=0;int hidden=0;
        double? initial=shot.InitialPenetrationMm;double? residual=null;bool stopped=false,ricochet=false;
        ForensicLayer? stopLayer=null,ricochetLayer=null;
        // Every ERA element this shot initiated (in any of its native blocks): another pass through it is not "not initiated".
        var initiatedEra=new HashSet<int>();
        foreach(var s in starts)
        {
            var own=shot.Events.Where(x=>x.Stage==s.Stage&&x.HitId==(s.HitId??s.Layer!.Id)&&x.Kind is "heavyEra" or "lightEra" or "eraReaction" or "eraResidual").ToArray();
            if(own.Length==0)continue;
            foreach(var p in s.Layer!.Parts.Where(p=>(p.MaterialId??"").Contains("Active",StringComparison.OrdinalIgnoreCase)))initiatedEra.Add(p.Entity);
            foreach(var x in own)if(x.RelatedEntity is {} id)initiatedEra.Add(id);
        }
        // 0.22.51: a block that pays no new material joins the preceding solid layer; a later crossing of one object is named.
        var continuity=CombatLayerContinuity.Analyse(shot);
        for(int i=0;i<starts.Length;i++)
        {
            var e=starts[i];var info=e.Layer!;
            var events=shot.Events.Where(x=>x.Stage==e.Stage&&x.HitId==(e.HitId??info.Id)).ToArray();
            var terminal=events.LastOrDefault(x=>x.Kind is "plateResidual" or "componentResidual" or "nativeStop" or "materialStop" or "outcome");
            bool stop=events.Any(x=>x.Kind is "nativeStop" or "materialStop"||x.Kind=="outcome"&&x.Text.StartsWith("Stopped",StringComparison.OrdinalIgnoreCase));
            bool rico=events.Any(x=>x.Kind=="outcome"&&x.Text.StartsWith("Deflected",StringComparison.OrdinalIgnoreCase));
            var after=terminal?.AfterMm??events.LastOrDefault(x=>x.AfterMm!=null&&x.Kind is "materialReaction" or "eraResidual" or "eraReaction")?.AfterMm;
            // A zero reaction is a recorded exhausted budget even when native never emits a final stop.
            stop|=after==0;
            if(stop)after=0;
            if(after==null&&!stop&&!rico&&i+1<starts.Length&&starts[i+1].Stage==e.Stage)after=starts[i+1].BeforeMm;
            var before=e.BeforeMm??terminal?.BeforeMm;
            double? loss=before is {} b&&after is {} a?b-a:null;
            string era=events.Any(x=>x.Kind is "heavyEra" or "lightEra" or "eraReaction" or "eraResidual")?"ERA activated":
                events.Any(x=>x.Kind=="eraSpent")?"ERA already spent":events.Any(x=>x.Kind=="eraUnavailable"&&!continuity.IsRepeatedHousing(x))?"ERA unavailable":
                info.Parts.Any(p=>(p.MaterialId??"").Contains("Active",StringComparison.OrdinalIgnoreCase))?"ERA not initiated":"";
            // An air gap holds no ERA element; another pass through an element this shot initiated is not a separate statement.
            var active=info.Parts.Where(p=>(p.MaterialId??"").Contains("Active",StringComparison.OrdinalIgnoreCase)).Select(p=>p.Entity).ToArray();
            if(info.AirGapMm!=null)era="";
            else if(era=="ERA not initiated"&&active.Length>0&&active.All(initiatedEra.Contains))era="";
            var eraEvent=events.LastOrDefault(x=>x.Kind is "eraResidual" or "eraReaction"&&x.BeforeMm!=null&&x.AfterMm!=null);
            double? eraMm=eraEvent==null?null:Math.Max(0,eraEvent.BeforeMm!.Value-eraEvent.AfterMm!.Value);
            var armour=info.Parts.Where(p=>p.Armour).ToArray();
            var paths=armour.Select(Path).ToArray();
            double? path=info.AirGapMm??(paths.Length>0&&paths.All(p=>p!=null)?paths.Max():null);
            var main=e.Stage!="Precursor";
            // 0.22.41 (user 08-11): a 0 mm measured path that leaves the penetration unchanged is not a reported layer.
            if(!stop&&!rico&&CombatZeroSpan.Is(e.HitId??info.Id,path,before,after)){hidden++;if(main)residual=after;continue;}
            // 0.22.51: all material of this block was paid earlier in the shot. It is one row with the preceding solid layer that paid it;
            // its native loss is added there, so the sum of the losses and the entry/exit continuity are unchanged.
            if(continuity.IsSettled(e.Stage,e.HitId??info.Id)&&layers.Count>0&&layers[^1] is {GapMm:null} owner&&owner.Stage==e.Stage)
            {
                var exit=after??owner.After;
                double? total=owner.Before is {} entry&&exit is {} left?entry-left:owner.AbsorbedMm;
                var merged=owner with{After=exit,AbsorbedMm=total,Percent=main&&initial is >0&&total!=null?100*total/initial:owner.Percent,
                    Era=owner.Era.Length>0?owner.Era:era,EraMm=owner.EraMm??eraMm};
                layers[^1]=merged;
                if(main)
                {
                    residual=exit;stopped|=stop;ricochet|=rico;
                    if(stop)stopLayer??=merged;
                    if(rico)ricochetLayer??=merged;
                }
                continue;
            }
            // 0.22.43: an internal module on the shot line (crew position, engine, fuel tank, ammunition stowage, transmission ...). On its own it is a
            // layer named after the module; grouped with one plate the layer keeps the plate's name (the module is listed under it); a composite
            // array keeps its name.
            var module=info.AirGapMm!=null||armour.Length>1?"":string.Join(" and ",info.Parts.Where(CombatEnvelope.IsInternal).Select(CombatReportView.ModuleName).Distinct());
            string name=info.AirGapMm!=null?"Air gap":armour.Length>1?CombatReportLanguage.ArrayName:module.Length>0&&armour.Length==0?module:
                armour.Length==1?PlateMaterial(armour[0]):
                info.Parts.FirstOrDefault() is {} part?CombatReportView.ModuleName(part):"Layer";
            string plate=info.AirGapMm!=null||armour.Length>1?"":armour.Length==1?CombatReportView.ModuleName(armour[0]):"";
            // 0.22.51: a later crossing of the same object with newly paid material of its own (a folded plate, a housing's far wall).
            if(CombatLayerContinuity.Suffix(continuity.Ordinal(e.Stage,e.HitId??info.Id)) is {Length:>0} crossing)
            {
                if(plate.Length>0)plate+=crossing;
                else if(info.AirGapMm==null&&armour.Length==0)name+=crossing;
            }
            var sub=constituents&&info.AirGapMm==null&&armour.Length==1?CombatCompositeLayers.For(armour[0]):null;
            var layer=new ForensicLayer(layers.Count+1,e.HitId??info.Id,e.Stage,name,info.Parts,info.Combined,info.AirGapMm,before,after,loss,
                main&&initial is >0&&loss!=null?100*loss/initial:null,era,position,path,
                info.AirGapMm!=null?"#4d5965":module.Length>0&&armour.Length==0?CombatEnvelope.Grey:MaterialColour(armour.FirstOrDefault()?.MaterialId),sub,eraMm,plate,Module:module,PaidIntervals:events.Where(MaterialIntervalDisplay.Is).SelectMany(x=>MaterialIntervalDisplay.Sublayers(x,info,events)).ToArray(),PlateStop:events.Select(LayeredPlateReport.ReadStop).FirstOrDefault(x=>x!=null),ModuleOutcome:module.Length>0?ModuleOutcomeOf(shot.Events,info.Parts):"");
            layers.Add(layer);
            position+=path??0;
            if(main)
            {
                residual=after;stopped|=stop;ricochet|=rico;
                if(stop)stopLayer??=layer;
                if(rico)ricochetLayer??=layer;
            }
        }
        return new(layers,hidden,residual,stopped,ricochet,stopLayer,ricochetLayer,starts.Length);
    }
    /// <summary>0.22.43: true when the projectile reached an internal module with residual penetration (the protective envelope was perforated).
    /// Pure and exception-safe: the hit panel calls it once per impact.</summary>
    internal static bool EnvelopePerforated(CombatReportShot shot)
    {
        try{return Envelope(Measure(LayeredPlateReport.Apply(CombatReportTruth.Normalize(shot)),false).Layers).Perforated;}
        catch(Exception){return false;}
    }
    /// <summary>
    /// 0.22.43 (presentation and analysis only): the protective envelope of a shot. The first internal module that the main charge reached with
    /// residual penetration ends it; a module grouped with a plate counts when the projectile left that group with residual penetration, and its
    /// entry value is then the group's exit (a lower bound). No measured value changes here.
    /// </summary>
    internal static ForensicEnvelope Envelope(IReadOnlyList<ForensicLayer> layers)
    {
        var main=layers.Where(l=>l.Stage!="Precursor").ToArray();
        ForensicLayer? first=null;double? entry=null;
        foreach(var l in main.Where(l=>l.Internal))
        {
            bool grouped=l.Parts.Any(p=>p.Armour);
            if(!grouped&&l.Before is >0){first=l;entry=l.Before;break;}
            if(grouped&&l.After is >0){first=l;entry=l.After;break;}
        }
        if(first==null)return ForensicEnvelope.None with{ArmourLayers=main.Where(l=>l.GapMm==null&&!l.Internal).Select(l=>l.Number).ToArray()};
        var ahead=main.TakeWhile(l=>!ReferenceEquals(l,first)).Where(l=>l.GapMm==null&&!l.Internal).Select(l=>l.Number).ToArray();
        var reached=main.Where(l=>l.Internal&&l.Number>=first.Number).ToArray();
        var modules=reached.Select(l=>new ForensicModule(l.Number,l.Module,ReferenceEquals(l,first)?entry:l.Before,l.Percent,
            l.Parts.Any(p=>p.Armour)?l.Plate:"")).ToArray();
        return new(true,ahead,first.Number,entry,CombatEnvelope.Compartment(reached.SelectMany(l=>l.Parts.Where(CombatEnvelope.IsInternal)).ToArray()),modules);
    }
    internal static ForensicModel Build(CombatReportShot raw)
    {
        var shot=LayeredPlateReport.Apply(CombatReportTruth.Normalize(raw));var block=HitBlockBuilder.Build(shot);
        var measured=Measure(shot,true);
        var layers=measured.Layers;int hidden=measured.Hidden;double? initial=shot.InitialPenetrationMm;double? residual=measured.Residual;
        bool stopped=measured.Stopped,ricochet=measured.Ricochet;var stopLayer=measured.StopLayer;var ricochetLayer=measured.RicochetLayer;
        // 0.22.43: the outcome follows the perforation of the protective envelope, not only the place where the projectile came to rest.
        // A module reached without any armour ahead of it (the hit panel's NO ARMOUR) keeps that result word.
        var envelope=Envelope(layers);bool perforated=envelope.Perforated&&!ricochet,bare=perforated&&block.Result==HitBlockBuilder.NoArmour;
        // 0.22.42: the plates of every composite array front to back, each drawn once over the array's extent.
        var arrays=Arrange(layers);
        if(stopLayer!=null)stopLayer=layers[stopLayer.Number-1];
        if(ricochetLayer!=null)ricochetLayer=layers[ricochetLayer.Number-1];
        string outcome=perforated&&!bare?HitBlockBuilder.Penetrated:stopped?(block.Result==HitBlockBuilder.StoppedSpall?block.Result:"STOPPED"):ricochet?"RICOCHET":block.Result;
        if(measured.Starts==0)residual=null;
        double? restPct=initial is >0&&residual!=null&&!ricochet?100*residual/initial:null;
        var mainLayers=layers.Where(l=>l.Stage!="Precursor").ToArray();
        double? unattributed=initial!=null&&residual!=null&&!ricochet?initial-residual-mainLayers.Sum(l=>l.AbsorbedMm??0):null;
        string Pct(double? mm)=>initial is >0&&mm is {} v?$" ({N(100*v/initial)} % of initial penetration)":"";
        var firstModule=perforated?layers[envelope.FirstLayer-1]:null;
        string explanation;
        if(layers.Count==0)explanation="No armour layer data for this impact.";
        else if(perforated)
        {
            // "Perforation of the protective envelope: residual penetration 203.1 mm RHA-eq on entry into the engine compartment (layer 9, fuel tank) (…);
            //  projectile arrested in layer 16 (Rolled Homogeneous Armour) after traversing the engine compartment."
            string where=firstModule!.Module==envelope.Compartment?$"layer {firstModule.Number}":$"layer {firstModule.Number}, {firstModule.Module}";
            explanation=(envelope.ArmourLayers.Count>0?"Perforation of the protective envelope":$"No armour ahead of the {envelope.Compartment}")+
                $": residual penetration {N(envelope.EntryMm)} mm RHA-eq on entry into the {envelope.Compartment} ({where}){Pct(envelope.EntryMm)}";
            if(stopped&&stopLayer!=null)explanation+=stopLayer.Internal&&!stopLayer.Grouped?$"; projectile arrested in {StopRef(stopLayer)}.":$"; projectile arrested in {StopRef(stopLayer)} after traversing the {envelope.Compartment}.";
            else if(stopped)explanation+="; projectile arrested.";
            else if(residual is >0&&mainLayers.LastOrDefault() is {} last)explanation+=$"; residual penetration {N(residual)} mm RHA-eq behind layer {last.Number}{Pct(residual)}.";
            else explanation+=".";
        }
        else if(stopped)explanation=(stopLayer!=null?$"Projectile arrested in {StopRef(stopLayer)}":"Projectile arrested")+"; residual penetration 0 mm RHA-eq.";
        else if(ricochet)explanation="Ricochet"+(ricochetLayer!=null?$" at {Ref(ricochetLayer)}"+(Obliquity(ricochetLayer) is {} theta?$", obliquity {N(theta)}°":""):"")+".";
        else if(outcome==HitBlockBuilder.Penetrated&&residual is {} res)explanation=$"Perforation: residual penetration {N(res)} mm RHA-eq behind the armour{Pct(res)}.";
        else{var last=mainLayers.LastOrDefault()??layers[^1];explanation=$"Last measured layer: {Ref(last)}"+(last.Before is {} entry?$", entry penetration {N(entry)} mm RHA-eq":"")+".";}
        var findings=new List<string>{explanation};var advice=new List<string>();
        if(perforated)
        {
            // 0.22.43: the envelope ahead of the first internal module, and every internal module reached, in order, with its entry value.
            if(envelope.ArmourLayers.Count>0)findings.Add($"Protective envelope ahead of the {envelope.Compartment}: {LayerList(envelope.ArmourLayers)}, reducing the penetration"+
                (initial is >0?$" from {N(initial)}":"")+$" to {N(envelope.EntryMm)} mm RHA-eq.");
            findings.Add($"Internal modules on the shot line, in order: "+string.Join("; ",envelope.Modules.Select(x=>$"layer {x.Layer} {x.Name}"+(x.With.Length>0?$" together with {x.With}":"")+
                (x.EntryMm is {} e?$", entry {N(e)} mm RHA-eq":"")))+".");
        }
        var largest=mainLayers.Where(l=>l.AbsorbedMm>0).OrderByDescending(l=>l.AbsorbedMm).FirstOrDefault();
        if(largest!=null)findings.Add($"Largest reduction: {Ref(largest)}, {N(largest.AbsorbedMm)} mm RHA-eq{Pct(largest.AbsorbedMm)}.");
        foreach(var l in layers.Where(l=>EraText(l).Length>0))
            findings.Add($"Layer {l.Number}: {EraText(l)}"+(l.Era=="ERA activated"&&l.EraMm is >0?$"; ERA reduction {N(l.EraMm)} mm RHA-eq":"")+".");
        // Obliquity is stated where it lengthens the path by at least 5 % (1 / cos θ ≥ 1.05, θ ≥ 17.8°).
        var angled=layers.Where(l=>l.GapMm==null).SelectMany(l=>l.Parts.Where(p=>p.Armour&&p.AngleDegrees>0&&Path(p) is {} los&&los>=1.05*p.ThicknessMm).Select(p=>(l.Number,p))).ToArray();
        if(angled.Length>0)
        {
            var item=angled.OrderByDescending(x=>Path(x.Item2)-x.Item2.ThicknessMm).First();var p=item.Item2;
            findings.Add($"Obliquity: layer {item.Item1} at {N(p.AngleDegrees)}° gives a line-of-sight thickness of {N(Path(p))} mm for a normal thickness of {N(p.ThicknessMm)} mm.");
            advice.Add($"Obliquity is effective at layer {item.Item1}: {N(p.AngleDegrees)}° multiplies the normal thickness by {(1/Math.Cos(p.AngleDegrees!.Value*Math.PI/180)).ToString("0.00",CultureInfo.InvariantCulture)} (1 / cos θ).");
        }
        foreach(var a in arrays)
            findings.Add((a.Layers.Count==1?$"Layer {a.First} is a composite array":$"{Capital(LayerList(a.Layers))} form one composite array")+
                $" of {a.Plates.Count} plates, front to back: {string.Join(", ",a.Plates.Select(p=>p.Material))}; "+
                (a.Layers.Count==1?"its reduction is stated for the plates together.":"each layer states the reduction of its plates together."));
        foreach(var l in layers.Where(l=>l.Sublayers is {Count:>0}))
            findings.Add($"Layer {l.Number} ({l.Name}): {l.Sublayers!.Count} constituent layers of the composer preset, scaled to the {N(l.Parts.First(p=>p.Armour).ThicknessMm)} mm normal thickness of the plate.");
        foreach(var a in arrays)foreach(var p in a.Plates.Where(p=>p.Sublayers is {Count:>0}))
        {
            // The layers whose rows list these constituents (a plate without a measured thickness in a layer lists none there).
            var holding=layers.Where(l=>l.ArrayFirst==a.First&&l.Plates!.Any(x=>x.Part.Entity==p.Part.Entity&&x.Sublayers is {Count:>0})).Select(l=>l.Number).ToArray();
            findings.Add($"{Capital(LayerList(holding))}, {p.Material}: {p.Sublayers!.Count} constituent layers of the composer preset, scaled to the {N(p.Part.ThicknessMm)} mm normal thickness of the plate.");
        }
        if(unattributed is {} u&&Math.Abs(u)>.01)findings.Add(u>0?$"Reduction between layers: {N(u)} mm RHA-eq.":$"Increase between layers: {N(-u)} mm RHA-eq.");
        var damage=Effects(CombatDamageSummary.From(shot.Events),shot.CrewTotal);
        findings.Add("Behind-armour effects: "+damage);
        if(shot.CrewTotal!=null&&shot.CrewAlive!=null)findings.Add($"Crew alive after impact: {shot.CrewAlive} of {shot.CrewTotal}.");
        if(layers.Any(l=>l.Stage=="Precursor"))findings.Add("Precursor-charge layers are listed separately and excluded from the main-charge shares.");
        if(shot.OmittedEvents>0)findings.Add($"The technical log omits {shot.OmittedEvents} events beyond its size limit.");
        // 0.22.43: the protection that was needed is the residual on entry into the envelope's compartment; never "no additional protection" when
        // internal modules were hit (by the projectile, or by spall behind an arrest).
        bool hurt=HitResultRule.InternalDamage(shot.Events)||CombatDamageSummary.IsDestroyed(shot);
        if(perforated)advice.Insert(0,$"Preventing perforation required at least {N(envelope.EntryMm)} mm RHA-eq of additional protection ahead of the {envelope.Compartment} (RHA equivalent, not a plate thickness).");
        else if(stopped&&hurt)advice.Insert(0,(stopLayer!=null?$"The projectile was arrested in {StopRef(stopLayer)}":"The projectile was arrested")+"; the internal damage arose from behind-armour effects (spall).");
        else if(stopped)advice.Insert(0,"No additional protection was required against this impact"+(stopLayer!=null?$": the projectile was arrested in {StopRef(stopLayer)}.":"."));
        else if(outcome==HitBlockBuilder.Penetrated&&residual is >0)advice.Insert(0,$"Arresting this projectile requires at least {N(residual)} mm RHA-eq of additional protection at this point of impact (RHA equivalent, not a plate thickness).");
        var initiated=layers.Where(l=>l.Era=="ERA activated"&&l.EraMm is >0).ToArray();
        if(initiated.Length>0){var sum=initiated.Sum(l=>l.EraMm!.Value);advice.Add($"The ERA element{(initiated.Length==1?"":"s")} reduced the penetration by {N(sum)} mm RHA-eq{Pct(sum)}.");}
        var spent=layers.Where(l=>l.Era=="ERA already spent").ToArray();
        if(spent.Length>0)advice.Add($"The ERA element at {LayerList(spent)} had been initiated by an earlier impact; this projectile met the remaining layers only.");
        string speed=shot.SpeedMps is {} mps?$", {N(mps)} m/s":"";
        string context=$"Impact {shot.Number}: {shot.Profile}{speed} → {shot.Target??"target vehicle"}."+(layers.Count>0?" Layers: "+string.Join(" → ",layers.Select(l=>$"{l.Number} {Short(l)}")):"");
        return new(shot.Number,outcome,stopped&&!perforated?"#e07a4f":block.Colour,explanation,context,layers,initial,residual,restPct,unattributed,findings,advice,damage,hidden,arrays,
            envelope,stopped);
    }
    /// <summary>
    /// 0.22.72 (presentation only): what the report recorded for the internal modules of one layer, from the "interaction" events of the shot ("Crew member Gunner killed",
    /// "Engine damaged"). An event belongs to a part by its object id, or by the readable name when the seat or part has one. Several modules in a layer: the most severe
    /// result. Empty when no event names them.
    /// </summary>
    internal static string ModuleOutcomeOf(IReadOnlyList<CombatReportEvent> events,IReadOnlyList<CombatLayerPart> parts)
    {
        int best=0;string text="";
        foreach(var e in events)
        {
            if(e==null||e.Kind!="interaction")continue;
            var t=(e.Text??"").Trim();
            string verb=t.EndsWith(" killed",StringComparison.Ordinal)?"killed":t.EndsWith(" wounded",StringComparison.Ordinal)?"wounded":t.EndsWith(" destroyed",StringComparison.Ordinal)?"destroyed":t.EndsWith(" damaged",StringComparison.Ordinal)?"damaged":"";
            if(verb.Length==0)continue;
            string subject=t[..^(verb.Length+1)].Trim();
            bool crewEvent=subject.StartsWith("Crew member",StringComparison.OrdinalIgnoreCase)||subject.StartsWith("Crew seat",StringComparison.OrdinalIgnoreCase);
            foreach(var p in parts.Where(CombatEnvelope.IsInternal))
            {
                bool match=e.RelatedEntity is {} id&&id==p.Entity;
                if(!match)
                {
                    string a=crewEvent?CombatDamageSummary.CrewLabel(subject):CombatDamageSummary.PartLabel(subject),b=CombatReportView.ModuleName(p);
                    if(crewEvent)b=CombatDamageSummary.CrewLabel(p.Name);
                    match=a.Length>0&&!(crewEvent&&CombatDamageSummary.IsAnonymous(a))&&string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
                }
                if(!match)continue;
                int rank=verb is "killed" or "destroyed"?2:1;
                if(rank>best){best=rank;text=verb=="killed"?"incapacitated":verb;}
            }
        }
        return text;
    }
    /// <summary>The material name of a plate as the user gave it in the composer, else its readable part name.</summary>
    internal static string PlateMaterial(CombatLayerPart p)=>CombatReportLanguage.Material(p) is {Length:>0} m?m:CombatReportView.ModuleName(p);
    /// <summary>
    /// 0.22.42: the plates of a composite array front to back, derived from the sequence of its layers (consecutive native groups that share
    /// plates). A plate that occurs in an earlier layer and no longer in a later one lies in front; a plate that remains to the last layer lies
    /// behind: plates are ordered by the last layer that holds them. Plates that leave in the same layer keep the order of that layer's plate
    /// list, which is also the order of a single group. Returns the plate entities.
    /// </summary>
    internal static IReadOnlyList<int> PlateOrder(IReadOnlyList<IReadOnlyList<CombatLayerPart>> sequence)
    {
        var last=new Dictionary<int,(int Layer,int Index)>();
        for(int k=0;k<sequence.Count;k++)
        {
            var seen=new HashSet<int>();int index=0;
            foreach(var p in sequence[k].Where(p=>p.Armour)){if(!seen.Add(p.Entity))continue;last[p.Entity]=(k,index++);}
        }
        return last.OrderBy(x=>x.Value.Layer).ThenBy(x=>x.Value.Index).Select(x=>x.Key).ToArray();
    }
    /// <summary>Consecutive layers of the same stage, each a native group of several plates, that share at least one plate.</summary>
    private static List<List<int>> Runs(IReadOnlyList<ForensicLayer> layers)
    {
        var runs=new List<List<int>>();List<int>? run=null;
        for(int i=0;i<layers.Count;i++)
        {
            var l=layers[i];
            if(!l.Array){run=null;continue;}
            var plates=l.Parts.Where(p=>p.Armour).Select(p=>p.Entity).ToHashSet();
            var previous=run==null?null:layers[run[^1]];
            if(previous!=null&&previous.Stage==l.Stage&&previous.Parts.Any(p=>p.Armour&&plates.Contains(p.Entity)))run!.Add(i);
            else{run=new List<int>{i};runs.Add(run);}
        }
        return runs;
    }
    /// <summary>
    /// 0.22.42 (presentation only): gives every layer of a composite array its plates front to back, and lays the array out along the drawn shot
    /// line as the sum of its plates' line-of-sight thicknesses, each plate once (its first measured normal thickness and obliquity). The array's
    /// layers share that extent in the proportion of their own measured paths (equal parts when one is missing); later layers move with it.
    /// An array without any measured plate geometry keeps its positions. No value of penetration, reduction or share changes here.
    /// </summary>
    private static List<ForensicArray> Arrange(List<ForensicLayer> layers)
    {
        var arrays=new List<ForensicArray>();double shift=0;int next=0;
        foreach(var run in Runs(layers))
        {
            for(;next<run[0];next++)layers[next]=Shift(layers[next],shift);
            var order=PlateOrder(run.Select(i=>layers[i].Parts).ToArray());
            var rank=new Dictionary<int,int>();for(int k=0;k<order.Count;k++)rank[order[k]]=k;
            ForensicArrayPlate Plate(CombatLayerPart p)=>new(rank[p.Entity]+1,p,CombatReportView.ModuleName(p),PlateMaterial(p),MaterialColour(p.MaterialId),CombatCompositeLayers.For(p));
            var unique=order.Select(entity=>
            {
                var all=run.SelectMany(i=>layers[i].Parts).Where(p=>p.Armour&&p.Entity==entity).ToArray();
                return Plate(all.FirstOrDefault(p=>Path(p)!=null)??all[0]);
            }).ToArray();
            double before=run.Sum(i=>layers[i].PathMm??0),extent=unique.Sum(p=>Path(p.Part)??0);
            bool keep=!(extent>0&&double.IsFinite(extent));
            double start=layers[run[0]].StartMm+shift,at=start;
            var weights=run.All(i=>layers[i].PathMm is >0)?run.Select(i=>layers[i].PathMm!.Value).ToArray():run.Select(_=>1d).ToArray();
            double total=weights.Sum();
            for(int k=0;k<run.Count;k++)
            {
                var l=layers[run[k]];
                var plates=l.Parts.Where(p=>p.Armour).DistinctBy(p=>p.Entity).OrderBy(p=>rank[p.Entity]).Select(Plate).ToArray();
                double? path=keep?l.PathMm:extent*weights[k]/total;
                layers[run[k]]=l with{StartMm=keep?l.StartMm+shift:at,PathMm=path,Plates=plates,ArrayFirst=layers[run[0]].Number};
                at+=path??0;
            }
            var percents=run.Select(i=>layers[i].Percent).Where(p=>p!=null).ToArray();
            arrays.Add(new(run.Select(i=>layers[i].Number).ToArray(),unique,start,keep?before:extent,percents.Length>0?percents.Sum():null));
            if(!keep)shift+=extent-before;
            next=run[^1]+1;
        }
        for(;next<layers.Count;next++)layers[next]=Shift(layers[next],shift);
        return arrays;
    }
    private static ForensicLayer Shift(ForensicLayer l,double by)=>by==0?l:l with{StartMm=l.StartMm+by};
    internal static string LayerList(IReadOnlyList<int> numbers)=>numbers.Count==1?"layer "+numbers[0]:
        "layers "+string.Join(", ",numbers.Take(numbers.Count-1))+" and "+numbers[^1];
    /// <summary>"crew casualties: 1 killed (gunner) of 4; ammunition stowage, engine destroyed." or "no crew casualties or module damage."</summary>
    internal static string Effects(CombatDamageSummary d,int? crewTotal)
    {
        var bits=new List<string>();
        if(d.CrewKilled>0||d.CrewWounded>0)
        {
            var crew=new List<string>();
            if(d.CrewKilled>0)crew.Add($"{d.CrewKilled} killed"+(d.KilledRoles.Count>0?" ("+string.Join(", ",d.KilledRoles)+")":""));
            if(d.CrewWounded>0)crew.Add($"{d.CrewWounded} wounded"+(d.WoundedRoles.Count>0?" ("+string.Join(", ",d.WoundedRoles)+")":""));
            bits.Add("crew casualties: "+string.Join(", ",crew)+(crewTotal is >0?$" of {crewTotal}":""));
        }
        var parts=d.PartsLine();if(parts.Length>0)bits.Add(parts);
        return (bits.Count==0?ForensicModel.NoDamage:string.Join("; ",bits))+".";
    }
    private static double? Obliquity(ForensicLayer l)=>l.Parts.Where(p=>p.Armour).Select(p=>p.AngleDegrees).FirstOrDefault(a=>a!=null);
    internal static string Capital(string s)=>s.Length==0?s:char.ToUpperInvariant(s[0])+s[1..];
}

/// <summary>
/// 0.22.43: which recorded parts are internal modules behind the armour, and the compartment they name. Only the vehicle's own modules count
/// (crew positions, engine, motor, fuel tank, ammunition stowage, transmission, laying drive, turret basket); add-on armour and ERA parts, tracks,
/// the main gun, the turret ring and generic models do not. Pure; no game state.
/// </summary>
internal static class CombatEnvelope
{
    /// <summary>The fill of an internal module in the cross-section and the list (a neutral grey, distinct from the steel blue of RHA).</summary>
    internal const string Grey="#9c9c97";
    private static readonly Dictionary<string,string?> Modules=new(StringComparer.OrdinalIgnoreCase)
    {
        ["engine"]="engine compartment",["motor"]="engine compartment",["transmission"]="engine compartment",["gearbox"]="engine compartment",["radiator"]="engine compartment",
        ["ammoRack"]="ammunition stowage",["ammo rack"]="ammunition stowage",["layingDrive"]="crew compartment",["basket"]="crew compartment",
        // A fuel tank lies in the compartment of the module it is next to on the shot line.
        ["fuelTank"]=null,["fuel tank"]=null
    };
    private static bool Crew(string name)=>name.StartsWith("Crew member",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Crew seat",StringComparison.OrdinalIgnoreCase);
    internal static bool IsInternal(CombatLayerPart p)=>!p.Armour&&p.Name is {} n&&(Crew(n.Trim())||Modules.ContainsKey(n.Trim()));
    private static string? Class(CombatLayerPart p)=>Crew(p.Name.Trim())?"crew compartment":Modules.TryGetValue(p.Name.Trim(),out var c)?c:null;
    /// <summary>The compartment of the first module with a known compartment, in shot-line order; else the first module's own name.</summary>
    internal static string Compartment(IReadOnlyList<CombatLayerPart> modules)=>
        modules.Select(Class).FirstOrDefault(c=>c!=null)??(modules.Count>0?CombatReportView.ModuleName(modules[0]):"vehicle interior");
}
