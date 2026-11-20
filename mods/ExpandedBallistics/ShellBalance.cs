namespace SprocketExpandedBallistics;

// Gameplay scaling around a 100 mm reference gun; not real explosive/jet physics.
internal static class ShellBalance
{
    internal static double Ratio(double calibreMm)
    {
        if(!double.IsFinite(calibreMm)||calibreMm<=0) throw new ArgumentOutOfRangeException(nameof(calibreMm));
        return calibreMm/100;
    }
    internal static bool IsAtgm(string? behavior) => behavior is "atgm" or "atgm_gun";
    internal static bool FlightEnabled(ShellProfile? p)=>p!=null&&(p.Flight is {} f?f.Propulsion=="rocket"||f.Guidance!="none":IsAtgm(p.Behavior));
    internal static bool Powered(ShellProfile p)=>p.Flight?.Propulsion=="rocket"||(p.Flight==null&&IsAtgm(p.Behavior));
    internal static string Carrier(ShellProfile p)=>p.Flight?.Carrier??(p.Behavior=="atgm_gun"?"gunLaunch":p.Behavior=="atgm"?"launcher":p.Behavior=="apfsds"?"sabot":"fullBore");
    internal static IEnumerable<string> RequiredTechnologyIds(ShellProfile p)
    {
        if(p.Flight==null)yield return "expandedBallistics_"+p.Behavior;
        else
        {
            yield return "expandedBallistics_"+ImpactBehavior(p);
            if(Carrier(p)=="sabot")yield return "expandedBallistics_apfsds";
            if(FlightEnabled(p)||Carrier(p) is "launcher" or "gunLaunch")yield return Carrier(p)=="gunLaunch"?"expandedBallistics_atgm_gun":"expandedBallistics_atgm";
        }
        foreach(var id in p.NativeTechnologyIds??Array.Empty<string>())yield return id;
    }
    internal static string ImpactBehavior(ShellProfile p) => IsAtgm(p.Behavior) ? "heat" : p.Behavior;
    internal static double ChemicalPenetration(ShellProfile p,double calibreMm) => Math.Clamp(p.ChemicalPenetrationMm*Ratio(calibreMm)*100/p.ReferenceCalibreMm,1,2000);
    internal static bool SuppressPayloadBurst(string behavior,bool original,bool alreadyBurst) => alreadyBurst && (behavior!="heat" || !original);
    internal const int MaxHeatPayloadBursts=3;
    internal static double HeatBurstTarget(double baseTarget,int priorBursts)
    {
        if(!double.IsFinite(baseTarget)||baseTarget<=0||priorBursts<0||priorBursts>=MaxHeatPayloadBursts)return 0;
        var scale=priorBursts switch{0=>1.0,1=>0.5,_=>0.25};
        var cap=priorBursts switch{0=>32.0,1=>16.0,_=>8.0};
        return Math.Clamp(Math.Round(baseTarget*scale),4,cap);
    }
    internal static double BlastPower(ShellProfile p,double calibreMm) => Math.Clamp(p.NativeExplosivePower*Math.Pow(Ratio(calibreMm),3),.1,500);
    internal static double SecondPlateBudget(ShellProfile p,double calibreMm,double remainingMm) => Math.Min(remainingMm,ChemicalPenetration(p,calibreMm)*p.SecondPlatePenetrationFactor);
    internal static double SpacedRetention(ShellProfile p,double calibreMm,double plateRhaMm,double gapMm)
    {
        _=Ratio(calibreMm);
        if(!double.IsFinite(plateRhaMm)||plateRhaMm<0||!double.IsFinite(gapMm)||gapMm<0)throw new ArgumentOutOfRangeException(nameof(gapMm));
        var gap=gapMm/calibreMm;
        // Distinct gameplay curves: HEAT jet disruption versus HESH decoupling.
        var exponent=ImpactBehavior(p)=="heat"
            ? p.AirGapLossPerCalibre*Math.Max(0,gap-p.StandoffFreeCalibres)*(.25+.75*(1-Math.Exp(-plateRhaMm/(calibreMm*.25))))
            : p.Behavior=="hesh" ? p.AirGapLossPerCalibre*gap : 0;
        return p.SecondPlatePenetrationFactor+(1-p.SecondPlatePenetrationFactor)*Math.Exp(-exponent);
    }
    internal static int FragmentTarget(string behavior,double calibreMm) => (int)Math.Clamp(Math.Round((behavior=="heat"?40:48)*Math.Pow(Ratio(calibreMm),1.5)),4,32);
    internal static double FragmentVolume(string behavior,double calibreMm)
    {
        var diameter=calibreMm*.001;
        return SpallBalance.ReferenceApVolume(diameter,diameter)*(behavior=="heat"?4:behavior=="hesh"?16:10);
    }
    internal static double RodCone(DartSettings s) => Math.Clamp(1+(5-s.LengthInCalibres)*.06,.7,1.25);
    internal static DartSettings BallisticSettings(ShellProfile p) => p.Settings;
    // APCR/HVAP flies as the light carrier but reaches the plate as its dense core.
    internal static ImpactGeometry CoreImpactGeometry(ShellProfile p,double gunDiameterM)
    {
        var core=ShellBallistics.CoreGeometry(gunDiameterM*1000,p.Settings);
        return new(core.Length,core.Diameter,core.Mass,gunDiameterM);
    }
    internal static DartBallistics Calculate(double calibreMm,double vanillaVelocity,ushort vanillaK,ShellProfile p)
    {
        var result=ShellBallistics.Calculate(calibreMm,vanillaVelocity,vanillaK,BallisticSettings(p));
        if(Powered(p) && p.Atgm is {} flight) result=result with {Velocity=(float)AtgmGuidance.InitialSpeed(flight,vanillaVelocity)};
        // One initial native K budget; no refill or repeated cap after armour/ERA traversal.
        var referenceVelocity=Powered(p) && p.Atgm is {} motor ? Math.Max(result.Velocity,motor.FlightSpeed) : result.Velocity;
        return p.Behavior=="apfsds" ? LongRodPenetration.Bound(result,p.Settings.Density,referenceVelocity) : result;
    }
    internal static double FragmentSpeed(string behavior,double calibreMm) => (behavior=="heat"?1100:behavior=="hesh"?700:900)*Math.Clamp(Math.Pow(Ratio(calibreMm),.15),.7,1.3);
    // Payload energy is available even when the native shell barely perforates.
    // This applies to spawned fragments only; never restore the chemical parent budget.
    internal static double PayloadFragmentSpeed(string behavior,double calibreMm,double nativeSpeed)
    {
        var target=FragmentSpeed(behavior,calibreMm);
        return double.IsFinite(nativeSpeed)?Math.Clamp(nativeSpeed,target,target*1.25):target;
    }
    internal static double Visual(double scale,double calibreMm) => scale*Math.Clamp(Math.Pow(Ratio(calibreMm),1d/3),.6,2);
}

