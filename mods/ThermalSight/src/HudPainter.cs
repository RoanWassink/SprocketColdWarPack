namespace SprocketThermalSight;

// 0.5.1: the approved HUD mockups (2026-11-16\sight-huds\index.html) rasterised on the CPU into an
// RGBA32 buffer on the 640x480 design frame, plus a list of text items. Pure managed code without
// Unity types: NightHud uploads the buffer into one Texture2D on a uGUI RawImage and shows the texts
// with TextMeshProUGUI. 0.5.0 drew the same layouts with IMGUI, which this IL2CPP build lacks.
public readonly record struct HudColor(float R, float G, float B, float A);
public enum HudAnchor { Left, Center, Right }
// Position in design units; Y is the text baseline, as in the canvas mockups.
public readonly record struct HudText(string Text, float X, float Y, float Size, HudColor Color, HudAnchor Anchor, bool Bold);

public sealed class HudData
{
    public HudStyle Style;
    public string ProfileId = "", Palette = "whiteHot";
    public float? Heading, Turret, Elevation, Magnification;
    public bool? Narrow, Loaded;
    public double? Range;
    public RangeStatus RangeStatus;
    public int Echoes;
    public bool TargetCentred, IrLamp;
    public float Gain = 1, Cooling;
    public string? Ammo;
    public readonly List<(string Code, int Count, bool Chambered)> Rounds = new();

    // Changes when anything visible changes; the canvas repaints only then (and at most ~20 times a second).
    public int Signature(bool reticle)
    {
        var h = new HashCode();
        h.Add(Style); h.Add(ProfileId); h.Add(Palette); h.Add(reticle);
        h.Add(Q(Heading, 10)); h.Add(Q(Turret, 2)); h.Add(Q(Elevation, 10)); h.Add(Q(Magnification, 10));
        h.Add(Narrow); h.Add(Loaded); h.Add(Range is { } r ? Math.Round(r) : -1); h.Add(RangeStatus); h.Add(Echoes);
        h.Add(TargetCentred); h.Add(IrLamp); h.Add((int)MathF.Round(HudMath.GainFraction(Gain) * 120));
        h.Add(Cooling > 0 ? (int)MathF.Ceiling(Cooling) : 0); h.Add(Ammo);
        foreach (var round in Rounds) h.Add(round);
        return h.ToHashCode();
    }
    private static int Q(float? value, float steps) => value is { } v && float.IsFinite(v) ? (int)MathF.Round(v * steps) : int.MinValue;
}

// RGBA32 pixels, top row first, Scale pixels per design unit.
public sealed class HudRaster
{
    public readonly float Scale;
    public readonly int Width, Height;
    public readonly byte[] Pixels;
    public readonly List<HudText> Texts = new();
    // Round eyepiece styles: the canvas also blacks out the screen outside the design frame.
    public bool Eyepiece;
    public HudRaster(float scale)
    {
        Scale = float.IsFinite(scale) && scale > 0 ? scale : 1;
        Width = Math.Max(1, (int)MathF.Round(HudLayout.DesignWidth * Scale));
        Height = Math.Max(1, (int)MathF.Round(HudLayout.DesignHeight * Scale));
        Pixels = new byte[checked(Width * Height * 4)];
    }
    public byte Alpha(int x, int y) => Pixels[(y * Width + x) * 4 + 3];
}

public static class HudPainter
{
    private const float Cx = 320, Cy = 240;
    private static readonly HudColor Amber = new(.88f, .64f, .23f, 1f), Black = new(0, 0, 0, 1);
    private static HudRaster r = null!;
    private static HudColor ink;
    private static byte[]? eyepiece;
    private static (int W, int H, HudColor Ink) eyepieceKey;

    // Raster resolution: the screen scale of the design frame, kept between 1 and 2 pixels per unit
    // (1280x960 at most) so a repaint stays cheap; the RawImage scales it to the screen.
    public static float RasterScale(float layoutScale) => float.IsFinite(layoutScale) ? Math.Clamp(layoutScale, 1f, 2f) : 1f;

