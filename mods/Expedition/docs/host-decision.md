# Host decision and first playtest plan

Date: 2026-10-20. Decision owner: coordinator. This follows
`docs/implementation.md` and `docs/native-adapter-implementation.md`.

## Decision: Custom Battle / `DeathmatchGameMode` is the campaign host

The final Expedition mode will be a separately clickable mode/scenario that opens
the game's Custom Battle host, not the fixed Defence mission.

Why this is technically better:

- A standalone entry can be added beside Custom Battle / the scenario list and
  starts the mode directly.
- Authored teams, spawn points, maps, AI paths/targets and mission rules are native
  Custom Battle concepts; the Battle Editor source demonstrates the available
  methods.
- The player can bring a locked design as a fresh spawned unit, instead of using
  the editor's live vehicle; this avoids the post-loss design/editor damage.
- Friendly/enemy counts, maps, reserves and future endless content fit the Custom
  Battle team/spawn model.
- Defence's two-mission graph remains the **proof/reference** for native lifecycle
  behaviour (editor exit, outcome, resets, editor restoration), not the campaign
  content host.

The Battle Editor/Map Framework mod is a **method reference only**, not a runtime
dependency. No beta DLL is installed or linked.

## First playtest scope (minimum viable)

The first playtest is not the finished 18-battle campaign. It is one complete
encounter loop in the real game:

1. A small Expedition entry/screen: new run, confirm the current design, show one
   generated offer, start it.
2. Custom Battle host on one checked map:
   - one player unit from the locked design;
   - one identical ally AI unit;
   - one enemy unit from the encounter plan;
   - correct teams/spawn positions and native orders;
   - one victory and one all-friendly-destroyed loss path;
   - native editor/menu return and cleanup.
3. Controller/presenter wiring with one persisted checkpoint; no 18-round UI,
   records screen or endless polish required for this test.
4. Fixture/checked content is labelled; no fake roster or fake result is accepted.

Acceptance for the first playtest:

- Player + ally + enemy are actual spawned vehicles, not requested counts.
- Ally receives orders and does not fire at the player.
- Victory emits once; all friendlies destroyed emits loss once; losing only the
  original player tank does not end the encounter.
- Leaving/destroying the encounter returns cleanly; the editor/named design is not
  overwritten.
- Repeat the encounter at least twice without stale vehicles or duplicate callbacks.

## Milestones and order

1. **Custom Battle spawn spike** (native frontier, highest risk):
   set up one Custom Battle map/teams, spawn the player + ally from the locked
   blueprint, spawn one planned enemy, preserve orders, observe outcome, return.
   **Status:** host/teams/restart/roster/all-friendlies-outcome spike implemented
   and compiles in the campaign build using existing native unit definitions; live
   qualification pending. The locked-blueprint resolver and authored spawn positions
   are the next increment. Gate remains live roster/outcome evidence, not a compile.
2. **Minimal presenter/UI host**: Expedition entry, start/confirm/offer/battle/result,
   controller wired to the real adapter; one checkpoint.
3. **First playtest**: run the acceptance above, record game/plugin hashes and
   evidence separately from builds.

The controller, presenter contract, run engine, encounter catalogue and persistence
are already implemented and green (149 unit tests + integration scenario). The
remaining work is native spawn/team/outcome integration and the UI host.

## Estimate

No reliable calendar date can be promised while the Custom Battle spawn route is
unqualified. Realistically this is **three focused implementation/live cycles**:
spike, UI/controller hookup, playtest. The spawn spike is the gate; if it needs
additional live iterations, each adds a cycle. Work can start immediately; the first
playtest is ready only after the spike passes and the minimal UI is connected.
