# Custom Battle host spike — build and live-test protocol

Date: 2026-10-20. Campaign-only debug spike. No installation, game start or
publication was performed from this task.

## Status: PASSED (live, 2026-10-20)

Final installed build: Game DLL
`D83E73337C0D7BFFFF341A7D45B05CB8B3B98DB25FB26E4701B7D7470D785EFA`, Core
`CC31446B32C34AC90EE89892AD692A43EFC7E4CF91267C82FA262953CD1396C0`, journal
`artifacts/install-campaign-20261020-204654`.

Live evidence accepted by the tester:

- locked design captured (`74838` bytes, SHA-256 `d5abc060…`) and resolved to a
  native `UnitDefinition`; player + identical ally spawned from it;
- actual spawns `3/3`, roster `friendlies=2; enemies=1`, `playerTeamID=2`;
- native victory emitted once through `CompleteMission`;
- losing only the player did not end the encounter; the surviving ally continued;
- all friendlies without living crew produced exactly one campaign LOSS;
- the campaign defeat overlay appeared; **Return to Menu** reached the main menu;
- **Retry Encounter** via F7 and via the large overlay panel restarted the host
  lineup; cursor control was restored for the vehicle;
- no fake spawn/result was reported.

Debug-only actions (not release): F6 captures the editor design, F7 starts/retries
the host spike, F11 damages the next friendly for loss tests. The native Custom
Battle fail screen is Retry-only and is deliberately bypassed; the campaign result
overlay owns the visible result. A combat loss remains permanent in the release
contract; the Retry control is a debug action only.

Next milestone: minimal campaign hub/entry plus controller/presenter wiring. The
tank editor is opened from that hub to confirm a locked design; encounters spawn
fresh units from the locked bytes, never the live editor vehicle.

## First-playtest hub harness (in progress)

`CampaignLoopHub.cs` is a deliberately small **fixture harness**, not the final
controller. It exercises one loop with the proven host:

1. In the vanilla tank editor, press **F6** to capture the locked design.
2. Start a Custom Battle with one unit per team; the hub persists across scenes.
3. Click **New Expedition** → the labelled **fixture offer** appears.
4. Click **Begin Encounter**; the host spike starts the real native encounter.
5. The hub receives the actual victory/LOSS result, suppressing the native
   Retry-only fail/success screens.
6. **Retry Encounter (debug)** restarts with the campaign lineup; **Return to Menu**
   uses the native main-menu scene transition.

This harness does not use `ExpeditionController`/`RunStore` yet and the offer is
explicitly a fixture. It exists to prove the playable loop and input/scene handoff;
the next replacement wires the Core controller, catalogue and persistence.

Harness build 1: Game DLL
`54B725A0161D3043D53BF11FC4420DB5F0FDFE99492F4A23B23922EEF8D74831` with Core
`CC31446B…`, journal `artifacts/install-campaign-20261020-205143`.

Harness live finding: the first hub was placed at the top-right and required a
locked design before it would show an offer. In the test it was not discoverable in
the Custom Battle, `New Expedition` appeared to do nothing when no design was
captured, and F6 produced no capture log (the tester was not in the qualified
Defence editor). The harness is being made explicit rather than silently gated.

Harness build 2: Game DLL
`BA575E78E5786BE808CA8571012E5EFEB342989F08E95AC748BA80D8363D030A` with Core
`CC31446B…`. Changes:

- the hub is centered at the top of the screen, not hidden at the right edge;
- buttons **New Expedition**, **Capture Design**, **Use Existing**;
- Capture Design logs its entry and readiness reason and updates the hub status; it
  still requires the settled Defence scenario/tank editor for the real locked path;
- **Use Existing** and **New Expedition** allow the labelled fixture loop to run on
  the currently loaded Custom Battle units when no locked design exists (the true
  locked-design path still requires the capture);
- fixture offer remains explicitly labelled, not qualified content.

Harness build 3: Game DLL
`0F7D84A6E72CD46E8FCE0B07AB67512E5061831773C665C16BF8701A952E81E5` with Core
`CC31446B…`. The Idle/Offer panels now use a **large left-hand action button**
(“Open Fixture Offer” / “Begin Encounter”) because small IMGUI buttons repeatedly
missed clicks in the live game view. Keyboard fallbacks: **F7** advances the hub
(Idle→Offer→Begin, Result→Retry), **F6** captures in Idle / returns from Result /
cancels the Offer. Hub actions log at each transition. The fixture fallback uses the
currently loaded Custom Battle units; the locked-design path still requires the
Defence editor capture. Harness live finding (0F7D84A6): the fixture offer panel was visible, but
**Begin Encounter** repeatedly logged `no host spike component is active`. The
static hub could not see the component because `active` was only published inside
the first `StartSpike`; before any encounter `active` was null. The game also kept
the cursor locked in the active battle, so the small/large Begin click was not
usable and F7 only produced the same failure repeatedly.

Harness build 4: Game DLL
`22A0ECFC2498FA637D161D5A58211B81295F3C2289075C732C8CD0102853FF6E` with Core
`CC31446B…`. The component publishes `active = this` in `Start`, and
`StartCampaignEncounter` falls back to `FindObjectOfType<CustomBattleHostSpike>`.
`CampaignLoopHub.Tick` keeps the pointer visible/unlocked while the hub is
interactive; Begin/Retry re-lock it for vehicle control.

Harness live finding (22A0ECFC): inside the Custom Battle the fixture offer, Begin,
victory/LOSS and Retry all worked. Two UI issues remained: the hub fixture panels
were also visible in the **main menu**, and the small Result **Return to Menu**
button no longer received clicks, so the return path appeared dead.

Harness build 5: Game DLL
`C1B3CF2ED9555D2DB8B7500FCF849E04E20AA0C0A7D9C2C45655B9E535AB2BA3` with Core
`CC31446B…`. The hub is hidden unless a `DeathmatchGameMode` (Custom Battle) or the
Defence `ScenarioGameMode` is loaded, so main-menu fixture panels are gone. The
Result screen now uses two **full-width** rows, Retry and Return, because small
side buttons have repeatedly missed live clicks; F7 remains Retry and F6 remains
Return.

