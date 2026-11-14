<!-- sp-compat {"hamish.sprocket": "0.2.55.5", "bepinex.bepinex": "6.0.0-be.788"} -->

**Required dependency: Sprocket Keybinds API (>=0.1.3 <0.2.0). Hydropneumatic, Telescopic Mast, Thermal Sight and Smoke Launchers will not load without it. The full pack includes it: keep its DLL installed. For separate plugin downloads, install the API ZIP once, merging its BepInEx folder into your game folder.**

Raise, lower and pitch your tank while driving, with optional independent front/rear control.

## 0.4.4 - date balance

- The Hydropneumatic part and `HydroEraPolicy` now use 1967.01.01 instead of 1945.09.03.
- The exact final-era sentinel 9999.12.31 makes the part available instead of being compared with the last finite era start.
- A too-early imported part keeps its identity, keeps the native suspension and logs one warning.

## What changes for you

Controls now appear in the shared Mod keybinds menu. Rebind them without editing keyboard settings in a CFG file. This build also retains the suspension geometry fixes when changing suspension parts.

**Beta:** tested together in the Cold War pack. Armour-response values are bounded gameplay approximations, not exact historical protection or a guarantee against every shell.

## Requirements and update

Sprocket 0.2.55.5, Windows x64 and an already-working Sprocket Mod Loader / BepInEx 6 IL2CPP setup. **Loader not included. Quality of Life not required.**
- Sprocket Keybinds (>=0.1.3 <0.2.0), installed separately once. This is required: without a compatible API, BepInEx skips this mod. The full pack includes it.

Close the game, back up matching files and saves, then merge the ZIP's folders into the game directory. Keep one DLL per plugin and preserve customized configs/catalogues/WAV overrides. See the README for exact use, controls, limitations and uninstall instructions.

[Separate loader installation](https://github.com/Hans21223/Sprocket-Mod-Loader).

