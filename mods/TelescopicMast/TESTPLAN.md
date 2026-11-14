# Testplan Hinge + I/O Flow 0.4.0 - 0.9.9 (in de game)

Vooraf: Sprocket UI Kit **>=0.1.12 <0.2.0** staat in `BepInEx\plugins\SprocketUiKit`; BepInEx laadt de mast niet zonder die Kit.

## v0.9.9 (3 stappen; nog niet in de game getest)

1. Met een Emitter die RegisterPartSettings gebruikt: selecteer zijn output in I/O Flow; controleer Settings, getal/eenheid/grenzen, toggle, lijst en showIf. Verander waarden; controleer onmiddellijke opslag, de undo-hint en in Play alleen-lezen. De fake afnemer `testEmitter` dekt dit geautomatiseerd via `tools/Run-Gate.ps1`.
2. Sla het voertuig op, herlaad en maak een spiegelkopie: de instellingen blijven bewaard; een paar buiten het midden heeft twee sets. Dezelfde ronde met echte Save/Load-code en blueprint-state-doubles staat in de twin repro van `tools/Run-Gate.ps1`, met payloads `logs/settings-payload-0.9.9-*.json` in de scratchkopie.
3. Plaats een samengevoegde tweeling op dezelfde base: wijzig de primaire en controleer beide waarden/callbacks. Een tweede eigen bedrade node blijft apart. De fake afnemer controleert beide gevallen plus terugkerende designer-objecten in Play; controleer dit ook met de Emitter in de game.

## v0.9.8 (3 stappen; nog niet in de game getest)

1. Plaats met spiegelen aan een mast-insert, rotator-plate of hinge leaf/mount op een base in het midden: I/O Flow en de sjablonen bieden een output. Een bestaande bedrade tweede node blijft geldig met de hint "twin of ..., can be removed".
2. Geef een commando en start Play met dezelfde designer-objecten: beide delen bewegen gelijk in de wereld, ook met een gespiegelde lokale orientatie. Een bestaande bedrade tweede node blijft zijn eigen deel besturen.
3. Plaats een gespiegeld paar op twee bases buiten het midden: er blijven twee onafhankelijke outputs, ook na opslaan en herladen. Voor een enkel kind op een middenbase kan spiegelen bij plaatsen uit.

## v0.9.7 (3 stappen; nog niet in de game getest)

1. Plaats een Telescope Base met twee Separate inserts; kies **Mast: raise after firing** voor beide en **Brake light** voor een lamp. Start vanuit de designer, vuur (groep 4 / Any) en controleer dat beide mastdelen zichtbaar omhooggaan en de lamp bij remmen aangaat. Keer terug en herhaal Play met dezelfde onderdelen; de sandbox-doelkopie blijft beneden. Een rechtstreekse eventdraad toont een waarschuwing en blijft slechts een tick aan.
2. Bekijk sleeve/piston, telescope base/part en hinge base/leaf: glad metaal, vierkante textuurdetails op kap en wand, camo even fijn als op de romp, ook na opslaan/herladen.
3. Bekijk Rotator Base/Plate: dezelfde fijne camo/slijtage als op de romp, gladde zijband en kap, ongewijzigde beweging.

## v0.9.6 (3 stappen; nog niet in de game getest)

1. Open I/O Flow in de designer: de lamp en zijn gespiegelde kopie staan met leesbare, afzonderlijke namen in de partlijst. Bewaar de `[IO EXT] designer list`-regel bij DetailedLogging.
2. Kies Brake light: Vehicle braking wordt met de lamp verbonden; sla het voertuig op en controleer na herladen dat de draad blijft bestaan (`ioPartId` en `ioFlowV1` op de lamp-parts).
3. Start de game: de lamp gaat aan bij remmen en uit bij loslaten.

## v0.9.5 (3 stappen; nog niet in de game getest)

1. Open I/O Flow in de designer van "T80 bvm proporties": een geplaatste lamp verschijnt met een leesbare naam; een gespiegelde lamp staat als eigen output in de lijst. DetailedLogging blijft aan; bewaar de gewijzigde `[IO EXT] scan`-regels.
2. Kies Brake light: het sjabloon is actief en verbindt Vehicle braking met de lamp-output.
3. Start de game: de lamp gaat aan bij remmen en uit bij loslaten.

