# Hookreview Telescopic Mast 0.7.1

## 0.8.1

Geen hookwijziging: 24 callbacks op 14 targets, bytecode-metadata vergeleken met de ongewijzigde 0.8.0-release. Nieuwe code betreft de beheerde editor, templates, dezelfde read-only loadsampler en DetailedLogging. Resterende risico's blijven 14 high (native lifecycle/state), 3 medium (fire/icon), 7 low; live saving en beweging blijven apart te testen.


## 0.8.0 phases A and B

The hook set is unchanged: **24 callbacks on 14 targets**, with identical owners, target declarations, callback kinds, parameter names and types versus the phase-A snapshot. `tools/Check-Hooks.ps1` (also run by `Verify.ps1`) checks the exact list of declarations. All 14 current RVAs/Lengths remain unique (`tools/Check-HookRvas.ps1`).

The existing `HingeCoreHooks.LoadData` ComponentID filter additionally admits **sprocketMastPiston, sprocketMastSleeve, sprocketTelescopeBaseModel, sprocketTelescopePartModel**. `ToBlueprint` still merges keys in place through `StateMerge`; it adds `ioPartId` and `ioFlowV1` and preserves the original `hingeIoV1` string. No native target, Harmony signature or new callback was added. API load/save callbacks run from `HingeHost.Update`, graph evaluation from the existing `LateUpdate` tick. The link engine remains only as a backwards-comparison oracle, outside runtime vehicle state.

Residual risk stays the previously reviewed lifecycle/native writes (14 high), `FireInternal` hot path (2 medium), icon result (1 medium), and seven low-risk tracking/read paths. Mast persistence extends the data handled by existing lifecycle callbacks. A managed build and serialization tests do not prove actual native blueprint saving, clean reload, destroyed-part discovery or transform behavior; those remain unverified in game because this build must not be installed. Phase B adds only managed panel/editor code, with the public Kit 0.1.9 graph API; the same risks remain.

Read-only review for 0.7.1; the table below lists every changed hook. Source: every `[Harmony*]` attribute in `*.cs` (excluding `tests/`).
Evidence: `tools/Check-HookRvas.ps1` and `tools/Check-Hooks.ps1`. They re-read the current `Cpp2IL/cpp2il_out` `AddressAttribute` data, optionally compare it with an earlier `all-native-methods.json` index (`-ReferenceIndex`), check the current `GameAssembly.dll` hash and the exact callback set in the built DLL.

## 0.7.1 result

24 Harmony callbacks on 14 native targets (was 30 on 17). Every remaining target has a unique current RVA (0 shared addresses), no constructor/empty-`void` target, and no callback parameter that is a value type or by-ref. `FireInternal` now has a prefix and a postfix, both taking only `CannonBehaviour __instance`; the pair compares the native `ProjectileRegister.nextID` counter around the call and keeps the 150 ms duplicate filter. The `WeaponFireInfo __result` read is gone. Risk totals: **14 high, 3 medium, 7 low** (was 17 high, 6 medium, 7 low).

| # | Hook | 0.7.1 change |
|---:|---|---|
| 4 | `HingeCoreHooks.Tick` on `VehicleBehaviour.GameUpdate` | **Removed.** `HingeHost.LateUpdate` now ticks every registered vehicle (AI included) after native Update. |
| 13 | `HingeFireHooks.BeforeFire` prefix on `FireInternal` | **Restored as a read-only sample.** Takes only `CannonBehaviour __instance`; stores `ProjectileRegister.nextID` in a `[ThreadStatic]` field. No return value, struct or finalizer. |
| 14 | `HingeFireHooks.AfterFire` postfix on `FireInternal` | **Counter comparison.** Reads the same class-object counter again; only a changed value counts, then the 150 ms duplicate filter. `WeaponFireInfo __result` is no longer read. |
| 15 | `HingeFireHooks.FireDone` finalizer on `FireInternal` | **Removed.** |
| 19 | `HingeLaunchHooks.BeforeLaunch` on `ProjectileRegister.Launch` | **Removed.** No launch means no `nextID` change; the class-object counter pair is the cancellation signal. |
| 20 | `HingeLaunchHooks.AfterLaunch` on `ProjectileRegister.Launch` | **Removed.** |
| 25 | `Hooks.Input` on `VehicleController.UpdateControl` | **Removed.** `MastDriver.Update` moves the local player's masts; the player vehicle is read-only from the cached local `VehicleController.ControlledVehicle`. |
| 26 | `Hooks.AfterVehicleUpdate` on `VehicleBehaviour.GameUpdate` | **Removed.** `MastDriver.LateUpdate` runs `MastRuntime.RefreshAll` after native processing. |
| 1-3, 5-12, 16-18, 21-24, 27-30 | all other callbacks | **Unchanged** (same owner, target, kind and callback parameters). |

