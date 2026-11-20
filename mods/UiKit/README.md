# Sprocket UI Kit

UI Kit gives the mods in the pack one common window style. It has no gameplay of its own: other mods describe what their window shows, and the kit draws it and handles clicks, typing and dragging.

It is a shared library. Telescopic Mast, AI Orders and Expanded Ballistics need it, and Carousel Autoloader can use it. UI Kit itself needs [Keybinds](../Keybinds). Install it with the pack.

Version 0.1.13.

## What it adds

- Windows with lists, tables, tabs, dropdowns, sliders, toggles, text fields, tooltips and confirmation dialogs.
- Line charts, bar charts and a ring that shows progress.
- A map-like drawing area, an editable node graph (the signal editor of Telescopic Mast uses it), on-screen overlays and markers in the world.
- Windows that you can drag, resize and maximize. The kit remembers where you left them.
- One window at a time has keyboard focus. While it has, the game's own keys pause, and so they do while you type in a field.
- Esc closes the open window instead of opening the pause menu. Clicks on a window do not reach the designer or the game behind it.
- A shared "Mod settings" window with the settings of the mods that register them.

## How to use it

You do not need to do anything with the kit itself. It opens when a mod opens a window.

1. To see the shared settings window, open the game's Settings screen, go to Controls, find Mod settings and click Open. Then leave Settings. The window appears once Settings has closed.
<!-- SCREENSHOT: uikit-mod-settings.png (the shared Mod settings window) -->
2. For a direct way in, open the mod keybinds panel in the same Controls screen and set a key for "Open mod settings". It starts unbound. The kit also has a demo window under "UI Kit / Demo", which is unbound as well.
3. Drag a window by its title bar, resize it by its edges, and maximize it with the button next to the close button.
<!-- SCREENSHOT: uikit-window.png (a window from the pack, for example the AI Orders map) -->

## Settings

The settings file is `BepInEx\config\sprocket.uikit.cfg`. Most values change live. `Display.FontSource` and `Input.SkipBlockTargets` need a restart.

| Setting | Default | What it does |
|---|---|---|
| `Display.Scale` | 1.0 | Multiplies the automatic scale (screen height divided by 1080). |
| `Display.FontSource` | Default | Where the font comes from. |
| `Input.BlockGameplayInput` | true | Keeps clicks on a window from reaching the game. |
| `Input.BlockKeysWhilePanelFocused` | true | Pauses the game's keys while a window has focus. |
| `Input.SuspendGameKeysWhileTyping` | true | Pauses the game's keys while you type in a field. |
| `Input.SkipBlockTargets` | empty | Input targets that the kit should not block. |
| `Diagnostics.DetailedLogging` | false | Writes extra lines to the log. |

Window positions and sizes are in `BepInEx\config\sprocket.uikit.panels.cfg`. Delete a line there to centre that window again.

## For mod makers

Mods describe their window content and the kit draws it. An error in one mod's window is logged and does not take the kit down. The API reference and examples are in [GUIDE.md](GUIDE.md). For keys, settings and windows together, read the Sprocket Mod Framework [getting started guide](../../docs/FRAMEWORK.md). A release is the DLL plus `SprocketUiKit.xml`.

## Known limits

- Tested in the game: the first versions (0.1.0 and 0.1.1) for look, clicks, lists, typing, the mouse wheel, dragging, input blocking and the Esc fix. The AI Orders map window also worked on the kit (0.1.2).
- Not yet tested in the game as a whole: versions 0.1.4 to 0.1.13 (maximize, resize, tables, charts, the progress ring, the node graph and keyboard focus for one window).
- The pack was renamed recently and has not been tested in the game again since.
- The mod was written for game version 0.2.56.2.

## Building

The project is a .NET project in this folder (`SprocketUiKit.csproj`, net6.0). It builds against the game's interop assemblies and the Keybinds DLL: `dotnet build -c Release -p:GameDir=<path to Sprocket folder>`. Build copies can use `-p:KeybindsDll=<path>`, and `Verify.ps1` accepts `-KeybindsDll <path>`. Tests run with `dotnet run --project tests/UiKit.Tests.csproj -c Release`. An example plugin is in `examples/`. The plugin folder is `BepInEx\plugins\SprocketUiKit\`.

More notes: [GUIDE.md](GUIDE.md), [docs/STYLE.md](docs/STYLE.md) (style values), [TESTPLAN.md](TESTPLAN.md) and [CHANGELOG.md](CHANGELOG.md). Plugin ID: `sprocket.uikit`. Licence: MIT (see the repository LICENSE). Author: Nero.
