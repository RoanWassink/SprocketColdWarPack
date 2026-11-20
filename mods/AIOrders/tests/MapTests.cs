using SprocketAIOrders;

internal static class MapTests
{
    public static void Run()
    {
        // A 90-degree rotated rectangle: local sizes 100 x 60, world scales 2 x 3,
        // centred at (20, -10). Inverse rotation maps world +Z to local +X.
        var area = new BattleArea((nint)1, 0, 0, .5f, 5, -1f / 3, 0, 0, 20f / 3, 100, 60, 2, 3);
        Require(area.Contains(20, 0, -10) && Near(area.EdgeDistance(20, 0, -10), -90), "Rotated scaled centre is inside with negative edge distance");
        Require(Near(area.EdgeDistance(20, 0, 90), 0) && area.Contains(20, 0, 90), "Rotated scaled edge has zero distance");
        Require(Near(area.EdgeDistance(20, 0, 100), 10), "Rotated scaled point is ten metres beyond edge");
        Require(Near(area.EdgeDistance(-100, 0, 130), 50), "Rotated scaled corner uses nearest Euclidean edge distance");
        Require(area.Contains(20, 0, 80) && !area.Contains(20, 0, 80, 16) && area.EdgeDistance(20, 0, 80) < 0, "Inset refusal excludes margin from reported distance and darkening");
        Require(area.Contains(20, 100, -10), "Yaw-only bounds ignore terrain height");
        var angle = 37 * MathF.PI / 180; var cos = MathF.Cos(angle); var sin = MathF.Sin(angle);
        var tilted = new BattleArea((nint)3, cos / 2, 0, -sin / 2, (-cos * 20 - sin * 10) / 2,
            sin / 3, 0, cos / 3, (-sin * 20 + cos * 10) / 3, 100, 60, 2, 3);
        var outsideX = 20 + cos * 110; var outsideZ = -10 - sin * 110;
        Require(Near(tilted.EdgeDistance(outsideX, 0, outsideZ), 10) && !tilted.Contains(outsideX, 0, outsideZ), "Arbitrary yaw and nonuniform scale preserve ten metre distance");
        Require(tilted.Contains(20, 0, -10) && tilted.EdgeDistance(20, 0, -10) <= 0, "Arbitrary yaw centre remains inside");
        var mirrored = area with { ScaleX = -2, ScaleZ = -3 };
        Require(Near(mirrored.EdgeDistance(20, 0, 100), 10), "Negative scale keeps distance in metres");
        Require(MapPolicy.Refusal(0, false) == OrderRefusal.NoSelection && MapPolicy.Refusal(0, true) == OrderRefusal.NoSelection, "No selection takes precedence over outside area");
        Require(MapPolicy.Refusal(2, false) == OrderRefusal.Outside && MapPolicy.Refusal(2, true) == OrderRefusal.None, "Selected units require an inside destination");
        Require(MapPolicy.MarkerVisible(11.999f, 10, true) && !MapPolicy.MarkerVisible(12, 10, true), "Refusal expires at exactly two unscaled seconds");
        Require(MapPolicy.MarkerVisible(10.999f, 10, false) && !MapPolicy.MarkerVisible(11, 10, false), "Confirmation expires at exactly one unscaled second");
        Require(!MapPolicy.MarkerVisible(9, 10, true) && !MapPolicy.MarkerVisible(float.NaN, 10, false), "Invalid marker time never displays");
        Require(!MapPolicy.NeedsRebuild(area, area) && !MapPolicy.NeedsRebuild(null, null), "Unchanged area does not rebuild");
        Require(MapPolicy.NeedsRebuild(area, area with { Identity = (nint)2 }), "Different WorldBounds identity rebuilds");
        Require(MapPolicy.NeedsRebuild(area, area with { X3 = 6 }), "Changed centre rebuilds");
        Require(MapPolicy.NeedsRebuild(area, area with { SizeX = 101 }), "Changed size rebuilds");
        Require(MapPolicy.NeedsRebuild(area, area with { ScaleZ = 4 }) && MapPolicy.NeedsRebuild(area, area with { X0 = .1f }), "Changed scale or rotation rebuilds");
        Require(MapPolicy.NeedsRebuild(null, area) && MapPolicy.NeedsRebuild(area, null), "Area availability changes rebuild");
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
