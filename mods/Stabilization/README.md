# Sprocket Stabilization

Adds a stabilizer option to the turret drives in the vehicle designer. A drive that is marked as stabilized gets more torque and a realistic rate limit while it follows the aim point, so the turret can hold the target while the hull moves.

- Version: 0.7.3
- Plugin GUID: `sprocket.stabilization`
- Author: Nero
- Requires: BepInEx 6 IL2CPP (no other plugin; the mod declares no `BepInDependency`)
- Optional: Sprocket Oscillating Turret (`sprocket.oscillatingturret`). Its elevation ring carries the `oscillatingElevationDrive` tag that this mod uses to tell an elevation drive from a horizontal one. No code dependency.
- Game version tested: 0.2.56.2

## Features

- Adds a "Stabilizer - drive setup" section with one checkbox to the traverse motor inspector: "Horizontal drive stabilized (auto torque)" for an ordinary turret ring, "Elevation drive stabilized (auto torque)" for a ring tagged `oscillatingElevationDrive`.
- While a marked drive is aiming, the native turret torque multiplier is raised for that call only (default 100) and restored as soon as the call returns. The native controller, crew and aiming logic stay in charge.
- Realistic limits are on by default: horizontal traverse at most 24 deg/s, elevation at most 6 deg/s, 0.75 s to reach full rate (acceleration = rate / response time). A native limit is never raised above its own value.
- Optional explicit limits (`OverrideRateLimits`) replace the realistic ones with your own traverse rate, acceleration and brake values.
- Adds a "Stabilizer - elevation drive strength" field to the laying drive inspector. It shows the drive's native elevation torque multiplier divided by 100. It needs one uniquely connected turret motor; a shared or ambiguous design is left unchanged and the inspector says why.
- The checkbox is stored in the motor blueprint (`sprocket.stabilization.v1`) and in the legacy component state, so it survives saving and loading a design.
- Dated: the Technology `sprocketStabilization` starts on 1948.01.01 (the final-era date 9999.12.31 always qualifies). On an earlier vehicle date the controls are hidden; a saved checkbox is kept but not applied, and one warning is logged.
- The checkbox, boost, component-state and blueprint-state patch groups load independently: if one cannot bind to the game, only that part is lost and an error is logged.

This is an in-game approximation: it boosts drive torque and limits the rate, it does not simulate a gyroscopic stabilizer.

## Settings and keys

Config file: `BepInEx\config\sprocket.stabilization.cfg`, section `[Stabilizer]`, created by BepInEx on first start.

| Entry | Default |
|---|---|
| `Enabled` | true |
| `RealisticLimits` | true |
| `HorizontalMaxTraverseRate` (deg/s) | 24 |
| `ElevationMaxTraverseRate` (deg/s) | 6 |
| `ResponseTimeSeconds` | 0.75 |
| `TorqueMultiplier` | 100 |
| `OverrideRateLimits` | false |
| `MaxTraverseRate` / `MaxAcceleration` / `MaxBrakeAcceleration` (only with `OverrideRateLimits`) | 360 / 3600 / 3600 |

No key bindings; the mod does not use Sprocket Keybinds. Shipping data: two Technology files in `Sprocket_Data\StreamingAssets\Technology\` (`SprocketStabilization.json`, `SprocketStabilizationTraverseMotor.json`, both dated 1948.01.01). The plugin folder is `BepInEx\plugins\SprocketStabilization\`.

## Status

Installed for the date change on 2026-11-01. The status notes record no in-game test of the stabilizer or of the 1948 date gate yet ("not yet tested in game"); the open checks are a Coldwar-era vehicle with the option available and a too-early saved design that loads with one warning. Unit checks and regression checks with game doubles (135 and 86 checks) passed when the pack was repacked, but the renamed pack has not been re-tested in game.

## Building

.NET project in `src\` (`SprocketStabilization.csproj`, net6.0); it builds against the game's interop assemblies:
`dotnet build src\SprocketStabilization.csproj -c Release -p:GameDir=<path to Sprocket folder>`. Pure checks are in `tests\Checks.csproj`, game-double regression checks in `probes\Probes.csproj`. History: [CHANGELOG.md](CHANGELOG.md).
