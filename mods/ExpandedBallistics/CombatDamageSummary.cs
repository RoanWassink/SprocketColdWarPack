using System.Text;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// Crew and part damage of one impact as a readable line, from its "interaction" events (0.22.6B). The raw event text is
/// "Crew member Crew member #12 killed" when the seat has no name (the fallback label already says "Crew member"), which the
/// combat report used to cut off in a 48 character cell. This groups the events instead:
/// "3 crew killed (gunner, loader, driver), 1 wounded · engine destroyed, gun damaged".
/// Crew are counted once each (by native object id when the event carries one), roles are shown only when the seat has a name.
/// </summary>
internal sealed class CombatDamageSummary
{
    internal int CrewKilled{get;private set;}
    internal int CrewWounded{get;private set;}
    internal IReadOnlyList<string> KilledRoles=>killedRoles;
    internal IReadOnlyList<string> WoundedRoles=>woundedRoles;
    internal IReadOnlyList<string> PartsDestroyed=>destroyed;
    internal IReadOnlyList<string> PartsDamaged=>damaged;
    internal bool VehicleDestroyedEvent{get;private set;}
    internal bool Any=>CrewKilled>0||CrewWounded>0||destroyed.Count>0||damaged.Count>0;
    private readonly List<string> killedRoles=new(),woundedRoles=new(),destroyed=new(),damaged=new();
    private int killedUnnamed,woundedUnnamed;

