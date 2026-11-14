Sprocket Telescopic Mast, release **0.9.9**. Author: Nero.
Acceptance: `tools/Run-Gate.ps1` (runs every check once in a scratch copy; see the file header).

## 0.9.9 - IO-API v1.2 part settings

`ApiVersion = 1`, `ApiRevision = 2`.
```csharp
bool IoApi.RegisterPartSettings(string componentId, string dataKey, string schemaJson,
    Action<GameObject>? onChanged = null);
```
Register the ComponentID with `RegisterOutput` or `RegisterManagedOutput` first, then register settings once.
The copied reflection shim `api/IoApiClient.cs` includes the optional v1.2 binding and an example;
older masts return false, so consumers keep their defaults. The facade takes only BCL/Unity types.

Schema: a JSON array up to 16 fields / 4 KB (UTF-8). Each field has a unique `[a-z0-9_]`
key (1..32 characters), label (1..40), type and optional unit (up to 8).
Float/int fields require finite min/max/default and optional positive step; bool requires a bool default;
enum requires 1..16 unique `{value,label}` options and an existing default value.
`showIf: {"key":"enabled","value":true}` (or an enum value) controls visibility only.
Unknown types, invalid conditions, duplicates and invalid limits fail registration with one reason warning.
`dataKey` follows the v1.1 `<prefix>.<name>` rules, at most 64 characters; `io.` is reserved.

Select an output block to see **Settings** in the Kit inspector. Numbers show unit/range, bools a toggle,
enums a dropdown; controls are editable in Designer and read-only in Play. Settings save immediately
and are outside flow undo (also stated in the panel). No new blueprint state key: values are one JSON
object under dataKey inside `ioPartDataV1`, limited to 4 KB/value and 16 KB/part.
Missing/invalid values use defaults; numbers clamp, ints round halfway away from zero, unknown enum
values fall back, and unknown JSON keys survive. Consumers must apply the same defaults/bounds when reading.
The host prepares defaults after load, outside hooks; settings edits save all targets before callbacks.
`onChanged(part)` runs on the main thread from the panel; errors warn once per failed invocation and
leave the value intact. DetailedLogging emits only actual edits as
`[IO SETTINGS] component=<id> part=<partId> key=<dataKey> changed=<field>`.

Coincident settings outputs use the existing twin grouping (same parent/base, GUID and position).
An edit updates both parts of an unwired merged twin and calls both consumers; a wired secondary keeps
its own values. Off-centre mirrored parts have independent settings. Existing RegisterOutput consumers
with settings retain loadRules/saveRules and use the existing flow carrier for designer visibility/identity.
Native copying/mirroring carries the existing part-data state. Kit remains >=0.1.12 <0.2.0.

Validation uses fake ComponentID `testEmitter`, all four types and bool/enum showIf, the production
inspector/settings panel, flow, outer Save/Load and state merge with native/Kit boundary doubles.
Logs/payloads: `logs/settings-unit-0.9.9.txt`, `logs/twin-tests-0.9.9.log`,
`logs/settings-payload-0.9.9-*.json`. These are managed evidence; in-game rendering and the native
blueprint copy/reload still need the three TESTPLAN v0.9.9 steps.

0.9.8: Coincident mirrored children on one base appear as one output; the lowest VUID leads. Children on separate bases stay independent. A previously wired secondary node keeps controlling its own part and shows "twin of ..., can be removed". To prevent a twin, turn mirroring off when placing a child on a base in the centre.
TESTPLAN v0.9.8 has three live steps.

0.9.7: in-game checks are TESTPLAN v0.9.7 (three steps).
Use **Mast: raise after firing** to hold the raise signal after a shot. A direct Cannon fired wire remains one tick; the editor warns about that connection.
Native procedural plate density is 1 UV/m; native imported part atlases vary.

# Sprocket Telescopic Mast 0.9.9

## 0.9.6 - Registered lamps in the designer

I/O Flow lists every session's managed carrier with a registered ComponentID,
even when the designer vehicle has no runtime VehicleBehaviour or ready external
output. Each row uses its PartId, registration type/provider and M2 part name/number;
a ready external output wins for the same PartId. Only an unregistered carrier warns.
Brake light can select the lamp and save its wire as ioFlowV1/ioPartId on the parts.
DetailedLogging is off by default; when set to true it logs changed designer lists as
`[IO EXT] designer list parts=<n> registration=<id>:<n>`.
Gameplay Items discovery, external carrier identity and apply ticking are unchanged.
Requires **Sprocket UI Kit >=0.1.12 <0.2.0**. No new hooks.
The real-designer doubles (null/non-castable Behaviour), save and fresh play roundtrip run in `tools/Run-Gate.ps1`.
The three v0.9.6 TESTPLAN steps still require in-game confirmation.

