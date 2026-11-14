namespace SprocketExpandedBallistics;

internal static class CassetteProtectionComparison
{
    // Both references use the selected shot angle. Equal mass refers to kg per square metre
    // of plate face, so its RHA thickness must also be divided by cos(theta).
    internal static double? Value(double protectionMm,double depthMm,double kgM2,double angle,int metric,double rhaDensity=7850)
    {
        if(!double.IsFinite(protectionMm)||protectionMm<0||!double.IsFinite(angle)||angle<0||angle>=90)return null;
        if(metric==0)return protectionMm;
        var reference=Reference(depthMm,kgM2,angle,metric,rhaDensity);
        return reference is {} r?protectionMm/r:null;
    }
    // 0.22.21: the RHA thickness (mm along the shot path) the factor is measured against: metric 1 = same line-of-sight thickness,
    // metric 2 = same weight per square metre. Null for the absolute row or when it cannot be read.
    internal static double? Reference(double depthMm,double kgM2,double angle,int metric,double rhaDensity=7850)
    {
        if(!double.IsFinite(angle)||angle<0||angle>=90)return null;
        double normal=metric==1?depthMm:metric==2&&rhaDensity>0?kgM2*1000/rhaDensity:double.NaN;
        double reference=normal/Math.Cos(angle*Math.PI/180);
        return double.IsFinite(reference)&&reference>0?reference:null;
    }
}
