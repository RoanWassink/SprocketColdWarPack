using UnityEngine;
namespace SprocketUiKit;

/// <summary>Demo tabs for the 0.1.2 features: HUD, canvas, slider, confirm, form errors, pages.</summary>
internal static class Demo012
{
    internal const int FirstTab = 4; // tabs 0..3 are the 0.1.0 tabs
    internal static readonly string[] Tabs = { "HUD", "Canvas", "Slider", "Confirm", "Form", "Pages" };
    private static HudPanel? hud; private static WorldMarker? marker;
    private static bool hudOn, markerOn;
    private static double hudValue = .4;
    // 0.1.8: HUD reload example (4 s fill, 1 s full/green), with a cached centre text (no per-frame string)
    private static readonly Color ReloadAccent = new Color32(0xdc, 0x9e, 0x33, 0xff), ReloadDone = new Color32(0x5f, 0xe0, 0x7a, 0xff);
    private static string reloadText = "4.0"; private static int reloadTenths = -1;
    private static float ReloadFraction() => Math.Clamp((Time.unscaledTime % 5f) / 4f, 0f, 1f);
    private static string ReloadText()
    {
        int tenths = (int)MathF.Ceiling(MathF.Max(0f, 4f - Time.unscaledTime % 5f) * 10f);
        if (tenths != reloadTenths) { reloadTenths = tenths; reloadText = (tenths / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); }
        return reloadText;
    }
    // canvas
    private static readonly List<Vector2> route = new() { new(-120, 60), new(-40, -10), new(60, 20), new(140, -60) };
    private static readonly List<(Vector2 P, int Button)> clicks = new();
    private static Texture2D? topo; private static int topoRow; private static float nextTopoRow;
    private static string canvasInfo = "Middle-drag pans, wheel zooms at the cursor, left/right click adds a marker (Shift = red).";
    // slider
    private static double angle = 45, armour = 80, fine = .5, maxArmour = 150;
    private static string liveText = ""; private static int liveKeys;
    // form
    private static string shellName = "", velocity = "900";
    private static double calibre = 75;
    private static int savedCount;
    // pages
    private static string pageNote = "";

    internal static void Build(Panel p, Func<int> tab, Action<string> log)
    {
        BuildHud(p, tab, log);
        BuildCanvas(p, tab, log);
        BuildSlider(p, tab, log);
        BuildConfirm(p, tab, log);
        BuildForm(p, tab, log);
        BuildPages(p, tab, log);
    }
    internal static void Unregister()
    {
        if (hud != null) { UiKit.DestroyHud(hud); hud = null; }
        if (marker != null) { UiKit.DestroyMarker(marker); marker = null; }
        if (topo != null) { UnityEngine.Object.Destroy(topo); topo = null; }
    }

