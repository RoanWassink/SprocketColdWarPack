# ATGM flight and tracer settings

Status: managed implementation prepared for the host compiler and test suite.
It has not been built, run or tested against the game in this source-only
packet. The trajectory checks and tracer policy checks are delivered in
`tests/AtgmFlightChecks.cs`; live evidence remains pending.

## Compatibility rule

All new fields are optional flat keys in the existing schema-1 shell JSON. A
profile with none of these keys keeps the legacy sight-ray law, the existing
motor model and the existing top-attack path. The optional global rear flare
is enabled by default for existing guided rocket types as well.
`AtgmSettings.FlightProfile` stays `null` for such a profile, so missing keys
do not silently introduce a new default trajectory.

No native hooks were added. Existing ownership, newest-per-vehicle command
guidance, input release, top-attack independence, native collision and HEAT /
tandem / ERA handling are unchanged.

## Flight settings

| JSON key | Unit | Default | Meaning |
|---|---:|---:|---|
| `guidanceLaw` | choice | `legacy` | `legacy` uses the existing projected along-ray aim. `losLead` aims on the visible sight ray at radial sight-to-missile distance plus `leadDistanceM`. |
| `leadDistanceM` | m | `25` | Fixed lead ahead on the sight ray for `losLead`; ignored by the legacy law. |
| `rangePattern` | choice | `none` | `none` keeps ordinary LOS steering. `hump` enables the gun-launched range-band pattern below. |
| `humpHeightM` | m | `1` | Small height above the sight-ray command at mid-course. |
| `humpRangeMinM` | m | `200` | Lower edge of the hump band. |
| `humpRangeMaxM` | m | `1200` | Upper edge of the hump band. |
| `humpTerminalDistanceM` | m | `80` | Distance before the snapshotted launch range over which the hump smoothly returns to LOS. |
| `humpRampDistanceM` | m | `40` | Distance after launch used to raise the hump smoothly instead of stepping abruptly. |
| `directRangeBelowM` | m | `120` | Launch ranges below this use direct steering. |
| `shortRangeThresholdM` | m | `150` | Hump-profile ranges below this may shorten the steering lockout. |
| `shortRangeGuidanceDelay` | s | `.05` | Replacement guidance delay for a short hump shot; `0` skips the steering lockout. It must be less than `maximumFlightTime`. |
| `spiralAmplitudeM` | m | `0` | Low-amplitude lateral spiral added to the aim command. |
| `spiralFrequencyHz` | Hz | `1.5` | Spiral frequency. |
| `disturbanceAmplitudeM` | m | `0` | Smooth deterministic disturbance amplitude. |
| `disturbanceFrequencyHz` | Hz | `.8` | Disturbance frequency. |
| `initialElevationDegrees` | ° | `0` | Optional launch elevation relative to the snapshotted launch heading, applied only during the initial lockout and skipped in the direct short-range fallback. It obeys `maxTurnRate`. |
| `speedCalibration` | choice | `motor` | `motor` preserves `AtgmGuidance.SpeedAtAge` and the existing launch/ejection model. `fixed` is an arcade calibration that commands `flightSpeed` after motor start. |

Validation is shared by JSON parsing, previews, modular compilation and the
runtime. Amplitudes are bounded at 2 m, frequencies at 20 Hz, initial elevation
at 45°, and the hump band must have `humpRangeMaxM >= humpRangeMinM`. A hump
pattern requires an `atgm_gun` profile with `sight` guidance; it is not target
lock, top attack or fire-and-forget.

## Launch range and range bands

At launch only, `RuntimeAtgm` snapshots a range once:

1. The exact `CannonBehaviour.Sight` scope sample, if it is fresh and carries a
   plausible finite positive `ScopeController.GunConverganceDistance`.
2. Otherwise the fresh `RuntimeAtgmAi` cached intercept distance, exposed by
   additive `TryAimRange` while keeping the existing `TryAim` signature.
3. Otherwise no range.

