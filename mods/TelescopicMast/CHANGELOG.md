## 0.9.9

- IO-API v1.2: ApiRevision 2 and RegisterPartSettings(componentId, dataKey, schemaJson, onChanged = null), validated schemas, Kit inspector controls, showIf, immediate per-part storage and isolated main-thread callbacks. Designer editing, Play read-only, settings outside flow undo.
- Defaults, clamping/rounding, enum fallback and unknown-key preservation use ioPartDataV1. Merged twins update both parts; wired/offset pairs remain independent. Existing v1 output callbacks remain active. No new hooks/native writes, Kit/consumer/part-JSON changes or installation.
- Complete inherited gate plus fake testEmitter through real inspector, settings panel, flow and outer Save/Load, mirrored payload and retained Designer-to-Play objects. Live verification remains pending.

## 0.9.8

- Merge coincident mirror twins of Telescope Parts, Rotator Plates, Hinge Leaves and Hinge Mounts into one part-list output, ordered by VUID with a stable fallback. Detection reads MirrorVUID and checks the same base, part GUID and seat within 1 mm; missing references fall back to those same physical criteria.
- Unwired twins follow the primary node and world pose through existing pose setters. Wired secondary nodes remain independent and carry a removable-twin hint. Neither part identities nor graph payloads are merged or rewritten.
- Log twin relation changes only. No hooks, part-JSON changes, deletion, additional hiding, mass changes or new native writes.

## 0.9.7

- Flow clock now lives with the gateway session/FlowEngine, preserving monotonic game time when retained designer objects receive a new VehicleBehaviour on subsequent sandbox runs. No stored-flow semantics changed.
- Event-only cannon receipt diagnostics with vehicle session and selector match count; direct event-to-mast warning, one-tick footer hint and Mast: raise after firing Memory template.
- Mast cylindrical walls / planar caps, hinge planar facets with continuous band offsets, and rotator atlas use native procedural plate density: 1 UV/m, Repeat. RecalculateTangents on visible generators. No position/index/normal, mass/cost/collision changes or seam duplication.
- Regressions execute production TickVehicle input/flow phase, HingeCannons/HingeFire/BuiltinInputs and mast binding/step against native doubles. Native source/asset measurements, byte hashes and Blender renders accompany the full gate.

# Changelog

## 0.9.6 - 2026-11-09

- List registered managed carriers directly in the designer session without requiring VehicleBehaviour or a ready ExternalOutput, retaining M2 labels, registration type/provider and PartId; ready external rows win without duplicates. Brake light can select and persist the lamp wire. Log designer list changes only; warn only for an unregistered carrier. Verify null/non-castable designer Behaviour and saved-state roundtrip to fresh gameplay objects. Gameplay discovery/tick and 24 callbacks on 14 targets remain unchanged.

## 0.9.5 - 2026-11-09

- Fix API lamp discovery in designer and game: read VehicleObjectReader.Items, merge known Save/Load/RegisterFlowPart objects for managed and v1 outputs, preserve external keys and sequence, list mirrored copies through duplicate identity handling, warn once for an unlisted managed PartId, and log scan changes with reader/known/registration counts. Brake light can target the discovered lamp. Require UI Kit >=0.1.12 <0.2.0 (focus-only shortcuts); DetailedLogging is off by default. No added hooks, native writes, installation, Kit or Visual Effects changes.

## 0.9.4 - 2026-11-08

- I/O Flow on Sprocket UI Kit 0.1.11: drag a wire from a port (`OnPortDrag`) with the two-click rules (12 px snap, replace an occupied input, status line and `[FLOW] wire refused`; a no-target drop is silent); Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z through `Panel.AddShortcut` only while the panel owns keyboard focus, with the Kit guard keeping the designer undo out; placement uses `VisibleRect`/`ViewSize` instead of 760x560; Tabs uses the labels getter so add/rename updates in place. Dependency `>=0.1.11 <0.2.0`. No new Harmony patch, native write or Kit change.

## 0.9.3 - 2026-11-08

