using HarmonyLib;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons;
using Sprocket.Vehicles.Weapons.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;
namespace SprocketExpandedBallistics;

// 0.22.7: "never fire without a real load" for a carried ammunition mix (user test 0.22.6: HESH ran out while HEAT was left on a
// carousel Oplot, and the gun fired in rapid succession instead of switching to HEAT with a normal reload).
// The rules are pure (AmmoSelection, ChamberLedger in AmmoChamberLedger.cs, covered by the tests); this file is the native side:
//   - every real debit from the typed stock under a native load request is a credit for that load task (CreditDebit);
//   - the gun may only report CanFire while it holds a credit whose load has had a quarter of its reported phases (FireGuard);
//   - firing, unloading and cancelling use the credit up;
//   - missing credit blocks firing. Cleanup additionally requires a completed no-debit request, unchanged physical stock and
//     a proven absence of an external loader; uncertainty never discards a possibly real round;
//   - when a type runs out the next type with rounds is requested (Choose); when nothing is left the gun stays empty, with one log line
//     and the HUD notice "Out of <type>".
// The carousel mod is untouched: its own requests only see typed views with rounds, and our request stays a valid native AmmoRequest.
internal static partial class RuntimeAmmoLoadout
{
    private static readonly ChamberLedger<IntPtr> Chamber=new();
    private static readonly Dictionary<IntPtr,IntPtr> ChamberVehicles=new();
    private static readonly Dictionary<(IntPtr Vehicle,int Vuid),string> EmptyLogged=new();
    private static readonly Dictionary<IntPtr,(int Type,int Stock)> NoDebitProof=new();
    // Rounds taken inside LoadInstantly (the first chamber) are instant by design: they are exempt from the load-time rule.
    [ThreadStatic] private static int instantDepth;

    private static double PhaseSeconds(LoadTask task)
    {
        var sum=(double)task.prepareTime+task.pickupTime+task.totalDistance+task.dropoffTime;
        return double.IsFinite(sum)&&sum>0?sum:0;
    }
    /// <summary>A real debit happened inside the native request of this task: the round now in the gun is real.</summary>
    private static void CreditDebit(LoadTask task,int nativeType)
    {
        var vehicle=TaskCannon(task)?.Vehicle?.Behaviour?.Pointer??IntPtr.Zero;
        ChamberVehicles[task.Pointer]=vehicle;
        Chamber.Debit(task.Pointer,Time.time,nativeType.ToString(),instantDepth>0);
        NoDebitProof.Remove(task.Pointer);
    }
    /// <summary>The mix was stocked while a round was already in the gun (the game debited it).</summary>
    private static void CreditInitial(LoadTask task,IntPtr vehicle,string? profile)
    {
        ChamberVehicles[task.Pointer]=vehicle;
        Chamber.Initial(task.Pointer,profile);
        NoDebitProof.Remove(task.Pointer);
    }
    private static void ForgetVehicle(IntPtr vehicle)
    {
        foreach(var key in ChamberVehicles.Where(p=>p.Value==vehicle).Select(p=>p.Key).ToArray()){Chamber.Spend(key);ChamberVehicles.Remove(key);NoDebitProof.Remove(key);CycleBaselines.Remove(key);}
        foreach(var key in EmptyLogged.Keys.Where(k=>k.Vehicle==vehicle).ToArray())EmptyLogged.Remove(key);
    }
    private static void ClearChamber(){Chamber.Reset();ChamberVehicles.Clear();EmptyLogged.Clear();NoDebitProof.Clear();CycleBaselines.Clear();}
    private static int? PhysicalStock(Cannon cannon)
    {
        var vehicle=cannon.Vehicle?.Behaviour;
        if(vehicle==null||!Distributions.TryGetValue((vehicle.Pointer,cannon.VUID.Value),out var dist)||dist.Frozen||dist.Stores.Count==0)return null;
        int total=0;foreach(var store in dist.Stores){if(store.Initializing||store.Destroyed)return null;total+=store.Total;}
        return total;
    }
    private static bool? ExternalLoader(Cannon cannon)
    {
        try
        {
            if(CarouselApi==null)return false;
            var method=CarouselApi.GetMethod("GetStorage");if(method==null)return null;
            object?[] args={cannon,0,0,null};
            return method.Invoke(null,args) is int status?status==0?false:status==1?true:null:null;
        }
        catch(Exception){return null;}
    }
    private static bool ProvenPhantom(Cannon cannon,LoadTask task)
    {
        var proof=NoDebitProof.TryGetValue(task.Pointer,out var p)&&p.Type==task.Request.TypeID.Value&&
            CycleBaselines.TryGetValue(task.Pointer,out var cycle)&&cycle.Stock==p.Stock;
        return AmmoReloadPolicy.RemovePhantom(task.State==LoadState.Loaded,task.State==LoadState.Loading,Chamber.Has(task.Pointer),
            Chamber.IsPhantom(task.Pointer,Time.time),proof,proof&&PhysicalStock(cannon)==p.Stock,ExternalLoader(cannon));
    }

