using HarmonyLib;
using Sprocket;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Serialization;
using Sprocket.Vehicles.Weapons.Cannons;
using SerializationInfo = Il2CppSystem.Runtime.Serialization.SerializationInfo;
namespace SprocketTelescopicMast;

// Fire detection reads the native ProjectileRegister.nextID once before and once after
// FireInternal. That counter only moves when Launch created a projectile, so a cancelled
// launch leaves it unchanged. Both callbacks take only the class instance: no __result, no
// struct/by-ref parameters and no finalizer. Every hook declares class parameters
// (__instance, __0, __result). Each group has its own Harmony owner so an optional failure
// never disables the mast or the other groups.

[HarmonyPatch]
internal static class HingeCoreHooks
{
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void BeforeBuild(VehicleObjectModel __instance)
    { try { HingeRuntime.Prepare(__instance); RotatorRuntime.Prepare(__instance); } catch (Exception ex) { HingeRuntime.Warn("Mesh preparation", ex); } }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void AfterBuild(VehicleObjectModel __instance)
    { try { HingeRuntime.Prepare(__instance); HingeRuntime.Balance(__instance); RotatorRuntime.Prepare(__instance); RotatorRuntime.Balance(__instance); } catch (Exception ex) { HingeRuntime.Warn("Mass/cost", ex); } }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.OnModelLoaded))]
    private static void Loaded(VehicleObjectModel __instance)
    { try { if (HingeRuntime.Own(__instance) || RotatorRuntime.Own(__instance)) { HingeRuntime.Prepare(__instance); RotatorRuntime.Prepare(__instance); __instance.ForceRebuild(); } } catch (Exception ex) { HingeRuntime.Warn("Model loaded", ex); } }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.EnablePhysics))]
    private static void Physics(VehicleObject __instance)
    { try { HingeRuntime.CaptureRest(__instance); RotatorRuntime.CaptureRest(__instance); } catch (Exception ex) { HingeRuntime.Warn("Rest pose", ex); } }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.DisablePhysics))]
    private static void Designer(VehicleObject __instance)
    { try { HingeRuntime.Designer(__instance); RotatorRuntime.Restore(__instance, false); } catch (Exception ex) { HingeRuntime.Warn("Return to designer", ex); } }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleTransform), nameof(VehicleTransform.SaveState))]
    private static void SaveState(VehicleTransform __instance)
    { try { HingeRuntime.Restore(__instance.VehicleObject, "SaveState", true); RotatorRuntime.Restore(__instance.VehicleObject, true); } catch (Exception ex) { HingeRuntime.Warn("Save pose", ex); } }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static void BeforeBlueprint(VehicleObject __0)
    { try { HingeRuntime.Restore(__0, "ToBlueprint", true); RotatorRuntime.Restore(__0, true); } catch (Exception ex) { HingeRuntime.Warn("Blueprint pose", ex); } }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static void Save(VehicleObject __0, VehicleObjectBlueprint __result)
    { try { HingeRuntime.Save(__0, __result); } catch (Exception ex) { HingeRuntime.Warn("Save I/O config", ex); } }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.LoadData))]
    private static void LoadData(VehicleComponent __instance, SerializationInfo __0)
    {
        try { if (HingeRuntime.FlowComponent(__instance.ComponentID)) HingeRuntime.Load(__instance.VehicleObject, __0, "LoadData"); }
        catch (Exception ex) { HingeRuntime.Warn("Load I/O config", ex); }
    }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.Release))]
    private static void Release(VehicleObject __instance) { try { HingeRuntime.Remove(__instance); RotatorRuntime.Remove(__instance); } catch { } }
}

/// <summary>Backup reader: the transform state of the same object (mirrors the proven SaveState hook).</summary>
[HarmonyPatch]
internal static class HingeLoadBackupHooks
{
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleTransform), nameof(VehicleTransform.LoadState))]
    private static void LoadState(VehicleTransform __instance, SerializationInfo __0)
    {
        try { var obj = __instance.VehicleObject; if (HingeRuntime.IsMoving(obj) || RotatorRuntime.IsPart(obj) || HingeRuntime.IsExternalPart(obj)) HingeRuntime.Load(obj, __0, "LoadState"); }
        catch (Exception ex) { HingeRuntime.Warn("Load I/O config (transform)", ex); }
    }
}

[HarmonyPatch]
internal static class HingeFireHooks
{
    // Prefix/postfix pair on the fire path. `nextID` is read from the shared ProjectileRegister
    // (through CannonBehaviour.projectileFactory) before and after the call; a change is a real
    // projectile, not a launch that another mod cancelled. No return value is touched.
    [HarmonyPrefix, HarmonyPatch(typeof(CannonBehaviour), nameof(CannonBehaviour.FireInternal))]
    private static void BeforeFire(CannonBehaviour __instance)
    { try { HingeFire.BeginFire(__instance); } catch (Exception ex) { HingeRuntime.Warn("Fire start", ex); } }

    [HarmonyPostfix, HarmonyPatch(typeof(CannonBehaviour), nameof(CannonBehaviour.FireInternal))]
    private static void AfterFire(CannonBehaviour __instance)
    { try { HingeFire.EndFire(__instance); } catch (Exception ex) { HingeRuntime.Warn("Fire event", ex); } }

    [HarmonyPostfix, HarmonyPatch(typeof(Cannon), nameof(Cannon.Build))]
    private static void Built(Cannon __instance) { try { HingeCannons.Track(__instance); } catch (Exception ex) { HingeRuntime.Warn("Cannon register", ex); } }
    [HarmonyPostfix, HarmonyPatch(typeof(Cannon), nameof(Cannon.OnVehicleBuilt))]
    private static void VehicleBuilt(Cannon __instance) { try { HingeCannons.Track(__instance); } catch (Exception ex) { HingeRuntime.Warn("Cannon register", ex); } }
    [HarmonyPrefix, HarmonyPatch(typeof(Cannon), nameof(Cannon.Release))]
    private static void Released(Cannon __instance) { try { HingeCannons.Forget(__instance); } catch { } }
}

