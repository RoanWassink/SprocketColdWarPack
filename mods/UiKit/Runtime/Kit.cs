using BepInEx.Configuration;
using SprocketKeybinds;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace SprocketUiKit;

/// <summary>Injected host: all kit work happens in Update/LateUpdate, each pass guarded.</summary>
public sealed class UiKitHost : MonoBehaviour
{
    public UiKitHost(IntPtr pointer) : base(pointer) { }
    public void Update() { try { Kit.Update(); } catch (Exception ex) { Kit.SafeReset(); Guard.Report("Kit.Update", ex); } }
    public void LateUpdate() { try { Kit.LateUpdate(); } catch (Exception ex) { Kit.SafeReset(); Guard.Report("Kit.LateUpdate", ex); } }
    public void OnApplicationFocus(bool focused) { if(!focused)Kit.ReleaseKeyboard("alt-tab"); }
}

/// <summary>
/// Shared runtime: one overlay canvas, panel z-order, Input System pointer/keyboard dispatch,
/// cursor ownership, text focus, the dropdown overlay and tooltips. Behaviour lives here, never
/// in individual mods.
/// </summary>
internal static class Kit
{
    internal const int SortingOrder = 30000;
    internal static readonly List<Panel> Panels = new();
    internal static readonly ZOrder<Panel> Order = new();
    internal static readonly List<(Panel Panel, ModKeybind Key)> Toggles = new();
    internal static float Scale = 1, ViewW = 1920, ViewH = 1080, Now;
    internal static Vec Pointer; internal static bool PointerValid;
    internal static bool LeftHeld;
    internal static Element? Hover, Pressed;
    internal static Panel? DragPanel;
    internal static PopupList? Popup;
    internal static TextField? Focus;
    private static readonly KeyboardFocusState<Panel> keyboard = new();
    internal static Panel? KeyboardFocus => keyboard.Current;
    private static readonly List<int> sceneHandles = new();
    private static int activeScene;
    private static bool scenesKnown, focusSuppressed, shuttingDown;
    private static bool CanFocus => !focusSuppressed && !shuttingDown && Application.isFocused && !Keybinds.IsConfiguring && !GamePaused();
    internal static void SetKeyboardFocus(Panel? panel, string reason)
    {
        keyboard.Press(CanFocus && panel is { IsHud: false, IsOpenInternal: true } ? panel : null);
        KeyboardGuard.Tick(reason);
    }
    internal static void ReleaseKeyboard(string reason)
    {
        keyboard.Clear();
        try { KeyboardGuard.Reset(reason); } finally { Blur(false); }
    }
    private static void CheckKeyboardLifecycle(bool configuring)
    {
        int count = SceneManager.sceneCount, active = SceneManager.GetActiveScene().handle;
        bool changed = scenesKnown && (count != sceneHandles.Count || active != activeScene);
        for(int i=0;i<count;i++)
        {
            int handle = SceneManager.GetSceneAt(i).handle;
            if(i<sceneHandles.Count) { changed |= scenesKnown && sceneHandles[i]!=handle; sceneHandles[i]=handle; }
            else sceneHandles.Add(handle);
        }
        if(sceneHandles.Count>count)sceneHandles.RemoveRange(count,sceneHandles.Count-count);
        scenesKnown=true; activeScene=active;
        focusSuppressed = changed || !Application.isFocused || configuring || GamePaused();
        if(focusSuppressed)
            ReleaseKeyboard(changed ? "scene change" : !Application.isFocused ? "window focus lost" : configuring ? "settings" : "pause");
        KeyboardGuard.Tick("lifecycle/self-heal");
    }
    internal static readonly TextEdit Edit = new();
    internal static UiContexts Context = UiContexts.Other;
    internal static int OpenCount => Panels.Count(p => p.IsOpenInternal);
    // 0.1.2
    internal static bool ShiftHeld, CtrlHeld, AltHeld;
    internal static float HudViewW = 1920, HudViewH = 1080;
    internal static NodeGraph? GraphCapture; internal static int GraphButton;
    internal static CanvasView? CanvasCapture; internal static int CanvasButton;
    internal static Sprocket.GameTime? GameTimeRef; internal static bool PauseHookActive;
    // 0.1.4: resize grips, drag from the maximized state
    internal static Panel? ResizePanel, GripHoverPanel;
    private static ResizeEdge resizeEdge; private static RectF resizeStart; private static Vec resizeFrom;
    private static bool dragFromMax; private static Vec dragFrom;
    private static int pauseTraces;
    /// <summary>
    /// The game's own pause menu (0.1.2): GameTime.PauseState of the instance seen in RequestPause.
    /// A mod that only sets Time.timeScale = 0 (Expedition menu) is not a pause. Without the pause
    /// hook the old timeScale rule is the fallback.
    /// </summary>
    internal static bool GamePaused()
    {
        if (!PauseHookActive) return Time.timeScale <= 0;
        var gt = GameTimeRef;
        if (gt == null) return false;
        try { var s = gt.PauseState; return s == Sprocket.PauseState.Paused || s == Sprocket.PauseState.Pausing; }
        catch (Exception ex) { if (pauseTraces++ < 3) Guard.Report("Pause state", ex); GameTimeRef = null; return false; }
    }
    /// <summary>Scale of the canvas a panel's elements are drawn on (HUD scale for HUDs).</summary>
    internal static float CurrentScale(Panel p) => p.IsHud ? KitMath.Scale(Screen.height, Plugin.ScaleFactor?.Value ?? 1f) * (p.Hud?.ScaleFactor ?? 1) : Scale > 0 ? Scale : 1;
    /// <summary>Forget pointer/focus/popup state inside a panel whose shown content changed (page or modal).</summary>
    internal static void LeftSubtree(Panel panel)
    {
        if (Focus != null && Focus.Panel == panel) Blur(true);
        if (Popup != null && Popup.Owner.Panel == panel) ClosePopup();
        if (Hover?.Panel == panel) Hover = null;
        if (Pressed?.Panel == panel) Pressed = null;
        if (CanvasCapture?.Panel == panel) { CanvasCapture.PointerCancel(); CanvasCapture = null; }
        if (GraphCapture?.Panel == panel) { GraphCapture.PointerCancel(); GraphCapture = null; }
        if (tipTarget?.Panel == panel) HideTip();
    }
    internal static void BeginCanvasCapture(CanvasView view, int button, Vec panelLocal)
    {
        if (CanvasCapture != null) return;
        CanvasCapture = view; CanvasButton = button;
        view.PointerDown(button, panelLocal);
    }

