namespace SprocketEngineExpansion.Core;

public sealed record EngineCharacterProfile(
    double InertiaMultiplier,
    double FrictionMultiplier,
    double ImmediateThrottleShare,
    double SpoolUpTauSeconds,
    double SpoolDownTauSeconds,
    double SpoolHoldSeconds,
    double IdleSpoolFloor,
    double IdleGovernorPassthrough,
    double IdleFuelFloor,
    double PartLoadFuelScale,
    double MaxIdleFuelMultiplier,
    double FireIgnitionChance,
    double FireDurationMultiplier,
    double ExhaustSootSurge,
    double ExhaustBaseBurnScale,
    double ExhaustMinRate,
    double NativeAudioPitchScale,
    double NativeAudioVolumeScale,
    double TurbineWhineGain,
    double TurboWhistleGain);

/// <summary>
/// Pure-math character rules distinguishing Petrol, Diesel and Gas Turbine engines across
/// inertia, engine braking, spool response, idle/part-load fuel consumption, exhaust soot,
/// combat fire risk and sound profile. Petrol stays exact 1.0x native baseline.
/// </summary>
public static class EngineCharacterRules
{
    public static readonly EngineCharacterProfile Petrol=new(
        InertiaMultiplier:1.0,
        FrictionMultiplier:1.0,
        ImmediateThrottleShare:1.0,
        SpoolUpTauSeconds:0.0,
        SpoolDownTauSeconds:0.0,
        SpoolHoldSeconds:0.0,
        IdleSpoolFloor:1.0,
        IdleGovernorPassthrough:1.0,
        IdleFuelFloor:0.0,
        PartLoadFuelScale:1.0,
        MaxIdleFuelMultiplier:1.0,
        FireIgnitionChance:1.0,
        FireDurationMultiplier:1.0,
        ExhaustSootSurge:0.0,
        ExhaustBaseBurnScale:1.0,
        ExhaustMinRate:0.0,
        NativeAudioPitchScale:1.0,
        NativeAudioVolumeScale:1.0,
        TurbineWhineGain:0.0,
        TurboWhistleGain:0.0);

    public static readonly EngineCharacterProfile Diesel=new(
        InertiaMultiplier:1.40,
        FrictionMultiplier:1.50,
        ImmediateThrottleShare:0.76,
        SpoolUpTauSeconds:0.32,
        SpoolDownTauSeconds:0.45,
        SpoolHoldSeconds:0.95,
        IdleSpoolFloor:0.25,
        IdleGovernorPassthrough:0.22,
        IdleFuelFloor:0.0,
        PartLoadFuelScale:0.78,
        MaxIdleFuelMultiplier:1.0,
        FireIgnitionChance:0.35,
        FireDurationMultiplier:0.80,
        ExhaustSootSurge:0.85,
        ExhaustBaseBurnScale:1.05,
        ExhaustMinRate:0.05,
        NativeAudioPitchScale:0.68,
        NativeAudioVolumeScale:0.55,
        TurbineWhineGain:0.0,
        TurboWhistleGain:0.78);

    public static readonly EngineCharacterProfile GasTurbine=new(
        InertiaMultiplier:1.25,
        FrictionMultiplier:0.30,
        ImmediateThrottleShare:0.30,
        SpoolUpTauSeconds:0.85,
        SpoolDownTauSeconds:1.35,
        SpoolHoldSeconds:1.10,
        IdleSpoolFloor:0.30,
        IdleGovernorPassthrough:0.24,
        IdleFuelFloor:0.42,
        PartLoadFuelScale:1.0,
        MaxIdleFuelMultiplier:4.50,
        FireIgnitionChance:0.55,
        FireDurationMultiplier:0.90,
        ExhaustSootSurge:0.0,
        ExhaustBaseBurnScale:0.14,
        ExhaustMinRate:0.42,
        NativeAudioPitchScale:0.95,
        NativeAudioVolumeScale:0.16,
        TurbineWhineGain:0.85,
        TurboWhistleGain:0.0);

