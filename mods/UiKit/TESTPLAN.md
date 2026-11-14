# Test plan: Sprocket UI Kit 0.1.12 (pending in-game validation)

Coordinator installs separately; this build did not install, launch or stop the game.

1. Open Demo > Window, click a non-text part of the demo and press Ctrl+Shift+D: the counter
   increases, HasFocus is true and FocusedPanel identifies the demo.
2. Click in the designer, press Ctrl+Shift+D: the counter stays unchanged, HasFocus is false,
   FocusedPanel is null and the designer responds to its configured action on that chord.
3. In the mast, Ctrl+Z with flow-panel focus affects only the flow; click the designer outside
   all Kit panels and Ctrl+Z affects only the tank.
4. Press Enter in the designer: no Kit default action or modal opens/confirms. With A focused
   below B, Enter uses A's default/modal; with a popup open neither shortcut nor Enter fires.
5. Recheck focus on open/inside/outside press, close handoff and native pause; HUDs stay unfocused.
   Regress Esc, text entry, key/mouse blocking and mod toggle keys as in the 0.1.11 plan below.

# Test plan: Sprocket UI Kit 0.1.11 (pending in-game validation)

Coordinator installs separately with the game closed. This build did not install, launch or stop it.

1. In the designer, focus the composer or demo by opening it or pressing title/content/border.
   Press/hold WASD, Delete and Ctrl+Z: the tank stays unchanged; panel shortcuts still run.
   Open a second panel: only its title is accented; click the first to switch focus.
2. Click the tank outside all Kit panels: title dims and designer keys work again. HUDs, markers
   and tooltips never focus. Close the focused panel: the top remaining normal panel takes focus.
3. Use the mast's F8 toggle with panel focus: still opens/closes. Type into a field: text entry
   and existing mod-key suspension work; blur back to panel: game keys remain blocked.
4. Alt-tab, return, open native pause (including StayOpenWhilePaused) and close panels: keys
   restore and remain restored until a fresh panel open/press. Change scene and unload as well.
   With DetailedLogging=true (default is false), check counts/reasons and no orphaned suspended actions in Window.
5. Set BlockKeysWhilePanelFocused=false: panel-only suspension ends. Check both values of
   SuspendGameKeysWhileTyping independently; restore defaults. Re-enable game actions while
   focused: rescans disable them within one second, without per-frame resource searches.
6. Demo > Graph: drag output to input and input to output, including scale 1.333 and zoom changes.
   Dashed drag wire appears only after >4 physical px; blocks/view stay fixed (also on wheel).
   Short clicks still connect in two clicks. Drop on empty/body/footer/wire and outside; no stuck
   wire. Mast pattern retains its own pending wire during a drag; close/switch tabs to cancel.
7. Stack blocks: higher bodies beat covered lower ports/footers, higher ports still work at their
   protruding edge. Check default radius 7 and PortHitRadius(12), ViewSize and VisibleRect after
   pan/zoom/resize. Regress Stress, clipping and the UInt16 cap with both temporary wires active.
8. Demo > Window: rename selected tab, add/remove tabs, switch and resize; labels and hit widths
   update. Regress existing controls, text, Esc, mouse blocking and old consumers unchanged.

# Test plan: Sprocket UI Kit 0.1.10 (pending in-game validation)

Coordinator installs separately with the game closed. This build did not install, launch or stop it.

1. Demo > Graph > Mast pattern: move the mouse for 30 seconds while the pending dashed wire and
   three regular wires restart at 10 Hz with fresh ids. Pan, zoom and drag blocks: no fans or trails.
   With DetailedLogging=true, check UInt16 and coherent slot/vertex/index counts, at most one
   rebuild line per second unless slot count changes; world renderer count includes node graphics.
2. Maximise at 1440p or Kit scale 1.33; pan blocks and wires to the right edge, then check scale
   1 and 0.75. Curves reach the viewport boundary and clip there, including a graph in a ScrollView.
