using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Control;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using UnityEngine;

namespace SprocketCarouselAutoloader;

// 0.2.26: HUD data for the player's bustle/revolver autoloaders. No UI Kit types here, so the mod
// loads without the kit. Sampled from the existing LoadTask.Update tick at most 10 times per second
// per rack; the cards cache their strings, so the kit's per-frame getters do not allocate.
// 0.2.27: every autoloader of the player's vehicle is followed (up to MaxTracked), but only the ones
// with something running are shown, at most MaxRows of them (AutoloaderHudLayout.Select).
internal static class AutoloaderHudFeed
{
    internal const int MaxRows = AutoloaderHudLayout.MaxRows;
    private const int MaxTracked = 8;
    private static readonly HudCard[] Cards = NewCards();
    private static readonly BustleState?[] Owners = new BustleState?[MaxTracked];
    private static readonly float[] Seen = new float[MaxTracked];
    private static readonly bool[] Flags = new bool[MaxTracked];
    private static readonly int[] Picked = new int[MaxRows];
    private static readonly HudCardShape[] Shapes = new HudCardShape[MaxRows];
    private static float lastSample = -10;

    /// <summary>The cards on screen (first ShownCount entries, the rest null); the view's getters read these.</summary>
    internal static readonly HudCard?[] Shown = new HudCard?[MaxRows];
    internal static int ShownCount;
    /// <summary>Height in kit px of the HUD with the cards now on screen; the view anchors with it.</summary>
    internal static float HeightPx = 2 * AutoloaderHudLayout.Pad;
    /// <summary>[Hud] BlockedSeconds.</summary>
    internal static float BlockedSeconds = AutoloaderHudLayout.DefaultBlockedSeconds;

    private static HudCard[] NewCards()
    {
        var cards = new HudCard[MaxTracked];
        for (int i = 0; i < cards.Length; i++) cards[i] = new HudCard();
        return cards;
    }

    /// <summary>True while fresh samples arrive (Play, player vehicle) and at least one autoloader has something running.</summary>
    internal static bool Active => Plugin.HudEnabled && ShownCount > 0 && Time.unscaledTime - lastSample < 1f;

    internal static void Sample(BustleState state, Cannon cannon, LoadTask task)
    {
        if (!Plugin.HudEnabled || state.Semi) return;
        try
        {
            float now = Time.unscaledTime;
            if (now < state.HudNext) return;
            state.HudNext = now + .1f;
            if (now >= state.HudPlayerCheck)
            {
                state.HudPlayerCheck = now + 1f;
                state.HudPlayer = state.Rack.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.ControlType == ControlType.Player;
            }
            Expire(now);
            if (!state.HudPlayer) return;
            int slot = -1;
            for (int i = 0; i < MaxTracked; i++) if (ReferenceEquals(Owners[i], state)) { slot = i; break; }
            if (slot < 0) for (int i = 0; i < MaxTracked; i++) if (Owners[i] == null) { slot = i; Owners[i] = state; break; }
            if (slot < 0) return;
            Seen[slot] = now; lastSample = now;
            var magazine = state.Rack.behaviour;
            int stored = magazine?.AmountStored ?? 0, capacity = magazine?.Capacity ?? state.Rack.Capacity;
            bool waiting = MagazineRefill.Waiting(task);
            bool cycling = task.State == LoadState.Loading && !waiting;
            if (state.BeltFeed)
            {
                Cards[slot].UpdateBelt(stored, capacity, Belt.Rpm(cannon.Blueprint.Caliber),
                    Belt.Status(cycling, stored, capacity, state.Refilling, state.BeltStationary, state.BeltCrew));
                Reselect(); return;
            }
            double cycleFraction = cycling ? task.LoadFraction : 0, cycleRemaining = cycling ? task.RemainingTime : double.NaN;
            var reason = state.RefillReason;
            double progress = reason == RefillReason.Crew ? state.RefillProgress : reason == RefillReason.Stationary ? state.NoLoaderProgress : 0;
            double remaining = (reason == RefillReason.Crew || reason == RefillReason.Stationary) && state.RefillSecondsPerRound > 0
                ? (1 - Math.Clamp(progress, 0, 1)) * state.RefillSecondsPerRound : double.NaN;
            if (stored >= capacity && capacity > 0) reason = RefillReason.Full;
            // Game time, so the "Blocked" window stands still while the game is paused.
            Cards[slot].Update(state.Revolver, state.TwoPieceActive, stored, capacity, cycling, waiting, cycleFraction, cycleRemaining,
                reason, progress, remaining, Time.time, BlockedSeconds);
            Reselect();
        }
        catch { /* The HUD never affects loading. */ }
    }

    private static void Expire(float now)
    {
        bool freed = false;
        for (int i = 0; i < MaxTracked; i++)
            if (Owners[i] != null && now - Seen[i] > 1f) { Owners[i] = null; Cards[i].Reset(); freed = true; }
        if (freed) Reselect();
    }

    // Chooses the cards on screen and the HUD height; eight bools and at most four shapes, no allocation.
    private static void Reselect()
    {
        for (int i = 0; i < MaxTracked; i++) Flags[i] = Owners[i] != null && Cards[i].Shown;
        int n = AutoloaderHudLayout.Select(Flags, Picked);
        for (int k = 0; k < MaxRows; k++)
        {
            var card = k < n ? Cards[Picked[k]] : null;
            Shown[k] = card;
            Shapes[k] = card != null ? card.Shape : default;
        }
        ShownCount = n;
        HeightPx = AutoloaderHudLayout.Height(new ReadOnlySpan<HudCardShape>(Shapes, 0, n), Plugin.HudRings);
    }
}
