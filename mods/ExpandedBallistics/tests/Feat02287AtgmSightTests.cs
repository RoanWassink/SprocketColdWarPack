using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NV = System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.87 (user 15-11, "Ja, na HE" and "Toren volgt de sight"): one ATGM guidance sight drives a group of dedicated launchers (the Bradley TOW pair), and on a
/// shared native turret the turret does the yaw while the head only elevates. The group rules, the axis ownership, the log texts, the launch rule for two missiles
/// in the air and the turret-follows decision are the production code (AtgmIndependentSightPolicy.cs); 0.22.86 is loaded from its own test build
/// (src-0.22.86/tests/bin, read only) for the bit-for-bit comparisons. The native turret in test 6 is a model (own rate, lag, delay), not the game.
/// Herpak: the comparisons with the 0.22.86 build and source are removed (process checks against an earlier version); the checks of the current code stay.
/// </summary>
internal static class Feat02287AtgmSightTests
{
    /// <summary>The production files 0.22.87 may change on top of 0.22.86.</summary>
    internal static bool IsPlanned(string rel)=>rel is "RuntimeAtgmGuidanceSight.cs" or "AtgmIndependentSightPolicy.cs" or "RuntimeAtgm.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    internal const string ReplacedMark=" // 0.22.87 ATGM group replaced";
    /// <summary>The one line of RuntimeAtgm.cs that 0.22.87 replaces (the launch supersede rule), and its 0.22.86 text.</summary>
    internal static readonly (string File,string New,string Old)[] Replaced=
    {
        ("RuntimeAtgm.cs","            foreach(var old in Flights.Values.Where(f=>TopAttackLockPolicy.Supersede(f.Profile.Atgm!.GuidanceMode)&&(f.Weapon.Pointer==weapon.Pointer || flight.Vehicle!=IntPtr.Zero && f.Vehicle==flight.Vehicle)&&!RuntimeAtgmGuidanceSight.GuidesTogether(f.Weapon,f.Profile.Atgm!.GuidanceMode,weapon,profile.Atgm.GuidanceMode)))old.Guide=false;",
                          "            foreach(var old in Flights.Values.Where(f=>TopAttackLockPolicy.Supersede(f.Profile.Atgm!.GuidanceMode)&&(f.Weapon.Pointer==weapon.Pointer || flight.Vehicle!=IntPtr.Zero && f.Vehicle==flight.Vehicle)))old.Guide=false;"),
    };
    /// <summary>RuntimeAtgm.cs without its declared 0.22.87 line (line endings kept).</summary>
    internal static string Un87(string text)
    {
        var lines=text.Split('\n');
        for(int i=0;i<lines.Length;i++)
        {
            var core=lines[i].TrimEnd('\r');var cr=lines[i].Length>core.Length?lines[i][core.Length..]:"";
            if(!core.EndsWith(ReplacedMark,StringComparison.Ordinal))continue;
            var neu=core[..^ReplacedMark.Length];var hit=Replaced.FirstOrDefault(r=>r.New==neu);
            lines[i]=(hit.Old??"<<unknown replaced line>>")+cr;
        }
        return string.Join("\n",lines);
    }
    internal const string SightGuid="cd1dfaf1-4ce6-4d8d-a1b5-fc0d09f35001",TurretRingGuid="99281776-6b29-4ffb-9d8b-04139ca7b6a2",ElevationRingGuid="31fa3dd0-0738-4d29-b6a5-c3d77c291ea2";
    // herpak: the fixture after the rename script (its three old blueprint keys renamed to sprocketShellProfile, sprocketApfsdsBeta, sprocketHydropneumaticV1); the user's file had D5CBC964...EC84
    internal const string BradleySha="9ED2520AD16B7AC0EF863DD3B9FFFF354721046059F715551BDB53BB439DAB61";

    // ---------------- the 0.22.86 build, read only, for bit-for-bit comparisons ----------------
    private sealed class Old86
    {
        private const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        private readonly Assembly a;
        internal Old86(string dll)
        {
            var dir=Path.GetDirectoryName(dll)!;var alc=new AssemblyLoadContext("eb-0.22.86-atgm",false);
            alc.Resolving+=(c,n)=>{var p=Path.Combine(dir,n.Name+".dll");return File.Exists(p)?c.LoadFromAssemblyPath(p):null;};
            a=alc.LoadFromAssemblyPath(dll);
        }
        private Type T(string n)=>a.GetType("SprocketExpandedBallistics."+n,true)!;
        private object Call(string type,string method,params object[] args)=>T(type).GetMethod(method,F)!.Invoke(null,args)!;
        internal bool Allows(bool player,int matches,bool ordinary,bool valid)=>(bool)Call("AtgmIndependentSightPolicy","Allows",player,matches,ordinary,valid);
        private object Axes(AtgmAimingAxes x)=>Activator.CreateInstance(T("AtgmAimingAxes"),x.Horizontal,x.Vertical)!;
        private Array List(IReadOnlyList<AtgmAimingAxes> xs){var r=Array.CreateInstance(T("AtgmAimingAxes"),xs.Count);for(int i=0;i<xs.Count;i++)r.SetValue(Axes(xs[i]),i);return r;}
        internal bool SharedElevation(AtgmAimingAxes own,IReadOnlyList<AtgmAimingAxes> others)=>(bool)Call("AtgmGuidanceAxisOwnership","SharedElevation",Axes(own),List(others));
        internal HashSet<IntPtr> ExclusiveAxes(AtgmAimingAxes own,IReadOnlyList<AtgmAimingAxes> others)=>(HashSet<IntPtr>)Call("AtgmGuidanceAxisOwnership","ExclusiveAxes",Axes(own),List(others));
        internal AtgmHeadAngles Slew(AtgmHeadAngles head,AtgmHeadAngles command,float yawRate,float pitchRate,float dt,bool gunner,AtgmHeadLimits l)
        {
            var angles=T("AtgmHeadAngles");var limits=T("AtgmHeadLimits");
            var r=Call("AtgmSightHead","Slew",Activator.CreateInstance(angles,head.Yaw,head.Pitch)!,Activator.CreateInstance(angles,command.Yaw,command.Pitch)!,yawRate,pitchRate,dt,gunner,
                Activator.CreateInstance(limits,l.MinYaw,l.MaxYaw,l.MinPitch,l.MaxPitch)!);
            return new((float)angles.GetProperty("Yaw")!.GetValue(r)!,(float)angles.GetProperty("Pitch")!.GetValue(r)!);
        }
    }
    /// <summary>Renders a 0.22.86 interpolated log template (taken from its source) with the given values in order.</summary>
    private static string Render(string template,params object[] values)
    {
        int i=0;return Regex.Replace(template,@"\{[^{}]+\}",_=>Convert.ToString(values[i++],CultureInfo.InvariantCulture)!);
    }
    private static string Template(string source,string start)
    {
        var at=source.IndexOf("$\""+start,StringComparison.Ordinal);if(at<0)return "<<template not found: "+start+">>";
        var end=source.IndexOf('"',at+2);return source[(at+2)..end];
    }
    private static string F(double v,string format="0.###")=>v.ToString(format,CultureInfo.InvariantCulture);
    private static AtgmLinkedLauncher L(int vuid,bool healthy=true,bool compatible=true,bool same=true)=>new(vuid,same,healthy,compatible);
    private static IntPtr P(int v)=>(IntPtr)v;
    private static AtgmAimingAxes A(int h,int v)=>new(P(h),P(v));

