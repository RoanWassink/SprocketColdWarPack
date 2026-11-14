using System.Numerics;
namespace SprocketVisualEffects;

// 0.3.0 lit flipbook puffs (smoke + ground dust). Pure, closed-form and Unity-free:
// the same code drives the runtime (PuffRenderer), the editor proof (VfxProof,
// copied into the Unity project by prepare-project.py) and checks/EffectMathChecks.
// Parameters follow RESEARCH-EPIC-SMOKE.md section 5 (B/C), 120-125 mm = scale ~1.
internal struct Puff
{
    internal bool Dust;
    internal int Slice;            // Texture2DArray slice (0-2 smoke, 3-4 dust)
    internal float Born, Life;     // absolute spawn time and lifetime (s)
    internal Vector3 Origin, Dir;  // spawn point and initial direction (unit)
    internal float V0, Drag;       // initial speed (m/s) and exponential drag (1/s)
    internal float S0, S1;         // visible size start -> end (m)
    internal float Quad;           // quad size (m): S1 / final visible diameter fraction
    internal float Peak, Rot, Spin, Lift, Seed;
}

// 0.3.1 user sliders (config). 1 = the "modern smokeless 120/125 mm gun" defaults.
internal readonly struct PuffTuning
{
    internal readonly float SmokeAmount,SmokeSize,SmokeLifetime,SmokeBrightness,DustAmount;
    internal PuffTuning(float amount,float size,float life,float brightness,float dust)
    {
        static float C(float v,float lo,float hi)=>float.IsFinite(v)?Math.Clamp(v,lo,hi):1f;
        SmokeAmount=C(amount,0,2);SmokeSize=C(size,.5f,2);SmokeLifetime=C(life,.5f,2);SmokeBrightness=C(brightness,.3f,1.5f);DustAmount=C(dust,0,2);
    }
    internal static PuffTuning Default=>new(1,1,1,1,1);
}

internal readonly struct PuffState
{
    internal readonly Vector3 Center;     // quad centre (world)
    internal readonly float Quad, Rotation, Frame, Alpha, Heat, Dissolve;
    internal PuffState(Vector3 c,float q,float r,float f,float a,float h,float d){Center=c;Quad=q;Rotation=r;Frame=f;Alpha=a;Heat=h;Dissolve=d;}
}

internal static class PuffMath
{
    // 0.3.1 "modern smokeless tank gun" defaults (0.3.0 in brackets): a short, fast,
    // see-through grey gas cloud mostly gone in 1.5-3 s; the lasting volume is ground dust.
    internal const float SmokeDrag=3.5f,DustDrag=4f,GrowTau=.8f,LiftSeconds=1f,SpawnWindow=.25f;      // [3, 4, 1.5, 2, .35]
    internal const float SmokeLifeMin=2f,SmokeLifeMax=4f,DustLifeMin=3f,DustLifeMax=6f;               // [6-10, 4-7]
    internal const float SmokeV0Min=22f,SmokeV0Max=48f,ConeDegrees=8f;                                // [25-60, 9]
    internal const float SmokeS0Min=1f,SmokeS0Max=2f,SmokeS1Min=4f,SmokeS1Max=7f;                     // [1.5-3 -> 8-14]
    internal const float DustS0Min=2f,DustS0Max=4f,DustS1Min=6f,DustS1Max=10f;                        // [6-9]
    internal const float DustRingMin=7f,DustRingMax=11f,DustRingSeconds=.6f;                          // [6-10]
    internal const float SmokePeakMin=.12f,SmokePeakMax=.2f,DustPeak=.5f;                             // [.22-.34, .42]
    internal const float SmokeFadeStart=.12f,DustFadeStart=.5f,SmokeHeatSeconds=.1f;                  // [.3L, .2L, .15]
    internal const float MaxShearHeight=5f,ShearPerMetre=.12f;

    static float Smooth(float e0,float e1,float x){var t=Math.Clamp((x-e0)/(e1-e0),0,1);return t*t*(3-2*t);}
    static float Range(Random r,float a,float b)=>a+(float)r.NextDouble()*(b-a);

