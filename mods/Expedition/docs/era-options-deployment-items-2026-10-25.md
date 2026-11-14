# Start-era options, deployment variety and challenge continuity — 25 October 2026

## New Game

Both native New Game entry points now open an options overlay, defaulting to WWI.
The six campaign eras are checked against native playable era definitions. The
selected era begins at its canonical round (WWI 1, Interwar 5, Earlywar 9, Midwar 13,
Latewar 17, Coldwar 21); the campaign continues through Coldwar. Bosses remain the
fourth battle of each era. Earlier rounds do not award victories, funds, items,
processed attempts or veterans. Actual victory totals remain separate from the
canonical round number. Endless still begins at round 25.

The existing measured WWI base and finite-era multipliers govern the starting
budget. A starter from the chosen era is selected from the original curated/native
pool before veterans/community additions, with a community fallback when necessary.
The base budget is raised only if needed to afford two copies; the final live
native editor quote is checked before run creation. No tank costs or native era
dates are invented. The editor's visible budget uses the chosen era's budget.

StartingEraIndex is an optional, immutable checkpoint property; zero is omitted to
retain old run bytes. Old snapshots default to WWI and preserve their existing
progression. The complete six-era schedule/catalogue remains intact. Newly saved
nondefault optional fields require this build on resume. Existing saves are not
rewritten during installation. Veteran records use the genuinely completed era and
canonical round, never a skipped era.

New-run catalogues are now pinned only after final slot selection. This avoids an
era-specific starter/budget colliding with an active slot's immutable manifest
during initialization. Existing active runs are never replaced by selecting a
different start era.

The loading label's literal mojibake `â€¦` is replaced by ASCII `...`. The new menu
text is ASCII, including its campaign-development subtitle.

## Both teams' deployment

A pure planner proposes six bounded transforms of the selected friendly formation:
five translations 80–320 m from its original center with varied headings, then one
rotation at the original center. The exact original formation is a final fallback.
All transformations preserve authored friendly spacing. Native terrain, obstacle,
slope, clearance and authored WorldBounds checks apply to every proposed friendly
and enemy endpoint before any marker changes. The complete enemy front arc or
special pattern must also fit before accepting a friendly proposal.

Both sides' scene-owned spawn markers are moved together before native SetupGame;
assembled tanks are never teleported. Friendlies receive upright heading rotations,
enemies face their center. Existing 350–500 m distance/front constraints apply from
every selected friendly anchor, and cover remains preferred with the documented
open fallback. Special Swarm/Line/Split layouts retain their geometry.

Identity derives from map, era and exact plan hash. Different encounters vary;
retrying the same encounter retains its placement. Terrain probes are bounded,
the cover-ray cap remains 8192 and unsuccessful placement restores native markers.
This is bounded local relocation, not arbitrary placement over the whole map.
Logs record candidate, old/new friendly centers, terrain probes and fallback.

## Items and post-boss choices

Previously every active-item change bumped OfferRoll, cleared all offers and
discarded the selected challenge. Ordinary item activation/replacement/discard now
keeps already selected map, enemies, reward, offer ID and plan hash. Fleet and
budget effects update immediately. A small optional OfferEconomy context retains
the original generation budget/multiplier so exact plans still validate on reload
after budget modifiers change. Enemy-budget modifiers affect future offers; they
do not secretly alter an accepted challenge. Explicit Operational Freedom still
rerolls normal offers, clears selection/context and remains forbidden for bosses.

Losing a budget item can make the current tank unaffordable. That selected refit
state is persistable, but BeginEncounter still refuses it until a fresh valid,
affordable native quote is captured. Editor Play checks the engine's selected
challenge rather than relying on whichever view the armory last presented.

After boss loot is claimed/declined, next-era offers are generated immediately from
the new era budget, before the next editor Play. A narrow adventure-era boundary
permits offer generation/selection before refit. It does not approve the previous
era tank: combat still requires native design and era validation. This also works
after saving/resuming the post-boss boundary or its already selected offer. Initial
run design checks and legacy behavior remain intact.