    /// <summary>One log line per empty event: the preferred type is out, and either another type takes over or the gun stays empty.</summary>
    private static void EmptyEvent(Cannon cannon,(IntPtr Vehicle,int Vuid) key,string preferred,bool preferredHasRounds,AmmoChoice choice)
    {
        if(preferredHasRounds){EmptyLogged.Remove(key);return;}
        var signature=preferred+">"+(choice.Type??"none");
        if(EmptyLogged.TryGetValue(key,out var seen)&&seen==signature)return;
        EmptyLogged[key]=signature;
        if(choice.Type==null)
            Plugin.ModLog.LogInfo($"[Ammo loadout] cannon {cannon.VUID.Value}: out of ammunition ({Label(preferred)} is empty and no other carried type has rounds); the gun stays empty and cannot fire.");
        else
            Plugin.ModLog.LogInfo($"[Ammo loadout] cannon {cannon.VUID.Value}: {Label(preferred)} is empty; next load {Label(choice.Type)}, a normal reload (no shot before that load completes).");
    }

    /// <summary>A loaded round without any debit: empty the chamber so that a real load starts (no refund: the round never came from the stock).</summary>
    private static void RemovePhantom(Cannon cannon,LoadTask task)
    {
        Plugin.ModLog.LogWarning($"[Ammo loadout] cannon {cannon.VUID.Value}: phantom removed: completed native request had no debit, physical typed stock is unchanged since the shot/empty cycle, no external autoloader owns the load.");
        task.rack=null;task.currentRequest=LoadRequest.Consumed;
        task.SetState(LoadState.Unloaded);
        Chamber.Spend(task.Pointer);
        NoDebitProof.Remove(task.Pointer);
    }

    /// <summary>The gun may report CanFire only with a real round whose load has had its time.</summary>
    [HarmonyPostfix,HarmonyPatch(typeof(WeaponBehaviour),"get_CanFire"),HarmonyPriority(Priority.Last)]
    private static void FireGuard(WeaponBehaviour __instance,ref bool __result)
    {
        if(!__result||!Ready||!Enabled)return;
        try
        {
            var cannon=__instance.TryCast<CannonBehaviour>()?.mount?.TryCast<Cannon>();
            if(cannon==null||!HasActiveMix(cannon))return;
            var task=__instance.LoadTask?.TryCast<LoadTask>();
            if(task==null)return;
            var verdict=Chamber.Check(task.Pointer,Time.time,PhaseSeconds(task),out var needed,out var elapsed);
            if(verdict==FireVerdict.Allowed)return;
            __result=false;
            if(!Chamber.FirstTime(task.Pointer,verdict))return;
            Plugin.ModLog.LogInfo(verdict==FireVerdict.NoRound
                ?$"[Ammo loadout] cannon {cannon.VUID.Value}: shot blocked, no verified chamber debit; an unconfirmed native/external load is preserved."
                :$"[Ammo loadout] cannon {cannon.VUID.Value}: shot blocked, the round was loaded {elapsed:0.00} s ago and its load needs {needed:0.00} s.");
        }
        catch(Exception ex){Report("Fire guard: "+ex.Message);}
    }
    /// <summary>The shot was fired: the credit of that round is used up, the next shot needs a new load.</summary>
    [HarmonyPrefix,HarmonyPatch(typeof(CannonBehaviour),nameof(CannonBehaviour.FireInternal))]
    private static void Firing(CannonBehaviour __instance,out (IntPtr Task,long Token) __state)
    {
        __state=default;
        try{if(__instance.LoadTask?.TryCast<LoadTask>() is {} task){__state=(task.Pointer,Chamber.Token(task.Pointer));CaptureCycle(task);}}
        catch(Exception ex){Report("Fire ledger: "+ex.Message);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(CannonBehaviour),nameof(CannonBehaviour.FireInternal))]
    private static void Fired((IntPtr Task,long Token) __state)
    {
        try{Chamber.Spend(__state.Task,__state.Token);}
        catch(Exception ex){Report("Fire ledger: "+ex.Message);}
    }
    // Unload and Cancel refund the occupied round (once, see RefundLatch): that round is no longer in the gun.
    [HarmonyPrefix,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.Unload)),HarmonyPriority(Priority.First+400)]
    private static void SpendOnUnload(LoadTask __instance){Chamber.Spend(__instance.Pointer);NoDebitProof.Remove(__instance.Pointer);CycleBaselines.Remove(__instance.Pointer);}
    [HarmonyPrefix,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.Cancel)),HarmonyPriority(Priority.First+400)]
    private static void SpendOnCancel(LoadTask __instance){Chamber.Spend(__instance.Pointer);NoDebitProof.Remove(__instance.Pointer);CycleBaselines.Remove(__instance.Pointer);}
}
