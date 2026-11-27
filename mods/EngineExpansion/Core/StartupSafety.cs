namespace SprocketEngineExpansion.Core;
public static class StartupSafety
{
    public const string AuditedHash="3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B";
    public const string AuditedUpdateHash="77FC601748991DC2101435C6B72C7A5A4B3DC3C68429B349C2EFEE711F6D5388";
    public static bool SafeMode(string hash)=>!string.Equals(hash,AuditedHash,StringComparison.OrdinalIgnoreCase)&&!string.Equals(hash,AuditedUpdateHash,StringComparison.OrdinalIgnoreCase);
    // An unknown binary has no proven native addresses or lengths: install zero patches.
    public static bool InstallHooks(string hash)=>!SafeMode(hash);
    public const string Warning="Engine Expansion 0.7.4 safe mode: GameAssembly SHA256 differs from the audited builds (or could not be read). No hooks installed; EE profiles, powerpack, model, collision filter, engine character, compactness and editor controls are disabled. Saving a tank in this mode CAN REMOVE EE engine settings and legacy turbo metadata; native layout flags remain. Use an audited game build to preserve EE settings.";
}