`GunConverganceDistance` is a verified sight-convergence value, not evidence
of a separate native FCS or laser rangefinder range dial. The HUD/scope
convergence semantics should be treated as a gameplay launch-range snapshot.
The value is never updated from hidden entity positions during flight.

With `rangePattern: "hump"`:

- below `directRangeBelowM`: direct steering;
- within `[humpRangeMinM, humpRangeMaxM]`: hump pattern;
- otherwise: normal LOS steering;
- missing or implausible range: safe direct fallback, with a concise warning;
- below `shortRangeThresholdM`: the steering lockout is shortened to
  `shortRangeGuidanceDelay`.

The hump rises toward `humpHeightM` over `humpRampDistanceM`, stays above the
sight ray, then descends smoothly to zero over the last
`humpTerminalDistanceM` before the snapshotted range. It does not turn back
after passing the range.

## Disturbances and speed

Spiral and disturbance offsets are generated from the missile's launch
identity, its own age and a per-flight orthogonal basis. The spiral is a true
two-axis circular component in the perpendicular plane, with a stable radius
equal to `spiralAmplitudeM`; the smooth noise is added separately. There is no
global phase shared by simultaneous missiles. The basis uses a fallback
reference near vertical headings so it remains finite and orthogonal. Zero
amplitudes return exactly zero, so legacy flight is byte-for-byte on the old
command path for the same inputs.

The manual/keyboard path uses the same production `ManualCommand` helper as
the managed checks: input and a time-scaled disturbance are combined before
one turn cap from the original heading. Disturbance remains present with held
input. It never aims at
a target, is forward-only and is disabled before activation, after link loss
or when another missile owns command. A zero setting returns the exact legacy
`KeyboardStep` command.

`speedCalibration: "fixed"` uses `flightSpeed` after motor start. It is an
initial gameplay calibration, not a claim of a calibrated GHPC motor curve or
real engine simulation. Launch velocity, motor delay and native ejection are
still preserved before motor start.

## Initial elevation

`initialElevationDegrees` adds an optional upward bias relative to the launch
heading snapshotted at launch. It acts only while the initial guidance lockout
is active, rotates under the configured `maxTurnRate`, and is skipped in the
dynamic direct short-range band. It is a missile-flight helper only: the native
cannon and sight are not rotated, and no independent launcher ownership is
claimed.

## Tracer settings

The optional `RuntimeAtgmTrail` keeps its procedural missile, motor flame,
motor smoke puffs and native-renderer hiding. It adds a persistent rear
guidance flare that is visually separate from the ignition-only red motor
flame. The flare is a soft billboard with a shared material, explicit
transparent settings and an `_ZTest`/depth-tested queue where the shader
exposes it. It is not a through-wall marker.

| Config key | Default | Meaning |
|---|---:|---|
| `ATGM Visuals/Enabled` | `true` | Existing whole-visual switch. |
| `ATGM Visuals/GuidanceFlare` | `true` | Enable the rear guidance flare. |
| `ATGM Visuals/GuidanceFlareSize` | `.5` | Base world size at 250 m; clamped. |
| `ATGM Visuals/GuidanceFlareMaxDistance` | `5000` | Camera-distance cutoff; covers the 4 km reference variants. |
| `ATGM Visuals/GuidanceFlareLifetime` | `45` | Maximum flare lifetime in seconds. |

Flare eligibility follows the live powered missile and its guided type, not
active command ownership: superseding the missile or losing the control link
does not extinguish the physical tail flare. The pure `AtgmTracerPolicy`
bounds world size, distance and lifetime; its rear factor makes the flare
larger from behind than head-on. Flare and missile objects are still cleaned
up on real impact, release, scene reset and reused projectile identity;
shared materials and the bounded smoke-puff pool are reused rather than
allocating material per frame. Visual failure disables only visuals, not
flight.

## Example profiles

Three appended defaults are initial gameplay calibrations, not exact GHPC
replicas. They reuse the existing 135 mm reference / 600 mm chemical warhead
calibration and do not add HEAT, ERA or damage advantages.

