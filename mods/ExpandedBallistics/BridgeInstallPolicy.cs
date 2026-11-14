namespace SprocketExpandedBallistics;

internal enum BridgeInstallAction { Wait, Install, Disable }

/// <summary>
/// A missing DynamicSight assembly is transient (it may load after Ballistics), so the
/// bridge must wait and retry. Once the assembly is present with an incompatible shape,
/// the version-locked adapter is disabled rather than patched speculatively.
/// </summary>
internal static class BridgeInstallPolicy
{
    internal static BridgeInstallAction Decide(bool typesPresent, bool shapeValid, bool installed)
    {
        if (installed) return BridgeInstallAction.Install;
        if (!typesPresent) return BridgeInstallAction.Wait;
        return shapeValid ? BridgeInstallAction.Install : BridgeInstallAction.Disable;
    }
}
