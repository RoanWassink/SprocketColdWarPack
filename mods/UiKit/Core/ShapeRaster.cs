namespace SprocketUiKit;

/// <summary>
/// Procedural alpha masks for the kit's sprites (rounded 9-slice boxes, rings, circles, glow,
/// shadow and line icons). Row-major, row 0 = bottom (Unity texture order), values 0..1.
/// Pure so it can be tested; the runtime turns masks into white textures tinted by Image.color.
/// </summary>
internal static class ShapeRaster
{
    internal const int Supersample = 4; // texture px per kit unit for crisp corners at high UI scale

    [Flags] internal enum Corners { None = 0, TopLeft = 1, TopRight = 2, BottomRight = 4, BottomLeft = 8, Top = 3, Bottom = 12, Left = 9, Right = 6, All = 15 }

    /// <summary>Texture size and 9-slice border (px) for a box with radius r and ring t (kit units).</summary>
    internal static (int Size, int Border) BoxLayout(float radius, float ring)
    {
        int border = (int)MathF.Ceiling(MathF.Max(radius, ring) * Supersample) + 1;
        return (border * 2 + 4, border);
    }

    /// <summary>Rounded box (ring = 0) or its outline of width <paramref name="ring"/>, radius in kit units.</summary>
    internal static float[] Box(float radius, Corners corners, float ring)
    {
        var (size, _) = BoxLayout(radius, ring);
        float r = radius * Supersample, t = ring * Supersample;
        var a = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // texture row 0 is the bottom: convert to y-down for corner selection
                float px = x + .5f, py = size - (y + .5f);
                float outer = Coverage(RoundedDistance(px, py, 0, 0, size, size, r, corners));
                if (t > 0)
                {
                    float inner = Coverage(RoundedDistance(px, py, t, t, size - t, size - t, MathF.Max(0, r - t), corners));
                    outer = MathF.Max(0, outer - inner);
                }
                a[y * size + x] = outer;
            }
        return a;
    }

    /// <summary>Signed distance to a rounded rectangle (negative inside), y down.</summary>
    internal static float RoundedDistance(float px, float py, float x0, float y0, float x1, float y1, float r, Corners corners)
    {
        bool left = px < x0 + r, right = px > x1 - r, top = py < y0 + r, bottom = py > y1 - r;
        bool round = r > 0 && (left && top && corners.HasFlag(Corners.TopLeft) || right && top && corners.HasFlag(Corners.TopRight)
            || right && bottom && corners.HasFlag(Corners.BottomRight) || left && bottom && corners.HasFlag(Corners.BottomLeft));
        if (round)
        {
            float ccx = left ? x0 + r : x1 - r, ccy = top ? y0 + r : y1 - r;
            return MathF.Sqrt((px - ccx) * (px - ccx) + (py - ccy) * (py - ccy)) - r;
        }
        // square region: distance to the nearest edge (negative inside)
        float dx = MathF.Max(x0 - px, px - x1), dy = MathF.Max(y0 - py, py - y1);
        if (px >= x0 && px <= x1 && py >= y0 && py <= y1) return MathF.Max(dx, dy);
        return MathF.Sqrt(MathF.Max(0, dx) * MathF.Max(0, dx) + MathF.Max(0, dy) * MathF.Max(0, dy));
    }

    internal static float Coverage(float distance) => Math.Clamp(.5f - distance, 0, 1);

    /// <summary>Filled disc (ring = 0) or ring, diameter <paramref name="size"/> px.</summary>
    internal static float[] Circle(int size, float ringPx = 0)
    {
        var a = new float[size * size];
        float c = size / 2f, r = size / 2f - .5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = MathF.Sqrt((x + .5f - c) * (x + .5f - c) + (y + .5f - c) * (y + .5f - c)) - r;
                float v = Coverage(d);
                if (ringPx > 0) v = MathF.Max(0, v - Coverage(d + ringPx));
                a[y * size + x] = v;
            }
        return a;
    }

    /// <summary>Soft radial glow (lamp box-shadow 0 0 8px): 1 at the core edge, 0 at core+blur.</summary>
    internal static float[] Glow(int size, float coreFraction)
    {
        var a = new float[size * size];
        float c = size / 2f, core = c * coreFraction;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = MathF.Sqrt((x + .5f - c) * (x + .5f - c) + (y + .5f - c) * (y + .5f - c));
                float t = Math.Clamp((d - core) / MathF.Max(1, c - core), 0, 1);
                a[y * size + x] = (1 - t) * (1 - t) * .9f;
            }
        return a;
    }

    /// <summary>Drop shadow 9-slice: rounded box of radius r with a smooth falloff over blur (kit units, 1 px/unit).</summary>
    internal static float[] Shadow(float radius, float blur, out int size, out int border)
    {
        border = (int)MathF.Ceiling(radius + blur) + 1;
        size = border * 2 + 4;
        var a = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundedDistance(x + .5f, y + .5f, blur, blur, size - blur, size - blur, radius, Corners.All);
                float t = Math.Clamp((d + blur * .35f) / blur, 0, 1); // starts slightly inside the box
                a[y * size + x] = 1 - (t * t * (3 - 2 * t));
            }
        return a;
    }

    // ---------- Line icons (16-unit design box, strokes from the canvas SVGs) ----------
    internal readonly struct Seg { internal readonly float X0, Y0, X1, Y1; internal Seg(float a, float b, float c, float d) { X0 = a; Y0 = b; X1 = c; Y1 = d; } }

    internal static (Seg[] Segs, float Stroke, (float X, float Y, float R)[] Rings) IconShape(Icon icon) => icon switch
    {
        // 14-unit SVGs placed in the 16 box with +1 offset
        Icon.Close => (new[] { S(3, 3, 13, 13), S(13, 3, 3, 13) }, 1.8f, NoRings),
        Icon.Trash => (new[] { S(3, 5, 13, 5), S(6, 5, 6, 3.5f), S(6, 3.5f, 10, 3.5f), S(10, 3.5f, 10, 5), S(4.5f, 5, 5.2f, 13), S(5.2f, 13, 10.8f, 13), S(10.8f, 13, 11.5f, 5) }, 1.5f, NoRings),
        Icon.Plus => (new[] { S(8, 3, 8, 13), S(3, 8, 13, 8) }, 1.8f, NoRings),
        Icon.Minus => (new[] { S(3, 8, 13, 8) }, 1.8f, NoRings),
        // 12-unit chevrons scaled 16/12
        Icon.ChevronDown => (new[] { S(2.7f, 5.3f, 8, 10.7f), S(8, 10.7f, 13.3f, 5.3f) }, 2.1f, NoRings),
        Icon.ChevronRight => (new[] { S(5.3f, 2.7f, 10.7f, 8), S(10.7f, 8, 5.3f, 13.3f) }, 2.1f, NoRings),
        Icon.ChevronLeft => (new[] { S(10.7f, 2.7f, 5.3f, 8), S(5.3f, 8, 10.7f, 13.3f) }, 2.1f, NoRings),
        Icon.ChevronUp => (new[] { S(2.7f, 10.7f, 8, 5.3f), S(8, 5.3f, 13.3f, 10.7f) }, 2.1f, NoRings),
        Icon.ArrowRight => (new[] { S(1, 8, 15, 8), S(11, 4, 15, 8), S(15, 8, 11, 12) }, 1.5f, NoRings),
        Icon.Check => (new[] { S(3, 8.5f, 6.5f, 12), S(6.5f, 12, 13.5f, 4.5f) }, 1.8f, NoRings),
        Icon.Warning => (new[] { S(8, 2.5f, 14, 13.5f), S(14, 13.5f, 2, 13.5f), S(2, 13.5f, 8, 2.5f), S(8, 7, 8, 10), S(8, 11.6f, 8, 12) }, 1.6f, NoRings),
        Icon.Hinge => (new[] { S(5.5f, 10.5f, 13, 3), S(9, 3, 13, 3), S(13, 3, 13, 7) }, 1.6f, new[] { (4f, 12f, 2f) }),
        Icon.Help => (new[] { S(6, 6, 6.6f, 4.6f), S(6.6f, 4.6f, 8, 4), S(8, 4, 9.4f, 4.6f), S(9.4f, 4.6f, 10, 6), S(10, 6, 9.4f, 7.3f), S(9.4f, 7.3f, 8, 8.2f), S(8, 8.2f, 8, 9.6f), S(8, 11.8f, 8, 12.2f) }, 1.7f, NoRings),
        Icon.Dot => (Array.Empty<Seg>(), 0, new[] { (8f, 8f, 0f) }),
        // 0.1.4 title bar: maximize = one square, restore = two overlapping squares (Windows style), same 1.5 stroke as trash
        Icon.Maximize => (new[] { S(3.5f, 3.5f, 12.5f, 3.5f), S(12.5f, 3.5f, 12.5f, 12.5f), S(12.5f, 12.5f, 3.5f, 12.5f), S(3.5f, 12.5f, 3.5f, 3.5f) }, 1.5f, NoRings),
        Icon.Restore => (new[] { S(3, 6, 10, 6), S(10, 6, 10, 13), S(10, 13, 3, 13), S(3, 13, 3, 6), S(6, 6, 6, 3), S(6, 3, 13, 3), S(13, 3, 13, 10), S(13, 10, 10, 10) }, 1.5f, NoRings),
        // resize grip: three diagonal strokes in the bottom-right corner
        Icon.ResizeGrip => (new[] { S(14, 4, 4, 14), S(14, 8, 8, 14), S(14, 12, 12, 14) }, 1.4f, NoRings),
        _ => (Array.Empty<Seg>(), 0, NoRings)
    };
    private static readonly (float, float, float)[] NoRings = Array.Empty<(float, float, float)>();
    private static Seg S(float a, float b, float c, float d) => new(a, b, c, d);

    /// <summary>Icon alpha mask, <paramref name="size"/> px square (16-unit design box), row 0 = bottom.</summary>
    internal static float[] IconMask(Icon icon, int size)
    {
        var (segs, stroke, rings) = IconShape(icon);
        var a = new float[size * size];
        float k = size / 16f, half = stroke * k / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = (x + .5f) / k, py = 16 - (y + .5f) / k; // design units, y down
                float best = float.MaxValue;
                foreach (var s in segs) best = MathF.Min(best, SegmentDistance(px, py, s));
                float v = Coverage(best * k - half);
                foreach (var (cx, cy, r) in rings)
                {
                    float d = MathF.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    v = MathF.Max(v, r <= 0 ? Coverage((d - 3.5f) * k) : Coverage((MathF.Abs(d - r)) * k - half));
                }
                a[y * size + x] = v;
            }
        return a;
    }

    internal static float SegmentDistance(float px, float py, Seg s)
    {
        float dx = s.X1 - s.X0, dy = s.Y1 - s.Y0, len = dx * dx + dy * dy;
        float t = len <= 0 ? 0 : Math.Clamp(((px - s.X0) * dx + (py - s.Y0) * dy) / len, 0, 1);
        float qx = s.X0 + t * dx - px, qy = s.Y0 + t * dy - py;
        return MathF.Sqrt(qx * qx + qy * qy);
    }
}