    private static GameObject? root; private static Canvas? canvas;
    private static RectTransform? panelLayer, overlay, tipLayer;
    private static Vec dragOffset;
    private static bool pressOnPopup, swallowRelease;
    private static int swallowReleaseFrame = -10;
    private static bool cursorSaved, savedVisible, skipCursorRestore;
    private static CursorLockMode savedLock;
    private static string sceneSignature = ""; private static float nextSceneCheck;
    private static bool waitingLogged;
    private static ConfigFile? positions;
    // tooltip
    private static Element? tipTarget; private static float tipSince; private static string tipShown = "";
    private static BoxView? tipBox; private static TextMeshProUGUI? tipText;
    // keyboard
    private static (Key Key, string Name)[]? typingKeys;
    private static readonly KeyRepeat repBack = new(), repDel = new(), repLeft = new(), repRight = new();

    // ---------- Panels ----------
    internal static Panel Create(string modGuid, string id, string title, string? subtitle, float w, float h)
    {
        if (string.IsNullOrWhiteSpace(modGuid) || string.IsNullOrWhiteSpace(id)) throw new ArgumentException("modGuid and id are required.");
        if (Panels.Any(p => p.ModGuid == modGuid && p.Id == id)) throw new InvalidOperationException($"Panel {modGuid}/{id} already exists.");
        var panel = new Panel(modGuid, id, title, subtitle, w, h);
        Panels.Add(panel);
        Order.BringToFront(panel);
        return panel;
    }
    internal static void Destroy(Panel panel)
    {
        panel.Close("destroyed");
        Toggles.RemoveAll(t => t.Panel == panel);
        Panels.Remove(panel); Order.Remove(panel);
        if (panel.Rt != null) Object.Destroy(panel.Rt.gameObject);
        panel.Invalidate();
    }
    internal static void Opened(Panel panel)
    {
        BringToFront(panel);
        if (!panel.IsHud) SetKeyboardFocus(panel,"panel opened");
        if (Context == UiContexts.Game && panel.BlockGameplayInput) SaveCursor();
    }
    internal static void Closed(Panel panel)
    {
        keyboard.Close(panel, Order.Items, static p => p.IsOpenInternal && !p.IsHud);
        if(!CanFocus)keyboard.Clear();
        if (Focus != null && Focus.Panel == panel) Blur(true);
        if (Popup != null && Popup.Owner.Panel == panel) ClosePopup();
        if (Hover?.Panel == panel) Hover = null;
        if (Pressed?.Panel == panel) Pressed = null;
        if (DragPanel == panel) { DragPanel = null; SavePosition(panel); }
        if (ResizePanel == panel) { ResizePanel = null; SavePosition(panel); }
        if (GripHoverPanel == panel) GripHoverPanel = null;
        if (tipTarget?.Panel == panel) HideTip();
        if (CanvasCapture?.Panel == panel) { CanvasCapture.PointerCancel(); CanvasCapture = null; }
        if (GraphCapture?.Panel == panel) { GraphCapture.PointerCancel(); GraphCapture = null; }
        if (OpenCount == 0) RestoreCursor();
        KeyboardGuard.Tick("panel closed/focus handed on");
    }
    internal static void BringToFront(Panel panel)
    {
        Order.BringToFront(panel);
        if (panel.Rt != null) panel.Rt.SetAsLastSibling();
    }
    internal static void ForgetElement() { Hover = null; Pressed = null; GraphCapture?.PointerCancel(); GraphCapture = null; }
    internal static void BeginGraphCapture(NodeGraph graph, int button, Vec local)
    { if (GraphCapture != null || CanvasCapture != null) return; GraphCapture=graph; GraphButton=button; graph.PointerDown(button,local); }
    internal static void SafeReset()
    {
        ReleaseKeyboard("safe reset");
        try { ClosePopup(); } catch { }
        try { Blur(false); } catch { }
        Hover = Pressed = null; DragPanel = null; ResizePanel = null; GripHoverPanel = null;
        GraphCapture?.PointerCancel(); GraphCapture=null;
        LeftHeld = false; swallowReleaseFrame = -10;
    }