- I/O API v1.1 (additive, `ApiVersion` 1, `ApiRevision` 1): `SetPartData`/`GetPartData` store one `ioPartDataV1` JSON object per registered external part through the existing `StateMerge` (key/value/part limits, one warning per part); `RegisterManagedOutput` keeps the flow copy and `ioPartId` on the part itself, so it saves and loads without a mast part and can serve three part definitions sharing one ComponentID; `RegisterPartIcon` adds lazy PNG icons under `BepInEx\plugins` on the existing CreateCard postfix. External outputs that were on get one `apply(part,false)` on the designer return and on release. Editor labels use the part name plus a number (native `PartInfoSource`, no new hook). The shim treats the new methods as optional (false/null, one log line). No new Harmony patch, no native write, no v1 behaviour change; 24 callbacks on 14 targets.

## 0.9.2

- I/O Flow editor: drops within 12 px snap to the nearest fitting port, a drop on an occupied input replaces its wire, refused wires show a reason (status line plus `[FLOW] wire refused`); tabs per function (`sheets`/`sheet`, migration to Main, templates open a named tab, cross-tab wires refused); free placement with a 24 px margin; Undo/Redo buttons with 50 steps; the panel and keybinds are renamed "I/O Flow" with unchanged ids. No new hooks, Kit change or engine change.

## 0.9.1

- Add Always on logic; new cannon inputs default to Any group while explicit saved groups retain their filter. Warn about momentary signals driving rotator ports or Follow hinges; verify per-vehicle shot isolation and 0.9.0 unknown-block preservation.

- Preserve the approved rotator geometry and mass; replace triangle atlas UVs with metric charts and indexed crease normals, retain sharp bevels, and package the seven supplied captioned icons. Remove the era wording from Telescope Base.

## 0.9.0

- Added visible Rotator Base/Plate, approved OpenSCAD STL meshes and icons, earliest date, native 0.1–5 scaling with X used for both horizontal diameters, steel-volume mass/cost and Exterior camo.
- Added Rotator run/reverse/fast/home flow ports, bounded 720 degrees/s motion with 0.3 s ramps, shortest-route home and wrap-aware rotator.state. Home takes priority and holds the target. Added held Key mode only to flow nodes, preserving legacy key-rule serialization.
- Added Radar spin and Turntable home templates, stepper parameters, Segmented direction, live footers and an isolated designer preview. Base and Plate each persist the flow and stable part identity through the existing ComponentID filter.
- Runtime uses HingeHost, absolute captured rest poses and Leaf child notifications; restores zero for designer/blueprints and starts each spawn at zero. Mod COM requests are throttled to 10 Hz with a final stopped refresh. DetailedLogging adds per-vehicle ROTATOR PERF statistics every 10 s.
- No new Harmony declarations (24 callbacks / 14 targets), native scale writes, Kit changes or installation.

## 0.8.1

- Hinge Base, Hinge Leaf en zichtbare mastparts krijgen native Exterior-camo met blauwe DPM-mask en render-UV's. Een materiaal per model/context, hergebruikt bij rebuilds en opgeruimd bij Release; bij een fout blijft het vorige materiaal staan en volgt een waarschuwing. Hinge Mount, massa, kosten, botsing en de 24 callbacks op 14 targets blijven ongewijzigd.

- Behoud de pending wire zolang de gekozen poort gelijk blijft; geen End/Begin bij elke SyncSelection. Null-veilige graph/zoomlabel en teardown; tests linken de echte HingePanel-bron en evalueren getters direct bij creatie.
- TEMPLATES: herladen -> Follow, fired -> Delay 0.5 s -> Pulse 2 s -> Follow, en remmen -> vrije API-output/geselecteerd scharnier. Complete keten als één rev/save, zonder dubbele outputparts; bestaande hinge-instellingen blijven behouden.
- Herlaadsjabloon gebruikt notReady zodat Unloaded-gaten de klep niet vroeg sluiten. Losse loading-blokken en de matcher blijven ongewijzigd. Kanonfooter vermeldt bron/modus/groep/fallback; geen voertuigvreemde kanonnen in de bronlijst.
- Native load-state-adapter gedeeld met de end-to-endtests. Tien herlaadcycli (vijf per modus) gaan via standaard any/groep 1, filtering, echte input, flow-engine en HingeMath open/dicht; native haken/beweging blijven een aparte gamepoort.
- DetailedLogging: flow save/load met rev/aantallen/part, outputovergangen met oorzaakblok. 24 callbacks op 14 targets ongewijzigd. Kit niet gewijzigd.
- De oorzaak per oude mislukte run kan niet uit de 0.8.0-log worden bewezen: flow-revisie/draden/outputwisselingen ontbreken.

