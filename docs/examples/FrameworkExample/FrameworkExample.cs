using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using SprocketKeybinds;
using SprocketUiKit;

[BepInPlugin(GUID, "Framework Example", "1.0.0")]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.10 <0.2.0")]
[BepInDependency(UiKit.PluginGuid, ">=0.1.13 <0.2.0")]
public sealed class FrameworkExample : BasePlugin
{
    public const string GUID = "sprocket.example.framework";

    public override void Load()
    {
        try
        {
            var showGreeting = Config.Bind("Example", "Show greeting", true,
                "Show the greeting in the example panel.");
            var panelOpacity = Config.Bind("Example", "Panel opacity", 0.9f,
                new ConfigDescription("Opacity of the example panel content.",
                    new AcceptableValueRange<float>(0.2f, 1f)));
            var detailedLogging = Config.Bind("Diagnostics", "DetailedLogging", false,
                "Verbose trace lines. Warnings and errors are always logged.");

            Config.SettingChanged += (_, e) =>
            {
                try
                {
                    if (e.ChangedSetting == showGreeting || e.ChangedSetting == panelOpacity)
                    {
                        if (detailedLogging.Value) Log.LogInfo("Example setting changed.");
                    }
                }
                catch (Exception ex) { Log.LogWarning("Setting callback: " + ex.Message); }
            };

            Keybinds.RegisterModMetadata(GUID, "Nero", "Framework Example");
            var greetingSetting = ModSettings.Register(GUID, showGreeting,
                new SettingOptions { Label = "Show greeting", Group = "Example" });
            var opacitySetting = ModSettings.Register(GUID, panelOpacity,
                new SettingOptions { Label = "Panel opacity", Step = 0.05f });
            ModSettings.RegisterSection(GUID, Config, "Diagnostics");

            var toggle = Keybinds.RegisterButton(GUID, "Framework Example", "toggle-panel",
                "Toggle example panel", "");

            try
            {
                var panel = UiKit.CreatePanel(GUID, "main", "Framework Example", null, 600, 340);
                UiKit.BindToggle(panel, toggle);
                var content = panel.Body;
                content.Alpha(() => panelOpacity.Value);
                content.Label("Hello from the Sprocket Mod Framework!")
                    .VisibleWhen(() => showGreeting.Value);
                if (greetingSetting != null) content.Setting(greetingSetting);
                if (opacitySetting != null) content.Setting(opacitySetting);
            }
            catch (Exception ex) { Log.LogWarning("Example panel unavailable: " + ex.Message); }
        }
        catch (Exception ex) { Log.LogWarning("Example setup incomplete: " + ex.Message); }
    }
}
