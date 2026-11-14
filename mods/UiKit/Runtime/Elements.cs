using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace SprocketUiKit;

/// <summary>
/// Retained UI element. Layout is computed by the kit (kit units = 1080p reference px, y down,
/// relative to the panel's top-left); uGUI objects only mirror it. Live values come from getters
/// that the kit evaluates every frame inside <see cref="Guard"/>.
/// </summary>
public abstract class Element
{
    internal Panel Panel = null!;
    internal Element? Parent;
    internal RectTransform? Rt;
    internal RectF Rect;
    internal RectF Clip = RectF.Infinite;
    internal float FixedW = -1, FixedH = -1, FlexWeight = 1;
    internal bool FillHeight;
    internal Func<bool>? VisibleGetter, EnabledGetter;
    internal bool Visible = true, Enabled = true;
    internal string? TooltipText;
    internal Func<string?>? TooltipGetter;
    // 0.1.4 (#18): Where and per-frame Guard site names are cached, so polling allocates no strings
    private string? where; private string?[]? sites;
    internal string Where => where ??= $"{Panel?.Key ?? "?"}:{GetType().Name}";
    /// <summary>Cached "Where + suffix" for a Guard call that runs every frame; <paramref name="slot"/> 0..3 per class.</summary>
    internal string Site(int slot, string suffix) { sites ??= new string?[4]; return sites[slot] ??= Where + suffix; }

    /// <summary>True when this element is laid out and visible.</summary>
    public bool IsVisible => Visible;

    internal virtual void Attach(Panel panel, Element? parent) { Panel = panel; Parent = parent; where = null; sites = null; }
    internal void BuildIn(RectTransform parent)
    {
        Rt = Draw.Node(GetType().Name, parent);
        Build();
        if (!Visible) Rt.gameObject.SetActive(false);
    }
    internal virtual void Unbuild() { Rt = null; errorLabel = null; errorRing = null; group = null; }
    protected abstract void Build();
    /// <summary>Width wanted in a Row: &gt;= 0 fixed/intrinsic, &lt; 0 flexible (weight = -value).</summary>
    internal virtual float IntrinsicWidth() => FixedW >= 0 ? FixedW : -FlexWeight;
    internal abstract float MeasureHeight(float width);
    /// <summary>Height including the error line (0.1.2) while the element shows an error.</summary>
    internal float HeightFor(float width) => (FixedH >= 0 ? FixedH : MeasureHeight(width)) + (ErrorText != null ? Tok.ErrorLineH : 0);
    /// <summary>Place the element; the error line (if any) is reserved under the control.</summary>
    internal void Arrange(RectF r, Vec origin, RectF clip)
    {
        if (ErrorText == null) { ArrangeCore(r, origin, clip); if (errorLabel != null) { Draw.SetActive(errorLabel, false); Draw.SetActive(errorRing!.Rt, false); } return; }
        var control = new RectF(r.X, r.Y, r.W, MathF.Max(0, r.H - Tok.ErrorLineH));
        ArrangeCore(control, origin, clip);
        ShowError(control);
    }
    internal virtual void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        Rect = r; Clip = clip;
        if (Rt != null) Draw.Place(Rt, r, origin);
    }
    /// <summary>Evaluate visibility/enabled (and error/alpha, 0.1.2); returns true when visibility changed (layout dirty).</summary>
    internal bool Evaluate()
    {
        Enabled = EnabledGetter == null || Guard.Get(Where + ".EnabledWhen", EnabledGetter, true);
        bool v = VisibleGetter == null || Guard.Get(Where + ".VisibleWhen", VisibleGetter, true);
        if (ErrorGetter != null && v) EvaluateError();
        if (AlphaGetter != null && Rt != null && v) ApplyAlpha();
        if (v == Visible) return false;
        Visible = v;
        if (Rt != null) Rt.gameObject.SetActive(v);
        return true;
    }

    // ---------- 0.1.2: field error state ----------
    internal Func<string?>? ErrorGetter;
    internal string? ErrorText;
    private TextMeshProUGUI? errorLabel; private BoxView? errorRing;
    /// <summary>True while the element's <c>.Error(...)</c> getter returns a message.</summary>
    public bool HasError => ErrorText != null;
    internal string? CurrentError() => ErrorGetter == null ? null : NullIfEmpty(Guard.Get(Where + ".Error", ErrorGetter, null));
    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
    private void EvaluateError()
    {
        var e = CurrentError();
        if ((e == null) != (ErrorText == null)) { ErrorText = e; MarkDirty(); }
        else if (e != ErrorText) { ErrorText = e; Draw.SetText(errorLabel, e); }
    }
    private void ShowError(RectF control)
    {
        if (Rt == null) return;
        if (errorLabel == null)
        {
            errorRing = new BoxView(Rt, "ErrorRing", Tok.Radius); errorRing.Set(Rgba.Clear, Tok.Error); Draw.Stretch(errorRing.Rt);
            errorLabel = Draw.Text(Draw.Node("Error", Rt), TextStyle.Small); errorLabel.color = Draw.C(Tok.ErrorText);
        }
        errorRing!.Rt.SetAsLastSibling();
        Draw.SetActive(errorRing.Rt, true); Draw.SetActive(errorLabel, true);
        Draw.SetText(errorLabel, ErrorText);
        Draw.Place(errorLabel.rectTransform, 0, control.H + 2, control.W, Tok.ErrorLineH - 2);
    }

    // ---------- 0.1.2: element alpha ----------
    internal Func<float>? AlphaGetter;
    private CanvasGroup? group;
    private void ApplyAlpha()
    {
        float a = Math.Clamp(Guard.Get(Where + ".Alpha", AlphaGetter, 1f), 0f, 1f);
        if (float.IsNaN(a)) a = 1;
        if (group == null) { group = Rt!.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false; }
        if (MathF.Abs(group.alpha - a) > .001f) group.alpha = a;
    }
    internal virtual void Refresh(float now) { }
    internal virtual bool Interactive => TooltipText != null || TooltipGetter != null;
    internal virtual Element? HitTest(Vec p) => Visible && Interactive && Clip.Contains(p) && Rect.Contains(p) ? this : null;
    internal virtual IEnumerable<Element> Kids => Array.Empty<Element>();
    internal virtual void OnPress(Vec p) { }
    internal virtual void OnClick(Vec p) { }
    internal virtual bool OnScroll(float notches, Vec p) => false;
    internal virtual string? TooltipNow() => TooltipGetter != null ? Guard.Get(Where + ".Tooltip", TooltipGetter, null) : TooltipText;
    internal bool Hovered => Kit.Hover == this;
    internal bool Pressed => Kit.Pressed == this && Kit.Hover == this;
    internal void MarkDirty() => Panel?.MarkDirty();
}

