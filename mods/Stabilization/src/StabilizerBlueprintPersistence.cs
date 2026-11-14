using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Sprocket.Blueprints;
using Sprocket.Motors;
using Sprocket.Vehicles;

namespace SprocketStabilization;

/// <summary>
/// Primary design persistence: the native outer blueprint ToBlueprint path
/// bypasses VehicleComponent.SaveData, so the checkbox is stored in the motor
/// blueprint's object-state as a namespaced string entry. This is the same
/// route other mods on Sprocket 0.2.56.2 use successfully.
///
/// This class is a separate Harmony owner from the boost, editor and legacy
/// component-state owners: in 0.7.0 a single bad postfix signature (Clone
/// returning Il2CppSystem.Object while the postfix declared Blueprint) made
/// Harmony's PatchAll fail and silently disabled every persistence hook. The
/// live log showed:
///   Failed to patch virtual Il2CppSystem.Object Sprocket.Blueprints.Blueprint::Clone():
///   Cannot assign method return type Il2CppSystem.Object to __result type Sprocket.Blueprints.Blueprint
/// so the Clone postfix now takes Il2CppSystem.Object and casts it.
/// </summary>
[HarmonyPatch]
internal static class StabilizerBlueprintPersistence
{
    private static int _warnings;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MotorBlueprint), nameof(MotorBlueprint.Save))]
    private static void MotorSavePostfix(MotorBlueprint __instance, IBlueprintSerializer __0)
        => SaveBlueprint(__instance, __0);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TraverseMotorBlueprint), nameof(TraverseMotorBlueprint.Save))]
    private static void TraverseMotorSavePostfix(TraverseMotorBlueprint __instance, IBlueprintSerializer __0)
        => SaveBlueprint(__instance, __0);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MotorBlueprint), nameof(MotorBlueprint.Load))]
    private static void MotorLoadPostfix(MotorBlueprint __instance, IBlueprintDeserializer __0)
        => LoadBlueprint(__instance, __0);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TraverseMotorBlueprint), nameof(TraverseMotorBlueprint.Load))]
    private static void TraverseMotorLoadPostfix(TraverseMotorBlueprint __instance, IBlueprintDeserializer __0)
        => LoadBlueprint(__instance, __0);

    // Structured duplication/instantiation must carry the flag to the new
    // blueprint identity. Copy is a static two-argument native method.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Blueprint), nameof(Blueprint.Copy))]
    private static void BlueprintCopyPostfix(Blueprint __0, Blueprint __1)
        => StabilizerState.CopyBlueprintFlag(__0, __1);

    // Clone is virtual and its interop signature returns Il2CppSystem.Object.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Blueprint), nameof(Blueprint.Clone))]
    private static void BlueprintClonePostfix(Blueprint __instance, Il2CppSystem.Object __result)
    {
        try { StabilizerState.CopyBlueprintFlag(__instance, __result?.TryCast<Blueprint>()); }
        catch (Exception ex) { Warn("blueprint clone", ex); }
    }

    private static void SaveBlueprint(Il2CppObjectBase? blueprint, IBlueprintSerializer? serializer)
    {
        try
        {
            if (blueprint == null || serializer == null) return;
            if (!StabilizerState.GetBlueprintFlag(blueprint)) return;
            serializer.AddValue(StabilizerState.BlueprintSaveKey, "1");
        }
        catch (Exception ex)
        {
            // A duplicate key from a nested override save is harmless; the first
            // write already preserved the value.
            Warn("blueprint save", ex);
        }
    }

    private static void LoadBlueprint(Il2CppObjectBase? blueprint, IBlueprintDeserializer? deserializer)
    {
        if (blueprint == null) return;
        bool value = false;
        try
        {
            if (deserializer != null)
            {
                try { value = deserializer.GetString(StabilizerState.BlueprintSaveKey) == "1"; }
                catch { value = false; }
            }
        }
        catch (Exception ex)
        {
            Warn("blueprint load", ex);
            value = false;
        }

        StabilizerState.SetBlueprintFlag(blueprint, value);
    }

    private static void Warn(string area, Exception ex)
    {
        if (_warnings >= 20) return;
        _warnings++;
        Plugin.ModLog.LogWarning("[Stabilizer] " + area + ": " + ex.GetType().Name + ": " + ex.Message);
    }
}
