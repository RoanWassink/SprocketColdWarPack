# RunSnapshot freeze v1.0

Date: 2026-10-20. Source: `src/SprocketExpedition.Core/Run/RunSnapshot.cs`.
Status: **frozen for 3B and implemented**. 3A produced the shape; coordinator reviewed it after
27/27 run-engine tests. 3B consumed it and passed 30/30 persistence tests. 3B may not
add, remove or rename fields; any change is contract v1.1 with a schema version bump
and an explicit migration decision.

## Frozen public types

- `RunSnapshot`
- `RunDesignState`
- `RunConfiguration`
- `RunActionStatus`
- `RunActionResult`

## Fields

`RunSnapshot` carries schema version, run identity/seed, catalogue version, cost
unit, measured initial base budget B0, era schedule, phase and `SuspendedFrom`,
round/block/era counters, wins/victories/defeats, block base/supply bonus/available
budget/pending supply points, the confirmed design and block confirmation flag,
offers, selected offer id, locked plan, current attempt, monotonic generation
counter, processed attempts, consumed offer ids, and the last outcome/detail.

## Conventions

- `Round` is one-based: 1..18 finite, 19.. endless.
- `BlockIndex` and `EraIndex` are zero-based; `BlockIndex` is also the finite era index.
- `OfferRequest.Block` is the zero-based block index.
- `SuspendedFrom` is the phase that was exited.
- After win 18, `RunSnapshot.Round` is 19 while `Phase == AwaitingEndlessChoice`;
  UI must key the choice screen off `Phase`, not `Round` alone.
- B0 is the measured native value; only calculated block-boundary budgets and
  supply bonuses are floored to six decimals. Content/adapter owns native prices.
- `FriendlyForceDestroyed` is adapter truth: all friendly vehicles, not only the
  original player tank. Core never derives it from control type or a single tank.

## Persistence obligations for 3B

- Serialize only this shape plus opaque blueprint bytes/hash; no arbitrary CLR
  types, no native objects, no paths outside the run folder.
- Preserve unsupported future schema bytes unchanged and return an explicit
  resume-blocked result; do not reinterpret them.
- Atomic checkpoint/backup and active-run index; a valid checkpoint must never
  point at absent or hash-mismatched snapshot bytes.
- Missing/mismatched required mods block resume without rewriting or deleting data.
- A recorded `Lost`/`Finished` run cannot resume. Suspended Building/Choosing/
  AwaitingEndlessChoice resumes exactly; loading/fighting replays the exact locked
  plan under a new generation.
- Deserialize `EncounterPlan`/`AttemptIdentity`/`EraReference`/`ScenarioValue`
  through explicit, tested converters. Do not rely on arbitrary interface
  deserialization or platform-dependent ordering.

## Accepted lifecycle decisions

- `ReportCombatStarted(AttemptIdentity)` is an accepted extra engine signal. It
  distinguishes a precombat abort from a combat retreat and is required to enter
  `Fighting`. It is not a public contract change to `Contracts.cs`.
- `Resolving` is not persisted. `AcceptResult` applies the authoritative outcome
  atomically, so no half-applied reward can be stored or resumed.
- `FriendlyForceDestroyed` and `CombatRetreat` are permanent losses; `PrecombatAborted`
  and `TechnicalFailure` suspend/retry without reward or loss.

## Open gaps (not part of the frozen shape)

- **AbandonCurrentRun / Abandoned:** F6 requires an explicit in-app abandon and an
  archived Abandoned run. The current engine only allows `NewRun` after `Lost` or
  `Finished`; adding an `Abandoned` phase/action is a v1.1 seam for the controller/
  storage/UI phase. It must not be silently treated as a combat loss.
- **Endless content exhaustion:** the catalogue can report exhausted qualified
  content, but the engine's `Finish` is only available at `AwaitingEndlessChoice`.
  A controller-level `FinishEndless`/completion action is still needed for an
  endless Building/ChoosingEncounter state.
- **Offer-difference definition:** 3A validates distinct map/layout/sorted roster;
  3C should be checked against the same definition during integration. A mismatch
  must be reported, not patched by weakening either validator.
- **Abandon/Endless actions must not change existing snapshot fields**; if they do,
  the schema version and migration decision must be updated before 3B stores them.

## Storage preconditions accepted with 3B

- **RunId:** the controller must generate a canonical GUID string for every new run.
  Storage maps GUID forms to one lowercase `"D"` folder and rejects non-GUID ids as
  unsafe path input. Engine tests may use other ids; that is not a storage contract.
- **SnapshotHash:** `BlueprintReference.SnapshotHash` is the hex SHA-256 of the exact
  caller-supplied blueprint bytes; storage accepts either hex case and normalizes it.
  Any other adapter hash algorithm/encoding is rejected.
- **Mod matching:** id and version are compared with exact ordinal (case-sensitive)
  matching. Missing or mismatched unions block resume before any write.
- **Content hashes:** the frozen snapshot has no separate map/enemy content-hash field.
  Storage preserves `CatalogueVersion`, `PlanHash` and the required-mod union; the
  adapter/catalogue must verify map/enemy content before load. Adding independent
  content-hash verification is a v1.1 shape decision.
- **Recovery-to-Previous:** an explicit rollback restores the backup as current; a
  still-valid superseded checkpoint may remain in the backup slot. The index hash
  identifies the active checkpoint.
- **Terminal records are derived:** the committed checkpoint and active index are
  authoritative; a failed terminal-record write returns a warning and recovery can
  regenerate the record. Terminal runs remain readable/exportable but never resume.
  The terminal active index clears **both** `activeRunId` and `checkpointHash`; an
  exact duplicate terminal checkpoint returns `Unchanged` with no checkpoint/index/
  record rewrite, while a changed terminal snapshot is rejected `TerminalImmutable`.
- **No blueprint garbage collection:** content-addressed blobs are retained so older
  checkpoints and backups remain verifiable. Growth is bounded by run history, not
  deleted opportunistically.
- **Phase/outcome relationships** remain engine/caller rules; storage validates the
  serialized shape and transaction safety, not gameplay semantics.

# Coordinator amendment — 24 October 2026

Schema 1 and its serialization stay supported. New explicitly opted-in native
runs use schema 2, the optional `Rewards` object and four fights per era. Additive
offer/encounter metadata pins rewards, boss identity and reroll generation;
`AwaitingRecovery` is appended without changing previous enum values. Reward
inventory revisions and granted-offer history are monotonic in storage. See
[the implemented rules](adventure-rewards-2026-10-24.md). The original freeze above
describes schema 1; this amendment is coordinator-owned and user-authorized.

