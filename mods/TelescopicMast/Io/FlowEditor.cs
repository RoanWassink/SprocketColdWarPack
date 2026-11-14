using System.Globalization;
namespace SprocketTelescopicMast.Io;

public sealed record FlowPart(string Id, string Type, string Title, string Provider = "", string Selector = "");
public readonly record struct FlowPort(string Node, int Index, bool Output);
public enum SheetDelete { Deleted, NeedsConfirm, LastTab, Missing, ReadOnly }

/// <summary>Editor commands and selection, with no Kit, Unity or native callbacks.</summary>
public sealed class FlowEditor
{
    public const string ReasonLoop = "would create a loop", ReasonCompatible = "not compatible", ReasonTab = "blocks are on different tabs",
        ReasonReadOnly = "flow is read-only", ReasonConnected = "already connected";
    public FlowDocument Flow { get; }
    public FlowHistory History { get; }
    public string? SelectedNode { get; private set; }
    public FlowWire? SelectedWire { get; private set; }
    public FlowPort? Pending { get; private set; }
    public string? Hint => Pending is { } p ? p.Output
        ? "Now click an input port to connect. Click empty canvas to cancel."
        : "Now click an output port to connect. Click empty canvas to cancel." : null;
    public Action? Saved { get; set; }
    /// <summary>Short reason of the last refused wire (or a replacement note) for the panel status line.</summary>
    public string? Notice { get; private set; }
    /// <summary>Raised once per refused wire with the reason, for the DetailedLogging trace.</summary>
    public Action<string>? Refused { get; set; }
    /// <summary>Visible graph rectangle for free placement; null means a default view at the origin.</summary>
    public FlowRect? View { get; set; }
    public int StructureRevision { get; private set; }
    private string active;
    private IReadOnlyList<FlowSheet>? sheets;
    private long sheetsRev = -1;
    public FlowEditor(FlowDocument flow, FlowHistory? history = null)
    {
        Flow = flow; History = history ?? new FlowHistory();
        // Open on the first tab that has blocks, so an existing flow is never hidden behind an empty tab.
        active = Sheets.FirstOrDefault(s => Flow.Nodes.Any(n => FlowSheets.Of(n, Sheets) == s.Id))?.Id ?? Sheets[0].Id;
    }
    public FlowNode? Node => Flow.Nodes.FirstOrDefault(n => n.Id == SelectedNode);
    public void Cancel() => Pending = null;
    public void Canvas() { Cancel(); SelectedNode = null; SelectedWire = null; }
    public void SelectNode(string id) { Cancel(); SelectedWire = null; SelectedNode = Flow.Nodes.Any(n => n.Id == id) ? id : null; }
    public void SelectWire(FlowWire wire) { Cancel(); SelectedNode = null; SelectedWire = Flow.Wires.Contains(wire) ? wire : null; }
    private void Save(bool structure = false) { if (structure) StructureRevision++; Saved?.Invoke(); }
    public static bool HasOutput(FlowNode n) => FlowValidation.Ports(n) >= 0 && !FlowValidation.IsOutput(n);
    private bool Valid(FlowPort p)
    { var n = Flow.Nodes.FirstOrDefault(n => n.Id == p.Node); return n != null && (p.Output ? p.Index == 0 && HasOutput(n) : p.Index >= 0 && p.Index < FlowValidation.Ports(n)); }

    // ---------- Undo: every edit below runs through Commit, so it is exactly one step ----------
    /// <summary>Runs one edit. A changed revision records the previous whole-flow state and saves once.</summary>
    private bool Commit(Func<bool> edit, bool structure = true)
    {
        if (Flow.ReadOnly) return false;
        string before = FlowJson.Write(Flow); long rev = Flow.Rev;
        bool result = edit();
        if (Flow.Rev != rev) { History.Record(before); sheets = null; Save(structure); }
        return result;
    }
    public bool CanUndo => !Flow.ReadOnly && History.UndoCount > 0;
    public bool CanRedo => !Flow.ReadOnly && History.RedoCount > 0;
    public bool Undo() => CanUndo && Restore(History.Undo(FlowJson.Write(Flow))!);
    public bool Redo() => CanRedo && Restore(History.Redo(FlowJson.Write(Flow))!);
    /// <summary>Puts a snapshot back into the same document object (storage keeps its reference) with a new revision.</summary>
    private bool Restore(string json)
    {
        var state = FlowJson.Parse(json); var oldSheets = Sheets.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        string? keep = SelectedNode;
        Flow.Edited();
        Flow.Nodes.Clear(); Flow.Nodes.AddRange(state.Nodes);
        Flow.Wires.Clear(); Flow.Wires.AddRange(state.Wires);
        Flow.Extra.Clear(); foreach (var p in state.Extra) Flow.Extra[p.Key] = p.Value;
        sheets = null; Canvas();
        // A tab that came back (undo of a delete) is shown; a tab that vanished falls back to the first.
        if (Sheets.FirstOrDefault(s => !oldSheets.Contains(s.Id)) is { } returned) active = returned.Id;
        if (keep != null && Flow.Nodes.Any(n => n.Id == keep)) SelectedNode = keep;
        Notice = null; Save(true); return true;
    }