    public static EngineCharacterProfile For(EngineKind kind)=>kind switch
    {
        EngineKind.Petrol=>Petrol,
        EngineKind.Diesel=>Diesel,
        EngineKind.GasTurbine=>GasTurbine,
        _=>throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static double StepSpool(double currentSpool,double requestedThrottle,bool running,double dt,EngineCharacterProfile p)=>
        StepSpoolState(currentSpool,0.0,requestedThrottle,running,dt,p).Spool;

    public static (double Spool,double HoldRemaining) StepSpoolState(
        double currentSpool,double holdRemaining,double requestedThrottle,bool running,double dt,EngineCharacterProfile p)
    {
        double cur=double.IsFinite(currentSpool)?Math.Clamp(currentSpool,0,1):0;
        double hold=double.IsFinite(holdRemaining)?Math.Max(0,holdRemaining):0;
        double req=double.IsFinite(requestedThrottle)?Math.Clamp(requestedThrottle,0,1):0;
        double stepDt=double.IsFinite(dt)?Math.Clamp(dt,0,0.25):0;
        if(!running)
        {
            if(p.SpoolDownTauSeconds<=1e-6)return (0,0);
            double offAlpha=1-Math.Exp(-stepDt/Math.Max(0.15,p.SpoolDownTauSeconds));
            return (Math.Clamp(cur+(0-cur)*offAlpha,0,1),0);
        }
        double target=Math.Max(req,p.IdleSpoolFloor);
        if(p.SpoolUpTauSeconds<=1e-6)return (target,0);
        if(target>=cur)
        {
            double nextHold=req>p.IdleGovernorPassthrough?p.SpoolHoldSeconds:hold;
            double upAlpha=1-Math.Exp(-stepDt/p.SpoolUpTauSeconds);
            return (Math.Clamp(cur+(target-cur)*upAlpha,0,1),nextHold);
        }
        // Throttle dropped below current spool: hold briefly across automatic gear shifts, then decay smoothly.
        double held=Math.Min(hold,stepDt);
        double remHold=hold-held;
        double decayDt=stepDt-held;
        if(decayDt<=1e-9)return (cur,remHold);
        double downAlpha=1-Math.Exp(-decayDt/Math.Max(1e-6,p.SpoolDownTauSeconds));
        return (Math.Clamp(cur+(target-cur)*downAlpha,0,1),remHold);
    }

    public static double EffectiveThrottle(double requestedThrottle,double spool,EngineCharacterProfile p)
    {
        double req=double.IsFinite(requestedThrottle)?Math.Clamp(requestedThrottle,0,1):0;
        if(p.ImmediateThrottleShare>=1.0-1e-6||req<=p.IdleGovernorPassthrough)return req;
        double s=double.IsFinite(spool)?Math.Clamp(spool,0,1):0;
        double floor=Math.Clamp(p.IdleSpoolFloor,0,0.99);
        double progress=Math.Clamp((s-floor)/(1.0-floor),0,1);
        double instant=p.IdleGovernorPassthrough+(req-p.IdleGovernorPassthrough)*p.ImmediateThrottleShare;
        return Math.Clamp(instant+(req-instant)*progress,0,req);
    }

    public static double LoadFuelMultiplier(double fuelInjectionFraction,bool running,EngineCharacterProfile p)
    {
        if(!running)return 1.0;
        double inj=double.IsFinite(fuelInjectionFraction)?Math.Clamp(fuelInjectionFraction,0,1):0;
        if(p.IdleFuelFloor>0&&inj<p.IdleFuelFloor)
        {
            double denom=Math.Max(inj,p.IdleFuelFloor/Math.Max(1.0,p.MaxIdleFuelMultiplier));
            return Math.Min(p.MaxIdleFuelMultiplier,p.IdleFuelFloor/denom);
        }
        return p.PartLoadFuelScale+(1.0-p.PartLoadFuelScale)*inj;
    }

    public static (double Rate,double Throttle,double FuelBurn) AdaptExhaust(
        double rate,double throttle,double fuelBurn,double requestedThrottle,double spool,bool running,EngineCharacterProfile p)
    {
        if(!running)return (rate,throttle,fuelBurn);
        double req=double.IsFinite(requestedThrottle)?Math.Clamp(requestedThrottle,0,1):0;
        double s=double.IsFinite(spool)?Math.Clamp(spool,0,1):0;
        double baseRate=double.IsFinite(rate)?rate:0;
        double baseBurn=double.IsFinite(fuelBurn)?fuelBurn:0;
        double surge=Math.Max(0,req-s);
        double outRate=Math.Clamp(Math.Max(baseRate,p.ExhaustMinRate*(0.6+0.4*s)),0,1);
        double outBurn=Math.Clamp(baseBurn*p.ExhaustBaseBurnScale+surge*p.ExhaustSootSurge,0,1);
        return (outRate,double.IsFinite(throttle)?Math.Clamp(throttle,0,1):0,outBurn);
    }

    public static (bool Ignites,double Duration) FireOutcome(double roll01,double nativeDuration,EngineCharacterProfile p)
    {
        double roll=double.IsFinite(roll01)?roll01:0;
        double baseDur=double.IsFinite(nativeDuration)?nativeDuration:1.0;
        bool ignites=roll<p.FireIgnitionChance;
        double duration=Math.Max(0.5,baseDur*p.FireDurationMultiplier);
        return (ignites,duration);
    }

    public static (double NativePitchScale,double NativeVolumeScale,double AuxPitch,double AuxVolume) ComputeAudio(
        EngineKind kind,double spool,double requestedThrottle,bool running,EngineCharacterProfile p)=>
        ComputeAudio(kind,spool,requestedThrottle,running,0.35,p);

    public static (double NativePitchScale,double NativeVolumeScale,double AuxPitch,double AuxVolume) ComputeAudio(
        EngineKind kind,double spool,double requestedThrottle,bool running,double rpmFraction,EngineCharacterProfile p)
    {
        double s=double.IsFinite(spool)?Math.Clamp(spool,0,1):0;
        double req=double.IsFinite(requestedThrottle)?Math.Clamp(requestedThrottle,0,1):0;
        double r=double.IsFinite(rpmFraction)?Math.Clamp(rpmFraction,0.15,1.15):0.35;
        double auxPitch=1.0,auxVol=0.0;
        if(kind==EngineKind.GasTurbine)
        {
            auxPitch=0.55+0.85*s;
            auxVol=(running||s>0.02)?p.TurbineWhineGain*(running?0.42+0.58*s:s*0.75):0.0;
        }
        else if(kind==EngineKind.Diesel)
        {
            auxPitch=0.45+1.35*r;
            auxVol=running?p.TurboWhistleGain*(0.28+0.72*Math.Clamp(0.35*r+0.65*req,0,1)):0.0;
        }
        return (p.NativeAudioPitchScale,p.NativeAudioVolumeScale,auxPitch,auxVol);
    }

    public static string Summary(EngineKind kind)=>kind switch
    {
        EngineKind.Petrol=>"Petrol: instant throttle, medium engine brake, higher fire risk.",
        EngineKind.Diesel=>"Diesel: +40% inertia, strong engine brake, soot surge, economical cruise, low fire risk.",
        EngineKind.GasTurbine=>"Turbine: 0.80x compact pack, spool lag, low engine brake, high idle burn, turbine whine.",
        _=>""
    };
}
