# Sprocket UI Kit: design agreement (0.1.0, 2026-10-31)

Source: the user's chosen canvas (Main.dc.html, Components.dc.html, Rules.dc.html). All sizes are
reference px at 1080p; the kit scales everything by screen height / 1080 × `Display.Scale`.
Font: the game's TMP font (`TMP_Settings.defaultFontAsset`, fallback: most used font of live scene
TMP texts). "Condensed" headings are emulated with caps + letter spacing.

## Tokens
| Role | Colour | Role | Colour |
|---|---|---|---|
| Panel | `#14150f` (window .96 alpha) | Text | `#fff0cc` |
| Header/footer, blocks, popup | `#1b1c15` | Text 2 / labels | `#b8ad94` |
| Row | `#24231c` | Value | `#c9bc9c` |
| Button | `#2b2924` (hover `#3a372e`) | Faint (counts, keywords) | `#8f8670` |
| Selected | `#3a3220` + border `#dc9e33` | Section heading | `#e9c27a` |
| Accent | `#dc9e33` (border `#f0b44a`, ink `#1b1406`) | Accent text | `#f3cf86` |
| Lamp on | `#5fe07a` (+8 px glow) | Off / switch track | `#3d3c35` |
| Error | `#e07a4f` (text `#ffcbb3`, row `#2a1c15`/`#8a4a30`) | Success text | `#8fd99c` |
| Frame | `#4a4536` | Divider | `#3a372e` |
| Border | `#5c5442` (hover `#8a7a52`) | Disabled | `#1f1e18` / `#34322a` / `#6f6857` |

## Sizes
- Window: radius 6, 1 px frame, shadow 0 18 48 (.55). Title bar 44 (title 20 caps bold, subtitle 13,
  context chip, "drag" hint, 32 px close). Footer 32 (status 12, key caps radius 3).
- Columns: padding 14, gap 10, 1 px dividers; section heading 15 caps accent + count 12 right;
  rows gap 4. Footer block: `#1b1c15`, border, radius 4, padding 12, gap 8, title 12 caps.
- Button 36 high, padding 16, radius 4, text 14 (compact 28/10/12). Icon button 32. Help "?" 24 round.
- Field 34 (dropdown), text field 32, inline dropdown 32 (sized to text), segmented 34 (13 text),
  stepper 32 + value 72 (compact 28 + 52), switch 44×24 knob 18, tab 36 + 2 px underline.
- List row 36 (title) / 40 (title + subtitle) / card rows with progress 4 and note; rule card min 48.
- Lamp 10, progress 4, chip 20 (context) / 17 (tag) / 23 (command), tooltip max 280 on `#fff0cc`.
- Dropdown popup: 4 px below (or above when no room), `#1b1c15`, accent border, items 31,
  group headings 26 (12 caps faint), max 8 rows then wheel scroll; on its own overlay layer.

## Behaviour (always the kit, never per mod)
- Open with a Keybinds API action (`UiKit.BindToggle`); Esc closes the top panel (or the open
  dropdown; while typing Esc reverts the text). Close button top right.
- Drag at the title bar; position remembered per panel; kept on screen. Since 0.1.4: maximize (button
  or double-click), resize (right/bottom edge, corner grip), size and maximized state remembered.
- Clicks, hover, drag, wheel and typing come from the Input System (not EventSystem/IMGUI).
- In game: cursor freed while open and restored after; fire/scope/orders/camera blocked (9 hooks).
  Pause menu or scene/context change closes panels.
- Designer: no click-through (`Inputs.pointerOverGUI` while over a panel); a GraphicRaycaster on the
  kit canvas also stops native uGUI underneath.
- While a text field has focus, game keys and other mod keybinds are suspended.
- Several panels at once; clicking brings one to the front.
- Every mod callback runs in `Guard`: an exception is logged once and the panel keeps working.
- Colour is never the only signal: lamps have labels/tags, status has an icon and text.

