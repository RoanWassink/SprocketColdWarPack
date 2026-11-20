using System.Globalization;
using System.Numerics;
namespace SprocketExpandedBallistics;

// 0.22.92: pure rules for the ATGM guidance sight's scope mask choice and its
// own HUD. No Unity types: the tests compile this file directly and the runtime
// (RuntimeAtgmSightHud) only draws what these rules and this layout say.
internal enum AtgmScopeMaskMode{Hidden,Follow}
internal enum AtgmHudStyle{Auto,Digital,Analog,Off}

internal static class AtgmSightHudConfig
{
    internal const string Section="ATGM guidance sight";
    internal const string MaskKey="Scope mask",StyleKey="HUD style",YearKey="Digital from year";
    internal const string RangeKey="Range readout",TimerKey="Flight timer",LampsKey="Status lamps",ReticleKey="Reticle";
    internal const string DefaultMask="Hidden",DefaultStyle="Auto";
    internal const int DefaultDigitalYear=1990,MinimumYear=1900,MaximumYear=2100;
    internal static readonly string[] MaskValues={"Hidden","Follow"};
    internal static readonly string[] StyleValues={"Auto","Digital","Analog","Off"};
    internal const string MaskDescription="What happens to the game's black scope circle in the ATGM guidance sight. "+
        "Hidden: the circle and its black edge are switched off while you look through the ATGM guidance sight, and come back as soon as you leave it. "+
        "Follow: the circle stays and moves with the sight's guidance line while you aim, instead of only after switching sights. Either way the aim point follows the guidance line.";
    internal const string StyleDescription="Symbols drawn in the ATGM guidance sight (display only). "+
        "Digital: a thin reticle with mil marks, a 4-digit laser range in metres, green status lamps and a flight timer, like a TOW-2B sight of the 1990s. "+
        "Analog: a thin graticule with mil marks and range stadia, no digits, and two small lamps, like a Swingfire-era sight. "+
        "Auto: Digital when the vehicle's date is in or after 'Digital from year', otherwise Analog. Off: no HUD.";
    internal const string YearDescription="Auto HUD style: vehicles dated in or after this year get the Digital style, earlier ones the Analog style.";
    internal static AtgmScopeMaskMode Mask(string? text)=>
        string.Equals(text?.Trim(),"Follow",StringComparison.OrdinalIgnoreCase)?AtgmScopeMaskMode.Follow:AtgmScopeMaskMode.Hidden;
    internal static AtgmHudStyle Style(string? text)=>(text?.Trim().ToLowerInvariant())switch
    {"digital"=>AtgmHudStyle.Digital,"analog"=>AtgmHudStyle.Analog,"off"=>AtgmHudStyle.Off,_=>AtgmHudStyle.Auto};
    internal static int Year(int value)=>value is >=MinimumYear and <=MaximumYear?value:DefaultDigitalYear;
}

internal sealed record AtgmSightHudOptions(AtgmScopeMaskMode Mask=AtgmScopeMaskMode.Hidden,AtgmHudStyle Style=AtgmHudStyle.Auto,int DigitalFromYear=AtgmSightHudConfig.DefaultDigitalYear,
    bool RangeReadout=true,bool FlightTimer=true,bool StatusLamps=true,bool Reticle=true);

// Read-only inputs for one frame: everything comes from what EB already has
// (TryAim, the launcher group, the shell profile, the native fire time).
internal readonly record struct AtgmHudState(int RoundsReady,int Tubes,bool? LauncherUp,double? FlightSeconds,double GuidanceSeconds,double? RangeM,double? MaxRangeM,double Time);

