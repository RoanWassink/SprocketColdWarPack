# Changelog

## 0.3.1 - 2026-11-08

- Move the component cleanup prefix from the shared, empty `VehicleComponent.Release()` native stub to the unique `VehicleComponent.ReleaseInternal()` lifecycle caller. The cleanup callback and fallback sweeps are unchanged.
- Add a source-derived ICF test with current interop inheritance/overload resolution, native address checks across assemblies and negative controls. DLL-only update; no ring, Heavy Trunnion, configuration, identity or asset changes.

## 0.3.0 - 2026-11-03

- New part **Heavy Trunnion Mount** (mantlet `58b7e71b-b383-4782-ac62-d5b0873688c1`, laying drive `5fd35c60-7493-42aa-a065-5910f3280868`, tag `heavyTrunnionDrive`), available from 1952.01.01. Native trunnions at trunnionsScale 1.5 stay the build surface; elevation +25/-10, no azimuth.
- Stronger, not faster: elevation torque x2.0 (config `[Heavy Trunnion] TorqueFactor`). Native MaxSpeed is 180/ratio and never uses the torque, so the top speed stays native.
- +250 kg (`ExtraMassKg`, never below 0), cost 2.0 x the native mechanism price of that mass (`CostFactor`, never below 1), drive-to-trunnion distance 1.5 m instead of 1 m (`MaxDistanceFactor`).
- Render-only OpenSCAD model in three meshes (frame, trunnion block, pinion); the pinion turns -3.75 x the elevation. Native trunnion and drive renderers are hidden, their colliders stay. Own picker icon.
- `[Heavy trunnion]` diagnostics behind `Diagnostics.DetailedLogging`: one line per build and one per drive enable.
- Includes the unreleased 0.2.11 X-ray fix. The oscillating ring path is unchanged.

## 0.2.11 - 2026-11-02

- Fixed the X-ray (O) roll: when an owned behaviour loses its registry entry, the plugin now restores the native up axis before native Apply can run. Native Apply with the plugin's local -X axis produced the 90-degree rolled upper turret.
- Ownership recovery attempts are now keyed by behaviour and transform, so a rebuilt transform gets a fresh recovery window instead of inheriting an exhausted counter.
- Added `Diagnostics.DetailedLogging` (off by default): one line per overlay switch and one line per unclaimed Apply correction, with ring id, X-ray state and the rotation before/after.

## 0.2.10 - 2026-11-01

- The oscillating elevation drive part and generated turret preset are dated 1952.01.01.
- The generated preset is not offered on an earlier designer vehicle, and an imported too-early drive keeps its part identity but leaves the turret native, with one warning.
- The exact final-era sentinel 9999.12.31 remains available.
