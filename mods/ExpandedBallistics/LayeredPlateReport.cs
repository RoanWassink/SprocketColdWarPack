using System.Globalization;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>0.22.71: the text of the log line per hit on a plate in layers (only built when the setting Composite fill / Layer hit log is on).</summary>
internal static class LayerHitLogText
{
    internal static string Uniform(long entity,IReadOnlyList<LayerChord> chords,double[] factors)=>
        "[Layer faces] plate #"+entity+" (uniform): "+string.Join("; ",chords.Select((c,i)=>string.Create(CultureInfo.InvariantCulture,$"[{c.StartM:0.######},{c.EndM:0.######}] {c.Angle:0.#}° x {factors[i]:0.####}")))+".";
    internal static string PerLayer(long entity,IReadOnlyList<LayerChord> chords,double[] factors,int fallbacks)=>
        "[Layer faces] plate #"+entity+": "+string.Join("; ",chords.Select((c,i)=>string.Create(CultureInfo.InvariantCulture,$"rank {c.Rank}{(c.Back?" back":"")} [{c.StartM:0.######},{c.EndM:0.######}] x {factors[i]:0.####}")))+$"; {fallbacks} by the table.";
}

/// <summary>0.22.69: where a shot that stopped inside a plate in layers ran out of solid material: layer <see cref="Group"/> of <see cref="Groups"/> of the recipe, at <see cref="Fraction"/> of the recipe depth (air included) from the front.</summary>
internal sealed record LayerStop(int Group,int Groups,double Fraction);