    public static HudColor Ink(HudData d) => d.Palette switch
    {
        "greenHot" => d.Style == HudStyle.ActiveIr ? new HudColor(.47f, 1f, .59f, .9f) : new HudColor(.59f, 1f, .67f, .95f),
        "blackHot" => new HudColor(0f, 0f, 0f, .92f),
        _ => d.Style == HudStyle.Gen3Hd ? new HudColor(.92f, .96f, .98f, .95f) : new HudColor(1f, 1f, 1f, .95f),
    };

    public static void Paint(HudRaster raster, HudData d, bool reticle)
    {
        r = raster; ink = Ink(d);
        Array.Clear(r.Pixels, 0, r.Pixels.Length);
        r.Texts.Clear();
        r.Eyepiece = d.Style is HudStyle.ActiveIr or HudStyle.Intensifier;
        switch (d.Style)
        {
            case HudStyle.ActiveIr: ActiveIr(d, reticle); break;
            case HudStyle.Intensifier: Intensifier(d, reticle); break;
            case HudStyle.Gen1: Gen1(d, reticle); break;
            case HudStyle.Gen1Late: Gen1(d, reticle); Gen1Late(d); break;
            case HudStyle.Gen2: Gen2(d, reticle); break;
            case HudStyle.Gen3: Gen3(d, reticle); break;
            case HudStyle.Gen3Uncooled: Gen3Uncooled(d, reticle); break;
            case HudStyle.Gen3Hd: Gen3Hd(d, reticle); break;
        }
        if (d.Cooling > 0 && d.Style != HudStyle.Gen3 && d.Style != HudStyle.Gen3Hd)
            Text("COOLING " + CooldownTimer.Format(d.Cooling), 40, 70, 12, Amber, HudAnchor.Left, true);
    }

    // ------------------------------------------------------------------ layouts
    private static void ActiveIr(HudData d, bool reticle)
    {
        Eyepiece();
        if (reticle)
        {
            Line(Cx - 150, Cy, Cx - 12, Cy, 1.5f); Line(Cx + 12, Cy, Cx + 150, Cy, 1.5f);
            Line(Cx, Cy - 12, Cx, Cy - 120, 1.5f); Line(Cx, Cy + 12, Cx, Cy + 80, 1.5f);
            for (int i = 1; i <= 4; i++) Line(Cx - 6, Cy + i * 16, Cx + 6, Cy + i * 16, 1.2f);
        }
        // Lamp indicator: filled while the IR searchlight lights the scene, open ring otherwise.
        if (d.IrLamp) Disc(140, 105, 5); else Circle(140, 105, 5, 1.2f);
        Text("IR", 150, 110, 16, ink, HudAnchor.Left, true);
    }

    private static void Intensifier(HudData d, bool reticle)
    {
        Eyepiece();
        if (reticle)
        {
            Line(Cx - 22, Cy + 14, Cx, Cy, 2); Line(Cx, Cy, Cx + 22, Cy + 14, 2);
            int[] ladder = { 4, 6, 8, 10, 12 };
            for (int i = 0; i < ladder.Length; i++)
            {
                float y = Cy + 28 + i * 16;
                Line(Cx - 14 + i * 2, y, Cx + 14 - i * 2, y, 1.2f);
                Text(ladder[i].ToString(), Cx + 22, y + 4, 11, ink, HudAnchor.Left);
            }
        }
        // Automatic gain of the intensifier tube (sensor processing), logarithmic bar.
        Outline(150, 180, 8, 120, 1);
        float h = 120 * HudMath.GainFraction(d.Gain);
        if (h > 0) Fill(150, 300 - h, 8, h, ink);
        Text("GAIN", 138, 320, 10, ink, HudAnchor.Left);
    }

