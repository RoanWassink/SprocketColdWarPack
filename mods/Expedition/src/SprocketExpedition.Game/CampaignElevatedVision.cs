#if CAMPAIGN
using BepInEx.Configuration;
using HarmonyLib;
using Sprocket.ArtificialIntelligence;
using Sprocket.PlayerControl;
using Sprocket.Vehicles;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace SprocketExpedition.Game;

// A private observer Transform is substituted for the duration of native SlowTick.
// Never move the original observer: it can be part of the physical vehicle.
internal static class CampaignElevatedVision
{
    private static readonly CampaignVisionHeightPolicy height = new();
    private static readonly Dictionary<IntPtr, GameObject> probes = new();
    private static Harmony? patches;
    private static ConfigEntry<bool>? enabled;
    private static PlayerCameraController? cameras;
    private static float nextCameraSearch;
    private static bool faulted, loggedSample;
    private static int applied;

    internal static void Initialize(ConfigFile config)
    {
        enabled = config.Bind("Campaign", "CameraHeightVision", true,
            "Give Expedition AI both teams the current third-person camera height above their own hull. "
            + "Keep last height in the gun sight; use 6 m until the first camera sample. "
            + "Native obstacles, range and firing-line checks remain active.");
        try
        {
            patches = new Harmony("sprocket.expedition.camera-height-vision");
            var method = AccessTools.Method(typeof(CommanderAI), nameof(CommanderAI.SlowTick), new[] { typeof(float) });
            if (method == null) throw new MissingMethodException("CommanderAI.SlowTick(float)");
            patches.Patch(method,
                prefix: new HarmonyMethod(typeof(CampaignElevatedVision), nameof(BeforeSlowTick)),
                finalizer: new HarmonyMethod(typeof(CampaignElevatedVision), nameof(AfterSlowTick)));
            Plugin.Logger.LogInfo("Expedition camera-height vision hook prepared; scoped to owned Expedition AI.");
        }
        catch (Exception ex)
        {
            patches?.UnpatchSelf(); patches = null;
            Plugin.Logger.LogWarning("Expedition camera-height vision unavailable; native observer retained: " + ex.Message);
        }
    }

    internal static void ObservePlayer(VehicleBehaviour? player)
    {
        if (enabled?.Value != true || faulted || patches == null || player == null) return;
        try
        {
            if (cameras == null || cameras.WasCollected)
            {
                if (Time.time < nextCameraSearch) return;
                nextCameraSearch = Time.time + 1f;
                cameras = UObject.FindObjectOfType<PlayerCameraController>();
            }
            if (cameras == null) return;
            var third = cameras.thirdPersonCamera;
            var active = cameras.ActiveCamera;
            // A disabled third-person camera can contain stale editor coordinates.
            if (third == null || active == null || active.Pointer != third.Pointer
                || !third.isActiveAndEnabled) return;
            height.Observe(third.transform.position.y, player.Position.y);
            if (height.Sampled && !loggedSample)
            {
                loggedSample = true;
                Plugin.Logger.LogInfo($"Expedition camera-height vision: sampled {height.Height:N2} m above hull; "
                    + "both AI teams use this height; weapon-level firing checks retained.");
            }
        }
        catch { cameras = null; } // Camera transition: preserve the last valid sample.
    }

    private static void BeforeSlowTick(CommanderAI __instance, out Transform? __state)
    {
        __state = null;
        if (enabled?.Value != true || faulted) return;
        try
        {
            var tank = CampaignEnemyHunt.VisionTank(__instance);
            if (tank == null) return;
            var original = __instance.observerTransform;
            if (original == null) return;
            var origin = original.position;
            origin.y = height.ObserverY(origin.y, tank.Position.y);
            if (!float.IsFinite(origin.y)) return;
            if (!probes.TryGetValue(__instance.Pointer, out var probe) || probe == null)
            {
                probe = new GameObject("Expedition AI observer");
                probe.hideFlags = HideFlags.HideAndDontSave;
                probes[__instance.Pointer] = probe;
            }
            probe.transform.position = origin;
            probe.transform.rotation = original.rotation;
            __state = original; // Also restore if the native tick throws.
            __instance.observerTransform = probe.transform;
            if (++applied == 1)
                Plugin.Logger.LogInfo($"Expedition camera-height vision: first native observer substitution; "
                    + $"height={height.Height:N2} m; sampledCamera={height.Sampled}.");
        }
        catch (Exception ex)
        {
            faulted = true;
            Plugin.Logger.LogWarning("Expedition camera-height vision paused: " + ex.Message);
        }
    }

    private static void AfterSlowTick(CommanderAI __instance, Transform? __state)
    {
        if (__state == null) return;
        try
        {
            if (!__instance.WasCollected) __instance.observerTransform = __state;
        }
        catch (Exception ex)
        {
            faulted = true;
            Plugin.Logger.LogWarning("Expedition observer restoration failed: " + ex.Message);
        }
    }

    internal static void Reset()
    {
        foreach (var probe in probes.Values)
            if (probe != null) UObject.Destroy(probe);
        probes.Clear(); height.Reset(); cameras = null; nextCameraSearch = 0;
        faulted = loggedSample = false; applied = 0;
    }

    internal static void Unload() { patches?.UnpatchSelf(); patches = null; Reset(); }
}
#endif
