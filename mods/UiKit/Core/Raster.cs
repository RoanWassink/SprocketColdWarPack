namespace SprocketUiKit;

/// <summary>2D affine transform: x' = A·x + C·y + E, y' = B·x + D·y + F (y down).</summary>
internal readonly struct Affine
{
    internal readonly float A, B, C, D, E, F;
    internal Affine(float a, float b, float c, float d, float e, float f) { A = a; B = b; C = c; D = d; E = e; F = f; }
    internal static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);
    internal static Affine Translate(float x, float y) => new(1, 0, 0, 1, x, y);
    internal static Affine Scale(float sx, float sy) => new(sx, 0, 0, sy, 0, 0);
    internal static Affine Rotate(float degrees)
    {
        float r = degrees * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
        return new(c, s, -s, c, 0, 0);
    }
    /// <summary>Apply <paramref name="inner"/> first, then this.</summary>
    internal Affine Times(Affine inner) => new(
        A * inner.A + C * inner.B, B * inner.A + D * inner.B,
        A * inner.C + C * inner.D, B * inner.C + D * inner.D,
        A * inner.E + C * inner.F + E, B * inner.E + D * inner.F + F);
    internal (float X, float Y) Apply(float x, float y) => (A * x + C * y + E, B * x + D * y + F);
    internal (float X, float Y) ApplyVector(float x, float y) => (A * x + C * y, B * x + D * y);
    internal float Det => A * D - B * C;
    /// <summary>Uniform scale estimate (sqrt |det|): converts local lengths to device px.</summary>
    internal float ScaleFactor => MathF.Sqrt(MathF.Abs(Det));
    internal bool IsAxisAligned => MathF.Abs(B) < 1e-6f && MathF.Abs(C) < 1e-6f;
    internal Affine Inverse()
    {
        float det = Det;
        if (MathF.Abs(det) < 1e-12f) return Identity;
        float id = 1 / det;
        float a = D * id, b = -B * id, c = -C * id, d = A * id;
        return new(a, b, c, d, -(a * E + c * F), -(b * E + d * F));
    }
}

/// <summary>
/// Pan/zoom camera for a canvas: local = world × Zoom + Offset (local = canvas units, y down).
/// Zooming at a point keeps the world point under it fixed.
/// </summary>
internal sealed class CanvasCamera
{
    internal float Zoom = 1, OffsetX, OffsetY, MinZoom = .1f, MaxZoom = 20;
    internal Affine Transform => new(Zoom, 0, 0, Zoom, OffsetX, OffsetY);
    internal (float X, float Y) ToWorld(float lx, float ly) => ((lx - OffsetX) / Zoom, (ly - OffsetY) / Zoom);
    internal (float X, float Y) ToLocal(float wx, float wy) => (wx * Zoom + OffsetX, wy * Zoom + OffsetY);
    internal void PanBy(float dx, float dy) { OffsetX += dx; OffsetY += dy; }
    internal void ZoomAt(float lx, float ly, float factor)
    {
        if (!(factor > 0) || float.IsInfinity(factor)) return;
        var (wx, wy) = ToWorld(lx, ly);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        OffsetX = lx - wx * Zoom; OffsetY = ly - wy * Zoom;
    }
    /// <summary>Show the world rectangle centred in a canvas of w×h (fit, aspect kept).</summary>
    internal void Fit(float x0, float y0, float x1, float y1, float w, float h)
    {
        float ww = MathF.Max(1e-6f, x1 - x0), wh = MathF.Max(1e-6f, y1 - y0);
        Zoom = Math.Clamp(MathF.Min(w / ww, h / wh), MinZoom, MaxZoom);
        OffsetX = (w - ww * Zoom) / 2 - x0 * Zoom; OffsetY = (h - wh * Zoom) / 2 - y0 * Zoom;
    }
}

/// <summary>Pure geometry helpers for canvas hit testing.</summary>
internal static class Geometry
{
    /// <summary>Even-odd point in polygon; <paramref name="xy"/> = x0,y0,x1,y1,...</summary>
    internal static bool PointInPolygon(ReadOnlySpan<float> xy, float x, float y)
    {
        int n = xy.Length / 2; bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = xy[2 * i], yi = xy[2 * i + 1], xj = xy[2 * j], yj = xy[2 * j + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }
    internal static float SegmentDistance(float px, float py, float x0, float y0, float x1, float y1) =>
        ShapeRaster.SegmentDistance(px, py, new ShapeRaster.Seg(x0, y0, x1, y1));
}

/// <summary>
/// CPU rasteriser for the kit canvas: straight-alpha RGBA32 pixels (byte order R,G,B,A = uint
/// little endian), row 0 = top (the RawImage flips it). Anti-aliased polygon fill by signed-area
/// accumulation, discs/rings by distance. Pure (no Unity) so it is tested and timed outside the game.
/// Coordinates are device pixels; all scratch buffers are reused, so a redraw allocates nothing.
/// </summary>
internal sealed class Raster
{
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal uint[] Pixels { get; private set; } = Array.Empty<uint>();
    private float[] acc = Array.Empty<float>();
    private int cx0, cy0, cx1, cy1; // clip, half-open
    private float[] tmp = new float[64];

