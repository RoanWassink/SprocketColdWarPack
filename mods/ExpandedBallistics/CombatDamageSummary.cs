using System.Text;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

internal sealed record CombatDamageGroup(
    string Label,
    int CrewKilled,
    int CrewWounded,
    IReadOnlyList<string> KilledRoles,
    IReadOnlyList<string> WoundedRoles,
    int KilledUnnamed,
    int WoundedUnnamed,
    IReadOnlyList<string> PartsDestroyed,
    IReadOnlyList<string> PartsDamaged)
{
    internal bool Any=>CrewKilled>0||CrewWounded>0||PartsDestroyed.Count>0||PartsDamaged.Count>0;
}

/// <summary>
/// Crew and part damage of one impact as a readable line, from its "interaction" events (0.22.6B).
/// 0.22.112: HE/HESH cause tags ([breach overpressure], [shock X %], [exterior], [interior]) are grouped once per
/// cause instead of repeating the bracketed tag on every module and inside crew role parentheses.
/// Untagged events (AP/HEAT/APFSDS/APHE) keep their exact 0.22.7 output.
/// </summary>
internal sealed class CombatDamageSummary
{
    internal int CrewKilled{get;private set;}
    internal int CrewWounded{get;private set;}
    internal IReadOnlyList<string> KilledRoles=>killedRoles;
    internal IReadOnlyList<string> WoundedRoles=>woundedRoles;
    internal IReadOnlyList<string> PartsDestroyed=>destroyed;
    internal IReadOnlyList<string> PartsDamaged=>damaged;
    internal IReadOnlyList<CombatDamageGroup> Groups=>groups;
    internal bool HasCauseGroups=>groups.Count>0&&groups.Any(g=>g.Label.Length>0);
    internal bool VehicleDestroyedEvent{get;private set;}
    internal bool Any=>CrewKilled>0||CrewWounded>0||destroyed.Count>0||damaged.Count>0;
    private readonly List<string> killedRoles=new(),woundedRoles=new(),destroyed=new(),damaged=new();
    private readonly List<CombatDamageGroup> groups=new();
    private int killedUnnamed,woundedUnnamed;