    // ---------------- test 6: the turret model ----------------
    /// <summary>A native turret: it turns toward the camera's world yaw it saw <c>Delay</c> seconds ago, with a rate command K x error limited to R, through a first-order lag.</summary>
    internal sealed record TurretModel(string Name,double RateDeg,double Gain,double Lag,double Delay);
    internal sealed record TurretRun(string Model,string Mode,string User,double FinalError,double PeakToPeakLast2s,double MaxErrorLast2s,double MaxHeadYaw,double SettleSeconds,double FinalPitchError,int SignChangesLast3s);
    /// <summary>Two missiles from the two tubes of one sight, 1 s apart, steered by the production law on the sight ray (optionally turning at <paramref name="sightYawRate"/> deg/s from 1.5 s).
    /// <paramref name="supersede"/> = the 0.22.86 launch rule: the first missile stops being guided when the second fires.</summary>
    internal static (double[] Initial,double[] At700,double[] AtEnd,double Together,bool Done) TwoMissiles(AtgmSettings tow,double sightYawRate,bool supersede)
    {
        var sight=new NV(-.68f,2.6f,.4f);var target=new NV(-.68f,1.6f,1500);var baseRay=NV.Normalize(target-sight);
        NV Ray(double t){var a=(float)(sightYawRate*Math.Max(0,t-1.5)*Math.PI/180);return NV.Normalize(new NV(baseRay.X*MathF.Cos(a)+baseRay.Z*MathF.Sin(a),baseRay.Y,-baseRay.X*MathF.Sin(a)+baseRay.Z*MathF.Cos(a)));}
        var launch=new[]{0.0,1.0};var pos=new[]{new NV(-1.19f,2.05f,.6f),new NV(-1.21f,1.78f,.6f)};var heading=new[]{new NV(0,0,1),new NV(0,0,1)};
        var done=new bool[2];var air=new bool[2];var initial=new double[2];var at700=new double[2];var atEnd=new double[2];double together=0;const double dt=1/60.0;
        double Off(NV p,double t){var r=Ray(t);var v=p-sight;return (v-r*NV.Dot(v,r)).Length();}
        for(int i=0;i<2;i++)initial[i]=Off(pos[i],launch[i]);
        for(double t=0;t<12&&!(done[0]&&done[1]);t+=dt)
        {
            for(int i=0;i<2;i++)
            {
                if(done[i]||t<launch[i])continue;air[i]=true;var age=t-launch[i];
                var speed=Math.Min(tow.FlightSpeed,(tow.LaunchSpeed??50)+tow.Acceleration*Math.Max(0,age-tow.MotorDelay));
                var guided=!(supersede&&i==0&&t>=launch[1]);
                if(guided&&age>=tow.MotorDelay&&AtgmFlightDynamics.TryAimAlongSightRay(sight,Ray(t),pos[i],30,out var aim))
                    heading[i]=NV.Normalize(AtgmGuidance.Step(heading[i],aim-pos[i],tow with{FlightSpeed=speed},dt));
                var along=NV.Dot(pos[i]-sight,Ray(t));pos[i]+=heading[i]*(float)(speed*dt);var after=NV.Dot(pos[i]-sight,Ray(t));
                if(along<700&&after>=700)at700[i]=Off(pos[i],t);
                if(after>=1500){atEnd[i]=Off(pos[i],t);done[i]=true;air[i]=false;}
            }
            if(air[0]&&air[1])together+=dt;
        }
        return (initial,at700,atEnd,together,done.All(d=>d));
    }
    /// <summary>
    /// One scoped run. Mode "0.22.86": the head yaws on the turret and the camera is written from the head every frame (the 0.22.86 head path on a shared
    /// turret). Mode "0.22.87": the production decision (AtgmSharedTurretHead) for a shared turret: head yaw 0, camera left to the native world aim.
    /// User "tracks": the gunner keeps moving the mouse toward the target (gain 6/s, at most 180 deg/s); "still": the mouse rests after 1 s.
    /// </summary>
    internal static TurretRun Simulate(TurretModel m,bool turretMode,bool userTracks,double targetYaw=35,double targetPitch=8,double seconds=10,double dt=1/60.0)
    {
        var limits=new AtgmHeadLimits();var head=new AtgmHeadAngles(0,0);
        double turret=0,rate=0,cameraYaw=0,cameraPitch=0;var delayed=new Queue<double>();int lag=Math.Max(0,(int)Math.Round(m.Delay/dt));
        var samples=new List<(double T,double Dir,double HeadYaw,double Pitch)>();
        for(double t=0;t<seconds-1e-9;t+=dt)
        {
            // native camera input (prefix: 0.22.86 writes the camera from the head first)
            if(AtgmSharedTurretHead.WritesCamera(turretMode)){cameraYaw=turret+head.Yaw;cameraPitch=head.Pitch;}
            double dYaw=0,dPitch=0;
            if(userTracks||t<1){var max=180*dt;dYaw=Math.Clamp(6*(targetYaw-cameraYaw)*dt,-max,max);dPitch=Math.Clamp(6*(targetPitch-cameraPitch)*dt,-max,max);}
            var aimYaw=cameraYaw+dYaw;var aimPitch=cameraPitch+dPitch;
            // the head step (postfix): command in the part frame (the turret yaws the part), then the production decision and slew
            var command=AtgmSharedTurretHead.Command(new AtgmHeadAngles(AtgmSightHead.Wrap((float)(aimYaw-turret)),(float)aimPitch),turretMode);
            head=AtgmSightHead.Slew(head,command,60,40,(float)dt,true,limits);
            cameraYaw=AtgmSharedTurretHead.WritesCamera(turretMode)?turret+head.Yaw:aimYaw;
            cameraPitch=AtgmSharedTurretHead.WritesCamera(turretMode)?head.Pitch:aimPitch;
            // the native turret: toward the camera's world yaw, delayed, rate limited, lagged
            delayed.Enqueue(cameraYaw-turret);var seen=delayed.Count>lag?delayed.Dequeue():0;
            var wanted=Math.Clamp(m.Gain*seen,-m.RateDeg,m.RateDeg);rate+=(wanted-rate)*Math.Min(1,dt/m.Lag);turret+=rate*dt;
            samples.Add((t+dt,turret+head.Yaw,head.Yaw,head.Pitch));
        }
        var last2=samples.Where(s=>s.T>seconds-2).ToArray();var last3=samples.Where(s=>s.T>seconds-3).Select(s=>s.Dir-targetYaw).ToArray();
        // a crossing counts when the error swings through zero by more than 0.05 deg on both sides (numerical rest is no oscillation)
        int changes=0;int? side=null;foreach(var e in last3){if(Math.Abs(e)<.05)continue;var sign=Math.Sign(e);if(side is {} prev&&prev!=sign)changes++;side=sign;}
        double settle=0;for(int i=samples.Count-1;i>=0;i--)if(Math.Abs(samples[i].Dir-targetYaw)>.5){settle=samples[i].T;break;}
        return new(m.Name,turretMode?"0.22.87":"0.22.86",userTracks?"tracks":"still",samples[^1].Dir-targetYaw,last2.Max(s=>s.Dir)-last2.Min(s=>s.Dir),last2.Max(s=>Math.Abs(s.Dir-targetYaw)),
            samples.Max(s=>Math.Abs(s.HeadYaw)),settle,samples[^1].Pitch-targetPitch,changes);
    }

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT87: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();
        Directory.CreateDirectory(output);
        var cases=new StringBuilder("test\tcase\texpected\tresult\tok\n");
        void Case(string test,string name,object expected,object result,bool ok){cases.Append(test).Append('\t').Append(name).Append('\t').Append(expected).Append('\t').Append(result).Append('\t').Append(ok).Append('\n');Check(ok,test+": "+name+" (expected "+expected+", got "+result+")");}
        string source=File.ReadAllText(Path.Combine(root,"RuntimeAtgmGuidanceSight.cs"));

