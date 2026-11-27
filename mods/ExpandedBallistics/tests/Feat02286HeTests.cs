using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.86 HE (user 15-11, "Ja, bouw HE"): (A) the blast filter: inside the HE scope the game's loop over the pools of a vehicle is replaced by the same loop with
/// the game's own value for exterior modules, for interior modules after a perforation, and the value x shock(calibre) for interior modules otherwise; (B) the
/// perforating part: an HE type gets [AP part, HE], the part has a fixed budget, a perforation ends in fragments behind the plate and the body never flies on,
/// without perforation nothing flies on and there is no ricochet below the limit. The pool arithmetic is a managed copy of the game's HealthPool.HealthDelta
/// (disassembly 0x1DD8010) and VehicleHealthRegister.HealthDelta(float,mods) (0x1FCEEB0); the native block outcome is the LOS-MAP model of SimulateFragment
/// (test-only, as FEAT78). The rules, the plan, the settings, the report texts and the presentation are the production code.
/// </summary>
internal static class Feat02286HeTests
{
    /// <summary>The production files 0.22.86 may change on top of 0.22.84.</summary>
    internal static bool IsPlanned(string rel)=>rel is "RuntimeShellSelection.cs" or "RuntimeDamageFeed.cs" or "RuntimeSpall.cs" or "ShellBalance.cs" or "RuntimeArmourResponses.cs" or "HitPanelModel.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    /// <summary>Files whose 0.22.86 change is marked: insertions ("// 0.22.86 HE begin" ... "// 0.22.86 HE end", lines ending in "// 0.22.86 HE") and the declared replaced lines (ending in "// 0.22.86 HE replaced").</summary>
    internal static readonly string[] Marked={"RuntimeShellSelection.cs","RuntimeDamageFeed.cs","RuntimeSpall.cs","ShellBalance.cs","RuntimeArmourResponses.cs","HitPanelModel.cs"};
    /// <summary>The 0.22.84 line of every line 0.22.86 replaces (key: file and the new line without its marker).</summary>
    internal static readonly (string File,string New,string Old)[] Replaced=
    {
        ("RuntimeShellSelection.cs","            __instance.penetration.Value = profile.Behavior==\"he\" ? HePenetration(cannon.Blueprint.Caliber) : profile.Behavior is \"heat\" or \"hesh\" or \"atgm\" or \"atgm_gun\" ? (float)RuntimeShellEra.ChemicalBudget(profile,cannon.Blueprint.Caliber,cannon.Vehicle) : PenetrationUtils.ComputePenetration(dart.Diameter * 1000,",
                                    "            __instance.penetration.Value = profile.Behavior==\"he\" ? 0 : profile.Behavior is \"heat\" or \"hesh\" or \"atgm\" or \"atgm_gun\" ? (float)RuntimeShellEra.ChemicalBudget(profile,cannon.Blueprint.Caliber,cannon.Vehicle) : PenetrationUtils.ComputePenetration(dart.Diameter * 1000,"),
        ("RuntimeShellSelection.cs","    private static void ExplosiveImpact(ref ProjectileInstance projectile,out ImpactState __state){Impact(ref projectile,out __state);HeScope(ref projectile,false);}",
                                    "    private static void ExplosiveImpact(ref ProjectileInstance projectile,out ImpactState __state) => Impact(ref projectile,out __state);"),
        ("RuntimeShellSelection.cs","    private static void ExplosiveEnd(ImpactState __state){try{EndImpact(__state);}finally{HeBlast.End();}}",
                                    "    private static void ExplosiveEnd(ImpactState __state) => EndImpact(__state);"),
        ("RuntimeShellSelection.cs","    private static void ExplosivePhysics(ref ProjectileInstance projectile,out ImpactState __state){Impact(ref projectile,out __state);HeScope(ref projectile,true);}",
                                    "    private static void ExplosivePhysics(ref ProjectileInstance projectile,out ImpactState __state) => Impact(ref projectile,out __state);"),
        ("RuntimeShellSelection.cs","    private static void ExplosivePhysicsEnd(ImpactState __state){try{EndImpact(__state);}finally{HeBlast.End();}}",
                                    "    private static void ExplosivePhysicsEnd(ImpactState __state) => EndImpact(__state);"),
        ("RuntimeDamageFeed.cs","    private static void BeginExplosive(ref ProjectileInstance __0,out Shot? __state){if(HeResume(ref __0,out __state))return;BeginImpact(ref __0,out __state);if(current!=null)current.HeExplosive=true;}",
                                "    private static void BeginExplosive(ref ProjectileInstance __0,out Shot? __state)=>BeginImpact(ref __0,out __state);"),
        ("RuntimeDamageFeed.cs","    private static bool BeginDirectAll(VehicleHealthRegister __instance,float __0,DamageModelDamageModifiers __1,out RuntimeDamageFeed.HealthScope __state){__state=RuntimeDamageFeed.BeginDirectDamage(__instance);return HeBlast.Filter(__instance,__0,__1);}",
                                "    private static void BeginDirectAll(VehicleHealthRegister __instance,out RuntimeDamageFeed.HealthScope __state)=>__state=RuntimeDamageFeed.BeginDirectDamage(__instance);"),
    };
    private const string ReplacedMark=" // 0.22.86 HE replaced";
    /// <summary>A source text without its 0.22.86 HE change: the marked insertions removed, the replaced lines back as 0.22.84 had them (line endings kept; every new line is unique, so the file name is not needed).</summary>
    internal static string Un86(string text)
    {
        text=Feat02289Un.Un89(text); // 0.22.89 adapted: without the marked 0.22.89 change first (TEST-ADAPTATIONS.md)
        var lines=text.Split('\n');var kept=new List<string>(lines.Length);bool skip=false;
        foreach(var line in lines)
        {
            var core=line.TrimEnd('\r');var cr=line.Length>core.Length?line[core.Length..]:"";var t=core.Trim();
            if(t=="// 0.22.86 HE begin"){skip=true;continue;}
            if(t=="// 0.22.86 HE end"){skip=false;continue;}
            if(skip||core.EndsWith("// 0.22.86 HE",StringComparison.Ordinal))continue;
            if(core.EndsWith(ReplacedMark,StringComparison.Ordinal))
            {
                var neu=core[..^ReplacedMark.Length];var hit=Replaced.FirstOrDefault(r=>r.New==neu);
                kept.Add((hit.Old??"<<unknown replaced line>>")+cr);continue;
            }
            kept.Add(line);
        }
        return string.Join("\n",kept);
    }
    /// <summary>The bytes of a source file without its 0.22.86 HE change (unchanged bytes for any other file).</summary>
    internal static byte[] Un86Bytes(string path)
    {
        var bytes=Feat02289Un.Un89Bytes(path);var name=Path.GetFileName(path); // 0.22.89 adapted: the 0.22.88 bytes first
        return Marked.Contains(name)?Encoding.UTF8.GetBytes(Un86(Encoding.UTF8.GetString(bytes))):bytes;
    }

