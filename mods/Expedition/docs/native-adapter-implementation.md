# Native campaign adapter — implementation status and next work

Date: 2026-10-20. This is the Game/frontier work for Phase 4. It records what is
already compiled and what must be live-qualified before any campaign claim.

## Implemented in `CampaignGameAdapter`

- `CheckReadiness`: exactly one loaded Defence scenario, settled editor, loaded
  vehicle-editor target, positive native cost, no active proof session.
- `CaptureDesignAsync(EraReference era, ...)`: reads the final native editor target
  through `IVehicleBlueprintSerializer.Serializer.ToBlueprint` and
  `SerializeToJSON(blueprint, false)`, stores the exact UTF-8 bytes, computes the
  lowercase SHA-256, and returns `DesignCapture` with the measured native cost and
  the block era. It does not write a named design.
- `OpenEditorAsync`: requires the settled Defence editor; it does not fake a scene
  load.
- `CleanupAsync`: refuses while another proof session owns the scenario.
- `RunEncounterAsync`: **deliberately returns `TechnicalFailure`** with a clear
  detail. No roster, combat-start callback or outcome is fabricated.

## Custom Battle host spike (campaign build, compile-only)

`CustomBattleHostSpike.cs` is the first concrete Custom Battle host proof:

- requires one `DeathmatchGameMode` in `Running` state and at least one existing
  `UnitDefinition` on Team 1 and Team 2;
- sets Team 1 to two copies of its existing unit (player + identical ally) and Team 2
  to one copy of its existing unit;
- restarts through native `RetryAsyncVoid()`, logs actual `vehicleSpawns` and roster,
  and emits one all-friendlies loss or enemy-destroyed victory;
- exposes `RestoreOriginalLineup()` for the next restart;
- campaign-only debug keybind **Expedition → Custom Battle host spike (debug)**;
- does not yet resolve the locked player blueprint or authored spawn positions, and
  does not force a native end screen/return.

Build: `dotnet build src/SprocketExpedition.Game -c Release -p:CampaignBuild=true
-p:OutputPath=bin/Campaign` → 0 warnings/0 errors. Default proof build remains
Core-free. Live protocol: `docs/custom-battle-spike.md`; no install was performed.

## Decisions required before dynamic spawning

1. **Host route (decided):** the campaign uses Custom Battle/`DeathmatchGameMode`;
   the Defence `MissionScenarioGameMode` proof remains the lifecycle/reference
   validation only.
   - Defence preserves the surveyed two-mission graph and native orders/objectives,
     but its player is the editor-exit vehicle and its spawners are fixed.
   - Custom Battle supports a standalone entry, authored teams/spawn points, maps and
     mission rules, and a player-pick/fresh design. The Battle Editor/Map Framework
     methods are reference only; no beta DLL is linked.
2. **Fresh player spawn:** the release must not use the editor's live vehicle. A
   locked blueprint must be spawned as a fresh player unit (and the allied copies),
   with native control/team/register assignment. This is the same requirement that
   exposed the post-loss turret damage.
3. **Blueprint resolver:** `EncounterPlan.EnemyUnit.BlueprintId` is a local catalogue
   ID, not a game path. The adapter/content layer needs a checked resolver to the
   exact native blueprint/asset, with era/part/mod validation. Core must not carry
   raw paths or native data.
4. **Spawn shape:** map/spawn-layout to native locators; friendly count including
   player; enemy counts up to 12; no overlap/unsafe ground; no beyond-cap fallback.
5. **Orders/control:** preserve native AI commander orders; player may continue in a
   surviving friendly; all-friendly-destroyed loss exactly once.
6. **Outcome/cleanup:** one consolidated result, native fail/success handling,
   editor return, actual reset tasks, design/editor restoration, no named-design
   overwrite.

## Candidate implementation steps

1. Add a checked content resolver from catalogue blueprint/map IDs to native
   blueprints and map/spawn layouts (frontier-owned, no paths in Core).
2. Extend the proven memento session or build a Custom Battle host:
   - set ally/enemy spawn limits and team/register from the request;
   - spawn the player and allies from the locked blueprint as fresh assemblies;
   - retain native orders/objectives or the Custom Battle mission rules.
3. Signal `combatStarted` once, observe the actual roster, deliver one
   `EncounterResult`, then perform the native fail/success return and reset tasks.
4. Verify the locked design bytes decode to the same native blueprint and that the
   editor design is never the combat vehicle.
5. Only then wire the live adapter into the controller/UI host.

The Battle Editor method reference in `docs/battle-editor-methods.md` is the source
for player-pick/fresh-spawn, authored spawn points/team mapping, mission rules and
spawn safety. Do not install/link the beta mod or call its internals as a dependency.

## Required live qualification

- Fresh player + identical allies + planned enemies, correct teams/locations, no
  overlap, native orders and no friendly fire.
- One victory and one all-friendly-destroyed loss, each exactly once; player may
  continue in a surviving ally; losing the original tank alone is not defeat.
- Editor return, both original missions/reset tasks, design/editor restoration,
  ordinary scenario Play unaffected.
- Repeat ten cycles; one rerun from a saved Fighting checkpoint with a new
  generation; leave during loading and combat; missing mod/content blocked without
  deleting data.
- No fake roster/result is accepted as evidence. A managed build or a compile is not
  a live pass.
