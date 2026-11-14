using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Sprocket.Vehicles.Fires;
using Sprocket.WeaponFramework;
using UnityEngine;
using System.Reflection;
using System.Security.Cryptography;
using System.Diagnostics;
using Il2CppInterop.Runtime;
namespace SprocketVisualEffects;

[BepInPlugin("sprocket.visualeffects","Sprocket Visual Effects","0.4.5")]
[BepInDependency("sprocket.telescopicmast",BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin:BasePlugin
{
    internal static Plugin Instance=null!;
    internal ConfigEntry<bool> Enabled=null!,Detailed=null!;
    internal ConfigEntry<float> Minimum=null!,Multiplier=null!,Distance=null!,VolumetricDensity=null!,VolumetricScale=null!;
    internal ConfigEntry<int> Capacity=null!,RaymarchSteps=null!,MaxVolumes=null!;
    internal ConfigEntry<bool> FallbackToBillboard=null!,MuzzleLight=null!,GroundDust=null!,LegacyVolumeSmoke=null!;
    internal ConfigEntry<float> WindSpeed=null!,WindDirection=null!;
    internal ConfigEntry<int> MaxPuffs=null!;
    internal ConfigEntry<float> SmokeAmount=null!,SmokeSize=null!,SmokeLifetime=null!,SmokeBrightness=null!,DustAmount=null!;
    internal EffectDriver? Driver;
    internal LampRuntime? Lamps;
    internal ConfigEntry<bool> LampDefaultOn=null!;
    internal ConfigEntry<int> MaxLamps=null!;
    internal ConfigEntry<float> LampDistance=null!,LensBrightness=null!,HaloStrength=null!;
    private readonly ConfigEntry<string>[] lampColors=new ConfigEntry<string>[3];
    private readonly ConfigEntry<float>[] lampIntensities=new ConfigEntry<float>[3],lampRanges=new ConfigEntry<float>[3];
    // An untouched 0.4.0-0.4.3 factory intensity (rect 8 cd, marker 2 cd) is read as the 0.4.4 default;
    // BepInEx keeps old values in an existing cfg, so changing only the code default would not reach the user.
    internal LampSettings LampDefaults(int form) {
        float intensity=LampSettings.MigrateIntensity(form,lampIntensities[form].Value);
        return new LampSettings{Color=lampColors[form].Value,Intensity=intensity,Range=lampRanges[form].Value}.Sanitize(LampSettings.Default(form));
    }
    private Harmony? inspector;
    private Harmony? observer;
    private int errors;
    internal void Warn(Exception e){if(errors++<3)Log.LogWarning("Optional shot overlay disabled/skipped: "+e.Message);}
    internal void Trace(string s){if(Detailed.Value)Log.LogInfo(s);}
    public override void Load()
    {
        Instance=this;
        Enabled=Config.Bind("Visuals","Enabled",true,"Optional cannon-shot overlay. Native flash/audio/damage stay active.");
        Detailed=Config.Bind("Diagnostics","DetailedLogging",false,"Verbose trace lines for testing. Warnings and errors are always logged.");
        Minimum=Config.Bind("Visuals","MinimumCalibreMm",50f,new ConfigDescription("Minimum cannon calibre in millimetres.",new AcceptableValueRange<float>(20,500)));
        Multiplier=Config.Bind("Visuals","ScaleMultiplier",1f,new ConfigDescription("Visual scale; calibre/120mm is separately clamped to 0.35-2.5.",new AcceptableValueRange<float>(.25f,3)));
        Capacity=Config.Bind("Visuals","MaxConcurrentEffects",12,new ConfigDescription("Preallocated pool. Restart to change capacity; oldest shot recycled when full.",new AcceptableValueRange<int>(1,24)));
        Distance=Config.Bind("Visuals","MaxDistance",1500f,new ConfigDescription("Maximum camera distance in metres.",new AcceptableValueRange<float>(50,5000)));
        RaymarchSteps=Config.Bind("Visuals","RaymarchSteps",24,new ConfigDescription("Volumetric view samples per pixel. Restart to change; shader clamps to 16-36.",new AcceptableValueRange<int>(16,36)));
        MaxVolumes=Config.Bind("Visuals","MaxVolumes",2,new ConfigDescription("Preallocated volumetric proxies, oldest shot recycled when full. Restart to change.",new AcceptableValueRange<int>(1,4)));
        VolumetricDensity=Config.Bind("Visuals","VolumetricDensity",1f,new ConfigDescription("Volumetric smoke opacity multiplier on the daylight-calibrated base density. Higher = more solid smoke.",new AcceptableValueRange<float>(.25f,3)));
        VolumetricScale=Config.Bind("Visuals","VolumetricScale",1f,new ConfigDescription("Volumetric plume size multiplier; calibre/120mm is separately clamped to 0.35-2.5. ScaleMultiplier only affects the billboard fallback.",new AcceptableValueRange<float>(.5f,2.5f)));
        MuzzleLight=Config.Bind("Visuals","MuzzleLight",true,"Short HDRP point-light flash per shot (pool of 2, ~0.12 s, no shadows) that lights barrel, tank and ground.");
        WindSpeed=Config.Bind("Visuals","WindSpeed",2.5f,new ConfigDescription("Wind for smoke and dust puffs (m/s); stronger higher up.",new AcceptableValueRange<float>(0,12)));
        WindDirection=Config.Bind("Visuals","WindDirection",200f,new ConfigDescription("Direction the wind blows TOWARDS, degrees (0 = world +Z, 90 = world +X).",new AcceptableValueRange<float>(0,360)));
        MaxPuffs=Config.Bind("Visuals","MaxPuffs",160,new ConfigDescription("Puff budget for smoke + dust over all shots; oldest recycled first.",new AcceptableValueRange<int>(32,400)));
        SmokeAmount=Config.Bind("Smoke","SmokeAmount",1f,new ConfigDescription("Number of smoke puffs per shot (1 = modern smokeless gun, ~16 at 120 mm; 0 = no smoke).",new AcceptableValueRange<float>(0,2)));
        SmokeSize=Config.Bind("Smoke","SmokeSize",1f,new ConfigDescription("Smoke puff size (1 = grows to 4-7 m).",new AcceptableValueRange<float>(.5f,2)));
        SmokeLifetime=Config.Bind("Smoke","SmokeLifetime",1f,new ConfigDescription("How long smoke lingers (1 = 2-4 s, mostly gone after 1.5-3 s).",new AcceptableValueRange<float>(.5f,2)));
        SmokeBrightness=Config.Bind("Smoke","SmokeBrightness",1f,new ConfigDescription("Smoke colour brightness (1 = mid grey in full sun; higher = whiter, lower = darker).",new AcceptableValueRange<float>(.3f,1.5f)));
        DustAmount=Config.Bind("Smoke","DustAmount",1f,new ConfigDescription("Ground dust kicked up per shot (1 = ~18 low sand puffs lasting 3-6 s; 0 = none).",new AcceptableValueRange<float>(0,2)));
        LegacyVolumeSmoke=Config.Bind("Visuals","LegacyVolumeSmoke",false,"Also keep the 0.2.3 raymarched smoke cloud and dust volume (off: the volume only draws the fire/blast and jet for 0.6 s; smoke and dust are lit puffs).");
        GroundDust=Config.Bind("Visuals","GroundDust",true,"Ring of kicked-up ground dust below and in front of the muzzle (one raycast per shot, no hook).");
        FallbackToBillboard=Config.Bind("Visuals","FallbackToBillboard",true,"Use the existing 0.1.0 billboard overlay if the volumetric bundle, shader or material fails to load.");
        LampDefaultOn=Config.Bind("Lamps","LampDefaultOn",false,"Without Mast, lamps start on in play. Designer always previews the lens only.");
        MaxLamps=Config.Bind("Lamps","MaxLamps",24,new ConfigDescription("Scene-wide real-light budget; extra lamps retain lens emission.",new AcceptableValueRange<int>(0,128)));
        LampDistance=Config.Bind("Lamps","MaxDistance",250f,new ConfigDescription("Real-light distance LOD; fades over the outer 20 percent.",new AcceptableValueRange<float>(10,2000)));
        for(int i=0;i<3;i++) {
            var d=LampSettings.Default(i);var shape=LampSettings.Forms[i];
            lampColors[i]=Config.Bind("Lamps",shape+"Color",d.Color,"Fallback RGB #RRGGBB. #000000 reserves IR (no visible light).");
            lampIntensities[i]=Config.Bind("Lamps",shape+"Intensity",d.Intensity,"Fallback physical intensity in candela.");
            lampRanges[i]=Config.Bind("Lamps",shape+"Range",d.Range,"Fallback range in metres.");
        }
        LensBrightness=Config.Bind("Lamps","LensBrightness",LampLook.MaxChannelDefault,new ConfigDescription("Brightest colour channel of a fully lit lens, in display units (exposure independent). Caps the hue so red stays red.",new AcceptableValueRange<float>(.25f,4)));
        HaloStrength=Config.Bind("Lamps","HaloStrength",.45f,new ConfigDescription("Opacity of the soft glow around a lit lens (0 = no halo).",new AcceptableValueRange<float>(0,1)));
        try{Lamps=AddComponent<LampRuntime>();Lamps.RegisterIo();}catch(Exception ex){Log.LogWarning("Lamp startup skipped: "+ex.Message);}
        // Separate owner: an inspector failure unpatches only itself, never the shot observer.
        try
        {
            if(Lamps==null)throw new InvalidOperationException("lamp runtime absent");
            if(!LampInspector.Compatible(out var reason)){Log.LogWarning("Lamp inspector section not patched: "+reason);}
            else
            {
                inspector=new Harmony("sprocket.visualeffects.inspector");
                inspector.Patch(LampInspector.Method(),postfix:new HarmonyMethod(typeof(LampInspector),nameof(LampInspector.AfterOnGUI)));
                Log.LogInfo("Lamp inspector: postfix on VehicleObjectEditor.OnGUI (RVA 0x1F92E50 length 0x36D, unique) - "+reason);
            }
        }
        catch(Exception ex){inspector?.UnpatchSelf();inspector=null;Log.LogWarning("Lamp inspector section disabled: "+ex.Message);}
        try
        {
            if(!HookGuard.Compatible(out var reason)){Log.LogWarning("Shot overlay not patched: "+reason);return;}
            Driver=AddComponent<EffectDriver>();
            observer=new Harmony("sprocket.visualeffects.observer");
            var method=HookGuard.Method();
            observer.Patch(method,postfix:new HarmonyMethod(typeof(Plugin),nameof(AfterSetup)));
            Trace("Prepared observer Setup RVA 0x2104760 length 0xAF3; development diagnostics enabled. Author Nero.");
        }
        catch(Exception ex){observer?.UnpatchSelf();Driver?.Dispose();Warn(ex);}
    }
    // Calibre is the one necessary by-value argument: Setup does not retain it.
    // No ref parameters, prefixes, result changes or native object writes.
    private static void AfterSetup(MuzzleFlashEffect __instance,CannonMuzzleFlashEffectInfo __0)
    {
        try
        {
            var p=Instance;
            if(!p.Enabled.Value||p.Driver==null||!p.Driver.Ready||!p.Driver.IsMainThread)return;
            var calibre=(float)__0.Calibre;
            if(calibre<EffectMath.Safe(p.Minimum.Value,50,20,500))return;
            var t=__instance.transform;
            // B3/B4 anchor diagnostic without extra hook parameters: Setup parents its own
            // transform (worldPositionStays) to the cannon side, so t.parent is that transform.
            p.Driver.Emit(t.position,t.rotation,calibre,t.parent);
        }
        catch(Exception ex){Instance.Warn(ex);}
    }
    public override bool Unload(){inspector?.UnpatchSelf();observer?.UnpatchSelf();Driver?.Dispose();Lamps?.Dispose();return true;}
}

internal static class HookGuard
{
    internal const string GameHash="3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B";
    internal static MethodInfo Method()=>typeof(MuzzleFlashEffect).GetMethod("Setup",new[]{typeof(CannonMuzzleFlashEffectInfo),typeof(Transform),typeof(Vector3),typeof(Quaternion),typeof(bool)})??throw new MissingMethodException("Setup signature changed");
    internal static unsafe bool Compatible(out string reason)
    {
        var file=Path.Combine(Paths.GameRootPath,"GameAssembly.dll");
        using var stream=File.OpenRead(file);
        using var sha=SHA256.Create();
        if(Convert.ToHexString(sha.ComputeHash(stream))!=GameHash){reason="GameAssembly hash differs from audited build";return false;}
        var m=Method();
        if(m.ReturnType!=typeof(void)){reason="Setup return signature differs";return false;}
        if(System.Runtime.InteropServices.Marshal.SizeOf<CannonMuzzleFlashEffectInfo>()!=12||System.Runtime.InteropServices.Marshal.OffsetOf<CannonMuzzleFlashEffectInfo>(nameof(CannonMuzzleFlashEffectInfo.Calibre)).ToInt32()!=0){reason="Calibre layout differs";return false;}
        var field=typeof(MuzzleFlashEffect).GetFields(BindingFlags.NonPublic|BindingFlags.Static).Single(f=>f.Name.StartsWith("NativeMethodInfoPtr_Setup_"));
        var info=(IntPtr)field.GetValue(null)!;
        var nativeInfo=Il2CppInterop.Runtime.Runtime.UnityVersionHandler.Wrap((Il2CppInterop.Runtime.Runtime.Il2CppMethodInfo*)info);
        if(nativeInfo.Token!=100663384){reason="Native Setup token differs";return false;}
        var native=nativeInfo.MethodPointer;
        using var process=Process.GetCurrentProcess();
        var module=process.Modules.Cast<ProcessModule>().Single(x=>string.Equals(x.ModuleName,"GameAssembly.dll",StringComparison.OrdinalIgnoreCase));
        if(native.ToInt64()-module.BaseAddress.ToInt64()!=0x2104760){reason="Setup native address differs from unique audited RVA";return false;}
        reason="hash, exact signature, struct layout and native RVA match";return true;
    }
}
