using System.Diagnostics;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Corners = SprocketUiKit.ShapeRaster.Corners;
namespace SprocketUiKit;

/// <summary>Pointer event on a <see cref="CanvasView"/> (0.1.2).</summary>
public readonly struct CanvasPointer
{
    /// <summary>Position in canvas units (reference px, origin top-left of the canvas).</summary>
    public Vector2 Position { get; init; }
    /// <summary>Position in world units of the built-in pan/zoom camera (= Position without PanZoom).</summary>
    public Vector2 World { get; init; }
    /// <summary>Movement since the previous drag event (canvas units); zero for clicks.</summary>
    public Vector2 Delta { get; init; }
    /// <summary>0 = left, 1 = right, 2 = middle.</summary>
    public int Button { get; init; }
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
}

/// <summary>
/// Drawing surface (0.1.2). Technique: the vector shapes are rasterised on the CPU (anti-aliased,
/// pure <c>Raster</c> code shared with the tests) into one RGBA32 <see cref="Texture2D"/> shown by a
/// stock <see cref="RawImage"/>, uploaded with <c>LoadRawTextureData(IntPtr)</c>: no Graphic subclass,
/// no delegate marshalling, no IMGUI. Runtime textures (<see cref="Painter.Image"/>) are pooled
/// RawImages under the shapes (GPU pan/zoom/clip, <c>tex.Apply()</c> shows immediately), text is pooled
/// TMP labels above them. Redraws only on <see cref="Invalidate"/>, a <see cref="Version"/> change, a
/// resize/rescale, or at the <see cref="Fps"/> rate; between redraws a frame costs a few comparisons.
/// Mouse: left/right/middle click and drag, wheel, hover, Shift/Ctrl/Alt, via the kit's Input System path.
/// </summary>
public sealed class CanvasView : Element
{
    private readonly Action<Painter> draw;
    private readonly Painter painter;
    private Func<int>? version; private int lastVersion = int.MinValue;
    private float fps, nextDraw, nextResizeDraw; private bool invalid = true;
    private Rgba background = Tok.CanvasBg; private bool border = true;
    private Action<CanvasPointer>? onClick, onDrag, onDragEnd; private Action<float, CanvasPointer>? onWheel;
    private Func<string, string?>? regionTooltip;
    internal readonly CanvasCamera Camera = new();
    private bool panZoom; private int panButton = 2; private float zoomStep = 1.15f;
    private BoxView? box; private RawImage? image; private Texture2D? tex; private RectTransform? clip, imageLayer, textLayer;
    private int texW, texH; private float drawnScale, requestedScale = -1; private float drawnW, drawnH;
    // pointer capture
    private bool down, moved; private int button; private Vec start, last;
    internal readonly List<(string Id, float[] Xy)> Regions = new();
    private string? hoverRegion;
    /// <summary>Milliseconds the last redraw took (raster + upload), for diagnostics.</summary>
    public double LastDrawMs { get; private set; }

    internal CanvasView(Action<Painter> draw) { this.draw = draw; painter = new Painter(this); FillHeight = false; }

