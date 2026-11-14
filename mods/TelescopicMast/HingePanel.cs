using System.Globalization;
using System.Text.Json.Nodes;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Control;
using SprocketKeybinds;
using SprocketTelescopicMast.Io;
using SprocketUiKit;
using UnityEngine;
namespace SprocketTelescopicMast;

/// <summary>Injected MonoBehaviour: polls the hinge action keys, refreshes the panel data, re-seats leaves.</summary>
public sealed class HingeHost : MonoBehaviour
{
    public HingeHost(IntPtr pointer) : base(pointer) { }
    public void Update()
    {
        try
        {
            // While a kit text field has focus, typed digits are not I/O Flow / Action presses.
            HingeKeys.Suspended = UiKit.TextInputActive;
            HingeKeys.Poll();
            HingeRuntime.PrepareExternal();
            HingeRuntime.PrepareFlows();
            HingePanel.Update();
        }
        catch (Exception ex) { HingeRuntime.Warn("Panel update", ex); }
    }
    public void LateUpdate()
    {
        try
        {
            // Own tick, after the native Update phase (was HingeCoreHooks.Tick on GameUpdate).
            HingeRuntime.TickAll();
            HingeRuntime.Maintain();
            RotatorRuntime.Maintain();
        }
        catch (Exception ex) { HingeRuntime.Warn("Hinge seating", ex); }
    }
}

