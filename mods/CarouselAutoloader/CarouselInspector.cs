using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Turrets;
using UnityEngine;
using UnityEngine.Events;
using VehicleDesigner.Compartments.Design;

namespace SprocketCarouselAutoloader;

internal static class CarouselInspector
{
    private static bool open = true;
    private static readonly Dictionary<IntPtr, TurretBasketEditor> Editors = new();
    internal static void Redraw(TurretBasket basket)
    {
        if (Editors.TryGetValue(basket.Pointer, out var editor)) editor.RequestRedraw();
    }
    internal static void Forget(TurretBasket basket) => Editors.Remove(basket.Pointer);
    private static Il2CppSystem.Action<bool> Bool(Action<bool> callback) => DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(callback)!;
    private static UnityAction<int> Int(Action<int> callback) => DelegateSupport.ConvertDelegate<UnityAction<int>>(callback)!;
    private static Il2CppSystem.Collections.Generic.IReadOnlyList<string> Labels(IEnumerable<string> values)
    {
        var list = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var value in values) list.Add(value);
        return list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>();
    }
    [HarmonyPostfix, HarmonyPatch(typeof(TurretBasketEditor), nameof(TurretBasketEditor.OnGUI))]
    private static void Draw(TurretBasketEditor __instance, IGUILayout __0)
    {
        CarouselRuntime.Guard("Basket inspector", () =>
        {
            var basket = __instance.Component;
            Editors[basket.Pointer] = __instance;
            var state = CarouselRuntime.State(basket);
            var ui = __0.TryCast<IGUIElementDrawer>();
            if (ui == null) return;
            __0.EndAllDropdowns();
            __0.BeginDropdown("Carousel autoloader", open, Bool(value => open = value));
            try
            {
                void Changed()
                {
                    CarouselRuntime.Invalidate(state);
                    basket.RequestRebuild();
                    __instance.RequestRedraw();
                }
                ui.ToggleField("Carousel autoloader", state.Enabled, Bool(value =>
                {
                    state.Enabled = value;
                    // Choose automatically only when there is a single unambiguous candidate.
                    var candidates = CarouselRuntime.Cannons(basket);
                    if (value && state.CannonId < 0 && candidates.Count == 1) state.CannonId = candidates[0].VUID.Value;
                    Changed();
                }), "Separate projectile and charge. Consumes finite ammunition; ordinary racks remain available for manual loading.");
                if (!state.Enabled) return;
                var date = CarouselRuntime.Date(basket);
                var allowed = CarouselRuntime.Allowed(state);
                if (!allowed)
                {
                    var from = CarouselDatePolicy.ForLayout(state.Layout);
                    ui.InfoField($"Storage layout needs {from:yyyy-MM-dd}; this vehicle keeps manual loading. Saved layout retained.", 2,
                        new UnityEngine.Color(.6f, .6f, .6f));
                }
                var layouts = new[] { CarouselLayout.HorizontalCassette, CarouselLayout.VerticalCharge };
                var layoutLabels = layouts.Select(layout =>
                {
                    var name = layout == CarouselLayout.HorizontalCassette
                        ? "T-72 style: both horizontal" : "T-64/80 style: upright charge";
                    var from = CarouselDatePolicy.ForLayout(layout);
                    return CarouselDatePolicy.Allows(date, from)
                        ? name : $"{name} (vanaf {from:yyyy-MM-dd})";
                });
                ui.Dropdown("Storage layout", Labels(layoutLabels), (int)state.Layout,
                    Int(index =>
                    {
                        if (index is < 0 or > 1) return;
                        var layout = (CarouselLayout)index;
                        if (!CarouselDatePolicy.Allows(date, CarouselDatePolicy.ForLayout(layout))) return;
                        state.Layout = layout; Changed();
                    }),
                    "Horizontal or upright charge storage. Too-early layouts are labelled with their start date.");
                var cannons = CarouselRuntime.Cannons(basket);
                var selected = cannons.FindIndex(c => c.VUID.Value == state.CannonId);
                var names = new[] { "Select cannon..." }.Concat(cannons.Select(c =>
                    $"{c.Blueprint.Name} | {c.Blueprint.Caliber} mm"));
                ui.Dropdown("Assigned cannon", Labels(names), selected + 1, Int(index =>
                {
                    if (index < 0 || index > cannons.Count) return;
                    state.CannonId = index == 0 ? -1 : cannons[index - 1].VUID.Value;
                    // Prevent two baskets feeding the same cannon.
                    if (index > 0)
                    {
                        foreach (var bustle in BustleRuntime.Racks(cannons[index - 1]))
                            if (bustle.CannonId == state.CannonId)
                            { bustle.CannonId = -1; bustle.AutoAssign = false; bustle.Rack.RequestRebuild(); }
                        foreach (var other in CarouselRuntime.Baskets(cannons[index - 1]))
                        {
                            var otherState = CarouselRuntime.State(other);
                            if (other.Pointer != basket.Pointer && otherState.CannonId == state.CannonId)
                            { otherState.CannonId = -1; CarouselRuntime.Invalidate(otherState); other.RequestRebuild(); }
                        }
                    }
                    Changed();
                }), "Choose the cannon supplied by this carousel.");
                ui.InfoField("Ammunition follows assigned cannon; choose its profile on the cannon", 2);
                if (selected < 0)
                {
                    ui.InfoField(cannons.Count == 0 ? "No installed cannon found in this turret" : "Select a cannon to calculate capacity", 2);
                    return;
                }
                var cannon = cannons[selected];
                var size = CarouselRuntime.Calculate(basket, cannon);
                var shell = cannon.shellBlueprintSlot?.Blueprint;
                if (shell == null) { ui.InfoField("Ammunition blueprint is not ready", 2); return; }
                foreach(var line in LoaderFitReadout.Carousel(size)) ui.InfoField(line,2);
                if(size.Capacity==0) ui.InfoField("Disable or unassign the carousel to use manual loading.",2);
            }
            finally { __0.EndAllDropdowns(); }
        });
    }
}


