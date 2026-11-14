using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

// 0.22.17 point 7: a spall fragment that ricochets keeps its energy in native code, so HEAT fragments bounce
// between two plates without losing speed. Every ricochet now costs energy and a fragment stops after a
// maximum number of them. Pure rules only; the hook is in RuntimeSpall.
internal sealed record FragmentRicochetSettings(bool Enabled=true,double EnergyLoss=.5,int MaxRicochets=3,
    string[]? Behaviors=null,string[]? KineticKinds=null,bool AffectsOriginal=false,double ThinGapKillMm=25,double GrazingDoubleDegrees=70)
{
    internal static readonly string[] DefaultBehaviors={"heat","hesh","he","aphe","apfsds"};
    internal static readonly string[] DefaultKineticKinds={"apds","apcr"};
    internal static readonly FragmentRicochetSettings Default=new();
    internal string[] BehaviorList=>Behaviors??DefaultBehaviors;
    internal string[] KindList=>KineticKinds??DefaultKineticKinds;
}
internal enum RicochetOutcome{Scale,Kill}
// A bounced fragment that made no child: either it really ricocheted in place (Deflected) or it simply stuck (Embedded wins over Deflected).
internal enum InPlaceKind{None,Embedded,Ricochet}
internal static class FragmentRicochetModel
{
    internal static readonly string[] Keys={"enabled","energyLoss","maxRicochets","behaviors","kineticKinds","affectsOriginal","thinGapKillMm","grazingDoubleDegrees"};
    // Speed factor for a loss of kinetic energy: E ~ v^2.
    internal static double SpeedFactor(double energyLoss)=>Math.Sqrt(1-Math.Clamp(double.IsFinite(energyLoss)?energyLoss:0,0,1));
    // A null impact context is vanilla ammunition (vanilla AP): always native. Mod AP follows the kind list.
    internal static bool Applies(FragmentRicochetSettings s,bool hasImpact,string? behavior,string? kineticKind)
    {
        if(!s.Enabled||!hasImpact||string.IsNullOrEmpty(behavior))return false;
        if(behavior=="ap")return !string.IsNullOrEmpty(kineticKind)&&s.KindList.Contains(kineticKind,StringComparer.Ordinal);
        return s.BehaviorList.Contains(behavior,StringComparer.Ordinal);
    }
    // Ricochets in the chain that ends at the fragment that just ricocheted: itself plus every deflected ancestor.
    // Parent links must strictly decrease and the walk is bounded, so a cycle or a forward link ends it.
    internal static int CountChain(Func<int,int> parentOf,Func<int,bool> deflected,int index,int fragmentCount)
    {
        int count=1,current=index;
        for(int step=0;step<64;step++)
        {
            var parent=parentOf(current);
            if(parent<0||parent>=current||parent>=fragmentCount)break;
            if(deflected(parent))count++;
            current=parent;
        }
        return count;
    }
    // 0.22.22: a ricochet at a grazing incidence (more than GrazingDoubleDegrees from the surface normal) counts twice, so a fragment that skims
    // between two nearly parallel plates runs out of ricochets twice as fast. The incidence angle follows from the turn the fragment made:
    // a reflection turns it by 180 - 2*theta, so theta = (180 - turn)/2. Unknown directions count once.
    internal static int Weight(FragmentRicochetSettings s,double incomingX,double incomingY,double incomingZ,double outgoingX,double outgoingY,double outgoingZ)
    {
        var a=Math.Sqrt(incomingX*incomingX+incomingY*incomingY+incomingZ*incomingZ);
        var b=Math.Sqrt(outgoingX*outgoingX+outgoingY*outgoingY+outgoingZ*outgoingZ);
        if(!double.IsFinite(a)||!double.IsFinite(b)||a<1e-6||b<1e-6)return 1;
        var dot=(incomingX*outgoingX+incomingY*outgoingY+incomingZ*outgoingZ)/(a*b);
        var turn=Math.Acos(Math.Clamp(dot,-1,1))*180/Math.PI;
        var theta=(180-turn)/2;
        return theta>s.GrazingDoubleDegrees?2:1;
    }
    internal static InPlaceKind ClassifyInPlace(bool deflected,bool gouge,bool embedded)=>embedded?InPlaceKind.Embedded:deflected||gouge?InPlaceKind.Ricochet:InPlaceKind.None;
    internal static string InPlaceProbeText(InPlaceKind kind,int fragment,string preFlags,string postFlags,double speed,int count,int max,double factor)=>kind switch
    {
        InPlaceKind.Embedded=>$"[Spall probe] fragment embedded (no ricochet): fragment {fragment} flags {preFlags}->{postFlags} speed {speed:0.0}; no child, speed not scaled.",
        InPlaceKind.Ricochet=>$"[Spall probe] ricochet in place (no child): fragment {fragment} flags {preFlags}->{postFlags} speed {speed:0.0}; chain {count}/{max}; factor {factor:0.000}.",
        _=>""
    };
    internal static RicochetOutcome Next(int count,int max)=>count>max?RicochetOutcome.Kill:RicochetOutcome.Scale;
    internal static void Validate(FragmentRicochetSettings s)
    {
        if(!double.IsFinite(s.EnergyLoss)||s.EnergyLoss<0||s.EnergyLoss>.95)throw new FormatException("fragmentRicochet.energyLoss must be 0-0.95.");
        if(s.MaxRicochets<1||s.MaxRicochets>20)throw new FormatException("fragmentRicochet.maxRicochets must be 1-20.");
        if(!double.IsFinite(s.ThinGapKillMm)||s.ThinGapKillMm<0||s.ThinGapKillMm>500)throw new FormatException("fragmentRicochet.thinGapKillMm must be 0-500 (0 = off).");
        if(!double.IsFinite(s.GrazingDoubleDegrees)||s.GrazingDoubleDegrees<30||s.GrazingDoubleDegrees>90)throw new FormatException("fragmentRicochet.grazingDoubleDegrees must be 30-90 (90 = off).");
        string[] allowedBehaviors={"ap","aphe","apfsds","heat","hesh","he"};
        foreach(var b in s.BehaviorList)if(!allowedBehaviors.Contains(b))throw new FormatException("fragmentRicochet.behaviors: unknown behavior "+b);
        string[] allowedKinds={"ap","apc","apcbc","apcr","apds"};
        foreach(var k in s.KindList)if(!allowedKinds.Contains(k))throw new FormatException("fragmentRicochet.kineticKinds: unknown kind "+k);
    }
    internal static JsonObject ToNode(FragmentRicochetSettings s)
    {
        var node=new JsonObject{["enabled"]=s.Enabled,["energyLoss"]=s.EnergyLoss,["maxRicochets"]=s.MaxRicochets};
        var behaviors=new JsonArray();foreach(var b in s.BehaviorList)behaviors.Add(JsonValue.Create(b));
        var kinds=new JsonArray();foreach(var k in s.KindList)kinds.Add(JsonValue.Create(k));
        node["behaviors"]=behaviors;node["kineticKinds"]=kinds;node["affectsOriginal"]=s.AffectsOriginal;node["thinGapKillMm"]=s.ThinGapKillMm;node["grazingDoubleDegrees"]=s.GrazingDoubleDegrees;
        return node;
    }
    internal static FragmentRicochetSettings Parse(JsonNode? node)
    {
        if(node is not JsonObject obj)throw new FormatException("fragmentRicochet must be an object.");
        foreach(var p in obj)if(!Keys.Contains(p.Key))throw new FormatException("Unknown fragmentRicochet field: "+p.Key);
        var d=FragmentRicochetSettings.Default;
        bool Bool(string k,bool f){if(obj[k] is not {} v)return f;try{return v.GetValue<bool>();}catch(Exception){throw new FormatException("fragmentRicochet."+k+" must be a boolean.");}}
        double Num(string k,double f){if(obj[k] is not {} v)return f;try{return v.GetValue<double>();}catch(Exception){throw new FormatException("fragmentRicochet."+k+" must be a number.");}}
        string[] List(string k,string[] f)
        {
            if(obj[k] is not {} v)return f;
            if(v is not JsonArray a)throw new FormatException("fragmentRicochet."+k+" must be a list.");
            try{return a.Select(x=>x!.GetValue<string>()).ToArray();}catch(Exception){throw new FormatException("fragmentRicochet."+k+" must be a list of text.");}
        }
        var max=Num("maxRicochets",d.MaxRicochets);
        if(max!=Math.Floor(max))throw new FormatException("fragmentRicochet.maxRicochets must be a whole number.");
        var s=new FragmentRicochetSettings(Bool("enabled",d.Enabled),Num("energyLoss",d.EnergyLoss),(int)max,
            List("behaviors",FragmentRicochetSettings.DefaultBehaviors),List("kineticKinds",FragmentRicochetSettings.DefaultKineticKinds),Bool("affectsOriginal",d.AffectsOriginal),Num("thinGapKillMm",d.ThinGapKillMm),Num("grazingDoubleDegrees",d.GrazingDoubleDegrees));
        Validate(s);return s;
    }
    // Adds only what is missing; every key the user has is kept as it is. Returns the same text when nothing is missing.
    internal static string MergeMissing(string json)
    {
        var root=JsonNode.Parse(json) as JsonObject??throw new FormatException("Expected spall settings object.");
        var defaults=ToNode(FragmentRicochetSettings.Default);var changed=false;
        if(!root.ContainsKey("minSpallGapMm")){root["minSpallGapMm"]=SpallGapModel.DefaultMinSpallGapMm;changed=true;}
        if(root["fragmentRicochet"] is JsonObject existing)
        {
            foreach(var p in defaults)
                if(!existing.ContainsKey(p.Key)){existing[p.Key]=JsonNode.Parse(p.Value!.ToJsonString());changed=true;}
        }
        else{root["fragmentRicochet"]=defaults;changed=true;}
        return changed?root.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true}):json;
    }
}
