# Sprocket Engine Expansion 0.5.0

Plugin GUID `sprocket.engineexpansion`, DLL `SprocketEngineExpansion.dll`, author Nero. Install by copying the DLL to
`BepInEx\plugins\SprocketEngineExpansion\` (the five dormant turbo part files in `data\` go to `Sprocket_Data\StreamingAssets\`, see below).

What it does (details in `CHANGELOG.md`):

- Engine profiles (petrol, diesel, gas turbine) with their own availability dates in the engine editor.
- Drivetrain: smaller transmission volume at unchanged torque, mass and cost; larger driveshaft angle limit; repair of out-of-range stored shift RPM.
- Powerpack option: engine and gearbox as one block (single or Twin), junction-chain collapse, housing mass and cost,
  MTU/Renk engine and Renk gearbox models and a render-only casing.
- Turbo functionality is disabled in 0.5.0. Saved turbo parts and their metadata are kept (dormant hooks `legacy-turbo-data`).
- Safe mode: if the SHA256 of `GameAssembly.dll` differs from the audited build, no hook is installed and one warning is logged.

## Configuration (`BepInEx\config\sprocket.engineexpansion.cfg`)

`Diagnostics.DetailedLogging`; `Drivetrain.TransmissionVolumeMultiplier`, `DriveshaftAngleMultiplier`, `RepairInvalidShiftRPM`;
`Powerpack.AvailableFrom`, `HousingMassFraction`, `HousingCostFraction`; `Powerpack.Layout.CollapseJunctions`, `MaxCollapseDistance`;
`Powerpack.Model.Casing`, `Enabled`, `SizeFactor`, `LengthFactor`, `HeightFactor`, `TurboLayout`, `TransmissionVolumeFactor`.
Every entry carries its description and default in the generated file.

Saved data keys (inside blueprints): `sprocket.engineexpansion.v1` (engine selection), `sprocket.engineexpansion.route.v1`
and `sprocket.engineexpansion.assembly.v1` (dormant turbo metadata).

## Build

```
dotnet build SprocketEngineExpansion.csproj -c Release -p:GameDir=<game folder or a copy holding BepInEx\core and BepInEx\interop> -o <output folder>
```

The output is `SprocketEngineExpansion.dll`; `Deterministic`, no pdb, `PathMap` set.

## Tests (run from this folder, .NET SDK 8 or 6)

1. `dotnet run --project tests\Checks.csproj -c Release` : pure rule checks (profiles, drivetrain, saved metadata, powerpack, obstruction,
   MTU model, casing). Reads `Assets\casing-lod*.meshbin` from this tree.
2. `dotnet run --project tests\CollapseCheck\CollapseCheck.csproj -c Release -- <fixtures folder>` : junction collapse, safe mode and
   the synthetic topology cases. With a folder of real tank `.blueprint` files (argument 1 or `ENGINE_EXPANSION_FIXTURES`) it also replays the
   collapse on every blueprint, requires that no layout that 0.4.1 accepted becomes refused, and checks the calibration tanks
   (Challenger 1 and 2, Puma II, Lowe 2A2 ready; Panzer IV refused as front drive). It writes `ELIGIBILITY-0.5.0.md` next to the fixtures folder.
   Without fixtures only the synthetic cases run.
3. `dotnet run --project tests\HookCheck\HookCheck.csproj -c Release -- <built dll> <game>\BepInEx\interop mtu-model\model <report.md>` : resolves every
   Harmony patch against the game's interop assemblies, audits the native targets for shared (ICF) addresses against the reviewed
   `GameAssembly.dll`, and checks that the meshes embedded in the DLL equal the STLs in `mtu-model\model\parts`. Reads the game, writes only the report.
4. `py -I -B mtu-model\model\embed_meshes.py --check` : STL to `Assets\*.meshbin` and `Native\PowerpackMeshManifest.g.cs`, exact bytes.

## Fixtures

The blueprint fixtures used by `CollapseCheck` are real tanks of the author and of other players. They are not part of this source tree. Keep them
outside and pass the folder as described above.

## Assets

`mtu-model\model\` holds the OpenSCAD sources (`mtu-lib.scad`, `mtu-part.scad`, `casing.scad`, `view-*.scad`), the canonical STLs (`parts\`),
the scripts to rebuild them (`build.ps1`, `build040.ps1`, `build_hulls.py`, `build-casing.py`, `stlnorm.py`, `stlcheck.py`, `embed_meshes.py`, `render*.py`,
`preview-casing-fit.py`) and `MODEL.md` / `CASING-0.5.0.md`. OpenSCAD 2021.01 (`C:\Program Files\OpenSCAD\openscad.com`) is needed to rebuild.
Re-exporting the casing changes its STL hash; see `CASING-0.5.0.md` before running `build-casing.py`.

## Dormant turbo part files (`data\`)

`data\Parts\sprocketTurbo*Part.json` and `data\Localization\en-UK\Parts\sprocketTurbo*.xml` define the five turbo parts of the earliest releases
(Housing, EngineEnd, TurboEnd, Bend, Segment). The plugin no longer offers them, but a saved tank that contains them still needs the part files to load.
Copy them to `Sprocket_Data\StreamingAssets\` of the game (same relative paths).
