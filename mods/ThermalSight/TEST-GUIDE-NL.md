# Testgids — Thermal Sight 0.4.0: warmtemodel (Nederlands)

Installeren: de uitlevering `dist\thermal-sight\` (via `dist\install-all.ps1`, Sprocket gesloten). `Diagnostics.DetailedLogging` staat standaard uit; zet hem voor de tests met logcontrole aan (`DetailedLogging = true` in `sprocket.thermalsight.cfg`, Sprocket gesloten).

## 0. Vooraf
- `BepInEx\LogOutput.log`: `Thermal sight v0.4.0 loaded; shared Settings keybinds, dynamic-resolution rendering and per-vehicle heat model supported.`
- In `BepInEx\config\sprocket.thermalsight.cfg` staat een nieuwe sectie `[Heat]` met standaardwaarden. Geen regel `[Heat] Invalid ...`.

## 1. Sandbox-opstelling
- Eigen tank met een werkende thermal sight (bijv. Mk3 White hot of `tiGen2`, voertuig in Coldwar3).
- Doeltanks (Sandbox Targets of handmatig) op **500 m** en **1000 m**: (a) motor draaiend, (b) motor afgezet, (c) een tank die schiet (of schiet zelf met een tweede tank naast je doel), (d) een wrak (bemanning uitgeschakeld, liefst ook brandend).
- Herhaal alles **overdag** en **'s nachts**.

## 2. Wat je moet zien
1. **Motor draait:** heldere vlek op de motorruimte, en kleinere, nog fellere vlekjes bij de uitlaten. Meer gas of toerental → feller. Romp zelf: net iets lichter dan de omgeving.
2. **Motor afgezet:** de vlek dooft langzaam (uitlaat binnen enkele minuten, motorruimte in ~15 min grotendeels). Een tank die nooit gestart is: alleen een zwakke romp.
3. **Vooraanzicht van een tank met motor achterin:** de motorvlek hoort **niet** door de romp heen te schijnen (benadering: alleen hotspots aan de camerakant).
4. **Schieten:** na elk schot wordt de loop warmer (vooral kamer en mond); na een paar schoten duidelijk zichtbaar, na 3–5 minuten weer koel.
5. **Wrak:** direct na uitschakeling de hele tank heet; brandend nog heter. In ~10 minuten koelt hij af tot net boven de omgeving.
6. **Afstand:** op 1000 m zijn de vlekken kleiner maar nog te zien; ze zitten op de tank en nooit op de grond ernaast.
7. **Nacht:** omgeving donkerder, hotspots blijven fel.
8. Zitten de vlekken gespiegeld (bijv. motorvlek onder de tank i.p.v. erop)? Zet dan `[Heat] InvertSpotY = true` en meld dat.
9. Bestaand gedrag blijft gelijk: profielen, Active IR, beeldversterker, rangefinder, console, camo. Met `[Heat] Enabled = false` is het beeld weer zoals in 0.3.7.

## 3. Verwachte logregels (`[Heat]`, gelimiteerd)
- `[Heat] Resolved vehicle <naam>#<id>: engines=1 (radius ... m), exhausts=N, cannons=N, crewModule=True, fireModule=True, bounds=... m.`
- `[Heat] <naam>#<id>: engine 60 C (running=True, load=0.20), exhaust 160 C, barrel 15 C, wreck 15 C (wreck=False, fire=0.00), spots in view=5/5.`
- `[Heat] Shot detected on <naam>#<id>: barrel 21 C after 1 shots.`
- `[Heat] Hot spots in view: N from M tracked vehicles (sensor 640x480).`
- Een wrak: `wreck=True` en `wreck` rond 265 C (brandend tot ~465 C), daarna dalend.
- Geen `Heat model disabled ...`, `Heat polling skipped` of `Heat projection skipped`. Komt er toch één: graag de hele regel melden.

## 4. Melden
Per situatie (motor aan/uit, schieten, wrak; 500/1000 m; dag/nacht): zichtbaar ja/nee, sterkte goed/te sterk/te zwak, en of de vlekken op de goede plek zitten. Waarden zijn instelbaar in `[Heat]`.

---

# Testgids — Thermal Sight 0.3.7 (Nederlands)

