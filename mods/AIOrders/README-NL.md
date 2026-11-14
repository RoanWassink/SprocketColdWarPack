# AI Orders 0.5.2 — niet-modaal F9-snelmenu

Testbuild voor Sprocket **0.2.56.2 Experimental** en BepInEx 6 IL2CPP; HUD en kaart zijn nog niet in-game bevestigd (zie [TESTPLAN.md](TESTPLAN.md)). Wat er per versie veranderde staat in [CHANGELOG.md](CHANGELOG.md); deze tekst beschrijft de bediening die sinds 0.5.0 geldt.

Vereist: **Sprocket UI Kit ≥0.1.2 <0.2.0** (`sprocket.uikit`) en **Sprocket Keybinds ≥0.1.7 <0.2.0** (`sprocket.keybinds`). Beide mods moeten afzonderlijk in de game staan; hun DLL's worden niet meegeleverd. Optioneel: **Sprocket Expedition** (`sprocket.expedition`, zachte afhankelijkheid); in Expedition-gevechten geeft AI Orders zijn orders aan Expedition door.

## Wat is nieuw in 0.5.0

F9 is geen Kit-paneel meer maar een kleine, niet-modale HUD linksonder (340 px breed, 24 px van links en 150 px van onder, in Kit-referentiepx). Die hoogte houdt hem boven de eigen Sprocket-HUD onderaan het scherm; Kit 0.1.2 heeft alleen hoekankers, dus een rechtsmidden-anker bestaat niet. De HUD krijgt geen cursor, blokkeert geen muis, camera, vuren of rijden en blijft zichtbaar wanneer een ander paneel de cursor vrijgeeft. F9 aan/uit gaat via de bestaande keybind; de HUD sluit zodra de M-kaart opent.

De inhoud is alleen tekst, live via getters: een kop met **AI Orders** en `Selected n/m`, de menuregels met hun cijfer, de submenu's met pagina's en één statusregel met het resultaat van de laatste order. Er is geen lijst van alle bondgenoten meer; in submenu 2 staat `[x]` bij gekozen tanks. Alle labels hebben een outline. Een geslaagde Follow/Halt/Attack/Retreat-order of 0 in het hoofdmenu verbergt de HUD. De M-kaart is ongewijzigd ten opzichte van 0.4.1.

## Bediening

**Standaardtoetsen**: F9 snelmenu-HUD, M tactische kaart, 1–9/0 keuzes. Aanpassen kan in de gewone Settings / keybinds / AI Orders; persoonlijke bindings blijven gelden. De HUD-regels tonen hun cijfer.

**Snelmenu (F9)**: 1 alle bondgenoten aan/uit · 2 bondgenoten kiezen · 3 Follow column · 4 Follow line · 5 Halt · 6 Attack… · 7 Retreat… · 8 Release to AI · 9 kaart · 0 sluiten. In submenu's kies je met 1–7, blader je met 8/9 en ga je met 0 terug. `[x]` markeert gekozen tanks in submenu 2. De statusregel toont de laatste order of weigering.

De cijfers worden alleen door AI Orders gelezen zolang de HUD zichtbaar is. Is de HUD dicht, dan blijven 1–9/0 van de game. Omdat de toets niet wordt geconsumeerd, kan de game tijdens de HUD ook op een cijfer reageren (bijvoorbeeld munitiekeuze op 1–9); zie het testplan. Op de kaart regelen de cijfers de selectie zoals voorheen. F9 opent de HUD niet boven de kaart.

**Tactische kaart (M)**:

- Links: bondgenotenlijst met nummer, ontwerpnaam, ordertag en afstand. Klik selecteert; Shift+klik voegt toe of verwijdert uit de selectie.
- Select all / Clear, Follow column / Follow line, Halt, Release to AI, Attack nearest spotted (afstand; uit zonder zichtbare vijand), Center on me en Whole map staan in het Kit-paneel.
- Linksklik op een blauwe tank selecteert; Shift+klik voegt toe of verwijdert. Lege plek zonder Shift wist de selectie; Shift+klik op leeg terrein behoudt de selectie.
- Rechtsklik op terrein geeft een verplaatsingsorder; meerdere tanks spreiden in een linie bij het doel. Shift+rechtsklik voegt een waypoint toe.
- Rechtsklik op een zichtbare rode vijand geeft een aanvalsorder, ook met Shift (hetzelfde gedrag als 0.3.0).
- Wiel boven het Canvas zoomt naar de cursor; middelste muisknop slepen schuift. Knoppen en lijst buiten het Canvas geven geen kaartorder of kaartzoom.
- Cijfers 1–9 selecteren tank 1–9; Shift voegt toe. 0 wisselt alles/niets.
- Jezelf: geel; bondgenoten: blauw met nummer/richting en gele selectiering; zichtbare vijanden: rood met afstand. Laatst geziene vijanden vervagen met een vraagteken gedurende 90 seconden.
- De topokaart toont terreinreliëf, hoogtelijnen en de rode gevechtsgrens.

Terwijl de kaart open is, blokkeert **de UI Kit** schieten/scopen en camerabeweging door muisklikken. Rijden met het toetsenbord blijft werken. Esc sluit eerst het paneel zonder pauzemenu; nogmaals Esc kan daarna normaal pauzeren. Bij pauze of scènewissel sluit de Kit de kaart.

## Wat er anders is aan de orders

