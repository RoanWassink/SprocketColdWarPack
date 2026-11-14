# Making your own shell profiles

This guide describes **v0.21.1**. Profiles do not change the cannon's propellant setting or visible ammunition model.

## Add a shell

1. Close Sprocket and back up `BepInEx/config/sprocket.expandedballistics.shells.json`.
2. Copy an object inside its `profiles` array.
3. Give it a unique `id` and `label`. Set `behavior` to the shell mechanics you want.
4. Save valid JSON, restart, and select it on the cannon. The simulator has its own separate selection.

Use `{"schemaVersion": 1, "profiles": [ ... ]}`. Separate objects with commas, with no trailing commas or comments. Up to 32 profiles; IDs use lowercase letters, digits, `_`, `-` (1–40 characters). Labels are unique ignoring case (1–80 characters). `vanilla` / `Vanilla ammunition` are reserved. Unknown/duplicate keys or invalid values reject the file.

## Copyable APFSDS example

Add this object to the existing array:

```json
{
  "id": "my_long_rod",
  "label": "My long rod",
  "behavior": "apfsds",
  "penetratorDiameterFactor": 0.22,
  "penetratorLengthInCalibres": 6,
  "penetratorDensity": 17500,
  "velocityEfficiency": 0.85,
  "velocityMultiplier": 1,
  "maximumVelocityFactor": 2.2,
  "maximumVelocity": 2200,
  "penetrationQuality": 0.45,
  "fragmentDamageMultiplier": 0.5
}
```

**`behavior`, not `id` or `label`, activates APFSDS mechanics.** This copy gets the narrow rod cone, remaining-penetration spall, length modifiers and optional suppression of classic AP normalization. There is no need to use the old `apfsds` ID. Copy short rod with length 3 for a lighter starting point. A renamed copy with identical settings has identical calculated performance.

## Choose mechanics

| `behavior` | Mechanics | Payload settings |
|---|---|---|
| `ap` | Custom AP ballistics and native spall | No extra fields required; optional `kineticKind` and APCR carrier fields |
| `apfsds` | Rod ballistics, narrow energy-dependent cone, more spall as penetration is spent | No extra fields required |
| `aphe` | AP ballistics with a heavy broad payload burst after perforation | Global APHE spall/visual settings |
| `he` | Native impact blast | Positive `nativeExplosivePower`; `explosionScale` |
| `heat` | High chemical first-plate budget, concentrated spall, spaced-armour gap loss | Positive `chemicalPenetrationMm`, `secondPlatePenetrationFactor`, `spallMultiplier`, `coneHalfAngleDegrees`, `explosionScale` |
| `hesh` | Heavy broad spall, lower chemical budget, spaced-armour gap loss | Same fields as HEAT |

All ten numeric ballistic fields in the example remain required for every behavior. Start from the corresponding built-in example in [default-shells.json](default-shells.json), rather than changing only a label. If `behavior` is absent, legacy IDs `apfsds` and `aphe` infer those behaviors; all other IDs infer `ap`. New custom shells should always specify it.

### Kinetic kinds and APCR carriers (0.21.1)

`ap` behavior may add `kineticKind` = `ap` (default), `apc`, `apcbc`, `apcr` or `apds`. It selects the profile's native obliquity exponent and its armour threat class; it does not change the base native formula.

- `apcr` and `apds` need `"armourThreatTags": [ "shortRod" ]` so material recipes receive `shortRod` instead of `kineticFullBore`. The profile tag replaces the behaviour's default class rather than adding to it.
- APCR/HVAP may also declare a light full-bore carrier. `carrierDiameterFactor` (0.05–1.0, default = `penetratorDiameterFactor`) is the flying body's diameter; `carrierMassFraction` (0.05–1.5, default = the core mass) is a fraction of the vanilla AP mass `1.59e-5 × calibre³`. Native drag and the muzzle speed formula use the carrier; at impact the penetrator is rewritten to the core's diameter, mass and density with speed and direction retained.
- APDS normally needs no carrier: the sub-calibre core is the flying projectile, which represents the discarded sabot geometry adequately.

