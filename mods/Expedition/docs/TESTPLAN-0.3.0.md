# Expedition 0.3.0 — in-game testplan (nog niet uitgevoerd)

Voor de eigenaar na een aparte installatiebeslissing; deze workspace is niet geïnstalleerd.
Gebruik kopieën van slots en zet Diagnostics.DetailedLogging aan. Verwachte logs staan hieronder.
Managed uitslagen: `../../artifacts/fase0/`, `../../artifacts/fase1/`, `../../artifacts/compat/`.

1. Open een bestaande schema-5-run en herlaad: dezelfde kaarten/hashes, oorspronkelijke verlies/herstelregels; geen schemawijziging in checkpoint.
2. Start een nieuwe normale run: schema 6, IRONMAN OFF; controleer alle beschikbare start-era's en vier fights per era.
3. Open Difficulty: Recruit/Regular/Veteran/Ace tonen 1/1.25/1.5/2 vijandbudget en 0.75/1/1.25/1.6 score; bij acht era's eindprogressie +35%.
4. Controleer eerste offer-scherm: `Era boss: naam (prijs)`; vergelijk later met de daadwerkelijke boss en gepinde prijs.
5. Controleer elke offerkaart: `Enemy force: N.N× your budget` = echte som prijs × aantal / beschikbaar budget, ook boven 1×.
6. Activeer EliteWorkshop na offergeneratie: budget en editor-quote wijzigen direct; alle offer-id's en planhashes blijven exact gelijk.
7. Vervang en discard actieve items; cash War Bonds en herlaad. Dezelfde kaarten blijven geldig; geen Blocked/Error-view.
8. Activeer Contract: huidige vijandtarget onveranderd; na winst volgende ronde ×1.2. Controleer gepinde economy, niet alleen afgeronde UI-getallen.
9. Activeer Operational Freedom in een normale ronde: nieuwe kaarten/roll; in bossronde geweigerd, inventaris behouden.
10. Breek voorbereiding af: resultaatscherm heeft RETRY; dezelfde offer/hashes, hogere attempt generation en geen itemverloop of supply/loot.
11. Veroorzaak een technische startfout met een testkopie: RETRY behoudt kaarten; oude callback mag geen winst geven. Let op `combat-started persistence failed` en `Start/load`-fouten.
12. Win drie normale fights: doelen per era gebruiken 0.70/0.90/1.10; vierde is één bosskaart met target basis ×1.15×1.35×difficulty×progressie (en actief Contract).
13. Win met alle friendlies intact: SCORE-tegel en details tonen flawless +25%; vergelijk score_gained in CSV en decimale checkpointscore.
14. Win na één friendly-verlies: geen flawless; verlies van alleen de bestuurde tank met overlevende allies beëindigt het gevecht niet. Vergelijk HUD-roster en CSV-metrics.
15. Verlies normaal: RETRY en CHOOSE ANOTHER BATTLE; Attempt N-tekst; dezelfde kaarten/selectie, itemtimers en economie. Geen loot/funds/supply.
16. Herlaad na verlies, RETRY naar editor, wijzig tank en Play: nieuwe generation, dezelfde vijand; alternatief kiezen werkt ook. Score ×0.75 per mislukte poging, bodem ×0.25.
17. Win daarna: RoundAttempts wordt 0; Defeats/Attempts blijven cumulatief; volgende ronde krijgt weer volledige scorefactor.
18. Return to Editor vóór contact: gratis, geen RoundAttempts/Defeats; wel één abortregel. Native Resume sluit pauze. Let op `Expedition pause: return to editor requested; contact=False`.
19. Return na bevestigde tankdood én afzonderlijk na 60 s zonder doden: label `counts as retry`; één Retreat-regel, één penalty, vaste kaarten. Log `contact=True` en juiste generation.
20. Start Ironman ON en herlaad vóór de fight: mode blijft ON. Vóór contact gratis editor-retour; ná contact disabled met reden, ook bij opnieuw openen pauze.
21. Verlies Ironman: Lost, geen RETRY/herstel, record met Ironman true, index leeg; score van eerdere wins ×1.3. Geen EmergencyRecovery in normale of bossloot.
22. Win boss: SCORE eerst, CONTINUE naar drie lootkeuzes, daarna volgende era met nieuwe boss-preview; controleer laatste finite victory → loot → endless/Finish en geen dubbele beloning.
23. RETIRE RUN op build én offers: CANCEL bewaart alles; bevestigen schrijft Retired + score/verste era/mode/tellers, wist active index en kan niet worden hervat. Controleer records en slot-summary.
24. Controleer `BepInEx/config/SprocketExpedition/stats/battles.csv`: één rij per afgeronde generation; juiste quoting/decimaalpunt/score_gained. Onleesbare roster geeft lege metrics, nooit verzonnen kills. Test onschrijfbaar pad en oude header met een kopie: flow gaat door, warning `Expedition battle log`, origineel blijft bij fout intact.
25. Herhaal pause/retour/Play, victory/defeat/Retry en slots; geen dubbele callbacks, cursor/input/crash of afwijkende vanilla/andere-mod-flow. Verwacht `[Expedition menu]`, `[Expedition HUD]`, `Expedition definitive victory roster`, native `outcome: VICTORY`/loss; fouten en late-era contentuitputting blijven open native risico's.
