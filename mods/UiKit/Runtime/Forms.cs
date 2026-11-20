using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Corners = SprocketUiKit.ShapeRaster.Corners;
namespace SprocketUiKit;

/// <summary>
/// Slider (0.1.2): 4 px track (#3d3c35) with accent fill, 14 px accent knob with an ink ring, value
/// text on the right (or a linked numeric field). Drag or click on the track; Shift while dragging =
/// fine (10× slower); wheel = one step, Shift+wheel = 10 steps. Snaps to step, clamps to the live min/max.
/// </summary>
public sealed class Slider : Element
{
    private readonly Func<double> value; private readonly Action<double> set;
    private readonly Func<double> min, max; private readonly double step; private readonly string? unit;
    private Func<double, string>? format; private Action? dragEnd;
    private TextField? field; private float fieldW = Tok.SliderValueW;
    private Action<string>? fieldChange; private bool hideValue;
    private BoxView? track, fill; private Image? knobRing, knob; private TextMeshProUGUI? valueText;
    private bool dragging, fineMode; private double dragStart; private float dragStartX;
    private float trackX0, trackX1;
    internal Slider(Func<double> value, Action<double> set, Func<double> min, Func<double> max, double step, string? unit)
    { this.value = value; this.set = set; this.min = min; this.max = max; this.step = step; this.unit = unit; }

    /// <summary>Replace the value text by a linked numeric field (Enter/blur commits, Esc reverts).</summary>
    public Slider WithField(float width = 64)
    {
        fieldW = MathF.Max(40, width);
        if (field == null)
        {
            field = new TextField(() => FormatPlain(Current()), Commit, "").Numeric();
            field.MaxLength = 16;
            if (fieldChange != null) field.OnChange(fieldChange);
            if (Panel != null) field.Attach(Panel, this);
            if (Rt != null) field.BuildIn(Rt);
        }
        MarkDirty(); return this;
    }
    /// <summary>
    /// Called with the field text on every keystroke in the field of <see cref="WithField"/> (0.1.4, #16),
    /// e.g. to validate with <c>.Error(...)</c> while typing. The value is still set on Enter/blur.
    /// </summary>
    public Slider OnFieldChange(Action<string> changed) { fieldChange = changed; field?.OnChange(changed); return this; }
    /// <summary>No value text and no space reserved for it: the track takes the full width (0.1.4, #16). A <see cref="WithField"/> field still shows.</summary>
    public Slider HideValue() { hideValue = true; MarkDirty(); return this; }
    public Slider Format(Func<double, string> f) { format = f; return this; }
    /// <summary>Called once when a drag (or a click on the track) ends, e.g. for an expensive preview.</summary>
    public Slider OnDragEnd(Action done) { dragEnd = done; return this; }

    private double Min => Guard.Get(Site(0, ".Min"), min, 0d);
    private double Max => Guard.Get(Site(1, ".Max"), max, 1d);
    private double Current() => Guard.Get(Site(2, ".Value"), value, double.NaN);
    private double plainValue = double.NaN, shownValue = double.NaN;
    private string plainCache = "", shownCache = "-";
    private string FormatPlain(double v)
    {
        if (!v.Equals(plainValue)) { plainValue = v; plainCache = FormatPlainCore(v); }
        return plainCache;
    }
    private string FormatShown(double v)
    {
        if (format != null) return FormatShownCore(v);
        if (!v.Equals(shownValue)) { shownValue = v; shownCache = FormatShownCore(v); }
        return shownCache;
    }
    private string FormatPlainCore(double v) => double.IsNaN(v) ? "" : KitMath.FormatValue(v, step > 0 ? step : .01, null);
    private string FormatShownCore(double v) => double.IsNaN(v) ? "-" : format != null ? Guard.Get(Where + ".Format", () => format(v), "?") : KitMath.FormatValue(v, step > 0 ? step : .01, unit);
    private void Commit(string text)
    {
        if (!Logic.TryParseNumber(text, out double v)) return;
        Apply(Logic.Snap(v, Min, Max, step));
    }
    private void Apply(double next)
    {
        double v = Current();
        if (double.IsNaN(next) || next == v) return;
        Guard.Run(Where + " set", () => set(next));
    }