    internal static uint Pack(byte r, byte g, byte b, byte a) => r | (uint)g << 8 | (uint)b << 16 | (uint)a << 24;
    internal static uint Pack(Rgba c) => Pack(c.R, c.G, c.B, c.A);
    internal static Rgba Unpack(uint p) => new((byte)p, (byte)(p >> 8), (byte)(p >> 16), (byte)(p >> 24));

    /// <summary>Set the size; the pixel array only grows (reused across redraws).</summary>
    internal void Resize(int w, int h)
    {
        w = Math.Max(1, w); h = Math.Max(1, h);
        Width = w; Height = h;
        if (Pixels.Length < w * h) Pixels = new uint[w * h];
        ResetClip();
    }
    internal void Clear(uint colour = 0) => Array.Fill(Pixels, colour, 0, Width * Height);
    internal void ResetClip() { cx0 = 0; cy0 = 0; cx1 = Width; cy1 = Height; }
    internal void SetClip(float x0, float y0, float x1, float y1)
    {
        cx0 = Math.Clamp((int)MathF.Floor(MathF.Min(x0, x1)), 0, Width); cx1 = Math.Clamp((int)MathF.Ceiling(MathF.Max(x0, x1)), 0, Width);
        cy0 = Math.Clamp((int)MathF.Floor(MathF.Min(y0, y1)), 0, Height); cy1 = Math.Clamp((int)MathF.Ceiling(MathF.Max(y0, y1)), 0, Height);
    }
    internal (int X0, int Y0, int X1, int Y1) Clip => (cx0, cy0, cx1, cy1);

    /// <summary>Source-over blend of <paramref name="colour"/> at coverage 0..1 (straight alpha).</summary>
    internal void Blend(int index, uint colour, float coverage)
    {
        int sa = (int)((colour >> 24) * coverage + .5f);
        if (sa <= 0) return;
        if (sa >= 255) { Pixels[index] = colour | 0xFF000000u; return; }
        uint d = Pixels[index];
        int da = (int)(d >> 24);
        if (da == 0) { Pixels[index] = (colour & 0x00FFFFFFu) | (uint)sa << 24; return; }
        int dw = da * (255 - sa) / 255, oa = sa + dw;
        int r = ((int)(colour & 255) * sa + (int)(d & 255) * dw) / oa;
        int g = ((int)(colour >> 8 & 255) * sa + (int)(d >> 8 & 255) * dw) / oa;
        int b = ((int)(colour >> 16 & 255) * sa + (int)(d >> 16 & 255) * dw) / oa;
        Pixels[index] = (uint)r | (uint)g << 8 | (uint)b << 16 | (uint)oa << 24;
    }

