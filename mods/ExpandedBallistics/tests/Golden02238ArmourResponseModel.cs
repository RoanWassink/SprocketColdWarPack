namespace SprocketExpandedBallistics.Golden02238;

// Allocation-free impact identity for the hot path: no per-block string formatting.
internal readonly record struct ArmourHitKey(long Spawn,int Entity,int Triangle,double Entry,double Exit);

// Gameplay calibration, additional to native passive resistance. No trajectory rotation.
internal sealed class ArmourShotState
{
    internal double Disturbance;
    internal double AdditionalRetention=1;
    internal readonly HashSet<string> Applied=new(StringComparer.Ordinal);
    internal readonly HashSet<string> Preconditioned=new(StringComparer.Ordinal);
    internal readonly HashSet<ArmourHitKey> AppliedKeys=new();
    internal readonly HashSet<ArmourHitKey> PreconditionedKeys=new();
    internal bool ContainsApplied(in ArmourHitKey key)=>AppliedKeys.Contains(key);
    internal double? LastSteelExitMm;
    // Layer stacks whose front layer this precursor pass already spent (allocated on first tandem use).
    internal HashSet<(long Spawn,int Mount,int X,int Y)>? StackGuard;
    // True when this layer is the rear layer of a stack already hit by this pass: no reaction, charge kept.
    internal bool RearGuarded((long Spawn,int Mount,int X,int Y) key)=>StackGuard!=null&&StackGuard.Contains(key);
    internal void FrontSpent((long Spawn,int Mount,int X,int Y) key)=>(StackGuard??=new()).Add(key);
}
internal readonly record struct EraCell(long Spawn,int Element,int Surface,int X,int Y);
internal sealed class EraCells
{
    private readonly HashSet<EraCell> spent=new();
    internal bool Consume(EraCell cell){lock(spent)return spent.Add(cell);}
    internal void RemoveSpawn(long spawn){lock(spent)spent.RemoveWhere(c=>c.Spawn==spawn);}
    internal void RemoveElement(long spawn,int element){lock(spent)spent.RemoveWhere(c=>c.Spawn==spawn&&c.Element==element);}
    internal void Remove(EraCell cell){lock(spent)spent.Remove(cell);}
    internal bool Contains(EraCell cell){lock(spent)return spent.Contains(cell);}
    internal EraCell[] Cells(long spawn,int element){lock(spent)return spent.Where(c=>c.Spawn==spawn&&c.Element==element).ToArray();}
    internal bool Any(long spawn,int element){lock(spent)return spent.Any(c=>c.Spawn==spawn&&c.Element==element);}
    internal void Clear(){lock(spent)spent.Clear();}
    internal int Count {get{lock(spent)return spent.Count;}}
}
internal static class ArmourResponseModel
{
    // Consumes a placed cell. With guard (precursor pass over a multi-layer stack) the front layer (stage 2)
    // records its stack and the rear layer (stage 1) is left intact when the front layer was spent in this pass.
    // Without guard this is exactly ledger.Consume(cell). No allocation except the one-time guard set.
    internal static bool ConsumeStacked(EraCells ledger,ArmourShotState state,EraCell cell,int stageIndex,int mountHash,bool guard,out bool rearGuarded)
    {
        rearGuarded=false;
        if(!guard)return ledger.Consume(cell);
        var key=(cell.Spawn,mountHash,cell.X,cell.Y);
        if(stageIndex==1&&state.RearGuarded(key)){rearGuarded=true;return false;}
        var intact=ledger.Consume(cell);
        if(intact&&stageIndex==2)state.FrontSpent(key);
        return intact;
    }
    internal static bool ThreatAllowed(ArmourResponse response,string threatClass)=>
        response.EligibleThreatClasses==null || response.EligibleThreatClasses.Contains(threatClass,StringComparer.Ordinal);
    internal static bool ReactiveThreat(ArmourResponse response,string behavior,string threatClass="unknown")=>
        ThreatAllowed(response,threatClass) && (response.Kind=="lightEra"?behavior=="heat":response.Kind=="heavyEra"&&behavior is "heat" or "apfsds");
    // One threat classification for the whole shot. It is computed once in the
    // Begin scope, not per block, and is also the report label source.
    internal static string EraThreat(MaterialThreatTags tags)
    {
        if(!tags.Original)return "fragment";
        if(tags.UsesJet)return "chemicalJet";
        if(tags.UsesHesh)return "hesh";
        if(tags.Behavior=="he")return "he";
        if(tags.Contains("longRod"))return "longRod";
        if(tags.Contains("shortRod"))return "shortRod";
        if(tags.UsesClassic)return "kineticFullBore";
        return "fragment";
    }
    internal static bool KineticEraThreat(string threat)=>threat is "longRod" or "shortRod" or "kineticFullBore";
    internal static bool EraTriggerCalibre(double calibreMm,double minimumMm)=>
        double.IsFinite(calibreMm)&&calibreMm>=minimumMm;
    internal static double AngularWeight(ArmourResponse response,string behavior,double angle)=>
        Weight(response.Kind=="heavyEra"&&behavior=="apfsds"?response.Geometry.KineticAngularCurve??Array.Empty<CurvePoint>():response.Geometry.AngularCurve,angle);
    internal static double EraAngularWeight(ArmourResponse response,string threat,double angle)=>
        Weight(response.Kind=="heavyEra"&&KineticEraThreat(threat)
            ?response.Geometry.KineticAngularCurve??response.Geometry.AngularCurve
            :response.Geometry.AngularCurve,angle);
    internal static string EraReactionText(string label,string shell,string threat,string kind,double retention,double scale)
    {
        if(kind=="lightEra"&&KineticEraThreat(threat)||threat is "hesh" or "he"||retention>=1-1e-9)
            return "ERA "+label+" detonated: no effect vs "+shell;
        var percent=Math.Clamp((1-retention)*100,0,100).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture);
        var factor=KineticEraThreat(threat)&&scale>0&&scale<1?" (\u00D7"+scale.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+")":"";
        return "ERA "+label+": "+shell+" \u2212"+percent+" %"+factor;
    }

    internal static float ResidualSpeed(float nativeSpeed,float parentSpeedRatio)=>
        float.IsFinite(nativeSpeed)&&nativeSpeed>=0&&float.IsFinite(parentSpeedRatio)?nativeSpeed*Math.Clamp(parentSpeedRatio,0,1):nativeSpeed;
    internal static double Weight(IReadOnlyList<CurvePoint> curve,double input)
    {
        if(!double.IsFinite(input)||curve.Count==0||input<curve[0].Input||input>curve[^1].Input)return 0;
        for(var i=1;i<curve.Count;i++)if(input<=curve[i].Input)
            return curve[i-1].Weight+(curve[i].Weight-curve[i-1].Weight)*(input-curve[i-1].Input)/(curve[i].Input-curve[i-1].Input);
        return curve[^1].Weight;
    }
    internal static bool PerforatedSteel(ArmourShotState shot,RodPreconditioning p,string material,double thickness,double angle,double exitMm,string hitKey)=>
        PerforatedSteelCore(shot,p,material,thickness,angle,exitMm,false,default,hitKey);
    internal static bool PerforatedSteel(ArmourShotState shot,RodPreconditioning p,string material,double thickness,double angle,double exitMm,in ArmourHitKey hitKey)=>
        PerforatedSteelCore(shot,p,material,thickness,angle,exitMm,true,hitKey,null);
    private static bool PerforatedSteelCore(ArmourShotState shot,RodPreconditioning p,string material,double thickness,double angle,double exitMm,bool structKey,in ArmourHitKey key,string? textKey)
    {
        if(!p.SteelIds.Contains(material)||!double.IsFinite(thickness)||!double.IsFinite(angle)||!double.IsFinite(exitMm)||
            thickness<p.MinimumThickness||angle<p.MinimumAngle||angle>p.MaximumAngle||p.FullAngle<=p.MinimumAngle||
            !(structKey?shot.PreconditionedKeys.Add(key):shot.Preconditioned.Add(textKey!)))return false;
        shot.Disturbance=Math.Min(p.TotalCap,shot.Disturbance+p.PerLayerCap*Math.Clamp((angle-p.MinimumAngle)/(p.FullAngle-p.MinimumAngle),0,1)*Math.Clamp(thickness/p.FullThickness,0,1));
        shot.LastSteelExitMm=exitMm;
        return true;
    }
    internal static double Apply(ArmourResponseCatalogue catalogue,ArmourResponse response,ArmourShotState shot,
        string hitKey,string behavior,bool original,bool coldwar,double thickness,double angle,double entryMm,bool intactCell,double? experimentalLoss=null,double? experimentalCap=null,string threatClass="unknown",string? threat=null)=>
        ApplyCore(catalogue,response,shot,false,default,hitKey,behavior,original,coldwar,thickness,angle,entryMm,intactCell,experimentalLoss,experimentalCap,threatClass,threat??ThreatForBehavior(behavior));
    internal static double Apply(ArmourResponseCatalogue catalogue,ArmourResponse response,ArmourShotState shot,
        in ArmourHitKey hitKey,string behavior,bool original,bool coldwar,double thickness,double angle,double entryMm,bool intactCell,double? experimentalLoss=null,double? experimentalCap=null,string threatClass="unknown",string? threat=null)=>
        ApplyCore(catalogue,response,shot,true,hitKey,null,behavior,original,coldwar,thickness,angle,entryMm,intactCell,experimentalLoss,experimentalCap,threatClass,threat??ThreatForBehavior(behavior));
    private static string ThreatForBehavior(string behavior)=>behavior switch
    {
        "heat" or "atgm" or "atgm_gun"=>"chemicalJet","hesh"=>"hesh","he"=>"he",
        "apfsds"=>"longRod",_=>"kineticFullBore"
    };
    private static double ApplyCore(ArmourResponseCatalogue catalogue,ArmourResponse response,ArmourShotState shot,
        bool structKey,in ArmourHitKey key,string? textKey,string behavior,bool original,bool coldwar,double thickness,double angle,double entryMm,bool intactCell,double? experimentalLoss=null,double? experimentalCap=null,string threatClass="unknown",string threat="kineticFullBore")
    {
        if(!ThreatAllowed(response,threatClass)||!catalogue.Enabled||!original||!coldwar||!double.IsFinite(thickness)||!double.IsFinite(angle)||
            thickness<response.Geometry.MinNormalThicknessMm||thickness>response.Geometry.MaxNormalThicknessMm)return 1;
        if(!(structKey?shot.AppliedKeys.Add(key):shot.Applied.Add(textKey!)))return 1;
        var isEra=response.Kind is "lightEra" or "heavyEra";
        var weight=isEra?EraAngularWeight(response,threat,angle):AngularWeight(response,behavior,angle);
        if(response.Geometry.Mode=="resolvedLayers")
        {
            if(shot.LastSteelExitMm is not {} exit)return 1;
            var gap=entryMm-exit;
            if(gap<response.Geometry.MinMeasuredGapMm||gap>response.Geometry.MaxMeasuredGapMm)return 1;
            weight*=Weight(response.Geometry.MeasuredGapCurve,gap);
        }
        double retention;
        if(isEra)
        {
            if(response.Kind=="heavyEra"&&!intactCell)return 1;
            if(threat=="chemicalJet")retention=response.Kind=="lightEra"&&!intactCell?1:response.Calibration.HeatRetention;
            else if(KineticEraThreat(threat))
            {
                if(response.Kind=="lightEra")return 1;
                var baseRetention=response.Calibration.IntactRodRetention-
                    shot.Disturbance*(response.Calibration.IntactRodRetention-response.Calibration.DisturbedRodRetention);
                var scale=response.ThreatScale?.For(threat)??1;
                retention=1-(1-baseRetention)*scale;
            }
            else return 1;
        }
        else
        {
            if(behavior=="heat")retention=response.Calibration.HeatRetention;
            else if(behavior=="apfsds")retention=response.Calibration.IntactRodRetention-
                shot.Disturbance*(response.Calibration.IntactRodRetention-response.Calibration.DisturbedRodRetention);
            else return 1;
        }
        var cap=behavior=="heat"?catalogue.MaximumAdditionalHeatLoss:catalogue.MaximumAdditionalKineticLoss;
        var next=experimentalLoss is {} loss&&(behavior=="apfsds"||isEra&&KineticEraThreat(threat))&&experimentalCap is {} ec
            ?ExperimentalRodModel.Next(shot.AdditionalRetention,loss,ec)
            :Math.Max(1-cap,shot.AdditionalRetention*(1-weight*(1-retention)));
        var factor=next/shot.AdditionalRetention;
        shot.AdditionalRetention=next;
        return factor;
    }
}
