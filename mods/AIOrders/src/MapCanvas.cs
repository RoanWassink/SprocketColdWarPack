using Il2CppInterop.Runtime.Attributes;
using Sprocket.Vehicles;
using SprocketUiKit;
using UnityEngine;

namespace SprocketAIOrders;

public sealed partial class OrdersHost
{
    [HideFromIl2Cpp]
    private Vector2 MapPoint(Vector3 world)
    { var p = mapSpace.ToMap(world.x, world.z); return new Vector2(p.X, p.Y); }

    [HideFromIl2Cpp]
    private void FitMap() => mapCanvas?.FitWorld(0, 0, mapSpace.SizeX, mapSpace.SizeZ);

    [HideFromIl2Cpp]
    private void CenterMap()
    {
        if (mapCanvas == null || player == null) return;
        var p = mapCanvas.ToLocal(MapPoint(player.Position));
        mapCanvas.Pan += new Vector2((mapCanvas.CanvasWidth - 2) / 2 - p.x, (mapCanvas.CanvasHeight - 2) / 2 - p.y);
        mapCanvas.Invalidate();
    }

    [HideFromIl2Cpp]
    private void MapClick(CanvasPointer e)
    {
        if (Stopped || !mapOpen || player == null || Time.timeScale <= 0f || !Application.isFocused ||
            SprocketKeybinds.Keybinds.IsConfiguring || UiKit.TextInputActive) return;
        var w = mapSpace.ToWorld(e.World.x, e.World.y);
        var ally = e.Button == 0 ? HitTank(allies, w.X, w.Z) : null;
        var enemy = e.Button == 1 ? HitTank(contacts.Values.Where(c => c.Visible).Select(c => c.Tank), w.X, w.Z) : null;
        var action = MapPolicy.Decide(e.Button, e.Shift, ally != null, enemy != null);
        if (Plugin.Detailed) Plugin.Info($"AI Orders map click: button={e.Button}; world=({w.X:F1}, {w.Z:F1}); hit={(ally != null ? Name(ally) : enemy != null ? Name(enemy) : "ground")}; shift={e.Shift}; ctrl={e.Ctrl}; alt={e.Alt}; action={action}.");
        switch (action)
        {
            case MapClickAction.Select: selected.Clear(); selected.Add(ally!.Pointer); break;
            case MapClickAction.Toggle: if (!selected.Add(ally!.Pointer)) selected.Remove(ally.Pointer); break;
            case MapClickAction.Clear: selected.Clear(); break;
            case MapClickAction.Attack: AttackSelected(enemy!); break;
            case MapClickAction.Move:
            case MapClickAction.Waypoint:
                var y = SampleHeight(w.X, w.Z);
                MoveSelected(new Vector3(w.X, float.IsFinite(y) ? y : player.Position.y, w.Z), action == MapClickAction.Waypoint);
                break;
        }
        mapCanvas?.Invalidate();
    }

    [HideFromIl2Cpp]
    private VehicleBehaviour? HitTank(IEnumerable<VehicleBehaviour> source, float x, float z)
    {
        var tanks = source.Where(Usable).ToArray();
        var positions = tanks.Select(t => (t.Position.x, t.Position.z)).ToArray();
        var index = MapPolicy.HitTest(positions, x, z, 14, mapCanvas!.Zoom);
        return index < 0 ? null : tanks[index];
    }

    [HideFromIl2Cpp]
    private void ObserveMapDraw()
    {
        if (mapCanvas == null) return;
        if (appliedFps != Plugin.MapRedrawFps) mapCanvas.Fps(appliedFps = Plugin.MapRedrawFps);
        // LateUpdate runs after the synchronous Kit draw callback and raster/upload return.
        if (sampledDrawSerial != drawSerial)
        {
            sampledDrawSerial = drawSerial;
            if (Plugin.Detailed) drawStats.Add(mapCanvas.LastDrawMs, drawShapes);
        }
        if (!Plugin.Detailed) { drawStats.Reset(); nextDrawLog = Time.unscaledTime + 5; return; }
        if (Time.unscaledTime < nextDrawLog) return;
        nextDrawLog = Time.unscaledTime + 5;
        if (drawStats.Count > 0)
            Plugin.Info(FormattableString.Invariant($"AI Orders map draw: avg={drawStats.Average:F2}ms max={drawStats.Maximum:F2}ms shapes={drawStats.MaxShapes}"));
        drawStats.Reset();
    }

