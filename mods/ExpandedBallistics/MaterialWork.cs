namespace SprocketExpandedBallistics;

// Value-only shared capacity calculation: loose plates and authored cassettes must
// pay the same work. Dose is dimensionless, relative to the calibrated reference.
internal readonly record struct MaterialWork(double Joules,double JetMm,double ErosionShare,double DisturbanceShare,double YawDegrees);
internal static class MaterialWorkMath
{
    internal static double ClassicDose(MaterialReactionRecipe recipe,double thickness,double angle,double coupling,bool intact)
    {
        var dose=2*recipe.LossPerReference/(1-recipe.LossPerReference)*thickness/(thickness+recipe.ReferenceThicknessMm)*MaterialReactionModel.CooperativeAngle(recipe,angle)*coupling;
        return dose*(recipe.Mode=="backed"&&!intact?recipe.DegradedFraction:1);
    }
    internal static double ClassicDirectLoss(MaterialReactionRecipe recipe,double thickness,double angle,double catalogueCap)=>
        Math.Min(catalogueCap,Math.Clamp(recipe.LossPerReference*thickness/recipe.ReferenceThicknessMm*MaterialReactionModel.DirectAngle(recipe,angle),0,recipe.MaximumLoss));
    internal static MaterialWork Calculate(MaterialReactionRecipe recipe,MaterialWorkCalibration calibration,
        bool rod,double thickness,double angle,double coupling,bool intact,double diameter)
    {
        if(!double.IsFinite(thickness)||thickness<recipe.MinimumThicknessMm||thickness>recipe.MaximumThicknessMm||
            !double.IsFinite(angle)||angle<0||angle>=90||!double.IsFinite(coupling)||coupling<=0)return default;
        bool cooperative=recipe.Mode is "backed" or "sandwich";
        double angular=cooperative?MaterialReactionModel.CooperativeAngle(recipe,angle):MaterialReactionModel.DirectAngle(recipe,angle);
        var referenceDose=recipe.LossPerReference/(1-recipe.LossPerReference)*(rod?1.6:1);
        var referenceLoss=cooperative?referenceDose/(1+referenceDose):recipe.LossPerReference;
        if(!cooperative)referenceLoss=Math.Min(referenceLoss,recipe.MaximumLoss);
        var fraction=rod?1-Math.Pow(1-referenceLoss,2/1.43):referenceLoss;
        var reference=recipe.ReferenceThicknessMm;
        double Capacity(double t)=>t+calibration.InterfaceWeight*reference*(1-Math.Exp(-t/reference));
        var scale=Capacity(thickness)/Capacity(reference)*angular*coupling*(recipe.Mode=="backed"&&!intact?recipe.DegradedFraction:1);
        // Cap f*S in the same units as the reference dose. This preserves the
        // live reference-thickness calibration; it only bounds thicker stacks.
        var dose=fraction*scale;
        if(cooperative)dose=Math.Min(dose,recipe.MaximumLoss/(1-recipe.MaximumLoss));
        if(!double.IsFinite(dose)||dose<=0)return default;
        var area=Math.PI*.25*diameter*diameter;
        var joules=.5*calibration.RodDensityKgM3*calibration.RodLengthM*calibration.RodSpeedMps*calibration.RodSpeedMps*area*dose;
        var share=recipe.WorkErosionShare??(recipe.Mode=="backed"?calibration.BackedErosionShare:recipe.Mode=="sandwich"?calibration.SandwichErosionShare:calibration.DirectErosionShare);
        return new(rod?joules:0,rod?0:calibration.JetBudgetMm*dose,share,recipe.RodDisturbanceShare,recipe.RodYawDegreesAtReference);
    }
}