    // ---------- HUD (#1) and world marker (#11) ----------
    private static void BuildHud(Panel p, Func<int> tab, Action<string> log)
    {
        var c = p.Columns(0, 0);
        c[0].Parent!.VisibleWhen(() => tab() == FirstTab);
        var s = c[0].Section("HUD overlay");
        s.Label("A HUD never takes clicks, never frees the cursor and ignores Esc. This demo HUD is allowed in every context and stays up while panels are open (HideWhenCursorFree = false); a mod HUD defaults to game only.", TextStyle.Small).Wrap();
        s.FieldRow("Show demo HUD").Toggle(() => hudOn, v => { hudOn = v; EnsureHud(); if (v) hud!.Show(); else hud!.Hide(); log("HUD " + v); });
        s.FieldRow("Corner").Segmented(new[] { "Top left", "Top right", "Bottom left", "Bottom right" }, () => (int)(hud?.Corner ?? HudCorner.TopLeft), i => { EnsureHud(); hud!.Anchor((HudCorner)i, 24, 24); });
        s.FieldRow("Value shown").Slider(() => hudValue, v => hudValue = v, 0, 1, .01).Width(260);
        s.FieldRow("Background").Slider(() => hud?.Opacity ?? .82, v => { EnsureHud(); hud!.Opacity = (float)v; }, 0, 1, .01).Width(260);
        var m = c[1].Section("World marker");
        m.Label("Ring and label above a world point (here: 1.5 m above the world origin, where the designer vehicle stands). Hidden behind the camera.", TextStyle.Small).Wrap();
        m.FieldRow("Show marker").Toggle(() => markerOn, v =>
        {
            markerOn = v;
            if (marker == null) marker = UiKit.CreateMarker(UiKit.PluginGuid, "demo-marker").At(() => new Vector3(0, 1.5f, 0)).Label(() => "Origin + 1.5 m").Visible(() => markerOn);
            log("marker " + v);
        });
    }
    private static void EnsureHud()
    {
        if (hud != null) return;
        hud = UiKit.CreateHud(UiKit.PluginGuid, "demo-hud", 300).Anchor(HudCorner.TopRight, 24, 24).Visible(() => hudOn);
        hud.AllowedContexts = UiContexts.All; hud.HideWhenCursorFree = false;
        hud.Body.Label("DEMO HUD", TextStyle.BlockTitle);
        var r = hud.Body.Row(8);
        r.Lamp(() => Time.unscaledTime % 1f < .5f, "Blinking").Kind(() => hudValue > .8 ? LampKind.Error : hudValue > .5 ? LampKind.Warning : LampKind.On);
        r.Spacer();
        r.Chip(() => $"{hudValue * 100:0}%", ChipStyle.Accent);
        hud.Body.Progress(() => (float)hudValue);
        hud.Body.Label(() => $"t = {Time.unscaledTime:0.0} s", TextStyle.Value).Outline();
        hud.Body.Label("RELOAD", TextStyle.BlockTitle);
        var reload = hud.Body.Row(10);
        reload.Radial(ReloadFraction, 56, ReloadText).Thickness(7)
            .Colour(() => ReloadFraction() >= 1f ? ReloadDone : ReloadAccent)
            .Tooltip("Reload: the ring fills in four seconds, shows the seconds left and turns green when full.");
        reload.Radial(() => .65f, 44).Thickness(5).Tooltip("Fixed 65 percent.");
        reload.Radial(() => .35f, 32).Thickness(4).Track(new Color32(0x4a, 0x45, 0x36, 0xff)).Tooltip("Fixed 35 percent, with a lighter track.");
        hud.Body.Label("56 / 44 / 32 px", TextStyle.Faint);
    }

