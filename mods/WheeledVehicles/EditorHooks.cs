using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Tracks;
using Sprocket.Vehicles.Tracks.Editor;
using UnityEngine;

namespace SprocketWheeledVehicles;

[HarmonyPatch]
internal static class EditorHooks
{
    internal const string StockAirTyreWheelGuid = "864e251d-8eac-471d-b70c-f030d25f8c87";
    private static readonly Color HintColour = new(0.74f, 0.78f, 0.82f);
    private static bool _expanded = true;

    [HarmonyPostfix, HarmonyPatch(typeof(TrackEditor), nameof(TrackEditor.OnGUI))]
    private static void Draw(TrackEditor __instance, IGUILayout __0)
    {
        if (!Plugin.Enabled.Value) return;
        if (!TrackEditor.trackShown && !TrackEditor.suspensionShown && !TrackEditor.wheelShown && !TrackEditor.beltShown)
            return;

        try
        {
            var track = __instance.Component;
            var blueprint = __instance.blueprint;
            var ui = __0?.TryCast<IGUIElementDrawer>();
            if (track == null || blueprint == null || ui == null) return;
            var vehicle = track.Vehicle;
            if (vehicle == null) return;

            bool isWheeledPart = WheeledPart.IsWheeledPartSelected(track);
            bool isHpsPart = WheeledPart.IsHpsSelected(track);
            var settings = WheeledProfiles.Get(blueprint);

            // Show the panel when Wheeled Suspension is selected, when Hydropneumatic (HPS) is selected
            // (so HPS + Wheeled can be combined), or when an older blueprint still has wheeled mode enabled.
            if (!isWheeledPart && !isHpsPart && !settings.Enabled)
                return;

            if (isWheeledPart && !settings.Enabled)
                settings.Enabled = true;

            __0!.EndAllDropdowns();
            string header = isHpsPart ? "Wheeled Vehicle (HPS Compatible)" : "Wheeled Vehicle";
            __0.BeginDropdown(header, _expanded,
                DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v => _expanded = v)));
            try
            {
                if (!isWheeledPart)
                {
                    string toggleLabel = isHpsPart
                        ? "Wheeled mode (combine with HPS)"
                        : "Wheeled vehicle mode";
                    string toggleTip = isHpsPart
                        ? "Uses true raycast wheel physics & steering while keeping live HPS ride-height, pitch and roll controls."
                        : "Tip: select the dedicated 'Wheeled' suspension part in the Suspension picker, or uncheck here to return to tracks.";

                    ui.ToggleField(toggleLabel, settings.Enabled,
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(enabled =>
                        {
                            var current = WheeledProfiles.Get(__instance.blueprint);
                            if (enabled == current.Enabled) return;
                            var next = current.Copy();
                            next.Enabled = enabled;
                            Apply(__instance, vehicle, next, redraw: true, updateVisibility: true);
                        })),
                        toggleTip);

                    if (!settings.Enabled)
                    {
                        ui.InfoField(isHpsPart
                            ? "Off: standard tracked Hydropneumatic suspension."
                            : "Off: normal Sprocket tracked vehicle.", 1, HintColour);
                        return;
                    }
                }

                int axleCount = EstimateAxleCount(track);
                string layoutLabel = axleCount > 0
                    ? $"Active layout: {settings.DescribeDriveLayout(axleCount)} ({axleCount} axles)" + (isHpsPart ? " + HPS" : "")
                    : "Place at least 2 roadwheel axles per side.";
                ui.InfoField(layoutLabel, 1, HintColour);

                // Quick layout presets (4x4, 6x4, 6x6, 8x4, 8x8, 10x10)
                ui.Header("Quick presets");
                PresetButton(ui, __instance, vehicle, "4x4", 2,
                    new[] { true, true },
                    new[] { AxleSteerMode.Front, AxleSteerMode.None });
                PresetButton(ui, __instance, vehicle, "6x4", 3,
                    new[] { false, true, true },
                    new[] { AxleSteerMode.Front, AxleSteerMode.None, AxleSteerMode.None });
                PresetButton(ui, __instance, vehicle, "6x6", 3,
                    new[] { true, true, true },
                    new[] { AxleSteerMode.Front, AxleSteerMode.None, AxleSteerMode.None });
                PresetButton(ui, __instance, vehicle, "8x4", 4,
                    new[] { false, false, true, true },
                    new[] { AxleSteerMode.Front, AxleSteerMode.Front, AxleSteerMode.None, AxleSteerMode.None });
                PresetButton(ui, __instance, vehicle, "8x8", 4,
                    new[] { true, true, true, true },
                    new[] { AxleSteerMode.Front, AxleSteerMode.Front, AxleSteerMode.None, AxleSteerMode.None });
                PresetButton(ui, __instance, vehicle, "10x10", 5,
                    new[] { true, true, true, true, true },
                    new[] { AxleSteerMode.Front, AxleSteerMode.Front, AxleSteerMode.None, AxleSteerMode.None, AxleSteerMode.Rear });