    private static void Gen1(HudData d, bool reticle)
    {
        if (reticle)
        {
            Outline(Cx - 10, Cy - 10, 20, 20, 2);
            Line(Cx - 90, Cy, Cx - 30, Cy, 2); Line(Cx + 30, Cy, Cx + 90, Cy, 2); Line(Cx, Cy + 30, Cx, Cy + 80, 2);
        }
        Text(HudMath.FormatRange4(d.Range), Cx - 48, 445, 30, ink, HudAnchor.Left, true);
        if (d.Narrow is { } n) Text(n ? "N" : "W", 40, 445, 22, ink, HudAnchor.Left, true);
        Text(HudMath.Polarity(d.Palette), 580, 445, 18, ink, HudAnchor.Left, true);
    }

    private static void Gen1Late(HudData d)
    {
        if (d.Ammo != null) Text(Short(d.Ammo, 6), 40, 50, 20, ink, HudAnchor.Left, true);
        if (d.Loaded == true) Text("RDY", 560, 50, 18, ink, HudAnchor.Left, true);
        if (d.Echoes >= 2 && d.Range != null) Text("2", Cx + 60, 430, 14, ink, HudAnchor.Left);
    }

    private static void Gen2(HudData d, bool reticle)
    {
        foreach (var (x, y, sx, sy) in new[] { (60f, 60f, 1, 1), (580f, 60f, -1, 1), (60f, 420f, 1, -1), (580f, 420f, -1, -1) })
        { Line(x, y + sy * 30, x, y, 2); Line(x, y, x + sx * 30, y, 2); }
        if (reticle)
        {
            Line(Cx - 110, Cy, Cx - 8, Cy, 1.6f); Line(Cx + 8, Cy, Cx + 110, Cy, 1.6f); Line(Cx, Cy + 8, Cx, Cy + 70, 1.6f);
            for (int i = 1; i <= 3; i++) { Line(Cx + i * 28, Cy - 5, Cx + i * 28, Cy + 5, 1.4f); Line(Cx - i * 28, Cy - 5, Cx - i * 28, Cy + 5, 1.4f); }
            Disc(Cx, Cy + 22, 2.5f);
        }
        ElevationScale(d, 600, 140, 340, 8, 6, true);
        Text(HudMath.FormatRange(d.Range) + " M", Cx, 455, 20, ink, HudAnchor.Center, true);
        if (d.Ammo != null) Text(Short(d.Ammo, 8), 72, 455, 16, ink, HudAnchor.Left);
        Text((d.Narrow is { } n ? (n ? "NFOV  " : "WFOV  ") : "") + HudMath.Polarity(d.Palette), 568, 455, 14, ink, HudAnchor.Right);
    }

