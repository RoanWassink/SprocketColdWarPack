using SprocketUiKit;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

// Pure tests for everything that does not need Unity: layout, stepper, text editing,
// hit testing, z-order, scale, popup/tooltip placement, colours, raster shapes, Guard.
int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); passed++; }
bool Near(double a, double b, double eps = 1e-4) => Math.Abs(a - b) <= eps;

// ---------- Scale ----------
Check(Near(KitMath.Scale(1080, 1), 1), "scale 1080p = 1");
Check(Near(KitMath.Scale(1440, 1), 4f / 3), "scale 1440p = 1.333");
Check(Near(KitMath.Scale(2160, 1), 2), "scale 2160p = 2");
Check(Near(KitMath.Scale(1080, 1.25f), 1.25), "config factor multiplies");
Check(Near(KitMath.Scale(1080, 9), 2) && Near(KitMath.Scale(1080, 0.1f), .5), "factor clamped to 0.5-2");
Check(Near(KitMath.Scale(0, 1), 1) && Near(KitMath.Scale(1080, float.NaN), 1), "invalid input -> 1");
var k = KitMath.ScreenToKit(960, 1440, 1440, 4f / 3);
Check(Near(k.X, 720) && Near(k.Y, 0), "screen (bottom-left px) to kit (top-left units) at 1440p");
k = KitMath.ScreenToKit(0, 0, 1080, 1);
Check(Near(k.X, 0) && Near(k.Y, 1080), "screen origin maps to kit bottom-left");

// ---------- Layout ----------
var d = KitMath.Distribute(1240, 0, new float[] { 340, 0, 360 });
Check(Near(d[0], 340) && Near(d[1], 540) && Near(d[2], 360), "columns 340 | flex | 360 in 1240");
d = KitMath.Distribute(1000, 10, new float[] { 0, 0, 0, 0 });
Check(d.All(x => Near(x, 242.5)), "four equal flex columns with gaps");
d = KitMath.Distribute(300, 0, new float[] { -1, -3 });
Check(Near(d[0], 75) && Near(d[1], 225), "flex weights 1:3");
d = KitMath.Distribute(100, 0, new float[] { 80, 80, 0 });
Check(Near(d[2], 0) && Near(d[0], 80), "overflow keeps fixed sizes, flex >= 0");
var o = KitMath.Offsets(14, 8, new float[] { 10, 20, 30 });
Check(Near(o[0], 14) && Near(o[1], 32) && Near(o[2], 60), "offsets with gap");
var sh = KitMath.StackHeights(400, 10, new float[] { 36, 50, 36 }, new[] { false, true, false });
Check(Near(sh[1], 50 + (400 - 122 - 20)), "stack fill child takes leftover");
sh = KitMath.StackHeights(100, 10, new float[] { 36, 50, 36 }, new[] { false, true, false });
Check(Near(sh[1], 50), "fill child never shrinks below its height");
sh = KitMath.StackHeights(200, 0, new float[] { 0, 0 }, new[] { true, true });
Check(Near(sh[0], 100) && Near(sh[1], 100), "two fill children share equally");
var rw = KitMath.RowWidths(500, 8, new float[] { 10, 34, -1, 120, 32 });
Check(Near(rw[2], 500 - 10 - 34 - 120 - 32 - 32), "row: spacer gets the rest");
rw = KitMath.RowWidths(300, 0, new float[] { -1, -1, 100 });
Check(Near(rw[0], 100) && Near(rw[1], 100), "row: two flex share");

// ---------- Stepper ----------
Check(Near(KitMath.Step(0.1, 0.1, 1, 0, 10), 0.2), "0.1 + step 0.1 = 0.2");
double v = 0; for (int i = 0; i < 3; i++) v = KitMath.Step(v, .1, 1, 0, 10);
Check(v == .3, "three steps of 0.1 give exactly 0.3 (no drift)");
Check(Near(KitMath.Step(0.35, 0.1, 1, 0, 10), 0.4) && Near(KitMath.Step(0.35, 0.1, -1, 0, 10), 0.3), "off-grid value snaps to the next grid point");
Check(Near(KitMath.Step(9.95, 0.1, 1, 0, 10), 10) && Near(KitMath.Step(10, 0.1, 1, 0, 10), 10), "clamped at max");
Check(Near(KitMath.Step(0, 0.1, -1, 0, 10), 0), "clamped at min");
Check(Near(KitMath.Step(0.1, 0.5, 1, 0.1, 60), 0.6), "grid anchored at min");
Check(Near(KitMath.Step(7, 5, 1, double.NegativeInfinity, double.PositiveInfinity), 10), "unbounded grid anchored at 0");
Check(KitMath.FormatValue(2, .1, "s") == "2.0 s" && KitMath.FormatValue(90, 5, "°") == "90°" && KitMath.FormatValue(62, 5, "%") == "62%", "value formatting");
Check(KitMath.FormatValue(-0.0001, .1, null) == "0.0", "no negative zero");

// ---------- Text editing ----------
var t = new TextEdit { MaxLength = 10 };
t.Begin("Hatch");
Check(t.Caret == 5 && !t.Changed, "begin puts caret at the end");
t.Insert(" 1"); Check(t.Text == "Hatch 1" && t.Changed, "typing appends");
t.Backspace(); Check(t.Text == "Hatch ", "backspace");
t.Left(); t.Left(); t.Insert('X'); Check(t.Text == "HatcXh ", "insert at caret after arrows");
t.Home(); t.Delete(); Check(t.Text == "atcXh ", "home + delete");
t.SelectAll(); t.Insert('Z'); Check(t.Text == "Z" && !t.AllSelected, "Ctrl+A then typing replaces all");
t.SelectAll(); t.Backspace(); Check(t.Text == "", "Ctrl+A then backspace clears");
t.Insert("0123456789ABC"); Check(t.Text == "0123456789", "max length enforced");
t.Revert(); Check(t.Text == "Hatch" && !t.Changed, "Esc reverts to the original");
t.Filter = c => char.IsDigit(c); t.Begin(""); t.Insert("a1b2"); Check(t.Text == "12", "filter rejects characters");
t.Filter = null; t.Begin(""); t.Insert("\n\t"); Check(t.Text == "", "control characters ignored");
Check(KeyChars.Char("A", false) == 'a' && KeyChars.Char("a", true) == 'A', "letters with shift");
Check(KeyChars.Char("Digit1", false) == '1' && KeyChars.Char("Digit1", true) == '!' && KeyChars.Char("Digit0", true) == ')', "digit row with shift");
Check(KeyChars.Char("Minus", true) == '_' && KeyChars.Char("Period", false) == '.' && KeyChars.Char("Slash", true) == '?', "punctuation");
Check(KeyChars.Char("NumpadPlus", false) == '+' && KeyChars.Char("Numpad7", false) == '7' && KeyChars.Char("Space", false) == ' ', "numpad and space");
Check(KeyChars.Char("Enter", false) == null && KeyChars.Char("Escape", false) == null, "non-typing keys map to nothing");
var rep = new KeyRepeat();
Check(rep.Tick(true, true, 0) && !rep.Tick(false, true, .2f) && rep.Tick(false, true, .46f) && !rep.Tick(false, true, .47f) && rep.Tick(false, true, .5f), "key repeat: press, delay, rate");
Check(!rep.Tick(false, false, 1), "key repeat stops on release");

// ---------- Hit testing / z-order ----------
var r = new RectF(10, 20, 100, 50);
Check(r.Contains(new Vec(10, 20)) && !r.Contains(new Vec(110, 20)) && !r.Contains(new Vec(10, 70)) && r.Contains(new Vec(109.9f, 69.9f)), "half-open rectangle");
var a = new RectF(0, 0, 50, 50); var b = new RectF(50, 0, 50, 50); var pt = new Vec(50, 10);
Check(!a.Contains(pt) && b.Contains(pt), "adjacent cells never both hit");
var clip = new RectF(0, 0, 100, 100).Intersect(new RectF(50, 50, 100, 100));
Check(Near(clip.X, 50) && Near(clip.W, 50) && Near(clip.H, 50), "clip intersection");
Check(new RectF(0, 0, 10, 10).Intersect(new RectF(20, 20, 5, 5)).W == 0, "disjoint intersection is empty");
var z = new ZOrder<string>();
z.BringToFront("A"); z.BringToFront("B"); z.BringToFront("C");
Check(z.Top == "C", "last opened on top");
z.BringToFront("A"); Check(z.Top == "A" && z.Items.SequenceEqual(new[] { "B", "C", "A" }), "click brings to front");
var rects = new Dictionary<string, RectF> { ["A"] = new(0, 0, 100, 100), ["B"] = new(50, 50, 100, 100), ["C"] = new(500, 500, 10, 10) };
Check(z.TopAt(n => rects[n].Contains(new Vec(60, 60))) == "A", "overlap: topmost panel wins");
z.BringToFront("B"); Check(z.TopAt(n => rects[n].Contains(new Vec(60, 60))) == "B", "overlap after refocus");
Check(z.TopAt(n => rects[n].Contains(new Vec(400, 400))) == null, "no panel under pointer");
var open = new HashSet<string> { "C" }; Check(z.TopWhere(open.Contains) == "C", "Esc target = topmost open panel");
z.Remove("C"); Check(!z.Items.Contains("C"), "remove from z-order");

// ---------- Placement ----------
var cp = KitMath.ClampToView(new RectF(-2000, -50, 600, 400), 1920, 1080);
Check(Near(cp.X, 120 - 600) && Near(cp.Y, 0), "drag clamp keeps 120 units reachable on the left, top on screen");
cp = KitMath.ClampToView(new RectF(5000, 5000, 600, 400), 1920, 1080);
Check(Near(cp.X, 1800) && Near(cp.Y, 1080 - Tok.HeaderH), "drag clamp right/bottom keeps the title bar visible");
var c0 = KitMath.Centre(1240, 640, 1920, 1080); Check(Near(c0.X, 340) && Near(c0.Y, 220), "centred first position");
var anchor = new RectF(100, 100, 200, 34);
var pop = KitMath.PlacePopup(anchor, 230, 200, 1920, 1080);
Check(Near(pop.Y, 138) && Near(pop.X, 100) && Near(pop.H, 200), "popup opens below with 4 gap");
pop = KitMath.PlacePopup(new RectF(100, 950, 200, 32), 230, 200, 1920, 1080);
Check(Near(pop.Bottom, 946) && pop.Y < 950, "popup flips up near the bottom");
pop = KitMath.PlacePopup(new RectF(1800, 100, 100, 32), 230, 200, 1920, 1080);
Check(pop.Right <= 1916, "popup clamped horizontally");
pop = KitMath.PlacePopup(new RectF(100, 500, 200, 32), 230, 2000, 1920, 1080);
Check(pop.Y >= 4 && pop.Bottom <= 1076, "oversized popup shrinks to the room (then scrolls)");
var tip = KitMath.PlaceTooltip(new Vec(1900, 1070), 280, 60, 1920, 1080);
Check(tip.Right <= 1916 && tip.Bottom <= 1076, "tooltip stays on screen");
Check(Near(KitMath.ClampScroll(500, 300, 200), 100) && KitMath.ClampScroll(-5, 300, 200) == 0 && KitMath.ClampScroll(50, 100, 200) == 0, "scroll clamping");
var vr = KitMath.VisibleRange(80, 100, 36, 4, 50); Check(vr.First == 2 && vr.Count == 3, "visible row range");
Check(Near(KitMath.WheelNotches(120), 1) && Near(KitMath.WheelNotches(-240), -2) && Near(KitMath.WheelNotches(1), 1), "wheel notches (120 or 1 per notch)");

