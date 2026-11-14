namespace SprocketTelescopicMast.Io;

public readonly record struct FlowRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    /// <summary>Open intervals: touching edges do not overlap.</summary>
    public bool Overlaps(FlowRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    public FlowRect Inflate(double m) => new(X - m, Y - m, W + 2 * m, H + 2 * m);
    public bool Contains(FlowRect o) => o.X >= X && o.Y >= Y && o.Right <= Right && o.Bottom <= Bottom;
}

/// <summary>
/// Block geometry in graph units, mirroring the UI Kit 0.1.11 node layout (GraphMath: head 30,
/// row 24, foot 30; ports at the left/right edge, centred in their row). Pure: no Kit or Unity.
/// </summary>
public static class FlowLayout
{
    public const double Width = 176, Head = 30, Row = 24, Foot = 30, Margin = 24, Step = 216, Grid = 8, Scan = 16;
    /// <summary>Fallback visible canvas (Kit units) only before the graph has a layout size.</summary>
    public const double ViewWidth = 760, ViewHeight = 560;
    /// <summary>A pending wire snaps to the nearest fitting port within this many panel pixels.</summary>
    public const double SnapPixels = 12;
    /// <summary>Port pick radius in panel pixels, passed to the Kit (default 7; 8 helps drags).</summary>
    public const double PortHitPixels = 8;
    public static int Inputs(FlowNode n) => Math.Max(0, FlowValidation.Ports(n));
    public static int Outputs(FlowNode n) => FlowEditor.HasOutput(n) ? 1 : 0;
    public static double Height(FlowNode n) => Head + 6 + Row * Math.Max(1, Math.Max(Inputs(n), Outputs(n))) + Foot;
    public static FlowRect Bounds(FlowNode n) => new(FlowPresentation.Coordinate(n, "x"), FlowPresentation.Coordinate(n, "y"), Width, Height(n));
    public static (double X, double Y) Port(FlowNode n, bool output, int index)
        => (FlowPresentation.Coordinate(n, "x") + (output ? Width : 0), FlowPresentation.Coordinate(n, "y") + Head + 6 + Row * (index + .5));
    public static FlowRect View(double panX, double panY, double zoom)
    { if (!(zoom > 0) || !double.IsFinite(zoom)) zoom = 1; return new(-panX / zoom, -panY / zoom, ViewWidth / zoom, ViewHeight / zoom); }
    public static bool Clear(IReadOnlyList<FlowRect> occupied, FlowRect r)
    { var grown = r.Inflate(Margin); foreach (var o in occupied) if (grown.Overlaps(o)) return false; return true; }
    private static double Up(double v) => Math.Ceiling(v / Grid) * Grid;
    /// <summary>
    /// Top-left of a w x h group whose rectangle plus <see cref="Margin"/> is free of every occupied
    /// rectangle. Prefers the visible view (row by row from its top-left); otherwise right of the content.
    /// </summary>
    public static (double X, double Y) Free(IReadOnlyList<FlowRect> occupied, double w, double h, FlowRect view)
    {
        double x0 = Up(view.X + 32), y0 = Up(view.Y + 32);
        for (double y = y0; y + h <= view.Bottom - Grid; y += Scan)
            for (double x = x0; x + w <= view.Right - Grid; x += Scan)
                if (Clear(occupied, new(x, y, w, h))) return (x, y);
        if (occupied.Count == 0) return (x0, y0);
        double right = occupied.Max(r => r.Right), top = occupied.Min(r => r.Y);
        return (Up(right + Margin), Up(top));
    }
    /// <summary>Nearest accepted port centre within <paramref name="radius"/> graph units, or null.</summary>
    public static FlowPort? Nearest(IEnumerable<FlowNode> nodes, double x, double y, double radius, Func<FlowPort, bool> accept)
    {
        FlowPort? best = null; double bestSq = radius * radius;
        foreach (var n in nodes)
        {
            int inputs = Inputs(n);
            for (int i = -Outputs(n); i < inputs; i++)
            {
                var port = i < 0 ? new FlowPort(n.Id, 0, true) : new FlowPort(n.Id, i, false);
                var p = Port(n, port.Output, port.Index);
                double d = (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y);
                if (d <= bestSq && accept(port)) { bestSq = d; best = port; }
            }
        }
        return best;
    }
}
