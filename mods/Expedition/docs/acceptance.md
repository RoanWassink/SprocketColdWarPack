# Native proof and release acceptance

No item below is passed by compilation. Record gamehash, pluginhash, scene/map, steps and actualresult for each livecase. Keep local logs private/sanitized in handoffs.

## Gate for broad implementation

- [x] Opt-in proof can be entered from a supported loaded scenario editor. *(live 2026-10-20)*
- [x] Player + exactly1 identical AIally +1 enemy spawn in correct teams/locations. *(roster live; native placement only)*
- [ ] Ally receives valid combatorders and does not fire at player.
- [x] All enemies disabled emits victory once; all friendly vehicles destroyed emits loss once; losing only the original player tank does not end the encounter. *(4 wins + 2 losses live)*
- [x] Native return to editor and every original mission's reset task complete before temporary runtime settings are restored or owned objects disposed. *(both resets 2/2)*
- [ ] Exact original mission/spawn settings restored; normal editorPlay still works. Restoration detects intervening changes instead of overwriting another owner. *(restore and editor repair live-passed; normal Play recheck pending)*
- [x] Repeat10cycles without duplicate callbacks, stalevehicles or rising objectcounts. *(13 clean restores, editorBehaviours=0 each time)*
- [ ] Leave during loading/combat; cancel/cleanup does not modify vanilla assets. *(combat return exercised; loading not)*
- [ ] Actor lifecycle reinitializes modstocks and spentERA using existing nativehooks.

The initial isolated-clone prototype is not the chosen Defence adapter. The live survey found two original mission graphs with orders, objectives and shared native registrations. The next proof preserves those graphs and uses a bounded runtime memento for audited spawn settings. This changes the implementation route, not the live gate above. `AllowCloneExperiment` stays false; a separate opt-in runtime-proof flag is required before any temporary setting changes.

## Full release after workers and integration

- [ ] Three qualifiedmaps and at least4enemies pernativeera, notfixtures.
- [ ] Valid budget20/cost10 =>one player+one AI; max8 friendly/max12enemy.
- [ ] Newrun→18rounds→finish/endless;3roundrebuild boundaries and correct eraavailability.
- [ ] Three meaningful persistedchoices perround, same choice afterresume.
- [ ] Finaldesignquote equals displayed vanilla scenario cost; disabled/faulty nativecriterion blocks launch.
- [ ] Free repair/rearm includes fuel/crew/ammo/autoloaders/ERA; blueprint still preserves moddata.
- [ ] Defeat permanent; records/blueprints retained; loadfailure notdefeat.
- [ ] Restart between encounters; crashretry restores same encounter without doublereward.
- [ ] Missingmod blocksresume preserving data; failedatomicwrite retains lastgoodcheckpoint.
- [ ] Ordinary scenarios/CustomBattles unchanged afterexit/disable.
- [ ] Enemy hulls leave the map's fixed waypoint, advance from their own spawns to the player's
  last-seen position, search after lost sight and attack on sight; allies are not targeted and
  non-Expedition AI is unchanged. *(implemented and compiled; live pending)*
- [ ] FPS/objectcounts compared against equivalent vanilla20vehicle battle.
- [ ] Packman closedgame install/backups/hashjournal and rollback recorded separately.
