# Changelog — Sprocket Thermal Sight

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

