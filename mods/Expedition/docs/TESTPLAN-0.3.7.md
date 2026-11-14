# Testplan 0.3.7: Dangerous-only ontwerpen (max 6 stappen)

Voorwaarde: Sprocket gesloten; installeer met
het installatiescript van de uitgave en de hashes van de uitgave. Start daarna een normale (geen testmodus) run.

1. **Catalogus en log.** Open een nieuwe run. Verwacht in het log één regel
   `Expedition dangerous-only designs: <era>=<n>; ...` met een telling per era;
   zonder de nieuwe map staat er overal 0 en is de catalogusversie gelijk aan
   0.3.6.
2. **Kaarten scheiden.** Met de acht HGS 105-ontwerpen in de dangerous-map: geen
   HGS in een Cautious- of Standard-kaart (samenstelling, prijs, dreigingsdoel).
   Een Dangerous-kaart mag er wel een bevatten; dan verschijnt
   `Expedition dangerous-only: round <r> uses '<naam>'`.
3. **Boss, beloning, starter.** De vierde kaart (boss) en de era-boss-preview
   tonen nooit een HGS; Lend Lease/Captured Prototype nooit een HGS; de starter
   in de editor is nooit een HGS. Een Dangerous-run met HGS levert geen
   veteraan-archief met een HGS op.
4. **Dubbele map.** Zet hetzelfde markerbestand in `community-blueprints` en
   `community-blueprints-dangerous`. Verwacht één warning
   `... Dangerous wins ...`, één catalogusvermelding en geen dubbele catalogus.
   Verwijder daarna de dangerous-map: alles werkt als vóór 0.3.7.
5. **Saves.** Sluit de nieuwe run uit stap 1 af naar het hoofdmenu en laad hem opnieuw (archief mét `MinimumRisk`):
   hij laadt zonder fout en toont dezelfde kaarten. Laad daarna slot 1 en slot 2. Beide komen in hun eigen ronde/cursor
   terug, met byte-gelijke offers en archief; geen ontbrekend-ontwerp- of
   herstelmelding. Een oude catalogus zonder `MinimumRisk` houdt na lezen en
   schrijven dezelfde bytes en hash.
6. **Regressie.** `tools\Verify-UiKit.ps1` slaagt; menu en HUD zijn onveranderd.
   Bevestig dat `CampaignEnemyHunt.PlayerCommand` (AI Orders) en het
   defeat-scherm van 0.3.6 zich gedragen als voorheen.
