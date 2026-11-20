using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using UnityEngine;
using SprocketUiKit;

namespace SprocketAIOrders;

public sealed partial class OrdersHost
{
    private const int MapResolution = 512;
    private bool mapOpen => mapPanel?.IsOpen ?? false;
    // Extent and north-up conversion only; CanvasView owns the camera.
    private readonly MapSpace mapSpace = new();
    private CanvasView? mapCanvas;
    private float[]? heights;
    private int heightRow = -1;
    private Texture2D? mapTexture;
    private BattleArea? battleArea;
    private bool mapFailed;
    private (float X0, float Z0, float X1, float Z1)[] terrainRects = Array.Empty<(float, float, float, float)>();
    private float[] terrainBase = Array.Empty<float>();
    private Terrain[] terrains = Array.Empty<Terrain>();
    private long drawSerial, sampledDrawSerial;
    private int drawShapes, appliedFps;
    private readonly MapDrawStats drawStats = new();
    private float nextDrawLog;

    [HideFromIl2Cpp]
    private void OpenMap() { if (player != null) mapPanel?.Open(); }
    [HideFromIl2Cpp]
    private void CloseMap() => mapPanel?.Close();
    [HideFromIl2Cpp]
    private void MapDigit(int n)
    {
        if (n == 0) { Execute("all"); mapCanvas?.Invalidate(); return; }
        var ally = allies.FirstOrDefault(a => Number(a) == n);
        if (ally == null) return;
        var shift = UnityEngine.InputSystem.Keyboard.current?.shiftKey?.isPressed ?? false;
        if (!shift) selected.Clear();
        if (!selected.Add(ally.Pointer) && shift) selected.Remove(ally.Pointer);
        mapCanvas?.Invalidate();
    }