    private static void Gen3(HudData d, bool reticle)
    {
        if (d.Palette != "blackHot") Fill(170, 14, 300, 34, new HudColor(0, 0, 0, .35f));
        Tape(d.Heading, 6, 24, 5, 40, 34, 30, 27, 11, 1.2f);
        Line(Cx - 6, 48, Cx, 42, 1.6f); Line(Cx, 42, Cx + 6, 48, 1.6f);
        if (reticle)
        {
            Line(Cx - 70, Cy, Cx - 14, Cy, 1.6f); Line(Cx + 14, Cy, Cx + 70, Cy, 1.6f);
            Line(Cx, Cy + 14, Cx, Cy + 60, 1.6f); Line(Cx, Cy - 14, Cx, Cy - 40, 1.6f);
            Circle(Cx, Cy, 4, 1.2f);
        }
        // Hull outline fixed, turret and barrel turned by the turret bearing relative to the hull.
        Outline(70 - 14, 400 - 24, 28, 48, 1.6f);
        if (d.Turret is { } t)
        {
            Circle(70, 400, 9, 1.6f);
            var (x1, y1) = Polar(70, 400, 9, t); var (x2, y2) = Polar(70, 400, 34, t);
            Line(x1, y1, x2, y2, 1.6f);
        }
        Text("TUR " + (d.Turret is { } tu ? HudMath.WholeDegrees(tu).ToString("000") + "°" : "----"), 100, 425, 12, ink, HudAnchor.Left);
        Text(HudMath.FormatRange(d.Range), Cx, 446, 26, ink, HudAnchor.Center, true);
        Text("M  " + LrfStatus(d), Cx + 56, 446, 12, d.RangeStatus is RangeStatus.NoReturn or RangeStatus.TooClose ? Amber : ink, HudAnchor.Left);
        Text((d.Ammo != null ? Short(d.Ammo, 8) : "----") + (d.Loaded == true ? " > LOADED" : ""), 600, 396, 13, ink, HudAnchor.Right);
        Text(Zoom(d) + (d.Narrow is { } n ? (n ? " NFOV" : " WFOV") : ""), 600, 418, 13, ink, HudAnchor.Right);
        Text(HudMath.Polarity(d.Palette), 600, 440, 13, ink, HudAnchor.Right);
        if (d.Cooling > 0) Text("COOLING " + CooldownTimer.Format(d.Cooling), 40, 70, 12, Amber, HudAnchor.Left, true);
        ElevationScale(d, 612, 150, 330, 0, 10, false);
    }

    private static void Gen3Uncooled(HudData d, bool reticle)
    {
        Tape(d.Heading, 5, 26, 5, 38, 33, 29, 25, 11, 1.3f);
        if (reticle)
        { Line(Cx - 60, Cy, Cx - 10, Cy, 1.8f); Line(Cx + 10, Cy, Cx + 60, Cy, 1.8f); Line(Cx, Cy + 10, Cx, Cy + 50, 1.8f); }
        Text(HudMath.FormatRange(d.Range) + " M", Cx, 450, 20, ink, HudAnchor.Center, true);
        Text(HudMath.Polarity(d.Palette) + "  " + Zoom(d), 600, 450, 13, ink, HudAnchor.Right);
    }

    private static void Gen3Hd(HudData d, bool reticle)
    {
        Tape(d.Heading, 10, 16, 2, 36, 32, 27, 22, 10, 1f);
        Line(Cx - 5, 44, Cx, 38, 1.4f); Line(Cx, 38, Cx + 5, 44, 1.4f);
        if (reticle)
        {
            Line(Cx - 46, Cy, Cx - 8, Cy, 1); Line(Cx + 8, Cy, Cx + 46, Cy, 1); Line(Cx, Cy + 8, Cx, Cy + 40, 1);
            Fill(Cx - 1, Cy - 1, 2, 2, ink);
        }
        // Visual only: corners around the centre while the last laser return is a body on the current line of sight.
        if (d.TargetCentred)
        {
            const float bw = 40, bh = 24;
            foreach (var (x, y, sx, sy) in new[] { (Cx - bw, Cy - bh, 1, 1), (Cx + bw, Cy - bh, -1, 1), (Cx - bw, Cy + bh, 1, -1), (Cx + bw, Cy + bh, -1, -1) })
            { Line(x, y + sy * 10, x, y, 1.6f, Amber); Line(x, y, x + sx * 10, y, 1.6f, Amber); }
            Text("TRK", Cx + bw + 6, Cy - bh + 8, 10, Amber, HudAnchor.Left, true);
        }
        Text("IR  " + HudMath.Polarity(d.Palette), 28, 70, 11, ink, HudAnchor.Left);
        Text("E-ZOOM " + Zoom(d), 28, 90, 11, ink, HudAnchor.Left);
        if (d.Cooling > 0) Text("COOLING " + CooldownTimer.Format(d.Cooling), 28, 50, 12, Amber, HudAnchor.Left, true);
        // Fixed dark backing and opaque 14-unit text remain legible over both hot and cold imagery.
        int ammoRows = Math.Max(1, d.Rounds.Count);
        float ammoTop = Math.Max(100, 442 - ammoRows * 24);
        Fill(20, ammoTop - 24, 250, 442 - ammoTop + 24, new HudColor(0, 0, 0, .82f));
        var ammoInk = new HudColor(1, 1, 1, 1);
        if (d.Rounds.Count == 0) Text("----", 28, ammoTop, 14, ammoInk, HudAnchor.Left, true);
        for (int i = 0; i < d.Rounds.Count; i++)
        {
            var (code, count, chambered) = d.Rounds[i];
            Text(Short(code, 21), 28, ammoTop + i * 24, 14, ammoInk, HudAnchor.Left, chambered);
            Text(count.ToString(), 260, ammoTop + i * 24, 14, ammoInk, HudAnchor.Right, chambered);
        }
        Text(HudMath.FormatRange(d.Range), Cx, 450, 24, ink, HudAnchor.Center, true);
        Text("m", Cx + 40, 450, 11, ink, HudAnchor.Left);
        if (d.RangeStatus is RangeStatus.NoReturn or RangeStatus.TooClose)
            Text(d.RangeStatus == RangeStatus.NoReturn ? "NO RETURN" : "TOO CLOSE", Cx, 466, 10, Amber, HudAnchor.Center, true);
        Circle(590, 410, 22, 1.2f);
        if (d.Turret is { } t) { var (x, y) = Polar(590, 410, 22, t); Line(590, 410, x, y, 1.2f); }
        Text("EL " + HudMath.FormatSigned(d.Elevation), 612, 450, 11, ink, HudAnchor.Right);
    }