    /// <summary>Redraw on the next frame (e.g. after <c>tex.Apply()</c> or a data change).</summary>
    public void Invalidate() => invalid = true;
    /// <summary>Redraw whenever this value changes (diagram style).</summary>
    public CanvasView Version(Func<int> v) { version = v; return this; }
    /// <summary>Redraw continuously at most this many times per second (0 = off), e.g. 25 for moving markers.</summary>
    public CanvasView Fps(float perSecond) { fps = MathF.Max(0, perSecond); return this; }
    /// <summary>Background colour (default #1b1c15) and whether the 1 px border is drawn.</summary>
    public CanvasView Background(Color colour, bool withBorder = true) { background = Draw.R(colour); border = withBorder; box?.Set(background, border ? Tok.Divider : Rgba.Clear); return this; }
    public CanvasView OnClick(Action<CanvasPointer> click) { onClick = click; return this; }
    /// <summary>Called every frame while a button drags (after 4 px of movement); see <see cref="CanvasPointer.Button"/>.</summary>
    public CanvasView OnDrag(Action<CanvasPointer> drag) { onDrag = drag; return this; }
    public CanvasView OnDragEnd(Action<CanvasPointer> end) { onDragEnd = end; return this; }
    /// <summary>Wheel notches (+ = away from the user) at a pointer position.</summary>
    public CanvasView OnWheel(Action<float, CanvasPointer> wheel) { onWheel = wheel; return this; }
    /// <summary>Tooltip text for the region id under the pointer (see <see cref="Painter.Region"/>).</summary>
    public CanvasView RegionTooltip(Func<string, string?> textForRegionId) { regionTooltip = textForRegionId; return this; }
    /// <summary>
    /// Built-in camera: drag with <paramref name="panButton"/> (default 2 = middle) pans, the wheel zooms
    /// at the pointer. The painter starts in world units; <see cref="ToWorld"/>/<see cref="ToLocal"/> convert.
    /// </summary>
    public CanvasView PanZoom(int panButton = 2, float minZoom = .1f, float maxZoom = 20, float wheelStep = 1.15f)
    {
        panZoom = true; this.panButton = Math.Clamp(panButton, 0, 2); Camera.MinZoom = minZoom; Camera.MaxZoom = MathF.Max(minZoom, maxZoom); zoomStep = MathF.Max(1.01f, wheelStep);
        invalid = true; return this;
    }
    public float Zoom { get => Camera.Zoom; set { Camera.Zoom = Math.Clamp(value, Camera.MinZoom, Camera.MaxZoom); invalid = true; } }
    /// <summary>Camera offset: canvas position of world (0,0).</summary>
    public Vector2 Pan { get => new(Camera.OffsetX, Camera.OffsetY); set { Camera.OffsetX = value.x; Camera.OffsetY = value.y; invalid = true; } }
    /// <summary>Fit a world rectangle into the canvas (aspect kept).</summary>
    public void FitWorld(float x0, float y0, float x1, float y1) { pendingFit = (x0, y0, x1, y1); invalid = true; }
    private (float, float, float, float)? pendingFit;
    public Vector2 ToWorld(Vector2 local) { var (x, y) = Camera.ToWorld(local.x, local.y); return new Vector2(x, y); }
    public Vector2 ToLocal(Vector2 world) { var (x, y) = Camera.ToLocal(world.x, world.y); return new Vector2(x, y); }
    /// <summary>Pointer position in canvas units while hovered, else null.</summary>
    public Vector2? HoverPos => Hovered || down ? ToV(LocalPointer) : null;
    /// <summary>Region id under the pointer, or null.</summary>
    public string? HoverRegion => hoverRegion;
    public float CanvasWidth => Rect.W;
    public float CanvasHeight => Rect.H;

    private static Vector2 ToV(Vec v) => new(v.X, v.Y);
    private Vec LocalPointer => Panel.LocalPointer - new Vec(Rect.X, Rect.Y);
    private CanvasPointer Ptr(Vec local, Vector2 delta, int btn)
    {
        var (wx, wy) = panZoom ? Camera.ToWorld(local.X, local.Y) : (local.X, local.Y);
        return new CanvasPointer { Position = ToV(local), World = new Vector2(wx, wy), Delta = delta, Button = btn, Shift = Kit.ShiftHeld, Ctrl = Kit.CtrlHeld, Alt = Kit.AltHeld };
    }

