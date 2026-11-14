# 0.1.2
- Native upright/automatic placement, air placement and gizmo rotation retained.
- Approved board geometry rotated into local YZ. No suitable native plate found;
  native ForceRebuild is called after replacing render/interaction/collision meshes.
- Live GetModelScale in LateUpdate for known boards; cached transforms, no steady
  frame allocations or writes when scale is unchanged. Panel dimensions use Z/Y.
- Two picture quads 3 mm in world units above the solid native board; letterboxing
  is the exposed backing, without another almost coplanar slab.
- Designer discovery at 1 Hz, timed once. Pickup no longer evicts unseen boards.
- H3 only, session memory, panel, paste, GUID and dependencies retained.
- Offline checks are separate from pending live editor and collision acceptance.

# Reference Images 0.1.1 - 2026-11-06
- Fixed part deserialization: model info v0.2, removed associatedComponentFileID; mass 0 and damageable false.
- Added exact native root/component version-schema checks and a regression fixture for the rejected 0.1.0 JSON.

# Reference Images 0.1.0 - 2026-11-06
- Added a zero-mass model-only Reference Image part, approved OpenSCAD board and embedded icon.
- Added native-tool placement/scaling, projected two-sided pictures, per-board Mirror and letterboxing.
- Added a Designer UI Kit panel, PNG/JPG library and main-thread PNG/DIBV5/DIB clipboard import.
- Added unbound Reference Images / Panel and Reference Images / Paste actions via Keybinds.
- Added RAM-only assignment matching by native vehicle identity and local transform; no blueprint or save/load calls.
- Added one narrowly guarded CreateCard icon postfix; current-game hash gate and ICF audit.
- Added deterministic asset tools, offline tests and an actual-DLL interop scan.
- Live gates: native identity across reopens, model/collider lifetime, HDRP appearance, native mass/cost/scaler and clipboard integration.
