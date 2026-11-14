using SprocketVisualEffects;

static void Check(bool condition,string description){if(!condition)throw new Exception(description);}
static void InRange(float value,float min,float max,string description)
{
    if(!float.IsFinite(value)||value<min||value>max)throw new Exception($"{description}: {value} outside {min}..{max}");
}
static bool Near(float a,float b)=>Math.Abs(a-b)<1e-4f;

InRange(EffectMath.HeavyLifetime,3.5f,5f,"Smoke lifetime");
InRange(EffectMath.HeavyFireEnd,.12f,.20f,"Fire end");
InRange(EffectMath.HeavyFireFull,.12f,.20f,"Fire full");
Check(EffectMath.HeavyFireEnd>=EffectMath.HeavyFireFull,"Fire window order");
Check(EffectMath.HeavyFire(0)==0,"No fire before launch");
InRange(EffectMath.HeavyFire(.12f),.1f,1f,"Fire at 0.12 s");
Check(EffectMath.HeavyFire(EffectMath.HeavyFireEnd)==0,"No fire after 0.19 s");
Check(EffectMath.HeavyFire(.21f)==0,"No fire at 0.21 s");
InRange(EffectMath.HeavyHead(.3f),4f,5f,"Head at 0.3 s");
InRange(EffectMath.HeavyPlumeLength(.35f),6f,9f,"Plume length at 0.35 s");
InRange(EffectMath.HeavyPlumeLength(.4f),6f,9f,"Plume length at 0.4 s");
InRange(EffectMath.HeavyPlumeLength(.5f),6f,9f,"Plume length at 0.5 s");
InRange(EffectMath.HeavyRadius(.35f),1.5f,1.75f,"Radius at 0.35 s");
InRange(EffectMath.HeavyRadius(.5f),1.5f,1.75f,"Radius at 0.5 s");
Check(EffectMath.HeavyRise(.3f)==0&&EffectMath.HeavyRise(1f)>0,"Rise starts after 0.3 s");
Check(EffectMath.HeavyRise(4f)<2f,"Rise stays gentle");
Console.WriteLine("PASS HEAVY125 fire window, 4-5 m head, 6-9 m plume, 1.6 m radius and gentle late rise");

var previousSpeed=float.PositiveInfinity;
foreach(var age in new[]{.05f,.1f,.2f,.25f,.3f,.35f,.5f})
{
    var speed=EffectMath.HeavyHeadSpeed(age);
    Check(speed>0&&speed<previousSpeed,"Head speed must decay at "+age);
    previousSpeed=speed;
}
var previousFade=2f;
for(var i=0;i<=450;i++)
{
    var age=i/100f;var fade=EffectMath.HeavySmokeFade(age);
    Check(fade<=previousFade+1e-6f,"Smoke fade must not increase at "+age);
    previousFade=fade;
}
Check(EffectMath.HeavySmokeFade(3.5f)>.3f,"Smoke still visible at 3.5 s");
Check(EffectMath.HeavySmokeFade(4.4f)>0,"Smoke still fading near lifetime");
Check(EffectMath.HeavySmokeFade(EffectMath.HeavyLifetime)==0,"Smoke gone at lifetime");
Check(EffectMath.HeavySmokeFade(5f)==0,"Smoke gone after lifetime");
Console.WriteLine("PASS decaying launch speed and gradual 3.5-4.5 s smoke fade");

for(var i=0;i<=500;i++)
{
    var age=i/100f;
    Check(float.IsFinite(EffectMath.HeavyHead(age))&&float.IsFinite(EffectMath.HeavyHeadSpeed(age)),"Finite head/"+age);
    Check(float.IsFinite(EffectMath.HeavyRadius(age))&&float.IsFinite(EffectMath.HeavyPlumeLength(age)),"Finite plume/"+age);
    Check(float.IsFinite(EffectMath.HeavyFire(age))&&float.IsFinite(EffectMath.HeavySmokeFade(age)),"Finite fade/"+age);
}
Check(EffectMath.HeavyHead(-1)==0&&EffectMath.HeavyRadius(float.NaN)==0,"Negative/NaN gates");
Console.WriteLine("PASS finite curves over the whole lifetime and invalid-input gates");