## 0.7.0 baseline

**Result:** 30 Harmony callbacks on 17 native targets. Every target has a unique current RVA (0 shared addresses), and every current RVA/Length still matches the old index. There is no constructor target and no empty-`void` target. The callbacks themselves declare only class parameters (`__instance`, `__0`, `__result`); no callback declares a `ref`/`out` or value-type parameter. `ProjectileRegister.Launch` (3 by-ref structs) and `CannonBehaviour.FireInternal` (hot fire path) are still listed as medium risk because the detour sits on that path. Risk totals: **17 high, 6 medium, 7 low** (23 of 30 touch a rule-7 risk category).

| # | Hook owner.callback | Target; kind; callback parameters | RVA / Length | Shared RVA | Class | Risk in ordinary words | Safer alternative |
|---:|---|---|---|---|---|---|---|
| 1 | HingeCoreHooks.BeforeBuild | `VehicleObjectModel.Build`; prefix; `(VehicleObjectModel __instance)` | `0x209DE90` / `0x9A8` | none | High | Replaces mesh/collider setup for mast/hinge parts on every model build. A mistake can affect rendering or collision on a shared lifecycle. | Do the mesh work from a part-specific load event/`OnModelLoaded` after the native build; keep `Build` untouched. |
| 2 | HingeCoreHooks.AfterBuild | `VehicleObjectModel.Build`; postfix; `(VehicleObjectModel __instance)` | `0x209DE90` / `0x9A8` | none | High | Writes mass, cost, colliders and COM state immediately after every build. Bad state can stick to a blueprint. | Recompute own parts from a unique part callback, or validate the values in `OnModelLoaded` only. |
| 3 | HingeCoreHooks.Loaded | `VehicleObjectModel.OnModelLoaded`; postfix; `(VehicleObjectModel __instance)` | `0x209D760` / `0x17A` | none | High | Calls `ForceRebuild` while a model is loading; a re-entrant or timing error can duplicate/replace renderers. | Subscribe to the part's own load-finished event and rebuild once, outside the callback. |
| 4 | HingeCoreHooks.Tick | `VehicleBehaviour.GameUpdate`; postfix; `(VehicleBehaviour __instance)` | `0x1FCCF30` / `0x20F` | none | High | Runs every vehicle frame and writes leaf transforms (native state) from the I/O engine. Bad math or a callback can move physics objects. | Tick the engines from the existing `HingeHost`/`MastDriver` MonoBehaviour and iterate registered vehicles; no `GameUpdate` patch. |
| 5 | HingeCoreHooks.Physics | `VehicleObject.EnablePhysics`; prefix; `(VehicleObject __instance)` | `0x1FD6070` / `0xDF` | none | High | Captures the rest pose during the physics transition for every vehicle object. A wrong pose is baked into the moving assembly. | Capture the pose from the part's own enable/physics event, or lazily on the first tick after `PhysicsEnabled` changes. |
| 6 | HingeCoreHooks.Designer | `VehicleObject.DisablePhysics`; prefix; `(VehicleObject __instance)` | `0x1FD6150` / `0x115` | none | High | Writes the leaf back to its rest transform before leaving physics. A bad write can leave the design displaced. | Restore from `HingeHost.LateUpdate` when the designer scene is active, using a saved rest pose. |
| 7 | HingeCoreHooks.SaveState | `VehicleTransform.SaveState`; prefix; `(VehicleTransform __instance)` | `0x1FE53A0` / `0x252` | none | High | Writes the leaf pose to rest while native state is being serialized. Saving is a bad place to mutate transforms. | Serialize the logical hinge angle/height in our own state and rebuild the pose on load; do not move transforms during save. |
| 8 | HingeCoreHooks.BeforeBlueprint | `VehicleObjectSerialization.ToBlueprint`; prefix; `(VehicleObject __0)` | `0x1FEC120` / `0x5BC` | none | High | Same native-state write as #7, now on the outer blueprint path. | Same as #7; or a unique caller that passes a logical pose into the blueprint. |
| 9 | HingeCoreHooks.Save | `VehicleObjectSerialization.ToBlueprint`; postfix; `(VehicleObject __0, VehicleObjectBlueprint __result)` | `0x1FEC120` / `0x5BC` | none | High | Replaces `__result.State` with a fresh `SerializationInfo` and adds `hingeIoV1`; a collision with another mod's state key could drop data. | Extend the existing state through a narrower API/unique caller, or at least merge without replacing the whole object; verify keys. |
| 10 | HingeCoreHooks.LoadData | `VehicleComponent.LoadData`; postfix; `(VehicleComponent __instance, SerializationInfo __0)` | `0x1FD1820` / `0x75` | none | Low | Read-only lookup of `hingeIoV1`; does not alter native state. | No change needed; optionally read from one unique component caller. |
| 11 | HingeCoreHooks.Release | `VehicleObject.Release`; prefix; `(VehicleObject __instance)` | `0x1FD7680` / `0x8C` | none | Low | Removes managed dictionary entries; no native write. | Weak references/pruning in the host loop would avoid the hook. |
| 12 | HingeLoadBackupHooks.LoadState | `VehicleTransform.LoadState`; postfix; `(VehicleTransform __instance, SerializationInfo __0)` | `0x1FE5600` / `0x2E9` | none | Low | Read-only backup reader for `hingeIoV1`; no native write. | Keep as backup, or remove once the `LoadData` path is proven for every load route. |
| 13 | HingeFireHooks.BeforeFire | `CannonBehaviour.FireInternal`; prefix; `(CannonBehaviour __instance)` | `0x210E320` / `0x6EC` | none | Medium | Detour on the hot firing path; reads a class-object counter before the original. Another firing mod may skip/alter the original; a skipped postfix leaves no sample because the next prefix overwrites it. | Prefix/postfix counter comparison (0.7.1), or no `FireInternal` hook; never read a return struct. |
| 14 | HingeFireHooks.AfterFire | `CannonBehaviour.FireInternal`; postfix; `(CannonBehaviour __instance)` | `0x210E320` / `0x6EC` | none | Medium | Runs on every shot, reads the same class-object counter and cannon/vehicle state; a throw is caught but the hook is still hot. | Counter-comparison postfix with the duplicate filter (0.7.1), or no `FireInternal` hook. |
| 15 | HingeFireHooks.FireDone | `CannonBehaviour.FireInternal`; finalizer; `()` | `0x210E320` / `0x6EC` | none | Medium | Finalizer on every firing call; a finalizer ordering error can leak the thread-local frame stack. | Remove if #13/#14 are not needed; otherwise use `try/finally` inside one postfix. |
| 16 | HingeFireHooks.Built | `Cannon.Build`; postfix; `(Cannon __instance)` | `0x2030C50` / `0x497` | none | Low | Adds a cannon to a managed dictionary; no native write. | Scan the vehicle object reader for cannons once per membership refresh. |
| 17 | HingeFireHooks.VehicleBuilt | `Cannon.OnVehicleBuilt`; postfix; `(Cannon __instance)` | `0x20310F0` / `0x6F` | none | Low | Same managed tracking at vehicle-build time. | Same scan/poll alternative. |
| 18 | HingeFireHooks.Released | `Cannon.Release`; prefix; `(Cannon __instance)` | `0x202FF60` / `0x12F` | none | Low | Removes a dictionary entry; no native write. | Prune dead entries while scanning. |
| 19 | HingeLaunchHooks.BeforeLaunch | `ProjectileRegister.Launch`; prefix; `(ProjectileRegister __instance)` | `0x1EE52D0` / `0x744` | none | Medium | Sits on the projectile hot path whose signature has three by-ref structs; callback does not touch them, but any detour there is sensitive. | Use one `Launch` postfix to observe `nextID`/count, or infer cancellation from `FireInternal` result and drop this pair. |
| 20 | HingeLaunchHooks.AfterLaunch | `ProjectileRegister.Launch`; postfix; `(ProjectileRegister __instance)` | `0x1EE52D0` / `0x744` | none | Medium | Same hot/ref-struct path; only reads register counters. | Same as #19. |
| 21 | IconHooks.Card | `PartDefinitionCardFactory.CreateCard`; postfix; `(PartDefinition __0, PartDisplayCard __result)` | `0x200FA10` / `0x171` | none | Medium | Sets `__result.Icon` for our GUIDs; UI creation is broad and another icon mod may own the same result. | Use the native part icon/asset field if exposed, or a unique definition-specific caller. |
| 22 | Hooks.BeforeBuild | `VehicleObjectModel.Build`; prefix; `(VehicleObjectModel __instance)` | `0x209DE90` / `0x9A8` | none | High | Mast mesh/collider preparation on every model build; same shared-lifecycle risk as #1. | Move to `OnModelLoaded`/part load event, or a dedicated mast-part callback. |
| 23 | Hooks.AfterBuild | `VehicleObjectModel.Build`; postfix; `(VehicleObjectModel __instance)` | `0x209DE90` / `0x9A8` | none | High | Mast mass/COM/cost writes on every build; bad values can persist in blueprints. | Recompute only own parts from a unique load callback. |
| 24 | Hooks.Loaded | `VehicleObjectModel.OnModelLoaded`; postfix; `(VehicleObjectModel __instance)` | `0x209D760` / `0x17A` | none | High | Calls `ForceRebuild` for mast parts during model load; same re-entrancy risk as #3. | One rebuild from the part's load-finished event. |
| 25 | Hooks.Input | `VehicleController.UpdateControl`; postfix; `(VehicleController __instance, GameTime __0)` | `0x1F5C4F0` / `0x890` | none | High | Moves masts while control input is processed; writes native transforms every tick. | Poll the two Keybinds in `MastDriver.Update` and move masts there, outside `UpdateControl`. |
| 26 | Hooks.AfterVehicleUpdate | `VehicleBehaviour.GameUpdate`; postfix; `(VehicleBehaviour __instance)` | `0x1FCCF30` / `0x20F` | none | High | Fixes sleeve position after native processing; writes a model position each vehicle frame. | Do the sleeve refresh in `MastDriver.LateUpdate` for registered mast vehicles. |
| 27 | Hooks.Designer | `VehicleObject.DisablePhysics`; prefix; `(VehicleObject __instance)` | `0x1FD6150` / `0x115` | none | High | Retracts a mast before leaving physics; native transform write during a transition. | Retract from `MastDriver` when the designer scene becomes active, with a saved logical height. |
| 28 | Hooks.Physics | `VehicleObject.EnablePhysics`; prefix; `(VehicleObject __instance)` | `0x1FD6070` / `0xDF` | none | High | Mutates collider flags and traces every object before physics. A broad hook can affect unrelated parts if the ownership check ever fails. | Apply the safety flags in the part's own build/load path, not on every enable. |
| 29 | Hooks.Save | `VehicleTransform.SaveState`; prefix; `(VehicleTransform __instance)` | `0x1FE53A0` / `0x252` | none | High | Retracts a mast during native save; saving should not mutate live transforms. | Store mast height logically and reconstruct; leave transforms untouched during save. |
| 30 | Hooks.Release | `VehicleObject.Release`; prefix; `(VehicleObject __instance)` | `0x1FD7680` / `0x8C` | none | Low | Removes a managed mast entry; no native write. | Prune dead entries in the host loop. |