3. Check IN, LOGIC and the long tag in Mast pattern: centred inside the chip, chip grows to its
   limit, title begins after it and ellipsises. Resize and zoom; no stray tag below the header.
4. Stress (60/100): select wires, connect ports, toggle signals and compare frame ms. Shared is
   the production default. Compare Canvas remains experimental: check isolated clipping separately
   before interpreting timings. Profile native uploads/GC if available; pure timings exclude Unity.
5. Regress Table/Chart/List/ScrollView/dropdown/slider/radial/HUD, typing, Esc, resize and existing
   consumers without recompilation. Retain screenshots/logs; visual and native GC acceptance is pending.

# Test plan: Sprocket UI Kit 0.1.9 (pending in-game validation)

Coordinator installs separately with the game closed. This build did not install, launch or stop it.

1. Open UI Kit / Demo > Graph in designer and gameplay: Brake pedal, AND, Delay, Hinge open,
   coloured chips, port labels, footer lamp and Hold/Release; long titles ellipsise; resize/maximise
   and change Kit scale at 1080p/1440p; all nodes/wires/hints clip inside the canvas and enclosing views.
2. Drag header and body with left, including outside the panel, and release: position snaps at 8,
   dragged/selected node is above peers, live move callbacks end with final=true; clicks under 4
   physical px work. Footer/ports never drag nodes. Close, change tabs, pause or rebuild during capture.
3. Pan empty canvas with left, anywhere with middle; wheel around cursor at min/max zoom, then Fit.
   Check capture outside the panel, no game click-through/fire, and panel resize/title dragging.
4. Click an output then input: chosen circle, dashed pending wire and Now click an input port...
   hint appear; cancel by canvas click; click a curve to select it. Check 7 px port / 6 px wire hits
   and priority footer > port > body > wire > canvas; Hold/Release drives the example's output lamp.
5. Stress: verify 60 blocks/100 wires at 10 Hz, pan/drag while maximised at 1440p. Compare Canvas
   shared/isolated with the same camera/scene for 10 seconds each, record average frame ms and native
   Canvas.BuildBatch/GC with a profiler if available. Confirm On-only changes touch colours, with no
   geometry/layout uploads. Choose the measured Canvas mode before calling this performance accepted.
6. Regression: unchanged Table/Chart/List/ScrollView/dropdown/slider/radial/HUD, typing, Esc, panel
   resizing and existing consumers without recompilation. Check zero graph-managed bytes on an idle
   frame (excluding mod getters/demo text); retain logs and report visual/input/native GC results.

# Test plan: Sprocket UI Kit 0.1.8 (in game, after installing)

Mast 0.6, AI Orders 0.5, Expedition 0.3 and Ballistics 0.22 must load and work unchanged. Log: `[UI Kit] v0.1.8 ready`.

1. **Load and HUD reload**: open the demo, tab "HUD", switch on the demo HUD: three rings of 56, 44
   and 32 px are visible; the 56 px ring fills clockwise from 12 o'clock in 4 s, shows the seconds
   left in the centre changing each 0.1 s ("3.1"), turns green when full, then repeats. The 44/32 px
   rings show fixed 65% and 35% with a muted track.
2. **Panel Row**: open Components > Progress: the 56 px ring sits in a Row next to "Reload progress";
   move the Value stepper: the ring follows the same 0-100% immediately, the tooltip appears on hover,
   and the circle stays round at 48/56 px and in a Row with a long label.
3. **Scale and resize**: repeat at 1440p and `Display.Scale` 1.25, then resize and maximize the demo:
   the ring stays sharp (no blocky edge) and round; the HUD ring scales with the HUD ScaleFactor.
4. **Regression**: the Chart tab, Table, ListView, ScrollView, dropdown and the 0.1.5 bar behave as
   before; open Ballistics and the other consumers without recompiling; Esc, typing, resize, maximize
   and the input block are unchanged; the pure tests show 0 bytes over 1000 equal radial polls.

