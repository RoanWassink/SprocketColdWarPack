# Menus and saved-run editor restoration — 21 October 2026

This increment continues the accepted editor/Custom Battle route. It does not
replace the combat host or qualify the fixture catalogue as a finished campaign.

## Implemented

- Main-menu Load Game opens the existing four slots. Open selects the correct
  slot (including the fixed loop-variable capture); Resume validates the active
  checkpoint, mods and blueprint hash before loading its opaque tank bytes into
  the real editor. Named personal designs are not written.
- Persisted offers/selection are retained. A checkpoint saved immediately after
  victory can legitimately contain no next-round offers; resume now generates
  and commits those before presenting them. Previously the native test hit an
  empty-offer menu here. Recovery uses the same controller correction.
- An interrupted encounter restores the saved tank and exposes an explicit retry
  action. It does not auto-launch, refresh to another design or silently restore
  an older pre-encounter backup. The existing fresh-scene route is used on retry.
- Active, suspended and unverified slots are never automatically reclaimed for
  New Game. Empty/verified terminal slots are eligible; archiving failures stop
  reuse. Manual archive/free-slot has an explicit in-game confirmation.
- Presenter views map through pure ScreenContracts/ScreenComposer: start/records,
  building, three challenge cards, encounter state, result, finished, endless,
  blocked and errors. Buttons serialize through the controller; busy and stale
  view clicks are rejected. Terminal results cannot expose a stale Retry action.
- The UI reports the actual prototype map (Defence), supplied costs/counts/rewards
  and the plan's fixture composition. It does not calculate a new fleet budget.
- The native canvas now fits and centers its design area on ultrawide screens,
  with a full-window backdrop. Plain-text labels avoid rich-text interpretation.
- A blocked start no longer immediately overwrites its reason with the start view.

## Validation

- Managed: 28 run + 92 encounter + 30 persistence + 18 presentation tests pass.
  The expanded controller integration passes, covering real temporary RunStore
  bytes/hash, selection, missing-mod/corrupt-blueprint blocking, no automatic
  combat on resume, active/suspended/unverified slot protection, and the empty
  next-offer boundary after victory. Existing console net6 projects emit their
  prior NETSDK1138 support warning.
- Both proof-only and campaign native builds compile with zero warnings/errors.
- First installed menu candidate F44D1213... exposed bottom-action clipping on
  3440x1440. The 344EBC7F... layout repair fits its controls; Load Game restored
  the tank but exposed the empty-next-offer bug. Neither is the final candidate.
- Final installed Game SHA256:
  `9022590108B172F19285FB5AD6F0223C4253468C4E4775FEA51C352E1161B983`.
  Core SHA256:
  `EE0DAEE4C662C2A8D4B51583620E1BBE9A506D3480E591583883314D50DBB7C1`.
  Journal: `artifacts/install-campaign-20261021-214211/journal.json`.
  Previous DLL pair/config backed up; existing config kept byte-for-byte.
- Final native check: main menu -> Load Game -> slot 4 (round 3, two previous
  victories) -> Resume -> restored tank in real editor -> three visible offers ->
  cautious choice -> editable tank -> native Play -> fresh Custom Battle with
  7/7 actual spawns, 2 friendlies and 5 enemies, observed roster armed. Resume
  itself did not start combat. Combat/damage was visible. This validates the
  resume-to-next-battle path, not all content or a full new campaign completion.

## Next acceptance/content work

Native interrupted-fight retry, third-win era/build boundary, technical-load
failure, menu input capture across all views, ally orders and ordinary scenarios
still need dedicated acceptance. Explicit checkpoint recovery/abandon and
content-exhaustion completion remain planned work. Replace flat fixture prices,
synthetic eras/maps/layouts and index-based enemy mapping with measured native
bindings and checked authored content before calling this a campaign release.
