using Sprocket.DamageModelling;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.CrewSystems;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using UnityEngine;

namespace SprocketCarouselAutoloader;

internal static class MagazineRefill
{
    private static readonly HashSet<IntPtr> BusyCrew = new();
    private static int crewFrame = -1;
    private static readonly HashSet<IntPtr> WaitingTasks = new();
    private static readonly Dictionary<IntPtr, Vector3> OriginalCrewPoints = new();
    private static readonly HashSet<IntPtr> MissingBodyWarned = new();
    private static readonly Dictionary<IntPtr, RefillTypePin> NativeTypePins = new();
    internal static void PinNativeType(Cannon cannon, int requested, int pinned)
    {
        if (AmmoLoadoutBridge.HasLoadout(cannon)) return;
        NativeTypePins[cannon.Pointer] = NativeTypePins.TryGetValue(cannon.Pointer, out var previous)
            ? previous.With(requested, pinned) : new(requested, pinned);
    }
    internal static bool Waiting(LoadTask task) => WaitingTasks.Contains(task.Pointer);
    // 0.2.28 (H2 "Alleen bewaken"): the native task fields as they were just before the magazine route took the
    // task, restored as raw fields on an error or a time-out. Never through SetState: SetState(Loaded) calls the
    // weapon's load callback (disasm of LoadTask::SetState, state 0 branch) and could chamber a round.
    private sealed class RouteSnapshot
    {
        internal LoadState State;
        internal LoadTask.LoadAction Action;
        internal IAmmoSource? Rack;
        internal ShellLoadInfo LoadInfo;
        internal LoadRequest Request;
        internal float Travel, Prepare, Pickup, Dropoff;
        internal BustleState? Magazine;
        internal bool Refilling;
    }
    private static readonly InterventionLedger<RouteSnapshot> Routes = new();
    private static readonly InterventionSwitch RouteSwitch = new("Magazine route (crew refill wait)");
    internal static void ForgetPointers() => RouteSwitch.ForgetPointers();
    private static RouteSnapshot Snapshot(LoadTask task, BustleState magazine) => new()
    {
        State = task.state, Action = task.action, Rack = task.rack, LoadInfo = task.currentLoadInfo, Request = task.currentRequest,
        Travel = task.travelProgress, Prepare = task.prepareCurrentTime, Pickup = task.pickupCurrentTime, Dropoff = task.dropoffCurrentTime,
        Magazine = magazine, Refilling = magazine.Refilling,
    };
    private static void Restore(LoadTask task, RouteSnapshot snapshot)
    {
        task.state = snapshot.State; task.action = snapshot.Action; task.rack = snapshot.Rack;
        task.currentLoadInfo = snapshot.LoadInfo; task.currentRequest = snapshot.Request;
        task.travelProgress = snapshot.Travel; task.prepareCurrentTime = snapshot.Prepare;
        task.pickupCurrentTime = snapshot.Pickup; task.dropoffCurrentTime = snapshot.Dropoff;
    }
    // Error inside the magazine route: native fields back as before, task released, route off for this cannon.
    private static void Abandon(LoadTask task, Cannon? cannon, Exception ex)
    {
        var snapshot = Routes.Abort(task.Pointer);
        WaitingTasks.Remove(task.Pointer);
        if (snapshot != null)
        {
            try { Restore(task, snapshot); if (snapshot.Magazine != null) snapshot.Magazine.Refilling = snapshot.Refilling; }
            catch (Exception restore) { HookGuard.Warn("Magazine route restore", HookGuard.Vuid(cannon), restore); }
        }
        if (cannon == null) { try { cannon = CannonFor(task); } catch { cannon = null; } }
        RouteSwitch.Disable(cannon, ex);
    }
    // Expected time until the waiting task can resume: one round of crew or stationary refill, at least the fire cycle.
    private static double ExpectedSeconds(BustleState state, Cannon cannon)
    {
        double cycle;
        try { cycle = BustleRuntime.Seconds(state, cannon); } catch { cycle = 0; }
        var refill = state.RefillSecondsPerRound;
        return Math.Max(double.IsFinite(refill) ? refill : 0, double.IsFinite(cycle) ? cycle : 0);
    }
    private static int Count<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> list) =>
        list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;

    private static Cannon? CannonFor(LoadTask task) => task.target?.TryCast<WeaponBehaviour>()?.mount?.TryCast<Cannon>();
    // Keep the native crew task and hand allocation; aim them at the magazine
    // being replenished instead of the mechanically operated cannon breech.
    internal static void UpdateCrewPoint(Cannon cannon)
    {
        var breech = FindBreech(cannon);
        var operable = breech?.loaderOperable;
        if (operable == null) return;
        var state = BustleRuntime.Racks(cannon).FirstOrDefault(s => BustleRuntime.Eligible(s, cannon));
        if (state == null) { RestoreCrewPoint(cannon); return; }
        if (!OriginalCrewPoints.ContainsKey(operable.Pointer)) OriginalCrewPoints[operable.Pointer] = operable.OperateLocalPosition;
        // 0.2.26: the revolver is refilled at the ram chamber, where the feed arm meets the breech side,
        // so the native hand reach is measured there (0.2.24 arm point); the bustle keeps its native load point.
        var world = state.Rack.transform.TransformPoint(state.Revolver
            ? BustleRuntime.FeedLocalPosition(state.Rack, cannon) : state.Rack.LoadLocalPosition);
        state.CrewPointApplied = true;
        var local = breech!.transform.InverseTransformPoint(world);
        if (Vector3.Distance(operable.OperateLocalPosition, local) > .001f)
        {
            operable.OperateLocalPosition = local;
            Plugin.ModLog.LogDebug($"[Bustle] Cannon {cannon.VUID.Value} crew loader now operates magazine {state.Rack.VUID.Value}; native hand reach retained.");
        }
    }
    private static void RestoreCrewPoint(Cannon cannon)
    {
        var operable = FindBreech(cannon)?.loaderOperable;
        if (operable != null && OriginalCrewPoints.Remove(operable.Pointer, out var point)) operable.OperateLocalPosition = point;
    }
    // 0.2.26: one Info line per refill state change (rack VUID and reason), also with DetailedLogging off;
    // a reason that returns is logged again after 10 s at most. Compared as enum/int: no work per frame.
    private static readonly Dictionary<(int Rack, RefillReason Reason), float> LoggedReasons = new();
    internal static void Status(BustleState state, RefillReason reason, int detail = 0)
    {
        if (state.RefillReason == reason && state.RefillDetail == detail) return;
        state.RefillReason = reason; state.RefillDetail = detail;
        if (state.Semi) return; // Semi unchanged: no new log lines
        try
        {
            var key = (state.Rack.VUID.Value, reason);
            var now = Time.unscaledTime;
            if (!Plugin.DetailedLogging && LoggedReasons.TryGetValue(key, out var last) && now - last < 10f) return;
            LoggedReasons[key] = now;
            var magazine = state.Rack.behaviour;
            Plugin.ModLog.LogInfo($"[{(state.Revolver ? "Revolver" : state.BeltFeed ? "Belt" : "Bustle")}] Magazine {state.Rack.VUID.Value} refill: {RefillPolicy.Text(reason)}" +
                $"{(detail != 0 ? $" from rack {detail}" : "")}; stock {magazine?.AmountStored ?? 0}/{magazine?.Capacity ?? 0}.");
        }
        catch { /* Logging never changes refilling. */ }
    }
    internal static IEnumerable<AmmoRack> Reserves(Cannon cannon)
    {
        foreach (var rack in VehicleLoadIndex.Get(cannon).Reserves)
            if (Reserve(cannon, rack))
                yield return rack;
    }

    private static int Size(Cannon cannon) => ProjectileSizeID.FromPropellantLength(
        cannon.ShellBlueprint.Diameter, cannon.ShellBlueprint.PropellantLength).Value;
    private static bool Reserve(Cannon cannon, AmmoRack rack) => RefillMatch.Reserve(rack.IsInstalled, rack.HealthFraction,
        Size(cannon), rack.projectileSizeID.Value, rack.behaviour?.AmountStored ?? 0);

    private static bool MatchingSupply(Cannon cannon, AmmoRackBehaviour aggregate, int type)
    {
        var result = AmmoLoadoutBridge.Supply(cannon, aggregate, type, out var typed);
        if (result < 0) return false;
        var source = result == 1 ? typed : aggregate;
        return source != null && RefillMatch.Matches(Size(cannon), type, ProjectileTypeID.Invalid.Value,
            source.ShellSizeID.Value, source.StoredTypeID.Value, source.AmountStored);
    }

    internal static AmmoRack? RefillSource(BustleState state, Cannon cannon)
    {
        var source = state.RefillRack;
        var magazine = state.Rack.behaviour;
        if (magazine == null) return null;
        var type = RefillType(cannon, magazine, state.BeltFeed ? BeltRuntime.Type(state, cannon, magazine) : RequestedType(cannon, magazine));
        if(state.RefillType!=type)
        {state.RefillType=type;state.RefillProgress=0;state.RefillSource=IntPtr.Zero;source=null;}
        if (state.RefillProgress > 0 && source != null && Reserve(cannon, source) &&
            MatchingSupply(cannon, source.behaviour, type)) return source;
        source = null;
        float nearest = float.PositiveInfinity;
        var destination = state.Rack.transform.TransformPoint(state.Rack.LoadLocalPosition);
        foreach (var rack in Reserves(cannon))
        {
            if (!MatchingSupply(cannon, rack.behaviour, type)) continue;
            float distance = (rack.transform.TransformPoint(rack.LoadLocalPosition) - destination).sqrMagnitude;
            if (distance >= nearest) continue;
            source = rack; nearest = distance;
        }
        if (state.RefillRack?.Pointer != source?.Pointer) state.RefillPath = null;
        state.RefillRack = source;
        return source;
    }
    internal static int RequestedType(Cannon cannon,AmmoRackBehaviour fallback) =>
        cannon.Behaviour?.LoadTask?.TryCast<LoadTask>()?.Request.TypeID.Value??fallback.StoredTypeID.Value;

    // A native single-type box can adopt a different real source identity only while empty.
    // While partly filled its current type is the storage constraint; EB always uses its requested view.
    private static int RefillType(Cannon cannon, AmmoRackBehaviour magazine, int requested)
    {
        if (AmmoLoadoutBridge.HasLoadout(cannon)) return requested;
        int current = RequestedType(cannon, magazine);
        int original = NativeTypePins.TryGetValue(cannon.Pointer, out var pin) ? pin.Original(current) : current;
        return original != ProjectileTypeID.Invalid.Value ? original :
            magazine.AmountStored > 0 ? magazine.StoredTypeID.Value : ProjectileTypeID.Invalid.Value;
    }

    // 0.2.26: a revolver crew loader counts when assigned, healthy and within the native hand reach of the
    // drum. Until 0.2.25 the native loader task also had to be "working", which it only is while the gun
    // itself is Loading, so a drum next to a loaded gun never got crew refills (the commander-loader case).
    internal static bool RevolverCrew(Cannon cannon) => SemiAutoloader.HasCrew(cannon);

    internal static RefillReason CrewStatus(Cannon cannon)
    {
        var breech = FindBreech(cannon);
        var operable = breech?.loaderOperable;
        var active = operable?.AssignedOperator?.ActiveBehaviour;
        return RefillPolicy.CrewStatus(breech != null && breech.HealthFraction > 0,
            active != null && active.Enabled && active.HealthFraction > 0, operable?.Valid == true,
            operable?.behaviour?.TryCast<LoadContributeTask>() != null);
    }

    private static AmmoRack? LoaderlessSource(BustleState state, Cannon cannon, int type)
    {
        type = RefillType(cannon, state.Rack.behaviour, type);
        var source = state.NoLoaderRack;
        if (source != null && Reserve(cannon, source) && MatchingSupply(cannon, source.behaviour, type)) return source;
        source = null;
        float nearest = float.PositiveInfinity;
        var destination = state.Rack.transform.TransformPoint(state.Rack.LoadLocalPosition);
        foreach (var rack in Reserves(cannon))
        {
            if (!MatchingSupply(cannon, rack.behaviour, type)) continue;
            float distance = (rack.transform.TransformPoint(rack.LoadLocalPosition) - destination).sqrMagnitude;
            if (distance >= nearest) continue;
            source = rack; nearest = distance;
        }
        // No source right now pauses progress; only an actually different source
        // starts a new transfer, so a brief shortage cannot discard the partial round.
        if (source == null) return null;
        if (state.NoLoaderRack != null && state.NoLoaderRack.Pointer != source.Pointer) state.NoLoaderProgress = 0;
        state.NoLoaderRack = source;
        return source;
    }

    internal static bool TransferOne(Cannon cannon, AmmoRackBehaviour source, AmmoRackBehaviour magazine, int requested)
    {
        requested = RefillType(cannon, magazine, requested);
        // Validate the same request as source selection. EB owns its finite typed transfer/refund.
        if (!MatchingSupply(cannon, source, requested) || magazine.ShellSizeID.Value != Size(cannon) ||
            magazine.AmountStored >= magazine.Capacity) return false;
        var transfer = AmmoLoadoutBridge.Transfer(cannon, source, magazine, requested);
        if (transfer == 1) return true;
        if (transfer != 0) return false;
        var task = cannon.Behaviour?.LoadTask?.TryCast<LoadTask>();
        bool canRetype = task != null && (Waiting(task) || task.State is LoadState.Unloaded or LoadState.NeverLoaded);
        int originalType = magazine.StoredTypeID.Value;
        try
        {
            return RefillTransfer.One(new NativeStore(source), new NativeStore(magazine),
                Size(cannon), requested, ProjectileTypeID.Invalid.Value, canRetype);
        }
        finally
        {
            if (task != null && magazine.StoredTypeID.Value != originalType)
            {
                // Our supply route pins Request to the old box type. At this empty/unoccupied boundary
                // update that pin to the real incoming round; never retag a Loading/Loaded round.
                var request = task.Request;
                PinNativeType(cannon, request.TypeID.Value, magazine.StoredTypeID.Value);
                request.TypeID = magazine.StoredTypeID;
                task.Request = request;
            }
        }
    }

    private readonly record struct NativeStore(AmmoRackBehaviour Rack) : IRefillStore
    {
        public int Size => Rack.ShellSizeID.Value;
        public int Type
        {
            get => Rack.StoredTypeID.Value;
            set => Rack.StoredTypeID = new ProjectileTypeID(value);
        }
        public int Amount => Rack.AmountStored;
        public int Capacity => Rack.Capacity;
        public bool Take() => Rack.TakeShell();
        public int Load() => Rack.LoadAmmo(1);
    }

    private static void Loaderless(BustleState state, Cannon cannon, AmmoRackBehaviour magazine, int requested, float dt)
    {
        var speed = float.PositiveInfinity;
        var body = state.VehicleBody();
        if (body == null)
        {
            // Distinguish "no speed source" from "driving" in the live log.
            state.StillSeconds = 0;
            if (MissingBodyWarned.Add(state.Rack.Pointer))
                Plugin.ModLog.LogWarning($"[Revolver] Rack {state.Rack.VUID.Value}: vehicle body not found; refill without crew loader unavailable.");
            Status(state, RefillReason.NoBody);
            return;
        }
        try { speed = body.linearVelocity.magnitude; }
        catch { state.BodyCache = null; speed = float.PositiveInfinity; }
        state.StillSeconds = RevolverPolicy.StillSeconds(state.StillSeconds, speed, dt);
        if (!RevolverPolicy.IsStationary(state.StillSeconds))
        {
            Status(state, RefillReason.WaitingStill);
            return;
        }
        if (state.NoLoaderType != requested)
        {
            state.NoLoaderType = requested;
            state.NoLoaderProgress = 0;
            state.NoLoaderRack = null;
        }
        var source = LoaderlessSource(state, cannon, requested);
        if (source == null)
        {
            Status(state, RefillReason.NoReserve);
            return;
        }
        var perRound = RevolverPolicy.RefillSecondsPerRound(BustleRuntime.BaseSeconds(state, cannon));
        if (!double.IsFinite(perRound) || perRound <= 0) return;
        state.RefillSecondsPerRound = perRound;
        Status(state, RefillReason.Stationary, source.VUID.Value);
        if (!state.NoLoaderActive)
        {
            state.NoLoaderActive = true;
            if (Plugin.DetailedLogging)
                Plugin.ModLog.LogDebug($"[Revolver] Rack {state.Rack.VUID.Value} refill without crew loader started; " +
                    $"{perRound:0.0} s per round while stationary.");
        }
        state.Refilling = true;
        state.NoLoaderProgress += dt / perRound;
        if (state.NoLoaderProgress < 1) return;
        if (magazine.AmountStored >= magazine.Capacity)
        {
            state.NoLoaderProgress = 0; state.NoLoaderActive = false; state.Refilling = false;
            return;
        }
        if (!TransferOne(cannon, source.behaviour!, magazine, requested))
        {
            // Retry next tick; the completed fraction of this round is not discarded.
            state.NoLoaderProgress = Math.Min(state.NoLoaderProgress, 1);
            return;
        }
        state.NoLoaderProgress = 0;
        if (Plugin.DetailedLogging)
            Plugin.ModLog.LogDebug($"[Revolver] Rack {state.Rack.VUID.Value} refilled one round from rack " +
                $"{source.VUID.Value}; {magazine.AmountStored}/{magazine.Capacity}.");
        if (magazine.AmountStored >= magazine.Capacity)
        {
            state.Refilling = false; state.NoLoaderActive = false;
            if (Plugin.DetailedLogging)
                Plugin.ModLog.LogDebug($"[Revolver] Rack {state.Rack.VUID.Value} drum full at " +
                    $"{magazine.AmountStored}/{magazine.Capacity}.");
        }
    }

    internal static bool NoLoaderDisplay(LoadTask task, out double progress, out double perRound)
    {
        progress = perRound = 0;
        try
        {
            if (!Waiting(task)) return false;
            var cannon = CannonFor(task);
            if (cannon == null) return false;
            var view = task.rack?.TryCast<AmmoRackBehaviour>();
            BustleState? match = null;
            foreach (var state in BustleRuntime.Racks(cannon))
            {
                if (!state.Revolver || state.CannonId != cannon.VUID.Value) continue;
                var aggregate = state.Rack.behaviour;
                if (aggregate == null) continue;
                if (view != null && !AmmoLoadoutBridge.SameStorage(aggregate, view)) continue;
                if (!BustleRuntime.Eligible(state, cannon)) continue;
                match = state; break;
            }
            if (match == null || RevolverCrew(cannon)) return false;
            var magazine = match.Rack.behaviour!;
            if (magazine.AmountStored >= magazine.Capacity) return false;
            perRound = RevolverPolicy.RefillSecondsPerRound(BustleRuntime.BaseSeconds(match, cannon));
            if (!double.IsFinite(perRound) || perRound <= 0) return false;
            progress = Math.Clamp(match.NoLoaderProgress, 0, 1);
            return true;
        }
        catch { return false; }
    }

    // Do not let the native lookup bypass an empty magazine and feed the chamber directly.
    [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(LoadTask), nameof(LoadTask.UpdateRequest)),HarmonyAfter("sprocket.expandedballistics.ammoloadout")]
    private static bool KeepMagazineRoute(LoadTask __instance)
    {
        Cannon? cannon = null;
        try
        {
            // 0.2.28 (M5): a cannon without a bustle/revolver/Semi rack in its turret returns at once (cached, no
            // allocation); a waiting task keeps its 0.2.27 path.
            if (!Waiting(__instance) && !VehicleLoadIndex.HasBustles(__instance)) return true;
            cannon = CannonFor(__instance);
            if (cannon == null) return true;
            if (Waiting(__instance)) return false;
            if (__instance.State == LoadState.Loading ||
                (__instance.State == LoadState.Loaded && __instance.currentRequest != LoadRequest.Consumed)) return true;
            BustleState? empty;
            if (BeltRuntime.Assigned(__instance, out var belt))
            {
                // A non-mix box keeps its one native type; a carried mix switches across typed views of the same belt box.
                if (!BustleRuntime.Eligible(belt, cannon) || belt.Rack.behaviour == null) return false;
                var aggregate = belt.Rack.behaviour;
                bool mixed = AmmoLoadoutBridge.HasLoadout(cannon);
                int type = mixed ? BeltRuntime.Type(belt, cannon, aggregate) : BeltRuntime.Type(belt, aggregate);
                if (!mixed && __instance.Request.TypeID.Value != ProjectileTypeID.Invalid.Value && __instance.Request.TypeID.Value != type) return false;
                empty = AmmoLoadoutBridge.Available(cannon, aggregate, type) == 0 ? belt : null;
            }
            else empty = BustleRuntime.Racks(cannon).FirstOrDefault(s => BustleRuntime.Eligible(s, cannon) && s.Rack.behaviour != null &&
                AmmoLoadoutBridge.Available(cannon,s.Rack.behaviour,RequestedType(cannon,s.Rack.behaviour))==0);
            if (empty == null) return true;
            if (AmmoLoadoutBridge.HasLoadout(cannon) && empty.Rack.behaviour is { } mag)
            {
                int req = empty.BeltFeed ? BeltRuntime.Type(empty, cannon, mag) : RequestedType(cannon, mag);
                bool full = mag.Capacity > 0 && mag.AmountStored >= mag.Capacity;
                bool hasCrew = CrewStatus(cannon) == RefillReason.Crew;
                bool hasReserve = Reserves(cannon).Any(r => r.behaviour != null && MatchingSupply(cannon, r.behaviour, req));
                if (!RefillPolicy.CanTransferFromReserve(empty.Revolver, full, hasCrew, hasReserve))
                {
                    Status(empty, full ? RefillReason.Full : (!empty.Revolver && !hasCrew) ? CrewStatus(cannon) : RefillReason.NoReserve);
                    __instance.rack = null;
                    return false;
                }
            }
            // 0.2.28 (H2): after an earlier failure this cannon loads natively for the rest of the session.
            if (RouteSwitch.Off(cannon)) return true;
            Routes.Complete(__instance.Pointer); // A stale entry of a released task never shadows this snapshot.
            Routes.Begin(__instance.Pointer, Snapshot(__instance, empty), Time.time);
            empty.Refilling = true;
            WaitingTasks.Add(__instance.Pointer);
            // Consumed means already fired; Unload would incorrectly refund the round.
            if (__instance.State == LoadState.Loaded) __instance.SetState(LoadState.Unloaded);
            __instance.rack = empty.Rack.behaviour.Cast<IAmmoSource>();
            __instance.currentLoadInfo = empty.Rack.LoadInfo;
            __instance.currentRequest = LoadRequest.Consumed;
            // Native crew Contribute grants loader priority only for Loading.
            __instance.SetState(LoadState.Loading);
            Plugin.ModLog.LogDebug($"[Bustle] Cannon {cannon.VUID.Value} waiting for crew refill; direct reserve-to-chamber loading blocked.");
            return false;
        }
        catch (Exception ex)
        {
            // Failed while taking the task: raw fields back as before, route off for this cannon (one warning per VUID).
            if (Waiting(__instance) || Routes.Contains(__instance.Pointer)) Abandon(__instance, cannon, ex);
            else HookGuard.Warn("LoadTask.UpdateRequest magazine route", HookGuard.Vuid(cannon), ex);
            return true;
        }
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(LoadTask), nameof(LoadTask.Update))]
    private static bool Tick(LoadTask __instance, float __3)
    {
        Cannon? targetCannon = null;
        try
        {
            // 0.2.28 (M5): cached per task; a cannon without a bustle/revolver/Semi rack returns at once, as before.
            if (!VehicleLoadIndex.HasBustles(__instance)) return Waiting(__instance) ? Orphaned(__instance) : true;
            targetCannon = CannonFor(__instance);
            if (targetCannon == null || VehicleLoadIndex.Get(targetCannon).Bustles.Length == 0)
                return Waiting(__instance) ? Orphaned(__instance) : true;
        }
        catch (Exception ex)
        {
            if (Waiting(__instance)) Abandon(__instance, targetCannon, ex);
            else HookGuard.Warn("LoadTask.Update magazine refill", HookGuard.TaskVuid(__instance), ex);
            return true;
        }
        CarouselRuntime.Guard("Crew magazine refill", () =>
        {
            var cannon = CannonFor(__instance);
            if (cannon == null || __3 <= 0 || !float.IsFinite(__3)) return;
            if (crewFrame != Time.frameCount) { crewFrame = Time.frameCount; BusyCrew.Clear(); }
            foreach (var state in VehicleLoadIndex.Get(cannon).Bustles)
            {
                if (state.CannonId != cannon.VUID.Value) continue;
                var magazine = state.Rack.behaviour;
                if (magazine == null || state.RefillFrame == Time.frameCount) continue;
                state.RefillFrame = Time.frameCount;
                if (state.BeltFeed) { BeltRuntime.Refill(state, cannon, __3, BusyCrew); continue; }
                if (magazine.AmountStored >= magazine.Capacity)
                {
                    state.Refilling = false; state.RefillProgress = 0;
                    state.NoLoaderProgress = 0; state.NoLoaderActive = false;
                    Status(state, RefillReason.Full);
                    continue;
                }
                var requested=RequestedType(cannon,magazine);
                var requestedStock=AmmoLoadoutBridge.Available(cannon,magazine,requested);
                // Bustle and Semi keep the exact early exit (crew refill from empty). The revolver tops up
                // whenever the drum is not full: with a crew loader in reach, else only while stationary.
                var revolverCrew = state.Revolver && RevolverCrew(cannon);
                var revolverNoLoader = state.Revolver && !revolverCrew;
                // Once per Play: move the revolver crew point to the ram (also when Cannon.EnableBehaviour ran
                // before the drum was eligible). UpdateCrewPoint allocates, so never per tick.
                if (state.Revolver && !state.CrewPointApplied) { state.CrewPointApplied = true; UpdateCrewPoint(cannon); revolverCrew = RevolverCrew(cannon); revolverNoLoader = !revolverCrew; }
                if (!state.Revolver && !state.Refilling && requestedStock > 0) { Status(state, RefillReason.WaitingEmpty); continue; }
                if (!BustleRuntime.Eligible(state, cannon)) { state.RefillProgress = 0; state.Refilling = false; Status(state, RefillReason.NotEligible); continue; }
                if (revolverNoLoader)
                {
                    Loaderless(state, cannon, magazine, requested, __3);
                    continue;
                }
                if (requestedStock == 0 || state.Revolver) state.Refilling = true;
                if (magazine.AmountStored >= magazine.Capacity) { state.Refilling = false; state.RefillProgress = 0; }
                if (!state.Refilling) continue;
                var breech = FindBreech(cannon);
                var operable = breech?.loaderOperable;
                var seat = operable?.AssignedOperator;
                var realTask = operable?.behaviour?.TryCast<LoadContributeTask>();
                // This is the real crew task, not the plugin's synthetic automatic contributor.
                if (seat?.ActiveBehaviour == null || !seat.ActiveBehaviour.Enabled || seat.ActiveBehaviour.HealthFraction <= 0)
                { Status(state, RefillReason.NoCrew); continue; }
                if (!operable!.Valid) { Status(state, RefillReason.NoReach); continue; }
                if (realTask == null) { Status(state, RefillReason.NoCrew); continue; }
                // The bustle keeps the native allocation rule; the revolver crew loader may be idle (gun loaded).
                if (!state.Revolver && (realTask.operateCount == 0 || realTask.efficiency <= 0))
                { Status(state, RefillReason.CrewIdle); continue; }
                if (BusyCrew.Contains(seat.Pointer)) continue;
                var source = RefillSource(state, cannon);
                if (source == null) { state.RefillProgress = 0; state.RefillSource = IntPtr.Zero; Status(state, RefillReason.NoReserve); continue; }
                if (state.RefillSource != source.Pointer) { state.RefillSource = source.Pointer; state.RefillProgress = 0; }
                BusyCrew.Add(seat.Pointer);
                var path = state.RefillPath ??= new VehicleComponentPath(source, state.Rack);
                path.aLocalPosition = source.LoadLocalPosition; path.bLocalPosition = state.Rack.LoadLocalPosition; path.Update();
                var shot = source.LoadInfo;
                var technique = LoaderRules.GetLoadTechnique(ref shot);
                // Native command efficiency adds preparation time; it does not multiply
                // manipulation speed. A commander temporarily loading may have zero
                // command efficiency, which must not stop reserve transfer entirely.
                var seconds = Math.Max(.5, LoaderRules.CalculateLoadTime(technique, path.Distance, ref shot) +
                    LoaderRules.GetPrepareTime(realTask.commandEfficiency));
                Status(state, RefillReason.Crew, source.VUID.Value);
                state.RefillSecondsPerRound = seconds;
                // An idle revolver loader has no native efficiency yet; a healthy one works at full rate.
                var efficiency = state.Revolver && !(float.IsFinite(realTask.efficiency) && realTask.efficiency > 0) ? 1f : realTask.efficiency;
                state.RefillProgress += __3 * Math.Clamp(efficiency, 0, 1) / seconds;
                if (state.RefillProgress < 1 || magazine.AmountStored >= magazine.Capacity) continue;
                // Consume real reserve ammunition only on completed transfer; no stock is created.
                if (TransferOne(cannon, source.behaviour, magazine, requested))
                    Plugin.ModLog.LogDebug($"[Bustle] Crew transferred one carried round: rack {source.VUID.Value} -> magazine {state.Rack.VUID.Value}; stock {magazine.AmountStored}/{magazine.Capacity}.");
                state.RefillProgress = 0;
            }
        });
        return Resume(__instance);
    }

    // The waiting task resumes from the refilled magazine, is released when no eligible magazine is left, or,
    // 0.2.28, is restored after the time-out (3 x the expected cycle, at least 10 s). Errors restore the native fields.
    private static bool Resume(LoadTask __instance)
    {
        if (!Waiting(__instance)) return true;
        Cannon? assigned = null;
        try
        {
            assigned = CannonFor(__instance);
            var ready = assigned == null ? null : BeltRuntime.Assigned(__instance, out var belt)
                ? (BustleRuntime.Eligible(belt, assigned) ? belt : null)
                : BustleRuntime.Racks(assigned).FirstOrDefault(s => BustleRuntime.Eligible(s, assigned));
            var readyAggregate=ready?.Rack.behaviour;
            int requested = readyAggregate == null ? -1 : ready!.BeltFeed ? BeltRuntime.Type(ready, assigned!, readyAggregate) : RequestedType(assigned!, readyAggregate);
            if (readyAggregate!=null&&AmmoLoadoutBridge.Available(assigned!,readyAggregate,requested)>0)
            {
                WaitingTasks.Remove(__instance.Pointer);
                var source=readyAggregate;
                var result=AmmoLoadoutBridge.Supply(assigned!,readyAggregate,requested,out var typed);
                if(result<0){WaitingTasks.Add(__instance.Pointer);return false;}
                if(result==1)source=typed!;
                __instance.rack = source.Cast<IAmmoSource>();
                __instance.SetState(LoadState.Unloaded);
                __instance.RequestReload(LoadRequest.Consumed);
                Routes.Complete(__instance.Pointer);
                Plugin.ModLog.LogDebug($"[Bustle] Cannon {assigned!.VUID.Value} resumed mechanical loading from replenished magazine.");
                return true;
            }
            if (AmmoLoadoutBridge.HasLoadout(assigned!) && ready != null && readyAggregate != null)
            {
                bool full = readyAggregate.Capacity > 0 && readyAggregate.AmountStored >= readyAggregate.Capacity;
                bool hasCrew = CrewStatus(assigned!) == RefillReason.Crew;
                bool hasReserve = Reserves(assigned!).Any(r => r.behaviour != null && MatchingSupply(assigned!, r.behaviour, requested));
                if (!RefillPolicy.CanTransferFromReserve(ready.Revolver, full, hasCrew, hasReserve))
                {
                    WaitingTasks.Remove(__instance.Pointer);
                    Routes.Complete(__instance.Pointer);
                    __instance.rack = null;
                    __instance.SetState(LoadState.Unloaded);
                    return true;
                }
            }
            if (ready == null)
            {
                WaitingTasks.Remove(__instance.Pointer);
                Routes.Complete(__instance.Pointer);
                __instance.rack = null;
                __instance.SetState(LoadState.Unloaded);
                return true;
            }
            var expected = ExpectedSeconds(ready, assigned!);
            var started = Routes.Started(__instance.Pointer);
            var snapshot = Routes.Expired(__instance.Pointer, Time.time, expected);
            if (snapshot != null)
            {
                // Native fields back as before the route; the next native UpdateRequest decides again (the magazine
                // route takes the task again while the magazine is still empty). The crew refill progress is kept.
                WaitingTasks.Remove(__instance.Pointer);
                Restore(__instance, snapshot);
                if (TimeoutWarned.First("route time-out", HookGuard.Vuid(assigned)))
                {
                    var text = $"[Bustle] Cannon {HookGuard.Vuid(assigned)} waited {Time.time - started:0} s for a magazine refill " +
                        $"(limit {GuardPolicy.TimeoutSeconds(expected):0} s, refill: {RefillPolicy.Text(ready.RefillReason)}); load task restored to its native state. Logged once per VUID.";
                    // A known block (no reserve, no crew, driving revolver, ...) is an expected long wait; anything else is unexpected.
                    if (RefillPolicy.Blocked(ready.RefillReason) || ready.RefillReason == RefillReason.WaitingStill) Plugin.ModLog.LogInfo(text);
                    else Plugin.ModLog.LogWarning(text);
                }
                else if (Plugin.DetailedLogging)
                    Plugin.ModLog.LogDebug($"[Bustle] Cannon {HookGuard.Vuid(assigned)} magazine route time-out; load task restored.");
                return true;
            }
            return false;
        }
        catch (Exception ex) { Abandon(__instance, assigned, ex); return true; }
    }
    private static readonly WarnOnce TimeoutWarned = new();

    // 0.2.28: a waiting task whose magazine left the turret. 0.2.27 kept it waiting for good (the refill tick returned
    // before the resume); the native step still runs as before, and after the time-out the task is restored.
    private static bool Orphaned(LoadTask task)
    {
        try
        {
            var snapshot = Routes.Expired(task.Pointer, Time.time, 0);
            if (snapshot == null) return true;
            WaitingTasks.Remove(task.Pointer);
            Restore(task, snapshot);
            if (TimeoutWarned.First("route time-out", HookGuard.TaskVuid(task)))
                Plugin.ModLog.LogWarning($"[Bustle] Cannon {HookGuard.TaskVuid(task)}: waiting load task without a magazine in its turret; " +
                    "restored to its native state after the time-out. Logged once per VUID.");
        }
        catch (Exception ex) { HookGuard.Warn("Magazine route orphan restore", HookGuard.TaskVuid(task), ex); }
        return true;
    }

    // 0.2.28: part of the merged Cannon.DisableBehaviour prefix (CarouselRuntime.CannonDisabled), same order as before.
    internal static void ClearWaiting(Cannon __instance)
    {
        RestoreCrewPoint(__instance);
        NativeTypePins.Remove(__instance.Pointer);
        foreach (var state in BustleRuntime.Racks(__instance))
            if (state.CannonId == __instance.VUID.Value) state.CrewPointApplied = false;
        var task = __instance.Behaviour?.LoadTask?.TryCast<LoadTask>();
        if (task != null) { WaitingTasks.Remove(task.Pointer); Routes.Complete(task.Pointer); }
    }

    // 0.2.28 (M3): the three LoadTask.Update postfixes of 0.2.27 in one hook, in their registration order:
    // the visual/HUD tick (core), the native-cycle diagnostics (core, DetailedLogging only) and the reload audio
    // (audio group, only while that group is active). Each part has its own guard.
    [HarmonyPostfix, HarmonyPatch(typeof(LoadTask), nameof(LoadTask.Update))]
    private static void AfterUpdate(LoadTask __instance)
    {
        try { BeltRuntime.AfterStep(__instance); }
        catch (Exception ex) { HookGuard.Warn("Belt cycle accounting", HookGuard.TaskVuid(__instance), ex); }
        // 0.2.32: carousel ring turns (one count check when nothing turns; nothing for vehicles without a carousel).
        if (CarouselVisuals.Available)
        {
            try { CarouselVisuals.Tick(); }
            catch (Exception ex) { HookGuard.Warn("LoadTask.Update ring turn", HookGuard.TaskVuid(__instance), ex); }
        }
        try
        {
            // Visual work follows the same task tick, including skipped refill updates.
            if (VehicleLoadIndex.HasBustles(__instance))
            {
                var cannon=CannonFor(__instance);
                if(cannon!=null) AutoloaderVisuals.Tick(cannon,__instance);
            }
        }
        catch (Exception ex) { HookGuard.Warn("LoadTask.Update visual tick", HookGuard.TaskVuid(__instance), ex); }
        if (Plugin.DetailedLogging)
        {
            try { NativeCycleDiagnostics.Observe(__instance); }
            catch (Exception ex) { HookGuard.Warn("LoadTask.Update cycle diagnostics", HookGuard.TaskVuid(__instance), ex); }
        }
        AutoloaderAudio.Updated(__instance);
    }

    private static CannonBreech? FindBreech(Cannon cannon)
    {
        return VehicleLoadIndex.Get(cannon).Breech;
    }
}

