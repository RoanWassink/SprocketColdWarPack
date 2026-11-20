# Guided ATGMs

SACLOS uses the controlled vehicle's sight or reticle. MCLOS uses W/S for vertical and A/D for horizontal steering, temporarily blocking vehicle commands while guidance is active. Top attack locks a target with an optional keybind, cruises above it and dives steeply. Choose your camera view before firing. AI retains native target selection and uses missile lead. Only the newest command-guided missile per vehicle receives player commands. Locked top-attack missiles remain independent after launch, including when another missile fires or the launcher is destroyed.

Use `behavior: atgm` for launcher missiles and `behavior: atgm_gun` for gun-launched missiles. Guidance mode (`sight`, `keyboard` or `topattack`) is independent of launch type. Both use the existing HEAT impact/air-gap model and optional configured tandem stages, with calibre-scaled chemical penetration. Top attack tracks a designated surface rather than a simulated seeker sensor and does not use wire transport.

Motor settings are per-profile. Fresh examples use 50 m/s fixed launch speed, 150 m/s² acceleration after a 0.12 s delay, 200 m/s maximum flight speed, 4 s motor burn and 1 m/s² coast loss. The gun-launched example uses cannon speed × 0.2 and a 0.08 s motor delay. They are editable gameplay values. Native gravity and drag apply before ignition; powered guidance runs before native movement/collision. Flame, light and smoke are linked to motor ignition.

[Custom ATGM field ranges and formulas](CUSTOM-SHELLS.md#custom-atgms) explain all motor/guidance settings. [Launcher and finite ammunition setup](README.md#dedicated-launcher-and-ammunition-box) covers the native gunner, one ready initial missile and optional automatic reserve box.

## Top attack

`topattack` climbs above a designated point, cruises toward it and commands a steep terminal dive. The turn-rate limit still applies: the chosen dive angle is a steering target, not a guaranteed impact angle at very short range. The reference example is a gameplay profile, not a named missile specification.

Bind **Sprocket Expanded Ballistics / Lock top-attack target** in Sprocket Keybinds (unbound by default). Hold the key with your reticle on the target for **TopAttackLockSeconds** (default 1 s, range 0.1–5). A compact HUD shows **LOCKING**, the progress percentage and **LOCK READY**. Acquisition selects the nearest non-trigger external hit within 4,000 m and excludes the launching vehicle's hull and compartment roots. Changing to another target resets the dwell; movement of the same target is tracked in its local space.

Release after completion and fire within 30 seconds. Exactly the next successful top-attack launch from that vehicle consumes the lock. The missile follows the acquired surface while it exists, then retains its last valid point. It needs no further sight commands and is not cancelled by a later launch or launcher destruction. AI performs one physical acquisition from its current aim ray at launch and follows the same path. A missile launched without a valid lock stays straight; acquiring a lock later cannot redirect it. Wire transport is incompatible with this fire-and-forget mode and is rejected in profile validation.

## Optional settings

In `BepInEx/config/sprocket.expandedballistics.cfg`, `[ATGM guidance] Enabled` controls powered guidance. If disabled or unavailable, missiles retain native ballistic launch with their chemical impact. `DiagnosticLogging` defaults to false and enables detailed commanded/native movement reports. `TopAttackLockSeconds` defaults to 1 (0.1–5 s) and sets how long the top-attack lock key must stay on a target.

`[ATGM Launcher Tests] InitialReadyMissile` defaults to true and `OpticalSightDirection` to false; these section/key names are retained for existing configuration compatibility. The automatic ammo-box feed is `[ATGM ammo box] AutomaticFeed`, default true since 0.22.89 (the old `[ATGM Launcher Tests] AutomaticAmmoBox` is no longer read). Restart after edits.

Audio is separate for launcher and gun-launched missiles. `[ATGM Audio] Enabled` defaults to true, `VolumeMultiplier` is 0–2 (default 1). Optional PCM16 WAV overrides belong in `BepInEx/config/sprocket.expandedballistics.audio/atgm.wav` and `atgm_gun.wav`. Native positional attenuation/mixer processing remains in use. See [audio provenance](audio/README.md).

## Updating

Existing tuned profiles and saved IDs remain intact. Missing `[ATGM guidance]` values are seeded from the older `[ATGM Experimental]` section, including disabled guidance, diagnostics and lock dwell. Existing `[ATGM guidance]` values take precedence. Missing motor fields receive constant-speed equivalents rather than soft-launch values. Configure motor fields explicitly to adopt the examples. Keep a matching DLL and configuration backup when reverting.


Top-attack lofting needs enough stand-off for both the climb and the bounded pitch-over. The stock 200 m/s, 90°/s, 60 m-altitude profile conservatively requires about 390 m on level ground; height and initial alignment change this. A closer or misaligned locked target uses **Direct approach** instead, with a visible HUD notice. It still steers with the real turn-rate limit and must physically collide; very close, fast-moving or obstructed targets can still be missed. The terminal turn uses actual height and current pitch, including altitude overshoot, rather than assuming a level missile at exactly the configured altitude.
