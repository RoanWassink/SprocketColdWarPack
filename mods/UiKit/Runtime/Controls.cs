using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Corners = SprocketUiKit.ShapeRaster.Corners;
namespace SprocketUiKit;

/// <summary>Text button: primary / normal / quiet / danger, disabled via EnabledWhen.</summary>
public sealed class Button : Element
{
    private string text;
    private Func<string>? textGetter;
    private readonly Action onClick;
    private ButtonStyle style;
    private Func<ButtonStyle>? styleGetter;
    private bool compact;
    private Icon icon;
    private BoxView? box; private TextMeshProUGUI? label; private Image? iconImg;
    internal Button(string text, Action onClick, ButtonStyle style) { this.text = text; this.onClick = onClick; this.style = style; }
    /// <summary>28 px high, 12 px text ("Rename").</summary>
    public Button Compact() { compact = true; MarkDirty(); return this; }
    /// <summary>Leading icon (e.g. Icon.Plus for "Add").</summary>
    public Button WithIcon(Icon i) { icon = i; MarkDirty(); return this; }
    public Button Text(Func<string> getter) { textGetter = getter; return this; }
    public Button Style(Func<ButtonStyle> getter) { styleGetter = getter; return this; }
    public string Label { get => text; set { text = value ?? ""; Draw.SetText(label, text); MarkDirty(); } }
    private float Pad => compact ? Tok.ButtonCompactPadX : Tok.ButtonPadX;
    protected override void Build()
    {
        box = new BoxView(Rt!, "Box", Tok.Radius); Draw.Stretch(box.Rt);
        if (icon != Icon.None) iconImg = Draw.Child(Rt!, "Icon", Assets.IconSprite(icon), Tok.Text);
        label = Draw.Text(Draw.Node("Label", Rt!), TextStyle.Body, text);
        label.fontSize = compact ? Tok.FontSmall : Tok.FontLabel;
        label.alignment = TextAlignmentOptions.Midline;
    }
    internal override void Unbuild() { box = null; label = null; iconImg = null; base.Unbuild(); }
    private float IconW => icon == Icon.None ? 0 : 14 + 6;
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : (label != null ? Draw.Width(label, text) : text.Length * 8) + IconW + 2 * Pad + 2;
    internal override float MeasureHeight(float width) => compact ? Tok.ButtonCompactH : Tok.ButtonH;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        if (label == null) return;
        float tw = MathF.Min(Draw.Width(label, text) + 2, r.W - 2 * Pad - IconW);
        float x0 = (r.W - tw - IconW) / 2;
        if (iconImg != null) Draw.Place(iconImg.rectTransform, x0, (r.H - 14) / 2, 14, 14);
        Draw.Place(label.rectTransform, x0 + IconW, 0, MathF.Max(0, tw), r.H);
    }
    internal override bool Interactive => true;
    internal override void Refresh(float now)
    {
        if (textGetter != null) { var t = Guard.Get(Where + ".Text", textGetter, text) ?? ""; if (t != text) { text = t; Draw.SetText(label, t); MarkDirty(); } }
        var st = styleGetter != null ? Guard.Get(Where + ".Style", styleGetter, style) : style;
        var (fill, border, ink) = Look.Button(st, Enabled, Hovered, Pressed);
        box?.Set(fill, border);
        Draw.SetColor(label, ink); Draw.SetColor(iconImg, ink);
        if (label != null)
        {
            var want = st == ButtonStyle.Primary ? FontStyles.Bold : FontStyles.Normal;
            if (label.fontStyle != want) label.fontStyle = want;
        }
    }
    internal override void OnClick(Vec p) => Guard.Run(Where + " '" + text + "' click", onClick);
}

/// <summary>Square icon button (close, delete, add); <see cref="Round"/> for the "?" help style.</summary>
public sealed class IconButton : Element
{
    private Icon icon; private readonly Action onClick; private bool round;
    private BoxView? box; private Image? img;
    internal IconButton(Icon icon, Action onClick) { this.icon = icon; this.onClick = onClick; round = icon == Icon.Help; }
    /// <summary>Swap the icon (title bar maximize/restore, 0.1.4).</summary>
    internal void SetIcon(Icon i) { if (i == icon) return; icon = i; if (img != null) img.sprite = Assets.IconSprite(i); }
    public IconButton Round() { round = true; MarkDirty(); return this; }
    private float Size => round ? 24 : Tok.IconButton;
    protected override void Build()
    {
        box = new BoxView(Rt!, "Box", round ? 12 : Tok.Radius); Draw.Stretch(box.Rt);
        img = Draw.Child(Rt!, "Icon", Assets.IconSprite(icon), Tok.Text);
    }
    internal override void Unbuild() { box = null; img = null; base.Unbuild(); }
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : Size;
    internal override float MeasureHeight(float width) => Size;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        float s = round ? 14 : 14;
        if (img != null) Draw.Place(img.rectTransform, (r.W - s) / 2, (r.H - s) / 2, s, s);
    }
    internal override bool Interactive => true;
    internal override void Refresh(float now)
    {
        bool h = Hovered && Enabled, p = Pressed && Enabled;
        if (round) box?.Set(h ? Tok.BtnHover : Tok.Btn, h ? Tok.BorderHover : Tok.Border);
        else box?.Set(p ? Tok.BtnPressed : h ? Tok.Btn : Rgba.Clear, h || p ? Tok.Border : Rgba.Clear);
        var ink = !Enabled ? Tok.DisabledText : round ? Tok.Heading : icon == Icon.Trash ? (h ? Tok.Error : Tok.Text2) : Tok.Text;
        Draw.SetColor(img, ink);
    }
    internal override void OnClick(Vec p) => Guard.Run(Where + " " + icon + " click", onClick);
}