        // ================= 1. policy: one tube as now, two and three allowed, ordinary cannon and none refused, a broken tube drops out =================
        {
            bool Decide87(AtgmLinkedLauncher[] linked,bool ordinary,out int[] members){members=AtgmIndependentSightPolicy.Members(linked);return AtgmIndependentSightPolicy.Allows(true,members.Length,ordinary,members.Length>0);}
            foreach(var ordinary in new[]{true,false})Case("1 policy","no tube"+(ordinary?" (ordinary cannon only)":""),"refused",Decide87(Array.Empty<AtgmLinkedLauncher>(),ordinary,out _),!Decide87(Array.Empty<AtgmLinkedLauncher>(),ordinary,out _));
            Case("1 policy","two tubes","allowed, group 334,449",Decide87(new[]{L(334),L(449)},false,out var two)+" "+string.Join(",",two),Decide87(new[]{L(334),L(449)},false,out two)&&two.SequenceEqual(new[]{0,1}));
            Case("1 policy","three tubes","allowed, group of 3",Decide87(new[]{L(512),L(334),L(449)},false,out var three)+" "+string.Join(",",three),Decide87(new[]{L(512),L(334),L(449)},false,out three)&&three.SequenceEqual(new[]{1,2,0}));
            Case("1 policy","two tubes plus an ordinary cannon on the sight","refused",Decide87(new[]{L(334),L(449)},true,out _),!Decide87(new[]{L(334),L(449)},true,out _));
            Case("1 policy","two tubes, one broken","group of the healthy one",Decide87(new[]{L(334,healthy:false),L(449)},false,out var broken)+" "+string.Join(",",broken),Decide87(new[]{L(334,healthy:false),L(449)},false,out broken)&&broken.SequenceEqual(new[]{1}));
            Case("1 policy","two tubes, one without a launchable ATGM","group of the other",string.Join(",",AtgmIndependentSightPolicy.Members(new[]{L(334),L(449,compatible:false)})),AtgmIndependentSightPolicy.Members(new[]{L(334),L(449,compatible:false)}).SequenceEqual(new[]{0}));
            Case("1 policy","two tubes, both broken","refused",Decide87(new[]{L(334,healthy:false),L(449,healthy:false)},false,out _),!Decide87(new[]{L(334,healthy:false),L(449,healthy:false)},false,out _));
            Case("1 policy","a tube of another vehicle","not in the group",string.Join(",",AtgmIndependentSightPolicy.Members(new[]{L(334),L(449,same:false)})),AtgmIndependentSightPolicy.Members(new[]{L(334),L(449,same:false)}).SequenceEqual(new[]{0}));
            Case("1 policy","another vehicle's sight (not the player)","refused as 0.22.86",AtgmIndependentSightPolicy.Allows(false,2,false,true),!AtgmIndependentSightPolicy.Allows(false,2,false,true));
            Case("1 policy","the group runs in VUID order (primary = lowest)","449,334 -> 334,449",string.Join(",",AtgmIndependentSightPolicy.Members(new[]{L(449),L(334)})),AtgmIndependentSightPolicy.Members(new[]{L(449),L(334)}).SequenceEqual(new[]{1,0}));
            IntPtr[] G(params int[] v)=>v.Select(P).ToArray();
            Case("1 policy","a session keeps going when a tube leaves","[334,449]->[449] kept",AtgmIndependentSightPolicy.Keeps(G(334,449),G(449)),AtgmIndependentSightPolicy.Keeps(G(334,449),G(449)));
            Case("1 policy","one tube: only the same tube keeps the session (0.22.86)","[334]->[334] yes, [334]->[449] no",AtgmIndependentSightPolicy.Keeps(G(334),G(334))+"/"+AtgmIndependentSightPolicy.Keeps(G(334),G(449)),AtgmIndependentSightPolicy.Keeps(G(334),G(334))&&!AtgmIndependentSightPolicy.Keeps(G(334),G(449)));
            Case("1 policy","a joined or swapped tube, or none left: bind anew","no",AtgmIndependentSightPolicy.Keeps(G(334),G(334,449))+"/"+AtgmIndependentSightPolicy.Keeps(G(334,449),G(334,512))+"/"+AtgmIndependentSightPolicy.Keeps(G(334,449),G()),
                !AtgmIndependentSightPolicy.Keeps(G(334),G(334,449))&&!AtgmIndependentSightPolicy.Keeps(G(334,449),G(334,512))&&!AtgmIndependentSightPolicy.Keeps(G(334,449),G()));
            // gunner: the head moves when at least one tube of the group has a valid gunner; one tube is the 0.22.86 rule
            Case("1 policy","gunner on one tube of two","head moves",AtgmIndependentSightPolicy.HeadMoves(new[]{(false,false),(true,true)}),AtgmIndependentSightPolicy.HeadMoves(new[]{(false,false),(true,true)}));
            Case("1 policy","no valid gunner on any tube","head locked",AtgmIndependentSightPolicy.HeadMoves(new[]{(true,false),(false,true)}),!AtgmIndependentSightPolicy.HeadMoves(new[]{(true,false),(false,true)}));
            Case("1 policy","one tube: the gunner rule of 0.22.86 (AtgmSightHead.CanMove)","4 of 4",new[]{(true,true),(true,false),(false,true),(false,false)}.Count(c=>AtgmIndependentSightPolicy.HeadMoves(new[]{c})==AtgmSightHead.CanMove(c.Item1,c.Item2)),
                new[]{(true,true),(true,false),(false,true),(false,false)}.All(c=>AtgmIndependentSightPolicy.HeadMoves(new[]{c})==AtgmSightHead.CanMove(c.Item1,c.Item2)));
        }