internal static class AtgmSightHudRules
{
    // Never an era name: only the vehicle's own date against the cfg year. An
    // unreadable date gets Digital, the look of the dual-aperture sight model.
    internal static AtgmHudStyle Resolve(AtgmHudStyle style,int? vehicleYear,int digitalFromYear)=>style!=AtgmHudStyle.Auto?style:
        vehicleYear is {} year&&year<AtgmSightHudConfig.Year(digitalFromYear)?AtgmHudStyle.Analog:AtgmHudStyle.Digital;
    internal static bool Visible(AtgmHudStyle resolved,bool activeSight,bool cursorVisible,bool editor)=>
        resolved is AtgmHudStyle.Digital or AtgmHudStyle.Analog&&activeSight&&!cursorVisible&&!editor;
    // The laser readout: 4 digits in metres, "----" without a return.
    internal static string RangeText(double? metres)=>metres is {} m&&double.IsFinite(m)&&m>=.5&&m<9999.5?((int)Math.Round(m,MidpointRounding.AwayFromZero)).ToString("0000",CultureInfo.InvariantCulture):"----";
    internal static string FlightText(double? seconds)=>seconds is {} s&&double.IsFinite(s)&&s>=0?"FLT "+Math.Min(s,99.9).ToString("00.0",CultureInfo.InvariantCulture):"FLT --.-";
    internal static string ReadyText(int rounds)=>"RDY "+Math.Clamp(rounds,0,9).ToString(CultureInfo.InvariantCulture);
    internal static string LauncherText(bool? up)=>up==true?"LCHR UP":up==false?"LCHR DN":"LCHR --";
    // Guided time: the profile's maximum flight time, cut short by the wire
    // length at the profile's flight speed. An estimate: the boost phase is not modelled.
    internal static double GuidanceSeconds(double flightSpeed,double maximumFlightTime,double? wireLength)
    {
        var limit=double.IsFinite(maximumFlightTime)&&maximumFlightTime>0?maximumFlightTime:0;
        if(wireLength is {} wire&&double.IsFinite(wire)&&wire>0&&double.IsFinite(flightSpeed)&&flightSpeed>0)limit=Math.Min(limit,wire/flightSpeed);
        return limit;
    }
    internal static double? MaxRangeMeters(double flightSpeed,double maximumFlightTime,double? wireLength)
    {
        double? range=double.IsFinite(flightSpeed)&&flightSpeed>0&&double.IsFinite(maximumFlightTime)&&maximumFlightTime>0?flightSpeed*maximumFlightTime:null;
        if(wireLength is {} wire&&double.IsFinite(wire)&&wire>0)range=range is {} r?Math.Min(r,wire):wire;
        return range;
    }
    internal static bool InFlight(double? seconds,double limit)=>seconds is {} s&&double.IsFinite(s)&&s>=0&&s<limit;
    // Warn in the last tenth of the guided time, at least the last 1.5 s.
    internal static bool NearLimit(double? seconds,double limit)=>InFlight(seconds,limit)&&limit-seconds!.Value<=Math.Max(1.5,.1*limit);
    internal static bool OutOfRange(double? rangeM,double? maxRangeM)=>rangeM is {} r&&maxRangeM is {} m&&double.IsFinite(r)&&r>m;
    internal static bool Blink(double time)=>double.IsFinite(time)&&(long)Math.Floor(time*4)%2==0;
    // NATO mil (2*pi/6400 rad) in reference pixels at a 1080-high canvas.
    internal const double MilRadians=Math.PI*2/6400;
    internal const double ReferenceHeight=1080,FallbackFieldOfView=10,MinPixelsPerMil=2,MaxPixelsPerMil=30;
    internal static double PixelsPerMil(double verticalFovDegrees)
    {
        var fov=double.IsFinite(verticalFovDegrees)&&verticalFovDegrees>.1&&verticalFovDegrees<170?verticalFovDegrees:FallbackFieldOfView;
        return Math.Clamp(ReferenceHeight/2/Math.Tan(fov*Math.PI/360)*MilRadians,MinPixelsPerMil,MaxPixelsPerMil);
    }
    // Analog stadia: the angle of a target of this height at a range, in mils.
    internal const double StadiaTargetHeight=2.4;
    internal static readonly double[] StadiaRanges={500,1000,1500,2000,3000};
    internal static double StadiaMils(double range)=>StadiaTargetHeight/range/MilRadians;
}

// Counts launches by watching the native last-fire time of each tube; the first
// sample of a tube is only a baseline, so opening the sight never starts the clock.
internal sealed class AtgmLaunchClock
{
    private readonly Dictionary<IntPtr,float> seen=new();
    private double? launchedAt;
    internal void Observe(IntPtr tube,float lastFireTime,double now)
    {
        if(!float.IsFinite(lastFireTime))return;
        if(seen.TryGetValue(tube,out var previous)&&lastFireTime>previous)launchedAt=now;
        seen[tube]=lastFireTime;
    }
    internal double? Seconds(double now)=>launchedAt is {} t&&now>=t?now-t:null;
    internal void Reset(){seen.Clear();launchedAt=null;}
}

