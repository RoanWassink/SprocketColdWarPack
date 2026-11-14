# Sprocket Cold War Expansion Pack 0.2.0 (core)

Core for Sprocket 0.2.55.5 with BepInEx 6 IL2CPP. It contains the core DLL and the native Era/Technology data for the Cold War eras (Coldwar1, Coldwar2 and Coldwar3). Gameplay addons and the mod loader are separate. Install only one copy of the core DLL.

## What this core does

Era playability, technology availability, Part dates and engine pricing follow native data and calculations. The core does not force a later technology frame, unlock an era at runtime, or override engine prices. Engine and cannon settings are declared in editable dated Technology JSONs.

The one core fix handles the exact native maximum-date boundary in the final registered era. It reads the current ordered era timeline and preserves the vehicle's saved date. Renamed eras and custom valid timelines do not require a hardcoded Coldwar name or modern start date.

The plugin, config and Harmony names are `sprocket.coldwarexpansionpack`. Optional Keybinds pack grouping recognizes the Hydropneumatic, Telescopic Mast, Thermal Sight and Smoke Launchers plugins. Keybinds remains optional for this core.

## Install or update

1. Close Sprocket. Back up the existing core DLL, core configuration and any native data you intend to edit.
2. Merge the supplied `BepInEx` folder into the folder containing `Sprocket.exe`, replacing `BepInEx/plugins/SprocketColdWarExpansionPack/SprocketColdWarExpansionPack.dll`. Remove any duplicate core DLL from other plugin locations after backing it up.
3. **The native data files are defaults, not a mandatory overwrite.** The supplied `Sprocket_Data/StreamingAssets` folder contains `Technology/CWEP_Engine.json`, `Technology/CWEP_Cannon.json` and the three new era files `Eras/Coldwar1.json`, `Eras/Coldwar2.json` and `Eras/Coldwar3.json`. If these already exist with your own dates or properties, preserve them and review the supplied values before changing anything.
4. The three eras are new files of this pack: the game's own `Eras/Coldwar.json` (a placeholder with start 1945.09.03 that the game does not make playable) is neither shipped nor changed any more. Coldwar1 starts the day after it (1945.09.04), Coldwar2 on 1960.01.01 and Coldwar3 on 1975.01.01, each running up to the next start. An older version of this pack set `playable: true` in the game's `Coldwar.json`; if your copy still has that, restore the game's original file (the installer `install-all.ps1` does it by default; or use Steam's "Verify integrity of game files"), otherwise the era list shows an extra one-day era.
5. Start Sprocket. Core config is `BepInEx/config/sprocket.coldwarexpansionpack.cfg`. Restart after editing native data or config.

The package does not alter user saves, addon profiles, bindings, loader files or unrelated plugins. It does not include a preconfigured CFG that would overwrite your choices.

## Editable native data

| File | Native type / settings |
|---|---|
| CWEP_Engine.json | `combustionEngine`, default date 1945.09.03; `torqueCoefficient: 1.15`, `technologyFactor: 2.25` |
| CWEP_Cannon.json | `cannon`, default date 1945.09.03; operating pressure 55,000, penetrator constant 1,800, expected maximum armour 500, calibre 120, propellant length 440 |
| Coldwar1.json | Name Coldwar1, start 1945.09.04, playable true, medium mass 18,000, heavy mass 36,000 |
| Coldwar2.json | Name Coldwar2, start 1960.01.01, playable true, medium mass 18,000, heavy mass 36,000 |
| Coldwar3.json | Name Coldwar3, start 1975.01.01, playable true, medium mass 18,000, heavy mass 36,000 |

These are game calibrations, not historical equipment specifications. Engine maximum RPM is derived by native calculations using engine geometry and technology factor; there is no new direct max-RPM switch in this core. If a custom timeline moves technology introduction, edit the corresponding native data dates rather than relying on an era-name override.

`General.Enabled=false` disables the final-date repair. Native data and independently installed addons remain active.

To roll back, close Sprocket and restore the backed-up DLL and intentionally edited native files.

<!-- sp-compat {"hamish.sprocket": "0.2.55.5", "bepinex.bepinex": "6.0.0-be.788"} -->