## 0.8.0 - 2026-11-06 (fase B)
- Het oude paneel vervangen door de Kit-nodegraph met bibliotheek, inspector, voetregels en statusbalk.
- Tweeclickverbindingen in beide richtingen, vervangen van draden, selectie en verwijderen; alle bewerkingen verhogen rev en bewaren alle kopieën.
- Geïsoleerde designer-Fire/Hold-simulatie zonder native callbacks; live signalen op 10 Hz, ontbrekende parts gedimd, LOOP en lusbeveiligingswaarschuwing.
- UI Kit-minimum 0.1.9; publieke graph-API, geen Kit-wijzigingen of reflectie. Hookset blijft 24 callbacks op 14 targets.
- UTF-8-documentatie hersteld; API-contract in IO-API.md.

## 0.7.1 - 2026-11-05

- **Saving merges in place.** `ToBlueprint` no longer replaces `__result.State`.
  Existing entries keep their name, value and type; only `hingeIoV1` is added or
  replaced (through the pure `StateMerge` policy), and a null `State` now gets a
  fresh object instead of skipping the save.
- **Per-frame work moved to our MonoBehaviours.** `HingeHost.LateUpdate` runs the
  I/O tick for every registered vehicle (AI included) after native Update;
  `MastDriver.Update` moves the local player's masts and `MastDriver.LateUpdate`
  corrects the sleeve. The `VehicleBehaviour.GameUpdate` and
  `VehicleController.UpdateControl` hooks are gone. The player vehicle is found
  read-only through the cached local `VehicleController.ControlledVehicle`; pause
  comes from `GameController.PauseState`.
- **Fire path: counter, no struct result.** `CannonBehaviour.FireInternal` has a
  prefix and a postfix, both taking only `__instance`. They read the native
  `ProjectileRegister.nextID` counter before and after the call; only a changed
  counter counts as a shot, and the existing 150 ms duplicate filter is kept.
  `WeaponFireInfo __result` is no longer read; there is no finalizer and no
  `ProjectileRegister.Launch` hook.
- `Verify.ps1` checks the exact 0.7.1 hook set (24 callbacks on 14 targets,
  removed hooks absent), rejects every value-type or by-ref Harmony callback
  parameter, and checks the replacement paths. Remaining hooks unchanged:
  Build/OnModelLoaded, Enable/DisablePhysics, SaveState/ToBlueprint,
  LoadData/LoadState, Release, Cannon.Build/OnVehicleBuilt/Release and icons.
- Version 0.7.1. Existing 0.5/0.6 `hingeIoV1` configs still load and save
  byte-identically. `Diagnostics.DetailedLogging` is off by default.

## 0.7.0 - 2026-11-05

- **Public I/O API v1** (`IO-API.md`): `SprocketTelescopicMast.Io.IoApi` with
  `ApiVersion = 1`, level/event input registration and per-part output
  registration. The copyable client shim is `api/IoApiClient.cs` and is not
  compiled into the mast DLL. Invalid/duplicate ids return `false` and log one
  line; a throwing callback is caught, logged once and disables that registration
  for the vehicle until its next spawn.
- External output parts are found on the vehicle by `ComponentID`, get their own
  engine output id, load their rules through `loadRules` from the main-thread host
  (never inside a native hook), save through `saveRules` after a panel change, and
  receive `apply` only on a state change plus once with the initial spawn/load
  state. The panel's left column shows both hinges and external output parts; an
  external part uses the registration's on/off labels in rule sentences.