# Test plan: Sprocket UI Kit 0.1.7 (in game, after installing)

Mast 0.6, AI Orders 0.5, Expedition 0.3 and Ballistics 0.22 must load and work unchanged. Log: `[UI Kit] v0.1.7 ready`.

1. **Load and Chart tab**: open the demo, tab "Chart": the angle chart, the Step chart and the
   waterfall are visible with their captions ("Angle (deg)", "Protection (mm RHA)", "Depth (mm)",
   "Remaining (mm)") and the KE/HEAT legend chips; "Change data" and "Stop at 0" follow every click.
2. **Angle chart**: the KE and HEAT lines rise as 120 / cos(angle) (scaled per threat), the dashed
   "RHA 200" line is visible and the automatic y range includes it. Hover the plot: the vertical line,
   a dot on each series and the tooltip with both values move with the mouse without the chart
   flickering or redrawing (no visible lag).
3. **Axes and resize**: resize the panel and maximize it: the charts fill the column, tick labels and
   captions stay readable, and values keep matching the axes. Repeat at 1440p or `Display.Scale` 1.25:
   the image is sharp. An empty series shows the `.Empty` text, not empty axes.
4. **Step chart**: the remaining protection is a staircase (horizontal to the next depth, then
   vertical) over four layers; hover shows the depth and the remaining mm.
5. **Waterfall**: the first bar runs from 0 to 400, the layer bars float from the old to the new
   remainder, and "Left" ends at 70. Click "Stop at 0": the third layer is clamped to 0, its tooltip
   says "Stopped at 0.", it uses the error colour and the fourth layer is not drawn.
6. **Regression**: the Table tab, ListView, ScrollView, dropdown and the 0.1.5 bar behave as before;
   the wheel over a chart inside a ScrollView scrolls the ScrollView; open Ballistics and the other
   consumers without recompiling; Esc, typing, resize and maximize unchanged.

# Test plan: Sprocket UI Kit 0.1.6 (in game, after installing)

Mast 0.6, AI Orders 0.5, Expedition 0.3 and Ballistics 0.22 must load and work unchanged. Log: `[UI Kit] v0.1.6 ready`.

1. **Load and Table tab**: open the demo, tab "Table": the protection table, the 60-row table and the
   table in the ScrollView are visible; the line "Resize the panel: columns stay aligned." is shown.
2. **Protection columns**: the values sit right under their right-aligned headers; "Factor on plate
   thickness" wraps to two lines and the header row is equally high for all columns; hover the header
   shows its tooltip; one HEAT cell is amber, one KE cell is bold. Hover a cell that is too narrow for
   its text: the full text appears as a tooltip.
3. **Resize and scale**: resize the demo narrower/wider and maximize it: columns keep their alignment;
   Fixed columns scale with the Kit. Repeat at 1440p (or `Display.Scale` 1.25) and on another screen
   size; numbers stay under their headers at every width.
4. **60 rows**: wheel, drag the bar and click the track: rows scroll under a fixed header; only visible
   rows respond; zebra alternates; clicking a row selects it (and not the row under a bar click).
5. **Table in ScrollView**: wheel over the table: the outer ScrollView scrolls and no inner bar appears;
   the table header scrolls with the table, as in 0.1.5.
6. **Regression**: ListView, ScrollView, dropdown and the 0.1.5 bar behave as before; open Ballistics
   (Cassette Composer) and the other consumers without recompiling; Esc, typing, resize and maximize
   unchanged.

# Test plan: Sprocket UI Kit 0.1.5 (in game, after installing)

Mast 0.6, AI Orders 0.5, Expedition 0.3 and Ballistics 0.22 must load and work unchanged. Log: `[UI Kit] v0.1.5 ready`.

1. **Load and demo**: open the demo, tab "Scroll": an overflowing `ScrollView` (right column), an
   overflowing `ListView` and a 24-option dropdown are visible; the wheel, clicks and Esc still work.
