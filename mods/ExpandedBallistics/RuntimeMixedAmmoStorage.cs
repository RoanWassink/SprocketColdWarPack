using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
namespace SprocketExpandedBallistics;

public static class AmmoLoadoutApi
{
    public static bool HasLoadout(Cannon cannon)=>RuntimeAmmoLoadout.HasLoadout(cannon);
    public static int GetTypedSupply(Cannon cannon,AmmoRackBehaviour aggregate,int type,out AmmoRackBehaviour? source)=>RuntimeAmmoLoadout.TypedSupply(cannon,aggregate,type,out source);
    public static bool IsSameStorage(AmmoRackBehaviour aggregate,AmmoRackBehaviour view)=>RuntimeAmmoLoadout.SameStore(aggregate,view);
    public static void ClearStorage(AmmoRackBehaviour aggregate)=>RuntimeAmmoLoadout.EmptyStore(aggregate);
    public static void NotifyStorageDestroyed(AmmoRackBehaviour aggregate)=>ClearStorage(aggregate);
    public static int GetAvailable(Cannon cannon,AmmoRackBehaviour aggregate,int type)=>RuntimeAmmoLoadout.AvailableIn(cannon,aggregate,type);
    public static int TryTransfer(Cannon cannon,AmmoRackBehaviour source,AmmoRackBehaviour target,int type)=>RuntimeAmmoLoadout.Transfer(cannon,source,target,type);
}