/// <summary>Fluent settings shared by every element.</summary>
public static class ElementExtensions
{
    /// <summary>Fixed width in reference px (1080p).</summary>
    public static T Width<T>(this T e, float width) where T : Element { e.FixedW = width; e.MarkDirty(); return e; }
    /// <summary>Fixed height in reference px (1080p).</summary>
    public static T Height<T>(this T e, float height) where T : Element { e.FixedH = height; e.MarkDirty(); return e; }
    /// <summary>Flexible width in a Row with the given weight.</summary>
    public static T Flex<T>(this T e, float weight = 1) where T : Element { e.FixedW = -1; e.FlexWeight = MathF.Max(.01f, weight); e.MarkDirty(); return e; }
    /// <summary>Take the leftover height of the parent stack/column.</summary>
    public static T Fill<T>(this T e, bool fill = true) where T : Element { e.FillHeight = fill; e.MarkDirty(); return e; }
    public static T Tooltip<T>(this T e, string? text) where T : Element { e.TooltipText = text; return e; }
    public static T Tooltip<T>(this T e, Func<string?> text) where T : Element { e.TooltipGetter = text; return e; }
    public static T VisibleWhen<T>(this T e, Func<bool> visible) where T : Element { e.VisibleGetter = visible; return e; }
    public static T EnabledWhen<T>(this T e, Func<bool> enabled) where T : Element { e.EnabledGetter = enabled; return e; }
    /// <summary>
    /// Field error state (0.1.2): while <paramref name="message"/> returns text, the element gets a red
    /// border and the message in a 12 px line under it (height reserved only then). See
    /// <see cref="Panel.HasErrors"/>. Hidden elements never count.
    /// </summary>
    public static T Error<T>(this T e, Func<string?> message) where T : Element { e.ErrorGetter = message; e.MarkDirty(); return e; }
    /// <summary>Opacity 0..1 from a getter, polled every frame (0.1.2; a CanvasGroup is added on first use).</summary>
    public static T Alpha<T>(this T e, Func<float> alpha) where T : Element { e.AlphaGetter = alpha; return e; }
}