- New built-in level input `vehicle.braking` (group Vehicle, source "Vehicle",
  events "brakes" / "is braking (while)"). It only reads
  `PowertrainBehaviour.action` (Braking = 7); for `ClutchBraking` the two brake
  fractions veto one-sided steering. No Harmony patch and no native write.
- Pure `BrakingPolicy`, `OutputChangeGate` and `IoApiCore` support the new tests;
  `Verify.ps1` checks the exact public `IoApi` signatures in the built DLL.
- Version 0.7.0. Existing 0.5/0.6 `hingeIoV1` configs still load and save
  byte-identically. `Diagnostics.DetailedLogging` is off by default.

## 0.6.1 - 2026-11-01

- Telescope Base, Telescope Part and Telescopic Sight Mast dated 1967.01.01 (was 1960.01.01).
- Hinge Base/Leaf/Mount and the I/O panel remain at 0000.01.01; they are tools, not dated equipment.
- Mast movement now checks the owning vehicle date at use, so an imported too-early mast keeps its part identity but does not extend and logs one warning.
- The exact final-era sentinel 9999.12.31 remains available.

## 0.6.0 (development build, 2026-10-31)

- **I/O panel v2 on the Sprocket UI Kit** (IO-PANEL-V2.md, approved board Rules):
  hinges on the left (cards with LEAF/MOUNT, live angle and bar, rule count),
  the selected hinge in the middle with its **rules as sentences**
  (`IF [source] [event] THEN [command] for [pulse] after [delay]`, all inline
  dropdowns/steppers, lamp, delete), **Add rule** (filled in: first cannon,
  fires -> open), three **templates** (case flap, hatch on key, open while mast
  is up), Settings (name, angles, speed) in a collapsible, Test open/close
  buttons, and LOOP / cannon-fallback / no-cannon / read-only warning rows.
  Right: **live signals** with lamps; a click adds a rule with that signal.
- Sources and events come from `IoRegistry`: new optional
  `IoRegistry.DescribeInput(typeId, RuleDescription)` (group, source parameters,
  sources, events with fixed parameters and a "while" flag). A type without a
  description gets a fallback (own group, "triggers" / "turns on" + "is on
  (while)"), so a new input type appears in the pickers by itself. Pure
  `Io/Rules.cs` (RuleModel, RuleTemplate, BuiltinRules) does the derivation and
  edits; `IoCatalog.CannonFireGroups` gives a specific cannon its own group as
  fallback.
- Engine and storage unchanged: rules are the same links in the target hinge
  (`hingeIoV1`); 0.4/0.5 configs load and save byte-identical.
- Removed from the mast: the IMGUI `IoPanel`, its cursor handling, the gameplay
  input block (`HingeInputBlock`, 9 manual patches) and the designer pointer
  guard (`HingePointerHooks` on `Inputs.Update`). The UI Kit does all of this.
  Hinge / Action 1-4 and the mast keybinds are unchanged; the action keys are
  ignored while a kit text field has focus.
- New hard dependency `sprocket.uikit` `>=0.1.1 <0.2.0` (referenced
  Private=false, not shipped). Hinge / I/O panel stays F8, now via
  `UiKit.BindToggle`. The on-screen marker of the selected hinge (IMGUI) is gone.
- Verify.ps1 checks the dependency, the installed kit and its API, and that no
  IMGUI/cursor/pointer/input-block code is left; VerifyKeybinds.ps1 has no
  polling exceptions any more and checks the F8 BindToggle.

## 0.5.0 (development build, 2026-10-31)

- New part **Hinge Mount**: small carrier plate (0.10 x 0.10 m, 10 mm, 20 mm pin,
  origin on the pin, ~0.95 kg steel at scale 1, cost like the leaf). Shares all
  leaf code (rotation, `hingeIoV1` storage, I/O). Visible in the designer, hidden
  in play/test drive via the model's `IVehicleModel.SetVisible` (only the mount's
  own renderer; parts built on it stay visible and swing with it). Keeps its own
  scale (0.1-5) instead of copying the base scale. Not damageable, no damage
  colliders, Physics and Detection collider bits removed; Selection and
  PlacementSurface kept for the designer.