                // Optional air-tyre shortcut (wheel selection stays completely free)
                var tyreTip = new UITooltip("Use air tyre wheel",
                    "Optional shortcut: assigns Sprocket's stock rubber air-tyre wheel to the roadwheels. You remain free to pick any wheel.");
                ui.Button("Use air tyre wheel",
                    DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>((Action)(() =>
                        AssignAirTyreWheel(__instance, vehicle))),
                    ref tyreTip);

                // Per-axle drive and steering controls
                if (axleCount > 0)
                {
                    ui.Header("Per-axle drive & steering");
                    int shownAxles = Math.Min(axleCount, 8);
                    for (int a = 0; a < shownAxles; a++)
                    {
                        int axleIdx = a;
                        var cfg = settings.GetEffectiveAxleConfig(axleIdx, axleCount);
                        string posTag = axleIdx == 0 ? "Front" : (axleIdx == axleCount - 1 ? "Rear" : "Mid");

                        ui.ToggleField($"Axle {axleIdx + 1} ({posTag}) driven", cfg.Driven,
                            DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(driven =>
                            {
                                var next = WheeledProfiles.Get(__instance.blueprint).Copy();
                                next.EnsureAxleCount(axleCount);
                                next.Axles[axleIdx].Driven = driven;
                                Apply(__instance, vehicle, next, redraw: true, updateVisibility: false);
                            })),
                            "Toggle whether engine torque drives this axle.");

                        string modeName = cfg.SteerMode switch
                        {
                            AxleSteerMode.Front => "Front steer",
                            AxleSteerMode.Rear => "Rear steer",
                            _ => "Fixed (no steer)"
                        };
                        var steerTip = new UITooltip($"Axle {axleIdx + 1} steering",
                            "Click to cycle between Fixed, Front steer and Rear steer (counter-steers at low speed).");
                        ui.Button($"Axle {axleIdx + 1} steer: {modeName}",
                            DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>((Action)(() =>
                            {
                                var next = WheeledProfiles.Get(__instance.blueprint).Copy();
                                next.EnsureAxleCount(axleCount);
                                next.Axles[axleIdx].SteerMode = next.Axles[axleIdx].SteerMode switch
                                {
                                    AxleSteerMode.None => AxleSteerMode.Front,
                                    AxleSteerMode.Front => AxleSteerMode.Rear,
                                    _ => AxleSteerMode.None
                                };
                                next.RearSteerEnabled = next.Axles.Any(x => x.SteerMode == AxleSteerMode.Rear);
                                Apply(__instance, vehicle, next, redraw: true, updateVisibility: false);
                            })),
                            ref steerTip);
                    }
                }

                // Steering & rear-wheel steering settings
                ui.Header("Steering & tyres");
                ui.ToggleField("Ackermann steering", settings.AckermannEnabled,
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v =>
                    {
                        var next = WheeledProfiles.Get(__instance.blueprint).Copy();
                        next.AckermannEnabled = v;
                        Apply(__instance, vehicle, next, redraw: false, updateVisibility: false);
                    })),
                    "Turns the inside wheel sharper than the outside wheel so tyres do not scrub in turns.");

                Slider(ui, __instance, vehicle, "Max steer angle (°)", settings.MaxSteerAngleDeg,
                    WheeledSettings.MinSteerAngleDeg, WheeledSettings.MaxSteerAngleDegLimit,
                    (s, v) => s.MaxSteerAngleDeg = v);

                if (settings.HasAnyRearSteer(Math.Max(2, axleCount)))
                {
                    Slider(ui, __instance, vehicle, "Rear steer max (°)", settings.RearSteerMaxAngleDeg,
                        4f, 25f, (s, v) => s.RearSteerMaxAngleDeg = v);

                    Slider(ui, __instance, vehicle, "Rear steer switch (km/h)", settings.RearSteerTransitionKmh,
                        10f, 60f, (s, v) => s.RearSteerTransitionKmh = v);

                    string hsLabel = settings.RearSteerHighSpeed == RearSteerHighSpeedMode.SameDirection
                        ? "High-speed rear steer: Same direction"
                        : "High-speed rear steer: Off (straight)";
                    var hsTip = new UITooltip("High-speed rear steering",
                        "Below transition speed, rear axles counter-steer for a tight turning circle. Above it, choose Off (straight) or Same direction (stability).");
                    ui.Button(hsLabel,
                        DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>((Action)(() =>
                        {
                            var next = WheeledProfiles.Get(__instance.blueprint).Copy();
                            next.RearSteerHighSpeed = next.RearSteerHighSpeed == RearSteerHighSpeedMode.Off
                                ? RearSteerHighSpeedMode.SameDirection
                                : RearSteerHighSpeedMode.Off;
                            Apply(__instance, vehicle, next, redraw: true, updateVisibility: false);
                        })),
                        ref hsTip);
                }

                Slider(ui, __instance, vehicle, "Tyre grip", settings.TireGrip,
                    WheeledSettings.MinGrip, WheeledSettings.MaxGrip,
                    (s, v) => s.TireGrip = v);

                Slider(ui, __instance, vehicle, "Suspension firmness", settings.SuspensionStiffness,
                    WheeledSettings.MinSuspension, WheeledSettings.MaxSuspension,
                    (s, v) => s.SuspensionStiffness = v);

                Slider(ui, __instance, vehicle, "Suspension damping", settings.SuspensionDamping,
                    WheeledSettings.MinSuspension, WheeledSettings.MaxSuspension,
                    (s, v) => s.SuspensionDamping = v);

                Slider(ui, __instance, vehicle, "Down travel (mm)", settings.DownTravelMeters * 1000f,
                    WheeledSettings.MinDownTravel * 1000f, WheeledSettings.MaxDownTravel * 1000f,
                    (s, v) => s.DownTravelMeters = v * 0.001f);

                Slider(ui, __instance, vehicle, "Up travel (mm)", settings.UpTravelMeters * 1000f,
                    WheeledSettings.MinUpTravel * 1000f, WheeledSettings.MaxUpTravel * 1000f,
                    (s, v) => s.UpTravelMeters = v * 0.001f);

                // Visibility options
                ui.Header("Visuals");
                ui.ToggleField("Hide track belt", settings.HideTrackBelt,
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v =>
                    {
                        var next = WheeledProfiles.Get(__instance.blueprint).Copy();
                        next.HideTrackBelt = v;
                        Apply(__instance, vehicle, next, redraw: false, updateVisibility: true);
                    })),
                    "Hides the visual track belt in designer and battle.");

                ui.ToggleField("Hide sprocket / idler / rollers", settings.HideEndWheels,
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v =>
                    {
                        var next = WheeledProfiles.Get(__instance.blueprint).Copy();
                        next.HideEndWheels = v;
                        Apply(__instance, vehicle, next, redraw: false, updateVisibility: true);
                    })),
                    "Hides the drive sprocket, idler and return rollers while keeping the drivetrain intact.");

                var resetTip = new UITooltip("Reset wheeled defaults",
                    "Resets axle overrides, steering angle, tyre grip and suspension tuning to defaults.");
                ui.Button("Reset wheeled defaults",
                    DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>((Action)(() =>
                        Apply(__instance, vehicle, new WheeledSettings { Enabled = true }, redraw: true, updateVisibility: true))),
                    ref resetTip);
            }
            finally
            {
                __0.EndDropdown();
            }
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("Track editor UI", ex);
        }
    }

    private static void PresetButton(
        IGUIElementDrawer ui,
        TrackEditor editor,
        IVehicleGateway vehicle,
        string label,
        int axleCount,
        bool[] driven,
        AxleSteerMode[] steer)
    {
        var tip = new UITooltip($"Preset {label}",
            $"Sets {axleCount} axles per side and applies standard {label} drive and steering layout.");
        ui.Button(label, DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>((Action)(() =>
        {
            var next = WheeledProfiles.Get(editor.blueprint).Copy();
            next.Enabled = true;
            next.ApplyPreset(axleCount, driven, steer);
            ApplyPresetAxles(editor, vehicle, axleCount, next);
        })), ref tip);
    }

    private static void ApplyPresetAxles(TrackEditor editor, IVehicleGateway vehicle, int axleCount, WheeledSettings settings)
    {
        try
        {
            var objects = vehicle.ObjectReader.Items;
            int objectCount = objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for (int i = 0; i < objectCount; i++)
            {
                var components = objects[i].Components;
                int componentCount = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for (int j = 0; j < componentCount; j++)
                {
                    var track = components[j].TryCast<TrackAssembly>();
                    if (track?.BlueprintSlot?.Blueprint is not { } bp) continue;
                    WheeledProfiles.Set(bp, settings.Copy());
                    track.BlueprintSlot.MarkModified();

                    var arrSlot = track.Suspensions?.BlueprintSlot;
                    var arrBp = arrSlot?.Blueprint;
                    if (arrBp != null)
                    {
                        arrBp.SpacingMode = SpacingMode.Uniform;
                        arrBp.Count = axleCount;
                        arrSlot!.MarkModified();
                        track.Build();
                    }
                    VisualHooks.ApplyTrackAssemblyVisibility(track, allowRestore: true);
                }
            }
            editor.RequestRedraw();
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("Apply axle preset", ex);
        }
    }

    private static int EstimateAxleCount(TrackAssembly track)
    {
        try
        {
            var susp = track.Suspensions;
            if (susp != null)
            {
                int count = susp.AxleCount;
                if (count > 0) return count;
                if (susp.AssemblyCount > 0) return susp.AssemblyCount;
            }
        }
        catch { }
        return 0;
    }

    private static void Slider(
        IGUIElementDrawer ui,
        TrackEditor editor,
        IVehicleGateway vehicle,
        string label,
        float value,
        float min,
        float max,
        Action<WheeledSettings, float> setter)
    {
        ui.Slider(label, value, min, max, DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>((Action<float>)(v =>
        {
            var next = WheeledProfiles.Get(editor.blueprint).Copy();
            setter(next, Math.Clamp(v, min, max));
            // Do not RequestRedraw during slider drag (prevents native Property.EndEdit exception).
            Apply(editor, vehicle, next, redraw: false, updateVisibility: false);
        })));
    }

    private static void Apply(TrackEditor editor, IVehicleGateway vehicle, WheeledSettings settings, bool redraw, bool updateVisibility)
    {
        if (!settings.IsValid()) return;

        var objects = vehicle.ObjectReader.Items;
        int objectCount = objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for (int i = 0; i < objectCount; i++)
        {
            var components = objects[i].Components;
            int componentCount = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for (int j = 0; j < componentCount; j++)
            {
                var track = components[j].TryCast<TrackAssembly>();
                if (track?.BlueprintSlot?.Blueprint is not { } bp) continue;
                WheeledProfiles.Set(bp, settings.Copy());
                track.BlueprintSlot.MarkModified();
                if (updateVisibility)
                    VisualHooks.ApplyTrackAssemblyVisibility(track, allowRestore: true);
            }
        }

        if (redraw)
            editor.RequestRedraw();
    }

    private static void AssignAirTyreWheel(TrackEditor editor, IVehicleGateway vehicle)
    {
        try
        {
            var objects = vehicle.ObjectReader.Items;
            int objectCount = objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            bool changed = false;
            for (int i = 0; i < objectCount; i++)
            {
                var components = objects[i].Components;
                int componentCount = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for (int j = 0; j < componentCount; j++)
                {
                    var track = components[j].TryCast<TrackAssembly>();
                    var wheelSlot = track?.Suspensions?.SharedWheelBlueprint;
                    var wheelBp = wheelSlot?.Blueprint;
                    if (wheelBp == null) continue;
                    wheelBp.Assign(StockAirTyreWheelGuid);
                    wheelSlot!.MarkModified();
                    track!.Build();
                    VisualHooks.ApplyTrackAssemblyVisibility(track, allowRestore: true);
                    changed = true;
                }
            }
            if (changed)
            {
                Plugin.Diag(() => "Assigned stock air tyre wheel to roadwheels.");
                editor.RequestRedraw();
            }
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("Assign air tyre wheel", ex);
        }
    }
}