    // ---------- Frame ----------
    internal static void Update()
    {
        Now = Time.unscaledTime;
        var previous = Context;
        Context = DetectContext();
        bool configuring = Keybinds.IsConfiguring;
        CheckKeyboardLifecycle(configuring);
        try { HudLayer.Update(Context, configuring, OpenCount > 0, Context == UiContexts.Game && GamePaused()); }
        catch (Exception ex) { Guard.Report("HUD layer", ex); }
        if (configuring)
        {
            GraphCapture?.PointerCancel(); GraphCapture=null;
            // Vanilla settings menu owns input; hide (not close) the panels.
            if (root != null && root.activeSelf) { ClosePopup(); Blur(false); HideTip(); root.SetActive(false); }
            return;
        }
        PollToggles();
        if (OpenCount == 0) { Idle(); return; }
        CloseRules(previous);
        if (OpenCount == 0) { Idle(); return; }
        if (!Application.isFocused) return;
        if (!EnsureCanvas()) return;
        if (!root!.activeSelf) root.SetActive(true);
        UpdateScale();
        bool anyFresh = false;
        foreach (var p in Panels.Where(p => p.IsOpenInternal).ToList())
        {
            bool fresh = !p.Built;
            p.EnsureBuilt(panelLayer!);
            if (fresh) { p.PlaceInitially(); anyFresh = true; }
            if (p.FitPending) p.FitOnScreen();
        }
        if (anyFresh) foreach (var p in Order.Items) if (p.Rt != null) p.Rt.SetAsLastSibling();
        ProcessPointer();
        ProcessKeyboard();
        KeyboardGuard.Tick("frame/self-heal");
        foreach (var p in Order.Items.Where(p => p.IsOpenInternal).ToList()) p.Tick(Now);
        Popup?.Refresh(Pointer, PointerValid, LeftHeld);
        UpdateTip();
    }
    internal static void LateUpdate()
    {
        if (OpenCount > 0 && Context == UiContexts.Game && Panels.Any(p => p.IsOpenInternal && p.BlockGameplayInput) && !Keybinds.IsConfiguring)
        {
            SaveCursor();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
    }
    private static void Idle()
    {
        if (root != null && root.activeSelf) { HideTip(); root.SetActive(false); }
        ReleaseKeyboard("idle/self-heal");
        RestoreCursor();
    }

    private static UiContexts DetectContext()
    {
        if (SceneManager.GetSceneByName("VehicleControlUI").isLoaded) return UiContexts.Game;
        if (SceneManager.GetSceneByName("VehicleDesignerUI").isLoaded) return UiContexts.Designer;
        return UiContexts.Other;
    }
    private static void CloseRules(UiContexts previous)
    {
        // pause menu: it owns the cursor, so do not restore ours
        if (Context == UiContexts.Game && GamePaused())
        {
            var closing = Panels.Where(p => p.IsOpenInternal && !p.StayOpenWhilePaused).ToList();
            if (closing.Count > 0)
            {
                skipCursorRestore = true;
                foreach (var p in closing) p.Close("closed: game paused");
                skipCursorRestore = false;
                if (OpenCount == 0) { cursorSaved = false; return; }
            }
        }
        foreach (var p in Panels.Where(p => p.IsOpenInternal && (p.AllowedContexts & Context) == 0).ToList()) p.Close($"closed: context {Context} not allowed");
        if (previous != Context)
            foreach (var p in Panels.Where(p => p.IsOpenInternal && p.CloseOnSceneChange).ToList()) p.Close($"closed: context {previous} -> {Context}");
        if (Now >= nextSceneCheck)
        {
            nextSceneCheck = Now + .5f;
            var sig = string.Join("|", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name));
            if (sceneSignature.Length > 0 && sig != sceneSignature)
            {
                if (Plugin.Detailed) Plugin.Trace($"[UI Kit] Scenes changed: {sceneSignature} -> {sig}");
                foreach (var p in Panels.Where(p => p.IsOpenInternal && p.CloseOnSceneChange).ToList()) p.Close("closed: scene change");
            }
            sceneSignature = sig;
        }
    }
    internal static void TrackScenes()
    {
        // keep the scene signature current while no panel is open, so opening never sees a stale change
        if (Now < nextSceneCheck) return;
        nextSceneCheck = Now + .5f;
        sceneSignature = string.Join("|", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name));
    }
    private static void PollToggles()
    {
        if (Focus != null) { TrackScenes(); return; }
        foreach (var (panel, key) in Toggles.ToList())
        {
            bool pressed;
            try { pressed = key.WasPressedThisFrame; }
            catch (Exception ex) { Guard.Report(panel.Key + ".ToggleKey", ex); pressed = false; }
            if (pressed) panel.ToggleByKey();
        }
        if (OpenCount == 0) TrackScenes();
    }

    // ---------- Canvas / scale ----------
    private static bool EnsureCanvas()
    {
        bool alive = false;
        try { alive = root != null && canvas != null && panelLayer != null; } catch { }
        if (alive && Assets.FontAlive()) return true;
        if (Assets.Font == null)
        {
            if (!waitingLogged) { waitingLogged = true; Plugin.Instance.Log.LogWarning("[UI Kit] Waiting for a TMP font (TMP_Settings.defaultFontAsset / scene text); panels open as soon as one exists."); }
            return false;
        }
        if (alive) { foreach (var p in Panels) { if (p.Rt != null) Object.Destroy(p.Rt.gameObject); p.Invalidate(); } return true; }
        root = new GameObject("SprocketUiKit") { layer = 5 };
        root.AddComponent<RectTransform>();
        Object.DontDestroyOnLoad(root);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        canvas.pixelPerfect = false;
        // Raycaster only so the game's EventSystem sees the panel and does not click through to
        // native uGUI underneath; the kit's own clicks come from the Input System.
        try { root.AddComponent<GraphicRaycaster>(); } catch (Exception ex) { Plugin.Instance.Log.LogWarning("[UI Kit] GraphicRaycaster unavailable: " + ex.Message); }
        var crt = root.GetComponent<RectTransform>();
        panelLayer = Draw.Node("Panels", crt);
        overlay = Draw.Node("Overlay", crt);
        tipLayer = Draw.Node("Tooltip", crt);
        tipBox = null; tipText = null;
        foreach (var p in Panels) p.Invalidate();
        Scale = -1;
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] Canvas created (sort {SortingOrder}); font '{Assets.Font!.name}' via {Assets.FontRoute}.");
        return true;
    }
    private static void UpdateScale()
    {
        float s = KitMath.Scale(Screen.height, Plugin.ScaleFactor?.Value ?? 1f);
        float vw = Screen.width / s, vh = Screen.height / s;
        if (MathF.Abs(s - Scale) < .0001f && MathF.Abs(vw - ViewW) < .5f && MathF.Abs(vh - ViewH) < .5f) return;
        Scale = s; ViewW = vw; ViewH = vh;
        canvas!.scaleFactor = s;
        foreach (var p in Panels) p.ViewChanged();
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] Scale {s:0.###} (screen {Screen.width}x{Screen.height}, factor {Plugin.ScaleFactor?.Value ?? 1f}); view {vw:0}x{vh:0} units.");
    }

    // ---------- Pointer ----------
    private static bool ReadPointer(out Vec p, out Mouse? mouse)
    {
        p = default; mouse = Mouse.current;
        if (mouse == null) return false;
        var v = mouse.position.ReadValue();
        float s = Scale > 0 ? Scale : KitMath.Scale(Screen.height, Plugin.ScaleFactor?.Value ?? 1f);
        p = KitMath.ScreenToKit(v.x, v.y, Screen.height, s);
        return true;
    }
    private static void ProcessPointer()
    {
        var kbm = Keyboard.current;
        ShiftHeld = kbm != null && kbm.shiftKey.isPressed; CtrlHeld = kbm != null && kbm.ctrlKey.isPressed; AltHeld = kbm != null && kbm.altKey.isPressed;
        PointerValid = ReadPointer(out Pointer, out var mouse);
        if (!PointerValid || mouse == null) { GraphCapture?.PointerCancel(); GraphCapture=null; Hover = null; GripHoverPanel = null; LeftHeld = false; if (CanvasCapture != null) { CanvasCapture.PointerCancel(); CanvasCapture = null; } return; }
        LeftHeld = mouse.leftButton.isPressed;
        bool overPopup = Popup != null && Popup.Contains(Pointer);
        Panel? panel = overPopup ? null : TopPanelAt(Pointer);
        Vec panelLocal = panel != null ? Pointer - new Vec(panel.X, panel.Y) : default;
        Element? contentHit = panel?.HitTest(panelLocal);
        bool overBar = contentHit is ScrollBarHandle;
        // 0.1.4/0.1.5: resize grips win over content in their thin bands, except where a scrollbar's
        // click strip is under the pointer inside the panel; there the bar wins (see Window.ResolveGrip).
        var grip = panel != null && Pressed == null && DragPanel == null && ResizePanel == null && CanvasCapture == null && GraphCapture == null
            ? Window.ResolveGrip(panel.GripAt(panelLocal), overBar, panelLocal.X >= 0 && panelLocal.X < panel.W && panelLocal.Y >= 0 && panelLocal.Y < panel.H)
            : ResizeEdge.None;
        GripHoverPanel = grip != ResizeEdge.None ? panel : null;
        Element? hit = grip == ResizeEdge.None ? contentHit : null;
        Hover = hit;
        bool down = mouse.leftButton.wasPressedThisFrame, held = mouse.leftButton.isPressed, up = mouse.leftButton.wasReleasedThisFrame;
        // Remember captures before the resize/drag blocks end them on the release frame.
        bool hadCapture = Pressed != null || pressOnPopup || DragPanel != null || ResizePanel != null || CanvasCapture != null || GraphCapture != null;

        if (down || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
        {
            var owner = overPopup ? Popup!.Owner.Panel : Order.TopWhere(p => p.IsOpenInternal && !p.IsHud && p.Built && p.ScreenRect.Contains(Pointer));
            SetKeyboardFocus(owner,"pointer press");
            if(owner==null && Focus!=null)Blur(true);
        }
        if (down)
        {
            if (Popup != null && !overPopup) { ClosePopup(); swallowRelease = true; }
            else if (overPopup) { pressOnPopup = true; Popup!.PressBar(Pointer, Now); }
            else if (panel != null)
            {
                BringToFront(panel);
                if (Focus != null && hit != Focus) Blur(true);
                if (grip != ResizeEdge.None)
                {
                    ResizePanel = panel; resizeEdge = grip; resizeStart = panel.ScreenRect; resizeFrom = Pointer;
                    if (Plugin.Detailed) Plugin.Trace($"[UI Kit] resize {panel.Key} ({grip}) from {resizeStart}");
                }
                else if (hit != null)
                {
                    Pressed = hit;
                    if (hit.Enabled) Guard.Run(hit.Where + ".Press", () => hit.OnPress(panelLocal));
                }
                else if (panel.InTitleBar(panelLocal))
                {
                    // 0.1.4: double-click on an empty spot of the title bar toggles maximize (like Windows)
                    if (panel.TitleClicks.Press(Now, Pointer) && panel.CanMaximize) panel.ToggleMaximize();
                    else { DragPanel = panel; dragOffset = Pointer - new Vec(panel.X, panel.Y); dragFrom = Pointer; dragFromMax = panel.IsMaximized; }
                }
            }
            else if (Focus != null) Blur(true);
        }
        if (ResizePanel != null)
        {
            if (held)
            {
                var rp = ResizePanel;
                var r = Window.Resize(resizeStart, resizeEdge, Pointer.X - resizeFrom.X, Pointer.Y - resizeFrom.Y, rp.MinW, rp.MinH, ViewW, ViewH);
                rp.ApplySize(r.W, r.H);
            }
            else EndResize();
        }
        // 0.1.2: right/middle buttons start a canvas capture; any captured button drives the canvas
        if (CanvasCapture == null && !overPopup && panel != null && hit is CanvasView cv && cv.Enabled && GraphCapture == null)
        {
            int b = mouse.rightButton.wasPressedThisFrame ? 1 : mouse.middleButton.wasPressedThisFrame ? 2 : -1;
            if (b > 0) { BringToFront(panel); if (Focus != null) Blur(true); BeginCanvasCapture(cv, b, Pointer - new Vec(panel.X, panel.Y)); }
        }
        if (GraphCapture == null && CanvasCapture == null && !overPopup && panel != null && hit is NodeGraph graph && graph.Enabled)
        {
            int gb = mouse.middleButton.wasPressedThisFrame ? 2 : mouse.rightButton.wasPressedThisFrame ? 1 : -1;
            if (gb > 0) { BringToFront(panel); if (Focus != null) Blur(true); BeginGraphCapture(graph, gb, panelLocal); }
        }
        if (GraphCapture != null)
        {
            var gp=GraphCapture; var glocal=Pointer-new Vec(gp.Panel.X,gp.Panel.Y);
            var control=GraphButton==0?mouse.leftButton:GraphButton==1?mouse.rightButton:mouse.middleButton;
            bool graphVisible=gp.Visible&&gp.Enabled;
            for(var ancestor=gp.Parent;ancestor!=null;ancestor=ancestor.Parent)graphVisible&=ancestor.Visible&&ancestor.Enabled;
            if (!graphVisible) { gp.PointerCancel(); GraphCapture=null; }
            else if (control.isPressed) gp.PointerMove(glocal);
            else { gp.PointerUp(glocal); GraphCapture=null; if (panel!=gp.Panel) swallowReleaseFrame=Time.frameCount; }
        }
        if (CanvasCapture != null)
        {
            var cp = CanvasCapture; var plocal = Pointer - new Vec(cp.Panel.X, cp.Panel.Y);
            var btn = CanvasButton == 0 ? mouse.leftButton : CanvasButton == 1 ? mouse.rightButton : mouse.middleButton;
            if (btn.isPressed) Guard.Run(cp.Where + ".Drag", () => cp.PointerMove(plocal));
            else { CanvasCapture = null; Guard.Run(cp.Where + ".Release", () => cp.PointerUp(plocal)); }
        }
        if (DragPanel != null)
        {
            if (held && dragFromMax)
            {
                // a maximized panel restores once the drag really starts; the pointer keeps its place in the title bar
                if (MathF.Abs(Pointer.X - dragFrom.X) + MathF.Abs(Pointer.Y - dragFrom.Y) > Window.DragThreshold)
                {
                    DragPanel.RestoreForDrag(Pointer);
                    dragOffset = Pointer - new Vec(DragPanel.X, DragPanel.Y); dragFromMax = false;
                }
            }
            else if (held) { var at = Pointer - dragOffset; DragPanel.MoveTo(at.X, at.Y); }
            else { SavePosition(DragPanel); DragPanel = null; }
        }
        if (up)
        {
            bool captured = hadCapture || Pressed != null || pressOnPopup || DragPanel != null || ResizePanel != null || CanvasCapture != null || GraphCapture != null;
            if (pressOnPopup && overPopup && !swallowRelease && Popup != null && !Popup.BarGesture) Popup.Click(Pointer);
            else if (Pressed != null && Pressed == Hover && Pressed.Enabled && !swallowRelease)
            {
                var e = Pressed; var local = Pointer - new Vec(e.Panel.X, e.Panel.Y);
                Guard.Run(e.Where + ".Click", () => e.OnClick(local));
                if (Plugin.Detailed) Plugin.Trace($"[UI Kit] click {e.Where} at {local}");
            }
            // A drag that ends outside the panel (scrollbar thumb, slider, title/grip) must not let the
            // release reach the game; the designer guard also checks this frame.
            if (captured && !overPopup && (panel == null || !panel.ScreenRect.Contains(Pointer))) swallowReleaseFrame = Time.frameCount;
            if (Popup != null) Popup.EndBar();
            Pressed = null; pressOnPopup = false; swallowRelease = false;
            if (DragPanel != null) { SavePosition(DragPanel); DragPanel = null; }
            if (ResizePanel != null) EndResize();
        }
        else if (!held) { Pressed = null; pressOnPopup = false; }

        float wheel = 0;
        try { wheel = mouse.scroll.ReadValue().y; } catch { }
        float notches = KitMath.WheelNotches(wheel);
        if (MathF.Abs(notches) > .01f)
        {
            if (overPopup) Popup!.Scroll(notches);
            else if (Popup != null) ClosePopup();
            else if (panel != null)
            {
                var local = Pointer - new Vec(panel.X, panel.Y);
                // deepest element under the pointer that scrolls (also non-interactive scroll views)
                for (var e = DeepestAt(panel, local); e != null; e = e.Parent)
                    if (e.Enabled && Guard.Get(e.Where + ".Scroll", () => e.OnScroll(notches, local), false)) break;
            }
        }
    }
    private static void EndResize()
    {
        var p = ResizePanel; ResizePanel = null;
        if (p == null) return;
        SavePosition(p);
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] resized {p.Key} to {p.Width:0}x{p.Height:0}");
    }
    private static Element? DeepestAt(Panel panel, Vec local)
    {
        // Hit test ignoring interactivity so wheel works over labels inside a scroll view.
        Element? best = panel.HitTest(local);
        if (best != null) return best;
        if (local.Y < Tok.HeaderH || local.Y >= panel.H - Tok.FooterH) return null;
        return Deep(panel.InputRoot, local);
        static Element? Deep(Element e, Vec p)
        {
            if (!e.Visible || !e.Rect.Contains(p) || !e.Clip.Contains(p)) return null;
            if (e is ColumnSet cs) { foreach (var c in cs.Columns) if (Deep(c, p) is { } h) return h; return e; }
            if (e is Container c2) { for (int i = c2.Children.Count - 1; i >= 0; i--) if (Deep(c2.Children[i], p) is { } h) return h; }
            return e;
        }
    }
    internal static Panel? TopPanelAt(Vec p) => Order.TopAt(x => x.IsOpenInternal && x.Built && (x.ScreenRect.Contains(p) || x.HasBackdrop || x.GripAt(p - new Vec(x.X, x.Y)) != ResizeEdge.None));

    /// <summary>Fresh check for the designer click-through hook (Inputs.Update postfix).</summary>
    internal static bool PointerCaptured()
    {
        if (OpenCount == 0 || Keybinds.IsConfiguring) return false;
        if (Time.frameCount == swallowReleaseFrame) return true;
        if (DragPanel != null || ResizePanel != null || Pressed != null || pressOnPopup || CanvasCapture != null || GraphCapture != null) return true;
        if (!ReadPointer(out var p, out _)) return false;
        return Popup != null && Popup.Contains(p) || TopPanelAt(p) != null;
    }
    internal static bool OwnsGameplayInput =>
        Context == UiContexts.Game && (Plugin.BlockGameplay?.Value ?? true) && !Keybinds.IsConfiguring && Panels.Any(p => p.IsOpenInternal && p.BlockGameplayInput);

    // ---------- Keyboard ----------
    private static void ProcessKeyboard()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame && (Focus != null || Popup != null || Panels.Any(p => p.IsOpenInternal))) EscConsumedFrame = Time.frameCount;
        if (Focus != null) { TypeKeys(kb); return; }
        var top = Order.TopWhere(p => p.IsOpenInternal);
        if (kb.escapeKey.wasPressedThisFrame)
        {
            // 0.1.2 order: dropdown, modal, sub-page (back), panel (close, if allowed)
            switch (Logic.Esc(false, Popup != null, top?.ModalOpen ?? false, top?.PageDepth ?? 0, top != null))
            {
                case EscTarget.Popup: ClosePopup(); break;
                case EscTarget.Modal: top!.CancelModal(); break;
                case EscTarget.Page: top!.Pop(); break;
                case EscTarget.Panel: top!.RequestClose("closed (Esc)"); break;
            }
            return;
        }
        var shortcutOwner = InputFocusPolicy.ShortcutTarget(KeyboardFocus, Popup != null);
        if (shortcutOwner == null) return;
        bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
        if (shortcutOwner.ModalOpen) { if (enter) shortcutOwner.ModalEnter(); return; }
        if (enter && shortcutOwner.DefaultActionHandler != null) { var a = shortcutOwner.DefaultActionHandler; Guard.Run(shortcutOwner.Key + ".DefaultAction", a); return; }
        // Keep the existing snapshot: callbacks may mutate the shortcut list during dispatch.
        foreach (var (chord, key, action) in shortcutOwner.Shortcuts.ToList())
        {
            KeyControl? control = null;
            try { control = kb[key]; } catch { }
            if (control != null && control.wasPressedThisFrame && Logic.ChordMatches(chord, CtrlHeld, ShiftHeld, AltHeld))
            {
                Guard.Run(shortcutOwner.Key + ".Shortcut " + chord, action);
                if (Plugin.Detailed) Plugin.Trace($"[UI Kit] shortcut {chord} on {shortcutOwner.Key}");
            }
        }
    }

    // Esc that closed a popup, text field or panel must not also open the game's pause menu (0.1.0 test).
    internal static int EscConsumedFrame = -10;
    internal static bool EscapeBelongsToKit()
    {
        if (Time.frameCount - EscConsumedFrame <= 1) return true;
        var kb = Keyboard.current;
        // The game may ask for the pause menu before the kit's Update ran this frame.
        return kb != null && kb.escapeKey.wasPressedThisFrame && (Focus != null || Popup != null || Panels.Any(p => p.IsOpenInternal));
    }
    private static void TypeKeys(Keyboard kb)
    {
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) { Blur(true); return; }
        if (kb.escapeKey.wasPressedThisFrame) { Edit.Revert(); Focus?.Changed(Edit.Text); Blur(false); return; }
        bool ctrl = kb.ctrlKey.isPressed, shift = kb.shiftKey.isPressed, changed = false;
        if (ctrl && kb.aKey.wasPressedThisFrame) Edit.SelectAll();
        if (repBack.Tick(kb.backspaceKey.wasPressedThisFrame, kb.backspaceKey.isPressed, Now)) changed |= Edit.Backspace();
        if (repDel.Tick(kb.deleteKey.wasPressedThisFrame, kb.deleteKey.isPressed, Now)) changed |= Edit.Delete();
        if (repLeft.Tick(kb.leftArrowKey.wasPressedThisFrame, kb.leftArrowKey.isPressed, Now)) Edit.Left();
        if (repRight.Tick(kb.rightArrowKey.wasPressedThisFrame, kb.rightArrowKey.isPressed, Now)) Edit.Right();
        if (kb.homeKey.wasPressedThisFrame) Edit.Home();
        if (kb.endKey.wasPressedThisFrame) Edit.End();
        if (!ctrl && !kb.altKey.isPressed)
        {
            typingKeys ??= KeyChars.KeyNames.Select(n => (Ok: Enum.TryParse<Key>(n, true, out var k), Key: k, Name: n)).Where(t => t.Ok).Select(t => (t.Key, t.Name)).ToArray();
            foreach (var (key, name) in typingKeys)
            {
                var control = kb[key];
                if (control != null && control.wasPressedThisFrame && KeyChars.Char(name, shift) is { } c) changed |= Edit.Insert(c);
            }
        }
        // 0.1.4 (#16): the field's OnChange runs per edit through Edit.OnEdit (set in FocusText), not once per frame
    }
    private static readonly Action<string> EditChanged = t => Focus?.Changed(t);
    internal static void FocusText(TextField field, string text)
    {
        if (Focus == field || !CanFocus || field.Panel.IsHud) return;
        if (Focus != null) Blur(true);
        Focus = field;
        Edit.MaxLength = field.MaxLength; Edit.Filter = field.Filter;
        Edit.Begin(text);
        Edit.OnEdit = EditChanged;
        if (CanFocus) keyboard.Open(field.Panel,!field.Panel.IsHud);
        TypingGuard.Begin();
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] text focus {field.Where}; game keys suspended: {TypingGuard.Suspended}.");
    }
    internal static void Blur(bool commit)
    {
        var f = Focus;
        if (f == null) return;
        Focus = null;
        TypingGuard.End();
        if (commit && Edit.Changed) f.Commit(Edit.Text);
    }

    // ---------- Popup ----------
    internal static void OpenPopup(PopupList popup)
    {
        ClosePopup();
        if (overlay == null) return;
        if (Focus != null) Blur(true);
        Popup = popup;
        popup.Build(overlay);
        HideTip();
    }
    internal static void ClosePopup()
    {
        var p = Popup; Popup = null;
        p?.Destroy();
    }

    // ---------- Tooltip ----------
    private static void UpdateTip()
    {
        var target = Hover;
        if (target != tipTarget) { tipTarget = target; tipSince = Now; HideTip(); }
        if (target == null || Pressed != null || Popup != null || DragPanel != null || ResizePanel != null || Now - tipSince < .45f) return;
        string? text = target.TooltipNow();
        if (string.IsNullOrEmpty(text)) { HideTip(); return; }
        if (tipBox == null)
        {
            tipBox = new BoxView(tipLayer!, "Tip", Tok.Radius, ShapeRaster.Corners.All, ring: false); tipBox.Set(Tok.Tooltip, Rgba.Clear);
            tipText = Draw.Text(Draw.Node("Text", tipBox.Rt), TextStyle.Body); tipText.fontSize = Tok.FontValue; tipText.color = Draw.C(Tok.Ink);
            tipText.textWrappingMode = TextWrappingModes.Normal; tipText.overflowMode = TextOverflowModes.Overflow; tipText.alignment = TextAlignmentOptions.TopLeft;
        }
        if (text != tipShown)
        {
            tipShown = text!;
            Draw.SetText(tipText, text);
            float w = MathF.Min(Tok.TooltipMaxW, Draw.Width(tipText!, text!) + 22);
            float h = Draw.WrappedHeight(tipText!, text!, w - 20) + 16;
            var r = KitMath.PlaceTooltip(Pointer, w, h, ViewW, ViewH);
            tipBox.Place(r.X, r.Y, r.W, r.H);
            Draw.Place(tipText!.rectTransform, 10, 8, w - 20, h - 16);
        }
        if (!tipBox.Rt.gameObject.activeSelf) tipBox.Rt.gameObject.SetActive(true);
    }
    private static void HideTip()
    {
        tipShown = "";
        if (tipBox != null) try { tipBox.Rt.gameObject.SetActive(false); } catch { tipBox = null; tipText = null; }
    }

    // ---------- Cursor ----------
    private static void SaveCursor()
    {
        if (cursorSaved) return;
        try { savedLock = Cursor.lockState; savedVisible = Cursor.visible; cursorSaved = true; } catch { cursorSaved = false; }
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] Cursor saved {savedLock}/{savedVisible}; freed while a panel is open.");
    }
    private static void RestoreCursor()
    {
        if (!cursorSaved) return;
        cursorSaved = false;
        if (skipCursorRestore || Context != UiContexts.Game) return; // never lock the cursor in the designer or a menu
        try { Cursor.lockState = savedLock; Cursor.visible = savedVisible; } catch { }
        if (Plugin.Detailed) Plugin.Trace($"[UI Kit] Cursor restored {savedLock}/{savedVisible}.");
    }

    // ---------- Positions ----------
    private static ConfigFile Positions => positions ??= new ConfigFile(Path.Combine(BepInEx.Paths.ConfigPath, UiKit.PluginGuid + ".panels.cfg"), true);
    private const string PositionNote = "Last panel position (x,y in 1080p reference px). Delete to re-centre.";
    private static ConfigEntry<string> PositionEntry(Panel panel) => Positions.Bind("Positions", KitMath.PositionKey(panel.ModGuid, panel.Id), "", PositionNote);
    /// <summary>
    /// Remembered state (0.1.4): "x,y" as in 0.1.3, "x,y,w,h" after a resize, "x,y,w,h,max" while
    /// maximized (x,y,w,h = the restore rectangle). 0.1.3 lines with only a position stay valid.
    /// </summary>
    internal static bool TryLoadState(Panel panel, out PanelState state)
    {
        state = default;
        try { return Window.TryParse(PositionEntry(panel).Value, out state); }
        catch (Exception ex) { Guard.Report("Panel position load", ex); return false; }
    }
    internal static void SavePosition(Panel panel)
    {
        try
        {
            var entry = PositionEntry(panel);
            string v = Window.Format(panel.State);
            if (entry.Value != v) entry.Value = v;
        }
        catch (Exception ex) { Guard.Report("Panel position save", ex); }
    }
    /// <summary>Remove the remembered line (Panel.ResetPosition).</summary>
    internal static void ForgetPosition(Panel panel)
    {
        try
        {
            var entry = PositionEntry(panel);
            if (Positions.Remove(entry.Definition)) Positions.Save();
        }
        catch (Exception ex) { Guard.Report("Panel position reset", ex); }
    }

    internal static void Shutdown()
    {
        shuttingDown=true;
        ReleaseKeyboard("plugin unload");
        SafeReset();
        foreach (var p in Panels.ToList()) p.Close("closed: shutdown");
        ReleaseKeyboard("plugin unloaded");
        RestoreCursor();
        if (root != null) Object.Destroy(root);
        HudLayer.Shutdown();
        root = null; canvas = null; panelLayer = overlay = tipLayer = null;
        Assets.DestroyAll();
    }
}

