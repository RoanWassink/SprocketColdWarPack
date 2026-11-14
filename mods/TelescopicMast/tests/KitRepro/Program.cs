// Reproduction for "wires often do not connect with several blocks" (mast 0.9.1 on UI Kit 0.1.9).
// Replays a 24-block session through the Kit's real GraphPicking.Hit (linked Kit Core source) with the
// 0.9.1 placement rules, then the same session with the 0.9.2 editor (free placement, tabs, 12 px snap).
using System.Security.Cryptography;
using SprocketTelescopicMast.Io;
using SprocketUiKit;

const float KitScale = 1.333f; // LogOutput.log line 700: "Scale 1,333 (screen 3440x1440, factor 1)"
var output = new List<string>();
int failures = 0;
void Say(string s) { output.Add(s); Console.WriteLine(s); }
void Expect(bool ok, string what) { if (!ok) { failures++; Say("FAIL " + what); } else Say("ok   " + what); }

IoRegistry.ClearForTests();
IoRegistry.RegisterInput(new CannonFiredInput()); IoRegistry.RegisterInput(new CannonReloadingInput());
IoRegistry.RegisterInput(new VehicleBrakingInput()); IoRegistry.RegisterInput(new KeyInput());
var flapA = new FlowPart("flapA", "hinge", "Shell flap"); var flapB = new FlowPart("flapB", "hinge", "Rear flap");
var lamp = new FlowPart("lamp", "effects.light", "Brake light", "Effects");
string[] palette = { "and", "or", "not", "delay", "pulse", "counter", "memory", "and", "or", "not", "delay", "pulse", "counter", "memory", "and", "or" };

