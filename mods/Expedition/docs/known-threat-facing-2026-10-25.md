# Known-threat turret orientation

Status: installed with backup while Sprocket was closed; native gameplay acceptance
pending. Receipt: `artifacts/install-campaign-20261025-234620/journal.json`.
Installed hashes, unchanged configuration, original root/executable ACLs and Medium
integrity labels were verified.

The existing firing hold already requests native hull facing above 12 degrees of
deviation and settles within 6 degrees before halting. This presents the hull
front toward a visible firing target. Moving tanks retain native route steering
so a competing stationary turn task does not block their advance.

The new addition pre-orients idle native gun layers during advance/search and
the acquisition delay. It uses only the unit's actual visible target, the team's
last-known/frozen opposing-spawn position, or the uniquely identified boss's
explicitly authorized private live-player pursuit objective. Ordinary lost
contacts are snapshots, never retained live target transforms.

The verified native AimAtDirectionTask(Vector3, float) emits direction-only
instructions. A zero completion tolerance retains the bearing while the owned
task is needed. Its world direction is updated at the existing 4 Hz AI scan.
Only otherwise idle gun layers receive it. Existing native aiming tasks retain
priority; owned pre-aim tasks are finished and removed before an actual attack.
No gunner/fire permission, ammunition or physics transform is modified.

Manual orders, player control handoff, unusable hull removal, optional-controller
failure, session release and encounter reset all release the owned tasks.
A pre-aim exception disables just this optional addition for the affected unit.
AI path samples include preAimLayers so the live path can be distinguished from
an unrelated native aim task. Physical motors, their limits, native pathfinding
and current firing tasks remain authoritative.

Evidence: installed-game hash matches the qualified .56.2 GameAssembly; generated
interop signatures and private local native instruction analysis confirm task
shape and direction-only output. Campaign and proof builds pass with zero
warnings/errors. Existing CampaignContent checks, including all six era distances,
pass. These checks do not prove the native animation or T-84 firing issue fixed.

Source backup: `artifacts/known-threat-facing-before-20261025-234446`.
Builds: `bin/KnownThreatFacing`, `bin/KnownThreatFacingProof`.
Package: `artifacts/known-threat-facing-package.json`.

User test: while AI tanks advance toward a known threat, watch whether their
turrets point toward it. Once stopped and engaging, watch whether the hull
turns to present its front. Manual control should remain native. The pending
T-84 ally firing test and weapon samples remain part of this build.