/// <summary>Equal-width segmented choice ("Open | Close | Toggle | Pulse | Follow"); the selected one is primary.</summary>
public sealed class SegmentedButtons : Element
{
    private readonly IReadOnlyList<string> options; private readonly Func<int> selected; private readonly Action<int> select;
    private readonly List<(BoxView Box, TextMeshProUGUI Text)> parts = new();
    private int hoverPart = -1;
    internal SegmentedButtons(IReadOnlyList<string> options, Func<int> selected, Action<int> select) { this.options = options; this.selected = selected; this.select = select; }
    protected override void Build()
    {
        parts.Clear();
        for (int i = 0; i < options.Count; i++)
        {
            var corners = options.Count == 1 ? Corners.All : i == 0 ? Corners.Left : i == options.Count - 1 ? Corners.Right : Corners.None;
            var b = new BoxView(Rt!, "Segment", Tok.Radius, corners);
            var t = Draw.Text(Draw.Node("Label", b.Rt), TextStyle.Body, options[i]);
            t.fontSize = Tok.FontValue; t.alignment = TextAlignmentOptions.Midline; Draw.Stretch(t.rectTransform, 4, 0, 4, 0);
            parts.Add((b, t));
        }
    }
    internal override void Unbuild() { parts.Clear(); base.Unbuild(); }
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : -FlexWeight;
    internal override float MeasureHeight(float width) => Tok.FieldH;
    private float[] Xs(float w) { var s = new float[options.Count + 1]; for (int i = 0; i <= options.Count; i++) s[i] = MathF.Round(w * i / Math.Max(1, options.Count)); return s; }
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        var xs = Xs(r.W);
        for (int i = 0; i < parts.Count; i++) parts[i].Box.Place(xs[i] - (i > 0 ? 1 : 0), 0, xs[i + 1] - xs[i] + (i > 0 ? 1 : 0), r.H);
    }
    private int PartAt(Vec p) { var xs = Xs(Rect.W); for (int i = 0; i < options.Count; i++) if (p.X - Rect.X >= xs[i] && p.X - Rect.X < xs[i + 1]) return i; return -1; }
    internal override bool Interactive => true;
    internal override void Refresh(float now)
    {
        int sel = Guard.Get(Site(0, ".Selected"), selected, -1);
        hoverPart = Hovered ? PartAt(Panel.LocalPointer) : -1;
        for (int i = 0; i < parts.Count; i++)
        {
            var (fill, border, ink) = Look.Button(i == sel ? ButtonStyle.Primary : ButtonStyle.Normal, Enabled, hoverPart == i, Pressed && hoverPart == i);
            parts[i].Box.Set(fill, border); Draw.SetColor(parts[i].Text, ink);
            var want = i == sel ? FontStyles.Bold : FontStyles.Normal;
            if (parts[i].Text.fontStyle != want) parts[i].Text.fontStyle = want;
            // keep the selected/hovered segment's border on top of its neighbours
            if (i == sel || i == hoverPart) parts[i].Box.Rt.SetAsLastSibling();
        }
    }
    internal override void OnClick(Vec p) { int i = PartAt(p); if (i >= 0) Guard.Run(Where + " select", () => select(i)); }
}

