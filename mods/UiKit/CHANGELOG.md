# Changelog

## 0.1.13 (2026-11-15, development build; live validation pending)
- Add `public Row Container.Setting(SprocketKeybinds.ModSetting setting)` and the grouped Mod settings presenter; Keybinds dependency is now `>=0.1.10 <0.2.0`.
- Register the seven Kit settings; FontSource and advanced SkipBlockTargets require restart, the other five work live.
- Deferred panel focus starts after Settings closes; StayOpenWhilePaused panels can take focus above pause. No changed hooks.
- Cache setting values and steady UI snapshots/formatting; remove menu idle managed allocations on these paths.
- Ship DLL plus XML documentation; hide runtime entry points from IntelliSense and document all public types and core surface members.
- Reorganize the guide by component; preserve original version context below. API remains additive to 0.1.12.

### 0.1.13 acceptance (pending)

1. Main menu: Settings > "Mod settings / Open" changes to "Opens after Settings". Close with Back, then repeat with Accept and Cancel; each round shows the panel only after Settings closes.
2. In the panel, change UI Kit Scale live and toggle DetailedLogging; check the cfg changes. Esc closes the panel.
3. In game, pause > Settings > Open, then leave Settings; the panel appears above the pause menu and stays open.
4. In the designer, bind the normally unbound "Open mod settings" action, then use it to open the panel.
5. Check "Restart required" for FontSource and SkipBlockTargets, conditional Reset, and confirm/cancel "Reset all".
6. "Show advanced" reveals SkipBlockTargets; turn it off to hide it again.
7. Regress focus: waiting panels suspend no keys, visible panels gain focus; clicking outside releases it, including above pause.
8. Verify an empty registry explanation and a consumer page using Container.Setting, including external value updates, slider release and text Enter/blur commits.


## 0.1.12 (2026-11-09, development build, not yet tested in game)
- #38: AddShortcut and Enter (DefaultAction or modal) dispatch only to KeyboardFocus,
  never to the top panel when focus is absent; Enter uses the focused panel's modal state.
- Add read-only Panel.HasFocus and UiKit.FocusedPanel; HUDs never own focus.
- Pure recipient policy and state-machine tests; Verify enforces exactly the two API additions
  against 0.1.11, getter wiring, unchanged protected input paths and byte-identical Hooks.cs.
- Window demo exposes HasFocus and a Ctrl+Shift+D counter. Existing mutation-safe shortcut
  ToList snapshot retained; no added per-frame allocation. Esc/text/popup/guards unchanged.
- Release 0.1.12 / assembly 0.1.12.0; no new Harmony hooks.

## 0.1.11 (2026-11-08, development build, not yet tested in game)
- #36: one keyboard-focus owner for normal panels in every context. Open/press takes focus;
  outside press releases it, close hands it on. Pause, scene changes, alt-tab and settings
  clear focus. Focus title tokens distinguish the owner without changing pointer blocking or Esc.
- Shared panel/typing InputAction ownership, UI-map and DontSave exclusions, independent input
  config options, immediate restore on every release/unload/exception and same-frame self-heal.
  Failed restores remain tracked for retry; one-second rescans catch re-enabled/new game actions.
  Panel focus leaves the typing proxy alone, preserving mod toggle keys. AddShortcut is unchanged;
  no consume parameter. Detailed batch logs include counts and reasons.
- #32: OnPortDrag(Action<GraphPortDrop>), independent dashed port-drag wire after >4 physical px,
  exact source/direction/release hit; short port clicks retain OnClick. No node/view movement.
- #33: per-block top-down picking (footer, ports, body), then wires/canvas; hidden ports cannot
  steal hits. #34: PortHitRadius(float px), default 7 physical px.
- #35: dynamic Tabs labels, zero-byte unchanged polling; rename updates text and count rebuilds.
  #37: NodeGraph.ViewSize and VisibleRect expose layout extent and graph-space view bounds.
- Demo Window shows focus/suspended actions and editable/addable/removable tabs; Graph connects
  in either drag direction. Verify compares 0.1.10, checks new interop/guard routes and Hooks bytes.
- Release 0.1.11 / assembly 0.1.11.0; additive API only. No changed or new Harmony hooks.