// The aim object the native scope mask follows (EB's own transform). Its pose is
// the guidance line: origin plus the normalized direction, an up that is never
// parallel to it. The native mask point is position + forward x convergence.
internal readonly record struct AtgmMaskPose(Vector3 Position,Vector3 Forward,Vector3 Up);
internal static class AtgmMaskAim
{
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    internal static AtgmMaskPose? Pose(Vector3 origin,Vector3 direction,Vector3 up)
    {
        if(!Finite(origin)||!Finite(direction)||direction.LengthSquared()<.25f)return null;
        var forward=Vector3.Normalize(direction);
        Vector3 Usable(Vector3 u)=>Finite(u)&&u.LengthSquared()>.25f&&MathF.Abs(Vector3.Dot(Vector3.Normalize(u),forward))<.999f?Vector3.Normalize(u):Vector3.Zero;
        var chosen=Usable(up);
        if(chosen==Vector3.Zero)chosen=Usable(Vector3.UnitY);
        if(chosen==Vector3.Zero)chosen=Usable(Vector3.UnitZ);
        return new(origin,forward,chosen);
    }
    internal static Vector3 MaskPoint(AtgmMaskPose pose,float convergence)=>pose.Position+pose.Forward*(convergence>1?convergence:300);
}

// Hidden mode: who switched which native scope mask off, and its exact old value.
internal interface IAtgmMaskHandle{IntPtr Key{get;} bool Alive{get;} bool Active{get;set;}}
internal sealed class AtgmScopeMaskLedger
{
    private sealed class Entry{internal IntPtr Owner;internal IAtgmMaskHandle Handle=null!;internal bool Original;}
    private readonly Dictionary<IntPtr,Entry> hidden=new();
    internal int Count=>hidden.Count;
    internal bool Hides(IntPtr key)=>hidden.ContainsKey(key);
    // Per frame while the owner's session is the active sight. Only a mask that is
    // on is switched off; one that something else already switched off is left alone.
    internal void Apply(IntPtr owner,IAtgmMaskHandle? handle,bool hide)
    {
        if(handle==null||!handle.Alive)return;
        if(!hide){Restore(handle.Key);return;}
        if(!handle.Active)return;
        hidden[handle.Key]=new(){Owner=owner,Handle=handle,Original=true};
        handle.Active=false;
    }
    // Exact restore: only while the mask still holds the value EB wrote.
    internal bool Restore(IntPtr key)
    {
        if(!hidden.Remove(key,out var e))return false;
        try{if(e.Handle.Alive&&!e.Handle.Active){e.Handle.Active=e.Original;return true;}}catch{}
        return false;
    }
    internal int End(IntPtr owner)=>hidden.Where(p=>p.Value.Owner==owner).Select(p=>p.Key).ToArray().Count(Restore);
    // Safety net: anything hidden for an owner that is not the active sight now
    // (or while Hidden is no longer wanted) comes back.
    internal int SafetyNet(IntPtr activeOwner,bool hideWanted)=>
        hidden.Where(p=>!hideWanted||activeOwner==IntPtr.Zero||p.Value.Owner!=activeOwner||!p.Value.Handle.Alive).Select(p=>p.Key).ToArray().Count(Restore);
    internal int RestoreAll()=>hidden.Keys.ToArray().Count(Restore);
}

// Layout in reference pixels (1920 x 1080, scaled with the screen height),
// origin at the screen centre, +Y up. The runtime and the preview draw the same items.
internal readonly record struct AtgmHudColor(float R,float G,float B,float A);
internal enum AtgmHudKind{Line,Panel,Lamp,Text}
internal sealed record AtgmHudItem(string Id,AtgmHudKind Kind,float X,float Y,float W,float H,AtgmHudColor Color,string Text="",float Size=0,int Align=0,bool Round=false,string Element="");
internal sealed record AtgmHudLayout(AtgmHudStyle Style,double PixelsPerMil,IReadOnlyList<AtgmHudItem> Items,AtgmHudColor Lit,AtgmHudColor Dim,AtgmHudColor GuidanceLit)
{
    internal string Signature=>$"{Style}|{PixelsPerMil:0.00}|{string.Join(",",Items.Select(i=>i.Id))}";
}
internal sealed record AtgmHudFrame(IReadOnlyDictionary<string,string> Texts,IReadOnlyDictionary<string,bool> Lamps);