    // ---------- Kit pointer path ----------
    internal void PointerDown(int btn, Vec panelLocal)
    {
        down = true; moved = false; button = btn; start = last = panelLocal - new Vec(Rect.X, Rect.Y);
    }
    internal void PointerMove(Vec panelLocal)
    {
        if (!down) return;
        var p = panelLocal - new Vec(Rect.X, Rect.Y);
        if (!moved && MathF.Abs(p.X - start.X) + MathF.Abs(p.Y - start.Y) > 4) moved = true;
        if (!moved) return;
        var d = new Vector2(p.X - last.X, p.Y - last.Y); last = p;
        if (d.sqrMagnitude <= 0) return;
        if (panZoom && button == panButton) { Camera.PanBy(d.x, d.y); invalid = true; }
        else if (onDrag != null) { var e = Ptr(p, d, button); Guard.Run(Where + ".OnDrag", () => onDrag(e)); }
    }
    internal void PointerUp(Vec panelLocal)
    {
        if (!down) return;
        down = false;
        var p = panelLocal - new Vec(Rect.X, Rect.Y);
        if (!moved) { if (onClick != null) { var e = Ptr(p, default, button); Guard.Run(Where + ".OnClick", () => onClick(e)); } }
        else if (onDragEnd != null && !(panZoom && button == panButton)) { var e = Ptr(p, default, button); Guard.Run(Where + ".OnDragEnd", () => onDragEnd(e)); }
    }
    internal void PointerCancel() { down = false; }
    internal override void OnPress(Vec p) => Kit.BeginCanvasCapture(this, 0, p);
    internal override bool OnScroll(float notches, Vec p)
    {
        if (!panZoom && onWheel == null) return false;
        var local = p - new Vec(Rect.X, Rect.Y);
        if (panZoom) { Camera.ZoomAt(local.X, local.Y, MathF.Pow(zoomStep, notches)); invalid = true; }
        if (onWheel != null) { var e = Ptr(local, default, -1); Guard.Run(Where + ".OnWheel", () => onWheel(notches, e)); }
        return true;
    }
    internal override bool Interactive => true;
    internal override string? TooltipNow() => regionTooltip != null && hoverRegion != null ? Guard.Get(Where + ".RegionTooltip", () => regionTooltip(hoverRegion), null) : base.TooltipNow();