// ---------- Position store ----------
Check(KitMath.FormatPosition(40.25f, 70) == "40.3,70" || KitMath.FormatPosition(40.25f, 70) == "40.2,70", "position format invariant");
Check(KitMath.TryParsePosition("40.5,70", out var px, out var py) && Near(px, 40.5) && Near(py, 70), "position parse");
Check(!KitMath.TryParsePosition("", out _, out _) && !KitMath.TryParsePosition("NaN,1", out _, out _) && !KitMath.TryParsePosition("1;2", out _, out _), "bad positions rejected");
Check(KitMath.PositionKey("sprocket.example", "io panel[1]") == "sprocket.example.io_panel_1_", "config key sanitised");

// ---------- Lamp afterglow ----------
float last = -100;
Check(KitMath.LampLit(true, 1, ref last, .3f) && KitMath.LampLit(false, 1.2f, ref last, .3f) && !KitMath.LampLit(false, 1.31f, ref last, .3f), "lamp holds 0.3 s");
last = -100; Check(!KitMath.LampLit(false, 1, ref last, 0), "lamp without hold");

// ---------- Look ----------
Check(Look.Button(ButtonStyle.Primary, true, false, false).Fill.Equals(Rgba.Hex("#dc9e33")) && Look.Button(ButtonStyle.Primary, true, false, false).Text.Equals(Rgba.Hex("#1b1406")), "primary colours from the canvas");
Check(Look.Button(ButtonStyle.Normal, true, true, false).Fill.Equals(Rgba.Hex("#3a372e")) && Look.Button(ButtonStyle.Normal, true, true, false).Border.Equals(Rgba.Hex("#8a7a52")), "hover colours from the canvas");
Check(Look.Button(ButtonStyle.Danger, true, false, false).Border.Equals(Rgba.Hex("#e07a4f")), "danger border");
Check(Look.Button(ButtonStyle.Primary, false, true, true).Text.Equals(Rgba.Hex("#6f6857")), "disabled overrides style and state");
Check(Look.Button(ButtonStyle.Quiet, true, false, false).Fill.A == 0, "quiet is transparent");
Check(Look.Status(StatusKind.Success).Equals(Rgba.Hex("#8fd99c")) && Look.StatusIcon(StatusKind.Success) == Icon.Check && Look.StatusIcon(StatusKind.Info) == Icon.None, "status colours and icons");
Check(Look.Chip(ChipStyle.Context).Radius == 10 && Look.Chip(ChipStyle.Accent).Border.Equals(Rgba.Hex("#a57a2c")), "chip looks");
var rows = Look.PopupRows(new[] { new Choice("Cannon 1", "Cannons"), new Choice("Cannon 2", "Cannons"), new Choice("Action 1", "Keys"), new Choice("Mast") });
Check(rows.Count == 6 && rows[0].Heading && rows[0].Label == "Cannons" && rows[3].Heading && rows[3].Label == "Keys" && rows[5].Index == 3 && rows[1].Index == 0, "grouped dropdown rows: one heading per group");
Check(Look.PopupRows(new[] { new Choice("a"), new Choice("b") }).All(x => !x.Heading), "ungrouped dropdown has no headings");
Check(Rgba.Hex("#14150f").R == 0x14 && Rgba.Hex("#00000080").A == 0x80, "hex colours");
Check(Tok.PanelGlass.A == 245, "panel alpha .96");

// ---------- Raster shapes ----------
var (size, border) = ShapeRaster.BoxLayout(4, 1);
var box = ShapeRaster.Box(4, ShapeRaster.Corners.All, 0);
Check(box.Length == size * size && border == 17, "9-slice box layout (radius 4 at 4x)");
Check(box[(size / 2) * size + size / 2] == 1f, "box centre opaque (uniform 9-slice centre)");
Check(box[(size - 1) * size + 0] < .05f, "rounded corner transparent");
var square = ShapeRaster.Box(4, ShapeRaster.Corners.Top, 0);
Check(square[0] > .95f && square[(size - 1) * size] < .05f, "only top corners rounded (row 0 = bottom)");
var ring = ShapeRaster.Box(4, ShapeRaster.Corners.All, 1);
Check(ring[(size / 2) * size + size / 2] == 0 && ring[(size / 2) * size + 1] > .9f, "ring: hollow centre, 1 unit edge");
var circle = ShapeRaster.Circle(32);
Check(circle[16 * 32 + 16] == 1 && circle[0] == 0, "circle");
var icon = ShapeRaster.IconMask(Icon.Close, 64);
Check(icon[32 * 64 + 32] > .9f && icon[32 * 64 + 2] < .05f, "close icon: centre stroke, empty edge");
foreach (Icon i in Enum.GetValues<Icon>()) if (i != Icon.None) Check(ShapeRaster.IconMask(i, 32).Max() > .5f, "icon drawn: " + i);
var shadow = ShapeRaster.Shadow(6, 24, out int ss, out int sb);
Check(shadow[(ss / 2) * ss + ss / 2] > .99f && shadow[0] < .02f && sb == 31, "shadow falloff");

// ---------- Guard ----------
var logged = new List<string>(); Guard.Sink = logged.Add; Guard.ResetForTests();
Check(!Guard.Run("x", () => throw new InvalidOperationException("boom")) && logged.Count == 1, "guard catches and logs");
for (int i = 0; i < 5; i++) Guard.Run("x", () => throw new Exception());
Check(logged.Count == 1 && Guard.Count("x") == 6, "guard logs once per site, counts the rest");
Check(Guard.Get("y", () => 5, 0) == 5 && Guard.Get<int>("y", () => throw new Exception(), 7) == 7, "guard getter fallback");
Check(Guard.Run("z", null) && Guard.Get<int>("z", null, 3) == 3, "null callbacks are no-ops");

// ================= 0.1.2 =================
// ---------- Slider rounding ----------
Check(Near(Logic.Snap(0.34, 0, 1, .1), .3) && Near(Logic.Snap(0.35, 0, 1, .1), .4), "slider snaps to step (half away from zero)");
Check(Logic.Snap(.1 + .2, 0, 1, .1) == .3, "slider snap removes binary drift");
Check(Near(Logic.Snap(7, 2, 20, 5), 7) && Near(Logic.Snap(8, 2, 20, 5), 7) && Near(Logic.Snap(10, 2, 20, 5), 12), "slider grid anchored at min");
Check(Near(Logic.Snap(500, 0, 150, 5), 150) && Near(Logic.Snap(-3, 0, 150, 5), 0), "slider clamps to min/max");
Check(Near(Logic.Snap(0.123, 0, 1, 0), .123), "step 0 only clamps");
Check(Near(Logic.SliderAt(.5, 0, 180, 1), 90) && Near(Logic.SliderAt(-1, 0, 180, 1), 0) && Near(Logic.SliderAt(2, 0, 180, 1), 180), "track position to value");
Check(Near(Logic.SliderFraction(45, 0, 180), .25) && Logic.SliderFraction(5, 5, 5) == 0 && Logic.SliderFraction(double.NaN, 0, 1) == 0, "value to knob fraction");
Check(Near(Logic.SliderDrag(90, 50, 200, 0, 180, 1, false), 135) && Near(Logic.SliderDrag(90, 50, 200, 0, 180, 1, true), 95) && Near(Logic.SliderDrag(90, 40, 200, 0, 180, 1, true), 94), "drag: normal vs Shift fine (10x slower)");
Check(Near(Logic.SliderWheel(45, 1, 0, 180, 1, false), 46) && Near(Logic.SliderWheel(45, -2, 0, 180, 1, false), 43) && Near(Logic.SliderWheel(45, 1, 0, 180, 1, true), 55), "wheel: step per notch, Shift = 10 steps");
Check(Near(Logic.SliderWheel(45, .3f, 0, 180, 1, false), 46), "wheel: a partial notch still moves one step");
Check(Near(Logic.SliderWheel(178, 1, 0, 180, 5, true), 180), "wheel clamps at max");
Check(Logic.TryParseNumber(" 12,5 ", out var pn) && Near(pn, 12.5) && !Logic.TryParseNumber("abc", out _) && !Logic.TryParseNumber("NaN", out _), "typed numbers (comma ok, NaN rejected)");

// ---------- Page stack ----------
var ps = new PageStack();
Check(ps.Current == null && ps.Depth == 0 && !ps.Back(), "page stack starts at root");
ps.Navigate("list"); ps.Navigate("edit");
Check(ps.Current == "edit" && ps.Depth == 2 && ps.Path.SequenceEqual(new[] { "list", "edit" }), "push two pages");
Check(!ps.Navigate("edit") && ps.Depth == 2, "push current page is a no-op");
ps.Navigate("adv"); ps.Navigate("list");
Check(ps.Current == "list" && ps.Depth == 1, "push a page already in the trail goes back to it");
Check(ps.Back() && ps.Current == null && ps.Depth == 0, "back to root");
ps.Navigate("a"); ps.Navigate("b"); ps.Navigate(null); Check(ps.Depth == 0, "navigate(null) = root");
ps.Navigate("a"); ps.Navigate("b"); ps.Remove("a"); Check(ps.Depth == 0, "removing a page truncates the trail");