/// <summary>
/// 0.22.69: a plate in layers is paid by the uniform hit model (RuntimeGroupedProtection, one factor per solid millimetre), and its report is one line. The game runs the shot through
/// one native block per layer and per air gap; this presentation step (called by the readers of a report: <see cref="CombatHitProjection.Build"/> and <see cref="CombatForensics"/>) replaces the
/// blocks of one plate in layers on the shot line by one block: the plate, its recipe, the solid material on the line, the angle, the penetration in and out and one reaction
/// ("T-90A welded turret, 13 layers: 387 mm × 0.71 = 276 mm RHA-eq"). The lines about a layer face that was not found or about a side wall, and the lines of the air gaps between
/// the layers, are not shown any more. Pure; the protection and the native simulation are not touched, and a report without the marker event "layeredPlate" is left as it is.
/// Where the shot stopped inside the plate, the place is computed from the same numbers as the hit (how far the penetration reached through the solid material) and written into the marker.
/// </summary>
internal static class LayeredPlateReport
{
    internal const string Marker="layeredPlate",Merged="layeredPlateMerged";
    /// <summary>0.22.81: the marker a paid cavity of a plate marked [Solid block] leaves in the report (one per passage; <see cref="SolidBlockMarker"/>).</summary>
    internal const string SolidMarker="solidBlock";
    private static readonly Regex Header=new(@"^(?<label>.*?): plate in layers, (?<n>\d+) layers(?:; (?<solid>\d+(?:\.\d+)?) mm solid in (?<extent>\d+(?:\.\d+)?) mm)?\.",RegexOptions.CultureInvariant);
    private static readonly Regex Arrest=new(@"Arrested in layer (?<g>\d+) of (?<n>\d+) at (?<f>\d+(?:\.\d+)?) % of the plate depth\.",RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Kept=new(StringComparer.Ordinal){"nativeStop","materialStop","outcome","interaction","heavyEra","lightEra","eraSpent","eraUnavailable","eraResidual","eraReaction","materialUnavailable"};

    /// <summary>The angle that half of the solid material of the line lies below (weighted by length): a short steep piece, as a side wall gives, does not move it.</summary>
    private static double MedianAngle(IReadOnlyList<PaidMaterialPiece> pieces,double solid)
    {
        double half=solid/2,run=0;
        foreach(var p in pieces.OrderBy(p=>p.Angle)){run+=p.LengthMm;if(run>=half)return p.Angle;}
        return pieces[^1].Angle;
    }
    /// <summary>The recorded place of the arrest in a merged plate, or null.</summary>
    internal static LayerStop? ReadStop(CombatReportEvent marker)
    {
        if(marker.Kind!=Merged)return null;
        var m=Arrest.Match(marker.Text);
        if(!m.Success)return null;
        return new(int.Parse(m.Groups["g"].Value,CultureInfo.InvariantCulture),int.Parse(m.Groups["n"].Value,CultureInfo.InvariantCulture),double.Parse(m.Groups["f"].Value,CultureInfo.InvariantCulture)/100);
    }

    /// <summary>The shot with its plates in layers merged (the shot itself when it has none). Called by the readers of a report (the list, Forensic, the HTML); the stored report, its JSON and <see cref="CombatReportTruth.Normalize"/> stay as they are.
    /// 0.22.88: after the merge, a plate thickness the hit did not give is derived from the paid path (<see cref="DeriveThickness"/>).
    /// 0.22.91: then a module of parts that are no plate (VARMA, a side module) is one layer with its confirmed intervals as sublayers, and a vehicle part without a material is not named "ERA" (<see cref="CombatModuleLayers"/>).</summary>
    internal static CombatReportShot Apply(CombatReportShot shot)
    {
        var events=CombatModuleLayers.Apply(DeriveThickness(Collapse(shot.Events),shot.Events)); // 0.22.91
        return ReferenceEquals(events,shot.Events)?shot:shot with{Events=events};
    }

    /// <summary>0.22.88: the text after the name of a part whose normal thickness was derived from the paid path, not given by the hit.</summary>
    internal const string DerivedLabel=" (derived from the path)";
    /// <summary>
    /// 0.22.88 (user 15-11, "Ja, als 0.22.88"): the hit gives no normal thickness for a plate when the face it reads cannot be resolved (THICKNESS-NULL-DIAGNOSE). For an armour
    /// part of a solid layer with a measured obliquity and no thickness, the thickness for the readers is the paid length of that entity in the same hit (the PaidIntervals of
    /// its interval lines, EndM − StartM, each interval once) × cos(obliquity); its name gets <see cref="DerivedLabel"/>. A thickness the hit gave is never replaced; without a
    /// paid interval or an obliquity the part stays as it is. A merged plate in layers or a solid block (the merged marker of <see cref="Collapse"/>, or a part of an entity that
    /// carries a "layeredPlate" or "solidBlock" marker in the recorded report) keeps what that path gives. Pure: returns the same list when nothing is derived; the stored report
    /// and its JSON are not touched. <paramref name="recorded"/> is the report before <see cref="Collapse"/> (which removes the solid-block markers).
    /// </summary>
    internal static IReadOnlyList<CombatReportEvent> DeriveThickness(IReadOnlyList<CombatReportEvent> events,IReadOnlyList<CombatReportEvent>? recorded=null)
    {
        if(events==null||events.Count==0)return events??Array.Empty<CombatReportEvent>();
        List<CombatReportEvent>? copy=null;
        for(int i=0;i<events.Count;i++)
        {
            var e=events[i];
            if(e==null||e.Kind!="layerStart"||e.Layer is not {AirGapMm:null} layer||layer.Parts==null||!layer.Parts.Any(Missing))continue;
            string id=e.HitId??layer.Id;
            if(events.Any(x=>x!=null&&x.Kind==Merged&&x.Stage==e.Stage&&x.HitId==id))continue;
            var paid=events.Where(x=>x!=null&&x.Stage==e.Stage&&x.HitId==id&&x.Kind is "materialReaction" or "materialStop"&&x.PaidIntervals!=null).SelectMany(x=>x.PaidIntervals!).ToArray();
            if(paid.Length==0)continue;
            var parts=layer.Parts.ToArray();bool changed=false;
            for(int k=0;k<parts.Length;k++)
            {
                var p=parts[k];
                if(!Missing(p)||Marked(p.Entity,e.Stage))continue;
                var pieces=paid.Where(x=>x!=null&&x.Entity==p.Entity&&double.IsFinite(x.StartM)&&double.IsFinite(x.EndM)&&x.EndM>x.StartM).Select(x=>(x.StartM,x.EndM)).Distinct().ToArray();
                if(pieces.Length==0)continue;
                double thickness=pieces.Sum(x=>(x.EndM-x.StartM)*1000)*Math.Cos(p.AngleDegrees!.Value*Math.PI/180);
                if(!(thickness>0)||!double.IsFinite(thickness))continue;
                parts[k]=p with{ThicknessMm=thickness,Name=p.Name+DerivedLabel};changed=true;
            }
            if(!changed)continue;
            copy??=events.ToList();
            copy[i]=e with{Layer=layer with{Parts=parts}};
        }
        return copy??events;
        static bool Missing(CombatLayerPart p)=>p!=null&&p.Armour&&p.ThicknessMm==null&&p.AngleDegrees is {} a&&double.IsFinite(a)&&a>=0&&a<89.9;
        bool Marked(int entity,string stage)=>events.Concat(recorded??Array.Empty<CombatReportEvent>()).Any(x=>x!=null&&x.Kind is Marker or SolidMarker&&x.RelatedEntity==entity&&x.Stage==stage);
    }

    internal static IReadOnlyList<CombatReportEvent> Collapse(IReadOnlyList<CombatReportEvent> events)
    {
        // 0.22.81: a report with neither marker is returned as it is (the same list), as before
        if(events==null||events.Count==0||!events.Any(e=>e!=null&&(e.Kind==Marker||e.Kind==SolidMarker)&&e.RelatedEntity!=null))return events??Array.Empty<CombatReportEvent>();
        var list=events.Where(e=>e!=null).ToList();
        foreach(var marker in events.Where(e=>e!=null&&e.Kind==Marker&&e.RelatedEntity!=null).GroupBy(e=>(e.Stage,e.RelatedEntity!.Value)).Select(g=>g.First()).ToArray())
        {
            try{list=CollapseOne(list,marker);}catch(Exception){}
        }
        // 0.22.81: every paid passage of a solid block is one part; a marker that cannot be merged is dropped, the report stays as it was recorded
        foreach(var marker in events.Where(e=>e!=null&&e.Kind==SolidMarker&&e.RelatedEntity!=null).ToArray())
        {
            try{list=CollapseSolid(list,marker);}catch(Exception){}
        }
        list.RemoveAll(e=>e.Kind==SolidMarker);
        return list;
    }

    /// <summary>
    /// 0.22.81 (user 14-11: "Overigens zie ik bij forensic niet meer de laagjes waar het schot doorheen ging"): one paid passage through a plate marked [Solid block] is one part,
    /// from the entry of its front skin to the exit of its back skin, with the recipe's layers over that whole length (Forensic scales them to the part, air as air). The native
    /// blocks of the passage (front skin, the cavity as an air gap, back skin) become one block: the cavity line as recorded (what was paid for the cavity, once), one interval line
    /// for front skin, cavity and back skin, the exit of the back skin. The run stops at a block of another object (that block keeps its own row; the part then ends at the back
    /// skin's entry), at a stop, or at a block of the same plate behind the back skin (a second passage, which has its own marker). Pure; nothing else is changed.
    /// </summary>
    private static List<CombatReportEvent> CollapseSolid(List<CombatReportEvent> list,CombatReportEvent marker)
    {
        int entity=marker.RelatedEntity!.Value;string stage=marker.Stage;
        if(SolidBlockMarker.Parse(marker.Text) is not {} info||marker.HitId==null)return list;
        const double Tol=SolidBlock.MergeGapM;
        var starts=Enumerable.Range(0,list.Count).Where(i=>list[i].Kind=="layerStart"&&list[i].Layer!=null&&list[i].Stage==stage).ToArray();
        int at=Array.FindIndex(starts,i=>Id(list[i])==marker.HitId);
        if(at<0||Class(list[starts[at]],entity)!=1)return list;
        PaidMaterialPiece[] Own(string id)=>list.Where(e=>e.Stage==stage&&e.HitId==id&&e.Kind is "materialReaction" or "materialStop"&&e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!)
            .Where(p=>p.Entity==entity&&p.EndM>p.StartM&&double.IsFinite(p.Factor)).ToArray();
        bool Stops(string id)=>list.Any(e=>e.Stage==stage&&e.HitId==id&&(e.Kind is "nativeStop" or "materialStop"||e.Kind=="outcome"&&e.Text.StartsWith("Stopped",StringComparison.OrdinalIgnoreCase)||
            e.Kind=="materialReaction"&&e.AfterMm==0));
        var run=new List<int>{starts[at]};var air=new List<int>();
        bool back=Own(Id(list[starts[at]])).Any(p=>p.EndM>info.BackStartM-Tol),stopped=Stops(Id(list[starts[at]]));
        for(int k=at+1;k<starts.Length&&!back&&!stopped;k++)
        {
            var start=list[starts[k]];int kind=Class(start,entity);
            if(kind==2){air.Add(starts[k]);continue;}
            if(kind!=1)break;
            var paid=Own(Id(start));
            if(paid.Length>0&&paid.Min(p=>p.StartM)>info.BackEndM+Tol)break; // the same plate behind this passage
            run.AddRange(air);air.Clear();run.Add(starts[k]);
            back=paid.Any(p=>p.EndM>info.BackStartM-Tol);stopped=Stops(Id(start));
        }
        var ids=run.Select(i=>Id(list[i])).ToHashSet(StringComparer.Ordinal);
        var inside=Enumerable.Range(0,list.Count).Where(i=>list[i].Stage==stage&&list[i].HitId!=null&&ids.Contains(list[i].HitId!)||run.Contains(i)).ToArray();
        var first=list[run[0]];string mergedId=Id(first);
        var firstPart=first.Layer!.Parts.First(p=>p.Armour&&p.Entity==entity);
        double? before=first.BeforeMm;
        var lastSolidId=Id(list[run.Last(i=>Class(list[i],entity)==1)]);
        var events=inside.Select(i=>list[i]).ToArray();
        // the cavity line of this passage, as the hit recorded it (in the block of the front skin)
        var cavity=events.Where(e=>e.Kind=="materialReaction"&&e.PaidIntervals==null&&e.HitId==mergedId&&e.Text.StartsWith(info.Label+": solid block",StringComparison.Ordinal)).ToArray();
        bool terminalStop=events.Any(e=>e.Kind is "nativeStop" or "materialStop"||e.Kind=="outcome"&&e.Text.StartsWith("Stopped",StringComparison.OrdinalIgnoreCase))||cavity.Any(e=>e.AfterMm==0);
        var lastResidual=events.LastOrDefault(e=>e.HitId==lastSolidId&&e.Kind is "plateResidual" or "componentResidual");
        double? after=terminalStop?0:lastResidual?.AfterMm??events.LastOrDefault(e=>e.HitId==lastSolidId&&e.AfterMm!=null)?.AfterMm;
        // the length: the whole passage; when the back skin lies in a block of another object, up to the entry of the back skin
        bool whole=back||terminalStop;
        double extentMm=((whole?info.BackEndM:info.BackStartM)-info.FrontStartM)*1000;
        if(!(extentMm>0)||!double.IsFinite(extentMm))return list;
        double angle=Math.Clamp(double.IsFinite(info.Angle)?info.Angle:firstPart.AngleDegrees??0,0,89.9);
        string material=firstPart.MaterialId??info.Label;
        string Side(PaidMaterialPiece p)=>p.EndM<=info.FrontEndM+Tol?"front skin":p.StartM>=info.BackStartM-Tol?"back skin":"skin";
        var pieces=events.Where(e=>e.Kind is "materialReaction" or "materialStop"&&e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!)
            .Where(p=>p.Entity==entity&&p.EndM>p.StartM&&double.IsFinite(p.Factor)).Select(p=>p with{Label=info.Label+", "+Side(p)}).ToList();
        if(info.CavityMm>0)pieces.Add(new(entity,material,info.Label+", cavity",info.FrontEndM,info.FrontEndM+info.CavityMm/1000,info.Factor,info.Angle));
        pieces.Sort((a,b)=>a.StartM.CompareTo(b.StartM));
        var part=firstPart with{Name=SolidName(firstPart.Name),AngleDegrees=Math.Clamp(info.Angle,0,90),ThicknessMm=extentMm*Math.Cos(angle*Math.PI/180),Armour=true};
        var created=new List<CombatReportEvent>{new("layerStart",stage,"Reached layer",before,HitId:mergedId,Layer:new CombatLayerInfo(mergedId,new[]{part},false,null))};
        foreach(var c in cavity)created.Add(c with{HitId=mergedId});
        double? skinsBefore=cavity.LastOrDefault(c=>c.AfterMm!=null)?.AfterMm??before;
        bool skinsPaid=pieces.Any(p=>!p.Label.EndsWith(", cavity",StringComparison.Ordinal));
        // one interval line for the part: front skin, cavity and back skin (the reaction of the row); its budget is that of the skins, the cavity line above holds its own
        if(pieces.Count>0)created.Add(new("materialReaction",stage,"Confirmed material intervals: "+string.Join("; ",pieces.Select(p=>p.Text)),skinsPaid?skinsBefore:null,skinsPaid?after:null,mergedId){PaidIntervals=pieces.ToArray()});
        foreach(var i in inside)
            if(Kept.Contains(list[i].Kind)&&list[i].Layer==null)created.Add(list[i] with{HitId=mergedId});
        if(!terminalStop&&lastResidual!=null)created.Add(lastResidual with{HitId=mergedId,BeforeMm=before});
        string mergedText=string.Create(CultureInfo.InvariantCulture,$"{info.Label}: solid block, {extentMm:0.#} mm along the shot line ({(whole?"front skin, cavity and back skin":"front skin and cavity; the back skin lies in the next layer")}).");
        if(terminalStop&&before is {} entry&&LocateSolid(material,pieces,entry,extentMm,info.FrontStartM) is {} stop)
            mergedText+=string.Create(CultureInfo.InvariantCulture,$" Arrested in layer {stop.Group} of {stop.Groups} at {100*stop.Fraction:0.#} % of the plate depth.");
        created.Add(new(Merged,stage,mergedText,before,after,mergedId,RelatedEntity:entity));
        var result=new List<CombatReportEvent>(list.Count);int insertAt=inside.Min();var remove=inside.ToHashSet();
        for(int i=0;i<list.Count;i++)
        {
            if(i==insertAt)result.AddRange(created);
            if(!remove.Contains(i))result.Add(list[i]);
        }
        return result;
    }
    /// <summary>0.22.81: the plate name of a solid block up to its mark ("Composite armour plate [Solid block]"), without a later suffix.</summary>
    internal static string SolidName(string name)
    {
        int i=(name??"").IndexOf("[Solid block]",StringComparison.Ordinal);
        return i<0?name??"":name![..(i+"[Solid block]".Length)];
    }
    /// <summary>0.22.81: true for the merged marker of a solid block (its text, written by <see cref="CollapseSolid"/>).</summary>
    internal static bool IsSolidMerged(CombatReportEvent e)=>e.Kind==Merged&&e.Text.Contains(": solid block, ",StringComparison.Ordinal)&&e.Text.Contains(" mm along the shot line (",StringComparison.Ordinal);
    /// <summary>
    /// 0.22.81: where a shot that stopped inside a solid block ran out: its pieces in order along the line (front skin, cavity, back skin) are paid length x factor from the entry
    /// penetration (as <see cref="Locate(CassettePreset,int,IReadOnlyList{PaidMaterialPiece},double,double?)"/> does for a plate in layers); the place is that share of the part, and
    /// the layer is the recipe layer at that depth (the recipe over the whole part, as Forensic draws it). Null when the recipe is not known.
    /// </summary>
    internal static LayerStop? LocateSolid(string? materialId,IReadOnlyList<PaidMaterialPiece> pieces,double before,double extentMm,double frontStartM)
    {
        var preset=CombatCompositeLayers.Preset(materialId);if(preset==null||!(extentMm>0)||!double.IsFinite(before))return null;
        double reach=extentMm,paid=0;
        foreach(var p in pieces.Where(p=>p.EndM>p.StartM).OrderBy(p=>p.StartM))
        {
            double cost=p.LengthMm*p.Factor;
            if(p.Factor>0&&paid+cost>=before){reach=(p.StartM-frontStartM)*1000+(before-paid)/p.Factor;break;}
            paid+=cost;
        }
        double share=Math.Clamp(reach/extentMm,0,1),depth=0,total=0;
        var layers=preset.Layers;foreach(var l in layers)total+=LayerLayout.LayerDepth(l);
        if(!(total>0))return null;
        for(int k=0;k<layers.Length;k++)
        {
            depth+=LayerLayout.LayerDepth(layers[k]);
            if(depth>=share*total-1e-9||k==layers.Length-1)return new(k+1,layers.Length,share);
        }
        return null;
    }

    /// <summary>
    /// 0.22.98: where a shot that stopped inside a composite plate (native [Solid block] or standard composite cassette plate)
    /// ran out of penetration budget, measured across its confirmed paid intervals (in order along the shot line) and mapped onto the
    /// preset's solid layers (air included in the depth fraction). Null when preset or intervals are unavailable.
    /// </summary>
    internal static LayerStop? LocateComposite(string? materialId,IReadOnlyList<PaidMaterialPiece> pieces,double entryBudget)
    {
        var preset=CombatCompositeLayers.Preset(materialId);
        if(preset==null||preset.Layers.Length==0||!double.IsFinite(entryBudget)||!(entryBudget>0))return null;
        var ordered=pieces.Where(p=>p!=null&&p.EndM>p.StartM&&double.IsFinite(p.Factor)&&p.Factor>0)
            .GroupBy(p=>(p.StartM,p.EndM,p.Factor)).Select(g=>g.First()).OrderBy(p=>p.StartM).ToArray();
        if(ordered.Length==0)return null;
        double totalSolidLen=ordered.Sum(p=>p.LengthMm);
        if(!(totalSolidLen>0))return null;
        double paid=0,walked=0,reach=totalSolidLen;
        foreach(var p in ordered)
        {
            double cost=p.LengthMm*p.Factor;
            if(p.Factor>0&&paid+cost>=entryBudget){reach=walked+(entryBudget-paid)/p.Factor;break;}
            paid+=cost;walked+=p.LengthMm;
        }
        double solidShare=Math.Clamp(reach/totalSolidLen,0,1);
        var layers=preset.Layers;
        double solidTotal=0,depthTotal=0;
        foreach(var l in layers){double d=LayerLayout.LayerDepth(l);depthTotal+=d;if(l.Material!="air")solidTotal+=d;}
        if(!(solidTotal>0)||!(depthTotal>0))return null;
        double target=solidShare*solidTotal,consumed=0,depth=0;int at=-1;double position=0;
        for(int k=0;k<layers.Length;k++)
        {
            double d=LayerLayout.LayerDepth(layers[k]);
            if(layers[k].Material=="air"){depth+=d;continue;}
            if(consumed+d>=target-1e-9||k==layers.Length-1||layers.Skip(k+1).All(x=>x.Material=="air"))
            {
                at=k;
                position=depth+(d>0?Math.Clamp((target-consumed)/d,0,1):1)*d;
                break;
            }
            consumed+=d;depth+=d;
        }
        return at<0?null:new LayerStop(at+1,layers.Length,Math.Clamp(position/depthTotal,0,1));
    }

    private static string Id(CombatReportEvent start)=>start.HitId??start.Layer!.Id;

    /// <summary>1: a solid block of this plate alone; 2: an air gap with only this plate beside it (inside the plate); 0: anything else.</summary>
    private static int Class(CombatReportEvent start,int entity)
    {
        var info=start.Layer!;
        if(info.AirGapMm==null){var armour=info.Parts.Where(p=>p.Armour).Select(p=>p.Entity).Distinct().ToArray();return armour.Length==1&&armour[0]==entity?1:0;}
        return info.Parts.Count>0&&info.Parts.All(p=>p.Entity==entity)?2:0;
    }

    private static List<CombatReportEvent> CollapseOne(List<CombatReportEvent> list,CombatReportEvent marker)
    {
        int entity=marker.RelatedEntity!.Value;string stage=marker.Stage;
        var header=Header.Match(marker.Text);
        string label=header.Success?header.Groups["label"].Value:"Plate";int groups=header.Success?int.Parse(header.Groups["n"].Value,CultureInfo.InvariantCulture):0;
        // what the game knew about the whole line when the plate was first reached: its solid millimetres (overlap once) and its extent, also beyond the place where the shot stopped
        double? lineSolid=header.Success&&header.Groups["solid"].Success?double.Parse(header.Groups["solid"].Value,CultureInfo.InvariantCulture):null,lineExtent=header.Success&&header.Groups["extent"].Success?double.Parse(header.Groups["extent"].Value,CultureInfo.InvariantCulture):null;
        var starts=Enumerable.Range(0,list.Count).Where(i=>list[i].Kind=="layerStart"&&list[i].Layer!=null&&list[i].Stage==stage).ToArray();
        // runs of solid blocks of this plate with only its own air gaps between them
        var runs=new List<List<int>>();List<int>? run=null;var air=new List<int>();
        foreach(var i in starts)
        {
            int kind=Class(list[i],entity);
            if(kind==1){run??=new();run.AddRange(air);air.Clear();run.Add(i);}
            else if(kind==2&&run!=null)air.Add(i);
            else{if(run!=null)runs.Add(run);run=null;air.Clear();}
        }
        if(run!=null)runs.Add(run);
        if(runs.Count==0)return list;
        var remove=new HashSet<int>();var insert=new Dictionary<int,List<CombatReportEvent>>();
        foreach(var r in runs)
        {
            var ids=r.Select(i=>Id(list[i])).ToHashSet(StringComparer.Ordinal);
            var inside=Enumerable.Range(0,list.Count).Where(i=>list[i].Stage==stage&&list[i].HitId!=null&&ids.Contains(list[i].HitId!)||r.Contains(i)).ToArray();
            var first=list[r[0]];string mergedId=Id(first);
            var solidStarts=r.Where(i=>Class(list[i],entity)==1).ToArray();
            var lastSolidId=Id(list[solidStarts[^1]]);
            var pieces=inside.Select(i=>list[i]).Where(e=>e.Kind=="materialReaction"&&e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!).Where(p=>p.Entity==entity&&p.EndM>p.StartM&&double.IsFinite(p.Factor)).ToArray();
            var firstPart=list[solidStarts[0]].Layer!.Parts.First(p=>p.Armour&&p.Entity==entity);
            double? before=first.BeforeMm??list[r[0]].BeforeMm;
            var terminalStop=inside.Select(i=>list[i]).Any(e=>e.Kind is "nativeStop" or "materialStop"||e.Kind=="outcome"&&e.Text.StartsWith("Stopped",StringComparison.OrdinalIgnoreCase));
            var lastResidual=inside.Select(i=>list[i]).LastOrDefault(e=>e.HitId==lastSolidId&&e.Kind is "plateResidual" or "componentResidual");
            double? after=terminalStop?0:lastResidual?.AfterMm??inside.Select(i=>list[i]).LastOrDefault(e=>e.HitId==lastSolidId&&e.AfterMm!=null)?.AfterMm;
            double solid=pieces.Sum(p=>p.LengthMm),eq=pieces.Sum(p=>p.RhaMm),factor=solid>0?eq/solid:0;
            double angle=solid>0?MedianAngle(pieces,solid):firstPart.AngleDegrees??0;
            double? extent=lineExtent??(pieces.Length>0?(pieces.Max(p=>p.EndM)-pieces.Min(p=>p.StartM))*1000:null);
            var part=firstPart with{AngleDegrees=double.IsFinite(angle)?Math.Clamp(angle,0,90):null,ThicknessMm=extent is {} x&&double.IsFinite(angle)?x*Math.Cos(Math.Clamp(angle,0,89.9)*Math.PI/180):firstPart.ThicknessMm,Armour=true};
            var layer=new CombatLayerInfo(mergedId,new[]{part},false,null);
            var created=new List<CombatReportEvent>{new("layerStart",stage,"Reached layer",before,HitId:mergedId,Layer:layer)};
            string text=$"{label}, {groups} layers";
            if(pieces.Length>0&&solid>0)
            {
                var piece=new PaidMaterialPiece(entity,part.MaterialId??pieces[0].Material,text,pieces.Min(p=>p.StartM),pieces.Min(p=>p.StartM)+solid/1000,factor,double.IsFinite(angle)?angle:0);
                created.Add(new("materialReaction",stage,"Confirmed material intervals: "+piece.Text,before,after,mergedId){PaidIntervals=new[]{piece}});
            }
            foreach(var i in inside)
                if(Kept.Contains(list[i].Kind)&&list[i].Layer==null)created.Add(list[i] with{HitId=mergedId});
            if(!terminalStop&&lastResidual!=null)created.Add(lastResidual with{HitId=mergedId,BeforeMm=before});
            string mergedText=$"{label}: plate in layers, {groups} layers.";
            if(terminalStop&&before is {} entry&&Locate(part.MaterialId,groups,pieces,entry,lineSolid) is {} stop)
                mergedText+=string.Create(CultureInfo.InvariantCulture,$" Arrested in layer {stop.Group} of {stop.Groups} at {100*stop.Fraction:0.#} % of the plate depth.");
            created.Add(new(Merged,stage,mergedText,before,after,mergedId,RelatedEntity:entity));
            foreach(var i in inside)remove.Add(i);
            insert[inside.Min()]=created;
        }
        foreach(var i in Enumerable.Range(0,list.Count))if(list[i].Kind==Marker&&list[i].Stage==stage&&list[i].RelatedEntity==entity)remove.Add(i);
        var result=new List<CombatReportEvent>(list.Count);
        for(int i=0;i<list.Count;i++)
        {
            if(insert.TryGetValue(i,out var made))result.AddRange(made);
            if(!remove.Contains(i))result.Add(list[i]);
        }
        return result;
    }

    /// <summary>
    /// Where the shot ran out of solid material: the solid pieces of the plate along the line are paid in order (length x factor); the penetration on entry
    /// (<paramref name="before"/>, mm RHA-eq) lasts through that much of the solid material, which is the same share of the recipe's solid layers from the front.
    /// Null when the recipe is not known.
    /// </summary>
    internal static LayerStop? Locate(string? materialId,int groups,IReadOnlyList<PaidMaterialPiece> pieces,double before,double? lineSolid=null)
    {
        var preset=CombatCompositeLayers.Preset(materialId);if(preset==null||groups<=0)return null;
        return Locate(preset,groups,pieces,before,lineSolid);
    }
    /// <param name="lineSolid">The solid material of the whole plate on the line (mm), as the game measured it when the plate was first reached; the pieces only cover the part up to the place where the shot stopped. Without it the pieces themselves are taken for the whole line.</param>
    internal static LayerStop? Locate(CassettePreset preset,int groups,IReadOnlyList<PaidMaterialPiece> pieces,double before,double? lineSolid=null)
    {
        var ordered=pieces.Where(p=>p.EndM>p.StartM).OrderBy(p=>p.StartM).ToArray();
        double total=lineSolid is {} whole&&whole>0?whole:ordered.Sum(p=>p.LengthMm);if(!(total>0)||!double.IsFinite(before))return null;
        double paid=0,walked=0,reach=total;
        foreach(var p in ordered)
        {
            double cost=p.LengthMm*p.Factor;
            if(p.Factor>0&&paid+cost>=before){reach=walked+(before-paid)/p.Factor;break;}
            paid+=cost;walked+=p.LengthMm;
        }
        double share=Math.Clamp(reach/total,0,1);
        LayerLayout? layout=null;
        foreach(var kind in new[]{LayerLayoutKind.PerLayer,LayerLayoutKind.PerBlock})
        {
            try{var candidate=LayerLayout.Build(preset,kind);if(candidate.Groups.Length==groups){layout=candidate;break;}layout??=candidate;}catch(Exception){}
        }
        if(layout==null)return null;
        var layers=preset.Layers;double solidTotal=0;foreach(var l in layers)if(l.Material!="air")solidTotal+=LayerLayout.LayerDepth(l);
        if(!(solidTotal>0))return null;
        double target=share*solidTotal,consumed=0,depth=0;int at=-1;double position=0;
        for(int k=0;k<layers.Length;k++)
        {
            double d=LayerLayout.LayerDepth(layers[k]);
            if(layers[k].Material=="air"){depth+=d;continue;}
            if(consumed+d>=target-1e-9||k==layers.Length-1||layers.Skip(k+1).All(x=>x.Material=="air")){at=k;position=depth+(d>0?Math.Clamp((target-consumed)/d,0,1):1)*d;break;}
            consumed+=d;depth+=d;
        }
        if(at<0||!(layout.DepthMm>0))return null;
        var group=layout.Groups.FirstOrDefault(g=>g.Layers.Contains(at));
        return group==null?null:new(group.Rank+1,layout.Groups.Length,Math.Clamp(position/layout.DepthMm,0,1));
    }
}

/// <summary>
/// 0.22.81: the marker of one paid passage through a plate marked [Solid block], written by the hit (RuntimeGroupedProtection.SolidCavity, after the cavity line) and read by
/// <see cref="LayeredPlateReport"/>: the recipe label, the passage, the front and back skin on the persistent ray (metres, the coordinates of the paid intervals), and the cavity
/// as paid (length, factor, steel equivalent, angle). Report only; the calculation never reads it.
/// </summary>
internal static class SolidBlockMarker
{
    internal sealed record Info(string Label,int Number,int Count,double FrontStartM,double FrontEndM,double BackStartM,double BackEndM,double CavityMm,double Factor,double RhaMm,double Angle);
    private const string Num=@"-?\d+(?:\.\d+)?";
    private static readonly Regex Pattern=new(@"^(?<label>.*?): solid block, passage (?<n>\d+) of (?<m>\d+); front skin \[(?<fs>"+Num+"),(?<fe>"+Num+@")\] m; back skin \[(?<bs>"+Num+"),(?<be>"+Num+@")\] m; cavity (?<mm>"+Num+") mm x (?<f>"+Num+") = (?<rha>"+Num+") mm RHA-eq at (?<a>"+Num+@")°\.$",RegexOptions.CultureInvariant);
    internal static string Text(string label,SolidCharge c)=>string.Create(CultureInfo.InvariantCulture,
        $"{label}: solid block, passage {c.Number} of {c.Count}; front skin [{c.Passage.Front.StartM:0.######},{c.Passage.Front.EndM:0.######}] m; back skin [{c.Passage.Back.StartM:0.######},{c.Passage.Back.EndM:0.######}] m; cavity {c.CavityMm:0.#########} mm x {c.Factor:0.#########} = {c.RhaMm:0.###} mm RHA-eq at {c.Angle:0.###}°.");
    internal static Info? Parse(string? text)
    {
        var m=Pattern.Match(text??"");if(!m.Success)return null;
        double D(string g)=>double.Parse(m.Groups[g].Value,CultureInfo.InvariantCulture);
        var info=new Info(m.Groups["label"].Value,int.Parse(m.Groups["n"].Value,CultureInfo.InvariantCulture),int.Parse(m.Groups["m"].Value,CultureInfo.InvariantCulture),
            D("fs"),D("fe"),D("bs"),D("be"),D("mm"),D("f"),D("rha"),D("a"));
        return info.FrontStartM<=info.FrontEndM&&info.FrontEndM<=info.BackStartM&&info.BackStartM<=info.BackEndM?info:null;
    }
}