    // ---------------- the game's pool arithmetic (managed copy, single precision, operation order of the disassembly) ----------------
    internal sealed class Register{internal bool Changed;}
    internal sealed class Pool
    {
        internal float Current,Max,P=1,B=1,Pr=1,T=1,AccP,AccB,AccPr,AccT;internal bool HasChanged;internal Register? Owner;
        /// <summary>HealthPool.HealthDelta(float, DamageModifiers) 0x1DD8010.</summary>
        internal float HealthDelta(float value,float mP,float mB,float mPr,float mT)
        {
            float x6=P*mP,x7=B*mB,x8=Pr*mPr,x9=T*mT;
            float sum=x7+x6;sum=sum+x8;sum=sum+x9;
            float delta=sum*value;
            x6=x6*delta;x7=x7*delta;x8=x8*delta;x9=x9*delta;
            AccP=x6+AccP;AccB=x7+AccB;AccPr=x8+AccPr;AccT=x9+AccT;
            float applied;
            if(0f>delta){var neg=-delta;var cut=Current>neg?neg:Current;applied=-cut;Current=Current-cut;}
            else{var room=Max-Current;applied=room>delta?delta:room;Current=applied+Current;}
            if(applied!=0f||float.IsNaN(applied)){HasChanged=true;if(Owner!=null)Owner.Changed=true;}
            return applied;
        }
        internal Pool Copy(Register? owner)=>new(){Current=Current,Max=Max,P=P,B=B,Pr=Pr,T=T,AccP=AccP,AccB=AccB,AccPr=AccPr,AccT=AccT,HasChanged=HasChanged,Owner=owner};
        internal string Bits=>string.Join(",",new[]{Current,AccP,AccB,AccPr,AccT}.Select(f=>BitConverter.SingleToInt32Bits(f).ToString("X8",CultureInfo.InvariantCulture)))+(HasChanged?"+":"-");
    }
    /// <summary>VehicleHealthRegister.HealthDelta(float, mods) 0x1FCEEB0: the count read once, then HealthPool.HealthDelta(value, mods) on every pool in list order.</summary>
    private static void NativeLoop(List<Pool?> list,float value,float[] m){var n=list.Count;for(int i=0;i<n;i++){var pool=list[i]??throw new NullReferenceException();pool.HealthDelta(value,m[0],m[1],m[2],m[3]);}}
    private sealed class Pools:IHeBlastPools
    {
        internal readonly List<Pool?> List;internal readonly List<string[]?> Owners;internal readonly float[] M;internal int ThrowTypesAt=-1,ThrowApplyAt=-1,Calls;
        internal Pools(List<Pool?> list,List<string[]?> owners,float[] m){List=list;Owners=owners;M=m;}
        public int Count=>List.Count;
        public IReadOnlyList<string>? Types(int index){if(index==ThrowTypesAt)throw new InvalidOperationException("owner lookup failed");_=List[index]??throw new NullReferenceException("pool "+index);return Owners[index];}
        public void Apply(int index,float value){if(index==ThrowApplyAt)throw new InvalidOperationException("damage call failed");Calls++;List[index]!.HealthDelta(value,M[0],M[1],M[2],M[3]);}
    }
    private static readonly (string Name,string[]? Types,float Max,float[] Mods)[] Vehicle=
    {
        ("crew (gunner)",new[]{"CrewSeat","VehicleComponent"},100,new[]{1f,1f,1f,1f}),
        ("crew (driver)",new[]{"CrewSeat","VehicleComponent"},100,new[]{1f,1f,1f,1f}),
        ("ammunition",new[]{"AmmoRack","VehicleComponent"},200,new[]{1f,.5f,1.25f,2f}),
        ("track left",new[]{"TrackAssembly","VehicleComponent"},400,new[]{1f,1f,1f,1f}),
        ("track right",new[]{"TrackAssembly","VehicleComponent"},400,new[]{1f,1f,.75f,1f}),
        ("main gun",new[]{"Cannon","VehicleComponent"},300,new[]{.5f,1f,1f,1f}),
        ("engine",new[]{"CombustionEngine","VehicleComponent"},500,new[]{1f,1f,1f,1f}),
        ("radio (on neither list)",new[]{"Radio","VehicleComponent"},50,new[]{1f,1f,1f,1f}),
        ("pool without an owner",null,80,new[]{1f,1f,1f,1f}),
    };
    private static (List<Pool?> List,List<string[]?> Owners,Register Register) Fresh(float start=1)
    {
        var r=new Register();var list=new List<Pool?>();var owners=new List<string[]?>();
        foreach(var v in Vehicle){list.Add(new Pool{Max=v.Max,Current=v.Max*start,P=v.Mods[0],B=v.Mods[1],Pr=v.Mods[2],T=v.Mods[3],Owner=r});owners.Add(v.Types);}
        return (list,owners,r);
    }