/// <summary>Element that holds children and offers the component factories.</summary>
public abstract class Container : Element
{
    internal readonly List<Element> Children = new();
    internal float Gap;
    internal float PadL, PadT, PadR, PadB;

    internal override void Attach(Panel panel, Element? parent)
    {
        base.Attach(panel, parent);
        foreach (var c in Children) c.Attach(panel, this);
    }
    /// <summary>uGUI parent for children (a scroll view's content node, otherwise this element).</summary>
    internal virtual RectTransform? ChildRoot => Rt;
    protected override void Build() { foreach (var c in Children) c.BuildIn(ChildRoot!); }
    internal override void Unbuild() { foreach (var c in Children) c.Unbuild(); base.Unbuild(); }

    /// <summary>Add any element (also custom ones) to this container.</summary>
    public virtual T Add<T>(T element) where T : Element
    {
        if (element.Parent != null) throw new InvalidOperationException("Element already has a parent.");
        Children.Add(element);
        element.Attach(Panel, this);
        if (ChildRoot != null && Panel != null && Panel.Built) element.BuildIn(ChildRoot);
        MarkDirty();
        return element;
    }
    /// <summary>Remove a child (its uGUI objects are destroyed).</summary>
    public void Remove(Element element)
    {
        if (!Children.Remove(element)) return;
        if (element.Rt != null) UnityEngine.Object.Destroy(element.Rt.gameObject);
        element.Unbuild(); element.Parent = null;
        if (Kit.Hover == element || Kit.Pressed == element) Kit.ForgetElement();
        MarkDirty();
    }
    public Container Padding(float all) => Padding(all, all, all, all);
    public Container Padding(float top, float right, float bottom, float left) { PadT = top; PadR = right; PadB = bottom; PadL = left; MarkDirty(); return this; }
    public Container Spacing(float gap) { Gap = gap; MarkDirty(); return this; }

