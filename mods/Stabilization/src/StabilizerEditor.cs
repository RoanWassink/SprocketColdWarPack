using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Sprocket.Blueprints;
using Sprocket.UI;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;
using VehicleDesigner.Compartments.Design;

namespace SprocketStabilization;

/// <summary>
/// Adds one stabilizer checkbox to the traverse motor inspector (the drive):
/// - elevation for the tagged oscillating elevation ring,
/// - horizontal for an ordinary turret ring.
///
/// The checkbox enables the runtime in-game approximation of stabilization: an
/// automatic torque/rate boost on that drive so the native world-target
/// controller can follow the aim while the hull moves. It is only shown when
/// the vehicle has the dated sprocketStabilization Technology, and its value is
/// persisted through the native vehicle save path.
/// </summary>
[HarmonyPatch]
internal static class StabilizerEditor
{
    private static readonly Dictionary<IntPtr, bool> Expanded = new();
    private static int _logs;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TraverseMotorEditor), nameof(TraverseMotorEditor.OnGUI))]
    private static void TraverseMotorGui(TraverseMotorEditor __instance, IGUILayout __0)
    {
        try
        {
            if (__instance == null) return;
            TraverseMotor? motor = __instance.Component;
            if (motor == null) return;
            if (!StabilizerState.TechEnabled(motor)) return;

            TurretRing? ring = StabilizerState.FindInAncestry<TurretRing>(motor.transform);
            StabilizerAxis axis = StabilizerState.AxisOf(ring);
            if (axis == StabilizerAxis.None) return;

            string label = axis == StabilizerAxis.Elevation
                ? "Elevation drive stabilized (auto torque)"
                : "Horizontal drive stabilized (auto torque)";

            Il2CppObjectBase? blueprint = StabilizerState.BlueprintOf(motor);
            if (blueprint == null) return;

            Draw(__0, __instance.Pointer, motor, blueprint, label);
        }
        catch (Exception ex)
        {
            Warn("traverse motor editor", ex);
        }
    }

    private static void Draw(
        IGUILayout layout,
        IntPtr editorPointer,
        TraverseMotor motor,
        Il2CppObjectBase blueprint,
        string label)
    {
        var ui = layout.TryCast<IGUIElementDrawer>();
        if (ui == null) return;

        Expanded.TryGetValue(editorPointer, out bool expanded);
        if (Expanded.Count > 64) Expanded.Clear();
        layout.EndAllDropdowns();
        layout.BeginDropdown("Stabilizer — drive setup", expanded, Ui.BoolCallback(value => Expanded[editorPointer] = value));
        try
        {
            bool current = StabilizerState.GetBlueprintFlag(blueprint);
            ui.ToggleField(
                label,
                current,
                Ui.BoolCallback(value =>
                {
                    // Resolve the live blueprint identity at click time: a
                    // load/rebuild can change it between draw and click.
                    Il2CppObjectBase? liveBlueprint = StabilizerState.BlueprintOf(motor);
                    StabilizerState.SetBlueprintFlag(liveBlueprint, value);
                    MarkModified(liveBlueprint);
                }),
                "Stabilizer: boosts this drive's torque while the native world-target controller aims and "
                + "applies the configured realistic rate/acceleration limits. Saved inside the motor blueprint "
                + "object-state, so it survives design save/load; restored when unchecked.");
            ui.InfoField("Stabilizer-limieten: traverse 24°/s, elevatie 6°/s, responstijd 0,75 s (config).", 2);
        }
        finally
        {
            layout.EndAllDropdowns();
        }
    }

    private static void MarkModified(Il2CppObjectBase? blueprint)
    {
        try { blueprint?.TryCast<Blueprint>()?.MarkModified(); }
        catch { }
    }

    internal static void ResetForUnload()
    {
        Expanded.Clear();
    }

    private static void Warn(string area, Exception ex)
    {
        if (_logs >= 10) return;
        _logs++;
        Plugin.ModLog.LogWarning("[Stabilizer] " + area + ": " + ex.GetType().Name + ": " + ex.Message);
    }
}
