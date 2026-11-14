namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.51 (user 08-11, "lijkt net alsof elke plaat 2 keer in het forensic menu wordt getoond"), presentation only. Two kinds of repetition
/// on one shot line:
/// <list type="number">
/// <item>A native block whose material was already paid earlier in the shot (the interval ledger pays no new length in it) is not a layer
/// of its own. Its small native loss joins the preceding visible solid row of the same stage, the row that paid that material, so the
/// sum of the losses and the continuity of the residual penetration are unchanged.</item>
/// <item>A later crossing of the same object with newly paid material of its own (a folded plate, the far wall of a housing) stays a row
/// and is named as such: "Plate #516 (second crossing)".</item>
/// </list>
/// Pure; the protection, the native simulation and the persisted JSON are never changed here.
/// </summary>
internal static class CombatLayerContinuity
{
    /// <summary>Below this paid length (mm) a block is treated as paying no new material (the report shows it as 0.0 mm).</summary>
    internal const double SettledMm=.05;
    internal sealed record Analysis(HashSet<(string Stage,string HitId)> Settled,Dictionary<(string Stage,string HitId),int> Crossing,
        HashSet<(string Stage,string HitId,int Entity)> RepeatedHousing)
    {
        internal bool IsSettled(string stage,string? hitId)=>hitId!=null&&Settled.Contains((stage,hitId));
        /// <summary>1 for a first crossing, 2 for the second, ...</summary>
        internal int Ordinal(string stage,string? hitId)=>hitId!=null&&Crossing.TryGetValue((stage,hitId),out var n)?n:1;
        /// <summary>
        /// The housing-only statement belongs to the first crossing of a housing, when the trace still held the module's interior. On a later
        /// wall of the same housing it would repeat or contradict that (an element of the module may already have been in the path), so it is
        /// not shown there. Runtime 0.22.51 no longer records it; this keeps older reports consistent.
        /// </summary>
        internal bool IsRepeatedHousing(CombatReportEvent e)=>e.Kind=="eraUnavailable"&&e.Text==EraRejectionReport.Housing&&e.HitId!=null&&
            e.RelatedEntity is {} id&&RepeatedHousing.Contains((e.Stage,e.HitId,id));
    }
    private static bool Paying(CombatReportEvent e)=>e.Kind is "materialReaction" or "materialStop"&&e.PaidIntervals!=null;
    private static bool Activation(CombatReportEvent e)=>e.Kind is "heavyEra" or "lightEra" or "eraReaction" or "eraResidual";
    /// <summary>The one object a layer stands for (a single plate, or a single non-armour part such as an ERA housing); null for a group or an air gap.</summary>
    internal static int? SingleObject(CombatLayerInfo layer)
    {
        if(layer.AirGapMm!=null)return null;
        var armour=layer.Parts.Where(p=>p.Armour).Select(p=>p.Entity).Distinct().ToArray();
        if(armour.Length==1)return armour[0];
        if(armour.Length==0&&layer.Parts.Select(p=>p.Entity).Distinct().Count()==1)return layer.Parts[0].Entity;
        return null;
    }
    internal static Analysis Analyse(CombatReportShot shot)
    {
        var settled=new HashSet<(string,string)>();var crossing=new Dictionary<(string,string),int>();
        var repeated=new HashSet<(string,string,int)>();
        var crossed=new Dictionary<(string Stage,int Entity),int>();
        var seen=new HashSet<(string Stage,int Entity)>();
        var byBlock=shot.Events.Where(e=>e.HitId!=null).GroupBy(e=>(e.Stage,e.HitId!)).ToDictionary(g=>g.Key,g=>g.ToArray());
        var solidBefore=new HashSet<string>();
        // The objects of the preceding solid layer of each stage, while no air gap lies between: a native block that continues the
        // same object (a plate split at another object's boundary) is the same crossing, not a new one.
        var adjacent=new Dictionary<string,HashSet<int>>();
        foreach(var start in shot.Events.Where(e=>e.Kind=="layerStart"&&e.Layer!=null))
        {
            var info=start.Layer!;var id=start.HitId??info.Id;var key=(start.Stage,id);
            if(info.AirGapMm!=null){adjacent.Remove(start.Stage);continue;}
            var own=byBlock.TryGetValue(key,out var list)?list:Array.Empty<CombatReportEvent>();
            foreach(var e in own)
                if(e.Kind=="eraUnavailable"&&e.Text==EraRejectionReport.Housing&&e.RelatedEntity is {} housing&&seen.Contains((start.Stage,housing)))
                    repeated.Add((start.Stage,id,housing));
            foreach(var p in info.Parts)seen.Add((start.Stage,p.Entity));
            var paying=own.Where(Paying).ToArray();
            var pieces=paying.SelectMany(e=>e.PaidIntervals!).ToArray();
            // Only the PerInterval ledger records what is paid per block; a Legacy report never merges. An internal module is never
            // merged: the protective-envelope verdict (0.22.43) depends on it.
            if(paying.Length>0&&pieces.Sum(p=>p.LengthMm)<SettledMm&&!own.Any(Activation)&&!info.Parts.Any(CombatEnvelope.IsInternal)&&solidBefore.Contains(start.Stage))
            {settled.Add(key);continue;}
            solidBefore.Add(start.Stage);
            var previous=adjacent.TryGetValue(start.Stage,out var before)?before:null;
            // The objects this block crossed: what the ledger paid; in a report without ledger data its single object, or the plates of a
            // group with a measured boundary in this block (an obliquity is only recorded for an own intersection; bare membership is not a crossing).
            var objects=paying.Length>0?pieces.Where(p=>p.LengthMm>=SettledMm).Select(p=>p.Entity).Distinct().ToArray():
                SingleObject(info) is {} only?new[]{only}:info.Parts.Where(p=>p.Armour&&p.AngleDegrees!=null).Select(p=>p.Entity).Distinct().ToArray();
            bool Continues(int entity)=>previous?.Contains(entity)==true&&crossed.GetValueOrDefault((start.Stage,entity))>0;
            if(SingleObject(info) is {} single)
            {
                var n=crossed.GetValueOrDefault((start.Stage,single))+(Continues(single)?0:1);
                if(n>1)crossing[key]=n;
            }
            foreach(var entity in objects)
                if(!Continues(entity))crossed[(start.Stage,entity)]=crossed.GetValueOrDefault((start.Stage,entity))+1;
            adjacent[start.Stage]=info.Parts.Select(p=>p.Entity).Concat(objects).ToHashSet();
        }
        return new(settled,crossing,repeated);
    }
    private static readonly string[] Ordinals={"","first","second","third","fourth","fifth","sixth","seventh","eighth","ninth","tenth"};
    /// <summary>" (second crossing)" for a later crossing of the same object; empty for the first.</summary>
    internal static string Suffix(int ordinal)=>ordinal<2?"":" ("+(ordinal<Ordinals.Length?Ordinals[ordinal]:ordinal+"th")+" crossing)";
}
