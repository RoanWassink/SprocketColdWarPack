# Chemical impact identity, speed and obliquity

October 16-24 findings for .56.2. Read alongside [residual state](residual-ballistics.md); these repairs do not close every HEAT continuation branch.

## Diagnose before changing warhead strength

`heat`, `atgm` and `atgm_gun` normalize to the same chemical impact family. Chemical initialization derives a virtual jet from immutable launch profile and ChemicalBudget, using incoming velocity for direction. The reviewed model uses 5% bore diameter and 5% carrier mass. ChemicalInitialized prevents refilling; tandem precursor/main have separate contexts and budgets.

Actual helper tests with identical warheads at 20/50/200/1000 m/s produce the same initial jet. Native damage later uses remaining simulation-fragment mass and speed (`0.5*m*v^2` in the inspected GetDamage path). This is not the carrier's pre-impact speed. Constructor tests cannot detect a missing impact context before the constructor runs, and do not prove collision reachability.

Compare equal reference caliber, actual caliber, budget, angle, geometry and traversed layers. A 600 mm @135 mm profile on 125 mm gives 555.56 mm; 600 mm @100 mm gives 750 mm. A native label can fall back to a definition name and does not prove impact-profile lookup survived.

## Per-shot release is not register teardown

Generated `ProjectileRegister.Release(int)` releases one projectile; `DestroyAll()` ends the register/session. The former global ClearEraTypes hook incorrectly removed the whole register from cached Types, DamageFactors, ImpactProfiles and type-recovery metadata on every Release. An unrelated projectile could erase a slow missile's launch metadata while it was still flying. The eventual impact then used a tiny native carrier penetration budget and deflected without chemical initialization.

The minimal repair moves whole-register cleanup to DestroyAll, retaining per-flight cleanup at Release. Preserve Users membership across shared types/registers and remove metadata only after the last real user. Test slow missile + unrelated release, shared/unrelated registers, repeated teardown, same-frame impacts and tandem. This is a proven ownership defect with causal fit to saved failures; the exact unrelated Release sequence was not captured in those old reports. Do not turn this into a universal no-ricochet gate or synthetic hit.

## Native angular penalty is separate from normalization

Current native Penetrate initializes the original projectile profile with obliquity exponent about 1.33. SimulateFragment computes a `cos(angle)^exponent` penalty, relaxes it by overmatch, and compares effective budget to actual line-of-sight span. Maximum normalization zero does not remove that penalty. A 439.318 mm base budget at 75.955 degrees can become about 66.816 mm before overmatch, facing a 309.036 mm path. Displayed base penetration alone does not prove it should pass.

The scoped chemical correction zeroes only this exponent during the identified custom original HEAT SimulateFragment call: valid positive chemical budget, OriginalPenetrator, profile index0, source material -1, AP flag1. Preserve passive LOS resistance, material work, remaining energy, direction and genuine stops. Exclude vanilla, HESH, APFSDS, spall and non-original/invalid inputs. Save/restore the float in existing prefix/finalizer scope, handle nesting, and explicitly write the value-type profile back to its IL2CPP array both times. No unsafe new byref hook is required. Native outer-loop serial child processing was audited; source/managed isolation checks are not steep-angle gameplay acceptance.

## Gaps, geometry and reports

The existing .35-per-caliber gap curve beginning beyond .1 caliber is a separate gameplay calibration. It can dominate loss over long internal air travel; do not silently change it while repairing guidance or angular policy. Reported sequential before/after losses are already included in later residuals; do not subtract them twice or advertise them as independent RHA thickness.

October16 native samples isolated two first-block stops with about401.5 mm ray span and no ERA/gap/material subtraction. Other shots penetrated the front and damaged internal components before stopping. Combined-block membership does not locate exact entry/exit surfaces; require VUIDs, flags and intersection geometry before claiming a geometry correction. Small measured casing paths refute a blanket assumption that every ERA housing is treated as solid steel.

Bounded chemicalGeometry/chemicalGap records may precede layerStart and have HitId=null. Do not assign them causally to the previous UI plate. Match exact simulation, stage, budgets and provenance; warning caps can hide later events. A later session without rebound does not close the older unmapped 0x181 or 8.673->18.497 mm branch.

Evidence: `outputs/atgm-heat-regression-20261020/REVIEW-NL.md`, `outputs/atgm-bounce-20261020/HANDOFF.md`, `outputs/chemical-obliquity-review-20261020/REVIEW.md`, `outputs/live-heat-followup-20261016/REVIEW-NL.md`, `outputs/lofting-review-20261020/REPORT-FINDINGS.md` in the original integration workspace. Native addresses must be re-resolved against its recorded GameAssembly hash.