// ---------- Esc order page -> panel, confirm keys ----------
Check(Logic.Esc(true, true, true, 2, true) == EscTarget.TextField, "Esc: text field first");
Check(Logic.Esc(false, true, true, 2, true) == EscTarget.Popup, "Esc: then dropdown");
Check(Logic.Esc(false, false, true, 2, true) == EscTarget.Modal, "Esc: then modal");
Check(Logic.Esc(false, false, false, 2, true) == EscTarget.Page && Logic.Esc(false, false, false, 1, true) == EscTarget.Page, "Esc: then one page back");
Check(Logic.Esc(false, false, false, 0, true) == EscTarget.Panel, "Esc: at the root page it closes the panel");
Check(Logic.Esc(false, false, false, 0, false) == EscTarget.None, "Esc: nothing open");
Check(Logic.ConfirmKey(false, true, false) == ModalKey.Cancel && Logic.ConfirmKey(false, true, true) == ModalKey.Cancel, "confirm: Esc cancels");
Check(Logic.ConfirmKey(true, false, false) == ModalKey.Confirm, "confirm: Enter confirms a normal confirmation");
Check(Logic.ConfirmKey(true, false, true) == ModalKey.Cancel, "confirm: Enter cancels a Danger confirmation (safe default)");
Check(Logic.ConfirmKey(false, false, true) == ModalKey.None && Logic.ConfirmKey(true, true, false) == ModalKey.Cancel, "confirm: no key / Esc wins");

// ---------- HUD anchors, fade ----------
var hr = Logic.HudRect(HudCorner.TopLeft, 16, 16, 300, 100, 1920, 1080); Check(Near(hr.X, 16) && Near(hr.Y, 16), "HUD top-left");
hr = Logic.HudRect(HudCorner.TopRight, 24, 10, 300, 100, 1920, 1080); Check(Near(hr.X, 1920 - 324) && Near(hr.Y, 10), "HUD top-right");
hr = Logic.HudRect(HudCorner.BottomLeft, 16, 20, 300, 100, 1920, 1080); Check(Near(hr.X, 16) && Near(hr.Y, 1080 - 120), "HUD bottom-left");
hr = Logic.HudRect(HudCorner.BottomRight, 16, 16, 300, 100, 2560, 1440); Check(Near(hr.Right, 2544) && Near(hr.Bottom, 1424), "HUD bottom-right (1440p view)");
hr = Logic.HudRect(HudCorner.TopCentre, 0, 8, 300, 100, 1920, 1080); Check(Near(hr.X, 810) && Near(hr.Y, 8), "HUD top centre");
hr = Logic.HudRect(HudCorner.BottomRight, 16, 16, 3000, 2000, 1920, 1080); Check(hr.X == 0 && hr.Y == 0, "HUD larger than the view stays on screen");
Check(Near(Logic.Fade(0, true, .2f, .1f), .5) && Logic.Fade(.9f, true, .2f, .1f) == 1 && Near(Logic.Fade(1, false, .2f, .05f), .75) && Logic.Fade(.3f, true, 0, .01f) == 1, "HUD fade");

// ---------- Canvas transform and hit test ----------
var cam = new CanvasCamera();
cam.ZoomAt(100, 50, 2);
var (wx0, wy0) = cam.ToWorld(100, 50); Check(Near(wx0, 100) && Near(wy0, 50) && Near(cam.Zoom, 2), "zoom at cursor keeps the world point under the cursor");
cam.PanBy(30, -10); var (lx0, ly0) = cam.ToLocal(100, 50); Check(Near(lx0, 130) && Near(ly0, 40), "pan moves local positions");
var (rx0, ry0) = cam.ToWorld(cam.ToLocal(-37, 12).X, cam.ToLocal(-37, 12).Y); Check(Near(rx0, -37) && Near(ry0, 12), "ToWorld/ToLocal are inverse");
cam.ZoomAt(0, 0, 1000); Check(Near(cam.Zoom, cam.MaxZoom), "zoom clamped to max");
cam.Fit(-200, -150, 200, 150, 800, 600); var (fx, fy) = cam.ToLocal(0, 0); Check(Near(cam.Zoom, 2) && Near(fx, 400) && Near(fy, 300), "fit world rectangle centred");
var af = Affine.Translate(10, 20).Times(Affine.Rotate(90)).Times(Affine.Scale(2, 2));
var (ax0, ay0) = af.Apply(1, 0); Check(Near(ax0, 10) && Near(ay0, 22), "affine: scale, rotate 90 (y down), translate");
var (bx0, by0) = af.Inverse().Apply(ax0, ay0); Check(Near(bx0, 1) && Near(by0, 0), "affine inverse");
Check(Near(af.ScaleFactor, 2), "affine scale factor");
var tri = new float[] { 0, 0, 100, 0, 0, 100 };
Check(Geometry.PointInPolygon(tri, 10, 10) && !Geometry.PointInPolygon(tri, 80, 80) && !Geometry.PointInPolygon(tri, -1, 5), "point in polygon");
Check(Near(Geometry.SegmentDistance(50, 10, 0, 0, 100, 0), 10) && Near(Geometry.SegmentDistance(-3, 4, 0, 0, 100, 0), 5), "distance to segment");

// ---------- Raster ----------
var ras = new Raster(); ras.Resize(40, 30); ras.Clear(0);
uint red = Raster.Pack(255, 0, 0, 255), half = Raster.Pack(0, 0, 255, 128);
ras.FillRect(10, 10, 10, 10, red);
Check(ras.Pixels[15 * 40 + 15] == red && ras.Pixels[5 * 40 + 5] == 0, "fill rect: inside opaque, outside untouched");
Check(Raster.Unpack(ras.Pixels[15 * 40 + 10]).A == 255 && Raster.Unpack(ras.Pixels[15 * 40 + 20]).A == 0, "fill rect: pixel-aligned edges are exact");
ras.Clear(0); ras.FillRect(10.5f, 10, 10, 10, red);
Check(Math.Abs(Raster.Unpack(ras.Pixels[15 * 40 + 10]).A - 128) <= 2, "fill rect: half-pixel edge is half covered (anti-aliasing)");
ras.Clear(Raster.Pack(255, 255, 255, 255)); ras.FillRect(0, 0, 40, 30, half);
var mix = Raster.Unpack(ras.Pixels[0]); Check(Math.Abs(mix.R - 127) <= 2 && Math.Abs(mix.B - 255) <= 1 && mix.A == 255, "alpha blend over opaque");
ras.Clear(0); ras.FillRect(0, 0, 40, 30, half); Check(Raster.Unpack(ras.Pixels[0]).A == 128 && Raster.Unpack(ras.Pixels[0]).B == 255, "blend onto transparent keeps colour, alpha");
ras.Clear(0); ras.FillCircle(20, 15, 8, red);
Check(Raster.Unpack(ras.Pixels[15 * 40 + 20]).A == 255 && ras.Pixels[0] == 0 && Raster.Unpack(ras.Pixels[15 * 40 + 28]).A < 200, "filled circle");
ras.Clear(0); ras.Ring(20, 15, 8, 2, red);
Check(Raster.Unpack(ras.Pixels[15 * 40 + 20]).A == 0 && Raster.Unpack(ras.Pixels[15 * 40 + 12]).A > 200, "ring: hollow centre");
ras.Clear(0); ras.FillPolygon(new float[] { -100, -100, 1000, -100, 1000, 1000, -100, 1000 }, red);
Check(ras.Pixels.Take(40 * 30).All(p => p == red), "polygon far outside the raster still fills it exactly (edge clipping)");
ras.Clear(0); ras.SetClip(0, 0, 20, 30); ras.FillRect(0, 0, 40, 30, red); ras.ResetClip();
Check(ras.Pixels[5 * 40 + 10] == red && ras.Pixels[5 * 40 + 30] == 0, "clip rectangle");
ras.Clear(0); ras.Polyline(new float[] { 2, 15, 38, 15 }, 2, red, 4);
int on = Enumerable.Range(0, 40).Count(x => Raster.Unpack(ras.Pixels[15 * 40 + x]).A > 100);
Check(on > 12 && on < 26, "dashed line: about half the pixels on");
ras.Clear(0); ras.Segment(5, 5, 35, 25, .5f, red); Check(ras.Pixels.Take(40 * 30).Max(p => p >> 24) < 200, "sub-pixel width fades instead of thinning");
ras.Clear(0); ras.FillPolygon(new float[] { 0, 0, 40, 0, 40, 30, 0, 30 }, red, 45, 6, 1);
int hatched = ras.Pixels.Take(40 * 30).Count(p => (p >> 24) > 100); Check(hatched > 100 && hatched < 700, "hatch masks the fill into stripes");
ras.Clear(0); ras.Arrow(5, 15, 35, 15, 2, 8, red); Check(Raster.Unpack(ras.Pixels[15 * 40 + 33]).A > 100 && Raster.Unpack(ras.Pixels[13 * 40 + 29]).A > 50 && Raster.Unpack(ras.Pixels[10 * 40 + 20]).A == 0, "arrow: shaft and head");
// cost of one 800x600 redraw (pure part: clear + 300 shapes), median of 5
var big = new Raster(); big.Resize(800, 600);
var rnd = new Random(1); var times = new List<double>();
var sw = System.Diagnostics.Stopwatch.StartNew();
for (int run = 0; run < 6; run++)
{
    sw.Restart();
    big.Clear(0);
    for (int i = 0; i < 100; i++) big.Segment(rnd.Next(800), rnd.Next(600), rnd.Next(800), rnd.Next(600), 2, Raster.Pack(220, 158, 51, 255));
    for (int i = 0; i < 100; i++) big.FillCircle(rnd.Next(800), rnd.Next(600), 6, Raster.Pack(95, 224, 122, 200));
    for (int i = 0; i < 50; i++) { float x = rnd.Next(780), y = rnd.Next(580); big.FillPolygon(new float[] { x, y, x + 20, y, x + 10, y + 18 }, Raster.Pack(224, 122, 79, 160)); }
    var route = new float[40]; for (int i = 0; i < 20; i++) { route[2 * i] = i * 40; route[2 * i + 1] = 300 + MathF.Sin(i) * 100; }
    for (int i = 0; i < 50; i++) big.Polyline(route, 2.5f, Raster.Pack(243, 207, 134, 255), 8);
    big.FillPolygon(new float[] { 0, 0, 800, 0, 800, 600, 0, 600 }, Raster.Pack(0, 0, 0, 30));
    if (run > 0) times.Add(sw.Elapsed.TotalMilliseconds);
}
times.Sort(); double median = times[times.Count / 2];
Console.WriteLine($"INFO canvas redraw 800x600, 300 shapes + full-size fill (pure raster, no upload): median {median:0.00} ms (runs {string.Join(", ", times.Select(t => t.ToString("0.0")))})");
Check(median < 60, "800x600 redraw well under a frame budget for 20-30 fps");