## 0.1.10 (2026-11-06, development build, not yet tested in game)
- NodeGraph fixes (#31): use a UInt16 index buffer with a 511-slot cap (pending included), skip
  overflow safely and warn once. Topology/slot changes fully upload Clear, vertices, uv, colors32,
  triangles, SetMesh in that order. Detailed rebuild diagnostics are throttled to 1 Hz except for
  slot-count changes, with counts, index format and world-layer CanvasRenderer count.
- Raw wire renderer clipping intersects the canvasRect of every active RectMask2D ancestor,
  including padding, in canvas space. Empty clips cull wires. Shared Canvas remains the default;
  isolated world Canvas is a demo-only experiment with coordinate conversion and pending live QA.
- Node tags now sit inside measured chips (36 px minimum, 40% block width maximum), with padding
  and centred text. Titles begin after the chip and retain ellipsis overflow.
- Graph demo adds Mast pattern: recreate wires with fresh ids and restart the pending wire at
  10 Hz while the mouse moves. Includes IN, LOGIC and a long tag.
- No public API additions/changes, hooks, native subclasses, ClassInjector or consumer reflection.
  Verify also compares baseline 0.1.9 and checks numeric dependency acceptance and mesh upload IL.


## 0.1.9 (2026-11-06, development build, not yet tested in game)
- NodeGraph (#30): retained mutable node/port/wire handles, pooled uGUI blocks, native single wire
  mesh with soft edges, pending dashed wire, pan/zoom, snap, FitAll, hint, ordered hit-testing and
  guarded click/move/view events. Input capture extends Kit.cs; no new hooks, native subclass,
  ClassInjector, consumer reflection or raster route.
- Shape and colour updates are separate. Only incident wire curves are tessellated on node moves;
  signal/selection changes update colour buffers only. Idle graph work and geometry reuse are
  allocation-free in pure tests; native allocation behaviour remains an in-game gate.
- Graph demo: Brake pedal, AND, Delay, Hinge open; connect/select wires, Hold/Release, Stress
  (60 nodes, 100 wires, 10 Hz signals), average frame ms and shared/isolated Canvas comparison.
- Shared Canvas is the provisional default. Native batching A/B measurement is deferred because
  the brief excludes launching the game. The demo supplies both modes for the same scene.
- Verify checks mesh/material/clipping interop, graph route restrictions and compatibility with
  baseline 0.1.8 as well as every earlier baseline. 92 additive public entries versus 0.1.8.
- Complete compiling flow editor: examples/GraphEditor.cs; API: docs/NODEGRAPH-API.md. No installation.


## 0.1.8 (2026-11-02, development build, not yet tested in game)
Purely additive: Verify.ps1 checks the public API against 0.1.1, 0.1.3, 0.1.4, 0.1.5, 0.1.6 and the
installed 0.1.7. Consumers keep working without a rebuild. New public entries versus 0.1.7: 7.
- **RadialProgress** (#29, requested via Carousel Autoloader for the reload HUD):
  `Container.Radial(Func<float> value, float size = 48, Func<string?>? centre = null)` returns a
  `RadialProgress` with `.Thickness(float px)`, `.Colour(Color)`, `.Colour(Func<Color>)` and
  `.Track(Color)`; tooltips come from `Element`. The value is clamped to 0..1 (NaN/infinite = 0), the
  ring fills clockwise from 12 o'clock, the default colour is Accent, the track is the muted
  `RadialTrack` token, and the default thickness is about 12% of the diameter (minimum 3 px). The
  optional centre text uses `TextStyle.Value` or smaller and is only set when it changes.
- One procedural ring sprite (same anti-aliasing as `Assets.Circle()`), cached by diameter and
  thickness in device pixels, is shown as a plain track image and as an `Image.Type.Filled` fill
  (`Radial360`, `Top`, clockwise). The IL2CPP interop has these properties (Verify now checks
  `set_type`, `set_fillMethod`, `set_fillAmount`, `set_fillOrigin`, `set_fillClockwise`), so there is
  no `CanvasView` and no CPU raster per value. `fillAmount` is only written on a change of at least
  1/720; equal value and equal text allocate 0 bytes per poll.
- Works in a panel, in `HudPanel.Body` and in a `Row`. Demo: the HUD tab has three rings of different
  sizes, the 56 px one fills in 4 s with the seconds left in the centre and turns green when full;
  the Components > Progress section has one ring in a Row next to a label.
- Docs: README section with a reload-HUD example, `docs/STYLE.md` tokens, TESTPLAN block. Pure tests:
  358 total (21 new), all pass.

## 0.1.7 (2026-11-02, development build, not yet tested in game)
Purely additive: Verify.ps1 checks the public API against 0.1.1, 0.1.3, 0.1.4, 0.1.5 and the installed
0.1.6. Consumers keep working without a rebuild. New public entries versus 0.1.6: 34.
- **LineChart** (#28, requested via Expanded Ballistics for protection against angle):
  `Container.LineChart(float height = 220)` with `.Series(name, points, colour?, SeriesStyle)`,
  `.XAxis/.YAxis(label, unit, min?, max?, tick?)`, `.TickFormat(...)`, `.RefLine(label, y, colour?)`,
  `.Legend(bool)` and `.Empty(string)`. One to four series; `SeriesStyle.Line/Step/Points`; a fixed
  palette order (Accent, Success, Error, Text2). Automatic ranges use 1/2/5 x 10^n ticks; a fixed
  range is exact. NaN/infinite points are skipped and leave a gap; one point is a dot. Hover shows a
  vertical line, a dot per series and a tooltip with name, value and unit.
- **BarChart** (#28, penetration budget): `Container.BarChart(float height = 220)` with
  `.Bars(Func<IReadOnlyList<ChartBar>>)`, `.Waterfall(start, startLabel, endLabel)`, `.YAxis`,
  `.TickFormat`, `.RefLine` and `.Empty`. `ChartBar` has `Label`, `Value`, optional `Colour` and
  `Tooltip`. A waterfall floats each delta bar from the old to the new remainder, starts at 0..start
  and ends at 0..remainder; a layer that reaches 0 is clamped, marked "Stopped" and ends the waterfall.
- Charts are built on the existing `CanvasView`/`Painter`; there is no second rasteriser. They redraw
  only when the data list (reference, count and a cheap value hash), the size or the range changes;
  hover uses uGUI images and the Kit tooltip and never rasterises. The wheel goes to a surrounding
  `ScrollView`; no pan or zoom. Full 600 x 240 line chart with 4 x 81 points: 0.28 ms at scale 1 and
  0.60 ms at 1440p (pure raster, tests).
- Demo: tab "Chart" with protection against angle (KE/HEAT and an RHA reference), the remaining
  protection as a Step chart and the same layers as a Waterfall, plus "Change data" and "Stop at 0".
- Docs: README section with both Ballistics examples, `docs/STYLE.md` chart tokens, TESTPLAN block.
  Pure tests: 337 total (36 new), all pass.

## 0.1.6 (2026-11-02, development build, not yet tested in game)
Purely additive: Verify.ps1 checks the public API against 0.1.1, 0.1.3, 0.1.4 and the installed 0.1.5.
Consumers keep working without a rebuild.
- **Table<T>** (#27, requested via Expanded Ballistics for the Cassette Composer protection table):
  `Container.Table<T>(Func<IReadOnlyList<T>> rows)` with `.Column(header, cell, ColumnWidth, CellAlign,
  headerTooltip?)`, `.CellStyle((row, column) -> CellStyle)`, `.RowTooltip`, `.CellTooltip`, `.Zebra`,
  `.OnSelect`, `.Selected`, `.Empty` and `.MaxHeight` (plus the usual `.Fill()`). Public value types:
  `ColumnWidth.Fixed(px)`, `ColumnWidth.Auto`, `ColumnWidth.Fill(weight = 1)`, `CellAlign` and
  `CellStyle` (`Color?`, `Strong`, `Faint`).
- Column widths are computed once per layout in whole pixels: Fixed exact, Auto around the widest
  header/cell (remeasured only when a text changes), Fill by weight with the longest header word as
  minimum (40 px floor). When too narrow, Fill shrinks to its minimum first, then Auto proportionally;
  Fixed never shrinks. Header and body share the same x positions; text gets an ellipsis instead of
  crossing into the next column. Headers wrap to two lines with one common header height.
- Default height is the full table and the surrounding `ScrollView` scrolls it; `.MaxHeight(h)` or
  `.Fill()` gives a fixed header and a body with the 0.1.5 scrollbar. Only visible rows are pooled and
  active; cell text is set only when it changes and the text cache allocates nothing while texts are
  equal. Truncated cells show their full text as a tooltip unless the mod supplies one.
- Demo: tab "Table" with the protection table (long wrapping header and header tooltip, one coloured
  cell, one Strong cell), a 60-row `MaxHeight` table with zebra and selection, and a table inside a
  `ScrollView`, plus "Resize the panel: columns stay aligned.".
- Docs: README section with a complete protection example, `docs/STYLE.md` table tokens and rules,
  TESTPLAN block. Pure tests: 301 total (28 new for table geometry, alignment, ellipsis, row index,
  zebra/selection and the zero-allocation text cache), all pass.

## 0.1.5 (2026-11-02, development build, not yet tested in game)
Purely additive: no public API change; Verify.ps1 checks the public API against 0.1.1, 0.1.3 and the
installed 0.1.4. Consumers get the new bar without a rebuild.
- **Real scrollbar** (#26, Expanded Ballistics Cassette Composer): `ScrollView`, `ListView<T>` and an open
  dropdown list now share one scrollbar helper with a draggable thumb, track paging and the existing wheel.
  Drag keeps the grab point under the cursor and captures outside the bar and panel; releasing outside
  does not click through to the game. Track click pages one viewport; holding repeats after .45 s at .07 s
  (the Stepper rhythm) until the thumb is under the cursor.
- Look: 3 px `Border` thumb at rest, 6 px `Text2` on hover and `Accent` while dragging; a subtle
  `#24231c` track appears while hovered/dragged. Tokens and the resize-grip rule are in `docs/STYLE.md`.
- Only overflowing content gets a bar; its click strip is at most 10 px from the right edge and the
  0.1.4 content width (6 px reserved) is unchanged. The bar exists on every built-in vertical scroller
  and is not drawn horizontally anywhere.
- `ListView`: bar clicks do not select a row or press delete. Open dropdown: dragging/paging does not
  close it or choose an option; an option click works as before.
- Demo: tab "Scroll" with an overflowing `ScrollView` in the rightmost column, an overflowing `ListView`
  and a 24-option dropdown. Pure tests: 273 total (30 new for the scrollbar math and grip rule), all pass.

## 0.1.4 (2026-11-02, development build, not yet tested in game)
Purely additive: Verify.ps1 checks the public API against both `baseline/SprocketUiKit-0.1.1.dll`
(272 entries) and `baseline/SprocketUiKit-0.1.3.dll` (444 entries): nothing removed or changed.
- **Maximize** (#24): button left of the close button (in its place when not closable), double-click on
  the title bar, drag from maximized restores under the cursor, follows screen changes.
  `Panel.Maximize()`, `Restore()`, `ToggleMaximize()`, `IsMaximized`, `Maximizable` (default true).
- **Resize** (#25): right edge, bottom edge and corner grip. `Panel.Resizable` (default true),
  `MinSize(w, h)` (fluent; default the CreatePanel/last SetSize size), event `Resized` (at most once per
  frame). Maximum is the screen. A canvas redraws at most 10/s while an edge is dragged. Column sets
  where every column is fixed give the panel's growth to their widest column; otherwise unchanged.
- Size and maximized state are remembered in `.panels.cfg` next to the position (`x,y,w,h[,max]`);
  0.1.3 lines (`x,y`) stay valid, and unresized panels still write `x,y`.
- **Back on screen** (#23): on open and when growing, a panel (partly) off screen comes back; a
  remembered size larger than the screen shrinks. `Panel.ResetPosition()`.
- `Panel.RequestClose()` (#17) -> bool: closes like Esc/close button (Closable, OnCloseRequest).
- `Slider.OnFieldChange(Action<string>)` (per keystroke in the `WithField` field) and
  `Slider.HideValue()` (#16). `TextField.OnChange` now runs per keystroke instead of once per frame.
- No per-frame allocation (#18) for `Dropdown(Func<IReadOnlyList<string>>)` (Choice array cached while
  the labels are equal) and `Section(title, Func<int>)` (text rebuilt only on a new count); element
  site names and icon sprite lookups are cached too.
- `SetSize` now also sets the resize minimum and keeps a growing panel on screen.
- `Icon.Maximize`, `Icon.Restore`, `Icon.ResizeGrip`.
- Demo: tab "Window" (MinSize 1000 x 560, Resized counter, a `Maximizable = false` panel,
  ResetPosition, RequestClose with a refuse switch); slider tab: live field and HideValue.

## 0.1.3 (2026-10-31, hotfix)

- Dashed lines (`Painter.Polyline/Line/Rect` with dash) could loop forever through float rounding and freeze the game (AI Orders route). The dash phase now wraps per period, tiny steps snap to the boundary, and an iteration cap backs it up. Regression test: 9600 random dashed polylines at 1, 1.33, 1.5 and 2 px scale.
- `Label.Wrap()` now also works on a label added after the panel was built (e.g. in `Repeat` templates); it stayed NoWrap + Ellipsis before (#21, Expedition offer cards).

## 0.1.2 (2026-10-31, development build, not yet tested in game)
Purely additive: every public type and member of 0.1.1 is unchanged (checked by Verify.ps1 against
`baseline/SprocketUiKit-0.1.1.dll`). Consumers asking `>=0.1.1 <0.2.0` keep working without a rebuild.
- HUD overlay `UiKit.CreateHud` → `HudPanel` (own canvas under the panels, no clicks/cursor/Esc).
- Drawing surface `Container.Canvas` / `Container.Diagram` → `CanvasView` + `Painter` (CPU raster to
  one Texture2D, images and text pooled; pan/zoom camera; left/right/middle click and drag, wheel,
  modifiers, region tooltips; redraw on Invalidate, Version or Fps).
- `Slider` (drag, track click, wheel, Shift fine/coarse, live limits, linked field, OnDragEnd).
- `Panel.Confirm`, `ShowModal`, `CloseModal`, `ModalOpen`.
- Field errors `.Error(() => string?)`, `Panel.HasErrors`, `Panel.FirstError`.
- Sub-pages `Panel.Page/Push/Pop/PopToRoot/CurrentPage/PageDepth/PageChanged`; Esc goes back first.
- Demo keybind factory default is now **unbound** (F6 clashed with QoL flashlight); bind it in Settings.
- Smaller additions: `.Alpha(getter)`, `Label.Colour/Color/Outline`, `Card.Stripe/Accent`,
  `ListRow.Swatch/LampKind/AccentColor`, `Lamp.Kind`, `LampKind`, `Chip(text, Color)`/`Chip.Colour`,
  `WarningRow.Severity/OnClick`, `Stepper.FastStep`, `Panel.AddShortcut/DefaultAction/OnCloseRequest`,
  `Panel.Closable`, `Panel.StayOpenWhilePaused`, `Panel.Backdrop(alpha)`, section body
  `SectionVisibleWhen/SectionTitle`, `Row.ShrinkToFit`, `UiKit.CreateMarker` → `WorldMarker`,
  `UiKit.Colour(hex)`, `Icon.ChevronLeft`, `HudCorner`.
- Behaviour fix: panels close on the game's own pause menu (GameTime.PauseState via the RequestPause
  hook), no longer on `Time.timeScale == 0` alone (Expedition menu); timeScale stays the fallback if
  the pause hook is unavailable.
- The close button and the toggle keybind now go through `OnCloseRequest`/`Closable` (defaults
  unchanged: they close).


## 0.1.1 (2026-10-31)

- Esc that closes a kit panel, dropdown or text field no longer also opens the game's pause menu (prefix on `GameTime.RequestPause`; traced with DetailedLogging).

## 0.1.0 (2026-10-31, development build, not yet tested in game)
- New shared plugin `sprocket.uikit` (depends on Keybinds API `>=0.1.3 <0.2.0`).
- Panel with title bar, context chip, drag, close, status line and key hints; columns, sections,
  footer blocks, stacks, rows, cards, scroll views, collapsible sections, repeaters.
- Components: Button (primary/normal/quiet/danger/disabled, compact, icon), IconButton,
  SegmentedButtons, Stepper, Toggle, Dropdown (static/dynamic/grouped, inline, accent; overlay
  popup that flips up), TextField, Tabs, ListView<T>, Lamp, ProgressBar, Chip, Tooltip, WarningRow.
- Central behaviour: Input System clicks/typing, cursor, 9 gameplay input-block hooks, designer
  click-through guard, Esc, z-order, remembered positions, scale = screen height/1080 × factor,
  close on pause/scene change, Guard around every callback, Diagnostics.DetailedLogging (default false).
- Demo panel (keybind "UI Kit / Demo", factory default F6) with all components, a mini I/O
  example, rule rows (IF [source] [event] THEN [command] after [stepper]) and a click log.

## Historical guide context through 0.1.12

<details>
<summary>Original guide and release notes before the topic reorganization</summary>

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
See [exact API and handle semantics](docs/NODEGRAPH-API.md).

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
Pure timings and allocation results are in the build's test log (not shipped); they do not prove native rendering,
GC behaviour or game frame times. See TESTPLAN.md before installation by the coordinator.

</details>
