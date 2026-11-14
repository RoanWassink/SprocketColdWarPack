using SprocketKeybinds;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Corners = SprocketUiKit.ShapeRaster.Corners;
namespace SprocketUiKit;

/// <summary>
/// A draggable window in the kit style: title bar (icon, title, subtitle, context chip, drag hint,
/// close), body (columns/stacks/components) and footer (status line + key hints). Create with
/// <see cref="UiKit.CreatePanel"/>; build the content once, values update through getters.
/// </summary>
public sealed class Panel
{
    public string ModGuid { get; }
    public string Id { get; }
    internal string Key { get; }
    private string title, subtitle;
    /// <summary>Content area below the title bar; Columns/Stack/components go here.</summary>
    public Stack Body { get; }
    /// <summary>Raised after the panel closed (Esc, close button, keybind, context change...).</summary>
    public event Action? OnClose;
    /// <summary>Raised after the panel opened.</summary>
    public event Action? OnOpen;
    /// <summary>Contexts in which the panel may be open (default designer and game).</summary>
    public UiContexts AllowedContexts { get; set; } = UiContexts.DesignerAndGame;
    /// <summary>In game: free the cursor and block firing/camera while open (default true).</summary>
    public bool BlockGameplayInput { get; set; } = true;
    /// <summary>Close when the loaded scenes change (map load, leaving the designer). Default true.</summary>
    public bool CloseOnSceneChange { get; set; } = true;
    /// <summary>Show the default key hints (bound toggle key, Esc) in the footer.</summary>
    public bool DefaultKeyHints { get; set; } = true;
    public Icon TitleIcon { get; set; } = Icon.None;

    // ---------- 0.1.2 behaviour switches ----------
    /// <summary>Stay open while the game's pause menu is open (default false: the pause menu closes panels).</summary>
    public bool StayOpenWhilePaused { get; set; }
    /// <summary>
    /// False = no close button; Esc (after going back through sub-pages) and the toggle keybind do not
    /// close it. Code can still call <see cref="Close()"/>; pause and scene change still close it.
    /// </summary>
    public bool Closable { get => closable; set { closable = value; dirty = true; } }
    private bool closable = true;
    /// <summary>
    /// Asked before Esc, the close button or the toggle keybind close the panel; return false to stay
    /// open (e.g. "unsaved changes" confirm). Not asked for <see cref="Close()"/>, pause or scene change.
    /// </summary>
    public Func<bool>? OnCloseRequest { get; set; }

    internal readonly bool IsHud;
    internal float X, Y, W, H;
    private bool positioned;
    internal bool IsOpenInternal;
    public bool IsOpen => IsOpenInternal;
    /// <summary>Whether this panel owns keyboard focus, acquired on open/inside press and released or handed on by outside press, close or lifecycle changes (see README.md).</summary>
    public bool HasFocus => Kit.KeyboardFocus == this;
    internal bool Built;
    private bool dirty = true;
    internal ModKeybind? ToggleKey;
    private Func<string?>? contextGetter = () => UiKit.ContextLabel;
    private string statusText = ""; private StatusKind statusKind;
    private readonly List<(string Key, string Action)> hints = new();
    private string hintSignature = "";
    private float nextHintCheck;

    // visuals
    internal RectTransform? Rt;
    private RectTransform? bodyClip;
    private BoxView? frame, header, footer;
    private Image? headerLine, footerLine, iconImg, statusIcon, backdropImg;
    private TextMeshProUGUI? titleText, subtitleText, dragHint, statusLabel;
    private readonly IconButton close, back, maxButton;
    private readonly Chip contextChip;
    private readonly List<(BoxView Cap, TextMeshProUGUI Key, TextMeshProUGUI Action)> hintViews = new();
    private Image? gripImg, gripZone;

    internal Panel(string modGuid, string id, string title, string? subtitle, float width, float height, bool hud = false)
    {
        ModGuid = modGuid; Id = id; Key = modGuid + "/" + id; this.title = title; this.subtitle = subtitle ?? "";
        W = width; H = height; IsHud = hud;
        baseW = width; baseH = height;
        maxButton = new IconButton(Icon.Maximize, () => ToggleMaximize());
        maxButton.Attach(this, null); maxButton.TooltipText = null;
        Body = new Stack { Gap = 0 };
        Body.Attach(this, null);
        close = new IconButton(Icon.Close, () => RequestClose("closed (close button)"));
        close.Attach(this, null); close.TooltipText = null;
        back = new IconButton(Icon.ChevronLeft, () => Pop());
        back.Attach(this, null); back.TooltipText = "Back";
        contextChip = new Chip("", () => Guard.Get(Key + ".Context", contextGetter, null) ?? "", ChipStyle.Context);
        contextChip.Attach(this, null);
    }

