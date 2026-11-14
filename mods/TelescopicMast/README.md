# Sprocket Telescopic Mast

Adds a retractable mast, hinge parts and a rotating turntable to the Sprocket designer, plus an in-game I/O Flow editor (node graph) that wires signals such as cannon fired, braking or a key press to those parts.

It also offers an I/O API that other mods use to put their own parts into the same flow: Visual Effects (lamps), Emitter and Cupolas (icon) do.

- Version: 0.9.9
- Plugin GUID: `sprocket.telescopicmast`
- Requires: Sprocket Keybinds (`sprocket.keybinds`, `>=0.1.3 <0.2.0`), Sprocket UI Kit (`sprocket.uikit`, `>=0.1.12 <0.2.0`)
- Optional: Sprocket Visual Effects, Sprocket Emitter, Sprocket Cupolas (they soft-depend on this mod and use its I/O API)
- Game version tested: 0.2.56.2

## Features

- Telescopic Sight Mast, Telescope Base and Telescope Part: hold a key to extend or retract. Stroke is 1 m at height scale 1 and follows the scale. Available from vehicle date 1967.01.01.
- Hinge Base, Hinge Leaf and Hinge Mount: a leaf swings about a pin, and parts built on it swing along. Angles and speed are set per hinge. Available at all dates.
- Rotator Base and Rotator Plate: a turntable for radars and similar parts, driven from the flow (run, reverse, fast, home), with slow/fast speed, direction and target angle.
- I/O Flow panel on the UI Kit with inputs (cannon fired/reloading/reloaded/loaded, hinge/mast state, vehicle braking, keys), logic (AND, OR, NOT, delay, pulse, counter, memory) and outputs. Wires can be dragged, with undo and redo.
- Flow templates: shell flap, brake light, radar spin, mast raise after firing.
- Public I/O API (`ApiVersion` 1, revision 2) with a copyable client shim (`api/IoApiClient.cs`): inputs, outputs, part icons and per-part settings shown in the flow inspector.
- Flows and hinge settings are stored in the blueprint together with the parts.

## Settings and keys

- Config file: `BepInEx\config\sprocket.telescopicmast.cfg`. Defaults: `General.Enabled` true, `Mechanism.StrokeMetres` 1.0 (0.05-1), `Mechanism.SpeedMetresPerSecond` 0.2 (0.01-1), `Diagnostics.DetailedLogging` true (verbose log lines in this development build), `Diagnostics.PassiveAtStartup` false. `Keys.Raise` and `Keys.Lower` are only read once to migrate old bindings.
- Keys (rebind in Settings > Controls > Mod keybinds): Mast / Raise Insert, Mast / Lower Delete, I/O Flow / Open panel F8, I/O Flow / Action 1 to 4 Numpad 1 to 4. F8 is also the factory key of some other mod actions.
- Panel position is stored by the UI Kit in `sprocket.uikit.panels.cfg`.
- Shipping data: eight part definitions and their English localization under `Sprocket_Data\StreamingAssets`, and part icons in `BepInEx\plugins\SprocketTelescopicMast\assets`.

## Status

- Tested in game earlier: the mast and its mass fix (0.3.7), the hinge with the I/O panel and fire, delay, pulse (0.4.1), and the Hinge Mount as a shell-ejector flap (0.5.0). Per the Emitter status, the lamp coupling and braking input (0.9.8) and part settings (0.9.9, seen through the Emitter) also worked in game.
- Not yet tested in game: the node-graph flow editor and its undo, the rotator, the smooth meshes and icons, and saving and reloading part settings (0.8.0 to 0.9.9).
- The whole pack was renamed and has not been re-tested in game yet.

## Building

.NET project in this folder (`SprocketTelescopicMast.csproj`, net6.0). It builds against the game's interop assemblies and the Keybinds and UI Kit DLLs: `dotnet build -c Release -p:GameDir=<path to Sprocket folder>`. Checks: `Verify.ps1` and `tools/Run-Gate.ps1`; unit tests are in `tests/`.

Docs: [GUIDE.md](GUIDE.md) (full description, hinge and I/O panel), [IO-API.md](IO-API.md) (API contract, mostly Dutch), [CONFIGURATION.md](CONFIGURATION.md) (partly Dutch), [CHANGELOG.md](CHANGELOG.md), [TESTPLAN.md](TESTPLAN.md) (Dutch), [HOOK-REVIEW.md](HOOK-REVIEW.md). Licence: MIT (see the repository LICENSE). Author: Nero.
