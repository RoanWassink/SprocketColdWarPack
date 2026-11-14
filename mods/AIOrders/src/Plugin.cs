using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SprocketKeybinds;
using SprocketUiKit;
using BepInEx.Configuration;
using UnityEngine;
using System.Security.Cryptography;

namespace SprocketAIOrders;

[BepInPlugin(Guid, "Sprocket AI Orders", "0.5.2")]
[BepInDependency(UiKit.PluginGuid, ">=0.1.2 <0.2.0")]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.7 <0.2.0")]
[BepInDependency("sprocket.expedition", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "sprocket.aiorders";
    internal static Plugin Instance = null!;
    internal static ModKeybind Toggle = null!, MapKey = null!;
    internal static readonly List<ModKeybind> Digits = new();
    private Harmony? harmony;
    private static ConfigEntry<bool> detailedLogging = null!;
    private static ConfigEntry<int> mapRedrawFps = null!;
    internal static int MapRedrawFps => mapRedrawFps?.Value ?? 12;
    internal static bool Detailed => detailedLogging?.Value ?? false;
    public override void Load()
    {
        Instance = this;
        detailedLogging = Config.Bind("Diagnostics", "DetailedLogging", false,"Verbose trace lines for testing. Warnings and errors are always logged.");
        mapRedrawFps = Config.Bind("Map", "RedrawFps", 12, new ConfigDescription("Map redraws per second; pan, zoom and selection also invalidate immediately.", new AcceptableValueRange<int>(1, 30)));
        OrdersHost.Stopped = false;
        using var file = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(file)) != "3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B")
        { Log.LogError("AI Orders disabled: unqualified native game version."); return; }
        try
        {
            Toggle = Keybinds.RegisterButton(Guid, "AI Orders", "panel", "Quick orders HUD", "<Keyboard>/f9");
            MapKey = Keybinds.RegisterButton(Guid, "AI Orders", "map", "Tactical map", "<Keyboard>/m");
            for (var n = 0; n <= 9; n++)
                Digits.Add(Keybinds.RegisterButton(Guid, "AI Orders", "menu." + n, n == 0 ? "Menu back / close" : "Menu choice " + n, "<Keyboard>/" + n));
            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            var host = AddComponent<OrdersHost>();
            OrdersHost.Current = host;
            host.CreatePanels();
        }
        catch
        {
            harmony?.UnpatchSelf();
            OrdersHost.Current?.DestroyPanels();
            if (OrdersHost.Current != null) UnityEngine.Object.Destroy(OrdersHost.Current);
            OrdersHost.Current = null;
            UnregisterAll();
            OrdersHost.Stopped = true;
            throw;
        }
        Log.LogInfo("AI Orders 0.5.2 ready: non-modal F9 quick HUD and tactical map.");
    }
    public override bool Unload()
    {
        OrdersHost.Current?.Reset();
        OrdersHost.Current?.DestroyPanels();
        if (OrdersHost.Current != null) UnityEngine.Object.Destroy(OrdersHost.Current);
        OrdersHost.Current = null;
        harmony?.UnpatchSelf();
        UnregisterAll();
        OrdersHost.Stopped = true;
        return true;
    }
    internal static void Info(string message) => Instance.Log.LogInfo(message);
    private static void UnregisterAll()
    {
        if (Toggle != null) Keybinds.Unregister(Guid, "panel");
        if (MapKey != null) Keybinds.Unregister(Guid, "map");
        for (var i = 0; i < Digits.Count; i++) Keybinds.Unregister(Guid, "menu." + i);
        Digits.Clear();
    }
    internal static void Warn(string message) => Instance.Log.LogWarning(message);
}
