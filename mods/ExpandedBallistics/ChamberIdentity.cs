namespace SprocketExpandedBallistics;

// 0.22.17 point 9: the round in the chamber is the one that was there when the gun became Loaded.
// The load request may already name the next type, so it is only the fallback.
internal static class ChamberIdentity
{
    internal static int Resolve(bool loaded,int captured,int requested)=>loaded&&captured>=0?captured:requested;
    // New captured value for this frame: cleared when the gun is not Loaded, set on the first Loaded frame, otherwise kept.
    internal static int Track(bool loadedAndHeld,bool notLoaded,int captured,int requested)=>
        notLoaded?-1:loadedAndHeld&&captured<0?requested:captured;
}
