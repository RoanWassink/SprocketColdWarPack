# Enemy pursuit instead of fixed map waypoints — 23 October 2026

User report: the enemy AI always drives the same direction. The native custom battle appears to
give each AI hull a scripted map/formation position. The request was to derive the move from the
enemy spawn and then from where the player was last seen, and to make the enemies actively hunt
and destroy the player.

Status: implemented, compiled, installed and ready for live playtest. No live combat observation
of the new behaviour exists yet. This page separates that build/static evidence from gameplay
qualification.

## What changed

`CampaignEnemyHunt` is an Expedition-only controller for the enemy team in the current
Custom Battle/Deathmatch session.

- Every enemy hull is claimed once, at its own native spawn position.
- The native map's scripted movement/formation orders are suppressed for those claimed hulls
  only: a Harmony prefix on `CommanderAI.Order` drops external orders and a prefix on
  `CommanderAI.UpdateFormationTasks` stops formation followers from dragging the hull back to its
  map slot. The exact move/attack order this plugin issues is passed through by instance identity.
  The first suppressed order per hull and order type is logged, so the live log proves whether a
  map script actually tried to move that hull.
- Each hull advances from its own spawn toward the shared last-seen player position. The native
  spawn dispersion is kept: the hull's lateral offset from the enemy spawn centroid becomes its
  lane beside the target, capped at 45 m, so the formation does not collapse into a single column.
- Sightings are shared. As soon as any claimed enemy's native `visibleTargets` buffer contains the
  player's `EntityID`, the player's position becomes the new last-seen point for every enemy.
- If the player is not in sight, the hull advances to that remembered point. On arrival it
  searches deterministic rings around it (70/105/140/175 m, one narrow point at a time, eight
  seconds per step); this is the "search" phase rather than parking at a fixed waypoint.
- When a hull itself has the player in sight it receives an aggressive native `AttackOrder`
  against the player's `EntityID` (fire on the move beyond 300 m, stop to fire inside 300 m).
  Its `FireMode` is set to `FireAtWill`. The order is refreshed only when the native attack order
  is gone, or after a twelve-second safety interval.
- Native ground sampling rejects search points that are not terrain or are steeper than 25°, so a
  search ring cannot deliberately send a hull up a cliff or into empty space.
- Friendly hulls are never claimed or targeted. Enemies still fight allied tanks through their own
  native combat code when those allies are seen.

The pure decisions (deterministic search ring, per-enemy variance, order-refresh cadence, stuck
detection, horizontal pursuit distance) live in `CampaignHuntPlanner` and are checked without
Unity. The native adapter supplies visibility, ground height, pathfinding and the actual orders.

## Native API used

Signatures were read from the installed interop assemblies, not guessed:

- `Sprocket.ArtificialIntelligence.CommanderAI.Order(Order)`,
  `UpdateFormationTasks()`, `visibleTargets`, `attackOrderCts`, `oneOffMoveOrderCts`,
  `driver.CurrentTask`, `FireMode`, `CanReceiveOrder`.
- `Sprocket.Orders.Order.HighestPriority`, `OrderCancellationType`,
  `MoveToPositionOrder`, `PositionTarget`, `AttackOrder`, `AttackOrderInfo`,
  `AttackApproachType.Aggressive`, `AttackEngageMode`.
- `Sprocket.Vehicles.VehicleBehaviour.OrderReciever`, `ControlType`, `Flags`, `Position`, `ID`.
- `Sprocket.Vehicles.Spawning.VehicleSpawn.Vehicle`, `IVehicleGateway` and `VehicleBehaviour`.
- `Sprocket.EntityID.FilterMatch`, used to match a native visible target against the player's ID.

The order/attack pattern follows the public, MIT-licensed Sprocket Battle Editor's documented use
of `CommanderAI.Order` and `UpdateFormationTasks`; no Battle Editor code or native binaries are
redistributed.

## Scope and safety

- The Harmony prefixes return native behaviour whenever `CampaignNativeFlow.OwnsSession` is false.
  Normal scenarios, vanilla Custom Battles and other mods keep their own AI.
- Only commanders claimed by the current Expedition battle can be controlled. Claims are dropped
  when the hull is no longer usable or when the scene ends; `CampaignNativeFlow.EndSession`
  releases the whole controller.
- No map asset, spawn marker, mission graph, blueprint, save path or player file is changed. The
  controller only issues orders to live native hulls.
- The overlay/HUD line `AI hunt:` reports hunter count, how many currently see the player, time
  since the last sighting and order count.

## Evidence

- Campaign and Core-free proof builds: zero warnings, zero errors.
- `tests/CampaignContent`: new hunt checks pass together with archive, span, cover, music and
  veteran checks.
- Other managed suites remain green: Run 30/30, Encounters 106/106, Persistence 30/30,
  Integration passed, Presentation 18/18.
- Installed with Sprocket closed and the proof config preserved:
  `artifacts/install-campaign-20261023-233248/journal.json`.
  - Game SHA256 `E4E53CB44F9A68F7003024FF3E1206B1F80F76589CA32DD8EFCE582A067AD6E4`
  - Core SHA256 `5A2C1AB786479C538D8977FCAB5385F63F003C096F8C2D7B62E85208A76BE21C`
  - Music manifest unchanged `CAD9BDEC06FFD7F62368220A4BC7E3D730FC35CA289895B7E047DD05DABE2885`
  - Earlier 23:29–23:32 installs were superseded by this final build; their plugin hashes were
    `0AE554D7…`, `89FF9C23…` and `21E71A5D…`.

## Live test protocol

1. Start Sprocket normally and enter **Expedition → New Game/Load Game**, then a battle on Fields,
   Defence or Dunes.
2. Watch the HUD line beginning with `AI hunt:`. It should change from
   `waiting for native enemy spawns` to a hunter count and `last seen ...`.
3. At battle start the enemies should leave their spawn area toward the player instead of driving
   to their old map position. Their spacing should reflect the original spawn lanes.
4. Break line of sight and move. Enemies should continue to the remembered position, then search
   the surrounding rings rather than stopping.
5. When an enemy sees the player again, the log should show
   `Expedition AI hunt: enemy N has the player in sight ... aggressive native attack order.`
   The hull should close and fire; hulls beyond 300 m fire on the move, closer hulls stop to fire.
6. Return to the editor and start another encounter. The new battle must re-arm the hunt with a
   fresh identity; the previous claims must not affect it.

Useful local log lines: `Expedition AI hunt: armed`, `enemy N claimed at spawn`,
`first native move order`, `suppressed native ...`, `has the player in sight`, the 15-second
`AI hunt summary`, and `released`.

## Known limits

- The controller depends on the native `CommanderAI.Order` path. A hull without a commander or
  with `CanReceiveOrder == false` logs a refused order and retries; it does not invent a second
  movement system.
- Pathfinding, collision avoidance and firing remain the game's native systems. No navmesh,
  teleport or vehicle relocation is introduced.
- The initial last-seen point is the player's battle-start position until a real sighting occurs.
  That is deliberate: the map's fixed waypoint is replaced, not silently treated as knowledge of
  the player's later movement.
- The search is a local ring search, not a full map sweep. If the player moves a long way without
  ever being seen, the enemies search the last known area and then remain there.
- Live acceptance still has to confirm the map-order suppression on all three current maps, the
  attack order's actual movement, and that allies are not treated as the player.
