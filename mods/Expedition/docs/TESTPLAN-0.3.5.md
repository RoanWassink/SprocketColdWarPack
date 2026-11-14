# Testplan 0.3.5: vijanden blijven niet stilstaan zonder schot (max 6 stappen)

Voorwaarde: 0.3.5 geïnstalleerd met het installatiescript van de uitgave
en met de hashes van de uitgave; `[Campaign] TestPool = true`; Sprocket via Steam gestart.
Installeer alleen met Sprocket dicht. Dit is één gevecht (bijvoorbeeld testpool-slot 1 of 2).

1. **Gevecht starten.** Load Game → een testpool-slot met vier vijanden → Play. Rijd naar contact
   en laat de vijanden stoppen (clear shot). Verwacht: het gevecht loopt normaal; vijanden die
   blijven staan zonder te schieten gaan na ongeveer 8 s rijden.
2. **Eerste stall.** Wacht bij een stilstaande, niet-schietende vijand op de regel
   `Expedition team AI: enemy slot <n> no firing progress for <s> s (shots=..., loaded=..., has=...,
   error=..., hullAngle=...); repositioning to (<x>, <z>)`. Verwacht: de tank rijdt zijwaarts
   (25–40 m) en iets naar het doel (15–30 m), stopt daarna en schiet weer als de lijn vrij is.
3. **Tweede stall.** Laat dezelfde of een andere vijand nog eens vastlopen. Verwacht: de nieuwe
   `repositioning to`-x ligt aan de andere kant van de tank dan de vorige; niet hetzelfde punt.
4. **Ambusher (optioneel).** Alleen in een normale Latewar-run: Grille 17 en 21 staan sinds 2026-11-02 in
   `test-pool-goedgekeurd`, niet meer in de testpool. Verwacht: bij een stall alleen de regel die eindigt op
   `; holding (ambusher)`, en de tank verplaatst niet.
5. **Bondgenoot.** Kijk na contact naar een AI-bondgenoot die stopt en niet vooruit schiet.
   Verwacht: dezelfde regel met `ally slot <n>` en een flankverplaatsing; een door jou
   overgenomen tank (AI Orders/handmatig) krijgt nooit zo'n regel.
6. **Afsluiten en log.** Laat het gevecht eindigen of Return to Editor, sluit Sprocket normaal af
   en geef Opus `BepInEx\LogOutput.log` (en eventueel `stats\test-pool-results.csv`).

## Wat Opus daarna leest
- Precies één stallregel per stall, met `shots` (schoten sinds het stoporder), `loaded` (j/n),
  `has` (alleen informatief; de Cavalier vuurde met `has=False`), `error` (richtfout hoofdkanon)
  en `hullAngle` (romphoek naar het doel). Bij een Ambusher eindigt de regel op `; holding (ambusher)`.
- Per stall een `repositioning to (x, z)`; de volgende stall van dezelfde tank gebruikt de andere kant.
- In elke `Expedition AI path sample` staat `fireStall=<s>`; tijdens het rijden is die 0, na een
  stall loopt hij op tot de verplaatsing begint.
- Geen `Expedition update failed`, geen nieuwe warning bij het geven van de flankorder, en de
  bestaande `Expedition combat order`-regels blijven ongewijzigd.
- Een tank die laadt (lange herlaadtijd zonder schot) mag niet als stall verschijnen; pas na
  8 geladen seconden zonder schot, richtverbetering of rompdraai volgt een stall.
