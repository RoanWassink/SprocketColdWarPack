using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.Serialization;
using SerializationInfo = Il2CppSystem.Runtime.Serialization.SerializationInfo;

namespace SprocketFloppyParts;

[HarmonyPatch]
internal static class PersistenceHooks
{
    internal const string StateKey = "floppyPartsV1";

    private sealed class SerializationStateBag : IStateBag<Il2CppSystem.Type>
    {
        private readonly SerializationInfo state;

        internal SerializationStateBag(SerializationInfo state) => this.state = state;

        public int Count => state.MemberCount;
        public string NameAt(int index) => state.m_members[index];

        public string? ValueAt(int index)
        {
            try { return state.GetString(state.m_members[index]); }
            catch { return null; }
        }

        public void Set(int index, string key, string value, Il2CppSystem.Type type) =>
            state.UpdateValue(key, value, type);

        public void Add(string key, string value, Il2CppSystem.Type type) =>
            state.AddValue(key, value, type);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static void BeforeSave()
    {
        try
        {
            FloppyMotionController.RestoreAllAttachedChildrenToRest();
        }
        catch
        {
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(VehicleTransform), nameof(VehicleTransform.SaveState))]
    private static void BeforeSaveTransform()
    {
        try
        {
            FloppyMotionController.RestoreAllAttachedChildrenToRest();
        }
        catch
        {
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static void Save(VehicleObject __0, VehicleObjectBlueprint __result)
    {
        try
        {
            if (__result == null || __0 == null || __0.Released) return;
            var cfg = PlateConfigStore.GetForVehicle(__0);
            if (cfg.IsDefault) return;

            __result.State ??= new SerializationInfo(
                Il2CppType.Of<Il2CppSystem.Object>(),
                new Il2CppSystem.Runtime.Serialization.FormatterConverter()
                    .Cast<Il2CppSystem.Runtime.Serialization.IFormatterConverter>());

            string json = cfg.Serialize();
            StateMerge.Merge(new SerializationStateBag(__result.State), StateKey, json, Il2CppType.Of<string>());

            if (Plugin.Detailed)
                Plugin.ModLog.LogInfo(
                    $"[Persistence] Saved {StateKey} for object {__0.GetInstanceID()}: {cfg.Preset} motion={cfg.Motion} pin={cfg.PinMode} stiff={cfg.Stiffness:F2} damp={cfg.Damping:F2} wind={cfg.WindScale:F2}.");
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[Persistence] Save failed: {ex.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.LoadData))]
    private static void Load(VehicleComponent __instance, SerializationInfo __0)
    {
        try
        {
            if (__instance == null || __0 == null) return;
            if (__instance.TryCast<PlateStructure>() is not { } plate) return;

            string? json = null;
            var e = __0.GetEnumerator();
            while (e.MoveNext())
            {
                if (string.Equals(e.Name, StateKey, StringComparison.Ordinal))
                {
                    json = __0.GetString(StateKey);
                    break;
                }
            }

            if (json != null && PlateLookConfig.TryDeserialize(json, out var cfg))
            {
                PlateConfigStore.Set(plate, cfg);
                if (cfg.Motion == MotionProfile.Hinged)
                    FloppyOverlayRuntime.ProtectTransformHierarchy(plate.gameObject);
                FloppyOverlayRuntime.Note(plate, true);
                if (Plugin.Detailed)
                    Plugin.ModLog.LogInfo(
                        $"[Persistence] Loaded {StateKey} for plate {plate.VUID.Value}: {cfg.Preset} motion={cfg.Motion} pin={cfg.PinMode} stiff={cfg.Stiffness:F2} damp={cfg.Damping:F2} wind={cfg.WindScale:F2}.");
            }
            else if (json == null)
            {
                PlateConfigStore.Remove(plate);
            }
            else
            {
                Plugin.ModLog.LogWarning($"[Persistence] Ignored invalid {StateKey} JSON on plate {plate.VUID.Value}.");
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[Persistence] Load failed: {ex.Message}");
        }
    }
}
