namespace SprocketAIOrders;

// No camera state here: map coordinates are metres, with north at the top.
public sealed class MapSpace
{
    public float MinX { get; private set; }
    public float MinZ { get; private set; }
    public float SizeX { get; private set; } = 1;
    public float SizeZ { get; private set; } = 1;
    public void SetWorld(float minX, float minZ, float sizeX, float sizeZ)
    { MinX = minX; MinZ = minZ; SizeX = Math.Max(1, sizeX); SizeZ = Math.Max(1, sizeZ); }
    public (float X, float Y) ToMap(float x, float z) => (x - MinX, MinZ + SizeZ - z);
    public (float X, float Z) ToWorld(float x, float y) => (MinX + x, MinZ + SizeZ - y);
}

public enum MapClickAction { None, Select, Toggle, Clear, Move, Waypoint, Attack }

public static class MapPolicy
{
    public static MapClickAction Decide(int button, bool shift, bool allyHit, bool enemyHit) => button switch
    {
        0 when allyHit => shift ? MapClickAction.Toggle : MapClickAction.Select,
        0 => shift ? MapClickAction.None : MapClickAction.Clear,
        // Preserve 0.3.0: a visible enemy takes precedence over queued movement.
        1 when enemyHit => MapClickAction.Attack,
        1 => shift ? MapClickAction.Waypoint : MapClickAction.Move,
        _ => MapClickAction.None
    };

    public static int HitTest(IReadOnlyList<(float X, float Z)> tanks, float x, float z, float radiusPx, float zoom)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z) || !float.IsFinite(zoom) || zoom <= 0 ||
            !float.IsFinite(radiusPx) || radiusPx < 0) return -1;
        var radius = radiusPx / zoom;
        var bestDistance = radius * radius;
        var best = -1;
        for (var i = 0; i < tanks.Count; i++)
        {
            var dx = tanks[i].X - x; var dz = tanks[i].Z - z;
            var distance = dx * dx + dz * dz;
            if (float.IsFinite(distance) && distance <= bestDistance && (best < 0 || distance < bestDistance))
            { best = i; bestDistance = distance; }
        }
        return best;
    }

    // At most about 13 lines per axis, even on ultrawide screens or far outside the topo.
    // Number of dashes for a dashed line of the given length; 0 = draw it solid. Always finite and
    // at most MaxDashes, so a huge or broken length can never stall the draw.
    public const int MaxDashes = 200;
    public static int DashCount(float length, float dashLength)
    {
        if (!float.IsFinite(length) || !float.IsFinite(dashLength) || length <= 0 || dashLength <= 0) return 0;
        var n = Math.Floor((length / dashLength + 1) / 2);
        return n < 2 ? 0 : (int)Math.Min(n, MaxDashes);
    }

    public static float GridStep(float zoom, float widthPx, float heightPx)
    {
        var wanted = Math.Max(100, Math.Max(widthPx, heightPx) / 12) / Math.Max(.0001f, zoom);
        var power = MathF.Pow(10, MathF.Floor(MathF.Log10(wanted)));
        foreach (var multiple in new[] { 1f, 2f, 5f, 10f })
            if (multiple * power >= wanted) return multiple * power;
        return power * 10;
    }
}

// One sample per completed redraw, including identical consecutive timings.
public sealed class MapDrawStats
{
    private double sum;
    public int Count { get; private set; }
    public double Average => Count == 0 ? 0 : sum / Count;
    public double Maximum { get; private set; }
    public int MaxShapes { get; private set; }
    public void Add(double milliseconds, int shapes)
    { sum += milliseconds; Count++; Maximum = Math.Max(Maximum, milliseconds); MaxShapes = Math.Max(MaxShapes, shapes); }
    public void Reset() { sum = Maximum = 0; Count = MaxShapes = 0; }
}