    internal override void Refresh(float now)
    {
        bool dirty = false;
        foreach (var c in Children)
        {
            dirty |= c.Evaluate();
            if (c.Visible) c.Refresh(now);
        }
        if (dirty) MarkDirty();
    }
    internal override Element? HitTest(Vec p)
    {
        if (!Visible || !Clip.Contains(p) || !Rect.Contains(p)) return null;
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            var hit = Children[i].HitTest(p);
            if (hit != null) return hit;
        }
        return Interactive ? this : null;
    }
    internal IEnumerable<Element> VisibleChildren => Children.Where(c => c.Visible);
    internal override IEnumerable<Element> Kids => Children;

    // ---------- Factories (each adds the new element to this container) ----------
    public NodeGraph NodeGraph() => Add(new NodeGraph());
    public Label Label(string text, TextStyle style = TextStyle.Value) => Add(new Label(text, null, style));
    public Label Label(Func<string> text, TextStyle style = TextStyle.Value) => Add(new Label("", text, style));
    public Button Button(string text, Action onClick, ButtonStyle style = ButtonStyle.Normal) => Add(new Button(text, onClick, style));
    public IconButton IconButton(Icon icon, Action onClick, string? tooltip = null) => Add(new IconButton(icon, onClick)).Tooltip(tooltip);
    public SegmentedButtons Segmented(IReadOnlyList<string> options, Func<int> selected, Action<int> select) => Add(new SegmentedButtons(options, selected, select));
    public Stepper Stepper(Func<double> value, Action<double> set, double min, double max, double step, string? unit = null) => Add(new Stepper(value, set, min, max, step, unit));
    public Toggle Toggle(Func<bool> value, Action<bool> set) => Add(new Toggle(value, set));
    public Dropdown Dropdown(IReadOnlyList<string> options, Func<int> selected, Action<int> select) => Add(new Dropdown(() => options, selected, select));
    public Dropdown Dropdown(Func<IReadOnlyList<string>> options, Func<int> selected, Action<int> select) => Add(new Dropdown(options, selected, select));
    /// <summary>Dropdown with grouped options: a heading is shown for each new <see cref="Choice.Group"/>.</summary>
    public Dropdown Dropdown(Func<IReadOnlyList<Choice>> choices, Func<int> selected, Action<int> select) => Add(new Dropdown(choices, selected, select, true));
    /// <summary>Row with the list-row background (min height 48) for a variable set of controls.</summary>
    public Card Card() => Add(new Card());
    /// <summary>Expandable section; add content to <see cref="Collapsible.Body"/>.</summary>
    public Collapsible Collapsible(string title, bool expanded = false) => Add(new Collapsible(title, expanded));
    /// <summary>Clipped stack that scrolls with the mouse wheel (use Fill() or Height()).</summary>
    public ScrollView ScrollView() => Add(new ScrollView());
    /// <summary>One block per item, rebuilt when the item list changes.</summary>
    public Repeat<T> Repeat<T>(Func<IReadOnlyList<T>> items, Action<T, Stack> build, float gap = 6) => Add(new Repeat<T>(items, build, gap));
    public TextField TextField(Func<string> value, Action<string> commit, string placeholder = "") => Add(new TextField(value, commit, placeholder));
    public Tabs Tabs(IReadOnlyList<string> tabs, Func<int> selected, Action<int> select) => Add(new Tabs(() => tabs, selected, select));
    public Tabs Tabs(Func<IReadOnlyList<string>> labels, Func<int> selected, Action<int> select) => Add(new Tabs(labels, selected, select));
    public ListView<T> List<T>(Func<IReadOnlyList<T>> items, Action<T, ListRow> template) => Add(new ListView<T>(items, template));
    /// <summary>Aligned data table; columns keep their x positions at every size (0.1.6).</summary>
    public Table<T> Table<T>(Func<IReadOnlyList<T>> rows) => Add(new Table<T>(rows));
    /// <summary>Line chart (0.1.7), fixed height, full column width; one to four series with hover and a legend.</summary>
    public LineChart LineChart(float height = 220) => Add(new LineChart { FixedH = height });
    /// <summary>Bar chart or floating waterfall (0.1.7), fixed height, full column width.</summary>
    public BarChart BarChart(float height = 220) => Add(new BarChart { FixedH = height });
    public Lamp Lamp(Func<bool> on, string? label = null) => Add(new Lamp(on, label));
    public ProgressBar Progress(Func<float> value) => Add(new ProgressBar(value));
    /// <summary>Circular progress (0.1.8): diameter in Kit units, optional centre text, fill 0..1.</summary>
    public RadialProgress Radial(Func<float> value, float size = 48, Func<string?>? centre = null) => Add(new RadialProgress(value, size, centre));
    public Chip Chip(string text, ChipStyle style = ChipStyle.Neutral) => Add(new Chip(text, null, style));
    public Chip Chip(Func<string> text, ChipStyle style = ChipStyle.Neutral) => Add(new Chip("", text, style));
    public WarningRow Warning(string text) => Add(new WarningRow(text, null));
    public WarningRow Warning(Func<string> text) => Add(new WarningRow("", text));
    public Stack Stack(float gap = Tok.SectionGap) => Add(new Stack { Gap = gap });
    public Row Row(float gap = 8) => Add(new Row { Gap = gap });
    public Spacer Spacer(float size = -1) => Add(new Spacer(size));
    /// <summary>Row with a label on the left; add the control to the returned row (it is right-aligned).</summary>
    public Row FieldRow(string label)
    {
        var row = Row(12);
        row.Add(new Label(label, null, TextStyle.Value) { ColourOverride = Tok.Text2 });
        row.Spacer();
        return row;
    }
    /// <summary>Small label above a control ("Type", "Cannon"); add the control to the returned stack.</summary>
    public Stack Labeled(string label)
    {
        var s = Stack(4);
        s.Add(new Label(label, null, TextStyle.Small));
        return s;
    }
    // ---------- 0.1.2 factories ----------
    /// <summary>Slider (drag, click on the track, wheel = step, Shift+wheel = 10 steps, Shift+drag = fine).</summary>
    public Slider Slider(Func<double> get, Action<double> set, double min, double max, double step, string? unit = null) => Add(new Slider(get, set, () => min, () => max, step, unit));
    /// <summary>Slider with live limits (clamped to the current min/max every frame).</summary>
    public Slider Slider(Func<double> get, Action<double> set, Func<double> min, Func<double> max, double step, string? unit = null) => Add(new Slider(get, set, min, max, step, unit));
    /// <summary>
    /// Drawing surface (<paramref name="height"/> px, full width; <c>.Fill()</c> takes the leftover
    /// height). <paramref name="draw"/> runs only after <see cref="CanvasView.Invalidate"/>, a
    /// <see cref="CanvasView.Version"/> change, a resize, or at the <see cref="CanvasView.Fps"/> rate.
    /// </summary>
    public CanvasView Canvas(float height, Action<Painter> draw) => Add(new CanvasView(draw) { FixedH = height });
    /// <summary>Fixed-size drawing (diagram) that redraws when <paramref name="version"/> changes.</summary>
    public CanvasView Diagram(float width, float height, Func<int> version, Action<Painter> draw) => Add(new CanvasView(draw) { FixedW = width, FixedH = height }).Version(version);
    /// <summary>Chip in a custom colour (text and border), e.g. rarity colours.</summary>
    public Chip Chip(string text, Color colour) => Add(new Chip(text, null, ChipStyle.Neutral)).Colour(colour);

    /// <summary>Side-by-side columns. Width &gt; 0 = fixed px, 0 = flexible, &lt; 0 = flexible with weight.</summary>
    public Column[] Columns(params float[] widths)
    {
        var set = Add(new ColumnSet(widths));
        return set.Columns;
    }
}