## Additions in 0.1.2
Tokens (new): warning row `#2b2410` / border `#a57a2c` / text `#f3cf86` / icon `#dc9e33`; lamp kinds
on `#5fe07a`, warning `#dc9e33`, error `#e07a4f` (same glow); modal backdrop rgba(0,0,0,.55); HUD
background `#14150f` at .82; canvas background `#1b1c15` with a `#3a372e` border; slider track
`#3d3c35`; swatch outline `#8f8670`.
- **Slider** 32 high: track 4 (`#3d3c35`, radius 2), accent fill, knob 14 (accent with a 1.5 px ink
  `#1b1406` ring; 16 on hover/drag, `#e8ab42`), value 13 px right-aligned in 64 (or a 64 px numeric
  field). Disabled: track `#34322a`, knob/fill `#6f6857`. The K6 proposal's `#14150f` track was not
  used: it is invisible on the panel background.
- **Field error**: 1 px `#e07a4f` ring over the control (radius 4), value text `#ffcbb3`, message 12 px
  `#ffcbb3` in a 16 px line under the control (reserved only while there is a message).
- **Modal / confirm**: backdrop over the panel body only; card `#1b1c15`, accent `#dc9e33` border,
  radius 6, padding 16, gap 12, width 440; title = Heading (15 caps); buttons right-aligned, cancel
  normal + confirm (danger by default). Esc cancels; Enter confirms, except a danger confirm (cancels).
- **Pages**: back icon button (chevron left, 32) left in the title bar instead of the title icon; the
  trail ("Shell list  /  Edit shell") replaces the subtitle in accent text `#f3cf86`; footer hint
  "Esc back". Esc goes back one page, then closes.
- **Warning severity**: Error = 0.1.0 look; Warning = amber row; Info/Success = row look (`#24231c`,
  `#3a372e`, text `#fff0cc`, icon text-2 / success). Clickable rows get the `#8a7a52` hover border.
- **Accent colours** (rarity etc.): Card/ListRow 3 px left bar plus border at .6 alpha; Chip text and
  border. Colour is never the only signal: the text names it too. List swatch 10×22 with a 1 px
  `#8f8670` outline, 8 px before the title.
- **HUD**: own canvas, sorting order 80 (panels 30000), no raycaster; radius 4, padding 10, default
  width 420, auto height (max 600); `Label.Outline()` for text on bright maps. Default: game only,
  hidden while the cursor is free / a kit panel is open / paused; .15 s fade.
- **World marker**: ring 18 + inner dot in accent, label 14 bold `#fff0cc` with a black 1 px shadow
  above it, on the HUD canvas.
- **Canvas**: background `#1b1c15`, 1 px `#3a372e` border, radius 4; shapes anti-aliased; widths,
  dashes, dot radii and text in reference px (never scaled by zoom); text 1 px black shadow.
- **Full-screen backdrop** (`Panel.Backdrop(alpha)`): black at the given alpha (default .6) under the
  panel, catches all clicks.

## Additions in 0.1.4
- **Maximize margin**: token `MaximizeMargin` = **8 px**. Maximized = the visible view (`ViewW` x
  `ViewH`, reference px) minus 8 on every side; it follows a new screen size.
- **Title bar buttons**: maximize/restore is an icon button (32, same look as the close button) directly
  left of the close button with a 4 px gap (`TitleButtonGap`), or in the close button's place when the
  panel is not closable. The drag hint and title move left accordingly.
- **Icons** (16-unit box, line icons like the others, `#fff0cc`): `Maximize` = one square (3.5..12.5,
  stroke 1.5); `Restore` = two overlapping squares, front 3..10 / 6..13, back 6..13 / 3..10 (stroke 1.5),
  as in Windows; `ResizeGrip` = three diagonal strokes in the bottom-right corner (stroke 1.4).
- **Resize grip**: 12 px `ResizeGrip` icon, 3 px from the bottom-right corner (`GripIcon`, `GripInset`),
  faint `#8f8670`, accent `#dc9e33` while hovered or dragged. Hidden when not resizable or maximized.
