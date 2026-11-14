# Approved faction additions — 25 October 2026

The user approved the nine-design proposal and added six updated native Coldwar
designs: Leopard 1-b1, AMX 158 D, Leopard 2a4-n (identified by the user as the plain
2A4), Leopard 2a5, Leopard 2a6-2 and Leopard 2a7. C42 Falcon-S was explicitly moved
to Latewar. The latest Nederland files were reread, including the updated PzH6000.
No source names, dates, prices, ERA/HEAT content or component definitions were edited.

The selected bytes were frozen locally and checked against installed native Era
definitions: 14 Coldwar entries and one Latewar entry. All headers are current 2.0
vehicle format, bounded size and positive saved price, with cannon definitions.
Test/autosave/unsupported copies were excluded. The ordinary 2A4 keeps its original
`Leopard 2a4-n` header/filename for identity stability.

Imported with Sprocket closed using the existing hash-guarded community importer.
Receipt: `artifacts/import-community-20261025-195723/journal.json`. It backs up the
previous import metadata, copies reviewed bytes into the immutable content library
and hardlinks pool entries to those immutable copies, never to personal originals.
The previous 614 community files remain; there are now 629. All 15 pool/blob hashes
match the reviewed sources and all 15 personal originals still match their hashes.
The actual stationary-name policy rejects none of the selected designs.

Existing checkpoint/catalogue JSON, native-probe configuration and installed Game
DLL hashes are unchanged. Root/executable ACLs are unchanged and a new pool entry
inherits Medium integrity. No DLL, music, global era, boss override or save was
modified. The highest selected Coldwar price is below the previous automatic
Coldwar-boss price, so this batch does not supersede it by cost.

Current pinned native catalogue still contains two Coldwar designs. New Game will
read the expanded library and is expected to reach 16 Coldwar designs and add one
Latewar design, apart from any separately earned new veterans. Existing saved runs
retain their pinned content. Imported bytes/headers/filter checks are verified;
native card loading, part compatibility, live scalar cost and actual spawning still
require the user's next-game test. No game process was launched or controlled.

Reviewed manifest, frozen source copies, full inventory, before-state hashes and
verification: the original development workspace (not part of this release).
Manifest SHA256: `49cd86805ff7de4dd0a533bf0de8ab50462fbe4b4d49b4d8f87640bf5d0a500a`.
The faction audit tool now skips shortlist entries that moved era or are already
imported, so the approved C42 change does not break a later audit.

User check: New Game, select Coldwar, confirm the expanded count in the log and
play encounters containing the imported designs. C42 Falcon-S should be available
to new Latewar pools, not introduced as a new Coldwar entry.
