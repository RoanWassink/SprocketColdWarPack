using System.Globalization;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// Readable captions, units and option labels for the Kit shell editor (wave 2b).
/// Pure lookup tables only: fields, ranges, defaults and visibility come from
/// <see cref="ModularShellDraft"/>. A field that is missing here still shows, with a
/// generic caption from its key, so new optional MSD fields appear automatically.
/// Entries for fields that do not exist (yet) are harmless.
/// </summary>
internal static class ShellEditorText
{
    internal static readonly IReadOnlyDictionary<string,string> Captions=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["label"]="Name",
        ["guidanceTransport"]="Command link",["wireMaximumLength"]="Max wire length",["wireRetentionSeconds"]="Wire visible after break",
        ["wireDisplayWidth"]="Cable display width",["wireSampleDistance"]="Cable sample distance",
        ["guidanceMode"]="Guidance",["maxTurnRate"]="Max turn rate",["guidanceDelay"]="Guidance delay",
        ["topAttackAltitude"]="Top-attack altitude",["topAttackClimbAngle"]="Top-attack climb angle",["topAttackDiveAngle"]="Top-attack dive angle",
        ["guidanceLaw"]="Guidance law",["leadDistanceM"]="Lead distance",["rangePattern"]="Range profile",
        ["humpHeightM"]="Hump height",["humpRangeMinM"]="Hump from range",["humpRangeMaxM"]="Hump to range",
        ["humpTerminalDistanceM"]="Hump terminal distance",["humpRampDistanceM"]="Hump ramp distance",
        ["directRangeBelowM"]="Direct flight below",["shortRangeThresholdM"]="Short-range threshold",["shortRangeGuidanceDelay"]="Short-range guidance delay",
        ["spiralAmplitudeM"]="Spiral amplitude",["spiralFrequencyHz"]="Spiral frequency",
        ["disturbanceAmplitudeM"]="Disturbance amplitude",["disturbanceFrequencyHz"]="Disturbance frequency",["initialElevationDegrees"]="Initial elevation",
        ["velocityEfficiency"]="Velocity efficiency",["velocityMultiplier"]="Velocity multiplier",
        ["maximumVelocityFactor"]="Max velocity factor",["maximumVelocity"]="Max velocity",
        ["flightSpeed"]="Flight speed",["launchSpeedMode"]="Launch speed source",["launchSpeed"]="Launch speed",["launchSpeedMultiplier"]="Launch speed multiplier",
        ["acceleration"]="Acceleration",["motorDelay"]="Motor delay",["motorBurnTime"]="Motor burn time (0 = unlimited)",
        ["coastDeceleration"]="Coast deceleration",["maximumFlightTime"]="Max flight time",["speedCalibration"]="Speed calibration",
        ["penetratorDiameterFactor"]="Penetrator diameter",["penetratorLengthInCalibres"]="Penetrator length",["penetratorDensity"]="Penetrator density",
        ["penetrationQuality"]="Penetration quality",["fragmentDamageMultiplier"]="Fragment damage",["nativeExplosivePower"]="Explosive power",
        ["chemicalPenetrationMm"]="Main charge penetration (at ref. calibre)",["tandemPrecursorPenetrationMm"]="Precursor (0 = single charge)",
        ["spallMultiplier"]="Spall multiplier",["coneHalfAngleDegrees"]="Spall cone half-angle",["explosionScale"]="Explosion scale",
        ["secondPlatePenetrationFactor"]="Min. retention behind a gap",["airGapLossPerCalibre"]="Air-gap loss per calibre",["referenceCalibreMm"]="Reference calibre",
        ["minimumDate"]="Available from (date)",
        // Rebalance 0.21.1 fields (not in this base yet; readable after the merge).
        ["kineticKind"]="Kinetic type",["carrierDiameterFactor"]="Carrier diameter",["carrierMassFraction"]="Carrier mass",
        ["chemicalSource"]="Penetration source",["standoffFreeCalibres"]="Free standoff",
        ["keepAfterRetirement"]="Keep in later eras (archive)",
        ["apheFuseSensitivityRhaMm"]="Fuse sensitivity (min armour)",
        ["apheFuseDelayMeters"]="Fuse delay (depth inside)",
        ["apheDetonateOnStop"]="Detonate if stopped inside",
        ["apheConeHalfAngleDegrees"]="Burst cone half-angle (180° = 360°)",
        ["apheForwardMomentumBlend"]="Forward shell-speed carry",
        ["apheFragmentCountAt100Mm"]="Fragments (at 100 mm)",
        ["apheFragmentMassKgAt100Mm"]="Shrapnel mass (at 100 mm)",
        ["apheFragmentSpeedMetersPerSecond"]="Explosive burst speed",
        ["apheMaxGapFraction"]="Narrow-compartment clamp",
        ["apheMinWallClearanceMeters"]="Far-wall clearance",
        ["apheOpenFallbackMeters"]="Open-space fallback delay",
        ["apheExplosionVisualScale"]="Explosion visual scale"
    };
    /// <summary>Unit shown next to the number. Used instead of the MSD unit when the MSD unit is empty or a description.</summary>
    internal static readonly IReadOnlyDictionary<string,string> Units=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["wireDisplayWidth"]="m",["nativeExplosivePower"]="native",
        ["penetratorDiameterFactor"]="× calibre",["penetratorLengthInCalibres"]="calibres",
        ["velocityEfficiency"]="×",["velocityMultiplier"]="×",["maximumVelocityFactor"]="×",["launchSpeedMultiplier"]="×",
        ["penetrationQuality"]="×",["fragmentDamageMultiplier"]="×",["spallMultiplier"]="×",["explosionScale"]="×",
        ["secondPlatePenetrationFactor"]="×",["airGapLossPerCalibre"]="per cal.",
        ["carrierDiameterFactor"]="× calibre",["carrierMassFraction"]="× shell mass",["standoffFreeCalibres"]="calibres",
        ["apheForwardMomentumBlend"]="×",["apheMaxGapFraction"]="× gap",["apheExplosionVisualScale"]="×"
    };
    private static readonly IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>> FieldOptions=new Dictionary<string,IReadOnlyDictionary<string,string>>(StringComparer.Ordinal)
    {
        ["guidanceTransport"]=Map(("current","Radio / laser link"),("wire","Wire (visible cable)")),
        ["guidanceMode"]=Map(("sight","Sight (SACLOS)"),("keyboard","Keys (MCLOS)"),("topattack","Top attack"),("none","None")),
        ["guidanceLaw"]=Map(("legacy","Classic"),("losLead","LOS lead")),
        ["rangePattern"]=Map(("none","None"),("hump","Hump")),
        ["launchSpeedMode"]=Map(("fixed","Fixed"),("cannon","From cannon")),
        ["speedCalibration"]=Map(("motor","Motor"),("motorSmooth","Motor (smooth)"),("fixed","Fixed")),
        ["kineticKind"]=Map(("ap","AP"),("apc","APC"),("apcbc","APCBC"),("apcr","APCR"),("apds","APDS")),
        ["chemicalSource"]=Map(("tech","Dated HEAT technology"),("profile","This profile")),
        ["modules.tail"]=Map(("spin","Spin"),("fins","Fins"),("guided","Guided"),("none","None")),
        ["modules.propulsion"]=Map(("ballistic","Ballistic"),("rocket","Rocket motor")),
        ["modules.body"]=Map(("fullBore","Full-bore"),("longRod","Long rod"),("shapedCharge","Shaped charge")),
        ["modules.effect"]=Map(("kinetic","Kinetic"),("spall","Internal burst (APHE)"),("blast","HE blast"),("heat","HEAT"),("hesh","HESH")),
        ["modules.carrier"]=Map(("fullBore","Full-bore"),("sabot","Sabot"),("launcher","Dedicated launcher"),("gunLaunch","Gun launch"))
    };
    private static IReadOnlyDictionary<string,string> Map(params (string Value,string Label)[] pairs)=>pairs.ToDictionary(p=>p.Value,p=>p.Label,StringComparer.Ordinal);

    internal static readonly IReadOnlyDictionary<string,string> Behaviours=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["ap"]="AP",["aphe"]="APHE",["he"]="HE",["heat"]="HEAT",["hesh"]="HESH",["apfsds"]="APFSDS",["atgm"]="ATGM",["atgm_gun"]="Gun-launched ATGM"
    };
    internal static string Behaviour(string? behavior)=>behavior==null?"Pending":Behaviours.TryGetValue(behavior,out var label)?label:Humanize(behavior);
    internal static string Tag(string? behavior)=>behavior==null?"PENDING":behavior=="atgm_gun"?"GL-ATGM":behavior.ToUpperInvariant();

    /// <summary>"leadDistanceM" → "Lead distance m": the generic caption for fields without an entry.</summary>
    internal static string Humanize(string key)
    {
        if(string.IsNullOrEmpty(key))return "";
        var words=Regex.Replace(key,"([a-z0-9])([A-Z])","$1 $2").ToLowerInvariant();
        return char.ToUpperInvariant(words[0])+words[1..];
    }
    internal static string Caption(string key)=>Captions.TryGetValue(key,out var caption)?caption:Humanize(key);
    internal static string Unit(ModularShellDraft.Field field)=>Units.TryGetValue(field.Key,out var unit)?unit:field.Unit;
    /// <summary>Option label for a choice field ("guidanceMode") or a module group ("modules.tail").</summary>
    internal static string Option(string field,string value)
    {
        if(FieldOptions.TryGetValue(field,out var options)&&options.TryGetValue(value,out var label))return label;
        return Humanize(value);
    }
    internal static string Module(string group,string kind)=>ModularShellDraft.Options.TryGetValue(group,out var known)&&known.Contains(kind)
        ?Option("modules."+group,kind):kind+" (unknown)";
    internal static string GroupTitle(string group)=>group;

    /// <summary>
    /// Short list subtitle ("Fins · Rod", "Guided · Rocket · HEAT · Gun-launched"); the full module chain
    /// stays in the editor header. Defaults that say nothing (ballistic, kinetic full-bore, launcher, sabot) are left out.
    /// </summary>
    internal static string ShortChain(IReadOnlyDictionary<string,string> modules)
    {
        string K(string key)=>modules.TryGetValue(key,out var v)?v:"";
        var parts=new List<string>();
        parts.Add(K("tail") switch{"spin"=>"Spin","fins"=>"Fins","guided"=>"Guided","none"=>"Unguided",var t=>t});
        if(K("propulsion")=="rocket")parts.Add("Rocket");
        parts.Add((K("body"),K("effect")) switch
        {
            ("longRod",_)=>"Rod",(_,"blast")=>"HE",(_,"heat")=>"HEAT",(_,"hesh")=>"HESH",(_,"spall")=>"APHE",
            ("shapedCharge","kinetic")=>"Shaped body",("fullBore","kinetic")=>"Full-bore",(var b,var e)=>Humanize(b)+" "+Humanize(e).ToLowerInvariant()
        });
        if(K("carrier")=="gunLaunch")parts.Add("Gun-launched");
        return string.Join(" · ",parts.Where(p=>p.Length>0));
    }
    /// <summary>
    /// List tag from an availability reason, by prefix (the reasons the shell rebalance helper will return):
    /// "From 1985" → "1985+", "Retired 1990" → "RETIRED 1990", "Launcher only" → "LAUNCHER", "Unavailable" → "N/A",
    /// "Archive (retired 1990)" → "ARCHIVE". Null = no special tag (the behaviour tag stays).
    /// </summary>
    internal static string? AvailabilityTag(string? reason)
    {
        var r=(reason??"").Trim();
        string Year(){var m=Regex.Match(r,@"(\d{4})");return m.Success?m.Groups[1].Value:"";}
        if(r.StartsWith("Archive",StringComparison.OrdinalIgnoreCase))return "ARCHIVE";
        if(r.StartsWith("From",StringComparison.OrdinalIgnoreCase))return Year().Length>0?Year()+"+":"LATER";
        if(r.StartsWith("Retired",StringComparison.OrdinalIgnoreCase))return Year().Length>0?"RETIRED "+Year():"RETIRED";
        if(r.StartsWith("Launcher",StringComparison.OrdinalIgnoreCase))return "LAUNCHER";
        if(r.StartsWith("Unavailable",StringComparison.OrdinalIgnoreCase))return "N/A";
        return null;
    }
    /// <summary>Dimmed in the list: not available on this vehicle, except archive shells (selectable, own chip, not dimmed).</summary>
    internal static bool Dimmed(bool available,string? reason)=>!available&&!(reason??"").TrimStart().StartsWith("Archive",StringComparison.OrdinalIgnoreCase);
    /// <summary>A two-option no/yes choice field: drawn as a Toggle.</summary>
    internal static bool IsYesNo(IReadOnlyList<string>? choices)=>choices is {Count:2}&&choices.Contains("no")&&choices.Contains("yes");

    /// <summary>Group help text (as in the JSON Editor session), chosen from the current modules.</summary>
    internal static string GroupDescription(string group,string? behavior,string transport,string effect)
    {
        var status=behavior==null?"Pending unknown module: the live catalogue cannot change. ":"";
        return status+(group switch
        {
            "Tail"=>transport=="wire"
                ?"Visible wire from the fixed launch site; a break stops steering only. Wire can also accompany unguided flight."
                :"Guided steers the projectile. Spin, fins and none use native motion without a stability or drag bonus.",
            "Propulsion"=>"Ballistic launch or a rocket motor. Speed, delay and burn time set the flight profile.",
            "Body"=>"Dimensions and density define the native penetrator proxy. A shaped-charge body alone does not add a HEAT jet.",
            "Effect"=>effect switch
            {
                "spall"=>"Post-penetration APHE detonation. Configure fuse sensitivity, fuse delay, burst cone, fragmentation power and safety margins per shell.",
                "heat"=>"Set the precursor above zero for tandem HEAT. Both charges use the reference calibre; zero means a single charge.",
                _=>"The effect module decides the impact; rocket propulsion and guidance do not change it."
            },
            "Carrier"=>behavior==null?"This combination has no adapter. Save keeps it as a draft; live shells stay unchanged."
                :"The carrier sets the launch context and native technology requirements, without a stability bonus.",
            _=>""
        });
    }

    /// <summary>Number for the text field: at least the step's decimals, never hiding a typed finer value; no float noise.</summary>
    internal static string Number(double value,double step)
    {
        if(!double.IsFinite(value))return "";
        return Math.Round(value,Math.Max(Decimals(step),6)).ToString("0.######",CultureInfo.InvariantCulture);
    }
    internal static int Decimals(double step)
    {
        if(!(step>0)||step>=1)return 0;
        return Math.Clamp((int)Math.Ceiling(-Math.Log10(step)-1e-9),0,6);
    }
    /// <summary>"between 50 and 1000 m/s" with the step's decimals.</summary>
    internal static string Range(double min,double max,double step,string unit)=>
        $"Between {Number(min,step)} and {Number(max,step)}{(unit.Length==0?"":" "+unit)}.";

    /// <summary>Compiler messages cite ids and JSON keys; show the shell name and captions instead (P5).</summary>
    internal static string Humane(string message,string id,string label)
    {
        var text=message.Replace($"Profile '{id}': ",label+": ",StringComparison.Ordinal);
        foreach(var key in ModularShellDraft.Fields.Select(f=>f.Key).OrderByDescending(k=>k.Length))
            text=Regex.Replace(text,@"\b"+Regex.Escape(key)+@"\b",Caption(key).ToLowerInvariant());
        return text;
    }
}
