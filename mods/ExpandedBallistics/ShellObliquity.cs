namespace SprocketExpandedBallistics;

// Single decision seam for the scoped native obliquity write. Vanilla AP has no
// profile and is never passed here; the native 1.33 exponent stays untouched.
internal static class ShellObliquity
{
    internal static bool Applies(ShellProfile? profile)=>profile!=null&&profile.Behavior!="he";
    internal static double Target(ShellProfile profile,SpallSettings settings)=>
        SpallBalance.ObliquityExponent(profile.Behavior,profile.KineticKind,settings);
    internal static bool TryTarget(ShellProfile? profile,double nativeExponent,SpallSettings settings,out double target)
    {
        target=nativeExponent;
        if(!Applies(profile)||!double.IsFinite(nativeExponent)||nativeExponent<=0)return false;
        target=Target(profile!,settings);
        return double.IsFinite(target)&&target>=0&&target<=2&&Math.Abs(target-nativeExponent)>1e-6;
    }
}