    // ---------- Build / layout ----------
    protected override void Build()
    {
        box = new BoxView(Rt!, "Background", Tok.Radius); Draw.Stretch(box.Rt); box.Set(background, border ? Tok.Divider : Rgba.Clear);
        clip = Draw.Node("Clip", Rt!); Draw.Stretch(clip, 1, 1, 1, 1); clip.gameObject.AddComponent<RectMask2D>();
        imageLayer = Draw.Node("Images", clip);
        var node = Draw.Node("Shapes", clip);
        image = node.gameObject.AddComponent<RawImage>(); image.raycastTarget = false;
        image.uvRect = new UnityEngine.Rect(0, 1, 1, -1); // raster row 0 is the top
        textLayer = Draw.Node("Text", clip);
        painter.ResetPools();
        invalid = true;
    }
    internal override void Unbuild()
    {
        box = null; image = null; clip = imageLayer = textLayer = null;
        if (tex != null) { try { UnityEngine.Object.Destroy(tex); } catch { } tex = null; }
        texW = texH = 0; painter.ResetPools();
        base.Unbuild();
    }
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : -FlexWeight;
    internal override float MeasureHeight(float width) => 120;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clipRect)
    {
        base.ArrangeCore(r, origin, clipRect);
        if (image != null) Draw.Place(image.rectTransform, 0, 0, MathF.Max(0, r.W - 2), MathF.Max(0, r.H - 2));
        if (imageLayer != null) Draw.Place(imageLayer, 0, 0, r.W - 2, r.H - 2);
        if (textLayer != null) Draw.Place(textLayer, 0, 0, r.W - 2, r.H - 2);
        if (MathF.Abs(r.W - drawnW) > .5f || MathF.Abs(r.H - drawnH) > .5f) invalid = true;
    }

    internal override void Refresh(float now)
    {
        if (image == null) return;
        // hover region
        hoverRegion = null;
        if (Hovered && Regions.Count > 0)
        {
            var p = LocalPointer; float s = drawnScale > 0 ? drawnScale : 1;
            for (int i = Regions.Count - 1; i >= 0; i--) if (Geometry.PointInPolygon(Regions[i].Xy, p.X * s, p.Y * s)) { hoverRegion = Regions[i].Id; break; }
        }
        if (version != null) { int v = Guard.Get(Where + ".Version", version, lastVersion); if (v != lastVersion) { lastVersion = v; invalid = true; } }
        float scale = Kit.CurrentScale(Panel);
        if (MathF.Abs(scale - requestedScale) > .0001f) invalid = true;
        if (fps > 0 && now >= nextDraw) invalid = true;
        if (!invalid || Rect.W < 4 || Rect.H < 4) return;
        // 0.1.4: while the panel's edge is dragged, redraw at most 10 times a second (the image stretches
        // in between); the release frame redraws at the final size
        if (Kit.ResizePanel == Panel && now < nextResizeDraw) return;
        nextResizeDraw = now + .1f;
        if (pendingFit is { } pf) { pendingFit = null; Camera.Fit(pf.Item1, pf.Item2, pf.Item3, pf.Item4, Rect.W - 2, Rect.H - 2); }
        invalid = false; nextDraw = now + (fps > 0 ? 1f / fps : 0);
        Redraw(scale);
    }
    private void Redraw(float scale)
    {
        var sw = Stopwatch.StartNew();
        float w = Rect.W - 2, h = Rect.H - 2;
        int pw = Math.Clamp((int)MathF.Ceiling(w * scale), 1, 4096), ph = Math.Clamp((int)MathF.Ceiling(h * scale), 1, 4096);
        drawnScale = pw / MathF.Max(1, w); requestedScale = scale; drawnW = Rect.W; drawnH = Rect.H;
        painter.Begin(pw, ph, drawnScale, panZoom ? Camera.Transform : Affine.Identity);
        Guard.Run(Where + ".Draw", () => draw(painter));
        painter.End(imageLayer!, textLayer!);
        Upload(pw, ph);
        LastDrawMs = sw.Elapsed.TotalMilliseconds;
    }
    private void Upload(int pw, int ph)
    {
        if (tex == null || texW != pw || texH != ph)
        {
            if (tex != null) { try { UnityEngine.Object.Destroy(tex); } catch { } }
            tex = new Texture2D(pw, ph, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "UiKit canvas " + Where };
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            texW = pw; texH = ph;
            image!.texture = tex;
        }
        var handle = GCHandle.Alloc(painter.Raster.Pixels, GCHandleType.Pinned);
        try { tex.LoadRawTextureData(handle.AddrOfPinnedObject(), pw * ph * 4); }
        finally { handle.Free(); }
        tex.Apply(false, false);
    }
    internal RectTransform? TextRoot => textLayer;
    internal RectTransform? ImageRoot => imageLayer;
}

/// <summary>
/// Drawing commands for a <see cref="CanvasView"/> (0.1.2). Coordinates are canvas units (reference
/// px), or world units under <see cref="CanvasView.PanZoom"/>/<see cref="ViewBox"/>/<see cref="Push"/>.
/// Line widths, dash lengths, dot radii and text sizes are reference px and do not scale with zoom.
/// Colours: <see cref="Color"/> or "#rrggbb[aa]". Order: images under shapes, text on top.
/// </summary>
public sealed class Painter
{
    private readonly CanvasView owner;
    internal readonly Raster Raster = new();
    private float px; // device px per reference px
    private Affine baseTf, tf;
    private readonly Stack<(Affine Tf, (int, int, int, int) Clip)> stack = new();
    private readonly List<(RawImage Img, bool Used)> images = new();
    private readonly List<(TextMeshProUGUI Text, TextMeshProUGUI Shadow)> texts = new();
    private int imageCount, textCount;
    private RectTransform? imageLayer, textLayer;
    private float[] pts = new float[64];
    internal Painter(CanvasView owner) { this.owner = owner; }
    /// <summary>Canvas size in reference px.</summary>
    public Vector2 Size => new(Raster.Width / px, Raster.Height / px);
    /// <summary>Seconds since startup (unscaled), for animation.</summary>
    public float Time => UnityEngine.Time.unscaledTime;