    internal override void Attach(Panel panel, Element? parent) { base.Attach(panel, parent); field?.Attach(panel, this); }
    protected override void Build()
    {
        track = new BoxView(Rt!, "Track", 2, Corners.All, ring: false);
        fill = new BoxView(Rt!, "Fill", 2, Corners.All, ring: false);
        knobRing = Draw.Child(Rt!, "KnobRing", Assets.Circle(), Tok.Ink);
        knob = Draw.Child(knobRing.rectTransform, "Knob", Assets.Circle(), Tok.Accent);
        valueText = Draw.Text(Draw.Node("Value", Rt!), TextStyle.Value); valueText.alignment = TextAlignmentOptions.MidlineRight;
        field?.BuildIn(Rt!);
    }
    internal override void Unbuild() { track = fill = null; knobRing = knob = null; valueText = null; field?.Unbuild(); base.Unbuild(); }
    internal override float MeasureHeight(float width) => Tok.SliderH;
    private float ValueW => field != null ? fieldW : hideValue ? 0 : Tok.SliderValueW;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        float k = Tok.SliderKnob / 2;
        trackX0 = k; trackX1 = MathF.Max(trackX0 + 10, r.W - (ValueW > 0 ? ValueW + 12 : 0) - k);
        track?.Place(trackX0, (r.H - Tok.SliderTrackH) / 2, trackX1 - trackX0, Tok.SliderTrackH);
        if (valueText != null) { Draw.Place(valueText.rectTransform, r.W - ValueW, 0, ValueW, r.H); Draw.SetActive(valueText, field == null && !hideValue); }
        field?.Arrange(new RectF(r.Right - fieldW, r.Y, fieldW, r.H), new Vec(r.X, r.Y), clip);
    }
    internal override bool Interactive => true;
    internal override Element? HitTest(Vec p)
    {
        if (field != null && Visible && field.HitTest(p) is { } f) return f;
        var hit = base.HitTest(p);
        // only the track area is the slider; the value text is not
        return hit != null && p.X - Rect.X <= trackX1 + Tok.SliderKnob / 2 + 4 ? hit : TooltipText != null || TooltipGetter != null ? hit : null;
    }
    internal override void OnPress(Vec p)
    {
        double v = Current();
        if (double.IsNaN(v)) return;
        dragging = true; fineMode = Kit.ShiftHeld;
        dragStartX = p.X; dragStart = v;
        if (!fineMode) Apply(Logic.SliderAt((p.X - Rect.X - trackX0) / MathF.Max(1, trackX1 - trackX0), Min, Max, step));
    }
    internal override bool OnScroll(float notches, Vec p)
    {
        double v = Current();
        if (double.IsNaN(v)) return false;
        Apply(Logic.SliderWheel(v, notches, Min, Max, step, Kit.ShiftHeld));
        return true;
    }
    internal override void Refresh(float now)
    {
        double lo = Min, hi = Max, v = Current();
        if (dragging)
        {
            if (Kit.Pressed == this && Enabled)
            {
                float x = Panel.LocalPointer.X;
                if (Kit.ShiftHeld != fineMode) { fineMode = Kit.ShiftHeld; dragStartX = x; dragStart = v; }
                double next = fineMode ? Logic.SliderDrag(dragStart, x - dragStartX, trackX1 - trackX0, lo, hi, step, true)
                                       : Logic.SliderAt((x - Rect.X - trackX0) / MathF.Max(1, trackX1 - trackX0), lo, hi, step);
                Apply(next); v = Current();
            }
            else { dragging = false; if (dragEnd != null) Guard.Run(Where + ".OnDragEnd", dragEnd); }
        }
        float f = (float)Logic.SliderFraction(v, lo, hi);
        float tw = trackX1 - trackX0, kx = trackX0 + f * tw;
        bool active = Enabled && (Hovered || dragging);
        track?.Set(Enabled ? Tok.SliderTrack : Tok.DisabledBorder, Rgba.Clear);
        fill?.Set(!Enabled ? Tok.DisabledText : HasError ? Tok.Error : active ? Tok.AccentHover : Tok.Accent, Rgba.Clear);
        fill?.Place(trackX0, (Rect.H - Tok.SliderTrackH) / 2, MathF.Max(Tok.SliderTrackH, kx - trackX0), Tok.SliderTrackH);
        if (fill != null) Draw.SetActive(fill.Rt, f > .001f);
        float ks = Tok.SliderKnob + (active ? 2 : 0);
        if (knobRing != null) Draw.Place(knobRing.rectTransform, kx - ks / 2, (Rect.H - ks) / 2, ks, ks);
        if (knob != null) Draw.Place(knob.rectTransform, 1.5f, 1.5f, ks - 3, ks - 3);
        Draw.SetColor(knob, !Enabled ? Tok.DisabledText : HasError ? Tok.Error : active ? Tok.AccentHover : Tok.Accent);
        if (!hideValue) Draw.SetText(valueText, FormatShown(v));
        Draw.SetColor(valueText, !Enabled ? Tok.DisabledText : HasError ? Tok.ErrorText : Tok.Text);
        if (field != null) { field.Enabled = Enabled; field.Refresh(now); }
    }
}

