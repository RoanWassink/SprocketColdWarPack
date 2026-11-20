# Sprocket Oscillating Turret

Oscillating Turret adds two ways to build a gun that tilts together with a larger part of the turret. One is a compact elevation drive that tilts a whole upper turret with the gun. The other is a Heavy Trunnion Mount with a stronger elevation motor.

- Version: 0.3.1
- Needs: no other mod
- Used by other mods: the missile mount preset of Sprocket Expanded Ballistics needs an active Oscillating Turret, and Sprocket Stabilization uses the `oscillatingElevationDrive` part tag to tell an elevation drive from a horizontal one
- Game version tested: 0.2.56.2

## What it adds

- The "Oscillating elevation drive" part (from 1952.01.01), a compact elevation basket. You place it upright on an azimuth turret, attach the upper turret to it and set the elevation arc in the properties of the turret motor. It uses the turret motor you assign and the gun-layer crew.
- A generated "Oscillating" turret preset in the game's own tab of generated turret presets (also from 1952.01.01). A vehicle dated earlier is not offered the preset.
- The "Heavy Trunnion Mount" (from 1952.01.01), a mantlet with a laying drive. It is built on the native trunnions at scale 1.5, with elevation from +25 to -10 degrees and no azimuth. The elevation torque is doubled and the top speed stays as in the game.
- The Heavy Trunnion Mount adds 250 kg and costs twice the native price of that mass. The laying drive may sit 1.5 m from the trunnions instead of 1 m.
- An own model for the Heavy Trunnion Mount (a frame, a trunnion block and a pinion that turns with the elevation) and its own icon in the parts picker.
- An optional compact pivot housing, an enclosed horizontal pivot and actuator, instead of the open elevation ring and the visible basket. Motor, axis, mass and cost stay as in the game.
- A guard for the X-ray view: when the mod loses control of a turret, the game's own up direction is restored, so the upper turret no longer rolls onto its side.

## How to use it

1. Open the vehicle designer. For the elevation drive, find the "Oscillating elevation drive" in the parts list, or pick the "Oscillating" preset in the generated turret presets.
<!-- SCREENSHOT: oscillating-parts.png (the elevation drive and the Heavy Trunnion Mount in the parts list) -->
2. Place the elevation drive upright on an azimuth turret ring. Attach the upper turret to it.
3. Open the properties of the turret motor and set the elevation arc. Assign the turret motor and a gun-layer crew member.
4. For the Heavy Trunnion Mount, place the mantlet part and the laying drive part. Keep the laying drive within 1.5 m of the trunnions.
5. Start a test drive. The upper turret tilts together with the gun.
<!-- SCREENSHOT: oscillating-turret.png (a tank with the oscillating turret) -->

## Settings

The settings file is `BepInEx\config\sprocket.oscillatingturret.cfg`. The game creates it on the first start. The mod has no keys of its own.

| Section / key | Default | What it does |
|---|---|---|
| `General / Enabled` | `true` | `false` turns the mod off. Set it before the game starts. |
| `Visual / RotateRingModel` | `true` | Rolls the visible ring model so that its hole faces the elevation axis. |
| `Visual / RingModelRollDegrees` | `90` | The roll of that model. 0 leaves it untouched. |
| `Visual / CompactPivotHousing` | `true` | Uses the enclosed pivot and actuator model. Restart to change. |
| `Heavy Trunnion / TorqueFactor` | `2` | Elevation torque as a multiple of the native drive. |
| `Heavy Trunnion / ExtraMassKg` | `250` | Mass the Heavy Trunnion Mount adds. |
| `Heavy Trunnion / CostFactor` | `2` | Cost as a multiple of the native price of that mass. |
| `Heavy Trunnion / MaxDistanceFactor` | `1.5` | Allowed distance between laying drive and trunnions, as a multiple of 1 m. |
| `Diagnostics / DetailedLogging` | `false` | Extra lines in the log file for troubleshooting. |

## Known limits

- Version 0.3.1 is installed and has not been tested in the game yet. This covers the X-ray fix, the Heavy Trunnion Mount and the change to the release hook.
- The one recorded game test (version 0.2.10, X-ray view) failed. That led to the fix.
- An imported vehicle with a drive that is too early keeps the part, but its turret stays native, and one warning is written to the log.
- The renamed pack as a whole has not been re-tested in the game yet.

## Building

The source is a .NET project in `src\` (`SprocketOscillatingTurret.csproj`, net6.0). Build it with `dotnet build src\SprocketOscillatingTurret.csproj -c Release -p:GameDir=<path to Sprocket folder>`. `check.ps1` runs the build and the tests, and with a dump of the native methods it also checks the addresses of the patched methods. No other mod is needed to build.

Release notes: [CHANGELOG.md](CHANGELOG.md). Model sources and notes: [model/MODEL.md](model/MODEL.md) (OpenSCAD, units in mm).
