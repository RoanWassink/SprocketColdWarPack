# Fields distance trial and live roster — 23 October 2026

The user confirmed the added maps work, but some are too large, asked for an
enemies-alive counter, and then limited the spawn-distance experiment to Fields.
Approved formation-center targets: WWI 300 m, Interwar 450 m, Earlywar 600 m,
Midwar 800 m, Latewar 1000 m, Coldwar 1200 m. These are gameplay tuning values,
not native era dates. Other maps keep their original spawn layout.

## Actual solo spawn diagnosis

The latest actual Fields roster was one friendly and one enemy, with 2/2 spawns
registered and no unreadable vehicles. Slot 1 confirms a live player quote of
11440.32 against 21391 budget, giving floor(budget / price) = 1 total. It was
therefore a correctly budgeted solo player, not missing expected allies. Earlier
Fields runs registered two friendlies and one enemy. The current pre-trial native
start distance was not measured by the installed build; no metre estimate was
claimed from map size or appearance.

## Changes

- The HUD uses the existing host's actual living-crew roster. It shows enemies
  alive, friendly tanks including the player, actual/requested counts, spawned
  units and unreadable entries. It never grants results or spawns extra units.
- A one-time horizontal nearest-enemy measurement after the initial roster arms
  reports actual vehicle separation, separately from the requested start-group
  center distance. The HUD and local log make both values inspectable.
- Native crew reads remain conservative when an operator is unreadable: a partial
  zero-health read cannot certify the whole tank dead. Existing native mission
  and encounter generation ownership still govern results.
- A Fields-only prefix on verified DeathmatchGameMode.SetupGame runs before native
  spawning. It is scoped to the exact Expedition BattleConfig pointer and active
  session. It reads the native player team's two discrete SpawnPoints layouts,
  keeps the friendly layout, and proposes a translation of enemy markers toward
  the desired group-center distance. Marker order, spacing, rotation and team IDs
  are retained. No vehicle is teleported and no scene/terrain asset is written.
- Every proposed marker needs a downward terrain hit, slope <=18 degrees, clear
  headroom and at least 80 m horizontal separation from friendly markers. Five
  bounded directions are attempted. The whole proposal is checked before any
  marker is moved; unsupported/random-line locators or blocked terrain retain the
  original layout with a HUD/log explanation. Original marker positions restore
  on session end/new trial. A closer native layout is not made longer.
- This is an explicitly requested runtime trial, including existing saved Fields
  encounters. It does not rewrite archived catalogue/offer hashes or player saves;
  it is not a promise of identical historical spawn coordinates across mod-code
  changes. Requested armies/maps remain from the frozen encounter plan.

## Checks and build status

Native signatures were read from current installed interop: SetupGame(), native
team spawnPoints/spawner references, SpawnPoints properties, Physics.Raycast with
RaycastHit/out and QueryTriggerInteraction, OverlapBox and SyncTransforms.
Both campaign and Core-free proof builds compile with zero warnings/errors.
CampaignContent and controller integration checks pass; their prior net6 support
warning remains. These managed checks do not establish a native spawn relocation.

Initial candidate Game SHA256 `66FAAA245B28321F5873F146ACB25816C1B76FD28B25D9C95B3C8B41E3E439CF`.
Core SHA256 `B22D7F532B3EBADC037CFBBC2C8683F062EB5A7AAD4E103497DC6B290268AC4A`.
Installation and live evidence are recorded below once the game is closed and the
candidate can be tested. Native sources/research remain local.

First native result: roster/HUD worked, with one friendly and two enemies,
unreadable=0. Measured nearest initial enemy was 1932.6 m. SetupGame was too early
to resolve player spawn ownership, so the guard correctly retained native markers
and reported the fallback. It did not establish distance relocation.

The second candidate moves the prefix to the verified native Load(units) method,
before vehicle construction, and resolves player ownership from native mission
Player flags before falling back to an already-assigned player team ID.
Installed with game closed and backup, journal
`artifacts/install-campaign-20261023-203843/journal.json`.
Game SHA256 `240A3358EAE86CFC294805DD722123873B1069C583B3582153A3028DD994B241`.

Second native result: the user saw 1888 m, with local measurement 1888.2 m and
readable roster 1 friendly / 2 enemies. The Load(units) hook did not enter the
scoped placement path. This candidate also did not establish relocation.

Third candidate restores the verified SetupGame entry and resolves the scene
locator from native mission Player flags, then the unique native Attacker/Defender
role matching the requested player definition. It no longer relies on the runtime
playerTeamID already being assigned. It logs entry, team-resolution failures,
original group centers and successful placement explicitly. The user now performs
all gameplay testing; Codex does not launch/control the game for these tests.
Build passes with zero warnings/errors; terrain placement is awaiting user test.
Game SHA256 `0F50CC4A6B36606FB5E32BCA36AEFB12EF187395BCFECCDCF6435959B25FCC16`.

Third native result: the user measured 322 m but reported upside-down tanks.
Local evidence confirms the native marker centers moved from 1908.0 to 300 m,
and actual initial nearest enemy was 321.7 m; roster remained 1 friendly / 2
enemies, all readable. Distance relocation is established; placement stability
failed. The original proposal used ground +0.1 m and retained old marker tilt.

Fourth candidate measures and preserves each original marker's ground clearance,
with a 1 m minimum instead of ground +0.1 m, and sets the relocated marker upright
while retaining horizontal heading. Original marker rotation is now backed up and
restored together with position. Initial tank up-vector dot/up and world height
are logged to distinguish initially inverted assembly from later physics tipping.
This is a placement correction awaiting the user's same-encounter stability test.
Game SHA256 `1DA580982D3055E2A48D3C83AB885FDBAAA9D32DAA8BE8EFB786BCDE599902FE`.

Fourth candidate installed with game closed and prior pair/config backup: artifacts/install-campaign-20261023-205816/journal.json. User performs the next stability check.

Fourth native result: the user confirms the upright correction works. Local
observations retain 3 m original marker clearance and initial up-dot near 1;
nearest-enemy measurements were approximately 323–334 m. The user finds this too
close. The next Fields-only request supersedes fixed era distances with individual
random enemy markers in a forward half-circle at 350–500 m: see
[front-arc trial](fields-front-arc-2026-10-23.md).