## Reading the classes

- **High (14):** #1, #2, #3, #5, #6, #7, #8, #9, #22, #23, #24, #27, #28, #29 — callbacks that write native transforms, colliders, mass, renderers or serialized state on broad/lifecycle paths.
- **Medium (3):** #13 and #14 on the hot firing path, and #21 on a broad UI result.
- **Low (7):** #10, #11, #12, #16, #17, #18, #30 — read-only/tracking/cleanup callbacks.

No hook was added, removed or changed in 0.7.0. The runtime work added for the public API is managed only: part discovery via the existing object reader, `loadRules` from `HingeHost.Update` (not a Harmony hook), and `apply` from the I/O tick. The new `vehicle.braking` input is a read-only powertrain poll with no patch.

0.7.1 removed six callbacks (#4, #15, #19, #20, #25, #26), narrowed #14 to a counter-comparison postfix and restored #13 as its CannonBehaviour-only prefix; that pair replaces the former `WeaponFireInfo __result` read. No other hook was added, removed or changed. The remaining residual risks are the lifecycle/native-write hooks (#1-3, #5-9, #22-24, #27-29), the `FireInternal` hot path (#13/#14) and the icon result (#21); neither fire callback takes a value-type or by-ref parameter.
# 0.9.0 rotator extension (2026-11-08)

The same 24 callbacks on 14 targets remain. Attributes and signatures are checked against the original release; current/old native RVA, length and ICF uniqueness checks remain mandatory. Existing callback bodies also dispatch owned rotator models through preparation, Balance, physics rest capture, designer/save restore and release. LoadData admits `sprocketRotatorBaseModel` and `sprocketRotatorPlateModel`; LoadState is the existing backup. No added Harmony patch or changed signature.

HingeHost drives simulation and seating. Runtime pose writes are the same absolute VehicleTransform Position/Rotation plus NotifyTransformChanged used by the Leaf; no parenting, native scale or Rigidbody writes are introduced. Mesh-only Z compensation preserves circular geometry.

Rotator Balance reuses owned-component mass, cachedMass, material/assembly cost and RecalculateCenterOfMass calls. Motion requests component COM refresh at 10 Hz while rotating and once at a stop, via a per-vehicle gate. The game's own VehicleCenterOfMassVehicleModule.FixedUpdate remains native; changing its global scheduling or writing its native fields would exceed the permitted state scope. The mod does not claim to cap unrelated native physics COM updates.

Native child movement, actual saved blueprint payload/reload, camo and 1/4-rotator performance remain unverified until the v0.9 TESTPLAN is performed after installation by the director. Approved models/icons are copied unchanged; no Kit or game files are modified by this build.