## 0.9.5 - API lamps in I/O Flow

External discovery uses the object reader's `Items` plus parts already known from
Save/Load and `RegisterFlowPart`. The generic behaviour reader enumerates components,
so `GetAll<VehicleObject>()` missed lamp objects in both designer and game.
Managed and v1 outputs share discovery, readable part names and stable sequencing.
Mirrored copies receive their own identity via the existing duplicate path.
Brake light connects Vehicle braking to a discovered lamp output.
In 0.9.5 a managed PartId without a ready output emitted one `[IO EXT] managed part not listed:`
warning (superseded by the registered-carrier list in 0.9.6). DetailedLogging is off by default; when set to true, changed discovery emits
`[IO EXT] scan vehicle=<ptr> reader=<n> known=<n> found=<registration>:<n>`.
Requires **Sprocket UI Kit >=0.1.12 <0.2.0**, whose shortcuts reach only the focused
panel. No new hooks, native writes, Kit or Visual Effects changes.
Production-path discovery tests with fake managed and v1 registrations run in `tools/Run-Gate.ps1`.
Native in-game lamp behaviour still needs the three v0.9.5 TESTPLAN steps.

## 0.9.4 - I/O Flow on Sprocket UI Kit 0.1.11

The I/O Flow editor uses the additive Kit 0.1.11 API. No Harmony callback, native
write or Kit change was added (still 24 callbacks on 14 targets).

- **Drag wires.** Press a port and drag: the Kit draws the dashed wire and reports
  `OnPortDrag` on release. The drop follows exactly the two-click rules (12 px snap
  scaled by zoom, replacement of an occupied input, the same refusal reasons in the
  status line and `[FLOW] wire refused <reason>`). Clicking still works. A drag that
  ends on the canvas without a port in reach changes and reports nothing.
- **Undo/redo keys.** `Ctrl+Z`, `Ctrl+Y` and `Ctrl+Shift+Z` via `Panel.AddShortcut`.
  They act only while the I/O Flow panel owns keyboard focus; the Kit's 0.1.11
  keyboard guard suspends the game actions, including the designer undo. The toolbar
  buttons stay.
- **Placement.** The editor uses the graph's real `VisibleRect` (and `ViewSize` as a
  pre-layout fallback) instead of a fixed 760x560 picture. A new block lands in the
  visible part when there is room and the view follows it otherwise; the 24 px
  no-overlap rule is unchanged.
- **Tabs.** The Tabs labels getter updates the same tab elements in place, so
  renaming or adding a tab does not flicker through a rebuilt bar.
- **Dependency.** Sprocket UI Kit `>=0.1.12 <0.2.0`; BepInEx does not load the mast without it, and the mast never ships the Kit.
- Reproduce with `tools/Run-Gate.ps1`.

## 0.9.3 - I/O API v1.1: per-part data, managed outputs, part icons

`ApiVersion` stays 1; `ApiRevision` is 1. The additions are optional for a consumer:
`SetPartData`, `GetPartData`, `RegisterManagedOutput` and `RegisterPartIcon`. An
older mast has no `ApiRevision` (0), and the updated `api/IoApiClient.cs` then
returns false/null with one log line. The three v1 methods and `RegisterOutput`
with `loadRules`/`saveRules` are unchanged.

- **Per-part data.** `SetPartData(part, key, value)` stores one JSON object under
  the part's own state key `ioPartDataV1`; `null` removes a key. Only parts whose
  ComponentID was registered through `RegisterOutput` or `RegisterManagedOutput`
  are accepted. Keys use `<prefix>.<name>` with `[a-z0-9._-]`, at most 64
  characters, `io.` is reserved, a value is at most 4 KB and the whole object at
  most 16 KB. A limit returns false with one warning per part. The key is written
  through the existing `StateMerge`, so every other state entry keeps its name,
  value and type.
- **Managed outputs.** `RegisterManagedOutput(id, displayName, componentId,
  apply, onLabel, offLabel)` takes no rules callbacks. One registration serves one
  ComponentID (three lamp definitions may share `vfxLamp`). The mast keeps the
  flow copy and `ioPartId` on each part exactly like the mast's own parts, so a
  managed flow survives save/load even when the vehicle has no mast part. In the
  editor each part is named `<part name> <n>`; the name is read through the native
  `PartInfoSource` by `VehicleObject.GUID` (no new hook) with the registration
  `displayName` as fallback.
- **Part icons.** `RegisterPartIcon(partGuid, pngPath)` takes an absolute PNG under
  `BepInEx\plugins`, at most 2 MB. The first registration per GUID wins and the
  image is decoded only on the first `CreateCard` for that GUID, on the mast's
  existing postfix. The mast's own seven icons keep priority and bytes.