    private static readonly Regex Anonymous=new(@"^(#?\d+|(crew( member)?|seat|crew seat|unnamed( structure)?)(\s*#?\d+)?)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex VehicleLine=new(@"^(vehicle|tank)\b.*\bdestroyed$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex CamelCase=new("(?<=[a-z0-9])([A-Z])",RegexOptions.CultureInvariant);
    private static readonly Regex ShockTag=new(@"^\[shock\s+([\d.]+)\s*%\]$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);

    /// <summary>"Crew member Crew member #12" -> "#12"; "Crew member Gunner" -> "Gunner".</summary>
    internal static string CrewLabel(string label)
    {
        var t=(label??"").Trim();
        while(t.StartsWith("Crew member ",StringComparison.OrdinalIgnoreCase))t=t["Crew member ".Length..].Trim();
        return t;
    }
    internal static bool IsAnonymous(string label)=>label.Length==0||Anonymous.IsMatch(label);

    internal static (string Clean,string GroupLabel,int GroupOrder) SplitCauseTag(string text)
    {
        var t=(text??"").Trim();
        int bracket=t.LastIndexOf('[');
        if(bracket<=0||!t.EndsWith(']'))return (t,"",4);
        var rawTag=t[bracket..].Trim();
        var head=t[..bracket].Trim();
        if(rawTag.Equals("[breach overpressure]",StringComparison.OrdinalIgnoreCase))return (head,"Breach overpressure",0);
        if(rawTag.Equals("[interior]",StringComparison.OrdinalIgnoreCase))return (head,"Interior",1);
        var sm=ShockTag.Match(rawTag);
        if(sm.Success)return (head,$"Interior shock ({sm.Groups[1].Value} %)",2);
        if(rawTag.Equals("[exterior]",StringComparison.OrdinalIgnoreCase))return (head,"Exterior",3);
        return (t,"",4);
    }

    private static string CrewRole(string label)
    {
        var (clean,_,_)=SplitCauseTag(label);
        if(IsAnonymous(clean))return "";
        return clean;
    }

    /// <summary>Component ids such as "fuelTank" read as "fuel tank"; names with spaces or numbers stay as they are.</summary>
    internal static string PartLabel(string label)
    {
        var t=Regex.Replace((label??"").Trim(),@"\s*\[Material:[^\]]*\]","");
        if(t.Length==0)return "part";
        var (clean,group,_)=SplitCauseTag(t);
        if(group.Length>0)t=clean;
        // 0.22.41: report names for native module ids ("ammoRack" → "ammunition stowage", "cannon" → "main gun").
        if(CombatReportLanguage.KnownModule(t) is {} known)t=known;
        else if(!t.Contains(' ')&&!t.Contains('#')&&CamelCase.IsMatch(t))t=CamelCase.Replace(t," $1").ToLowerInvariant();
        else if(t.Length>1&&char.IsUpper(t[0])&&!char.IsUpper(t[1])&&!t.Any(char.IsDigit)&&!t.Contains('#'))t=char.ToLowerInvariant(t[0])+t[1..];
        return t.Length>40?t[..40]:t;
    }

    private sealed class GroupAcc
    {
        internal readonly string Label;
        internal readonly int Order;
        internal readonly Dictionary<string,(string Role,bool Killed)> Crew=new(StringComparer.Ordinal);
        internal readonly List<string> Destroyed=new(),Damaged=new();
        internal GroupAcc(string label,int order){Label=label;Order=order;}
    }

    internal static CombatDamageSummary From(IEnumerable<CombatReportEvent> events)
    {
        var s=new CombatDamageSummary();
        var crew=new Dictionary<string,(string Role,bool Killed,string Group,int Order)>(StringComparer.Ordinal);
        var byGroup=new Dictionary<string,GroupAcc>(StringComparer.OrdinalIgnoreCase);
        GroupAcc GroupOf(string label,int order)
        {
            if(!byGroup.TryGetValue(label,out var acc)){acc=new GroupAcc(label,order);byGroup[label]=acc;}
            return acc;
        }
        foreach(var e in events)
        {
            if(e==null||e.Kind=="vehicleDestroyed"){if(e!=null)s.VehicleDestroyedEvent=true;continue;}
            if(e.Kind!="interaction")continue;
            var text=(e.Text??"").Trim();
            if(VehicleLine.IsMatch(text)){s.VehicleDestroyedEvent=true;continue;}
            if(text.StartsWith("Crew member ",StringComparison.OrdinalIgnoreCase)&&(text.EndsWith(" killed",StringComparison.Ordinal)||text.EndsWith(" wounded",StringComparison.Ordinal)))
            {
                var killed=text.EndsWith(" killed",StringComparison.Ordinal);
                var rawLabel=CrewLabel(text[..^(killed?" killed".Length:" wounded".Length)]);
                var (cleanLabel,groupLabel,order)=SplitCauseTag(rawLabel);
                var role=IsAnonymous(cleanLabel)?"":cleanLabel;
                var key=e.RelatedEntity is {} id?"#"+id:cleanLabel.Length==0?"?"+crew.Count:cleanLabel;
                if(crew.TryGetValue(key,out var old))
                {
                    bool pickNew=killed&&!old.Killed||(killed==old.Killed&&order<old.Order);
                    var nextGroup=pickNew?groupLabel:old.Group;
                    var nextOrder=pickNew?order:old.Order;
                    crew[key]=(old.Role.Length>0?old.Role:role,old.Killed||killed,nextGroup,nextOrder);
                }
                else crew[key]=(role,killed,groupLabel,order);
                continue;
            }
            bool dead=text.EndsWith(" destroyed",StringComparison.Ordinal),hurt=text.EndsWith(" damaged",StringComparison.Ordinal);
            if(!dead&&!hurt)continue;
            var rawPart=text[..^(dead?" destroyed".Length:" damaged".Length)];
            var (_,partGroup,partOrder)=SplitCauseTag(rawPart);
            var part=PartLabel(rawPart);
            if(dead)
            {
                s.destroyed.RemoveAll(p=>p.Equals(part,StringComparison.OrdinalIgnoreCase));
                s.destroyed.Add(part);
                s.damaged.RemoveAll(p=>p.Equals(part,StringComparison.OrdinalIgnoreCase));
            }
            else if(!s.destroyed.Contains(part,StringComparer.OrdinalIgnoreCase)&&!s.damaged.Contains(part,StringComparer.OrdinalIgnoreCase))
            {
                s.damaged.Add(part);
            }
            var g=GroupOf(partGroup,partOrder);
            if(dead)
            {
                g.Destroyed.RemoveAll(p=>p.Equals(part,StringComparison.OrdinalIgnoreCase));
                g.Destroyed.Add(part);
                g.Damaged.RemoveAll(p=>p.Equals(part,StringComparison.OrdinalIgnoreCase));
            }
            else if(!g.Destroyed.Contains(part,StringComparer.OrdinalIgnoreCase)&&!g.Damaged.Contains(part,StringComparer.OrdinalIgnoreCase))
            {
                g.Damaged.Add(part);
            }
        }
        foreach(var kv in crew)
        {
            var (role,killed,groupLabel,order)=kv.Value;
            if(killed){s.CrewKilled++;if(role.Length==0)s.killedUnnamed++;else if(!s.killedRoles.Contains(role.ToLowerInvariant()))s.killedRoles.Add(role.ToLowerInvariant());}
            else{s.CrewWounded++;if(role.Length==0)s.woundedUnnamed++;else if(!s.woundedRoles.Contains(role.ToLowerInvariant()))s.woundedRoles.Add(role.ToLowerInvariant());}
            GroupOf(groupLabel,order).Crew[kv.Key]=(role,killed);
        }
        foreach(var acc in byGroup.Values.OrderBy(g=>g.Order).ThenBy(g=>g.Label,StringComparer.Ordinal))
        {
            int ck=0,cw=0,ku=0,wu=0;var kr=new List<string>();var wr=new List<string>();
            foreach(var (role,killed) in acc.Crew.Values)
            {
                if(killed){ck++;if(role.Length==0)ku++;else if(!kr.Contains(role.ToLowerInvariant()))kr.Add(role.ToLowerInvariant());}
                else{cw++;if(role.Length==0)wu++;else if(!wr.Contains(role.ToLowerInvariant()))wr.Add(role.ToLowerInvariant());}
            }
            var group=new CombatDamageGroup(acc.Label,ck,cw,kr,wr,ku,wu,acc.Destroyed,acc.Damaged);
            if(group.Any)s.groups.Add(group);
        }
        return s;
    }

    private static string Roles(IReadOnlyList<string> named,int unnamed)
    {
        if(named.Count==0)return "";
        return " ("+string.Join(", ",named)+(unnamed>0?$", {unnamed} unnamed":"")+")";
    }
    private static string FormatCrewLine(int killed,int wounded,IReadOnlyList<string> killedRoles,int killedUnnamed,IReadOnlyList<string> woundedRoles,int woundedUnnamed,int? crewTotal)
    {
        var parts=new List<string>();
        bool known=crewTotal is >0;
        if(killed>0)parts.Add((known?$"Crew {killed}/{crewTotal} killed":$"{killed} crew killed")+Roles(killedRoles,killedUnnamed));
        if(wounded>0)
            parts.Add(killed>0?$"{wounded} wounded"+Roles(woundedRoles,woundedUnnamed)
                :(known?$"Crew {wounded}/{crewTotal} wounded":$"{wounded} crew wounded")+Roles(woundedRoles,woundedUnnamed));
        return string.Join(", ",parts);
    }
    private static string FormatPartsLine(IReadOnlyList<string> destroyed,IReadOnlyList<string> damaged,int max)
    {
        var dead=destroyed.ToList();var hurt=damaged.ToList();int more=0;
        if(dead.Count+hurt.Count>max)
        {
            more=dead.Count+hurt.Count-max;
            var keepDead=Math.Min(dead.Count,max);dead=dead.Take(keepDead).ToList();hurt=hurt.Take(max-keepDead).ToList();
        }
        var parts=new List<string>();
        if(dead.Count>0)parts.Add(string.Join(", ",dead)+" destroyed");
        if(hurt.Count>0)parts.Add(string.Join(", ",hurt)+" damaged");
        var text=string.Join("; ",parts);
        return more>0?text+$" +{more} more":text;
    }

    /// <summary>The crew sentence: "3 crew killed (gunner, loader, driver), 1 wounded".</summary>
    internal string CrewText
    {
        get
        {
            var parts=new List<string>();
            if(CrewKilled>0)parts.Add($"{CrewKilled} crew killed"+Roles(killedRoles,killedUnnamed));
            if(CrewWounded>0)parts.Add(CrewKilled>0?$"{CrewWounded} wounded"+Roles(woundedRoles,woundedUnnamed):$"{CrewWounded} crew wounded"+Roles(woundedRoles,woundedUnnamed));
            return string.Join(", ",parts);
        }
    }
    /// <summary>The parts sentence: "engine destroyed, gun damaged" (at most <paramref name="max"/> named, then "+N more").</summary>
    internal string PartsText(int max=6)
    {
        var all=destroyed.Select(p=>p+" destroyed").Concat(damaged.Select(p=>p+" damaged")).ToList();
        if(all.Count>max){var rest=all.Count-max;all=all.Take(max).ToList();all.Add($"+{rest} more");}
        return string.Join(", ",all);
    }
    /// <summary>
    /// 0.22.7 crew phrase: "Crew 2/4 killed" with the crew total when it is known (the survivors are the rest), else "2 crew killed";
    /// roles in brackets when the seats have names; wounded after it ("Crew 2/4 killed, 1 wounded").
    /// </summary>
    internal string CrewLine(int? crewTotal)=>FormatCrewLine(CrewKilled,CrewWounded,killedRoles,killedUnnamed,woundedRoles,woundedUnnamed,crewTotal);
    /// <summary>
    /// 0.22.7 parts phrase, grouped by status instead of repeating the verb: "ammo rack, motor, wheel array, fuel tank destroyed;
    /// gun damaged". At most <paramref name="max"/> names are listed, then "+N more".
    /// </summary>
    internal string PartsLine(int max=8)=>FormatPartsLine(destroyed,damaged,max);
    /// <summary>
    /// One line for the whole impact: crew, then parts, separated by " · ". When HE/HESH cause tags are present,
    /// groups cleanly by cause ("Interior shock (1.3 %): Crew 3/3 wounded · engine, fuel tank damaged | Exterior: main gun damaged").
    /// </summary>
    internal string Line(int? crewTotal,int maxParts=8)
    {
        if(!HasCauseGroups)
        {
            var crew=CrewLine(crewTotal);var parts=PartsLine(maxParts);
            return crew.Length>0&&parts.Length>0?crew+" · "+parts:crew+parts;
        }
        var sections=new List<string>();
        int remaining=maxParts;
        foreach(var g in groups)
        {
            var crew=FormatCrewLine(g.CrewKilled,g.CrewWounded,g.KilledRoles,g.KilledUnnamed,g.WoundedRoles,g.WoundedUnnamed,crewTotal);
            int take=Math.Max(1,remaining);
            var parts=FormatPartsLine(g.PartsDestroyed,g.PartsDamaged,take);
            remaining=Math.Max(1,remaining-(g.PartsDestroyed.Count+g.PartsDamaged.Count));
            var body=crew.Length>0&&parts.Length>0?crew+" · "+parts:crew+parts;
            if(body.Length==0)continue;
            sections.Add(g.Label.Length>0?g.Label+": "+body:body);
        }
        return string.Join(" | ",sections);
    }
    /// <summary>The whole line: crew first, then parts, separated by " · ". Empty when nothing was damaged.</summary>
    internal string Text(int maxParts=6)
    {
        var sb=new StringBuilder(CrewText);
        var parts=PartsText(maxParts);
        if(parts.Length>0){if(sb.Length>0)sb.Append(" · ");sb.Append(parts);}
        return sb.ToString();
    }

    /// <summary>
    /// The tank is destroyed when every crew member is dead (the native crew total and survivors recorded for the shot) or the
    /// shot carries a native vehicle-destroyed event. Unknown crew counts never claim a kill.
    /// </summary>
    internal static bool IsDestroyed(CombatReportShot shot)
    {
        if(shot.CrewTotal is >0&&shot.CrewAlive==0)return true;
        return From(shot.Events??Array.Empty<CombatReportEvent>()).VehicleDestroyedEvent;
    }
}
