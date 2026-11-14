namespace SprocketEngineExpansion.Core;
public static class StartupSafety
{
    public const string AuditedHash="3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B";
    public static bool SafeMode(string hash)=>!string.Equals(hash,AuditedHash,StringComparison.OrdinalIgnoreCase);
    // An unknown binary has no proven native addresses or lengths: install zero patches.
    public static bool InstallHooks(string hash)=>!SafeMode(hash);
    public const string Warning="Engine Expansion 0.5.0 safe mode: GameAssembly SHA256 differs from the audited build (or could not be read). No hooks installed; EE profiles, powerpack, model, compactness and editor controls are disabled. Saving a tank in this mode CAN REMOVE EE engine settings and legacy turbo metadata; native layout flags remain. Use the audited game build to preserve EE settings.";
}