/// <summary>Vertical container: children stretch to the full width unless they have a fixed width.</summary>
public class Stack : Container
{
    internal override float MeasureHeight(float width)
    {
        float inner = width - PadL - PadR, h = PadT + PadB; int n = 0;
        foreach (var c in VisibleChildren) { h += c.HeightFor(ChildWidth(c, inner)); n++; }
        return h + Gap * Math.Max(0, n - 1);
    }
    private static float ChildWidth(Element c, float inner) => c.FixedW >= 0 ? MathF.Min(c.FixedW, inner) : inner;
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        ArrangeChildren(new RectF(r.X + PadL, r.Y + PadT, r.W - PadL - PadR, r.H - PadT - PadB), clip.Intersect(r));
    }
    internal void ArrangeChildren(RectF inner, RectF clip)
    {
        var kids = VisibleChildren.ToList();
        var heights = kids.Select(c => c.HeightFor(ChildWidth(c, inner.W))).ToList();
        var final = KitMath.StackHeights(inner.H, Gap, heights, kids.Select(c => c.FillHeight).ToList());
        float y = inner.Y; var me = new Vec(Rect.X, Rect.Y);
        for (int i = 0; i < kids.Count; i++)
        {
            kids[i].Arrange(new RectF(inner.X, y, ChildWidth(kids[i], inner.W), final[i]), me, clip);
            y += final[i] + Gap;
        }
    }
    private Section OwnerSection => Parent as Section is { } s && ReferenceEquals(s.Body, this) ? s
        : throw new InvalidOperationException("Only the stack returned by Column.Section(...) belongs to a section.");
    /// <summary>On a section body (from <c>Column.Section</c>): show/hide the whole section, heading included (0.1.2).</summary>
    public Stack SectionVisibleWhen(Func<bool> visible) { OwnerSection.VisibleGetter = visible; return this; }
    /// <summary>On a section body: heading text from a getter (0.1.2).</summary>
    public Stack SectionTitle(Func<string> title) { OwnerSection.SetTitle(title); return this; }
}

