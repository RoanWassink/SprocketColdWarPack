using Sprocket.DamageModelling;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.CrewSystems;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using UnityEngine;

namespace SprocketCarouselAutoloader;

/// <summary>Belt branches called only by the existing hooks. Native rack owns ammunition and damage.</summary>
internal static class BeltRuntime
{
    internal static bool Assigned(LoadTask task, out BustleState state)
    {
        state = null!;
        var link = VehicleLoadIndex.Link(task);
        if (link?.Cannon == null) return false;
        var candidates = link.Connections.Bustles;
        for (int i = 0; i < candidates.Length; i++)
            if (candidates[i].BeltFeed && candidates[i].Active && candidates[i].CannonId == link.Cannon.VUID.Value)
            { state = candidates[i]; return true; }
        return false;
    }

    internal static int Type(BustleState state, AmmoRackBehaviour magazine)
    {
        // Phase one pins the box's one native type, never the player's next-load preference.
        state.BeltType = magazine.StoredTypeID.Value;
        return state.BeltType;
    }

    internal static int Type(BustleState state, Cannon cannon, AmmoRackBehaviour magazine)
    {
        if (AmmoLoadoutBridge.HasLoadout(cannon))
        {
            int req = MagazineRefill.RequestedType(cannon, magazine);
            if (req != ProjectileTypeID.Invalid.Value) { state.BeltType = req; return req; }
        }
        return Type(state, magazine);
    }

    internal static bool SelectSupply(LoadTask task, Cannon cannon, BustleState state)
    {
        if (!BustleRuntime.Eligible(state, cannon)) return false;
        var aggregate = state.Rack.behaviour;
        if (aggregate == null) return false;
        bool mixed = AmmoLoadoutBridge.HasLoadout(cannon);
        var request = task.Request;
        int type = mixed && request.TypeID.Value != ProjectileTypeID.Invalid.Value
            ? request.TypeID.Value : Type(state, aggregate);
        if (!mixed && request.TypeID.Value != ProjectileTypeID.Invalid.Value && request.TypeID.Value != type) return false;
        if (!state.BeltSupply.TryGet(aggregate.Pointer, type, out var supply))
        {
            var result = AmmoLoadoutBridge.Supply(cannon, aggregate, type, out var typed);
            if (result < 0) return false;
            supply = result == 1 ? typed : aggregate;
            if (supply == null) return false;
            state.BeltSupply.Store(aggregate.Pointer, type, supply);
            state.BeltNativeSupply = supply.Cast<IAmmoSource>();
        }
        else if (supply != null && (state.BeltNativeSupply == null || state.BeltNativeSupply.Pointer != supply.Pointer))
            state.BeltNativeSupply = supply.Cast<IAmmoSource>();
        if (supply == null || supply.AmountStored == 0 || supply.StoredTypeID.Value == ProjectileTypeID.Invalid.Value) return false;
        if (request.TypeID.Value == ProjectileTypeID.Invalid.Value || request.SizeID.Caliber == 0)
        {
            MagazineRefill.PinNativeType(cannon, request.TypeID.Value, supply.StoredTypeID.Value);
            request.TypeID = supply.StoredTypeID; request.SizeID = supply.ShellSizeID; request.Destination = cannon.LoadPosition;
        }
        request.Amount = 1;
        if (!request.IsMatch(state.BeltNativeSupply!)) return false;
        task.Request = request;
        if (task.State == LoadState.NeverLoaded) task.currentRequest = LoadRequest.Unused;
        task.rack = state.BeltNativeSupply;
        task.currentLoadInfo = state.Rack.LoadInfo;
        double cycle = BustleRuntime.Seconds(state, cannon);
        double frame = Time.deltaTime;
        double quantum = state.BeltQuantum > 0 ? state.BeltQuantum : frame / BeltCycleClock.Steps(cycle, frame);
        if (!(quantum > 0)) quantum = cycle / BeltCycleClock.Phases;
        state.BeltClock.Begin(cycle, quantum, frame);
        SetTimings(task, state);
        return true;
    }

    internal static void SetTimings(LoadTask task, BustleState state)
    {
        task.pickupTime = 0; task.totalDistance = 0;
        task.dropoffTime = state.BeltClock.Active ? state.BeltClock.RamWork(task.dropoffCurrentTime,
            state.BeltQuantum > 0 ? state.BeltQuantum : Math.Max(Time.deltaTime, .00001f)) : 0;
    }

