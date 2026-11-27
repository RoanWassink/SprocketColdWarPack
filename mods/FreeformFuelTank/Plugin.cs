using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace SprocketFreeformFuelTank;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "sprocket.freeformfueltank";
    public const string PluginName = "Sprocket Freeform Fuel Tank";
    public const string PluginVersion = "0.1.4";

    internal static ManualLogSource ModLog { get; private set; } = null!;
    internal static ConfigEntry<bool> DetailedLogging { get; private set; } = null!;
    internal static ConfigEntry<float> FillEfficiency { get; private set; } = null!;
    internal static ConfigEntry<float> FuelDensity { get; private set; } = null!;
    internal static ConfigEntry<bool> RequireClosedMesh { get; private set; } = null!;

    private Harmony? _harmony;

    public override void Load()
    {
        ModLog = Log;

        DetailedLogging = Config.Bind(
            "Diagnostics",
            "DetailedLogging",
            true,
            "Write detailed diagnostic logs for Freeform Fuel Tank builds, volumes, and colliders.");

        FillEfficiency = Config.Bind(
            "FuelTank",
            "FillEfficiency",
            1.0f,
            "Fraction of enclosed mesh volume usable as fuel capacity (1.0 = 1000 L/m^3, matching vanilla Box Fuel Tank).");

        FuelDensity = Config.Bind(
            "FuelTank",
            "FuelDensityKgPerLitre",
            FreeformMeshMath.VanillaDieselDensityKgPerLitre,
            "Fuel mass per litre in kg/L (vanilla diesel = 0.832).");

        RequireClosedMesh = Config.Bind(
            "FuelTank",
            "RequireClosedMesh",
            true,
            "If true, open/non-watertight meshes report 0 L capacity until all faces are closed.");

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(Plugin).Assembly);

        ModLog.LogInfo($"[{PluginName} {PluginVersion}] Loaded. GameAssembly={TryGetGameAssemblyShortHash()}");
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        return base.Unload();
    }

    internal static void Diag(Func<string> messageFactory)
    {
        if (DetailedLogging?.Value == true)
        {
            ModLog.LogInfo($"[FreeformFuelTank] {messageFactory()}");
        }
    }

    internal static void Warn(string context, Exception ex)
    {
        ModLog.LogWarning($"[FreeformFuelTank] {context}: {ex.GetType().Name}: {ex.Message}");
    }

    private static string TryGetGameAssemblyShortHash()
    {
        try
        {
            string gameDir = Path.GetFullPath(Path.Combine(Paths.GameRootPath));
            string dll = Path.Combine(gameDir, "GameAssembly.dll");
            if (!File.Exists(dll)) return "missing";
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(dll);
            return Convert.ToHexString(sha.ComputeHash(fs))[..12];
        }
        catch
        {
            return "unknown";
        }
    }
}
