# Sprocket Expedition implementation contract

Current route update, 21 October 2026: the user's tested editor -> Play -> Custom
Battle -> choice -> editor/upgrade loop supersedes the original three-battle
editing lock below. A tank can be upgraded before each encounter; economy and era
boundaries still occur every three wins. Native spawning/controller integration
and the UI-host prerequisite have passed their initial live slice. Managed UI
composition and native Load Game wiring are now implemented; remaining live and
content work is tracked by GUIDE.md and menu-resume-2026-10-21.md. The dated
execution status below is historical, not a request to restart the native gate.

## Approved gameplay

18 separate battles: three each in native WWI, Interwar, Earlywar, Midwar, Latewar and Coldwar, then optional endless. The run ends when every friendly vehicle is destroyed; losing only the original player tank does not end it. Keep designs and records, no permanent combat bonuses. Improve the tank in the editor before each encounter; every three victories starts the next economy/era block. Fully replenish all friendly vehicles between encounters through the native fresh-assembly lifecycle.

Fleet budget is a ceiling, not a purchase wallet. Friendly count includes the player and is `min(8, floor(budget / final native design price))`; validate price > 0, design and native era eligibility. Same design for every allied AI. Budget20/price10 => player+one AI. Defeated allied tanks return next encounter. Native control may pass to a surviving friendly vehicle, so the player can continue the fight in any surviving tank; this is not a manual tank switch, and voluntary switching is not part of v1. Only when no friendly vehicle remains does the encounter become a defeat. Each round offers three visible choices, cautious/standard/dangerous. V1 objective is eliminate the enemy formation. Three checked maps and four checked enemy designs per native era are required before release; no unverified fixture may appear as playable content. No HP/penetration/pantser multipliers. Cap 12 simultaneous enemies and 8 friendlies including player.

## Initial tuning

21 October cost-rule update: the user's native-price rule supersedes the older
independent curated threat score. New runs use the native six-era schedule and
saved native scalar design prices. Old fixture saves retain their original profile.

Candidate B0 is twice the median of up to three available native WWI tank prices,
with a floor of twice the exact live starter quote measured before player input.
This prevents integer blueprint-header rounding from removing the starting ally.
The sample size/prices remain candidate evidence until full content qualification.
Era base multipliers `[1,1.6,2.5,3.5,5,8]`. Each choice's authored enemy-cost target is era-block budget times within-block multiplier `[.65,.90,1.15]` times risk multiplier `[.9,1,1.15]`. Formation strength is `sum(native saved design price * quantity)`. Cost classes are relative price bands within each era, for display; they do not add hidden combat multipliers. The three cards must have strictly increasing actual total costs and distinct real rosters. Native-cost mode raises an authored ceiling only when needed to reach the first/second/third attainable positive costs (up to three copies of the cheapest designs). This explicit discrete-price floor permits 1/2/3-copy choices in a one-model era. Missing-era content still blocks; another era is never substituted. Legacy fixtures keep their historical target/score semantics.

Victory supply points by risk `[1,2,3]`, converted at the next three-win boundary to an additive budget bonus of `next era base * supplyPoints * .01`, then reset. The previous bonus expires at the next boundary; rewards do not compound accidentally. Show pending points and current/next total budget explicitly. The sixth block's supply is applied to the first endless block, which starts at `Coldwar base * 1.15`. Later endless blocks increase base budget by 15% per three battles, applying only the preceding block's supply bonus. Era remains native Coldwar; no native date or playability mutations.

Three battles per block use the same actual available friendly fleet budget as enemy-scaling input. Endless eventually needs stronger registered enemy rosters at the count cap; finite content must show exhaustion and offer run completion instead of pretending unbounded vehicle strength exists. These are authored starting coefficients, subject to recorded playtest revisions.

## State and recovery

Flow: Building → ChoosingEncounter → LoadingEncounter → Fighting → Resolving → ChoosingEncounter; every third win enters Building at next era. After win18 enter AwaitingEndlessChoice. Defeat enters Lost permanently. User exit outside combat suspends; retreat during live combat counts as loss. A load/spawn failure is TechnicalFailure, no loss/reward, cleanup then retry same encounter. Aborted loading is not a combat defeat.

Persist after design confirmation, offer generation, choice, encounter start and terminal result. Include schema version, run ID, seed, round/block/era reference, budget, supply points, blueprint snapshot/hash, required mods, generated offers, chosen encounter, generation and resolved encounter IDs. Capture a mod-preserving native blueprint snapshot locally; never overwrite the player's named designs. Atomic checkpoint/backup replacement. Result processing must be idempotent. Commit result before opening the next UI. Missing mods block resume without deleting data.

Crash in battle retries the exact persisted encounter with full fresh vehicles; saves do not serialize physics/projectiles. A recorded loss cannot be resumed. Scene unload/cancellation invalidates encounter generations and removes hooks, AI, mission state and event listeners owned by this plugin.

## Implementation gate

Phase0: tooling and contracts. Phase1: compiled opt-in native proof and real game test. Phase2: freeze interfaces from evidence. Only then phase3A-D workers. Phase4 integration. Phase5 checked content/balance. Phase6 full live acceptance and Packman installation. Do not install a placeholder and call the gamemode finished.

Required live gate: player + one ally + one enemy, correct orders, once-only win/loss, editor return, repeat ten cycles, normal scenarios unaffected. A managed compile does not unlock this gate.

## Execution status — 2026-10-20

- Phase 0: complete. Tooling, sanitized worker setup, contracts/preflight.
- Phase 1: core native gate passed. Player + ally + enemy spawn, one win and one loss, native editor return, 13 clean restarts, post-loss editor restoration. Remaining acceptance observations (ally orders, leave during loading, mod/ERA reload, normal Play) are non-blocking for Core.
- Phase 2: complete. Core contract v1.0 and `RunSnapshot` v1.0 frozen.
- Phase 3A (run engine), 3B (persistence) and 3C (encounters): complete and merged; **149/149** automated tests pass.
- Phase 3D: still blocked by the plan's own precondition: a coordinator-provided presenter/UI host and a tested native UI handoff. Do not start 3D screens against direct native calls.
- **Next: Phase 4 integration.** Build the campaign controller/presenter and the native `IGameAdapter` on the proven lifecycle, connect RunDirector + IEncounterCatalogue + RunStore, and provide the UI host. Only then dispatch 3D. Phase 5 checked content/balance (three maps and at least four enemy designs per native era) can proceed in parallel once content authoring starts. Phase 6 remains full live acceptance and Packman installation.

The plan is authoritative over ad-hoc feature requests: do not skip 3D's presenter/UI-host precondition and do not label fixture content as playable.