Deze gids is voor testen in de game. De build is niet geïnstalleerd door de
ontwikkelaar; installeer eerst de uitlevering `dist\thermal-sight\` (zet bestanden in
`BepInEx\plugins\SprocketThermalSight\` en
`Sprocket_Data\StreamingAssets\Parts\`) en start de game opnieuw.

## 0. Vooraf

- Controleer in `BepInEx\LogOutput.log` dat de plugin laadt met
  `Thermal sight v0.3.7 loaded`.
- Open Settings / keybinds en controleer dat `Thermal / Toggle` en
  `Thermal / Reload profiles` bestaan. Standaard zijn dat `N` en `F8`.
- Een "sight" is de combinatie van een thermal-sight-part en de losse lens.
  De IR-searchlight is een aparte part met tag `irSearchlight`.

## 1. Coldwar1, voertuigdatum 1955 — active IR met searchlight

1. Bouw een voertuig in Coldwar1 (vanaf 1945.09.04, voertuigdatum rond 1955) met een gunner-sight,
   `thermalSight` of `thermalSightVertical`, de lens en een
   `irSearchlight`.
2. Kies in het rechtsklikmenu op de sight het profiel
   "Active IR night sight" (werkt vanaf 1950.01.01).
3. Richt de searchlight met de lens naar voren en kijk door de sight.

Verwacht:
- Binnen de kegel is de scène sterk verlicht en groen; buiten de kegel bijna
  zwart met ruis.
- De searchlight moet gezond zijn (`HealthFraction > 0`) en op hetzelfde
  voertuig zitten.
- Zonder searchlight: donker beeld met ruis en één waarschuwing
  `Active IR requires a healthy IR searchlight...`, geen spam.

Logregels:
- `[IR searchlight] Model ready: 2573 unique vertices, 2992 triangles, vn/vt retained; ...`
- `[Exterior paint] Housing linked to native exterior paint; component=irSearchlight; ... flags=None; slot=Exterior; ...`

## 2. Coldwar1, voertuigdatum 1955 — camo en lens van dichtbij

1. Verf het voertuig in een andere camo (editor of spuit/verfoptie).
2. Bekijk de IR-searchlight van buiten, met de camera dichtbij.
3. Bekijk daarna de lens van heel dichtbij.

Verwacht:
- De housing (het cilinderhuis) heeft dezelfde camo/verf als de rest van het
  voertuig. De lens zelf blijft donker en verandert niet mee.
- De lens oogt als glas: transparant/donker, met een lichte donkerrode
  IR-filtertint en een glimmende coating.
- De searchlight is ongeveer 0,30 m diameter en 0,40 m hoog; de voet staat
  op het montagepunt.

Als de housing niet meeverft:
- Controleer of `[Exterior paint] Housing linked...` in het log staat.
- Controleer of het voertuig daadwerkelijk een exterior paint-slot heeft
  (verf het voertuig opnieuw).
- Meld welke camo en welk voertuig; dit is het eerste punt dat in de game
  geverifieerd moet worden.

## 3. Coldwar2 — image intensifier

1. Bouw een voertuig in de Coldwar2-start (rond 1960-1974).
2. Kies "Image intensifier Gen 1" (vanaf 1962.01.01) of Gen 2 (vanaf
   1970.01.01).

Verwacht:
- Groen beeld dat de zichtbare scène versterkt; geen warmtebeeld en geen
  voertuigmasker.
- In het donker wordt het beeld automatisch helderder (gain), met meer ruis
  als er weinig licht is.
- Heldere lichtbronnen hebben een zachte bloom.
- Rook houdt het zicht tegen (het werkt op het zichtbare beeld).

## 4. Coldwar3 — thermal

1. Bouw een voertuig in de Coldwar3-start (vanaf 1975.01.01) met een thermal
   sight en lens.
2. Kies "Thermal Gen 1" (1977.01.01), "Thermal Gen 1 (late)" (1983.01.01) of
   "Thermal Gen 2" (1992.01.01) voor zover de voertuigdatum dat toelaat.

Verwacht:
- Voertuigen en warme delen lichten op ten opzichte van de achtergrond.
- Oudere profielen zijn grover/ruiziger; latere scherper.
- Een profiel dat te laat is, is uitgegrijsd of toont "(from yyyy.MM.dd)" en
  is niet kiesbaar.
- Een opgeslagen keuze die te laat is: gewoon zicht + waarschuwing
  `Saved sight profile ... works from ...; normal sight retained, identity/mass/cost preserved.`

## 5. Open laatste era

1. Start een scenario in de laatste era (de era die op 1975.01.01 begint;
   er is geen Coldwar4).
2. Kies "Thermal Gen 2" (1992.01.01).

Verwacht:
- Het profiel is kiesbaar en werkt; de laatste era is open-ended
  (`TechDate.MaxValue`) en heeft geen kunstmatige bovengrens.
- Op 1976.12.31 is "Thermal Gen 1" (1977.01.01) nog niet kiesbaar; op
  1977.01.01 wel.

## 6. Opgeslagen 0.2.9-voertuig laden

1. Laad een voertuig dat in 0.2.9 is opgeslagen, met een thermal sight en
   eventueel een opgeslagen profiel.
2. Open het rechtsklikmenu op de sight.

Verwacht:
- Massa en kosten van de sight blijven aanwezig, ook als het profiel op de
  datum niet werkt.
- Een oud profiel blijft als identiteit bestaan; werkt het niet op de datum,
  dan krijg je gewoon zicht en de waarschuwing hierboven.
- De catalogus wordt niet stilzwijgend naar een ander profiel omgezet.

## 7. F8 — profielen herladen

1. Pas `BepInEx\plugins\SprocketThermalSight\thermal-models.json` aan
   (bijvoorbeeld een `availableFrom` of `noise`).
2. Druk in de game op `F8`.
3. Maak daarna een fout in de JSON en druk opnieuw op `F8`.

Verwacht:
- Geldige JSON: `[Thermal] Accepted N part profiles.` en het nieuwe profiel
  is direct kiesbaar.
- Ongeldige JSON: `Profile reload rejected; previous catalog retained: ...`;
  de vorige catalogus blijft werken.
- Na F8 worden part-datums en de part-cache opnieuw gelezen.

## 8. Rangefinder met gemengde munitie

1. Laad een voertuig met een laser-rangefinder en twee verschillende
   granaattypes (bijvoorbeeld AP en HE).
2. Wissel tussen de granaten en meet een doel op 800-1500 m.
3. Herhaal met een tweede granaattype.

Verwacht:
- De ballistische correctie gebruikt het **geladen** granaattype, niet het
  eerste/initiële profiel.
- Wisselen van granaat verandert de berekende elevatie.
- Zonder Expanded Ballistics blijft de mod werken (fallback); er verschijnt
  geen blijvende "assembly not found"-fout.

## 9. Log controleren

Gebruik `BepInEx\LogOutput.log`. Nuttige regels:

- `Thermal sight v0.3.7 loaded`
- `[Thermal] Accepted ... part profiles.`
- `[Thermal native date] component=...; part=...; invention=...`
- `[Optical glass] shader=...; ... native unpainted material ready.`
- `[IR searchlight] Model ready: ...`
- `[Exterior paint] Housing linked to native exterior paint; component=...; flags=None; slot=Exterior; ...`
- `[Thermal] Profile reload rejected; previous catalog retained: ...`
- `Saved sight profile ... works from ...; normal sight retained ...`

Meld bij een fout: het scenario/era-jaar, het gekozen profiel, het voertuig,
de camo, en de relevante logregels. Voeg geen hele logs met persoonlijke
paden toe als dat niet nodig is.

## 10. Thermal Sight 0.3.3 — laser-rangefinder en drop

Zet `Diagnostics.DetailedLogging = true` in `sprocket.thermalsight.cfg` (staat standaard uit).
Controleer eerst `Thermal sight v0.3.3 loaded` in de log.

1. Sandbox, dezelfde T-84 met `automaticLaserRangefinder`, t72StyleSight en de
   mix `apds_late=11;heat=5`. Wacht tot de eerste granaat geladen is.
2. Kijk door de sight, meet een doel op ±500 m (Thermal / Measure range) en
   blijf 10 s richten zonder handmatig te zeroen.
3. Vuur één APDS. Wacht op de herlading, richt opnieuw 10 s.
4. Wissel naar HEAT, wacht tot hij geladen is, meet opnieuw en vuur.

Verwacht en logregels:
- `[Rangefinder FCS] range=500,0m speed=…` (eerste 8 keer) en
  `[Rangefinder FCS diag] … applied=<getal groter dan 0>`: de correctie werkt.
- Geen `applied` maar wel een reden: die reden is de oorzaak.
  - `aimCalls=0`: de `GunLayer.AimAtPosition`-hook wordt niet bereikt.
  - `stale-control-frame`: de richtopdracht komt buiten het control-frame.
  - `control-target-not-linked-cannon` plus een regel `layer target=…`: de
    richtlaag van de speler hoort niet bij het gekoppelde kanon.
  - `native-distance-changed …` of `automatic zero 500 m released: …`: iets
    anders verandert de native afstand.
  - `guided:<reden>`: Expanded Ballistics meldt een geleide of onbekende
    granaat (`chamber-unconfirmed`, `chamber-unknown:<id>`, `mix-frozen`).
- Bij stap 4 valt HEAT nog steeds lager dan APDS: Thermal rekent met de
  mondingssnelheid van het ontwerp, niet met die van de geladen HEAT. Dat
  zit in Expanded Ballistics. Noteer de `speed=` uit de FCS-regel.
- Handmatig zeroen (native afstand) compenseert de drop niet; dat is native
  gedrag.

Stuur de regels met `[Rangefinder` uit `BepInEx\LogOutput.log` terug.

## 11. Thermal Sight 0.3.4 — drop met de snelheid van de geladen granaat

Eerst Expanded Ballistics 0.22.17 installeren; met een oudere EB blijft alles
zoals in 0.3.3 (`speedSource=cannon`). Controleer `Thermal sight v0.3.4 loaded`.

1. Dezelfde T-84 en mix `apds_late=11;heat=5` als in §10, DetailedLogging aan.
2. Met APDS geladen: meet ±500 m, richt 10 s, vuur. De granaat moet op het
   richtpunt komen.
3. Wissel naar HEAT, wacht tot hij geladen is (HUD: LOADED), meet opnieuw,
   richt 10 s en vuur. HEAT moet nu ook op het richtpunt komen, niet eronder.

Verwacht in de log:
- APDS: `[Rangefinder FCS] range=…m speed=<APDS>m/s speedSource=chambered
  cannonSpeed=<APDS>m/s … lift=…`.
- HEAT: een nieuwe regel met `speedSource=chambered`, een lagere `speed=` dan
  `cannonSpeed=` en een grotere `lift=`. Dat bevestigt de fix.
- `speedSource=cannon` met HEAT geladen: EB levert de snelheid niet (oude EB,
  of de kamer is leeg/onbevestigd). Stuur dan ook de `[Rangefinder FCS diag]`-
  regels mee.

## 12. Thermal Sight 0.3.6 — oscillerende toren (T-84)

Controleer `Thermal sight v0.3.6 loaded`. DetailedLogging aan.

1. T-84 surblinde (Addon): meet ±500 m, richt 10 s, vuur. De granaat moet op
   het richtpunt komen.
2. Controleer in `BepInEx\LogOutput.log`:
   - één regel `[Rangefinder FCS diag] topology …` met `layer0=… accepted=yes`,
     `layer1=… accepted=yes`, `horizontal=…`, `vertical=…`; noteer de namen en
     VUID's.
   - `[Rangefinder FCS] range=…` en in de 5-secondenregel `applied` groter dan 0.
3. Herhaal met een tank zonder oscillerende toren (bijv. de Leopard 2a7 of een
   stockvoertuig). Dezelfde `[Rangefinder FCS]`-regel moet verschijnen en
   `applied` moet groter dan 0 zijn.
4. Staat er `accepted=no` bij de elevation-layer, stuur dan de topology-regel
   terug. Dan loopt die laag niet via `AimAtPosition` en is een andere aanpak
   nodig.

Stuur de regels met `[Rangefinder` uit `BepInEx\LogOutput.log` terug.


## 0.3.6 — Laser box sight

1. Sluit Sprocket en installeer de uitlevering `dist\thermal-sight\` (zie boven).
2. Open een voertuig vanaf 1945.09.03. Plaats `laserBoxSight` in Sights and electronics naast een T72-sight. Wijs hem als gunner-sight toe, zoals de T72. Controleer selectie, verplaatsen, dupliceren en schalen (0,25–3).
3. De drie lenzen wijzen naar voren (+Z); de neergeklapte klep ligt ervoor. Bekijk `box-sight/renders/front-oblique.png` en `paint-mask.png` als referentie. Er is geen fysiek oculair; het scopepunt achterin is virtueel, zonder modelaanpassing.
4. Verander de voertuigcamo. Alleen de felroze delen van het masker (kast, klep, lensranden) moeten meekleuren. Steel, dark en alle drie glanzende glasvlakken blijven gelijk. Herhaal dit eerst zonder IR-searchlight op het voertuig en daarna met een IR-searchlight; beide housings moeten hun eigen camo blijven volgen.
5. Kijk door de scope en gebruik de bestaande Settings-actie `Thermal / Measure range`. Verwacht een afstand in de HUD, handmatige laser zoals de T72, zonder automatische elevatiecorrectie of thermal-profiel voor deze sight. Controleer dat de T72 er hetzelfde uitziet en nog meet, en de IR-searchlight hetzelfde werkt.
6. Sla het voertuig op en laad het opnieuw; controleer model, camo, scope en meetknop opnieuw. Dit is nog niet live geverifieerd.

Verwachte logregels (model-/paintregels verschijnen beperkt per sessie):
- `Thermal sight v0.3.6 loaded; ...`
- `[Electronics icon] Loaded box-sight-icon.png` (bij opvragen van het icoon; bestaande limiet op icon-logs).
- `[Box sight] User model loaded: 5034 triangles (2700 paint, 914 steel, 748 dark, 672 lens); +Z forward; vn/vt retained.`
- `[Box sight] Native model applied; exterior paint linked; flags=None; slot=Exterior; meshMatch=True`
- `[Rangefinder] Request sight=laserBoxSight, device=True, ...` en `[Rangefinder] Reading=...; ...; sight=laserBoxSight.` (binnen de bestaande loglimieten).
- Geen `laserBoxSight visual unavailable`, `Sight visual scan unavailable` of model-load-waarschuwingen.

Voor geautomatiseerde controles: de testprojecten in `src\tests` (zie `GUIDE.md`); dit bewijst geen native gameplay.

## 0.3.7 — Camo op alle modellen

Sluit Sprocket en installeer de uitlevering `dist\thermal-sight\` (DLL en de vijf vervangen OBJ's).

Controleer eerst `Thermal sight v0.3.7 loaded` en daarna per onderdeel:
plaats het onderdeel, verf het voertuig in een duidelijk andere camo (bijv.
woestijn of winter), bekijk het van dichtbij en controleer dat glas donker en
ongeverfd blijft. Herhaal na opslaan en opnieuw laden.

1. **IR-searchlight** (`irSearchlight`): plaats op een voertuig vanaf 1950,
   verf het voertuig, bekijk de cilinderbehuizing. Behuizing volgt de camo;
   de IR-lens blijft donker met filtertint. Log: `[Exterior paint] Housing
   linked to native exterior paint; component=irSearchlight; flags=None;
   slot=Exterior; ...`.
2. **Thermal head** (`thermalSight`, `thermalSightVertical`, `thermalSightModel1/2/3`,
   `thermalSightMk3WhiteHot/BlackHot`): plaats naast de lens op een voertuig
   vanaf 1977 (MK3 vanaf de part-datum). De behuizing (`Paint`) volgt de camo;
   `Dark` en `Lens` blijven. Log: `[Thermal model] ...` en de gedeelde
   `[Exterior paint]`-regel met `component=thermalSight...`.
3. **T72-sight** (`t72StyleSight`): plaats als gunner-sight. Het
   `Housing`-deel inclusief montageframe volgt de camo; `Glass` en
   `AmberGlass` blijven glas. Log: `[T72 sight] Native model applied; ...`.
4. **FCS-console** (`sightConsole`): plaats in de crew/console-opstelling.
   Alleen `Housing` volgt de camo; de gereserveerde `Glass`-submesh (leeg)
   blijft vast. Log: `[FCS] Native model applied; ...`.
5. **Laser box sight** (`laserBoxSight`): plaats als gunner-sight. `paint`
   (kast/klep/lensranden) volgt de camo; `steel`, `dark` en `lens` blijven.
   Log: `[Box sight] Native model applied; exterior paint linked; flags=None;
   slot=Exterior; ...`.
6. **Laser-rangefinder-sight** (`laserRangefinderSight`): plaats als
   sight/device. `Paint`, `PaintLight` en `Mount` volgen de camo; `Glass`
   blijft. Log: `[Rangefinder model] ManualLaser; exterior paint linked;
   glass fixed.`
7. **Automatic laser rangefinder** (`automaticLaserRangefinder`): idem;
   `Paint` volgt de camo, het losse geïntegreerde venster blijft glas. Log:
   `[Rangefinder model] AutomaticLaser; exterior paint linked; glass fixed.`
8. **Optical coincidence rangefinder** (`opticalCoincidenceRangefinder`):
   `Paint` en `Tube` volgen de camo; `Glass` blijft. Log:
   `[Rangefinder model] Optical; exterior paint linked; glass fixed.`
9. **Losse lenzen/ramen** (`thermalSightLens`, `clearWindowPane`,
   `tintedWindowPane`, `yellowWindowPane`): plaats en controleer dat elk
   volledig glas blijft; er hoort geen camo op te zitten en er is geen
   `[Exterior paint]`-regel voor deze onderdelen.

Verwacht algemeen: glas- en `Dark`/`steel`-vlakken veranderen niet mee met de
camo; geen exceptions of `visual unavailable` in de log.
In de ontwerper-preview (zonder vehicle/transform/controller) blijven alle
modellen hun oude vaste kleur houden; dat is de fallback, geen fout.

Voor geautomatiseerde controles: de testprojecten in `src\tests` (zie `GUIDE.md`);
dit bewijst geen native camo.
