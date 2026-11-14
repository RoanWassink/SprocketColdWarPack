# Sprocket JSON Editor

A reusable in-game editor window for lists of JSON entries. Other mods plug in their own data (fields, ranges, file paths, saving) and the editor shows it with add/duplicate controls, validation and a Save button.

This is a shared library with no standalone button or screen. Expanded Ballistics requires it for its older editor windows (shell editor, combat report and material editor, which fall back to it when their `Use UI Kit` option is off).

- Version: 0.1.5
- Plugin GUID: `sprocket.jsoneditor`
- Requires: no other plugin (BepInEx 6 IL2CPP loader only)
- Optional: none
- Game version tested: 0.2.56.2

## Features

- Expandable entries with text and numeric inputs, dropdowns defined by the consuming mod, and optional grouped sections per entry.
- Add and duplicate controls based on templates that the consuming mod supplies.
- Field-level validation feedback; Save is only called once visible errors are resolved.
- Numeric ranges (minimum, maximum, step) for bounded fields and sliders.
- A read-only mode for list viewers.
- Blocks gameplay input while the editor is open.
- `JsonFileTransaction` writes a group of text files with conflict checks, backups and rollback.
- Public API for modders: subclass `EditorSession`, then call `JsonEditor.Open(session)` on the main thread. The consuming mod owns validation, writing files and refreshing the game.

## Settings and keys

- No config file and no keys of its own. The plugin creates no settings entries, uses no Harmony patches and stores nothing.
- Plugin folder: `BepInEx\plugins\SprocketJsonEditor\`. It ships no parts, technology or localization data.

## Status

- No separate test record exists for this library; it was used in game through Expanded Ballistics' editors.
- The whole pack was renamed and has not been re-tested in game yet.
- Expanded Ballistics now prefers its UI Kit windows, so this editor mainly remains as the fallback.

## Building

.NET project in this folder (`SprocketJsonEditor.csproj`, net6.0, shared settings in `SprocketMod.props`). It builds against the game's interop assemblies: `dotnet build SprocketJsonEditor.csproj -c Release -p:GameDir=<path to Sprocket folder>`. There is no test project.

Docs: [GUIDE.md](GUIDE.md) (usage and API notes), [BUILD.md](BUILD.md), [RELEASE-NOTES.md](RELEASE-NOTES.md). Licence: MIT ([LICENSE](LICENSE)). Author: Nero.
