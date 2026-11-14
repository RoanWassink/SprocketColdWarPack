# Sprocket Cold War Expansion Pack

Core of the Cold War pack for Sprocket: it adds the Coldwar era with editable engine and cannon technology data, and repairs the classification of vehicles dated on the final-era maximum date. The gameplay mods of the pack are separate.

- Version: 0.2.0
- Plugin GUID: `sprocket.coldwarexpansionpack`
- Author: Nero. MIT licence (see `LICENSE`).
- Requires: BepInEx 6 IL2CPP (no other plugin; the only `BepInDependency` is a soft one)
- Optional: Sprocket Keybinds (`sprocket.keybinds`) for the pack grouping in the key settings; Hydropneumatic, Telescopic Mast, Thermal Sight and Smoke Launchers are the plugins it groups there
- Game version tested: 0.2.56.2

## Features

- Final-era repair: a vehicle on the exact native maximum date (9999.12.31) is classified into the last registered era. The era list is read from the game at run time, so no era name or start date is hard-coded, and the vehicle's saved date is not changed.
- Data: `Eras\Coldwar.json` defines the era "Coldwar" (start 1945.09.03, playable, medium vehicle mass 18,000, heavy 36,000).
- Data: `Technology\CWEP_Engine.json` is a combustion-engine technology dated 1945.09.03 (`torqueCoefficient` 1.15, `technologyFactor` 2.25).
- Data: `Technology\CWEP_Cannon.json` is a cannon technology dated 1945.09.03 (operating pressure 55,000, penetrator constant 1,800, expected maximum armour 500, calibre 120, propellant length 440).
- Nothing is forced at run time: no era unlock, no technology horizon and no engine price override. Era access, part dates and prices follow the native data and calculations; edit the JSON files to change them.
- With Sprocket Keybinds installed, the four plugins above are listed under one "Cold War Expansion Pack" group in the key settings.
- Engine maximum RPM stays a native calculation (engine geometry and technology factor); there is no direct max-RPM setting.

## Settings and keys

Config file: `BepInEx\config\sprocket.coldwarexpansionpack.cfg`, created by BepInEx on first start. The only entry is `[General] Enabled` (default true); false switches the final-era repair off and leaves the data files in effect. No key bindings.

Shipping data (in `Sprocket_Data\StreamingAssets\`, source in `data\`): `Eras\Coldwar.json`, `Technology\CWEP_Engine.json`, `Technology\CWEP_Cannon.json`. They are defaults; if you already edited these files, keep your versions. The plugin folder is `BepInEx\plugins\SprocketColdWarExpansionPack\`.

## Status

Version 0.2.0 is unchanged since the DLL of 2026-10-07 and was re-checked (code and data, not in game) on 2026-11-01 against Coldwar3 as the last era. The status notes record no explicit in-game test of the final-era repair or of the era checks per vehicle date ("not yet tested in game"). The renamed pack has not been re-tested.

## Building

.NET project in this folder (`SprocketColdWarExpansionPack.csproj`, net6.0); it builds against the game's interop assemblies:
`dotnet build SprocketColdWarExpansionPack.csproj -c Release -p:GameDir=<path to Sprocket folder>`. The pure era rules have checks in `Checks\Checks.csproj`. Install and update notes: [GUIDE.md](GUIDE.md); release notes: [RELEASE-NOTES.md](RELEASE-NOTES.md).
