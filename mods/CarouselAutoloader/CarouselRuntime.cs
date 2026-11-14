using HarmonyLib;
using Sprocket;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using Sprocket.DamageModelling;
using Sprocket.Vehicles.CrewSystems;
using UnityEngine;

namespace SprocketCarouselAutoloader;

internal sealed class BasketState
{
    internal readonly TurretBasket Basket;
    internal bool Enabled;
    internal int CannonId = -1;
    internal CarouselLayout Layout;
    internal AmmoRackBehaviour? Rack;
    internal bool MixedStorage;
    internal readonly CarouselDamagePolicy Damage=new();
    internal CarouselSize Size;
    internal int ShellId = -1;
    internal string LastPreview = "";
    internal IProjectileTypeRegister? Register;
    internal Il2CppSystem.Guid RegisteredGuid;
    internal bool HasRegistration;
    // Date gate is separate from the saved Enabled flag: an imported too-early
    // carousel keeps its identity and setting but supplies no ammunition.
    internal bool DateAllowed = true;
    internal int DateKey = int.MinValue;
    internal CarouselLayout DateLayout = (CarouselLayout)(-1);
    internal DateTime? VehicleDate;
    internal bool Active => Enabled && CarouselRuntime.Allowed(this);
    internal BasketState(TurretBasket basket) => Basket = basket;
}