        // ================= 2. axes: one tilting block is no conflict, own axes both driven, a shared gun elevation refused, shared yaw native =================
        {
            // pointers: 147 turret yaw (shared with the 25 mm), 428 the TOW block's laying drive, 153 the 25 mm's laying drive
            var gun=A(147,153);
            var block=new[]{A(147,428),A(147,428)};
            Case("2 axes","two tubes on one laying drive (one tilting block)","no conflict",AtgmGuidanceAxisOwnership.GroupSharedElevation(block,new[]{gun}),!AtgmGuidanceAxisOwnership.GroupSharedElevation(block,new[]{gun}));
            var ex=AtgmGuidanceAxisOwnership.GroupExclusiveAxes(block,new[]{gun});
            Case("2 axes","one tilting block: its drive is held or driven once","{428}",string.Join(",",ex),ex.Count==1&&ex.Contains(P(428)));
            var ownDrives=new[]{A(147,428),A(147,429)};var ex2=AtgmGuidanceAxisOwnership.GroupExclusiveAxes(ownDrives,new[]{gun});
            Case("2 axes","two tubes, each with an own laying drive","both: {428,429}",string.Join(",",ex2.OrderBy(x=>x)),!AtgmGuidanceAxisOwnership.GroupSharedElevation(ownDrives,new[]{gun})&&ex2.SetEquals(new[]{P(428),P(429)}));
            var shared=new[]{A(147,153),A(147,428)};
            Case("2 axes","a tube on the 25 mm's elevation","refused, as 0.22.86",AtgmGuidanceAxisOwnership.GroupSharedElevation(shared,new[]{gun}),
                AtgmGuidanceAxisOwnership.GroupSharedElevation(shared,new[]{gun}));
            Case("2 axes","shared turret yaw stays native (never held)","147 not held",string.Join(",",ex),!ex.Contains(P(147))&&!ex2.Contains(P(147)));
            Case("2 axes","an unshared turret yaw of the group alone is its own","{147,428}",string.Join(",",AtgmGuidanceAxisOwnership.GroupExclusiveAxes(block,Array.Empty<AtgmAimingAxes>()).OrderBy(x=>x)),
                AtgmGuidanceAxisOwnership.GroupExclusiveAxes(block,Array.Empty<AtgmAimingAxes>()).SetEquals(new[]{P(147),P(428)}));
            // the refusal text: the 0.22.86 text with an ordinary cannon; with an unlinked dedicated launcher on the same mount, the advice
            var conflicts=AtgmGuidanceAxisOwnership.ElevationConflicts(new[]{A(147,428)},new[]{gun,A(147,428)});
            Case("2 axes","the conflicting outside cannon on the TOW block","index 1 (tube 449)",string.Join(",",conflicts),conflicts.SequenceEqual(new[]{1}));
            var plain=AtgmSightLog.SkipSharedElevation(357,Array.Empty<int>()); // herpak: its comparison with the 0.22.86 template is removed
            Case("2 axes","refusal text with another gun: no advice","no 'link every launcher'",plain,!plain.Contains("link every launcher",StringComparison.Ordinal));
            var advice=AtgmSightLog.SkipSharedElevation(357,new[]{449});
            Case("2 axes","refusal text with an unlinked tube on the mount: the advice","...; link every launcher on this mount to the sight (unlinked launcher 449).",advice,
                advice=="[ATGM guidance sight] SKIP sight=357: launcher shares elevation with another cannon; use a separate elevation mount or a fixed tube; link every launcher on this mount to the sight (unlinked launcher 449).");
        }

