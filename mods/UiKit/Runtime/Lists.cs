using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Corners = SprocketUiKit.ShapeRaster.Corners;
namespace SprocketUiKit;

/// <summary>What a list row shows; the ListView's template fills it for each item every frame.</summary>
public sealed class ListRow
{
    public string Title = "";
    public string? Subtitle, Tag, Value, Note;
    /// <summary>Lamp on/off; null = no lamp.</summary>
    public bool? Lamp;
    /// <summary>Progress 0..1 under the title line; null = none.</summary>
    public float? Progress;
    /// <summary>Value text in the accent colour (#f3cf86).</summary>
    public bool ValueAccent;
    /// <summary>Tag drawn as a chip (#2b2924) instead of plain caps text.</summary>
    public bool TagChip;
    public bool Selected, Removable;
    /// <summary>Stable identity for lamp afterglow and hover (defaults to the row index).</summary>
    public string? Key;
    /// <summary>0.1.2: colour block (10×22, outlined) before the title, "#rrggbb[aa]"; null = none.</summary>
    public string? Swatch;
    /// <summary>0.1.2: lamp colour (On/Warning/Error) when the lamp is lit.</summary>
    public LampKind? LampKind;
    /// <summary>0.1.2: accent colour: 3 px bar on the left edge and a tinted border (e.g. rarity).</summary>
    public Color? AccentColor;
    internal void Reset() { Title = ""; Subtitle = Tag = Value = Note = Key = Swatch = null; Lamp = null; Progress = null; ValueAccent = TagChip = Selected = Removable = false; LampKind = null; AccentColor = null; }
    internal bool Tall => Progress != null || Note != null;
    internal float Height => Tall ? 10 + 18 + (Progress != null ? 8 + Tok.Progress : 0) + (Note != null ? 8 + 15 : 0) + 10 : Subtitle != null ? Tok.RowTallH : Tok.RowH;
}

/// <summary>
/// List of items from a getter, rows from a template (title, subtitle, tag, lamp, value, progress,
/// note), selection, delete button and wheel scrolling. Rows are pooled uGUI objects.
/// </summary>
public sealed class ListView<T> : Element
{
    private readonly Func<IReadOnlyList<T>> items; private readonly Action<T, ListRow> template;
    private Action<T>? onSelect, onRemove;
    private string emptyText = "Nothing here yet.";
    private float lampHold, maxHeight = 320;
    private readonly List<RowView> pool = new();
    private readonly List<ListRow> rows = new();
    private readonly List<T> current = new();
    private readonly Dictionary<string, float> lampOn = new(StringComparer.Ordinal);
    private RectTransform? viewport; private ScrollBarHandle? bar; private TextMeshProUGUI? empty;
    private float scroll, contentH;
    private int lastCount = -1;
    internal ListView(Func<IReadOnlyList<T>> items, Action<T, ListRow> template) { this.items = items; this.template = template; }
    public ListView<T> OnSelect(Action<T> select) { onSelect = select; return this; }
    /// <summary>Show a delete button on rows with Removable = true.</summary>
    public ListView<T> OnRemove(Action<T> remove) { onRemove = remove; return this; }
    public ListView<T> Empty(string text) { emptyText = text; Draw.SetText(empty, text); return this; }
    /// <summary>Lamps stay lit this many seconds after going off.</summary>
    public ListView<T> LampHold(float seconds) { lampHold = seconds; return this; }
    /// <summary>Height limit when neither Height nor Fill is set (default 320); scroll beyond.</summary>
    public ListView<T> MaxHeight(float h) { maxHeight = h; MarkDirty(); return this; }

