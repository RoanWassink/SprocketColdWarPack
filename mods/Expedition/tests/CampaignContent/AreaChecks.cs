using System.Numerics;
using SprocketExpedition.Game;

internal static class AreaChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        // Synthetic map dimensions, not an assertion about any shipped map.
        var map = new CampaignAreaFootprint(new Vector2(512, 128), new Vector2(1024, 768));
        Check(map.Contains(new Vector2(512, 128), 32), "map center is usable");
        Check(map.Contains(new Vector2(992, 128), 32)
            && !map.Contains(new Vector2(993, 128), 32), "enemy hull clearance must be retained at map edges");
        Check(!map.Contains(new Vector2(1208, 128), 0),
            "terrain beyond the playable bounds must not validate an enemy spawn");
        Check(map.Contains(new Vector2(32, -224), 32)
            && !map.Contains(new Vector2(32, -225), 32), "offset and negative map coordinates keep correct edges");
        Check(!map.Contains(new Vector2(float.NaN, 0), 0)
            && !map.Contains(Vector2.Zero, float.NaN)
            && !map.Contains(Vector2.Zero, -1), "invalid native values cannot validate a position");
        Check(!default(CampaignAreaFootprint).Contains(Vector2.Zero, 0)
            && !new CampaignAreaFootprint(Vector2.Zero, new Vector2(30, 30)).Contains(Vector2.Zero, 16),
            "unknown or too-small bounds reject new waypoints");
        Check(map.Contains(new Vector2(1000, 128), 16)
            && !map.Contains(new Vector2(1009, 128), 16), "AI waypoint safety margin is independent of spawn clearance");
        Console.WriteLine("PASS playable bounds: offset maps, enemy edge clearance, AI margin and unknown/non-finite rejection");
    }
}
