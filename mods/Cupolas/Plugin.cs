using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
namespace SprocketCupolas;

[BepInPlugin("sprocket.cupolas", "Sprocket Cupolas", "0.1.0")]
[BepInDependency("sprocket.telescopicmast", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Instance = null!;
    internal ConfigEntry<bool> Detailed = null!;
    private CupolaRuntime? driver;
    public override void Load()
    {
        Instance = this;
        Detailed = Config.Bind("Diagnostics", "DetailedLogging", true,
            "Verbose trace lines for testing. Warnings and errors are always logged.");
        IconClient.Register();
        driver = AddComponent<CupolaRuntime>();
        Log.LogInfo("Sprocket Cupolas 0.1.0: static lid; stock collision and damage; hookless discovery.");
    }
    public override bool Unload() { driver?.Dispose(); return true; }
    internal static void Diag(Func<string> message) { if (Instance.Detailed.Value) Instance.Log.LogInfo("[Cupola] " + message()); }
}
