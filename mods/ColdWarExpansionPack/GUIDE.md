# Sprocket Cold War Expansion Pack 0.2.0 (core)

Core for Sprocket 0.2.55.5 with BepInEx 6 IL2CPP. It contains the core DLL and the native Era/Technology data for the Cold War era. Gameplay addons and the mod loader are separate. Install only one copy of the core DLL.

## What this core does

Era playability, technology availability, Part dates and engine pricing follow native data and calculations. The core does not force a later technology frame, unlock an era at runtime, or override engine prices. Engine and cannon settings are declared in editable dated Technology JSONs.

The one core fix handles the exact native maximum-date boundary in the final registered era. It reads the current ordered era timeline and preserves the vehicle's saved date. Renamed eras and custom valid timelines do not require a hardcoded Coldwar name or modern start date.

The plugin, config and Harmony names are `sprocket.coldwarexpansionpack`. Optional Keybinds pack grouping recognizes the Hydropneumatic, Telescopic Mast, Thermal Sight and Smoke Launchers plugins. Keybinds remains optional for this core.

## Install or update

1. Close Sprocket. Back up the existing core DLL, core configuration and any native data you intend to edit.
2. Merge the supplied `BepInEx` folder into the folder containing `Sprocket.exe`, replacing `BepInEx/plugins/SprocketColdWarExpansionPack/SprocketColdWarExpansionPack.dll`. Remove any duplicate core DLL from other plugin locations after backing it up.
3. **The native data files are defaults, not a mandatory overwrite.** The supplied `Sprocket_Data/StreamingAssets` folder contains `Technology/CWEP_Engine.json`, `Technology/CWEP_Cannon.json` and `Eras/Coldwar.json`. If these already exist with your own dates or properties, preserve them and review the supplied values before changing anything.
4. For Coldwar, review the existing `Eras/Coldwar.json`. Native `playable: true` enables it; retain its current name, start date and mass classifications. Do not replace an existing custom Era file merely to enable playability.
5. Start Sprocket. Core config is `BepInEx/config/sprocket.coldwarexpansionpack.cfg`. Restart after editing native data or config.

The package does not alter user saves, addon profiles, bindings, loader files or unrelated plugins. It does not include a preconfigured CFG that would overwrite your choices.

## Editable native data

| File | Native type / settings |
|---|---|
| CWEP_Engine.json | `combustionEngine`, default date 1945.09.03; `torqueCoefficient: 1.15`, `technologyFactor: 2.25` |
| CWEP_Cannon.json | `cannon`, default date 1945.09.03; operating pressure 55,000, penetrator constant 1,800, expected maximum armour 500, calibre 120, propellant length 440 |
| Coldwar.json | Default name Coldwar, start 1945.09.03, playable true, medium mass 18,000, heavy mass 36,000 |

These are game calibrations, not historical equipment specifications. Engine maximum RPM is derived by native calculations using engine geometry and technology factor; there is no new direct max-RPM switch in this core. If a custom timeline moves technology introduction, edit the corresponding native data dates rather than relying on an era-name override.

`General.Enabled=false` disables the final-date repair. Native data and independently installed addons remain active.

To roll back, close Sprocket and restore the backed-up DLL and intentionally edited native files.

<!-- sp-compat {"hamish.sprocket": "0.2.55.5", "bepinex.bepinex": "6.0.0-be.788"} -->
