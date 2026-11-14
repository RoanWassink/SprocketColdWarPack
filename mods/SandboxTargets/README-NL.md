# Sprocket Sandbox Targets 0.3.2

Extra schietdoelen in de stock **flat-map-sandbox** (`Sandbox (Low performance)`).
Standaard: kopieen van je eigen tank op 150, 250, 350 en 500 meter, verspreid links/rechts
en met de rompfronten naar je startpositie. Per doel kun je ook een andere blueprint kiezen.
De stock kopie blijft staan. Alleen GameAssembly-hash `3E580828...25FC17B`
(0.2.56.2 Experimental) wordt geaccepteerd; bij een andere versie schakelt de mod zichzelf uit
en zet een waarschuwing in het BepInEx-log.

## Hoe het werkt

De flat-map-sandbox laat zijn eigen spawn-gebeurtenis (`StageVehicleSpawnEvent`) een kopie
van je ontwerp neerzetten. De mod voegt achteraf extra posities toe aan de lijst die die
gebeurtenis teruggeeft (`GetSpawnInstances`, een methode zonder parameters), zodat het spel de
doelen zelf bouwt, registreert, activeert en bij een reset weer opruimt. Doelen met een eigen
blueprint gaan via gekloonde spawn-gebeurtenissen, op dezelfde manier als waarop de gewone
sandbox-map zijn vaste doel laat verschijnen. Er worden geen eigen rij-, richt- of
schietopdrachten gegeven. Details en bewijsgrenzen: `SANDBOX-MAP.md`.

## Config

De mod maakt `BepInEx\config\sprocket.sandboxtargets.json` bij de eerste start, met de
standaardwaarden. Een bestaand bestand wordt nooit overschreven. De JSON wordt bij elke nieuwe
sandbox/Play-sessie opnieuw gelezen. Kapotte JSON of ongeldige veldwaarden geven een waarschuwing
en standaardwaarden; het bestand blijft intact. Het voorbeeld (gelijk aan de standaardwaarden)
staat in `config\sprocket.sandboxtargets.example.json`.

- `enabled`: true/false; false schakelt nieuwe doelen uit.
- `targets`: lijst (ook leeg toegestaan, maximaal 64).
- `distance`: horizontale rechte afstand tussen tankmiddens, in meter (0 < d <= 10000).
- `lateral`: meter langs de rechteras van je romp bij het spawnen; negatief=links,
  positief=rechts; abs(lateral) moet kleiner zijn dan distance.
- `blueprint`: null voor een kopie van je eigen tank; `"Default/PZ4"` voor factie/naam;
  `"PZ4"` zoekt binnen de actieve factie. Het bestand moet staan op
  `Documents\My Games\Sprocket\Factions\<factie>\Blueprints\Vehicles\<naam>.blueprint`.
  Ontbrekend of niet native te vinden blueprint: waarschuwing en eigen tank als terugval.
- `facing`: `"towards"`, `"side"` (90 graden rechtsom; linkerzijde naar speler),
  of een getal in graden rechtsom ten opzichte van frontaal.

Het middelpunt van je eigen tank is het midden van de native bounding box. Voor doelen met je
eigen ontwerp geldt hetzelfde (de afstand tussen het midden van de speler en het midden van het doel
klopt); voor een andere blueprint is de bounding box vooraf onbekend, dus daar staat het
voertuigpunt (de root) op de berekende plek. Afstanden gelden vanaf de startpositie bij het
spawnen: rij je zelf weg, dan verandert je actuele afstand. De voorgeschreven offsets garanderen
bij zeer brede of lange tanks geen vrij silhouet; vergroot dan `lateral` binnen de afstand.

## Bouwen en installeren

`.\Build-SandboxTargets.ps1 -GameRoot <map met BepInEx\core en BepInEx\interop>` bouwt de DLL
(`src\bin\Release\net6.0\SprocketSandboxTargets.dll`); het script schrijft niets in de spelmap en
installeert niets. `.\tests\Check-SandboxTargets.ps1` controleert de productieparser, de geometrie, het
blueprintpad en het voorbeeldconfigbestand; testbestanden en logs komen buiten de bron
(parameter `-WorkDir`). `.\tests\Check-Interop.ps1 -GameRoot <spelmap>` leest de interop-DLL's van
het spel en controleert de signaturen waar de mod op hookt en de gebouwde DLL (alleen lezen).

Installeren: sluit het spel en plaats de DLL in `BepInEx\plugins\SprocketSandboxTargets\`
(met `dist\install-all.ps1`, of met de hand). De config blijft staan; de mod maakt hem alleen aan als hij ontbreekt.

## Testlijst in het spel

1. Open flat-map-sandbox, ontwerp laden, Play: stock kopie plus vier extra doelen;
   log bevat config, stock-status en per doel positie, afstand en yaw (prefix `[SandboxTargets]`).
2. Blijf op het startpunt: controleer 150/250/350/500 m in de log,
   links/rechts -15/+15/-30/+30; rompfront en neutrale koepel; geen beweging/vuur.
3. Controleer grondcontact, vering, penetratie en afstand na enkele seconden en na een inslag.
4. Editor, Play, retry en scene herladen: precies een nieuwe set; vorige set weg.
5. Wijzig JSON tussen twee sessies: afstanden, enabled=false, side en numerieke facing.
6. Test geldig faction/naam, ontbrekend blueprint en kapotte JSON; geen overschrijving/foutspawn.
7. Gewone Sandbox, andere maps, custom battles en editor: geen extra doelen of wijzigingen.
8. Stop/return-to-editor tijdens laden: geen laat opduikende doelen of native foutmeldingen.