- Inputs (each one class + `IoRegistry.RegisterInput`; storage unchanged):
  `cannon.reloaded` (event, Loading -> Loaded; first load after spawn and
  < 0.1 s blips ignored), `cannon.loaded` (level), `hinge.state` (another
  hinge reached open/closed; level + rising-edge events), `mast.state` (fully
  extended / fully retracted / raised).
- Cannon choice for every cannon input: parameter `cannon` = "any" (fire group,
  0.4.x behaviour) or a specific cannon by VUID, shown as
  "Cannon 2 – 75 mm (group 1)". A VUID not on the vehicle (copy) falls back to
  "any in group N" and the panel says "not found: any in group N".
- 0.4.x links (`cannon.fired` with only `fireGroup`, `cannon.reloading` with
  `mode`) load unchanged and get `cannon=any`.
- Engine safety: at most one command per output per tick (Follow levels
  excepted; two shots in one frame now toggle once). Outputs driven by
  `hinge.state` links that flip 8 times within 10 s are marked LOOP: those chain
  links pause until the configuration changes; one log warning, panel notice.
- Panel: generic Choice parameter (dynamic options, `-`/`+` cycle, readable
  label); Outputs column shows Leaf / Mount and LOOP.
- Built-in input types moved to `Io/BuiltinInputs.cs` and read a pure
  `IVehicleSignals` snapshot, so they are unit-tested.

## 0.4.1

- I/O panel: clicks, dragging and renaming now read the Input System mouse/keyboard; IMGUI drew the panel but never delivered clicks in game.

## 0.4.0 (development build, 2026-10-31)

- New parts **Hinge Base** and **Hinge Leaf** (procedural meshes, scalable 0.1-5,
  available from the earliest game date, steel mass 7850 kg/m3, 0.3.7 mass/COM
  cache fix, native collision mesh retained, mirroring enabled).
- Leaf rotation about the base's local X from an absolute rest pose captured at
  EnablePhysics; rest pose restored on DisablePhysics, SaveState and before
  ToBlueprint, then re-applied on the next tick.
- I/O system (`SprocketTelescopicMast.Io`): public `IoRegistry`, parameter
  schemas (int/float/bool/enum), `IoEngine` (Open/Close/Toggle/Pulse/Follow,
  delay queue, pulse re-trigger extends), versioned JSON per hinge
  (`hingeIoV1`) that keeps unknown input types, commands and fields.
- Inputs: `cannon.fired` (CannonBehaviour.FireInternal postfix; launches that
  another mod cancelled are not counted; 150 ms duplicate filter per cannon),
  `cannon.reloading` (LoadState polling with 0.1 s flicker filter, modes
  loading / not ready), `key` (Hinge / Action 1-4). Output: `hinge`.
- IMGUI I/O panel (Hinge / I/O panel, factory F8): Inputs | Links | Outputs,
  cursor release and gameplay mouse block in game, designer click-through guard
  through `Inputs.pointerOverGUI`, on-screen marker for the selected hinge.
- `Diagnostics.DetailedLogging` (default **false**) gates all
  `[Mast SPAWN]`/`[Mast CHILD]`/mast traces and the new `[Hinge ...]`,
  `[IO ...]` and `[Mast TILT]` traces. Warnings and errors are always logged.
- Verify.ps1: game hash updated to 0.2.56.2; checks all new hook signatures,
  used members and the hinge part files. VerifyKeybinds.ps1 covers the new keybinds (and its Unity
  direct-polling check is now case-sensitive, so ModKeybind.WasPressedThisFrame
  is not mistaken for ButtonControl.wasPressedThisFrame).
- Mast behaviour, parts, GUID, keybinds and config keys unchanged from 0.3.8.

## 0.3.8

- Mast controls through the shared Sprocket Keybinds API; 0.3.7 COM cache fix.