        // ================= 3. guidance: both tubes see the same sight, two missiles in the air =================
        var flight=new StringBuilder("sight\tmissile\trule\tdistance from the sight line at launch (m)\tat 700 m\tat 1500 m\tboth in the air (s)\n");
        {
            var group=new[]{P(334),P(449)};
            Case("3 guidance","both tubes are in the sight's group (one head, one session, one direction for TryAim)","334 and 449",AtgmIndependentSightPolicy.InGroup(group,P(334))+"/"+AtgmIndependentSightPolicy.InGroup(group,P(449)),
                AtgmIndependentSightPolicy.InGroup(group,P(334))&&AtgmIndependentSightPolicy.InGroup(group,P(449))&&!AtgmIndependentSightPolicy.InGroup(group,P(88))&&!AtgmIndependentSightPolicy.InGroup(group,IntPtr.Zero));
            Case("3 guidance","a missile from 334 keeps its guidance when 449 fires","not superseded",AtgmIndependentSightPolicy.GuidesTogether(group,P(334),"sight",P(449),"sight"),AtgmIndependentSightPolicy.GuidesTogether(group,P(334),"sight",P(449),"sight"));
            Case("3 guidance","the same tube firing again, one tube, another tube, keyboard: superseded as 0.22.86","all false",
                AtgmIndependentSightPolicy.GuidesTogether(group,P(334),"sight",P(334),"sight")+"/"+AtgmIndependentSightPolicy.GuidesTogether(new[]{P(334)},P(334),"sight",P(449),"sight")+"/"+AtgmIndependentSightPolicy.GuidesTogether(group,P(88),"sight",P(449),"sight")+"/"+AtgmIndependentSightPolicy.GuidesTogether(group,P(334),"keyboard",P(449),"keyboard"),
                !AtgmIndependentSightPolicy.GuidesTogether(group,P(334),"sight",P(334),"sight")&&!AtgmIndependentSightPolicy.GuidesTogether(new[]{P(334)},P(334),"sight",P(449),"sight")&&!AtgmIndependentSightPolicy.GuidesTogether(group,P(88),"sight",P(449),"sight")&&!AtgmIndependentSightPolicy.GuidesTogether(group,P(334),"keyboard",P(449),"keyboard")&&!AtgmIndependentSightPolicy.GuidesTogether(group,P(334),"sight",P(449),"keyboard"));
            // two missiles on the same sight ray: the TOW profile's turn rate and LOS-lead law, the production steering (AtgmGuidance.Step, TryAimAlongSightRay)
            var tow=ShellProfiles.Parse(ReleaseProfiles.Defaults()).First(p=>p.Id=="atgm_saclos_direct_v1").Atgm!;
            foreach(var (rate,label,limit) in new[]{(0.0,"sight held still",.5),(0.3,"sight turning 0.3 deg/s (crossing target)",1.5)})
            {
                var now=TwoMissiles(tow,rate,false);var before=TwoMissiles(tow,rate,true);
                for(int i=0;i<2;i++)flight.Append(label).Append('\t').Append(i==0?"334 (first)":"449 (second, +1 s)").Append("\t0.22.87 (both guided)\t").Append(F(now.Initial[i],"0.00")).Append('\t').Append(F(now.At700[i],"0.00")).Append('\t').Append(F(now.AtEnd[i],"0.00")).Append('\t').Append(F(now.Together,"0.00")).Append('\n');
                flight.Append(label).Append("\t334 (first)\t0.22.86 rule (superseded at +1 s)\t").Append(F(before.Initial[0],"0.00")).Append('\t').Append(F(before.At700[0],"0.00")).Append('\t').Append(F(before.AtEnd[0],"0.00")).Append("\t\n");
                Case("3 guidance",$"two missiles in the air at once, {label}: both on the sight line",$"both within {F(limit)} m at 700 m and at 1500 m, at least 4 s together",
                    $"334: {F(now.At700[0],"0.00")}/{F(now.AtEnd[0],"0.00")} m, 449: {F(now.At700[1],"0.00")}/{F(now.AtEnd[1],"0.00")} m, together {F(now.Together,"0.0")} s",
                    now.Done&&now.At700.All(x=>x<limit)&&now.AtEnd.All(x=>x<limit)&&now.Initial.All(x=>x>.3)&&now.Together>=4);
                if(rate>0)Case("3 guidance","with the 0.22.86 rule the first missile would leave the moving sight line","more than 5 m off at 1500 m",F(before.AtEnd[0],"0.00"),before.AtEnd[0]>5);
            }
        }

        // ================= 4. one tube (herpak: the comparisons with the 0.22.86 build - axes, log templates, head slew - are removed) =================
        var logLines=new StringBuilder();
        {
            foreach(var (name,line) in new[]{("BIND",AtgmSightLog.Bind(new[]{334},357)),("SKIP assignment, none",AtgmSightLog.SkipAssignment(357,0,0,false)),("SKIP assignment, ordinary shared",AtgmSightLog.SkipAssignment(357,1,1,true)),
                ("SKIP assignment, broken tube",AtgmSightLog.SkipAssignment(357,1,0,false)),("SKIP shared elevation",AtgmSightLog.SkipSharedElevation(357,Array.Empty<int>())),("shared traverse",AtgmSightLog.SharedTraverse(357)),("no gunner",AtgmSightLog.NoGunner(new[]{334}))})
                logLines.Append("one tube, ").Append(name).Append(":\n  ").Append(line).Append('\n');
            logLines.Append("group of 2, BIND:\n  ").Append(AtgmSightLog.Bind(new[]{334,449},357)).Append('\n');
            logLines.Append("group of 2, refused (ordinary cannon on the sight):\n  ").Append(AtgmSightLog.SkipAssignment(357,2,2,true)).Append('\n');
            logLines.Append("group, a tube left:\n  ").Append(AtgmSightLog.Group(357,new[]{449},new[]{334})).Append('\n');
            logLines.Append("group, no gunner:\n  ").Append(AtgmSightLog.NoGunner(new[]{334,449})).Append('\n');
            logLines.Append("shared turret (one tube or a group):\n  ").Append(AtgmSightLog.TurretFollows(357)).Append('\n');
            logLines.Append("unlinked tube on the mount:\n  ").Append(AtgmSightLog.SkipSharedElevation(357,new[]{449})).Append('\n');
            Case("4 one tube","the BIND line of the group as the brief asks","[ATGM guidance sight] BIND launchers=334,449 sight=357 (group of 2); ...",AtgmSightLog.Bind(new[]{334,449},357),
                AtgmSightLog.Bind(new[]{334,449},357).StartsWith("[ATGM guidance sight] BIND launchers=334,449 sight=357 (group of 2); ",StringComparison.Ordinal));
            // the one-tube runtime keeps the 0.22.86 calls for the ring axes and the AXES description
            Case("4 one tube","one tube: the ring axes and the AXES text are the 0.22.86 calls","RuntimeAtgmSightMount.Axes / Describe",
                Regex.Matches(source,@"if\(group\.Length==1&&linked\.Count==1\)return RuntimeAtgmSightMount\.(Axes|Describe)\(sight,group\[0\]\);").Count,
                Regex.Matches(source,@"if\(group\.Length==1&&linked\.Count==1\)return RuntimeAtgmSightMount\.(Axes|Describe)\(sight,group\[0\]\);").Count==2);
        }

