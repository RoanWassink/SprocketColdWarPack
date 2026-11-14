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

    /// <summary>The shot with its plates in layers merged (the shot itself when it has none). Called by the readers of a report (the list, Forensic, the HTML); the stored report, its JSON and <see cref="CombatReportTruth.Normalize"/> stay as they are.</summary>
    internal static CombatReportShot Apply(CombatReportShot shot)
    {
        var events=Collapse(shot.Events);
        return ReferenceEquals(events,shot.Events)?shot:shot with{Events=events};
    }

    internal static IReadOnlyList<CombatReportEvent> Collapse(IReadOnlyList<CombatReportEvent> events)
    {
        if(events==null||events.Count==0||!events.Any(e=>e!=null&&e.Kind==Marker&&e.RelatedEntity!=null))return events??Array.Empty<CombatReportEvent>();
        var list=events.Where(e=>e!=null).ToList();
        foreach(var marker in events.Where(e=>e!=null&&e.Kind==Marker&&e.RelatedEntity!=null).GroupBy(e=>(e.Stage,e.RelatedEntity!.Value)).Select(g=>g.First()).ToArray())
        {
            try{list=CollapseOne(list,marker);}catch(Exception){}
        }
        return list;
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
