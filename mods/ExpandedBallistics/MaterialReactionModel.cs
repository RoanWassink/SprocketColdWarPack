namespace SprocketExpandedBallistics;

// 0.22.28: the recipe maths that the composer model (CassetteModel, MaterialWorkMath) and the material tooltip use. The per-shot reaction
// state of 0.22.6-0.22.27 (Observe/Commit, pending backing across plates, combined plans) is replaced by the protection table.
internal static class MaterialReactionModel
{
    internal static bool PassiveMatches(MaterialReactionBinding binding,double density,double rha,double spall)=>
        Near(binding.Density,density)&&Near(binding.RhaFactor,rha)&&Near(binding.SpallFactor,spall);
    private static bool Near(double expected,double actual)=>double.IsFinite(actual)&&Math.Abs(expected-actual)<=Math.Max(.000001,Math.Abs(expected)*.00001);
    internal static double DirectAngle(MaterialReactionRecipe recipe,double angle)
    {
        if(angle<recipe.MinimumAngleDegrees||angle>recipe.MaximumAngleDegrees)return 0;
        if(recipe.FullAngleDegrees<=recipe.MinimumAngleDegrees||angle>=recipe.FullAngleDegrees)return 1;
        return (angle-recipe.MinimumAngleDegrees)/(recipe.FullAngleDegrees-recipe.MinimumAngleDegrees);
    }
    internal const double PlacementToleranceMm=1;
    internal const double FreeBackingGapMm=50;
    internal static double Coupling(double gap,double referenceGap)
    {
        if(!double.IsFinite(gap)||!double.IsFinite(referenceGap)||referenceGap<0||gap<-PlacementToleranceMm)return 0;
        var distance=Math.Max(0,gap-FreeBackingGapMm)/Math.Max(1,referenceGap);
        return 1/(1+distance*distance);
    }
    internal static double CooperativeAngle(MaterialReactionRecipe recipe,double angle)
    {
        if(!double.IsFinite(angle)||angle<0||angle>=90)return 0;
        double onset=1;
        if(recipe.FullAngleDegrees>recipe.MinimumAngleDegrees)
        {
            var t=Math.Clamp((angle-recipe.MinimumAngleDegrees)/(recipe.FullAngleDegrees-recipe.MinimumAngleDegrees),0,1);
            onset=t*t*(3-2*t);
        }
        var sine=Math.Sin(angle*Math.PI/180);
        return onset*(1+sine*sine)*TaperFactor(recipe,angle); // Smooth obliquity gain, bounded below two.
    }
    // 0.22.17 point 4 (option A): cooperative recipes honour maximumAngleDegrees. At or below it nothing changes;
    // above it the gain follows a cosine taper that reaches 0 at 90 degrees. A recipe with max 90 is never tapered.
    internal static double TaperFactor(MaterialReactionRecipe recipe,double angle)
    {
        var max=recipe.MaximumAngleDegrees;
        if(!double.IsFinite(angle)||!double.IsFinite(max)||max>=90||angle<=max)return 1;
        if(angle>=90)return 0;
        return .5*(1+Math.Cos(Math.PI*(angle-max)/(90-max)));
    }
}