    // ---------- Tabs ----------
    public IReadOnlyList<FlowSheet> Sheets
    { get { if (sheets == null || sheetsRev != Flow.Rev) { sheets = FlowSheets.Read(Flow); sheetsRev = Flow.Rev; } return sheets; } }
    public string ActiveSheet { get { var list = Sheets; foreach (var s in list) if (s.Id == active) return active; return list[0].Id; } }
    public string SheetOf(FlowNode n) => FlowSheets.Of(n, Sheets);
    public IEnumerable<FlowNode> SheetNodes(string? sheet = null) { string id = sheet ?? ActiveSheet; var list = Sheets; return Flow.Nodes.Where(n => FlowSheets.Of(n, list) == id); }
    public bool SelectSheet(string id)
    {
        if (!Sheets.Any(s => s.Id == id) || id == ActiveSheet) return false;
        active = id; Canvas(); return true;
    }
    private static string NewSheetId() => "s" + Guid.NewGuid().ToString("N").Substring(0, 12);
    private string UniqueName(string name)
    {
        string baseName = FlowSheets.Clean(name); if (baseName.Length == 0) baseName = "Tab";
        if (!Sheets.Any(s => s.Name == baseName)) return baseName;
        for (int i = 2; ; i++) { string candidate = FlowSheets.Clean(baseName.Substring(0, Math.Min(baseName.Length, FlowSheets.MaxName - 4)) + " " + i); if (!Sheets.Any(s => s.Name == candidate)) return candidate; }
    }
    /// <summary>One undo step: a new empty tab, which becomes the visible tab.</summary>
    public string? AddSheet(string? name = null)
    {
        if (Flow.ReadOnly) return null;
        string id = NewSheetId(); string title = UniqueName(string.IsNullOrWhiteSpace(name) ? "Tab " + (Sheets.Count + 1) : name);
        Commit(() => { Flow.Edited(); FlowSheets.Write(Flow, Sheets.Append(new FlowSheet(id, title)).ToArray()); return true; });
        active = id; Canvas(); return id;
    }
    public bool RenameSheet(string id, string name)
    {
        string clean = FlowSheets.Clean(name);
        var sheet = Sheets.FirstOrDefault(s => s.Id == id);
        if (Flow.ReadOnly || sheet == null || clean.Length == 0 || clean == sheet.Name) return false;
        return Commit(() => { Flow.Edited(); FlowSheets.Write(Flow, Sheets.Select(s => s.Id == id ? s with { Name = clean } : s).ToArray()); return true; });
    }
    /// <summary>An empty tab goes at once; a tab with blocks only with <paramref name="confirmed"/>. The last tab stays.</summary>
    public SheetDelete DeleteSheet(string id, bool confirmed = false)
    {
        if (Flow.ReadOnly) return SheetDelete.ReadOnly;
        if (!Sheets.Any(s => s.Id == id)) return SheetDelete.Missing;
        if (Sheets.Count <= 1) return SheetDelete.LastTab;
        var doomed = SheetNodes(id).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        if (doomed.Count > 0 && !confirmed) return SheetDelete.NeedsConfirm;
        var rest = Sheets.Where(s => s.Id != id).ToArray();
        Commit(() =>
        {
            Flow.Edited();
            Flow.Nodes.RemoveAll(n => doomed.Contains(n.Id));
            Flow.Wires.RemoveAll(w => doomed.Contains(w.From) || doomed.Contains(w.To));
            FlowSheets.Write(Flow, rest); return true;
        });
        if (active == id) active = rest[0].Id;
        Canvas(); return SheetDelete.Deleted;
    }

