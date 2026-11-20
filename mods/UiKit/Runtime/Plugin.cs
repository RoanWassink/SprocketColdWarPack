using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SprocketKeybinds;
namespace SprocketUiKit;

/// <summary>Not part of the public contract. BepInEx plugin entry point.</summary>
[BepInPlugin(UiKit.PluginGuid, "Sprocket UI Kit", UiKit.Version)]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.10 <0.2.0")]
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Instance = null!;
    internal static ConfigEntry<bool>? DetailedLogging;
    internal static ConfigEntry<float>? ScaleFactor;
    internal static ConfigEntry<bool>? BlockGameplay, SuspendGameKeys, BlockKeysWhilePanelFocused;
    internal static ConfigEntry<string>? FontSource;
    /// <summary>Diagnostics.DetailedLogging: gate every trace line with "if (Plugin.Detailed)".</summary>
    internal static bool Detailed => DetailedLogging?.Value ?? false;
    internal static void Trace(string message) => Instance.Log.LogInfo(message);
    private readonly List<Harmony> harmonies = new();

    /// <summary>Not part of the public contract. BepInEx startup entry point.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public override void Load()
    {
        Instance = this;
        Guard.Sink = m => Log.LogWarning(m);
        DetailedLogging = Config.Bind("Diagnostics", "DetailedLogging", false,"Verbose trace lines for testing. Warnings and errors are always logged.");
        ScaleFactor = Config.Bind("Display", "Scale", 1f, new ConfigDescription("Multiplier on the automatic UI scale (screen height / 1080).", new AcceptableValueRange<float>(KitMath.MinFactor, KitMath.MaxFactor)));
        FontSource = Config.Bind("Display", "FontSource", "Default", new ConfigDescription("TMP font route: Default = TMP_Settings.defaultFontAsset first, Scene = the most used font of the game's visible UI first. The other route is the fallback.", new AcceptableValueList<string>("Default", "Scene")));
        BlockGameplay = Config.Bind("Input", "BlockGameplayInput", true, "In game, while a panel is open: block firing, scope, orders and camera turning (panels can opt out).");
        BlockKeysWhilePanelFocused = Config.Bind("Input", "BlockKeysWhilePanelFocused", true, "Disable game actions while a normal Kit panel owns keyboard focus; mod keybinds and native UI stay active.");
        SuspendGameKeys = Config.Bind("Input", "SuspendGameKeysWhileTyping", true, "While a kit text field has focus, disable the game's keyboard actions (restored on blur).");
        var skip = Config.Bind("Input", "SkipBlockTargets", "", "Comma-separated input-block targets to leave unpatched, e.g. 'ScopeCameraController.ProcessInputs' or 'Fire'.");

        Keybinds.RegisterModMetadata(UiKit.PluginGuid, "Nero", "UI Kit");
        ModSettings.Register(UiKit.PluginGuid, DetailedLogging, new SettingOptions { Label = "Detailed logging" });
        ModSettings.Register(UiKit.PluginGuid, ScaleFactor);
        ModSettings.Register(UiKit.PluginGuid, FontSource, new SettingOptions { RequiresRestart = true });
        ModSettings.Register(UiKit.PluginGuid, BlockGameplay);
        ModSettings.Register(UiKit.PluginGuid, BlockKeysWhilePanelFocused);
        ModSettings.Register(UiKit.PluginGuid, SuspendGameKeys);
        ModSettings.Register(UiKit.PluginGuid, skip, new SettingOptions { Advanced = true, RequiresRestart = true });
        SettingsMenu.Register();

        // Optional parts never stop the kit (and its dependents) from loading.
        PatchGroup("pointer", typeof(PointerHooks), "designer click-through guard");
        Kit.PauseHookActive = PatchGroup("pause", typeof(PauseHooks), "Esc pause guard and pause-menu detection");
        try
        {
            var h = new Harmony(UiKit.PluginGuid + ".inputblock");
            InputBlock.Apply(h, new HashSet<string>(skip.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase));
            harmonies.Add(h);
        }
        catch (Exception ex) { Log.LogWarning("[UI Kit] Gameplay input block unavailable: " + ex.Message); }
        AddComponent<UiKitHost>();
        try { Demo.Register(); }
        catch (Exception ex) { Log.LogWarning("[UI Kit] Demo panel unavailable: " + ex); }
        Log.LogInfo($"[UI Kit] v{UiKit.Version} ready: input block {InputBlock.Applied}/{InputBlock.Expected}{(InputBlock.Missing.Count > 0 ? " (missing: " + string.Join(", ", InputBlock.Missing) + ")" : "")}; scale factor {ScaleFactor.Value}; font route {FontSource.Value}; detailed logging {(Detailed ? "on" : "off")}. Demo: bind 'UI Kit / Demo' in the Settings keybind menu (factory default: unbound).");
    }
    private bool PatchGroup(string suffix, Type hooks, string what)
    {
        var h = new Harmony(UiKit.PluginGuid + "." + suffix);
        try { h.PatchAll(hooks); harmonies.Add(h); return true; }
        catch (Exception ex) { try { h.UnpatchSelf(); } catch { } Log.LogWarning($"[UI Kit] Optional hooks for {what} unavailable: {ex.Message}"); return false; }
    }
    /// <summary>Not part of the public contract. BepInEx shutdown entry point.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public override bool Unload()
    {
        try { SettingsMenu.Unregister(); Kit.Shutdown(); Demo.Unregister(); }
        finally { foreach (var h in harmonies) h.UnpatchSelf(); }
        return true;
    }
}
