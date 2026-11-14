namespace SprocketExpandedBallistics;

// A value-only view of the exact same tag union used by loose layers. A recipe
// checks every relevant tag, but receives work only once when several match.
internal readonly record struct MaterialThreatTags(string Behavior,string ThreatClass,IReadOnlyList<string>? ProfileTags=null,double? LengthToDiameter=null,bool Original=true,string? KineticKind=null)
{
    // A profile-declared class replaces the behaviour default. APCR/APDS use
    // armourThreatTags:"shortRod" so they do not also claim kineticFullBore.
    private bool DeclaresClass()
    {
        var tags=ProfileTags;if(tags==null)return false;
        for(var i=0;i<tags.Count;i++)
            if(tags[i] is "kineticFullBore" or "shortRod" or "longRod" or "chemicalJet")return true;
        return false;
    }
    internal bool Contains(string tag)
    {
        if(!Original)return tag=="fragment";
        if(ThreatClass!="unknown"&&ThreatClass==tag)return true;
        if(tag!="fragment"&&ProfileTags!=null)for(int i=0;i<ProfileTags.Count;i++)if(ProfileTags[i]==tag)return true;
        return Behavior switch
        {
            "heat" or "atgm" or "atgm_gun"=>tag=="chemicalJet",
            "apfsds"=>tag==(LengthToDiameter>=10?"longRod":"shortRod"),
            "ap" or "aphe"=>tag=="kineticFullBore"&&!DeclaresClass(),
            _=>false
        };
    }
    internal bool UsesRod=>Original&&(Contains("longRod")||Contains("shortRod"));
    internal bool UsesJet=>Original&&!UsesRod&&Contains("chemicalJet");
    internal bool UsesClassic=>Original&&!UsesRod&&!Contains("chemicalJet")&&Behavior is not ("he" or "hesh")&&Contains("kineticFullBore");
    internal bool UsesHesh=>Original&&Behavior=="hesh";
    // The one cassette threat bucket a hit pays in. Long rod wins over a
    // simultaneously inferred short-rod tag so a long rod never pays twice.
    internal string Category=>UsesJet?"jet":Contains("longRod")?"longRod":Contains("shortRod")?"shortRod":UsesClassic?"kineticFullBore":"";
    // Concrete shell type for reports; never the internal tag name.
    internal string ShellLabel()
    {
        if(!Original)return "fragment";
        if(UsesHesh)return "HESH";
        if(UsesJet)return "HEAT";
        if(Behavior=="he")return "HE";
        if(Behavior=="apfsds")return "APFSDS";
        var kind=KineticKind??"";
        return kind switch{"apcr"=>"APCR","apds"=>"APDS","apc"=>"APC","apcbc"=>"APCBC",_=>Behavior=="aphe"?"APHE":"AP"};
    }
    internal bool Matches(string[] recipeTags)
    {for(int i=0;i<recipeTags.Length;i++)if(Contains(recipeTags[i]))return true;return false;}
    internal string[] ToArray()
    {
        if(!Original)return new[]{"fragment"};
        var tags=new HashSet<string>(StringComparer.Ordinal);
        if(ThreatClass!="unknown")tags.Add(ThreatClass);
        foreach(var tag in ProfileTags??Array.Empty<string>())if(tag!="fragment")tags.Add(tag);
        var declared=DeclaresClass();
        switch(Behavior)
        {
            case "heat":case "atgm":case "atgm_gun":if(!declared)tags.Add("chemicalJet");break;
            case "apfsds":if(!declared)tags.Add(LengthToDiameter>=10?"longRod":"shortRod");break;
            case "ap":case "aphe":if(!declared)tags.Add("kineticFullBore");break;
        }
        return tags.ToArray();
    }
}
