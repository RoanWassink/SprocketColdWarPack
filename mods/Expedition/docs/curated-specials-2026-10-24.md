# Curated bosses and yellow specials — 24 October 2026

New native runs explicitly use adventure/snapshot schema 3. Every fourth finite-era battle has **one** automatically selected curated card, fixed Standard profile/two supply. Refit in the editor and press native Play. Boss loot retains the three-purple-item choice, including a captured boss.

Unconfigured bosses use the **most expensive eligible design in the upcoming native era**, after stationary filtering; the finale uses the most expensive Coldwar design. Stable ID breaks ties. Explicit `[BossBlueprints]` names/IDs remain available for the user's later selections. The map is fixed deterministically per era; escorts are cost-planned current-era designs, at most twelve enemies total. Operational Freedom cannot reroll a boss and is retained if attempted.

Schema-1/2 runs retain their pinned rules, including old three-card boss rounds. Start a new run for the new rules. No forced reset/migration. Default-valued new metadata is omitted so old schema-2 hashes remain stable. Single boss plans support save/reload, refit and one-charge recovery with the exact same plan/new attempt generation.

## Yellow specials

Normal rounds retain three cards. An authored ~30% opportunity can replace Standard with a yellow special when the current-era cost pool supports it. Yellow is a special category; its advertised reward is guaranteed after victory and uses the existing inventory and expiry rules.

| Special | Enemies and placement | Yellow item |
|---|---|---|
| Rat Plague | 8–12 from the cheapest cost third, preferably twelve. Compact 4×3 swarm with 24 m marker spacing (~87 m group diameter). | Assembly Line: 20% extra discount per successive paid ally, combined 40% cap, three victories. |
| Steel Wall | 1–3 from the expensive cost third. Broad line, 60 m marker spacing. | Elite Workshop: +25% effective budget, max two tanks including player, four victories. |
| Crossfire | 4–8 from the middle cost third. Two separated forward flanks. | Veteran Crew: 30% discount for eligible previously surviving AI copies, four victories. |

Cost does not prove mass/speed. Policy is deterministic and independent of input order, aggregates counts, excludes conflicting IDs and guards arithmetic. Integrated specials must cost 80–125% of the ordinary Standard formation's native price×count. Unsupported events remain ordinary offers before presentation. Gold Elite Workshop honours its 25% magnitude.

Placement is whole-formation: 350–500 m forward from every friendly anchor, at least 24 m separation, native upright/height clearance, terrain/playable-area checks and preference for cover. Open fallback retains the pattern. No complete safe layout means explicit native-layout fallback in HUD/log; no assembled tanks are teleported. Native gameplay remains a user test.

## Stationary guns

A conservative name policy excludes obvious PaK/FlaK, field/AT/AA/towed/stationary guns, calibre-prefixed guns/howitzers and related artillery. It retains mobile labels (Self-Propelled, SPG, Gun Carrier/Carriage and chassis aliases), including ambiguous LeFH18B2/RZ25 names. A bare 'gun' substring is insufficient. The collection has **24 matches among 614 unique vehicle blueprints**. Files are retained; fresh pools are filtered, existing encounters are preserved. Names can still yield false negatives; concrete examples can be corrected without changing designs.

## Missing catalogue on reused slots

Read-only checks found slot 1's schema-2 round-6 manifest had moved into `slots/archive` when the slot was reused. The exact original was located and verified against its saved offers, also for the round-5 pre-encounter checkpoint. Repair copies only that exact file; it does not replace opponents or alter progress, items, checkpoints or originals.

Prevention pins after slot selection/archival and immediately before the first checkpoint on both new-game routes. Missing-manifest recovery searches sibling archived slots for the exact version, validates era schedule/cost unit and every persisted offer, then copies original bytes atomically. Corrupt existing targets are not overwritten. Without an exact match the prior guarded legacy-boundary policy remains.

## Evidence

Root integrated and tested determinism, ordering, overflow, costs/counts and all presets. Tests walk schema 3 through 24 fights with one max-price boss each fourth round, saves/reload, gold items and single-card recovery; schema-2 bosses remain three. Controller/presentation cover the single boss/editor path. Geometry tests cover all anchors, spacing, patterns, cover/open fallback and failed terrain. All seven current/pre-encounter checkpoints were checked using exact pinned content, including the archived source for slot 1. Native tests remain user-controlled; this coordinator launched no game.
