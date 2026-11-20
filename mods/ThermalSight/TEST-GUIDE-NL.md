# Testgids Thermal Sight 0.5.4

Deze build is voorbereid, niet geïnstalleerd. Alleen de gewijzigde onderdelen testen: de negen gestandaardiseerde profielen, legacy-aliassen, de polariteitsschakelaar en het gerepareerde actieve IR-nachtzicht.

## Installeren en terugzetten

Sluit Sprocket. Voer `.\install-0.5.4.ps1` uit (standaard `E:\SteamLibrary\steamapps\common\Sprocket`). Dit maakt eerst een geverifieerde back-up van de huidige DLL en `thermal-models.json` en kopieert daarna beide nieuwe bestanden; je `.cfg` blijft behouden. Herstel met `.\uninstall-0.5.4.ps1` (optioneel `-BackupDirectory <pad>`). Beide scripts weigeren zolang Sprocket draait.

## Gerichte gamecontrole

1. **Profielenlijst en oude tanks/saves (geen duplicaten):**
   - Open een tank met een thermische sight in de designer: de dropdown **Profile** toont precies 9 profielen (`irActiveL1`, `iiGen1`, `iiGen2`, `tiGen1`, `tiGen1Late`, `tiGen2`, `tiGen3`, `tiGen3Uncooled`, `tiGen3HD`), zonder de vijf oude `thermalSightModel*`/`Mk3*`-duplicaten.
   - Laad een oudere blueprint met een legacy part-GUID of opgeslagen profiel-id: Model 1 -> `tiGen1`, Model 2 -> `tiGen1Late`, Model 3 / Mk3 White Hot -> `tiGen2`, Mk3 Black Hot -> `tiGen2` met zwart-heet actief. Datumpoort en opgeslagen blueprint-formaat (`sprocketThermalProfilesV1`) blijven ongewijzigd.
2. **Polariteitsschakelaar (`Thermal / Polarity`):**
   - Wijs in *Settings / Keybinds* een vrije toets toe aan **Thermal / Polarity** (standaard ongebonden; `N` en `F8` zijn al bezet).
   - Test de toets op een thermisch profiel: **ook al vóór het eerste scopebeeld** (vóór inschakelen van de scope/thermal) en tijdens actief beeld. Het beeld keert om tussen wit-heet en zwart-heet, en de HUD-tekst wisselt mee tussen `WH` en `BH`.
   - De keuze blijft alleen per sight tijdens de sessie bestaan (niet in de save). Op `irActiveL1`, `iiGen1` en `iiGen2` verandert niets (beeld blijft groen).
3. **Actief IR bij nacht (`irActiveL1`, met werkend IR-zoeklicht):**
   - Rijd in een nachtscène en bekijk een doel op 300–800 m met ingeschakelde actieve IR-sight.
   - Verwacht beeld: duidelijke elliptische lichtbundel (6°) op de grond rond het doel, zachte overgang naar bijna zwart buiten de kegel en in de lucht, geen witte vlek/blob en geen dambord- of rasterranden. Op 300 m zijn romp, toren en schaduwdetail van de tank herkenbaar met fijne buiskorrel; richting 800 m valt het licht geleidelijk weg.
   - Zonder gezond IR-zoeklicht blijft het beeld donker met zwakke groene korrel en de bekende melding.
4. **Alle 9 profielen (progressie en leesbaarheid):**
   - **Actief IR (1950):** 480×360, 25 Hz, 6°-bundel, fijne korrel.
   - **II Gen 1 (1962) -> II Gen 2 (1970):** 400×300 -> 480×360, minder ruis, zachte verzadiging, bloom alleen op echt felle lichtbronnen, groene buis.
   - **TI Gen 1 (1977) -> Gen 1 late (1983) -> Gen 2 (1992):** 240×180 -> 400×300 -> 640×480, 15 -> 25 -> 30 Hz, ruis omlaag; achtergrond middendonker met zichtbaar terrein, tank duidelijk los met warme motor/uitlaat; vanaf Gen 2 herkenbaar op 1000 m.
   - **TI Gen 3 (2004, gekoeld):** 640×480, 50 Hz, zeer lage ruis, afkoeltijd actief.
   - **TI Gen 3 uncooled (2010):** 640×480, 30 Hz, standaard wit-heet (schakelbaar naar zwart-heet), duidelijk ruiziger/zachter maar lichter en goedkoper dan gekoelde Gen 3.
   - **TI Gen 3 HD (2015, gekoeld):** 1280×1024, 60 Hz, scherpste beeld met lichte randverscherping.

## Verwachte logregels

- `Thermal sight v0.5.4 loaded; nine standardised sensor profiles, legacy part aliases, white-hot/black-hot switch, repaired active IR (soft exposure, per-pixel beam, fine grain) and per-type night-sight HUD supported.`
- `[Thermal] Accepted 9 part profiles; 5 legacy part aliases.`
- Bij een oud profiel/onderdeel: `[Thermal] Legacy profile '<oud>' maps to '<nieuw>'.`
- Bij polariteitswissel: `[Thermal] Polarity <naam>: black-hot (session only).` of `white-hot (session only).`
- Bij polariteitstoets zonder actieve sight: éénmaal `[Thermal] Polarity key pressed without an active sight; ignored.`
- Geen foutmeldingen (`runtime-error`, `GPU sensor readback failed`, `Disabled for this session`).

## Reproduceerbare checks (lokaal, zonder game te starten)

- Build (0 warnings): `dotnet build src\SprocketThermalSight.csproj -c Release -p:GameDir=E:\SteamLibrary\steamapps\common\Sprocket -p:UseAppHost=false` (`BUILD.txt`).
- Tests: `Sensor.Tests.csproj` en `ConsoleLink.Tests.csproj` (`TESTS.txt`).
- Controlebeelden voor/na (300 m en 1000 m) en overzicht: `review-0.5.4\contact-sheet.png` en `review-0.5.4\image-targets.txt`.