    private sealed class RowView
    {
        internal BoxView Box = null!; internal Image Lamp = null!, Glow = null!, Thumb = null!, Stripe = null!; internal BoxView Swatch = null!;
        internal TextMeshProUGUI Title = null!, Sub = null!, Tag = null!, Value = null!, Note = null!;
        internal BoxView TagBox = null!, Track = null!, Fill = null!, Del = null!; internal Image DelIcon = null!;
    }
    protected override void Build()
    {
        pool.Clear();
        viewport = Draw.Node("Viewport", Rt!); viewport.gameObject.AddComponent<RectMask2D>(); Draw.Stretch(viewport);
        empty = Draw.Text(Draw.Node("Empty", Rt!), TextStyle.Small, emptyText);
        bar = new ScrollBarHandle(() => scroll, v => { scroll = v; MarkDirty(); });
        bar.Attach(Panel, this); bar.BuildIn(Rt!);
        lastCount = -1;
    }
    internal override void Unbuild() { pool.Clear(); viewport = null; bar?.Unbuild(); bar = null; empty = null; base.Unbuild(); }
    private RowView MakeRow()
    {
        var v = new RowView();
        v.Box = new BoxView(viewport!, "Row", Tok.Radius);
        var rt = v.Box.Rt;
        v.Glow = Draw.Child(rt, "Glow", Assets.Glow(), Tok.On.WithAlpha(.55f));
        v.Lamp = Draw.Child(rt, "Lamp", Assets.Circle(), Tok.OffTrack);
        v.Title = Draw.Text(Draw.Node("Title", rt), TextStyle.Label);
        v.Sub = Draw.Text(Draw.Node("Subtitle", rt), TextStyle.Small); v.Sub.color = Draw.C(Tok.Value);
        v.TagBox = new BoxView(rt, "TagBox", Tok.RadiusSmall, Corners.All, ring: false); v.TagBox.Set(Tok.Btn, Rgba.Clear);
        v.Tag = Draw.Text(Draw.Node("Tag", rt), TextStyle.Small); v.Tag.fontSize = Tok.FontTag; v.Tag.characterSpacing = 6; v.Tag.fontStyle = FontStyles.UpperCase; v.Tag.alignment = TextAlignmentOptions.MidlineRight;
        v.Value = Draw.Text(Draw.Node("Value", rt), TextStyle.Value); v.Value.alignment = TextAlignmentOptions.MidlineRight;
        v.Track = new BoxView(rt, "Track", 2, Corners.All, ring: false); v.Track.Set(Tok.Panel, Rgba.Clear);
        v.Fill = new BoxView(rt, "Fill", 2, Corners.All, ring: false); v.Fill.Set(Tok.Accent, Rgba.Clear);
        v.Note = Draw.Text(Draw.Node("Note", rt), TextStyle.Small); v.Note.color = Draw.C(Tok.Value);
        v.Del = new BoxView(rt, "Delete", Tok.Radius);
        v.Stripe = Draw.Child(rt, "Stripe", Assets.Box(2, Corners.Left, 0), Tok.Accent, sliced: true);
        v.Swatch = new BoxView(rt, "Swatch", 2);
        v.DelIcon = Draw.Child(v.Del.Rt, "Icon", Assets.IconSprite(Icon.Trash), Tok.Text2);
        return v;
    }
    internal override float MeasureHeight(float width)
    {
        if (FillHeight) return Tok.RowH;
        return Math.Clamp(contentH, Tok.RowH, maxHeight);
    }
    internal override bool Interactive => true;
    private float Gap => Tok.SectionGap;
    internal override void Refresh(float now)
    {
        if (viewport == null) return;
        var list = Guard.Get(Where + ".Items", items, (IReadOnlyList<T>)Array.Empty<T>()) ?? Array.Empty<T>();
        current.Clear(); current.AddRange(list);
        while (rows.Count < list.Count) rows.Add(new ListRow());
        float h = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var row = rows[i]; row.Reset();
            var item = list[i];
            if (!Guard.Run(Where + ".Template", () => template(item, row))) row.Title = "(error)";
            h += row.Height + (i > 0 ? Gap : 0);
        }
        if (MathF.Abs(h - contentH) > .5f || list.Count != lastCount) { contentH = h; lastCount = list.Count; MarkDirty(); }
        while (pool.Count < list.Count) pool.Add(MakeRow());
        scroll = KitMath.ClampScroll(scroll, contentH, Rect.H);
        var pointer = Panel.LocalPointer;
        if (bar != null)
        {
            bar.Enabled = Enabled;
            if (bar.Rt != null) Draw.Place(bar.Rt, 0, 0, Rect.W, Rect.H);
            bar.LayoutBar(new RectF(0, 0, Rect.W, Rect.H), new Vec(Rect.X, Rect.Y), contentH, scroll);
            bar.Tick(now, pointer, Kit.Hover == bar, Kit.Pressed == bar);
        }
        Draw.SetActive(empty, list.Count == 0);
        if (empty != null) Draw.Place(empty.rectTransform, 4, 0, Rect.W - 8, Tok.RowH);
        bool hoverList = Hovered;
        float y = -scroll;
        for (int i = 0; i < pool.Count; i++)
        {
            var v = pool[i];
            if (i >= list.Count) { Draw.SetActive(v.Box.Rt, false); continue; }
            var row = rows[i]; float rh = row.Height;
            bool visible = y + rh > 0 && y < Rect.H;
            Draw.SetActive(v.Box.Rt, visible);
            if (visible) DrawRow(v, row, i, y, rh, hoverList && new RectF(Rect.X, Rect.Y + y, Rect.W, rh).Contains(pointer), pointer, now);
            y += rh + Gap;
        }
    }
    internal override Element? HitTest(Vec p)
    {
        if (!Visible || !Clip.Contains(p) || !Rect.Contains(p)) return null;
        if (bar != null && bar.HitTest(p) != null) return bar;
        return base.HitTest(p);
    }
    private bool CanRemove(ListRow row) => row.Removable && onRemove != null;
    private void DrawRow(RowView v, ListRow row, int index, float y, float h, bool hover, Vec pointer, float now)
    {
        float w = Rect.W - (contentH > Rect.H + .5f ? 6 : 0);
        v.Box.Place(0, y, w, h);
        bool clickable = onSelect != null;
        Rgba? accent = row.AccentColor is { } ac ? Draw.R(ac) : null;
        v.Box.Set(row.Selected ? Tok.Selected : hover && clickable ? Tok.Btn : Tok.RowBg, row.Selected ? Tok.Accent : hover && clickable ? Tok.BorderHover : accent?.WithAlpha(.6f) ?? Tok.Divider);
        Draw.SetActive(v.Stripe, accent != null);
        if (accent is { } sc) { Draw.Place(v.Stripe.rectTransform, 0, 0, Tok.StripeW, h); Draw.SetColor(v.Stripe, sc); }
        float pad = row.Tall ? 12 : 10, x = pad, lineY = row.Tall ? 10 : 0, lineH = row.Tall ? 18 : h;
        if (accent != null) x += Tok.StripeW;
        // lamp
        string key = row.Key ?? index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        bool hasLamp = row.Lamp != null;
        Draw.SetActive(v.Lamp, hasLamp);
        if (hasLamp)
        {
            lampOn.TryGetValue(key, out float last);
            bool lit = KitMath.LampLit(row.Lamp == true, now, ref last, lampHold);
            lampOn[key] = last;
            Draw.Place(v.Lamp.rectTransform, x, lineY + (lineH - Tok.Lamp) / 2, Tok.Lamp, Tok.Lamp);
            Draw.Place(v.Glow.rectTransform, x - 8, lineY + (lineH - Tok.Lamp) / 2 - 8, Tok.Lamp + 16, Tok.Lamp + 16);
            var lc = row.LampKind is { } lk ? Look.Lamp(lk) : Tok.On;
            Draw.SetColor(v.Lamp, lit ? lc : Tok.OffTrack); Draw.SetActive(v.Glow, lit);
            if (lit) Draw.SetColor(v.Glow, lc.WithAlpha(.55f));
            x += Tok.Lamp + 10;
        }
        else Draw.SetActive(v.Glow, false);
        var swatch = Draw.TryHex(row.Swatch);
        Draw.SetActive(v.Swatch.Rt, swatch != null);
        if (swatch is { } sw)
        {
            v.Swatch.Place(x, lineY + (lineH - Tok.SwatchH) / 2, Tok.SwatchW, Tok.SwatchH); v.Swatch.Set(sw, Tok.SwatchOutline);
            x += Tok.SwatchW + 8;
        }
        // right side: delete button, value, tag
        float right = w - (row.Tall ? 12 : 6);
        bool del = CanRemove(row);
        Draw.SetActive(v.Del.Rt, del);
        if (del)
        {
            var delRect = new RectF(right - 32, lineY + (lineH - 32) / 2, 32, 32); // row-local
            v.Del.Place(delRect.X, delRect.Y, 32, 32);
            Draw.Place(v.DelIcon.rectTransform, 9, 9, 14, 14);
            bool dh = hover && delRect.Offset(Rect.X, Rect.Y + y).Contains(pointer);
            v.Del.Set(dh ? Tok.Btn : Rgba.Clear, dh ? Tok.Border : Rgba.Clear);
            Draw.SetColor(v.DelIcon, dh ? Tok.Error : Tok.Text2);
            right -= 32 + 6;
        }
        Draw.SetText(v.Value, row.Value);
        float vw = string.IsNullOrEmpty(row.Value) ? 0 : Draw.Width(v.Value, row.Value!) + 2;
        Draw.SetActive(v.Value, vw > 0);
        if (vw > 0) { Draw.Place(v.Value.rectTransform, right - vw, lineY, vw, lineH); Draw.SetColor(v.Value, row.ValueAccent ? Tok.AccentText : Tok.Value); right -= vw + 8; }
        Draw.SetText(v.Tag, row.Tag);
        float tw = string.IsNullOrEmpty(row.Tag) ? 0 : Draw.Width(v.Tag, row.Tag!) + 2;
        Draw.SetActive(v.Tag, tw > 0); Draw.SetActive(v.TagBox.Rt, tw > 0 && row.TagChip);
        float titleRight = right;
        if (tw > 0)
        {
            if (row.TagChip)
            {
                // chip directly after the title (Main.dc.html outputs)
                float titleW = Draw.Width(v.Title, row.Title) + 2;
                float cx = MathF.Min(x + titleW + 8, right - tw - 14);
                v.TagBox.Place(cx, lineY + (lineH - 17) / 2, tw + 14, 17);
                Draw.Place(v.Tag.rectTransform, cx, lineY, tw + 14, lineH); v.Tag.alignment = TextAlignmentOptions.Midline; v.Tag.color = Draw.C(Tok.Value);
                titleRight = cx - 8;
            }
            else
            {
                Draw.Place(v.Tag.rectTransform, right - tw, lineY, tw, lineH); v.Tag.alignment = TextAlignmentOptions.MidlineRight; v.Tag.color = Draw.C(Tok.Faint);
                titleRight = right - tw - 8;
            }
        }
        // title / subtitle
        Draw.SetText(v.Title, row.Title);
        bool sub = row.Subtitle != null && !row.Tall;
        Draw.SetActive(v.Sub, sub);
        if (sub)
        {
            Draw.Place(v.Title.rectTransform, x, h / 2 - 17, MathF.Max(0, titleRight - x), 18);
            Draw.Place(v.Sub.rectTransform, x, h / 2 + 1, MathF.Max(0, titleRight - x), 15);
            Draw.SetText(v.Sub, row.Subtitle);
        }
        else Draw.Place(v.Title.rectTransform, x, lineY, MathF.Max(0, titleRight - x), lineH);
        // progress + note (tall rows)
        float ty = lineY + lineH;
        bool prog = row.Progress != null;
        Draw.SetActive(v.Track.Rt, prog); Draw.SetActive(v.Fill.Rt, prog && row.Progress > .001f);
        if (prog)
        {
            ty += 8;
            v.Track.Place(12, ty, w - 24, Tok.Progress);
            v.Fill.Place(12, ty, MathF.Max(Tok.Progress, (w - 24) * Math.Clamp(row.Progress!.Value, 0, 1)), Tok.Progress);
            ty += Tok.Progress;
        }
        Draw.SetActive(v.Note, row.Note != null);
        if (row.Note != null) { ty += 8; Draw.Place(v.Note.rectTransform, 12, ty, w - 24, 15); Draw.SetText(v.Note, row.Note); }
    }
    private (int Index, bool Delete) RowAt(Vec p)
    {
        float y = p.Y - Rect.Y + scroll;
        float w = Rect.W - (contentH > Rect.H + .5f ? 6 : 0);
        for (int i = 0; i < current.Count && i < rows.Count; i++)
        {
            float h = rows[i].Height;
            if (y >= 0 && y < h)
            {
                float right = w - (rows[i].Tall ? 12 : 6);
                float lineY = rows[i].Tall ? 10 : 0, lineH = rows[i].Tall ? 18 : h;
                var delRect = new RectF(right - 32, lineY + (lineH - 32) / 2, 32, 32);
                bool del = CanRemove(rows[i]) && delRect.Contains(new Vec(p.X - Rect.X, y));
                return (i, del);
            }
            y -= h + Gap;
        }
        return (-1, false);
    }
    internal override void OnClick(Vec p)
    {
        var (i, del) = RowAt(p);
        if (i < 0 || i >= current.Count) return;
        var item = current[i];
        if (del) { Guard.Run(Where + " remove", () => onRemove!(item)); return; }
        if (onSelect != null) Guard.Run(Where + " select", () => onSelect(item));
    }
    internal override bool OnScroll(float notches, Vec p)
    {
        if (contentH <= Rect.H + .5f) return false;
        scroll = KitMath.ClampScroll(scroll - notches * 40, contentH, Rect.H);
        return true;
    }
}

