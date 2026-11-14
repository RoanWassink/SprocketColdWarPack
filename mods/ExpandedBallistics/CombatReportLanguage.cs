using System.Globalization;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.41 (user decision 2026-11-08): every visible report text reads like a ballistic test report, never like game jargon.
/// This is the one place that turns recorded event texts and native ids into report wording, and that lists the forbidden terms
/// the tests scan for. Presentation only: no projection value, budget or outcome is computed here. The Technical log of the HTML
/// keeps the raw event texts.
/// </summary>
internal static class CombatReportLanguage
{
    /// <summary>Terms that must not appear in visible report text (panel, HTML without the Technical log, forensic screen, hit panel).</summary>
    internal static readonly string[] Jargon={"native","proxy","game","budget","block","grouped block","entity","component","member","Kit","HitId","ammoRack","Reached layer","Traversed","not recorded","no reaction logged","(add-on)","camelCase identifiers"};
    /// <summary>Statements that only say that something is not known; they do not appear in the forensic screen.</summary>
    internal static readonly string[] Uncertainty={"cannot","unknown","unmeasured","not measured","not recorded","depth not recorded","not quantified","unquantified","undetermined","not determined","spare margin","stopping margin","member order","unresolved","not resolved","?"};
    /// <summary>A native group of several plates. 0.22.42: its plates are shown front to back, so the name carries no statement about the order.</summary>
    internal const string ArrayName="Composite array";

    private static readonly Regex[] JargonPatterns=new[]
    {
        @"\bnative\b",@"\bprox(y|ies)\b",@"\bgames?\b",@"\bbudgets?\b",@"\bblocks?\b",@"\bentit(y|ies)\b",@"\bcomponents?\b",@"\bmembers?\b",@"\bkit\b",
        @"\bhit ?ids?\b",@"\bammo ?rack\b",@"\breached layers?\b",@"\btraversed\b",@"\bnot recorded\b",@"\bno reaction logged\b",@"\(add-on\)"
    }.Select(p=>new Regex(p,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)).ToArray();
    private static readonly Regex CamelCase=new(@"\b[a-z]+[A-Z][A-Za-z0-9]*\b",RegexOptions.CultureInvariant);
    private static readonly Regex[] UncertaintyPatterns=new[]
    {
        @"\bcannot\b",@"\bunknown\b",@"\bunmeasured\b",@"\bnot measured\b",@"\bnot recorded\b",@"\bnot quantified\b",@"\bunquantified\b",@"\bundetermined\b",
        @"\bnot determined\b",@"\bspare margin\b",@"\bstopping margin\b",@"\bmember order\b",@"\bunresolved\b",@"\bnot resolved\b",@"\?"
    }.Select(p=>new Regex(p,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)).ToArray();

    /// <summary>Forbidden terms found in <paramref name="text"/>, after removing the exempt user-given names (materials, plates, vehicles, shells).</summary>
    internal static IReadOnlyList<string> JargonIn(string text,IEnumerable<string>? exempt=null)
    {
        var t=Strip(text,exempt);var found=new List<string>();
        foreach(var r in JargonPatterns)foreach(Match m in r.Matches(t))found.Add(m.Value);
        foreach(Match m in CamelCase.Matches(t))found.Add(m.Value);
        return found;
    }
    /// <summary>Uncertainty statements in forensic text (0.22.42: no exception; the plates of a composite array are shown in order).</summary>
    internal static IReadOnlyList<string> UncertaintyIn(string text,IEnumerable<string>? exempt=null)
    {
        var t=Strip(text,exempt);var found=new List<string>();
        foreach(var r in UncertaintyPatterns)foreach(Match m in r.Matches(t))found.Add(m.Value);
        return found;
    }
    private static string Strip(string text,IEnumerable<string>? exempt)
    {
        var t=text??"";
        if(exempt!=null)foreach(var e in exempt.Where(e=>!string.IsNullOrWhiteSpace(e)).Distinct().OrderByDescending(e=>e.Length))t=t.Replace(e," · ",StringComparison.Ordinal);
        return t;
    }

