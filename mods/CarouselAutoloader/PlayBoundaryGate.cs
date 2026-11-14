namespace SprocketCarouselAutoloader;
// The native editor entry owns the boundary; native BehaviourEnabled distinguishes
// an already active test from a fresh one. Reentrant initialization cannot reset twice.
internal sealed class PlayBoundaryGate
{
    private bool entering;
    internal bool Begin(bool nativeBehaviourEnabled)
    { if(nativeBehaviourEnabled || entering) return false; entering=true; return true; }
    internal void End() => entering=false;
}
