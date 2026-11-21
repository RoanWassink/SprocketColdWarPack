using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Fires;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using UnityEngine;
using System.Reflection;
using System.Security.Cryptography;
using System.Diagnostics;
using Il2CppInterop.Runtime;
namespace SprocketVisualEffects;

[BepInPlugin("sprocket.visualeffects","Sprocket Visual Effects","0.5.4")]
[BepInDependency("sprocket.telescopicmast",BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin:BasePlugin
{
    internal static Plugin Instance=null!;
    internal ConfigEntry<bool> Enabled=null!,Detailed=null!;
    internal ConfigEntry<float> Minimum=null!,Multiplier=null!,Distance=null!,VolumetricDensity=null!,VolumetricScale=null!;
    internal ConfigEntry<int> Capacity=null!,RaymarchSteps=null!,MaxVolumes=null!,MaxSparks=null!,MaxImpactVolumes=null!;
    internal ConfigEntry<bool> FallbackToBillboard=null!,MuzzleLight=null!,GroundDust=null!,LegacyVolumeSmoke=null!;
    internal ConfigEntry<bool> MuzzleSparks=null!,MuzzleShockwave=null!;
    internal ConfigEntry<bool> ImpactsEnabled=null!,ImpactSparks=null!,ImpactShockwaves=null!,ImpactLights=null!,CookoffEffects=null!;
    internal ConfigEntry<float> MuzzleFireIntensity=null!,ImpactScaleMultiplier=null!,SparkIntensity=null!,ExplosionFireballIntensity=null!,ImpactMaxDistance=null!;
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
    private Harmony? inspector,observer,impacts,detonate;
    private int errors,impactErrors,detonateErrors;
    internal void Warn(Exception e){if(errors++<3)Log.LogWarning("Optional shot overlay disabled/skipped: "+e.Message);}
    internal void WarnImpact(Exception e){if(impactErrors++<3)Log.LogWarning("Optional impact overlay skipped: "+e.Message);}
    internal void WarnDetonate(Exception e){if(detonateErrors++<3)Log.LogWarning("Optional cookoff overlay skipped: "+e.Message);}
    internal void Trace(string s){if(Detailed.Value)Log.LogInfo(s);}
    public override void Load()
    {
        Instance=this;
        Enabled=Config.Bind("Visuals","Enabled",true,"Master switch for cannon-shot, impact, explosion and lamp VFX. Native flash/audio/damage stay active.");
        Detailed=Config.Bind("Diagnostics","DetailedLogging",true,"Verbose trace lines for testing. Warnings and errors are always logged.");
        Minimum=Config.Bind("Visuals","MinimumCalibreMm",50f,new ConfigDescription("Minimum cannon calibre in millimetres for muzzle overlay.",new AcceptableValueRange<float>(20,500)));
        Multiplier=Config.Bind("Visuals","ScaleMultiplier",1f,new ConfigDescription("Visual scale; calibre/120mm is separately clamped to 0.35-2.5.",new AcceptableValueRange<float>(.25f,3)));
        Capacity=Config.Bind("Visuals","MaxConcurrentEffects",12,new ConfigDescription("Preallocated billboard fallback pool. Restart to change.",new AcceptableValueRange<int>(1,24)));
        Distance=Config.Bind("Visuals","MaxDistance",1500f,new ConfigDescription("Maximum camera distance in metres.",new AcceptableValueRange<float>(50,5000)));
        RaymarchSteps=Config.Bind("Visuals","RaymarchSteps",24,new ConfigDescription("Volumetric view samples per pixel. Restart to change; shader clamps to 16-36.",new AcceptableValueRange<int>(16,36)));
        MaxVolumes=Config.Bind("Visuals","MaxVolumes",4,new ConfigDescription("Preallocated volumetric muzzle proxies, oldest shot recycled when full. Restart to change.",new AcceptableValueRange<int>(1,8)));
        VolumetricDensity=Config.Bind("Visuals","VolumetricDensity",1f,new ConfigDescription("Volumetric smoke opacity multiplier on the daylight-calibrated base density. Higher = more solid smoke.",new AcceptableValueRange<float>(.25f,3)));
        VolumetricScale=Config.Bind("Visuals","VolumetricScale",1f,new ConfigDescription("Volumetric plume size multiplier; calibre/120mm is separately clamped to 0.35-2.5.",new AcceptableValueRange<float>(.5f,2.5f)));
        MuzzleLight=Config.Bind("Visuals","MuzzleLight",true,"Short HDRP point-light flash per shot (pool of 4, ~0.14 s, no shadows) that lights barrel, tank and ground.");
        MuzzleFireIntensity=Config.Bind("Visuals","MuzzleFireIntensity",1.3f,new ConfigDescription("Muzzle fireball luminance, corona flash and point-light multiplier.",new AcceptableValueRange<float>(.3f,3f)));
        MuzzleSparks=Config.Bind("Visuals","MuzzleSparks",true,"Emit bright hot propellant sparks from the cannon muzzle on each shot.");
        MuzzleShockwave=Config.Bind("Visuals","MuzzleShockwave",true,"Emit a rapid supersonic blast shockwave ring from the cannon muzzle.");
        MaxSparks=Config.Bind("Visuals","MaxSparks",1600,new ConfigDescription("Global pool capacity for velocity-stretched HDR sparks across shots and impacts. Restart to change.",new AcceptableValueRange<int>(128,3000)));
        WindSpeed=Config.Bind("Visuals","WindSpeed",2.5f,new ConfigDescription("Wind for smoke and dust puffs (m/s); stronger higher up.",new AcceptableValueRange<float>(0,12)));
        WindDirection=Config.Bind("Visuals","WindDirection",200f,new ConfigDescription("Direction the wind blows TOWARDS, degrees (0 = world +Z, 90 = world +X).",new AcceptableValueRange<float>(0,360)));
        MaxPuffs=Config.Bind("Visuals","MaxPuffs",320,new ConfigDescription("Puff budget for smoke, dust, soot and dirt over all shots and impacts; oldest recycled first.",new AcceptableValueRange<int>(32,600)));
        LegacyVolumeSmoke=Config.Bind("Visuals","LegacyVolumeSmoke",false,"Also keep the 0.2.3 raymarched smoke cloud and dust volume (off: the volume only draws the fire/blast and jet for 0.6 s; smoke and dust are lit puffs).");
        GroundDust=Config.Bind("Visuals","GroundDust",true,"Ring of kicked-up ground dust below and in front of the muzzle (one raycast per shot, no hook).");
        FallbackToBillboard=Config.Bind("Visuals","FallbackToBillboard",true,"Use the existing 0.1.0 billboard overlay if the volumetric bundle, shader or material fails to load.");

        ImpactsEnabled=Config.Bind("Impacts","Enabled",true,"Explosive impact fireballs, blazing metallic sparks, dirt geysers and shockwaves.");
        ImpactSparks=Config.Bind("Impacts","Sparks",true,"Blazing velocity-stretched HDR sparks on ricochets, penetrations, non-penetrations and explosions.");
        ImpactShockwaves=Config.Bind("Impacts","Shockwaves",true,"Supersonic shockwave rings and flash coronas on impacts and explosions.");
        ImpactLights=Config.Bind("Impacts","ImpactLights",true,"Dynamic HDRP point-light flashes (no shadows) illuminating armor and terrain on hits and explosions.");
        CookoffEffects=Config.Bind("Impacts","CookoffEffects",true,"Massive upward spark fountain, fireball and oily smoke column on ammo-rack detonations.");
        ImpactScaleMultiplier=Config.Bind("Impacts","ScaleMultiplier",1.2f,new ConfigDescription("Overall size and reach multiplier for impact/explosion fireballs and sparks.",new AcceptableValueRange<float>(.4f,2.5f)));
        SparkIntensity=Config.Bind("Impacts","SparkIntensity",1.25f,new ConfigDescription("Spark count multiplier across impacts and muzzle shots.",new AcceptableValueRange<float>(0f,3f)));
        ExplosionFireballIntensity=Config.Bind("Impacts","ExplosionFireballIntensity",1.3f,new ConfigDescription("3D explosion fireball luminance and glowing puff fire multiplier.",new AcceptableValueRange<float>(0f,3f)));
        MaxImpactVolumes=Config.Bind("Impacts","MaxImpactVolumes",6,new ConfigDescription("Preallocated 3D volumetric explosion fireball proxies. Restart to change.",new AcceptableValueRange<int>(1,12)));
        ImpactMaxDistance=Config.Bind("Impacts","MaxDistance",1800f,new ConfigDescription("Maximum camera distance in metres for impact and explosion effects.",new AcceptableValueRange<float>(100,5000)));

        SmokeAmount=Config.Bind("Smoke","SmokeAmount",1f,new ConfigDescription("Number of smoke puffs per shot (1 = modern smokeless gun, ~16 at 120 mm; 0 = no smoke).",new AcceptableValueRange<float>(0,2)));
        SmokeSize=Config.Bind("Smoke","SmokeSize",1f,new ConfigDescription("Smoke puff size (1 = grows to 4-7 m).",new AcceptableValueRange<float>(.5f,2)));
        SmokeLifetime=Config.Bind("Smoke","SmokeLifetime",1f,new ConfigDescription("How long smoke lingers (1 = 2-4 s, mostly gone after 1.5-3 s).",new AcceptableValueRange<float>(.5f,2)));
        SmokeBrightness=Config.Bind("Smoke","SmokeBrightness",1f,new ConfigDescription("Smoke colour brightness (1 = mid grey in full sun; higher = whiter, lower = darker).",new AcceptableValueRange<float>(.3f,1.5f)));
        DustAmount=Config.Bind("Smoke","DustAmount",1f,new ConfigDescription("Ground dust kicked up per shot (1 = ~18 low sand puffs lasting 3-6 s; 0 = none).",new AcceptableValueRange<float>(0,2)));
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
        // Separate owner: an inspector failure unpatches only itself, never the shot/impact observers.
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
            Driver=AddComponent<EffectDriver>();
            if(!HookGuard.Compatible(out var reason)){Log.LogWarning("Shot overlay not patched: "+reason);}
            else
            {
                observer=new Harmony("sprocket.visualeffects.observer");
                observer.Patch(HookGuard.Method(),postfix:new HarmonyMethod(typeof(Plugin),nameof(AfterSetup)));
                Trace("Prepared observer Setup RVA 0x2104760 length 0xAF3 ("+reason+"); Author Nero.");
            }
        }
        catch(Exception ex){observer?.UnpatchSelf();observer=null;Warn(ex);}

        try
        {
            if(!HookGuard.ImpactsCompatible(out var reason)){Log.LogWarning("Impact overlay not patched: "+reason);}
            else
            {
                impacts=new Harmony("sprocket.visualeffects.impacts");
                impacts.Patch(HookGuard.ImpactMethod(),postfix:new HarmonyMethod(typeof(Plugin),nameof(AfterImpact)));
                Log.LogInfo("Impact overlay: postfix on ProjectileEffectConfig.PlayEffect (RVA 0x21015E0 length 0x2F3, unique) - "+reason);
            }
        }
        catch(Exception ex){impacts?.UnpatchSelf();impacts=null;Log.LogWarning("Impact overlay disabled: "+ex.Message);}

        try
        {
            if(!HookGuard.DetonateCompatible(out var reason)){Log.LogWarning("Cookoff overlay not patched: "+reason);}
            else
            {
                detonate=new Harmony("sprocket.visualeffects.detonate");
                detonate.Patch(HookGuard.DetonateMethod(),postfix:new HarmonyMethod(typeof(Plugin),nameof(AfterDetonate)));
                Log.LogInfo("Cookoff overlay: postfix on Explosive.Detonate (RVA 0x208D7D0 length 0x81E, unique) - "+reason);
            }
        }
        catch(Exception ex){detonate?.UnpatchSelf();detonate=null;Log.LogWarning("Cookoff overlay disabled: "+ex.Message);}
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
    private static void AfterImpact(ProjectileEffectConfig __instance,ProjectileEffectInfo __0)
    {
        try
        {
            var p=Instance;
            if(!p.Enabled.Value||!p.ImpactsEnabled.Value||p.Driver==null||!p.Driver.Ready||!p.Driver.IsMainThread||__0==null)return;
            var t=__0.Type;
            var kind=(t&ProjectileEffectType.Explosion)!=0?ImpactMath.ResolveExplosionKind(BallisticsApiClient.ReadExplosionKind())
                :(t&ProjectileEffectType.Ricochet)!=0?ImpactKind.Ricochet
                :(t&ProjectileEffectType.Penetration)!=0?ImpactKind.Penetration
                :(t&ProjectileEffectType.NonPenetration)!=0?ImpactKind.NonPenetration
                :(t&ProjectileEffectType.EnvironmentImpact)!=0?ImpactKind.EnvironmentImpact
                :(ImpactKind)255;
            if((byte)kind==255)return;
            p.Driver.EmitImpact(kind,__0.Position,__0.HitNormal,__0.HitVelocity,(float)__0.Calibre);
        }
        catch(Exception ex){Instance.WarnImpact(ex);}
    }
    private static void AfterDetonate(Explosive __instance)
    {
        try
        {
            var p=Instance;
            if(!p.Enabled.Value||!p.ImpactsEnabled.Value||!p.CookoffEffects.Value||p.Driver==null||!p.Driver.Ready||!p.Driver.IsMainThread||__instance==null)return;
            p.Driver.EmitCookoff(__instance.transform.position,__instance.mass,__instance.destructionImpulse);
        }
        catch(Exception ex){Instance.WarnDetonate(ex);}
    }
    public override bool Unload(){inspector?.UnpatchSelf();observer?.UnpatchSelf();impacts?.UnpatchSelf();detonate?.UnpatchSelf();Driver?.Dispose();Lamps?.Dispose();return true;}
}

internal static class HookGuard
{
    private static readonly string[] AuditedHashes={
        "77FC601748991DC2101435C6B72C7A5A4B3DC3C68429B349C2EFEE711F6D5388",
        "3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B"
    };
    private static readonly Lazy<string?> gameHash=new(()=>{
        try{
            using var stream=File.OpenRead(Path.Combine(Paths.GameRootPath,"GameAssembly.dll"));
            using var sha=SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }catch{return null;}
    });
    internal static bool IsAuditedGameHash(out string reason)
    {
        var h=gameHash.Value;
        if(h!=null&&AuditedHashes.Contains(h,StringComparer.OrdinalIgnoreCase)){reason="GameAssembly "+h[..8]+" matches audited build";return true;}
        reason="GameAssembly hash differs from audited build";return false;
    }
    private static unsafe bool CheckNativeMethod(Type type,string fieldPrefix,int expectedToken,long expectedRva,out string reason)
    {
        var fields=type.GetFields(BindingFlags.NonPublic|BindingFlags.Static).Where(f=>f.Name.StartsWith(fieldPrefix)).ToArray();
        if(fields.Length!=1){reason="Native pointer field missing/ambiguous";return false;}
        var info=(IntPtr)fields[0].GetValue(null)!;
        var nativeInfo=Il2CppInterop.Runtime.Runtime.UnityVersionHandler.Wrap((Il2CppInterop.Runtime.Runtime.Il2CppMethodInfo*)info);
        if(nativeInfo.Token!=expectedToken){reason="Native token differs";return false;}
        var native=nativeInfo.MethodPointer;
        using var process=Process.GetCurrentProcess();
        var module=process.Modules.Cast<ProcessModule>().Single(x=>string.Equals(x.ModuleName,"GameAssembly.dll",StringComparison.OrdinalIgnoreCase));
        if(native.ToInt64()-module.BaseAddress.ToInt64()!=expectedRva){reason="Native address differs from unique audited RVA";return false;}
        reason="hash, exact signature and unique native RVA match";return true;
    }
    internal static MethodInfo Method()=>typeof(MuzzleFlashEffect).GetMethod("Setup",new[]{typeof(CannonMuzzleFlashEffectInfo),typeof(Transform),typeof(Vector3),typeof(Quaternion),typeof(bool)})??throw new MissingMethodException("Setup signature changed");
    internal static MethodInfo ImpactMethod()=>typeof(ProjectileEffectConfig).GetMethod("PlayEffect",new[]{typeof(ProjectileEffectInfo)})??throw new MissingMethodException("PlayEffect signature changed");
    internal static MethodInfo DetonateMethod()=>typeof(Explosive).GetMethod("Detonate",Type.EmptyTypes)??throw new MissingMethodException("Detonate signature changed");
    internal static unsafe bool Compatible(out string reason)
    {
        if(!IsAuditedGameHash(out reason))return false;
        var m=Method();
        if(m.ReturnType!=typeof(void)){reason="Setup return signature differs";return false;}
        if(System.Runtime.InteropServices.Marshal.SizeOf<CannonMuzzleFlashEffectInfo>()!=12||System.Runtime.InteropServices.Marshal.OffsetOf<CannonMuzzleFlashEffectInfo>(nameof(CannonMuzzleFlashEffectInfo.Calibre)).ToInt32()!=0){reason="Calibre layout differs";return false;}
        return CheckNativeMethod(typeof(MuzzleFlashEffect),"NativeMethodInfoPtr_Setup_",100663384,0x2104760,out reason);
    }
    internal static unsafe bool ImpactsCompatible(out string reason)
    {
        if(!IsAuditedGameHash(out reason))return false;
        var m=ImpactMethod();
        if(m.ReturnType!=typeof(void)||m.GetParameters().Length!=1){reason="PlayEffect signature differs";return false;}
        var ptr=Il2CppClassPointerStore<ProjectileEffectInfo>.NativeClassPtr;
        if(ptr==IntPtr.Zero){reason="ProjectileEffectInfo NativeClassPtr null";return false;}
        uint align=0;
        if(IL2CPP.il2cpp_class_value_size(ptr,ref align)!=0x38){reason="ProjectileEffectInfo size differs";return false;}
        foreach(var fn in new[]{"NativeFieldInfoPtr_Type","NativeFieldInfoPtr_Position","NativeFieldInfoPtr_HitNormal","NativeFieldInfoPtr_HitVelocity","NativeFieldInfoPtr_Parent","NativeFieldInfoPtr_Calibre"})
        {
            var f=typeof(ProjectileEffectInfo).GetField(fn,BindingFlags.NonPublic|BindingFlags.Static);
            if(f==null||((IntPtr)f.GetValue(null)!)==IntPtr.Zero){reason="ProjectileEffectInfo missing "+fn;return false;}
        }
        return CheckNativeMethod(typeof(ProjectileEffectConfig),"NativeMethodInfoPtr_PlayEffect_",100663340,0x21015E0,out reason);
    }
    internal static unsafe bool DetonateCompatible(out string reason)
    {
        if(!IsAuditedGameHash(out reason))return false;
        var m=DetonateMethod();
        if(m.ReturnType!=typeof(void)||m.GetParameters().Length!=0){reason="Detonate signature differs";return false;}
        return CheckNativeMethod(typeof(Explosive),"NativeMethodInfoPtr_Detonate_",100663305,0x208D7D0,out reason);
    }
}