    // ---------- Wires ----------
    /// <summary>Null when <paramref name="from"/> (an output) may drive <paramref name="to"/>, otherwise the reason.</summary>
    public string? Refusal(FlowPort from, FlowPort to)
    {
        if (Flow.ReadOnly) return ReasonReadOnly;
        if (!from.Output || to.Output || !Valid(from) || !Valid(to)) return ReasonCompatible;
        if (from.Node == to.Node) return ReasonLoop;
        var a = Flow.Nodes.First(n => n.Id == from.Node); var b = Flow.Nodes.First(n => n.Id == to.Node);
        if (SheetOf(a) != SheetOf(b)) return ReasonTab;
        return FlowValidation.ValidateWire(Flow, new FlowWire(from.Node, to.Node, to.Index)) == null ? null : ReasonCompatible;
    }
    private void Refuse(string reason, string? text = null) { Notice = text ?? "Wire refused: " + reason + "."; Refused?.Invoke(reason); }
    public bool ClickPort(FlowPort port)
    {
        Notice = null;
        if (Flow.ReadOnly) { if (Pending != null) Refuse(ReasonReadOnly); return false; }
        if (!Valid(port)) { if (Pending != null) Refuse(ReasonCompatible); return false; }
        if (Pending == port) { Cancel(); return false; }
        if (Pending is not { } start) { Pending = port; return false; }
        if (start.Output == port.Output)
        {
            // Same direction: the wire now starts here, which is the usual node-editor correction.
            Refuse(ReasonCompatible, "Wire refused: not compatible (two " + (port.Output ? "outputs" : "inputs") + "). The wire now starts here.");
            Pending = port; return false;
        }
        var from = start.Output ? start : port; var to = start.Output ? port : start;
        if (Refusal(from, to) is { } reason) { Refuse(reason); return false; }
        return Connect(from, to);
    }
    /// <summary>
    /// Completes a wire dragged from <paramref name="from"/> onto <paramref name="to"/> with exactly the
    /// two-click rules: one revision and one save, replacement of an occupied input, and the same notices.
    /// </summary>
    public bool Drop(FlowPort from, FlowPort to)
    {
        Notice = null;
        if (Flow.ReadOnly) { Refuse(ReasonReadOnly); return false; }
        if (!Valid(from) || !Valid(to) || from.Output == to.Output) { Refuse(ReasonCompatible); return false; }
        var output = from.Output ? from : to; var input = from.Output ? to : from;
        if (output.Node == input.Node) { Refuse(ReasonLoop); return false; }
        if (Refusal(output, input) is { } reason) { Refuse(reason); return false; }
        return Connect(output, input);
    }
    private bool Connect(FlowPort from, FlowPort to)
    {
        if (Flow.Wires.FirstOrDefault(w => w.From == from.Node && w.To == to.Node && w.Port == to.Index) is { } same)
        { Refuse(ReasonConnected, "Already connected."); SelectWire(same); return false; }
        var target = Flow.Nodes.First(n => n.Id == to.Node);
        bool replaced = Flow.Wires.Any(w => w.To == to.Node && w.Port == to.Index);
        // FlowDocument.Connect replaces the existing driver of this input in the same revision.
        Commit(() => { Flow.Connect(from.Node, to.Node, to.Index); SelectWire(Flow.Wires.Last()); return true; });
        if (replaced) Notice = "Replaced the old wire into " + FlowPresentation.Title(target) + " (" + FlowPresentation.Port(target, to.Index) + ").";
        return true;
    }
    /// <summary>The nearest port on the visible tab that would complete the pending wire, within <paramref name="radius"/> graph units.</summary>
    public FlowPort? Snap(double x, double y, double radius)
        => Pending is { } start ? SnapFrom(start, x, y, radius, true) : null;
    /// <summary>
    /// Nearest port of the opposite direction on the visible tab within <paramref name="radius"/> graph
    /// units. <paramref name="fittingOnly"/> keeps only wires that would be accepted; a drag uses false so a
    /// nearby port can still report its refusal.
    /// </summary>
    public FlowPort? SnapFrom(FlowPort start, double x, double y, double radius, bool fittingOnly = false)
    {
        if (Flow.ReadOnly || !double.IsFinite(x) || !double.IsFinite(y) || !(radius > 0)) return null;
        return FlowLayout.Nearest(SheetNodes(), x, y, radius, p => p.Output != start.Output && p.Node != start.Node &&
            (!fittingOnly || Refusal(start.Output ? start : p, start.Output ? p : start) == null));
    }