internal static class AtgmSightHudLayout
{
    internal static readonly AtgmHudColor Green=new(.45f,1f,.5f,.95f),GreenDim=new(.45f,1f,.5f,.22f),GreenLabel=new(.45f,1f,.5f,.6f),Backing=new(0,0,0,.35f);
    internal static readonly AtgmHudColor Graticule=new(.06f,.06f,.06f,.9f),Amber=new(1f,.62f,.12f,1f),AmberDim=new(.35f,.24f,.1f,.9f),LampGreen=new(.4f,1f,.45f,1f),Housing=new(.05f,.05f,.05f,.85f),Engraving=new(.78f,.78f,.74f,.9f);
    internal const float Line=2;
    internal static AtgmHudLayout Build(AtgmHudStyle style,AtgmSightHudOptions options,double pixelsPerMil)
    {
        var ppm=Math.Clamp(double.IsFinite(pixelsPerMil)?pixelsPerMil:AtgmSightHudRules.PixelsPerMil(AtgmSightHudRules.FallbackFieldOfView),AtgmSightHudRules.MinPixelsPerMil,AtgmSightHudRules.MaxPixelsPerMil);
        var items=new List<AtgmHudItem>();
        if(style==AtgmHudStyle.Digital)Digital(items,options,ppm);
        else if(style==AtgmHudStyle.Analog)Analog(items,options,ppm);
        return style==AtgmHudStyle.Analog?new(style,ppm,items,Amber,AmberDim,LampGreen):new(style,ppm,items,Green,GreenDim,Green);
    }
    private static void H(List<AtgmHudItem> items,string id,float x0,float x1,float y,AtgmHudColor c,float w=Line)=>items.Add(new(id,AtgmHudKind.Line,(x0+x1)/2,y,Math.Abs(x1-x0),w,c,Element:"reticle"));
    private static void V(List<AtgmHudItem> items,string id,float x,float y0,float y1,AtgmHudColor c,float w=Line)=>items.Add(new(id,AtgmHudKind.Line,x,(y0+y1)/2,w,Math.Abs(y1-y0),c,Element:"reticle"));
    // Ticks along an arm from the centre gap outward; minor ones only where they stay readable.
    private static void Ticks(List<AtgmHudItem> items,string id,double ppm,float gap,float arm,int minor,int major,float minorLength,float majorLength,AtgmHudColor c,bool vertical,int sign)
    {
        var step=minor*ppm>=8?minor:major;
        for(int mil=step,n=0;mil*ppm<=arm&&n<200;mil+=step,n++)
        {
            var at=(float)(mil*ppm);if(at<=gap)continue;
            var length=mil%major==0?majorLength:minorLength;
            if(vertical)H(items,$"{id}{mil}",-length/2,length/2,-at,c);
            else{V(items,$"{id}{(sign>0?"+":"-")}{mil}",sign*at,0,-length,c);}
        }
    }
    private static void Digital(List<AtgmHudItem> items,AtgmSightHudOptions o,double ppm)
    {
        if(o.Reticle)
        {
            const float gap=16,arm=360,up=200,down=260;
            H(items,"cross-left",-arm,-gap,0,Green);H(items,"cross-right",gap,arm,0,Green);
            V(items,"cross-up",0,gap,up,Green);V(items,"cross-down",0,-down,-gap,Green);
            Ticks(items,"tick-h",ppm,gap,arm,2,10,8,16,Green,false,1);Ticks(items,"tick-h",ppm,gap,arm,2,10,8,16,Green,false,-1);
            Ticks(items,"tick-v",ppm,gap,down,5,10,10,20,Green,true,-1);
        }
        bool range=o.RangeReadout,timer=o.FlightTimer,lamps=o.StatusLamps;
        if(range||timer||lamps)items.Add(new("backing",AtgmHudKind.Panel,0,-452,900,136,Backing,Element:"panel"));
        if(range)
        {
            items.Add(new("range-label",AtgmHudKind.Text,0,-399,0,0,GreenLabel,"RANGE  M",15,0,Element:"range"));
            items.Add(new("range",AtgmHudKind.Text,0,-432,0,0,Green,"----",42,0,Element:"range"));
        }
        const float row=-490;
        void Lamp(string id,float x,string text,string element)
        {
            items.Add(new(id+"-lamp",AtgmHudKind.Lamp,x,row,12,12,GreenDim,Element:element));
            items.Add(new(id,AtgmHudKind.Text,x+14,row,0,0,GreenDim,text,20,-1,Element:element));
        }
        if(lamps){Lamp("rdy",-380,AtgmSightHudRules.ReadyText(0),"lamps");Lamp("lchr",-225,AtgmSightHudRules.LauncherText(null),"lamps");Lamp("track",-55,"TRACK","lamps");}
        if(timer)Lamp("flt",80,AtgmSightHudRules.FlightText(null),"timer");
        if(lamps)Lamp("maxrng",250,"MAX RNG","lamps");
    }
    private static void Analog(List<AtgmHudItem> items,AtgmSightHudOptions o,double ppm)
    {
        if(o.Reticle)
        {
            const float gap=6,arm=560,up=300,down=420;
            H(items,"cross-left",-arm,-gap,0,Graticule);H(items,"cross-right",gap,arm,0,Graticule);
            V(items,"cross-up",0,gap,up,Graticule);V(items,"cross-down",0,-down,-gap,Graticule);
            Ticks(items,"tick-h",ppm,gap,arm,5,10,10,18,Graticule,false,1);Ticks(items,"tick-h",ppm,gap,arm,5,10,10,18,Graticule,false,-1);
            // Stadia: put the base line on the target's foot; the bar that reaches its
            // top gives the range. One bar per range, nearest on the left, no figures.
            const float baseY=-90,x0=70;
            H(items,"stadia-base",x0,x0+250,baseY,Graticule);
            var columns=new[]{110f,150f,190f,230f,290f};
            for(int i=0;i<AtgmSightHudRules.StadiaRanges.Length;i++)
            {
                var height=(float)(AtgmSightHudRules.StadiaMils(AtgmSightHudRules.StadiaRanges[i])*ppm);
                V(items,$"stadia-{AtgmSightHudRules.StadiaRanges[i]:0}",columns[i],baseY,baseY+height,Graticule);
                H(items,$"stadia-cap-{AtgmSightHudRules.StadiaRanges[i]:0}",columns[i]-5,columns[i]+5,baseY+height,Graticule);
            }
        }
        if(o.StatusLamps)
        {
            items.Add(new("housing",AtgmHudKind.Panel,800,-470,170,92,Housing,Element:"lamps"));
            items.Add(new("ready-lamp",AtgmHudKind.Lamp,760,-458,20,20,AmberDim,Round:true,Element:"lamps"));
            items.Add(new("guidance-lamp",AtgmHudKind.Lamp,840,-458,20,20,AmberDim,Round:true,Element:"lamps"));
            items.Add(new("ready-label",AtgmHudKind.Text,760,-492,0,0,Engraving,"READY",12,0,Element:"lamps"));
            items.Add(new("guidance-label",AtgmHudKind.Text,840,-492,0,0,Engraving,"GUIDANCE",12,0,Element:"lamps"));
        }
    }
    // What changes per frame: texts and which lamps are lit.
    internal static AtgmHudFrame Frame(AtgmHudLayout layout,AtgmHudState s)
    {
        var texts=new Dictionary<string,string>();var lamps=new Dictionary<string,bool>();
        bool flying=AtgmSightHudRules.InFlight(s.FlightSeconds,s.GuidanceSeconds),near=AtgmSightHudRules.NearLimit(s.FlightSeconds,s.GuidanceSeconds),blink=AtgmSightHudRules.Blink(s.Time);
        if(layout.Style==AtgmHudStyle.Digital)
        {
            texts["range"]=AtgmSightHudRules.RangeText(s.RangeM);
            texts["rdy"]=AtgmSightHudRules.ReadyText(s.RoundsReady);lamps["rdy"]=s.RoundsReady>0;
            texts["lchr"]=AtgmSightHudRules.LauncherText(s.LauncherUp);lamps["lchr"]=s.LauncherUp==true;
            lamps["track"]=flying;
            texts["flt"]=AtgmSightHudRules.FlightText(flying?s.FlightSeconds:null);lamps["flt"]=flying;
            lamps["maxrng"]=near?blink:!flying&&AtgmSightHudRules.OutOfRange(s.RangeM,s.MaxRangeM);
        }
        else if(layout.Style==AtgmHudStyle.Analog)
        {
            lamps["ready"]=s.RoundsReady>0;
            lamps["guidance"]=flying&&(!near||blink);
        }
        return new(texts,lamps);
    }
}
