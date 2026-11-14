# Full-era active pool — 25 October 2026

The user clarified that approval covered the complete per-era curation proposal,
not just the fifteen faction additions. The active selection is now applied to
every era, without changing DLLs or deleting personal/library designs.

| Era | Base designs | Existing veterans | Expected active total |
|---|---:|---:|---:|
| WWI | 20 | 2 | 22 |
| Interwar | 25 | 2 | 27 |
| Earlywar | 25 | 0 | 25 |
| Midwar | 25 | 0 | 25 |
| Latewar | 25 | 0 | 25 |
| Coldwar | 16 | 0 | 16 |

The selection spreads provisional name roles, measured costs and recognizable
families. Native mass buckets assist names with no role clue; they do not establish
armour or tactical capability. Mandatory stock/curated faction entries, native
starter, explicitly approved additions, cheap viable options and max-cost mobile
boss candidates remain. The existing automatic next-era boss policy is retained.
Role tags are curation metadata, not new AI/weapon behavior.

WWI requires repeated-family snapshots to reach twenty base designs and contains
14 recognizable base families. Coldwar has sixteen approved available designs
across roughly ten recognized families; four more are needed for a base pool of
twenty. Test/autosave/extreme unapproved Coldwar imports were not used as filler.
Veterans keep expanding their actual era as previously agreed.

Content checks excluded obvious stationary names and entries without a native
engine/transmission/track/cannon component. This caught gun-only models missed by
name filtering, plus unarmed Little Willie and FT-17 (MG) snapshots. It does not
prove operational weapon mounting or native pathfinding. Nederland's Variant18
(WWI, 75 mm, saved price 16,011) was added as a byte-identical immutable copy to
bring the WWI base to twenty with another distinct family. Its source was not
edited. Import receipt: `artifacts/import-community-20261025-201137/journal.json`.

## Application and preservation

Preparation tool: `tools/prepare-era-curation.py`; reviewed application tool:
`tools/Apply-EraCuration.ps1`. The latter checks the exact plan hash, explicit
absolute active/reserve directories, marker partition, content hashes, immutable
backing files, destination collisions and game closure before moving individual
files. No recursive move/delete is used. The game was closed and never launched or
terminated by the agent.

There are 115 active community markers and 515 in
`BepInEx/config/SprocketExpedition/community-blueprints-reserve`. These are moved
directory entries of immutable pool copies, never links to mutable personal files.
All 630 reviewed community identities still exist exactly once across active and
reserve; immutable content-library blobs remain in place. NativeCatalogue reads
only direct files in community-blueprints, so reserve files do not join new runs.
The 21 protected native stock/faction sources and 4 current veteran records are
retained, giving an expected fresh native total of 140.

Current checkpoints/catalogues, native-probe configuration, Game DLL and original
approved personal files are hash-identical. Game root/executable ACLs are unchanged.
Old runs read their pinned catalogue and immutable library, so old opponents remain
resumable even when their marker is in reserve. Missing-manifest recovery and the
existing resume logic were not changed. No music or native era data was edited.

Application receipt: `artifacts/era-curation-20261025-201527/journal.json`.
Reviewed plan SHA256: `62BF3D5BEE2E0EFABF4EA5AB0D2207CB331ED233987D989481F61F1D0815D072`.
The receipt retains every from/to path and before/after pool identity, enabling a
future explicit reversal. `pool-curation.json` records the current source selection.

## Verification

- Every selected marker hash matches its content identity; all marker identities
  retained once, all immutable backing paths retained.
- Real-ID/cost offer preflight: 1,536 round/seed requests across all 24 rounds,
  exact offer restores, selected-only enemies and max-cost upcoming-era boss policy.
- Current/pre-encounter slots 1 and 4 and stored prior slots validate read-only
  against their original pinned content after the marker move.
- All protected native stock/faction bytes match the measured catalogue exactly.
- Expected fresh counts are not a native-load/spawn acceptance claim. New
  catalogue loading and actual battles remain user tests. Only new runs use this
  selection; existing campaigns are deliberately unchanged.

Report, complete CSV, plan, before-state and verification:
the original development workspace (not part of this release).
Test with New Game in the desired era, then inspect the logged era counts and
several normal/special offers. No blueprints, saves or native data were sent to an
external worker.
