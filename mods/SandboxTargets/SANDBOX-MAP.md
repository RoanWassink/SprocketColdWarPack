# Sandbox/flat-map: brononderzoek (0.2.56.2 Experimental)

- GameAssembly SHA256: `3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B`.
- Gelezen: actuele BepInEx/interop, Cpp2IL-metadata, native GameAssembly en scene-assets.
- `BuildSettings` geeft `Assets/Scenes/Game Scenes/Sandbox (Low performance).unity`, index **21**.
- Dit is de flat-map-sandbox met eigen-tankkopie; `Sandbox.unity` (17) bevat vaste 150/300m-doelen.
- `level21`: GameObject `Vehicle Spawner` heeft VehicleSource, VehicleSpawner en StageVehicleSpawnEvent.
- Source: `SpawnMode.EditorExitSave` (=3), lege BlueprintName, Source=1 (gebruikersdata).
- Event: enabled, spawnLimit=16, useBudget=false, delay=0, eigen SpawnLocator.
- Spawner: DefaultEnableBehaviourOptions=1 (FlattenTransformHeirarchy), team=0, group=0.
- De aparte `Selection Spawner` heeft een disabled StageVehicleSpawnEvent; die wordt genegeerd.

## Stock route en argumenten

- `Task<VehicleSpawn[]> StageVehicleSpawnEvent.SpawnAsync(CancellationToken ct)`.
- Native state machine `StageVehicleSpawnEvent.<SpawnAsync>d__21.MoveNext()` kiest de blueprint via VehicleSource.Mode.
- `EditorExitSave` kiest dezelfde tijdelijke editor-exit-blueprint; `GetBlueprint()` biedt dezelfde keuze.
- `IVehicleBlueprint VehicleSource.GetBlueprint()` en `string GetDesignerPath()` zijn beschikbaar.
- `VehicleSpawnInstance[] StageVehicleSpawnEvent.GetSpawnInstances()` leest de SpawnLocator.
- Instance bevat `Vector3 position`, `Quaternion rotation`, `TeamID teamIdOverride`, `GroupID groupIdOverride`.
- `Task<VehicleSpawn[]> VehicleSpawner.Spawn(IVehicleBlueprint, VehicleSpawnInstance[], VehicleSpawnOptions, CancellationToken)`.
- Er bestaat ook een overload met een struct-by-value instance; die wordt niet gepatcht of gebruikt.
- Stock event gebruikt `VehicleSpawnOptions.AppendNameID` (=1), zijn eigen spawner en ct.
- Spawner bouwt via IVehicleFactory, zet positie/rotatie, en EnableBehaviour met eigen resources/options.
- Het event registreert de voltooide `VehicleSpawn.Vehicle` via `IVehicleRegister.Register(...)`.
- De mod hergebruikt dit alles: extra instances in de lijst van het stock event, dus dezelfde spawner,
  defaults, team/group en registratie; geen AI-orders of crewwijzigingen.
- De normale sandbox-map spawnt zijn IS-3 via een eigen event met `VehicleSource` in `Explicit`-modus en een
  blueprintnaam; de flat map heeft alleen het `EditorExitSave`-event (zie `CustomSpawners.cs`).

## Hookpunten en levenscyclus (0.3.2)

- Postfix op de parameterloze `StageVehicleSpawnEvent.GetSpawnInstances()` (`NativeTargets.cs`).
  Alleen voor het stock event: `EditorExitSave` in exact de scene hierboven (`NativeTargets.IsStock`).
- Daar wordt de config opnieuw gelezen, de speler-tank opgezocht (`GameController` > `ScenarioGameMode` >
  `sharedState.PlayerVehicle`), de grond onder elk doel bepaald en de positie berekend.
- Doelen met eigen ontwerp: extra `VehicleSpawnInstance`'s achter de stock instance in de teruggegeven array
  (kopie van de stock instance, dus team/group blijven; alleen positie en rotatie zijn anders).
- Doelen met een blueprint (`CustomSpawners.cs`): per blueprintnaam een kloon van het spawn-GameObject
  (`SandboxTargets Spawner N`). De kloon staat uitgeschakeld (Unity draait zijn Start niet) en krijgt
  `blueprintSource.Mode = Explicit`. `Resolve` laat `VehicleSource.GetPath()` het pad bepalen en bewaart de eerste
  instelling die op precies het bestand uit de config wijst. `StageStart(token)` start de kloon; de postfix op diens
  `GetSpawnInstances` geeft de voorbereide array terug (`TryTake`). Lukt dat niet (bestand ontbreekt, native pad wijkt af,
  fout): waarschuwing en een eigen tank op de geplande plek.
- Opruimen: postfix op de parameterloze `StageReset()` van het stock event en prefix op de parameterloze
  `MissionScenarioGameState.Exit()` annuleren het token en resetten de klonen.
- Geen hooks op struct-by-value-parameters; alle drie de gepatchte methoden zijn parameterloos
  (`tests\Check-Interop.ps1` controleert de signaturen en dat er drie patchklassen zijn).
- Config en spelversie: `Plugin.Load` vergelijkt de GameAssembly-hash en doet bij een afwijking niets.

## Geometrie

- Middelpunt van de speler = `VehicleBehaviour.transform.TransformPoint(LocalBounds.center)`; de afstand van de
  root naar dat midden wordt voor eigen-ontwerpdoelen meegedraaid, zodat de middens op `distance` staan.
- Horizontale forward=sqrt(distance^2-lateral^2); yaw=atan2(playerX-targetX,playerZ-targetZ)+facing.
- Grondraycast (`RaycastAll` van 2000 m hoogte) sluit colliders van voertuigen en hellingen steiler dan 60 graden uit;
  de hoogte-offset (root boven grond) wordt van de stock kopie gemeten.
- Vaste offsets kunnen bij brede of lange tanks silhouetten laten overlappen; vergroot dan `lateral`.
- Wat alleen in het spel te controleren is staat in de testlijst van `README-NL.md`.
- Ruwe onderzoeksbestanden (decompilatie, dumps van scenes en assets) horen niet bij deze uitgave.