    // ------------------------------------------------------------------ parts
    private static string LrfStatus(HudData d) => d.RangeStatus switch
    {
        RangeStatus.Ok => "LRF OK",
        RangeStatus.NoReturn => "LRF NO RTN",
        RangeStatus.TooClose => "LRF MIN",
        _ => "LRF ----",
    };
    private static string Zoom(HudData d) => d.Magnification is { } m ? "×" + m.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "×----";
    private static string Short(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    private static (float X, float Y) Polar(float x, float y, float radius, float clockwiseDegrees)
    {
        float a = clockwiseDegrees * MathF.PI / 180f;
        return (x + radius * MathF.Sin(a), y - radius * MathF.Cos(a));
    }

    // Heading tape: one tick per step degrees, spacing units apart, labels every 10 degrees.
    private static void Tape(float? heading, int half, float spacing, int step, float baseY, float minorY, float majorY, float labelY, int size, float width)
    {
        if (heading is not { } h) { Text("---", Cx, labelY, size, ink, HudAnchor.Center); return; }
        float first = MathF.Floor(h / step) * step;
        float offset = (h - first) / step * spacing;
        for (int k = -half; k <= half + 1; k++)
        {
            float x = Cx + k * spacing - offset;
            if (x < Cx - half * spacing - .01f || x > Cx + half * spacing + .01f) continue;
            int value = HudMath.WholeDegrees(first + k * step);
            bool major = value % 10 == 0;
            Line(x, baseY, x, major ? majorY : minorY, width);
            if (major) Text(value.ToString("000"), x, labelY, size, ink, HudAnchor.Center);
        }
    }

    // Vertical elevation scale: -10..+20 degrees between bottom and top, pointer at the gun elevation.
    private static void ElevationScale(HudData d, float x, float top, float bottom, int ticks, float tickLength, bool label)
    {
        Line(x, top, x, bottom, label ? 1.4f : 1.2f);
        for (int i = 0; i <= ticks; i++) { float y = top + i * (bottom - top) / Math.Max(1, ticks); Line(x - tickLength, y, x, y, 1.2f); }
        if (d.Elevation is not { } e) return;
        float yy = bottom - Math.Clamp((e + 10f) / 30f, 0, 1) * (bottom - top);
        Line(x - 14 + (label ? 0 : 4), yy - 6, x, yy, 1.6f); Line(x, yy, x - 14 + (label ? 0 : 4), yy + 6, 1.6f);
        if (label) Text(HudMath.FormatSigned(e), x - 60, yy - 10, 12, ink, HudAnchor.Left);
    }

    // Round eyepiece: black outside x = 80..560 and outside a 215-unit circle with a soft edge (from 150),
    // plus a thin ring in the HUD colour. The layer is cached per raster size and colour.
    private static void Eyepiece()
    {
        if (eyepiece == null || eyepieceKey != (r.Width, r.Height, ink))
        {
            var layer = new byte[r.Pixels.Length];
            float s = r.Scale;
            for (int py = 0; py < r.Height; py++)
                for (int px = 0; px < r.Width; px++)
                {
                    float u = (px + .5f) / s, v = (py + .5f) / s;
                    float dist = MathF.Sqrt((u - Cx) * (u - Cx) + (v - Cy) * (v - Cy));
                    float a = MathF.Abs(u - Cx) >= 240 || dist >= 215 ? 1 : dist <= 150 ? 0 : .85f * (dist - 150) / 66f;
                    int k = (py * r.Width + px) * 4;
                    Blend(layer, k, Black, Math.Clamp(a, 0, 1));
                    Blend(layer, k, ink, Math.Clamp(1.2f - Math.Abs(dist - 214.5f) / 1.2f, 0, 1));
                }
            eyepiece = layer; eyepieceKey = (r.Width, r.Height, ink);
        }
        Buffer.BlockCopy(eyepiece, 0, r.Pixels, 0, r.Pixels.Length);
    }

    // ------------------------------------------------------------------ primitives (design units)
    private static void Line(float x1, float y1, float x2, float y2, float w, HudColor? colour = null)
    {
        var c = colour ?? ink;
        float s = r.Scale;
        float ax = x1 * s, ay = y1 * s, bx = x2 * s, by = y2 * s;
        float half = Math.Max(1f, w * s) / 2f;
        // Axis-aligned lines as filled rectangles (crisp, square ends as in 0.5.0).
        if (Math.Abs(ay - by) < .01f) { FillPx(Math.Min(ax, bx), ay - half, Math.Abs(bx - ax), 2 * half, c); return; }
        if (Math.Abs(ax - bx) < .01f) { FillPx(ax - half, Math.Min(ay, by), 2 * half, Math.Abs(by - ay), c); return; }
        int minX = Math.Max(0, (int)MathF.Floor(Math.Min(ax, bx) - half - 1)), maxX = Math.Min(r.Width - 1, (int)MathF.Ceiling(Math.Max(ax, bx) + half + 1));
        int minY = Math.Max(0, (int)MathF.Floor(Math.Min(ay, by) - half - 1)), maxY = Math.Min(r.Height - 1, (int)MathF.Ceiling(Math.Max(ay, by) + half + 1));
        float dx = bx - ax, dy = by - ay, length2 = dx * dx + dy * dy;
        for (int py = minY; py <= maxY; py++)
            for (int px = minX; px <= maxX; px++)
            {
                float qx = px + .5f - ax, qy = py + .5f - ay;
                float t = length2 > 0 ? Math.Clamp((qx * dx + qy * dy) / length2, 0, 1) : 0;
                float ex = qx - t * dx, ey = qy - t * dy;
                float coverage = Math.Clamp(half + .5f - MathF.Sqrt(ex * ex + ey * ey), 0, 1);
                if (coverage > 0) Blend(r.Pixels, (py * r.Width + px) * 4, c, coverage);
            }
    }
    private static void Circle(float x, float y, float radius, float w)
    {
        float s = r.Scale, cx = x * s, cy = y * s, rr = radius * s, half = Math.Max(1f, w * s) / 2f;
        Radial(cx, cy, rr + half + 1, dist => Math.Clamp(half + .5f - Math.Abs(dist - rr), 0, 1));
    }
    private static void Disc(float x, float y, float radius)
    {
        float s = r.Scale, rr = radius * s;
        Radial(x * s, y * s, rr + 1, dist => Math.Clamp(rr + .5f - dist, 0, 1));
    }
    private static void Radial(float cx, float cy, float extent, Func<float, float> coverage)
    {
        int minX = Math.Max(0, (int)MathF.Floor(cx - extent)), maxX = Math.Min(r.Width - 1, (int)MathF.Ceiling(cx + extent));
        int minY = Math.Max(0, (int)MathF.Floor(cy - extent)), maxY = Math.Min(r.Height - 1, (int)MathF.Ceiling(cy + extent));
        for (int py = minY; py <= maxY; py++)
            for (int px = minX; px <= maxX; px++)
            {
                float ex = px + .5f - cx, ey = py + .5f - cy;
                float a = coverage(MathF.Sqrt(ex * ex + ey * ey));
                if (a > 0) Blend(r.Pixels, (py * r.Width + px) * 4, ink, a);
            }
    }
    private static void Outline(float x, float y, float w, float h, float t)
    {
        Fill(x - t / 2, y - t / 2, w + t, t, ink); Fill(x - t / 2, y + h - t / 2, w + t, t, ink);
        Fill(x - t / 2, y + t / 2, t, h - t, ink); Fill(x + w - t / 2, y + t / 2, t, h - t, ink);
    }
    private static void Fill(float x, float y, float w, float h, HudColor c)
    {
        float s = r.Scale;
        FillPx(x * s, y * s, Math.Max(1, w * s), Math.Max(1, h * s), c);
    }
    // Rectangle in raster pixels with fractional edge coverage.
    private static void FillPx(float x, float y, float w, float h, HudColor c)
    {
        float x1 = x + w, y1 = y + h;
        int minX = Math.Max(0, (int)MathF.Floor(x)), maxX = Math.Min(r.Width - 1, (int)MathF.Ceiling(x1) - 1);
        int minY = Math.Max(0, (int)MathF.Floor(y)), maxY = Math.Min(r.Height - 1, (int)MathF.Ceiling(y1) - 1);
        for (int py = minY; py <= maxY; py++)
        {
            float cy = Math.Min(py + 1, y1) - Math.Max(py, y);
            if (cy <= 0) continue;
            for (int px = minX; px <= maxX; px++)
            {
                float cx = Math.Min(px + 1, x1) - Math.Max(px, x);
                if (cx > 0) Blend(r.Pixels, (py * r.Width + px) * 4, c, cx * cy);
            }
        }
    }
    // Straight-alpha "over" compositing.
    internal static void Blend(byte[] pixels, int k, HudColor c, float coverage)
    {
        float sa = c.A * Math.Clamp(coverage, 0, 1);
        if (sa <= 0) return;
        float da = pixels[k + 3] / 255f, oa = sa + da * (1 - sa);
        if (oa <= 0) return;
        float keep = da * (1 - sa) / 255f;
        pixels[k] = (byte)Math.Clamp((int)MathF.Round((c.R * sa + pixels[k] * keep) / oa * 255), 0, 255);
        pixels[k + 1] = (byte)Math.Clamp((int)MathF.Round((c.G * sa + pixels[k + 1] * keep) / oa * 255), 0, 255);
        pixels[k + 2] = (byte)Math.Clamp((int)MathF.Round((c.B * sa + pixels[k + 2] * keep) / oa * 255), 0, 255);
        pixels[k + 3] = (byte)Math.Clamp((int)MathF.Round(oa * 255), 0, 255);
    }
    private static void Text(string s, float x, float y, int size, HudColor c, HudAnchor anchor, bool bold = false)
        => r.Texts.Add(new HudText(s, x, y, size, c, anchor, bold));
}