    // ---------- Polygons ----------
    /// <summary>
    /// Fill a polygon (nonzero-ish winding by accumulated signed area; device px). Optional hatch:
    /// stripes at <paramref name="hatchAngle"/> degrees, <paramref name="hatchSpacing"/> apart,
    /// <paramref name="hatchWidth"/> wide, masking the fill.
    /// </summary>
    internal void FillPolygon(ReadOnlySpan<float> xy, uint colour, float hatchAngle = float.NaN, float hatchSpacing = 0, float hatchWidth = 1)
    {
        int n = xy.Length / 2;
        if (n < 3 || (colour >> 24) == 0) return;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            float x = xy[2 * i], y = xy[2 * i + 1];
            if (!float.IsFinite(x) || !float.IsFinite(y)) return;
            if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
        }
        int bx0 = Math.Max(cx0, (int)MathF.Floor(minX)), by0 = Math.Max(cy0, (int)MathF.Floor(minY));
        int bx1 = Math.Min(cx1, (int)MathF.Ceiling(maxX) + 1), by1 = Math.Min(cy1, (int)MathF.Ceiling(maxY) + 1);
        int bw = bx1 - bx0, bh = by1 - by0;
        if (bw <= 0 || bh <= 0) return;
        int stride = bw + 2, size = stride * bh;
        if (acc.Length < size) acc = new float[Math.Max(size, acc.Length * 2)];
        for (int i = 0, j = n - 1; i < n; j = i++)
            Edge(xy[2 * j] - bx0, xy[2 * j + 1] - by0, xy[2 * i] - bx0, xy[2 * i + 1] - by0, bw, bh, stride);
        bool hatch = !float.IsNaN(hatchAngle) && hatchSpacing > 0;
        float hc = 0, hs = 0;
        if (hatch) { float r = hatchAngle * MathF.PI / 180f; hc = -MathF.Sin(r); hs = MathF.Cos(r); }
        for (int y = 0; y < bh; y++)
        {
            float sum = 0; int row = y * stride, pix = (by0 + y) * Width + bx0;
            for (int x = 0; x < bw; x++)
            {
                sum += acc[row + x];
                float cov = MathF.Min(1, MathF.Abs(sum));
                if (cov > .002f)
                {
                    if (hatch)
                    {
                        float t = ((bx0 + x + .5f) * hc + (by0 + y + .5f) * hs) / hatchSpacing;
                        float dist = MathF.Abs(t - MathF.Round(t)) * hatchSpacing;
                        cov *= Math.Clamp(hatchWidth / 2 - dist + .5f, 0, 1);
                    }
                    Blend(pix + x, colour, cov);
                }
            }
            Array.Clear(acc, row, stride);
        }
    }

    /// <summary>Accumulate one edge (bbox-local px), clipping x to [0, w] exactly by splitting.</summary>
    private void Edge(float x0, float y0, float x1, float y1, int w, int h, int stride)
    {
        if (MathF.Abs(y0 - y1) < 1e-7f) return;
        // split at x = 0 and x = w so every part lies inside or on one side, then clamp x
        Span<float> ts = stackalloc float[4]; int k = 0;
        ts[k++] = 0;
        float dx = x1 - x0;
        if (MathF.Abs(dx) > 1e-9f)
        {
            float t0 = -x0 / dx, tw = (w - x0) / dx;
            if (t0 > 0 && t0 < 1) ts[k++] = t0;
            if (tw > 0 && tw < 1) ts[k++] = tw;
        }
        if (k == 3 && ts[1] > ts[2]) (ts[1], ts[2]) = (ts[2], ts[1]);
        ts[k++] = 1;
        float dy = y1 - y0;
        for (int i = 0; i + 1 < k; i++)
        {
            float ax = Math.Clamp(x0 + dx * ts[i], 0, w), ay = y0 + dy * ts[i];
            float bx = Math.Clamp(x0 + dx * ts[i + 1], 0, w), by = y0 + dy * ts[i + 1];
            Line(ax, ay, bx, by, h, stride);
        }
    }

    /// <summary>Signed-area accumulation of one line (font-rs algorithm), x already in [0, w].</summary>
    private void Line(float px0, float py0, float px1, float py1, int h, int stride)
    {
        if (MathF.Abs(py0 - py1) < 1e-7f) return;
        float dir; float ax, ay, bx, by;
        if (py0 < py1) { dir = 1; ax = px0; ay = py0; bx = px1; by = py1; }
        else { dir = -1; ax = px1; ay = py1; bx = px0; by = py0; }
        if (by <= 0 || ay >= h) return;
        float dxdy = (bx - ax) / (by - ay);
        float x = ax;
        if (ay < 0) { x -= ay * dxdy; }
        int ys = Math.Max(0, (int)MathF.Floor(ay)), ye = Math.Min(h, (int)MathF.Ceiling(by));
        for (int y = ys; y < ye; y++)
        {
            int line = y * stride;
            float dy = MathF.Min(y + 1, by) - MathF.Max(y, ay);
            if (dy <= 0) continue;
            float xnext = x + dxdy * dy;
            float d = dy * dir;
            float x0 = MathF.Min(x, xnext), x1 = MathF.Max(x, xnext);
            float x0floor = MathF.Floor(x0); int x0i = (int)x0floor;
            float x1ceil = MathF.Ceiling(x1); int x1i = (int)x1ceil;
            if (x1i <= x0i + 1)
            {
                float xmf = .5f * (x + xnext) - x0floor;
                acc[line + x0i] += d - d * xmf;
                acc[line + x0i + 1] += d * xmf;
            }
            else
            {
                float s = 1 / (x1 - x0);
                float x0f = x0 - x0floor;
                float a0 = .5f * s * (1 - x0f) * (1 - x0f);
                float x1f = x1 - x1ceil + 1;
                float am = .5f * s * x1f * x1f;
                acc[line + x0i] += d * a0;
                if (x1i == x0i + 2) acc[line + x0i + 1] += d * (1 - a0 - am);
                else
                {
                    float a1 = s * (1.5f - x0f);
                    acc[line + x0i + 1] += d * (a1 - a0);
                    for (int xi = x0i + 2; xi < x1i - 1; xi++) acc[line + xi] += d * s;
                    float a2 = a1 + (x1i - x0i - 3) * s;
                    acc[line + x1i - 1] += d * (1 - a2 - am);
                }
                acc[line + x1i] += d * am;
            }
            x = xnext;
        }
    }

