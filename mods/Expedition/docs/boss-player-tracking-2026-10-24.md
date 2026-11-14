# Boss player tracking — 24 October 2026

The uniquely identified enemy boss privately follows the current player-controlled
vehicle, even when hidden. The native vehicle-control state is read every AI scan
(0.25 s), so a manual tank handoff changes its target. With a real native sighting
the boss prioritizes that player for its existing character-specific attack. Without
one it requests a native movement order toward a terrain-qualified approach near
the live player position. Existing order throttling and movement progress/recovery
remain active; the path updates after sufficient target movement rather than being
cancelled every scan. Battery's bounded visible-threat withdrawal remains intact.

The hidden position never enters either team's sighting memory or native detection
buffers. Other enemies and allies retain existing visibility rules. No active usable
player means normal target selection. Terrain, slope and native map bounds still
qualify movement destinations; unavailable safe endpoints do not cause unsafe moves.
Ordinary battles, released sessions and ambiguous boss identities do not acquire this
ability. This uses the existing enabled Expedition team-AI controller.

Managed CampaignContent checks pass, including changed positions, exclusion of
ordinary enemies/allies, unavailable players, invalid positions and encounter release.
Campaign and proof builds pass with zero warnings/errors. Native behavior remains
for the user to test; no game process was launched or controlled.

Installed while Sprocket was closed, with DLL/config backups and verified hashes:

- Game SHA256: `7448C262AFE745166150A464C4B93F899157ABDCEADB9596E2088C209261634B`
- Core SHA256: `863113B042109166CC5F189F5AFF6B1369EEA7C5614FED18D794DFA8090AE253`
- Receipt: `artifacts/install-campaign-20261024-173959/journal.json`
- Evidence: `artifacts/boss-tracking-build.txt`,
  `artifacts/boss-tracking-proof-build.txt`, `artifacts/boss-tracking-check.txt`.

Config preserved byte-for-byte, game-root ACL unchanged, Sprocket.exe still inherits
Medium integrity. Saves and catalogue data were not changed.

User check: start a boss battle, break line of sight behind a hill or building,
then change direction. The boss should keep approaching the new position while
minions use ordinary sightings. If possible, switch to another friendly tank and
check the boss changes focus. Confirm an ordinary encounter still behaves normally.