## Tankpool audit

Read-only tool: `tools/report-spawn-pool.py`. It validates the latest saved catalogue
envelope hash, counts exact eligible blueprint identities, and retains native era
and scalar price. Name/family role guesses have Explicit/Inferred/Review labels.
No physical armour/mass/mobility evaluation is claimed; no pool is automatically
changed. Candidate stationary guns are flagged for review, not removed.

Current sampled catalogue `native.cost.1.d60aa8e127aeadbe`: WWI 22, Interwar 88,
Earlywar 169, Midwar 167, Latewar 166, Coldwar 2; total 614. Three are veterans.
Exact names/variants do not imply distinct tactical families. Coldwar is the
clearest numerical gap. WW2-era heavy-name candidates number 17/13/22, so a general
absence of WW2 heavies is not supported by names alone. Unknown classes remain
explicit. Shortlists cap obvious families at two variants and spread name roles
and native prices; they contain 15/25/25/25/25/2 suggestions, not an applied pool.

User report and complete CSV:
the original development workspace (not part of this release).
No native code, logs, saves or personal tanks were sent to an external worker.

## Validation and package

- Adventure: 18 scenarios, including all six starts under schemas 2/3, zero fake
  wins/grants, checkpoint/retry/Coldwar completion/endless, pinned-item refit.
- Integration: selected challenge after items, budget/enemy modifiers, resume,
  Play-time re-quote; post-boss offers before Play, selected reload and new-era quote;
  native-era adapter capture for a later start; existing loop/recovery/capacity.
- CampaignContent: new deployment rigid-transform/determinism/front-range checks,
  late-start veteran unlocks, existing archive/spawn/map/music/AI/startup checks.
- Run 30/30, Persistence 30/30, Encounters 106/106, Presentation 19/19.
- Current slot/catalogue validation is read-only; no saves repaired or rewritten.
- Campaign and proof builds have zero warnings/errors. Native UI/terrain/loop tests
  remain user-controlled. No game process was launched or controlled.

Package: project-root `bin/EraOptions` and `bin/EraOptionsProof`.
Game DLL: `14B546C51F3CC12FE91F619E4B97F2D686BF057B57C5A79410F1F69BCD3AF93F`.
Core DLL: `C9BE852CFD57472E4FF399843ED4DAB3796C6BEC66D46E172AECBB0814170780`.
Music manifest retained:
`841CE9243D27763DA42C7E3A321C69857EE68A552B8D5A824827C994A6278A91`.
Evidence: `artifacts/era-options-*`; before-source snapshot:
`artifacts/era-options-before-20261025-173113`.

Installed with Sprocket closed after the user's confirmation. Receipt:
`artifacts/install-campaign-20261025-175150/journal.json`; verification:
`artifacts/era-options-install-verification.json`. Source hashes and installed
baseline were rechecked before installation. Configuration stayed byte-identical;
root/executable ACLs stayed unchanged with inherited Medium integrity. The existing
checkpoint JSON was additionally copied to
`artifacts/era-options-checkpoint-backup-20261025-175142`; no live save was changed.
All 70 compiled music/PCM method bodies match the previous installed build and
user-owned Audio/Local was not an installer target. BuildOutput was
`../../bin/EraOptions`.

User checks after installation:

1. New Game: clean `Reading blueprint cards...`, chosen era, matching starter and
   budget; existing Load Game retains its own era.
2. Select a challenge, edit items, close inventory, Play: same map/enemies/reward,
   no repeated offer selection. A genuine reroll remains an explicit exception.
3. Boss victory, claim/decline loot: next-era offers appear before editor Play.
   Select one, refit, Play: it starts that already selected challenge.
4. New encounters vary the player's start position/heading as well as enemies,
   upright and inside bounds. Retry stays stable; note any fallback log/map.
