# Editor fleet budget correction — 21 October 2026

The user reported 150000 in the round-four editor, a 10.94k tank and only one
AI ally. The Defence Sandbox editor was showing its own vanilla scenario cost
criterion (150000, 16 vehicles), while Expedition uses its persisted fleet budget.
The first three native battles actually registered two friendly tanks including
the player, consistent with the WWI campaign budget, rather than one total tank.

The inspected checkpoint after the third actual victory was Building, round 4,
era index 1 (Interwar), B0 21391, block base 34225.60, supply bonus 1711.28 and
available fleet budget 35936.88. At a full tank quote of 10940, the fleet is three
tanks including the player, with two AI allies. The existing campaign cap is eight
including the player. No balance, saved-run budget or cap has been changed.

## Correction

- The current native-cost Expedition editor visit receives the actual Building/
  Offers budget; before the first Play it uses the measured starter fleet budget.
- The owning ScenarioCostVehicleEditorPlugin is found through the active editor's
  registered plugins. Only its local criterion receives the campaign budget and
  eight-vehicle cap. Installed interop verified setter signatures and the existing
  UpdateCriterion method; no new native Harmony hook or global asset change.
- A noninteractive editor label shows live tank price, fleet budget, total tanks,
  AI allies and the cap. It updates as the tank changes and is hidden behind menus.
  It previews the same full-precision floor and cap as the authoritative Core.
- Original criterion budget/limit are retained and restored before scene handoff,
  menu return or session end; the label is removed on leaving the editor. Old
  fixture slots and ordinary scenarios do not receive this native-cost display.
- Spawns remain driven by the actual quote captured at Play. Display code cannot
  award budget, alter a checkpoint or replace the controller's fleet request.

## Validation and installation

29/29 run tests pass, including the exact round-four budget/requote/three-tank
spawn-request regression. Controller integration passes. Campaign and Core-free
proof builds compile with zero warnings/errors. Existing net6 test warning remains.
Other unchanged suites previously passed (106 encounters, 30 persistence, 18
presentation); this change does not change Core bytes. Native display validation
is recorded separately below.

Installed while Sprocket was closed with previous pair/config backup:
`artifacts/install-campaign-20261021-221807/journal.json`.
Game SHA256 `16AE9264C2A4FD6AD385620E505FA062F2ACDD43C7EEAFD8BA295ED1131A1439`.
Core SHA256 `B22D7F532B3EBADC037CFBBC2C8683F062EB5A7AAD4E103497DC6B290268AC4A`.
This native-only change was handled locally; no additional source/logs/designs
were sent to an external model.

Live after restart: Load Game restored slot 4 in the Interwar editor. The log
confirmed binding the owning criterion from 150000/16 to campaign 35936.88/8.
The native scenario line displayed 35937 budget / 3 vehicles. The Expedition
label showed the precise 35936.88 ceiling and 3 total = player + 2 AI allies.
As the user edited, the native quote and label changed from 10901.03 to 10972.88
without losing the three-tank preview. The label was readable at 3440x1440.
The first era transition is now observed in the native Interwar editor; this
does not qualify a complete campaign through all later eras.
