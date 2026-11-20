# Sprocket JSON Editor

JSON Editor is an in-game window for editing lists of entries. Other mods supply the data, and the editor shows it with controls to add and copy entries, checks on what you type and a Save button.

It is a shared library with no button or screen of its own. Expanded Ballistics needs it for its older editor windows (the shell editor, the combat report and the material editor), which use it when their `Use UI Kit` option is off. Install it with the pack.

Version 0.1.5. It needs no other mod.

## What it adds

- Entries that open and close, with text and number fields, dropdowns and optional groups of fields.
- Add and duplicate buttons, based on templates that the mod supplies.
- A message beside a field when its value is not valid. Save only goes ahead once all visible errors are fixed.
- Minimum, maximum and step for number fields, shown as sliders where that fits.
- A read-only mode for lists that you only look at.
- Game input is blocked while the editor is open.

## How to use it

You do not open this editor yourself. It appears when a mod opens one of its lists.

1. In Expanded Ballistics, set `Use UI Kit` to off for the window you want, for example the shell editor.
2. Open that window in the game. The list appears in the JSON Editor.
<!-- SCREENSHOT: jsoneditor-list.png (a list of entries in the JSON Editor, for example the shell editor) -->
3. Click an entry to open it, change the fields, or add and duplicate entries. Fix any field that shows an error.
4. Click Save.

## Settings

The editor has no settings file and no keys. It creates no entries in the settings and stores nothing. The plugin folder is `BepInEx\plugins\SprocketJsonEditor\`.

## For mod makers

A mod subclasses `EditorSession` and calls `JsonEditor.Open(session)` on the main thread. The mod keeps control of validation, writing files and refreshing the game. `JsonFileTransaction` writes a group of text files with conflict checks, backups and rollback. Usage and API notes are in [GUIDE.md](GUIDE.md).

## Known limits

- No separate test record exists for this library. It was used in the game through the editors of Expanded Ballistics.
- The pack was renamed recently and has not been tested in the game again since.
- Expanded Ballistics now prefers its UI Kit windows, so this editor mostly remains as the fallback.
- The mod was written for game version 0.2.56.2.

## Building

The project is a .NET project in this folder (`SprocketJsonEditor.csproj`, net6.0, shared settings in `SprocketMod.props`). It builds against the game's interop assemblies: `dotnet build SprocketJsonEditor.csproj -c Release -p:GameDir=<path to Sprocket folder>`. There is no test project.

More notes: [GUIDE.md](GUIDE.md), [BUILD.md](BUILD.md) and [RELEASE-NOTES.md](RELEASE-NOTES.md). Plugin ID: `sprocket.jsoneditor`. Licence: MIT ([LICENSE](LICENSE)). Author: Nero.