Harness live finding (C1B3CF2E): Return worked with **F6**, but the lower full-width
Return button still missed clicks; Retry was fine. The log also showed repeated
`locked design capture rejected` because the tester was in the normal vehicle
designer, while the qualified capture only accepts the Defence `ScenarioGameMode`
editor.

Harness build 6: Game DLL
`1BD3BB58CDCB277C82945EF7398F0086E84B88B512E529F0D5C2756383CD5C19` with Core
`CC31446B…`. Return is now the **upper** full-width row (the region where clicks
work), Retry is the lower row with F7 fallback. Capture Design first tries the
qualified Defence adapter, then the normal `VehicleDesignerCore` target as a
labelled fixture path; capture readiness is reported in the hub and log. Install/live
test pending.

## Controller-driven first loop (real Core path)

Build `B206BBD95F28E0CAB43329C1563F446FCF766BE2FBE0CCDAB819F863CB273BDB` with
Core `CC31446B…` replaces the fixture-only hub as the active UI with the real
`ExpeditionController`:

- `CampaignControllerHost` constructs a `SeededEncounterCatalogue` from a labelled
  synthetic first-playtest catalogue, a `RunStore` under
  `BepInEx/config/SprocketExpedition/first-playtest-*`, the real controller and a
  minimal presenter.
- `CampaignControllerHub` renders the frozen presenter views: start, building,
  offers, result, run-finished, error/blocked. It enqueues controller commands from
  Update only; no async work is launched from OnGUI.
- The adapter now captures the locked design from the **normal `VehicleDesignerCore`**
  as well as the Defence editor, stores the opaque bytes, and `RunEncounterAsync`
  bridges to the qualified host spike with a main-thread result completion. A real
  victory/LOSS feeds `EncounterResult` back into `RunDirector`; the controller
  persists the checkpoint and presents the next offers/result.
- The fixture cost is 100 and initial budget 200 so the controller's friendly count
  matches the host's two friendly units; the normal designer capture remains a
  labelled fixture path.
- The older `CampaignLoopHub` fixture harness is no longer drawn.

Live test flow: start the controller run in the normal tank editor (F7/Start), then
start a Custom Battle with one unit per team; the offers persist across the scene.
Select an offer (F7 selects the first) to run the real native encounter, then
Continue for the next controller offer or Return to Menu.

Controller live finding (B206BBD9): step 2 rejected with
`Native era references must be unique.` The fixture catalogue has one era and the
host passed that same reference six times; the frozen run engine requires exactly
six unique block eras. This was a fixture configuration abort, not a user action.

Controller build 2: Game DLL
`541E8FBDEE25561DE1EA7570D659C4D8FE0B0E2237FBE83B56DD3CE95E822041` with Core
`CC31446B…`. The fixture now defines six distinct `fixture.era.firstplay.0..5`
references, each with three maps and four enemies, so the run schedule is valid and
every block has fixture content.

Controller live finding (541E8FBD): victory + Continue + selecting the next offer
produced a **TechnicalFailure**. After the first result the host spike kept
`observing=true`, so `StartCampaignEncounter` rejected the next Begin; the native
`CompleteMission` was also being called repeatedly, causing a large result log and
stale callbacks. The controller could not start a second encounter.

Controller build 3: Game DLL
`8EA89979C8EF0B491B396A911E48408DFC2BE24486DA4C314FA77C365376C99E` with Core
`CC31446B…`. The adapter pauses the host observer after the result is delivered; the
next Begin resets and restarts the host. Native result callbacks are suppressed
without logging after the first result and before the next battle has actually
entered; stale CompleteMission calls cannot become a second result.

Controller live finding (8EA89979): the second encounter started, but the offer said
five enemies and the native host spawned only the loaded one; the host still used
the manually loaded enemy lineup. After the next victory, only the top offer row
accepted a mouse click; the lower rows did not. The run/Continue/result path itself
worked.

Controller build 4: Game DLL
`30C7F2A8BF6B076B42796F89980D9F2BC80838EEE673F87F5709ECB34045EF53` with Core
`CC31446B…`. The adapter now passes the plan's total enemy count and friendly count
to the host; the host builds the player team and enemy team with those counts. The
fixture path maps every plan enemy to the currently loaded native enemy
UnitDefinition (count is real, design is still a fixture stand-in). Offers also
support **F6 = next offer / F7 = begin selected** as a keyboard path because lower
mouse rows have missed clicks.

Controller live finding (30C7F2A8): more enemies spawned, but the tester could not
defeat them, and the manual sandbox -> main menu -> Custom Battle -> F7 round trip
made each iteration too expensive. The next step is a placeholder in-battle menu,
not more F7 plumbing.

Controller build 5: Game DLL
`F6F04F467EAC613962136F3C73FD2752823A5A58EA2CD27AD92A9F7F409799CA` with Core
`CC31446B…`. A running Custom Battle is now a first-class placeholder start: the
adapter returns a labelled placeholder design (no native blueprint snapshot) and
uses the already loaded native units, while still passing the plan friendly/enemy
counts. Start the controller directly in the Custom Battle, then use the hub
offers/F6/F7/Continue without returning to the sandbox or main menu.

Controller live finding (F6F04F46): the in-battle placeholder flow and Continue
worked; Continue spawned the expected number of tanks.

## Save slots (item 1)

Controller build 6: Game DLL
`A587CD5823D5D953D38EDA41E83D012D1D0C23D21596E9E7ABB8B7FBE0E6CD8B` with Core
`CC31446B…`.

- Four local slots under `BepInEx/config/SprocketExpedition/slots/slot-1..4`.
- Each slot has the real `RunStore` checkpoint/blueprint store plus
  `slot-summary.json`.