- **Grip zones** (behaviour, not drawn): right and bottom bands 4 px inside + 4 px outside the panel
  edge, corner 16x16 inside + the outside band. Left and top edges have no grip. They take precedence
  over content in those bands, count for `PointerOverPanel` and the click-through guard, and an
  invisible raycast image covers the outside band. The cursor shape does not change.
- **Double-click**: two presses within 0.5 s and 6 px on an empty spot of the title bar.
- **Limits**: minimum = `CreatePanel`/last `SetSize` size or `MinSize`; maximum = the screen (wins
  over the minimum). Dragging a maximized title bar restores after 4 px of movement.

## Additions in 0.1.5
- **Tokens**: `ScrollW` = **3 px** (thumb at rest), `ScrollWideW` = **6 px** (thumb while hovered or
  dragged, and the track width), `ScrollHitW` = **10 px** (click strip from the right edge, only while
  the content overflows), `ScrollMinH` = **20 px** (minimum thumb length).
- **Thumb colours**: rest `ScrollThumb` = `#5c5442` (`Border`); hover `ScrollThumbHover` = `#b8ad94`
  (`Text2`); dragging `ScrollThumbDrag` = `#dc9e33` (`Accent`).
- **Track**: `ScrollTrack` = `#24231c` at .55 (`rgba(36,35,28,140)`), radius 2, 6 px wide, only visible
  while the bar is hovered or dragged; the thumb sits on top and grows to the same 6 px.
- **Placement**: the rest thumb keeps the 0.1.4 look: 3 px at the right edge (`ScrollView`, `ListView`)
  or 3 px inside the popup border (`PopupList`). The layout does not change: overflowing content still
  reserves 6 px; the 10 px strip may overlap 4 px of content and wins there.
- **Resize rule**: inside the panel the bar's strip wins over the 4 px corner/right grip; the grip band
  **outside** the right edge still resizes (the bottom band and corner outside band are unchanged). A bar
  in a rightmost column is inside the 14 px column padding, so there is no overlap in normal layouts.
- **No horizontal bar**: the kit never scrolls horizontally and draws no horizontal indicator.

## Additions in 0.1.6
- **Table tokens**: row height `TableRowH` = **32 px**; cell padding `TableCellPadX` = **10 px**,
  `TableCellPadY` = **6 px**; column gap `TableColumnGap` = **12 px**; header line height
  `TableHeaderLineH` = **16 px**, header vertical padding `TableHeaderPadY` = **8 px** (a one-line
  header is 32 px, a two-line header 48 px); minimum fill column `TableMinCol` = **40 px** (the
  longest header word can raise it); row/header radius `TableRadius` = **2 px**.
- **Header**: `TableHeaderBg` = `#1b1c15` (`HeadFoot`), header text `BlockTitle` (`#b8ad94`, 12 px
  caps, bold, 10 spacing), and a 1 px `TableHeaderLine` = `#5c5442` (`Border`) under the whole row.
  The header scrolls with the body by default; with `.MaxHeight()` / `.Fill()` only the body scrolls.
- **Rows**: normal `TableRowBg` = `#14150f` (`Panel`), zebra `TableZebra` = `#1b1c15` (`HeadFoot`);
  hover `#2b2924` (`Btn`) with `#8a7a52` (`BorderHover`); selected `#3a3220` (`Selected`) with a
  1 px `#dc9e33` (`Accent`) ring. Text: `Value` 13 by default, `Strong` = `Label` (14 bold),
  `Faint` = `Faint` (12); a `CellStyle.Colour` overrides the colour. These are the `ListView` row
  colours, so selection and hover read the same across the kit.
- **Widths**: Fixed columns keep their exact reference-px width at every Kit scale; Auto is the
  widest header/cell text plus 2 x `TableCellPadX` and is remeasured only when a cell text changes
  (with no Fill column, Auto shares the leftover width); Fill takes the leftover by weight with the
  longest header word (minimum 40 px). Fill shrinks to its
  minimum first, then Auto shrinks proportionally; Fixed never shrinks. Widths and x positions are
  whole pixels and header/body use the same x positions. Text that does not fit gets an ellipsis.
  Headers wrap to at most two lines; the header row uses the largest line count of any column.
