namespace SprocketExpandedBallistics;

internal sealed record EraStackSettings(bool Enabled,double UnitNormalThicknessMm,double ThicknessToleranceMm,int MaximumStages);
internal static class FaceEraStackPolicy
{
    internal static int Stages(double declaredThickness,EraStackSettings? settings)
    {
        if(!double.IsFinite(declaredThickness)||declaredThickness<=0)return 0;
        if(settings is not {Enabled:true})return 1;
        if(!double.IsFinite(settings.UnitNormalThicknessMm)||settings.UnitNormalThicknessMm<=0||
            !double.IsFinite(settings.ThicknessToleranceMm)||settings.ThicknessToleranceMm<0||
            settings.ThicknessToleranceMm>Math.Min(5,settings.UnitNormalThicknessMm*.05)||settings.MaximumStages is <1 or >16)return 0;
        var n=Math.Floor((declaredThickness+settings.ThicknessToleranceMm)/settings.UnitNormalThicknessMm);
        if(n<1||n>settings.MaximumStages)return 0;
        return (int)n;
    }
}