- Summary fields: slot, run id, phase, round, victories, defeats, score (victories
  for now), build credits (`AvailableBudget` + unit), map id, design local id/hash,
  updated UTC. The actual design bytes stay in the RunStore content-addressed
  blueprint file.
- The summary is autosaved after every controller command, including the
  post-victory encounter-result checkpoint; the RunStore checkpoint is saved by the
  controller at each phase boundary.
- The hub now shows four slot rows. F6 cycles the slot, F7 opens it; an active slot
  offers Resume, an empty slot offers Start New Run. Placeholder runs resume without
  an in-memory blueprint by recognizing `player.placeholder`.

Next ordered item: item 2, automatic designs from a faction such as
`Expedition Spawns`; then item 3, a native menu rather than the floating hub.

Save-slot live finding (A587CD58): three enemies spawned; all were destroyed and the
host observer logged `VICTORY (enemy formation destroyed)`, but the round never
ended. The observer's victory fallback only set `resultLogged` and logged; it did not
invoke `CampaignOutcome`, so the adapter/controller never received the result. The
native CompleteMission path was not the one that fired.

Fix build: Game DLL
`7D0278D65F013DE8A85B519253DA3CFBE08E88E67C0057C97F6C19D016237362` with Core
`CC31446B…`. The roster-observer victory path now invokes the campaign outcome
callback exactly once, so a victory without a native CompleteMission completes the
controller result.

Save/resume live finding (7D0278D6): resuming the previously stalled run showed
nothing, because its persisted phase was Fighting and the hub treated EncounterView
as active gameplay even though no native encounter was running. A lost run left the
slot in a terminal state with only a menu option; a new run could not be started.

Fix build 2: Game DLL
`9A6CD2B00823F8FF6F2D382EC03FF7FC033C5E331FAD0C9F754DDFA6F8F2A81C` with Core
`CC31446B…`.

- EncounterView is shown as an **interrupted encounter** panel when no host observer
  is running: Retry restarts the persisted plan, Return exits.
- Return to Menu resets the in-memory controller/view; the slot/run persists on disk.
- A terminal slot (Lost/Finished) offers **Start New Run (archive old)**: the old
  slot directory is moved under `slots/archive/` before a fresh run; the loss state
  no longer blocks another attempt.
- Resume of a healthy ChoosingEncounter/Building/Won state still presents the saved
  offers/building view.

Save/resume live finding (9A6CD2B0): the stalled run recovered and a new run after
death worked. Returning to the main menu after a **victory** hung in an infinite
loading screen (campaign button and native menu). The campaign had suppressed the
native player CompleteMission, so the native mission/mode was not terminal when the
scene change began.

Fix build 3: Game DLL
`7FD1813EED6E9DB3267CE2561FC049401341C059C88CB841E0532FBA982A7C31` with Core
`CC31446B…`. Return now finishes the native player mission first (Complete for a
victory, Fail for a loss, `missionRunning=false`) and waits for the actual native
task before requesting the main-menu scene. Campaign result suppression remains
active until the scene request succeeds, so a native success/fail screen cannot race
the loading transition.

Save/resume live finding (7FD1813E): the campaign Return button still missed the
click, but the native main-menu return worked. After returning and re-entering a
Custom Battle, the hub showed a stale in-memory EncounterView (Retry → failed)
instead of the slot panel; the authoritative slot was actually
`ChoosingEncounter`/round 2. Starting a new run was also unavailable because the
stale view was non-terminal.

Fix build 4: Game DLL
`0497CD400CD5046190974238F9F1C62E036C4D8FDD762E865ED56857D49E3EBD` with Core
`CC31446B…`. When a native/scene return leaves the campaign scene, the in-memory
controller and presenter view are discarded; the slot stores remain authoritative.
Re-entering a Custom Battle shows the slot panel, and Resume loads the actual
checkpoint (ChoosingEncounter → offers, or Fighting → interrupted/Retry).

Save/resume live finding (0497CD40): after returning via the native menu and
re-entering a Custom Battle, the **Encounter interrupted** panel appeared but F7/F6
did not respond. The scene reset skipped while the previous controller command was
still marked busy, so `busy` stayed true and the UI/queued commands were dead. The
resumed encounter had also spawned five enemies; the ally killed four, but the
tester could not damage the remaining/encounter enemies with the player tank.

Fix build 5: Game DLL
`855622D32D713BAB65D0AE758FF4AD40C0E58161C81272E174FF18798F51A940` with Core
`CC31446B…`.

- Scene-end reset now runs even if a command was busy; it clears the queue, clears
  busy, increments a command generation and discards stale completion writes.
- The presenter is suspended on reset so an old controller task cannot overwrite the
  new slot view; a new slot controller activates it.
- Re-entry shows the slot panel and F7/F6/slot actions work again.

The player-damage observation is recorded separately; the ally was damaging/killing
enemies, so this needs a focused combat/resume test after the input/state fix, not a
blind change to damage.

Damage/control finding: the ally was killing enemies while the player's shots passed
through without collision/feedback. The likely native cause is player control bound
to the wrong team/stale vehicle after a retry or resume: the controlled tank treats
the plan enemies as friendlies while the allied AI kills them.

Control-guard build: Game DLL
`2C6E97E1EA9DB489978F1834A7A226DCD8F46CD5E70BDAAD69394855ED2769CC` with Core
`CC31446B…`. When the actual roster is armed, the host reads
`DeathmatchGameMode.vehicleControlState` → `VehicleControlPlayerState.vehicleController
.ControlledVehicle`; if the controlled vehicle is null or not on `playerTeamID`, it
logs the current controlled team and calls the native
`IVehicleControlPlayerState.SetControlTarget(friendly IVehicleBehaviour)`. The ally
AI path was already damaging enemies; this specifically aligns the player's control
and collision/team identity with a friendly tank.

Live finding (2C6E97E1): resume still led to Encounter interrupted → Retry and
non-colliding enemies; this happened consistently even with a freshly started slot.
The retry of an interrupted native battle is not a safe encounter resume.

