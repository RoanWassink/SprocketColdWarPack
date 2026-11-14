# Sprocket Expedition

This workspace contains the uninstalled 0.3.12 development candidate. Installation
and live-test statements below describe earlier builds, not this candidate.

## Boss-kandidaten (0.3.12)

`[BossBlueprints] Midwar = Panzer VIII Maus|Obj. 962.II` selects one of these
Latewar designs for each new v7 boss round. Names or native IDs are separated by
`|`; invalid or wrong-era entries warn once and are ignored. With none valid,
the existing automatic fallback applies. A single valid entry retains the old
catalogue bytes and hash. Multiple candidates are pinned in the archive; seed,
era and round determine the pick, including the preview and boss card. Existing
runs keep their archived choices. No config or game files are changed by this build.
See [test plan](docs/TESTPLAN-0.3.12.md).

## Design caps (0.3.12)

`[Campaign] DesignCaps = T-84 Oplot-M Duplet (WT-maten)=2` limits matching native
designs in new v7 catalogues. Use exact names and positive integers separated by
`;`. Invalid entries warn once and are ignored; the first valid duplicate wins.
Over-cap units are replaced from the same allowed pool within twelve slots and
the threat target where possible. All risks, specials and boss escorts share the
limit. Existing pinned runs, older schemas and test pool retain their plans.
`Autosave*.blueprint` in Expedition Spawns is skipped and logged before card reads.
See [live test plan](docs/TESTPLAN-0.3.12.md).

## Slots (0.3.12)

- `[Campaign] SaveSlots = 8`, range 4-12; restart to apply. Existing slot paths stay intact.
- A full Play / New Game screen lists all runs (era, round and local save date),
  with **Delete & start new** and **Back**. One confirmation archives the selected
  slot under `slots/archive`, then opens the usual era / Ironman options.
- Opening an empty slot starts native New Game options and keeps that slot through
  the editor handoff. Missing native content stops creation; it cannot fall back
  to synthetic enemies. `[Diagnostics] AllowFixtureCatalogue = false` gates new
  fixture runs; an existing fixture save can still load. With this developer
  option enabled, empty slots may use the diagnostic fixture start screen.
- Slot rows scroll within the shared UI Kit; Back stays in the footer.
- This candidate is not installed or tested live. See [test plan](docs/TESTPLAN-0.3.12.md).

## Hotfix designer-crash (0.3.12)

- The Shrapnel battle-stats hook is gone. Its empty `HitDamageModel` shared a
  native address with unrelated methods (IL2CPP ICF), which crashed face deletion
  in the designer.
- Every remaining battle-stats hook reads its native `methodPointer` and the first
  16 bytes first. A trivial body (`ret`, `ret imm16`, `xor eax,eax; ret`, or a `ret`
  in the first 8 bytes) is skipped with one log line instead of patched.
- The kill-event postfix only reads `KillEventArgs` when the native class of
  `__instance` is exactly that type.
- New cfg `[Diagnostics] BattleStatsHooks = true`. Set it to `false` to install no
  battle-stats hooks; the defeat screen then shows `—` for the statistics and
  `cause unknown` for losses.

## Meer verrassing (0.3.8)

New runs use schema 7. Runs with schema 6 or older keep their exact pinned offers,
formations, rewards and bosses.

- **Yellow specials are back.** A normal (non-boss, non-test-pool) round has a 25%
  chance that one card becomes a special: Rat Plague (Swarm), Steel Wall (Line) or
  Crossfire (Split). The card keeps its risk and the schema-6 threat target; a
  special that cannot reach that target falls back to the normal card. Specials
  never use Dangerous-only designs on a Cautious or Standard card. The reward is
  yellow (`RewardRarity.Special`) with the existing family, magnitude and duration.