## v0.9.4 (3 stappen; nog niet in de game getest)

1. **Draden slepen tussen veel blokken.** Plaats 20+ blokken en sleep van een uitgang naar een ingang, ook net naast de poort (binnen 12 px) en op een bezette ingang: de draad volgt, snapt, vervangt en een weigering noemt de reden plus `[FLOW] wire refused`. Laat een sleep op het lege canvas los: niets verandert en er komt geen melding. Klikken blijft werken.
2. **Ctrl+Z en Ctrl+Y in het paneel.** Open I/O Flow (F8), wijzig de flow en druk Ctrl+Z, Ctrl+Y en Ctrl+Shift+Z: alleen de flow gaat terug/vooruit en de tank in de designer verandert niet. Klik daarna buiten het paneel en probeer opnieuw: de game/designer-undo doet dan weer mee. De knoppen Undo/Redo blijven gelijk.
3. **Een nieuw blok komt in beeld.** Maak het paneel klein of verschuif/zoom de view en plaats een blok: het landt in het zichtbare deel als daar ruimte is, anders rechts van de inhoud en de view volgt het. Bestaande blokken overlappen niet (24 px marge). Hernoem een tab en voeg een tab toe: de tabbalk flikkert niet en de namen kloppen.

## v0.9.3 (3 stappen; nog niet in de game getest)

1. **Bestaande flows en eigen iconen.** Laad een 0.9.2-ontwerp met scharnier-, mast- en rotatorflows: blokken, draden, tabs en namen staan er nog en werken. Open de partlijst: de zeven eigen iconen (telescope, hinge, rotator) zijn ongewijzigd.
2. **Scharnieren bewaren.** Open/sluit een klep via de flow, sla op, herstart en laad schoon: hoeken, namen, flow en `ioPartId` keren terug; de klep beweegt weer.
3. **Rotator bewaren.** Laat de rotator draaien en naar home gaan, sla op en herlaad: vier poorten, parameters, flow en beide `ioPartId`'s staan er nog; de Plate start op 0. De lamp zelf test Visual Effects later met de v1.1-API.

## v0.9.2 (5 stappen; nog niet in de game getest)

1. **Draden met veel blokken.** Plaats 20+ blokken en leg 10 draden: klik precies op de poorten en ook net naast een poort op het blok. Elke draad verbindt; een draad op een bezette ingang vervangt de oude. Probeer een blok met zichzelf en twee outputs: de statusregel noemt de reden, `[FLOW] wire refused` staat in de log.
2. **Tabs en sjablonen.** Plaats twee sjablonen: elk opent een eigen tab met de sjabloonnaam. Maak een tab met +, hernoem hem, verwijder een lege tab direct en een tab met blokken na bevestiging. Nieuwe blokken liggen nooit over een ander blok. Alle tabs werken samen tijdens het rijden.
3. **Undo en redo.** Doe na elkaar: blok toevoegen, draad leggen, parameter wijzigen, blok slepen, tab verwijderen. Undo zet elke stap terug (slepen in één stap), Redo zet ze terug; de knoppen zijn grijs als er niets is. (0.9.4 voegt Ctrl+Z/Ctrl+Y toe; zie het v0.9.4-blok.)
4. **Naam.** Paneeltitel "I/O Flow"; in Settings heten de toetsen "I/O Flow / Open panel" en "I/O Flow / Action 1-4", met de oude toewijzing (F8, Numpad 1-4). De partbeschrijvingen noemen het I/O Flow-paneel.
5. **Herladen.** Sla het voertuig op met drie tabs, herstart en laad schoon: tabs, namen, blokken per tab en draden zijn er nog, de flow werkt (`[FLOW] load` met dezelfde rev).


## v0.9.1

1. Place the rotator: check the smooth drum, hub, rim and Plate bolts and the absence of the crystal pattern, with and without camo. The approved octagonal Base bolt edges retain their 45-degree creases.
2. Check all seven icons and their captions in the part list.
3. Repeat the v0.9.0 steps below.
4. Connect Always on to rotator.run: it rotates continuously, including after saving and reloading.
5. Place a new Cannon fired block (Any group): fire a group-4 cannon and verify its signal. A direct wire to run is momentary; check the editor warning and use Memory or Pulse for a lasting run signal.

