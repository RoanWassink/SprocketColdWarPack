using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using SprocketKeybinds;
using UnityEngine;

[BepInPlugin("example.sprocket.keybinddemo", "Keybind API Demo", "0.1.0")]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.10 <0.2.0")]
public sealed class DemoPlugin : BasePlugin
{
    internal static ModKeybind Toggle = null!, Raise = null!, Lower = null!;
    internal static ManualLogSource Logger = null!;
    internal static bool Active;
    private ConfigEntry<bool> demoEnabled = null!;
    private ConfigEntry<int> demoLevel = null!;
    public override void Load()
    {
        Logger = Log;
        Keybinds.RegisterModMetadata("example.sprocket.keybinddemo", "Example Author", "Keybind API Demo");
        demoEnabled = Config.Bind("General", "Enabled", true, "Enable the demo.");
        demoLevel = Config.Bind("General", "Level", 5,
            new ConfigDescription("Demo level.", new AcceptableValueRange<int>(1, 10)));
        var enabledSetting = ModSettings.Register("example.sprocket.keybinddemo", demoEnabled, new SettingOptions { Label = "Demo enabled" });
        if (enabledSetting != null) Logger.LogInfo("Registered setting: " + enabledSetting.Label); // Getter-only API property.
        ModSettings.Register("example.sprocket.keybinddemo", demoLevel);
        Toggle = Keybinds.RegisterButton("example.sprocket.keybinddemo", "Keybind API Demo", "toggle", "Toggle demo", "<Keyboard>/n");
        Raise = Keybinds.RegisterButton("example.sprocket.keybinddemo", "Keybind API Demo", "raise", "Raise demo", "<Keyboard>/insert");
        Lower = Keybinds.RegisterButton("example.sprocket.keybinddemo", "Keybind API Demo", "lower", "Lower demo", "<Keyboard>/delete");
        Active = true;
        AddComponent<DemoDriver>();
    }
    public override bool Unload()
    {
        Active = false;
        ModSettings.Unregister("example.sprocket.keybinddemo", demoEnabled.Definition);
        ModSettings.Unregister("example.sprocket.keybinddemo", demoLevel.Definition);
        Keybinds.Unregister("example.sprocket.keybinddemo", "toggle");
        Keybinds.Unregister("example.sprocket.keybinddemo", "raise");
        Keybinds.Unregister("example.sprocket.keybinddemo", "lower");
        return true;
    }
}
public sealed class DemoDriver : MonoBehaviour
{
    public DemoDriver(IntPtr ptr) : base(ptr) { }
    public void Update()
    {
        if (!DemoPlugin.Active) return;
        // Replace these logs with your mod's actions, and keep your gameplay/context checks here.
        if (DemoPlugin.Toggle.WasPressedThisFrame) DemoPlugin.Logger.LogInfo("Demo toggled");
        if (DemoPlugin.Raise.WasPressedThisFrame) DemoPlugin.Logger.LogInfo("Demo raise pressed");
        if (DemoPlugin.Lower.WasPressedThisFrame) DemoPlugin.Logger.LogInfo("Demo lower pressed");
        // A moving part normally consumes Raise.IsHeld and Lower.IsHeld each frame.
    }
}