    // ---------- Canvas (#2) ----------
    private static void BuildCanvas(Panel p, Func<int> tab, Action<string> log)
    {
        var c = p.Columns(0, 300);
        c[0].Parent!.VisibleWhen(() => tab() == FirstTab + 1);
        var s = c[0];
        s.Label("Canvas: map style (pan, zoom, click)", TextStyle.Section);
        CanvasView? view = null;
        view = s.Canvas(200, d =>
        {
            if (topo != null) d.Image(topo, -200, -150, 400, 300);
            for (int x = -200; x <= 200; x += 50) d.Line(x, -150, x, 150, new Color(1, 1, 1, .08f));
            for (int y = -150; y <= 150; y += 50) d.Line(-200, y, 200, y, new Color(1, 1, 1, .08f));
            d.Rect(-200, -150, 400, 300, UiKit.Colour("#5c5442"), 1);
            d.Polyline(route, UiKit.Colour("#dc9e33"), 2.5f, 8);
            for (int i = 0; i < route.Count; i++) d.Dot(route[i].x, route[i].y, 4, UiKit.Colour("#f3cf86"));
            d.FillPolygon(new[] { new Vector2(40, 60), new Vector2(110, 50), new Vector2(120, 120), new Vector2(50, 130) }, new Color(.37f, .88f, .48f, .25f));
            d.Circle(80, 90, 40, UiKit.Colour("#5fe07a"), 1.5f);
            float t = d.Time;
            var mover = new Vector2(MathF.Cos(t) * 120, MathF.Sin(t * .7f) * 80);
            d.Dot(mover.x, mover.y, 6, UiKit.Colour("#e07a4f"), Color.black, 1.5f);
            d.Text(mover.x, mover.y - 14, "Moving", Color.white, TextStyle.Small, TextAnchor.LowerCenter);
            foreach (var (pt, b) in clicks) { d.Dot(pt.x, pt.y, 5, b == 1 ? UiKit.Colour("#e07a4f") : UiKit.Colour("#5fe07a"), Color.black); }
            d.Text(-195, -145, "Topo texture builds row by row", UiKit.Colour("#b8ad94"), TextStyle.Small, TextAnchor.UpperLeft);
        }).PanZoom(2, .25f, 8).Fps(25).Fill()
          .OnClick(e => { clicks.Add((e.World, e.Shift ? 1 : e.Button)); if (clicks.Count > 40) clicks.RemoveAt(0); log($"canvas click b{e.Button} at world {e.World.x:0},{e.World.y:0}{(e.Shift ? " +Shift" : "")}"); })
          .OnDrag(e => { if (e.Button == 0) canvasInfo = $"left drag {e.Delta.x:0},{e.Delta.y:0} at {e.World.x:0},{e.World.y:0}"; })
          .OnWheel((n, e) => canvasInfo = $"wheel {n:+0.#;-0.#} at world {e.World.x:0},{e.World.y:0}");
        s.Label(() => canvasInfo, TextStyle.Small);
        s.Label(() => view == null ? "" : $"zoom {view.Zoom:0.00}  hover {(view.HoverPos is { } h ? $"{view.ToWorld(h).x:0},{view.ToWorld(h).y:0}" : "-")}  last redraw {view.LastDrawMs:0.0} ms", TextStyle.Faint);
        p.OnOpen += () => view?.FitWorld(-210, -160, 210, 160);
        // diagram (composer style): fixed size, redraw on version change, regions with tooltips
        var dgm = c[1].Section("Diagram (redraws on version)");
        int version = 0; double tilt = 30;
        dgm.Diagram(272, 180, () => version, d =>
        {
            d.ViewBox(0, 0, 300, 200);
            float[] plate = Plate(tilt);
            d.Poly(plate, "#3d3c35", "#8f8670", 1);
            d.Hatch(plate, "#5c5442", 45, 6, 1);
            d.Region("plate", plate);
            d.Arrow(20, 100, 140, 100, "#dc9e33", 2, 10);
            d.Text(150, 30, $"{tilt:0}°", TextStyle.Label, "#fff0cc", TextAnchor.MiddleCenter, 0, "#14150f");
            d.Line(150, 10, 150, 190, "#e9c27a", 1, 4);
        }).RegionTooltip(id => id == "plate" ? $"Armour plate, tilt {tilt:0}°" : null);
        dgm.FieldRow("Tilt").Slider(() => tilt, v => { tilt = v; version++; }, 0, 80, 1, "°").Width(200);
        // topo texture filled row by row (AI Orders requirement: Invalidate after tex.Apply)
        dgm.Button("Build topo texture", () =>
        {
            topo ??= new Texture2D(128, 96, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            topoRow = 0; nextTopoRow = 0; log("topo start");
        }).Compact();
        dgm.Label(() => topo == null ? "" : topoRow < 96 ? $"row {topoRow}/96" : "topo complete", TextStyle.Faint);
        p.Body.Add(new Ticker(() =>
        {
            if (topo == null || topoRow >= 96 || Time.unscaledTime < nextTopoRow) return;
            nextTopoRow = Time.unscaledTime + .02f;
            for (int x = 0; x < 128; x++)
            {
                float h = Mathf.PerlinNoise(x * .05f, topoRow * .05f);
                topo.SetPixel(x, 95 - topoRow, new Color(.2f + h * .3f, .25f + h * .35f, .15f + h * .2f, 1));
            }
            topo.Apply(false); topoRow++;
            view?.Invalidate();
        }));
    }
    private static float[] Plate(double tiltDeg)
    {
        float t = (float)(tiltDeg * Math.PI / 180), cx = 150, cy = 100, hl = 70, hw = 10;
        float ux = MathF.Sin(t), uy = -MathF.Cos(t), nx = MathF.Cos(t), ny = MathF.Sin(t);
        return new[] { cx - ux * hl - nx * hw, cy - uy * hl - ny * hw, cx + ux * hl - nx * hw, cy + uy * hl - ny * hw, cx + ux * hl + nx * hw, cy + uy * hl + ny * hw, cx - ux * hl + nx * hw, cy - uy * hl + ny * hw };
    }

    // ---------- Slider (#3) ----------
    private static void BuildSlider(Panel p, Func<int> tab, Action<string> log)
    {
        var c = p.Columns(0, 0);
        c[0].Parent!.VisibleWhen(() => tab() == FirstTab + 2);
        var s = c[0].Section("Slider");
        s.Label("Drag, or click the track. Shift while dragging = fine. Wheel = one step, Shift+wheel = 10 steps.", TextStyle.Small).Wrap();
        s.Labeled("Hinge angle (0-180°, step 1)").Slider(() => angle, v => angle = v, 0, 180, 1, "°").OnDragEnd(() => log($"angle {angle:0}° (drag end)"));
        s.Labeled("Armour with linked field (live max)").Slider(() => armour, v => armour = v, () => 5, () => maxArmour, 5, "mm").WithField();
        s.FieldRow("Max armour").Stepper(() => maxArmour, v => maxArmour = v, 50, 300, 10, "mm").Compact().FastStep(50);
        s.Labeled("Fine 0-1, step 0.01, custom format").Slider(() => fine, v => fine = v, 0, 1, .01).Format(v => $"{v * 100:0}%");
        s.Labeled("Disabled").Slider(() => 30, _ => { }, 0, 100, 1).EnabledWhen(() => false);
        var r = c[1].Section("Stepper fast step");
        r.Label("Shift+click = 10 per step, Ctrl+click = 50 (FastStep).", TextStyle.Small).Wrap();
        r.FieldRow("Range").Stepper(() => maxArmour, v => maxArmour = v, 50, 300, 1, "mm").FastStep(10, 50);
        // 0.1.4 (#16): live field (callback per keystroke, error while typing) and a slider without value box
        s = c[1].Section("Live field (0.1.4)");
        s.Labeled("OnFieldChange: called per keystroke").Slider(() => armour, v => armour = v, () => 5, () => maxArmour, 5, "mm")
            .WithField(72).OnFieldChange(t => { liveText = t; liveKeys++; })
            .Error(() => liveText.Length > 0 && (!Logic.TryParseNumber(liveText, out var lv) || lv < 5 || lv > maxArmour) ? $"Typing: between 5 and {maxArmour:0} mm." : null);
        s.Label(() => $"field text '{liveText}', {liveKeys} keystrokes", TextStyle.Faint);
        var hv = s.FieldRow("HideValue (no 64 px box)");
        hv.Slider(() => fine, v => fine = v, 0, 1, .01).HideValue().Width(220);
        hv.Label(() => $"{fine * 100:0}%", TextStyle.Value).Width(40);
    }

    // ---------- Confirm (#4) ----------
    private static void BuildConfirm(Panel p, Func<int> tab, Action<string> log)
    {
        var c = p.Columns(0);
        c[0].Parent!.VisibleWhen(() => tab() == FirstTab + 3);
        var s = c[0].Section("Confirm and modal");
        s.Label("Danger confirm: Esc and Enter cancel (safe default). Normal confirm: Enter confirms. The title bar stays draggable; clicks under the card are blocked.", TextStyle.Small).Wrap();
        var r = s.Row(8);
        r.Button("Delete shell...", () => p.Confirm("Delete shell", "Delete 'AP-T 75 mm'? This cannot be undone.", "Delete", () => { p.SetStatus("Deleted (demo)", StatusKind.Success); log("confirm delete"); }, ButtonStyle.Danger, "Back", () => log("cancel delete")), ButtonStyle.Danger);
        r.Button("Overwrite...", () => p.Confirm("Overwrite preset", "A preset with this name exists. Overwrite it?", "Overwrite", () => { p.SetStatus("Overwritten (demo)", StatusKind.Success); log("confirm overwrite"); }, ButtonStyle.Primary, "Cancel"));
        r.Button("Custom modal...", () => p.ShowModal("Rename", m =>
        {
            m.Labeled("New name").TextField(() => shellName, v => shellName = v, "Name").MaxChars(24);
            var b = m.Row(8); b.Spacer();
            b.Button("Close", () => p.CloseModal());
        }));
        s.Label(() => p.ModalOpen ? "Modal open" : "No modal", TextStyle.Faint);
    }

    // ---------- Form with errors (#5) ----------
    private static void BuildForm(Panel p, Func<int> tab, Action<string> log)
    {
        var c = p.Columns(0, 0);
        c[0].Parent!.VisibleWhen(() => tab() == FirstTab + 4);
        var s = c[0].Section("Shell form (field errors)");
        s.Labeled("Name").TextField(() => shellName, v => shellName = v, "Shell name").MaxChars(24)
            .Error(() => shellName.Trim().Length == 0 ? "A name is required." : null);
        s.Labeled("Muzzle velocity (m/s)").TextField(() => velocity, v => velocity = v, "m/s").Numeric()
            .Error(() => !Logic.TryParseNumber(velocity, out var v) ? "Not a number." : v < 100 || v > 2000 ? "Between 100 and 2000 m/s." : null);
        s.FieldRow("Calibre").Stepper(() => calibre, v => calibre = v, 5, 200, 5, "mm")
            .Error(() => calibre > 150 ? "Over 150 mm needs a heavy mount." : null);
        var b = s.Row(8);
        b.Button("Save", () => { savedCount++; p.SetStatus("Saved " + shellName, StatusKind.Success); log("form saved"); }, ButtonStyle.Primary).EnabledWhen(() => !p.HasErrors);
        b.Label(() => p.FirstError is { } e ? "Fix: " + e.Message : "No errors", TextStyle.Small);
        var w = c[1].Section("Warning severity, colours");
        w.Warning("Error severity (default look)");
        w.Warning("Warning severity").Severity(StatusKind.Warning);
        w.Warning("Info severity, clickable").Severity(StatusKind.Info).OnClick(() => log("warning row clicked"));
        var cards = w.Row(8).ShrinkToFit();
        cards.Chip("Rare", UiKit.Colour("#5aa0ff")); cards.Chip("Epic", UiKit.Colour("#b77cff")); cards.Chip("Legendary", UiKit.Colour("#e9c27a"));
        cards.Label("A long label in a shrink-to-fit row gets an ellipsis instead of pushing the chips off", TextStyle.Small);
        w.Card().Accent(UiKit.Colour("#b77cff")).Label("Card with accent colour (Epic)", TextStyle.Label);
        w.Card().Stripe(() => calibre > 150 ? "#e07a4f" : "#5fe07a").Label(() => $"Card stripe from a getter ({calibre:0} mm)", TextStyle.Label);
        w.List(() => new[] { "Common", "Rare", "Epic" }, (n, row) =>
        {
            row.Title = n; row.Swatch = n == "Rare" ? "#5aa0ff" : n == "Epic" ? "#b77cff" : "#b8ad94";
            row.Lamp = true; row.LampKind = n == "Epic" ? LampKind.Warning : LampKind.On;
            if (n == "Epic") row.AccentColor = UiKit.Colour("#b77cff");
        });
    }

    // ---------- Pages (#6) ----------
    private static void BuildPages(Panel p, Func<int> tab, Action<string> log)
    {
        var c = p.Columns(0);
        c[0].Parent!.VisibleWhen(() => tab() == FirstTab + 5);
        var s = c[0].Section("Sub-pages");
        s.Label("Open a page: the title bar shows a back button and the trail; Esc goes back one page, then closes.", TextStyle.Small).Wrap();
        s.Button("Open 'Shell list'", () => p.Push("shells"), ButtonStyle.Primary);
        s.Label(() => $"Current page: {p.CurrentPage ?? "(root)"}, depth {p.PageDepth}. {pageNote}", TextStyle.Faint);
        var list = p.Page("shells", "Shell list").Columns(0)[0];
        var ls = list.Section("Shells", () => 3);
        foreach (var name in new[] { "AP-T 75 mm", "APHE 75 mm", "HE 75 mm" })
            ls.Card().OnClick(() => { pageNote = "selected " + name; p.Push("edit"); }).Label(name, TextStyle.Label);
        list.Footer().Button("Back", () => p.Pop());
        var edit = p.Page("edit", "Edit shell").Columns(0)[0];
        var es = edit.Section("Edit");
        es.SectionTitle(() => "Edit: " + pageNote.Replace("selected ", ""));
        es.Labeled("Velocity").Slider(() => fine * 2000, v => fine = v / 2000, 0, 2000, 10, "m/s");
        es.Button("Advanced (third page)", () => p.Push("adv"));
        var adv = p.Page("adv", "Advanced").Columns(0)[0];
        var hideMe = adv.Section("Optional section");
        bool show = true;
        adv.Section("Toggle").FieldRow("Show optional section").Toggle(() => show, v => show = v);
        hideMe.SectionVisibleWhen(() => show).Label("This whole section (heading included) hides with the switch.", TextStyle.Small).Wrap();
        adv.Button("Back to root", () => p.PopToRoot());
    }
}

/// <summary>Invisible element that runs an action every frame while its page is shown (demo helper).</summary>
internal sealed class Ticker : Element
{
    private readonly Action tick;
    internal Ticker(Action tick) { this.tick = tick; }
    protected override void Build() { }
    internal override float MeasureHeight(float width) => 0;
    internal override float IntrinsicWidth() => 0;
    internal override void Refresh(float now) => Guard.Run("Demo ticker", tick);
}