    // World extent: the battle bounds when known, otherwise all terrain tiles.
    [HideFromIl2Cpp]
    private void StartMapBuild()
    {
        try
        {
            terrains = Terrain.activeTerrains?.ToArray() ?? Array.Empty<Terrain>();
        }
        catch { terrains = Array.Empty<Terrain>(); }
        // Cache tile rectangles: sampling calls them a quarter million times.
        terrainRects = new (float, float, float, float)[terrains.Length]; terrainBase = new float[terrains.Length];
        for (var i = 0; i < terrains.Length; i++)
        {
            try { var p = terrains[i].GetPosition(); var size = terrains[i].terrainData.size; terrainRects[i] = (p.x, p.z, p.x + size.x, p.z + size.z); terrainBase[i] = p.y; }
            catch { terrainRects[i] = (1, 1, -1, -1); }
        }
        float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        if (bounds != null && !bounds.WasCollected)
        {
            var c = bounds.centre; var s = bounds.size / 2;
            foreach (var corner in new[] { new Vector3(-s.x, 0, -s.z), new Vector3(s.x, 0, -s.z), new Vector3(-s.x, 0, s.z), new Vector3(s.x, 0, s.z) })
            {
                var w = bounds.transform.TransformPoint(c + corner);
                minX = Math.Min(minX, w.x); maxX = Math.Max(maxX, w.x); minZ = Math.Min(minZ, w.z); maxZ = Math.Max(maxZ, w.z);
            }
        }
        if (!float.IsFinite(minX) || maxX - minX < 50f)
            foreach (var t in terrains)
            {
                var p = t.GetPosition(); var size = t.terrainData.size;
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x + size.x); minZ = Math.Min(minZ, p.z); maxZ = Math.Max(maxZ, p.z + size.z);
            }
        if (!float.IsFinite(minX))
        {
            var p = player!.Position;
            minX = p.x - 1500; maxX = p.x + 1500; minZ = p.z - 1500; maxZ = p.z + 1500;
        }
        // Square extent keeps texture pixels square.
        var side = Math.Max(maxX - minX, maxZ - minZ);
        var cx = (minX + maxX) / 2; var cz = (minZ + maxZ) / 2;
        mapSpace.SetWorld(cx - side / 2, cz - side / 2, side, side);
        FitMap();
        heights = new float[MapResolution * MapResolution];
        heightRow = 0;
        if (Plugin.Detailed) Plugin.Info($"AI Orders map: building {MapResolution}px topo over {side:F0} m from {terrains.Length} terrain tile(s).");
    }

    [HideFromIl2Cpp]
    private void DiscardMapTexture()
    {
        heights = null; heightRow = -1; FitMap();
        if (mapTexture != null && !mapTexture.WasCollected) UnityEngine.Object.Destroy(mapTexture);
        mapTexture = null;
        mapFailed = false; mapCanvas?.Invalidate();
    }

    // Height sampling is spread over frames; colouring happens once at the end.
    [HideFromIl2Cpp]
    private void BuildMapStep()
    {
        if (heights == null || heightRow < 0 || heightRow >= MapResolution) return;
        try
        {
            var step = mapSpace.SizeX / MapResolution;
            var end = Math.Min(MapResolution, heightRow + 16);
            for (var row = heightRow; row < end; row++)
            {
                var z = mapSpace.MinZ + (row + .5f) * step;
                for (var col = 0; col < MapResolution; col++)
                    heights[row * MapResolution + col] = SampleHeight(mapSpace.MinX + (col + .5f) * step, z);
            }
            heightRow = end;
            if (heightRow >= MapResolution) Colourise(step);
        }
        catch (Exception ex)
        {
            Plugin.Warn("AI Orders map build failed: " + ex.Message);
            heightRow = MapResolution; mapFailed = true; heights = null;
        }
    }

    [HideFromIl2Cpp]
    private float SampleHeight(float x, float z)
    {
        for (var i = 0; i < terrains.Length; i++)
        {
            var r = terrainRects[i];
            if (x < r.X0 || z < r.Z0 || x > r.X1 || z > r.Z1) continue;
            var t = terrains[i];
            if (t == null || t.WasCollected) continue;
            return t.SampleHeight(new Vector3(x, 0, z)) + terrainBase[i];
        }
        return float.NaN;
    }

    [HideFromIl2Cpp]
    private void Colourise(float metersPerPixel)
    {
        var n = MapResolution;
        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        var heights = this.heights!;
        foreach (var h in heights) if (float.IsFinite(h)) { lo = Math.Min(lo, h); hi = Math.Max(hi, h); }
        if (!float.IsFinite(lo)) { lo = 0; hi = 1; }
        var range = Math.Max(1f, hi - lo);
        var minor = range > 150 ? 10f : 5f;
        var pixels = new Color32[n * n];
        for (var row = 0; row < n; row++)
            for (var col = 0; col < n; col++)
            {
                var h = heights[row * n + col];
                if (!float.IsFinite(h))
                {
                    var outside = battleArea is { } missingArea && !missingArea.Contains(mapSpace.MinX + (col + .5f) * metersPerPixel,
                        player?.Position.y ?? 0, mapSpace.MinZ + (row + .5f) * metersPerPixel);
                    pixels[row * n + col] = outside ? new Color32(29, 32, 35, 255) : new Color32(58, 64, 70, 255);
                    continue;
                }
                var dx = (H(heights, n, row, col + 1, h) - H(heights, n, row, col - 1, h)) / (2 * metersPerPixel);
                var dz = (H(heights, n, row + 1, col, h) - H(heights, n, row - 1, col, h)) / (2 * metersPerPixel);
                // Light from the north-west, like a printed relief map.
                var shade = Math.Clamp(.82f + (dx - dz) * .7f * .9f, .55f, 1.12f);
                var t = (h - lo) / range;
                var r0 = Lerp(196, 176, t); var g0 = Lerp(206, 160, t); var b0 = Lerp(160, 118, t);
                var contour = Math.Floor(h / minor) != Math.Floor(H(heights, n, row, col + 1, h) / minor) || Math.Floor(h / minor) != Math.Floor(H(heights, n, row + 1, col, h) / minor);
                var major = Math.Floor(h / (minor * 5)) != Math.Floor(H(heights, n, row, col + 1, h) / (minor * 5)) || Math.Floor(h / (minor * 5)) != Math.Floor(H(heights, n, row + 1, col, h) / (minor * 5));
                var dark = major ? .55f : contour ? .78f : 1f;
                // Snapshot uses the same inverse transform as InsideBounds, with no 16 m inset.
                if (battleArea is { } area && !area.Contains(mapSpace.MinX + (col + .5f) * metersPerPixel,
                    h, mapSpace.MinZ + (row + .5f) * metersPerPixel)) dark *= .5f;
                pixels[row * n + col] = new Color32(Byte(r0 * shade * dark), Byte(g0 * shade * dark), Byte(b0 * shade * dark), 255);
            }
        try
        {
            mapTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            mapTexture.filterMode = FilterMode.Bilinear; mapTexture.wrapMode = TextureWrapMode.Clamp;
            mapTexture.SetPixels32(new Il2CppStructArray<Color32>(pixels));
            mapTexture.Apply(false);
            mapCanvas?.Invalidate();
            if (Plugin.Detailed) Plugin.Info($"AI Orders map ready: heights {lo:F0}-{hi:F0} m, contour {minor} m.");
        }
        catch (Exception ex) { mapFailed = true; Plugin.Warn("AI Orders map texture unavailable, using plain grid: " + ex.Message); }
        heights = null;
    }
    // Neighbour height with edge clamp; a hole falls back to the centre height.
    [HideFromIl2Cpp]
    private static float H(float[] heights, int n, int r, int c, float fallback)
    { r = Math.Clamp(r, 0, n - 1); c = Math.Clamp(c, 0, n - 1); var v = heights[r * n + c]; return float.IsFinite(v) ? v : fallback; }
    [HideFromIl2Cpp]
    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
    [HideFromIl2Cpp]
    private static byte Byte(float v) => (byte)Math.Clamp((int)v, 0, 255);


    [HideFromIl2Cpp]
    private string OrderText(VehicleBehaviour tank)
    {
        if (!owned.TryGetValue(tank.Pointer, out var u))
            return tank.OrderReciever != null && gameOrdered.Contains(tank.OrderReciever.Pointer) ? "game order (Sprocket menu)" : "own AI";
        return u.Order switch
        {
            Kind.Follow => (u.Column ? "follow col" : "follow line") + (u.Native ? "" : "*"),
            Kind.Attack when u.Approaching => "attack: closing in",
            Kind.Move => u.Queue.Count > 0 ? $"move (+{u.Queue.Count})" : "move",
            Kind.Attack => "attack",
            Kind.Halt => "halt",
            _ => u.Order.ToString().ToLowerInvariant()
        };
    }

}