    internal void FillRect(float x, float y, float w, float h, uint colour)
    {
        Span<float> q = stackalloc float[] { x, y, x + w, y, x + w, y + h, x, y + h };
        FillPolygon(q, colour);
    }

    // ---------- Discs, rings ----------
    internal void FillCircle(float cx, float cy, float r, uint colour) => Disc(cx, cy, r, -1, colour);
    /// <summary>Ring centred on radius <paramref name="r"/> with stroke <paramref name="width"/>.</summary>
    internal void Ring(float cx, float cy, float r, float width, uint colour) => Disc(cx, cy, r, MathF.Max(.01f, width), colour);
    private void Disc(float cx, float cy, float r, float ring, uint colour)
    {
        if (!(r > 0) || (colour >> 24) == 0 || !float.IsFinite(cx) || !float.IsFinite(cy)) return;
        float alphaScale = 1;
        if (ring > 0 && ring < 1) { alphaScale = ring; ring = 1; }
        float outer = r + (ring > 0 ? ring / 2 : 0) + 1;
        int x0 = Math.Max(cx0, (int)MathF.Floor(cx - outer)), x1 = Math.Min(cx1, (int)MathF.Ceiling(cx + outer));
        int y0 = Math.Max(cy0, (int)MathF.Floor(cy - outer)), y1 = Math.Min(cy1, (int)MathF.Ceiling(cy + outer));
        for (int y = y0; y < y1; y++)
        {
            float dy = y + .5f - cy; int row = y * Width;
            for (int x = x0; x < x1; x++)
            {
                float dx = x + .5f - cx, dist = MathF.Sqrt(dx * dx + dy * dy);
                float cov = ring > 0 ? Math.Clamp(ring / 2 + .5f - MathF.Abs(dist - r), 0, 1) : Math.Clamp(r + .5f - dist, 0, 1);
                if (cov > .002f) Blend(row + x, colour, cov * alphaScale);
            }
        }
    }