- **Designer/release.** An external output that was on receives exactly one
  `apply(part, false)` when the vehicle returns to the designer or the part is
  released. A throwing callback is caught; nothing reaches native code.
- No new Harmony patch (still 24 callbacks on 14 targets), no native write, no
  managed flow-engine change. Reproduce with `tools/Run-Gate.ps1`.

## 0.9.2 - I/O Flow: reliable wires, tabs, free placement, undo

The flow editor is now called **I/O Flow** (panel title and keybinds
**I/O Flow / Open panel**, **I/O Flow / Action 1-4**). Internal ids are unchanged
(`hinge-panel`, `hinge-action-N`, panel `hinge-io`, config keys), so existing
bindings and the remembered panel position keep working.

- **Wires.** Click an output port, then an input port (or the other way round). A
  click within 12 px of a fitting port counts, also when it lands on the block body
  (0.9.1 cancelled the wire there). Dropping on an input that already has a wire
  replaces that wire. A refused wire says why in the status line (*would create a
  loop*, *not compatible*, *blocks are on different tabs*, *already connected*) and,
  with `DetailedLogging`, logs `[FLOW] wire refused <reason>`. Since 0.9.4 the same
  rules also apply when dragging from a port (Sprocket UI Kit 0.1.11).
- **Tabs.** Above the canvas: **+** adds a tab, *Tab name* renames it, **Delete
  tab** removes an empty tab at once and a tab with blocks after confirmation (the
  last tab stays). A template opens in a new tab named after it; on an output that
  is already placed it joins that output's tab. Wires stay inside one tab; all tabs
  run together as one vehicle flow. Stored as `ioFlowV1.sheets` plus a `sheet` per
  block; a flow without tabs opens as one tab **Main**. 0.9.1 keeps both fields and
  runs every block (one canvas, no tabs).
- **Free placement.** A new block or template chain never lands on another block:
  its rectangle plus 24 px is free, preferably in the visible canvas, otherwise to
  the right of the content (the view follows it). Since 0.9.4 the visible part is
  the graph's real `VisibleRect`.
- **Undo/Redo.** Toolbar buttons, 50 steps per vehicle while the panel is open:
  blocks, wires, parameters and modes, drags (one step per drop), tabs and
  templates (one step). Each undo/redo saves normally with a new `rev`. 0.9.4 adds
  Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z while the panel owns keyboard focus.


## 0.9.1 - Rotator surface and supplied part icons

The Logic palette now includes **Always on** (`always`): no input and one continuously
active output, footer ON. Connect it to rotator.run for continuous rotation or a
Follow hinge to keep it open. New cannon input blocks select **Any group**
(`fireGroup=0`); saved explicit groups keep their filters. The editor warns when
a momentary input drives a rotator port or Follow hinge: use Memory or Pulse
for a lasting signal, or Toggle on a hinge. Older 0.9.0 builds preserve `always`
as an unknown block without executing it.

The approved STL shape, steel mass and costs are unchanged. The rotator now uses
indexed vertices, area-weighted normals with a 40-degree crease, and metric UV
charts with one shared scale for both parts. Cylindrical bands unwrap behind the
part on -X. The 45-degree bevels and 90-degree edges stay sharp; the approved
Base's eight-sided bolt edges also stay sharp because they meet at 45 degrees.
The Plate's twelve-sided bolts, drum, rim and hub have smooth side normals.
Seven supplied captioned icons are validated without regeneration.

Reproduce the full gate with `tools/Run-Gate.ps1`.
Export the exact embedded meshes with `py tools\stl_to_mesh.py --check --obj logs\rotator-obj`.
The remaining in-game checks are in `TESTPLAN.md`.

## 0.9.0 - Rotator Base and Plate

Place **Rotator Base** on the hull or turret, then attach **Rotator Plate** to it. Build the radar or other parts on the Plate. Both use the approved OpenSCAD geometry, native Exterior camo and steel-volume mass/cost. The Plate keeps its own scale; its mounting plane seats on the Base. Without a Base it stays still and warns once per Plate.

Use the native scaler, 0.1 to 5 per axis. **X sets both X and Z diameter; Z is ignored so the turntable stays round. Y sets height.** The mesh compensates for native Z scaling; no native scale values are written. The approved Base is 400 mm diameter by 60 mm; the Plate body is 360 mm by 20 mm, with its approved raised hub/pointer reaching 26 mm. The original notch and bolts are unchanged.

The **Rotator** flow output has four independent input ports:

