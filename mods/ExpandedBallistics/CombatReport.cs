using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
namespace SprocketExpandedBallistics;
internal sealed record CombatLayerPart(int Entity,string Name,string? MaterialId,string? MaterialLabel,double? ThicknessMm,double? AngleDegrees,bool Armour);
internal sealed record CombatLayerInfo(string Id,IReadOnlyList<CombatLayerPart> Parts,bool Combined,double? AirGapMm=null);
internal sealed record CombatReportEvent(string Kind,string Stage,string Text,double? BeforeMm=null,double? AfterMm=null,string? HitId=null,CombatLayerInfo? Layer=null,int? RelatedEntity=null,int Occurrences=1)
{
    public PaidMaterialPiece[]? PaidIntervals {get;init;}
}
/// <summary>One impact. <c>Target</c> is the blueprint name of the vehicle that was hit and <c>CrewTotal</c>/<c>CrewAlive</c> its crew after the shot (0.22.6B); all three are empty when the game did not say.</summary>
internal sealed record CombatReportShot(int Number,string Direction,string Profile,double? SpeedMps,IReadOnlyList<CombatReportEvent> Events,int OmittedEvents=0,double? InitialPenetrationMm=null,string? Target=null,int? CrewTotal=null,int? CrewAlive=null,string? Shooter=null);
internal sealed class CombatReportHistory
{
    internal const int MaximumShots=128;
    internal const int MaximumBlastMappings=16;
    internal bool Playing {get;private set;}
    internal int Omitted {get;private set;}
    internal int Revision {get;private set;}
    internal List<CombatReportShot> Shots {get;}=new();
    private readonly Dictionary<int,int> blastSeqToShotNumber=new();
    private readonly Queue<int> blastSeqOrder=new();
    internal void Begin(){if(Playing)return;Playing=true;Shots.Clear();Omitted=0;ClearBlastMappings();Revision++;}
    internal void End()=>Playing=false;
    internal void ClearBlastMappings(){blastSeqToShotNumber.Clear();blastSeqOrder.Clear();}
    internal void RegisterBlastSeq(int seq,int shotNumber)
    {
        if(seq<=0||shotNumber<=0)return;
        if(blastSeqToShotNumber.ContainsKey(seq)){blastSeqToShotNumber[seq]=shotNumber;return;}
        while(blastSeqOrder.Count>=MaximumBlastMappings){var oldest=blastSeqOrder.Dequeue();blastSeqToShotNumber.Remove(oldest);}
        blastSeqToShotNumber[seq]=shotNumber;blastSeqOrder.Enqueue(seq);
    }
    internal bool TryFindShotIndexForBlast(int seq,out int index)
    {
        index=-1;
        if(seq<=0||Shots.Count==0)return false;
        if(blastSeqToShotNumber.TryGetValue(seq,out var shotNumber))
        {
            for(int i=Shots.Count-1;i>=0;i--)if(Shots[i].Number==shotNumber){index=i;return true;}
            return false;
        }
        var last=Shots[^1];
        if(CombatBlastReport.IsHeOrHeshShot(last)){index=Shots.Count-1;return true;}
        return false;
    }
    internal CombatReportShot? ApplyBlastEvent(int seq,CombatReportEvent evt,int? crewAliveAfter=null,int? crewTotal=null)
    {
        if(evt==null||!TryFindShotIndexForBlast(seq,out var idx))return null;
        var existing=Shots[idx];
        var events=existing.Events?.ToList()??new List<CombatReportEvent>();
        bool changed=false;
        if(!CombatBlastReport.HasEvent(events,evt)&&events.Count<256){events.Add(evt);changed=true;}
        int? nextTotal=existing.CrewTotal,nextAlive=existing.CrewAlive;
        if(crewTotal is >0 and <=256)
        {
            int ct=crewTotal.Value;
            int ca=Math.Clamp(crewAliveAfter??ct,0,ct);
            int mergedAlive=nextAlive is {} prev?Math.Min(prev,ca):ca;
            mergedAlive=Math.Clamp(mergedAlive,0,ct);
            if(nextTotal!=ct||nextAlive!=mergedAlive){nextTotal=ct;nextAlive=mergedAlive;changed=true;}
        }
        if(!changed)return null;
        var updated=CombatReportTruth.Normalize(existing with{Events=events,CrewTotal=nextTotal,CrewAlive=nextAlive});
        Shots[idx]=updated;
        Revision++;
        return updated;
    }
    internal void Restore(IEnumerable<CombatReportShot> shots,int omitted){if(Playing)return;var list=shots.ToArray();var clean=list.Where(s=>s!=null).TakeLast(MaximumShots).Select(CombatReportTruth.Normalize).ToArray();Shots.Clear();Shots.AddRange(clean);Omitted=CombatReportTruth.Count(omitted,list.Length-clean.Length);ClearBlastMappings();Revision++;}
    internal void Add(CombatReportShot shot){if(!Playing)return;Shots.Add(CombatReportTruth.Normalize(shot));if(Shots.Count>MaximumShots){Shots.RemoveAt(0);Omitted=CombatReportTruth.Count(Omitted,1);}Revision++;}
}
internal readonly record struct ParsedBlastPlate(int PlateVuid,bool IsSolidBlock,double ThicknessMm,bool Breached,double HoleDiameterM,double CapacityMm);
internal readonly record struct ParsedBlastEra(int Count,double RadiusM);
internal readonly record struct ParsedBlastSummary(string Kind,double CalibreMm,double Power,double BreachCapacityMm);
internal sealed record BlastReportData(
    bool IsBlastShot,
    bool IsPureBlastDetonation,
    string Kind,
    double? CalibreMm,
    double? Power,
    double? BreachCapacityMm,
    double? ShockPercent,
    bool Breached,
    IReadOnlyList<ParsedBlastPlate> Plates,
    IReadOnlyList<ParsedBlastEra> StrippedEra,
    string PrimaryPlateName,
    string PrimaryMaterial,
    string PrimaryMaterialColour,
    double? PrimaryThicknessMm,
    double? PrimaryObliquityDeg,
    bool PrimaryIsSolidBlock,
    double? HoleDiameterM,
    int TotalEraStripped,
    double? EraStripRadiusM);