/// <summary>
/// Row with the list-row background (#24231c, border #3a372e, radius 4), min height 48 and a
/// variable number of children (rule rows: lamp, keywords, inline dropdowns, stepper, delete).
/// </summary>
public sealed class Card : Container
{
    private readonly Row row = new() { Gap = 8 };
    private BoxView? box; private Func<bool>? highlight; private Action? onClick;
    internal float MinH = 48;
    internal Card() { PadL = 12; PadR = 6; Children.Add(row); row.Parent = this; row.FlexWeight = 1; }
    /// <summary>The row that holds the card's content (the factories on the card add to it).</summary>
    public Row Content => row;
    public override T Add<T>(T element) => element is Row r && ReferenceEquals(r, row) ? element : row.Add(element);
    public Card Highlight(Func<bool> when) { highlight = when; return this; }
    public Card OnClick(Action click) { onClick = click; return this; }
    public Card MinHeight(float h) { MinH = h; MarkDirty(); return this; }
    private Func<string?>? stripe; private float stripeW = Tok.StripeW; private Rgba? accent; private Image? stripeImg;
    /// <summary>Left colour bar inside the radius from a getter ("#rrggbb[aa]"; null = none) (0.1.2).</summary>
    public Card Stripe(Func<string?> hex, float width = 3) { stripe = hex; stripeW = MathF.Max(1, width); return this; }
    /// <summary>Accent colour: left bar plus a tinted border, e.g. rarity (0.1.2). Keep a text that names it too.</summary>
    public Card Accent(Color colour) { accent = Draw.R(colour); return this; }
    protected override void Build()
    {
        box = new BoxView(Rt!, "Box", Tok.Radius); Draw.Stretch(box.Rt);
        stripeImg = Draw.Child(Rt!, "Stripe", Assets.Box(2, Corners.Left, 0), Tok.Accent, sliced: true); Draw.SetActive(stripeImg, false);
        base.Build();
    }
    internal override void Unbuild() { box = null; stripeImg = null; base.Unbuild(); }
    internal override float MeasureHeight(float width) => MathF.Max(MinH, row.HeightFor(width - PadL - PadR) + 12);
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        float h = row.HeightFor(r.W - PadL - PadR);
        row.Arrange(new RectF(r.X + PadL, r.Y + (r.H - h) / 2, r.W - PadL - PadR, h), new Vec(r.X, r.Y), clip.Intersect(r));
    }
    internal override bool Interactive => onClick != null || base.Interactive;
    internal override void Refresh(float now)
    {
        base.Refresh(now);
        bool hi = highlight != null && Guard.Get(Where + ".Highlight", highlight, false);
        box?.Set(onClick != null && Hovered ? Tok.Btn : Tok.RowBg, hi ? Tok.Accent : onClick != null && Hovered ? Tok.BorderHover : accent?.WithAlpha(.6f) ?? Tok.Divider);
        var bar = stripe != null ? Draw.TryHex(Guard.Get(Where + ".Stripe", stripe, null)) : accent;
        Draw.SetActive(stripeImg, bar != null);
        if (bar is { } b && stripeImg != null) { Draw.SetColor(stripeImg, b); Draw.Place(stripeImg.rectTransform, 0, 0, stripe != null ? stripeW : Tok.StripeW, Rect.H); }
    }
    internal override void OnClick(Vec p) { if (onClick != null) Guard.Run(Where + " click", onClick); }
}