// ---------- Shortcuts, row shrink ----------
Check(Logic.TryParseChord("Ctrl+D", out var ch) && ch.Key == "D" && ch.Ctrl && !ch.Shift && ch.ToString() == "Ctrl+D", "chord Ctrl+D");
Check(Logic.TryParseChord("alt + up", out ch) && ch.Key == "UpArrow" && ch.Alt && ch.ToString() == "Alt+Up", "chord Alt+Up alias");
Check(Logic.TryParseChord("Delete", out ch) && ch.Key == "Delete" && !ch.Ctrl, "chord Delete");
Check(Logic.TryParseChord("Ctrl+Shift+1", out ch) && ch.Key == "Digit1" && ch.Ctrl && ch.Shift, "chord with digit");
Check(Logic.TryParseChord("F5", out ch) && ch.Key == "F5", "chord F5");
Check(!Logic.TryParseChord("Esc", out _) && !Logic.TryParseChord("Ctrl+", out _) && !Logic.TryParseChord("Ctrl+Shift", out _) && !Logic.TryParseChord("Ctrl+Foo", out _) && !Logic.TryParseChord("A+B", out _), "bad chords rejected (Esc reserved)");
Logic.TryParseChord("Ctrl+D", out ch);
Check(Logic.ChordMatches(ch, true, false, false) && !Logic.ChordMatches(ch, true, true, false) && !Logic.ChordMatches(ch, false, false, false), "chords need exact modifiers");
var sk = Logic.ShrinkWidths(300, 8, new float[] { 200, 150, 60 }, new[] { true, false, false });
Check(Near(sk[0] + sk[1] + sk[2] + 16, 300) && Near(sk[1], 150) && Near(sk[2], 60), "shrink-to-fit shrinks only labels/chips to fit");
sk = Logic.ShrinkWidths(100, 0, new float[] { 200, 150 }, new[] { true, false }); Check(Near(sk[0], 24), "shrink stops at the minimum width");
sk = Logic.ShrinkWidths(500, 8, new float[] { 200, -1, 60 }, new[] { true, false, false }); Check(Near(sk[0], 200) && sk[1] < 0, "no shrink when it fits; flex untouched");
Check(Look.Warning(StatusKind.Warning).Bg.Equals(Rgba.Hex("#2b2410")) && Look.Warning(StatusKind.Error).Border.Equals(Rgba.Hex("#8a4a30")), "warning severity colours");
Check(Look.Lamp(LampKind.Warning).Equals(Rgba.Hex("#dc9e33")) && Look.Lamp(LampKind.Error).Equals(Rgba.Hex("#e07a4f")) && Look.Lamp(LampKind.On).Equals(Rgba.Hex("#5fe07a")), "lamp kind colours");