    // ---------------- the fragments, as FEAT78 (reference semantics, as the boxed IL2CPP Fragment) ----------------
    internal sealed class Frag{internal ushort Flags;internal float Speed;internal int Parent=-1;internal Frag Copy()=>new(){Flags=Flags,Speed=Speed,Parent=Parent};}
    private readonly struct Frags:IHeshFragments
    {
        internal readonly List<Frag> List;internal Frags(List<Frag> list)=>List=list;
        public int Count=>List.Count;
        public ushort Flags(int index)=>List[index].Flags;
        public int Parent(int index)=>List[index].Parent;
        public void SetFlags(int index,ushort flags)=>List[index].Flags=flags;
        public void Stop(int index){List[index].Speed=0;List[index].Flags|=HeshRules.Killed;}
    }
    private const ushort O=HeshRules.Original,K=HeshRules.Killed,D=HeshRules.Deflected,E=HeshRules.Embedded,P=HeshRules.Penetrated,PC=HeshRules.Precomputed,GS=HeshRules.GenerateSpalling,ModelExit=2;
    private enum Native{Perforated,Ricochet,Embedded}
    /// <summary>One native block (LOS-MAP section 1): B = budget x apModifier / rha x [cos^(e-1)(1-om)+om]; perforation if B &gt; path, ricochet if B &lt;= d and at least 10 degrees, otherwise embedded.</summary>
    private static (Native Outcome,double Effective,double Path) Block(double budget,double apModifier,double rha,double exponent,double angle,double normalMm,double dMm,double overmatch)
    {
        var b=Feat02290NativeDivisorTests.NativeBudget(budget,apModifier,rha,exponent,angle,overmatch); // 0.22.90 adapted: the native order B=b*ap/rha; om<1: (B-c*B)*om+c*B (VANISH-MAP section 2)
        var path=ProtectionFactorTable.PathMm(normalMm,angle);
        var used=b>path?path:b; // 0.22.90 adapted: the native charge (1D78360)
        return (b/path>1?Native.Perforated:!(used>dMm)&&angle>=HeshSettings.NativeRicochetDegrees?Native.Ricochet:Native.Embedded,b,path); // 0.22.90 adapted: native tests (perforation B/path>1; embedded when used>d or under 10 degrees)
    }
    private static List<Frag> Outcome(Native outcome,double angle,bool gouge,float speed=4000)
    {
        var list=new List<Frag>{new(){Flags=O,Speed=speed}};
        switch(outcome)
        {
            case Native.Perforated:list[0].Flags|=P;list.Add(new(){Flags=O|PC|GS,Speed=speed*.3f,Parent=0});break;
            case Native.Ricochet:
                list[0].Flags|=D;
                if(gouge)list.Add(new(){Flags=O|D|E,Speed=speed*.2f,Parent=0});
                list.Add(new(){Flags=O|GS,Speed=(float)(speed*Math.Sqrt(Math.Max(0,.9-.7*Math.Cos(angle*Math.PI/180)))),Parent=0});break;
            default:list[0].Flags|=E;list.Add(new(){Flags=O|ModelExit|E,Speed=0,Parent=0});break;
        }
        return list;
    }
    private static string F(double v,string format="0.###")=>v.ToString(format,CultureInfo.InvariantCulture);
    /// <summary>The native speed of a budget (inverse of the native penetration law, DynamicEraTests): v = (d_cm)^0.75 K (P/100)^(1/1.43) / sqrt(m).</summary>
    private static double Speed(double dMm,double mass,double budgetMm,double k)=>Math.Pow(dMm*.1,.75)*k*Math.Pow(budgetMm*.01,1/1.43)/Math.Sqrt(mass);
    private static CombatReportShot Shot(int number,IReadOnlyList<CombatReportEvent> events,double entry)=>new(number,"Outgoing","HE",650,events.ToArray(),0,entry,"Target tank",4,3,"Own tank");

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT86: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),parent=Fix02228Tests.SessionRoot();Directory.CreateDirectory(output);
        var profile=ShellProfiles.Parse(ReleaseProfiles.Defaults()).First(p=>p.Id=="he");
        var on=HeSettings.Default;
        var calibres=new[]{76d,100,122,152,203};

        // ================= 1. the table: perforation, shock, fragments =================
        var table=new StringBuilder("calibre mm\tperforation budget mm RHA\tinterior shock %\tfragments (game count)\tfragment mass kg (steel)\tkg per fragment\tfragment speed m/s\tHESH scab kg (0.22.78)\tblast P\tradius m\n");
        foreach(var c in calibres)
        {
            var fr=HeRules.Fragments(c);var hesh=HeshRules.Scab(c,1,HeshSettings.Default);var p=ShellBalance.BlastPower(profile,c);var speed=ShellBalance.FragmentSpeed("he",c);
            table.AppendLine($"{c:0}\t{F(HeRules.PerforationMm(on,c),"0.0")}\t{F(HeRules.Shock(on,c)*100,"0.0#")}\t{HeshRules.NativeCount(fr.Count)}\t{F(fr.MassKg,"0.00")}\t{F(fr.MassPerFragmentKg,"0.000")}\t{F(speed,"0")}-{F(speed*1.25,"0")}\t{F(hesh.MassKg,"0.0")}\t{F(p,"0.0")}\t{F(Math.Sqrt(p/10),"0.00")}");
            Check(HeshRules.NativeCount(fr.Count)<=32&&fr.MassKg<hesh.MassKg&&fr.MassKg>hesh.MassKg/5,$"{c:0} mm: {HeshRules.NativeCount(fr.Count)} fragments, {F(fr.MassKg,"0.0")} kg (HESH scab {F(hesh.MassKg,"0.0")} kg): the same order, smaller, at most 32");
        }
        Check(Math.Abs(HeRules.PerforationMm(on,100)-20)<1e-12&&Math.Abs(HeRules.PerforationMm(on,152)-46.208)<1e-9&&Math.Abs(HeRules.PerforationMm(on,76)-11.552)<1e-9,"perforation: 20 mm at 100 mm, 46.2 mm at 152 mm (brief: about 45), x (calibre/100)^2");
        Check(Math.Abs(HeRules.Shock(on,100)-.04)<1e-12&&Math.Abs(HeRules.Shock(on,152)-.04*1.52*1.52)<1e-12&&HeRules.Shock(on,300)==.25&&HeRules.Shock(on,250)==.25,"shock: 4 % at 100 mm, x (calibre/100)^2, at most 25 %");
        Check(calibres.Zip(calibres.Skip(1)).All(p=>HeRules.PerforationMm(on,p.First)<HeRules.PerforationMm(on,p.Second)&&HeRules.Shock(on,p.First)<HeRules.Shock(on,p.Second)),"perforation and shock grow with calibre");

        // ================= 2. the blast filter on rebuilt pools =================
        var blast=new StringBuilder("case\tpool\tclass\tfactor\tgame value\tvalue passed\tnative current\tfiltered current\tbit-identical to the game\n");
        var pressure=new[]{0f,0f,1f,0f};
        var power=(float)ShellBalance.BlastPower(profile,152);var value=-Math.Min(power,power/(0f*0f));
        var shock152=HeRules.Shock(on,152);
        (List<Pool?> L,HeBlastPlan Plan,List<Pool?> N,Pools A) Blast(bool perforated,double shock,float v,HeSettings s,int throwTypes=-1,int throwApply=-1,float start=1)
        {
            var native=Fresh(start);NativeLoop(native.List,v,pressure);
            var ours=Fresh(start);var adapter=new Pools(ours.List,ours.Owners,pressure){ThrowTypesAt=throwTypes,ThrowApplyAt=throwApply};
            var plan=HeRules.Run(adapter,v,perforated,shock,s);
            return (ours.List,plan,native.List,adapter);
        }
        void Rows(string name,(List<Pool?> L,HeBlastPlan Plan,List<Pool?> N,Pools A) r,float v)
        {
            for(int i=0;i<Vehicle.Length;i++)
                blast.AppendLine($"{name}\t{Vehicle[i].Name}\t{(i<r.Plan.Classes.Length?r.Plan.Classes[i].ToString():"-")}\t{(i<r.Plan.Factors.Length?F(r.Plan.Factors[i],"0.####"):"-")}\t{F(v,"0.###")}\t{(i<r.Plan.Factors.Length?F(r.Plan.Factors[i]==1?v:(float)(v*r.Plan.Factors[i]),"0.###"):"-")}\t{F(r.N[i]!.Current,"0.###")}\t{F(r.L[i]!.Current,"0.###")}\t{r.N[i]!.Bits==r.L[i]!.Bits}");
        }
        {
            var r=Blast(false,shock152,value,on);Rows("152 mm, no perforation",r,value);
            Check(r.Plan.Outcome==HeBlastOutcome.Applied&&r.Plan.Applied==Vehicle.Length,"no perforation: the whole plan is applied, pool by pool in the game's order");
            var exterior=new[]{3,4,5};var interior=new[]{0,1,2,6};var unknown=new[]{7,8};
            Check(exterior.All(i=>r.L[i]!.Bits==r.N[i]!.Bits),"no perforation: tracks and gun are bit-identical to the game's loop (current, accumulated modifiers, change flag)");
            Check(unknown.All(i=>r.L[i]!.Bits==r.N[i]!.Bits)&&r.Plan.Classes[7]==HePoolClass.Unknown&&r.Plan.Classes[8]==HePoolClass.Unknown,"no perforation: a module on neither list and a pool without an owner keep the game's value, bit for bit");
            foreach(var i in interior)
            {
                var expect=Fresh().List[i]!;expect.HealthDelta((float)(value*shock152),0,0,1,0);
                var nativeLoss=Vehicle[i].Max-r.N[i]!.Current;var ourLoss=Vehicle[i].Max-r.L[i]!.Current;var raw=-value*Vehicle[i].Mods[2]; // the game's value before it is clamped at the remaining health
                Check(r.L[i]!.Bits==expect.Bits&&Math.Abs(ourLoss-Math.Min(Vehicle[i].Max,raw*shock152))<1e-3,$"no perforation: {Vehicle[i].Name} takes the game's value x {HeRules.Percent(shock152)} % ({F(ourLoss,"0.##")} instead of the game's {F(nativeLoss,"0.##")}; unclamped game value {F(raw,"0.##")})");
            }
            Check(r.Plan.Shocked&&HeRules.OutsideText(r.Plan.Shock).Contains(" 9.2 % ",StringComparison.Ordinal),"no perforation: the report line names the shock of 9.2 % for 152 mm");
        }
        {
            var r=Blast(true,shock152,value,on);Rows("152 mm, perforated",r,value);
            Check(r.Plan.Outcome==HeBlastOutcome.Applied&&Enumerable.Range(0,Vehicle.Length).All(i=>r.L[i]!.Bits==r.N[i]!.Bits)&&r.Plan.Factors.All(f=>f==1)&&!r.Plan.Shocked,"perforated: every pool takes the game's value, bit-identical to the game's loop");
        }
        {
            // a partly damaged vehicle and a blast at distance (d = 1.7 m): the clamping at the remaining health is the game's own
            var v=-Math.Min(power,power/(1.7f*1.7f));var r=Blast(false,shock152,v,on,start:.03f);Rows("152 mm at 1.7 m, 3 % health left",r,v);
            Check(new[]{3,4,5,7,8}.All(i=>r.L[i]!.Bits==r.N[i]!.Bits)&&new[]{0,1,2,6}.All(i=>r.L[i]!.Current>=r.N[i]!.Current),"at a distance and near zero health: exterior and unknown bit-identical, interior never worse than the game's");
        }
        {
            // the list as the game holds it, with the pools in another order and modifiers that are not all 1: same result per pool
            var native=Fresh();native.List.Reverse();native.Owners.Reverse();NativeLoop(native.List,value,new[]{.25f,.5f,1f,2f});
            var ours=Fresh();ours.List.Reverse();ours.Owners.Reverse();var plan=HeRules.Run(new Pools(ours.List,ours.Owners,new[]{.25f,.5f,1f,2f}),value,true,shock152,on);
            Check(plan.Outcome==HeBlastOutcome.Applied&&Enumerable.Range(0,ours.List.Count).All(i=>ours.List[i]!.Bits==native.List[i]!.Bits),"perforated, other order and mixed modifiers: still bit-identical (the sum of pool x blast modifiers is the game's own)");
        }
        {
            // the HE scope: proxy record → scope → finalizer; a ground hit and a mine after an HE shot
            HeScopeState.End();
            Check(HeScopeState.Active==null,"no scope outside an HE detonation (mines, mission zones, fragments): the filter's first test sends them to the game's loop");
            var regA=new IntPtr(0x1000);var regB=new IntPtr(0x2000);
            HeScopeState.ProxyStarted(41,12.5f);HeScopeState.Perforated(new HePerforation(regA,15,15,0,"7:0:2:3"));HeScopeState.LastLayer("7:0:2:3");
            Check(ReferenceEquals(HeScopeState.ProxyStarted(41,12.5f),HeScopeState.Proxy)&&HeScopeState.ProxyImpact(41,12.5f)&&!HeScopeState.ProxyImpact(41,12.75f),"the HE call of the same projectile keeps the record of its AP part; another projectile does not match");
            var scope=HeScopeState.Begin(41,12.5f,1,2,3,40,100,"guid",false,on);
            Check(ReferenceEquals(HeScopeState.Active,scope)&&scope.IsPerforated(regA)&&!scope.IsPerforated(regB)&&scope.LastHitId=="7:0:2:3"&&Math.Abs(scope.Shock-.04)<1e-12,"scope: knalpunt, P, calibre, GUID; perforated only for the vehicle whose armour the part perforated");
            HeScopeState.End();
            Check(HeScopeState.Active==null&&HeScopeState.Proxy==null,"finalizer: the scope and the record are gone");
            // a mine right after: outside the scope the game's loop runs (managed copy): identical to an untouched loop
            var mine=Fresh();var mineRef=Fresh();
            if(HeScopeState.Active is null)NativeLoop(mine.List,-25f,pressure);else HeRules.Run(new Pools(mine.List,mine.Owners,pressure),-25f,false,.5,on);
            NativeLoop(mineRef.List,-25f,pressure);
            Check(Enumerable.Range(0,mine.List.Count).All(i=>mine.List[i]!.Bits==mineRef.List[i]!.Bits),"a mine (and a mission zone) right after an HE shot: the game's own loop, bit for bit (the scope does not leak)");
            HeScopeState.ProxyStarted(42,13f);HeScopeState.Perforated(new HePerforation(regA,15,15,0,null));
            var ground=HeScopeState.Begin(42,13f,0,0,0,40,100,"guid",true,on);
            Check(!ground.IsPerforated(regA)&&ground.Perforated.Count==0&&ground.LastHitId==null,"ground hit (HitPhysicsObject): never perforated, even with a record of the same projectile");
            HeScopeState.End();
            HeScopeState.ProxyStarted(43,14f);HeScopeState.Perforated(new HePerforation(regA,15,15,0,null));
            var other=HeScopeState.Begin(44,14f,0,0,0,40,100,"guid",false,on);
            Check(!other.IsPerforated(regA),"the HE call of another projectile never inherits a perforation");
            HeScopeState.End();
            var bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100000;i++){if(HeScopeState.Active is not null)throw new InvalidOperationException();_=HeScopeState.ProxyImpact(i,i);}bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Check(bytes==0,$"outside the HE scope the test of the filter allocates nothing ({bytes} bytes for 100000 calls)");
        }

        // ================= 3. the perforating part =================
        var cases=new StringBuilder("case\tcalibre mm\tplate\tangle deg\tbudget B mm\td mm\tmass kg\tspeed m/s\tovermatch\teffective B mm\tpath mm\tnative outcome\t0.22.86 outcome\tfragments\tflying originals after\n");
        var exponent=1.33; // the game's own obliquity exponent of the AP part (ShellObliquity leaves HE native)
        string Case(string name,double calibre,double normalMm,double angle,bool gouge=false)
        {
            var g=ShellBallistics.CoreGeometry(calibre,profile.Settings);var d=g.Diameter*1000;var mass=g.Mass;var budget=HeRules.PerforationMm(on,calibre);
            var speed=Speed(d,mass,budget,ShellBallistics.Calculate(calibre,700,1800,profile.Settings).PenetratorConstant);
            var om=ProtectionFactorTable.Overmatch(mass,speed,d/1000,normalMm/1000/Math.Cos(angle*Math.PI/180),angle);
            var block=Block(budget,1,1,exponent,angle,normalMm,d,om);
            var f=Outcome(block.Outcome,angle,gouge);string outcome;int fragments=0;
            // the part's call ends (RuntimeArmourResponses.End, HE step)
            var r=HeRules.NoPerforation(new Frags(f),0,1,angle,on.MinAngleDegrees);HeRules.StopContinuation(new Frags(f),0,1);
            if(block.Outcome==Native.Perforated)
            {
                // the continuation's own call: burst (fragments), the body consumed, then the air copy the game makes from it, then the finalizer
                Check(HeRules.FragmentsApply(f[1].Flags),$"{name}: the burst of the continuation (0x{f[1].Flags:X}) makes the HE fragments");
                fragments=HeshRules.NativeCount(HeRules.Fragments(calibre).Count);
                new Frags(f).Stop(1);
                f.Add(new(){Flags=(ushort)((f[1].Flags&O)|PC),Speed=f[1].Speed,Parent=1});
                HeRules.StopContinuation(new Frags(f),1,2);
                outcome="perforated";
            }
            else outcome=(f[0].Flags&E)!=0?"stopped":(f[0].Flags&D)!=0?"ricochet (graze)":"?";
            f[0].Speed=0; // RuntimeArmourResponses.HeEnd: the part itself is at speed 0 after its call (the game pays fragment hits with ½mv² of the final speed)
            var flying=f.Skip(1).Count(x=>(x.Flags&O)!=0&&(x.Flags&K)==0&&x.Speed>0);
            Check(f.All(x=>(x.Flags&O)==0||x.Speed==0),$"{name}: no original of the part keeps a speed, so the part deals no kinetic damage of its own (½mv² = 0)");
            cases.AppendLine($"{name}\t{calibre:0}\t{F(normalMm)} mm RHA\t{F(angle)}\t{F(budget,"0.0")}\t{F(d,"0.0")}\t{F(mass,"0.00")}\t{F(speed,"0")}\t{F(om,"0.000")}\t{F(block.Effective,"0.00")}\t{F(block.Path,"0.00")}\t{block.Outcome}\t{outcome}\t{fragments}\t{flying}");
            Check(flying==0,$"{name}: no original flies on after the part's calls ({outcome})");
            return outcome;
        }
        Check(Case("100 mm HE on 15 mm RHA at 0 deg",100,15,0)=="perforated","100 mm HE on 15 mm RHA: perforation, fragments behind the plate");
        Check(Case("152 mm HE on 40 mm RHA at 0 deg",152,40,0)=="perforated","152 mm HE on 40 mm RHA at 0 deg: perforation");
        var edge=Case("152 mm HE on 40 mm RHA at 30 deg (46.2 mm path)",152,40,30);
        Check(edge is "perforated" or "stopped","152 mm on 40 mm at 30 deg (path 46.19 mm against 46.21 mm): the edge case, here "+edge);
        Check(Case("152 mm HE on 40 mm RHA at 30.1 deg",152,40,30.1)=="stopped"&&Case("152 mm HE on 40 mm RHA at 35 deg",152,40,35)=="stopped","152 mm on 40 mm just past the edge (30.1 deg, 35 deg): no perforation, stopped (the limit is about 30.05 deg)");
        foreach(var a in new[]{0d,30,60,74.9})Check(Case($"152 mm HE on 80 mm RHA at {F(a,"0.#")} deg",152,80,a,gouge:a>=30)=="stopped",$"152 mm on 80 mm at {F(a,"0.#")} deg: no perforation and no ricochet (stopped)");
        foreach(var a in new[]{75d,80})Check(Case($"152 mm HE on 80 mm RHA at {F(a,"0")} deg",152,80,a)=="ricochet (graze)",$"152 mm on 80 mm at {F(a,"0")} deg: the ricochet stays (from the limit on), nothing flies on");
        {
            // the native ricochet rule for the part: B <= d always (budget 0.2-0.46 calibre against d 0.9 calibre), so every non-perforating hit from 10 degrees is a native ricochet
            var ok=calibres.All(c=>HeRules.PerforationMm(on,c)<ShellBallistics.CoreGeometry(c,profile.Settings).Diameter*1000);
            Check(ok&&Math.Abs(profile.Settings.DiameterRatio-.9)<1e-12,"the native ricochet rule: B <= d for every calibre (d = 0.9 calibre), so without perforation from 10 degrees the game makes a ricochet; below 75 degrees the part stops instead");
            var grid=0;var wrong=0;
            for(int flags=0;flags<512;flags++)foreach(var angle in new[]{0d,9.99,10,44,74.9,75,80,double.NaN})
            {
                var f=new List<Frag>{new(){Flags=(ushort)flags,Speed=500},new(){Flags=O|GS,Speed=200,Parent=0},new(){Flags=0,Speed=300,Parent=0}};grid++;
                var r=HeRules.NoPerforation(new Frags(f),0,1,angle,75);
                var deflected=(flags&O)!=0&&(flags&D)!=0&&(flags&(K|E|P))==0;
                var expected=deflected?(angle<75?"converted":"grazed"):"none";
                var got=r.Converted?"converted":r.Grazed?"grazed":"none";
                if(got!=expected||deflected&&(f[1].Speed!=0||(f[1].Flags&K)==0)||f[2].Speed!=300||!deflected&&(f[1].Speed!=200))wrong++;
            }
            Check(wrong==0,$"the rule converts exactly the native ricochet below 75 deg, stops the original children from 75 deg (and without an angle), and leaves fragments and every other outcome ({grid} states)");
            var none=new List<Frag>{new(){Flags=O|P,Speed=500}};var view=new Frags(none);HeRules.NoPerforation(view,0,1,30,75);HeRules.StopContinuation(view,0,1);
            var bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100000;i++){HeRules.NoPerforation(view,0,1,30,75);HeRules.StopContinuation(view,0,1);_=HeRules.FragmentsApply((ushort)i);}bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Check(bytes==0,$"the per-call decisions of the part allocate nothing for a fragment they do not touch ({bytes} bytes for 100000 calls)");
            Check(!HeRules.FragmentsApply(O|GS)&&!HeRules.FragmentsApply(PC|GS)&&HeRules.FragmentsApply(O|PC|GS)&&HeRules.FragmentsApply(O|PC),"fragments only from the burst of a perforation continuation (0x181), never from a ricochet child (0x101) or spall");
        }

        // ================= 4. the switches =================
        string Read(string f)=>File.ReadAllText(Path.Combine(root,f)).Replace("\r\n","\n");
        var selection=Read("RuntimeShellSelection.cs");var feed=Read("RuntimeDamageFeed.cs");var spall=Read("RuntimeSpall.cs");var responses=Read("RuntimeArmourResponses.cs");var hit=Read("HitPanelModel.cs");
        {
            var launch="                if(profile.Behavior==\"he\"&&RuntimeSpall.He.Perforation)functions.Add(new ArmouredPiercingProjectileFunctionDefinition(dart.PenetratorConstant)); // 0.22.86 HE\n                if(profile.Behavior==\"he\") functions.Add(new HighExplosiveProjectileFunctionDefinition(";
            Check(selection.Contains(launch,StringComparison.Ordinal),"the HE type gets [AP part, HE] in that order, the AP part only with perforation on (off: [HE], the list of 0.22.84)");
            var scope=selection[selection.IndexOf("private static void HeScope(",StringComparison.Ordinal)..];scope=scope[..scope.IndexOf("// 0.22.86 HE end",StringComparison.Ordinal)];
            Check(Regex.IsMatch(scope,@"try\s*\{\s*if\(!RuntimeSpall\.He\.BlastFilter\)return;"),"blast filter off: no scope is ever set, so every HealthDelta runs the game's loop");
            Check(responses.Contains("if(context.Behavior==\"he\"&&context.LiveImpact&&RuntimeSpall.He.Perforation)active.He=true;",StringComparison.Ordinal),"the part's rules in the armour finalizer only with perforation on");
            Check(selection.Contains("private static float HePenetration(double calibreMm)=>RuntimeSpall.He.Perforation?(float)HeRules.PerforationMm(RuntimeSpall.He,calibreMm):0;",StringComparison.Ordinal),"the cannon's penetration value of HE: the part's budget, 0 with perforation off (as 0.22.84)");
            // only blastFilter: the type keeps [HE]; only perforation: the game's value on every pool (no scope)
            var onlyBlast=on with{Perforation=false};var onlyPerf=on with{BlastFilter=false};
            Check(!onlyBlast.Perforation&&onlyBlast.BlastFilter&&onlyPerf.Perforation&&!onlyPerf.BlastFilter,"each part has its own switch");
            var native=Fresh();NativeLoop(native.List,value,pressure);var noScope=Fresh();
            if(onlyPerf.BlastFilter)HeRules.Run(new Pools(noScope.List,noScope.Owners,pressure),value,false,shock152,onlyPerf);else NativeLoop(noScope.List,value,pressure);
            Check(Enumerable.Range(0,native.List.Count).All(i=>native.List[i]!.Bits==noScope.List[i]!.Bits),"only perforation: the game's value on all pools (bit for bit)");
        }
        // no hook in the marked 0.22.86 insertions (herpak: the byte comparison with src-0.22.84 is removed)
        foreach(var file in Marked)
        {
            var now=File.ReadAllBytes(Path.Combine(root,file));
            static int Hooks(string s)=>s.Split('\n').Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal));
            Check(Hooks(Encoding.UTF8.GetString(now))==Hooks(Encoding.UTF8.GetString(Un86Bytes(Path.Combine(root,file)))),$"{file}: no [HarmonyPatch in the marked HE insertions");
        }
        Check(Replaced.All(r=>(r.File=="RuntimeShellSelection.cs"?selection:feed).Contains(r.New.Replace("\r","")+ReplacedMark,StringComparison.Ordinal)),"every declared replaced line is in the source exactly once as declared");
        // the hot paths: one cheap test first, outside the HE scope nothing else
        {
            var filter=feed[feed.IndexOf("internal static bool Filter(",StringComparison.Ordinal)..];
            Check(Regex.IsMatch(filter,@"^internal static bool Filter\(VehicleHealthRegister register,float value,DamageModelDamageModifiers modifiers\)\s*\{\s*if\(HeScopeState\.Active is not \{\} scope\)return true;"),"HealthDelta prefix: the first statement of the filter tests the thread-local scope and returns to the game's loop");
            Check(feed.Contains("{__state=RuntimeDamageFeed.BeginDirectDamage(__instance);return HeBlast.Filter(__instance,__0,__1);}",StringComparison.Ordinal),"HealthDelta prefix: the damage-feed baseline first, then the filter (true = the game's loop)");
            Check(!feed.Contains("BeginDirectOne(VehicleHealthRegister __instance,float",StringComparison.Ordinal)&&feed.Contains("private static void BeginDirectOne(VehicleHealthRegister __instance,out RuntimeDamageFeed.HealthScope __state)=>__state=RuntimeDamageFeed.BeginDirectDamage(__instance);",StringComparison.Ordinal),"the per-pool overload (fragments) is untouched");
            var tag=feed[feed.IndexOf("internal static string Tag(VehicleComponent owner,string label)",StringComparison.Ordinal)..];
            Check(Regex.IsMatch(tag,@"^internal static string Tag\(VehicleComponent owner,string label\)\s*\{\s*if\(HeScopeState\.Active\?\.Tags is not \{\} tags\|\|!tags\.TryGetValue\(owner\.Pointer,out var tag\)\)return label;"),"damage line: one test of the scope, the label unchanged outside it");
            Check(spall.Contains("        if(context is {Behavior:\"he\"}){HePenetrator(ref penetrator,context);return;}\n",StringComparison.Ordinal)&&spall.Contains("            if(context is {Behavior:\"he\"})return HeBurst(simulation,ref o,ref __state,context);\n",StringComparison.Ordinal),"Penetrate and burst prefixes: one property test, HE only");
            Check(responses.Contains("        if(scope is {He:true}&&__result.Valid&&__result.MaterialIndex>=0)HeBlock(scope,__result,__0,__1,__5);\n",StringComparison.Ordinal)&&responses.Contains("            if(current is {He:true})HeEnd(current);\n",StringComparison.Ordinal),"block postfix and fragment finalizer: one property test, HE only");
            Check(hit.Contains("        if(shown==StoppedSpall&&HeReportText.OutsideBlast(shot.Events))shown=StoppedBlast; // 0.22.86 HE\n",StringComparison.Ordinal),"hit panel: the word changes only for a shot with the HE line");
            var heEnd=responses[responses.IndexOf("private static void HeEnd(Scope scope)",StringComparison.Ordinal)..];heEnd=heEnd[..heEnd.IndexOf("// 0.22.86 HE end",StringComparison.Ordinal)];
            Check(heEnd.Contains("var self=scope.Sim.fragments[index];\n            if(self.speed!=0){self.speed=0;scope.Sim.fragments[index]=self;}",StringComparison.Ordinal)&&heEnd.IndexOf("HeRules.StopContinuation(",StringComparison.Ordinal)<heEnd.IndexOf("var self=",StringComparison.Ordinal),"the part deals no kinetic damage of its own: its speed is written to 0 after its call, written back to the array");
            // errors: once per session, the game's loop before any damage, never twice after
            Check(feed.Contains("if(plan.Outcome==HeBlastOutcome.Native){Warn(ref planWarned,",StringComparison.Ordinal)&&feed.Contains("return true;}",StringComparison.Ordinal)&&feed.Contains("if(plan.Outcome==HeBlastOutcome.Failed)Warn(ref applyWarned,",StringComparison.Ordinal)&&Regex.IsMatch(feed,@"catch\(Exception\)\{/\* tags, report line and log are optional \*/\}\s*return false;"),"filter: an error before applying returns true (the game's loop) with one warning; after the start it returns false (no double damage) with one warning");
            var configBlock=spall[spall.IndexOf("        // 0.22.86 HE begin\n        // The optional \"he\" object",StringComparison.Ordinal)..];configBlock=configBlock[..configBlock.IndexOf("// 0.22.86 HE end",StringComparison.Ordinal)];
            Check(!configBlock.Contains("Write",StringComparison.Ordinal)&&configBlock.Contains("He=HeRules.Read(heshFree,",StringComparison.Ordinal),"configuration: the user's spall.json is read, never written");
            Check(selection.Contains("private static void ExplosiveEnd(ImpactState __state){try{EndImpact(__state);}finally{HeBlast.End();}}",StringComparison.Ordinal)&&selection.Contains("private static void ExplosivePhysicsEnd(ImpactState __state){try{EndImpact(__state);}finally{HeBlast.End();}}",StringComparison.Ordinal),"the Explosive finalizers clear the scope in every case (finally)");
        }

        // ================= 7. errors: before applying the game's loop; after the start no double damage =================
        {
            var r=Blast(false,shock152,value,on,throwTypes:3);var untouched=Fresh();
            var handled=r.Plan.Outcome==HeBlastOutcome.Native&&r.Plan.Applied==0&&r.A.Calls==0&&Enumerable.Range(0,Vehicle.Length).All(i=>r.L[i]!.Bits==untouched.List[i]!.Bits);
            if(r.Plan.Outcome==HeBlastOutcome.Native)NativeLoop(r.L,value,pressure); // the prefix returns true: the game's loop runs once
            Check(handled&&Enumerable.Range(0,Vehicle.Length).All(i=>r.L[i]!.Bits==r.N[i]!.Bits),"an exception while planning: nothing applied, the game's loop runs once (the game's damage, bit for bit)");
            var r2=Blast(false,shock152,value,on,throwApply:3);var once=Blast(false,shock152,value,on);
            Check(r2.Plan.Outcome==HeBlastOutcome.Failed&&r2.Plan.Applied==3&&Enumerable.Range(0,3).All(i=>r2.L[i]!.Bits==once.L[i]!.Bits)&&Enumerable.Range(3,Vehicle.Length-3).All(i=>r2.L[i]!.Bits==untouched.List[i]!.Bits),"an exception half way: the pools before it are damaged once, the rest not at all, and the game's loop does not run (no double damage)");
            var nanPlan=HeRules.Run(new Pools(Fresh().List,Fresh().Owners,pressure),float.NaN,false,shock152,on);
            var nullList=Fresh();nullList.List[2]=null;var nullPlan=HeRules.Run(new Pools(nullList.List,nullList.Owners,pressure),value,false,shock152,on);
            Check(nanPlan.Outcome==HeBlastOutcome.Native&&nullPlan.Outcome==HeBlastOutcome.Native&&nullPlan.Applied==0,"a value that is not finite and a missing pool: the game's own loop (as it would have run)");
        }

        // ================= 6. the report: one shot per hit; report, hit panel, Dutch summary; forbidden words =================
        var reportLines=new StringBuilder();
        {
            // one hit is one shot: the AP part's shot is kept, the HE call continues it
            var recorded=new List<string>();var link=new HeShotLink<string>();int number=0;
            string Begin()=>"#"+(++number);
            var s1=Begin();var kept=link.Defer(s1,5,1.25f,false,true,recorded.Add);var resumed=link.Resume(5,1.25f,recorded.Add);recorded.Add(resumed??Begin());
            Check(kept&&resumed==s1&&recorded.SequenceEqual(new[]{"#1"})&&number==1,"an HE hit with the AP part: one shot (#1), recorded once");
            var s2=Begin();link.Defer(s2,6,2f,false,true,recorded.Add);
            var s3=Begin();link.Defer(s3,7,2.5f,false,true,recorded.Add);var r3=link.Resume(7,2.5f,recorded.Add);recorded.Add(r3!);
            Check(recorded.SequenceEqual(new[]{"#1","#2","#3"})&&r3==s3,"a kept shot that no HE call picked up is recorded once, at the next kept shot");
            var s4=Begin();link.Defer(s4,8,3f,false,true,recorded.Add);var r5=link.Resume(9,3.5f,recorded.Add);var s5=Begin();recorded.Add(s5);
            Check(r5==null&&recorded.SequenceEqual(new[]{"#1","#2","#3","#4","#5"}),"the HE call of another projectile records the kept shot first and opens its own");
            Check(!link.Defer("x",10,4f,true,true,recorded.Add)&&!link.Defer("y",11,4.5f,false,false,recorded.Add)&&link.Pending==null,"never kept: a shot of the HE call itself, and a shot of any other round (no AP part of HE)");
            Check(feed.Contains("                if(HeDefer(shot))return; // 0.22.86 HE\n",StringComparison.Ordinal)&&feed.Contains("private static bool HeDefer(Shot shot)=>(heLink??=new()).Defer(shot,shot.NativeId,shot.SpawnTime,shot.HeExplosive,HeScopeState.ProxyImpact(shot.NativeId,shot.SpawnTime),HeRecord);",StringComparison.Ordinal),"the AP finalizer keeps only the shot of an HE round's AP part; the number advances only in BeginImpact");

            var plate=new CombatLayerPart(12,"Plate #12","rha","Rolled homogeneous armour",80,30,true);
            var id="9:0:2:3";var layer=new CombatLayerInfo(id,new[]{plate},false);
            var shockText=HeRules.OutsideText(shock152);
            var outside=new List<CombatReportEvent>
            {
                new("layerStart","","Reached layer",46.2,null,id,layer),
                new("nativeStop","","Native stop in Plate #12 [Material: Rolled homogeneous armour]; no outgoing original core recorded.",46.2,null,id),
                new("materialNote","",shockText,HitId:id),
                new("interaction","","Crew member Gunner [shock 9.2 %] wounded",RelatedEntity:21),
                new("interaction","",CombatReportLanguage.Module("trackAssembly")+" [exterior] destroyed",RelatedEntity:31),
                new("interaction","",CombatReportLanguage.Module("cannon")+" [exterior] damaged",RelatedEntity:32),
                new("interaction","",CombatReportLanguage.Module("fuelTank")+" [shock 9.2 %] damaged",RelatedEntity:33),
            };
            var thin=new CombatLayerPart(12,"Plate #12","rha","Rolled homogeneous armour",15,0,true);var thinLayer=new CombatLayerInfo(id,new[]{thin},false);
            var perforated=new List<CombatReportEvent>
            {
                new("layerStart","","Reached layer",20,null,id,thinLayer),
                new("plateResidual","","Traversed Plate #12 [Material: Rolled homogeneous armour]; block exit includes native resistance and any listed extra reactions.",20,5,id),
                new("materialNote","",HeRules.PerforationText(15,15,0,32),HitId:id),
                new("interaction","","Crew member Driver [interior] killed",RelatedEntity:22),
                new("interaction","",CombatReportLanguage.Module("ammoRack")+" [interior] destroyed",RelatedEntity:34),
            };
            var exempt=new[]{"Plate #12","Rolled homogeneous armour","Target tank","Own tank","HE","Gunner","Driver"};
            foreach(var (name,events,entry,word) in new[]{("152 mm HE on 80 mm RHA at 30 deg, no perforation",outside,46.2,HitBlockBuilder.StoppedBlast),("100 mm HE on 15 mm RHA, perforation",perforated,20d,HitBlockBuilder.Penetrated)})
            {
                var shot=Shot(1,events,entry);
                var block=HitBlockBuilder.Build(shot);var view=CombatReportView.Build(new[]{shot})[0];var nl=CombatHitSummaryModel.Build(shot);var forensic=CombatForensics.Build(shot);
                var history=new CombatReportHistory();history.Begin();history.Add(shot);var html=CombatReportHtml.Render(history);
                var note=events.First(e=>e.Kind=="materialNote").Text;
                Check(block.Result==word,$"{name}: hit panel result {block.Result}");
                Check(block.Detail.Contains(HeReportText.Token(events)!,StringComparison.Ordinal)&&block.DamageShort.Contains('[')&&block.DamageExpanded.Contains("[",StringComparison.Ordinal),$"{name}: hit panel detail '{block.Detail}', damage '{block.DamageShort}'");
                Check(view.Layers.Length==1&&view.Layers[0].Reaction.Contains(note,StringComparison.Ordinal)&&(view.SummaryLine.Contains("Interior shock",StringComparison.Ordinal)||view.SummaryLine.Contains("Interior:",StringComparison.Ordinal))&&!view.SummaryLine.Contains("[",StringComparison.Ordinal),$"{name}: report row reaction '{view.Layers[0].Reaction}', summary '{view.SummaryLine}'");
                Check(html.Contains(System.Net.WebUtility.HtmlEncode(note),StringComparison.Ordinal)||html.Contains(note,StringComparison.Ordinal),$"{name}: the HTML report carries the line");
                var texts=new List<string>{block.Result,block.Detail,block.Values,block.DamageShort,block.DamageExpanded,nl.Header,nl.Detail,forensic.Outcome,forensic.Explanation,view.SummaryLine,view.Head,note};
                texts.AddRange(block.Breakdown);texts.AddRange(forensic.Findings);texts.AddRange(forensic.Advice);
                foreach(var l in view.Layers){texts.Add(l.Name);texts.Add(l.Material);texts.Add(l.Reaction);texts.Add(l.BeforeAfter);texts.Add(l.Note);}
                var doc=XDocument.Parse(html);foreach(var d in doc.Descendants("details").ToArray())d.Remove();foreach(var s in doc.Descendants("style").ToArray())s.Remove();
                texts.AddRange(doc.Root!.DescendantNodes().OfType<XText>().Select(t=>t.Value));
                var jargon=texts.Where(t=>!string.IsNullOrEmpty(t)).SelectMany(t=>CombatReportLanguage.JargonIn(t,exempt).Select(j=>j+" in: "+t)).ToArray();
                Check(jargon.Length==0,$"{name}: no game jargon in the visible texts"+(jargon.Length>0?": "+string.Join(" | ",jargon.Take(4)):""));
                reportLines.AppendLine("== "+name+" ==");
                reportLines.AppendLine("hit panel: "+block.Result+" · "+block.Values+" · "+block.Detail);
                reportLines.AppendLine("hit panel damage: "+block.DamageExpanded);
                reportLines.AppendLine("report: "+view.Head);
                reportLines.AppendLine("report row: "+view.Layers[0].Name+" | "+view.Layers[0].Material+" | "+view.Layers[0].Thickness+" | "+view.Layers[0].Angle+" | "+view.Layers[0].BeforeAfter+" | "+view.Layers[0].Reaction);
                reportLines.AppendLine("report summary: "+view.SummaryLine);
                reportLines.AppendLine("Dutch: "+nl.Header+" | "+nl.Detail);
                reportLines.AppendLine("forensic: "+forensic.Outcome+": "+forensic.Explanation);
            }
            Check(CombatReportLanguage.JargonIn(HeRules.OutsideText(.092)).Count==0&&CombatReportLanguage.JargonIn(HeRules.PerforationText(40,46,30,32)).Count==0&&new[]{"[exterior]","[interior]",HeRules.Tag(HePoolClass.Interior,false,.092)}.All(t=>CombatReportLanguage.JargonIn(t).Count==0),"the new wording itself is free of game jargon (\"modules and crew\", not \"components\")");
            Check(HeRules.Tag(HePoolClass.Exterior,false,.1)=="[exterior]"&&HeRules.Tag(HePoolClass.Interior,true,.1)=="[interior]"&&HeRules.Tag(HePoolClass.Interior,false,.0924)=="[shock 9.2 %]"&&HeRules.Tag(HePoolClass.Unknown,false,.1)=="","the tags per module: exterior / interior / shock; none for an unknown module");
            var plain=new[]{new CombatReportEvent("nativeStop","","Native stop in Plate #12; no outgoing original core recorded.",50,null,"1:0:1:2"),new CombatReportEvent("interaction","","Crew member Gunner wounded",RelatedEntity:2)};
            Check(HeReportText.Token(plain)==null&&!HeReportText.OutsideBlast(plain)&&HitBlockBuilder.Build(Shot(2,plain,50)).Result==HitBlockBuilder.StoppedSpall,"a shot without an HE line keeps its 0.22.84 hit panel (STOPPED · spall damage)");
            Check(CombatReportView.ResultChipWidth>=CombatReportView.ChipWidth(HitBlockBuilder.StoppedBlast),"the result chip is wide enough for STOPPED · blast damage");
        }

        // ================= 8. configuration =================
        var config=new StringBuilder("case\tblastFilter\tperforation\tperforationMmAt100\tperforationExponent\tminAngleDegrees\tinteriorShockAt100\tinteriorShockExponent\tinteriorShockMax\texteriorTypes\tinteriorTypes\tlog lines\tlog\n");
        {
            var baseJson=SpallBalance.ToJson(new SpallSettings{SpacedKinetic=SpacedKineticSettings.Default,FragmentRicochet=FragmentRicochetSettings.Default});
            string With(string he)=>baseJson.TrimEnd().TrimEnd('}')+",\n  \"he\": "+he+"\n}";
            (HeSettings S,List<string> Log,string Free) R(string json){var log=new List<string>();var s=HeRules.Read(json,log.Add,out var free);return (s,log,free);}
            void Row(string name,(HeSettings S,List<string> Log,string Free) r)=>config.AppendLine($"{name}\t{r.S.BlastFilter}\t{r.S.Perforation}\t{F(r.S.PerforationMmAt100)}\t{F(r.S.PerforationExponent)}\t{F(r.S.MinAngleDegrees)}\t{F(r.S.InteriorShockAt100)}\t{F(r.S.InteriorShockExponent)}\t{F(r.S.InteriorShockMax)}\t{string.Join(",",r.S.ExteriorTypes)}\t{string.Join(",",r.S.InteriorTypes)}\t{r.Log.Count}\t{string.Join(" | ",r.Log)}");
            var missing=R(baseJson);Row("key missing",missing);
            Check(missing.S==HeSettings.Default&&missing.Log.Count==0&&ReferenceEquals(missing.Free,baseJson),"missing key: the code defaults, the same text to the spall parser, no log");
            var empty=R(With("{}"));Row("empty object",empty);
            Check(empty.S==HeSettings.Default&&empty.Log.Count==0&&SpallBalance.ToJson(SpallBalance.Parse(empty.Free))==SpallBalance.ToJson(SpallBalance.Parse(baseJson)),"empty object: defaults; the spall settings parse exactly as without the key");
            var threw=false;try{SpallBalance.Parse(With("{}"));}catch(FormatException){threw=true;}
            Check(threw,"the strict spall parser itself would reject the key, so it is read and removed first");
            var partial=R(With("{\"blastFilter\": false}"));Row("one key",partial);
            Check(partial.S==HeSettings.Default with{BlastFilter=false}&&partial.Log.Count==0,"one key given: the others keep their defaults");
            foreach(var (json,check,name) in new (string,Func<HeSettings,bool>,string)[]{
                ("{\"perforationMmAt100\": 0}",s=>s.PerforationMmAt100==1,"perforationMmAt100 0 -> 1"),("{\"perforationMmAt100\": 900}",s=>s.PerforationMmAt100==500,"perforationMmAt100 900 -> 500"),
                ("{\"perforationExponent\": -1}",s=>s.PerforationExponent==0,"perforationExponent -1 -> 0"),("{\"minAngleDegrees\": 5}",s=>s.MinAngleDegrees==10,"minAngleDegrees 5 -> 10"),
                ("{\"minAngleDegrees\": 120}",s=>s.MinAngleDegrees==90,"minAngleDegrees 120 -> 90"),("{\"interiorShockAt100\": 2}",s=>s.InteriorShockAt100==1,"interiorShockAt100 2 -> 1"),
                ("{\"interiorShockMax\": -0.5}",s=>s.InteriorShockMax==0,"interiorShockMax -0.5 -> 0"),("{\"interiorShockExponent\": 7}",s=>s.InteriorShockExponent==4,"interiorShockExponent 7 -> 4"),
                ("{\"blastFilter\": \"yes\"}",s=>s.BlastFilter,"blastFilter text -> default"),("{\"perforation\": 1}",s=>s.Perforation,"perforation number -> default"),
                ("{\"interiorShockAt100\": \"0.04\"}",s=>s.InteriorShockAt100==.04,"interiorShockAt100 text -> default"),("{\"exteriorTypes\": \"Cannon\"}",s=>s.ExteriorTypes.SequenceEqual(HeSettings.DefaultExteriorTypes),"exteriorTypes not a list -> default list"),
                ("{\"interiorTypes\": [\"CrewSeat\", 5]}",s=>s.InteriorTypes.SequenceEqual(HeSettings.DefaultInteriorTypes),"interiorTypes with a number -> default list"),
                ("{\"interiorTypes\": [\"CrewSeat\",\"Cannon\"], \"exteriorTypes\": [\"Cannon\"]}",s=>s.InteriorTypes.SequenceEqual(new[]{"CrewSeat"})&&s.ExteriorTypes.SequenceEqual(new[]{"Cannon"}),"a name on both lists -> exterior"),
                ("{\"colour\": 1}",s=>s==HeSettings.Default,"unknown key -> ignored")})
            {
                var r=R(With(json));Row(name,r);
                Check(check(r.S)&&r.Log.Count==1&&r.Log[0].StartsWith("[HE] spall.json he: ",StringComparison.Ordinal),$"{name}: {r.Log.Count} log line(s) {string.Join(" ",r.Log)}");
            }
            var valid=R(With("{\"blastFilter\": false, \"perforation\": false, \"perforationMmAt100\": 25, \"perforationExponent\": 2.5, \"minAngleDegrees\": 70, \"interiorShockAt100\": 0.02, \"interiorShockExponent\": 1, \"interiorShockMax\": 0.5, \"exteriorTypes\": [\"Cannon\"], \"interiorTypes\": [\"CrewSeat\"]}"));Row("all keys valid",valid);
            Check(!valid.S.BlastFilter&&!valid.S.Perforation&&valid.S.PerforationMmAt100==25&&valid.S.PerforationExponent==2.5&&valid.S.MinAngleDegrees==70&&valid.S.InteriorShockAt100==.02&&valid.S.InteriorShockExponent==1&&valid.S.InteriorShockMax==.5&&valid.Log.Count==0,"valid values are read as given");
            var many=R(With("{\"minAngleDegrees\": 3, \"blastFilter\": \"no\", \"interiorShockMax\": 9, \"other\": 1}"));Row("several problems",many);
            Check(many.Log.Count==1&&many.Log[0].Contains("unknown key other",StringComparison.Ordinal)&&many.Log[0].Contains("blastFilter is not true or false",StringComparison.Ordinal)&&many.S.MinAngleDegrees==10&&many.S.InteriorShockMax==1,"several problems: each clamped or defaulted, together one log line");
            var notObject=R(With("5"));Row("not an object",notObject);
            Check(notObject.S==HeSettings.Default&&notObject.Log.Count==1,"an he value that is not an object: defaults, one log line");
            // with "hesh" and "he" together: both are read and both left out of the strict parser (the order of the runtime: HESH, then HE)
            var both=baseJson.TrimEnd().TrimEnd('}')+",\n  \"hesh\": {\"minAngleDegrees\": 60},\n  \"he\": {\"minAngleDegrees\": 70}\n}";
            var hesh=HeshRules.Read(both,_=>{},out var heshFree);var he=HeRules.Read(heshFree,_=>{},out var heFree);
            Check(hesh.MinAngleDegrees==60&&he.MinAngleDegrees==70&&SpallBalance.ToJson(SpallBalance.Parse(heFree))==SpallBalance.ToJson(SpallBalance.Parse(baseJson)),"hesh and he together: both read, the spall settings as without them");
            var broken="{\"coneMultiplier\": ";var b=R(broken);
            Check(b.S==HeSettings.Default&&ReferenceEquals(b.Free,broken)&&b.Log.Count==0,"malformed JSON: defaults, the text goes unchanged to the spall parser (which reports it as before)");
            Check(HeRules.Describe(on).Contains("blast filter on",StringComparison.Ordinal)&&HeRules.Describe(on).Contains("perforating part on",StringComparison.Ordinal),"diagnostic description of the active settings");
        }

        File.WriteAllText(Path.Combine(output,"HE-TABLE.tsv"),table.ToString());
        File.WriteAllText(Path.Combine(output,"HE-BLAST-CASES.tsv"),blast.ToString());
        File.WriteAllText(Path.Combine(output,"HE-PERFORATION-CASES.tsv"),cases.ToString());
        File.WriteAllText(Path.Combine(output,"HE-REPORT-LINES.txt"),reportLines.ToString());
        File.WriteAllText(Path.Combine(output,"HE-CONFIG-CASES.tsv"),config.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT86-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures,
            perforation100Mm=HeRules.PerforationMm(on,100),perforation152Mm=HeRules.PerforationMm(on,152),shock100=HeRules.Shock(on,100),shock152=HeRules.Shock(on,152),edge30Deg=edge,
            exteriorTypes=on.ExteriorTypes,interiorTypes=on.InteriorTypes},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT86 PASS: {checks} checks, {failures} failures");
    }
}
