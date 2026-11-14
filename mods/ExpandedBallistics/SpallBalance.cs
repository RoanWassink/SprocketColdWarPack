using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal sealed record SpallSettings(double ConeMultiplier = .3, double ThinCalibres = .15,
    double MiddleCalibres = .75, double ThickCalibres = 1.5, double ThinRatio = .08,
    double MiddleRatio = 1, double ThickRatio = 1.25, double CurveExponent = 3,
    int ApheFragmentCount = 96, double ApheFragmentMass = 2.4, double ApheFragmentSpeed = 650,
    int ApheFragmentK = 8000, double RemainingPenetrationExponent = 1,
    double ApheFuseRhaMm = 25, double ApheFuseDelayMilliseconds = .5,
    double ConeEnergyWidening = .15, double ApheSpallMultiplier = 4, bool ApheExplosionEffect = true,
    double ApheConeHalfAngleDegrees = 90, double ApheExplosionScale = .65,
    bool ApfsdsDisableClassicNormalization = true, bool ApfsdsPlateDeflection = false,
    double ApfsdsDeflectionMaximumDegrees = 8, double ApfsdsDeflectionMinimumObliquityDegrees = 30,
    double ApObliquityExponent = 1.33, double ApheObliquityExponent = 1.33, double ApcObliquityExponent = 1.33,
    double ApcbcObliquityExponent = 1.15, double ApcrObliquityExponent = 1.60, double ApdsObliquityExponent = 1.00,
    double ApfsdsObliquityExponent = 1.00, double HeatObliquityExponent = 1.00, double HeshObliquityExponent = 0.00,
    SpacedKineticSettings? SpacedKinetic = null, FragmentRicochetSettings? FragmentRicochet = null,
    double MinSpallGapMm = 50);
