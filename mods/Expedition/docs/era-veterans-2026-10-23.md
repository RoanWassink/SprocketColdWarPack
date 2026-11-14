# Era-winning tanks in future enemy pools — 23 October 2026

User request: after completing an era, add the tank version that won its last
battle to the enemy spawns for that era. The implementation stores the confirmed
pre-battle blueprint of the third victory in each finite era (rounds 3/6/9/12/15/18).
It preserves exact opaque bytes, native era/date, full measured design quote,
name, winning run/attempt and completed round. It does not export battlefield
damage, the first tank used in that era, or a later editor revision.

Promotion runs only from an authoritative Loaded checkpoint with no issues,
accepted Victory, matching processed/consumed attempt, completed finite-era
boundary and confirmed design belonging to the completed native era. The stored
blueprint blob must match that design hash; the native loader verifies its own
creation era. Loss, refit, cancellation, technical failure, an ordinary victory
and endless blocks do not unlock another finite-era tank.

The independent local library lives at
`BepInEx/config/SprocketExpedition/veterans/<blueprint-hash>.json`. These are private
personal blueprint archives; they must never be packaged or sent to a model.
Each schema-1 envelope hashes its serialized payload, including metadata and bytes.
Writes are immutable/atomic with flush and readback. Repeated processing of the
same blueprint is idempotent. Corrupt records are preserved and individually
skipped on import. Failure logs without undoing/blocking the persisted victory;
initializing a slot checks the same boundary again for interrupted promotion.
No RunSnapshot schema or save identities changed.

Fresh catalogues merge library entries as `(Veteran)` candidates. Native creation
era must still match; costs/threat use the captured quote, so the existing
price-times-quantity gamemaster automatically accounts for them. An identical
blueprint already in the normal pool uses the veteran's measured quote without
duplicating its content ID. Stock/custom starters and B0 are chosen/calculated
before the merge; unlocked tanks do not change the starting economy. Existing
64-design-per-era catalogue limits remain: entries beyond capacity stay archived
and log that they were not imported. The local library itself is not discarded.

Active runs retain their pinned immutable catalogue, enemy bytes and offers.
Veterans enter **future new runs**, not already-frozen offers or existing run
catalogues. A fresh catalogue version covers the added IDs/hashes/prices; its
normal slot archive then freezes them. Missing/incompatible native content is
not replaced with another era. The result screen reports the unlocked tank or
that this exact version was already in the library. Spawn selection remains
cost-based/randomized; unlocking does not force that tank into the next offer.

Managed validation uses synthetic blueprint bytes only. A real RunDirector
plays all 18 finite victories with a new design revision per round, verifying
all six boundary captures, correct final version/quote/era, persistence reload,
byte-for-byte duplicate handling, changed-byte/wrong-era rejection, no ordinary
win/loss/technical/endless/uncommitted promotion, immutable price metadata and
isolated corruption/temp-file handling. CampaignContent passes, including prior
archive/map/cover/music checks. Controller integration passes. Campaign and
Core-free proof builds compile with zero warnings/errors; tests retain the
existing net6 support warning. No private tank was fed into an automated fixture
or external model.

Installed with Sprocket closed, backup and preserved music/config:
`artifacts/install-campaign-20261023-222920/journal.json`.

- Game SHA256: `AD690C43FD273F8CEC8323EC9CB5B86F3848875A1101C52056AD90BC411E4FFF`
- Core SHA256: `5A2C1AB786479C538D8977FCAB5385F63F003C096F8C2D7B62E85208A76BE21C`
- Music manifest: `CAD9BDEC06FFD7F62368220A4BC7E3D730FC35CA289895B7E047DD05DABE2885`

Native acceptance pending: complete an era, check the result's added/already-known
tank message, and in a future new run look for that era's `(Veteran)` in offers.
Its appearance is probabilistic; a log/catalogue read can verify availability
without requiring the next generated offer to pick it. Existing runs, music,
three-map cover/spawn policy and the detour-free pause repair remain included.
Codex does not launch/control the game for these tests.