/// <summary>Expandable section: chevron + heading; click the heading to open/close the body.</summary>
public sealed class Collapsible : Stack
{
    private readonly string title; private bool open;
    private readonly Stack body = new() { Gap = Tok.SectionGap };
    private Image? chevron; private TextMeshProUGUI? head;
    internal Collapsible(string title, bool open)
    {
        this.title = title; this.open = open; Gap = 6;
        Children.Add(body); body.Parent = this; body.Visible = open;
        body.VisibleGetter = () => this.open;
    }
    public Stack Body => body;
    public bool IsExpanded { get => open; set { open = value; MarkDirty(); } }
    protected override void Build()
    {
        chevron = Draw.Child(Rt!, "Chevron", Assets.IconSprite(open ? Icon.ChevronDown : Icon.ChevronRight), Tok.Heading);
        head = Draw.Text(Draw.Node("Heading", Rt!), TextStyle.Section, title);
        base.Build();
    }
    internal override void Unbuild() { chevron = null; head = null; base.Unbuild(); }
    private const float HeadH = 28;
    internal override float MeasureHeight(float width) => HeadH + (open ? Gap + body.HeightFor(width) : 0);
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        Rect = r; Clip = clip;
        if (Rt != null) Draw.Place(Rt, r, origin);
        if (chevron != null) Draw.Place(chevron.rectTransform, 0, (HeadH - 12) / 2, 12, 12);
        if (head != null) Draw.Place(head.rectTransform, 20, 0, MathF.Max(0, r.W - 20), HeadH);
        if (open) body.Arrange(new RectF(r.X, r.Y + HeadH + Gap, r.W, MathF.Max(0, r.H - HeadH - Gap)), new Vec(r.X, r.Y), clip.Intersect(r));
    }
    internal override Element? HitTest(Vec p)
    {
        if (!Visible || !Clip.Contains(p) || !Rect.Contains(p)) return null;
        if (p.Y < Rect.Y + HeadH) return this;
        return body.HitTest(p);
    }
    internal override bool Interactive => true;
    internal override void Refresh(float now)
    {
        base.Refresh(now);
        if (chevron != null) { var s = Assets.IconSprite(open ? Icon.ChevronDown : Icon.ChevronRight); if (chevron.sprite != s) chevron.sprite = s; }
        Draw.SetColor(head, Hovered ? Tok.AccentText : Tok.Heading);
    }
    internal override void OnClick(Vec p) { if (p.Y < Rect.Y + HeadH) { open = !open; MarkDirty(); } }
}

