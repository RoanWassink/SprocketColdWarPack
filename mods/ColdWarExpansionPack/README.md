# Sprocket Cold War Expansion Pack

The Cold War Expansion Pack adds three eras to Sprocket: Coldwar1, Coldwar2 and Coldwar3. Each comes with its own engine and cannon technology that you can edit, and the mod repairs how vehicles dated on the final-era maximum date are classified. The other mods of the pack are separate.

- Version: 0.2.0
- Plugin GUID: `sprocket.coldwarexpansionpack`
- Author: Nero. MIT licence (see `LICENSE`).
- Requires: BepInEx 6 IL2CPP, and no other plugin
- Optional: Sprocket Keybinds (`sprocket.keybinds`), which lets this mod group the key settings of Hydropneumatic, Telescopic Mast, Thermal Sight and Smoke Launchers under one heading
- Game version tested: 0.2.56.2

## What it adds

- Three playable eras: Coldwar1 (from 1945.09.04), Coldwar2 (from 1960.01.01) and Coldwar3 (from 1975.01.01). Each era lasts until the next one starts. Medium vehicles go up to 18,000 mass and heavy vehicles start at 36,000.
- An engine technology (`CWEP_Engine`, dated 1945.09.03) with `torqueCoefficient` 1.15 and `technologyFactor` 2.25.
- A cannon technology (`CWEP_Cannon`, dated 1945.09.03) with operating pressure 55,000, penetrator constant 1,800, expected maximum armour 500, calibre 120 and propellant length 440.
- A repair for vehicles dated on the exact maximum date (9999.12.31). They are classified into the last era that is registered, and the saved date of the vehicle does not change. The era list is read from the game, so no era name or start date is fixed in the mod.
- One group named Cold War Expansion Pack in the key settings, when Sprocket Keybinds is installed.

The game's own `Eras\Coldwar.json` is a placeholder that starts on 1945.09.03 and is not playable. This mod does not ship it and does not change it. Coldwar1 starts one day later on purpose. Two eras with the same start date make the era checks of this pack (and of Expanded Ballistics, Thermal Sight and Hydropneumatic) fail closed.

## How to use it

1. Install the pack and start the game. The eras and technologies load with the game's own data.
2. In the vehicle designer, give your vehicle a date or era. The new eras appear next to the game's own, and parts of other mods in the pack follow the date of the vehicle.
3. To change an engine or cannon technology, edit the JSON files with the game closed (see Settings).

<!-- SCREENSHOT: cold-war-eras.png (the era choice in the designer showing Coldwar1, Coldwar2 and Coldwar3) -->

Nothing is forced while you play. The mod does not unlock eras, set a technology limit or change engine prices. Era access, part dates and prices follow the game's own data and calculations, so you change them by editing the JSON files. The maximum engine speed stays a calculation of the game (from engine geometry and the technology factor), and there is no setting for it.

## Settings

The settings file is `BepInEx\config\sprocket.coldwarexpansionpack.cfg`. BepInEx creates it on the first start. It has one entry, `[General] Enabled`, which is true by default. Set it to false to switch the final-era repair off. The data files stay in effect. The mod has no key bindings.

The data files sit in `Sprocket_Data\StreamingAssets\`: `Eras\Coldwar1.json`, `Eras\Coldwar2.json`, `Eras\Coldwar3.json`, `Technology\CWEP_Engine.json` and `Technology\CWEP_Cannon.json`. They are defaults. If you have edited them, keep your own versions when you update. The plugin folder is `BepInEx\plugins\SprocketColdWarExpansionPack\`.

## Known limits

- Version 0.2.0 has not changed since the build of 7 October 2026. The code and data were re-checked on 1 November 2026 against Coldwar3 as the last era. That check was done on paper and by script, not in the game.
- The final-era repair and the era checks per vehicle date have not been tested in the game.
- The renamed pack has not been re-tested in the game.
- The era files changed in the fresh release (Coldwar1 now starts on 1945.09.04). The scripted checks read these files and pass (100 checks), also outside the game.

## Building

This is a .NET project (`SprocketColdWarExpansionPack.csproj`, net6.0). It builds against the game's interop files:

`dotnet build SprocketColdWarExpansionPack.csproj -c Release -p:GameDir=<path to Sprocket folder>`

The era rules have checks in `Checks\Checks.csproj`. Install and update notes are in [GUIDE.md](GUIDE.md) and release notes in [RELEASE-NOTES.md](RELEASE-NOTES.md).