2. **ScrollView drag**: drag the thumb: the content follows the mouse and the grab point stays under the
   cursor; drag outside the bar and outside the panel, then release there: no click reaches the game.
   Hover the 10 px strip: the thumb grows to 6 px and a subtle track appears.
3. **Track and ListView**: click above/below the thumb: one viewport; hold: repeats after a short pause
   until the thumb is under the cursor. In the `ListView`, a bar click selects no row and presses no bin.
4. **Dropdown**: open the 24-option list; drag its thumb and page its track: the list stays open and no
   option is chosen; a normal option click still selects and closes. Wheel over the bar still scrolls.
5. **Cassette Composer** (Ballistics 0.22.19): open the composer, maximize it, use the right column
   `ScrollView().Fill()`: drag and page its bar; the resize grip still works from the 4 px band outside
   the panel's right edge (inside the panel the bar wins its strip).
6. **Regression**: nested `ScrollView`s: the inner one under the cursor wins the wheel; 0.1.4 maximize,
   resize, panels, dropdown and wheel behaviour unchanged; no horizontal bar appears anywhere.

# Test plan: Sprocket UI Kit 0.1.4 (in game, after installing)

Mast 0.6, AI Orders 0.5, Expedition 0.3 and Ballistics 0.22 must load and work unchanged. Log: `[UI Kit] v0.1.4 ready`.

1. **Maximize**: open the demo; click the square left of the close button: the panel fills the screen with
   a small margin and the icon becomes two squares; click again: same size and place as before.
2. **Double-click** an empty spot of the title bar: maximizes; double-click again: restores. A single
   click and a drag still just move the panel.
3. **Drag from maximized**: maximize, then drag the title bar: it restores at its old size and the cursor
   stays at the same relative spot in the title bar.
4. **Resize**: drag the right edge, the bottom edge and the corner grip (grip turns amber on hover): it
   does not go below 1000 x 560 (Window tab, MinSize) or past the screen edge; the left/top edge do nothing.
   The I/O tab sidebars keep their width; the middle column and the canvas tab grow.
5. **Canvas while resizing**: Canvas tab, drag the corner: the map stretches and redraws a few times a
   second, sharp again on release; no stutter.
6. **Remember**: resize the demo, maximize the second panel, close both, restart the game: same size and
   maximized state. `Window` tab: "Reset position" centres it at 1240 x 640 and removes its line.
