# Sprocket UI Kit 0.1.12

Shared BepInEx 6 IL2CPP plugin (`sprocket.uikit`) that draws mod panels in one style and
handles their behaviour centrally: Input System clicks/typing, cursor, gameplay-input block in game,
designer click-through guard, Esc, dragging, remembered positions, scale, focus/z-order and guarded
callbacks. Mods only describe content; values update every frame through getters.

- Requires: Sprocket Keybinds API `>=0.1.3 <0.2.0` (tested against installed 0.1.9), Sprocket 0.2.56.2.
- Install: `BepInEx\plugins\SprocketUiKit\SprocketUiKit.dll` (with Sprocket closed).
- Build and check: `dotnet build SprocketUiKit.csproj -c Release` (add `-p:GameDir=<folder with BepInEx\core, interop and the Keybinds DLL>` for a copy of the game),
  `dotnet run --project tests\UiKit.Tests.csproj -c Release` (pure tests), `Verify.ps1` (static checks and public API against `baseline\`; read only),
  `dotnet build examples\Example.csproj -c Release` (the examples below).
- Demo: keybind **UI Kit / Demo**, factory default **unbound** since 0.1.2 (F6 clashed with QoL flashlight): bind it in Settings > keybinds > Mod keybinds.
- Config `BepInEx\config\sprocket.uikit.cfg`: `Display.Scale` (0.5–2, multiplies screen height/1080),
  `Display.FontSource` (Default/Scene), `Input.BlockGameplayInput`, `Input.BlockKeysWhilePanelFocused` (default true), `Input.SuspendGameKeysWhileTyping`,
  `Input.SkipBlockTargets`, `Diagnostics.DetailedLogging` (default false).
  Panel positions: `sprocket.uikit.panels.cfg` (delete a line to re-centre). Since 0.1.4 a line is
  `x,y` (original size), `x,y,w,h` (resized) or `x,y,w,h,max` (maximized; x,y,w,h = restore rectangle).

## Focus-only keyboard routing (0.1.12)

Keys go to the panel with focus: `AddShortcut`, Enter's `DefaultAction` and modal Enter
use only `UiKit.FocusedPanel`. With no focus the Kit does nothing with these keys and
only the game receives them; there is no fallback to the top panel. A popup still suppresses
these actions, text entry keeps its existing path, and Esc retains its existing top-panel order.
Enter consults the focused panel's modal, even when another panel is higher in z-order.

```csharp
public bool Panel.HasFocus { get; }
public static Panel? UiKit.FocusedPanel { get; }
```

`HasFocus` is true exactly when `UiKit.FocusedPanel` is that panel; the latter is null when
there is no owner. HUDs never acquire focus. The open/press/close and lifecycle rules below
remain unchanged. Demo > Window shows both properties and a Ctrl+Shift+D shortcut counter.
The shortcut loop retains its existing `ToList()` snapshot so callback additions/removals
cannot invalidate enumeration; the new recipient policy and property getters allocate nothing.

## Keyboard focus and game keys (0.1.11)

Normal panels take keyboard focus when opened and on any mouse-button press in the title,
content or border. Only one panel owns focus. Clicking outside all Kit panels releases it;
closing the owner passes it to the top remaining open normal panel. HUDs, markers and tooltips
never own focus. Native pause, scene changes, alt-tab, settings and plugin unload release both
panel and text focus. A focused title has a subtle amber accent; other titles are dimmed.

`Input.BlockKeysWhilePanelFocused=true` disables enabled game InputActions while a normal panel
owns focus, in the designer as well as gameplay. The native UI input module's maps and all
DontSave assets (including the Keybinds API) are excluded. Panel focus never selects the typing
proxy: mod toggle keys such as a consumer's F8 still work. Text fields retain their existing
proxy and `Input.SuspendGameKeysWhileTyping` behaviour. Turning off panel blocking releases its
claim immediately; typing blocking remains independently controlled by the typing option.

`Panel.AddShortcut` has its existing signature, with no `consume` parameter: while its panel is
focused, the designer's game actions are disabled too (for example Ctrl+Z). Esc and pointer
blocking use the existing paths. The guard records only actions it disabled and restores only
those actions. It restores on focus loss, config opt-out, lifecycle changes and exceptions,
before reporting the error; failed native restores stay tracked for retry. An orphaned set is
restored in the same Update. A resource scan runs on entering a blocking session, then at most
once per second to catch actions the game enabled again. A continuous panel/text handoff keeps
that throttle. DetailedLogging emits one count/reason line per disable or restore batch.

## Graph gestures, picking and live tabs (0.1.11)

```csharp
NodeGraph graph = column.NodeGraph().Fill().PortHitRadius(12); // default 7 physical px
int serial = 0;
graph.OnPortDrag(drop =>
{
    if (drop.Drop.Kind != GraphHitKind.Port) return;
    var target = graph.Node(drop.Drop.NodeId!)!;
    // This example connects drags from an output to an input named "in".
    // The consumer owns port types, connection validity and replacement rules.
    if (!drop.FromOutput || drop.Drop.PortId != "in") return;
    graph.AddWire("drag" + serial++, drop.FromNode, drop.FromPort, target.Id, "in");
});
Vector2 layoutSize = graph.ViewSize;
Rect visibleGraphSpace = graph.VisibleRect;
var names = new List<string> { "Main", "Auxiliary" };
int selected = 0;
column.Tabs(() => names, () => selected, i => selected = i);
column.TextField(() => names[selected], value => names[selected] = value);
```

A left press on a port followed by movement strictly greater than 4 physical px starts a dashed
wire from that port to the mouse. It cannot move blocks or pan/zoom the graph. Release invokes
`OnPortDrag(Action<GraphPortDrop>)` once, including drops on the canvas, a block, a wire or footer;
short press/release still uses `OnClick`. The drag wire is independent of the mod's
`BeginPendingWire`/`EndPendingWire`, and disappears on release, cancellation, hidden content or
source-node removal. The two temporary wires each reserve one of the 511 mesh slots, leaving
509 regular wires if both are active. Input-origin drags report `FromOutput=false`.

Picking walks blocks from top to bottom: within each block, footer button, ports, then body.
The first block that hits wins, including port circles extending outside that block. Higher
bodies therefore cover lower ports and footers. Wires follow all blocks, then the canvas.
`PortHitRadius(float px)` accepts finite nonnegative physical pixels, independent of Kit scale
and zoom. `ViewSize` is the laid-out graph extent in Kit units (zero before first layout);
`VisibleRect` is that extent transformed into graph space by current pan and zoom.

`Container.Tabs(Func<IReadOnlyList<string>> labels, Func<int> selected, Action<int> select)`
polls the list and detects in-place edits as well as replacement lists. Return a cached list:
equal labels allocate zero bytes, renaming changes text only, and changing the count rebuilds
the tab strip. The consumer keeps the selected index valid when removing tabs.

## Een paneel maken (make a panel)

Reference `SprocketUiKit.dll` and `SprocketKeybinds.dll` with `Private=false`; never ship copies.
This complete plugin compiles (`examples/ExamplePlugin.cs`, `examples/Example.csproj`):

```csharp
using BepInEx;
using BepInEx.Unity.IL2CPP;
using SprocketKeybinds;
using SprocketUiKit;

[BepInPlugin("example.hatches", "Example Hatches", "1.0.0")]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.3 <0.2.0")]
[BepInDependency(UiKit.PluginGuid, ">=0.1.0 <0.2.0")]
public sealed class ExamplePlugin : BasePlugin
{
    private sealed class Hatch { public string Name = ""; public bool Open; public double Speed = 90; }
    private readonly List<Hatch> hatches = new() { new() { Name = "Commander hatch" }, new() { Name = "Loader hatch" } };
    private Hatch? selected;

    public override void Load()
    {
        var key = Keybinds.RegisterButton("example.hatches", "Example Hatches", "panel", "Hatches / Panel", "");
        var panel = UiKit.CreatePanel("example.hatches", "main", "Hatches", "Example Hatches 1.0", 760, 460);
        UiKit.BindToggle(panel, key);                       // open/close with the player's keybind

        var cols = panel.Columns(320, 0);                   // fixed 320 px + flexible
        cols[0].Section("Hatches", () => hatches.Count)
            .List(() => hatches, (h, row) =>
            {
                row.Title = h.Name; row.Lamp = h.Open; row.Selected = h == selected;
                row.Tag = h.Open ? "OPEN" : "CLOSED";
            })
            .OnSelect(h => selected = h);

        var edit = cols[1].Section("Selected hatch");
        edit.Label(() => selected?.Name ?? "Click a hatch on the left.", TextStyle.Label);
        edit.FieldRow("Open").Toggle(() => selected?.Open ?? false, v => { if (selected != null) selected.Open = v; })
            .EnabledWhen(() => selected != null);
        edit.FieldRow("Speed").Stepper(() => selected?.Speed ?? 0, v => selected!.Speed = v, 10, 360, 10, "°/s")
            .EnabledWhen(() => selected != null).Tooltip("Degrees per second.");

        var add = cols[1].Footer("New hatch");
        add.Labeled("Name").TextField(() => "", name => newName = name, "Hatch name");
        add.Button("Add hatch", () =>
        {
            if (newName.Trim().Length == 0) { panel.SetStatus("Type a name first.", StatusKind.Warning); return; }
            hatches.Add(new Hatch { Name = newName.Trim() });
            panel.SetStatus("Added " + newName.Trim(), StatusKind.Success);
        }, ButtonStyle.Primary).WithIcon(Icon.Plus);
    }
    private string newName = "";
}
```

## API overview

| | |
|---|---|
| `UiKit.CreatePanel(modGuid, id, title, subtitle?, width=900, height=560)` | Panel (unique per modGuid+id; duplicate throws). Sizes in 1080p reference px. |
| `UiKit.BindToggle(panel, ModKeybind)` | Toggle on the Keybinds API action (ignored while typing / in the settings menu). |
| `UiKit.Context`, `ContextLabel`, `AnyPanelOpen`, `PointerOverPanel`, `TextInputActive`, `Scale` | State for mods. |
| `Panel.Open/Close/Toggle`, `IsOpen`, `OnOpen`, `OnClose` | Esc, close button, keybind, pause and scene/context change all close. |
| `Panel.SetStatus(text, StatusKind)`, `AddKeyHint(key, action)`, `SetContext(text or getter)`, `TitleIcon` | Footer status (info/success/warning/error), key hints, context chip. |
| `Panel.AllowedContexts`, `BlockGameplayInput`, `CloseOnSceneChange`, `SetSize` | Behaviour switches (defaults: designer+game, block, close). |
| `Panel.Columns(widths…)` / `Body` | Columns: `>0` px, `0` flexible, `<0` flexible weight. |
| `Column.Section(title, count/note?)` → Stack, `Column.Footer(title?)` → Block pinned to the bottom | |
| Container factories: `Stack`, `Row`, `Card`, `ScrollView`, `Collapsible`, `Repeat<T>`, `FieldRow(label)`, `Labeled(label)`, `Spacer` | Layout. |
| `Button(text, onClick, ButtonStyle)` `.Compact()` `.WithIcon()`, `IconButton(icon, onClick, tooltip)` `.Round()` | Primary / Normal / Quiet / Danger; disabled via `EnabledWhen`. |
| `Segmented(options, get, set)`, `Stepper(get, set, min, max, step, unit)` `.Compact()` `.Format()`, `Toggle(get, set)` | |
| `Dropdown(options or getter or Choice-getter, get, set)` `.Inline()` `.Accent()` | Overlay list above everything, flips up, scrolls, grouped headings via `Choice.Group`. |
| `TextField(get, commit, placeholder)` `.MaxChars()` `.Numeric()` `.OnChange()` | Enter/blur commits, Esc reverts, Ctrl+A. |
| `Tabs(tabs, get, set)`, `List<T>(items, template)` `.OnSelect` `.OnRemove` `.LampHold` `.Empty` `.MaxHeight` | `ListRow`: Title, Subtitle, Tag(+TagChip), Lamp, Value(+ValueAccent), Progress, Note, Selected, Removable, Key. |
| `Table<T>(rows)` `.Column(header, cell, ColumnWidth, CellAlign, headerTooltip?)` `.CellStyle(...)` `.RowTooltip` `.CellTooltip` `.Zebra` `.OnSelect` `.Selected` `.Empty` `.MaxHeight` | Aligned data table (0.1.6); columns stay aligned at every size and scale. |
| `LineChart(height=220)` `.Series` `.XAxis` `.YAxis` `.TickFormat` `.RefLine` `.Legend` `.Empty` | Line/staircase/point chart with auto or fixed axes, ticks, hover and legend (0.1.7). |
| `BarChart(height=220)` `.Bars` `.Waterfall` `.YAxis` `.TickFormat` `.RefLine` `.Empty` | Bar chart and floating waterfall (0.1.7). |
| `Radial(value, size=48, centre?)` `.Thickness(px)` `.Colour(...)` `.Track(...)` | Circular progress with a GPU-filled ring and optional centre text; 0..1 (0.1.8). |
| `Lamp(on, label?)` `.Hold(s)`, `Progress(value)`, `Chip(text, ChipStyle)` `.StyleWhen()`, `Warning(text)`, `Label(text or getter, TextStyle)` `.Wrap()` | |
| Every element: `.Width() .Height() .Flex() .Fill() .Tooltip() .VisibleWhen() .EnabledWhen()` | |
| `Guard.Run(where, action)`, `Guard.Get(where, getter, fallback)` | Every callback already runs inside Guard; also usable by mods. |

Notes: build the content once (in `Load`); use `VisibleWhen` for tabs/conditional parts and `Repeat<T>`
for rows whose controls depend on data. Callbacks run on the Unity main thread. Panels are built on
first open (the TMP font must exist by then).

## New in 0.1.8: RadialProgress

`Container.Radial(...)` draws a circular progress without a `CanvasView`: one procedural ring sprite
is shown twice, as a muted track and as a GPU-filled `Image` (`Radial360`), so a value change costs an
`Image.fillAmount` update and no CPU raster. The same element works in a panel, in `HudPanel.Body` and
in a `Row` next to a label. The default colour is Accent, the track is the muted `#3d3c35` token, and
the default ring thickness is about 12% of the diameter (minimum 3 px).

```csharp
public RadialProgress Radial(Func<float> value, float size = 48, Func<string?>? centre = null);

public RadialProgress Thickness(float px);       // reference px, scales with the Kit
public RadialProgress Colour(Color colour);
public RadialProgress Colour(Func<Color> colour);
public RadialProgress Track(Color colour);
```

The value is clamped to 0..1; NaN and infinite values count as 0. The ring fills clockwise from
12 o'clock. The optional centre text uses `TextStyle.Value` or smaller, is only set when it changes,
and the `fillAmount` is only set for a change of at least 1/720 (half a degree). Equal value and equal
text cost 0 bytes per poll. Every element method still applies (`.Width`, `.Height`, `.Tooltip`, ...).

Example for a reload HUD (the getter values come from your reload state; keep the colours and centre
string cached, as below):

```csharp
static readonly Color Accent = new Color32(0xdc, 0x9e, 0x33, 0xff);
static readonly Color Ready = new Color32(0x5f, 0xe0, 0x7a, 0xff);
float reload01 = 0; string secondsLeft = "0.0";   // e.g. 1 - remaining / reloadTime

var hud = UiKit.CreateHud("example.hatches", "reload", 220).Anchor(HudCorner.BottomRight, 24, 24);
hud.Body.Label("RELOAD", TextStyle.BlockTitle);
var row = hud.Body.Row(10);
row.Radial(() => reload01, 56, () => secondsLeft)
   .Thickness(7)
   .Colour(() => reload01 >= 1 ? Ready : Accent)
   .Tooltip("Reload progress; the ring fills clockwise.");
row.Label("Main gun", TextStyle.Value);
```

Consumers of this element need `[BepInDependency(UiKit.PluginGuid, ">=0.1.8 <0.2.0")]`.

## New in 0.1.7: LineChart and BarChart

Two new elements on the existing `CanvasView`/`Painter` (no second rasteriser), for the two ballistic
uses in #28: protection against an angle and the penetration budget along the shot line. The width
fills the column; the height is fixed. Data comes from getters that the Kit polls every frame. A
redraw happens only when the data list (reference, count or a cheap value hash), the size or the axis
range changes. Hover uses uGUI images over the image and the Kit tooltip, so hovering does not redraw.
Inside a `ScrollView` the wheel goes to the ScrollView (no pan or zoom).

```csharp
public LineChart LineChart(float height = 220);                 // on Container
public BarChart BarChart(float height = 220);

public LineChart Series(string name, Func<IReadOnlyList<Vector2>> points, Color? colour = null, SeriesStyle style = SeriesStyle.Line);
public LineChart XAxis(string label, string unit = "", float? min = null, float? max = null, float? tick = null);
public LineChart YAxis(string label, string unit = "", float? min = null, float? max = null, float? tick = null);
public LineChart TickFormat(Func<float, string> format);        // both axes
public LineChart TickFormat(Func<float, string> x, Func<float, string> y);
public LineChart RefLine(string label, Func<float> y, Color? colour = null);
public LineChart Legend(bool show = true);
public LineChart Empty(string text);

public BarChart Bars(Func<IReadOnlyList<ChartBar>> bars);
public BarChart Waterfall(Func<float> start, string startLabel = "Start", string endLabel = "Left");
public BarChart YAxis(string label, string unit = "", float? min = null, float? max = null, float? tick = null);
public BarChart TickFormat(Func<float, string> format);
public BarChart RefLine(string label, Func<float> y, Color? colour = null);
public BarChart Empty(string text);

public enum SeriesStyle { Line, Step, Points }
public readonly struct ChartBar
{
    public string Label { get; init; }
    public float Value { get; init; }
    public Color? Colour { get; init; }
    public string? Tooltip { get; init; }
    public ChartBar(string label, float value, Color? colour = null, string? tooltip = null);
}
```

- **Series**: one to four; `Line` connects the points, `Step` draws horizontal then vertical, `Points`
  draws only dots. Non-finite points (NaN, +/-infinity) are skipped and leave a gap in the line; one
  point is drawn as a dot. Without a colour the fixed palette order is Accent `#dc9e33`, Success
  `#8fd99c`, Error `#e07a4f`, Text2 `#b8ad94`.
- **Axes**: without `min`/`max` the range is automatic with nice ticks (1, 2 or 5 x 10^n); a fixed
  bound is used exactly, a `tick` overrides the step. An empty or flat range gets a small margin.
  Captions carry the unit: `XAxis("Angle", "deg")` shows "Angle (deg)".
- **Reference line**: a dashed horizontal line at the getter's current value, with its label at the
  right. The line is included in an automatic value range, so "RHA" stays visible.
- **Legend**: default on when there is more than one series, as small chips with the series colour.
- **Hover**: a vertical line at the nearest x, a dot on every series and a tooltip with the axis value
  and each series name, value and unit. Bars light up; their tooltip shows label, value, an optional
  own tooltip and, for a waterfall layer, the remaining protection.
- **Bars**: `ChartBar.Value`; the value axis includes 0. `YAxis`, `RefLine` and `TickFormat` work as
  on a line chart. `Empty` is shown when there are no bars.
- **Waterfall**: the bars are deltas (negative = consumed). The first bar runs from 0 to `start`;
  every layer is a floating bar from the old to the new remainder; the last bar runs from 0 to the
  end remainder. A layer that reaches 0 is clamped to 0, marked "Stopped" in the tooltip and ends
  the waterfall. The layer labels come from `Bars`; the start and end bars use `startLabel` and
  `endLabel`.

### Example 1: protection against angle (KE/HEAT and RHA)

```csharp
private readonly List<Vector2> ke = new(), heat = new();

// LOS = plate thickness / cos(angle); the two threats scale it differently.
private void RebuildAngle(float thickness)
{
    ke.Clear(); heat.Clear();
    for (int angle = 0; angle <= 80; angle++)
    {
        float los = thickness / MathF.Cos(angle * MathF.PI / 180f);
        ke.Add(new Vector2(angle, los * .85f));
        heat.Add(new Vector2(angle, los * .60f));
    }
}

var section = column.Section("Protection against angle");
section.LineChart(260)
    .Series("KE", () => ke)
    .Series("HEAT", () => heat, UiKit.Colour("#8fd99c"))
    .XAxis("Angle", "deg", 0, 80, 10)
    .YAxis("Protection", "mm RHA")
    .RefLine("RHA 200", () => 200f)
    .Legend()
    .Empty("No protection data.");
```

### Example 2: penetration budget along the shot line, as Step and as Waterfall

```csharp
private const float Start = 400f;
private readonly List<Vector2> remaining = new();
private List<ChartBar> layers = new();

private void RebuildLayers()
{
    float[] depths = { 25, 55, 90, 130 };          // mm along the shot line
    float[] deltas = { -60, -80, -90, -100 };      // negative = consumed
    remaining.Clear(); remaining.Add(new Vector2(0, Start));
    float left = Start;
    for (int i = 0; i < depths.Length; i++)
    {
        left = MathF.Max(0, left + deltas[i]);
        remaining.Add(new Vector2(depths[i], left));
    }
    layers = new List<ChartBar>
    {
        new("Front plate", deltas[0], tooltip: "25 mm steel."),
        new("Spall liner", deltas[1], tooltip: "30 mm composite."),
        new("Backing", deltas[2], tooltip: "35 mm steel."),
        new("Frame", deltas[3], tooltip: "40 mm structure."),
    };
}

section.LineChart(200)
    .Series("Remaining", () => remaining, UiKit.Colour("#8fd99c"), SeriesStyle.Step)
    .XAxis("Depth", "mm")
    .YAxis("Remaining", "mm", 0)
    .Legend(false)
    .Empty("No layers.");

section.BarChart(200)
    .Bars(() => layers)
    .Waterfall(() => Start, "Start", "Left")
    .YAxis("Remaining", "mm", 0)
    .Empty("No layers.");
```

With deltas `{ -120, -150, -180, -60 }` the third layer reaches 0: its bar is clamped, the tooltip
says "Stopped at 0." and the fourth layer is not drawn.

## New in 0.1.6: Table<T>

`Container.Table<T>(Func<IReadOnlyList<T>> rows)` returns a table in the `ListView<T>` style: the
getter is polled every frame, columns and rows are drawn in the kit look, and only visible rows are
active. The exact API:

```csharp
public Table<T> Table<T>(Func<IReadOnlyList<T>> rows);                       // on Container
public Table<T> Column(string header, Func<T, string> cell, ColumnWidth width, CellAlign align, string? headerTooltip = null);
public Table<T> CellStyle(Func<T, int, CellStyle> style);                   // (row item, column index)
public Table<T> RowTooltip(Func<T, string?> tooltip);
public Table<T> CellTooltip(Func<T, int, string?> tooltip);
public Table<T> Zebra(bool on = true);
public Table<T> OnSelect(Action<T> select);
public Table<T> Selected(Func<T, bool> isSelected);
public Table<T> Empty(string text);
public Table<T> MaxHeight(float height);                                    // and the usual .Fill()
public readonly struct ColumnWidth { Fixed(float px), Auto, Fill(float weight = 1) }
public enum CellAlign { Left, Centre, Right }
public readonly struct CellStyle { CellStyle(Color? colour = null, bool strong = false, bool faint = false) }
```

The complete protection table (also in the demo, tab "Table"):

```csharp
private sealed class ThreatRow
{
    public string Threat = "", Note = "";
    public int Rha;
    public double Thickness, Mass;
}

private readonly List<ThreatRow> threats = new()
{
    new() { Threat = "KE", Rha = 150, Thickness = 1.00, Mass = 1.00, Note = "Long rod and AP shot." },
    new() { Threat = "HEAT", Rha = 95, Thickness = .75, Mass = .85, Note = "Shaped charge jet." },
};

// inside a panel, after a Section:
var table = column.Section("Protection").Table(() => threats)
    .Column("Threat", r => r.Threat, ColumnWidth.Fill(1), CellAlign.Left, "The projectile class this row describes.")
    .Column("Protection (mm RHA)", r => r.Rha.ToString(), ColumnWidth.Fixed(160), CellAlign.Right, "Equivalent homogeneous armour.")
    .Column("Factor on plate thickness", r => r.Thickness.ToString("0.00"), ColumnWidth.Fixed(130), CellAlign.Right)
    .Column("Factor on mass", r => r.Mass.ToString("0.00"), ColumnWidth.Fill(1.2f), CellAlign.Right)
    .CellStyle((r, i) => i == 1 && r.Threat == "HEAT" ? new CellStyle(UiKit.Colour("#e07a4f"))
        : i == 1 && r.Threat == "KE" ? new CellStyle(strong: true) : default)
    .RowTooltip(r => r.Note)
    .Zebra()
    .Empty("No threats in this vehicle.");
```

- **Numbers**: right-align numeric columns yourself with `CellAlign.Right`; headers use the same
  alignment, so values sit directly under their header. `CellAlign.Centre` is available too.
- **Widths**: `Fixed(px)` is exact in reference px and scales with the Kit. `Auto` is the widest of the
  header and all cells plus the cell padding, remeasured only when a cell text changes (with no Fill
  column, the extra width is shared by the Auto columns). `Fill(weight)` takes the leftover; its
  minimum is the longest word of the header (at least 40 px).
- **Too narrow**: Fill columns shrink to their minimum first, then Auto columns shrink proportionally;
  Fixed never shrinks. Widths and x positions are whole pixels, header and body share them, and text
  that does not fit gets an ellipsis instead of overlapping the next column. Headers may wrap to two
  lines; the header line is then equally high for every column.
- **Height**: by default the table measures its full height and does not scroll, so a `ScrollView`
  around it owns the wheel. `.MaxHeight(px)` or `.Fill()` gives the table a fixed header and a
  scrolling body with the 0.1.5 bar (drag, track paging and wheel); only visible rows are built/hit.
- **Tooltips**: `headerTooltip` explains a column. A truncated cell shows its full text as its
  tooltip unless `CellTooltip` returns one; `RowTooltip` is the fallback for the rest of the row.
- **Style**: `CellStyle` can set a `Color`, `Strong` (bold label style) or `Faint`; `Zebra` alternates
  the row background and `OnSelect` gives the `ListView` hover look. `Selected` only reads your state.

## New in 0.1.5
No API change: `ScrollView`, `ListView<T>` and an open dropdown list get a real vertical scrollbar
automatically. Consumers keep their existing dependency and need no rebuild; the Cassette Composer
(`ScrollView().Fill()` in its right column) gets it as-is.
- Rest: 3 px thumb `#5c5442` (`Border`). Hover over the 10 px strip or drag: 6 px, `#b8ad94` (`Text2`);
  while dragging `#dc9e33` (`Accent`); a subtle 6 px track `#24231c` at .55 appears. Tokens in `docs/STYLE.md`.
- Drag the thumb: the content follows the mouse proportionally, the grab point stays under the cursor,
  and the capture continues outside the bar and panel (`OnPress` + `Kit.Pressed`, like `Slider`).
  Click the track above or below the thumb to page one viewport; hold to repeat after .45 s at .07 s
  (the `Stepper` rhythm) until the thumb is under the cursor.
- The wheel is unchanged, also over the bar; nested `ScrollView`s keep the innermost list under the
  cursor. The bar exists only while the content overflows and takes clicks only in the 10 px strip at
  the right edge. The reserved 6 px content width (and so the layout) is unchanged from 0.1.4.
- Resize grip versus bar: inside the panel the bar owns its strip; the 4 px grip band outside the right
  edge still resizes. A bar in the rightmost column sits inside the 14 px column padding, clear of the grip.
- `ListView`: a click on the bar selects no row and presses no delete button. In an open dropdown,
  dragging or paging keeps the list open and chooses no option; an option click works as before.
- No horizontal scrollbar exists in the kit, and none was added.

## New in 0.1.4
Declare `[BepInDependency(UiKit.PluginGuid, ">=0.1.4 <0.2.0")]` to call these. Maximize and resize
are on by default for every panel (not for HUDs, markers or modals); older mods get them without a rebuild.

### Maximize (`Maximize`, `Restore`, `ToggleMaximize`, `IsMaximized`, `Maximizable`)
```csharp
panel.Maximizable = false;          // no button, no double-click (default true)
panel.Maximize();                   // fills the screen minus 8 px; panel.Restore() goes back
toolbar.Button("Full screen", panel.ToggleMaximize);
bool big = panel.IsMaximized;
```
Button left of the close button (in its place when `Closable = false`); double-click on an empty spot of
the title bar toggles; dragging the title bar of a maximized panel restores it under the cursor. A
maximized panel follows a new screen size. The state is remembered.

### Resize (`Resizable`, `MinSize`, `Resized`)
```csharp
var panel = UiKit.CreatePanel(Guid, "editor", "Shell editor", null, 1100, 640)
    .MinSize(800, 480);             // default minimum: the CreatePanel / last SetSize size
panel.Resizable = false;            // no grips (default true)
panel.Resized += () => map?.Invalidate();   // after every size change, at most once per frame
```
Grips: the right edge, the bottom edge and the corner grip (bottom right). Maximum: the screen (it also
wins over `MinSize`). Size is remembered next to the position. Extra room goes to what already grows:
`Fill()`, `ScrollView`, `Canvas(...).Fill()` and flexible columns. Column rule: fixed columns keep their
width and flexible ones take the extra; only when **all** columns are fixed, the growth goes to the
widest one (the content, not the sidebars). At the original size the layout is exactly as in 0.1.3. A
canvas redraws at most 10 times a second while an edge is dragged (the image stretches in between).

### Back on screen, reset (`ResetPosition`)
```csharp
settings.Button("Reset window", panel.ResetPosition);   // centred, original size, not maximized; line forgotten
```
On open, and when it grows, a panel whose remembered place is (partly) off screen comes back fully on
screen; a remembered size larger than the screen shrinks to the screen.

### Close like the user (`RequestClose`)
```csharp
footer.Button("Close", () => panel.RequestClose());     // asks OnCloseRequest, honours Closable
```
Returns true when the panel is closed afterwards. `Close()` still closes unconditionally.

### Slider live field (`OnFieldChange`, `HideValue`)
```csharp
f.Labeled("Velocity").Slider(() => v, x => v = x, 100, 2000, 10, "m/s").WithField(72)
    .OnFieldChange(text => typed = text)                // every keystroke; the value is set on Enter/blur
    .Error(() => Bad(typed) ? "Between 100 and 2000 m/s." : null);
row.Slider(() => k, x => k = x, 0, 1, .01).HideValue(); // no 64 px value box: the track takes the width
```
`TextField.OnChange` now also runs once per keystroke (before: once per frame with a change).

### No allocations per frame
`Dropdown(() => list)` (string lists) and `Column.Section(title, () => count)` no longer allocate while
the labels or the count stay the same; mods do not need their own caches for these.

## New in 0.1.2

All additive. A mod that uses anything below declares `[BepInDependency(UiKit.PluginGuid, ">=0.1.2 <0.2.0")]`.

### HUD overlay (`UiKit.CreateHud`)
Non-interactive, own canvas below the panels: no clicks, no cursor change, no input block, no Esc, not
in `AnyPanelOpen`/`PointerOverPanel`. Getters are only polled while it is shown.
```csharp
var hud = UiKit.CreateHud("my.mod", "hits", 360).Anchor(HudCorner.TopRight, 24, 24).Visible(() => lastHit != null);
hud.Body.Label(() => lastHit!.Title, TextStyle.Label).Outline();
hud.Body.Row().Lamp(() => lastHit!.Penetrated, "Penetrated").Kind(() => LampKind.Error);
// AllowedContexts (default Game), HideWhenCursorFree (default true), Opacity (.82), ScaleFactor,
// Width, Height (0 = auto), MaxHeight(px), FadeSeconds, Show()/Hide(), IsShown, UiKit.DestroyHud(hud)
```

### World marker (`UiKit.CreateMarker`)
```csharp
var m = UiKit.CreateMarker("my.mod", "hinge").Follow(() => selectedHinge?.transform).Label(() => selectedHinge?.name);
// .At(() => worldPos), .Visible(() => ...), .Colour(Color), .Size(18, liftPx), Show()/Hide(); designer + game
```

### Canvas and diagram (`Canvas`, `Diagram` → `CanvasView`, `Painter`)
Shapes are rasterised on the CPU into one texture (anti-aliased), runtime textures and text are pooled
uGUI objects; nothing is drawn per frame unless asked (`Invalidate()`, `Version(...)`, `Fps(...)`).
```csharp
var map = col.Canvas(300, d =>
{
    d.Image(topoTexture, 0, 0, 1024, 1024);                         // under the shapes; Apply() + Invalidate() shows new rows
    d.Polyline(route, UiKit.Colour("#dc9e33"), 2.5f, dashPx: 8);     // world units, widths in px
    foreach (var u in units) d.Dot(u.X, u.Y, 6, u.Seen ? Color.red : new Color(1, 0, 0, .35f), Color.black);
    d.Circle(sel.X, sel.Y, 40, Color.white, 1.5f);                   // ring (radius in world units)
    d.Text(sel.X, sel.Y - 12, sel.Name, Color.white, TextStyle.Small, TextAnchor.LowerCenter); // px size, shadow
}).Fill().PanZoom(panButton: 2).Fps(25)
  .OnClick(e => Order(e.World, e.Button, e.Shift))                  // 0 left, 1 right, 2 middle
  .OnDrag(e => { /* e.Delta, e.Button */ }).OnWheel((notches, e) => { });
map.FitWorld(0, 0, 1024, 1024);  // map.ToWorld(local), ToLocal(world), HoverPos, Zoom, Pan, LastDrawMs

col.Diagram(612, 250, () => version, d =>                            // composer style, redraws on version
{
    d.ViewBox(0, 0, 612, 250);
    d.Poly(plateXy, "#3d3c35", "#8f8670"); d.Hatch(plateXy, "#5c5442", 45, 6);
    d.Arrow(20, 120, 200, 120, "#dc9e33"); d.Region("plate", plateXy);
    d.Push(300, 120, rotationDeg: 30); d.Rect(-40, -5, 80, 10, "#5c5442"); d.Pop();
    d.Text(300, 20, "60°", TextStyle.Label, "#fff0cc", TextAnchor.MiddleCenter, 0, background: "#14150f");
}).RegionTooltip(id => id == "plate" ? "Upper plate 80 mm" : null);
```
Painter: `Line`, `Polyline`, `Rect`/`FillRect`, `Circle`/`FillCircle`, `Dot`, `FillPolygon`, `Poly`,
`Arrow`, `Hatch`, `Text`, `Image`, `Region`, `ViewBox`, `Push`/`Pop`, `Clip`, `ToCanvas`, `Size`, `Time`;
colours as `Color` or "#rrggbb[aa]". One redraw of 800×600 with 300 shapes costs about 10 ms of raster
time (tests), plus the texture upload.

### Slider
```csharp
form.Labeled("Angle").Slider(() => angle, v => angle = v, 0, 180, 1, "°").OnDragEnd(Preview);
form.Labeled("Armour").Slider(() => mm, v => mm = v, () => 5, () => maxMm, 5, "mm").WithField(64);
```
Drag or click the track; Shift+drag = fine; wheel = one step, Shift+wheel = 10 steps; `.Format(v => ...)`.

### Confirm and modal
```csharp
panel.Confirm("Delete shell", $"Delete '{shell.Name}'?", "Delete", () => Delete(shell));   // Danger, "Back"
panel.Confirm("Overwrite", "Overwrite the preset?", "Overwrite", Save, ButtonStyle.Primary, "Cancel", onCancel: null);
panel.ShowModal("Rename", m => { m.TextField(() => name, v => name = v); m.Button("Close", panel.CloseModal); });
```
Esc cancels. Enter confirms, except a Danger confirm, where Enter cancels (safe default). `ModalOpen`.

### Field errors
```csharp
f.Labeled("Velocity").TextField(() => v, s => v = s).Numeric().Error(() => !ok ? "Between 100 and 2000 m/s." : null);
f.Button("Save", Save, ButtonStyle.Primary).EnabledWhen(() => !panel.HasErrors);   // panel.FirstError?.Message
```
Works on every element (red ring, message line under it); hidden elements never count.

### Sub-pages
```csharp
var edit = panel.Page("edit", "Edit shell");          // a Stack, built once like panel.Body
edit.Columns(0)[0].Section("Edit").Slider(...);
list.OnSelect(s => { selected = s; panel.Push("edit"); });
// panel.Pop(), PopToRoot(), CurrentPage (null = root), PageDepth, PageChanged
```
The title bar shows a back button and the page trail; Esc goes back one page, then closes.

### Smaller additions
| | |
|---|---|
| `.Alpha(() => float)` | opacity of any element |
| `Label.Colour("#hex" or getter)`, `Label.Color(Color)`, `Label.Outline()` | text colour, HUD outline |
| `Card.Stripe(() => "#hex", 3)`, `Card.Accent(Color)`, `Chip(text, Color)`, `Chip.Colour(Color)` | accent colours (rarity) |
| `ListRow.Swatch`, `ListRow.LampKind`, `ListRow.AccentColor`, `Lamp.Kind(() => LampKind)` | rows and lamps |
| `Warning(...).Severity(StatusKind)`, `.OnClick(...)` | amber/info warning rows, clickable |
| `Stepper.FastStep(shiftStep, ctrlStep?)` | Shift/Ctrl+click steps |
| `panel.AddShortcut("Ctrl+D", action, hint)`, `DefaultAction(action)` | keys for the focused panel only (not while typing/modal; no focus means no action) |
| `panel.OnCloseRequest = () => bool`, `panel.Closable = false` | refuse or forbid closing by Esc/✕/keybind |
| `panel.StayOpenWhilePaused` | stay open under the pause menu |
| `panel.Backdrop(.6f)` | full-screen dim layer that catches every click |
| section body `.SectionVisibleWhen(...)`, `.SectionTitle(() => ...)` | hide a whole section, dynamic heading |
| `row.ShrinkToFit()` | shrink labels/chips instead of clipping controls |
| `UiKit.Colour("#hex")`, `Icon.ChevronLeft`, `HudCorner`, `LampKind` | helpers |

Pause: panels close on the game's own pause menu (`GameTime.PauseState`), not on `Time.timeScale = 0`
set by a mod.


## Node graph / flow editor (0.1.9)

Require `>=0.1.9 <0.2.0`. `Container.NodeGraph().Fill()` occupies the flexible column between the
library and inspector. Nodes are pooled native uGUI objects under one pan/zoom transform, clipped
by RectMask2D. All wires share one anti-aliased CanvasRenderer mesh. This component uses no
CanvasView raster and adds no hooks or native subclasses. A signal tick changes only colours;
geometry is rebuilt for changed wire shapes and topology, with managed and IL2CPP buffers reused.

Retain the node, port and wire handles in your flow model; set `On`, `Chosen`, `Lamp`, `Selected`,
`FooterText`, `Position`, etc. on the main thread. The Kit coalesces changes once per frame.
Connecting ports, signal evaluation, cycles, undo and persistence belong to the consumer.
Selection is explicit: set the selected handles from `OnClick`. A pending wire is visual only.
Wire drawing is limited to 511 slots: 128 vertices per slot gives 65,408 vertices, safely below
65,535 for the UInt16 canvas mesh (512 slots would require 65,536 vertices). A pending wire reserves
one slot, leaving 510 regular wires; an independent port-drag wire reserves another, leaving 509
when both are active. Extra wires stay in the model but are not drawn or hit-tested;
the Kit logs one warning per graph instance. Slots follow insertion order; removing wires restores
available slots. The limit is independent of wire length (each slot reserves up to 32 segments).
See [exact API and handle semantics](logs/0.1.9/API.md).

Controls: drag a block body/header, left-drag empty canvas, middle-drag anywhere, wheel-zoom around
the cursor. Snap is in graph units (8 for the mast); hit radii and the 4 px drag threshold stay in
physical screen pixels. Footer buttons and ports take priority over block dragging. Captured drags
continue outside the panel. Selected/dragged nodes move to the front. `FitAll()` runs after layout.

The following complete editor compiles as part of `examples/Example.csproj`. Construct it once from
your plugin's Load, then bind `editor.Panel` with `UiKit.BindToggle` using your normal Keybinds API
action. Implement the two Save methods in your own model; call SetSignal/SetWireSignal from your
main-thread signal tick (10 Hz is sufficient). It includes a library, inspector, input-to-output
connection flow, selection, deletion and manual Hold/Release.

```csharp
using SprocketUiKit;
using UnityEngine;

// Build once from a plugin's Load(). Register/open the returned panel through the Keybinds API.
// Persistence and flow evaluation belong to the consumer; this sample keeps the handles for live updates.
public sealed class GraphEditor
{
    private readonly NodeGraph graph;
    private readonly Dictionary<string,GraphNode> nodes=new();
    private readonly Dictionary<(string Node,string Port),GraphPort> ports=new();
    private readonly List<GraphWire> wires=new();
    private (string Node,string Port)? pending;
    private GraphWire? selectedWire;
    private GraphNode? selectedNode;
    private int serial;
    public Panel Panel { get; }
    public GraphEditor(string modGuid)
    {
        Panel=UiKit.CreatePanel(modGuid,"flow","I/O sandbox","Example flow editor",1120,650);
        var columns=Panel.Columns(170,0,220);
        var library=columns[0].Section("Library");
        library.Button("Add delay",()=>AddDelay());
        library.Button("Fit",()=>graph!.FitAll());
        graph=columns[1].NodeGraph().Fill().Snap(8).ZoomRange(.5f,1.5f)
            .Hint(()=>pending.HasValue?"Now click an input port...":null);
        var inspector=columns[2].Section("Inspector");
        inspector.Label(()=>selectedNode?.Title??(selectedWire!=null?"Wire":"Click a block or wire"));
        inspector.TextField(()=>selectedNode?.Title??"",text=> { if(selectedNode!=null)selectedNode.Title=text; });
        inspector.Button("Delete",()=>DeleteSelection());
        var brake=Add("brake",20,100,"Brake pedal","IN",GraphColours.In); Port(brake,"signal","Signal",true); brake.FooterButton="Hold";
        var and=Add("and",250,70,"AND","LOGIC",GraphColours.Logic); Port(and,"a","A",false);Port(and,"b","B",false);Port(and,"result","Result",true);
        var delay=Add("delay",250,260,"Delay","LOGIC",GraphColours.Logic);Port(delay,"in","Signal",false);Port(delay,"out","Signal",true);delay.FooterText="0.5 s";
        var hinge=Add("hinge",490,100,"Hinge open","OUT",GraphColours.Out);Port(hinge,"open","Open",false);
        Connect("brake","signal","and","a");Connect("and","result","delay","in");Connect("delay","out","hinge","open");
        graph.OnClick(Click).OnPortDrag(PortDrag).OnNodeMoved((id,position,final)=> { if(final)SavePosition(id,position); })
            .OnViewChanged((pan,zoom)=>SaveView(pan,zoom)).FitAll();
    }
    private GraphNode Add(string id,float x,float y,string title,string tag,Color colour)
    { var node=graph.AddNode(id,x,y,title,tag,colour);nodes.Add(id,node);return node; }
    private void Port(GraphNode node,string id,string label,bool output)
        =>ports.Add((node.Id,id),output?node.AddOutput(id,label):node.AddInput(id,label));
    private void AddDelay()
    { var node=Add("delay"+serial++,120,180,"Delay","LOGIC",GraphColours.Logic);Port(node,"in","Signal",false);Port(node,"out","Signal",true); }
    private void Connect(string from,string output,string to,string input)
    {
        // The sample flow allows one driver per input; fan-out is unrestricted.
        for(int i=wires.Count-1;i>=0;i--) if(destinations[wires[i].Id]==(to,input)) { graph.RemoveWire(wires[i].Id);destinations.Remove(wires[i].Id);wires.RemoveAt(i); }
        string id="wire"+serial++;wires.Add(graph.AddWire(id,from,output,to,input));destinations[id]=(to,input);
    }
    private readonly Dictionary<string,(string Node,string Port)> destinations=new();
    private void Cancel()
    { if(pending is { } start&&ports.TryGetValue(start,out var port))port.Chosen=false;pending=null;graph.EndPendingWire(); }
    private void PortDrag(GraphPortDrop drop)
    {
        var hit=drop.Drop;
        if(hit.Kind!=GraphHitKind.Port||hit.NodeId==null||hit.PortId==null)return;
        var target=ports[(hit.NodeId,hit.PortId)];
        if(target.IsOutput==drop.FromOutput)return;
        if(drop.FromOutput)Connect(drop.FromNode,drop.FromPort,hit.NodeId,hit.PortId);
        else Connect(hit.NodeId,hit.PortId,drop.FromNode,drop.FromPort);
    }
    private void Click(GraphHit hit)
    {
        if(hit.Button!=0)return;
        if(hit.Kind==GraphHitKind.Port)
        {
            var key=(hit.NodeId!,hit.PortId!);var port=ports[key];
            if(port.IsOutput) { Cancel();pending=key;port.Chosen=true;graph.BeginPendingWire(key.Item1,key.Item2); }
            else if(pending is { } from) { Connect(from.Node,from.Port,key.Item1,key.Item2);Cancel(); }
            return;
        }
        Cancel();
        foreach(var node in nodes.Values)node.Selected=node.Id==hit.NodeId;
        foreach(var wire in wires)wire.Selected=wire.Id==hit.WireId;
        selectedNode=hit.NodeId==null?null:nodes[hit.NodeId]; selectedWire=hit.WireId==null?null:graph.Wire(hit.WireId);
        if(hit.Kind==GraphHitKind.FooterButton&&selectedNode!=null)
        { selectedNode.Lamp=!selectedNode.Lamp;selectedNode.FooterButton=selectedNode.Lamp?"Release":"Hold";ports[(selectedNode.Id,"signal")].On=selectedNode.Lamp; }
    }
    private void DeleteSelection()
    {
        Cancel();if(selectedWire!=null) { graph.RemoveWire(selectedWire.Id);wires.Remove(selectedWire);destinations.Remove(selectedWire.Id);selectedWire=null; }
        if(selectedNode!=null)
        {
            string id=selectedNode.Id; graph.RemoveNode(id);nodes.Remove(id);selectedNode=null;
            foreach(var key in ports.Keys.Where(key=>key.Node==id).ToArray())ports.Remove(key);
            for(int i=wires.Count-1;i>=0;i--)if(graph.Wire(wires[i].Id)==null) { destinations.Remove(wires[i].Id);wires.RemoveAt(i); }
        }
    }
    // Invoke from the consumer's existing main-thread signal tick (for example 10 Hz).
    public void SetSignal(string node,string port,bool on) => ports[(node,port)].On=on;
    public void SetWireSignal(string id,bool on) { var wire=graph.Wire(id);if(wire!=null)wire.On=on; }
    private void SavePosition(string id,Vector2 position) { /* Store in the consumer's flow model. */ }
    private void SaveView(Vector2 pan,float zoom) { /* Store with the consumer's editor state. */ }
}
```

The demo's Graph tab has the four-block example, a 60-node/100-wire Stress scene with 10 Hz signal
changes, average whole-frame milliseconds, and a Compare Canvas switch. Default is the shared
canvas. The isolated-world alternative exists for a like-for-like in-game comparison; no native
batching measurement has been made in this build because starting/running the game was excluded.
Pure timings and allocation results are in `logs/0.1.9/tests.log`; they do not prove native rendering,
GC behaviour or game frame times. See TESTPLAN.md before installation by the coordinator.
