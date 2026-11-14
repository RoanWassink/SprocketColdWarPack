namespace SprocketExpandedBallistics;
internal readonly record struct LauncherBox(string Name,float X,float Y,float Z,float Width,float Height,float Length);
internal static class AtgmLauncherModel
{
    internal const string Guid="07a91e5d-be14-5a5c-9321-574b21aadcf3";
    internal static bool Allows(ShellProfile? profile)=>profile!=null&&ShellBalance.Carrier(profile)=="launcher";
    internal static LauncherBox[] SegmentBoxes(float length,float frontOffset,bool mount)
    {
        if(!float.IsFinite(length)||length<=0||!float.IsFinite(frontOffset))throw new ArgumentOutOfRangeException(nameof(length));
        var boxes=new List<LauncherBox>{
            new("top",0,.112f,frontOffset-length/2,.24f,.016f,length),
            new("bottom",0,-.112f,frontOffset-length/2,.24f,.016f,length),
            new("left",-.112f,0,frontOffset-length/2,.016f,.208f,length),
            new("right",.112f,0,frontOffset-length/2,.016f,.208f,length)};
        if(mount)boxes.Add(new("mount",0,-.146f,frontOffset-length/2,.15f,.052f,Math.Min(.26f,length)));
        return boxes.ToArray();
    }
    // Rendered cradle: a bottom rail with front and rear U-clamps that hold the
    // launch canister. The square tube boxes above stay the collision/selection hull.
    internal static LauncherBox[] CradleBoxes(float length,float frontOffset,bool mount)
    {
        if(!float.IsFinite(length)||length<=0||!float.IsFinite(frontOffset))throw new ArgumentOutOfRangeException(nameof(length));
        var centre=frontOffset-length/2;
        var boxes=new List<LauncherBox>{new("rail",0,-.112f,centre,.12f,.016f,length)};
        if(mount)
        {
            const float clamp=.05f;
            foreach(var z in new[]{frontOffset-.12f,frontOffset-length+.12f})
            {
                if(z>frontOffset||z<frontOffset-length)continue;
                boxes.Add(new("clamp base",0,-.112f,z,.24f,.02f,clamp));
                boxes.Add(new("clamp left",-.112f,-.06f,z,.016f,.10f,clamp));
                boxes.Add(new("clamp right",.112f,-.06f,z,.016f,.10f,clamp));
            }
            boxes.Add(new("mount",0,-.146f,centre,.15f,.052f,Math.Min(.26f,length)));
        }
        return boxes.ToArray();
    }
    // Coordinates relative to the actual native muzzle, pointing along +Z.
    internal static readonly LauncherBox[] Boxes={
        new("top",0,.112f,-.65f,.24f,.016f,1.30f),
        new("bottom",0,-.112f,-.65f,.24f,.016f,1.30f),
        new("left",-.112f,0,-.65f,.016f,.208f,1.30f),
        new("right",.112f,0,-.65f,.016f,.208f,1.30f),
        new("mount",0,-.146f,-.70f,.15f,.052f,.26f)
    };
}
