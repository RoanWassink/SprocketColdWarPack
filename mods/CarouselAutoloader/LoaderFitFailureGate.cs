namespace SprocketCarouselAutoloader;

// Retain observed ownership across failed native queries. A completed scan may clear it.
internal sealed class LoaderFitFailureGate
{
    private readonly HashSet<long> knownCustomFeeds=new();
    internal void Observe(long cannon,bool managed,bool assigned)
    { if(managed&&assigned) knownCustomFeeds.Add(cannon); else knownCustomFeeds.Remove(cannon); }
    internal bool AllowFallback(long cannon) => !knownCustomFeeds.Contains(cannon);
    internal void Forget(long cannon) => knownCustomFeeds.Remove(cannon);
}