| Port | Behavior |
|---|---|
| `run` | Spin while on. All ports off brakes to a stop at the current angle. |
| `reverse` | Invert the configured CW/CCW direction. |
| `fast` | Select fast speed instead of slow. |
| `home` | Takes priority over run/reverse; approach the target by the shortest angular route, then hold while on. |

Parameters: `slow` 1–360 degrees/s (30 default), `fast` 1–720 (90 default), `direction` CW/CCW (CW default), and `target` 0–359 degrees (0 default). CW means clockwise when looking down onto the Plate. Speeds ramp over 0.3 s; there is no physical inertia or torque model. Home brakes smoothly if already spinning; braking distance can carry it past a very close target before it returns and holds. Numeric fields use steppers; direction uses Segmented. Live footers show direction, speed, angle or home holding.

**Radar: spin while key held** connects a held Key to `run`; **Turntable: go home on key** connects a held Key to `home`. Select a Rotator output or use the first free Plate. A selected Plate is reused; only the template's destination port is replaced. Key blocks offer `press` and `held` in the flow inspector. Existing key rules keep their old press/event serialization. Configure I/O Flow / Action keys in the vanilla Settings keybind menu.

**Rotator angle** (`rotator.state`) selects a Plate by `ioPartId` and an inclusive `from`/`to` range. A range such as 350 to 10 crosses zero. Feedback samples the previous tick on that vehicle; designer tests simulate it without moving real parts.

The host uses the Leaf's absolute rest-pose rotation and native child notification route. The designer and each spawn start at 0; returning from physics restores the rest pose. Only `ioFlowV1` and `ioPartId` are stored on both Base and Plate; current angle/velocity are transient. Mod-requested COM refreshes run at most 10 Hz while rotating, plus a final refresh after a stop. Native physics/COM scheduling is untouched.

With `Diagnostics.DetailedLogging = true`, `[ROTATOR PERF]` logs each vehicle's average/maximum rotator tick milliseconds, rotator count and number of descendant parts every 10 active game seconds. Includes transform notification and mod COM work; excludes flow evaluation and the occasional descendant-count/log formatting. The native 1/4-rotator measurements and actual blueprint reload are pending the seven-step v0.9 test in [TESTPLAN.md](TESTPLAN.md).

Reproduce the full gate with `tools/Run-Gate.ps1`; regenerate meshes with `py tools\stl_to_mesh.py` and verify with `--check`. Sources, exact STL exports, approved PNGs and the bounds viewer are in `rotator-assets/`.

## 0.8.1 - templates en draadselectie

TEMPLATES in de bibliotheek plaatst een complete keten met draden in één bewerking:
- **Shell flap: open while reloading**: Cannon reloading (`notReady`, standaardbron `any`, fire group 1) -> hinge (Follow). Selecteer een scharnier, of gebruik het eerste vrije scharnier.
- **Shell flap: pulse after firing**: Cannon fired -> Delay 0.5 s -> Pulse 2 s -> hinge (Follow).
- **Brake light**: Vehicle braking -> de eerste vrije API-output, anders het geselecteerde scharnier. Een mast is geen remlampdoel.

Een vrij part is nog niet geplaatst of heeft nog geen inkomende draad. Een geselecteerd scharnier wordt hergebruikt, met behoud van hoeken/snelheid/positie; de nieuwe keten vervangt zijn inkomende draad. Zonder geschikt part blijft de knop uitgeschakeld. De hele keten verhoogt `rev` één keer en wordt compleet gepubliceerd.

Voor een klep tijdens herladen is een directe level-verbinding naar **Follow** voldoende. `loading` volgt alleen de actieve Loading-fase. `notReady` blijft ook aan tijdens een Unloaded-gat, totdat het kanon Loaded is (NeverLoaded bij spawn telt niet). De bestaande standaardmodus van losse Cannon reloading-blokken blijft `loading`. Een Pulse maakt een vaste tijdpuls en volgt niet het einde van het herladen; Open/Toggle houden hun doelstand vast na de flank. `any` betekent OF over alle kanonnen in de gekozen groep: één ander herladend kanon houdt de klep open. Een specifieke kanonkeuze gaat voor; ontbreekt die, dan valt de matcher terug op de fire group.

Kanonvoetteksten tonen de modus, het voertuiggebonden kanon en de groep, plus een eventuele ontbrekende-bronfallback. De bronkeuzelijst valt niet langer terug op kanonnen van andere voertuigen. De pending wire start/stopt alleen bij een wijziging van de gekozen poort; ongewijzigde 10 Hz-refreshes wijzigen alleen live vlaggen/tekst. De zoomlabel-getter is veilig voordat de graph bestaat; getters zijn ook gecontroleerd zonder voertuig/selectie en na teardown.