```json
"behavior": "ap",
"kineticKind": "apcr",
"penetratorDiameterFactor": 0.40,
"penetratorLengthInCalibres": 1.6,
"penetratorDensity": 16000,
"carrierDiameterFactor": 1.0,
"carrierMassFraction": 0.6,
"armourThreatTags": [ "shortRod" ]
```

APFSDS `penetrationQuality` is accepted but cannot raise the result: the finite eroding long-rod envelope always binds at muzzle speed, so changing quality alone does nothing. Tune APFSDS through length, diameter, density and velocity instead. A tungsten rod whose impact speed falls below about 717 m/s stays unavailable (the native eroding regime has no interface speed); the UI reports the shot as unavailable rather than silently using a smaller budget.

## Ballistic variables

Calibre is full gun calibre. Ranges are inclusive.

| Setting | Range | Interaction |
|---|---|---|
| `penetratorDiameterFactor` | 0.05–0.9 | Projectile diameter / gun calibre; mass scales with diameter squared |
| `penetratorLengthInCalibres` | 0.5–10 | Length / full gun calibre; mass scales with length |
| `penetratorDensity` | 1000–25000 | kg/mÂ³; mass scales with density |
| `velocityEfficiency` | 0.1–2 | Multiplies mass-based speed factor |
| `velocityMultiplier` | 0.1–4 | Further speed tuning before limits |
| `maximumVelocityFactor` | 1–4 | Maximum multiple of vanilla muzzle speed; values below 1 are invalid for every behavior |
| `maximumVelocity` | 100–5000 | Final absolute speed cap in m/s |
| `penetrationQuality` | 0.1–4 | Higher improves penetration via native constant; no direct speed change |
| `fragmentDamageMultiplier` | 0.01–1 | Health-damage scaling for plain `ap` only; APFSDS, APHE, HEAT and HESH keep native damage (factor 1) |

```text
diameter = calibreMetres × diameterFactor
length   = calibreMetres × lengthInCalibres
mass     = Ï€/4 × diameter² × length × density
raw speed factor = sqrt((1.59e-5 × calibreMmÂ³) / mass)
                   × velocityEfficiency × velocityMultiplier
speed = min(vanillaSpeed × clamp(raw factor, 1, maximumVelocityFactor),
            maximumVelocity)
```

Doubling rod length doubles mass. Doubling diameter quadruples mass. A heavier rod can slow down; a lighter one can speed up. The speed-factor floor is 1, so lowering efficiency eventually stops reducing speed; the absolute cap can still put speed below vanilla. Increasing speed settings has no effect once an upper limit is reached.

### Configuration errors

Every numeric setting must be finite and within its inclusive range above or in the payload table below. Ballistic limits apply to **all behaviors**, including HEAT and HESH. `maximumVelocityFactor: 0.75` is invalid even for an `early_heat` profile: the speed-factor calculation also has a lower bound of 1. Use a factor of at least 1; a lower absolute `maximumVelocity` can cap muzzle speed below vanilla if desired. This does not create velocity-independent HEAT mechanics by itself.

v0.9.8 reports the offending profile, JSON field, received value and limits, for example:

```text
Profile 'early_heat': maximumVelocityFactor is 0.75; expected 1–4 (finite, inclusive).
```

Earlier builds, including v0.9.4, instead report `Invalid APFSDS balance setting` for any behavior using the shared validator. This can come from a HEAT profile even when APFSDS profiles are valid. Invalid custom configurations are rejected and left intact; the mod does not silently replace their settings. Correct the reported field in `BepInEx/config/sprocket.expandedballistics.shells.json`, then restart the game. These messages do not relax the ranges or change the speed formula.


APFSDS also applies:

```text
rod cone modifier = clamp(1 + (5-lengthInCalibres) × 0.06, 0.7, 1.25)
K = clamp(round(vanillaK / penetrationQuality^(1/1.43)), 1, 65535)
```