Safe-pre-encounter build: Game DLL
`149081619D00FF09F7B0AAA9A62E65C01E9471B26019333C6EA34025C54E6AF1` with Core
`CC31446B…`.

- When an offer is selected and before Begin, the whole slot root is copied to
  `slot-N.pre-encounter` (checkpoint, active index, blueprints).
- Resuming a `Fighting`/`LoadingEncounter` slot with a pre-encounter copy does **not**
  retry the broken native battle. It archives the interrupted slot and restores the
  pre-encounter state, so the player gets the selected offer back and can start a
  fresh encounter in a fresh native battle.
- The result screen now states that the checkpoint was autosaved for the slot.
- If an older slot has no pre-encounter copy, the previous interrupted/Retry fallback
  remains.

This makes mid-combat resume safe but deliberate: an interrupted attempt is rewound
to its offer, not resumed natively.

## Faction-sourced enemy designs (item 2)

Faction build: Game DLL
`262422C45A16BEA7D2DD541B685E9FF006EC23500333EE2333283F0B2B8F299A` with Core
`CC31446B…`.

`CampaignFactionDesigns` scans, read-only:

`Documents/My Games/Sprocket/Factions/<faction>/Blueprints/Vehicles/*.blueprint`

Preferred faction name: **Expedition Spawns**. If that folder does not exist, it
falls back in order to the game's current faction, `Nederland`, then `Default`. The
reason is logged. Designs are converted through the same native
`VehicleCard.LoadVehicleCard` + `UnitSelectionLoader.NewUnit` path as the locked
player design.

The adapter maps the encounter plan's enemy groups to the resolved faction designs
(cycled by group) and passes the resulting UnitDefinition/count lineup to the host.
The host uses multiple native designs and the plan counts instead of the single
loaded Custom Battle enemy. If no faction design is loadable, it falls back to the
loaded Custom Battle enemy UnitDefinition and labels that fallback. The map is still
the currently loaded Custom Battle map; native map selection remains a later content
step.

Live confirmation: the tester created **Expedition Spawns** with stock designs. The
log resolved `7 design(s) loaded from faction 'Expedition Spawns'`, plan groups=2,
totalEnemies=4. Faction loading works.

Slot cleanup build: Game DLL
`50B99C3A94E324ED00C1B88C3341011DB300B2BEE8C66263967D3C52632C853D` with Core
`CC31446B…`. The IMGUI slot panel added a full-width Delete/Archive button, but the
live tester confirmed floating IMGUI buttons do not receive mouse clicks, so it was
not usable.

## Native menu (item 3, first shell)

Native-menu build: Game DLL
`959089CF7BB77DF32C9163D8E733E26344D5AF46A8FB182D4F8002B42943FFB7` with Core
`CC31446B…`.

- `CampaignNativeMenu` is a full-screen **uGUI** menu (Canvas + CanvasScaler +
  GraphicRaycaster), not IMGUI. It reads the controller presenter views and uses the
  game's EventSystem, so mouse buttons work.
- `CampaignMenuButton` Harmony-postfixes the game's `MenuPanel.Button` and adds an
  **Expedition** button beside Custom Battle. The native menu can also be toggled
  with the existing F7 key while in a Custom Battle.
- Slots show native **OPEN** and **DELETE / ARCHIVE** buttons; offers show native
  BEGIN ENCOUNTER buttons; result shows native CONTINUE / RETRY / RETURN.
- The floating campaign hub is no longer drawn. The battle itself is pure gameplay;
  reopen the native menu with F7 after the result to choose Continue/Return.
- Creating a new run still needs the designer/Custom Battle context; automatic
  native encounter launch from the menu is the next part of item 3.

Native-flow shell build: Game DLL
`761299BC05E0E967EF08061A16FD568D52AB4CE0752AB345B54C4677CE1523DE` with Core
`CC31446B…`.

- The custom Expedition menu is no longer opened; F7/F6 test menu/capture actions
  are disabled with a log line. The floating campaign hub is not drawn.
- The native main menu now adds **Expedition — New Game** and
  **Expedition — Load Game** beside Custom Battle.
- Both use the game's native **Sandbox** button to open the native vehicle
  designer; New Game sets the new-run pending flag, Load Game sets the load pending
  flag.

Native nesting build: Game DLL
`2429C5FB6F684107B1420DD2DC591E8C73B866BCF897BE018A0F931C4456ADC0` with Core
`CC31446B…`. The top level added a single **Expedition** button and cloned the
game's `MenuPanel` for the New/Load submenu.

Live finding (2429C5FB): clicking Expedition produced no submenu; the cloned panel
`Clear()` threw `NullReferenceException` because the clone was never
`Initiate`-ed. The added button also pushed the native menu buttons against the
bottom edge.

Native nesting fix: Game DLL
`611239D940539BCCF2C3312A727D460E9332BD121DD2F1DA4B79C1A82D7EB6D3` with Core
`CC31446B…`.

- No cloning: the existing initialized game `MenuPanel` is temporarily repurposed
  as the Expedition submenu.
- Native button definitions are recorded; **New Game**, **Load Game** and **Back**
  replace them; Back clears and re-adds the recorded native buttons plus Expedition.
- The main panel is shifted up by one native row height when Expedition is added,
  so the buttons no longer touch the bottom edge.
- New/Load still open the native Sandbox vehicle designer.

Live finding (611239D9): Expedition/New/Load/Back all worked, but after Back the
Expedition button was re-added after Quit instead of below Custom Battle.

Position fix: Game DLL
`9E313B532EA408D7EBD85A690AA89816053DC06555ABB1CBB8F9FEABA327F12A` with Core
`CC31446B…`. Back re-adds the recorded native buttons in order and inserts
Expedition directly after the Custom Battle button (with an end fallback if that
button is absent).