    // About half of 0.3.0 (33 in game at 120 mm): ~16 smoke puffs; dust ~18 (0.3.0: 12-20). Sliders scale the count.
    internal static int SmokeCount(float scale,float amount=1)=>amount<=0?0:Math.Clamp((int)MathF.Round(16*MathF.Sqrt(Math.Max(scale,0))*amount),2,40);
    internal static int DustCount(float scale,float amount=1)=>amount<=0?0:Math.Clamp((int)MathF.Round(18*MathF.Sqrt(Math.Max(scale,0))*amount),2,44);

    // Visible diameter (fraction of the quad) and frame lookup from the baked tables.
    internal static float FrameFor(int slice,float visibleFraction)
    {
        var d=FlipbookTables.Diameter[slice];
        if(visibleFraction<=d[0])return 0;
        for(var i=1;i<d.Length;i++)
            if(d[i]>=visibleFraction){var span=d[i]-d[i-1];return i-1+(span>1e-5f?(visibleFraction-d[i-1])/span:0);}
        return d.Length-1;
    }

    // One shot of smoke puffs out of the muzzle along `forward`.
    internal static void SpawnSmoke(Random r,Vector3 muzzle,Vector3 forward,Vector3 up,float scale,float now,Span<Puff> into,out int count)
        =>SpawnSmoke(r,muzzle,forward,up,scale,now,PuffTuning.Default,into,out count);
    internal static void SpawnSmoke(Random r,Vector3 muzzle,Vector3 forward,Vector3 up,float scale,float now,in PuffTuning t,Span<Puff> into,out int count)
    {
        count=Math.Min(SmokeCount(scale,t.SmokeAmount),into.Length);
        var side=Vector3.Normalize(Vector3.Cross(up,forward));if(!float.IsFinite(side.X))side=Vector3.UnitX;
        var cone=MathF.Tan(ConeDegrees*MathF.PI/180);
        var sq=MathF.Sqrt(Math.Clamp(scale,.35f,2.5f));
        for(var i=0;i<count;i++)
        {
            var a=Range(r,0,MathF.PI*2);var k=MathF.Sqrt((float)r.NextDouble())*cone;
            var dir=Vector3.Normalize(forward+side*(MathF.Cos(a)*k)+up*(MathF.Sin(a)*k));
            var slice=r.Next(FlipbookTables.SmokeSlices);var s1=Range(r,SmokeS1Min,SmokeS1Max)*sq*t.SmokeSize;
            into[i]=new Puff{Dust=false,Slice=slice,Born=now+Range(r,0,SpawnWindow),Life=Range(r,SmokeLifeMin,SmokeLifeMax)*t.SmokeLifetime,
                Origin=muzzle,Dir=dir,V0=Range(r,SmokeV0Min,SmokeV0Max)*sq,Drag=SmokeDrag,
                S0=Range(r,SmokeS0Min,SmokeS0Max)*sq*t.SmokeSize,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                Peak=Range(r,SmokePeakMin,SmokePeakMax),Rot=Range(r,0,MathF.PI*2),Spin=Range(r,-.1f,.1f),
                Lift=Range(r,.5f,1f),Seed=Range(r,0,97)};
        }
    }

