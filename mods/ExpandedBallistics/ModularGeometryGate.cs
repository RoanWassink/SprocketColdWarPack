namespace SprocketExpandedBallistics;

// Load/build work is valid in Play. Arbitrary transform/visibility callbacks are
// editor work; a live impact overrides both permissions. No spent state enters here.
internal static class ModularGeometryGate
{
    internal static bool CanWrite(bool editor,bool liveImpact,bool loading)
        =>!liveImpact&&(editor||loading);
    internal static bool DeferImpact(bool installed)=>!installed;
    internal static bool DropDeferred(bool editor,bool impactDeferred,bool installed)
        =>!editor&&impactDeferred&&installed;
}
