using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.TurretBaskets;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Colliders;
using Sprocket.Vehicles.Events.Flameouts;
using Sprocket.Vehicles.DamageModels;
using Sprocket.DamageModelling;
using UnityEngine;

namespace SprocketCarouselAutoloader;

// Native hit objects belong to the real basket. The only ammunition object is its existing
// finite AmmoRackBehaviour; the existing native vehicle module owns damage and all effects.
internal static class CarouselNativeDamage
{
    internal static bool Available;
    private static IEnumerable<T> Items<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> list)
    {
        var count=list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;
        for(int i=0;i<count;i++) yield return list[i];
    }
    private sealed class Binding
    {
        internal readonly BasketState State;
        internal readonly VehicleHealthRegister Register;
        internal readonly IDurable Pool;
        internal HealthChange? HealthChanged;
        internal AmmoRackBehaviour? Rack;
        internal FlameoutControlVehicleModule? Module;
        internal Il2CppSystem.EventHandler<FlameoutEventArgs>? Ignition;
        internal Binding(BasketState state, VehicleHealthRegister register, IDurable pool)
        { State=state; Register=register; Pool=pool; }
    }
    private static readonly Dictionary<IntPtr, Binding> Bindings=new();
    private static int warnings;
    private static int initTraces,meshTraces,hitTraces;
    private static void TraceInit(string message) { if(initTraces++<8) Plugin.ModLog.LogInfo("[Carousel damage init] "+message); }
    private static void TraceMesh(string message) { if(meshTraces++<8) Plugin.ModLog.LogInfo("[Carousel damage mesh] "+message); }
    private static void TraceHit(string message) { if(hitTraces++<8) Plugin.ModLog.LogInfo("[Carousel damage hit] "+message); }
    private static void Guard(Action action)
    {
        try { action(); }
        catch(Exception ex) { if(warnings++<8) Plugin.ModLog.LogWarning($"[Carousel damage] {ex.Message}"); }
    }
    internal static void CheckHooks(string owner)
    {
        foreach(var name in new[]{"CreateHealthPoolInternal","CreateHealthPool","Release"})
            if(AccessTools.Method(typeof(VehicleHealthRegister),name)==null)
                throw new MissingMethodException(typeof(VehicleHealthRegister).FullName,name);
        var methods=new[]{AccessTools.Method(typeof(VehicleHealthRegister),"CreateHealthPoolInternal"),
            AccessTools.Method(typeof(VehicleHealthRegister),"CreateHealthPool"),
            AccessTools.Method(typeof(VehicleDamageModelGeneration),nameof(VehicleDamageModelGeneration.CreateStructures)),
            AccessTools.Method(typeof(Vehicle),nameof(Vehicle.EnableBehaviour)),
            AccessTools.Method(typeof(Vehicle),nameof(Vehicle.DisableBehaviour)),
            AccessTools.Method(typeof(VehicleBehaviour),nameof(VehicleBehaviour.AddModule))};
        foreach(var method in methods)
            if(Harmony.GetPatchInfo(method)?.Owners.Contains(owner)!=true)
                throw new InvalidOperationException("Native carousel damage hook not installed: "+method);
    }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleHealthRegister), "CreateHealthPoolInternal")]
    private static void PoolReady(VehicleHealthRegister __instance, VehicleComponent __0, IDurable __result)
    {
        try
        {
            var basket=__0.TryCast<TurretBasket>();
            if(basket!=null && __result!=null) Guard(()=>AttachPool(__instance,basket,__result));
        }
        catch(Exception ex) { HookGuard.Warn("VehicleHealthRegister.CreateHealthPoolInternal", HookGuard.Vuid(__0), ex); }
    }
    // Current native CreateHealthPool inlines its Internal method. Inspect exact registered
    // owner/pool pairs after either entrypoint; never resolve a basket by proximity or guessed ID.
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleHealthRegister), "CreateHealthPool")]
    private static void PublicPoolReady(VehicleHealthRegister __instance, int __0, IDurable __result)
    {
        try { if(__result==null) return; }
        catch(Exception ex) { HookGuard.Warn("VehicleHealthRegister.CreateHealthPool", __0, ex); return; }
        Guard(()=>
        {
            TurretBasket? basket=null;
            foreach(var entry in __instance.entries)
            {
                if(entry.HealthPool?.Pointer!=__result.Pointer) continue;
                var owner=entry.Owner?.TryCast<TurretBasket>();
                if(owner==null || owner.VUID.Value!=__0) continue;
                if(basket!=null && basket.Pointer!=owner.Pointer) return;
                basket=owner;
            }
            if(basket!=null) AttachPool(__instance,basket,__result);
        });
    }
    private static void AttachPool(VehicleHealthRegister register,TurretBasket basket,IDurable pool)
    {
        var state=CarouselRuntime.State(basket);
        if(!state.Active) return;
        if(Bindings.TryGetValue(basket.Pointer,out var previous) && previous.Pool.Pointer==pool.Pointer &&
            previous.Register.Pointer==register.Pointer) return;
        Forget(basket.Pointer);
        var binding=new Binding(state,register,pool);
        binding.HealthChanged=(Action<IDurable,HealthChangeInfo>)((source,change)=>Guard(()=>Damage(binding,source,change)));
        Bindings[basket.Pointer]=binding;
        pool.add_HealthChanged(binding.HealthChanged);
        BindRack(state);
    }
    internal static void BindRack(BasketState state)
    {
        if(!Available || !Bindings.TryGetValue(state.Basket.Pointer,out var binding)) return;
        Guard(()=>
        {
            var module=FindModule(state.Basket);
            if(binding.Rack?.Pointer==state.Rack?.Pointer && binding.Module?.Pointer==module?.Pointer) return;
            UnbindRack(binding);
            if(state.Rack==null || module==null) return;
            binding.Rack=state.Rack; binding.Module=module;
            binding.Ignition=DelegateSupport.ConvertDelegate<Il2CppSystem.EventHandler<FlameoutEventArgs>>(
                (Action<Il2CppSystem.Object,FlameoutEventArgs>)((sender,args)=>Guard(()=>Ignite(binding,sender,args))));
            binding.Rack.add_FlameoutStarted(binding.Ignition);
        });
    }
    private static FlameoutControlVehicleModule? FindModule(TurretBasket basket)
    {
        var behaviour=basket.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>();
        var modules=behaviour?.Modules;
        if(modules==null) return null;
        var count=modules.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleModule>>().Count;
        FlameoutControlVehicleModule? result=null;
        for(int i=0;i<count;i++)
        {
            var current=modules[i].TryCast<FlameoutControlVehicleModule>();
            if(current==null) continue;
            if(result!=null) return null; // Ambiguous native owner: never route into two simulations.
            result=current;
        }
        return result;
    }
    private static void Damage(Binding binding, IDurable pool, HealthChangeInfo change)
    {
        if(!Bindings.TryGetValue(binding.State.Basket.Pointer,out var current) || current!=binding ||
            pool.Pointer!=binding.Pool.Pointer || !binding.State.Active || !binding.State.Basket.IsInstalled) return;
        BindRack(binding.State);
        var rack=binding.Rack;
        if(rack==null || binding.Module==null || rack.Pointer!=binding.State.Rack?.Pointer)
        { if(hitTraces<8) TraceHit($"basket={binding.State.Basket.VUID.Value} delta={change.Delta} skipped: rack={rack!=null} module={binding.Module!=null}"); return; }
        var max=pool.HealthInfo.Max;
        var damage=-change.Delta;
        if(hitTraces<8) TraceHit($"basket={binding.State.Basket.VUID.Value} damage={damage} max={max} remaining={rack.AmountStored} ignited={binding.State.Damage.Ignited}");
        if(!binding.State.Damage.ShouldApply(rack.AmountStored,max,damage)) return;
        // Native threshold, ammunition encoding and remaining-propellant calculation retained.
        rack.ApplyDamage(max,damage);
    }
    private static void Ignite(Binding binding, Il2CppSystem.Object sender, FlameoutEventArgs args)
    {
        if(!Bindings.TryGetValue(binding.State.Basket.Pointer,out var current) || current!=binding ||
            binding.Rack==null || binding.Module==null || binding.Rack.Pointer!=binding.State.Rack?.Pointer ||
            !binding.State.Damage.CommitIgnition(binding.Rack.AmountStored)) return;
        var remaining=binding.Rack.AmountStored;
        // Commit before the native module applies any recursive thermal/pressure damage.
        // Mixed ClearStorage clears the compartments and projects their sum by
        // native TakeShell calls under its projection guard. The following loop
        // remains the legacy fallback; a mixed carrier is already drained here.
        AmmoLoadoutBridge.Destroy(binding.Rack);
        for(int i=0;i<remaining;i++) if(!binding.Rack.TakeShell()) break;
        binding.Module.OnFlameoutStart(sender,args);
        Plugin.ModLog.LogInfo($"[Carousel damage] Basket {binding.State.Basket.VUID.Value}: native cookoff, {remaining} remaining rounds consumed.");
    }
    internal static void RackInvalidated(BasketState state)
    { if(Bindings.TryGetValue(state.Basket.Pointer,out var binding)) Guard(()=>UnbindRack(binding)); }
    private static void UnbindRack(Binding binding)
    {
        var rack=binding.Rack; var callback=binding.Ignition;
        binding.Rack=null; binding.Module=null; binding.Ignition=null;
        if(rack!=null && callback!=null) rack.remove_FlameoutStarted(callback);
    }
    private static void Forget(IntPtr basket)
    {
        if(!Bindings.Remove(basket,out var binding)) return;
        Guard(()=>UnbindRack(binding));
        if(binding.HealthChanged!=null) Guard(()=>binding.Pool.remove_HealthChanged(binding.HealthChanged));
        binding.HealthChanged=null;
    }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleBehaviour),nameof(VehicleBehaviour.AddModule))]
    private static void ModuleReady(VehicleModule __0)
    {
        try
        {
            if(__0.TryCast<FlameoutControlVehicleModule>()==null) return;
            foreach(var binding in Bindings.Values.ToArray()) BindRack(binding.State);
        }
        catch(Exception ex) { HookGuard.Warn("VehicleBehaviour.AddModule", -1, ex); }
    }
    [HarmonyPrefix, HarmonyPatch(typeof(FlameoutControlVehicleModule),nameof(FlameoutControlVehicleModule.Release))]
    private static void ModuleReleased(FlameoutControlVehicleModule __instance)
    {
        try
        {
            foreach(var binding in Bindings.Values.ToArray())
                if(binding.Module?.Pointer==__instance.Pointer) Guard(()=>UnbindRack(binding));
        }
        catch(Exception ex) { HookGuard.Warn("FlameoutControlVehicleModule.Release", -1, ex); }
    }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.Release))]
    private static void RegisterReleased(VehicleHealthRegister __instance)
    {
        try
        {
            foreach(var binding in Bindings.Values.ToArray())
                if(binding.Register.Pointer==__instance.Pointer) Forget(binding.State.Basket.Pointer);
        }
        catch(Exception ex) { HookGuard.Warn("VehicleHealthRegister.Release", -1, ex); }
    }
    // Vehicle.EnableBehaviour inlines VehicleObject.EnablePhysics in the current game.
    // Reconcile after the real entrypoint returns: native weapon supply and flameout modules
    // have now assembled; the returned behaviour supplies the exact root health register.
    [HarmonyPostfix, HarmonyPatch(typeof(Vehicle),nameof(Vehicle.EnableBehaviour))]
    private static void VehicleEnabled(Vehicle __instance,IVehicleBehaviour __result)
    {
        Guard(()=>
        {
            var register=__result?.TryCast<VehicleBehaviour>()?.healthRegister;
            if(register==null) { TraceInit("native root returned without health register"); return; }
            int configured=0;
            foreach(var vehicleObject in Items(__instance.ObjectReader.Items))
            foreach(var component in Items(vehicleObject.Components))
            {
                var basket=component.TryCast<TurretBasket>();
                if(basket==null || !basket.IsInstalled || !CarouselRuntime.State(basket).Enabled) continue;
                configured++;
                IDurable? existing=null;
                foreach(var entry in register.entries)
                    if(entry.Owner?.Pointer==basket.Pointer) { existing=entry.HealthPool; break; }
                var pool=existing ?? register.CreateHealthPool(basket.VUID.Value,100f);
                if(existing==null) pool.DamageModifiers=new DamageModifiers(2f,.25f,0f,.1f);
                AttachPool(register,basket,pool);
                var state=CarouselRuntime.State(basket);
                TraceInit($"basket={basket.VUID.Value} pool={pool.Pointer} max={pool.HealthInfo.Max} stock={state.Rack?.AmountStored.ToString() ?? "uninitialized"} module={FindModule(basket)!=null} existing={existing!=null}");
            }
            if(configured==0) TraceInit("native root enabled; no configured installed carousel");
        });
    }
    [HarmonyPrefix, HarmonyPatch(typeof(Vehicle),nameof(Vehicle.DisableBehaviour))]
    private static void VehicleDisabled(Vehicle __instance)
    {
        Guard(()=>
        {
            foreach(var vehicleObject in Items(__instance.ObjectReader.Items))
            foreach(var component in Items(vehicleObject.Components))
            { var basket=component.TryCast<TurretBasket>(); if(basket!=null) Forget(basket.Pointer); }
        });
    }
    // 0.2.28 (M3): part of the merged TurretBasket.Release prefix (CarouselRuntime.Released), only while this group is patched.
    internal static void BasketReleased(TurretBasket __instance) => Forget(__instance.Pointer);

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleDamageModelGeneration),nameof(VehicleDamageModelGeneration.CreateStructures))]
    private static void StructuresReady(IDamageModelConstructor __0,Il2CppSystem.Collections.Generic.IReadOnlyList<VehicleObject> __1)
    {
        Guard(()=>
        {
            var builder=new VehicleDamageModelGeneration.DamageModelStructureBuilder(__0).Cast<IVehicleComponentDamageModelBuilder>();
            foreach(var vehicleObject in Items(__1))
            {
                var registered=__0.EntityIsRegistered(vehicleObject.EntityID);
                foreach(var component in Items(vehicleObject.Components))
                {
                    var basket=component.TryCast<TurretBasket>();
                    if(basket==null || !CarouselRuntime.State(basket).Enabled) continue;
                    if(!registered) { TraceMesh($"basket={basket.VUID.Value} skipped: native entity {vehicleObject.EntityID} not registered"); continue; }
                    AddDamageGeometry(basket,builder);
                }
            }
        });
    }
    private static void AddDamageGeometry(TurretBasket basket,IVehicleComponentDamageModelBuilder builder)
    {
        Guard(()=>
        {
            var state=CarouselRuntime.State(basket);
            if(!state.Active || !basket.IsInstalled) return;
            var cannon=CarouselRuntime.Cannons(basket).FirstOrDefault(c=>c.VUID.Value==state.CannonId);
            var generator=basket.generator?.TryCast<BasketGenerator>();
            if(cannon==null || generator==null) { TraceMesh($"basket={basket.VUID.Value} skipped: cannon={cannon!=null} generator={generator!=null}"); return; }
            var size=CarouselRuntime.Calculate(basket,cannon);
            if(size.Capacity<=0) { TraceMesh($"basket={basket.VUID.Value} skipped: capacity0 {size.Reason}"); return; }
            var scale=basket.Scale;
            if(Math.Abs(scale.x)<.0001f || Math.Abs(scale.y)<.0001f || Math.Abs(scale.z)<.0001f) return;
            var geometry=state.Layout==CarouselLayout.StackedUnitary
                ? StackedCarouselGeometry.Build(size.DiameterMm/1000,size.DepthMm/1000,size.Capacity,size.SlotsPerTier,
                    cannon.ShellBlueprint.Diameter/1000.0,size.StoredProjectileMm/1000,showLift:false)
                : CarouselVisualGeometry.Build(size.DiameterMm/1000,size.DepthMm/1000,size.Capacity,
                    cannon.ShellBlueprint.Diameter/1000.0,size.StoredProjectileMm/1000,size.StoredChargeMm/1000,
                    state.Layout==CarouselLayout.VerticalCharge);
            var bounds=generator.LocalBounds;
            var matrix=generator.transform.localToWorldMatrix*Matrix4x4.TRS(
                new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),Quaternion.identity,
                new Vector3(1/Math.Abs(scale.x),1/Math.Abs(scale.y),1/Math.Abs(scale.z)));
            // Same native rack parameters: RHA factor .1, density 3000, no spall, part priority 192.
            var parameters=new DamageModelParameters(.1f,0f,3000f,192);
            var ammo=geometry.Where(g=>g.SlotPrefixIndexCounts.Length==size.Capacity+1).ToArray();
            var vertices=new List<Vector3>(); var indices=new List<int>();
            foreach(var mesh in ammo)
            {
                var offset=vertices.Count;
                vertices.AddRange(mesh.Vertices.Select(v=>new Vector3((float)v.X,(float)v.Y,(float)v.Z)));
                indices.AddRange(mesh.Triangles.Select(i=>i+offset));
            }
            if(indices.Count==0) { TraceMesh($"basket={basket.VUID.Value} skipped: empty generated ammunition"); return; }
            // One native damage object, preserving the actual central gap and individual rounds.
            // Several boxes could register repeated damage to the same native health pool.
            builder.AddMesh(basket.VUID,ref matrix,basket.VehicleObject.EntityID,
                new Il2CppStructArray<Vector3>(vertices.ToArray()),new Il2CppStructArray<int>(indices.ToArray()),ref parameters);
            TraceMesh($"basket={basket.VUID.Value} entity={basket.VehicleObject.EntityID} triangles={indices.Count/3} slots={size.Capacity} native mesh registered");
        });
    }
}
