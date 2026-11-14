using UnityEngine;
using UnityEngine.UI;
using Corners = SprocketUiKit.ShapeRaster.Corners;
namespace SprocketUiKit;

/// <summary>
/// The one runtime scrollbar (0.1.5): geometry/visuals plus drag and track-paging state. ScrollView,
/// ListView and PopupList each own one; only the press routing differs. No per-frame allocation.
/// </summary>
internal sealed class ScrollBar
{
    private readonly Func<float> getScroll;
    private readonly Action<float> setScroll;
    private BoxView? track; private Image? thumb;
    private RectF view = RectF.Empty; private Vec origin;
    private float contentH, viewH, scroll, rightInset;
    private float thumbY, thumbLen;
    private bool hover, dragging, paging;
    private int pageDir;
    private float dragStartScroll, dragStartY, dragTravel, nextRepeat;

    internal ScrollBar(Func<float> getScroll, Action<float> setScroll) { this.getScroll = getScroll; this.setScroll = setScroll; }

    internal void Build(RectTransform parent)
    {
        track = new BoxView(parent, "ScrollTrack", 2, Corners.All, ring: false);
        thumb = Draw.Child(parent, "ScrollThumb", Assets.Box(2, Corners.All, 0), Tok.ScrollThumb, sliced: true);
        Draw.SetActive(track.Rt, false);
        Draw.SetActive(thumb, false);
    }
    internal void Unbuild() { track = null; thumb = null; }

    /// <summary>Place the bar in parent-local coordinates; <paramref name="origin"/> maps them to pointer space.</summary>
    internal void Layout(RectF viewLocal, Vec origin, float contentHeight, float scrollOffset, float rightInset = 0)
    {
        view = viewLocal; this.origin = origin; contentH = contentHeight; viewH = viewLocal.H; this.rightInset = rightInset;
        scroll = Math.Clamp(scrollOffset, 0, ScrollBarMath.MaxScroll(contentH, viewH));
        UpdateThumb();
        Visual();
    }

    private bool Over => ScrollBarMath.Overflow(contentH, viewH);

    internal bool Hit(Vec p)
    {
        if (!Over) return false;
        Vec local = p - origin;
        return ScrollBarMath.InStrip(local.X, view.Right, true) && local.Y >= view.Y && local.Y < view.Bottom;
    }

    private ScrollPart PartAt(Vec p)
    {
        if (!Hit(p)) return ScrollPart.None;
        Vec local = p - origin;
        return ScrollBarMath.PartAt(local.Y, view.Y, contentH, viewH, scroll);
    }

    /// <summary>Start a thumb drag or a track page on press; false when the point is not on the bar.</summary>
    internal bool OnPress(Vec p, float now)
    {
        var part = PartAt(p);
        if (part == ScrollPart.Thumb)
        {
            dragging = true; paging = false;
            dragStartScroll = Math.Clamp(getScroll(), 0, ScrollBarMath.MaxScroll(contentH, viewH));
            scroll = dragStartScroll; UpdateThumb();
            dragStartY = p.Y;
            dragTravel = MathF.Max(0, viewH - thumbLen);
            return true;
        }
        if (part == ScrollPart.Above || part == ScrollPart.Below)
        {
            paging = true; dragging = false;
            pageDir = part == ScrollPart.Above ? -1 : 1;
            scroll = Math.Clamp(getScroll(), 0, ScrollBarMath.MaxScroll(contentH, viewH));
            Apply(Page(p.Y));
            nextRepeat = now + KeyRepeat.Delay;
            return true;
        }
        return false;
    }

    /// <summary>Advance a held drag/page gesture. Returns true when the scroll offset changed.</summary>
    internal bool Tick(float now, Vec pointer, bool hovered, bool held)
    {
        hover = hovered;
        bool changed = false;
        if (!held) { dragging = false; paging = false; }
        else if (dragging)
        {
            changed = Apply(ScrollBarMath.DragTo(dragStartScroll, pointer.Y - dragStartY, dragTravel, contentH, viewH));
        }
        else if (paging)
        {
            var part = PartAt(pointer);
            if (part == ScrollPart.None || part == ScrollPart.Thumb) paging = false;
            else
            {
                pageDir = part == ScrollPart.Above ? -1 : 1;
                if (now >= nextRepeat)
                {
                    changed = Apply(Page(pointer.Y));
                    nextRepeat = now + KeyRepeat.StepRate;
                }
            }
        }
        Visual();
        return changed;
    }

    internal void Cancel() { dragging = false; paging = false; }

    private float Page(float pointerY) => ScrollBarMath.PageTarget(scroll, contentH, viewH, pointerY, view.Y, pageDir);

    private bool Apply(float target)
    {
        float next = Math.Clamp(target, 0, ScrollBarMath.MaxScroll(contentH, viewH));
        if (MathF.Abs(next - scroll) < .01f) return false;
        scroll = next;
        UpdateThumb();
        setScroll(next);
        return true;
    }
    private void UpdateThumb()
    {
        var t = ScrollBarMath.Thumb(scroll, contentH, viewH);
        thumbY = view.Y + t.Offset;
        thumbLen = t.Length;
    }
    private void Visual()
    {
        bool over = Over;
        bool active = over && (hover || dragging || paging);
        Draw.SetActive(track?.Rt, active);
        Draw.SetActive(thumb, over);
        if (!over || thumb == null) return;
        float w = active ? Tok.ScrollWideW : Tok.ScrollW;
        float right = view.Right - rightInset;
        Draw.Place(thumb.rectTransform, right - w, thumbY, w, thumbLen);
        Draw.SetColor(thumb, dragging || paging ? Tok.ScrollThumbDrag : hover ? Tok.ScrollThumbHover : Tok.ScrollThumb);
        if (track != null && active)
        {
            track.Place(right - Tok.ScrollWideW, view.Y, Tok.ScrollWideW, viewH);
            track.Set(Tok.ScrollTrack, Rgba.Clear);
        }
    }
}

/// <summary>
/// Interactive strip for a <see cref="ScrollBar"/> inside a panel. It is built next to the content
/// (not in the layout children), so it never changes the 0.1.4 measurements; the owner's HitTest
/// offers it first when the content overflows.
/// </summary>
internal sealed class ScrollBarHandle : Element
{
    internal readonly ScrollBar Bar;
    internal ScrollBarHandle(Func<float> getScroll, Action<float> setScroll) { Bar = new ScrollBar(getScroll, setScroll); }
    protected override void Build() => Bar.Build(Rt!);
    internal override void Unbuild() { Bar.Unbuild(); base.Unbuild(); }
    internal override bool Interactive => true;
    internal override Element? HitTest(Vec p) => Bar.Hit(p) ? this : null;
    internal override void OnPress(Vec p) => Bar.OnPress(p, Kit.Now);
    internal override void OnClick(Vec p) { }
    internal override float MeasureHeight(float width) => 0;
    internal override float IntrinsicWidth() => 0;
    internal void LayoutBar(RectF viewLocal, Vec origin, float contentHeight, float scroll, float rightInset = 0) =>
        Bar.Layout(viewLocal, origin, contentHeight, scroll, rightInset);
    internal void Tick(float now, Vec pointer, bool hovered, bool held) => Bar.Tick(now, pointer, hovered, held);
}