`Diagnostics.DetailedLogging` staat standaard uit (`false`); staat hij aan, dan geldt: `[FLOW] save/load` (ook API en editor-publicatie) vermeldt `rev`, `blocks`, `wires`, `part`; outputovergangen geven `[FLOW] out <titel> on/off (by <blok>)`. Er is geen nieuwe config-sleutel, API-signature of Harmony-callback.

Een native/visuele test in de game is nog nodig (zie TESTPLAN.md).

## 0.8.0 - flow editor (fase B)

Eén Unity-vrije flow per voertuig (`ioFlowV1`, revisie `rev`), met alle geregistreerde inputs, AND/OR/NOT, delay, pulse, counter en memory. Hinge- en mastoutputs ondersteunen Follow, Toggle, Open en Close. API-signatures blijven v1; het enige contract staat in [IO-API.md](IO-API.md), §3 beschrijft de opake opslagstring.

F8 (I/O Flow / Open panel) opent het I/O Flow-paneel: INPUTS/LOGIC/OUTPUTS links, het vrije Kit-canvas in het midden en de inspector rechts. Klik een bibliotheekitem om te plaatsen; `placed` selecteert het bestaande outputblok. Sleep een titel om te verplaatsen, het lege canvas om te pannen en gebruik het wiel of de knoppen om te zoomen. Fit kadert de flow bij openen. Pan en zoom worden niet opgeslagen.

Klik twee poorten van verschillende richting om te verbinden, ook ingang eerst. Een nieuwe draad vervangt de vorige op die ingang. Dezelfde poort of het lege canvas annuleert; een blok kan niet met zichzelf verbonden worden. Naam, bron, parameters, modus en hinge-instellingen zijn in de inspector te bewerken; Delete verwijdert de selectie. Elke bewerking verhoogt `rev` en publiceert de flow naar alle kopieën; sla het voertuig zelf op om het blueprint-bestand bij te werken.

De designer heeft een geïsoleerde simulatie: Fire geeft één tick, Hold/Release schakelt een level. Geen echte input-/output-callback of partbeweging wordt door deze tests aangeroepen. In de game toont het paneel live signalen met 10 Hz. Cycli tonen LOOP en gebruiken vorige-tickwaarden; meer dan tien outputwisselingen binnen één seconde pauzeert die output twee seconden. Ontbrekende parts blijven als gedimde blokken staan.

Opslag: `ioPartId` en een volledige flowkopie op Leaf/Mount en de vier mastmodel-componenten; API-parts via `saveRules`/`loadRules`, met `{partId,flow}`. Hoogste `rev` wint; dubbele part-id krijgt één waarschuwing en een nieuwe GUID. `hingeIoV1` blijft onaangeroerd. Onbekende nodes/velden blijven bewaard.

Vereist **Sprocket UI Kit >=0.1.12 <0.2.0** en de bestaande Keybinds API. `Diagnostics.DetailedLogging` staat standaard uit (`false`). Geen nieuwe configuratiesleutels of hooks: 24 callbacks op 14 targets. Native gedrag en de paneelopmaak zijn pas in de game te bevestigen.

De omzetting van regels (`hingeIoV1`) naar de flow staat in `tests/MigrationFixtures.cs`; de zes gemengde-Toggle-gevallen wijken daar bewust af.

De oudere regelpaneel-beschrijving hieronder is alleen referentie voor 0.7.1 en eerder.

## Installation

Requires Sprocket 0.2.56.2, BepInEx 6 IL2CPP and the separately installed shared
**Sprocket Keybinds API >=0.1.3 and <0.2.0** and, since 0.6.0, the separately
installed **Sprocket UI Kit >=0.1.12 and <0.2.0** (`BepInEx\plugins\SprocketUiKit`;
BepInEx will not load the mast without it).
This mod bundles neither.
Close Sprocket, back up the existing mast plugin/assets/config, then copy the
package's BepInEx and Sprocket_Data folders into the game directory, merging
folders and replacing matching mast files. Preserve other mods and settings.
The repack's `install-all.ps1` does exactly this, with a backup and hash verification.

## Controls and assembly

Open the vanilla **Settings keybind menu** and find **Mast / Raise** and
**Mast / Lower** under the Telescopic Mast labels. Hold an action to move,
release to stop. Holding both cancels movement. Factory defaults are Insert
and Delete, respectively; the menu bindings control actual input.

Existing Keys.Raise/Lower CFG values are imported once. Saved API choices take
precedence, including Reset and unbound actions. Reset restores the factory
defaults. The old CFG key entries remain for migration and are no longer polled
for gameplay. StrokeMetres, SpeedMetresPerSecond and Enabled remain mast CFG
settings. If using diagnostics, PassiveAtStartup=true intentionally disables
mast and hinge gameplay/model hooks; set false with the game closed and restart.