// ---------- 0.1.3: dashed lines always terminate (0.1.2 froze the game) ----------
{
    var dr = new Raster(); dr.Resize(200, 200);
    var drng = new Random(1);
    var dsw = System.Diagnostics.Stopwatch.StartNew();
    int dcases = 0;
    foreach (float pxs in new[] { 1f, 4f / 3, 1.5f, 2f })
        for (int kc = 0; kc < 2400; kc++)
        {
            float ddash = 8 * pxs * (0.5f + (float)drng.NextDouble());
            var dpts = new float[12];
            for (int p = 0; p < dpts.Length; p++) dpts[p] = (float)drng.NextDouble() * 400 - 100;
            dr.Clear(0); dr.Polyline(dpts, 1.5f, 0xFFFFFFFF, ddash, closed: kc % 2 == 0);
            dcases++;
        }
    // The reported case: ddash 10.666667 with the phase near 64.
    dr.Polyline(new float[] { 0, 0, 64.0000019f, 0, 200, 0 }, 2, 0xFFFFFFFF, 10.666667f);
    Check(dsw.ElapsedMilliseconds < 20000, $"dashed polylines terminate ({dcases} random dcases in {dsw.ElapsedMilliseconds} ms)");
}
// ================= 0.1.4 =================
// ---------- Maximize / restore rectangles (#24) ----------
var mx = Window.Maximized(1920, 1080);
Check(Near(mx.X, 8) && Near(mx.Y, 8) && Near(mx.W, 1904) && Near(mx.H, 1064), "maximized = view minus 8 px margin (1080p)");
mx = Window.Maximized(2560f / (1440f / 1080), 1080);
Check(Near(mx.W, 1920 - 16) && Near(mx.H, 1064), "maximized at 1440p uses the view in kit units");
Check(Near(Tok.MaximizeMargin, 8), "margin token is 8 px");
mx = Window.Maximized(1440, 1080); Check(Near(mx.W, 1424), "maximized follows a new screen size (4:3)");
var rs = Window.ClampSize(900, 560, 900, 560, 1920, 1080); Check(Near(rs.W, 900) && Near(rs.H, 560), "restore size unchanged when it fits");
rs = Window.ClampSize(2400, 1300, 900, 560, 1920, 1080); Check(Near(rs.W, 1920) && Near(rs.H, 1080), "restore size larger than the screen shrinks to it");
rs = Window.ClampSize(1000, 600, 1600, 1200, 1440, 1080); Check(Near(rs.W, 1440) && Near(rs.H, 1080), "screen wins over MinSize");
// ---------- Resize clamping (#25) ----------
var st0 = new RectF(100, 100, 900, 560);
var rz = Window.Resize(st0, ResizeEdge.Corner, 300, 200, 900, 560, 1920, 1080);
Check(Near(rz.W, 1200) && Near(rz.H, 760) && Near(rz.X, 100) && Near(rz.Y, 100), "corner grip grows both axes, top-left stays");
rz = Window.Resize(st0, ResizeEdge.Corner, -500, -500, 900, 560, 1920, 1080);
Check(Near(rz.W, 900) && Near(rz.H, 560), "not smaller than the minimum (creation size)");
rz = Window.Resize(st0, ResizeEdge.Corner, -500, -500, 500, 300, 1920, 1080);
Check(Near(rz.W, 500) && Near(rz.H, 300), "MinSize allows smaller than the creation size");
rz = Window.Resize(st0, ResizeEdge.Right, 5000, 5000, 900, 560, 1920, 1080);
Check(Near(rz.W, 1820) && Near(rz.H, 560), "right grip: width up to the screen edge, height unchanged");
rz = Window.Resize(st0, ResizeEdge.Bottom, 5000, 5000, 900, 560, 1920, 1080);
Check(Near(rz.W, 900) && Near(rz.H, 980), "bottom grip: height up to the screen edge, width unchanged");
rz = Window.Resize(new RectF(1500, 600, 900, 560), ResizeEdge.Corner, -100, -100, 900, 560, 1920, 1080);
Check(Near(rz.W, 900) && Near(rz.H, 560), "minimum kept even when the panel sits partly off screen");
rz = Window.Resize(new RectF(0, 0, 1000, 600), ResizeEdge.Corner, 0, 0, 1600, 1200, 1440, 1080);
Check(Near(rz.W, 1440) && Near(rz.H, 1080), "resize: the screen wins over MinSize");
Check(Window.GripAt(new Vec(898, 300), 900, 560) == ResizeEdge.Right && Window.GripAt(new Vec(902, 300), 900, 560) == ResizeEdge.Right, "right grip band 4 px inside and outside");
Check(Window.GripAt(new Vec(400, 558), 900, 560) == ResizeEdge.Bottom && Window.GripAt(new Vec(400, 563), 900, 560) == ResizeEdge.Bottom, "bottom grip band");
Check(Window.GripAt(new Vec(890, 550), 900, 560) == ResizeEdge.Corner && Window.GripAt(new Vec(903, 563), 900, 560) == ResizeEdge.Corner, "corner grip 16 px square plus the outside band");
Check(Window.GripAt(new Vec(880, 300), 900, 560) == ResizeEdge.None && Window.GripAt(new Vec(905, 300), 900, 560) == ResizeEdge.None && Window.GripAt(new Vec(-1, 559), 900, 560) == ResizeEdge.None, "no grip inside the content or past the band");
Check(Window.GripAt(new Vec(2, 300), 900, 560) == ResizeEdge.None && Window.GripAt(new Vec(400, 1), 900, 560) == ResizeEdge.None, "no grip on the left or top edge");
// ---------- Double-click (#24) ----------
var dc = new DoubleClick();
Check(!dc.Press(10, new Vec(300, 20)) && dc.Press(10.3f, new Vec(302, 21)), "double-click: two presses within 0.5 s and 6 px");
Check(!dc.Press(10.4f, new Vec(302, 21)), "a third press starts over (no triple toggle)");
dc.Reset(); dc.Press(20, new Vec(300, 20)); Check(!dc.Press(20.6f, new Vec(300, 20)), "too slow is not a double-click");
dc.Reset(); dc.Press(30, new Vec(300, 20)); Check(!dc.Press(30.2f, new Vec(340, 20)), "too far apart is not a double-click");
// ---------- Drag from maximized: restore under the pointer (#24) ----------
var max0 = Window.Maximized(1920, 1080);
var ru = Window.RestoreUnderPointer(new Vec(8 + 1904 * .75f, 20), max0, 900, 560);
Check(Near(ru.W, 900) && Near(ru.H, 560) && Near((8 + 1904 * .75f - ru.X) / 900, .75) && Near(20 - ru.Y, 12), "restore under the pointer: same relative x and height in the title bar");
ru = Window.RestoreUnderPointer(new Vec(10, 30), max0, 900, 560);
Check(Near(ru.X, 10 - 900 * (2f / 1904)) && Near(ru.Y, 8), "restore near the left edge keeps the pointer on the title bar");
// ---------- State store: size and maximized next to the position (#25) ----------
Check(Window.Format(new PanelState(340, 220)) == "340,220", "unresized panel keeps the 0.1.3 line");
Check(Window.Format(new PanelState(340, 220, 1400, 800)) == "340,220,1400,800", "resized panel stores x,y,w,h");
Check(Window.Format(new PanelState(340, 220, 1240, 640, true)) == "340,220,1240,640,max", "maximized stores the restore rectangle and max");
Check(Window.TryParse("340,220", out var ps0) && Near(ps0.X, 340) && !ps0.HasSize && !ps0.Maximized, "0.1.3 line with only a position is still valid");
Check(Window.TryParse("340.5,220,1400,800", out var ps1) && ps1.HasSize && Near(ps1.W, 1400) && Near(ps1.H, 800) && !ps1.Maximized, "size line read back");
Check(Window.TryParse("10,20,1240,640,max", out var ps2) && ps2.Maximized && Near(ps2.W, 1240), "maximized line read back");
Check(Window.TryParse(Window.Format(new PanelState(-12.5f, 7, 999.5f, 600.25f, true)), out var ps3) && Near(ps3.X, -12.5) && Near(ps3.W, 999.5) && ps3.Maximized, "format/parse round trip");
Check(!Window.TryParse("1,2,3", out _) && !Window.TryParse("1,2,0,5", out _) && !Window.TryParse("1,2,3,4,big", out _) && !Window.TryParse("a,b", out _) && !Window.TryParse("", out _), "bad state lines rejected");
// ---------- Back on screen (#23) ----------
var fo = Window.FitOnScreen(new RectF(1700, 900, 900, 560), 1920, 1080);
Check(Near(fo.X, 1020) && Near(fo.Y, 520) && Near(fo.W, 900), "remembered position partly off screen comes back fully");
fo = Window.FitOnScreen(new RectF(-300, -40, 900, 560), 1920, 1080); Check(Near(fo.X, 0) && Near(fo.Y, 0), "off the left/top edge comes back");
fo = Window.FitOnScreen(new RectF(100, 50, 2600, 1300), 1920, 1080); Check(Near(fo.X, 0) && Near(fo.Y, 0) && Near(fo.W, 1920) && Near(fo.H, 1080), "remembered size larger than the screen shrinks to the screen");
Check(Window.FullyOnScreen(new RectF(340, 220, 1240, 640), 1920, 1080) && !Window.FullyOnScreen(new RectF(1000, 220, 1240, 640), 1920, 1080), "fully-on-screen test");
var keep = new RectF(340, 220, 1240, 640); var kept = Window.FitOnScreen(keep, 1920, 1080);
Check(Near(kept.X, 340) && Near(kept.Y, 220) && Near(kept.W, 1240), "a panel already on screen is not moved");
// ---------- Columns when resized ----------
var cw0 = Window.ColumnWidths(1240, new float[] { 340, 0, 360 }, 0);
var cw1 = Window.ColumnWidths(1904, new float[] { 340, 0, 360 }, 664);
Check(Near(cw0[1], 540) && Near(cw1[0], 340) && Near(cw1[2], 360) && Near(cw1[1], 1204), "fixed sidebars stay fixed, the flexible column takes the extra width");
var cw2 = Window.ColumnWidths(1240, new float[] { 280, 600, 280 }, 0);
Check(Near(cw2[0], 280) && Near(cw2[1], 600) && Near(cw2[2], 280), "all fixed at the original size: identical to 0.1.3 (slack stays empty)");
var cw3 = Window.ColumnWidths(1540, new float[] { 280, 600, 280 }, 300);
Check(Near(cw3[0], 280) && Near(cw3[1], 900) && Near(cw3[2], 280), "all fixed: the growth goes to the widest (content) column");
var cw4 = Window.ColumnWidths(1540, new float[] { 300, 300, 300 }, 300);
Check(Near(cw4[1], 600) && Near(cw4[0], 300) && Near(cw4[2], 300), "all fixed, equal: an inner column grows, edges stay");
var cw5 = Window.ColumnWidths(700, new float[] { 300, 300 }, 100);
Check(Near(cw5[0] + cw5[1], 700) && Near(cw5[0], 400), "growth is capped at the free width");
var cw6 = Window.ColumnWidths(1240, new float[] { 0, 0 }, 500);
Check(Near(cw6[0], 620) && Near(cw6[1], 620), "flexible columns share as before");
// ---------- RequestClose (#17) ----------
Check(Logic.MayClose(true, null, "t") && !Logic.MayClose(false, null, "t"), "RequestClose: closable closes, not closable never");
Check(!Logic.MayClose(true, () => false, "t") && Logic.MayClose(true, () => true, "t"), "RequestClose: OnCloseRequest decides");
int asked = 0; Check(!Logic.MayClose(false, () => { asked++; return true; }, "t") && asked == 0, "RequestClose: OnCloseRequest is not asked when not closable");
Check(Logic.MayClose(true, () => throw new InvalidOperationException("x"), "t.close"), "RequestClose: a throwing OnCloseRequest closes (as Esc in 0.1.2)");
// ---------- #18: no allocations per poll ----------
{
    var labels = new List<string> { "Cannon 1", "Cannon 2", "Mast" };
    IReadOnlyList<string> Labels() => labels;
    var cache = new ChoiceCache(Labels);
    var first = cache.Get(); cache.Get();
    long before = GC.GetAllocatedBytesForCurrentThread();
    IReadOnlyList<Choice>? got = null;
    for (int i = 0; i < 1000; i++) got = cache.Get();
    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"INFO dropdown options: {bytes} bytes over 1000 polls");
    Check(bytes == 0 && ReferenceEquals(got, first) && got!.Count == 3 && got[2].Label == "Mast", "dropdown string list: 0 bytes per poll, same Choice array");
    labels[2] = "Hinge"; var changed = cache.Get();
    Check(changed[2].Label == "Hinge" && cache.Rebuilds == 2 && !ReferenceEquals(changed, first), "list edited in place is picked up (rebuild once)");
    var fresh = new[] { "Cannon 1", "Cannon 2", "Hinge" };
    var cache2 = new ChoiceCache(() => fresh); cache2.Get(); cache2.Get();
    Check(cache2.Rebuilds == 1, "same labels: no rebuild");
    int count = 7;
    var it = new IntText(() => count);
    string s0 = it.Get(); it.Get();
    before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 1000; i++) it.Get();
    bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"INFO section count: {bytes} bytes over 1000 polls");
    Check(bytes == 0 && s0 == "7" && ReferenceEquals(it.Get(), s0), "section count: 0 bytes per poll");
    count = 12; Check(it.Get() == "12", "section count text follows the value");
}
// ---------- #16: field callback per keystroke ----------
{
    var calls = new List<string>();
    var te = new TextEdit { MaxLength = 8, Filter = c => char.IsDigit(c) || c == '.' };
    te.OnEdit = calls.Add;
    te.Begin("12");
    te.Insert('3'); te.Insert('x'); te.Insert('.'); te.Backspace(); te.Left(); te.Home(); te.Delete(); te.End(); te.Backspace();
    Check(calls.SequenceEqual(new[] { "123", "123.", "123", "23", "2" }), "OnEdit once per changing keystroke (filtered key, caret moves: none)");
    calls.Clear(); te.SelectAll(); te.Insert('9'); Check(calls.SequenceEqual(new[] { "9" }), "Ctrl+A then a key: one call");
    calls.Clear(); te.Revert(); te.Begin(""); te.Backspace(); Check(calls.Count == 0, "Revert, Begin and no-op Backspace do not call");
}
// ---------- Redraw cost on a maximized 1440p panel ----------
{
    float s1440 = 1440f / 1080; var mxr = Window.Maximized(2560 / s1440, 1080);
    // canvas filling the body of the maximized panel (column padding 14), in device px
    int cwpx = (int)MathF.Ceiling((mxr.W - 28 - 2) * s1440), chpx = (int)MathF.Ceiling((mxr.H - Tok.HeaderH - Tok.FooterH - 28 - 2) * s1440);
    var mr = new Raster(); mr.Resize(cwpx, chpx);
    var mrnd = new Random(2); var mtimes = new List<double>();
    var msw = System.Diagnostics.Stopwatch.StartNew();
    for (int run = 0; run < 6; run++)
    {
        msw.Restart();
        mr.Clear(0);
        for (int i = 0; i < 100; i++) mr.Segment(mrnd.Next(cwpx), mrnd.Next(chpx), mrnd.Next(cwpx), mrnd.Next(chpx), 2.7f, Raster.Pack(220, 158, 51, 255));
        for (int i = 0; i < 100; i++) mr.FillCircle(mrnd.Next(cwpx), mrnd.Next(chpx), 8, Raster.Pack(95, 224, 122, 200));
        for (int i = 0; i < 50; i++) { float x = mrnd.Next(cwpx - 30), y = mrnd.Next(chpx - 30); mr.FillPolygon(new float[] { x, y, x + 27, y, x + 13, y + 24 }, Raster.Pack(224, 122, 79, 160)); }
        var mroute = new float[40]; for (int i = 0; i < 20; i++) { mroute[2 * i] = i * cwpx / 19f; mroute[2 * i + 1] = chpx / 2f + MathF.Sin(i) * chpx / 4; }
        for (int i = 0; i < 50; i++) mr.Polyline(mroute, 3.3f, Raster.Pack(243, 207, 134, 255), 10.7f);
        mr.FillPolygon(new float[] { 0, 0, cwpx, 0, cwpx, chpx, 0, chpx }, Raster.Pack(0, 0, 0, 30));
        if (run > 0) mtimes.Add(msw.Elapsed.TotalMilliseconds);
    }
    mtimes.Sort(); double mmed = mtimes[mtimes.Count / 2];
    Console.WriteLine($"INFO canvas redraw on a maximized 1440p panel ({cwpx}x{chpx} px, 300 shapes + full fill, pure raster): median {mmed:0.0} ms (runs {string.Join(", ", mtimes.Select(t => t.ToString("0.0")))}); during an edge drag at most 10 redraws/s");
    Check(mmed < 250, "maximized 1440p redraw fits the 10/s budget while resizing");
}
// ================= 0.1.5 =================
// ---------- Scrollbar thumb geometry ----------
var sb0 = ScrollBarMath.Thumb(0, 1000, 200);
Check(Near(sb0.Offset, 0) && Near(sb0.Length, 40), "thumb at the top: 200*200/1000 = 40 high");
sb0 = ScrollBarMath.Thumb(400, 1000, 200);
Check(Near(sb0.Offset, 80) && Near(sb0.Length, 40), "thumb halfway: offset 80");
sb0 = ScrollBarMath.Thumb(800, 1000, 200);
Check(Near(sb0.Offset, 160) && Near(sb0.Length, 40), "thumb at max: bottom reaches the track bottom");
sb0 = ScrollBarMath.Thumb(5000, 1000, 200);
Check(Near(sb0.Offset, 160), "scroll past max clamps the thumb");
sb0 = ScrollBarMath.Thumb(0, 100000, 200);
Check(Near(sb0.Length, Tok.ScrollMinH) && Near(sb0.Offset, 0), "thumb never shorter than 20 px");
sb0 = ScrollBarMath.Thumb(100, 100, 200);
Check(Near(sb0.Length, 200) && Near(sb0.Offset, 0) && !ScrollBarMath.Overflow(100, 200), "no overflow: no thumb travel");
sb0 = ScrollBarMath.Thumb(0, 1000, 10);
Check(Near(sb0.Length, 10), "thumb never taller than a tiny track");
Check(Near(Tok.ScrollW, 3) && Near(Tok.ScrollWideW, 6) && Near(Tok.ScrollHitW, 10) && Near(Tok.ScrollMinH, 20), "scrollbar tokens 3/6/10/20 px");

