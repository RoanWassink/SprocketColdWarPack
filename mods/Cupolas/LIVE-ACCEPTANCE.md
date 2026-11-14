# Pending native acceptance — owner/coordinator handoff

No part of this checklist was run in the game. Use a disposable vehicle copy. Preserve both BepInEx LogOutput.log and Unity Player.log.

1. Check date before/on 1992.01.01, card name/icon, cupola category, stock cupola selected → intended variant replacement, unrelated ERA/rotator/hatch selection unaffected.
2. Hold and cancel a ghost for more than one second: no ADOPT/SWAP. Place on hull and turret: READY lookup, ADOPT resolves both model VUIDs, both SWAP readbacks match, and baseline STATE is CLOSED_REFERENCE. An uninspectable atlas stage intentionally produces REJECT and stock geometry; capture its reason before revising the gate.
3. Compare mirroring on/off on the centre line; distinct VUIDs must remain distinct parts. Rotate, scale 0.5/1/2, select base and lid, duplicate, Undo/Redo, zoom through LODs. Native collision stays stock.
4. Compare measured vehicle mass/cost deltas against a German stock cupola on the same design. Confirm stock paint/camo, commander viewport and native task behaviour.
5. Drive, move turret, fire, return to editor, repeat on the same player objects and a fresh distant target. Check for painter/atlas/release errors, disappearing LODs and COM changes. An extra attached object must fail closed.
6. Save As, exit, restart, clean reload: GUID, offsets, two models, paint, mirrored parts, commander and viewport survive; then repeat the drive/return test.
7. Native cook-off may hide the lid; observe Blown/visibility/pose without writes. There is no ordinary lid animation or control in this version.

Prepared 0.1.0 is not native acceptance. Keep DetailedLogging=true until user-confirmed testing; the owner can then apply the standard logging-off release step.