    internal void ResetPools() { images.Clear(); texts.Clear(); }
    internal void Begin(int w, int h, float pxPerUnit, Affine camera)
    {
        Raster.Resize(w, h); Raster.Clear(0);
        px = pxPerUnit; baseTf = Affine.Scale(px, px).Times(camera); tf = baseTf;
        stack.Clear(); imageCount = textCount = 0; owner.Regions.Clear();
    }
    internal void End(RectTransform imgLayer, RectTransform txtLayer)
    {
        imageLayer = imgLayer; textLayer = txtLayer;
        for (int i = imageCount; i < images.Count; i++) Draw.SetActive(images[i].Img, false);
        for (int i = textCount; i < texts.Count; i++) { Draw.SetActive(texts[i].Text, false); Draw.SetActive(texts[i].Shadow, false); }
    }

    // ---------- Transform / clip ----------
    /// <summary>Map the world rectangle (x0,y0)-(x1,y1) onto the canvas (aspect kept and centred by default).</summary>
    public void ViewBox(float x0, float y0, float x1, float y1, bool keepAspect = true)
    {
        var sz = Size; float ww = MathF.Max(1e-6f, x1 - x0), wh = MathF.Max(1e-6f, y1 - y0);
        float sx = sz.x / ww, sy = sz.y / wh;
        if (keepAspect) sx = sy = MathF.Min(sx, sy);
        float ox = (sz.x - ww * sx) / 2, oy = (sz.y - wh * sy) / 2;
        tf = Affine.Scale(px, px).Times(Affine.Translate(ox, oy)).Times(Affine.Scale(sx, sy)).Times(Affine.Translate(-x0, -y0));
    }
    /// <summary>Nested transform: translate, then rotate (degrees) and scale about the new origin. Undo with <see cref="Pop"/>.</summary>
    public void Push(float translateX, float translateY, float rotationDeg = 0, float scale = 1)
    {
        stack.Push((tf, Raster.Clip));
        tf = tf.Times(Affine.Translate(translateX, translateY)).Times(Affine.Rotate(rotationDeg)).Times(Affine.Scale(scale, scale));
    }
    public void Pop()
    {
        if (stack.Count == 0) return;
        var (t, c) = stack.Pop(); tf = t; Raster.SetClip(c.Item1, c.Item2, c.Item3, c.Item4);
    }
    /// <summary>Clip later shapes to a rectangle in current units (bounding box if rotated); restored by Pop.</summary>
    public void Clip(float x, float y, float w, float h)
    {
        var (ax, ay) = tf.Apply(x, y); var (bx, by) = tf.Apply(x + w, y + h); var (cx, cy) = tf.Apply(x + w, y); var (dx, dy) = tf.Apply(x, y + h);
        var cur = Raster.Clip;
        float x0 = MathF.Max(cur.X0, Min4(ax, bx, cx, dx)), y0 = MathF.Max(cur.Y0, Min4(ay, by, cy, dy));
        float x1 = MathF.Min(cur.X1, Max4(ax, bx, cx, dx)), y1 = MathF.Min(cur.Y1, Max4(ay, by, cy, dy));
        Raster.SetClip(x0, y0, MathF.Max(x0, x1), MathF.Max(y0, y1));
    }
    private static float Min4(float a, float b, float c, float d) => MathF.Min(MathF.Min(a, b), MathF.Min(c, d));
    private static float Max4(float a, float b, float c, float d) => MathF.Max(MathF.Max(a, b), MathF.Max(c, d));
    /// <summary>Canvas position (reference px) of a point in current units.</summary>
    public Vector2 ToCanvas(float x, float y) { var (a, b) = tf.Apply(x, y); return new Vector2(a / px, b / px); }

    // ---------- Colour helpers ----------
    private static uint U(Color c) { Color32 k = c; return Raster.Pack(k.r, k.g, k.b, k.a); }
    private static uint U(string? hex) => Draw.TryHex(hex) is { } c ? Raster.Pack(c) : 0;
    private float W(float refPx) => MathF.Max(.1f, refPx * px);

