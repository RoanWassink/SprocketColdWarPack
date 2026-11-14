namespace SprocketCarouselAutoloader;

// Pure lifecycle policy. A native ignition spends the current Play supply exactly once.
internal sealed class CarouselDamagePolicy
{
    internal bool Ignited { get; private set; }
    internal bool ShouldApply(int remaining, float maximumHealth, float damage) =>
        !Ignited && remaining > 0 && float.IsFinite(maximumHealth) && maximumHealth > 0 &&
        float.IsFinite(damage) && damage > 0;
    internal bool CommitIgnition(int remaining)
    {
        if (Ignited || remaining <= 0) return false;
        Ignited = true;
        return true;
    }
    internal void ResetForNewPlay() => Ignited = false;
}