        // ================= 5. the Bradley fixture: how the two TOW tubes are mounted =================
        var fixture=new Dictionary<string,object?>();
        {
            var path=Path.Combine(root,"tests","fixtures","Bradley M3A3.blueprint");
            var sha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            Case("5 Bradley","the fixture is the user's blueprint of 15-11 22:41 (copied, read only)",BradleySha,sha,sha==BradleySha);
            using var doc=JsonDocument.Parse(File.ReadAllText(path));var r=doc.RootElement;
            var objects=r.GetProperty("objects").EnumerateArray().ToDictionary(o=>o.GetProperty("vuid").GetInt32(),o=>o);
            var blueprints=r.GetProperty("blueprints").EnumerateArray().ToDictionary(b=>b.GetProperty("id").GetInt32(),b=>b);
            int? Int(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Number?v.GetInt32():null;
            IEnumerable<JsonElement> Up(int vuid){for(var v=objects[vuid].GetProperty("pvuid").GetInt32();v>=0&&objects.ContainsKey(v);v=objects[v].GetProperty("pvuid").GetInt32())yield return objects[v];}
            JsonElement? Mantlet(int vuid){foreach(var a in Up(vuid))if(a.TryGetProperty("mantletBlueprint",out _))return a;return null;}
            JsonElement? Ring(int vuid){foreach(var a in Up(vuid))if(a.TryGetProperty("turretRing",out _))return a;return null;}
            int RingMotor(JsonElement ring)=>blueprints[ring.GetProperty("ringBlueprintVuid").GetInt32()].GetProperty("blueprint").GetProperty("motorVuid").GetInt32();
            var cannons=objects.Values.Where(o=>o.TryGetProperty("cannon",out _)).Select(o=>
            {
                var vuid=o.GetProperty("vuid").GetInt32();var inst=blueprints[o.GetProperty("cannonInstanceBlueprintVuid").GetInt32()].GetProperty("blueprint");
                var mantlet=Mantlet(vuid);var ring=Ring(vuid);
                return (Object:vuid,Cannon:o.GetProperty("cannon").GetInt32(),Dedicated:o.GetProperty("guid").GetString()==AtgmLauncherModel.Guid,
                    FireGroup:inst.GetProperty("fireGroup").GetInt32(),Sight:inst.GetProperty("sightVuid").GetInt32(),
                    Mantlet:mantlet?.GetProperty("vuid").GetInt32(),Drive:mantlet?.GetProperty("mantletBlueprint").GetProperty("drive_vuid").GetInt32(),
                    Ring:ring is {} rr?rr.GetProperty("turretRing").GetInt32():(int?)null,Motor:ring is {} rm?RingMotor(rm):(int?)null);
            }).OrderBy(c=>c.Cannon).ToArray();
            var tows=cannons.Where(c=>c.Dedicated).ToArray();var gun=cannons.Single(c=>!c.Dedicated);
            var sightObject=objects.Values.Single(o=>o.GetProperty("guid").GetString()==SightGuid);var sightVuid=sightObject.GetProperty("sight").GetInt32();
            var sightRing=Ring(sightObject.GetProperty("vuid").GetInt32());
            var drives=blueprints.Values.Where(b=>b.GetProperty("type").GetString()=="layingDrive").ToDictionary(b=>b.GetProperty("id").GetInt32(),b=>b.GetProperty("blueprint"));
            var seats=blueprints.Values.Where(b=>b.GetProperty("type").GetString()=="crewSeat").Select(b=>b.GetProperty("blueprint").GetProperty("operatedBehaviours").EnumerateArray().Select(x=>x.GetInt32()).ToHashSet()).ToArray();
            Case("5 Bradley","two dedicated TOW launchers and one ordinary gun","334,449 and 88",string.Join(",",tows.Select(t=>t.Cannon))+" and "+gun.Cannon,tows.Select(t=>t.Cannon).SequenceEqual(new[]{334,449})&&gun.Cannon==88);
            Case("5 Bradley","both tubes on one mantlet and one laying drive: one tilting block","mantlet 425, drive 428",string.Join(";",tows.Select(t=>$"{t.Cannon}: mantlet {t.Mantlet}, drive {t.Drive}")),
                tows.All(t=>t.Mantlet==425&&t.Drive==428));
            Case("5 Bradley","the 25 mm has its own laying drive","153",gun.Drive?.ToString()??"none",gun.Drive==153&&gun.Drive!=428);
            Case("5 Bradley","the tubes, the 25 mm and the guidance sight ride on one turret (ring 32, motor 147)","ring 32 / motor 147",
                string.Join(";",cannons.Select(c=>$"{c.Cannon}: ring {c.Ring} motor {c.Motor}"))+$"; sight {sightVuid}: ring {sightRing?.GetProperty("turretRing").GetInt32()}",
                cannons.All(c=>c.Ring==32&&c.Motor==147)&&sightRing?.GetProperty("turretRing").GetInt32()==32&&sightRing?.GetProperty("guid").GetString()==TurretRingGuid);
            var towDrive=objects.Values.First(o=>Int(o,"layingDrive")==428);var towDriveBp=drives[towDrive.GetProperty("layingDriveBlueprintVuid").GetInt32()];
            var azimuthFixed=towDriveBp.GetProperty("azimuth").GetProperty("max").GetDouble()==0&&towDriveBp.GetProperty("azimuth").GetProperty("min").GetDouble()==0;
            Case("5 Bradley","the TOW drive has no azimuth travel: the tubes' yaw is the turret motor (assumed mapping)","azimuth 0..0",azimuthFixed,azimuthFixed);
            Case("5 Bradley","one gunner seat operates both tubes, both drives and the turret motor","seat with 88,147,153,428,334,449",seats.Any(s=>new[]{88,147,153,428,334,449}.All(s.Contains)),seats.Any(s=>new[]{88,147,153,428,334,449}.All(s.Contains)));
            // the runtime axes as the blueprint gives them (assumed: Horizontal = ring motor, Vertical = mantlet drive)
            AtgmAimingAxes Ax((int Object,int Cannon,bool Dedicated,int FireGroup,int Sight,int? Mantlet,int? Drive,int? Ring,int? Motor) c)=>A(c.Motor??0,c.Drive??0);
            AtgmTurretRing TurretRing(int[] group,HashSet<IntPtr> exclusive)=>new(sightRing?.GetProperty("guid").GetString()==TurretRingGuid,sightRing?.GetProperty("guid").GetString()==ElevationRingGuid,true,
                cannons.Where(c=>group.Contains(c.Cannon)).Any(c=>c.Motor==147),exclusive.Contains(P(147)));
            (bool Allowed,string Line,HashSet<IntPtr> Exclusive,bool Turret,int[] Group) Bind(Func<int,int> sightOf)
            {
                var linked=cannons.Where(c=>sightOf(c.Cannon)==sightVuid).ToArray();var tubes=linked.Where(c=>c.Dedicated).ToArray();var ordinary=linked.Any(c=>!c.Dedicated);
                var members=AtgmIndependentSightPolicy.Members(tubes.Select(t=>L(t.Cannon)).ToArray());
                if(!AtgmIndependentSightPolicy.Allows(true,members.Length,ordinary,members.Length>0))return (false,AtgmSightLog.SkipAssignment(sightVuid,tubes.Length,members.Length,ordinary),new(),false,Array.Empty<int>());
                var group=members.Select(i=>tubes[i]).ToArray();var outside=cannons.Where(c=>!tubes.Contains(c)).ToArray();
                var own=group.Select(Ax).ToArray();var others=outside.Select(Ax).ToArray();
                if(AtgmGuidanceAxisOwnership.GroupSharedElevation(own,others))
                    return (false,AtgmSightLog.SkipSharedElevation(sightVuid,AtgmGuidanceAxisOwnership.ElevationConflicts(own,others).Select(i=>outside[i]).Where(c=>c.Dedicated).Select(c=>c.Cannon).ToArray()),new(),false,group.Select(g=>g.Cannon).ToArray());
                var ex=AtgmGuidanceAxisOwnership.GroupExclusiveAxes(own,others);var ids=group.Select(g=>g.Cannon).ToArray();
                return (true,AtgmSightLog.Bind(ids,sightVuid),ex,AtgmSharedTurretHead.Applies(true,new[]{TurretRing(ids,ex)}),ids);
            }
            var saved=Bind(c=>cannons.Single(x=>x.Cannon==c).Sight);
            Case("5 Bradley","as saved (22:41): sight 357 on tube 334 only; 449 sits on the thermal sight 363 with the 25 mm","refused, with the advice to link 449",saved.Line,
                !saved.Allowed&&saved.Line==AtgmSightLog.SkipSharedElevation(357,new[]{449})&&cannons.Single(c=>c.Cannon==449).Sight==363&&cannons.Single(c=>c.Cannon==334).Sight==357);
            var linkedBoth=Bind(c=>c is 334 or 449?sightVuid:cannons.Single(x=>x.Cannon==c).Sight);
            Case("5 Bradley","both tubes linked to sight 357 (the setup of 22:11): a group of 2 within the rules","BIND launchers=334,449 sight=357 (group of 2); TOW block 428 held once; yaw 147 native",
                linkedBoth.Line+" exclusive="+string.Join(",",linkedBoth.Exclusive),linkedBoth.Allowed&&linkedBoth.Line==AtgmSightLog.Bind(new[]{334,449},357)&&linkedBoth.Exclusive.SetEquals(new[]{P(428)}));
            Case("5 Bradley","the sight is on the shared turret: the turret follows, the head only elevates","turret follows",linkedBoth.Turret,linkedBoth.Turret&&saved.Turret==false);
            var withGun=Bind(c=>sightVuid);
            Case("5 Bradley","the 25 mm on sight 357 as well: refused (ordinary cannon on the sight)","refused",withGun.Line,!withGun.Allowed&&withGun.Line.Contains("ordinaryShared=True",StringComparison.Ordinal));
            fixture["sha256"]=sha;fixture["tubes"]=tows.Select(t=>new{cannon=t.Cannon,partObject=t.Object,fireGroup=t.FireGroup,sightVuid=t.Sight,mantlet=t.Mantlet,elevationDrive=t.Drive,ring=t.Ring,traverseMotor=t.Motor}).ToArray();
            fixture["gun25mm"]=new{cannon=gun.Cannon,fireGroup=gun.FireGroup,sightVuid=gun.Sight,elevationDrive=gun.Drive,ring=gun.Ring,traverseMotor=gun.Motor};
            fixture["guidanceSight"]=new{part=sightObject.GetProperty("vuid").GetInt32(),sight=sightVuid,ring=sightRing?.GetProperty("turretRing").GetInt32()};
            fixture["mount"]="one tilting block: both tubes under mantlet 425 / trunnions 430 on laying drive 428 (elevation +20/-10, no azimuth); the turret ring 32 (motor 147) yaws both tubes, the 25 mm (own drive 153) and the sight";
            fixture["asSaved"]=new{allowed=saved.Allowed,line=saved.Line};
            fixture["bothLinked"]=new{allowed=linkedBoth.Allowed,line=linkedBoth.Line,heldOrDriven=linkedBoth.Exclusive.Select(x=>x.ToInt64()).ToArray(),turretFollows=linkedBoth.Turret};
            fixture["assumptions"]="runtime axes from the tree: VerticalAimingMechanism = the mantlet's laying drive, HorizontalAimingMechanism = the turret ring's motor; motor connected (log 22:26: connected=True)";
        }

        // ================= 6. the turret: on a shared turret the head yaw stays 0 and the direction settles; free sight and own ring unchanged =================
        var sim=new StringBuilder("model\tmode\tuser\tfinal error deg\tpeak-to-peak last 2 s\tmax error last 2 s\tmax head yaw\tsettled after s (0.5 deg)\tfinal pitch error\tsign changes last 3 s\n");
        {
            var sharedRing=new AtgmTurretRing(SightAxis:true,Pitch:false,Connected:true,LauncherYaw:true,Exclusive:false);
            Case("6 turret","the Bradley's turret (shared native ring carrying the sight and the tubes' yaw)","turret follows",AtgmSharedTurretHead.Applies(true,new[]{sharedRing}),AtgmSharedTurretHead.Applies(true,new[]{sharedRing}));
            var notTurret=new (string Name,bool HeadMode,AtgmTurretRing[] Rings)[]
            {
                ("free sight, no ring",true,Array.Empty<AtgmTurretRing>()),
                ("ring that does not carry the sight",true,new[]{sharedRing with{SightAxis=false}}),
                ("own unshared ring (ring mode: the sight drives it)",false,new[]{sharedRing with{Exclusive=true}}),
                ("own unshared ring, as seen in head mode",true,new[]{sharedRing with{Exclusive=true}}),
                ("elevation ring",true,new[]{sharedRing with{Pitch=true}}),
                ("ring whose motor is not connected",true,new[]{sharedRing with{Connected=false}}),
                ("shared ring that yaws only another gun",true,new[]{sharedRing with{LauncherYaw=false}}),
            };
            foreach(var n in notTurret)Case("6 turret",n.Name,"no change (0.22.86 head path)",AtgmSharedTurretHead.Applies(n.HeadMode,n.Rings),!AtgmSharedTurretHead.Applies(n.HeadMode,n.Rings));
            var cmd=new AtgmHeadAngles(37.5f,12.25f);
            Case("6 turret","decision on a shared turret: head yaw 0, the pitch kept, the camera left native","(0, 12.25), no camera write",$"{AtgmSharedTurretHead.Command(cmd,true)}, write={AtgmSharedTurretHead.WritesCamera(true)}",
                AtgmSharedTurretHead.Command(cmd,true)==new AtgmHeadAngles(0,12.25f)&&!AtgmSharedTurretHead.WritesCamera(true)&&AtgmSharedTurretHead.Command(cmd,false)==cmd&&AtgmSharedTurretHead.WritesCamera(false));
            var models=new[]{new TurretModel("fast 60 deg/s, lag 0.08 s, delay 0.05 s",60,4,.08,.05),new TurretModel("medium 25 deg/s, lag 0.2 s, delay 0.1 s",25,3,.2,.1),new TurretModel("slow 15 deg/s, lag 0.3 s, delay 0.2 s",15,2,.3,.2)};
            foreach(var m in models)foreach(var tracks in new[]{true,false})
            {
                var now=Simulate(m,true,tracks);var before=Simulate(m,false,tracks);
                foreach(var x in new[]{before,now})sim.Append(x.Model).Append('\t').Append(x.Mode).Append('\t').Append(x.User).Append('\t').Append(F(x.FinalError,"0.000")).Append('\t').Append(F(x.PeakToPeakLast2s,"0.000")).Append('\t')
                    .Append(F(x.MaxErrorLast2s,"0.000")).Append('\t').Append(F(x.MaxHeadYaw,"0.000")).Append('\t').Append(F(x.SettleSeconds,"0.00")).Append('\t').Append(F(x.FinalPitchError,"0.000")).Append('\t').Append(x.SignChangesLast3s).Append('\n');
                Case("6 turret",$"{m.Name}, gunner {(tracks?"keeps tracking":"rests the mouse")}: head yaw stays 0","0",F(now.MaxHeadYaw),now.MaxHeadYaw==0);
                Case("6 turret",$"{m.Name}, gunner {(tracks?"keeps tracking":"rests the mouse")}: the direction settles within 0.5 deg and stays",
                    "last 2 s within 0.5 deg, peak-to-peak below 0.5 deg, at most one crossing in the last 3 s",$"max {F(now.MaxErrorLast2s,"0.000")}, p-p {F(now.PeakToPeakLast2s,"0.000")}, crossings {now.SignChangesLast3s}, settled after {F(now.SettleSeconds,"0.00")} s",
                    now.MaxErrorLast2s<=.5&&now.PeakToPeakLast2s<=.5&&now.SignChangesLast3s<=1&&now.SettleSeconds<8);
                Case("6 turret",$"{m.Name}: the head elevates to the camera (pitch within 0.5 deg)","8 deg",F(8+now.FinalPitchError),Math.Abs(now.FinalPitchError)<=.5);
            }
            // the code: only the two per-frame camera writes are skipped on a shared turret; no native layer command is added, changed or held
            Case("6 turret","the per-frame camera writes are skipped only for a turret-follows head","2 guarded writes",Regex.Matches(source,@"AtgmSharedTurretHead\.WritesCamera\(head\.Turret\)\)HeadCamera\(head,__instance\);").Count,
                Regex.Matches(source,@"AtgmSharedTurretHead\.WritesCamera\(head\.Turret\)\)HeadCamera\(head,__instance\);").Count==2);
        }

        // ================= the files: the declared RuntimeAtgm line, no hook added (herpak: the inventory and byte comparisons with src-0.22.86 are removed) =================
        {
            static int N2(string s,string what)=>Regex.Matches(s,Regex.Escape(what)).Count;
            static string[] Hooks(string text)=>text.Split('\n').Where(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)).Select(l=>l.Trim()).ToArray();
            var atgm=File.ReadAllText(Path.Combine(root,"RuntimeAtgm.cs"));
            Case("files","RuntimeAtgm.cs: the declared supersede line, marked once","1 line",N2(atgm,ReplacedMark),N2(atgm,ReplacedMark)==1&&atgm.Contains(Replaced[0].New+ReplacedMark,StringComparison.Ordinal));
            bool Skip(string rel)=>rel.Split('/').Any(p=>p is "bin" or "obj" or "tests" or ".git");
            var all87=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories).Where(f=>!Skip(Path.GetRelativePath(root,f).Replace('\\','/'))).Sum(f=>Hooks(File.ReadAllText(f)).Length);
            Case("files","[HarmonyPatch annotations","47",all87,all87==47);
        }

        File.WriteAllText(Path.Combine(output,"ATGM-GROUP-CASES.tsv"),cases.ToString());
        File.WriteAllText(Path.Combine(output,"ATGM-TWO-MISSILES.tsv"),flight.ToString());
        File.WriteAllText(Path.Combine(output,"ATGM-LOG-LINES.txt"),logLines.ToString());
        File.WriteAllText(Path.Combine(output,"TURRET-SIM.tsv"),sim.ToString());
        File.WriteAllText(Path.Combine(output,"BRADLEY-FIXTURE.json"),JsonSerializer.Serialize(fixture,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"FEAT87-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures,
            bind=AtgmSightLog.Bind(new[]{334,449},357),turretFollows=AtgmSightLog.TurretFollows(357)},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT87 PASS: {checks} checks, {failures} failures");
    }
}