/// <summary>Horizontal container: fixed/intrinsic widths first, flexible children share the rest; vertically centred.</summary>
public sealed class Row : Container
{
    private bool shrink;
    /// <summary>
    /// When the content is wider than the row, shrink labels and chips (ellipsis) instead of
    /// clipping the controls at the right edge (0.1.2). Use <c>Label.Wrap()</c> to wrap instead.
    /// </summary>
    public Row ShrinkToFit(bool on = true) { shrink = on; MarkDirty(); return this; }
    private float[] Widths(float width)
    {
        var kids = VisibleChildren.ToList();
        var intrinsic = kids.Select(c => c.IntrinsicWidth()).ToList();
        if (shrink) intrinsic = Logic.ShrinkWidths(width - PadL - PadR, Gap, intrinsic, kids.Select(c => c.FixedW < 0 && c is SprocketUiKit.Label or SprocketUiKit.Chip).ToList()).ToList();
        return KitMath.RowWidths(width - PadL - PadR, Gap, intrinsic);
    }
    internal override float IntrinsicWidth()
    {
        if (FixedW >= 0) return FixedW;
        float w = PadL + PadR; int n = 0;
        foreach (var c in VisibleChildren) { float cw = c.IntrinsicWidth(); if (cw < 0) return -FlexWeight; w += cw; n++; }
        return w + Gap * Math.Max(0, n - 1);
    }
    internal override float MeasureHeight(float width)
    {
        var kids = VisibleChildren.ToList(); var w = Widths(width);
        float h = 0;
        for (int i = 0; i < kids.Count; i++) h = MathF.Max(h, kids[i].HeightFor(w[i]));
        return h + PadT + PadB;
    }
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        var kids = VisibleChildren.ToList(); var w = Widths(r.W);
        var x = KitMath.Offsets(r.X + PadL, Gap, w);
        float innerH = r.H - PadT - PadB; var me = new Vec(r.X, r.Y); var c = clip.Intersect(r);
        for (int i = 0; i < kids.Count; i++)
        {
            float h = kids[i].FillHeight ? innerH : MathF.Min(innerH, kids[i].HeightFor(w[i]));
            kids[i].Arrange(new RectF(x[i], r.Y + PadT + (innerH - h) / 2, w[i], h), me, c);
        }
    }
}

/// <summary>Empty flexible (or fixed) space, e.g. to push the next item in a Row to the right.</summary>
public sealed class Spacer : Element
{
    private readonly float size;
    internal Spacer(float size) { this.size = size; }
    protected override void Build() { }
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : size >= 0 ? size : -FlexWeight;
    internal override float MeasureHeight(float width) => size >= 0 ? size : 0;
}

/// <summary>Panel column (padding 14, gap 10) with an optional footer block pinned to the bottom.</summary>
public sealed class Column : Stack
{
    internal Block? FooterBlock;
    internal Column() { Gap = Tok.ColumnGap; PadL = PadR = Tok.ColumnPadX; PadT = PadB = Tok.ColumnPadY; }
    /// <summary>Section with a heading (and optional right-hand note/count); add rows to the returned body.</summary>
    public Stack Section(string title, Func<string>? note = null)
    {
        var s = Add(new Section(title, note));
        return s.Body;
    }
    /// <summary>Section with a count on the right; the text is only rebuilt when the count changes (0.1.4, #18).</summary>
    public Stack Section(string title, Func<int> count) => Section(title, new IntText(count).Get);
    /// <summary>Boxed block pinned to the bottom of the column ("NEW INPUT"); add controls to it.</summary>
    public Block Footer(string? title = null)
    {
        if (FooterBlock != null) throw new InvalidOperationException("This column already has a footer block.");
        FooterBlock = new Block(title);
        Children.Add(FooterBlock); FooterBlock.Attach(Panel, this);
        if (Rt != null) FooterBlock.BuildIn(Rt);
        MarkDirty();
        return FooterBlock;
    }
    internal override float MeasureHeight(float width) => base.MeasureHeight(width);
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        if (FooterBlock == null || !FooterBlock.Visible) { base.ArrangeCore(r, origin, clip); return; }
        Rect = r; Clip = clip;
        if (Rt != null) Draw.Place(Rt, r, origin);
        float inner = r.W - PadL - PadR;
        float fh = FooterBlock.HeightFor(inner);
        var c = clip.Intersect(r);
        FooterBlock.Arrange(new RectF(r.X + PadL, r.Bottom - PadB - fh, inner, fh), new Vec(r.X, r.Y), c);
        // content above the footer
        FooterBlock.Visible = false;
        try { ArrangeChildren(new RectF(r.X + PadL, r.Y + PadT, inner, r.H - PadT - PadB - fh - Gap), c); }
        finally { FooterBlock.Visible = true; }
    }
}

