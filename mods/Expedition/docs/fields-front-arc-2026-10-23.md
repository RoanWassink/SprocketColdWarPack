# Fields front-arc trial — 23 October 2026

User request: keep the working upright spawn correction, but scatter enemies in
the forward half-circle at 350–500 m. This replaces the earlier Fields era-distance
trial only; Defence and Dunes keep their native layouts.

Each enemy marker receives an individual randomized position. The planner checks
350–500 m horizontal range and the forward half-plane against every possible
native friendly start marker, since the native spawner chooses the player's
marker. Enemy markers stay at least 24 m apart. The encounter plan hash seeds the
layout: different encounters vary; retrying the same plan repeats the geometry
if terrain validation is unchanged. With few enemies, every sector need not be
occupied.

The proven scoped SetupGame prefix and native team-role resolution remain. All
positions must pass the existing terrain raycast, slope and obstacle checks before
any marker moves. Native ground clearance is retained; rotations are upright and
face the friendly start. The planner has bounded attempts, layout retries and a
4096 terrain-probe limit. If no complete safe layout fits, original markers remain
and the HUD/log reports the fallback. Session cleanup restores original positions
and rotations. No save/catalogue identities, armies, costs or offers are rewritten.

Managed checks pass: distance/front bounds across player anchors, 24 m spacing,
terrain vetoes, atomic failure, deterministic retries, different encounter seeds,
impossible layouts and a 32-anchor wide-grid stress fixture. CampaignContent tests
pass (existing net6 support warning). Campaign and Core-free proof builds compile
with zero warnings/errors. These are geometry/build checks, not a live terrain or
native spawn acceptance test.

Installed with Sprocket closed, backup and preserved config:
`artifacts/install-campaign-20261023-210926/journal.json`.

- Game SHA256: `27D1766EA8751DDB6D4C0551D356153428BAF6441BE3E5AC3D18A8A3065CB590`
- Core SHA256: `B22D7F532B3EBADC037CFBBC2C8683F062EB5A7AAD4E103497DC6B290268AC4A`
- Accepted upright baseline is backed up and its earlier output package retained.

User performs gameplay validation: start a Fields encounter, check that enemies
start upright, separated and ahead, and note the initial nearest-enemy distance.
Use a different encounter to assess layout variation. Native front-arc acceptance
is pending; Codex does not launch or control the game.

## Native failure and encounter-count correction

The user reports `Fields trial unavailable`; music is confirmed good. Local log
shows the scoped prefix ran and preserved 3 m native clearance, but failed to find
a complete layout. It fell back to the native markers; actual nearest enemy was
1852.8 m with 1 friendly and 2 enemies. This is a geometry failure after the hook,
not the old team-resolution/hook failure.

The planner demanded all 32 enemy slots while constraining them against all 32
possible friendly starts. The actual Fields friendly rows are much longer than
the initial synthetic grid. A private local read of both native formations
reproduces 0/24 complete 32-slot layouts even with all terrain assumed clear.
For both formations, 2-slot and 12-slot layouts pass 24/24 seeds and retain all
350–500 m/front constraints. The native coordinate export stays local and is not
included in packages or external model context.

The correction reads the exact enemy quantity from the current locked native
lineup, plans only that many markers (1–12), and temporarily reparents unused
enemy markers into an inactive owned holder. The same native SpawnPoints locator
must report the requested capacity before spawning continues. Native random slot
selection is temporarily disabled because the planned positions are already
randomized. All original parents/sibling indices and selection settings restore
on fallback or cleanup. Friendly markers remain unchanged. Upright rotations,
original clearance, spacing and terrain vetoes remain in force.

Geometry attempts now accommodate the smaller feasible region of long friendly
rows. New diagnostics distinguish geometry, spacing and terrain rejection and
report best placed count. A long 32-anchor, two-row synthetic regression checks
2 and 12 enemies over 24 seeds. CampaignContent and both builds pass (the console
test retains its existing net6 warning). The correction is installed; native
capacity/slot consumption and terrain acceptance still need the user's test.

Installed with game closed and backup:
`artifacts/install-campaign-20261023-213141/journal.json`.
Game SHA256 `5183BC5F477D3B9E70E468CA19E5DB546F5FF7D4C0964172D438D90896C4EFD2`.
Core and music manifest retain their prior hashes. The accepted soundtrack and
the prior installed DLL/config/assets are preserved in the install journal.