- **Alignment**: `CellAlign.Left`, `Centre`, `Right`; header and cells use the same alignment. Numbers
  are right-aligned by the mod (`CellAlign.Right`), never switched automatically.

## Additions in 0.1.7 (charts)
- **Tokens**:
  - grid `ChartGrid` = `#3a372e` (`Divider`), axes `ChartAxis` = `#5c5442` (`Border`), tick text
    `ChartTick` = `#8f8670` (`Faint`), axis captions `#b8ad94` (`Text2`);
  - reference line and label `ChartRef` = `#b8ad94` (`Text2`), dashed;
  - series palette without an explicit colour, in this order: Accent `#dc9e33`, Success `#8fd99c`,
    Error `#e07a4f`, Text2 `#b8ad94`;
  - bar fill `ChartBar` = `#5c5442` at .78 (`rgba(92,84,66,200)`), bar border `ChartBarBorder` =
    `#5c5442`; hover wash `ChartBarHover` = `#b8ad94` at .27; stopped bar `ChartStopped` = `#e07a4f`;
    waterfall start bar `Accent`, end bar `Text2`; legend chip `ChartChip` = `#2b2924` at .92
    (`rgba(43,41,36,235)`) with a `#3a372e` border.
- **Metrics**: chart padding **8 px**; plot grid/axis line **1 px**; series line **2 px**; point and
  hover dot **3.2 px radius** (7 px marker); reference line **1.2 px**, dash **5 px**; bar gap **4 px**;
  tick row **16 px**; axis caption row **16 px**; legend chip height **18 px**; hover line **1 px**.
- **Type**: tick labels `Faint` 12, axis captions `Small` 12, legend labels `Faint` 12; the chart title
  is the section heading. Axis captions always put the unit in parentheses once ("Angle (deg)").
- **Charts are calm**: grid `Divider`, axes `Border`, no filled plot background and no chart border of
  their own (the `CanvasView` border remains); the legend is a row of small chips with a 10 x 3 px
  colour swatch. Bars are muted; only the hovered bar gets the light wash and a stopped waterfall bar
  uses the error colour, with a small "Stopped" label in the bar plus "Stopped at 0." in the tooltip.
  Colour is never the only signal.
- **Hover never rasterises**: the crosshair, series dots and bar wash are plain uGUI images above the
  raster; the tooltip is the kit tooltip. A redraw happens only when the data, the size or the range
  changes (reference, count and a cheap value hash), or at most 10 times a second while a panel edge
  is dragged (the 0.1.4 rule).

## Additions in 0.1.8 (radial progress)
- **Tokens**: fill `Accent` = `#dc9e33`; track `RadialTrack` = `#3d3c35` (the same muted neutral as
  `OffTrack`, visible on the panel `#14150f`); centre text `Value` = `#c9bc9c`. There is no new
  accent colour: a mod passes `.Colour(...)` for states such as a completed reload (`On` `#5fe07a`).
- **Metrics**: diameter in Kit reference px (`size`, default **48**); ring thickness in reference px
  (default **12% of the diameter, rounded, minimum 3 px**); centre text **28% of the diameter**,
  clamped to **8..13 px** (`TextStyle.Value` is 13). The ring is inset by half a device pixel, like
  `Assets.Circle()`: the outer edge is anti-aliased over one device pixel.
- **Drawing route**: one procedural ring sprite (`ShapeRaster.Circle(size, thickness)`, the same
  anti-aliasing as the other kit circles) cached by **diameter and thickness in device pixels**; the
  track is a plain `Image` and the fill is an `Image` with `Type.Filled`, `FillMethod.Radial360`,
  `Origin360.Top` and `fillClockwise = true`. No `CanvasView` and no CPU raster per value; the fill
  is clipped on the GPU.