/// <summary>Columns side by side, separated by 1 px dividers; fills the remaining panel height.</summary>
public sealed class ColumnSet : Element
{
    internal readonly Column[] Columns;
    private readonly float[] specs;
    private readonly List<Image> dividers = new();
    internal ColumnSet(float[] widths)
    {
        if (widths.Length == 0) widths = new float[] { 0 };
        specs = widths; FillHeight = true;
        Columns = widths.Select(_ => new Column()).ToArray();
    }
    internal override void Attach(Panel panel, Element? parent)
    {
        base.Attach(panel, parent);
        foreach (var c in Columns) c.Attach(panel, this);
    }
    protected override void Build()
    {
        dividers.Clear();
        for (int i = 1; i < Columns.Length; i++) dividers.Add(Draw.Child(Rt!, "Divider", null, Tok.Divider));
        foreach (var c in Columns) c.BuildIn(Rt!);
    }
    internal override void Unbuild() { foreach (var c in Columns) c.Unbuild(); dividers.Clear(); base.Unbuild(); }
    internal override IEnumerable<Element> Kids => Columns;
    // 0.1.4: all-fixed column sets give the panel's growth to their widest column (see Window.ColumnWidths)
    private float[] Widths(float width) => Window.ColumnWidths(width, specs, Panel?.GrowW ?? 0);
    internal override float MeasureHeight(float width)
    {
        var w = Widths(width);
        float h = 0;
        for (int i = 0; i < Columns.Length; i++) if (Columns[i].Visible) h = MathF.Max(h, Columns[i].HeightFor(w[i]) + (Columns[i].FooterBlock != null ? 0 : 0));
        return h;
    }
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        var w = Widths(r.W);
        var x = KitMath.Offsets(r.X, 0, w);
        var me = new Vec(r.X, r.Y); var c = clip.Intersect(r);
        for (int i = 0; i < Columns.Length; i++)
        {
            Columns[i].Arrange(new RectF(x[i], r.Y, w[i], r.H), me, c);
            if (i > 0 && i - 1 < dividers.Count) Draw.Place(dividers[i - 1].rectTransform, x[i] - r.X, 0, 1, r.H);
        }
    }
    internal override void Refresh(float now)
    {
        bool dirty = false;
        foreach (var c in Columns) { dirty |= c.Evaluate(); if (c.Visible) c.Refresh(now); }
        if (dirty) MarkDirty();
    }
    internal override Element? HitTest(Vec p)
    {
        if (!Visible || !Rect.Contains(p) || !Clip.Contains(p)) return null;
        foreach (var c in Columns) { var hit = c.HitTest(p); if (hit != null) return hit; }
        return null;
    }
}

/// <summary>Section heading (15 px caps, accent) with an optional note on the right, then the body.</summary>
public sealed class Section : Stack
{
    internal readonly Stack Body;
    private readonly Label titleLabel;
    internal Section(string title, Func<string>? note)
    {
        Gap = Tok.ColumnGap;
        var head = new Row { Gap = 8 };
        Children.Add(head); head.Parent = this;
        titleLabel = new Label(title, null, TextStyle.Section) { Parent = head };
        head.Children.Add(titleLabel);
        head.Children.Add(new Spacer(-1) { Parent = head });
        if (note != null) head.Children.Add(new Label("", note, TextStyle.Faint) { Parent = head });
        Body = new Stack { Gap = Tok.SectionGap, Parent = this };
        Children.Add(Body);
    }
    internal void SetTitle(Func<string> title) => titleLabel.SetGetter(title);
}

/// <summary>Boxed block (#1b1c15, border, radius 4, padding 12, gap 8) with a small caps title.</summary>
public sealed class Block : Stack
{
    private BoxView? box;
    internal Block(string? title)
    {
        Gap = Tok.BlockGap; PadL = PadR = PadT = PadB = Tok.BlockPad;
        if (!string.IsNullOrEmpty(title)) { var l = new Label(title!, null, TextStyle.BlockTitle) { Parent = this }; Children.Add(l); }
    }
    protected override void Build()
    {
        box = new BoxView(Rt!, "Box", Tok.Radius);
        Draw.Stretch(box.Rt); box.Set(Tok.HeadFoot, Tok.Divider);
        base.Build();
    }
    internal override void Unbuild() { box = null; base.Unbuild(); }
}