    public string Title { get => title; set { title = value ?? ""; Draw.SetText(titleText, title); dirty = true; } }
    public string Subtitle { get => subtitle; set { subtitle = value ?? ""; dirty = true; } }
    /// <summary>
    /// Panel size in reference px (1080p). Since 0.1.4 this size is also the minimum for the user's
    /// resizing (unless <see cref="MinSize"/> is set), and a panel that grows is kept on screen.
    /// </summary>
    public void SetSize(float width, float height)
    {
        float w = MathF.Max(240, width), h = MathF.Max(120, height);
        baseW = w; baseH = h;
        if (maximized) { restoreRect = new RectF(restoreRect.X, restoreRect.Y, w, h); dirty = true; return; }
        bool grew = w > W + .01f || h > H + .01f;
        ApplySize(w, h);
        dirty = true; if (Rt != null) Draw.Place(Rt, X, Y, W, H);
        if (grew && positioned) fitPending = true;
    }
    public float Width => W;
    public float Height => H;
    /// <summary>Context chip text (null/empty hides it). Default: "DESIGNER" / "IN GAME".</summary>
    public void SetContext(string? fixedText) => contextGetter = () => fixedText;
    public void SetContext(Func<string?>? getter) => contextGetter = getter;
    /// <summary>Status line in the footer (info / success ✓ / warning / error).</summary>
    public void SetStatus(string? text, StatusKind kind = StatusKind.Info) { statusText = text ?? ""; statusKind = kind; ApplyStatus(); }
    public string StatusText => statusText;
    /// <summary>Extra key hint in the footer, e.g. ("Del", "remove").</summary>
    public void AddKeyHint(string key, string action) { hints.Add((key, action)); hintSignature = ""; }
    public void ClearKeyHints() { hints.Clear(); hintSignature = ""; }

    /// <summary>Side-by-side columns filling the body. Width &gt; 0 = px, 0 = flexible, &lt; 0 = flexible weight.</summary>
    public Column[] Columns(params float[] widths) => Body.Columns(widths);

