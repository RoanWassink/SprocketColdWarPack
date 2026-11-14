# Presenter/controller contract — Phase 4

Date: 2026-10-20. Status: **controller and presenter boundary implemented and
integration-tested with fake adapter/catalogue/store**. The native `IGameAdapter`
implementation is the next step. This is the coordinator-owned boundary between the
pure Core, the native adapter, and the 3D UI host. It does not change the approved
gameplay in `docs/implementation.md`.

## Ownership and threading

- Core owns `RunDirector`, `IEncounterCatalogue`, `RunStore` and the view models.
- The Game/frontier owner implements `IGameAdapter` (native spawn, orders, outcome,
  editor return, cleanup). The UI never calls native objects directly.
- The presenter is output-only. The UI host calls controller commands on the Unity
  main thread and marshals presenter calls back to the main thread.
- One controller operation runs at a time. Buttons/commands are disabled while an
  async native or save operation is in flight; rendering never launches one directly.
- The presenter and controller never derive native era, budget, fleet count or
  resume semantics. Those come from Core/adapter data.

## Flow

`Start/Resume -> Building -> ChoosingEncounter -> LoadingEncounter -> Fighting ->`
`Resolving -> Building/ChoosingEncounter/AwaitingEndlessChoice/Lost -> ...`

At each block boundary the controller opens the native build editor through
`IGameAdapter.OpenEditorAsync`; otherwise the locked design is spawned fresh through
`RunEncounterAsync`. The adapter returns exactly one consolidated `EncounterResult`;
Core applies it atomically and the controller persists the snapshot before showing
the next screen.

## Presenter surface (output-only)

The UI implements `IExpeditionPresenter`:

- `ShowStart(StartView)` — new/resume/records availability.
- `ShowBuilding(BuildingView)` — era, round/block, budget, quote, fleet count,
  rejection reason.
- `ShowOffers(OffersView)` — exactly three cards with map/layout, enemies, risk and
  pending reward.
- `ShowEncounter(EncounterView)` — round/era, plan, friendly/enemy counts, combat
  state.
- `ShowResult(ResultView)` — victory/loss/technical, supply, next phase/budget.
- `ShowEndlessChoice(EndlessChoiceView)` — continue endless or finish.
- `ShowResumeBlocked(BlockedView)` — missing mods/content, unsupported schema or
  recovery required; data is preserved.
- `ShowError(ErrorView)` — technical error with safe retry/back.

No view model contains native pointers, Unity objects, file paths outside the run
folder, or credentials.

## Controller commands (draft)

- `StartNewRunAsync(NewRunCommand)` — create a GUID run id, accept the measured B0/
  era schedule/catalogue version from content, capture the final native design quote,
  `NewRun` + `ConfirmDesign`, persist, generate three offers, persist, show building.
- `ResumeAsync()` — `RunStore.LoadActiveRun`; block on missing mods/content or
  unsupported schema; restore `RunDirector` (loading/fighting replay under a new
  generation); show the correct phase.
- `ConfirmDesignAsync()` — at a build boundary, capture the final quote and confirm
  the block design; persist; generate offers.
- `SelectOfferAsync(offerId)` — only a stored, non-consumed offer; persist selection.
- `BeginEncounterAsync()` — `BeginEncounter`; persist; call
  `IGameAdapter.RunEncounterAsync`; on return call `RunDirector.AcceptResult` and
  persist before presenting the result.
- `ContinueAsync()` — after the result screen or resume: present Building, generate
  offers for the next round when ChoosingEncounter, or show the terminal/endless
  screen. It never invents an outcome.
- `RetryEncounterAsync()` — replay the persisted Loading/Fighting attempt under its
  current generation.
- `ContinueEndlessAsync()` / `FinishAsync()` — endless choice.
- `SuspendAsync()` — suspend at a safe boundary and persist; the start screen offers
  resume.
- `ResumeAsync()` — restores the active snapshot, loads the verified blueprint bytes,
  restores the catalogue's offer set, marks consumed offers resolved, and resumes a
  Suspended snapshot. Loading/Fighting snapshots present the retry view.
- `RecoverAsync(runId, selection)` — explicit current/previous checkpoint recovery.
- `AbandonAsync()` — **v1.1 seam**: F6 requires an explicit Abandoned archive; do
  not silently treat it as a combat loss.

## Adapter boundary

`IGameAdapter` remains the only native surface:
`CheckReadiness`, `CaptureDesignAsync` (returns the measured `DesignQuote` plus the
opaque native snapshot bytes), `OpenEditorAsync`, `RunEncounterAsync` (with a
`combatStarted` callback), `CleanupAsync`. The adapter owns:
- native player/ally/enemy spawning and team/register setup;
- AI orders and the "play as a surviving ally" control transfer;
- all-friendly-destroyed loss and enemy-elimination victory, exactly once;
- native fail/success screen handling and editor return;
- reset/completion tasks and cleanup before another attempt.

The adapter must not write named player designs, and the player must spawn from the
locked design rather than the editor's live vehicle (see
`docs/battle-editor-methods.md` for the method reference).

**Implemented scaffold:** `CampaignGameAdapter` has real readiness probing and
native design capture (serializer → opaque bytes → SHA-256 → `DesignQuote`). It
deliberately returns `TechnicalFailure` from `RunEncounterAsync` until dynamic
authored spawning is implemented and live-qualified; it does not fake a roster,
combat start or outcome. The campaign build uses `-p:CampaignBuild=true` and includes
`SprocketExpedition.Core.dll`; the default proof build remains Core-free.

## Persistence points

Persist after: run creation, design confirmation, offer generation, selection,
encounter start/combat start, terminal result, and explicit recovery. Use the frozen
`RunSnapshot` shape and `RunStore` transaction rules. A checkpoint is committed before
the next screen is shown.

## Tests for the controller

- `tests/Integration` now runs the implemented controller against a temporary real
  `RunStore`, the fixture catalogue, a scripted `IGameAdapter` and a recording
  presenter. It covers: fresh start, three offers, active-run blocking, exact resume
  with a fresh catalogue instance, selection, victory/round advance, loss/terminal
  clearing, new run after loss, combat-started persistence, technical failure,
  suspended resume and exact-plan retry. All assertions pass.
- Mock presenter records the exact screen sequence.
- Fake `IGameAdapter` returns scripted quotes/results (including loss, technical
  failure and duplicate/stale results).
- Fake `IEncounterCatalogue` returns deterministic three-card sets.
- Real `RunStore` in a temporary root for roundtrip/resume/blocked/recovery.
- Assert one active operation, exactly-once result, no reward on loss/failure,
  persistence before presenter, and no direct native call from presenter tests.

## Open decisions before 3D dispatch

- Source of run seed and measured B0/era schedule (content provider vs adapter).
- Whether the build editor opens at every block boundary or only when a new design
  is required.
- Abandon/Abandoned and endless-content-exhaustion actions (v1.1 seams).
- Native UI host and input lease ownership for 3D.