    private static readonly Regex Camel=new("(?<=[a-z0-9])([A-Z])",RegexOptions.CultureInvariant);
    private static readonly Dictionary<string,string> Modules=new(StringComparer.OrdinalIgnoreCase)
    {
        ["ammoRack"]="ammunition stowage",["ammo rack"]="ammunition stowage",["cannon"]="main gun",["mainGun"]="main gun"
    };
    /// <summary>The report name of a native module id that has one ("ammoRack" → "ammunition stowage", "cannon" → "main gun"); else null.</summary>
    internal static string? KnownModule(string? name)=>Modules.TryGetValue((name??"").Trim(),out var known)?known:null;
    /// <summary>A readable module or part name: "ammoRack" → "ammunition stowage", "cannon" → "main gun", "fuelTank" → "fuel tank". Other names stay.</summary>
    internal static string Module(string? name)
    {
        var t=StripTags((name??"").Trim()); // mod part names such as "VARMA-style passive side armour (game proxy)"
        if(t.Length==0)return "module";
        // Older reports name a hit by its event: "Native stop in Composite armour plate [Material: Leopard 2A4 hull]".
        if(t.StartsWith("Native stop in ",StringComparison.Ordinal))t=t["Native stop in ".Length..].Trim();
        var tagged=Regex.Match(t,@"^(.*?)\s*\[Material: ([^\]]*)\]$",RegexOptions.CultureInvariant);
        if(tagged.Success)return Module(tagged.Groups[1].Value)+" ("+Material(tagged.Groups[2].Value)+")";
        if(t is "grouped block" or "Combined armour block" or "Armour array" or "combined armour block")return "composite array";
        if(Modules.TryGetValue(t,out var known))return known;
        if(t.StartsWith("Crew member",StringComparison.OrdinalIgnoreCase)||t.StartsWith("Crew seat",StringComparison.OrdinalIgnoreCase))
        {
            var role=CombatDamageSummary.CrewLabel(t);
            return CombatDamageSummary.IsAnonymous(role)?"Crew":role;
        }
        if(!t.Contains(' ')&&!t.Contains('#')&&Camel.IsMatch(t))return Camel.Replace(t," $1").ToLowerInvariant();
        return t;
    }
    /// <summary>"Leopard 2A7 turret wedge (add-on)" (a built-in preset tag) reads "Leopard 2A7 turret wedge"; a user's own composer name stays exactly as given.</summary>
    internal static string Material(string? label,string? id=null)
    {
        var t=(label??"").Trim();
        if(id?.StartsWith("ebCassette_user_",StringComparison.Ordinal)==true)return t;
        return StripTags(t);
    }
    /// <summary>The built-in technical tags of preset names: "(add-on)" and "(game proxy)".</summary>
    private static string StripTags(string t)=>t.Replace(" (add-on)","",StringComparison.Ordinal).Replace("(add-on)","",StringComparison.Ordinal)
        .Replace(" (game proxy)","",StringComparison.Ordinal).Replace("(game proxy)","",StringComparison.Ordinal).Trim();
    internal static string Material(CombatLayerPart part)=>Material(part.MaterialLabel??(part.MaterialId==null?null:EraDisplayName.Material(part.MaterialId)),part.MaterialId);

