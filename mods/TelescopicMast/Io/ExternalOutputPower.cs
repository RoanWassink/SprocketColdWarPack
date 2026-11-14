namespace SprocketTelescopicMast.Io;

/// <summary>
/// On/off state of one external output part. The flow applies only on a change; the designer
/// return and part release turn a still-on output off exactly once (IO-API.md §6 M4).
/// Unity-free so the "one apply(false)" contract is tested directly.
/// </summary>
public sealed class ExternalOutputPower
{
    private readonly OutputChangeGate gate = new();
    public bool Initialized => gate.Initialized;
    public bool LastApplied => gate.Last;
    public bool ShouldApply(bool on) => gate.ShouldApply(on);
    public void Applied(bool on) => gate.AppliedState(on);
    public void Reset() => gate.Reset();

    /// <summary>True exactly once while the last applied state was on.</summary>
    public bool TurnOff()
    {
        if (!gate.Initialized || !gate.Last) return false;
        gate.AppliedState(false);
        return true;
    }
}