Check(Near(EffectMath.Scale(120,1),1)&&Near(EffectMath.Scale(50,1),50f/120)&&Near(EffectMath.Scale(1,1),.35f)&&Near(EffectMath.Scale(1000,1),2.5f),"Calibre scale bounds 0.35-2.5");
Check(Near(EffectMath.Scale(120,float.NaN),1)&&Near(EffectMath.Scale(120,99),3)&&Near(EffectMath.Scale(120,.1f),.25f),"Scale multiplier sanitization");
var heavyScale=EffectMath.Scale(125,1);
InRange(EffectMath.HeavyHead(.3f)*heavyScale,4f,5f,"125 mm scaled head");
InRange(EffectMath.HeavyPlumeLength(.35f)*heavyScale,6f,9f,"125 mm scaled plume");
InRange(EffectMath.HeavyRadius(.35f)*heavyScale,1.5f,1.8f,"125 mm scaled radius");
Console.WriteLine("PASS calibre clamp and 125 mm world-scale preset");

Check(EffectMath.VolumeCapacity(-5)==1&&EffectMath.VolumeCapacity(0)==1&&EffectMath.VolumeCapacity(1)==1,"MaxVolumes lower clamp");
Check(EffectMath.VolumeCapacity(2)==2&&EffectMath.VolumeCapacity(4)==4&&EffectMath.VolumeCapacity(5)==4&&EffectMath.VolumeCapacity(100)==4,"MaxVolumes upper clamp 4");
Check(EffectMath.VolumeSteps(0)==16&&EffectMath.VolumeSteps(15)==16&&EffectMath.VolumeSteps(16)==16,"Raymarch lower clamp");
Check(EffectMath.VolumeSteps(24)==24&&EffectMath.VolumeSteps(36)==36&&EffectMath.VolumeSteps(99)==36,"Raymarch upper clamp 36");
var births=new float[2];var active=new bool[2];
Check(EffectMath.SelectSlot(births,active)==0,"Empty pool first slot");
births[0]=1;active[0]=true;
Check(EffectMath.SelectSlot(births,active)==1,"Free second slot");
births[1]=2;active[1]=true;
Check(EffectMath.SelectSlot(births,active)==0,"Oldest recycled at capacity 2");
births[0]=3;
Check(EffectMath.SelectSlot(births,active)==1,"Second-oldest recycled");
var cap=EffectMath.VolumeCapacity(99);var b4=new float[cap];var a4=new bool[cap];
for(var shot=0;shot<37;shot++)
{
    var slot=EffectMath.SelectSlot(b4,a4);
    Check(slot>=0&&slot<cap&&slot<4,"Pool slot outside 1-4 bound");
    b4[slot]=shot;a4[slot]=true;
}
Console.WriteLine("PASS bounded 1-4 volume pool and deterministic oldest-slot recycling");
var previousRoll=-1f;var previousStep=float.PositiveInfinity;
for(var i=1;i<=450;i++)
{
    var age=i/100f;var roll=EffectMath.HeavyRollAngle(age);var step=roll-previousRoll;
    Check(roll>=previousRoll&&roll<=EffectMath.HeavyRollLimit&&(age>1||roll>previousRoll),"Roll must grow but never exceed its limit at "+age);
    if(i>1)Check(step<=previousStep+1e-6f,"Roll rate must decay (no linear wind-up) at "+age);
    previousRoll=roll;previousStep=step;
}
InRange(EffectMath.HeavyRollLimit,1f,2.5f,"Roll limit (rad) well below a full winding");
Check(EffectMath.HeavyHeadFront>EffectMath.HeavyHeadBack&&EffectMath.HeavyHeadBack>EffectMath.HeavyHeadWide,"Head stretched forward, longer than wide");
for(var i=0;i<=500;i++)
{
    var age=i/100f;var r=EffectMath.HeavyRadius(age);var g=EffectMath.HeavyGrowth(age);
    Check(EffectMath.HeavyPlumeLength(age)<EffectMath.BoxFront,"Plume inside proxy front at "+age);
    Check(MathF.Max(EffectMath.HeavyHeadWide*r,1f*r)+EffectMath.HeavyCurlReach*g<EffectMath.BoxHalfWidth,"Plume inside proxy width at "+age);
    Check(1f*r+EffectMath.HeavyRise(age)+EffectMath.HeavyCurlReach*g<EffectMath.BoxTop,"Plume inside proxy top at "+age);
}
Check(EffectMath.HeavyGrowth(.5f)==1&&EffectMath.HeavyGrowth(EffectMath.HeavyLifetime)<2.5f,"Late spreading starts after 0.5 s and stays bounded");
Check(MathF.Max(EffectMath.HeavySmokeR,MathF.Max(EffectMath.HeavySmokeG,EffectMath.HeavySmokeB))-MathF.Min(EffectMath.HeavySmokeR,MathF.Min(EffectMath.HeavySmokeG,EffectMath.HeavySmokeB))<.08f&&EffectMath.HeavySmokeB>=.5f,"Grey-white smoke colour");
Console.WriteLine("PASS bounded decaying roll, forward-stretched head, proxy-box containment and grey-white colour");
InRange(EffectMath.HeavyDensity,5f,8f,"Daylight-calibrated base density");
Check(Near(EffectMath.VolumetricDensityFactor(1),1)&&Near(EffectMath.VolumetricDensityFactor(.1f),.25f)&&Near(EffectMath.VolumetricDensityFactor(9),3)&&Near(EffectMath.VolumetricDensityFactor(float.NaN),1),"VolumetricDensity clamp 0.25-3, default 1");
Check(Near(EffectMath.VolumetricScaleFactor(1),1)&&Near(EffectMath.VolumetricScaleFactor(.1f),.5f)&&Near(EffectMath.VolumetricScaleFactor(9),2.5f)&&Near(EffectMath.VolumetricScaleFactor(float.PositiveInfinity),1),"VolumetricScale clamp 0.5-2.5, default 1");
Check(EffectMath.Scale(125,EffectMath.VolumetricScaleFactor(2.5f))<=2.5f*3f&&EffectMath.Scale(125,EffectMath.VolumetricScaleFactor(2.5f))>EffectMath.Scale(125,1),"VolumetricScale enlarges the 125 mm plume within bounds");
Console.WriteLine("PASS daylight density and VolumetricDensity/VolumetricScale sanitising");
Check(EffectMath.BoxBack<=-3.6f&&EffectMath.MuzzleBoxMin.Z==EffectMath.BoxBack&&EffectMath.MuzzleBoxMax.Y==EffectMath.BoxTop,"Box reaches 3.6 m behind the muzzle (blast bubble <= 3.5 m; 0.2.2 cut at -1 m)");
Check(EffectMath.FlashCurve(0)==0&&EffectMath.FlashCurve(EffectMath.FlashDuration)==0&&EffectMath.FlashCurve(.2f)==0,"Flash light starts and ends at zero");
InRange(EffectMath.FlashCurve(.015f),.9f,1f,"Flash light near peak at 15 ms");
InRange(EffectMath.FlashDuration,.05f,.15f,"Flash light 0.05-0.15 s");
Check(EffectMath.FlashLightPool>=1&&EffectMath.FlashLightPool<=2,"Flash light pool 1-2");
InRange(EffectMath.HeavyFire(.05f),.5f,1f,"Fire bright at 0.05 s");
InRange(EffectMath.DustLifetime,1.5f,4f,"Dust lifetime");
Check(EffectMath.DustBoxMin.Y<0&&EffectMath.DustBoxMax.Y>=3f&&EffectMath.DustMaxDrop<=10f,"Dust box spans the ground plane; raycast reach bounded");
Console.WriteLine("PASS 0.2.3 box, 5 s fade, flash light curve/pool and dust ring bounds");
// B3: blown out, not spawned.
InRange(EffectMath.HeavyHead(.02f),.5f,1.5f,"Front only 0.5-1.5 m out of the barrel at 0.02 s");
InRange(EffectMath.HeavyHead(.1f),3f,4.2f,"Front 3-4.2 m at 0.1 s");
Check(EffectMath.HeavyRadius(.02f)<.5f&&EffectMath.HeavyRadius(.05f)<.8f,"Bulb still small while the front travels (not full-size at 0.05 s)");
Check(EffectMath.HeavyBlastRadius(0)<=.2f&&EffectMath.HeavyBlastRadius(.05f)<1f&&EffectMath.HeavyBlastRadius(.3f)>1.4f&&EffectMath.HeavyBlastRadius(5f)<=1.75f,"Blast bubble grows at the muzzle, not full-size at 0.05 s");
for(var i=1;i<=30;i++){var a=i/100f;Check(EffectMath.HeavyHead(a)>EffectMath.HeavyHead(a-.01f),"Front advances monotonically at "+a);}
Console.WriteLine("PASS B3 front advances from the muzzle, bulb and blast grow instead of spawning");
// 0.3.1 puff layer (S3: modern smokeless gun, not black powder)
Check(PuffMath.SmokeCount(1f)==16&&PuffMath.SmokeCount(.1f)==5&&PuffMath.SmokeCount(9f)==40,"Smoke puffs ~16 per 120 mm shot (0.3.0: 24-40)");
Check(PuffMath.DustCount(1f)==18&&PuffMath.DustCount(.1f)==6&&PuffMath.DustCount(9f)==44,"Dust puffs ~18 per shot (0.3.0: 12-20)");
Check(PuffMath.SmokeCount(1f,0)==0&&PuffMath.DustCount(1f,0)==0&&PuffMath.SmokeCount(1f,2)==32&&PuffMath.DustCount(1f,.5f)==9,"SmokeAmount/DustAmount scale the counts, 0 = none");
var tc=new PuffTuning(float.NaN,9,-1,0,5);
Check(tc.SmokeAmount==1&&tc.SmokeSize==2&&tc.SmokeLifetime==.5f&&tc.SmokeBrightness==.3f&&tc.DustAmount==2,"Tuning clamps 0-2/.5-2/.5-2/.3-1.5/0-2, NaN -> 1");
var td=PuffTuning.Default;Check(td.SmokeAmount==1&&td.SmokeSize==1&&td.SmokeLifetime==1&&td.SmokeBrightness==1&&td.DustAmount==1,"Tuning default 1");
var prng=new Random(5);var buf=new Puff[64];
var fwd=System.Numerics.Vector3.UnitZ;var upv=System.Numerics.Vector3.UnitY;
for(var trial=0;trial<200;trial++)
{
    PuffMath.SpawnSmoke(prng,System.Numerics.Vector3.Zero,fwd,upv,1f,0f,buf,out var n);
    Check(n==16,"Default smoke count");
    for(var i=0;i<n;i++)
    {
        var p=buf[i];
        InRange(p.Born,0,PuffMath.SpawnWindow,"Smoke spawn window");InRange(p.V0,22,48,"Smoke v0");InRange(p.Life,2,4,"Smoke life 2-4 s");
        InRange(p.S0,1,2,"Smoke start size");InRange(p.S1,4,7,"Smoke end size 4-7 m");InRange(p.Peak,.12f,.2f,"Smoke peak alpha .12-.2");
        Check(System.Numerics.Vector3.Dot(p.Dir,fwd)>=MathF.Cos(PuffMath.ConeDegrees*MathF.PI/180)-1e-4f,"Smoke cone <= 8 deg");
        Check(PuffMath.Travel(p,3f)<=p.V0/p.Drag+1e-3f&&PuffMath.Travel(p,3f)<=14f,"Smoke stalls within 14 m (drag 3.5/s)");
        Check(Near(PuffMath.VisibleSize(p,0),p.S0)&&PuffMath.VisibleSize(p,4f)>=p.S0+(p.S1-p.S0)*.99f-1e-3f,"Smoke grows to its end size");
        Check(PuffMath.Alpha(p,0)==0&&PuffMath.Alpha(p,p.Life)==0&&PuffMath.Alpha(p,.5f)>0&&PuffMath.Alpha(p,.5f)<=p.Peak,"Smoke alpha curve");
        Check(PuffMath.Alpha(p,MathF.Min(3f,p.Life))<=.3f*p.Peak,"Smoke mostly transparent by 1.5-3 s");
        Check(PuffMath.Rise(p,1f)<=1.001f&&PuffMath.Rise(p,3f)==PuffMath.Rise(p,1f),"Upward drift only in the first 1 s");
        var prev=float.PositiveInfinity;for(var t=.1f;t<p.Life;t+=.05f){var a=PuffMath.Alpha(p,t);Check(a<=prev+1e-5f,"Smoke alpha never rises after 0.1 s");prev=a;}
        var st=PuffMath.State(p,p.Born+.3f,PuffMath.Wind(2.5f,200),upv);Check(st.HasValue&&st.Value.Frame>=0&&st.Value.Frame<=63&&st.Value.Dissolve==0,"Smoke state early");
        var hot=PuffMath.State(p,p.Born+.01f,System.Numerics.Vector3.Zero,upv);var cool=PuffMath.State(p,p.Born+PuffMath.SmokeHeatSeconds,System.Numerics.Vector3.Zero,upv);
        Check(hot.HasValue&&hot.Value.Heat>0&&cool.HasValue&&cool.Value.Heat==0,"Warm tint only in the first 0.1 s");
        Check(PuffMath.State(p,p.Born+p.Life+.01f,System.Numerics.Vector3.Zero,upv)==null&&PuffMath.State(p,p.Born-.01f,System.Numerics.Vector3.Zero,upv)==null,"Smoke dead outside its life");
    }
    PuffMath.SpawnSmoke(prng,System.Numerics.Vector3.Zero,fwd,upv,1f,0f,new PuffTuning(1,2,.5f,1,1),buf,out n);
    for(var i=0;i<n;i++){InRange(buf[i].S1,8,14,"SmokeSize 2 doubles");InRange(buf[i].Life,1,2,"SmokeLifetime .5 halves");}
    PuffMath.SpawnDust(prng,System.Numerics.Vector3.Zero,fwd,1f,0f,buf,out n);
    Check(n==18,"Default dust count");
    for(var i=0;i<n;i++)
    {
        var p=buf[i];
        InRange(PuffMath.Travel(p,.6f),6.99f,11.01f,"Dust ring 7-11 m after 0.6 s");InRange(p.Life,3,6,"Dust life 3-6 s");InRange(p.S1,6,10,"Dust end size");
        Check(p.Dir.Y==0&&p.Slice>=FlipbookTables.DustFirstSlice,"Dust flat ring, dust flipbooks");
        Check(PuffMath.Rise(p,.4f)==0&&PuffMath.Rise(p,2f)>0&&PuffMath.Rise(p,6f)<=1.71f,"Dust stays low (< 1.7 m rise), no column");
        Check(PuffMath.Alpha(p,1.5f)>.5f*p.Peak&&PuffMath.Alpha(p,1.5f)>PuffMath.SmokePeakMax,"Dust dominates the smoke after 1 s");
    }
}
for(var s_=0;s_<5;s_++){var prevF=-1f;for(var v=0f;v<=1f;v+=.01f){var fr=PuffMath.FrameFor(s_,v);Check(fr>=prevF-1e-5f&&fr>=0&&fr<=63,"Frame lookup monotone 0-63");prevF=fr;}Check(PuffMath.FrameFor(s_,FlipbookTables.Diameter[s_][^1])>=62.9f,"Final size reaches the last frame");}
var wv=PuffMath.Wind(3,90);Check(Near(wv.Length(),3)&&Near(wv.X,3),"Wind speed/direction");Check(Near(PuffMath.Wind(99,0).Length(),12)&&Near(PuffMath.Wind(float.NaN,0).Length(),2.5f),"Wind clamp and NaN fallback");
Check(EffectMath.PuffCapacity(5)==32&&EffectMath.PuffCapacity(160)==160&&EffectMath.PuffCapacity(9999)==400,"MaxPuffs clamp 32-400");
InRange(EffectMath.PuffSunStrength(100000,.7f),.99f,1f,"Daylight sun strength");Check(EffectMath.PuffSunStrength(.3f,-.2f)<.05f&&EffectMath.PuffSunStrength(float.NaN,float.NaN)>0,"Night/below horizon dim, NaN safe");
var smax=MathF.Max(EffectMath.PuffSmokeR,MathF.Max(EffectMath.PuffSmokeG,EffectMath.PuffSmokeB));
Check(smax<=.5f&&EffectMath.PuffSmokeB>=EffectMath.PuffSmokeR&&smax-EffectMath.PuffSmokeR<=.06f,"Smoke albedo mid grey (<= .5), neutral-cool");
Check(smax*(1f+EffectMath.PuffAmbient(1f))<=.7f,"Smoke in full sun stays below .7 display (0.3.0: .87)");
Check(EffectMath.PuffDustR>EffectMath.PuffDustG&&EffectMath.PuffDustG>EffectMath.PuffDustB,"Dust sand coloured");
Check(EffectMath.VolumeFireLifetime<=1f&&EffectMath.VolumeSmokeFadeEnd<EffectMath.VolumeFireLifetime,"Volume keeps only fire/jet since 0.3.0");
Console.WriteLine("PASS 0.3.1 puffs: counts, tuning clamps, spawn ranges, 2-4 s smoke, grey albedo, warm only 0.1 s, low 3-6 s dust, frame lookup, wind, budget, sun");
Console.WriteLine("ALL EFFECTMATH CHECKS PASSED");
