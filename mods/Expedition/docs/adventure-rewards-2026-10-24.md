# Adventure items, bosses and community pool — 24 October 2026

New native Expedition runs use snapshot schema 2: four victories per era, 24 finite fights. Existing schema-1 runs retain their original three fights per era and 18-fight progression. Nothing migrates an existing run's economics, items or enemy catalogue automatically.

## Rewards

Each ordinary offer displays its exact deterministic reward before combat. A victory adds the item to inventory; when full it remains a pending choice until you discard a chosen stored item or decline the drop. Boss victory offers three purple rewards; choose one, including a captured copy of that boss. Six stored items and three active items. Activate in the editor before choosing a challenge. Held items do not expire. Activating changes the offer generation so its budget/risk is recalculated and saved.

Blue/common lasts 1–2 victories; red/rare 3–4; purple/ultra rare 5–6. Victory ticks existing active items once before granting the new item. Refit, technical failure, suspension and recovery do not tick timers. Recovery has one charge and six-round expiry once activated. War Bonds are a fixed two-victory investment after activation; the card's original rarity duration can be longer, so its active remaining counter is authoritative for this exception.

| Item | Effect |
|---|---|
| Mass Production | Second paid tank 25% cheaper; purple 30%. Player's tank remains full native price. |
| Assembly Line | Each successive paid ally adds a 10/15/20% discount step. Combined discounts cap at 40%. |
| Elite Workshop | +20% effective fleet budget; at most two tanks including player. |
| Swarm Doctrine | Allies 20% cheaper if the native player price is at most a third of effective budget. |
| Field Reinforcements / Emergency Reserve | One free copy of the player's tank; same family, cannot stack together. |
| Lend Lease | One free pinned design of the current era. |
| Captured Prototype | One free captured pinned design. Boss reward becomes usable after advancing to that native era. Same active family as Lend Lease. |
| Salvage Rights | Each confirmed enemy defeated on a victory adds 1% of the era base budget, capped at 12 enemies. |
| Veteran Crew | Up to the previous victory's confirmed surviving AI-ally count receive 20% ally discounts. |
| War Bonds | Cash immediately for 15% of era base budget, or activate and receive 30% after two victories. |
| Operational Freedom | Consume to reroll all offers, including their advertised rewards; persists the new generation. |
| Last Stand | +25% effective budget, player alone. |
| Experimental Contract | +35% effective budget; enemy targets +20% for all offers. |
| Emergency Recovery | Rare drop type, purple, six-victory active lifetime. On force destruction choose refit/retry or permanent defeat. One charge; retry preserves exactly the selected challenge/reward. Combat retreat remains defeat. |

Every victory also permanently adds 5% of the current era base budget to earned funds. The existing supply reward (1/2/3 for cautious/standard/dangerous) still becomes an additional budget bonus at the next era boundary. Earned funds are additive, not a compounding multiplier. Free allies reserve fleet slots before paid allies; hard cap remains eight tanks including player. Discount families cannot stack with themselves.

Ordinary rarity weights by risk (blue/red/purple) are 90/9/1, 70/25/5 and 45/40/15. Recovery is an additional low-weight type that always promotes to purple; actual purple frequency is therefore slightly higher. No item is active merely because it dropped. Permanent doctrines beyond earned funds are not implemented in this milestone.

## Bosses

Fourth fight of each finite era: one next-era tank and a cost-planned current-era escort, at most 12 enemies total. Coldwar finale uses the strongest available Coldwar placeholder. Dangerous offers have an additional 35% enemy budget target; the legacy cost-risk profile also applies. No forced technology dates, hidden native stat buffs or synthetic cost changes.

Until named blueprints are supplied, placeholders are selected from available native content and labelled on the cards. Character cycle: Fortress, Hunter, Battery, Veteran. Their native firing/movement policy differs where the exact unique boss hull can be identified; no guessed binding to a random enemy. Background hunt/rally and terrain/bounds checks remain in force.

Config section `[BossBlueprints]` in `sprocket.expedition.cfg` has keys WWI, Interwar, Earlywar, Midwar, Latewar, Coldwar. Value is an exact unique blueprint name or native content ID; it must belong to the upcoming era (Coldwar for final key). Empty means automatic placeholder. Changes apply to new catalogues/runs; existing runs retain their pinned boss definitions. Community names can be ambiguous; use the content ID in that case. Winning player design remains a persistent veteran unlock after the fourth victory, while old three-victory unlocks remain readable.

## Large local community library

Importer scans the explicitly requested Downloads folder, deduplicates exact byte hashes, excludes component blueprints with no vehicle header/creation date, and preserves originals. This import found 1,037 blueprint files, including 614 distinct vehicle blueprints (~3.48 GB decimal). It does not classify era by hardcoded dates: native VehicleCard/VehicleClassifications do that at startup, using each card's own date and saved cost. Native rejection is logged and never substitutes another era. Native loading/playability of every community tank is pending user testing.

Files live in `BepInEx/config/SprocketExpedition/content-library/blueprint-blobs` with community hardlinks in the adjacent `community-blueprints` directory. Neither directory is part of the distributable mod. Pool import does not change the measured stock/faction starter baseline. Distinct designs sharing a name remain eligible; byte duplicates appear once. No 64-design-per-era truncation remains; the bounded manifest supports 4,096 total designs.

Catalogue archive schema 2 stores a small manifest pointing to shared immutable hash-named blobs. It loads only selected design bytes, checking the hash before native loading. Old embedded-byte schema-1 archives remain readable. Missing/corrupt content blocks the affected load/attempt; it does not silently replace opponents. Keep the content library when backing up saves. Do not edit hash-named files directly. Existing slot manifests do not acquire newly imported opponents.

## Validation status

Managed domain/controller checks cover 24 wins, boss era/escort/loot, stock schema-1 runs, full inventory, one-charge recovery with fresh attempt identity, timer preservation on refit/failure, exact restore and rejection of stale item writes. Campaign compilation checks installed interop signatures. Pure control tests qualify the policy, while native initial-control transfer and large community startup still need user playtesting. No automated game launch or live acceptance claimed.