    private ReadOnlySpan<float> Transform(IReadOnlyList<Vector2> p, out int n)
    {
        n = p.Count; if (pts.Length < 2 * n) pts = new float[Math.Max(2 * n, pts.Length * 2)];
        for (int i = 0; i < n; i++) { var (x, y) = tf.Apply(p[i].x, p[i].y); pts[2 * i] = x; pts[2 * i + 1] = y; }
        return new ReadOnlySpan<float>(pts, 0, 2 * n);
    }
    private ReadOnlySpan<float> Transform(float[] xy)
    {
        int n = xy.Length / 2; if (pts.Length < 2 * n) pts = new float[Math.Max(2 * n, pts.Length * 2)];
        for (int i = 0; i < n; i++) { var (x, y) = tf.Apply(xy[2 * i], xy[2 * i + 1]); pts[2 * i] = x; pts[2 * i + 1] = y; }
        return new ReadOnlySpan<float>(pts, 0, 2 * n);
    }
    private ReadOnlySpan<float> Quad(float x, float y, float w, float h)
    {
        var (ax, ay) = tf.Apply(x, y); var (bx, by) = tf.Apply(x + w, y); var (cx, cy) = tf.Apply(x + w, y + h); var (dx, dy) = tf.Apply(x, y + h);
        pts[0] = ax; pts[1] = ay; pts[2] = bx; pts[3] = by; pts[4] = cx; pts[5] = cy; pts[6] = dx; pts[7] = dy;
        return new ReadOnlySpan<float>(pts, 0, 8);
    }

    // ---------- Shapes (Color) ----------
    public void Line(float x1, float y1, float x2, float y2, Color colour, float widthPx = 1, float dashPx = 0) => LineU(x1, y1, x2, y2, U(colour), widthPx, dashPx);
    /// <summary>Open (or closed) polyline; dash &gt; 0 for dashed routes.</summary>
    public void Polyline(IReadOnlyList<Vector2> points, Color colour, float widthPx = 1, float dashPx = 0, bool closed = false)
    { if (points == null || points.Count < 2) return; Raster.Polyline(Transform(points, out _), W(widthPx), U(colour), dashPx * px, closed); }
    public void FillPolygon(IReadOnlyList<Vector2> points, Color fill)
    { if (points == null || points.Count < 3) return; Raster.FillPolygon(Transform(points, out _), U(fill)); }
    public void Rect(float x, float y, float w, float h, Color stroke, float widthPx = 1, float dashPx = 0) { var q = Quad(x, y, w, h); Raster.Polyline(q, W(widthPx), U(stroke), dashPx * px, true); }
    public void FillRect(float x, float y, float w, float h, Color fill) => Raster.FillPolygon(Quad(x, y, w, h), U(fill));
    /// <summary>Ring (outline) with radius in current units.</summary>
    public void Circle(float cx, float cy, float radius, Color stroke, float widthPx = 1) { var (x, y) = tf.Apply(cx, cy); Raster.Ring(x, y, radius * tf.ScaleFactor, W(widthPx), U(stroke)); }
    public void FillCircle(float cx, float cy, float radius, Color fill) { var (x, y) = tf.Apply(cx, cy); Raster.FillCircle(x, y, radius * tf.ScaleFactor, U(fill)); }
    /// <summary>Marker at a point with a radius in reference px (does not scale with zoom); optional outline ring.</summary>
    public void Dot(float x, float y, float radiusPx, Color fill, Color? outline = null, float outlinePx = 1.5f)
    {
        var (a, b) = tf.Apply(x, y);
        Raster.FillCircle(a, b, radiusPx * px, U(fill));
        if (outline is { } o) Raster.Ring(a, b, radiusPx * px, W(outlinePx), U(o));
    }
    public void Arrow(float x1, float y1, float x2, float y2, Color colour, float widthPx = 2, float headPx = 8)
    { var (a, b) = tf.Apply(x1, y1); var (c, d) = tf.Apply(x2, y2); Raster.Arrow(a, b, c, d, W(widthPx), headPx * px, U(colour)); }

