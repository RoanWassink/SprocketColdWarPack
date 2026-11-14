# SU-100Y, item capacity and menu return — 24 October 2026

## Reported vehicle

The pinned Earlywar SU-100Y costs 56,978 and matches enemy slot 1 of the reported
Dunes encounter. It received FireSupport, a 650 m stop range and 1.57-second reaction,
then a Passive/StopToFire order at 201 m. Logs therefore confirm it received the
stop intent; they do not establish its subsequent native driver behavior because
the previous summary sampled only the first three allied units.

The stationary firing controller now permits a native FaceTargetTask before its
strict HaltTask. Facing begins above 12 degrees of horizontal hull deviation and
continues until 6 degrees. This allows casemate guns to turn toward a currently
visible opponent instead of a long-lived brake task suppressing hull rotation.
The native task is bound to the current attack cancellation token; its managed
owner releases it on target/order changes, lost contact, player handoff, manual
ally commands and session release. Old native paths are explicitly cleared at
attack handoff and stationary alignment. There are no throttle/physics overrides.

Native signatures and matching binary behavior of FaceTargetTask, HaltTask,
ClearTasks and ClearPath were inspected locally. FaceTargetTask emits FaceDirection
without MoveToDestination. Pure facing-policy checks cover behind/side targets,
alignment hysteresis and invalid input. The user must still test whether this
addresses the reported drive-through behavior. Cost/role variation is retained.

AI summaries now include up to all twenty owned AI units with design names,
actual native tasks, waypoint range and a target range only if genuinely visible.
This makes another SU-100Y report traceable without adding hidden target knowledge.

## Full item slots

Expected reward-command rejections present ArmoryView.Notice rather than ErrorView.
Full storage offers stored-item discard/decline and keeps pending loot. Full active
slots explain the limit and disable invalid activation. MANAGE ACTIVE ITEMS lets
the player discard an active bonus, then activate another type. Discard ends the
effect and does not return/refill the item, pay expired bonds or refresh its timer.
Same-family replacement remains available with all three slots occupied.

Discarding an active item uses normal reward revision, offer invalidation and
budget refresh, including affordability checks. Pending loot/recovery restrictions
remain enforced. Notices are presentation data, not new save fields. Unexpected
storage failures retain the genuine error path.

Controller integration tests for both rule versions 2 and 3 cover full inventory,
full active slots, no ErrorView, inert rejection, discard/claim, active discard,
activation, same-family replacement and checkpoint reload. Adventure 16/16, Run
30/30, Persistence 30/30 and Presentation 19/19 pass, together with CampaignContent.

## Endless loading after Return to Menu

Captured native logs show suppressed ResetGame during unload immediately followed
by NullReferenceException in DeathmatchGameMode.ExitGameInternal. The previous
return gate incorrectly blocked required cleanup. In addition, its Task-returning
Harmony prefixes could skip a native method without setting a non-null result.

The gate now marks the scene request before RequestSceneChange can synchronously
start exit, and permits the actual ResetGame during that exit. Retry/Setup remain
suppressed. An intentionally suppressed Task-returning Retry, ResetGame or SetupGame
gets native Task.CompletedTask rather than null; this is a no-op, never a substitute
for the actual exit cleanup. Result callbacks stay suppressed until the menu is
ready. Pure gate checks explicitly cover allowing cleanup while blocking restart.

All seven current/pre-encounter saves were verified read-only. Slot 1 is round 12,
with the reported win saved. No user checkpoint or catalogue was rewritten.

## Installed trial

- Campaign/proof builds: zero warnings/errors. Native behavior remains user-tested.
- Installed closed with backup: `artifacts/install-campaign-20261024-195534/journal.json`.
- Game SHA256: `762547AC4A1DAE057D01BB66CD948568B2D0A6C93C0E9A0B026F20BBD659C74E`.
- Core SHA256: `F64D33901C33254607D66BB2D0158ED35061BF832059B59233622D3B3AAA790E`.
- Config preserved byte-for-byte, root ACL unchanged, executable still Medium.
- Tests/build/slot evidence: `artifacts/su100-inventory-*.txt`.
- Private native evidence: `artifacts/su100-drive-20261024-194647/` and
  `artifacts/su100-*.local.txt`; not distributed or sent externally.

User check: resume slot 1, manage a full active inventory without leaving the item
screen, then Return to Menu and resume again. In a later SU-100Y encounter, watch
whether it turns toward visible opponents before stopping and firing.
