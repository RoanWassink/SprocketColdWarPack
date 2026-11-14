using SprocketAIOrders;

internal static class MapTests
{
    public static void Run()
    {
        var map = new MapSpace();
        map.SetWorld(-1200, 350, 4000, 2000);
        Require(map.ToMap(-1200, 2350) == (0, 0), "Northwest corner is map origin");
        Require(map.ToMap(2800, 350) == (4000, 2000), "Southeast corner and non-square extent");
        Require(map.ToMap(0, 800).Y < map.ToMap(0, 700).Y, "North is up");
        foreach (var p in new[] { (-1200f, 350f), (2800f, 2350f), (123.5f, 912.25f), (-9000f, 8000f) })
        {
            var m = map.ToMap(p.Item1, p.Item2); var w = map.ToWorld(m.X, m.Y);
            Require(Near(w.X, p.Item1) && Near(w.Z, p.Item2), "World/map round trip including outside extent");
        }
        // Synthetic CanvasPointer.World values: camera/scale are owned by Kit, so these
        // checks cover our north-up boundary for arbitrary pan and zoom, without a second camera.
        foreach (var zoom in new[] { .01f, .2f, 1f, 4f })
        foreach (var pan in new[] { (-400f, 120f), (200f, -800f) })
        {
            var m = map.ToMap(123.5f, 912.25f);
            var local = (X: m.X * zoom + pan.Item1, Y: m.Y * zoom + pan.Item2);
            var w = map.ToWorld((local.X - pan.Item1) / zoom, (local.Y - pan.Item2) / zoom);
            Require(Near(w.X, 123.5f, .02f) && Near(w.Z, 912.25f, .02f), "Canvas world boundary survives arbitrary camera pan/zoom");
        }

        var tanks = new[] { (X: 10f, Z: 10f), (X: 15f, Z: 10f), (X: 30f, Z: 10f) };
        Require(MapPolicy.HitTest(tanks, 14, 10, 14, 1) == 1, "Hit chooses nearest tank, not first in radius");
        Require(MapPolicy.HitTest(tanks, 12.5f, 10, 14, 1) == 0, "Equal-distance hits are deterministic");
        Require(MapPolicy.HitTest(tanks, 44, 10, 14, 1) == 2, "Hit includes exact radius boundary");
        Require(MapPolicy.HitTest(tanks, 44.1f, 10, 14, 1) == -1, "Hit rejects outside radius");
        foreach (var zoom in new[] { .05f, .5f, 1f, 4f })
        {
            var one = new[] { (X: 100f, Z: 200f) };
            Require(MapPolicy.HitTest(one, 100 + 13 / zoom, 200, 14, zoom) == 0, "13px tank hit at zoom " + zoom);
            Require(MapPolicy.HitTest(one, 100 + 15 / zoom, 200, 14, zoom) == -1, "15px tank miss at zoom " + zoom);
        }
        Require(MapPolicy.HitTest(Array.Empty<(float, float)>(), 0, 0, 14, 1) == -1, "Empty tank list misses");
        foreach (var zoom in new[] { 0, -1f, float.NaN, float.PositiveInfinity })
            Require(MapPolicy.HitTest(tanks, 10, 10, 14, zoom) == -1, "Invalid zoom cannot hit");
        Require(MapPolicy.HitTest(tanks, float.NaN, 10, 14, 1) == -1, "Invalid world point misses");
        Require(MapPolicy.HitTest(new[] { (float.NaN, 0f), (10f, 10f) }, 10, 10, 14, 1) == 1, "Invalid tank position is ignored");

        foreach (var button in new[] { -1, 0, 1, 2, 3 })
        foreach (var shift in new[] { false, true })
        foreach (var ally in new[] { false, true })
        foreach (var enemy in new[] { false, true })
        {
            var expected = MapClickAction.None;
            if (button == 0) expected = ally ? (shift ? MapClickAction.Toggle : MapClickAction.Select) : (shift ? MapClickAction.None : MapClickAction.Clear);
            if (button == 1) expected = enemy ? MapClickAction.Attack : (shift ? MapClickAction.Waypoint : MapClickAction.Move);
            Require(MapPolicy.Decide(button, shift, ally, enemy) == expected,
                $"Click decision button={button} shift={shift} ally={ally} enemy={enemy}");
        }
        foreach (var zoom in new[] { .0001f, .05f, .5f, 4f })
        foreach (var size in new[] { (800f, 600f), (3000f, 500f), (500f, 2000f) })
        {
            var step = MapPolicy.GridStep(zoom, size.Item1, size.Item2);
            Require(step * zoom >= 100 && Math.Max(size.Item1, size.Item2) / (step * zoom) <= 12.01f,
                "Grid density bounded for zoom/aspect " + zoom + "/" + size);
        }
        var stats = new MapDrawStats();
        stats.Add(10, 123); stats.Add(10, 120); stats.Add(40, 140);
        Require(stats.Count == 3 && stats.Average == 20 && stats.Maximum == 40 && stats.MaxShapes == 140,
            "Draw stats count identical samples; average, maximum and peak shape count");
        stats.Reset();
        Require(stats.Count == 0 && stats.Average == 0 && stats.Maximum == 0 && stats.MaxShapes == 0, "Draw interval reset");
    }
    private static bool Near(float a, float b, float epsilon = .01f) => Math.Abs(a - b) < epsilon;
    private static void Require(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
}