Native Play/offer scaffold: Game DLL
`9FEB1613BF97C459631753236B99D1D80121D8953F869BF5FAD0630787361727` with Core
`CC31446B…` (built, **not installed** while Sprocket was running).

- Harmony prefix on `VehicleEditorScenarioGameState.ExitEditor`, the native
  designer Play/Space path. Ordinary Sandbox Play is untouched when no Expedition
  New/Load flow is pending.
- First Expedition Play suppresses the native exit, starts the controller run,
  captures the settled design through the Defence adapter, and opens the UI-only
  offer screen. Choosing an offer uses `SelectOnly` and closes the screen, returning
  to the native editor with the offer selected and checkpointed.
- Second Play with a selected offer currently shows a clear pending message and
  keeps the designer open. The Defence campaign encounter host is **not faked**:
  the existing Custom Battle host cannot be launched from this Play path, and the
  proof host does not yet spawn the locked player design or feed the controller.
- Load Game slot selection/loading is also still pending; it currently opens the
  native Sandbox and reports the pending slot-selection message.
- The remaining major native gate is the Defence (or bridged Custom Battle) campaign
  host: spawn player+allies+planned enemies, observe one result, feed the
  controller, return to the editor. Do not treat this build as a playable campaign
  loop.

Installation of `9FEB1613…` still requires Sprocket to be closed normally.

Live finding (9FEB1613): the designer Play hook **did fire** and initialized the
controller run, but `CampaignNativeMenu.Open()` failed with
`TypeInitializationException: MethodInfoStoreGeneric_AddComponent_Public_T_0`.
The managed `MenuTicker` MonoBehaviour added to the uGUI Canvas was the unsupported
`AddComponent` call; the exception was caught and native Play proceeded, so the
default Sandbox started without an offer screen.

Offer-screen fix: Game DLL
`1421525CF7780CC70061DB9CD6615618F596CF73D0F12105C36779B2DD49C92E` with Core
`CC31446B…`. The managed ticker component is removed; `CampaignNativeMenu.Tick()` is
driven from the existing `ProbeUI.Update`, which refreshes the uGUI menu when the
presenter view changes.

Live finding (1421525C): the offer screen opened briefly, then the controller
rejected New Game with `An existing run cannot be overwritten. Resume it or recover
it first.` The default slot still held an old active run.

New-game slot selection build: Game DLL
`5DD88CC9ECF8ABF1876F376EDDDCD1B98AA74D8F7B7FCFBD48B02450B9022CBE` with Core
`CC31446B…`. New Game now chooses an empty slot; if none, it archives a
terminal/suspended slot, otherwise the oldest active slot, under `slots/archive`,
and starts a fresh run there.

Live finding (5DD88CC9): the offer-only screen appeared with three offers, but the
buttons could not be clicked; the screen looked unfocused.

Offer-input fix: Game DLL
`C26110F313188930A3F00082050F35F035CDDEF8ED040C8F658956902D5BB338` with Core
`CC31446B…`.

- Canvas sorting order raised to 32760.
- The pointer is unlocked and shown while the offer screen is open (tick + open).
- The game's EventSystem/BaseInputModule is found, enabled if needed and logged
  (`eventSystem=...; active=...; module=...`); a missing EventSystem is reported
  explicitly.
- No new managed MonoBehaviour is added to the Canvas.

Live finding (C26110F3): the game EventSystem was active, but clicks still did not
reach the offer buttons; the module resolved as `BaseInputModule`.

Dedicated-input build: Game DLL
`556CF40565583B27C1E93E6B413763ED9262AE9C2FF81C1BFB575A09C8DA6B60` with Core
`CC31446B…`. The offer screen creates a dedicated EventSystem with
`InputSystemUIInputModule`/default actions and restores the previous one on close.

Live finding (556CF405): clicks still did not register and the screen flickered.

Manual-pointer fix: Game DLL
`CE0BBE77F5C57752EA1808C4DFBDEA392E4ED3DC0BEA12D5537E1A190A4119D7` with Core
`CC31446B…`.

- The offer screen no longer relies on EventSystem dispatch for actions. It records
  each button's `RectTransform` and action, polls
  `Mouse.current.leftButton.wasPressedThisFrame` + `RectTransformUtility` in
  `CampaignNativeMenu.Tick`, and dispatches the first hit.
- The pointer remains unlocked/visible.
- Rebuilds only when the presenter view object changes; the previous `Busy` rebuild
  was causing the flicker.
- uGUI remains the visual layer; native input modules are not trusted for clicks in
  this editor context.

## Defence campaign host — first native slice

Defence-host build: Game DLL
`EFA77E3C3C7DB29B054882F8F3BC417F603A85A6CA2CC3BC6A915B25F6276096` with Core
`CC31446B…`.

- The selected-offer Play no longer shows a pending message. The adapter detects the
  Defence scenario and drives `RuntimeMementoProof` as the encounter host:
  `Prepare()` with the plan's total enemy count (capped at 6), then `Start()` to exit
  the native editor, then awaits the actual victory/all-friendlies loss callback.
- `RuntimeMementoProof` now exposes `CampaignEnemyLimit` and a one-shot
  `CampaignOutcome` callback; the observer's loss and the native player result feed
  the controller exactly once.
- The native Play/Space hook permits the host's own `ExitEditor` via an
  `AllowNativeExit` flag, while ordinary Play is untouched.
- After the result and native editor restoration, the UI-only result screen opens
  automatically; Continue returns to the next offers, Return goes back to the menu.
- First-slice limitations, labelled honestly: the player is the editor's live
  vehicle (not a fresh locked spawn), the ally/enemy blueprints are the fixed
  Defence designs (not faction designs yet), enemy count is capped at 6, and the map
  is the single Defence scenario. No fake spawn/result is used.
Installation/live test pending.

## What is implemented and compiles

`src/SprocketExpedition.Game/CustomBattleHostSpike.cs`, included only with
`-p:CampaignBuild=true` and `#if CAMPAIGN` in `Plugin.Load`.