    // Ground dust ring around the ground point below the muzzle (forward-biased).
    internal static void SpawnDust(Random r,Vector3 ground,Vector3 forward,float scale,float now,Span<Puff> into,out int count)
        =>SpawnDust(r,ground,forward,scale,now,PuffTuning.Default,into,out count);
    internal static void SpawnDust(Random r,Vector3 ground,Vector3 forward,float scale,float now,in PuffTuning t,Span<Puff> into,out int count)
    {
        count=Math.Min(DustCount(scale,t.DustAmount),into.Length);
        var flat=new Vector3(forward.X,0,forward.Z);flat=flat.LengthSquared()>1e-4f?Vector3.Normalize(flat):Vector3.UnitZ;
        var sq=MathF.Sqrt(Math.Clamp(scale,.35f,2.5f));
        var reach=(1-MathF.Exp(-DustDrag*DustRingSeconds))/DustDrag;   // distance per unit v0 after 0.6 s
        for(var i=0;i<count;i++)
        {
            var a=Range(r,-MathF.PI,MathF.PI)*.7f+(i%6==0?MathF.PI:0);       // most puffs ahead/beside, a few behind
            var dir=Vector3.Normalize(new Vector3(flat.X*MathF.Cos(a)-flat.Z*MathF.Sin(a),0,flat.X*MathF.Sin(a)+flat.Z*MathF.Cos(a)));
            var slice=FlipbookTables.DustFirstSlice+r.Next(FlipbookTables.DustSlices);var s1=Range(r,DustS1Min,DustS1Max)*sq;
            into[i]=new Puff{Dust=true,Slice=slice,Born=now+Range(r,0,.08f),Life=Range(r,DustLifeMin,DustLifeMax),
                Origin=ground,Dir=dir,V0=Range(r,DustRingMin,DustRingMax)*sq/reach,Drag=DustDrag,
                S0=Range(r,DustS0Min,DustS0Max)*sq,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                Peak=DustPeak,Rot=Range(r,-.25f,.25f),Spin=0,Lift=Range(r,.12f,.3f),Seed=Range(r,0,97)};   // low: no tall column
        }
    }

    internal const float DustGrowTau=1.5f;
    internal static float VisibleSize(in Puff p,float age)=>p.S0+(p.S1-p.S0)*(1-MathF.Exp(-Math.Max(age,0)/(p.Dust?DustGrowTau:GrowTau)));
    internal static float Travel(in Puff p,float age)=>p.V0/p.Drag*(1-MathF.Exp(-p.Drag*Math.Max(age,0)));
    internal static float Alpha(in Puff p,float age)
    {
        if(age<=0||age>=p.Life)return 0;
        return p.Dust?p.Peak*Smooth(0,.08f,age)*(1-Smooth(DustFadeStart*p.Life,p.Life,age))
                     :p.Peak*Smooth(0,.06f,age)*(1-Smooth(SmokeFadeStart*p.Life,p.Life,age));
    }
    internal static float Rise(in Puff p,float age)=>p.Dust?p.Lift*Math.Max(0,age-.4f):p.Lift*Math.Min(Math.Max(age,0),LiftSeconds);

    // Closed-form state at absolute time `now`; null when not alive.
    internal static PuffState? State(in Puff p,float now,Vector3 wind,Vector3 up)
    {
        var age=now-p.Born;
        if(!float.IsFinite(age)||age<=0||age>=p.Life)return null;
        var rise=Rise(p,age);
        var shear=1+ShearPerMetre*Math.Min(rise,MaxShearHeight);           // more wind higher up
        var dustWindLag=p.Dust?Smooth(0,1.2f,age):1;                         // dust first follows the blast
        var visibleCentre=p.Origin+p.Dir*Travel(p,age)+up*rise+wind*(age*shear*dustWindLag);
        var size=VisibleSize(p,age);
        var frame=FrameFor(p.Slice,size/p.Quad);
        var cy=FlipbookTables.CenterY[p.Slice][Math.Clamp((int)frame,0,FlipbookTables.Frames-1)];
        var centre=visibleCentre+up*((cy-.5f)*p.Quad);                       // keep the visible cloud on its path
        var heat=p.Dust?0:1-Smooth(0,SmokeHeatSeconds,age);
        var dissolve=.7f*Smooth((p.Dust?.55f:.35f)*p.Life,p.Life,age);
        return new PuffState(centre,p.Quad,p.Rot+p.Spin*age,frame,Alpha(p,age),heat,dissolve);
    }

    internal static Vector3 Wind(float speed,float directionDeg)
    {
        // Compass-style: 0 = towards +Z (world north), 90 = towards +X.
        var s=Math.Clamp(float.IsFinite(speed)?speed:2.5f,0,12);var a=(float.IsFinite(directionDeg)?directionDeg:60)*MathF.PI/180;
        return new Vector3(MathF.Sin(a)*s,0,MathF.Cos(a)*s);
    }
}