Quality no longer receives an extra length multiplier. APFSDS initial native penetration is bounded by a finite eroding-phase Alekseevskii–Tate reference model, using actual rod length, density and speed. Reference steel density is 7850 kg/m³, rod strength 1 GPa and target resistance 5.5 GPa. This is a gameplay calibration of the eroding phase, not a complete physical terminal solver or a guaranteed real-world ammunition rating. The integer native K is increased only if necessary; actual mass, dimensions and speed remain intact. Armour and ERA consume the resulting native budget normally, without resetting it at each layer.

For scale, a 1250 mm tungsten rod at 1986 m/s has an approximately 1247 mm reference envelope; the actual native result can be lower. This does not impose an arbitrary 2000 mm global cap. Slow/light APFSDS outside the eroding regime is explicitly unavailable; use an AP profile for a slow full-bore projectile. A powered rod is bounded at its configured peak flight speed, a conservative launch-budget approximation that does not claim to match the eroding envelope at every lower impact speed.

Compatible autoloaders read the selected custom shell's storage dimensions. Projectile storage length is at least the actual rod length and the native three-calibre packaging allowance; the native propellant length is preserved. Carousel checks projectile and charge separately. Bustle and assisted magazines pack the complete round. A 1250 mm rod cannot fit an 860 mm projectile limit. An invalid assigned loader blocks loading until it is disabled/unassigned or the shell fits; a previously chambered round is retained. Standalone older loaders without the geometry API do not provide this custom fit check.

