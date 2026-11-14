using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
namespace SprocketExpandedBallistics;

internal sealed record ArmourComparisonReference(string Id,string Label,string Behavior,float GunDiameterM,float DiameterM,
    float MassKg,float LengthM,float DensityKgM3,ushort PenetratorConstant,float ReferenceSpeedMps);
internal sealed record ArmourComparisonProbeResult(ComparisonProbeOutcome Outcome,string Reason,double? RemainingMm=null,IReadOnlyList<ArmourComparisonLayer>? Layers=null);

internal static class RuntimeArmourComparisonRunner
{
    private sealed class Trial
    {
        internal readonly ArmourComparisonTarget Target;
        internal ArmourComparisonSelectedInput? ExpectedSelected;internal bool SelectedMismatch;
        internal PenetrationSimulation? Simulation;internal short Index;
        internal bool Seen;internal bool Matched;internal bool NonArmour;internal ArmourComparisonProbeResult? FrontBoundary;
        internal readonly Dictionary<int,int> ArmourObjects=new();internal string Failure="Requested face was not reached.";
        internal Trial(ArmourComparisonTarget target){Target=target;}
    }
    [ThreadStatic] private static Trial? active;
    internal static bool IsProbing=>active!=null;
    internal static IVehicleGateway? CurrentOwner=>active?.Target.Owner;
    internal static IReadOnlyList<ArmourComparisonReference> References {get;}=new[]{
        new ArmourComparisonReference("reference_apfsds","APFSDS reference","apfsds",.125f,.022f,
            (float)(Math.PI*.022*.022*.25*.6*17500),.6f,17500,1800,1700),
        new ArmourComparisonReference("reference_heat","Single-charge HEAT reference","heat",.125f,.00625f,1.55f,.6f,8900,1800,1700)};
    private static int mainThread;
    internal static void Configure(){mainThread=Environment.CurrentManagedThreadId;active=null;}
    internal static void Clear(){active=null;}
    internal static void ValidateSelectedInput(PenetratorInfo input)
    {
        if(active is {ExpectedSelected:{} selected} trial&&!selected.MatchesInput(input))trial.SelectedMismatch=true;
    }
    internal static bool IsCurrent(ArmourComparisonTarget target,out string reason)
    {
        reason="The design or comparison settings changed; fire a new simulator sample.";
        try
        {
            if(!RuntimeShellEra.IsActivePreviewOwner(target.Owner)||
                target.Structure==null||target.Owner==null||target.Owner.gameObject==null||
                target.Structure.StructureHash!=target.StructureHash||
                (target.Structure.Position-target.StructurePosition).sqrMagnitude>1e-10f||
                Math.Abs(Quaternion.Dot(target.Structure.Orientation,target.StructureOrientation))<.999999f||
                !ReferenceEquals(target.Snapshot,RuntimeArmourResponses.Snapshot))return false;
            reason="";return true;
        }
        catch(Exception){return false;}
    }
    internal static ArmourComparisonProbeResult Probe(ArmourComparisonTarget target,ArmourComparisonReference reference,
        double angleFromNormal,double nominalMm)
    {
        static ArmourComparisonProbeResult Unknown(string reason)=>new(ComparisonProbeOutcome.Indeterminate,reason);
        if(Environment.CurrentManagedThreadId!=mainThread||active!=null)return Unknown("Comparison requires an idle editor main thread.");
        if(!IsCurrent(target,out var stale))return Unknown(stale);
        if(!double.IsFinite(angleFromNormal)||angleFromNormal<0||angleFromNormal>=90)return Unknown("Exactly grazing or invalid angle: no finite protection value.");
        if(!double.IsFinite(nominalMm)||nominalMm<=0||nominalMm>10000||!References.Contains(reference))return Unknown("Invalid reference or nominal penetration.");
        var normal=target.Normal.normalized;
        if(Vector3.Dot(normal,target.IncomingDirection)>0)normal=-normal;
        var tangent=target.IncomingDirection-normal*Vector3.Dot(target.IncomingDirection,normal);
        if(tangent.sqrMagnitude<1e-8f)tangent=Vector3.Cross(normal,Math.Abs(normal.y)<.9f?Vector3.up:Vector3.right);
        tangent=tangent.normalized;
        var radians=(float)(angleFromNormal*Math.PI/180);
        var direction=(-normal*Mathf.Cos(radians)+tangent*Mathf.Sin(radians)).normalized;
        if(!RuntimeArmourComparisonCapture.Finite(direction)||direction.sqrMagnitude<.99f)return Unknown("Invalid comparison direction.");
        var speed=PenetrationUtils.ComputeRequiredPenetrationSpeed(reference.DiameterM*1000,reference.MassKg,(float)nominalMm,reference.PenetratorConstant);
        if(!float.IsFinite(speed)||speed<=0)return Unknown("Native reference speed conversion failed.");
        var profile=new ShellProfile(reference.Id,reference.Label,new DartSettings(DiameterRatio:reference.DiameterM/reference.GunDiameterM,
            LengthInCalibres:reference.LengthM/reference.GunDiameterM,Density:reference.DensityKgM3),reference.Behavior,
            ChemicalPenetrationMm:reference.Behavior=="heat"?nominalMm:0,ReferenceCalibreMm:125);
        var context=new ShellImpactContext{ProfileId=profile.Id,Profile=profile,ThreatClass="unknown",LiveImpact=false,
            Geometry=new(reference.LengthM,reference.DiameterM,reference.MassKg,reference.GunDiameterM),
            ArmourSnapshot=target.Snapshot,Experimental=target.Snapshot.Experimental,FiringEra=target.Era,
            GunDiameter=reference.GunDiameterM,ChemicalInitialized=true,ChemicalBudget=nominalMm,SharedPreviewCells=new EraCells()};
        var input=new PenetratorInfo(reference.MassKg,reference.DiameterM,reference.DensityKgM3,0,
            reference.PenetratorConstant,target.Point-direction*.025f,direction*speed);
        return Run(target,input,context);
    }
    internal static ArmourComparisonProbeResult ProbeSelected(ArmourComparisonTarget target,ArmourComparisonSelectedInput selected)
    {
        if(Environment.CurrentManagedThreadId!=mainThread||active!=null)
            return new(ComparisonProbeOutcome.Indeterminate,"Comparison requires an idle editor main thread.");
        if(!IsCurrent(target,out var reason))return new(ComparisonProbeOutcome.Indeterminate,reason);
        return Run(target,selected.Input(),selected.Context(target),selected);
    }
    internal static ArmourComparisonProbeResult ProbeSelectedAtBudget(ArmourComparisonTarget target,ArmourComparisonSelectedInput selected,double nominalMm)
    {
        if(Environment.CurrentManagedThreadId!=mainThread||active!=null)return new(ComparisonProbeOutcome.Indeterminate,"Comparison requires an idle editor main thread.");
        if(!IsCurrent(target,out var reason))return new(ComparisonProbeOutcome.Indeterminate,reason);
        if(!double.IsFinite(nominalMm)||nominalMm<=0||nominalMm>10000||
            !float.IsFinite(selected.Diameter)||selected.Diameter<=0||!float.IsFinite(selected.Mass)||selected.Mass<=0||selected.Constant==0||
            selected.Profile.TandemPrecursorPenetrationMm!=0||ShellBalance.ImpactBehavior(selected.Profile) is not ("apfsds" or "heat"))
            return new(ComparisonProbeOutcome.Indeterminate,"Unsupported selected profile or nominal penetration.");
        var speed=PenetrationUtils.ComputeRequiredPenetrationSpeed(selected.Diameter*1000,selected.Mass,(float)nominalMm,selected.Constant);
        if(!float.IsFinite(speed)||speed<=0||!RuntimeArmourComparisonCapture.Finite(selected.Velocity)||!float.IsFinite(selected.Velocity.sqrMagnitude)||selected.Velocity.sqrMagnitude<=0)
            return new(ComparisonProbeOutcome.Indeterminate,"Selected reference speed conversion failed.");
        var varied=selected with{Velocity=selected.Velocity.normalized*speed,ChemicalBudget=nominalMm};
        return ProbeSelected(target,varied);
    }
    private static ArmourComparisonProbeResult Run(ArmourComparisonTarget target,PenetratorInfo input,ShellImpactContext context,ArmourComparisonSelectedInput? expectedSelected=null)
    {
        static ArmourComparisonProbeResult Unknown(string reason)=>new(ComparisonProbeOutcome.Indeterminate,reason);
        var previousImpact=RuntimeSpall.Impact;var previous=active;
        var oldLast=target.Structure.lastSim;var oldCache=target.Structure.SimulationCacheCount;var oldDraw=target.Structure.SimulationDrawIndex;
        var oldInternal=target.Structure.SimulationRanInternal;var oldEvent=target.Structure.SimulationRan;
        try
        {
            active=new(target){ExpectedSelected=expectedSelected};RuntimeSpall.Impact=context;
            using var evidence=ArmourComparisonEvidence.Begin(target);
            // Native notification subscribers otherwise replace the visible last sample/history.
            // No global reset and no ApplyFragmentHits: only this synchronous preview is isolated.
            target.Structure.SimulationCacheCount=0;
            target.Structure.SimulationRanInternal=null;target.Structure.SimulationRan=null;
            using var reportScope=RuntimeDamageFeed.SuspendForComparison();
            var simulation=target.Structure.Penetrate(ref input,PenetrationSimulationOptions.Default)?.TryCast<PenetrationSimulation>();
            if(active.SelectedMismatch)return Unknown("Selected native input was rewritten before replay; parity unavailable.");
            if(simulation==null||!simulation.Hit||!active.Seen||!active.Matched)return Unknown(active.Failure);
            if(!IsCurrent(target,out var stale))return Unknown(stale);
            if(active.NonArmour)return Unknown("Mixed armour/component block or unresolved target ownership: no ordered armour boundary.") with{Layers=evidence.Freeze()};
            return (active.FrontBoundary??Classify(simulation)) with{Layers=evidence.Freeze()};
        }
        catch(Exception ex){return Unknown("Native comparison unavailable: "+ex.Message);}
        finally
        {
            // Attempt every restoration even if a native setter itself fails.
            try
            {
                try{target.Structure.SimulationRanInternal=oldInternal;}
                finally
                {
                    try{target.Structure.SimulationRan=oldEvent;}
                    finally
                    {
                        try{target.Structure.lastSim=oldLast;}
                        finally
                        {
                            try{target.Structure.SimulationDrawIndex=oldDraw;}
                            finally{target.Structure.SimulationCacheCount=oldCache;}
                        }
                    }
                }
            }
            finally{RuntimeSpall.Impact=previousImpact;active=previous;}
        }
    }
    internal static void BeginFragment(CompoundStructure structure,PenetrationSimulation sim,short index)
    {
        if(active is not {} trial||trial.Target.Structure.Pointer!=structure.Pointer)return;
        if(index==0&&trial.ExpectedSelected is {} selected&&!selected.MatchesInitial(sim,index))trial.SelectedMismatch=true;
        trial.Simulation=sim;trial.Index=index;
        ArmourComparisonEvidence.BeginFragment(sim,index);
    }
    internal static void ObserveFirstBlock(MaterialBlock block,Il2CppStructArray<StructureIntersection> hits,int count)
    {
        if(active is not {} trial||trial.Simulation is not {} sim||trial.Index<0||trial.Index>=sim.FragmentCount||
            (sim.fragments[trial.Index].flags&FragmentFlag.OriginalPenetrator)==0)return;
        int e=block.EnterIntersectionIndex,x=block.ExitIntersectionIndex;
        if(e<0||x<0||e>=count||x>=count||e>=hits.Length||x>=hits.Length){trial.NonArmour=true;return;}
        if(trial.FrontBoundary!=null)return;
        bool anyArmour=false,anyComponent=false,unknown=false;
        var objects=new HashSet<int>{hits[e].objectIndex,hits[x].objectIndex};
        if(block.IntersectedObjectIndices is {} members)foreach(var member in members)objects.Add(member);
        foreach(var objectIndex in objects)
        {
            if(objectIndex<0||objectIndex>=sim.objects.Length){unknown=true;continue;}
            var id=sim.objects[objectIndex].ID;
            if(!trial.ArmourObjects.TryGetValue(id,out var kind))
            {
                kind=ObjectKind(trial.Target.Owner,trial.Target.Snapshot,id);
                trial.ArmourObjects[id]=kind;
            }
            if(kind==1)anyArmour=true;else if(kind==2)anyComponent=true;else unknown=true;
        }
        if(unknown||anyArmour&&anyComponent){trial.NonArmour=true;return;}
        if(anyComponent)
        {
            if(!trial.Seen||!trial.Matched||!RuntimeArmourComparisonCapture.TryEntry(trial.Target.Structure,sim,trial.Index,block,hits,count,out _,out _,out _,out _))
            {trial.NonArmour=true;return;}
            var f=sim.fragments[trial.Index];
            if(f.projectileProfileIndex<0||f.projectileProfileIndex>=sim.projectileProfiles.Length){trial.NonArmour=true;return;}
            var budget=f.GetBasePenetration(sim.projectileProfiles[f.projectileProfileIndex].penetratorConstant)*1000;
            if(!float.IsFinite(budget)||budget<=0){trial.NonArmour=true;return;}
            trial.FrontBoundary=new(ComparisonProbeOutcome.Perforated,"Front armour passed; positive original budget reached the first internal component, before its resistance.",budget);
            for(int i=trial.Index,steps=0;i>=0&&steps<sim.FragmentCount;steps++)
            {
                var ancestor=sim.fragments[i];
                if((ancestor.flags&(FragmentFlag.Deflected|FragmentFlag.GougeDeflect))!=0)
                {trial.FrontBoundary=new(ComparisonProbeOutcome.Stopped,"Reference ricocheted before the component boundary.");break;}
                if(ancestor.parentIndex<0)break;
                if(ancestor.parentIndex>=i){trial.NonArmour=true;break;}
                i=ancestor.parentIndex;
            }
            ArmourComparisonEvidence.Seal();
            return;
        }
        if(trial.Seen)return;
        trial.Seen=true;
        if(!RuntimeArmourComparisonCapture.TryEntry(trial.Target.Structure,sim,trial.Index,block,hits,count,
            out var point,out _,out _,out var entity)){trial.Failure="Native first entry is not an unambiguous incoming surface.";return;}
        trial.Matched=entity==trial.Target.Entity&&(point-trial.Target.Point).sqrMagnitude<=.005f*.005f;
        trial.Failure="Changed angle reaches another object or location before the selected surface; not comparable.";
    }
    internal static bool IsArmourObject(IVehicleGateway owner,ArmourCatalogueSnapshot snapshot,int id)=>ObjectKind(owner,snapshot,id)==1;
    // 0=unresolved, 1=exact armour, 2=exact non-armour component.
    private static int ObjectKind(IVehicleGateway owner,ArmourCatalogueSnapshot snapshot,int id)
    {
        var reader=owner.ObjectReader;var items=reader?.Items;
        if(items==null)return 0;
        bool armour=false;IntPtr exact=IntPtr.Zero;
        for(int i=0;i<reader!.Count;i++)
        {
            var behaviours=items[i]?.behaviours;if(behaviours==null)continue;
            for(int j=0;j<behaviours.Length;j++)
            {
                var component=behaviours[j];if(component==null||component.VUID.Value!=id)continue;
                if(exact!=IntPtr.Zero&&exact!=component.Pointer)return 0;
                exact=component.Pointer;
                if(component.TryCast<PlateStructure>()!=null)armour=true;
                else if(component.TryCast<VehicleObjectModel>() is {} model&&snapshot.Bindings is {} bindings&&
                    (bindings.TryGetCassette(model.ComponentID,out _)||bindings.TryGetMount(model.ComponentID,out _)))armour=true;
            }
        }
        return exact==IntPtr.Zero?0:armour?1:2;
    }
    internal static ArmourComparisonProbeResult Classify(PenetrationSimulation simulation)
    {
        if(simulation.FragmentCount<1||simulation.FragmentCount>16384)return new(ComparisonProbeOutcome.Indeterminate,"Unsupported native fragment count.");
        int leaf=-1;var parents=new bool[simulation.FragmentCount];
        for(int i=0;i<simulation.FragmentCount;i++)
        {
            var f=simulation.fragments[i];
            if((f.flags&FragmentFlag.OriginalPenetrator)!=0&&f.parentIndex>=0&&f.parentIndex<i)parents[f.parentIndex]=true;
        }
        for(int i=0;i<simulation.FragmentCount;i++)
        {
            if((simulation.fragments[i].flags&FragmentFlag.OriginalPenetrator)==0||parents[i])continue;
            if(leaf>=0)return new(ComparisonProbeOutcome.Indeterminate,"Multiple outgoing original branches; no unique boundary.");
            leaf=i;
        }
        if(leaf<0)return new(ComparisonProbeOutcome.Indeterminate,"No final original penetrator.");
        var final=simulation.fragments[leaf];
        for(int i=leaf,steps=0;i>=0&&steps<simulation.FragmentCount;steps++)
        {
            var ancestor=simulation.fragments[i];
            if((ancestor.flags&(FragmentFlag.Deflected|FragmentFlag.GougeDeflect))!=0)
                return new(ComparisonProbeOutcome.Stopped,"Reference ricocheted rather than perforating the selected path.");
            if(ancestor.parentIndex<0)break;
            if(ancestor.parentIndex>=i)return new(ComparisonProbeOutcome.Indeterminate,"Invalid original ancestry.");
            i=ancestor.parentIndex;
        }
        if((final.flags&(FragmentFlag.Deflected|FragmentFlag.GougeDeflect))!=0)
            return new(ComparisonProbeOutcome.Stopped,"Reference deflected rather than perforating.");
        if((final.flags&(FragmentFlag.Killed|FragmentFlag.Embedded))!=0||final.speed<=0)
            return new(ComparisonProbeOutcome.Stopped,"Original penetrator stopped; cause requires layer evidence.");
        if((final.flags&FragmentFlag.ModelExit)==0)return new(ComparisonProbeOutcome.Indeterminate,"Native termination does not prove model exit.");
        if(final.projectileProfileIndex<0||final.projectileProfileIndex>=simulation.projectileProfiles.Length)
            return new(ComparisonProbeOutcome.Indeterminate,"Missing outgoing projectile profile.");
        var budget=final.GetBasePenetration(simulation.projectileProfiles[final.projectileProfileIndex].penetratorConstant)*1000;
        if(!float.IsFinite(budget)||budget<=0)return new(ComparisonProbeOutcome.Indeterminate,"Invalid outgoing penetration.");
        return new(ComparisonProbeOutcome.Perforated,"Original penetrator exited the target model.",budget);
    }
}
