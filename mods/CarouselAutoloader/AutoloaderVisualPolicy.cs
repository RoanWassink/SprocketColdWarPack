namespace SprocketCarouselAutoloader;

// The Unity adapter and tests share these rules. 0.2.23: static prop, no animation.
public static class AutoloaderVisualPolicy
{
    public const int TriangleBudget = 12000;

    public static int Stock(int capacity, int stored) => Math.Clamp(stored, 0, Math.Max(0, capacity));

    // Cheap pre-check before building; InBudget makes the exact decision.
    public const int MaximumCapacity = 192;
    public static bool Supported(int capacity) => capacity >= 1 && capacity <= MaximumCapacity;

    // Capacity is never changed for the decoration. An over-budget or incomplete model is refused
    // and the native grid stays, rather than showing a wrong round count.
    public static bool InBudget(LoaderModel model) => model.Triangles <= TriangleBudget &&
        model.Meshes.SelectMany(p => p.Materials).Distinct().Count() <= 6;

    // Rounds with Slot < stored are visible; the outlet round (highest slot) disappears first.
    public static bool Visible(MeshPart part, int stored, bool own = true) =>
        part.Role != PartRole.Round || own && part.Slot < stored;

    // Per material, the number of round triangles with slot < s for every s = 0..capacity, when the
    // round triangles are ordered by slot. The adapter draws a prefix of one prebuilt index buffer.
    public static int[] Prefix(IReadOnlyList<MeshPart> roundsBySlot, int material, int capacity)
    {
        var prefix = new int[capacity + 1];
        foreach (var part in roundsBySlot)
        {
            if (part.Slot < 0 || part.Slot >= capacity) continue;
            int count = 0;
            foreach (var m in part.Materials) if (m == material) count++;
            prefix[part.Slot + 1] += count;
        }
        for (int s = 1; s <= capacity; s++) prefix[s] += prefix[s - 1];
        return prefix;
    }
}