| ID | Calibration |
|---|---|
| `atgm_saclos_direct_v1` | I-TOW-like direct: 278 m/s fixed cruise, 6.87549354 °/s turn, 30 m LOS lead, 13.6 s lifetime. Spiral/noise are deliberately small independent gameplay approximations rather than raw reference roll/noise scalars. |
| `atgm_mclos_manual_v1` | Malyutka-M-like manual: 115 m/s fixed cruise, 11.45915590 °/s turn, 30 m LOS lead, 26.2 s lifetime for approximately 3 km straight flight. Keyboard control uses the bounded manual helper. |
| `atgm_gun_hump_v1` | Kobra-like hump: 375 m/s fixed cruise, 5.72957795 °/s turn, 200 m lead, 1 s guidance delay, direct below 1000 m, no hump below 1920 m, 4 m hump, 700 m terminal descent, configured band up to 4000 m, 10.8 s lifetime and 0.5° initial elevation. |

The measured speed, turn-rate, lead, delay and hump figures come from
`docs/CALIBRATION-FACTS.md`. Spiral/noise amplitudes are independent,
conservative gameplay approximations; the raw reference spiral/roll and
noise scalars are not copied into metre-displacement fields. Range and
arming-distance gates are not claimed beyond the implemented launch-range
bands. Lifetimes approximate reference range through time at the calibrated
speed, allowing initial ejection; they are not an exact traveled-distance or
warhead-arming gate. Hump progress is monotonic distance from the launch
position, so camera/launcher movement cannot restart a completed hump.
Existing defaults and custom presets are not overwritten.
`ReleaseProfiles` still appends only missing default IDs, and the catalogue
remains below the 32-profile cap.

## Limitations

- Independent launcher angle/sight offset is deliberately unresolved. The
  native cannon sight currently follows the cannon angle; this delivery does
  not claim a separate launcher ownership or sight offset.
- The new presets use the measured local-munition speed, turn, lead, delay and
  hump figures, but exact GHPC-equivalent flight/visual parity is still pending.
  Spiral and noise amplitudes are independent gameplay approximations.
- Initial elevation is a scoped flight-layer tilt relative to the launch
  heading; it does not rotate the native cannon or sight.
- `GunConverganceDistance` is the verified scope convergence getter; its exact
  in-game meaning for every sight mode needs host/live confirmation.
- AI range is an estimate from the cached native AI intercept direction, not a
  new entity lookup.
- Top attack is left on its existing path; the new law, hump and disturbance
  settings are not applied to top-attack flights.
- Managed helper checks do not prove native movement, collision, input or
  rendering. A host build and live test are still required.

## Live test sequence

1. Build on the host and run the main suite with the linked
   `tests/AtgmFlightChecks.cs`; record the actual exit code and test count.
2. Launch `atgm_saclos_direct_v1` at roughly 200, 500 and 1000 m against a
   stationary target. Verify convergence to the sight ray, no U-turn after
   passing, and the measured 6.87549354 °/s turn limit.
3. Launch `atgm_gun_hump_v1` below 1000 m (direct), between 1000 and 1920 m
   (normal, no hump), between 1920 and 4000 m (hump), and beyond 4000 m
   (normal). Check the 4 m mid-course height, the descent over the last 700 m,
   the skipped initial lockout below 1000 m, the 0.5° launch elevation, and no
   snap after the range is passed.
4. Launch the hump profile without a fresh exact scope sample in third person;
   verify direct fallback and the diagnostic, then repeat with a fresh scope.
5. Launch `atgm_mclos_manual_v1`; verify existing WASD MCLOS control, the
   bounded spiral/noise on the manual command, and input release on hit,
   expiry, wire loss and reset.
6. View the flare from behind, the side and head-on. Verify it is depth-tested
   against terrain/hulls, remains visible after another missile supersedes
   command or the link is lost, is distinct from motor ignition, and is removed
   on real impact, release and Play reset.
7. Confirm existing legacy ATGM profiles keep their old trajectory with the
   new keys absent, and that only one command-guided missile per vehicle is
   owned while independent top-attack flights remain unaffected.