// WW2 spaced-armour effect against full-bore AP. k is the maximum retention
// loss at a fully developed skirt gap; APC/APCBC lose their cap instead and
// continue as bare AP (Ap/Apc/Apcbc are the flat fallback if that write fails).
internal sealed record SpacedKineticSettings(double Ap=0,double Aphe=0,double Apc=.03,double Apcbc=.03,
    double Apcr=.15,double Apds=.03,double Apfsds=0,bool CapStrip=true)
{
    internal static readonly SpacedKineticSettings Default=new();
}
internal readonly record struct SpacedKineticEffect(double Retention,bool CapStrip,string Label);
internal sealed class SpacedKineticGate
{
    private readonly HashSet<IntPtr> shots=new();
    internal bool TryClaim(IntPtr simulation)=>shots.Count<16384&&shots.Add(simulation);
    internal void Clear()=>shots.Clear();
}
internal static class SpacedKineticModel
{
    internal const double GapCalibres=3,PlateCalibres=.25;
    // A weld seam or assembly line is not spaced armour: a gap only pays from
    // a quarter of the full scale. The cap needs half the scale before it lets go.
    internal const double MinimumScale=.25,CapStripScale=.5;
    internal static bool Applies(string behavior)=>behavior is "ap" or "aphe" or "apfsds";
    internal static bool Applies(ShellProfile? profile)=>profile!=null&&Applies(profile.Behavior);
    internal static double Scale(double calibreMm,double plateRhaMm,double gapMm)
    {
        if(!double.IsFinite(calibreMm)||calibreMm<=0||!double.IsFinite(plateRhaMm)||plateRhaMm<=0||
            !double.IsFinite(gapMm)||gapMm<=0)return 0;
        return Math.Min(1,gapMm/(GapCalibres*calibreMm))*Math.Min(1,plateRhaMm/(PlateCalibres*calibreMm));
    }
    internal static SpacedKineticEffect Evaluate(SpacedKineticSettings s,string behavior,string kineticKind,
        double calibreMm,double plateRhaMm,double gapMm)
    {
        if(!Applies(behavior))return new(1,false,"");
        var scale=Scale(calibreMm,plateRhaMm,gapMm);
        if(scale<MinimumScale)return new(1,false,"");
        if(behavior=="apfsds")return new(1,false,"");
        if(behavior=="aphe")return Loss(s.Aphe,scale,"yaw");
        var kind=string.IsNullOrEmpty(kineticKind)?"ap":kineticKind;
        if((kind is "apc" or "apcbc")&&s.CapStrip&&scale>=CapStripScale)return new(1,true,"cap");
        var k=kind switch{"apc"=>s.Apc,"apcbc"=>s.Apcbc,"apcr"=>s.Apcr,"apds"=>s.Apds,_=>s.Ap};
        return Loss(k,scale,"yaw");
    }
    internal static bool HasEffect(SpacedKineticEffect effect)=>effect.CapStrip||effect.Retention<1-1e-9;
    // The once-per-shot gate must stay untouched when a seam is too narrow to
    // matter, otherwise a later real skirt plate can no longer pay.
    internal static bool TryClaim(SpacedKineticGate gate,IntPtr shot,SpacedKineticEffect effect)=>
        HasEffect(effect)&&gate.TryClaim(shot);
    internal static double KineticObliquityExponent(ShellProfile profile,SpallSettings settings,bool capStripped)=>
        capStripped?settings.ApObliquityExponent:SpallBalance.ObliquityExponent(profile.Behavior,profile.KineticKind,settings);
    internal static double Flat(SpacedKineticSettings s,string kineticKind,double scale)
    {
        var kind=string.IsNullOrEmpty(kineticKind)?"ap":kineticKind;
        var k=kind switch{"apc"=>s.Apc,"apcbc"=>s.Apcbc,"apcr"=>s.Apcr,"apds"=>s.Apds,_=>s.Ap};
        return 1-Math.Clamp(double.IsFinite(k)?k:0,0,1)*Math.Clamp(double.IsFinite(scale)?scale:0,0,1);
    }
    private static SpacedKineticEffect Loss(double k,double scale,string label)=>
        new(1-Math.Clamp(double.IsFinite(k)?k:0,0,1)*scale,false,label);
}
internal static class SpallBalance
{
    internal static void Validate(SpallSettings s)
    {
        void Range(double x, double min, double max) { if (!double.IsFinite(x) || x < min || x > max) throw new FormatException("Invalid spall setting."); }
        Range(s.ConeMultiplier,.01,1); Range(s.ThinCalibres,.01,4); Range(s.MiddleCalibres,.02,8); Range(s.ThickCalibres,.03,12);
        if (!(s.ThinCalibres < s.MiddleCalibres && s.MiddleCalibres < s.ThickCalibres)) throw new FormatException("Spall thickness anchors must increase.");
        Range(s.ThinRatio,.001,1); Range(s.MiddleRatio,.01,3); Range(s.ThickRatio,.01,5); Range(s.CurveExponent,1,8);
        if (!(s.ThinRatio <= s.MiddleRatio && s.MiddleRatio <= s.ThickRatio)) throw new FormatException("Spall ratios must increase.");
        Range(s.ApheFragmentCount,1,256); Range(s.ApheFragmentMass,.001,10); Range(s.ApheFragmentSpeed,10,1500); Range(s.ApheFragmentK,4000,65535);
        Range(s.RemainingPenetrationExponent,.25,4);
        Range(s.ApheFuseRhaMm,1,200); Range(s.ApheFuseDelayMilliseconds,0,5); Range(s.ConeEnergyWidening,0,.5);
        Range(s.ApheSpallMultiplier,1,12);
        Range(s.ApheConeHalfAngleDegrees,1,90);Range(s.ApheExplosionScale,.1,1);
        Range(s.ApfsdsDeflectionMaximumDegrees,0,15);Range(s.ApfsdsDeflectionMinimumObliquityDegrees,0,80);
        Range(s.ApObliquityExponent,0,2);Range(s.ApheObliquityExponent,0,2);Range(s.ApcObliquityExponent,0,2);
        Range(s.ApcbcObliquityExponent,0,2);Range(s.ApcrObliquityExponent,0,2);Range(s.ApdsObliquityExponent,0,2);
        Range(s.ApfsdsObliquityExponent,0,2);Range(s.HeatObliquityExponent,0,2);Range(s.HeshObliquityExponent,0,2);
        var spaced=s.SpacedKinetic??SpacedKineticSettings.Default;
        Range(spaced.Ap,0,1);Range(spaced.Aphe,0,1);Range(spaced.Apc,0,1);Range(spaced.Apcbc,0,1);
        Range(spaced.Apcr,0,1);Range(spaced.Apds,0,1);Range(spaced.Apfsds,0,1);
        Range(s.MinSpallGapMm,0,1000);
        if(s.FragmentRicochet is {} ricochet)FragmentRicochetModel.Validate(ricochet);
    }
    // The native AP obliquity exponent is 1.33. Vanilla AP has no profile and
    // therefore never reaches this table; custom profiles opt in per kind.
    // 0.22.25: native semantics (LOS-MAP.md): the defeated normal thickness is P·cos^e before overmatch, so 1 is pure LOS
    // and 0 only the normal thickness. 0.22.28: APFSDS and HEAT (HEAT-FS, tandem, ATGM) default to 1 (native LOS) at the
    // source; HESH stays 0 (user 03-11, normal thickness). The old obliquityCorrection key is tolerated and ignored.
    internal static double ObliquityExponent(string behavior,string? kineticKind,SpallSettings s)=>
        behavior switch
        {
            "apfsds"=>s.ApfsdsObliquityExponent,
            "heat" or "atgm" or "atgm_gun"=>s.HeatObliquityExponent,
            "hesh"=>s.HeshObliquityExponent,
            "aphe"=>s.ApheObliquityExponent,
            "ap"=>kineticKind switch
            {
                "apc"=>s.ApcObliquityExponent,"apcbc"=>s.ApcbcObliquityExponent,
                "apcr"=>s.ApcrObliquityExponent,"apds"=>s.ApdsObliquityExponent,
                _=>s.ApObliquityExponent
            },
            _=>s.ApObliquityExponent
        };
    // Single pure decision for the layer follower: chemical rounds need the
    // chemical gap ledger, kinetic rounds only the spaced-plate step. Vanilla AP
    // has no profile and therefore never follows layers.
    internal static bool IsChemicalLayerBehavior(string behavior)=>behavior is "heat" or "hesh";
    internal static bool FollowsLayers(ShellProfile? profile)=>profile is {} p &&
        (IsChemicalLayerBehavior(ShellBalance.ImpactBehavior(p))||SpacedKineticModel.Applies(p));
    internal static double ConeFactor(double remainingEnergyFraction, SpallSettings s)
    {
        if(!double.IsFinite(remainingEnergyFraction) || remainingEnergyFraction<0) throw new ArgumentOutOfRangeException(nameof(remainingEnergyFraction));
        return s.ConeMultiplier*(1+s.ConeEnergyWidening*Math.Clamp(remainingEnergyFraction,0,1));
    }
    internal static double RemainingPenetrationRatio(double remainingFraction, SpallSettings s)
    {
        if (!double.IsFinite(remainingFraction) || remainingFraction < 0) throw new ArgumentOutOfRangeException(nameof(remainingFraction));
        return s.ThinRatio + (s.ThickRatio-s.ThinRatio)*Math.Pow(1-Math.Clamp(remainingFraction,0,1),s.RemainingPenetrationExponent);
    }
    internal static double ThicknessRatio(double pathCalibres, SpallSettings s)
    {
        if (!double.IsFinite(pathCalibres) || pathCalibres < 0) throw new ArgumentOutOfRangeException(nameof(pathCalibres));
        if (pathCalibres <= s.ThinCalibres) return s.ThinRatio;
        if (pathCalibres >= s.ThickCalibres) return s.ThickRatio;
        if (pathCalibres <= s.MiddleCalibres)
            return s.ThinRatio + (s.MiddleRatio-s.ThinRatio)*Math.Pow((pathCalibres-s.ThinCalibres)/(s.MiddleCalibres-s.ThinCalibres),s.CurveExponent);
        return s.MiddleRatio + (s.ThickRatio-s.MiddleRatio)*(1-Math.Pow(1-(pathCalibres-s.MiddleCalibres)/(s.ThickCalibres-s.MiddleCalibres),s.CurveExponent));
    }
    // Confirmed native AP volume: clamp(path / projectileDiameter - .04, 0, .26) * diameter * area.
    internal static double ReferenceApVolume(double gunDiameter, double materialPath)
    {
        if (!double.IsFinite(gunDiameter) || gunDiameter <= 0 || !double.IsFinite(materialPath) || materialPath < 0)
            throw new ArgumentOutOfRangeException(nameof(gunDiameter));
        return Math.Clamp(materialPath/gunDiameter-.04,0,.26)*gunDiameter*Math.PI*gunDiameter*gunDiameter*.25;
    }
    internal static (float X,float Y,float Z) Sphere(double azimuthUnit,double polarUnit)
    {
        if(!double.IsFinite(azimuthUnit) || !double.IsFinite(polarUnit) || azimuthUnit<0 || azimuthUnit>1 || polarUnit<0 || polarUnit>1)
            throw new ArgumentOutOfRangeException(nameof(azimuthUnit));
        var z=polarUnit*2-1;
        var phi=azimuthUnit*Math.PI*2;
        var radius=Math.Sqrt(Math.Max(0,1-z*z));
        return ((float)(radius*Math.Cos(phi)),(float)(radius*Math.Sin(phi)),(float)z);
    }
    internal static (float X,float Y,float Z) Cone(double azimuthUnit,double polarUnit,double halfAngleDegrees)
    {
        if(!double.IsFinite(halfAngleDegrees) || halfAngleDegrees<1 || halfAngleDegrees>90)
            throw new ArgumentOutOfRangeException(nameof(halfAngleDegrees));
        if(!double.IsFinite(azimuthUnit) || !double.IsFinite(polarUnit) || azimuthUnit<0 || azimuthUnit>1 || polarUnit<0 || polarUnit>1)
            throw new ArgumentOutOfRangeException(nameof(polarUnit));
        var z=1-polarUnit*(1-Math.Cos(halfAngleDegrees*Math.PI/180));
        var radius=Math.Sqrt(Math.Max(0,1-z*z));var phi=azimuthUnit*Math.PI*2;
        return ((float)(radius*Math.Cos(phi)),(float)(radius*Math.Sin(phi)),(float)z);
    }
    private static readonly string[] DeprecatedKeys={
        "apheFragmentCount","apheFragmentMass","apheFragmentSpeed","apheFragmentK",
        "apheFuseRhaMm","apheFuseDelayMilliseconds","apfsdsPlateDeflection",
        "apfsdsDeflectionMaximumDegrees","apfsdsDeflectionMinimumObliquityDegrees"};
    internal static readonly string[] ObliquityKeys={"ap","aphe","apc","apcbc","apcr","apds","apfsds","heat","hesh"};
    private static readonly string[] ObliquityFields={"apObliquityExponent","apheObliquityExponent","apcObliquityExponent",
        "apcbcObliquityExponent","apcrObliquityExponent","apdsObliquityExponent","apfsdsObliquityExponent",
        "heatObliquityExponent","heshObliquityExponent"};
    internal static string ToJson(SpallSettings s)
    {
        var options=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
        var node=JsonSerializer.SerializeToNode(s,options)!.AsObject();
        // Retired parser-only fields stay accepted, but new defaults never write them.
        foreach(var key in DeprecatedKeys)node.Remove(key);
        if(s.SpacedKinetic is {} spaced)node["spacedKinetic"]=JsonSerializer.SerializeToNode(spaced,options);
        else node.Remove("spacedKinetic");
        if(s.FragmentRicochet is {} ricochet)node["fragmentRicochet"]=FragmentRicochetModel.ToNode(ricochet);
        else node.Remove("fragmentRicochet");
        var values=new[]{s.ApObliquityExponent,s.ApheObliquityExponent,s.ApcObliquityExponent,
            s.ApcbcObliquityExponent,s.ApcrObliquityExponent,s.ApdsObliquityExponent,
            s.ApfsdsObliquityExponent,s.HeatObliquityExponent,s.HeshObliquityExponent};
        var exponents=new JsonObject();
        for(var i=0;i<ObliquityKeys.Length;i++){node.Remove(ObliquityFields[i]);exponents[ObliquityKeys[i]]=values[i];}
        node["obliquityExponents"]=exponents;
        return node.ToJsonString(new JsonSerializerOptions{WriteIndented=true});
    }
    internal static SpallSettings Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);
        var root=doc.RootElement;
        var expected=new HashSet<string>(StringComparer.Ordinal){"coneMultiplier","thinCalibres","middleCalibres","thickCalibres","thinRatio","middleRatio","thickRatio","curveExponent"};
        if(root.ValueKind != JsonValueKind.Object) throw new FormatException("Expected spall settings object.");
        var optional=new HashSet<string>(StringComparer.Ordinal){"apheFragmentCount","apheFragmentMass","apheFragmentSpeed","apheFragmentK","remainingPenetrationExponent","apheFuseRhaMm","apheFuseDelayMilliseconds","coneEnergyWidening","apheSpallMultiplier","apheExplosionEffect","apheConeHalfAngleDegrees","apheExplosionScale","apfsdsDisableClassicNormalization","apfsdsPlateDeflection","apfsdsDeflectionMaximumDegrees","apfsdsDeflectionMinimumObliquityDegrees","spacedKinetic","fragmentRicochet","minSpallGapMm","obliquityCorrection"};
        var hasExponents=false;
        foreach(var p in root.EnumerateObject())
        {
            if(p.Name=="obliquityExponents"){if(hasExponents)throw new FormatException("Duplicate spall setting: obliquityExponents");hasExponents=true;continue;}
            if(!expected.Remove(p.Name) && !optional.Remove(p.Name)) throw new FormatException("Unknown or duplicate spall setting: "+p.Name);
        }
        if(expected.Count != 0) throw new FormatException("Missing spall settings.");
        var values=new double[]{1.33,1.33,1.33,1.15,1.60,1.00,1.00,1.00,.00};
        if(hasExponents)
        {
            var exponents=root.GetProperty("obliquityExponents");
            if(exponents.ValueKind!=JsonValueKind.Object)throw new FormatException("obliquityExponents must be an object.");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in exponents.EnumerateObject())
            {
                var index=Array.IndexOf(ObliquityKeys,p.Name);
                if(index<0||!seen.Add(p.Name))throw new FormatException("Unknown or duplicate obliquity exponent: "+p.Name);
                if(p.Value.ValueKind!=JsonValueKind.Number||!p.Value.TryGetDouble(out var value)||!double.IsFinite(value)||value<0||value>2)
                    throw new FormatException("Obliquity exponent is outside 0-2: "+p.Name);
                values[index]=value;
            }
        }
        var spaced=root.TryGetProperty("spacedKinetic",out var spacedNode)?ParseSpaced(spacedNode):null;
        var ricochet=root.TryGetProperty("fragmentRicochet",out _)?FragmentRicochetModel.Parse(JsonNode.Parse(json)!.AsObject()["fragmentRicochet"]):null;
        // 0.22.28: obliquityCorrection (0.22.25-0.22.27) is accepted with any content and ignored.
        var node=JsonNode.Parse(json)!.AsObject();node.Remove("obliquityExponents");node.Remove("spacedKinetic");node.Remove("fragmentRicochet");node.Remove("obliquityCorrection");
        var s=JsonSerializer.Deserialize<SpallSettings>(node.ToJsonString(),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})!;
        s=s with{ApObliquityExponent=values[0],ApheObliquityExponent=values[1],ApcObliquityExponent=values[2],
            ApcbcObliquityExponent=values[3],ApcrObliquityExponent=values[4],ApdsObliquityExponent=values[5],
            ApfsdsObliquityExponent=values[6],HeatObliquityExponent=values[7],HeshObliquityExponent=values[8],
            SpacedKinetic=spaced,FragmentRicochet=ricochet};
        Validate(s); return s;
    }
    private static SpacedKineticSettings ParseSpaced(JsonElement node)
    {
        if(node.ValueKind!=JsonValueKind.Object)throw new FormatException("spacedKinetic must be an object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        double Read(string name,double fallback)
        {
            if(!seen.Add(name))throw new FormatException("Duplicate spacedKinetic field: "+name);
            if(!node.TryGetProperty(name,out var value))return fallback;
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var number)||!double.IsFinite(number)||number<0||number>1)
                throw new FormatException("spacedKinetic value is outside 0-1: "+name);
            return number;
        }
        var defaults=SpacedKineticSettings.Default;
        var ap=Read("ap",defaults.Ap);var aphe=Read("aphe",defaults.Aphe);var apc=Read("apc",defaults.Apc);
        var apcbc=Read("apcbc",defaults.Apcbc);var apcr=Read("apcr",defaults.Apcr);var apds=Read("apds",defaults.Apds);
        var apfsds=Read("apfsds",defaults.Apfsds);var capStrip=defaults.CapStrip;
        if(seen.Add("capStrip")&&node.TryGetProperty("capStrip",out var cap))
        {
            if(cap.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new FormatException("spacedKinetic capStrip must be a boolean.");
            capStrip=cap.GetBoolean();
        }
        foreach(var property in node.EnumerateObject())
            if(seen.Add(property.Name))throw new FormatException("Unknown spacedKinetic field: "+property.Name);
        return new(ap,aphe,apc,apcbc,apcr,apds,apfsds,capStrip);
    }
}