    [HideFromIl2Cpp]
    private bool OnMap(Vector2 p, float margin = 24)
    {
        var local = mapCanvas!.ToLocal(p);
        return local.x >= -margin && local.y >= -margin && local.x <= mapCanvas.CanvasWidth + margin && local.y <= mapCanvas.CanvasHeight + margin;
    }

    [HideFromIl2Cpp]
    private void MapLabel(Painter d, Vector2 p, string label, Color colour, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        // Public Painter.Text supports shadow only. Four 1px offsets supply a true outline.
        var pixel = 1 / mapCanvas!.Zoom;
        var black = new Color(0, 0, 0, .85f * colour.a);
        d.Text(p.x - pixel, p.y, label, black, TextStyle.Small, anchor, shadow: false);
        d.Text(p.x + pixel, p.y, label, black, TextStyle.Small, anchor, shadow: false);
        d.Text(p.x, p.y - pixel, label, black, TextStyle.Small, anchor, shadow: false);
        d.Text(p.x, p.y + pixel, label, black, TextStyle.Small, anchor, shadow: false);
        d.Text(p.x, p.y, label, colour, TextStyle.Small, anchor, shadow: false);
        drawShapes += 5;
    }

    [HideFromIl2Cpp]
    private void MapHeading(Painter d, Vector2 p, Vector3 forward, Color colour)
    {
        forward.y = 0;
        if (forward.sqrMagnitude < .01f) return;
        forward.Normalize();
        var length = 20 / mapCanvas!.Zoom;
        d.Line(p.x, p.y, p.x + forward.x * length, p.y - forward.z * length, colour, 2);
        drawShapes++;
    }

    [HideFromIl2Cpp]
    private void DrawMapCanvas(Painter d)
    {
        drawShapes = 0;
        if (mapTexture != null && !mapTexture.WasCollected)
        {
            // Texture row zero is south. RawImage's normal UVs put it at the bottom.
            d.Image(mapTexture, 0, 0, mapSpace.SizeX, mapSpace.SizeZ);
            drawShapes++;
        }
        DrawCanvasGrid(d);
        if (player != null && !Stopped)
        {
            DrawCanvasBounds(d);
            DrawCanvasRoutes(d);
            DrawCanvasContacts(d);
            foreach (var ally in allies.Where(Usable))
            {
                var p = MapPoint(ally.Position);
                if (!OnMap(p, 80)) continue;
                var chosen = selected.Contains(ally.Pointer);
                MapHeading(d, p, ally.transform.forward, new Color(.6f, .8f, 1));
                d.Dot(p.x, p.y, 7, new Color(.2f, .45f, .9f), Color.black); drawShapes++;
                if (chosen) { d.Circle(p.x, p.y, 11 / mapCanvas!.Zoom, Color.yellow, 2); drawShapes++; }
                MapLabel(d, p, Number(ally).ToString(), Color.white);
                if (chosen || mapCanvas!.Zoom > 1f / 3)
                    MapLabel(d, p + new Vector2(12, -18) / mapCanvas!.Zoom, Name(ally), new Color(.8f, .9f, 1), TextAnchor.MiddleLeft);
            }
            var me = MapPoint(player.Position);
            if (OnMap(me))
            {
                MapHeading(d, me, player.transform.forward, Color.white);
                d.Dot(me.x, me.y, 7, new Color(1, .85f, .2f), Color.black); drawShapes++;
                MapLabel(d, me + new Vector2(0, 18) / mapCanvas!.Zoom, "YOU", Color.yellow);
            }
        }
        if (heightRow >= 0 && heightRow < MapResolution || mapFailed)
        {
            var at = mapCanvas!.ToWorld(new Vector2(20, 40));
            MapLabel(d, at, mapFailed ? "Terrain unavailable; see log. Grid and units remain available." : $"Building map… {heightRow * 100 / MapResolution}%", Color.white, TextAnchor.MiddleLeft);
        }
        drawSerial++;
    }

    [HideFromIl2Cpp]
    private void DrawCanvasGrid(Painter d)
    {
        var step = MapPolicy.GridStep(mapCanvas!.Zoom, d.Size.x, d.Size.y);
        var a = mapCanvas.ToWorld(Vector2.zero); var b = mapCanvas.ToWorld(d.Size);
        var left = Math.Max(0, a.x); var top = Math.Max(0, a.y);
        var right = Math.Min(mapSpace.SizeX, b.x); var bottom = Math.Min(mapSpace.SizeZ, b.y);
        var colour = new Color(0, 0, 0, .22f);
        if (right >= left && bottom >= top)
        {
            for (var x = MathF.Ceiling(left / step) * step; x <= right; x += step)
            { d.Line(x, top, x, bottom, colour); drawShapes++; }
            for (var y = MathF.Ceiling(top / step) * step; y <= bottom; y += step)
            { d.Line(left, y, right, y, colour); drawShapes++; }
        }
        MapLabel(d, mapCanvas.ToWorld(new Vector2(12, 16)), $"N ↑   Grid {step:F0} m", Color.white, TextAnchor.MiddleLeft);
    }