## v0.9-blok (7 stappen; nog niet in de game getest)

1. **Plaatsen en schalen.** Plaats Rotator Base en Plate in de designer. Test X/Y/Z apart van 0,1 tot 5 en X ongelijk aan Z: X bepaalt beide diameters, Z wordt genegeerd, Y bepaalt hoogte; beide blijven rond. De Plate rust op de Base en toont altijd hoek 0. Een losse Plate geeft eenmaal een waarschuwing en draait niet.
2. **Camo.** Verander de native Exterior-camo; Base en Plate kleuren mee, ook na rebuild. Controleer de goedgekeurde kerf/nok, bouten en massa/kosten; bij gelijke X/Y en andere Z blijven massa en uiterlijk gelijk.
3. **Radar-sjabloon.** Plaats “Radar: spin while key held”. Houd de ingestelde I/O Flow / Action-toets vast en laat los: optrekken/stoppen in 0,3 s. Test CW/CCW, reverse en slow/fast via extra held-Key-inputs. Controleer slow 1–360 en fast 1–720, voetregel en stoppen op de huidige hoek; pauze bevriest de beweging.
4. **Home en feedback.** Plaats “Turntable: go home on key” op dezelfde Plate. Home wint bij gelijktijdig run; test doelhoeken 0/10/350 en beide kortste routes over nul. Houdt vast op target, remt rustig bij inschakelen tijdens draaien. Test Rotator angle 350–10: aan aan beide kanten van nul, uit daarbuiten.
5. **Meedraaiende radar.** Bouw ongeveer 10 radarparts op de Plate, inclusief geneste kinderen. Draai de romp/toren en de Base-montagenormaal; alle kinderen draaien mee, zonder losraken of dubbele rotatie. Terug naar designer geeft rustpose 0; test ook twee scharnieren/masten samen met de rotator.
6. **Opslaan/herladen.** Sla op tijdens draaien en in de designer. Herstart en laad schoon: flow, vier poorten, parameters en beide sjablonen blijven behouden; elke Base/Plate heeft ioFlowV1 en een eigen ioPartId, de Plate start op 0. Controleer hoogste rev, dubbele-idkopie, ontbrekende Plate, en dat opgeslagen transforms de rustpose hebben. Lever de echte blueprint en log als bewijs.
7. **Perf.** Met DetailedLogging=true: meet eerst 1, daarna 4 tegelijk draaiende rotators met elk circa 10 parts (40 totaal), minimaal 30 actieve seconden elk. Noteer alle [ROTATOR PERF]-regels: voertuig, avg/max ms/tick, rotators=1/4, parts≈10/40. Bewaar log, resolutie/FPS en voertuig; beoordeel COM/stabiliteit op verschillende wereldposities. Rapporteer echte meetwaarden; beheerde tests zijn geen native perf-bewijs.

## v0.8.1 camo

1. Verander de camo in de designer: Hinge Base, Hinge Leaf en alle zichtbare mastparts verven mee. Sla op en herlaad het ontwerp; verander de camo opnieuw en controleer dezelfde parts, ook na een rebuild. Hinge Mount blijft zoals hij was; controleer dat massa, kosten en botsing gelijk blijven.

## v0.8.1-blok (max 5 stappen)

