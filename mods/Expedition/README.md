# Sprocket Expedition

A campaign mode for Sprocket: a numbered run through the installed eras, where you design and edit your tank in the native editor between fights and fight battles against AI enemies chosen from a pool of designs.

- Version: 0.3.12
- Requires: Sprocket UI Kit (`sprocket.uikit`, >=0.1.3 <0.2.0)
- Optional: Sprocket AI Orders (`sprocket.aiorders`) cooperates with it; AI Orders hands its orders to Expedition during battles. Expedition itself does not depend on it
- Plugin GUID: `sprocket.expedition`
- Game version tested: 0.2.56.2

## Features

- An "Expedition" button on the main menu opens a slot screen (8 save slots by default, 4 to 12 configurable), then the native New Game options: starting era, difficulty and an Ironman switch.
- The run's era schedule is built from every playable era found in the game that has at least one usable non-stationary tank, ordered by start date. New runs have four fights per era, the fourth a boss.
- Each fight is a card with a risk level (Cautious, Standard, Dangerous), the enemy cost against your available budget, and its reward; you edit your tank and fleet in the native editor within that budget.
- Rewards are items (for example Mass Production, Lend Lease, War Bonds, Operational Freedom), funds and supply; each victory also adds earned funds. Item list: `docs\adventure-rewards-2026-10-24.md`.
- Difficulty Recruit, Regular, Veteran or Ace (enemy budget x1, x1.25, x1.5, x2), optional +5% enemy budget per later era; normal runs allow unlimited retries, Ironman ends on one defeat. Scores and per-battle statistics (`stats\battles.csv`) are recorded.
- Enemy AI: both teams advance towards the opposing spawn and attack visible targets, with flanking, an Ambusher role for casemate designs, and set behaviours for the Fortress, Hunter and Battery bosses; some non-boss cards become specials (Rat Plague, Steel Wall, Crossfire).
- Enemy designs come from native blueprints and the `Expedition Spawns` faction, with optional extra pools under `BepInEx\config\SprocketExpedition\`; per-design caps per battle are configurable.
- Battle HUD and menus use Sprocket UI Kit; a defeat screen shows battle statistics. A soundtrack plays per phase (menu, build, combat, boss, victory, defeat) and can be changed or muted in the config.
- A test pool mode (`TestPool`) lets you try new designs as enemies.

## Settings and keys

- Config: `BepInEx\config\sprocket.expedition.cfg` (created by BepInEx on first start). Important entries and defaults:
- `[Campaign]`: `SaveSlots = 8`, `DifficultyEnemyBudget = 1.5` (1.0, 1.25, 1.5 or 2.0), `DifficultyEraProgression = true`, `EnemyHunt = true`, `CameraHeightVision = true`, `DesignCaps = T-84 Oplot-M Duplet (WT-maten)=2`, `TestPool = false`.
- `[BossBlueprints]`: one entry per era, empty by default; `A|B` lists several boss candidates.
- `[Combat]`: `AmbusherDesigns` and `NotAmbusherDesigns` (design names separated by `;`, empty by default).
- `[Expedition Music]`: `Enabled = true`, `Volume = 0.35`, `Playlist = true`, and one key per track slot.
- `[Diagnostics]`: `DetailedLogging = false`, `BattleStatsHooks = true` (turn it off if the designer or game crashes), `AllowFixtureCatalogue = false`.
- Keys: none; the mod is operated with the mouse, and Esc acts like the screen's Close or Cancel button. It does not use Sprocket Keybinds.
- Shipped files in `BepInEx\plugins\SprocketExpedition\`: `SprocketExpedition.Game.dll`, `SprocketExpedition.Core.dll`, `Audio\` and `THIRD-PARTY-NOTICES.md`. No parts, technology or localization files. Saves and statistics go to `BepInEx\config\SprocketExpedition\`.
- The soundtrack (six WAV tracks, about 136 MB) is supplied by the author and is not in this repository copy; it ships in the release. Its rights statement is in `Audio\MUSIC-NOTICE.md` of the release (copy in `assets\audio\`).
- One method (the designer event handoff) is adapted from the MIT-licensed Sprocket Battle Editor; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Status

- In game, early builds (0.2.0 to 0.3.3): the encounter menu, the Victory screen, era selection and test-pool battles worked; enemy reaction time and seek-and-destroy behaviour were seen in the log. The Ambusher role was not yet seen.
- Everything from 0.3.4 on (defeat screen, specials, boss behaviour, slot screen, design caps, several boss candidates, 0.3.12 as installed on 2026-11-06) has not been tested in game. The full campaign still needs player qualification.
- The whole pack was just renamed and has not been re-tested in game since.

## Building

.NET solution parts in `src\` (`SprocketExpedition.Core`, `SprocketExpedition.Game`, net6.0); the full campaign build is `dotnet build src\SprocketExpedition.Game -c Release -p:CampaignBuild=true -p:GameRoot=<path to Sprocket folder>` (the property is `GameRoot`; it also references the installed `SprocketUiKit.dll`). The audio files are copied from `assets\audio\` at build time and are not in this repository copy.
Tests are in `tests\` (run each with `dotnet run --project tests\<name> -c Release`). Full development notes, rules and release history: [GUIDE.md](GUIDE.md), [CHANGELOG.md](CHANGELOG.md) and the `docs\` folder (several documents and test plans are in Dutch).
