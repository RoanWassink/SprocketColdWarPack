# Configuration

## 0.9.0 - Rotator

Rotators are controlled only through the vehicle flow. There are no new key actions or configuration entries. The Rotator output has `run`, `reverse`, `fast`, `home` ports; home wins and holds the target. All off brakes to a stop. Parameters: `slow` 1–360 degrees/s, default 30; `fast` 1–720, default 90; `direction` CW/CCW, default CW; `target` 0–359, default 0. Acceleration/braking takes 0.3 s. `rotator.state` selects a Plate and an inclusive angle range; from greater than to wraps through zero.

The native part scaler accepts 0.1–5 per axis. X sets both horizontal diameters, Z is ignored, Y sets height. This is mesh compensation; native scale values remain as set by the player. Plate and Base retain independent scales. Current rotation is not saved; every spawn/designer starts at zero. Both parts carry `ioFlowV1`/`ioPartId` copies.

The Radar and Turntable templates use a Key block with `signal = held`; ordinary Key blocks default to `press`. This setting belongs only to the flow node. Configure actions in the vanilla Settings keybind menu. `Diagnostics.DetailedLogging` is off by default; when set to true it enables `[ROTATOR PERF]` every 10 active game seconds per vehicle (avg/max ms per tick, rotators, attached descendants). Native performance results remain pending the game test.

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

Vereist **Sprocket UI Kit >=0.1.9 <0.2.0** en de bestaande Keybinds API. `Diagnostics.DetailedLogging` staat standaard uit (`false`). Geen nieuwe configuratiesleutels of hooks: 24 callbacks op 14 targets. Native gedrag en de paneelopmaak zijn pas in de game te bevestigen.

De omzetting van regels (`hingeIoV1`) naar de flow staat in `tests/MigrationFixtures.cs`; de zes gemengde-Toggle-gevallen wijken daar bewust af.

De oudere regelpaneel-beschrijving hieronder is alleen referentie voor 0.7.1 en eerder.

## Keybinds (vanilla Settings keybind menu, Sprocket Keybinds API)

| Action | Factory default |
|---|---|
| Mast / Raise, Mast / Lower | Insert, Delete |
| I/O Flow / Open panel | F8 |
| I/O Flow / Action 1-4 | Numpad 1-4 |

F8 is also the factory key of other installed mod actions (`reload-profiles`,
`prepare`). Rebind I/O Flow / Open panel if that overlap is a problem.

Since 0.6.0 the I/O panel is a Sprocket UI Kit panel: its position is remembered by
the kit in `BepInEx/config/sprocket.uikit.panels.cfg` (key
`sprocket.telescopicmast/hinge-io`), and size/scale follow the kit's
`Display.Scale`. The mast config has no panel settings.

## Per hinge (stored in the blueprint, edited in the I/O panel)

Object-state key `hingeIoV1` on the Hinge Leaf or Hinge Mount, a JSON string:

```json
{"Version":1,"Name":"Hinge 1","Output":{"Type":"hinge","Params":{"closedAngle":0,"openAngle":90,"speed":90}},
 "Links":[{"Input":{"Type":"cannon.fired","Params":{"cannon":"any","fireGroup":1}},"Command":"Pulse","Delay":0,"Duration":2},
          {"Input":{"Type":"cannon.reloaded","Params":{"cannon":"vuid:326","fireGroup":1}},"Command":"Close","Delay":0,"Duration":2}]}
```

Input parameters (0.5.0):

| Input | Parameters |
|---|---|
| cannon.fired / cannon.reloaded / cannon.loaded | `fireGroup` 1-9, `cannon` = `any` or `vuid:N` (missing VUID: any in fireGroup) |
| cannon.reloading | as above plus `mode` = `loading` / `notReady` |
| hinge.state | `hinge` = `vuid:N:Name` (VUID, else unique name), `state` = `open` / `closed` |
| mast.state | `state` = `extended` / `retracted` / `raised` |
| vehicle.braking | none (level: vehicle brakes on both sides) |
| key | `action` 1-4 |

0.4.x links without `cannon` load as `cannon=any`.

| Setting | Default | Range |
|---|---|---|
| closedAngle | 0 deg | -180..180 |
| openAngle | 90 deg | -180..180 |
| speed | 90 deg/s | 5..720 |
| link Delay | 0 s | 0..60 |
| link Duration (Pulse) | 2 s | 0..60 |

## Public I/O API (since 0.7.0)

Other mods do not edit this config. They register with the in-process facade
`SprocketTelescopicMast.Io.IoApi` through the copyable
`api/IoApiClient.cs`; the contract and signatures are in `IO-API.md` §2-§4.
External outputs store the opaque `{partId,flow}` envelope on the
consumer's part through `saveRules`/`loadRules`. The mast config has no API
settings. `vehicle.braking` is a read-only built-in input, not a config key.
