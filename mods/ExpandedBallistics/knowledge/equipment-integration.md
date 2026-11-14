# Sights, ranging, FCS and native drive integration

Observed October6-14; native addresses are build-qualified. Use the current module source and installed part catalogue before applying provisional dimensions, costs or historical dates.

## Device versus sight

A fitted laser rangefinder is separate equipment. Resolve it on the controlled vehicle through that vehicle's gateway/ObjectReader; it enables measurement through any active owned sight. Do not require a special rangefinding sight or globally scan all tanks.

Manual measurement reports a range; it does not automatically apply gun elevation. An automatic LRF/FCS is a different capability. A measured number with no elevation correction can simply mean the player installed or selected the manual device. Check device type before blaming another scope plugin.

Use the native active sight/lens and aim point for a world ray. Ignore triggers and the firing vehicle hierarchy, choose the nearest valid external hit, then apply device range/rounding/cooldown. Filtering a too-near hit must not reveal an object behind it. Manual, automatic and optical devices have distinct health, timing and accuracy contracts; provisional gameplay values are not sourced equipment specifications.

Optical coincidence equipment uses effective physical baseline. The reviewed design defaulted to1m; readout steps were deterministic100m below2m baseline,50m from2m and25m at3m. Native health, physical scale, current catalogue and version still govern. This is a gameplay quantizer, not an optical instrument simulation.

## Automatic elevation

October22 repair supersedes convergence-only assumptions: driving could erase elevation after a one-shot update. Retain the measured native distance and issue ongoing gravity-aware gun-layer requests for the exact linked player cannon using current ray/muzzle and a platform-relative reference. No separate native gravity-zero setter was proved. The initial convergence-only draft was rejected, not installed. Thermal0.2.9 source/managed checks cover ownership and lifecycle, but moving/no-new-mouse-input accuracy, no look-drag/compounding, manual override and device/sight/vehicle teardown remain native test gates. See integration workspace `outputs/fixes-20261022/{CURRENT-STATE.md,TEST-GUIDE-NL.md}`; do not rebuild an old thermal release over this repair.

Reuse native `SetGunConvergance` and the native gun-layer aiming path, scoped to the player's exact linked cannon/sight. The reviewed premium solver calls native `Ballistics.CalculateInterceptPoint` using the actual muzzle position and current cannon muzzle velocity. An early prototype assumed stationary targets and did not model drag, dispersion or moving-platform correction; do not advertise pinpoint moving-target fire control from that helper.

The external sight linked to the console must also be the sight actually assigned to the cannon. A console-to-sight link does not select it as the cannon's AssignedSightVUID. This explained a thermal-versus-TPD comparison; no separate TPD gunner reach rule was found.

## Native gunner reach and console routing

In .56.1, `Cannon.UpdateAssociatedSight` and `UpdateSightTransform` convert the assigned sight eyepiece into cannon-local coordinates and update `Cannon.Operable.operateLocalPosition`. `MarkModified` triggers native operator revalidation; it is not just a rendering dirty flag. Native eye reach remained1m in the inspected paths. Crew validation uses the operated target and limb-specific limits, not a special thermal/TPD classification.

A remote sight console moves that exact cannon operation endpoint to the installed same-vehicle console. It does not bypass reach, role, health, posture or crew efficiency. Sight-to-console distance may be unrestricted while gunner-to-console remains native. Resolve missing/duplicate/foreign VUIDs conservatively; removing a valid link must restore the original endpoint. No global crew-validity patch is appropriate.

Sensor profiles belong to their physical sight/sensor; consoles control links. Do not accidentally transfer sensor electronics mass/performance ownership to every console. A decorative screen or glass pane is not an actual live thermal display or functional sight.

## Persistence and remapping

Native outer `ToBlueprint` bypassed the expected public component `SaveData` hook. Persist console links through the actual object-state serialization path. Native string object-state entries were supported. On structured duplication/instantiation, remap VUIDs through authoritative component ownership.