    public bool Open()
    {
        if (IsHud) return false;
        if (IsOpenInternal) { Kit.BringToFront(this); return true; }
        if ((AllowedContexts & Kit.Context) == 0)
        {
            if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key}: not opened in context {Kit.Context} (allowed {AllowedContexts}).");
            return false;
        }
        IsOpenInternal = true; dirty = true;
        if (positioned) fitPending = true; // #23: back on screen if it was left (partly) off it
        Kit.Opened(this);
        Guard.Run(Key + ".OnOpen", OnOpen);
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key} opened in {Kit.Context}; scale {Kit.Scale:0.###}; open panels {Kit.OpenCount}.");
        return true;
    }
    public void Close() => Close("closed");
    internal void Close(string reason)
    {
        if (!IsOpenInternal) return;
        IsOpenInternal = false;
        if (modal != null) { var m = modal; DropModal(); if (m.OnCancel != null) Guard.Run(Key + ".Modal.OnCancel", m.OnCancel); }
        Kit.Closed(this);
        if (Rt != null) Rt.gameObject.SetActive(false);
        Guard.Run(Key + ".OnClose", OnClose);
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key} {reason}.");
    }
    /// <summary>User-initiated close (Esc, close button, keybind): respects Closable and OnCloseRequest.</summary>
    internal bool RequestClose(string reason)
    {
        if (!IsOpenInternal) return true;
        if (!closable) { if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key}: close ignored (Closable = false)."); return false; }
        if (!Logic.MayClose(true, OnCloseRequest, Key + ".OnCloseRequest")) { if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key}: close refused by OnCloseRequest."); return false; }
        Close(reason); return true;
    }
    /// <summary>
    /// Close as the user would (0.1.4, #17), e.g. a "Close" button in the footer: respects
    /// <see cref="Closable"/> and <see cref="OnCloseRequest"/> like Esc and the close button.
    /// True when the panel is closed afterwards (also when it was not open).
    /// </summary>
    public bool RequestClose() => RequestClose("closed (RequestClose)");
    public void Toggle() { if (IsOpenInternal) Close("closed (toggle)"); else Open(); }
    internal void ToggleByKey() { if (IsOpenInternal) RequestClose("closed (toggle key)"); else Open(); }

    // ---------- 0.1.2: sub-pages ----------
    private readonly Dictionary<string, (Stack Root, string Title)> pages = new(StringComparer.Ordinal);
    private readonly PageStack nav = new();
    /// <summary>Raised after <see cref="Push"/>/<see cref="Pop"/> changed the current page.</summary>
    public event Action? PageChanged;
    /// <summary>
    /// Sub-page (built once, like <see cref="Body"/>, which is the root page). Show it with
    /// <see cref="Push"/>; the title bar then shows a back button and the page trail, and Esc goes back
    /// one page before it closes the panel. Calling it again with the same id returns the same page.
    /// </summary>
    public Stack Page(string id, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Page id is required.", nameof(id));
        if (pages.TryGetValue(id, out var p)) return p.Root;
        var root = new Stack { Gap = 0 };
        root.Attach(this, null); root.Visible = false;
        pages[id] = (root, string.IsNullOrEmpty(title) ? id : title!);
        if (Built && bodyClip != null) { root.BuildIn(bodyClip); Draw.SetActive(root.Rt, false); }
        return root;
    }
    /// <summary>Show page <paramref name="id"/> (an id already in the trail goes back to it). Unknown ids throw.</summary>
    public void Push(string id)
    {
        if (!pages.ContainsKey(id)) throw new KeyNotFoundException($"Panel {Key} has no page '{id}' (create it with Page(id) first).");
        Navigate(id);
    }
    /// <summary>One page back; false at the root page.</summary>
    public bool Pop() { var before = nav.Current; if (!nav.Back()) return false; SwapPage(before); return true; }
    /// <summary>Back to the root page (Body).</summary>
    public void PopToRoot() { var before = nav.Current; if (nav.Navigate(null)) SwapPage(before); }
    /// <summary>Current page id; null = the root page (Body).</summary>
    public string? CurrentPage => nav.Current;
    /// <summary>0 at the root page, 1 on a page pushed from it, ...</summary>
    public int PageDepth => nav.Depth;
    private void Navigate(string id) { var before = nav.Current; if (nav.Navigate(id)) SwapPage(before); }
    private void SwapPage(string? before)
    {
        var old = RootFor(before);
        old.Visible = false; Draw.SetActive(old.Rt, false);
        var now = ContentRoot; now.Visible = true; Draw.SetActive(now.Rt, true);
        Kit.LeftSubtree(this);
        dirty = true;
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key} page -> {nav.Current ?? "(root)"} (depth {nav.Depth}).");
        Guard.Run(Key + ".PageChanged", PageChanged);
    }
    private Stack RootFor(string? id) => id != null && pages.TryGetValue(id, out var p) ? p.Root : Body;
    /// <summary>The root that is shown: the current page, or Body.</summary>
    internal Stack ContentRoot => RootFor(nav.Current);
    private string Trail() => string.Join("  /  ", nav.Path.Select(id => pages.TryGetValue(id, out var p) ? p.Title : id));

    // ---------- 0.1.2: modal ----------
    private ModalView? modal;
    public bool ModalOpen => modal != null;
    internal ModalView? Modal => modal;
    /// <summary>
    /// Modal card over the panel body (dimmed; clicks underneath blocked; the title bar stays
    /// draggable). <paramref name="build"/> fills the card. Esc closes it. Replaces an open modal.
    /// </summary>
    public void ShowModal(string title, Action<Stack> build, float width = 440)
    {
        var m = new ModalView(this, title, width, false, false, null, null);
        Guard.Run(Key + ".ShowModal", () => build(m.Content));
        SetModal(m);
    }
    /// <summary>
    /// Confirmation: title, message, [cancel] [confirm]. Esc cancels. Enter confirms, except for a
    /// <see cref="ButtonStyle.Danger"/> confirm, where Enter cancels (the safe default).
    /// </summary>
    public void Confirm(string title, string body, string confirmText, Action onConfirm,
                        ButtonStyle confirmStyle = ButtonStyle.Danger, string cancelText = "Back", Action? onCancel = null)
    {
        var m = new ModalView(this, title, Tok.ModalW, true, confirmStyle == ButtonStyle.Danger, onConfirm, onCancel);
        if (!string.IsNullOrEmpty(body)) m.Content.Add(new Label(body, null, TextStyle.Body)).Wrap();
        var row = m.Content.Row(8);
        row.Spacer();
        row.Button(cancelText, () => CancelModal());
        row.Button(confirmText, () => ConfirmModal(), confirmStyle);
        SetModal(m);
    }
    /// <summary>Close the modal without calling its callbacks.</summary>
    public void CloseModal() => DropModal();
    private void SetModal(ModalView m)
    {
        DropModal();
        modal = m;
        Kit.LeftSubtree(this);
        if (Built && Rt != null) m.Build(Rt);
        dirty = true;
    }
    private void DropModal()
    {
        var m = modal; if (m == null) return;
        modal = null;
        Kit.LeftSubtree(this);
        m.Destroy();
        dirty = true;
    }
    internal void CancelModal() { var m = modal; if (m == null) return; DropModal(); if (m.OnCancel != null) Guard.Run(Key + ".Modal.OnCancel", m.OnCancel); }
    internal void ConfirmModal() { var m = modal; if (m == null) return; DropModal(); if (m.OnConfirm != null) Guard.Run(Key + ".Modal.OnConfirm", m.OnConfirm); }
    internal void ModalEnter()
    {
        var m = modal; if (m == null || !m.IsConfirm) return;
        if (Logic.ConfirmKey(true, false, m.Danger) == ModalKey.Confirm) ConfirmModal(); else CancelModal();
    }

    // ---------- 0.1.2: shortcuts ----------
    internal readonly List<(Logic.Chord Chord, Key Key, Action Action)> Shortcuts = new();
    internal Action? DefaultActionHandler;
    /// <summary>
    /// Key chord for this panel while it has keyboard focus, e.g. "Ctrl+D", "Delete", "Alt+UpArrow", "Ctrl+S" (not while typing,
    /// not while a modal is open). <paramref name="hint"/> adds a footer key hint. Invalid chords throw.
    /// Game bindings are blocked while focused when Input.BlockKeysWhilePanelFocused is enabled.
    /// </summary>
    public void AddShortcut(string chord, Action action, string? hint = null)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        if (!Logic.TryParseChord(chord, out var c) || !Enum.TryParse<Key>(c.Key, true, out var key)) throw new ArgumentException($"Unknown key chord '{chord}'.", nameof(chord));
        Shortcuts.Add((c, key, action));
        if (!string.IsNullOrEmpty(hint)) AddKeyHint(c.ToString(), hint!);
    }
    /// <summary>Enter while this panel has keyboard focus (no text field focused, no modal) runs this, e.g. "Save".</summary>
    public void DefaultAction(Action action) => DefaultActionHandler = action;

    // ---------- 0.1.2: full-screen backdrop ----------
    private float backdropAlpha;
    /// <summary>
    /// Full-screen dim layer under this panel that catches all clicks (game, designer and other
    /// panels underneath); 0 = off. For mandatory full-screen menus.
    /// </summary>
    public Panel Backdrop(float alpha = .6f) { backdropAlpha = Math.Clamp(alpha, 0, 1); if (backdropImg != null) { Draw.SetColor(backdropImg, new Rgba(0, 0, 0, (byte)(backdropAlpha * 255))); Draw.SetActive(backdropImg, backdropAlpha > 0); } return this; }
    public bool HasBackdrop => backdropAlpha > 0;

    // ---------- 0.1.2: errors ----------
    /// <summary>True when any visible element on the shown page (or modal) has a field error.</summary>
    public bool HasErrors => FirstError != null;
    /// <summary>First visible element with a field error, in layout order, and its message.</summary>
    public (Element Element, string Message)? FirstError
    {
        get
        {
            if (modal != null && Find(modal.Content) is { } m) return m;
            return Find(ContentRoot);
            static (Element, string)? Find(Element e)
            {
                if (!e.Visible) return null;
                if (e.CurrentError() is { } msg) return (e, msg);
                foreach (var k in e.Kids) if (Find(k) is { } hit) return hit;
                return null;
            }
        }
    }

    // ---------- 0.1.4: maximize and resize (#24, #25), back on screen (#23) ----------
    private float baseW, baseH;          // size from CreatePanel or the last SetSize from code
    private float minW = -1, minH = -1;  // MinSize(w, h); < 0 = the base size
    private bool resizable = true, maximizable = true, maximized;
    private RectF restoreRect;
    private bool fitPending, sizeChanged;
    internal readonly DoubleClick TitleClicks = new();
    /// <summary>
    /// The user can resize the panel with the right edge, the bottom edge and the corner grip (default
    /// true; never for HUDs). The size is remembered per panel.
    /// </summary>
    public bool Resizable { get => resizable; set { resizable = value; dirty = true; } }
    /// <summary>
    /// Maximize button left of the close button and double-click on the title bar (default true; never
    /// for HUDs). Setting false while maximized restores the panel.
    /// </summary>
    public bool Maximizable { get => maximizable; set { maximizable = value; if (!value && maximized) Restore(); dirty = true; } }
    /// <summary>True while the panel fills the screen (minus an 8 px margin).</summary>
    public bool IsMaximized => maximized;
    /// <summary>Raised after the size changed (resize, maximize, restore, SetSize, screen change); at most once per frame.</summary>
    public event Action? Resized;
    /// <summary>
    /// Smallest size the user can resize to (reference px). Default: the size from CreatePanel or the
    /// last SetSize. The screen wins: a panel is never larger than the screen.
    /// </summary>
    public Panel MinSize(float width, float height)
    {
        minW = MathF.Max(240, width); minH = MathF.Max(120, height);
        if (positioned && !maximized && (W < minW || H < minH))
        {
            var (w, h) = Window.ClampSize(W, H, minW, minH, Kit.ViewW, Kit.ViewH);
            ApplySize(w, h); fitPending = true;
        }
        return this;
    }
    /// <summary>Width gained over the original size (resize/maximize); 0 at the original size.</summary>
    internal float GrowW => MathF.Max(0, W - baseW);
    internal float MinW => minW >= 0 ? minW : baseW;
    internal float MinH => minH >= 0 ? minH : baseH;
    internal bool CanMaximize => maximizable && !IsHud;
    internal bool CanResize => resizable && !IsHud && !maximized;
    internal bool FitPending => fitPending;

    /// <summary>Fill the visible screen minus <c>8</c> px; <see cref="Restore"/> goes back. No-op when not <see cref="Maximizable"/>.</summary>
    public void Maximize()
    {
        if (!CanMaximize || maximized) return;
        restoreRect = new RectF(X, Y, W, H);
        maximized = true; TitleClicks.Reset();
        if (positioned) { ApplyMaximized(); Kit.SavePosition(this); }
        dirty = true;
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key} maximized ({W:0}x{H:0}; restore {restoreRect}).");
    }
    /// <summary>Back to the size and place from before <see cref="Maximize"/>.</summary>
    public void Restore()
    {
        if (!maximized) return;
        maximized = false; TitleClicks.Reset();
        var (w, h) = Window.ClampSize(restoreRect.W, restoreRect.H, MinW, MinH, Kit.ViewW, Kit.ViewH);
        X = restoreRect.X; Y = restoreRect.Y; ApplySize(w, h); ClampPosition();
        if (positioned) Kit.SavePosition(this);
        dirty = true;
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key} restored to {ScreenRect}.");
    }
    public void ToggleMaximize() { if (maximized) Restore(); else Maximize(); }
    /// <summary>
    /// Centre the panel at its original size (CreatePanel / last SetSize), not maximized, and forget the
    /// remembered position and size (#23).
    /// </summary>
    public void ResetPosition()
    {
        maximized = false; TitleClicks.Reset();
        Kit.ForgetPosition(this);
        if (!positioned) return;
        var (w, h) = Window.ClampSize(baseW, baseH, 0, 0, Kit.ViewW, Kit.ViewH);
        ApplySize(w, h);
        var c = KitMath.Centre(W, H, Kit.ViewW, Kit.ViewH); X = c.X; Y = c.Y;
        ClampPosition(); dirty = true;
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key} position reset to {ScreenRect}.");
    }
    /// <summary>Size change by the kit (resize, maximize, restore, fit): does not change the base/minimum size.</summary>
    internal void ApplySize(float w, float h)
    {
        if (MathF.Abs(w - W) < .01f && MathF.Abs(h - H) < .01f) return;
        W = w; H = h; dirty = true; sizeChanged = true;
        if (Rt != null) Draw.Place(Rt, X, Y, W, H);
        PlaceBackdrop();
    }
    private void ApplyMaximized()
    {
        var r = Window.Maximized(Kit.ViewW, Kit.ViewH);
        X = r.X; Y = r.Y; ApplySize(r.W, r.H);
        if (Rt != null) Draw.Place(Rt, X, Y, W, H);
        PlaceBackdrop();
    }
    /// <summary>Title bar drag from the maximized state: restore under the pointer (like Windows).</summary>
    internal void RestoreForDrag(Vec pointer)
    {
        if (!maximized) return;
        var (w, h) = Window.ClampSize(restoreRect.W, restoreRect.H, MinW, MinH, Kit.ViewW, Kit.ViewH);
        var r = Window.RestoreUnderPointer(pointer, ScreenRect, w, h);
        maximized = false; TitleClicks.Reset();
        X = r.X; Y = r.Y; ApplySize(r.W, r.H); ClampPosition(); dirty = true;
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key} restored by dragging the title bar ({ScreenRect}).");
    }
    internal ResizeEdge GripAt(Vec local) => CanResize ? Window.GripAt(local, W, H) : ResizeEdge.None;
    /// <summary>#23: on open and after growing, a panel (partly) off screen comes back; too large shrinks to the screen.</summary>
    internal void FitOnScreen()
    {
        fitPending = false;
        if (IsHud || !positioned) return;
        if (maximized) { ApplyMaximized(); return; }
        var r = ScreenRect;
        if (Window.FullyOnScreen(r, Kit.ViewW, Kit.ViewH)) return;
        var f = Window.FitOnScreen(r, Kit.ViewW, Kit.ViewH);
        X = f.X; Y = f.Y; ApplySize(f.W, f.H); ClampPosition();
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] {Key} moved back on screen: {r} -> {ScreenRect} (view {Kit.ViewW:0}x{Kit.ViewH:0}).");
    }
    /// <summary>Screen size or UI scale changed: a maximized panel keeps filling the screen.</summary>
    internal void ViewChanged()
    {
        if (maximized && positioned) ApplyMaximized();
        ClampPosition(); dirty = true;
    }
    /// <summary>What .panels.cfg remembers: position, plus size when resized, plus the restore rectangle when maximized.</summary>
    internal PanelState State =>
        maximized ? new PanelState(restoreRect.X, restoreRect.Y, restoreRect.W, restoreRect.H, true)
        : MathF.Abs(W - baseW) > .5f || MathF.Abs(H - baseH) > .5f ? new PanelState(X, Y, W, H) : new PanelState(X, Y);

    internal void MarkDirty() => dirty = true;
    internal RectF ScreenRect => new(X, Y, W, H);
    internal RectF ToScreen(RectF local) => local.Offset(X, Y);
    internal Vec LocalPointer => Kit.Pointer - new Vec(X, Y);
    internal bool InTitleBar(Vec local) => local.Y >= 0 && local.Y < Tok.HeaderH && local.X >= 0 && local.X < W;

    internal void PlaceInitially()
    {
        if (positioned) return;
        positioned = true;
        bool max = maximized; maximized = false;
        if (Kit.TryLoadState(this, out var s))
        {
            X = s.X; Y = s.Y;
            if (s.HasSize) { var (w, h) = Window.ClampSize(s.W, s.H, MinW, MinH, Kit.ViewW, Kit.ViewH); ApplySize(w, h); }
            max |= s.Maximized;
            // #23: a remembered position (partly) off screen comes back on screen
            var f = Window.FitOnScreen(ScreenRect, Kit.ViewW, Kit.ViewH);
            if (!Window.FullyOnScreen(ScreenRect, Kit.ViewW, Kit.ViewH)) { X = f.X; Y = f.Y; ApplySize(f.W, f.H); }
        }
        else { var c = KitMath.Centre(W, H, Kit.ViewW, Kit.ViewH); X = c.X; Y = c.Y; }
        ClampPosition();
        fitPending = false;
        if (max && CanMaximize) { restoreRect = ScreenRect; maximized = true; ApplyMaximized(); }
    }
    internal void ClampPosition()
    {
        var r = KitMath.ClampToView(new RectF(X, Y, W, H), Kit.ViewW, Kit.ViewH);
        X = r.X; Y = r.Y;
        if (Rt != null) Draw.Place(Rt, X, Y, W, H);
        PlaceBackdrop();
    }
    private void PlaceBackdrop() { if (backdropImg != null) Draw.Place(backdropImg.rectTransform, -X - 4, -Y - 4, Kit.ViewW + 8, Kit.ViewH + 8); }
    internal void MoveTo(float x, float y) { X = x; Y = y; ClampPosition(); }

    // ---------- Visuals ----------
    internal void Invalidate()
    {
        Built = false; Rt = null; frame = header = footer = null; hintViews.Clear(); hintSignature = ""; backdropImg = null;
        gripImg = gripZone = null;
        Body.Unbuild(); close.Unbuild(); back.Unbuild(); maxButton.Unbuild(); contextChip.Unbuild();
        foreach (var p in pages.Values) p.Root.Unbuild();
        modal?.Unbuild();
    }
    internal void EnsureBuilt(RectTransform layer)
    {
        if (Built && Rt != null) { if (!Rt.gameObject.activeSelf) Rt.gameObject.SetActive(true); return; }
        Invalidate();
        if (IsHud) { BuildHud(layer); return; }
        Rt = Draw.Node("Panel " + Key, layer);
        Draw.Place(Rt, X, Y, W, H);
        backdropImg = Draw.Child(Rt, "Backdrop", null, new Rgba(0, 0, 0, (byte)(backdropAlpha * 255)));
        backdropImg.raycastTarget = true; Draw.SetActive(backdropImg, backdropAlpha > 0); PlaceBackdrop();
        // 0.1.4: invisible raycast target over the grip bands that reach past the right/bottom edge
        gripZone = Draw.Child(Rt, "GripZone", null, Rgba.Clear); gripZone.raycastTarget = true;
        Draw.Stretch(gripZone.rectTransform, 0, 0, -Window.GripOutside, -Window.GripOutside);
        var shadow = Draw.Child(Rt, "Shadow", Assets.Shadow(out _), Tok.Shadow, sliced: true);
        shadow.pixelsPerUnitMultiplier = 1;
        // box-shadow 0 18px 48px: blur radius 24 around the frame, shifted down 18
        Draw.Stretch(shadow.rectTransform, -Assets.ShadowBlur, -Assets.ShadowBlur + 18, -Assets.ShadowBlur, -Assets.ShadowBlur - 18);
        frame = new BoxView(Rt, "Frame", Tok.RadiusPanel, Corners.All, ring: true, raycast: true); Draw.Stretch(frame.Rt); frame.Set(Tok.PanelGlass, Tok.Frame);
        header = new BoxView(Rt, "Header", Tok.RadiusPanel, Corners.Top, ring: false); header.Set(Tok.HeadFoot, Rgba.Clear);
        headerLine = Draw.Child(Rt, "HeaderLine", null, Tok.Frame);
        if (TitleIcon != Icon.None) iconImg = Draw.Child(Rt, "Icon", Assets.IconSprite(TitleIcon), Tok.Accent);
        titleText = Draw.Text(Draw.Node("Title", Rt), TextStyle.Title, title);
        subtitleText = Draw.Text(Draw.Node("Subtitle", Rt), TextStyle.Value, subtitle); subtitleText.color = Draw.C(Tok.Text2);
        dragHint = Draw.Text(Draw.Node("DragHint", Rt), TextStyle.Faint, "Drag the title bar to move"); dragHint.alignment = TextAlignmentOptions.MidlineRight;
        contextChip.BuildIn(Rt);
        close.BuildIn(Rt);
        maxButton.BuildIn(Rt);
        back.BuildIn(Rt);
        bodyClip = Draw.Node("BodyClip", Rt); bodyClip.gameObject.AddComponent<RectMask2D>();
        Draw.Place(bodyClip, 0, 0, W, H);
        Body.BuildIn(bodyClip);
        foreach (var p in pages.Values) p.Root.BuildIn(bodyClip);
        var shown = ContentRoot;
        Body.Visible = shown == Body; Draw.SetActive(Body.Rt, shown == Body);
        foreach (var p in pages.Values) { p.Root.Visible = p.Root == shown; Draw.SetActive(p.Root.Rt, p.Root == shown); }
        footer = new BoxView(Rt, "Footer", Tok.RadiusPanel, Corners.Bottom, ring: false); footer.Set(Tok.HeadFoot, Rgba.Clear);
        footerLine = Draw.Child(Rt, "FooterLine", null, Tok.Divider);
        statusIcon = Draw.Child(footer.Rt, "StatusIcon", null, Tok.Success);
        statusLabel = Draw.Text(Draw.Node("Status", footer.Rt), TextStyle.Small, statusText);
        gripImg = Draw.Child(Rt, "ResizeGrip", Assets.IconSprite(Icon.ResizeGrip), Tok.Faint);
        modal?.Build(Rt);
        Built = true; dirty = true;
        ApplyStatus();
    }
    private void ApplyStatus()
    {
        if (statusLabel == null || statusIcon == null) return;
        Draw.SetText(statusLabel, statusText);
        var c = Look.Status(statusKind);
        statusLabel.color = Draw.C(c);
        var icon = Look.StatusIcon(statusKind);
        statusIcon.gameObject.SetActive(icon != Icon.None && statusText.Length > 0);
        if (icon != Icon.None) { statusIcon.sprite = Assets.IconSprite(icon); statusIcon.color = Draw.C(statusKind == StatusKind.Error ? Tok.Error : c); }
        dirty = true;
    }

    private void Layout()
    {
        if (Rt == null) return;
        dirty = false;
        if (IsHud) { LayoutHud(); return; }
        Draw.Place(Rt, X, Y, W, H);
        PlaceBackdrop();
        header!.Place(0, 0, W, Tok.HeaderH);
        Draw.Place(headerLine!.rectTransform, 0, Tok.HeaderH - 1, W, 1);
        float x = 16;
        bool showBack = nav.Depth > 0;
        back.Visible = showBack; if (back.Rt != null) back.Rt.gameObject.SetActive(showBack);
        if (showBack) { back.Arrange(new RectF(6, (Tok.HeaderH - Tok.IconButton) / 2, Tok.IconButton, Tok.IconButton), new Vec(0, 0), RectF.Infinite); x = 6 + Tok.IconButton + 6; }
        if (iconImg != null) { Draw.SetActive(iconImg, !showBack); if (!showBack) { Draw.Place(iconImg.rectTransform, x, (Tok.HeaderH - 16) / 2, 16, 16); x += 16 + 12; } }
        close.Visible = closable; if (close.Rt != null) close.Rt.gameObject.SetActive(closable);
        float closeX = W - 8 - Tok.IconButton;
        close.Arrange(new RectF(closeX, (Tok.HeaderH - Tok.IconButton) / 2, Tok.IconButton, Tok.IconButton), new Vec(0, 0), RectF.Infinite);
        // 0.1.4: maximize/restore directly left of the close button, or in its place when not closable
        bool showMax = CanMaximize;
        maxButton.Visible = showMax; if (maxButton.Rt != null) maxButton.Rt.gameObject.SetActive(showMax);
        float maxX = closable ? closeX - Tok.TitleButtonGap - Tok.IconButton : closeX;
        if (showMax) maxButton.Arrange(new RectF(maxX, (Tok.HeaderH - Tok.IconButton) / 2, Tok.IconButton, Tok.IconButton), new Vec(0, 0), RectF.Infinite);
        float rightEdge = showMax ? maxX : closable ? closeX : W - 8;
        float hintW = Draw.Width(dragHint!, dragHint!.text);
        float hintX = rightEdge - 12 - hintW;
        float tw = MathF.Min(Draw.Width(titleText!, title) + 2, MathF.Max(40, hintX - x - 40));
        Draw.Place(titleText!.rectTransform, x, 0, tw, Tok.HeaderH); x += tw + 12;
        string sub = showBack ? Trail() : subtitle;
        Draw.SetText(subtitleText, sub);
        Draw.SetColor(subtitleText, showBack ? Tok.AccentText : Tok.Text2);
        float sw = sub.Length == 0 ? 0 : MathF.Min(Draw.Width(subtitleText!, sub) + 2, MathF.Max(0, hintX - x - 20));
        Draw.Place(subtitleText!.rectTransform, x, 0, sw, Tok.HeaderH); if (sw > 0) x += sw + 8;
        string ctx = Guard.Get(Key + ".Context", contextGetter, null) ?? "";
        contextChip.Visible = ctx.Length > 0; if (contextChip.Rt != null) contextChip.Rt.gameObject.SetActive(contextChip.Visible);
        float cw = contextChip.Visible ? contextChip.IntrinsicWidth() : 0;
        contextChip.Arrange(new RectF(x, (Tok.HeaderH - 20) / 2, cw, 20), new Vec(0, 0), RectF.Infinite);
        if (cw > 0) x += cw + 8;
        bool showHint = hintX > x + 8;
        dragHint.gameObject.SetActive(showHint);
        Draw.Place(dragHint.rectTransform, hintX, 0, hintW, Tok.HeaderH);
        // body
        var bodyRect = new RectF(0, Tok.HeaderH, W, H - Tok.HeaderH - Tok.FooterH);
        Draw.Place(bodyClip!, bodyRect.X, bodyRect.Y, bodyRect.W, bodyRect.H);
        ContentRoot.Arrange(bodyRect, new Vec(bodyRect.X, bodyRect.Y), bodyRect);
        // footer
        footer!.Place(0, H - Tok.FooterH, W, Tok.FooterH);
        Draw.Place(footerLine!.rectTransform, 0, H - Tok.FooterH, W, 1);
        LayoutFooter();
        // 0.1.4: resize grip (bottom-right corner) and the grip raycast zone
        bool grips = CanResize;
        Draw.SetActive(gripImg, grips); Draw.SetActive(gripZone, grips);
        if (gripImg != null) Draw.Place(gripImg.rectTransform, W - Tok.GripInset - Tok.GripIcon, H - Tok.GripInset - Tok.GripIcon, Tok.GripIcon, Tok.GripIcon);
        modal?.Layout(W, bodyRect);
    }
    private void LayoutFooter()
    {
        float right = W - 16;
        for (int i = hintViews.Count - 1; i >= 0; i--)
        {
            var (cap, key, action) = hintViews[i];
            float aw = Draw.Width(action, action.text), kw = Draw.Width(key, key.text) + 12;
            Draw.Place(action.rectTransform, right - aw, 0, aw, Tok.FooterH); right -= aw + 6;
            cap.Place(right - kw, (Tok.FooterH - 18) / 2, kw, 18);
            Draw.Place(key.rectTransform, 0, 0, kw, 18);
            right -= kw + 16;
        }
        bool icon = statusIcon!.gameObject.activeSelf;
        float sx = 16;
        if (icon) { Draw.Place(statusIcon.rectTransform, sx, (Tok.FooterH - 12) / 2, 12, 12); sx += 18; }
        Draw.Place(statusLabel!.rectTransform, sx, 0, MathF.Max(0, right - sx), Tok.FooterH);
    }
    private void RefreshHints(float now)
    {
        if (now < nextHintCheck && hintSignature.Length > 0) return;
        nextHintCheck = now + 1;
        var all = new List<(string, string)>();
        if (DefaultKeyHints && ToggleKey != null && closable)
        {
            string k = Guard.Get(Key + ".ToggleKey", () => ToggleKey.GetBindingDisplayString(0), "");
            if (!string.IsNullOrEmpty(k)) all.Add((k, "close"));
        }
        if (DefaultKeyHints)
        {
            if (modal != null) all.Add(("Esc", "cancel"));
            else if (nav.Depth > 0) all.Add(("Esc", "back"));
            else if (closable) all.Add(("Esc", "close"));
        }
        all.AddRange(hints);
        string sig = string.Join("|", all.Select(h => h.Item1 + "=" + h.Item2));
        if (sig == hintSignature) return;
        hintSignature = sig;
        foreach (var v in hintViews) UnityEngine.Object.Destroy(v.Cap.Rt.gameObject);
        foreach (var v in hintViews) UnityEngine.Object.Destroy(v.Action.gameObject);
        hintViews.Clear();
        foreach (var (k, a) in all)
        {
            var cap = new BoxView(footer!.Rt, "KeyCap", Tok.RadiusSmall); cap.Set(Rgba.Clear, Tok.Border);
            var key = Draw.Text(Draw.Node("Key", cap.Rt), TextStyle.Small, k); key.color = Draw.C(Tok.Text); key.alignment = TextAlignmentOptions.Midline;
            var act = Draw.Text(Draw.Node("Hint", footer.Rt), TextStyle.Small, a);
            hintViews.Add((cap, key, act));
        }
        dirty = true;
    }

    /// <summary>Per frame: bindings, visual state, layout when something changed.</summary>
    internal void Tick(float now)
    {
        if (!Built || Rt == null) return;
        if (IsHud) { Body.Refresh(now); if (dirty) Layout(); return; }
        if (sizeChanged) { sizeChanged = false; Guard.Run(Key + ".Resized", Resized); }
        bool focused = Kit.KeyboardFocus == this;
        header?.Set(focused ? Tok.FocusHeader : Tok.HeadFoot, Rgba.Clear);
        Draw.SetColor(headerLine, focused ? Tok.AccentLine : Tok.Frame);
        Draw.SetColor(titleText, focused ? Tok.FocusTitle : Tok.UnfocusedTitle);
        ContentRoot.Refresh(now);
        modal?.Content.Refresh(now);
        close.Refresh(now);
        if (CanMaximize) { maxButton.SetIcon(maximized ? Icon.Restore : Icon.Maximize); maxButton.Refresh(now); }
        if (gripImg != null && CanResize) Draw.SetColor(gripImg, Kit.ResizePanel == this || Kit.GripHoverPanel == this ? Tok.Accent : Tok.Faint);
        if (nav.Depth > 0) back.Refresh(now);
        string before = contextChip.Visible ? "1" : "0";
        contextChip.Refresh(now);
        bool ctxVisible = (Guard.Get(Key + ".Context", contextGetter, null) ?? "").Length > 0;
        if ((ctxVisible ? "1" : "0") != before) dirty = true;
        RefreshHints(now);
        if (dirty) Layout();
    }
    internal Element? HitTest(Vec local)
    {
        if (!new RectF(0, 0, W, H).Contains(local)) return null;
        if (closable && close.HitTest(local) is { } c) return c;
        if (CanMaximize && maxButton.HitTest(local) is { } m) return m;
        if (nav.Depth > 0 && back.HitTest(local) is { } b) return b;
        if (local.Y < Tok.HeaderH || local.Y >= H - Tok.FooterH) return null;
        if (modal != null) return modal.Content.HitTest(local);
        return ContentRoot.HitTest(local);
    }
    /// <summary>Root for wheel/deepest hit tests: the modal card while one is open, else the shown page.</summary>
    internal Element InputRoot => modal != null ? modal.Content : ContentRoot;

    // ---------- HUD mode (HudPanel host) ----------
    internal HudPanel? Hud;
    private BoxView? hudBox;
    internal float HudOpacity = .82f;
    private void BuildHud(RectTransform layer)
    {
        Rt = Draw.Node("Hud " + Key, layer);
        hudBox = new BoxView(Rt, "Background", Tok.Radius, Corners.All, ring: false); Draw.Stretch(hudBox.Rt);
        hudBox.Set(Tok.HudBg.WithAlpha(HudOpacity), Rgba.Clear);
        Body.BuildIn(Rt);
        Built = true; dirty = true;
    }
    internal void SetHudOpacity(float a) { HudOpacity = Math.Clamp(a, 0, 1); hudBox?.Set(Tok.HudBg.WithAlpha(HudOpacity), Rgba.Clear); }
    private void LayoutHud()
    {
        var hud = Hud!;
        float inner = W - 2 * Tok.HudPad;
        float h = hud.FixedHeight > 0 ? hud.FixedHeight : MathF.Min(hud.MaxHeightPx, Body.HeightFor(inner) + 2 * Tok.HudPad);
        H = h;
        var r = Logic.HudRect(hud.Corner, hud.OffsetX, hud.OffsetY, W * hud.ScaleFactor, H * hud.ScaleFactor, Kit.HudViewW, Kit.HudViewH);
        X = r.X; Y = r.Y;
        Draw.Place(Rt!, X, Y, W, H);
        Rt!.localScale = new Vector3(hud.ScaleFactor, hud.ScaleFactor, 1);
        var body = new RectF(Tok.HudPad, Tok.HudPad, inner, H - 2 * Tok.HudPad);
        Body.Arrange(body, new Vec(0, 0), new RectF(0, 0, W, H));
    }
}