1. Open F8 in de designer, ook zonder geselecteerd blok/voertuig. Geen `Label getter`-exception. Verbind pulse-uitgang met hinge-ingang, annuleer met dezelfde poort/canvas, vervang de draad, sleep en wacht: geen stapel oude pending wires; meld de Kit-sporen, verticale clipping of losstaande tagtekst met screenshot.
2. Selecteer de klep en kies **Shell flap: open while reloading**. Controleer één directe draad, Follow, bron any/groep 1 en modus Not ready in de footer. Test vijf schoten, steeds tot minstens één seconde na vuurklaar wachten: open tijdens herladen, daarna dicht. Vergelijk indien gewenst loading met notReady tijdens een Unloaded-gat; de log toont iedere outputovergang en oorzaakblok.
3. Selecteer dezelfde klep en kies **Shell flap: pulse after firing**: fired -> Delay 0.5 s -> Pulse 2 s -> Follow. In de designer Fire, daarna in Play schieten: na 0.5 s open, na 2 s dicht. Instellingen/positie van de klep blijven behouden en er is één draad op zijn ingang.
4. Kies **Brake light** met een vrije API-bulb; rijden/remmen/loslaten schakelt de lamp. Zonder vrije API-output wordt het geselecteerde scharnier gebruikt. Controleer dat een tweede template een bezet niet-geselecteerd part overslaat en dat geen part dubbel op het canvas komt.
5. Voertuig opslaan, game sluiten/heropenen en laden: templateblokken, draden, params en hinge-instellingen blijven staan; de hoogste flow-rev keert terug. Bewaar `[FLOW] save/load` (rev/blocks/wires/part), `[FLOW] out ... (by ...)`, IO FIRE/RELOADED en een screenshot.

Voorbereiding: Sprocket dicht, de mast installeren (bestanden kopiëren of `install-all.ps1`), `Diagnostics.DetailedLogging = true` (standaard in deze build). Na de test `BepInEx\LogOutput.log` en het opgeslagen `.blueprint` bewaren.

## v0.8-blok (7 stappen)

1. **F8 en migratie.** Open een voertuig met oude regels. F8 opent het I/O Flow-paneel met de gemigreerde flow in kolommen. Controleer de drie bibliotheeksecties, inspector, Fit, status en API-bulb (indien aanwezig).
2. **Plaatsen en bewegen.** Plaats een input, elk van de zeven logicablokken en een output. Sleep een blok, pan het lege canvas, zoom met wiel en knoppen, maximaliseer en gebruik Fit. `placed` selecteert de bestaande output; geen tweede blok voor hetzelfde part.
3. **Verbinden en inspector.** Verbind in beide klikrichtingen. Vervang de draad op een ingang; annuleer met dezelfde poort en met leeg canvas; zelfverbinden mag niet. Selecteer een draad en verwijder hem. Bewerk naam, bron, parameter, modus en hinge-hoeken/snelheid; verwijder een blok.
4. **Designer-simulatie.** Fire geeft precies één tick; Hold/Release blijft aan/uit. Test delay, pulse, counter en memory met hun voetregels. Echte parts, masten en lampen bewegen/veranderen niet. Maak een cyclus: LOOP verschijnt en de UI blijft reageren.
5. **In de game.** Test de hulzenklep (schot -> puls -> hinge) en remlamp (vehicle.braking -> API-bulb). Beide volgen de signalen; eenzijdig remsturen activeert de lamp niet. Controleer live poorten/draden, voetregels, pauze en lusbeveiliging. Masttoetsen blijven werken.
6. **Opslaan en herladen.** Bewerk en sla het voertuig op, sluit Sprocket en laad opnieuw: namen, posities, parameters en draden blijven gelijk. Controleer hoogste rev en alle ioFlowV1-kopieën in de opgeslagen blueprint/API-state; pan/zoom zijn niet opgeslagen. Test ook een gekopieerd part met dubbele id.
7. **Ontbrekend part.** Verwijder een outputpart in de designer en sloop er een in de game: het blok blijft gedimd, voert niets uit en kan worden verwijderd. Test ook het laatste outputpart. Sluit/open het paneel, bekijk de Kit-opmaak op normale en maximale grootte en lever de log plus blueprint aan voor acceptatie.

## v0.7.1-blok (max 6 stappen)

