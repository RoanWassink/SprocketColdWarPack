# Native eras and price-based offers — 21 October 2026

User rule: more expensive native enemy designs count as more difficult, combined
with quantity. New runs progress WWI -> Interwar -> Earlywar -> Midwar -> Latewar
-> Coldwar, three wins per era. Enemies retain their own native creation dates.

## Implementation

- Pure cost profile: formation strength is sum(CheckedPrice * count). It rejects
  a catalogue whose ThreatScore differs from the checked price. A bounded seeded
  candidate frontier chooses strictly increasing real costs for the three risks.
  It preserves exact offer hashes/IDs, 12-enemy cap, retry and restore behavior.
- Native-cost target floor: where discrete prices collapse the authored targets,
  the first/second/third attainable positive formation costs establish minimum
  ceilings. A single real design can therefore produce 1/2/3-copy choices. This
  is recorded starting balance, not hidden damage/armour/HP scaling.
- Native cards come from Expedition Spawns plus bundled native vehicle blueprints.
  The faction overrides the same named model in the same era. Static bundled guns
  and firing-range targets are excluded from this tank pool. Actual native era
  classification and scalar saved design prices are read; no Custom Battle
  resource vector is flattened or guessed. Native definitions are not rewritten.
- New runs receive six actual native era references, a WWI starter, native price
  budget and final live player quote. Initial budget uses the available WWI sample
  and a measured live starter floor; no flat 100-price/200-budget fixture path.
- Each selected enemy resolves by its exact content ID and immutable byte hash.
  The old modulo/index-based lineup remains only for historical fixture saves.
  Player and enemy dates are checked before the native battle can load. Missing
  content/another era fails explicitly instead of substituting a tank.
- Each build boundary uses the native era-end date for the player's owned editor
  and its technology limit. Advancing a player's run-owned copy to the next era
  does not edit named designs or any enemy's date. A later-era player design is
  rejected when captured for an earlier-era encounter.
- Offers show real tank names, low/mid/high saved-price class within their era,
  per-tank price, quantities and total enemy price. Classes are labels; the
  gamemaster uses actual total prices.
- Catalogue versions fingerprint exact design bytes, prices and era references.
  Native profiles use candidate qualification and one real Defence map; no claim
  of the release minimum of three checked maps/four designs per era is made.

## Checks

Managed at initial candidate: 28 run + 106 encounter + 30 persistence + 18
presentation tests pass; controller integration passes. Both native campaign and
proof-only builds compile with zero warnings/errors. Existing net6 console tests
emit their prior framework-support warning.

## Native validation

Initial test install: Game `6DA039B9...`, Core `B22D7F53...`, journal
`artifacts/install-campaign-20261021-220610/journal.json`. Configuration preserved
byte-for-byte and previous DLL pair backed up. Native evidence is appended below
as observed; builds/managed tests alone do not establish gameplay acceptance.

Observed on the installed pair:

- New Game loaded NTL Light in the real WWI editor. Native live starter quote
  was 9254.097 and B0 was 21391 (two saved-price WWI tank candidates available).
- Local catalogue coverage was WWI 2 / Interwar 5 / Earlywar 4 / Midwar 4 /
  Latewar 5 / Coldwar 1. These are candidates, not release-qualified designs.
- Round 1 Standard selected one B4 Bratten, saved native price 12140, own date
  classified WWI. Fresh Custom Battle registered 3/3 actual spawns, two friendly
  tanks and one enemy. Native player CompleteMission produced one victory.
- The actual checkpoint contains all six native era names and native price unit.
  Round 2 offers are 1 B4 Bratten = 12140, 2 NTL Light = 18502, and one of each
  = 21391, with strictly increasing prices and quantity included in the calculation.
- The player returned to the editor, upgraded the tank, chose Cautious and
  started round 2. The exact new snapshot and live price 10361.66 were persisted;
  another fresh battle registered two friendlies and the selected WWI enemy.
- Round 2 also finished in a native victory. Round 3 cards were visually checked:
  one NTL Light plus one B4 Bratten = 21391; two B4 Bratten = 24280; three NTL
  Light = 27753. All names, prices, counts, rewards and buttons were readable on
  the ultrawide menu. The selected Standard battle registered 4/4 actual spawns,
  two friendlies and two B4 Bratten, total enemy price 24280.
- This establishes the new WWI price/era loop and editor upgrade handoff. The
  third-victory native era transition and full six-era playthrough are not yet
  claimed as live-qualified. Managed six-era boundary and 18-round tests pass.

Installed/packaged Game SHA256:
`6DA039B9BCC201E204EDE4015351C868D849B21DA0733BEC0BF92DB73B7FAB83`.
Core SHA256:
`B22D7F532B3EBADC037CFBBC2C8683F062EB5A7AAD4E103497DC6B290268AC4A`.
Configuration SHA256 remains
`86BC02A28A48CFA3CE46121CC94FCA08CAD97BEDBCBA4FB0000DB19B12132E3D`.

Follow-up: the third WWI battle ended in a third victory and persisted round 4,
Building, Interwar, budget 35936.88. The native Interwar editor was subsequently
observed with the owned NTL Light and correct technology era after loading that
slot. The misleading vanilla 150000 editor criterion is corrected in the newer
[fleet-budget build](editor-fleet-budget-2026-10-21.md); earlier package hashes
above identify the preceding price/era candidate, not that correction.