    /// <summary>Report wording of a reaction line (the Kit table Reaction column, the waterfall tooltips, the hit panel).</summary>
    internal static string Reaction(string? text)
    {
        var t=(text??"").Trim();
        if(t.Length==0)return t;
        switch(t)
        {
            case "ERA activated":return "ERA element initiated";
            case "ERA already spent":return "ERA element previously initiated";
            case "Material reaction stopped the core at the support plate":return "Projectile arrested by the material response at the support plate";
            case "Cassette unavailable: not loaded or vehicle date too early":return "Composite response not applied: composition not loaded, or the vehicle date precedes it";
            case "Cassette not loaded - restart Sprocket":return "Composite response not applied: composition not loaded";
            case "Sandwich: no steel backing confirmed":return "Sandwich response not applied: no steel backing behind the layer";
            case "Cassette: exact depth unavailable in blended block":return "Composite response not applied: depth within the composite array not resolved";
            case "Material: declared face unavailable":return "Material response not applied: strike face orientation not established";
            case "Material settings differ from reaction binding":return "Material response not applied: material properties differ from the response definition";
        }
        if(t.StartsWith("Backing confirmed | extra reduction ",StringComparison.Ordinal))return "Material response: additional reduction "+t["Backing confirmed | extra reduction ".Length..];
        // "ERA Duplet modular · layer 2: APFSDS −24.5 %" → "ERA element initiated (Duplet modular · layer 2): APFSDS −24.5 %".
        if(t.StartsWith("ERA ",StringComparison.Ordinal)&&t.IndexOf(':')>4&&!t.StartsWith("ERA element",StringComparison.Ordinal))
        {
            var colon=t.IndexOf(':');t="ERA element initiated ("+t[4..colon].Trim()+")"+t[colon..];
        }
        if(t.StartsWith("Cassette ",StringComparison.Ordinal))t="Composite "+t["Cassette ".Length..];
        t=ProtectionLineText(t);
        return Scrub(t);
    }
    /// <summary>"Leopard 2A7 turret wedge (add-on): 29 mm × 0.68 (long rod @66°) = 20 mm RHA" → "Leopard 2A7 turret wedge: LOS 29 mm × 0.68 (long rod, 66°) = 20 mm RHA-eq".</summary>
    internal static string ProtectionLineText(string text)
    {
        if(!ProtectionLine.Is(text.Split(';')[0]))return text;
        var head=text.Split(';')[0];var rest=text.Length>head.Length?text[head.Length..]:"";
        var colon=head.LastIndexOf(": ",head.IndexOf(" mm × ",StringComparison.Ordinal),StringComparison.Ordinal);
        if(colon<0)return text;
        var label=Material(head[..colon]);var body=head[(colon+2)..];
        body=Regex.Replace(body,@"\((long rod|full-bore KE|jet|HESH) @(\d+)°\)","($1, $2°)",RegexOptions.CultureInvariant);
        if(body.EndsWith(ProtectionLine.Marker,StringComparison.Ordinal))body+="-eq";
        return label+": LOS "+body+rest;
    }
    /// <summary>Targeted replacements of native wording inside a recorded sentence; material names are never rewritten.</summary>
    internal static string Scrub(string text)
    {
        var t=text;
        // "overlapped by Crew member #549 after 140 mm: native did not charge 483 mm of plate path; 483 mm × 0.49 ΔRHA = L 237.6 mm RHA; actual loss −184.2 mm by the native law"
        t=Regex.Replace(t,@"overlapped by (.+?) after ([\d.]+) mm: native did not charge ([\d.]+) mm of plate path;",
            m=>$"overlapped by {Module(m.Groups[1].Value)} after {m.Groups[2].Value} mm: the remaining {m.Groups[3].Value} mm of plate path charged separately;",RegexOptions.CultureInvariant);
        t=Regex.Replace(t,@"actual loss ([−-][\d.]+) mm by the native law","measured reduction $1 mm (residual-penetration law)",RegexOptions.CultureInvariant);
        t=Regex.Replace(t,@"blended block charged at ([\d.]+), (\+[\d.]+ mm RHA) by the native law","composite array charged at factor $1, $2 (residual-penetration law)",RegexOptions.CultureInvariant);
        t=t.Replace("by the native law","(residual-penetration law)",StringComparison.Ordinal).Replace("blended member path unavailable","path within the composite array not resolved",StringComparison.Ordinal);
        t=Regex.Replace(t,@"native skipped plate \(air gap ([\d.]+) mm\); cassette and passive resistance applied by the mod","plate beyond an air gap of $1 mm; composite and passive resistance applied",RegexOptions.CultureInvariant);
        t=t.Replace("native speed conversion unavailable","speed conversion unavailable",StringComparison.Ordinal).Replace("native round trip invalid","round-trip check invalid",StringComparison.Ordinal);
        t=t.Replace("Composite module interval unresolved/overlapping","Composite module intervals overlap",StringComparison.Ordinal).Replace("Reached ERA response","ERA response",StringComparison.Ordinal);
        t=t.Replace("not added again to block total","not added again to the array total",StringComparison.Ordinal).Replace("block exit","array exit",StringComparison.Ordinal);
        t=t.Replace("; native grouped block: interval-weighted resistance, shared outcome","; composite array: interval-weighted resistance, common outcome",StringComparison.Ordinal);
        t=t.Replace("Combined armour block",ArrayName,StringComparison.Ordinal).Replace("combined armour block","composite array",StringComparison.Ordinal);
        t=t.Replace("native grouped block","composite array",StringComparison.Ordinal).Replace("grouped block","composite array",StringComparison.Ordinal);
        t=t.Replace("blended native block","composite array",StringComparison.Ordinal).Replace("native resistance only","passive resistance only",StringComparison.Ordinal);
        t=t.Replace("native resistance","passive resistance",StringComparison.Ordinal).Replace("Native block","Composite array",StringComparison.Ordinal);
        t=t.Replace("native passive resistance","passive resistance",StringComparison.Ordinal).Replace("Native passive resistance","Passive resistance",StringComparison.Ordinal);
        t=StripTags(t);
        // "[Material: X]" tags of recorded plate names read "(X)".
        t=Regex.Replace(t,@"\s*\[Material: ([^\]]*)\]",m=>" ("+StripTags(m.Groups[1].Value)+")",RegexOptions.CultureInvariant);
        foreach(var (id,name) in Modules)t=Regex.Replace(t,@"\b"+Regex.Escape(id)+@"\b",name,RegexOptions.CultureInvariant);
        return t;
    }
    /// <summary>The panel note for a composite plate the shot line did not reach (raw: "Skipped composite plate #12: line ended before another solid block; ...").</summary>
    internal static string Diagnostic(string? text)
    {
        var t=(text??"").Trim();
        var m=Regex.Match(t,@"^Skipped composite plate #(\d+): line ended before another solid block; cassette and passive resistance not applied\.(?: \((\d+) occurrences\))?",RegexOptions.CultureInvariant);
        if(m.Success)return $"Composite plate #{m.Groups[1].Value} not engaged: the shot line ended before the next solid layer; no composite or passive resistance applied"+(m.Groups[2].Success?$" ({m.Groups[2].Value} occurrences).":".");
        return Scrub(t);
    }
    /// <summary>Damage item "ammoRack destroyed" → "ammunition stowage destroyed"; crew items keep their role.</summary>
    internal static string Damage(string? text)
    {
        var t=(text??"").Trim();
        foreach(var verb in new[]{" destroyed"," damaged"," killed"," wounded"})
            if(t.EndsWith(verb,StringComparison.Ordinal))return Module(t[..^verb.Length])+verb;
        return Module(t);
    }
}