1. **Opstarten en oude regels.** In de log staat `[Hinge] v0.7.1 hinge + I/O ready: ... API v1 ...`. Open een ontwerp met 0.5/0.6-regels: dezelfde regels en namen staan er nog en werken.
2. **Mast toetsen, eigen voertuig.** Test drive: houd Mast / Raise en Lower in; alleen de mast van je eigen voertuig beweegt, niet die van een AI ernaast. Rijd daarna met de mast omhoog: de huls blijft op zijn plek staan (geen zichtbare sprint of scheefstand).
3. **Scharnier bij schieten.** Regel `cannon.fired -> pulse` op een klep. Eén schot geeft één puls; een geblokkeerd schot (bijv. met Ballistics dat een schot tegenhoudt) geeft geen puls. In de log staat `[IO FIRE] ... nextID=<voor>-><na>`: alleen een verandering telt. Twee snelle schoten geven twee pulsen, niet drie.
4. **AI-voertuig.** Laat een AI-tank met een scharnier en een fire-group-regel vechten: zijn klep opent bij zijn schoten. De toetsen doen niets voor de AI.
5. **Blueprint opslaan en herladen.** Sla een ontwerp met scharnierregels op (Ctrl+S en via het menu), sluit Sprocket, start opnieuw en laad het ontwerp: de regels, namen en hoeken staan er nog. Herhaal met een andere mod die ook object-state op hetzelfde voertuig schrijft; die state mag niet verdwijnen.
6. **Remregel uit 0.7.0.** Een regel `IF Vehicle is braking (while) THEN follow` op een klep: de klep volgt de rem, en sturen met de rem aan één kant opent hem niet.

## v0.7.0-blok (max 6 stappen)

1. **Opstarten en oude regels.** In de log staat `[Hinge] v0.7.1 hinge + I/O ready: ... API v1 ...`. Open een ontwerp met 0.5/0.6-regels: dezelfde regels en namen staan er nog en werken.
2. **ALS voertuig remt DAN scharnier volgen.** Maak een regel `IF Vehicle is braking (while) THEN follow` op een klep. Rijden en remmen: de klep opent zodra het voertuig op beide kanten remt en sluit bij loslaten. (Log: `vehicle.braking` false/true in de input/trace.)
3. **Eén kant remmen telt niet.** Rijdend sturen met de rem aan één kant (of een pivot): de klep opent niet. Meld het als dat wel gebeurt, met het voertuigtype en de logregels.
4. **Opslaan en herladen.** Opslaan, Sprocket afsluiten, opnieuw starten, ontwerp laden, F8: de remregel en de andere regels staan er nog; de klep reageert opnieuw op remmen. (Log: `[IO SAVE]` / `[IO LOAD]`.)
5. **0.6-regels werken nog.** Een oude regel `cannon.fired -> pulse` en een `hinge.state`-keten testen; geen onbekende/verdwenen invoer in het paneel.
6. **Externe output (later, met Visual Effects).** Visual Effects plaatst hun bulb met geregistreerde `componentId`; F8 toont de bulb als externe output met volgnummer. Hun test: een regel op de bulb, `apply` alleen bij open/dicht, en de bulb-JSON via `saveRules`/`loadRules` na opslaan/herladen. Dit test de afnemer, niet alleen de mast; de mast hoeft hierbij niets in hun part-data te schrijven.