/// <summary>Text. Single line with ellipsis by default; <see cref="Wrap"/> for multi-line help text.</summary>
public sealed class Label : Element
{
    private string text;
    private Func<string>? getter;
    private TextStyle style;
    private bool wrap, right, centre;
    internal Rgba? ColourOverride;
    private Func<string?>? colourGetter; private Rgba? appliedColour;
    private float outline;
    private TextMeshProUGUI? tmp;
    internal Label(string text, Func<string>? getter, TextStyle style) { this.text = text; this.getter = getter; this.style = style; if (getter != null) this.text = Guard.Get("Label getter", getter, ""); }
    // 0.1.3 (#21): a label added after the panel was built already has its TMP; apply the wrap settings
    // now, as AlignRight/Centre do, or it stays NoWrap + Ellipsis while MeasureHeight counts lines.
    public Label Wrap() { wrap = true; if (tmp != null) ApplyWrap(); MarkDirty(); return this; }
    private void ApplyWrap() { tmp!.textWrappingMode = TextWrappingModes.Normal; tmp.overflowMode = TextOverflowModes.Overflow; tmp.alignment = TextAlignmentOptions.TopLeft; }
    public Label AlignRight() { right = true; if (tmp != null) tmp.alignment = TextAlignmentOptions.MidlineRight; return this; }
    public Label Centre() { centre = true; if (tmp != null) tmp.alignment = TextAlignmentOptions.Midline; return this; }
    public Label Style(TextStyle s) { style = s; if (tmp != null) { Draw.ApplyStyle(tmp, s); appliedColour = null; ApplyColour(); } MarkDirty(); return this; }
    /// <summary>Change a static label's text.</summary>
    public string Text { get => text; set { text = value ?? ""; Draw.SetText(tmp, text); MarkDirty(); } }
    /// <summary>Fixed text colour "#rrggbb" or "#rrggbbaa" (0.1.2).</summary>
    public Label Colour(string hex) { ColourOverride = Rgba.Hex(hex); colourGetter = null; ApplyColour(); return this; }
    /// <summary>Text colour from a getter ("#rrggbb[aa]"; null = the style colour), polled every frame (0.1.2).</summary>
    public Label Colour(Func<string?> hex) { colourGetter = hex; return this; }
    /// <summary>Fixed text colour (0.1.2).</summary>
    public Label Color(Color colour) { ColourOverride = Draw.R(colour); colourGetter = null; ApplyColour(); return this; }
    /// <summary>Black TMP outline for HUD text on bright backgrounds (0.1.2; width in TMP units, 0..1).</summary>
    public Label Outline(float width = .12f) { outline = Math.Clamp(width, 0, 1); ApplyOutline(); return this; }
    internal void SetGetter(Func<string> g) { getter = g; text = Guard.Get("Label getter", g, text) ?? ""; Draw.SetText(tmp, text); MarkDirty(); }
    protected override void Build()
    {
        tmp = Draw.Text(Rt!, style, text);
        appliedColour = null; ApplyColour(); ApplyOutline();
        if (wrap) ApplyWrap();
        else if (right) tmp.alignment = TextAlignmentOptions.MidlineRight;
        else if (centre) tmp.alignment = TextAlignmentOptions.Midline;
    }
    private void ApplyColour()
    {
        if (tmp == null) return;
        Rgba? want = ColourOverride;
        if (colourGetter != null) { var hex = Guard.Get(Site(1, ".Colour"), colourGetter, null); want = Draw.TryHex(hex); }
        var c = want ?? Draw.Style(style).Color;
        if (appliedColour is { } a && a.Equals(c)) return;
        appliedColour = c; tmp.color = Draw.C(c);
    }
    private void ApplyOutline()
    {
        if (tmp == null || outline <= 0) return;
        try { tmp.outlineWidth = outline; tmp.outlineColor = new Color32(0, 0, 0, 255); } catch (Exception ex) { Guard.Report(Where + ".Outline", ex); }
    }
    internal override void Unbuild() { tmp = null; appliedColour = null; base.Unbuild(); }
    private float LineH => MathF.Round(Draw.Style(style).Size * 1.35f);
    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : wrap ? -FlexWeight : tmp != null ? Draw.Width(tmp, text) + 1 : text.Length * 7;
    internal override float MeasureHeight(float width) => wrap && tmp != null ? MathF.Max(LineH, Draw.WrappedHeight(tmp, text, width)) : LineH;
    internal override void Refresh(float now)
    {
        if (colourGetter != null) ApplyColour();
        if (getter == null) return;
        var t = Guard.Get(Site(0, ".Text"), getter, text) ?? "";
        if (t == text) return;
        bool widthMayChange = wrap || Parent is Row;
        text = t; Draw.SetText(tmp, t);
        if (widthMayChange) MarkDirty();
    }
}