    [HideFromIl2Cpp]
    private void DrawCanvasBounds(Painter d)
    {
        if (bounds == null || bounds.WasCollected) return;
        var c = bounds.centre; var s = bounds.size / 2;
        var corners = new[] { new Vector3(-s.x, 0, -s.z), new Vector3(s.x, 0, -s.z), new Vector3(s.x, 0, s.z), new Vector3(-s.x, 0, s.z) }
            .Select(v => MapPoint(bounds.transform.TransformPoint(c + v))).ToArray();
        d.Polyline(corners, new Color(.75f, .15f, .1f, .9f), 2, closed: true); drawShapes++;
    }

    // Kit 0.1.2 dashes (dashPx > 0) can loop forever on float rounding and froze the game on the
    // first drawn route (0.5.0 in-game test). Dash here with a bounded integer loop of solid lines.
    [HideFromIl2Cpp]
    private int DashedLine(Painter d, Vector2 a, Vector2 b, Color colour, float dashPx)
    {
        var n = MapPolicy.DashCount(Vector2.Distance(a, b), dashPx / Math.Max(mapCanvas!.Zoom, 1e-4f));
        if (n == 0) { d.Line(a.x, a.y, b.x, b.y, colour, 2); return 1; }
        for (var i = 0; i < n; i++)
        {
            var p = Vector2.Lerp(a, b, (2f * i) / (2f * n - 1)); var q = Vector2.Lerp(a, b, (2f * i + 1) / (2f * n - 1));
            d.Line(p.x, p.y, q.x, q.y, colour, 2);
        }
        return n;
    }

    [HideFromIl2Cpp]
    private void DrawCanvasRoutes(Painter d)
    {
        foreach (var unit in owned.Values)
        {
            if (!Usable(unit.Tank)) continue;
            var chosen = selected.Contains(unit.Tank.Pointer);
            var colour = new Color(.3f, .85f, 1, chosen ? .95f : .55f);
            var from = MapPoint(unit.Tank.Position);
            if (unit.Order == Kind.Move)
            {
                var route = new[] { from, MapPoint(unit.Goal) }.Concat(unit.Queue.Select(MapPoint)).ToArray();
                for (var i = 1; i < route.Length; i++) drawShapes += DashedLine(d, route[i - 1], route[i], colour, 8);
                var end = route[^1];
                if (OnMap(end)) { d.Dot(end.x, end.y, 3, colour, Color.black); drawShapes++; }
            }
            else if (unit.Order == Kind.Attack && Usable(unit.Target))
            {
                var end = MapPoint(unit.Target!.Position);
                drawShapes += DashedLine(d, from, end, new Color(1, .3f, .2f, colour.a), 8);
            }
            else if (unit.Order == Kind.Follow && chosen)
            {
                var end = MapPoint(player!.Position);
                drawShapes += DashedLine(d, from, end, new Color(1, .9f, .4f, .5f), 10);
            }
        }
    }

    [HideFromIl2Cpp]
    private void DrawCanvasContacts(Painter d)
    {
        foreach (var contact in contacts.Values)
        {
            var p = MapPoint(contact.Position);
            if (!OnMap(p, 80)) continue;
            if (contact.Visible)
            {
                if (Usable(contact.Tank)) MapHeading(d, p, contact.Tank.transform.forward, new Color(1, .5f, .4f));
                d.Dot(p.x, p.y, 6, new Color(.9f, .15f, .1f), Color.black); drawShapes++;
                MapLabel(d, p + new Vector2(10, -18) / mapCanvas!.Zoom, $"{FlatDistance(player!.Position, contact.Position):F0} m", new Color(1, .7f, .65f), TextAnchor.MiddleLeft);
            }
            else
            {
                var fade = Math.Clamp(1 - (Time.time - contact.Seen) / 90, .25f, .8f);
                d.Dot(p.x, p.y, 5, new Color(.55f, .1f, .08f, fade), new Color(0, 0, 0, fade)); drawShapes++;
                MapLabel(d, p, "?", new Color(1, .8f, .8f, fade));
            }
        }
    }
}
