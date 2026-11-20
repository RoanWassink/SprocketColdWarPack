# Sprocket UI Kit 0.1.9 - definitive graph API

All names are in `SprocketUiKit`; `Vector2` and `Color` are UnityEngine types.
All mutations run on the Unity main thread. `NodeGraph` inherits `Element` (including `.Fill()`,
`.Height()`, tooltips and visibility). Handles retain their state across UI rebuilds.

```csharp
// Container factory
public NodeGraph Container.NodeGraph();

// NodeGraph : Element (sealed; factory-created)
public Vector2 Pan { get; }
public float Zoom { get; }
public NodeGraph SetView(Vector2 pan, float zoom);
public NodeGraph FitAll(float margin = 40);
public NodeGraph ZoomRange(float min = .5f, float max = 1.5f);
public NodeGraph Snap(float grid);
public NodeGraph Hint(Func<string?> text);
public GraphNode AddNode(string id, float x, float y, string title, string tag, Color tagColour, float width = 176);
public GraphNode? Node(string id);
public bool RemoveNode(string id);
public void Clear();
public GraphWire AddWire(string id, string fromNode, string fromPort, string toNode, string toPort);
public GraphWire? Wire(string id);
public bool RemoveWire(string id);
public void BeginPendingWire(string node, string port);
public void EndPendingWire();
public NodeGraph OnClick(Action<GraphHit> handler);
public NodeGraph OnNodeMoved(Action<string, Vector2, bool> handler);
public NodeGraph OnViewChanged(Action<Vector2, float> handler);

// GraphNode (sealed; returned by AddNode)
public string Id { get; }
public string Title { get; set; }
public string Tag { get; set; }
public Color TagColour { get; set; }
public Vector2 Position { get; set; }
public bool Selected { get; set; }
public bool Dimmed { get; set; }
public bool Lamp { get; set; }
public string FooterText { get; set; }
public string? FooterButton { get; set; }
public GraphPort AddInput(string id, string label);
public GraphPort AddOutput(string id, string label);

// GraphPort (sealed; retain the returned handle in the consumer model)
public string Id { get; }
public bool IsOutput { get; }
public bool On { get; set; }
public bool Chosen { get; set; }
public string Label { get; set; }

// GraphWire (sealed)
public string Id { get; }
public bool On { get; set; }
public bool Selected { get; set; }
public Color? Colour { get; set; }
public float Width { get; set; }

public enum GraphHitKind { Canvas, Node, Port, Wire, FooterButton }
public readonly struct GraphHit {
    public GraphHitKind Kind { get; init; }
    public string? NodeId { get; init; }
    public string? PortId { get; init; }
    public string? WireId { get; init; }
    public Vector2 GraphPos { get; init; }
    public int Button { get; init; }
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
}
public static class GraphColours {
    public static Color In { get; }
    public static Color Logic { get; }
    public static Color Out { get; }
    public static Color WireOn { get; }
    public static Color WireOff { get; }
}
```

`SetView` clamps zoom. Invalid/nonfinite coordinates, widths, zoom ranges and negative snap/margins
throw. Node width is immutable and at least 100. `Snap(0)` disables snapping and applies to dragging
only; programmatic `Position` and `AddNode` preserve exact coordinates. `FitAll` applies after layout,
centres all nodes and respects the zoom range. Empty graphs keep the current view.

Node/wire IDs are unique per graph. Port IDs are unique across both directions of one node.
Unknown lookup returns null; removing a missing ID returns false. Wire endpoints must exist and
run from output to input. Self-links, cycles, parallel wires and multiple drivers are allowed by the
Kit; the consumer enforces its own flow rules. Removing a node removes its incident wires and pending
wire. `Clear` returns node views to the pool. Removed handles are detached from the visible model;
create fresh handles when re-adding IDs.

Events replace the previous handler. `Button`: 0 left, 1 right, 2 middle. GraphPos is graph space,
y down, with origin at graph (0,0); unrelated IDs are null. Click priority is FooterButton, Port, Node,
Wire, Canvas; overlapping targets use visual z-order. Motion exceeding 4 physical screen pixels
suppresses the click. Drag callbacks use the node ID and absolute position, `final=false` only on
position changes, `final=true` on a completed node drag. Middle drag pans anywhere in the graph;
left drag pans empty canvas. All callbacks/getters are exception-guarded. Hints should return a
cached string. Wire Selected changes colour only; Width changes geometry. Colour null restores the
On/Off palette; selection takes precedence. Live On/Chosen/Lamp/Dimmed/Selected changes do not lay out
nodes or tessellate wires. Pending wires follow the pointer and are excluded from hit-testing.