internal static class CombatBlastReport
{
    private static readonly CultureInfo C=CultureInfo.InvariantCulture;
    private static readonly Regex SummaryRegex=new(@"^(HE|HESH)\s+blast:\s*([\d.]+)\s*mm\s*·\s*power\s*([\d.]+)\s*·\s*RHA\s+breach\s+(?:budget|capacity)\s*([\d.]+)\s*mm$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex PlateRegex=new(@"^Plate #(\d+)( \[Solid block\])? \(([\d.]+) mm\): (?:BREACHED — blast hole Ø ([\d.]+) m|resisted blast — dent only) \(capacity ([\d.]+) mm\)$",RegexOptions.CultureInvariant);
    private static readonly Regex EraRegex=new(@"^Blast stripped (\d+) ERA blocks? within ([\d.]+) m$",RegexOptions.CultureInvariant);
    private static readonly Regex ShockRegex=new(@"shock(?: of)?\s+([\d.]+)\s*%",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static double Safe(double value)=>double.IsFinite(value)&&value>=0?value:0;

    internal static string SummaryText(string kind,double calibreMm,double power,double breachMm)
    {
        var label=string.Equals(kind,"hesh",StringComparison.OrdinalIgnoreCase)?"HESH":"HE";
        return string.Create(C,$"{label} blast: {Safe(calibreMm):0.#} mm · power {Safe(power):0.#} · RHA breach budget {Safe(breachMm):0.#} mm");
    }
    internal static CombatReportEvent SummaryEvent(string kind,double calibreMm,double power,double breachMm,string stage="")
        =>new("blastSummary",stage,SummaryText(kind,calibreMm,power,breachMm),CombatReportTruth.Value(breachMm));

    internal static CombatReportEvent PlateEvent(int plateVuid,double localThicknessMm,double breachCapacityMm,bool breached,double holeRadiusM,bool isSolidBlock,bool isDirect)
    {
        double thick=Safe(localThicknessMm),cap=Safe(breachCapacityMm),diam=Safe(holeRadiusM)*2.0;
        var solid=isSolidBlock?" [Solid block]":"";
        var text=breached
            ?string.Create(C,$"Plate #{plateVuid}{solid} ({thick:0.#} mm): BREACHED — blast hole Ø {diam:0.00} m (capacity {cap:0.#} mm)")
            :string.Create(C,$"Plate #{plateVuid}{solid} ({thick:0.#} mm): resisted blast — dent only (capacity {cap:0.#} mm)");
        return new("blastPlate","",text,CombatReportTruth.Value(cap),breached?CombatReportTruth.Value(Math.Max(0,cap-thick)):0,RelatedEntity:plateVuid);
    }

    internal static CombatReportEvent? EraStrippedEvent(int count,double radiusM)
    {
        if(count<=0)return null;
        double r=Safe(radiusM);
        return new("blastEra","",string.Create(C,$"Blast stripped {count} ERA block{(count==1?"":"s")} within {r:0.00} m"));
    }

    internal static string BreachModuleName(string? componentType)
    {
        var raw=(componentType??"").Trim();
        if(raw.Length==0)return "module";
        return raw.ToLowerInvariant() switch
        {
            "cannon"=>"main gun",
            "layingdrive"=>"laying drive",
            "traversemotor"=>"motor",
            "fueltank"=>"fuel tank",
            "ammorack"=>"ammunition stowage",
            "combustionengine"=>"engine",
            "transmissionblock"=>"transmission",
            _=>CombatDamageSummary.PartLabel(CombatReportLanguage.Module(raw))
        };
    }

    internal static CombatReportEvent BreachDamageEvent(int entityVuid,string componentType,bool alive)
    {
        var text=string.Equals(componentType?.Trim(),"CrewSeat",StringComparison.OrdinalIgnoreCase)
            ?string.Create(C,$"Crew member #{entityVuid} [breach overpressure] {(alive?"wounded":"killed")}")
            :string.Create(C,$"{BreachModuleName(componentType)} [breach overpressure] {(alive?"damaged":"destroyed")}");
        return new("interaction","",text,RelatedEntity:entityVuid);
    }

    internal static bool IsHeOrHeshProfile(string? profile)
    {
        if(string.IsNullOrWhiteSpace(profile))return false;
        var t=profile.Trim();
        return Regex.IsMatch(t,@"(?:^|\b)(?:HE|HESH|HE-FRAG)(?:\b|$)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    }
    internal static bool IsHeOrHeshShot(CombatReportShot shot)=>
        IsHeOrHeshProfile(shot.Profile)||(shot.Events?.Any(e=>e?.Kind is "blastSummary" or "blastPlate" or "blastEra")??false);

    internal static bool HasEvent(IReadOnlyList<CombatReportEvent> events,CombatReportEvent candidate)
    {
        for(int i=0;i<events.Count;i++)
        {
            var e=events[i];
            if(e!=null&&e.Kind==candidate.Kind&&e.RelatedEntity==candidate.RelatedEntity&&string.Equals(e.Text,candidate.Text,StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    internal static bool TryParsePlate(CombatReportEvent? e,out ParsedBlastPlate parsed)
    {
        parsed=default;
        if(e?.Kind!="blastPlate"||string.IsNullOrEmpty(e.Text))return false;
        var m=PlateRegex.Match(e.Text);
        if(!m.Success)return false;
        int vuid=int.Parse(m.Groups[1].Value,C);
        bool solid=m.Groups[2].Success;
        double thick=double.Parse(m.Groups[3].Value,C);
        bool breached=m.Groups[4].Success;
        double diam=breached?double.Parse(m.Groups[4].Value,C):0;
        double cap=double.Parse(m.Groups[5].Value,C);
        parsed=new(vuid,solid,thick,breached,diam,cap);
        return true;
    }

    internal static bool TryParseEra(CombatReportEvent? e,out ParsedBlastEra parsed)
    {
        parsed=default;
        if(e?.Kind!="blastEra"||string.IsNullOrEmpty(e.Text))return false;
        var m=EraRegex.Match(e.Text);
        if(!m.Success)return false;
        parsed=new(int.Parse(m.Groups[1].Value,C),double.Parse(m.Groups[2].Value,C));
        return true;
    }

    internal static bool TryParseSummary(CombatReportEvent? e,out ParsedBlastSummary parsed)
    {
        parsed=default;
        if(e?.Kind!="blastSummary"||string.IsNullOrEmpty(e.Text))return false;
        var m=SummaryRegex.Match(e.Text.Trim());
        if(!m.Success)return false;
        parsed=new(
            m.Groups[1].Value.ToUpperInvariant(),
            double.Parse(m.Groups[2].Value,C),
            double.Parse(m.Groups[3].Value,C),
            double.Parse(m.Groups[4].Value,C));
        return true;
    }

    internal static BlastReportData? Extract(CombatReportShot shot,IReadOnlyList<ForensicLayer>? layers=null,bool envelopePerforated=false)
    {
        var events=shot.Events??Array.Empty<CombatReportEvent>();
        ParsedBlastSummary? summary=null;
        var plates=new List<ParsedBlastPlate>();
        var eras=new List<ParsedBlastEra>();
        double? shockPct=null;
        foreach(var e in events)
        {
            if(e==null)continue;
            if(summary==null&&TryParseSummary(e,out var s))summary=s;
            else if(TryParsePlate(e,out var p))plates.Add(p);
            else if(TryParseEra(e,out var er))eras.Add(er);
            if(shockPct==null&&e.Kind is "materialNote" or "interaction"&&!string.IsNullOrEmpty(e.Text))
            {
                var sm=ShockRegex.Match(e.Text);
                if(sm.Success&&double.TryParse(sm.Groups[1].Value,NumberStyles.Float,C,out var sp))shockPct=sp;
            }
        }
        bool hasBlastEvents=summary!=null||plates.Count>0||eras.Count>0;
        bool outsideBlast=HeReportText.OutsideBlast(events);
        if(!hasBlastEvents&&!outsideBlast)return null;

        bool isHesh=summary?.Kind=="HESH"||shot.Profile.Contains("HESH",StringComparison.OrdinalIgnoreCase);
        string kind=isHesh?"HESH":"HE";
        bool breached=plates.Any(p=>p.Breached);
        var primaryParsed=plates.FirstOrDefault(p=>p.Breached);
        if(primaryParsed.PlateVuid==0&&plates.Count>0)primaryParsed=plates[0];

        // Resolve primary armour plate geometry from measured layers when available.
        CombatLayerPart? primaryPart=null;
        string primaryColour="#8095a6";
        if(layers is {Count:>0})
        {
            foreach(var l in layers)
            {
                if(l.GapMm!=null)continue;
                var match=primaryParsed.PlateVuid>0
                    ?l.Parts.FirstOrDefault(pt=>pt.Armour&&pt.Entity==primaryParsed.PlateVuid)
                    :null;
                match??=l.Parts.FirstOrDefault(pt=>pt.Armour);
                if(match!=null){primaryPart=match;primaryColour=l.Colour;break;}
            }
        }
        else
        {
            foreach(var e in events)
            {
                if(e?.Kind!="layerStart"||e.Layer==null||e.Layer.AirGapMm!=null)continue;
                var match=primaryParsed.PlateVuid>0
                    ?e.Layer.Parts.FirstOrDefault(pt=>pt.Armour&&pt.Entity==primaryParsed.PlateVuid)
                    :null;
                match??=e.Layer.Parts.FirstOrDefault(pt=>pt.Armour);
                if(match!=null){primaryPart=match;primaryColour=CombatForensics.MaterialColour(match.MaterialId);break;}
            }
        }

        int armourLayerCount=layers!=null
            ?layers.Count(l=>l.GapMm==null&&!l.Internal)
            :events.Count(e=>e?.Kind=="layerStart"&&e.Layer!=null&&e.Layer.AirGapMm==null&&e.Layer.Parts.Any(p=>p.Armour));
        bool pureBlast=hasBlastEvents&&!envelopePerforated&&armourLayerCount<=1;

        string plateName=primaryPart!=null
            ?CombatReportView.PlateName(primaryPart.Name,primaryPart.Entity)
            :primaryParsed.PlateVuid>0?$"Plate #{primaryParsed.PlateVuid}":"Armour plate";
        bool isSolid=primaryParsed.IsSolidBlock||plateName.Contains("[Solid block]",StringComparison.OrdinalIgnoreCase);
        string material=primaryPart!=null?CombatForensics.PlateMaterial(primaryPart):(isSolid?"Solid composite armour":"Rolled Homogeneous Armour");
        double? thick=primaryParsed.ThicknessMm>0?primaryParsed.ThicknessMm:primaryPart?.ThicknessMm;
        double? angle=primaryPart?.AngleDegrees;
        double? capacity=summary?.BreachCapacityMm??(primaryParsed.CapacityMm>0?primaryParsed.CapacityMm:shot.InitialPenetrationMm);
        double? holeDiam=breached&&primaryParsed.HoleDiameterM>0?primaryParsed.HoleDiameterM:null;
        int totalEra=eras.Sum(x=>x.Count);
        double? eraRadius=eras.Count>0?eras.Max(x=>x.RadiusM):null;

        return new BlastReportData(
            IsBlastShot:true,
            IsPureBlastDetonation:pureBlast,
            Kind:kind,
            CalibreMm:summary?.CalibreMm,
            Power:summary?.Power,
            BreachCapacityMm:capacity,
            ShockPercent:shockPct,
            Breached:breached,
            Plates:plates,
            StrippedEra:eras,
            PrimaryPlateName:plateName,
            PrimaryMaterial:material,
            PrimaryMaterialColour:primaryColour,
            PrimaryThicknessMm:thick,
            PrimaryObliquityDeg:angle,
            PrimaryIsSolidBlock:isSolid,
            HoleDiameterM:holeDiam,
            TotalEraStripped:totalEra,
            EraStripRadiusM:eraRadius);
    }
}
internal static class CombatReportTruth
{
    internal static double? Value(double? value)=>value is {} v&&double.IsFinite(v)&&v>=0?v:null;
    internal static int Count(int existing,int added)=>(int)Math.Min(int.MaxValue,(long)Math.Max(0,existing)+Math.Max(0,added));
    internal static string Text(string? value,string fallback){var clean=new string((value??"").Where(c=>!char.IsControl(c)).Take(512).ToArray()).Trim();return clean.Length==0?fallback:clean;}
    internal static string EventText(string? value){var clean=new string((value??"").Where(c=>!char.IsControl(c)||c=='\n').Take(512).ToArray()).Trim();return clean.Length==0?"Unknown interaction":clean;}
    internal static CombatReportShot Normalize(CombatReportShot shot)
    {
        var all=CombatReportNoise.Collapse(shot.Events??Array.Empty<CombatReportEvent>());var events=all.Where(e=>e!=null).Take(256).Select(e=>e with {Kind=Text(e.Kind,"interaction"),Stage=Text(e.Stage,""),Text=EventText(e.Text),BeforeMm=Value(e.BeforeMm),AfterMm=Value(e.AfterMm),HitId=string.IsNullOrWhiteSpace(e.HitId)?null:Text(e.HitId,""),Layer=CleanLayer(e.Layer)}).ToArray();
        return shot with {Number=Math.Max(0,shot.Number),Direction=Text(shot.Direction,"Unknown direction"),Profile=Text(shot.Profile,"Unknown shell"),SpeedMps=Value(shot.SpeedMps),InitialPenetrationMm=Value(shot.InitialPenetrationMm),Shooter=Text(shot.Shooter,"unknown"),Target=string.IsNullOrWhiteSpace(shot.Target)?null:Text(shot.Target,""),CrewTotal=shot.CrewTotal is >=0 and <=256?shot.CrewTotal:null,CrewAlive=shot.CrewTotal is >=0 and <=256&&shot.CrewAlive is >=0 and <=256&&shot.CrewAlive<=shot.CrewTotal?shot.CrewAlive:null,Events=events,OmittedEvents=Count(shot.OmittedEvents,all.Count-events.Length)};
    }
    private static CombatLayerInfo? CleanLayer(CombatLayerInfo? layer)=>layer==null?null:layer with
    {
        Id=Text(layer.Id,"unknown"),Parts=(layer.Parts??Array.Empty<CombatLayerPart>()).Where(p=>p!=null).Take(32).Select(p=>p with
        {Name=Text(p.Name,"Layer"),MaterialId=p.MaterialId==null?null:Text(p.MaterialId,""),MaterialLabel=p.MaterialLabel==null?null:Text(p.MaterialLabel,""),ThicknessMm=Value(p.ThicknessMm),AngleDegrees=p.AngleDegrees is >=0 and <=90?Value(p.AngleDegrees):null}).ToArray(),
        AirGapMm=layer.AirGapMm is >=0?Value(layer.AirGapMm):null
    };
    internal static bool LaunchMatches(int id,float spawn,string definition,int recordedId,float recordedSpawn,string recordedDefinition)=>id==recordedId&&spawn==recordedSpawn&&definition==recordedDefinition;
    internal static string Direction(IntPtr player,IntPtr shooter,IntPtr target)=>player==IntPtr.Zero?"Unknown direction":target==player?(shooter==player?"Own vehicle self-hit":"Incoming"):shooter==player?"Outgoing":shooter==IntPtr.Zero||target==IntPtr.Zero?"Unknown direction":"Other vehicles";
}

