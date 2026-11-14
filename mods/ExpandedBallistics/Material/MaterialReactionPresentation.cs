using System.Globalization;
using SprocketExpandedBallistics;
namespace SprocketExpandedBallistics.Materials;
internal static class MaterialReactionPresentation
{
    private static string N(double n)=>n.ToString("0.###",CultureInfo.InvariantCulture);
    internal static string[] Summary(MaterialReactionRecipe recipe,bool enabled)
    {
        var lines=new List<string>{recipe.Label};
        if(!enabled){lines.Add("Additional material response disabled; native base armour remains active.");return lines.ToArray();}
        if(recipe.Mode=="fragmentAbsorber"){lines.Add("Fragment absorption unavailable; native base armour remains active.");return lines.ToArray();}
        string Tag(string t)=>t switch {"kineticFullBore"=>"full-calibre AP/APHE","shortRod"=>"short rods","longRod"=>"long rods","chemicalJet"=>"HEAT/ATGM jets","fragment"=>"incoming secondary fragments",_=>t};
        lines.Add("Acts on "+string.Join(", ",recipe.ThreatTags.Select(Tag))+".");
        lines.Add("Insert thickness: "+N(recipe.MinimumThicknessMm)+"–"+N(recipe.MaximumThicknessMm)+" mm; impact angle: "+N(recipe.MinimumAngleDegrees)+"–"+N(recipe.MaximumAngleDegrees)+"°.");
        if(recipe.Mode is "backed" or "sandwich")
        {
            if(recipe.MaximumAngleDegrees<90)lines.Add("Above "+N(recipe.MaximumAngleDegrees)+" degrees the angle gain tapers smoothly to 0 at 90 degrees.");
            lines.Add(recipe.Mode=="sandwich"?"Requires steel / insert / steel layers in the cassette recipe.":"Requires a steel backing layer in the cassette recipe.");
            lines.Add("Steel must be at least "+N(recipe.MinimumSteelThicknessMm)+" mm; full gap coupling through 50 mm, then smooth rolloff (half strength at "+N(MaterialReactionModel.FreeBackingGapMm+Math.Max(1,recipe.MaximumGapMm))+" mm).");
            lines.Add("Angled, repeated thin units strengthen the response, with diminishing returns.");
        }
        lines.Add("Protection is calculated in advance from the recipe and impact angle; native armour uses the resulting factor.");
        if(recipe.Mode is "backed" or "sandwich")lines.Add("A loose single layer has no steel backing; use the composer for a supported stack.");
        if(recipe.RodDisturbanceShare>0&&recipe.RodYawDegreesAtReference>0)lines.Add("Rod disturbance and erosion contribute to the precomputed protection; no damage state is carried between plates.");
        return lines.ToArray();
    }
}



