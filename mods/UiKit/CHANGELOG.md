# Changelog

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
- Complete compiling flow editor: examples/GraphEditor.cs; API: logs/0.1.9/API.md. No installation.


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