    private static readonly Regex Anonymous=new(@"^(#?\d+|(crew( member)?|seat|crew seat|unnamed( structure)?)(\s*#?\d+)?)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex VehicleLine=new(@"^(vehicle|tank)\b.*\bdestroyed$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex CamelCase=new("(?<=[a-z0-9])([A-Z])",RegexOptions.CultureInvariant);

    /// <summary>"Crew member Crew member #12" -> "#12"; "Crew member Gunner" -> "Gunner".</summary>
    internal static string CrewLabel(string label)
    {
        var t=(label??"").Trim();
        while(t.StartsWith("Crew member ",StringComparison.OrdinalIgnoreCase))t=t["Crew member ".Length..].Trim();
        return t;
    }
    internal static bool IsAnonymous(string label)=>label.Length==0||Anonymous.IsMatch(label);
    /// <summary>Component ids such as "fuelTank" read as "fuel tank"; names with spaces or numbers stay as they are.</summary>
    internal static string PartLabel(string label)
    {
        var t=Regex.Replace((label??"").Trim(),@"\s*\[Material:[^\]]*\]","");
        if(t.Length==0)return "part";
        // 0.22.41: report names for native module ids ("ammoRack" → "ammunition stowage", "cannon" → "main gun").
        if(CombatReportLanguage.KnownModule(t) is {} known)return known;
        if(!t.Contains(' ')&&!t.Contains('#')&&CamelCase.IsMatch(t))t=CamelCase.Replace(t," $1").ToLowerInvariant();
        else if(t.Length>1&&char.IsUpper(t[0])&&!char.IsUpper(t[1])&&!t.Any(char.IsDigit)&&!t.Contains('#'))t=char.ToLowerInvariant(t[0])+t[1..];
        return t.Length>40?t[..40]:t;
    }

    internal static CombatDamageSummary From(IEnumerable<CombatReportEvent> events)
    {
        var s=new CombatDamageSummary();
        var crew=new Dictionary<string,(string Role,bool Killed)>(StringComparer.Ordinal);
        foreach(var e in events)
        {
            if(e==null||e.Kind=="vehicleDestroyed"){if(e!=null)s.VehicleDestroyedEvent=true;continue;}
            if(e.Kind!="interaction")continue;
            var text=(e.Text??"").Trim();
            if(VehicleLine.IsMatch(text)){s.VehicleDestroyedEvent=true;continue;}
            if(text.StartsWith("Crew member ",StringComparison.OrdinalIgnoreCase)&&(text.EndsWith(" killed",StringComparison.Ordinal)||text.EndsWith(" wounded",StringComparison.Ordinal)))
            {
                var killed=text.EndsWith(" killed",StringComparison.Ordinal);
                var label=CrewLabel(text[..^(killed?" killed".Length:" wounded".Length)]);
                var role=IsAnonymous(label)?"":label;
                var key=e.RelatedEntity is {} id?"#"+id:label.Length==0?"?"+crew.Count:label;
                crew[key]=crew.TryGetValue(key,out var old)?(old.Role.Length>0?old.Role:role,old.Killed||killed):(role,killed);
                continue;
            }
            bool dead=text.EndsWith(" destroyed",StringComparison.Ordinal),hurt=text.EndsWith(" damaged",StringComparison.Ordinal);
            if(!dead&&!hurt)continue;
            var part=PartLabel(text[..^(dead?" destroyed".Length:" damaged".Length)]);
            if(dead){s.destroyed.Remove(part);if(!s.destroyed.Contains(part,StringComparer.OrdinalIgnoreCase))s.destroyed.Add(part);s.damaged.RemoveAll(p=>p.Equals(part,StringComparison.OrdinalIgnoreCase));}
            else if(!s.destroyed.Contains(part,StringComparer.OrdinalIgnoreCase)&&!s.damaged.Contains(part,StringComparer.OrdinalIgnoreCase))s.damaged.Add(part);
        }
        foreach(var (role,killed) in crew.Values)
        {
            if(killed){s.CrewKilled++;if(role.Length==0)s.killedUnnamed++;else if(!s.killedRoles.Contains(role.ToLowerInvariant()))s.killedRoles.Add(role.ToLowerInvariant());}
            else{s.CrewWounded++;if(role.Length==0)s.woundedUnnamed++;else if(!s.woundedRoles.Contains(role.ToLowerInvariant()))s.woundedRoles.Add(role.ToLowerInvariant());}
        }
        return s;
    }

    private static string Roles(IReadOnlyList<string> named,int unnamed)
    {
        if(named.Count==0)return "";
        return " ("+string.Join(", ",named)+(unnamed>0?$", {unnamed} unnamed":"")+")";
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
    internal string CrewLine(int? crewTotal)
    {
        var parts=new List<string>();
        bool known=crewTotal is >0;
        if(CrewKilled>0)parts.Add((known?$"Crew {CrewKilled}/{crewTotal} killed":$"{CrewKilled} crew killed")+Roles(killedRoles,killedUnnamed));
        if(CrewWounded>0)
            parts.Add(CrewKilled>0?$"{CrewWounded} wounded"+Roles(woundedRoles,woundedUnnamed)
                :(known?$"Crew {CrewWounded}/{crewTotal} wounded":$"{CrewWounded} crew wounded")+Roles(woundedRoles,woundedUnnamed));
        return string.Join(", ",parts);
    }
    /// <summary>
    /// 0.22.7 parts phrase, grouped by status instead of repeating the verb: "ammo rack, motor, wheel array, fuel tank destroyed;
    /// gun damaged". At most <paramref name="max"/> names are listed, then "+N more".
    /// </summary>
    internal string PartsLine(int max=8)
    {
        var dead=destroyed.ToList();var hurt=damaged.ToList();int more=0;
        if(dead.Count+hurt.Count>max)
        {
            more=dead.Count+hurt.Count-max;
            var keepDead=Math.Min(dead.Count,max);dead=dead.Take(keepDead).ToList();hurt=hurt.Take(max-keepDead).ToList();
        }
        var groups=new List<string>();
        if(dead.Count>0)groups.Add(string.Join(", ",dead)+" destroyed");
        if(hurt.Count>0)groups.Add(string.Join(", ",hurt)+" damaged");
        var text=string.Join("; ",groups);
        return more>0?text+$" +{more} more":text;
    }
    /// <summary>One line for the whole impact (0.22.7): crew, then parts, separated by " · ". Empty when nothing was damaged.</summary>
    internal string Line(int? crewTotal,int maxParts=8)
    {
        var crew=CrewLine(crewTotal);var parts=PartsLine(maxParts);
        return crew.Length>0&&parts.Length>0?crew+" · "+parts:crew+parts;
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
