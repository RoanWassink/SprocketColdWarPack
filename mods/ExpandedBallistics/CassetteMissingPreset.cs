namespace SprocketExpandedBallistics;

internal sealed record CassetteFallback(string RequestedId,CassetteMaterial Passive,bool Exact,string Message);
internal static class CassetteMissingPreset
{
    internal static CassetteFallback? Resolve(string id,bool nativeAvailable,CassetteMaterial? knownPassive)
    {
        if(!id.StartsWith("ebCassette_user_",StringComparison.Ordinal)||nativeAvailable)return null;
        if(knownPassive is {} p&&(!float.IsFinite((float)p.SpallFactor)||p.SpallFactor<0||
            !SprocketExpandedBallistics.Materials.MaterialBalance.Calculate((float)p.RhaFactor,(float)p.Density,(float)p.CostMultiplier).IsValid))knownPassive=null;
        var passive=knownPassive??new CassetteMaterial("rha",7850,1,.0005,2);
        return new(id,passive,knownPassive!=null,knownPassive!=null
            ?"Missing user cassette Technology: native homogeneous RHA-equivalent only; import its .cassette.json to recover the recipe."
            :"Missing user cassette preset and Technology: composition/RHA equivalent unknown; using 1x native RHA at the authored thickness. Import its .cassette.json to recover protection.");
    }
}
