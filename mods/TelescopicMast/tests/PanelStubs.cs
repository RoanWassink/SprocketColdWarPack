// Test doubles for the public Kit API and the native context. The real panel source is linked
// into this test executable. These measure consumer calls; they do not emulate Kit rendering.
using SprocketTelescopicMast.Io;
namespace UnityEngine
{
    public class MonoBehaviour { public MonoBehaviour(IntPtr p) { } }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMax => x + width; public float yMax => y + height;
    }
    public struct Color { }
    public static class Time { public static float unscaledTime; }
}
namespace Sprocket.Vehicles { }
namespace Sprocket.Vehicles.Control { }
namespace SprocketKeybinds { public sealed class ModKeybind { } }
namespace SprocketTelescopicMast
{
    internal static class RotatorRuntime { internal static void Maintain() { } }
    internal static class Plugin
    {
        internal const string ModGuid = "test.mast"; internal static bool Detailed = true;
        internal static readonly List<string> Traces = new(); internal static void Trace(string message) => Traces.Add(message);
    }
    internal static class GameState { internal static bool Paused() => false; }
    internal static class HingeKeys { internal static bool Suspended; internal static void Poll() { } }
    internal sealed class FlowPanelData
    {
        internal object Token = new(); internal FlowDocument Flow = new(); internal FlowEngine Engine = new();
        internal List<FlowPart> Parts = new(); internal Func<FlowNode, double> Position = _ => 0;
          internal Func<FlowNode,string?> TwinHint = _ => null;
          internal Func<string,bool> Exists = _ => false;
        internal Func<FlowNode,SettingsBinding?> Settings = _ => null;
        internal Action Save = () => { };
    }
    internal static class HingeRuntime
    {
        internal static FlowPanelData? Current;
        internal static FlowPanelData? PanelFlow(bool designer, object? token) => Current == null ? null : new()
          { Token = Current.Token, Flow = Current.Flow, Engine = Current.Engine, Parts = Current.Parts.ToList(), Save = Current.Save, TwinHint = Current.TwinHint, Exists = Current.Exists, Settings = Current.Settings };
        internal static void PrepareExternal() { } internal static void PrepareFlows() { }
        internal static void TickAll() { } internal static void Maintain() { }
        internal static void Warn(string text, Exception ex) => throw new Exception(text, ex);
    }
}
namespace SprocketUiKit
{
    using UnityEngine;
    public enum UiContexts { Designer, Game }
    public enum Icon { Hinge }
    public enum TextStyle { Section, Faint, Heading, Small, Body, Value }
    public enum ButtonStyle { Danger }
    public enum StatusKind { Warning, Info }
    public enum GraphHitKind { Canvas, Node, Port, Wire, FooterButton }
    public readonly struct GraphHit
    {
        public GraphHitKind Kind { get; init; } public string? NodeId { get; init; }
        public string? PortId { get; init; } public string? WireId { get; init; } public int Button { get; init; }
        public Vector2 GraphPos { get; init; } public bool Shift { get; init; } public bool Ctrl { get; init; }
    }
    public readonly struct GraphPortDrop
    {
        public string FromNode { get; } public string FromPort { get; }
        public bool FromOutput { get; } public GraphHit Drop { get; }
        public GraphPortDrop(string fromNode, string fromPort, bool fromOutput, GraphHit drop)
        { FromNode = fromNode; FromPort = fromPort; FromOutput = fromOutput; Drop = drop; }
    }
    public static class UiKit
    {
        public static readonly Dictionary<string,Action<double>> Numbers = new();
        public static readonly Dictionary<string,Action<bool>> Toggles = new();
        public static readonly Dictionary<string,Action<int>> Choices = new();
        public static readonly Dictionary<string,Func<bool>> FieldEnabled = new();
        public static readonly List<string> Labels = new();
        public static readonly List<string> StepperFields = new(), SegmentedFields = new();
        public static UiContexts Context; public static bool TextInputActive;
        public static Panel Latest = null!;
        public static readonly List<Action> Getters = new();
        public static readonly Dictionary<string, Action> Buttons = new();
        public static readonly List<Action> Repeaters = new();
        public static readonly List<(string Text, Func<bool> Visible)> Warnings = new();
        public static readonly Dictionary<string, Func<bool>> ButtonEnabled = new();
        public static readonly Dictionary<string, Action<string>> TextFields = new();
        public static readonly List<Tabs> TabBars = new();
        public static readonly List<(string Title, string Body, Action Confirm)> Confirms = new();
        public static (string Id, string Title, string Subtitle) Created;
        // 0.1.12: shortcuts are dispatched only to the focus owner, with no top-panel fallback.
        public static Panel? Focus; public static int GameUndo; public static int ShortcutCalls;
        public static bool GameKeysBlocked => Focus != null;
        public static Panel CreatePanel(string guid, string id, string title, string sub, int w, int h)
        {
            Getters.Clear(); Buttons.Clear(); Repeaters.Clear(); Warnings.Clear(); StepperFields.Clear(); SegmentedFields.Clear();
            ButtonEnabled.Clear(); TextFields.Clear(); TabBars.Clear(); Confirms.Clear(); Focus = null;
            Created = (id, title, sub); return Latest = new();
        }
        public static void BindToggle(Panel p, SprocketKeybinds.ModKeybind k) { }
        public static void DestroyPanel(Panel p) { p.Close(); }
        public static void Poll()
        { foreach (var r in Repeaters.ToArray()) r(); foreach (var g in Getters.ToArray()) g(); }
        internal static void Getter<T>(Func<T> get) { get(); Getters.Add(() => get()); }
        /// <summary>Models the game's designer undo: it must never run while a Kit panel has focus.</summary>
        public static void GameKey(string chord)
        {
            if (GameKeysBlocked) throw new InvalidOperationException("game input was not blocked while the panel had focus");
            if (chord == "Ctrl+Z") GameUndo++;
        }
        /// <summary>One key press through the Kit: focus owner shortcuts first, otherwise the game.</summary>
        public static bool Key(string chord)
        {
            var owner = Focus;
            if (owner != null)
            {
                if (owner.Shortcuts.TryGetValue(chord, out var action)) { ShortcutCalls++; action(); return true; }
                return false;
            }
            GameKey(chord); return false;
        }
    }
    public sealed class Tabs : Element
    {
        private readonly Func<IReadOnlyList<string>> source; private readonly Func<int> selected; private readonly Action<int> select;
        internal Tabs(Func<IReadOnlyList<string>> source, Func<int> selected, Action<int> select)
        { this.source = source; this.selected = selected; this.select = select; UiKit.Getter(source); UiKit.Getter(selected); }
        public IReadOnlyList<string> Labels() => source();
        public int Selected() => selected();
        public void Select(int index) => select(index);
    }
    public class Element
    {
        internal string? WarningText, ButtonText, Field; internal Action<string>? TextSetter;
        public Element EnabledWhen(Func<bool> f) { UiKit.Getter(f); if (Field != null) UiKit.FieldEnabled[Field] = f; if (ButtonText != null) UiKit.ButtonEnabled[ButtonText] = f; return this; }
        public Element VisibleWhen(Func<bool> f) { UiKit.Getter(f); if (WarningText != null) UiKit.Warnings.Add((WarningText, f)); return this; }
        public Element Colour(string c) => this; public Element Wrap() => this; public Element Flex() => this;
        public Element Compact() => this; public Element Width(float w) => this;
        public Element MaxChars(int n) => this;
        public Element Tooltip(string text) { if (TextSetter != null) UiKit.TextFields[text] = TextSetter; return this; }
    }
    public class Container : Element
    {
        private string fieldLabel = "";
        public Container Fill() => this; public Container ScrollView() => new();
        public Container Row(float n) => new(); public Stack Stack() => new();
        public Container Labeled(string label) => new() { fieldLabel = label };
        public Element Label(string t, TextStyle s = default) { UiKit.Labels.Add(t); return new(); }
        public Element Label(Func<string> f, TextStyle s = default) { UiKit.Getter(f); return new(); }
        public Element Chip(string t, Color c) => new();
        public Element Chip(Func<string> f) { UiKit.Getter(f); return new(); }
        public Element Lamp(Func<bool> f) { UiKit.Getter(f); return new(); }
        public Element Warning(string t) => new() { WarningText = t };
        public Element Button(string t, Action a, ButtonStyle s = default) { UiKit.Buttons[t] = a; return new() { ButtonText = t }; }
        public Element Repeat<T>(Func<IReadOnlyList<T>> get, Action<T, Stack> build)
        {
            IReadOnlyList<T>? old = null;
            UiKit.Repeaters.Add(() => { var list = get(); if (ReferenceEquals(list, old)) return; old = list; foreach (var item in list) build(item, new()); });
            return new();
        }
        public Tabs Tabs(Func<IReadOnlyList<string>> labels, Func<int> selected, Action<int> select)
        { var tabs = new Tabs(labels, selected, select); UiKit.TabBars.Add(tabs); return tabs; }
        public Element TextField(Func<string> get, Action<string> set) { UiKit.Getter(get); return new() { TextSetter = set }; }
        public Element Segmented(string[] choices, Func<int> get, Action<int> set) { UiKit.SegmentedFields.Add(fieldLabel); UiKit.Getter(get); return new(); }
        public Element Toggle(Func<bool> get, Action<bool> set) { UiKit.Toggles[fieldLabel]=set; UiKit.Getter(get); return new() { Field=fieldLabel }; }
        public Element Stepper(Func<double> get, Action<double> set, double min, double max, double step, string unit) { UiKit.Numbers[fieldLabel]=set; UiKit.StepperFields.Add(fieldLabel); UiKit.Getter(get); return new() { Field=fieldLabel }; }
        public Element Dropdown(Func<IReadOnlyList<string>> choices, Func<int> get, Action<int> set) { UiKit.Choices[fieldLabel]=set; UiKit.Getter(choices); UiKit.Getter(get); return new() { Field=fieldLabel }; }
        public Collapsible Collapsible(string title) => new();
        public NodeGraph NodeGraph() => SprocketUiKit.NodeGraph.Latest = new();
    }
    public sealed class Stack : Container { }
    public sealed class Collapsible { public Container Body = new(); }
    public sealed class Panel : Container
    {
        public bool IsOpen; public Icon TitleIcon; public event Action? OnOpen, OnClose;
        public Container[] Columns(params int[] widths) => widths.Select(_ => new Container()).ToArray();
        public void SetContext(Func<string> f) => UiKit.Getter(f);
        public string Status = ""; public StatusKind StatusKind;
        public readonly Dictionary<string, Action> Shortcuts = new();
        public void SetStatus(string t, StatusKind s) { Status = t; StatusKind = s; }
        public void Confirm(string title, string body, string confirm, Action onConfirm, ButtonStyle style = ButtonStyle.Danger, string cancel = "Back", Action? onCancel = null)
            => UiKit.Confirms.Add((title, body, onConfirm));
        public void AddShortcut(string chord, Action action, string? hint = null) => Shortcuts[chord] = action;
        public void Open() { IsOpen = true; UiKit.Focus = this; OnOpen?.Invoke(); }
        public void Close() { IsOpen = false; if (ReferenceEquals(UiKit.Focus, this)) UiKit.Focus = null; OnClose?.Invoke(); }
    }
    public static class GraphColours { public static Color In, Out, Logic; }
    public sealed class NodeGraph : Element
    {
        public static NodeGraph Latest = null!;
        public readonly Dictionary<string, GraphNode> Nodes = new(); public readonly Dictionary<string, GraphWire> Wires = new();
        public int StructuralCalls, Begins, Ends, FlagWrites;
        public Vector2 Pan { get; private set; } public float Zoom { get; private set; } = 1;
        private float viewW = 760, viewH = 560;
        // 0.1.11 layout and visible rectangle are in graph units; SetSize models the laid-out canvas.
        public Vector2 ViewSize => new(viewW, viewH);
        public Rect VisibleRect => new(-Pan.x / Zoom, -Pan.y / Zoom, viewW / Zoom, viewH / Zoom);
        public void SetSize(float w, float h) { viewW = w; viewH = h; }
        public Action<GraphHit>? Click; public Action<string, Vector2, bool>? Move;
        public Action<GraphPortDrop>? PortDrop; public float PortRadius { get; private set; } = 7;
        public NodeGraph Fill() => this; public NodeGraph Snap(float f) => this; public NodeGraph ZoomRange(float a, float b) => this;
        public NodeGraph Hint(Func<string?> f) { UiKit.Getter(f); return this; }
        public NodeGraph OnClick(Action<GraphHit> a) { Click = a; return this; }
        public NodeGraph OnNodeMoved(Action<string, Vector2, bool> a) { Move = a; return this; }
        public NodeGraph PortHitRadius(float px) { PortRadius = px; return this; }
        public NodeGraph OnPortDrag(Action<GraphPortDrop> a) { PortDrop = a; return this; }
        public void Drop(GraphPortDrop drop) => PortDrop?.Invoke(drop);
        public int Fits;
        public void FitAll(float margin = 40) { StructuralCalls++; Fits++; }
        public void SetView(Vector2 p, float z) { StructuralCalls++; Pan = p; Zoom = Math.Clamp(z, .5f, 1.5f); }
        public void Clear() { StructuralCalls++; Nodes.Clear(); Wires.Clear(); }
        public GraphNode AddNode(string id, float x, float y, string title, string tag, Color colour, float width = 176)
        { StructuralCalls++; var n = new GraphNode(this, id) { Title = title }; n.SetInitial(new(x, y)); Nodes.Add(id, n); return n; }
        public void RemoveNode(string id) { StructuralCalls++; Nodes.Remove(id); }
        public GraphWire AddWire(string id, string from, string fp, string to, string tp)
        { StructuralCalls++; return Wires[id] = new(this); }
        public void RemoveWire(string id) { StructuralCalls++; Wires.Remove(id); }
        public GraphWire? Wire(string id) => Wires.GetValueOrDefault(id);
        public void BeginPendingWire(string n, string p) { Begins++; }
        public void EndPendingWire() { Ends++; }
    }
    public sealed class GraphNode
    {
        private readonly NodeGraph graph; private string title = "", footer = "";
        public readonly Dictionary<string, GraphPort> Ports = new(); public string Id { get; }
        public GraphNode(NodeGraph graph, string id) { this.graph = graph; Id = id; }
        public string Title { get => title; set { title = value; graph.StructuralCalls++; } }
        private bool dimmed;
        public bool Selected { set { graph.FlagWrites++; } } public bool Dimmed { get=>dimmed;set { dimmed=value;graph.FlagWrites++; } }
        public bool Lamp { set { graph.FlagWrites++; } }
        public string FooterText { get => footer; set { footer = value; graph.FlagWrites++; } }
        public string? FooterButton { get; set; }
        private Vector2 position; internal void SetInitial(Vector2 p) => position = p;
        public Vector2 Position { get => position; set { position = value; graph.StructuralCalls++; } }
        public GraphPort AddInput(string id, string label) { graph.StructuralCalls++; return Ports[id] = new(graph, id); }
        public GraphPort AddOutput(string id, string label) { graph.StructuralCalls++; return Ports[id] = new(graph, id); }
    }
    public sealed class GraphPort
    {
        private readonly NodeGraph graph; public string Id { get; }
        public GraphPort(NodeGraph graph, string id) { this.graph = graph; Id = id; }
        public bool Chosen { set { graph.FlagWrites++; } } public bool On { set { graph.FlagWrites++; } }
    }
    public sealed class GraphWire
    {
        private readonly NodeGraph graph; public GraphWire(NodeGraph graph) { this.graph = graph; }
        public bool Selected { set { graph.FlagWrites++; } } public bool On { set { graph.FlagWrites++; } }
    }
}
