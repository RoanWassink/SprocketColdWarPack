namespace SprocketExpandedBallistics;
// Runtime-only pointer ledger: a native override becomes the new restore value.
// It is never written into a blueprint or retained beyond the camera session.
internal sealed class AtgmSightReferenceState
{
    internal IntPtr RestoreTarget {get;private set;}
    internal IntPtr WrittenTarget {get;private set;}
    internal AtgmSightReferenceState(IntPtr original)=>RestoreTarget=original;
    internal bool Redirecting(IntPtr current,IntPtr target)
    {
        if(target==IntPtr.Zero)throw new ArgumentOutOfRangeException(nameof(target));
        var nativeChanged=WrittenTarget==IntPtr.Zero||current!=WrittenTarget;
        if(nativeChanged)RestoreTarget=current;
        WrittenTarget=target;return nativeChanged;
    }
    internal bool CanRestore(IntPtr current)=>WrittenTarget!=IntPtr.Zero&&current==WrittenTarget;
}