/// <summary>Number stepper: [−][value][+], snapped to step, clamped to min/max, hold to repeat.</summary>
public sealed class Stepper : Element
{
    private readonly Func<double> value; private readonly Action<double> set;
    private readonly double min, max, step; private readonly string? unit;
    private Func<double, string>? format;
    private bool compact; private float valueW = Tok.StepperValueW;
    private BoxView? minus, plus, field; private Image? minusIcon, plusIcon; private TextMeshProUGUI? text;
    private float nextRepeat; private int repeatPart;
    internal Stepper(Func<double> value, Action<double> set, double min, double max, double step, string? unit)
    { this.value = value; this.set = set; this.min = min; this.max = max; this.step = step; this.unit = unit; }
    /// <summary>28×32 buttons and a 52 px value (inline rule rows).</summary>
    public Stepper Compact() { compact = true; valueW = 52; MarkDirty(); return this; }
    public Stepper ValueWidth(float w) { valueW = w; MarkDirty(); return this; }
    public Stepper Format(Func<double, string> f) { format = f; return this; }
    private double fastStep = double.NaN, ctrlStep = double.NaN;
    /// <summary>Shift+click/hold uses <paramref name="shiftStep"/>, Ctrl+click/hold <paramref name="ctrlStep"/> (0.1.2).</summary>
    public Stepper FastStep(double shiftStep, double? ctrlStep = null) { fastStep = shiftStep; this.ctrlStep = ctrlStep ?? double.NaN; return this; }
    private float Btn => compact ? 28 : Tok.StepperBtn;
    protected override void Build()
    {
        field = new BoxView(Rt!, "Value", 0, Corners.None);
        minus = new BoxView(Rt!, "Minus", Tok.Radius, Corners.Left);
        plus = new BoxView(Rt!, "Plus", Tok.Radius, Corners.Right);
        minusIcon = Draw.Child(minus.Rt, "Icon", Assets.IconSprite(Icon.Minus), Tok.Text);
        plusIcon = Draw.Child(plus.Rt, "Icon", Assets.IconSprite(Icon.Plus), Tok.Text);
        text = Draw.Text(Draw.Node("Text", field.Rt), TextStyle.Body); text.alignment = TextAlignmentOptions.Midline; Draw.Stretch(text.rectTransform, 2, 0, 2, 0);
    }
    internal override void Unbuild() { minus = plus = field = null; minusIcon = plusIcon = null; text = null; base.Unbuild(); }
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : 2 * Btn + valueW;
    internal override float MeasureHeight(float width) => 32;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        float vw = MathF.Max(20, r.W - 2 * Btn);
        minus?.Place(0, 0, Btn, r.H); plus?.Place(Btn + vw, 0, Btn, r.H);
        field?.Place(Btn - 1, 1, vw + 2, r.H - 2);
        if (minusIcon != null) Draw.Place(minusIcon.rectTransform, (Btn - 12) / 2, (r.H - 12) / 2, 12, 12);
        if (plusIcon != null) Draw.Place(plusIcon.rectTransform, (Btn - 12) / 2, (r.H - 12) / 2, 12, 12);
    }
    private int PartAt(Vec p) { float x = p.X - Rect.X; return x < Btn ? -1 : x >= Rect.W - Btn ? 1 : 0; }
    internal override bool Interactive => true;
    internal override void Refresh(float now)
    {
        double v = Guard.Get(Where + ".Value", value, double.NaN);
        string shown = double.IsNaN(v) ? "-" : format != null ? Guard.Get(Where + ".Format", () => format(v), "?") : KitMath.FormatValue(v, step, unit);
        Draw.SetText(text, shown);
        int part = Hovered ? PartAt(Panel.LocalPointer) : 0;
        bool canDown = Enabled && !(v <= min), canUp = Enabled && !(v >= max);
        var (mf, mb, mi) = Look.Button(ButtonStyle.Normal, canDown, part < 0, Pressed && part < 0);
        var (pf, pb, pi) = Look.Button(ButtonStyle.Normal, canUp, part > 0, Pressed && part > 0);
        minus?.Set(mf, mb); plus?.Set(pf, pb); Draw.SetColor(minusIcon, mi); Draw.SetColor(plusIcon, pi);
        field?.Set(Tok.Panel, Tok.Border); Draw.SetColor(text, !Enabled ? Tok.DisabledText : HasError ? Tok.ErrorText : Tok.Text);
        if (part < 0 && Pressed) minus?.Rt.SetAsLastSibling(); else if (part > 0 && Pressed) plus?.Rt.SetAsLastSibling();
        // hold to repeat
        if (Pressed && repeatPart != 0 && now >= nextRepeat && part == repeatPart) { nextRepeat = now + KeyRepeat.StepRate; Apply(repeatPart); }
        if (!Pressed) repeatPart = 0;
    }
    internal override void OnPress(Vec p)
    {
        int part = PartAt(p); if (part == 0) return;
        repeatPart = part; nextRepeat = Kit.Now + KeyRepeat.Delay; Apply(part);
    }
    private void Apply(int dir)
    {
        double v = Guard.Get(Where + ".Value", value, double.NaN);
        if (double.IsNaN(v)) return;
        double s = Kit.CtrlHeld && ctrlStep > 0 ? ctrlStep : Kit.ShiftHeld && fastStep > 0 ? fastStep : step;
        double next = KitMath.Step(v, s, dir, min, max);
        if (s != step) next = KitMath.Clean(next, step);
        if (next != v) Guard.Run(Where + " set", () => set(next));
    }
}

/// <summary>On/off switch (44×24), animated knob.</summary>
public sealed class Toggle : Element
{
    private static readonly Rgba HoverOff = Rgba.Hex("#4a4840");
    private readonly Func<bool> value; private readonly Action<bool> set;
    private BoxView? track; private Image? knob; private float pos = -1;
    internal Toggle(Func<bool> value, Action<bool> set) { this.value = value; this.set = set; }
    protected override void Build()
    {
        track = new BoxView(Rt!, "Track", 12, Corners.All, ring: false);
        knob = Draw.Child(track.Rt, "Knob", Assets.Circle(), Tok.Ink);
        pos = -1;
    }
    internal override void Unbuild() { track = null; knob = null; base.Unbuild(); }
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : Tok.ToggleW;
    internal override float MeasureHeight(float width) => Tok.ToggleH;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        track?.Place(0, 0, Tok.ToggleW, Tok.ToggleH);
    }
    internal override bool Interactive => true;
    internal override void Refresh(float now)
    {
        bool on = Guard.Get(Site(0, ".Value"), value, false);
        float target = on ? 1 : 0;
        pos = pos < 0 ? target : MathF.Abs(pos - target) < .01f ? target : pos + (target - pos) * MathF.Min(1, Time.unscaledDeltaTime * 14);
        var fill = !Enabled ? Tok.DisabledBg : on ? (Hovered ? Tok.AccentHover : Tok.Accent) : (Hovered ? HoverOff : Tok.OffTrack);
        track?.Set(fill, Rgba.Clear);
        Draw.SetColor(knob, on ? Tok.Ink : Enabled ? Tok.Text2 : Tok.DisabledText);
        if (knob != null) Draw.Place(knob.rectTransform, 3 + pos * (Tok.ToggleW - Tok.Knob - 6), 3, Tok.Knob, Tok.Knob);
    }
    internal override void OnClick(Vec p) { bool on = Guard.Get(Site(0, ".Value"), value, false); Guard.Run(Where + " set", () => set(!on)); }
}