/// <summary>Modal card inside a panel (0.1.2): dims the body, blocks clicks underneath, Esc cancels.</summary>
internal sealed class ModalView
{
    internal readonly Stack Content;
    internal readonly float Width;
    internal readonly Action? OnConfirm, OnCancel;
    internal readonly bool IsConfirm, Danger;
    internal RectTransform? Root; private Image? backdrop; private BoxView? card;
    internal ModalView(Panel panel, string title, float width, bool isConfirm, bool danger, Action? onConfirm, Action? onCancel)
    {
        Width = width; IsConfirm = isConfirm; Danger = danger; OnConfirm = onConfirm; OnCancel = onCancel;
        Content = new Stack { Gap = Tok.ModalGap, PadL = Tok.ModalPad, PadR = Tok.ModalPad, PadT = Tok.ModalPad, PadB = Tok.ModalPad };
        Content.Attach(panel, null);
        if (!string.IsNullOrEmpty(title)) Content.Add(new Label(title, null, TextStyle.Heading));
    }
    internal void Build(RectTransform panelRt)
    {
        Root = Draw.Node("Modal", panelRt);
        backdrop = Draw.Child(Root, "Backdrop", null, Tok.Backdrop);
        card = new BoxView(Root, "Card", Tok.RadiusPanel); card.Set(Tok.HeadFoot, Tok.Accent);
        Content.BuildIn(Root);
    }
    internal void Unbuild() { Root = null; backdrop = null; card = null; Content.Unbuild(); }
    internal void Destroy() { if (Root != null) UnityEngine.Object.Destroy(Root.gameObject); Unbuild(); }
    internal void Layout(float panelW, RectF body)
    {
        if (Root == null) return;
        Draw.Place(Root, 0, 0, panelW, body.Bottom);
        Draw.Place(backdrop!.rectTransform, body.X, body.Y, body.W, body.H);
        float w = MathF.Min(Width, body.W - 32), h = MathF.Min(Content.HeightFor(w), body.H - 16);
        float x = body.X + (body.W - w) / 2, y = body.Y + MathF.Max(8, (body.H - h) / 2);
        card!.Place(x, y, w, h);
        Content.Arrange(new RectF(x, y, w, h), new Vec(0, 0), body);
        Root.SetAsLastSibling();
    }
}
