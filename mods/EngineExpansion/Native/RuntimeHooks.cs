using HarmonyLib;
using Sprocket.Engines;
using Sprocket.Vehicles.Engines;
using SprocketEngineExpansion.Core;
using Unity.Collections;
using Unity.Mathematics;
using Sprocket.Vehicles.Powertrains;
namespace SprocketEngineExpansion.Native;

[HarmonyPatch]
internal static class RuntimeHooks
{
    private sealed record Binding(EngineBehaviour Behaviour,EngineBlueprint Blueprint,Plans.Snapshot Snapshot,float NativeFuel,AssemblyCommit Commit)
    {internal int LastBand=-1;}
    private static readonly Dictionary<IntPtr,Binding> bindings=new();
    private static readonly HashSet<IntPtr> pending=new();
    [ThreadStatic]private static bool draining;
    private static int reports,fuelReports;
    private static void CompleteReaders(VehicleMovementRegister register)
    {
        if(Environment.CurrentManagedThreadId!=Plugin.MainThreadId)throw new InvalidOperationException("Engine table mutation is restricted to the loader's Unity main thread.");
        var combined=register.currentUpdateHandle;combined.Complete();register.currentUpdateHandle=combined;
        var physics=register.physicsUpdateJobHandles;
        if(physics!=null)for(int i=0;i<physics.Length;i++){var handle=physics[i];handle.Complete();physics[i]=handle;}
    }
    internal static void Refresh(CombustionEngine engine)
    {if(engine.Behaviour?.TryCast<EngineBehaviour>() is {} behaviour&&bindings.TryGetValue(behaviour.Pointer,out var binding)&&binding.Blueprint.Pointer==engine.Blueprint.Pointer)pending.Add(behaviour.Pointer);}
    // The 0.2.56.2 API takes an explicit deltaTime; this prefix leaves it unchanged.
    // Mirror the native FixedUpdate completion boundary, before it schedules the next step.
    // Complete both physics handles and the combined force-update handle: LateFixedUpdate
    // alone completes only the physics set and is insufficient for freeing referenced buffers.
    // Geometry/state/obstruction callbacks only enqueue; none replace/free a table mid-job.
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleMovementRegister),nameof(VehicleMovementRegister.FixedUpdate),new[]{typeof(float)})]
    private static void CompletedBoundary(VehicleMovementRegister __instance)
    {
        if(draining||pending.Count==0)return;draining=true;
        try
        {
            if(VehicleMovementRegister.Instance?.Pointer!=__instance.Pointer)return;
            CompleteReaders(__instance);
            var batch=pending.ToArray();pending.Clear();
            foreach(var pointer in batch)
            {
                if(!bindings.TryGetValue(pointer,out var binding))continue;
                try{binding.Behaviour.SetBlueprint(binding.Blueprint.Cast<IEngineBlueprint>());}
                catch(Exception ex){Plugin.Warn("completed boundary refresh",ex);}
            }
        }
        catch(Exception ex){Plugin.Warn("completion boundary",ex);}
        finally{draining=false;}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(EngineBehaviour),nameof(EngineBehaviour.SetBlueprint))]
    private static bool BeforeSet(EngineBehaviour __instance,IEngineBlueprint __0,out bool __state)
    {
        __state=true;
        if(Plans.NativeRead>0||draining)return true; // Already inside a completed adapter transaction.
        var bp=__0.TryCast<EngineBlueprint>();
        if(!bindings.ContainsKey(__instance.Pointer)&&(bp==null||Plans.EffectiveKind(bp)==EngineKind.Petrol))return true;
        try
        {
            if(Environment.CurrentManagedThreadId!=Plugin.MainThreadId)throw new InvalidOperationException("Off-thread engine SetBlueprint refused for modified engine.");
            // Normal game SetBlueprint callers are also protected before original native disposal.
            if(VehicleMovementRegister.Instance is {} register)CompleteReaders(register);
            else if(bindings.ContainsKey(__instance.Pointer))throw new InvalidOperationException("No current movement register to synchronize a bound engine.");
            return true;
        }
        catch(Exception ex){__state=false;Plugin.Warn("SetBlueprint synchronization refused",ex);return false;}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBehaviour),nameof(EngineBehaviour.SetBlueprint))]
    private static void Set(EngineBehaviour __instance,IEngineBlueprint __0,bool __state)
    {
        if(!__state)return;
        if(Plans.NativeRead>0)return;
        pending.Remove(__instance.Pointer);
        bindings.Remove(__instance.Pointer);
        var bp=__0.TryCast<EngineBlueprint>();if(bp==null||Plans.EffectiveKind(bp)==EngineKind.Petrol)return;
        try
        {
            var snapshot=Plans.Capture(bp);
            bool active=false;
            var commit=new AssemblyCommit(snapshot.Plan.Selection,0,active);
            var points=snapshot.Plan.Sample(active).Points;
            var next=new NativeArray<float2>(points.Length,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
            var job=__instance.physicsJob;
            bool committed=false;
            var previous=job.torqueCurveLookupTable;
            try
            {
                for(int i=0;i<points.Length;i++)next[i]=new float2((float)(points[i].X*snapshot.Plan.MaxRPM*Math.PI/30),(float)points[i].Y);
                job.torqueCurveLookupTable=next;
                job.maxTorque=(float)points.Max(p=>p.Y);
                __instance.physicsJob=job; // IL2CPP value-wrapper writeback, never mutate an uncommitted copy.
                committed=true;
            }
            catch{if(!committed&&next.IsCreated)next.Dispose();throw;}
            // Never free a newly committed table because releasing the prior allocation throws.
            try{if(previous.IsCreated)previous.Dispose();}catch(Exception ex){Plugin.Warn("prior torque table disposal",ex);}
            float nativeFuel=__instance.maxFuelUsePerRotation;
            bindings[__instance.Pointer]=new(__instance,bp,snapshot,nativeFuel,commit);
            __instance.maxFuelUsePerRotation=nativeFuel*(float)commit.Fuel(snapshot.Plan,__instance.RPM);
            if(reports++<32)Plugin.ModLog.LogInfo($"[Engine Expansion] CURVE blueprint={bp.Pointer} behaviour={__instance.Pointer} kind={snapshot.Plan.Selection.Kind} boost={snapshot.Plan.Selection.BoostPercent} validAssembly={AssemblyStatus.ValidFor(__instance)} knots={points.Length} jobMaxTorque={__instance.physicsJob.maxTorque} maxRPM={__instance.MaxRPM} nativeFuel={nativeFuel} fuelCache={__instance.maxFuelUsePerRotation} nativePeakKW={snapshot.Plan.Native.MaxProduct(snapshot.Plan.IdleRPM/snapshot.Plan.NativeMaxRPM,1)*snapshot.Plan.NativeMaxRPM/EnginePlan.NmRpmPerKW} profilePeakKW={snapshot.Plan.Profile.Power*snapshot.Plan.Native.MaxProduct(snapshot.Plan.IdleRPM/snapshot.Plan.NativeMaxRPM,1)*snapshot.Plan.NativeMaxRPM/EnginePlan.NmRpmPerKW} coefficient={snapshot.OutputCoefficient} shiftMidRPM={(bp.Downshift+bp.Upshift)*.5}");
        }
        catch(Exception ex)
        {
            Plugin.Warn("runtime curve",ex);
            // Restore a coherent native job/cache if adapting this particular engine failed.
            Plans.NativeRead++;try{__instance.SetBlueprint(__0);}catch(Exception restore){Plugin.Warn("runtime restore",restore);}finally{Plans.NativeRead--;}
        }
    }
    [HarmonyPrefix,HarmonyPatch(typeof(EngineBehaviour),nameof(EngineBehaviour.Update),new[]{typeof(float)})]
    private static void Update(EngineBehaviour __instance)
    {
        if(!bindings.TryGetValue(__instance.Pointer,out var binding))return;
        try
        {
            // Native Update consumes this cache directly: radians/(2*pi), delta time, injection fraction.
            // Recompute from the baseline, never multiply last frame's modified cache.
            bool active=false;
            var effective=Selections.Get(binding.Blueprint) with {Kind=Plans.EffectiveKind(binding.Blueprint),Turbo=false};
            if(!binding.Commit.Matches(effective,0,active))pending.Add(__instance.Pointer);
            __instance.maxFuelUsePerRotation=binding.NativeFuel*(float)binding.Commit.Fuel(binding.Snapshot.Plan,__instance.RPM);
            int band=(int)(__instance.RPM/binding.Snapshot.Plan.MaxRPM*4);
            if(band!=binding.LastBand&&__instance.State==Sprocket.Powertrains.Engines.EngineState.Running)
            {
                binding.LastBand=band;
                if(fuelReports++<64)Plugin.ModLog.LogInfo($"[Engine Expansion] FUEL behaviour={__instance.Pointer} RPM={__instance.RPM} generatedTorque={__instance.GeneratedTorque} cache={__instance.maxFuelUsePerRotation} coefficient={binding.Commit.Fuel(binding.Snapshot.Plan,__instance.RPM)} committedBoost={binding.Commit.Selection.BoostPercent} active={binding.Commit.Active} revision={binding.Commit.Revision}");
            }
        }
        catch(Exception ex){Plugin.Warn("fuel cache",ex);}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(EngineBehaviour),nameof(EngineBehaviour.Dispose))]
    private static void Dispose(EngineBehaviour __instance){bindings.Remove(__instance.Pointer);pending.Remove(__instance.Pointer);}
    // Update observes state through AssemblyCommit.Matches and queues the existing safe boundary.
    // Do not detour SetStateInternal: the reviewed crash reaches padding after its short return.
}



