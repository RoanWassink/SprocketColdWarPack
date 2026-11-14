using System.Text.Json;
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
    internal bool Playing {get;private set;}
    internal int Omitted {get;private set;}
    internal List<CombatReportShot> Shots {get;}=new();
    internal void Begin(){if(Playing)return;Playing=true;Shots.Clear();Omitted=0;}
    internal void End()=>Playing=false;
    internal void Restore(IEnumerable<CombatReportShot> shots,int omitted){if(Playing)return;var list=shots.ToArray();var clean=list.Where(s=>s!=null).TakeLast(MaximumShots).Select(CombatReportTruth.Normalize).ToArray();Shots.Clear();Shots.AddRange(clean);Omitted=CombatReportTruth.Count(omitted,list.Length-clean.Length);}
    internal void Add(CombatReportShot shot){if(!Playing)return;Shots.Add(CombatReportTruth.Normalize(shot));if(Shots.Count>MaximumShots){Shots.RemoveAt(0);Omitted=CombatReportTruth.Count(Omitted,1);}}
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

