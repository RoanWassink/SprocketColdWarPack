using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Security.Cryptography;

namespace SprocketSandboxTargets;

[BepInPlugin(Guid, "Sprocket Sandbox Targets", "0.3.2")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "sprocket.sandboxtargets";
    public const string QualifiedHash = "3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B";
    internal static Plugin Instance = null!;
    private Harmony? harmony;
    public override void Load()
    {
        Instance = this;
        try
        {
            using var stream = File.OpenRead(Path.Combine(Paths.GameRootPath,"GameAssembly.dll"));
            using var sha = SHA256.Create();
            if (Convert.ToHexString(sha.ComputeHash(stream)) != QualifiedHash)
            { Warn("Uitgeschakeld: niet-gekwalificeerde GameAssembly-versie."); return; }
            TargetConfig.Load(ConfigPath, Warn);
            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            NativeTargets.Stopped = false;
            Info("0.3.2 gereed; doelen via native spawns (eigen blueprints via gekloonde spawners).");
        }
        catch (Exception ex)
        { harmony?.UnpatchSelf(); NativeTargets.Stopped = true; Warn("Laden mislukt: " + ex); }
    }
    public override bool Unload()
    {
        NativeTargets.Stopped = true;
        harmony?.UnpatchSelf();
        return true;
    }
    internal static string ConfigPath => Path.Combine(Paths.ConfigPath,Guid + ".json");
    internal static void Info(string value) => Instance.Log.LogInfo("[SandboxTargets] " + value);
    internal static void Warn(string value) => Instance.Log.LogWarning("[SandboxTargets] " + value);
}
