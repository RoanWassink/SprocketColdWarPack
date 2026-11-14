using System;
using HarmonyLib;
using Il2CppSystem.Runtime.Serialization;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;

namespace SprocketStabilization;

/// <summary>
/// Legacy component-state persistence for runtime vehicle state. It is NOT the
/// blueprint design route: the native outer ToBlueprint path bypasses
/// VehicleComponent.SaveData, which is why the stabilizer checkbox is persisted
/// by StabilizerBlueprintPersistence through the actual blueprint object-state.
/// </summary>
[HarmonyPatch]
internal static class StabilizerPersistence
{
    private const string ReleaseMethodName = "ReleaseInternal";

    private static int _warnings;

    // --- Legacy component state route (runtime vehicle state, not blueprints) -

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.SaveData))]
    private static void SaveDataPostfix(VehicleComponent __instance, SerializationInfo __0)
    {
        try
        {
            if (__instance == null || __0 == null) return;
            if (!IsDrive(__instance)) return;

            StabilizerKey key = StabilizerState.KeyOf(__instance);
            bool value = StabilizerState.GetFlag(key);
            __0.AddValue(StabilizerState.SaveKey, value);
        }
        catch (Exception ex)
        {
            Warn("save", ex);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.LoadData))]
    private static void LoadDataPostfix(VehicleComponent __instance, SerializationInfo __0)
    {
        try
        {
            if (__instance == null) return;
            if (!IsDrive(__instance)) return;

            StabilizerKey key = StabilizerState.KeyOf(__instance);
            if (!key.IsValid) return;

            bool value = false;
            try
            {
                // Missing key or a malformed/wrong-typed value means false.
                if (__0 != null) value = __0.GetBoolean(StabilizerState.SaveKey);
            }
            catch
            {
                value = false;
            }

            // The completed load establishes this component's current identity.
            // Drop any stale value for the same live component (for example a
            // pre-load VUID) and store the authoritative result.
            StabilizerState.ClearComponent(key);
            StabilizerState.SetFlag(key, value);
        }
        catch (Exception ex)
        {
            Warn("load", ex);
        }
    }

    // A prefix on the non-public native release entry point removes our state
    // while the component identity fields are still attached. String-based
    // target so the mod does not depend on the method's accessibility.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(VehicleComponent), ReleaseMethodName)]
    private static void ReleaseInternalPrefix(VehicleComponent __instance)
    {
        try
        {
            if (__instance == null) return;
            StabilizerKey key = StabilizerState.KeyOf(__instance);
            if (!key.IsValid) return;
            StabilizerState.ClearComponent(key);
        }
        catch (Exception ex)
        {
            // Never block the native release because cleanup failed.
            Warn("release", ex);
        }
    }

    private static bool IsDrive(VehicleComponent component)
    {
        if (component == null) return false;
        return component.TryCast<TurretRing>() != null ||
               component.TryCast<TraverseMotor>() != null ||
               component.TryCast<LayingDrive>() != null;
    }

    private static void Warn(string area, Exception ex)
    {
        if (_warnings >= 20) return;
        _warnings++;
        Plugin.ModLog.LogWarning("[Stabilizer] " + area + ": " + ex.GetType().Name + ": " + ex.Message);
    }
}