    internal static void BeforeStep(LoadTask task, BustleState state, float dt)
    {
        state.BeltQuantum = dt;
        // Pending time is consumed by the inner postfix only. The outer subdividing call has none.
        state.BeltPendingDt = dt;
        if (task.State == LoadState.Loading && !state.BeltClock.Active)
        {
            // A native request already in progress (e.g. initial Play loading) gets no old rounding credit.
            var cannon = VehicleLoadIndex.Link(task)?.Cannon;
            if (cannon != null) state.BeltClock.Begin(BustleRuntime.Seconds(state, cannon), dt, Time.deltaTime);
        }
        SetTimings(task, state);
    }

    internal static void AfterStep(LoadTask task)
    {
        if (!Assigned(task, out var state) || state.BeltPendingDt <= 0) return;
        float dt = state.BeltPendingDt; state.BeltPendingDt = 0;
        if (MagazineRefill.Waiting(task)) { state.BeltClock.Reset(); return; }
        if (task.State is LoadState.Loading or LoadState.Loaded)
            state.BeltClock.Step(dt, task.State == LoadState.Loaded);
        else state.BeltClock.Reset();
    }

    internal static void Refill(BustleState state, Cannon cannon, float dt, HashSet<IntPtr> busy)
    {
        var magazine = state.Rack.behaviour;
        if (magazine == null) return;
        // Keep the stillness timer current even while full or lacking a loader.
        double speed = double.PositiveInfinity;
        var body = state.VehicleBody();
        if (body != null)
        {
            try { speed = body.linearVelocity.magnitude; }
            catch { state.BodyCache = null; }
        }
        state.StillSeconds = Belt.Still(state.StillSeconds, speed, dt);
        state.BeltStationary = double.IsFinite(speed) && Belt.Stationary(state.StillSeconds);
        state.Refilling = false;
        if (magazine.AmountStored >= magazine.Capacity)
        { Stop(state, RefillReason.Full); return; }
        if (!BustleRuntime.Eligible(state, cannon)) { Stop(state, RefillReason.NotEligible); return; }
        // EnableBehaviour can run before the box is built/eligible. Apply the native refill
        // point once it is ready, as for the revolver, rather than leaving the breech point.
        if (!state.CrewPointApplied) MagazineRefill.UpdateCrewPoint(cannon);
        var crewReason = MagazineRefill.CrewStatus(cannon);
        state.BeltCrew = crewReason == RefillReason.Crew;
        if (!state.BeltStationary) { Stop(state, body == null ? RefillReason.NoBody : RefillReason.WaitingStill); return; }
        if (!state.BeltCrew) { Stop(state, crewReason); return; }
        var operable = VehicleLoadIndex.Get(cannon).Breech?.loaderOperable;
        var seat = operable?.AssignedOperator;
        var real = operable?.behaviour?.TryCast<LoadContributeTask>();
        if (seat == null || real == null) { Stop(state, RefillReason.NoCrewTask); return; }
        if (busy.Contains(seat.Pointer)) { state.RefillProgress = 0; return; }
        var source = MagazineRefill.RefillSource(state, cannon);
        if (source?.behaviour == null) { Stop(state, RefillReason.NoReserve); return; }
        if (state.RefillSource != source.Pointer) { state.RefillSource = source.Pointer; state.RefillProgress = 0; }
        busy.Add(seat.Pointer);
        double efficiency = double.IsFinite(real.efficiency) && real.efficiency > 0 ? real.efficiency : 1;
        state.RefillSecondsPerRound = Belt.RefillTime(efficiency);
        state.Refilling = true;
        MagazineRefill.Status(state, RefillReason.Crew, source.VUID.Value);
        if (Belt.AdvanceRefill(ref state.RefillProgress, true, true, false, true, dt, efficiency))
            MagazineRefill.TransferOne(cannon, source.behaviour, magazine, Type(state, cannon, magazine));
    }

    private static void Stop(BustleState state, RefillReason reason)
    {
        state.RefillProgress = 0; state.RefillSource = IntPtr.Zero;
        MagazineRefill.Status(state, reason);
    }
}
