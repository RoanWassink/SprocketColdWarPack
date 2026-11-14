# Sprocket Smoke Launchers

Adds a three-tube smoke launcher bank to Sprocket. One key fires a salvo of smoke grenades that burst into visual smoke screens.

- Version: 0.2.5-datums.2
- Requires: Sprocket Keybinds (`sprocket.keybinds`, version >=0.1.5 <0.2.4)
- Optional: none
- Plugin GUID: `sprocket.smokelaunchers`
- Game version tested: 0.2.56.2

## Features

- "Smoke Launcher Bank" part (available from 1945.01.01): three grenade tubes on a hull or turret, mirrorable and placeable like other external equipment. Loaded mass 18 kg, price 120 (60 material, 60 assembly).
- One loaded salvo of three grenades per bank; all fitted banks fire together (up to 8 banks per volley). There is no reload during combat.
- Every new Play or combat session loads one fresh salvo per bank. A spent bank stays spent for the rest of that session, including copies and reloaded saves of the vehicle.
- Grenades fly a short ballistic path and open into smoke clouds at the impact point or at the end of flight (at most 5 s); a cloud lasts 25 s.
- Authored launch discharge and a separate grenade-deployment cue, both generated in code (no recorded or third-party audio); one volume setting controls both.
- The smoke is visual only: it does not block the line of sight of computer-controlled units or of thermal sights.
- The stock German smoke launcher part is dated 1942.01.01 by this mod (the data file is shipped); only the bank fires.
- An imported bank on a vehicle that is too early keeps its identity, fires nothing and logs one warning; the last era makes it available.

## Settings and keys

Config file: `BepInEx\config\sprocket.smokelaunchers.cfg`, created on first start.

| Section / key | Default | Meaning |
|---|---|---|
| `General / Enabled` | `true` | Enable firing. Disabling clears effects but never refills ammunition. |
| `Audio / LaunchVolume` | `0.6` | Volume of the launch and deployment sounds, 0 to 1; 0 mutes both. |

Key: "Fire smoke salvo", default G. Rebind it in the game's Settings keybind menu (Sprocket Keybinds) under Smoke Launchers.

Shipped data: `smokeLauncherBankPart.json` and `germanSmokeLauncherPart.json` in `Sprocket_Data\StreamingAssets\Parts`, and `smokeLauncherBank.xml` in the English localization folder; the plugin folder is `BepInEx\plugins\SprocketSmokeLaunchers\`.

## Status

The author's notes record that version 0.2.3 ran without crashes and reloaded the banks after a Play restart. Version 0.2.5-datums.2 was installed on 2026-11-01; its stronger launch and deployment sounds, the 1945 date gate (bank selectable in a Coldwar vehicle, also in Expedition) and the session reload rules are listed as not yet tested in the game. The whole pack was just renamed and has not been re-tested in the game yet.

## Building

.NET project in this folder (`SprocketSmokeLaunchers.csproj`, net6.0); build with `dotnet build SprocketSmokeLaunchers.csproj -c Release -p:GameDir=<path to Sprocket folder>`. The Sprocket Keybinds DLL is taken from `$(GameDir)\BepInEx\plugins\SprocketKeybinds` or from `-p:KeybindsDll=<path>`.

Docs: [GUIDE.md](GUIDE.md), [CHANGELOG.md](CHANGELOG.md), [RESEARCH.md](RESEARCH.md), [TEST-PLAN.md](TEST-PLAN.md). Tests: `dotnet run --project tests -c Release` and `Verify.ps1`.
