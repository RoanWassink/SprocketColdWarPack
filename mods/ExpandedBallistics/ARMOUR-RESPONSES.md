# Armour materials and reactive protection

## What to install

For active armour interactions use **Expanded Ballistics** (the shell module and the material module are one plugin) together with the Cold War core, with the response catalogue enabled. The full Cold War pack configures this combination. Without the shell module's impact hooks the materials give passive properties only, not an active ERA effect. Do not load a heavyEra catalogue into a plugin build that predates it: its parser can reject the whole catalogue.

Edit `BepInEx/config/sprocket.expandedballistics.responses.json` with the game closed and restart. The standalone example is disabled (`enabled: false`); enable it only after installing the matching consumers. The pack explicitly enables its clean starter catalogue. Updating DLLs does not overwrite an existing catalogue or automatically append a new recipe. Back up and merge the new entry, retaining your root settings and customized old recipes.

## Choosing an armour recipe

| Recipe | Purpose and limits |
|---|---|
| Glass/textolite | Additional reduction for a rod already disturbed by supported upstream steel; not a universal bonus against an intact APFSDS rod. |
| NERA sandwich | Reusable, angle-dependent HEAT interaction; it does not explode. Full added response at 30–60 degrees from the plate normal; none head-on. |
| Light ERA | One-use cells against every original projectile at or above the 20 mm trigger calibre. HEAT pays the configured heatRetention; kinetic rounds (APFSDS/APDS/AP/APHE/APCBC) detonate the cell with no additional penetration effect. 15–60 mm normal cassette thickness; nominal HEAT retention 0.62 at 0–60 degrees. |
| Heavy ERA | Kontakt-5-inspired gameplay proxy. Use 70 mm for an initial test; only 60–80 mm normal thickness is eligible. HEAT retention 0.50 at 0–60 degrees; APFSDS keeps the rod recipe; APDS pays half of it (`shortRod` 0.5); AP/APHE/APCBC make the cell detonate without any effect on the penetration (default `kineticFullBore` 0). Vanilla AP does not enter this code and does not set off ERA. HESH and HE detonate without an extra penetration effect. |
| Passive composite | Threat-dependent additional protection with bulk/weight/cost tradeoffs, not real layer-by-layer ceramic fracture. |

Angles are measured **from the plate normal**: 0 degrees is a straight-on hit. Values are retention of remaining penetration capacity, not a fixed amount of RHA protection, damage reduction, or proof that a shell will be stopped. Angular weighting and global caps apply. Cassette thickness is measured normal to the surface, not line-of-sight depth. Outside eligibility limits the material keeps its passive properties but loses the extra response.

Both ERA kinds divide the surface into 25 cm cells. The first intact cell reaction consumes it, and later hits in that cell receive passive protection only. Nearby cells are separate. The 0.22.12 trigger uses the gun bore for mod profiles (not the APDS/APFSDS penetrator core) and the native fragment diameter for full-bore rounds; below 20 mm the cell stays intact and only native armour applies. A cell spent against a kinetic round can never pay again against HEAT. Preview state is separate from live combat. Permanent spent-cell blueprint persistence is not promised.

Reactive hits request a small native explosion visual. Flying tiles, explosion area damage, chain reactions, real rod rotation/fracture and tandem-charge protection are not implemented. These coefficients are **gameplay calibrations**, not exact Kontakt-5 specifications or universal historical performance. The whole-pack policy exposes its Cold War repertoire from the era start; this is not a claim that every technology existed in 1945.

At 70 mm, heavy ERA has density 4000 kg/m³ and passive RHA factor 0.55: a 1 m² cassette weighs 280 kg and its passive normal resistance is 38.5 mm RHA-equivalent. An equal 785 kg comparison adds 505 kg of RHA backing. That complete array has approximately 2.52 times the reference RHA material cost; assembly costs are separate. Do not compare equal thickness and call it equal weight. A cassette usually needs backing armour.

## Adding your own material: complete examples

`examples/myHeavyEra.json` is a complete native Technology definition. Copy it to `Sprocket_Data/StreamingAssets/Technology/myHeavyEra.json`. `examples/myHeavyEra-response.json` is a complete response entry: append it to the `responses` array in your backed-up catalogue. It is **not** a complete root catalogue. Use `examples/sprocket.expandedballistics.responses.json` only for a clean installation/reference.

