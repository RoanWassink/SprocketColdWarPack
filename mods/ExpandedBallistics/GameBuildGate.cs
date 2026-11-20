#if !EB_PURE_SOLID_MARK
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
#endif
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.95 game-build hash gate. After a Steam update the shot/hit-path hooks must not patch blindly:
/// GameAssembly.dll in the game folder is hashed once (SHA256, uppercase hex) and compared with the
/// audited builds below. On an unknown build only the risky hook classes are skipped, at their PatchAll
/// call sites, behind GameBuildGate.AllowHotPath; designer, parts, ERA and UI keep loading.
/// </summary>
internal static class GameBuildGate
{
    /// <summary>Audited GameAssembly.dll SHA256 hashes, uppercase hex.</summary>
    internal static readonly string[] KnownHashes=
    {
        "3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B",
        "77FC601748991DC2101435C6B72C7A5A4B3DC3C68429B349C2EFEE711F6D5388"
    };
    /// <summary>Pure core decision, unit-testable without the game: an audited hash, or an explicit trust override. A null hash (read failure) is treated as unknown.</summary>
    internal static bool Decide(string? hash,bool trust)=>trust||(hash!=null&&KnownHashes.Contains(hash,StringComparer.OrdinalIgnoreCase));
#if !EB_PURE_SOLID_MARK
    internal const string Section="Game build gate";
    internal const string TrustKey="TrustUnknownGameBuild";
    private static ConfigEntry<bool>? trust;
    private static ManualLogSource? log;
    private static readonly Lazy<string?> hash=new(ComputeHash);
    private static int reported;
    /// <summary>SHA256 of GameAssembly.dll, uppercase hex; null when the file could not be read (treated as unknown). Computed once, lazily.</summary>
    internal static string? Hash=>hash.Value;
    /// <summary>True when the current GameAssembly.dll hash is one of the audited builds.</summary>
    internal static bool Known=>Hash is {} h&&KnownHashes.Contains(h,StringComparer.OrdinalIgnoreCase);
    /// <summary>[Game build gate] TrustUnknownGameBuild.</summary>
    internal static bool Trust=>trust?.Value==true;
    /// <summary>True when the shot/hit-path hooks may patch: audited build, or the trust override is on.</summary>
    internal static bool AllowHotPath=>Decide(Hash,Trust);
    /// <summary>Binds the config entry on the plugin's own ConfigFile (same Bind pattern as the other EB settings) and reports the gate result once per session.</summary>
    internal static void Configure(ConfigFile config,ManualLogSource logger)
    {
        log=logger;
        trust=config.Bind(Section,TrustKey,false,
            "Trust this game build even when its GameAssembly.dll is not one of the audited hashes, so the shot/hit-path hooks still patch. Off by default: after a game update those hooks must be re-audited first. Turning this on is at your own risk.");
        ReportOnce();
    }
    /// <summary>Logs the gate result exactly once per session.</summary>
    internal static void ReportOnce()
    {
        if(Interlocked.Exchange(ref reported,1)==1)return;
        var logger=log;
        if(logger==null)return;
        var h=Hash;
        if(h!=null&&KnownHashes.Contains(h,StringComparer.OrdinalIgnoreCase))
            logger.LogInfo($"[Game build gate] GameAssembly {h[..8]} known; all hooks enabled");
        else
            logger.LogWarning($"[Game build gate] GameAssembly {h??"unknown"} is not audited; shot/hit-path hooks disabled (set TrustUnknownGameBuild=true to override at your own risk)");
    }
    private static string? ComputeHash()
    {
        try
        {
            using var sha=SHA256.Create();
            using var stream=File.OpenRead(Path.Combine(Paths.GameRootPath,"GameAssembly.dll"));
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
        catch(Exception){return null;}
    }
#endif
}
