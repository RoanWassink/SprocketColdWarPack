using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;

namespace SprocketFloppyParts;

internal static class PlateConfigStore
{
    private static readonly Dictionary<IntPtr, PlateLookConfig> byPlate = new();
    private static readonly Dictionary<IntPtr, PlateLookConfig> byVehicleObject = new();
    private static readonly Dictionary<IntPtr, IntPtr> plateToVehicle = new();

    internal static long Revision { get; private set; }

    internal static PlateLookConfig Get(PlateStructure? plate)
    {
        if (plate == null) return PlateLookConfig.DefaultsFor(LookPreset.Native);
        if (byPlate.TryGetValue(plate.Pointer, out var cfg)) return cfg.Clone();

        try
        {
            var vo = plate.VehicleObject;
            if (vo != null && byVehicleObject.TryGetValue(vo.Pointer, out var vCfg))
            {
                byPlate[plate.Pointer] = vCfg;
                plateToVehicle[plate.Pointer] = vo.Pointer;
                return vCfg.Clone();
            }
        }
        catch
        {
        }

        if (Plugin.AutoAdoptExpandedBallisticsLooks?.Value == true)
        {
            LookPreset? adopted = TryMapExpandedBallisticsId(plate.armourTechID);
            if (adopted.HasValue)
                return PlateLookConfig.DefaultsFor(adopted.Value);
        }

        return PlateLookConfig.DefaultsFor(LookPreset.Native);
    }

    internal static PlateLookConfig GetForVehicle(VehicleObject? vehicleObject)
    {
        if (vehicleObject == null) return PlateLookConfig.DefaultsFor(LookPreset.Native);
        if (byVehicleObject.TryGetValue(vehicleObject.Pointer, out var cfg)) return cfg.Clone();

        try
        {
            var components = vehicleObject.Components;
            if (components != null)
            {
                int count = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for (int i = 0; i < count; i++)
                {
                    if (components[i]?.TryCast<PlateStructure>() is { } plate &&
                        byPlate.TryGetValue(plate.Pointer, out var pCfg))
                    {
                        byVehicleObject[vehicleObject.Pointer] = pCfg;
                        plateToVehicle[plate.Pointer] = vehicleObject.Pointer;
                        return pCfg.Clone();
                    }
                }
            }
        }
        catch
        {
        }

        return PlateLookConfig.DefaultsFor(LookPreset.Native);
    }

    internal static void Set(PlateStructure plate, PlateLookConfig config)
    {
        if (plate == null) return;
        var clean = config.Clone().Sanitize();
        if (clean.IsDefault)
        {
            Remove(plate);
            return;
        }

        byPlate[plate.Pointer] = clean;
        try
        {
            var vo = plate.VehicleObject;
            if (vo != null)
            {
                byVehicleObject[vo.Pointer] = clean;
                plateToVehicle[plate.Pointer] = vo.Pointer;
            }
        }
        catch
        {
        }
        Revision++;
    }

    internal static void Remove(PlateStructure plate)
    {
        if (plate == null) return;
        bool changed = byPlate.Remove(plate.Pointer);
        if (plateToVehicle.Remove(plate.Pointer, out var vPtr))
            changed |= byVehicleObject.Remove(vPtr);

        try
        {
            var vo = plate.VehicleObject;
            if (vo != null)
                changed |= byVehicleObject.Remove(vo.Pointer);
        }
        catch
        {
        }

        if (changed) Revision++;
    }

    internal static void Clear()
    {
        byPlate.Clear();
        byVehicleObject.Clear();
        plateToVehicle.Clear();
        Revision++;
    }

    private static LookPreset? TryMapExpandedBallisticsId(string? id) => id switch
    {
        "ebFabric" => LookPreset.Canvas,
        "ebRubber" => LookPreset.Rubber,
        "ebWood" => LookPreset.Wood,
        "ebGlass" => LookPreset.Glass,
        _ => null
    };
}
