# Sprocket UI Kit

A shared panel library for Sprocket mods. Mods describe their panel content, and the kit draws it in one common style and handles clicks, typing, dragging, focus and game-input blocking.

This is a shared library with no gameplay of its own. Other mods in this pack use it for their windows: Telescopic Mast, AI Orders, Expedition, Reference Images and Expanded Ballistics (hard dependency), and Carousel Autoloader (optional).

- Version: 0.1.12
- Plugin GUID: `sprocket.uikit`
- Requires: Sprocket Keybinds (`sprocket.keybinds`, `>=0.1.3 <0.2.0`)
- Optional: none
- Game version tested: 0.2.56.2

## Features

- Panels with columns, sections, lists, tables, tabs, dropdowns, sliders, steppers, toggles, text fields, tooltips, confirm dialogs and field errors.
- Charts: line chart, bar chart and waterfall, plus a radial progress ring.
- A canvas for custom drawing, an editable node graph (used for the flow editor of Telescopic Mast), HUD overlays and world markers.
- Panels can be dragged, resized and maximized, and the kit remembers their position and size.
- Keyboard focus belongs to one panel at a time. Shortcuts and Enter only go to the focused panel, and the game keys are suspended while a panel has focus or you type in a field.
- Esc closes the open panel instead of opening the pause menu; clicks on a panel do not pass through to the designer or the game.
- Callbacks run inside a guard, so an error in one mod's panel is logged and does not take the kit down.
- Public API with a demo panel for modders. See [GUIDE.md](GUIDE.md) for the API reference and examples.

## Settings and keys

- Config file: `BepInEx\config\sprocket.uikit.cfg`. Entries and defaults: `Display.Scale` 1.0 (multiplies the automatic screen-height/1080 scale), `Display.FontSource` Default, `Input.BlockGameplayInput` true, `Input.BlockKeysWhilePanelFocused` true, `Input.SuspendGameKeysWhileTyping` true, `Input.SkipBlockTargets` empty, `Diagnostics.DetailedLogging` true (verbose log lines in this development build).
- Panel positions and sizes are stored in `BepInEx\config\sprocket.uikit.panels.cfg`. Delete a line there to re-centre that panel.
- Key: "UI Kit / Demo" opens the demo panel. It is unbound by default; bind it in Settings > Controls > Mod keybinds.
- Plugin folder: `BepInEx\plugins\SprocketUiKit\`. It ships no parts, technology or localization data.

## Status

- Tested in game: the first versions (0.1.0 and 0.1.1): look, clicks, lists, typing, wheel, dragging, input blocking and the Esc fix. AI Orders' map panel also worked on the kit (0.1.2).
- Not yet tested in game as a whole: 0.1.4 to 0.1.12 (maximize, resize, tables, charts, radial progress, node graph, focus-only keyboard routing).
- The whole pack was renamed and has not been re-tested in game yet.

## Building

.NET project in this folder (`SprocketUiKit.csproj`, net6.0). It builds against the game's interop assemblies and the Keybinds DLL: `dotnet build -c Release -p:GameDir=<path to Sprocket folder>`. Pure tests: `dotnet run --project tests/UiKit.Tests.csproj -c Release`. An example plugin is in `examples/`.

Docs: [GUIDE.md](GUIDE.md) (API reference), [docs/STYLE.md](docs/STYLE.md) (style tokens), [TESTPLAN.md](TESTPLAN.md), [CHANGELOG.md](CHANGELOG.md). Licence: MIT (see the repository LICENSE). Author: Nero.
