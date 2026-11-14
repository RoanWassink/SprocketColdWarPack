# Sprocket Oscillating Turret

Adds oscillating upper turrets to Sprocket: a compact elevation drive that tilts a whole upper turret with the gun, and a Heavy Trunnion Mount with a stronger elevation motor.

- Version: 0.3.1
- Requires: no other plugin (runs on BepInEx 6.0.0-be.788, IL2CPP)
- Optional: none. Other mods look for this one: Sprocket Expanded Ballistics (its ATGM mount preset needs an active Oscillating Turret) and Sprocket Stabilization (it uses the `oscillatingElevationDrive` part tag)
- Plugin GUID: `sprocket.oscillatingturret`
- Game version tested: 0.2.56.2

## Features

- "Oscillating elevation drive" part (available from 1952.01.01): a compact elevation basket. Place it upright on an azimuth turret, attach the upper turret to it and set the elevation arc in the native motor properties. It uses the assigned native turret motor and the gun-layer crew.
- A generated "Oscillating" turret preset, merged into the game's own generated-turret preset tab (also from 1952.01.01). A preset is not offered on a vehicle dated earlier.
- "Heavy Trunnion Mount" (mantlet plus laying drive, from 1952.01.01): built on the native trunnions at scale 1.5, elevation +25 / -10 degrees, no azimuth. Elevation torque is doubled and the top speed stays native.
- The Heavy Trunnion Mount adds 250 kg, costs twice the native mechanism price of that mass, and allows the laying drive 1.5 m from the trunnions instead of 1 m. All four values are config entries.
- Own render-only model for the Heavy Trunnion Mount (frame, trunnion block and a pinion that turns with the elevation) and its own picker icon.
- Optional compact pivot housing: an enclosed horizontal pivot and actuator model instead of the exposed elevation ring and visible basket (native motor, axis, mass and cost stay).
- Guard for the X-ray view (key O): the native up axis is restored when the plugin loses ownership of a turret, so the upper turret no longer rolls onto its side.
- An imported vehicle with a too-early drive keeps the part but leaves the turret native, with one warning in the log.

## Settings and keys

Config file: `BepInEx\config\sprocket.oscillatingturret.cfg`, created on first start. This mod has no key bindings of its own.

| Section / key | Default | Meaning |
|---|---|---|
| `General / Enabled` | `true` | `false` skips all patches (set before start). |
| `Visual / RotateRingModel` | `true` | Roll the visible ring model so its hole faces the elevation axis. |
| `Visual / RingModelRollDegrees` | `90` | Roll of that model; 0 leaves it untouched. |
| `Visual / CompactPivotHousing` | `true` | Use the enclosed pivot and actuator model (restart to change). |
| `Heavy Trunnion / TorqueFactor` | `2` | Elevation torque as a multiple of the native drive. |
| `Heavy Trunnion / ExtraMassKg` | `250` | Mass the Heavy Trunnion Mount adds. |
| `Heavy Trunnion / CostFactor` | `2` | Cost multiple on the native price of that mass. |
| `Heavy Trunnion / MaxDistanceFactor` | `1.5` | Allowed drive-to-trunnion distance as a multiple of 1 m. |
| `Diagnostics / DetailedLogging` | `false` | One log line per X-ray switch, per correction and per build. |

Shipped data: three part definitions (`oscillatingElevationDrivePart.json`, `heavyTrunnionMantletPart.json`, `heavyTrunnionDrivePart.json`) and two English localization files in `Sprocket_Data\StreamingAssets`; the plugin folder is `BepInEx\plugins\SprocketOscillatingTurret\`.

## Status

Version 0.3.1 was installed on 2026-11-08 and has not been tested in the game yet; this covers the X-ray fix, the Heavy Trunnion Mount and the release-hook change. The one recorded game test (0.2.10, X-ray view) failed, which led to the fix. The whole pack was just renamed and has not been re-tested in the game yet.

## Building

.NET project in `src\` (`SprocketOscillatingTurret.csproj`, net6.0); build with `dotnet build src\SprocketOscillatingTurret.csproj -c Release -p:GameDir=<path to Sprocket folder>`. `check.ps1` runs the build and the tests, and with a native-methods dump also the native-address audit of the patched methods. No other plugin is needed to build.

Release notes: [CHANGELOG.md](CHANGELOG.md). Model sources and notes: [model/MODEL.md](model/MODEL.md) (OpenSCAD, units in mm).
