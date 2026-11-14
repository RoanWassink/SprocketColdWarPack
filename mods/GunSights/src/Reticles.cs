namespace SprocketGunSights;

// Reticle geometry in mils (1/1000 rad), x right, y up, aim point at the origin.
internal abstract record Shape;
internal sealed record Line(float X1, float Y1, float X2, float Y2, float Width) : Shape;
internal sealed record Triangle(float X1, float Y1, float X2, float Y2, float X3, float Y3) : Shape;
internal sealed record Ring(float X, float Y, float Radius, float Width) : Shape;
internal sealed record Disc(float X, float Y, float Radius) : Shape;

internal sealed record ReticleDesign(string Id, float Extent, Shape[] Shapes);

/// <summary>
/// Original reconstructions of the reticle families, by period and nation.
/// Patterns follow the published layout principles (German "Winkel" triangles at
/// 4-mil spacing, Soviet chevron and stadia, US/UK range ladders, Cold War mil
/// crosshair), not any particular drawing.
/// </summary>
internal static class Reticles
{
    private const float Thin = .25f, Medium = .4f, Heavy = .8f;

    internal static ReticleDesign Get(string id) => id switch
    {
        "post" => Post(),
        "winkel" => Winkel(),
        "chevron" => Chevron(),
        "rangefinder" => Rangefinder(),
        "ladder" => Ladder(),
        "cross" => Cross(),
        "atgm" => Atgm(),
        _ => Nato(),
    };

    // WW1: heavy post from below to the aim point with a broken horizontal bar.
    private static ReticleDesign Post() => new("post", 24, new Shape[]
    {
        new Line(0, -24, 0, 0, 1.4f),
        new Line(-20, 0, -3, 0, Heavy), new Line(3, 0, 20, 0, Heavy),
    });

    // German: large aim triangle, smaller side triangles every 4 mils.
    private static ReticleDesign Winkel()
    {
        var shapes = new List<Shape> { new Triangle(0, 0, -2, -4, 2, -4) };
        for (var i = 1; i <= 4; i++)
            foreach (var s in new[] { -1f, 1f })
            {
                var x = s * 4 * i;
                shapes.Add(new Triangle(x, 0, x - 1, -2, x + 1, -2));
            }
        shapes.Add(new Line(0, -6, 0, -14, Medium));
        return new("winkel", 20, shapes.ToArray());
    }

    // Soviet WW2: chevron at the aim point, side ticks every 4 thousandths.
    private static ReticleDesign Chevron()
    {
        var shapes = new List<Shape> { new Line(-2.5f, -2.5f, 0, 0, Medium), new Line(0, 0, 2.5f, -2.5f, Medium) };
        for (var i = 1; i <= 4; i++)
            foreach (var s in new[] { -1f, 1f })
            {
                var x = s * 4 * i;
                shapes.Add(new Line(x - 1, -1, x, 0, Thin));
                shapes.Add(new Line(x, 0, x + 1, -1, Thin));
            }
        shapes.Add(new Line(0, -5, 0, -16, Thin));
        return new("chevron", 20, shapes.ToArray());
    }

    // Soviet Cold War: chevron plus stadiametric rangefinder for a 2.7 m target,
    // marked at 400-2000 m (height in mils = 2700 / range).
    private static ReticleDesign Rangefinder()
    {
        var c = Chevron();
        var shapes = c.Shapes.ToList();
        float x0 = -18, baseY = -14;
        shapes.Add(new Line(x0, baseY, x0 + 14, baseY, Thin));
        var prev = (X: 0f, Y: 0f); var first = true;
        for (var range = 400; range <= 2000; range += 100)
        {
            var x = x0 + (range - 400) / 1600f * 14;
            var y = baseY + 2700f / range;
            if (!first) shapes.Add(new Line(prev.X, prev.Y, x, y, Thin));
            if (range % 400 == 0) shapes.Add(new Line(x, baseY, x, baseY - 1, Thin));
            prev = (x, y); first = false;
        }
        return new("rangefinder", 22, shapes.ToArray());
    }