Place Telescope Base, then attach Telescope Part to its upper rim. Attach the
sight or equipment to the insert's cap. Scale the BASE on X/Y/Z; the insert
matches its scale/alignment in the editor. At height scale 1 the maximum travel
is 1 metre, retaining 0.23 metre overlap. Travel and speed follow height scale.
Saving does not subtract an old offset from the separate insert. Reloading or
returning to the editor seats it on the base; extension is transient.

## Preserved behavior and validation

The user-confirmed 0.3.7 fix synchronizes mass resources and the cached mass used
for centre of mass. It resolved the reproduced mast/turret tipping failure on
Sandbox Flat without blueprint-specific changes. 0.3.8 changed input integration
and control descriptions; 0.4.0 leaves the mast parts, keybinds, mass/cost
calibration, native attachments, geometry, collision behavior and save handling
as they were. Only mast trace lines moved behind `Diagnostics.DetailedLogging`.

Render and interaction meshes are cylindrical; the native collision mesh is
retained, so physical shape does not match the visible cylinder. Own collision
flags/damage behavior and attached native parts retain the accepted 0.3.7 paths.
96 radial segments and separate normals provide smooth cylindrical surfaces.
Selector icons load from the plugin's assets folder with an optional fallback.

## Hinge (0.4.0)

**Hinge Base** is a fixed hinge block with a pin along its local X axis. Attach a
**Hinge Leaf** to it: in the designer the leaf is seated with its origin on the
pin, aligned with and scaled like the base (closed pose). Everything placed on
the leaf swings with it. Scale the base to size the hinge; at scale 1 the leaf
is 0.50 x 0.40 m, 20 mm thick, with a 40 mm pin. A positive angle lifts the leaf
plate away from the surface. Both parts are available from the earliest game
date. Mass is steel volume x 7850 kg/m3 (base 11.7 kg, leaf 32.2 kg at scale 1);
the 0.3.7 mass/centre-of-mass cache fix is applied to both. Like the mast, the
render/placement mesh is custom and the native collision mesh is retained.

Rotation: when physics starts (test drive or battle) the rest pose of the leaf
relative to its base is recorded. The leaf rotation is then written absolutely
as `base * AngleAxis(angle, X) * rest`, never as a delta. On returning to the
designer, on SaveState and before ToBlueprint the rest pose is restored first.

## Hinge Mount (0.5.0)

A small carrier for your own moving parts, e.g. a round shell-ejector flap.
Attach **Hinge Mount** to a Hinge Base like a leaf; it is seated on the pin and
aligned with the base but keeps its own scale. At scale 1 it is a 0.10 x 0.10 m
plate, 10 mm thick, on a 20 mm pin (about 0.95 kg steel, cost like the leaf).
Build the visible part on top of it. The mount itself is visible in the designer
(to place and select it) and hidden during play and test drive; what you built
on it stays visible and swings with it. It rotates, stores links and appears in
the I/O panel exactly like a leaf (Outputs show "Mount"). It is not damageable,
has no damage colliders and no physics/detection collider bits, so it is no
invisible armour or spotting cover; its selection/placement collider remains.

## I/O panel (0.6.0, Sprocket UI Kit)

Open with **I/O Flow / Open panel** (factory default F8; change it in the vanilla
Settings keybind menu). In the designer it lists the hinges of the design; in
game only your own vehicle. The UI Kit frees the cursor in game, blocks firing
and camera while the panel is open, prevents clicks reaching the design and
closes the panel with Esc (or F8).

- **Hinges & outputs** (left): a card per hinge (name, LEAF/MOUNT, live angle and
  bar, number of rules) and, since 0.7.0, per external output part registered by
  another mod (registration name plus a sequence number, ON/OFF). Click to select.
- **Selected output** (middle): for a hinge, name, open/closed/speed summary,
  **Test open**/**Test close** and **Settings** (name, closed angle, open angle,
  speed). For an external part: **Test on**/**Test off** and no angle/speed
  settings. Then the **rules** as sentences, all editable inline:
  `IF [source] [event] THEN [command] for [duration, pulse only] after [delay]`,
  with a lamp and a delete button. For an external part, Open/Close read as that
  registration's `onLabel`/`offLabel`. **Add rule** adds a filled-in rule (first
  cannon, fires -> open). **Templates**: *Case flap: shot -> open, reloaded ->
  close*, *Hatch on key (toggle)*, *Open while mast is up*.
- **Live signals** (right): watch only, with lamps. Clicking one adds a rule with
  that signal to the selected output.