/// <summary>
/// Dropdown with static or dynamic options (optionally grouped). The list opens on the kit's own
/// overlay layer above every panel, is never clipped by the column or window, and opens upwards
/// when there is no room below. <see cref="Inline"/> sizes it to its text for use inside a Row.
/// </summary>
public sealed class Dropdown : Element
{
    private readonly Func<IReadOnlyList<Choice>> choices;
    private readonly Func<int> selected; private readonly Action<int> select;
    private bool inline, accent; private Func<bool>? accentWhen;
    private string placeholder = "Choose...";
    private BoxView? box; private TextMeshProUGUI? caption; private Image? chevron;
    private string shown = "";
    // 0.1.4 (#18): the Choice list is cached per returned list instance and contents (no per-frame allocation)
    internal Dropdown(Func<IReadOnlyList<string>> options, Func<int> selected, Action<int> select)
        : this(new ChoiceCache(options).Get, selected, select, true) { }
    internal Dropdown(Func<IReadOnlyList<Choice>> choices, Func<int> selected, Action<int> select, bool _)
    { this.choices = choices; this.selected = selected; this.select = select; }
    /// <summary>Size to the current text (for inline rule rows); 32 px high.</summary>
    public Dropdown Inline() { inline = true; MarkDirty(); return this; }
    /// <summary>Accent look (command field: #3a3220 / #a57a2c / bold).</summary>
    public Dropdown Accent(Func<bool>? when = null) { accent = when == null; accentWhen = when; return this; }
    public Dropdown Placeholder(string text) { placeholder = text; return this; }
    internal IReadOnlyList<Choice> Current() => Guard.Get(Site(0, ".Options"), choices, (IReadOnlyList<Choice>)Array.Empty<Choice>()) ?? Array.Empty<Choice>();
    internal int SelectedIndex() => Guard.Get(Site(1, ".Selected"), selected, -1);
    internal void Choose(int index) => Guard.Run(Where + " select", () => select(index));
    internal bool IsOpen => Kit.Popup?.Owner == this;
    protected override void Build()
    {
        box = new BoxView(Rt!, "Box", Tok.Radius); Draw.Stretch(box.Rt);
        caption = Draw.Text(Draw.Node("Caption", Rt!), TextStyle.Body);
        chevron = Draw.Child(Rt!, "Chevron", Assets.IconSprite(Icon.ChevronDown), Tok.Heading);
    }
    internal override void Unbuild() { box = null; caption = null; chevron = null; base.Unbuild(); }
    private float Chev => inline ? 10 : 12;
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : inline ? (caption != null ? Draw.Width(caption, shown) : shown.Length * 8) + 10 + 8 + Chev + 10 + 2 : -FlexWeight;
    internal override float MeasureHeight(float width) => inline ? 32 : Tok.FieldH;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        if (caption != null) Draw.Place(caption.rectTransform, 10, 0, MathF.Max(0, r.W - 20 - Chev - 8), r.H);
        if (chevron != null) Draw.Place(chevron.rectTransform, r.W - 10 - Chev, (r.H - Chev) / 2, Chev, Chev);
    }
    internal override bool Interactive => true;
    internal override void Refresh(float now)
    {
        var list = Current(); int i = SelectedIndex();
        string t = i >= 0 && i < list.Count ? list[i].Label : placeholder;
        if (t != shown) { shown = t; Draw.SetText(caption, t); if (inline) MarkDirty(); }
        bool acc = accent || accentWhen != null && Guard.Get(Site(2, ".Accent"), accentWhen, false);
        bool open = IsOpen;
        Rgba fill, border, ink;
        if (!Enabled) (fill, border, ink) = Look.Button(ButtonStyle.Normal, false, false, false);
        else if (acc) { fill = Tok.Selected; border = open ? Tok.Accent : Hovered ? Tok.AccentBorder : Tok.AccentLine; ink = Tok.AccentText; }
        else { fill = Hovered && !open ? Tok.BtnHover : Tok.Btn; border = open ? Tok.Accent : Hovered ? Tok.BorderHover : Tok.Border; ink = i >= 0 && i < list.Count ? Tok.Text : Tok.Text2; }
        box?.Set(fill, border); Draw.SetColor(caption, ink);
        if (caption != null) { var fs = acc ? FontStyles.Bold : FontStyles.Normal; if (caption.fontStyle != fs) caption.fontStyle = fs; }
        if (chevron != null)
        {
            var sprite = Assets.IconSprite(open && Kit.Popup!.Upward ? Icon.ChevronDown : open ? Icon.ChevronUp : Icon.ChevronDown);
            if (chevron.sprite != sprite) chevron.sprite = sprite;
        }
    }
    internal override void OnClick(Vec p)
    {
        if (IsOpen) { Kit.ClosePopup(); return; }
        Kit.OpenPopup(new PopupList(this, Panel.ToScreen(Rect)));
    }
}