    // US: vertical range ladder below the aim point, lead ticks every 5 mils.
    private static ReticleDesign Ladder()
    {
        var shapes = new List<Shape>
        {
            new Line(0, -1, 0, -18, Thin),
            new Line(-20, 0, -1.5f, 0, Thin), new Line(1.5f, 0, 20, 0, Thin),
        };
        for (var i = 1; i <= 8; i++)
            shapes.Add(new Line(-(i % 2 == 0 ? 2 : 1), -2 * i, i % 2 == 0 ? 2 : 1, -2 * i, Thin));
        for (var i = 1; i <= 3; i++)
            foreach (var s in new[] { -1f, 1f })
                shapes.Add(new Line(s * 5 * i, -1, s * 5 * i, 1, Thin));
        return new("ladder", 20, shapes.ToArray());
    }

    // British: crosshair with a centre gap, range ticks down, lead ticks sideways.
    private static ReticleDesign Cross()
    {
        var shapes = new List<Shape>
        {
            new Line(0, 1.5f, 0, 16, Thin), new Line(0, -1.5f, 0, -18, Thin),
            new Line(-20, 0, -1.5f, 0, Thin), new Line(1.5f, 0, 20, 0, Thin),
        };
        for (var i = 1; i <= 6; i++) shapes.Add(new Line(-1.5f, -2.5f * i, 1.5f, -2.5f * i, Thin));
        for (var i = 1; i <= 4; i++)
            foreach (var s in new[] { -1f, 1f })
                shapes.Add(new Line(s * 4 * i, -.8f, s * 4 * i, .8f, Thin));
        return new("cross", 20, shapes.ToArray());
    }

    // Cold War NATO: thin mil crosshair, ticks every mil, longer every 5.
    private static ReticleDesign Nato()
    {
        var shapes = new List<Shape>
        {
            new Line(-15, 0, -1, 0, Thin), new Line(1, 0, 15, 0, Thin),
            new Line(0, -15, 0, -1, Thin), new Line(0, 1, 0, 6, Thin),
            new Disc(0, 0, .2f),
        };
        for (var i = 2; i <= 15; i++)
        {
            var h = i % 5 == 0 ? .8f : .35f;
            shapes.Add(new Line(i, -h, i, h, Thin)); shapes.Add(new Line(-i, -h, -i, h, Thin));
            shapes.Add(new Line(-h, -i, h, -i, Thin));
        }
        return new("nato", 16, shapes.ToArray());
    }

    // ATGM: fine cross with an open tracking square and range-independent ticks.
    private static ReticleDesign Atgm()
    {
        const float b = 1.5f;
        var shapes = new List<Shape>
        {
            new Line(-12, 0, -b, 0, Thin), new Line(b, 0, 12, 0, Thin),
            new Line(0, -12, 0, -b, Thin), new Line(0, b, 0, 12, Thin),
            new Line(-b, -b, b, -b, Thin), new Line(b, -b, b, b, Thin),
            new Line(b, b, -b, b, Thin), new Line(-b, b, -b, -b, Thin),
        };
        foreach (var d in new[] { 4f, 8f })
            foreach (var s in new[] { -1f, 1f })
            {
                shapes.Add(new Line(s * d, -.6f, s * d, .6f, Thin));
                shapes.Add(new Line(-.6f, s * d, .6f, s * d, Thin));
            }
        return new("atgm", 14, shapes.ToArray());
    }
}