// ---------- Drag keeps the grab point, clamps at both ends ----------
var dragStart = ScrollBarMath.Thumb(1000, 5000, 200);
var dragged = ScrollBarMath.DragTo(1000, 90, 180, 5000, 200);
var dragEnd = ScrollBarMath.Thumb(dragged, 5000, 200);
Check(Near(dragged, 3400) && Near(dragEnd.Offset - dragStart.Offset, 90), "drag: scroll moves proportionally; thumb keeps the grab point");
Check(Near(ScrollBarMath.DragTo(1000, -10000, 180, 5000, 200), 0), "drag clamps at the top");
Check(Near(ScrollBarMath.DragTo(1000, 10000, 180, 5000, 200), 4800), "drag clamps at the maximum");
Check(Near(ScrollBarMath.DragTo(1000, 50, 0, 5000, 200), 1000), "no thumb travel: drag keeps the current offset");

// ---------- Track paging ----------
Check(Near(ScrollBarMath.PageTarget(0, 1000, 200, 100, 0, 1), 200), "page down one viewport when the thumb is far away");
Check(Near(ScrollBarMath.PageTarget(400, 1000, 200, 150, 0, 1), 550), "page down stops when the thumb bottom reaches the cursor");
Check(ScrollBarMath.PartAt(150, 0, 1000, 200, 550) == ScrollPart.Thumb, "after the page step the cursor is on the thumb");
Check(Near(ScrollBarMath.PageTarget(400, 1000, 200, 20, 0, -1), 200), "page up one viewport when the thumb is far away");
Check(Near(ScrollBarMath.PageTarget(150, 1000, 200, 10, 0, -1), 50), "page up stops when the thumb top reaches the cursor");
Check(ScrollBarMath.PartAt(10, 0, 1000, 200, 50) == ScrollPart.Thumb, "after the page-up step the cursor is on the thumb");
Check(Near(ScrollBarMath.PageTarget(0, 1000, 200, 100, 0, 1), 200) && Near(ScrollBarMath.PageTarget(800, 1000, 200, 0, 0, -1), 600), "paging never leaves 0..max");
Check(Near(ScrollBarMath.PageTarget(0, 100, 200, 100, 0, 1), 0), "no paging without overflow");

// ---------- Track part and click strip ----------
Check(ScrollBarMath.PartAt(20, 0, 1000, 200, 0) == ScrollPart.Thumb && ScrollBarMath.PartAt(100, 0, 1000, 200, 0) == ScrollPart.Below && ScrollBarMath.PartAt(-1, 0, 1000, 200, 0) == ScrollPart.None, "track parts above/on/below the thumb");
Check(ScrollBarMath.PartAt(100, 0, 100, 200, 0) == ScrollPart.None && ScrollBarMath.PartAt(200, 0, 1000, 200, 0) == ScrollPart.None, "no bar without overflow or outside the track");
Check(ScrollBarMath.InStrip(95, 100, true) && ScrollBarMath.InStrip(90, 100, true) && !ScrollBarMath.InStrip(89.9f, 100, true) && !ScrollBarMath.InStrip(100, 100, true), "hit strip is at most 10 px from the right edge");
Check(!ScrollBarMath.InStrip(95, 100, false), "no hit strip without overflow");

// ---------- Resize grip vs scrollbar ----------
Check(Window.ResolveGrip(ResizeEdge.Right, true, true) == ResizeEdge.None, "inside the panel the scrollbar wins over the right grip");
Check(Window.ResolveGrip(ResizeEdge.Right, true, false) == ResizeEdge.Right, "outside the panel the resize grip still wins");
Check(Window.ResolveGrip(ResizeEdge.Right, false, true) == ResizeEdge.Right && Window.ResolveGrip(ResizeEdge.Corner, true, true) == ResizeEdge.None, "no bar: the grip wins; a bar also wins in the corner band");
// Composer geometry: a ScrollView in the rightmost column ends at panel W - ColumnPadX (14), so its
// 10 px strip stays clear of the panel's 4 px grip band at both the normal and maximized width.
var normalPanel = new RectF(0, 0, 1240, 640);
var maxPanel = Window.Maximized(1920, 1080);
float normalBarRight = normalPanel.W - Tok.ColumnPadX, maxBarRight = maxPanel.W - Tok.ColumnPadX;
Check(Near(normalBarRight, 1226) && Window.GripAt(new Vec(normalBarRight, 300), normalPanel.W, normalPanel.H) == ResizeEdge.None && Window.GripAt(new Vec(normalPanel.W - 2, 300), normalPanel.W, normalPanel.H) == ResizeEdge.Right, "normal panel: rightmost-column bar edge is clear of the right grip band");
Check(Near(maxBarRight, 1890) && Window.GripAt(new Vec(maxBarRight, 300), maxPanel.W, maxPanel.H) == ResizeEdge.None && Window.GripAt(new Vec(maxPanel.W - 2, 300), maxPanel.W, maxPanel.H) == ResizeEdge.Right, "maximized panel: rightmost-column bar edge is clear of the right grip band");

