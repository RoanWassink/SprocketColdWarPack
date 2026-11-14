# HEAT-MAP: native bronnen voor het warmtemodel (Thermal Sight 0.4.0)

Gelezen (alleen lezen) uit de geïnstalleerde interop (`BepInEx\interop`, game-versie van 13-11-2026). De decompilatie met ilspycmd (`heat-decomp\<assembly>\`, plus een reflectiedump `heat-decomp\TYPEDUMP.txt` van Sprocket.VehicleFramework) is niet meegeleverd in deze uitgave; maak hem opnieuw met ilspycmd op de interop-DLL's als je een verwijzing wilt nalopen. Alle paden hieronder zijn relatief aan `heat-decomp\`.

**Conclusie:** alle drie de gevraagde bronnen (motor en uitlaat, loop na schieten, wrakken) zijn met **pollen van leesbare velden** te krijgen. Er is **geen nieuwe Harmony-patch** en **geen schrijven naar native state** nodig. De stopregel is dus niet geraakt; alles is gebouwd.

## Velden per bron

| Gegeven | Type en pad | Pollen veilig? | Bewijs |
|---|---|---|---|
| Motoronderdeel (positie) | `Sprocket.Vehicles.Engines.CombustionEngine : VehicleComponent` (MonoBehaviour; `transform`, `LocalBounds`) via `Vehicle.ObjectReader.Items[i].Components[j].TryCast<CombustionEngine>()` (zelfde route als bestaande `ConsoleLinks.Components`) | ja, alleen lezen; opzoeken 1× per 5 s per voertuig | `Sprocket.EngineDesigner/Sprocket.Vehicles.Engines/CombustionEngine.cs:24`; `TYPEDUMP.txt:197` (LocalBounds) |
| Motor draait | `CombustionEngine.Behaviour` (`IEngineBehaviour`) → `TryCast<EngineBehaviour>()` → `State` (`EngineState` flags: Off=1, Starting=2, Running=4, ShuttingDown=8); terugval `TryCast<IReadOnlyPowertrainEngine>().State` | ja, property-getter zonder bijwerking | `CombustionEngine.cs:326`; `Sprocket.EngineDesigner/Sprocket.Engines/EngineBehaviour.cs:867`; `Sprocket.Powertrains/Sprocket.Powertrains.Engines/EngineState.cs:10`; `.../IReadOnlyPowertrainEngine.cs:143` |
| Toerental en gas | `EngineBehaviour.RPM`, `IdleRPM`, `MaxRPM`, `Throttle` (float) | ja (Throttle heeft ook een setter; die gebruiken we niet) | `EngineBehaviour.cs:451`, `:535`, `:765`, `:591` |
| (alternatief toerental) | `VehiclePowertrainBehaviourModule.powertrain.EngineRPM` | ja; niet gebruikt (één motor per powertrain) | `Sprocket.Vehicles.Powertrains/.../PowertrainBehaviour.cs:1204`, `VehiclePowertrainBehaviourModule.cs:97` |
| Uitlaat (positie) | `Sprocket.Vehicles.Exhausts.EngineExhaust : VehicleComponent`; wereldpositie = `OutputParent.TransformPoint(OutputLocalPosition)` | ja, alleen lezen | `Sprocket.Exhausts/Sprocket.Vehicles.Exhausts/EngineExhaust.cs:11`, `:104`, `:119` |
| Schot van een kanon | `Cannon.Behaviour` (`CannonBehaviour : WeaponBehaviour`) → `LastFireTime` (float-veld, verandert per schot) en `LoadState` (Loaded=0, Loading=1, Unloaded=2, NeverLoaded=3). Een schot = `LastFireTime` veranderd **of** Loaded → Loading/Unloaded sinds de vorige poll (één telling per poll) | ja, veld- en property-lezen | `Sprocket.Vehicles.Cannons/.../Cannon.cs:477`; `Sprocket.WeaponFramework/Sprocket.WeaponFramework/WeaponBehaviour.cs:530`, `:871` |
| Looprichting en -lengte | `WeaponBehaviour.LoadPosition` (kamer/staartstuk, wereld) en `ShellSpawnPosition` (mond, wereld); lengte = afstand; kaliber `CannonBehaviour.Caliber` (mm) | ja (Vector3-getters) | `WeaponBehaviour.cs:705`, `:690`; `.../CannonBehaviour.cs:621` |
| Voertuig uitgeschakeld (wrak) | `Vehicle.behaviour` (`VehicleBehaviour`) → `modules` → `VehicleCrewWorkspaceModule.operators[i].internalOperator.HealthFraction`; wrak = alle bemanning leesbaar en ≤ 0. Zelfde route als Expedition (live bewezen, `CustomBattleHostSpike.cs:982`) | ja, alleen lezen | `Sprocket.Vehicles/Sprocket.Vehicles/Vehicle.cs:849`; `TYPEDUMP.txt:37`; `Sprocket.VehicleCrew/.../VehicleCrewWorkspaceModule.cs:90`; `.../InjectedOperator.cs:34`; `TYPEDUMP.txt:206` |
| Brand | `VehicleBehaviour.Flags & VehicleFlags.OnFire (64)`, sterkte `FireControlVehicleModule.Intensity` (0..1) | ja | `TYPEDUMP.txt:60`, `:202`; `Sprocket.Vehicles/Sprocket.Vehicles.Fires/FireControlVehicleModule.cs:247`, `:275` |
| Voertuigpositie/-maat | `VehicleBehaviour.Rigidbody.transform` (lokale ruimte van de hotspots), `VehicleBehaviour.LocalBounds` (wrakvlek) | ja | `TYPEDUMP.txt:59`, `:67` |

## Wat niet gebruikt is, en waarom

- **`CannonBehaviour.FireInternal`-hook** (zoals de I/O-mod doet): dat is een nieuwe Harmony-patch op een vaak lopend pad. Niet nodig: `LastFireTime`/`LoadState` pollen geeft hetzelfde signaal. Nadeel van pollen: twee schoten binnen één poll (≤ 0,1 s) tellen als één; voor tankkanonnen is dat geen probleem, voor automatische kanonnen een onderschatting.
- **`Vehicle.Released` / `BehaviourEnabled` als "vernietigd"**: geen bewezen betekenis voor uitschakeling; de bestaande maskerlijst slaat zulke voertuigen al over. Er is volgens eerder onderzoek (Expedition `player-death-research`) geen universele `Destroyed`-eigenschap; dode bemanning is de bewezen route.
- **Per-voertuig maskerwaarde**: mogelijk zonder native state te schrijven door per voertuig een **eigen** materiaalinstantie (eigen kleur) aan `cmd.DrawRenderer` te geven, in plaats van één override-materiaal. Dat raakt geen risico uit de lijst (geen patch, geen native materiaal, geen MaterialPropertyBlock op native renderers), maar vraagt een kleur-/alfa-codering van het masker en meer draws. **Niet gebouwd** (buiten de scope van 0.4.0); de wrakwarmte gebruikt nu een vlakke vlek over de voertuigbounds.

## Risico-inschatting

- Alle reads gebeuren in `LateUpdate` (Runtime.Tick) en tijdens de bestaande custom-pass-capture, op de hoofdthread, met try/catch per voertuig; een fout schakelt alleen het warmtemodel voor dat voertuig/die poll uit en logt één waarschuwing.
- Geen allocaties per frame: wrappers worden bij het opzoeken (1× per 5 s per voertuig) gecachet; per poll alleen value-type getters. Opzoeken alloceert wel kleine wrappers.
- Niet live bewezen (eerste test in de game): dat `LastFireTime` per schot verandert, dat `EngineBehaviour.State` in de strijd `Running` meldt, en de oriëntatie van de projectie op het sensorbeeld (escape: `[Heat] InvertSpotY`).