/// <summary>The open dropdown list, drawn on the kit overlay (screen kit units).</summary>
internal sealed class PopupList
{
    internal readonly Dropdown Owner;
    internal RectF Anchor, Rect;
    internal bool Upward;
    private List<(bool Heading, string Label, int Index)> rows = new();
    private RectTransform? root; private BoxView? box; private RectTransform? content; private ScrollBar? bar;
    private readonly List<(BoxView Bg, TextMeshProUGUI Text)> views = new();
    private float scroll, contentH;
    private int hover = -1, count = -1;
    private bool barPress;
    private const float Pad = 4, HeadingH = 26;
    internal PopupList(Dropdown owner, RectF anchor) { Owner = owner; Anchor = anchor; }
    private float RowH(int i) => rows[i].Heading ? HeadingH : Tok.PopupItemH;
    internal void Build(RectTransform overlay)
    {
        root = Draw.Node("Popup", overlay);
        var shadow = Draw.Child(root, "Shadow", Assets.Shadow(out _), Tok.Shadow.WithAlpha(.5f), sliced: true);
        shadow.pixelsPerUnitMultiplier = 1;
        Draw.Stretch(shadow.rectTransform, -14, -6, -14, -22);
        box = new BoxView(root, "Box", Tok.Radius, raycast: true); Draw.Stretch(box.Rt); box.Set(Tok.HeadFoot, Tok.Accent);
        var view = Draw.Node("Viewport", root); Draw.Stretch(view, 1, Pad, 1, Pad); view.gameObject.AddComponent<RectMask2D>();
        content = Draw.Node("Content", view);
        bar = new ScrollBar(() => scroll, v => scroll = v);
        bar.Build(root);
        Rebuild();
    }
    private void Rebuild()
    {
        rows = Look.PopupRows(Owner.Current());
        count = Owner.Current().Count;
        foreach (var v in views) UnityEngine.Object.Destroy(v.Bg.Rt.gameObject);
        views.Clear();
        float w = 160;
        foreach (var r in rows)
        {
            var bg = new BoxView(content!, r.Heading ? "Heading" : "Item", 0, Corners.None, ring: false);
            var t = Draw.Text(Draw.Node("Text", bg.Rt), r.Heading ? TextStyle.BlockTitle : TextStyle.Body, r.Label);
            if (r.Heading) t.color = Draw.C(Tok.Faint);
            views.Add((bg, t));
            w = MathF.Max(w, Draw.Width(t, r.Label) + 24 + 12);
        }
        contentH = rows.Select((_, i) => RowH(i)).Sum();
        float width = Math.Clamp(MathF.Max(Anchor.W, w), 120, 480);
        float h = MathF.Min(contentH, Tok.PopupMaxItems * Tok.PopupItemH + HeadingH) + 2 * Pad;
        Rect = KitMath.PlacePopup(Anchor, width, h, Kit.ViewW, Kit.ViewH);
        Upward = Rect.Y < Anchor.Y;
        Draw.Place(root!, Rect.X, Rect.Y, Rect.W, Rect.H);
        // start with the selected option in view
        int sel = Owner.SelectedIndex(); float y = 0;
        for (int i = 0; i < rows.Count; i++) { if (rows[i].Index == sel) { scroll = KitMath.ClampScroll(y - (Rect.H - 2 * Pad) / 2, contentH, Rect.H - 2 * Pad); break; } y += RowH(i); }
        Layout();
    }
    private void Layout()
    {
        float viewH = Rect.H - 2 * Pad, y = -scroll;
        for (int i = 0; i < views.Count; i++)
        {
            float h = RowH(i);
            views[i].Bg.Place(0, y, Rect.W - 2, h);
            Draw.Place(views[i].Text.rectTransform, 12, 0, Rect.W - 2 - 24, h);
            y += h;
        }
        if (bar != null) bar.Layout(new RectF(1, Pad, Rect.W - 1, viewH), new Vec(Rect.X, Rect.Y), contentH, scroll, 3);
    }
    internal bool Contains(Vec p) => Rect.Contains(p);
    internal bool BarHit(Vec p) => bar != null && bar.Hit(p);
    internal bool BarGesture => barPress;
    internal bool PressBar(Vec p, float now)
    {
        if (bar == null || !bar.OnPress(p, now)) return false;
        barPress = true;
        Layout();
        return true;
    }
    internal void EndBar() { barPress = false; bar?.Cancel(); }
    private int RowAt(Vec p)
    {
        if (!Rect.Inset(0, Pad, 0, Pad).Contains(p)) return -1;
        float y = p.Y - Rect.Y - Pad + scroll;
        for (int i = 0; i < rows.Count; i++) { float h = RowH(i); if (y < h) return i; y -= h; }
        return -1;
    }
    internal void Refresh(Vec pointer, bool pointerValid, bool held)
    {
        if (!Owner.Visible || !Owner.Panel.IsOpen) { Kit.ClosePopup(); return; }
        if (Owner.Current().Count != count) Rebuild();
        if (bar != null)
        {
            if (bar.Tick(Kit.Now, pointer, pointerValid && bar.Hit(pointer), held && barPress)) Layout();
            if (!held) barPress = false;
        }
        hover = pointerValid && !BarHit(pointer) ? RowAt(pointer) : -1;
        int sel = Owner.SelectedIndex();
        for (int i = 0; i < views.Count; i++)
        {
            if (rows[i].Heading) continue;
            bool isSel = rows[i].Index == sel, isHover = i == hover;
            views[i].Bg.Set(isSel ? Tok.Selected : isHover ? Tok.Btn : Rgba.Clear, Rgba.Clear);
            Draw.SetColor(views[i].Text, isSel ? Tok.AccentText : Tok.Text);
        }
    }
    internal void Click(Vec p)
    {
        if (BarHit(p)) return;
        int r = RowAt(p);
        if (r < 0 || rows[r].Heading) return;
        Owner.Choose(rows[r].Index);
        Kit.ClosePopup();
    }
    internal void Scroll(float notches)
    {
        float viewH = Rect.H - 2 * Pad;
        scroll = KitMath.ClampScroll(scroll - notches * Tok.PopupItemH * 2, contentH, viewH);
        Layout();
    }
    internal void Destroy()
    {
        bar?.Cancel(); bar?.Unbuild(); bar = null; barPress = false;
        if (root != null) UnityEngine.Object.Destroy(root.gameObject);
        root = null;
    }
}