// ---------- New code allocates nothing per frame ----------
{
    for (int i = 0; i < 10; i++) { var th = ScrollBarMath.Thumb(i, 2000, 300); ScrollBarMath.DragTo(100, i, 200, 2000, 300); ScrollBarMath.PageTarget(100, 2000, 300, i, 0, 1); ScrollBarMath.PartAt(i, 0, 2000, 300, 100); ScrollBarMath.InStrip(i, 300, true); Window.ResolveGrip(ResizeEdge.Right, true, true); }
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 1000; i++)
    {
        var th = ScrollBarMath.Thumb(i, 2000, 300);
        ScrollBarMath.DragTo(100, i, 200, 2000, 300);
        ScrollBarMath.PageTarget(100, 2000, 300, i, 0, 1);
        ScrollBarMath.PartAt(i, 0, 2000, 300, 100);
        ScrollBarMath.InStrip(i, 300, true);
        Window.ResolveGrip(ResizeEdge.Right, true, true);
        if (th.Length < 0) throw new Exception("unreachable");
    }
    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"INFO scrollbar math: {bytes} bytes over 1000 frames");
    Check(bytes == 0, "scrollbar math: 0 bytes over 1000 frames");
}
// ================= 0.1.6 =================
// ---------- Table column widths: Fixed, Auto and weighted Fill ----------
{
    var tw = TableLayout.Compute(500, 10, new[] { TableColumnSpec.Fixed(100), TableColumnSpec.Auto(80), TableColumnSpec.Fill(2, 40), TableColumnSpec.Fill(1, 40) });
    Check(tw.Length == 4 && tw[0] == 100 && tw[1] == 80 && tw[2] == 193 && tw[3] == 97, "table 500: fixed 100, auto 80, fill 2:1 = 193/97");
    Check(tw.Sum() + 3 * 10 == 500, "table widths and gaps sum to the available width");
    tw = TableLayout.Compute(400, 10, new[] { TableColumnSpec.Fill(1, 40), TableColumnSpec.Fill(3, 40) });
    Check(tw[0] == 98 && tw[1] == 292 && tw.Sum() + 10 == 400, "table fill weights 1:3 split the whole width");
}
// ---------- Too narrow: Fill to minimum first, then Auto proportionally, Fixed never ----------
{
    var tw = TableLayout.Compute(260, 10, new[] { TableColumnSpec.Fixed(100), TableColumnSpec.Auto(80), TableColumnSpec.Fill(1, 40), TableColumnSpec.Fill(1, 40) });
    Check(tw[0] == 100 && tw[1] == 50 && tw[2] == 40 && tw[3] == 40, "table narrow: fills at 40, auto shrinks 80 -> 50, fixed stays 100");
    Check(tw.Sum() + 3 * 10 == 260, "table narrow widths still sum to the width");
    tw = TableLayout.Compute(230, 10, new[] { TableColumnSpec.Fixed(100), TableColumnSpec.Auto(80), TableColumnSpec.Auto(40), TableColumnSpec.Fill(1, 40) });
    Check(tw[0] == 100 && tw[1] == 40 && tw[2] == 20 && tw[3] == 40, "table narrow: two autos shrink proportionally (80:40 -> 40:20)");
    tw = TableLayout.Compute(150, 20, new[] { TableColumnSpec.Fixed(100), TableColumnSpec.Auto(80), TableColumnSpec.Fill(1, 40) });
    Check(tw[0] == 100 && tw[1] == 0 && tw[2] == 40, "table narrower than fixed + fill minimum: fixed still 100, auto 0, fill 40");
}
// ---------- Minimums, whole pixels and exact sums ----------
{
    var tw = TableLayout.Compute(160, 0, new[] { TableColumnSpec.Fill(1, 60), TableColumnSpec.Fill(1, 60) });
    Check(tw[0] == 80 && tw[1] == 80, "table fill above its minimum shares by weight");
    tw = TableLayout.Compute(100, 0, new[] { TableColumnSpec.Fill(1, 60), TableColumnSpec.Fill(1, 60) });
    Check(tw[0] == 60 && tw[1] == 60, "table below both fill minimums keeps the minimums (and overflows)");
    Check(Near(Tok.TableMinCol, 40), "table fill minimum token is 40 px");
    tw = TableLayout.Compute(503, 7, new[] { TableColumnSpec.Fixed(97.4f), TableColumnSpec.Auto(89), TableColumnSpec.Fill(1.7f, 40), TableColumnSpec.Fill(.3f, 40) });
    Check(tw.All(w => w >= 0) && tw.Sum() + 3 * 7 == 503, "table widths are whole pixels and sum exactly to 503");
}
// ---------- Header and body share x positions ----------
{
    var tw = TableLayout.Compute(500, 10, new[] { TableColumnSpec.Fixed(100), TableColumnSpec.Auto(80), TableColumnSpec.Fill(2, 40), TableColumnSpec.Fill(1, 40) });
    var x = TableLayout.Positions(tw, 10);
    Check(x[0] == 0 && x[1] == 110 && x[2] == 200 && x[3] == 403, "table header and body use one set of column x positions");
    Check(x[3] + tw[3] == 500, "table last column ends exactly on the available width");
}
// ---------- Alignment offsets ----------
{
    Check(TableLayout.AlignOffset(100, 30, 8, CellAlign.Left) == 8, "table align left: padding only");
    Check(TableLayout.AlignOffset(100, 30, 8, CellAlign.Right) == 62, "table align right: 8 + 84 - 30");
    Check(TableLayout.AlignOffset(100, 30, 8, CellAlign.Centre) == 35, "table align centre: 8 + (84 - 30) / 2");
    Check(TableLayout.AlignOffset(40, 200, 8, CellAlign.Right) == 8, "table alignment clamps text wider than the cell");
}
// ---------- Ellipsis, header lines, row index and zebra ----------
{
    Check(TableLayout.Truncates(80, 79) && !TableLayout.Truncates(79, 79) && !TableLayout.Truncates(79.2f, 79), "table ellipsis only when the text is wider than the cell");
    Check(TableLayout.HeaderLineCount(16, 16) == 1 && TableLayout.HeaderLineCount(30, 16) == 2 && TableLayout.HeaderLineCount(64, 16) == 2, "table header wraps to at most two lines");
    Check(!TableLayout.Zebra(0) && TableLayout.Zebra(1) && !TableLayout.Zebra(2) && TableLayout.Zebra(3), "table zebra alternates from the first row");
    Check(TableLayout.RowAt(0, 20, 32, 10) == 0 && TableLayout.RowAt(12, 20, 32, 10) == 1 && TableLayout.RowAt(50, 20, 32, 10) == 2 && TableLayout.RowAt(-1, 0, 32, 10) == -1, "table row index accounts for the scroll offset");
    var vr0 = TableLayout.VisibleRows(40, 100, 32, 10);
    Check(vr0.First == 1 && vr0.Count == 4, "table visible rows: scroll 40, viewport 100 -> rows 1..4");
    var zr = TableLayout.RowColours(1, false, false, true, false);
    Check(zr.Fill.Equals(Tok.TableZebra) && zr.Border.A == 0, "table zebra row uses the zebra fill");
    zr = TableLayout.RowColours(0, false, true, true, true);
    Check(zr.Fill.Equals(Tok.Btn) && zr.Border.Equals(Tok.BorderHover), "table hover uses the ListView hover look");
    zr = TableLayout.RowColours(1, true, true, true, true);
    Check(zr.Fill.Equals(Tok.Selected) && zr.Border.Equals(Tok.Accent), "table selection wins over hover and zebra");
    zr = TableLayout.RowColours(1, false, true, false, false);
    Check(zr.Fill.Equals(Tok.TableRowBg), "table hover needs OnSelect (clickable)");
}
// ---------- No allocations while the cell texts stay the same ----------
{
    var texts = new[] { "KE", "120", "1.00", "0.85" };
    var sameContent = new[] { new string("KE".ToCharArray()), new string("120".ToCharArray()), new string("1.00".ToCharArray()), new string("0.85".ToCharArray()) };
    var cache = new TableTextCache();
    for (int i = 0; i < 10; i++) for (int c = 0; c < texts.Length; c++) cache.Set(c, texts[c]);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 1000; i++)
    {
        // Different string instances with the same contents: still no allocation and no re-measure.
        for (int c = 0; c < texts.Length; c++) { cache.Set(c, sameContent[c]); if (cache.Get(c).Length == 0) throw new Exception("unreachable"); }
    }
    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"INFO table text cache: {bytes} bytes over 1000 polls");
    Check(bytes == 0, "table cell text cache: 0 bytes over 1000 polls with equal texts");
    Check(cache.Set(0, "HEAT") && cache.Get(0) == "HEAT", "table cell text cache notices a changed text");
}
// ================= 0.1.7 =================
// ---------- Nice ticks: 1, 2 or 5 x 10^n ----------
{
    Check(Near(ChartMath.NiceStep(80), 20) && Near(ChartMath.NiceStep(1), .2) && Near(ChartMath.NiceStep(220), 50) && Near(ChartMath.NiceStep(.7f), .2) && Near(ChartMath.NiceStep(0), 1), "chart nice step: 1, 2 or 5 x 10^n");
    Check(ChartMath.AxisLabel("Angle", "deg") == "Angle (deg)" && ChartMath.AxisLabel("", "mm") == "mm" && ChartMath.AxisLabel("Depth", "") == "Depth", "chart axis caption carries the unit once");
    Check(ChartMath.FormatTick(20, 20) == "20" && ChartMath.FormatTick(.30000001f, .1f) == "0.3" && ChartMath.FormatTick(-0.000001f, .001f) == "0", "chart tick text has no trailing zeros and no -0");
}
// ---------- Automatic and fixed range ----------
{
    var cr = ChartMath.Range(0, 80, null, null, false);
    Check(Near(cr.Min, 0) && Near(cr.Max, 80) && Near(cr.Step, 20) && cr.Valid, "chart auto range 0..80 gets ticks of 20");
    cr = ChartMath.Range(3, 97, null, null, false);
    Check(Near(cr.Min, 0) && Near(cr.Max, 100) && Near(cr.Step, 20), "chart auto range snaps out to nice bounds");
    cr = ChartMath.Range(3, 97, 10, 50, false);
    Check(Near(cr.Min, 10) && Near(cr.Max, 50) && Near(cr.Step, 10), "chart fixed range is used exactly (10..50)");
    cr = ChartMath.Range(float.NaN, float.NaN, null, null, false);
    Check(Near(cr.Min, 0) && Near(cr.Max, 1) && cr.Valid, "chart empty range gets a sane 0..1 margin");
    cr = ChartMath.Range(5, 5, null, null, false);
    Check(cr.Min <= 4 && cr.Max >= 6 && cr.Valid, "chart flat range (one point) gets a margin");
    cr = ChartMath.Range(50, 80, null, null, true);
    Check(Near(cr.Min, 0) && cr.Max >= 80, "chart include-zero range keeps the baseline visible");
    Span<float> ticks = stackalloc float[8];
    var fixedRange = new ChartRange(0, 80, 20);
    int tn = ChartMath.Ticks(fixedRange, ticks);
    Check(tn == 5 && Near(ticks[0], 0) && Near(ticks[2], 40) && Near(ticks[4], 80), "chart ticks 0,20,40,60,80");
    var offsetRange = new ChartRange(10, 50, 10);
    tn = ChartMath.Ticks(offsetRange, ticks);
    Check(tn == 5 && Near(ticks[0], 10) && Near(ticks[4], 50), "chart ticks start at the first multiple of the step");
}
// ---------- Data to pixels and nearest x ----------
{
    var cr = new ChartRange(0, 100, 20);
    Check(Near(ChartMath.MapX(50, cr, 10, 200), 110) && Near(ChartMath.MapY(50, cr, 20, 100), 70), "chart data maps to pixels");
    Check(Near(ChartMath.MapX(0, cr, 10, 200), 10) && Near(ChartMath.MapY(100, cr, 20, 100), 20) && Near(ChartMath.MapY(0, cr, 20, 100), 120), "chart maps both corners (y down)");
    Check(float.IsNaN(ChartMath.MapX(float.NaN, cr, 0, 100)) && float.IsNaN(ChartMath.MapY(float.PositiveInfinity, cr, 0, 100)), "chart maps non-finite data to NaN");
    var pts = new[] { new ChartPoint(0, 0), new ChartPoint(10, 5), new ChartPoint(20, 2) };
    Check(ChartMath.NearestPointIndex(pts, 12) == 1 && ChartMath.NearestPointIndex(pts, 18) == 2 && ChartMath.NearestPointIndex(pts, -5) == 0 && ChartMath.NearestPointIndex(pts, 100) == 2, "chart hover picks the nearest x");
    var withNan = new[] { new ChartPoint(0, 0), new ChartPoint(float.NaN, 9), new ChartPoint(30, 1) };
    Check(ChartMath.NearestPointIndex(withNan, 10) == 0 && ChartMath.NearestPointIndex(withNan, 25) == 2, "chart hover ignores NaN points");
    Check(ChartMath.NearestIndex(new float[] { 0, 10, 20 }, 11) == 1 && ChartMath.NearestIndex(Array.Empty<float>(), 1) == -1, "chart nearest x over plain values");
}
// ---------- Step polyline and NaN gap ----------
{
    var pts = new[] { new ChartPoint(0, 0), new ChartPoint(10, 5), new ChartPoint(20, 2) };
    var dst = new ChartPoint[16];
    int n = ChartMath.BuildStep(pts, dst);
    Check(n == 5 && Near(dst[0].X, 0) && Near(dst[1].X, 10) && Near(dst[1].Y, 0) && Near(dst[2].X, 10) && Near(dst[2].Y, 5) && Near(dst[3].X, 20) && Near(dst[3].Y, 5) && Near(dst[4].Y, 2), "chart step polyline: horizontal then vertical");
    var gap = new[] { new ChartPoint(0, 0), new ChartPoint(5, float.NaN), new ChartPoint(10, 3) };
    n = ChartMath.BuildStep(gap, dst);
    Check(n == 2 && Near(dst[0].X, 0) && Near(dst[1].X, 10) && Near(dst[1].Y, 3), "chart step splits at a NaN gap");
    n = ChartMath.BuildStep(new[] { new ChartPoint(4, 7) }, dst);
    Check(n == 1 && Near(dst[0].X, 4) && Near(dst[0].Y, 7), "chart step keeps a single point as a dot");
}
// ---------- Waterfall geometry, including stopping at 0 ----------
{
    var steps = new WaterfallStep[8];
    int n = ChartMath.Waterfall(100, new[] { -30f, -20f, -60f }, steps);
    Check(n == 5, "chart waterfall has a start bar, layers and an end bar");
    Check(Near(steps[0].From, 0) && Near(steps[0].To, 100), "chart waterfall start bar runs 0..100");
    Check(Near(steps[1].From, 100) && Near(steps[1].To, 70) && !steps[1].Stopped, "chart waterfall first layer floats 100 -> 70");
    Check(Near(steps[2].From, 70) && Near(steps[2].To, 50), "chart waterfall second layer floats 70 -> 50");
    Check(Near(steps[3].From, 50) && Near(steps[3].To, 0) && steps[3].Stopped, "chart waterfall stops at 0 and marks the layer");
    Check(Near(steps[4].From, 0) && Near(steps[4].To, 0), "chart waterfall end bar is the final remainder");
    n = ChartMath.Waterfall(100, new[] { -30f, 10f }, steps);
    Check(n == 4 && Near(steps[2].From, 70) && Near(steps[2].To, 80) && Near(steps[3].To, 80) && !steps[2].Stopped, "chart waterfall accepts a positive layer");
    n = ChartMath.Waterfall(float.NaN, new[] { float.NaN }, steps);
    Check(n == 3 && Near(steps[0].To, 0) && Near(steps[1].To, 0) && Near(steps[2].To, 0), "chart waterfall ignores non-finite input");
}
// ---------- Bar slots ----------
{
    var plot = new RectF(0, 0, 100, 50);
    var b0 = ChartMath.BarRect(plot, 0, 4, 4);
    var b3 = ChartMath.BarRect(plot, 3, 4, 4);
    Check(Near(b0.W, 21) && Near(b3.X, 77) && ChartMath.BarAt(plot, 4, b0.X + 1) == 0 && ChartMath.BarAt(plot, 4, b3.X + 1) == 3 && ChartMath.BarAt(plot, 4, 100) == -1, "chart bar slots and hover index");
}
// ---------- NaN gap and one-point dot in the raster ----------
{
    var nanRaster = new Raster(); nanRaster.Resize(120, 50); nanRaster.Clear(0);
    var pts = new[] { new ChartPoint(0, 5), new ChartPoint(5, 5), new ChartPoint(8, float.NaN), new ChartPoint(15, 5), new ChartPoint(20, 5) };
    ChartRender.Series(nanRaster, 1, new RectF(10, 0, 100, 50), new ChartRange(0, 20, 5), new ChartRange(0, 10, 2), pts, SeriesStyle.Line, Tok.Accent);
    uint left = nanRaster.Pixels[25 * 120 + 12], middle = nanRaster.Pixels[25 * 120 + 55], right = nanRaster.Pixels[25 * 120 + 95];
    Check((left >> 24) > 0 && (middle >> 24) == 0 && (right >> 24) > 0, "chart NaN leaves a real gap in the line");
    nanRaster.Clear(0);
    ChartRender.Series(nanRaster, 1, new RectF(10, 0, 100, 50), new ChartRange(0, 20, 5), new ChartRange(0, 10, 2), new[] { new ChartPoint(10, 5) }, SeriesStyle.Line, Tok.Accent);
    Check((nanRaster.Pixels[25 * 120 + 60] >> 24) > 0, "chart one point becomes a dot");
}
// ---------- 0 bytes over 1000 polls with equal data ----------
{
    var data = new ChartPoint[4][];
    for (int s = 0; s < 4; s++)
    {
        data[s] = new ChartPoint[81];
        for (int i = 0; i < 81; i++) data[s][i] = new ChartPoint(i, MathF.Sin(i * .2f) + s);
    }
    var prints = new ChartFingerprint[4];
    for (int s = 0; s < 4; s++) prints[s].Changed(data[s], data[s]);
    long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    bool changed = false;
    for (int poll = 0; poll < 1000; poll++)
        for (int s = 0; s < 4; s++) changed |= prints[s].Changed(data[s], data[s]);
    long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    Console.WriteLine($"INFO chart fingerprint: {bytes} bytes over 1000 polls of 4 x 81 points");
    Check(bytes == 0 && !changed, "chart data fingerprint: 0 bytes and no redraw over 1000 equal polls");
    data[0][10] = new ChartPoint(10, 99);
    Check(prints[0].Changed(data[0], data[0]), "chart data fingerprint notices a changed value");
    var copy = data[1].ToArray();
    Check(prints[1].Changed(copy, copy), "chart data fingerprint notices a replaced list");
}
// ---------- One full LineChart raster pass: 600 x 240, 4 x 81 points, scale 1 and 1440p ----------
{
    var series = new ChartPoint[4][];
    for (int s = 0; s < 4; s++)
    {
        series[s] = new ChartPoint[81];
        for (int i = 0; i < 81; i++)
        {
            float x = i * 80f / 80f;
            float y = 40f + s * 18f + MathF.Sin(i * .11f + s) * 14f;
            series[s][i] = new ChartPoint(x, y);
        }
    }
    float[] xt = { 0, 20, 40, 60, 80 };
    float[] yt = { 0, 20, 40, 60, 80, 100 };
    var xr = new ChartRange(0, 80, 20);
    var yr = new ChartRange(0, 100, 20);
    for (int pass = 0; pass < 2; pass++)
    {
        float scale = pass == 0 ? 1f : 1440f / 1080f;
        int w = (int)MathF.Ceiling(600 * scale), h = (int)MathF.Ceiling(240 * scale);
        var chartRaster = new Raster(); chartRaster.Resize(w, h);
        var plot = new RectF(48, 24, 600 - 58, 240 - 60);
        var chartSw = System.Diagnostics.Stopwatch.StartNew();
        var chartRuns = new List<double>();
        for (int run = 0; run < 9; run++)
        {
            chartSw.Restart();
            chartRaster.Clear(0);
            ChartRender.Grid(chartRaster, scale, plot, xr, yr, xt, yt);
            for (int s = 0; s < 4; s++) ChartRender.Series(chartRaster, scale, plot, xr, yr, series[s], SeriesStyle.Line, Tok.ChartSeries(s));
            ChartRender.RefLine(chartRaster, scale, plot, yr, 90, Tok.ChartRef);
            for (int s = 0; s < 4; s++) ChartRender.Chip(chartRaster, scale, 400 + s * 45, 4, 40, 16, Tok.ChartChip, Tok.Divider);
            if (run > 1) chartRuns.Add(chartSw.Elapsed.TotalMilliseconds);
        }
        chartRuns.Sort();
        double chartMedian = chartRuns[chartRuns.Count / 2];
        Console.WriteLine($"INFO LineChart 600x240, 4 x 81 points, scale {scale:0.###} ({w}x{h} px): median {chartMedian:0.00} ms (runs {string.Join(", ", chartRuns.Select(t => t.ToString("0.00")))})");
        Check(chartMedian < 100, "full LineChart raster pass fits the frame budget at scale " + scale.ToString("0.###"));
    }
}
// ================= 0.1.8 =================
// ---------- RadialProgress: clamp, NaN and infinity ----------
{
    Check(RadialMath.Clamp(-.5f) == 0 && RadialMath.Clamp(.25f) == .25f && RadialMath.Clamp(1.5f) == 1, "radial value clamps to 0..1");
    Check(RadialMath.Clamp(float.NaN) == 0 && RadialMath.Clamp(float.PositiveInfinity) == 0 && RadialMath.Clamp(float.NegativeInfinity) == 0, "radial NaN and infinity count as 0");
    var np = new RadialPoll();
    Check(np.Poll(.7f, null) == RadialChange.Value && np.Value == .7f, "radial first poll takes the value");
    Check(np.Poll(float.NaN, null) == RadialChange.Value && np.Value == 0, "radial NaN resets the fill to 0");
    Check(np.Poll(.4f, null) == RadialChange.Value && np.Poll(float.PositiveInfinity, null) == RadialChange.Value && np.Value == 0, "radial infinity resets the fill to 0");
}
// ---------- The fillAmount threshold (1/720) ----------
{
    Check(RadialMath.Perceptible(0, RadialMath.FillStep) && !RadialMath.Perceptible(0, RadialMath.FillStep * .99f), "radial threshold accepts 1/720, rejects less");
    var rp = new RadialPoll();
    Check(rp.Poll(.25f, "3.1") == (RadialChange.Value | RadialChange.Text), "radial first poll reports value and text");
    Check(rp.Poll(.2505f, "3.1") == RadialChange.None && rp.Value == .25f, "radial fill ignores a change below 1/720");
    var exact = new RadialPoll(); exact.Poll(0f, null);
    Check(exact.Poll(RadialMath.FillStep, null) == RadialChange.Value, "radial fill follows a change at 1/720");
    Check(rp.Poll(.9f, "3.1") == RadialChange.Value && rp.Value == .9f, "radial fill follows a visible change");
}
// ---------- Default thickness and centre type size ----------
{
    Check(RadialMath.DefaultThickness(48) == 6 && RadialMath.DefaultThickness(100) == 12, "radial default thickness is 12% of the diameter");
    Check(RadialMath.DefaultThickness(24) == 3 && RadialMath.DefaultThickness(10) == 3 && RadialMath.DefaultThickness(float.NaN) == 3, "radial default thickness is at least 3 px");
    Check(RadialMath.CentreFontSize(48) == 13 && RadialMath.CentreFontSize(32) == 9 && RadialMath.CentreFontSize(16) == 8 && RadialMath.CentreFontSize(200) == Tok.FontValue, "radial centre text stays at TextStyle.Value or smaller");
}
// ---------- Ring sprite cache key (diameter + thickness in device px) ----------
{
    var k1 = RadialMath.SpriteKey(48, 6, 1);
    var k2 = RadialMath.SpriteKey(48, 6, 1);
    Check(k1.Equals(k2) && k1.GetHashCode() == k2.GetHashCode() && k1.DiameterPx == 48 && k1.ThicknessPx == 6, "radial ring key: same size and thickness -> same key");
    Check(!k1.Equals(RadialMath.SpriteKey(48, 7, 1)) && !k1.Equals(RadialMath.SpriteKey(49, 6, 1)), "radial ring key: another diameter or thickness -> another key");
    var k3 = RadialMath.SpriteKey(48, 6, 2);
    Check(k3.DiameterPx == 96 && k3.ThicknessPx == 12, "radial ring key works in device pixels at scale 2");
    Check(RadialMath.SpriteKey(48.2f, 6.1f, 1.5f).DiameterPx == 73, "radial ring key rounds the device diameter up");
    Console.WriteLine($"INFO radial ring key {k1}");
}
// ---------- The ring mask matches Assets.Circle's anti-aliasing ----------
{
    var ringMask = ShapeRaster.Circle(48, 6);
    Check(ringMask[24 * 48 + 24] == 0, "radial ring: hollow centre");
    Check(ringMask[24 * 48 + 44] > .99f && ringMask[24 * 48 + 47] < .6f, "radial ring: 6 px band, anti-aliased outer edge");
}
// ---------- 0 bytes over 1000 polls with an equal value and equal text ----------
{
    var state = new RadialPoll();
    string same = "3.1";
    state.Poll(.42f, same);
    long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var changed = RadialChange.None;
    for (int i = 0; i < 1000; i++) changed |= state.Poll(.42f, same);
    long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    Console.WriteLine($"INFO radial poll: {bytes} bytes over 1000 polls");
    Check(bytes == 0 && changed == RadialChange.None && ReferenceEquals(state.Text, same), "radial poll: 0 bytes over 1000 equal value/text polls");
    Check(state.Poll(.42f, "3.2") == RadialChange.Text && ReferenceEquals(state.Text, "3.2"), "radial poll notices a changed centre text");
}
GraphTests.Run(Check);
GraphRenderingTests.Run(Check);
FocusGraph0111Tests.Run(Check);
Focus0112Tests.Run(Check);
Console.WriteLine($"ALL PASSED: {passed}");


