# Sprocket Cupolas

Adds a commander's cupola in the style of the T-90A (not an exact copy) to the Sprocket designer. It keeps the game's own commander, viewport and cook-off behaviour and replaces only the visible meshes.

- Version: 0.1.0
- Plugin GUID: `sprocket.cupolas`
- Requires: no other plugin
- Optional: Sprocket Telescopic Mast (`sprocket.telescopicmast`), used only to register the part icon. Without it the native fallback icon is shown and the part works as usual.
- Game version tested: 0.2.56.2

## Features

- One part, "Cupola T-90A", in the cupola category (tags cupola, commander, ussr), with a part date of 1992.01.01. Scale 0.5 to 2.
- A detailed ring (about 0.67 m across at scale 1) with a projecting sight head, and a separate lid. The lid is a static model: it does not open or close.
- Built on a clone of the stock German cupola, so the commander, viewport and cook-off use the native code. A cook-off may hide the lid.
- Collision, damage and mass stay those of the stock cupola (0 kg, 0 model cost); the visible ring is not extra armour.
- Replaces the model meshes of a placed cupola at runtime, on all native LOD levels, with generated UV charts and the native materials. Ghost previews keep the stock shape.
- Conservative checks: if the vehicle layout is ambiguous or the paint atlas cannot be inspected, the stock cupola is kept and a reason is logged.
- No Harmony patches, no UI, no controls and no saved data of its own.
- Approved models and icon were made with OpenSCAD; the sources are in `Assets/approved`.

## Settings and keys

- Config file: `BepInEx\config\sprocket.cupolas.cfg` (shipped with the mod; do not overwrite an existing file when copying). The only entry is `Diagnostics.DetailedLogging`, default false (set it to true for verbose log lines: READY, SCAN, REJECT, ADOPT, SWAP, STATE, BALANCE, RELEASE).
- No keys.
- Shipping data: part definition (`t90aCupolaPart.json`), English localization (`t90aCupola.xml`) under `Sprocket_Data\StreamingAssets`, and DLL and icon in `BepInEx\plugins\SprocketCupolas\`.

## Status

- Installed on 2026-11-11 with a hash check, but not yet tested in game; no in-game test result has been recorded.
- Open in-game checks (see `LIVE-ACCEPTANCE.md`): placement and selection in the designer, camo, all zoom levels, mirroring on the centre line, test drive and return to the designer, save and reload, and stock mass and cost.
- The whole pack was renamed and has not been re-tested in game yet.

## Building

.NET project in this folder (`SprocketCupolas.csproj`, net6.0). It builds against the game's interop assemblies: `dotnet build -c Release -p:GameDir=<path to Sprocket folder>`. `Verify.ps1` runs the offline checks, and `Build-Package.ps1 -Out <empty folder>` stages the shipped files in the game folder layout. The STL exports are not part of the repository; the OBJ files and OpenSCAD sources are.

Docs: [GUIDE.md](GUIDE.md) (developer notes), [CHANGELOG.md](CHANGELOG.md), [GEOMETRY-AND-EVIDENCE.md](GEOMETRY-AND-EVIDENCE.md), [LIVE-ACCEPTANCE.md](LIVE-ACCEPTANCE.md). Licence: MIT (see the repository LICENSE). Author: Nero.