- Finds exactly one loaded `DeathmatchGameMode` in `Running` state.
- Requires its `BattleConfig.Teams` to contain at least two team definitions and an
  existing `UnitDefinition` on Team 1 and Team 2. If either is missing it logs the
  exact blocker and does **not** invent a unit or spawn.
- Takes Team 1's existing unit and gives it `Count = 2` (player + identical AI ally);
  Team 2's existing unit gets `Count = 1` (planned enemy).
- Restarts the native Custom Battle with `RetryAsyncVoid()`.
- Logs actual `vehicleSpawns` progress, the actual Team1/Team2 roster, and exactly one
  outcome: `LOSS` only when Team1 reaches zero (all friendlies), otherwise `VICTORY`
  when Team2 reaches zero. Losing only the original player is not inferred as a loss
  while an ally remains.
- Exposes `RestoreOriginalLineup()` for restoring the previous team dictionaries for
  the next restart. It is not called automatically while a live battle is running.

Build evidence:

```powershell
dotnet build src/SprocketExpedition.Game -c Release -p:CampaignBuild=true -p:OutputPath=bin/Campaign
# 0 warnings, 0 errors; output includes SprocketExpedition.Core.dll
```

The default proof build remains Core-free and excludes this file.

## What is not implemented yet

- It reuses **existing native UnitDefinitions** from the loaded Custom Battle; it
  does not yet resolve the locked player blueprint bytes to a native `UnitDefinition`.
  The fresh locked-design spawn is the next increment (blueprint resolver).
- Authored spawn positions/layouts are not set; the native battle's existing spawn
  locators are used. Correct-position validation remains live work.
- It does not force a native end screen/editor return; it observes the native Custom
  Battle lifecycle and logs the result. Cleanup/return policy is next.
- No UI, persistence or controller wiring is included in this spike.

## Live-test protocol (requires explicit coordinator/user go-ahead)

**Installed campaign build (2026-10-20):**

- `SprocketExpedition.Game.dll` SHA256 `6E1FE14AE98DFF8803B3AEB25A06BAC4BB14132FD75FB498CB89DCF5E436E1E2`
- `SprocketExpedition.Core.dll` SHA256 `CC31446B32C34AC90EE89892AD692A43EFC7E4CF91267C82FA262953CD1396C0`
- Journal `artifacts/install-campaign-20261020-193949/journal.json`; proof config preserved.
- Debug action pre-bound to **F7**; keybind backup/journal
  `artifacts/keybinds-before-campaign-20261020-194017/`.
- The previous proof DLL `8B9E541B…` is backed up in the install journal.
- **Player-team fix installed (2026-10-20):** Game DLL
  `277379AD092A0BF5FA49CF4F893FB95A1AD151669452E1A082E4B1F850CA330C` with Core
  `CC31446B…`, journal `artifacts/install-campaign-20261020-194646/journal.json`.
  The campaign build loaded cleanly and registered the F7 action; the corrected live
  Custom Battle run is still pending. Previous roster-fix build was `B4D60CE1…`,
  journal `artifacts/install-campaign-20261020-194353`.
- **Load check passed (2026-10-20):** Sprocket started with the campaign build; BepInEx
  loaded `Sprocket Expedition Native Probe`, registered `CustomBattleHostSpike`, and
  registered `sprocket.expedition.campaign/hostspike`; no load/Core exception was
  logged. The live Custom Battle test itself was not started by the coordinator.
- **Third live run (2026-10-20) — spawn/team arrangement correct, labels still wrong:** the log
  showed `playerCustomTeam=0 (flags=Player|Attacker|RequiresUnits)`,
  `enemyCustomTeam=1 (Defender)`, `nativePlayerTeamID=2`; spawns reached `3/3` and
  native `Team1=1; Team2=2`. Because the player's native team is Team2, that is
  actually **two player-side units + one enemy**, not a failed lineup. The observer
  was still counting native Team1/Team2 instead of the player's team. Fix: count
  `tank.ID.TeamID == mode.playerTeamID` as friendlies and the other team as enemies;
  outcome is loss only when friendlies reach zero. Candidate Game DLL
  `793E5E8EEF614505F5599289BB4CA7CD3E84A1F6EC789C25D161CFCDE4778B63`;
- **Locked-blueprint resolver installed (2026-10-20):** Game DLL
  `D3D1732B34EB4B3092F3A8FE91BBB246B01445921DBC6BC5763A143085E95CF8` with Core
  `CC31446B…`, journal `artifacts/install-campaign-20261020-200340`; capture action
  pre-bound to **F6**, keybind journal
  `artifacts/keybinds-before-capture-20261020-200345`. It writes the captured bytes
  to a run-owned cache file (`cache/SprocketExpedition/locked-blueprints`), never a
  named design, and resolves them through `VehicleCard.LoadVehicleCard` +
  `UnitSelectionLoader.NewUnit`. A native resolver failure is reported and no
  existing unit is substituted.
- **Sixth live run (2026-10-20) — LOCKED BLUEPRINT PASSED (log):** F6 captured
  `74838` bytes, hash `d5abc060b8c1ab65e519259ff1d87d6e6a18210b27bcf22c5dfbcb85f6ddab23`;
  F7 logged `locked blueprint resolved to a native UnitDefinition`, cached under
  `BepInEx/cache/SprocketExpedition/locked-blueprints/locked-d5abc060….blueprint`,
  then `3/3` spawns, `friendlies=2; enemies=1`, and one native `VICTORY`. Visual
  identity of player + ally awaits tester confirmation; the log proves the native
  UnitDefinition path, not the rendered mesh.
- **Loss-test build (2026-10-20, not installed):** Game DLL
  `FEAF7217973DFF0C1A18152970901ED66E74E98F98A4D8280DF80191A495799E` with Core
  `CC31446B…`. The roster now counts **living crew**, not merely spawned vehicles;
  unreadable crew never proves death. The native result hook re-reads the roster at
  the result boundary. A debug action (planned **F11**) destroys the next friendly,
  preferring the player-controlled vehicle, so the first press tests player loss
  with an ally still alive and the second tests exactly one all-friendlies LOSS.
