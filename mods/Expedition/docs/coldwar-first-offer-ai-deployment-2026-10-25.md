# First offer, allied advance and deployment trial

Status: installed with backup after the user closed Sprocket. Gameplay acceptance
is pending the user's test. Receipt: `artifacts/install-campaign-20261025-232748/journal.json`.
Installed file hashes, unchanged configuration, original root/executable ACLs and
Medium integrity labels were verified after installation.

## Findings and changes

The reported session used the reviewed `14B546C5...` Game DLL. Its fresh native
catalogue contained WWI 22, Interwar 27, Earlywar/Midwar/Latewar 25 and Coldwar 16.
No catalogue repair or content import is needed.

Initial editor Play explicitly selected the Standard offer and immediately
started it, in every starting era. Play now quotes the tank, creates the run and
opens unselected offers. Choosing a card returns to the same editor; the next
Play starts that choice.

Both observed allies received native movement orders. Random FireSupport/Line
profiles then halted within their firing ranges. Native visibility-buffer
membership alone could retain an indefinite hold behind an obstacle. The first
ally now uses Assault, the second Line and the third FireSupport, repeating for
larger fleets. Enemy price-weighted roles, individual reactions and manual player
order priority are preserved.

Cached hull/turret-height physics rays now qualify stationary firing. A detected
contact obstructed by terrain, scenery or another vehicle instead gets a bounded
native approach waypoint. Own/target colliders and triggers are excluded. Only
already natively detected target positions are sampled. The rays approximate a
firing envelope, not exact gun ballistics or penetration. Native aiming,
pathfinding, firing and damage remain authoritative.

Fields reported a friendly marker relocation of only about 95 m. Eight proposals
now prefer 250–650 m offsets in spread directions, followed by two 125–250 m
proposals, rotation-only and exact-native fallbacks. The full friendly formation
and opposing 350–500 m front arc must pass terrain, obstacle and native map-boundary
checks together. Total terrain/sight-ray caps remain bounded. A retry keeps its
deployment identity; another encounter gets another seed.

Logs distinguish moved, rotation-only and original-anchor fallbacks and report
displacement. Initial player-control verification also logs actual player
coordinates, allowing the native hull spawn to be compared with moved markers.

## Verification and package

- Campaign/proof builds: zero warnings/errors against installed interop.
- CampaignContent checks pass, including small allied fleets, blocked-shot holds,
  displacement, spacing, bounds and retry geometry.
- Integration checks pass for schemas 2/3. All six starting eras retain unselected
  first offers and accept a non-default card. Existing item, boss, recovery,
  refit and selected-challenge checks pass.
- Only six intended native adapter/policy/deployment source files differ from
  the previous reviewed package. Core DLL, music sources and manifest match.
- Package: `artifacts/coldwar-ai-deployment-package.json`.
- Builds: `bin/ColdwarAiDeployment` and `bin/ColdwarAiDeploymentProof`.
- Source backup: `artifacts/coldwar-ai-deployment-before-20261025-232209`.

## User test

1. New Game, Coldwar: first Play opens offers; choose a non-default card, then
   verify Play starts it without another selection prompt.
2. Fields with two allies: verify they advance, halt for clear shots and seek a
   firing position when terrain blocks the contact.
3. Compare player position/orientation across two different encounters. Terrain
   may still require a nearer or original-position fallback. Retrying the same
   encounter deliberately keeps its deployment.

The agent did not launch, control or terminate Sprocket. Compilation and pure
checks do not establish native gameplay acceptance.