    // ---------- Shapes (hex strings, diagram style) ----------
    public void Line(float x1, float y1, float x2, float y2, string colour, float widthPx = 1, float dashPx = 0) => LineU(x1, y1, x2, y2, U(colour), widthPx, dashPx);
    public void Rect(float x, float y, float w, float h, string fill, string? stroke = null, float strokePx = 1, float dashPx = 0)
    {
        var q = Quad(x, y, w, h); Raster.FillPolygon(q, U(fill));
        if (stroke != null) { q = Quad(x, y, w, h); Raster.Polyline(q, W(strokePx), U(stroke), dashPx * px, true); }
    }
    /// <summary>Polygon from x0,y0,x1,y1,... with fill and optional stroke.</summary>
    public void Poly(float[] xy, string fill, string? stroke = null, float strokePx = 1, float dashPx = 0)
    {
        if (xy == null || xy.Length < 6) return;
        Raster.FillPolygon(Transform(xy), U(fill));
        if (stroke != null) Raster.Polyline(Transform(xy), W(strokePx), U(stroke), dashPx * px, true);
    }
    public void Arrow(float x1, float y1, float x2, float y2, string colour, float widthPx = 2, float headPx = 8)
    { var (a, b) = tf.Apply(x1, y1); var (c, d) = tf.Apply(x2, y2); Raster.Arrow(a, b, c, d, W(widthPx), headPx * px, U(colour)); }
    /// <summary>Hatch a polygon with stripes at <paramref name="angleDeg"/>, <paramref name="spacingPx"/> apart.</summary>
    public void Hatch(float[] xy, string colour, float angleDeg, float spacingPx, float widthPx = 1)
    { if (xy == null || xy.Length < 6) return; Raster.FillPolygon(Transform(xy), U(colour), angleDeg, spacingPx * px, W(widthPx)); }
    /// <summary>Hover region (polygon in current units); see <see cref="CanvasView.RegionTooltip"/>.</summary>
    public void Region(string id, float[] xy)
    { if (xy == null || xy.Length < 6) return; var t = Transform(xy).ToArray(); owner.Regions.Add((id, t)); }

    private void LineU(float x1, float y1, float x2, float y2, uint c, float widthPx, float dashPx)
    {
        var (a, b) = tf.Apply(x1, y1); var (d, e) = tf.Apply(x2, y2);
        if (dashPx > 0) { pts[0] = a; pts[1] = b; pts[2] = d; pts[3] = e; Raster.Polyline(new ReadOnlySpan<float>(pts, 0, 4), W(widthPx), c, dashPx * px); }
        else Raster.Segment(a, b, d, e, W(widthPx), c);
    }

