# Sprocket Keybinds

A shared keybind menu for Sprocket mods. Mods register their actions once, and you rebind them inside the game's own Settings screen, including Shift/Ctrl/Alt combinations.

This is a shared library. Several other mods in this pack use it for their keys: Telescopic Mast, AI Orders, Hydropneumatic, Reference Images, Smoke Launchers and Thermal Sight (hard dependency), and Expanded Ballistics and the Cold War Expansion Pack (optional).

- Version: 0.1.9
- Plugin GUID: `sprocket.keybinds`
- Requires: no other plugin. It needs the Sprocket BepInEx 6 IL2CPP loader (build 6.0.0-be.788).
- Optional: none
- Game version tested: 0.2.56.2

## Features

- Adds a "Mod keybinds" panel to the vanilla Settings > Controls screen (Mod keybinds > Open).
- Lists actions grouped by author or by declared mod pack (By author / By pack). Grouping changes the display only.
- Each action has a primary and an optional secondary slot. Keyboard keys and mouse buttons are supported.
- Combo bindings with Shift, Ctrl and Alt (either side). A modifier can also be bound alone. The most specific matching combo wins over a plain key on the same main key.
- Clear unbinds a slot, Reset restores the factory default, Apply saves, Accept saves and closes, Cancel returns to the last applied bindings.
- Shows examples of free single keys, based on the vanilla bindings and the registered mod actions. If the vanilla bindings cannot be read, it says so instead of listing keys.
- Saved bindings are kept per mod and action; settings of a mod that is currently absent are kept until it returns.
- Public API for modders: `Keybinds.RegisterButton`, `RegisterModMetadata`, `RegisterPackMembership` and `TryMigrateBindingIdentity`. See [INTEGRATION.md](INTEGRATION.md).

## Settings and keys

- Keybinds has no `.cfg` file and no keys of its own. Your bindings are stored in `BepInEx\config\sprocket.keybinds.json` (a `.bak` copy is kept). Keep both when updating.
- Rebind keys of all registered mods in Settings > Controls > Mod keybinds > Open.
- Plugin folder: `BepInEx\plugins\SprocketKeybinds\`. It ships no parts, technology or localization data.

## Status

- Version 0.1.9 loaded in game (session of 2026-11-01) without warnings or errors.
- Not yet tested in game: the panel layout and scaling at other resolutions, Shift+X versus X combos, Clear/Reset/Apply/Cancel, and keeping bindings across a restart.
- The whole pack was renamed (plugin GUIDs) and has not been re-tested in game since.

## Building

.NET project in this folder (`SprocketKeybinds.csproj`, net6.0). It builds against the game's interop assemblies: `dotnet build -c Release -p:GameDir=<path to Sprocket folder>`. Tests: `dotnet run --project tests/Keybinds.Tests.csproj -c Release`.

Docs: [GUIDE.md](GUIDE.md) (install and use), [INTEGRATION.md](INTEGRATION.md) (API for other mods), [BUILD.md](BUILD.md), [REFERENCE-GUIDE.md](REFERENCE-GUIDE.md), [NOTES.md](NOTES.md), [CHANGELOG.md](CHANGELOG.md), [RELEASE-NOTES.md](RELEASE-NOTES.md). Licence: MIT ([LICENSE](LICENSE)). Author: Nero.