/// <summary>
/// Single-line text field typed via the Input System: letters, digits, space, punctuation,
/// Backspace/Delete/arrows/Home/End, Ctrl+A, Enter commits, Esc reverts, click outside commits.
/// </summary>
public sealed class TextField : Element
{
    private readonly Func<string> value; private readonly Action<string> commit;
    private readonly string placeholder;
    private Action<string>? onChange;
    internal int MaxLength = 64; internal Func<char, bool>? Filter;
    private BoxView? box; private RectTransform? clip; private TextMeshProUGUI? text; private Image? caret, selection;
    private string lastShown = "\u0001";
    internal TextField(Func<string> value, Action<string> commit, string placeholder) { this.value = value; this.commit = commit; this.placeholder = placeholder; }
    public TextField MaxChars(int n) { MaxLength = Math.Max(1, n); return this; }
    /// <summary>Only digits, sign and decimal point.</summary>
    public TextField Numeric() { Filter = c => char.IsDigit(c) || c is '.' or '-' or ','; return this; }
    /// <summary>Called on every edit while typing (commit is still called on Enter/blur).</summary>
    public TextField OnChange(Action<string> changed) { onChange = changed; return this; }
    internal bool Focused => Kit.Focus == this;
    protected override void Build()
    {
        box = new BoxView(Rt!, "Box", Tok.Radius); Draw.Stretch(box.Rt);
        clip = Draw.Node("Clip", Rt!); clip.gameObject.AddComponent<RectMask2D>();
        selection = Draw.Child(clip, "Selection", null, Tok.Accent.WithAlpha(.35f));
        text = Draw.Text(Draw.Node("Text", clip), TextStyle.Body); text.overflowMode = TextOverflowModes.Overflow;
        caret = Draw.Child(clip, "Caret", null, Tok.Text);
        lastShown = "\u0001";
    }
    internal override void Unbuild() { box = null; clip = null; text = null; caret = selection = null; base.Unbuild(); }
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : -FlexWeight;
    internal override float MeasureHeight(float width) => Tok.TextFieldH;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clipRect)
    {
        base.ArrangeCore(r, origin, clipRect);
        if (clip != null) Draw.Place(clip, 10, 0, MathF.Max(0, r.W - 20), r.H);
        lastShown = "\u0001";
    }
    internal override bool Interactive => true;
    internal override void Refresh(float now)
    {
        if (text == null || clip == null) return;
        bool focus = Focused;
        string shown = focus ? Kit.Edit.Text : Guard.Get(Site(0, ".Value"), value, "") ?? "";
        bool empty = shown.Length == 0;
        string display = empty && !focus ? placeholder : shown;
        float innerW = Rect.W - 20;
        if (display + (focus ? Kit.Edit.Caret + "|" + Kit.Edit.AllSelected : "") != lastShown)
        {
            lastShown = display + (focus ? Kit.Edit.Caret + "|" + Kit.Edit.AllSelected : "");
            Draw.SetText(text, display);
            float full = Draw.Width(text, display);
            float caretX = focus ? Draw.Width(text, shown[..Math.Min(Kit.Edit.Caret, shown.Length)].Replace(' ', ' ')) : 0;
            float shift = focus ? MathF.Max(0, caretX - innerW + 4) : 0;
            Draw.Place(text.rectTransform, -shift, 0, MathF.Max(innerW, full + 4) + shift, Rect.H);
            if (caret != null) Draw.Place(caret.rectTransform, caretX - shift, (Rect.H - 18) / 2, 1.5f, 18);
            if (selection != null) Draw.Place(selection.rectTransform, -shift, (Rect.H - 20) / 2, MathF.Min(full, innerW + shift), 20);
        }
        Draw.SetColor(text, empty && !focus ? Tok.Faint : !Enabled ? Tok.DisabledText : HasError ? Tok.ErrorText : Tok.Text);
        box?.Set(Tok.Panel, focus ? Tok.Accent : Hovered && Enabled ? Tok.BorderHover : Tok.Border);
        Draw.SetActive(caret, focus && !Kit.Edit.AllSelected && (now * 1.9f % 2f) < 1.2f);
        Draw.SetActive(selection, focus && Kit.Edit.AllSelected);
    }
    internal override void OnPress(Vec p)
    {
        if (!Focused) Kit.FocusText(this, Guard.Get(Site(0, ".Value"), value, "") ?? "");
        else { Kit.Edit.End(); lastShown = "\u0001"; }
    }
    internal void Changed(string t) { if (onChange != null) Guard.Run(Where + " change", () => onChange(t)); }
    internal void Commit(string t) { Guard.Run(Where + " commit", () => commit(t)); }
}

