# Changelog — Sprocket Thermal Sight

## 0.5.4 — 2026-11-19

- **Dubbele legacy-profielen verwijderd zonder oude tanks of saves te breken.** De vijf oude profielen (`thermalSightModel1`, `thermalSightModel2`, `thermalSightModel3`, `thermalSightMk3WhiteHot`, `thermalSightMk3BlackHot`) zijn uit de keuzelijst verwijderd. Een aliastabel (`aliases`) in catalogus v3 mapt zowel de vijf oude part-GUID's (gevalideerd tegen `ThermalAlias.LegacyPartGuids`) als opgeslagen profiel-id's (`sprocketThermalProfilesV1` en legacy single-profile key): Model 1 -> `tiGen1`, Model 2 -> `tiGen1Late`, Model 3 / Mk3 White Hot -> `tiGen2`, Mk3 Black Hot -> `tiGen2` met `blackHot: true`. `defaultProfileId` blijft `tiGen1`; de datumpoort blijft ongewijzigd.
- **Polariteitsschakelaar (WH/BH) voor thermische profielen.** Toegevoegd via de gedeelde keybind-route (`Thermal / Polarity`, standaard ongebonden). Per-sight sessie-opslag in geheugen (`PolarityState`), zonder nieuwe save-hooks of wijziging van het opslagformaat. Werkt ook vóór de eerste scopeframe op de actieve sight die de runtime kent (zonder sight: niets doen en één keer loggen). Overschrijft de catalogus-standaard beide kanten op; de HUD-tekst (WH/BH) volgt de werkelijke polariteit. Actief IR en beeldversterkers blijven groen.
- **Actief IR (`irActiveL1`) gerepareerd voor nachtherkenning op 300–800 m.**
  - Belichting: lineaire `* 20`-versterking vervangen door zachte verzadiging `1 - exp(-exposureK * luma * illumination)` (`exposureK = 6.0`), zodat verlichte vlakken hun schaduwdetail behouden (< 5 % geklipt op 300 m).
  - Bundel en grond: de 6°-kegelafval wordt per pixel berekend (`IrBeamPolicy.PixelWeight`, smoothstep tot 0 op `beamAngleDeg/2`) met een 3-stralen frustumbasis zonder per-pixel native calls; het 32×24-raycastraster levert alleen afstandsverval (geclamped kwadratisch, `nearM = 200 m`, naar 0 op `beamRangeM`) en occlusie. Oorzaak donkere grond: gemiste luchtcellen werden als `0` meegeïnterpoleerd en verdonkerden aangrenzende grond; genormaliseerde 2×2-interpolatie (`SampleGrid`) negeert luchtcellen (`-1`), behoudt occlusie (`0`) en laat zuivere lucht op `0`.
  - Korrel en scherpte: resolutie verhoogd naar 480×360, `smoothPixels: true`, fijne per-pixel ruis (`noise = 0.04`, iets sterker in donkere delen), zonder dambord- of rasterartefacten. Buiten de bundel blijft het beeld bijna zwart met zwakke korrel.
- **Eén standaard voor alle 9 moderne profielen (catalogus v3).** Alle beeldvelden expliciet (validatie weigert ontbrekende velden), consequente progressie op datum/resolutie/ruis/verversing/massa/kosten (met `tiGen3Uncooled` lichter, goedkoper, ruiziger en langzamer dan gekoelde Gen 3, standaard `whiteHot`). Beeldversterkers (`iiGen1`, `iiGen2`) gebruiken nu eveneens zachte verzadiging, bloom alleen op echt felle bronnen (`> 0.88`) en fijnere korrel (`0.035` / `0.02`, `smoothPixels: true`).
- **Controle en levering:** 41 HarmonyPatch-regels (ongewijzigd), geen nieuwe shaders, native state alleen gelezen. 0 build-warnings; 31.921 `Sensor.Tests`- + 472 `ConsoleLink.Tests`-assertions groen; voor/na-controlebeelden op 300 m en 1000 m plus `contact-sheet.png` in `review-0.5.4/`. Install/uninstall 0.5.4 voor E: met geverifieerde back-up en weigering bij draaiende game. Niet geïnstalleerd en niet live getest.

## 0.5.3 - 2026-11-18