Sources are cannons (a specific cannon or *any cannon in group N*), keys
(Action 1-4), other hinges, the mast and the vehicle brake. Events depend on the
source; *(while)* events keep the output on as long as they last (the command
becomes follow).
Commands: open, close, toggle, pulse (open for a duration; re-triggering extends
it), follow. Warnings appear for a LOOP, for a chosen cannon that is not on this
vehicle (fallback to its fire group) and when no cannons are found yet.
Built-in inputs:

| Input | Kind | Meaning |
|---|---|---|
| `cannon.fired` | event | a selected cannon fired (launch confirmed) |
| `cannon.reloading` | level | a selected cannon is loading (mode *loading*) or not ready (*not ready*) |
| `cannon.reloaded` | event | a selected cannon went Loading -> Loaded: end of its reload cycle |
| `cannon.loaded` | level | a selected cannon is loaded and ready to fire |
| `hinge.state` | level + edge | another hinge has reached open (or closed); for chains |
| `mast.state` | level | masts fully extended, fully retracted, or raised at all |
| `vehicle.braking` | level | the vehicle is braking on both sides (read-only powertrain state) |
| `key` | event + level | **I/O Flow / Action 1-4** (factory Numpad 1-4), own vehicle only |

Every cannon source is *any cannon in group N* (uses each cannon's own
fire-group setting; a cannon in groups 1+3 counts for both) or one specific
cannon, listed as "Cannon 2 – 75 mm (group 1)" and stored by its VUID. If that
cannon is not on the vehicle (e.g. a copied vehicle got new VUIDs), the input
falls back to *any in fire group N* and the panel shows "not found: any in group
N" (a warning row in the panel); pick the cannon again to fix it. `hinge.state` stores the other hinge by VUID
and name and falls back to a unique name. Level inputs also give an event on
their rising edge, so Open/Close/Toggle/Pulse react to "reaches open".

`vehicle.braking` reads only `PowertrainBehaviour.action`; `Braking` is enum value
7. When the layout is `ClutchBraking`, both brake fractions must be active and
symmetric, so steering with the brake on one track does not open a flap. If a
layout has no usable action/brake fields, the input stays false and logs one line.

Shell ejector example: `cannon.fired` (specific cannon) -> Open and
`cannon.reloaded` (same cannon) -> Close on a Hinge Mount carrying a round plate.
Brake flap example: `vehicle.braking` -> Follow, so the flap is open while the
vehicle brakes.

Safety: an output takes at most one command per tick (Follow levels excepted),
and a hinge driven by `hinge.state` links that flips 8 times within 10 s is
marked LOOP; those chain links pause (log warning, panel notice) until a link or
setting changes. Keys only drive your own vehicle; AI vehicles still run the
other links stored in their blueprint.

Hinge rules are stored in the target hinge (object-state key `hingeIoV1`, a JSON
string with a version; unchanged from 0.4/0.5/0.6). External output rules use the
same JSON shape and live in that part's own data through the API `loadRules` /
`saveRules` callbacks; the mast never writes another mod's part data itself. They
refer to input types and parameters; only the optional specific cannon/hinge
choice names another part (by VUID, with the fallbacks above), so a copied hinge
keeps working. Links of unknown input types (e.g. from another mod that is not
installed) are kept unchanged and shown as "kept, not active". A rule is a link:
the panel only shows it as a sentence. Changes made in game last for that
session; save the design in the designer (external data is saved immediately
through `saveRules`).

## Public I/O API (since 0.7.0, for other mods)