/// <summary>Tab bar (condensed caps, 2 px accent underline on the selected tab).</summary>
public sealed class Tabs : Element
{
    private readonly Func<IReadOnlyList<string>> source;
    private readonly TabLabelCache cache = new();
    private readonly Func<int> selected; private readonly Action<int> select;
    private readonly List<(TextMeshProUGUI Text, Image Line)> parts = new();
    private Image? baseLine; private float[] xs = Array.Empty<float>(), ws = Array.Empty<float>();
    internal Tabs(Func<IReadOnlyList<string>> source, Func<int> selected, Action<int> select) { this.source = source; this.selected = selected; this.select = select; }
    protected override void Build()
    {
        parts.Clear();
        baseLine = Draw.Child(Rt!, "Line", null, Tok.Frame);
        PollLabels();
        BuildParts();
    }
    private void BuildParts()
    {
        foreach (var t in cache.Labels)
        {
            var node = Draw.Node("Tab", Rt!);
            var text = Draw.Text(node, TextStyle.Heading, t); text.alignment = TextAlignmentOptions.Midline;
            var line = Draw.Child(Rt!, "Underline", null, Tok.Accent);
            parts.Add((text, line));
        }
    }
    private bool PollLabels() => cache.Poll(Guard.Get(Site(0,".Labels"),source,(IReadOnlyList<string>)Array.Empty<string>()) ?? Array.Empty<string>());
    internal override void Unbuild() { parts.Clear(); baseLine = null; base.Unbuild(); }
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : -FlexWeight;
    internal override float MeasureHeight(float width) => Tok.TabH + 1;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        if(ws.Length!=parts.Count) { ws=new float[parts.Count]; xs=new float[parts.Count]; }
        float x=0;
        for(int i=0;i<parts.Count;i++) { xs[i]=x; ws[i]=Draw.Width(parts[i].Text,parts[i].Text.text)+2*Tok.TabPadX; x+=ws[i]+2; }
        for (int i = 0; i < parts.Count; i++)
        {
            Draw.Place(parts[i].Text.rectTransform, xs[i], 0, ws[i], Tok.TabH);
            Draw.Place(parts[i].Line.rectTransform, xs[i], Tok.TabH - 2, ws[i], 2);
        }
        if (baseLine != null) Draw.Place(baseLine.rectTransform, 0, Tok.TabH, r.W, 1);
    }
    private int PartAt(Vec p) { float x = p.X - Rect.X; for (int i = 0; i < xs.Length; i++) if (x >= xs[i] && x < xs[i] + ws[i]) return i; return -1; }
    internal override bool Interactive => true;
    internal override Element? HitTest(Vec p) => base.HitTest(p) is { } e && (PartAt(p) >= 0 || TooltipText != null) ? e : null;
    internal override void Refresh(float now)
    {
        if(PollLabels())
        {
            if(parts.Count!=cache.Labels.Count)
            {
                foreach(var part in parts) { UnityEngine.Object.Destroy(part.Text.gameObject); UnityEngine.Object.Destroy(part.Line.gameObject); }
                parts.Clear(); BuildParts();
            }
            else for(int i=0;i<parts.Count;i++) Draw.SetText(parts[i].Text,cache.Labels[i]);
            MarkDirty();
        }
        int sel = Guard.Get(Site(1,".Selected"), selected, 0);
        int hover = Hovered ? PartAt(Panel.LocalPointer) : -1;
        for (int i = 0; i < parts.Count; i++)
        {
            Draw.SetColor(parts[i].Text, !Enabled ? Tok.DisabledText : i == sel || i == hover ? Tok.Text : Tok.Text2);
            Draw.SetActive(parts[i].Line, i == sel);
        }
    }
    internal override void OnClick(Vec p) { int i = PartAt(p); if (i >= 0) Guard.Run(Where + " select", () => select(i)); }
}

/// <summary>Status lamp (10 px, green with glow when on), optional label, optional afterglow.</summary>
public sealed class Lamp : Element
{
    private readonly Func<bool> on; private readonly string? label;
    private float hold, lastOn = -100;
    private Func<LampKind>? kind;
    private Image? dot, glow; private TextMeshProUGUI? text;
    internal Lamp(Func<bool> on, string? label) { this.on = on; this.label = label; }
    /// <summary>Stay lit this many seconds after the getter goes false (short events stay visible).</summary>
    public Lamp Hold(float seconds) { hold = MathF.Max(0, seconds); return this; }
    /// <summary>Lit colour: On (green), Warning (amber) or Error (red), from a getter (0.1.2).</summary>
    public Lamp Kind(Func<LampKind> lampKind) { kind = lampKind; return this; }
    protected override void Build()
    {
        glow = Draw.Child(Rt!, "Glow", Assets.Glow(), Tok.On.WithAlpha(.55f));
        dot = Draw.Child(Rt!, "Dot", Assets.Circle(), Tok.OffTrack);
        if (label != null) text = Draw.Text(Draw.Node("Label", Rt!), TextStyle.Body, label);
    }
    internal override void Unbuild() { dot = glow = null; text = null; base.Unbuild(); }
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : Tok.Lamp + (label != null && text != null ? 10 + Draw.Width(text, label) : label != null ? 10 + label.Length * 8 : 0);
    internal override float MeasureHeight(float width) => label != null ? 20 : Tok.Lamp;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        float y = (r.H - Tok.Lamp) / 2;
        if (dot != null) Draw.Place(dot.rectTransform, 0, y, Tok.Lamp, Tok.Lamp);
        if (glow != null) Draw.Place(glow.rectTransform, -8, y - 8, Tok.Lamp + 16, Tok.Lamp + 16);
        if (text != null) Draw.Place(text.rectTransform, Tok.Lamp + 10, 0, MathF.Max(0, r.W - Tok.Lamp - 10), r.H);
    }
    internal override void Refresh(float now)
    {
        bool lit = KitMath.LampLit(Guard.Get(Where + ".On", on, false), now, ref lastOn, hold);
        var c = kind == null ? Tok.On : Look.Lamp(Guard.Get(Where + ".Kind", kind, LampKind.On));
        Draw.SetColor(dot, lit ? c : Tok.OffTrack);
        Draw.SetActive(glow, lit);
        if (lit) Draw.SetColor(glow, c.WithAlpha(.55f));
    }
}

/// <summary>4 px progress bar (#14150f track, accent fill), value 0..1.</summary>
public sealed class ProgressBar : Element
{
    private readonly Func<float> value; private BoxView? track, fill;
    internal ProgressBar(Func<float> value) { this.value = value; }
    protected override void Build()
    {
        track = new BoxView(Rt!, "Track", 2, Corners.All, ring: false); Draw.Stretch(track.Rt); track.Set(Tok.Panel, Rgba.Clear);
        fill = new BoxView(Rt!, "Fill", 2, Corners.All, ring: false); fill.Set(Tok.Accent, Rgba.Clear);
    }
    internal override void Unbuild() { track = fill = null; base.Unbuild(); }
    internal override float MeasureHeight(float width) => Tok.Progress;
    internal override void Refresh(float now)
    {
        float v = Math.Clamp(Guard.Get(Where + ".Value", value, 0f), 0f, 1f);
        if (float.IsNaN(v)) v = 0;
        if (fill != null) { Draw.SetActive(fill.Fill, v > 0.001f); fill.Place(0, 0, MathF.Max(Tok.Progress, Rect.W * v), Rect.H); }
    }
}