- **Live F11 loss probe (2026-10-20) — needs native end drive:** the debug action
  removed all four crew from the targeted vehicle, but the native Custom Battle kept
  running with crewless vehicles. The first run's roster reader did log
  `friendlies=0` and `LOSS`, but the spike only logged it; it did not call the
  native result/end path. The second run reached `friendlies=0` without a second
  LOSS because `resultLogged` was still true from the prior run. This is a real host
  gap: crew-only removal is not a native vehicle destruction, and a repeated battle
  must reset the result state.
- **Native-end loss build (2026-10-20, installed):** Game DLL
  `64BBCBE88B11CD41113F76293F0B83AF35D9EB734C74506840D51D96E4C78D36` with Core
  `CC31446B…`, journal `artifacts/install-campaign-20261020-201616`. The spike calls
  the native player `Mission.Fail()` and `DeathmatchGameMode.ShowFailScreen()` when
  every friendly has no living crew, and suppresses a native player FailMission while
  an ally remains. A native result is skipped only when the spike owns it; victory
  still uses the native path. Result state resets when a new battle generation clears
  the spawns.
- **Seventh live run (2026-10-20) — ALL-FRIENDLIES LOSS PASSED; RETURN GAP:** after
  F11 removed the player crew, the battle kept running (ally continuation), no fail
  screen; after F11 removed the ally crew, the log showed `friendlies=0`, `LOSS
  accepted`, native `Mission.Fail`, then the real native Custom Battle **defeat
  screen**. The screen only offered **Retry** (space), not a Custom Battle editor or
  menu return. This is the remaining host gap: the native `DeathmatchGameMode`
  fail screen is not a return-to-editor screen.
- **Return-gap fix attempt (2026-10-20, installed):** Game DLL
  `89B28BB9B0C10D3EDC6B751BE26C351D4EDA37F7FFAAA2286E1D67DBEE68FFA9` with Core
  `CC31446B…`. A Harmony prefix on `DeathmatchGameMode.RetryAsyncVoid` intercepts the
  Retry action only while the spike owns an accepted loss, suppresses the restart and
  calls the native `ExitGameInternal()` to return to the menu/editor. Initial setup
  Retry and ordinary victory Retry are unaffected (`lossMission` is null).
- **Eighth live run (2026-10-20) — RETRY BYPASSED THE HOOK:** the native
  `ShowFailScreen` task completed as soon as the screen was shown, so
  `FinishLossOutcome` cleared `outcomeStage` and stopped the observer before the
  player pressed space. The Retry hook therefore allowed the native restart; the
  restarted battle spawned only the player (no ally) and no host observer was left to
  report it. Fix: the fail-screen completion now enters **stage 4**, retaining loss
  ownership (and the observer) until the mode actually exits; Retry is redirected to
  `ExitGameInternal` instead of restarting.
- **Return-hold build (2026-10-20, installed):** Game DLL
  `65BBD7BCB0A70AD3CB75E616C1C21BD9F070F265EC57D10F5F12F13D9B89F21F` with Core
  `CC31446B…`, journal `artifacts/install-campaign-20261020-202333`. Stage 4 keeps
  the loss mission/owner alive after the fail screen, and the observer stops only
  when the mode exits.
- **Ninth live run (2026-10-20) — RESETGAME PATH FOUND; BLACK SCREEN:** the fail
  screen's space/Retry calls `DeathmatchGameMode.ResetGame` (not
  `RetryAsyncVoid`). The new prefix intercepted it and called the protected
  `ExitGameInternal()` directly. The game went to a black screen; the pause menu did
  not respond and the tester closed with Alt+F4. `ExitGameInternal` is only the
  protected cleanup step; it does not own the scene transition.
- **ExitGame wrapper build (2026-10-20, installed):** Game DLL
  `AFF762294351C31AC6617456C9C00C7BD215C4AA310FD46A1A70BB11A9ED0F81` with Core
  `CC31446B…`, journal `artifacts/install-campaign-20261020-202726`. The loss redirect
  called the public `GameMode.ExitGame()` wrapper, not `ExitGameInternal`.
- **Tenth live run (2026-10-20) — EXITGAME ALSO BLACK:** pressing space hit
  `ResetGame`, the spike called `GameMode.ExitGame()`, and the screen went black
  again; no scene transition. `GameMode.ExitGame` is lifecycle cleanup, not a
  main-menu scene load.
- **Main-menu scene build (2026-10-04/h installed):** Game DLL
  `AB73643AF36408BAEA5F8426E8B796747C3A353CF9385EBC55E8AC8537838BFF` with Core
  `CC31446B…`, journal `artifacts/install-campaign-20261020-202944`. The return used
  `ISceneManager.Instance.TryGetFirstScene(SceneFlags.MainMenu, out menu)` +
  `GameController.RequestSceneChange(menu)`. The call succeeded, but because it ran
  from inside the native `ResetGame`/failure-screen transition, the game loaded
  `PauseMenu` and stayed black; the native failure coroutine owns that transition.
- **Campaign-result overlay build (2026-10-20, installed):** Game DLL
  `175695D15C6C6599E32239495A50C4E1D8DFBBCF1EC6E8F8AB4B540B1A3B2549` with Core
  `CC31446B…`, journal `artifacts/install-campaign-20261020-203138`. The spike
  bypassed the native Retry-only fail screen and owned a campaign defeat overlay with
  **Return to Menu**.
- **Eleventh live run (2026-10-20) — SPIKE OnGUI UNSUPPORTED:** the overlay did not
  appear after two F11 presses. The log filled with
  `System.NotSupportedException: Method unstripping failed at
  SprocketExpedition.Game.CustomBattleHostSpike.OnGUI()` on every frame (2.1 MB log).
  A managed `Il2CppInterop` MonoBehaviour cannot own this IMGUI entry point in the
  current runtime. No Retry screen appeared, but the game was effectively broken by
  the exception flood.
