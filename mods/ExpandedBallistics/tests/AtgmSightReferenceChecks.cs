namespace SprocketExpandedBallistics;
internal static class AtgmSightReferenceChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var reference=new AtgmSightReferenceState((IntPtr)100);
        check(reference.Redirecting((IntPtr)100,(IntPtr)200)&&reference.RestoreTarget==(IntPtr)100,"first redirect saves native reference");
        check(!reference.Redirecting((IntPtr)200,(IntPtr)201)&&reference.RestoreTarget==(IntPtr)100,"optical-parent rebuild retains original native restore target");
        check(!reference.CanRestore((IntPtr)200)&&reference.CanRestore((IntPtr)201),"teardown matches the exact last written transform rather than a live optical parent");
        check(reference.Redirecting((IntPtr)101,(IntPtr)202)&&reference.RestoreTarget==(IntPtr)101,"native rebuild override becomes the new restore target");
        check(!reference.CanRestore((IntPtr)999),"teardown never overwrites a later external reference");
        check(reference.Redirecting(IntPtr.Zero,(IntPtr)203)&&reference.RestoreTarget==IntPtr.Zero,"native clear cannot restore a stale old reference");
        var other=new AtgmSightReferenceState((IntPtr)500);other.Redirecting((IntPtr)500,(IntPtr)600);
        check(reference.RestoreTarget==IntPtr.Zero&&other.RestoreTarget==(IntPtr)500,"reference ownership is per camera session");
    }
}