/// <summary>Vertical stack that clips and scrolls with the mouse wheel; give it Fill() or Height().</summary>
public sealed class ScrollView : Stack
{
    private float scroll, contentH;
    private RectTransform? viewport, content; private ScrollBarHandle? bar;
    internal ScrollView() { Gap = Tok.SectionGap; }
    internal override RectTransform? ChildRoot => content;
    protected override void Build()
    {
        viewport = Draw.Node("Viewport", Rt!); viewport.gameObject.AddComponent<RectMask2D>(); Draw.Stretch(viewport);
        content = Draw.Node("Content", viewport);
        base.Build();
        bar = new ScrollBarHandle(() => scroll, v => { scroll = v; MarkDirty(); });
        bar.Attach(Panel, this); bar.BuildIn(Rt!);
    }
    internal override void Unbuild() { viewport = content = null; bar?.Unbuild(); bar = null; base.Unbuild(); }
    internal override float MeasureHeight(float width) => MathF.Min(base.MeasureHeight(width), FillHeight ? Tok.RowH : 10000);
    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        Rect = r; Clip = clip;
        if (Rt != null) Draw.Place(Rt, r, origin);
        contentH = base.MeasureHeight(r.W - 6);
        scroll = KitMath.ClampScroll(scroll, contentH, r.H);
        bool over = contentH > r.H + .5f;
        float w = r.W - (over ? 6 : 0);
        if (content != null) Draw.Place(content, 0, 0, w, contentH);
        // children are parented to Content (at the viewport top-left): origin = (r.X, r.Y)
        // Child rects are their true (scrolled) panel positions; Content sits at the viewport origin.
        var kids = VisibleChildren.ToList(); float y = r.Y + PadT - scroll;
        foreach (var k in kids)
        {
            float h = k.HeightFor(w - PadL - PadR);
            k.Arrange(new RectF(r.X + PadL, y, w - PadL - PadR, h), new Vec(r.X, r.Y), clip.Intersect(r));
            y += h + Gap;
        }
        if (bar != null)
        {
            if (bar.Rt != null) Draw.Place(bar.Rt, 0, 0, r.W, r.H);
            bar.LayoutBar(new RectF(0, 0, r.W, r.H), new Vec(r.X, r.Y), contentH, scroll);
        }
    }
    internal override void Refresh(float now)
    {
        base.Refresh(now);
        if (bar != null) { bar.Enabled = Enabled; bar.Tick(now, Panel.LocalPointer, Kit.Hover == bar, Kit.Pressed == bar); }
    }
    internal override Element? HitTest(Vec p)
    {
        if (!Visible || !Clip.Contains(p) || !Rect.Contains(p)) return null;
        if (bar != null && bar.HitTest(p) != null) return bar;
        return base.HitTest(p);
    }
    internal override bool Interactive => false;
    internal override bool OnScroll(float notches, Vec p)
    {
        if (contentH <= Rect.H + .5f) return false;
        scroll = KitMath.ClampScroll(scroll - notches * 40, contentH, Rect.H);
        MarkDirty();
        return true;
    }
}

/// <summary>
/// One block of elements per item, rebuilt when the item list changes (by item equality). Use for
/// rows with a variable number of controls, e.g. one rule Card per rule.
/// </summary>
public sealed class Repeat<T> : Stack
{
    private readonly Func<IReadOnlyList<T>> items; private readonly Action<T, Stack> build;
    private readonly List<T> built = new();
    private bool initial = true;
    internal Repeat(Func<IReadOnlyList<T>> items, Action<T, Stack> build, float gap) { this.items = items; this.build = build; Gap = gap; }
    internal override void Refresh(float now)
    {
        var list = Guard.Get(Where + ".Items", items, (IReadOnlyList<T>)Array.Empty<T>()) ?? Array.Empty<T>();
        if (initial || list.Count != built.Count || !list.SequenceEqual(built))
        {
            initial = false;
            foreach (var c in Children.ToList()) Remove(c);
            built.Clear(); built.AddRange(list);
            foreach (var item in list)
            {
                var s = Add(new Stack { Gap = Tok.SectionGap });
                Guard.Run(Where + ".Build", () => build(item, s));
            }
            MarkDirty();
        }
        base.Refresh(now);
    }
}
