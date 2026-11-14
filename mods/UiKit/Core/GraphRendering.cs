namespace SprocketUiKit;

// Fixed-size slots keep every segment's indices inside its own quad, including unused/dashed quads.
internal static class GraphMeshLayout
{
    internal const int VerticesPerSlot = 128, IndicesPerSlot = 192;
    internal const int MaxSlots = 65534 / VerticesPerSlot; // 511 slots / 65408 vertices
    internal static int WireSlots(int wires, bool pending, bool dragging = false) => Math.Min(wires, MaxSlots - (pending ? 1 : 0) - (dragging ? 1 : 0));
    internal static int IndexAt(int index)
    {
        int vertex = index / 6 * 4;
        return vertex + ((index % 6) switch { 0 => 0, 1 => 2, 2 => 1, 3 => 2, 4 => 3, _ => 1 });
    }
    internal static bool Rebuild(bool topology, int previousSlots, int slots) => topology || previousSlots != slots;
}

internal static class GraphClip
{
    // Coordinates are canvas x/y minima (y up), never panel-local Kit units.
    internal static RectF Padded(RectF rect, float left, float bottom, float right, float top) =>
        new(rect.X + left, rect.Y + bottom, rect.W - left - right, rect.H - bottom - top);
    internal static RectF Intersect(RectF a, RectF b)
    {
        float x = MathF.Max(a.X, b.X), y = MathF.Max(a.Y, b.Y);
        float right = MathF.Min(a.Right, b.Right), top = MathF.Min(a.Bottom, b.Bottom);
        return right > x && top > y ? new(x, y, right - x, top - y) : RectF.Empty;
    }
}

internal readonly record struct GraphHeader(RectF Head, RectF Chip, RectF Tag, RectF Title)
{
    // Tag is chip-local, while Chip and Title are header-local.
    internal static GraphHeader Layout(float width, float measuredTag)
    {
        float chipWidth = Math.Clamp(MathF.Ceiling(measuredTag) + 12, 36, MathF.Max(36, width * .4f));
        var chip = new RectF(7, 5, chipWidth, 20);
        float titleX = chip.Right + 7;
        return new(new(0, 0, width, 30), chip, new(6, 0, chipWidth - 12, 20), new(titleX, 0, width - titleX - 6, 30));
    }
}