1. **Opstarten.** Hoofdmenu bereiken. In de log staan `[Mast] v0.4.0 ...` en `[Hinge] v0.4.0 hinge + I/O ready ... input block 9/9`, geen `[Hinge] Optional hooks ... unavailable`.
2. **Mast ongewijzigd.** Een bestaand ontwerp met mast laden: Mast / Raise en Lower werken als in 0.3.8.
3. **Luik plaatsen.** Designer: Hinge Base op het dak, Hinge Leaf op de base. De klep springt op de pin, ligt plat en volgt de schaal van de base. Zet iets kleins (bijv. een periscoop) op de klep. Controleer massa en kosten in het ontwerp (base ~12 kg, klep ~32 kg).
4. **Paneel in de designer.** F8 (I/O Flow / Open panel). Drie kolommen, "Hinge 1" rechts. Klikken op knoppen werkt, en klikken op het paneel selecteert of plaatst niets in het ontwerp eronder (meld het als dat wel gebeurt).
5. **Regel maken.** Input: `<`/`>` naar "Cannon fired", fire group 1, "Add input". Klik de input, klik "Hinge 1", Command "Pulse", Duration 2 s, "Add link". De regel staat in de middelste kolom.
6. **Opslaan en schoon herladen.** Ontwerp opslaan, Sprocket afsluiten, opnieuw starten, ontwerp laden, F8: de regel en de naam staan er nog. (In de log: `[IO SAVE]` en `[IO LOAD] ... links=1`.)
7. **Schieten.** Test drive of Sandbox: kanon in fire group 1 vuren. De klep gaat open (90°), blijft ~2 s open en sluit. Twee snelle schoten verlengen de puls. Kinderen op de klep draaien mee.
8. **Herladen-follow.** Tweede regel: "Cannon reloading" (group 1, mode loading) -> Follow. Schieten: de klep blijft open zolang het kanon herlaadt.
9. **Toets.** Regel "Key I/O Flow / Action 1" -> Toggle. Numpad 1 opent, nogmaals drukken sluit. Test ook de Open/Close-knoppen in de Outputs-kolom.
10. **Paneel in de game.** F8 tijdens het rijden: muis vrij, linkermuisknop vuurt niet en de camera draait niet mee. F8 sluit en de muis is weer gevangen.
11. **Terug naar de designer.** De klep staat weer dicht en op de pin; niets is verschoven of gedraaid.
12. **Kopie.** Dupliceer base + klep (of het hele voertuig met "opslaan als"). De kopie heeft dezelfde regels en werkt in de test drive.
13. **Sandbox Flat stabiel.** Spawn het voertuig op Sandbox Flat en een andere kaart, klep een paar keer open/dicht: de tank kantelt niet en staat stil.
14. **AI-voertuig.** Laat een AI-tank met dit ontwerp (met de fire-group-1-regel) vechten: de klep opent bij zijn schoten; toetsen doen niets voor de AI.
15. **Hoeken.** In het paneel open-hoek naar 45° en snelheid naar 30°/s zetten: de klep gaat langzamer en minder ver open.

## Aanvulling 0.5.0: shell ejector, onzichtbare mount, nieuwe inputs

Opstarten: in de log `[Hinge] v0.5.0 hinge + I/O ready: inputs cannon.fired, cannon.reloading, cannon.reloaded, cannon.loaded, hinge.state, mast.state, key`.

16. **Mount plaatsen.** Designer, tank met minstens twee kanonnen (bijv. 75 mm in groep 1 en een coax in groep 1). Kleine Hinge Base (schaal ~0,2) op de romp, **Hinge Mount** op de base: hij springt op de pin en houdt zijn eigen schaal. Bouw er een ronde plaat op. Massa van de mount ~0,95 kg bij schaal 1.
17. **Mount zichtbaar in de designer.** De mount is te zien en te selecteren; F8 toont hem rechts als "Hinge N (Mount)".
18. **Kanonkeuze.** Nieuwe input "Cannon fired", parameter Cannon met `-`/`+`: de lijst toont "Any in fire group" en "Cannon 1 – 75 mm (group 1)", "Cannon 2 – … mm (group 1)". Kies het hoofdkanon, Add input, link → **Open** → de mount. Daarna "Cannon reloaded", hetzelfde kanon, link → **Close** → de mount. Opslaan.
19. **Shell ejector in test drive.** De mount zelf is onzichtbaar, de ronde plaat niet. Hoofdkanon vuren: de plaat klapt open en blijft open tot het einde van de herlaadcyclus, dan dicht. Coax vuren: niets. (Log: `[Hinge MOUNT] ... hidden for play`, `[IO FIRE]`, `[IO RELOADED] vuid=...`.)
20. **Onzichtbaar = geen pantser/klikvlak.** Schiet (of laat een AI schieten) op de plek van de mount waar geen plaat zit: er is geen treffer op een onzichtbaar onderdeel. Terug naar de designer: de mount is weer zichtbaar en dicht.
21. **Kopie van het voertuig.** "Opslaan als" en de kopie laden, F8: staat er "not found: any in group 1", dan is de VUID veranderd en reageert de klep op elk kanon in groep 1 (meld welke tekst je ziet).
22. **Keten en lus.** Twee luiken A en B: link "Hinge state: A is open" → Open op B; A met een toets openen: B volgt. Maak daarna een lus (A Toggle bij "B is open" en bij "B is closed", B idem bij A) en start hem met de toets: na ~8 wisselingen staat er LOOP in het paneel en een `[IO] Loop detected`-waarschuwing in de log, en de luiken stoppen.
23. **Overige inputs.** "Cannon loaded" → Follow (klep open zolang geladen) en "Mast state: fully extended" → Follow op een voertuig met mast (Mast / Raise tot boven: klep opent).
24. **Oude opslag.** Een in 0.4.1 opgeslagen ontwerp met fire-group-links laden: links staan er als "Cannon fired (any in group 1)" en werken als voorheen.

