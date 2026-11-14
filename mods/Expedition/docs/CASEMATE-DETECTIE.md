# Casemate-detectie uit v0.2-blueprints

De Ambusher-rol wordt per vijandontwerp bepaald uit de blueprint-bytes die de
campagne al pinned. Er wordt niets extra in de game geladen en er komen geen
nieuwe terreinscans bij.

## Opbouw van de v0.2-blueprint

- `header.v` is `0.2`. De objectboom staat in `objects`; ieder object heeft een
  `vuid` en de ouder staat in `pvuid`. `pvuid = 0` is de romp/root.
- Componentdefinities staan in `blueprints` met een numeriek `id` en een `type`.
  Velden als `cannonBlueprintVuid`, `layingDriveBlueprintVuid` en
  `ringBlueprintVuid` verwijzen naar zo'n `id`.
- Een kanonobject heeft `cannon` (instantie) en `cannonBlueprintVuid` (definitie).
  De definitie van type `cannon` levert `caliber`.
- De kanonketen loopt via `pvuid` omhoog: kanon -> plateStructure/trunnions ->
  `mantlet`. Een `turretRing`-object verder omhoog betekent dat de mount in een
  draaiende toren hangt.
- Het `mantlet`-object heeft `mantletBlueprint.drive_vuid`. Dat nummer is gelijk
  aan het `layingDrive`-veld van het bijbehorende object; dat object verwijst met
  `layingDriveBlueprintVuid` naar de definitie met `azimuth.min` en `azimuth.max`.
- Een `turretRing`-object verwijst met `ringBlueprintVuid` naar de ringdefinitie.
  De bijbehorende `turret`-definitie levert `minRot`/`maxRot`; die bepaalt of de
  ring werkelijk draait.

## Regel

1. Kies het hoofdkanon met het grootste `caliber`. Bij gelijk kaliber moeten alle
   kandidaten dezelfde mantlet/ring-combinatie hebben; anders is het ontwerp
   ambigue en dus géén casemate.
2. Lees de azimuthboog van de laying drive. Ontbreekt die, dan is het ontwerp
   geen casemate.
3. Geen `turretRing` tussen kanon en romp: casemate.
4. Wel een ring, maar geen eenduidige `turret`-definitie of een turret die niet
   draait: casemate (de ring is dan geen werkende traverse).
5. Anders is de ketenboog `driveBoog + turretBoog`. Kleiner dan 60°: casemate.
   60° of meer: geen casemate.

De drempel is een heuristic, geen claim over elk historisch voertuig. Onleesbare
of tegenstrijdige bytes worden nooit als casemate behandeld.

## Uitslag op de vijf voorbeelden

| Ontwerp | Hoofdkaliber | Laying-drive azimuth | Turretring | Uitkomst |
|---|---:|---:|---|---|
| FV-90 | 160 | -6..6 (12°) | geen | casemate |
| Grille 17 | 170 | -5..5 (10°) | geen | casemate |
| Grille 21 | 210 | -5..5 (10°) | geen | casemate |
| Flakpanzer IV Wirbelwind | 24 (vier gelijke kanonnen, één mount) | 0..0 | 360° | geen casemate |
| AMX-13-90 | 90 | 0..0 | 360° | geen casemate |

De managed test leest deze vijf bestanden uit de testpool en controleert zowel de
detectie als de bron `detected`.

## Config-overrides

- `[Combat] AmbusherDesigns = Naam1; Naam2` forceert de rol, ook zonder leesbare
  bytes.
- `[Combat] NotAmbusherDesigns = Naam1; Naam2` sluit de rol uit.
- `NotAmbusherDesigns` wint als een naam in beide lijsten staat. Namen worden
  getrimd en hoofdletterongevoelig vergeleken.
- Een override levert `source=config` in de ambusher-log; detectie levert
  `source=detected`.