    // ---------- Text ----------
    /// <summary>Text at a point in screen px size (does not scale with zoom), with a 1 px shadow; anchor = which side of the text sits on the point.</summary>
    public void Text(float x, float y, string text, Color colour, TextStyle style = TextStyle.Value, TextAnchor anchor = TextAnchor.MiddleCenter, float rotationDeg = 0, bool shadow = true)
        => TextCore(x, y, text, style, Draw.R(colour), anchor, rotationDeg, shadow, null);
    /// <summary>Diagram-style text: optional hex colour (null = style colour) and optional background box.</summary>
    public void Text(float x, float y, string text, TextStyle style, string? colour = null, TextAnchor anchor = TextAnchor.MiddleCenter, float rotationDeg = 0, string? background = null)
        => TextCore(x, y, text, style, Draw.TryHex(colour) ?? Draw.Style(style).Color, anchor, rotationDeg, false, Draw.TryHex(background));
    private void TextCore(float x, float y, string text, TextStyle style, Rgba colour, TextAnchor anchor, float rotationDeg, bool shadow, Rgba? background)
    {
        if (string.IsNullOrEmpty(text) || textLayerNull()) return;
        var p = ToCanvas(x, y);
        while (texts.Count <= textCount)
        {
            var sh = Draw.Text(Draw.Node("Shadow", owner.TextRoot!), style); sh.textWrappingMode = TextWrappingModes.NoWrap; sh.overflowMode = TextOverflowModes.Overflow;
            var t = Draw.Text(Draw.Node("Text", owner.TextRoot!), style); t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
            texts.Add((t, sh));
        }
        var (tm, sd) = texts[textCount++];
        Draw.ApplyStyle(tm, style); tm.color = Draw.C(colour); Draw.SetText(tm, text);
        float w = Draw.Width(tm, text) + 2, h = MathF.Round(Draw.Style(style).Size * 1.35f);
        var (ax, ay) = AnchorOffset(anchor, w, h);
        Draw.SetActive(tm, true);
        var rt = tm.rectTransform; rt.pivot = new Vector2(ax, 1 - ay);
        rt.anchoredPosition = new Vector2(p.x, -p.y); rt.sizeDelta = new Vector2(w, h); rt.localEulerAngles = new Vector3(0, 0, -rotationDeg);
        tm.alignment = TextAlignmentOptions.Midline;
        Draw.SetActive(sd, shadow);
        if (shadow)
        {
            Draw.ApplyStyle(sd, style); sd.color = new Color(0, 0, 0, .85f); Draw.SetText(sd, text); sd.alignment = TextAlignmentOptions.Midline;
            var srt = sd.rectTransform; srt.pivot = rt.pivot; srt.anchoredPosition = new Vector2(p.x + 1, -p.y - 1); srt.sizeDelta = rt.sizeDelta; srt.localEulerAngles = rt.localEulerAngles;
            sd.transform.SetSiblingIndex(tm.transform.GetSiblingIndex());
        }
        if (background is { } bg)
        {
            float bx = p.x - ax * w - 4, by = p.y - ay * h - 1;
            Raster.FillRect(bx * px, by * px, (w + 8) * px, (h + 2) * px, Raster.Pack(bg));
        }
    }
    private bool textLayerNull() => owner.TextRoot == null;
    private static (float X, float Y) AnchorOffset(TextAnchor a, float w, float h) => a switch
    {
        TextAnchor.UpperLeft => (0, 0), TextAnchor.UpperCenter => (.5f, 0), TextAnchor.UpperRight => (1, 0),
        TextAnchor.MiddleLeft => (0, .5f), TextAnchor.MiddleRight => (1, .5f),
        TextAnchor.LowerLeft => (0, 1), TextAnchor.LowerCenter => (.5f, 1), TextAnchor.LowerRight => (1, 1),
        _ => (.5f, .5f)
    };

    // ---------- Images ----------
    /// <summary>
    /// Runtime texture in a rectangle (current units; translate/scale only, rotation is ignored),
    /// optional uv sub-rectangle and tint. Drawn under all shapes; clipped to the canvas.
    /// </summary>
    public void Image(Texture texture, float x, float y, float w, float h, UnityEngine.Rect? uv = null, Color? tint = null)
    {
        if (texture == null || owner.ImageRoot == null) return;
        var a = ToCanvas(x, y); var b = ToCanvas(x + w, y + h);
        while (images.Count <= imageCount)
        {
            var node = Draw.Node("Image", owner.ImageRoot);
            var ri = node.gameObject.AddComponent<RawImage>(); ri.raycastTarget = false;
            images.Add((ri, true));
        }
        var img = images[imageCount++].Img;
        Draw.SetActive(img, true);
        if (img.texture != texture) img.texture = texture;
        img.uvRect = uv ?? new UnityEngine.Rect(0, 0, 1, 1);
        img.color = tint ?? UnityEngine.Color.white;
        Draw.Place(img.rectTransform, MathF.Min(a.x, b.x), MathF.Min(a.y, b.y), MathF.Abs(b.x - a.x), MathF.Abs(b.y - a.y));
    }
}