The native `type` must equal the response's `compatibleMaterialIds` entry. Each responseId and bound material ID must be unique. Never bind the protected `rha` or `sheetMetal` IDs. Keep a saved vehicle's IDs stable; changing a label is safer than changing its identity. Edit values in the native Technology file and matching `passiveMaterial` together: mismatched density/RHA/spall causes the extra response to be skipped. For an unchanged clone, keep all curves and geometry from the example.

| Field | Units / supported values | Use |
|---|---|---|
| Technology `type` | Unique material ID | Save identity and recipe binding. |
| Technology `date` | yyyy.MM.dd | Native availability; Cold War core is needed for the new era. |
| `density` | kg/m³; response parser 100–25000 | Physical weight; 4000 for the heavy example. |
| `rhaFactor` | Dimensionless; 0.001–5 | Passive resistance per thickness; 0.55 for heavy. |
| `spallFactor` | 0–1 | Native spall scaling; 0.25 for heavy. |
| `costMultiplier` / `requestedCostMultiplier` | Nonnegative, response upper bound 1e12 | Requested price; balance floors may raise it. Example 10.5 is not 10.5 times RHA, whose effective multiplier is 2. |
| `heatRetention`, rod retentions | 0–1, constrained by global loss caps | Lower retention increases the extra response; full heavy defaults 0.50/0.85. |
| Root `maximumAdditionalHeatLoss` | 0–0.60 | Cumulative extra HEAT loss cap. |
| Root `maximumAdditionalKineticLoss` | 0–0.35 | Cumulative extra kinetic loss cap. |
| Root `minimumTriggerCalibreMm` | mm; default 20 | Minimum gun calibre that detonates a cell. Below it, fragments and small rounds stay native-only. |
| `threatScale` (ERA only) | Optional object, buckets 0–1 | Live per-bucket factor on the heavy rod recipe: `chemicalJet`, `longRod`, `shortRod`, `kineticFullBore`, `hesh`, `he`. Missing keys keep 1/1/0.5/0/0/0 for heavy and 1/0/0/0/0/0 for light, so by default AP/APHE/APCBC detonate a heavy cell and consume it without affecting the penetration (`kineticFullBore` 0); vanilla AP does not enter this code and does not set off ERA. The installer merges defaults without overwriting a user value. |
| Thickness limits | mm; 0.1–1000, max >= min | Reactive eligibility, heavy defaults 60–80. |
| Curve angle / weight | Degrees / 0–1 | Sorted response weights. HEAT uses angularCurve; heavyEra additionally requires kineticAngularCurve. Old kinds must not include that field. |
| `cellPitchM` | Metres; strict ERA bounds in validator | Finite cell size. Keep 0.25 for these ERA recipes; changing pitch can change cell identity/spent behaviour. |

Only the existing kinds glassTextolite, nera, lightEra, heavyEra and passiveComposite are supported. Do not invent kinds, keys or hidden shell physics. Every response carries a real availability date (`minimumDate: "1985.01.01"`) plus matching `historicalDate` metadata; the native Technology date, `minimumDate` and the owning vehicle date are the gate. An old `minimumEra: "coldwar"` file still loads once as `minimumDate: "1945.09.03"`. The complete example carries the required remaining fields and defaults. Unknown fields, duplicate IDs, invalid curves, trailing commas in the response JSON, or a recipe outside the root loss caps can disable the optional response catalogue. Native Technology files permit trailing commas; use strict valid JSON for all examples and config edits.

## Update, recovery and uninstall

Back up the catalogue and native Technology files before changing them. Preserve root enabled/caps/preconditioning and existing entries when adding heavy ERA. Restart both consumers after edits; a material-side reload does not reload Shell's startup catalogue. Check LogOutput.log for skipped recipes, unknown IDs and parser errors. Restore the paired DLLs and pre-update catalogue together for rollback. Before uninstalling, replace custom materials on affected vehicles with stock materials and save; keep originals/backups if you want to return later.
## Native verification in 0.12.4

Heavy ERA against HEAT is verified in both Sample and Play, including same-cell consumption and new-Play reset. The lookup repair also serves the other recipes; their individual native behavior is not yet equally verified. Additional protection remains conditional on valid thickness, angle, native technology and matching passive recipe. Repeated Sample tests use separate preview cells and do not spend combat cells.