Meld bij elke stap die afwijkt wat je zag; lees daarna de log (`[Hinge POSE]`, `[Hinge CHILD]`, `[IO FIRE]`, `[IO CANNON]`, `[IO UI]`, `[IO SAVE]`/`[IO LOAD]`, `[Mast TILT]`).

## v2-blok 0.6.0: I/O-paneel op de UI Kit (max 10 stappen)

Vooraf: UI Kit 0.1.1 staat in `BepInEx\plugins\SprocketUiKit`. In de log `[Hinge] v0.6.0 hinge + I/O ready: ... panel UI Kit 0.1.1`. Stappen 5-6 en de 0.4/0.5-stappen hierboven blijven geldig, maar met het nieuwe paneel (regels in plaats van links).

25. **Openen in de designer.** Ontwerp met een kanon en twee scharnieren, F8: links de scharnierkaarten (LEAF/MOUNT, hoek, aantal regels), midden het eerste scharnier, rechts "Live signals". F8 of Esc sluit; Esc opent daarbij niet het pauzemenu.
26. **Regel in één klik.** "Add rule": er staat meteen `IF Cannon 1 – … fires THEN open now` (eerste kanon). De status onderin meldt "Rule added to …".
27. **Inline aanpassen.** In die regel: bron → "Any cannon in group 2", gebeurtenis → "is reloading (while)" (commando springt naar follow), commando → pulse (gebeurtenis wordt "starts reloading", er verschijnt "for 2 s"), vertraging + naar 0,5 s ("after"). Prullenbak verwijdert de regel.
28. **Sjabloon hulzenklep.** Selecteer de mount, "Case flap: shot -> open, reloaded -> close": twee regels (fires → open, finished reloading → close) met hetzelfde kanon. Nogmaals klikken voegt niets dubbel toe.
29. **Signaal-klik.** Klik rechts "Action 1 is pressed": er komt een regel `IF Action 1 is pressed THEN open` bij het gekozen scharnier.
30. **Designer zonder doorklikken.** Klik, sleep en scroll boven het paneel (ook in een open keuzelijst boven het ontwerp): in het ontwerp eronder wordt niets geselecteerd, geplaatst of gedraaid.
31. **Instellingen.** "Settings" uitklappen: naam typen (cijfers tijdens typen schakelen geen I/O Flow / Action), open-hoek 45°, snelheid 30°/s. Opslaan, schoon herladen, F8: regels, naam en hoeken staan er nog (`[IO SAVE]`/`[IO LOAD]`).
32. **In de game niet vuren door een klik.** Test drive met het eigen voertuig, F8: muis vrij; klikken op knoppen, keuzelijsten en Test open/Test close vuurt het kanon niet en draait de camera niet. Test open/close bewegen het scharnier. Esc/F8 sluit, de muis is weer gevangen en vuren werkt weer.
33. **Live lampjes.** Paneel open in de test drive, kanon vuren met de toets/knop buiten het paneel (paneel sluiten, vuren, openen) of Numpad 1: de lampjes van "Cannon 1 fires"/"Action 1 is pressed" en van de bijbehorende regel lichten op; de hulzenklep doet wat de regels zeggen.
34. **Oude configs.** Een in 0.5.0 opgeslagen ontwerp laden: alle regels staan er als zin (bijv. "Cannon #… (not found: any in group 1)" met een waarschuwingsrij bij een gekopieerd voertuig) en werken als voorheen.

Meld per stap wat afwijkt; lees daarna `[IO UI] Panel v2 opened`, `[IO UI] Rule added/changed/removed`, `[IO UI] Template`, `[IO SAVE]`/`[IO LOAD]` in de log.