- Hook-heraudit na de Steam-update van 18-11 (buildid 26757044, GameAssembly `77FC6017...`): alle patchdoelen bestaan nog met dezelfde signature, RVA's en velden ongewijzigd, geen gedeeld adres (ICF) in de volledige dump van 116.538 methodes.
- De postfix op `ScopeController.Update` is verwijderd. Die methode is in de game inlined en de hook liep nooit; `Runtime.Tick` leest de scope al elke frame. 41 HarmonyPatch-regels.
- Audit door Codex (GPT-6); afgerond en gecontroleerd door Claude (Opus) na de usage-limiet van Codex.

## 0.5.2 ? 2026-11-17

- Motorwarmte volgt de lokale motorbounds: acht geori?nteerde hoeken worden een gevuld convex omhulsel in het sensorbeeld. Ongeldige bounds, een doorsnijding van het cameravlak of een te kleine projectie vallen terug op `EngineSpotRadiusM` (0,9 m). Alleen voertuigmaskerpixels ontvangen warmte.
- `[Heat] SpotSoftness = 0.75` (0?1): smoothstep-rand voor motor, uitlaat en loop. Het wrakplateau blijft exact gelijk. Uitlaat en de drie bestaande loopspots behouden hun posities en vorm.
- `[Heat] HullBaseK = 3` (0?100 K): gelijkmatige lichte rompbijdrage boven de omgeving, gedeeld door de bestaande `FullScaleKelvin`; 0 herstelt de oude basisbijdrage. Temperaturen en opwarm-/afkoelcurven zijn niet gewijzigd.
- Uncooled blackHot: contrast/backgroundLevel `tiGen3Uncooled` 1,9/0,26 ? 0,7/0,43; legacy `thermalSightMk3BlackHot` 1,7/0,24 ? 0,7/0,43. Zo blijft de achtergrond middengrijs en behoudt de romp tintverschillen. `tiGen2` krijgt expliciet contrast 1,2 (was impliciet 1,4). Andere waarden en profielen blijven gelijk.
- HD-munitielijst: EB Label heeft voorrang op Code; naam plus aantal, 14 in plaats van 11 ontwerpeenheden, opaak wit op een donkere achtergrond, alle aangeleverde typen (geen afkapping tot vier). Geen gegevens: `----`. Voorraadgegevens worden ook bij een onbevestigde kamer getoond; alleen bevestigde kamerdata wordt gemarkeerd.
- De oude `ammo=unconfirmed`-log vermengde ontbrekend snapshot en onbevestigde kamer. Nieuwe begrensde statuslogs onderscheiden `no-snapshot`, `frozen`, `chamber-unconfirmed` en `chamber-confirmed`. EB-aanwezigheid is geen garantie voor een snapshot; de ge?nstalleerde API levert alleen een ingestelde lading.
- Gericht testprogramma `src/tests/HeatShape.Tests.csproj`, echte 0.5.1-bron als baseline; controlebeelden en CPU-metingen in `review-0.5.2/`. Install/uninstall 0.5.2 voor E: met geverifieerde back-up van DLL ?n catalogus en weigering bij draaiende game.
- Gebouwd door Codex (GPT-6), 42 HarmonyPatch-regels, geen nieuwe hooks of shaders; native gameplaystate alleen gelezen. Niet ge?nstalleerd en niet live getest.

## 0.5.1 — 2026-11-16

