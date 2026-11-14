# Map spawns and line of sight — 23 October 2026

User requested the other maps and a way to avoid immediate mutual visibility.
The 350–500 m forward arc now applies to all three current offer maps: Fields,
Defence and Dunes. This uses the proven SetupGame prefix/config/session guard.
Other maps and non-Expedition battles retain native behaviour.

Before this increment, local live evidence confirms the Fields count correction:
2/32 active enemy markers, range 354.2–460.8 m across the possible friendly starts,
initial measured nearest enemy 354.3 m. Defence still used its original layout,
with nearest enemy 828.7 m. Music is user accepted and retained.

Only the actual friendly/enemy quantities remain active in the native locators.
Friendlies use a compact cluster of existing authored positions: for each native
anchor, consider its nearest requested neighbours, select the smallest cluster
diameter, and break ties by proximity to the original group centre and index.
This matters on Defence, whose native starts span over 1 km. Taking merely the
central positions can straddle gaps and make the distance annulus impractical.
Friendly positions/rotations are not relocated. Unused markers are temporarily
parked, native random slot selection is disabled, and the locator's capacity must
match the exact requested count. Both teams' original parents, sibling indices
and selection settings restore on fallback or cleanup.

Enemy positions remain randomized per immutable encounter plan, within 350–500 m
and ahead of every selected friendly start, with at least 24 m spacing. Native
ground clearance/upright orientation and slope/obstacle checks are preserved.

The first layout pass requires physical cover between each enemy and every
selected friendly start. It tests heights 4.0, 2.5 and 1.2 m above terrain, at
centre and +/-3 m lateral offsets: nine rays per friendly/enemy pair. Every ray
must hit solid collision geometry between the endpoints, ignoring triggers and
the first/last 4 m. A low wall or narrow pole alone does not establish cover.
Terrain and building colliders can provide cover; visual foliage without a
collider cannot. This samples a tank-sized envelope, not exact blueprint meshes,
AI visibility or the complete ballistic firing solution.

Search is bounded to 512 grounded candidates and 8192 sight rays in the cover
pass. If no complete covered layout is found, a second pass retains all existing
distance/spacing/terrain requirements and explicitly reports
`open fallback (no complete cover layout)` in the HUD. Cover therefore has
priority within the requested distance; it is not guaranteed on flat open maps.
The planner does not silently enlarge the distance band. If neither pass finds a
complete safe layout, it restores native markers and reports that fallback.
No native map assets, save identities, frozen offers, costs or armies are changed.

Managed tests pass: hull/turret/flank coverage, multiple friendly origins, low and
narrow obstruction rejection, covered preference, explicit open fallback, unsafe
terrain rejection, deterministic retries and compact authored-slot selection.
Existing archive/map/geometry/music checks also pass. A private local geometry
check of both native formations on all three maps passes 24 seeds for 1/8
friendlies and 2/12 enemies with terrain assumed clear. Coordinates stay local;
this check does not establish actual collision cover or terrain placement.
Campaign and Core-free proof builds compile with zero warnings/errors; test
projects retain the existing net6 support warning.

Installed with Sprocket closed, backup and preserved config/music:
`artifacts/install-campaign-20261023-214626/journal.json`.

- Game SHA256: `C7F4FD9BA2FD9E0D1B8BA0277B39498EF7FF564083065B5EBF0964688DA10365`
- Core SHA256: `B22D7F532B3EBADC037CFBBC2C8683F062EB5A7AAD4E103497DC6B290268AC4A`
- Music manifest SHA256: `CAD9BDEC06FFD7F62368220A4BC7E3D730FC35CA289895B7E047DD05DABE2885`

Native gameplay acceptance is pending. User tests each map: initial distance,
upright/non-overlapping starts, correct friendly/enemy counts, and whether the
HUD says `cover checked` or `open fallback`; confirm actual initial visibility
matches the status. Codex does not launch/control the game.

Later local live evidence (before the unrelated Escape crash): Dunes placement
reported cover=True with 54 sight rays, 3 selected friendly starts and 2 enemy
starts. Actual roster was 5/5 spawned, 3 friendly / 2 enemy, all readable;
nearest initial enemy was 359.0 m. Initial up-dots were 0.984–0.998. This confirms
the native placement/cover-query path on that encounter, not a full human visual
acceptance of every map or a working pause menu.