/// <summary>Retained Kit graph; editing rules and the designer sandbox live in the pure core.</summary>
internal static class HingePanel
{
    internal const string Title = "I/O Flow";
    private static Panel? panel;
    private static NodeGraph? graph;
    private static FlowPort? shownPending;
    private static FlowPanelData? data;
    private static FlowEditor? editor;
    private static FlowPreview preview = new();
    // Undo/redo per vehicle flow while the panel is open (cleared on close).
    private static readonly Dictionary<FlowDocument, FlowHistory> histories = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<string, (Vector2 Pan, float Zoom)> views = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (double X, double Y)> drawnAt = new(StringComparer.Ordinal);
    private static string? drawnSheet;
    private static string[] tabLabels = Array.Empty<string>();
    private static string notice = "";
    private static float noticeUntil;
    private static readonly Dictionary<string, GraphNode> nodes = new();
    private static readonly Dictionary<FlowPort, GraphPort> ports = new();
    private static readonly Dictionary<FlowWire, string> wires = new();
    private static IReadOnlyList<IInputType> inputs = Array.Empty<IInputType>();
    private static IReadOnlyList<FlowPart> parts = Array.Empty<FlowPart>();
    private static IReadOnlyList<FlowNode> inspector = Array.Empty<FlowNode>();
    private static int registryRevision = -1, wireSerial;
    private static long drawnRev = -1;
    private static float nextRefresh;
    private static bool wasDesigner;
    private static string status = "", live = "";
    private static bool Designer => UiKit.Context == UiContexts.Designer;
    private static bool Editable => editor != null && !editor.Flow.ReadOnly;
    private static FlowEngine? Engine => Designer ? preview.Engine : data?.Engine;
    internal static bool IsOpen => panel?.IsOpen == true;
    internal static void Create(ModKeybind key)
    {
        // 0.9.2: shown as "I/O Flow"; the panel id stays "hinge-io" so position/size settings survive.
        panel = UiKit.CreatePanel(Plugin.ModGuid, "hinge-io", Title, "Telescopic Mast", 1280, 800);
        panel.TitleIcon = Icon.Hinge;
        UiKit.BindToggle(panel, key);
        panel.SetContext(() => Designer ? "Designer - simulation" : "Vehicle - live");
        Build();
        panel.OnOpen += () => { nextRefresh = 0; Refresh(); graph?.FitAll(); };
        panel.OnClose += () => { editor?.Cancel(); SyncSelection(); preview = new(); drawnRev = -1; ClearHistories(); views.Clear(); notice = ""; };
        // UI Kit 0.1.11 dispatches these only while this panel owns keyboard focus and its guard has
        // suspended the game actions; no consume parameter is needed.
        panel.AddShortcut("Ctrl+Z", Undo);
        panel.AddShortcut("Ctrl+Y", Redo);
        panel.AddShortcut("Ctrl+Shift+Z", Redo);
    }
    private static void Build()
    {
        var columns = panel!.Columns(212, 0, 272);
        var library = columns[0].ScrollView().Fill();
        library.Label("TEMPLATES", TextStyle.Section).Colour("#f3cf86");
        foreach (var template in FlowTemplates.All)
            library.Button(template.Title, () => PlaceTemplate(template.Id)).EnabledWhen(() => Editable && FlowTemplates.Target(template.Id, editor!.Flow, editor.Node, parts) != null);
        library.Label("INPUTS", TextStyle.Section).Colour("#79d8e8");
        library.Repeat(() => inputs, (input, row) => row.Button(input.DisplayName, () => Place(input.Id)).EnabledWhen(() => Editable));
        library.Label("LOGIC", TextStyle.Section).Colour("#c9bc9c");
        foreach (string type in FlowPresentation.Logic)
            library.Button(FlowPresentation.Name(type), () => Place(type)).EnabledWhen(() => Editable);
        library.Label("OUTPUTS", TextStyle.Section).Colour("#f3cf86");
        library.Repeat(() => parts, (part, stack) =>
        {
            var row = stack.Row(4);
            row.Button(part.Title, () => Place(part.Type, part)).Flex().EnabledWhen(() => Editable);
            row.Chip(() => editor?.Flow.Nodes.Any(n => n.PartId == part.Id) == true ? "placed" : part.Provider.Length > 0 ? "API" : "OUT");
        });
        library.Label("Outputs are the hinges, masts, rotators and API parts on this vehicle. Each part is one block.", TextStyle.Faint).Wrap();
        var toolbar = columns[1].Row(6);
        toolbar.Label(Title, TextStyle.Heading).Flex();
        toolbar.Button("Undo", Undo).Compact().EnabledWhen(() => editor?.CanUndo == true).Tooltip("Undo the last change (up to 50 steps while the panel is open).");
        toolbar.Button("Redo", Redo).Compact().EnabledWhen(() => editor?.CanRedo == true).Tooltip("Redo the change you undid.");
        toolbar.Button("-", () => Zoom(-.1f)).Compact().Width(28);
        toolbar.Label(() => Math.Round((graph?.Zoom ?? 1) * 100) + "%", TextStyle.Small).Width(45);
        toolbar.Button("+", () => Zoom(.1f)).Compact().Width(28);
        toolbar.Button("Fit", () => graph?.FitAll()).Compact();
        var tabs = columns[1].Row(6);
        // The labels getter lets the Kit update (and rename) the same tab elements in place.
        tabs.Tabs(() => tabLabels, () => editor == null ? 0 : Math.Max(0, IndexOf(editor.ActiveSheet)), SelectTab).Flex();
        tabs.Button("+", AddTab).Compact().Width(28).EnabledWhen(() => Editable).Tooltip("New tab");
        var sheet = columns[1].Row(6);
        sheet.Label("Tab name", TextStyle.Small).Width(64);
        sheet.TextField(() => editor?.Sheets.FirstOrDefault(s => s.Id == editor.ActiveSheet)?.Name ?? "", RenameTab).MaxChars(FlowSheets.MaxName).Tooltip("Tab name").Flex().EnabledWhen(() => Editable);
        sheet.Button("Delete tab", DeleteTab, ButtonStyle.Danger).Compact().EnabledWhen(() => Editable && editor!.Sheets.Count > 1);
        graph = columns[1].NodeGraph().Fill().Snap(8).ZoomRange(.5f, 1.5f).Hint(() => editor?.Hint)
            .PortHitRadius((float)FlowLayout.PortHitPixels)
            .OnClick(Click).OnPortDrag(PortDrag).OnNodeMoved((id, pos, final) => editor?.Move(id, pos.x, pos.y, final));
        graph.Tooltip("Drag a title to move a block, or a port to wire it. Drag empty canvas to pan; use the wheel to zoom.");
        var right = columns[2].ScrollView().Fill();
        var help = right.Stack(); help.VisibleWhen(() => editor?.Node == null && editor?.SelectedWire == null);
        help.Label("How it works", TextStyle.Heading);
        foreach (string text in new[] {
            "Start anywhere: an input, a logic block or an output. Order does not matter.",
            "1. Click a block in the library to place it. It lands in free space on the open tab.",
            "2. Drag a block by its title bar. Drag a port to a port to draw a wire, or drag empty canvas to pan.",
            "3. Or click an output port (right), then an input port (left). A drop or click close to a port counts. An input takes one wire: a new wire replaces the old one. An output can feed many.",
            "Tabs group the flow by function; all tabs run together. A template opens in a new tab. Wires stay inside one tab.",
            "4. Press Test or Fire on an input. Lit wires carry a signal, so you can try the logic before driving.",
            "The flow is saved with the vehicle automatically." }) help.Label(text, TextStyle.Body).Wrap();
        help.Warning("Add an I/O part to this vehicle to start a saved flow.").VisibleWhen(() => data == null);
        right.Warning("This flow uses a future format and is read-only.").VisibleWhen(() => editor?.Flow.ReadOnly == true);
        right.Repeat(() => inspector, BuildInspector);
        var wire = right.Stack(); wire.VisibleWhen(() => editor?.SelectedWire != null);
        wire.Label("Wire", TextStyle.Heading);
        wire.Label(WireText, TextStyle.Body).Wrap();
        wire.Warning(FlowPresentation.MomentaryWarning).VisibleWhen(() => editor?.SelectedWire is { } w && FlowPresentation.MomentaryToLevel(editor.Flow, w));
        wire.Button("Delete wire", () => { editor?.Delete(); Sync(); }, ButtonStyle.Danger).EnabledWhen(() => Editable);
    }
    private static void BuildInspector(FlowNode node, SprocketUiKit.Stack stack)
    {
        bool output = FlowValidation.IsOutput(node);
        stack.Chip(output ? "OUT" : FlowPresentation.Logic.Contains(node.Type) ? "LOGIC" : "IN",
            output ? GraphColours.Out : FlowPresentation.Logic.Contains(node.Type) ? GraphColours.Logic : GraphColours.In);
        stack.Label(FlowPresentation.Name(node.Type), TextStyle.Heading);
        stack.Labeled("Name").TextField(() => FlowPresentation.Title(node), text => { editor?.Rename(node.Id, text); Sync(); }).MaxChars(100).EnabledWhen(() => Editable);
        if (output)
        {
            IReadOnlyList<ParamOption> Choices() => new[] { new ParamOption(node.PartId, parts.FirstOrDefault(p => p.Id == node.PartId)?.Title ?? (data?.Exists(node.PartId) == true ? FlowPresentation.Title(node) : "missing part")) }
                .Concat(parts.Where(p => p.Type == node.Type && p.Id != node.PartId && editor?.Flow.Nodes.Any(n => n.PartId == p.Id) != true).Select(p => new ParamOption(p.Id, p.Title))).ToArray();
            AddDropdown(stack, "Part", Choices, () => node.PartId, value => Change(node, "partId", value));
            if (node.Type != "rotator") stack.Labeled("When the signal arrives").Segmented(FlowPresentation.Modes,
                () => Array.IndexOf(FlowPresentation.Modes, node.Get("mode", "Follow")), i => Change(node, "mode", FlowPresentation.Modes[i])).EnabledWhen(() => Editable);
        }
        if (output) SettingsPanel.Build(stack, () => data?.Settings(node), () => Designer && Editable);
        var parameters = FlowPresentation.Parameters(node);
        if (node.Type == "hinge")
        {
            var settings = stack.Collapsible("Hinge settings").Body;
            foreach (var p in parameters) AddParameter(settings, node, p);
        }
        else foreach (var p in parameters) AddParameter(stack, node, p);
        stack.Label(FlowPresentation.Help(node), TextStyle.Body).Wrap();
        stack.Label(() => data?.TwinHint(node) ?? "", TextStyle.Faint).Wrap().VisibleWhen(() => data?.TwinHint(node) != null);
        if (node.Type is "rotator" or "hinge")
            stack.Warning(FlowPresentation.MomentaryWarning).VisibleWhen(() => editor != null && editor.Flow.Wires.Any(w => w.To == node.Id && FlowPresentation.MomentaryToLevel(editor.Flow, w)));
        var value = stack.Row(6);
        value.Lamp(() => Engine?.Value(node.Id) == true);
        value.Label(() => live, TextStyle.Value).Wrap().Flex();
        string provider = IoApiCore.FindOutput(node.Type)?.Provider ?? IoApiCore.FindInput(node.Type)?.Provider ?? "";
        if (provider.Length > 0) stack.Label("Provided by " + provider + " through the I/O API.", TextStyle.Faint).Wrap();
        stack.Button("Delete block", () => { editor?.Delete(); Sync(); }, ButtonStyle.Danger).EnabledWhen(() => Editable);
    }
    private static void AddParameter(Container container, FlowNode n, ParamDef p)
    {
        if (p.Id == "fireGroup" && n.Type.StartsWith("cannon.", StringComparison.Ordinal))
            AddDropdown(container, p.DisplayName, () => CannonParams.Groups, () => n.Get(p.Id, p.Default), value => Change(n, p.Id, p.Normalize(value)));
        else if (n.Type == "rotator" && p.Id == "direction")
            container.Labeled(p.DisplayName).Segmented(p.Options.ToArray(),
                () => n.Get(p.Id, p.Default) == "CCW" ? 1 : 0,
                i => Change(n, p.Id, i == 1 ? "CCW" : "CW")).EnabledWhen(() => Editable);
        else if (p.Kind == ParamKind.Choice || p.Kind == ParamKind.Enum)
        {
            IReadOnlyList<ParamOption> Options()
            {
                var options = p.Kind == ParamKind.Choice ? p.CurrentOptions() : p.Options.Select(o => new ParamOption(o, o)).ToArray();
                string current = n.Get(p.Id, p.Default);
                return options.Any(o => o.Value == current) ? options : new[] { new ParamOption(current, current + " (not found)") }.Concat(options).ToArray();
            }
            AddDropdown(container, p.DisplayName, Options, () => n.Get(p.Id, p.Default), value =>
            {
                Change(n, p.Id, p.Normalize(value));
                if (p.Id == "cannon" && IoCatalog.CannonFireGroups.TryGetValue(value, out int group)) Change(n, "fireGroup", group.ToString(CultureInfo.InvariantCulture));
            });
        }
        else if (p.Kind == ParamKind.Bool)
            container.Labeled(p.DisplayName).Toggle(() => n.Get(p.Id, p.Default) == "true", value => Change(n, p.Id, value ? "true" : "false")).EnabledWhen(() => Editable);
        else container.Labeled(p.DisplayName).Stepper(() => n.Number(p.Id, double.Parse(p.Default, CultureInfo.InvariantCulture)),
            value => Change(n, p.Id, p.Normalize(value.ToString(CultureInfo.InvariantCulture))), p.Min, p.Max, p.Step,
            p.Id == "t" ? "s" : p.Id.Contains("Angle") || p.Id is "target" or "from" or "to" ? "deg" : p.Id is "speed" or "slow" or "fast" ? "deg/s" : "").EnabledWhen(() => Editable);
    }
    private static void AddDropdown(Container container, string label, Func<IReadOnlyList<ParamOption>> options, Func<string> value, Action<string> set)
    {
        container.Labeled(label).Dropdown(() => options().Select(o => o.Label).ToArray(),
            () => { var list = options(); for (int i = 0; i < list.Count; i++) if (list[i].Value == value()) return i; return -1; },
            i => { var list = options(); if (i >= 0 && i < list.Count) set(list[i].Value); }).EnabledWhen(() => Editable);
    }
    private static void Change(FlowNode node, string key, string value) { editor?.Parameter(node.Id, key, value); Sync(); }
    private static void Zoom(float delta) { if (graph != null) graph.SetView(graph.Pan, graph.Zoom + delta); }
    private static void PlaceTemplate(string id)
    {
        if (editor == null || graph == null) return;
        SetView();
        var view = editor.View!.Value;
        double x = view.X + 32 / Math.Max(.01f, graph.Zoom), y = view.Y + 32 / Math.Max(.01f, graph.Zoom);
        if (editor.Template(id, parts, x, y)) { Sync(); Reveal(editor.Node); }
    }
    private static void Place(string type, FlowPart? part = null)
    {
        if (editor == null || graph == null) return;
        SetView();
        var placed = editor.PlaceFree(type, part); Sync(); Reveal(placed);
    }
    /// <summary>
    /// Placement uses the graph's real visible rectangle in graph units (0.9.4), not a fixed 760x560
    /// picture. The old size survives only as a fallback before the Kit has laid the graph out.
    /// </summary>
    private static void SetView()
    {
        if (editor == null || graph == null) return;
        var r = graph.VisibleRect; float zoom = graph.Zoom > 0 ? graph.Zoom : 1;
        if (r.width > 0 && r.height > 0) { editor.View = new FlowRect(r.x, r.y, r.width, r.height); return; }
        var size = graph.ViewSize;
        double w = size.x > 0 ? size.x / zoom : FlowLayout.ViewWidth, h = size.y > 0 ? size.y / zoom : FlowLayout.ViewHeight;
        editor.View = new FlowRect(-graph.Pan.x / zoom, -graph.Pan.y / zoom, w, h);
    }
    /// <summary>A block placed right of the content (no room in view) is panned into view.</summary>
    private static void Reveal(FlowNode? n)
    {
        if (n == null || graph == null || editor?.View is not { } view || drawnSheet != editor.SheetOf(n)) return;
        var b = FlowLayout.Bounds(n);
        if (view.Contains(b)) return;
        graph.SetView(new Vector2((float)(32 - b.X * graph.Zoom), (float)(32 - b.Y * graph.Zoom)), graph.Zoom);
    }
    private static void Undo() { if (editor?.Undo() == true) { Show("Undone."); Sync(); } }
    private static void Redo() { if (editor?.Redo() == true) { Show("Redone."); Sync(); } }
    private static int IndexOf(string sheet) { var list = editor!.Sheets; for (int i = 0; i < list.Count; i++) if (list[i].Id == sheet) return i; return -1; }
    private static void SelectTab(int index)
    { if (editor != null && index >= 0 && index < editor.Sheets.Count && editor.SelectSheet(editor.Sheets[index].Id)) Sync(); }
    private static void AddTab() { if (editor?.AddSheet() != null) Sync(); }
    private static void RenameTab(string name) { if (editor != null && editor.RenameSheet(editor.ActiveSheet, name)) Sync(); }
    private static void DeleteTab()
    {
        if (editor == null || panel == null) return;
        string id = editor.ActiveSheet; var current = editor;
        var result = editor.DeleteSheet(id);
        if (result == SheetDelete.NeedsConfirm)
        {
            string name = editor.Sheets.First(s => s.Id == id).Name; int count = editor.SheetNodes(id).Count();
            panel.Confirm("Delete tab", "Delete tab '" + name + "' and its " + count + (count == 1 ? " block" : " blocks") + "? Undo brings it back.", "Delete",
                () => { if (editor == current && current.DeleteSheet(id, true) == SheetDelete.Deleted) Sync(); });
        }
        else if (result == SheetDelete.Deleted) Sync();
    }
    private static void Show(string text) { notice = text; noticeUntil = Time.unscaledTime + 5; }
    private static void Click(GraphHit hit)
    {
        if (hit.Button != 0 || editor == null) return;
        FlowPort? clicked = null;
        if (hit.Kind == GraphHitKind.Port && hit.NodeId != null && hit.PortId != null)
        {
            var port = ports.FirstOrDefault(p => p.Key.Node == hit.NodeId && p.Value.Id == hit.PortId);
            if (port.Value != null) clicked = port.Key;
        }
        // A pending wire takes the nearest fitting port within 12 px, also when the click landed on a
        // block body, a wire, the canvas or a port of the wrong direction (0.9.1 cancelled it instead).
        if (editor.Pending is { } pending && clicked != pending && (clicked == null || clicked.Value.Output == pending.Output) && graph != null)
            if (editor.Snap(hit.GraphPos.x, hit.GraphPos.y, FlowLayout.SnapPixels / Math.Max(.01f, graph.Zoom)) is { } snapped) clicked = snapped;
        if (clicked is { } chosen)
        {
            editor.ClickPort(chosen);
            if (editor.Notice != null) Show(editor.Notice);
        }
        else if (hit.Kind == GraphHitKind.Wire)
        { var wire = wires.FirstOrDefault(w => w.Value == hit.WireId).Key; if (wire != null) editor.SelectWire(wire); }
        else if (hit.NodeId != null)
        {
            editor.SelectNode(hit.NodeId);
            if (hit.Kind == GraphHitKind.FooterButton && Designer && editor.Node is { } n)
            {
                preview.Test(n);
                if (IoRegistry.FindInput(n.Type)?.SignalKind == SignalKind.Level || n.Type == "key" && n.Get("signal") == "held") nodes[n.Id].FooterButton = preview.Held(n.Id) ? "Release" : "Hold";
            }
        }
        else editor.Canvas();
        Sync();
    }
    /// <summary>
    /// A completed port drag. It uses the same FlowEditor rules and messages as two clicks, including the
    /// 12 px snap (scaled by zoom) and replacement of an occupied input. A drag that ends on the canvas
    /// without a port within reach changes nothing and reports nothing.
    /// </summary>
    private static void PortDrag(GraphPortDrop drop)
    {
        if (editor == null || graph == null) return;
        if (FindPort(drop.FromNode, drop.FromPort) is not { } start || start.Output != drop.FromOutput) return;
        var target = FindPort(drop.Drop.NodeId, drop.Drop.PortId) ??
            editor.SnapFrom(start, drop.Drop.GraphPos.x, drop.Drop.GraphPos.y, FlowLayout.SnapPixels / Math.Max(.01f, graph.Zoom));
        if (target is not { } to || to == start) return;
        editor.Cancel();
        editor.Drop(start, to);
        Sync();
        if (editor.Notice != null) Show(editor.Notice);
    }
    private static FlowPort? FindPort(string? node, string? port)
    {
        if (node == null || port == null) return null;
        foreach (var p in ports) if (p.Key.Node == node && p.Value.Id == port) return p.Key;
        return null;
    }
    private static string WireText()
    {
        if (editor?.SelectedWire is not { } w) return "";
        var a = editor.Flow.Nodes.FirstOrDefault(n => n.Id == w.From); var b = editor.Flow.Nodes.FirstOrDefault(n => n.Id == w.To);
        return a == null || b == null ? "Unknown wire endpoint." : FlowPresentation.Title(a) + " -> " + FlowPresentation.Title(b) + " (" + FlowPresentation.Port(b, w.Port) + "). Signal " + (Engine?.Value(w.From) == true ? "on." : "off.");
    }
    internal static void Update() { if (IsOpen) Refresh(); }
    private static void Refresh()
    {
        if (graph == null || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .1f;
        var current = HingeRuntime.PanelFlow(Designer, data?.Token);
        bool membership = !parts.SequenceEqual(current?.Parts ?? new List<FlowPart>());
        bool changed = data?.Token != current?.Token || editor?.Flow != current?.Flow || wasDesigner != Designer;
        data = current;
        if (membership) parts = current?.Parts ?? new List<FlowPart>();
        if (changed)
        {
            wasDesigner = Designer; ClearGraph(); nodes.Clear(); ports.Clear(); wires.Clear(); drawnRev = -1;
            preview = new(); drawnSheet = null; views.Clear(); notice = "";
            if (current == null) editor = null;
            else
            {
                if (!histories.TryGetValue(current.Flow, out var history)) histories[current.Flow] = history = new FlowHistory();
                editor = new FlowEditor(current.Flow, history) { Saved = () => data?.Save(), Refused = Refused };
            }
        }
        bool registered = registryRevision != IoRegistry.Revision;
        if (registered)
        {
            registryRevision = IoRegistry.Revision; inputs = IoRegistry.InputTypes;
            // A previously unknown block may have acquired a schema/ports through a late API registration.
            if (!changed) { ClearGraph(); nodes.Clear(); ports.Clear(); wires.Clear(); drawnRev = -1; }
        }
        if (changed || registered || drawnRev != editor?.Flow.Rev) { Sync(); if (changed) graph.FitAll(); }
        if (membership || changed)
        {
            foreach (var n in editor?.Flow.Nodes ?? new List<FlowNode>()) if (nodes.TryGetValue(n.Id, out var node)) node.Dimmed = n.PartId.Length > 0 && !PartPresent(n.PartId);
            if (editor != null) preview.Configure(editor.Flow, PartPresent, parts);
        }
        if (Designer && editor != null && !GameState.Paused()) preview.Tick(Time.unscaledTime);
        UpdateSignals();
    }
    /// <summary>The stack lives while the panel is open: closing empties it (the editor keeps the same object).</summary>
    private static void ClearHistories() { foreach (var h in histories.Values) h.Clear(); histories.Clear(); }
    private static void Refused(string reason) { if (Plugin.Detailed) Plugin.Trace("[FLOW] wire refused " + reason); }
    /// <summary>
    /// Feeds the Kit Tabs labels getter. The Kit caches and edits the same label elements in place, so a
    /// rename or a new tab never rebuilds the tab bar (0.9.4).
    /// </summary>
    private static void UpdateTabs()
    {
        var names = editor?.Sheets.Select(s => s.Name).ToArray() ?? Array.Empty<string>();
        if (!names.SequenceEqual(tabLabels)) tabLabels = names;
    }
    private static void ClearGraph()
    {
        if (graph == null) return;
        if (shownPending != null) graph.EndPendingWire();
        shownPending = null; graph.Clear();
    }
    // Structural edits reconcile handles once. The 10 Hz path below only updates live flags/text.
    private static void Sync()
    {
        if (editor == null || graph == null) { inspector = Array.Empty<FlowNode>(); UpdateTabs(); return; }
        var flow = editor.Flow;
        string sheet = editor.ActiveSheet;
        if (drawnSheet != sheet)
        {
            // Another tab: remember this tab's view, redraw only the new tab's blocks.
            if (drawnSheet != null) views[drawnSheet] = (graph.Pan, graph.Zoom);
            ClearGraph(); nodes.Clear(); ports.Clear(); wires.Clear(); drawnAt.Clear();
            if (views.TryGetValue(sheet, out var view)) graph.SetView(view.Pan, view.Zoom); else if (drawnSheet != null) graph.FitAll();
            drawnSheet = sheet;
        }
        UpdateTabs();
        var shown = editor.SheetNodes(sheet).ToList();
        if (shownPending is { } pending && !shown.Any(n => n.Id == pending.Node))
        { editor.Cancel(); SyncSelection(); }
        foreach (var gone in nodes.Keys.Where(id => !shown.Any(n => n.Id == id)).ToArray())
        {
            graph.RemoveNode(gone); nodes.Remove(gone); drawnAt.Remove(gone);
            foreach (var key in ports.Keys.Where(p => p.Node == gone).ToArray()) ports.Remove(key);
            // The Kit removed the incident wires with the node; forget their handles too.
            foreach (var w in wires.Keys.Where(w => w.From == gone || w.To == gone).ToArray()) wires.Remove(w);
        }
        foreach (var n in shown)
        {
            if (!nodes.TryGetValue(n.Id, out var node))
            {
                bool output = FlowValidation.IsOutput(n), logic = FlowPresentation.Logic.Contains(n.Type);
                node = graph.AddNode(n.Id, (float)FlowPresentation.Coordinate(n, "x"), (float)FlowPresentation.Coordinate(n, "y"), FlowPresentation.Title(n), output ? "OUT" : logic ? "LOGIC" : "IN", output ? GraphColours.Out : logic ? GraphColours.Logic : GraphColours.In);
                nodes[n.Id] = node; drawnAt[n.Id] = (FlowPresentation.Coordinate(n, "x"), FlowPresentation.Coordinate(n, "y"));
                for (int i = 0; i < FlowValidation.Ports(n); i++) ports[new(n.Id, i, false)] = node.AddInput("in" + i, FlowPresentation.Port(n, i));
                if (FlowEditor.HasOutput(n)) ports[new(n.Id, 0, true)] = node.AddOutput("out", "signal");
                if (IoRegistry.FindInput(n.Type) is { } input) node.FooterButton = input.SignalKind == SignalKind.Event ? "Fire" : "Hold";
            }
            // Undo/redo can move a block that is already drawn; a normal drag already sits there.
            var at = (FlowPresentation.Coordinate(n, "x"), FlowPresentation.Coordinate(n, "y"));
            if (drawnAt.TryGetValue(n.Id, out var was) && was != at) { node.Position = new Vector2((float)at.Item1, (float)at.Item2); drawnAt[n.Id] = at; }
            node.Title = FlowPresentation.Title(n);
            node.Dimmed = n.PartId.Length > 0 && !PartPresent(n.PartId);
            if (IoRegistry.FindInput(n.Type) is { } type) node.FooterButton = type.SignalKind == SignalKind.Event && !(n.Type == "key" && n.Get("signal") == "held") ? "Fire" : preview.Held(n.Id) ? "Release" : "Hold";
        }
        foreach (var gone in wires.Keys.Where(w => !flow.Wires.Contains(w)).ToArray()) { graph.RemoveWire(wires[gone]); wires.Remove(gone); }
        foreach (var w in flow.Wires)
            if (!wires.ContainsKey(w) && ports.TryGetValue(new(w.From, 0, true), out var from) && ports.TryGetValue(new(w.To, w.Port, false), out var to))
            { string id = "wire" + ++wireSerial; graph.AddWire(id, w.From, from.Id, w.To, to.Id); wires[w] = id; }
        if (drawnRev != flow.Rev) { preview.Configure(flow, PartPresent, parts); drawnRev = flow.Rev; }
        SyncSelection();
    }
    private static void SyncSelection()
    {
        if (graph == null) return;
        foreach (var n in nodes) n.Value.Selected = n.Key == editor?.SelectedNode;
        foreach (var w in wires) if (graph.Wire(w.Value) is { } wire) wire.Selected = w.Key == editor?.SelectedWire;
        foreach (var p in ports) p.Value.Chosen = p.Key == editor?.Pending;
        var pending = editor?.Pending;
        if (pending != shownPending)
        {
            if (shownPending != null) graph.EndPendingWire();
            shownPending = pending;
            if (pending is { } start && ports.TryGetValue(start, out var chosen)) graph.BeginPendingWire(start.Node, chosen.Id);
        }
        if (editor?.Node is { } selected)
        { if (inspector.Count != 1 || inspector[0] != selected) inspector = new[] { selected }; }
        else inspector = Array.Empty<FlowNode>();
    }
    private static bool PartPresent(string id) => parts.Any(p => p.Id == id) || data?.Exists(id) == true;
    private static void UpdateSignals()
    {
        var engine = Engine;
        if (editor == null || engine == null || graph == null) { SetStatus("0 blocks - 0 wires | Add an I/O part to this vehicle.", false); return; }
        foreach (var n in editor.Flow.Nodes)
            if (nodes.TryGetValue(n.Id, out var node))
            {
                node.Lamp = engine.DisplayValue(n.Id);
                node.FooterText = data?.TwinHint(n) ?? FlowPresentation.Summary(n, engine, Designer ? preview.Position(n.Id) : data?.Position(n) ?? 0);
            }
        foreach (var p in ports)
            p.Value.On = p.Key.Output ? engine.DisplayValue(p.Key.Node) : editor.Flow.Wires.Any(w => w.To == p.Key.Node && w.Port == p.Key.Index && engine.DisplayValue(w.From));
        foreach (var w in wires) if (graph.Wire(w.Value) is { } wire) wire.On = engine.DisplayValue(w.Key.From);
        live = editor.Node is { } selected && nodes.TryGetValue(selected.Id, out var view) ? view.FooterText + " | Signal " + (engine.Value(selected.Id) ? "on" : "off") : "";
        bool paused = engine.Status.Values.Any(s => s == "loop paused");
        bool fresh = notice.Length > 0 && Time.unscaledTime < noticeUntil;
        SetStatus((fresh ? notice + " | " : "") + editor.Flow.Nodes.Count + " blocks - " + editor.Flow.Wires.Count + " wires - " + editor.Sheets.Count + (editor.Sheets.Count == 1 ? " tab" : " tabs") + " | Saved with the vehicle" +
            (engine.Loop ? " | LOOP: blocks in a loop use last tick's value" : "") + (paused ? " | Loop protection: output paused for 2 seconds" : ""), engine.Loop || paused || fresh && notice.StartsWith("Wire refused", StringComparison.Ordinal));
    }
    private static void SetStatus(string text, bool warning)
    { if (status == text) return; status = text; panel?.SetStatus(text, warning ? StatusKind.Warning : StatusKind.Info); }
    internal static void Destroy()
    {
        if (panel != null) { try { UiKit.DestroyPanel(panel); } catch { } panel = null; }
        graph = null; shownPending = null; editor = null; data = null;
        nodes.Clear(); ports.Clear(); wires.Clear(); inputs = Array.Empty<IInputType>(); parts = Array.Empty<FlowPart>(); inspector = Array.Empty<FlowNode>();
        ClearHistories(); views.Clear(); drawnAt.Clear(); drawnSheet = null; tabLabels = Array.Empty<string>();
        registryRevision = -1; drawnRev = -1; status = live = notice = "";
    }
}
