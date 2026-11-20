# Sprocket Stabilization

Stabilization adds a stabilizer option to the turret drives in the vehicle designer. A drive that is marked as stabilized gets more torque and a realistic speed limit while it follows the aim point, so the turret can hold the target while the hull moves.

- Version: 0.7.4
- Author: Nero
- Needs: no other mod
- Works with: Sprocket Oscillating Turret. Its elevation ring carries the `oscillatingElevationDrive` tag, which this mod uses to tell an elevation drive from a horizontal one.
- Game version tested: 0.2.56.2

## What it adds

- A section "Stabilizer - drive setup" with one tick box in the inspector of the traverse motor. The box reads "Horizontal drive stabilized (auto torque)" for an ordinary turret ring and "Elevation drive stabilized (auto torque)" for a ring from Oscillating Turret.
- Extra torque while a marked drive is aiming. The game's own controller, crew and aiming stay in charge.
- Realistic limits, on by default: horizontal traverse at most 24 degrees per second, elevation at most 6 degrees per second, and 0.75 seconds to reach full speed. A limit of the game itself is never raised.
- Optional limits of your own (`OverrideRateLimits`) for traverse rate, acceleration and braking.
- A field "Stabilizer - elevation drive strength" in the inspector of the laying drive. It shows the native elevation torque of the drive divided by 100. It needs one turret motor that is clearly connected. A shared or unclear design is left unchanged, and the inspector says why.
- The tick box is saved with the vehicle, so it survives saving and loading a design.
- A date: the stabilizer is available from 1948.01.01. On an earlier vehicle the controls are hidden, and a saved tick box is kept but not applied, with one warning in the log.

This is an approximation inside the game. It raises the drive torque and limits the speed, and it does not simulate a gyroscopic stabilizer.

## How to use it

1. Open the vehicle designer and place a turret with a traverse motor. Set the vehicle date to 1948 or later.
2. Select the traverse motor and open its inspector.
<!-- SCREENSHOT: stabilizer-inspector.png (the "Stabilizer - drive setup" section in the traverse motor inspector) -->
3. Tick "Horizontal drive stabilized (auto torque)". For the elevation ring of an oscillating turret, tick "Elevation drive stabilized (auto torque)" instead.
4. For the elevation drive, open the laying drive inspector to read the field "Stabilizer - elevation drive strength".
5. Start a test drive. The turret holds the aim point while the hull moves, within the speed limits.

## Settings

The settings file is `BepInEx\config\sprocket.stabilization.cfg`, in the section `[Stabilizer]`. The game creates it on the first start. The mod has no keys.

| Entry | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Turns the stabilizer on. |
| `RealisticLimits` | `true` | Uses the speed limits below. |
| `HorizontalMaxTraverseRate` | `24` | Top traverse speed in degrees per second. |
| `ElevationMaxTraverseRate` | `6` | Top elevation speed in degrees per second. |
| `ResponseTimeSeconds` | `0.75` | Time to reach full speed. |
| `TorqueMultiplier` | `100` | Torque multiplier of a stabilized drive. |
| `OverrideRateLimits` | `false` | Uses your own values below instead of the realistic limits. |
| `MaxTraverseRate`, `MaxAcceleration`, `MaxBrakeAcceleration` | `360`, `3600`, `3600` | Your own values, used only with `OverrideRateLimits`. |

The two data files `SprocketStabilization.json` and `SprocketStabilizationTraverseMotor.json` (both dated 1948.01.01) are in `Sprocket_Data\StreamingAssets\Technology\`. The plugin folder is `BepInEx\plugins\SprocketStabilization\`.

## Known limits

- The stabilizer and the 1948 date limit have not been tested in the game yet. The open checks are a Cold War vehicle where the option is available, and a saved design that is too early and loads with one warning.
- Unit checks and regression checks with game stand-ins (135 and 86 checks) passed when the pack was repacked.
- If one of the parts of the mod (tick box, torque boost, or saving) cannot connect to the game, only that part is lost and an error is written to the log.
- The renamed pack as a whole has not been re-tested in the game yet.

## Building

The source is a .NET project in `src\` (`SprocketStabilization.csproj`, net6.0). It builds against the game's interface files: `dotnet build src\SprocketStabilization.csproj -c Release -p:GameDir=<path to Sprocket folder>`. Plain checks are in `tests\Checks.csproj`, and regression checks with game stand-ins are in `probes\Probes.csproj`. History: [CHANGELOG.md](CHANGELOG.md).
