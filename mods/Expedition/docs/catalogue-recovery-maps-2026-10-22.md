# Catalogue persistence and per-offer maps — 22 October 2026

## Report and preserved evidence

Slot 4 could not continue after plugin/content updates. The saved catalogue was
`native.cost.1.e1a8fb3dfd724c56`; the rebuilt native catalogue reported
`native.cost.1.a254e8cd0df8cac7`. The old implementation fingerprinted the entire
live design/era pool but saved only its identifier, not the content behind it.
A changed fingerprint therefore invalidated future offer generation even when
the player tank and checkpoint were intact. The mismatch alone does not prove
which particular plugin/file changed.

Before repair, the entire slot was copied to local private
`artifacts/slot4-before-catalogue-recovery-20261022`. It contains personal blueprint
bytes and must not be distributed or sent to external models. The actual save was
Building, round 7, six victories, Earlywar, fleet budget 57220.925, no pending
offers, selection, locked plan or current attempt. The confirmed tank's stored
hash and opaque bytes remain authoritative.

## Content protection and old-save rescue

- Native catalogue metadata and exact enemy bytes are now archived under each
  slot's `catalogues/` directory. Archive filenames hash the version string; data
  cannot turn a version into a path. Archives travel with existing whole-slot
  pre-encounter backups/archive operations.
- Writes are durable, atomic, read back and immutable. The envelope has a schema
  and SHA256; every enemy is separately checked against its exact blueprint hash,
  native-era reference and saved price. Oversized/invalid content blocks instead
  of silently selecting another tank/catalogue.
- Existing archived runs load their saved catalogue, not the newly rebuilt pool.
  New maps, edited faction designs and unrelated catalogue changes therefore do
  not invalidate a previously saved offer hash or replace a retry's enemy bytes.
- The early-build exception is explicit: a missing old archive may adopt today's
  content only in Building with zero offers, selection, locked plan or attempt,
  identical six era references and price unit, and a verified saved tank. The
  archive preserves the old run's opaque catalogue identifier and records the
  adopted source version and build round. This is a recorded boundary adoption,
  not a claim to have recovered unavailable historical enemy content. Checkpoint,
  player tank, seed, budget, victories and identities need no rewriting.
- A missing archive with committed offers/combat still blocks: the code never
  fabricates the old formation or deletes the checkpoint. Corrupt archives also
  block instead of being replaced. Missing native parts/eras or incompatible mod
  mechanics can still prevent native loading; an archive cannot restore a removed
  plugin or freeze its executable behavior.

## Map pool

Candidate pool: Defence, Fields and Dunes. Their installed native config files
declare Custom Battle/Deathmatch support, two teams and sufficient spawn capacity
for the campaign's 8-friendly/12-enemy caps. Configuration validation does not by
itself establish terrain/spawn/outcome gameplay qualification.

Core already rotates distinct maps through the three risk cards deterministically.
The native card now displays the actual offer's MapId. Both BattleConfig.MapName
and the native scene request use that same selected map; the editor remains on
Defence. Missing/unsupported maps fail explicitly; no fallback scene substitutes
for an advertised map. Config hashes and map IDs enter new catalogue fingerprints.
Existing archived runs retain their pinned pool. Slot 4's safe-boundary adoption
includes the new pool; future pool changes otherwise apply to new runs.

## Managed/install checks

New pure CampaignContent suite passes archive integrity/immutability, exact offer
restore after a new live version, explicit safe-boundary lineage, refusal of
unsafe adoption (offers, attempt, selection, combat, cost/era mismatch), three
distinct per-offer maps, Deathmatch and spawn-capacity gates. Existing controller
integration passes. Campaign and Core-free proof builds compile with zero errors
or warnings; prior net6 console support warning remains. Core DLL is unchanged.

Installed with Sprocket closed, previous pair/config backed up:
`artifacts/install-campaign-20261022-210736/journal.json`.
Game SHA256 `02DE2BBAA926E731D4E8DA97FBC7286ECCFE06730FC605B9F3BF14FA619F5A4B`.
Core SHA256 `B22D7F532B3EBADC037CFBBC2C8683F062EB5A7AAD4E103497DC6B290268AC4A`.
No additional source, native files, saves or logs were sent externally for this
native save/content repair. Native evidence follows as observed.

Native follow-up: slot 4 restored the Earlywar editor and round-seven offers with
unchanged six victories and budget 57220.925. Cards showed Fields / Defence /
Dunes. The selected Fields scene loaded and registered 6/6 actual spawns, four
friendlies and two enemies with readable crew. On 23 October the user confirmed
the maps work, while reporting their size as a separate tuning issue.
