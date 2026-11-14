namespace SprocketExpandedBallistics;
internal static class AtgmGuidanceAxisChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var own=new AtgmAimingAxes((IntPtr)100,(IntPtr)200);
        var yawShared=new[]{new AtgmAimingAxes((IntPtr)100,(IntPtr)300)};
        check(!AtgmGuidanceAxisOwnership.SharedElevation(own,yawShared),"shared turret yaw must not reject independent optics");
        check(AtgmGuidanceAxisOwnership.ExclusiveAxes(own,yawShared).SetEquals(new[]{(IntPtr)200}),"shared yaw stays native while dedicated elevation can hold");
        var fixedTube=new AtgmAimingAxes((IntPtr)100,IntPtr.Zero);
        check(!AtgmGuidanceAxisOwnership.SharedElevation(fixedTube,yawShared)&&AtgmGuidanceAxisOwnership.ExclusiveAxes(fixedTube,yawShared).Count==0,"fixed tube on a shared turret binds without suppressing the turret motor");
        var pitchShared=new[]{new AtgmAimingAxes((IntPtr)400,(IntPtr)200)};
        check(AtgmGuidanceAxisOwnership.SharedElevation(own,pitchShared),"shared gun elevation remains a binding conflict");
        check(AtgmGuidanceAxisOwnership.ExclusiveAxes(own,pitchShared).SetEquals(new[]{(IntPtr)100}),"an elevation used by another cannon is never cleared");
        var none=Array.Empty<AtgmAimingAxes>();
        check(AtgmGuidanceAxisOwnership.ExclusiveAxes(own,none).SetEquals(new[]{(IntPtr)100,(IntPtr)200}),"unshared mount keeps the previous fixed-angle control");
        check(AtgmGuidanceAxisOwnership.ExclusiveAxes(new(IntPtr.Zero,IntPtr.Zero),none).Count==0,"missing axes cannot target another controller");
        check(AtgmGuidanceAxisOwnership.ExclusiveAxes(own,new[]{new AtgmAimingAxes((IntPtr)400,(IntPtr)100)}).SetEquals(new[]{(IntPtr)200}),"cross-axis sharing cannot capture another weapon's drive");
        check(AtgmGuidanceAxisOwnership.ExclusiveAxes(new((IntPtr)100,(IntPtr)100),none).Count==1,"duplicate axis references are held once");
        check(AtgmGuidanceAxisOwnership.ExclusiveAxes(own,yawShared).SetEquals(new[]{(IntPtr)200})&&AtgmGuidanceAxisOwnership.ExclusiveAxes(own,none).Count==2,"rebuild ownership snapshot refreshes after the other weapon is removed");
    }
}
