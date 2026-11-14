#if CAMPAIGN
using Sprocket;
using Sprocket.CustomBattles;
using Sprocket.Gameplay;
using UnityEngine;
using Numerics = System.Numerics;
using UObject = UnityEngine.Object;

namespace SprocketExpedition.Game;

// Read-only scene bounds. Never expands the map or substitutes terrain extents
// for its authored combat area. Unknown bounds cannot validate a new position.
internal static class CampaignBattleArea
{
    private static WorldBounds? bounds;
    private static CampaignAreaFootprint footprint;
    internal static string Status { get; private set; } = "map bounds unavailable";
    internal static bool Ready
    {
        get
        {
            try { return bounds != null && !bounds.WasCollected && bounds.gameObject.scene.isLoaded && footprint.Valid; }
            catch { return false; }
        }
    }

    internal static bool Capture(DeathmatchGameMode mode, Vector3[] trustedNativeAnchors)
    {
        Release();
        if (trustedNativeAnchors.Length == 0) return false;
        var smallest = float.PositiveInfinity;
        foreach (var candidate in UObject.FindObjectsOfType<WorldBounds>())
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy
                || candidate.gameObject.scene.name != mode.gameObject.scene.name) continue;
            var center = candidate.centre;
            var size = candidate.size;
            var area = new CampaignAreaFootprint(new Numerics.Vector2(center.x, center.z),
                new Numerics.Vector2(size.x, size.z));
            if (!area.Valid || !float.IsFinite(size.x * size.z) || size.x * size.z >= smallest) continue;
            if (!trustedNativeAnchors.All(anchor =>
                {
                    var local = candidate.transform.InverseTransformPoint(anchor);
                    return area.Contains(new Numerics.Vector2(local.x, local.z), 0);
                })) continue;
            bounds = candidate;
            footprint = area;
            smallest = size.x * size.z;
        }
        if (!Ready)
        {
            Plugin.Logger.LogWarning($"Expedition {mode.gameObject.scene.name}: no native WorldBounds containing the authored friendly markers; new positions cannot be validated.");
            return false;
        }
        Status = "native map bounds checked";
        Plugin.Logger.LogInfo($"Expedition {mode.gameObject.scene.name} native WorldBounds: local center={footprint.Center}; size={footprint.Size}; transform={bounds!.transform.position}; enemy spawn margin=32 m; AI waypoint margin=16 m.");
        return true;
    }

    internal static bool Contains(Vector3 worldPoint, float margin = 16f)
    {
        if (!Ready || !float.IsFinite(worldPoint.x) || !float.IsFinite(worldPoint.y)
            || !float.IsFinite(worldPoint.z) || !float.IsFinite(margin) || margin < 0) return false;
        try
        {
            // Convert a world-space safety margin to a conservative local-space
            // margin even on scaled scene bounds. Rotations use the native transform.
            var scale = bounds!.transform.lossyScale;
            var minimumScale = Math.Min(Math.Abs(scale.x), Math.Abs(scale.z));
            if (!float.IsFinite(minimumScale) || minimumScale < .001f) return false;
            var local = bounds.transform.InverseTransformPoint(worldPoint);
            return footprint.Contains(new Numerics.Vector2(local.x, local.z), margin / minimumScale);
        }
        catch { return false; }
    }

    internal static CampaignSpawnRange FitSpawnRange(CampaignSpawnRange requested)
    {
        if (!Ready) return requested;
        var scale = bounds!.transform.lossyScale;
        return requested.FitToMap(footprint.Size.X * Math.Abs(scale.x), footprint.Size.Y * Math.Abs(scale.z));
    }

    internal static void Release()
    {
        bounds = null;
        footprint = default;
        Status = "map bounds unavailable";
    }
}
#endif