internal static class CarouselRuntime
{
    private static int Count<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> list) =>
        list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;
    private static readonly Dictionary<IntPtr, BasketState> States = new();
    private static readonly Dictionary<IntPtr, BasketState> Racks = new();
    private static readonly HashSet<string> Warnings = new();
    private sealed record AutoLoaderBinding(Cannon Cannon, LoadTask Task, LoadContributeTask Contributor);
    private static readonly Dictionary<IntPtr, AutoLoaderBinding> AutoLoaders = new();
    private static readonly LoaderFitFailureGate fitFailureGate=new();
    private static bool fitFailureLogged;
    internal static BasketState State(TurretBasket basket)
    {
        if (!States.TryGetValue(basket.Pointer, out var state)) States[basket.Pointer] = state = new(basket);
        return state;
    }
    internal static DateTime? Date(VehicleComponent? component)
    {
        try
        {
            var value = component?.Vehicle?.DesignInfo?.Date;
            if (value is not { Valid: true } date) return null;
            return date.Year == 0 ? DateTime.MinValue : new DateTime(date.Year, date.Month, date.Day);
        }
        catch { return null; }
    }
    internal static bool Allowed(BasketState state)
    {
        try
        {
            var date = Date(state.Basket);
            var layout = state.Layout;
            int key = date is { } d ? d.Year * 10000 + d.Month * 100 + d.Day : int.MinValue;
            if (state.DateKey == key && state.DateLayout == layout) return state.DateAllowed;
            state.DateKey = key; state.DateLayout = layout; state.VehicleDate = date; state.DateAllowed = true;
            if (date is { } vehicle)
            {
                var from = CarouselDatePolicy.ForLayout(layout);
                state.DateAllowed = CarouselDatePolicy.Allows(vehicle, from);
                if (!state.DateAllowed && Warnings.Add("date " + state.Basket.Pointer + " " + key + " " + (int)layout))
                    Plugin.ModLog.LogWarning(
                        $"[Carousel] {layout} carousel needs {from:yyyy-MM-dd}; vehicle date is " +
                        $"{vehicle:yyyy-MM-dd}. Saved layout retained, native manual loading used.");
            }
            return state.DateAllowed;
        }
        catch (Exception ex)
        {
            if (Warnings.Add("date check " + state.Basket.Pointer))
                Plugin.ModLog.LogWarning("[Carousel] Date check unavailable; carousel disabled: " + ex.Message);
            return false;
        }
    }
    // 0.2.28: the same error is logged once per operation (also on the per-frame paths), never per frame.
    private static readonly WarnOnce GuardErrors = new();
    internal static void Guard(string operation, Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            try { if (GuardErrors.First(operation + "|" + ex.GetType().FullName + "|" + ex.Message, 0)) Plugin.ModLog.LogError($"[Carousel] {operation} (logged once per error): {ex}"); }
            catch { /* Logging never changes the game. */ }
        }
    }
    // 0.2.28 (M5): live Enabled box of a basket without creating its state (early exit of the per-frame hooks).
    internal static bool EnabledBasket(TurretBasket basket) => States.TryGetValue(basket.Pointer, out var state) && state.Enabled;
    // 0.2.28 (H3 "Alleen bewaken"): writes into the native contributor lists, restored and switched off per cannon on failure.
    private static readonly InterventionSwitch ContributorSwitch = new("Automatic loader contributor write");
    internal static void ForgetPointers() => ContributorSwitch.ForgetPointers();
    internal static IEnumerable<TurretBasket> Baskets(Cannon cannon)
    {
        foreach (var basket in VehicleLoadIndex.Get(cannon).Baskets)
            if (basket.IsInstalled) yield return basket;
    }
    private static VehicleObject? Turret(VehicleComponent component)
    {
        var t = component.VehicleTransform;
        // A turret ring/basket is on the owning turret object. Stop at the nearest basket.
        for (int depth = 0; t != null && depth < 128; depth++, t = t.Parent)
        {
            var parts = t.VehicleObject.Components;
            for (int i = 0; i < Count(parts); i++)
                if (parts[i].TryCast<TurretBasket>() != null) return t.VehicleObject;
        }
        return null;
    }
    internal static bool SameTurret(VehicleComponent basket, Cannon cannon)
    {
        var a = Turret(basket); var b = Turret(cannon);
        return a != null && b != null && a.Pointer == b.Pointer;
    }
    internal static List<Cannon> Cannons(VehicleComponent basket)
    {
        var result = new List<Cannon>();
        var objects = basket.Vehicle.ObjectReader.Items;
        for (int i = 0; i < Count(objects); i++)
        {
            var parts = objects[i].Components;
            for (int j = 0; j < Count(parts); j++)
            {
                var cannon = parts[j].TryCast<Cannon>();
                if (cannon != null && cannon.IsInstalled && SameTurret(basket, cannon)) result.Add(cannon);
            }
        }
        return result.OrderBy(c => c.VUID.Value).ToList();
    }
    internal static CarouselSize Calculate(TurretBasket basket, Cannon cannon,LoaderShellSize? explicitSize=null)
    {
        var bp = basket.BlueprintSlot.Blueprint;
        var scale = basket.Scale;
        var segments = new List<Segment>();
        if (bp?.Segments != null)
            foreach (var segment in bp.Segments)
                segments.Add(new(segment.Diameter, segment.Depth));
        segments = CarouselGeometry.ResolveBasket(segments, basket.ringDiameter)
            .Select(s => new Segment(s.DiameterMm * Math.Min(Math.Abs(scale.x), Math.Abs(scale.z)), s.DepthMm * Math.Abs(scale.y))).ToList();
        var shell = cannon.shellBlueprintSlot?.Blueprint;
        if(shell==null) return new(0,0,0,0,0,0,"Ammunition blueprint is not ready");
        var physical=explicitSize??ShellLoaderGeometry.Read(cannon);
        if(!physical.Valid) return new(0,0,0,0,0,0,"Custom shell geometry is unavailable or invalid");
        return CarouselGeometry.Calculate(segments,Math.Max(shell.Diameter,physical.DiameterMm),
            physical.FullLengthMm,shell.Mass,State(basket).Layout,
            physical.Status==1?physical.ProjectileMm:null,physical.Status==1?physical.ChargeMm:null);
    }
    internal static void Invalidate(BasketState state)
    {
        CarouselNativeDamage.RackInvalidated(state);
        if (state.Rack != null) Racks.Remove(state.Rack.Pointer);
        foreach(var pointer in Racks.Where(p=>ReferenceEquals(p.Value,state)).Select(p=>p.Key).ToArray())Racks.Remove(pointer);
        state.Rack = null;
        state.ShellId = -1;
        state.MixedStorage=false;
    }
    internal static AmmoRackBehaviour? EnsureMixedStorage(BasketState state,Cannon cannon,CarouselSize size)
    {
        if(!AmmoLoadoutBridge.HasLoadout(cannon)||state.Damage.Ignited||size.Capacity<=0)return null;
        if(state.Rack!=null)
        {
            // A live legacy supply must never become a fresh mixed magazine halfway through Play.
            if(!state.MixedStorage)return null;
            state.Rack.Capacity=(ushort)size.Capacity;
            return state.Rack;
        }
        var task=cannon.Behaviour?.LoadTask?.TryCast<LoadTask>();
        if(task==null)return null;
        var request=task.Request;
        if(!InitializeRequest(task,cannon,state,ref request))return null;
        state.Rack=new AmmoRackBehaviour($"Carousel {state.Basket.VUID.Value}",request.TypeID,request.SizeID,size.Capacity);
        // Empty by construction: the shared carried-loadout owner allocates the only finite stock.
        state.MixedStorage=true;state.Size=size;state.ShellId=cannon.ShellSizeBlueprintID;
        Racks[state.Rack.Pointer]=state;
        CarouselNativeDamage.BindRack(state);
        return state.Rack;
    }
    private static void ReleaseRegistration(BasketState state)
    {
        if (state.HasRegistration && state.Register != null) state.Register.RemoveUser(state.RegisteredGuid);
        state.HasRegistration = false;
        state.Register = null;
    }
    private static bool InitializeRequest(LoadTask task, Cannon cannon, BasketState state, ref AmmoRequest request)
    {
        if (request.TypeID.Value != ProjectileTypeID.Invalid.Value && request.SizeID.Caliber > 0) return true;
        // Existing rounds retain their actual native identity. An incomplete
        // loading/loaded request cannot be inferred from the selected profile.
        if(task.State==LoadState.Loading||task.State==LoadState.Loaded)return false;
        if (Calculate(state.Basket, cannon).Capacity == 0) return false;
        var shell = cannon.ShellBlueprint;
        var types = shell.GeneratedProjectileBlueprint;
        var register = IProjectileTypeRegister.Instance;
        if (register == null || types == null || Count(types) == 0) return false;
        var projectile = types[0]; // Native AP base; the cannon's Expanded Ballistics shell profile overrides firing as usual.
        var guid = projectile.ProjectileTypeGuid;
        if (!state.HasRegistration || state.Register?.Pointer != register.Pointer || !state.RegisteredGuid.Equals(guid))
        {
            ReleaseRegistration(state);
            if (!register.IsRegistered(guid))
                register.Create($"{shell.Diameter} mm carousel", guid, MathF.Pow(shell.Diameter, 3) * 1.58999992e-5f,
                    shell.Diameter * .001f, shell.Diameter * .003f, 1f, 10f, projectile.FunctionDefinitions);
            register.AddUser(guid);
            state.Register = register;
            state.RegisteredGuid = guid;
            state.HasRegistration = true;
        }
        request.TypeID = register.GetID(guid); // Only after verified registration, never a speculative GUID lookup.
        request.SizeID = ProjectileSizeID.FromPropellantLength(shell.Diameter, shell.PropellantLength);
        request.Amount = 1;
        request.Destination = cannon.LoadPosition;
        task.Request = request; // Write the plain struct back to the native task.
        if (task.State == LoadState.NeverLoaded) task.currentRequest = LoadRequest.Unused;
        Plugin.ModLog.LogDebug($"[Carousel] Initialized rack-independent request: cannon {cannon.VUID.Value}, type {request.TypeID.Value}, caliber {request.SizeID.Caliber}, propellant {request.SizeID.PropellantLength}");
        return true;
    }
    // Native constructor detouring is unsupported; existing Cannon.EnableBehaviour/assembly
    // and LoadTask.Update bootstrap paths initialize supply without a managed-only fallback.
    private static AmmoRackBehaviour? Supply(BasketState state, Cannon cannon, ProjectileSizeID actualSize, ProjectileTypeID type)
    {
        var size = Calculate(state.Basket, cannon);
        state.Size = size;
        if (state.Damage.Ignited || size.Capacity == 0 || !state.Active || !state.Basket.IsInstalled || state.Basket.HealthFraction <= 0) return null;
        // The native request is authoritative in combat. Never accept a differently sized round.
        if (actualSize.Caliber != cannon.ShellBlueprint.Diameter ||
            actualSize.PropellantLength != cannon.ShellBlueprint.PropellantLength) return null;
        if (type.Value == ProjectileTypeID.Invalid.Value) return null;
        if(AmmoLoadoutBridge.HasLoadout(cannon))
        {
            var aggregate=EnsureMixedStorage(state,cannon,size);
            if(aggregate==null)return null;
            var result=AmmoLoadoutBridge.Supply(cannon,aggregate,type.Value,out var view);
            if(result!=1||view==null)return null;
            Racks[view.Pointer]=state;
            return view;
        }
        if (state.Rack != null && state.Rack.ShellSizeID.Value == actualSize.Value && state.Rack.StoredTypeID.Value == type.Value)
        {
            // Rebuilding or shrinking during play must never manufacture replacement rounds.
            state.Rack.Capacity = (ushort)size.Capacity;
            if (state.Rack.AmountStored > size.Capacity) state.Rack.amountStored = (ushort)size.Capacity;
            CarouselNativeDamage.BindRack(state);
            return state.Rack;
        }
        // The cannon's native load request already contains the registered runtime type.
        // Blueprint GUIDs can be stale after registration/profile overrides. Do not resolve them again.
        // A type change cannot convert existing ammunition into another kind.
        // Mixed magazines use the explicit shared storage adapter above.
        if(state.Rack!=null)return null;
        Invalidate(state);
        state.Rack = new AmmoRackBehaviour($"Carousel {state.Basket.VUID.Value}", type,
            actualSize, size.Capacity);
        state.Rack.FullyLoad();
        state.ShellId = cannon.ShellSizeBlueprintID;
        Racks[state.Rack.Pointer] = state;
        CarouselNativeDamage.BindRack(state);
        Plugin.ModLog.LogDebug($"[Carousel] Basket {state.Basket.VUID.Value}, cannon {cannon.VUID.Value}: {size.Capacity} rounds, {size.MaxLengthMm:0} mm limit, {size.ReloadSeconds:0.0} s reload");
        return state.Rack;
    }
    internal static bool Mechanical(LoadTask task, out BasketState state)
    {
        state = null!;
        if(task.rack==null || !Racks.TryGetValue(task.rack.Pointer,out state!) || !state.Active ||
            !state.Basket.IsInstalled || state.Basket.HealthFraction<=0) return false;
        var cannon=task.target?.TryCast<WeaponBehaviour>()?.mount?.TryCast<Cannon>();
        if(cannon==null) return false;
        var source=task.rack.TryCast<AmmoRackBehaviour>();
        state.Size=Calculate(state.Basket,cannon,source==null?null:ShellLoaderGeometry.ReadForType(cannon,source.StoredTypeID.Value));
        return state.Size.Capacity>0;
    }
    private static bool ReplacesLoader(VehicleOperable operable)
    {
        // Loader roles belong to the breech, unlike gunner roles on the cannon.
        var breech = operable.Component?.TryCast<CannonBreech>();
        if (breech == null || breech.loaderOperable?.Pointer != operable.Pointer) return false;
        var objects = breech.Vehicle.ObjectReader.Items;
        for (int i = 0; i < Count(objects); i++)
        {
            var parts = objects[i].Components;
            for (int j = 0; j < Count(parts); j++)
            {
                var cannon = parts[j].TryCast<Cannon>();
                if (cannon == null || cannon.VUID.Value != breech.parentCannonVuid.Value || !cannon.IsInstalled) continue;
                if (BustleRuntime.HasDesign(cannon)) return true;
                foreach (var basket in Baskets(cannon))
                {
                    var state = State(basket);
                    if (state.Active && state.CannonId == cannon.VUID.Value && basket.HealthFraction > 0 && Calculate(basket, cannon).Capacity > 0)
                        return true;
                }
            }
        }
        return false;
    }
    [HarmonyPrefix, HarmonyPatch(typeof(AllOperablesValidCriterion), nameof(AllOperablesValidCriterion.Validate))]
    private static void ValidateAutomaticLoaders(AllOperablesValidCriterion __instance,
        out Il2CppSystem.Collections.Generic.IEnumerable<VehicleOperable>? __state)
    {
        __state = null;
        try
        {
            var original = __instance.operables;
            var kept = new Il2CppSystem.Collections.Generic.List<VehicleOperable>();
            // Copy before inspecting components: lazy component access can modify the source crew registry.
            var snapshot = Il2CppSystem.Linq.Enumerable.ToArray<VehicleOperable>(original);
            bool changed = false;
            foreach (var operable in snapshot)
            {
                if (ReplacesLoader(operable)) changed = true;
                else kept.Add(operable);
            }
            if (!changed) return;
            __state = original;
            __instance.operables = kept.Cast<Il2CppSystem.Collections.Generic.IEnumerable<VehicleOperable>>();
        }
        catch (Exception ex) { Plugin.ModLog.LogError($"[Carousel] Automatic-loader validation: {ex}"); }
    }
    [HarmonyPostfix, HarmonyPatch(typeof(Cannon), nameof(Cannon.EnableBehaviour))]
    private static void AttachNativeLoader(Cannon __instance)
    {
        Guard("Attach native automatic loader", () =>
        {
            MagazineRefill.UpdateCrewPoint(__instance);
            var weapon = __instance.Behaviour;
            var task = weapon?.LoadTask?.TryCast<LoadTask>();
            if (weapon == null || task == null) return;
            if (!Baskets(__instance).Any(b =>
            {
                var s = State(b);
                return s.Enabled && s.CannonId == __instance.VUID.Value && b.HealthFraction > 0 && Calculate(b, __instance).Capacity > 0;
            }) && !BustleRuntime.HasDesign(__instance)) return;
            SelectSupply(task);
            // 0.2.28 (H3): after a failed write this cannon keeps its native crew contributors for the session.
            if (ContributorSwitch.Off(__instance)) return;
            var exists = AutoLoaders.TryGetValue(__instance.Pointer, out var binding) && binding.Task.Pointer == task.Pointer;
            var contributor = exists ? binding!.Contributor : new LoadContributeTask { efficiency = 1f, commandEfficiency = 1f, operateCount = 1 };
            var previous = weapon.LoadTaskContributors;
            if (previous != null && previous.Any(c => c.Pointer == contributor.Pointer)) return;
            var written = false;
            try
            {
                var appended = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ILoadTaskContributor>((previous?.Length ?? 0) + 1);
                if (previous != null) for (int i = 0; i < previous.Length; i++) appended[i] = previous[i];
                appended[appended.Length - 1] = contributor.Cast<ILoadTaskContributor>();
                weapon.LoadTaskContributors = appended;
                written = true;
                AutoLoaders[__instance.Pointer] = new(__instance, task, contributor);
                loaderBindings = AutoLoaders.Values.ToArray();
            }
            catch (Exception ex)
            {
                // The weapon's own contributor array back as it was; no binding, so no synthetic loader is driven.
                if (written) { try { weapon.LoadTaskContributors = previous; } catch (Exception restore) { HookGuard.Warn("Contributor restore", HookGuard.Vuid(__instance), restore); } }
                if (AutoLoaders.Remove(__instance.Pointer)) loaderBindings = AutoLoaders.Values.ToArray();
                ContributorSwitch.Disable(__instance, ex);
                return;
            }
            Plugin.ModLog.LogDebug($"[Carousel] Native automatic loader attached to cannon {__instance.VUID.Value}; no crew-seat role required.");
        });
    }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleDesigner.AmmunitionStorage.WeaponLoadControllerAssemblyStage),
        nameof(VehicleDesigner.AmmunitionStorage.WeaponLoadControllerAssemblyStage.Execute))]
    private static void AttachBeforeControllerAssembly(VehicleDesigner.AmmunitionStorage.WeaponLoadControllerAssemblyStage __instance)
    {
        // Crew assembly may replace the breech contributor array after Cannon.EnableBehaviour.
        // The controller captures this array, so ensure our contributor is present before that capture.
        Guard("Assemble automatic loaders", () =>
        {
            foreach (var source in __instance.sources)
            {
                var cannon = source.TryCast<Cannon>();
                if (cannon != null) AttachNativeLoader(cannon);
            }
        });
    }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleWeaponLoadController), nameof(VehicleWeaponLoadController.Update))]
    private static void RefreshNativeLoaders(VehicleWeaponLoadController __instance)
    {
        try
        {
            // Native captures the contributor array during assembly. A valid feed
            // becoming available later must also reach that captured controller.
            // 0.2.28 (M5): only controllers of a vehicle with a Carousel autoloader (cached, no allocation for others).
            if (VehicleLoadIndex.HasLoader(__instance)) RefreshCapturedLoadersSafely(__instance);
            if (!loaderRefresh.Enter(Time.frameCount) || loaderBindings.Length == 0) return;
            Guard("Refresh native automatic loaders", () =>
            {
                foreach (var binding in loaderBindings)
                {
                    var task = binding.Task;
                    if (task.State != LoadState.Loading && task.State != LoadState.Loaded) SelectSupply(task);
                    var active = AutomaticSourceActive(task, binding.Cannon);
                    binding.Contributor.efficiency = active ? 1f : 0f;
                    binding.Contributor.commandEfficiency = active ? 1f : 0f;
                    binding.Contributor.operateCount = active ? (byte)1 : (byte)0;
                }
            });
        }
        catch (Exception ex) { HookGuard.Warn("VehicleWeaponLoadController.Update", -1, ex); }
    }
    private static readonly OncePerFrame loaderRefresh = new();
    private static AutoLoaderBinding[] loaderBindings = Array.Empty<AutoLoaderBinding>();
    private static void DetachNativeLoader(Cannon __instance)
    {
        lateBindAt.Remove(__instance.Pointer);
        loadTrace.Remove(__instance.Pointer);
        fitFailureGate.Forget(__instance.Pointer.ToInt64());
        if (AutoLoaders.Remove(__instance.Pointer)) loaderBindings = AutoLoaders.Values.ToArray();
    }
    // 0.2.28 (M3): the three Cannon.DisableBehaviour prefixes of 0.2.27 at normal priority in one hook, in their
    // registration order (core: this class, then MagazineRefill; then the audio group). The load-index
    // invalidation stays its own Priority.First prefix (VehicleLoadIndex). Each part has its own guard.
    [HarmonyPrefix, HarmonyPatch(typeof(Cannon), nameof(Cannon.DisableBehaviour))]
    private static void CannonDisabled(Cannon __instance)
    {
        try { DetachNativeLoader(__instance); }
        catch (Exception ex) { HookGuard.Warn("Cannon.DisableBehaviour loader", HookGuard.Vuid(__instance), ex); }
        try { MagazineRefill.ClearWaiting(__instance); }
        catch (Exception ex) { HookGuard.Warn("Cannon.DisableBehaviour magazine route", HookGuard.Vuid(__instance), ex); }
        try { AutoloaderAudio.Release(__instance); }
        catch (Exception ex) { HookGuard.Warn("Cannon.DisableBehaviour audio", HookGuard.Vuid(__instance), ex); }
    }
    [HarmonyFinalizer, HarmonyPatch(typeof(AllOperablesValidCriterion), nameof(AllOperablesValidCriterion.Validate))]
    private static void RestoreOperables(AllOperablesValidCriterion __instance,
        Il2CppSystem.Collections.Generic.IEnumerable<VehicleOperable>? __state)
    {
        try { if (__state != null) __instance.operables = __state; }
        catch (Exception ex) { HookGuard.Warn("AllOperablesValidCriterion.Validate restore", -1, ex); }
    }
    private static bool AutomaticSourceActive(LoadTask task, Cannon? cannon)
    {
        if (BustleRuntime.Active(task, cannon)) return true;
        if (Mechanical(task, out var state))
            return task.State == LoadState.Loading || task.State == LoadState.Loaded || state.Rack?.AmountStored > 0;
        // Physics activation can invalidate the supply cache after the initial chamber load.
        if (task.State != LoadState.Loaded || task.rack == null || cannon == null) return false;
        return Baskets(cannon).Any(basket =>
        {
            var configured = State(basket);
            return configured.Enabled && configured.CannonId == cannon.VUID.Value && basket.HealthFraction > 0 &&
                task.rack.RackIdentifier == $"Carousel {basket.VUID.Value}" && Calculate(basket, cannon).Capacity > 0;
        });
    }
    [HarmonyPrefix, HarmonyPatch(typeof(WeaponBehaviour), nameof(WeaponBehaviour.LoaderEnableFraction), MethodType.Getter)]
    private static bool AutomaticLoaderEnabled(WeaponBehaviour __instance, ref float __result)
    {
        try
        {
            var task = __instance.LoadTask?.TryCast<LoadTask>();
            if (task == null) return true;
            var cannon = __instance.mount?.TryCast<Cannon>();
            if (!AutomaticSourceActive(task, cannon))
            {
                // Native LoadContributeTask.EnableFraction is constant 1 even with efficiency 0.
                // Exclude the inactive synthetic task when reporting manual-loader availability.
                if (cannon == null || !AutoLoaders.TryGetValue(cannon.Pointer, out var binding)) return true;
                float total = 0f;
                int count = 0;
                var contributors = __instance.LoadTaskContributors;
                if (contributors != null) foreach (var contributor in contributors)
                {
                    if (contributor.Pointer == binding.Contributor.Pointer) continue;
                    total += contributor.EnableFraction;
                    count++;
                }
                __result = count == 0 ? 0f : total / count;
                return false;
            }
            __result = 1f;
            return false;
        }
        catch (Exception ex) { Plugin.ModLog.LogError($"[Carousel] Automatic loader enabled: {ex}"); return true; }
    }

    // RackPath is a boxed IL2CPP value type. Do not detour its by-ref output:
    // inject a native IAmmoSource reference before the native request selects a rack.
    [HarmonyPrefix, HarmonyPatch(typeof(LoadTask), nameof(LoadTask.UpdateRequest)),HarmonyAfter("sprocket.expandedballistics.ammoloadout")]
    private static bool SelectSupply(LoadTask __instance)
    {
        Cannon? cannon=null;
        try
        {
            // LoadInstantly uses Unused for initial chamber loading too. Only an
            // already-loaded Unused request is an unload/type-switch operation.
            if (__instance.State == LoadState.Loading ||
                (__instance.State == LoadState.Loaded && __instance.currentRequest == LoadRequest.Unused)) return true;
            // 0.2.28 (M5): no bustle-type rack and no enabled basket for this cannon: nothing to select (cached).
            if (!VehicleLoadIndex.HasLoader(__instance)) return true;
            cannon = __instance.target?.TryCast<WeaponBehaviour>()?.mount?.TryCast<Cannon>();
            if (cannon == null) return true;
            if(InvalidAssignedFeed(cannon)) return false;
            if (BustleRuntime.SelectSupply(__instance, cannon)) return true;
            var request = __instance.Request;
            request.Amount = 1;
            foreach (var basket in Baskets(cannon))
            {
                var state = State(basket);
                if (!state.Active || state.CannonId != cannon.VUID.Value) continue;
                if (!InitializeRequest(__instance, cannon, state, ref request))
                {if(AmmoLoadoutBridge.HasLoadout(cannon)){__instance.rack=null;return false;}continue;}
                var rack = Supply(state, cannon, request.SizeID, request.TypeID);
                if (rack == null || !request.IsMatch(rack.Cast<IAmmoSource>()))
                {
                    // A configured automatic ready magazine cannot be bypassed by a
                    // native reserve search when its requested compartment is empty.
                    if(AmmoLoadoutBridge.HasLoadout(cannon))
                    {__instance.rack=null;return false;}
                    continue;
                }
                var shell = cannon.ShellBlueprint;
                state.Size=Calculate(state.Basket,cannon,ShellLoaderGeometry.ReadForType(cannon,request.TypeID.Value));
                __instance.rack = rack.Cast<IAmmoSource>();
                __instance.currentLoadInfo = new ShellLoadInfo(shell.ProjectileMass, shell.PropellantMass,
                    (float)ShellLoaderGeometry.ReadForType(cannon,request.TypeID.Value).FullLengthMm / 1000);
                __instance.pickupTime = (float)state.Size.ReloadSeconds * .2f;
                __instance.totalDistance = (float)state.Size.ReloadSeconds * .2f;
                __instance.dropoffTime = (float)state.Size.ReloadSeconds * .4f;
                return true;
            }
        }
        catch (Exception ex)
        {
            HookGuard.Warn("LoadTask.UpdateRequest select supply", HookGuard.Vuid(cannon), ex);
            try { return cannon==null||(!AmmoLoadoutBridge.HasLoadout(cannon)&&fitFailureGate.AllowFallback(cannon.Pointer.ToInt64())); }
            catch { return true; }
        }
        return true;
    }
    internal static bool InvalidAssignedFeed(Cannon cannon)
    {
        var key=cannon.Pointer.ToInt64();
        var managed=ShellLoaderGeometry.Read(cannon).Status!=0;
        if(!managed) fitFailureGate.Observe(key,false,false);
        var assigned=false;
        try
        {
            foreach(var state in BustleRuntime.Racks(cannon))
            {
                if(!state.Active||state.CannonId!=cannon.VUID.Value) continue;
                // Record ownership before any native installation/packing query can throw.
                if(managed) fitFailureGate.Observe(key,true,true);
                if(!state.Rack.IsInstalled) continue;
                assigned=true;
                if(BustleRuntime.Capacity(state,cannon)==0) return true;
            }
            foreach(var basket in Baskets(cannon))
            {
                var state=State(basket);
                if(!state.Active||state.CannonId!=cannon.VUID.Value) continue;
                if(managed) fitFailureGate.Observe(key,true,true);
                if(!basket.IsInstalled) continue;
                assigned=true;
                if(Calculate(basket,cannon).Capacity==0) return true;
            }
            fitFailureGate.Observe(key,managed,assigned);
            return false;
        }
        catch(Exception ex)
        {
            if(!fitFailureLogged){fitFailureLogged=true;Plugin.ModLog.LogWarning($"[Autoloader] Physical fit query failed; known custom feeds remain blocked: {ex.Message}");}
            return !fitFailureGate.AllowFallback(key);
        }
    }
    // The native controller calls Update even with no crew contributors. Override only
    // tasks drawing a carousel round; fallback rack loading keeps the original crew speeds.
    [HarmonyPrefix, HarmonyPatch(typeof(LoadTask), nameof(LoadTask.Update))]
    private static bool Speed(LoadTask __instance, ref float __0, ref float __1, ref float __2)
    {
        Cannon? cannon=null;
        try
        {
            // 0.2.28 (M5): a cannon without a Carousel autoloader keeps its native speeds (cached, no allocation).
            if (!VehicleLoadIndex.HasLoader(__instance)) return true;
            cannon=__instance.target?.TryCast<WeaponBehaviour>()?.mount?.TryCast<Cannon>();
            if(cannon!=null&&__instance.State!=LoadState.Loaded&&InvalidAssignedFeed(cannon)) return false;
            // Native Update skips UpdateRequest when no request exists. Bootstrap the first one without ordinary racks.
            if (__instance.State == LoadState.NeverLoaded && __instance.Request.TypeID.Value == ProjectileTypeID.Invalid.Value)
                SelectSupply(__instance);
            if (!Mechanical(__instance, out var state))
            { BustleRuntime.ApplySpeed(__instance, ref __0, ref __1, ref __2); return true; }
            __0 = 1; __1 = 1; __2 = (float)state.Size.ReloadSeconds * .2f;
        }
        catch (Exception ex)
        {
            HookGuard.Warn("LoadTask.Update loading speeds", HookGuard.Vuid(cannon), ex);
            try { return cannon==null||(!AmmoLoadoutBridge.HasLoadout(cannon)&&fitFailureGate.AllowFallback(cannon.Pointer.ToInt64())); }
            catch { return true; }
        }
        return true;
    }
    private static void Timings(LoadTask __instance)
    {
        Guard("Loading phases", () =>
        {
            if (!Mechanical(__instance, out var state)) { BustleRuntime.ApplyTimings(__instance); return; }
            if (__instance.State != LoadState.Loading) return;
            var seconds = (float)state.Size.ReloadSeconds;
            __instance.pickupTime = seconds * .2f;
            __instance.totalDistance = seconds * .2f;
            __instance.dropoffTime = seconds * .4f;
        });
    }
    // 0.2.28 (M3): the two LoadTask.UpdateRequest postfixes of 0.2.27 in one hook, in registration order: loading
    // phases (core), then the carousel stock display (carousel-visuals group, only while that group is patched).
    [HarmonyPostfix, HarmonyPatch(typeof(LoadTask), nameof(LoadTask.UpdateRequest))]
    private static void RequestUpdated(LoadTask __instance)
    {
        try
        {
            // M5: neither part does anything for a cannon without a Carousel autoloader (cached, no allocation).
            if (!VehicleLoadIndex.HasLoader(__instance)) return;
        }
        catch (Exception ex) { HookGuard.Warn("LoadTask.UpdateRequest postfix", HookGuard.TaskVuid(__instance), ex); return; }
        Timings(__instance);
        if (CarouselVisuals.Available)
        {
            try { CarouselVisuals.SupplyReady(__instance); }
            catch (Exception ex) { HookGuard.Warn("LoadTask.UpdateRequest carousel visual", HookGuard.TaskVuid(__instance), ex); }
        }
    }
    // 0.2.28 (M3): Cannon.Build postfixes of CarouselRuntime and BustleRuntime in one hook, same order.
    [HarmonyPostfix, HarmonyPatch(typeof(Cannon), nameof(Cannon.Build))]
    private static void CannonBuilt(Cannon __instance)
    {
        CannonChanged(__instance);
        BustleRuntime.CannonChanged(__instance);
    }
    private static void CannonChanged(Cannon __instance)
    {
        Guard("Update basket capacity", () =>
        {
            foreach (var basket in Baskets(__instance))
            {
                var state = State(basket);
                if (state.Active && state.CannonId == __instance.VUID.Value)
                {
                    Mass(basket);
                    CarouselInspector.Redraw(basket);
                    var shell = __instance.shellBlueprintSlot?.Blueprint;
                    if (shell == null) continue;
                    var preview = $"cannon={__instance.VUID.Value}, shellBlueprint={__instance.ShellSizeBlueprintID}, caliber={shell.Diameter}, propellant={shell.PropellantLength}, fullRound={CarouselGeometry.FullRoundLength(shell.Diameter, shell.PropellantLength)}";
                    if (preview != state.LastPreview)
                    {
                        state.LastPreview = preview;
                        Plugin.ModLog.LogDebug($"[Carousel] Ammunition refreshed: {preview}");
                    }
                }
            }
        });
    }
    // 0.2.28 (M3): ToBlueprint postfixes of CarouselRuntime and BustleRuntime in one hook, same order.
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static void Saved(VehicleObject __0, Sprocket.Vehicles.Serialization.VehicleObjectBlueprint __result)
    {
        Save(__0, __result);
        BustleRuntime.Save(__0, __result);
    }
    private static void Save(VehicleObject __0, Sprocket.Vehicles.Serialization.VehicleObjectBlueprint __result)
    {
        // ToBlueprint inlines VehicleComponent.SaveData; its base-method detour is never reached on file saves.
        Guard("Save basket", () =>
        {
            var parts = __0.Components;
            for (int i = 0; i < Count(parts); i++)
            {
                var basket = parts[i].TryCast<TurretBasket>();
                if (basket == null) continue;
                var state = State(basket);
                CarouselPersistence.Write(__result.State, state.Enabled, state.CannonId, state.Layout);
                Plugin.ModLog.LogDebug($"[Carousel] Saved basket {basket.VUID.Value}: enabled={state.Enabled}, cannon={state.CannonId}, layout={state.Layout}");
            }
        });
    }
    // 0.2.28 (M3/M4): LoadData postfixes of CarouselRuntime and BustleRuntime in one hook, same order, each guarded
    // including its TryCast (was outside the guard).
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.LoadData))]
    private static void Loaded(VehicleComponent __instance, Il2CppSystem.Runtime.Serialization.SerializationInfo __0)
    {
        try { Load(__instance, __0); }
        catch (Exception ex) { HookGuard.Warn("VehicleComponent.LoadData basket", HookGuard.Vuid(__instance), ex); }
        try { BustleRuntime.Load(__instance, __0); }
        catch (Exception ex) { HookGuard.Warn("VehicleComponent.LoadData rack", HookGuard.Vuid(__instance), ex); }
    }
    private static void Load(VehicleComponent __instance, Il2CppSystem.Runtime.Serialization.SerializationInfo __0)
    {
        var basket = __instance.TryCast<TurretBasket>();
        if (basket == null) return;
        Guard("Load basket", () =>
        {
            var state = State(basket); Invalidate(state); ReleaseRegistration(state); state.Damage.ResetForNewPlay();
            var saved = CarouselPersistence.Read(__0);
            state.Enabled = saved.Enabled; state.CannonId = saved.CannonId; state.Layout = saved.Layout;
            if (saved.Enabled)
                Plugin.ModLog.LogDebug($"[Carousel] Restored basket {basket.VUID.Value}: cannon={state.CannonId}, layout={state.Layout}");
        });
    }
    internal static void ResetEditorPlaySupply(IVehicleGateway vehicle)
    {
        var objects=vehicle.ObjectReader.Items;
        for(int i=0;i<Count(objects);i++)
        {
            var parts=objects[i].Components;
            for(int j=0;j<Count(parts);j++)
            {
                var basket=parts[j].TryCast<TurretBasket>();
                if(basket==null || !States.TryGetValue(basket.Pointer,out var state) || !state.Active) continue;
                // Before any native load task/controller is assembled: never orphan a bound rack mid-play.
                state.Damage.ResetForNewPlay();
                var previous=state.Rack?.AmountStored;
                Invalidate(state);
                Plugin.ModLog.LogDebug($"[Carousel] New editor Play supply: basket {basket.VUID.Value}, previous stock={previous}; fresh rack on assembly.");
            }
        }
    }
    // 0.2.28 (M3): the three TurretBasket.Release prefixes of 0.2.27 in one hook, in registration order: core (this
    // class), native damage (only while that group is patched), carousel visuals (only while that group is patched).
    [HarmonyPrefix, HarmonyPatch(typeof(TurretBasket), nameof(TurretBasket.Release), new Type[0])]
    private static void Released(TurretBasket __instance)
    {
        try { Release(__instance); }
        catch (Exception ex) { HookGuard.Warn("TurretBasket.Release carousel", HookGuard.Vuid(__instance), ex); }
        if (CarouselNativeDamage.Available)
        {
            try { CarouselNativeDamage.BasketReleased(__instance); }
            catch (Exception ex) { HookGuard.Warn("TurretBasket.Release native damage", HookGuard.Vuid(__instance), ex); }
        }
        if (CarouselVisuals.Available)
        {
            try { CarouselVisuals.Release(__instance); }
            catch (Exception ex) { HookGuard.Warn("TurretBasket.Release carousel visual", HookGuard.Vuid(__instance), ex); }
        }
    }
    private static void Release(TurretBasket __instance)
    {
        CarouselInspector.Forget(__instance);
        if (States.Remove(__instance.Pointer, out var state)) { Invalidate(state); Guard("Release projectile registration", () => ReleaseRegistration(state)); }
    }
    // 0.2.28 (M3): the two TurretBasket.Build postfixes of 0.2.27 in one hook, in registration order: basket mass
    // (core), then the carousel visual (carousel-visuals group, only while patched). The load-index prefix stays.
    [HarmonyPostfix, HarmonyPatch(typeof(TurretBasket), nameof(TurretBasket.Build))]
    private static void BasketBuilt(TurretBasket __instance)
    {
        Mass(__instance);
        if (CarouselVisuals.Available)
        {
            try { CarouselVisuals.Built(__instance); }
            catch (Exception ex) { HookGuard.Warn("TurretBasket.Build carousel visual", HookGuard.Vuid(__instance), ex); }
        }
    }
    // Use the game's resource setters; no Harmony detour of a native struct return.
    private static void Mass(TurretBasket __instance)
    {
        var basket = __instance;
        try
        {
            var state = State(basket);
            basket.SetMass(0, MassType.Mechanisms);
            basket.SetMass(0, MassType.Ammunition);
            if (!state.Active) return;
            var cannon = Cannons(basket).FirstOrDefault(c => c.VUID.Value == state.CannonId);
            if (cannon == null) return;
            var size = Calculate(basket, cannon);
            basket.SetMass((float)size.MechanismMassKg, MassType.Mechanisms);
            basket.SetMass(size.Capacity * cannon.ShellBlueprint.Mass, MassType.Ammunition);
        }
        catch (Exception ex) { Plugin.ModLog.LogError($"[Carousel] Basket mass: {ex}"); }
    }

    private static readonly Dictionary<IntPtr, float> lateBindAt = new();
    private static readonly Dictionary<IntPtr, (float Next, int Remaining)> loadTrace = new();

    private static void RefreshCapturedLoaders(VehicleWeaponLoadController controller)
    {
        var entries = controller.loadTasks;
        if (entries == null || entries.Length > 128) return;
        var now = Time.time;
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var task = entry?.Task?.TryCast<LoadTask>();
            var weapon = task?.target?.TryCast<WeaponBehaviour>();
            var cannon = weapon?.mount?.TryCast<Cannon>();
            if (task == null || weapon == null || cannon == null) continue;
            if (!AutoLoaders.TryGetValue(cannon.Pointer, out var binding) || binding.Task.Pointer != task.Pointer)
            {
                // Bounded retry for sources still assembling. The established
                // attachment path checks configured physical feed/health/fit.
                if (!lateBindAt.TryGetValue(cannon.Pointer, out var next) || now >= next)
                {
                    lateBindAt[cannon.Pointer] = now + 2f;
                    AttachNativeLoader(cannon);
                }
                if (!AutoLoaders.TryGetValue(cannon.Pointer, out binding) || binding.Task.Pointer != task.Pointer)
                {
                    if (BustleRuntime.Racks(cannon).Any(s => s.Enabled && !s.Semi && s.CannonId == cannon.VUID.Value))
                        TraceUnloaded(cannon, task, now, entry!.Contributors?.Length ?? 0);
                    continue;
                }
            }
            var automatic = binding.Contributor.Cast<ILoadTaskContributor>();
            var captured = entry!.Contributors;
            if (captured?.Any(c => c != null && c.Pointer == automatic.Pointer) != true && !ContributorSwitch.Off(cannon))
            {
                var advertised = weapon.LoadTaskContributors?.Any(c => c != null && c.Pointer == automatic.Pointer) == true;
                if (LoaderCapturePolicy.TryAppend(captured?.ToArray() ?? Array.Empty<ILoadTaskContributor>(), automatic,
                    advertised, (a, b) => a != null && a.Pointer == b.Pointer, out var merged))
                {
                    // 0.2.28 (H3): on a failed write the captured array goes back as it was and the repair stays off
                    // for this cannon for the session (one warning per VUID).
                    try
                    {
                        entry.Contributors = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ILoadTaskContributor>(merged);
                        entries[i] = entry; // Native array entry is a value-type wrapper.
                    }
                    catch (Exception ex)
                    {
                        try { entry.Contributors = captured; entries[i] = entry; }
                        catch (Exception restore) { HookGuard.Warn("Captured contributor restore", HookGuard.Vuid(cannon), restore); }
                        ContributorSwitch.Disable(cannon, ex);
                        continue;
                    }
                    Plugin.ModLog.LogInfo($"[Autoloaders] Repaired captured loader for '{cannon.Vehicle?.DesignInfo?.Name}', "
                        + $"cannon {cannon.VUID.Value}; existing contributors preserved; finite stock unchanged.");
                }
            }
            TraceUnloaded(cannon, task, now, entry.Contributors?.Length ?? 0);
        }
    }

    private static void TraceUnloaded(Cannon cannon, LoadTask task, float now, int contributors)
    {
        if (task.State == LoadState.Loaded) return;
        var trace = loadTrace.TryGetValue(cannon.Pointer, out var old) ? old : (Next: now + 3f, Remaining: 3);
        if (!loadTrace.ContainsKey(cannon.Pointer)) loadTrace[cannon.Pointer] = trace;
        if (trace.Remaining <= 0 || now < trace.Next) return;
        loadTrace[cannon.Pointer] = (now + 15f, trace.Remaining - 1);
        var racks = BustleRuntime.Racks(cannon).Select(s =>
            $"rack={s.Rack.VUID.Value},enabled={s.Enabled},installed={s.Rack.IsInstalled},assigned={s.CannonId},"
            + $"capacity={s.Rack.Capacity},stored={s.Rack.behaviour?.AmountStored},type={s.Rack.behaviour?.StoredTypeID.Value},"
            + $"eligible={BustleRuntime.Eligible(s, cannon)},refill={s.Refilling}");
        Plugin.ModLog.LogInfo($"[Autoloaders load trace] '{cannon.Vehicle?.DesignInfo?.Name}', cannon={cannon.VUID.Value}, "
            + $"state={task.State},request={task.currentRequest},type={task.Request.TypeID.Value},amount={task.Request.Amount},"
            + $"loadFraction={task.LoadFraction:N2},capturedContributors={contributors},"
            + $"source={task.rack?.RackIdentifier},progress={task.CurrentProgress:N2}; " + string.Join(" | ", racks));
    }
    private static void RefreshCapturedLoadersSafely(VehicleWeaponLoadController controller)
    {
        Guard("Refresh captured automatic loaders", () => RefreshCapturedLoaders(controller));
    }

}