The stable contract is `IO-API.md`; this README only points to it. The facade is
`SprocketTelescopicMast.Io.IoApi` (`ApiVersion = 1`) in the mast DLL. A consumer
does **not** reference the mast: use `[BepInDependency("sprocket.telescopicmast", BepInDependency.DependencyFlags.SoftDependency)]`
(the mast's plugin GUID, also `IoApiClient.MastGuid`),
copy `api/IoApiClient.cs`, call `IoApiClient.TryBind()`, then register a level or
event input, or an output on one of your own parts by `ComponentID`. Missing mast
means the shim is a no-op. Output `loadRules`/`saveRules` carry the mast's
`hingeIoV1` JSON for that part; `apply(part, on)` is called only on a change, and
once after spawn/load. The panel shows registered external output parts next to
the hinges. See `IO-API.md` §2-§4 and the usage example at the top of
`api/IoApiClient.cs`.

The built-in `vehicle.braking` input is intended for the same API-style use from
the panel: source "Vehicle", event "brakes" / "is braking (while)".

## Adding an input (for other mods or later versions)

One class plus one registration; UI, storage and engine need no change:

```csharp
using SprocketTelescopicMast;
using SprocketTelescopicMast.Io;

// Choice parameters (dynamic lists) use ParamDef.Choice(id, name, () => options, default).
public sealed class SpeedAboveInput : IInputType
{
    public string Id => "vehicle.speedAbove";            // stable id, stored in blueprints
    public string DisplayName => "Speed above";
    public SignalKind SignalKind => SignalKind.Level;     // Level: on/off; Event: pulses
    public ParamSchema Schema { get; } = new(new[] { ParamDef.Float("kmh", "km/h", 0, 120, 20, 5) });
    public string Describe(IReadOnlyDictionary<string, string> v) => $"Speed above {v["kmh"]} km/h";
    public InputSample Read(InputInstance input, object? context)
    {
        if (context is not VehicleIoContext c || c.Vehicle?.Rigidbody == null) return InputSample.Off;
        float kmh = c.Vehicle.Rigidbody.linearVelocity.magnitude * 3.6f;
        return new InputSample(kmh > input.GetFloat("kmh"), 0); // rising edges become events automatically
    }
}

// In your plugin's Load (after Sprocket Telescopic Mast has loaded):
IoRegistry.RegisterInput(new SpeedAboveInput());

// Optional (0.6.0): how it reads in the panel's rule sentence. Without this the type
// still appears: source "Speed above", events "turns on" / "is on (while)".
var none = new Dictionary<string, string>();
IoRegistry.DescribeInput("vehicle.speedAbove", new RuleDescription("Vehicle", null,
    () => new[] { new RuleSourceOption("This vehicle", none) },
    new[] { new RuleEventDef("goes faster than the limit", none, While: false, Signal: true),
            new RuleEventDef("is faster than the limit (while)", none, While: true) }));
```

A `RuleDescription` names the source group (types with the same group share one
source list and their events are merged, e.g. a new cannon input joins
*Cannons*), the parameters that pick the source, the concrete sources and the
events with their fixed parameter values.

`Read` is called once per tick per distinct input instance with the vehicle's
`VehicleIoContext`. It implements the pure `IVehicleSignals` snapshot (player
flag, shots with VUID and fire-group mask, cannon load states and reload edges,
other hinges, masts, vehicle braking, I/O Flow / Action keys) plus `Vehicle` for
anything else. The eight built-in inputs in `Io/BuiltinInputs.cs` were added this
way without changes to the panel or storage. An input that reads other outputs
implements the marker `IFeedbackInput` so the engine's loop guard covers it.
Outputs register the same way with `IoRegistry.RegisterOutput(IOutputType)`;
the mod ships the `hinge` output.

## Hinge validation status

0.7.1: build, pure tests (in-place state merge, `nextID` counter fire gate with
duplicate filter) and the hook-set/replacement-path checks pass. The remaining set
is 24 callbacks on the 14 reviewed targets; `GameUpdate`, `UpdateControl` and
`ProjectileRegister.Launch` are gone. Still needs the in-game test (TESTPLAN.md,
v0.7.1 block), especially real and cancelled shots, AI vehicles, pause behaviour
and Ctrl+S save/reload with another mod's state present.

0.7.0: build, pure tests (API ids/callbacks, external apply gate,
`saveRules`/`loadRules`, braking policy, 0.6 config, shim version check), the
built-DLL `IoApi` signature check and interop/UI Kit checks pass. Still needs the
in-game test (TESTPLAN.md, v0.7 block), especially a real Visual Effects bulb and
real powertrain layouts.

0.6.0: build, pure tests (rule sentences from the registry, sentence edits,
default rule, templates, signals, description fallback, 0.5 configs saved
byte-identical) and interop/UI Kit checks pass. The new panel still needs the
in-game test (TESTPLAN.md, v2 block).

0.4.1 was confirmed in game (hinge, panel, fire -> delay -> pulse, saving).
0.5.0: build, pure tests (new inputs, cannon choice and fallback, reload edge,
loop guard, 0.4.x links, mount mass) and interop checks pass. Not yet proven in
game: mount hiding with SetVisible, reload-edge timing on real cannons, VUID
stability designer -> test drive, chains, AI vehicles. With `Diagnostics.DetailedLogging=true`
the log records `[Hinge ...]`, `[IO ...]` and `[Mast TILT]` lines for these
checks; see TESTPLAN.md.

## Uninstall

Remove mast and hinge parts from vehicles you want to keep using first. Remove
only the SprocketTelescopicMast plugin folder and its part JSON/XML files (three
mast pairs, hingeBase, hingeLeaf, hingeMount). Preserve the shared Keybinds API and UI Kit if other mods use them,
BepInEx, other mods and customized settings.
