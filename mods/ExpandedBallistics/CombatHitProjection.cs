using System.Globalization;
namespace SprocketExpandedBallistics;

internal sealed class CombatHitView
{
    internal string Id="",Stage="",Name="Hit",Status="Reached",Reaction="",Damage="";
    /// <summary>0.22.51: 1 for the first crossing of this row's single object, 2 for a second crossing (a folded plate, a housing's far wall), ...</summary>
    internal int Crossing=1;
    internal CombatLayerInfo? Layer;
    internal double? Before,After;
    /// <summary>Exit measured on an earlier pass of the same plate, kept only as the last resort when nothing later is known (0.22.6B).</summary>
    internal double? AfterEarlier;
    /// <summary>
    /// 0.22.41: the exit of a block that has no residual event. <see cref="MeasuredExit"/> is the block's own complete protection line
    /// (entry → exit of this block); <see cref="NextEntry"/> is the recorded entry of the next native block of the same stage, hidden or not.
    /// The exit is the budget after this layer, or else the next entry, never the entry of a later visible row (which would move hidden
    /// losses, such as a crew pass, into this layer).
    /// </summary>
    internal double? MeasuredExit,NextEntry;
    /// <summary>0.22.12: position in the full reached-block list. Only order-adjacent blocks may merge; a hidden component keeps its gap.</summary>
    internal int Order=-1;
    /// <summary>0.22.12: wall thicknesses of order-adjacent identical plates merged into this row; one entry stays "100 mm", two become "100 + 50 mm".</summary>
    internal readonly List<double> Walls=new();
    internal readonly List<CombatReportEvent> Details=new();
    internal string Summary(int number)
    {
        string N(double? value)=>value is {} v?v.ToString("0.#",CultureInfo.InvariantCulture):"—";
        var parts=Layer?.Parts;
        var facts=parts is {Count:>0}?string.Join(" / ",parts.Where(p=>p.Armour).Select(p=>$"{p.MaterialLabel??p.Name} | {N(p.ThicknessMm)} mm | {N(p.AngleDegrees)}\u00B0").Distinct()):"";
        var armour=parts?.Where(p=>p.Armour).ToArray();
        if(armour is {Length:>1})facts=string.Join(" / ",armour.Select(p=>p.MaterialLabel??"Armour").Distinct())+$" | {armour.Length} plates | thickness/angle: multiple";
        if(facts.Length==0)facts=Name;
        var exit=Status.StartsWith("Stopped",StringComparison.Ordinal)?After==0?"0 mm - stopped":"stopped":Status=="Deflected"?"deflected":After is {} a?N(a)+" mm":"outcome unmeasured";
        return $"{number:00}  {Stage}  {Name}\n{facts}\n{N(Before)} mm \u2192 {exit}"+(Reaction.Length>0?"\n"+Reaction:"")+(Damage.Length>0?"\nDamage: "+Damage:"");
    }
}
internal static class CombatHitProjection
{
    internal static CombatHitView[] Build(CombatReportShot shot)
    {
        shot=LayeredPlateReport.Apply(shot); // 0.22.69: a plate in layers is one row
        var hits=new List<CombatHitView>();var byId=new Dictionary<(string Stage,string Id),CombatHitView>();
        // 0.22.51: settled blocks, later crossings and repeated housing statements of this shot line (presentation only).
        var continuity=CombatLayerContinuity.Analyse(shot);
        var chemical=new List<CombatReportEvent>();
        var deferredEra=new List<CombatReportEvent>();
        var lastStarted=new Dictionary<string,CombatHitView>(StringComparer.Ordinal);
        foreach(var e in shot.Events)
        {
            // Native chemical observations precede layerStart. Never borrow the previous hit.
            if(e.Kind is "chemicalGeometry" or "chemicalGap"){chemical.Add(e);continue;}
            CombatHitView? hit=null;
            if(e.HitId!=null)byId.TryGetValue((e.Stage,e.HitId),out hit);
            if(IsEra(e))
            {
                if(hit!=null&&(e.RelatedEntity==null||hit.Layer?.Parts.Any(p=>p.Entity==e.RelatedEntity)==true))ApplyEra(hit,e);
                else deferredEra.Add(e);
                continue;
            }
            if(e.Kind=="layerStart"&&e.Layer is {} layer)
            {
                if(hit==null){hit=new(){Id=layer.Id,Stage=e.Stage,Layer=layer,Before=e.BeforeMm,Name=Name(layer),Order=hits.Count};hits.Add(hit);byId[(e.Stage,layer.Id)]=hit;}
                else{hit.Layer=layer;hit.Name=Name(layer);}
                if(lastStarted.TryGetValue(e.Stage,out var previousStart)&&!ReferenceEquals(previousStart,hit))previousStart.NextEntry??=e.BeforeMm;
                lastStarted[e.Stage]=hit;
                continue;
            }
            if(hit==null&&e.Kind is "plateResidual" or "componentResidual" or "nativeStop" or "materialStop" or "outcome")
            {
                var legacy=e.Text.Split(';')[0].Replace("Traversed ","").Replace("Passed ","");
                if(legacy.Contains("Combined armour block",StringComparison.Ordinal))legacy="Armour array";
                hit=new(){Id="legacy:"+hits.Count,Stage=e.Stage,Name=legacy,Before=e.BeforeMm,Order=hits.Count};hits.Add(hit);
            }
            hit??=hits.LastOrDefault(h=>h.Stage==e.Stage);
            if(hit==null)continue;
            if(e.Kind=="materialNote")
            {
                var insert=e.RelatedEntity is {} noteEntity?hits.LastOrDefault(h=>h.Stage==e.Stage&&h.Layer?.Parts.Any(p=>p.Entity==noteEntity)==true):null;
                insert??=hit;
                insert.Details.Add(e);
                insert.Reaction=insert.Reaction.Length==0?e.Text:insert.Reaction+" \u00B7 "+e.Text;
                insert.Details.RemoveAll(d=>d.Kind=="materialPending");
                continue;
            }
            hit.Details.Add(e);
            if(e.Kind is "heavyEra" or "lightEra")hit.Reaction="ERA activated";
            if(e.Kind=="eraSpent")hit.Reaction="ERA already spent";
            if(e.Kind=="interaction"&&(e.Text.Contains("destroyed",StringComparison.OrdinalIgnoreCase)||e.Text.Contains("killed",StringComparison.OrdinalIgnoreCase)||e.Text.Contains("wounded",StringComparison.OrdinalIgnoreCase)))hit.Damage=hit.Damage.Length==0?e.Text:hit.Damage+"; "+e.Text;
            if(e.Kind is "plateResidual" or "componentResidual"){hit.Before??=e.BeforeMm;hit.After=e.AfterMm;if(hit.Status=="Reached")hit.Status="Passed";}
            if(e.Kind is "nativeStop" or "materialStop"){hit.Status="Stopped";hit.Before??=e.BeforeMm;hit.After=e.AfterMm;}
            if(e.Kind=="outcome"&&e.Text.StartsWith("Stopped",StringComparison.OrdinalIgnoreCase))hit.Status="Stopped";
            if(e.Kind=="outcome"&&e.Text.StartsWith("Deflected",StringComparison.OrdinalIgnoreCase))hit.Status="Deflected";
            if(e.Kind=="materialSupport"&&e.RelatedEntity is {} supportEntity)
            {
                var insert=hits.LastOrDefault(h=>h.Stage==e.Stage&&h.Layer?.Parts.Any(p=>p.Entity==supportEntity)==true);
                if(insert!=null&&!ReferenceEquals(insert,hit)){hit.Details.Remove(e);insert.Details.Add(e);}
            }
            // 0.22.41: a block's own complete protection line (WriteFactor: entry → exit of this block) is its measured exit.
            if(e.Kind=="materialReaction"&&e.HitId!=null&&e.HitId==hit.Id&&e.AfterMm!=null&&e.BeforeMm is {} lineEntry&&hit.Before is {} hitEntry&&
               Math.Abs(lineEntry-hitEntry)<=1e-6&&ProtectionLine.Is(e.Text.Split(';')[0]))hit.MeasuredExit=e.AfterMm;
            if(e.Kind is "materialReaction" or "materialStop" or "materialUnavailable" or "eraReaction")
            {
                var insert=e.RelatedEntity is {} entity?hits.LastOrDefault(h=>h.Stage==e.Stage&&h.Layer?.Parts.Any(p=>p.Entity==entity)==true):
                    hits.LastOrDefault(h=>h.Stage==e.Stage&&h.Details.Any(d=>d.Kind=="materialPending"&&e.Text.Contains(d.Text.Split(':')[0],StringComparison.Ordinal)))??hit;
                if(insert!=null)
                {
                    if(!ReferenceEquals(insert,hit))insert.Details.Add(e);
                    insert.Details.RemoveAll(d=>d.Kind=="materialPending");
                    if(MaterialIntervalDisplay.Is(e))
                    {
                        insert.Reaction=MaterialIntervalDisplay.Reaction(e,insert.Layer,shot.Events);
                        continue;
                    }
                    insert.Reaction=
                        e.Kind=="eraReaction"?e.Text:
                        insert.Layer?.Parts.Any(p=>p.MaterialId==CompositeModule.MaterialId)==true?e.Text:
                        e.Kind=="materialUnavailable"?CombatMaterialReason.From(e):
                        e.Kind=="materialStop"&&LegacyReportText.IsObliquityCorrection(e.Text)?e.Text: // 0.22.25: the plate's own line of sight stopped the core
                        e.Kind=="materialStop"?"Material reaction stopped the core at the support plate":
                        e.Text.StartsWith("Cassette ",StringComparison.Ordinal)?CombatMaterialReason.Cassette(e):
                        e.Text.StartsWith(CombatMaterialReason.SpacedPrefix,StringComparison.Ordinal)?CombatMaterialReason.Spaced(e):
                        $"Backing confirmed | extra reduction {Math.Max(0,(e.BeforeMm??0)-(e.AfterMm??0)):0.#} mm";
                }
            }
        }
        foreach(var hit in hits)
            if(hit.Reaction.Length==0&&hit.Details.LastOrDefault(e=>e.Kind=="materialPending") is {} pending)
                hit.Reaction=CombatMaterialReason.From(pending);
        var visible=hits.Where(h=>h.Layer?.Parts.Any(p=>p.Armour)==true||h.Status is "Stopped" or "Deflected"||h.Details.Any(EraRejectionReport.IsReason)||
            h.Layer?.AirGapMm!=null&&(h.Reaction.Length>0||h.Details.Any(e=>e.Kind is "materialNote" or "materialReaction" or "materialStop" or "plateResidual" or "materialUnavailable"))||
            h.Layer==null&&h.Details.Any(e=>e.Kind is "plateResidual" or "nativeStop" or "materialStop" or "outcome")).ToArray();
        // 0.22.12: a component with its own measured pass-through between two
        // blocks of the same plate is a real row; folding it away makes the
        // 163 mm cannon passage disappear and lets the two plate rows merge.
        var keep=new HashSet<CombatHitView>(visible);
        foreach(var hidden in hits.Except(visible))
        {
            if(hidden.Layer==null||hidden.Layer.Parts.Any(p=>p.Armour))continue;
            if(hidden.Before is not {} before||hidden.After is not {} after||Math.Abs(after-before)<=1e-9)continue;
            var previous=visible.LastOrDefault(h=>h.Stage==hidden.Stage&&h.Order<hidden.Order);
            var next=visible.FirstOrDefault(h=>h.Stage==hidden.Stage&&h.Order>hidden.Order);
            if(previous==null||next==null)continue;
            if(Armour(previous) is {} a&&Armour(next) is {} b&&a.SetEquals(b))keep.Add(hidden);
        }
        var rows=hits.Where(h=>keep.Contains(h)).Take(128).ToList();
        foreach(var hidden in hits.Except(rows))
        {
            var target=rows.FirstOrDefault(h=>h.Stage==hidden.Stage&&h.Order>hidden.Order)??rows.LastOrDefault(h=>h.Stage==hidden.Stage);
            if(target!=null)target.Details.AddRange(hidden.Details);
        }
        var result=rows.ToList();
        foreach(var e in chemical)
        {
            var target=!string.IsNullOrWhiteSpace(e.HitId)?rows.FirstOrDefault(h=>h.Stage==e.Stage&&h.Id==e.HitId):null;
            if(target==null)
            {
                target=result.FirstOrDefault(h=>h.Id=="chemical-unassigned:"+e.Stage);
                if(target==null){target=new(){Id="chemical-unassigned:"+e.Stage,Stage=e.Stage,Name="Unassigned chemical diagnostics",Status="Diagnostic only",Order=hits.Count};result.Add(target);}
            }
            target.Details.Add(e);
        }
        foreach(var e in deferredEra)
        {
            CombatHitView? target=null;
            if(e.HitId!=null)byId.TryGetValue((e.Stage,e.HitId),out target);
            if(target!=null&&e.RelatedEntity!=null&&target.Layer?.Parts.Any(p=>p.Entity==e.RelatedEntity)!=true)target=null;
            if(target==null&&e.HitId==null&&e.RelatedEntity is {} entity)
            {
                var candidates=hits.Where(h=>h.Stage==e.Stage&&h.Layer?.Parts.Any(p=>p.Entity==entity)==true).Take(2).ToArray();
                if(candidates.Length==1)target=candidates[0];
            }
            if(target==null||!result.Contains(target))
            {
                target=result.FirstOrDefault(h=>h.Id=="era-unassigned:"+e.Stage);
                if(target==null){target=new(){Id="era-unassigned:"+e.Stage,Stage=e.Stage,Name="Unassigned ERA diagnostics",Status="Diagnostic only"};result.Add(target);}
            }
            ApplyEra(target,e);
        }
        // 0.22.51: a block whose material was already paid earlier in this shot is one row with the preceding row that paid it.
        result=FoldSettled(result,continuity);
        result=Consolidate(result);
        // 0.22.51: a later crossing of the same object, with newly paid material of its own, is named as such.
        foreach(var hit in result){hit.Crossing=continuity.Ordinal(hit.Stage,hit.Id);hit.Name+=CombatLayerContinuity.Suffix(hit.Crossing);}
        // Restore event order after deferred associations, without affecting budgets or status.
        var eventOrder=new Dictionary<CombatReportEvent,int>();
        for(int i=0;i<shot.Events.Count;i++)eventOrder.TryAdd(shot.Events[i],i);
        foreach(var hit in result)hit.Details.Sort((a,b)=>eventOrder.GetValueOrDefault(a,int.MaxValue).CompareTo(eventOrder.GetValueOrDefault(b,int.MaxValue)));
        // Finalize after interval display and deferred ERA attribution: a passive
        // payment line must not erase the reason an explosive element did not react.
        foreach(var hit in result)
        {
            var all=EraRejectionReport.Lines(hit.Details);
            if(all.Length==0)continue;
            // 0.22.51: the housing-only statement of a later wall of an already crossed housing is not repeated (see CombatLayerContinuity).
            var reasons=EraRejectionReport.Lines(hit.Details.Where(e=>!continuity.IsRepeatedHousing(e)));
            var lines=hit.Reaction.Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(l=>!all.Contains(l,StringComparer.Ordinal)).ToList();
            lines.AddRange(reasons);hit.Reaction=string.Join("\n",lines);
        }
        return result.ToArray();
    }
    /// <summary>
    /// 0.22.51: a settled block (all its material paid earlier in this shot, see <see cref="CombatLayerContinuity"/>) joins the preceding solid
    /// row of the same stage: that row keeps its name and paid material, takes the block's exit, status and reasons, so the residual
    /// penetration stays continuous and the losses still add up. Without such a row the block keeps its own row.
    /// </summary>
    internal static List<CombatHitView> FoldSettled(List<CombatHitView> hits,CombatLayerContinuity.Analysis continuity)
    {
        var result=new List<CombatHitView>();
        foreach(var hit in hits)
        {
            var owner=result.LastOrDefault();
            if(owner!=null&&continuity.IsSettled(hit.Stage,hit.Id)&&owner.Stage==hit.Stage&&owner.Layer is {AirGapMm:null}&&
               owner.Status is not ("Stopped" or "Deflected" or "Diagnostic only")&&!owner.Status.StartsWith("Stopped",StringComparison.Ordinal)&&hit.Status!="Diagnostic only")
            {
                owner.Details.AddRange(hit.Details);
                owner.After=hit.After;owner.MeasuredExit=hit.MeasuredExit;owner.NextEntry=hit.NextEntry;owner.Order=hit.Order;
                if(hit.Status.StartsWith("Stopped",StringComparison.Ordinal)||hit.Status=="Deflected")owner.Status=hit.Status;
                else if(hit.Status=="Passed")owner.Status="Passed";
                if(hit.Damage.Length>0&&!owner.Damage.Contains(hit.Damage,StringComparison.Ordinal))owner.Damage=owner.Damage.Length==0?hit.Damage:owner.Damage+"; "+hit.Damage;
                continue;
            }
            result.Add(hit);
        }
        return result;
    }
    private static bool IsEra(CombatReportEvent e)=>e.Kind is "heavyEra" or "lightEra" or "eraSpent" or "eraUnavailable" or "eraResidual";
    private static void ApplyEra(CombatHitView hit,CombatReportEvent e)
    {
        hit.Details.Add(e);
        if(e.Kind is "heavyEra" or "lightEra")hit.Reaction="ERA activated";
        else if(e.Kind=="eraSpent"&&hit.Reaction!="ERA activated")hit.Reaction=EraRejectionReport.Spent;
        else if(e.Kind=="eraUnavailable"&&hit.Reaction.Length==0)hit.Reaction=EraRejectionReport.Text(e);
    }
    private static HashSet<int>? Armour(CombatHitView hit)
    {
        var set=hit.Layer?.Parts.Where(p=>p.Armour).Select(p=>p.Entity).ToHashSet();
        return set is {Count:>0}?set:null;
    }
    private static IEnumerable<double> WallThicknesses(CombatHitView hit)
    {
        if(hit.Layer==null)yield break;
        foreach(var part in hit.Layer.Parts)
            if(part.Armour&&part.ThicknessMm is >0&&double.IsFinite(part.ThicknessMm.Value))
                yield return part.ThicknessMm.Value;
    }
    /// <summary>
    /// 0.22.6B. The native simulation can reach one plate twice in a row (a second reached block of the same entity), which
    /// showed as two identical layers, the second with no exit ("397 → ?"). Consecutive reached blocks of the same plate(s) in
    /// the same stage become one layer (entry of the first, exit of the last); an exit that is still missing is taken from the
    /// entry of the next layer, and stays unknown only when nothing later was measured.
    /// </summary>
    internal static List<CombatHitView> Consolidate(List<CombatHitView> hits)
    {
        hits=FoldMembershipBridges(hits);
        var merged=new List<CombatHitView>();
        foreach(var hit in hits)
        {
            var previous=merged.Count>0?merged[^1]:null;
            if(previous!=null&&previous.Order+1==hit.Order&&previous.Stage==hit.Stage&&previous.Status is not ("Stopped" or "Deflected" or "Diagnostic only")&&hit.Status!="Diagnostic only"
               &&Armour(previous) is {} a&&Armour(hit) is {} b&&a.SetEquals(b))
            {
                if(previous.Walls.Count==0)previous.Walls.AddRange(WallThicknesses(previous));
                previous.Walls.AddRange(WallThicknesses(hit));
                previous.Details.AddRange(hit.Details);
                if(previous.After!=null)previous.AfterEarlier=previous.After;
                previous.After=hit.After;previous.MeasuredExit=hit.MeasuredExit;previous.NextEntry=hit.NextEntry;
                if(hit.Status.StartsWith("Stopped",StringComparison.Ordinal)||hit.Status=="Deflected")previous.Status=hit.Status;
                else if(hit.Status=="Passed")previous.Status="Passed";
                if(previous.Reaction.Length==0)previous.Reaction=hit.Reaction;
                if(hit.Damage.Length>0&&!previous.Damage.Contains(hit.Damage,StringComparison.Ordinal))previous.Damage=previous.Damage.Length==0?hit.Damage:previous.Damage+"; "+hit.Damage;
                continue;
            }
            merged.Add(hit);
        }
        for(int i=0;i<merged.Count;i++)
        {
            var hit=merged[i];
            if(hit.After!=null||hit.Status is "Stopped" or "Deflected" or "Diagnostic only"||hit.Status.StartsWith("Stopped",StringComparison.Ordinal))continue;
            var next=merged.Skip(i+1).FirstOrDefault(h=>h.Stage==hit.Stage&&h.Status!="Diagnostic only"&&h.Before!=null);
            // 0.22.41 (#122: 94 → 94, not 94 → 93): the measured exit of this block, else the recorded entry of the very next block.
            hit.After=hit.MeasuredExit??hit.NextEntry??next?.Before??hit.AfterEarlier;
        }
        return merged;
    }
    private static List<CombatHitView> FoldMembershipBridges(List<CombatHitView> hits)
    {
        var result=new List<CombatHitView>();
        for(int i=0;i<hits.Count;i++)
        {
            var hit=hits[i];var before=result.LastOrDefault();var next=i+1<hits.Count?hits[i+1]:null;
            // An unmeasured membership-only block straddles two independently measured plates.
            // Equal boundary budgets and exact member coverage prove it is not another paid layer.
            if(before!=null&&next!=null&&before.Status=="Passed"&&hit.Layer?.Combined==true&&hit.Layer.Parts.All(p=>p.Armour)&&hit.Status=="Reached"&&hit.After==null&&
                next.Details.Any(e=>e.Kind is "plateResidual" or "nativeStop" or "materialStop")&&
                hit.Details.All(e=>e.Kind=="blockMembers")&&hit.Reaction.Length==0&&hit.Damage.Length==0&&
                before.Stage==hit.Stage&&next.Stage==hit.Stage&&before.After!=null&&before.After==hit.Before&&next.Before==hit.Before&&
                before.Id.Split(':')[0]==hit.Id.Split(':')[0]&&next.Id.Split(':')[0]==hit.Id.Split(':')[0]&&
                Armour(before) is {} a&&Armour(next) is {} b&&Armour(hit) is {} members&&a.Overlaps(members)&&b.Overlaps(members)&&
                !a.SetEquals(b)&&members.IsSubsetOf(a.Union(b)))
            {
                before.Details.Add(new("blockMembership",hit.Stage,"Intermediate native block membership only: "+
                    string.Join(", ",hit.Layer.Parts.Select(p=>p.Name+" (#"+p.Entity+")")),HitId:hit.Id,Layer:hit.Layer));
                before.Details.AddRange(hit.Details);continue;
            }
            result.Add(hit);
        }
        return result;
    }
    private static string Name(CombatLayerInfo layer)
    {
        if(layer.AirGapMm is {} gap)
        {
            var before=layer.Parts.FirstOrDefault(p=>p.MaterialId?.StartsWith("ebCassette_",StringComparison.Ordinal)==true)
                ??layer.Parts.LastOrDefault(p=>p.Name.Contains("Composite",StringComparison.OrdinalIgnoreCase))
                ??layer.Parts.LastOrDefault();
            var plate=before==null?"plate":CombatReportView.PlateName(before.Name,before.Entity);
            return $"Air gap {gap.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)} mm before {plate}";
        }
        var armour=layer.Parts.Where(p=>p.Armour).ToArray();
        return armour.Length>1?"Armour array":armour.Length==1?armour[0].MaterialLabel?.StartsWith("ERA",StringComparison.OrdinalIgnoreCase)==true?armour[0].MaterialLabel!:armour[0].Name:layer.Parts.FirstOrDefault()?.Name??"Hit";
    }
}
