using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.Vehicles.AmmunitionStorage;
using UnityEngine;
using UnityEngine.Events;

namespace SprocketCarouselAutoloader;

internal static class BustleInspector
{
    private static bool open = true;
    private static Il2CppSystem.Action<bool> Bool(Action<bool> callback) => DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(callback)!;
    [HarmonyPostfix, HarmonyPatch(typeof(AmmoRackEditor), nameof(AmmoRackEditor.OnGUI))]
    private static void Draw(AmmoRackEditor __instance, IGUILayout __0)
    {
        Sprocket.Vehicles.AmmunitionStorage.AmmoRack rack;
        try { rack = __instance.Component; if (!BustleRuntime.IsPart(rack)) return; }
        catch (Exception ex) { HookGuard.Warn("AmmoRackEditor.OnGUI", -1, ex); return; }
        CarouselRuntime.Guard("Bustle inspector", () =>
        {
            var state = BustleRuntime.State(rack);
            var ui = __0.TryCast<IGUIElementDrawer>();
            if (ui == null) return;
            __0.EndAllDropdowns();
            __0.BeginDropdown(state.Semi ? "Assisted autoloader" : state.Revolver ? "Revolver autoloader" : "Bustle autoloader", open, Bool(v => open = v));
            try
            {
                void Changed() { rack.RequestRebuild(); __instance.RequestRedraw(); }
                if (!BustleRuntime.Allowed(state))
                {
                    var from = CarouselDatePolicy.For(state.Kind);
                    ui.InfoField($"{from:yyyy-MM-dd} required; saved part retained and manual loading used on this vehicle.", 2,
                        new UnityEngine.Color(.6f, .6f, .6f));
                }
                ui.ToggleField(state.Semi ? "Mechanical assistance" : "Automatic loader", state.Enabled, Bool(v => { state.Enabled = v; Changed(); }),
                    state.Revolver
                        ? "Fast fire from one drum. Without a crew loader the crew refills the drum from the normal racks only while the vehicle stands still."
                        : "Uses a finite ready magazine. An assigned crew loader replenishes it from normal racks after it empties.");
                var cannons = CarouselRuntime.Cannons(rack);
                var selected = cannons.FindIndex(c => c.VUID.Value == state.CannonId);
                // 0.2.24: with an assigned cannon the bustle/revolver arm is placed automatically at the
                // point nearest the breech; the mirror toggle remains for Semi and for racks without a cannon.
                bool auto = !state.Semi && selected >= 0 && BustleRuntime.Feed(state, cannons[selected]).Auto && state.FeedKnown;
                if (!auto)
                    ui.ToggleField("Mirror feed arm", state.MirrorFeedArm, Bool(v =>
                    {
                        state.MirrorFeedArm = v;
                        BustleRuntime.InvalidateRuntimeCaches();
                        Changed();
                    }), state.Semi ? "Place the feed arm on the opposite side." : "Place the feed arm on the opposite side (used while no cannon is assigned).");
                if (!state.Semi)
                    ui.ToggleField("Two-piece ammunition", state.TwoPiece, Bool(v =>
                    {
                        state.TwoPiece = v;
                        BustleRuntime.InvalidateRuntimeCaches();
                        Changed();
                    }), "Projectile and charge stored separately: the rack only needs the longest piece, holds half as many complete rounds and the cycle is 1.25x slower.");
                var labels = new Il2CppSystem.Collections.Generic.List<string>();
                labels.Add("Select cannon...");
                foreach (var cannon in cannons) labels.Add($"{cannon.Blueprint.Name} | {cannon.Blueprint.Caliber} mm");
                ui.Dropdown("Assigned cannon", labels.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), selected + 1,
                    DelegateSupport.ConvertDelegate<UnityAction<int>>((Action<int>)(index =>
                    {
                        if (index < 0 || index > cannons.Count) return;
                        BustleRuntime.Assign(state, index == 0 ? null : cannons[index - 1]);
                        Changed();
                    }))!, "Cannons in the same turret. This rack follows the assigned cannon's ammunition.");

                if (selected < 0) { ui.InfoField("Place under the same turret and choose its cannon", 2); return; }
                var assigned = cannons[selected];
                // Editor only: a moved rack or cannon re-places the arm (rebuild once, nothing per frame in Play).
                if (auto && state.FeedSpec != null && BustleRuntime.LoadPoint(rack, assigned, out var load) &&
                    Math.Abs(load.x - state.FeedSpec.LoadX) + Math.Abs(load.y - state.FeedSpec.LoadY) + Math.Abs(load.z - state.FeedSpec.LoadZ) > .002)
                { state.FeedValid = false; rack.RequestRebuild(); }
                var physical=ShellLoaderGeometry.Read(assigned);
                var capacity=BustleRuntime.Capacity(state,assigned);
                if(state.TwoPieceActive && physical.Valid)
                {
                    var pieces=TwoPiecePolicy.Pieces(physical.Status,physical.ProjectileMm,physical.ChargeMm,physical.DiameterMm);
                    ui.InfoField($"Two-piece: projectile {Math.Ceiling(pieces.Projectile):0} mm + charge {Math.Ceiling(pieces.Charge):0} mm; longest piece {TwoPiecePolicy.LongestMm(pieces.Projectile,pieces.Charge)} mm",2);
                    ui.InfoField(capacity>0?$"Shells: {capacity} complete rounds":"Ammunition does not fit",2);
                }
                else foreach(var line in LoaderFitReadout.Magazine(physical.ProjectileMm,physical.ChargeMm,capacity)) ui.InfoField(line,2);
                if(capacity==0)
                {
                    // Exactly one reason (never "too small" and "too large" together).
                    if(state.Revolver) ui.InfoField(BustleRuntime.CapacityReason(state,assigned),2);
                    else if(!state.Semi && !state.TwoPieceActive && physical.Valid && rack.Blueprint!=null && Math.Ceiling(physical.FullLengthMm)>rack.Blueprint.Length)
                        ui.InfoField($"Rack shorter than the round ({Math.Ceiling(physical.FullLengthMm):0} mm): lengthen it or use two-piece",2);
                    ui.InfoField("Disable or unassign the loader to use manual loading.",2);
                }
                var distance = BustleRuntime.Distance(state, assigned);
                var limit = BustleRuntime.FeedLimit(state);
                ui.InfoField($"Feed arm to breech: {distance:0.00} / {limit:0.00} m", 2);
                if (auto) ui.InfoField("Feed arm placed automatically at the point nearest the breech", 2);
                if (distance >= limit) ui.InfoField(!state.Semi && Plugin.MaxFeedDistance > 0
                    ? "Outside feed reach (config Bustle.MaxFeedDistance): move the autoloader closer"
                    : "Outside native crew-loader reach: move the autoloader closer", 2);
                if(state.Semi) ui.InfoField(SemiAutoloader.HasCrew(assigned)?"Crew loader ready":"Requires a healthy crew loader",2);
                else if(state.Revolver)
                {
                    var drum=BustleRuntime.Drum(rack,assigned,state.TwoPieceActive);
                    if(capacity>0) ui.InfoField(state.TwoPieceActive
                        ? $"1 drum: {drum.Chambers} chambers x {drum.Rows} rows = {drum.Capacity} pieces = {capacity} rounds"
                        : RevolverPolicy.DrumText(drum.Chambers,drum.Rows,capacity),2);
                    var stored=physical.Valid ? BustleRuntime.StoredLengthMm(state,physical) : 0;
                    ui.InfoField($"Calibre: {physical.DiameterMm:0} / {RevolverPolicy.MaxCaliberMm:0} mm",2);
                    ui.InfoField(RevolverPolicy.MaxRoundLengthMm>0
                        ? $"{(state.TwoPieceActive?"Longest piece":"Round length")}: {stored:0} / {RevolverPolicy.MaxRoundLengthMm:0} mm"
                        : $"{(state.TwoPieceActive?"Longest piece":"Round length")}: {stored:0} mm (no fixed limit; the rack decides)",2);
                    var cycle=BustleRuntime.Seconds(state,assigned);
                    ui.InfoField($"Fire cycle: {cycle:0.00} s",2);
                    // Designer check: an assigned healthy loader; "working" only exists in Play.
                    ui.InfoField(SemiAutoloader.HasCrew(assigned)
                        ? "Refill: crew loader (also while driving)"
                        : $"Refill: {RevolverPolicy.RefillSecondsPerRound(BustleRuntime.BaseSeconds(state,assigned)):0.0} s per round while stationary (no crew loader)",2);
                }
                else if(capacity>0) ui.InfoField($"Reload: {BustleRuntime.Seconds(state,assigned):0.00} s",2);
                if(state.Refilling)
                {
                    var refill = state.Revolver && !MagazineRefill.RevolverCrew(assigned) ? state.NoLoaderProgress : state.RefillProgress;
                    ui.InfoField($"Refilling: {refill:P0}",2);
                }
            }
            finally { __0.EndAllDropdowns(); }
        });
    }
}