7. **Off screen** (#23): drag the demo half off the right edge, close, reopen: it is fully on screen again.
8. **Not maximizable / close**: "Open a panel with Maximizable = false": no square button, double-click
   does nothing, edges still resize. "RequestClose()" closes the demo; with "Refuse close requests" on
   it stays open (also for Esc and the close button).
9. **Slider tab**: type in the live field: the keystroke counter rises per key and the red message shows
   while typing; the HideValue slider has no value box.
10. **Regression**: the Resized counter rises while dragging; designer: no click-through on the grip
    band just outside the right/bottom edge; Esc, typing, dropdowns and two panels as in 0.1.2.

# Test plan: Sprocket UI Kit 0.1.2 (in game, after installing)

Start Sprocket; afterwards Opus reads `BepInEx\LogOutput.log` (lines with `[UI Kit]`). Mast 0.6 and
AI Orders 0.3 must still load and work unchanged (they were built against 0.1.1).

1. **Load and key**: log shows `v0.1.2 ready`, `input block 9/9`, "factory default: unbound". The demo
   has no key yet: Settings > keybinds > "UI Kit / Demo", bind a free key, open it in the designer.
2. **HUD** tab: switch on the demo HUD, try all four corners and the background slider. The HUD shows a
   lamp that turns amber/red with the value, a chip, progress and an outlined timer; clicks pass through
   it to the designer; Esc does not touch it. In a battle (panel closed) it stays visible and fire/camera work.
3. **World marker** (HUD tab): ring and label float 1.5 m above the vehicle origin in the designer and
   disappear when you turn the camera away.
4. **Canvas** tab: middle-drag pans, wheel zooms towards the cursor, left/right click and Shift+click
   place green/red dots, the red marker moves smoothly, "Build topo texture" fills the background row by
   row. The "last redraw" figure stays low (note it, 1080p and 1440p). Diagram: tilt slider redraws,
   hover the plate for the tooltip.
5. **Slider** tab: drag, click on the track, wheel (one step), Shift+wheel (10), Shift+drag (fine);
   linked field: type 120, Enter; lower "Max armour" below the value: the slider clamps. Disabled one ignores input.
6. **Confirm** tab: Delete... → Esc cancels, Enter cancels (danger), Delete confirms; Overwrite... →
   Enter confirms. While a confirm is open the body does not react; the title bar still drags.
7. **Form** tab: empty name, velocity "abc"/50, calibre 160 each show a red border and a message under
   the field; Save stays disabled until all are fixed. Warning rows in three severities; rarity chips,
   cards and list rows show their colours.
8. **Pages** tab: Open 'Shell list' → back arrow and trail in the title bar; click a shell → Edit page
   (title shows the shell); Advanced: the switch hides the whole "Optional section". Esc goes back one
   page at a time, then closes the panel.
9. **Pause**: in a battle with the demo open, open the pause menu: the panel closes, no stuck cursor.
   (Expedition: its timeScale-0 menu no longer closes kit panels; checked when Expedition moves to the kit.)
10. **Regression**: Components/I/O/Rules tabs as in 0.1.1 (TESTPLAN 0.1.0 steps 3–9 in short): clicks,
    dropdowns, typing, drag, Esc without pause menu, two panels.

# Test plan: Sprocket UI Kit 0.1.0 (in game, after installing)

Start Sprocket; afterwards Opus reads `BepInEx\LogOutput.log` (lines with `[UI Kit]`).

1. **Load**: log shows `[UI Kit] v0.1.0 ready: input block 9/9` and no warnings; all other mods still load.
2. **Open in designer**: press F6 (Settings > Mod keybinds > "UI Kit / Demo" if F6 is taken). Panel looks like the canvas: font, rounded corners, shadow, colours; chip says DESIGNER. Log shows the font route.
3. **Components tab**: click every button (log tab records each), hover shows hover colours, disabled button does nothing but shows its tooltip; segmented, switches (knob slides), stepper (click, hold repeats, stops at 0/10), tabs, chips, progress.
4. **Dropdowns**: open/select/close; click outside closes; in the Rules tab inline dropdowns sit in the row, the source list shows group headings (Cannons, Keys, Hinges), and a dropdown near the bottom of the screen opens upwards, above the window edge.
5. **Typing**: text field: letters, Shift, digits, space, punctuation, Backspace (hold), arrows, Ctrl+A then type, Enter commits (status line), Esc reverts. While typing, designer shortcuts and F6 do nothing.
6. **Lists and scroll**: remove rows with the bin, Restore; in Rules add 8+ rules and scroll with the wheel; list lamps blink and stay lit ~0.3 s.
7. **Drag and remember**: drag the title bar, close (Esc), reopen: same place; restart the game: same place.
8. **Two panels**: "Open second panel"; click each to bring it to front; Esc closes the top one first.
9. **No click-through in designer**: click/drag on the panel over the tank: no part selected, camera does not move; outside the panel the designer works normally.
10. **In game**: open the demo in a battle: cursor free, firing/scope/camera blocked while open; close: cursor locked again and firing works. Pause menu (Esc) closes the panel without stealing the cursor.
11. **Scale**: at 1080p and 1440p (or `Display.Scale` 1.25) the panel keeps proportions, text stays sharp, clicks land on the right controls.
12. **Context change**: leave designer to the menu or start a battle with the demo open: it closes; no stuck cursor or blocked input afterwards.
