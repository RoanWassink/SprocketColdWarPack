# Sprocket Keybinds

Keybinds adds a menu for the keys of all mods to the game's own Settings screen. There you set a key for every action that a mod offers, including combinations with Shift, Ctrl and Alt.

It is a shared library. Thermal Sight, Hydropneumatic, Smoke Launchers, Telescopic Mast and AI Orders need it, and UI Kit needs it too. Expanded Ballistics and the Cold War Expansion Pack can use it as well, but work without it. Install it with the pack.

Version 0.1.10. It needs no other mod.

## What it adds

- A "Mod keybinds" panel in Settings, under Controls.
- Actions grouped by author or by mod pack (a display choice only).
- A primary and an optional second slot for each action. Keyboard keys and mouse buttons both work.
- Combinations with Shift, Ctrl and Alt on either side. A modifier can also be bound on its own. When a combination and a plain key share the same main key, the combination wins.
- Buttons to clear a slot, reset it to its default, apply, accept and cancel.
- Examples of keys that are still free, based on the game's own keys and the registered mod actions. If the game's keys cannot be read, the panel says so and shows no examples.
- A "Mod settings" row next to it, which opens a shared settings panel when UI Kit is installed. Without UI Kit, the row reads "Needs UI Kit" and cannot be clicked.

## How to use it

1. Start the game and open Settings. Go to Controls.
2. Find "Mod keybinds" and click Open.
<!-- SCREENSHOT: keybinds-settings-row.png (the Controls screen with the Mod keybinds row) -->
3. Pick an action, click a slot and press the key or combination that you want. The mods in the pack set no keys, so every action starts empty.
<!-- SCREENSHOT: keybinds-panel.png (the Mod keybinds panel with a few actions and bindings) -->
4. Click Apply to save and stay, Accept to save and close, or Cancel to go back to the last applied keys.
5. For the shared settings panel, go to Settings, Controls, Mod settings and click Open. The panel appears after you leave Settings. The action "Open mod settings" starts unbound, so choose a key for it in the keybind panel if you want a direct way in.
<!-- SCREENSHOT: keybinds-mod-settings-row.png (the Mod settings row in Controls) -->

Your keys are stored per mod and per action. The settings of a mod that is not installed at the moment stay saved until it returns.

## Settings

- `sprocket.keybinds.cfg` has one entry: `Diagnostics.DetailedLogging` (default false), which writes extra lines to the log.
- Your bindings are in `BepInEx\config\sprocket.keybinds.json`, and a copy with the ending `.bak` is kept. Keep both files when you update.

## For mod makers

Mods register their actions and settings once, and Keybinds does the rest. The entry points are `Keybinds.RegisterButton`, `RegisterModMetadata`, `RegisterPackMembership`, `TryMigrateBindingIdentity` and the `ModSettings` registry. Keybinds and UI Kit are described together in the [framework guide](../../docs/FRAMEWORK.md), and the details are in [INTEGRATION.md](INTEGRATION.md) and [API.md](API.md).

## Known limits

- Version 0.1.10 is built and checked with automatic tests. Its new Mod settings row and the link to UI Kit still need the checklist in the game (see NOTES.md).
- The previous version, 0.1.9, loaded in the game (session of 1 November 2026) without warnings or errors.
- Not yet tested in the game: the panel layout and scaling at other screen sizes, Shift plus a key against the same key alone, the Clear, Reset, Apply and Cancel buttons, and keeping the bindings across a restart.
- The pack was renamed recently and has not been tested in the game again since.
- The mod was written for game version 0.2.56.2.

## Building

The project is a .NET project in this folder (`SprocketKeybinds.csproj`, net6.0). It builds against the game's interop assemblies: `dotnet build -c Release -p:GameDir=<path to Sprocket folder>`. Tests run with `dotnet run --project tests/Keybinds.Tests.csproj -c Release`. The plugin folder is `BepInEx\plugins\SprocketKeybinds\`.

More notes: [GUIDE.md](GUIDE.md), [INTEGRATION.md](INTEGRATION.md), [BUILD.md](BUILD.md), [REFERENCE-GUIDE.md](REFERENCE-GUIDE.md), [NOTES.md](NOTES.md), [CHANGELOG.md](CHANGELOG.md) and [RELEASE-NOTES.md](RELEASE-NOTES.md). Plugin ID: `sprocket.keybinds`. Licence: MIT ([LICENSE](LICENSE)). Author: Nero.