/// <summary>
/// Every mod callback runs through Guard: an exception is logged (once per key, then counted)
/// and the panel keeps working. Pure; the plugin sets <see cref="Sink"/>.
/// </summary>
public static class Guard
{
    internal static Action<string>? Sink;
    private static readonly Dictionary<string, int> Seen = new(StringComparer.Ordinal);
    internal static int Failures { get; private set; }

    /// <summary>Run <paramref name="action"/>; on failure log "[UI Kit] where: exception" once per <paramref name="where"/>.</summary>
    public static bool Run(string where, Action? action)
    {
        if (action == null) return true;
        try { action(); return true; }
        catch (Exception ex) { Report(where, ex); return false; }
    }
    /// <summary>Evaluate a getter; on failure log once and return <paramref name="fallback"/>.</summary>
    public static T Get<T>(string where, Func<T>? getter, T fallback)
    {
        if (getter == null) return fallback;
        try { return getter(); }
        catch (Exception ex) { Report(where, ex); return fallback; }
    }
    internal static void Report(string where, Exception ex)
    {
        Failures++;
        Seen.TryGetValue(where, out int n); Seen[where] = n + 1;
        if (n == 0) Sink?.Invoke($"[UI Kit] {where}: {ex.GetType().Name}: {ex.Message} (further failures here are counted, not logged)\n{ex.StackTrace}");
        else if (n == 99) Sink?.Invoke($"[UI Kit] {where}: 100 failures so far.");
    }
    internal static int Count(string where) => Seen.TryGetValue(where, out int n) ? n : 0;
    internal static void ResetForTests() { Seen.Clear(); Failures = 0; }
}