The armour simulator's manually entered penetration is a test input, not a prediction of achievable muzzle penetration. Check the cannon's base penetration for launch performance. Model equations: [ARL-TR-2855](https://www.govinfo.gov/content/pkg/GOVPUB-D101-PURL-gpo5744/pdf/GOVPUB-D101-PURL-gpo5744.pdf).

## Explosive and chemical variables

| Setting | Range | Meaning |
|---|---|---|
| `chemicalPenetrationMm` | 0–2000; positive for HEAT/HESH/ATGM | Initial RHA capacity at a **100mm gun**; actual capacity is value × calibre/100, bounded to 1–2000mm |
| `chemicalSource` | `tech` or `profile` / `profile` | `tech` uses the dated native `expandedBallistics_heat.penetrationPerCalibre`; only the shipped `heat` profile opts in. Any edit to a profile-source HEAT keeps its configured budget |
| `standoffFreeCalibres` | 0–10 / 2.0 for HEAT and ATGM, 0 for HESH | Gap distance at which HEAT/ATGM disruption starts, in gun calibres; HESH keeps its own steeper curve |
| `retiredAfter` | strict `yyyy.MM.dd` / absent | Last vehicle date on which the profile is offered in the choice lists. It must be later than the earliest known technology/era availability. A stored choice remains usable |
| `keepAfterRetirement` | JSON `true` or `false` / false | When `true`, `retiredAfter` no longer hides the profile: it stays in the cannon, mixed-ammunition and simulator lists as an archive option. It never bypasses the start-date technology gate |
| `secondPlatePenetrationFactor` | 0.01–1 | Lower bound of each air-gap retention curve, applied to CURRENT remaining penetration; defaults HEAT .15, HESH .10 |
| `airGapLossPerCalibre` | 0–100 | Additional gap-loss sensitivity; HEAT defaults .35, HESH 12; independently configurable per profile. Zero disables extra gap loss |
| `nativeExplosivePower` | 0–500; positive for HE | Reference native blast power at 100mm; scales with calibreÂ³ and is capped at 500. Not calibrated kg TNT |
| `spallMultiplier` | 1–12 | HEAT/HESH fragment volume/count tuning; release reference values HEAT 1.5, HESH 8 |
| `coneHalfAngleDegrees` | 1–90 | HEAT/HESH half-angle: default HEAT 8 gives 16° total; HESH 70 gives 140° |
| `explosionScale` | 0.1–3 | HE/HEAT/HESH visual asset scale, also scaled with calibre; no direct damage change |

Example HEAT extras, keeping all required ballistic fields:

```json
"behavior": "heat",
"chemicalPenetrationMm": 400,
"secondPlatePenetrationFactor": 0.15,
"spallMultiplier": 1.5,
"coneHalfAngleDegrees": 8,
"explosionScale": 0.7
```

At 100mm calibre, the stock HEAT profile has no fixed budget of its own: it reads the dated native HEAT technology (1.2 calibre in 1939, 2.0 in 1945, then 2.8 / 3.8 / 4.4 / 5.0). A custom `profile`-source HEAT uses its configured `chemicalPenetrationMm`. The previous fixed 60mm second-plate cap is removed. Gap loss now multiplies remaining native penetration using spacing and preceding LOS RHA thickness, and only starts beyond `standoffFreeCalibres` (2.0 for HEAT/ATGM): a 100mm HEAT on a 100mm gun ignores gaps up to 200mm, while larger gaps lose progressively more. HESH uses its own much steeper 12 × G curve and ignores the standoff field. See [equations](docs/SPACED-ARMOUR.md).

`secondPlatePenetrationFactor` changed meaning in v0.9.7: .15 means the HEAT curve approaches 15% retention for a very large gap, not an immediate cap of 15% of initial penetration. Multiple gaps compound. `airGapLossPerCalibre` can differ on every custom shell. Fragment energy does not reset the parent's budget; secondary fragments retain their own native penetration.

For HESH start from 140, .10, 8, 70, .65 respectively. It is a broad damage proxy that still requires perforation, not real nonperforating backface scabbing. For HE use reference power 40 and visual scale 1.5. For APHE use the built-in APHE body and tune the global `apheSpallMultiplier`, `apheConeHalfAngleDegrees` and `apheExplosionScale` in the spall config. APHE does not use these per-profile chemical fields.

Payload fragment mass scales with calibreÂ³, while counts scale with calibre and stop at native 32 per burst. APHE/HESH/HEAT retain payload fragment speed even after a barely successful penetration. No payload burst is forced through an unperforated plate.

## Tuning tips

- For more APFSDS penetration, change length, diameter, density or velocity. `penetrationQuality` cannot raise the bounded long-rod result; speed limits may make velocity tweaks ineffective.
- For long/short rod experiments, change length, then compare mass, speed, penetration and cone. Do not assume length alone guarantees better output.
- For stronger HEAT penetration, change the chemical budget. Quality/flight speed are not its impact penetration budget.
- For more HEAT/HESH damage, change spall multiplier and cone. Count can hit 32; volume still changes fragment mass.
- For stronger spaced-armour resistance, increase airGapLossPerCalibre or lower the secondPlatePenetrationFactor floor. Tune HEAT and HESH separately; this is a distance-aware gameplay proxy.
- Compare shells at equal full gun calibre. Simulator penetration is manual; chemical modes also obey their configured cap. Select the live shell separately on the cannon.

Back up config before tuning. Restart after edits and check `BepInEx/LogOutput.log` if the selector disappears.

## Custom ATGMs

Copy either ATGM example from [default-shells.json](default-shells.json), change ID/label, use `"behavior": "atgm"` for a launcher or `"behavior": "atgm_gun"` for a gun-launched missile, and choose `"guidanceMode": "sight"`, `"keyboard"`, `"topattack"` or `"none"`. IDs do not determine guidance. Sight, keyboard and topattack share the same motor controls and HEAT impact logic; `none` is powered straight flight.

| Setting | Range / default when omitted | Interaction |
|---|---|---|
| `referenceCalibreMm` | 10–500 / 100 | Calibre at which chemicalPenetrationMm applies; examples use 135 |
| `flightSpeed` | 50–1000 / 200 | Maximum powered speed, m/s |
| `launchSpeedMode` | fixed or cannon / fixed | Choose configured launch speed or native cannon muzzle velocity |
| `launchSpeed` | 10–flightSpeed / flightSpeed | Initial speed in fixed mode; still validated if supplied in cannon mode |
| `launchSpeedMultiplier` | 0.01–4 / 1 | In cannon mode, multiply native muzzle velocity then clamp to 10–flightSpeed |
| `acceleration` | 0–2000 / 0 | Motor acceleration in m/s²; zero retains initial speed |
| `motorDelay` | 0–5 / 0 | Seconds after spawn before acceleration; less than flight lifetime if acceleration > 0 |
| `motorBurnTime` | 0–60 / 0 | Seconds of acceleration after ignition; 0 means unlimited burn for legacy compatibility |
| `coastDeceleration` | 0–100 / 0 | m/s² speed loss after finite burnout; minimum flight speed 10 m/s |
| `maxTurnRate` | 0–90 / 20 | Degrees/second shared by all guidance modes; diagonal keyboard commands share the limit, and top attack uses it to size the dive arc |
| `maximumFlightTime` | 1–60 / 25 | Seconds before missile release without additional detonation |
| `guidanceDelay` | 0–5 / 0.25 | Delay before steering; must be less than maximumFlightTime |
| `guidanceMode` | sight, keyboard, topattack or none / sight | Reticle, WASD, locked cruise-and-dive, or straight flight |
| `topAttackAltitude` | 5–300 / 60 | Cruise altitude in metres above the locked point; used by `topattack` only |
| `topAttackClimbAngle` | 5–85 / 25 | Degrees above horizontal while climbing to the cruise altitude |
| `topAttackDiveAngle` | 10–85 / 55 | Steepest minimum dive angle in the terminal phase |

`topattack` uses one lock per launch. Bind **Sprocket Expanded Ballistics / Lock top-attack target**, hold over an external target until the HUD says **LOCK READY**, then fire within 30 seconds. `[ATGM guidance] TopAttackLockSeconds` defaults to 1 (0.1–5 s); missing new-section values inherit the old section. Acquisition is limited to 4,000 m, excludes your own hull/compartments and resets when the target changes. Successful launch consumes only that vehicle's lock. Later launches, sight movement and launcher destruction do not cancel a locked missile. AI acquires one physical target at launch. No valid launch-time lock means straight flight for that missile; a later lock cannot redirect it. `flight.guidance: "topattack"` is supported for explicit flight modules and architecture conversions. `guidanceTransport: "wire"` is rejected for this fire-and-forget mode. Lower turn rate or higher altitude moves the dive trigger farther out; actual dive angle remains limited by available flight distance and turn rate.

For a soft launch, add these fields inside either existing ATGM profile:

```json
"launchSpeedMode": "fixed",
"launchSpeed": 50,
"launchSpeedMultiplier": 1,
"acceleration": 150,
"motorDelay": 0.12,
"motorBurnTime": 4,
"coastDeceleration": 1,
"flightSpeed": 200
```

For top attack, add these fields (a high turn rate keeps the dive steep and the standoff short):

```json
"guidanceMode": "topattack",
"maxTurnRate": 90,
"topAttackAltitude": 60,
"topAttackClimbAngle": 25,
"topAttackDiveAngle": 55
```

For propellant-dependent launch speed, use `"launchSpeedMode": "cannon"` and e.g. `"launchSpeedMultiplier": 0.2`. A native 400 m/s cannon launches at 80 m/s; an 800 m/s cannon launches at 160 m/s; speeds above 200 m/s are capped when flightSpeed is 200. Cannon propellant/barrel/technology changes affect the native muzzle speed. The missile motor uses its own `acceleration`; it is not automatically strengthened by a longer cannon charge.

```text
fixed launch = launchSpeed (or flightSpeed if omitted)
cannon launch = clamp(native cannon muzzle velocity × launchSpeedMultiplier, 10, flightSpeed)
poweredAge = max(0, t - motorDelay)
burnAge = motorBurnTime > 0 ? min(poweredAge, motorBurnTime) : poweredAge
peak = min(flightSpeed, initial speed + acceleration × burnAge)
coastAge = motorBurnTime > 0 ? max(0, poweredAge - motorBurnTime) : 0
speed = max(10, peak - coastDeceleration × coastAge)
chemical penetration = clamp(chemicalPenetrationMm × gun calibre / referenceCalibreMm, 1, 2000)
```

Launch inherits vehicle velocity. The controller records the resulting initial world-speed magnitude, clamped to 10–flightSpeed, and applies the motor curve to that speed. Native gravity/drag can act within each physics tick; the next powered command restores the curve. This is a gameplay motor model, not thrust/mass/fuel simulation. Guidance starts independently of motor delay. Flight time with acceleration is not simply distance/cruise speed near the launcher.

Penetration does not increase with flight speed: ATGM uses its configured chemical budget and HEAT air-gap losses. Tune `chemicalPenetrationMm`, `spallMultiplier`, `coneHalfAngleDegrees` and `explosionScale` for the impact; velocityEfficiency/maximumVelocity remain validated body fields but do not control ATGM launch speed. Changing launch speed does not change the native ammunition mesh, propellant UI, storage or loading cost.

Old custom ATGMs without motor fields retain constant flightSpeed. Migration writes explicit equivalent settings and a backup. At the 32-profile limit, free a slot to add a missing example. Do not replace your whole config with default-shells.json unless you want to discard custom shells.

See [ATGM.md](ATGM.md) for controls, guidance references and rollback.

Before ignition, native ballistics are used; at ignition actual speed and heading are captured as the powered phase starts. The curve describes commanded speed, not a physical thrust/fuel model. Missing burn/coast fields default to zero and preserve legacy sustained power. Default examples use burn time 4 s and coast loss 1 m/s². Both ATGM behaviors share guidance and HEAT impact; their audio can be overridden independently.


## Retired spall fields

The following fields remain accepted by the parser for old `sprocket.expandedballistics.spall.json` files, but they have no effect and are no longer written into new defaults: `apheFragmentCount`, `apheFragmentMass`, `apheFragmentSpeed`, `apheFragmentK`, `apheFuseRhaMm`, `apheFuseDelayMilliseconds`, `apfsdsPlateDeflection`, `apfsdsDeflectionMaximumDegrees`, `apfsdsDeflectionMinimumObliquityDegrees`. The internal `ThicknessRatio` curve and `SecondPlateBudget` helper are also unused by the shipped impact path. Remove them from hand-edited files if desired; leaving them in does not change a shot.

`spall.json` now also accepts an optional `obliquityExponents` object with keys `ap`, `aphe`, `apc`, `apcbc`, `apcr`, `apds`, `apfsds`, `heat` and `hesh`. Each missing key uses its default (1.33, 1.33, 1.33, 1.15, 1.60, 1.00, 0, 0, 0). Values are validated 0–2. Vanilla AP has no mod profile and always keeps the native exponent.

Native meaning of the exponent (0.22.25, read from the game code): per plate the native budget is scaled by cos^(e−1)·(1−om) + om and compared with the plate path, where om is the native overmatch factor (high for thin plates, near 0 for thick ones). So 1 is pure line of sight, 0 only the normal thickness, and 1.33 (vanilla AP) a little more than the line of sight. The optional `obliquityCorrection` object restores the line of sight the defaults 0 of `apfsds`, `heat` and `hesh` were meant to give: after each native plate the residual penetration of the round is corrected to the native law with exponent max(e, `minimumExponent`).
- `enabled` (default true).
- `minimumExponent` 0–2 (default 1 = never less than the line of sight; 0 turns the floor off; for example 0.8 allows a little less than the line of sight on thick plates).
- `maxRatio` 0 or 1–50 (default 6): the corrected length is at most this many times what native charged; 0 = no cap.
- `excludedBehaviors` (default `["hesh"]` since 0.22.26): impact behaviors that keep the native result, from `ap`, `aphe`, `apfsds`, `heat` (ATGMs included), `hesh`, `he`. HESH physically depends more on the normal thickness than on the line of sight, so it stays native by default; `[]` corrects every eligible behavior.

Only mod rounds are corrected: vanilla AP, spall fragments and rounds whose exponent is already at or above the floor (AP, APHE, APC, APCBC, APCR, APDS, HE) are not. The native ricochet and stop decisions are never changed; when the line of sight exceeds the remaining penetration, the round stops in that plate. The combat report shows the step as "Obliquity (LOS) correction".

## Native technology availability

Your profile's `behavior` must have an available native `expandedBallistics_<behavior>` Technology record with `properties.enabled: true`. The supplied records cover all behaviors; their dates use the actual vehicle technology frame. A profile is hidden in the cannon inspector while any id in `RequiredTechnologyIds` (its behavior plus `nativeTechnologyIds`) is unavailable, and a saved unavailable choice falls back with the existing era warning. Older profiles stay visible.

The choice lists (cannon dropdown, mixed-ammunition “Add shell type” and armour simulator) also hide a profile when the vehicle date is past its `retiredAfter`, unless `keepAfterRetirement` is true. A profile already selected on that cannon or carried in its mix stays listed regardless, and a launcher-carried ATGM is only listed on an actual ATGM launcher. `ShellEraPolicy.Allowed`, `RuntimeShellEra.Allowed`, `Resolve` and `Effective` deliberately ignore retirement: a stored retired profile keeps working instead of falling back to vanilla. The 0.22.2 defaults retire APHE/APCBC/APCR after 1960, APDS after 1970, and APDS late plus APFSDS gen 1 after 1985; HE, HEAT, HESH, tandem HEAT, APFSDS gen 2/3/4 and the ATGMs have no retirement.

### Availability helper (editor backend)

`RuntimeShellEra.Availability(ShellProfile profile, Cannon? cannon)` returns a record with `Selectable`, `Reason`, `From` and `Until`. It builds the same native context as the inspector (one context per call sequence) and reports exactly one of:

| Reason | Meaning |
|---|---|
| `""` | Available and selectable |
| `From yyyy` | Not yet available; the year comes from the required native Technology date (or the optional `minimumDate`) |
| `Retired yyyy` | Past `retiredAfter`, hidden from the lists |
| `Archive (retired yyyy)` | Past `retiredAfter` but `keepAfterRetirement` is true, so still listed |
| `Launcher only` | Launcher-carried ATGM shown on an ordinary cannon |
| `Unavailable` | Missing context, disabled/unknown technology, unknown era, or the profile cannot be selected on this cannon |

`From` carries the full unlocking date and `Until` the `retiredAfter` date, both as `ShellNativeDate`. The helper does not include the “already selected” keep exception; list callers use `RuntimeShellEra.Selectable`. Technology years are read from the installed `Sprocket_Data/StreamingAssets/Technology` JSON files (falling back to the copied test catalogue); they are never hard-coded.

0.21.1 ships dated HEAT technology at 1939 (1.2), 1945 (2.0), 1952 (2.8), 1960 (3.8), 1975 (4.4) and 1985 (5.0); APFSDS at 1961, 1970, 1985 and 2000; ATGM behavior at 1956 and gun-launched ATGM at 1976, with per-missile technology ids. `expandedBallistics_atgm` and `expandedBallistics_atgm_gun` no longer share the 1945 date. The resolver is native and takes the newest record at or before the vehicle date; that resolver behaviour is still to be confirmed in-game.

Only a profile with `"chemicalSource": "tech"` adopts its active technology's `penetrationPerCalibre`. The shipped `heat` profile does; custom or edited HEAT defaults to `"profile"` and retains its own chemical budget. Editing a stock `tech` profile's label or unrelated fields no longer silently changes its penetration source. Copy the shell JSON examples under `examples/` only when desired; preserve existing customized configuration. See [technology dates](GUIDE.md#native-technology-and-dates).


Top-attack lofting needs enough stand-off for both the climb and the bounded pitch-over. The stock 200 m/s, 90°/s, 60 m-altitude profile conservatively requires about 390 m on level ground; height and initial alignment change this. A closer or misaligned locked target uses **Direct approach** instead, with a visible HUD notice. It still steers with the real turn-rate limit and must physically collide; very close, fast-moving or obstructed targets can still be missed. The terminal turn uses actual height and current pitch, including altitude overshoot, rather than assuming a level missile at exactly the configured altitude.
