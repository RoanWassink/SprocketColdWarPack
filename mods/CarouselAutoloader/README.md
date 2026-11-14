# Sprocket Carousel Autoloader

Adds dated autoloader mechanisms to Sprocket: bustle, semi-autoloader and revolver parts, plus a carousel option on turret baskets. Each one feeds a cannon from a finite store of rounds and shows its own model, sound and HUD.

- Version: 0.2.34
- Requires: no other plugin (runs on BepInEx 6.0.0-be.788, IL2CPP)
- Optional: Sprocket UI Kit (`sprocket.uikit`) for the HUD; Sprocket Expanded Ballistics (`sprocket.expandedballistics`) for mixed-ammunition supply, round geometry and HUD placement under its AMMO panel
- Plugin GUID: `sprocket.carousel`
- Game version tested: 0.2.56.2

## Features

- Bustle autoloader part (available from 1952.01.01): a finite ready magazine that an assigned crew loader refills from the normal racks after it empties.
- Semi-autoloader part (1953.01.01): mechanical assistance for a crew loader; it needs a healthy crew loader.
- Revolver autoloader part (1952.01.01): a two-drum, AMX-13 style loader with fast fire from one drum. A crew loader refills it (also while driving); without one, the drum refills round by round only while the vehicle stands still.
- Carousel option on a turret basket, with a T-72 style (1973.01.01) or T-64/80 style (1967.01.01) layout; the dates are enforced by the inspector and the runtime.
- Optional two-piece ammunition for the bustle and revolver: projectile and charge are stored as separate pieces, which lengthens the cycle by a factor 1.25.
- Own models with true-size rounds in cassettes, chains and drums; the carousel ring turns one slot per shot and a cassette lift sits behind the breech.
- Native cook-off for carousel basket ammunition through the game's damage model (switchable).
- Own reload sounds and an optional HUD for stock, fire cycle and refill, shown only while something is running.
- A too-early or oversized loader keeps its saved part but does no work, with one warning in the log.

## Settings and keys

Config file: `BepInEx\config\sprocket.carousel.cfg`, created on first start. This mod has no key bindings; the loaders are set up in the part and basket inspectors in the vehicle editor.

| Section / key | Default | Meaning |
|---|---|---|
| `Visuals / OwnRounds` | `true` | Own true-size rounds instead of the native ammunition grid. |
| `Visuals / CarouselModel` | `Detailed` | `Detailed` or `Simple` carousel model (the detailed one falls back to simple if it does not fit). |
| `Visuals / CarouselTurnSeconds` | `0.5` | Seconds the ring takes to turn one slot; 0 = jump. |
| `Visuals / CarouselLift` | `true` | Show the cassette lift behind the slots. |
| `Bustle / MaxFeedDistance` | `0` | Feed arm reach in metres; 0 = native crew hand reach. |
| `Revolver / MaxCaliberMm` | `155` | Largest calibre the drum accepts. |
| `Revolver / MaxRoundLengthMm` | `0` | Longest round in mm; 0 = no fixed limit. |
| `Revolver / CycleFactor` | `0.6` | Fire cycle as a multiple of the bustle cycle. |
| `Damage / NativeCookoff` | `true` | `false` turns off the mod's own cook-off route for basket ammunition. |
| `Audio / Enabled` | `true` | Own reload sounds; they only run on Unity 6000.3.21f1. |
| `Hud / Enabled`, `Placement` | `true`, `BelowAmmo` | HUD on or off; `BelowAmmo` or `Manual` placement. |
| `Diagnostics / DetailedLogging` | `false` | Extra log lines for troubleshooting. |

More entries (`RefillCycleFactor`, `MinimumRefillSeconds`, HUD offsets) are in the cfg file with descriptions. Shipped data: three part definitions (`sprocketBustleAutoloaderPart.json`, `sprocketRevolverAutoloaderPart.json`, `sprocketSemiAutoloaderPart.json`) in `Sprocket_Data\StreamingAssets\Parts`; the plugin folder is `BepInEx\plugins\SprocketCarouselAutoloader\`.

## Status

Version 0.2.34 was installed on 2026-11-09 and is waiting for in-game tests. Earlier versions were seen working in the game for the HUD, the T-80 style carousel model and the lift position; the ring turn was confirmed in the log but not yet by eye. The gameplay checks of the loaders (refill, two-piece ammunition, revolver limits) and the 0.2.34 lift fix (basket depths 1193 to 1216 mm) are listed as open. A report of a vehicle vanishing after a carousel basket explosion (2026-11-12) is under investigation; `Damage / NativeCookoff = false` switches the mod's cook-off route off. The whole pack was just renamed and has not been re-tested in the game yet.

## Building

.NET project in this folder (`SprocketCarouselAutoloader.csproj`, net6.0); build with `dotnet build SprocketCarouselAutoloader.csproj -c Release -p:GameDir=<path to Sprocket folder>`. The UI Kit DLL is taken from `$(GameDir)\BepInEx\plugins\SprocketUiKit` or from `-p:UiKitDll=<path>`. The three reload sound files (`Assets\Audio\t90.wav`, `t64.wav`, `bustle.wav`) are embedded by the project but are not in this folder: the audio files ship with the release only. The three sounds were created by the author with his own Suno account.

Release notes: [CHANGELOG.md](CHANGELOG.md). Tests: `dotnet run --project tests\Carousel.Tests.csproj -c Release`. The bustle and revolver meshes come from the OpenSCAD sources in `Models\scad`; the two carousel models are `Models\carousel_t72.obj` and `carousel_t80.obj`.
