# Sprocket Hydropneumatic

Adds an adjustable hydropneumatic suspension part to Sprocket. While driving you can raise, lower and pitch the tank, with optional separate front and rear control.

- Version: 0.4.4
- Requires: Sprocket Keybinds (`sprocket.keybinds`, version >=0.1.3 <0.2.0)
- Optional: none
- Plugin GUID: `sprocket.hydropneumatic`
- Game version tested: 0.2.56.2

## Features

- A standalone "Hydropneumatic" suspension part, selectable in the suspension list next to Christie, torsion bar and HVSS. It is available from 1967.01.01 (and in the last era, which makes every dated part available).
- Raise and lower the whole tank while driving, or tilt it forward and backward; a Neutral key returns the stance gradually.
- Optional independent front and rear control (Suspension settings); the same five actions then raise and lower the front and the rear separately.
- Editor settings per tank: wheel travel (default 300 mm), travel kept in reserve (30 mm), height change speed (40 mm/s), spring firmness, firmness when compressed and bounce damping, plus a "Reset HPS settings" button. New profiles start at +/-100 mm front and rear travel.
- Priced as a premium suspension: a reference suspension mass of 185 kg per roadwheel, and a hydraulic unit cost of 1.20 per kg plus 160 assembly. These are gameplay choices, not measurements of a real tank.
- Returning to the Hydropneumatic part in the same editing session restores its previous wheel layout.
- The settings are saved in the vehicle blueprint (key `sprocketHydropneumaticV1`); an imported vehicle that is too early keeps the part and its native suspension, with one warning in the log.

## Settings and keys

Config file: `BepInEx\config\sprocket.hydropneumatic.cfg`, created on first start. Entries: `General / Enabled` (default `true`) and `Diagnostics / VerboseLogging` (default `false`). The `[Keys]` section only holds legacy direct-key values that are imported once into Sprocket Keybinds.

Keys are bound in the game's Settings keybind menu (Sprocket Keybinds), under the "HPS /" labels:

| Label | Default | Normal mode | Independent mode |
|---|---|---|---|
| HPS / Raise | Up arrow | Raise whole tank | Raise front |
| HPS / Lower | Down arrow | Lower whole tank | Lower front |
| HPS / Tilt forward | Page Up | Lean forward | Raise rear |
| HPS / Tilt back | Page Down | Lean backward | Lower rear |
| HPS / Neutral | Home | Return to neutral | Return to neutral |

Shipped data: one part definition (`hydropneumaticSuspensionPart.json`), its English localization (`hydropneumaticSuspension.xml`) and an icon in `BepInEx\plugins\SprocketHydropneumatic\assets`. The mod references stock game meshes; none are bundled.

## Status

Earlier builds were tried in the game by the author. For 0.4.4 the date gate (part present from the Coldwar2 era, absent in Coldwar) is listed as not yet tested in the game, and the guide lists rebinding, key migration, reset and both control modes of the Keybinds integration as still needing in-game tests. The whole pack was just renamed and has not been re-tested in the game yet.

## Building

.NET project in this folder (`SprocketHydropneumatic.csproj`, net6.0); build with `dotnet build SprocketHydropneumatic.csproj -c Release -p:GameDir=<path to Sprocket folder>`. It also needs the Sprocket Keybinds DLL from `$(GameDir)\BepInEx\plugins\SprocketKeybinds`.

Docs: [GUIDE.md](GUIDE.md), [REFERENCE-GUIDE.md](REFERENCE-GUIDE.md), [BALANCE.md](BALANCE.md) (Dutch), [BUILD.md](BUILD.md), [RELEASE-NOTES.md](RELEASE-NOTES.md). Licence: MIT ([LICENSE](LICENSE)). Tests: `dotnet run --project tests\Hydro.Tests.csproj -c Release`.