Clear destination links before parsing the batch. Reject poisoned duplicate source/destination IDs permanently for that batch; do not leave old raw IDs after failed remapping. Filter source envelopes against components actually owned by that source. Reapply endpoints only after native assignment/load completes. Do not fix serialization by silently reassigning the player's cannon.

Live user feedback confirmed console operation and later link retention on their tested vehicle; exhaustive foreign/missing/duplicate/delete/rebuild cases still require targeted checks.

## Models, materials and menus

For mixed-ammo HUD, assigned-sight magnification, WW part schema, optional Expedition picker filtering and stripped icon APIs, read [DynamicSight and WW sights](dynamic-sights.md). It records later source/installed/candidate distinctions; a successful build does not prove a generated Unity method can execute.

- Native part definitions, model render/shadow/interaction/collision groups and stable GUIDs must agree. A custom model filename is not automatically an asset GUID.
- Decorative thermal-lens/window components must not retain a functional sight component from a prototype blueprint. Older saved parts can keep obsolete components until replaced/migrated.
- Native VehicleMaterial wrappers can repaint custom glass. Reuse the accepted unpainted/material and dynamic model repair path; HDRP transparency against an opaque hull does not prove transparent rendering. Test against a visible background.
- Clear/tinted/yellow panes are decorative scalable windows, not thermal detectors or armour upgrades. Keep their independent xyz scaling and original cosmetic identity.
- Model placement surfaces belong to part/model definitions. A visual child alone does not make the underlying surface placeable.
- `SelectionPanel.SetDisplayOptions` can split tagged native OptionGroups into Sights and electronics while retaining original actions, state and delegates. Remap active indices after filtering and preserve vanilla/unrelated entries.

An early borrowed rangefinder housing was private/internal-use only. It was replaced by an original generic housing/icon before the public batch. Do not resurrect the borrowed prototype because it remains in a local output folder.

## Native stabilization

The stabilization addon edits native elevation/traverse drive parameters, not a separate ideal gyro. One elevation UI unit equals100 native multiplier in the reviewed contract. Torque improves acceleration/holding; ratio/top-speed remains a separate constraint. Increasing torque alone cannot overcome a speed ceiling. Retain native inertia, crew, damage, mass/cost and actual technology frame.

Only uniquely associated drives are editable through the extension; shared/ambiguous native designs must be made independent first. Use native edit/undo/rebuild storage. Keep imported out-of-range values visible rather than silently clamping saved vehicles. The supplied postwar date1945.09.03 is a project gameplay gate, not a historical deployment claim for every feature.

Evidence: `outputs/fcs-native-reach-audit/FINDINGS.md`, `outputs/fcs-link-repair/REVIEW.md`, FR006 stage TEST-GUIDE/MODEL-NOTES and the reviewed Thermal/STABILIZATION handoffs. Raw native disassembly remains local.

### Moving LRF solver details (October22)

SetGunConvergance stores distance; SetAimPoint computes a point at that distance. Inspected AimAtPosition/ExecuteUpdateEnd did not itself prove gravity compensation. Reconstruct each solution from the current lens plus normalized **raw** player AimRay times retained range, current muzzle and actual muzzle velocity; use negative inherited platform velocity for a stationary-world launch-frame reference. Never feed the previous corrected target/ScopeAimPoint back into the solver or write camera direction. The native intercept helper models constant velocity and gravity, not drag/motor/dispersion or sensed target tracking.

Require current exact own device/sight/scope/cannon, healthy cannon, supported unguided context, native readback and ready/enabled/nonfailed measurement state. Disabled/failed context must discard automatic attribution; transient pause/focus absence can suspend it. Manual distance changes win. Actual-source repeated-output tests established noncompounding under fixed raw input with documented native doubles; they do not prove camera/no-mouse driving accuracy in game.
