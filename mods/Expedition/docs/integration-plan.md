# Phase 4 integration plan

Date: 2026-10-20. This follows `docs/implementation.md`; it does not replace the
approved gameplay or acceptance rules. 3D is deliberately **not** started first:
the plan requires a coordinator-provided presenter/UI host and a tested native UI
handoff before UI screens are written.

## Goal

One playable campaign loop with the real native adapter:

`RunDirector <-> IEncounterCatalogue <-> RunStore <-> IGameAdapter <-> presenter/UI`

The Core is already implemented and green (149/149 tests). The proof already proves
player/ally/enemy spawning, win/loss, editor return, repeat and editor restoration.

## Workstreams and order

### A. Presenter/controller contract (coordinator, first)

- Freeze a presenter interface for: start/new/resume/records, building, three-card
  choice, battle HUD, resolving, loss, endless choice, technical failure,
  missing-content/resume-blocked and abandon confirmation.
- Commands are serialized to the main-thread controller; rendering never launches
  async native work directly.
- The presenter never derives native era, budget, fleet count or resume semantics.
- Provide a mock presenter for buildable tests.

### B. Native campaign adapter (frontier, in parallel with A)

- Implement `IGameAdapter`: readiness, design quote capture, build-boundary editor,
  encounter run, cleanup.
- Reuse the proven native lifecycle from the Defence memento proof (editor exit,
  roster/outcome observation, fail screen, reset tasks, editor restoration).
- For the release, the player must spawn from the locked design, not the editor's
  live vehicle. Use the Battle Editor method reference (`Battle.PreparePick`,
  `BattleFile.WithPicks`, authored spawn plan/team mapping) as the source of
  methods to adapt; do not install or link the beta mod.
- All friendly vehicles in the encounter share the locked design; defeat is the
  adapter's consolidated all-friendly-destroyed result.
- No named-design overwrite, no native data serialization into Core, no UI calls
  from the adapter.

**Current status:** the scaffold implements `CheckReadiness`, real native design
capture with an opaque blueprint snapshot and SHA-256/hash quote, the settled-editor
guard and cleanup. It compiles against the installed interop. `RunEncounterAsync`
deliberately returns `TechnicalFailure` instead of faking a spawn or outcome:
dynamic authored spawning from the encounter plan (fresh player/allies/enemies,
teams/orders/register, outcome and editor return) is still to be implemented and
live-qualified. The default proof build remains Core-free; the campaign build is
`-p:CampaignBuild=true` and includes `SprocketExpedition.Core.dll`.

### C. Integration controller (coordinator, after A/B contracts)

- Start new/resume; capture quote; confirm design; generate/restore offers;
  select; begin; run; accept authoritative result; persist atomically.
- Exactly-once result handling, stale generation rejection, retry same plan with a
  new generation, technical failure without reward/loss.
- Wire `RunStore` checkpoints before the next UI screen.
- Handle the known v1.1 seams explicitly: `AbandonCurrentRun`/Abandoned and
  endless-content-exhaustion completion.

### D. UI host and 3D (after A/B; worker)

- Establish the native UI/input lease and hand off `Game/UI` explicitly.
- Implement the frozen presenter screens; mock tests first, live screenshots and
  input capture as manual acceptance.
- No direct spawning/scene/save calls from UI code.

### E. Content / balance (parallel once authoring starts)

- Three qualified maps and at least four checked enemy designs per native era.
- `content/example.catalogue.json` stays a labelled fixture until real checked
  content replaces it.
- Battle Editor/Map Framework methods may inform map/spawn authoring; no runtime
  dependency and no third-party content without provenance.

### F. Phase 6 acceptance

- Full live acceptance, Packman closed-game install/backups/hash journal, rollback,
  ordinary scenarios unchanged, FPS/object-count comparison.

## Immediate next actions

1. Presenter/controller contract, output-only view models and `ExpeditionController`
   are implemented. `tests/Integration` passes fresh start, resume, victory, loss and
   technical-retry against a real `RunStore` with scripted adapter/catalogue/presenter.
2. Implement the native campaign adapter against the proven proof lifecycle.
3. Then connect the live adapter and dispatch 3D against the frozen presenter.
4. Start content qualification in parallel when the adapter's content inputs are
   stable.

Remaining non-blocking live checks from the native gate can run alongside A/B:
ally orders/friendly fire, leave during loading, mod/ERA reload, normal editor Play.
