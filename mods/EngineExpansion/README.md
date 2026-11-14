# Sprocket Engine Expansion

Adds diesel and gas turbine engine types with their own availability dates, a smaller transmission, and a Powerpack option that fits engine and gearbox together as one block with MTU/Renk-style models.

- Version: 0.5.0
- Plugin GUID: `sprocket.engineexpansion`
- Author: Nero
- Requires: BepInEx 6 IL2CPP (no other plugin; the mod declares no `BepInDependency`)
- Optional: none
- Game version tested: 0.2.56.2

## Features

- Engine type dropdown in the engine inspector with Petrol (the native engine), Diesel and Gas turbine. Each profile scales power, RPM range, fuel use, mass and cost relative to the native engine (numbers in `Core\Profiles.cs`).
- Availability by vehicle date: petrol 1914.07.28, diesel 1935.01.01, gas turbine 1967.01.01. A too-early type is labelled and cannot be picked; a saved too-early type runs as native petrol and logs one warning. The final-era date 9999.12.31 allows everything.
- Drivetrain: transmission volume is halved at unchanged rated torque, mass and cost (`TransmissionVolumeMultiplier`), the driveshaft connection angle limit is doubled, and an out-of-range stored shift RPM is repaired.
- Powerpack checkbox on the engine: the transmission is placed against the engine automatically and the connection becomes shaftless. One native undo step; unticking restores a normal shaft. Single transmission and Twin (splitter plus two mirrored gearboxes) are supported.
- The Powerpack housing adds engine mass (10 %) and cost (15 %); both are configurable. It is available from 1965.01.01 by default (provisional).
- Junction chains between engine and gearbox are collapsed in the same undo step when the gearbox sits within 1.75 m of the engine output. On a set of 154 test blueprints this raised the number of eligible tanks from 57 to 123.
- MTU/Renk engine and gearbox models (own OpenSCAD models) plus a render-only casing over the seam. Native collision stays with 1 mm clearance.
- Turbo parts are disabled in 0.5.0. Saved turbo parts and metadata are kept (dormant), and their five part files are shipped so old designs still load.
- Safe mode: if the SHA256 of `GameAssembly.dll` differs from the audited build, no hook is installed and one warning is logged. Saving in that state can drop Engine Expansion selections.

## Settings and keys

Config file: `BepInEx\config\sprocket.engineexpansion.cfg`, created by BepInEx on first start; every entry carries its description. Restart after changing.

| Section / entry | Default |
|---|---|
| `Diagnostics` / `DetailedLogging` | true (verbose trace lines; warnings and errors are always logged) |
| `Drivetrain` / `TransmissionVolumeMultiplier`, `DriveshaftAngleMultiplier`, `RepairInvalidShiftRPM` | 0.5, 2, true |
| `Powerpack` / `AvailableFrom`, `HousingMassFraction`, `HousingCostFraction` | 1965.01.01, 0.10, 0.15 |
| `Powerpack.Layout` / `CollapseJunctions`, `MaxCollapseDistance` | true, 1.75 |
| `Powerpack.Model` / `Enabled`, `Casing`, `SizeFactor`, `LengthFactor`, `HeightFactor`, `TurboLayout`, `TransmissionVolumeFactor` | true, true, 1, 0.9, 1, Compact, 1 |

No key bindings. Shipping data: the five dormant turbo part JSON files and their localization in `data\` (they go to `Sprocket_Data\StreamingAssets\`); the models are embedded in the DLL (`Assets\*.meshbin`). The plugin folder is `BepInEx\plugins\SprocketEngineExpansion\`. Blueprints store their selection under `sprocket.engineexpansion.v1`.

## Status

Earlier builds were tested in the game: 0.2.3 (Powerpack, including Twin) worked on 2026-11-03, and 0.4.1 started without Engine Expansion warnings after the startup fix. 0.5.0 (junction collapse, casing, safe mode) was installed on 2026-11-09 but its in-game checks (Challenger, Leopard 2 Twin, save and load, undo) and the engine date checks are not yet tested in game. The renamed pack has not been re-tested.

## Building

.NET project in this folder (`SprocketEngineExpansion.csproj`, net6.0); it builds against the game's interop assemblies:
`dotnet build SprocketEngineExpansion.csproj -c Release -p:GameDir=<path to Sprocket folder>`. Checks are in `tests\` (`Checks`, `CollapseCheck`, `HookCheck`); the real-tank blueprint fixtures they can use are not part of this repository. Details, model sources and the dormant turbo files: [GUIDE.md](GUIDE.md); history: [CHANGELOG.md](CHANGELOG.md).
