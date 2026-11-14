# Editor / Custom Battle loop — 21 October 2026

The user's intended loop is authoritative: New Game -> tank editor -> native
Play -> fight -> victory -> choose a challenge -> improve tank -> native Play
-> another fight. Custom Battle / Deathmatch remains the combat host. Defence's
mission memento is only historical proof tooling, not the campaign route.

## Repair

- New Game opens the actual native editor via a Sandbox setup context. The first
  Play captures the real tank and starts the Standard first encounter.
- The designer's actual StateChangeRequested/Exit event is wrapped per visit.
  Photo mode is forwarded; the original native delegate is restored on leave.
- Each encounter loads a fresh map with a Deathmatch context and real native
  BattleConfig/teams/units. No hidden setup clicks, placeholder tank, or Retry
  restart is used by this session route.
- The run/controller survives owned scene transitions. Real blueprint bytes
  restore the editor's tank, including unsaved edits, without writing a named
  personal blueprint. Choosing an offer returns to the editor; Play recaptures
  upgrades and preserves the selected offer and exact enemy plan.
- Pre-encounter upgrades recalculate affordability/friendly count. Once loading
  starts, the tank cannot be replaced by the upgrade API. Cancellation/session
  generations reject old callbacks. Technical retries resume before retrying.
- The first native test exposed zero-distance fog from the raw EnvironmentConfig
  constructor. The follow-up uses explicit daytime/3000m visibility.
- Existing stock-era/technology data and other plugins are not changed. The
  source method reference and MIT notice are in THIRD-PARTY-NOTICES.md.

## Evidence

- Managed: 28 run + 92 encounter + 30 persistence tests pass, plus controller
  integration with persisted upgraded tank bytes and preserved enemy plan.
- Both proof-only and campaign builds compile against installed interop.
- Installed first test: Game 7834AE44..., Core FB47FB7C...,
  artifacts/install-campaign-20261021-211504/journal.json. New Game -> native
  editor -> actual Play -> fresh Custom Battle was observed live. Actual roster
  2 friendlies / 2 enemies, spawns 4/4, native player-team control. Its victory
  occurred near application exit, so it is not used as the qualifying win.
  This build had the fog defect.
- Fog repair installed: Game F2608E85..., same Core,
  artifacts/install-campaign-20261021-211913/journal.json. Further native
  test passed New Game/editor/Play, visible terrain/tank, 4/4 actual spawns,
  2 friendlies / 2 enemies, native damage, 2 -> 1 -> 0 enemies, one victory,
  native result UI, choice, clean editor return and fresh Play in the same run.
  The next capture changed from 229091 to 229097 bytes, and the second encounter
  registered 2 friendlies / 5 enemies. The tester also edited the tank in the
  returned native editor. No named blueprint was saved by the plugin.
- Follow-up guard: require native GameController Running, and treat missing
  vehicle wrappers as unknown unless their crew death was already observed.
  This prevents application/scene teardown from looking like an enemy wipe.
  Historical proof overlay is hidden outside an explicit proof session.

## Remaining scope

This remains a first-playtest build. Catalogue prices, era identifiers and offer
maps are labelled fixtures. The bridge explicitly uses the tested Defence map,
with enemies loaded from Expedition Spawns (existing faction fallback retained).
It is not a validated 18-battle content release. Native Load Game was subsequently
implemented; see menu-resume-2026-10-21.md for its separate validation. Remaining:
full native era/budget/content qualification, and the third-win era boundary
still require further work. Legacy three-battle economy/era blocks remain; a
new block requires design confirmation in the editor before its offers.