/// <summary>
/// While a kit text field has focus: (1) select a hidden object that carries a disabled
/// TMP_InputField, so the Keybinds API (and code checking EventSystem selection) treats the
/// keyboard as busy; (2) disable enabled non-UI game InputActions (JSON Editor pattern), skipping
/// the Keybinds API's own assets. Everything is restored on blur.
/// </summary>
internal static class TypingGuard
{
    private static GameObject? proxy;
    internal static int Suspended => KeyboardGuard.Suspended;
    internal static void Begin()
    {
        try
        {
            var es = EventSystem.current;
            if (es != null)
            {
                if (proxy == null)
                {
                    proxy = new GameObject("SprocketUiKit.TypingProxy");
                    proxy.SetActive(false);
                    Object.DontDestroyOnLoad(proxy);
                    var field = proxy.AddComponent<TMP_InputField>();
                    field.enabled = false;
                }
                es.SetSelectedGameObject(proxy);
            }
        }
        catch (Exception ex) { Kit.ReleaseKeyboard("typing proxy exception"); Guard.Report("Typing proxy", ex); return; }
        Tick();
    }
    internal static void Tick() => KeyboardGuard.Tick("typing");
    internal static void End()
    {
        KeyboardGuard.Tick("text blur");
        try
        {
            var es = EventSystem.current;
            if (es != null && proxy != null && es.currentSelectedGameObject == proxy) es.SetSelectedGameObject(null);
        }
        catch (Exception ex) { Kit.ReleaseKeyboard("typing proxy release exception"); Guard.Report("Typing proxy release", ex); }
    }
}
