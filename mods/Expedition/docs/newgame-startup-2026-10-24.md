# New Game responsiveness — 24 October 2026

The user reported a roughly three-second pause immediately after New Game, followed
by normal progress. The synchronous handler rebuilt the entire native catalogue,
including over 600 community blueprint card reads, on the main thread before
requesting the editor scene. Recent logs show 590 accepted and 24 filtered community
designs, plus the normal/veteran pool. Exact native timings were not recorded by
that build; this is the identified blocking path, not a measured speedup claim.

New Game now queues a preparation phase and immediately displays PREPARING NEW
EXPEDITION with a file counter. Update pumps the same catalogue-building iterator
for at most 32 boundaries or about 4 ms before returning to Unity. Native card,
era and blueprint operations remain on the main thread. One indivisible native/file
operation can exceed the budget; there is no unsafe thread-pool access to Unity.
Only the completed catalogue is published and the scene request then consumes
that cache, rather than scanning a second time. Duplicate starts are ignored while
preparing; session end, failure and replacement dispose unfinished enumeration.
Failures show the existing error view after clearing preparation state.

Within the current process, unchanged community cards reuse managed name/price/era
metadata, never native VehicleCard or TechDate wrappers or blueprint byte arrays.
Keys require the same path, length, UTC modification ticks and content identity.
Changed native era start/end definitions invalidate the cache. Current file bounds,
duplicate IDs and the design-count limit apply on cache hits too. Normal stock,
curated starter pricing and veteran validation retain their original reads. Cache
state is not persisted across game restarts. Existing saved catalogues and selected
blueprint-byte hash validation remain authoritative and unchanged.

Managed checks pass for bounded frame work, exact completion, cancellation, failure
cleanup, unchanged metadata reuse and all file/hash/era invalidation cases. Existing
content, archive, music, AI and veteran checks pass. Campaign and proof builds have
zero warnings/errors. No game process was launched or controlled. Actual first-run
responsiveness and total loading time remain user tests; the total wait may differ
from the old synchronous path. New logs record total preparation duration, frame
count, largest single step and reused metadata count for subsequent tuning.

Installed closed with backup:

- Receipt: `artifacts/install-campaign-20261024-201814/journal.json`
- Game SHA256: `477FD52EA74C1238B7DE9102BBDEA7AA46A6FFEF3B8528D64C85A36A7687EC04`
- Core unchanged: `F64D33901C33254607D66BB2D0158ED35061BF832059B59233622D3B3AAA790E`
- Config preserved, root ACL unchanged, executable still inherited Medium.
- Build/check evidence: `artifacts/frame-newgame-*.txt`.

User check: start New Game and watch whether the progress counter advances while
the interface keeps rendering. If practical, return to the menu and start again
within the same game process to exercise metadata reuse. No playthrough or existing
save needs to be overwritten to observe the initial preparation/editor transition.