// Only a solid -> air -> solid transition counts as spaced armour.
internal sealed class ChemicalLayers
{
    private double previousPlate, pendingGap;
    private bool havePlate, gapPending;
    internal (double PlateRhaMm,double GapMm)? ObserveBlock(bool solid,double lengthMm)
    {
        if(!double.IsFinite(lengthMm)||lengthMm<0)return null;
        if(!solid){if(havePlate){pendingGap+=lengthMm;gapPending=true;}return null;}
        var result=havePlate && gapPending && pendingGap>0 ? (previousPlate,pendingGap) : ((double,double)?)null;
        previousPlate=gapPending?lengthMm:previousPlate+lengthMm;
        pendingGap=0;gapPending=false;havePlate=true;
        return result;
    }
    private bool seenSolid, seenGap;
    internal bool Degraded { get; private set; }
    internal bool Observe(bool solid)
    {
        if(!solid){if(seenSolid)seenGap=true;return false;}
        if(seenSolid && seenGap)Degraded=true;
        seenSolid=true;
        return Degraded;
    }
}
// 0.22.78 HESH begin
// 0.22.78 (user decision 14-11, "Ja, zo doen"): HESH detonates on the armour surface instead of ricocheting, unless the impact is very
// oblique (from minAngleDegrees on, default 75, the native ricochet rule stands); a perforation ends in a scab: fragments from the rear
// face whose total mass grows with calibre^exponent, and no projectile body that flies on. Pure rules and the optional spall.json "hesh"
// object (absent: these code defaults; the file is never rewritten). Hooks: the existing SimulateFragment prefix/finalizer of
// RuntimeArmourResponses and the existing payload burst of RuntimeSpall.
internal sealed record HeshSettings(double MinAngleDegrees=75,bool LegacyScab=false,double ScabMassKgAt100Mm=25.6,double ScabCalibreExponent=3)
{
    internal static readonly HeshSettings Default=new();
    /// <summary>Native ricochet needs at least 10 degrees from the normal: a limit of 10 changes nothing (bit-identical to 0.22.77).</summary>
    internal const double NativeRicochetDegrees=10,MaximumDegrees=90;
    internal bool SurfaceDetonation=>MinAngleDegrees>NativeRicochetDegrees;
}
/// <summary>The scab of one HESH perforation: fragment count (before the native rounding and 32 cap), steel volume, total mass (rolled homogeneous armour) and mass per native fragment.</summary>
internal readonly record struct HeshScab(double Count,double VolumeM3,double MassKg,double MassPerFragmentKg);
internal readonly record struct HeshSurfaceResult(bool Converted,int ChildrenStopped);
/// <summary>The fragment array as the HESH rules see it: the native adapter in RuntimeArmourResponses/RuntimeSpall, managed stand-ins in the tests.</summary>
internal interface IHeshFragments
{
    int Count{get;}
    ushort Flags(int index);
    int Parent(int index);
    void SetFlags(int index,ushort flags);
    /// <summary>Speed 0 and Killed, written back to the array.</summary>
    void Stop(int index);
}
internal static class HeshRules
{
    internal const ushort Original=1,Killed=8,Deflected=16,Embedded=32,Penetrated=64,Precomputed=128,GenerateSpalling=256;
    internal const double SteelDensity=7850;
    internal const string SurfaceText="HESH detonated on the surface; no scab (plate too thick or too hard)";
    internal static readonly string[] Keys={"minAngleDegrees","legacyScab","scabMassKgAt100mm","scabCalibreExponent"};
    /// <summary>
    /// Native .56.2 ricochet (LOS-MAP; disassembly 1D783B9..1D78858): no perforation, B &lt;= d and at least 10 degrees mark the parent Deflected and
    /// allocate a reflected ORIGINAL child (NewFragment keeps the OriginalPenetrator bit, GenerateSpalling), sometimes also an embedded gouge child.
    /// Below the limit the original HESH stops where it struck: the parent becomes Embedded and every original child of this call is stopped
    /// (speed 0, Killed), so nothing flies on or hits anything. From the limit on, and for every other outcome, the native result stays.
    /// </summary>
    internal static HeshSurfaceResult Surface<T>(T fragments,int index,int countBefore,double angleDegrees,double minAngleDegrees) where T:IHeshFragments
    {
        if(!(minAngleDegrees>HeshSettings.NativeRicochetDegrees)||!double.IsFinite(angleDegrees)||angleDegrees>=minAngleDegrees)return default;
        if(index<0||index>=fragments.Count)return default;
        var flags=fragments.Flags(index);
        if((flags&Original)==0||(flags&(Killed|Embedded|Penetrated))!=0||(flags&Deflected)==0)return default;
        int stopped=0;
        for(int i=Math.Max(0,countBefore);i<fragments.Count;i++)
        {
            if(i==index||fragments.Parent(i)!=index||(fragments.Flags(i)&Original)==0)continue;
            fragments.Stop(i);stopped++;
        }
        fragments.SetFlags(index,(ushort)((flags&~Deflected)|Embedded));
        return new(true,stopped);
    }
    /// <summary>The scab rule applies to the burst of a perforation continuation (an original with PenetrationPrecomputed) unless legacyScab is set; never to other bursts.</summary>
    internal static bool ScabApplies(HeshSettings settings,ushort parentFlags)=>!settings.LegacyScab&&(parentFlags&(Original|Precomputed))==(Original|Precomputed);
    /// <summary>After the scab burst the body is consumed: speed 0 and Killed on the continuation that carried the burst. The air continuation the
    /// game copies from it afterwards starts at speed 0 (the native guard ends it) and the absorbed-parent path of the finalizer stops any other.</summary>
    internal static bool StopBody<T>(T fragments,int parentIndex,HeshSettings settings) where T:IHeshFragments
    {
        if(parentIndex<0||parentIndex>=fragments.Count||!ScabApplies(settings,fragments.Flags(parentIndex)))return false;
        fragments.Stop(parentIndex);return true;
    }
    /// <summary>Count as in 0.22.77 (48 at 100 mm x (calibre/100)^1.5 x profile tuning, 4..32; the game rounds up and caps at 32); total mass =
    /// scabMassKgAt100mm x (calibre/100)^scabCalibreExponent x tuning, as steel volume (the plate's own density gives the mass). Once the count is
    /// at its cap the mass per fragment grows.</summary>
    internal static HeshScab Scab(double calibreMm,double tuning,HeshSettings settings)
    {
        var ratio=ShellBalance.Ratio(calibreMm);
        var count=Math.Clamp(ShellBalance.FragmentTarget("hesh",calibreMm)*tuning,4,32);
        var mass=settings.ScabMassKgAt100Mm*Math.Pow(ratio,settings.ScabCalibreExponent)*tuning;
        return new(count,mass/SteelDensity,mass,mass/NativeCount(count));
    }
    /// <summary>The fragment count of a burst as the game makes it: ceil(area / spallFactor), at most 32.</summary>
    internal static int NativeCount(double count)=>(int)Math.Clamp(Math.Ceiling(count-1e-9),1,32);
    /// <summary>
    /// Reads the optional "hesh" object of sprocket.expandedballistics.spall.json and returns the text without it for the strict spall parser
    /// (the same string instance when the key is absent). A missing key gives the code default; an invalid value is clamped (minAngleDegrees
    /// to 10-90) or replaced by its default, and all problems together give one log line. Never writes the file; never throws.
    /// </summary>
    internal static HeshSettings Read(string json,Action<string> log,out string spallJson)
    {
        spallJson=json;
        System.Text.Json.Nodes.JsonObject? root;
        try{root=System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;}catch(Exception){return HeshSettings.Default;}
        if(root==null||!root.TryGetPropertyValue("hesh",out var node))return HeshSettings.Default;
        root.Remove("hesh");spallJson=root.ToJsonString();
        var d=HeshSettings.Default;var issues=new List<string>();
        if(node is not System.Text.Json.Nodes.JsonObject obj){log("[HESH] spall.json: \"hesh\" must be an object; the defaults are used.");return d;}
        foreach(var p in obj)if(Array.IndexOf(Keys,p.Key)<0)issues.Add("unknown key "+p.Key+" ignored");
        string N(double x)=>x.ToString(Math.Abs(x)<1e9?"0.###":"G4",System.Globalization.CultureInfo.InvariantCulture);
        double Number(string key,double fallback,double min,double max)
        {
            if(!obj.TryGetPropertyValue(key,out var v))return fallback;
            if(v is not System.Text.Json.Nodes.JsonValue value||!value.TryGetValue<double>(out var x)||!double.IsFinite(x)){issues.Add(key+" is not a number, using "+N(fallback));return fallback;}
            if(x<min){issues.Add(key+" "+N(x)+" is below "+N(min)+", using "+N(min));return min;}
            if(x>max){issues.Add(key+" "+N(x)+" is above "+N(max)+", using "+N(max));return max;}
            return x;
        }
        bool Flag(string key,bool fallback)
        {
            if(!obj.TryGetPropertyValue(key,out var v))return fallback;
            if(v is System.Text.Json.Nodes.JsonValue value&&value.TryGetValue<bool>(out var b))return b;
            issues.Add(key+" is not true or false, using "+(fallback?"true":"false"));return fallback;
        }
        var s=new HeshSettings(Number("minAngleDegrees",d.MinAngleDegrees,HeshSettings.NativeRicochetDegrees,HeshSettings.MaximumDegrees),Flag("legacyScab",d.LegacyScab),
            Number("scabMassKgAt100mm",d.ScabMassKgAt100Mm,.1,500),Number("scabCalibreExponent",d.ScabCalibreExponent,1,4));
        if(issues.Count>0)log("[HESH] spall.json hesh: "+string.Join("; ",issues)+".");
        return s;
    }
    internal static string Describe(HeshSettings s)=>"[HESH] "+(s.SurfaceDetonation?$"detonates on the surface below {s.MinAngleDegrees:0.#} deg (native ricochet from there on)":"native ricochet rule (minAngleDegrees 10)")+"; "+
        (s.LegacyScab?"perforation: 0.22.77 payload burst, the body flies on (legacyScab)":$"perforation: scab of {s.ScabMassKgAt100Mm:0.##} kg x (calibre/100)^{s.ScabCalibreExponent:0.##} from the rear face, no residual projectile");
}
// 0.22.78 HESH end
// 0.22.86 HE begin
// 0.22.86 (user decision 15-11, "Ja, bouw HE"): HE gets two parts, each with its own switch in the optional spall.json "he" object (absent: these
// code defaults; the file is never rewritten). (1) The blast filter: inside the HE scope (set by the existing Explosive prefixes, cleared by their
// finalizers) the existing VehicleHealthRegister.HealthDelta(float,mods) prefix replaces the game's loop over the pools of a vehicle by the same loop,
// with per pool the game's own value for an exterior module, for an interior module when the part perforated, and the value x shock(calibre) for an
// interior module otherwise. (2) The perforating part: an HE type gets the functions [AP part, HE]; the existing Penetrate prefix gives the AP part a fixed
// budget of perforationMmAt100 x (calibre/100)^perforationExponent mm RHA; a perforation ends in a burst of shell and plate fragments behind the plate and
// the body never flies on; without perforation nothing flies on and there is no ricochet below minAngleDegrees. Pure rules only; the hooks are in
// RuntimeShellSelection, RuntimeDamageFeed, RuntimeSpall and RuntimeArmourResponses.
internal sealed record HeSettings
{
    /// <summary>Modules outside the armour (the full blast always): running gear, the gun and the optics. Names of the game's own module classes.</summary>
    internal static readonly string[] DefaultExteriorTypes={"TrackAssembly","WheelMount","Cannon","GunnerSight","Viewport","CommanderCupola"};
    /// <summary>Modules and crew behind the armour (the full blast only after a perforation, otherwise the shock).</summary>
    internal static readonly string[] DefaultInteriorTypes={"CrewSeat","AmmoRack","FuelTank","FuelReserve","CombustionEngine","TransmissionBlock","TraverseMotor","LayingDrive","CannonBreech","TurretBasket"};
    internal static readonly HeSettings Default=new();
    internal bool BlastFilter{get;init;}=true;
    internal bool Perforation{get;init;}=true;
    internal double PerforationMmAt100{get;init;}=20;
    internal double PerforationExponent{get;init;}=2;
    internal double MinAngleDegrees{get;init;}=75;
    internal double InteriorShockAt100{get;init;}=.04;
    internal double InteriorShockExponent{get;init;}=2;
    internal double InteriorShockMax{get;init;}=.25;
    internal string[] ExteriorTypes{get;init;}=DefaultExteriorTypes;
    internal string[] InteriorTypes{get;init;}=DefaultInteriorTypes;
    // 0.22.89 begin
    /// <summary>Spaced-armour (skirt) rule: a perforation counts for the interior only when no armour of the same vehicle follows within SpacedArmourGapMm behind the plate.</summary>
    internal bool SpacedArmourRule{get;init;}=true;
    internal double SpacedArmourGapMm{get;init;}=750;
    // 0.22.89 end
    /// <summary>A ricochet of the part below the limit becomes a stop where it struck (the HESH rule); a limit of 10 leaves the game's word.</summary>
    internal bool SurfaceDetonation=>MinAngleDegrees>HeshSettings.NativeRicochetDegrees;
}
internal enum HePoolClass:byte{Unknown,Exterior,Interior}
internal enum HeBlastOutcome:byte{Native,Applied,Failed}
/// <summary>The pools of one vehicle as the blast filter sees them: the native adapter in RuntimeDamageFeed, managed stand-ins in the tests.</summary>
internal interface IHeBlastPools
{
    /// <summary>The number of pools in the game's list, read once (as the game's loop reads it once).</summary>
    int Count{get;}
    /// <summary>The class names of the owner of pool i, its own class first, then its base classes; null when the owner is unknown. May throw.</summary>
    IReadOnlyList<string>? Types(int index);
    /// <summary>The game's own per-pool damage call (HealthPool.HealthDelta(value, modifiers)), with the modifiers of the blast.</summary>
    void Apply(int index,float value);
}
/// <summary>The plan of one blast on one vehicle, computed in full before any damage is applied.</summary>
internal sealed class HeBlastPlan
{
    internal HePoolClass[] Classes=Array.Empty<HePoolClass>();
    internal double[] Factors=Array.Empty<double>();
    internal string[] TypeNames=Array.Empty<string>();
    internal int Count,Applied;
    internal HeBlastOutcome Outcome;
    internal Exception? Error;
    internal bool Perforated;internal double Shock;
    internal int Of(HePoolClass c){int n=0;foreach(var x in Classes)if(x==c)n++;return n;}
    internal bool Shocked{get{for(int i=0;i<Classes.Length;i++)if(Classes[i]==HePoolClass.Interior&&Factors[i]<1)return true;return false;}}
}
/// <summary>The perforation of one plate by the part, for the blast (the vehicle) and the report line.</summary>
internal sealed record HePerforation(IntPtr Register,double NormalMm,double RhaEqMm,double AngleDegrees,string? HitId);
/// <summary>What the AP part of one projectile left for its HE call in the same hit: the vehicles whose armour it perforated and the layer it ended in.</summary>
internal sealed class HeProxyRecord
{
    internal int Id;internal float Spawn;
    internal readonly HashSet<IntPtr> Perforated=new();
    internal HePerforation? Perforation;
    internal HeSpacedPerforation? Spaced; // 0.22.89: the first perforation of spaced armour (skirt rule)
    internal string? LastHitId;
    internal bool Matches(int id,float spawn)=>Id==id&&Spawn.Equals(spawn);
}
/// <summary>The HE scope of one detonation: set by the Explosive prefix, read by the HealthDelta prefix, cleared by the Explosive finalizer.</summary>
internal sealed class HeScopeData
{
    internal int Id;internal float Spawn;internal bool Ground;
    internal double CalibreMm,Power,Shock;
    internal string Guid="";
    internal float X,Y,Z;
    internal IReadOnlyCollection<IntPtr> Perforated=Array.Empty<IntPtr>();
    internal string? LastHitId;
    internal HeSpacedPerforation? Spaced; // 0.22.89: the spaced armour the part perforated (skirt rule)
    internal bool NoteRecorded{get;set;}
    /// <summary>Owner (component pointer) → the tag of its damage line ("[exterior]", "[interior]", "[shock 9.2 %]").</summary>
    internal Dictionary<IntPtr,string>? Tags{get;set;}
    internal bool IsPerforated(IntPtr register)=>!Ground&&Perforated.Contains(register);
}
/// <summary>The thread-local HE state: the record of the AP part and the scope of the HE call. Main thread only (the game simulates projectiles there).</summary>
internal static class HeScopeState
{
    [ThreadStatic] private static HeProxyRecord? proxy;
    [ThreadStatic] private static HeScopeData? active;
    internal static HeScopeData? Active=>active;
    internal static HeProxyRecord? Proxy=>proxy;
    /// <summary>Every impact call of an HE round (its AP part, then its HE call) starts here; the record of the same projectile is kept, any other is replaced.</summary>
    internal static HeProxyRecord ProxyStarted(int id,float spawn)
    {
        if(proxy is {} p&&p.Matches(id,spawn))return p;
        return proxy=new HeProxyRecord{Id=id,Spawn=spawn};
    }
    internal static bool ProxyImpact(int id,float spawn)=>proxy is {} p&&p.Matches(id,spawn);
    internal static void Perforated(HePerforation perforation){if(proxy is not {} p)return;p.Perforated.Add(perforation.Register);p.Perforation??=perforation;}
    internal static void Spaced(HeSpacedPerforation perforation){if(proxy is {} p)p.Spaced??=perforation;} // 0.22.89
    internal static void LastLayer(string? hitId){if(proxy is {} p&&hitId!=null)p.LastHitId=hitId;}
    /// <summary>The HE scope: the knalpunt, P, calibre, projectile GUID and, unless it is a ground hit, the vehicles whose armour the part perforated.</summary>
    internal static HeScopeData Begin(int id,float spawn,float x,float y,float z,double power,double calibreMm,string guid,bool ground,HeSettings settings)
    {
        var p=proxy is {} r&&r.Matches(id,spawn)?r:null;
        var scope=new HeScopeData{Id=id,Spawn=spawn,X=x,Y=y,Z=z,Power=power,CalibreMm=calibreMm,Guid=guid,Ground=ground, // 0.22.89 replaced
            Shock=HeRules.Shock(settings,calibreMm),Perforated=ground||p==null?Array.Empty<IntPtr>():p.Perforated.ToArray(),LastHitId=ground?null:p?.LastHitId};
        if(!ground&&p?.Spaced is {} spaced)scope.Spaced=spaced; // 0.22.89
        return active=scope; // 0.22.89
    }
    /// <summary>The finalizer of every Explosive call: the scope and the record of the AP part are gone (mines and zones are always outside).</summary>
    internal static void End(){active=null;proxy=null;}
}
/// <summary>
/// One impact of an HE round with a perforating part is one shot: the game calls the HitDamageModel of both functions for the same hit, the AP part first.
/// The shot the AP part opened is kept open, not recorded, and the HE call continues it. A kept shot that no HE call picks up is recorded at the next
/// HE call or the next kept shot (so nothing is lost and nothing is recorded twice).
/// </summary>
internal sealed class HeShotLink<TShot> where TShot:class
{
    private TShot? pending;private int pendingId;private float pendingSpawn;
    internal TShot? Pending=>pending;
    /// <summary>The AP part ended: true when the shot is kept open for the HE call (it is then not recorded now).</summary>
    internal bool Defer(TShot shot,int id,float spawn,bool explosivePhase,bool proxyImpact,Action<TShot> record)
    {
        if(explosivePhase||!proxyImpact)return false;
        if(pending is {} older&&!ReferenceEquals(older,shot)){pending=null;record(older);}
        pending=shot;pendingId=id;pendingSpawn=spawn;return true;
    }
    /// <summary>The HE call starts: the kept shot of the same projectile, or null (an unrelated kept shot is recorded first).</summary>
    internal TShot? Resume(int id,float spawn,Action<TShot> record)
    {
        if(pending is not {} shot)return null;
        pending=null;
        if(pendingId==id&&pendingSpawn.Equals(spawn))return shot;
        record(shot);return null;
    }
}
internal readonly record struct HeFragments(double Count,double MassKg,double VolumeM3,double MassPerFragmentKg);
internal readonly record struct HeSurfaceResult(bool Converted,bool Grazed,int ChildrenStopped);
internal static class HeRules
{
    internal static readonly string[] Keys={"blastFilter","perforation","perforationMmAt100","perforationExponent","minAngleDegrees","interiorShockAt100","interiorShockExponent","interiorShockMax","exteriorTypes","interiorTypes","spacedArmourRule","spacedArmourGapMm"}; // 0.22.89 replaced
    internal const double FragmentMassKgAt100=8,FragmentMassExponent=3;
    internal const string OutsidePrefix="HE detonated outside the armour",PerforationPrefix="HE perforated ";
    private static readonly System.Globalization.CultureInfo C=System.Globalization.CultureInfo.InvariantCulture;
    /// <summary>The fixed budget of the perforating part in mm RHA: perforationMmAt100 x (calibre/100)^perforationExponent.</summary>
    internal static double PerforationMm(HeSettings s,double calibreMm)=>s.PerforationMmAt100*Math.Pow(ShellBalance.Ratio(calibreMm),s.PerforationExponent);
    /// <summary>The fraction of the blast that reaches an interior module without perforation: min(interiorShockMax, interiorShockAt100 x (calibre/100)^interiorShockExponent).</summary>
    internal static double Shock(HeSettings s,double calibreMm)
    {
        if(!double.IsFinite(calibreMm)||calibreMm<=0)return Math.Clamp(s.InteriorShockMax,0,1);
        var x=Math.Min(s.InteriorShockMax,s.InteriorShockAt100*Math.Pow(calibreMm/100,s.InteriorShockExponent));
        return double.IsFinite(x)?Math.Clamp(x,0,1):Math.Clamp(s.InteriorShockMax,0,1);
    }
    /// <summary>
    /// The fragments of one perforation (the burst from the rear face): count as every payload burst (48 x (calibre/100)^1.5, 4..32; the game rounds up and
    /// caps a burst at 32), total mass 8 kg x (calibre/100)^3 of steel: about the forward half of the steel body of the shell plus the plug of the plate. That is
    /// a third of the HESH scab (25.6 kg x (calibre/100)^3), whose fragments are all plate material.
    /// </summary>
    internal static HeFragments Fragments(double calibreMm)
    {
        var ratio=ShellBalance.Ratio(calibreMm);
        var count=Math.Clamp(ShellBalance.FragmentTarget("he",calibreMm),4,32);
        var mass=FragmentMassKgAt100*Math.Pow(ratio,FragmentMassExponent);
        return new(count,mass,mass/HeshRules.SteelDensity,mass/HeshRules.NativeCount(count));
    }
    /// <summary>The class of a pool's owner: the first of its class names (own class, then base classes) that a list names; a name on both lists counts as exterior.</summary>
    internal static HePoolClass Classify(HeSettings s,IReadOnlyList<string>? types)
    {
        if(types==null)return HePoolClass.Unknown;
        for(int i=0;i<types.Count;i++)
        {
            var t=types[i];
            if(Array.IndexOf(s.ExteriorTypes,t)>=0)return HePoolClass.Exterior;
            if(Array.IndexOf(s.InteriorTypes,t)>=0)return HePoolClass.Interior;
        }
        return HePoolClass.Unknown;
    }
    /// <summary>The factor on the game's own value: the shock for an interior module without perforation, else exactly 1 (the value passes unchanged).</summary>
    internal static double Factor(HePoolClass c,bool perforated,double shock)=>c==HePoolClass.Interior&&!perforated?shock:1;
    /// <summary>
    /// The blast filter on one vehicle. First the whole plan (classes and factors of every pool); an exception there leaves the game's own loop
    /// (<see cref="HeBlastOutcome.Native"/>). Then the game's own per-pool call, in the game's order, with the game's value for a factor of 1 and value x factor
    /// otherwise; once the first call has started, the game's loop never runs as well (<see cref="HeBlastOutcome.Failed"/> on an exception: no double damage).
    /// </summary>
    internal static HeBlastPlan Run<T>(T pools,float value,bool perforated,double shock,HeSettings s) where T:IHeBlastPools
    {
        var plan=new HeBlastPlan{Perforated=perforated,Shock=shock};
        try
        {
            if(!float.IsFinite(value))throw new ArgumentOutOfRangeException(nameof(value),"blast value is not finite");
            if(!double.IsFinite(shock)||shock<0||shock>1)throw new ArgumentOutOfRangeException(nameof(shock));
            var n=pools.Count;
            if(n<0)throw new InvalidOperationException("negative pool count");
            plan.Count=n;plan.Classes=new HePoolClass[n];plan.Factors=new double[n];plan.TypeNames=new string[n];
            for(int i=0;i<n;i++)
            {
                var types=pools.Types(i);
                plan.Classes[i]=Classify(s,types);
                plan.TypeNames[i]=types is {Count:>0}?types[0]:"";
                plan.Factors[i]=Factor(plan.Classes[i],perforated,shock);
            }
        }
        catch(Exception ex){plan.Outcome=HeBlastOutcome.Native;plan.Error=ex;return plan;}
        plan.Outcome=HeBlastOutcome.Failed;
        try
        {
            for(int i=0;i<plan.Count;i++)
            {
                var f=plan.Factors[i];
                pools.Apply(i,f==1?value:(float)(value*f));
                plan.Applied++;
            }
            plan.Outcome=HeBlastOutcome.Applied;
        }
        catch(Exception ex){plan.Error=ex;}
        return plan;
    }
    /// <summary>The fragments of the part apply to the burst of a perforation continuation only (an original that left the plate: 0x181).</summary>
    internal static bool FragmentsApply(ushort flags)=>(flags&(HeshRules.Original|HeshRules.Precomputed))==(HeshRules.Original|HeshRules.Precomputed);
    /// <summary>
    /// Without a perforation the part does nothing more. Below the limit a ricochet becomes a stop where it struck (the HESH rule: Embedded, children stopped);
    /// from the limit on the ricochet word stays but no child flies on (no residual projectile). Every other outcome stays as the game made it.
    /// </summary>
    internal static HeSurfaceResult NoPerforation<T>(T fragments,int index,int countBefore,double angleDegrees,double minAngleDegrees) where T:IHeshFragments
    {
        if(index<0||index>=fragments.Count)return default;
        var flags=fragments.Flags(index);
        if((flags&HeshRules.Original)==0||(flags&(HeshRules.Killed|HeshRules.Embedded|HeshRules.Penetrated))!=0||(flags&HeshRules.Deflected)==0)return default;
        var surface=HeshRules.Surface(fragments,index,countBefore,angleDegrees,minAngleDegrees);
        if(surface.Converted)return new(true,false,surface.ChildrenStopped);
        return new(false,true,StopChildren(fragments,index,countBefore));
    }
    /// <summary>The body never flies on after a perforation: a continuation (original + precomputed) is stopped after its own call, with every original child of that call.</summary>
    internal static int StopContinuation<T>(T fragments,int index,int countBefore) where T:IHeshFragments
    {
        if(index<0||index>=fragments.Count||!FragmentsApply(fragments.Flags(index)))return 0;
        fragments.Stop(index);
        return 1+StopChildren(fragments,index,countBefore);
    }
    private static int StopChildren<T>(T fragments,int index,int countBefore) where T:IHeshFragments
    {
        int stopped=0;
        for(int i=Math.Max(0,countBefore);i<fragments.Count;i++)
        {
            if(i==index||fragments.Parent(i)!=index||(fragments.Flags(i)&HeshRules.Original)==0)continue;
            fragments.Stop(i);stopped++;
        }
        return stopped;
    }
    internal static string Percent(double fraction)=>(fraction*100).ToString("0.#",C);
    /// <summary>Report line without a perforation (scientific English; "modules and crew", never game terms).</summary>
    internal static string OutsideText(double shock)=>$"{OutsidePrefix}; interior modules and crew received a shock of {Percent(shock)} % of the blast";
    /// <summary>Report line of a perforation.</summary>
    internal static string PerforationText(double normalMm,double rhaEqMm,double angleDegrees,int fragments)=>
        string.Create(C,$"{PerforationPrefix}{normalMm:0} mm ({rhaEqMm:0} mm RHA-eq) at {angleDegrees:0}°; detonation behind the plate; {fragments} fragments");
    /// <summary>The tag of a damage line: "[exterior]", "[interior]" (after a perforation) or "[shock N %]"; nothing for an unknown module.</summary>
    internal static string Tag(HePoolClass c,bool perforated,double shock)=>c switch
    {
        HePoolClass.Exterior=>"[exterior]",
        HePoolClass.Interior=>perforated?"[interior]":$"[shock {Percent(shock)} %]",
        _=>""
    };
    /// <summary>
    /// Reads the optional "he" object of sprocket.expandedballistics.spall.json and returns the text without it for the strict spall parser (the same string
    /// instance when the key is absent). A missing key gives the code default; an invalid value is clamped or replaced by its default, and all problems
    /// together give one log line. Never writes the file; never throws.
    /// </summary>
    internal static HeSettings Read(string json,Action<string> log,out string spallJson)
    {
        spallJson=json;
        System.Text.Json.Nodes.JsonObject? root;
        try{root=System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;}catch(Exception){return HeSettings.Default;}
        if(root==null||!root.TryGetPropertyValue("he",out var node))return HeSettings.Default;
        root.Remove("he");spallJson=root.ToJsonString();
        var d=HeSettings.Default;var issues=new List<string>();
        if(node is not System.Text.Json.Nodes.JsonObject obj){log("[HE] spall.json: \"he\" must be an object; the defaults are used.");return d;}
        foreach(var p in obj)if(Array.IndexOf(Keys,p.Key)<0)issues.Add("unknown key "+p.Key+" ignored");
        string N(double x)=>x.ToString(Math.Abs(x)<1e9?"0.###":"G4",C);
        double Number(string key,double fallback,double min,double max)
        {
            if(!obj.TryGetPropertyValue(key,out var v))return fallback;
            if(v is not System.Text.Json.Nodes.JsonValue value||!value.TryGetValue<double>(out var x)||!double.IsFinite(x)){issues.Add(key+" is not a number, using "+N(fallback));return fallback;}
            if(x<min){issues.Add(key+" "+N(x)+" is below "+N(min)+", using "+N(min));return min;}
            if(x>max){issues.Add(key+" "+N(x)+" is above "+N(max)+", using "+N(max));return max;}
            return x;
        }
        bool Flag(string key,bool fallback)
        {
            if(!obj.TryGetPropertyValue(key,out var v))return fallback;
            if(v is System.Text.Json.Nodes.JsonValue value&&value.TryGetValue<bool>(out var b))return b;
            issues.Add(key+" is not true or false, using "+(fallback?"true":"false"));return fallback;
        }
        string[] Names(string key,string[] fallback)
        {
            if(!obj.TryGetPropertyValue(key,out var v))return fallback;
            if(v is not System.Text.Json.Nodes.JsonArray array||array.Count>64){issues.Add(key+" is not a list of at most 64 names, using the default list");return fallback;}
            var names=new List<string>();
            foreach(var item in array)
            {
                if(item is not System.Text.Json.Nodes.JsonValue value||!value.TryGetValue<string>(out var name)||string.IsNullOrWhiteSpace(name)||name.Length>64||name.Any(char.IsControl))
                {issues.Add(key+" holds an entry that is not a name, using the default list");return fallback;}
                if(!names.Contains(name.Trim(),StringComparer.Ordinal))names.Add(name.Trim());
            }
            return names.ToArray();
        }
        var exterior=Names("exteriorTypes",d.ExteriorTypes);var interior=Names("interiorTypes",d.InteriorTypes);
        var both=interior.Where(n=>Array.IndexOf(exterior,n)>=0).ToArray();
        if(both.Length>0){issues.Add("on both lists, counted as exterior: "+string.Join(", ",both));interior=interior.Where(n=>Array.IndexOf(exterior,n)<0).ToArray();}
        var s=new HeSettings
        {
            BlastFilter=Flag("blastFilter",d.BlastFilter),Perforation=Flag("perforation",d.Perforation),
            PerforationMmAt100=Number("perforationMmAt100",d.PerforationMmAt100,1,500),PerforationExponent=Number("perforationExponent",d.PerforationExponent,0,4),
            MinAngleDegrees=Number("minAngleDegrees",d.MinAngleDegrees,HeshSettings.NativeRicochetDegrees,HeshSettings.MaximumDegrees),
            InteriorShockAt100=Number("interiorShockAt100",d.InteriorShockAt100,0,1),InteriorShockExponent=Number("interiorShockExponent",d.InteriorShockExponent,0,4),
            InteriorShockMax=Number("interiorShockMax",d.InteriorShockMax,0,1),ExteriorTypes=exterior,InteriorTypes=interior
            ,SpacedArmourRule=Flag("spacedArmourRule",d.SpacedArmourRule),SpacedArmourGapMm=Number("spacedArmourGapMm",d.SpacedArmourGapMm,0,3000) // 0.22.89: read last, so the log of the other keys is as in 0.22.88
        };
        if(issues.Count>0)log("[HE] spall.json he: "+string.Join("; ",issues)+".");
        return s;
    }
    internal static string Describe(HeSettings s)=>string.Create(C,$"[HE] blast filter {(s.BlastFilter?"on":"off")} (interior shock {s.InteriorShockAt100*100:0.##} % x (calibre/100)^{s.InteriorShockExponent:0.##}, at most {s.InteriorShockMax*100:0.##} %; exterior: {string.Join(",",s.ExteriorTypes)}; interior: {string.Join(",",s.InteriorTypes)}); ")+
        string.Create(C,$"perforating part {(s.Perforation?"on":"off")} ({s.PerforationMmAt100:0.##} mm x (calibre/100)^{s.PerforationExponent:0.##}; stop below {s.MinAngleDegrees:0.#} deg)")+"; "+HeSpaced.Describe(s); // 0.22.89 replaced
}
// 0.22.86 HE end
// 0.22.89 begin
// 0.22.89 (user 15-11 "Nu installeren", the skirt rule announced for a later version; built with 0.22.89 on 16-11, "ja bouw het vangnet exact hetzelfde"):
// a perforation by the HE part counts as "interior exposed" (the full blast for the interior modules) only when no armour of the same vehicle follows along
// the line of fire within spacedArmourGapMm behind the perforated plate. Otherwise the plate is spaced armour (a skirt, a mudguard, an ERA box): HE
// detonates in the gap and the interior receives the shock only. The data is the sorted intersection list of the block the part reached (the list that
// GetNextMaterialBlock reads, as the spall gap probe); no new hook and no new probe per module. Pure rules only; the hook bodies are in
// RuntimeArmourResponses (HeBlock, HeEnd) and RuntimeDamageFeed (HeBlast.After).
/// <summary>The perforation of spaced armour by the part: the vehicle, the plate, and the distance to the next armour behind it.</summary>
internal sealed record HeSpacedPerforation(IntPtr Register,double NormalMm,double RhaEqMm,double AngleDegrees,double NextArmourMm,string? HitId);
internal static class HeSpaced
{
    private static readonly System.Globalization.CultureInfo C=System.Globalization.CultureInfo.InvariantCulture;
    private const string Marker=" mm of spaced armour; detonation in the gap (next armour ";
    private static readonly System.Text.RegularExpressions.Regex Pattern=new(@"^HE perforated (\d+) mm of spaced armour; detonation in the gap \(next armour (\d+) mm behind\); interior modules and crew received a shock of ([\d.]+) %$",System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    /// <summary>The rule decides "spaced": it is on and armour of the same vehicle follows within the gap (a distance of 0 is a layer that touches the plate).</summary>
    internal static bool Applies(HeSettings s,double nextArmourMm)=>s.SpacedArmourRule&&!double.IsNaN(nextArmourMm)&&nextArmourMm>=0&&nextArmourMm<=s.SpacedArmourGapMm;
    /// <summary>
    /// The distance in mm from the exit of the perforated block (hit index exitIndex) to the nearest Enter hit of armour behind it, looked at only up to windowMm
    /// (the armour test runs only for hits inside the window); +infinity when none. distanceM is metres along the path, as the native hits are.
    /// </summary>
    internal static double NextArmourMm(int count,Func<int,double> distanceM,Func<int,bool> enter,Func<int,bool> armour,int exitIndex,double windowMm)
    {
        if(exitIndex<0||exitIndex>=count||double.IsNaN(windowMm)||windowMm<0)return double.PositiveInfinity;
        var exit=distanceM(exitIndex);if(!double.IsFinite(exit))return double.PositiveInfinity;
        var best=double.PositiveInfinity;
        for(int k=exitIndex+1;k<count;k++)
        {
            var d=distanceM(k);if(!double.IsFinite(d))continue;
            var gap=(d-exit)*1000.0;
            if(gap<-1||gap>windowMm||gap>=best||!enter(k)||!armour(k))continue;
            best=Math.Max(0,gap);
        }
        return best;
    }
    /// <summary>Report line of a perforation of spaced armour (scientific English).</summary>
    internal static string Text(double normalMm,double nextArmourMm,double shock)=>
        string.Create(C,$"{HeRules.PerforationPrefix}{normalMm:0} mm of spaced armour; detonation in the gap (next armour {nextArmourMm:0} mm behind); interior modules and crew received a shock of {HeRules.Percent(shock)} %");
    internal static bool Is(string? text)=>text!=null&&text.StartsWith(HeRules.PerforationPrefix,StringComparison.Ordinal)&&text.Contains(Marker,StringComparison.Ordinal);
    /// <summary>The hit panel's short form: "HE in the gap behind 8 mm spaced armour, shock 9.2 %".</summary>
    internal static string Token(string text){var m=Pattern.Match(text);return m.Success?$"HE in the gap behind {m.Groups[1].Value} mm spaced armour, shock {m.Groups[3].Value} %":"HE in the gap of spaced armour";}
    internal static string Describe(HeSettings s)=>string.Create(C,$"spaced-armour rule {(s.SpacedArmourRule?"on":"off")} (armour within {s.SpacedArmourGapMm:0} mm behind a perforated plate: detonation in the gap, interior shock only)");
}
// 0.22.89 end
