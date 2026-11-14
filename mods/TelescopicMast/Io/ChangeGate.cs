namespace SprocketTelescopicMast.Io;

/// <summary>
/// Tracks whether an output callback still needs to run. The first observation is always
/// applied once (the spawn/load initial state); after that only a changed on/off state is due.
/// Kept pure so the apply-only-on-change contract is tested without Unity.
/// </summary>
public sealed class OutputChangeGate
{
    public bool Initialized { get; private set; }
    public bool Last { get; private set; }
    public int Applied { get; private set; }

    public bool ShouldApply(bool value) => !Initialized || value != Last;
    public void AppliedState(bool value) { Initialized = true; Last = value; Applied++; }
    public void Reset() { Initialized = false; Last = false; Applied = 0; }
}
