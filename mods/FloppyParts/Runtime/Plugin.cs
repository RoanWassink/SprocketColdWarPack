using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace SprocketFloppyParts;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    internal const string PluginGuid = "sprocket.floppyparts";
    internal const string PluginName = "Sprocket Floppy Parts";
    internal const string PluginVersion = "0.3.6";
    internal const string ExpandedBallisticsGuid = "sprocket.expandedballistics";

    internal static ManualLogSource ModLog = null!;
    internal static ConfigEntry<bool> DetailedLogging = null!;
    internal static ConfigEntry<bool> TrustUnknownGameBuild = null!;
    internal static ConfigEntry<bool> AutoAdoptExpandedBallisticsLooks = null!;
    internal static ConfigEntry<bool> SectionOpen = null!;
    internal static ConfigEntry<bool> SimulationEnabled = null!;
    internal static ConfigEntry<int> MaxSimulatedPlates = null!;
    internal static ConfigEntry<bool> SurfaceCollisionEnabled = null!;
    internal static ConfigEntry<bool> HingedAttachmentsEnabled = null!;
    internal static ConfigEntry<bool> MuzzleBlastEnabled = null!;
    internal static ConfigEntry<bool> ExhaustAndVentAirflowEnabled = null!;

    private static Harmony? harmony;

    internal static bool Detailed => DetailedLogging?.Value == true;

    internal static bool IsExpandedBallisticsLoaded()
    {
        try
        {
            return IL2CPPChainloader.Instance.Plugins.ContainsKey(ExpandedBallisticsGuid);
        }
        catch
        {
            return false;
        }
    }

    public override void Load()
    {
        ModLog = Log;

        DetailedLogging = Config.Bind(
            "Diagnostics",
            "DetailedLogging",
            true,
            "Log overlay builds, material loads, and persistence activity.");
        TrustUnknownGameBuild = Config.Bind(
            "Compatibility",
            "TrustUnknownGameBuild",
            false,
            "Allow Harmony patches to load on an unverified GameAssembly.dll build (at your own risk).");
        AutoAdoptExpandedBallisticsLooks = Config.Bind(
            "Compatibility",
            "AutoAdoptExpandedBallisticsLooks",
            false,
            "Automatically adopt Expanded Ballistics material looks (ebFabric/ebRubber/ebWood/ebGlass) when no Floppy Parts look is saved.");
        SectionOpen = Config.Bind(
            "UI",
            "FloppyPartsSectionOpen",
            true,
            "Whether the Floppy Parts section in plate-structure inspectors starts open.");
        SimulationEnabled = Config.Bind(
            "Simulation",
            "Enabled",
            true,
            "Enable real-time cloth, rubber, and hinged plate motion during Play.");
        MaxSimulatedPlates = Config.Bind(
            "Simulation",
            "MaxSimulatedPlates",
            32,
            new ConfigDescription(
                "Maximum number of active simulated plates updated per frame.",
                new AcceptableValueRange<int>(1, 128)));
        SurfaceCollisionEnabled = Config.Bind(
            "Simulation",
            "SurfaceCollisionEnabled",
            true,
            "Prevent cloth and rubber from clipping through the tank body, turret, barrel, tracks, and ground.");
        HingedAttachmentsEnabled = Config.Bind(
            "Simulation",
            "HingedAttachmentsEnabled",
            true,
            "Swing objects placed onto a Hinged plate along with the plate during Play and Photo Mode.");
        MuzzleBlastEnabled = Config.Bind(
            "Simulation",
            "MuzzleBlastEnabled",
            true,
            "Apply cannon muzzle blast impulse to nearby cloth, rubber, and hinged plates.");
        ExhaustAndVentAirflowEnabled = Config.Bind(
            "Simulation",
            "ExhaustAndVentAirflowEnabled",
            true,
            "Simulate live engine exhaust and radiator vent airflow fluttering nearby cloth, rubber, and hinged plates.");

        var sha256 = ComputeGameAssemblySha256();
        var (allowed, reason) = GameBuildGateCore.Evaluate(sha256, TrustUnknownGameBuild.Value);
        if (!allowed)
        {
            Log.LogWarning($"[GameBuildGate] {reason}");
            return;
        }
        Log.LogInfo($"[GameBuildGate] {reason}");

        string pluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? Paths.PluginPath;
        MaterialLibrary.Initialize(pluginDir);

        harmony = new Harmony(PluginGuid);
        harmony.PatchAll(typeof(PersistenceHooks));
        harmony.PatchAll(typeof(FloppyOverlayRuntime));
        harmony.PatchAll(typeof(PlateInspectorUi));
        harmony.PatchAll(typeof(MuzzleBlastHooks));

        Log.LogInfo($"{PluginName} {PluginVersion} loaded (EB detected={IsExpandedBallisticsLoaded()}).");
    }

    public override bool Unload()
    {
        try
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }
        catch (Exception ex)
        {
            ModLog?.LogWarning($"[Unload] Harmony unpatch error: {ex.Message}");
        }

        FloppyOverlayRuntime.Clear();
        MaterialLibrary.Clear();
        PlateConfigStore.Clear();
        return base.Unload();
    }

    private static string? ComputeGameAssemblySha256()
    {
        try
        {
            string path = Path.Combine(Paths.GameRootPath, "GameAssembly.dll");
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
        catch
        {
            return null;
        }
    }
}
