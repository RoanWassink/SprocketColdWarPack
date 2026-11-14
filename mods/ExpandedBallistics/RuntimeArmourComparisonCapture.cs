using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using UnityEngine;
namespace SprocketExpandedBallistics;

internal sealed record ArmourComparisonTarget(CompoundStructure Structure,IVehicleGateway Owner,Vector3 Point,Vector3 Normal,
    Vector3 IncomingDirection,int Entity,int StructureHash,Vector3 StructurePosition,Quaternion StructureOrientation,
    ArmourCatalogueSnapshot Snapshot,string? Era,float ActualAngleDegrees)
{
    internal ArmourComparisonSelectedInput? SelectedInput {get;init;}
    internal ArmourComparisonProbeResult? OrdinaryWholeModel {get;init;}
}

// Observes actual ordinary simulator entry geometry, never a Play impact or a
// reconstructed nearest face. All scopes restore correctly across nested calls.
[HarmonyPatch]
internal static class RuntimeArmourComparisonCapture
{
    private sealed class Attempt
    {
        internal readonly CompoundStructure Structure;internal readonly IVehicleGateway Owner;
        internal ArmourComparisonTarget? Candidate;internal bool SawSolid;
        internal ArmourComparisonSelectedInput? Input;internal PenetrationSimulation? Simulation;
        internal Attempt(CompoundStructure structure,IVehicleGateway owner){Structure=structure;Owner=owner;}
    }
    private sealed record FragmentScope(Attempt Attempt,PenetrationSimulation Sim,short Index);
    [ThreadStatic] private static Attempt? active;
    [ThreadStatic] private static FragmentScope? fragment;
    private static int mainThread;
    private static bool? inputOrderVerified;
    internal static ArmourComparisonTarget? Latest {get;private set;}
    internal static void Configure(){mainThread=Environment.CurrentManagedThreadId;Clear();RuntimeArmourComparisonFinalInput.Install();}
    internal static void Clear(){Latest=null;active=null;fragment=null;inputOrderVerified=null;RuntimeArmourComparisonParity.Clear();RuntimeArmourComparisonFinalInput.Clear();}
    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.Penetrate))]
    [HarmonyPriority(Priority.First)]
    private static void Begin(CompoundStructure __instance,out object? __state)
    {
        __state=active;
        active=null;
        if(Environment.CurrentManagedThreadId!=mainThread||RuntimeArmourComparisonRunner.IsProbing||RuntimeTandem.PreviewInsidePrecursor||
            RuntimeArmourSimulator.CurrentOwner is not {} owner||RuntimeSpall.Impact?.LiveImpact==true)return;
        active=new(__instance,owner);
    }
    internal static void CaptureInput(PenetratorInfo penetrator)
    {
        if(inputOrderVerified==null)
        {
            try
            {
                var method=AccessTools.Method(typeof(CompoundStructure),nameof(CompoundStructure.Penetrate));
                var info=Harmony.GetPatchInfo(method);
                var ordered=info==null?null:PatchProcessor.GetSortedPatchMethods(method,info.Prefixes.ToArray());
                inputOrderVerified=ordered?.LastOrDefault()==AccessTools.Method(typeof(RuntimeArmourComparisonFinalInput),"Capture");
            }
            catch(Exception){inputOrderVerified=false;}
        }
        RuntimeArmourComparisonRunner.ValidateSelectedInput(penetrator);
        if(active is {} attempt&&inputOrderVerified==true)attempt.Input=ArmourComparisonSelectedInput.Capture(penetrator,RuntimeSpall.Impact);
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.Penetrate))]
    private static Exception? End(object? __state,Exception? __exception)
    {
        try{if(__exception==null&&active?.Candidate is {} captured)
        {
            Latest=captured with{SelectedInput=active.Input,OrdinaryWholeModel=active.Simulation is {} sim?RuntimeArmourComparisonRunner.Classify(sim):null};
            if(active.Simulation is {} sample)RuntimeArmourComparisonParity.ObserveOrdinary(Latest,sample);
        }}
        catch(Exception){Latest=null;/* Optional classification never changes the ordinary sample. */}
        finally{active=__state as Attempt;}
        return __exception;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]
    private static void BeginFragment(CompoundStructure __instance,PenetrationSimulation sim,short index,out object? __state)
    {
        __state=fragment;fragment=null;
        if(active is {} attempt&&attempt.Candidate==null&&attempt.Structure.Pointer==__instance.Pointer&&index==0)
        {
            attempt.Simulation=sim;
            if(attempt.Input is {} input&&!input.MatchesInitial(sim,index))attempt.Input=null;
            fragment=new(attempt,sim,index);
        }
        if(RuntimeArmourComparisonRunner.IsProbing)RuntimeArmourComparisonRunner.BeginFragment(__instance,sim,index);
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]
    [HarmonyPriority(Priority.Last-100)]
    private static void EndFragment(object? __state,PenetrationSimulation sim,short index)
    {try{ArmourComparisonEvidence.EndFragment(sim,index);}finally{fragment=__state as FragmentScope;}}
    [HarmonyPostfix,HarmonyPatch(typeof(StructureIntersection),nameof(StructureIntersection.GetNextMaterialBlock))]
    private static void Layer(MaterialBlock __result,Il2CppStructArray<StructureIntersection> __0,int __1)
    {
        if(!__result.Valid||__result.MaterialIndex<0)return;
        if(RuntimeArmourComparisonRunner.IsProbing){RuntimeArmourComparisonRunner.ObserveFirstBlock(__result,__0,__1);return;}
        if(fragment is not {} scope||scope.Attempt.SawSolid)return;
        scope.Attempt.SawSolid=true;Latest=null;
        try
        {
            if(!TryEntry(scope.Attempt.Structure,scope.Sim,scope.Index,__result,__0,__1,out var point,out var normal,out var direction,out var entity))return;
            var target=scope.Attempt.Structure;
            var snapshot=RuntimeSpall.Impact?.ArmourSnapshot??RuntimeArmourResponses.Snapshot;
            if(!RuntimeArmourComparisonRunner.IsArmourObject(scope.Attempt.Owner,snapshot,entity))return;
            scope.Attempt.Candidate=new(target,scope.Attempt.Owner,point,normal,direction,entity,target.StructureHash,target.Position,target.Orientation,
                snapshot,RuntimeShellEra.Era(scope.Attempt.Owner),
                (float)(Math.Acos(Math.Clamp(Math.Abs(Vector3.Dot(normal,direction)),0,1))*180/Math.PI));
        }
        catch(Exception){/* Optional observation must not interrupt the simulator. */}
    }
    internal static bool TryEntry(CompoundStructure structure,PenetrationSimulation sim,short index,MaterialBlock block,
        Il2CppStructArray<StructureIntersection> hits,int count,out Vector3 point,out Vector3 normal,out Vector3 direction,out int entity)
    {
        point=normal=direction=default;entity=0;
        int entry=block.EnterIntersectionIndex;
        if(index<0||index>=sim.FragmentCount||entry<0||entry>=count||entry>=hits.Length)return false;
        var hit=hits[entry];
        if(hit.objectIndex<0||hit.objectIndex>=sim.objects.Length||!float.IsFinite(hit.distance)||hit.distance<0)return false;
        if((hit.flags&IntersectionFlags.Enter)==0||(hit.flags&IntersectionFlags.Exit)!=0)return false;
        var original=sim.fragments[index];if((original.flags&FragmentFlag.OriginalPenetrator)==0)return false;
        var axis=original.direction.normalized;
        point=sim.SimToWorldSpacePoint(original.spawnPoint+axis*hit.distance);
        normal=sim.SimToWorldSpaceDirection(structure.GetIntersectionNormal(ref hit)).normalized;
        direction=sim.SimToWorldSpaceDirection(axis).normalized;entity=sim.objects[hit.objectIndex].ID;
        return Finite(point)&&Finite(normal)&&Finite(direction)&&normal.sqrMagnitude>.99f&&direction.sqrMagnitude>.99f;
    }
    internal static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
}
