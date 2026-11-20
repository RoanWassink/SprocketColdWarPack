using System.Numerics;
namespace SprocketVisualEffects;

// 0.3.0 lit flipbook puffs (smoke + ground dust). Pure, closed-form and Unity-free:
// the same code drives the runtime (PuffRenderer), the editor proof (VfxProof,
// copied into the Unity project by prepare-project.py) and checks/EffectMathChecks.
// Parameters follow RESEARCH-EPIC-SMOKE.md section 5 (B/C), 120-125 mm = scale ~1.
internal struct Puff
{
    internal bool Dust;
    internal byte Style;           // 0 smoke, 1 dust, 2 explosion soot, 3 dirt plume, 4 armour scorch, 5 cookoff soot
    internal int Slice;            // Texture2DArray slice (0-2 smoke, 3-4 dust)
    internal float Born, Life;     // absolute spawn time and lifetime (s)
    internal Vector3 Origin, Dir;  // spawn point and initial direction (unit)
    internal float V0, Drag;       // initial speed (m/s) and exponential drag (1/s)
    internal float S0, S1;         // visible size start -> end (m)
    internal float Quad;           // quad size (m): S1 / final visible diameter fraction
    internal float Peak, Rot, Spin, Lift, Seed;
    internal float HeatPeak, HeatSeconds;
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
            into[i]=new Puff{Dust=true,Style=1,Slice=slice,Born=now+Range(r,0,.08f),Life=Range(r,DustLifeMin,DustLifeMax),
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
        if(p.Dust)return p.Peak*Smooth(0,.08f,age)*(1-Smooth(DustFadeStart*p.Life,p.Life,age));
        var fadeStart=p.Style switch{2=>.22f,3=>.40f,4=>.25f,5=>.38f,_=>SmokeFadeStart};
        var riseIn=p.Style==0?.06f:.04f;
        return p.Peak*Smooth(0,riseIn,age)*(1-Smooth(fadeStart*p.Life,p.Life,age));
    }
    internal static float Rise(in Puff p,float age)
    {
        if(p.Dust)return p.Lift*Math.Max(0,age-.4f);
        if(p.Style is 2)return p.Lift*Math.Min(Math.Max(age,0),1.6f);
        if(p.Style is 5)return p.Lift*Math.Min(Math.Max(age,0),4.5f);
        if(p.Style is 3)return p.Lift*Math.Min(Math.Max(age,0),1.8f);
        return p.Lift*Math.Min(Math.Max(age,0),LiftSeconds);
    }

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
        var heatSec=p.HeatSeconds>0f?p.HeatSeconds:SmokeHeatSeconds;
        var heatPeak=p.HeatPeak>0f?p.HeatPeak:1f;
        var heat=p.Dust?0f:heatPeak*(1-Smooth(0,heatSec,age));
        var dissStart=p.Style switch{1=>.55f,2=>.35f,3=>.50f,5=>.52f,_=>.35f};
        var dissolve=.7f*Smooth(dissStart*p.Life,p.Life,age);
        return new PuffState(centre,p.Quad,p.Rot+p.Spin*age,frame,Alpha(p,age),heat,dissolve);
    }

    private static Vector3 SafeNorm(Vector3 v,Vector3 fallback)
    {
        var l=v.LengthSquared();
        return l>1e-6f&&float.IsFinite(l)?v/MathF.Sqrt(l):fallback;
    }

    private static void OrthoBasis(Vector3 axis,out Vector3 t1,out Vector3 t2)
    {
        axis=SafeNorm(axis,Vector3.UnitY);
        var r=MathF.Abs(axis.Y)<.9f?Vector3.UnitY:Vector3.UnitX;
        t1=SafeNorm(Vector3.Cross(r,axis),Vector3.UnitX);
        t2=SafeNorm(Vector3.Cross(axis,t1),Vector3.UnitZ);
    }

    private static Vector3 ConeDir(Random r,Vector3 axis,Vector3 t1,Vector3 t2,float minDeg,float maxDeg)
    {
        var ang=Range(r,Math.Clamp(minDeg,.1f,89f),Math.Clamp(maxDeg,.2f,89.5f))*MathF.PI/180f;
        var s=MathF.Sin(ang);var c=MathF.Cos(ang);var phi=Range(r,0f,MathF.PI*2f);
        return SafeNorm(axis*c+(t1*MathF.Cos(phi)+t2*MathF.Sin(phi))*s,axis);
    }

    // 0.5.0: Explosive fireball soot billows (dark charcoal smoke with incandescent early fire core) + radial shock dust.
    internal static void SpawnExplosionPuffs(Random r,Vector3 pos,Vector3 normal,float scale,float now,float fireMul,Span<Puff> into,out int count)
    {
        var s=Math.Clamp(scale,.25f,3.5f);
        var sq=MathF.Sqrt(s);
        var fire=Math.Clamp(float.IsFinite(fireMul)?fireMul:1.3f,0f,3f);
        var target=Math.Clamp((int)MathF.Round(18f*sq),8,28);
        count=Math.Min(target,into.Length);
        var plumeUp=SafeNorm(normal*.35f+Vector3.UnitY*.85f,Vector3.UnitY);
        OrthoBasis(plumeUp,out var t1,out var t2);
        var ringSplit=count*3/5;
        for(var i=0;i<count;i++)
        {
            if(i<ringSplit)
            {
                var slice=r.Next(FlipbookTables.SmokeSlices);
                var a=Range(r,0,MathF.PI*2f);
                var elev=Range(r,.2f,.95f);
                var dir=SafeNorm(plumeUp*elev+(t1*MathF.Cos(a)+t2*MathF.Sin(a))*(1.15f-elev*.4f),plumeUp);
                var s1=Range(r,3.2f,6.0f)*sq;
                into[i]=new Puff{
                    Dust=false,Style=2,Slice=slice,Born=now+Range(r,0f,.035f),Life=Range(r,1.3f,2.3f),
                    Origin=pos+plumeUp*.2f,Dir=dir,V0=Range(r,14f,30f)*sq,Drag=3.2f,
                    S0=Range(r,1.3f,2.4f)*sq,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                    Peak=Range(r,.46f,.68f),Rot=Range(r,0f,MathF.PI*2f),Spin=Range(r,-.25f,.25f),
                    Lift=Range(r,.35f,.78f),Seed=Range(r,0f,97f),
                    HeatPeak=Range(r,.65f,1.05f)*fire,HeatSeconds=Range(r,.12f,.22f)
                };
            }
            else
            {
                var slice=FlipbookTables.DustFirstSlice+r.Next(FlipbookTables.DustSlices);
                var a=Range(r,0,MathF.PI*2f);
                var dir=SafeNorm(t1*MathF.Cos(a)+t2*MathF.Sin(a)+Vector3.UnitY*.08f,Vector3.UnitX);
                var s1=Range(r,4.5f,7.2f)*sq;
                into[i]=new Puff{
                    Dust=true,Style=1,Slice=slice,Born=now+Range(r,0f,.03f),Life=Range(r,1.8f,3.0f),
                    Origin=pos,Dir=dir,V0=Range(r,18f,36f)*sq,Drag=3.8f,
                    S0=Range(r,1.5f,2.6f)*sq,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                    Peak=.48f,Rot=Range(r,-.3f,.3f),Spin=0f,Lift=Range(r,.10f,.25f),Seed=Range(r,0f,97f)
                };
            }
        }
    }

    // 0.5.0: Ground impact V-cone dirt/dust geyser + low shock-dust skirt.
    internal static void SpawnGroundImpactPuffs(Random r,Vector3 pos,Vector3 normal,float scale,float now,Span<Puff> into,out int count)
    {
        var s=Math.Clamp(scale,.25f,3.5f);
        var sq=MathF.Sqrt(s);
        var target=Math.Clamp((int)MathF.Round(18f*sq),6,28);
        count=Math.Min(target,into.Length);
        var upAxis=SafeNorm(normal*.5f+Vector3.UnitY*.8f,Vector3.UnitY);
        OrthoBasis(upAxis,out var t1,out var t2);
        var coneSplit=count*2/3;
        for(var i=0;i<count;i++)
        {
            if(i<coneSplit)
            {
                var slice=FlipbookTables.DustFirstSlice+r.Next(FlipbookTables.DustSlices);
                var a=Range(r,0f,MathF.PI*2f);
                var spread=Range(r,.12f,.48f);
                var dir=SafeNorm(upAxis+(t1*MathF.Cos(a)+t2*MathF.Sin(a))*spread,upAxis);
                var s1=Range(r,3.8f,7.5f)*sq;
                into[i]=new Puff{
                    Dust=false,Style=3,Slice=slice,Born=now+Range(r,0f,.08f),Life=Range(r,3.0f,5.2f),
                    Origin=pos,Dir=dir,V0=Range(r,14f,34f)*sq,Drag=2.9f,
                    S0=Range(r,1.2f,2.4f)*sq,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                    Peak=Range(r,.52f,.78f),Rot=Range(r,-.4f,.4f),Spin=Range(r,-.15f,.15f),
                    Lift=Range(r,-.25f,.35f),Seed=Range(r,0f,97f)
                };
            }
            else
            {
                var slice=FlipbookTables.DustFirstSlice+r.Next(FlipbookTables.DustSlices);
                var a=Range(r,0f,MathF.PI*2f);
                var dir=SafeNorm(new Vector3(MathF.Cos(a),0f,MathF.Sin(a)),Vector3.UnitX);
                var s1=Range(r,4.5f,8.5f)*sq;
                into[i]=new Puff{
                    Dust=true,Style=1,Slice=slice,Born=now+Range(r,0f,.05f),Life=Range(r,2.8f,5.0f),
                    Origin=pos,Dir=dir,V0=Range(r,10f,20f)*sq,Drag=3.6f,
                    S0=Range(r,1.6f,3.0f)*sq,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                    Peak=.48f,Rot=Range(r,-.25f,.25f),Spin=0f,Lift=Range(r,.10f,.28f),Seed=Range(r,0f,97f)
                };
            }
        }
    }

    // 0.5.0: Crisp metallic impact scorch + hot flash wisp on armor hit.
    internal static void SpawnArmourImpactPuffs(Random r,Vector3 pos,Vector3 normal,float scale,float now,bool ricochet,Span<Puff> into,out int count)
    {
        var s=Math.Clamp(scale,.25f,3.5f);
        var sq=MathF.Sqrt(s);
        var target=Math.Clamp((int)MathF.Round((ricochet?5f:7f)*sq),3,10);
        count=Math.Min(target,into.Length);
        var n=SafeNorm(normal,Vector3.UnitY);
        OrthoBasis(n,out var t1,out var t2);
        for(var i=0;i<count;i++)
        {
            var slice=r.Next(FlipbookTables.SmokeSlices);
            var a=Range(r,0f,MathF.PI*2f);
            var spread=Range(r,.2f,.85f);
            var dir=SafeNorm(n+(t1*MathF.Cos(a)+t2*MathF.Sin(a))*spread,n);
            var s1=Range(r,1.8f,3.8f)*sq;
            into[i]=new Puff{
                Dust=false,Style=4,Slice=slice,Born=now+Range(r,0f,.04f),Life=Range(r,1.4f,2.6f),
                Origin=pos+n*.06f,Dir=dir,V0=Range(r,6f,16f)*sq,Drag=3.4f,
                S0=Range(r,.7f,1.4f)*sq,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                Peak=Range(r,.26f,.42f),Rot=Range(r,0f,MathF.PI*2f),Spin=Range(r,-.3f,.3f),
                Lift=Range(r,.35f,.75f),Seed=Range(r,0f,97f),
                HeatPeak=i<2?1.2f:0.4f,HeatSeconds=.14f
            };
        }
    }

    // 0.5.0: Towering pitch-black oily smoke column + blazing fire puffs for ammo-rack cookoffs.
    internal static void SpawnCookoffPuffs(Random r,Vector3 pos,float scale,float now,float fireMul,Span<Puff> into,out int count)
    {
        var s=Math.Clamp(scale,.5f,3.5f);
        var sq=MathF.Sqrt(s);
        var fire=Math.Clamp(float.IsFinite(fireMul)?fireMul:1.3f,0f,3f);
        var target=Math.Clamp((int)MathF.Round(26f*sq),12,38);
        count=Math.Min(target,into.Length);
        OrthoBasis(Vector3.UnitY,out var t1,out var t2);
        for(var i=0;i<count;i++)
        {
            var slice=r.Next(FlipbookTables.SmokeSlices);
            var a=Range(r,0f,MathF.PI*2f);
            var spread=Range(r,.15f,.65f);
            var dir=SafeNorm(Vector3.UnitY+(t1*MathF.Cos(a)+t2*MathF.Sin(a))*spread,Vector3.UnitY);
            var s1=Range(r,6.0f,12.0f)*sq;
            into[i]=new Puff{
                Dust=false,Style=5,Slice=slice,Born=now+Range(r,0f,.38f),Life=Range(r,4.5f,7.5f),
                Origin=pos+Vector3.UnitY*.3f,Dir=dir,V0=Range(r,12f,28f)*sq,Drag=2.1f,
                S0=Range(r,2.2f,4.0f)*sq,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                Peak=Range(r,.68f,.94f),Rot=Range(r,0f,MathF.PI*2f),Spin=Range(r,-.3f,.3f),
                Lift=Range(r,2.0f,3.4f),Seed=Range(r,0f,97f),
                HeatPeak=Range(r,1.5f,2.1f)*fire,HeatSeconds=Range(r,.75f,1.35f)
            };
        }
    }

    // 0.5.3: Compact ERA tile puffs jetting outward along plate normal (dark explosive smoke, low glow).
    internal static void SpawnEraPuffs(Random r,Vector3 pos,Vector3 outward,float scale,float now,float fireMul,Span<Puff> into,out int count)
    {
        var s=Math.Clamp(scale,.25f,3.5f);
        var sq=MathF.Sqrt(s);
        var fire=Math.Clamp(float.IsFinite(fireMul)?fireMul:1.3f,0f,3f);
        var target=Math.Clamp((int)MathF.Round(10f*sq),8,12);
        count=Math.Min(target,into.Length);
        var axis=SafeNorm(outward,Vector3.UnitY);
        OrthoBasis(axis,out var t1,out var t2);
        for(var i=0;i<count;i++)
        {
            var slice=r.Next(FlipbookTables.SmokeSlices);
            var dir=ConeDir(r,axis,t1,t2,22f,45f);
            var s1=Range(r,1.2f,2.4f)*sq;
            into[i]=new Puff{
                Dust=false,Style=2,Slice=slice,Born=now+Range(r,0f,.025f),Life=Range(r,1.0f,1.8f),
                Origin=pos+axis*.12f,Dir=dir,V0=Range(r,14f,28f)*sq,Drag=4.2f,
                S0=Range(r,.6f,1.1f)*sq,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                Peak=Range(r,.42f,.64f),Rot=Range(r,0f,MathF.PI*2f),Spin=Range(r,-.3f,.3f),
                Lift=Range(r,.22f,.50f),Seed=Range(r,0f,97f),
                HeatPeak=Range(r,.25f,.45f)*fire,HeatSeconds=Range(r,.05f,.09f)
            };
        }
    }

    // 0.5.3: HEAT jet backsplash (~70% tight outward stream + ~30% lateral armor splash collar).
    internal static void SpawnHeatPuffs(Random r,Vector3 pos,Vector3 outward,float scale,float now,float fireMul,Span<Puff> into,out int count)
    {
        var s=Math.Clamp(scale,.25f,3.5f);
        var sq=MathF.Sqrt(s);
        var fire=Math.Clamp(float.IsFinite(fireMul)?fireMul:1.3f,0f,3f);
        var target=Math.Clamp((int)MathF.Round(14f*sq),10,16);
        count=Math.Min(target,into.Length);
        var axis=SafeNorm(outward,Vector3.UnitY);
        OrthoBasis(axis,out var t1,out var t2);
        var fwdSplit=count*7/10;
        for(var i=0;i<count;i++)
        {
            var isFwd=i<fwdSplit;
            var slice=r.Next(FlipbookTables.SmokeSlices);
            var dir=isFwd?ConeDir(r,axis,t1,t2,18f,38f):ConeDir(r,axis,t1,t2,70f,85f);
            var s1=Range(r,1.7f,3.2f)*sq;
            into[i]=new Puff{
                Dust=false,Style=2,Slice=slice,Born=now+Range(r,0f,.03f),Life=Range(r,1.2f,2.0f),
                Origin=pos+axis*.12f,Dir=dir,V0=Range(r,isFwd?18f:14f,isFwd?36f:26f)*sq,Drag=3.7f,
                S0=Range(r,.8f,1.4f)*sq,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                Peak=Range(r,.44f,.68f),Rot=Range(r,0f,MathF.PI*2f),Spin=Range(r,-.25f,.25f),
                Lift=Range(r,.25f,.58f),Seed=Range(r,0f,97f),
                HeatPeak=Range(r,.35f,.60f)*fire,HeatSeconds=Range(r,.06f,.11f)
            };
        }
    }

    // 0.5.3: HESH tamped plastic-explosive plate blast (~55% outward plume + ~45% wide radial armor sheet).
    internal static void SpawnHeshPuffs(Random r,Vector3 pos,Vector3 outward,float scale,float now,float fireMul,Span<Puff> into,out int count)
    {
        var s=Math.Clamp(scale,.25f,3.5f);
        var sq=MathF.Sqrt(s);
        var fire=Math.Clamp(float.IsFinite(fireMul)?fireMul:1.3f,0f,3f);
        var target=Math.Clamp((int)MathF.Round(18f*sq),14,22);
        count=Math.Min(target,into.Length);
        var axis=SafeNorm(outward,Vector3.UnitY);
        OrthoBasis(axis,out var t1,out var t2);
        var fwdSplit=count*55/100;
        for(var i=0;i<count;i++)
        {
            var isFwd=i<fwdSplit;
            var slice=r.Next(FlipbookTables.SmokeSlices);
            var dir=isFwd?ConeDir(r,axis,t1,t2,28f,55f):ConeDir(r,axis,t1,t2,72f,88f);
            var s1=Range(r,2.2f,4.2f)*sq;
            into[i]=new Puff{
                Dust=false,Style=2,Slice=slice,Born=now+Range(r,0f,.035f),Life=Range(r,1.4f,2.3f),
                Origin=pos+axis*.12f,Dir=dir,V0=Range(r,isFwd?16f:18f,34f)*sq,Drag=isFwd?3.3f:3.5f,
                S0=Range(r,1.0f,1.8f)*sq,S1=s1,Quad=s1/FlipbookTables.Diameter[slice][^1],
                Peak=Range(r,.48f,.72f),Rot=Range(r,0f,MathF.PI*2f),Spin=Range(r,-.25f,.25f),
                Lift=Range(r,.30f,.65f),Seed=Range(r,0f,97f),
                HeatPeak=Range(r,.40f,.70f)*fire,HeatSeconds=Range(r,.08f,.14f)
            };
        }
    }

    internal static Vector3 Wind(float speed,float directionDeg)
    {
        // Compass-style: 0 = towards +Z (world north), 90 = towards +X.
        var s=Math.Clamp(float.IsFinite(speed)?speed:2.5f,0,12);var a=(float.IsFinite(directionDeg)?directionDeg:60)*MathF.PI/180;
        return new Vector3(MathF.Sin(a)*s,0,MathF.Cos(a)*s);
    }
}
