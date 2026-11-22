using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace SprocketWheeledVehicles;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "sprocket.wheeledvehicles";
    public const string PluginName = "Sprocket Wheeled Vehicles";
    public const string PluginVersion = "0.4.2";

    internal static ManualLogSource ModLog { get; private set; } = null!;
    internal static ConfigEntry<bool> Enabled { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableTireAudio { get; private set; } = null!;
    internal static ConfigEntry<float> TireAudioVolume { get; private set; } = null!;
    internal static ConfigEntry<bool> DetailedLogging { get; private set; } = null!;

    private readonly List<Harmony> _harmonies = new();

    public override void Load()
    {
        ModLog = Log;
        Enabled = Config.Bind("General", "Enabled", true,
            "Enable true wheeled vehicle physics and steering for vehicles with wheeled mode enabled.");
        EnableTireAudio = Config.Bind("Audio", "EnableTireAudio", true,
            "Replace steel track clatter with a soft rubber tyre rolling sound on wheeled vehicles.");
        TireAudioVolume = Config.Bind("Audio", "TireAudioVolume", 0.32f,
            "Volume multiplier for rubber tyre rolling sound (0.0 to 1.5).");
        DetailedLogging = Config.Bind("Diagnostics", "DetailedLogging", true,
            "Verbose trace lines for testing. Warnings and errors are always logged.");

        PatchOwner($"{PluginGuid}.blueprint", typeof(BlueprintHooks));
        PatchOwner($"{PluginGuid}.editor", typeof(EditorHooks));
        PatchOwner($"{PluginGuid}.icon", typeof(IconHooks));
        PatchOwner($"{PluginGuid}.visual", typeof(VisualHooks));
        PatchOwner($"{PluginGuid}.audio", typeof(AudioHooks));
        PatchOwner($"{PluginGuid}.runtime", typeof(RuntimeHooks));

        ModLog.LogInfo($"[{PluginName}] Loaded v{PluginVersion} (Enabled={Enabled.Value}, TireAudio={EnableTireAudio.Value}, DetailedLogging={DetailedLogging.Value}).");
    }

    internal static void Diag(Func<string> messageFactory)
    {
        if (!DetailedLogging.Value) return;
        try
        {
            ModLog.LogInfo($"[Wheeled] {messageFactory()}");
        }
        catch { }
    }

    private void PatchOwner(string ownerId, Type patchClass)
    {
        try
        {
            var harmony = new Harmony(ownerId);
            harmony.PatchAll(patchClass);
            _harmonies.Add(harmony);
        }
        catch (Exception ex)
        {
            ModLog.LogError($"[{PluginName}] Failed to apply patches for {ownerId}: {ex}");
        }
    }
}