- **HUD via een uGUI-Canvas.** Oorzaak van "geen HUD" in de test van 0.5.0: `GUI.DrawTexture`/`GUIStyle` bestaan niet in deze IL2CPP-build (`Method unstripping failed`), de HUD schakelde zichzelf uit. Nu zoals Gun Sights: een eigen Canvas (ScreenSpaceOverlay, sortering 70, boven Gun Sights' 60), één RawImage met een `Texture2D` waarin `HudPainter` de lijnen op de CPU rastert (ontwerpframe 640×480, 1–2 pixels per eenheid, anti-aliased, ververst alleen bij verandering en hooguit 20× per seconde; ±0,25 ms per repaint), TextMeshProUGUI-teksten met `TMP_Settings.defaultFontAsset`, en vier zwarte zijpanelen voor de ronde oculairrand. Inhoud en vormgeving per type als 0.5.0. Geen IMGUI meer behalve `GUI.Label`. Vangnet ongewijzigd: faalt iets, dan één waarschuwing, HUD uit, thermalbeeld blijft. Canvas en texture worden bij uitzetten, wisselen en uitstappen vernietigd (in LateUpdate, niet in een hook).
- **Afkoelen één keer per sight per sessie.** Eerste keer inschakelen: 20 s grijs ruisbeeld. Uitzetten pauzeert niet, de tijd loopt door; is de sight afgekoeld, dan is hij bij opnieuw inschakelen direct klaar. Opnieuw afkoelen alleen na een ander voertuig of een nieuwe missie (gevechtsscène verlaten). Log `[Thermal] Cooling 20 s: ...` alleen bij de start en `[Thermal] Cooled; sensor image live: ...` één keer bij het einde (ook als de sight dan uit staat). "COOLING mm:ss" staat ook via `GUI.Label` in beeld als de HUD niet beschikbaar is (uitgezet, mislukt of font nog niet klaar).
- **Gen 3-contrast.** Oorzaak van het vlakke lichtgrijze beeld: `tiGen3`, `tiGen3Uncooled` en `tiGen3HD` hadden geen contrast-/achtergrondwaarden in de catalogus en kregen de standaardwaarden (contrast 1,4, backgroundLevel 0,3, detail 0,3, gamma 0,6, 64 grijstinten). Nu in de catalogus: Gen 3 contrast 2,2 / helderheid 0,14; uncooled 1,9 / 0,08; HD 2,6 / 0,20; alle drie backgroundLevel 0,24–0,26, backgroundDetail 0,14–0,15, backgroundGamma 1, gamma 1, grijstinten 256 (uncooled 128). Testscène (0–255): achtergrond 45–62 < lucht 96–104 < romp 107–112 < motor 255; 0.5.0 gaf achtergrond 119, romp 155. Warmtemodel ongewijzigd.
- HD-beeld na het afkoelen gecontroleerd: geen fout gevonden. In de log van de test staat geen `Cooled`; elke toggle begon het afkoelen opnieuw, dus het HD-beeld werd nooit bereikt. Een pure test bevestigt dat het HD-pad na het afkoelen het thermalbeeld geeft.
- Geen nieuwe Harmony-patches (42 vóór en na), geen shaders, native state alleen gelezen. Niet geïnstalleerd.

## 0.5.0 — 2026-11-16

- **HUD per type nachtzicht** (goedgekeurde mockups `2026-11-16\sight-huds`), getekend in de bestaande `OnGUI`-route, alleen zolang een nachtzicht- of thermalbeeld in de scope staat (zelfde voorwaarde als de beeldvervanging). Ontwerpframe 640×480, gecentreerd en geschaald op de schermresolutie. Kleur volgt het palet: groen, wit, zwart bij zwart-heet; amber alleen voor waarschuwingen (COOLING, LRF-fout) en de volghoeken.
  - Actief IR: ronde oculairrand, kruis met stadia-streepjes, IR-lamp (gevuld als de IR-schijnwerper de scène belicht, open ring anders).
  - Lichtversterker Gen 1/2: ronde rand, chevron met afstandsladder, versterkingsbalk (de automatische gain van de buis).
  - Thermal Gen 1: richtvierkant, afstand in 4 cijfers, FOV N/W, WH/BH. Gen 1 (laat): plus munitieletter (EB), RDY (kanon geladen), "2" bij meerdere echo's.
  - Thermal Gen 2: beeldveldhoeken, voorhoud-streepjes, elevatieschaal rechts, afstand, munitie (EB), FOV en polariteit.
  - Thermal Gen 3: koersband, romp/toren-icoon met TUR-hoek, afstand met LRF-status, munitie + LOADED, zoomfactor, polariteit, COOLING mm:ss, elevatieschaal.
  - Thermal Gen 3 (uncooled): koersband, kruis, afstand, polariteit, zoom.
  - Thermal Gen 3 HD: dunne lijnen, koersband, kanaal/polariteit, e-zoom, munitielijst met aantallen (EB), afstand, toren-klok, elevatie; volghoeken alleen als de laatste laserreflectie een bewegend lichaam (voertuig) op de huidige richtlijn is. Alleen visueel, geen doelvolging.
  - Data alleen gelezen: koers (sight-richting), toren t.o.v. romp, kanonelevatie (wapen-transform t.o.v. romp), zoom (camera-FOV, 60° = 1×; N/W uit het native zoombereik), laatst gemeten afstand van de eigen rangefinder, RDY uit `WeaponBehaviour.LoadState`, munitie via reflectie uit Expanded Ballistics (optioneel, geen verwijzing). Niet leesbaar: "----" of weggelaten. STAB weggelaten (geen native stabilisator).
  - Config `[Hud] Enabled = true`, `[Hud] DrawReticle = true` (eigen richtmerken uit als het native of Gun Sights-reticle al zichtbaar is).
- **Drie nieuwe profielen:** `tiGen3` Thermal Gen 3 (2004, 640×480, 50 Hz, gekoeld, 36 kg, +4500), `tiGen3Uncooled` (2010, 640×480, 30 Hz, zwart-heet, ongekoeld, 24 kg, +3800), `tiGen3HD` (2015, 1280×1024, 60 Hz, gekoeld, randverscherping, 38 kg, +6000). Nieuwe catalogusvelden `cooled` en `sharpen` (standaard uit; bestaande profielen ongewijzigd).
- **Afkoelen:** `[Sensor] CooldownSeconds = 20`. Na het inschakelen van een gekoeld profiel een grijs, ruisend beeld met "COOLING mm:ss"; geen andere gevolgen. Logregels `[Thermal] Cooling 20 s: ...` en `[Thermal] Cooled; sensor image live: ...`.
- **Randverscherping HD:** unsharp mask op de bestaande 3×3-buren in de CPU-beeldbewerking, geen shader. Sensoren boven 524.288 pixels verwerken de rijen parallel (HD ±6 ms per beeld op 16 threads); kleinere profielen houden exact de oude volgorde en uitkomst.
- Resolutiegrens van de catalogus 1024×768 → 1280×1024 (max. 1.310.720 pixels).
- Rangefinder: de getoonde waarde wordt ook als getal bewaard voor de HUD (met echo-aantal en richting); meten, tekst en automatisch instellen ongewijzigd.
- Geen nieuwe Harmony-patches (42 vóór en na), geen shaders, native state alleen gelezen. Warmtemodel, camo, UV's en profielopslag ongewijzigd. Niet geïnstalleerd.

## 0.4.3 — 2026-11-14

- **Hotspots komen nu in beeld.** Oorzaak: de doorschijnregel (`HeatProjection.Facing`) gaf motor- en uitlaatvlekken aan de verre kant van het voertuig gewicht 0. De doelen in de sandbox staan naar de speler toe (`facing: towards`), dus motor en uitlaten (achterin) vielen altijd weg: `spots in view=0/3`. Nu houdt een vlek aan de verre kant `[Heat] FarSideWeight` (standaard 0,35) van zijn sterkte; 0 geeft het oude gedrag.
- De projectie gebruikt nu de camera van de custom-pass-context (`ctx.hdCamera.camera`, dezelfde die het masker tekent) en zijn eigen matrices (`projectionMatrix * worldToCameraMatrix`). De straal in pixels komt uit de geprojecteerde straal, dus ook goed bij een fysieke camera (focal length, Gun Sights). Rij 0 van de readback is de onderkant van het beeld; `InvertSpotY` blijft als uitweg.
- **De loop warmt op na schoten.** Oorzaak: de schoten werden alleen bij de poll van een voertuig in beeld geteld, vergeleken met waarden van het oplossen; bij de eerste waarneming gooide `Initialize(input with { Shots = 0 })` het gedetecteerde schot weg ("barrel 15 C after 0 shots"). Nu telt een `ShotCounter` per kanon elk frame (twee native getters, alleen lezen), voor alle gevolgde voertuigen; de poll verwerkt alle schoten sinds de vorige poll, ook die vóór de eerste waarneming. Twee signalen van hetzelfde schot (tijd en laadstatus, ook op verschillende frames) tellen één keer. Voertuigen dicht bij de camera (de eigen tank) worden altijd gepold.
- Diagnose achter `DetailedLogging` (gelimiteerd, elke 10 s per voertuig): `[Heat] Diag <voertuig>: engine spot screen=(x, y) px ..., depth ... m, result=<in-view|behind-camera|off-screen|far-side|out-of-range|negligible|no-vehicle-pixels>, vehiclePixels=N ...; camera=<naam>#<id> fov=...`. De samenvattingsregel noemt nu ook de afwijzingen per reden en de camera.
- Geen nieuwe Harmony-patches (42 vóór en na), geen shaders, native state alleen gelezen. Profielopslag (0.4.2), rangefinder, camo en UV's ongewijzigd. Niet geïnstalleerd.

## 0.4.2 — 2026-11-14

- Het gekozen sight-profiel (inspector, dropdown "Profile") blijft nu bewaard in de blueprint. Oorzaak: 0.4.1 schreef het profiel alleen met een postfix op `VehicleComponent.SaveData`; de buitenste `VehicleObjectSerialization.ToBlueprint` slaat die route over. Geen enkele blueprint in `Factions` bevatte `sprocketThermalProfile`, dus na laden gold altijd het standaardprofiel.
- Nieuwe key `sprocketThermalProfilesV1` in de object-state, naast `sprocketFcsLinksV1`: `{"Version":1,"Profiles":[{"Sight":<VUID>,"Profile":"<profileId>"}]}`. Geschreven in de bestaande `ToBlueprint`-postfix, gelezen in de bestaande `InstantiateStructured`-postfix. Zelfde regels als de console-links: verse state, andere keys behouden, VUID-remap bij dupliceren en instantiëren, kapotte of dubbele regels geweigerd, toepassen na de batch via `ProfileStateHooks` (dezelfde opslag als `Select`).
- Alleen een expliciete keuze op een thermische sight wordt opgeslagen; zonder keuze geldt bij laden het standaardprofiel van de part, zoals voorheen. Een onbekend (verwijderd) profiel-id blijft bewaard; de bestaande waarschuwing "Saved profile ... is missing" valt terug op het standaardprofiel tot het profiel terug is.
- Geen nieuwe Harmony-patches (42 vóór en na). De oude `SaveData`/`LoadData`-postfixes blijven: `LoadData` als terugval voor de oude keys, `SaveData` omdat niet aantoonbaar is dat geen andere native route (kopiëren binnen de designer) hem gebruikt.
- Logregels FCS en T72 zeggen nu "metric housing UVs (1 UV/m, since 0.4.1)" in plaats van "original UVs ... retained". Warmtemodel, rangefinder, camo en UV's zijn ongewijzigd.
- Nieuwe logregels: `[Thermal] Saved N sight profile(s) in sprocketThermalProfilesV1: <VUID>=<id>` en `[Thermal] Restored N sight profile(s) from sprocketThermalProfilesV1 (M saved row(s)): <VUID>=<id>`; een regel die gelijk is aan de vorige wordt overgeslagen (designer-snapshots).
- Pure tests: codec, remap, samengaan met `sprocketFcsLinksV1`, en een kopie van een echte blueprint (`Object 292`) als fixture. Niet geïnstalleerd; opslaan en laden in de game moet nog bevestigd worden.

## 0.4.1 — 2026-11-14

- Gestreepte en uitgerekte camo op de mod-modellen gerepareerd. De native exterior paint, grime en slijtage volgen UV0; die UV's waren niet metrisch: in 0.3.7 een box-projectie per driehoek, en bij FCS-console, T72-sight en box-sight UV's van de maker met ingeklapte eilanden (dichtheid 0 tot 4 UV/m).
- Nieuwe generator `uv_tools/add_uv.py`, 1 UV per meter zoals de native platen. Driehoeken worden gegroepeerd in vlakke vlakken (±2°). Elk vlak wordt in zijn eigen vlak geprojecteerd met wereldpositie, zodat naden zo veel mogelijk doorlopen. Gladde ketens worden afgewikkeld: cilinders, kegels (lensranden, knoppen) en bijna-vlakke kappen. Elke afwikkeling wordt aan de grenzen getoetst; anders valt hij terug op per vlak.
- Toegepast op de geverfde submeshes van alle negen OBJ's: thermal heads (2), automatic LRF, LRF-concept, optical rangefinder, FCS-console, T72-sight, en na meting ook IR-searchlight en box-sight. Glas, lens en donkere delen houden hun UV's. Geometrie, normalen, aantallen, bounds en submesh-volgorde zijn ongewijzigd.
- `BoxSightObj` verwacht nu 15102 `vt` in plaats van 2060; de andere lezers controleren geen `vt`-aantallen. Geen nieuwe Harmony-patches (42 vóór en na). Warmtemodel, rangefinder en camo-route zijn ongewijzigd.
- Controle: `uv_tools/UV-METRICS-0.4.1.txt` (vóór/na per model) en de dambordrenders in `uv_tools/renders-0.4.1/` (0.3.7 en 0.4.1). Niet geïnstalleerd; de camo in de game moet nog bevestigd worden.

## 0.4.0 — 2026-11-13

- Warmtemodel per voertuig in plaats van één vaste `vehicleHeat` voor alle voertuigen. Bronnen: **motor en uitlaat** (warm als de motor draait, sterker bij hoger toerental/gas, koelt af na afzetten), **loop** (warmt per schot op, koelt in enkele minuten af; hotspots bij kamer, midden en mond) en **wrak** (heet bij uitschakeling, heter als het brandt, in ~10 min af tot net boven de omgeving). Rupsen en wielen niet.
- De romp staat net boven de omgeving (`HullContrast`), zodat hotspots opvallen. Hotspots worden met de sight-camera op de sensorresolutie geprojecteerd en als zachte vlekken opgeteld, alleen op pixels die in het masker voertuig zijn. Geen nieuwe shader of AssetBundle.
- Native gegevens worden alleen gelezen door te pollen (standaard 5 Hz, hoogstens 10 Hz, alleen voertuigen in beeld): `EngineBehaviour.State/RPM/Throttle`, `EngineExhaust`-positie, `WeaponBehaviour.LastFireTime/LoadState`, `LoadPosition`/`ShellSpawnPosition`, bemanningsgezondheid en `VehicleFlags.OnFire`/`FireControlVehicleModule.Intensity`. Geen nieuwe Harmony-patches (42 vóór en na), geen schrijven naar native state. Zie `HEAT-MAP.md`.
- Nieuwe configsectie `[Heat]` met realistische standaarden: schaal per bron, opwarm-/afkoeltijden, omgeving, wrak-start en wrak-afkoeltijd, pollfrequentie, bereik en `InvertSpotY`. Ongeldige waarden: één waarschuwing en alle standaarden. `[Heat] Enabled = false` geeft het beeld van 0.3.7.
- Benaderingen: een hotspot telt alleen als hij aan de kant van de camera ligt (tegen doorschijnen door de romp); wrakwarmte is een vlakke vlek over de voertuigbounds en kan een voertuig dat ervoor staat ook opwarmen; vlekken volgen het voertuig per frame, de looprichting per poll.
- Diagnose achter `Diagnostics.DetailedLogging`, gelimiteerd: per voertuig motor/uitlaat/loop/wrak in °C en het aantal hotspots in beeld, plus schotdetectie.
- Pure tests voor warmtemodel, config-validatie, projectie en masker-optelling; meting van de extra CPU-stap in `PERF-0.4.0.txt`. Niet geïnstalleerd; alle native bronnen moeten nog in de game worden bevestigd.

## 0.3.7 — 2026-11-06

- Alle structurele behuizingen van deze mod gebruiken nu de native exterior paint: IR-searchlight, thermal heads (beide modellen), T72-sight, FCS-console, laser box sight en de drie losse rangefinders. Lens, glas, ramen, scherm, donkere randen en rubber blijven vast.
- Eén gedeelde route in `ExteriorPaint.cs` (factory, cache, `createdPaints`, cleanup); IR-searchlight gebruikt die nu ook. Geen nieuwe Harmony-patches; bestaande `Tick()`/`OnModelLoaded`-routes. Bij ontbrekende vehicle/transform/controller of een native materiaal valt elk onderdeel terug op zijn oude vaste materiaal, zonder exception.
- `thermal-head.obj`, `thermal-head-vertical.obj`, `automatic-lrf.obj`, `lrf-concept.obj` en `optical-rangefinder.obj` hebben box-geprojecteerde UV's (1 eenheid per meter) en face-normalen. `uv_tools/add_uv.py` reproduceert ze; geometrie, driehoekaantallen, bounds en submesh-volgorde zijn ongewijzigd. `fcs-console.obj`, `t72-style-sight.obj`, IR- en box-sight behouden hun bestaande UV's.
- Nieuwe `PaintedObj`-lezer met harde validatie op aantallen, indices, UV-bereik en normalen; de thermal- en rangefinderlezers gebruiken die. Pure tests controleren de lezers, de package-hashes en de lijst van te verven submeshes.
- Fallback en live risico's: box-projectie geeft naden en kan schuine vlakken tot √3 uitrekken; native part-scaling kan de patroondichtheid beïnvloeden. Niet geïnstalleerd; camo, dubbelzijdigheid en save/reload moeten nog in de game worden bevestigd.

## 0.3.6 — 2026-11-04

- Nieuwe `laserBoxSight`: native gunner-sight met scope en ingebouwde handmatige laser, naast de T72-sight. Zelfde part-instellingen en datum 1945.09.03; geen thermal-profiel, config of keybind erbij.
- Het aangeleverde model is ongewijzigd behouden, zonder roof/hatchring/mantlet, met 180 graden Y-rotatie (+Z voor). Vier submeshes: 2700/914/748/672 driehoeken; totaal 5034.
- Alleen paint (kast, klep en lensranden) krijgt native exterior camo. Steel/dark en dubbelzijdig HDRP-glas blijven ongeverfd. Geen fysiek oculair: de scope gebruikt het opgegeven virtuele achterpunt.
- Bestaande modelhaken werken per sight-ID; geen nieuwe Harmony-patches. T72-mesh en materiaalinstellingen blijven gelijk. De gedeelde paint-cleanup bewaart ook eigendom na cache-evictie.
- Herhaalbare OBJ/STL-converter, OpenSCAD-beelden/icoon, pure loader- en pakkettests, install/uninstall met DLL-back-up. Niet geïnstalleerd; native camo/scope/laser moeten nog in de game worden bevestigd.
- Bronafwijking: 24 driehoeken met nul oppervlakte uit samengevallen bronfaces blijven behouden; daarvoor bestaat geen geometrische normaal. Alle niet-degenerate driehoeken worden op winding gecontroleerd.

## 0.3.0 — 2026-11-01

### Sensoren en profielen
- Profielcatalogus v2: `availableFrom` (verplicht, kalenderdatum), `sensorType`
  (`thermal` / `imageIntensifier` / `activeIR`) en voor activeIR
  `beamRangeM`/`beamAngleDeg`. Oude v1-bestanden worden in het geheugen
  gemigreerd (`thermal`, vanaf 1977.01.01, `blackHot` → `palette`).
- Datumgate per profiel: een profiel werkt vanaf
  `max(part-creationDate, availableFrom)`. De laatste era blijft open
  (`TechDate.MaxValue`); er is geen extra scheiding of era-naam in code.
- Nieuwe profielen: Active IR L1 (1950), II Gen 1 (1962), II Gen 2 (1970),
  Thermal Gen 1 (1977), Thermal Gen 1 late (1983), Thermal Gen 2 (1992).
- Rechtsklikmenu toont te late profielen uitgegrijsd met
  "(from yyyy.MM.dd)"; een te late opgeslagen keuze houdt identiteit, maar
  geeft gewoon zicht plus waarschuwing. Massa en kosten blijven tellen.
- `defaultProfileId` = `tiGen1`; nieuwe sights kiezen dat profiel als het op
  de voertuigdatum werkt, anders het nieuwste werkende profiel.

### Beeld
- Image intensifier: luminantie met automatische gain, ruis die met de gain
  stijgt, bloom op heldere pixels en groen palet; geen voertuigmasker.
- Active IR: sterk versterkte scène binnen de kegel van een gezonde IR-
  searchlight, donker met ruis zonder lamp. Bundel is niet zichtbaar voor
  anderen en de AI ziet hem niet (bewust nog niet).
- Nieuwe part `irSearchlight` (GUID `bc3bd5c7-d1a6-4e89-b4e7-175ef19f9842`,
  datum 1950.01.01, 80 kg, assembly 300) met Terra's `ir-searchlight.obj` en
  icoon.
- IR-housing kleurt mee met de native voertuigcamo via de native
  exterior-paint-controller; de lens gebruikt hetzelfde transparante
  coatingglas als de thermal lens, met een donkerrode IR-filtertint.
- OBJ-loader leest `v`/`vt`/`vn` en maakt één vertex per unieke
  (v,vt,vn)-combinatie; normalen uit het bestand, alleen zonder `vn` terug
  naar `RecalculateNormals`.

### Opgelost
- Rangefinder-ballistiek gebruikt de geladen granaat in plaats van
  `InitialProfile` (via reflectie op Expanded Ballistics; ontbrekende assembly
  wordt niet permanent gecacht).
- Mesh- en materiaalcaches worden opgeruimd bij unload; de part-cache wordt
  ook bij F8-reload geleegd.
- Readback-fouten loggen het exceptietype; waarschuwingen worden op een vaste
  sleutel gededupliceerd.
- Null-checks voor `models`/model in de catalogus met een gerichte
  validatiefout.
- Mk3 Black hot gebruikt dezelfde achtergrondwaarden als White hot.

### Tests
- Pure tests voor v1/v2-validatie, profieldatums, defaultkeuze, de open laatste
  era (sentinel 9999.12.31 haalt 1992) en de grens 1976.12.31/1977.01.01.
- `Sensor.Tests` en `ConsoleLink.Tests` groen; native rendering, part-loading
  en mass/cost zijn nog niet in de game gevalideerd.

## 0.3.1 (2026-11-01)
- Active IR without a healthy IR searchlight on the vehicle no longer shows a full-noise image: the normal sight stays and the HUD shows 'ACTIVE IR  no IR searchlight' for 4 s. A searchlight destroyed mid-use ends the IR view the same way.


## 0.3.2 (2026-11-01)
- Replaces 0.3.1 (never installed), at the user's request: Active IR without a healthy IR searchlight shows the green noise image again (as 0.3.0), and the HUD shows 'ACTIVE IR  IR-zoeklicht nodig' for as long as that lasts.


## 0.3.3 (2026-11-02)
- Automatic laser rangefinder: an empty, reloading or just-switched gun no longer suspends the gravity correction when every carried type in the Expanded Ballistics mix is unguided (e.g. `apds_late` + `heat`). A mix with a guided, unknown or unreadable type still fails closed, as before.
- Diagnostics (`Diagnostics.DetailedLogging`, rate-limited): while an automatic zero is active, every 5 s (max 60 lines) `[Rangefinder FCS diag] zero=… aimCalls=… playerLayerCalls=… applied=…; refused: <reason> xN`, naming the condition that refused the correction. Also logged: why a zero is released (manual zeroing, device, disabled), why a zero was not set, and a one-time gun-layer/aiming-mechanism mismatch line.
- Known, not fixed here (Expanded Ballistics): `Cannon.muzzleVelocity` is the design shell's speed, not the chambered round's, so with HEAT loaded in an APDS design the correction uses the APDS speed and HEAT still drops. Manual zeroing (native `SetGunConvergance`) is distance-only convergence and has never compensated for gravity.

## 0.3.4 (2026-11-02)
- Automatic laser rangefinder: the drop correction now uses the muzzle speed of the round really in the chamber (`BallisticsAmmoState.ChamberedMuzzleVelocityMps`, Expanded Ballistics 0.22.17+), so HEAT loaded in an APDS design is lifted for HEAT's speed. It falls back to `Cannon.muzzleVelocity` with older EB (no field), without EB or a mix, with an empty/unconfirmed chamber, or when the value is not finite or below 50 m/s.
- `[Rangefinder FCS]` lines now show `speed=… speedSource=chambered|cannon cannonSpeed=…`; besides the first 8 corrections, up to 16 more lines are logged whenever the speed changes (ammo switch). The 0.3.3 diagnostics are unchanged.

## 0.3.5 (2026-11-02)
- Automatic laser rangefinder: a player gun layer now counts as the linked cannon when its control target is the cannon's own mechanism or lies on the same mount path (ancestor or descendant of the cannon or one of its mechanisms). This accepts the oscillating-turret cradle/`TurretRing` layers of the T-84, where the control target is not equal to `Cannon.HorizontalAimingMechanism`/`VerticalAimingMechanism`. A layer for another cannon or vehicle stays refused.
- The correction is recomputed from the current sight ray and lens for every layer, so two layers on one cannon receive the same fresh world aim point (no compounding).
- Diagnostics (`Diagnostics.DetailedLogging`): one `[Rangefinder FCS diag] topology` line per vehicle with, for every player layer target and both cannon mechanisms, GameObject name, type, VUID, GUID and six parent levels, plus `accepted=yes|no`. The existing lines remain.