- **Plugin-hosted overlay build (2026-10-20, installed):** Game DLL
  `BFF1F1A40E36D1E6A16442395FEC3457EB1642EF2F64F44A11BD63FCF6E33BAA` with Core
  `CC31446B…`, journal `artifacts/install-campaign-20261020-203324`. The spike no
  longer defines `OnGUI`; it exposes `LossOverlayVisible`/`RequestLossReturnFromGui`.
- **Twelfth live run (2026-10-20) — DRAW/TEXTURE STILL UNSUPPORTED:** the plugin
  overlay threw `NotSupportedException: Method unstripping failed` at
  `ProbeUI.OnGUI`; `guiFailed` disabled rendering, so no overlay appeared. The
  failure followed the new `GUI.DrawTexture(..., Texture2D.whiteTexture)` call. The
  native `ResetGame` still fired after `Mission.Fail` and the main-menu request did
  not produce a menu. This confirms both the overlay API restriction and that
  failing the native mission drags the Retry screen back in.
- **Thirteenth live run (2026-10-20) — RETURN OK; RETRY DEAD; CURSOR FIXED:** with
  the overlay-only build `381DC9C9…`, the cursor was usable and **Return to Menu**
  reached the main menu. **Retry Encounter** did nothing; the log showed a native
  `ResetGame` being intercepted by the old redirect path and no `debug Retry
  selected` line. The old reset redirect was still trying to request the main menu
  from inside the battle, so it could not produce a clean retry.
- **Overlay-owned retry build (2026-10-20, installed):** Game DLL
  `31D2EF520590F98695C0C5F0BF51852D9C10592A2AC9E4012283B3E4CDA2353F` with Core
  `CC31446B…`, journal `artifacts/install-campaign-20261020-204151`. Native
  `RetryAsyncVoid`/`Retry`/`ResetGame`/`SetupGame` are suppressed while the overlay
  owns the result; the campaign Retry button starts a fresh host lineup.
- **Fourteenth live run (2026-10-20) — RETRY CLICK STILL NOT DELIVERED:** **Return to
  Menu** worked again, but the **Retry Encounter** button produced no
  `Retry button queued` log and no retry. The right-hand button position did not
  receive the click even though the left-hand Return did. Native resets were no
  longer the cause.
- **Centered-buttons build (2026-10-20, installed):** Game DLL
  `E2FDDF873491B1764838D449B9678F5FF0EC7F00764A955E1B4845F5B0862EFC` with Core
  `CC31446B…`, journal `artifacts/install-campaign-20261020-204340`. Keyboard
  fallbacks **F6 = Return**, **F7 = Retry (debug)** work while the overlay is open.
- **Fifteenth live run (2026-10-20) — F7 RETRY PASSES; CURSOR STAYS UNLOCKED:** F7
  restarted the encounter, but the cursor remained visible/unlocked for vehicle
  control. The stacked small Retry button still did not receive clicks; Return did.
- **Large-panel retry build (2026-10-20, not installed):** Game DLL
  `D83E73337C0D7BFFFF341A7D45B05CB8B3B98DB25FB26E4701B7D7470D785EFA` with Core
  `CC31446B…`. The small Retry control is replaced by a **large defeat panel** whose
  whole surface is the Retry button, so a click in the working left/center region
  retries. After a successful debug Retry, the cursor is re-locked/hidden for vehicle
  control. Return to Menu remains a separate button and **F6**; Retry also remains
  **F7**. Install/live retest pending.
- **Overlay-only loss build (2026-10-20, not installed):** Game DLL
  `12A3ABE8646ABB0DAB325A4CDB11BCF5C87FF39A7567B1791E54CBF2C19A70E2` with Core
  `CC31446B…`. The defeat overlay uses only the already-proven `GUI.Box`/`GUI.Label`/
  `GUI.Button` calls (no `DrawTexture`/`Texture2D.whiteTexture`). The loss path no
  longer calls `Mission.Fail()` or the native fail screen at all: it shows the
  campaign overlay while the battle is still Running, and the Return button requests
  the main-menu scene from that coherent state. The native mission is left untouched;
  the campaign owns the loss. Install/live retest pending.
 `playerCustomTeam=0`,
  `nativePlayerTeamID=2`, spawns `3/3`, roster `friendlies=2; enemies=1`. On the
  player win the native hook logged
  `native CompleteMission: mission=Attackers; flags=3; playerMission=True;
  lastFriendlies=2; lastEnemies=1` followed by exactly one
  `outcome: VICTORY (native player CompleteMission), once`.
- Exit without a wipe (earlier A) logged no outcome. The host spike now has live
  evidence for fresh player + identical ally + one enemy, native teams/orders,
  victory-once, and clean exit. The all-friendlies LOSS while Running and the locked
  player-blueprint resolver remain the next live increments.

1. Start Sprocket and load/start a **Custom Battle** with at least one tank placed on
   each team, so both `TeamDefinition` unit dictionaries are non-empty.
2. The campaign debug action is pre-bound to **F7** (Settings still shows
   **Expedition → Custom Battle host spike (debug)** if rebinding is needed).
3. In the running Custom Battle, press **F7** once.
4. Capture the `Expedition campaign host spike` log lines:
   - restart requested;
   - actual spawns count;
   - actual roster Team1/Team2;
   - exactly one outcome, with the all-friendlies rule.
5. Verify visually: fresh player tank, one identical ally, one enemy, native teams
   and orders; player death with a surviving ally does not log loss; all friendlies
   lost logs loss once.
6. Report the exact game/plugin hashes, scenario/map and observed roster. A compile
   or the requested `Count` values are not evidence of a passed host spike.

If Team 1 or Team 2 has no existing unit, the log names that requirement; place a
tank first. If the native restart/roster API differs at runtime, report the exact
exception/line rather than substituting a spawn or result.
