namespace SprocketEngineExpansion.Core;

public enum PowerpackDriveshaftCollisionMode { None = 0, ConnectedOnly = 1, SamePowertrain = 2 }

public readonly record struct PowerpackMember(IntPtr Powertrain, int GroupKey, int ModuleId);

/// <summary>
/// Pure collision-filtering rules for active powerpack assemblies.
/// Internal engine/transmission/driveshaft overlaps inside the powerpack are ignored while
/// all external collisions (hull, turret, crew, ammunition, fuel, other engines) stay native.
/// </summary>
public static class PowerpackCollisionRules
{
    public const byte LayerEngine = 3;
    public const byte LayerTransmission = 4;
    public const byte LayerDriveshaft = 5;

    public static bool IsPowertrainLayer(byte layer) =>
        layer is LayerEngine or LayerTransmission or LayerDriveshaft;

    public static bool ShouldIgnore(
        byte layerA, IntPtr ownerA,
        byte layerB, IntPtr ownerB,
        Func<IntPtr, PowerpackMember?> lookupMember,
        Func<IntPtr, IntPtr?> lookupShaftPowertrain,
        Func<bool, (int Provider, int Receiver)?> lookupShaftEndpoints,
        Func<IntPtr, int, IReadOnlySet<int>?> lookupGroupModules,
        bool ignoreInternal,
        PowerpackDriveshaftCollisionMode driveshaftMode)
    {
        if (!IsPowertrainLayer(layerA) || !IsPowertrainLayer(layerB)) return false;
        if (ownerA == IntPtr.Zero || ownerB == IntPtr.Zero) return false;

        if (layerA != LayerDriveshaft && layerB != LayerDriveshaft)
        {
            if (!ignoreInternal) return false;
            return lookupMember(ownerA) is { } a
                && lookupMember(ownerB) is { } b
                && a.Powertrain == b.Powertrain
                && a.GroupKey == b.GroupKey;
        }

        if (layerA == LayerDriveshaft && layerB == LayerDriveshaft) return false;
        if (driveshaftMode == PowerpackDriveshaftCollisionMode.None) return false;

        bool shaftIsA = layerA == LayerDriveshaft;
        IntPtr shaftOwner = shaftIsA ? ownerA : ownerB;
        IntPtr blockOwner = shaftIsA ? ownerB : ownerA;

        if (lookupMember(blockOwner) is not { } member) return false;
        if (lookupShaftPowertrain(shaftOwner) is not { } shaftPowertrain || shaftPowertrain != member.Powertrain) return false;
        if (driveshaftMode == PowerpackDriveshaftCollisionMode.SamePowertrain) return true;

        if (driveshaftMode == PowerpackDriveshaftCollisionMode.ConnectedOnly)
        {
            if (lookupShaftEndpoints(shaftIsA) is not { } ep) return false;
            var groupMods = lookupGroupModules(member.Powertrain, member.GroupKey);
            return groupMods != null && (groupMods.Contains(ep.Provider) || groupMods.Contains(ep.Receiver));
        }

        return false;
    }
}