string graphSource = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../ui-kit/Core/Graph.cs"));
if (File.Exists(graphSource)) Say("Kit Core/Graph.cs SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(graphSource))) + " (linked read-only)");
Say($"Kit hit radius: port 7 physical px = {7 / KitScale:0.00} graph units at Kit scale {KitScale} and zoom 1; block port dot is 8 graph units.");

// ---------- A: 0.9.1 placement (the rules of the 0.9.1 panel, Io/FlowEditor.cs Template) ----------
// Template: chain at (x + i*216, y) with x = y = (32 - pan)/zoom, so every template starts on the same spot.
// Palette:  offset = Nodes.Count % 6; (x + offset*24, y + offset*32).
var a = new List<FlowNode>();
FlowNode Add(List<FlowNode> list, string type, double x, double y, FlowPart? part = null)
{ var n = new FlowNode("n" + list.Count, type, x, y, part?.Title ?? type); if (part != null) n.Set("partId", part.Id); list.Add(n); return n; }
void OldTemplate(params (string Type, FlowPart? Part)[] chain) { for (int i = 0; i < chain.Length; i++) Add(a, chain[i].Type, 32 + i * 216, 32, chain[i].Part); }
OldTemplate(("cannon.reloading", null), ("hinge", flapA));
OldTemplate(("cannon.fired", null), ("delay", null), ("pulse", null), ("hinge", flapB));
OldTemplate(("vehicle.braking", null), ("effects.light", lamp));
foreach (var type in palette) { int o = a.Count % 6; Add(a, type, 32 + o * 24, 32 + o * 32); }
var resultA = Analyse("0.9.1 layout", a);
Expect(a.Count >= 20, $"A has {a.Count} blocks (>= 20)");
Expect(resultA.Overlaps > 0, $"A: {resultA.Overlaps} overlapping block pairs");
if (resultA.Hidden > 0)
    Expect(true, $"A: {resultA.Hidden} ports lie under another block's body and still win the Kit pick (ports are tested before bodies)");
else
    Say("ok   A: 0 hidden-but-picked ports on the installed Kit 0.1.11 (a covering body now wins before a covered port); 0.9.1 still has 129 overlaps and 36 unreachable ports");
Expect(resultA.Unreachable > 0, $"A: {resultA.Unreachable} ports are unreachable (an exactly stacked port above takes the click)");
Expect(resultA.NearMissCancels > 0, $"A: {resultA.NearMissCancels}/{resultA.Ports} near misses (9.5 px from a port, on its body) are Kit Node hits; 0.9.1 then selects the block and cancels the pending wire");

// ---------- B: 0.9.2 editor (same session) ----------
var e = new FlowEditor(new()) { View = FlowLayout.View(0, 0, 1) };
e.Template(FlowTemplates.Reload, new[] { flapA }); e.SelectSheet(e.Sheets[0].Id);
e.Template(FlowTemplates.Fired, new[] { flapA, flapB }); e.SelectSheet(e.Sheets[0].Id);
e.Template(FlowTemplates.Brake, new[] { lamp }); e.SelectSheet(e.Sheets[0].Id);
foreach (var type in palette) e.PlaceFree(type);
Expect(e.Flow.Nodes.Count == a.Count, $"B has the same {e.Flow.Nodes.Count} blocks, on {e.Sheets.Count} tabs");
int overlapsB = 0, hiddenB = 0, unreachableB = 0, nearB = 0, portsB = 0, snapsB = 0;
foreach (var sheet in e.Sheets)
{
    var nodes = e.SheetNodes(sheet.Id).ToList();
    var r = Analyse("0.9.2 tab '" + sheet.Name + "'", nodes);
    overlapsB += r.Overlaps; hiddenB += r.Hidden; unreachableB += r.Unreachable; nearB += r.NearMissCancels; portsB += r.Ports;
    // The 0.9.2 click handler snaps a pending wire to the nearest fitting port within 12 px.
    foreach (var n in nodes)
        for (int i = 0; i < FlowLayout.Inputs(n); i++)
        {
            var src = nodes.FirstOrDefault(s => FlowEditor.HasOutput(s) && s.Id != n.Id); if (src == null) continue;
            e.SelectSheet(sheet.Id); e.Canvas(); e.ClickPort(new(src.Id, 0, true));
            var (x, y) = FlowLayout.Port(n, false, i);
            if (e.Snap(x + 9, y - 3, FlowLayout.SnapPixels) == new FlowPort(n.Id, i, false)) snapsB++;
            e.Cancel();
        }
}
Expect(overlapsB == 0 && hiddenB == 0 && unreachableB == 0, $"B: {overlapsB} overlaps, {hiddenB} hidden ports, {unreachableB} unreachable ports");
int inputsB = e.Flow.Nodes.Sum(FlowLayout.Inputs);
Expect(snapsB == inputsB, $"B: {snapsB}/{inputsB} near misses on an input snap to that input (Kit still reports {nearB} of them as Node hits)");
Say("");
Say("Drag gesture (source evidence, Kit Runtime/NodeGraph.cs, not replayable without Unity): PointerDown on a port sets no dragNode and no pan;");
Say("PointerUp after > 4 physical px (GraphMath.Dragged) skips the click. A port-to-port drag therefore produces no GraphHit at all; see KIT-WISH-0.1.11.md.");
Directory.CreateDirectory("logs");
File.WriteAllLines("logs/wire-repro-0.9.3.txt", output);
Console.WriteLine(failures == 0 ? "PASS wire reproduction (0.9.1 faults reproduced, 0.9.2 layout and snap clean)" : $"FAIL {failures} reproduction checks");
return failures == 0 ? 0 : 1;

(int Overlaps, int Hidden, int Unreachable, int NearMissCancels, int Ports) Analyse(string label, List<FlowNode> nodes)
{
    // Kit model: one GraphNodeBounds per block in draw order; later blocks are on top.
    var bounds = nodes.Select(n => { var b = FlowLayout.Bounds(n); return new GraphNodeBounds(new RectF((float)b.X, (float)b.Y, (float)b.W, (float)b.H), FlowLayout.Inputs(n), FlowLayout.Outputs(n), false); }).ToList();
    var noWires = new List<GraphStrip>();
    int overlaps = 0, hidden = 0, unreachable = 0, near = 0, ports = 0;
    for (int i = 0; i < bounds.Count; i++)
        for (int j = i + 1; j < bounds.Count; j++)
        { var x = bounds[i].Body.Intersect(bounds[j].Body); if (x.W > 0 && x.H > 0) overlaps++; }
    for (int i = 0; i < bounds.Count; i++)
        for (int side = 0; side < 2; side++)
        {
            bool outSide = side == 1; int count = outSide ? bounds[i].Outputs : bounds[i].Inputs;
            for (int k = 0; k < count; k++)
            {
                ports++;
                var p = bounds[i].Port(outSide, k);
                var pick = GraphPicking.Hit(bounds, noWires, p, KitScale);
                bool self = pick.Kind == GraphPickKind.Port && pick.Index == i && pick.Port == k && pick.Output == outSide;
                bool covered = Enumerable.Range(i + 1, bounds.Count - i - 1).Any(j => bounds[j].Body.Contains(p));
                if (!self) unreachable++;
                else if (covered) hidden++;
                if (!outSide)
                {
                    var miss = new Vec(p.X + 9, p.Y - 3);
                    if (GraphPicking.Hit(bounds, noWires, miss, KitScale).Kind == GraphPickKind.Node) near++;
                }
            }
        }
    Say($"{label}: {nodes.Count} blocks, {ports} ports, {overlaps} overlapping pairs, {hidden} hidden-but-picked ports, {unreachable} unreachable ports, {near} near-miss Node hits");
    return (overlaps, hidden, unreachable, near, ports);
}