- **Follow** gebruikt Sprocket's eigen formatiesysteem (JoinFormation + FollowFormationDriveTask), met jou als leider. Bondgenoten sturen dan continu bij en passen hun snelheid aan op die van jou, in plaats van elke paar seconden stil te staan voor een nieuw waypoint. Je kiest tussen **column** (achter elkaar) en **line** (breed). Als de native formatie binnen ~5 s niet aanslaat, schakelt die tank automatisch over op waypoint-volgen. Dat zie je in de zijbalk als `follow col*`. De waypoints liggen nu ook een stukje vooruit langs je rijrichting, zodat volgers minder achterblijven.
- **Halt / Move / Attack** halen een tank eerst echt uit de formatie. De native MoveToPosition doet dat bij gelijke prioriteit namelijk niet.
- **Move** blijft het doel opnieuw proberen als de AI tussendoor stopt om te vuren. Pas na 3 pogingen zonder voortgang zegt hij "cannot reach the waypoint".
- **Release to AI** geeft een tank terug aan de eigen AI van de game.

## Bouwen, controleren en installeren

Vanuit deze map:

```powershell
dotnet build -c Release
dotnet run --project tests/PolicyTests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File ./Verify.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/VerifyChecks.ps1
```

De mod staat in `build/SprocketAIOrders.dll` (`-p:OutputPath=...` kiest een andere map). Heb je een andere spelmap, of een kopie met alleen `BepInEx\core`, `BepInEx\interop` en de DLL's van UI Kit en Keybinds, bouw dan met `-p:GameDir="pad"` en roep Verify aan met `-GameDir "pad"` (en `-BuildDir` als de DLL elders staat). Verify controleert de dependency in de bron, geïnstalleerde Kit-versie, ontbrekende/ongewenste buildbestanden en verboden oude UI/inputcode. 
```powershell
dotnet run --project tests/PolicyTests.csproj -c Release -- "C:/Program Files (x86)/Steam/steamapps/common/Sprocket/BepInEx/plugins/SprocketUiKit/SprocketUiKit.dll" "build/SprocketAIOrders.dll"
```

VerifyChecks gebruikt alleen fixtures in een tijdelijke map (`-WorkDir`, standaard onder de tijdelijke map van Windows) en laat de spelmap en de bronmap met rust.

**Installeren:** sluit Sprocket en zet `SprocketAIOrders.dll` in `BepInEx\plugins\SprocketAIOrders\` (of gebruik `install-all.ps1` uit de herpak-uitlevering, met back-up). Er is geen configbestand om mee te leveren: de mod maakt `BepInEx\config\sprocket.aiorders.cfg` zelf aan; een bestaand bestand blijft staan.

Volg daarna [TESTPLAN.md](TESTPLAN.md) (10 stappen).

## Diagnostiek en beperkingen

`[Diagnostics] DetailedLogging = false` is de standaard; zet hem op `true` voor uitgebreide logregels bij het testen. Een reeds opgeslagen cfg-waarde blijft leidend. De switch regelt onder andere `AI Orders quick HUD open/closed`, kaart open/dicht, `AI Orders Kit button: ...`, kaartklikken met knop/wereldpositie/raakobject, `AI Orders map ...` en `Order state: ...`. Warnings en errors blijven altijd zichtbaar.

- De HUD is niet-modaal: geen cursor, geen inputblokkade en geen Esc-afhandeling. De kaart gebruikt nog steeds het Kit-paneel met cursor.
- De cijfertoetsen zijn gedeeld met game-acties. AI Orders leest ze alleen bij een zichtbare HUD; de toets wordt niet geconsumeerd, dus een native actie op hetzelfde cijfer kan ook afgaan.
- Tank- of missiewissel geeft de orders vrij zoals voorheen en sluit de HUD.
- De native-gamehashcontrole uit 0.2.0 blijft actief; andere gameversies worden geweigerd.
- Deze build/tests bewijzen geen live HUD-positie, cursorherstel, Esc/pauze of AI-beweging: daarvoor is het in-game testplan nodig.
- Geen GHPC-code of -assets gebruikt.

## Kaartprestaties en logging

`[Map] RedrawFps = 12` beperkt de periodieke hertekeningen (instelbaar 1–30). Pan, zoom, selectie en de topotextuur na `Apply()` geven daarnaast direct `Invalidate()`. De terreinopbouw en inkleuring zijn behouden: hoogtebemonstering per 16 rijen, daarna één texture-upload.

Het raster past de afstand aan de zoom en Canvas-afmetingen aan, met maximaal ongeveer 13 lijnen per as. Routes gebruiken één gestreepte polyline per tank (alle waypoints blijven opgenomen), met één eindmarker. Markers buiten het zicht worden overgeslagen. Er is geen herhaald CPU-tekenen van het topo-beeld.

Met DetailedLogging verschijnt elke 5 s bij geopende kaart:
`AI Orders map draw: avg=…ms max=…ms shapes=…`
De tijd komt van de publieke `CanvasView.LastDrawMs` (volledige hertekening inclusief raster/upload); elk voltooid redraw telt één keer. `shapes` is het hoogste aantal Painter-aanroepen in dat interval, inclusief Image/Text; een Dot bevat intern ook een rand. Geen meting van game-FPS. Sluiten/heropenen start een nieuw meetinterval.

Schatting voor 10 bondgenoten, 10 zichtbare vijanden, 3 selecties en 10 verplaatsingsroutes: ongeveer 200–220 Painter-aanroepen per redraw (inclusief vijf Text-aanroepen per outlined label); meer geselecteerde/nabije namen en laatst geziene contacten verhogen dat aantal. Dit is een schatting, geen in-game meting.

Kit-wensen: native outline-optie voor `Painter.Text` (nu vier zwarte offsetlabels plus één gekleurd label), een publieke voltooid-redraw-teller/event voor directe timingverzameling, en een rechtsmidden-anker voor HUD's. De publieke API is voldoende voor alle vereiste functies; er wordt geen intern Kit-type of -lid gebruikt.