    // ---------- Lines ----------
    /// <summary>Straight segment of <paramref name="width"/> px (butt caps); widths below 1 fade instead of thinning.</summary>
    internal void Segment(float x0, float y0, float x1, float y1, float width, uint colour)
    {
        float dx = x1 - x0, dy = y1 - y0, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f || (colour >> 24) == 0) return;
        if (width < 1) { colour = (colour & 0x00FFFFFFu) | (uint)((colour >> 24) * Math.Clamp(width, 0, 1)) << 24; width = 1; }
        // Per-row distance raster: only the pixels along the line are visited (a polygon fill would
        // sweep the whole bounding box of a long diagonal line).
        float ux = dx / len, uy = dy / len, nx = -uy, ny = ux, h = width / 2, reach = h + 1;
        int ry0 = Math.Max(cy0, (int)MathF.Floor(MathF.Min(y0, y1) - reach)), ry1 = Math.Min(cy1, (int)MathF.Ceiling(MathF.Max(y0, y1) + reach));
        int bx0 = Math.Max(cx0, (int)MathF.Floor(MathF.Min(x0, x1) - reach)), bx1 = Math.Min(cx1, (int)MathF.Ceiling(MathF.Max(x0, x1) + reach));
        if (bx0 >= bx1) return;
        for (int y = ry0; y < ry1; y++)
        {
            float yc = y + .5f - y0;
            float lo = bx0, hi = bx1;
            // |d(x)| <= reach, d(x) = (x - x0)·nx + yc·ny
            if (MathF.Abs(nx) > 1e-4f)
            {
                float a = (-reach - yc * ny) / nx + x0, b = (reach - yc * ny) / nx + x0;
                lo = MathF.Max(lo, MathF.Min(a, b) - 1); hi = MathF.Min(hi, MathF.Max(a, b) + 1);
            }
            else if (MathF.Abs(yc * ny) > reach) continue;
            // -1 <= t(x) <= len + 1, t(x) = (x - x0)·ux + yc·uy
            if (MathF.Abs(ux) > 1e-4f)
            {
                float a = (-1 - yc * uy) / ux + x0, b = (len + 1 - yc * uy) / ux + x0;
                lo = MathF.Max(lo, MathF.Min(a, b) - 1); hi = MathF.Min(hi, MathF.Max(a, b) + 1);
            }
            else { float t = yc * uy; if (t < -1 || t > len + 1) continue; }
            int xs = Math.Max(bx0, (int)MathF.Floor(lo)), xe = Math.Min(bx1, (int)MathF.Ceiling(hi));
            int row = y * Width;
            for (int x = xs; x < xe; x++)
            {
                float px = x + .5f - x0;
                float d = MathF.Abs(px * nx + yc * ny), t = px * ux + yc * uy;
                float across = Math.Clamp(h + .5f - d, 0, 1);
                if (across <= 0) continue;
                float along = Math.Clamp(MathF.Min(t, len - t) + .5f, 0, 1);
                float cov = across * along;
                if (cov > .002f) Blend(row + x, colour, cov);
            }
        }
    }

    /// <summary>
    /// Polyline through <paramref name="xy"/>; dash &gt; 0 = dash and gap of that length (continuing
    /// across vertices). Round joins for opaque lines of 2.5 px and more.
    /// </summary>
    internal void Polyline(ReadOnlySpan<float> xy, float width, uint colour, float dash = 0, bool closed = false)
    {
        int n = xy.Length / 2;
        if (n < 2) return;
        int segs = closed ? n : n - 1;
        // Position inside one dash period, kept in [0, period). 0.1.2 accumulated it without wrapping:
        // once the phase grew large, a tiny step no longer changed it (below half an ulp) and the loop
        // never ended, freezing the game (AI Orders route, dash 10.67 px at 1440p).
        float phase = 0, period = dash * 2;
        const float MinStep = 1e-3f;
        bool joins = dash <= 0 && width >= 2.5f && (colour >> 24) == 255;
        for (int s = 0; s < segs; s++)
        {
            int i = s, j = (s + 1) % n;
            float ax = xy[2 * i], ay = xy[2 * i + 1], bx = xy[2 * j], by = xy[2 * j + 1];
            if (dash <= 0) { Segment(ax, ay, bx, by, width, colour); if (joins && (closed || s > 0)) FillCircle(ax, ay, width / 2, colour); continue; }
            float dx = bx - ax, dy = by - ay, len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) continue;
            float t = 0;
            // Each pass crosses a dash/gap boundary or ends the segment; the cap is a hard backstop.
            int guard = (int)MathF.Min(1_000_000f, len / dash * 2 + 8);
            while (len - t > MinStep && guard-- > 0)
            {
                bool on = phase < dash;
                float boundary = on ? dash : period;
                float toBoundary = boundary - phase;
                if (toBoundary <= MinStep) { phase = boundary >= period ? 0 : boundary; continue; }
                float step = MathF.Min(len - t, toBoundary);
                if (on) Segment(ax + dx * t / len, ay + dy * t / len, ax + dx * (t + step) / len, ay + dy * (t + step) / len, width, colour);
                t += step; phase += step;
                if (phase >= period - MinStep) phase = 0;
            }
        }
    }

    /// <summary>Arrow from (x0,y0) to (x1,y1): shaft plus a filled head of <paramref name="head"/> px.</summary>
    internal void Arrow(float x0, float y0, float x1, float y1, float width, float head, uint colour)
    {
        float dx = x1 - x0, dy = y1 - y0, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) return;
        float ux = dx / len, uy = dy / len;
        head = MathF.Min(head, len);
        Segment(x0, y0, x1 - ux * head * .8f, y1 - uy * head * .8f, width, colour);
        float hw = head * .55f;
        Span<float> tri = stackalloc float[] { x1, y1, x1 - ux * head - uy * hw, y1 - uy * head + ux * hw, x1 - ux * head + uy * hw, y1 - uy * head - ux * hw };
        FillPolygon(tri, colour);
    }

    /// <summary>Scratch buffer for transformed points (reused).</summary>
    internal float[] Scratch(int floats)
    {
        if (tmp.Length < floats) tmp = new float[Math.Max(floats, tmp.Length * 2)];
        return tmp;
    }
}