/// <summary>Anti-aliased coverage raster of a reticle into a square alpha mask.</summary>
internal static class ReticleRaster
{
    /// <summary>
    /// Raster at the on-screen scale (pixels per mil), so the pattern keeps its
    /// true angular size while strokes never get thinner than minimumPixels.
    /// Returns the square size; the mask is size*size bytes.
    /// </summary>
    internal static byte[] Render(ReticleDesign design, float scale, float minimumPixels, out int size)
    {
        size = Math.Clamp((int)MathF.Ceiling(2f * design.Extent * scale), 16, 4096);
        scale = size / (2f * design.Extent);
        var alpha = new byte[size * size];
        var minimum = minimumPixels / scale;
        foreach (var raw in design.Shapes)
        {
            var shape = raw switch
            {
                Line l when l.Width < minimum => l with { Width = minimum },
                Ring r when r.Width < minimum => r with { Width = minimum },
                Disc d when d.Radius < minimum => d with { Radius = minimum },
                _ => raw,
            };
            var (minX, minY, maxX, maxY) = Bounds(shape);
            int x0 = Math.Max(0, (int)MathF.Floor((minX + design.Extent) * scale) - 2);
            int x1 = Math.Min(size - 1, (int)MathF.Ceiling((maxX + design.Extent) * scale) + 2);
            int y0 = Math.Max(0, (int)MathF.Floor((minY + design.Extent) * scale) - 2);
            int y1 = Math.Min(size - 1, (int)MathF.Ceiling((maxY + design.Extent) * scale) + 2);
            for (var py = y0; py <= y1; py++)
                for (var px = x0; px <= x1; px++)
                {
                    // Pixel centre back to mils; row 0 is the bottom (Unity texture order).
                    var mx = (px + .5f) / scale - design.Extent;
                    var my = (py + .5f) / scale - design.Extent;
                    var coverage = Math.Clamp(.5f - Distance(shape, mx, my) * scale, 0f, 1f);
                    var value = (byte)(coverage * 255f);
                    var i = py * size + px;
                    if (value > alpha[i]) alpha[i] = value;
                }
        }
        return alpha;
    }

    // Signed distance (mils) outside the shape; negative inside.
    private static float Distance(Shape shape, float x, float y) => shape switch
    {
        Line l => Segment(x, y, l.X1, l.Y1, l.X2, l.Y2) - l.Width / 2,
        Ring r => MathF.Abs(MathF.Sqrt((x - r.X) * (x - r.X) + (y - r.Y) * (y - r.Y)) - r.Radius) - r.Width / 2,
        Disc d => MathF.Sqrt((x - d.X) * (x - d.X) + (y - d.Y) * (y - d.Y)) - d.Radius,
        Triangle t => TriangleDistance(x, y, t),
        _ => float.MaxValue,
    };

    private static float Segment(float x, float y, float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1, dy = y2 - y1, len = dx * dx + dy * dy;
        var t = len <= 0 ? 0 : Math.Clamp(((x - x1) * dx + (y - y1) * dy) / len, 0, 1);
        float cx = x1 + t * dx - x, cy = y1 + t * dy - y;
        return MathF.Sqrt(cx * cx + cy * cy);
    }

    private static float TriangleDistance(float x, float y, Triangle t)
    {
        var edge = Math.Min(Segment(x, y, t.X1, t.Y1, t.X2, t.Y2),
            Math.Min(Segment(x, y, t.X2, t.Y2, t.X3, t.Y3), Segment(x, y, t.X3, t.Y3, t.X1, t.Y1)));
        static float Side(float px, float py, float ax, float ay, float bx, float by) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);
        var d1 = Side(x, y, t.X1, t.Y1, t.X2, t.Y2);
        var d2 = Side(x, y, t.X2, t.Y2, t.X3, t.Y3);
        var d3 = Side(x, y, t.X3, t.Y3, t.X1, t.Y1);
        var inside = (d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0);
        return inside ? -edge : edge;
    }

    private static (float, float, float, float) Bounds(Shape shape) => shape switch
    {
        Line l => (Math.Min(l.X1, l.X2) - l.Width, Math.Min(l.Y1, l.Y2) - l.Width, Math.Max(l.X1, l.X2) + l.Width, Math.Max(l.Y1, l.Y2) + l.Width),
        Ring r => (r.X - r.Radius - r.Width, r.Y - r.Radius - r.Width, r.X + r.Radius + r.Width, r.Y + r.Radius + r.Width),
        Disc d => (d.X - d.Radius, d.Y - d.Radius, d.X + d.Radius, d.Y + d.Radius),
        Triangle t => (Math.Min(t.X1, Math.Min(t.X2, t.X3)), Math.Min(t.Y1, Math.Min(t.Y2, t.Y3)),
            Math.Max(t.X1, Math.Max(t.X2, t.X3)), Math.Max(t.Y1, Math.Max(t.Y2, t.Y3))),
        _ => (0, 0, 0, 0),
    };
}