- **Ambushes are more common.** Every tactical group of two or more hulls can get
  an Ambush: 1 in 2 with flankers, 1 in 3 without. Without flanker roles half of
  the group takes the side bearings (3/9 o'clock) instead of one front line.
- **Bosses have character.** The Fortress escort holds a compact defensive box;
  the Hunter gets light/recon flankers in an Ambush; the Battery escort spawns in
  the deepest support lane; the Veteran is unchanged.
- **Boss combat behaviour** (runs, not saves): the Fortress and its escort hold,
  turn the hull toward the target and fire, with a lateral fallback of at most 20 m
  and one slow step after 90 s without contact. The Hunter pursues the player and
  its escort flanks. The Battery fires at any range with a clear shot and withdraws
  once the player closes inside half the first-contact range, sideways only.
- One line per boss battle:
  `Expedition boss character: <design> -> <behaviour>`. Boss cards show a short
  character line under the name; special cards show their title.

## Dangerous-only (0.3.7)

Designs in `BepInEx\config\SprocketExpedition\community-blueprints-dangerous\` use
the same content-addressed `<sha256>.blueprint` markers and `content-library`
blobs as the normal community pool, but receive `MinimumRisk = Dangerous` in the
catalogue. A Dangerous-only design is allowed only in a Dangerous plan; it is
never a boss, boss minion, reward (Lend Lease / Captured Prototype), starter or
veteran. Dangerous enemies count in that card's cost distribution and threat
target; Cautious and Standard plans are generated from the pool without them and
stay unchanged. A marker present in both community folders is imported from the
dangerous folder only, with one warning per duplicate; without the folder nothing
changes. Older rules versions, the test pool and existing pinned catalogues are
untouched, and a catalogue without the field serializes and hashes exactly as
before. Logs: `Expedition dangerous-only designs: <era>=<n>; ...` at catalogue
build and `Expedition dangerous-only: round <r> uses '<name>'` for a Dangerous
plan that uses one. See [0.3.7 test plan](docs/TESTPLAN-0.3.7.md).

## Defeat-scherm (0.3.6)

A lost battle with combat now shows two extra Kit blocks. **Losses** lists every own
tank (at most six, then `+n more`) with `destroyed by <design> at <m> m`, or
`destroyed (cause unknown)`. **Battle stats** shows the player's tank separately and
one team-total line: rounds fired/hit, accuracy, hits taken, hits survived and kills,
plus the longest hit and battle duration. Victory, abort and technical failure are
unchanged. After every battle one always-on log line per own tank reports the same
numbers:

`Expedition battle stats: <outcome>; <player|ally> '<name>' fired=<n> hit=<n> acc=<%|—> taken=<n> survived=<n> kills=<n> destroyedBy=<'design' at <m> m|unknown|alive>`

The counters live only in the running encounter (no save changes). Fire and hit
attribution use observing Harmony hooks on the native projectile launch and
damage-model hit; an explicit native killer is used when the game publishes one,
otherwise the last enemy hit within 15 s. A hit older than 15 s or no hit at all
stays unknown. See [0.3.5 test plan](docs/TESTPLAN-0.3.5.md) for the AI behaviour and
[0.3.6 test plan](docs/TESTPLAN-0.3.6.md) for this screen.

## Vijand-AI (0.3.5)

A stopped-to-fire hull now has to show fire progress: a new shot, at least 0.5°
better gun error within 2 s, or at least 2° hull turn within 2 s while a
`FaceTargetTask` runs. Reloading pauses the 8 s stall clock. A stalled hull has no
clear shot for that target and drives to a flank point 25–40 m sideways and 15–30 m
toward the target, alternating sides on every next stall; `TryGround` validates the
point and the existing approach waypoint is the fallback. The leg ends on arrival,
after 12 s or after 60 m, then the normal clear-shot stop/fire decision resumes with
a fresh tracker. Ambushers only log and keep their position. Log line:
`Expedition team AI: <enemy|ally> slot <n> no firing progress for <s> s (shots=<n>, loaded=<j/n>, has=<j/n>, error=<°>, hullAngle=<°>); repositioning to (<x>, <z>)`
(an Ambusher ends with `; holding (ambusher)`); every `AI path sample` adds
`fireStall=<s>`. See [0.3.5 test plan](docs/TESTPLAN-0.3.5.md).

## Testpool (0.3.4)

The test pool now moves on after every fought battle, won or lost (user decision
2026-11-02: "Yes, after any fight"). Victory, defeat and Retreat (Return to Editor
after contact) advance the cursor by the tanks in that battle; an aborted
preparation or a technical failure does not, because nothing was fought. After a
lost test battle the same round is offered again with the next four tanks: no
reward, Defeats and RoundAttempts rise as before, and RETRY keeps the lost card's
risk selected. Ironman still ends on a loss; normal runs keep "same cards after a
loss". Existing saves load as they are (no migration); the cursor check now allows
up to 4 × (victories + defeats). Each fought test battle logs
`Expedition test pool: offset <old> -> <new> after <outcome>`.
`stats\test-pool-results.csv` gains `tank_gespawnd`, `contact` (ja/nee/onbekend)
and `contact_gevecht` (ja/nee) as last columns; an old file only gets its header
line replaced. Per-tank contact means the tank got an attack order from the team
AI, lost crew health or was destroyed; a hit that harms no crew is not seen.
`Diagnostics.DetailedLogging` now defaults to `false` (an existing cfg keeps its
value). See [0.3.4 test plan](docs/TESTPLAN-0.3.4.md).

## Ambusher (0.3.3)

Enemy designs whose v0.2 blueprint has no rotating turret ring between the main gun
and hull, or whose whole azimuth chain is below 60°, receive the `Ambusher` combat
role. That covers casemates such as FV-90, Grille 17 and Grille 21. The rule is
campaign-wide, so WW2 casemates such as StuG, Jagdpanzer, Nashorn and Grille are
covered when their own blueprint meets it; Wirbelwind and AMX-13-90 stay ordinary
turret tanks.
An ambusher reacts in a fixed 1.00 s, turns its hull toward a visible target while
that timer runs, holds its position while it has a target, and only creeps forward
after 45 s without a sighting. During front-arc placement its design slot gets the
highest already validated marker (bounds, cover, upright and spacing unchanged) and
the spawn rotation already points the hull at the friendly deployment centre.
`[Combat] AmbusherDesigns` and `NotAmbusherDesigns` override detection by design
name. Per-ambusher log line:
`Expedition ambusher: enemy slot <n>; design='<name>'; source=detected|config; height=<m> above player spawn; facing=<deg> to player.`
See [casemate detection](docs/CASEMATE-DETECTIE.md) and
[Ambusher test plan](docs/TESTPLAN-AMBUSHER.md).

## Test pool

Set `[Campaign] TestPool = true` in the plugin config before starting a new run.
Put new `.blueprint` files in `BepInEx\config\SprocketExpedition\test-pool\`.
The New Game note shows the accepted count; at least three distinct tanks are
required. Every file receives an acceptance/rejection row in
`stats\test-pool-report.csv` under the same config directory. Native card checks
retain each tank's measured cost, name, era and date; stationary names are allowed.

Test runs pin the accepted pool and run flag. Each normal round shows the next
three different tanks sorted by name, one copy per card, regardless of enemy
budget or native era. A boss round shows the next one with normal boss presentation
and music. Every fought battle advances the rotation (0.3.4, see above); aborted
attempts and rerolls keep the same tanks. Items, player budget, scoring and retry rules remain normal. Test records
are marked `test_pool: true` / `TEST` and never unlock veterans. Changing the config
or folder does not alter an existing run's pinned content.

`battles.csv` appends `test_pool` (0/1) and `enemy_designs` (names joined by `|`)
for all runs, with atomic upgrades of the previous 31/32-column formats. Test
battles also log the roster's observed enemy spawn count. Default-off catalogue
hashes, offers and checkpoint/record bytes are verified against 0.3.0; native
spawning and firing still require the player's live test.
See [test instructions](docs/TESTPLAN-TESTPOOL.md).

A guarded autoloader testbuild is installed for the Leopard/T-84 unloaded-weapon
failure. It repairs a missing captured loader connection only when detected and
adds bounded stock/state traces; native firing remains unverified. See
[bustle AI loading investigation](docs/bustle-ai-loading-2026-10-26.md).

T-84 Oplot-M Duplet now replaces both T-84 Oplot-S and Sprocket Oplot-rep for new
runs. Older pool copies, including the Oplot-rep autosave, are preserved in reserve;
Nederland originals and pinned campaigns remain intact. See
[preferred Oplot replacement](docs/oplot-duplet-replacement-2026-10-26.md).

Seven approved community tanks are imported for new runs: four Coldwar, two
Midwar and one WWI. Four Expedition copies use the explicitly requested native
era; Downloads originals remain unchanged. ZZ-2 retains the author's cheat warning
and awaits a native spawn check. See [26 October additions](docs/community-additions-2026-10-26.md).

Known-threat turret pre-orientation is installed on top of the era-range build;
native hull facing during stopped fire remains in place. See
[known-threat orientation and lifecycle checks](docs/known-threat-facing-2026-10-25.md).

The installed testbuild restores increasing spawn distances through all six eras,
including Coldwar 1,200–1,500 m, and increases late-era combat stand-off distances.
Read-only gun diagnostics investigate the T-84 allies that do not fire; that cause
is still unresolved. See [era ranges and T-84 weapon investigation](docs/era-ranges-t84-weapons-2026-10-25.md).

A testbuild is installed with backup for explicit first-offer selection in every starting era,
advancing allied roles, clear-shot firing holds and more visible two-team spawn
variation. Gameplay verification awaits the user's test. See
[Coldwar/AI/deployment fixes and checks](docs/coldwar-first-offer-ai-deployment-2026-10-25.md).

The complete per-era active selection is now applied for new runs: expected
WWI 22, Interwar 27, Earlywar/Midwar/Latewar 25 each and Coldwar 16, including
current veterans. Unselected community copies are retained in reserve. See
[full-era curation and verification](docs/all-era-curation-2026-10-25.md).

Fifteen approved faction designs are imported as immutable copies: fourteen
Coldwar and the updated C42 Falcon-S in Latewar. New runs read the expanded pool;
saved runs keep their pinned catalogues. See [faction additions](docs/faction-import-2026-10-25.md).

New Game options, ASCII loading text, varied two-team deployment and selected
challenge continuity are installed with backup for testing. Post-boss loot now leads directly
to next-era offers before editor Play. See [25 October changes and checks](docs/era-options-deployment-items-2026-10-25.md).

A forward-travel AI trial is installed with backup (25 October).
It limits automatic reverse on owned waypoint legs longer than 18 m and adds
heading/signed-speed diagnostics. See [PZ4 reverse-driving trial](docs/forward-travel-2026-10-25.md).

New Game now prepares blueprint cards in frame-budgeted batches with a visible
progress message. Unchanged community card metadata is reused within the same
process, with file/hash/native-era invalidation. See [startup responsiveness](docs/newgame-startup-2026-10-24.md).

The build editor now has an ITEMS / BONUSES button under the fleet budget. It opens
the armory before Play, supports active-item management and returns to the same
editor/tank without a scene reload. See [editor item access](docs/editor-items-2026-10-24.md).

Full item slots now stay in the armory with an actionable notice and active-item
discard controls. Native exit ResetGame is allowed and suppressed Task-returning
restart calls return a completed no-op instead of null. Stationary combat now
allows native hull facing before braking, including casemate vehicles. Installed
fixes and current evidence: [SU-100Y, inventory and menu return](docs/su100-inventory-return-2026-10-24.md).

The soundtrack now follows the phase: Mechanical Tension in the Expedition menu and
loading, the new Minimal Focus Loop while building in the editor, the new Heavy
Industry in normal combat and The Final Blow in boss battles, with Triumph Complete
for victory and the new Skill Issue replacing Mechanical Fadeout for defeat. See
[music expansion](docs/music-expansion-2026-10-24.md) and
[boss soundtrack](docs/boss-music-2026-10-24.md). Track lists for every phase are configurable with comma-separated WAV files under `[Expedition Music]` (relative to the plugin Audio folder, for example `Local\my-track.wav`); selections rotate per encounter and fall back to the built-in track. Per-era combat pools (`CombatWWI` ... `CombatColdwar`) can override the shared Combat list; an empty key inherits it. See [music configuration](docs/music-config-2026-10-24.md).

AI now mixes close attackers, medium-range line tanks and distant fire support,
with stable 1–5 second sighting reactions weighted by measured enemy price. Attack
handoff cancels the separate native waypoint channel; stationary firing uses a
native halt task. Menu-return guards survive the actual unload, and pause-button
listeners detach before destruction. Installed trial and checks:
[combat roles and menu return](docs/combat-roles-return-2026-10-24.md).

Activating another item in an already active reward family now replaces the old
item with the new strength, tank and full duration, including with three occupied
slots. The inventory shows REPLACE ACTIVE and durations in victories.
See [replacement rules and checks](docs/reward-replacement-2026-10-24.md).

Boss AI now privately tracks the currently player-controlled tank, including
behind cover and after control handoffs. Native visibility still gates attack
orders; ordinary enemies retain their existing sighting memory. Installed test
build and self-test: [boss player tracking](docs/boss-player-tracking-2026-10-24.md).

## Current rules - schema 7 (new runs), schema 6 and 5 (existing runs)

New native runs use schema 7. Every included native era has four fights: fights
1-3 use enemy-budget factors 0.70/0.90/1.10, and the fourth is the boss at an
explicit 1.15 × 1.35 boss factor (Standard risk). Endless blocks have four wins and use 0.70/0.90/1.10/1.15 with
three cards and no boss.

- The enemy target comes from the base budget (era base + supply + earned funds),
  never from item multipliers, discounts or free allies. Difficulty, era
  progression (+5% per era) and risk still apply.
- The offers of a round are generated once and stay fixed until the round is won.
  Activating, replacing or discarding an item changes your fleet budget and the
  editor quote immediately, but not that round's offers. Only Operational Freedom
  rerolls them. A loss, technical failure or aborted preparation keeps them too.
- Experimental Contract's +20% enemy applies to offers generated after
  activation, so from the next battle, not the running round.
- Schema 6 records run totals (attempts, enemies destroyed, friendlies lost) and
  writes one row per finished attempt to
  `BepInEx\config\SprocketExpedition\stats\battles.csv`.
- The Difficulty screen now shows the real final-era value for the planned run
  ("+5% per era; the final era of this run: +X%").

### Losing, Ironman, retiring and score (schema 6)

Normal runs allow unlimited retries after defeat or retreat. The same cards and
selection remain available: RETRY returns to the editor, CHOOSE ANOTHER BATTLE
opens those cards. Failure grants no loot, funds or supply and consumes no item
duration. Each failure raises Defeats and RoundAttempts; a victory resets only
RoundAttempts. Attempts counts every completed attempt, including free aborts
and technical failures. Emergency Recovery no longer drops in schema 6; schema 5
retains its original loot and recovery rules.

New Game offers IRONMAN: OFF/ON. This mode is saved at run creation and cannot be
changed. One defeat ends an Ironman run with a terminal Lost record. Mode-tagged
records keep Normal and Ironman scores distinct; Ironman scores gain ×1.3.

Return to Editor is free before contact. Contact is the first confirmed tank
destruction on either team or 60 seconds of combat time, latched per generation.
Unreadable or incomplete rosters do not imply a death. No native hit hook is used:
this adapter has no verified attempt-scoped hit event. After contact, a normal
return counts once as a failed attempt and writes `Retreat` to the battle log;
Ironman disables the button and shows the reason. Native Resume and stale-result
guards retain ownership of pause dismissal and cancellation.

RETIRE RUN appears while building or choosing a battle. It asks “End this run and
save the record? This cannot be undone.” Confirming saves a terminal Retired
record and clears the active index. It grants no battle reward.

A win earns `100 × (1 + 0.5 × eraIndex) × risk`, with risk 0.75/1/1.5 for
Cautious/Standard/Dangerous, ×3 for a boss, and +25% when the roster reliably
reports zero friendly tanks lost. Unknown losses receive no flawless bonus.
Then apply `max(0.25, 0.75^RoundAttempts)`, the pinned difficulty score factor,
and ×1.3 for Ironman. Recruit/Regular/Veteran/Ace retain enemy multipliers
1/1.25/1.5/2 and score multipliers 0.75/1/1.25/1.6. At constant difficulty this
equals the sum of battle points multiplied by the run's difficulty and mode.
Changing difficulty affects future cards and their score, never past gains or
the current pinned battle. Decimal score is saved exactly and rounded only for
display. Victory shows its gain and breakdown before boss loot; records show
score, furthest era, mode and Lost/Finished/Retired outcome. HUD contents stay
unchanged. Offer cards add actual enemy cost / current available budget and the
first card screen of each era previews the same deterministically selected boss.

`battles.csv` appends `score_gained` as the last column. An existing phase-0
31-column file is extended atomically, preserving quoted multiline rows and
leaving old scores empty. Unknown headers and failed writes are reported and
preserved; even a failing warning callback cannot interrupt gameplay.

Managed validation and the in-game checklist: [0.3.0 test plan](docs/TESTPLAN-0.3.0.md).
BalanceSim reads the supplied pinned catalogue; its report explicitly marks the
three late eras supplemented with synthetic price candidates. Those curves are
diagnostics, not proof of native content capacity or combat difficulty.

Runs with schema 1-5 keep their exact rules, offers and saved bytes, including
schema 5's old three-round curve (0.65/0.90/1.15), which rotates because an era
has four fights. The yellow specials (Rat Plague, Steel Wall, Crossfire) are
active in schema 7 only; schemas 5/6 keep their exact old offers without them. The
boss is one curated card every fourth fight.

Standalone Sprocket roguelite in development. The editor/Custom Battle loop is a
live-tested foundation; the full native campaign still needs player qualification.

The community import contains 614 distinct vehicle blueprints from the supplied
Downloads collection; component blueprints and byte duplicates are excluded.
Native cards determine each design's own era and cost. New runs can use the pool;
existing slots keep their archived opponents. A shared content-addressed library
avoids loading or embedding the whole 3.5 GB collection into each save. Native startup card validation now accepts all 614 designs (0 skipped); live playability of the full pool is still pending user testing.

Managed adventure checks walk all 24 fights and test persistence, expiry,
recovery, stale callbacks and changed offers. Controller checks cover item
activation, boss choice save/reload and recovery save/reload. These checks and
interop compilation are separate from live acceptance. Boss names remain configurable placeholders until the user chooses their final designs.

## Verification - 24 October 2026

Rebuilding both configurations reproduces the installed and packaged DLL hashes exactly. All
managed suites were rerun: 30/30 run, 106/106 encounters, 30/30 persistence, 3/3 integration,
18/18 presentation, 10/10 campaign-content groups, 1/1 player control and 12/12 adventure
scenarios (210 checks, no failures). A startup run of the installed build accepted 614/614
community designs with 0 skipped; it was sandboxed, so the native faction write was denied and
the scene hand-off faulted. A normal Steam launch is required for gameplay.

## Previous milestone â€” 21 October 2026 (schema-1 history)

New Game â†’ native tank editor â†’ native Play â†’ fresh Custom Battle â†’ victory â†’
choose a challenge â†’ improve the tank â†’ native Play â†’ next battle.

The user's 21 October loop supersedes the old restriction against editing within
a three-battle block. Upgrades are allowed before each encounter; economics and
era progression now advance every four victories (four fights per era). Custom Battle/Deathmatch is
the combat host. Defence scenario/memento tooling remains historical proof only.

- Core: 30 run, 106 encounter and 30 persistence tests pass.
- Presenter: 18 managed screen/action tests pass.
- Controller integration passes, including exact offers/tank restore, corrupt
  blueprint/missing-mod blocking, safe slots and explicit interrupted-fight retry.
- Main menu Load Game now opens the four existing slots and restores the verified
  saved tank in the editor. It does not launch a battle automatically.
- Menus show three offers with composition, risk and supply; results, loss,
  technical failure, records and endless choice use the presenter boundary.
- New Game does not reclaim active, suspended or unverified saves. Manual slot
  archiving requires an explicit in-game confirmation and preserves the files.
- New runs start with a native WWI tank and progress through every native era the game lists
  (WWI to Coldwar, plus later eras such as Coldwar2 and Coldwar3 when installed), ordered on start
  date; an era that is not playable or has no usable non-stationary tank is skipped. A run keeps the
  era schedule it started with. Four battles per era. The gamemaster ranks formations by actual
  saved design price times quantity; cards show real tank names and prices.
  Enemies resolve by exact content identity and keep their own native era/date.
- Native price/era build: WWI editor, two victories, editor upgrades and a third
  fresh battle have run live. All schema-1 rounds/six-era boundaries pass managed checks;
  the complete native campaign still needs live/content qualification.
- Both campaign and proof-only builds compile against installed interop. Managed,
  installed and live evidence are distinct: see the ledgers below.

Latest baseline loop evidence: [editor/Custom Battle](docs/editor-custom-battle-loop.md).
Current increment and native checks: [menus and resume](docs/menu-resume-2026-10-21.md).
Current price/era milestone: [native eras and price-based offers](docs/native-era-cost-milestone.md).
Editor budget correction: [actual campaign fleet budget and live count](docs/editor-fleet-budget-2026-10-21.md).
Save/content protection and map offers: [catalogue archives and recovery](docs/catalogue-recovery-maps-2026-10-22.md).
Current spawn experiment on Fields, Defence and Dunes: [front arc and cover checks](docs/map-cover-spawns-2026-10-23.md).
Current rally and playable-area trial: [mutual spawn waypoints](docs/rally-ai-2026-10-24.md), [native map bounds](docs/rally-map-bounds-2026-10-24.md).
Team tactics for friendly and enemy tanks: [24 October AI testbuild](docs/team-ai-2026-10-24.md).
Enemy AI pursuit instead of fixed map waypoints: [shared last-seen hunt](docs/enemy-hunt-2026-10-23.md).
Earlier Fields trial: [random front arc at 350â€“500 m](docs/fields-front-arc-2026-10-23.md).
Earlier spawn/HUD evidence: [spawn distance and live roster counter](docs/fields-spawn-trial-2026-10-23.md).
Soundtrack: [intro/background, victory and defeat music](docs/soundtrack-2026-10-23.md); [build, combat and defeat expansion](docs/music-expansion-2026-10-24.md); boss track above; [music configuration and rotation](docs/music-config-2026-10-24.md).
Pause-menu refit: [Return to Editor and Escape-crash repair](docs/pause-menu-crash-repair-2026-10-23.md).
Personal enemy progression: [era-winning tanks become Veterans](docs/era-veterans-2026-10-23.md).

## Next work, in order

The packaged Adventure build is installed; user playtesting of the campaign/item/boss/recovery
loop and the 614-design pool is the next action before the items below.

1. Live-qualify the enemy pursuit on Fields, Defence and Dunes: enemies leave the fixed map
   waypoint, advance from their spawns, share the last-seen position, search after lost sight and
   switch to the aggressive attack order. Confirm allies are not targeted and non-Expedition AI
   is unchanged. See [enemy hunt](docs/enemy-hunt-2026-10-23.md).
2. Complete live qualification of native battles in later eras. The third WWI
   victory and return/load into the Interwar editor are observed. WWI start,
   exact price-based enemy selection and repeated edited-tank battles have passed.
   Legacy fixture saves retain compatibility.
3. Complete fresh retry after interrupted loading/combat and menu input acceptance.
4. Qualify at least three maps and four enemy designs per native era, measure the
   three WWI starters for B0, then tune the encounters and risk differentiation.
5. Finish explicit checkpoint-recovery/abandon flows and finite-content exhaustion.
6. Full acceptance: ally control/orders, win/loss once, cancellation/retreat,
   repeated sessions, other scenarios/mods unchanged, performance and rollback.

Full gameplay/economy: [implementation](docs/implementation.md).
Do not call fixture content release-ready or restore a scenario-based campaign route.

## UI

Since 0.2.0 (31 October 2026) the Expedition menu (slots, New Game options, difficulty,
offers, armory, results) and the battle HUD run on the shared **Sprocket UI Kit**
(`sprocket.uikit`, required `>=0.1.3 <0.2.0`, installed separately under
`BepInEx\plugins\SprocketUiKit`; the Expedition package never ships the Kit DLL).
The menu is one modal Kit panel with a dim backdrop and Kit sub-pages; the Kit handles
clicks, Esc, the in-game cursor, blocking of fire/camera input and designer
click-through. Screens, buttons and their actions are unchanged; the battle is still
frozen while choices are open. Esc does what the screen's CLOSE/CANCEL button did, and
nothing on screens without one (battle result, preparation). The battle HUD is a Kit
HUD at the top centre. Native buttons (main menu, pause menu) and the editor budget
label stay as they were.
`Diagnostics.DetailedLogging` (default `false` since 0.3.4) traces menu
open/close, screen changes and HUD show/hide. `tools\Verify-UiKit.ps1` checks the port statically.

In-game checks: [UI Kit test plan](docs/TESTPLAN-UIKIT-2026-10-31.md).

## Build and tests

```powershell
dotnet run --project tests/Run -c Release
dotnet run --project tests/Encounters -c Release
dotnet run --project tests/Persistence -c Release
dotnet run --project tests/Integration -c Release
dotnet run --project tests/Presentation -c Release
dotnet run --project tests/CampaignContent -c Release
dotnet run --project tests/PlayerControl -c Release
dotnet run --project tests/Adventure -c Release
dotnet build src/SprocketExpedition.Game -c Release
dotnet build src/SprocketExpedition.Game -c Release -p:CampaignBuild=true -p:OutputPath=bin/V0312
```

Test environment (all optional; a missing fixture folder skips only the saved-slot checks): `EXPEDITION_FIXTURES` = folder with the saved slots `slot-1` and `slot-2` (and, next to them, a `content-library\blueprint-blobs` folder with the blueprints they reference; override with `EXPEDITION_FIXTURE_LIBRARY`), `SPROCKET_BLUEPRINT_DIR` = folder with the five reference blueprints of the casemate check (FV-90, Grille 17, Grille 21, Flakpanzer IV Wirbelwind, AMX-13-90), `EXPEDITION_TEST_TEMP` = scratch folder for the slot tests. `tools\Verify-All.ps1 -GameRoot <folder>` builds both configurations and runs everything.

The builds compile against the Sprocket interop assemblies; pass the Sprocket folder (or a copy of its
`BepInEx\core`, `BepInEx\interop` and the UI Kit DLL under `BepInEx\plugins\SprocketUiKit`) as
`-p:GameRoot=<folder>`.

The default proof build stays Core-free. Campaign installs require both Game and
Core DLLs, placed together with the `Audio` folder and `THIRD-PARTY-NOTICES.md` in
`BepInEx\plugins\SprocketExpedition`. No post-build deployment. Install only with Sprocket
closed and keep a backup of the previous files and configuration.
Do not redistribute native references, personal designs or logs. Never use the Sprocket installation folder as an agent workspace: the Windows sandbox lowers that folder's integrity label and Sprocket then starts in low-integrity mode (editor faction writes fail). See docs/campaign-access-denied-2026-10-24.md.