/// <summary>
/// 0.22.41: a layer whose measured path is zero (the native entry and exit are the same intersection, or the LOS is at most 0.05 mm) and
/// that leaves the penetration unchanged is hidden from the visible report (layer table, cross-section, absorption list, findings). The raw
/// events stay in the Technical log. A stop or a ricochet is never a zero span.
/// </summary>
internal static class CombatZeroSpan
{
    internal const double PathMm=.05,ChangeMm=.005;
    /// <summary>"sim:fragment:entry:exit" with entry == exit (indices into one native intersection array).</summary>
    internal static bool SameIntersection(string? hitId)
    {
        if(string.IsNullOrWhiteSpace(hitId))return false;
        var p=hitId.Split(':');
        return p.Length>=4&&int.TryParse(p[^2],NumberStyles.Integer,CultureInfo.InvariantCulture,out var entry)&&int.TryParse(p[^1],NumberStyles.Integer,CultureInfo.InvariantCulture,out var exit)&&entry==exit;
    }
    internal static bool Is(string? hitId,double? pathMm,double? before,double? after)=>
        before is {} b&&after is {} a&&double.IsFinite(b)&&double.IsFinite(a)&&Math.Abs(b-a)<=ChangeMm&&(SameIntersection(hitId)||pathMm is {} p&&p<=PathMm);
    internal static bool Is(CombatHitView v)
    {
        if(v.Status.StartsWith("Stopped",StringComparison.Ordinal)||v.Status is "Deflected" or "Diagnostic only")return false;
        if(v.Walls.Count>1)return false; // a merged row spans several measured blocks
        return Is(v.Id,PathOf(v),v.Before,v.After);
    }
    private static double? PathOf(CombatHitView v)
    {
        if(v.Layer?.AirGapMm is {} gap)return gap;
        var paths=v.Layer?.Parts.Where(p=>p.Armour).Select(CombatForensics.Path).ToArray()??Array.Empty<double?>();
        return paths.Length>0&&paths.All(p=>p!=null)?paths.Max():null;
    }
}
