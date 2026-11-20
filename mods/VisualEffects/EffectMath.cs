using System.Numerics;
namespace SprocketVisualEffects;

internal static class EffectMath
{
    internal const float Duration=2.64f;
    internal static float Safe(float value,float fallback,float min,float max)=>float.IsFinite(value)?Math.Clamp(value,min,max):fallback;
    internal static int Frame(float age)
    {
        if(!float.IsFinite(age)||age<0||age>=Duration)return -1;
        // Same row-major 64 frame schedule as effect-manifest.json, not uniform FPS.
        return age<.24f?Math.Min(23,(int)MathF.Floor(age*100f+1e-5f)):Math.Min(63,24+(int)MathF.Floor((age-.24f)/.06f+1e-5f));
    }
    internal static float Scale(float calibreMm,float multiplier)=>Math.Clamp(calibreMm/120f,.35f,2.5f)*Safe(multiplier,1,.25f,3);
    // HEAVY125mm artist preset. These mirror Field() in HeavyMuzzle.shader; the
    // shader still owns the density shape and receives only age/seed/options.
    internal const float HeavyLifetime=5.0f;
    internal const float HeavyFireFull=.12f;
    internal const float HeavyFireEnd=.15f;
    // 0.2.1: doubled after the daylight/sand calibration (out/proof-C2/calib2):
    // at 3.2 the 1.5-4 s smoke read as too thin against bright desert sand.
    internal const float HeavyDensity=6.4f;
    internal const float HeavyFireStrength=24f;
    internal const float HeavyCurlStrength=1f;
    // Grey-white smoke scattering colour (shader default, set per shot).
    internal const float HeavySmokeR=.62f,HeavySmokeG=.61f,HeavySmokeB=.58f;
    // Head ellipsoid axes in units of HeavyRadius; roll limit in radians.
    // B3 front model: bulb centred 0.25 R behind the front, 1.2 R ahead, 1.0 R behind, 0.8 R wide.
    internal const float HeavyHeadFront=1.2f,HeavyHeadBack=1.0f,HeavyHeadWide=.8f;
    internal const float HeavyRollLimit=1.7f;
    // Same asymmetric local proxy box as RayBox() in HeavyMuzzle.shader.
    internal const float BoxHalfWidth=5.5f,BoxTop=8f,BoxFront=13f,BoxBack=-4f,BoxBottom=-4.5f;
    // 0.2.2 ended the box at z=-1 behind the muzzle: smoke drifting back was cut by a
    // straight vertical edge. Same values as _BoxMin/_BoxMax defaults in the shader.
    internal static readonly Vector3 MuzzleBoxMin=new(-BoxHalfWidth,BoxBottom,BoxBack),MuzzleBoxMax=new(BoxHalfWidth,BoxTop,BoxFront);
    // Fire core as physical luminance (cd/m2) times exposure in the shader.
    internal const float HeavyFireLuminance=2800000f;
    // Muzzle flash point light (HDRP, candela = native unit for point lights).
    internal const float FlashPeakCandela=900000f,FlashRange=32f,FlashDuration=.14f,FlashForward=1.2f;
    internal const float FlashR=1f,FlashG=.62f,FlashB=.28f;
    internal const int FlashLightPool=4;
    // Ground dust ring (shader _Mode 1), placed by a downward raycast.
    internal const float DustLifetime=3f,DustDensity=2.4f,DustMaxDrop=8f;
    internal const float DustR=.86f,DustG=.75f,DustB=.57f;
    internal static readonly Vector3 DustBoxMin=new(-10f,-.5f,-8f),DustBoxMax=new(10f,4f,11f);
    internal const float HeavyCurlReach=.47f*.95f;
    private static float Smooth(float a,float b,float x)
    {
        var t=Math.Clamp((x-a)/(b-a),0,1);
        return t*t*(3-2*t);
    }
    internal static float HeavyHead(float age)
    {
        if(!float.IsFinite(age)||age<0)return 0;
        // Front of the gas leaving the barrel (shader HeadZ): 0 -> 4.6 m, tau 0.08 s.
        return 4.6f*(1-MathF.Exp(-age/.08f))+.22f*MathF.Max(0,age-.3f);
    }
    internal static float HeavyHeadSpeed(float age)
    {
        if(!float.IsFinite(age)||age<0)return 0;
        // Derivative of HeavyHead; the small late drift is included after 0.3 s.
        return 57.5f*MathF.Exp(-age/.08f)+(age>=.3f?.22f:0);
    }
    // Slow late spreading after 0.5 s; density thins by 1/growth.
    internal static float HeavyGrowth(float age)
    {
        if(!float.IsFinite(age)||age<0)return 1;
        return 1+.28f*MathF.Max(0,age-.5f);
    }
    internal static float HeavyRadius(float age)
    {
        if(!float.IsFinite(age)||age<0)return 0;
        return (.12f+1.5f*(1-MathF.Exp(-age/.12f)))*HeavyGrowth(age);
    }
    // Axial reach includes the forward-stretched head (1.8 * radius ahead).
    internal static float HeavyPlumeLength(float age)=>HeavyHead(age)+HeavyHeadFront*HeavyRadius(age);
    // Toroidal roll angle: bounded and decelerating, never winds up linearly.
    internal static float HeavyRollAngle(float age)
    {
        if(!float.IsFinite(age)||age<0)return 0;
        return HeavyRollLimit*(1-MathF.Exp(-age/.22f));
    }
    internal static float HeavyRise(float age)
    {
        if(!float.IsFinite(age)||age<0)return 0;
        return .5f*MathF.Max(0,age-.3f);
    }
    internal static float HeavyFire(float age)
    {
        if(!float.IsFinite(age)||age<0||age>=HeavyLifetime)return 0;
        return Smooth(0,.008f,age)*(1-Smooth(.04f,HeavyFireEnd,age));
    }
    internal static float HeavySmokeFade(float age)
    {
        if(!float.IsFinite(age)||age<0||age>=HeavyLifetime)return 0;
        // 0.2.3: thinning starts at 1 s and runs to 5 s (0.2.2: 2.3-4.5 s).
        return 1-Smooth(1.0f,HeavyLifetime,age);
    }
    // User multipliers (config VolumetricDensity / VolumetricScale), sanitised.
    internal static float VolumetricDensityFactor(float value)=>Safe(value,1,.25f,3);
    internal static float VolumetricScaleFactor(float value)=>Safe(value,1,.5f,2.5f);
    // Muzzle blast bubble grows at the muzzle (shader BlastRadius): 0.15 -> 1.75 m, tau 0.12 s.
    internal static float HeavyBlastRadius(float age)
    {
        if(!float.IsFinite(age)||age<0)return 0;
        return .15f+1.6f*(1-MathF.Exp(-age/.12f));
    }
    internal static float FlashCurve(float age)
    {
        if(!float.IsFinite(age)||age<=0)return 0;
        return Smooth(0,.01f,age)*(1-Smooth(.02f,FlashDuration,age));
    }
    // 0.3.0 / 0.5.0 puff, spark & blast-wave layers.
    internal const string PuffShaderName="SprocketVfx/SixWayPuff";
    internal const string SparkShaderName="SprocketVfx/ImpactSpark";
    internal const string BlastWaveShaderName="SprocketVfx/BlastWave";
    internal const float VolumeFireLifetime=.6f,VolumeSmokeFadeEnd=.5f;    // volume = fire/blast + jet only
    // 0.3.1: mid-grey, slightly cool smoke albedo; 0.5.0 adds explosion soot, dirt geyser and armour scorch tints.
    internal const float PuffSmokeR=.40f,PuffSmokeG=.42f,PuffSmokeB=.45f,PuffDustR=.64f,PuffDustG=.55f,PuffDustB=.42f;
    internal const float PuffSootR=.11f,PuffSootG=.105f,PuffSootB=.10f;
    internal const float PuffDirtR=.34f,PuffDirtG=.27f,PuffDirtB=.19f;
    internal const float PuffScorchR=.18f,PuffScorchG=.17f,PuffScorchB=.16f;
    internal const float PuffCookoffR=.07f,PuffCookoffG=.065f,PuffCookoffB=.06f;
    internal static int PuffCapacity(int value)=>Math.Clamp(value,32,600);
    internal static int SparkCapacity(int value)=>Math.Clamp(value,128,3000);
    internal static int ImpactVolumeCapacity(int value)=>Math.Clamp(value,1,12);
    // Display-relative sun strength from the directional light's lux and elevation
    // (dir.y of the direction TOWARDS the sun): full at >= 80 klx and > ~9 deg elevation.
    internal static float PuffSunStrength(float lux,float towardsSunY)
    {
        var l=float.IsFinite(lux)?Math.Clamp(lux/80000f,.12f,1f):.6f;
        var y=float.IsFinite(towardsSunY)?towardsSunY:.5f;var t=Math.Clamp((y+.05f)/.2f,0,1);
        return l*(.15f+.85f*t*t*(3-2*t));
    }
    internal static float PuffAmbient(float sunStrength)=>.12f+.2f*Math.Clamp(sunStrength,0,1);
    internal static int VolumeCapacity(int value)=>Math.Clamp(value,1,8);
    internal static int VolumeSteps(int value)=>Math.Clamp(value,16,36);
    internal static float FrontWeight(float alignment)
    {
        var t=Math.Clamp((MathF.Abs(alignment)-.55f)/.30f,0,1);
        return t*t*(3-2*t);
    }
    internal static Vector2 Uv(int frame,float x,float y)
    {
        if(frame is <0 or >63)throw new ArgumentOutOfRangeException(nameof(frame));
        // PNG top-left rows -> Unity bottom-left UV. Half texel avoids adjacent-tile bleed.
        return new Vector2((frame%8+(x==0?.5f/512f:1-.5f/512f))/8f,
            1-(frame/8+(y==0?.5f/256f:1-.5f/256f))/8f);
    }
    internal static Vector3 Corner(Vector3 horizontal,Vector3 vertical,float x,float y,bool front,float scale)
        =>horizontal*((x-(front?.5f:.1f))*7*scale)+vertical*((.5f-y)*3.5f*scale);
    internal static Vector3 SideVertical(Vector3 forward,Vector3 up,Vector3 view)
    {
        var normal=view-forward*Vector3.Dot(view,forward);
        var vertical=normal.LengthSquared()>1e-5f?Vector3.Normalize(Vector3.Cross(Vector3.Normalize(normal),forward)):up;
        return Vector3.Dot(vertical,up)<0?-vertical:vertical;
    }
    internal static int SelectSlot(float[] births,bool[] active)
    {
        if(births.Length!=active.Length||births.Length==0)throw new ArgumentException("Invalid pool");
        var oldest=0;
        for(var i=0;i<active.Length;i++){if(!active[i])return i;if(births[i]<births[oldest])oldest=i;}
        return oldest;
    }
}
