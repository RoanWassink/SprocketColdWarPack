using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
namespace SprocketEmitter;

[BepInPlugin("sprocket.emitter","Sprocket Emitter",Version)]
[BepInDependency("sprocket.telescopicmast",BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin:BasePlugin
{
    internal const string Version="0.1.1";
    internal static Plugin Instance=null!;
    internal ConfigEntry<bool> Detailed=null!,AllowFallback=null!;
    private ConfigEntry<string> item=null!;
    private ConfigEntry<float> caliber=null!,speed=null!,spread=null!,spin=null!,interval=null!,lifetime=null!;
    private ConfigEntry<int> count=null!;
    private ConfigEntry<bool> continuous=null!,inherit=null!;
    internal ConfigEntry<int> MaxObjects=null!,MaxCasts=null!;
    internal ConfigEntry<float> MaxLifetime=null!,CasingLength=null!,ProjectileLength=null!,Slope=null!,Friction=null!;
    internal Settings Defaults=>new Settings {Item=item.Value,Caliber=caliber.Value,Speed=speed.Value,Spread=spread.Value,Spin=spin.Value,Count=count.Value,Continuous=continuous.Value,Interval=interval.Value,Lifetime=lifetime.Value,Inherit=inherit.Value}.Sanitize();
    public override void Load() {
        Instance=this;
        Detailed=Config.Bind("Diagnostics","DetailedLogging",true,"Verbose trace lines for testing. Warnings and errors are always logged.");
        AllowFallback=Config.Bind("Meshes","AllowEmbeddedFallback",false,"Use the plain embedded Carousel shapes when no stock rack mesh is available. Off: nothing is thrown without a stock mesh.");
        item=Config.Bind("Defaults","item","casing","casing or projectile");
        caliber=Config.Bind("Defaults","caliber",30f,"Diameter in mm (5-155).");
        speed=Config.Bind("Defaults","speed",3f,"Ejection speed in m/s (0.5-15).");
        spread=Config.Bind("Defaults","spread",8f,"Cone half angle in degrees (0-30).");
        spin=Config.Bind("Defaults","spin",180f,"Spin in degrees per second (0-720).");
        count=Config.Bind("Defaults","count",1,"Objects per rising edge (1-8).");
        continuous=Config.Bind("Defaults","continuous",false,"Repeat while the Boolean output stays on.");
        interval=Config.Bind("Defaults","interval",.5f,"Repeat interval in seconds (0.05-5).");
        lifetime=Config.Bind("Defaults","lifetime",10f,"Object lifetime in seconds (2-60).");
        inherit=Config.Bind("Defaults","inherit",true,"Read the velocity of the vehicle at the outlet.");
        MaxObjects=Config.Bind("Limits","MaxObjects",64,"Hard pool capacity, resting objects included (1-512). Restart required.");
        MaxCasts=Config.Bind("Limits","MaxCastsPerFrame",512,"Hard cast budget per frame (1-4096). Exhausted objects wait, without tunnelling.");
        MaxLifetime=Config.Bind("Limits","MaxLifetime",60f,"Absolute lifetime cap in seconds (2-60).");
        CasingLength=Config.Bind("Casing","LengthFactor",6.08f,"Length / diameter (0.1-20).");
        ProjectileLength=Config.Bind("Projectile","LengthFactor",3f,"Length / diameter (0.1-20).");
        Slope=Config.Bind("Slope","MaxDegrees",35f,"Maximum resting slope (0-80 degrees).");
        Friction=Config.Bind("Slope","Friction",.65f,"Sliding damping (0-10 /s).");
        var runtime=AddComponent<EmitterRuntime>();runtime.RegisterIo();
        Log.LogInfo("Sprocket Emitter "+Version+" prepared development build; cosmetic output, zero patches.");
    }
    internal void Trace(string message){if(Detailed.Value)Log.LogInfo("[EMITTER] "+message);}
}
