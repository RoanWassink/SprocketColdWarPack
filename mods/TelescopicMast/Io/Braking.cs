namespace SprocketTelescopicMast.Io;

// Pure brake decision. The game adapter (HingeBraking in HingeIo.cs) fills this from
// PowertrainBehaviour.action and, when the layout is ClutchBraking, from the two brake
// fractions of its force-update job. No Unity or IL2CPP types here.
//
// DriveAction values in the installed interop:
// Stopped=0, Launching=1, Pivoting=2, Accelerating=3, Steering=4, Shifting=5,
// Coasting=6, Braking=7. UpdateBrake returns Braking (7).
//
// A one-sided brake (steering with one track braked) must not count as vehicle braking.
// When fractions are available they can veto an asymmetric state; when they are not,
// the action field alone is used.

public readonly record struct BrakingFields(
    bool HasAction, int Action,
    bool HasLastAction, int LastAction,
    bool HasFractions, float LeftBrakeFraction, float RightBrakeFraction);

public static class BrakingPolicy
{
    public const int BrakingAction = 7; // PowertrainBehaviour.DriveAction.Braking
    public const float SideEpsilon = 0.01f;
    public const float AsymmetryLimit = 0.25f;

    /// <summary>
    /// True when the current action is Braking and the brake is not clearly one-sided.
    /// lastAction is deliberately not OR-ed in: it is the previous state and would keep
    /// the signal on for one extra tick after the driver releases the brake.
    /// </summary>
    public static bool IsBraking(BrakingFields fields)
    {
        if (!fields.HasAction || fields.Action != BrakingAction) return false;
        if (!fields.HasFractions) return true;

        float left = MathF.Abs(fields.LeftBrakeFraction);
        float right = MathF.Abs(fields.RightBrakeFraction);
        bool leftOn = left >= SideEpsilon, rightOn = right >= SideEpsilon;
        if (leftOn != rightOn) return false;             // one track only: steering, not braking
        if (!leftOn) return true;                        // both ~0: action still says Braking
        float larger = MathF.Max(left, right);
        return MathF.Abs(left - right) <= larger * AsymmetryLimit;
    }
}
