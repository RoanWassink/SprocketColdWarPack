namespace SprocketExpedition.Game;

// Pure placement and hold policy for the Ambusher role. Native terrain, cover,
// bounds and spacing checks still decide whether a marker is legal; this class
// only decides which legal marker slot receives an ambusher design and when an
// ambusher may leave its position.
internal static class CampaignAmbusherPlacement
{
    internal const float HoldSeconds = 45f;
    internal const float SlowStepMeters = 40f;
    internal const float SlowStepSeconds = 14f;

    // Ambusher slots receive the highest markers in the already validated set.
    // The permutation never changes the set of positions, so terrain, cover,
    // bounds, upright rotations and minimum spacing remain exactly as planned.
    internal static int[] OrderByHeight(IReadOnlyList<float> heights, IReadOnlyList<bool> ambusher)
    {
        if (heights.Count != ambusher.Count)
            throw new ArgumentException("Heights and ambusher flags must have equal length.");
        var count = heights.Count;
        var order = Enumerable.Range(0, count).ToArray();
        var slots = Enumerable.Range(0, count).Where(index => ambusher[index]).ToArray();
        if (slots.Length == 0 || slots.Length == count) return order;

        var ranked = Enumerable.Range(0, count)
            .OrderByDescending(index => SortableHeight(heights[index]))
            .ThenBy(index => index)
            .ToArray();
        var used = new bool[count];
        var next = 0;
        foreach (var slot in slots)
        {
            order[slot] = ranked[next];
            used[ranked[next]] = true;
            next++;
        }
        var remaining = Enumerable.Range(0, count).Where(index => !used[index]).ToArray();
        next = 0;
        for (var slot = 0; slot < count; slot++)
            if (!ambusher[slot]) order[slot] = remaining[next++];
        return order;
    }

    internal static bool MayAdvanceAfterNoSight(float now, float lastSightAt) =>
        float.IsFinite(now) && float.IsFinite(lastSightAt)
        && now >= lastSightAt && now - lastSightAt >= HoldSeconds;

    // A visible target always keeps an ambusher in place; without one it waits
    // 45 seconds from the last sighting (or from spawn when nothing was seen).
    internal static bool HoldsPosition(bool ambusher, bool targetVisible, float now, float lastSightAt) =>
        ambusher && (targetVisible || !MayAdvanceAfterNoSight(now, lastSightAt));

    private static float SortableHeight(float value) => float.IsFinite(value) ? value : float.NegativeInfinity;
}
