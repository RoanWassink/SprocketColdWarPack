namespace SprocketExpandedBallistics;
internal static class AtgmIndependentSightPolicy
{
    internal static bool Allows(bool player,int dedicatedMatches,bool ordinaryShares,bool validLauncherProfile)=>
        player&&dedicatedMatches==1&&!ordinaryShares&&validLauncherProfile;
}

internal readonly record struct AtgmAimingAxes(IntPtr Horizontal,IntPtr Vertical);
internal static class AtgmGuidanceAxisOwnership
{
    // A shared turret yaw remains under native control. Shared elevation cannot
    // simultaneously hold a fixed tube angle and preserve the other gun's laying.
    internal static bool SharedElevation(AtgmAimingAxes own,IReadOnlyList<AtgmAimingAxes> others)=>
        Shared(own.Vertical,others);
    internal static HashSet<IntPtr> ExclusiveAxes(AtgmAimingAxes own,IReadOnlyList<AtgmAimingAxes> others)
    {
        var result=new HashSet<IntPtr>();
        foreach(var axis in new[]{own.Horizontal,own.Vertical})
            if(axis!=IntPtr.Zero&&!Shared(axis,others))result.Add(axis);
        return result;
    }
    private static bool Shared(IntPtr axis,IReadOnlyList<AtgmAimingAxes> others)=>
        axis!=IntPtr.Zero&&others.Any(o=>o.Horizontal==axis||o.Vertical==axis);
}
