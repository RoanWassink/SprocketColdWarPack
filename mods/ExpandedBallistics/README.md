# Sprocket Expanded Ballistics

Ammunition and armour for Sprocket in one plugin: dated shell profiles and guided ATGMs, an armour material and reactive-armour (ERA) system, a layered cassette composer, and a combat report that shows how each hit was resolved.

- Version: 0.22.76
- Plugin GUID: `sprocket.expandedballistics`
- Author: Nero. MIT licence (see `LICENSE`).
- Requires: Sprocket JSON Editor (`sprocket.jsoneditor`, 0.1.5 or later 0.1.x) and Sprocket UI Kit (`sprocket.uikit`, 0.1.7 or later 0.1.x)
- Optional: Sprocket Keybinds (`sprocket.keybinds`) for all key bindings. It also cooperates with Sprocket Carousel Autoloader (`sprocket.carousel`, mixed ammunition in its store), Sprocket Oscillating Turret (`sprocket.oscillatingturret`, ATGM sight mount preset), Sprocket Expedition (`sprocket.expedition`), the Cold War Expansion Pack (`sprocket.coldwarexpansionpack`, see `ARMOUR-RESPONSES.md`) and, if present, the third-party Dynamic Gun Sight plugin.
- Game version tested: 0.2.56.2

## Features

- Shell profiles dated by the vehicle date and its technologies: APFSDS (several generations, long and short rod), APCR, APDS, APCBC, APHE, HE, HEAT, tandem HEAT and HESH. APFSDS penetration follows penetrator length, density and speed, and a long rod needs room in the loader.
- Shell editor in the cannon inspector (shared JSON editor) for your own profiles, with a 32-profile limit. An invalid APFSDS design is withheld instead of silently replaced by stock ammunition.
- Ammunition loadout: carry a mix of shell types per cannon design, switch type in battle, and see the loaded shell and the mix in a HUD.
- Guided ATGMs: SACLOS, MCLOS (W/S/A/D), top attack with a lock key, gun-launched missiles and a wire-guided profile. A dedicated launcher, a guidance sight with a gimbal head and a finite ammunition box are placeable parts. Computer-controlled gunners lead their target.
- Armour materials: a material editor in the plate-structure panels, plus extra materials such as wood, rubber, fabric and glass (optional own texture folders), ceramics and perforated HHS.
- Placeable ERA in the Reactive armor tab: Kontakt-1, Kontakt-5 (with four size variants), Relikt, Nizh, Duplet, BRAT, ARAT and ROMOR-A. One cell reacts once and is rearmed on the next Edit/Play cycle; a spent housing shows its open state.
- Cassette composer: layered passive and reactive packages with presets modelled on named vehicles, a composite side module and composite plates, and an armour view of the layers.
- Armour simulator: preview shots in the designer. Repeated samples are previews and do not spend installed ERA.
- Combat report: the newest ten observed impacts, a Forensic section drawing per impact (with internal modules on the shot line), an optional on-screen damage feed, and the last session saved as local HTML and JSON.

## Experimental

- Composer fill tools (Cheek fill (shell), Convert to layers, layer overlay) are recent and still being refined; shapes outside the supported cases are refused with a message.
- Top attack uses a reference profile (`atgm_topattack_test`); the status notes record no in-game result for it.
- Tandem HEAT: the precursor and main charge share temporary ERA consumption; no guaranteed tandem defeat is claimed.
- `sprocket.expandedballistics.experimental.json` (dynamic rod-family recipes) and the `experimental*` cassette parts.
- All protection numbers are gameplay approximations, not manufacturer performance.

## Settings and keys

Config file: `BepInEx\config\sprocket.expandedballistics.cfg` (BepInEx creates it on first start; materials use `sprocket.expandedballistics.materials.cfg`). Main entries and defaults:

| Section / entry | Default |
|---|---|
| `APFSDS Beta` / `Enabled` (shell profiles; legacy section name) | true |
| `Ammunition loadout` / `Enabled`, `HUD` | true, true |
| `ATGM guidance` / `Enabled`, `TopAttackLockSeconds` (0.1 to 5), `DiagnosticLogging` | true, 1, false |
| `ATGM Launcher Tests` / `InitialReadyMissile`, `AutomaticAmmoBox` | true, false |
| `ATGM Audio` / `Enabled`, `VolumeMultiplier` (0 to 2) | true, 1 |
| `Damage feed` / `Enabled`, `Corner` | false, right |
| `Protection` / `GroupedBlockMode`, `CompositeProtectionScale` | PerInterval, 1 |
| `Diagnostics` / `Detailed logging` | false |

JSON files next to it (shell profiles, armour responses, cassette presets, ERA bindings, material reactions, composite module, spall) are described in [CUSTOM-SHELLS.md](CUSTOM-SHELLS.md) and [ARMOUR-RESPONSES.md](ARMOUR-RESPONSES.md).

Keys (group "Sprocket Expanded Ballistics" in the Sprocket Keybinds settings): W, S, A, D steer an MCLOS missile (up, down, left, right; they stay hard-wired if Keybinds is absent). Unbound by default: Combat report, Next and Previous shell type, Shell type slot 1 to 8, Lock top-attack target, Toggle damage feed in Play, Cycle damage feed corner, Show cassette layers, Cassette composer.

Shipping data: part, Technology and localization JSON in `Sprocket_Data\StreamingAssets\` (source in `native-data\` and `Material\files\`; `native-data\Eras\` holds Coldwar1, Coldwar2 and Coldwar3 as test data only; the Cold War Expansion Pack ships these eras. The shell editor's era list skips an era file marked `"playable": false`, such as the game's own one-day Coldwar), ERA meshes and icons in `era-assets\`, texture folders in `material-looks\`. The plugin folder is `BepInEx\plugins\SprocketExpandedBallistics\`.

## Status

Tested in the game in earlier 0.22.x builds: shell editor and ammunition HUD, the gimbal ATGM sight, player guidance and computer-controlled aiming (reported as accepted), material textures with save and load, and the combat report with Forensic. The composer fill tools were tested on several tanks in November with fixes still following. 0.22.72 to 0.22.76 (Forensic module icons, four Kontakt-5 size variants, choose-then-Apply in the composer, selected fronts that follow the wall) are installed but not yet tested in game. The renamed pack has not been re-tested.

## Building

.NET project in this folder (`SprocketExpandedBallistics.csproj`, net6.0); it builds against the game's interop assemblies and the built Sprocket JSON Editor, Keybinds and UI Kit plugins:
`dotnet build SprocketExpandedBallistics.csproj -c Release -p:GameDir=<path to Sprocket folder>`. The two ATGM launch sounds (`audio\atgm.wav`, `audio\atgm_gun.wav`) are embedded at build time and are not part of this repository (the released DLL contains them; both were created by the author with his own Suno account); supply PCM16 WAVs of your own under those names to build from this repository. Test fixtures are not included either. See [BUILD.md](BUILD.md).

## Documents

[GUIDE.md](GUIDE.md) (install and configuration), [REFERENCE-GUIDE.md](REFERENCE-GUIDE.md) (shell reference; older than parts of the current feature set, for example it still says there is no top attack), [CUSTOM-SHELLS.md](CUSTOM-SHELLS.md), [ARMOUR-RESPONSES.md](ARMOUR-RESPONSES.md), [ATGM.md](ATGM.md), `docs\`, [RELEASE-NOTES.md](RELEASE-NOTES.md).
