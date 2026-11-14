using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Sprocket;
using Sprocket.MissionFramework;

namespace SprocketExpedition.Game;

[BepInPlugin("sprocket.expedition", "Sprocket Expedition", "0.3.12")]
#if CAMPAIGN
[BepInDependency(SprocketUiKit.UiKit.PluginGuid, ">=0.1.3 <0.2.0")]
#endif
public sealed class Plugin : BasePlugin
{
    internal static BepInEx.Logging.ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> EnemyHunt = null!;
#if CAMPAIGN
    internal static ConfigEntry<string>? AmbusherDesigns;
    internal static ConfigEntry<string>? NotAmbusherDesigns;
    internal static ConfigEntry<bool>? BattleStatsHooks;
    internal static ConfigEntry<bool>? AllowFixtureCatalogue;
    internal static ConfigEntry<int>? SaveSlots;
#endif
    internal static ConfigEntry<bool>? DetailedLogging;
    internal static bool Detailed => DetailedLogging?.Value ?? false;
    // Verbose trace line; callers check Detailed first so a disabled switch builds no strings.
    internal static void Trace(string message)
    {
        if (Detailed) Logger.LogInfo(message);
    }
    private Harmony? harmony;
    public override void Load()
    {
        Logger = Log;
        var assemblyPath = Path.Combine(Paths.GameRootPath, "GameAssembly.dll");
        using var nativeStream = File.OpenRead(assemblyPath);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(nativeStream));
        if (hash != "3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B")
        { Log.LogError("Expedition disabled: unqualified GameAssembly " + hash); return; }
        DetailedLogging = Config.Bind("Diagnostics", "DetailedLogging", false,
            "Verbose trace lines for testing. Warnings and errors are always logged.");
#if CAMPAIGN
        SaveSlots = Config.Bind("Campaign", "SaveSlots", 8,
            new ConfigDescription("Number of local campaign save slots (restart to apply).",
                new AcceptableValueRange<int>(4, 12)));
        CampaignSlotManager.Configure(SaveSlots.Value);
        AllowFixtureCatalogue = Config.Bind("Diagnostics", "AllowFixtureCatalogue", false,
            "Developer only: allow new fixture runs outside the native New Game flow.");
        BattleStatsHooks = Config.Bind("Diagnostics", "BattleStatsHooks", true,
            "Observe shots, hits and kills for the defeat screen. Turn off if the designer or game crashes.");
        EnemyHunt = Config.Bind("Campaign", "EnemyHunt", true,
            "Both AI teams rally toward the opposing starting spawn and attack visible targets. "
            + "Native player commands take priority for the rest of the battle. "
            + "Set false to keep the native map AI for comparison or diagnosis.");
        AmbusherDesigns = Config.Bind("Combat", "AmbusherDesigns", string.Empty,
            "Semicolon-separated native design names that must use the Ambusher role. "
            + "Empty uses blueprint-based casemate detection.");
        NotAmbusherDesigns = Config.Bind("Combat", "NotAmbusherDesigns", string.Empty,
            "Semicolon-separated native design names that must never use the Ambusher role. "
            + "Takes priority over AmbusherDesigns and detection.");
        CampaignMusic.Configure(Config);
        CampaignBossRoster.Configure(Config);
        CampaignDifficulty.Configure(Config);
        CampaignNativeCatalogue.Configure(Config);
        AddComponent<CustomBattleHostSpike>();
#endif
        harmony = new Harmony("sprocket.expedition");
        harmony.PatchAll(typeof(Plugin).Assembly);
#if CAMPAIGN
        // Failed or unsafe observers disable only that measurement, never the mod.
        BattleStatsPatches.Install(harmony, BattleStatsHooks.Value);
        CampaignElevatedVision.Initialize(Config);
#endif
        AddComponent<ProbeUI>();
        Log.LogInfo("Expedition prepared. No scenario assets or era definitions changed.");
    }
    public override bool Unload()
    {
#if CAMPAIGN
        CampaignPauseMenu.Release();
        CampaignElevatedVision.Unload();
        CampaignMusic.Release();
#endif
        harmony?.UnpatchSelf();
        return true;
    }
}

public sealed class ProbeUI : MonoBehaviour
{
    public ProbeUI(IntPtr pointer) : base(pointer) { }
    private bool updateFailed;
    public void Update()
    {
        try
        {
#if CAMPAIGN
            CampaignControllerHost.Tick();
            CampaignNativeFlow.Tick();
            CampaignNativeMenu.Tick();
            CampaignEditorBudget.Tick();
            CampaignBattleHud.Tick();
            CampaignMusic.Tick();
            CampaignPauseMenu.Tick();
#endif
        }
        catch (Exception ex)
        {
            if (!updateFailed)
            {
                updateFailed = true;
                Plugin.Logger.LogError("Expedition update failed: " + ex);
            }
        }
    }
}

// Keep the existing targets: these prefixes also run for campaign missions.
// Native campaign outcomes are owned by CampaignSpikeComplete/FailMissionHook.
[HarmonyPatch(typeof(MissionScenarioGameState), nameof(MissionScenarioGameState.CompleteMission))]
internal static class ProofWinPatch
{
    static bool Prefix(MissionScenarioGameState __instance, Mission __0) => true;
}
[HarmonyPatch(typeof(MissionScenarioGameState), nameof(MissionScenarioGameState.FailMission))]
internal static class ProofLossPatch
{
    static bool Prefix(MissionScenarioGameState __instance, Mission __0) => true;
}