- **Behaviour**: value 0..1 clamped; NaN and infinity are 0; fills clockwise from 12 o'clock. The
  `fillAmount` is only written when the change is at least **1/720** (half a degree), so a 4 s fill is
  smooth without a per-frame limit. The centre text is only set when it changes. Equal value and
  equal text allocate 0 bytes per poll. Usable in a panel, in `HudPanel.Body` and in a `Row`;
  `.Tooltip(...)` comes from `Element`.


## Node graph (0.1.9)

All sizes are graph/Kit units unless explicitly screen pixels. The world transform scales all node
text, chips, circles and wire widths together; only hit tolerances and the drag threshold are screen
pixels. Canvas fill and border use CanvasBg and Divider. The graph is free-form; Snap does not draw
a grid. Fixed side columns remain normal Kit containers.

| Token / item | Value |
| --- | --- |
| Node width | 176 default, minimum 100; immutable per node |
| Node/header surfaces | HeadFoot / RowBg |
| Node border | Border; Selected uses Accent |
| Node radius | 5 |
| Header | 30 high; 48 x 20 chip at (7,5), radius 4; title at x=62, Kit Label, ellipsis |
| Ports | rows 24 high, body top padding 6; first centre y=48 |
| Port circles | diameter 8, centred on left/right node edge; labels Kit Small, ellipsis |
| Port states | GraphWireOff / Accent when On / AccentText when Chosen |
| Footer | 30 high; lamp diameter 6 at x=10; text x=23, Kit Small |
| Footer button | 54 x 22, right inset 6, bottom inset 4; Kit Btn/Border, radius 4 |
| Node height | 30 + 6 + 24 * max(1, input count, output count) + 30 |
| Missing-part opacity | 0.38 (Dimmed) |
| GraphColours.In | #79d8e8 |
| GraphColours.Logic | #c9bc9c (Value) |
| GraphColours.Out | #f3cf86 (AccentText) |
| GraphColours.WireOn | #dc9e33 (Accent) |
| GraphColours.WireOff / GraphWireOff | #6b6250 |
| Wire selected | AccentText; custom Colour applies otherwise |
| Wire width | 2 default; positive custom Width in graph units |
| Wire soft fringe | 1 unit per side, sampled from cached 1 x 16 alpha texture |
| Bezier | horizontal handles max(40, abs(dx)/2); 16-32 segments based on length |
| Pending wire | Text, width 2; alternating visible/collapsed strip segments |
| Hint overlay | inset 12; height 30, Selected/AccentLine, Kit Small centred |
| Hit tolerances | ports radius 7 screen px; wires 6 screen px; drag threshold >4 screen px |
| Camera | zoom 0.5-1.5 default; wheel factor 1.15 per notch; snap opt-in |

Nodes use BoxView, Draw.Text and Assets.Circle pools. The mesh is the first world child, so wires
stay below nodes; selection and dragging raise the node. Live signal/selection colours never change
wire width or layout. No EventSystem-driven controls or custom Graphic subclass are introduced.

## Keyboard focus and port drag (0.1.11)

| Token / item | Value |
| --- | --- |
| FocusHeader | #242219; normal unfocused header remains HeadFoot #1b1c15 |
| FocusTitle | #fff0cc |
| UnfocusedTitle | #b2a78d |
| Focused header bottom line | AccentLine #a57a2c, existing 1-unit line |
| Unfocused header bottom line | Frame #4a4536 |
| Port drag wire | AccentText #f3cf86, width 2 graph units, dashed strip |
| Port drag start | movement strictly greater than 4 physical px; threshold stays latched |
| PortHitRadius | 7 physical px default; configurable finite value >=0 |
| Graph picking | each block top-to-bottom: footer, ports, body; then wires, canvas |

HUDs, markers and tooltips never use keyboard-focus styling. Focus does not change panel size,
layout or pointer/Esc rules. Tabs keep the existing condensed heading and 2-unit underline;
renaming changes only text and measured hit widths, count changes rebuild the strip.