internal static partial class RuntimeAmmoLoadout
{
    private sealed class PhysicalStore
    {
        internal Cannon Cannon=null!;
        internal AmmoRackBehaviour Aggregate=null!;
        internal AmmoRack? Component;
        internal int Capacity,Vuid;
        internal ushort OriginalCapacity,OriginalAmount;
        internal bool Initializing=true;
        internal bool Destroyed;
        internal readonly Dictionary<string,AmmoRackBehaviour> Views=new(StringComparer.Ordinal);
        internal int Total=>Views.Values.Sum(v=>(int)v.AmountStored);
    }
    private static readonly Dictionary<IntPtr,PhysicalStore> Stores=new();
    private static readonly Dictionary<IntPtr,PhysicalStore> ViewOwners=new();
    private static Type? CarouselApi=>AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="SprocketCarouselAutoloader")?.GetType("SprocketCarouselAutoloader.AmmoStorageApi");
    private static bool LoaderSupportsMix(out string? reason)
    {
        reason=null;var assembly=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="SprocketCarouselAutoloader");
        if(assembly==null)return true;
        if(CarouselApi?.GetProperty("MixedStorageApiVersion")?.GetValue(null) is int version&&version>=1)return true;
        reason="Mixed ammunition requires the matching updated autoloader plugin; the installed loader cannot preserve typed stock.";return false;
    }
    private static int ConservativeRackCapacity(Cannon cannon,AmmoRack rack)
    {
        var status=ShellGeometryApi.GetLoaderGeometry(cannon,out var projectile,out var charge,out var diameter);
        if(status==0)return Math.Clamp(rack.Capacity,0,ushort.MaxValue);
        if(status!=1)return 0;
        var b=rack.Blueprint;if(b==null||projectile+charge>ushort.MaxValue||diameter>ushort.MaxValue)return 0;
        return Math.Clamp(AmmoRackUtils.CalculateCapacity(b.Width,b.Height,b.Length,(ushort)Math.Ceiling(diameter),(ushort)Math.Ceiling(projectile+charge)),0,ushort.MaxValue);
    }
    private static int CarouselCapacity(Cannon cannon)
    {
        if(CarouselApi is not {} api)return 0;
        var status=ShellGeometryApi.GetLoaderGeometry(cannon,out var p,out var c,out var d);
        if(status==0)status=ShellGeometryApi.GetLoaderGeometryForProfile(cannon,ShellProfiles.Vanilla,out p,out c,out d);
        if(status!=1)return 0;
        var method=api.GetMethod("GetCarouselCapacityForGeometry");if(method==null)return 0;
        object?[] args={cannon,p,c,d,0};return method.Invoke(null,args) is int result&&result==1?Math.Clamp((int)args[4]!,0,ushort.MaxValue):0;
    }
    private static (AmmoRackBehaviour? Aggregate,int Vuid,int Capacity) CarouselStore(Cannon cannon)
    {
        var method=CarouselApi?.GetMethod("GetStorage");if(method==null)return(default,0,0);
        object?[] args={cannon,0,0,null};return method.Invoke(null,args) is int status&&status==1?
            (args[3] as AmmoRackBehaviour,(int)args[1]!,Math.Min((int)args[2]!,CarouselCapacity(cannon))):(default,0,0);
    }
    [ThreadStatic] private static bool projecting;
    [ThreadStatic] private static bool transferring;
    [ThreadStatic] private static LoadTask? consumingTask;
    private static readonly Dictionary<IntPtr,(IntPtr Cannon,IntPtr Vehicle)> FailedConsumptions=new();
    private static void FailedConsumption(LoadTask task)
    {if(TaskCannon(task) is {} cannon)FailedConsumptions[task.Pointer]=(cannon.Pointer,cannon.Vehicle?.Behaviour?.Pointer??IntPtr.Zero);}
    private static bool IsFailedConsumption(LoadTask task)=>FailedConsumptions.TryGetValue(task.Pointer,out var owner)&&TaskCannon(task) is {} cannon&&
        owner.Cannon==cannon.Pointer&&owner.Vehicle==(cannon.Vehicle?.Behaviour?.Pointer??IntPtr.Zero);
    private static void RepairFailedConsumption(LoadTask task)
    {
        if(!IsFailedConsumption(task)||task.currentRequest==LoadRequest.Consumed&&task.State!=LoadState.Loaded)return;
        // Defensive cleanup. The SetState guard blocks the audited native call
        // before it can load a projectile; cleanup cannot undo an inlined load.
        task.rack=null;task.currentRequest=LoadRequest.Consumed;
        task.SetState(LoadState.Unloaded);
    }
    [HarmonyPrefix,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.SetState)),HarmonyPriority(Priority.First+200)]
    private static void GuardFailedState(LoadTask __instance,ref LoadState __0)
    {
        if(__0==LoadState.Loaded)TraceNative(__instance,"load-state","to="+__0);
        var noRoundWaiting=__0==LoadState.Loaded&&__instance.currentRequest==LoadRequest.Consumed&&TaskCannon(__instance) is {} cannon&&HasLoadout(cannon);
        if(noRoundWaiting||IsFailedConsumption(__instance)&&(__0==LoadState.Loaded||__0==LoadState.Loading&&__instance.currentRequest!=LoadRequest.Consumed))
        {__0=LoadState.Unloaded;__instance.rack=null;__instance.currentRequest=LoadRequest.Consumed;}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.Update)),HarmonyPriority(Priority.First+200)]
    private static void BeginUpdateConsumption(LoadTask __instance,out LoadTask? __state)
    {__state=consumingTask;consumingTask=__instance;}
    [HarmonyFinalizer,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.Update))]
    private static void EndUpdateConsumption(LoadTask __instance,LoadTask? __state){try{RepairFailedConsumption(__instance);}finally{consumingTask=__state;}}
    [HarmonyPrefix,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.LoadInstantly)),HarmonyPriority(Priority.First+200)]
    private static void BeginInstantConsumption(LoadTask __instance,out LoadTask? __state)
    {__state=consumingTask;consumingTask=__instance;instantDepth++;}
    [HarmonyFinalizer,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.LoadInstantly))]
    private static void EndInstantConsumption(LoadTask __instance,LoadTask? __state){try{RepairFailedConsumption(__instance);}finally{consumingTask=__state;if(instantDepth>0)instantDepth--;}}
    private sealed record SourceCount(AmmoRackBehaviour Source,int Amount);
    private sealed record NativeRequestScope(LoadTask? PreviousTask,RefundScope? PreviousRefund,Cannon? Cannon,
        int Type,long Credit,int? Stock,SourceCount[] Sources,bool EmptyBefore);
    private static readonly Dictionary<IntPtr,NativeRequestScope> CycleBaselines=new();
    private static void CaptureCycle(LoadTask task)
    {
        try
        {
            var state=CaptureRequest(task,null,null,true);
            if(state.Stock!=null&&state.Cannon?.Vehicle?.Behaviour is {} vehicle)
            {CycleBaselines[task.Pointer]=state;ChamberVehicles[task.Pointer]=vehicle.Pointer;}
            else CycleBaselines.Remove(task.Pointer);
        }
        catch(Exception){CycleBaselines.Remove(task.Pointer);}
    }
    private static NativeRequestScope CaptureRequest(LoadTask task,LoadTask? previous,RefundScope? refund,bool allTypes=false)
    {
        var cannon=TaskCannon(task);var type=task.Request.TypeID.Value;var sources=new List<SourceCount>();
        if(cannon?.Vehicle?.Behaviour is {} vehicle&&Distributions.TryGetValue((vehicle.Pointer,cannon.VUID.Value),out var dist)&&!dist.Frozen)
            foreach(var store in dist.Stores)
            {
                if(store.Initializing||store.Destroyed)continue;
                foreach(var source in store.Views.Values)
                    if(allTypes||source.StoredTypeID.Value==type&&source.ShellSizeID.Value==task.Request.SizeID.Value)sources.Add(new(source,source.AmountStored));
            }
        return new(previous,refund,cannon,type,Chamber.Token(task.Pointer),cannon==null?null:PhysicalStock(cannon),sources.ToArray(),!HoldsRound(task));
    }
    private static void ObserveRequest(LoadTask task,NativeRequestScope state)
    {
        var cannon=state.Cannon;
        if(cannon==null||!state.EmptyBefore||task.Request.TypeID.Value!=state.Type||task.Request.Amount!=1||
            Chamber.Token(task.Pointer)!=state.Credit||task.rack?.TryCast<AmmoRackBehaviour>() is not {} source||
            source.StoredTypeID.Value!=state.Type||source.ShellSizeID.Value!=task.Request.SizeID.Value||
            !ViewOwners.TryGetValue(source.Pointer,out var store)||store.Cannon.Pointer!=cannon.Pointer||store.Destroyed||store.Initializing)return;
        var occupied=task.State is LoadState.Loading or LoadState.Loaded;
        var consumed=task.currentRequest==LoadRequest.Consumed;
        foreach(var sample in state.Sources)
            if(sample.Source.Pointer==source.Pointer&&AmmoReloadPolicy.RecoverDebit(sample.Amount,source.AmountStored,true,occupied,consumed))
            {
                FailedConsumptions.Remove(task.Pointer);CreditDebit(task,state.Type);Project(store);
                TraceNative(task,"debit-recovered","exact typed source count fell by one");return;
            }
        // Another loader can debit before entering UpdateRequest. Compare with the pre-shot/empty-cycle baseline,
        // requiring a matching one-round fall in both this exact source and the whole physical stock.
        if(CycleBaselines.TryGetValue(task.Pointer,out var cycle)&&cycle.Cannon?.Pointer==cannon.Pointer&&
            cycle.Stock is {} cycleStock&&PhysicalStock(cannon) is {} nowStock)
            foreach(var sample in cycle.Sources)
                if(sample.Source.Pointer==source.Pointer&&AmmoReloadPolicy.RecoverCycleDebit(cycleStock,nowStock,sample.Amount,source.AmountStored,occupied,consumed))
                {
                    FailedConsumptions.Remove(task.Pointer);CreditDebit(task,state.Type);Project(store);
                    TraceNative(task,"debit-recovered","exact typed source and physical stock fell by one since the shot");return;
                }
        if(task.State==LoadState.Loaded&&!consumed&&state.Stock is {} before&&PhysicalStock(cannon)==before)
        {
            ChamberVehicles[task.Pointer]=cannon.Vehicle.Behaviour.Pointer;
            NoDebitProof[task.Pointer]=(state.Type,before);
        }
    }
    [HarmonyPrefix,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.UpdateRequest)),HarmonyPriority(Priority.First)]
    private static void BeginNativeRequest(LoadTask __instance,out NativeRequestScope __state)
    {
        BeginNativeRefund(__instance,out var previousRefund);
        var previousTask=consumingTask;consumingTask=__instance;
        // Native callers can enter UpdateRequest without the outer Update prefix.
        // Resolve next-load preference at the actual consumption boundary.
        Guard("Native request boundary",()=>Stock(__instance));
        try{__state=CaptureRequest(__instance,previousTask,previousRefund);}
        catch(Exception){__state=new(previousTask,previousRefund,null,0,0,null,Array.Empty<SourceCount>(),false);}
        TraceNative(__instance,"request",null);
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.UpdateRequest))]
    private static void EndNativeRequest(LoadTask __instance,NativeRequestScope? __state,Exception? __exception)
    {
        try
        {
            if(__exception==null&&__state!=null)Guard("Native debit observation",()=>ObserveRequest(__instance,__state));
            RepairFailedConsumption(__instance);
        }
        finally{consumingTask=__state?.PreviousTask;refundScope=__state?.PreviousRefund;}
    }
    private sealed class RefundScope
    {
        internal LoadTask Task=null!;
        internal AmmoRackBehaviour Original=null!,Typed=null!;
        internal PhysicalStore Store=null!;
        internal readonly RefundLatch Latch=new();
    }
    [ThreadStatic] private static RefundScope? refundScope;
    [HarmonyPrefix,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.Unload)),HarmonyPriority(Priority.First+300)]
    private static void BeginUnloadRefund(LoadTask __instance,out RefundScope? __state)=>BeginNativeRefund(__instance,out __state);
    [HarmonyPrefix,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.Cancel)),HarmonyPriority(Priority.First+300)]
    private static void BeginCancelRefund(LoadTask __instance,out RefundScope? __state)=>BeginNativeRefund(__instance,out __state);
    private static void BeginNativeRefund(LoadTask __instance,out RefundScope? __state)
    {
        __state=refundScope;
        if(refundScope?.Task.Pointer==__instance.Pointer)return;
        refundScope=null;
        if(!HoldsRound(__instance)||__instance.rack?.TryCast<AmmoRackBehaviour>() is not {} original)return;
        var cannon=TaskCannon(__instance);if(cannon==null)return;
        PhysicalStore? store=null;AmmoRackBehaviour? typed=null;
        if(ViewOwners.TryGetValue(original.Pointer,out var viewOwner)){store=viewOwner;typed=original;}
        else if(Stores.TryGetValue(original.Pointer,out var aggregateOwner))
        {
            store=aggregateOwner;
            if(TryProfileForType(cannon,__instance.Request.TypeID.Value,out var profile))store.Views.TryGetValue(profile,out typed);
        }
        if(store==null||typed==null||store.Initializing||store.Destroyed||store.Cannon.Pointer!=cannon.Pointer)return;
        refundScope=new RefundScope{Task=__instance,Original=original,Typed=typed,Store=store};
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.Unload))]
    private static void EndUnloadRefund(RefundScope? __state)=>EndNativeRefund(__state);
    [HarmonyFinalizer,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.Cancel))]
    private static void EndCancelRefund(RefundScope? __state)=>EndNativeRefund(__state);
    private static void EndNativeRefund(RefundScope? __state){refundScope=__state;}
    private static bool TryRefund(AmmoRackBehaviour receiver,ushort amount,out ushort leftover)
    {
        leftover=amount;var scope=refundScope;
        if(scope==null||scope.Latch.Used||amount!=1||receiver.Pointer!=scope.Original.Pointer||scope.Store.Destroyed||scope.Store.Initializing||scope.Store.Total>=scope.Store.Capacity)return false;
        scope.Latch.TryTake(); // once only: the native refund of one occupied round adds exactly one round
        var old=transferring;transferring=true;
        try{leftover=scope.Typed.LoadAmmo(1);}
        finally{transferring=old;Project(scope.Store);}
        return true;
    }
    private static Cannon? TaskCannon(LoadTask task)=>task.target?.TryCast<WeaponBehaviour>()?.mount?.TryCast<Cannon>();
    private static void ClearPhysicalStores(){Stores.Clear();ViewOwners.Clear();FailedConsumptions.Clear();refundScope=null;SwitchTraceBudget.Clear();switchTraceActions=0;}
    private static readonly Dictionary<IntPtr,int> SwitchTraceBudget=new();
    private static int switchTraceActions;
    private static void BeginSwitchTrace(Cannon cannon,LoadTask? task,string next)
    {
        if(switchTraceActions++>=16)return;
        SwitchTraceBudget[cannon.Pointer]=8;
        if(task!=null)TraceNative(task,"selected","next="+next);
    }
    private static void TraceNative(LoadTask task,string stage,string? extra)
    {
        var cannon=TaskCannon(task);if(cannon==null||!SwitchTraceBudget.TryGetValue(cannon.Pointer,out var budget)||budget<=0)return;
        SwitchTraceBudget[cannon.Pointer]=budget-1;
        Plugin.ModLog.LogInfo($"[Ammo switch trace] cannon={cannon.VUID.Value} stage={stage} state={task.State} pending={task.currentRequest} request={task.Request.TypeID.Value} source={task.rack?.StoredTypeID.Value.ToString()??"none"} {extra}");
    }
    internal static void TraceLaunch(Cannon cannon,int incoming,string? resolved)
    {
        var task=cannon.Behaviour?.LoadTask?.TryCast<LoadTask>();
        if(task!=null)TraceNative(task,"launch",$"incoming={incoming} resolved={resolved??"unknown"}");
    }
    private static Distribution Freeze(Cannon cannon,string reason)
    {Report("Ammunition mix unavailable: "+reason+" Return to Edit to fix fit/loadout.");return new Distribution{Frozen=true,Instance=cannon,FailureReason=reason};}
    internal static bool HasBlockedMix(Cannon cannon)=>cannon.Vehicle?.Behaviour is {} vehicle&&
        Distributions.TryGetValue((vehicle.Pointer,cannon.VUID.Value),out var dist)&&dist.Frozen;
    internal static bool HasActiveMix(Cannon cannon)=>cannon.Vehicle?.Behaviour is {} vehicle&&
        Distributions.TryGetValue((vehicle.Pointer,cannon.VUID.Value),out var dist)&&!dist.Frozen&&dist.Stores.Count>0;
    internal static bool TryProfileForType(Cannon cannon,int type,out string profile)
    {
        profile="";var instance=cannon.Vehicle?.Behaviour;if(instance==null)return false;
        if(!Distributions.TryGetValue((instance.Pointer,cannon.VUID.Value),out var dist)||dist.Frozen)return false;
        if(dist.InitialChamberProfile!=null&&dist.InitialNativeType==type){profile=dist.InitialChamberProfile;return true;}
        foreach(var pair in dist.Types)if(pair.Value.Value==type){profile=pair.Key;return true;}
        return false;
    }
    internal static void ShotLaunched(Cannon cannon,string profile)
    {
        var vehicle=cannon.Vehicle?.Behaviour;
        if(vehicle!=null&&Distributions.TryGetValue((vehicle.Pointer,cannon.VUID.Value),out var dist)&&dist.InitialChamberProfile==profile)
        {dist.InitialChamberProfile=null;dist.InitialNativeType=-1;}
    }
    private static PhysicalStore? Owned(Cannon cannon,AmmoRackBehaviour aggregate)=>Stores.TryGetValue(aggregate.Pointer,out var store)&&store.Cannon.Pointer==cannon.Pointer?store:null;
    internal static int TypedSupply(Cannon cannon,AmmoRackBehaviour aggregate,int type,out AmmoRackBehaviour? source)
    {
        source=null;if(!HasLoadout(cannon))return 0;
        var s=Owned(cannon,aggregate);if(s==null||s.Initializing||s.Destroyed||!TryProfileForType(cannon,type,out var id)||!s.Views.TryGetValue(id,out var view)||view.AmountStored==0)return -1;
        source=view;return 1;
    }
    internal static bool SameStore(AmmoRackBehaviour aggregate,AmmoRackBehaviour view)=>Stores.TryGetValue(aggregate.Pointer,out var s)&&ViewOwners.TryGetValue(view.Pointer,out var owner)&&ReferenceEquals(s,owner);
    internal static int AvailableIn(Cannon cannon,AmmoRackBehaviour aggregate,int type)
    {if(!HasLoadout(cannon))return -1;var s=Owned(cannon,aggregate);return s!=null&&!s.Destroyed&&TryProfileForType(cannon,type,out var id)&&s.Views.TryGetValue(id,out var v)?v.AmountStored:0;}
    private static void Project(PhysicalStore store)
    {
        var target=store.Total;if(target>store.Capacity)throw new InvalidOperationException("Mixed ammunition exceeds its physical storage.");
        var old=projecting;projecting=true;
        try
        {
            while(store.Aggregate.AmountStored>target)if(!store.Aggregate.TakeShell())throw new InvalidOperationException("Native aggregate decrement failed.");
            if(store.Aggregate.AmountStored<target)store.Aggregate.LoadAmmo((ushort)(target-store.Aggregate.AmountStored));
            if(store.Aggregate.AmountStored!=target)throw new InvalidOperationException("Native aggregate count differs from typed stock.");
        }
        finally{projecting=old;}
    }
    internal static void EmptyStore(AmmoRackBehaviour aggregate)
    {
        if(!Stores.TryGetValue(aggregate.Pointer,out var store))return;
        store.Destroyed=true;
        foreach(var view in store.Views.Values)view.amountStored=0;
        Project(store);
    }
    internal static int Transfer(Cannon cannon,AmmoRackBehaviour source,AmmoRackBehaviour target,int type)
    {
        if(!HasLoadout(cannon))return 0;
        var a=Owned(cannon,source);var b=Owned(cannon,target);
        if(a==null||b==null||a.Destroyed||b.Destroyed||a.Initializing||b.Initializing||ReferenceEquals(a,b)||!TryProfileForType(cannon,type,out var id)||
            !a.Views.TryGetValue(id,out var from)||!b.Views.TryGetValue(id,out var to)||from.AmountStored==0||b.Total>=b.Capacity)return -1;
        var old=transferring;transferring=true;
        try
        {
            if(!from.TakeShell())return -1;
            var destinationBefore=to.AmountStored;
            var leftover=to.LoadAmmo(1);
            if(leftover!=0||to.AmountStored!=destinationBefore+1)
            {
                var rollbackBefore=from.AmountStored;
                if(from.LoadAmmo(1)!=0||from.AmountStored!=rollbackBefore+1)throw new InvalidOperationException("Native ammunition transfer rollback failed.");
                return -1;
            }
            return 1;
        }
        finally{transferring=old;Project(a);Project(b);}
    }
    private static PhysicalStore MakeStore(Cannon cannon,AmmoRackBehaviour aggregate,AmmoRack? component,int capacity,int vuid,Distribution distribution)
    {
        if(Stores.ContainsKey(aggregate.Pointer))throw new InvalidOperationException("Physical ammunition storage already has an owner.");
        var s=new PhysicalStore{Cannon=cannon,Aggregate=aggregate,Component=component,Capacity=Math.Clamp(capacity,0,ushort.MaxValue),Vuid=vuid,
            OriginalCapacity=aggregate.Capacity,OriginalAmount=aggregate.AmountStored};
        Stores.Add(aggregate.Pointer,s);distribution.Stores.Add(s);
        aggregate.Capacity=(ushort)s.Capacity;
        foreach(var pair in distribution.Types)
        {
            var view=new AmmoRackBehaviour($"mixed:{vuid}:{pair.Key}",pair.Value,aggregate.ShellSizeID,s.Capacity);
            view.amountStored=0;s.Views.Add(pair.Key,view);ViewOwners.Add(view.Pointer,s);
        }
        return s;
    }
    private static void RollbackStores(Distribution distribution)
    {
        var old=projecting;projecting=true;
        try
        {
            foreach(var store in distribution.Stores)
            {
                foreach(var view in store.Views.Values)ViewOwners.Remove(view.Pointer);
                store.Aggregate.Capacity=store.OriginalCapacity;
                while(store.Aggregate.AmountStored>store.OriginalAmount)if(!store.Aggregate.TakeShell())throw new InvalidOperationException("Native storage rollback decrement failed.");
                if(store.Aggregate.AmountStored<store.OriginalAmount)store.Aggregate.LoadAmmo((ushort)(store.OriginalAmount-store.Aggregate.AmountStored));
                if(store.Aggregate.AmountStored!=store.OriginalAmount)throw new InvalidOperationException("Native storage rollback amount mismatch.");
                Stores.Remove(store.Aggregate.Pointer);
            }
            distribution.Stores.Clear();
        }
        finally{projecting=old;}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(AmmoRequest),nameof(AmmoRequest.IsMatch))]
    private static void NoAggregateSource(AmmoRequest __instance,IAmmoSource __0,ref bool __result)
    {
        if(__0==null||!Stores.TryGetValue(__0.Pointer,out var store))return;
        __result=false;
        var task=consumingTask;var cannon=task==null?null:TaskCannon(task);
        if(cannon==null||store.Cannon.Pointer!=cannon.Pointer||task!.Request.TypeID.Value!=__instance.TypeID.Value||task.Request.SizeID.Value!=__instance.SizeID.Value||
            __instance.Amount!=1||__instance.SizeID.Value!=store.Aggregate.ShellSizeID.Value)return;
        __result=TypedSupply(cannon,store.Aggregate,__instance.TypeID.Value,out _) ==1;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(AmmoRackBehaviour),nameof(AmmoRackBehaviour.LoadAmmo))]
    private static bool LimitTypedLoad(AmmoRackBehaviour __instance,ref ushort __0,ref ushort __result,out ushort __state)
    {
        __state=__0;
        if(projecting)return true;
        if(!transferring&&TryRefund(__instance,__0,out var refundLeftover)){__result=refundLeftover;return false;}
        if(Stores.ContainsKey(__instance.Pointer)){__result=__0;return false;}
        if(ViewOwners.TryGetValue(__instance.Pointer,out var store))
        {
            if(!store.Initializing&&!transferring){__result=__0;return false;}
            __0=(ushort)Math.Min(__0,Math.Max(0,store.Capacity-store.Total));
        }
        return true;
    }
    [HarmonyPostfix,HarmonyPatch(typeof(AmmoRackBehaviour),nameof(AmmoRackBehaviour.LoadAmmo))]
    private static void TypedLoaded(AmmoRackBehaviour __instance,ushort __0,ref ushort __result,ushort __state)
    {
        // Native LoadAmmo returns unaccepted rounds, including our shared-slot clamp.
        __result=(ushort)Math.Min(ushort.MaxValue,(int)__result+Math.Max(0,__state-__0));
        if(!projecting&&!transferring&&ViewOwners.TryGetValue(__instance.Pointer,out var store))Project(store);
    }
    [HarmonyPrefix,HarmonyPatch(typeof(AmmoRackBehaviour),nameof(AmmoRackBehaviour.TakeShell))]
    private static bool AggregateDrain(AmmoRackBehaviour __instance,ref bool __result)
    {
        if(projecting)return true;
        if(ViewOwners.TryGetValue(__instance.Pointer,out var viewStore))
        {
            if(transferring)return true;
            var caller=consumingTask==null?null:TaskCannon(consumingTask);
            var allowed=caller!=null&&caller.Pointer==viewStore.Cannon.Pointer&&consumingTask!.Request.Amount==1&&
                consumingTask.Request.TypeID.Value==__instance.StoredTypeID.Value&&consumingTask.Request.SizeID.Value==__instance.ShellSizeID.Value;
            if(!allowed){__result=false;if(consumingTask!=null)FailedConsumption(consumingTask);}return allowed;
        }
        if(!Stores.TryGetValue(__instance.Pointer,out var store))return true;
        if(consumingTask is {} task)
        {
            __result=false;var cannon=TaskCannon(task);var request=task.Request;
            if(cannon==null||cannon.Pointer!=store.Cannon.Pointer||request.Amount!=1||request.SizeID.Value!=__instance.ShellSizeID.Value||
                TypedSupply(cannon,__instance,request.TypeID.Value,out var typed)!=1||typed==null){FailedConsumption(task);return false;}
            // Native UpdateRequest consumes inside its body. Redirect its actual
            // source before TakeShell returns and native SetState reads identity.
            task.rack=typed.Cast<IAmmoSource>();typed.MarkInUse(__instance.currentUseLoadTime);
            __result=typed.TakeShell();TraceNative(task,"debit",$"actualType={typed.StoredTypeID.Value} accepted={__result}");if(__result){FailedConsumptions.Remove(task.Pointer);CreditDebit(task,typed.StoredTypeID.Value);}else FailedConsumption(task);return false;
        }
        var had=__instance.AmountStored>0;EmptyStore(__instance);__result=had;return false;
    }
    [HarmonyPostfix,HarmonyPatch(typeof(AmmoRackBehaviour),nameof(AmmoRackBehaviour.TakeShell))]
    private static void TypedTaken(AmmoRackBehaviour __instance,bool __result)
    {
        if(projecting||transferring||!ViewOwners.TryGetValue(__instance.Pointer,out var store))return;
        if(consumingTask!=null){if(__result){FailedConsumptions.Remove(consumingTask.Pointer);CreditDebit(consumingTask,__instance.StoredTypeID.Value);}else FailedConsumption(consumingTask);}
        if(__result)Project(store);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(AmmoRackBehaviour),nameof(AmmoRackBehaviour.ApplyDamage))]
    private static void StorageDamaged(AmmoRackBehaviour __instance)
    {if(Stores.ContainsKey(__instance.Pointer)&&__instance.AmountStored==0)EmptyStore(__instance);}
}