    // ---------- Blocks ----------
    private FlowRect DefaultView(double x = 32, double y = 32) => View ?? new FlowRect(x - 32, y - 32, FlowLayout.ViewWidth, FlowLayout.ViewHeight);
    private List<FlowRect> Occupied(string sheet) => SheetNodes(sheet).Select(FlowLayout.Bounds).ToList();
    public FlowNode? Place(string type, double x, double y, FlowPart? part = null)
    {
        if (Flow.ReadOnly) return null;
        if (part != null && Flow.Nodes.FirstOrDefault(n => n.PartId == part.Id) is { } placed) { SelectSheet(SheetOf(placed)); SelectNode(placed.Id); return placed; }
        if (part == null && !FlowPresentation.Logic.Contains(type) && IoRegistry.FindInput(type) == null) return null;
        var n = Create(type, x, y, part);
        Commit(() => { Flow.AddNode(n); SelectNode(n.Id); return true; });
        return n;
    }
    /// <summary>Place in free space on the visible tab: the block plus 24 px is clear of every other block.</summary>
    public FlowNode? PlaceFree(string type, FlowPart? part = null)
    {
        if (Flow.ReadOnly) return null;
        if (part != null && Flow.Nodes.FirstOrDefault(n => n.PartId == part.Id) is { } placed) return Place(type, 0, 0, part);
        var probe = Create(type, 0, 0, part);
        var spot = FlowLayout.Free(Occupied(ActiveSheet), FlowLayout.Width, FlowLayout.Height(probe), DefaultView());
        return Place(type, spot.X, spot.Y, part);
    }
    private FlowNode Create(string type, double x, double y, FlowPart? part)
    {
        var n = new FlowNode(Guid.NewGuid().ToString("N"), type, x, y, part?.Title ?? FlowPresentation.Name(type));
        var schema = part == null ? IoRegistry.FindInput(type)?.Schema : IoRegistry.FindOutput(type)?.Schema;
        if (schema != null) foreach (var p in schema.Params) n.Set(p.Id, p.Default);
        if (type == "delay") n.Set("t", "0.5");
        if (type == "pulse") n.Set("t", "2");
        if (type == "counter") n.Set("n", "3");
        if (part != null) { n.Set("partId", part.Id); n.Set("mode", "Follow"); }
        FlowSheets.Assign(n, ActiveSheet);
        return n;
    }
    /// <summary>
    /// One edit/save/undo step for the complete connected template. A new output opens a new tab
    /// named after the template at (x, y); an output that is already placed keeps its tab and the
    /// chain goes into free space there. A missing target is a no-op.
    /// </summary>
    public bool Template(string id, IReadOnlyList<FlowPart> parts, double x = 32, double y = 32)
    {
        var part = FlowTemplates.Target(id, Flow, Node, parts);
        if (part == null || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        var output = Flow.Nodes.FirstOrDefault(n => n.PartId == part.Id);
        var chain = new List<FlowNode>();
        FlowNode Add(string type)
        {
            var n = new FlowNode(Guid.NewGuid().ToString("N"), type, 0, 0, FlowPresentation.Name(type));
            var schema = IoRegistry.FindInput(type)?.Schema;
            if (schema != null) foreach (var p in schema.Params) n.Set(p.Id, p.Default);
            chain.Add(n); return n;
        }
        bool rotator = id is FlowTemplates.Spin or FlowTemplates.Home;
        int port = id == FlowTemplates.Home ? 3 : 0;
        var input = Add(rotator ? "key" : id == FlowTemplates.Reload ? "cannon.reloading" : id is FlowTemplates.Fired or FlowTemplates.MastFired ? "cannon.fired" : "vehicle.braking");
        if (IoRegistry.FindInput(input.Type) == null) return false;
        if (rotator) input.Set("signal", "held");
        // NotReady holds through any unloaded gap until the cannon is actually ready, excluding NeverLoaded at spawn.
        if (id == FlowTemplates.Reload) input.Set("mode", "notReady");
        if (id == FlowTemplates.Fired) { Add("delay").Set("t", "0.5"); Add("pulse").Set("t", "2"); }
        if (id == FlowTemplates.MastFired) Add("memory");
        bool newSheet = output == null;
        string sheet = newSheet ? NewSheetId() : SheetOf(output!);
        if (output == null)
        {
            output = new FlowNode(Guid.NewGuid().ToString("N"), part.Type, 0, 0, part.Title);
            var schema = IoRegistry.FindOutput(part.Type)?.Schema;
            if (schema != null) foreach (var p in schema.Params) output.Set(p.Id, p.Default);
            output.Set("partId", part.Id);
        }
        var group = newSheet ? chain.Append(output).ToList() : chain;
        double gx = x, gy = y;
        if (!newSheet)
            (gx, gy) = FlowLayout.Free(Occupied(sheet), (group.Count - 1) * FlowLayout.Step + FlowLayout.Width, group.Max(FlowLayout.Height), DefaultView(x, y));
        for (int i = 0; i < group.Count; i++) { group[i].Data["x"] = gx + i * FlowLayout.Step; group[i].Data["y"] = gy; FlowSheets.Assign(group[i], sheet); }
        string title = FlowTemplates.All.First(t => t.Id == id).Title;
        var finalOutput = output;
        Commit(() =>
        {
            Flow.Edited();
            if (newSheet) FlowSheets.Write(Flow, Sheets.Append(new FlowSheet(sheet, UniqueName(title))).ToArray());
            Flow.Nodes.AddRange(chain);
            if (!Flow.Nodes.Contains(finalOutput)) Flow.Nodes.Add(finalOutput);
            finalOutput.Set("mode", "Follow");
            Flow.Wires.RemoveAll(w => w.To == finalOutput.Id && w.Port == port);
            var wired = chain.Append(finalOutput).ToList();
            for (int i = 1; i < wired.Count; i++) Flow.Wires.Add(new(wired[i - 1].Id, wired[i].Id, i == wired.Count - 1 ? port : 0));
            return true;
        });
        active = sheet; SelectNode(finalOutput.Id); return true;
    }
    public void Move(string id, double x, double y, bool final)
    {
        if (!final || Flow.ReadOnly) return;
        var n = Flow.Nodes.FirstOrDefault(n => n.Id == id); if (n == null) return;
        if (FlowPresentation.Coordinate(n, "x") == x && FlowPresentation.Coordinate(n, "y") == y) return;
        Commit(() => { Flow.MoveNode(id, x, y); return true; }, false);
    }
    public void Rename(string id, string title)
    {
        var n = Flow.Nodes.FirstOrDefault(n => n.Id == id); if (n == null || Flow.ReadOnly || FlowNode.Text(n.Data["title"]) == title) return;
        Commit(() => { Flow.Edited(); n.Data["title"] = title; return true; }, false);
    }
    public void Parameter(string id, string key, string value)
    {
        var n = Flow.Nodes.FirstOrDefault(n => n.Id == id); if (n == null || Flow.ReadOnly || n.Get(key) == value) return;
        Commit(() => { Flow.SetParam(id, key, value); return true; }, false);
    }
    public void Delete()
    {
        if (Flow.ReadOnly) return;
        if (Node is { } n) Commit(() => { Flow.RemoveNode(n.Id); Canvas(); return true; });
        else if (SelectedWire is { } wire && Flow.Wires.Contains(wire)) Commit(() => { Flow.Edited(); Flow.Wires.Remove(wire); Canvas(); return true; });
    }
}

public static class FlowPresentation
{
    public static readonly string[] Logic = { "always", "and", "or", "not", "delay", "pulse", "counter", "memory" };
    public static readonly string[] Modes = { "Follow", "Toggle", "Open", "Close" };
    public static string Name(string type) => type switch
    { "always" => "Always on", "and" => "AND", "or" => "OR", "not" => "NOT", "delay" => "Delay", "pulse" => "Pulse", "counter" => "Counter", "memory" => "Memory", _ => IoRegistry.FindInput(type)?.DisplayName ?? IoRegistry.FindOutput(type)?.DisplayName ?? type };
    public static string Title(FlowNode n) => FlowNode.Text(n.Data["title"]) is { Length: > 0 } title ? title : Name(n.Type);
    public static double Coordinate(FlowNode n, string key) => double.TryParse(FlowNode.Text(n.Data[key]), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Clamp(v, -1000000, 1000000) : 0;
    public static string Port(FlowNode n, int port) => n.Type switch
    { "rotator" => new[] { "run", "reverse", "fast", "home" }[port], "and" or "or" => port == 0 ? "A" : "B", "counter" => port == 0 ? "count" : "reset", "memory" => port == 0 ? "set" : "reset", "pulse" => "trigger", "mast" => "raise", _ => "signal" };
    public static IReadOnlyList<ParamDef> Parameters(FlowNode n) => n.Type switch
    {
        "key" => new[] { ParamDef.Int("action", "Action", 1, KeyInput.Actions, 1), ParamDef.Enum("signal", "Signal", new[] { "press", "held" }, "press") },
        "delay" => new[] { ParamDef.Float("t", "Delay", 0, 30, .5, .1) },
        "pulse" => new[] { ParamDef.Float("t", "Duration", .5, 30, 2, .5) },
        "counter" => new[] { ParamDef.Int("n", "Count to", 1, 99, 3) },
        _ => (FlowValidation.IsOutput(n) ? IoRegistry.FindOutput(n.Type)?.Schema : IoRegistry.FindInput(n.Type)?.Schema)?.Params ?? Array.Empty<ParamDef>()
    };
    public static string Summary(FlowNode n, FlowEngine e, double position = 0) => e.Status.TryGetValue(n.Id, out var status) ? status : n.Type switch
    {
        "always" => "ON",
        "delay" => "Delay " + n.Number("t", .5).ToString("0.0#", CultureInfo.InvariantCulture) + " s",
        "pulse" => "Duration " + n.Number("t", 2).ToString("0.0#", CultureInfo.InvariantCulture) + " s",
        "counter" => "Count " + e.Count(n.Id) + " / " + n.Number("n", 3).ToString(CultureInfo.InvariantCulture),
        "hinge" => n.Get("mode", "Follow") + " - " + position.ToString("0", CultureInfo.InvariantCulture) + " deg",
        "mast" => "Raised " + (position * 100).ToString("0", CultureInfo.InvariantCulture) + "%",
        "rotator" => e.RotatorFooters.GetValueOrDefault(n.Id, "Stopped \u00b7 " + position.ToString("0", CultureInfo.InvariantCulture) + "\u00b0"),
        "cannon.fired" => "Momentary (one tick) - " + CannonParams.Footer(n.Type, n.InputParams),
        "cannon.reloading" or "cannon.reloaded" or "cannon.loaded" => CannonParams.Footer(n.Type, n.InputParams),
        _ => FlowValidation.IsOutput(n) ? (e.Target(n.Id) ? "Lamp ON" : "Lamp off") : e.Value(n.Id) ? "on" : "off"
    };
    public static string Help(FlowNode n) => n.Type switch
    {
        "always" => "Always sends an on signal. Connect to run for continuous rotation or to a Follow hinge to keep it open.",
        "and" => "On while both inputs are on.", "or" => "On while either input is on.", "not" => "On while the input is off.",
        "delay" => "Passes the signal on after the delay.", "pulse" => "Each trigger gives one pulse of this length.",
        "counter" => "On once it has counted this many triggers. Reset sets it back to 0.",
        "memory" => "Set switches it on, reset switches it off. It holds in between.",
        "rotator" => "Run spins while on; reverse flips CW/CCW; fast selects the fast speed. Home takes priority and follows the shortest route to target. All off stops here. Acceleration and braking take 0.3 s.",
        _ => FlowValidation.IsOutput(n) ? "Follow: on while the signal is on. Toggle, Open and Close act once per rising signal." : "Use Fire or Hold in the designer to try the flow before driving."
    };

    public const string MomentaryWarning = "Momentary signal: use Toggle, Memory or Pulse";
    /// <summary>Advisory only: never changes wiring, modes or execution.</summary>
    public static bool MomentaryToLevel(FlowDocument flow, FlowWire wire)
    {
        if (FlowValidation.ValidateWire(flow, wire) != null) return false;
        var source = flow.Nodes.First(n => n.Id == wire.From);
        var target = flow.Nodes.First(n => n.Id == wire.To);
        bool momentary = IoRegistry.FindInput(source.Type)?.SignalKind == SignalKind.Event &&
            !(source.Type == "key" && source.Get("signal") == "held");
        return momentary && (target.Type is "rotator" or "mast" || target.Type == "hinge" && target.Get("mode", "Follow") == "Follow");
    }
}