/// <summary>Chip/tag: context pill, small tag, accent command or neutral command.</summary>
public sealed class Chip : Element
{
    private string text; private readonly Func<string>? getter; private ChipStyle style; private Func<ChipStyle>? styleGetter;
    private Rgba? custom;
    private BoxView? box; private TextMeshProUGUI? label;
    internal Chip(string text, Func<string>? getter, ChipStyle style) { this.text = text; this.getter = getter; this.style = style; if (getter != null) this.text = Guard.Get("Chip getter", getter, ""); }
    public Chip StyleWhen(Func<ChipStyle> s) { styleGetter = s; return this; }
    /// <summary>Custom colour for text and border (0.1.2), e.g. rarity; keep a text that names it too.</summary>
    public Chip Colour(Color colour) { custom = Draw.R(colour); applied = (ChipStyle)(-1); return this; }
    protected override void Build()
    {
        var l = Look.Chip(style);
        box = new BoxView(Rt!, "Box", l.Radius, Corners.All, ring: true); Draw.Stretch(box.Rt);
        label = Draw.Text(Draw.Node("Label", Rt!), TextStyle.Body, text); label.alignment = TextAlignmentOptions.Midline;
        Apply(style);
    }
    private ChipStyle applied = (ChipStyle)(-1);
    private void Apply(ChipStyle s)
    {
        if (label == null || box == null || s == applied) return;
        applied = s; var l = Look.Chip(s);
        box.Set(l.Fill, custom ?? l.Border); label.fontSize = l.Size; label.color = Draw.C(custom ?? l.Text); label.characterSpacing = l.Spacing;
        label.fontStyle = (l.Bold ? FontStyles.Bold : FontStyles.Normal) | FontStyles.UpperCase;
        MarkDirty();
    }
    internal override void Unbuild() { box = null; label = null; applied = (ChipStyle)(-1); base.Unbuild(); }
    private ChipStyle Current => styleGetter != null ? Guard.Get(Site(0, ".Style"), styleGetter, style) : style;
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : (label != null ? Draw.Width(label, text) : text.Length * 8) + 2 * Look.Chip(Current).PadX + 2;
    internal override float MeasureHeight(float width) => Look.Chip(Current).H;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        if (label != null) Draw.Place(label.rectTransform, 0, 0, r.W, r.H);
    }
    internal override void Refresh(float now)
    {
        Apply(Current);
        if (getter == null) return;
        var t = Guard.Get(Site(1, ".Text"), getter, text) ?? "";
        if (t != text) { text = t; Draw.SetText(label, t); MarkDirty(); }
    }
}

/// <summary>Warning row (#2a1c15, #8a4a30 border, warning icon, #ffcbb3 text); severity and click since 0.1.2.</summary>
public sealed class WarningRow : Element
{
    private string text; private readonly Func<string>? getter;
    private Func<StatusKind>? severity; private StatusKind fixedSeverity = StatusKind.Error;
    private Action? onClick;
    private BoxView? box; private Image? icon; private TextMeshProUGUI? label;
    internal WarningRow(string text, Func<string>? getter) { this.text = text; this.getter = getter; }
    /// <summary>Error (default look), Warning (amber), Info/Success (row look) (0.1.2).</summary>
    public WarningRow Severity(StatusKind kind) { fixedSeverity = kind; severity = null; return this; }
    public WarningRow Severity(Func<StatusKind> kind) { severity = kind; return this; }
    /// <summary>Make the row clickable (hover border #8a7a52), e.g. "jump to the field" (0.1.2).</summary>
    public WarningRow OnClick(Action click) { onClick = click; return this; }
    protected override void Build()
    {
        box = new BoxView(Rt!, "Box", Tok.Radius); Draw.Stretch(box.Rt); box.Set(Tok.WarnRowBg, Tok.WarnRowBorder);
        icon = Draw.Child(Rt!, "Icon", Assets.IconSprite(Icon.Warning), Tok.Error);
        label = Draw.Text(Draw.Node("Text", Rt!), TextStyle.Body, text); label.color = Draw.C(Tok.ErrorText);
    }
    internal override void Unbuild() { box = null; icon = null; label = null; base.Unbuild(); }
    internal override float MeasureHeight(float width) => Tok.RowH;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        if (icon != null) Draw.Place(icon.rectTransform, 10, (r.H - 14) / 2, 14, 14);
        if (label != null) Draw.Place(label.rectTransform, 34, 0, MathF.Max(0, r.W - 44), r.H);
    }
    internal override bool Interactive => onClick != null || base.Interactive;
    internal override void Refresh(float now)
    {
        var kind = severity != null ? Guard.Get(Where + ".Severity", severity, fixedSeverity) : fixedSeverity;
        var (bg, border, ink, ic) = Look.Warning(kind);
        box?.Set(bg, onClick != null && Hovered ? Tok.BorderHover : border);
        Draw.SetColor(label, ink); Draw.SetColor(icon, ic);
        if (icon != null) { var s = Assets.IconSprite(kind == StatusKind.Success ? Icon.Check : Icon.Warning); if (icon.sprite != s) icon.sprite = s; }
        if (getter == null) return;
        var t = Guard.Get(Where + ".Text", getter, text) ?? "";
        if (t != text) { text = t; Draw.SetText(label, t); }
    }
    internal override void OnClick(Vec p) { if (onClick != null) Guard.Run(Where + " click", onClick); }
}
